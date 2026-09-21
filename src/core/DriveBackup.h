// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  DriveBackup.h : Respaldo y sincronización en Google Drive (spec §3.4).
//  Flujo OAuth 2.0 "loopback" (https://developers.google.com/identity/
//  protocols/oauth2/native-app): se abre el navegador con redirect_uri
//  http://127.0.0.1:<puerto> y el código llega al listener local — sin
//  copiar/pegar códigos y sin secretos embebidos en el binario.
//  Alcance MÍNIMO: drive.file (solo crea/lee los archivos de la propia app,
//  carpeta oculta appDataFolder). Los respaldos son el .db completo.
//  Rotación: se conservan los 4 más recientes (idéntico al backup local).
// ============================================================================
#ifndef LUMINA_DRIVEBACKUP_H
#define LUMINA_DRIVEBACKUP_H

#include <QObject>
#include <QTcpServer>
#include <QTcpSocket>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QJsonDocument>
#include <QJsonObject>
#include <QJsonArray>
#include <QFile>
#include <QDesktopServices>
#include <QTimer>
#include <QDateTime>
#include <QUrlQuery>
#include <functional>

#include "core/Database.h"

class Database;

class DriveBackup : public QObject
{
    Q_OBJECT
public:
    static constexpr int kTimeoutMs = 20000;
    static constexpr int kAuthTimeoutMs = 180000;     // 3 min para autorizar
    static constexpr int kKeepBackups = 4;            // rotación

    explicit DriveBackup(QObject *parent = nullptr) : QObject(parent) {}

    void setDatabase(Database *db) { m_db = db; }

    // ¿Hay cuenta conectada (refresh token guardado)?
    bool hasAccount() const;

    // --------------------------- OAuth (conectar) ----------------------------
    // Requiere Client ID + Secret de una app "Escritorio" creada por el
    // usuario en Google Cloud Console (README: guía paso a paso).
    void connectAccount(const QString &clientId, const QString &clientSecret);

    // --------------------------- Operaciones --------------------------------
    void backupNow(const QString &dbFilePath);        // subir .db a Drive
    void listBackups();                                // lista (más nuevo 1º)
    void downloadBackup(const QString &fileName, const QString &destPath);
    void disconnectAccount();                          // borra credenciales

signals:
    void authResult(bool ok, const QString &message);
    void uploadResult(bool ok, const QString &message);
    void backupsListed(bool ok, const QString &message, const QStringList &names);
    void downloadResult(bool ok, const QString &message, const QString &path);

private:
    // --------------------- Gestión de token de acceso -----------------------
    // Obtiene un access_token válido (renueva con el refresh token si ha
    // expirado). El resultado llega por callback (asincrónico).
    void ensureToken(const std::function<void(bool, const QString &)> &cb);

    void tokenRequest(const QVariantMap &params,
                      const std::function<void(bool, const QJsonObject &)> &cb);

    // --------------------------- Llamadas API --------------------------------
    void callApi(const QString &method, const QUrl &url,
                 const QByteArray &body, const QString &contentType,
                 const std::function<void(bool, int, const QByteArray &)> &cb);

    void uploadMultipart(const QString &accessToken, const QString &dbFilePath);

    void rotateAfterUpload(const QString &accessToken);

    void storeSetting(const QString &key, const QString &value);
    QString storedSetting(const QString &key, const QString &def = QString()) const;

    // --------------------------- OAuth loopback ------------------------------
    void onLoopbackConnection();

    Database *m_db = nullptr;
    QNetworkAccessManager m_nam;
    QString m_accessToken;
    QDateTime m_accessExpiry;          // validez del access_token
    QTcpServer *m_loopback = nullptr;
    QTimer m_authTimeout;
};

// ---------------------------------------------------------------------------
// Implementación (header-only, consistente con el resto del core)
// ---------------------------------------------------------------------------

inline bool DriveBackup::hasAccount() const
{
    return !storedSetting(QStringLiteral("drive_refresh_token")).isEmpty() &&
           !storedSetting(QStringLiteral("drive_client_id")).isEmpty();
}

inline void DriveBackup::storeSetting(const QString &key, const QString &value)
{
    if (m_db) m_db->setSetting(key, value);
}

inline QString DriveBackup::storedSetting(const QString &key, const QString &def) const
{
    return m_db ? m_db->setting(key, def) : def;
}

inline void DriveBackup::disconnectAccount()
{
    storeSetting(QStringLiteral("drive_refresh_token"), QString());
    storeSetting(QStringLiteral("drive_access_expiry"), QString());
    m_accessToken.clear();
    m_accessExpiry = QDateTime();
    emit authResult(false, QStringLiteral("Cuenta de Google desconectada."));
}

// ---------------------------------------------------------------------------
inline void DriveBackup::connectAccount(const QString &clientId, const QString &clientSecret)
{
    const QString cid = clientId.trimmed();
    const QString csec = clientSecret.trimmed();
    if (cid.isEmpty() || csec.isEmpty()) {
        emit authResult(false, QStringLiteral("Escriba el Client ID y el Client Secret de su aplicación de escritorio de Google Cloud."));
        return;
    }
    if (m_loopback) {                       // conexión ya en curso: ignorar
        emit authResult(false, QStringLiteral("Ya hay una conexión en curso. Finalice el navegador abierto primero."));
        return;
    }
    m_loopback = new QTcpServer(this);
    if (!m_loopback->listen(QHostAddress::LocalHost, 0)) {
        m_loopback->deleteLater();
        m_loopback = nullptr;
        emit authResult(false, QStringLiteral("No se pudo abrir el puerto local temporal para la conexión."));
        return;
    }
    const quint16 port = m_loopback->serverPort();
    const QString redirect = QStringLiteral("http://127.0.0.1:%1").arg(port);

    // Guardar credenciales ANTES del intercambio (el redirect las necesita)
    storeSetting(QStringLiteral("drive_client_id"), cid);
    storeSetting(QStringLiteral("drive_client_secret"), csec);
    storeSetting(QStringLiteral("drive_refresh_token"), QString());

    QUrlQuery q;
    q.addQueryItem(QStringLiteral("client_id"), cid);
    q.addQueryItem(QStringLiteral("redirect_uri"), redirect);
    q.addQueryItem(QStringLiteral("response_type"), QStringLiteral("code"));
    q.addQueryItem(QStringLiteral("scope"),
                   QStringLiteral("https://www.googleapis.com/auth/drive.file"));
    q.addQueryItem(QStringLiteral("access_type"), QStringLiteral("offline"));
    q.addQueryItem(QStringLiteral("prompt"), QStringLiteral("consent"));
    QUrl authUrl(QStringLiteral("https://accounts.google.com/o/oauth2/v2/auth"));
    authUrl.setQuery(q);

    m_authTimeout.setSingleShot(true);
    connect(&m_authTimeout, &QTimer::timeout, this, [this]() {
        if (m_loopback) { m_loopback->close(); m_loopback->deleteLater(); m_loopback = nullptr; }
        emit authResult(false, QStringLiteral("Tiempo agotado: no se completó la autorización en el navegador."));
    });
    m_authTimeout.start(kAuthTimeoutMs);
    connect(m_loopback, &QTcpServer::newConnection, this, &DriveBackup::onLoopbackConnection);

    if (!QDesktopServices::openUrl(authUrl)) {
        m_authTimeout.stop();
        m_loopback->close();
        m_loopback->deleteLater();
        m_loopback = nullptr;
        emit authResult(false, QStringLiteral("No se pudo abrir el navegador. Copie manualmente esta dirección:\n%1")
                        .arg(authUrl.toString()));
        return;
    }
    qInfo() << "[Drive] Autorización OAuth abierta en el navegador (loopback" << port << ")";
}

inline void DriveBackup::onLoopbackConnection()
{
    QTcpSocket *sock = m_loopback->nextPendingConnection();
    if (!sock) return;
    sock->setParent(this);
    // El navegador envía "GET /?code=...&scope=... HTTP/1.1"
    connect(sock, &QTcpSocket::readyRead, this, [this, sock]() {
        const QByteArray req = sock->readAll();
        const int sp = req.indexOf(' ');
        const int eol = req.indexOf("\r\n");
        if (sp < 0 || eol < 0) { sock->disconnectFromHost(); return; }
        const QString target = QString::fromLatin1(req.mid(sp + 1, eol - sp - 1));
        QString code;
        if (target.startsWith(QLatin1Char('/'))) {
            const int qm = target.indexOf(QLatin1Char('?'));
            if (qm >= 0) {
                QUrlQuery query(target.mid(qm + 1));
                code = query.queryItemValue(QStringLiteral("code"));
            }
        }
        // Respuesta amigable (el usuario ve esta pestaña en el navegador)
        const QByteArray body =
            "<html><head><meta charset=\"utf-8\"><title>LuminaPresentation</title></head>"
            "<body style=\"font-family:Segoe UI,Arial;background:#10141c;color:#e8ecf4;"
            "display:flex;align-items:center;justify-content:center;height:100vh;margin:0\">"
            "<div style=\"text-align:center\"><h2>LuminaPresentation Suite</h2>"
            "<p id=\"s\">Procesando autorización…</p>"
            "<script>document.getElementById('s').textContent=" +
            (code.isEmpty()
                 ? QByteArrayLiteral("'No se recibió el código de autorización. Cierre e intente de nuevo.'")
                 : QByteArrayLiteral("'Autorización recibida. Puede cerrar esta pestaña y volver al programa.'")) +
            ";</script></div></body></html>";
        const QByteArray head =
            "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
            QByteArray("Content-Length: ") + QByteArray::number(body.size()) + "\r\n"
            "Connection: close\r\n\r\n";
        sock->write(head + body);
        sock->flush();
        sock->disconnectFromHost();

        m_authTimeout.stop();
        if (m_loopback) { m_loopback->close(); m_loopback->deleteLater(); m_loopback = nullptr; }

        if (code.isEmpty()) {
            emit authResult(false, QStringLiteral("El navegador no devolvió el código de autorización (¿canceló el permiso?)."));
            return;
        }
        // Intercambio code -> tokens
        tokenRequest(QVariantMap{
            { QStringLiteral("code"), code },
            { QStringLiteral("client_id"), storedSetting(QStringLiteral("drive_client_id")) },
            { QStringLiteral("client_secret"), storedSetting(QStringLiteral("drive_client_secret")) },
            { QStringLiteral("redirect_uri"),
              QStringLiteral("http://127.0.0.1:%1").arg(sock->localPort()) },
            { QStringLiteral("grant_type"), QStringLiteral("authorization_code") },
        }, [this](bool ok, const QJsonObject &tokens) {
            if (!ok || !tokens.contains(QStringLiteral("refresh_token"))) {
                storeSetting(QStringLiteral("drive_client_id"), QString());
                storeSetting(QStringLiteral("drive_client_secret"), QString());
                emit authResult(false, QStringLiteral("Google no entregó el token de refresco. Verifique Client ID/Secret y que la app esté publicada o en modo de prueba con su usuario agregado."));
                return;
            }
            storeSetting(QStringLiteral("drive_refresh_token"),
                         tokens.value(QStringLiteral("refresh_token")).toString());
            m_accessToken = tokens.value(QStringLiteral("access_token")).toString();
            m_accessExpiry = QDateTime::currentDateTimeUtc().addSecs(
                tokens.value(QStringLiteral("expires_in")).toInt() - 60);
            qInfo() << "[Drive] Cuenta conectada (token de refresco guardado)";
            emit authResult(true, QStringLiteral("Cuenta de Google conectada. Los respaldos usarán la carpeta de la aplicación en Drive."));
        });
    });
}

// ---------------------------------------------------------------------------
inline void DriveBackup::tokenRequest(const QVariantMap &params,
                                       const std::function<void(bool, const QJsonObject &)> &cb)
{
    QUrlQuery q;
    for (auto it = params.constBegin(); it != params.constEnd(); ++it)
        q.addQueryItem(it.key(), it.value().toString());
    QNetworkRequest req((QUrl(QStringLiteral("https://oauth2.googleapis.com/token"))));
    req.setHeader(QNetworkRequest::ContentTypeHeader,
                  QStringLiteral("application/x-www-form-urlencoded"));
    req.setTransferTimeout(kTimeoutMs);
    QNetworkReply *rep = m_nam.post(req, q.toString(QUrl::FullyEncoded).toUtf8());
    // ERR-9 (qt-cpp-review): registrar errores TLS (no se ignoran en ciego)
    connect(rep, &QNetworkReply::sslErrors, this, [rep](const QList<QSslError> &errs) {
        for (const QSslError &e : errs)
            qWarning() << "[Drive] TLS:" << e.errorString();
    });
    connect(rep, &QNetworkReply::finished, this, [rep, cb]() {
        rep->deleteLater();
        const QJsonObject obj = QJsonDocument::fromJson(rep->readAll()).object();
        const bool ok = rep->error() == QNetworkReply::NoError &&
                        obj.contains(QStringLiteral("access_token"));
        cb(ok, obj);
    });
}

inline void DriveBackup::ensureToken(const std::function<void(bool, const QString &)> &cb)
{
    if (!hasAccount()) {
        cb(false, QStringLiteral("No hay cuenta de Google conectada (ajústela en Comunicación)."));
        return;
    }
    if (!m_accessToken.isEmpty() && m_accessExpiry.isValid() &&
        QDateTime::currentDateTimeUtc() < m_accessExpiry) {
        cb(true, m_accessToken);
        return;
    }
    tokenRequest(QVariantMap{
        { QStringLiteral("client_id"), storedSetting(QStringLiteral("drive_client_id")) },
        { QStringLiteral("client_secret"), storedSetting(QStringLiteral("drive_client_secret")) },
        { QStringLiteral("refresh_token"), storedSetting(QStringLiteral("drive_refresh_token")) },
        { QStringLiteral("grant_type"), QStringLiteral("refresh_token") },
    }, [this, cb](bool ok, const QJsonObject &tokens) {
        if (!ok) {
            m_accessToken.clear();
            m_accessExpiry = QDateTime();
            cb(false, QStringLiteral("No se pudo renovar el acceso a Google (revocó el permiso o cambió la contraseña?). Vuelva a conectar la cuenta."));
            return;
        }
        m_accessToken = tokens.value(QStringLiteral("access_token")).toString();
        m_accessExpiry = QDateTime::currentDateTimeUtc().addSecs(
            tokens.value(QStringLiteral("expires_in")).toInt() - 60);
        cb(true, m_accessToken);
    });
}

// ---------------------------------------------------------------------------
inline void DriveBackup::callApi(const QString &method, const QUrl &url,
                                 const QByteArray &body, const QString &contentType,
                                 const std::function<void(bool, int, const QByteArray &)> &cb)
{
    ensureToken([this, method, url, body, contentType, cb](bool ok, const QString &token) {
        if (!ok) { cb(false, 0, QByteArray()); return; }
        QNetworkRequest req(url);
        req.setRawHeader(QByteArrayLiteral("Authorization"),
                         QStringLiteral("Bearer %1").arg(token).toLatin1());
        if (!contentType.isEmpty())
            req.setHeader(QNetworkRequest::ContentTypeHeader, contentType);
        req.setTransferTimeout(kTimeoutMs);
        QNetworkReply *rep = method == QStringLiteral("GET") ? m_nam.get(req)
                             : method == QStringLiteral("DELETE") ? m_nam.deleteResource(req)
                             : m_nam.sendCustomRequest(req, method.toLatin1(), body);
        connect(rep, &QNetworkReply::sslErrors, this, [rep](const QList<QSslError> &errs) {
            for (const QSslError &e : errs)
                qWarning() << "[Drive] TLS:" << e.errorString();
        });
        connect(rep, &QNetworkReply::finished, this, [rep, cb]() {
            rep->deleteLater();
            const int status = rep->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
            cb(rep->error() == QNetworkReply::NoError, status, rep->readAll());
        });
    });
}

// ---------------------------------------------------------------------------
inline void DriveBackup::backupNow(const QString &dbFilePath)
{
    ensureToken([this, dbFilePath](bool ok, const QString &token) {
        if (!ok) { emit uploadResult(false, QStringLiteral("Sin acceso a Google Drive.")); return; }
        uploadMultipart(token, dbFilePath);
    });
}

inline void DriveBackup::uploadMultipart(const QString &accessToken, const QString &dbFilePath)
{
    QFile f(dbFilePath);
    if (!f.open(QIODevice::ReadOnly)) {          // ERR: comprobar open()
        emit uploadResult(false, QStringLiteral("No se pudo leer el archivo de respaldo local."));
        return;
    }
    const QByteArray dbBytes = f.readAll();
    f.close();

    const QString name = QStringLiteral("lumina_backup_%1.db")
            .arg(QDateTime::currentDateTime().toString(QStringLiteral("yyyyMMdd_HHmm")));
    const QByteArray boundary = QByteArrayLiteral("lumiNa30N3xO5");
    const QByteArray meta = QStringLiteral(
        "{\"name\":\"%1\",\"parents\":[\"appDataFolder\"]}").arg(name).toUtf8();
    const QByteArray head =
        QByteArrayLiteral("--") + boundary + "\r\n"
        "Content-Type: application/json; charset=UTF-8\r\n\r\n" + meta + "\r\n"
        "--" + boundary + "\r\n"
        "Content-Type: application/octet-stream\r\n\r\n";
    const QByteArray tail = "\r\n--" + boundary + QByteArrayLiteral("--") + "\r\n";
    const QByteArray payload = head + dbBytes + tail;

    QUrl url(QStringLiteral(
        "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id"));
    QNetworkRequest req(url);
    req.setRawHeader(QByteArrayLiteral("Authorization"),
                     QStringLiteral("Bearer %1").arg(accessToken).toLatin1());
    req.setHeader(QNetworkRequest::ContentTypeHeader,
                  QStringLiteral("multipart/related; boundary=%1").arg(QString::fromLatin1(boundary)));
    req.setTransferTimeout(60000);              // .db puede ser grande
    QNetworkReply *rep = m_nam.post(req, payload);
    connect(rep, &QNetworkReply::sslErrors, this, [rep](const QList<QSslError> &errs) {
        for (const QSslError &e : errs)
            qWarning() << "[Drive] TLS:" << e.errorString();
    });
    connect(rep, &QNetworkReply::finished, this, [this, rep, accessToken, name]() {
        rep->deleteLater();
        if (rep->error() != QNetworkReply::NoError) {
            emit uploadResult(false, QStringLiteral("No se pudo subir el respaldo a Drive (%1).")
                              .arg(QString::number(int(rep->error()))));
            return;
        }
        qInfo() << "[Drive] Respaldo subido:" << name;
        rotateAfterUpload(accessToken);
        emit uploadResult(true, QStringLiteral("Respaldo subido a Google Drive: %1").arg(name));
    });
}

inline void DriveBackup::rotateAfterUpload(const QString &accessToken)
{
    QUrl url(QStringLiteral("https://www.googleapis.com/drive/v3/files"));
    QUrlQuery q;
    q.addQueryItem(QStringLiteral("spaces"), QStringLiteral("appDataFolder"));
    q.addQueryItem(QStringLiteral("orderBy"), QStringLiteral("createdTime desc"));
    q.addQueryItem(QStringLiteral("pageSize"), QString::number(kKeepBackups + 4));
    q.addQueryItem(QStringLiteral("fields"), QStringLiteral("files(id,name)"));
    url.setQuery(q);
    QNetworkRequest req(url);
    req.setRawHeader(QByteArrayLiteral("Authorization"),
                     QStringLiteral("Bearer %1").arg(accessToken).toLatin1());
    req.setTransferTimeout(kTimeoutMs);
    QNetworkReply *rep = m_nam.get(req);
    connect(rep, &QNetworkReply::finished, this, [this, rep]() {
        rep->deleteLater();
        if (rep->error() != QNetworkReply::NoError) return;
        const QJsonArray files = QJsonDocument::fromJson(rep->readAll())
                .object().value(QStringLiteral("files")).toArray();
        for (int i = kKeepBackups; i < files.size(); ++i) {
            const QString id = files.at(i).toObject().value(QStringLiteral("id")).toString();
            if (id.isEmpty()) continue;
            QNetworkRequest delReq(QUrl(QStringLiteral(
                "https://www.googleapis.com/drive/v3/files/%1").arg(id)));
            delReq.setRawHeader(QByteArrayLiteral("Authorization"),
                                QStringLiteral("Bearer %1")
                                    .arg(m_accessToken).toLatin1());
            delReq.setTransferTimeout(kTimeoutMs);
            QNetworkReply *del = m_nam.deleteResource(delReq);
            connect(del, &QNetworkReply::finished, del, &QObject::deleteLater);
            qInfo() << "[Drive] Rotación: eliminada copia antigua" << id;
        }
    });
}

// ---------------------------------------------------------------------------
inline void DriveBackup::listBackups()
{
    callApi(QStringLiteral("GET"),
            []() {
                QUrl url(QStringLiteral("https://www.googleapis.com/drive/v3/files"));
                QUrlQuery q;
                q.addQueryItem(QStringLiteral("spaces"), QStringLiteral("appDataFolder"));
                q.addQueryItem(QStringLiteral("orderBy"), QStringLiteral("createdTime desc"));
                q.addQueryItem(QStringLiteral("pageSize"), QStringLiteral("20"));
                q.addQueryItem(QStringLiteral("fields"), QStringLiteral("files(id,name,createdTime)"));
                url.setQuery(q);
                return url;
            }(),
            QByteArray(), QString(),
            [this](bool ok, int, const QByteArray &data) {
                QStringList names;
                if (ok) {
                    const QJsonArray files = QJsonDocument::fromJson(data)
                            .object().value(QStringLiteral("files")).toArray();
                    for (const QJsonValue &v : files)
                        names.append(v.toObject().value(QStringLiteral("name")).toString());
                }
                emit backupsListed(ok, ok ? QString() : QStringLiteral("No se pudo leer la lista de respaldos de Drive."), names);
            });
}

inline void DriveBackup::downloadBackup(const QString &fileName, const QString &destPath)
{
    // 1) resolver id a partir del nombre; 2) descargar alt=media
    callApi(QStringLiteral("GET"),
            []() {
                QUrl url(QStringLiteral("https://www.googleapis.com/drive/v3/files"));
                QUrlQuery q;
                q.addQueryItem(QStringLiteral("spaces"), QStringLiteral("appDataFolder"));
                q.addQueryItem(QStringLiteral("pageSize"), QStringLiteral("50"));
                q.addQueryItem(QStringLiteral("fields"), QStringLiteral("files(id,name)"));
                url.setQuery(q);
                return url;
            }(),
            QByteArray(), QString(),
            [this, fileName, destPath](bool ok, int, const QByteArray &data) {
                if (!ok) {
                    emit downloadResult(false, QStringLiteral("No se pudo consultar Drive para la descarga."), QString());
                    return;
                }
                QString id;
                const QJsonArray files = QJsonDocument::fromJson(data)
                        .object().value(QStringLiteral("files")).toArray();
                for (const QJsonValue &v : files) {
                    // fileName vacío = la MÁS RECIENTE (la lista ya viene
                    // ordenada por createdTime descendente).
                    if (fileName.isEmpty() ||
                        v.toObject().value(QStringLiteral("name")).toString() == fileName) {
                        id = v.toObject().value(QStringLiteral("id")).toString();
                        break;
                    }
                }
                if (id.isEmpty()) {
                    emit downloadResult(false, QStringLiteral("No se encontró en Drive el respaldo «%1».").arg(fileName), QString());
                    return;
                }
                QUrl media(QStringLiteral("https://www.googleapis.com/drive/v3/files/%1?alt=media").arg(id));
                callApi(QStringLiteral("GET"), media, QByteArray(), QString(),
                        [this, destPath](bool ok2, int, const QByteArray &bytes) {
                    if (!ok2) {
                        emit downloadResult(false, QStringLiteral("Falló la descarga desde Drive."), QString());
                        return;
                    }
                    QFile out(destPath);
                    if (!out.open(QIODevice::WriteOnly)) {   // ERR: comprobar open()
                        emit downloadResult(false, QStringLiteral("No se pudo escribir el archivo destino."), QString());
                        return;
                    }
                    out.write(bytes);
                    out.close();
                    emit downloadResult(true, QStringLiteral("Respaldo descargado. Puede restaurarlo con «Restaurar copia…»."), destPath);
                });
            });
}

#endif // LUMINA_DRIVEBACKUP_H
