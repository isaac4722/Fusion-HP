// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  SongsPanel.h : Panel de biblioteca de canciones (busqueda instantanea FTS,
//  filtro por etiquetas, CRUD, Envio a vivo y al culto).
// ============================================================================
#ifndef LUMINA_SONGSPANEL_H
#define LUMINA_SONGSPANEL_H

#include "../core/Types.h"

#include <wx/listctrl.h>
#include <wx/listctrl.h>
#include <wx/panel.h>
#include <wx/stattext.h>
#include <wx/timer.h>

class wxListCtrl;
class wxTextCtrl;
class wxComboBox;
class Database;

class SongsPanel : public wxPanel
{
public:
    SongsPanel(wxWindow *parent, Database &db);

    void RefreshList(bool keepSelection = true);
    void RefreshTags();
    void FocusSearch();
    int  SelectedSongId() const;
    void EditSong(int songId);              // abre el editor
    void NewSong();

private:
    void OnSearchText(wxCommandEvent &e);
    void OnSearchTimer(wxTimerEvent &e);
    void OnTagFilter(wxCommandEvent &e);
    void OnItemSelected(wxListEvent &e);
    void OnItemActivated(wxListEvent &e);   // doble clic = en vivo
    void OnContextMenu(wxContextMenuEvent &e);
    void OnBtnNew(wxCommandEvent &e);
    void OnBtnEdit(wxCommandEvent &e);
    void OnBtnDuplicate(wxCommandEvent &e);
    void OnBtnDelete(wxCommandEvent &e);
    void OnBtnLive(wxCommandEvent &e);
    void OnBtnAdd(wxCommandEvent &e);
    void OnKeyDown(wxKeyEvent &e);

    void QueueEventToFrame(wxEventType type, int id = 0, const wxString &str = wxString());
    void EmitDataChanged();

    Database &m_db;
    wxTextCtrl *m_search;
    wxComboBox *m_tagFilter;
    wxListCtrl *m_list;
    wxStaticText *m_hint;
    wxTimer m_searchTimer;
    std::vector<Song> m_rows;
    bool m_hasTagsComboInit = false;

    wxDECLARE_EVENT_TABLE();
};

#endif // LUMINA_SONGSPANEL_H
