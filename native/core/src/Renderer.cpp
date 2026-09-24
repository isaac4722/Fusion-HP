// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
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

#include "Highlight.h"
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

namespace lumina {
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

// v6.0.0 «HORIZONTE»: dibuja una LÍNEA con resaltado — segmento por segmento,
// centrando el conjunto igual que una línea simple. Los segmentos que
// coinciden con la palabra buscada se pintan con relleno de ACENTO (el resto
// conserva el relleno de primer plano); contorno/sombra idénticos por
// segmento. Mismo estilo visual del tema, énfasis solo por color.
void DrawLineWithHighlight(HDC hdc, int w, int y, const std::string& lnUtf8,
                            const std::string& highlightUtf8,
                            COLORREF outline, double outlineWidth, COLORREF fill,
                            COLORREF accent, COLORREF shadow, int shadowOffset) {
    std::vector<HlSegment> segs = Highlight::Split(lnUtf8, highlightUtf8);
    std::vector<int> widths(segs.size(), 0);
    int totalW = 0;
    for (size_t i = 0; i < segs.size(); ++i) {
        widths[i] = TextWidth(hdc, Utf8ToWide(segs[i].text));
        totalW += widths[i];
    }
    int x = (w - totalW) / 2;
    for (size_t i = 0; i < segs.size(); ++i) {
        const std::wstring sw = Utf8ToWide(segs[i].text);
        if (!sw.empty()) {
            DrawOutlined(hdc, x, y, sw, outline, outlineWidth,
                         segs[i].match ? accent : fill, shadow, shadowOffset);
        }
        x += widths[i];
    }
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

/* ------------------------------------------------- v7.1.0 «OPERADOR» ---- */
/* Slide COMPUESTA (lienzo libre, feedback #1): dibuja los elementos en sus
 * rects. Texto: GDI+ DrawString con ajuste de línea, alineación y alfa por
 * brush (+ sombra discreta); tamaño auto-ajustable como el camino clásico
 * (shrink 10% hasta caber en el rect). Imagen: GDI+ con ColorMatrix para
 * opacidad y cover/contain DENTRO del rect del elemento. El fondo y el
 * negro de salida corren por el camino clásico (DrawBackground/black). */
void DrawComposedElements(HDC hdc, int w, int h,
                           const Slide* slide, const Theme& theme) {
    // v7.1.1 (lección del run 36001576255): GDI+ debe estar ARRANCADO antes
    // de crear CUALQUIER objeto (Graphics/Font/Brush/MeasureString). El
    // camino clásico de texto es GDI puro (DrawTextW) y JAMÁS inicia GDI+:
    // una slide compuesta sin imagen de fondo en el tema creaba Graphics
    // sin GdiplusStartup → comportamiento indefinido/crash (el selftest del
    // runner Windows lo demostró: única ruta de render PNG del suite).
    // GdiplusToken() lo inicia perezosamente y devuelve 0 si no se pudo.
    if (GdiplusToken() == 0) return;
    Gdiplus::Graphics g(hdc);
    if (g.GetLastStatus() != Gdiplus::Ok) return;
    g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
    g.SetTextRenderingHint(Gdiplus::TextRenderingHintClearTypeGridFit);
    const std::wstring face = Utf8ToWide(theme.fontFace.empty()
                                             ? std::string("Segoe UI")
                                             : theme.fontFace);
    const Gdiplus::REAL shadowOffset = (Gdiplus::REAL)std::max(2, h / 540);

    for (const ComposedElement& e : slide->elements) {
        Gdiplus::RectF rc((Gdiplus::REAL)(e.x * w), (Gdiplus::REAL)(e.y * h),
                          (Gdiplus::REAL)(e.w * w), (Gdiplus::REAL)(e.h * h));
        if (rc.Width <= 1 || rc.Height <= 1) continue;

        if (e.kind == 0) {                    // ---- elemento de TEXTO ----
            if (e.lines.empty()) continue;
            // Color: del elemento o del tema (fg), con alfa de opacidad.
            const RGBA base = e.color.empty()
                ? ParseColor(theme.fgColor, RGBA{255, 255, 255, 255})
                : ParseColor(e.color, RGBA{255, 255, 255, 255});
            const int alpha = (int)std::lround(std::min(1.0, e.opacity) * 255.0);

            // Líneas visibles (uppercase del tema, solo ASCII — determinista).
            std::vector<std::wstring> texts;
            for (const std::string& raw : e.lines) {
                std::string t = raw;
                if (theme.uppercase) {
                    for (size_t ci = 0; ci < t.size(); ++ci)
                        if (t[ci] >= 'a' && t[ci] <= 'z')
                            t[ci] = (char)(t[ci] - 'a' + 'A');
                }
                texts.push_back(Utf8ToWide(t));
            }

            // Alineación horizontal + centrado vertical del bloque.
            Gdiplus::StringFormat sf(Gdiplus::StringFormat::GenericTypographic());
            sf.SetFormatFlags(Gdiplus::StringFormatFlagsNoClip);
            const Gdiplus::StringAlignment sa = e.align == 0
                ? Gdiplus::StringAlignmentNear
                : (e.align == 2 ? Gdiplus::StringAlignmentFar
                                : Gdiplus::StringAlignmentCenter);
            sf.SetAlignment(sa);
            sf.SetLineAlignment(Gdiplus::StringAlignmentCenter);
            sf.SetTrimming(Gdiplus::StringTrimmingNone);

            // Tamaño: fijo (fontSizePct de la altura) o auto-ajustado al rect.
            const Gdiplus::FontStyle style = theme.bold
                ? Gdiplus::FontStyleBold : Gdiplus::FontStyleRegular;
            double fontPx = e.fontSizePct > 0
                ? (double)e.fontSizePct * (h / 100.0)
                : (double)rc.Height / (double)std::max((size_t)1, texts.size()) * 0.82;
            if (fontPx < 10) fontPx = 10;

            // Ajuste tipográfico: shrink 10% hasta caber (máx. 24 pasos).
            // Font de SONDEO por iteración (se libera sola); la definitiva se
            // construye UNA vez al final con el tamaño que quedó.
            Gdiplus::RectF box;
            for (int iter = 0; iter < 24; ++iter) {
                Gdiplus::Font probe(face.c_str(), (Gdiplus::REAL)fontPx, style,
                                    Gdiplus::UnitPixel);
                if (probe.GetLastStatus() != Gdiplus::Ok) break;
                double totalH = 0;
                Gdiplus::REAL maxW = 0;
                for (const std::wstring& ln : texts) {
                    g.MeasureString(ln.c_str(), (int)ln.size(), &probe, rc, &sf, &box);
                    totalH += (double)box.Height;
                    if (box.Width > maxW) maxW = box.Width;
                }
                if (totalH <= (double)rc.Height + 0.5 &&
                    (double)maxW <= (double)rc.Width + 0.5) break;
                fontPx *= 0.9;
                if (fontPx < 10) { fontPx = 10; break; }
            }
            Gdiplus::Font font(face.c_str(), (Gdiplus::REAL)fontPx, style,
                               Gdiplus::UnitPixel);
            if (font.GetLastStatus() != Gdiplus::Ok) continue;

            // Sombra + relleno (misma alfa del elemento; sombra al 60%).
            const int shAlpha = (int)std::lround(alpha * 0.6);
            Gdiplus::SolidBrush shadow(Gdiplus::Color((BYTE)shAlpha, 0, 0, 0));
            Gdiplus::SolidBrush fill(Gdiplus::Color((BYTE)alpha,
                (BYTE)base.r, (BYTE)base.g, (BYTE)base.b));
            Gdiplus::RectF sh = rc;
            sh.X += shadowOffset; sh.Y += shadowOffset;
            for (size_t li = 0; li < texts.size(); ++li) {
                g.DrawString(texts[li].c_str(), (int)texts[li].size(),
                             &font, sh, &sf, &shadow);
                g.DrawString(texts[li].c_str(), (int)texts[li].size(),
                             &font, rc, &sf, &fill);
            }
        } else {                               // ---- elemento de IMAGEN ----
            ImageGuard guard;
            guard.img = LoadImageUtf8(e.imagePath);
            if (!guard.img) continue;
            const double iw = (double)guard.img->GetWidth();
            const double ih = (double)guard.img->GetHeight();
            if (iw <= 0 || ih <= 0) continue;
            // Opacidad por ColorMatrix (canal alfa escalado).
            Gdiplus::ImageAttributes attr;
            Gdiplus::ColorMatrix mx = {{
                {1.0f, 0, 0, 0, 0},
                {0, 1.0f, 0, 0, 0},
                {0, 0, 1.0f, 0, 0},
                {0, 0, 0, (Gdiplus::REAL)std::max(0.0, std::min(1.0, e.opacity)), 0},
                {0, 0, 0, 0, 1.0f}
            }};
            attr.SetColorMatrix(&mx);
            // cover/contain DENTRO del rect del elemento.
            const double scale = (e.imageFit == 1)
                ? std::max((double)rc.Width / iw, (double)rc.Height / ih)
                : std::min((double)rc.Width / iw, (double)rc.Height / ih);
            const double dw = iw * scale, dh = ih * scale;
            Gdiplus::RectF dest(rc.X + (rc.Width - (Gdiplus::REAL)dw) / 2.0f,
                                rc.Y + (rc.Height - (Gdiplus::REAL)dh) / 2.0f,
                                (Gdiplus::REAL)dw, (Gdiplus::REAL)dh);
            g.SetInterpolationMode(Gdiplus::InterpolationModeHighQualityBicubic);
            g.DrawImage(guard.img, dest, 0.0f, 0.0f, (Gdiplus::REAL)iw,
                        (Gdiplus::REAL)ih, Gdiplus::UnitPixel, &attr, nullptr, nullptr);
        }
    }
}

} // namespace

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

// v7.0.0 «ULTRA» (F1.01): mezcla fg→bg para atenuar líneas inactivas (GDI
// no tiene alfa por texto: el degradado de color hacia el fondo es la vía
// determinista; con dimInactive el operador distingue la línea activa).
static COLORREF DimColor(const Theme& theme, COLORREF fg) {
    const RGBA bg = ParseColor(theme.bgColor, RGBA{255, 11, 31, 42});
    const double k = 0.42;   // 42% del color frontal
    const int r = (int)std::lround(GetRValue(fg) * k + (double)bg.r * (1.0 - k));
    const int g = (int)std::lround(GetGValue(fg) * k + (double)bg.g * (1.0 - k));
    const int b = (int)std::lround(GetBValue(fg) * k + (double)bg.b * (1.0 - k));
    return RGB(r, g, b);
}

void Renderer::DrawSlide(HDC hdc, int w, int h,
                         const Slide* slide, const Theme& theme,
                         bool black, bool showCounter, int counter,
                         int activeLine) {
    if (black) {
        RECT rc = {0, 0, w, h};
        HBRUSH b = CreateSolidBrush(RGB(0, 0, 0));
        FillRect(hdc, &rc, b);
        DeleteObject(b);
        return;
    }
    DrawBackground(hdc, w, h, theme);
    // v7.1.0 «OPERADOR» (feedback #1): slide COMPUESTA — el lienzo libre se
    // dibuja por POSICIONES de sus elementos (antes del camino clásico de
    // líneas centradas, que no aplica). El negro de salida ya regresó arriba.
    if (slide && slide->kind == SLIDE_COMPOSED && !slide->elements.empty()) {
        DrawComposedElements(hdc, w, h, slide, theme);
        return;
    }
    if (!slide || slide->kind == SLIDE_BLANK || slide->lines.empty())
        return;

    const RGBA fg = ParseColor(theme.fgColor, RGBA{255, 255, 255, 255});
    const RGBA ac = ParseColor(theme.accentColor, RGBA{255, 58, 166, 185});
    const COLORREF fill = ToColorRef(fg);
    const COLORREF outline = ToColorRef(ac);
    const COLORREF shadow = RGB(0, 0, 0);

    // Líneas visibles (uppercase opcional del tema, solo ASCII — determinista).
    // v5.1.0: cada línea lleva su línea de acordes adjunta (vacía si no hay):
    // se dibuja ENCIMA de la letra, en acento y al 62% del cuerpo — el modo
    // "músicos" de Holyrics (requisito del spec, componente 1).
    // v6.0.0: se conserva también el texto UTF-8 (con uppercase aplicado) para
    // el resaltado en proyección (Highlight::Split trabaja sobre UTF-8).
    std::vector<std::pair<std::wstring, std::string> > texts;   // (wstring, utf8)
    std::vector<std::string> chordLines;
    for (const SlideLine& l : slide->lines) {
        std::string t = l.text;
        if (theme.uppercase) {
            for (size_t i = 0; i < t.size(); ++i)
                if (t[i] >= 'a' && t[i] <= 'z') t[i] = (char)(t[i] - 'a' + 'A');
        }
        texts.push_back(std::make_pair(Utf8ToWide(t), t));
        chordLines.push_back(l.chords);
    }
    const bool useHighlight = !slide->highlight.empty();

    // Tamaño base de fuente escalado a la altura del lienzo (1080p nominal).
    int fontPx = std::max(14, (int)std::lround(theme.fontSize * (h / 1080.0)));
    const int maxBlockH = (int)(h * 0.78);
    const int gapBase = std::max(6, fontPx / 4);

    // Ajuste tipográfico: reduce 10% hasta caber.
    int totalH = 0;
    HFONT font = nullptr;
    HFONT fontCh = nullptr;
    for (int iter = 0; iter < 24; ++iter) {
        if (font) DeleteObject(font);
        if (fontCh) { DeleteObject(fontCh); fontCh = nullptr; }
        font = MakeFont(theme, fontPx, theme.bold || slide->kind == SLIDE_TITLE);
        fontCh = MakeFont(theme, std::max(12, (int)std::lround(fontPx * 0.62)), true);
        HGDIOBJ old = SelectObject(hdc, font);
        (void)old;
        totalH = 0;
        int maxW = 0;
        for (size_t i = 0; i < texts.size(); ++i) {
            const std::wstring& ln = texts[i].first;
            if (!chordLines[i].empty()) {
                const std::wstring chw = Utf8ToWide(chordLines[i]);
                // v5.2.0: higiene /W4 — el retorno de SelectObject de medición
                // no se usa (se restaura con font de inmediato).
                (void)SelectObject(hdc, fontCh);
                totalH += TextHeight(hdc, chw) + std::max(2, fontPx / 8);
                maxW = std::max(maxW, TextWidth(hdc, chw));
                SelectObject(hdc, font);
            }
            totalH += TextHeight(hdc, ln);
            maxW = std::max(maxW, TextWidth(hdc, ln));
        }
        totalH += gapBase * (int)(texts.size() > 1 ? texts.size() - 1 : 0);
        SelectObject(hdc, GetStockObject(SYSTEM_FONT));
        if (totalH <= maxBlockH && maxW <= (int)(w * 0.9)) break;
        fontPx = (int)std::lround(fontPx * 0.9);
        if (fontPx < 12) { fontPx = 12; font = MakeFont(theme, fontPx, theme.bold); break; }
    }
    if (!font) font = MakeFont(theme, fontPx, theme.bold);
    if (!fontCh) fontCh = MakeFont(theme, std::max(12, (int)std::lround(fontPx * 0.62)), true);

    HGDIOBJ oldFont = SelectObject(hdc, font);
    SetTextAlign(hdc, TA_LEFT | TA_TOP);

    const int y0 = (h - totalH) / 2;
    const int shadowOffset = std::max(2, fontPx / 16);
    const int gap = std::max(6, fontPx / 4);
    int y = y0;
    for (size_t li = 0; li < texts.size(); ++li) {
        const std::wstring& ch = Utf8ToWide(chordLines[li]);
        const std::wstring& ln = texts[li].first;
        if (!ch.empty()) {
            // v5.2.0: higiene /W4 — ídem: retorno de medición no usado.
            (void)SelectObject(hdc, fontCh);
            SetBkMode(hdc, TRANSPARENT);
            SetTextColor(hdc, outline);          // color de acento
            const int cw = TextWidth(hdc, ch);
            TextOutW(hdc, (w - cw) / 2, y, ch.c_str(), (int)ch.size());
            y += TextHeight(hdc, ch) + std::max(2, fontPx / 8);
            SelectObject(hdc, font);
        }
        // v7.0.0 «ULTRA» (F1.01): estilo de línea activa por tema. Con
        // activeLine >= 0 y dimInactive: la línea activa al 100% (color
        // activo/acento) y las demás atenuadas hacia el fondo. Sin
        // dimInactive o sin línea activa: conducta clásica.
        const bool isLineActive =
            (activeLine >= 0 && (int)li == activeLine);
        COLORREF lineFill   = fill;
        COLORREF lineOutl   = outline;
        if (activeLine >= 0 && theme.dimInactive) {
            const RGBA acv = ParseColor(theme.activeLineColor, RGBA{255, 58, 166, 185});
            if (isLineActive) {
                if (!theme.activeLineColor.empty()) {
                    lineFill = ToColorRef(acv);
                    lineOutl = ToColorRef(acv);
                } else {
                    lineFill = ToColorRef(ac);     // acento del tema
                    lineOutl = outline;
                }
            } else {
                lineFill = DimColor(theme, fill);
                lineOutl = DimColor(theme, outline);
            }
        }
        HFONT fontLine = font;
        if (isLineActive && theme.activeLineBold && !theme.bold) {
            fontLine = MakeFont(theme, fontPx, true);
            SelectObject(hdc, fontLine);
        }
        if (useHighlight) {
            // v6.0.0: línea con resaltado (matches en color de acento).
            DrawLineWithHighlight(hdc, w, y, texts[li].second, slide->highlight,
                                  lineOutl, theme.outlineWidth * (fontPx / 54.0),
                                  lineFill, ToColorRef(ac), shadow, shadowOffset);
        } else {
            const int tw = TextWidth(hdc, ln);
            const int x = (w - tw) / 2;
            DrawOutlined(hdc, x, y, ln, lineOutl,
                         theme.outlineWidth * (fontPx / 54.0), lineFill, shadow, shadowOffset);
        }
        if (fontLine != font) {
            SelectObject(hdc, font);
            DeleteObject(fontLine);
            fontLine = nullptr;
        }
        y += TextHeight(hdc, ln) + gap;
    }
    SelectObject(hdc, oldFont);
    DeleteObject(font);
    DeleteObject(fontCh);

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
        swprintf_s(buf, 32, L"%d", counter);
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

} // namespace lumina
