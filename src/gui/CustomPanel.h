// ============================================================================
//  LuminaPresentation Suite - CustomPanel.h
//  Lienzo vectorial libre (estilo PowerPoint): slides personalizadas con
//  textos, formas e imagenes; guardado en DB como JSON y proyeccion.
// ============================================================================
#ifndef LUMINA_CUSTOMPANEL_H
#define LUMINA_CUSTOMPANEL_H

#include "core/AppContext.h"
#include "VectorCanvas.h"

#include <QWidget>
#include <QListWidget>
#include <QLineEdit>
#include <QPushButton>

class CustomPanel : public QWidget
{
    Q_OBJECT
public:
    explicit CustomPanel(AppContext *ctx, QWidget *parent = nullptr);

signals:
    void requestProjectCustom(const Slide &slide);

private slots:
    void onNewSlide();
    void onSaveSlide();
    void onLoadSlide();
    void onDeleteSlide();
    void onAddText();
    void onAddImage();
    void onProject();

private:
    void buildUi();
    Slide currentSlide();

    AppContext *m_ctx;
    VectorCanvas *m_canvas = nullptr;
    QListWidget *m_list = nullptr;
    QLineEdit *m_name = nullptr;
    int m_currentId = 0;
};

#endif // LUMINA_CUSTOMPANEL_H
