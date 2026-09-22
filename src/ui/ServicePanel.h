// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ServicePanel.h : Panel del culto (playlist) — cultos guardados en la BD,
//  reordenamiento, envio a vivo, avisos y persistencia automatica.
// ============================================================================
#ifndef LUMINA_SERVICEPANEL_H
#define LUMINA_SERVICEPANEL_H

#include "../core/Types.h"

#include <wx/listctrl.h>
#include <wx/panel.h>

class wxComboBox;
class wxListCtrl;
class wxStaticText;
class Database;

class ServicePanel : public wxPanel
{
public:
    ServicePanel(wxWindow *parent, Database &db);

    // Reemplaza el contenido del culto activo (MainFrame guarda en BD)
    void SetItems(const std::vector<ServiceItem> &items, bool selectFirst = false);
    std::vector<ServiceItem> GetItems() const;
    int SelectedIndex() const;
    void SelectRow(int row);
    void RefreshPlaylists(int selectId = 0);
    int CurrentPlaylistId() const { return m_playlistId; }
    void SetCurrentPlaylist(int id);
    void OnNewPlaylist(wxCommandEvent &e);

private:
    void OnPlaylistChanged(wxCommandEvent &e);
    void OnDelPlaylist(wxCommandEvent &e);
    void OnAddSong(wxCommandEvent &e);
    void OnAddNotice(wxCommandEvent &e);
    void OnRemove(wxCommandEvent &e);
    void OnUp(wxCommandEvent &e);
    void OnDown(wxCommandEvent &e);
    void OnClear(wxCommandEvent &e);
    void OnListActivated(wxListEvent &e);
    void OnKeyDown(wxKeyEvent &e);
    void OnItemSelected(wxListEvent &e);

    void RebuildList();
    void MoveRow(int delta);
    void QueueEventToFrame(wxEventType type, int id = 0, const wxString &str = wxString());
    void NotifyChanged(int goLiveRow = -1);

    Database &m_db;
    wxComboBox *m_playlists;
    wxListCtrl *m_list;
    wxStaticText *m_count;
    std::vector<ServiceItem> m_items;
    int m_playlistId = 0;

    wxDECLARE_EVENT_TABLE();
};

#endif // LUMINA_SERVICEPANEL_H
