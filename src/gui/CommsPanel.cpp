// ============================================================================
//  LuminaPresentation Suite - CommsPanel.cpp
// ============================================================================
#include "CommsPanel.h"
#include "core/Database.h"
#include "core/Triggers.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGroupBox>
#include <QFormLayout>
#include <QMessageBox>

CommsPanel::CommsPanel(AppContext *ctx, QWidget *parent)
    : QWidget(parent), m_ctx(ctx)
{
    buildUi();
    loadConfig();
    // Bandeja Telegram
    if (m_ctx->triggers)
        connect(m_ctx->triggers->telegram(), &TelegramBot::messageReceived, this,
                [this](const QString &from, const QString &text) {
                    auto *it = new QListWidgetItem(QStringLiteral("✉ %1: %2").arg(from, text));
                    m_inbox->insertItem(0, it);
                    m_ctx->db->logAlert(QStringLiteral("Telegram [%1]: %2").arg(from, text));
                });
}

void CommsPanel::buildUi()
{
    auto *lay = new QHBoxLayout(this);
    lay->setContentsMargins(12, 12, 12, 12);

    // Izquierda: alertas + temporizador
    auto *left = new QVBoxLayout();
    auto *grpAlert = new QGroupBox(QStringLiteral("Alerta al Stage View (mensajes al escenario)"), this);
    auto *alay = new QVBoxLayout(grpAlert);
    m_alertText = new QPlainTextEdit(grpAlert);
    m_alertText->setPlaceholderText(QStringLiteral("Ej: «El vehículo blanco ABC-123 lo está esperando…»"));
    alay->addWidget(m_alertText);
    auto *bSend = new QPushButton(QStringLiteral("📣 Enviar alerta al escenario"), grpAlert);
    bSend->setStyleSheet(QStringLiteral("QPushButton{background:#D9821E;color:white;font-weight:bold;padding:8px 14px;}"));
    connect(bSend, &QPushButton::clicked, this, [this]() {
        const QString t = m_alertText->toPlainText().trimmed();
        if (!t.isEmpty()) emit sendAlert(t);
    });
    alay->addWidget(bSend);
    left->addWidget(grpAlert);

    auto *grpTimer = new QGroupBox(QStringLiteral("Temporizador regresivo (sermón/prédica)"), this);
    auto *tlay = new QHBoxLayout(grpTimer);
    auto *spin = new QSpinBox(grpTimer);
    spin->setRange(1, 180);
    spin->setValue(30);
    spin->setSuffix(QStringLiteral(" min"));
    auto *bStart = new QPushButton(QStringLiteral("Iniciar"), grpTimer);
    auto *bStop = new QPushButton(QStringLiteral("Detener"), grpTimer);
    connect(bStart, &QPushButton::clicked, this, [this, spin]() { emit startCountdown(spin->value()); });
    connect(bStop, &QPushButton::clicked, this, [this]() { emit stopCountdownSignal(); });
    tlay->addWidget(spin);
    tlay->addWidget(bStart);
    tlay->addWidget(bStop);
    tlay->addStretch();
    left->addWidget(grpTimer);

    auto *grpInbox = new QGroupBox(QStringLiteral("Peticiones recibidas (Telegram)"), this);
    auto *ilay = new QVBoxLayout(grpInbox);
    m_inbox = new QListWidget(grpInbox);
    ilay->addWidget(m_inbox);
    auto *bShow = new QPushButton(QStringLiteral("Mostrar como alerta en Stage View"), grpInbox);
    connect(bShow, &QPushButton::clicked, this, [this]() {
        auto *it = m_inbox->currentItem();
        if (it) emit sendAlert(it->text());
    });
    ilay->addWidget(bShow);
    left->addWidget(grpInbox, 1);
    lay->addLayout(left, 1);

    // Derecha: triggers
    auto *right = new QVBoxLayout();
    auto *grpTrig = new QGroupBox(QStringLiteral("Gatillos (Triggers) de automatización"), this);
    auto *tform = new QFormLayout(grpTrig);

    m_webhookOn = new QCheckBox(QStringLiteral("Webhook HTTP activo"), grpTrig);
    m_webhookUrl = new QLineEdit(grpTrig);
    m_webhookUrl->setPlaceholderText(QStringLiteral("http://127.0.0.1:8000/hook?event={event}&slide={slide}"));
    tform->addRow(m_webhookOn);
    tform->addRow(QStringLiteral("URL plantilla:"), m_webhookUrl);

    m_obsOn = new QCheckBox(QStringLiteral("OBS Studio (obs-websocket v5)"), grpTrig);
    m_obsHost = new QLineEdit(QStringLiteral("127.0.0.1"), grpTrig);
    m_obsPort = new QSpinBox(grpTrig);
    m_obsPort->setRange(1, 65535);
    m_obsPort->setValue(4455);
    m_obsPass = new QLineEdit(grpTrig);
    m_obsPass->setEchoMode(QLineEdit::Password);
    m_obsScene = new QLineEdit(QStringLiteral("Proyeccion"), grpTrig);
    tform->addRow(m_obsOn);
    tform->addRow(QStringLiteral("Host:"), m_obsHost);
    tform->addRow(QStringLiteral("Puerto:"), m_obsPort);
    tform->addRow(QStringLiteral("Contraseña:"), m_obsPass);
    tform->addRow(QStringLiteral("Escena al proyectar:"), m_obsScene);

    m_midiOn = new QCheckBox(QStringLiteral("MIDI Out (winmm) — Program Change al proyectar"), grpTrig);
    m_midiProgram = new QSpinBox(grpTrig);
    m_midiProgram->setRange(0, 127);
    tform->addRow(m_midiOn);
    tform->addRow(QStringLiteral("Programa MIDI:"), m_midiProgram);

    m_tgOn = new QCheckBox(QStringLiteral("Bot de Telegram"), grpTrig);
    m_tgToken = new QLineEdit(grpTrig);
    m_tgToken->setEchoMode(QLineEdit::Password);
    m_tgToken->setPlaceholderText(QStringLiteral("token del bot"));
    m_tgChat = new QLineEdit(grpTrig);
    m_tgChat->setPlaceholderText(QStringLiteral("chat id"));
    tform->addRow(m_tgOn);
    tform->addRow(QStringLiteral("Token:"), m_tgToken);
    tform->addRow(QStringLiteral("Chat ID:"), m_tgChat);

    right->addWidget(grpTrig);
    auto *bSave = new QPushButton(QStringLiteral("💾 Guardar y aplicar triggers"), this);
    auto *bTest = new QPushButton(QStringLiteral("🧪 Probar triggers"), this);
    connect(bSave, &QPushButton::clicked, this, &CommsPanel::saveConfig);
    connect(bTest, &QPushButton::clicked, this, &CommsPanel::onTestTriggers);
    right->addWidget(bSave);
    right->addWidget(bTest);
    right->addStretch();
    lay->addLayout(right, 1);
}

void CommsPanel::loadConfig()
{
    const QString j = m_ctx->db->setting(QStringLiteral("triggers"));
    if (j.isEmpty()) return;
    const QJsonObject o = QJsonDocument::fromJson(j.toUtf8()).object();
    m_webhookOn->setChecked(o.value(QStringLiteral("webhookOn")).toBool(false));
    m_webhookUrl->setText(o.value(QStringLiteral("webhookUrl")).toString());
    m_obsOn->setChecked(o.value(QStringLiteral("obsOn")).toBool(false));
    m_obsHost->setText(o.value(QStringLiteral("obsHost")).toString(QStringLiteral("127.0.0.1")));
    m_obsPort->setValue(o.value(QStringLiteral("obsPort")).toInt(4455));
    m_obsPass->setText(o.value(QStringLiteral("obsPass")).toString());
    m_obsScene->setText(o.value(QStringLiteral("obsScene")).toString(QStringLiteral("Proyeccion")));
    m_midiOn->setChecked(o.value(QStringLiteral("midiOn")).toBool(false));
    m_midiProgram->setValue(o.value(QStringLiteral("midiProgram")).toInt(0));
    m_tgOn->setChecked(o.value(QStringLiteral("tgOn")).toBool(false));
    m_tgToken->setText(o.value(QStringLiteral("tgToken")).toString());
    m_tgChat->setText(o.value(QStringLiteral("tgChat")).toString());
}

void CommsPanel::saveConfig()
{
    QJsonObject o;
    o["webhookOn"] = m_webhookOn->isChecked();
    o["webhookUrl"] = m_webhookUrl->text();
    o["obsOn"] = m_obsOn->isChecked();
    o["obsHost"] = m_obsHost->text();
    o["obsPort"] = m_obsPort->value();
    o["obsPass"] = m_obsPass->text();
    o["obsScene"] = m_obsScene->text();
    o["midiOn"] = m_midiOn->isChecked();
    o["midiProgram"] = m_midiProgram->value();
    o["tgOn"] = m_tgOn->isChecked();
    o["tgToken"] = m_tgToken->text();
    o["tgChat"] = m_tgChat->text();
    m_ctx->db->setSetting(QStringLiteral("triggers"),
                          QString::fromUtf8(QJsonDocument(o).toJson(QJsonDocument::Compact)));

    if (m_ctx->triggers) {
        Triggers::Config c;
        c.webhookEnabled = m_webhookOn->isChecked();
        c.webhookUrl = m_webhookUrl->text();
        c.obsEnabled = m_obsOn->isChecked();
        c.obsHost = m_obsHost->text();
        c.obsPort = quint16(m_obsPort->value());
        c.obsPassword = m_obsPass->text();
        c.obsSceneOnSlide = m_obsScene->text();
        c.midiEnabled = m_midiOn->isChecked();
        c.midiProgramOnSlide = m_midiProgram->value();
        c.telegramEnabled = m_tgOn->isChecked();
        c.telegramToken = m_tgToken->text();
        c.telegramChatId = m_tgChat->text();
        m_ctx->triggers->applyConfig(c);
    }
    QMessageBox::information(this, QStringLiteral("Triggers"),
                             QStringLiteral("Configuración guardada y aplicada."));
}

void CommsPanel::onTestTriggers()
{
    saveConfig();
    if (m_ctx->triggers) m_ctx->triggers->testAll();
}
