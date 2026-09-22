// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Renderer.cpp : implementacion del motor de render (ver Renderer.h)
// ============================================================================
#include "Renderer.h"
#include "BibleRef.h"

#include <wx/dcmemory.h>
#include <wx/image.h>
#include <wx/tokenzr.h>

#include <algorithm>
#include <cmath>
#include <map>
#include <set>

// ---------------------------------------------------------------------------
// Cache LRU de imagenes de fondo (correccion B7 heredada: suficientes
// entradas para fondo + slide actual + siguiente sin desalojar el fondo)
// ---------------------------------------------------------------------------
namespace {
constexpr int IMG_CACHE_MAX = 4;
constexpr double kPi = 3.14159265358979323846;

// Medición con fuente explícita (wx 3.2: GetTextExtent usa la fuente ACTUAL;
// fijamos la fuente pedida y medimos). La fuente es restablecida por quien dibuja.
inline void MeasureText(wxGraphicsContext *gc, const wxFont &font, const wxString &str,
                        double *w, double *h, double *desc = nullptr, double *lead = nullptr)
{
    gc->SetFont(font, wxColour(0, 0, 0));
    gc->GetTextExtent(str, w, h, desc, lead);
}

struct ImgCache
{
    std::map<wxString, wxImage> map;
    std::vector<wxString> order;               // mas reciente al final
    void Touch(const wxString &k)
    {
        for (auto it = order.begin(); it != order.end(); ++it) {
            if (*it == k) {
                order.erase(it);
                order.push_back(k);
                return;
            }
        }
        order.push_back(k);
    }
    void Evict()
    {
        while (order.size() > (size_t)IMG_CACHE_MAX) {
            const wxString victim = order.front();
            order.erase(order.begin());
            map.erase(victim);
        }
    }
};

ImgCache &Cache()
{
    static ImgCache inst;
    return inst;
}

wxColour WithAlpha(const wxColour &c, int alpha)
{
    return wxColour(c.Red(), c.Green(), c.Blue(), alpha);
}

double RelativeLuminance(const wxColour &c)
{
    auto lin = [](double v) {
        v /= 255.0;
        return v <= 0.03928 ? v / 12.92 : std::pow((v + 0.055) / 1.055, 2.4);
    };
    return 0.2126 * lin(c.Red()) + 0.7152 * lin(c.Green()) + 0.0722 * lin(c.Blue());
}

// Palabras de resaltado normalizadas (sin acentos, minusculas)
std::set<wxString> HighlightSet(const wxString &raw)
{
    std::set<wxString> out;
    const wxArrayString toks = wxStringTokenize(raw, " \t,", wxTOKEN_STRTOK);
    for (const wxString &t : toks)
        out.insert(BibleRef::StripAccents(t.Lower()));
    return out;
}

// Gradiente escalado al angulo (0=vert abajo->arriba, 90=horizontal izq->der)
void GradientCoords(double angleDeg, double w, double h,
                    double *x1, double *y1, double *x2, double *y2)
{
    const double rad = angleDeg * kPi / 180.0;
    const double cx = w / 2.0, cy = h / 2.0;
    const double len = std::fabs(w * std::cos(rad)) + std::fabs(h * std::sin(rad));
    *x1 = cx - std::cos(rad) * len / 2.0;
    *y1 = cy - std::sin(rad) * len / 2.0;
    *x2 = cx + std::cos(rad) * len / 2.0;
    *y2 = cy + std::sin(rad) * len / 2.0;
}
} // namespace

// ---------------------------------------------------------------------------
// API publica
// ---------------------------------------------------------------------------
double Renderer::ContrastRatio(const wxColour &a, const wxColour &b)
{
    const double la = RelativeLuminance(a);
    const double lb = RelativeLuminance(b);
    const double hi = std::max(la, lb), lo = std::min(la, lb);
    return (lo <= 0.0) ? 21.0 : (hi + 0.05) / (lo + 0.05);
}

wxString Renderer::ContrastLevel(const wxColour &a, const wxColour &b)
{
    const double r = ContrastRatio(a, b);
    if (r >= 7.0)  return "ok";
    if (r >= 4.5)  return "warn";
    return "err";
}

void Renderer::ClearCache()
{
    Cache().map.clear();
    Cache().order.clear();
}

const wxImage *Renderer::CachedImage(const wxString &path)
{
    ImgCache &c = Cache();
    auto it = c.map.find(path);
    if (it != c.map.end()) {
        c.Touch(path);
        return &it->second;
    }
    wxImage img(path, wxBITMAP_TYPE_ANY);
    if (!img.IsOk())
        return nullptr;
    auto res = c.map.emplace(path, img);
    c.Touch(path);
    c.Evict();
    return &res.first->second;
}

wxBitmap Renderer::RenderBackground(const Theme &theme, int w, int h)
{
    Slide blank;
    blank.kind = Slide::Blank;
    Ctx c;
    c.w = w;
    c.h = h;
    c.scale = h / 1080.0;
    wxBitmap bmp(w, h, 32);
    wxMemoryDC mdc(bmp);
    wxGraphicsContext *gc = wxGraphicsContext::Create(mdc);
    if (!gc)
        return bmp;
    c.gc = gc;
    DrawBackground(c, theme);
    delete gc;
    return bmp;
}

wxBitmap Renderer::Render(const Slide &slide, const Theme &theme, int w, int h,
                          const RenderOpts &opts)
{
    wxBitmap bmp(w, h, 32);
    wxMemoryDC mdc(bmp);
    wxGraphicsContext *gc = wxGraphicsContext::Create(mdc);
    if (!gc)
        return bmp;
    Ctx c;
    c.gc = gc;
    c.w = w;
    c.h = h;
    c.scale = h / 1080.0;
    c.opts = &opts;

    gc->SetAntialiasMode(wxANTIALIAS_DEFAULT);
    gc->SetInterpolationQuality(wxINTERPOLATION_BEST);

    switch (slide.kind) {
        case Slide::Image:
            DrawBackground(c, theme);
            DrawImageSlide(c, slide);
            break;
        case Slide::Title:
            DrawBackground(c, theme);
            DrawTitleSlide(c, slide, theme);
            break;
        case Slide::Aviso:
            DrawBackground(c, theme);
            DrawAvisoSlide(c, slide, theme);
            break;
        case Slide::Video:
            DrawBackground(c, theme);
            {
                // Pantalla de "cargando video" con el titulo
                Slide aux = slide;
                aux.kind = Slide::Aviso;
                if (aux.lines.empty())
                    aux.lines.push_back(SlideLine(L"▶ " + aux.title));
                DrawAvisoSlide(c, aux, theme);
            }
            break;
        case Slide::Blank:
            DrawBackground(c, theme);
            break;
        default:
            DrawBackground(c, theme);
            DrawContentSlide(c, slide, theme, slide.kind == Slide::Bible);
            break;
    }

    if (opts.showCounter && opts.counterTotal > 0)
        DrawCounter(c, opts);
    if (opts.lowerThird)
        DrawLowerThird(c);

    delete gc;
    return bmp;
}

// ---------------------------------------------------------------------------
// Fondos
// ---------------------------------------------------------------------------
void Renderer::DrawBackground(Ctx &c, const Theme &theme)
{
    const BackgroundStyle &bg = theme.background;
    if (bg.type == 2 && !bg.imagePath.empty()) {
        const wxImage *img = CachedImage(bg.imagePath);
        if (img && img->IsOk()) {
            wxImage scaled = *img;
            if (bg.imageFit == 0) {
                // Llenar (cover): escala a cubrir y recorta centrado
                const double sw = (double)c.w / img->GetWidth();
                const double sh = (double)c.h / img->GetHeight();
                const double s = std::max(sw, sh);
                const int nw = std::max(1, (int)(img->GetWidth() * s));
                const int nh = std::max(1, (int)(img->GetHeight() * s));
                scaled = scaled.Scale(nw, nh, wxIMAGE_QUALITY_BILINEAR);
                const int ox = (nw - c.w) / 2, oy = (nh - c.h) / 2;
                if (nw > c.w || nh > c.h)
                    scaled = scaled.GetSubImage(wxRect(ox, oy, c.w, c.h));
            } else {
                // Ajustar (contain): escala completo y centra sobre el fondo
                wxImage cover = *img;
                const double sw = (double)c.w / img->GetWidth();
                const double sh = (double)c.h / img->GetHeight();
                const double s = std::min(sw, sh);
                const int nw = std::max(1, (int)(img->GetWidth() * s));
                const int nh = std::max(1, (int)(img->GetHeight() * s));
                cover = cover.Scale(nw, nh, wxIMAGE_QUALITY_BILINEAR);
                c.gc->SetBrush(wxBrush(WithAlpha(bg.color1, 255)));
                c.gc->SetPen(*wxTRANSPARENT_PEN);
                c.gc->DrawRectangle(0, 0, c.w, c.h);
                wxBitmap bm(cover);
                c.gc->DrawBitmap(bm, (c.w - nw) / 2, (c.h - nh) / 2, nw, nh);
                return;
            }
            c.gc->DrawBitmap(wxBitmap(scaled), 0, 0, c.w, c.h);
            return;
        }
        // Imagen no disponible: cae a gradiente
    }
    if (bg.type == 1) {
        double x1, y1, x2, y2;
        GradientCoords(bg.gradientAngle, c.w, c.h, &x1, &y1, &x2, &y2);
        const wxGraphicsBrush br = c.gc->CreateLinearGradientBrush(
            x1, y1, x2, y2, bg.color1, bg.color2);
        c.gc->SetBrush(br);
        c.gc->SetPen(*wxTRANSPARENT_PEN);
        c.gc->DrawRectangle(0, 0, c.w, c.h);
        return;
    }
    c.gc->SetBrush(wxBrush(WithAlpha(bg.color1, 255)));
    c.gc->SetPen(*wxTRANSPARENT_PEN);
    c.gc->DrawRectangle(0, 0, c.w, c.h);
}

// ---------------------------------------------------------------------------
// Bloques de texto (ajuste, contorno, sombra, cifrado, resaltado)
// ---------------------------------------------------------------------------
namespace {

struct DrawStyle
{
    wxColour color;
    wxColour outline;
    int outlineW = 0;
    bool shadow = false;
    wxColour shadowCol;
    int shadowOff = 0;
    int align = 0;
};

// Dibuja UNA linea ya medida con contorno/sombra. Devuelve la altura usada.
void DrawOneLine(wxGraphicsContext *gc, const wxString &text, const wxFont &font,
                 const DrawStyle &ds, double x, double y, double maxW,
                 const std::set<wxString> *highlight = nullptr)
{
    auto drawStr = [&](const wxFont &f, const wxString &s, double px, double py,
                       const wxColour &col) {
        gc->SetFont(f, col);
        gc->DrawText(s, px, py);
    };

    double tw = 0, th = 0, desc = 0, lead = 0;
    MeasureText(gc, font, text, &tw, &th, &desc, &lead);

    double ox = x;
    if (ds.align == 0)      ox = x + (maxW - tw) / 2.0;
    else if (ds.align == 2) ox = x + (maxW - tw);

    // Sombra (linea completa; el resaltado se pinta en el relleno)
    if (ds.shadow) {
        drawStr(font, text, ox + ds.shadowOff, y + ds.shadowOff, ds.shadowCol);
    }
    // Contorno (silueta en 8 direcciones)
    if (ds.outlineW > 0) {
        const int r = ds.outlineW;
        for (int dx = -r; dx <= r; dx += r) {
            for (int dy = -r; dy <= r; dy += r) {
                if (dx == 0 && dy == 0) continue;
                drawStr(font, text, ox + dx, y + dy, ds.outline);
                if (r == 1) {                    // a 1 px completar anillo 3x3
                    drawStr(font, text, ox + dx, y, ds.outline);
                    drawStr(font, text, ox, y + dy, ds.outline);
                }
            }
        }
        drawStr(font, text, ox + r, y, ds.outline);
        drawStr(font, text, ox - r, y, ds.outline);
        drawStr(font, text, ox, y + r, ds.outline);
        drawStr(font, text, ox, y - r, ds.outline);
    }
    // Relleno (con o sin resaltado por palabra)
    if (highlight && !text.empty()) {
        double wx0 = ox;
        wxString word;
        wxString space = " ";
        // recorremos conservando espacios para el ancho exacto
        size_t i = 0;
        const size_t n = text.Length();
        while (i < n) {
            word.clear();
            while (i < n && text[i] != ' ') { word += text[i]; ++i; }
            while (i < n && text[i] == ' ') { ++i; }
            if (word.empty()) continue;
            double ww = 0, wh = 0;
            MeasureText(gc, font, word, &ww, &wh);
            const wxString norm = BibleRef::StripAccents(word.Lower());
            // recorta puntuacion extrema para la comparacion
            wxString bare = norm;
            while (!bare.empty() && wxString(",.;:!?¡¿\"'()[]").Contains(bare.Last()))
                bare.RemoveLast();
            const bool isHi = highlight->count(bare) > 0 || highlight->count(norm) > 0;
            const wxColour fillCol = isHi ? wxColour(255, 205, 60) : ds.color;
            const wxFont &useFont = isHi ? font.Bold() : font;
            drawStr(useFont, word, wx0, y, fillCol);
            double sw2 = 0;
            MeasureText(gc, font, word + " ", &sw2, &wh);
            wx0 += sw2;
        }
    } else {
        drawStr(font, text, ox, y, ds.color);
    }
}

// Parte una linea en palabras respetando un ancho maximo
std::vector<wxString> WrapLine(wxGraphicsContext *gc, const wxString &text,
                               const wxFont &font, double maxW)
{
    std::vector<wxString> out;
    if (text.empty()) {
        out.push_back("");
        return out;
    }
    const wxArrayString words = wxStringTokenize(text, ' ', wxTOKEN_STRTOK);
    if (words.empty()) {
        out.push_back("");
        return out;
    }
    double sw = 0, sh = 0;
    wxString cur;
    for (const wxString &w : words) {
        const wxString cand = cur.empty() ? w : cur + " " + w;
        MeasureText(gc, font, cand, &sw, &sh);
        if (sw <= maxW || cur.empty()) {
            cur = cand;
        } else {
            out.push_back(cur);
            cur = w;
        }
    }
    if (!cur.empty())
        out.push_back(cur);
    return out;
}
} // namespace

void Renderer::DrawTextBlock(Ctx &c, const std::vector<SlideLine> &lines,
                             const TextStyle &style, const wxRect &box,
                             bool autoFit, int basePx, bool useHighlight)
{
    if (lines.empty())
        return;
    wxGraphicsContext *gc = c.gc;

    DrawStyle ds;
    ds.color = style.color;
    ds.outline = style.outlineColor;
    ds.outlineW = style.outlineWidth;
    ds.shadow = style.shadow;
    ds.shadowCol = style.shadowColor;
    ds.shadowOff = (int)std::max(1.0, style.shadowOffset * c.scale);
    ds.align = style.align;

    std::set<wxString> hi;
    if (useHighlight && c.opts && !c.opts->highlight.empty())
        hi = HighlightSet(c.opts->highlight);

    // Tamano de fuente en px desde la referencia 1080p
    int px = (int)std::max(10.0, basePx * c.scale);
    wxFont font = style.MakeFont(px);
    if (!font.IsOk())
        font = *wxNORMAL_FONT;

    // Autoajuste: reduce hasta que el bloque completo quepa en la caja
    double lineH = 0, totalH = 0;
    std::vector<std::pair<wxString, wxString>> wrapped;   // (texto, cifrado)
    const double chordFactor = 0.55;
    for (int attempt = 0; attempt < 40; ++attempt) {
        font = style.MakeFont(px);
        wrapped.clear();
        totalH = 0;
        bool fits = true;
        double lh0 = 0;
        MeasureText(gc, font, "Mg", nullptr, &lh0);
        for (const SlideLine &sl : lines) {
            const wxString txt = style.upperCase ? sl.text.Upper() : sl.text;
            std::vector<wxString> wl = WrapLine(gc, txt, font, (double)box.width);
            if (!sl.chords.empty() && c.opts && c.opts->showChords) {
                wxFont cf = font.Smaller().Smaller();
                double chh = 0;
                MeasureText(gc, cf, sl.chords, nullptr, &chh);
                totalH += chh + lh0 * 0.08;
            }
            for (size_t i = 0; i < wl.size(); ++i) {
                wrapped.emplace_back(wl[i], i == 0 ? sl.chords : wxString());
            }
            double lh = 0;
            MeasureText(gc, font, "Mg", nullptr, &lh);
            totalH += lh * 1.22 * wl.size();
            if (totalH > box.height) { fits = false; break; }
        }
        // ancho: ninguna palabra partida debe exceder (WrapLine ya garantiza
        // mientras haya al menos una palabra que quepa; a partir de ~7px OK)
        if (!autoFit || fits)
            break;
        px = (int)(px * 0.92);
        if (px < 10)
            break;
    }

    // Altura de linea final
    double lh = 0;
    wxFont f2 = font;
    MeasureText(gc, f2, "Mg", nullptr, &lh);
    lineH = lh * 1.22;

    // Verticales: centrado dentro de la caja
    const double total = totalH;
    double y = box.y + std::max(0.0, (box.height - total) / 2.0);

    for (const auto &entry : wrapped) {
        const wxString &text = entry.first;
        const wxString &chords = entry.second;
        if (!chords.empty() && c.opts && c.opts->showChords) {
            wxFont cf = font.Smaller().Smaller();
            DrawStyle cds = ds;
            cds.color = wxColour(255, 205, 90);       // dorado cifrado
            cds.outlineW = std::max(1, cds.outlineW);
            DrawOneLine(gc, chords, cf, cds, (double)box.x, y, (double)box.width);
            double chh = 0;
            MeasureText(gc, cf, chords, nullptr, &chh);
            y += chh + lh * 0.08;
        }
        if (text.empty()) {
            y += lineH * 0.6;
            continue;
        }
        DrawOneLine(gc, text, font, ds, (double)box.x, y, (double)box.width, hi.empty() ? nullptr : &hi);
        y += lineH;
    }
}

// ---------------------------------------------------------------------------
// Slides
// ---------------------------------------------------------------------------
void Renderer::DrawTitleSlide(Ctx &c, const Slide &slide, const Theme &theme)
{
    const int margin = (int)(c.w * 0.08);
    const wxRect box(margin, (int)(c.h * 0.22), c.w - 2 * margin, (int)(c.h * 0.56));

    // Titulo grande centrado
    TextStyle ts = theme.title;
    ts.align = 0;
    if (slide.lines.empty())
        return;
    std::vector<SlideLine> main;
    main.push_back(slide.lines[0]);
    DrawTextBlock(c, main, ts, wxRect(box.x, box.y, box.width, box.height / 2),
                  true, (int)(theme.body.pointSize * 1.15));

    if (slide.lines.size() > 1) {
        TextStyle bs = theme.body;
        bs.align = 0;
        bs.pointSize = (int)(theme.body.pointSize * 0.55);
        bs.color = WithAlpha(bs.color, 225);
        std::vector<SlideLine> sub;
        sub.push_back(slide.lines[1]);
        DrawTextBlock(c, sub, bs,
                      wxRect(box.x, box.y + box.height / 2, box.width, box.height / 2),
                      true, bs.pointSize);
    }
}

void Renderer::DrawContentSlide(Ctx &c, const Slide &slide, const Theme &theme, bool bible)
{
    const int margin = (int)(c.w * 0.08);

    if (bible && !slide.refLabel.empty()) {
        // Referencia biblica discreta arriba-derecha
        TextStyle rs = theme.title;
        rs.pointSize = std::max(18, (int)(theme.title.pointSize * 0.45));
        rs.align = 2;
        DrawTextBlock(c, std::vector<SlideLine>{ SlideLine(slide.refLabel) }, rs,
                      wxRect(margin, (int)(c.h * 0.045), c.w - 2 * margin,
                             (int)(c.h * 0.09)), false, rs.pointSize);
    } else if (!slide.title.empty()) {
        TextStyle ts = theme.title;
        ts.pointSize = std::max(20, (int)(theme.title.pointSize * 0.6));
        DrawTextBlock(c, std::vector<SlideLine>{ SlideLine(slide.title) }, ts,
                      wxRect(margin, (int)(c.h * 0.045), c.w - 2 * margin,
                             (int)(c.h * 0.12)), false, ts.pointSize);
    }

    const int top = (int)(c.h * 0.20);
    const int bottom = (int)(c.h * 0.94);
    DrawTextBlock(c, slide.lines, theme.body,
                  wxRect(margin, top, c.w - 2 * margin, bottom - top),
                  true, theme.body.pointSize, bible);
}

void Renderer::DrawAvisoSlide(Ctx &c, const Slide &slide, const Theme &theme)
{
    const int margin = (int)(c.w * 0.10);
    std::vector<SlideLine> lines = slide.lines;
    if (lines.empty() && !slide.title.empty())
        lines.push_back(SlideLine(slide.title));
    if (lines.empty())
        return;
    TextStyle bs = theme.body;
    bs.align = 0;
    bs.pointSize = (int)(theme.body.pointSize * 1.1);
    DrawTextBlock(c, lines, bs,
                  wxRect(margin, (int)(c.h * 0.25), c.w - 2 * margin, (int)(c.h * 0.5)),
                  true, bs.pointSize);
}

void Renderer::DrawImageSlide(Ctx &c, const Slide &slide)
{
    if (slide.mediaPath.empty())
        return;
    const wxImage *img = CachedImage(slide.mediaPath);
    if (!img || !img->IsOk())
        return;
    const double sw = (double)c.w / img->GetWidth();
    const double sh = (double)c.h / img->GetHeight();
    const double s = std::max(sw, sh);
    wxImage scaled = img->Scale(std::max(1, (int)(img->GetWidth() * s)),
                                std::max(1, (int)(img->GetHeight() * s)),
                                wxIMAGE_QUALITY_BILINEAR);
    const int ox = std::max(0, (scaled.GetWidth() - c.w) / 2);
    const int oy = std::max(0, (scaled.GetHeight() - c.h) / 2);
    if (scaled.GetWidth() > c.w || scaled.GetHeight() > c.h)
        scaled = scaled.GetSubImage(wxRect(ox, oy, c.w, c.h));
    c.gc->DrawBitmap(wxBitmap(scaled), 0, 0, c.w, c.h);
}

void Renderer::DrawCounter(Ctx &c, const RenderOpts &opts)
{
    const wxString txt = wxString::Format("%d/%d", opts.counterIndex, opts.counterTotal);
    const int px = (int)std::max(12.0, 22 * c.scale);
    const wxFont font(wxFontInfo(px).Family(wxFONTFAMILY_SWISS));
    c.gc->SetFont(font, wxColour(255, 255, 255, 180));
    double tw = 0, th = 0;
    MeasureText(c.gc, font, txt, &tw, &th);
    const int pad = (int)(6 * c.scale);
    const int bx = c.w - (int)tw - pad * 3;
    const int by = c.h - (int)th - pad * 2;
    c.gc->SetBrush(wxBrush(wxColour(0, 0, 0, 90)));
    c.gc->SetPen(*wxTRANSPARENT_PEN);
    c.gc->DrawRoundedRectangle(bx, by, tw + pad * 2, th + pad, (int)(6 * c.scale));
    c.gc->DrawText(txt, bx + pad, by + pad / 2);
}

void Renderer::DrawLowerThird(Ctx &c)
{
    if (!c.opts)
        return;
    const int bandH = (int)(c.h * 0.17);
    const int y = c.h - bandH - (int)(c.h * 0.06);
    const int x = (int)(c.w * 0.06);
    const int w = c.w - 2 * x;

    c.gc->SetBrush(wxBrush(wxColour(8, 10, 14, 168)));
    c.gc->SetPen(*wxTRANSPARENT_PEN);
    c.gc->DrawRoundedRectangle(x, y, w, bandH, (int)(10 * c.scale));
    c.gc->SetBrush(wxBrush(wxColour(230, 168, 42, 235)));    // barra dorada
    c.gc->DrawRectangle(x, y, (int)(6 * c.scale), bandH);

    const int pxT = (int)std::max(14.0, 34 * c.scale);
    const int pxS = (int)std::max(12.0, 24 * c.scale);
    const int padL = (int)(18 * c.scale);
    if (!c.opts->lowerTitle.empty()) {
        const wxFont ft(wxFontInfo(pxT).Family(wxFONTFAMILY_SWISS).Bold());
        c.gc->SetFont(ft, wxColour(255, 231, 160));
        c.gc->DrawText(c.opts->lowerTitle, x + padL, y + (int)(8 * c.scale));
    }
    if (!c.opts->lowerText.empty()) {
        const wxFont fs(wxFontInfo(pxS).Family(wxFONTFAMILY_SWISS));
        c.gc->SetFont(fs, wxColour(255, 255, 255, 235));
        c.gc->DrawText(c.opts->lowerText, x + padL, y + bandH - pxS - (int)(10 * c.scale));
    }
}
