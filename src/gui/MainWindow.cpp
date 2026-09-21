// ============================================================================
//  LuminaPresentation Suite - MainWindow.cpp
// ============================================================================
#include "MainWindow.h"
#include "core/Database.h"
#include "core/Lyrics.h"
#include "core/BibleRef.h"
#include "core/PptxEngine.h"
#include "gui/SongPanel.h"
#include "gui/BiblePanel.h"
#include "gui/MediaPanel.h"
#include "gui/PptxPanel.h"
#include "gui/ThemePanel.h"
#include "gui/CustomPanel.h"
#include "gui/ServicePanel.h"
#include "gui/CommsPanel.h"
#include "gui/HistoryPanel.h"
#include "gui/SettingsPanel.h"

#include <QToolBar>
#include <QDockWidget>
#include <QSplitter>
#include <QMessageBox>
#include <QInputDialog>
#include <QFileDialog>
#include <QComboBox>
#include <QStatusBar>
#include <QApplication>
#include <QDesktopWidget>
#include <QScreen>
#include <QKeyEvent>
#include <QCloseEvent>
#include <QTimer>
#include <QShortcut>
#include <QItemSelectionModel>
#include <QStyle>
#include <QFileInfo>
#include <QLineEdit>
#include <QPlainTextEdit>
#include <QVBoxLayout>
#include <QJsonDocument>
#include <QPdfWriter>
#include <QPageSize>
#include <QDragEnterEvent>
#include <QDropEvent>
#include <QMimeData>
#include <QUrl>
#include <QDir>
#include <QRegularExpression>
#include <QRegExp>
#include <functional>

MainWindow::MainWindow(AppContext *ctx, QWidget *parent)
    : QMainWindow(parent)
{
    setWindowTitle(QStringLiteral("LuminaPresentation Suite — Proyección Híbrida"));
    setWindowIcon(QIcon(QStringLiteral(":/img/logo.png")));
    resize(1480, 900);

    m_ctx = *ctx;
    m_ctx.currentTheme = &m_theme;   // los paneles leen el tema activo desde aqui

    m_logoPixmap = QPixmap(QStringLiteral(":/img/logo.png"));

    // Crear ventanas de salida (ocultas hasta asignar)
    m_output = new OutputWindow();
    m_stage = new StageWindow();

    buildUi();
    buildShortcuts();

    // Estado inicial
    const int themeId = m_ctx.db->setting(QStringLiteral("default_theme")).toInt();
    applyTheme(themeId > 0 ? themeId : 1);

    // Servidor embebido
    const bool autostart = m_ctx.db->setting(QStringLiteral("server_autostart"), QStringLiteral("1")) == QStringLiteral("1");
    if (autostart) {
        quint16 ws = quint16(m_ctx.db->setting(QStringLiteral("ws_port"), QStringLiteral("8765")).toInt());
        quint16 http = quint16(m_ctx.db->setting(QStringLiteral("http_port"), QStringLiteral("8088")).toInt());
        QString err;
        if (!m_ctx.web->start(ws, http, &err))
            statusBar()->showMessage(QStringLiteral("Servidor remoto: %1").arg(err), 8000);
    }
    // v1.1.0: token opcional de la API HTTP (espec Holyrics)
    m_ctx.web->setApiToken(m_ctx.db->setting(QStringLiteral("api_token")));

    // v1.3.0 GUI Aurora: reloj del dock (500 ms) + chip de estado del servidor
    // en la barra de estado (permanente, verde/rojo).
    m_clockTimer = new QTimer(this);
    connect(m_clockTimer, &QTimer::timeout, this, &MainWindow::onClockTick);
    m_clockTimer->start(500);
    m_srvChip = new QLabel(QString(), this);
    m_srvChip->setObjectName(QStringLiteral("SrvChip"));
    statusBar()->addPermanentWidget(m_srvChip);
    updateSrvChip();

    // v1.3.0: drag & drop de medios sobre la ventana (spec Holyrics:
    // "importar videos directamente como fondo arrastrándolos").
    setAcceptDrops(true);

    statusBar()->showMessage(QStringLiteral(
        "LuminaPresentation Suite %1 — Salida audiencia: %2 · Stage: %3")
        .arg(QApplication::applicationVersion(),
             m_ctx.db->setting(QStringLiteral("screen_output"), QStringLiteral("auto")),
             m_ctx.db->setting(QStringLiteral("screen_stage"), QStringLiteral("auto"))));

    pushWebState();     // estado inicial para overlay OBS / control remoto
}

MainWindow::~MainWindow() = default;

// ---------------------------------------------------------------------------
// Construccion de la interfaz
// ---------------------------------------------------------------------------
void MainWindow::buildUi()
{
    // Toolbar superior (operativa en vivo)
    QToolBar *tb = addToolBar(QStringLiteral("live"));
    tb->setMovable(false);
    tb->setToolButtonStyle(Qt::ToolButtonTextBesideIcon);

    auto *bLive = new QAction(style()->standardIcon(QStyle::SP_MediaPlay), QStringLiteral(" En Vivo (F5)"), this);
    auto *bPrev = new QAction(style()->standardIcon(QStyle::SP_ArrowBack), QStringLiteral(" Anterior"), this);
    auto *bNext = new QAction(style()->standardIcon(QStyle::SP_ArrowForward), QStringLiteral(" Siguiente"), this);
    auto *bBlack = new QAction(QStringLiteral("⬛"), this);
    bBlack->setToolTip(QStringLiteral("Negro (B)"));
    auto *bClear = new QAction(QStringLiteral("🖼"), this);
    bClear->setToolTip(QStringLiteral("Solo fondo (C)"));
    auto *bLogo = new QAction(QStringLiteral("✝"), this);
    bLogo->setToolTip(QStringLiteral("Logo (L)"));
    auto *bQuick = new QAction(QStringLiteral("⚡ Versículo rápido (F9)"), this);
    auto *bLower = new QAction(QStringLiteral("📌 Lower Third"), this);
    bLower->setToolTip(QStringLiteral("Superposición de texto inferior (títulos, citas) — elemento del spec"));
    connect(bLive, &QAction::triggered, this, [this]() {
        if (m_liveSlides.isEmpty()) {
            // Si no hay nada cargado, proyecta la primera cancion del culto activo
            statusBar()->showMessage(QStringLiteral("No hay contenido en vivo: abre una canción, biblia o culto."), 4000);
            return;
        }
        showSlideIndex(m_liveIndex < 0 ? 0 : m_liveIndex);
    });
    connect(bPrev, &QAction::triggered, this, &MainWindow::prevSlide);
    connect(bNext, &QAction::triggered, this, &MainWindow::nextSlide);
    connect(bBlack, &QAction::triggered, this, &MainWindow::showBlack);
    connect(bClear, &QAction::triggered, this, &MainWindow::showClear);
    connect(bLogo, &QAction::triggered, this, &MainWindow::showLogo);
    connect(bQuick, &QAction::triggered, this, &MainWindow::quickVerse);
    connect(bLower, &QAction::triggered, this, &MainWindow::quickLowerThird);
    tb->addAction(bLive);
    tb->addAction(bPrev);
    tb->addAction(bNext);
    tb->addAction(bBlack);
    tb->addAction(bClear);
    tb->addAction(bLogo);
    tb->addAction(bQuick);
    tb->addAction(bLower);
    tb->addSeparator();

    // Selectores de pantalla en la toolbar
    tb->addWidget(new QLabel(QStringLiteral(" Audiencia:"), this));
    m_comboOutputScreen = new QComboBox(this);
    tb->addWidget(m_comboOutputScreen);
    auto *bOut = new QAction(QStringLiteral("⬆ Salir pantalla"), this);
    bOut->setToolTip(QStringLiteral("Abrir/cerrar salida de audiencia"));
    connect(bOut, &QAction::triggered, this, &MainWindow::toggleOutput);
    tb->addAction(bOut);
    tb->addWidget(new QLabel(QStringLiteral(" Escenario:"), this));
    m_comboStageScreen = new QComboBox(this);
    tb->addWidget(m_comboStageScreen);
    auto *bStage = new QAction(QStringLiteral("🎚 Stage View"), this);
    connect(bStage, &QAction::triggered, this, &MainWindow::toggleStageView);
    tb->addAction(bStage);
    tb->addSeparator();

    // v1.3.0: Modo Presentación (F11) — consola mínima del operador (spec maestro:
    // "la interfaz de presentación es un lienzo limpio").
    auto *bPresent = new QAction(QStringLiteral("🖥 Modo Presentación (F11)"), this);
    bPresent->setToolTip(QStringLiteral("Oculta paneles y deja solo la consola de proyección "
                                         "(preview + transporte + cola). F11 para volver."));
    connect(bPresent, &QAction::triggered, this, &MainWindow::togglePresentationMode);
    tb->addAction(bPresent);
    auto *bAbout = new QAction(QStringLiteral("ℹ"), this);
    bAbout->setToolTip(QStringLiteral("Acerca de LuminaPresentation Suite"));
    connect(bAbout, &QAction::triggered, this, &MainWindow::showAbout);
    tb->addAction(bAbout);
    rebuildScreenCombos();

    setCentralWidget(nullptr);

    // Nav izquierda — v1.3.0 GUI Aurora: sidebar con secciones categorizadas
    // (los encabezados son items no seleccionables; el índice de panel viaja
    // en Qt::UserRole para que onNavChanged no dependa de la posición física).
    m_nav = new QListWidget(this);
    m_nav->setObjectName(QStringLiteral("NavList"));
    m_nav->setFixedWidth(200);
    m_nav->setIconSize(QSize(22, 22));
    auto addNavHeader = [this](const QString &t) {
        auto *it = new QListWidgetItem(t, m_nav);
        it->setFlags(Qt::NoItemFlags);          // no seleccionable
        it->setData(Qt::UserRole, -1);
    };
    auto addNavItem = [this](const QString &t, int panelIndex) {
        auto *it = new QListWidgetItem(t, m_nav);
        it->setData(Qt::UserRole, panelIndex);
        it->setFlags(Qt::ItemIsSelectable | Qt::ItemIsEnabled);
    };
    addNavHeader(QStringLiteral("BIBLIOTECA"));
    addNavItem(QStringLiteral("🎵  Canciones"), 0);
    addNavItem(QStringLiteral("📖  Biblia"), 1);
    addNavItem(QStringLiteral("🎬  Medios"), 2);
    addNavItem(QStringLiteral("📽  PowerPoint (.pptx)"), 3);
    addNavHeader(QStringLiteral("DISEÑO"));
    addNavItem(QStringLiteral("🎨  Temas / Plantillas"), 4);
    addNavItem(QStringLiteral("🖌  Lienzo libre"), 5);
    addNavHeader(QStringLiteral("SERVICIO"));
    addNavItem(QStringLiteral("🗓  Cultos"), 6);
    addNavItem(QStringLiteral("📣  Comunicación"), 7);
    addNavItem(QStringLiteral("📊  Historial"), 8);
    addNavHeader(QStringLiteral("SISTEMA"));
    addNavItem(QStringLiteral("⚙  Ajustes"), 9);
    // Selecciona "Canciones" sin disparar onNavChanged (el stack ya está en 0)
    for (int i = 0; i < m_nav->count(); ++i)
        if (m_nav->item(i)->data(Qt::UserRole).toInt() == 0) { m_nav->setCurrentRow(i); break; }
    connect(m_nav, &QListWidget::currentRowChanged, this, &MainWindow::onNavChanged);

    // Stack central
    m_stack = new QStackedWidget(this);
    m_songPanel = new SongPanel(&m_ctx, this);
    m_biblePanel = new BiblePanel(&m_ctx, this);
    m_mediaPanel = new MediaPanel(&m_ctx, this);
    m_pptxPanel = new PptxPanel(&m_ctx, this);
    m_themePanel = new ThemePanel(&m_ctx, this);
    m_customPanel = new CustomPanel(&m_ctx, this);
    m_servicePanel = new ServicePanel(&m_ctx, this);
    m_commsPanel = new CommsPanel(&m_ctx, this);
    m_historyPanel = new HistoryPanel(&m_ctx, this);
    m_settingsPanel = new SettingsPanel(&m_ctx, this);
    m_stack->addWidget(m_songPanel);
    m_stack->addWidget(m_biblePanel);
    m_stack->addWidget(m_mediaPanel);
    m_stack->addWidget(m_pptxPanel);
    m_stack->addWidget(m_themePanel);
    m_stack->addWidget(m_customPanel);
    m_stack->addWidget(m_servicePanel);
    m_stack->addWidget(m_commsPanel);
    m_stack->addWidget(m_historyPanel);
    m_stack->addWidget(m_settingsPanel);

    // Dock derecho: en vivo (cola + slides + preview) — v1.3.0 GUI Aurora:
    // header con chip EN VIVO + reloj, preview 16:9 con marco dorado al
    // proyectar, mini-preview de la SIGUIENTE slide (vista de moderador, spec
    // PowerPoint) y multiview (miniatura del Stage View, spec Holyrics).
    auto *dock = new QDockWidget(QStringLiteral("PROYECCIÓN EN VIVO"), this);
    dock->setAllowedAreas(Qt::RightDockWidgetArea | Qt::LeftDockWidgetArea);
    dock->setFeatures(QDockWidget::DockWidgetMovable |
                      QDockWidget::DockWidgetFloatable);   // no cerrable: es la consola de proyección
    auto *liveWidget = new QWidget(dock);
    auto *lv = new QVBoxLayout(liveWidget);
    lv->setContentsMargins(8, 8, 8, 8);
    lv->setSpacing(6);

    // Fila 1: chip EN VIVO + info + reloj
    auto *hdrRow = new QHBoxLayout();
    auto *liveChip = new QLabel(QStringLiteral("● EN VIVO"), liveWidget);
    liveChip->setObjectName(QStringLiteral("LiveChip"));
    liveChip->setVisible(false);
    m_liveChip = liveChip;
    m_liveInfo = new QLabel(QStringLiteral("— nada en vivo —"), liveWidget);
    m_liveInfo->setWordWrap(true);
    m_liveInfo->setObjectName(QStringLiteral("LiveInfoLabel"));
    m_clockLabel = new QLabel(QTime::currentTime().toString(QStringLiteral("hh:mm:ss")), liveWidget);
    m_clockLabel->setObjectName(QStringLiteral("ClockLabel"));
    hdrRow->addWidget(liveChip);
    hdrRow->addWidget(m_liveInfo, 1);
    hdrRow->addWidget(m_clockLabel);
    lv->addLayout(hdrRow);

    // Fila 2: preview principal 16:9
    m_preview = new QLabel(liveWidget);
    m_preview->setObjectName(QStringLiteral("LivePreview"));
    m_preview->setMinimumHeight(170);
    m_preview->setAlignment(Qt::AlignCenter);
    m_preview->setProperty("live", false);
    lv->addWidget(m_preview);

    // Fila 3: SIGUIENTE (vista de moderador) + ESCENARIO (multiview)
    auto *miniRow = new QHBoxLayout();
    auto *colNext = new QVBoxLayout();
    auto *lblNext = new QLabel(QStringLiteral("SIGUIENTE ▸"), liveWidget);
    lblNext->setObjectName(QStringLiteral("SectionLabel"));
    m_nextPreview = new QLabel(liveWidget);
    m_nextPreview->setObjectName(QStringLiteral("NextPreview"));
    m_nextPreview->setMinimumHeight(54);
    m_nextPreview->setAlignment(Qt::AlignCenter);
    colNext->addWidget(lblNext);
    colNext->addWidget(m_nextPreview);
    auto *colStage = new QVBoxLayout();
    auto *lblStage = new QLabel(QStringLiteral("ESCEENARIO (musicos)"), liveWidget);
    lblStage->setObjectName(QStringLiteral("SectionLabel"));
    m_stageMini = new QLabel(QStringLiteral("—"), liveWidget);
    m_stageMini->setObjectName(QStringLiteral("StageMini"));
    m_stageMini->setMinimumHeight(54);
    m_stageMini->setAlignment(Qt::AlignCenter);
    m_stageMini->setWordWrap(true);
    colStage->addWidget(lblStage);
    colStage->addWidget(m_stageMini);
    miniRow->addLayout(colNext, 1);
    miniRow->addLayout(colStage, 1);
    lv->addLayout(miniRow);

    // Fila 3b: chip de cuenta regresiva (visible solo con temporizador activo)
    m_countdownChip = new QLabel(QString(), liveWidget);
    m_countdownChip->setObjectName(QStringLiteral("CountdownChip"));
    m_countdownChip->setAlignment(Qt::AlignCenter);
    m_countdownChip->setVisible(false);
    lv->addWidget(m_countdownChip);

    // Barra de transporte del Modo Presentación (oculta fuera de F11)
    m_presBar = new QWidget(liveWidget);
    auto *pb = new QHBoxLayout(m_presBar);
    pb->setContentsMargins(0, 4, 0, 4);
    auto presBtn = [this, &pb](const QString &t, const char *cls, std::function<void()> fn) {
        auto *b = new QPushButton(t, m_presBar);
        if (cls) b->setProperty("class", cls);
        b->setObjectName(QStringLiteral("PresBtn"));
        connect(b, &QPushButton::clicked, this, std::move(fn));
        pb->addWidget(b, 1);
    };
    presBtn(QStringLiteral("◀ Anterior"), nullptr, [this]() { prevSlide(); });
    presBtn(QStringLiteral("Siguiente ▶"), "primary", [this]() { nextSlide(); });
    presBtn(QStringLiteral("⬛ Negro"), nullptr, [this]() { showBlack(); });
    presBtn(QStringLiteral("✝ Logo"), nullptr, [this]() { showLogo(); });
    presBtn(QStringLiteral("🖼 Fondo"), nullptr, [this]() { showClear(); });
    presBtn(QStringLiteral("⚡ Versículo"), "gold", [this]() { quickVerse(); });
    m_presBar->setVisible(false);
    lv->addWidget(m_presBar);

    lv->addWidget(new QLabel(QStringLiteral("<b>Slides del elemento actual</b>"), liveWidget));
    m_slideList = new QListWidget(liveWidget);
    connect(m_slideList, &QListWidget::currentRowChanged, this, [this](int row) {
        // CORRECCION v1.2.0: showSlideIndex() sincroniza la lista con
        // setCurrentRow(), lo que re-disparaba currentRowChanged y ejecutaba el
        // flujo completo DOS veces por navegacion (triggers OBS/MIDI/webhook
        // duplicados + doble render). El guard row != m_liveIndex lo corta.
        if (row >= 0 && row < m_liveSlides.size() && row != m_liveIndex)
            showSlideIndex(row);
    });
    lv->addWidget(m_slideList, 2);
    lv->addWidget(new QLabel(QStringLiteral("<b>Cola del culto</b>"), liveWidget));
    m_queueList = new QListWidget(liveWidget);
    m_queueList->setContextMenuPolicy(Qt::NoContextMenu);
    connect(m_queueList, &QListWidget::itemDoubleClicked, this, [this](QListWidgetItem *it) {
        // Ejecuta el item del culto con doble clic
        const ServiceItem item = it->data(Qt::UserRole).value<ServiceItem>();
        m_servicePanel->requestRunItem(item);
    });
    lv->addWidget(m_queueList, 1);
    dock->setWidget(liveWidget);
    addDockWidget(Qt::RightDockWidgetArea, dock);

    // Layout central: nav + stack
    auto *central = new QWidget(this);
    auto *cl = new QHBoxLayout(central);
    cl->setContentsMargins(0, 0, 0, 0);
    cl->addWidget(m_nav);
    cl->addWidget(m_stack, 1);
    setCentralWidget(central);

    // ---- Conexiones entre paneles y motor en vivo ----
    connect(m_songPanel, &SongPanel::requestProjectSong, this, [this](int songId) {
        const Song s = m_ctx.db->songById(songId);
        goLiveSong(s, ServiceItem::Song, s.id);
        m_ctx.db->touchSongUsage(s.id);
        m_ctx.triggers->fireEvent(QStringLiteral("golive"), { { QStringLiteral("song"), s.title } });
    });
    connect(m_songPanel, &SongPanel::requestAddToService, this, [this](int songId) {
        const Song s = m_ctx.db->songById(songId);
        // CORRECCION v1.2.0: "A culto" añadia SIEMPRE al culto mas reciente
        // (mayor id) en vez del culto que el usuario tiene seleccionado en el
        // panel Cultos. Ahora se usa el culto activo del ServicePanel.
        const int plId = activePlaylistId();
        if (plId <= 0) {
            const int id = m_ctx.db->createPlaylist(QStringLiteral("Culto"));
            ServiceItem it; it.kind = ServiceItem::Song; it.refId = songId; it.label = s.title;
            m_ctx.db->addPlaylistItem(id, it);
        } else {
            ServiceItem it; it.kind = ServiceItem::Song; it.refId = songId; it.label = s.title;
            m_ctx.db->addPlaylistItem(plId, it);
        }
        statusBar()->showMessage(QStringLiteral("Canción añadida al culto activo."), 3000);
    });

    connect(m_biblePanel, &BiblePanel::requestProjectVerses, this,
            [this](const QStringList &versions, int book, int ch, int from, int to,
                   const QString &highlight) {
        if (ch <= 0) return;
        QVector<Slide> slides;
        // Slides por grupos de 2 versiculos; versiones paralelas apiladas
        const QVector<BibleRef::Verse> primary = m_ctx.db->bibleRange(versions.first(), book, ch,
                                                                     from > 0 ? from : 1,
                                                                     to > 0 ? to : (from > 0 ? from : 1));
        if (primary.isEmpty()) return;
        for (int i = 0; i < primary.size(); i += 2) {
            Slide s;
            s.kind = Slide::Bible;
            s.title = primary.first().ref.bookName + QStringLiteral(" %1").arg(ch);
            s.refLabel = BibleRef::formatRef(primary.at(i).ref) +
                         (i + 1 < primary.size()
                              ? QStringLiteral("-%1").arg(primary.at(i + 1).ref.verse)
                              : QString());
            s.highlight = highlight;        // v1.1.0: resaltado de palabras
            if (versions.size() > 1) {
                s.refLabel += QStringLiteral("  (%1)").arg(versions.join(QStringLiteral("+")));
                for (const QString &v : versions) {
                    const QVector<BibleRef::Verse> pv = m_ctx.db->bibleRange(
                        v, book, ch, primary.at(i).ref.verse,
                        i + 1 < primary.size() ? primary.at(i + 1).ref.verse : primary.at(i).ref.verse);
                    QString text;
                    for (const BibleRef::Verse &vv : pv)
                        text += (text.isEmpty() ? QString() : QStringLiteral(" ")) +
                                QStringLiteral("%1 %2").arg(vv.ref.verse).arg(vv.text);
                    s.lines.append(SlideLine(QStringLiteral("[%1] %2").arg(v, text)));
                }
            } else {
                for (int j = i; j < qMin(i + 2, primary.size()); ++j)
                    s.lines.append(SlideLine(QStringLiteral("%1 %2").arg(primary.at(j).ref.verse)
                                             .arg(primary.at(j).text)));
            }
            slides.append(s);
        }
        goLive(slides, s_titleOf(book, ch), ServiceItem::Bible, 0);
        m_ctx.triggers->fireEvent(QStringLiteral("golive"), { { QStringLiteral("item"), s_titleOf(book, ch) } });
    });
    connect(m_biblePanel, &BiblePanel::requestAddVerseToService, this,
            [this](const QStringList &versions, int book, int ch, int from, int to) {
        // CORRECCION v1.2.0: mismo defecto que requestAddToService — añadía al
        // culto más reciente en lugar del culto activo seleccionado.
        const int plId = activePlaylistId();
        if (plId <= 0) return;
        ServiceItem it;
        it.kind = ServiceItem::Bible;
        it.label = QStringLiteral("Biblia: %1 %2:%3")
                       .arg(m_ctx.db->bibleRange(versions.first(), book, ch, qMax(1, from), qMax(1, from))
                                .value(0).ref.bookName)
                       .arg(ch).arg(from > 0 ? from : 1);
        it.payload = QStringLiteral("%1|%2|%3|%4|%5").arg(versions.join(QStringLiteral("+")))
                         .arg(book).arg(ch).arg(from).arg(to);
        m_ctx.db->addPlaylistItem(plId, it);
        statusBar()->showMessage(QStringLiteral("Versículo añadido al culto."), 3000);
    });

    connect(m_mediaPanel, &MediaPanel::playMedia, this, &MainWindow::onPlayMedia);
    connect(m_mediaPanel, &MediaPanel::stopMedia, this, &MainWindow::onStopMedia);
    connect(m_mediaPanel, &MediaPanel::volumeChanged, this, &MainWindow::onVolume);
    // FIX v1.1.0: las señales del motor multimedia nunca estaban conectadas
    // al panel — la barra de progreso y el tiempo estaban muertos. Ademas,
    // el fin de medio (finished) ahora limpia la salida y dispara trigger.
    if (m_ctx.media) {
        connect(m_ctx.media, &MediaEngine::positionChanged, m_mediaPanel, &MediaPanel::onPosition);
        connect(m_ctx.media, &MediaEngine::stateChanged, m_mediaPanel, &MediaPanel::onMediaState);
        connect(m_ctx.media, &MediaEngine::finished, this, [this]() {
            m_ctx.media->stopMain();
            m_output->setVideoVisible(false);
            m_ctx.triggers->fireEvent(QStringLiteral("media_stop"));
            statusBar()->showMessage(QStringLiteral("Medio finalizado."), 3000);
        });
    }

    connect(m_pptxPanel, &PptxPanel::requestProjectPptx, this, [this](const QVector<Slide> &slides) {
        goLive(slides, QStringLiteral("Presentación PowerPoint"), ServiceItem::Pptx, 0);
        m_ctx.triggers->fireEvent(QStringLiteral("golive"), { { QStringLiteral("item"), QStringLiteral("PowerPoint") } });
    });
    // FIX v1.1.0: los items PPTX añadidos al culto guardaban el ARCHIVO en
    // payload (antes solo el nombre) y nunca podian ejecutarse desde la cola.
    connect(m_pptxPanel, &PptxPanel::requestAddPptxToService, this, [this](const QString &filePath) {
        // CORRECCION v1.2.0: idem — usa el culto activo del ServicePanel.
        const int plId = activePlaylistId();
        if (plId > 0) {
            ServiceItem it; it.kind = ServiceItem::Pptx;
            it.label = QStringLiteral("📽 %1").arg(QFileInfo(filePath).completeBaseName());
            it.payload = filePath;
            m_ctx.db->addPlaylistItem(plId, it);
            statusBar()->showMessage(QStringLiteral("PowerPoint añadido al culto activo."), 3000);
        }
    });
    // Exportar el contenido EN VIVO (cancion/biblia) a .pptx con texto real
    connect(m_pptxPanel, &PptxPanel::requestExportLive, this, [this]() {
        if (m_liveSlides.isEmpty()) {
            QMessageBox::information(this, QStringLiteral("Exportar a PowerPoint"),
                                     QStringLiteral("No hay contenido en vivo. Proyecta una canción o "
                                                    "versículo primero, o carga un .pptx en el panel "
                                                    "de PowerPoint."));
            return;
        }
        const QString out = QFileDialog::getSaveFileName(this, QStringLiteral("Exportar en vivo a PowerPoint"),
                                                         QStringLiteral("presentacion.pptx"),
                                                         QStringLiteral("PowerPoint (*.pptx)"));
        if (out.isEmpty()) return;
        QString err;
        if (PptxEngine::exportPptx(m_liveSlides, m_theme, out, &err))
            QMessageBox::information(this, QStringLiteral("Exportar"),
                                     QStringLiteral("Exportado correctamente a:\n%1").arg(out));
        else
            QMessageBox::warning(this, QStringLiteral("Exportar"), err);
    });
    // v1.1.0: exportar el escenario en vivo a PDF (spec: exportar a "PPTX y PDF")
    connect(m_pptxPanel, &PptxPanel::requestExportPdf, this, &MainWindow::exportLivePdf);
    connect(m_pptxPanel, &PptxPanel::requestExportPng, this, &MainWindow::exportLivePng);   // v1.3.0

    connect(m_themePanel, &ThemePanel::themeChanged, this, [this](const Theme &t) {
        m_theme = t;
        if (m_liveIndex >= 0 && !m_liveSlides.isEmpty())
            showSlideIndex(m_liveIndex, false);
        else
            updatePreview();
    });

    connect(m_customPanel, &CustomPanel::requestProjectCustom, this, [this](const Slide &s) {
        QVector<Slide> v; v.append(s);
        goLive(v, s.title.isEmpty() ? QStringLiteral("Lienzo libre") : s.title, ServiceItem::Custom, 0);
    });
    // v1.2.0: slides del lienzo añadibles a la cola del culto (en el culto activo).
    connect(m_customPanel, &CustomPanel::requestAddCustomToService, this, [this](int customId) {
        const int plId = activePlaylistId();
        if (plId <= 0) {
            statusBar()->showMessage(QStringLiteral("Crea o selecciona un culto en el panel Cultos primero."), 5000);
            return;
        }
        bool ok = false;
        const QJsonObject json = m_ctx.db->customSlideJson(customId, &ok);
        if (!ok) return;
        ServiceItem it;
        it.kind = ServiceItem::Custom;
        it.refId = customId;
        it.label = QStringLiteral("🎨 Slide");
        for (const auto &cs : m_ctx.db->customSlides())
            if (cs.first == customId) it.label = QStringLiteral("🎨 %1").arg(cs.second);
        it.payload = QString::fromUtf8(QJsonDocument(json).toJson(QJsonDocument::Compact));
        m_ctx.db->addPlaylistItem(plId, it);
        statusBar()->showMessage(QStringLiteral("Slide de lienzo añadida al culto activo."), 3000);
    });

    connect(m_servicePanel, &ServicePanel::startService, this, [this](int playlistId) {
        // Carga la cola en el dock y proyecta el primer item
        m_queueList->clear();
        m_queueData.clear();
        const auto items = m_ctx.db->playlistItems(playlistId);
        for (const ServiceItem &it : items) {
            m_queueData.append(it);
            auto *li = new QListWidgetItem(it.label);
            QVariant v;
            v.setValue(it);
            li->setData(Qt::UserRole, v);
            m_queueList->addItem(li);
        }
        statusBar()->showMessage(QStringLiteral("Culto cargado: %1 items.").arg(items.size()), 4000);
        if (!items.isEmpty()) runServiceItem(items.first());
    });
    connect(m_servicePanel, &ServicePanel::requestRunItem, this, &MainWindow::runServiceItem);

    connect(m_commsPanel, &CommsPanel::sendAlert, this, &MainWindow::onSendAlert);
    connect(m_commsPanel, &CommsPanel::startCountdown, this, &MainWindow::onStartCountdown);
    connect(m_commsPanel, &CommsPanel::stopCountdownSignal, this, &MainWindow::onStopCountdown);

    // v1.1.0: transposición en vivo del Stage View (spec: cifras para musicos)
    m_stageTranspose = m_ctx.db->setting(QStringLiteral("stage_transpose"), QStringLiteral("0")).toInt();
    connect(m_songPanel, &SongPanel::stageTransposeChanged, this, [this](int semi) {
        m_stageTranspose = semi;
        m_ctx.db->setSetting(QStringLiteral("stage_transpose"), QString::number(semi));
        updateStage();
    });

    connect(m_settingsPanel, &SettingsPanel::settingsApplied, this, [this]() {
        // FIX v1.1.0: los combos de la toolbar quedaban desincronizados tras
        // aplicar Ajustes (la pantalla elegida en Ajustes no se reflejaba).
        rebuildScreenCombos();
        const int outScr = m_ctx.db->setting(QStringLiteral("screen_output"), QStringLiteral("-1")).toInt();
        const int stgScr = m_ctx.db->setting(QStringLiteral("screen_stage"), QStringLiteral("0")).toInt();
        if (m_comboOutputScreen->findData(outScr) >= 0)
            m_comboOutputScreen->setCurrentIndex(m_comboOutputScreen->findData(outScr));
        if (m_comboStageScreen->findData(stgScr) >= 0)
            m_comboStageScreen->setCurrentIndex(m_comboStageScreen->findData(stgScr));
        reassignOutputs();
        // CORRECCION v1.2.0: el "Tema activo" elegido en Ajustes ahora se
        // aplica EN VIVO (antes solo surtía efecto tras reiniciar la app).
        const int themeId = m_ctx.db->setting(QStringLiteral("default_theme")).toInt();
        if (themeId > 0) applyTheme(themeId);
        // CORRECCION v1.2.0: el arranque del servidor ignoraba el ajuste
        // "Iniciar servidor automáticamente" (arrancaba siempre) y los errores
        // de puerto ocupado se tragaban en silencio.
        if (m_ctx.db->setting(QStringLiteral("server_autostart"), QStringLiteral("1")) == QStringLiteral("1")) {
            quint16 ws = quint16(m_ctx.db->setting(QStringLiteral("ws_port"), QStringLiteral("8765")).toInt());
            quint16 http = quint16(m_ctx.db->setting(QStringLiteral("http_port"), QStringLiteral("8088")).toInt());
            QString err;
            if (!m_ctx.web->start(ws, http, &err))
                statusBar()->showMessage(QStringLiteral("Servidor remoto: %1").arg(err), 8000);
        } else {
            m_ctx.web->stop();
        }
        m_ctx.web->setApiToken(m_ctx.db->setting(QStringLiteral("api_token")));   // v1.1.0
        updateSrvChip();                       // v1.3.0: refleja el estado real del servidor
        statusBar()->showMessage(QStringLiteral("Configuración aplicada y servidor reiniciado."), 4000);
    });

    // CORRECCION v1.2.0: doble clic en la salida de audiencia = abrir/cerrar
    // la proyección. Antes la señal outputClicked existía pero nunca estaba
    // conectada: con la salida fullscreen en pantalla única el operador quedaba
    // atrapado sin forma de recuperar el control (salvo matar el proceso).
    connect(m_output, &OutputWindow::outputClicked, this, [this]() {
        toggleOutput();
    });

    // Servidor remoto
    connect(m_ctx.web, &WebServer::remoteCommand, this, &MainWindow::onRemoteCommand);

    // Pantallas
    connect(m_comboOutputScreen, qOverload<int>(&QComboBox::currentIndexChanged), this, [this](int) { reassignOutputs(); });
    connect(m_comboStageScreen, qOverload<int>(&QComboBox::currentIndexChanged), this, [this](int) { reassignOutputs(); });

    // Triggers por defecto desde DB
    const QString tj = m_ctx.db->setting(QStringLiteral("triggers"));
    if (!tj.isEmpty()) {
        const QJsonObject o = QJsonDocument::fromJson(tj.toUtf8()).object();
        Triggers::Config c;
        c.webhookEnabled = o.value(QStringLiteral("webhookOn")).toBool(false);
        c.webhookUrl = o.value(QStringLiteral("webhookUrl")).toString();
        c.obsEnabled = o.value(QStringLiteral("obsOn")).toBool(false);
        c.obsHost = o.value(QStringLiteral("obsHost")).toString(QStringLiteral("127.0.0.1"));
        c.obsPort = quint16(o.value(QStringLiteral("obsPort")).toInt(4455));
        c.obsPassword = o.value(QStringLiteral("obsPass")).toString();
        c.obsSceneOnSlide = o.value(QStringLiteral("obsScene")).toString(QStringLiteral("Proyeccion"));
        c.midiEnabled = o.value(QStringLiteral("midiOn")).toBool(false);
        c.midiProgramOnSlide = o.value(QStringLiteral("midiProgram")).toInt(0);
        c.telegramEnabled = o.value(QStringLiteral("tgOn")).toBool(false);
        c.telegramToken = o.value(QStringLiteral("tgToken")).toString();
        c.telegramChatId = o.value(QStringLiteral("tgChat")).toString();
        m_ctx.triggers->applyConfig(c);
    }
}

// ---------------------------------------------------------------------------
// Navegacion lateral
// ---------------------------------------------------------------------------
void MainWindow::onNavChanged(int row)
{
    // v1.3.0 GUI Aurora: el índice de panel viaja en Qt::UserRole (la sidebar
    // intercala encabezados de sección no seleccionables).
    if (row < 0 || row >= m_nav->count()) return;
    const int panel = m_nav->item(row)->data(Qt::UserRole).toInt();
    if (panel < 0 || panel >= m_stack->count()) return;
    m_stack->setCurrentIndex(panel);
    if (panel == 8) m_historyPanel->reload();      // Historial
    if (panel == 9) m_settingsPanel->refreshScreens(); // Ajustes
}

QString MainWindow::s_titleOf(int book, int ch)
{
    if (book >= 1 && book <= BibleRef::books().size())
        return QStringLiteral("%1 %2").arg(BibleRef::books().at(book - 1).name).arg(ch);
    return QStringLiteral("Biblia");
}

// v1.2.0: culto activo según el combo del ServicePanel (fallback: ninguno).
int MainWindow::activePlaylistId() const
{
    return m_servicePanel ? m_servicePanel->activePlaylistId() : 0;
}

void MainWindow::buildShortcuts()
{
    // v1.3.0: atajos PERSONALIZABLES (spec Holyrics). Las claves key_* se
    // editan desde Ajustes › Atajos de teclado (QKeySequenceEdit) y se
    // persisten como texto de QKeySequence (p.ej. "Ctrl+Derecha").
    auto add = [this](const QKeySequence &k, std::function<void()> fn) {
        auto *sc = new QShortcut(k, this);
        connect(sc, &QShortcut::activated, this, std::move(fn));
    };
    add(shortcutSetting("key_golive", Qt::Key_F5), [this]() {
        if (!m_liveSlides.isEmpty()) showSlideIndex(m_liveIndex < 0 ? 0 : m_liveIndex);
    });
    // CORRECCION v1.2.0: Space con WindowShortcut interceptaba la barra
    // espaciadora incluso cuando el foco estaba en un botón/casilla (QShortcut
    // gana al keyPress del widget) — activar/desactivar casillas con el teclado
    // era imposible. Ahora se cede el paso si el foco está en un control.
    add(QKeySequence(Qt::Key_Space), [this]() {
        if (qobject_cast<QAbstractButton *>(QApplication::focusWidget())) return;
        nextSlide();
    });
    add(shortcutSetting("key_next", Qt::Key_Right), [this]() { nextSlide(); });
    add(shortcutSetting("key_prev", Qt::Key_Left), [this]() { prevSlide(); });
    add(QKeySequence(Qt::Key_PageDown), [this]() { nextSlide(); });
    add(QKeySequence(Qt::Key_PageUp), [this]() { prevSlide(); });
    add(shortcutSetting("key_black", Qt::Key_B), [this]() { showBlack(); });
    add(shortcutSetting("key_clear", Qt::Key_C), [this]() { showClear(); });
    add(shortcutSetting("key_logo", Qt::Key_L), [this]() { showLogo(); });
    add(shortcutSetting("key_quickverse", Qt::Key_F9), [this]() { quickVerse(); });
    add(shortcutSetting("key_lowerthird", Qt::Key_F10), [this]() { quickLowerThird(); });
    add(shortcutSetting("key_presentation", Qt::Key_F11), [this]() { togglePresentationMode(); });
    add(QKeySequence(Qt::Key_Escape), [this]() { closeOverlay(); });
}

QKeySequence MainWindow::shortcutSetting(const char *settingKey, int defaultKey) const
{
    // Ajuste vacío o ilegible => default de fábrica. QKeySequence::toString
    // produce PortableText ("Ctrl+X", "F9", "Return"...), roundtrip seguro.
    const QString raw = m_ctx.db->setting(QLatin1String(settingKey));
    if (!raw.trimmed().isEmpty()) {
        const QKeySequence ks(raw);
        if (!ks.isEmpty())
            return ks;
    }
    return QKeySequence(defaultKey);
}

// ---------------------------------------------------------------------------
// Tema
// ---------------------------------------------------------------------------
void MainWindow::applyTheme(int themeId)
{
    m_theme = m_ctx.db->themeById(themeId);
    updatePreview();
    if (m_liveIndex >= 0 && !m_liveSlides.isEmpty())
        showSlideIndex(m_liveIndex, false);
}

// ---------------------------------------------------------------------------
// En vivo
// ---------------------------------------------------------------------------
// v1.1.0: proyección de canciones centralizada (un solo lugar aplica los
// ajustes: slide de título, Modo Hinario, versos por slide).
void MainWindow::goLiveSong(const Song &s, int refKind, int refId)
{
    Lyrics::BuildOptions opt;
    opt.titleSlide = m_ctx.db->setting(QStringLiteral("song_title_slide"), QStringLiteral("1")) == QStringLiteral("1");
    opt.endBlank = m_ctx.db->setting(QStringLiteral("song_end_blank"), QStringLiteral("0")) == QStringLiteral("1");
    // v1.1.0: Modo Hinario (intercalar coro tras cada verso) — ajuste de spec
    opt.chorusInterleave = m_ctx.db->setting(QStringLiteral("song_hinario"), QStringLiteral("0")) == QStringLiteral("1");
    // v1.1.0: versos por slide configurable (2..8, por defecto 4)
    int maxLines = m_ctx.db->setting(QStringLiteral("song_maxlines"), QStringLiteral("4")).toInt();
    opt.maxLinesPerSlide = qBound(2, maxLines, 8);
    goLive(Lyrics::buildSlides(s, opt), s.title, refKind, refId);
}

void MainWindow::goLive(const QVector<Slide> &slides, const QString &label, int refKind, int refId,
                        bool isOverlay)
{
    // CORRECCION v1.2.0 (DEFECTO CRÍTICO): showSlideIndex() llamaba a
    // closeOverlay() como PRIMERA instrucción. Flujo roto del F9:
    //   quickVerse() guarda estado -> goLive(versiculo) -> showSlideIndex(0)
    //   -> closeOverlay() RESTAURA la canción -> m_liveSlides.at(0) ya NO era
    //   el versículo sino la slide 0 de la canción => el versículo rápido
    //   proyectaba la canción equivocada, Esc no restauraba nada, y con nada
    //   en vivo m_liveSlides.at(0) sobre vector vacío = crash/UB.
    // Solución: el overlay se cierra AQUÍ, al cargar contenido nuevo. Si el
    // nuevo contenido ES el overlay (isOverlay=true, F9) se conserva el punto
    // de retorno; si el operador proyecta otra cosa, el retorno se descarta.
    if (!isOverlay) {
        m_overlayActive = false;
        m_savedSlides.clear();
        m_savedIndex = -1;
        m_savedLabel.clear();
    }
    m_liveSlides = slides;
    m_liveLabel = label;
    m_liveRefKind = refKind;
    m_liveRefId = refId;
    m_liveIndex = slides.isEmpty() ? -1 : 0;
    m_liveInfo->setText(label);
    updateSlideList();
    if (!slides.isEmpty())
        showSlideIndex(0);
    // Asegura salida visible
    if (!m_output->isVisible())
        reassignOutputs();
}

void MainWindow::showSlideIndex(int idx, bool fireTriggers)
{
    if (idx < 0 || idx >= m_liveSlides.size()) return;
    // CORRECCION v1.2.0: el closeOverlay() que estaba aquí destruía el flujo
    // del versículo rápido (ver goLive). El cierre del overlay ahora ocurre
    // únicamente en goLive() y en closeOverlay() (tecla Esc).
    m_liveIndex = idx;
    const Slide &s = m_liveSlides.at(idx);

    // A la salida de audiencia
    m_output->setSlide(s, m_theme);
    if (!m_output->isVisible()) reassignOutputs();

    // Fondo de video del tema (si la slide no define otro)
    if (m_theme.background.type == 3 && !m_theme.background.videoPath.isEmpty())
        playThemeBackgroundVideo(m_theme);

    // Stage view con acordes
    updateStage();

    // Preview y lista
    updatePreview();
    if (m_slideList->currentRow() != idx)
        m_slideList->setCurrentRow(idx, QItemSelectionModel::ClearAndSelect);

    // Triggers + web
    if (fireTriggers) {
        QVariantMap data;
        data[QStringLiteral("slide")] = s.refLabel;
        data[QStringLiteral("song")] = m_liveLabel;
        data[QStringLiteral("index")] = idx;
        m_ctx.triggers->fireEvent(QStringLiteral("slide"), data);
    }
    pushWebState();
}

void MainWindow::nextSlide()
{
    if (m_liveIndex + 1 < m_liveSlides.size())
        showSlideIndex(m_liveIndex + 1);
}

void MainWindow::prevSlide()
{
    if (m_liveIndex > 0)
        showSlideIndex(m_liveIndex - 1);
}

void MainWindow::showBlack()
{
    stopBackgroundVideo();
    m_output->showBlack();
    m_ctx.triggers->fireEvent(QStringLiteral("black"));
    pushWebState();
}

void MainWindow::showClear()
{
    stopBackgroundVideo();
    m_output->setThemeBackground(m_theme);
    m_ctx.triggers->fireEvent(QStringLiteral("clear"));
    pushWebState();
}

void MainWindow::showLogo()
{
    stopBackgroundVideo();
    m_output->showLogo(m_logoPixmap);
    m_ctx.triggers->fireEvent(QStringLiteral("logo"));
    pushWebState();
}

void MainWindow::quickVerse()
{
    bool ok = false;
    const QString ref = QInputDialog::getText(this, QStringLiteral("Versículo rápido (F9)"),
                                              QStringLiteral("Referencia (ej. Jn 3:16, Sal 23:1-3):"),
                                              QLineEdit::Normal, QString(), &ok);
    if (!ok || ref.trimmed().isEmpty()) return;
    const BibleRef::VerseRef r = BibleRef::resolve(ref);
    if (!r.valid() || r.verse <= 0) {
        QMessageBox::information(this, QStringLiteral("Versículo rápido"),
                                 QStringLiteral("No se entendió la referencia. Ej: Jn 3:16"));
        return;
    }
    // Conserva el estado actual para restaurar con Esc
    // FIX v1.1.0: antes, un segundo F9 (versiculo sobre versiculo) machacaba
    // m_savedIndex con el indice del overlay y Esc restauraba la slide
    // equivocada. Ahora el estado original solo se guarda la primera vez.
    if (!m_overlayActive) {
        m_savedSlides = m_liveSlides;
        m_savedIndex = m_liveIndex;
        m_savedLabel = m_liveLabel;
        m_overlayActive = true;
    }
    const QString v1 = m_ctx.db->bibleVersions().value(0);
    if (v1.isEmpty()) return;
    QVector<Slide> slides;
    // CORRECCION v1.2.0: se ignoraba el RANGO de la referencia — "Sal 23:1-3"
    // proyectaba únicamente el versículo 1. Ahora se resuelve el rango
    // completo con BibleRef::rangeOf (el placeholder del diálogo lo prometía).
    const auto rng = BibleRef::rangeOf(ref);
    int vFrom = r.verse > 0 ? r.verse : 1;
    int vTo = vFrom;
    if (rng.first == r.chapter && rng.second.first > 0) {
        vFrom = rng.second.first;
        vTo = qMax(rng.second.second, vFrom);
    }
    const QVector<BibleRef::Verse> verses = m_ctx.db->bibleRange(v1, r.book, r.chapter, vFrom, vTo);
    if (verses.isEmpty()) return;
    Slide s;
    s.kind = Slide::Bible;
    s.title = m_savedLabel;
    s.refLabel = BibleRef::formatRef(r) + QStringLiteral(" (%1)").arg(v1);
    for (const BibleRef::Verse &v : verses)
        s.lines.append(SlideLine(QStringLiteral("%1 %2").arg(v.ref.verse).arg(v.text)));
    slides.append(s);
    // isOverlay=true: conserva el punto de retorno de la canción (Esc).
    goLive(slides, QStringLiteral("Versículo rápido"), ServiceItem::Bible, 0, true);
    statusBar()->showMessage(QStringLiteral("Versículo proyectado. Esc para volver a la canción."), 4000);
}

// ---------------------------------------------------------------------------
// v1.1.0: Lower Third (superposición inferior) — elemento del spec (Componente
// 2). Un dialogo pide titulo y lineas; se proyecta como slide semitransparente
// sobre el fondo/tema actual sin interrumpir la secuencia en vivo.
// ---------------------------------------------------------------------------
void MainWindow::quickLowerThird()
{
    QDialog dlg(this);
    dlg.setWindowTitle(QStringLiteral("Lower Third — superposición inferior"));
    dlg.resize(520, 220);
    auto *lay = new QVBoxLayout(&dlg);
    auto *edTitle = new QLineEdit(&dlg);
    edTitle->setPlaceholderText(QStringLiteral("Título (ej: Pr. Juan Pérez — Pastor)"));
    auto *edText = new QPlainTextEdit(&dlg);
    edText->setPlaceholderText(QStringLiteral("Texto del lower third (una o varias líneas)…"));
    edText->setMaximumHeight(90);
    lay->addWidget(new QLabel(QStringLiteral("Título:"), &dlg));
    lay->addWidget(edTitle);
    lay->addWidget(new QLabel(QStringLiteral("Texto:"), &dlg));
    lay->addWidget(edText);
    auto *btns = new QHBoxLayout();
    btns->addStretch();
    auto *ok = new QPushButton(QStringLiteral("Proyectar"), &dlg);
    ok->setDefault(true);
    auto *cancel = new QPushButton(QStringLiteral("Cancelar"), &dlg);
    connect(ok, &QPushButton::clicked, &dlg, &QDialog::accept);
    connect(cancel, &QPushButton::clicked, &dlg, &QDialog::reject);
    btns->addWidget(cancel);
    btns->addWidget(ok);
    lay->addLayout(btns);
    if (dlg.exec() != QDialog::Accepted) return;

    Slide s;
    s.kind = Slide::LowerThird;
    s.title = edTitle->text().trimmed();
    const QString body = edText->toPlainText().trimmed();
    for (const QString &ln : body.split(QChar('\n'), Qt::SkipEmptyParts))
        s.lines.append(SlideLine(ln.trimmed()));
    if (s.title.isEmpty() && s.lines.isEmpty()) return;
    s.refLabel = QStringLiteral("Lower Third");
    QVector<Slide> v; v.append(s);
    goLive(v, QStringLiteral("Lower Third"), ServiceItem::Custom, 0);
}

// ---------------------------------------------------------------------------
// v1.1.0: exportar el escenario EN VIVO a PDF (spec: exportar a PPTX y PDF).
// Cada slide se rasteriza con el Renderer a 1920x1080 y se pagina en un
// QPdfWriter (16:9).
// ---------------------------------------------------------------------------
void MainWindow::exportLivePdf()
{
    if (m_liveSlides.isEmpty()) {
        QMessageBox::information(this, QStringLiteral("Exportar a PDF"),
                                 QStringLiteral("No hay contenido en vivo. Proyecta una canción, "
                                                "versículo o presentación primero."));
        return;
    }
    const QString out = QFileDialog::getSaveFileName(this, QStringLiteral("Exportar en vivo a PDF"),
                                                     QStringLiteral("escenario.pdf"),
                                                     QStringLiteral("PDF (*.pdf)"));
    if (out.isEmpty()) return;

    QPdfWriter pdf(out);
    pdf.setResolution(96);
    // Página 16:9 (1920x1080 px @96dpi = 1440x810 pt)
    pdf.setPageSize(QPageSize(QSize(1440, 810), QPageSize::Point));
    pdf.setTitle(QStringLiteral("LuminaPresentation Suite — %1").arg(m_liveLabel));

    QPainter painter;
    if (!painter.begin(&pdf)) {
        QMessageBox::warning(this, QStringLiteral("Exportar a PDF"),
                             QStringLiteral("No se pudo crear el archivo PDF:\n%1").arg(out));
        return;
    }
    const QSize pagePx(pdf.width(), pdf.height());
    for (int i = 0; i < m_liveSlides.size(); ++i) {
        if (i > 0) pdf.newPage();
        const QPixmap pm = Renderer::render(m_theme, m_liveSlides.at(i), pagePx,
                                            Renderer::Options());
        painter.drawPixmap(0, 0, pm);
    }
    painter.end();
    QMessageBox::information(this, QStringLiteral("Exportar a PDF"),
                             QStringLiteral("PDF generado con %1 diapositiva(s):\n%2")
                                 .arg(m_liveSlides.size()).arg(out));
}

void MainWindow::closeOverlay()
{
    if (!m_overlayActive) return;
    m_overlayActive = false;
    m_liveSlides = m_savedSlides;
    m_liveIndex = m_savedIndex;
    m_liveLabel = m_savedLabel;
    m_liveInfo->setText(m_liveLabel);
    updateSlideList();
    if (m_liveIndex >= 0 && m_liveIndex < m_liveSlides.size())
        showSlideIndex(m_liveIndex, false);
}

// ---------------------------------------------------------------------------
// Medios
// ---------------------------------------------------------------------------
void MainWindow::onPlayMedia(const QString &path, bool asBackground, bool loop, int fitMode, bool isVideo)
{
    if (!m_ctx.media->available()) {
        QMessageBox::warning(this, QStringLiteral("Multimedia"),
                             QStringLiteral("LibVLC no está disponible. Copia la carpeta 'vlc/' junto al ejecutable."));
        return;
    }
    reassignOutputs();
    if (asBackground) {
        // Video de fondo en bucle; texto sigue disponible
        if (m_ctx.media->playBackground(path)) {
            m_ctx.media->attachBackground(m_output->videoHost());
            m_output->setVideoFitMode(fitMode);
            m_output->setVideoVisible(true);
            m_ctx.media->startBackground();
            m_output->setThemeBackground(m_theme);
        }
        return;
    }
    // Medios como salida principal
    if (m_ctx.media->playMain(path, m_output->videoHost(), loop, isVideo)) {
        m_output->setVideoFitMode(fitMode);
        m_output->setVideoVisible(isVideo);
        QTimer::singleShot(400, this, [this]() {
            const QSize nat = m_ctx.media->nativeVideoSize();
            if (nat.isValid()) { m_output->m_videoSize = nat; m_output->relayoutVideo(); }
        });
        m_ctx.triggers->fireEvent(QStringLiteral("media_play"), { { QStringLiteral("item"), path } });
        pushWebState();
    }
}

void MainWindow::onStopMedia()
{
    stopBackgroundVideo();
    m_ctx.media->stopMain();
    m_output->setVideoVisible(false);
    m_ctx.triggers->fireEvent(QStringLiteral("media_stop"));
}

void MainWindow::onVolume(int vol)
{
    m_ctx.media->setVolume(vol);
}

void MainWindow::stopBackgroundVideo()
{
    m_ctx.media->stopBackground();
    m_output->setVideoVisible(false);
}

void MainWindow::playThemeBackgroundVideo(const Theme &theme)
{
    if (!m_ctx.media->available() || theme.background.videoPath.isEmpty()) return;
    if (m_ctx.media->playBackground(theme.background.videoPath)) {
        m_ctx.media->attachBackground(m_output->videoHost());
        m_output->setVideoFitMode(0);
        m_output->setVideoVisible(true);
        m_ctx.media->startBackground();
    }
}

// ---------------------------------------------------------------------------
// Salidas / pantallas
// ---------------------------------------------------------------------------
void MainWindow::rebuildScreenCombos()
{
    m_comboOutputScreen->blockSignals(true);
    m_comboStageScreen->blockSignals(true);
    m_comboOutputScreen->clear();
    m_comboStageScreen->clear();
    const QList<QScreen *> screens = QGuiApplication::screens();
    for (int i = 0; i < screens.size(); ++i) {
        const QString label = QStringLiteral("Pantalla %1 (%2x%3)")
                .arg(i + 1).arg(screens.at(i)->geometry().width())
                .arg(screens.at(i)->geometry().height());
        m_comboOutputScreen->addItem(label, i);
        m_comboStageScreen->addItem(label, i);
    }
    if (screens.size() > 1) {
        m_comboOutputScreen->setCurrentIndex(1);
    }
    m_comboOutputScreen->blockSignals(false);
    m_comboStageScreen->blockSignals(false);
}

void MainWindow::reassignOutputs()
{
    m_outputScreen = m_comboOutputScreen->currentData().isValid() ? m_comboOutputScreen->currentData().toInt() : -1;
    m_stageScreen = m_comboStageScreen->currentData().isValid() ? m_comboStageScreen->currentData().toInt() : 0;
    const int fade = m_ctx.db->setting(QStringLiteral("fade_ms"), QStringLiteral("250")).toInt();
    m_output->setFadeMs(fade);

    const QSize outSize = m_output->isVisible() ? m_output->size() : QSize(1920, 1080);
    if (m_ctx.db && m_outputScreen >= 0) {
        DisplayEngine engine(this);
        if (engine.showOutputOn(m_outputScreen, m_output)) {
            m_output->resizeTo(QSize(1920, 1080));
            if (m_liveIndex >= 0 && !m_liveSlides.isEmpty())
                showSlideIndex(m_liveIndex, false);
            else
                m_output->showClear();
        }
    }
    if (m_stageOn && m_stageScreen >= 0) {
        DisplayEngine engine(this);
        QScreen *scr = engine.screenAt(m_stageScreen);
        if (scr) {
            m_stage->setGeometry(scr->geometry());
            m_stage->showFullScreen();
            updateStage();
        }
    }
    Q_UNUSED(outSize)
}

void MainWindow::toggleOutput()
{
    if (m_output->isVisible()) {
        // CORRECCION v1.2.0: al ocultar la salida el audio/video seguía
        // sonando de fondo. Ahora se detiene el medio al cerrar la proyección.
        onStopMedia();
        m_output->hide();
        statusBar()->showMessage(QStringLiteral("Salida de audiencia cerrada."), 3000);
    } else {
        reassignOutputs();
        statusBar()->showMessage(QStringLiteral("Salida de audiencia activa."), 3000);
    }
}

void MainWindow::toggleStageView()
{
    if (m_stageOn) {
        m_stage->hide();
        m_stageOn = false;
    } else {
        m_stageOn = true;
        reassignOutputs();
    }
}

// ---------------------------------------------------------------------------
// Stage / preview / listas
// ---------------------------------------------------------------------------
void MainWindow::updateStage()
{
    if (!m_stage->isVisible()) return;
    const bool chords = m_ctx.db->setting(QStringLiteral("stage_chords"), QStringLiteral("1")) == QStringLiteral("1");
    if (m_liveIndex >= 0 && m_liveIndex < m_liveSlides.size()) {
        const Slide &cur = m_liveSlides.at(m_liveIndex);
        const Slide *next = (m_liveIndex + 1 < m_liveSlides.size()) ? &m_liveSlides.at(m_liveIndex + 1) : nullptr;
        // Con acordes solo si se pide: quita acordes de las lineas si no
        Slide c = cur;
        if (!chords) for (SlideLine &l : c.lines) l.chords.clear();
        Slide n;
        if (next) {
            n = *next;
            if (!chords) for (SlideLine &l : n.lines) l.chords.clear();
        }
        // v1.1.0: transposición EN VIVO de las cifras del Stage View
        if (m_stageTranspose != 0) {
            for (SlideLine &l : c.lines)
                if (!l.chords.isEmpty())
                    l.chords = Chords::transposeLine(l.chords, m_stageTranspose, true);
            for (SlideLine &l : n.lines)
                if (!l.chords.isEmpty())
                    l.chords = Chords::transposeLine(l.chords, m_stageTranspose, true);
        }
        m_stage->setThemeColors(m_theme);
        m_stage->updateSlide(c, next ? &n : nullptr, m_theme);
        // El tono/BPM solo aplica a canciones (antes se consultaba songById
        // para cualquier tipo de slide, mostrando "Tono: " vacio).
        if (m_liveRefKind == ServiceItem::Song && m_liveRefId > 0)
            m_stage->setKeyBpm(QStringLiteral("Tono: %1").arg(m_ctx.db->songById(m_liveRefId).key));
        else
            m_stage->setKeyBpm(QString());
    } else {
        m_stage->clearSlide();
    }
}

void MainWindow::updatePreview()
{
    QPixmap pm;
    const bool live = m_liveIndex >= 0 && m_liveIndex < m_liveSlides.size();
    if (live) {
        Renderer::Options opt;
        pm = Renderer::render(m_theme, m_liveSlides.at(m_liveIndex), QSize(640, 360), opt);
    } else {
        pm = Renderer::renderBackground(m_theme, QSize(640, 360));
    }
    m_preview->setPixmap(pm.scaled(m_preview->size(), Qt::KeepAspectRatio, Qt::SmoothTransformation));

    // v1.3.0 GUI Aurora: marco dorado + chip EN VIVO cuando hay proyección
    if (m_preview->property("live").toBool() != live) {
        m_preview->setProperty("live", live);
        m_preview->style()->unpolish(m_preview);
        m_preview->style()->polish(m_preview);
    }
    m_liveChip->setVisible(live);

    // v1.3.0: mini-preview de la SIGUIENTE slide (vista de moderador, spec
    // PowerPoint) — se renderiza atenuada para distinguirla de la actual.
    QPixmap nextPm;
    if (m_liveIndex + 1 >= 0 && m_liveIndex + 1 < m_liveSlides.size()) {
        Renderer::Options opt;
        nextPm = Renderer::render(m_theme, m_liveSlides.at(m_liveIndex + 1), QSize(320, 180), opt);
    } else {
        nextPm = Renderer::renderBackground(m_theme, QSize(320, 180));
    }
    m_nextPreview->setPixmap(nextPm.scaled(m_nextPreview->size(), Qt::KeepAspectRatio,
                                           Qt::SmoothTransformation));

    // v1.3.0: multiview — miniatura textual del Stage View (spec Holyrics):
    // estrofa actual (con acordes) tal como la ven los músicos.
    QString stageTxt;
    if (live) {
        const Slide &s = m_liveSlides.at(m_liveIndex);
        for (const SlideLine &l : s.lines) {
            if (!l.chords.isEmpty())
                stageTxt += l.chords + QStringLiteral("\n");
            stageTxt += l.text + QStringLiteral("\n");
            if (stageTxt.count(QChar('\n')) >= 6)
                break;
        }
        stageTxt.chop(1);
    }
    m_stageMini->setText(stageTxt.isEmpty() ? QStringLiteral("—") : stageTxt);
}

void MainWindow::updateSlideList()
{
    m_slideList->clear();
    for (const Slide &s : m_liveSlides) {
        QString label = s.refLabel.isEmpty() ? QString() : QStringLiteral("[%1] ").arg(s.refLabel);
        label += Lyrics::plainText(s).section(QChar('\n'), 0, 2).replace(QChar('\n'), QStringLiteral(" / "));
        if (label.isEmpty()) label = QStringLiteral("(vacía)");
        m_slideList->addItem(label);
    }
}

// ---------------------------------------------------------------------------
// Culto
// ---------------------------------------------------------------------------
void MainWindow::runServiceItem(const ServiceItem &item)
{
    switch (item.kind) {
    case ServiceItem::Song: {
        const Song s = m_ctx.db->songById(item.refId);
        goLiveSong(s, ServiceItem::Song, s.id);
        m_ctx.db->touchSongUsage(s.id);
        m_historyPanel->reload();
        break;
    }
    case ServiceItem::Bible: {
        // payload: "versions|book|ch|from|to"
        const QStringList parts = item.payload.split(QChar('|'));
        if (parts.size() >= 5) {
            const QStringList vers = parts.at(0).split(QChar('+'));
            emit m_biblePanel->requestProjectVerses(vers, parts.at(1).toInt(), parts.at(2).toInt(),
                                                    parts.at(3).toInt(), parts.at(4).toInt(),
                                                    QString());
        } else {
            const BibleRef::VerseRef r = BibleRef::resolve(item.payload);
            if (r.valid()) {
                // CORRECCION v1.2.0: esta rama (items guardados como "Jn 3:16"
                // desde el panel Cultos) proyectaba el CAPÍTULO COMPLETO vía
                // bibleChapter(), ignorando versículo y rango guardados. Ahora
                // se proyecta exactamente la referencia pedida.
                const auto rng = BibleRef::rangeOf(item.payload);
                int vFrom = r.verse > 0 ? r.verse : 1;
                int vTo = vFrom;
                if (rng.first == r.chapter && rng.second.first > 0) {
                    vFrom = rng.second.first;
                    vTo = qMax(rng.second.second, vFrom);
                }
                const QString v1 = m_ctx.db->bibleVersions().value(0);
                QVector<Slide> slides;
                const QVector<BibleRef::Verse> verses = m_ctx.db->bibleRange(v1, r.book, r.chapter, vFrom, vTo);
                for (int i = 0; i < verses.size(); i += 2) {
                    Slide s;
                    s.kind = Slide::Bible;
                    s.title = verses.value(0).ref.bookName + QStringLiteral(" %1").arg(r.chapter);
                    s.refLabel = BibleRef::formatRef(verses.at(i).ref) +
                                 (i + 1 < verses.size()
                                      ? QStringLiteral("-%1").arg(verses.at(i + 1).ref.verse)
                                      : QString());
                    for (int j = i; j < qMin(i + 2, verses.size()); ++j)
                        s.lines.append(SlideLine(QStringLiteral("%1 %2").arg(verses.at(j).ref.verse)
                                                 .arg(verses.at(j).text)));
                    slides.append(s);
                }
                goLive(slides, item.label, ServiceItem::Bible, 0);
            }
        }
        break;
    }
    case ServiceItem::Aviso: {
        Slide s;
        s.kind = Slide::Aviso;
        s.title = QStringLiteral("AVISO");
        s.lines.append(SlideLine(item.payload));
        QVector<Slide> v; v.append(s);
        goLive(v, QStringLiteral("Aviso"), ServiceItem::Aviso, 0);
        break;
    }
    case ServiceItem::Custom: {
        // v1.2.0: ejecutar slides del lienzo libre desde la cola. El payload
        // guarda el JSON; si viene vacío se recupera de la DB por refId.
        QJsonObject json;
        if (!item.payload.trimmed().isEmpty())
            json = QJsonDocument::fromJson(item.payload.toUtf8()).object();
        else if (item.refId > 0) {
            bool ok = false;
            json = m_ctx.db->customSlideJson(item.refId, &ok);
        }
        const Slide s = CustomPanel::rasterizeCustomJson(json, item.label);
        QVector<Slide> v; v.append(s);
        goLive(v, item.label.isEmpty() ? QStringLiteral("Lienzo libre") : item.label,
               ServiceItem::Custom, item.refId);
        break;
    }
    case ServiceItem::Pptx: {
        // FIX v1.1.0: ejecutar items PPTX de la cola importando el archivo
        // guardado en payload y rasterizando con el helper compartido.
        const QString path = item.payload.trimmed();
        if (path.isEmpty() || !QFile::exists(path)) {
            QMessageBox::warning(this, QStringLiteral("Culto"),
                                 QStringLiteral("El archivo PowerPoint de este item no está disponible:\n%1")
                                     .arg(path.isEmpty() ? QStringLiteral("(ruta no guardada)") : path));
            break;
        }
        QVector<PptxEngine::PptxSlide> ps;
        QSize sz;
        QString err;
        if (!PptxEngine::importPptx(path, &ps, &sz, &err)) {
            QMessageBox::warning(this, QStringLiteral("Culto"), err);
            break;
        }
        const QString name = QFileInfo(path).completeBaseName();
        goLive(PptxEngine::renderToSlides(ps, sz, m_theme, name),
               name, ServiceItem::Pptx, 0);
        break;
    }
    default:
        QMessageBox::information(this, QStringLiteral("Culto"),
                                 QStringLiteral("Tipo de elemento no ejecutable desde la cola: %1").arg(item.label));
        break;
    }
}

// ---------------------------------------------------------------------------
// Comunicacion
// ---------------------------------------------------------------------------
void MainWindow::onSendAlert(const QString &text)
{
    m_stage->showAlert(text);
    m_ctx.web->broadcastAlert(text);
    m_ctx.db->logAlert(text);
    m_lastAlert = text;
    m_lastAlertAt = QDateTime::currentDateTime();
    m_ctx.triggers->fireEvent(QStringLiteral("alert"), { { QStringLiteral("slide"), text } });
    statusBar()->showMessage(QStringLiteral("Alerta enviada al escenario."), 3000);
}

void MainWindow::onStartCountdown(int minutes)
{
    m_stage->startCountdown(minutes);
    // v1.3.0: espejo de la deadline para el chip de cuenta regresiva del dock
    m_countdownDeadline = QDateTime::currentDateTime().addSecs(minutes * 60);
    m_countdownActive = true;
    if (!m_stage->isVisible()) {
        m_stageOn = true;
        reassignOutputs();
    }
}

void MainWindow::onStopCountdown()
{
    m_stage->stopCountdown();
    m_countdownActive = false;               // v1.3.0
    m_countdownChip->setVisible(false);
}

// ---------------------------------------------------------------------------
// Servidor remoto
// ---------------------------------------------------------------------------
void MainWindow::onRemoteCommand(const QString &cmd, const QJsonObject &data)
{
    if (cmd == QStringLiteral("next")) nextSlide();
    else if (cmd == QStringLiteral("prev")) prevSlide();
    else if (cmd == QStringLiteral("black")) showBlack();
    else if (cmd == QStringLiteral("clear")) showClear();
    else if (cmd == QStringLiteral("logo")) showLogo();
    else if (cmd == QStringLiteral("goto")) showSlideIndex(data.value(QStringLiteral("index")).toInt());
    else if (cmd == QStringLiteral("alert")) onSendAlert(data.value(QStringLiteral("text")).toString());
    else if (cmd == QStringLiteral("ping")) pushWebState();
    // v1.1.0: navegacion de la COLA del culto desde el remoto (spec Holyrics:
    // el operador avanza el orden del servicio desde el movil)
    else if (cmd == QStringLiteral("qnext")) {
        const int row = m_queueList->currentRow() + 1;
        if (row < m_queueData.size()) runQueueAt(row);
    } else if (cmd == QStringLiteral("qprev")) {
        const int row = m_queueList->currentRow() - 1;
        if (row >= 0) runQueueAt(row);
    }
    pushWebState();
}

// ---------------------------------------------------------------------------
// v1.1.0: ejecutar un item concreto de la cola (remoto qnext/qprev + doble clic)
// ---------------------------------------------------------------------------
void MainWindow::runQueueAt(int row)
{
    if (row < 0 || row >= m_queueData.size()) return;
    m_queueList->setCurrentRow(row);
    runServiceItem(m_queueData.at(row));
}

void MainWindow::pushWebState()
{
    QJsonObject st;
    st["ev"] = QStringLiteral("state");
    st["app"] = QStringLiteral("LuminaPresentation Suite");
    st["version"] = QApplication::applicationVersion();
    st["mode"] = m_output->mode() == OutputWindow::Black ? QStringLiteral("black")
                 : m_output->mode() == OutputWindow::Clear ? QStringLiteral("clear")
                 : m_output->mode() == OutputWindow::Logo ? QStringLiteral("logo")
                 : QStringLiteral("content");
    if (m_liveIndex >= 0 && m_liveIndex < m_liveSlides.size()) {
        const Slide &s = m_liveSlides.at(m_liveIndex);
        st["index"] = m_liveIndex;
        st["count"] = m_liveSlides.size();
        st["title"] = s.title;
        st["ref"] = s.refLabel;
        QJsonArray lines;
        for (const SlideLine &l : s.lines) lines.append(l.text);
        st["lines"] = lines;
        st["item"] = m_liveLabel;
    } else {
        st["index"] = -1;
        st["count"] = 0;
        st["item"] = QString();
    }
    // Alerta vigente para el overlay OBS (12 s)
    if (m_lastAlertAt.isValid() && m_lastAlertAt.secsTo(QDateTime::currentDateTime()) < 12)
        st["alert"] = m_lastAlert;
    else
        st["alert"] = QString();
    m_ctx.web->setLiveState(st);
}

// ---------------------------------------------------------------------------
// Eventos de teclado / cierre
// ---------------------------------------------------------------------------
void MainWindow::keyPressEvent(QKeyEvent *ev)
{
    QMainWindow::keyPressEvent(ev);
}

void MainWindow::closeEvent(QCloseEvent *ev)
{
    if (m_output->isVisible() || m_stage->isVisible()) {
        if (QMessageBox::question(this, QStringLiteral("Salir"),
                                  QStringLiteral("Hay salidas activas. ¿Cerrar LuminaPresentation Suite?"))
            != QMessageBox::Yes) {
            ev->ignore();
            return;
        }
    }
    // Detener el servidor embebido y el motor multimedia de forma ordenada
    m_ctx.web->stop();
    m_ctx.media->shutdown();
    // CORRECCION v1.2.0 (app "zombie"): las ventanas de salida son top-level
    // sin parent y permanecían VISIBLES tras aceptar el cierre -> como Qt solo
    // termina el event loop cuando no quedan ventanas, el proceso seguía vivo
    // con la proyección congelada y sin forma de controlarlo. Se ocultan
    // explícitamente para que la aplicación termine de verdad.
    m_output->hide();
    m_stage->hide();
    ev->accept();
}

// ---------------------------------------------------------------------------
// v1.3.0 — GUI Aurora: Modo Presentación, reloj, Acerca de, export PNG,
// chip del servidor, drag & drop
// ---------------------------------------------------------------------------
void MainWindow::togglePresentationMode()
{
    // Spec maestro: «La interfaz de usuario principal para la presentación debe
    // ser absolutamente minimalista». F11 oculta toolbar/paneles; el dock de
    // proyección se expande a toda la ventana (QMainWindow expande las áreas
    // de dock cuando el widget central está oculto) y aparece la barra de
    // transporte grande. F11 de nuevo restaura todo. La barra de estado queda
    // (una línea) como pista de cómo salir del modo.
    m_presentationMode = !m_presentationMode;
    if (m_presentationMode) {
        centralWidget()->hide();
        for (QToolBar *tb : findChildren<QToolBar *>())
            tb->hide();
        m_presBar->setVisible(true);
        statusBar()->showMessage(
            QStringLiteral("🖥 MODO PRESENTACIÓN — F11 para volver a la consola completa"));
    } else {
        m_presBar->setVisible(false);
        for (QToolBar *tb : findChildren<QToolBar *>())
            tb->show();
        centralWidget()->show();
        statusBar()->showMessage(
            QStringLiteral("LuminaPresentation Suite %1 — Salida audiencia: %2 · Stage: %3")
                .arg(QApplication::applicationVersion(),
                     m_ctx.db->setting(QStringLiteral("screen_output"), QStringLiteral("auto")),
                     m_ctx.db->setting(QStringLiteral("screen_stage"), QStringLiteral("auto"))));
    }
}

void MainWindow::onClockTick()
{
    // Reloj del dock + chip de cuenta regresiva (espejo del Stage View)
    m_clockLabel->setText(QTime::currentTime().toString(QStringLiteral("hh:mm:ss")));
    if (m_countdownActive && m_countdownDeadline.isValid()) {
        const qint64 secs = QDateTime::currentDateTime().secsTo(m_countdownDeadline);
        if (secs > 0) {
            m_countdownChip->setText(QStringLiteral("⏱ %1")
                .arg(QTime(0, 0).addSecs(int(secs)).toString(QStringLiteral("hh:mm:ss"))));
            m_countdownChip->setVisible(true);
        } else {
            m_countdownChip->setText(QStringLiteral("⏱ ¡TIEMPO!"));
            m_countdownChip->setVisible(true);
            m_countdownActive = false;    // el chip queda fijo hasta detener/reiniciar
        }
    }
}

void MainWindow::updateSrvChip()
{
    if (!m_srvChip) return;
    const bool on = m_ctx.web && m_ctx.web->running();
    if (m_srvChip->property("on").toBool() == on)
        return;
    m_srvChip->setProperty("on", on);
    m_srvChip->style()->unpolish(m_srvChip);
    m_srvChip->style()->polish(m_srvChip);
    const int httpPort = m_ctx.db ? m_ctx.db->setting(QStringLiteral("http_port"),
                                                      QStringLiteral("8088")).toInt() : 8088;
    m_srvChip->setText(on ? QStringLiteral("● Servidor :%1").arg(httpPort)
                          : QStringLiteral("● Servidor apagado"));
}

void MainWindow::showAbout()
{
    QMessageBox about(this);
    about.setWindowTitle(QStringLiteral("Acerca de LuminaPresentation Suite"));
    about.setIconPixmap(QPixmap(QStringLiteral(":/img/logo128.png")));
    about.setText(QStringLiteral("<h3 style='color:#7FA8F0;'>LuminaPresentation Suite %1</h3>"
                                  "<p>Proyección multimedia híbrida nativa — la convergencia entre la "
                                  "agilidad operativa de Holyrics y la potencia de composición de PowerPoint.</p>"
                                  "<p style='color:#93A7CC;'><b>C++17 · Qt 5.15 LTS · LibVLC 3 · SQLite FTS5</b><br>"
                                  "Sin JVM · sin .NET · sin Electron — portable de Windows 7 SP1 a Windows 11.</p>")
                       .arg(QApplication::applicationVersion()));
    about.setInformativeText(QStringLiteral(
        "<p style='color:#93A7CC;'>Control remoto: <code>http://&lt;ip&gt;:8088/remote.html</code> · "
        "Overlay OBS: <code>http://&lt;ip&gt;:8088/overlay.html</code><br>"
        "Uso de los binarios permitido; código fuente bajo Licencia View-Only (LICENSE.md).</p>"));
    about.setStandardButtons(QMessageBox::Ok);
    about.setDefaultButton(QMessageBox::Ok);
    about.exec();
}

void MainWindow::exportLivePng()
{
    // v1.3.0 — spec PowerPoint: «exportar diapositivas individuales como
    // imágenes (.png...)». Exporta TODO el elemento en vivo (no solo la slide
    // actual) a una carpeta, a 1920x1080.
    if (m_liveSlides.isEmpty()) {
        QMessageBox::information(this, QStringLiteral("Exportar a PNG"),
                                 QStringLiteral("No hay contenido en vivo. Proyecta una canción, "
                                                "versículo o presentación primero."));
        return;
    }
    const QString dir = QFileDialog::getExistingDirectory(
        this, QStringLiteral("Carpeta destino para los PNG"),
        QDir::homePath(), QFileDialog::ShowDirsOnly | QFileDialog::DontResolveSymlinks);
    if (dir.isEmpty()) return;

    const QString base = QStringLiteral("%1/%2")
            .arg(dir, m_liveLabel.isEmpty() ? QStringLiteral("slide")
                                            : m_liveLabel.simplified().replace(QRegExp(QStringLiteral("[^A-Za-z0-9ÁÉÍÓÚÜÑáéíóúüñ _-]")), QStringLiteral("_")));
    int saved = 0;
    for (int i = 0; i < m_liveSlides.size(); ++i) {
        const QPixmap pm = Renderer::render(m_theme, m_liveSlides.at(i),
                                            QSize(1920, 1080), Renderer::Options());
        const QString out = QStringLiteral("%1_%2.png").arg(base).arg(i + 1, 2, 10, QLatin1Char('0'));
        if (pm.save(out, "PNG"))
            ++saved;
    }
    if (saved == m_liveSlides.size()) {
        QMessageBox::information(this, QStringLiteral("Exportar a PNG"),
                                 QStringLiteral("%1 imagen(es) PNG (1920×1080) guardadas en:\n%2")
                                     .arg(saved).arg(dir));
    } else {
        QMessageBox::warning(this, QStringLiteral("Exportar a PNG"),
                             QStringLiteral("Solo se pudieron guardar %1 de %2 imágenes "
                                            "(revisa permisos de la carpeta).").arg(saved).arg(m_liveSlides.size()));
    }
}

void MainWindow::importSongFromTxt(const QString &path)
{
    // v1.3.0 — drop de .txt: importa una canción (formato de texto plano con
    // directivas @titulo/@autor/@tono/@bpm/@tags, o primera línea = título).
    QFile f(path);
    if (!f.open(QIODevice::ReadOnly | QIODevice::Text)) {
        statusBar()->showMessage(QStringLiteral("No se pudo leer el archivo arrastrado."), 4000);
        return;
    }
    const QString content = QString::fromUtf8(f.readAll());
    f.close();

    Song s;
    QString lyrics = content;
    // Directivas opcionales al inicio (mismo formato que la importación .txt)
    static const QRegularExpression rxTitle(QStringLiteral("^@titulo\\s+(.+)$"),
                                            QRegularExpression::MultilineOption);
    static const QRegularExpression rxArtist(QStringLiteral("^@autor\\s+(.+)$"),
                                             QRegularExpression::MultilineOption);
    static const QRegularExpression rxKey(QStringLiteral("^@tono\\s+(.+)$"),
                                          QRegularExpression::MultilineOption);
    static const QRegularExpression rxBpm(QStringLiteral("^@bpm\\s+(\\d+)$"),
                                          QRegularExpression::MultilineOption);
    QRegularExpressionMatch m;
    if ((m = rxTitle.match(lyrics)).hasMatch()) { s.title = m.captured(1).trimmed(); lyrics.remove(m.capturedStart(), m.capturedLength()); }
    if ((m = rxArtist.match(lyrics)).hasMatch()) { s.artist = m.captured(1).trimmed(); lyrics.remove(m.capturedStart(), m.capturedLength()); }
    if ((m = rxKey.match(lyrics)).hasMatch()) { s.key = m.captured(1).trimmed(); lyrics.remove(m.capturedStart(), m.capturedLength()); }
    if ((m = rxBpm.match(lyrics)).hasMatch()) { s.bpm = m.captured(1).toInt(); lyrics.remove(m.capturedStart(), m.capturedLength()); }
    if (s.title.isEmpty())
        s.title = QFileInfo(path).completeBaseName();     // fallback: nombre del archivo
    s.lyrics = lyrics.trimmed();

    if (s.lyrics.isEmpty()) {
        statusBar()->showMessage(QStringLiteral("El archivo no contiene letra."), 4000);
        return;
    }
    m_ctx.db->addSong(s);
    m_songPanel->reload();
    statusBar()->showMessage(QStringLiteral("Canción importada: %1").arg(s.title), 5000);
}

void MainWindow::dragEnterEvent(QDragEnterEvent *ev)
{
    // Acepta archivos locales: imagen/video -> fondo en vivo; .txt -> canción
    if (ev->mimeData()->hasUrls()) {
        for (const QUrl &u : ev->mimeData()->urls()) {
            if (u.isLocalFile()) { ev->acceptProposedAction(); return; }
        }
    }
    QMainWindow::dragEnterEvent(ev);
}

void MainWindow::dropEvent(QDropEvent *ev)
{
    if (!ev->mimeData()->hasUrls()) { QMainWindow::dropEvent(ev); return; }
    for (const QUrl &u : ev->mimeData()->urls()) {
        if (!u.isLocalFile()) continue;
        const QString file = u.toLocalFile();
        const QString ext = QFileInfo(file).suffix().toLower();

        if (ext == QStringLiteral("txt")) {
            importSongFromTxt(file);
        } else if (ext == QStringLiteral("png") || ext == QStringLiteral("jpg") ||
                   ext == QStringLiteral("jpeg") || ext == QStringLiteral("bmp")) {
            // Imagen -> fondo del tema en vivo (aplicación inmediata, spec
            // Holyrics: «importar recursos directamente arrastrándolos»)
            m_theme.background.type = 2;
            m_theme.background.imagePath = file;
            updatePreview();
            if (m_output->isVisible())
                showSlideIndex(m_liveIndex < 0 ? 0 : m_liveIndex, false);
            statusBar()->showMessage(QStringLiteral("Fondo de imagen aplicado: %1")
                                         .arg(QFileInfo(file).fileName()), 4000);
        } else if (ext == QStringLiteral("mp4") || ext == QStringLiteral("avi") ||
                   ext == QStringLiteral("mkv") || ext == QStringLiteral("mov") ||
                   ext == QStringLiteral("webm")) {
            // Video -> fondo en bucle con el texto encima
            onPlayMedia(file, /*asBackground=*/true, /*loop=*/true, /*fitMode=*/0, /*isVideo=*/true);
            statusBar()->showMessage(QStringLiteral("Fondo de video en bucle: %1")
                                         .arg(QFileInfo(file).fileName()), 4000);
        }
    }
    ev->acceptProposedAction();
}
