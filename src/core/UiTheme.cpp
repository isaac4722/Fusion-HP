// ============================================================================
//  Fusion-HP · UiTheme.cpp — sistema de diseño web replicado (Win32/GDI+)
// ============================================================================
#include "UiTheme.h"
#include "Environment.h"
#include "SlideState.h"
#include <mutex>
#include <map>
#include <filesystem>

namespace fusion {
namespace ui {

// ------------------------------------------------------------------ GDI+
static ULONG_PTR sToken = 0;
static Gdiplus::GdiplusStartupInput sInput;

bool InitGdiplus() {
    if (sToken) return true;
    return Gdiplus::GdiplusStartup(&sToken, &sInput, nullptr) == Gdiplus::Ok;
}
void ShutdownGdiplus() {
    if (sToken) { Gdiplus::GdiplusShutdown(sToken); sToken = 0; }
}

// ------------------------------------------------------------------ fuentes
struct FontKey { int px, weight; bool italic; bool operator<(const FontKey& o) const {
    return px != o.px ? px < o.px : weight != o.weight ? weight < o.weight : (int)italic < (int)o.italic; } };
static std::map<FontKey, HFONT> sFonts;

HFONT Font(int px, int weight, bool italic) {
    FontKey k{px, weight, italic};
    auto it = sFonts.find(k);
    if (it != sFonts.end()) return it->second;
    HFONT f = CreateFontW(-px, 0, 0, 0, weight, italic, 0, 0, DEFAULT_CHARSET,
                          0, 0, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
    sFonts[k] = f;
    return f;
}

// ------------------------------------------------------------------ iconos
struct IconKey { std::string name; int tint; bool operator<(const IconKey& o) const {
    return tint != o.tint ? tint < o.tint : name < o.name; } };
static std::map<IconKey, Gdiplus::Bitmap*> sIcons;

std::wstring IconRoot() {
    return ExeDir() + L"\\resources\\img\\icons";
}

Gdiplus::Bitmap* Icon(const std::string& name, IconTint tint) {
    if (name.empty()) return nullptr;
    IconKey k{name, (int)tint};
    auto it = sIcons.find(k);
    if (it != sIcons.end()) return it->second;
    const wchar_t* set = tint == TintWhite ? L"white20" : tint == TintAccent ? L"accent20" : L"ink20";
    std::wstring p = IconRoot() + L"\\" + set + L"\\" + ToWide(name) + L".png";
    Gdiplus::Bitmap* bmp = new Gdiplus::Bitmap(p.c_str());
    if (bmp->GetLastStatus() != Gdiplus::Ok) { delete bmp; bmp = nullptr; }
    sIcons[k] = bmp;             // cachea también el fallo (no reintenta por paint)
    return bmp;
}

// ------------------------------------------------------------------ helpers
Gdiplus::Color ToColor(uint32_t c) {
    return Gdiplus::Color((c >> 24) & 0xFF, (c >> 16) & 0xFF, (c >> 8) & 0xFF, c & 0xFF);
}

void RoundRect(Gdiplus::Graphics& g, const RECT& r, int rad, Gdiplus::Color fill,
               Gdiplus::Color line, float lineW) {
    if (r.right <= r.left || r.bottom <= r.top) return;
    Gdiplus::GraphicsPath path;
    int d = rad * 2;
    path.AddArc(r.left, r.top, d, d, 180, 90);
    path.AddArc(r.right - d, r.top, d, d, 270, 90);
    path.AddArc(r.right - d, r.bottom - d, d, d, 0, 90);
    path.AddArc(r.left, r.bottom - d, d, d, 90, 90);
    path.CloseFigure();
    if (fill.GetValue() != 0) {
        Gdiplus::SolidBrush br(fill);
        g.FillPath(&br, &path);
    }
    if (lineW > 0.0f && line.GetValue() != 0) {
        Gdiplus::Pen pn(line, lineW);
        g.DrawPath(&pn, &path);
    }
}

SIZE Measure(HDC dc, HFONT f, const std::wstring& text, int maxW) {
    SIZE sz = {0, 0};
    HFONT old = (HFONT)SelectObject(dc, f);
    if (maxW > 0) {
        RECT r = {0, 0, maxW, 0};
        DrawTextW(dc, text.c_str(), -1, &r, DT_CALCRECT | DT_WORDBREAK | DT_NOPREFIX);
        sz.cx = r.right; sz.cy = r.bottom;
    } else {
        GetTextExtentPoint32W(dc, text.c_str(), (int)text.size(), &sz);
    }
    SelectObject(dc, old);
    return sz;
}

std::wstring Elide(HDC dc, HFONT f, const std::wstring& text, int maxW) {
    if (maxW <= 8) return text;
    SIZE sz = Measure(dc, f, text);
    if (sz.cx <= maxW) return text;
    std::wstring out = text;
    while (out.size() > 1) {
        out.pop_back();
        std::wstring t = out + L"\u2026";   // …
        if (Measure(dc, f, t).cx <= maxW) return t;
    }
    return L"\u2026";
}

// ------------------------------------------------------------------ chips
static void DrawIcon20(Gdiplus::Graphics& g, Gdiplus::Bitmap* ic, int cx, int cy) {
    if (!ic) return;
    g.DrawImage(ic, cx - 10, cy - 10, 20, 20);
}

void Chip(Gdiplus::Graphics& g, const RECT& r, const std::wstring& label,
          const std::wstring& sub, const std::string& icon, int state, int kbd) {
    bool dis = state & kDis, acc = state & kAcc, on = state & kOn,
         hot = state & kHot, prs = state & kPress, danger = state & kDanger;
    Gdiplus::Color fill(0, 0, 0, 0);
    if (acc)            fill = ToColor(Accent);
    else if (prs && on) fill = ToColor(0xFFEAD9D2);
    else if (on)        fill = ToColor(AccentBg);
    else if (prs && danger) fill = ToColor(DangerBg);
    else if (prs)       fill = ToColor(0xFFE8E6E4);
    else if (hot && !dis && !danger) fill = ToColor(HoverBg);
    else if (hot && danger) fill = ToColor(DangerBg);
    if (fill.GetValue() != 0) RoundRect(g, r, 5, fill);
    else RoundRect(g, r, 5, Gdiplus::Color(0, 0, 0, 0));   // nada

    uint32_t fg = acc ? 0xFFFFFFFF : on ? AccentDk : Ink;
    if (danger && hot) fg = DangerTx;
    if (dis) fg = 0xFFA19F9D;

    HDC dc = g.GetHDC();
    int cy = (r.top + r.bottom) / 2;
    int tx = r.left + 8;
    if (!icon.empty()) {
        DrawIcon20(g, Icon(icon, acc ? TintWhite : on ? TintAccent : TintInk), r.left + 16, cy);
        tx = r.left + 30;
    }
    HFONT f = Font(12, (on || acc) ? FW_SEMIBOLD : FW_NORMAL);
    SelectObject(dc, f);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, fg);
    std::wstring t = Elide(dc, f, label, r.right - tx - (kbd ? 26 : 6));
    RECT tr = {tx, r.top, r.right - (kbd ? 26 : 4), r.bottom};
    DrawTextW(dc, t.c_str(), -1, &tr, DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
    if (!sub.empty()) {
        HFONT fs = Font(10);
        SelectObject(dc, fs);
        SetTextColor(dc, acc ? 0xFFE6D5D0 : GrayLt);
        RECT sr = {tx, r.bottom - 16, r.right - 4, r.bottom};
        DrawTextW(dc, Elide(dc, fs, sub, sr.right - sr.left).c_str(), -1, &sr,
                  DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
    }
    g.ReleaseHDC(dc);
    if (kbd) {
        RECT kr{r.right - 24, r.top + (r.bottom - r.top - 16) / 2, r.right - 6, r.top + (r.bottom - r.top + 16) / 2};
        Kbd(g, kr, std::to_wstring(kbd));
    }
}

void IconButton(Gdiplus::Graphics& g, const RECT& r, const std::string& icon,
                int state, const std::wstring&) {
    bool dis = state & kDis, on = state & kOn, hot = state & kHot,
         prs = state & kPress, acc = state & kAcc;
    if (acc) RoundRect(g, r, 5, ToColor(Accent));
    else if (on) RoundRect(g, r, 5, ToColor(AccentBg));
    else if (prs) RoundRect(g, r, 5, ToColor(0xFFE8E6E4));
    else if (hot && !dis) RoundRect(g, r, 5, ToColor(HoverBg));
    IconTint tint = (acc || (state & 64)) ? TintWhite : on ? TintAccent : TintInk;
    if (dis) tint = TintInk;
    Gdiplus::Bitmap* ic = Icon(icon, tint);
    if (ic) {
        int alpha = dis ? 90 : 255;
        Gdiplus::ColorMatrix cm = {};
        cm.m[0][0] = cm.m[1][1] = cm.m[2][2] = cm.m[4][4] = 1.0f;
        cm.m[3][3] = alpha / 255.0f;
        Gdiplus::ImageAttributes att;
        att.SetColorMatrix(&cm);
        g.DrawImage(ic, Gdiplus::Rect(r.left, r.top, r.right - r.left, r.bottom - r.top),
                    0, 0, ic->GetWidth(), ic->GetHeight(), Gdiplus::UnitPixel, &att);
    }
}

void Tab(Gdiplus::Graphics& g, const RECT& r, const std::wstring& label, bool active,
         bool hot, bool isArchivo) {
    if (active && isArchivo) {
        Gdiplus::SolidBrush br(ToColor(HoverBg));
        g.FillRectangle(&br, r.left, r.top, r.right - r.left, r.bottom - r.top);
    } else if (hot && !active) {
        Gdiplus::SolidBrush br(ToColor(HoverBg));
        g.FillRectangle(&br, r.left, r.top, r.right - r.left, r.bottom - r.top);
    }
    HDC dc = g.GetHDC();
    HFONT f = Font(13, active ? FW_SEMIBOLD : FW_NORMAL);
    SelectObject(dc, f);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, active ? Ink : 0xFF424242);
    RECT tr = {r.left, r.top, r.right, r.bottom};
    DrawTextW(dc, label.c_str(), -1, &tr, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);
    if (active && !isArchivo) {
        Gdiplus::SolidBrush br(ToColor(Accent));
        int x = r.left + 8, w = r.right - r.left - 16;
        g.FillRectangle(&br, x, r.bottom - 3, (Gdiplus::REAL)w, 2.5f);
    }
}

void Row(Gdiplus::Graphics& g, const RECT& r, int num, const std::wstring& title,
         const std::wstring& sub, const std::string& icon, int state) {
    bool on = state & kOn, hot = state & kHot, dis = state & kDis;
    if (on) RoundRect(g, r, 4, ToColor(AccentBg));
    else if (hot) RoundRect(g, r, 4, ToColor(HoverBg));
    HDC dc = g.GetHDC();
    SetBkMode(dc, TRANSPARENT);
    int cy = r.top + 11;
    if (num > 0) {
        HFONT fm = Font(11);
        SelectObject(dc, fm);
        SetTextColor(dc, on ? AccentDk : GrayLt);
        RECT nr = {r.left + 2, r.top, r.left + 24, r.bottom};
        std::wstring n = std::to_wstring(num);
        if (num < 10 && num < 100) n = L"0" + n;
        DrawTextW(dc, n.c_str(), -1, &nr, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    }
    int tx = r.left + (num > 0 ? 26 : 4);
    if (!icon.empty()) {
        DrawIcon20(g, Icon(icon, on ? TintAccent : TintInk), tx + 8, r.top + (r.bottom - r.top) / 2);
        tx += 22;
    }
    HFONT f = Font(12, on ? FW_SEMIBOLD : FW_NORMAL);
    SelectObject(dc, f);
    SetTextColor(dc, dis ? 0xFFA19F9D : on ? AccentDk : Ink);
    int subH = sub.empty() ? 0 : 14;
    RECT tr = {tx, r.top + (subH ? 2 : 0), r.right - 4, r.bottom - subH};
    DrawTextW(dc, Elide(dc, f, title, tr.right - tr.left).c_str(), -1, &tr,
              DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
    if (subH) {
        HFONT fs = Font(10);
        SelectObject(dc, fs);
        SetTextColor(dc, Gray);
        RECT sr = {tx, r.bottom - 17, r.right - 4, r.bottom - 1};
        DrawTextW(dc, Elide(dc, fs, sub, sr.right - sr.left).c_str(), -1, &sr,
                  DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
    }
    g.ReleaseHDC(dc);
}

void Card(Gdiplus::Graphics& g, const RECT& r, const std::wstring& title,
          const std::wstring& sub, bool hot, bool accentBar) {
    // marco
    Gdiplus::Pen pn(ToColor(hot ? 0xFF8A8886 : Border2), 1.0f);
    g.DrawRectangle(&pn, r.left, r.top, (Gdiplus::REAL)(r.right - r.left - 1),
                    (Gdiplus::REAL)(r.bottom - r.top - 1));
    // cabecera oscura 16:9 aprox
    int headH = (r.bottom - r.top) * 2 / 3;
    Gdiplus::SolidBrush bk(ToColor(Ink));
    g.FillRectangle(&bk, r.left + 1, r.top + 1, (Gdiplus::REAL)(r.right - r.left - 2), (Gdiplus::REAL)(headH - 1));
    if (accentBar) {
        Gdiplus::SolidBrush ab(ToColor(Accent));
        g.FillRectangle(&ab, r.left + 1, r.top + 1, 4.0f, (Gdiplus::REAL)(headH - 1));
    }
    HDC dc = g.GetHDC();
    SetBkMode(dc, TRANSPARENT);
    HFONT f1 = Font(13, FW_SEMIBOLD);
    SelectObject(dc, f1);
    SetTextColor(dc, 0xFFFFFFFF);
    RECT t1 = {r.left + 12, r.top + 10, r.right - 8, r.top + 30};
    DrawTextW(dc, Elide(dc, f1, title, t1.right - t1.left).c_str(), -1, &t1,
              DT_SINGLELINE | DT_NOPREFIX);
    HFONT f2 = Font(11);
    SelectObject(dc, f2);
    SetTextColor(dc, 0xB3FFFFFF);
    RECT t2 = {r.left + 12, r.top + 30, r.right - 8, r.top + 46};
    DrawTextW(dc, Elide(dc, f2, sub, t2.right - t2.left).c_str(), -1, &t2,
              DT_SINGLELINE | DT_NOPREFIX);
    HFONT f3 = Font(12);
    SelectObject(dc, f3);
    SetTextColor(dc, Ink);
    RECT t3 = {r.left + 8, r.top + headH + 2, r.right - 6, r.bottom - 2};
    DrawTextW(dc, Elide(dc, f3, title, t3.right - t3.left).c_str(), -1, &t3,
              DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
    g.ReleaseHDC(dc);
}

void Badge(Gdiplus::Graphics& g, const RECT& r, const std::wstring& text,
           uint32_t bg, uint32_t fg) {
    RoundRect(g, r, 8, ToColor(bg));
    HDC dc = g.GetHDC();
    HFONT f = Font(10, FW_SEMIBOLD);
    SelectObject(dc, f);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, fg);
    RECT tr = {r.left + 2, r.top, r.right - 2, r.bottom};
    DrawTextW(dc, text.c_str(), -1, &tr, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);
}

void Kbd(Gdiplus::Graphics& g, RECT r, const std::wstring& text) {
    Gdiplus::Pen pn(ToColor(0xFFC8C6C4), 1.0f);
    Gdiplus::SolidBrush br(ToColor(Paper));
    r.right -= 1; r.bottom -= 1;
    g.FillRectangle(&br, r.left, r.top, (Gdiplus::REAL)(r.right - r.left), (Gdiplus::REAL)(r.bottom - r.top));
    g.DrawRectangle(&pn, r.left, r.top, (Gdiplus::REAL)(r.right - r.left), (Gdiplus::REAL)(r.bottom - r.top));
    HDC dc = g.GetHDC();
    HFONT f = Font(10);
    SelectObject(dc, f);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, Gray);
    RECT tr = {r.left + 1, r.top, r.right, r.bottom};
    DrawTextW(dc, text.c_str(), -1, &tr, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);
}

void GroupLabel(Gdiplus::Graphics& g, const RECT& r, const std::wstring& label) {
    HDC dc = g.GetHDC();
    HFONT f = Font(10);
    SelectObject(dc, f);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, GrayLt);
    RECT tr = r;
    DrawTextW(dc, label.c_str(), -1, &tr, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
    g.ReleaseHDC(dc);
}

void Sep(Gdiplus::Graphics& g, const RECT& r) {
    Gdiplus::Pen pn(ToColor(Border), 1.0f);
    g.DrawLine(&pn, (Gdiplus::REAL)(r.left + 2), (Gdiplus::REAL)r.top,
               (Gdiplus::REAL)(r.left + 2), (Gdiplus::REAL)r.bottom);
}

void Field(Gdiplus::Graphics& g, const RECT& r, bool focused, bool hot) {
    Gdiplus::Pen pn(ToColor(focused ? Accent : hot ? 0xFF8A8886 : 0xFFC8C6C4), 1.0f);
    Gdiplus::SolidBrush br(ToColor(Paper));
    g.FillRectangle(&br, r.left, r.top, (Gdiplus::REAL)(r.right - r.left - 1), (Gdiplus::REAL)(r.bottom - r.top - 1));
    g.DrawRectangle(&pn, r.left, r.top, (Gdiplus::REAL)(r.right - r.left - 1), (Gdiplus::REAL)(r.bottom - r.top - 1));
}

// ------------------------------------------------------------------ miniaturas
struct ThumbBg { std::wstring path; Gdiplus::Bitmap* bmp; };
static std::vector<ThumbBg> sThumbBg;
static Gdiplus::Bitmap* ThumbBgLoad(const std::wstring& path) {
    if (path.empty()) return nullptr;
    for (auto& t : sThumbBg) if (t.path == path) return t.bmp;
    Gdiplus::Bitmap* b = new Gdiplus::Bitmap(path.c_str());
    if (b->GetLastStatus() != Gdiplus::Ok) { delete b; b = nullptr; }
    sThumbBg.push_back({path, b});
    return b;
}

Gdiplus::Bitmap* MakeThumb(const Json& slide, int w, int h) {
    if (w < 8 || h < 8) return nullptr;
    Gdiplus::Bitmap* bmp = new Gdiplus::Bitmap(w, h, PixelFormat32bppARGB);
    Gdiplus::Graphics g(bmp);
    g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
    g.SetTextRenderingHint(Gdiplus::TextRenderingHintClearTypeGridFit);

    // fondo
    uint32_t bgc = 0xFF111111;
    std::wstring img;
    if (slide.contains("bg") && slide["bg"].is_object()) {
        const Json& b = slide["bg"];
        if (b.contains("color") && b["color"].is_string())
            bgc = ParseColor(b["color"].get<std::string>(), bgc);
        if (b.contains("image") && b["image"].is_string()) {
            std::string ip = b["image"].get<std::string>();
            if (ip.rfind("app:", 0) == 0)
                img = ExeDir() + L"\\resources\\backgrounds\\" + ToWide(ip.substr(4));
            else img = ToWide(ip);
        }
    }
    Gdiplus::SolidBrush bb(ToColor(bgc));
    g.FillRectangle(&bb, 0, 0, w, h);
    Gdiplus::Bitmap* bgImg = ThumbBgLoad(img);
    if (bgImg) g.DrawImage(bgImg, 0, 0, (Gdiplus::REAL)w, (Gdiplus::REAL)h);

    // estilo
    std::wstring font = L"Segoe UI";
    double size = 44; bool bold = false; uint32_t col = 0xFFFFFFFF, act = 0xFFE8C872;
    int align = 1;
    if (slide.contains("style") && slide["style"].is_object()) {
        const Json& st = slide["style"];
        if (st.contains("font") && st["font"].is_string()) font = ToWide(st["font"].get<std::string>());
        if (st.contains("size")) size = st["size"].get<double>();
        if (st.contains("bold")) bold = st["bold"].get<bool>();
        if (st.contains("color") && st["color"].is_string()) col = ParseColor(st["color"].get<std::string>(), col);
        if (st.contains("activeColor") && st["activeColor"].is_string()) act = ParseColor(st["activeColor"].get<std::string>(), act);
        if (st.contains("align")) align = st["align"].get<int>();
    }
    // título (si hay) arriba
    std::vector<std::wstring> lines;
    int activeLine = 0;
    if (slide.contains("lines") && slide["lines"].is_array())
        for (auto& l : slide["lines"]) if (l.is_string()) lines.push_back(ToWide(l.get<std::string>()));
    if (slide.contains("activeLine")) activeLine = slide["activeLine"].get<int>();

    HDC dc = g.GetHDC();
    SetBkMode(dc, TRANSPARENT);
    double scale = (double)h / 540.0;            // escala respecto a 1080p
    double fs = size * scale * 0.85;             // compacto
    if (fs < 9) fs = 9;
    HFONT fT = Font((int)(fs * 1.06), FW_SEMIBOLD);
    HFONT fL = Font((int)fs, bold ? FW_SEMIBOLD : FW_NORMAL);

    int padX = (int)(w * 0.07);
    int y = (int)(h * 0.16);
    std::wstring ref;
    if (slide.contains("reference") && slide["reference"].is_string())
        ref = ToWide(slide["reference"].get<std::string>());
    if (!ref.empty()) {                       // versículo: referencia arriba
        SelectObject(dc, fT);
        SetTextColor(dc, act);
        RECT rr = {padX, y, w - padX, y + (int)fs + 6};
        DrawTextW(dc, Elide(dc, fT, ref, rr.right - rr.left).c_str(), -1, &rr,
                  DT_SINGLELINE | (align == 0 ? DT_LEFT : align == 2 ? DT_RIGHT : DT_CENTER));
        y += (int)fs + 10;
    }
    int maxLines = (int)((h - y - h * 0.1) / (fs * 1.3));
    if (maxLines < 1) maxLines = 1;
    int shown = (int)lines.size();
    if (shown > maxLines) shown = maxLines;
    int blockH = (int)(shown * fs * 1.3);
    int ly = (int)(h * 0.5) - blockH / 2;
    if (ly < y) ly = y;
    for (int i = 0; i < shown; i++) {
        SelectObject(dc, fL);
        SetTextColor(dc, i == activeLine ? act : col);
        RECT lr = {padX, ly, w - padX, ly + (int)(fs * 1.25)};
        DrawTextW(dc, Elide(dc, fL, lines[(size_t)i], lr.right - lr.left).c_str(), -1, &lr,
                  DT_SINGLELINE | DT_NOPREFIX |
                  (align == 0 ? DT_LEFT : align == 2 ? DT_RIGHT : DT_CENTER));
        ly += (int)(fs * 1.3);
    }
    g.ReleaseHDC(dc);
    return bmp;
}

} // namespace ui
} // namespace fusion
