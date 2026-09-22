// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  BiblePanel.cpp : implementacion (ver .h)
// ============================================================================
#include "BiblePanel.h"
#include "AppEvents.h"
#include "../core/Database.h"

#include <wx/button.h>
#include <wx/combobox.h>
#include <wx/msgdlg.h>
#include <wx/listctrl.h>
#include <wx/sizer.h>
#include <wx/stattext.h>
#include <wx/textctrl.h>
#include <wx/toplevel.h>

namespace {
enum {
    ID_QUICK = wxID_HIGHEST + 400,
    ID_GO,
    ID_SEARCHBOX,
    ID_SEARCH,
    ID_LIST,
    ID_LIVE,
    ID_ADD,
};
}

BEGIN_EVENT_TABLE(BiblePanel, wxPanel)
    EVT_TEXT_ENTER(ID_QUICK, BiblePanel::OnQuickRef)
    EVT_BUTTON(ID_GO, BiblePanel::OnQuickRef)
    EVT_TEXT_ENTER(ID_SEARCHBOX, BiblePanel::OnSearch)
    EVT_BUTTON(ID_SEARCH, BiblePanel::OnSearch)
    EVT_BUTTON(ID_LIVE, BiblePanel::OnBtnLive)
    EVT_BUTTON(ID_ADD, BiblePanel::OnBtnAdd)
    EVT_LIST_ITEM_ACTIVATED(ID_LIST, BiblePanel::OnListActivated)
END_EVENT_TABLE()


BiblePanel::BiblePanel(wxWindow *parent, Database &db)
    : wxPanel(parent, wxID_ANY), m_db(db)
{
    auto *root = new wxBoxSizer(wxVERTICAL);

    // --- Referencia rapida ------------------------------------------------------
    auto *quickRow = new wxBoxSizer(wxHORIZONTAL);
    m_version = new wxComboBox(this, wxID_ANY, wxString(), wxDefaultPosition,
                               wxDefaultSize, 0, nullptr, wxCB_READONLY);
    m_quickRef = new wxTextCtrl(this, ID_QUICK, wxString(), wxDefaultPosition,
                                wxDefaultSize, wxTE_PROCESS_ENTER);
    m_quickRef->SetHint(L"Referencia: Jn 3:16, salmo 23:1-4…  (F9 en cualquier parte)");
    auto *go = new wxButton(this, ID_GO, "Ir");
    go->SetDefault();
    quickRow->Add(m_version, 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    quickRow->Add(m_quickRef, 1, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    quickRow->Add(go, 0, wxALIGN_CENTER_VERTICAL);
    root->Add(quickRow, 0, wxEXPAND | wxBOTTOM, 6);

    // --- Busqueda por palabras -----------------------------------------------------
    auto *searchRow = new wxBoxSizer(wxHORIZONTAL);
    m_searchBox = new wxTextCtrl(this, ID_SEARCHBOX, wxString(), wxDefaultPosition,
                                 wxDefaultSize, wxTE_PROCESS_ENTER);
    m_searchBox->SetHint(L"Buscar palabras en toda la Biblia…");
    auto *searchBtn = new wxButton(this, ID_SEARCH, "Buscar");
    searchRow->Add(m_searchBox, 1, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    searchRow->Add(searchBtn, 0, wxALIGN_CENTER_VERTICAL);
    root->Add(searchRow, 0, wxEXPAND | wxBOTTOM, 6);

    // --- Resultados ----------------------------------------------------------------
    m_list = new wxListCtrl(this, ID_LIST, wxDefaultPosition, wxDefaultSize,
                            wxLC_REPORT | wxBORDER_THEME);
    m_list->InsertColumn(0, "Ref.", wxLIST_FORMAT_LEFT, 110);
    m_list->InsertColumn(1, "Texto", wxLIST_FORMAT_LEFT, 420);
    root->Add(m_list, 1, wxEXPAND);

    // --- Acciones ---------------------------------------------------------------------
    auto *btnRow = new wxBoxSizer(wxHORIZONTAL);
    auto *live = new wxButton(this, ID_LIVE, "En vivo");
    live->SetForegroundColour(wxColour(230, 168, 42));
    live->SetToolTip(L"Proyectar los versículos seleccionados (o todo el pasaje)");
    auto *add = new wxButton(this, ID_ADD, L"Añadir al culto");
    btnRow->AddStretchSpacer();
    btnRow->Add(live, 0, wxRIGHT, 6);
    btnRow->Add(add);
    root->Add(btnRow, 0, wxEXPAND | wxTOP, 6);

    SetSizer(root);
    RefreshVersions();
}

void BiblePanel::RefreshVersions()
{
    m_version->Clear();
    const std::vector<wxString> vs = m_db.BibleVersions();
    for (const wxString &v : vs)
        m_version->Append(v);
    if (m_version->GetCount() > 0)
        m_version->SetSelection(0);
    m_versionStr = m_version->GetStringSelection();
}

void BiblePanel::FocusQuickRef()
{
    m_quickRef->SetFocus();
    m_quickRef->SelectAll();
}

bool BiblePanel::HasSelection() const
{
    return m_list->GetSelectedItemCount() > 0;
}

void BiblePanel::QueueEventToFrame(wxEventType type, int id, const wxString &str)
{
    wxTopLevelWindow *top = wxDynamicCast(wxGetTopLevelParent(this), wxTopLevelWindow);
    if (!top)
        return;
    wxCommandEvent ev(type, id);
    ev.SetInt(id);
    ev.SetString(str);
    top->GetEventHandler()->QueueEvent(ev.Clone());
}

// ---------------------------------------------------------------------------
// Acciones
// ---------------------------------------------------------------------------
void BiblePanel::OnQuickRef(wxCommandEvent &)
{
    const wxString raw = m_quickRef->GetValue().Trim(true).Trim(false);
    if (raw.empty())
        return;
    const BibleRef::VerseRef ref = BibleRef::Resolve(raw);
    if (!ref.Valid()) {
        wxMessageDialog msg(this,
            L"No se reconoce la referencia «" + raw + "\".\n\n"
            L"Ejemplos válidos:  Jn 3:16 · salmo 23:1-4 · 1 co 13, 4-7 · genesis 1",
            "Referencia", wxOK | wxICON_INFORMATION);
        msg.ShowModal();
        return;
    }
    LoadPassage(ref);
}

void BiblePanel::LoadPassage(const BibleRef::VerseRef &ref)
{
    const int vFrom = ref.verse > 0 ? ref.verse : 1;
    const int vTo = ref.verse > 0 ? ref.verse : 200;
    m_rows = m_db.BiblePassage(m_versionStr, ref.book, ref.chapter, vFrom, vTo);
    m_list->Freeze();
    m_list->DeleteAllItems();
    long i = 0;
    for (const Database::BibleRow &r : m_rows) {
        const BibleRef::VerseRef vr{ r.book, r.chapter, r.verse, "" };
        m_list->InsertItem(i, BibleRef::FormatRef(vr));
        m_list->SetItem(i, 1, r.text);
        ++i;
    }
    m_list->Thaw();
}

void BiblePanel::OnSearch(wxCommandEvent &)
{
    const wxString q = m_searchBox->GetValue().Trim(true).Trim(false);
    if (q.empty())
        return;
    m_rows = m_db.BibleSearch(q, m_versionStr);
    m_list->Freeze();
    m_list->DeleteAllItems();
    long i = 0;
    for (const Database::BibleRow &r : m_rows) {
        const BibleRef::VerseRef vr{ r.book, r.chapter, r.verse, "" };
        m_list->InsertItem(i, BibleRef::FormatRef(vr));
        wxString txt = r.text;
        if (txt.Length() > 160)
            txt = txt.Left(160) + L"…";
        m_list->SetItem(i, 1, txt);
        ++i;
    }
    m_list->Thaw();
}

// Multiples seleccionados: si son consecutivos, un pasaje; si no, solo el primero
bool BiblePanel::CollectSelection(std::vector<Database::BibleRow> &out) const
{
    std::vector<int> idxs;
    long item = -1;
    while ((item = m_list->GetNextItem(item, wxLIST_NEXT_ALL, wxLIST_STATE_SELECTED)) >= 0)
        idxs.push_back((int)item);
    if (idxs.empty())
        return false;
    for (int i : idxs)
        out.push_back(m_rows[i]);
    return true;
}

void BiblePanel::GoLiveSelection()
{
    std::vector<Database::BibleRow> sel;
    if (!CollectSelection(sel) || sel.empty())
        return;
    // payload "book|chap|vfrom|vto" del rango seleccionado
    const int book = sel.front().book, chap = sel.front().chapter;
    const int vFrom = sel.front().verse;
    const int vTo = sel.back().verse;
    QueueEventToFrame(EVT_UI_GOLIVE_REF, 0,
                      wxString::Format("%d|%d|%d|%d", book, chap, vFrom, vTo));
}

void BiblePanel::OnBtnLive(wxCommandEvent &)
{
    GoLiveSelection();
}

void BiblePanel::OnBtnAdd(wxCommandEvent &)
{
    std::vector<Database::BibleRow> sel;
    if (!CollectSelection(sel) || sel.empty())
        return;
    const Database::BibleRow &r = sel.front();
    const BibleRef::VerseRef vr{ r.book, r.chapter, r.verse, "" };
    QueueEventToFrame(EVT_UI_ADD_REF, 0, BibleRef::FormatRef(vr));
}

void BiblePanel::OnListActivated(wxListEvent &e)
{
    if (e.GetIndex() >= 0 && e.GetIndex() < (int)m_rows.size()) {
        const Database::BibleRow &r = m_rows[e.GetIndex()];
        QueueEventToFrame(EVT_UI_GOLIVE_REF, 0,
                          wxString::Format("%d|%d|%d|%d", r.book, r.chapter, r.verse, r.verse));
    }
}
