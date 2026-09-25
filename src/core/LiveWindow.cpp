// ============================================================================
//  Fusion-HP · LiveWindow.cpp
// ============================================================================
#include "LiveWindow.h"
#include "Logger.h"
#include "Monitors.h"
#include "VideoPlayer.h"

namespace fusion {

static const wchar_t* kClassPublic = L"FusionHP.LiveWindow";
static const wchar_t* kClassStage  = L"FusionHP.StageWindow";

LiveWindow::LiveWindow() {}
LiveWindow::~LiveWindow() { Destroy(); }

static void RegisterOnce(const wchar_t* cls, bool layeredCursorHide) {
    WNDCLASSEXW wc = {};
    wc.cbSize = sizeof(wc);
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = LiveWindow::WndProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.hCursor = nullptr;                       // sin cursor [SPEC §6.1]
    wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
    wc.lpszClassName = cls;
    RegisterClassExW(&wc);
    (void)layeredCursorHide;
}

bool LiveWindow::Create(int monitorIndex, const std::wstring& title) {
    RegisterOnce(kClassPublic, true);

    RECT rc;
    if (!Monitors::RectOf(monitorIndex, &rc)) {
        Monitors::RectOf(0, &rc);
        monitorIndex = 0;
    }
    monitorIdx_ = monitorIndex;

    hwnd_ = CreateWindowExW(
        0, kClassPublic, title.c_str(),
        WS_POPUP,                                // borderless fullscreen [SPEC §6.1.1]
        rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top,
        nullptr, nullptr, GetModuleHandleW(nullptr), this);
    if (!hwnd_) return false;

    // HWND hijo para video DirectShow (windowless VMR9 clipia aquí)
    videoHwnd_ = CreateWindowExW(0, L"STATIC", L"", WS_CHILD,
                                 0, 0, rc.right - rc.left, rc.bottom - rc.top,
                                 hwnd_, nullptr, GetModuleHandleW(nullptr), nullptr);

    if (!renderer_.Init(hwnd_)) {
        Logger::Error("core.live", "fallo al inicializar el renderer de la salida publica");
        return false;
    }
    ShowWindow(hwnd_, SW_SHOWNOACTIVATE);
    UpdateWindow(hwnd_);
    Logger::Info("core.live", "ventana de salida creada en monitor " + std::to_string(monitorIndex));
    return true;
}

bool LiveWindow::CreateStage(int monitorIndex) {
    RegisterOnce(kClassStage, true);
    RECT rc;
    if (!Monitors::RectOf(monitorIndex, &rc)) return false;
    stageHwnd_ = CreateWindowExW(0, kClassStage, L"FusionHP Stage",
                                 WS_POPUP, rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top,
                                 nullptr, nullptr, GetModuleHandleW(nullptr), this);
    if (!stageHwnd_) return false;
    if (!stageRenderer_.Init(stageHwnd_)) return false;
    ShowWindow(stageHwnd_, SW_SHOWNOACTIVATE);
    Logger::Info("core.live", "ventana de retorno (stage) creada en monitor " + std::to_string(monitorIndex));
    return true;
}

void LiveWindow::ShowOnMonitor(int monitorIndex) {
    RECT rc;
    if (!Monitors::RectOf(monitorIndex, &rc)) return;
    monitorIdx_ = monitorIndex;
    SetWindowPos(hwnd_, nullptr, rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top,
                 SWP_NOZORDER | SWP_NOACTIVATE);
    renderer_.Resize();
    if (videoHwnd_) {
        SetWindowPos(videoHwnd_, nullptr, 0, 0, rc.right - rc.left, rc.bottom - rc.top,
                     SWP_NOZORDER | SWP_NOACTIVATE);
    }
    RenderNow();
}

void LiveWindow::Destroy() {
    if (stageHwnd_) { DestroyWindow(stageHwnd_); stageHwnd_ = nullptr; }
    if (videoHwnd_) { DestroyWindow(videoHwnd_); videoHwnd_ = nullptr; }
    if (hwnd_) { DestroyWindow(hwnd_); hwnd_ = nullptr; }
}

void LiveWindow::SetVideoChildVisible(bool v) {
    if (videoHwnd_) ShowWindow(videoHwnd_, v ? SW_SHOW : SW_HIDE);
}

void LiveWindow::RenderNow() {
    if (!hwnd_ || !state_) return;
    Slide s = state_->Get();
    BlankMode b = state_->Blank();
    // Con video activo el fondo lo pinta DirectShow en el hijo; el 2D se queda detrás
    bool videoActive = (s.kind == SlideKind::Video && video_ && video_->IsPlaying());
    SetVideoChildVisible(videoActive);
    renderer_.Render(s, videoActive ? BlankMode::Black : b);
}

void LiveWindow::RenderStageNow() {
    if (!stageHwnd_ || !state_) return;
    Slide s = state_->Get();
    if (state_->Blank() != BlankMode::None) {
        stageRenderer_.Render(s, BlankMode::Black);
        return;
    }
    stageRenderer_.RenderStage(s);
}

LRESULT CALLBACK LiveWindow::WndProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    LiveWindow* self = nullptr;
    if (m == WM_NCCREATE) {
        auto cs = (CREATESTRUCTW*)l;
        SetWindowLongPtrW(h, GWLP_USERDATA, (LONG_PTR)cs->lpCreateParams);
        self = (LiveWindow*)cs->lpCreateParams;
    } else {
        self = (LiveWindow*)GetWindowLongPtrW(h, GWLP_USERDATA);
    }
    if (self) return self->Handle(h, m, w, l);
    return DefWindowProcW(h, m, w, l);
}

LRESULT LiveWindow::Handle(HWND h, UINT m, WPARAM w, LPARAM l) {
    switch (m) {
        case WM_ERASEBKGND:
            return 1;                    // sin borrado intermedio: cero parpadeo [SPEC §6.3]
        case WM_PAINT: {
            PAINTSTRUCT ps;
            BeginPaint(h, &ps);
            if (h == hwnd_) RenderNow();
            else if (h == stageHwnd_) RenderStageNow();
            EndPaint(h, &ps);
            return 0;
        }
        case WM_SIZE:
            if (h == hwnd_) { renderer_.Resize(); RenderNow(); }
            else if (h == stageHwnd_) { stageRenderer_.Resize(); RenderStageNow(); }
            return 0;
        case WM_SETCURSOR:
            SetCursor(nullptr);          // cursor invisible sobre la salida [SPEC §6.1]
            return TRUE;
        case WM_MOUSEACTIVATE:
            return MA_NOACTIVATE;        // la salida no roba foco del operador [SPEC §6.1.2]
        case WM_DESTROY:
            return 0;
        default:
            return DefWindowProcW(h, m, w, l);
    }
}

} // namespace fusion
