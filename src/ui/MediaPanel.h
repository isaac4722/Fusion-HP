// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MediaPanel.h : Biblioteca de fondos y medios (imagenes/videos) con
//  etiquetas, envio a pantalla y al culto.
// ============================================================================
#ifndef LUMINA_MEDIAPANEL_H
#define LUMINA_MEDIAPANEL_H

#include "../core/Types.h"

#include <wx/listctrl.h>
#include <wx/panel.h>

class wxListCtrl;
class Database;

class MediaPanel : public wxPanel
{
public:
    MediaPanel(wxWindow *parent, Database &db);

    void RefreshList();

private:
    void OnAdd(wxCommandEvent &e);
    void OnRemove(wxCommandEvent &e);
    void OnLive(wxCommandEvent &e);
    void OnAddService(wxCommandEvent &e);
    void OnTags(wxCommandEvent &e);
    void OnListActivated(wxListEvent &e);
    void OnContextMenu(wxContextMenuEvent &e);

    static int DetectKind(const wxString &path);
    int SelectedIndex() const;

    void QueueEventToFrame(wxEventType type, int id = 0, const wxString &str = wxString());

    Database &m_db;
    wxListCtrl *m_list;
    std::vector<MediaRow> m_rows;

    wxDECLARE_EVENT_TABLE();
};

#endif // LUMINA_MEDIAPANEL_H
