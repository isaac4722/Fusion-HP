// ============================================================================
//  LuminaPresentation Suite - CommsPanel.h
//  Panel de comunicacion: alertas al Stage View, temporizador regresivo,
//  bandeja de peticiones (Telegram), configuracion de triggers
//  (Webhook / OBS WebSocket v5 / MIDI Out).
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

private:
    void buildUi();

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
};

#endif // LUMINA_COMMSPANEL_H
