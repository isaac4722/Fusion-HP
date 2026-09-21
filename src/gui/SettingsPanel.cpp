// ============================================================================
//  LuminaPresentation Suite - SettingsPanel.cpp
// ============================================================================
#include "SettingsPanel.h"
#include "core/Database.h"
#include "core/MediaEngine.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGroupBox>
#include <QFormLayout>
#include <QMessageBox>
#include <QDir>
#include <QFileDialog>
#include <QDateTime>
#include <QDesktopServices>
#include <QUrl>
#include <QGuiApplication>
#include <QScreen>
#include <QSysInfo>
#include <tuple>

SettingsPanel::SettingsPanel(AppContext *ctx, QWidget *parent)
    : QWidget(parent), m_ctx(ctx)
{
    buildUi();
    refreshScreens();
}

void SettingsPanel::buildUi()
{
    auto *lay = new QVBoxLayout(this);
    lay->setContentsMargins(12, 12, 12, 12);

    auto *grpScr = new QGroupBox(QStringLiteral("Pantallas"), this);
    auto *f1 = new QFormLayout(grpScr);
    m_screenOutput = new QComboBox(grpScr);
    m_screenStage = new QComboBox(grpScr);
    f1->addRow(QStringLiteral("Salida de audiencia:"), m_screenOutput);
    f1->addRow(QStringLiteral("Stage View (escenario):"), m_screenStage);
    lay->addWidget(grpScr);

    auto *grpWeb = new QGroupBox(QStringLiteral("Servidor embebido (control remoto / overlay OBS)"), this);
    auto *f2 = new QFormLayout(grpWeb);
    m_wsPort = new QSpinBox(grpWeb);
    m_wsPort->setRange(1024, 65535);
    m_wsPort->setValue(8765);
    m_httpPort = new QSpinBox(grpWeb);
    m_httpPort->setRange(1024, 65535);
    m_httpPort->setValue(8088);
    f2->addRow(QStringLiteral("Puerto WebSocket:"), m_wsPort);
    f2->addRow(QStringLiteral("Puerto HTTP:"), m_httpPort);
    m_autoStart = new QCheckBox(QStringLiteral("Iniciar servidor automáticamente"), grpWeb);
    m_autoStart->setChecked(true);
    f2->addRow(m_autoStart);
    lay->addWidget(grpWeb);

    auto *grpGen = new QGroupBox(QStringLiteral("General"), this);
    auto *f3 = new QFormLayout(grpGen);
    m_fade = new QSlider(Qt::Horizontal, grpGen);
    m_fade->setRange(0, 800);
    m_fade->setValue(250);
    f3->addRow(QStringLiteral("Duración de transición (ms):"), m_fade);
    m_defaultTheme = new QComboBox(grpGen);
    for (const auto &t : m_ctx->db->themes())
        m_defaultTheme->addItem(t.second, t.first);
    f3->addRow(QStringLiteral("Tema activo:"), m_defaultTheme);
    m_stageWithChords = new QCheckBox(QStringLiteral("Mostrar cifras/acordes en Stage View"), grpGen);
    m_stageWithChords->setChecked(true);
    f3->addRow(m_stageWithChords);
    lay->addWidget(grpGen);

    // v1.1.0: ajustes de canciones (spec: Modo Hinario, densidad de slides)
    auto *grpSongs = new QGroupBox(QStringLiteral("Canciones"), this);
    auto *f4 = new QFormLayout(grpSongs);
    m_titleSlide = new QCheckBox(QStringLiteral("Incluir slide de título al proyectar"), grpSongs);
    m_titleSlide->setChecked(true);
    f4->addRow(m_titleSlide);
    m_hinarioMode = new QCheckBox(QStringLiteral("Modo Hinario (intercalar el coro tras cada verso)"), grpSongs);
    m_hinarioMode->setToolTip(QStringLiteral("Formato tradicional: después de cada verso se proyecta "
                                            "automáticamente el coro de la canción."));
    f4->addRow(m_hinarioMode);
    m_maxLines = new QSpinBox(grpSongs);
    m_maxLines->setRange(2, 8);
    m_maxLines->setSuffix(QStringLiteral(" líneas por slide"));
    m_maxLines->setValue(4);
    f4->addRow(QStringLiteral("Densidad de proyección:"), m_maxLines);
    lay->addWidget(grpSongs);

    // v1.1.0: token opcional de la API HTTP (espec Holyrics)
    auto *grpApi = new QGroupBox(QStringLiteral("API HTTP (integraciones / Companion)"), this);
    auto *f5 = new QFormLayout(grpApi);
    m_apiToken = new QLineEdit(grpApi);
    m_apiToken->setPlaceholderText(QStringLiteral("(vacío = sin autenticación en la red local)"));
    m_apiToken->setEchoMode(QLineEdit::Password);
    f5->addRow(QStringLiteral("Token de la API:"), m_apiToken);
    QLabel *apiHelp = new QLabel(QStringLiteral(
        "Endpoints: <code>/api/cmd?c=next|prev|black|clear|logo|goto|i=N|qnext|qprev|alert&text=…</code> · "
        "<code>/api/live.txt</code> (fuente de texto para OBS) · <code>/api/state</code>"), grpApi);
    apiHelp->setWordWrap(true);
    apiHelp->setTextFormat(Qt::RichText);
    f5->addRow(apiHelp);
    lay->addWidget(grpApi);

    // ---------------------------------------------------------------------
    // v1.3.0 — ATAJOOS PERSONALIZABLES (spec Holyrics: "atajos de teclado
    // personalizables"). Se guardan como texto QKeySequence en settings y
    // MainWindow::buildShortcuts los aplica al arranque.
    // ---------------------------------------------------------------------
    auto *grpKeys = new QGroupBox(QStringLiteral("Atajos de teclado (personalizables)"), this);
    auto *fk = new QFormLayout(grpKeys);
    // (clave de ajuste, tecla default, etiqueta) — mismas claves que MainWindow
    const QVector<std::tuple<const char *, int, const char *>> defs = {
        { "key_next",         Qt::Key_Right, "Siguiente slide:" },
        { "key_prev",         Qt::Key_Left,  "Slide anterior:" },
        { "key_golive",       Qt::Key_F5,    "Proyectar en vivo:" },
        { "key_black",        Qt::Key_B,     "Pantalla negra:" },
        { "key_clear",        Qt::Key_C,     "Solo fondo (limpiar):" },
        { "key_logo",         Qt::Key_L,     "Logo:" },
        { "key_quickverse",   Qt::Key_F9,    "Versículo rápido:" },
        { "key_lowerthird",   Qt::Key_F10,   "Lower Third:" },
        { "key_presentation", Qt::Key_F11,   "Modo Presentación:" },
    };
    for (const auto &d : defs) {
        const QByteArray key = std::get<0>(d);
        auto *ed = new QKeySequenceEdit(grpKeys);
        const QString saved = m_ctx->db->setting(QLatin1String(key.constData()));
        ed->setKeySequence(saved.trimmed().isEmpty() ? QKeySequence(std::get<1>(d))
                                                     : QKeySequence(saved));
        fk->addRow(QString::fromUtf8(std::get<2>(d)), ed);
        m_shortcutDefs.append(qMakePair(key, std::get<1>(d)));
        m_shortcutEdits.append(ed);
    }
    auto *rowKeys = new QHBoxLayout();
    auto *bResetKeys = new QPushButton(QStringLiteral("↺ Restablecer valores de fábrica"), grpKeys);
    connect(bResetKeys, &QPushButton::clicked, this, &SettingsPanel::onResetShortcuts);
    rowKeys->addWidget(bResetKeys);
    rowKeys->addStretch();
    fk->addRow(rowKeys);
    lay->addWidget(grpKeys);

    // ---------------------------------------------------------------------
    // v1.3.0 — COPIA DE SEGURIDAD del vault (alternativa offline-safe a la
    // sincronización en la nube del spec Holyrics). Copia automática semanal
    // en <datos>/backups (rotación de 4); aquí se ofrecen copias manuales.
    // ---------------------------------------------------------------------
    auto *grpBackup = new QGroupBox(QStringLiteral("Copia de seguridad (todo el vault)"), this);
    auto *fb = new QVBoxLayout(grpBackup);
    auto *rowBackup = new QHBoxLayout();
    auto *bBackup = new QPushButton(QStringLiteral("💾 Crear copia de seguridad…"), grpBackup);
    bBackup->setProperty("class", QStringLiteral("primary"));
    auto *bRestore = new QPushButton(QStringLiteral("↩ Restaurar copia…"), grpBackup);
    connect(bBackup, &QPushButton::clicked, this, &SettingsPanel::onBackup);
    connect(bRestore, &QPushButton::clicked, this, &SettingsPanel::onRestore);
    rowBackup->addWidget(bBackup);
    rowBackup->addWidget(bRestore);
    rowBackup->addStretch();
    fb->addLayout(rowBackup);
    m_backupStatus = new QLabel(QString(), grpBackup);
    m_backupStatus->setObjectName(QStringLiteral("MutedLabel"));
    m_backupStatus->setWordWrap(true);
    fb->addWidget(m_backupStatus);
    const QString lastAuto = m_ctx->db->setting(QStringLiteral("last_auto_backup"));
    const QDateTime lastAutoAt = QDateTime::fromString(lastAuto, Qt::ISODate);
    if (lastAutoAt.isValid()) {
        m_backupStatus->setText(QStringLiteral("Última copia automática: %1 (semanal, en la "
                                                "subcarpeta 'backups' de tus datos)")
                                    .arg(lastAutoAt.toString(QStringLiteral("dd/MM/yyyy hh:mm"))));
    } else {
        m_backupStatus->setText(QStringLiteral("Copia automática semanal activada (subcarpeta 'backups')."));
    }
    lay->addWidget(grpBackup);

    // v1.1.0: cargar los valores guardados de los nuevos ajustes
    m_hinarioMode->setChecked(m_ctx->db->setting(QStringLiteral("song_hinario"), QStringLiteral("0")) == QStringLiteral("1"));
    m_maxLines->setValue(m_ctx->db->setting(QStringLiteral("song_maxlines"), QStringLiteral("4")).toInt());
    m_titleSlide->setChecked(m_ctx->db->setting(QStringLiteral("song_title_slide"), QStringLiteral("1")) == QStringLiteral("1"));
    m_apiToken->setText(m_ctx->db->setting(QStringLiteral("api_token")));

    // CORRECCION v1.2.0 (DEFECTO CRÍTICO — pérdida de configuración): el panel
    // NUNCA cargaba los valores persistidos de ws_port, http_port, fade_ms,
    // default_theme, server_autostart ni stage_chords; los combos de pantalla
    // además se reiniciaban a "Pantalla 2"/"Pantalla 1". Escenario real: el
    // usuario configuraba WS 9000 y fade 700 → reiniciaba → abría Ajustes (veía
    // 8765/250) → pulsaba "Aplicar" para cambiar solo el token → TODOS los
    // demás ajustes quedaban silenciosamente machacados con los defaults.
    // Ahora el panel refleja el estado persistido real antes de cualquier Apply.
    m_wsPort->setValue(m_ctx->db->setting(QStringLiteral("ws_port"), QStringLiteral("8765")).toInt());
    m_httpPort->setValue(m_ctx->db->setting(QStringLiteral("http_port"), QStringLiteral("8088")).toInt());
    m_fade->setValue(m_ctx->db->setting(QStringLiteral("fade_ms"), QStringLiteral("250")).toInt());
    m_autoStart->setChecked(m_ctx->db->setting(QStringLiteral("server_autostart"), QStringLiteral("1")) == QStringLiteral("1"));
    m_stageWithChords->setChecked(m_ctx->db->setting(QStringLiteral("stage_chords"), QStringLiteral("1")) == QStringLiteral("1"));
    const int savedTheme = m_ctx->db->setting(QStringLiteral("default_theme"), QStringLiteral("1")).toInt();
    if (const int tIdx = m_defaultTheme->findData(savedTheme); tIdx >= 0)
        m_defaultTheme->setCurrentIndex(tIdx);

    auto *row = new QHBoxLayout();
    auto *bApply = new QPushButton(QStringLiteral("💾 Aplicar configuración"), this);
    bApply->setProperty("class", QStringLiteral("primary"));
    auto *bFolder = new QPushButton(QStringLiteral("Abrir carpeta de datos"), this);
    connect(bApply, &QPushButton::clicked, this, &SettingsPanel::apply);
    connect(bFolder, &QPushButton::clicked, this, &SettingsPanel::onOpenDataFolder);
    row->addWidget(bApply);
    row->addWidget(bFolder);
    row->addStretch();
    lay->addLayout(row);

    m_sysInfo = new QLabel(QString(), this);
    m_sysInfo->setWordWrap(true);
    m_sysInfo->setObjectName(QStringLiteral("MutedLabel"));
    lay->addWidget(m_sysInfo);
    lay->addStretch();
}

void SettingsPanel::refreshScreens()
{
    m_screenOutput->clear();
    m_screenStage->clear();
    const QList<QScreen *> screens = QGuiApplication::screens();
    for (int i = 0; i < screens.size(); ++i) {
        const QString label = QStringLiteral("Pantalla %1 — %2x%3")
                .arg(i + 1).arg(screens.at(i)->geometry().width())
                .arg(screens.at(i)->geometry().height());
        m_screenOutput->addItem(label, i);
        m_screenStage->addItem(label, i);
    }
    // CORRECCION v1.2.0: el combo ignoraba la pantalla GUARDADA y siempre
    // forzaba "Pantalla 2"/"Pantalla 1" (perdía la selección del usuario al
    // entrar al panel y Aplicar la machacaba). Ahora se restaura la persistida.
    const int savedOut = m_ctx->db->setting(QStringLiteral("screen_output"), QStringLiteral("-1")).toInt();
    const int savedStg = m_ctx->db->setting(QStringLiteral("screen_stage"), QStringLiteral("0")).toInt();
    if (m_screenOutput->findData(savedOut) >= 0) {
        m_screenOutput->setCurrentIndex(m_screenOutput->findData(savedOut));
    } else if (screens.size() > 1) {
        m_screenOutput->setCurrentIndex(1);     // segunda pantalla para audiencia
    }
    if (m_screenStage->findData(savedStg) >= 0)
        m_screenStage->setCurrentIndex(m_screenStage->findData(savedStg));
    // Info de sistema
    const bool vlc = m_ctx->media && m_ctx->media->available();
    m_sysInfo->setText(QStringLiteral(
        "Sistema: %1 · Pantallas: %2 · Motor multimedia: %3 · Arquitectura: %4 bits")
            .arg(QSysInfo::prettyProductName())
            .arg(screens.size())
            .arg(vlc ? m_ctx->media->version() : QStringLiteral("sin LibVLC (modo texto)"))
            .arg(QSysInfo::buildCpuArchitecture()));
}

void SettingsPanel::apply()
{
    m_ctx->db->setSetting(QStringLiteral("screen_output"), QString::number(m_screenOutput->currentData().toInt()));
    m_ctx->db->setSetting(QStringLiteral("screen_stage"), QString::number(m_screenStage->currentData().toInt()));
    m_ctx->db->setSetting(QStringLiteral("ws_port"), QString::number(m_wsPort->value()));
    m_ctx->db->setSetting(QStringLiteral("http_port"), QString::number(m_httpPort->value()));
    m_ctx->db->setSetting(QStringLiteral("fade_ms"), QString::number(m_fade->value()));
    m_ctx->db->setSetting(QStringLiteral("default_theme"), QString::number(m_defaultTheme->currentData().toInt()));
    m_ctx->db->setSetting(QStringLiteral("server_autostart"), m_autoStart->isChecked() ? QStringLiteral("1") : QStringLiteral("0"));
    m_ctx->db->setSetting(QStringLiteral("stage_chords"), m_stageWithChords->isChecked() ? QStringLiteral("1") : QStringLiteral("0"));
    // v1.1.0: ajustes de canciones + API
    m_ctx->db->setSetting(QStringLiteral("song_hinario"), m_hinarioMode->isChecked() ? QStringLiteral("1") : QStringLiteral("0"));
    m_ctx->db->setSetting(QStringLiteral("song_maxlines"), QString::number(m_maxLines->value()));
    m_ctx->db->setSetting(QStringLiteral("song_title_slide"), m_titleSlide->isChecked() ? QStringLiteral("1") : QStringLiteral("0"));
    m_ctx->db->setSetting(QStringLiteral("api_token"), m_apiToken->text().trimmed());
    // v1.3.0: atajos personalizados — texto PortableText ("Ctrl+Derecha")
    for (int i = 0; i < m_shortcutEdits.size(); ++i) {
        const QKeySequence ks = m_shortcutEdits.at(i)->keySequence();
        const QByteArray key = m_shortcutDefs.at(i).first;
        if (ks.isEmpty() || ks == QKeySequence(m_shortcutDefs.at(i).second)) {
            // vacío o igual al default => guardar vacío (usa default de fábrica)
            m_ctx->db->setSetting(QLatin1String(key.constData()), QString());
        } else {
            m_ctx->db->setSetting(QLatin1String(key.constData()), ks.toString());
        }
    }
    emit settingsApplied();
    QMessageBox::information(this, QStringLiteral("Ajustes"),
                             QStringLiteral("Configuración aplicada.\n"
                                            "Los atajos de teclado nuevos surten efecto al reiniciar "
                                            "la aplicación."));
}

void SettingsPanel::onOpenDataFolder()
{
    QDir d(m_ctx->db->setting(QStringLiteral("data_dir")));
    QDesktopServices::openUrl(QUrl::fromLocalFile(d.absolutePath()));
}

// ---------------------------------------------------------------------------
// v1.3.0 — Copia de seguridad / restauración
// ---------------------------------------------------------------------------
void SettingsPanel::onBackup()
{
    const QString dataDir = m_ctx->db->setting(QStringLiteral("data_dir"));
    const QString suggested = QStringLiteral("%1/lumina_backup_%2.db")
            .arg(QDir(dataDir).absolutePath(),
                 QDateTime::currentDateTime().toString(QStringLiteral("yyyyMMdd_Hhmm")));
    const QString out = QFileDialog::getSaveFileName(this, QStringLiteral("Crear copia de seguridad"),
                                                     suggested,
                                                     QStringLiteral("Copia LuminaPresentation (*.db)"));
    if (out.isEmpty()) return;
    QString err;
    if (!m_ctx->db->backupTo(out, &err)) {
        QMessageBox::warning(this, QStringLiteral("Copia de seguridad"),
                             QStringLiteral("No se pudo crear la copia:\n%1").arg(err));
        return;
    }
    m_backupStatus->setText(QStringLiteral("✅ Copia creada: %1").arg(QDir::toNativeSeparators(out)));
    QMessageBox::information(this, QStringLiteral("Copia de seguridad"),
                             QStringLiteral("Copia creada correctamente:\n%1").arg(QDir::toNativeSeparators(out)));
}

void SettingsPanel::onRestore()
{
    const QString dataDir = m_ctx->db->setting(QStringLiteral("data_dir"));
    const QString src = QFileDialog::getOpenFileName(this, QStringLiteral("Restaurar desde copia"),
                                                     QDir(dataDir).absolutePath(),
                                                     QStringLiteral("Copia LuminaPresentation (*.db);;Todos (*)"));
    if (src.isEmpty()) return;
    if (QMessageBox::question(this, QStringLiteral("Restaurar copia"),
                              QStringLiteral("Se reemplazará TODO el contenido actual (canciones, "
                                              "biblias importadas, cultos, temas, ajustes) por el de la copia:\n%1\n\n"
                                              "¿Continuar? Se recomienda reiniciar la aplicación después.")
                                  .arg(QDir::toNativeSeparators(src))) != QMessageBox::Yes)
        return;
    // Copia de seguridad de seguridad: snapshot del estado actual antes de restaurar
    const QString safety = QStringLiteral("%1/pre_restore_%2.db")
            .arg(QDir(dataDir + QStringLiteral("/backups")).absolutePath(),
                 QDateTime::currentDateTime().toString(QStringLiteral("yyyyMMdd_Hhmm")));
    QString ignored;
    m_ctx->db->backupTo(safety, &ignored);
    QString err;
    if (!m_ctx->db->restoreFrom(src, &err)) {
        QMessageBox::warning(this, QStringLiteral("Restaurar copia"),
                             QStringLiteral("No se pudo restaurar:\n%1").arg(err));
        return;
    }
    QMessageBox::information(this, QStringLiteral("Restaurar copia"),
                             QStringLiteral("Contenido restaurado.\nSe creó una copia de seguridad del "
                                            "estado anterior en:\n%1\n\n"
                                            "Reinicia la aplicación para que todos los paneles "
                                            "reflejen el contenido restaurado.")
                                 .arg(QDir::toNativeSeparators(safety)));
}

void SettingsPanel::onResetShortcuts()
{
    for (int i = 0; i < m_shortcutEdits.size(); ++i)
        m_shortcutEdits.at(i)->setKeySequence(QKeySequence(m_shortcutDefs.at(i).second));
}
