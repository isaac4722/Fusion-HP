// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  BiblePanel.h : Panel de Biblia — referencia tipada ("Jn 3:16"), busqueda
//  por palabras (FTS), seleccion multiple y envio a vivo/culto.
// ============================================================================
#ifndef LUMINA_BIBLEPANEL_H
#define LUMINA_BIBLEPANEL_H

#include "../core/Types.h"
#include "../core/BibleRef.h"
#include "../core/Database.h"

#include <wx/listctrl.h>
#include <wx/panel.h>

class wxComboBox;
class wxTextCtrl;

class BiblePanel : public wxPanel
{
public:
    BiblePanel(wxWindow *parent, Database &db);

    void RefreshVersions();
    void FocusQuickRef();
    void GoLiveSelection();          // usado por F9 externamente
    bool HasSelection() const;
    bool CollectSelection(std::vector<Database::BibleRow> &out) const;

private:
    void OnQuickRef(wxCommandEvent &e);
    void OnSearch(wxCommandEvent &e);
    void OnBtnLive(wxCommandEvent &e);
    void OnBtnAdd(wxCommandEvent &e);
    void OnListActivated(wxListEvent &e);

    void QueueEventToFrame(wxEventType type, int id = 0, const wxString &str = wxString());
    void LoadPassage(const BibleRef::VerseRef &ref);

    Database &m_db;
    wxComboBox *m_version;
    wxTextCtrl *m_quickRef;
    wxTextCtrl *m_searchBox;
    wxListCtrl *m_list;
    std::vector<Database::BibleRow> m_rows;
    wxString m_versionStr;

    wxDECLARE_EVENT_TABLE();
};

#endif // LUMINA_BIBLEPANEL_H
