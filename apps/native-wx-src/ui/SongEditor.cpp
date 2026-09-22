// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  SongEditor.cpp : implementacion (ver .h)
// ============================================================================
#include "SongEditor.h"
#include "../core/Database.h"
#include "../core/Lyrics.h"

#include <wx/button.h>
#include <wx/font.h>
#include <wx/msgdlg.h>
#include <wx/listctrl.h>
#include <wx/spinctrl.h>
#include <wx/sizer.h>
#include <wx/tokenzr.h>
#include <wx/stattext.h>
#include <wx/textctrl.h>
#include <wx/valtext.h>

namespace {
enum { ID_LYRICS = wxID_HIGHEST + 300, ID_TIMER };
}

BEGIN_EVENT_TABLE(SongEditor, wxDialog)
    EVT_TEXT(ID_LYRICS, SongEditor::OnLyricsChange)
    EVT_TIMER(ID_TIMER, SongEditor::OnPreviewTimer)
    EVT_BUTTON(wxID_OK, SongEditor::OnOk)
END_EVENT_TABLE()

namespace {
const char *SAMPLE =
    "Bienvenido a Fusion-HP\n"
    "Escribe aqui tu cancion\n\n"
    "[Verso 1]\n"
    "Grande es el Senor\n"
    "Digno de alabar\n\n"
    "[Coro]\n"
    "Santo, santo, santo\n\n"
    "[Verso 2]\n"
    "Toda lengua confesara\n";
}

SongEditor::SongEditor(wxWindow *parent, Database &db, int songId)
    : wxDialog(parent, wxID_ANY, L"Canción", wxDefaultPosition, wxSize(880, 640),
               wxDEFAULT_DIALOG_STYLE | wxRESIZE_BORDER),
      m_db(db), m_songId(songId)
{
    m_previewTimer.SetOwner(this, ID_TIMER);
    if (songId > 0) {
        m_song = db.SongById(songId);
        SetTitle(L"Canción — " + m_song.title);
    }

    auto *root = new wxBoxSizer(wxVERTICAL);

    // --- Metadatos -----------------------------------------------------------
    auto *meta = new wxFlexGridSizer(4, 8, 8);
    meta->AddGrowableCol(1, 1);
    auto lbl = [](const wxString &t) {
        auto *s = new wxStaticText(nullptr, wxID_ANY, t + ":");
        return s;
    };
    m_title = new wxTextCtrl(this, wxID_ANY, m_song.title);
    m_artist = new wxTextCtrl(this, wxID_ANY, m_song.artist);
    meta->Add(lbl(L"Título"), 0, wxALIGN_CENTER_VERTICAL);
    meta->Add(m_title, 1, wxEXPAND);
    meta->Add(lbl("Autor"), 0, wxALIGN_CENTER_VERTICAL);
    meta->Add(m_artist, 1, wxEXPAND);
    m_key = new wxTextCtrl(this, wxID_ANY, m_song.key, wxDefaultPosition, wxSize(70, -1));
    m_key->SetHint("Do");
    m_bpm = new wxSpinCtrl(this, wxID_ANY, wxString(), wxDefaultPosition, wxSize(80, -1),
                           wxSP_ARROW_KEYS, 0, 300, m_song.bpm > 0 ? m_song.bpm : 0);
    m_tags = new wxTextCtrl(this, wxID_ANY, wxString());
    m_tags->SetHint(L"etiquetas separadas por coma: lento, entrada, navidad…");
    meta->Add(lbl("Tono"), 0, wxALIGN_CENTER_VERTICAL);
    meta->Add(m_key, 1, wxEXPAND);
    meta->Add(lbl("BPM"), 0, wxALIGN_CENTER_VERTICAL);
    meta->Add(m_bpm, 1, wxEXPAND);
    meta->Add(lbl("Etiquetas"), 0, wxALIGN_CENTER_VERTICAL);
    meta->Add(m_tags, 1, wxEXPAND);
    root->Add(meta, 0, wxEXPAND | wxLEFT | wxRIGHT | wxTOP, 12);

    // --- Letra + preview -------------------------------------------------------
    auto *split = new wxBoxSizer(wxHORIZONTAL);
    auto *leftCol = new wxBoxSizer(wxVERTICAL);
    leftCol->Add(new wxStaticText(this, wxID_ANY,
                 L"Letra  ([Verso 1], [Coro], [Puente]… — líneas de acordes sobre el texto)"),
                 0, wxBOTTOM, 4);
    m_lyrics = new wxTextCtrl(this, ID_LYRICS, wxString(), wxDefaultPosition, wxDefaultSize,
                              wxTE_MULTILINE | wxTE_RICH2 | wxHSCROLL);
    m_lyrics->SetFont(wxFont(10, wxFONTFAMILY_TELETYPE, wxFONTSTYLE_NORMAL, wxFONTWEIGHT_NORMAL));
    leftCol->Add(m_lyrics, 1, wxEXPAND);

    auto *rightCol = new wxBoxSizer(wxVERTICAL);
    rightCol->Add(new wxStaticText(this, wxID_ANY, "Reparto en slides (en vivo):"), 0, wxBOTTOM, 4);
    m_preview = new wxListCtrl(this, wxID_ANY, wxDefaultPosition, wxDefaultSize,
                               wxLC_REPORT | wxLC_NO_HEADER | wxBORDER_THEME);
    m_preview->InsertColumn(0, "Slide", wxLIST_FORMAT_LEFT, 340);
    rightCol->Add(m_preview, 1, wxEXPAND);

    split->Add(leftCol, 3, wxEXPAND | wxRIGHT, 10);
    split->Add(rightCol, 2, wxEXPAND);
    root->Add(split, 1, wxEXPAND | wxALL, 12);

    // --- Botones ---------------------------------------------------------------
    auto *btns = new wxStdDialogButtonSizer();
    btns->AddButton(new wxButton(this, wxID_OK, "Guardar"));
    btns->AddButton(new wxButton(this, wxID_CANCEL, "Cancelar"));
    btns->Realize();
    root->Add(btns, 0, wxALIGN_RIGHT | wxLEFT | wxRIGHT | wxBOTTOM, 12);

    SetSizer(root);
    m_lyrics->ChangeValue(m_song.lyrics.empty() ? wxString(SAMPLE) : m_song.lyrics);
    if (songId > 0)
        m_tags->ChangeValue(GetTagsCsv(songId));

    m_title->SetFocus();
    RebuildPreview();
}

wxString SongEditor::GetTagsCsv(int songId)
{
    const std::vector<wxString> tags = m_db.SongTags(songId);
    wxString out;
    for (size_t i = 0; i < tags.size(); ++i) {
        if (i) out += ", ";
        out += tags[i];
    }
    return out;
}

void SongEditor::OnLyricsChange(wxCommandEvent &)
{
    m_previewTimer.Start(350, wxTIMER_ONE_SHOT);
}

void SongEditor::OnPreviewTimer(wxTimerEvent &)
{
    RebuildPreview();
}

void SongEditor::RebuildPreview()
{
    Song tmp;
    tmp.title = m_title->GetValue().Trim().Trim(false);
    tmp.artist = m_artist->GetValue().Trim().Trim(false);
    tmp.key = m_key->GetValue().Trim().Trim(false);
    tmp.bpm = m_bpm->GetValue();
    tmp.lyrics = m_lyrics->GetValue();

    Lyrics::BuildOptions opt;
    opt.titleSlide = true;
    opt.maxLinesPerSlide = 4;
    opt.stripChords = true;
    const std::vector<Slide> slides = Lyrics::BuildSlides(tmp, opt);

    m_preview->Freeze();
    m_preview->DeleteAllItems();
    long i = 0;
    for (const Slide &s : slides) {
        wxString label;
        if (s.kind == Slide::Title)
            label = wxString::Format(L"★ %s", tmp.title);
        else {
            label = wxString::Format("[%s]  ", s.refLabel);
            label += Lyrics::PlainText(s);
            label.Replace("\n", " / ");
            if (label.Length() > 90)
                label = label.Left(90) + L"…";
        }
        m_preview->InsertItem(i, label);
        ++i;
    }
    m_preview->Thaw();
}

void SongEditor::OnOk(wxCommandEvent &)
{
    const wxString title = m_title->GetValue().Trim().Trim(false);
    if (title.empty()) {
        wxMessageDialog msg(this, L"El título es obligatorio.", L"Canción",
                            wxOK | wxICON_INFORMATION);
        msg.ShowModal();
        m_title->SetFocus();
        return;
    }
    m_song.title = title;
    m_song.artist = m_artist->GetValue().Trim().Trim(false);
    m_song.key = m_key->GetValue().Trim().Trim(false);
    m_song.bpm = m_bpm->GetValue();
    m_song.lyrics = m_lyrics->GetValue();

    // Tags
    std::vector<wxString> tags;
    wxStringTokenizer toks(m_tags->GetValue(), ",;");
    while (toks.HasMoreTokens()) {
        wxString t = toks.GetNextToken().Trim(true).Trim(false);
        if (!t.empty())
            tags.push_back(t);
    }

    if (m_songId > 0) {
        m_song.id = m_songId;
        m_db.UpdateSong(m_song);
    } else {
        m_songId = m_db.AddSong(m_song);
    }
    m_db.SetSongTags(m_songId, tags);
    EndModal(wxID_OK);
}
