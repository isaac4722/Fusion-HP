// ============================================================================
//  LuminaPresentation Suite - PptxPanel.h
//  Importacion de presentaciones .pptx (OpenXML) sin Office: vista previa
//  de slides, proyeccion directa, envio al culto y exportacion minima.
// ============================================================================
#ifndef LUMINA_PPTXPANEL_H
#define LUMINA_PPTXPANEL_H

#include "core/AppContext.h"
#include "core/PptxEngine.h"

#include <QWidget>
#include <QListWidget>
#include <QLabel>
#include <QVector>

class PptxPanel : public QWidget
{
    Q_OBJECT
public:
    explicit PptxPanel(AppContext *ctx, QWidget *parent = nullptr);

signals:
    // Proyecta la lista de slides convertida (Slide::Pptx con imagen renderizada)
    void requestProjectPptx(const QVector<Slide> &slides);
    void requestAddPptxToService(const QString &name, const QVector<Slide> &slides);

private slots:
    void onOpen();
    void onExport();

private:
    void buildUi();
    void renderPreviews();

    AppContext *m_ctx;
    QString m_file;
    QLabel *m_preview = nullptr;
    QListWidget *m_list = nullptr;
    QLabel *m_status = nullptr;
    QVector<PptxEngine::PptxSlide> m_slides;
    QSize m_slideSize = QSize(1920, 1080);
    QVector<Slide> m_rendered;      // conversion a Slide para proyeccion
    QString m_importDir;
};

#endif // LUMINA_PPTXPANEL_H
