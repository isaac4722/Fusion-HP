// ============================================================================
//  LuminaPresentation Suite - MediaPanel.cpp
// ============================================================================
#include "MediaPanel.h"
#include "core/Database.h"
#include "core/MediaEngine.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QFileDialog>
#include <QGroupBox>
#include <QTime>
#include <QTimer>

MediaPanel::MediaPanel(AppContext *ctx, QWidget *parent)
    : QWidget(parent), m_ctx(ctx)
{
    buildUi();
    updateVlcStatus();
}

void MediaPanel::updateVlcStatus()
{
    if (m_ctx->media && m_ctx->media->available())
        m_status->setText(QStringLiteral("✅ Motor LibVLC embebido activo"));
    else
        m_status->setText(QStringLiteral("⚠ LibVLC no disponible: la proyección de texto sigue operativa.\n"
                                         "Copia la carpeta 'vlc/' (libvlc.dll + plugins) junto al ejecutable."));
}

void MediaPanel::buildUi()
{
    auto *lay = new QVBoxLayout(this);
    lay->setContentsMargins(12, 12, 12, 12);

    auto *grp = new QGroupBox(QStringLiteral("Medios (video / audio / imágenes)"), this);
    auto *glay = new QVBoxLayout(grp);

    auto *row1 = new QHBoxLayout();
    m_path = new QLineEdit(grp);
    m_path->setPlaceholderText(QStringLiteral("Ruta del archivo de medios…"));
    auto *bBrowse = new QPushButton(QStringLiteral("Explorar…"), grp);
    connect(bBrowse, &QPushButton::clicked, this, &MediaPanel::onOpen);
    row1->addWidget(m_path, 1);
    row1->addWidget(bBrowse);
    glay->addLayout(row1);

    auto *row2 = new QHBoxLayout();
    m_bPlay = new QPushButton(QStringLiteral("▶  Reproducir"), grp);
    m_bPlay->setProperty("class", QStringLiteral("primary"));   // v1.3.0 Aurora
    m_bStop = new QPushButton(QStringLiteral("■  Detener"), grp);
    connect(m_bPlay, &QPushButton::clicked, this, &MediaPanel::onPlay);
    connect(m_bStop, &QPushButton::clicked, this, [this]() { emit stopMedia(); });
    row2->addWidget(m_bPlay);
    row2->addWidget(m_bStop);
    row2->addStretch();
    glay->addLayout(row2);

    auto *row3 = new QHBoxLayout();
    m_background = new QCheckBox(QStringLiteral("Usar como FONDO en bucle (texto encima)"), grp);
    m_loop = new QCheckBox(QStringLiteral("Repetir en bucle"), grp);
    m_loop->setChecked(true);
    row3->addWidget(m_background);
    row3->addWidget(m_loop);
    row3->addStretch();
    glay->addLayout(row3);

    auto *row4 = new QHBoxLayout();
    row4->addWidget(new QLabel(QStringLiteral("Encuadre (video vertical):"), grp));
    m_fit = new QComboBox(grp);
    m_fit->addItem(QStringLiteral("Llenar (recortar bordes)"), 0);
    m_fit->addItem(QStringLiteral("Ajustar (barras laterales)"), 1);
    m_fit->addItem(QStringLiteral("Centrar (tamaño nativo)"), 2);
    row4->addWidget(m_fit);
    row4->addWidget(new QLabel(QStringLiteral("Volumen:"), grp));
    m_volume = new QSlider(Qt::Horizontal, grp);
    m_volume->setRange(0, 100);
    m_volume->setValue(90);
    m_volume->setMaximumWidth(160);
    connect(m_volume, &QSlider::valueChanged, this, &MediaPanel::volumeChanged);
    row4->addWidget(m_volume);
    row4->addStretch();
    glay->addLayout(row4);

    // v1.3.0 — spec Holyrics: medios con «posición de inicio» configurable.
    // El seek se aplica 400 ms después de lanzar la reproducción (cuando el
    // reproductor ya tiene duración cargada).
    auto *row5 = new QHBoxLayout();
    row5->addWidget(new QLabel(QStringLiteral("Iniciar en (segundos):"), grp));
    m_startPos = new QSpinBox(grp);
    m_startPos->setRange(0, 7200);
    m_startPos->setValue(0);
    m_startPos->setSuffix(QStringLiteral(" s"));
    m_startPos->setToolTip(QStringLiteral("Salta a esta posición al reproducir (útil para clips "
                                            "con intro larga o secciones específicas)."));
    row5->addWidget(m_startPos);
    row5->addStretch();
    glay->addLayout(row5);

    m_seek = new QSlider(Qt::Horizontal, grp);
    m_seek->setRange(0, 10000);
    m_seek->setEnabled(false);
    connect(m_seek, &QSlider::sliderPressed, this, [this]() { m_seeking = true; });
    connect(m_seek, &QSlider::sliderReleased, this, [this]() {
        m_seeking = false;
        if (m_ctx->media && m_ctx->media->available()) {
            const qint64 len = m_ctx->media->mainLength();
            if (len > 0)
                m_ctx->media->seekMain(qint64(m_seek->value()) * len / 10000);
        }
    });
    glay->addWidget(m_seek);
    m_time = new QLabel(QStringLiteral("00:00 / 00:00"), grp);
    glay->addWidget(m_time);

    lay->addWidget(grp);

    // Nota BPM
    auto *note = new QLabel(
        QStringLiteral("Sugerencia: sincroniza la velocidad del fondo con el BPM de la alabanza "
                       "ajustando la duración del clip o usando bucles cortos (4/8 compases)."), this);
    note->setWordWrap(true);
    note->setObjectName(QStringLiteral("MutedLabel"));   // v1.3.0 Aurora
    lay->addWidget(note);

    lay->addStretch();
    m_status = new QLabel(QString(), this);
    lay->addWidget(m_status);
}

void MediaPanel::onOpen()
{
    const QString f = QFileDialog::getOpenFileName(
        this, QStringLiteral("Abrir medios"), QString(),
        // v1.5.0 (spec §3.2): JPG, PNG, GIF, BMP, TIF + audio/vídeo
        QStringLiteral("Medios (*.mp4 *.avi *.mkv *.mov *.webm *.mp3 *.wav *.m4a *.ogg "
                        "*.png *.jpg *.jpeg *.gif *.bmp *.tif *.tiff);;Todos (*)"));
    if (f.isEmpty()) return;
    m_path->setText(f);
    const QString ext = QFileInfo(f).suffix().toLower();
    m_isVideo = !(ext == QStringLiteral("mp3") || ext == QStringLiteral("wav") ||
                  ext == QStringLiteral("m4a") || ext == QStringLiteral("ogg"));
}

void MediaPanel::onPlay()
{
    const QString f = m_path->text().trimmed();
    if (f.isEmpty()) return;
    if (m_isVideo) {
        emit playMedia(f, m_background->isChecked(), m_loop->isChecked(), m_fit->currentData().toInt(), true);
    } else {
        emit playMedia(f, false, m_loop->isChecked(), 0, false);    // audio: salida principal
    }
    m_seek->setEnabled(true);
    // v1.3.0: posición inicial (spec Holyrics) — seek diferido
    const int startSec = m_startPos->value();
    if (startSec > 0 && m_ctx->media && m_ctx->media->available()) {
        QTimer::singleShot(400, this, [this, startSec]() {
            if (m_ctx->media && m_ctx->media->available())
                m_ctx->media->seekMain(qint64(startSec) * 1000);
        });
    }
}

void MediaPanel::onPosition(qint64 t, qint64 len)
{
    if (m_seeking || len <= 0) return;
    m_seek->setValue(int(t * 10000 / len));
    m_time->setText(QStringLiteral("%1 / %2")
                        .arg(QTime(0, 0).addMSecs(int(t)).toString(QStringLiteral("mm:ss")),
                             QTime(0, 0).addMSecs(int(len)).toString(QStringLiteral("mm:ss"))));
}

void MediaPanel::onMediaState(int st)
{
    // CORRECCION v1.2.0: el slot estaba vacío — la barra de progreso y el
    // tiempo seguían habilitados/actualizándose tras detener o terminar el
    // medio. Ahora el estado del motor se refleja en la botonera.
    const MediaEngine::State s = static_cast<MediaEngine::State>(st);
    const bool active = (s == MediaEngine::Playing || s == MediaEngine::Paused ||
                         s == MediaEngine::Buffering || s == MediaEngine::Opening);
    m_seek->setEnabled(active);
    if (!active) {
        m_seek->setValue(0);
        m_time->setText(QStringLiteral("00:00 / 00:00"));
        m_bPlay->setText(QStringLiteral("▶  Reproducir"));
    } else {
        m_bPlay->setText(s == MediaEngine::Paused ? QStringLiteral("▶  Reanudar")
                                                  : QStringLiteral("▶  Reproduciendo…"));
    }
}
