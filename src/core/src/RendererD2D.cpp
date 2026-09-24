// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  RendererD2D.cpp : implementación de la ruta Direct2D (F0.06.1).
//  Depende SOLO del SDK de Windows (d2d1.h, dwrite.h, wincodec.h). Sin
//  excepciones hacia el llamador: HRESULT → bool. Degradación a GDI+
//  anunciada por NativeLog (nunca un fallo silencioso).
// ============================================================================
#include "RendererD2D.h"

#if defined(LUMINA_HAS_WIN32) && defined(LUMINA_HAS_D2D)

#include <d2d1.h>
#include <dwrite.h>
#include <wincodec.h>
#include <objbase.h>

#include <mutex>

// ComPtr del SDK de Windows: RAII sin dependencias externas.
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;

namespace lumina {

namespace {

/* ------------------------------------------------------------- color --- */
// "#AARRGGBB" (contrato Theme/Models) → D2D1_COLOR_F. No lanza.
D2D1_COLOR_F ParseColor(const std::string& hex, D2D1_COLOR_F fallback) {
    if (hex.size() != 9 || hex[0] != '#') return fallback;
    auto nib = [](char c) -> int {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    };
    auto byte = [&](int i) -> int {
        const int hi = nib(hex[i]), lo = nib(hex[i + 1]);
        if (hi < 0 || lo < 0) return -1;
        return hi * 16 + lo;
    };
    const int a = byte(1), r = byte(3), g = byte(5), b = byte(7);
    if (a < 0 || r < 0 || g < 0 || b < 0) return fallback;
    return D2D1::ColorF(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
}

D2D1_COLOR_F WithAlpha(D2D1_COLOR_F c, float alpha) {
    c.a = alpha;
    return c;
}

/* --------------------------------------------------------------- utf8 --- */
std::wstring ToWide(const std::string& utf8) {
    if (utf8.empty()) return std::wstring();
    const int n = MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(),
                                      (int)utf8.size(), nullptr, 0);
    std::wstring w((size_t)(n > 0 ? n : 0), L'\0');
    if (n > 0)
        MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), (int)utf8.size(), &w[0], n);
    return w;
}

/* ----------------------------------------------- factories de proceso --- */
// D2D1_FACTORY_TYPE_SINGLE_THREADED: TODO el render ocurre en el hilo de
// render del Projector (misma disciplina que la ruta GDI+ — F0.06).
std::mutex               g_probeMx;
bool                     g_probed    = false;
bool                     g_available = false;
ComPtr<ID2D1Factory>     g_d2d;
ComPtr<IDWriteFactory>   g_dwrite;
ComPtr<IWICImagingFactory> g_wic;

bool ProbeFactories() {
    std::lock_guard<std::mutex> lk(g_probeMx);
    if (g_probed) return g_available;
    g_probed = true;

    // WIC (CoCreateInstance) necesita COM en este hilo. El host de render
    // normalmente ya inicializó COM; si no, esta llamada inicializa y queda
    // viva para la sesión del hilo (emparejada con el ciclo de vida del
    // proceso de render — no se desinicializa a medias).
    HRESULT hrCo = CoInitializeEx(nullptr,
                                  COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
    if (hrCo == RPC_E_CHANGED_MODE) {
        // El hilo ya tiene COM en otro modelo: suficiente para WIC.
    }

    D2D1_FACTORY_OPTIONS fo;
    ZeroMemory(&fo, sizeof(fo));
    HRESULT hr = D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,
                                   __uuidof(ID2D1Factory), &fo,
                                   reinterpret_cast<void**>(g_d2d.GetAddressOf()));
    if (FAILED(hr)) return false;

    hr = DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
                             reinterpret_cast<IUnknown**>(g_dwrite.GetAddressOf()));
    if (FAILED(hr)) return false;

    hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
                          __uuidof(IWICImagingFactory),
                          reinterpret_cast<void**>(g_wic.GetAddressOf()));
    if (FAILED(hr)) return false;

    g_available = true;
    return true;
}

/* --------------------------------------------------------- utilidades --- */
DWRITE_TEXT_ALIGNMENT AlignOf(int align) {
    switch (align) {
        case 0:  return DWRITE_TEXT_ALIGNMENT_LEADING;
        case 2:  return DWRITE_TEXT_ALIGNMENT_TRAILING;
        default: return DWRITE_TEXT_ALIGNMENT_CENTER;
    }
}

float SafeFloat(double v, float lo, float hi, float def) {
    if (!(v == v)) return def;                       // NaN
    float f = (float)v;
    if (f < lo) f = lo;
    if (f > hi) f = hi;
    return f;
}

} // namespace

/* ============================================================ Impl ===== */

struct RendererD2D::Impl {
    // Recursos por target (se recrean ante D2DERR_RECREATE_TARGET).
    HWND                       hwnd      = nullptr;
    UINT                       rtWidth   = 0;
    UINT                       rtHeight  = 0;
    ComPtr<ID2D1HwndRenderTarget> rt;

    // Cachés de frame (clave simple: el frame cambia poco entre slides).
    ComPtr<IDWriteTextFormat>  format;
    std::string                formatKey;
    ComPtr<ID2D1Bitmap>        bgBitmap;
    std::string                bgBitmapPath;
    ComPtr<ID2D1Bitmap>        fgBitmap;
    std::string                fgBitmapPath;

    LARGE_INTEGER perfFreq{};              // F0.06.7
    double        lastFrameMs = 0.0;

    Impl() { QueryPerformanceFrequency(&perfFreq); }

    /* ------------------------------------------------------------ RT ----- */
    HRESULT EnsureRenderTarget(HWND targetHwnd, UINT w, UINT h) {
        if (rt && hwnd == targetHwnd && rtWidth == w && rtHeight == h)
            return S_OK;
        rt.Reset();
        hwnd = targetHwnd;
        D2D1_SIZE_U size = D2D1::SizeU(w, h);
        // PIXEL_FORMAT_PREMULTIPLIED: requerido por D2D (transparencia correcta).
        HRESULT hr = g_d2d->CreateHwndRenderTarget(
            D2D1::RenderTargetProperties(
                D2D1_RENDER_TARGET_TYPE_DEFAULT,
                D2D1::PixelFormat(DXGI_FORMAT_UNKNOWN, D2D1_ALPHA_MODE_PREMULTIPLIED)),
            D2D1::HwndRenderTargetProperties(targetHwnd, size,
                                             D2D1_PRESENT_OPTIONS_NONE),
            &rt);
        if (FAILED(hr)) return hr;
        rtWidth = w; rtHeight = h;
        format.Reset(); formatKey.clear();      // formatos son por-RT (D2D1)
        bgBitmap.Reset(); fgBitmap.Reset();     // bitmaps son por-RT
        bgBitmapPath.clear(); fgBitmapPath.clear();
        return S_OK;
    }

    /* ------------------------------------------------------- imagen ----- */
    // Decodifica JPG/PNG/GIF/BMP/TIF vía WIC → ID2D1Bitmap (PBGRA).
    HRESULT LoadImageBitmap(ID2D1RenderTarget* target, const std::string& pathUtf8,
                            ComPtr<ID2D1Bitmap>* out) {
        out->Reset();
        const std::wstring wpath = ToWide(pathUtf8);
        if (wpath.empty()) return E_INVALIDARG;
        ComPtr<IWICBitmapDecoder> dec;
        HRESULT hr = g_wic->CreateDecoderFromFilename(
            wpath.c_str(), nullptr, GENERIC_READ,
            WICDecodeMetadataCacheOnDemand, &dec);
        if (FAILED(hr)) return hr;
        ComPtr<IWICBitmapFrameDecode> frame;
        hr = dec->GetFrame(0, &frame);
        if (FAILED(hr)) return hr;
        ComPtr<IWICFormatConverter> conv;
        hr = g_wic->CreateFormatConverter(&conv);
        if (FAILED(hr)) return hr;
        hr = conv->Initialize(frame.Get(), GUID_WICPixelFormat32bppPBGRA,
                              WICBitmapDitherTypeNone, nullptr, 0.0f,
                              WICBitmapPaletteTypeMedianCut);
        if (FAILED(hr)) return hr;
        return target->CreateBitmapFromWicBitmap(conv.Get(), nullptr,
                                                 out->GetAddressOf());
    }

    // Dibuja 'bmp' en destRect con contain/cover (§6.3) y opacidad.
    void DrawFitted(ID2D1RenderTarget* target, ID2D1Bitmap* bmp,
                    const D2D1_RECT_F& dest, bool cover, float opacity) {
        D2D1_SIZE_F src = bmp->GetSize();
        if (src.width <= 0 || src.height <= 0) return;
        const float dw = dest.right - dest.left;
        const float dh = dest.bottom - dest.top;
        if (dw <= 0 || dh <= 0) return;
        const float scale = cover
            ? (dw / src.width > dh / src.height ? dw / src.width : dh / src.height)
            : (dw / src.width < dh / src.height ? dw / src.width : dh / src.height);
        const float w = src.width * scale, h = src.height * scale;
        D2D1_RECT_F r = D2D1::RectF(
            dest.left + (dw - w) * 0.5f, dest.top + (dh - h) * 0.5f,
            dest.left + (dw - w) * 0.5f + w, dest.top + (dh - h) * 0.5f + h);
        target->DrawBitmap(bmp, r, SafeFloat(opacity, 0.0f, 1.0f, 1.0f),
                           D2D1_BITMAP_INTERPOLATION_MODE_LINEAR);
    }

    /* -------------------------------------------------------- texto ----- */
    HRESULT EnsureFormat(ID2D1RenderTarget* target, const RendererD2D::TextStyle& st) {
        std::string key = st.fontFace + "|" +
                          std::to_string((int)st.fontSizePx) + "|" +
                          (st.activeLineBold ? "b" : "n");
        if (format && formatKey == key) return S_OK;
        format.Reset();
        const std::wstring face = ToWide(st.fontFace);
        HRESULT hr = g_dwrite->CreateTextFormat(
            face.c_str(), nullptr,
            DWRITE_FONT_WEIGHT_REGULAR, DWRITE_FONT_STYLE_NORMAL,
            DWRITE_FONT_STRETCH_NORMAL, st.fontSizePx, L"es-VE", &format);
        if (FAILED(hr)) return hr;
        format->SetTextAlignment(AlignOf(st.align));
        format->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP);  // líneas pre-partidas
        formatKey = key;
        (void)target;
        return S_OK;
    }

    // Alto de línea lógico (contrato lineSpacing del tema).
    float LineHeight(const RendererD2D::TextStyle& st) const {
        return st.fontSizePx * SafeFloat(st.lineSpacing, 0.8f, 3.0f, 1.18f);
    }

    /* ------------------------------------------------------- frame ------ */
    HRESULT DrawFrame(ID2D1RenderTarget* target, UINT w, UINT h,
                      const RendererD2D::Frame& fr) {
        const float fw = (float)w, fh = (float)h;
        const D2D1_RECT_F full = D2D1::RectF(0.0f, 0.0f, fw, fh);

        /* 1) fondo (orden de composición §6.4: primero el fondo) --------- */
        const D2D1_COLOR_F bgA = ParseColor(fr.background.colorA,
                                            D2D1::ColorF(0x0B1F2A));
        if (!fr.background.imagePath.empty()) {
            if (bgBitmap && bgBitmapPath != fr.background.imagePath) {
                bgBitmap.Reset();
                bgBitmapPath.clear();
            }
            if (!bgBitmap) {
                HRESULT hr = LoadImageBitmap(target, fr.background.imagePath,
                                             &bgBitmap);
                if (SUCCEEDED(hr)) bgBitmapPath = fr.background.imagePath;
            }
            if (bgBitmap) {
                target->Clear(D2D1::ColorF(0.0f, 0.0f, 0.0f, 1.0f));
                DrawFitted(target, bgBitmap.Get(), full,
                           fr.background.imageCover,
                           fr.background.imageOpacity);
            } else {
                target->Clear(bgA);       // imagen ilegible → color base
            }
        } else if (fr.background.gradient) {
            target->Clear(bgA);
            const D2D1_COLOR_F bgB = ParseColor(fr.background.colorB, bgA);
            ComPtr<ID2D1GradientStopCollection> stops;
            D2D1_GRADIENT_STOP gs[2] = {
                { 0.0f, bgA }, { 1.0f, bgB },
            };
            HRESULT hr = target->CreateGradientStopCollection(
                gs, 2, D2D1_GAMMA_2_2, D2D1_EXTEND_MODE_CLAMP, &stops);
            if (SUCCEEDED(hr)) {
                ComPtr<ID2D1LinearGradientBrush> brush;
                hr = target->CreateLinearGradientBrush(
                    D2D1::LinearGradientBrushProperties(
                        D2D1::Point2F(0.0f, 0.0f), D2D1::Point2F(0.0f, fh)),
                    stops.Get(), &brush);
                if (SUCCEEDED(hr)) target->FillRectangle(full, brush.Get());
            }
        } else {
            target->Clear(bgA);
        }

        /* 2) contenido: imagen opcional del canvas ----------------------- */
        if (!fr.imagePath.empty()) {
            if (fgBitmap && fgBitmapPath != fr.imagePath) {
                fgBitmap.Reset();
                fgBitmapPath.clear();
            }
            if (!fgBitmap) {
                HRESULT hr = LoadImageBitmap(target, fr.imagePath, &fgBitmap);
                if (SUCCEEDED(hr)) fgBitmapPath = fr.imagePath;
            }
            if (fgBitmap)
                DrawFitted(target, fgBitmap.Get(), full, fr.imageCover,
                           fr.imageOpacity);
        }

        /* 3) texto multi-línea (F1.01) ----------------------------------- */
        if (!fr.lines.empty() && SUCCEEDED(EnsureFormat(target, fr.style))) {
            const float lh = LineHeight(fr.style);
            const float total = lh * (float)fr.lines.size();
            float y = (fh - total) * 0.5f;               // centrado vertical
            const D2D1_COLOR_F mainColor = ParseColor(fr.style.color,
                                                      D2D1::ColorF(0xFFFFFF));
            const D2D1_COLOR_F activeColor =
                fr.style.activeLineColor.empty()
                    ? mainColor
                    : ParseColor(fr.style.activeLineColor, mainColor);
            ComPtr<ID2D1SolidColorBrush> brush;
            target->CreateSolidColorBrush(mainColor, &brush);
            ComPtr<ID2D1SolidColorBrush> shadowBrush;
            const float shAlpha =
                SafeFloat(fr.style.shadowAlpha, 0.0f, 255.0f, 140.0f) / 255.0f;
            if (shAlpha > 0.0f)
                target->CreateSolidColorBrush(
                    WithAlpha(D2D1::ColorF(0x000000), shAlpha), &shadowBrush);

            for (size_t i = 0; i < fr.lines.size(); ++i) {
                const bool active = ((int)i == fr.style.activeLine);
                const std::wstring line = ToWide(fr.lines[i]);
                if (line.empty()) { y += lh; continue; }

                const D2D1_RECT_F lineRect = D2D1::RectF(0.0f, y, fw, y + lh);
                if (active) {
                    // Línea activa: fondo tenue + texto en color configurable
                    // (F1.01 activeLineColor; 25% de alpha sobre el lienzo).
                    ComPtr<ID2D1SolidColorBrush> hl;
                    target->CreateSolidColorBrush(WithAlpha(activeColor, 0.25f),
                                                  &hl);
                    if (hl) target->FillRectangle(lineRect, hl.Get());
                }
                // D2D renderiza texto directamente con DrawTextW (el par
                // TextLayout/TextRenderer de DWrite no es necesario para la
                // línea plana del proyector — menos asignaciones, menos DPI
                // edge cases, misma salida).
                if (shadowBrush) {
                    const float shadowDx = fr.style.fontSizePx * 0.03f;
                    const float shadowDy = fr.style.fontSizePx * 0.03f;
                    target->DrawTextW(line.c_str(), (UINT32)line.size(),
                                      format.Get(),
                                      D2D1::RectF(shadowDx, y + shadowDy,
                                                  fw + shadowDx, y + lh + shadowDy),
                                      shadowBrush.Get(),
                                      D2D1_DRAW_TEXT_OPTIONS_NONE);
                }
                if (brush) {
                    if (active) brush->SetColor(activeColor);
                    else        brush->SetColor(mainColor);
                    target->DrawTextW(line.c_str(), (UINT32)line.size(),
                                      format.Get(), lineRect, brush.Get(),
                                      D2D1_DRAW_TEXT_OPTIONS_NONE);
                }
                y += lh;
            }

            // Indicador de contador (F0.06.6, discreto: esquina inferior).
            if (fr.counter > 0) {
                const std::wstring cnt = std::to_wstring(fr.counter);
                ComPtr<ID2D1SolidColorBrush> idx;
                target->CreateSolidColorBrush(WithAlpha(mainColor, 0.55f), &idx);
                if (idx) target->DrawTextW(
                    cnt.c_str(), (UINT32)cnt.size(), format.Get(),
                    D2D1::RectF(fw - fw * 0.12f, fh - lh * 1.2f, fw, fh),
                    idx.Get(), D2D1_DRAW_TEXT_OPTIONS_NONE);
            }
        }
        return S_OK;
    }
};

/* ==================================================== RendererD2D ====== */

RendererD2D::RendererD2D() : impl_(new Impl()) {}
RendererD2D::~RendererD2D() = default;

bool RendererD2D::CreateAvailable(nlog::Log* log) {
    const bool ok = ProbeFactories();
    if (!ok && log) {
        // Degradación controlada, JAMÁS silenciosa (F0.06.1/F0.06.2).
        nlog::Entry e;
        e.severity = nlog::SEV_INFO;
        e.module   = "render";
        e.message  = "renderer: d2d no disponible \u2192 gdi+";
        log->Write(e);
    }
    return ok;
}

bool RendererD2D::Available() { return g_available; }

bool RendererD2D::RenderToHwnd(HWND hwnd, const Frame& frame) {
    if (!hwnd || !g_available || !impl_) return false;
    RECT rc;
    if (!GetClientRect(hwnd, &rc)) return false;
    const UINT w = (UINT)(rc.right - rc.left);
    const UINT h = (UINT)(rc.bottom - rc.top);
    if (w == 0 || h == 0) return false;

    HRESULT hr = impl_->EnsureRenderTarget(hwnd, w, h);
    if (FAILED(hr)) return false;

    LARGE_INTEGER t0, t1;
    QueryPerformanceCounter(&t0);
    impl_->rt->BeginDraw();                       // double buffering D2D (F0.06.3)
    impl_->DrawFrame(impl_->rt.Get(), w, h, frame);
    hr = impl_->rt->EndDraw();
    QueryPerformanceCounter(&t1);
    impl_->lastFrameMs =
        impl_->perfFreq.QuadPart > 0
            ? (double)(t1.QuadPart - t0.QuadPart) * 1000.0
              / (double)impl_->perfFreq.QuadPart
            : 0.0;

    if (hr == D2DERR_RECREATE_TARGET) {
        // Surface perdida (cambio de dispositivo/monitor): la próxima llamada
        // recrea (F0.06.8 — fallo registrado por el Projector).
        impl_->rt.Reset();
        return false;
    }
    return SUCCEEDED(hr);
}

bool RendererD2D::RenderToPng(int width, int height, const Frame& frame,
                              std::string* pngOut) {
    if (pngOut) pngOut->clear();
    if (!g_available || !impl_ || width <= 0 || height <= 0 ||
        width > 8192 || height > 8192)
        return false;

    // Target de CPU (WIC bitmap): selftest reproducible sin ventana.
    ComPtr<IWICBitmap> bmp;
    HRESULT hr = g_wic->CreateBitmap((UINT)width, (UINT)height,
                                     GUID_WICPixelFormat32bppPBGRA,
                                     WICBitmapCacheOnDemand, &bmp);
    if (FAILED(hr)) return false;
    ComPtr<ID2D1RenderTarget> rt;
    hr = g_d2d->CreateWicBitmapRenderTarget(
        bmp.Get(),
        D2D1::RenderTargetProperties(
            D2D1_RENDER_TARGET_TYPE_DEFAULT,
            D2D1::PixelFormat(DXGI_FORMAT_UNKNOWN,
                              D2D1_ALPHA_MODE_PREMULTIPLIED)),
        &rt);
    if (FAILED(hr)) return false;

    LARGE_INTEGER t0, t1;
    QueryPerformanceCounter(&t0);
    rt->BeginDraw();
    impl_->DrawFrame(rt.Get(), (UINT)width, (UINT)height, frame);
    hr = rt->EndDraw();
    QueryPerformanceCounter(&t1);
    impl_->lastFrameMs =
        impl_->perfFreq.QuadPart > 0
            ? (double)(t1.QuadPart - t0.QuadPart) * 1000.0
              / (double)impl_->perfFreq.QuadPart
            : 0.0;
    if (FAILED(hr)) return false;

    /* Codificación PNG para el selftest (WIC encoder, memoria). */
    ComPtr<IStream> stream;
    hr = CreateStreamOnHGlobal(nullptr, TRUE, &stream);
    if (FAILED(hr)) return false;
    ComPtr<IWICBitmapEncoder> enc;
    hr = g_wic->CreateEncoder(GUID_ContainerFormatPng, nullptr, &enc);
    if (FAILED(hr)) return false;
    hr = enc->Initialize(stream.Get(), WICBitmapEncoderNoCache);
    if (FAILED(hr)) return false;
    ComPtr<IWICBitmapFrameEncode> fe;
    hr = enc->CreateNewFrame(&fe, nullptr);
    if (FAILED(hr)) return false;
    hr = fe->Initialize(nullptr);
    if (FAILED(hr)) return false;
    hr = fe->SetSize((UINT)width, (UINT)height);
    if (FAILED(hr)) return false;
    WICPixelFormatGUID pf = GUID_WICPixelFormat32bppBGRA;
    fe->SetPixelFormat(&pf);
    hr = fe->WriteSource(bmp.Get(), nullptr);
    if (FAILED(hr)) return false;
    hr = fe->Commit();
    if (FAILED(hr)) return false;
    hr = enc->Commit();
    if (FAILED(hr)) return false;

    HGLOBAL g = nullptr;
    hr = GetHGlobalFromStream(stream.Get(), &g);
    if (FAILED(hr) || !g) return false;
    SIZE_T sz = GlobalSize(g);
    void*  p  = GlobalLock(g);
    if (!p || !pngOut) { if (p) GlobalUnlock(g); return pngOut != nullptr; }
    pngOut->assign((const char*)p, (size_t)sz);
    GlobalUnlock(g);
    return true;
}

double RendererD2D::LastFrameMs() const {
    return impl_ ? impl_->lastFrameMs : 0.0;
}

} // namespace lumina

#endif // LUMINA_HAS_WIN32 && LUMINA_HAS_D2D
