// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MainFrame.h : Ventana principal — toolbar de control en vivo, biblioteca
//  (pestañas), culto, previsualización dual, salida fullscreen, servidor
//  remoto, atajos y motor de proyección (en vivo / negro / limpiar / logo,
//  lower third, transposición).
// ============================================================================
#ifndef LUMINA_MAINFRAME_H
#define LUMINA_MAINFRAME_H

#include "../core/Types.h"

#include <wx/frame.h>
#include <wx/timer.h>

#include <memory>
#include <vector>

#include "OutputFrame.h"   // OutputFrame::Mode (necesario en firmas)

class Database;
class WebServer;
class PreviewPanel;
class SongsPanel;
class BiblePanel;
class MediaPanel;
class ThemesPanel;
class ServicePanel;
class wxNotebook;
class wxSplitterWindow;
class wxComboBox;
class wxStaticText;

class MainFrame : public wxFrame
{
public:
    MainFrame(Database &db);

    // Motor en vivo
    void GoLiveSong(int songId);
    void GoLiveRef(int book, int chapter, int vFrom, int vTo);
    void GoLiveMedia(const wxString &path);
    void GoLiveNotice(const wxString &text);
    void GoLiveServiceItem(int row);

    void NextSlide();
    void PrevSlide();
    void SetOutputMode(OutputFrame::Mode mode);
    void ApplyTheme(int themeId);
    void RefreshScreenCombo();

    // Estado para el servidor remoto
    wxString BuildStateJson() const;
    wxString BuildLiveText() const;

private:
    // Construccion de UI
    void BuildMenus();
    void BuildToolbar();
    void BuildLayout();
    void BuildStatusBar();
    void BindPanelEvents();
    void BindGlobalEvents();

    // Slides
    std::vector<Slide> BuildSongSlides(const Song &s);
    std::vector<Slide> BuildRefSlides(int book, int chapter, int vFrom, int vTo);
    void PushLive(std::vector<Slide> slides, const wxString &label);
    void UpdateProjection();
    void UpdatePreviews();
    void UpdateStatusBar();

    // Eventos UI
    void OnNext(wxCommandEvent &e);
    void OnPrev(wxCommandEvent &e);
    void OnBlack(wxCommandEvent &e);
    void OnClear(wxCommandEvent &e);
    void OnLogo(wxCommandEvent &e);
    void OnScreenSel(wxCommandEvent &e);
    void OnLowerThird(wxCommandEvent &e);
    void OnTranspose(wxCommandEvent &e);
    void OnServerToggle(wxCommandEvent &e);
    void OnSettings(wxCommandEvent &e);
    void OnAbout(wxCommandEvent &e);
    void OnShortcuts(wxCommandEvent &e);
    void OnImportBible(wxCommandEvent &e);
    void OnNewService(wxCommandEvent &e);
    void OnSaveService(wxCommandEvent &e);
    void OnOpenService(wxCommandEvent &e);
    void OnExportCsv(wxCommandEvent &e);
    void OnPresentationMode(wxCommandEvent &e);
    void OnClose(wxCloseEvent &e);
    void OnSearchFocus(wxCommandEvent &e);

    // Servidor remoto
    void StartServerIfNeeded();
    void StartServer();
    void StopServer();

    // Importacion de Biblia (JSON)
    void ImportBibleFile(const wxString &path);

    // Eventos de paneles
    void OnPanelGoLiveSong(wxCommandEvent &e);
    void OnPanelAddSong(wxCommandEvent &e);
    void OnPanelGoLiveRef(wxCommandEvent &e);
    void OnPanelAddRef(wxCommandEvent &e);
    void OnPanelGoLiveMedia(wxCommandEvent &e);
    void OnPanelAddMedia(wxCommandEvent &e);
    void OnPanelApplyTheme(wxCommandEvent &e);
    void OnPanelDataChanged(wxCommandEvent &e);
    void OnPanelServiceGoLive(wxCommandEvent &e);
    void OnPanelServiceChanged(wxCommandEvent &e);

    // Eventos de red/proyector
    void OnRemoteCommand(wxCommandEvent &e);
    void OnOutputCommand(wxCommandEvent &e);

    // Quick verse (F9)
    void QuickVerse();

    Database &m_db;
    WebServer *m_server = nullptr;
    std::unique_ptr<OutputFrame> m_output;

    wxNotebook *m_notebook = nullptr;
    SongsPanel *m_songs = nullptr;
    BiblePanel *m_bible = nullptr;
    MediaPanel *m_media = nullptr;
    ThemesPanel *m_themes = nullptr;
    ServicePanel *m_service = nullptr;
    PreviewPanel *m_preview = nullptr;
    wxSplitterWindow *m_mainSplit = nullptr;
    wxSplitterWindow *m_rightSplit = nullptr;
    wxComboBox *m_screenCombo = nullptr;
    wxStaticText *m_transposeLabel = nullptr;

    // Estado en vivo
    std::vector<Slide> m_liveSlides;
    int m_liveIndex = -1;
    wxString m_liveLabel;
    Theme m_liveTheme;
    int m_liveThemeId = 0;
    int m_transpose = 0;
    bool m_lowerOn = false;
    wxString m_lowerTitle, m_lowerText;
    int m_presentationMode = 0;

    wxDECLARE_EVENT_TABLE();
};

#endif // LUMINA_MAINFRAME_H
