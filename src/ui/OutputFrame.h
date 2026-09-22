// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  OutputFrame.h : Ventana de SALIDA (proyeccion). Fullscreen sobre la
//  pantalla seleccionada (o ventana flotante), modos En vivo / Negro /
//  Limpiar / Logo, video embebido con wxMediaCtrl, Lower Third y controles
//  por teclado (flechas/espacio/Esc) para el operador en el proyector.
// ============================================================================
#ifndef LUMINA_OUTPUTFRAME_H
#define LUMINA_OUTPUTFRAME_H

#include "../core/Types.h"

#include <wx/frame.h>
#include <wx/mediactrl.h>

#include <memory>

// Comandos del proyector (teclado/clic en la pantalla de salida)
wxDECLARE_EVENT(EVT_OUTPUT_COMMAND, wxCommandEvent);

class OutputFrame : public wxFrame
{
public:
    enum Mode { ModeLive = 0, ModeBlack, ModeClear, ModeLogo };

    explicit OutputFrame(wxEvtHandler *commandSink);

    // Muestra en la pantalla indicada (-1 = ventana flotante centrada)
    void AttachToDisplay(int displayIndex);
    int  CurrentDisplay() const { return m_display; }

    void ShowSlide(const Slide &slide, const Theme &theme, int index, int total);
    void SetMode(Mode mode);
    Mode GetMode() const { return m_mode; }

    void SetLowerThird(bool on, const wxString &title, const wxString &text);

    // Cache de render (si cambia el tamano de la salida, invalidar)
    void Invalidate();

    // Los comandos de teclado del proyector se emiten como comandos remotos
    // ("next"/"prev"/"clear") al sumidero (MainFrame)
    void SetCommandSink(wxEvtHandler *sink) { m_sink = sink; }

private:
    void OnPaint(wxPaintEvent &e);
    void OnErase(wxEraseEvent &e) { }
    void OnSize(wxSizeEvent &e);
    void OnKeyDown(wxKeyEvent &e);
    void OnLeftDown(wxMouseEvent &e);
    void OnLeftDClick(wxMouseEvent &e);
    void OnMediaState(wxMediaEvent &e);

    void Emit(const wxString &cmd);
    void EnsureVideo();
    void StopVideo();

    wxEvtHandler *m_sink = nullptr;
    Mode m_mode = ModeLive;
    int m_display = -1;
    Slide m_slide;
    Theme m_theme;
    int m_index = 0, m_total = 0;
    bool m_lower = false;
    wxString m_lowerTitle, m_lowerText;

    wxBitmap m_cache;
    wxSize m_cacheSize;
    bool m_cacheValid = false;

    wxMediaCtrl *m_media = nullptr;
    bool m_mediaPlaying = false;

    wxDECLARE_EVENT_TABLE();
};

#endif // LUMINA_OUTPUTFRAME_H
