// ============================================================================
//  LuminaPresentation Suite - ServicePanel.h
//  Gestor de cultos/servicios: playlists ordenadas (canciones, biblia,
//  pptx, medios, avisos), reordenado, exportacion CSV y arranque del culto.
// ============================================================================
#ifndef LUMINA_SERVICEPANEL_H
#define LUMINA_SERVICEPANEL_H

#include "core/AppContext.h"

#include <QWidget>
#include <QListWidget>
#include <QComboBox>
#include <QPushButton>

class ServicePanel : public QWidget
{
    Q_OBJECT
public:
    explicit ServicePanel(AppContext *ctx, QWidget *parent = nullptr);
    void reloadPlaylists(int selectId = 0);

signals:
    // Arrancar culto: MainWindow carga los items en la cola lateral
    void startService(int playlistId);
    void requestRunItem(const ServiceItem &item);

private slots:
    void onNewService();
    void onDeleteService();
    void onAddSong();
    void onAddVerse();
    void onAddAviso();
    void onRemoveItem();
    void onMoveItem(bool up);
    void onExportCsv();

private:
    void buildUi();
    void reloadItems();

    AppContext *m_ctx;
    QComboBox *m_services = nullptr;
    QListWidget *m_items = nullptr;
    int m_currentServiceId = 0;
};

#endif // LUMINA_SERVICEPANEL_H
