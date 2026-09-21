// ============================================================================
//  LuminaPresentation Suite - ThemePanel.h
//  Editor de Plantillas Maestras (Master Slides): fondo (color/gradiente/
//  imagen/video), tipografia, contorno, sombra, cajas normalizadas y
//  aplicacion dinamica a todo el repertorio.
// ============================================================================
#ifndef LUMINA_THEMEPANEL_H
#define LUMINA_THEMEPANEL_H

#include "core/AppContext.h"

#include <QWidget>
#include <QListWidget>
#include <QLineEdit>
#include <QPushButton>
#include <QComboBox>
#include <QSpinBox>
#include <QCheckBox>
#include <QLabel>
#include <QColor>
#include <QSlider>

class ThemePanel : public QWidget
{
    Q_OBJECT
public:
    explicit ThemePanel(AppContext *ctx, QWidget *parent = nullptr);
    void loadFromTheme(const Theme &t);

signals:
    void themeChanged(const Theme &t);      // aplicado en vivo

private slots:
    void onThemeSelected(int row);
    void onFieldChanged();
    void onPickColor1();
    void onPickColor2();
    void onPickImage();
    void onPickVideo();
    void onSaveAsNew();
    void onSave();
    void onDelete();

private:
    void buildUi();
    Theme collectTheme() const;

    AppContext *m_ctx;
    QListWidget *m_list = nullptr;
    QLineEdit *m_name = nullptr;
    QComboBox *m_bgType = nullptr;
    QComboBox *m_fontBody = nullptr;
    QComboBox *m_fontTitle = nullptr;
    QSpinBox *m_sizeBody = nullptr;
    QSpinBox *m_sizeTitle = nullptr;
    QPushButton *m_bColor1 = nullptr;
    QPushButton *m_bColor2 = nullptr;
    QPushButton *m_bTextColor = nullptr;
    QLineEdit *m_imagePath = nullptr;
    QLineEdit *m_videoPath = nullptr;
    QCheckBox *m_shadow = nullptr;
    QCheckBox *m_outline = nullptr;
    QSlider *m_boxTop = nullptr;
    QSlider *m_boxHeight = nullptr;
    QLabel *m_preview = nullptr;
    int m_selectedThemeId = 0;
    QColor m_color1 = QColor(8, 16, 40);
    QColor m_color2 = QColor(24, 60, 130);
    QColor m_textColor = Qt::white;
};

#endif // LUMINA_THEMEPANEL_H
