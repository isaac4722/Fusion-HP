// ============================================================================
//  LuminaPresentation Suite - SettingsPanel.h
//  Configuracion: pantallas de salida/escenario, transiciones, puertos del
//  servidor embebido, tema por defecto y estado del sistema.
// ============================================================================
#ifndef LUMINA_SETTINGSPANEL_H
#define LUMINA_SETTINGSPANEL_H

#include "core/AppContext.h"

#include <QWidget>
#include <QComboBox>
#include <QSpinBox>
#include <QSlider>
#include <QPushButton>
#include <QLabel>
#include <QCheckBox>
#include <QLineEdit>
#include <QKeySequenceEdit>
#include <QVector>
#include <QPair>

class SettingsPanel : public QWidget
{
    Q_OBJECT
public:
    explicit SettingsPanel(AppContext *ctx, QWidget *parent = nullptr);
    void refreshScreens();

signals:
    void settingsApplied();

private slots:
    void apply();
    void onOpenDataFolder();
    void onBackup();        // v1.3.0: crear copia de seguridad del vault
    void onRestore();       // v1.3.0: restaurar desde una copia
    void onResetShortcuts();  // v1.3.0: valores de fábrica de los atajos

private:
    void buildUi();
    AppContext *m_ctx;
    QComboBox *m_screenOutput = nullptr;
    QComboBox *m_screenStage = nullptr;
    QSpinBox *m_wsPort = nullptr;
    QSpinBox *m_httpPort = nullptr;
    QSlider *m_fade = nullptr;
    QComboBox *m_defaultTheme = nullptr;
    QCheckBox *m_autoStart = nullptr;
    QCheckBox *m_stageWithChords = nullptr;
    // v1.1.0: ajustes de canciones + API
    QCheckBox *m_hinarioMode = nullptr;
    QSpinBox *m_maxLines = nullptr;
    QCheckBox *m_titleSlide = nullptr;
    QLineEdit *m_apiToken = nullptr;
    QLabel *m_sysInfo = nullptr;
    // v1.3.0: atajos personalizables (spec Holyrics) — (settingKey, default)
    QVector<QPair<QByteArray, int>> m_shortcutDefs;
    QVector<QKeySequenceEdit *> m_shortcutEdits;
    QLabel *m_backupStatus = nullptr;
};

#endif // LUMINA_SETTINGSPANEL_H
