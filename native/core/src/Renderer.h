// ============================================================================
//  Fusion-HP / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Renderer.h : renderizador GDI de slides (solo Windows). Dibuja fondo
//  (color/imagen), texto con sombra+contorno y ajuste tipográfico, etiqueta
//  de referencia y título. Es LA fuente única de verdad visual: la ventana de
//  proyección y las vistas previas PNG usan el mismo código.
// ============================================================================
#ifndef FUSION_RENDERER_H
#define FUSION_RENDERER_H

#ifdef FUSION_HAS_WIN32

#include "Models.h"

#include <windows.h>

namespace fusion {

class Renderer {
public:
    // Dibuja una slide completa en hdc (w x h = píxeles físicos del destino).
    // slide.kind == SLIDE_BLANK o sin líneas → solo fondo (clear).
    static void DrawSlide(HDC hdc, int w, int h,
                          const Slide* slide, const Theme& theme,
                          bool black, bool showCounter = false, int counter = 0);

    // Fondo puro (tema): color sólido o imagen (cover/contain).
    static void DrawBackground(HDC hdc, int w, int h, const Theme& theme);

    // PNG (GDI+). bgra = 32bpp BGRA pre-multiplicado no requerido (Alpha=FF).
    static bool EncodePng(HBITMAP bmp, std::string* pngOut);
};

} // namespace fusion
#endif // FUSION_HAS_WIN32
#endif // FUSION_RENDERER_H
