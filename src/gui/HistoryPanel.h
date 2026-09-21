// ============================================================================
//  LuminaPresentation Suite - HistoryPanel.h
//  Historial y reportes: canciones mas usadas, uso reciente, exportacion
//  CSV y reporte PDF (QPrinter).
// ============================================================================
#ifndef LUMINA_HISTORYPANEL_H
#define LUMINA_HISTORYPANEL_H

#include "core/AppContext.h"

#include <QWidget>
#include <QTableWidget>

class HistoryPanel : public QWidget
{
    Q_OBJECT
public:
    explicit HistoryPanel(AppContext *ctx, QWidget *parent = nullptr);

public slots:
    void reload();

private slots:
    void onExportCsv();
    void onExportPdf();

private:
    void buildUi();
    AppContext *m_ctx;
    QTableWidget *m_stats = nullptr;
    QTableWidget *m_recent = nullptr;
};

#endif // LUMINA_HISTORYPANEL_H
