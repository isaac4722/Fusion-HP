// ============================================================================
//  Fusion-HP / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Projector.cpp : ventana de proyección nativa (hilo propio + WM_PAINT con
//  doble búfer) y conversiones UTF-8 ↔ UTF-16 de la capa Win32.
//  Contrato con Engine (ver FusionCore.h):
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

#include <algorithm>
#include <climits>
#include <chrono>
#include <condition_variable>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace fusion {

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

    std::thread worker;
    HWND hwnd = nullptr;
    ATOM cls = 0;
    // Para repintar tras SetContent (señal al hilo de ventana).
    std::condition_variable cv;
    bool dirty = false;
};

static const wchar_t* kProjClassName = L"FusionProjWindow_v3";

Projector::Projector() : impl_(new Impl()) {}

Projector::~Projector() { Close(); }

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
    }
    if (p->hwnd) InvalidateRect(p->hwnd, nullptr, FALSE);
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
            RECT rc = {0, 0, 960, 540};
            if (fs) {
                MonitorCtx ctx;
                ctx.index = screen > 0 ? screen : 0;
                EnumDisplayMonitors(nullptr, nullptr, MonitorEnumProc, (LPARAM)&ctx);
                if (ctx.found) rc = ctx.rect;
                else { rc.left = 0; rc.top = 0; rc.right = GetSystemMetrics(SM_CXSCREEN); rc.bottom = GetSystemMetrics(SM_CYSCREEN); }
                p->hwnd = CreateWindowExW(WS_EX_TOPMOST, kProjClassName, L"Fusion-HP",
                                          WS_POPUP, rc.left, rc.top,
                                          rc.right - rc.left, rc.bottom - rc.top,
                                          nullptr, nullptr, GetModuleHandleW(nullptr), this);
            } else {
                p->hwnd = CreateWindowExW(0, kProjClassName, L"Fusion-HP (prueba)",
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
    if (ok && pngOut->size() > 8 &&
        (*pngOut)[0] == (char)0x89 && (*pngOut)[1] == 'P' && (*pngOut)[2] == 'N' && (*pngOut)[3] == 'G')
        return true;
    return false;
}

} // namespace fusion

