// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  SongEditor.h : Editor de canciones con vista previa en vivo del reparto
//  en slides ([Verso]/[Coro], acordes, densidad).
// ============================================================================
#ifndef LUMINA_SONGEDITOR_H
#define LUMINA_SONGEDITOR_H

#include "../core/Types.h"

#include <wx/dialog.h>
#include <wx/timer.h>

class wxTextCtrl;
class wxSpinCtrl;
class wxListCtrl;
class Database;

class SongEditor : public wxDialog
{
public:
    SongEditor(wxWindow *parent, Database &db, int songId);

private:
    void OnLyricsChange(wxCommandEvent &e);
    void OnPreviewTimer(wxTimerEvent &e);
    void OnOk(wxCommandEvent &e);
    void RebuildPreview();
    wxString GetTagsCsv(int songId);

    Database &m_db;
    int m_songId;
    Song m_song;

    wxTextCtrl *m_title;
    wxTextCtrl *m_artist;
    wxTextCtrl *m_key;
    wxSpinCtrl *m_bpm;
    wxTextCtrl *m_tags;
    wxTextCtrl *m_lyrics;
    wxListCtrl *m_preview;
    wxTimer m_previewTimer;

    wxDECLARE_EVENT_TABLE();
};

#endif // LUMINA_SONGEDITOR_H
