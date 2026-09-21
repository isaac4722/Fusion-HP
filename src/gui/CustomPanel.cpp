// ============================================================================
//  LuminaPresentation Suite - CustomPanel.cpp
// ============================================================================
#include "CustomPanel.h"
#include "core/Database.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QFileDialog>
#include <QMessageBox>
#include <QLabel>
#include <QColorDialog>
#include <QJsonDocument>
#include <QDir>
#include <QDateTime>

CustomPanel::CustomPanel(AppContext *ctx, QWidget *parent)
    : QWidget(parent), m_ctx(ctx)
{
    buildUi();
    // Lista existente
    const auto slides = m_ctx->db->customSlides();
    for (const auto &s : slides) {
        auto *it = new QListWidgetItem(s.second);
        it->setData(Qt::UserRole, s.first);
        m_list->addItem(it);
    }
}

void CustomPanel::buildUi()
{
    auto *lay = new QHBoxLayout(this);
    lay->setContentsMargins(12, 12, 12, 12);

    // Izquierda: lista + gestion
    auto *left = new QVBoxLayout();
    left->addWidget(new QLabel(QStringLiteral("<b>Slides personalizadas</b>"), this));
    m_list = new QListWidget(this);
    connect(m_list, &QListWidget::currentRowChanged, this, [this](int) { onLoadSlide(); });
    left->addWidget(m_list, 1);
    m_name = new QLineEdit(this);
    m_name->setPlaceholderText(QStringLiteral("Nombre de la slide"));
    left->addWidget(m_name);
    auto *bNew = new QPushButton(QStringLiteral("➕ Nueva"), this);
    auto *bSave = new QPushButton(QStringLiteral("💾 Guardar"), this);
    auto *bDel = new QPushButton(QStringLiteral("🗑 Eliminar"), this);
    connect(bNew, &QPushButton::clicked, this, &CustomPanel::onNewSlide);
    connect(bSave, &QPushButton::clicked, this, &CustomPanel::onSaveSlide);
    connect(bDel, &QPushButton::clicked, this, &CustomPanel::onDeleteSlide);
    left->addWidget(bNew);
    left->addWidget(bSave);
    left->addWidget(bDel);
    lay->addLayout(left, 1);

    // Derecha: lienzo
    auto *right = new QVBoxLayout();
    auto *tools = new QHBoxLayout();
    m_canvas = new VectorCanvas(this);
    m_canvas->setMinimumHeight(420);

    auto *bText = new QPushButton(QStringLiteral("Texto"), this);
    auto *bRect = new QPushButton(QStringLiteral("Rectángulo"), this);
    auto *bRound = new QPushButton(QStringLiteral("Redondeada"), this);
    auto *bEllipse = new QPushButton(QStringLiteral("Elipse"), this);
    auto *bImage = new QPushButton(QStringLiteral("Imagen"), this);
    auto *bEdit = new QPushButton(QStringLiteral("Editar texto (doble clic)"), this);
    auto *bRaise = new QPushButton(QStringLiteral("▲ Subir"), this);
    auto *bLower = new QPushButton(QStringLiteral("▼ Bajar"), this);
    auto *bDelItem = new QPushButton(QStringLiteral("Borrar item"), this);
    connect(bText, &QPushButton::clicked, this, &CustomPanel::onAddText);
    connect(bRect, &QPushButton::clicked, this, [this]() { m_canvas->addShape(VectorCanvas::ShapeRect); });
    connect(bRound, &QPushButton::clicked, this, [this]() { m_canvas->addShape(VectorCanvas::ShapeRound); });
    connect(bEllipse, &QPushButton::clicked, this, [this]() { m_canvas->addShape(VectorCanvas::ShapeEllipse); });
    connect(bImage, &QPushButton::clicked, this, &CustomPanel::onAddImage);
    connect(bEdit, &QPushButton::clicked, m_canvas, &VectorCanvas::editSelectedText);
    connect(bRaise, &QPushButton::clicked, m_canvas, &VectorCanvas::raiseSelected);
    connect(bLower, &QPushButton::clicked, m_canvas, &VectorCanvas::lowerSelected);
    connect(bDelItem, &QPushButton::clicked, m_canvas, &VectorCanvas::deleteSelected);
    tools->addWidget(bText);
    tools->addWidget(bRect);
    tools->addWidget(bRound);
    tools->addWidget(bEllipse);
    tools->addWidget(bImage);
    tools->addWidget(bEdit);
    tools->addWidget(bRaise);
    tools->addWidget(bLower);
    tools->addWidget(bDelItem);
    tools->addStretch();
    right->addLayout(tools);
    right->addWidget(m_canvas, 1);


    auto *bProj = new QPushButton(QStringLiteral("▶ Proyectar esta slide"), this);
    bProj->setStyleSheet(QStringLiteral("QPushButton{background:#1E6FD9;color:white;font-weight:bold;padding:8px 16px;}"));
    connect(bProj, &QPushButton::clicked, this, &CustomPanel::onProject);
    // v1.2.0: "＋ A culto" para las slides del lienzo (antes este tipo de item
    // no podía añadirse ni ejecutarse desde la cola del culto).
    auto *bQueue = new QPushButton(QStringLiteral("＋ A culto"), this);
    connect(bQueue, &QPushButton::clicked, this, &CustomPanel::onAddToService);
    right->addWidget(bProj);
    right->addWidget(bQueue);
    lay->addLayout(right, 3);
}

void CustomPanel::onNewSlide()
{
    m_canvas->clearAll();
    m_name->clear();
    m_currentId = 0;
}

void CustomPanel::onSaveSlide()
{
    const QString name = m_name->text().trimmed();
    if (name.isEmpty()) {
        QMessageBox::information(this, QStringLiteral("Guardar"), QStringLiteral("Escribe un nombre para la slide."));
        return;
    }
    QJsonObject root;
    root["items"] = m_canvas->exportItems();
    if (m_currentId > 0) {
        m_ctx->db->updateCustomSlide(m_currentId, name, root);
    } else {
        m_currentId = m_ctx->db->addCustomSlide(name, root);
    }
    // refresca lista
    const QString cur = m_name->text();
    m_list->clear();
    const auto slides = m_ctx->db->customSlides();
    for (const auto &s : slides) {
        auto *it = new QListWidgetItem(s.second);
        it->setData(Qt::UserRole, s.first);
        m_list->addItem(it);
        if (s.first == m_currentId) m_list->setCurrentItem(it);
    }
    m_name->setText(cur);
}

void CustomPanel::onLoadSlide()
{
    auto *it = m_list->currentItem();
    if (!it) return;
    m_currentId = it->data(Qt::UserRole).toInt();
    m_name->setText(it->text());
    bool ok = false;
    const QJsonObject json = m_ctx->db->customSlideJson(m_currentId, &ok);
    if (ok) m_canvas->loadItems(json.value(QStringLiteral("items")).toArray());
}

void CustomPanel::onDeleteSlide()
{
    if (m_currentId <= 0) return;
    m_ctx->db->deleteCustomSlide(m_currentId);
    delete m_list->currentItem();
    m_canvas->clearAll();
    m_currentId = 0;
}

void CustomPanel::onAddText()
{
    QFont f(QStringLiteral("Arial"), 64);
    f.setBold(true);
    auto *item = m_canvas->addTextItem(QStringLiteral("Escribe aquí (doble clic)"), f, Qt::white);
    m_canvas->scenePtr()->clearSelection();
    item->setSelected(true);
}

void CustomPanel::onAddImage()
{
    const QString f = QFileDialog::getOpenFileName(this, QStringLiteral("Insertar imagen"),
                                                   QString(), QStringLiteral("Imágenes (*.png *.jpg *.jpeg *.webp)"));
    if (!f.isEmpty()) m_canvas->addImage(f);
}

Slide CustomPanel::currentSlide()
{
    Slide s;
    s.kind = Slide::Custom;
    s.title = m_name->text();
    s.mediaPath = QString::fromUtf8(QJsonDocument(
        QJsonObject{ { QStringLiteral("items"), m_canvas->exportItems() } }).toJson(QJsonDocument::Compact));
    return s;
}

void CustomPanel::onProject()
{
    // CORRECCION v1.2.0 (DEFECTO CRÍTICO): antes se emitía un Slide::Custom
    // cuyo JSON viajaba en mediaPath, pero NADIE renderiza ese tipo de slide
    // (ni el Renderer ni OutputWindow) -> "Proyectar esta slide" mostraba un
    // fondo vacío: la feature completa estaba muerta. Ahora el lienzo se
    // rasteriza a PNG (renderTo existe y jamás se llamaba) y se proyecta como
    // Slide::Image, ruta que el Renderer y la salida ya dibujan.
    if (m_canvas->scenePtr()->items().isEmpty()) {
        QMessageBox::information(this, QStringLiteral("Lienzo libre"),
                                 QStringLiteral("El lienzo está vacío: añade texto, formas o imágenes primero."));
        return;
    }
    const QPixmap pm = m_canvas->renderTo(QSize(1920, 1080));
    const QString png = QDir::tempPath() + QStringLiteral("/lumina_custom_%1.png")
                            .arg(QDateTime::currentMSecsSinceEpoch());
    if (!pm.save(png, "PNG")) {
        QMessageBox::warning(this, QStringLiteral("Lienzo libre"),
                             QStringLiteral("No se pudo rasterizar el lienzo."));
        return;
    }
    Slide s = currentSlide();          // conserva título y JSON de referencia
    s.kind = Slide::Image;
    s.mediaPath = png;
    emit requestProjectCustom(s);
}

void CustomPanel::onAddToService()
{
    // v1.2.0: persiste la slide antes de añadirla (si es nueva) y la registra
    // como item Custom en el culto activo.
    if (m_currentId <= 0)
        onSaveSlide();
    if (m_currentId > 0)
        emit requestAddCustomToService(m_currentId);
}

// v1.2.0: rasterizado de una slide guardada (para la cola del culto).
Slide CustomPanel::rasterizeCustomJson(const QJsonObject &itemsJson, const QString &title)
{
    Slide s;
    s.kind = Slide::Custom;
    s.title = title;
    VectorCanvas canvas;
    canvas.loadItems(itemsJson.value(QStringLiteral("items")).toArray());
    const QPixmap pm = canvas.renderTo(QSize(1920, 1080));
    const QString png = QDir::tempPath() + QStringLiteral("/lumina_custom_%1.png")
                            .arg(QDateTime::currentMSecsSinceEpoch());
    if (!pm.isNull() && pm.save(png, "PNG")) {
        s.kind = Slide::Image;
        s.mediaPath = png;
    }
    return s;
}
