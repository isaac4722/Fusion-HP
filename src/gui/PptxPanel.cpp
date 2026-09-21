// ============================================================================
//  LuminaPresentation Suite - PptxPanel.cpp
// ============================================================================
#include "PptxPanel.h"
#include "core/Database.h"
#include "core/Renderer.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QFileDialog>
#include <QMessageBox>
#include <QSplitter>
#include <QPushButton>
#include <QJsonDocument>
#include <QJsonObject>
#include <QJsonArray>
#include <QDateTime>

PptxPanel::PptxPanel(AppContext *ctx, QWidget *parent)
    : QWidget(parent), m_ctx(ctx)
{
    buildUi();
}

void PptxPanel::buildUi()
{
    auto *lay = new QVBoxLayout(this);
    lay->setContentsMargins(12, 12, 12, 12);

    auto *row = new QHBoxLayout();
    auto *bOpen = new QPushButton(QStringLiteral("📂 Abrir .PPTX (OpenXML, sin Office)"), this);
    bOpen->setStyleSheet(QStringLiteral("QPushButton{background:#1E6FD9;color:white;font-weight:bold;padding:8px 16px;}"));
    auto *bExport = new QPushButton(QStringLiteral("💾 Exportar a .PPTX"), this);
    // v1.1.0: exportar el escenario EN VIVO a PDF (spec: "PPTX y PDF")
    auto *bPdf = new QPushButton(QStringLiteral("📄 Exportar en vivo a PDF"), this);
    // v1.1.0: boton Añadir al culto (la señal existía pero NUNCA se emitía)
    auto *bCulto = new QPushButton(QStringLiteral("＋ A culto"), this);
    bCulto->setToolTip(QStringLiteral("Añade el .pptx abierto a la cola del culto activo "
                                      "(guarda la ruta del archivo para poder ejecutarlo desde la cola)."));
    connect(bOpen, &QPushButton::clicked, this, &PptxPanel::onOpen);
    connect(bExport, &QPushButton::clicked, this, &PptxPanel::onExport);
    connect(bPdf, &QPushButton::clicked, this, [this]() { emit requestExportPdf(); });
    connect(bCulto, &QPushButton::clicked, this, [this]() {
        if (m_file.isEmpty()) {
            QMessageBox::information(this, QStringLiteral("A culto"),
                                     QStringLiteral("Abre primero un archivo .pptx."));
            return;
        }
        emit requestAddPptxToService(m_file);
    });
    row->addWidget(bOpen);
    row->addWidget(bExport);
    row->addWidget(bPdf);
    row->addStretch();
    row->addWidget(bCulto);
    lay->addLayout(row);

    auto *split = new QSplitter(Qt::Horizontal, this);
    m_list = new QListWidget(split);
    m_list->setMinimumWidth(280);
    m_preview = new QLabel(split);
    m_preview->setAlignment(Qt::AlignCenter);
    m_preview->setMinimumSize(520, 320);
    m_preview->setStyleSheet(QStringLiteral("background:#111; border:1px solid #333;"));
    split->addWidget(m_list);
    split->addWidget(m_preview);
    split->setStretchFactor(1, 2);
    lay->addWidget(split, 1);

    connect(m_list, &QListWidget::currentRowChanged, this, [this](int row) {
        if (row >= 0 && row < m_rendered.size()) {
            const Slide &s = m_rendered.at(row);
            QImage img(s.mediaPath);
            if (img.isNull()) return;
            m_preview->setPixmap(QPixmap::fromImage(img).scaled(
                m_preview->size(), Qt::KeepAspectRatio, Qt::SmoothTransformation));
        }
    });

    m_status = new QLabel(QStringLiteral(
        "El motor OpenXML lee el ZIP interno del .pptx y extrae textos, formas e imágenes "
        "sin depender de Microsoft Office ni objetos OLE."), this);
    m_status->setWordWrap(true);
    m_status->setStyleSheet(QStringLiteral("color: #8FA3C8;"));
    lay->addWidget(m_status);
}

void PptxPanel::onOpen()
{
    const QString f = QFileDialog::getOpenFileName(this, QStringLiteral("Abrir presentación PowerPoint"),
                                                   QString(), QStringLiteral("PowerPoint (*.pptx);;Todos (*)"));
    if (f.isEmpty()) return;
    QString err;
    m_slides.clear();
    if (!PptxEngine::importPptx(f, &m_slides, &m_slideSize, &err)) {
        QMessageBox::warning(this, QStringLiteral("Importación PPTX"), err);
        return;
    }
    m_file = f;
    m_importDir = QDir::tempPath() + QStringLiteral("/LuminaImports/");

    // v1.1.0: la rasterización vive ahora en PptxEngine::renderToSlides
    // (compartida con la ejecución de items PPTX de la cola del culto).
    const Theme theme = *m_ctx->currentTheme;
    m_rendered = PptxEngine::renderToSlides(m_slides, m_slideSize, theme,
                                            QFileInfo(f).completeBaseName());

    // Lista + previews
    m_list->clear();
    for (int i = 0; i < m_rendered.size(); ++i) {
        QImage img(m_rendered.at(i).mediaPath);
        QIcon icon(QPixmap::fromImage(img.scaled(160, 90, Qt::KeepAspectRatio, Qt::SmoothTransformation)));
        auto *item = new QListWidgetItem(icon, QStringLiteral("Slide %1").arg(i + 1));
        m_list->addItem(item);
    }
    m_status->setText(QStringLiteral("✔ %1 slides importadas de “%2”. "
                                     "Doble propósito: proyectar ya o añadir al culto.")
                          .arg(m_rendered.size()).arg(QFileInfo(f).fileName()));
}

void PptxPanel::onExport()
{
    // CORRECCION: antes se exportaban m_rendered (slides de IMAGEN sin lines),
    // lo que producia un .pptx vacio. Ahora:
    //  1) Si hay un PPTX cargado, se exportan sus CAJAS DE TEXTO reales.
    //  2) Si no, se pide a MainWindow exportar el contenido en vivo
    //     (cancion/biblia con texto verdadero).
    if (!m_slides.isEmpty()) {
        QVector<Slide> textSlides;
        for (const PptxEngine::PptxSlide &ps : m_slides) {
            Slide s;
            s.kind = Slide::Pptx;
            s.title = QFileInfo(m_file).completeBaseName();
            s.refLabel = QStringLiteral("Slide %1/%2").arg(textSlides.size() + 1).arg(m_slides.size());
            for (const PptxEngine::Box &b : ps.boxes) {
                if (b.kind == PptxEngine::Box::Text && !b.text.simplified().isEmpty()) {
                    const QStringList paragraphs = b.text.split(QChar('\n'));
                    for (const QString &par : paragraphs)
                        s.lines.append(SlideLine(par));
                }
            }
            textSlides.append(s);
        }
        const QString out = QFileDialog::getSaveFileName(this, QStringLiteral("Exportar a PowerPoint"),
                                                         QStringLiteral("presentacion.pptx"),
                                                         QStringLiteral("PowerPoint (*.pptx)"));
        if (out.isEmpty()) return;
        QString err;
        if (PptxEngine::exportPptx(textSlides, *m_ctx->currentTheme, out, &err))
            QMessageBox::information(this, QStringLiteral("Exportar"),
                                     QStringLiteral("Exportado correctamente a:\n%1").arg(out));
        else
            QMessageBox::warning(this, QStringLiteral("Exportar"), err);
        return;
    }
    // Sin PPTX cargado: exporta el contenido en vivo (texto real de la
    // cancion/versiculo que esta proyectado).
    emit requestExportLive();
}
