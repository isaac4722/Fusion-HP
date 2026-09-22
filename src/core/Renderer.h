// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Renderer.h : Motor de renderizado de slides (port de la edicion Qt v1.6.0).
//  Dibuja sobre wxGraphicsContext (GDI+/Cairo): fondos solido/gradiente/imagen
//  con ajuste Llenar/Ajustar, texto con contorno por silueta + sombra,
//  ajuste automatico de tipografia, cifrado sobre el texto, contador de
//  slide, Lower Third y utilidades de contraste WCAG.
//
//  El renderizador trabaja en PIXELES FISICOS: quien llama pasa el tamano
//  real del destino (cliente * escala DPI) y todo el diseno escala desde la
//  referencia 1920x1080 — nitidez garantizada en escalados 100-200%.
// ============================================================================
#ifndef LUMINA_RENDERER_H
#define LUMINA_RENDERER_H

#include "Types.h"

#include <wx/bitmap.h>
#include <wx/graphics.h>
#include <wx/gdicmn.h>

#include <vector>

struct RenderOpts
{
    bool     showCounter = false;
    int      counterIndex = 0;       // 1-based
    int      counterTotal = 0;
    bool     showChords = false;     // cifrado visible en pantalla
    bool     lowerThird = false;     // superposicion inferior
    wxString lowerTitle;
    wxString lowerText;
    wxString highlight;              // palabras resaltadas (separadas por espacio)
};

class Renderer
{
public:
    // Renderiza una slide al tamano fisico (w x h px)
    static wxBitmap Render(const Slide &slide, const Theme &theme, int w, int h,
                           const RenderOpts &opts = {});

    // Fondo del tema solo (sin contenido) — para modos limpiar/logo
    static wxBitmap RenderBackground(const Theme &theme, int w, int h);

    // Contraste WCAG (1..21) y nivel ('ok' | 'warn' | 'err')
    static double ContrastRatio(const wxColour &a, const wxColour &b);
    static wxString ContrastLevel(const wxColour &a, const wxColour &b);

    static void ClearCache();

private:
    struct Ctx;

    static const wxImage *CachedImage(const wxString &path);
    static void DrawBackground(Ctx &c, const Theme &theme);
    static void DrawTextBlock(Ctx &c, const std::vector<SlideLine> &lines,
                              const TextStyle &style, const wxRect &box,
                              bool autoFit, int basePx, bool useHighlight = false);
    static void DrawTitleSlide(Ctx &c, const Slide &slide, const Theme &theme);
    static void DrawContentSlide(Ctx &c, const Slide &slide, const Theme &theme, bool bible);
    static void DrawAvisoSlide(Ctx &c, const Slide &slide, const Theme &theme);
    static void DrawImageSlide(Ctx &c, const Slide &slide);
    static void DrawCounter(Ctx &c, const RenderOpts &opts);
    static void DrawLowerThird(Ctx &c);

    struct Ctx
    {
        wxGraphicsContext *gc = nullptr;
        int w = 0, h = 0;
        double scale = 1.0;          // h / 1080.0
        const RenderOpts *opts = nullptr;
    };
};

#endif // LUMINA_RENDERER_H
