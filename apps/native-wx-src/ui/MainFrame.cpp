// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MainFrame.cpp : implementacion de la ventana principal (ver .h)
// ============================================================================
#include "MainFrame.h"
#include "AppEvents.h"
#include "BiblePanel.h"
#include "Icons.h"
#include "MediaPanel.h"
#include "OutputFrame.h"
#include "PreviewPanel.h"
#include "ServicePanel.h"
#include "SettingsDialog.h"
#include "SongsPanel.h"
#include "ThemesPanel.h"
#include "../core/AppPaths.h"
#include "../core/BibleRef.h"
#include "../core/Database.h"
#include "../core/Lyrics.h"
#include "../core/Renderer.h"
#include "../net/WebServer.h"

#include <wx/aboutdlg.h>
#include <wx/bmpbuttn.h>
#include <wx/buffer.h>
#include <wx/button.h>
#include <wx/combobox.h>
#include <wx/tglbtn.h>
#include <wx/tokenzr.h>
#include <wx/dataobj.h>
#include <wx/dateevt.h>
#include <wx/dirdlg.h>
#include <wx/display.h>
#include <wx/filedlg.h>
#include <wx/log.h>
#include <wx/menu.h>
#include <wx/msgdlg.h>
#include <wx/notebook.h>
#include <wx/settings.h>
#include <wx/sizer.h>
#include <wx/splitter.h>
#include <wx/stattext.h>
#include <wx/statusbr.h>
#include <wx/textdlg.h>
#include <wx/toolbar.h>
#include <wx/toplevel.h>
#include <wx/utils.h>

#include <fstream>
#include <sstream>

namespace {
enum {
    ID_LIVE = wxID_HIGHEST + 1000,
    ID_NEXT,
    ID_PREV,
    ID_BLACK,
    ID_CLEAR,
    ID_LOGO,
    ID_LOWER,
    ID_SCREENS,
    ID_TRANS_UP,
    ID_TRANS_DOWN,
    ID_TRANS_RESET,
    ID_SERVERBTN,
    ID_SETTINGS,
    // Menus / aceleradores
    ID_IMPORTBIBLE,
    ID_NEWSVC,
    ID_OPENSVC,
    ID_EXPORTCSV,
    ID_PRESENT,
    ID_SEARCHMENU,
    ID_SHORTCUTS,
    ID_NEWSONG,
    ID_QUICKVERSE,
};
}

BEGIN_EVENT_TABLE(MainFrame, wxFrame)
    // Toolbar (los tools emiten wxEVT_MENU: mismos Bind que los aceleradores)
    EVT_COMBOBOX(ID_SCREENS, MainFrame::OnScreenSel)
    // Paneles
    EVT_COMMAND(wxID_ANY, EVT_UI_GOLIVE_SONG, MainFrame::OnPanelGoLiveSong)
    EVT_COMMAND(wxID_ANY, EVT_UI_ADD_SONG, MainFrame::OnPanelAddSong)
    EVT_COMMAND(wxID_ANY, EVT_UI_GOLIVE_REF, MainFrame::OnPanelGoLiveRef)
    EVT_COMMAND(wxID_ANY, EVT_UI_ADD_REF, MainFrame::OnPanelAddRef)
    EVT_COMMAND(wxID_ANY, EVT_UI_GOLIVE_MEDIA, MainFrame::OnPanelGoLiveMedia)
    EVT_COMMAND(wxID_ANY, EVT_UI_ADD_MEDIA, MainFrame::OnPanelAddMedia)
    EVT_COMMAND(wxID_ANY, EVT_UI_APPLY_THEME, MainFrame::OnPanelApplyTheme)
    EVT_COMMAND(wxID_ANY, EVT_UI_DATA_CHANGED, MainFrame::OnPanelDataChanged)
    EVT_COMMAND(wxID_ANY, EVT_UI_SERVICE_GOLIVE, MainFrame::OnPanelServiceGoLive)
    EVT_COMMAND(wxID_ANY, EVT_UI_SERVICE_CHANGED, MainFrame::OnPanelServiceChanged)
    // Red / proyector
    EVT_COMMAND(wxID_ANY, EVT_REMOTE_COMMAND, MainFrame::OnRemoteCommand)
    EVT_COMMAND(wxID_ANY, EVT_OUTPUT_COMMAND, MainFrame::OnOutputCommand)
    // Ventana
    EVT_CLOSE(MainFrame::OnClose)
    EVT_MENU(wxID_ABOUT, MainFrame::OnAbout)
END_EVENT_TABLE()

namespace {
// JSON escape simple para /api/state
inline wxString JsonEsc(const wxString &s)
{
    wxString out = s;
    out.Replace("\\", "\\\\");
    out.Replace("\"", "\\\"");
    out.Replace("\n", "\\n");
    out.Replace("\r", "");
    out.Replace("\t", "\\t");
    return out;
}
}

MainFrame::MainFrame(Database &db)
    : wxFrame(nullptr, wxID_ANY, "LuminaPresentation Suite", wxDefaultPosition,
              wxSize(1280, 800), wxDEFAULT_FRAME_STYLE),
      m_db(db)
{
    SetMinSize(wxSize(1024, 640));

    // Tema en vivo = tema predeterminado
    const int defId = m_db.DefaultThemeId();
    m_liveTheme = defId > 0 ? m_db.ThemeById(defId) : Theme::DefaultTheme();
    m_liveThemeId = m_liveTheme.id;

    BuildMenus();
    BuildToolbar();
    BuildLayout();
    BuildStatusBar();
    BindPanelEvents();

    CreateStatusBar(3);
    const int widths[3] = { -3, 250, 170 };
    SetStatusWidths(3, widths);

    // Ventana de salida (oculta hasta usarse; se muestra al proyectar)
    m_output.reset(new OutputFrame(this));
    m_output->SetCommandSink(this);
    RefreshScreenCombo();

    BindGlobalEvents();
    StartServerIfNeeded();

    // Restaurar geometria
    SetSize(AppPaths::CfgInt("win_w", 1280), AppPaths::CfgInt("win_h", 800));
    Maximize(AppPaths::CfgInt("win_max", 0) != 0);

    CallAfter([this] { UpdateStatusBar(); });
}

// ---------------------------------------------------------------------------
// Menus
// ---------------------------------------------------------------------------
void MainFrame::BuildMenus()
{
    wxMenu *file = new wxMenu();
    file->Append(ID_SETTINGS, L"Ajustes…", L"Configuración de salida, servidor y datos");
    file->AppendSeparator();
    file->Append(ID_IMPORTBIBLE, L"Importar Biblia (JSON)…", L"Añadir versiones de la Biblia");
    file->AppendSeparator();
    file->Append(wxID_SAVE, "Guardar culto…\tCtrl+S", "Guardar el culto activo en un archivo");
    file->Append(ID_OPENSVC, "Abrir culto…\tCtrl+O", "Cargar un culto desde archivo");
    file->Append(ID_EXPORTCSV, L"Exportar culto a CSV…", "Lista del culto para imprimir/compartir");
    file->AppendSeparator();
    file->Append(wxID_EXIT, "Salir\tAlt+F4");
    Bind(wxEVT_MENU, [this](wxCommandEvent &) { Close(true); }, wxID_EXIT, wxID_EXIT);
    Bind(wxEVT_MENU, &MainFrame::OnSaveService, this, wxID_SAVE, wxID_SAVE);
    Bind(wxEVT_MENU, [this](wxCommandEvent &e) { m_service->OnNewPlaylist(e); },
         ID_NEWSVC, ID_NEWSVC);
    Bind(wxEVT_MENU, &MainFrame::OnOpenService, this, ID_OPENSVC, ID_OPENSVC);
    Bind(wxEVT_MENU, &MainFrame::OnExportCsv, this, ID_EXPORTCSV, ID_EXPORTCSV);
    Bind(wxEVT_MENU, &MainFrame::OnImportBible, this, ID_IMPORTBIBLE, ID_IMPORTBIBLE);
    Bind(wxEVT_MENU, &MainFrame::OnSettings, this, ID_SETTINGS, ID_SETTINGS);

    wxMenu *view = new wxMenu();
    view->Append(ID_PRESENT, "Modo presentación\tF11",
                 L"Maximiza la consola de proyección (oculta la biblioteca)");
    view->Append(ID_SEARCHMENU, "Buscar canciones\tCtrl+F");

    wxMenu *help = new wxMenu();
    help->Append(ID_SHORTCUTS, L"Atajos de teclado…");
    help->AppendSeparator();
    help->Append(wxID_ABOUT, L"Acerca de…");

    wxMenuBar *mb = new wxMenuBar();
    mb->Append(file, "&Archivo");
    mb->Append(view, "&Vista");
    mb->Append(help, "A&yuda");
    SetMenuBar(mb);

    Bind(wxEVT_MENU, &MainFrame::OnPresentationMode, this, ID_PRESENT, ID_PRESENT);
    Bind(wxEVT_MENU, &MainFrame::OnSearchFocus, this, ID_SEARCHMENU, ID_SEARCHMENU);
    Bind(wxEVT_MENU, &MainFrame::OnShortcuts, this, ID_SHORTCUTS, ID_SHORTCUTS);
}

// ---------------------------------------------------------------------------
// Toolbar
// ---------------------------------------------------------------------------
void MainFrame::BuildToolbar()
{
    wxToolBar *tb = CreateToolBar(wxTB_HORIZONTAL | wxTB_FLAT | wxTB_NODIVIDER);
    tb->SetToolPacking(4);

    auto *live = new wxButton(tb, ID_LIVE, " EN VIVO ", wxDefaultPosition, wxDefaultSize);
    live->SetBackgroundColour(wxColour(230, 168, 42));
    live->SetForegroundColour(wxColour(20, 24, 32));
    live->SetFont(GetFont().Bold());
    live->SetToolTip(L"Proyectar la canción/pasaje seleccionado (F5)");
    tb->AddControl(live);

    tb->AddTool(ID_PREV, "Anterior", Ico::Get("prev", 24), "Slide anterior (Page Up)");
    tb->AddTool(ID_NEXT, "Siguiente", Ico::Get("next", 24), "Slide siguiente (Espacio/F5)");
    tb->AddSeparator();
    tb->AddTool(ID_BLACK, "Negro", Ico::Get("black", 24), "Pantalla en negro (F6)");
    tb->AddTool(ID_CLEAR, "Limpiar", Ico::Get("clear", 24), "Solo el fondo, sin texto (F7)");
    tb->AddTool(ID_LOGO, "Logo", Ico::Get("logo", 24), "Logo de la iglesia (F8)");
    tb->AddTool(ID_LOWER, "Lower Third", Ico::Get("alert", 24),
                L"Banda inferior con título/texto (modo Lower Third)");
    tb->AddSeparator();

    tb->AddControl(new wxStaticText(tb, wxID_ANY, "  Salida:"));
    m_screenCombo = new wxComboBox(tb, ID_SCREENS, wxString(), wxDefaultPosition,
                                   wxDefaultSize, 0, nullptr, wxCB_READONLY);
    m_screenCombo->SetToolTip("Pantalla del proyector");
    tb->AddControl(m_screenCombo);

    tb->AddSeparator();
    tb->AddTool(ID_TRANS_DOWN, "Bajar tono", Ico::Get("down", 20), "Transponer -1 semitono");
    m_transposeLabel = new wxStaticText(tb, wxID_ANY, " 0 ");
    tb->AddControl(m_transposeLabel);
    tb->AddTool(ID_TRANS_UP, "Subir tono", Ico::Get("up", 20), "Transponer +1 semitono");
    tb->AddTool(ID_TRANS_RESET, "Tono original", Ico::Get("clear", 20), "Restaurar tono original");

    tb->AddSeparator();
    auto *serverBtn = new wxToggleButton(tb, ID_SERVERBTN, " Remoto ", wxDefaultPosition,
                                         wxDefaultSize);
    serverBtn->SetToolTip(L"Servidor de control remoto (móvil/OBS)");
    tb->AddControl(serverBtn);

    auto *settingsBtn = new wxBitmapButton(tb, ID_SETTINGS, Ico::Get("gear", 22),
                                           wxDefaultPosition, wxDefaultSize, wxBU_AUTODRAW);
    settingsBtn->SetToolTip("Ajustes");
    tb->AddControl(settingsBtn);

    tb->Realize();
}

// ---------------------------------------------------------------------------
// Layout
// ---------------------------------------------------------------------------
void MainFrame::BuildLayout()
{
    m_mainSplit = new wxSplitterWindow(this, wxID_ANY, wxDefaultPosition, wxDefaultSize,
                                       wxSP_LIVE_UPDATE | wxSP_3DSASH);
    m_mainSplit->SetMinimumPaneSize(280);
    m_mainSplit->SetSashGravity(0.5);

    // Biblioteca con pestañas
    m_notebook = new wxNotebook(m_mainSplit, wxID_ANY);
    m_songs = new SongsPanel(m_notebook, m_db);
    m_bible = new BiblePanel(m_notebook, m_db);
    m_media = new MediaPanel(m_notebook, m_db);
    m_themes = new ThemesPanel(m_notebook, m_db);
    m_notebook->AddPage(m_songs, "Canciones");
    m_notebook->AddPage(m_bible, "Biblia");
    m_notebook->AddPage(m_media, "Medios");
    m_notebook->AddPage(m_themes, "Temas");

    // Lado derecho: culto + preview
    m_rightSplit = new wxSplitterWindow(m_mainSplit, wxID_ANY, wxDefaultPosition,
                                        wxDefaultSize, wxSP_LIVE_UPDATE | wxSP_3DSASH);
    m_rightSplit->SetMinimumPaneSize(180);
    m_rightSplit->SetSashGravity(0.6);
    m_service = new ServicePanel(m_rightSplit, m_db);
    m_preview = new PreviewPanel(m_rightSplit);
    m_rightSplit->SplitHorizontally(m_service, m_preview, -260);

    m_mainSplit->SplitVertically(m_notebook, m_rightSplit, 560);
    m_mainSplit->SetMinimumPaneSize(320);

    auto *root = new wxBoxSizer(wxVERTICAL);
    root->Add(m_mainSplit, 1, wxEXPAND);
    SetSizer(root);
}

void MainFrame::BuildStatusBar()
{
}

// ---------------------------------------------------------------------------
// Eventos de paneles
// ---------------------------------------------------------------------------
void MainFrame::BindPanelEvents()
{
    Bind(EVT_UI_GOLIVE_SONG, &MainFrame::OnPanelGoLiveSong, this);
    Bind(EVT_UI_ADD_SONG, &MainFrame::OnPanelAddSong, this);
    Bind(EVT_UI_GOLIVE_REF, &MainFrame::OnPanelGoLiveRef, this);
    Bind(EVT_UI_ADD_REF, &MainFrame::OnPanelAddRef, this);
    Bind(EVT_UI_GOLIVE_MEDIA, &MainFrame::OnPanelGoLiveMedia, this);
    Bind(EVT_UI_ADD_MEDIA, &MainFrame::OnPanelAddMedia, this);
    Bind(EVT_UI_APPLY_THEME, &MainFrame::OnPanelApplyTheme, this);
    Bind(EVT_UI_DATA_CHANGED, &MainFrame::OnPanelDataChanged, this);
    Bind(EVT_UI_SERVICE_GOLIVE, &MainFrame::OnPanelServiceGoLive, this);
    Bind(EVT_UI_SERVICE_CHANGED, &MainFrame::OnPanelServiceChanged, this);
    Bind(EVT_REMOTE_COMMAND, &MainFrame::OnRemoteCommand, this);
    Bind(EVT_OUTPUT_COMMAND, &MainFrame::OnOutputCommand, this);
}

void MainFrame::BindGlobalEvents()
{
    // Atajos globales
    auto accel = [](int flags, int key, int id) {
        return wxAcceleratorEntry(flags, key, id);
    };
    wxAcceleratorEntry entries[] = {
        accel(wxACCEL_NORMAL, WXK_F5, ID_LIVE),
        accel(wxACCEL_NORMAL, WXK_F6, ID_BLACK),
        accel(wxACCEL_NORMAL, WXK_F7, ID_CLEAR),
        accel(wxACCEL_NORMAL, WXK_F8, ID_LOGO),
        accel(wxACCEL_NORMAL, WXK_F9, ID_QUICKVERSE),
        accel(wxACCEL_CTRL, 'F', ID_SEARCHMENU),
        accel(wxACCEL_CTRL, 'N', ID_NEWSONG),
        accel(wxACCEL_CTRL, 'S', wxID_SAVE),
        accel(wxACCEL_CTRL, 'O', ID_OPENSVC),
        accel(wxACCEL_NORMAL, WXK_F11, ID_PRESENT),
    };
    wxAcceleratorTable tbl(WXSIZEOF(entries), entries);
    SetAcceleratorTable(tbl);

    // Los ids de aceleradores disparan eventos de menu/tool reales
    Bind(wxEVT_MENU, [this](wxCommandEvent &) { SetOutputMode(OutputFrame::ModeBlack); },
         ID_BLACK, ID_BLACK);
    Bind(wxEVT_MENU, [this](wxCommandEvent &) { SetOutputMode(OutputFrame::ModeClear); },
         ID_CLEAR, ID_CLEAR);
    Bind(wxEVT_MENU, [this](wxCommandEvent &) { SetOutputMode(OutputFrame::ModeLogo); },
         ID_LOGO, ID_LOGO);
    Bind(wxEVT_MENU, [this](wxCommandEvent &) {
        if (m_liveIndex + 1 < (int)m_liveSlides.size())
            NextSlide();
        else if (m_service->SelectedIndex() >= 0)
            GoLiveServiceItem(m_service->SelectedIndex() + 1);
        else
            GoLiveServiceItem(0);
    }, ID_LIVE, ID_LIVE);
    Bind(wxEVT_MENU, [this](wxCommandEvent &) { PrevSlide(); }, ID_PREV, ID_PREV);
    Bind(wxEVT_MENU, [this](wxCommandEvent &) { NextSlide(); }, ID_NEXT, ID_NEXT);
    Bind(wxEVT_MENU, [this](wxCommandEvent &) { QuickVerse(); }, ID_QUICKVERSE, ID_QUICKVERSE);
    Bind(wxEVT_MENU, [this](wxCommandEvent &) { m_songs->FocusSearch(); },
         ID_SEARCHMENU, ID_SEARCHMENU);
    Bind(wxEVT_MENU, [this](wxCommandEvent &) { m_songs->NewSong(); }, ID_NEWSONG, ID_NEWSONG);
    Bind(wxEVT_MENU, &MainFrame::OnLowerThird, this, ID_LOWER, ID_LOWER);
    Bind(wxEVT_MENU, &MainFrame::OnTranspose, this, ID_TRANS_UP, ID_TRANS_UP);
    Bind(wxEVT_MENU, &MainFrame::OnTranspose, this, ID_TRANS_DOWN, ID_TRANS_DOWN);
    Bind(wxEVT_MENU, &MainFrame::OnTranspose, this, ID_TRANS_RESET, ID_TRANS_RESET);
    Bind(wxEVT_MENU, &MainFrame::OnServerToggle, this, ID_SERVERBTN, ID_SERVERBTN);
    Bind(wxEVT_MENU, &MainFrame::OnSettings, this, ID_SETTINGS, ID_SETTINGS);
}

// ---------------------------------------------------------------------------
// Motor en vivo
// ---------------------------------------------------------------------------
std::vector<Slide> MainFrame::BuildSongSlides(const Song &s)
{
    Lyrics::BuildOptions opt;
    opt.titleSlide = false;              // directo al contenido en vivo
    opt.maxLinesPerSlide = m_db.GetSettingInt("lines_per_slide", 4);
    opt.chorusInterleave = m_db.GetSettingInt("hymn_mode", 0) != 0;
    opt.stripChords = true;
    opt.transpose = m_transpose;
    opt.latinChords = true;
    return Lyrics::BuildSlides(s, opt);
}

std::vector<Slide> MainFrame::BuildRefSlides(int book, int chapter, int vFrom, int vTo)
{
    const wxString version = m_db.BibleVersions().empty()
        ? wxString("RVR1909")
        : m_db.BibleVersions().front();
    std::vector<Database::BibleRow> rows =
        m_db.BiblePassage(version, book, chapter, vFrom, vTo);
    std::vector<Slide> out;
    if (rows.empty())
        return out;
    const BibleRef::VerseRef first{ book, chapter, vFrom, "" };
    const BibleRef::VerseRef last{ book, chapter, rows.back().verse, "" };
    const wxString head = vFrom == rows.back().verse
        ? BibleRef::FormatRef(first)
        : BibleRef::FormatRef(first) + L"–" + wxString::Format("%d", rows.back().verse);

    // Hasta 3 versiculos por slide
    std::vector<SlideLine> cur;
    int curStart = 0;
    int inSlide = 0;
    for (const Database::BibleRow &r : rows) {
        wxString txt = r.text;
        txt.Replace("  ", " ");
        cur.push_back(SlideLine(wxString::Format("%d %s", r.verse, txt)));
        if (curStart == 0)
            curStart = r.verse;
        ++inSlide;
        if (inSlide == 3) {
            Slide s;
            s.kind = Slide::Bible;
            s.title = head;
            s.refLabel = head;
            s.lines = cur;
            out.push_back(s);
            cur.clear();
            inSlide = 0;
            curStart = 0;
        }
    }
    if (!cur.empty()) {
        Slide s;
        s.kind = Slide::Bible;
        s.title = head;
        s.refLabel = head;
        s.lines = cur;
        out.push_back(s);
    }
    return out;
}

void MainFrame::PushLive(std::vector<Slide> slides, const wxString &label)
{
    if (slides.empty()) {
        SetStatusText("No hay contenido para proyectar.", 0);
        return;
    }
    m_liveSlides = std::move(slides);
    m_liveIndex = 0;
    m_liveLabel = label;
    SetOutputMode(OutputFrame::ModeLive);
    if (!m_output->IsShown())
        m_output->AttachToDisplay((int)AppPaths::CfgInt("output_screen", -1));
    UpdateProjection();
    SetStatusText("EN VIVO: " + label, 0);
}

void MainFrame::GoLiveSong(int songId)
{
    const Song s = m_db.SongById(songId);
    if (s.id <= 0)
        return;
    m_db.LogSongUse(s.id);
    PushLive(BuildSongSlides(s), s.title);
}

void MainFrame::GoLiveRef(int book, int chapter, int vFrom, int vTo)
{
    const std::vector<Slide> slides = BuildRefSlides(book, chapter, vFrom, vTo);
    if (slides.empty()) {
        SetStatusText(L"Versículo no encontrado en la versión instalada.", 0);
        return;
    }
    PushLive(slides, slides.front().refLabel);
}

void MainFrame::GoLiveMedia(const wxString &path)
{
    wxFileName fn(path);
    const bool isVideo = wxString("mp4|mov|avi|wmv|mkv|webm|mpg|mpeg|m4v")
                             .Contains(fn.GetExt().Lower());
    Slide s;
    if (isVideo) {
        s.kind = Slide::Video;
        s.title = fn.GetFullName();
        s.mediaPath = path;
    } else {
        s.kind = Slide::Image;
        s.mediaPath = path;
    }
    PushLive({ s }, fn.GetFullName());
}

void MainFrame::GoLiveNotice(const wxString &text)
{
    Slide s;
    s.kind = Slide::Aviso;
    s.lines.push_back(SlideLine(text));
    PushLive({ s }, "Aviso");
}

void MainFrame::GoLiveServiceItem(int row)
{
    const std::vector<ServiceItem> items = m_service->GetItems();
    if (row < 0 || row >= (int)items.size())
        return;
    m_service->SelectRow(row);
    const ServiceItem &it = items[row];
    switch (it.kind) {
        case ServiceItem::Song:
            GoLiveSong(it.refId);
            break;
        case ServiceItem::Bible: {
            // payload "book|chap|vfrom|vto"
            wxStringTokenizer toks(it.payload, "|");
            long b = 0, c = 0, vf = 0, vt = 0;
            if (toks.HasMoreTokens()) toks.GetNextToken().ToLong(&b);
            if (toks.HasMoreTokens()) toks.GetNextToken().ToLong(&c);
            if (toks.HasMoreTokens()) toks.GetNextToken().ToLong(&vf);
            if (toks.HasMoreTokens()) toks.GetNextToken().ToLong(&vt);
            GoLiveRef((int)b, (int)c, (int)vf, (int)vt);
            break;
        }
        case ServiceItem::Image:
        case ServiceItem::Video:
            GoLiveMedia(it.payload);
            break;
        case ServiceItem::Aviso:
            GoLiveNotice(it.payload);
            break;
    }
}

void MainFrame::NextSlide()
{
    if (m_liveIndex + 1 < (int)m_liveSlides.size()) {
        ++m_liveIndex;
        SetOutputMode(OutputFrame::ModeLive);
        UpdateProjection();
    } else {
        // fin del item: pasar al siguiente del culto
        const int cur = m_service->SelectedIndex();
        if (cur + 1 < (int)m_service->GetItems().size())
            GoLiveServiceItem(cur + 1);
        else
            SetStatusText("Fin del culto.", 0);
    }
}

void MainFrame::PrevSlide()
{
    if (m_liveIndex > 0) {
        --m_liveIndex;
        SetOutputMode(OutputFrame::ModeLive);
        UpdateProjection();
    } else {
        const int cur = m_service->SelectedIndex();
        if (cur > 0)
            GoLiveServiceItem(cur - 1);
    }
}

void MainFrame::SetOutputMode(OutputFrame::Mode mode)
{
    if (!m_output->IsShown() && mode != OutputFrame::ModeLive)
        m_output->AttachToDisplay((int)AppPaths::CfgInt("output_screen", -1));
    m_output->SetMode(mode);
    UpdatePreviews();
    UpdateStatusBar();
}

void MainFrame::ApplyTheme(int themeId)
{
    if (themeId <= 0)
        return;
    m_liveTheme = m_db.ThemeById(themeId);
    m_liveThemeId = m_liveTheme.id;
    Renderer::ClearCache();
    m_output->Invalidate();
    UpdatePreviews();
    SetStatusText("Tema aplicado: " + m_liveTheme.name, 0);
}

void MainFrame::RefreshScreenCombo()
{
    if (!m_screenCombo)
        return;
    const wxString prev = m_screenCombo->GetStringSelection();
    m_screenCombo->Clear();
    m_screenCombo->Append("Ventana (prueba)");
    for (unsigned i = 0; i < wxDisplay::GetCount(); ++i) {
        wxDisplay d(i);
        m_screenCombo->Append(wxString::Format(L"Pantalla %u — %dx%d", i + 1,
                                               d.GetGeometry().width, d.GetGeometry().height));
    }
    const int sel = (int)AppPaths::CfgInt("output_screen", -1);
    m_screenCombo->SetSelection(sel + 1 >= 0 && sel + 1 < (int)m_screenCombo->GetCount()
                                ? sel + 1 : 0);
    (void)prev;
    // La salida se ADJUNTA al proyectar (no al arranque: no abrir ventana en negro)
}

// ---------------------------------------------------------------------------
// Proyeccion / previews
// ---------------------------------------------------------------------------
void MainFrame::UpdateProjection()
{
    m_output->Invalidate();
    if (m_liveIndex >= 0 && m_liveIndex < (int)m_liveSlides.size()) {
        m_output->ShowSlide(m_liveSlides[m_liveIndex], m_liveTheme,
                            m_liveIndex, (int)m_liveSlides.size());
    }
    UpdatePreviews();
    UpdateStatusBar();
}

void MainFrame::UpdatePreviews()
{
    if (m_liveIndex >= 0 && m_liveIndex < (int)m_liveSlides.size()) {
        const Slide *cur = &m_liveSlides[m_liveIndex];
        m_preview->SetLive(cur, &m_liveTheme, m_liveIndex, (int)m_liveSlides.size());
        const Slide *next = (m_liveIndex + 1 < (int)m_liveSlides.size())
            ? &m_liveSlides[m_liveIndex + 1] : nullptr;
        m_preview->SetNext(next, &m_liveTheme);
    } else {
        m_preview->SetPlaceholders();
    }
}

void MainFrame::UpdateStatusBar()
{
    const bool serverOn = m_server && m_server->IsRunning();
    SetStatusText(wxString::Format(L"Salida: %s · Tema: %s",
        m_output->GetMode() == OutputFrame::ModeLive ? wxString("en vivo")
        : m_output->GetMode() == OutputFrame::ModeBlack ? wxString("negro")
        : m_output->GetMode() == OutputFrame::ModeClear ? wxString("limpio") : wxString("logo"),
        m_liveTheme.name), 0);
    SetStatusText(serverOn
        ? wxString::Format(L"Remoto ON — puerto %d", m_server->Port())
        : wxString("Remoto apagado"), 1);
    SetStatusText(wxString::Format(L"v%s · %d canciones", APP_VERSION,
        (int)m_db.SearchSongs(Database::SearchFilter{}).size()), 2);
}

// ---------------------------------------------------------------------------
// Handlers de toolbar/menus
// ---------------------------------------------------------------------------
void MainFrame::OnNext(wxCommandEvent &)  { NextSlide(); }
void MainFrame::OnPrev(wxCommandEvent &)  { PrevSlide(); }
void MainFrame::OnBlack(wxCommandEvent &) { SetOutputMode(OutputFrame::ModeBlack); }
void MainFrame::OnClear(wxCommandEvent &) { SetOutputMode(OutputFrame::ModeClear); }
void MainFrame::OnLogo(wxCommandEvent &)  { SetOutputMode(OutputFrame::ModeLogo); }

void MainFrame::OnScreenSel(wxCommandEvent &)
{
    const int sel = m_screenCombo->GetSelection() - 1;    // -1 = ventana
    AppPaths::SetCfgInt("output_screen", sel);
    m_output->AttachToDisplay(sel);
    SetStatusText(sel < 0 ? "Salida: ventana flotante"
                          : wxString::Format("Salida: pantalla %d", sel + 1), 0);
}

void MainFrame::OnLowerThird(wxCommandEvent &)
{
    m_lowerOn = !m_lowerOn;
    if (m_lowerOn) {
        wxTextEntryDialog dlg(this, L"Texto del Lower Third (título — línea 2 opcional separada por |):",
                              "Lower Third", m_lowerTitle);
        if (dlg.ShowModal() == wxID_OK) {
            const wxString v = dlg.GetValue().Trim(true).Trim(false);
            if (v.empty()) {
                m_lowerOn = false;
            } else if (v.Contains('|')) {
                m_lowerTitle = v.BeforeFirst('|').Trim(true).Trim(false);
                m_lowerText = v.AfterFirst('|').Trim(true).Trim(false);
            } else {
                m_lowerTitle = v;
                m_lowerText.clear();
            }
        } else {
            m_lowerOn = false;
        }
    }
    m_output->SetLowerThird(m_lowerOn, m_lowerTitle, m_lowerText);
    SetStatusText(m_lowerOn ? "Lower Third activo" : "Lower Third desactivado", 0);
}

void MainFrame::OnTranspose(wxCommandEvent &e)
{
    if (e.GetId() == ID_TRANS_UP)
        ++m_transpose;
    else if (e.GetId() == ID_TRANS_DOWN)
        --m_transpose;
    else
        m_transpose = 0;
    m_transposeLabel->SetLabel(wxString::Format(" %+d ", m_transpose));
    // Re-proyectar la cancion actual con la nueva transposicion
    const int cur = m_service->SelectedIndex();
    const std::vector<ServiceItem> items = m_service->GetItems();
    if (cur >= 0 && cur < (int)items.size() && items[cur].kind == ServiceItem::Song) {
        const Song s = m_db.SongById(items[cur].refId);
        if (s.id > 0) {
            m_liveSlides = BuildSongSlides(s);
            if (m_liveIndex >= (int)m_liveSlides.size())
                m_liveIndex = (int)m_liveSlides.size() - 1;
            UpdateProjection();
        }
    }
    SetStatusText(m_transpose == 0 ? "Tono original"
                                   : wxString::Format(L"Transposición: %+d semitonos", m_transpose), 0);
}

// ---------------------------------------------------------------------------
// Servidor remoto
// ---------------------------------------------------------------------------
void MainFrame::StartServerIfNeeded()
{
    if (m_db.GetSettingInt("server_on", 1) == 0)
        return;
    StartServer();
}

void MainFrame::StartServer()
{
    StopServer();
    const int port = m_db.GetSettingInt("server_port", 8088);
    const wxString token = m_db.GetSetting("server_token");
    m_server = new WebServer();
    m_server->SetSink(this);
    wxString err;
    if (!m_server->Start(port, token,
                         [this] { return BuildStateJson(); },
                         [this] { return BuildLiveText(); }, &err)) {
        delete m_server;
        m_server = nullptr;
        SetStatusText(L"Remoto: error — " + err, 1);
        wxToggleButton *tb = wxDynamicCast(GetToolBar()->FindControl(ID_SERVERBTN), wxToggleButton);
        if (tb) tb->SetValue(false);
        return;
    }
    wxToggleButton *tb = wxDynamicCast(GetToolBar()->FindControl(ID_SERVERBTN), wxToggleButton);
    if (tb) tb->SetValue(true);
    SetStatusText(wxString::Format(L"Remoto ON — http://<pc>:%d", port), 1);
}

void MainFrame::StopServer()
{
    if (m_server) {
        m_server->Stop();
        delete m_server;
        m_server = nullptr;
    }
    wxToggleButton *tb = wxDynamicCast(GetToolBar()->FindControl(ID_SERVERBTN), wxToggleButton);
    if (tb) tb->SetValue(false);
}

void MainFrame::OnServerToggle(wxCommandEvent &)
{
    wxToggleButton *tb = wxDynamicCast(GetToolBar()->FindControl(ID_SERVERBTN), wxToggleButton);
    if (m_server && m_server->IsRunning()) {
        StopServer();
        SetStatusText("Remoto apagado", 1);
    } else {
        StartServer();
    }
    (void)tb;
}

// ---------------------------------------------------------------------------
// Estado JSON / texto vivo
// ---------------------------------------------------------------------------
wxString MainFrame::BuildStateJson() const
{
    wxString ref, text, mode;
    int index = 0, count = 0;
    if (m_liveIndex >= 0 && m_liveIndex < (int)m_liveSlides.size()) {
        const Slide &s = m_liveSlides[m_liveIndex];
        ref = s.refLabel.empty() ? s.title : s.title + (s.refLabel.empty() ? "" : L" · " + s.refLabel);
        text = Lyrics::PlainText(s);
        index = m_liveIndex + 1;
        count = (int)m_liveSlides.size();
    }
    switch (m_output->GetMode()) {
        case OutputFrame::ModeLive:  mode = "live";  break;
        case OutputFrame::ModeBlack: mode = "black"; break;
        case OutputFrame::ModeClear: mode = "clear"; break;
        case OutputFrame::ModeLogo:  mode = "logo";  break;
    }
    return wxString::Format(
        "{\"app\":\"Fusion-HP %s\",\"mode\":\"%s\",\"index\":%d,\"count\":%d,"
        "\"ref\":\"%s\",\"text\":\"%s\"}",
        APP_VERSION, mode, index, count, JsonEsc(ref), JsonEsc(text));
}

wxString MainFrame::BuildLiveText() const
{
    if (m_liveIndex < 0 || m_liveIndex >= (int)m_liveSlides.size())
        return wxString();
    return Lyrics::PlainText(m_liveSlides[m_liveIndex]);
}

// ---------------------------------------------------------------------------
// Comandos remotos / salida
// ---------------------------------------------------------------------------
void MainFrame::OnRemoteCommand(wxCommandEvent &e)
{
    const wxString cmd = e.GetString();
    if (cmd == "next")       NextSlide();
    else if (cmd == "prev")  PrevSlide();
    else if (cmd == "black") SetOutputMode(OutputFrame::ModeBlack);
    else if (cmd == "clear") SetOutputMode(OutputFrame::ModeClear);
    else if (cmd == "logo")  SetOutputMode(OutputFrame::ModeLogo);
    else if (cmd.StartsWith("goto:")) {
        long idx = 0;
        if (cmd.Mid(5).ToLong(&idx))
            GoLiveServiceItem((int)idx - 1);
    } else if (cmd.StartsWith("alert:")) {
        GoLiveNotice(cmd.Mid(6));
    }
}

void MainFrame::OnOutputCommand(wxCommandEvent &e)
{
    // Reutiliza el mismo vocabulario del remoto
    OnRemoteCommand(e);
}

// ---------------------------------------------------------------------------
// Eventos de paneles
// ---------------------------------------------------------------------------
void MainFrame::OnPanelGoLiveSong(wxCommandEvent &e) { GoLiveSong(e.GetInt()); }

void MainFrame::OnPanelAddSong(wxCommandEvent &e)
{
    const int id = e.GetInt();
    const Song s = m_db.SongById(id);
    if (s.id <= 0)
        return;
    ServiceItem it;
    it.kind = ServiceItem::Song;
    it.refId = s.id;
    it.label = s.title;
    std::vector<ServiceItem> items = m_service->GetItems();
    items.push_back(it);
    m_service->SetItems(items);
    SetStatusText(L"Añadido al culto: " + s.title, 0);
}

void MainFrame::OnPanelGoLiveRef(wxCommandEvent &e)
{
    wxStringTokenizer toks(e.GetString(), "|");
    long b = 0, c = 0, vf = 0, vt = 0;
    if (toks.HasMoreTokens()) toks.GetNextToken().ToLong(&b);
    if (toks.HasMoreTokens()) toks.GetNextToken().ToLong(&c);
    if (toks.HasMoreTokens()) toks.GetNextToken().ToLong(&vf);
    if (toks.HasMoreTokens()) toks.GetNextToken().ToLong(&vt);
    GoLiveRef((int)b, (int)c, (int)vf, (int)vt);
}

void MainFrame::OnPanelAddRef(wxCommandEvent &e)
{
    ServiceItem it;
    it.kind = ServiceItem::Bible;
    it.label = e.GetString();
    it.payload = e.GetString();     // resuelto al proyectar
    std::vector<ServiceItem> items = m_service->GetItems();
    items.push_back(it);
    m_service->SetItems(items);
    SetStatusText(L"Añadido al culto: " + e.GetString(), 0);
}

void MainFrame::OnPanelGoLiveMedia(wxCommandEvent &e) { GoLiveMedia(e.GetString()); }

void MainFrame::OnPanelAddMedia(wxCommandEvent &e)
{
    wxFileName fn(e.GetString());
    const bool isVideo = wxString("mp4|mov|avi|wmv|mkv|webm|mpg|mpeg|m4v")
                             .Contains(fn.GetExt().Lower());
    ServiceItem it;
    it.kind = isVideo ? ServiceItem::Video : ServiceItem::Image;
    it.label = fn.GetFullName();
    it.payload = e.GetString();
    std::vector<ServiceItem> items = m_service->GetItems();
    items.push_back(it);
    m_service->SetItems(items);
    SetStatusText(L"Añadido al culto: " + fn.GetFullName(), 0);
}

void MainFrame::OnPanelApplyTheme(wxCommandEvent &e) { ApplyTheme(e.GetInt()); }

void MainFrame::OnPanelDataChanged(wxCommandEvent &e)
{
    const wxString what = e.GetString();
    if (what == "themes") {
        // si el tema en vivo fue eliminado, mantener el actual en memoria
    }
    UpdateStatusBar();
}

void MainFrame::OnPanelServiceGoLive(wxCommandEvent &e) { GoLiveServiceItem(e.GetInt()); }

void MainFrame::OnPanelServiceChanged(wxCommandEvent &e)
{
    // Tras reordenar: si hay algo en vivo, la vista sigue; opcional: auto-go-live
    if (e.GetInt() >= 0)
        GoLiveServiceItem(e.GetInt());
}

// ---------------------------------------------------------------------------
// Ajustes / utilidades
// ---------------------------------------------------------------------------
void MainFrame::OnSettings(wxCommandEvent &)
{
    SettingsDialog dlg(this, m_db);
    if (dlg.ShowModal() == wxID_OK) {
        // reiniciar servidor si cambió la config
        StopServer();
        StartServerIfNeeded();
        RefreshScreenCombo();
        UpdateStatusBar();
    }
}

void MainFrame::OnAbout(wxCommandEvent &)
{
    wxAboutDialogInfo info;
    info.SetName("LuminaPresentation Suite");
    info.SetVersion(wxString::Format(L"v%s «Horizonte» (wxWidgets)", APP_VERSION));
    info.SetDescription("Proyección multimedia híbrida nativa para el culto —\n"
                        "canciones, Biblia, medios y temas en un solo programa.\n\n"
                        L"Binarios de uso libre · Código fuente bajo Licencia View-Only.");
    info.SetCopyright("(C) 2026 Isaac");
    info.SetWebSite("https://github.com/isaac4722/Fusion-HP");
    wxAboutBox(info);
}

void MainFrame::OnShortcuts(wxCommandEvent &)
{
    wxMessageBox(
        "Atajos de teclado\n\n"
        "F5   — En vivo (proyecta la selección o avanza)\n"
        "F6   — Pantalla en negro\n"
        "F7   — Limpiar (solo fondo)\n"
        "F8   — Logo\n"
        "F9   — Versículo rápido (escribe «Jn 3:16»)\n"
        "F11  — Modo presentación (consola mínima)\n\n"
        "Ctrl+F  Buscar canciones\n"
        "Ctrl+N  Nueva canción\n"
        "Ctrl+S  Guardar culto\n"
        "Ctrl+O  Abrir culto\n\n"
        "En la pantalla de salida: →/Espacio avanza · ← retrocede · Esc limpia\n"
        L"En el culto: Alt+↑/↓ mueve el ítem · Supr lo quita",
        "Atajos", wxOK | wxICON_INFORMATION);
}

void MainFrame::OnPresentationMode(wxCommandEvent &)
{
    m_presentationMode = !m_presentationMode;
    if (m_presentationMode) {
        m_mainSplit->Unsplit(m_notebook);        // solo consola de culto+preview
        SetStatusText(L"Modo presentación — F11 para restaurar", 0);
    } else {
        m_mainSplit->SplitVertically(m_notebook, m_rightSplit, 560);
        SetStatusText("Consola completa", 0);
    }
}

void MainFrame::OnSearchFocus(wxCommandEvent &)
{
    m_notebook->SetSelection(0);
    m_songs->FocusSearch();
}

void MainFrame::QuickVerse()
{
    wxTextEntryDialog dlg(this,
        "Escribe la referencia (Jn 3:16, salmo 23:1-4, 1 co 13, 4-7):",
        L"Versículo rápido");
    if (dlg.ShowModal() != wxID_OK)
        return;
    const wxString raw = dlg.GetValue().Trim(true).Trim(false);
    if (raw.empty())
        return;
    const BibleRef::VerseRef ref = BibleRef::Resolve(raw);
    if (!ref.Valid()) {
        wxMessageBox(L"Referencia no reconocida: «" + raw + "\"", L"Versículo rápido",
                     wxOK | wxICON_INFORMATION);
        return;
    }
    GoLiveRef(ref.book, ref.chapter, ref.verse > 0 ? ref.verse : 1,
              ref.verse > 0 ? ref.verse : 200);
}

// ---------------------------------------------------------------------------
// Cultos: guardar/abrir/exportar
// ---------------------------------------------------------------------------
void MainFrame::OnNewService(wxCommandEvent &e)
{
    m_service->OnNewPlaylist(e);
}

void MainFrame::OnSaveService(wxCommandEvent &)
{
    wxFileDialog dlg(this, "Guardar culto", AppPaths::DataDir(), "culto.json",
                     "Culto Fusion-HP (*.json)|*.json", wxFD_SAVE | wxFD_OVERWRITE_PROMPT);
    if (dlg.ShowModal() != wxID_OK)
        return;
    json j = json::array();
    for (const ServiceItem &it : m_service->GetItems()) {
        j.push_back({ { "kind", it.kind }, { "refId", it.refId },
                      { "label", ToUtf8(it.label) },
                      { "payload", ToUtf8(it.payload) } });
    }
    wxFile f(dlg.GetPath(), wxFile::write);
    if (f.IsOpened()) {
        const std::string data = j.dump(2);
        f.Write(data.c_str(), data.size());
        SetStatusText("Culto guardado: " + dlg.GetPath(), 0);
    }
}

void MainFrame::OnOpenService(wxCommandEvent &)
{
    wxFileDialog dlg(this, "Abrir culto", AppPaths::DataDir(), wxString(),
                     "Culto Fusion-HP (*.json)|*.json|Todos (*.*)|*.*",
                     wxFD_OPEN | wxFD_FILE_MUST_EXIST);
    if (dlg.ShowModal() != wxID_OK)
        return;
    wxFile f(dlg.GetPath());
    if (!f.IsOpened())
        return;
    const wxFileOffset len = f.Length();
    wxCharBuffer buf(len);
    f.Read(buf.data(), len);
    // Parseo directo de bytes UTF-8 (independiente del locale)
    json j = json::parse(buf.data(), buf.data() + len, nullptr, false);
    if (!j.is_array()) {
        wxMessageBox(L"El archivo no es un culto válido.", "Abrir culto",
                     wxOK | wxICON_ERROR);
        return;
    }
    std::vector<ServiceItem> items;
    for (const auto &el : j) {
        ServiceItem it;
        it.kind = el.value("kind", (int)ServiceItem::Song);
        it.refId = el.value("refId", 0);
        it.label = FromUtf8(el.value("label", ""));
        it.payload = FromUtf8(el.value("payload", ""));
        items.push_back(it);
    }
    m_service->SetItems(items);
    SetStatusText(wxString::Format(L"Culto cargado (%d ítems)", (int)items.size()), 0);
}

void MainFrame::OnExportCsv(wxCommandEvent &)
{
    wxFileDialog dlg(this, "Exportar culto a CSV", AppPaths::DataDir(), "culto.csv",
                     "CSV (*.csv)|*.csv", wxFD_SAVE | wxFD_OVERWRITE_PROMPT);
    if (dlg.ShowModal() != wxID_OK)
        return;
    wxFile f(dlg.GetPath(), wxFile::write);
    if (!f.IsOpened())
        return;
    // B14 heredado: escapado correcto de ; y comillas
    auto csvField = [](const wxString &s) {
        wxString v = s;
        if (v.Contains(';') || v.Contains('"') || v.Contains('\n')) {
            v.Replace("\"", "\"\"");
            v = "\"" + v + "\"";
        }
        return v;
    };
    wxString out = "#;Tipo;Título\n";
    int n = 1;
    for (const ServiceItem &it : m_service->GetItems()) {
        const wxString kind = (it.kind == ServiceItem::Song) ? L"Canción"
                              : (it.kind == ServiceItem::Bible) ? L"Biblia"
                              : (it.kind == ServiceItem::Image) ? L"Imagen"
                              : (it.kind == ServiceItem::Video) ? L"Video" : L"Aviso";
        out += wxString::Format("%d;%s;%s\n", n++, kind, csvField(it.label));
    }
    const wxScopedCharBuffer bb = out.utf8_str();
    f.Write(bb.data(), bb.length());
    SetStatusText("CSV exportado: " + dlg.GetPath(), 0);
}

// ---------------------------------------------------------------------------
// Importacion de Biblia (JSON incluido o del usuario)
// ---------------------------------------------------------------------------
void MainFrame::OnImportBible(wxCommandEvent &)
{
    wxFileDialog dlg(this, "Importar Biblia (formato JSON)",
                     wxString(), wxString(), "Biblia (*.json)|*.json",
                     wxFD_OPEN | wxFD_FILE_MUST_EXIST);
    if (dlg.ShowModal() != wxID_OK)
        return;
    ImportBibleFile(dlg.GetPath());
}

void MainFrame::ImportBibleFile(const wxString &path)
{
    wxFile f(path);
    if (!f.IsOpened()) {
        wxMessageBox("No se pudo abrir el archivo.", "Importar Biblia", wxOK | wxICON_ERROR);
        return;
    }
    wxBusyCursor busy;
    const wxFileOffset len2 = f.Length();
    wxCharBuffer buf2(len2);
    f.Read(buf2.data(), len2);
    json j = json::parse(buf2.data(), buf2.data() + len2, nullptr, false);
    if (!j.is_object() || !j.contains("books") || !j["books"].is_array()) {
        wxMessageBox(L"Formato no reconocido (se espera JSON con «books»).",
                     "Importar Biblia", wxOK | wxICON_ERROR);
        return;
    }
    wxString version("BIBLIA");
    if (j.contains("version") && j["version"].is_string())
        version = wxString(j["version"].get<std::string>());
    wxString name = version;
    if (j.contains("name") && j["name"].is_string())
        name = wxString(j["name"].get<std::string>());

    std::vector<Database::BibleRow> rows;
    for (const auto &b : j["books"]) {
        if (!b.is_object() || !b.contains("chapters"))
            continue;
        const int bookNum = b.value("n", 0);
        int chap = 0;
        for (const auto &ch : b["chapters"]) {
            ++chap;
            if (!ch.is_array())
                continue;
            int verse = 0;
            for (const auto &v : ch) {
                ++verse;
                if (!v.is_string())
                    continue;
                Database::BibleRow r;
                r.book = bookNum;
                r.chapter = chap;
                r.verse = verse;
                r.text = FromUtf8(v.get<std::string>());
                rows.push_back(r);
            }
        }
    }
    if (rows.empty()) {
        wxMessageBox(L"El archivo no contiene versículos.", "Importar Biblia",
                     wxOK | wxICON_ERROR);
        return;
    }
    wxString err;
    auto progress = [](int done, int total) {
        wxTheApp->Yield();       // mantener la UI viva durante la importacion
        (void)done; (void)total;
        return true;
    };
    if (m_db.ImportBible(version, name, rows, &err, progress)) {
        m_bible->RefreshVersions();
        wxMessageBox(wxString::Format(L"Biblia «%s» importada: %d versículos.",
                                      version, (int)rows.size()),
                     "Importar Biblia", wxOK | wxICON_INFORMATION);
    } else {
        wxMessageBox("Error importando: " + err, "Importar Biblia", wxOK | wxICON_ERROR);
    }
}

// ---------------------------------------------------------------------------
// Cierre
// ---------------------------------------------------------------------------
void MainFrame::OnClose(wxCloseEvent &e)
{
    AppPaths::SetCfgInt("win_w", GetSize().x);
    AppPaths::SetCfgInt("win_h", GetSize().y);
    AppPaths::SetCfgInt("win_max", IsMaximized() ? 1 : 0);
    StopServer();
    m_output.reset();             // destruir la salida ANTES del frame (TLW propia)
    e.Skip();
}
