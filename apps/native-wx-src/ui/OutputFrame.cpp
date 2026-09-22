// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  OutputFrame.cpp : implementacion de la ventana de salida (ver .h)
// ============================================================================
#include "OutputFrame.h"
#include "../core/Renderer.h"
#include "../core/AppPaths.h"
#include "Icons.h"

#include <wx/display.h>
#include <wx/dcbuffer.h>
#include <wx/filename.h>
#include <wx/image.h>
#include <wx/log.h>

#include <algorithm>

wxDEFINE_EVENT(EVT_OUTPUT_COMMAND, wxCommandEvent);

BEGIN_EVENT_TABLE(OutputFrame, wxFrame)
    EVT_PAINT(OutputFrame::OnPaint)
    EVT_ERASE_BACKGROUND(OutputFrame::OnErase)
    EVT_SIZE(OutputFrame::OnSize)
    EVT_KEY_DOWN(OutputFrame::OnKeyDown)
    EVT_LEFT_DOWN(OutputFrame::OnLeftDown)
    EVT_LEFT_DCLICK(OutputFrame::OnLeftDClick)
    EVT_MEDIA_STATECHANGED(wxID_ANY, OutputFrame::OnMediaState)
END_EVENT_TABLE()

OutputFrame::OutputFrame(wxEvtHandler *commandSink)
    : wxFrame(nullptr, wxID_ANY, L"LuminaPresentation — Salida", wxDefaultPosition,
              wxDefaultSize, wxFRAME_NO_TASKBAR | wxFRAME_SHAPED)
{
    SetBackgroundStyle(wxBG_STYLE_PAINT);
    SetBackgroundColour(*wxBLACK);
    m_sink = commandSink ? commandSink : GetEventHandler();
}

void OutputFrame::AttachToDisplay(int displayIndex)
{
    m_display = displayIndex;
    Hide();
    Invalidate();
    if (displayIndex < 0 || displayIndex >= (int)wxDisplay::GetCount()) {
        // Ventana flotante centrada en la principal (modo prueba)
        SetWindowStyle((GetWindowStyle() & ~wxFULLSCREEN_ALL));
        Maximize(false);
        SetSize(wxSize(960, 540));
        Center();
        Show();
        return;
    }
    wxDisplay disp(displayIndex);
    const wxRect geo = disp.GetClientArea();
    Move(geo.GetPosition());
    SetClientSize(geo.GetSize());
    ShowWithoutActivating();
    ShowFullScreen(true, wxFULLSCREEN_NOBORDER | wxFULLSCREEN_NOCAPTION);
}

void OutputFrame::Invalidate()
{
    m_cacheValid = false;
    if (IsShown())
        Refresh();
}

void OutputFrame::ShowSlide(const Slide &slide, const Theme &theme, int index, int total)
{
    m_slide = slide;
    m_theme = theme;
    m_index = index;
    m_total = total;
    m_mode = ModeLive;

    // Video: reproduce por wxMediaCtrl (DirectShow en Windows)
    if (slide.kind == Slide::Video && !slide.mediaPath.empty()) {
        EnsureVideo();
        if (m_media) {
            m_media->Show();
            if (m_media->Load(slide.mediaPath)) {
                m_media->Play();
                m_mediaPlaying = true;
            } else {
                wxLogWarning("No se pudo cargar el video: %s", slide.mediaPath);
            }
        }
    } else {
        StopVideo();
    }
    Invalidate();
}

void OutputFrame::SetMode(Mode mode)
{
    m_mode = mode;
    if (mode != ModeLive)
        StopVideo();
    Invalidate();
}

void OutputFrame::SetLowerThird(bool on, const wxString &title, const wxString &text)
{
    m_lower = on;
    m_lowerTitle = title;
    m_lowerText = text;
    Invalidate();
}

// ---------------------------------------------------------------------------
// Video
// ---------------------------------------------------------------------------
void OutputFrame::EnsureVideo()
{
    if (m_media)
        return;
    m_media = new wxMediaCtrl(this, wxID_ANY, wxString(), wxDefaultPosition, GetClientSize(), 0);
    // wx 3.2 no expone IsOk(): la creacion fallida deja el control sin backend;
    // Load() devolvera false y se informa via log. Se intenta igualmente.
    m_media->Hide();
}

void OutputFrame::StopVideo()
{
    if (m_media && m_mediaPlaying) {
        m_media->Stop();
        m_mediaPlaying = false;
    }
    if (m_media)
        m_media->Hide();
}

void OutputFrame::OnMediaState(wxMediaEvent &e)
{
    e.Skip();
}

// ---------------------------------------------------------------------------
// Render
// ---------------------------------------------------------------------------
void OutputFrame::OnSize(wxSizeEvent &e)
{
    Invalidate();
    if (m_media)
        m_media->SetSize(GetClientSize());
    e.Skip();
}

void OutputFrame::OnPaint(wxPaintEvent &)
{
    wxAutoBufferedPaintDC dc(this);
    const wxSize sz = GetClientSize();
    if (sz.x <= 0 || sz.y <= 0)
        return;

    switch (m_mode) {
        case ModeBlack:
            dc.SetBackground(*wxBLACK_BRUSH);
            dc.Clear();
            return;
        case ModeClear:
            dc.SetBackground(*wxBLACK_BRUSH);
            dc.Clear();
            // Modo limpiar = fondo del tema apagado (sin contenido)
            if (!m_cacheValid || m_cacheSize != sz) {
                m_cache = Renderer::RenderBackground(m_theme, sz.x, sz.y);
                m_cacheSize = sz;
                m_cacheValid = true;
            }
            dc.DrawBitmap(m_cache, 0, 0);
            return;
        case ModeLogo: {
            dc.SetBackground(*wxBLACK_BRUSH);
            dc.Clear();
            const wxString logo = AppPaths::LogoPath();
            if (!logo.empty()) {
                wxImage img(logo, wxBITMAP_TYPE_ANY);
                if (img.IsOk()) {
                    // escala a 40% de la salida conservando proporciones
                    const double s = std::min((double)sz.x / img.GetWidth() * 0.55,
                                              (double)sz.y / img.GetHeight() * 0.55);
                    img = img.Scale(std::max(1, (int)(img.GetWidth() * s)),
                                    std::max(1, (int)(img.GetHeight() * s)),
                                    wxIMAGE_QUALITY_BILINEAR);
                    dc.DrawBitmap(wxBitmap(img), (sz.x - img.GetWidth()) / 2,
                                  (sz.y - img.GetHeight()) / 2);
                    return;
                }
            }
            // Logo por defecto: texto de la suite
            dc.SetTextForeground(wxColour(230, 168, 42));
            wxFont f(wxFontInfo(sz.y / 10).Bold());
            dc.SetFont(f);
            const wxString t = "LuminaPresentation";
            const wxSize ts = dc.GetTextExtent(t);
            dc.DrawText(t, (sz.x - ts.x) / 2, (sz.y - ts.y) / 2);
            return;
        }
        case ModeLive:
        default:
            break;
    }

    if (!m_cacheValid || m_cacheSize != sz) {
        RenderOpts opts;
        opts.showCounter = m_total > 0;
        opts.counterIndex = m_index + 1;
        opts.counterTotal = m_total;
        opts.showChords = false;
        opts.lowerThird = m_lower;
        opts.lowerTitle = m_lowerTitle;
        opts.lowerText = m_lowerText;
        m_cache = Renderer::Render(m_slide, m_theme, sz.x, sz.y, opts);
        m_cacheSize = sz;
        m_cacheValid = true;
    }
    dc.DrawBitmap(m_cache, 0, 0);
}

// ---------------------------------------------------------------------------
// Entrada del operador en el proyector
// ---------------------------------------------------------------------------
void OutputFrame::Emit(const wxString &cmd)
{
    if (!m_sink)
        return;
    wxCommandEvent ev(EVT_OUTPUT_COMMAND);
    ev.SetString(cmd);
    m_sink->QueueEvent(ev.Clone());

}

void OutputFrame::OnKeyDown(wxKeyEvent &e)
{
    switch (e.GetKeyCode()) {
        case WXK_RIGHT:
        case WXK_SPACE:
        case WXK_PAGEDOWN:
            Emit("next");
            return;
        case WXK_LEFT:
        case WXK_PAGEUP:
            Emit("prev");
            return;
        case WXK_ESCAPE:
            Emit("clear");
            return;
        default:
            e.Skip();
    }
}

void OutputFrame::OnLeftDown(wxMouseEvent &)
{
    Emit("next");
}

void OutputFrame::OnLeftDClick(wxMouseEvent &)
{
    Emit("prev");
}
