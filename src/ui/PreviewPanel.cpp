// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  PreviewPanel.cpp : implementacion (ver .h)
// ============================================================================
#include "PreviewPanel.h"
#include "../core/Renderer.h"
#include "OutputFrame.h"      // EVT_OUTPUT_COMMAND

#include <wx/dcbuffer.h>
#include <wx/sizer.h>
#include <wx/stattext.h>
#include <wx/toplevel.h>

BEGIN_EVENT_TABLE(PreviewCanvas, wxPanel)
    EVT_PAINT(PreviewCanvas::OnPaint)
    EVT_ERASE_BACKGROUND(PreviewCanvas::OnErase)
    EVT_SIZE(PreviewCanvas::OnSize)
    EVT_LEFT_DOWN(PreviewCanvas::OnLeftDown)
END_EVENT_TABLE()

// ---------------------------------------------------------------------------
// PreviewCanvas
// ---------------------------------------------------------------------------
PreviewCanvas::PreviewCanvas(wxWindow *parent, bool isLive)
    : wxPanel(parent, wxID_ANY, wxDefaultPosition, wxDefaultSize, wxWANTS_CHARS)
{
    isLiveCanvas = isLive;
    SetBackgroundStyle(wxBG_STYLE_PAINT);
    SetMinSize(wxSize(240, 135));
    SetCursor(wxCURSOR_HAND);
    SetToolTip(isLive ? L"Vista EN VIVO — clic para avanzar (o F5)"
                      : L"SIGUIENTE slide");
}

void PreviewCanvas::SetSlide(const Slide *slide, const Theme *theme, int index, int total)
{
    m_slide = slide;
    m_theme = theme;
    m_index = index;
    m_total = total;
    m_placeholder.clear();
    m_cacheValid = false;
    Refresh();
    Update();
}

void PreviewCanvas::SetPlaceholder(const wxString &text)
{
    m_slide = nullptr;
    m_placeholder = text;
    m_cacheValid = false;
    Refresh();
    Update();
}

void PreviewCanvas::OnSize(wxSizeEvent &e)
{
    m_cacheValid = false;
    e.Skip();
}

void PreviewCanvas::OnLeftDown(wxMouseEvent &)
{
    // Clic en el preview en vivo = avanzar (comportamiento Holyrics)
    if (isLiveCanvas) {
        wxTopLevelWindow *top = wxDynamicCast(wxGetTopLevelParent(this), wxTopLevelWindow);
        if (top) {
            wxCommandEvent ev(EVT_OUTPUT_COMMAND);
            ev.SetString("next");
            top->GetEventHandler()->QueueEvent(ev.Clone());
        }
    }
}

void PreviewCanvas::OnPaint(wxPaintEvent &)
{
    wxAutoBufferedPaintDC dc(this);
    const wxSize sz = GetClientSize();
    if (sz.x <= 0 || sz.y <= 0)
        return;

    if (!m_cacheValid || m_cacheSize != sz) {
        if (m_slide && m_theme) {
            RenderOpts opts;
            opts.showCounter = m_total > 0;
            opts.counterIndex = m_index + 1;
            opts.counterTotal = m_total;
            m_cache = Renderer::Render(*m_slide, *m_theme, sz.x, sz.y, opts);
        } else {
            m_cache = wxBitmap(sz.x, sz.y, 32);
            wxMemoryDC mdc(m_cache);
            mdc.SetBackground(wxBrush(wxColour(14, 17, 24)));
            mdc.Clear();
            mdc.SetPen(wxPen(wxColour(52, 60, 74), 1));
            mdc.SetBrush(*wxTRANSPARENT_BRUSH);
            mdc.DrawRectangle(0, 0, sz.x, sz.y);
            mdc.SetTextForeground(wxColour(96, 106, 122));
            wxFont f(wxFontInfo(9).Family(wxFONTFAMILY_SWISS));
            mdc.SetFont(f);
            const wxString t = m_placeholder.empty()
                ? wxString("Sin contenido")
                : m_placeholder;
            const wxSize ts = mdc.GetTextExtent(t);
            mdc.DrawText(t, (sz.x - ts.x) / 2, (sz.y - ts.y) / 2);
        }
        m_cacheSize = sz;
        m_cacheValid = true;
    }
    dc.DrawBitmap(m_cache, 0, 0);

    // Borde de estado: ambar cuando el preview en vivo tiene contenido
    if (isLiveCanvas && m_slide) {
        dc.SetPen(wxPen(wxColour(230, 168, 42), 2));
        dc.SetBrush(wxBrush(wxTransparentColor));
        dc.DrawRectangle(1, 1, sz.x - 2, sz.y - 2);
    }
}

// ---------------------------------------------------------------------------
// PreviewPanel
// ---------------------------------------------------------------------------
PreviewPanel::PreviewPanel(wxWindow *parent)
    : wxPanel(parent, wxID_ANY)
{
    auto *root = new wxBoxSizer(wxVERTICAL);

    // Barras de encabezado
    auto *labels = new wxBoxSizer(wxHORIZONTAL);
    auto *lb = new wxStaticText(this, wxID_ANY, "  EN VIVO");
    lb->SetForegroundColour(wxColour(230, 168, 42));
    lb->SetFont(GetFont().Bold());
    auto *ln = new wxStaticText(this, wxID_ANY, "SIGUIENTE  ");
    ln->SetForegroundColour(wxColour(122, 134, 152));
    labels->Add(lb, 1, wxALIGN_CENTER_VERTICAL | wxLEFT, 4);
    labels->Add(ln, 1, wxALIGN_CENTER_VERTICAL | wxRIGHT, 4);
    root->Add(labels, 0, wxEXPAND | wxTOP | wxBOTTOM, 3);

    auto *row = new wxBoxSizer(wxHORIZONTAL);
    m_live = new PreviewCanvas(this, true);
    m_next = new PreviewCanvas(this, false);
    row->Add(m_live, 1, wxEXPAND | wxRIGHT, 4);
    row->Add(m_next, 1, wxEXPAND | wxLEFT, 4);
    root->Add(row, 1, wxEXPAND);

    SetSizer(root);
    SetPlaceholders();
}

void PreviewPanel::SetLive(const Slide *slide, const Theme *theme, int index, int total)
{
    m_live->SetSlide(slide, theme, index, total);
}

void PreviewPanel::SetNext(const Slide *slide, const Theme *theme)
{
    m_next->SetSlide(slide, theme, 0, 0);
}

void PreviewPanel::SetPlaceholders()
{
    m_live->SetPlaceholder("Sin contenido en vivo (F5)");
    m_next->SetPlaceholder(L"—");
}
