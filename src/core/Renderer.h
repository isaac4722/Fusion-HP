// ============================================================================
//  Fusion-HP · Renderer — pipeline de salida [SPEC §6.3, §6.4]
//  Direct2D con fallback GDI+ (doble buffer estricto). Capas fijas:
//  fondo (caché) → contenido → superposición. La ventana nunca se destruye
//  entre elementos; la conmutación es redraw de un frame completo compuesto
//  en superficie fuera de pantalla → sin frame negro intermedio.
// ============================================================================
#pragma once
#include "Common.h"
#include "SlideState.h"

// Direct2D / DirectWrite (Win7+)
#include <d2d1.h>
#include <d2d1helper.h>
#include <dwrite.h>
#pragma comment(lib, "d2d1.lib")
#pragma comment(lib, "dwrite.lib")

// WIC para decodificar imágenes
#include <wincodec.h>
#pragma comment(lib, "windowscodecs.lib")

// GDI+ (fallback y métrica secundaria)
#include <gdiplus.h>
#pragma comment(lib, "gdiplus.lib")

namespace fusion {

class Renderer {
public:
    Renderer();
    ~Renderer();

    bool Init(HWND hwnd);
    void Resize();
    void Render(const Slide& s, BlankMode blank);
    bool UsingD2D() const { return usingD2D_; }

    /// Decodifica una imagen en la caché sin dibujar (carga diferida [SPEC §6.4]).
    void PreloadImage(const std::wstring& path);

    // Vista de retorno (Stage View) [SPEC §8.5]: letra + línea activa + reloj
    void RenderStage(const Slide& s);

    void SetLogoPath(const std::wstring& p) { logoPath_ = p; }

private:
    HWND hwnd_ = nullptr;
    bool usingD2D_ = false;

    // --- D2D ---
    ID2D1Factory* d2d_ = nullptr;
    ID2D1HwndRenderTarget* rt_ = nullptr;
    IDWriteFactory* dw_ = nullptr;
    IWICImagingFactory* wic_ = nullptr;
    void RenderD2D(const Slide& s, BlankMode blank);
    ID2D1Bitmap* LoadBitmapD2D(const std::wstring& path);   // con caché por ruta
    void DrawTextLinesD2D(const Slide& s, D2D1_SIZE_F size, bool stageMode);
    void DrawLowerThirdD2D(const Slide& s, D2D1_SIZE_F size);
    double FitFontSizeD2D(const Slide& s, D2D1_SIZE_F box) const;

    // --- GDI+ fallback ---
    bool InitGdiplus();
    void RenderGdip(const Slide& s, BlankMode blank);
    void DrawTextLinesGdip(Gdiplus::Graphics& g, const Slide& s, int w, int h);
    Gdiplus::Bitmap* LoadBitmapGdip(const std::wstring& path); // con caché
    Gdiplus::Bitmap* ComposeBackgroundGdip(const BackgroundStyle& bg, int w, int h);

    // --- utilidades comunes ---
    void FillBlack();
    bool LoadLogo();

    struct CachedBitmap {
        std::wstring path;
        ID2D1Bitmap* d2d = nullptr;        // dueño: caché del renderer
        Gdiplus::Bitmap* gdip = nullptr;
    };
    std::vector<CachedBitmap> cache_;
    ID2D1Bitmap* FindCacheD2D(const std::wstring& p);
    Gdiplus::Bitmap* FindCacheGdip(const std::wstring& p);

    ULONG_PTR gdipToken_ = 0;
    std::wstring logoPath_;
    Gdiplus::Bitmap* logoGdip_ = nullptr;
    ID2D1Bitmap* logoD2D_ = nullptr;
};

// Conversión color packed → D2D1_COLOR_F
inline D2D1_COLOR_F ToD2D(uint32_t c) {
    return D2D1::ColorF(((c >> 16) & 0xFF) / 255.0f, ((c >> 8) & 0xFF) / 255.0f,
                        (c & 0xFF) / 255.0f, ((c >> 24) & 0xFF) / 255.0f);
}

} // namespace fusion
