// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  SongsPanel.cpp : implementacion (ver .h)
// ============================================================================
#include "SongsPanel.h"
#include "AppEvents.h"
#include "Icons.h"
#include "SongEditor.h"
#include "../core/Database.h"

#include <wx/bmpbuttn.h>
#include <wx/button.h>
#include <wx/combobox.h>
#include <wx/listctrl.h>
#include <wx/menu.h>
#include <wx/msgdlg.h>
#include <wx/sizer.h>
#include <wx/stattext.h>
#include <wx/textctrl.h>
#include <wx/toplevel.h>

namespace {
enum {
    ID_SEARCH = wxID_HIGHEST + 100,
    ID_TIMER,
    ID_TAGFILTER,
    ID_LIST,
    ID_NEW,
    ID_EDIT,
    ID_DUP,
    ID_DEL,
    ID_LIVE,
    ID_ADD,
};
}

BEGIN_EVENT_TABLE(SongsPanel, wxPanel)
    EVT_TEXT(ID_SEARCH, SongsPanel::OnSearchText)
    EVT_TIMER(ID_TIMER, SongsPanel::OnSearchTimer)
    EVT_COMBOBOX(ID_TAGFILTER, SongsPanel::OnTagFilter)
    EVT_LIST_ITEM_SELECTED(ID_LIST, SongsPanel::OnItemSelected)
    EVT_LIST_ITEM_ACTIVATED(ID_LIST, SongsPanel::OnItemActivated)
    EVT_CONTEXT_MENU(SongsPanel::OnContextMenu)
    EVT_BUTTON(ID_NEW, SongsPanel::OnBtnNew)
    EVT_BUTTON(ID_EDIT, SongsPanel::OnBtnEdit)
    EVT_BUTTON(ID_DUP, SongsPanel::OnBtnDuplicate)
    EVT_BUTTON(ID_DEL, SongsPanel::OnBtnDelete)
    EVT_BUTTON(ID_LIVE, SongsPanel::OnBtnLive)
    EVT_BUTTON(ID_ADD, SongsPanel::OnBtnAdd)
    EVT_KEY_DOWN(SongsPanel::OnKeyDown)
END_EVENT_TABLE()


SongsPanel::SongsPanel(wxWindow *parent, Database &db)
    : wxPanel(parent, wxID_ANY), m_db(db)
{
    m_searchTimer.SetOwner(this, ID_TIMER);

    auto *root = new wxBoxSizer(wxVERTICAL);

    // --- Fila de busqueda ---------------------------------------------------
    auto *searchRow = new wxBoxSizer(wxHORIZONTAL);
    m_search = new wxTextCtrl(this, ID_SEARCH, wxString(), wxDefaultPosition,
                              wxDefaultSize, wxTE_PROCESS_ENTER);
    m_search->SetHint(L"Buscar por título, autor o letra…  (Ctrl+F)");
    m_search->SetToolTip(L"Búsqueda instantánea (FTS): escribe y filtra al momento");
    auto *searchIcoBtn = new wxStaticText(this, wxID_ANY, "");
    (void)searchIcoBtn;
    m_tagFilter = new wxComboBox(this, ID_TAGFILTER, wxString(), wxDefaultPosition,
                                 wxDefaultSize, 0, nullptr, wxCB_READONLY);
    m_tagFilter->SetToolTip(L"Filtrar por etiqueta (lento, navidad, entrada…)");
    searchRow->Add(m_search, 1, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    searchRow->Add(m_tagFilter, 0, wxALIGN_CENTER_VERTICAL);
    root->Add(searchRow, 0, wxEXPAND | wxBOTTOM, 6);

    // --- Lista ---------------------------------------------------------------
    m_list = new wxListCtrl(this, ID_LIST, wxDefaultPosition, wxDefaultSize,
                            wxLC_REPORT | wxLC_SINGLE_SEL | wxBORDER_THEME);
    m_list->InsertColumn(0, L"Título", wxLIST_FORMAT_LEFT, 260);
    m_list->InsertColumn(1, "Autor", wxLIST_FORMAT_LEFT, 150);
    m_list->InsertColumn(2, "Tono", wxLIST_FORMAT_LEFT, 60);
    m_list->InsertColumn(3, "BPM", wxLIST_FORMAT_RIGHT, 56);
    root->Add(m_list, 1, wxEXPAND);

    m_hint = new wxStaticText(this, wxID_ANY,
                              L"No hay canciones todavía — pulsa «Nueva» para crear la primera.");
    m_hint->SetForegroundColour(wxColour(120, 130, 148));
    m_hint->Hide();
    root->Add(m_hint, 0, wxALIGN_CENTER_HORIZONTAL | wxTOP, 10);

    // --- Acciones -------------------------------------------------------------
    auto *btnRow = new wxBoxSizer(wxHORIZONTAL);
    auto mk = [&](int id, const wxString &label, const wxString &icon, const wxString &tip) {
        auto *b = new wxBitmapButton(this, id, Ico::Get(icon, 20), wxDefaultPosition,
                                     wxDefaultSize, wxBU_AUTODRAW);
        b->SetToolTip(tip);
        return b;
    };
    btnRow->Add(mk(ID_NEW, "Nueva", "add", L"Nueva canción (Ctrl+N)"));
    btnRow->Add(mk(ID_EDIT, "Editar", "edit", L"Editar la canción seleccionada"));
    btnRow->Add(mk(ID_DUP, "Duplicar", "copy", L"Duplicar la canción seleccionada"));
    btnRow->Add(mk(ID_DEL, "Eliminar", "del", L"Eliminar la canción seleccionada"));
    btnRow->AddStretchSpacer();
    auto *liveBtn = new wxButton(this, ID_LIVE, "En vivo");
    liveBtn->SetForegroundColour(wxColour(230, 168, 42));
    liveBtn->SetToolTip(L"Proyectar ahora (doble clic en la lista también)");
    btnRow->Add(liveBtn, 0, wxRIGHT, 6);
    btnRow->Add(mk(ID_ADD, "Al culto", "service", L"Añadir al culto activo"));
    root->Add(btnRow, 0, wxEXPAND | wxTOP, 6);

    SetSizer(root);
    RefreshTags();
    RefreshList(false);
}

// ---------------------------------------------------------------------------
// Carga de datos
// ---------------------------------------------------------------------------
void SongsPanel::RefreshList(bool keepSelection)
{
    const long sel = keepSelection ? SelectedSongId() : 0;
    Database::SearchFilter f;
    f.q = m_search->GetValue().Trim().Trim(false);
    if (m_tagFilter->GetSelection() > 0)
        f.tag = m_tagFilter->GetStringSelection();
    m_rows = m_db.SearchSongs(f);

    m_list->Freeze();
    m_list->DeleteAllItems();
    long idx = 0;
    for (const Song &s : m_rows) {
        long i = m_list->InsertItem(idx, s.title);
        m_list->SetItemPtrData(i, (wxUIntPtr)s.id);
        m_list->SetItem(i, 1, s.artist);
        m_list->SetItem(i, 2, s.key);
        m_list->SetItem(i, 3, s.bpm > 0 ? wxString::Format("%d", s.bpm) : wxString());
        if (s.id == sel)
            m_list->SetItemState(i, wxLIST_STATE_SELECTED, wxLIST_STATE_SELECTED);
        ++idx;
    }
    m_list->Thaw();
    m_hint->Show(m_rows.empty());
    Layout();
}

void SongsPanel::RefreshTags()
{
    m_hasTagsComboInit = true;
    m_tagFilter->Clear();
    m_tagFilter->Append("(todas)");
    for (const wxString &t : m_db.AllTagNames())
        m_tagFilter->Append(t);
    m_tagFilter->SetSelection(0);
}

void SongsPanel::FocusSearch()
{
    m_search->SetFocus();
    m_search->SelectAll();
}

int SongsPanel::SelectedSongId() const
{
    const long sel = m_list->GetNextItem(-1, wxLIST_NEXT_ALL, wxLIST_STATE_SELECTED);
    if (sel < 0)
        return 0;
    return (int)(intptr_t)m_list->GetItemData(sel);
}

void SongsPanel::QueueEventToFrame(wxEventType type, int id, const wxString &str)
{
    wxTopLevelWindow *top = wxDynamicCast(wxGetTopLevelParent(this), wxTopLevelWindow);
    if (!top)
        return;
    wxCommandEvent ev(type, id);
    ev.SetInt(id);
    ev.SetString(str);
    top->GetEventHandler()->QueueEvent(ev.Clone());
}

void SongsPanel::EmitDataChanged()
{
    QueueEventToFrame(EVT_UI_DATA_CHANGED, 0, "songs");
}

// ---------------------------------------------------------------------------
// Eventos
// ---------------------------------------------------------------------------
void SongsPanel::OnSearchText(wxCommandEvent &)
{
    m_searchTimer.Start(280, wxTIMER_ONE_SHOT);
}

void SongsPanel::OnSearchTimer(wxTimerEvent &)
{
    RefreshList(false);
}

void SongsPanel::OnTagFilter(wxCommandEvent &)
{
    RefreshList(false);
}

void SongsPanel::OnItemSelected(wxListEvent &)
{
}

void SongsPanel::OnItemActivated(wxListEvent &)
{
    const int id = SelectedSongId();
    if (id > 0)
        QueueEventToFrame(EVT_UI_GOLIVE_SONG, id);
}

void SongsPanel::OnKeyDown(wxKeyEvent &e)
{
    if (e.GetKeyCode() == WXK_DELETE && SelectedSongId() > 0) {
        wxCommandEvent ev;
        OnBtnDelete(ev);
        return;
    }
    e.Skip();
}

void SongsPanel::NewSong()
{
    EditSong(0);
}

void SongsPanel::OnBtnNew(wxCommandEvent &)
{
    EditSong(0);
}

void SongsPanel::OnBtnEdit(wxCommandEvent &)
{
    const int id = SelectedSongId();
    if (id > 0)
        EditSong(id);
}

void SongsPanel::OnBtnDuplicate(wxCommandEvent &)
{
    const int id = SelectedSongId();
    if (id <= 0)
        return;
    Song s = m_db.SongById(id);
    s.id = 0;
    s.title += " (copia)";
    const int newId = m_db.AddSong(s);
    m_db.SetSongTags(newId, m_db.SongTags(id));
    RefreshList(false);
    EmitDataChanged();
}

void SongsPanel::OnBtnDelete(wxCommandEvent &)
{
    const int id = SelectedSongId();
    if (id <= 0)
        return;
    const Song s = m_db.SongById(id);
    if (wxMessageBox(wxString::Format("¿Eliminar «%s»?\n\nEsta acción no se puede deshacer.",
                                      s.title),
                     L"Eliminar canción", wxYES_NO | wxICON_WARNING) != wxYES)
        return;
    m_db.DeleteSong(id);
    RefreshList(false);
    EmitDataChanged();
}

void SongsPanel::OnBtnLive(wxCommandEvent &)
{
    const int id = SelectedSongId();
    if (id > 0)
        QueueEventToFrame(EVT_UI_GOLIVE_SONG, id);
}

void SongsPanel::OnBtnAdd(wxCommandEvent &)
{
    const int id = SelectedSongId();
    if (id > 0)
        QueueEventToFrame(EVT_UI_ADD_SONG, id);
}

void SongsPanel::OnContextMenu(wxContextMenuEvent &e)
{
    if (SelectedSongId() <= 0) {
        e.Skip();
        return;
    }
    wxMenu menu;
    menu.Append(ID_LIVE, "En vivo\tF5");
    menu.Append(ID_ADD, L"Añadir al culto");
    menu.AppendSeparator();
    menu.Append(ID_EDIT, L"Editar…");
    menu.Append(ID_DUP, "Duplicar");
    menu.AppendSeparator();
    menu.Append(ID_DEL, "Eliminar\tSupr");
    PopupMenu(&menu);
}

// ---------------------------------------------------------------------------
// Editor
// ---------------------------------------------------------------------------
void SongsPanel::EditSong(int songId)
{
    SongEditor dlg(this, m_db, songId);
    if (dlg.ShowModal() == wxID_OK) {
        RefreshList();
        RefreshTags();
        EmitDataChanged();
    }
}
