// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ServicePanel.cpp : implementacion (ver .h)
// ============================================================================
#include "ServicePanel.h"
#include "AppEvents.h"
#include "Icons.h"
#include "../core/Database.h"

#include <wx/bmpbuttn.h>
#include <wx/button.h>
#include <wx/combobox.h>
#include <wx/listctrl.h>
#include <wx/msgdlg.h>
#include <wx/sizer.h>
#include <wx/stattext.h>
#include <wx/textdlg.h>
#include <wx/toplevel.h>

namespace {
enum {
    ID_PLAYLISTS = wxID_HIGHEST + 700,
    ID_NEWLIST,
    ID_DELLIST,
    ID_ADDSONG,
    ID_ADDNOTICE,
    ID_REMOVE,
    ID_UP,
    ID_DOWN,
    ID_CLEAR,
    ID_LIST,
};
}

BEGIN_EVENT_TABLE(ServicePanel, wxPanel)
    EVT_COMBOBOX(ID_PLAYLISTS, ServicePanel::OnPlaylistChanged)
    EVT_BUTTON(ID_NEWLIST, ServicePanel::OnNewPlaylist)
    EVT_BUTTON(ID_DELLIST, ServicePanel::OnDelPlaylist)
    EVT_BUTTON(ID_ADDSONG, ServicePanel::OnAddSong)
    EVT_BUTTON(ID_ADDNOTICE, ServicePanel::OnAddNotice)
    EVT_BUTTON(ID_REMOVE, ServicePanel::OnRemove)
    EVT_BUTTON(ID_UP, ServicePanel::OnUp)
    EVT_BUTTON(ID_DOWN, ServicePanel::OnDown)
    EVT_BUTTON(ID_CLEAR, ServicePanel::OnClear)
    EVT_LIST_ITEM_ACTIVATED(ID_LIST, ServicePanel::OnListActivated)
    EVT_LIST_ITEM_SELECTED(ID_LIST, ServicePanel::OnItemSelected)
    EVT_KEY_DOWN(ServicePanel::OnKeyDown)
END_EVENT_TABLE()


ServicePanel::ServicePanel(wxWindow *parent, Database &db)
    : wxPanel(parent, wxID_ANY), m_db(db)
{
    auto *root = new wxBoxSizer(wxVERTICAL);

    auto *head = new wxBoxSizer(wxHORIZONTAL);
    m_playlists = new wxComboBox(this, ID_PLAYLISTS, wxString(), wxDefaultPosition,
                                 wxDefaultSize, 0, nullptr, wxCB_READONLY);
    m_playlists->SetToolTip(L"Culto activo (se guarda automáticamente)");
    auto *newL = new wxBitmapButton(this, ID_NEWLIST, Ico::Get("add", 18), wxDefaultPosition,
                                    wxDefaultSize, wxBU_AUTODRAW);
    newL->SetToolTip("Nuevo culto");
    auto *delL = new wxBitmapButton(this, ID_DELLIST, Ico::Get("del", 18), wxDefaultPosition,
                                    wxDefaultSize, wxBU_AUTODRAW);
    delL->SetToolTip("Eliminar culto");
    head->Add(m_playlists, 1, wxALIGN_CENTER_VERTICAL | wxRIGHT, 4);
    head->Add(newL, 0, wxRIGHT, 2);
    head->Add(delL, 0);
    root->Add(head, 0, wxEXPAND | wxBOTTOM, 6);

    m_list = new wxListCtrl(this, ID_LIST, wxDefaultPosition, wxDefaultSize,
                            wxLC_REPORT | wxLC_SINGLE_SEL | wxBORDER_THEME);
    m_list->InsertColumn(0, "#", wxLIST_FORMAT_RIGHT, 34);
    m_list->InsertColumn(1, "Tipo", wxLIST_FORMAT_LEFT, 74);
    m_list->InsertColumn(2, L"Título", wxLIST_FORMAT_LEFT, 300);
    root->Add(m_list, 1, wxEXPAND);

    auto *btns = new wxBoxSizer(wxHORIZONTAL);
    auto mkIco = [&](int id, const wxString &icon, const wxString &tip) {
        auto *b = new wxBitmapButton(this, id, Ico::Get(icon, 18), wxDefaultPosition,
                                     wxDefaultSize, wxBU_AUTODRAW);
        b->SetToolTip(tip);
        return b;
    };
    btns->Add(new wxButton(this, ID_ADDSONG, L"Canción"));
    btns->Add(new wxButton(this, ID_ADDNOTICE, "Aviso"));
    btns->Add(mkIco(ID_UP, "up", "Subir"));
    btns->Add(mkIco(ID_DOWN, "down", "Bajar"));
    btns->Add(mkIco(ID_REMOVE, "del", "Quitar"));
    btns->AddStretchSpacer();
    m_count = new wxStaticText(this, wxID_ANY, L"0 ítems");
    m_count->SetForegroundColour(wxColour(120, 130, 148));
    btns->Add(m_count, 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    btns->Add(mkIco(ID_CLEAR, "clear", "Vaciar el culto"));
    root->Add(btns, 0, wxEXPAND | wxTOP, 6);

    SetSizer(root);
    RefreshPlaylists();
}

// ---------------------------------------------------------------------------
// Cultos
// ---------------------------------------------------------------------------
void ServicePanel::RefreshPlaylists(int selectId)
{
    m_playlists->Clear();
    auto lists = m_db.Playlists();
    int idx = 0, sel = 0;
    for (auto &pl : lists) {
        m_playlists->Append(pl.second);
        if (pl.first == selectId)
            sel = idx;
        ++idx;
    }
    if (m_playlists->GetCount() == 0) {
        const int id = m_db.CreatePlaylist("Culto nuevo");
        m_playlists->Append("Culto nuevo");
        m_playlists->SetSelection(0);
        m_playlistId = id;
    } else {
        m_playlists->SetSelection(sel);
        auto lists2 = m_db.Playlists();
        m_playlistId = lists2[sel].first;
    }
    m_items = m_db.PlaylistItems(m_playlistId);
    RebuildList();
}

void ServicePanel::SetCurrentPlaylist(int id)
{
    m_playlistId = id;
    m_items = m_db.PlaylistItems(id);
    // sincronizar combo
    auto lists = m_db.Playlists();
    int idx = 0;
    for (auto &pl : lists) {
        if (pl.first == id) {
            m_playlists->SetSelection(idx);
            break;
        }
        ++idx;
    }
    RebuildList();
}

void ServicePanel::SetItems(const std::vector<ServiceItem> &items, bool selectFirst)
{
    m_items = items;
    if (m_playlistId > 0)
        m_db.ReplaceItems(m_playlistId, items);
    RebuildList();
    if (selectFirst && !m_items.empty())
        SelectRow(0);
}

std::vector<ServiceItem> ServicePanel::GetItems() const
{
    return m_items;
}

int ServicePanel::SelectedIndex() const
{
    return m_list->GetNextItem(-1, wxLIST_NEXT_ALL, wxLIST_STATE_SELECTED);
}

void ServicePanel::SelectRow(int row)
{
    if (row >= 0 && row < (int)m_items.size())
        m_list->SetItemState(row, wxLIST_STATE_SELECTED, wxLIST_STATE_SELECTED);
}

void ServicePanel::RebuildList()
{
    m_list->Freeze();
    m_list->DeleteAllItems();
    long i = 0;
    for (const ServiceItem &it : m_items) {
        m_list->InsertItem(i, wxString::Format("%d", i + 1));
        const wxString kind = (it.kind == ServiceItem::Song) ? L"Canción"
                              : (it.kind == ServiceItem::Bible) ? L"Biblia"
                              : (it.kind == ServiceItem::Image) ? L"Imagen"
                              : (it.kind == ServiceItem::Video) ? L"Video" : L"Aviso";
        m_list->SetItem(i, 1, kind);
        m_list->SetItem(i, 2, it.label);
        ++i;
    }
    m_list->Thaw();
    m_count->SetLabel(wxString::Format(L"%d ítem%s", (int)m_items.size(),
                                       m_items.size() == 1 ? "" : "s"));
}

// ---------------------------------------------------------------------------
// Edicion
// ---------------------------------------------------------------------------
void ServicePanel::NotifyChanged(int goLiveRow)
{
    if (m_playlistId > 0)
        m_db.ReplaceItems(m_playlistId, m_items);
    RebuildList();
    QueueEventToFrame(EVT_UI_SERVICE_CHANGED, goLiveRow);
}

void ServicePanel::MoveRow(int delta)
{
    const int idx = SelectedIndex();
    if (idx < 0)
        return;
    const int dst = idx + delta;
    if (dst < 0 || dst >= (int)m_items.size())
        return;
    std::swap(m_items[idx], m_items[dst]);
    NotifyChanged();
    SelectRow(dst);
}

void ServicePanel::OnPlaylistChanged(wxCommandEvent &)
{
    const int idx = m_playlists->GetSelection();
    if (idx == wxNOT_FOUND)
        return;
    auto lists = m_db.Playlists();
    if (idx >= (int)lists.size())
        return;
    m_playlistId = lists[idx].first;
    m_items = m_db.PlaylistItems(m_playlistId);
    RebuildList();
}

void ServicePanel::OnNewPlaylist(wxCommandEvent &)
{
    wxTextEntryDialog dlg(this, "Nombre del culto:", "Nuevo culto",
                          wxString::Format("Culto %s", wxDateTime::Now().Format("%d/%m")));
    if (dlg.ShowModal() != wxID_OK)
        return;
    const wxString name = dlg.GetValue().Trim(true).Trim(false);
    if (name.empty())
        return;
    const int id = m_db.CreatePlaylist(name);
    RefreshPlaylists(id);
}

void ServicePanel::OnDelPlaylist(wxCommandEvent &)
{
    if (m_playlistId <= 0)
        return;
    if (wxMessageBox(L"¿Eliminar el culto activo?", "Eliminar culto",
                     wxYES_NO | wxICON_WARNING) != wxYES)
        return;
    m_db.DeletePlaylist(m_playlistId);
    m_playlistId = 0;
    RefreshPlaylists();
}

void ServicePanel::OnAddSong(wxCommandEvent &)
{
    // Selector de canciones rapido (busqueda + doble clic)
    static bool dialogOpen = false;
    if (dialogOpen)
        return;
    dialogOpen = true;
    wxDialog dlg(this, wxID_ANY, L"Añadir canción al culto", wxDefaultPosition,
                 wxSize(520, 460), wxDEFAULT_DIALOG_STYLE | wxRESIZE_BORDER);
    auto *root = new wxBoxSizer(wxVERTICAL);
    auto *search = new wxTextCtrl(&dlg, wxID_ANY);
    search->SetHint(L"Buscar canción…");
    auto *list = new wxListCtrl(&dlg, wxID_ANY, wxDefaultPosition, wxDefaultSize,
                                wxLC_REPORT | wxLC_SINGLE_SEL | wxBORDER_THEME);
    list->InsertColumn(0, L"Título", wxLIST_FORMAT_LEFT, 240);
    list->InsertColumn(1, "Autor", wxLIST_FORMAT_LEFT, 180);
    root->Add(search, 0, wxEXPAND | wxALL, 8);
    root->Add(list, 1, wxEXPAND | wxLEFT | wxRIGHT, 8);
    auto *btns = new wxStdDialogButtonSizer();
    auto *ok = new wxButton(&dlg, wxID_OK, L"Añadir");
    ok->SetDefault();
    btns->AddButton(ok);
    btns->AddButton(new wxButton(&dlg, wxID_CANCEL, "Cancelar"));
    btns->Realize();
    root->Add(btns, 0, wxALIGN_RIGHT | wxALL, 8);
    dlg.SetSizer(root);

    std::vector<Song> rows;
    auto reload = [&]() {
        Database::SearchFilter f;
        f.q = search->GetValue().Trim().Trim(false);
        rows = m_db.SearchSongs(f, 200);
        list->Freeze();
        list->DeleteAllItems();
        long i = 0;
        for (const Song &s : rows) {
            list->InsertItem(i, s.title);
            list->SetItem(i, 1, s.artist);
            list->SetItemPtrData(i, (wxUIntPtr)s.id);
            ++i;
        }
        list->Thaw();
    };
    search->Bind(wxEVT_TEXT, [&](wxCommandEvent &) { reload(); });
    list->Bind(wxEVT_LIST_ITEM_ACTIVATED, [&](wxListEvent &) { dlg.EndModal(wxID_OK); });
    reload();

    if (dlg.ShowModal() == wxID_OK) {
        const long sel = list->GetNextItem(-1, wxLIST_NEXT_ALL, wxLIST_STATE_SELECTED);
        if (sel >= 0) {
            const int songId = (int)(intptr_t)list->GetItemData(sel);
            const Song s = m_db.SongById(songId);
            ServiceItem it;
            it.kind = ServiceItem::Song;
            it.refId = s.id;
            it.label = s.title;
            m_items.push_back(it);
            NotifyChanged();
        }
    }
    dialogOpen = false;
}

void ServicePanel::OnAddNotice(wxCommandEvent &)
{
    wxTextEntryDialog dlg(this, "Texto del aviso:", L"Añadir aviso");
    if (dlg.ShowModal() != wxID_OK)
        return;
    const wxString t = dlg.GetValue().Trim(true).Trim(false);
    if (t.empty())
        return;
    ServiceItem it;
    it.kind = ServiceItem::Aviso;
    it.label = t.Left(60);
    it.payload = t;
    m_items.push_back(it);
    NotifyChanged();
}

void ServicePanel::OnRemove(wxCommandEvent &)
{
    const int idx = SelectedIndex();
    if (idx < 0)
        return;
    m_items.erase(m_items.begin() + idx);
    NotifyChanged();
    if (idx < (int)m_items.size())
        SelectRow(idx);
}

void ServicePanel::OnUp(wxCommandEvent &)   { MoveRow(-1); }
void ServicePanel::OnDown(wxCommandEvent &) { MoveRow(+1); }

void ServicePanel::OnClear(wxCommandEvent &)
{
    if (m_items.empty())
        return;
    if (wxMessageBox(L"¿Vaciar el culto activo?", "Vaciar",
                     wxYES_NO | wxICON_QUESTION) != wxYES)
        return;
    m_items.clear();
    NotifyChanged();
}

void ServicePanel::OnListActivated(wxListEvent &e)
{
    QueueEventToFrame(EVT_UI_SERVICE_GOLIVE, (int)e.GetIndex());
}

void ServicePanel::OnItemSelected(wxListEvent &) {}

void ServicePanel::OnKeyDown(wxKeyEvent &e)
{
    if (e.GetKeyCode() == WXK_DELETE && SelectedIndex() >= 0) {
        wxCommandEvent ev;
        OnRemove(ev);
        return;
    }
    if (e.GetKeyCode() == WXK_UP && e.GetModifiers() == wxMOD_ALT) { MoveRow(-1); return; }
    if (e.GetKeyCode() == WXK_DOWN && e.GetModifiers() == wxMOD_ALT) { MoveRow(+1); return; }
    e.Skip();
}

void ServicePanel::QueueEventToFrame(wxEventType type, int id, const wxString &str)
{
    wxTopLevelWindow *top = wxDynamicCast(wxGetTopLevelParent(this), wxTopLevelWindow);
    if (!top)
        return;
    wxCommandEvent ev(type, id);
    ev.SetInt(id);
    ev.SetString(str);
    top->GetEventHandler()->QueueEvent(ev.Clone());
}
