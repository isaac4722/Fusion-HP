// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MainWindow.h : Ventana principal de control. Orquesta los 6 modulos:
//  Database, DisplayEngine (salida audiencia + stage), MediaEngine (LibVLC),
//  WebServer (remoto/OBS), Triggers (automatizacion) y los paneles GUI.
// ============================================================================
#ifndef LUMINA_MAINWINDOW_H
#define LUMINA_MAINWINDOW_H

#include "core/AppContext.h"
#include "core/DisplayEngine.h"
#include "core/MediaEngine.h"
#include "core/Triggers.h"
#include "net/WebServer.h"
#include "StageWindow.h"

#include <QMainWindow>
#include <QListWidget>
#include <QStackedWidget>
#include <QLabel>
#include <QPushButton>
#include <QVector>

class SongPanel;
class BiblePanel;
class MediaPanel;
class PptxPanel;
class ThemePanel;
class CustomPanel;
class ServicePanel;
class CommsPanel;
class HistoryPanel;
class SettingsPanel;
class QComboBox;

class MainWindow : public QMainWindow
{
    Q_OBJECT
public:
    explicit MainWindow(AppContext *ctx, QWidget *parent = nullptr);
    ~MainWindow() override;

protected:
    void keyPressEvent(QKeyEvent *ev) override;
    void closeEvent(QCloseEvent *ev) override;

private slots:
    // Navegacion
    void onNavChanged(int row);
    // En vivo
    void goLive(const QVector<Slide> &slides, const QString &label, int refKind, int refId);
    void showSlideIndex(int idx, bool fireTriggers = true);
    void nextSlide();
    void prevSlide();
    void showBlack();
    void showClear();
    void showLogo();
    void exportLivePdf();          // v1.1.0: exportar escenario en vivo a PDF
    void runQueueAt(int row);      // v1.1.0: ejecutar item N de la cola del culto
    void quickVerse();
    void quickLowerThird();        // v1.1.0: superposición Lower Third (F10/diálogo)
    void closeOverlay();
    // Medios
    void onPlayMedia(const QString &path, bool asBackground, bool loop, int fitMode, bool isVideo);
    void onStopMedia();
    void onVolume(int vol);
    // Salidas
    void reassignOutputs();
    void rebuildScreenCombos();
    void toggleStageView();
    void toggleOutput();
    // Servidor remoto
    void onRemoteCommand(const QString &cmd, const QJsonObject &data);
    void pushWebState();
    // Comunicacion
    void onSendAlert(const QString &text);
    void onStartCountdown(int minutes);
    void onStopCountdown();
    // Culto
    void runServiceItem(const ServiceItem &item);

private:
    void buildUi();
    void buildShortcuts();
    void applyTheme(int themeId);
    void goLiveSong(const Song &s, int refKind, int refId);   // v1.1.0: proyección de canción con ajustes
    static QString s_titleOf(int book, int ch);
    void updatePreview();
    void updateSlideList();
    void updateStage();
    void playThemeBackgroundVideo(const Theme &theme);
    void stopBackgroundVideo();

    // Contexto
    AppContext m_ctx;
    Theme m_theme;

    // En vivo
    QVector<Slide> m_liveSlides;
    int m_liveIndex = -1;
    QString m_liveLabel;
    int m_liveRefKind = -1;
    int m_liveRefId = 0;

    // Overlay de versiculo rapido (F9)
    bool m_overlayActive = false;
    QVector<Slide> m_savedSlides;
    int m_savedIndex = -1;
    QString m_savedLabel;
    QString m_lastAlert;
    QDateTime m_lastAlertAt;
    int m_stageTranspose = 0;      // v1.1.0: transposicion en vivo del Stage View

    // Ventanas de salida
    OutputWindow *m_output = nullptr;
    StageWindow *m_stage = nullptr;
    int m_outputScreen = -1;
    int m_stageScreen = -1;
    bool m_stageOn = false;
    QPixmap m_logoPixmap;

    // GUI
    QListWidget *m_nav = nullptr;
    QStackedWidget *m_stack = nullptr;
    QLabel *m_preview = nullptr;
    QListWidget *m_slideList = nullptr;
    QListWidget *m_queueList = nullptr;
    QVector<ServiceItem> m_queueData;
    QLabel *m_liveInfo = nullptr;
    QComboBox *m_comboOutputScreen = nullptr;
    QComboBox *m_comboStageScreen = nullptr;

    // Paneles
    SongPanel *m_songPanel = nullptr;
    BiblePanel *m_biblePanel = nullptr;
    MediaPanel *m_mediaPanel = nullptr;
    PptxPanel *m_pptxPanel = nullptr;
    ThemePanel *m_themePanel = nullptr;
    CustomPanel *m_customPanel = nullptr;
    ServicePanel *m_servicePanel = nullptr;
    CommsPanel *m_commsPanel = nullptr;
    HistoryPanel *m_historyPanel = nullptr;
    SettingsPanel *m_settingsPanel = nullptr;
};

#endif // LUMINA_MAINWINDOW_H
