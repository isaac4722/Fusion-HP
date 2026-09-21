// ============================================================================
//  LuminaPresentation Suite - HistoryPanel.cpp
// ============================================================================
#include "HistoryPanel.h"
#include "core/Database.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QHeaderView>
#include <QFileDialog>
#include <QMessageBox>
#include <QLabel>
#include <QPushButton>
#include <QTextDocument>
#include <QPrinter>
#include <QPdfWriter>
#include <QDateTime>

HistoryPanel::HistoryPanel(AppContext *ctx, QWidget *parent)
    : QWidget(parent), m_ctx(ctx)
{
    buildUi();
    reload();
}

void HistoryPanel::buildUi()
{
    auto *lay = new QVBoxLayout(this);
    lay->setContentsMargins(12, 12, 12, 12);

    lay->addWidget(new QLabel(QStringLiteral("<b>Canciones más utilizadas</b>"), this));
    m_stats = new QTableWidget(this);
    m_stats->setColumnCount(3);
    m_stats->setHorizontalHeaderLabels({ QStringLiteral("Canción"), QStringLiteral("Veces"),
                                         QStringLiteral("Último uso") });
    m_stats->horizontalHeader()->setSectionResizeMode(0, QHeaderView::Stretch);
    m_stats->verticalHeader()->setVisible(false);
    m_stats->setEditTriggers(QAbstractItemView::NoEditTriggers);
    lay->addWidget(m_stats, 1);

    lay->addWidget(new QLabel(QStringLiteral("<b>Uso reciente</b>"), this));
    m_recent = new QTableWidget(this);
    m_recent->setColumnCount(2);
    m_recent->setHorizontalHeaderLabels({ QStringLiteral("Fecha / hora"), QStringLiteral("Canción") });
    m_recent->horizontalHeader()->setSectionResizeMode(0, QHeaderView::Stretch);
    m_recent->horizontalHeader()->setSectionResizeMode(1, QHeaderView::Stretch);
    m_recent->verticalHeader()->setVisible(false);
    m_recent->setEditTriggers(QAbstractItemView::NoEditTriggers);
    lay->addWidget(m_recent, 1);

    auto *row = new QHBoxLayout();
    auto *bCsv = new QPushButton(QStringLiteral("Exportar CSV"), this);
    auto *bPdf = new QPushButton(QStringLiteral("Generar reporte PDF"), this);
    connect(bCsv, &QPushButton::clicked, this, &HistoryPanel::onExportCsv);
    connect(bPdf, &QPushButton::clicked, this, &HistoryPanel::onExportPdf);
    row->addWidget(bCsv);
    row->addWidget(bPdf);
    row->addStretch();
    lay->addLayout(row);
}

void HistoryPanel::reload()
{
    const auto rows = m_ctx->db->songReport();
    m_stats->setRowCount(rows.size());
    for (int i = 0; i < rows.size(); ++i) {
        m_stats->setItem(i, 0, new QTableWidgetItem(rows.at(i).title));
        m_stats->setItem(i, 1, new QTableWidgetItem(QString::number(rows.at(i).count)));
        m_stats->setItem(i, 2, new QTableWidgetItem(rows.at(i).lastUsed));
    }
    const auto recent = m_ctx->db->recentUsage(120);
    m_recent->setRowCount(recent.size());
    for (int i = 0; i < recent.size(); ++i) {
        const QDateTime dt = QDateTime::fromString(recent.at(i).usedAt, Qt::ISODate);
        m_recent->setItem(i, 0, new QTableWidgetItem(dt.toString(QStringLiteral("dd/MM/yyyy hh:mm"))));
        m_recent->setItem(i, 1, new QTableWidgetItem(recent.at(i).title));
    }
}

void HistoryPanel::onExportCsv()
{
    const QString out = QFileDialog::getSaveFileName(this, QStringLiteral("Exportar reporte"),
                                                     QStringLiteral("reporte_canciones.csv"),
                                                     QStringLiteral("CSV (*.csv)"));
    if (out.isEmpty()) return;
    QFile f(out);
    if (!f.open(QIODevice::WriteOnly | QIODevice::Text)) return;
    QTextStream ts(&f);
    ts.setCodec("UTF-8");
    ts << QStringLiteral("cancion;veces;ultimo_uso\n");
    for (const auto &r : m_ctx->db->songReport())
        ts << QStringLiteral("%1;%2;%3\n").arg(r.title).arg(r.count).arg(r.lastUsed);
    f.close();
    QMessageBox::information(this, QStringLiteral("CSV"), QStringLiteral("Reporte exportado."));
}

void HistoryPanel::onExportPdf()
{
    const QString out = QFileDialog::getSaveFileName(this, QStringLiteral("Reporte PDF"),
                                                     QStringLiteral("reporte_lumina.pdf"),
                                                     QStringLiteral("PDF (*.pdf)"));
    if (out.isEmpty()) return;
    QPrinter printer(QPrinter::HighResolution);
    printer.setOutputFormat(QPrinter::PdfFormat);
    printer.setOutputFileName(out);
    QString html = QStringLiteral("<h1>LuminaPresentation Suite — Reporte de uso</h1>"
                                  "<p>Generado: %1</p><table border='1' cellspacing='0' cellpadding='4'>"
                                  "<tr><th>Canción</th><th>Veces</th><th>Último uso</th></tr>")
                       .arg(QDateTime::currentDateTime().toString(QStringLiteral("dd/MM/yyyy hh:mm")));
    // CORRECCION v1.2.0: los títulos se insertaban sin escapar HTML — una
    // canción llamada "A&B" o "<Padre>" rompía la tabla del reporte PDF.
    for (const auto &r : m_ctx->db->songReport())
        html += QStringLiteral("<tr><td>%1</td><td align='center'>%2</td><td>%3</td></tr>")
                    .arg(r.title.toHtmlEscaped()).arg(r.count).arg(r.lastUsed);
    html += QStringLiteral("</table>");
    QTextDocument doc;
    doc.setHtml(html);
    doc.print(&printer);
    QMessageBox::information(this, QStringLiteral("PDF"), QStringLiteral("Reporte generado:\n%1").arg(out));
}
