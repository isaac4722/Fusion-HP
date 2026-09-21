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
#include <QCompleter>
#include <QFileInfo>
#include <QDateTime>
#include <QRegularExpression>
#include <QStyle>

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
    // v1.4.0: biblioteca de fondos + completador de etiquetas (todos los
    // tags conocidos del vault: canciones, temas y fondos).
    refreshMediaList();
    const QStringList known = m_ctx->db->allTagNames();
    if (!known.isEmpty()) {
        auto *comp = new QCompleter(known, this);
        comp->setCaseSensitivity(Qt::CaseInsensitive);
        m_themeTags->setCompleter(comp);
        m_mediaTags->setCompleter(comp);
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
    // v1.4.0 — etiquetas del tema (spec Holyrics: tags en temas/fondos/
    // videos/canciones; base de la automatización semántica).
    m_themeTags = new QLineEdit(grpBg);
    m_themeTags->setPlaceholderText(QStringLiteral("lento, adoración, navidad…"));
    m_themeTags->setToolTip(QStringLiteral("Etiquetas separadas por coma. Las reglas de automatización (Comunicación) pueden aplicar este tema automáticamente cuando una canción lleve una de estas etiquetas."));
    fg->addRow(QStringLiteral("Etiquetas:"), m_themeTags);
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

    // Derecha: preview + accesibilidad + biblioteca de fondos
    auto *right = new QVBoxLayout();
    m_preview = new QLabel(this);
    m_preview->setMinimumSize(420, 200);
    m_preview->setAlignment(Qt::AlignCenter);
    m_preview->setObjectName(QStringLiteral("NextPreview"));   // v1.3.0 Aurora
    right->addWidget(m_preview, 1);

    // v1.4.0 — Comprobador de accesibilidad (spec PowerPoint: Accessibility
    // Checker con clasificación Error/Advertencia/Correcto). Calcula el ratio
    // de contraste WCAG del texto contra el fondo del tema EN VIVO, con cada
    // cambio de color/tipo de fondo.
    auto *grpAccess = new QGroupBox(QStringLiteral("Accesibilidad (WCAG 2.x)"), this);
    auto *alay = new QVBoxLayout(grpAccess);
    m_accessTitle = new QLabel(grpAccess);
    m_accessBody = new QLabel(grpAccess);
    m_accessTitle->setProperty("class", QStringLiteral("chip"));   // Aurora tokens
    m_accessBody->setProperty("class", QStringLiteral("chip"));
    alay->addWidget(m_accessTitle);
    alay->addWidget(m_accessBody);
    auto *accessNote = new QLabel(QStringLiteral(
        "Umbral AA: 4.5:1 texto normal · 3:1 texto grande (≥24pt). "
        "Con sombra o contorno la legibilidad real mejora; el ratio se mide "
        "sobre los colores puros."), grpAccess);
    accessNote->setWordWrap(true);
    accessNote->setProperty("class", QStringLiteral("note"));
    alay->addWidget(accessNote);
    right->addWidget(grpAccess);

    // v1.4.0 — Biblioteca de fondos por etiqueta (spec Holyrics: "buscar la
    // etiqueta 'agua' y ver una galería de todos los recursos").
    auto *grpLib = new QGroupBox(QStringLiteral("Biblioteca de fondos (por etiqueta)"), this);
    auto *llay = new QVBoxLayout(grpLib);
    m_mediaFilter = new QLineEdit(grpLib);
    m_mediaFilter->setPlaceholderText(QStringLiteral("Filtrar por etiqueta (vacío = todos)…"));
    connect(m_mediaFilter, &QLineEdit::textChanged, this, [this]() { refreshMediaList(); });
    llay->addWidget(m_mediaFilter);
    m_mediaList = new QListWidget(grpLib);
    m_mediaList->setMinimumHeight(110);
    connect(m_mediaList, &QListWidget::itemSelectionChanged, this, &ThemePanel::onMediaSelected);
    connect(m_mediaList, &QListWidget::itemDoubleClicked, this, [this](QListWidgetItem *) { onUseBackground(); });
    llay->addWidget(m_mediaList, 1);
    m_mediaTags = new QLineEdit(grpLib);
    m_mediaTags->setPlaceholderText(QStringLiteral("Etiquetas del fondo seleccionado (coma)…"));
    llay->addWidget(m_mediaTags);
    auto *libRow1 = new QHBoxLayout();
    auto *bAddImg = new QPushButton(QStringLiteral("＋ Imágenes"), grpLib);
    auto *bAddVid = new QPushButton(QStringLiteral("＋ Videos"), grpLib);
    connect(bAddImg, &QPushButton::clicked, this, [this]() { onAddBackgrounds(false); });
    connect(bAddVid, &QPushButton::clicked, this, [this]() { onAddBackgrounds(true); });
    libRow1->addWidget(bAddImg);
    libRow1->addWidget(bAddVid);
    libRow1->addStretch();
    llay->addLayout(libRow1);
    auto *libRow2 = new QHBoxLayout();
    auto *bUse = new QPushButton(QStringLiteral("Usar como fondo"), grpLib);
    bUse->setProperty("class", QStringLiteral("primary"));
    auto *bSaveTags = new QPushButton(QStringLiteral("Guardar etiquetas"), grpLib);
    auto *bRemove = new QPushButton(QStringLiteral("Quitar"), grpLib);
    connect(bUse, &QPushButton::clicked, this, &ThemePanel::onUseBackground);
    connect(bSaveTags, &QPushButton::clicked, this, &ThemePanel::onSaveMediaTags);
    connect(bRemove, &QPushButton::clicked, this, &ThemePanel::onRemoveBackground);
    libRow2->addWidget(bUse);
    libRow2->addWidget(bSaveTags);
    libRow2->addWidget(bRemove);
    llay->addLayout(libRow2);
    right->addWidget(grpLib, 1);

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
        QSignalBlocker b13(m_themeTags);  // v1.4.0: no dispara onFieldChanged a mitad de carga
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
        // v1.4.0: etiquetas del tema desde el vault
        m_themeTags->setText(m_selectedThemeId > 0
                                 ? m_ctx->db->themeTags(m_selectedThemeId).join(QStringLiteral(", "))
                                 : QString());
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
    refreshAccessibility();     // v1.4.0: ratios WCAG con cada cambio
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
        // v1.4.0: las etiquetas se guardan junto al tema (reemplazo total).
        m_ctx->db->setThemeTags(
            t.id, m_themeTags->text().split(QRegularExpression(QStringLiteral("\\s*[,;]\\s*")),
                                             Qt::SkipEmptyParts));
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
        // v1.4.0: el duplicado hereda las etiquetas del tema origen.
        m_ctx->db->setThemeTags(
            t.id, m_themeTags->text().split(QRegularExpression(QStringLiteral("\\s*[,;]\\s*")),
                                             Qt::SkipEmptyParts));
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

// ---------------------------------------------------------------------------
// v1.4.0 — Comprobador de accesibilidad (spec PowerPoint, WCAG 2.x)
// ---------------------------------------------------------------------------
// Clasificación estilo Accessibility Checker: Correcto / Advertencia / Error.
// Contra fondo sólido usa color1; contra gradiente evalúa color1 y color2 y
// reporta el PEOR caso (conservador); con imagen/video no es verificable por
// color → advertencia con recomendación (los píxeles reales varían).
void ThemePanel::refreshAccessibility()
{
    if (!m_accessTitle || !m_accessBody) return;
    const Theme t = collectTheme();

    auto setChip = [](QLabel *lbl, const QString &text, const char *cls) {
        lbl->setText(text);
        lbl->setProperty("state", QString::fromLatin1(cls));
        // repolish: el QSS solo reevalúa pseudo-estados al repolir
        lbl->style()->unpolish(lbl);
        lbl->style()->polish(lbl);
    };

    if (t.background.type >= 2) {
        setChip(m_accessTitle, QStringLiteral("⚠ Título: fondo de imagen/video — verificar contraste a ojo"),
                "warn");
        setChip(m_accessBody, QStringLiteral("⚠ Cuerpo: fondo de imagen/video — usar sombra/contorno"),
                "warn");
        return;
    }

    // Fondo de referencia: el color que MENOS contrasta (peor caso).
    const QColor bg = (t.background.type == 1 &&
                       Renderer::contrastRatio(m_textColor, m_color2) <
                       Renderer::contrastRatio(m_textColor, m_color1))
                           ? m_color2
                           : m_color1;
    const qreal ratio = Renderer::contrastRatio(m_textColor, bg);
    const Renderer::ContrastLevel lvlTitle = Renderer::contrastLevel(ratio, true);   // 44pt = grande
    const Renderer::ContrastLevel lvlBody  = Renderer::contrastLevel(ratio, true);   // 54pt = grande

    const QString ratioTxt = QString::number(ratio, 'f', 2);
    if (lvlTitle == Renderer::ContrastLevel::PassAA) {
        setChip(m_accessTitle, QStringLiteral("✓ Título: %1:1 — AA correcto").arg(ratioTxt), "ok");
    } else if (lvlTitle == Renderer::ContrastLevel::PassLargeOnly) {
        setChip(m_accessTitle, QStringLiteral("⚠ Título: %1:1 — solo texto grande (AA 3:1)").arg(ratioTxt), "warn");
    } else {
        setChip(m_accessTitle, QStringLiteral("✕ Título: %1:1 — ERROR de contraste (mínimo 3:1)").arg(ratioTxt), "err");
    }
    if (lvlBody == Renderer::ContrastLevel::PassAA) {
        setChip(m_accessBody, QStringLiteral("✓ Cuerpo: %1:1 — AA correcto").arg(ratioTxt), "ok");
    } else if (lvlBody == Renderer::ContrastLevel::PassLargeOnly) {
        setChip(m_accessBody, QStringLiteral("⚠ Cuerpo: %1:1 — solo texto grande (AA 3:1)").arg(ratioTxt), "warn");
    } else {
        setChip(m_accessBody, QStringLiteral("✕ Cuerpo: %1:1 — ERROR de contraste (mínimo 3:1)").arg(ratioTxt), "err");
    }
}

// ---------------------------------------------------------------------------
// v1.4.0 — Biblioteca de fondos por etiqueta (spec Holyrics)
// ---------------------------------------------------------------------------
void ThemePanel::refreshMediaList()
{
    if (!m_mediaList) return;
    m_mediaList->clear();
    const QString filter = m_mediaFilter ? m_mediaFilter->text().trimmed() : QString();
    const auto media = filter.isEmpty() ? m_ctx->db->mediaLibrary()
                                        : m_ctx->db->mediaByTag(filter);
    for (const auto &m : media) {
        // Título compacto: [IMG]/[VID] + nombre de archivo
        auto *it = new QListWidgetItem(QStringLiteral("%1  %2")
                                            .arg(m.kind == 1 ? QStringLiteral("[VID]")
                                                             : QStringLiteral("[IMG]"),
                                                 QFileInfo(m.path).fileName()));
        it->setData(Qt::UserRole, m.path);
        it->setData(Qt::UserRole + 1, m.kind);
        it->setToolTip(m.path);
        m_mediaList->addItem(it);
    }
    if (m_mediaTags) m_mediaTags->clear();
}

void ThemePanel::onAddBackgrounds(bool videos)
{
    const QString cap = videos ? QStringLiteral("Añadir videos a la biblioteca")
                               : QStringLiteral("Añadir imágenes a la biblioteca");
    const QString pat = videos
        ? QStringLiteral("Videos (*.mp4 *.avi *.mkv *.mov *.webm)")
        : QStringLiteral("Imágenes (*.png *.jpg *.jpeg *.webp *.bmp)");
    const QStringList files = QFileDialog::getOpenFileNames(this, cap, QString(), pat);
    int added = 0;
    for (const QString &f : files) {
        if (m_ctx->db->addMedia(f, videos ? 1 : 0)) ++added;
    }
    if (added > 0) refreshMediaList();
}

void ThemePanel::onMediaSelected()
{
    auto *it = m_mediaList ? m_mediaList->currentItem() : nullptr;
    if (!it) return;
    const QString path = it->data(Qt::UserRole).toString();
    m_mediaTags->setText(m_ctx->db->mediaTags(path).join(QStringLiteral(", ")));
}

void ThemePanel::onUseBackground()
{
    auto *it = m_mediaList ? m_mediaList->currentItem() : nullptr;
    if (!it) return;
    const QString path = it->data(Qt::UserRole).toString();
    const int kind = it->data(Qt::UserRole + 1).toInt();
    if (kind == 1) {
        m_videoPath->setText(path);
        m_imagePath->clear();
        m_bgType->setCurrentIndex(3);
    } else {
        m_imagePath->setText(path);
        m_videoPath->clear();
        m_bgType->setCurrentIndex(2);
    }
    onFieldChanged();      // preview + proyector en vivo
}

void ThemePanel::onSaveMediaTags()
{
    auto *it = m_mediaList ? m_mediaList->currentItem() : nullptr;
    if (!it) return;
    const QString path = it->data(Qt::UserRole).toString();
    m_ctx->db->setMediaTags(
        path, m_mediaTags->text().split(QRegularExpression(QStringLiteral("\\s*[,;]\\s*")),
                                        Qt::SkipEmptyParts));
    refreshMediaList();    // el filtro puede ahora incluir/excluir el item
}

void ThemePanel::onRemoveBackground()
{
    auto *it = m_mediaList ? m_mediaList->currentItem() : nullptr;
    if (!it) return;
    const QString path = it->data(Qt::UserRole).toString();
    if (m_ctx->db->removeMedia(path)) refreshMediaList();
}
