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
#include <QDesktopServices>
#include <QUrl>
#include <QGuiApplication>
#include <QScreen>
#include <QSysInfo>

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

    auto *row = new QHBoxLayout();
    auto *bApply = new QPushButton(QStringLiteral("💾 Aplicar configuración"), this);
    bApply->setStyleSheet(QStringLiteral("QPushButton{background:#1E6FD9;color:white;font-weight:bold;padding:8px 16px;}"));
    auto *bFolder = new QPushButton(QStringLiteral("Abrir carpeta de datos"), this);
    connect(bApply, &QPushButton::clicked, this, &SettingsPanel::apply);
    connect(bFolder, &QPushButton::clicked, this, &SettingsPanel::onOpenDataFolder);
    row->addWidget(bApply);
    row->addWidget(bFolder);
    row->addStretch();
    lay->addLayout(row);

    m_sysInfo = new QLabel(QString(), this);
    m_sysInfo->setWordWrap(true);
    m_sysInfo->setStyleSheet(QStringLiteral("color:#8FA3C8;"));
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
    if (screens.size() > 1) {
        m_screenOutput->setCurrentIndex(1);     // segunda pantalla para audiencia
        m_screenStage->setCurrentIndex(0);
    }
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
    emit settingsApplied();
    QMessageBox::information(this, QStringLiteral("Ajustes"), QStringLiteral("Configuración aplicada."));
}

void SettingsPanel::onOpenDataFolder()
{
    QDir d(m_ctx->db->setting(QStringLiteral("data_dir")));
    QDesktopServices::openUrl(QUrl::fromLocalFile(d.absolutePath()));
}
