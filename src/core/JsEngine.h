// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  JsEngine.h : Extensibilidad JavaScript / JSLib (spec §3.3).
//  Motor QJSEngine (Qt5Qml) con módulos .js del usuario y una librería
//  "jslib" que expone:
//    - Sockets TCP persistentes        (tcpConnect/tcpSend/onTcpMessage/…)
//    - WebSockets persistentes         (wsConnect/wsSend/onWsMessage/…)
//    - HTTP GET con callback           (httpGet)
//    - Temporizadores                  (setTimeout/clearTimeout)
//    - Registro y avisos               (log/notify)
//    - Suscripción a eventos de triggers (onEvent)
//  Los módulos se cargan desde <datos>/modules/*.js y pueden recargarse.
//  Diseño: TODO en el hilo principal (los sockets Qt requieren event loop
//  del hilo GUI); los callbacks JS se invocan desde las señales C++.
// ============================================================================
#ifndef LUMINA_JSENGINE_H
#define LUMINA_JSENGINE_H

#include <QObject>
#include <QJSEngine>
#include <QJSValue>
#include <QVariantMap>
#include <QStringList>
#include <QDir>
#include <QFile>
#include <QTimer>
#include <QTcpSocket>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QWebSocket>

// ---------------------------------------------------------------------------
// Host "jslib": QObject expuesto al motor JS con Q_INVOKABLEs.
// Padre = JsEngine => ciclo de vida gestionado por C++ (RAII).
// ---------------------------------------------------------------------------
class JsLibHost : public QObject
{
    Q_OBJECT
public:
    explicit JsLibHost(QObject *parent) : QObject(parent) {}

    QJSEngine *engine = nullptr;          // asignado por JsEngine tras crear
    QStringList recentLog;                // ring para la GUI (últimas 60 líneas)
    int maxLogLines = 60;

    void pushLog(const QString &line)
    {
        recentLog.append(line);
        while (recentLog.size() > maxLogLines)
            recentLog.removeAt(0);
        emit logChanged();
    }

    // ------------------------- Invocables desde JS --------------------------
    Q_INVOKABLE void log(const QString &msg)
    {
        const QString line = QStringLiteral("[js] %1").arg(msg);
        qInfo() << line;
        pushLog(line);
    }

    Q_INVOKABLE void notify(const QString &title, const QString &text)
    {
        const QString line = QStringLiteral("[js:notify] %1 — %2").arg(title, text);
        qInfo() << line;
        pushLog(line);
        emit jsNotify(title, text);
    }

    Q_INVOKABLE void httpGet(const QString &url, const QJSValue &callback)
    {
        QUrl u(url);
        if (!u.isValid() || u.scheme() != QStringLiteral("https") &&
                            u.scheme() != QStringLiteral("http")) {
            pushLog(QStringLiteral("[js:warn] httpGet: URL inválida: %1").arg(url));
            return;                         // (sin callback: contrato documentado)
        }
        QNetworkRequest req(u);
        req.setTransferTimeout(15000);      // qt-cpp-review ERR: timeout obligatorio
        QNetworkReply *rep = m_nam.get(req);
        connect(rep, &QNetworkReply::finished, this, [this, rep, callback]() {
            rep->deleteLater();
            // NOTA: los métodos de QJSValue son no-const en Qt 5 -> copia local
            QJSValue cb = callback;
            if (!engine || cb.isNull() || !cb.isCallable()) return;
            const int status = rep->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
            const QString body = QString::fromUtf8(rep->readAll());
            QJSValueList args;
            args << QJSValue(status) << engine->toScriptValue(body);
            const QJSValue r = cb.call(args);
            if (r.isError())
                pushLog(QStringLiteral("[js:err] callback httpGet: %1").arg(r.toString()));
        });
    }

    Q_INVOKABLE bool tcpConnect(const QString &id, const QString &host, int port)
    {
        if (id.isEmpty() || port <= 0 || port > 65535) return false;
        if (m_tcp.contains(id)) closeSocketInternal(m_tcp, id);
        auto *sock = new QTcpSocket(this);
        m_tcp.insert(id, sock);
        attachTcpSocket(id, sock);          // readyRead/error -> callbacks JS
        sock->connectToHost(host, quint16(port));
        return true;
    }

    Q_INVOKABLE bool tcpSend(const QString &id, const QString &text)
    {
        QTcpSocket *sock = m_tcp.value(id, nullptr);
        if (!sock || sock->state() != QAbstractSocket::ConnectedState) return false;
        sock->write(text.toUtf8());
        return true;
    }

    Q_INVOKABLE void tcpClose(const QString &id) { closeSocketInternal(m_tcp, id); }

    Q_INVOKABLE void onTcpMessage(const QString &id, const QJSValue &callback)
    {
        if (!id.isEmpty()) m_tcpCb[id] = callback;
    }

    Q_INVOKABLE bool wsConnect(const QString &id, const QString &url)
    {
        if (id.isEmpty()) return false;
        if (m_ws.contains(id)) closeSocketInternal(m_ws, id);
        auto *sock = new QWebSocket(QString(), QWebSocketProtocol::VersionLatest, this);
        m_ws.insert(id, sock);
        // v1.5.0 (spec §3.3): sockets persistentes para integraciones JS.
        connect(sock, &QWebSocket::textMessageReceived, this,
                [this, id, sock](const QString &msg) {
                    deliverToCallback(m_wsCb.value(id), msg, QStringLiteral("ws:%1").arg(sock->request().url().host()));
                });
        // QWebSocket::errorOccurred solo existe en Qt 6; en 5.15 la señal
        // nueva es la deprecated error() -> silencio puntual y documentado.
        QT_WARNING_PUSH
        QT_WARNING_DISABLE_DEPRECATED
        connect(sock, QOverload<QAbstractSocket::SocketError>::of(&QWebSocket::error),
                this, [this, id](QAbstractSocket::SocketError e) {
                    pushLog(QStringLiteral("[js:warn] ws %1 error %2").arg(id, QString::number(int(e))));
                });
        QT_WARNING_POP
        sock->open(QUrl(url));
        return true;
    }

    Q_INVOKABLE bool wsSend(const QString &id, const QString &text)
    {
        QWebSocket *sock = m_ws.value(id, nullptr);
        if (!sock || sock->state() != QAbstractSocket::ConnectedState) return false;
        sock->sendTextMessage(text);
        return true;
    }

    Q_INVOKABLE void wsClose(const QString &id) { closeSocketInternal(m_ws, id); }

    Q_INVOKABLE void onWsMessage(const QString &id, const QJSValue &callback)
    {
        if (!id.isEmpty()) m_wsCb[id] = callback;
    }

    Q_INVOKABLE void onEvent(const QString &name, const QJSValue &callback)
    {
        if (name.isEmpty() || !callback.isCallable()) return;
        m_eventCb[name].append(callback);
    }

    Q_INVOKABLE int setTimeout(int ms, const QJSValue &callback)
    {
        if (ms < 0 || !callback.isCallable()) return -1;    // ERR: validar negativos
        auto *t = new QTimer(this);
        t->setSingleShot(true);
        const int id = ++m_timerId;
        m_timers.insert(id, qMakePair(t, callback));
        connect(t, &QTimer::timeout, this, [this, id]() {
            auto entry = m_timers.take(id);          // no-const: QJSValue no-const
            QTimer *timer = entry.first;
            if (timer) timer->deleteLater();
            QJSValue cb = entry.second;
            if (engine && cb.isCallable()) {
                const QJSValue r = cb.call();
                if (r.isError())
                    pushLog(QStringLiteral("[js:err] setTimeout: %1").arg(r.toString()));
            }
        });
        t->start(ms);
        return id;
    }

    Q_INVOKABLE void clearTimeout(int id)
    {
        const auto entry = m_timers.take(id);
        if (entry.first) entry.first->deleteLater();
    }

    // --------------------- Llamadas desde C++ (eventos) ---------------------
    void fireEvent(const QString &name, const QVariantMap &data)
    {
        const QVector<QJSValue> cbs = m_eventCb.value(name);
        for (QJSValue cb : cbs) {                   // copia: QJSValue no-const
            if (!engine || !cb.isCallable()) continue;
            QJSValueList args;
            args << engine->toScriptValue(QVariant(data));
            const QJSValue r = cb.call(args);
            if (r.isError())
                pushLog(QStringLiteral("[js:err] onEvent %1: %2").arg(name, r.toString()));
        }
    }

    // Llama una función global del motor (acción de trigger tipo "script").
    bool callGlobalFunction(const QString &functionName, const QVariantMap &data)
    {
        if (!engine || functionName.isEmpty()) return false;
        QJSValue fn = engine->globalObject().property(functionName);   // no-const
        if (!fn.isCallable()) {
            pushLog(QStringLiteral("[js:warn] función no encontrada: %1").arg(functionName));
            return false;
        }
        QJSValueList args;
        args << engine->toScriptValue(QVariant(data));
        const QJSValue r = fn.call(args);
        if (r.isError()) {
            pushLog(QStringLiteral("[js:err] %1: %2").arg(functionName, r.toString()));
            return false;
        }
        return true;
    }

    bool hasEventHandlers() const { return !m_eventCb.isEmpty(); }

    void disconnectAll()
    {
        // Recarga de módulos: los sockets persistentes se cierran (los
        // callbacks quedan huérfanos de módulo y no deben seguir vivos).
        const QStringList tcpIds = m_tcp.keys();
        for (const QString &id : tcpIds) closeSocketInternal(m_tcp, id);
        const QStringList wsIds = m_ws.keys();
        for (const QString &id : wsIds) closeSocketInternal(m_ws, id);
        m_tcpCb.clear();
        m_wsCb.clear();
        m_eventCb.clear();
        const QList<int> ids = m_timers.keys();
        for (int id : ids) {
            QTimer *t = m_timers.take(id).first;
            if (t) t->deleteLater();
        }
    }

signals:
    void logChanged();
    void jsNotify(const QString &title, const QString &text);

private:
    template<typename S>
    void closeSocketInternal(QMap<QString, S *> &map, const QString &id)
    {
        S *sock = map.take(id);
        if (!sock) return;
        sock->disconnect(this);       // evita callbacks tras liberar
        sock->deleteLater();
    }

    void deliverToCallback(const QJSValue &cbIn, const QString &msg, const QString &origin)
    {
        QJSValue callback = cbIn;                   // copia: métodos no-const
        if (!engine || callback.isNull() || !callback.isCallable()) return;
        QJSValueList args;
        args << engine->toScriptValue(msg);
        const QJSValue r = callback.call(args);
        if (r.isError())
            pushLog(QStringLiteral("[js:err] mensaje %1: %2").arg(origin, r.toString()));
    }

    // Entrega de datos TCP: el host escucha readyRead de TODOS los sockets.
    // (connect() desde tcpConnect / attachTcpSocket).
    void attachTcpSocket(const QString &id, QTcpSocket *sock)
    {
        connect(sock, &QTcpSocket::readyRead, this, [this, id, sock]() {
            const QByteArray chunk = sock->readAll();
            deliverToCallback(m_tcpCb.value(id), QString::fromUtf8(chunk),
                              QStringLiteral("tcp:%1").arg(id));
        });
        connect(sock, &QAbstractSocket::errorOccurred,
                this, [this, id](QAbstractSocket::SocketError e) {
                    pushLog(QStringLiteral("[js:warn] tcp %1 error %2")
                                .arg(id, QString::number(int(e))));
                });
    }

    QNetworkAccessManager m_nam;
    QMap<QString, QTcpSocket *> m_tcp;
    QMap<QString, QWebSocket *> m_ws;
    QMap<QString, QJSValue>     m_tcpCb;
    QMap<QString, QJSValue>     m_wsCb;
    QMap<QString, QVector<QJSValue>> m_eventCb;
    QMap<int, QPair<QTimer *, QJSValue>> m_timers;
    int m_timerId = 0;
};

// ---------------------------------------------------------------------------
// Motor: crea QJSEngine + host, carga módulos y expone la API pública C++.
// ---------------------------------------------------------------------------
class JsEngine : public QObject
{
    Q_OBJECT
public:
    explicit JsEngine(QObject *parent = nullptr) : QObject(parent)
    {
        recreate();
    }

    ~JsEngine() override
    {
        // Orden seguro: los QJSValue de los callbacks referencian al motor.
        // Se sueltan (disconnectAll) ANTES de destruir QJSEngine; el host
        // es hijo de this y lo libera el padre QObject.
        if (m_host) m_host->disconnectAll();
        delete m_engine;         // QObject con padre: se desregistra solo
        m_engine = nullptr;
    }

    JsLibHost *host() const { return m_host; }
    QStringList loadedModules() const { return m_loaded; }
    QStringList errors() const { return m_errors; }

    // Carga (o recarga) los módulos de <dataDir>/modules/*.js
    bool loadModules(const QString &dataDir)
    {
        m_dataDir = dataDir;
        return reload();
    }

    bool reload()
    {
        recreate();           // engine + host limpios (sockets cerrados)
        m_loaded.clear();
        m_errors.clear();
        const QDir dir(QDir(m_dataDir).absoluteFilePath(QStringLiteral("modules")));
        if (!dir.exists()) {
            qInfo() << "[Js] Sin directorio de módulos (normal en primer arranque):" << dir.absolutePath();
            return true;      // no es error: simplemente no hay módulos
        }
        const QStringList files = dir.entryList(QStringList() << QStringLiteral("*.js"),
                                                QDir::Files, QDir::Name);
        for (const QString &file : files) {
            QFile f(dir.absoluteFilePath(file));
            if (!f.open(QIODevice::ReadOnly)) {           // ERR: comprobar open()
                m_errors.append(QStringLiteral("%1: no se pudo abrir").arg(file));
                continue;
            }
            const QString source = QString::fromUtf8(f.readAll());
            f.close();
            const QJSValue result = m_engine->evaluate(source, file);
            if (result.isError()) {
                const QString err = QStringLiteral("%1:%2 — %3")
                        .arg(file, QString::number(result.property(QStringLiteral("lineNumber")).toInt()),
                             result.toString());
                m_errors.append(err);
                qWarning() << "[Js] Error de módulo:" << err;
                continue;
            }
            m_loaded.append(file);
            qInfo() << "[Js] Módulo cargado:" << file;
        }
        emit modulesChanged();
        return m_errors.isEmpty();
    }

    // Puente de eventos: Triggers::fireEvent -> JS (onEvent)
    void fireEvent(const QString &name, const QVariantMap &data)
    {
        if (m_host && m_host->hasEventHandlers()) m_host->fireEvent(name, data);
    }

    // Acción de trigger "script": llamada a función global con el payload.
    bool callFunction(const QString &name, const QVariantMap &data)
    {
        return m_host ? m_host->callGlobalFunction(name, data) : false;
    }

signals:
    void modulesChanged();

private:
    void recreate()
    {
        if (m_host) {
            m_host->disconnectAll();
            delete m_host;      // destruye sockets/timers hijos antes del engine
        }
        m_engine = new QJSEngine(this);
        m_host = new JsLibHost(this);
        m_host->engine = m_engine;
        // jslib en el global object. newQObject SIN padre para el host
        // implicaría transferencia de ownership al engine; el host ya es
        // hijo de JsEngine => C++ conserva la propiedad (sin doble delete).
        m_engine->globalObject().setProperty(QStringLiteral("jslib"),
                                             m_engine->newQObject(m_host));
        // API extra documentada para los módulos:
        m_engine->evaluate(QStringLiteral(
            "var JSLIB_VERSION = 1.5;"));
    }

    QJSEngine *m_engine = nullptr;
    JsLibHost *m_host = nullptr;
    QString m_dataDir;
    QStringList m_loaded;
    QStringList m_errors;
};

#endif // LUMINA_JSENGINE_H
