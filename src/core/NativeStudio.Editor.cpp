// ============================================================================
//  Fusion-HP · NativeStudio.Editor.cpp — PowerStudio replicado en C++
//  Barra de título con acceso rápido · cinta de 8 pestañas · Backstage de
//  Archivo · miniaturas con secciones · lienzo con edición + notas ·
//  clasificador · panel Formato/Biblioteca · barra de estado.
// ============================================================================
#include "NativeStudio.h"
#include "NativeSession.h"
#include "Logger.h"
#include <filesystem>

namespace fs = std::filesystem;

namespace fusion {

// IDs de comandos (duplicados del .cpp principal via un pequeño puente)
namespace nsedit {
enum {
    CMD_NEW = 1, CMD_OPEN = 2, CMD_CONTINUE = 3,
    CMD_CARD_BASE = 16,
    CMD_SAVE = 100, CMD_UNDO, CMD_REDO, CMD_GOHOME, CMD_IMPORT, CMD_PRESENT,
    CMD_TAB_BASE = 120,
    CMD_ADD_TITLE = 140, CMD_ADD_SONG, CMD_ADD_VERSE, CMD_ADD_LT, CMD_ADD_BLANK,
    CMD_REMOVE_SECTION, CMD_DUP_PRES, CMD_PASTE, CMD_DUP_SLIDES, CMD_DEL_SLIDES,
    CMD_FONT_DOWN, CMD_FONT_UP, CMD_ALIGN_L, CMD_ALIGN_C, CMD_ALIGN_R, CMD_TOGGLE_LT,
    CMD_ITEM_UP, CMD_ITEM_DOWN,
    CMD_PROYECTOR = 170, CMD_MANDO,
    CMD_LIB_TAB_BASE = 200,
    CMD_SONG_BASE = 256,
    CMD_THEME_BASE = 512,
    CMD_BG_BASE = 640,
    CMD_MEDIA_BASE = 660,
    CMD_INSERT_REF = 700, CMD_BOOK_PREV, CMD_BOOK_NEXT, CMD_CH_PREV, CMD_CH_NEXT,
    CMD_LT_TOGGLE,
    CMD_P_VERSE_BASE = 1200,        // +i (verseNums_)
    CMD_APPLY_BG_SECTION = 720, CMD_APPLY_BG_ALL, CMD_APPLY_THEME_ALL,
    CMD_SLIDE_PREV = 740, CMD_SLIDE_NEXT,
    CMD_TRANS_BASE = 748,          // +i (cut/fade/slide de esta diapositiva)
    CMD_TRANSDEF_BASE = 752,        // +i (predeterminada global)
    CMD_VIEW_NORMAL = 760, CMD_VIEW_SORTER, CMD_TOGGLE_PANE, CMD_TOGGLE_NOTES, CMD_TOGGLE_TASK,
    CMD_SHORTCUTS = 780, CMD_OPTIONS,
    CMD_BS_PAGE_BASE = 800,
    CMD_BS_CLOSE = 820, CMD_BS_BLANK, CMD_BS_DUP, CMD_BS_BROWSE,
    CMD_BS_PNG_CUR, CMD_BS_PNG_ALL, CMD_BS_JSON, CMD_BS_CSV,
    CMD_OPT_ADVANCE = 840, CMD_OPT_CLOCK, CMD_OPT_ANIM,
    CMD_RECENT_BASE = 900,
    CMD_T_ITEM_BASE = 2300,
    CMD_T_SLIDE = 2400, CMD_T_UP = 2401, CMD_T_DOWN = 2402, CMD_T_DEL = 2403,
    CMD_S_SLIDE = 3000,
};
}

using namespace nsedit;

// ------------------------------------------------------------------ helpers
static int Hot(int id, int hoverId) { return hoverId == id ? ui::kHot : 0; }

void NativeStudio::PaintEditor(Gdiplus::Graphics& g, const RECT& cli) {
    int W = cli.right, H = cli.bottom;
    // esconder edits de otros modos se hace al pintar cada región
    PaintHeader(g, cli);

    // pestañas de la cinta
    RECT tabs{0, 44, W, 78};
    Gdiplus::SolidBrush wp(ui::ToColor(ui::Paper));
    g.FillRectangle(&wp, (Gdiplus::REAL)(0), (Gdiplus::REAL)(44),  (Gdiplus::REAL)W, (Gdiplus::REAL)(34));
    Gdiplus::Pen lp(ui::ToColor(ui::Border), 1.0f);
    g.DrawLine(&lp, (Gdiplus::REAL)(0),  77.5f,  (Gdiplus::REAL)W,  77.5f);
    static const wchar_t* kTabs[] = {L"Archivo", L"Inicio", L"Insertar", L"Diseño",
                                     L"Transiciones", L"Animaciones", L"Presentación", L"Vista"};
    HDC dcTabs = GetDC(hwnd_);
    int tx = 8;
    for (int i = 0; i < 8; i++) {
        SIZE sz = ui::Measure(dcTabs, ui::Font(13), kTabs[i]);
        RECT r{tx, 44, tx + sz.cx + 26, 78};
        ui::Tab(g, r, kTabs[i], ribbonTab_ == i, hoverId_ == CMD_TAB_BASE + i, i == 0);
        HitAdd(CMD_TAB_BASE + i, 0, 0, r);
        tx += sz.cx + 26;
    }
    ReleaseDC(hwnd_, dcTabs);
    HDC dc = g.GetHDC();
    SelectObject(dc, ui::Font(11));
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, ui::Cref(ui::GrayLt));
    RECT cnt{W - 200, 44, W - 10, 78};
    DrawTextW(dc, (std::to_wstring(SlideTotalCount()) + L" diapositivas").c_str(), -1,
              &cnt, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);

    if (ribbonTab_ == TAB_ARCHIVO) {
        RECT body{0, 78, W, H - 26};
        PaintBackstage(g, body);
        PaintStatus(g, cli);
        return;
    }

    RECT rib{0, 78, W, 164};
    PaintRibbon(g, rib);

    RECT ws{0, 164, W, H - 26};
    Gdiplus::SolidBrush cbg(ui::ToColor(0xFFE6E6E6));
    g.FillRectangle(&cbg, (Gdiplus::REAL)(ws.left), (Gdiplus::REAL)(ws.top),  (Gdiplus::REAL)(ws.right - ws.left), 
                    (Gdiplus::REAL)(ws.bottom - ws.top));

    int left = 0;
    if (paneOpen_) {
        RECT th{0, ws.top, 236, ws.bottom};
        PaintThumbs(g, th);
        left = 236;
    }
    int right = W;
    if (taskOpen_) {
        RECT tp{W - 300, ws.top, W, ws.bottom};
        PaintTaskPane(g, tp);
        right = W - 300;
    }
    RECT center{left, ws.top, right, ws.bottom};
    if (viewSorter_) PaintSorter(g, center);
    else PaintCanvas(g, center);

    PaintStatus(g, cli);
}

// ================================================================== cabecera
void NativeStudio::PaintHeader(Gdiplus::Graphics& g, const RECT& cli) {
    int W = cli.right;
    Gdiplus::SolidBrush wp(ui::ToColor(ui::Paper));
    g.FillRectangle(&wp, (Gdiplus::REAL)(0), (Gdiplus::REAL)(0),  (Gdiplus::REAL)W, (Gdiplus::REAL)(44));
    Gdiplus::Pen lp(ui::ToColor(ui::Border), 1.0f);
    g.DrawLine(&lp, (Gdiplus::REAL)(0),  43.5f,  (Gdiplus::REAL)W,  43.5f);

    // logo
    RECT lg{10, 8, 38, 36};
    ui::RoundRect(g, lg, 4, ui::ToColor(ui::Accent));
    HDC dc = g.GetHDC();
    SelectObject(dc, ui::Font(15, FW_SEMIBOLD));
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, 0xFFFFFF);
    RECT lt = lg;
    DrawTextW(dc, L"F", -1, &lt, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);

    // acceso rápido (web QBtn): guardar, deshacer, rehacer, inicio, importar
    struct QBtn { int id; const char* icon; };
    QBtn qb[] = {{CMD_SAVE, "device-floppy"}, {CMD_UNDO, "arrow-back-up"},
                 {CMD_REDO, "arrow-forward-up"}, {CMD_GOHOME, "home"},
                 {CMD_IMPORT, "upload"}};
    int x = 44;
    for (auto& b : qb) {
        RECT r{x, 9, x + 28, 37};
        bool dis = (b.id == CMD_UNDO && undo_.empty()) ||
                   (b.id == CMD_REDO && redo_.empty());
        ui::IconButton(g, r, b.icon, Hot(b.id, hoverId_) | (dis ? ui::kDis : 0));
        if (!dis) HitAdd(b.id, 0, 0, r);
        x += 30;
    }

    // nombre centrado + pastilla "Guardado automático"
    int nameW = 220;
    RECT np{W / 2 - 170, 12, W / 2 - 30, 32};
    ui::Badge(g, np, L"Guardado automático", ui::Border2, ui::Gray);
    RECT nr{W / 2 - nameW / 2, 8, W / 2 + nameW / 2, 36};
    ui::Field(g, nr, GetFocus() == edtName_, false);
    PlaceEdit(edtName_, RECT{nr.left + 1, nr.top + 1, nr.right - 1, nr.bottom - 1}, true);

    // Presentar (acento)
    RECT pb{W - 130, 8, W - 12, 36};
    ui::Chip(g, pb, L"Presentar", L"F5", "player-play",
             ui::kAcc | Hot(CMD_PRESENT, hoverId_));
    HitAdd(CMD_PRESENT, 0, 0, pb);

    // esconder edits de otros modos
    PlaceEdit(edtSearch_, RECT{}, false);
    PlaceEdit(edtBible_, RECT{}, false);
    PlaceEdit(edtDesc_, RECT{}, false);
}

// ================================================================== cinta
void NativeStudio::PaintRibbon(Gdiplus::Graphics& g, const RECT& r) {
    Gdiplus::SolidBrush wp(ui::ToColor(ui::Paper));
    g.FillRectangle(&wp, (Gdiplus::REAL)(r.left), (Gdiplus::REAL)(r.top),  (Gdiplus::REAL)(r.right - r.left), 
                    (Gdiplus::REAL)(r.bottom - r.top));
    Gdiplus::Pen lp(ui::ToColor(ui::Border), 1.0f);
    g.DrawLine(&lp, (Gdiplus::REAL)(0),  (Gdiplus::REAL)r.bottom - 0.5f,  (Gdiplus::REAL)r.right, 
               (Gdiplus::REAL)r.bottom - 0.5f);

    auto BigBtn = [&](int x, int y, const wchar_t* label, const wchar_t* sub,
                      const char* icon, int id, int flags = 0, int w = 74) {
        RECT b{x, y, x + w, y + 52};
        ui::Chip(g, b, label, sub, icon, flags | Hot(id, hoverId_));
        HitAdd(id, 0, 0, b);
    };
    auto Mini = [&](int x, int y, const wchar_t* label, int id, int flags = 0, int w = 108) {
        RECT b{x, y, x + w, y + 23};
        ui::Chip(g, b, label, L"", "", flags | Hot(id, hoverId_));
        HitAdd(id, 0, 0, b);
    };
    auto SepAt = [&](int x) {
        ui::Sep(g, RECT{x, r.top + 8, x + 5, r.bottom - 18});
    };
    auto GroupLbl = [&](int x, int w, const wchar_t* label) {
        ui::GroupLabel(g, RECT{x, r.bottom - 17, x + w, r.bottom - 2}, label);
    };

    int y0 = r.top + 6;
    switch (ribbonTab_) {
        case TAB_INICIO: {
            int x = 10;
            BigBtn(x, y0, L"Nueva", L"diapositiva", "plus", CMD_ADD_TITLE, ui::kAcc); x += 80;
            Mini(x, y0 - 2, L"Canto", CMD_ADD_SONG); Mini(x, y0 + 22, L"Versículo", CMD_ADD_VERSE);
            Mini(x, y0 + 46, L"Tercio", CMD_ADD_LT); x += 112;
            Mini(x, y0 - 2, L"Pausa", CMD_ADD_BLANK);
            Mini(x, y0 + 22, L"Quitar sección", CMD_REMOVE_SECTION, ui::kDanger);
            Mini(x, y0 + 46, L"Duplicar presentación", CMD_DUP_PRES, 0, 128); x += 132;
            Mini(x, y0 - 2, L"Pegar", CMD_PASTE, draft_.clipSlides_.empty() ? ui::kDis : 0);
            Mini(x, y0 + 22, L"Duplicar diapos.", CMD_DUP_SLIDES);
            Mini(x, y0 + 46, L"Eliminar diapos.", CMD_DEL_SLIDES, ui::kDanger);
            GroupLbl(10, 360, L"Diapositivas");
            SepAt(395); x = 405;
            // Fuente: A- [n] A+ · alineación · tercio
            RECT fa{x, y0 + 2, x + 34, y0 + 26};
            ui::Chip(g, fa, L"A−", L"", "", Hot(CMD_FONT_DOWN, hoverId_));
            HitAdd(CMD_FONT_DOWN, 0, 0, fa);
            HDC dc = g.GetHDC();
            SelectObject(dc, ui::Font(11, FW_SEMIBOLD));
            SetBkMode(dc, TRANSPARENT);
            SetTextColor(dc, ui::Cref(ui::Ink));
            RECT fv{x + 38, y0 + 2, x + 76, y0 + 26};
            DrawTextW(dc, L"44", -1, &fv, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
            g.ReleaseHDC(dc);
            RECT fx{x + 80, y0 + 2, x + 114, y0 + 26};
            ui::Chip(g, fx, L"A+", L"", "", Hot(CMD_FONT_UP, hoverId_));
            HitAdd(CMD_FONT_UP, 0, 0, fx);
            struct ABtn { int id; const char* icon; };
            ABtn ab[] = {{CMD_ALIGN_L, "align-left"}, {CMD_ALIGN_C, "align-center"},
                         {CMD_ALIGN_R, "align-right"}};
            for (int i = 0; i < 3; i++) {
                RECT b{x + i * 34, y0 + 32, x + i * 34 + 32, y0 + 64};
                DraftSlide* sl = SelSlide();
                bool on = sl && sl->align == i;
                ui::IconButton(g, b, ab[i].icon, (on ? ui::kOn : 0) | Hot(ab[i].id, hoverId_));
                HitAdd(ab[i].id, 0, 0, b);
            }
            GroupLbl(x - 10, 190, L"Fuente");
            SepAt(x + 190); int x2 = x + 200;
            BigBtn(x2, y0, L"Tercio", L"inferior", "app-window-bottom", CMD_TOGGLE_LT,
                   SelSlide() && SelSlide()->lowerThird ? ui::kOn : 0);
            GroupLbl(x2, 84, L"Formato");
            SepAt(x2 + 90); int x3 = x2 + 100;
            BigBtn(x3, y0, L"Subir", L"sección", "chevron-up", CMD_ITEM_UP,
                   draft_.selItem <= 0 ? ui::kDis : 0);
            BigBtn(x3 + 80, y0, L"Bajar", L"sección", "chevron-down", CMD_ITEM_DOWN,
                   draft_.selItem >= (int)draft_.items.size() - 1 ? ui::kDis : 0);
            GroupLbl(x3, 160, L"Organizar");
            SepAt(x3 + 166); int x4 = x3 + 176;
            BigBtn(x4, y0, L"Presentar", L"F5", "player-play", CMD_PRESENT, ui::kAcc);
            BigBtn(x4 + 80, y0, L"Proyector", L"ventana", "device-desktop", CMD_PROYECTOR);
            GroupLbl(x4, 160, L"Presentar");
            break;
        }
        case TAB_INSERTAR: {
            int x = 10;
            BigBtn(x, y0, L"Canto", L"biblioteca", "music", CMD_ADD_SONG); x += 80;
            BigBtn(x, y0, L"Versículo", L"biblioteca", "book", CMD_ADD_VERSE); x += 80;
            BigBtn(x, y0, L"Medios", L"imagen · vídeo", "photo", CMD_ADD_SONG); x += 96;
            GroupLbl(10, 260, L"Contenido bíblico");
            SepAt(280); x = 290;
            BigBtn(x, y0, L"Título", L"Ctrl+M", "heading", CMD_ADD_TITLE); x += 80;
            BigBtn(x, y0, L"Tercio", L"inferior", "app-window-bottom", CMD_ADD_LT); x += 80;
            BigBtn(x, y0, L"Pausa", L"en negro", "square", CMD_ADD_BLANK);
            GroupLbl(290, 240, L"Diapositivas");
            SepAt(540); x = 550;
            // abrir biblioteca en temas
            RECT tb{x, y0, x + 84, y0 + 52};
            ui::Chip(g, tb, L"Temas", L"ver todo", "palette",
                     Hot(CMD_LIB_TAB_BASE + LIB_TEMAS, hoverId_));
            HitAdd(CMD_LIB_TAB_BASE + LIB_TEMAS, 0, 0, tb);
            GroupLbl(550, 90, L"Biblioteca");
            break;
        }
        case TAB_DISENO: {
            // tarjetas de tema (clic aplica al elemento) — como la web
            int x = 10;
            DraftSlide* sl = SelSlide();
            for (size_t i = 0; i < Themes().size(); i++) {
                const ThemeDef& t = Themes()[i];
                RECT c{x, y0, x + 118, y0 + 58};
                bool on = sl && sl->themeId == t.id;
                Gdiplus::Pen pn(ui::ToColor(on ? ui::Accent : ui::Border2), on ? 2.0f : 1.0f);
                Gdiplus::SolidBrush wb(ui::ToColor(ui::Paper));
                g.FillRectangle(&wb, (Gdiplus::REAL)(c.left), (Gdiplus::REAL)(c.top),  (Gdiplus::REAL)(c.right - c.left - 1), 
                                (Gdiplus::REAL)(c.bottom - c.top - 1));
                g.DrawRectangle(&pn, (Gdiplus::REAL)(c.left), (Gdiplus::REAL)(c.top),  (Gdiplus::REAL)(c.right - c.left - 1), 
                                (Gdiplus::REAL)(c.bottom - c.top - 1));
                // muestra: franja oscura + bloque texto + bloque acento
                Gdiplus::SolidBrush ib(ui::ToColor(ui::Ink));
                g.FillRectangle(&ib, (Gdiplus::REAL)(c.left + 1), (Gdiplus::REAL)(c.top + 1),  (Gdiplus::REAL)(c.right - c.left - 2), (Gdiplus::REAL)(22));
                Gdiplus::SolidBrush tb2(ui::ToColor(0xE6FFFFFF));   // blanco 90 % sobre la muestra
                g.FillRectangle(&tb2, c.left + 8, c.top + 6, 26, 12);
                Gdiplus::SolidBrush ab(ui::ToColor(ParseColor(t.activeColor, 0xFFE8C872)));
                g.FillRectangle(&ab, c.left + 38, c.top + 6, 12, 12);
                HDC dc = g.GetHDC();
                SelectObject(dc, ui::Font(11));
                SetBkMode(dc, TRANSPARENT);
                SetTextColor(dc, ui::Cref(ui::Ink));
                RECT tr{c.left + 6, c.top + 26, c.right - 4, c.bottom - 2};
                DrawTextW(dc, ToWide(t.name).c_str(), -1, &tr,
                          DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
                g.ReleaseHDC(dc);
                HitAdd(CMD_THEME_BASE + (int)i, 0, 0, c);
                x += 124;
            }
            GroupLbl(10, Themes().size() * 124 + 10, L"Temas: clic aplica al elemento");
            SepAt(x + 6); int bx = x + 16;
            // fondos (clic aplica a la diapositiva)
            for (size_t i = 0; i < Backgrounds().size(); i++) {
                const BgDef& b = Backgrounds()[i];
                RECT c{bx + (int)i * 80, y0, bx + (int)i * 80 + 76, y0 + 48};
                bool on = sl && sl->bgId == b.id;
                Gdiplus::Pen pn(ui::ToColor(on ? ui::Accent : ui::Border2), on ? 2.0f : 1.0f);
                Gdiplus::SolidBrush bb(ui::ToColor(ParseColor(b.color, 0xFF000000)));
                g.FillRectangle(&bb, (Gdiplus::REAL)(c.left), (Gdiplus::REAL)(c.top),  (Gdiplus::REAL)(c.right - c.left - 1), 
                                (Gdiplus::REAL)(c.bottom - c.top - 1));
                g.DrawRectangle(&pn, (Gdiplus::REAL)(c.left), (Gdiplus::REAL)(c.top),  (Gdiplus::REAL)(c.right - c.left - 1), 
                                (Gdiplus::REAL)(c.bottom - c.top - 1));
                Gdiplus::Bitmap* img = ui::Icon("", ui::TintInk); (void)img;
                HitAdd(CMD_BG_BASE + (int)i, 0, 0, c);
            }
            GroupLbl(bx, Backgrounds().size() * 80 + 6, L"Fondos: clic aplica a la diapositiva");
            int ax = bx + (int)Backgrounds().size() * 80 + 16;
            SepAt(ax - 10);
            BigBtn(ax, y0, L"Aplicar", L"a todo", "check", CMD_APPLY_THEME_ALL);
            break;
        }
        case TAB_TRANSICIONES: {
            static const struct { const char* v; const wchar_t* label, *hint; const char* icon; }
            opts[] = {{"cut", L"Ninguna", L"corte", "minus"},
                      {"fade", L"Fundido", L"suave", "sparkles"},
                      {"slide", L"Desplazar", L"lateral", "chevron-right"}};
            DraftSlide* sl = SelSlide();
            std::string cur = sl ? (sl->transition.empty() ? "fade" : sl->transition) : "fade";
            int x = 10;
            for (auto& o : opts) {
                RECT c{x, y0, x + 100, y0 + 58};
                bool on = cur == o.v;
                Gdiplus::Pen pn(ui::ToColor(on ? ui::Accent : ui::Border2), on ? 2.0f : 1.0f);
                Gdiplus::SolidBrush wb(ui::ToColor(ui::Paper));
                g.FillRectangle(&wb, (Gdiplus::REAL)(c.left), (Gdiplus::REAL)(c.top),  (Gdiplus::REAL)(c.right - c.left - 1), 
                                (Gdiplus::REAL)(c.bottom - c.top - 1));
                g.DrawRectangle(&pn, (Gdiplus::REAL)(c.left), (Gdiplus::REAL)(c.top),  (Gdiplus::REAL)(c.right - c.left - 1), 
                                (Gdiplus::REAL)(c.bottom - c.top - 1));
                Gdiplus::SolidBrush gb(ui::ToColor(ui::HoverBg));
                g.FillRectangle(&gb, (Gdiplus::REAL)(c.left + 4), (Gdiplus::REAL)(c.top + 4),  (Gdiplus::REAL)(c.right - c.left - 9), (Gdiplus::REAL)(26));
                if (ui::Icon(o.icon, ui::TintInk))
                    g.DrawImage(ui::Icon(o.icon, ui::TintInk), c.left + (c.right - c.left) / 2 - 10,
                                c.top + 7, 20, 20);
                HDC dc = g.GetHDC();
                SelectObject(dc, ui::Font(12, FW_SEMIBOLD));
                SetBkMode(dc, TRANSPARENT);
                SetTextColor(dc, ui::Cref(ui::Ink));
                RECT tr{c.left, c.top + 32, c.right, c.top + 46};
                DrawTextW(dc, o.label, -1, &tr, DT_CENTER | DT_SINGLELINE);
                SelectObject(dc, ui::Font(10));
                SetTextColor(dc, ui::Cref(ui::GrayLt));
                RECT hr{c.left, c.top + 46, c.right, c.bottom - 2};
                DrawTextW(dc, o.hint, -1, &hr, DT_CENTER | DT_SINGLELINE);
                g.ReleaseHDC(dc);
                // id: codifico transición como CMD_SLIDE_PREV + índice no — uso THEME base? no.
                // Uso CMD_BG_BASE+64+i (reservado): mejor un base propio:
                HitAdd(CMD_TRANS_BASE + (int)(&o - opts), 0, 0, c);
                x += 106;
            }
            GroupLbl(10, 330, L"Transición de esta diapositiva");
            SepAt(x + 6);
            // predeterminada (global)
            int px = x + 16;
            for (int i = 0; i < 3; i++) {
                bool on = DefaultTransition() == std::string(opts[i].v);
                Mini(px, y0 + i * 26, opts[i].label, CMD_TRANSDEF_BASE + i,
                     on ? ui::kOn : 0, 100);
                px += 104;
            }
            GroupLbl(px - 312, 320, L"Predeterminada");
            break;
        }
        case TAB_ANIMACIONES: {
            int x = 10;
            BigBtn(x, y0, L"Por línea", L"cantos", "list", CMD_OPT_ADVANCE,
                   advance_ == "line" ? ui::kOn : 0); x += 80;
            BigBtn(x, y0, L"Por diapositiva", L"completa", "layout-grid", CMD_OPT_ADVANCE,
                   advance_ == "slide" ? ui::kOn : 0); x += 110;
            GroupLbl(10, 210, L"Avance en vivo");
            SepAt(230); x = 240;
            BigBtn(x, y0, L"Animación", animate_ ? L"sí" : L"no", "sparkles", CMD_OPT_ANIM,
                   animate_ ? ui::kOn : 0); x += 80;
            BigBtn(x, y0, L"Reloj", L"consola", "clock", CMD_OPT_CLOCK,
                   showClock_ ? ui::kOn : 0);
            GroupLbl(240, 170, L"Salida");
            break;
        }
        case TAB_PRESENTACION: {
            int x = 10;
            BigBtn(x, y0, L"Desde el inicio", L"F5", "player-play", CMD_PRESENT, ui::kAcc, 110); x += 116;
            BigBtn(x, y0, L"Proyector", L"ventana", "device-desktop", CMD_PROYECTOR); x += 80;
            GroupLbl(10, 200, L"Iniciar presentación");
            SepAt(220); x = 230;
            // Salidas: Mando (OBS eliminado por decisión del usuario)
            BigBtn(x, y0, L"Mando", L"táctil", "device-mobile", CMD_MANDO);
            GroupLbl(230, 90, L"Salidas");
            SepAt(330); x = 340;
            BigBtn(x, y0, L"PNG", L"actual", "photo", CMD_BS_PNG_CUR); x += 74;
            BigBtn(x, y0, L"PNG", L"todas", "layout-grid", CMD_BS_PNG_ALL); x += 74;
            BigBtn(x, y0, L"JSON", L"Ctrl+S", "download", CMD_BS_JSON); x += 74;
            BigBtn(x, y0, L"CSV", L"historial", "file-text", CMD_BS_CSV);
            GroupLbl(340, 310, L"Exportar");
            break;
        }
        case TAB_VISTA: {
            int x = 10;
            BigBtn(x, y0, L"Normal", L"edición", "heading", CMD_VIEW_NORMAL,
                   !viewSorter_ ? ui::kOn : 0); x += 80;
            BigBtn(x, y0, L"Clasificador", L"cuadrícula", "layout-grid", CMD_VIEW_SORTER,
                   viewSorter_ ? ui::kOn : 0); x += 100;
            GroupLbl(10, 190, L"Vistas de presentación");
            SepAt(210); x = 220;
            BigBtn(x, y0, L"Miniaturas", paneOpen_ ? L"visible" : L"oculto", "layout-grid",
                   CMD_TOGGLE_PANE, paneOpen_ ? ui::kOn : 0); x += 96;
            BigBtn(x, y0, L"Notas", notesOpen_ ? L"visible" : L"oculto", "list",
                   CMD_TOGGLE_NOTES, notesOpen_ ? ui::kOn : 0); x += 80;
            BigBtn(x, y0, L"Panel", taskOpen_ ? L"visible" : L"oculto", "palette",
                   CMD_TOGGLE_TASK, taskOpen_ ? ui::kOn : 0);
            GroupLbl(220, 270, L"Mostrar");
            SepAt(500); x = 510;
            BigBtn(x, y0, L"Métodos", L"abreviados", "keyboard", CMD_SHORTCUTS); x += 80;
            BigBtn(x, y0, L"Opciones", L"ajustes", "settings", CMD_OPTIONS);
            GroupLbl(510, 170, L"Ayuda");
            break;
        }
        default: break;
    }
}

// ================================================================== backstage
void NativeStudio::PaintBackstage(Gdiplus::Graphics& g, const RECT& r) {
    // barra lateral roja
    Gdiplus::SolidBrush rb(ui::ToColor(ui::Accent));
    g.FillRectangle(&rb, (Gdiplus::REAL)(r.left), (Gdiplus::REAL)(r.top), (Gdiplus::REAL)(220),  (Gdiplus::REAL)(r.bottom - r.top));
    HDC dc = g.GetHDC();
    SelectObject(dc, ui::Font(15, FW_SEMIBOLD));
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, 0xFFFFFF);
    RECT ht{r.left + 16, r.top + 10, 220, r.top + 34};
    DrawTextW(dc, L"Archivo", -1, &ht, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);
    static const wchar_t* pages[] = {L"Información", L"Nuevo", L"Abrir", L"Exportar", L"Opciones"};
    int y = r.top + 44;
    for (int i = 0; i < 5; i++) {
        RECT pr{r.left, y, 220, y + 32};
        if (backPage_ == i) {
            Gdiplus::SolidBrush dk(0x33000000);
            g.FillRectangle(&dk, (Gdiplus::REAL)(pr.left), (Gdiplus::REAL)(pr.top),  (Gdiplus::REAL)(pr.right - pr.left), 
                            (Gdiplus::REAL)(pr.bottom - pr.top));
        } else if (hoverId_ == CMD_BS_PAGE_BASE + i) {
            Gdiplus::SolidBrush hk(0x1A000000);
            g.FillRectangle(&hk, (Gdiplus::REAL)(pr.left), (Gdiplus::REAL)(pr.top),  (Gdiplus::REAL)(pr.right - pr.left), 
                            (Gdiplus::REAL)(pr.bottom - pr.top));
        }
        dc = g.GetHDC();
        SelectObject(dc, ui::Font(13, backPage_ == i ? FW_SEMIBOLD : FW_NORMAL));
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, 0xFFFFFF);
        RECT tr{r.left + 16, y, 214, y + 32};
        DrawTextW(dc, pages[i], -1, &tr, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
        g.ReleaseHDC(dc);
        HitAdd(CMD_BS_PAGE_BASE + i, 0, 0, pr);
        y += 32;
    }
    // botones inferiores
    RECT sb1{r.left + 8, r.bottom - 70, 212, r.bottom - 42};
    if (hoverId_ == CMD_GOHOME) {
        Gdiplus::SolidBrush hk(0x1A000000);
        g.FillRectangle(&hk, (Gdiplus::REAL)(sb1.left), (Gdiplus::REAL)(sb1.top),  (Gdiplus::REAL)(sb1.right - sb1.left), 
                        (Gdiplus::REAL)(sb1.bottom - sb1.top));
    }
    dc = g.GetHDC();
    SelectObject(dc, ui::Font(13));
    SetTextColor(dc, 0xFFFFFF);
    RECT s1 = sb1;
    DrawTextW(dc, L"Pantalla de inicio", -1, &s1, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);
    HitAdd(CMD_GOHOME, 0, 0, sb1);
    RECT sb2{r.left + 8, r.bottom - 38, 212, r.bottom - 10};
    Gdiplus::SolidBrush wb2(0x26FFFFFF);
    g.FillRectangle(&wb2, (Gdiplus::REAL)(sb2.left), (Gdiplus::REAL)(sb2.top),  (Gdiplus::REAL)(sb2.right - sb2.left), 
                    (Gdiplus::REAL)(sb2.bottom - sb2.top));
    dc = g.GetHDC();
    SelectObject(dc, ui::Font(13, FW_SEMIBOLD));
    SetTextColor(dc, 0xFFFFFF);
    RECT s2 = sb2;
    DrawTextW(dc, L"Cerrar y volver a la cinta", -1, &s2, DT_LEFT | DT_VCENTER |
              DT_WORDBREAK);
    g.ReleaseHDC(dc);
    HitAdd(CMD_BS_CLOSE, 0, 0, sb2);

    // página
    RECT pg{220, r.top, r.right, r.bottom};
    Gdiplus::SolidBrush cb(ui::ToColor(ui::CanvasBg));
    g.FillRectangle(&cb, (Gdiplus::REAL)(pg.left), (Gdiplus::REAL)(pg.top),  (Gdiplus::REAL)(pg.right - pg.left), 
                    (Gdiplus::REAL)(pg.bottom - pg.top));
    dc = g.GetHDC();
    SetBkMode(dc, TRANSPARENT);
    auto H1 = [&](const wchar_t* t) {
        SelectObject(dc, ui::Font(26, FW_SEMIBOLD));
        SetTextColor(dc, ui::Cref(ui::Ink));
        RECT hr{pg.left + 32, pg.top + 18, pg.left + 700, pg.top + 56};
        DrawTextW(dc, t, -1, &hr, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    };

    switch (backPage_) {
        case BP_INFO: {
            H1(L"Información");
            SelectObject(dc, ui::Font(13));
            SetTextColor(dc, ui::Cref(ui::Gray));
            RECT sub{pg.left + 32, pg.top + 56, pg.right - 32, pg.top + 78};
            DrawTextW(dc, (draft_.name + L" · " + std::to_wstring(draft_.items.size()) +
                           L" secciones · " + std::to_wstring(SlideTotalCount()) +
                           L" diapositivas").c_str(), -1, &sub, DT_LEFT | DT_SINGLELINE);
            g.ReleaseHDC(dc);
            // tarjeta: nombre + descripción
            RECT c1{pg.left + 32, pg.top + 92, pg.left + 32 + 420, pg.top + 300};
            Gdiplus::Pen pn(ui::ToColor(ui::Border2));
            Gdiplus::SolidBrush wb(ui::ToColor(ui::Paper));
            g.FillRectangle(&wb, (Gdiplus::REAL)(c1.left), (Gdiplus::REAL)(c1.top),  (Gdiplus::REAL)(c1.right - c1.left - 1), (Gdiplus::REAL)(c1.bottom - c1.top - 1));
            g.DrawRectangle(&pn, (Gdiplus::REAL)(c1.left), (Gdiplus::REAL)(c1.top),  (Gdiplus::REAL)(c1.right - c1.left - 1), 
                            (Gdiplus::REAL)(c1.bottom - c1.top - 1));
            dc = g.GetHDC();
            SetBkMode(dc, TRANSPARENT);
            SelectObject(dc, ui::Font(13, FW_SEMIBOLD));
            SetTextColor(dc, ui::Cref(ui::Ink));
            RECT l1{c1.left + 14, c1.top + 8, c1.right, c1.top + 28};
            DrawTextW(dc, L"Nombre", -1, &l1, DT_LEFT);
            g.ReleaseHDC(dc);
            RECT ne{c1.left + 14, c1.top + 32, c1.right - 14, c1.top + 60};
            ui::Field(g, ne, GetFocus() == edtName_, false);
            PlaceEdit(edtName_, RECT{ne.left + 1, ne.top + 1, ne.right - 1, ne.bottom - 1}, true);
            dc = g.GetHDC();
            SetBkMode(dc, TRANSPARENT);
            SelectObject(dc, ui::Font(13, FW_SEMIBOLD));
            SetTextColor(dc, ui::Cref(ui::Ink));
            RECT l2{c1.left + 14, c1.top + 66, c1.right, c1.top + 86};
            DrawTextW(dc, L"Descripción", -1, &l2, DT_LEFT);
            g.ReleaseHDC(dc);
            RECT de{c1.left + 14, c1.top + 90, c1.right - 14, c1.bottom - 14};
            ui::Field(g, de, GetFocus() == edtDesc_, false);
            PlaceEdit(edtDesc_, RECT{de.left + 2, de.top + 2, de.right - 2, de.bottom - 2}, true);
            break;
        }
        case BP_NUEVO: {
            H1(L"Nuevo");
            g.ReleaseHDC(dc);
            int bx = pg.left + 32, by = pg.top + 92;
            struct BN { const wchar_t* l; int id; bool accent; };
            BN bs[] = {{L"Presentación en blanco", CMD_BS_BLANK, true},
                       {L"Duplicar actual", CMD_BS_DUP, false}};
            for (auto& b : bs) {
                RECT r2{bx, by, bx + 220, by + 36};
                ui::Chip(g, r2, b.l, L"", "", (b.accent ? ui::kAcc : 0) | Hot(b.id, hoverId_));
                HitAdd(b.id, 0, 0, r2);
                by += 44;
            }
            dc = g.GetHDC();
            SelectObject(dc, ui::Font(13));
            SetTextColor(dc, ui::Cref(ui::Gray));
            RECT sub{pg.left + 32, pg.top + 56, pg.right - 32, pg.top + 78};
            DrawTextW(dc, L"Empieza en blanco o duplica la presentación actual.", -1, &sub,
                      DT_LEFT | DT_SINGLELINE);
            g.ReleaseHDC(dc);
            break;
        }
        case BP_ABRIR: {
            H1(L"Abrir");
            g.ReleaseHDC(dc);
            RECT bb{pg.left + 32, pg.top + 92, pg.left + 32 + 220, pg.top + 128};
            ui::Chip(g, bb, L"Examinar .ahp", L"", "folder-open",
                     ui::kAcc | Hot(CMD_BS_BROWSE, hoverId_));
            HitAdd(CMD_BS_BROWSE, 0, 0, bb);
            int ry = pg.top + 140;
            dc = g.GetHDC();
            for (auto& rc : recientes_) {
                Gdiplus::SolidBrush wb(ui::ToColor(ui::Paper));
                Gdiplus::Pen pn(ui::ToColor(ui::Border2));
                RECT r2{pg.left + 32, ry, pg.right - 32, ry + 38};
                g.FillRectangle(&wb, (Gdiplus::REAL)(r2.left), (Gdiplus::REAL)(r2.top),  (Gdiplus::REAL)(r2.right - r2.left - 1), 
                                (Gdiplus::REAL)(r2.bottom - r2.top - 1));
                g.DrawRectangle(&pn, (Gdiplus::REAL)(r2.left), (Gdiplus::REAL)(r2.top),  (Gdiplus::REAL)(r2.right - r2.left - 1), 
                                (Gdiplus::REAL)(r2.bottom - r2.top - 1));
                SetBkMode(dc, TRANSPARENT);
                SelectObject(dc, ui::Font(13));
                SetTextColor(dc, ui::Cref(ui::Ink));
                RECT tr{r2.left + 12, r2.top, r2.right, r2.bottom};
                DrawTextW(dc, (rc.name + L"  ·  " + rc.path).c_str(), -1, &tr,
                          DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
                g.ReleaseHDC(dc);
                int idx = (int)(&rc - &recientes_[0]);
                HitAdd(CMD_RECENT_BASE + idx, idx, 0, r2);
                dc = g.GetHDC();
                ry += 44;
            }
            g.ReleaseHDC(dc);
            break;
        }
        case BP_EXPORTAR: {
            H1(L"Exportar");
            g.ReleaseHDC(dc);
            struct BE { const wchar_t* l; const wchar_t* s; int id; const char* icon; bool acc; };
            BE bs[] = {{L"PNG", L"diapositiva actual", CMD_BS_PNG_CUR, "photo", false},
                       {L"PNG", L"todas las diapositivas", CMD_BS_PNG_ALL, "layout-grid", false},
                       {L"JSON", L"proyecto .ahp", CMD_BS_JSON, "download", true},
                       {L"CSV", L"historial de uso", CMD_BS_CSV, "file-text", false}};
            int bx = pg.left + 32, by = pg.top + 92;
            for (auto& b : bs) {
                RECT r2{bx, by, bx + 240, by + 54};
                ui::Chip(g, r2, b.l, b.s, b.icon, (b.acc ? ui::kAcc : 0) | Hot(b.id, hoverId_));
                HitAdd(b.id, 0, 0, r2);
                by += 62;
            }
            break;
        }
        case BP_OPCIONES: {
            H1(L"Opciones");
            g.ReleaseHDC(dc);
            optionsOpen_ = false;     // las opciones viven aquí también en backstage
            struct BO { const wchar_t* l; int id; bool on; };
            BO bs[] = {{L"Avance línea a línea", CMD_OPT_ADVANCE, advance_ == "line"},
                       {L"Mostrar reloj", CMD_OPT_CLOCK, showClock_},
                       {L"Animaciones", CMD_OPT_ANIM, animate_}};
            int by = pg.top + 92;
            RECT c1{pg.left + 32, by, pg.left + 32 + 420, by + 3 * 46 + 12};
            Gdiplus::Pen pn(ui::ToColor(ui::Border2));
            Gdiplus::SolidBrush wb(ui::ToColor(ui::Paper));
            g.FillRectangle(&wb, (Gdiplus::REAL)(c1.left), (Gdiplus::REAL)(c1.top),  (Gdiplus::REAL)(c1.right - c1.left - 1), 
                            (Gdiplus::REAL)(c1.bottom - c1.top - 1));
            g.DrawRectangle(&pn, (Gdiplus::REAL)(c1.left), (Gdiplus::REAL)(c1.top),  (Gdiplus::REAL)(c1.right - c1.left - 1), 
                            (Gdiplus::REAL)(c1.bottom - c1.top - 1));
            int ry = by + 8;
            for (auto& b : bs) {
                RECT tg{c1.right - 54, ry + 10, c1.right - 22, ry + 26};
                ui::RoundRect(g, tg, 8, ui::ToColor(b.on ? 0xFF8CE0A4 : 0xFFC8C6C4));
                Gdiplus::SolidBrush kb(ui::ToColor(ui::Paper));
                Gdiplus::REAL kx = b.on ? tg.right - 14 : tg.left + 2;
                g.FillEllipse(&kb, (Gdiplus::REAL)(kx),  (Gdiplus::REAL)tg.top + 2, (Gdiplus::REAL)(12), (Gdiplus::REAL)(12));
                dc = g.GetHDC();
                SelectObject(dc, ui::Font(13, b.on ? FW_SEMIBOLD : FW_NORMAL));
                SetBkMode(dc, TRANSPARENT);
                SetTextColor(dc, b.on ? ui::Cref(ui::AccentDk) : ui::Cref(ui::Ink));
                RECT tr{c1.left + 14, ry, c1.right - 60, ry + 42};
                DrawTextW(dc, b.l, -1, &tr, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
                g.ReleaseHDC(dc);
                RECT row{c1.left, ry, c1.right, ry + 42};
                HitAdd(b.id, 0, 0, row);
                ry += 46;
            }
            break;
        }
    }
    PlaceEdit(edtSearch_, RECT{}, false);
    PlaceEdit(edtTitle_, RECT{}, false);
    PlaceEdit(edtSub_, RECT{}, false);
    PlaceEdit(edtLines_, RECT{}, false);
    PlaceEdit(edtNotes_, RECT{}, false);
    PlaceEdit(edtBible_, RECT{}, false);
}

// ================================================================== miniaturas
void NativeStudio::PaintThumbs(Gdiplus::Graphics& g, const RECT& r) {
    Gdiplus::SolidBrush wp(ui::ToColor(ui::Paper));
    g.FillRectangle(&wp, (Gdiplus::REAL)(r.left), (Gdiplus::REAL)(r.top),  (Gdiplus::REAL)(r.right - r.left), 
                    (Gdiplus::REAL)(r.bottom - r.top));
    Gdiplus::Pen lp(ui::ToColor(ui::Border), 1.0f);
    g.DrawLine(&lp,  (Gdiplus::REAL)r.right - 0.5f, (Gdiplus::REAL)(r.top),  (Gdiplus::REAL)r.right - 0.5f, 
               (Gdiplus::REAL)r.bottom);
    HDC dc = g.GetHDC();
    SelectObject(dc, ui::Font(12, FW_SEMIBOLD));
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, ui::Cref(ui::Ink));
    RECT ht{r.left + 10, r.top + 6, r.right - 40, r.top + 28};
    DrawTextW(dc, L"Diapositivas", -1, &ht, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);
    RECT add{r.right - 34, r.top + 5, r.right - 8, r.top + 31};
    ui::IconButton(g, add, "plus", Hot(CMD_ADD_TITLE, hoverId_));
    HitAdd(CMD_ADD_TITLE, 0, 0, add);

    // lista con scroll
    RECT listR{r.left, r.top + 34, r.right, r.bottom};
    int total = 0;
    for (auto& it : draft_.items)
        total += 26 + (int)it.slides.size() * 96 + 14;
    ScrollAdd(listR, scThumbs_, std::max(0, (int)(total - (listR.bottom - listR.top))));

    int y = listR.top - scThumbs_;
    int n = 0;      // numeración global de diapositivas
    for (int i = 0; i < (int)draft_.items.size(); i++) {
        auto& it = draft_.items[(size_t)i];
        // cabecera de sección
        RECT hr{r.left + 4, y, r.right - 4, y + 22};
        bool sel = (draft_.selItem == i);
        dc = g.GetHDC();
        SelectObject(dc, ui::Font(11, FW_SEMIBOLD));
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, ui::Cref(ui::Gray));
        std::wstring lbl = it.label;
        if (lbl.empty()) lbl = L"Sección";
        RECT tr{hr.left + 4, hr.top, hr.right - 76, hr.bottom};
        DrawTextW(dc, (CharUpperW(&lbl[0]), lbl).c_str(), -1, &tr,
                  DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
        SelectObject(dc, ui::Font(10));
        SetTextColor(dc, ui::Cref(ui::GrayLt));
        RECT rr{hr.right - 74, hr.top, hr.right - 6, hr.bottom};
        int start = n + 1;
        DrawTextW(dc, (std::to_wstring(start) + L"–" + std::to_wstring(start + (int)it.slides.size() - 1)).c_str(),
                  -1, &rr, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
        g.ReleaseHDC(dc);
        HitAdd(CMD_T_ITEM_BASE + i, i, 0, RECT{hr.left, hr.top, hr.right - 76, hr.bottom});
        // mini botones subir/bajar/quitar
        RECT up{hr.right - 70, hr.top + 2, hr.right - 54, hr.bottom - 2};
        RECT dn{hr.right - 52, hr.top + 2, hr.right - 36, hr.bottom - 2};
        RECT del{hr.right - 34, hr.top + 2, hr.right - 18, hr.bottom - 2};
        ui::IconButton(g, up, "chevron-up", (i <= 0 ? ui::kDis : 0) | Hot(CMD_T_UP, hoverId_));
        if (i > 0) HitAdd(CMD_T_UP, i, 0, up);
        ui::IconButton(g, dn, "chevron-down",
                       (i >= (int)draft_.items.size() - 1 ? ui::kDis : 0) | Hot(CMD_T_DOWN, hoverId_));
        if (i < (int)draft_.items.size() - 1) HitAdd(CMD_T_DOWN, i, 0, dn);
        ui::IconButton(g, del, "trash", ui::kDanger | Hot(CMD_T_DEL, hoverId_));
        HitAdd(CMD_T_DEL, i, 0, del);
        y += 26;
        // diapositivas de la sección
        for (int j = 0; j < (int)it.slides.size(); j++) {
            auto& sl = it.slides[(size_t)j];
            if (sl.type != "blank") n++;
            RECT tr2{r.left + 10, y, r.right - 10, y + 90};
            if (tr2.bottom > listR.top && tr2.top < listR.bottom) {
                // número
                dc = g.GetHDC();
                SelectObject(dc, ui::Font(11, sel && draft_.selSlide == j ? FW_BOLD : FW_NORMAL));
                SetBkMode(dc, TRANSPARENT);
                SetTextColor(dc, sel && draft_.selSlide == j ? ui::Cref(ui::Accent)
                                                             : ui::Cref(ui::GrayLt));
                RECT nr2{tr2.left, tr2.top + 2, tr2.left + 20, tr2.top + 20};
                DrawTextW(dc, std::to_wstring(sl.type == "blank" ? 0 : n).c_str(), -1, &nr2,
                          DT_RIGHT | DT_SINGLELINE);
                g.ReleaseHDC(dc);
                // miniatura 16:9
                int tw = tr2.right - tr2.left - 24, thh = tw * 9 / 16;
                RECT tb{tr2.left + 24, tr2.top, tr2.left + 24 + tw, tr2.top + thh};
                if (sl.type == "blank") {
                    Gdiplus::SolidBrush bk(ui::ToColor(0xFF111111));
                    g.FillRectangle(&bk, (Gdiplus::REAL)(tb.left), (Gdiplus::REAL)(tb.top),  (Gdiplus::REAL)(tb.right - tb.left), 
                                    (Gdiplus::REAL)(tb.bottom - tb.top));
                    dc = g.GetHDC();
                    SelectObject(dc, ui::Font(10));
                    SetTextColor(dc, 0x999999);
                    RECT t3 = tb;
                    DrawTextW(dc, L"pausa", -1, &t3, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                    g.ReleaseHDC(dc);
                } else {
                    Gdiplus::Bitmap* bmp = ThumbOf(SlideJsonForPreview(sl), tw, thh, "t:" + sl.id);
                    if (bmp) g.DrawImage(bmp, tb.left, tb.top, tw, thh);
                    // marco activo
                    Gdiplus::Pen pn(ui::ToColor(sel && draft_.selSlide == j ? ui::Accent
                                                                        : ui::Border2),
                                    sel && draft_.selSlide == j ? 2.0f : 1.0f);
                    g.DrawRectangle(&pn,  tb.left - 0.5f,  tb.top - 0.5f, 
                                    (Gdiplus::REAL)(tb.right - tb.left), 
                                    (Gdiplus::REAL)(tb.bottom - tb.top));
                }
                HitAdd(CMD_T_SLIDE, i, j, RECT{tr2.left, tr2.top, tr2.right,
                                               tr2.top + std::max(thh, 24)});
            }
            y += 96;
        }
        y += 14;
    }
    if (draft_.items.empty()) {
        dc = g.GetHDC();
        SelectObject(dc, ui::Font(12));
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, ui::Cref(ui::Gray));
        RECT er{r.left + 10, r.top + 60, r.right - 10, r.top + 140};
        DrawTextW(dc, L"Sin diapositivas.\nUsa Inicio → Nueva diapositiva o Ctrl+M.", -1, &er,
                  DT_CENTER | DT_WORDBREAK);
        g.ReleaseHDC(dc);
    }
}

// ================================================================== lienzo
void NativeStudio::PaintCanvas(Gdiplus::Graphics& g, const RECT& r) {
    DraftItem* it = SelItem();
    DraftSlide* sl = SelSlide();

    // cabecera: tipo · etiqueta · N de M · transición
    HDC dc = g.GetHDC();
    SelectObject(dc, ui::Font(11));
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, ui::Cref(ui::Gray));
    RECT ht{r.left + 16, r.top + 6, r.left + 500, r.top + 24};
    std::wstring head = it ? it->label : L"Sin elementos";
    if (SlideTotalCount() > 0)
        head += L"   ·   " + std::to_wstring(SlideGlobalIndex()) + L" de " +
                std::to_wstring(SlideTotalCount());
    DrawTextW(dc, head.c_str(), -1, &ht,
              DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
    g.ReleaseHDC(dc);
    if (sl) {
        RECT tb{r.right - 110, r.top + 4, r.right - 16, r.top + 22};
        ui::Badge(g, tb, sl->transition.empty() ? L"FADE" : ToWide(sl->transition).c_str(),
                  ui::Border2, ui::Gray);
    }

    // marco de diapositiva centrado (web .ppt-slideframe)
    int maxW = std::min(880, (int)(r.right - r.left - 48));
    int sw = maxW, sh = sw * 9 / 16;
    int maxH = (r.bottom - r.top) - (notesOpen_ && sl ? 210 : 130) - 40;
    if (sh > maxH) { sh = maxH; sw = sh * 16 / 9; }
    int sx = r.left + (r.right - r.left - sw) / 2, sy = r.top + 32;
    RECT frame{sx, sy, sx + sw, sy + sh};
    Gdiplus::Pen fp(ui::ToColor(0xFFC8C6C4), 1.0f);
    Gdiplus::SolidBrush sb(ui::ToColor(0xFF000000));
    g.FillRectangle(&sb, (Gdiplus::REAL)(frame.left), (Gdiplus::REAL)(frame.top),  (Gdiplus::REAL)(frame.right - frame.left), 
                    (Gdiplus::REAL)(frame.bottom - frame.top));
    g.DrawRectangle(&fp, (Gdiplus::REAL)(frame.left), (Gdiplus::REAL)(frame.top),  (Gdiplus::REAL)(frame.right - frame.left - 1), 
                    (Gdiplus::REAL)(frame.bottom - frame.top - 1));
    if (sl) {
        Gdiplus::Bitmap* bmp = ThumbOf(SlideJsonForPreview(*sl), sw, sh, "c:" + sl->id);
        if (bmp) g.DrawImage(bmp, frame.left, frame.top, sw, sh);
    } else {
        dc = g.GetHDC();
        SelectObject(dc, ui::Font(14, FW_SEMIBOLD));
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, ui::Cref(ui::Ink));
        RECT t1{frame.left, frame.top + sh / 2 - 40, frame.right, frame.top + sh / 2 - 16};
        DrawTextW(dc, L"Sin diapositivas todavía", -1, &t1, DT_CENTER | DT_SINGLELINE);
        SelectObject(dc, ui::Font(12));
        SetTextColor(dc, ui::Cref(ui::Gray));
        RECT t2{frame.left + 80, frame.top + sh / 2 - 12, frame.right - 80, frame.top + sh / 2 + 10};
        DrawTextW(dc, L"Usa Inicio → Nueva diapositiva o pulsa Ctrl+M.", -1, &t2, DT_CENTER);
        g.ReleaseHDC(dc);
        RECT nb{frame.left + sw / 2 - 90, frame.top + sh / 2 + 20, frame.left + sw / 2 + 90,
                frame.top + sh / 2 + 52};
        ui::Chip(g, nb, L"Nueva diapositiva", L"", "plus", ui::kAcc | Hot(CMD_ADD_TITLE, hoverId_));
        HitAdd(CMD_ADD_TITLE, 0, 0, nb);
        PlaceEdit(edtTitle_, RECT{}, false);
        PlaceEdit(edtSub_, RECT{}, false);
        PlaceEdit(edtLines_, RECT{}, false);
        PlaceEdit(edtNotes_, RECT{}, false);
        return;
    }

    // tarjeta de edición (web: editar texto de la diapositiva)
    int cy = frame.bottom + 10;
    RECT card{sx, cy, sx + sw, cy + 128};
    Gdiplus::SolidBrush wb(ui::ToColor(ui::Paper));
    Gdiplus::Pen cp(ui::ToColor(ui::Border2), 1.0f);
    g.FillRectangle(&wb, (Gdiplus::REAL)(card.left), (Gdiplus::REAL)(card.top),  (Gdiplus::REAL)(card.right - card.left - 1), 
                    (Gdiplus::REAL)(card.bottom - card.top - 1));
    g.DrawRectangle(&cp, (Gdiplus::REAL)(card.left), (Gdiplus::REAL)(card.top),  (Gdiplus::REAL)(card.right - card.left - 1), 
                    (Gdiplus::REAL)(card.bottom - card.top - 1));
    dc = g.GetHDC();
    SelectObject(dc, ui::Font(11, FW_SEMIBOLD));
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, ui::Cref(ui::GrayLt));
    RECT ch{card.left + 12, card.top + 6, card.right, card.top + 22};
    DrawTextW(dc, L"EDITAR TEXTO DE LA DIAPOSITIVA", -1, &ch, DT_LEFT | DT_SINGLELINE);
    g.ReleaseHDC(dc);
    int half = (card.right - card.left - 36) / 2;
    RECT t1{card.left + 12, card.top + 26, card.left + 12 + half, card.top + 58};
    RECT t2{card.left + 24 + half, card.top + 26, card.left + 24 + 2 * half, card.top + 58};
    RECT t3{card.left + 12, card.top + 64, card.right - 12, card.top + 98};
    ui::Field(g, t1, GetFocus() == edtTitle_, false);
    PlaceEdit(edtTitle_, RECT{t1.left + 1, t1.top + 1, t1.right - 1, t1.bottom - 1}, true);
    ui::Field(g, t2, GetFocus() == edtSub_, false);
    PlaceEdit(edtSub_, RECT{t2.left + 1, t2.top + 1, t2.right - 1, t2.bottom - 1}, true);
    ui::Field(g, t3, GetFocus() == edtLines_, false);
    PlaceEdit(edtLines_, RECT{t3.left + 2, t3.top + 1, t3.right - 2, t3.bottom - 1}, true);
    // botones anterior/siguiente
    RECT pv{card.left + 12, card.top + 102, card.left + 120, card.top + 124};
    RECT nx{card.left + 128, card.top + 102, card.left + 240, card.top + 124};
    ui::Chip(g, pv, L"← Anterior", L"", "", Hot(CMD_SLIDE_PREV, hoverId_));
    HitAdd(CMD_SLIDE_PREV, 0, 0, pv);
    ui::Chip(g, nx, L"Siguiente →", L"", "", Hot(CMD_SLIDE_NEXT, hoverId_));
    HitAdd(CMD_SLIDE_NEXT, 0, 0, nx);

    // notas del orador
    if (notesOpen_) {
        int ny = card.bottom + 6;
        RECT nr{sx, ny, sx + sw, ny + 66};
        dc = g.GetHDC();
        SelectObject(dc, ui::Font(11, FW_SEMIBOLD));
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, ui::Cref(ui::GrayLt));
        RECT nl{nr.left + 2, ny, nr.right, ny + 16};
        DrawTextW(dc, L"NOTAS DEL ORADOR", -1, &nl, DT_LEFT | DT_SINGLELINE);
        g.ReleaseHDC(dc);
        ui::Field(g, RECT{nr.left, ny + 18, nr.right, ny + 64}, GetFocus() == edtNotes_, false);
        PlaceEdit(edtNotes_, RECT{nr.left + 2, ny + 19, nr.right - 2, ny + 63}, true);
    } else {
        PlaceEdit(edtNotes_, RECT{}, false);
    }
}

// ================================================================== clasificador
void NativeStudio::PaintSorter(Gdiplus::Graphics& g, const RECT& r) {
    HDC dc = g.GetHDC();
    SelectObject(dc, ui::Font(13, FW_SEMIBOLD));
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, ui::Cref(ui::Ink));
    RECT ht{r.left + 16, r.top + 10, r.left + 400, r.top + 30};
    DrawTextW(dc, (L"Clasificador · " + std::to_wstring(SlideTotalCount()) +
                   L" diapositivas").c_str(), -1, &ht, DT_LEFT | DT_VCENTER);
    g.ReleaseHDC(dc);
    RECT back{r.right - 150, r.top + 8, r.right - 16, r.top + 32};
    ui::Chip(g, back, L"Volver a Normal", L"", "heading", Hot(CMD_VIEW_NORMAL, hoverId_));
    HitAdd(CMD_VIEW_NORMAL, 0, 0, back);

    RECT grid{r.left + 12, r.top + 40, r.right - 12, r.bottom - 8};
    int cw = 168, chh = 94 + 26, gap = 10;
    int perRow = std::max(1, (int)((grid.right - grid.left) / (cw + gap)));
    int rows = (SlideTotalCount() + perRow - 1) / perRow;
    ScrollAdd(grid, scSorter_, std::max(0, (int)(rows * (chh + gap) - (grid.bottom - grid.top))));
    int n = 0;
    int y = grid.top - scSorter_;
    for (int i = 0; i < (int)draft_.items.size(); i++) {
        auto& it = draft_.items[(size_t)i];
        for (int j = 0; j < (int)it.slides.size(); j++) {
            auto& sl = it.slides[(size_t)j];
            if (sl.type == "blank") continue;
            n++;
            int row = (n - 1) / perRow, col = (n - 1) % perRow;
            RECT c{grid.left + col * (cw + gap), y + row * (chh + gap),
                   grid.left + col * (cw + gap) + cw, y + row * (chh + gap) + chh};
            if (c.bottom > grid.top && c.top < grid.bottom) {
                bool sel = (draft_.selItem == i && draft_.selSlide == j);
                Gdiplus::Pen pn(ui::ToColor(sel ? ui::Accent : ui::Border2), sel ? 2.0f : 1.0f);
                Gdiplus::SolidBrush wb(ui::ToColor(ui::Paper));
                g.FillRectangle(&wb, (Gdiplus::REAL)(c.left), (Gdiplus::REAL)(c.top),  (Gdiplus::REAL)(c.right - c.left - 1), 
                                (Gdiplus::REAL)(chh - 1));
                g.DrawRectangle(&pn, (Gdiplus::REAL)(c.left), (Gdiplus::REAL)(c.top),  (Gdiplus::REAL)(c.right - c.left - 1), 
                                (Gdiplus::REAL)(chh - 1));
                Gdiplus::Bitmap* bmp = ThumbOf(SlideJsonForPreview(sl), cw - 2, 94, "s:" + sl.id);
                if (bmp) g.DrawImage(bmp, c.left + 1, c.top + 1, cw - 2, 94);
                HDC dcx = g.GetHDC();
                SelectObject(dcx, ui::Font(11));
                SetBkMode(dcx, TRANSPARENT);
                SetTextColor(dcx, ui::Cref(ui::Gray));
                RECT nr2{c.left + 6, c.top + 96, c.left + 30, c.bottom - 2};
                DrawTextW(dcx, std::to_wstring(n).c_str(), -1, &nr2, DT_LEFT | DT_VCENTER);
                SelectObject(dcx, ui::Font(12));
                SetTextColor(dcx, ui::Cref(ui::Ink));
                RECT tr{c.left + 30, c.top + 96, c.right - 4, c.bottom - 2};
                DrawTextW(dcx, (sl.title.empty() ? it.label : sl.title).c_str(), -1, &tr,
                          DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
                g.ReleaseHDC(dcx);
                HitAdd(CMD_S_SLIDE, i, j, c);
            }
        }
    }
    if (n == 0) {
        dc = g.GetHDC();
        SelectObject(dc, ui::Font(13));
        SetTextColor(dc, ui::Cref(ui::Gray));
        RECT er = grid;
        DrawTextW(dc, L"Sin diapositivas. Crea una con Ctrl+M.", -1, &er,
                  DT_CENTER | DT_VCENTER);
        g.ReleaseHDC(dc);
    }
}

// ================================================================== panel tareas
void NativeStudio::PaintTaskPane(Gdiplus::Graphics& g, const RECT& r) {
    Gdiplus::SolidBrush wp(ui::ToColor(ui::Paper));
    g.FillRectangle(&wp, (Gdiplus::REAL)(r.left), (Gdiplus::REAL)(r.top),  (Gdiplus::REAL)(r.right - r.left), 
                    (Gdiplus::REAL)(r.bottom - r.top));
    Gdiplus::Pen lp(ui::ToColor(ui::Border), 1.0f);
    g.DrawLine(&lp,  (Gdiplus::REAL)r.left + 0.5f, (Gdiplus::REAL)(r.top),  (Gdiplus::REAL)r.left + 0.5f, 
               (Gdiplus::REAL)r.bottom);
    // pestañas Formato | Biblioteca + cerrar
    RECT t1{r.left + 6, r.top + 6, r.left + 106, r.top + 32};
    RECT t2{r.left + 112, r.top + 6, r.left + 222, r.top + 32};
    ui::Chip(g, t1, L"Formato", L"", "wand",
             (taskTab_ == 0 ? ui::kOn : 0) | Hot(9000, hoverId_));
    ui::Chip(g, t2, L"Biblioteca", L"", "book",
             (taskTab_ == 1 ? ui::kOn : 0) | Hot(9001, hoverId_));
    HitAdd(9000, 0, 0, t1);
    HitAdd(9001, 0, 0, t2);
    RECT xb{r.right - 32, r.top + 5, r.right - 6, r.top + 31};
    ui::IconButton(g, xb, "x", Hot(CMD_TOGGLE_TASK, hoverId_));
    HitAdd(CMD_TOGGLE_TASK, 0, 0, xb);

    RECT body{r.left, r.top + 38, r.right, r.bottom};
    if (taskTab_ == 0) {
        // ---------------- Formato ----------------
        DraftSlide* sl = SelSlide();
        if (!sl) {
            HDC dc = g.GetHDC();
            SelectObject(dc, ui::Font(12));
            SetBkMode(dc, TRANSPARENT);
            SetTextColor(dc, ui::Cref(ui::Gray));
            RECT tr{body.left + 14, body.top + 16, body.right - 14, body.top + 60};
            DrawTextW(dc, L"Selecciona una diapositiva para ver su formato.", -1, &tr, DT_WORDBREAK);
            g.ReleaseHDC(dc);
            return;
        }
        int y = body.top + 10;
        // tamaño
        HDC dc = g.GetHDC();
        SelectObject(dc, ui::Font(12, FW_SEMIBOLD));
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, ui::Cref(ui::Ink));
        RECT fh{body.left + 14, y, body.right - 14, y + 20};
        DrawTextW(dc, (L"Tamaño del texto  ·  " +
                       std::to_wstring(sl->fontSize > 0 ? sl->fontSize : 44) + L"px").c_str(),
                  -1, &fh, DT_LEFT | DT_VCENTER);
        g.ReleaseHDC(dc);
        y += 24;
        RECT fa{body.left + 14, y, body.left + 60, y + 28};
        RECT fx{body.left + 66, y, body.left + 112, y + 28};
        ui::Chip(g, fa, L"A−", L"", "", Hot(CMD_FONT_DOWN, hoverId_));
        HitAdd(CMD_FONT_DOWN, 0, 0, fa);
        ui::Chip(g, fx, L"A+", L"", "", Hot(CMD_FONT_UP, hoverId_));
        HitAdd(CMD_FONT_UP, 0, 0, fx);
        y += 36;
        // alineación
        struct ABtn { int id; const char* icon; };
        ABtn ab[] = {{CMD_ALIGN_L, "align-left"}, {CMD_ALIGN_C, "align-center"},
                     {CMD_ALIGN_R, "align-right"}};
        for (int i = 0; i < 3; i++) {
            int w3 = (body.right - body.left - 28) / 3;
            RECT b{body.left + 14 + i * (w3 + 2), y, body.left + 14 + (i + 1) * (w3 + 2) - 2, y + 34};
            ui::IconButton(g, b, ab[i].icon,
                           (sl->align == i ? ui::kOn : 0) | Hot(ab[i].id, hoverId_));
            HitAdd(ab[i].id, 0, 0, b);
        }
        y += 42;
        // tercio inferior
        RECT lt{body.left + 14, y, body.right - 14, y + 30};
        ui::Chip(g, lt, L"Tercio inferior", sl->lowerThird ? L"sí" : L"no", "app-window-bottom",
                 (sl->lowerThird ? ui::kOn : 0) | Hot(CMD_TOGGLE_LT, hoverId_));
        HitAdd(CMD_TOGGLE_LT, 0, 0, lt);
        y += 38;
        // transición
        static const struct { const char* v; const wchar_t* label; } trs[] = {
            {"cut", L"Ninguna (corte)"}, {"fade", L"Fundido"}, {"slide", L"Desplazar"}};
        for (auto& t : trs) {
            RECT b{body.left + 14, y, body.right - 14, y + 26};
            std::string cur = sl->transition.empty() ? "fade" : sl->transition;
            ui::Chip(g, b, t.label, L"", "", (cur == t.v ? ui::kOn : 0) |
                   Hot(CMD_TRANS_BASE + (int)(&t - trs), hoverId_));
            HitAdd(CMD_TRANS_BASE + (int)(&t - trs), 0, 0, b);
            y += 28;
        }
        y += 8;
        // fondos (aplicar a la diapositiva)
        dc = g.GetHDC();
        SelectObject(dc, ui::Font(11));
        SetTextColor(dc, ui::Cref(ui::Gray));
        RECT bh{body.left + 14, y, body.right, y + 16};
        DrawTextW(dc, L"Fondo", -1, &bh, DT_LEFT);
        g.ReleaseHDC(dc);
        y += 20;
        int gw = (body.right - body.left - 28 - 12) / 4;
        for (size_t i = 0; i < Backgrounds().size(); i++) {
            int row = (int)i / 4, col = (int)i % 4;
            RECT c{body.left + 14 + col * (gw + 4), y + row * (gw * 9 / 16 + 6),
                   body.left + 14 + (col + 1) * (gw + 4) - 4, y + row * (gw * 9 / 16 + 6) + gw * 9 / 16};
            Gdiplus::Pen pn(ui::ToColor(sl->bgId == Backgrounds()[i].id ? ui::Accent : ui::Border2),
                            sl->bgId == Backgrounds()[i].id ? 2.0f : 1.0f);
            Gdiplus::SolidBrush bb(ui::ToColor(ParseColor(Backgrounds()[i].color, 0xFF000000)));
            g.FillRectangle(&bb, (Gdiplus::REAL)(c.left), (Gdiplus::REAL)(c.top),  (Gdiplus::REAL)(c.right - c.left - 1), 
                            (Gdiplus::REAL)(c.bottom - c.top - 1));
            g.DrawRectangle(&pn, (Gdiplus::REAL)(c.left), (Gdiplus::REAL)(c.top),  (Gdiplus::REAL)(c.right - c.left - 1), 
                            (Gdiplus::REAL)(c.bottom - c.top - 1));
            HitAdd(CMD_BG_BASE + (int)i, 0, 0, c);
        }
        y += ((int)Backgrounds().size() + 3) / 4 * (gw * 9 / 16 + 6) + 8;
        // aplicar a sección / todo
        RECT a1{body.left + 14, y, body.right - 14, y + 28};
        ui::Chip(g, a1, L"Aplicar fondo a la sección", L"", "", Hot(CMD_APPLY_BG_SECTION, hoverId_));
        HitAdd(CMD_APPLY_BG_SECTION, 0, 0, a1);
        y += 32;
        RECT a2{body.left + 14, y, body.right - 14, y + 28};
        ui::Chip(g, a2, L"Aplicar fondo a todo", L"", "", Hot(CMD_APPLY_BG_ALL, hoverId_));
        HitAdd(CMD_APPLY_BG_ALL, 0, 0, a2);
        y += 32;
        RECT a3{body.left + 14, y, body.right - 14, y + 28};
        ui::Chip(g, a3, L"Aplicar tema a todo", L"", "", Hot(CMD_APPLY_THEME_ALL, hoverId_));
        HitAdd(CMD_APPLY_THEME_ALL, 0, 0, a3);
    } else {
        PaintLibrary(g, body);
    }
}

// ================================================================== biblioteca
void NativeStudio::PaintLibrary(Gdiplus::Graphics& g, const RECT& r) {
    int y = r.top + 6;
    // pestañas (web LIB_TABS)
    static const struct { int id; const wchar_t* label; } tabs[] = {
        {LIB_CANTOS, L"Cantos"}, {LIB_BIBLIA, L"Biblia"}, {LIB_MEDIOS, L"Medios"},
        {LIB_TEMAS, L"Temas"}, {LIB_LISTA, L"Lista"}};
    int x = r.left + 10;
    for (auto& t : tabs) {
        RECT b{x, y, x + 52, y + 24};
        bool on = libTab_ == t.id;
        if (on) {
            Gdiplus::SolidBrush ib(ui::ToColor(ui::Ink));
            g.FillRectangle(&ib, (Gdiplus::REAL)(b.left), (Gdiplus::REAL)(b.top),  (Gdiplus::REAL)(b.right - b.left), 
                            (Gdiplus::REAL)(b.bottom - b.top));
        } else if (hoverId_ == CMD_LIB_TAB_BASE + t.id) {
            Gdiplus::SolidBrush hb(ui::ToColor(ui::HoverBg));
            g.FillRectangle(&hb, (Gdiplus::REAL)(b.left), (Gdiplus::REAL)(b.top),  (Gdiplus::REAL)(b.right - b.left), 
                            (Gdiplus::REAL)(b.bottom - b.top));
        }
        HDC dc = g.GetHDC();
        SelectObject(dc, ui::Font(11, on ? FW_SEMIBOLD : FW_NORMAL));
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, on ? 0xFFFFFF : ui::Cref(ui::Gray));
        RECT tr = b;
        DrawTextW(dc, t.label, -1, &tr, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        g.ReleaseHDC(dc);
        HitAdd(CMD_LIB_TAB_BASE + t.id, 0, 0, b);
        x += 54;
    }
    y += 30;
    // buscador
    RECT sq{r.left + 10, y, r.right - 10, y + 28};
    ui::Field(g, sq, GetFocus() == edtLib_, false);
    PlaceEdit(edtLib_, RECT{sq.left + 24, sq.top + 1, sq.right - 2, sq.bottom - 1}, true);
    if (ui::Icon("search", ui::TintInk))
        g.DrawImage(ui::Icon("search", ui::TintInk), sq.left + 6, sq.top + 7, 14, 14);
    y += 34;

    if (libTab_ == LIB_CANTOS) {
        // resultados (lista directa completa, scroll)
        // [v3.0.0 — bug «bibliotecas»] Lista directa COMPLETA, sin tope de 200:
        // la consulta vacía devuelve todos los cantos y la lista hace scroll.
        songHits_ = songs_.Search(libQuery_, songs_.Count());
        RECT listR{r.left, y, r.right, r.bottom};
        int rowH = 44, total = (int)songHits_.size() * rowH;
        ScrollAdd(listR, scLib_, std::max(0, (int)(total - (listR.bottom - listR.top))));
        int yy = y - scLib_;
        for (size_t k = 0; k < songHits_.size(); k++) {
            const LibSong* s = songs_.At((size_t)songHits_[k]);
            if (!s) continue;
            RECT row{listR.left + 8, yy, listR.right - 8, yy + rowH - 4};
            if (row.bottom > listR.top && row.top < listR.bottom) {
                ui::Row(g, row, 0, s->title,
                        s->artist + L" · " + std::to_wstring(s->sections.size()) + L" partes",
                        "music", (hoverId_ == CMD_SONG_BASE && hoverA_ == (int)k ? ui::kHot : 0));
                HitAdd(CMD_SONG_BASE, (int)k, 0, row);
            }
            yy += rowH;
        }
        if (songHits_.empty()) {
            HDC dc = g.GetHDC();
            SelectObject(dc, ui::Font(12));
            SetBkMode(dc, TRANSPARENT);
            SetTextColor(dc, ui::Cref(ui::Gray));
            RECT tr{r.left + 12, y + 12, r.right - 12, y + 60};
            DrawTextW(dc, songs_.Loaded()
                      ? L"Sin cantos para ese filtro.\nImporta cantos desde FusionStudio (C#)."
                      : L"Sin cancionero (datos/cancionero.fdb).\nImporta cantos desde FusionStudio (C#).",
                      -1, &tr, DT_WORDBREAK);
            g.ReleaseHDC(dc);
        }
    } else if (libTab_ == LIB_BIBLIA) {
        if (bibleBook_ < 0) {
            for (size_t i = 0; i < bibles_.BookCount(); i++)
                if (bibles_.BookName(i) == L"Salmos") { bibleBook_ = (int)i; break; }
            if (bibleBook_ < 0 && bibles_.BookCount() > 0) bibleBook_ = 0;
        }
        // selector de biblia + tercio
        RECT bb{r.left + 10, y, r.left + 150, y + 26};
        ui::Chip(g, bb, bibles_.Count() ? (L"Biblia " + std::to_wstring(bibles_.Selected() + 1))
                                        : L"Sin biblias", L"", "book", Hot(9010, hoverId_));
        HitAdd(9010, 0, 0, bb);
        RECT lt{r.left + 156, y, r.right - 10, y + 26};
        ui::Chip(g, lt, L"Tercio", bibleAsLt_ ? L"sí" : L"no", "app-window-bottom",
                 (bibleAsLt_ ? ui::kOn : 0) | Hot(CMD_LT_TOGGLE, hoverId_));
        HitAdd(CMD_LT_TOGGLE, 0, 0, lt);
        y += 30;
        // navegación libro/capítulo
        std::wstring bn = bibles_.BookName((size_t)std::max(0, bibleBook_));
        int chMax = bibles_.ChapterCount(bn);
        HDC dc = g.GetHDC();
        SelectObject(dc, ui::Font(12, FW_SEMIBOLD));
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, ui::Cref(ui::Ink));
        RECT bh{r.left + 10, y, r.right - 10, y + 22};
        DrawTextW(dc, (bn + L" " + std::to_wstring(bibleChapter_)).c_str(), -1, &bh,
                  DT_LEFT | DT_VCENTER);
        g.ReleaseHDC(dc);
        RECT bp{r.right - 122, y - 2, r.right - 86, y + 22};
        RECT bnx{r.right - 82, y - 2, r.right - 46, y + 22};
        RECT cp{r.right - 42, y - 2, r.right - 24, y + 22};
        ui::IconButton(g, bp, "chevron-left", Hot(CMD_CH_PREV, hoverId_));
        HitAdd(CMD_CH_PREV, 0, 0, bp);
        ui::IconButton(g, bnx, "chevron-right", Hot(CMD_CH_NEXT, hoverId_));
        HitAdd(CMD_CH_NEXT, 0, 0, bnx);
        y += 26;
        // insertar referencia buscada
        RECT ir{r.left + 10, y, r.right - 10, y + 28};
        ui::Chip(g, ir, L"Insertar referencia buscada", L"", "plus",
                 ui::kAcc | Hot(CMD_INSERT_REF, hoverId_));
        HitAdd(CMD_INSERT_REF, 0, 0, ir);
        y += 34;
        // versículos
        verseNums_.clear();
        RECT listR{r.left, y, r.right, r.bottom};
        int vv = bibles_.VerseCount(bn, bibleChapter_);
        int rowH = 58;
        ScrollAdd(listR, scVerses_, std::max(0, (int)(vv * rowH - (listR.bottom - listR.top))));
        int yy = y - scVerses_;
        for (int i = 1; i <= vv && (int)verseNums_.size() < 100; i++) {
            std::wstring tx = bibles_.Verse(bn, bibleChapter_, i);
            if (tx.empty()) continue;
            RECT row{listR.left + 8, yy, listR.right - 8, yy + rowH - 4};
            if (row.bottom > listR.top && row.top < listR.bottom) {
                HDC dcx = g.GetHDC();
                SetBkMode(dcx, TRANSPARENT);
                SelectObject(dcx, ui::Font(11, FW_SEMIBOLD));
                SetTextColor(dcx, ui::Cref(ui::Accent));
                RECT rh{row.left + 4, row.top, row.right, row.top + 16};
                DrawTextW(dcx, (bn + L" " + std::to_wstring(bibleChapter_) + L":" +
                                std::to_wstring(i)).c_str(), -1, &rh, DT_LEFT | DT_SINGLELINE);
                SelectObject(dcx, ui::Font(11));
                SetTextColor(dcx, ui::Cref(0xFF424242));
                RECT th{row.left + 4, row.top + 16, row.right, row.bottom};
                DrawTextW(dcx, tx.c_str(), -1, &th, DT_LEFT | DT_WORDBREAK | DT_END_ELLIPSIS);
                g.ReleaseHDC(dcx);
                verseNums_.push_back(i);
                HitAdd(CMD_P_VERSE_BASE + (int)verseNums_.size() - 1, i, 0, row);
            }
            yy += rowH;
        }
    } else if (libTab_ == LIB_MEDIOS) {
        // fondos → insertar como imagen
        int gw = (r.right - r.left - 24) / 3;
        for (size_t i = 0; i < Backgrounds().size(); i++) {
            int row = (int)i / 3, col = (int)i % 3;
            RECT c{r.left + 10 + col * (gw + 4), y + row * (gw * 9 / 16 + 26),
                   r.left + 10 + (col + 1) * (gw + 4) - 4, y + row * (gw * 9 / 16 + 26) + gw * 9 / 16};
            Gdiplus::SolidBrush bb(ui::ToColor(ParseColor(Backgrounds()[i].color, 0xFF000000)));
            Gdiplus::Pen pn(ui::ToColor(ui::Border2));
            g.FillRectangle(&bb, (Gdiplus::REAL)(c.left), (Gdiplus::REAL)(c.top),  (Gdiplus::REAL)(c.right - c.left - 1), 
                            (Gdiplus::REAL)(c.bottom - c.top - 1));
            g.DrawRectangle(&pn, (Gdiplus::REAL)(c.left), (Gdiplus::REAL)(c.top),  (Gdiplus::REAL)(c.right - c.left - 1), 
                            (Gdiplus::REAL)(c.bottom - c.top - 1));
            HDC dcx = g.GetHDC();
            SelectObject(dcx, ui::Font(10));
            SetBkMode(dcx, TRANSPARENT);
            SetTextColor(dcx, ui::Cref(ui::Ink));
            RECT lh{c.left, c.bottom + 2, c.right, c.bottom + 18};
            DrawTextW(dcx, ToWide(Backgrounds()[i].name).c_str(), -1, &lh,
                      DT_LEFT | DT_SINGLELINE | DT_END_ELLIPSIS);
            g.ReleaseHDC(dcx);
            HitAdd(CMD_MEDIA_BASE + (int)i, 0, 0, RECT{c.left, c.top, c.right, c.bottom + 18});
        }
    } else if (libTab_ == LIB_TEMAS) {
        for (size_t i = 0; i < Themes().size(); i++) {
            RECT row{r.left + 10, y + (int)i * 40, r.right - 10, y + (int)i * 40 + 34};
            DraftSlide* sl = SelSlide();
            bool on = sl && sl->themeId == Themes()[i].id;
            ui::Chip(g, row, ToWide(Themes()[i].name), L"", "palette",
                     (on ? ui::kOn : 0) | (hoverId_ == CMD_THEME_BASE && hoverA_ == (int)i
                        ? ui::kHot : 0));
            HitAdd(CMD_THEME_BASE, (int)i, 0, row);
        }
    } else if (libTab_ == LIB_LISTA) {
        for (int i = 0; i < (int)draft_.items.size(); i++) {
            RECT row{r.left + 10, y + i * 34, r.right - 10, y + i * 34 + 30};
            ui::Row(g, row, i + 1, draft_.items[(size_t)i].label,
                    std::to_wstring(draft_.items[(size_t)i].slides.size()) + L" diapositivas",
                    "list", (draft_.selItem == i ? ui::kOn : 0) |
                    (hoverId_ == CMD_T_ITEM_BASE && hoverA_ == i ? ui::kHot : 0));
            HitAdd(CMD_T_ITEM_BASE + i, i, 0, row);
        }
        if (draft_.items.empty()) {
            HDC dc = g.GetHDC();
            SelectObject(dc, ui::Font(12));
            SetBkMode(dc, TRANSPARENT);
            SetTextColor(dc, ui::Cref(ui::Gray));
            RECT tr{r.left + 12, y + 12, r.right - 12, y + 60};
            DrawTextW(dc, L"Sin elementos en la presentación.", -1, &tr, DT_WORDBREAK);
            g.ReleaseHDC(dc);
        }
    }
}

// ================================================================== estado
void NativeStudio::PaintStatus(Gdiplus::Graphics& g, const RECT& cli) {
    int W = cli.right, H = cli.bottom;
    Gdiplus::SolidBrush wp(ui::ToColor(ui::Paper));
    g.FillRectangle(&wp, (Gdiplus::REAL)(0),  (Gdiplus::REAL)(H - 26),  (Gdiplus::REAL)W, (Gdiplus::REAL)(26));
    Gdiplus::Pen lp(ui::ToColor(ui::Border), 1.0f);
    g.DrawLine(&lp, (Gdiplus::REAL)(0),  (Gdiplus::REAL)(H - 26) + 0.5f,  (Gdiplus::REAL)W,  (Gdiplus::REAL)(H - 26) + 0.5f);
    HDC dc = g.GetHDC();
    SelectObject(dc, ui::Font(11));
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, ui::Cref(ui::Gray));
    RECT s1{12, H - 26, 260, H};
    int tot = SlideTotalCount();
    DrawTextW(dc, (L"Diapositiva " + std::to_wstring(tot ? SlideGlobalIndex() : 0) + L" de " +
                   std::to_wstring(tot)).c_str(), -1, &s1, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    RECT s2{270, H - 26, 420, H};
    DrawTextW(dc, L"Español", -1, &s2, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);
    // vistas
    RECT vn{W - 96, H - 24, W - 68, H - 2};
    RECT vs{W - 64, H - 24, W - 36, H - 2};
    ui::IconButton(g, vn, "heading", (!viewSorter_ ? ui::kOn : 0) | Hot(CMD_VIEW_NORMAL, hoverId_));
    HitAdd(CMD_VIEW_NORMAL, 0, 0, vn);
    ui::IconButton(g, vs, "layout-grid", (viewSorter_ ? ui::kOn : 0) | Hot(CMD_VIEW_SORTER, hoverId_));
    HitAdd(CMD_VIEW_SORTER, 0, 0, vs);
}

// ================================================================== sync edits
void NativeStudio::SyncEditsFromDraft() {
    auto Set = [](HWND ed, const std::wstring& t) {
        if (!ed) return;
        wchar_t buf[512];
        GetWindowTextW(ed, buf, 512);
        if (buf != t) SetWindowTextW(ed, t.c_str());
    };
    Set(edtName_, draft_.name);
    DraftSlide* sl = SelSlide();
    Set(edtTitle_, sl ? sl->title : L"");
    Set(edtSub_, sl ? sl->subtitle : L"");
    std::wstring lines;
    if (sl) for (size_t i = 0; i < sl->lines.size(); i++) {
        if (i) lines += L"\r\n";
        lines += sl->lines[i];
    }
    Set(edtLines_, lines);
    Set(edtNotes_, sl ? sl->notes : L"");
}

void NativeStudio::OnEditChanged(int editId) {
    wchar_t buf[2048];
    switch (editId) {
        case 1:
            GetWindowTextW(edtName_, buf, 512);
            draft_.name = buf;
            break;
        case 2:
            GetWindowTextW(edtSearch_, buf, 512);
            startQuery_ = buf;
            Repaint();
            break;
        case 3:
            GetWindowTextW(edtLib_, buf, 512);
            libQuery_ = buf;
            scLib_ = 0;
            scVerses_ = 0;
            Repaint();
            break;
        case 4: {
            DraftSlide* sl = SelSlide();
            if (!sl) break;
            GetWindowTextW(edtTitle_, buf, 512);
            sl->title = buf;
            ClearThumbs();
            break;
        }
        case 5: {
            DraftSlide* sl = SelSlide();
            if (!sl) break;
            GetWindowTextW(edtSub_, buf, 512);
            sl->subtitle = buf;
            break;
        }
        case 6: {
            DraftSlide* sl = SelSlide();
            if (!sl) break;
            GetWindowTextW(edtLines_, buf, 2048);
            std::wstring t = buf;
            sl->lines.clear();
            std::wstring cur;
            for (wchar_t c : t) {
                if (c == L'\r') continue;
                if (c == L'\n') { sl->lines.push_back(cur); cur.clear(); }
                else cur += c;
            }
            if (!cur.empty()) sl->lines.push_back(cur);
            ClearThumbs();
            break;
        }
        case 7: {
            DraftSlide* sl = SelSlide();
            if (!sl) break;
            GetWindowTextW(edtNotes_, buf, 2048);
            sl->notes = buf;
            break;
        }
        case 8:
            GetWindowTextW(edtBible_, buf, 512);
            bibleQuery_ = buf;
            Repaint();
            break;
        default: break;
    }
}

// ---- copiar / pegar diapositivas (web Ctrl+C/V/D) ----
void NativeStudio::CopySelSlides() {
    DraftItem* it = SelItem();
    if (!it || draft_.selSlide < 0 || draft_.selSlide >= (int)it->slides.size()) return;
    draft_.clipSlides_.clear();
    draft_.clipSlides_.push_back(it->slides[(size_t)draft_.selSlide]);
    draft_.clipType_ = it->type;
}
void NativeStudio::PasteSlides() {
    if (draft_.clipSlides_.empty()) return;
    PushUndo();
    DraftItem* it = SelItem();
    if (!it) {                    // sin selección: nueva sección
        DraftItem ni;
        ni.id = NewId();
        ni.type = draft_.clipType_.empty() ? "song" : draft_.clipType_;
        ni.label = L"Pegado";
        for (auto& s : draft_.clipSlides_) ni.slides.push_back(s);
        draft_.items.push_back(std::move(ni));
        draft_.selItem = (int)draft_.items.size() - 1;
        draft_.selSlide = 0;
    } else {
        for (auto& s : draft_.clipSlides_) {
            DraftSlide c = s;
            c.id = NewId();
            it->slides.insert(it->slides.begin() + draft_.selSlide + 1, std::move(c));
            draft_.selSlide++;
        }
    }
    SyncEditsFromDraft();
    Repaint();
}

} // namespace fusion
