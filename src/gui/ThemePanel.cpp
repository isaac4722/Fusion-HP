// ============================================================================
//  LuminaPresentation Suite - ThemePanel.cpp
// ============================================================================
#include "ThemePanel.h"
#include "core/Database.h"
#include "core/Renderer.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QFormLayout>
#include <QGroupBox>
#include <QFontDatabase>
#include <QFileDialog>
#include <QMessageBox>
#include <QColorDialog>

ThemePanel::ThemePanel(AppContext *ctx, QWidget *parent)
    : QWidget(parent), m_ctx(ctx)
{
    buildUi();
    // carga de lista
    const auto themes = m_ctx->db->themes();
    for (const auto &t : themes) {
        auto *it = new QListWidgetItem(t.second);
        it->setData(Qt::UserRole, t.first);
        m_list->addItem(it);
    }
    // CORRECCION v1.2.0 (DEFECTO CRÍTICO): la lista de temas NUNCA estaba
    // conectada — hacer clic en un tema no cargaba nada, el editor seguía
    // mostrando el anterior y "Guardar cambios" SOBRESCRIBÍA la plantilla
    // equivocada (corrupción de datos del usuario). Ahora la selección carga
    // el tema en el editor.
    connect(m_list, &QListWidget::currentRowChanged, this, &ThemePanel::onThemeSelected);
    if (m_list->count() > 0) {
        m_list->setCurrentRow(0);
        onThemeSelected(0);
    }
}

void ThemePanel::buildUi()
{
    auto *lay = new QHBoxLayout(this);
    lay->setContentsMargins(12, 12, 12, 12);

    // Izquierda: lista
    auto *left = new QVBoxLayout();
    left->addWidget(new QLabel(QStringLiteral("<b>Plantillas (Master Slides)</b>"), this));
    m_list = new QListWidget(this);
    left->addWidget(m_list, 1);
    auto *bDel = new QPushButton(QStringLiteral("Eliminar tema"), this);
    connect(bDel, &QPushButton::clicked, this, &ThemePanel::onDelete);
    left->addWidget(bDel);
    lay->addLayout(left, 1);

    // Centro: editor
    auto *ed = new QVBoxLayout();
    auto *grpBg = new QGroupBox(QStringLiteral("Fondo"), this);
    auto *fg = new QFormLayout(grpBg);
    m_name = new QLineEdit(grpBg);
    fg->addRow(QStringLiteral("Nombre:"), m_name);
    m_bgType = new QComboBox(grpBg);
    m_bgType->addItem(QStringLiteral("Color sólido"), 0);
    m_bgType->addItem(QStringLiteral("Gradiente"), 1);
    m_bgType->addItem(QStringLiteral("Imagen"), 2);
    m_bgType->addItem(QStringLiteral("Video (LibVLC, en bucle)"), 3);
    fg->addRow(QStringLiteral("Tipo:"), m_bgType);
    auto *colorRow = new QHBoxLayout();
    m_bColor1 = new QPushButton(QStringLiteral("Color 1"), grpBg);
    m_bColor2 = new QPushButton(QStringLiteral("Color 2"), grpBg);
    connect(m_bColor1, &QPushButton::clicked, this, &ThemePanel::onPickColor1);
    connect(m_bColor2, &QPushButton::clicked, this, &ThemePanel::onPickColor2);
    colorRow->addWidget(m_bColor1);
    colorRow->addWidget(m_bColor2);
    colorRow->addStretch();
    fg->addRow(QStringLiteral("Colores:"), colorRow);
    auto *imgRow = new QHBoxLayout();
    m_imagePath = new QLineEdit(grpBg);
    auto *bImg = new QPushButton(QStringLiteral("…"), grpBg);
    connect(bImg, &QPushButton::clicked, this, &ThemePanel::onPickImage);
    imgRow->addWidget(m_imagePath);
    imgRow->addWidget(bImg);
    fg->addRow(QStringLiteral("Imagen:"), imgRow);
    auto *vidRow = new QHBoxLayout();
    m_videoPath = new QLineEdit(grpBg);
    auto *bVid = new QPushButton(QStringLiteral("…"), grpBg);
    connect(bVid, &QPushButton::clicked, this, &ThemePanel::onPickVideo);
    vidRow->addWidget(m_videoPath);
    vidRow->addWidget(bVid);
    fg->addRow(QStringLiteral("Video fondo:"), vidRow);
    ed->addWidget(grpBg);

    auto *grpTxt = new QGroupBox(QStringLiteral("Tipografía y efectos"), this);
    auto *ft = new QFormLayout(grpTxt);
    m_fontTitle = new QComboBox(grpTxt);
    m_fontBody = new QComboBox(grpTxt);
    const QStringList fams = QFontDatabase().families();
    m_fontTitle->addItems(fams);
    m_fontBody->addItems(fams);
    m_fontTitle->setCurrentText(QStringLiteral("Arial"));
    m_fontBody->setCurrentText(QStringLiteral("Arial"));
    ft->addRow(QStringLiteral("Fuente título:"), m_fontTitle);
    ft->addRow(QStringLiteral("Fuente texto:"), m_fontBody);
    auto *szRow = new QHBoxLayout();
    m_sizeTitle = new QSpinBox(grpTxt); m_sizeTitle->setRange(12, 160); m_sizeTitle->setValue(44);
    m_sizeBody = new QSpinBox(grpTxt);  m_sizeBody->setRange(12, 160);  m_sizeBody->setValue(54);
    szRow->addWidget(new QLabel(QStringLiteral("Título"), grpTxt));
    szRow->addWidget(m_sizeTitle);
    szRow->addWidget(new QLabel(QStringLiteral("Cuerpo"), grpTxt));
    szRow->addWidget(m_sizeBody);
    szRow->addStretch();
    ft->addRow(QStringLiteral("Tamaños (pt):"), szRow);
    m_textColor = Qt::white;
    m_bTextColor = new QPushButton(QStringLiteral("Color del texto"), grpTxt);
    connect(m_bTextColor, &QPushButton::clicked, this, [this]() {
        const QColor c = QColorDialog::getColor(m_textColor, this, QStringLiteral("Color de texto"));
        if (c.isValid()) { m_textColor = c; onFieldChanged(); }
    });
    ft->addRow(QStringLiteral("Texto:"), m_bTextColor);
    m_shadow = new QCheckBox(QStringLiteral("Sombra"), grpTxt);
    m_shadow->setChecked(true);
    m_outline = new QCheckBox(QStringLiteral("Contorno"), grpTxt);
    ft->addRow(m_shadow, m_outline);
    ed->addWidget(grpTxt);

    auto *grpBox = new QGroupBox(QStringLiteral("Caja de texto (posición/altura del bloque)"), this);
    auto *fb = new QVBoxLayout(grpBox);
    m_boxTop = new QSlider(Qt::Horizontal, grpBox);
    m_boxTop->setRange(5, 60); m_boxTop->setValue(28);
    m_boxHeight = new QSlider(Qt::Horizontal, grpBox);
    m_boxHeight->setRange(20, 85); m_boxHeight->setValue(60);
    fb->addWidget(new QLabel(QStringLiteral("Inicio vertical (%)"), grpBox));
    fb->addWidget(m_boxTop);
    fb->addWidget(new QLabel(QStringLiteral("Altura (%)"), grpBox));
    fb->addWidget(m_boxHeight);
    ed->addWidget(grpBox);

    // Conecta todos los cambios
    connect(m_bgType, qOverload<int>(&QComboBox::currentIndexChanged), this, &ThemePanel::onFieldChanged);
    connect(m_fontTitle, qOverload<int>(&QComboBox::currentIndexChanged), this, &ThemePanel::onFieldChanged);
    connect(m_fontBody, qOverload<int>(&QComboBox::currentIndexChanged), this, &ThemePanel::onFieldChanged);
    connect(m_sizeTitle, qOverload<int>(&QSpinBox::valueChanged), this, &ThemePanel::onFieldChanged);
    connect(m_sizeBody, qOverload<int>(&QSpinBox::valueChanged), this, &ThemePanel::onFieldChanged);
    connect(m_shadow, &QCheckBox::toggled, this, &ThemePanel::onFieldChanged);
    connect(m_outline, &QCheckBox::toggled, this, &ThemePanel::onFieldChanged);
    connect(m_boxTop, &QSlider::valueChanged, this, &ThemePanel::onFieldChanged);
    connect(m_boxHeight, &QSlider::valueChanged, this, &ThemePanel::onFieldChanged);

    auto *btnRow = new QHBoxLayout();
    auto *bSave = new QPushButton(QStringLiteral("💾 Guardar cambios"), this);
    auto *bSaveNew = new QPushButton(QStringLiteral("Guardar como nuevo"), this);
    connect(bSave, &QPushButton::clicked, this, &ThemePanel::onSave);
    connect(bSaveNew, &QPushButton::clicked, this, &ThemePanel::onSaveAsNew);
    btnRow->addWidget(bSave);
    btnRow->addWidget(bSaveNew);
    btnRow->addStretch();
    ed->addLayout(btnRow);
    ed->addStretch();
    lay->addLayout(ed, 2);

    // Derecha: preview
    auto *right = new QVBoxLayout();
    m_preview = new QLabel(this);
    m_preview->setMinimumSize(420, 260);
    m_preview->setAlignment(Qt::AlignCenter);
    m_preview->setObjectName(QStringLiteral("NextPreview"));   // v1.3.0 Aurora
    right->addWidget(m_preview, 1);
    lay->addLayout(right, 2);
}

void ThemePanel::loadFromTheme(const Theme &t)
{
    m_selectedThemeId = t.id;
    // CORRECCION v1.2.0: los setValue()/setCurrentIndex() disparan señales
    // conectadas a onFieldChanged(), que emitía themeChanged con un estado
    // "híbrido" a mitad de carga (medio tema viejo + medio nuevo aplicado al
    // proyector). Se bloquean las señales durante la carga y se emite UNA vez
    // al final con el estado completo.
    {
        QSignalBlocker b1(m_name);        QSignalBlocker b2(m_bgType);
        QSignalBlocker b3(m_imagePath);   QSignalBlocker b4(m_videoPath);
        QSignalBlocker b5(m_fontTitle);   QSignalBlocker b6(m_fontBody);
        QSignalBlocker b7(m_sizeTitle);   QSignalBlocker b8(m_sizeBody);
        QSignalBlocker b9(m_shadow);      QSignalBlocker b10(m_outline);
        QSignalBlocker b11(m_boxTop);     QSignalBlocker b12(m_boxHeight);
        m_name->setText(t.name);
        m_bgType->setCurrentIndex(t.background.type);
        m_color1 = t.background.color1;
        m_color2 = t.background.color2;
        m_textColor = t.body.color;
        m_imagePath->setText(t.background.imagePath);
        m_videoPath->setText(t.background.videoPath);
        m_fontTitle->setCurrentText(t.title.family);
        m_fontBody->setCurrentText(t.body.family);
        m_sizeTitle->setValue(t.title.pointSize);
        m_sizeBody->setValue(t.body.pointSize);
        m_shadow->setChecked(t.body.shadow);
        m_outline->setChecked(t.body.outlineWidth > 0);
        m_boxTop->setValue(int(t.bodyBox.y() * 100));
        m_boxHeight->setValue(int(t.bodyBox.height() * 100));
    }
    onFieldChanged();
}

Theme ThemePanel::collectTheme() const
{
    // Base: tema actual en DB o por defecto
    Theme t = (m_selectedThemeId > 0) ? m_ctx->db->themeById(m_selectedThemeId) : Theme::defaultTheme();
    t.id = m_selectedThemeId;
    t.name = m_name->text().isEmpty() ? QStringLiteral("Sin nombre") : m_name->text();
    t.background.type = m_bgType->currentIndex();
    t.background.color1 = m_color1;
    t.background.color2 = m_color2;
    t.background.imagePath = m_imagePath->text();
    t.background.videoPath = m_videoPath->text();
    t.title.family = m_fontTitle->currentText();
    t.body.family = m_fontBody->currentText();
    t.title.pointSize = m_sizeTitle->value();
    t.body.pointSize = m_sizeBody->value();
    t.title.color = m_textColor;
    t.body.color = m_textColor;
    t.body.shadow = m_shadow->isChecked();
    t.title.shadow = m_shadow->isChecked();
    t.body.outlineWidth = m_outline->isChecked() ? 2 : 0;
    t.title.outlineWidth = m_outline->isChecked() ? 2 : 0;
    t.bodyBox = QRectF(0.08, m_boxTop->value() / 100.0, 0.84, m_boxHeight->value() / 100.0);
    return t;
}

void ThemePanel::onThemeSelected(int row)
{
    if (row < 0) return;
    const int id = m_list->item(row)->data(Qt::UserRole).toInt();
    const Theme t = m_ctx->db->themeById(id);
    loadFromTheme(t);
}

void ThemePanel::onFieldChanged()
{
    // Refresca botones de color + preview en vivo
    m_bColor1->setStyleSheet(QStringLiteral("background:%1;").arg(m_color1.name()));
    m_bColor2->setStyleSheet(QStringLiteral("background:%2;").arg(m_color2.name()));
    const Theme t = collectTheme();
    Slide s;
    s.kind = Slide::Text;
    s.title = QStringLiteral("EJEMPLO — Grandes son tus obras");
    s.lines.append(SlideLine(QStringLiteral("Grande es el Señor y digno de suprema alabanza")));
    s.lines.append(SlideLine(QStringLiteral("En la ciudad de nuestro Dios")));
    s.refLabel = QStringLiteral("Salmo 48:1");
    const QPixmap pm = Renderer::render(t, s, m_preview->size());
    m_preview->setPixmap(pm.scaled(m_preview->size(), Qt::KeepAspectRatio, Qt::SmoothTransformation));
    emit themeChanged(t);
}

void ThemePanel::onPickColor1()
{
    const QColor c = QColorDialog::getColor(m_color1, this);
    if (c.isValid()) { m_color1 = c; onFieldChanged(); }
}

void ThemePanel::onPickColor2()
{
    const QColor c = QColorDialog::getColor(m_color2, this);
    if (c.isValid()) { m_color2 = c; onFieldChanged(); }
}

void ThemePanel::onPickImage()
{
    const QString f = QFileDialog::getOpenFileName(this, QStringLiteral("Imagen de fondo"),
                                                   QString(), QStringLiteral("Imágenes (*.png *.jpg *.jpeg *.webp)"));
    if (!f.isEmpty()) { m_imagePath->setText(f); m_bgType->setCurrentIndex(2); onFieldChanged(); }
}

void ThemePanel::onPickVideo()
{
    const QString f = QFileDialog::getOpenFileName(this, QStringLiteral("Video de fondo"),
                                                   QString(), QStringLiteral("Videos (*.mp4 *.avi *.mkv *.mov *.webm)"));
    if (!f.isEmpty()) { m_videoPath->setText(f); m_bgType->setCurrentIndex(3); onFieldChanged(); }
}

void ThemePanel::onSave()
{
    Theme t = collectTheme();
    if (t.id > 0 && m_ctx->db->saveTheme(t)) {
        if (m_list->currentItem()) m_list->currentItem()->setText(t.name);
        emit themeChanged(t);
    }
}

void ThemePanel::onSaveAsNew()
{
    Theme t = collectTheme();
    t.id = 0;
    t.name += QStringLiteral(" (nuevo)");
    // CORRECCION v1.2.0: saveTheme() devolvía bool — el id del tema nuevo se
    // perdía, m_selectedThemeId quedaba en 0 y el siguiente "Guardar cambios"
    // fallaba EN SILENCIO. Ahora saveTheme() devuelve el id real.
    t.id = m_ctx->db->saveTheme(t);
    if (t.id > 0) {
        const auto themes = m_ctx->db->themes();
        m_list->clear();
        int newRow = 0;
        for (const auto &th : themes) {
            auto *it = new QListWidgetItem(th.second);
            it->setData(Qt::UserRole, th.first);
            m_list->addItem(it);
            if (th.first == t.id) newRow = m_list->count() - 1;
        }
        m_list->setCurrentRow(newRow);
        m_selectedThemeId = t.id;
        emit themeChanged(t);
    }
}

void ThemePanel::onDelete()
{
    if (m_selectedThemeId <= 0) return;
    if (QMessageBox::question(this, QStringLiteral("Eliminar tema"),
                              QStringLiteral("¿Eliminar la plantilla seleccionada?")) == QMessageBox::Yes) {
        m_ctx->db->deleteTheme(m_selectedThemeId);
        m_list->clear();
        const auto themes = m_ctx->db->themes();
        for (const auto &t : themes) {
            auto *it = new QListWidgetItem(t.second);
            it->setData(Qt::UserRole, t.first);
            m_list->addItem(it);
        }
        if (m_list->count() > 0) { m_list->setCurrentRow(0); onThemeSelected(0); }
    }
}
