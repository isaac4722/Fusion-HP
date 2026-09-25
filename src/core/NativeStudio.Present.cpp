// ============================================================================
//  Fusion-HP · NativeStudio.Present.cpp — consola del presentador replicada
//  (PresentMode.tsx de la web): cabecera EN VIVO + reloj, programa con
//  sub-líneas, vista previa + A continuación, Pausas B/C/L, transporte con
//  avance conmutable y Biblia rápida (G).
// ============================================================================
#include "NativeStudio.h"
#include "Motor.h"
#include "Monitors.h"
#include "Logger.h"

namespace fusion {

// puente con los ids del .cpp principal
namespace nspres {
enum {
    CMD_P_EDIT = 1000, CMD_P_RESTART, CMD_P_MONITOR, CMD_P_PREV, CMD_P_NEXT,
    CMD_P_BLANK_B, CMD_P_BLANK_C, CMD_P_BLANK_L, CMD_P_ADVANCE, CMD_P_BIBLE,
    CMD_P_ITEM = 1100, CMD_P_SLIDE = 1101, CMD_P_LINE = 1102,
    CMD_P_VERSE_BASE = 1200,
    CMD_PRESENT = 105, CMD_MANDO = 171, CMD_SHORTCUTS = 781,
    CMD_INSERT_REF = 700, CMD_CH_PREV = 703, CMD_CH_NEXT = 704,
};
}

using namespace nspres;

void NativeStudio::PaintPresent(Gdiplus::Graphics& g, const RECT& cli) {
    int W = cli.right, H = cli.bottom;

    // esconder edits de otros modos
    PlaceEdit(edtName_, RECT{}, false);
    PlaceEdit(edtSearch_, RECT{}, false);
    PlaceEdit(edtTitle_, RECT{}, false);
    PlaceEdit(edtSub_, RECT{}, false);
    PlaceEdit(edtLines_, RECT{}, false);
    PlaceEdit(edtNotes_, RECT{}, false);
    PlaceEdit(edtDesc_, RECT{}, false);

    // ================ cabecera (web: h-11 blanca) ================
    Gdiplus::SolidBrush wp(ui::ToColor(ui::Paper));
    g.FillRectangle(&wp, (Gdiplus::REAL)(0), (Gdiplus::REAL)(0),  (Gdiplus::REAL)W, (Gdiplus::REAL)(44));
    Gdiplus::Pen lp(ui::ToColor(ui::Border), 1.0f);
    g.DrawLine(&lp, (Gdiplus::REAL)(0),  43.5f,  (Gdiplus::REAL)W,  43.5f);
    RECT eb{10, 8, 92, 36};
    ui::Chip(g, eb, L"Editar", L"", "chevron-left", (hoverId_ == CMD_P_EDIT ? ui::kHot : 0));
    HitAdd(CMD_P_EDIT, 0, 0, eb);
    HDC dc = g.GetHDC();
    SetBkMode(dc, TRANSPARENT);
    SelectObject(dc, ui::Font(14, FW_SEMIBOLD));
    SetTextColor(dc, ui::Cref(ui::Ink));
    RECT nt{104, 4, W - 460, 24};
    DrawTextW(dc, draft_.name.c_str(), -1, &nt, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
    SelectObject(dc, ui::Font(11));
    SetTextColor(dc, ui::Cref(ui::Gray));
    RECT st{104, 24, W - 460, 40};
    DrawTextW(dc, L"Vista del presentador", -1, &st, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);
    // badge EN VIVO
    RECT lv{W - 450, 12, W - 386, 32};
    ui::RoundRect(g, lv, 8, ui::ToColor(ui::DangerBg));
    Gdiplus::SolidBrush dot(ui::ToColor(ui::LiveRed));
    g.FillEllipse(&dot,  (Gdiplus::REAL)lv.left + 8,  (Gdiplus::REAL)lv.top + 9, (Gdiplus::REAL)(5), (Gdiplus::REAL)(5));
    dc = g.GetHDC();
    SelectObject(dc, ui::Font(10, FW_SEMIBOLD));
    SetTextColor(dc, ui::Cref(ui::DangerTx));
    RECT lvt{lv.left + 16, lv.top, lv.right - 4, lv.bottom};
    DrawTextW(dc, L"EN VIVO", -1, &lvt, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    // reloj
    if (showClock_) {
        SelectObject(dc, ui::Font(12));
        SetTextColor(dc, ui::Cref(ui::Gray));
        RECT ct{W - 370, 12, W - 300, 32};
        DrawTextW(dc, ClockText().c_str(), -1, &ct, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
    }
    g.ReleaseHDC(dc);
    // iconos: proyector, atajos, reiniciar
    RECT mb{W - 288, 8, W - 260, 36};
    ui::IconButton(g, mb, "device-desktop", (hoverId_ == CMD_P_MONITOR ? ui::kHot : 0));
    HitAdd(CMD_P_MONITOR, 0, 0, mb);
    RECT kb{W - 256, 8, W - 228, 36};
    ui::IconButton(g, kb, "keyboard", (hoverId_ == CMD_SHORTCUTS ? ui::kHot : 0));
    HitAdd(CMD_SHORTCUTS, 0, 0, kb);
    RECT rb{W - 120, 8, W - 12, 36};
    ui::Chip(g, rb, L"Reiniciar", L"", "player-play",
             ui::kAcc | (hoverId_ == CMD_P_RESTART ? ui::kHot : 0));
    HitAdd(CMD_P_RESTART, 0, 0, rb);

    // ================ cuerpo ================
    RECT body{0, 44, W, H};
    Gdiplus::SolidBrush cbg(ui::ToColor(ui::CanvasBg));
    g.FillRectangle(&cbg, (Gdiplus::REAL)(0), (Gdiplus::REAL)(44),  (Gdiplus::REAL)W,  (Gdiplus::REAL)(H - 44));

    // ---- izquierda: programa (web aside 340px) ----
    RECT aside{0, 44, std::min(340, W / 3), H};
    Gdiplus::SolidBrush ab(ui::ToColor(ui::Paper));
    g.FillRectangle(&ab, (Gdiplus::REAL)(aside.left), (Gdiplus::REAL)(aside.top),  (Gdiplus::REAL)(aside.right - aside.left), 
                    (Gdiplus::REAL)(aside.bottom - aside.top));
    Gdiplus::Pen ap(ui::ToColor(ui::Border), 1.0f);
    g.DrawLine(&ap,  (Gdiplus::REAL)aside.right - 0.5f, (Gdiplus::REAL)(aside.top), 
               (Gdiplus::REAL)aside.right - 0.5f,  (Gdiplus::REAL)aside.bottom);
    dc = g.GetHDC();
    SetBkMode(dc, TRANSPARENT);
    SelectObject(dc, ui::Font(13, FW_SEMIBOLD));
    SetTextColor(dc, ui::Cref(ui::Ink));
    RECT pt{aside.left + 14, aside.top + 8, aside.right, aside.top + 26};
    DrawTextW(dc, L"Programa", -1, &pt, DT_LEFT | DT_VCENTER);
    SelectObject(dc, ui::Font(10));
    SetTextColor(dc, ui::Cref(ui::GrayLt));
    RECT pc{aside.left + 14, aside.top + 24, aside.right, aside.top + 38};
    DrawTextW(dc, (std::to_wstring(progTitles_.size()) + L" elementos").c_str(), -1, &pc,
              DT_LEFT | DT_VCENTER);
    g.ReleaseHDC(dc);

    // lista de secciones con miniaturas 2 columnas + sub-líneas
    RECT listR{aside.left, aside.top + 42, aside.right, aside.bottom - 96};
    int total = 0;
    const Json& prog = motorSnapshot_.contains("program") ? motorSnapshot_["program"] : Json::array();
    struct Row { int el; };
    int rows = 0;
    for (auto& sc : prog) if (sc.contains("elements")) rows += (int)sc["elements"].size();
    // alto estimado: cada elemento = 28 (cabecera) + thumbs grid + 8
    int est = 0;
    if (curScn_ >= 0 && curScn_ < (int)prog.size() && prog[(size_t)curScn_].contains("elements")) {
        const Json& els = prog[(size_t)curScn_]["elements"];
        int cols = 2, tw = (aside.right - aside.left - 36) / 2 - 4;
        for (size_t i = 0; i < els.size(); i++) {
            est += 26;
            est += (int)(((int)els.size() + cols - 1) / cols) * (tw * 9 / 16 + 8);
            const Json& el = els[i];
            if ((int)i == curEl_ && el.contains("slide") && el["slide"].contains("lines")) {
                int lc = (int)el["slide"]["lines"].size();
                est += lc * 18 + 10;
            }
        }
    }
    ScrollAdd(listR, scProgram_, std::max(0, (int)(est - (listR.bottom - listR.top))));

    if (curScn_ >= 0 && curScn_ < (int)prog.size() && prog[(size_t)curScn_].contains("elements")) {
        const Json& els = prog[(size_t)curScn_]["elements"];
        int y = listR.top - scProgram_;
        int tw = (aside.right - aside.left - 36) / 2 - 4, th = tw * 9 / 16;
        for (int i = 0; i < (int)els.size(); i++) {
            const Json& el = els[(size_t)i];
            // cabecera del elemento
            RECT hr{aside.left + 8, y, aside.right - 8, y + 24};
            if (hr.bottom > listR.top && hr.top < listR.bottom) {
                ui::Row(g, hr, i + 1, ToWide(el.value("title", std::string())),
                        ToWide(el.value("kind", std::string())), "",
                        (i == curEl_ ? ui::kOn : 0) |
                        (hoverId_ == CMD_P_ITEM && hoverA_ == i ? ui::kHot : 0));
                HitAdd(CMD_P_ITEM, i, 0, hr);
            }
            y += 26;
            // miniatura del elemento (una diapositiva por elemento en el Motor)
            RECT tb{aside.left + 18, y, aside.left + 18 + tw, y + th};
            if (tb.bottom > listR.top && tb.top < listR.bottom && el.contains("slide")) {
                Gdiplus::Bitmap* bmp = ThumbOf(el["slide"], tw, th,
                                               "p:" + el.value("id", std::string()));
                if (bmp) g.DrawImage(bmp, tb.left, tb.top, tw, th);
                else {
                    Gdiplus::SolidBrush bk(ui::ToColor(0xFF000000));
                    g.FillRectangle(&bk, (Gdiplus::REAL)(tb.left), (Gdiplus::REAL)(tb.top),  (Gdiplus::REAL)(tb.right - tb.left), 
                                    (Gdiplus::REAL)(tb.bottom - tb.top));
                }
                Gdiplus::Pen tp(i == curEl_ ? ui::ToColor(ui::Accent) : ui::ToColor(ui::Border2),
                                i == curEl_ ? 2.0f : 1.0f);
                g.DrawRectangle(&tp,  tb.left - 0.5f,  tb.top - 0.5f, 
                                (Gdiplus::REAL)(tb.right - tb.left), 
                                (Gdiplus::REAL)(tb.bottom - tb.top));
                dc = g.GetHDC();
                SelectObject(dc, ui::Font(9));
                SetBkMode(dc, TRANSPARENT);
                SetTextColor(dc, 0xCCFFFFFF);
                RECT nb2{tb.right - 20, tb.bottom - 16, tb.right - 2, tb.bottom - 2};
                DrawTextW(dc, L"1", -1, &nb2, DT_RIGHT | DT_SINGLELINE);
                g.ReleaseHDC(dc);
                HitAdd(CMD_P_SLIDE, i, 0, tb);
            }
            y += th + 6;
            // sub-líneas del elemento activo (borde acento, web)
            if (i == curEl_ && el.contains("slide") && el["slide"].contains("lines")) {
                const Json& lines = el["slide"]["lines"];
                RECT lbar{aside.left + 24, y, aside.left + 26, y + (int)lines.size() * 18 + 4};
                Gdiplus::SolidBrush ab2(ui::ToColor(ui::Accent));
                g.FillRectangle(&ab2,  (Gdiplus::REAL)lbar.left,  (Gdiplus::REAL)lbar.top,  2.0f, 
                                (Gdiplus::REAL)(lbar.bottom - lbar.top));
                for (int k = 0; k < (int)lines.size(); k++) {
                    RECT lr{aside.left + 32, y, aside.right - 8, y + 18};
                    if (lr.bottom > listR.top && lr.top < listR.bottom) {
                        bool cur = (k == curLine_);
                        if (cur) {
                            Gdiplus::SolidBrush hb(ui::ToColor(ui::AccentBg));
                            g.FillRectangle(&hb, (Gdiplus::REAL)(lr.left - 2), (Gdiplus::REAL)(lr.top),  (Gdiplus::REAL)(lr.right - lr.left + 2), 
                                            (Gdiplus::REAL)(lr.bottom - lr.top));
                        }
                        dc = g.GetHDC();
                        SelectObject(dc, ui::Font(11, cur ? FW_SEMIBOLD : FW_NORMAL));
                        SetBkMode(dc, TRANSPARENT);
                        SetTextColor(dc, cur ? ui::Cref(ui::AccentDk) : ui::Cref(ui::Gray));
                        DrawTextW(dc, ToWide(lines[(size_t)k].get<std::string>()).c_str(), -1, &lr,
                                  DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
                        g.ReleaseHDC(dc);
                        HitAdd(CMD_P_LINE, k, 0, lr);
                    }
                    y += 18;
                }
                y += 8;
            }
            y += 6;
        }
    } else {
        dc = g.GetHDC();
        SelectObject(dc, ui::Font(12));
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, ui::Cref(ui::Gray));
        RECT tr = listR;
        DrawTextW(dc, L"Sin programa.\nVuelve al editor y pulsa Presentar (F5).", -1, &tr,
                  DT_CENTER | DT_VCENTER | DT_WORDBREAK);
        g.ReleaseHDC(dc);
    }

    // botón Biblia rápida (footer del aside, web)
    RECT bf{aside.left + 10, aside.bottom - 88, aside.right - 10, aside.bottom - 58};
    ui::Chip(g, bf, L"Biblia rápida", L"G", "book",
             (bibleOpen_ ? ui::kOn : 0) | (hoverId_ == CMD_P_BIBLE ? ui::kHot : 0));
    HitAdd(CMD_P_BIBLE, 0, 0, bf);
    if (bibleOpen_) {
        RECT bq{aside.left + 10, aside.bottom - 54, aside.right - 10, aside.bottom - 28};
        ui::Field(g, bq, GetFocus() == edtBible_, false);
        PlaceEdit(edtBible_, RECT{bq.left + 2, bq.top + 1, bq.right - 2, bq.bottom - 1}, true);
        // resultados (máx 5, web max-h-40)
        std::vector<std::pair<std::wstring, std::wstring>> hits;
        if (!bibleQuery_.empty()) {
            std::wstring ref;
            std::vector<std::wstring> lns;
            if (bibles_.Range(bibleQuery_, ref, lns)) {
                std::wstring text;
                for (auto& l : lns) text += l;
                hits.push_back({ref, text});
            }
        }
        int hy = aside.bottom - 26;
        for (auto& h : hits) {
            if (hy > aside.bottom - 8) break;
            RECT hr{aside.left + 10, hy, aside.right - 10, hy + 20};
            dc = g.GetHDC();
            SelectObject(dc, ui::Font(11, FW_SEMIBOLD));
            SetBkMode(dc, TRANSPARENT);
            SetTextColor(dc, ui::Cref(ui::Accent));
            RECT r1{hr.left, hr.top, hr.right, hr.top + 12};
            DrawTextW(dc, h.first.c_str(), -1, &r1, DT_LEFT | DT_SINGLELINE);
            SelectObject(dc, ui::Font(10));
            SetTextColor(dc, ui::Cref(ui::Gray));
            RECT r2{hr.left, hr.top + 11, hr.right, hr.bottom};
            DrawTextW(dc, h.second.c_str(), -1, &r2, DT_LEFT | DT_SINGLELINE | DT_END_ELLIPSIS);
            g.ReleaseHDC(dc);
            HitAdd(CMD_INSERT_REF, 0, 0, hr);
            hy += 22;
        }
    } else {
        PlaceEdit(edtBible_, RECT{}, false);
    }

    // ---- derecha: preview + A continuación + pausas + transporte ----
    RECT main{aside.right, 44, W, H};
    int pad = 16;
    // cabecera PROGRAMA
    dc = g.GetHDC();
    SetBkMode(dc, TRANSPARENT);
    RECT ptag{main.left + pad, main.top + 10, main.left + pad + 86, main.top + 28};
    g.ReleaseHDC(dc);
    Gdiplus::SolidBrush tb2(ui::ToColor(ui::Accent));
    ui::RoundRect(g, ptag, 2, tb2);
    dc = g.GetHDC();
    SelectObject(dc, ui::Font(10, FW_BOLD));
    SetTextColor(dc, 0xFFFFFF);
    RECT ptagT = ptag;
    DrawTextW(dc, L"PROGRAMA", -1, &ptagT, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    SelectObject(dc, ui::Font(13));
    SetTextColor(dc, ui::Cref(ui::Ink));
    RECT pl{main.left + pad + 96, main.top + 8, main.left + 420, main.top + 30};
    DrawTextW(dc, (curEl_ >= 0 && curEl_ < (int)progTitles_.size()
                   ? ToWide(progTitles_[(size_t)curEl_].first) : L"Sin elementos").c_str(),
              -1, &pl, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
    g.ReleaseHDC(dc);

    int availW = main.right - main.left - pad * 2;
    int rightW = std::min(232, availW / 3);
    int prevW = availW - rightW - 12;
    int prevH = prevW * 9 / 16;
    int maxPrevH = (H - 44) - 120 - 90;
    if (prevH > maxPrevH) { prevH = maxPrevH; prevW = prevH * 16 / 9; }

    // vista previa grande
    RECT pv{main.left + pad, main.top + 40, main.left + pad + prevW, main.top + 40 + prevH};
    Gdiplus::Pen pp(ui::ToColor(0xFFC8C6C4), 1.0f);
    Gdiplus::SolidBrush pbb(ui::ToColor(0xFF000000));
    g.FillRectangle(&pbb, (Gdiplus::REAL)(pv.left), (Gdiplus::REAL)(pv.top),  (Gdiplus::REAL)(pv.right - pv.left), 
                    (Gdiplus::REAL)(pv.bottom - pv.top));
    g.DrawRectangle(&pp, (Gdiplus::REAL)(pv.left), (Gdiplus::REAL)(pv.top),  (Gdiplus::REAL)(pv.right - pv.left - 1), 
                    (Gdiplus::REAL)(pv.bottom - pv.top - 1));
    Json cur = CurrentSlideJson();
    if (!cur.is_null()) {
        Gdiplus::Bitmap* bmp = ThumbOf(cur, prevW, prevH, "prev:cur");
        if (bmp) g.DrawImage(bmp, pv.left, pv.top, prevW, prevH);
    }

    // columna derecha: A continuación + notas + pausas + salidas
    int rx = pv.right + 12, ry = main.top + 40;
    RECT rp{rx, ry, main.right - pad, H - 96};
    Gdiplus::SolidBrush rpb(ui::ToColor(ui::Paper));
    Gdiplus::Pen rbp(ui::ToColor(ui::Border2));
    g.FillRectangle(&rpb, (Gdiplus::REAL)(rp.left), (Gdiplus::REAL)(rp.top),  (Gdiplus::REAL)(rp.right - rp.left - 1), 
                    (Gdiplus::REAL)(rp.bottom - rp.top - 1));
    g.DrawRectangle(&rbp, (Gdiplus::REAL)(rp.left), (Gdiplus::REAL)(rp.top),  (Gdiplus::REAL)(rp.right - rp.left - 1), 
                    (Gdiplus::REAL)(rp.bottom - rp.top - 1));
    int yy = rp.top + 8;
    dc = g.GetHDC();
    SelectObject(dc, ui::Font(11, FW_SEMIBOLD));
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, ui::Cref(ui::GrayLt));
    RECT nh{rp.left + 10, yy, rp.right, yy + 16};
    DrawTextW(dc, L"A CONTINUACIÓN", -1, &nh, DT_LEFT | DT_SINGLELINE);
    g.ReleaseHDC(dc);
    yy += 20;
    int nw = rp.right - rp.left - 20, nh2 = nw * 9 / 16;
    // siguiente elemento del Motor
    Json next;
    if (curScn_ >= 0 && curScn_ < (int)prog.size() && prog[(size_t)curScn_].contains("elements")) {
        const Json& els = prog[(size_t)curScn_]["elements"];
        if (curEl_ + 1 < (int)els.size()) next = els[(size_t)curEl_ + 1]["slide"];
        else if (curScn_ + 1 < (int)prog.size() && prog[(size_t)(curScn_ + 1)].contains("elements")
                 && !prog[(size_t)(curScn_ + 1)]["elements"].empty())
            next = prog[(size_t)(curScn_ + 1)]["elements"][0]["slide"];
    }
    RECT nb{rp.left + 10, yy, rp.left + 10 + nw, yy + nh2};
    if (!next.is_null()) {
        Gdiplus::Bitmap* bmp = ThumbOf(next, nw, nh2, "prev:next");
        if (bmp) g.DrawImage(bmp, nb.left, nb.top, nw, nh2);
    } else {
        Gdiplus::SolidBrush bk(ui::ToColor(0xFF000000));
        g.FillRectangle(&bk, (Gdiplus::REAL)(nb.left), (Gdiplus::REAL)(nb.top),  (Gdiplus::REAL)(nb.right - nb.left), 
                        (Gdiplus::REAL)(nb.bottom - nb.top));
        dc = g.GetHDC();
        SelectObject(dc, ui::Font(11));
        SetTextColor(dc, 0x99FFFFFF);
        RECT t = nb;
        DrawTextW(dc, L"Fin de secuencia", -1, &t, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        g.ReleaseHDC(dc);
    }
    yy += nh2 + 10;
    // notas
    dc = g.GetHDC();
    SelectObject(dc, ui::Font(11, FW_SEMIBOLD));
    SetTextColor(dc, ui::Cref(ui::GrayLt));
    RECT ph{rp.left + 10, yy, rp.right, yy + 16};
    DrawTextW(dc, L"PAUSAS", -1, &ph, DT_LEFT | DT_SINGLELINE);
    g.ReleaseHDC(dc);
    yy += 20;
    struct PauseBtn { int id; const wchar_t* label, *key; const char* on; };
    PauseBtn pbs[] = {{CMD_P_BLANK_B, L"Negro", L"B", "black"},
                       {CMD_P_BLANK_C, L"Limpiar texto", L"C", "clear"},
                       {CMD_P_BLANK_L, L"Logo", L"L", "logo"}};
    for (auto& b : pbs) {
        RECT r{rp.left + 10, yy, rp.right - 10, yy + 30};
        bool on = blank_ == b.on;
        ui::Chip(g, r, b.label, b.key, "", (on ? ui::kOn : 0) | (hoverId_ == b.id ? ui::kHot : 0));
        HitAdd(b.id, 0, 0, r);
        yy += 34;
    }
    dc = g.GetHDC();
    SelectObject(dc, ui::Font(11, FW_SEMIBOLD));
    SetTextColor(dc, ui::Cref(ui::GrayLt));
    RECT sh{rp.left + 10, yy, rp.right, yy + 16};
    DrawTextW(dc, L"SALIDAS", -1, &sh, DT_LEFT | DT_SINGLELINE);
    g.ReleaseHDC(dc);
    yy += 20;
    RECT mb2{rp.left + 10, yy, rp.right - 10, yy + 30};
    ui::Chip(g, mb2, L"Abrir proyector", L"", "device-desktop",
             hoverId_ == CMD_P_MONITOR ? ui::kHot : 0);
    HitAdd(CMD_P_MONITOR, 0, 0, mb2);
    yy += 34;
    RECT mdb{rp.left + 10, yy, rp.right - 10, yy + 30};
    ui::Chip(g, mdb, L"Control remoto (Mando)", L"", "device-mobile",
             hoverId_ == CMD_MANDO ? ui::kHot : 0);
    HitAdd(CMD_MANDO, 0, 0, mdb);

    // ---- transporte (web Transport) ----
    RECT tr{main.left + pad, H - 84, main.right - pad, H - 12};
    Gdiplus::SolidBrush tbb(ui::ToColor(ui::Paper));
    Gdiplus::Pen trp(ui::ToColor(ui::Border2));
    g.FillRectangle(&tbb, (Gdiplus::REAL)(tr.left), (Gdiplus::REAL)(tr.top),  (Gdiplus::REAL)(tr.right - tr.left - 1), 
                    (Gdiplus::REAL)(tr.bottom - tr.top - 1));
    g.DrawRectangle(&trp, (Gdiplus::REAL)(tr.left), (Gdiplus::REAL)(tr.top),  (Gdiplus::REAL)(tr.right - tr.left - 1), 
                    (Gdiplus::REAL)(tr.bottom - tr.top - 1));
    // prev circular + next circular acento
    RECT pvb{tr.left + 16, tr.top + 14, tr.left + 52, tr.top + 50};
    RECT nxb{tr.left + 60, tr.top + 8, tr.left + 104, tr.top + 52};
    Gdiplus::SolidBrush pvl(ui::ToColor(ui::Paper));
    Gdiplus::Pen pcp(ui::ToColor(0xFFC8C6C4), 1.2f);
    g.FillEllipse(&pvl,  (Gdiplus::REAL)pvb.left,  (Gdiplus::REAL)pvb.top, (Gdiplus::REAL)(36), (Gdiplus::REAL)(36));
    g.DrawEllipse(&pcp,  (Gdiplus::REAL)pvb.left,  (Gdiplus::REAL)pvb.top, (Gdiplus::REAL)(36), (Gdiplus::REAL)(36));
    if (ui::Icon("player-skip-back", ui::TintInk))
        g.DrawImage(ui::Icon("player-skip-back", ui::TintInk), pvb.left + 8, pvb.top + 8, 20, 20);
    HitAdd(CMD_P_PREV, 0, 0, pvb);
    Gdiplus::SolidBrush nxb2(ui::ToColor(ui::Accent));
    g.FillEllipse(&nxb2,  (Gdiplus::REAL)nxb.left,  (Gdiplus::REAL)nxb.top, (Gdiplus::REAL)(44), (Gdiplus::REAL)(44));
    if (ui::Icon("player-skip-forward", ui::TintWhite))
        g.DrawImage(ui::Icon("player-skip-forward", ui::TintWhite), nxb.left + 10, nxb.top + 10, 24, 24);
    HitAdd(CMD_P_NEXT, 0, 0, nxb);
    // contadores + ayuda
    dc = g.GetHDC();
    SetBkMode(dc, TRANSPARENT);
    SelectObject(dc, ui::Font(12));
    SetTextColor(dc, ui::Cref(ui::Gray));
    RECT ct{tr.left + 130, tr.top + 12, tr.left + 460, tr.top + 32};
    DrawTextW(dc, (L"Elemento " + std::to_wstring(curEl_ + 1 > 0 ? curEl_ + 1 : 0) + L" / " +
                   std::to_wstring(progTitles_.size()) + L"   ·   Línea " +
                   std::to_wstring(curLine_ + 1)).c_str(), -1, &ct,
              DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    SelectObject(dc, ui::Font(11));
    RECT ht{tr.left + 130, tr.top + 34, tr.left + 470, tr.top + 54};
    DrawTextW(dc, L"Espacio o → avanzar · B negro · C limpiar · L logo · Esc salir · G biblia", -1,
              &ht, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);
    // avance conmutable
    RECT adv{tr.right - 150, tr.top + 20, tr.right - 14, tr.top + 46};
    ui::Chip(g, adv, advance_ == "line" ? L"Línea a línea" : L"Diapositiva", L"",
             "bolt", ui::kOn | (hoverId_ == CMD_P_ADVANCE ? ui::kHot : 0));
    HitAdd(CMD_P_ADVANCE, 0, 0, adv);
}

// ---- insertar la referencia escrita (biblia rápida / biblioteca) ----
void NativeStudio::InsertVerseRange() {
    std::wstring q = bibleQuery_;
    if (q.empty()) q = libQuery_;
    if (q.empty()) return;
    std::wstring ref;
    std::vector<std::wstring> lns;
    if (!bibles_.Range(q, ref, lns)) {
        MessageBoxW(hwnd_, (L"No se encontró la referencia \"" + q + L"\".\n\n"
                            L"Prueba con el formato: juan 3:16 · salmos 23 · 1 corintios 13:4-7")
                    .c_str(), L"Fusion HP", MB_ICONINFORMATION);
        return;
    }
    std::wstring text;
    for (auto& l : lns) text += l;
    if (mode_ == Mode::Present) {
        // addVerseQuiet de la web: insertar como escenario propio y saltar a él
        PushUndo();
        DraftItem it;
        it.id = NewId();
        it.type = "verse";
        it.label = ref;
        DraftSlide sl;
        sl.id = NewId();
        sl.type = "verse";
        sl.title = ref;
        sl.reference = ref;
        sl.lines = lns;
        it.slides.push_back(std::move(sl));
        draft_.items.push_back(std::move(it));
        // programa nuevo con el versículo al final y selección en él
        Json payload = DraftToProgram(draft_, true, animate_);
        if (motor_) motor_->LoadProgram(payload);
        SyncFromMotor();
        bibleOpen_ = false;
        SetWindowTextW(edtBible_, L"");
        bibleQuery_.clear();
    } else {
        PushUndo();
        AddVerseToDraft(draft_, ref, text, bibleAsLt_, "");
        SyncEditsFromDraft();
    }
    Repaint();
}

} // namespace fusion
