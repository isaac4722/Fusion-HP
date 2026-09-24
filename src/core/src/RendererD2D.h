// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  RendererD2D.h : ruta Direct2D del render base (F0.06.1 del plan —
//  «Implementar ruta Direct2D cuando esté disponible», doc técnico §6.3).
//
//  CONTRATO (F0.06):
//   * Sondea en RUNTIME (D2D1CreateFactory + DWriteCreateFactory + WIC):
//     CreateAvailable(). Si D2D/DWrite/WIC no está disponible devuelve false
//     y el motor conserva la ruta GDI+ existente (Renderer.cpp) — degradación
//     controlada, JAMÁS silenciosa: registra INFO en NativeLog
//     («renderer: d2d no disponible → gdi+», §10.1).
//   * Render de un frame completo a ID2D1HwndRenderTarget (proyección,
//     BeginDraw/EndDraw = double buffering estricto de D2D; la ventana usa
//     WS_EX_NOREDIRECTIONBITMAP cuando el SO lo tiene — F0.06.4, Projector)
//     o a un bitmap WIC codificado PNG para selftest/CI sin ventana.
//   * Orden de composición §6.4: fondo → contenido (imagen/texto) — el Lower
//     Third y los indicadores siguen siendo capases del Projector.
//   * Texto multi-línea con IDWriteFactory: fuente/tamaño/color/alineación/
//     sombra discreta (F1.01) y línea activa con estilo configurable
//     (color de texto + fondo tenue, F1.01 dimInactive/activeLineColor).
//   * Imagen vía WIC (JPG/PNG/GIF/BMP/TIF) con contain/cover y opacidad.
//   * Medición por frame (LastFrameMs) para F0.06.7/F6.01.
//
//  PUERTAS DE COMPILACIÓN:
//   * Win32-only (LUMINA_HAS_WIN32) y controlado por la propiedad MSBuild
//     <EnableD2D> (define LUMINA_HAS_D2D=1; por defecto true en Windows).
//     Sin LUMINA_HAS_D2D el módulo compila a vacío (0 líneas de código).
//   * Sin medios: el video sigue en DirectShow (VideoDS.cpp) — no cambia.
// ============================================================================
#ifndef LUMINA_RENDERER_D2D_H
#define LUMINA_RENDERER_D2D_H

#include "NativeLog.h"

#if defined(LUMINA_HAS_WIN32) && defined(LUMINA_HAS_D2D)

#include <windows.h>

#include <memory>
#include <string>
#include <vector>

namespace lumina {

class RendererD2D {
public:
    /* ------------------------------- parámetros de frame ----------------- */
    struct Background {
        bool        gradient = false;      // false = sólido; true = gradiente vertical
        std::string colorA   = "#FF0B1F2A"; // #AARRGGBB (arriba / sólido)
        std::string colorB   = "#FF06131C"; // abajo (solo gradiente)
        std::string imagePath;              // fondo de imagen opcional
        bool        imageCover = true;      // 0=contain 1=cover (contrato Theme)
        float       imageOpacity = 1.0f;
    };

    struct TextStyle {
        std::string fontFace   = "Segoe UI";
        float       fontSizePx = 54.0f;    // px @1080p lógico (contrato Theme)
        std::string color      = "#FFFFFFFF";
        int         align      = 1;        // 0=izq 1=centro 2=der (contrato Models)
        int         shadowAlpha= 140;     // 0 = sin sombra
        float       lineSpacing= 1.18f;
        // Línea activa (F1.01): índice dentro de 'lines'; -1 = clásico.
        int         activeLine      = -1;
        std::string activeLineColor;       // "" = usa 'color'
        bool        activeLineBold  = false;
    };

    struct Frame {
        Background background;
        std::vector<std::string> lines;    // líneas visibles UTF-8 (vacío = solo fondo)
        TextStyle style;
        // Contenido opcional de imagen (canvas completo, contain/cover).
        std::string imagePath;
        bool  imageCover   = false;
        float imageOpacity = 1.0f;
        // Indicador discreto de contador de slide (≤0 = oculto, F0.06.6).
        int counter = 0;
    };

    RendererD2D();
    ~RendererD2D();

    // Sondea y crea las factories D2D/DWrite/WIC (una vez por proceso).
    // Nunca silencioso: si falla, escribe INFO en 'log' («renderer: d2d no
    // disponible → gdi+») y devuelve false. log puede ser null (selftest).
    static bool CreateAvailable(nlog::Log* log = nullptr);

    // Las factories de la última CreateAvailable() están listas.
    static bool Available();

    // Frame completo sobre la ventana de proyección (target HWND cacheado;
    // recrea el render target si el HWND/tamaño cambia o D2DERR_RECREATE_…).
    bool RenderToHwnd(HWND hwnd, const Frame& frame);

    // Selftest/CI sin ventana: renderiza a un bitmap WIC (CPU) y devuelve el
    // PNG completo. false = fallo D2D/WIC (el llamador hace fallback GDI+).
    bool RenderToPng(int width, int height, const Frame& frame,
                     std::string* pngOut);

    // F0.06.7/F6.01: duración del último DrawFrame en milisegundos.
    double LastFrameMs() const;

private:
    struct Impl;                       // tipos d2d1/dwrite/wic ocultos
    std::unique_ptr<Impl> impl_;
};

} // namespace lumina
#endif // LUMINA_HAS_WIN32 && LUMINA_HAS_D2D
#endif // LUMINA_RENDERER_D2D_H
