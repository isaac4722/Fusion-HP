// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ThemesPanel.h : Panel de temas (plantillas maestras) — aplicar en vivo,
//  CRUD con editor visual, tema predeterminado.
// ============================================================================
#ifndef LUMINA_THEMESPANEL_H
#define LUMINA_THEMESPANEL_H

#include "../core/Types.h"

#include <wx/listctrl.h>
#include <wx/panel.h>

class wxListCtrl;
class Database;

class ThemesPanel : public wxPanel
{
public:
    ThemesPanel(wxWindow *parent, Database &db);

    void RefreshList();

private:
    void OnApply(wxCommandEvent &e);
    void OnNew(wxCommandEvent &e);
    void OnEdit(wxCommandEvent &e);
    void OnDuplicate(wxCommandEvent &e);
    void OnDelete(wxCommandEvent &e);
    void OnDefault(wxCommandEvent &e);
    void OnListActivated(wxListEvent &e);

    int SelectedIndex() const;
    void QueueEventToFrame(wxEventType type, int id = 0, const wxString &str = wxString());

    Database &m_db;
    wxListCtrl *m_list;
    std::vector<Theme> m_rows;

    wxDECLARE_EVENT_TABLE();
};

#endif // LUMINA_THEMESPANEL_H
