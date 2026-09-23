// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Projector.cpp : ventana de proyección nativa (hilo propio + WM_PAINT con
//  doble búfer) y conversiones UTF-8 ↔ UTF-16 de la capa Win32.
//  Contrato con Engine (ver LuminaCore.h):
//    Show(screenIndex, fullscreen) / Hide() / Close() / SetContent(...).
//  El contenido se publica por COPIA bajo mutex; el hilo de ventana lo lee
//  para pintar. Sin bloquear jamás al hilo que llama a la API.
// ============================================================================
#include "Projector.h"
#include "Renderer.h"
#include "Utf8.h"

#include <windows.h>
#include <objbase.h>   // macro `interface` — requerida por GdiplusImaging.h
#include <objidl.h>    // IStream
#include <gdiplus.h>

#pragma comment(lib, "msimg32.lib")   // v6.0.0: AlphaBlend (crossfade)

#include <algorithm>
#include <climits>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace lumina {

/* ---------------------------------------------- conversiones Win32 ------ */
std::wstring Utf8ToWide(const std::string& s) {
    if (s.empty()) return std::wstring();
    const int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0);
    if (n <= 0) return std::wstring();
    std::wstring w((size_t)n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), &w[0], n);
    return w;
}

std::string WideToUtf8(const std::wstring& w) {
    if (w.empty()) return std::string();
    const int n = WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(),
                                      nullptr, 0, nullptr, nullptr);
    if (n <= 0) return std::string();
    std::string s((size_t)n, '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), &s[0], n, nullptr, nullptr);
    return s;
}

/* ------------------------------------------------------------ Projector -- */
struct Projector::Impl {
    std::mutex mx;
    std::vector<Slide> slides;
    std::vector<std::string> titles;
    Theme theme;
    int current = -1;
    bool black = false;
    bool showWindow = false;
    bool quit = false;
    int screenIndex = -1;
    bool fullscreen = false;

    // v6.0.0 «HORIZONTE»: transición fundida entre slides.
    //  fadeOn/fadeMs : configuración (mode 0=corte, 1=fundido; 0..5000 ms)
    //  lastFrame     : copia del ÚLTIMO fotograma pintado (para congelar)
    //  fadeFrom      : fotograma congelado del que se funde
    //  fading/fadeStart : animación en curso (hilo de ventana invalida)
    bool fadeOn  = true;
    int  fadeMs  = 220;
    HBITMAP lastFrame = nullptr;   int lastW = 0, lastH = 0;
    HBITMAP fadeFrom  = nullptr;   int fadeW = 0,  fadeH = 0;
    bool fading = false;
    std::chrono::steady_clock::time_point fadeStart;

    std::thread worker;
    HWND hwnd = nullptr;
    ATOM cls = 0;
    // Para repintar tras SetContent (señal al hilo de ventana).
    std::condition_variable cv;
    bool dirty = false;
};

static const wchar_t* kProjClassName = L"LuminaProjWindow_v3";

Projector::Projector() : impl_(new Impl()) {}

Projector::~Projector()
{
    Close();
    if (impl_) {
        // El Close() de arriba reinicia Impl: esta pasada libera los búferes.
        if (impl_->lastFrame) { DeleteObject(impl_->lastFrame); impl_->lastFrame = nullptr; }
        if (impl_->fadeFrom)  { DeleteObject(impl_->fadeFrom);  impl_->fadeFrom  = nullptr; }
        delete impl_;
        impl_ = nullptr;
    }
}

void Projector::SetContent(const std::vector<Slide>& slides,
                           const std::vector<std::string>& itemTitles,
                           const Theme& theme, int current, bool black) {
    Impl* p = impl_;
    {
        std::lock_guard<std::mutex> lk(p->mx);
        p->slides = slides;
        p->titles = itemTitles;
        p->theme = theme;
        p->current = current;
        p->black = black;
        // v6.0.0: congelar el último fotograma y arrancar el fundido hacia el
        // contenido NUEVO (solo si ya hay fotograma previo con el mismo tamaño).
        if (p->hwnd && p->fadeOn && p->fadeMs > 0 && p->lastFrame && p->lastW > 0 &&
            p->fadeW == p->lastW && p->fadeH == p->lastH) {
            if (p->fadeFrom) { DeleteObject(p->fadeFrom); p->fadeFrom = nullptr; }
            HDC win = GetDC(p->hwnd);
            if (win) {
                HDC src = CreateCompatibleDC(win);
                HDC dst = CreateCompatibleDC(win);
                HGDIOBJ os = SelectObject(src, p->lastFrame);
                HGDIOBJ od = SelectObject(dst, p->fadeFrom =
                    CreateCompatibleBitmap(win, p->lastW, p->lastH));
                BitBlt(dst, 0, 0, p->lastW, p->lastH, src, 0, 0, SRCCOPY);
                SelectObject(src, os);
                SelectObject(dst, od);
                DeleteDC(src);
                DeleteDC(dst);
                ReleaseDC(p->hwnd, win);
            }
            if (p->fadeFrom) {
                p->fadeW = p->lastW;
                p->fadeH = p->lastH;
                p->fading = true;
                p->fadeStart = std::chrono::steady_clock::now();
            }
        } else {
            p->fading = false;   // corte: sin fotograma previo o modo desactivado
        }
    }
    if (p->hwnd) InvalidateRect(p->hwnd, nullptr, FALSE);
}

void Projector::SetTransition(int mode, int durationMs) {
    Impl* p = impl_;
    std::lock_guard<std::mutex> lk(p->mx);
    p->fadeOn = (mode != 0);
    p->fadeMs = std::max(0, std::min(5000, durationMs));
    if (!p->fadeOn || p->fadeMs == 0) p->fading = false;
}

bool Projector::Show(int screenIndex, bool fullscreen) {
    Impl* p = impl_;
    {
        std::lock_guard<std::mutex> lk(p->mx);
        p->screenIndex = screenIndex;
        p->fullscreen = fullscreen;
        p->showWindow = true;
        p->quit = false;
    }
    if (!p->worker.joinable()) {
        p->worker = std::thread([this] { WindowLoop(); });
    }
    return true;
}

void Projector::Hide() {
    Impl* p = impl_;
    {
        std::lock_guard<std::mutex> lk(p->mx);
        p->showWindow = false;
    }
    if (p->hwnd) PostMessageW(p->hwnd, WM_CLOSE, 0, 0);
}

void Projector::Close() {
    Impl* p = impl_;
    {
        std::lock_guard<std::mutex> lk(p->mx);
        p->quit = true;
        p->showWindow = false;
    }
    if (p->hwnd) PostMessageW(p->hwnd, WM_CLOSE, 0, 0);
    if (p->worker.joinable()) p->worker.join();
    // Los búferes de fundido se liberan aquí (el tamaño del lienzo puede
    // cambiar al reabrir): se recrean bajo demanda en SetContent/PaintInto.
    if (p->fadeFrom)  { DeleteObject(p->fadeFrom);  p->fadeFrom  = nullptr; }
    if (p->lastFrame) { DeleteObject(p->lastFrame); p->lastFrame = nullptr; }
    p->fading = false;
    delete impl_;
    impl_ = new Impl();
}

/* ------------------------------------------------ hilo de ventana ------- */
namespace {

struct MonitorCtx {
    int index = 0;      // ordinal pedido
    int seen = 0;       // ordinales visitados
    RECT rect = {0, 0, 0, 0};
    bool found = false;
};

BOOL CALLBACK MonitorEnumProc(HMONITOR, HDC, LPRECT rc, LPARAM lp) {
    MonitorCtx* ctx = reinterpret_cast<MonitorCtx*>(lp);
    if (ctx->seen == ctx->index) {
        ctx->rect = *rc;
        ctx->found = true;
    }
    ++ctx->seen;
    return TRUE;
}

// Pinta el contenido actual en hdc (w x h px). Llamado bajo p->mx.

} // namespace

void Projector::PaintInto(HDC hdc, int w, int h) {
    Impl* p = impl_;
    std::lock_guard<std::mutex> lk(p->mx);
    const Slide* slide = nullptr;
    if (p->current >= 0 && p->current < (int)p->slides.size())
        slide = &p->slides[(size_t)p->current];
    Renderer::DrawSlide(hdc, w, h, slide, p->theme, p->black, false, p->current + 1);

    // v6.0.0 «HORIZONTE»: fundido (crossfade) — sobre el contenido NUEVO se
    // mezcla el fotograma CONGELADO con alpha decreciente (t=0 → viejo opaco;
    // t=1 → nuevo visible). AlphaBlend es constante-α (AC_SRC_OVER sin canal
    // α por píxel): exactamente un crossfade de dos superficies opacas.
    bool stillFading = false;
    if (p->fading && p->fadeFrom && p->fadeW == w && p->fadeH == h) {
        const std::chrono::steady_clock::duration el =
            std::chrono::steady_clock::now() - p->fadeStart;
        const long long us =
            std::chrono::duration_cast<std::chrono::microseconds>(el).count();
        const double t = p->fadeMs > 0
            ? std::min(1.0, (double)us / (p->fadeMs * 1000.0)) : 1.0;
        if (t >= 1.0) {
            p->fading = false;                       // fundido completado
        } else {
            const BYTE alpha = (BYTE)std::lround((1.0 - t) * 255.0);
            if (alpha > 0) {
                HDC from = CreateCompatibleDC(hdc);
                HGDIOBJ of = SelectObject(from, p->fadeFrom);
                BLENDFUNCTION bf;
                bf.BlendOp    = AC_SRC_OVER;
                bf.BlendFlags = 0;
                bf.SourceConstantAlpha = alpha;
                bf.AlphaFormat = 0;                  // superficie opaca
                AlphaBlend(hdc, 0, 0, w, h, from, 0, 0, w, h, bf);
                SelectObject(from, of);
                DeleteDC(from);
                stillFading = true;
            } else {
                p->fading = false;
            }
        }
    }

    // Cache del ÚLTIMO fotograma presentado (fuente del próximo fundido). El
    // tamaño cambia (fullscreen/ventana) → se recrea; los 16..33 MB extra en
    // 1080p son el costo de la transición (documentado en la decisión).
    if (p->fadeOn && p->fadeMs > 0) {
        if (!p->lastFrame || p->lastW != w || p->lastH != h) {
            if (p->lastFrame) { DeleteObject(p->lastFrame); p->lastFrame = nullptr; }
            p->lastFrame = CreateCompatibleBitmap(hdc, w, h);
            p->lastW = w;
            p->lastH = h;
        }
        if (p->lastFrame) {
            HDC cache = CreateCompatibleDC(hdc);
            HGDIOBJ oc = SelectObject(cache, p->lastFrame);
            BitBlt(cache, 0, 0, w, h, hdc, 0, 0, SRCCOPY);
            SelectObject(cache, oc);
            DeleteDC(cache);
        }
    }
    (void)stillFading;   // el progreso lo impulsa WindowLoop (invalidaciones)
}

void Projector::WindowLoop() {
    Impl* p = impl_;

    // Registro de clase (una vez por proceso).
    static const ATOM cls = [] {
        WNDCLASSW wc = {0};
        wc.lpfnWndProc = &Projector::WndProcThunk;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.hCursor = LoadCursorW(nullptr, reinterpret_cast<LPCWSTR>(IDC_ARROW));
        wc.hbrBackground = nullptr;
        wc.lpszClassName = kProjClassName;
        return RegisterClassW(&wc);
    }();
    (void)cls;

    int lastScreen = INT_MIN;
    bool lastFullscreen = false;

    MSG msg;
    for (;;) {
        // Estado (bajo lock, copia barata de flags).
        bool quit, show;
        int screen;
        bool fs;
        {
            std::lock_guard<std::mutex> lk(p->mx);
            quit = p->quit;
            show = p->showWindow;
            screen = p->screenIndex;
            fs = p->fullscreen;
        }
        if (quit) break;

        // (Re)creación de ventana si hace falta.
        if (show && (!p->hwnd || screen != lastScreen || fs != lastFullscreen)) {
            if (p->hwnd) {
                DestroyWindow(p->hwnd);
                p->hwnd = nullptr;
            }
            // v6.0.0: el lienzo cambia de tamaño → el fotograma congelado y el
            // cached quedan inválidos (el fundido con tamaño mixto rompería).
            {
                std::lock_guard<std::mutex> lk(p->mx);
                p->fading = false;
                if (p->fadeFrom)  { DeleteObject(p->fadeFrom);  p->fadeFrom  = nullptr; }
                if (p->lastFrame) { DeleteObject(p->lastFrame); p->lastFrame = nullptr; }
                p->lastW = p->lastH = 0;
            }
            RECT rc = {0, 0, 960, 540};
            if (fs) {
                MonitorCtx ctx;
                ctx.index = screen > 0 ? screen : 0;
                EnumDisplayMonitors(nullptr, nullptr, MonitorEnumProc, (LPARAM)&ctx);
                if (ctx.found) rc = ctx.rect;
                else { rc.left = 0; rc.top = 0; rc.right = GetSystemMetrics(SM_CXSCREEN); rc.bottom = GetSystemMetrics(SM_CYSCREEN); }
                p->hwnd = CreateWindowExW(WS_EX_TOPMOST, kProjClassName, L"LuminaPresentation",
                                          WS_POPUP, rc.left, rc.top,
                                          rc.right - rc.left, rc.bottom - rc.top,
                                          nullptr, nullptr, GetModuleHandleW(nullptr), this);
            } else {
                p->hwnd = CreateWindowExW(0, kProjClassName, L"LuminaPresentation (prueba)",
                                          WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT,
                                          960, 540, nullptr, nullptr,
                                          GetModuleHandleW(nullptr), this);
            }
            if (p->hwnd) {
                SetWindowLongPtrW(p->hwnd, GWLP_USERDATA, (LONG_PTR)this);
                lastScreen = screen;
                lastFullscreen = fs;
                ShowWindow(p->hwnd, SW_SHOW);
                InvalidateRect(p->hwnd, nullptr, FALSE);
            }
        } else if (!show && p->hwnd) {
            DestroyWindow(p->hwnd);
            p->hwnd = nullptr;
        }

        // Bombeo de mensajes no bloqueante + pausa corta (16 ms ≈ 60 fps).
        while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
            if (msg.message == WM_QUIT) { p->quit = true; break; }
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        // v6.0.0: mientras corre el fundido se invalida el lienzo en cada paso
        // del bucle (~60 fps) — la animación avanza en PaintInto con el reloj.
        {
            std::lock_guard<std::mutex> lk(p->mx);
            if (p->fading && p->hwnd) InvalidateRect(p->hwnd, nullptr, FALSE);
        }
        Sleep(16);
    }
    if (p->hwnd) { DestroyWindow(p->hwnd); p->hwnd = nullptr; }
}

LRESULT CALLBACK Projector::WndProcThunk(HWND hwnd, UINT m, WPARAM wp, LPARAM lp) {
    Projector* self = reinterpret_cast<Projector*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    switch (m) {
        case WM_PAINT: {
            PAINTSTRUCT ps;
            HDC hdc = BeginPaint(hwnd, &ps);
            RECT rc = {0, 0, 0, 0};
            GetClientRect(hwnd, &rc);
            const int w = rc.right - rc.left, h = rc.bottom - rc.top;
            if (w > 0 && h > 0 && self) {
                // Doble búfer: memoria DC + BitBlt (sin parpadeo).
                HDC mem = CreateCompatibleDC(hdc);
                HBITMAP bmp = CreateCompatibleBitmap(hdc, w, h);
                HGDIOBJ old = SelectObject(mem, bmp);
                self->PaintInto(mem, w, h);
                BitBlt(hdc, 0, 0, w, h, mem, 0, 0, SRCCOPY);
                SelectObject(mem, old);
                DeleteObject(bmp);
                DeleteDC(mem);
            } else if (hdc) {
                RECT r2 = {0, 0, w, h};
                HBRUSH b = CreateSolidBrush(RGB(0, 0, 0));
                FillRect(hdc, &r2, b);
                DeleteObject(b);
            }
            EndPaint(hwnd, &ps);
            return 0;
        }
        case WM_ERASEBKGND:
            return 1;                       // evita parpadeo (pinta WM_PAINT)
        case WM_KEYDOWN:
            if (wp == VK_ESCAPE) {
                // ESC cierra la ventana de proyección (igual que la edición wx).
                if (self) {
                    std::lock_guard<std::mutex> lk(self->impl_->mx);
                    self->impl_->showWindow = false;
                }
                DestroyWindow(hwnd);
                return 0;
            }
            break;
        default:
            break;
    }
    if (m == WM_CLOSE || m == WM_DESTROY) {
        if (self) self->impl_->hwnd = nullptr;
        if (m == WM_DESTROY) PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hwnd, m, wp, lp);
}

/* ------------------------------------------------------ PNG de preview -- */

bool Projector::RenderSlidePng(const Slide& s, const Theme& t,
                               int w, int h, std::string* pngOut) {
    if (!pngOut || w <= 0 || h > (1 << 14) || w > (1 << 14) || h <= 0) return false;
    HDC screen = GetDC(nullptr);
    if (!screen) return false;
    HDC mem = CreateCompatibleDC(screen);
    HBITMAP bmp = CreateCompatibleBitmap(screen, w, h);
    HGDIOBJ old = SelectObject(mem, bmp);
    const Slide* ps = &s;
    Renderer::DrawSlide(mem, w, h, ps, t, false, false, 0);
    const bool ok = Renderer::EncodePng(bmp, pngOut);
    SelectObject(mem, old);
    DeleteObject(bmp);
    DeleteDC(mem);
    ReleaseDC(nullptr, screen);
    if (ok && pngOut->size() > 8) {
        // Firma PNG: 89 50 4E 47 (comparar sin cast que truncue — C4310)
        static const unsigned char kPngSig[4] = { 0x89, 0x50, 0x4E, 0x47 };
        const unsigned char* b = reinterpret_cast<const unsigned char*>(pngOut->data());
        if (b[0] == kPngSig[0] && b[1] == kPngSig[1] && b[2] == kPngSig[2] && b[3] == kPngSig[3])
            return true;
    }
    return false;
}

} // namespace lumina

