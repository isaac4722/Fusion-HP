// ============================================================================
//  Fusion-HP / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Renderer.cpp : implementación GDI(+GDI+) del renderizador de slides.
//  Decisiones:
//   * Todo el texto se dibuja con TextOutW (Unicode) sobre un DC doble-búfer.
//   * Contorno = silueta por offsets (8 direcciones) + sombra + texto.
//   * Ajuste tipográfico: si el bloque no cabe, se reduce el tamaño de fuente
//     escalando 0.9 hasta caber (mínimo 12 px) — herencia del ajuste wx v2.0.0.
//   * Colores "#AARRGGBB" (como el esquema de temas del repo).
//   * GDI+ solo para fondos de imagen y codificación PNG.
// ============================================================================
#include "Renderer.h"

#include "Utf8.h"

#include <windows.h>
#include <objbase.h>   // macro `interface` (objbase) — requerida por GdiplusImaging.h
#include <objidl.h>
#include <gdiplus.h>

#include <algorithm>
#include <wchar.h>
#include <cmath>
#include <memory>
#include <string>
#include <vector>

#pragma comment(lib, "gdiplus.lib")

namespace fusion {
namespace {

/* ------------------------------------------------------------- colores -- */
struct RGBA { BYTE a, r, g, b; };

RGBA ParseColor(const std::string& hex, RGBA fallback) {
    // Formato "#AARRGGBB" (9) o "#RRGGBB" (7, alpha=FF).
    std::string s = Trim(hex);
    if (!s.empty() && s[0] == '#') s = s.substr(1);
    auto hexVal = [](char c) -> int {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    };
    auto byte2 = [&](size_t i) -> int {
        if (i + 1 >= s.size()) return -1;
        const int hi = hexVal(s[i]), lo = hexVal(s[i + 1]);
        if (hi < 0 || lo < 0) return -1;
        return (hi << 4) | lo;
    };
    if (s.size() == 8) {
        const int a = byte2(0), r = byte2(2), g = byte2(4), b = byte2(6);
        if (a >= 0 && r >= 0 && g >= 0 && b >= 0)
            return RGBA{(BYTE)a, (BYTE)r, (BYTE)g, (BYTE)b};
    } else if (s.size() == 6) {
        const int r = byte2(0), g = byte2(2), b = byte2(4);
        if (r >= 0 && g >= 0 && b >= 0) return RGBA{255, (BYTE)r, (BYTE)g, (BYTE)b};
    }
    return fallback;
}

COLORREF ToColorRef(RGBA c) { return RGB(c.r, c.g, c.b); }

/* ----------------------------------------------------------- GDI+ init -- */
ULONG_PTR GdiplusToken() {
    static ULONG_PTR token = 0;
    static bool init = false;
    if (!init) {
        Gdiplus::GdiplusStartupInput input;
        if (Gdiplus::GdiplusStartup(&token, &input, nullptr) == Gdiplus::Ok) {
            // token válido
        } else {
            token = 0;
        }
        init = true;
    }
    return token;
}

int PngEncoderClsid(CLSID* out) {
    static CLSID clsid = {};
    static bool init = false;
    if (!init) {
        UINT n = 0, size = 0;
        Gdiplus::GetImageEncodersSize(&n, &size);
        if (n && size) {
            std::vector<BYTE> buf(size);
            Gdiplus::GetImageEncoders(n, size, reinterpret_cast<Gdiplus::ImageCodecInfo*>(buf.data()));
            for (UINT i = 0; i < n; ++i) {
                const Gdiplus::ImageCodecInfo* e =
                    reinterpret_cast<const Gdiplus::ImageCodecInfo*>(buf.data()) + i;
                if (wcscmp(e->MimeType, L"image/png") == 0) { clsid = e->Clsid; break; }
            }
        }
        init = true;
    }
    if (out) *out = clsid;
    return clsid.Data1 != 0 ? 1 : 0;
}

struct ImageGuard {
    Gdiplus::Image* img = nullptr;
    ~ImageGuard() { delete img; }
};

// Carga imagen (ruta UTF-8). Devuelve nullptr si falla.
Gdiplus::Image* LoadImageUtf8(const std::string& pathUtf8) {
    if (GdiplusToken() == 0) return nullptr;
    const std::wstring w = Utf8ToWide(pathUtf8);
    if (w.empty()) return nullptr;
    Gdiplus::Image* img = Gdiplus::Image::FromFile(w.c_str());
    if (!img || img->GetLastStatus() != Gdiplus::Ok) { delete img; return nullptr; }
    return img;
}

/* Dibuja imagen con modo cover/contain centrada (fuente ImageGuard externa). */
void DrawImageFit(HDC hdc, int w, int h, Gdiplus::Image* img, int mode) {
    if (!img) return;
    Gdiplus::Graphics g(hdc);
    g.SetInterpolationMode(Gdiplus::InterpolationModeHighQualityBicubic);
    const double iw = (double)img->GetWidth(), ih = (double)img->GetHeight();
    if (iw <= 0 || ih <= 0) return;
    double scale = (mode == 1) ? std::max(w / iw, h / ih)      // cover
                               : std::min(w / iw, h / ih);     // contain
    const double dw = iw * scale, dh = ih * scale;
    const double dx = (w - dw) / 2.0, dy = (h - dh) / 2.0;
    g.DrawImage(img, (Gdiplus::REAL)dx, (Gdiplus::REAL)dy,
                (Gdiplus::REAL)dw, (Gdiplus::REAL)dh);
}

/* ------------------------------------------------------------- texto ---- */
struct TextLine {
    std::wstring text;
    std::wstring chords;
};

int TextWidth(HDC hdc, const std::wstring& s) {
    SIZE sz = {0, 0};
    GetTextExtentPoint32W(hdc, s.c_str(), (int)s.size(), &sz);
    return sz.cx;
}

int TextHeight(HDC hdc, const std::wstring& s) {
    SIZE sz = {0, 0};
    GetTextExtentPoint32W(hdc, s.c_str(), (int)s.size(), &sz);
    return sz.cy;
}

// Silueta: dibuja el texto en 8 direcciones con outlineColor (simple y rápido).
void DrawOutlined(HDC hdc, int x, int y, const std::wstring& s,
                  COLORREF outline, double outlineWidth, COLORREF fill,
                  COLORREF shadow, int shadowOffset) {
    SetBkMode(hdc, TRANSPARENT);
    // Sombra
    SetTextColor(hdc, shadow);
    TextOutW(hdc, x + shadowOffset, y + shadowOffset, s.c_str(), (int)s.size());
    // Contorno (silueta en 8 direcciones; grosor ≥1 px)
    SetTextColor(hdc, outline);
    const int o = std::max(1, (int)std::lround(outlineWidth));
    for (int dx = -o; dx <= o; dx += o) {
        for (int dy = -o; dy <= o; dy += o) {
            if (dx == 0 && dy == 0) continue;
            TextOutW(hdc, x + dx, y + dy, s.c_str(), (int)s.size());
        }
    }
    // Texto principal
    SetTextColor(hdc, fill);
    TextOutW(hdc, x, y, s.c_str(), (int)s.size());
}

HFONT MakeFont(const Theme& theme, int pxHeight, bool bold) {
    const std::wstring face = Utf8ToWide(theme.fontFace.empty()
                                             ? std::string("Segoe UI")
                                             : theme.fontFace);
    return CreateFontW(-pxHeight, 0, 0, 0, bold ? FW_BOLD : FW_NORMAL,
                       FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                       OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                       CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_DONTCARE,
                       face.c_str());
}

} // namespace

/* --------------------------------------------------------- DrawSlide ---- */

void Renderer::DrawBackground(HDC hdc, int w, int h, const Theme& theme) {
    const RGBA bg = ParseColor(theme.bgColor, RGBA{255, 11, 31, 42});
    HBRUSH brush = CreateSolidBrush(ToColorRef(bg));
    RECT rc = {0, 0, w, h};
    FillRect(hdc, &rc, brush);
    DeleteObject(brush);
    if (!theme.imagePath.empty()) {
        ImageGuard img;
        img.img = LoadImageUtf8(theme.imagePath);
        if (img.img) DrawImageFit(hdc, w, h, img.img, theme.imageMode);
    }
}

void Renderer::DrawSlide(HDC hdc, int w, int h,
                         const Slide* slide, const Theme& theme,
                         bool black, bool showCounter, int counter) {
    if (black) {
        RECT rc = {0, 0, w, h};
        HBRUSH b = CreateSolidBrush(RGB(0, 0, 0));
        FillRect(hdc, &rc, b);
        DeleteObject(b);
        return;
    }
    DrawBackground(hdc, w, h, theme);
    if (!slide || slide->kind == SLIDE_BLANK || slide->lines.empty())
        return;

    const RGBA fg = ParseColor(theme.fgColor, RGBA{255, 255, 255, 255});
    const RGBA ac = ParseColor(theme.accentColor, RGBA{255, 58, 166, 185});
    const COLORREF fill = ToColorRef(fg);
    const COLORREF outline = ToColorRef(ac);
    const COLORREF shadow = RGB(0, 0, 0);

    // Líneas visibles (uppercase opcional del tema, solo ASCII — determinista).
    std::vector<std::wstring> lines;
    for (const SlideLine& l : slide->lines) {
        std::string t = l.text;
        if (theme.uppercase) {
            for (size_t i = 0; i < t.size(); ++i)
                if (t[i] >= 'a' && t[i] <= 'z') t[i] = (char)(t[i] - 'a' + 'A');
        }
        lines.push_back(Utf8ToWide(t));
    }

    // Tamaño base de fuente escalado a la altura del lienzo (1080p nominal).
    int fontPx = std::max(14, (int)std::lround(theme.fontSize * (h / 1080.0)));
    const int maxBlockH = (int)(h * 0.78);
    const int gapBase = std::max(6, fontPx / 4);

    // Ajuste tipográfico: reduce 10% hasta caber.
    int totalH = 0;
    HFONT font = nullptr;
    for (int iter = 0; iter < 24; ++iter) {
        if (font) DeleteObject(font);
        font = MakeFont(theme, fontPx, theme.bold || slide->kind == SLIDE_TITLE);
        HGDIOBJ old = SelectObject(hdc, font);
        (void)old;
        totalH = 0;
        int maxW = 0;
        for (const std::wstring& ln : lines) {
            totalH += TextHeight(hdc, ln);
            maxW = std::max(maxW, TextWidth(hdc, ln));
        }
        totalH += gapBase * (int)(lines.size() > 1 ? lines.size() - 1 : 0);
        SelectObject(hdc, GetStockObject(SYSTEM_FONT));
        if (totalH <= maxBlockH && maxW <= (int)(w * 0.9)) break;
        fontPx = (int)std::lround(fontPx * 0.9);
        if (fontPx < 12) { fontPx = 12; font = MakeFont(theme, fontPx, theme.bold); break; }
    }
    if (!font) font = MakeFont(theme, fontPx, theme.bold);

    HGDIOBJ oldFont = SelectObject(hdc, font);
    SetTextAlign(hdc, TA_LEFT | TA_TOP);

    const int y0 = (h - totalH) / 2;
    const int shadowOffset = std::max(2, fontPx / 16);
    const int gap = std::max(6, fontPx / 4);
    int y = y0;
    for (const std::wstring& ln : lines) {
        const int tw = TextWidth(hdc, ln);
        const int x = (w - tw) / 2;
        DrawOutlined(hdc, x, y, ln, outline,
                     theme.outlineWidth * (fontPx / 54.0), fill, shadow, shadowOffset);
        y += TextHeight(hdc, ln) + gap;
    }
    SelectObject(hdc, oldFont);
    DeleteObject(font);

    // Etiqueta de referencia (arriba-izquierda, color de acento).
    if (!slide->refLabel.empty()) {
        HFONT fRef = MakeFont(theme, std::max(14, fontPx / 3), true);
        HGDIOBJ o = SelectObject(hdc, fRef);
        const std::wstring ref = Utf8ToWide(slide->refLabel);
        SetBkMode(hdc, TRANSPARENT);
        SetTextColor(hdc, fill);
        TextOutW(hdc, 24, 20, ref.c_str(), (int)ref.size());
        SelectObject(hdc, o);
        DeleteObject(fRef);
    }
    // Título (abajo-derecha, discreto).
    if (!slide->title.empty()) {
        HFONT fT = MakeFont(theme, std::max(14, fontPx / 3), false);
        HGDIOBJ o = SelectObject(hdc, fT);
        const std::wstring t = Utf8ToWide(slide->title);
        SetBkMode(hdc, TRANSPARENT);
        SetTextColor(hdc, fill);
        const int tw = TextWidth(hdc, t);
        TextOutW(hdc, w - tw - 24, h - std::max(34, fontPx / 3) - 20,
                 t.c_str(), (int)t.size());
        SelectObject(hdc, o);
        DeleteObject(fT);
    }
    // Contador de slide opcional.
    if (showCounter) {
        HFONT fC = MakeFont(theme, std::max(14, fontPx / 3), false);
        HGDIOBJ o = SelectObject(hdc, fC);
        wchar_t buf[32];
        _snwprintf(buf, 31, L"%d", counter);
        buf[31] = 0;
        SetBkMode(hdc, TRANSPARENT);
        SetTextColor(hdc, ToColorRef(ac));
        TextOutW(hdc, 24, h - std::max(34, fontPx / 3) - 20, buf, (int)wcslen(buf));
        SelectObject(hdc, o);
        DeleteObject(fC);
    }
}

/* ---------------------------------------------------------- PNG encode -- */

bool Renderer::EncodePng(HBITMAP bmp, std::string* pngOut) {
    if (!pngOut) return false;
    pngOut->clear();
    if (GdiplusToken() == 0) return false;
    CLSID pngClsid;
    if (!PngEncoderClsid(&pngClsid)) return false;

    Gdiplus::Bitmap bitmap(bmp, nullptr);
    if (bitmap.GetLastStatus() != Gdiplus::Ok) return false;

    IStream* stream = nullptr;
    if (CreateStreamOnHGlobal(nullptr, TRUE, &stream) != S_OK) return false;

    const bool ok = bitmap.Save(stream, &pngClsid, nullptr) == Gdiplus::Ok;
    if (!ok) { stream->Release(); return false; }

    STATSTG st = {0};
    if (stream->Stat(&st, STATFLAG_NONAME) != S_OK || st.cbSize.QuadPart == 0) {
        stream->Release();
        return false;
    }
    const SIZE_T n = (SIZE_T)st.cbSize.QuadPart;
    HGLOBAL h = nullptr;
    if (GetHGlobalFromStream(stream, &h) != S_OK || !h) { stream->Release(); return false; }
    void* data = GlobalLock(h);
    if (!data) { stream->Release(); return false; }
    pngOut->assign(reinterpret_cast<const char*>(data), n);
    GlobalUnlock(h);
    stream->Release();
    return true;
}

} // namespace fusion
