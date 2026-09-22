// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MediaPanel.cpp : implementacion (ver .h)
// ============================================================================
#include "MediaPanel.h"
#include "AppEvents.h"
#include "../core/Database.h"

#include <wx/button.h>
#include <wx/filedlg.h>
#include <wx/filename.h>
#include <wx/listctrl.h>
#include <wx/menu.h>
#include <wx/msgdlg.h>
#include <wx/sizer.h>
#include <wx/stattext.h>
#include <wx/textdlg.h>
#include <wx/tokenzr.h>
#include <wx/toplevel.h>

namespace {
enum {
    ID_ADD = wxID_HIGHEST + 500,
    ID_DEL,
    ID_LIVE,
    ID_ADDSVC,
    ID_TAGS,
    ID_LIST,
};
}

BEGIN_EVENT_TABLE(MediaPanel, wxPanel)
    EVT_BUTTON(ID_ADD, MediaPanel::OnAdd)
    EVT_BUTTON(ID_DEL, MediaPanel::OnRemove)
    EVT_BUTTON(ID_LIVE, MediaPanel::OnLive)
    EVT_BUTTON(ID_ADDSVC, MediaPanel::OnAddService)
    EVT_BUTTON(ID_TAGS, MediaPanel::OnTags)
    EVT_LIST_ITEM_ACTIVATED(ID_LIST, MediaPanel::OnListActivated)
    EVT_CONTEXT_MENU(MediaPanel::OnContextMenu)
END_EVENT_TABLE()


MediaPanel::MediaPanel(wxWindow *parent, Database &db)
    : wxPanel(parent, wxID_ANY), m_db(db)
{
    auto *root = new wxBoxSizer(wxVERTICAL);

    root->Add(new wxStaticText(this, wxID_ANY,
              L"Fondos y videos — doble clic para proyectar; usa etiquetas para organizar (agua, ocaso…)"),
              0, wxBOTTOM, 6);

    m_list = new wxListCtrl(this, ID_LIST, wxDefaultPosition, wxDefaultSize,
                            wxLC_REPORT | wxLC_SINGLE_SEL | wxBORDER_THEME);
    m_list->InsertColumn(0, "Archivo", wxLIST_FORMAT_LEFT, 330);
    m_list->InsertColumn(1, "Tipo", wxLIST_FORMAT_LEFT, 70);
    m_list->InsertColumn(2, "Etiquetas", wxLIST_FORMAT_LEFT, 200);
    root->Add(m_list, 1, wxEXPAND);

    auto *btnRow = new wxBoxSizer(wxHORIZONTAL);
    auto *addBtn = new wxButton(this, ID_ADD, L"Añadir…");
    btnRow->Add(addBtn);
    btnRow->Add(new wxButton(this, ID_TAGS, L"Etiquetas…"));
    btnRow->Add(new wxButton(this, ID_DEL, "Quitar"));
    btnRow->AddStretchSpacer();
    auto *live = new wxButton(this, ID_LIVE, "En vivo");
    live->SetForegroundColour(wxColour(230, 168, 42));
    btnRow->Add(live, 0, wxRIGHT, 6);
    btnRow->Add(new wxButton(this, ID_ADDSVC, "Al culto"));
    root->Add(btnRow, 0, wxEXPAND | wxTOP, 6);

    SetSizer(root);
    RefreshList();
}

int MediaPanel::DetectKind(const wxString &path)
{
    const wxString ext = wxFileName(path).GetExt().Lower();
    static const wxString videoExt = "mp4|mov|avi|wmv|mkv|webm|mpg|mpeg|m4v";
    return videoExt.Contains(ext) ? 1 : 0;
}

void MediaPanel::RefreshList()
{
    m_rows = m_db.MediaLibrary();
    m_list->Freeze();
    m_list->DeleteAllItems();
    long i = 0;
    for (const MediaRow &r : m_rows) {
        const wxString name = wxFileName(r.path).GetFullName();
        m_list->InsertItem(i, name);
        m_list->SetItem(i, 1, r.kind == 1 ? "Video" : "Imagen");
        const std::vector<wxString> tags = m_db.ResourceTags("media", r.path);
        wxString tagStr;
        for (size_t k = 0; k < tags.size(); ++k) {
            if (k) tagStr += ", ";
            tagStr += tags[k];
        }
        m_list->SetItem(i, 2, tagStr);
        ++i;
    }
    m_list->Thaw();
}

int MediaPanel::SelectedIndex() const
{
    return m_list->GetNextItem(-1, wxLIST_NEXT_ALL, wxLIST_STATE_SELECTED);
}

void MediaPanel::QueueEventToFrame(wxEventType type, int id, const wxString &str)
{
    wxTopLevelWindow *top = wxDynamicCast(wxGetTopLevelParent(this), wxTopLevelWindow);
    if (!top)
        return;
    wxCommandEvent ev(type, id);
    ev.SetInt(id);
    ev.SetString(str);
    top->GetEventHandler()->QueueEvent(ev.Clone());
}

void MediaPanel::OnAdd(wxCommandEvent &)
{
    wxFileDialog dlg(this, L"Añadir medios (imágenes y videos)",
                     wxString(), wxString(),
                     L"Imágenes y videos|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.webp;"
                     "*.mp4;*.mov;*.avi;*.wmv;*.mkv;*.webm;*.mpg;*.mpeg;*.m4v|"
                     L"Imágenes|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.webp|"
                     "Videos|*.mp4;*.mov;*.avi;*.wmv;*.mkv;*.webm;*.mpg;*.mpeg;*.m4v",
                     wxFD_OPEN | wxFD_FILE_MUST_EXIST | wxFD_MULTIPLE);
    if (dlg.ShowModal() != wxID_OK)
        return;
    wxArrayString paths;
    dlg.GetPaths(paths);
    for (const wxString &p : paths)
        m_db.AddMedia(p, DetectKind(p));
    RefreshList();
    QueueEventToFrame(EVT_UI_DATA_CHANGED, 0, "media");
}

void MediaPanel::OnRemove(wxCommandEvent &)
{
    const int idx = SelectedIndex();
    if (idx < 0 || idx >= (int)m_rows.size())
        return;
    if (wxMessageBox(L"¿Quitar de la biblioteca? (el archivo no se borra del disco)",
                     "Quitar medio", wxYES_NO | wxICON_QUESTION) != wxYES)
        return;
    m_db.RemoveMedia(m_rows[idx].path);
    RefreshList();
    QueueEventToFrame(EVT_UI_DATA_CHANGED, 0, "media");
}

void MediaPanel::OnLive(wxCommandEvent &)
{
    const int idx = SelectedIndex();
    if (idx < 0 || idx >= (int)m_rows.size())
        return;
    QueueEventToFrame(EVT_UI_GOLIVE_MEDIA, 0, m_rows[idx].path);
}

void MediaPanel::OnAddService(wxCommandEvent &)
{
    const int idx = SelectedIndex();
    if (idx < 0 || idx >= (int)m_rows.size())
        return;
    QueueEventToFrame(EVT_UI_ADD_MEDIA, 0, m_rows[idx].path);
}

void MediaPanel::OnTags(wxCommandEvent &)
{
    const int idx = SelectedIndex();
    if (idx < 0 || idx >= (int)m_rows.size())
        return;
    const MediaRow &r = m_rows[idx];
    const std::vector<wxString> cur = m_db.ResourceTags("media", r.path);
    wxString csv;
    for (size_t i = 0; i < cur.size(); ++i) {
        if (i) csv += ", ";
        csv += cur[i];
    }
    wxTextEntryDialog dlg(this,
        L"Etiquetas para «" + wxFileName(r.path).GetFullName() + "\" (separadas por coma):",
        "Etiquetas", csv);
    if (dlg.ShowModal() != wxID_OK)
        return;
    std::vector<wxString> tags;
    wxStringTokenizer toks(dlg.GetValue(), ",;");
    while (toks.HasMoreTokens()) {
        wxString t = toks.GetNextToken().Trim(true).Trim(false);
        if (!t.empty())
            tags.push_back(t);
    }
    m_db.SetResourceTags("media", r.path, tags);
    RefreshList();
}

void MediaPanel::OnListActivated(wxListEvent &e)
{
    if (e.GetIndex() >= 0 && e.GetIndex() < (int)m_rows.size())
        QueueEventToFrame(EVT_UI_GOLIVE_MEDIA, 0, m_rows[e.GetIndex()].path);
}

void MediaPanel::OnContextMenu(wxContextMenuEvent &e)
{
    if (SelectedIndex() < 0) {
        e.Skip();
        return;
    }
    wxMenu menu;
    menu.Append(ID_LIVE, "En vivo");
    menu.Append(ID_ADDSVC, L"Añadir al culto");
    menu.AppendSeparator();
    menu.Append(ID_TAGS, L"Etiquetas…");
    menu.Append(ID_DEL, "Quitar");
    PopupMenu(&menu);
}
