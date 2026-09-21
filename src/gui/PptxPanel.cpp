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
    auto *bExport = new QPushButton(QStringLiteral("💾 Exportar culto/letra actual a .PPTX"), this);
    connect(bOpen, &QPushButton::clicked, this, &PptxPanel::onOpen);
    connect(bExport, &QPushButton::clicked, this, &PptxPanel::onExport);
    row->addWidget(bOpen);
    row->addWidget(bExport);
    row->addStretch();
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

    // Conversion a Slide: cada slide se rasteriza a PNG para salida fiel
    m_rendered.clear();
    const Theme theme = *m_ctx->currentTheme;
    for (const PptxEngine::PptxSlide &ps : m_slides) {
        QPixmap pm(m_slideSize);
        pm.fill(QColor(255, 255, 255));
        QPainter p(&pm);
        p.setRenderHint(QPainter::Antialiasing, true);
        p.setRenderHint(QPainter::TextAntialiasing, true);
        p.setRenderHint(QPainter::SmoothPixmapTransform, true);
        for (const PptxEngine::Box &b : ps.boxes) {
            QRectF box(b.x, b.y, b.w, b.h);
            if (b.kind == PptxEngine::Box::Image) {
                QImage img(b.imagePath);
                if (!img.isNull())
                    Renderer::drawImageFit(p, img, box.toRect(), Qt::KeepAspectRatio);
            } else if (b.kind == PptxEngine::Box::Shape) {
                QBrush fill(QColor(30, 60, 140, 200));
                QPen pen(QColor(60, 140, 255), 3);
                if (b.prst == QStringLiteral("ellipse"))
                    p.setBrush(fill), p.setPen(pen), p.drawEllipse(box);
                else if (b.prst == QStringLiteral("roundRect"))
                    p.setBrush(fill), p.setPen(pen), p.drawRoundedRect(box, 18, 18);
                else
                    p.setBrush(fill), p.setPen(pen), p.drawRect(box);
            } else {
                TextStyle st = theme.body;
                st.pointSize = b.fontSize;
                st.bold = b.bold;
                st.italic = b.italic;
                st.color = QColor(b.color);
                st.align = b.align;
                st.shadow = true;
                Renderer::drawStyledText(p, st, b.text, box, 1.0);
            }
        }
        p.end();
        const QString png = m_importDir + QStringLiteral("render_%1.png")
                .arg(QDateTime::currentMSecsSinceEpoch() + m_rendered.size());
        pm.save(png, "PNG");
        Slide s;
        s.kind = Slide::Pptx;
        s.title = QFileInfo(f).completeBaseName();
        s.refLabel = QStringLiteral("Slide %1/%2").arg(m_rendered.size() + 1).arg(m_slides.size());
        s.mediaPath = png;
        m_rendered.append(s);
    }

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
