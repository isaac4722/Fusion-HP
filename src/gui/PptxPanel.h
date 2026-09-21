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
    // v1.1.0: envia al culto la RUTA del archivo (la cola puede re-ejecutarlo)
    void requestAddPptxToService(const QString &filePath);
    // Exportar el contenido EN VIVO actual (canción/biblia proyectada) a .pptx
    void requestExportLive();
    // v1.1.0: exportar el escenario en vivo a PDF (spec: "PPTX y PDF")
    void requestExportPdf();
    // v1.3.0: exportar el escenario en vivo como imágenes PNG (spec PowerPoint:
    // «exportar diapositivas individuales como imágenes»)
    void requestExportPng();

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
