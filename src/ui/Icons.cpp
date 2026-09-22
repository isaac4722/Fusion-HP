// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Icons.cpp : implementacion de la fabrica de iconos (ver Icons.h)
// ============================================================================
#include "Icons.h"

#include <wx/dcmemory.h>
#include <wx/graphics.h>
#include <wx/image.h>

#include <cmath>
#include <map>
#include <utility>

namespace {
constexpr double kPi = 3.14159265358979323846;

const wxColour kGold   = wxColour(230, 168, 42);
const wxColour kInk    = wxColour(225, 231, 240);
const wxColour kMuted  = wxColour(148, 160, 178);
const wxColour kDanger = wxColour(224, 92, 92);
const wxColour kDark   = wxColour(18, 22, 30);
const wxColour kOk     = wxColour(63, 191, 127);
const wxColour kBlue   = wxColour(96, 165, 250);

wxBitmap Draw(const wxString &name, int px)
{
    // Lienzo transparente garantizado (wxImage inicializado a cero)
    wxImage img(px, px);
    img.InitAlpha();
    img.Clear();
    wxBitmap bmp(img);
    wxMemoryDC mdc(bmp);
    wxGraphicsContext *gc = wxGraphicsContext::Create(mdc);
    if (!gc)
        return bmp;
    gc->SetAntialiasMode(wxANTIALIAS_DEFAULT);

    const double u = px / 24.0;          // unidad de diseño 24x24
    const double wd = 2.1 * u;

    auto stroke = [&](const wxColour &c, double w) {
        gc->SetPen(wxPen(c, std::max(1, (int)w)));
    };
    auto fill = [&](const wxColour &c) {
        gc->SetBrush(wxBrush(c));
        gc->SetPen(*wxTRANSPARENT_PEN);
    };

    if (name == "live") {
        fill(kGold);
        gc->DrawEllipse(3 * u, 3 * u, 18 * u, 18 * u);
        fill(kDark);
        gc->DrawEllipse(5.4 * u, 5.4 * u, 13.2 * u, 13.2 * u);
        fill(kGold);
        wxGraphicsPath p = gc->CreatePath();
        p.MoveToPoint(9.6 * u, 7.6 * u);
        p.AddLineToPoint(17.0 * u, 12.0 * u);
        p.AddLineToPoint(9.6 * u, 16.4 * u);
        p.CloseSubpath();
        gc->FillPath(p);
    } else if (name == "next") {
        stroke(kInk, wd);
        wxGraphicsPath p = gc->CreatePath();
        p.MoveToPoint(6 * u, 5 * u);
        p.AddLineToPoint(14 * u, 12 * u);
        p.AddLineToPoint(6 * u, 19 * u);
        gc->StrokePath(p);
        gc->StrokeLine(16.5 * u, 5 * u, 16.5 * u, 19 * u);
    } else if (name == "prev") {
        stroke(kInk, wd);
        wxGraphicsPath p = gc->CreatePath();
        p.MoveToPoint(18 * u, 5 * u);
        p.AddLineToPoint(10 * u, 12 * u);
        p.AddLineToPoint(18 * u, 19 * u);
        gc->StrokePath(p);
        gc->StrokeLine(7.5 * u, 5 * u, 7.5 * u, 19 * u);
    } else if (name == "black") {
        fill(kInk);
        gc->DrawEllipse(3.5 * u, 3.5 * u, 17 * u, 17 * u);
        fill(kDark);
        gc->DrawEllipse(6.2 * u, 6.2 * u, 11.6 * u, 11.6 * u);
    } else if (name == "clear") {
        stroke(kDanger, 2.2 * u);
        gc->StrokeLine(6 * u, 6 * u, 18 * u, 18 * u);
        gc->StrokeLine(18 * u, 6 * u, 6 * u, 18 * u);
    } else if (name == "logo") {
        stroke(kGold, 1.9 * u);
        wxGraphicsPath p = gc->CreatePath();
        p.MoveToPoint(4.5 * u, 11 * u);
        p.AddLineToPoint(12 * u, 4.5 * u);
        p.AddLineToPoint(19.5 * u, 11 * u);
        gc->StrokePath(p);
        wxGraphicsPath h = gc->CreatePath();
        h.MoveToPoint(7 * u, 11 * u);
        h.AddLineToPoint(7 * u, 19 * u);
        h.AddLineToPoint(17 * u, 19 * u);
        h.AddLineToPoint(17 * u, 11 * u);
        gc->StrokePath(h);
    } else if (name == "add") {
        stroke(kInk, wd);
        gc->StrokeLine(12 * u, 5 * u, 12 * u, 19 * u);
        gc->StrokeLine(5 * u, 12 * u, 19 * u, 12 * u);
    } else if (name == "edit") {
        stroke(kInk, 1.8 * u);
        wxGraphicsPath p = gc->CreatePath();
        p.MoveToPoint(5 * u, 19 * u);
        p.AddLineToPoint(6.2 * u, 14.6 * u);
        p.AddLineToPoint(15.4 * u, 5.4 * u);
        p.AddLineToPoint(18.6 * u, 8.6 * u);
        p.AddLineToPoint(9.4 * u, 17.8 * u);
        p.CloseSubpath();
        gc->StrokePath(p);
        gc->StrokeLine(13.6 * u, 7.2 * u, 16.8 * u, 10.4 * u);
    } else if (name == "copy") {
        stroke(kMuted, 1.7 * u);
        gc->DrawRoundedRectangle(8.5 * u, 8.5 * u, 10 * u, 10.5 * u, 2 * u);
        wxGraphicsPath p = gc->CreatePath();
        p.MoveToPoint(15.5 * u, 8.5 * u);
        p.AddLineToPoint(15.5 * u, 5 * u);
        p.AddLineToPoint(5 * u, 5 * u);
        p.AddLineToPoint(5 * u, 15.5 * u);
        p.AddLineToPoint(8.5 * u, 15.5 * u);
        gc->StrokePath(p);
    } else if (name == "del") {
        stroke(kDanger, 1.8 * u);
        gc->DrawRoundedRectangle(5.5 * u, 7 * u, 13 * u, 13 * u, 2 * u);
        gc->StrokeLine(9.5 * u, 7 * u, 9.5 * u, 4.5 * u);
        gc->StrokeLine(9.5 * u, 4.5 * u, 14.5 * u, 4.5 * u);
        gc->StrokeLine(14.5 * u, 4.5 * u, 14.5 * u, 7 * u);
        gc->StrokeLine(9 * u, 11 * u, 9 * u, 16 * u);
        gc->StrokeLine(15 * u, 11 * u, 15 * u, 16 * u);
    } else if (name == "up" || name == "down") {
        stroke(kInk, wd);
        wxGraphicsPath p = gc->CreatePath();
        if (name == "up") {
            p.MoveToPoint(6 * u, 14.5 * u);
            p.AddLineToPoint(12 * u, 8 * u);
            p.AddLineToPoint(18 * u, 14.5 * u);
        } else {
            p.MoveToPoint(6 * u, 9.5 * u);
            p.AddLineToPoint(12 * u, 16 * u);
            p.AddLineToPoint(18 * u, 9.5 * u);
        }
        gc->StrokePath(p);
    } else if (name == "search") {
        stroke(kInk, 1.9 * u);
        gc->DrawEllipse(5 * u, 5 * u, 9.5 * u, 9.5 * u);
        gc->StrokeLine(13.2 * u, 13.2 * u, 19 * u, 19 * u);
    } else if (name == "gear") {
        stroke(kInk, 1.7 * u);
        gc->DrawEllipse(8.4 * u, 8.4 * u, 7.2 * u, 7.2 * u);
        for (int i = 0; i < 8; ++i) {
            const double a = i * kPi / 4.0;
            gc->StrokeLine(12 * u + std::cos(a) * 8.2 * u, 12 * u + std::sin(a) * 8.2 * u,
                           12 * u + std::cos(a) * 10.8 * u, 12 * u + std::sin(a) * 10.8 * u);
        }
    } else if (name == "song") {
        fill(kInk);
        wxGraphicsPath p = gc->CreatePath();
        p.MoveToPoint(9 * u, 18.2 * u);
        p.AddCurveToPoint(7.2 * u, 18.2 * u, 6 * u, 17 * u, 6 * u, 15.6 * u);
        p.AddCurveToPoint(6 * u, 14.2 * u, 7.4 * u, 13.2 * u, 9 * u, 13.2 * u);
        p.AddCurveToPoint(9.7 * u, 13.2 * u, 10.4 * u, 13.4 * u, 10.9 * u, 13.8 * u);
        p.AddLineToPoint(10.9 * u, 5 * u);
        p.AddLineToPoint(18.5 * u, 6.6 * u);
        p.AddLineToPoint(18.5 * u, 9.4 * u);
        p.AddLineToPoint(12.7 * u, 8.2 * u);
        p.AddLineToPoint(12.7 * u, 15.8 * u);
        p.AddCurveToPoint(12.7 * u, 17.2 * u, 10.9 * u, 18.2 * u, 9 * u, 18.2 * u);
        gc->FillPath(p);
    } else if (name == "bible") {
        stroke(kGold, 1.7 * u);
        wxGraphicsPath p = gc->CreatePath();
        p.MoveToPoint(6 * u, 4.5 * u);
        p.AddLineToPoint(18.5 * u, 4.5 * u);
        p.AddLineToPoint(18.5 * u, 19.5 * u);
        p.AddLineToPoint(6 * u, 19.5 * u);
        p.AddCurveToPoint(4.6 * u, 19.5 * u, 4.6 * u, 17 * u, 6 * u, 17 * u);
        p.AddLineToPoint(18.5 * u, 17 * u);
        gc->StrokePath(p);
        gc->StrokeLine(12.2 * u, 7 * u, 12.2 * u, 13 * u);
        gc->StrokeLine(9.2 * u, 10 * u, 15.2 * u, 10 * u);
    } else if (name == "media") {
        fill(kInk);
        gc->DrawRoundedRectangle(3.5 * u, 6 * u, 17 * u, 12 * u, 2.4 * u);
        fill(kDark);
        wxGraphicsPath p = gc->CreatePath();
        p.MoveToPoint(10.4 * u, 9.4 * u);
        p.AddLineToPoint(15.2 * u, 12 * u);
        p.AddLineToPoint(10.4 * u, 14.6 * u);
        p.CloseSubpath();
        gc->FillPath(p);
    } else if (name == "theme") {
        fill(kInk);
        gc->DrawEllipse(5 * u, 5 * u, 14 * u, 14 * u);
        fill(kGold);
        wxGraphicsPath p = gc->CreatePath();
        p.MoveToPoint(12 * u, 5 * u);
        p.AddArc(12 * u, 12 * u, 7 * u, -kPi / 2.0, kPi / 2.0, true);
        p.CloseSubpath();
        gc->FillPath(p);
    } else if (name == "service") {
        stroke(kInk, 1.8 * u);
        gc->StrokeLine(6.5 * u, 6.5 * u, 19 * u, 6.5 * u);
        gc->StrokeLine(6.5 * u, 12 * u, 19 * u, 12 * u);
        gc->StrokeLine(6.5 * u, 17.5 * u, 14 * u, 17.5 * u);
        fill(kGold);
        gc->DrawEllipse(3.6 * u, 5.6 * u, 1.8 * u, 1.8 * u);
        gc->DrawEllipse(3.6 * u, 11.1 * u, 1.8 * u, 1.8 * u);
        gc->DrawEllipse(3.6 * u, 16.6 * u, 1.8 * u, 1.8 * u);
    } else if (name == "screen") {
        stroke(kInk, 1.8 * u);
        gc->DrawRoundedRectangle(3.5 * u, 4.5 * u, 17 * u, 11.5 * u, 2 * u);
        gc->StrokeLine(9 * u, 19.5 * u, 15 * u, 19.5 * u);
        gc->StrokeLine(12 * u, 16 * u, 12 * u, 19.5 * u);
    } else if (name == "alert") {
        fill(kGold);
        wxGraphicsPath p = gc->CreatePath();
        p.MoveToPoint(12 * u, 3.5 * u);
        p.AddLineToPoint(21 * u, 19 * u);
        p.AddLineToPoint(3 * u, 19 * u);
        p.CloseSubpath();
        gc->FillPath(p);
        fill(kDark);
        gc->DrawRoundedRectangle(11.3 * u, 8.6 * u, 1.4 * u, 4.6 * u, 0.7 * u);
        gc->DrawEllipse(11.3 * u, 14.6 * u, 1.4 * u, 1.6 * u);
    } else if (name == "backup") {
        stroke(kGold, 1.8 * u);
        wxGraphicsPath p = gc->CreatePath();
        p.MoveToPoint(12 * u, 4 * u);
        p.AddLineToPoint(12 * u, 14 * u);
        gc->StrokePath(p);
        p = gc->CreatePath();
        p.MoveToPoint(7.5 * u, 9.5 * u);
        p.AddLineToPoint(12 * u, 14.2 * u);
        p.AddLineToPoint(16.5 * u, 9.5 * u);
        gc->StrokePath(p);
        gc->StrokeLine(4.5 * u, 17 * u, 19.5 * u, 17 * u);
    } else if (name == "remote") {
        fill(kInk);
        gc->DrawRoundedRectangle(7 * u, 3 * u, 10 * u, 18 * u, 2.2 * u);
        fill(kDark);
        gc->DrawEllipse(10.9 * u, 16 * u, 2.2 * u, 2.2 * u);
        fill(kOk);
        gc->DrawEllipse(10.9 * u, 5.4 * u, 2.2 * u, 2.2 * u);
    } else if (name == "check") {
        stroke(kOk, 2.4 * u);
        wxGraphicsPath p = gc->CreatePath();
        p.MoveToPoint(5.5 * u, 12.5 * u);
        p.AddLineToPoint(10 * u, 17 * u);
        p.AddLineToPoint(18.5 * u, 7 * u);
        gc->StrokePath(p);
    } else if (name == "warn") {
        fill(kGold);
        gc->DrawEllipse(4 * u, 4 * u, 16 * u, 16 * u);
        fill(*wxWHITE);
        gc->DrawRoundedRectangle(11.2 * u, 7 * u, 1.6 * u, 6.4 * u, 0.8 * u);
        gc->DrawEllipse(11.2 * u, 15.2 * u, 1.6 * u, 1.6 * u);
    } else if (name == "err") {
        fill(kDanger);
        gc->DrawEllipse(4 * u, 4 * u, 16 * u, 16 * u);
        stroke(*wxWHITE, 2.0 * u);
        gc->StrokeLine(8.8 * u, 8.8 * u, 15.2 * u, 15.2 * u);
        gc->StrokeLine(15.2 * u, 8.8 * u, 8.8 * u, 15.2 * u);
    } else if (name == "info") {
        fill(kBlue);
        gc->DrawEllipse(4 * u, 4 * u, 16 * u, 16 * u);
        fill(*wxWHITE);
        gc->DrawEllipse(11.15 * u, 6.8 * u, 1.7 * u, 1.7 * u);
        gc->DrawRoundedRectangle(11.15 * u, 10 * u, 1.7 * u, 7 * u, 0.85 * u);
    } else {
        fill(kGold);
        gc->DrawEllipse(8 * u, 8 * u, 8 * u, 8 * u);
    }

    delete gc;
    return bmp;
}
} // namespace

wxBitmap Ico::Get(const wxString &name, int px)
{
    static std::map<std::pair<wxString, int>, wxBitmap> cache;
    const auto key = std::make_pair(name, px);
    auto it = cache.find(key);
    if (it != cache.end())
        return it->second;
    const wxBitmap bmp = Draw(name, px);
    cache[key] = bmp;
    return bmp;
}
