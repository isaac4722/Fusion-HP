// ============================================================================
//  Fusion-HP · Renderer.cpp — implementación del pipeline de render
// ============================================================================
#include "Renderer.h"
#include "Logger.h"

namespace fusion {

using namespace Gdiplus;

Renderer::Renderer() {}
Renderer::~Renderer() {
    delete fonts_;
    delete[] fontFamilies_;
    for (auto& c : cache_) {
        if (c.d2d) c.d2d->Release();
        delete c.gdip;
    }
    if (logoD2D_) logoD2D_->Release();
    delete logoGdip_;
    if (rt_) rt_->Release();
    if (d2d_) d2d_->Release();
    if (dw_) dw_->Release();
    if (wic_) wic_->Release();
    if (gdipToken_) GdiplusShutdown(gdipToken_);
}

bool Renderer::Init(HWND hwnd) {
    hwnd_ = hwnd;

    // GDI+ siempre (utilidades de medición y fallback)
    GdiplusStartupInput gsi;
    if (GdiplusStartup(&gdipToken_, &gsi, nullptr) != Ok) return false;

    // Direct2D preferente [SPEC §3.5]
    HRESULT hr = D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, &d2d_);
    if (SUCCEEDED(hr)) hr = DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), (IUnknown**)&dw_);
    if (SUCCEEDED(hr)) hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
                                            __uuidof(IWICImagingFactory), (void**)&wic_);
    if (SUCCEEDED(hr)) {
        RECT rc; GetClientRect(hwnd_, &rc);
        D2D1_SIZE_U sz = D2D1::SizeU((UINT32)(rc.right - rc.left), (UINT32)(rc.bottom - rc.top));
        D2D1_RENDER_TARGET_PROPERTIES rp = D2D1::RenderTargetProperties();
        rp.usage = D2D1_RENDER_TARGET_USAGE_GDI_COMPATIBLE;
        hr = d2d_->CreateHwndRenderTarget(D2D1::RenderTargetProperties(), 
                                          D2D1::HwndRenderTargetProperties(hwnd_, sz), &rt_);
    }
    if (SUCCEEDED(hr) && rt_) {
        usingD2D_ = true;
        Logger::Info("core.render", "Direct2D activo (HwndRenderTarget)");
        return true;
    }

    // Fallback conservador GDI+ [SPEC §6.3.3]
    Logger::Warn("core.render", "Direct2D no disponible; fallback GDI+ con doble buffer");
    if (rt_) { rt_->Release(); rt_ = nullptr; }
    usingD2D_ = false;
    return InitGdiplus();
}

bool Renderer::InitGdiplus() {
    LoadPrivateFonts();
    // Nada extra: GDI+ ya iniciado; el doble buffer se crea en RenderGdip
    return true;
}

void Renderer::Resize() {
    if (usingD2D_ && rt_) {
        RECT rc; GetClientRect(hwnd_, &rc);
        D2D1_SIZE_U sz = D2D1::SizeU((UINT32)(rc.right - rc.left ? rc.right - rc.left : 1),
                                     (UINT32)(rc.bottom - rc.top ? rc.bottom - rc.top : 1));
        rt_->Resize(sz);
    }
    // GDI+: el buffer se redimensiona en el próximo render
}

// ---------------------------------------------------------------------------
// Imágenes
// ---------------------------------------------------------------------------
ID2D1Bitmap* Renderer::LoadBitmapD2D(const std::wstring& path) {
    if (ID2D1Bitmap* b = FindCacheD2D(path)) return b;
    if (!wic_ || !rt_) return nullptr;
    IWICBitmapDecoder* dec = nullptr;
    HRESULT hr = wic_->CreateDecoderFromFilename(path.c_str(), nullptr, GENERIC_READ,
                                                 WICDecodeMetadataCacheOnDemand, &dec);
    if (FAILED(hr) || !dec) return nullptr;
    IWICBitmapFrameDecode* frame = nullptr;
    hr = dec->GetFrame(0, &frame);
    if (FAILED(hr) || !frame) { dec->Release(); return nullptr; }
    IWICFormatConverter* conv = nullptr;
    hr = wic_->CreateFormatConverter(&conv);
    if (SUCCEEDED(hr)) {
        hr = conv->Initialize(frame, GUID_WICPixelFormat32bppPBGRA, WICBitmapDitherTypeNone,
                              nullptr, 0.0, WICBitmapPaletteTypeMedianCut);
    }
    ID2D1Bitmap* bmp = nullptr;
    if (SUCCEEDED(hr)) hr = rt_->CreateBitmapFromWicBitmap(conv, nullptr, &bmp);
    if (conv) conv->Release();
    if (frame) frame->Release();
    dec->Release();
    if (bmp) cache_.push_back({path, bmp, nullptr});
    return bmp;
}

ID2D1Bitmap* Renderer::FindCacheD2D(const std::wstring& p) {
    for (auto& c : cache_) if (c.path == p && c.d2d) return c.d2d;
    return nullptr;
}

Gdiplus::Bitmap* Renderer::LoadBitmapGdip(const std::wstring& path) {
    if (Gdiplus::Bitmap* b = FindCacheGdip(path)) return b;
    Gdiplus::Bitmap* bmp = new Gdiplus::Bitmap(path.c_str());
    if (bmp->GetLastStatus() != Ok) { delete bmp; return nullptr; }
    cache_.push_back({path, nullptr, bmp});
    return bmp;
}

Gdiplus::Bitmap* Renderer::FindCacheGdip(const std::wstring& p) {
    for (auto& c : cache_) if (c.path == p && c.gdip) return c.gdip;
    return nullptr;
}

bool Renderer::LoadLogo() {
    if (logoPath_.empty()) return false;
    if (usingD2D_) { if (!logoD2D_) logoD2D_ = LoadBitmapD2D(logoPath_); return logoD2D_ != nullptr; }
    if (!logoGdip_) logoGdip_ = LoadBitmapGdip(logoPath_);
    return logoGdip_ != nullptr;
}

// ---------------------------------------------------------------------------
// Render principal
// ---------------------------------------------------------------------------
void Renderer::PreloadImage(const std::wstring& path) {
    if (path.empty()) return;
    if (usingD2D_) LoadBitmapD2D(path);
    else LoadBitmapGdip(path);
}

void Renderer::Render(const Slide& s, BlankMode blank) {
    if (!hwnd_) return;
    if (usingD2D_ && !PreferGdipFor(s)) RenderD2D(s, blank);
    else RenderGdip(s, blank);
}

// --------------------------------------------------- fuentes privadas (GUI)
void Renderer::LoadPrivateFonts() {
    if (fonts_) return;
    wchar_t exePath[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, exePath, MAX_PATH);
    std::wstring dir(exePath);
    size_t slash = dir.find_last_of(L"\\/");
    if (slash != std::wstring::npos) dir = dir.substr(0, slash);
    dir += L"\\resources\\fonts";

    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((dir + L"\\*.ttf").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return;
    fonts_ = new Gdiplus::PrivateFontCollection();
    std::vector<std::wstring> files;
    do {
        if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY))
            files.push_back(dir + L"\\" + fd.cFileName);
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    for (auto& f : files) {
        Status st = fonts_->AddFontFile(f.c_str());
        (void)st;   // una fuente dañada no frena las demás
    }
    int n = fonts_->GetFamilyCount();
    if (n > 0) {
        fontFamilies_ = new Gdiplus::FontFamily[n];
        int found = 0;
        fonts_->GetFamilies(n, fontFamilies_, &found);
        fontFamilyCount_ = found;
    }
}

bool Renderer::NeedsPrivateFont(const std::wstring& family) const {
    if (!fontFamilies_ || fontFamilyCount_ == 0) return false;
    WCHAR nm[LF_FACESIZE];
    for (int i = 0; i < fontFamilyCount_; i++) {
        fontFamilies_[i].GetFamilyName(nm);
        if (_wcsicmp(nm, family.c_str()) == 0) return true;
    }
    return false;
}

bool Renderer::PreferGdipFor(const Slide& s) const {
    if (!s.highlight.empty()) return true;
    if (s.transition == "fade" || s.transition == "slide") return true;
    if (NeedsPrivateFont(s.style.font)) return true;
    if (s.overlay.present && NeedsPrivateFont(s.overlay.style.font)) return true;
    return false;
}

Gdiplus::Font* Renderer::MakeFontGdip(const std::wstring& family, REAL size, INT style) {
    if (fontFamilies_ && fontFamilyCount_ > 0) {
        WCHAR nm[LF_FACESIZE];
        for (int i = 0; i < fontFamilyCount_; i++) {
            fontFamilies_[i].GetFamilyName(nm);
            if (_wcsicmp(nm, family.c_str()) != 0) continue;
            INT st = style;
            if (!fontFamilies_[i].IsStyleAvailable(st)) {
                if ((st & FontStyleBold) && !fontFamilies_[i].IsStyleAvailable(st & ~FontStyleBold))
                    st &= ~FontStyleBold;
                if ((st & FontStyleItalic) && !fontFamilies_[i].IsStyleAvailable(st & ~FontStyleItalic))
                    st &= ~FontStyleItalic;
            }
            return new Gdiplus::Font(&fontFamilies_[i], size, st);
        }
    }
    return new Gdiplus::Font(family.c_str(), size, style);
}

// ----------------------------------------------- mezcla de transición en pantalla
void Renderer::PresentBlendGdip(Gdiplus::Bitmap* prev, Gdiplus::Bitmap* next,
                                double a, bool slideIn, int w, int h) {
    HDC wnd = GetDC(hwnd_);
    if (!wnd) return;
    Bitmap frame(w, h, PixelFormat32bppARGB);
    Graphics g(&frame);
    g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
    ImageAttributes ia;
    ColorMatrix cmPrev = { { {1,0,0,0,0}, {0,1,0,0,0}, {0,0,1,0,0},
                             {0,0,0,(REAL)(1.0 - a),0}, {0,0,0,0,1} } };
    ColorMatrix cmNext = { { {1,0,0,0,0}, {0,1,0,0,0}, {0,0,1,0,0},
                             {0,0,0,(REAL)a,0}, {0,0,0,0,1} } };
    if (slideIn) {
        ia.SetColorMatrix(&cmPrev);
        g.DrawImage(prev, Rect(-(int)(a * w), 0, w, h), 0, 0, w, h, UnitPixel, &ia);
        ia.SetColorMatrix(&cmNext);
        g.DrawImage(next, Rect((int)((1.0 - a) * w), 0, w, h), 0, 0, w, h, UnitPixel, &ia);
    } else {
        ia.SetColorMatrix(&cmPrev);
        g.DrawImage(prev, Rect(0, 0, w, h), 0, 0, w, h, UnitPixel, &ia);
        ia.SetColorMatrix(&cmNext);
        g.DrawImage(next, Rect(0, 0, w, h), 0, 0, w, h, UnitPixel, &ia);
    }
    Graphics wg(wnd);
    wg.DrawImage(&frame, Rect(0, 0, w, h), 0, 0, w, h, UnitPixel);
    ReleaseDC(hwnd_, wnd);
}

void Renderer::FillBlack() {
    RECT rc; GetClientRect(hwnd_, &rc);
    HDC dc = GetDC(hwnd_);
    if (dc) {
        HBRUSH br = CreateSolidBrush(RGB(0, 0, 0));
        FillRect(dc, &rc, br);
        DeleteObject(br);
        ReleaseDC(hwnd_, dc);
    }
}

// --- D2D -------------------------------------------------------------------
double Renderer::FitFontSizeD2D(const Slide& s, D2D1_SIZE_F box) const {
    // Tamaño de fuente que hace caber todas las líneas en la caja (pt → DIP)
    double size = s.style.size * (96.0 / 72.0);
    if (s.lines.empty()) return size;
    double maxLineW = 0;
    for (auto& l : s.lines) {
        RECT r = {0, 0, 10000, 10000};
        HDC dc = GetDC(nullptr);
        HFONT f = CreateFontW(-(int)(size * 96.0 / 72.0), 0, 0, 0,
                              s.style.bold ? FW_BOLD : FW_NORMAL, s.style.italic, 0, 0,
                              DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                              DEFAULT_QUALITY, DEFAULT_PITCH | FF_DONTCARE, s.style.font.c_str());
        HFONT of = (HFONT)SelectObject(dc, f);
        SIZE szc;
        GetTextExtentPoint32W(dc, l.c_str(), (int)l.size(), &szc);
        double wpx = szc.cx;
        SelectObject(dc, of);
        DeleteObject(f);
        ReleaseDC(nullptr, dc);
        if (wpx > maxLineW) maxLineW = wpx;
    }
    if (maxLineW > 0 && box.width > 0) {
        double byW = size * (box.width / maxLineW);
        size = (std::min)(size, byW * 0.98);
    }
    double totalH = (double)s.lines.size() * size * s.style.lineSpacing * (96.0 / 72.0);
    if (totalH > box.height && totalH > 0) {
        double byH = size * (box.height / totalH);
        size = (std::min)(size, byH);
    }
    return (std::max)(size, 8.0);
}

void Renderer::RenderD2D(const Slide& s, BlankMode blank) {
    if (!rt_) return;
    rt_->BeginDraw();

    D2D1_SIZE_F size = rt_->GetSize();
    // Fondo (caché): SIEMPRE se pinta primero; el cambio de línea no toca esta parte [SPEC §6.2.4]
    rt_->Clear(ToD2D(s.bg.color));

    bool contentVisible = (blank == BlankMode::None);

    if (blank == BlankMode::Logo && LoadLogo() && logoD2D_) {
        // Logo centrado con letterbox negro
        D2D1_SIZE_F ls = logoD2D_->GetSize();
        float sc = (std::min)(size.width / ls.width, size.height / ls.height);
        float w = ls.width * sc, h = ls.height * sc;
        rt_->DrawBitmap(logoD2D_, D2D1::RectF((size.width - w) / 2, (size.height - h) / 2,
                                              (size.width + w) / 2, (size.height + h) / 2));
    } else if (blank == BlankMode::Theme) {
        // Solo fondo del tema
    } else if (contentVisible) {
        // Imagen de fondo
        if (!s.bg.image.empty()) {
            if (ID2D1Bitmap* b = LoadBitmapD2D(s.bg.image)) {
                D2D1_SIZE_F bs = b->GetSize();
                if (s.bg.fit == 0) { // llenar (cover)
                    float sc = (std::max)(size.width / bs.width, size.height / bs.height);
                    float w = bs.width * sc, h = bs.height * sc;
                    rt_->DrawBitmap(b, D2D1::RectF((size.width - w) / 2, (size.height - h) / 2,
                                                   (size.width + w) / 2, (size.height + h) / 2),
                                    (FLOAT)s.bg.opacity);
                } else {            // ajustar (contain)
                    float sc = (std::min)(size.width / bs.width, size.height / bs.height);
                    float w = bs.width * sc, h = bs.height * sc;
                    rt_->DrawBitmap(b, D2D1::RectF((size.width - w) / 2, (size.height - h) / 2,
                                                   (size.width + w) / 2, (size.height + h) / 2),
                                    (FLOAT)s.bg.opacity);
                }
            }
        }

        if (s.kind == SlideKind::Image && !s.media.src.empty()) {
            if (ID2D1Bitmap* b = LoadBitmapD2D(s.media.src)) {
                D2D1_SIZE_F bs = b->GetSize();
                float sc = (std::min)(size.width / bs.width, size.height / bs.height);
                float w = bs.width * sc, h = bs.height * sc;
                rt_->DrawBitmap(b, D2D1::RectF((size.width - w) / 2, (size.height - h) / 2,
                                               (size.width + w) / 2, (size.height + h) / 2));
            }
        } else if (s.kind == SlideKind::Video) {
            // El video lo pinta DirectShow en el HWND hijo; aquí solo el fondo
        } else if (s.kind == SlideKind::Text || s.kind == SlideKind::Verse || s.kind == SlideKind::Lower3) {
            DrawTextLinesD2D(s, size, false);
        }
        if (s.overlay.present) DrawLowerThirdD2D(s, size);
    }

    HRESULT hr = rt_->EndDraw();
    if (hr == (HRESULT)D2DERR_RECREATE_TARGET) {
        // Recrear target sin destruir la ventana [SPEC §6.3.1]
        rt_->Release(); rt_ = nullptr;
        for (auto& c : cache_) if (c.d2d) { c.d2d->Release(); c.d2d = nullptr; }
        if (logoD2D_) { logoD2D_->Release(); logoD2D_ = nullptr; }
        cache_.clear();
        RECT rc; GetClientRect(hwnd_, &rc);
        d2d_->CreateHwndRenderTarget(D2D1::RenderTargetProperties(),
            D2D1::HwndRenderTargetProperties(hwnd_,
                D2D1::SizeU((UINT32)(rc.right - rc.left), (UINT32)(rc.bottom - rc.top))), &rt_);
        Logger::Warn("core.render", "D2DERR_RECREATE_TARGET: destino recreado");
    }
}

void Renderer::DrawTextLinesD2D(const Slide& s, D2D1_SIZE_F size, bool stageMode) {
    if (s.lines.empty() || !dw_ || !rt_) return;

    D2D1_RECT_F box = D2D1::RectF((FLOAT)(s.style.x * size.width), (FLOAT)(s.style.y * size.height),
                                  (FLOAT)((s.style.x + s.style.w) * size.width),
                                  (FLOAT)((s.style.y + s.style.h) * size.height));
    double sizeDip = stageMode ? (std::min)(s.style.size * 1.5, 72.0) * (96.0 / 72.0)
                               : FitFontSizeD2D(s, D2D1::SizeF(box.right - box.left, box.bottom - box.top));

    IDWriteTextFormat* fmt = nullptr;
    DWRITE_TEXT_ALIGNMENT ta = s.style.align == 0 ? DWRITE_TEXT_ALIGNMENT_LEADING :
                               s.style.align == 2 ? DWRITE_TEXT_ALIGNMENT_TRAILING :
                                                    DWRITE_TEXT_ALIGNMENT_CENTER;
    DWRITE_PARAGRAPH_ALIGNMENT pa = s.style.vAlign == 0 ? DWRITE_PARAGRAPH_ALIGNMENT_NEAR
                                                        : DWRITE_PARAGRAPH_ALIGNMENT_CENTER;
    if (FAILED(dw_->CreateTextFormat(s.style.font.c_str(), nullptr,
            s.style.bold ? DWRITE_FONT_WEIGHT_BOLD : DWRITE_FONT_WEIGHT_NORMAL,
            s.style.italic ? DWRITE_FONT_STYLE_ITALIC : DWRITE_FONT_STYLE_NORMAL,
            DWRITE_FONT_STRETCH_NORMAL, (FLOAT)sizeDip, L"es-VE", &fmt))) return;
    fmt->SetTextAlignment(ta);
    fmt->SetParagraphAlignment(pa);
    fmt->SetLineSpacing(DWRITE_LINE_SPACING_METHOD_UNIFORM, (FLOAT)(sizeDip * s.style.lineSpacing), 0);

    // Distribución vertical centrada del bloque
    float lineH = (FLOAT)(sizeDip * s.style.lineSpacing);
    float totalH = lineH * (FLOAT)s.lines.size();
    float y = (FLOAT)s.style.vAlign == 0 ? box.top :
              (box.top + box.bottom) / 2 - totalH / 2;

    ID2D1SolidColorBrush* shadowBrush = nullptr;
    rt_->CreateSolidColorBrush(D2D1::ColorF(0.0f, 0.0f, 0.0f, 0.85f), &shadowBrush);
    ID2D1SolidColorBrush* textBrush = nullptr;
    rt_->CreateSolidColorBrush(ToD2D(s.style.color), &textBrush);
    ID2D1SolidColorBrush* activeBrush = nullptr;
    rt_->CreateSolidColorBrush(ToD2D(s.style.activeColor), &activeBrush);

    for (size_t i = 0; i < s.lines.size(); i++) {
        bool active = ((int)i == s.activeLine);
        D2D1_RECT_F lineRect = D2D1::RectF(box.left, y, box.right, y + lineH);
        // Sombra [SPEC §5.2 #1]
        if (s.style.shadow && shadowBrush) {
            D2D1_RECT_F sh = D2D1::RectF(lineRect.left + 2.5f, lineRect.top + 2.5f, lineRect.right + 2.5f, lineRect.bottom + 2.5f);
            rt_->DrawTextW(s.lines[i].c_str(), (UINT32)s.lines[i].size(), fmt, sh, shadowBrush);
        }
        rt_->DrawTextW(s.lines[i].c_str(), (UINT32)s.lines[i].size(), fmt, lineRect,
                       active ? (activeBrush ? activeBrush : textBrush) : textBrush);
        y += lineH;
    }
    if (shadowBrush) shadowBrush->Release();
    if (textBrush) textBrush->Release();
    if (activeBrush) activeBrush->Release();
    fmt->Release();

    // Referencia bíblica (cita) bajo el bloque [SPEC §5.2 #2]
    if (s.kind == SlideKind::Verse && !s.reference.empty()) {
        IDWriteTextFormat* rfmt = nullptr;
        if (SUCCEEDED(dw_->CreateTextFormat(s.style.font.c_str(), nullptr,
                DWRITE_FONT_WEIGHT_SEMI_BOLD, DWRITE_FONT_STYLE_ITALIC, DWRITE_FONT_STRETCH_NORMAL,
                (FLOAT)(std::max)(14.0, sizeDip * 0.42), L"es-VE", &rfmt))) {
            rfmt->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
            rfmt->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_NEAR);
            D2D1_RECT_F rr = D2D1::RectF(box.left, box.bottom - (FLOAT)(std::max)(22.0, sizeDip * 0.62),
                                          box.right, box.bottom);
            ID2D1SolidColorBrush* rb = nullptr;
            rt_->CreateSolidColorBrush(ToD2D(s.style.activeColor), &rb);
            ID2D1SolidColorBrush* rsb = nullptr;
            rt_->CreateSolidColorBrush(D2D1::ColorF(0.0f, 0.0f, 0.0f, 0.85f), &rsb);
            if (s.style.shadow && rsb) {
                D2D1_RECT_F sh = D2D1::RectF(rr.left + 2, rr.top + 2, rr.right + 2, rr.bottom + 2);
                rt_->DrawTextW(s.reference.c_str(), (UINT32)s.reference.size(), rfmt, sh, rsb);
            }
            if (rb) {
                rt_->DrawTextW(s.reference.c_str(), (UINT32)s.reference.size(), rfmt, rr, rb);
                rb->Release();
            }
            if (rsb) rsb->Release();
            rfmt->Release();
        }
    }
}

void Renderer::DrawLowerThirdD2D(const Slide& s, D2D1_SIZE_F size) {
    // Zócalo inferior semitransparente [SPEC §5.2 #5]
    float bandH = (FLOAT)(size.height * 0.16);
    float y = s.overlay.position == 1 ? 0 : (float)size.height - bandH;
    D2D1_RECT_F band = D2D1::RectF(0, y, size.width, y + bandH);
    ID2D1SolidColorBrush* bandBrush = nullptr;
    ID2D1SolidColorBrush* accentBrush = nullptr;
    rt_->CreateSolidColorBrush(D2D1::ColorF(0.0f, 0.0f, 0.0f, 0.62f), &bandBrush);
    rt_->CreateSolidColorBrush(ToD2D(s.overlay.style.activeColor), &accentBrush);
    if (bandBrush) rt_->FillRectangle(band, bandBrush);
    if (accentBrush)
        rt_->FillRectangle(D2D1::RectF(0, s.overlay.position == 1 ? y + bandH - 4 : y, size.width,
                                       s.overlay.position == 1 ? y + bandH : y + 4), accentBrush);
    if (!s.overlay.lines.empty()) {
        IDWriteTextFormat* fmt = nullptr;
        if (SUCCEEDED(dw_->CreateTextFormat(s.overlay.style.font.c_str(), nullptr,
                DWRITE_FONT_WEIGHT_SEMI_BOLD, DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL,
                (FLOAT)(bandH * 0.42f), L"es-VE", &fmt))) {
            fmt->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
            fmt->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
            D2D1_RECT_F tr = D2D1::RectF(size.width * 0.05f, y, size.width * 0.95f, y + bandH);
            std::wstring joined;
            for (size_t i = 0; i < s.overlay.lines.size(); i++) {
                if (i) joined += L"  ·  ";
                joined += s.overlay.lines[i];
            }
            ID2D1SolidColorBrush* tb = nullptr;
            rt_->CreateSolidColorBrush(ToD2D(s.overlay.style.color), &tb);
            if (tb) {
                rt_->DrawTextW(joined.c_str(), (UINT32)joined.size(), fmt, tr, tb);
                tb->Release();
            }
            if (bandBrush) bandBrush->Release();
            if (accentBrush) accentBrush->Release();
            fmt->Release();
        }
    }
}

// --- GDI+ fallback (doble buffer estricto) --------------------------------
Gdiplus::Bitmap* Renderer::ComposeBackgroundGdip(const BackgroundStyle& bg, int w, int h) {
    Bitmap* canvas = new Bitmap(w, h, PixelFormat32bppARGB);
    Graphics g(canvas);
    g.SetSmoothingMode(SmoothingModeAntiAlias);
    g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
    SolidBrush br(Color(bg.color));
    g.FillRectangle(&br, 0, 0, w, h);
    if (!bg.image.empty()) {
        if (Bitmap* img = LoadBitmapGdip(bg.image)) {
            UINT iw = img->GetWidth(), ih = img->GetHeight();
            if (iw && ih) {
                double sc = bg.fit == 0 ? (std::max)((double)w / iw, (double)h / ih)
                                        : (std::min)((double)w / iw, (double)h / ih);
                int dw = (int)(iw * sc), dh = (int)(ih * sc);
                Rect dst((w - dw) / 2, (h - dh) / 2, dw, dh);
                ColorMatrix cm = {};
                cm.m[0][0] = cm.m[1][1] = cm.m[2][2] = cm.m[4][4] = 1.0f;
                cm.m[3][3] = (REAL)bg.opacity;
                ImageAttributes ia;
                ia.SetColorMatrix(&cm);
                g.DrawImage(img, dst, 0, 0, iw, ih, UnitPixel, &ia);
            }
        }
    }
    return canvas;
}

void Renderer::RenderGdip(const Slide& s, BlankMode blank) {
    RECT rc; GetClientRect(hwnd_, &rc);
    int w = (int)(rc.right - rc.left), h = (int)(rc.bottom - rc.top);
    if (w <= 0 || h <= 0) return;

    // Composición completa fuera de pantalla → un solo BitBlt (sin parpadeo)
    Bitmap canvas(w, h, PixelFormat32bppARGB);
    Graphics g(&canvas);
    g.SetSmoothingMode(SmoothingModeAntiAlias);
    g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
    g.SetTextRenderingHint(TextRenderingHintAntiAlias);

    if (blank == BlankMode::Black) {
        SolidBrush b(Color(0xFF000000));
        g.FillRectangle(&b, 0, 0, w, h);
    } else if (blank == BlankMode::Logo) {
        SolidBrush b(Color(0xFF000000));
        g.FillRectangle(&b, 0, 0, w, h);
        if (LoadLogo() && logoGdip_) {
            UINT iw = logoGdip_->GetWidth(), ih = logoGdip_->GetHeight();
            double sc = (std::min)((double)w / iw, (double)h / ih);
            int dw = (int)(iw * sc), dh = (int)(ih * sc);
            g.DrawImage(logoGdip_, Rect((w - dw) / 2, (h - dh) / 2, dw, dh), 0, 0, iw, ih, UnitPixel);
        }
    } else if (blank == BlankMode::Theme || blank == BlankMode::Clear) {
        // Clear (tecla C): fondo visible, texto oculto — igual que la referencia
        std::unique_ptr<Bitmap> bg(ComposeBackgroundGdip(s.bg, w, h));
        g.DrawImage(bg.get(), Rect(0, 0, w, h), 0, 0, w, h, UnitPixel);
    } else {
        std::unique_ptr<Bitmap> bg(ComposeBackgroundGdip(s.bg, w, h));
        g.DrawImage(bg.get(), Rect(0, 0, w, h), 0, 0, w, h, UnitPixel);

        if (s.kind == SlideKind::Image && !s.media.src.empty()) {
            if (Bitmap* img = LoadBitmapGdip(s.media.src)) {
                UINT iw = img->GetWidth(), ih = img->GetHeight();
                if (iw && ih) {
                    double sc = (std::min)((double)w / iw, (double)h / ih);
                    int dw = (int)(iw * sc), dh = (int)(ih * sc);
                    g.DrawImage(img, Rect((w - dw) / 2, (h - dh) / 2, dw, dh), 0, 0, iw, ih, UnitPixel);
                }
            }
        } else if (s.kind == SlideKind::Text || s.kind == SlideKind::Verse || s.kind == SlideKind::Lower3) {
            DrawTextLinesGdip(g, s, w, h);
        }
        if (s.overlay.present && !s.overlay.lines.empty()) {
            int bandH = (int)(h * 0.16);
            int y = s.overlay.position == 1 ? 0 : h - bandH;
            SolidBrush band(Color(160, 0, 0, 0));
            g.FillRectangle(&band, 0, y, w, bandH);
            SolidBrush accent(s.overlay.style.activeColor);
            g.FillRectangle(&accent, 0, s.overlay.position == 1 ? y + bandH - 4 : y, w, 4);
            std::unique_ptr<Font> f(MakeFontGdip(s.overlay.style.font, (REAL)(bandH * 0.38), FontStyleBold));
            StringFormat sf; sf.SetAlignment(StringAlignmentCenter); sf.SetLineAlignment(StringAlignmentCenter);
            std::wstring joined;
            for (size_t i = 0; i < s.overlay.lines.size(); i++) { if (i) joined += L"  ·  "; joined += s.overlay.lines[i]; }
            SolidBrush tx(s.overlay.style.color);
            g.DrawString(joined.c_str(), -1, f.get(), RectF(w * 0.05f, (REAL)y, w * 0.9f, (REAL)bandH), &sf, &tx);
        }
    }

    // Transición fade/slide SOLO al cambiar de elemento (nunca por línea) [SPEC §6.2]:
    // la conmutación base sigue siendo un blit; la animación compone fuera de pantalla.
    bool slideChanged = (s.id != lastSlideId_) || (blank != lastBlank_);
    lastSlideId_ = s.id;
    lastBlank_ = blank;
    if (slideChanged && prevFrame_ &&
        (s.transition == "fade" || s.transition == "slide")) {
        const ULONGLONG t0 = GetTickCount64();
        const int durMs = 300;
        for (;;) {
            double a = (double)(GetTickCount64() - t0) / (double)durMs;
            if (a >= 1.0) break;
            PresentBlendGdip(prevFrame_.get(), &canvas, a, s.transition == "slide", w, h);
            Sleep(10);
        }
    }
    prevFrame_.reset(canvas.Clone(0, 0, w, h, PixelFormat32bppARGB));

    // Blit único a pantalla
    HDC wnd = GetDC(hwnd_);
    if (wnd) {
        HDC mem = nullptr;
        Bitmap* srcBmp = &canvas;
        Graphics wg(wnd);
        wg.DrawImage(srcBmp, Rect(0, 0, w, h), 0, 0, w, h, UnitPixel);
        (void)mem;
        ReleaseDC(hwnd_, wnd);
    }
}

// Desplazamiento acumulado de los segmentos de resaltado (medición previa)
static double segOffset(const std::vector<RectF>& rects, size_t k) {
    double x = 0;
    for (size_t i = 0; i < k && i < rects.size(); i++) x += rects[i].Width;
    return x;
}

void Renderer::DrawTextLinesGdip(Gdiplus::Graphics& g, const Slide& s, int w, int h) {
    if (s.lines.empty()) return;

    // Medición para auto-ajuste de tamaño (fuentes de la GUI incluidas)
    double probeSize = (std::max)(8.0, s.style.size);
    std::unique_ptr<Font> probe(MakeFontGdip(s.style.font, (REAL)probeSize, FontStyleRegular));
    StringFormat msf; msf.SetAlignment(StringAlignmentNear); msf.SetLineAlignment(StringAlignmentNear);
    double maxW = 0;
    for (auto& l : s.lines) {
        RectF r; PointF o(0, 0);
        g.MeasureString(l.c_str(), -1, probe.get(), o, &msf, &r);
        if (r.Width > maxW) maxW = r.Width;
    }
    RectF box((REAL)(s.style.x * w), (REAL)(s.style.y * h),
              (REAL)(s.style.w * w), (REAL)(s.style.h * h));
    double sizePt = s.style.size;
    if (maxW > 0 && maxW > box.Width) sizePt *= ((double)box.Width / maxW) * 0.98;
    double lineHpx = sizePt * s.style.lineSpacing * 96.0 / 72.0;
    double totalH = lineHpx * (double)s.lines.size();
    if (totalH > (double)box.Height && totalH > 0) sizePt *= ((double)box.Height / totalH);
    sizePt = (std::max)(sizePt, 8.0);
    lineHpx = sizePt * s.style.lineSpacing * 96.0 / 72.0;
    totalH = lineHpx * (double)s.lines.size();

    std::unique_ptr<Font> f(MakeFontGdip(s.style.font, (REAL)sizePt,
           (FontStyle)((int)(s.style.bold ? FontStyleBold : FontStyleRegular) |
                       (int)(s.style.italic ? FontStyleItalic : FontStyleRegular))));
    StringFormat fmt;
    fmt.SetAlignment(s.style.align == 0 ? StringAlignmentNear :
                     s.style.align == 2 ? StringAlignmentFar : StringAlignmentCenter);
    REAL y = (REAL)(s.style.vAlign == 0 ? box.Y : box.Y + (box.Height - (REAL)totalH) / 2);

    StringFormat nearFmt; nearFmt.SetAlignment(StringAlignmentNear);
    nearFmt.SetLineAlignment(StringAlignmentCenter);
    for (size_t i = 0; i < s.lines.size(); i++) {
        bool active = ((int)i == s.activeLine);
        Color c(active ? s.style.activeColor : s.style.color);
        RectF lineRect(box.X, y, box.Width, (REAL)lineHpx);

        if (!s.highlight.empty()) {
            // Resaltado [SPEC §5.2 #2]: segmentos verbatim; coincidencias en
            // color de acento, resto con el color de la línea (port betas 1).
            auto segs = Highlight::Split(s.lines[i], s.highlight);
            double totalW = 0;
            std::vector<RectF> segRects;
            for (auto& sg : segs) {
                RectF r; PointF o(0, 0);
                g.MeasureString(sg.text.c_str(), -1, f.get(), o, &nearFmt, &r);
                segRects.push_back(r);
                totalW += r.Width;
            }
            double x0 = (double)box.X;
            if (s.style.align == 1) x0 = (double)box.X + ((double)box.Width - totalW) / 2.0;
            else if (s.style.align == 2) x0 = (double)box.X + (double)box.Width - totalW;
            RectF lineBox((REAL)x0, y, (REAL)std::max(totalW, 1.0), (REAL)lineHpx);
            if (s.style.shadow) {
                SolidBrush shb(Color(215, 0, 0, 0));
                for (size_t k = 0; k < segs.size(); k++) {
                    RectF sr(lineBox.X + (REAL)segOffset(segRects, k) + 2.5f, lineBox.Y + 2.5f,
                             segRects[k].Width, segRects[k].Height);
                    g.DrawString(segs[k].text.c_str(), -1, f.get(), sr, &nearFmt, &shb);
                }
            }
            for (size_t k = 0; k < segs.size(); k++) {
                SolidBrush b(segs[k].match ? s.style.activeColor : c.GetValue());
                RectF sr(lineBox.X + (REAL)segOffset(segRects, k), lineBox.Y,
                         segRects[k].Width, segRects[k].Height);
                g.DrawString(segs[k].text.c_str(), -1, f.get(), sr, &nearFmt, &b);
            }
        } else {
            if (s.style.shadow) {
                SolidBrush shb(Color(215, 0, 0, 0));
                RectF shRect(lineRect.X + 2.5f, lineRect.Y + 2.5f, lineRect.Width, lineRect.Height);
                g.DrawString(s.lines[i].c_str(), -1, f.get(), shRect, &fmt, &shb);
            }
            SolidBrush b(c);
            g.DrawString(s.lines[i].c_str(), -1, f.get(), lineRect, &fmt, &b);
        }
        y += (REAL)lineHpx;
    }

    if (s.kind == SlideKind::Verse && !s.reference.empty()) {
        std::unique_ptr<Font> rf(MakeFontGdip(s.style.font, (REAL)(std::max)(12.0, sizePt * 0.42), FontStyleItalic));
        StringFormat rsf; rsf.SetAlignment(StringAlignmentCenter); rsf.SetLineAlignment(StringAlignmentCenter);
        RectF rr(box.X, box.GetBottom() - (REAL)(std::max)(24.0, sizePt * 0.66), box.Width, (REAL)(std::max)(26.0, sizePt * 0.7));
        SolidBrush rb(s.style.activeColor);
        g.DrawString(s.reference.c_str(), -1, rf.get(), rr, &rsf, &rb);
    }
}

void Renderer::RenderStage(const Slide& s) {
    // Stage View (retorno): reutiliza el pipeline con estilo ampliado
    Slide st = s;
    st.style.vAlign = 1;
    Render(st, BlankMode::None);
}

} // namespace fusion
