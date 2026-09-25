// ============================================================================
//  Fusion-HP · NativeControl.cpp
// ============================================================================
#include "NativeControl.h"
#include "LiveWindow.h"
#include "VideoPlayer.h"
#include "Logger.h"
#include <commctrl.h>
#include <commdlg.h>
#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "comdlg32.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")

namespace fusion {

static const wchar_t* kClass = L"FusionHP.NativeControl";

NativeControl::NativeControl(SlideState* state, LiveWindow* live, VideoPlayer* video)
    : state_(state), live_(live), video_(video) {}
NativeControl::~NativeControl() { if (hwnd_) DestroyWindow(hwnd_); }

bool NativeControl::Create() {
    WNDCLASSEXW wc = {};
    wc.cbSize = sizeof(wc);
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = NativeControl::WndProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
    wc.lpszClassName = kClass;
    RegisterClassExW(&wc);

    hwnd_ = CreateWindowExW(0, kClass, L"Fusion HP — Control de emergencia (perfil C)",
                            WS_OVERLAPPEDWINDOW & ~WS_MAXIMIZEBOX,
                            CW_USEDEFAULT, CW_USEDEFAULT, 560, 480,
                            nullptr, nullptr, GetModuleHandleW(nullptr), this);
    if (!hwnd_) return false;
    BuildUi();
    ShowWindow(hwnd_, SW_SHOW);
    UpdateWindow(hwnd_);
    Logger::Info("core.native", "ventana de control nativo (perfil C) creada");
    return true;
}

void NativeControl::BuildUi() {
    CreateWindowExW(0, L"BUTTON", L"Abrir escenario (.ahp)...",
                    WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
                    10, 10, 190, 30, hwnd_, (HMENU)100, nullptr, nullptr);
    CreateWindowExW(0, L"BUTTON", L"Negro",
                    WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
                    210, 10, 80, 30, hwnd_, (HMENU)101, nullptr, nullptr);
    CreateWindowExW(0, L"BUTTON", L"Logo",
                    WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
                    295, 10, 80, 30, hwnd_, (HMENU)105, nullptr, nullptr);
    CreateWindowExW(0, L"BUTTON", L"Mostrar",
                    WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
                    380, 10, 80, 30, hwnd_, (HMENU)102, nullptr, nullptr);

    label_ = CreateWindowExW(0, L"STATIC", L"Sin sesion cargada",
                             WS_CHILD | WS_VISIBLE | SS_CENTER,
                             10, 48, 530, 24, hwnd_, nullptr, nullptr, nullptr);

    list_ = CreateWindowExW(0, L"LISTBOX", nullptr,
                            WS_CHILD | WS_VISIBLE | WS_VSCROLL | LBS_NOTIFY | LBS_HASSTRINGS,
                            10, 78, 530, 260, hwnd_, (HMENU)103, nullptr, nullptr);

    CreateWindowExW(0, L"BUTTON", L"< Elemento",
                    WS_CHILD | WS_VISIBLE, 10, 346, 90, 30, hwnd_, (HMENU)104, nullptr, nullptr);
    CreateWindowExW(0, L"BUTTON", L"Elemento >",
                    WS_CHILD | WS_VISIBLE, 106, 346, 90, 30, hwnd_, (HMENU)110, nullptr, nullptr);
    CreateWindowExW(0, L"BUTTON", L"Linea arriba",
                    WS_CHILD | WS_VISIBLE, 202, 346, 105, 30, hwnd_, (HMENU)106, nullptr, nullptr);
    CreateWindowExW(0, L"BUTTON", L"Linea abajo",
                    WS_CHILD | WS_VISIBLE, 312, 346, 105, 30, hwnd_, (HMENU)107, nullptr, nullptr);
    CreateWindowExW(0, L"STATIC",
                    L"Teclas: flechas navegar · Espacio siguiente linea · Esc negro · Enter mostrar",
                    WS_CHILD | WS_VISIBLE, 10, 384, 530, 40, hwnd_, nullptr, nullptr, nullptr);

    HFONT f = CreateFontW(16, 0, 0, 0, FW_NORMAL, 0, 0, 0, DEFAULT_CHARSET,
                          0, 0, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
    SendMessageW(list_, WM_SETFONT, (WPARAM)f, TRUE);
    SendMessageW(label_, WM_SETFONT, (WPARAM)f, TRUE);
    EnumChildWindows(hwnd_, [](HWND h, LPARAM lp) -> BOOL {
        SendMessageW(h, WM_SETFONT, (WPARAM)lp, TRUE);
        return TRUE;
    }, (LPARAM)f);
}

void NativeControl::ShowSession(const NativeSession& session) {
    items_ = session.Items();
    curItem_ = -1;
    curSlide_ = -1;
    std::wstring txt = L"Sesion: " + session.ProjectName() + L" — " +
                       std::to_wstring(items_.size()) + L" escenarios";
    SetWindowTextW(label_, txt.c_str());
    RefreshList();
}

void NativeControl::RefreshList() {
    SendMessageW(list_, LB_RESETCONTENT, 0, 0);
    for (size_t i = 0; i < items_.size(); i++) {
        std::wstring t = std::to_wstring(i + 1) + L". " + items_[i].title +
                         L"  [" + std::to_wstring(items_[i].slides.size()) + L"]";
        SendMessageW(list_, LB_ADDSTRING, 0, (LPARAM)t.c_str());
    }
}

void NativeControl::SelectItem(int idx) {
    if (idx < 0 || idx >= (int)items_.size()) return;
    curItem_ = idx;
    curSlide_ = items_[idx].slides.empty() ? -1 : 0;
    SendMessageW(list_, LB_SETCURSEL, idx, 0);
    ShowSlideOf(curSlide_);
}

void NativeControl::ShowSlideOf(int slideIdx) {
    if (curItem_ < 0) return;
    auto& slides = items_[(size_t)curItem_].slides;
    if (slideIdx < 0 || slideIdx >= (int)slides.size()) return;
    curSlide_ = slideIdx;
    Slide s = slides[(size_t)slideIdx];

    // Video por DirectShow con fail-safe [SPEC §6.5.4]
    if (s.kind == SlideKind::Video) {
        HWND vh = live_ ? live_->VideoHwnd() : nullptr;
        if (!video_->Play(s.media.src, vh, s.media.loop, s.media.volume, s.media.startAt)) {
            s.kind = SlideKind::Text;                    // fondo del tema + aviso
            s.lines = {L"(El video no se pudo reproducir)",
                       s.media.src};
            s.style.color = 0xFFE0E0E0;
            s.style.activeColor = 0xFFFFB0B0;
        }
    } else if (video_->IsPlaying()) {
        video_->Stop();
    }
    state_->Set(s);
    state_->SetBlank(BlankMode::None);
    if (live_) live_->RenderNow();
}

void NativeControl::NextLine(int dir) {
    Slide s = state_->Get();
    int n = s.activeLine + dir;
    if (n >= 0 && n < (int)s.lines.size()) {
        state_->SetActiveLine(n);
        if (live_) live_->RenderNow();
    }
}

void NativeControl::SetBlank(BlankMode b) {
    if (b == BlankMode::None && video_ && video_->IsPlaying()) video_->Resume();
    state_->SetBlank(b);
    if (live_) live_->RenderNow();
}

void NativeControl::OpenAhp() {
    wchar_t file[MAX_PATH] = L"";
    OPENFILENAMEW ofn = {};
    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = hwnd_;
    ofn.lpstrFilter = L"Proyecto Fusion-HP (*.ahp;*.json)\0*.ahp;*.json\0Todos (*.*)\0*.*\0";
    ofn.lpstrFile = file;
    ofn.nMaxFile = MAX_PATH;
    ofn.Flags = OFN_FILEMUSTEXIST | OFN_HIDEREADONLY;
    if (GetOpenFileNameW(&ofn)) {
        NativeSession session;
        if (session.Load(file)) ShowSession(session);
        else MessageBoxW(hwnd_, (L"No se pudo abrir el escenario.\n\nDetalle: " +
                                ToWide(session.LastError())).c_str(),
                         L"Fusion HP", MB_ICONINFORMATION);
    }
}

LRESULT CALLBACK NativeControl::WndProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    NativeControl* self = nullptr;
    if (m == WM_NCCREATE) {
        auto cs = (CREATESTRUCTW*)l;
        SetWindowLongPtrW(h, GWLP_USERDATA, (LONG_PTR)cs->lpCreateParams);
        self = (NativeControl*)cs->lpCreateParams;
    } else self = (NativeControl*)GetWindowLongPtrW(h, GWLP_USERDATA);
    if (self) return self->Handle(m, w, l);
    return DefWindowProcW(h, m, w, l);
}

LRESULT NativeControl::Handle(UINT m, WPARAM w, LPARAM l) {
    switch (m) {
        case WM_COMMAND: {
            int id = LOWORD(w);
            if (id == 100) OpenAhp();
            else if (id == 101) SetBlank(BlankMode::Black);
            else if (id == 102) SetBlank(BlankMode::None);
            else if (id == 105) SetBlank(BlankMode::Logo);
            else if (id == 104) { // elemento anterior
                if (curItem_ > 0) SelectItem(curItem_ - 1);
            } else if (id == 110) {
                if (curItem_ < (int)items_.size() - 1) SelectItem(curItem_ + 1);
            } else if (id == 106) NextLine(-1);
            else if (id == 107) NextLine(1);
            else if (id == 103 && HIWORD(w) == LBN_SELCHANGE) {
                int sel = (int)SendMessageW(list_, LB_GETCURSEL, 0, 0);
                if (sel != LB_ERR) SelectItem(sel);
            }
            return 0;
        }
        case WM_KEYDOWN:
            switch (w) {
                case VK_ESCAPE:   SetBlank(BlankMode::Black); return 0;
                case VK_SPACE:
                case VK_RIGHT:    NextLine(1); return 0;
                case VK_LEFT:     NextLine(-1); return 0;
                case VK_DOWN:     if (curItem_ < (int)items_.size() - 1) SelectItem(curItem_ + 1); return 0;
                case VK_UP:       if (curItem_ > 0) SelectItem(curItem_ - 1); return 0;
                case VK_RETURN:   SetBlank(BlankMode::None); return 0;
            }
            return 0;
        case WM_DESTROY:
            return 0;
        default:
            return DefWindowProcW(hwnd_, m, w, l);
    }
}

} // namespace fusion
