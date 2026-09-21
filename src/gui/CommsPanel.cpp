// ============================================================================
//  LuminaPresentation Suite - CommsPanel.cpp
// ============================================================================
#include "CommsPanel.h"
#include "core/Database.h"
#include "core/Triggers.h"
#include "core/MidiIn.h"
#include "net/WebServer.h"   // v1.3.0: broadcastMessage a los remotos

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGroupBox>
#include <QFormLayout>
#include <QMessageBox>
#include <QScrollArea>
#include <QHeaderView>
#include <QFileInfo>
#include <QStyle>

CommsPanel::CommsPanel(AppContext *ctx, QWidget *parent)
    : QWidget(parent), m_ctx(ctx)
{
    buildUi();
    loadConfig();
    refreshRules();
    refreshMidiStatus();
    // Bandeja Telegram
    if (m_ctx->triggers)
        connect(m_ctx->triggers->telegram(), &TelegramBot::messageReceived, this,
                [this](const QString &from, const QString &text) {
                    auto *it = new QListWidgetItem(QStringLiteral("✉ %1: %2").arg(from, text));
                    // v1.2.0: se guarda el TEXTO LIMPIO en UserRole — antes
                    // "Mostrar como alerta" enviaba al escenario la etiqueta
                    // completa ("✉ Juan: …" con emoji y prefijo) tal cual.
                    it->setData(Qt::UserRole, text);
                    m_inbox->insertItem(0, it);
                    m_ctx->db->logAlert(QStringLiteral("Telegram [%1]: %2").arg(from, text));
                });
}

void CommsPanel::buildUi()
{
    // v1.4.0 (hallmark: accesibilidad/responsivo): el panel creció con las
    // nuevas secciones — se envuelve en QScrollArea para que en pantallas
    // pequeñas (netbooks Win7 de 1366x768) nada quede inalcanzable.
    auto *scroll = new QScrollArea(this);
    scroll->setWidgetResizable(true);
    scroll->setFrameShape(QFrame::NoFrame);
    auto *host = new QWidget(scroll);
    auto *lay = new QHBoxLayout(host);
    lay->setContentsMargins(12, 12, 12, 12);
    scroll->setWidget(host);
    auto *outer = new QVBoxLayout(this);
    outer->setContentsMargins(0, 0, 0, 0);
    outer->addWidget(scroll);

    // Izquierda: alertas + temporizador
    auto *left = new QVBoxLayout();
    auto *grpAlert = new QGroupBox(QStringLiteral("Alerta al Stage View (mensajes al escenario)"), this);
    auto *alay = new QVBoxLayout(grpAlert);
    m_alertText = new QPlainTextEdit(grpAlert);
    m_alertText->setPlaceholderText(QStringLiteral("Ej: «El vehículo blanco ABC-123 lo está esperando…»"));
    alay->addWidget(m_alertText);
    auto *bSend = new QPushButton(QStringLiteral("📣 Enviar alerta al escenario"), grpAlert);
    bSend->setProperty("class", QStringLiteral("gold"));   // v1.3.0 Aurora
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

    // v1.3.0 — "Custom Messages" del spec Holyrics: avisos del operador a
    // todos los dispositivos remotos conectados (toast en remote.html; no
    // interrumpe la proyección ni el Stage View).
    auto *grpRemote = new QGroupBox(QStringLiteral("Mensaje a los remotos conectados (móviles/tablets)"), this);
    auto *rlay = new QVBoxLayout(grpRemote);
    m_remoteTitle = new QLineEdit(grpRemote);
    m_remoteTitle->setPlaceholderText(QStringLiteral("Título opcional (p.ej. «Aviso del operador»)"));
    m_remoteText = new QPlainTextEdit(grpRemote);
    m_remoteText->setPlaceholderText(QStringLiteral("Ej: «Entramos en 5 minutos — revisen micrófonos»"));
    m_remoteText->setMaximumHeight(90);
    rlay->addWidget(m_remoteTitle);
    rlay->addWidget(m_remoteText);
    auto *bRemote = new QPushButton(QStringLiteral("📡 Enviar a todos los remotos"), grpRemote);
    bRemote->setProperty("class", QStringLiteral("primary"));
    connect(bRemote, &QPushButton::clicked, this, [this]() {
        const QString t = m_remoteText->toPlainText().trimmed();
        if (t.isEmpty() || !m_ctx->web) return;
        m_ctx->web->broadcastMessage(t, m_remoteTitle->text().trimmed());
        m_remoteText->clear();
    });
    rlay->addWidget(bRemote);
    left->addWidget(grpRemote);

    auto *grpInbox = new QGroupBox(QStringLiteral("Peticiones recibidas (Telegram)"), this);
    auto *ilay = new QVBoxLayout(grpInbox);
    m_inbox = new QListWidget(grpInbox);
    ilay->addWidget(m_inbox);
    auto *bShow = new QPushButton(QStringLiteral("Mostrar como alerta en Stage View"), grpInbox);
    connect(bShow, &QPushButton::clicked, this, [this]() {
        auto *it = m_inbox->currentItem();
        // v1.2.0: usa el texto limpio guardado en UserRole (sin el prefijo
        // "✉ autor:" ni el emoji).
        if (it) {
            const QString clean = it->data(Qt::UserRole).toString();
            emit sendAlert(clean.isEmpty() ? it->text() : clean);
        }
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

    // v1.4.0 — Automatización semántica por etiquetas (spec Holyrics: "si se
    // reproduce una canción con la etiqueta 'lento', aplicar el tema 'calma'
    // y seleccionar un fondo con la etiqueta 'ocaso'").
    auto *grpSem = new QGroupBox(QStringLiteral("Automatización semántica (etiquetas)"), host);
    auto *slay = new QVBoxLayout(grpSem);
    m_semOn = new QCheckBox(QStringLiteral("Aplicar reglas automáticamente al proyectar"), grpSem);
    m_semOn->setToolTip(QStringLiteral("Al proyectar una canción, si alguna regla coincide con una de sus etiquetas, el tema (y opcionalmente el fondo) se aplican en vivo sin tocar la plantilla."));
    slay->addWidget(m_semOn);
    m_rules = new QTableWidget(grpSem);
    m_rules->setColumnCount(4);
    m_rules->setHorizontalHeaderLabels({ QStringLiteral("Etiqueta de la canción"),
                                         QStringLiteral("Tema"),
                                         QStringLiteral("Fondo (etiqueta)"),
                                         QStringLiteral("Activa") });
    m_rules->horizontalHeader()->setSectionResizeMode(0, QHeaderView::Stretch);
    m_rules->horizontalHeader()->setSectionResizeMode(1, QHeaderView::Stretch);
    m_rules->horizontalHeader()->setSectionResizeMode(2, QHeaderView::Stretch);
    m_rules->horizontalHeader()->setSectionResizeMode(3, QHeaderView::ResizeToContents);
    m_rules->verticalHeader()->setVisible(false);
    m_rules->setEditTriggers(QAbstractItemView::NoEditTriggers);
    m_rules->setMaximumHeight(140);
    m_rules->setSelectionBehavior(QAbstractItemView::SelectRows);
    connect(m_rules, &QTableWidget::cellClicked, this, &CommsPanel::onRuleToggled);
    slay->addWidget(m_rules);
    auto *ruleForm = new QFormLayout();
    m_ruleTag = new QLineEdit(grpSem);
    m_ruleTag->setPlaceholderText(QStringLiteral("lento"));
    m_ruleTheme = new QComboBox(grpSem);
    const auto themes = m_ctx->db->themes();
    for (const auto &t : themes) m_ruleTheme->addItem(t.second, t.first);
    m_ruleBgTag = new QLineEdit(grpSem);
    m_ruleBgTag->setPlaceholderText(QStringLiteral("ocaso (vacío = solo tema)"));
    ruleForm->addRow(QStringLiteral("Etiqueta:"), m_ruleTag);
    ruleForm->addRow(QStringLiteral("Tema:"), m_ruleTheme);
    ruleForm->addRow(QStringLiteral("Fondo:"), m_ruleBgTag);
    slay->addLayout(ruleForm);
    auto *semRow = new QHBoxLayout();
    auto *bAddRule = new QPushButton(QStringLiteral("＋ Añadir regla"), grpSem);
    bAddRule->setProperty("class", QStringLiteral("primary"));
    auto *bDelRule = new QPushButton(QStringLiteral("Eliminar regla"), grpSem);
    connect(bAddRule, &QPushButton::clicked, this, &CommsPanel::onAddRule);
    connect(bDelRule, &QPushButton::clicked, this, &CommsPanel::onDeleteRule);
    semRow->addWidget(bAddRule);
    semRow->addWidget(bDelRule);
    semRow->addStretch();
    slay->addLayout(semRow);
    right->addWidget(grpSem);

    // v1.4.0 — MIDI In (spec Holyrics: eventos de disparo externos: "recibir
    // un comando MIDI"). Un pedal/pad MIDI dispara los comandos del
    // presentador con el mismo vocabulario del control remoto web.
    auto *grpMidiIn = new QGroupBox(QStringLiteral("MIDI In (hardware → comandos)"), host);
    auto *milay = new QVBoxLayout(grpMidiIn);
    m_midiInOn = new QCheckBox(QStringLiteral("Escuchar el primer dispositivo MIDI IN"), grpMidiIn);
    milay->addWidget(m_midiInOn);
    m_midiStatus = new QLabel(grpMidiIn);
    m_midiStatus->setProperty("class", QStringLiteral("chip"));
    milay->addWidget(m_midiStatus);
    m_midiMap = new QTableWidget(grpMidiIn);
    m_midiMap->setColumnCount(2);
    m_midiMap->setHorizontalHeaderLabels({ QStringLiteral("Nota MIDI (0-127)"),
                                           QStringLiteral("Comando") });
    m_midiMap->horizontalHeader()->setSectionResizeMode(1, QHeaderView::Stretch);
    m_midiMap->verticalHeader()->setVisible(false);
    m_midiMap->setMaximumHeight(120);
    m_midiMap->setSelectionBehavior(QAbstractItemView::SelectRows);
    milay->addWidget(m_midiMap);
    auto *midiRow = new QHBoxLayout();
    auto *bAddMap = new QPushButton(QStringLiteral("＋ Fila"), grpMidiIn);
    auto *bDelMap = new QPushButton(QStringLiteral("Quitar"), grpMidiIn);
    auto *bTestMap = new QPushButton(QStringLiteral("Probar nota"), grpMidiIn);
    connect(bAddMap, &QPushButton::clicked, this, &CommsPanel::onAddMidiMap);
    connect(bDelMap, &QPushButton::clicked, this, &CommsPanel::onDeleteMidiMap);
    connect(bTestMap, &QPushButton::clicked, this, &CommsPanel::onTestMidiNote);
    midiRow->addWidget(bAddMap);
    midiRow->addWidget(bDelMap);
    midiRow->addWidget(bTestMap);
    midiRow->addStretch();
    milay->addLayout(midiRow);
    right->addWidget(grpMidiIn);

    auto *bSave = new QPushButton(QStringLiteral("💾 Guardar y aplicar triggers"), host);
    auto *bTest = new QPushButton(QStringLiteral("🧪 Probar triggers"), host);
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
    if (!j.isEmpty()) {
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
        // v1.4.0 — MIDI In
        m_midiInOn->setChecked(o.value(QStringLiteral("midiInOn")).toBool(false));
        loadMidiMapFromJson(o.value(QStringLiteral("midiInMap")).toArray());
    } else {
        loadMidiMapFromJson(QJsonArray());      // defaults C mayor (una octava)
    }
    // v1.4.0 — interruptor de la automatización semántica
    m_semOn->setChecked(m_ctx->db->setting(QStringLiteral("semantics_on"),
                                           QStringLiteral("0")) == QStringLiteral("1"));
}

void CommsPanel::loadMidiMapFromJson(const QJsonArray &arr)
{
    m_midiMap->setRowCount(0);
    // Defaults didácticos (octava de Do: pedal de una fila entera manda
    // toda la proyección). Solo se usan cuando no hay mapa guardado.
    static const QPair<int, QString> kDefaults[] = {
        { 60, QStringLiteral("next") },  { 62, QStringLiteral("prev") },
        { 64, QStringLiteral("black") }, { 65, QStringLiteral("clear") },
        { 67, QStringLiteral("logo") },  { 69, QStringLiteral("qnext") },
        { 71, QStringLiteral("qprev") }
    };
    if (arr.isEmpty()) {
        for (const auto &d : kDefaults) addMidiRow(d.first, d.second);
        return;
    }
    for (const QJsonValue &v : arr) {
        const QJsonObject o = v.toObject();
        addMidiRow(o.value(QStringLiteral("note")).toInt(-1),
                   o.value(QStringLiteral("cmd")).toString());
    }
}

void CommsPanel::addMidiRow(int note, const QString &cmd)
{
    const int row = m_midiMap->rowCount();
    m_midiMap->insertRow(row);
    auto *spin = new QSpinBox(m_midiMap);
    spin->setRange(0, 127);
    spin->setValue(qBound(0, note, 127));
    m_midiMap->setCellWidget(row, 0, spin);
    auto *combo = new QComboBox(m_midiMap);
    combo->addItems({ QStringLiteral("next"), QStringLiteral("prev"),
                      QStringLiteral("black"), QStringLiteral("clear"),
                      QStringLiteral("logo"), QStringLiteral("qnext"),
                      QStringLiteral("qprev") });
    combo->setCurrentText(cmd.isEmpty() ? QStringLiteral("next") : cmd);
    m_midiMap->setCellWidget(row, 1, combo);
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
    // v1.4.0 — MIDI In (mapa nota->comando)
    o["midiInOn"] = m_midiInOn->isChecked();
    QJsonArray map;
    for (int r = 0; r < m_midiMap->rowCount(); ++r) {
        auto *spin = qobject_cast<QSpinBox *>(m_midiMap->cellWidget(r, 0));
        auto *combo = qobject_cast<QComboBox *>(m_midiMap->cellWidget(r, 1));
        if (!spin || !combo) continue;
        QJsonObject e;
        e["note"] = spin->value();
        e["cmd"] = combo->currentText();
        map.append(e);
    }
    o["midiInMap"] = map;
    m_ctx->db->setSetting(QStringLiteral("triggers"),
                          QString::fromUtf8(QJsonDocument(o).toJson(QJsonDocument::Compact)));
    // v1.4.0 — interruptor semántico
    m_ctx->db->setSetting(QStringLiteral("semantics_on"),
                          m_semOn->isChecked() ? QStringLiteral("1") : QStringLiteral("0"));

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
        c.midiInEnabled = m_midiInOn->isChecked();
        for (int r = 0; r < m_midiMap->rowCount(); ++r) {
            auto *spin = qobject_cast<QSpinBox *>(m_midiMap->cellWidget(r, 0));
            auto *combo = qobject_cast<QComboBox *>(m_midiMap->cellWidget(r, 1));
            if (!spin || !combo) continue;
            c.midiInMap.append(qMakePair(spin->value(), combo->currentText()));
        }
        c.telegramEnabled = m_tgOn->isChecked();
        c.telegramToken = m_tgToken->text();
        c.telegramChatId = m_tgChat->text();
        m_ctx->triggers->applyConfig(c);
    }
    refreshMidiStatus();
    QMessageBox::information(this, QStringLiteral("Triggers"),
                             QStringLiteral("Configuración guardada y aplicada."));
}

void CommsPanel::onTestTriggers()
{
    saveConfig();
    if (m_ctx->triggers) m_ctx->triggers->testAll();
}

// ---------------------------------------------------------------------------
// v1.4.0 — Reglas de automatización semántica
// ---------------------------------------------------------------------------
void CommsPanel::refreshRules()
{
    if (!m_rules) return;
    m_rules->setRowCount(0);
    const auto rules = m_ctx->db->tagRules();
    // Mapa id->nombre de tema para la columna "Tema"
    QMap<int, QString> themeNames;
    const auto themes = m_ctx->db->themes();
    for (const auto &t : themes) themeNames.insert(t.first, t.second);

    for (const auto &r : rules) {
        const int row = m_rules->rowCount();
        m_rules->insertRow(row);
        auto *itTag = new QTableWidgetItem(r.songTag);
        itTag->setData(Qt::UserRole, r.id);
        m_rules->setItem(row, 0, itTag);
        m_rules->setItem(row, 1, new QTableWidgetItem(themeNames.value(r.themeId,
                              QStringLiteral("#%1").arg(r.themeId))));
        m_rules->setItem(row, 2, new QTableWidgetItem(r.bgTag.isEmpty()
                              ? QStringLiteral("—") : r.bgTag));
        auto *itOn = new QTableWidgetItem(r.enabled ? QStringLiteral("✔") : QStringLiteral("✕"));
        itOn->setTextAlignment(Qt::AlignCenter);
        m_rules->setItem(row, 3, itOn);
    }
}

void CommsPanel::onAddRule()
{
    const QString tag = m_ruleTag->text().trimmed();
    const int themeId = m_ruleTheme->currentData().toInt();
    if (tag.isEmpty() || themeId <= 0) {
        QMessageBox::information(this, QStringLiteral("Reglas"),
                                 QStringLiteral("Indica la etiqueta de la canción y el tema a aplicar."));
        return;
    }
    if (m_ctx->db->addTagRule(tag, themeId, m_ruleBgTag->text().trimmed()) > 0) {
        m_ruleTag->clear();
        m_ruleBgTag->clear();
        refreshRules();
    }
}

void CommsPanel::onDeleteRule()
{
    const int row = m_rules->currentRow();
    if (row < 0) return;
    const int id = m_rules->item(row, 0)->data(Qt::UserRole).toInt();
    if (m_ctx->db->deleteTagRule(id)) refreshRules();
}

void CommsPanel::onRuleToggled(int row, int col)
{
    // Clic en la columna "Activa" (o sobre cualquier fila con la tecla de
    // alternado) invierte el estado de la regla — edición rápida sin diálogo.
    Q_UNUSED(col)
    if (row < 0 || !m_rules->item(row, 0)) return;
    const int id = m_rules->item(row, 0)->data(Qt::UserRole).toInt();
    const bool currently = m_rules->item(row, 3)->text() == QStringLiteral("✔");
    if (m_ctx->db->setTagRuleEnabled(id, !currently)) refreshRules();
}

// ---------------------------------------------------------------------------
// v1.4.0 — MIDI In: mapa nota->comando
// ---------------------------------------------------------------------------
void CommsPanel::onAddMidiMap()
{
    addMidiRow(60, QStringLiteral("next"));
}

void CommsPanel::onDeleteMidiMap()
{
    const int row = m_midiMap->currentRow();
    if (row >= 0) m_midiMap->removeRow(row);
}

void CommsPanel::onTestMidiNote()
{
    if (!m_ctx->triggers || !m_ctx->triggers->midiIn()) return;
    const int row = m_midiMap->currentRow();
    if (row < 0) {
        QMessageBox::information(this, QStringLiteral("MIDI In"),
                                 QStringLiteral("Selecciona una fila del mapa para probar su nota."));
        return;
    }
    auto *spin = qobject_cast<QSpinBox *>(m_midiMap->cellWidget(row, 0));
    if (spin) m_ctx->triggers->midiIn()->injectNote(spin->value(), 100);
}

void CommsPanel::refreshMidiStatus()
{
    if (!m_midiStatus) return;
    const int devs = MidiIn::deviceCount();
    const bool active = m_ctx->triggers && m_ctx->triggers->midiIn() && m_ctx->triggers->midiIn()->valid();
    QString text;
    const char *state = nullptr;
    if (!m_midiInOn->isChecked()) {
        text = QStringLiteral("MIDI In desactivado");
        state = "off";
    } else if (devs == 0) {
        text = QStringLiteral("⚠ Sin dispositivos MIDI visibles en este equipo");
        state = "warn";
    } else if (active) {
        text = QStringLiteral("● Escuchando dispositivo 1 de %1").arg(devs);
        state = "ok";
    } else {
        text = QStringLiteral("⚠ No se pudo abrir el dispositivo (%1 detectados)").arg(devs);
        state = "warn";
    }
    m_midiStatus->setText(text);
    m_midiStatus->setProperty("state", QString::fromLatin1(state));
    m_midiStatus->style()->unpolish(m_midiStatus);
    m_midiStatus->style()->polish(m_midiStatus);
}
