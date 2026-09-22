// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  PreviewPanel.h : Area de previsualizacion dual (EN VIVO + SIGUIENTE) con
//  barras de titulo, render fiel al proyector y estado visual (borde ambar
//  cuando hay contenido en vivo).
// ============================================================================
#ifndef LUMINA_PREVIEWPANEL_H
#define LUMINA_PREVIEWPANEL_H

#include "../core/Types.h"

#include <wx/panel.h>

// Mini-lienzo que renderiza una slide con el Renderer real
class PreviewCanvas : public wxPanel
{
public:
    PreviewCanvas(wxWindow *parent, bool isLive);

    void SetSlide(const Slide *slide, const Theme *theme, int index, int total);
    void SetPlaceholder(const wxString &text);

    bool isLiveCanvas = false;

private:
    void OnPaint(wxPaintEvent &e);
    void OnErase(wxEraseEvent &) { }
    void OnSize(wxSizeEvent &e);
    void OnLeftDown(wxMouseEvent &e);

    const Slide *m_slide = nullptr;
    const Theme *m_theme = nullptr;
    int m_index = 0, m_total = 0;
    wxString m_placeholder;
    wxBitmap m_cache;
    wxSize m_cacheSize;
    bool m_cacheValid = false;

    wxDECLARE_EVENT_TABLE();
};

class PreviewPanel : public wxPanel
{
public:
    PreviewPanel(wxWindow *parent);

    void SetLive(const Slide *slide, const Theme *theme, int index, int total);
    void SetNext(const Slide *slide, const Theme *theme);
    void SetPlaceholders();
    PreviewCanvas *Live() const { return m_live; }
    PreviewCanvas *Next() const { return m_next; }

private:
    PreviewCanvas *m_live;
    PreviewCanvas *m_next;
};

#endif // LUMINA_PREVIEWPANEL_H
