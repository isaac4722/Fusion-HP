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
static const wchar_t* kClassHint   = L"FusionHP.MotorHint";
static const UINT_PTR kHintTimerId = 0x4D48;   // 'MH'

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
    if (hintTimer_) { KillTimer(hwnd_, kHintTimerId); hintTimer_ = 0; }
    if (hintHwnd_) { DestroyWindow(hintHwnd_); hintHwnd_ = nullptr; }
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
            // Autónomo: el clic da foco a la salida para controlar el Motor.
            // Con GUI: la salida no roba foco del operador [SPEC §6.1.2].
            return standalone_ ? MA_ACTIVATE : MA_NOACTIVATE;
        case WM_KEYDOWN:
        case WM_SYSKEYDOWN:
            if (standalone_ && KeyHook) {
                ShowHint(false);         // el operador ya sabe que el teclado vive
                KeyHook((UINT)w);
                return 0;
            }
            break;
        case WM_CLOSE:
            // Ventana PERSISTENTE [SPEC §6.3.1]: nunca se destruye por su cuenta.
            // En modo autónomo Alt+F4 APAGA el Motor (única salida sin GUI);
            // con GUI conectada el cierre ordenado lo pide la propia GUI.
            if (standalone_) PostQuitMessage(0);
            return 0;
        case WM_TIMER:
            if (w == kHintTimerId) { ShowHint(false); return 0; }
            break;
        case WM_DESTROY:
            return 0;
        default:
            return DefWindowProcW(h, m, w, l);
    }
    return DefWindowProcW(h, m, w, l);
}

// ------------------------------------------------------------ modo autónomo
void LiveWindow::SetStandalone(bool on) {
    if (standalone_ == on) return;
    standalone_ = on;
    Logger::Info("core.live", on ? "salida en modo motor autonomo (clic = teclado)"
                                  : "salida de vuelta a modo operado por GUI");
    if (on) ShowHint(true);
    else ShowHint(false);
}

void LiveWindow::ShowHint(bool on) {
    if (!hwnd_) return;
    if (hintTimer_) { KillTimer(hwnd_, kHintTimerId); hintTimer_ = 0; }
    if (!on) {
        if (hintHwnd_) ShowWindow(hintHwnd_, SW_HIDE);
        return;
    }
    if (!hintHwnd_) {
        WNDCLASSEXW wc = {};
        wc.cbSize = sizeof(wc);
        wc.lpfnWndProc = LiveWindow::HintWndProc;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
        wc.lpszClassName = kClassHint;
        RegisterClassExW(&wc);
        // WS_POPUP con dueño (no hijo: los hijos capa no existen en Win7) →
        // coordenadas de PANTALLA sobre el monitor de la salida.
        RECT rc;
        if (!Monitors::RectOf(monitorIdx_, &rc)) GetWindowRect(hwnd_, &rc);
        hintHwnd_ = CreateWindowExW(WS_EX_LAYERED | WS_EX_TRANSPARENT, kClassHint,
                                    L"", WS_POPUP, rc.left + 24, rc.bottom - 68, 620, 44,
                                    hwnd_, nullptr, GetModuleHandleW(nullptr), nullptr);
        if (!hintHwnd_) return;
        SetLayeredWindowAttributes(hintHwnd_, 0, 216, LWA_ALPHA);
    }
    ShowWindow(hintHwnd_, SW_SHOWNOACTIVATE);
    // Se esconde solo a los 8 s: informa sin invadir la proyección.
    hintTimer_ = SetTimer(hwnd_, kHintTimerId, 8000, nullptr);
}

LRESULT CALLBACK LiveWindow::HintWndProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    if (m == WM_PAINT) {
        PAINTSTRUCT ps;
        HDC dc = BeginPaint(h, &ps);
        RECT rc;
        GetClientRect(h, &rc);
        HBRUSH bg = CreateSolidBrush(RGB(16, 16, 18));
        FillRect(dc, &rc, bg);
        DeleteObject(bg);
        HPEN pen = CreatePen(PS_SOLID, 1, RGB(196, 62, 28));
        HGDIOBJ oldPen = SelectObject(dc, pen);
        HGDIOBJ oldBrush = SelectObject(dc, GetStockObject(NULL_BRUSH));
        Rectangle(dc, rc.left, rc.top, rc.right, rc.bottom);
        SelectObject(dc, oldPen);
        SelectObject(dc, oldBrush);
        DeleteObject(pen);
        HFONT f = CreateFontW(-16, 0, 0, 0, FW_SEMIBOLD, FALSE, FALSE, FALSE,
                              DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                              CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
        HGDIOBJ oldF = SelectObject(dc, f);
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, RGB(255, 248, 236));
        RECT txt = rc;
        txt.left += 14;
        DrawTextW(dc,
                  L"MOTOR ACTIVO — Espacio/\u2190\u2192 avanza · B negro · C texto · L logo · Alt+F4 apaga",
                  -1, &txt, DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
        SelectObject(dc, oldF);
        DeleteObject(f);
        EndPaint(h, &ps);
        return 0;
    }
    return DefWindowProcW(h, m, w, l);
}

} // namespace fusion
