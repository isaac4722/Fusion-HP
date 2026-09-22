// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Projector.h : ventana(s) de proyección nativas Win32 + hilo propio
//  (solo Windows). El contenido lo publica el Engine vía SetContent() y la
//  ventana repinta con el Renderer GDI. La vista previa PNG se genera con el
//  MISMO renderizador (fuente única de verdad visual).
// ============================================================================
#ifndef LUMINA_PROJECTOR_H
#define LUMINA_PROJECTOR_H

#ifdef LUMINA_HAS_WIN32

#include "Models.h"

#include <windows.h>

#include <string>
#include <vector>

namespace lumina {

class Projector {
public:
    Projector();
    ~Projector();
    Projector(const Projector&) = delete;
    Projector& operator=(const Projector&) = delete;

    // Muestra la ventana de proyección. screenIndex: 0 = primaria,
    // 1..n = monitores secundarios (EnumDisplayMonitors), -1 = ventana
    // de prueba (no fullscreen). fullscreen=true → popup en ese monitor.
    bool Show(int screenIndex, bool fullscreen);
    void Hide();                                    // destruye la ventana
    void Close();                                   // cierre definitivo (hilo)

    // Publica contenido (COPIA — thread-safe; el hilo de ventana repinta).
    void SetContent(const std::vector<Slide>& slides,
                    const std::vector<std::string>& itemTitles,
                    const Theme& theme, int current, bool black);

    // Renderiza una slide a PNG con el mismo renderizador (preview).
    static bool RenderSlidePng(const Slide& s, const Theme& t,
                               int w, int h, std::string* pngOut);

private:
    struct Impl;
    Impl* impl_;

    void WindowLoop();
    void PaintInto(HDC hdc, int w, int h);
    static LRESULT CALLBACK WndProcThunk(HWND hwnd, UINT msg,
                                         WPARAM wp, LPARAM lp);
};

} // namespace lumina
#endif // LUMINA_HAS_WIN32
#endif // LUMINA_PROJECTOR_H
