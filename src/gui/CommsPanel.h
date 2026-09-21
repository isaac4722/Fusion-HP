// ============================================================================
//  LuminaPresentation Suite - CommsPanel.h
//  Panel de comunicacion: alertas al Stage View, temporizador regresivo,
//  bandeja de peticiones (Telegram), configuracion de triggers
//  (Webhook / OBS WebSocket v5 / MIDI Out) y — v1.4.0 — automatizacion
//  semantica por etiquetas (reglas cancion->tema/fondo) y MIDI In
//  (eventos de disparo externos del spec Holyrics).
// ============================================================================
#ifndef LUMINA_COMMSPANEL_H
#define LUMINA_COMMSPANEL_H

#include "core/AppContext.h"

#include <QWidget>
#include <QPlainTextEdit>
#include <QListWidget>
#include <QSpinBox>
#include <QLineEdit>
#include <QCheckBox>
#include <QPushButton>
#include <QTableWidget>
#include <QComboBox>
#include <QLabel>

class CommsPanel : public QWidget
{
    Q_OBJECT
public:
    explicit CommsPanel(AppContext *ctx, QWidget *parent = nullptr);

signals:
    void sendAlert(const QString &text);
    void startCountdown(int minutes);
    void stopCountdownSignal();

private slots:
    void loadConfig();
    void saveConfig();
    void onTestTriggers();
    // v1.4.0
    void onAddRule();            // crea la regla de la fila de edición
    void onDeleteRule();         // elimina la regla seleccionada
    void onRuleToggled(int row, int col);   // activa/desactiva una regla
    void onAddMidiMap();         // añade fila nota->comando
    void onDeleteMidiMap();      // elimina fila seleccionada
    void onTestMidiNote();       // inyecta la nota de la fila seleccionada

private:
    void buildUi();
    void refreshRules();         // v1.4.0: recarga la tabla de reglas
    void refreshMidiStatus();    // v1.4.0: chip de estado MIDI In
    void loadMidiMapFromJson(const QJsonArray &arr);   // v1.4.0
    void addMidiRow(int note, const QString &cmd);     // v1.4.0

    AppContext *m_ctx;
    QPlainTextEdit *m_alertText = nullptr;
    QLineEdit *m_remoteTitle = nullptr;      // v1.3.0: mensaje a remotos
    QPlainTextEdit *m_remoteText = nullptr;   // v1.3.0
    QListWidget *m_inbox = nullptr;
    // Triggers
    QCheckBox *m_webhookOn = nullptr;
    QLineEdit *m_webhookUrl = nullptr;
    QCheckBox *m_obsOn = nullptr;
    QLineEdit *m_obsHost = nullptr;
    QSpinBox *m_obsPort = nullptr;
    QLineEdit *m_obsPass = nullptr;
    QLineEdit *m_obsScene = nullptr;
    QCheckBox *m_midiOn = nullptr;
    QSpinBox *m_midiProgram = nullptr;
    QCheckBox *m_tgOn = nullptr;
    QLineEdit *m_tgToken = nullptr;
    QLineEdit *m_tgChat = nullptr;
    // v1.4.0 — automatizacion semantica
    QCheckBox *m_semOn = nullptr;
    QTableWidget *m_rules = nullptr;
    QLineEdit *m_ruleTag = nullptr;
    QComboBox *m_ruleTheme = nullptr;
    QLineEdit *m_ruleBgTag = nullptr;
    // v1.4.0 — MIDI In
    QCheckBox *m_midiInOn = nullptr;
    QTableWidget *m_midiMap = nullptr;
    QLabel *m_midiStatus = nullptr;
};

#endif // LUMINA_COMMSPANEL_H
