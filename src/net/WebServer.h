// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  WebServer.h : Servidor embebido para control remoto y salida web.
//  - WebSocket (QWebSocketServer) : API JSON bidireccional para el control
//    remoto movil (next/prev/black/clear/logo/goto/alert...).
//  - HTTP (QTcpServer) : sirve el panel de control remoto (remote.html),
//    el overlay HTML5 con fondo transparente para OBS Studio / vMix
//    (overlay.html) y endpoints JSON de estado (/api/overlay.json).
// ============================================================================
#ifndef LUMINA_WEBSERVER_H
#define LUMINA_WEBSERVER_H

#include <QObject>
#include <QTcpServer>
#include <QTcpSocket>
#include <QWebSocket>
#include <QWebSocketServer>
#include <QJsonDocument>
#include <QJsonObject>
#include <QJsonArray>
#include <QDateTime>
#include <QFile>
#include <QUrl>
#include <QUrlQuery>
#include <QDebug>

class WebServer : public QObject
{
    Q_OBJECT
public:
    explicit WebServer(QObject *parent = nullptr) : QObject(parent) {}

    bool start(quint16 wsPort, quint16 httpPort, QString *error = nullptr)
    {
        // WebSocket
        if (m_ws) { m_ws->close(); m_ws->deleteLater(); m_ws = nullptr; }
        m_ws = new QWebSocketServer(QStringLiteral("LuminaRemoteServer"),
                                    QWebSocketServer::NonSecureMode, this);
        if (!m_ws->listen(QHostAddress::Any, wsPort)) {
            if (error) *error = QStringLiteral("No se pudo escuchar WebSocket en el puerto %1").arg(wsPort);
            // CORRECCION v1.2.0: el objeto recién creado (hijo de this) quedaba
            // huérfano con el puntero anulado — fuga por cada intento fallido.
            m_ws->deleteLater();
            m_ws = nullptr;
            return false;
        }
        connect(m_ws, &QWebSocketServer::newConnection, this, &WebServer::onNewWsConnection);

        // HTTP
        if (m_http) { m_http->close(); m_http->deleteLater(); m_http = nullptr; }
        m_http = new QTcpServer(this);
        if (!m_http->listen(QHostAddress::Any, httpPort)) {
            if (error) *error = QStringLiteral("No se pudo escuchar HTTP en el puerto %1").arg(httpPort);
            // CORRECCION v1.2.0: idem + se cerraba el WS ya operativo para no
            // dejar un estado mixto (start()==false pero running()==true).
            m_http->deleteLater();
            m_http = nullptr;
            m_ws->close();
            m_ws->deleteLater();
            m_ws = nullptr;
            return false;
        }
        connect(m_http, &QTcpServer::newConnection, this, &WebServer::onNewHttpConnection);
        qInfo() << "[Web] Servidor remoto OK  ws:" << wsPort << " http:" << httpPort;
        return true;
    }

    void stop()
    {
        // CORRECCION v1.2.0: stop() cerraba los servidores pero dejaba los
        // punteros vivos -> running() seguía devolviendo true tras parar.
        if (m_ws) { m_ws->close(); m_ws->deleteLater(); m_ws = nullptr; }
        if (m_http) { m_http->close(); m_http->deleteLater(); m_http = nullptr; }
        for (QWebSocket *c : qAsConst(m_clients))
            if (c) c->close();
        m_clients.clear();
        m_buffer.clear();
    }

    bool running() const { return m_ws != nullptr; }
    quint16 wsPort() const { return m_ws ? m_ws->serverPort() : 0; }
    quint16 httpPort() const { return m_http ? m_http->serverPort() : 0; }

    // v1.1.0: token opcional de la API HTTP (spec Holyrics: autenticacion
    // por token). Si esta vacio, la API local no exige token (red LAN).
    void setApiToken(const QString &token) { m_apiToken = token; }

public slots:
    // Estado actual (lo publica MainWindow)
    void setLiveState(const QJsonObject &state)
    {
        m_state = state;
        // v1.1.0: cache de texto plano para /api/live.txt (OBS)
        m_liveText.clear();
        for (const QJsonValue &v : state.value(QStringLiteral("lines")).toArray())
            m_liveText += (m_liveText.isEmpty() ? QString() : QStringLiteral("\n")) + v.toString();
        m_liveTextMode = state.value(QStringLiteral("mode")).toString() == QStringLiteral("content");
        broadcastState();
    }

    void broadcastAlert(const QString &text)
    {
        QJsonObject ev;
        ev["ev"] = QStringLiteral("alert");
        ev["text"] = text;
        broadcast(QString::fromUtf8(QJsonDocument(ev).toJson(QJsonDocument::Compact)));
    }

    // v1.3.0 — "Custom Messages" del spec Holyrics: mensaje del operador a
    // TODOS los dispositivos remotos conectados (se muestra como toast en
    // remote.html; no interrumpe la proyección ni el Stage View).
    void broadcastMessage(const QString &text, const QString &title = QString())
    {
        QJsonObject ev;
        ev["ev"] = QStringLiteral("message");
        ev["text"] = text;
        if (!title.isEmpty())
            ev["title"] = title;
        broadcast(QString::fromUtf8(QJsonDocument(ev).toJson(QJsonDocument::Compact)));
    }

signals:
    // Comandos del control remoto hacia MainWindow
    void remoteCommand(const QString &cmd, const QJsonObject &data);

private slots:
    void onNewWsConnection()
    {
        QWebSocket *sock = m_ws->nextPendingConnection();
        if (!sock) return;
        m_clients << sock;
        // CORRECCION v1.2.0: el control remoto no recibía el estado inicial al
        // conectar — mostraba "— sin contenido —" hasta que el operador movía
        // una slide o pulsaba un botón. Se envía el estado vigente de entrada.
        sock->sendTextMessage(QString::fromUtf8(QJsonDocument(m_state).toJson(QJsonDocument::Compact)));
        connect(sock, &QWebSocket::textMessageReceived, this, [this, sock](const QString &msg) {
            const QJsonObject obj = QJsonDocument::fromJson(msg.toUtf8()).object();
            const QString cmd = obj.value(QStringLiteral("cmd")).toString();
            emit remoteCommand(cmd, obj.value(QStringLiteral("data")).toObject());
            // Respuesta inmediata de estado
            sock->sendTextMessage(QString::fromUtf8(QJsonDocument(m_state).toJson(QJsonDocument::Compact)));
        });
        connect(sock, &QWebSocket::disconnected, this, [this, sock]() {
            m_clients.removeAll(sock);
            sock->deleteLater();
        });
    }

    void onNewHttpConnection()
    {
        QTcpSocket *sock = m_http->nextPendingConnection();
        if (!sock) return;
        connect(sock, &QTcpSocket::readyRead, this, [this, sock]() {
            m_buffer[sock] += sock->readAll();
            if (!m_buffer[sock].contains("\r\n\r\n")) return;
            const QByteArray req = m_buffer.take(sock);
            handleHttpRequest(sock, req);
        });
        // CORRECCION v1.2.0: si el cliente se desconectaba antes de enviar los
        // "\r\n\r\n" (puerto escaneado, petición abortada, keep-alive sin
        // cuerpo), su entrada en m_buffer quedaba colgando PARA SIEMPRE: fuga
        // del QByteArray acumulado + clave QTcpSocket* muerta (una conexión
        // nueva podía reutilizar esa dirección y concatenar datos ajenos).
        connect(sock, &QAbstractSocket::disconnected, this, [this, sock]() {
            m_buffer.remove(sock);
        });
        connect(sock, &QAbstractSocket::disconnected, sock, &QObject::deleteLater);
    }

private:
    void handleHttpRequest(QTcpSocket *sock, const QByteArray &request)
    {
        const int lineEnd = request.indexOf("\r\n");
        const QByteArray reqLine = request.left(lineEnd < 0 ? request.size() : lineEnd);
        const QList<QByteArray> parts = reqLine.split(' ');
        if (parts.size() < 2) { sock->disconnectFromHost(); return; }
        const QByteArray rawTarget = parts.at(1);
        QString path = QUrl::fromPercentEncoding(rawTarget);
        const int qm = path.indexOf(QChar('?'));
        // CORRECCION v1.1.0: QUrlQuery NO elimina la ruta del string — si se
        // le pasa "/api/cmd?c=next" el primer par quedaba como clave
        // "/api/cmd?c" y queryItemValue("c") devolvia vacio. Hay que extraer
        // SOLO la parte posterior al '?'.
        QUrlQuery query(qm >= 0 ? QString::fromUtf8(rawTarget.mid(qm + 1)) : QString());
        if (qm >= 0) path = path.left(qm);
        if (path == QStringLiteral("/")) path = QStringLiteral("/remote.html");

        // ----------------------------------------------------------------
        // v1.1.0: API HTTP de comandos (spec Componente 3: "puntos de
        // conexión HTTP simples para permitir el control remoto").
        //   GET /api/cmd?c=next|prev|black|clear|logo|goto|alert|qnext|qprev
        //        [&i=N] [&text=...] [&token=...]
        //   GET /api/live.txt  -> texto plano del slide actual (fuente de
        //        texto de OBS Studio / integraciones simples)
        // ----------------------------------------------------------------
        if (path == QStringLiteral("/api/cmd")) {
            if (!apiTokenOk(query)) {
                sendHttp(sock, 401, "application/json; charset=utf-8",
                         "{\"ok\":false,\"error\":\"token invalido\"}", true);
                return;
            }
            const QString cmd = query.queryItemValue(QStringLiteral("c"));
            QJsonObject data;
            const QString idx = query.queryItemValue(QStringLiteral("i"));
            if (!idx.isEmpty()) data["index"] = idx.toInt();
            const QString text = query.queryItemValue(QStringLiteral("text"));
            if (!text.isEmpty()) data["text"] = text;
            emit remoteCommand(cmd, data);
            sendHttp(sock, 200, "application/json; charset=utf-8",
                     "{\"ok\":true}", true);
            return;
        }
        if (path == QStringLiteral("/api/live.txt")) {
            if (!apiTokenOk(query)) {
                sendHttp(sock, 401, "text/plain; charset=utf-8", "token invalido\n", true);
                return;
            }
            QString txt;
            if (m_liveTextMode)
                txt = m_liveText;
            else if (!m_state.value(QStringLiteral("lines")).toArray().isEmpty()) {
                for (const QJsonValue &v : m_state.value(QStringLiteral("lines")).toArray())
                    txt += (txt.isEmpty() ? QString() : QStringLiteral("\n")) + v.toString();
            }
            sendHttp(sock, 200, "text/plain; charset=utf-8", (txt + QChar('\n')).toUtf8(), true);
            return;
        }

        QByteArray contentType = "text/html; charset=utf-8";
        QByteArray body;
        bool isJson = false;

        if (path == QStringLiteral("/api/overlay.json") || path == QStringLiteral("/api/state")) {
            body = QJsonDocument(m_state).toJson(QJsonDocument::Compact);
            contentType = "application/json; charset=utf-8";
            isJson = true;
        } else if (path == QStringLiteral("/overlay.html") || path == QStringLiteral("/overlay")) {
            body = resource(QStringLiteral(":/web/overlay.html"));
        } else if (path == QStringLiteral("/remote.html") || path == QStringLiteral("/remote")) {
            // CORRECCION: el puerto WS estaba hardcodeado (8765) en el HTML; si
            // el usuario lo cambiaba en Ajustes, el control remoto se rompia.
            // Ahora se inyecta el puerto real del servidor al servir la pagina.
            body = resource(QStringLiteral(":/web/remote.html"));
            const QString wsPortToken = QStringLiteral("%%WSPORT%%");
            const QByteArray wsPortValue = QByteArray::number(m_ws ? int(m_ws->serverPort()) : 8765);
            body.replace(wsPortToken.toUtf8(), wsPortValue);
        } else if (path == QStringLiteral("/favicon.ico")) {
            body = resource(QStringLiteral(":/img/logo.png"));
            contentType = "image/png";
        } else {
            body = "<html><body><h3>LuminaPresentation Suite</h3>"
                   "<p>Control remoto: <a href=\"/remote.html\">/remote.html</a> | "
                   "Overlay OBS: <a href=\"/overlay.html\">/overlay.html</a> | "
                   "API: <a href=\"/api/state\">/api/state</a> · "
                   "<a href=\"/api/live.txt\">/api/live.txt</a> · "
                   "<code>/api/cmd?c=next</code></p></body></html>";
        }
        sendHttp(sock, 200, contentType, body, isJson);
    }

    static void sendHttp(QTcpSocket *sock, int status, const QByteArray &contentType,
                         const QByteArray &body, bool cors)
    {
        // CORRECCION v1.2.0: reason phrase real por código (antes todo lo que
        // no era 200 se enviaba como " Error", p.ej. "401 Error").
        const char *reason = "OK";
        if (status == 400) reason = "Bad Request";
        else if (status == 401) reason = "Unauthorized";
        else if (status == 404) reason = "Not Found";
        else if (status == 500) reason = "Internal Server Error";
        QByteArray resp;
        resp += QByteArray("HTTP/1.1 ") + QByteArray::number(status) + ' ' + reason + "\r\n";
        resp += "Content-Type: " + contentType + "\r\n";
        if (cors) resp += "Access-Control-Allow-Origin: *\r\n";
        resp += "Content-Length: " + QByteArray::number(body.size()) + "\r\n";
        resp += "Connection: close\r\n\r\n";
        resp += body;
        sock->write(resp);
        sock->flush();
        sock->disconnectFromHost();
    }

    bool apiTokenOk(const QUrlQuery &query) const
    {
        if (m_apiToken.isEmpty()) return true;      // sin token configurado
        return query.queryItemValue(QStringLiteral("token")) == m_apiToken;
    }

    static QByteArray resource(const QString &path)
    {
        QFile f(path);
        if (!f.open(QIODevice::ReadOnly)) return QByteArray();
        return f.readAll();
    }

    void broadcastState()
    {
        if (m_clients.isEmpty()) return;
        broadcast(QString::fromUtf8(QJsonDocument(m_state).toJson(QJsonDocument::Compact)));
    }

    void broadcast(const QString &msg)
    {
        for (QWebSocket *c : qAsConst(m_clients))
            if (c && c->isValid()) c->sendTextMessage(msg);
    }

private:
    QWebSocketServer *m_ws = nullptr;
    QTcpServer *m_http = nullptr;
    QVector<QWebSocket *> m_clients;
    QHash<QTcpSocket *, QByteArray> m_buffer;
    QJsonObject m_state;
    QString m_apiToken;
    QString m_liveText;
    bool m_liveTextMode = false;
};

#endif // LUMINA_WEBSERVER_H
