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
#include <QMap>
#include <QSharedPointer>
#include <functional>

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
    static constexpr int kPerPage  = 100;       // v1.6.0 (M12): máximo real de la API v2
    static constexpr int kMaxItems = 200;       // tope de ítems agregados (4 páginas)
    static constexpr int kMaxPages = 10;        // v1.6.0 (M12): tope anti-bucle de paginación

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
    // v1.6.0 (M12): la API v2 limita per_page a 100 — antes se pedían 200 y
    // la respuesta se truncaba silenciosamente en planes largos. Ahora se
    // encadenan páginas siguiendo el cursor links.next (tope de seguridad de
    // kMaxPages) y itemsReady llega UNA sola vez con la lista agregada.
    void fetchPlanItems(const QString &planId)
    {
        auto items = QSharedPointer<QVector<PcoItem>>::create();
        auto songs = QSharedPointer<QMap<QString, QString>>::create();
        fetchItemsPage(QUrl(QStringLiteral(
                           "https://api.planningcenteronline.com/services/v2/plans/%1/items?include=song&per_page=%2")
                           .arg(planId).arg(kPerPage)),
                       planId, 0, items, songs);
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
    // v1.6.0 (M12): GET autenticado genérico por callbacks — get() lo usa con
    // emitResult() y la paginación de ítems lo encadena página a página.
    void getJson(const QUrl &url,
                 const std::function<void(const QJsonDocument &)> &onOk,
                 const std::function<void(const QString &)> &onError)
    {
        if (!hasToken()) {
            onError(QStringLiteral("Falta el token de acceso (ID y secreto de la aplicación)."));
            return;
        }
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
        connect(rep, &QNetworkReply::finished, this, [this, rep, onOk, onError]() {
            rep->deleteLater();
            if (rep->error() != QNetworkReply::NoError) {
                onError(friendlyError(rep));
                return;
            }
            // ERR-2 (qt-cpp-review): JSON de respuesta validado explícitamente
            const QJsonDocument doc = QJsonDocument::fromJson(rep->readAll());
            if (doc.isNull() || !doc.isObject()) {
                onError(QStringLiteral("Respuesta no válida del servidor de Planning Center."));
                return;
            }
            onOk(doc);
        });
    }

    void get(const QString &path, const QString &kind, const QString &planId = QString())
    {
        getJson(QUrl(QStringLiteral("https://api.planningcenteronline.com") + path),
                [this, kind, planId](const QJsonDocument &doc) {
                    emitResult(kind, planId, true, QString(), doc);
                },
                [this, kind, planId](const QString &err) {
                    emitResult(kind, planId, false, err, QJsonDocument());
                });
    }

    // v1.6.0 (M12): una página de la paginación de ítems. Los acumuladores se
    // comparten vía QSharedPointer para que cada fetchPlanItems lleve su
    // propio hilo de acumulación (aun si se solicitara otro en paralelo).
    void fetchItemsPage(const QUrl &url, const QString &planId, int page,
                        const QSharedPointer<QVector<PcoItem>> &items,
                        const QSharedPointer<QMap<QString, QString>> &songs)
    {
        getJson(url,
                [this, planId, page, items, songs](const QJsonDocument &doc) {
                    collectItemsPage(doc, songs, *items);
                    // Cursor: PCO entrega la página siguiente en links.next
                    // (URL completa o ruta relativa). Se encadena hasta que no
                    // haya más, con tope de páginas y del máximo de ítems.
                    const QString nextUrl = doc.object()
                            .value(QStringLiteral("links")).toObject()
                            .value(QStringLiteral("next")).toString();
                    if (page + 1 < kMaxPages && items->size() < kMaxItems &&
                        !nextUrl.isEmpty()) {
                        QUrl next(nextUrl);
                        if (next.scheme().isEmpty())
                            next = QUrl(QStringLiteral("https://api.planningcenteronline.com") + nextUrl);
                        fetchItemsPage(next, planId, page + 1, items, songs);
                        return;
                    }
                    emit itemsReady(true, QString(), planId, *items);
                },
                [this, planId](const QString &err) {
                    emit itemsReady(false, err, planId, QVector<PcoItem>());
                });
    }

    // v1.6.0 (M12): parseo de una página — las canciones «included» se
    // acumulan entre páginas (un ítem de la página N puede referenciar una
    // canción incluida en otra) y los ítems se añaden al acumulador.
    void collectItemsPage(const QJsonDocument &doc,
                          const QSharedPointer<QMap<QString, QString>> &songs,
                          QVector<PcoItem> &items) const
    {
        const QJsonArray included = doc.object()
                .value(QStringLiteral("included")).toArray();
        for (const QJsonValue &v : included) {
            const QJsonObject o = v.toObject();
            if (o.value(QStringLiteral("type")).toString() == QStringLiteral("Song")) {
                songs->insert(o.value(QStringLiteral("id")).toString(),
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
                it.songTitle = songs->value(songRel.value(QStringLiteral("id")).toString());
                if (it.songTitle.isEmpty()) it.songTitle = it.title;
            }
            if (!it.title.isEmpty() || it.isSong) items.append(it);
        }
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
        }
        // kind "items" ya no pasa por aquí (M12): se maneja por la paginación
        // fetchItemsPage/collectItemsPage y se emite al final con la lista
        // agregada completa.
    }

    QNetworkAccessManager m_nam;
    QString m_appId;
    QString m_appSecret;
};

#endif // LUMINA_PLANNINGCENTER_H
