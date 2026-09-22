// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ThemesPanel.cpp : implementacion (ver .h)
// ============================================================================
#include "ThemesPanel.h"
#include "AppEvents.h"
#include "ThemeEditor.h"
#include "../core/Database.h"

#include <wx/button.h>
#include <wx/listctrl.h>
#include <wx/msgdlg.h>
#include <wx/toplevel.h>
#include <wx/sizer.h>
#include <wx/stattext.h>

namespace {
enum {
    ID_APPLY = wxID_HIGHEST + 600,
    ID_NEW,
    ID_EDIT,
    ID_DUP,
    ID_DEL,
    ID_DEF,
    ID_LIST,
};
}

BEGIN_EVENT_TABLE(ThemesPanel, wxPanel)
    EVT_BUTTON(ID_APPLY, ThemesPanel::OnApply)
    EVT_BUTTON(ID_NEW, ThemesPanel::OnNew)
    EVT_BUTTON(ID_EDIT, ThemesPanel::OnEdit)
    EVT_BUTTON(ID_DUP, ThemesPanel::OnDuplicate)
    EVT_BUTTON(ID_DEL, ThemesPanel::OnDelete)
    EVT_BUTTON(ID_DEF, ThemesPanel::OnDefault)
    EVT_LIST_ITEM_ACTIVATED(ID_LIST, ThemesPanel::OnListActivated)
END_EVENT_TABLE()


ThemesPanel::ThemesPanel(wxWindow *parent, Database &db)
    : wxPanel(parent, wxID_ANY), m_db(db)
{
    auto *root = new wxBoxSizer(wxVERTICAL);

    root->Add(new wxStaticText(this, wxID_ANY,
              L"Temas: fondo, tipografía, contorno y sombra de la proyección. "
              "El tema aplicado se usa hasta que elijas otro."),
              0, wxBOTTOM, 6);

    m_list = new wxListCtrl(this, ID_LIST, wxDefaultPosition, wxDefaultSize,
                            wxLC_REPORT | wxLC_SINGLE_SEL | wxBORDER_THEME);
    m_list->InsertColumn(0, "Tema", wxLIST_FORMAT_LEFT, 220);
    m_list->InsertColumn(1, "Fondo", wxLIST_FORMAT_LEFT, 130);
    m_list->InsertColumn(2, L"Tipografía", wxLIST_FORMAT_LEFT, 180);
    root->Add(m_list, 1, wxEXPAND);

    auto *row1 = new wxBoxSizer(wxHORIZONTAL);
    auto *apply = new wxButton(this, ID_APPLY, "Aplicar en vivo");
    apply->SetForegroundColour(wxColour(230, 168, 42));
    row1->Add(apply);
    row1->Add(new wxButton(this, ID_NEW, "Nuevo"));
    row1->Add(new wxButton(this, ID_EDIT, "Editar"));
    auto *row2 = new wxBoxSizer(wxHORIZONTAL);
    row2->Add(new wxButton(this, ID_DUP, "Duplicar"));
    row2->Add(new wxButton(this, ID_DEL, "Eliminar"));
    row2->Add(new wxButton(this, ID_DEF, "Predeterminado"));
    row2->AddStretchSpacer();

    root->Add(row1, 0, wxEXPAND | wxTOP, 6);
    root->Add(row2, 0, wxEXPAND | wxTOP, 4);

    SetSizer(root);
    RefreshList();
}

void ThemesPanel::RefreshList()
{
    m_rows = m_db.Themes();
    const int defId = m_db.DefaultThemeId();
    m_list->Freeze();
    m_list->DeleteAllItems();
    long i = 0;
    for (const Theme &t : m_rows) {
        wxString name = t.name;
        if (t.id == defId)
            name += L"   ★";
        m_list->InsertItem(i, name);
        wxString bg;
        switch (t.background.type) {
            case 0: bg = "Color"; break;
            case 1: bg = "Gradiente"; break;
            case 2: bg = "Imagen"; break;
        }
        m_list->SetItem(i, 1, bg);
        m_list->SetItem(i, 2, wxString::Format("%s %d pt", t.body.family, t.body.pointSize));
        m_list->SetItemPtrData(i, (wxUIntPtr)t.id);
        ++i;
    }
    m_list->Thaw();
}

int ThemesPanel::SelectedIndex() const
{
    return m_list->GetNextItem(-1, wxLIST_NEXT_ALL, wxLIST_STATE_SELECTED);
}

void ThemesPanel::QueueEventToFrame(wxEventType type, int id, const wxString &str)
{
    wxTopLevelWindow *top = wxDynamicCast(wxGetTopLevelParent(this), wxTopLevelWindow);
    if (!top)
        return;
    wxCommandEvent ev(type, id);
    ev.SetInt(id);
    ev.SetString(str);
    top->GetEventHandler()->QueueEvent(ev.Clone());
}

void ThemesPanel::OnApply(wxCommandEvent &)
{
    const int idx = SelectedIndex();
    if (idx < 0 || idx >= (int)m_rows.size())
        return;
    QueueEventToFrame(EVT_UI_APPLY_THEME, m_rows[idx].id);
}

void ThemesPanel::OnListActivated(wxListEvent &)
{
    wxCommandEvent ev;
    OnApply(ev);
}

void ThemesPanel::OnNew(wxCommandEvent &)
{
    ThemeEditor dlg(this, m_db, Theme(), /*isNew=*/true);
    if (dlg.ShowModal() == wxID_OK) {
        RefreshList();
        QueueEventToFrame(EVT_UI_DATA_CHANGED, 0, "themes");
    }
}

void ThemesPanel::OnEdit(wxCommandEvent &)
{
    const int idx = SelectedIndex();
    if (idx < 0 || idx >= (int)m_rows.size())
        return;
    ThemeEditor dlg(this, m_db, m_rows[idx], false);
    if (dlg.ShowModal() == wxID_OK) {
        RefreshList();
        QueueEventToFrame(EVT_UI_DATA_CHANGED, 0, "themes");
    }
}

void ThemesPanel::OnDuplicate(wxCommandEvent &)
{
    const int idx = SelectedIndex();
    if (idx < 0 || idx >= (int)m_rows.size())
        return;
    Theme t = m_rows[idx];
    t.id = 0;
    t.name += " (copia)";
    m_db.SaveTheme(t);
    RefreshList();
    QueueEventToFrame(EVT_UI_DATA_CHANGED, 0, "themes");
}

void ThemesPanel::OnDelete(wxCommandEvent &)
{
    const int idx = SelectedIndex();
    if (idx < 0 || idx >= (int)m_rows.size())
        return;
    const Theme &t = m_rows[idx];
    if (m_rows.size() <= 1) {
        wxMessageDialog msg(this, "Debe existir al menos un tema.", "Temas",
                            wxOK | wxICON_INFORMATION);
        msg.ShowModal();
        return;
    }
    if (wxMessageBox(wxString::Format(L"¿Eliminar el tema «%s»?", t.name),
                     "Eliminar tema", wxYES_NO | wxICON_WARNING) != wxYES)
        return;
    m_db.DeleteTheme(t.id);
    RefreshList();
    QueueEventToFrame(EVT_UI_DATA_CHANGED, 0, "themes");
}

void ThemesPanel::OnDefault(wxCommandEvent &)
{
    const int idx = SelectedIndex();
    if (idx < 0 || idx >= (int)m_rows.size())
        return;
    m_db.SetDefaultTheme(m_rows[idx].id);
    RefreshList();
}
