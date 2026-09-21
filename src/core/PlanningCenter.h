// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  PlanningCenter.h : Sincronización con Planning Center Online (spec §3.3).
//  Cliente de la API v2 (https://api.planningcenteronline.com/services/v2)
//  con Personal Access Token (PAT) "id:secret" del usuario (HTTP Basic).
//  Flujo: fetchServiceTypes -> fetchPlans(futuros) -> fetchPlanItems
//  (con include=song) -> importación a la cola del culto (MainWindow).
//  Todas las respuestas llegan como señales; errores en lenguaje claro.
// ============================================================================
#ifndef LUMINA_PLANNINGCENTER_H
#define LUMINA_PLANNINGCENTER_H

#include <QObject>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QNetworkRequest>
#include <QJsonDocument>
#include <QJsonObject>
#include <QJsonArray>
#include <QUrl>
#include <QUrlQuery>
#include <QDateTime>

struct PcoServiceType
{
    QString id;
    QString name;
};

struct PcoPlan
{
    QString id;
    QString serviceTypeId;
    QString title;
    QString dates;          // p.ej. "Sep 27, 2026"
    QString sortDate;       // ISO — para ordenar
};

struct PcoItem
{
    QString title;          // título del ítem del plan
    QString description;    // notas/descripción del ítem
    QString songTitle;      // título de la canción PCO (si es ítem de tipo song)
    bool    isSong = false;
    int     sequence = 0;   // orden original dentro del plan
};

class PlanningCenter : public QObject
{
    Q_OBJECT
public:
    static constexpr int kTimeoutMs = 15000;    // qt-cpp-review ERR: timeouts
    static constexpr int kMaxPlans = 15;
    static constexpr int kMaxItems = 200;

    explicit PlanningCenter(QObject *parent = nullptr) : QObject(parent) {}

    void setToken(const QString &appId, const QString &appSecret)
    {
        m_appId = appId.trimmed();
        m_appSecret = appSecret.trimmed();
    }

    bool hasToken() const { return !m_appId.isEmpty() && !m_appSecret.isEmpty(); }

    // Paso 1: lista de tipos de servicio (ministerios) de la organización.
    void fetchServiceTypes()
    {
        get(QStringLiteral("/services/v2/service_types?per_page=100"),
            QStringLiteral("tipos"));
    }

    // Paso 2: planes FUTUROS de un tipo de servicio (los 15 más próximos).
    void fetchPlans(const QString &serviceTypeId)
    {
        get(QStringLiteral("/services/v2/service_types/%1/plans?filter=future&order=sort_date&per_page=%2")
                .arg(serviceTypeId).arg(kMaxPlans),
            QStringLiteral("planes"));
    }

    // Paso 3: ítems de un plan (incluye las canciones relacionadas).
    void fetchPlanItems(const QString &planId)
    {
        get(QStringLiteral("/services/v2/plans/%1/items?include=song&per_page=%2")
                .arg(planId).arg(kMaxItems),
            QStringLiteral("items"),
            planId);
    }

signals:
    // ok=false -> error en lenguaje no técnico (spec §2.6)
    void serviceTypesReady(bool ok, const QString &error,
                           const QVector<PcoServiceType> &types);
    void plansReady(bool ok, const QString &error,
                    const QVector<PcoPlan> &plans);
    void itemsReady(bool ok, const QString &error, const QString &planId,
                    const QVector<PcoItem> &items);

private:
    void get(const QString &path, const QString &kind, const QString &planId = QString())
    {
        if (!hasToken()) {
            emitResult(kind, planId, false,
                       QStringLiteral("Falta el token de acceso (ID y secreto de la aplicación)."),
                       QJsonDocument());
            return;
        }
        QUrl url(QStringLiteral("https://api.planningcenteronline.com") + path);
        // QUrlQuery no aplica: path ya viene codificado y sin query dinámica.
        QNetworkRequest req(url);
        req.setRawHeader(QByteArrayLiteral("Authorization"),
                         QStringLiteral("Basic %1")
                             .arg(QString::fromLatin1(basicAuth().toBase64()))
                             .toLatin1());
        req.setTransferTimeout(kTimeoutMs);
        req.setAttribute(QNetworkRequest::RedirectPolicyAttribute,
                         QNetworkRequest::NoLessSafeRedirectPolicy);
        QNetworkReply *rep = m_nam.get(req);
        // ERR-9 (qt-cpp-review): visibilidad de errores TLS — se registran y
        // la petición falla por su causa natural (NO se ignoran a ciegas).
        connect(rep, &QNetworkReply::sslErrors, this, [rep](const QList<QSslError> &errs) {
            for (const QSslError &e : errs)
                qWarning() << "[PCO] TLS:" << e.errorString();
        });
        connect(rep, &QNetworkReply::finished, this, [this, rep, kind, planId]() {
            rep->deleteLater();
            if (rep->error() != QNetworkReply::NoError) {
                emitResult(kind, planId, false, friendlyError(rep), QJsonDocument());
                return;
            }
            // ERR-2 (qt-cpp-review): JSON de respuesta validado explícitamente
            const QJsonDocument doc = QJsonDocument::fromJson(rep->readAll());
            if (doc.isNull() || !doc.isObject()) {
                emitResult(kind, planId, false,
                           QStringLiteral("Respuesta no válida del servidor de Planning Center."),
                           QJsonDocument());
                return;
            }
            emitResult(kind, planId, true, QString(), doc);
        });
    }

    QByteArray basicAuth() const
    {
        return QStringLiteral("%1:%2").arg(m_appId, m_appSecret).toLatin1();
    }

    QString friendlyError(QNetworkReply *rep) const
    {
        switch (rep->error()) {
        case QNetworkReply::AuthenticationRequiredError:
            return QStringLiteral("Token rechazado: revise el ID y el secreto de la aplicación.");
        case QNetworkReply::HostNotFoundError:
            return QStringLiteral("No se encontró el servidor (sin conexión a Internet).");
        case QNetworkReply::TimeoutError:
        case QNetworkReply::OperationCanceledError:
            return QStringLiteral("El servidor tardó demasiado en responder (tiempo agotado).");
        case QNetworkReply::SslHandshakeFailedError:
            return QStringLiteral("No se pudo establecer la conexión segura con Planning Center.");
        case QNetworkReply::ContentNotFoundError:
            return QStringLiteral("El plan o recurso solicitado ya no existe en Planning Center.");
        default:
            return QStringLiteral("Error de red (%1).").arg(QString::number(int(rep->error())));
        }
    }

    void emitResult(const QString &kind, const QString &planId, bool ok,
                    const QString &error, const QJsonDocument &doc)
    {
        if (kind == QStringLiteral("tipos")) {
            QVector<PcoServiceType> types;
            if (ok) {
                const QJsonArray data = doc.object()
                        .value(QStringLiteral("data")).toArray();
                for (const QJsonValue &v : data) {
                    const QJsonObject o = v.toObject();
                    PcoServiceType t;
                    t.id = o.value(QStringLiteral("id")).toString();
                    t.name = o.value(QStringLiteral("attributes")).toObject()
                                 .value(QStringLiteral("name")).toString();
                    if (!t.id.isEmpty()) types.append(t);
                }
            }
            emit serviceTypesReady(ok, error, types);
        } else if (kind == QStringLiteral("planes")) {
            QVector<PcoPlan> plans;
            if (ok) {
                const QJsonArray data = doc.object()
                        .value(QStringLiteral("data")).toArray();
                for (const QJsonValue &v : data) {
                    const QJsonObject o = v.toObject();
                    const QJsonObject a = o.value(QStringLiteral("attributes")).toObject();
                    PcoPlan p;
                    p.id = o.value(QStringLiteral("id")).toString();
                    p.title = a.value(QStringLiteral("title")).toString();
                    p.dates = a.value(QStringLiteral("short_dates")).toString();
                    if (p.dates.isEmpty())
                        p.dates = a.value(QStringLiteral("sort_date")).toString();
                    p.sortDate = a.value(QStringLiteral("sort_date")).toString();
                    if (!p.id.isEmpty()) plans.append(p);
                }
            }
            emit plansReady(ok, error, plans);
        } else if (kind == QStringLiteral("items")) {
            QVector<PcoItem> items;
            if (ok) {
                // Mapa id->título de las canciones incluidas (include=song)
                const QJsonArray included = doc.object()
                        .value(QStringLiteral("included")).toArray();
                QMap<QString, QString> songs;
                for (const QJsonValue &v : included) {
                    const QJsonObject o = v.toObject();
                    if (o.value(QStringLiteral("type")).toString() == QStringLiteral("Song")) {
                        songs.insert(o.value(QStringLiteral("id")).toString(),
                                     o.value(QStringLiteral("attributes")).toObject()
                                      .value(QStringLiteral("title")).toString());
                    }
                }
                const QJsonArray data = doc.object()
                        .value(QStringLiteral("data")).toArray();
                for (const QJsonValue &v : data) {
                    const QJsonObject o = v.toObject();
                    const QJsonObject a = o.value(QStringLiteral("attributes")).toObject();
                    PcoItem it;
                    it.title = a.value(QStringLiteral("title")).toString();
                    it.description = a.value(QStringLiteral("description")).toString();
                    it.sequence = a.value(QStringLiteral("sequence")).toInt();
                    const QJsonObject songRel = o.value(QStringLiteral("relationships"))
                            .toObject().value(QStringLiteral("song")).toObject()
                            .value(QStringLiteral("data")).toObject();
                    if (!songRel.isEmpty()) {
                        it.isSong = true;
                        it.songTitle = songs.value(songRel.value(QStringLiteral("id")).toString());
                        if (it.songTitle.isEmpty()) it.songTitle = it.title;
                    }
                    if (!it.title.isEmpty() || it.isSong) items.append(it);
                }
            }
            emit itemsReady(ok, error, planId, items);
        }
    }

    QNetworkAccessManager m_nam;
    QString m_appId;
    QString m_appSecret;
};

#endif // LUMINA_PLANNINGCENTER_H
