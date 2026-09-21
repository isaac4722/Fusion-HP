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
#include <QDebug>

class WebServer : public QObject
{
    Q_OBJECT
public:
    explicit WebServer(QObject *parent = nullptr) : QObject(parent) {}

    bool start(quint16 wsPort, quint16 httpPort, QString *error = nullptr)
    {
        // WebSocket
        if (m_ws) { m_ws->close(); m_ws->deleteLater(); }
        m_ws = new QWebSocketServer(QStringLiteral("LuminaRemoteServer"),
                                    QWebSocketServer::NonSecureMode, this);
        if (!m_ws->listen(QHostAddress::Any, wsPort)) {
            if (error) *error = QStringLiteral("No se pudo escuchar WebSocket en el puerto %1").arg(wsPort);
            m_ws = nullptr;
            return false;
        }
        connect(m_ws, &QWebSocketServer::newConnection, this, &WebServer::onNewWsConnection);

        // HTTP
        if (m_http) { m_http->close(); m_http->deleteLater(); }
        m_http = new QTcpServer(this);
        if (!m_http->listen(QHostAddress::Any, httpPort)) {
            if (error) *error = QStringLiteral("No se pudo escuchar HTTP en el puerto %1").arg(httpPort);
            m_http = nullptr;
            return false;
        }
        connect(m_http, &QTcpServer::newConnection, this, &WebServer::onNewHttpConnection);
        qInfo() << "[Web] Servidor remoto OK  ws:" << wsPort << " http:" << httpPort;
        return true;
    }

    void stop()
    {
        if (m_ws) { m_ws->close(); }
        if (m_http) { m_http->close(); }
    }

    bool running() const { return m_ws != nullptr; }
    quint16 wsPort() const { return m_ws ? m_ws->serverPort() : 0; }
    quint16 httpPort() const { return m_http ? m_http->serverPort() : 0; }

public slots:
    // Estado actual (lo publica MainWindow)
    void setLiveState(const QJsonObject &state)
    {
        m_state = state;
        broadcastState();
    }

    void broadcastAlert(const QString &text)
    {
        QJsonObject ev;
        ev["ev"] = QStringLiteral("alert");
        ev["text"] = text;
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
        connect(sock, &QAbstractSocket::disconnected, sock, &QObject::deleteLater);
    }

private:
    void handleHttpRequest(QTcpSocket *sock, const QByteArray &request)
    {
        const int lineEnd = request.indexOf("\r\n");
        const QByteArray reqLine = request.left(lineEnd < 0 ? request.size() : lineEnd);
        const QList<QByteArray> parts = reqLine.split(' ');
        if (parts.size() < 2) { sock->disconnectFromHost(); return; }
        QString path = QUrl::fromPercentEncoding(parts.at(1));
        const int qm = path.indexOf(QChar('?'));
        if (qm >= 0) path = path.left(qm);
        if (path == QStringLiteral("/")) path = QStringLiteral("/remote.html");

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
                   "API: <a href=\"/api/state\">/api/state</a></p></body></html>";
        }

        QByteArray resp;
        resp += "HTTP/1.1 200 OK\r\n";
        resp += "Content-Type: " + contentType + "\r\n";
        if (isJson) resp += "Access-Control-Allow-Origin: *\r\n";
        resp += "Content-Length: " + QByteArray::number(body.size()) + "\r\n";
        resp += "Connection: close\r\n\r\n";
        resp += body;
        sock->write(resp);
        sock->flush();
        sock->disconnectFromHost();
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
};

#endif // LUMINA_WEBSERVER_H
