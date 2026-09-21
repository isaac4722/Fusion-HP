// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Triggers.h : Sistema de automatizacion de eventos.
//  - Webhook HTTP (GET/POST) con plantillas {event}/{slide}/{song}
//  - Cliente OBS WebSocket v5 (obs-websocket) con autenticacion SHA-256
//    para cambio automatico de escenas al proyectar.
//  - MIDI Out (winmm) para mesas DMX.
//  - MIDI In (winmm) v1.4.0: eventos de disparo EXTERNOS del spec
//    Holyrics — hardware MIDI (pedales/pads) -> comandos del presentador.
//  - Bot de Telegram: recepcion de peticiones y envio de avisos.
// ============================================================================
#ifndef LUMINA_TRIGGERS_H
#define LUMINA_TRIGGERS_H

#include "MidiOut.h"
#include "MidiIn.h"

#include <QObject>
#include <QNetworkAccessManager>
#include <QNetworkRequest>
#include <QNetworkReply>
#include <QWebSocket>
#include <QTimer>
#include <QJsonDocument>
#include <QJsonObject>
#include <QJsonArray>
#include <QCryptographicHash>
#include <QByteArray>
#include <QUrlQuery>
#include <QUrl>
#include <QLoggingCategory>
#include <QDebug>

// Categoría de registro del módulo OBS (log estructurado, spec §2.6).
// Función inline con static local: una única instancia por proceso sin
// necesitar unidad de compilación propia.
inline const QLoggingCategory &obsLogCat()
{
    static const QLoggingCategory cat("lumina.obs");
    return cat;
}

// ---------------------------------------------------------------------------
// Cliente OBS WebSocket v5 (obs-websocket >= 5.0)
// ---------------------------------------------------------------------------
class ObsClient : public QObject
{
    Q_OBJECT
public:
    explicit ObsClient(QObject *parent = nullptr) : QObject(parent)
    {
        connect(&m_ws, &QWebSocket::connected, this, &ObsClient::onConnected);
        // CORRECCION v1.2.0: tras una desconexión (OBS cerrado/reiniciado) no
        // había reconexión — disconnected solo reseteaba m_authed y el timer
        // de reintento había quedado parado tras el Identified. El usuario
        // tenía que volver a Aplicar la configuración. Ahora se reactiva el
        // reintento periódico si el cliente sigue habilitado.
        connect(&m_ws, &QWebSocket::disconnected, this, [this]() {
            if (m_authed) emit connectionChanged(false);
            m_authed = false;
            if (m_enabled) m_reconnect.start();
            logCloseCause();
        });
        connect(&m_ws, &QWebSocket::textMessageReceived, this, &ObsClient::onMessage);
        // v1.6.0 (M13): diagnóstico de la conexión — antes un fallo era mudo y
        // el usuario no podía distinguir una dirección/puerto incorrecto de una
        // contraseña rechazada o de un tiempo agotado. Se registra el error del
        // socket y los cambios de estado; la causa final del cierre (close code
        // del servidor obs-websocket v5) se reporta en logCloseCause().
        connect(&m_ws, &QWebSocket::stateChanged, this, [](QAbstractSocket::SocketState st) {
            qCDebug(obsLogCat(), "OBS WebSocket estado: %d", int(st));
        });
        // QWebSocket::error está marcada deprecated desde Qt 6.5 (errorOccurred);
        // en 5.15 es la señal válida (mismo patrón silenciado de JsEngine.h).
        QT_WARNING_PUSH
        QT_WARNING_DISABLE_DEPRECATED
        connect(&m_ws, QOverload<QAbstractSocket::SocketError>::of(&QWebSocket::error),
                this, [this](QAbstractSocket::SocketError e) {
            if (e == QAbstractSocket::RemoteHostClosedError)
                return;             // cierre desde el servidor: lo reporta logCloseCause()
            if (e == QAbstractSocket::HostNotFoundError)
                qCWarning(obsLogCat(), "OBS: no se encontró el host «%s:%u» (¿dirección o puerto incorrectos? ¿OBS está abierto?)",
                          qUtf8Printable(m_host), unsigned(m_port));
            else if (e == QAbstractSocket::ConnectionRefusedError)
                qCWarning(obsLogCat(), "OBS: conexión rechazada en «%s:%u» (¿está habilitado obs-websocket?)",
                          qUtf8Printable(m_host), unsigned(m_port));
            else if (e == QAbstractSocket::SocketTimeoutError)
                qCWarning(obsLogCat(), "OBS: tiempo agotado esperando respuesta del servidor WebSocket");
            else
                qCWarning(obsLogCat(), "OBS: error de socket %d", int(e));
        });
        QT_WARNING_POP
        m_reconnect.setInterval(15000);
        connect(&m_reconnect, &QTimer::timeout, this, [this]() { if (m_enabled) connectTo(m_host, m_port, m_password); });
    }

    void configure(bool enabled, const QString &host, quint16 port, const QString &password)
    {
        m_enabled = enabled;
        m_host = host; m_port = port; m_password = password;
        if (enabled) connectTo(host, port, password);
        else { m_ws.close(); m_reconnect.stop(); }
    }

    bool connected() const { return m_authed; }

    void setScene(const QString &sceneName)
    {
        if (!m_authed) return;
        QJsonObject req;
        req["requestType"] = QStringLiteral("SetCurrentProgramScene");
        QJsonObject data; data["sceneName"] = sceneName;
        req["requestData"] = data;
        req["requestId"] = QStringLiteral("scene-%1").arg(++m_reqId);
        m_ws.sendTextMessage(QString::fromUtf8(QJsonDocument(req).toJson(QJsonDocument::Compact)));
    }

signals:
    void connectionChanged(bool ok);

private slots:
    void onConnected()
    {
        // Espera Hello (op 0) con challenge
    }
    void onMessage(const QString &msg)
    {
        const QJsonObject obj = QJsonDocument::fromJson(msg.toUtf8()).object();
        const int op = obj.value(QStringLiteral("op")).toInt(-1);
        const QJsonObject d = obj.value(QStringLiteral("d")).toObject();
        if (op == 0) {
            // Hello -> Identify
            const QString challenge = d.value(QStringLiteral("authentication")).toObject()
                                          .value(QStringLiteral("challenge")).toString();
            const QString salt = d.value(QStringLiteral("authentication")).toObject()
                                     .value(QStringLiteral("salt")).toString();
            QJsonObject ident;
            ident["rpcVersion"] = 1;
            if (!salt.isEmpty()) {
                const QByteArray secret = QCryptographicHash::hash(
                    (m_password + salt).toUtf8(), QCryptographicHash::Sha256).toBase64();
                const QByteArray auth = QCryptographicHash::hash(
                    secret + challenge.toUtf8(), QCryptographicHash::Sha256).toBase64();
                ident["authentication"] = QString::fromUtf8(auth);
            }
            QJsonObject out;
            out["op"] = 1; out["d"] = ident;
            m_ws.sendTextMessage(QString::fromUtf8(QJsonDocument(out).toJson(QJsonDocument::Compact)));
        } else if (op == 2) {
            m_authed = true;
            m_reconnect.stop();
            emit connectionChanged(true);
            qInfo() << "[OBS] Conectado y autenticado (obs-websocket v5)";
        }
    }

private:
    // v1.6.0 (M13): causa de la desconexión según el close code/reason que
    // entrega el servidor. obs-websocket v5 cierra con el código 4001 cuando
    // la autenticación (contraseña) es rechazada; también se inspecciona el
    // texto del motivo por si el servidor usa otra variante.
    void logCloseCause()
    {
        const quint16 code = m_ws.closeCode();
        const QString reason = m_ws.closeReason();
        if (code == 4001 || reason.contains(QStringLiteral("auth"), Qt::CaseInsensitive)) {
            qCWarning(obsLogCat(), "OBS: contraseña rechazada por obs-websocket (código %u) — revise la contraseña en OBS Herramientas▸WebSocket Server Settings",
                      code);
        } else if (code != 0 && code != QWebSocketProtocol::CloseCodeNormal) {
            qCWarning(obsLogCat(), "OBS cerró la conexión: código %u «%s»",
                      code, qUtf8Printable(reason));
        }
        // code 0 / 1005 / 1006 (sin frame de cierre): corte abrupto — típico
        // de OBS cerrado o red caída; el reintento periódico lo cubre.
    }

    void connectTo(const QString &host, quint16 port, const QString &)
    {
        if (m_ws.state() == QAbstractSocket::ConnectedState) return;
        const QUrl url(QStringLiteral("ws://%1:%2").arg(host).arg(port));
        m_ws.open(url);
        m_reconnect.start();
    }

    QWebSocket m_ws;
    QTimer m_reconnect;
    bool m_enabled = false, m_authed = false;
    QString m_host;
    quint16 m_port = 4455;
    QString m_password;
    int m_reqId = 0;
};

// ---------------------------------------------------------------------------
// Bot de Telegram (recepcion de peticiones / envio de avisos)
// ---------------------------------------------------------------------------
class TelegramBot : public QObject
{
    Q_OBJECT
public:
    explicit TelegramBot(QObject *parent = nullptr) : QObject(parent)
    {
        connect(&m_nam, &QNetworkAccessManager::finished, this, &TelegramBot::onReply);
        m_poll.setInterval(2500);
        connect(&m_poll, &QTimer::timeout, this, &TelegramBot::poll);
    }

    void configure(bool enabled, const QString &token, const QString &chatId)
    {
        m_token = token; m_chatId = chatId;
        if (enabled && !token.isEmpty()) m_poll.start();
        else m_poll.stop();
    }

    bool running() const { return m_poll.isActive(); }

    void sendMessage(const QString &text)
    {
        if (m_token.isEmpty() || m_chatId.isEmpty()) return;
        QUrl url(QStringLiteral("https://api.telegram.org/bot%1/sendMessage").arg(m_token));
        QUrlQuery q;
        q.addQueryItem(QStringLiteral("chat_id"), m_chatId);
        q.addQueryItem(QStringLiteral("text"), text);
        url.setQuery(q);
        QNetworkRequest req(url);
        // v1.6.0 (M6): sin timeout, una red caída dejaba el envío colgado.
        req.setTransferTimeout(10000);
        m_nam.get(req);
    }

signals:
    void messageReceived(const QString &from, const QString &text);

private slots:
    void poll()
    {
        // v1.6.0 (M6): no encolar otra encuesta si la anterior sigue en vuelo
        // (con timeout=0 la respuesta suele llegar rápido, pero con la red
        // degradada se acumulaban peticiones solapadas).
        if (m_pollInFlight) return;
        QUrl url(QStringLiteral("https://api.telegram.org/bot%1/getUpdates").arg(m_token));
        QUrlQuery q;
        q.addQueryItem(QStringLiteral("timeout"), QStringLiteral("0"));
        q.addQueryItem(QStringLiteral("offset"), QString::number(m_offset));
        q.addQueryItem(QStringLiteral("limit"), QStringLiteral("10"));
        url.setQuery(q);
        QNetworkRequest req(url);
        // v1.6.0 (M6): sin timeout, una red caída colgaba la encuesta y el bot
        // dejaba de responder hasta reiniciar el programa.
        req.setTransferTimeout(10000);
        m_pollInFlight = true;
        QNetworkReply *rep = m_nam.get(req);
        // Se libera el flag cuando ESTA respuesta termina (el deleteLater del
        // reply lo hace el onReply común del QNetworkAccessManager).
        connect(rep, &QNetworkReply::finished, this, [this]() { m_pollInFlight = false; });
    }

    void onReply(QNetworkReply *rep)
    {
        rep->deleteLater();
        if (rep->error() != QNetworkReply::NoError) return;
        const QJsonObject obj = QJsonDocument::fromJson(rep->readAll()).object();
        if (!obj.value(QStringLiteral("ok")).toBool()) return;
        const QJsonArray updates = obj.value(QStringLiteral("result")).toArray();
        for (const QJsonValue &v : updates) {
            const QJsonObject u = v.toObject();
            const qint64 id = u.value(QStringLiteral("update_id")).toVariant().toLongLong();
            if (id >= m_offset) m_offset = id + 1;
            const QJsonObject message = u.value(QStringLiteral("message")).toObject();
            if (message.isEmpty()) continue;
            // v1.6.0 (M6): solo se aceptan mensajes del chat configurado — antes
            // CUALQUIER usuario que hablara con el bot disparaba comandos.
            // m_chatId admite un ID numérico (grupo/canal, puede ser negativo)
            // o un @usuario público (comparación sin '@' e insensible a
            // mayúsculas); el resto se descarta.
            const QJsonObject chat = message.value(QStringLiteral("chat")).toObject();
            bool fromConfiguredChat = false;
            bool numericCfg = false;
            const qint64 cfgNum = m_chatId.trimmed().toLongLong(&numericCfg);
            if (numericCfg) {
                fromConfiguredChat =
                    chat.value(QStringLiteral("id")).toVariant().toLongLong() == cfgNum;
            } else {
                QString cfgUser = m_chatId.trimmed();
                if (cfgUser.startsWith(QLatin1Char('@'))) cfgUser.remove(0, 1);
                QString chatUser = chat.value(QStringLiteral("username")).toString();
                if (chatUser.startsWith(QLatin1Char('@'))) chatUser.remove(0, 1);
                fromConfiguredChat = !cfgUser.isEmpty() &&
                                     chatUser.compare(cfgUser, Qt::CaseInsensitive) == 0;
            }
            if (!fromConfiguredChat) continue;
            const QString from = message.value(QStringLiteral("from")).toObject()
                                     .value(QStringLiteral("first_name")).toString();
            const QString text = message.value(QStringLiteral("text")).toString();
            if (!text.isEmpty()) emit messageReceived(from, text);
        }
    }

private:
    QNetworkAccessManager m_nam;
    QTimer m_poll;
    QString m_token, m_chatId;
    qint64 m_offset = 0;
    bool m_pollInFlight = false;      // v1.6.0 (M6): encuesta getUpdates en curso
};

#include "core/JsEngine.h"      // v1.5.0: tipo completo (fireEvent -> JS)

// ---------------------------------------------------------------------------
// Orquestador de triggers
// ---------------------------------------------------------------------------
class Triggers : public QObject
{
    Q_OBJECT
public:
    struct Config
    {
        bool   webhookEnabled = false;
        QString webhookUrl;             // plantilla: {event} {slide} {song} {item}
        bool   obsEnabled = false;
        QString obsHost = QStringLiteral("127.0.0.1");
        quint16 obsPort = 4455;
        QString obsPassword;
        QString obsSceneOnSlide = QStringLiteral("Proyeccion");
        QString obsSceneOnClear = QStringLiteral("Camara");
        bool   midiEnabled = false;
        int    midiProgramOnSlide = 0;
        // v1.4.0 — MIDI In como fuente de eventos externos (spec Holyrics)
        bool   midiInEnabled = false;
        MidiIn::NoteMap midiInMap;           // (nota, comando)
        bool   telegramEnabled = false;
        QString telegramToken;
        QString telegramChatId;
    };

    explicit Triggers(QObject *parent = nullptr) : QObject(parent)
    {
        connect(&m_obs, &ObsClient::connectionChanged, this, &Triggers::obsConnectionChanged);
        // v1.4.0: los comandos MIDI recibidos se retransmiten como señal —
        // MainWindow los conecta al MISMO dispatcher que el control remoto
        // web (onRemoteCommand), con lo que el vocabulario queda unificado.
        connect(&m_midiIn, &MidiIn::midiCommand, this, &Triggers::midiCommandReceived);
    }

    void applyConfig(const Config &c)
    {
        m_cfg = c;
        m_obs.configure(c.obsEnabled, c.obsHost, c.obsPort, c.obsPassword);
        m_telegram.configure(c.telegramEnabled, c.telegramToken, c.telegramChatId);
        // MIDI In: arranca/para segun config; si ya estaba abierto con el
        // mismo mapa solo se actualiza el mapa (start() es idempotente).
        if (c.midiInEnabled)
            m_midiIn.start(c.midiInMap);
        else
            m_midiIn.stop();
    }
    const Config &config() const { return m_cfg; }

    TelegramBot *telegram() { return &m_telegram; }

    // v1.5.0 — módulos JS (spec §3.3 "JSLib"): todos los eventos se
    // retransmiten al motor para los "onEvent" de los módulos del usuario.
    void setJsEngine(JsEngine *js) { m_js = js; }

    // Dispara un evento por nombre: "slide_next","slide_prev","media_play",
    // "media_stop","black","clear","logo","golive","alert"
    void fireEvent(const QString &event, const QVariantMap &data = QVariantMap())
    {
        // 0) Módulos JS (v1.5.0): jslib.onEvent("slide_next", function(data){…})
        if (m_js) m_js->fireEvent(event, data);
        // 1) Webhook HTTP
        if (m_cfg.webhookEnabled && !m_cfg.webhookUrl.isEmpty()) {
            // v1.6.0 (M5): la sustitución cruda de placeholders rompía la URL
            // con títulos que contienen «&», «#», «%» o espacios (cortaba
            // parámetros o alteraba la ruta). Ahora la plantilla se separa en
            // base + query: la ruta se re-arma con setPath() y la query se
            // reconstruye ítem a ítem con QUrlQuery — ambos se mantienen en
            // forma DECODIFICADA y QUrl aplica el percent-encoding al
            // serializar (mismo enfoque que TelegramBot::sendMessage). Los
            // placeholders {event}/{slide}/{song}/{item} siguen funcionando,
            // ahora codificados de forma segura.
            QUrl url(m_cfg.webhookUrl);
            if (url.isValid()) {
                const QString ph[4] = { QStringLiteral("{event}"), QStringLiteral("{slide}"),
                                        QStringLiteral("{song}"),  QStringLiteral("{item}") };
                const QString val[4] = {
                    event,
                    data.value(QStringLiteral("slide")).toString(),
                    data.value(QStringLiteral("song")).toString(),
                    data.value(QStringLiteral("item")).toString()
                };
                auto expand = [&ph, &val](QString s) {
                    for (int i = 0; i < 4; ++i) s.replace(ph[i], val[i]);
                    return s;
                };
                // Ruta y query en dominio decodificado; el encoding lo hace QUrl
                url.setPath(expand(url.path()));
                QUrlQuery query;
                // Se parsea la query DIRECTAMENTE desde el QUrl (no desde su
                // representación en texto) para respetar los «&» y «=» que
                // lleguen ya codificados dentro de un valor de la plantilla.
                const auto items = QUrlQuery(url).queryItems(QUrl::FullyDecoded);
                for (const auto &item : items)
                    query.addQueryItem(expand(item.first), expand(item.second));
                url.setQuery(query);
                QNetworkRequest req(url);
                req.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/json"));
                // ERR-5 (qt-cpp-review v1.5.0): sin timeout un webhook caído
                // dejaba la petición colgada indefinidamente.
                req.setTransferTimeout(10000);
                QJsonObject payload;
                payload["event"] = event;
                for (auto it = data.constBegin(); it != data.constEnd(); ++it)
                    payload[it.key()] = QJsonValue::fromVariant(it.value());
                // CORRECCION v1.2.0: el QNetworkReply del POST nunca se liberaba
                // — con un webhook activo, cada slide proyectada acumulaba un
                // reply (y sus buffers internos) durante TODA la sesión.
                QNetworkReply *rep = m_nam.post(req, QJsonDocument(payload).toJson(QJsonDocument::Compact));
                connect(rep, &QNetworkReply::finished, rep, &QObject::deleteLater);
            }
        }
        // 2) OBS escena
        if (m_cfg.obsEnabled) {
            if (event == QStringLiteral("slide") && !m_cfg.obsSceneOnSlide.isEmpty())
                m_obs.setScene(m_cfg.obsSceneOnSlide);
            else if ((event == QStringLiteral("clear") || event == QStringLiteral("black")) &&
                     !m_cfg.obsSceneOnClear.isEmpty())
                m_obs.setScene(m_cfg.obsSceneOnClear);
        }
        // 3) MIDI
        if (m_cfg.midiEnabled && event == QStringLiteral("slide"))
            m_midi.sendProgramChange(m_cfg.midiProgramOnSlide);
    }

    void testAll()
    {
        fireEvent(QStringLiteral("test"), QVariantMap{ { QStringLiteral("note"), QStringLiteral("LuminaPresentation trigger test") } });
        m_telegram.sendMessage(QStringLiteral("LuminaPresentation Suite: prueba de Telegram OK"));
    }

    // v1.4.0: acceso al receptor MIDI (estado/diagnostico de la GUI).
    MidiIn *midiIn() { return &m_midiIn; }

signals:
    void obsConnectionChanged(bool ok);
    void midiCommandReceived(const QString &cmd);

private:
    QNetworkAccessManager m_nam;
    ObsClient m_obs;
    TelegramBot m_telegram;
    MidiOut m_midi;
    MidiIn m_midiIn;             // v1.4.0
    JsEngine *m_js = nullptr;    // v1.5.0 (no propietario: MainWindow lo crea)
    Config m_cfg;
};

#endif // LUMINA_TRIGGERS_H
