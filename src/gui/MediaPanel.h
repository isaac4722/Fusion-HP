// ============================================================================
//  LuminaPresentation Suite - MediaPanel.h
//  Reproduccion multimedia LibVLC: video a pantalla principal o de fondo
//  en bucle, control de volumen, modos de encuadre (Llenar/Ajustar/Centrar)
//  para videos verticales, sincronizacion BPM.
// ============================================================================
#ifndef LUMINA_MEDIAPANEL_H
#define LUMINA_MEDIAPANEL_H

#include "core/AppContext.h"

#include <QWidget>
#include <QPushButton>
#include <QSlider>
#include <QComboBox>
#include <QCheckBox>
#include <QLabel>
#include <QLineEdit>
#include <QSpinBox>

class MediaPanel : public QWidget
{
    Q_OBJECT
public:
    explicit MediaPanel(AppContext *ctx, QWidget *parent = nullptr);

signals:
    // MainWindow coordina la salida y el MediaEngine
    void playMedia(const QString &path, bool asBackground, bool loop, int fitMode, bool isVideo);
    void stopMedia();
    void volumeChanged(int vol);

public slots:
    // v1.1.0: ahora publicas — MainWindow las conecta a MediaEngine
    // (antes eran privadas y NUNCA se conectaban: barra/tiempo muertos)
    void onPosition(qint64 t, qint64 len);
    void onMediaState(int st);

private slots:
    void onOpen();
    void onPlay();

private:
    void buildUi();
    void updateVlcStatus();

    AppContext *m_ctx;
    QLineEdit *m_path = nullptr;
    QPushButton *m_bPlay = nullptr;
    QPushButton *m_bStop = nullptr;
    QSlider *m_volume = nullptr;
    QSpinBox *m_startPos = nullptr;      // v1.3.0: posición inicial (spec Holyrics)
    QSlider *m_seek = nullptr;
    QCheckBox *m_loop = nullptr;
    QCheckBox *m_background = nullptr;
    QComboBox *m_fit = nullptr;
    QLabel *m_status = nullptr;
    QLabel *m_time = nullptr;
    bool m_isVideo = true;
    bool m_seeking = false;
};

#endif // LUMINA_MEDIAPANEL_H
