// ============================================================================
//  Fusion-HP · NativeStudio.cpp — núcleo de la ventana + Inicio + modales
//  (el editor está en NativeStudio.Editor.cpp y la consola en .Present.cpp)
// ============================================================================
#include "NativeStudio.h"
#include "NativeSession.h"
#include "LiveWindow.h"
#include "VideoPlayer.h"
#include "Motor.h"
#include "SlideState.h"
#include "Logger.h"
#include "Monitors.h"
#include <commdlg.h>
#include <shlobj.h>
#include <windowsx.h>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <chrono>
#include <ctime>

namespace fs = std::filesystem;

namespace fusion {

// ------------------------------------------------------------------ comandos
// Las listas usan payloads (a/b) en un solo id: sin rangos con colisiones.
enum {
    CMD_NONE = 0,
    CMD_NEW = 1, CMD_OPEN, CMD_CONTINUE,
    CMD_CARD_BASE = 16,          // +i (tarjetas de inicio, i<16)

    CMD_SAVE = 100, CMD_UNDO, CMD_REDO, CMD_GOHOME, CMD_IMPORT, CMD_PRESENT,
    CMD_TAB_BASE = 120,          // +tab
    CMD_ADD_TITLE = 140, CMD_ADD_SONG, CMD_ADD_VERSE, CMD_ADD_LT, CMD_ADD_BLANK,
    CMD_REMOVE_SECTION, CMD_DUP_PRES, CMD_PASTE, CMD_DUP_SLIDES, CMD_DEL_SLIDES,
    CMD_FONT_DOWN, CMD_FONT_UP, CMD_ALIGN_L, CMD_ALIGN_C, CMD_ALIGN_R, CMD_TOGGLE_LT,
    CMD_ITEM_UP, CMD_ITEM_DOWN,
    CMD_PROYECTOR = 170,

    CMD_LIB_TAB_BASE = 200,      // +libtab
    CMD_SONG_BASE = 256,         // +i (songHits_ <200)
    CMD_THEME_BASE = 512,        // +i (Themes())
    CMD_BG_BASE = 640,           // +i (Backgrounds() — aplica a la diapositiva)
    CMD_MEDIA_BASE = 660,        // +i (Backgrounds() — inserta como imagen)
    CMD_INSERT_REF = 700, CMD_BOOK_PREV, CMD_BOOK_NEXT, CMD_CH_PREV, CMD_CH_NEXT,
    CMD_LT_TOGGLE,
    CMD_APPLY_BG_SECTION = 720, CMD_APPLY_BG_ALL, CMD_APPLY_THEME_ALL,
    CMD_SLIDE_PREV = 740, CMD_SLIDE_NEXT,
    CMD_TRANS_BASE = 748,          // +i (cut/fade/slide de esta diapositiva)
    CMD_TRANSDEF_BASE = 752,       // +i (predeterminada global)
    CMD_VIEW_NORMAL = 760, CMD_VIEW_SORTER, CMD_TOGGLE_PANE, CMD_TOGGLE_NOTES, CMD_TOGGLE_TASK,
    CMD_SHORTCUTS = 780, CMD_OPTIONS,

    CMD_BS_PAGE_BASE = 800,      // +page
    CMD_BS_CLOSE = 820, CMD_BS_BLANK, CMD_BS_DUP, CMD_BS_BROWSE,
    CMD_BS_PNG_CUR, CMD_BS_PNG_ALL, CMD_BS_JSON, CMD_BS_CSV,
    CMD_OPT_ADVANCE = 840, CMD_OPT_CLOCK, CMD_OPT_ANIM,

    CMD_RECENT_BASE = 900,       // +i (recientes_)

    CMD_P_EDIT = 1000, CMD_P_RESTART, CMD_P_MONITOR, CMD_P_PREV, CMD_P_NEXT,
    CMD_P_BLANK_B, CMD_P_BLANK_C, CMD_P_BLANK_L, CMD_P_ADVANCE, CMD_P_BIBLE,
    CMD_P_ITEM  = 1100,          // a=itemIdx
    CMD_P_SLIDE = 1101,          // a=itemIdx b=slideIdx
    CMD_P_LINE  = 1102,          // a=lineIdx
    CMD_P_VERSE_BASE = 1200,     // +i (verseHits_ <100)

    CMD_T_ITEM_BASE = 2300,      // +itemIdx (cabecera de sección en miniaturas)
    CMD_T_SLIDE = 2400,          // a=itemIdx b=slideIdx
    CMD_T_UP = 2401, CMD_T_DOWN = 2402, CMD_T_DEL = 2403,   // a=itemIdx

    CMD_S_SLIDE = 3000,          // clasificador: a=itemIdx b=slideIdx

    CMD_MODAL_CLOSE = 4000,
};

static const wchar_t* kClass = L"FusionHP.NativeStudio";

// ================================================================== crear
NativeStudio::NativeStudio(SlideState* state, LiveWindow* live, VideoPlayer* video, Motor* motor)
    : state_(state), live_(live), video_(video), motor_(motor) {}

NativeStudio::~NativeStudio() {
    ClearThumbs();
    if (memBmp_) DeleteObject(memBmp_);
    if (memDc_) DeleteDC(memDc_);
    if (editFont_) DeleteObject(editFont_);
}

static HWND MkEdit(HWND parent, bool multi) {
    DWORD style = WS_CHILD | ES_AUTOHSCROLL | WS_CLIPSIBLINGS;
    if (multi) style = WS_CHILD | ES_MULTILINE | ES_WANTRETURN | WS_VSCROLL |
                        ES_AUTOVSCROLL | WS_CLIPSIBLINGS;
    return CreateWindowExW(0, L"EDIT", L"", style, 0, 0, 10, 10, parent, nullptr, nullptr, nullptr);
}

bool NativeStudio::Create() {
    ui::InitGdiplus();

    WNDCLASSEXW wc = {};
    wc.cbSize = sizeof(wc);
    wc.style = CS_HREDRAW | CS_VREDRAW | CS_DBLCLKS;
    wc.lpfnWndProc = NativeStudio::WndProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = (HBRUSH)GetStockObject(WHITE_BRUSH);
    wc.lpszClassName = kClass;
    RegisterClassExW(&wc);

    hwnd_ = CreateWindowExW(0, kClass, L"Fusion HP — Estudio nativo",
                            WS_OVERLAPPEDWINDOW,
                            CW_USEDEFAULT, CW_USEDEFAULT, 1220, 800,
                            nullptr, nullptr, GetModuleHandleW(nullptr), this);
    if (!hwnd_) return false;

    editFont_ = CreateFontW(-13, 0, 0, 0, FW_NORMAL, 0, 0, 0, DEFAULT_CHARSET,
                            0, 0, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
    edtName_   = MkEdit(hwnd_, false);
    edtSearch_ = MkEdit(hwnd_, false);
    edtLib_    = MkEdit(hwnd_, false);
    edtTitle_  = MkEdit(hwnd_, false);
    edtSub_    = MkEdit(hwnd_, false);
    edtLines_  = MkEdit(hwnd_, true);
    edtNotes_  = MkEdit(hwnd_, true);
    edtBible_  = MkEdit(hwnd_, false);
    edtDesc_   = MkEdit(hwnd_, true);
    struct EId { HWND h; int id; };
    EId eids[] = {{edtName_, 1}, {edtSearch_, 2}, {edtLib_, 3}, {edtTitle_, 4},
                  {edtSub_, 5}, {edtLines_, 6}, {edtNotes_, 7}, {edtBible_, 8}, {edtDesc_, 9}};
    for (auto& e : eids) {
        SetWindowLongPtrW(e.h, GWL_ID, e.id);
        SendMessageW(e.h, WM_SETFONT, (WPARAM)editFont_, TRUE);
        SendMessageW(e.h, EM_SETCUEBANNER, TRUE, (LPARAM)L"");
    }
    SendMessageW(edtSearch_, EM_SETCUEBANNER, TRUE, (LPARAM)L"Buscar presentaciones");
    SendMessageW(edtLib_, EM_SETCUEBANNER, TRUE, (LPARAM)L"Buscar: juan 3:16");
    SendMessageW(edtBible_, EM_SETCUEBANNER, TRUE, (LPARAM)L"juan 3:16");

    // ---- datos ----
    songs_.Load(DataDir() + L"\\cancionero.fdb");
    if (bibles_.Discover(ExeDir() + L"\\resources\\data\\bibles"))
        bibles_.Select(0);                    // RV1960
    history_.SetFile(DataDir() + L"\\historial.jsonl");
    LoadRecientes();

    SyncFromMotor();
    timer_ = SetTimer(hwnd_, 1, 500, nullptr);

    ShowWindow(hwnd_, SW_SHOW);
    UpdateWindow(hwnd_);
    Logger::Info("core.studio", "estudio nativo (GUI web replicada en C++) creado");
    return true;
}

void NativeStudio::LoadRecientes() {
    recientes_.clear();
    try {
        std::ifstream f{fs::path(DataDir() + L"\\recientes.jsonl")};
        if (!f) return;
        std::string line;
        while (std::getline(f, line)) {
            try {
                Json j = Json::parse(line);
                Reciente r;
                r.t = j.value("t", (long long)0);
                r.path = ToWide(j.value("path", std::string()));
                r.name = ToWide(j.value("name", std::string()));
                if (!r.path.empty() && fs::exists(r.path)) {
                    if (r.name.empty()) r.name = fs::path(r.path).stem().wstring();
                    recientes_.push_back(r);
                }
            } catch (...) {}
        }
    } catch (...) {}
    std::sort(recientes_.begin(), recientes_.end(),
              [](const Reciente& a, const Reciente& b) { return a.t > b.t; });
    if (recientes_.size() > 16) recientes_.resize(16);
}

void NativeStudio::PushReciente(const std::wstring& path, const std::wstring& name) {
    // dedupe por ruta + al frente
    for (auto it = recientes_.begin(); it != recientes_.end(); )
        it = (it->path == path) ? recientes_.erase(it) : it + 1;
    Reciente r;
    r.t = (long long)std::time(nullptr);
    r.path = path;
    r.name = name.empty() ? fs::path(path).stem().wstring() : name;
    recientes_.insert(recientes_.begin(), r);
    if (recientes_.size() > 16) recientes_.resize(16);
    try {
        std::ofstream f{fs::path(DataDir() + L"\\recientes.jsonl")};
        if (!f) return;
        for (auto& x : recientes_) {
            Json j;
            j["t"] = x.t;
            j["path"] = ToUtf8(x.path);
            j["name"] = ToUtf8(x.name);
            f << j.dump() << "\n";
        }
    } catch (...) {}
}

// ================================================================== ventana
LRESULT CALLBACK NativeStudio::WndProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    NativeStudio* self = nullptr;
    if (m == WM_NCCREATE) {
        auto cs = (CREATESTRUCTW*)l;
        SetWindowLongPtrW(h, GWLP_USERDATA, (LONG_PTR)cs->lpCreateParams);
        self = (NativeStudio*)cs->lpCreateParams;
    } else self = (NativeStudio*)GetWindowLongPtrW(h, GWLP_USERDATA);
    if (self) return self->Handle(m, w, l);
    return DefWindowProcW(h, m, w, l);
}

LRESULT NativeStudio::Handle(UINT m, WPARAM w, LPARAM l) {
    switch (m) {
        case WM_ERASEBKGND:
            return 1;                          // doble buffer propio
        case WM_PAINT: {
            PAINTSTRUCT ps;
            HDC dc = BeginPaint(hwnd_, &ps);
            Paint();
            if (memDc_ && memW_ > 0)
                BitBlt(dc, 0, 0, memW_, memH_, memDc_, 0, 0, SRCCOPY);
            EndPaint(hwnd_, &ps);
            return 0;
        }
        case WM_SIZE:
            Repaint();
            return 0;
        case WM_TIMER:
            if (w == 1) Tick();
            return 0;
        case WM_MOUSEMOVE: {
            TRACKMOUSEEVENT tme = {sizeof(tme), TME_LEAVE, hwnd_, 0};
            TrackMouseEvent(&tme);
            POINT p{GET_X_LPARAM(l), GET_Y_LPARAM(l)};
            const Hit* hit = HitAt(p);
            int id = hit ? hit->id : 0;
            if (id != hoverId_) {
                hoverId_ = id;
                hoverA_ = hit ? hit->a : 0;
                hoverB_ = hit ? hit->b : 0;
                Repaint();
            } else if (hit && (hit->a != hoverA_ || hit->b != hoverB_)) {
                hoverA_ = hit->a; hoverB_ = hit->b;
                Repaint();
            }
            return 0;
        }
        case WM_MOUSELEAVE:
            if (hoverId_) { hoverId_ = 0; Repaint(); }
            return 0;
        case WM_LBUTTONDOWN: {
            POINT p{GET_X_LPARAM(l), GET_Y_LPARAM(l)};
            const Hit* hit = HitAt(p);
            pressId_ = hit ? hit->id : 0;
            pressA_ = hit ? hit->a : 0;
            pressB_ = hit ? hit->b : 0;
            if (hit) Repaint();
            SetFocus(hwnd_);
            return 0;
        }
        case WM_LBUTTONUP: {
            POINT p{GET_X_LPARAM(l), GET_Y_LPARAM(l)};
            const Hit* hit = HitAt(p);
            int id = hit ? hit->id : 0;
            bool same = pressId_ != 0 && pressId_ == id;
            pressId_ = 0;
            if (same && hit) {
                if (shortcutsOpen_ || optionsOpen_) {
                    if (id == CMD_MODAL_CLOSE) shortcutsOpen_ = optionsOpen_ = false;
                } else {
                    OnCommand(id);
                }
                Repaint();
            } else if (pressId_ || hit) {
                Repaint();
            }
            return 0;
        }
        case WM_LBUTTONDBLCLK: {
            POINT p{GET_X_LPARAM(l), GET_Y_LPARAM(l)};
            const Hit* hit = HitAt(p);
            if (hit && viewSorter_ && hit->id == CMD_S_SLIDE) {
                viewSorter_ = false;
                Repaint();
            }
            return 0;
        }
        case WM_MOUSEWHEEL: {
            // desplazamiento de la región bajo el cursor
            POINT p{GET_X_LPARAM(l), GET_Y_LPARAM(l)};
            ScreenToClient(hwnd_, &p);
            int dz = -((short)HIWORD(w)) / 40;
            OnWheel(p, dz);
            return 0;
        }
        case WM_SETCURSOR: {
            DWORD msg = HIWORD(l);
            if (msg == WM_MOUSEMOVE && LOWORD(w) == HTCLIENT) {
                POINT p; GetCursorPos(&p); ScreenToClient(hwnd_, &p);
                if (HitAt(p)) { SetCursor(LoadCursorW(nullptr, IDC_HAND)); return TRUE; }
            }
            break;
        }
        case WM_CTLCOLOREDIT: {
            HDC dc = (HDC)w;
            SetBkColor(dc, RGB(255, 255, 255));
            SetTextColor(dc, RGB(32, 31, 30));
            return (LRESULT)GetStockObject(WHITE_BRUSH);
        }
        case WM_COMMAND:
            if (HIWORD(w) == EN_CHANGE) OnEditChanged((int)(intptr_t)l);
            return 0;
        case WM_KEYDOWN:
            if (OnKey((UINT)w, true, (GetKeyState(VK_CONTROL) & 0x8000) != 0,
                      (GetKeyState(VK_SHIFT) & 0x8000) != 0))
                return 0;
            break;
        case WM_CHAR:
            return 0;                          // el studio come los caracteres
        case WM_CLOSE:
            // [v3.0.0 — bug «cerrar con la X»] Antes no había WM_CLOSE: DefWindowProc
            // destruía la ventana y el bucle de App::Run seguía sin PostQuitMessage
            // (proceso zombi con la salida encendida; al relanzar parecía «duplicada»).
            SaveSessionOnExit();
            DestroyWindow(hwnd_);
            return 0;
        case WM_ENDSESSION:
            // cierre del sistema: persistir y dejar que Windows termine
            if (w) SaveSessionOnExit();
            return 0;
        case WM_DESTROY:
            if (timer_) KillTimer(hwnd_, 1);
            PostQuitMessage(0);                // termina App::Run limpiamente [v3.0.0]
            return 0;
        default:
            break;
    }
    return DefWindowProcW(hwnd_, m, w, l);
}

// ================================================================== pintar
void NativeStudio::Repaint() {
    InvalidateRect(hwnd_, nullptr, FALSE);
}

void NativeStudio::Paint() {
    RECT cli;
    GetClientRect(hwnd_, &cli);
    int w = cli.right, h = cli.bottom;
    if (w <= 0 || h <= 0) return;
    if (!memDc_) memDc_ = CreateCompatibleDC(nullptr);
    if (memW_ != w || memH_ != h || !memBmp_) {
        if (memBmp_) DeleteObject(memBmp_);
        memBmp_ = CreateCompatible32(w, h);
        SelectObject(memDc_, memBmp_);
        memW_ = w; memH_ = h;
    }
    hits_.clear();
    scrolls_.clear();

    Gdiplus::Graphics g(memDc_);
    g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
    g.SetTextRenderingHint(Gdiplus::TextRenderingHintClearTypeGridFit);

    switch (mode_) {
        case Mode::Start:   PaintStart(g, cli); break;
        case Mode::Editor:  PaintEditor(g, cli); break;
        case Mode::Present: PaintPresent(g, cli); break;
    }
    if (shortcutsOpen_ || optionsOpen_) PaintModal(g, cli);
}

const NativeStudio::Hit* NativeStudio::HitAt(POINT p) const {
    // los ÚLTIMOS hits registrados ganan (los modales se pintan al final)
    for (size_t i = hits_.size(); i-- > 0; )
        if (p.x >= hits_[i].r.left && p.x < hits_[i].r.right &&
            p.y >= hits_[i].r.top && p.y < hits_[i].r.bottom)
            return &hits_[i];
    return nullptr;
}

HBITMAP NativeStudio::CreateCompatible32(int w, int h) {
    BITMAPINFO bi = {};
    bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth = w;
    bi.bmiHeader.biHeight = -h;
    bi.bmiHeader.biPlanes = 1;
    bi.bmiHeader.biBitCount = 32;
    bi.bmiHeader.biCompression = BI_RGB;
    void* bits = nullptr;
    return CreateDIBSection(nullptr, &bi, DIB_RGB_COLORS, &bits, nullptr, 0);
}

void NativeStudio::HitAdd(int id, int a, int b, const RECT& r) {
    if (r.right <= r.left || r.bottom <= r.top) return;
    hits_.push_back({id, a, b, r});
}

void NativeStudio::ScrollAdd(const RECT& r, int& off, int max) {
    if (r.right <= r.left || r.bottom <= r.top) return;
    if (off > max) off = max;
    if (off < 0) off = 0;
    scrolls_.push_back({r, &off, max});
}

// ================================================================== tick
void NativeStudio::Tick() {
    SyncFromMotor();
    if (mode_ == Mode::Present || mode_ == Mode::Start)
        Repaint();                             // reloj
}

void NativeStudio::SyncFromMotor() {
    if (!motor_) return;
    Json st = motor_->ProgramSnapshot();
    motorSnapshot_ = st;
    curScn_ = st.value("scenario", -1);
    curEl_ = st.value("element", -1);
    curLine_ = st.value("line", 0);
    blank_ = st.value("blank", std::string("black"));
    advance_ = st.value("advance", std::string("line"));

    progTitles_.clear();
    if (st.contains("program") && st["program"].is_array()) {
        int si = 0;
        for (auto& sc : st["program"]) {
            if (si++ != curScn_ || !sc.contains("elements") || !sc["elements"].is_array())
                continue;
            for (auto& el : sc["elements"])
                progTitles_.push_back({el.value("title", std::string()),
                                       el.value("kind", std::string("text"))});
        }
    }
    // historial (beta-1): registrar el elemento mostrado cuando cambia
    if (curScn_ >= 0 && curEl_ >= 0 && (long long)(curScn_ * 1000 + curEl_) != lastHistoryEl_) {
        lastHistoryEl_ = (long long)(curScn_ * 1000 + curEl_);
        if (curEl_ < (int)progTitles_.size())
            history_.Record(ToWide(progTitles_[(size_t)curEl_].first),
                            ToWide(progTitles_[(size_t)curEl_].second));
    }
}

Json NativeStudio::CurrentSlideJson() const {
    if (!motorSnapshot_.is_object()) return Json();
    if (!motorSnapshot_.contains("program")) return Json();
    const Json& prog = motorSnapshot_["program"];
    if (curScn_ < 0 || curScn_ >= (int)prog.size()) return Json();
    const Json& sc = prog[(size_t)curScn_];
    if (!sc.contains("elements")) return Json();
    const Json& els = sc["elements"];
    if (curEl_ < 0 || curEl_ >= (int)els.size()) return Json();
    const Json& el = els[(size_t)curEl_];
    if (el.contains("slide")) return el["slide"];
    return Json();
}

// ================================================================== acciones
void NativeStudio::GoMode(Mode m) {
    mode_ = m;
    hoverId_ = pressId_ = 0;
    if (m == Mode::Editor) SyncEditsFromDraft();
    Repaint();
}

void NativeStudio::Present() {
    if (!motor_ || draft_.items.empty()) { GoMode(Mode::Present); return; }
    Json payload = DraftToProgram(draft_, true, animate_);
    std::string err;
    motor_->LoadProgram(payload, &err);
    SyncFromMotor();
    GoMode(Mode::Present);
}

void NativeStudio::OpenAhp() {
    wchar_t file[MAX_PATH] = L"";
    OPENFILENAMEW ofn = {};
    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = hwnd_;
    ofn.lpstrFilter = L"Proyecto Fusion-HP (*.ahp;*.json)\0*.ahp;*.json\0Todos (*.*)\0*.*\0";
    ofn.lpstrFile = file;
    ofn.nMaxFile = MAX_PATH;
    ofn.Flags = OFN_FILEMUSTEXIST | OFN_HIDEREADONLY;
    if (!GetOpenFileNameW(&ofn)) return;

    NativeSession ses;
    if (ses.Load(file)) {
        draft_ = Draft();
        draft_.name = ses.ProjectName();
        for (auto& it : ses.Items()) {
            DraftItem di;
            di.id = it.id;
            di.label = it.title;
            di.type = "song";
            for (auto& sl : it.slides) {
                DraftSlide ds;
                ds.id = sl.id;
                ds.title = sl.reference.empty() ? it.title : sl.reference;
                for (auto& ln : sl.lines) ds.lines.push_back(ln);
                ds.reference = sl.reference;
                ds.type = sl.kind == SlideKind::Verse ? "verse"
                        : sl.kind == SlideKind::Image ? "image"
                        : sl.kind == SlideKind::Video ? "video"
                        : sl.kind == SlideKind::Lower3 ? "lower3" : "text";
                di.slides.push_back(std::move(ds));
            }
            if (!di.slides.empty()) draft_.items.push_back(std::move(di));
        }
        ahpPath_ = file;
        draft_.selItem = draft_.items.empty() ? -1 : 0;
        PushReciente(file, ses.ProjectName());
        GoMode(Mode::Editor);
    } else {
        MessageBoxW(hwnd_, (L"No se pudo abrir el proyecto.\n\nDetalle: " +
                            ToWide(ses.LastError())).c_str(),
                    L"Fusion HP", MB_ICONINFORMATION);
    }
}

void NativeStudio::SaveAhp() {
    std::wstring path = ahpPath_;
    if (path.empty()) {
        wchar_t file[MAX_PATH] = L"presentacion.ahp";
        OPENFILENAMEW ofn = {};
        ofn.lStructSize = sizeof(ofn);
        ofn.hwndOwner = hwnd_;
        ofn.lpstrFilter = L"Proyecto Fusion-HP (*.ahp)\0*.ahp\0Todos (*.*)\0*.*\0";
        ofn.lpstrFile = file;
        ofn.nMaxFile = MAX_PATH;
        ofn.Flags = OFN_OVERWRITEPROMPT | OFN_HIDEREADONLY;
        if (!GetSaveFileNameW(&ofn)) return;
        path = file;
        if (fs::path(path).extension().empty()) path += L".ahp";
    }
    if (SaveDraftAhp(draft_, path)) {
        ahpPath_ = path;
        PushReciente(path, draft_.name);
    } else {
        MessageBoxW(hwnd_, L"No se pudo guardar el proyecto.", L"Fusion HP", MB_ICONWARNING);
    }
}

// [v3.0.0 — bug «cerrar con la X»] Persistencia silenciosa al salir: guarda el
// borrador (recuperable en Inicio → borrador) y los recientes, sin diálogos.
// Nunca lanza: el cierre debe completarse aunque el disco falle.
void NativeStudio::SaveSessionOnExit() {
    try {
        if (!draft_.items.empty()) {
            std::wstring path = !ahpPath_.empty() ? ahpPath_
                                                  : DataDir() + L"\\borrador.ahp";
            SaveDraftAhp(draft_, path);
            if (ahpPath_.empty()) PushReciente(path, draft_.name);
        }
    } catch (...) {
        Logger::Warn("core.studio", "no se pudo persistir el borrador al salir");
    }
}

void NativeStudio::AddTitle() {
    PushUndo();
    DraftItem it;
    it.id = NewId();
    it.type = "title";
    it.label = L"Título";
    it.slides.push_back(DefaultTitleSlide());
    draft_.items.push_back(std::move(it));
    draft_.selItem = (int)draft_.items.size() - 1;
    draft_.selSlide = 0;
    SyncEditsFromDraft();
    Repaint();
}

void NativeStudio::AddBlankSlide() {
    PushUndo();
    DraftItem it;
    it.id = NewId();
    it.type = "blank";
    it.label = L"Pausa";
    it.slides.push_back(BlankSlide());
    draft_.items.push_back(std::move(it));
    draft_.selItem = (int)draft_.items.size() - 1;
    SyncEditsFromDraft();
    Repaint();
}

void NativeStudio::AddLowerThird() {
    PushUndo();
    DraftItem it;
    it.id = NewId();
    it.type = "lower3";
    it.label = L"Tercio inferior";
    it.slides.push_back(LowerThirdSlide());
    draft_.items.push_back(std::move(it));
    draft_.selItem = (int)draft_.items.size() - 1;
    SyncEditsFromDraft();
    Repaint();
}

void NativeStudio::InsertSong(int songIdx) {
    const LibSong* s = songs_.At((size_t)songIdx);
    if (!s) return;
    PushUndo();
    AddSongToDraft(draft_, *s, "");
    SyncEditsFromDraft();
    Repaint();
}

void NativeStudio::InsertImage(const BgDef& b) {
    PushUndo();
    DraftItem it;
    it.id = NewId();
    it.type = "image";
    it.label = ToWide(b.name);
    DraftSlide sl;
    sl.id = NewId();
    sl.type = "image";
    sl.title = ToWide(b.name);
    sl.src = "app:resources/backgrounds/" + b.file;
    sl.bgId = b.id;
    it.slides.push_back(std::move(sl));
    draft_.items.push_back(std::move(it));
    draft_.selItem = (int)draft_.items.size() - 1;
    SyncEditsFromDraft();
    Repaint();
}

void NativeStudio::ApplyThemeToSel(const std::string& themeId) {
    DraftSlide* sl = SelSlide();
    if (!sl) return;
    PushUndo();
    sl->themeId = themeId;
    ClearThumbs();
    Repaint();
}

void NativeStudio::ApplyBgToSel(const std::string& bgId) {
    DraftSlide* sl = SelSlide();
    if (!sl) return;
    PushUndo();
    sl->bgId = bgId;
    ClearThumbs();
    Repaint();
}

void NativeStudio::RemoveItem(int i) {
    if (i < 0 || i >= (int)draft_.items.size()) return;
    PushUndo();
    draft_.items.erase(draft_.items.begin() + i);
    if (draft_.selItem >= (int)draft_.items.size())
        draft_.selItem = (int)draft_.items.size() - 1;
    if (draft_.selItem < 0) draft_.selSlide = 0;
    SyncEditsFromDraft();
    Repaint();
}

void NativeStudio::MoveItem(int i, int dir) {
    int j = i + dir;
    if (i < 0 || j < 0 || i >= (int)draft_.items.size() || j >= (int)draft_.items.size()) return;
    PushUndo();
    std::swap(draft_.items[(size_t)i], draft_.items[(size_t)j]);
    if (draft_.selItem == i) draft_.selItem = j;
    else if (draft_.selItem == j) draft_.selItem = i;
    Repaint();
}

void NativeStudio::DeleteSelSlides() {
    DraftItem* it = SelItem();
    if (!it) return;
    if (draft_.selSlide < 0 || draft_.selSlide >= (int)it->slides.size()) return;
    PushUndo();
    it->slides.erase(it->slides.begin() + draft_.selSlide);
    if (draft_.selSlide >= (int)it->slides.size()) draft_.selSlide = (int)it->slides.size() - 1;
    if (it->slides.empty()) {
        // sección vacía: eliminarla (como la web quita la sección)
        draft_.items.erase(draft_.items.begin() + draft_.selItem);
        if (draft_.selItem >= (int)draft_.items.size()) draft_.selItem = (int)draft_.items.size() - 1;
    }
    SyncEditsFromDraft();
    Repaint();
}

void NativeStudio::DuplicateSelSlides() {
    DraftItem* it = SelItem();
    if (!it || draft_.selSlide < 0 || draft_.selSlide >= (int)it->slides.size()) return;
    PushUndo();
    DraftSlide copy = it->slides[(size_t)draft_.selSlide];
    copy.id = NewId();
    it->slides.insert(it->slides.begin() + draft_.selSlide + 1, std::move(copy));
    draft_.selSlide++;
    SyncEditsFromDraft();
    Repaint();
}

void NativeStudio::PushUndo() {
    undo_.push_back(draft_);
    if (undo_.size() > 40) undo_.erase(undo_.begin());   // historia 40 niveles (web)
    redo_.clear();
}
void NativeStudio::DoUndo() {
    if (undo_.empty()) return;
    redo_.push_back(draft_);
    draft_ = undo_.back();
    undo_.pop_back();
    if (draft_.selItem >= (int)draft_.items.size()) draft_.selItem = (int)draft_.items.size() - 1;
    SyncEditsFromDraft();
    Repaint();
}
void NativeStudio::DoRedo() {
    if (redo_.empty()) return;
    undo_.push_back(draft_);
    draft_ = redo_.back();
    redo_.pop_back();
    SyncEditsFromDraft();
    Repaint();
}

DraftItem* NativeStudio::SelItem() {
    if (draft_.selItem < 0 || draft_.selItem >= (int)draft_.items.size()) return nullptr;
    return &draft_.items[(size_t)draft_.selItem];
}
DraftSlide* NativeStudio::SelSlide() {
    DraftItem* it = SelItem();
    if (!it || draft_.selSlide < 0 || draft_.selSlide >= (int)it->slides.size()) return nullptr;
    return &it->slides[(size_t)draft_.selSlide];
}

int NativeStudio::SlideTotalCount() const {
    int n = 0;
    for (auto& it : draft_.items)
        for (auto& s : it.slides)
            if (s.type != "blank") n++;
    return n;
}

int NativeStudio::SlideGlobalIndex() const {
    int n = 0;
    for (int i = 0; i < (int)draft_.items.size(); i++) {
        for (int j = 0; j < (int)draft_.items[(size_t)i].slides.size(); j++) {
            if (draft_.items[(size_t)i].slides[(size_t)j].type == "blank") continue;
            if (i == draft_.selItem && j == draft_.selSlide) return n + 1;
            n++;
        }
    }
    return n > 0 ? 1 : 0;
}

std::wstring NativeStudio::ClockText() const {
    SYSTEMTIME st;
    GetLocalTime(&st);
    wchar_t buf[16];
    swprintf(buf, 16, L"%02d:%02d", st.wHour, st.wMinute);
    return buf;
}

// ---- motor ----
void NativeStudio::MotorGoto(int scn, int el, int line) {
    if (!motor_) return;
    motor_->Goto(scn, el, line);
    SyncFromMotor();
    Repaint();
}
void NativeStudio::MotorNext() {
    if (!motor_) return;
    motor_->Next();
    SyncFromMotor();
    Repaint();
}
void NativeStudio::MotorPrev() {
    if (!motor_) return;
    motor_->Prev();
    SyncFromMotor();
    Repaint();
}
void NativeStudio::MotorBlank(const std::string& mode) {
    if (!motor_) return;
    std::string next = (blank_ == mode) ? "none" : mode;
    motor_->SetBlank(next);
    SyncFromMotor();
    Repaint();
}

// ---- miniaturas ----
Gdiplus::Bitmap* NativeStudio::ThumbOf(const Json& slide, int w, int h, const std::string& key) {
    auto it = thumbs_.find(key);
    if (it != thumbs_.end()) return it->second;
    if (thumbs_.size() > 220) ClearThumbs();            // límite de memoria
    Gdiplus::Bitmap* bmp = ui::MakeThumb(slide, w, h);
    if (bmp) thumbs_[key] = bmp;
    return bmp;
}
void NativeStudio::ClearThumbs() {
    for (auto& kv : thumbs_) delete kv.second;
    thumbs_.clear();
}

// ---- exportar ----
static bool SavePng(Gdiplus::Bitmap* bmp, const std::wstring& path) {
    UINT n = 0, size = 0;
    Gdiplus::GetImageEncodersSize(&n, &size);
    if (!n) return false;
    std::vector<BYTE> buf(size);
    auto enc = (Gdiplus::ImageCodecInfo*)buf.data();
    Gdiplus::GetImageEncoders(n, size, enc);
    CLSID png{};
    for (UINT i = 0; i < n; i++)
        if (wcscmp(enc[i].MimeType, L"image/png") == 0) { png = enc[i].Clsid; break; }
    if (png.Data1 == 0 && png.Data2 == 0 && png.Data3 == 0) return false;
    return bmp->Save(path.c_str(), &png, nullptr) == Gdiplus::Ok;
}

void NativeStudio::ExportPngCurrent() {
    DraftSlide* sl = SelSlide();
    if (!sl) return;
    wchar_t file[MAX_PATH] = L"diapositiva.png";
    OPENFILENAMEW ofn = {};
    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = hwnd_;
    ofn.lpstrFilter = L"Imagen PNG (*.png)\0*.png\0";
    ofn.lpstrFile = file;
    ofn.nMaxFile = MAX_PATH;
    ofn.Flags = OFN_OVERWRITEPROMPT;
    if (!GetSaveFileNameW(&ofn)) return;
    std::wstring path = file;
    if (fs::path(path).extension().empty()) path += L".png";
    Gdiplus::Bitmap* bmp = ui::MakeThumb(SlideJsonForPreview(*sl), 1920, 1080);
    if (!bmp || !SavePng(bmp, path))
        MessageBoxW(hwnd_, L"No se pudo exportar la imagen.", L"Fusion HP", MB_ICONWARNING);
    delete bmp;
}

void NativeStudio::ExportPngAll() {
    if (draft_.items.empty()) return;
    wchar_t dir[MAX_PATH] = L"";
    BROWSEINFOW bi = {};
    bi.hwndOwner = hwnd_;
    bi.lpszTitle = L"Carpeta para las imágenes";
    bi.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;
    PIDLIST_ABSOLUTE pidl = SHBrowseForFolderW(&bi);
    if (!pidl) return;
    SHGetPathFromIDListW(pidl, dir);
    CoTaskMemFree(pidl);
    if (!*dir) return;
    int n = 0;
    for (auto& it : draft_.items)
        for (auto& sl : it.slides) {
            if (sl.type == "blank") continue;
            n++;
            wchar_t name[32];
            swprintf(name, 32, L"diapositiva-%02d.png", n);
            Gdiplus::Bitmap* bmp = ui::MakeThumb(SlideJsonForPreview(sl), 1920, 1080);
            if (bmp) { SavePng(bmp, std::wstring(dir) + L"\\" + name); delete bmp; }
        }
    MessageBoxW(hwnd_, (L"Se exportaron " + std::to_wstring(n) + L" imágenes.").c_str(),
                L"Fusion HP", MB_ICONINFORMATION);
}

void NativeStudio::ExportCsv() {
    wchar_t file[MAX_PATH] = L"historial.csv";
    OPENFILENAMEW ofn = {};
    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = hwnd_;
    ofn.lpstrFilter = L"CSV (*.csv)\0*.csv\0";
    ofn.lpstrFile = file;
    ofn.nMaxFile = MAX_PATH;
    ofn.Flags = OFN_OVERWRITEPROMPT;
    if (!GetSaveFileNameW(&ofn)) return;
    std::wstring path = file;
    if (fs::path(path).extension().empty()) path += L".csv";
    if (!history_.ExportCsv(path))
        MessageBoxW(hwnd_, L"No se pudo exportar el historial.", L"Fusion HP", MB_ICONWARNING);
}

// ---- rueda: desplaza la lista bajo el cursor ----
void NativeStudio::OnWheel(POINT p, int dz) {
    for (size_t i = scrolls_.size(); i-- > 0; ) {
        auto& s = scrolls_[i];
        if (p.x >= s.r.left && p.x < s.r.right && p.y >= s.r.top && p.y < s.r.bottom) {
            int v = *s.off + dz * 34;
            if (v < 0) v = 0;
            if (v > s.max) v = s.max;
            if (v != *s.off) { *s.off = v; Repaint(); }
            return;
        }
    }
}

// ================================================================== INICIO
// Replica de HomeShell + StartScreen (chrome.tsx de la web).
void NativeStudio::PaintStart(Gdiplus::Graphics& g, const RECT& cli) {
    int W = cli.right, H = cli.bottom;

    // ---- cabecera blanca con logo, reloj y botón Nuevo ----
    RECT top{0, 0, W, 44};
    Gdiplus::SolidBrush wp(ui::ToColor(ui::Paper));
    g.FillRectangle(&wp, (Gdiplus::REAL)(0), (Gdiplus::REAL)(0),  (Gdiplus::REAL)W, (Gdiplus::REAL)(44));
    Gdiplus::Pen lp(ui::ToColor(ui::Border), 1.0f);
    g.DrawLine(&lp, (Gdiplus::REAL)(0),  43.5f,  (Gdiplus::REAL)W,  43.5f);
    // logo "P" en cuadro acento (como la web)
    RECT lg{12, 8, 40, 36};
    ui::RoundRect(g, lg, 4, ui::ToColor(ui::Accent));
    HDC dc = g.GetHDC();
    HFONT fl = ui::Font(15, FW_SEMIBOLD);
    SelectObject(dc, fl);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, ui::Cref(0xFFFFFFFF));
    RECT lt = lg;
    DrawTextW(dc, L"F", -1, &lt, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    HFONT fn = ui::Font(15, FW_SEMIBOLD);
    SelectObject(dc, fn);
    SetTextColor(dc, ui::Cref(ui::Ink));
    RECT ln{46, 8, 260, 36};
    DrawTextW(dc, L"Fusion HP", -1, &ln, DT_VCENTER | DT_SINGLELINE);
    HFONT fs2 = ui::Font(11);
    SelectObject(dc, fs2);
    SetTextColor(dc, ui::Cref(ui::Gray));
    RECT ls{140, 8, 460, 36};
    DrawTextW(dc, L"Estudio de presentación · como PowerPoint, en español", -1, &ls,
              DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
    SetTextColor(dc, ui::Cref(ui::Gray));
    HFONT fc = ui::Font(12);
    SelectObject(dc, fc);
    RECT lc{W - 320, 8, W - 130, 36};
    DrawTextW(dc, ClockText().c_str(), -1, &lc, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);
    RECT bn{W - 120, 8, W - 12, 36};
    ui::Chip(g, bn, L"Nuevo", L"", "plus", ui::kAcc | (hoverId_ == CMD_NEW ? ui::kHot : 0));
    HitAdd(CMD_NEW, 0, 0, bn);

    // ---- barra lateral roja (Lumina web) ----
    RECT side{0, 44, 220, H - 26};
    Gdiplus::SolidBrush rb(ui::ToColor(ui::Accent));
    g.FillRectangle(&rb, (Gdiplus::REAL)(side.left), (Gdiplus::REAL)(side.top),  (Gdiplus::REAL)(side.right - side.left), 
                    (Gdiplus::REAL)(side.bottom - side.top));
    dc = g.GetHDC();
    SelectObject(dc, ui::Font(15, FW_SEMIBOLD));
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, ui::Cref(0xFFFFFFFF));
    RECT st = {side.left + 16, side.top + 6, side.right, side.top + 30};
    DrawTextW(dc, L"Fusion HP", -1, &st, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);
    struct SideBtn { int id; const wchar_t* label; const char* icon; bool darkBg; };
    SideBtn sb[] = {
        {CMD_NEW,        L"Nuevo",     "plus",          true},
        {CMD_OPEN,       L"Abrir",     "folder-open",   false},
        {CMD_CONTINUE,   L"Continuar", "player-play",   false},
    };
    int y = side.top + 40;
    for (auto& b : sb) {
        RECT r{side.left, y, side.right, y + 32};
        if (b.darkBg) {
            Gdiplus::SolidBrush dk(0x33000000);
            g.FillRectangle(&dk, (Gdiplus::REAL)(r.left), (Gdiplus::REAL)(r.top),  (Gdiplus::REAL)(r.right - r.left), 
                            (Gdiplus::REAL)(r.bottom - r.top));
        } else if (hoverId_ == b.id) {
            Gdiplus::SolidBrush hk(0x1A000000);
            g.FillRectangle(&hk, (Gdiplus::REAL)(r.left), (Gdiplus::REAL)(r.top),  (Gdiplus::REAL)(r.right - r.left), 
                            (Gdiplus::REAL)(r.bottom - r.top));
        }
        if (b.icon[0]) {
            Gdiplus::Bitmap* ic = ui::Icon(b.icon, ui::TintWhite);
            if (ic) g.DrawImage(ic, r.left + 16, r.top + 8, 16, 16);
        }
        dc = g.GetHDC();
        SelectObject(dc, ui::Font(13, b.darkBg ? FW_SEMIBOLD : FW_NORMAL));
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, ui::Cref(0xFFFFFFFF));
        RECT tr{r.left + 40, r.top, r.right, r.bottom};
        DrawTextW(dc, b.label, -1, &tr, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
        g.ReleaseHDC(dc);
        HitAdd(b.id, 0, 0, r);
        y += 32;
    }
    dc = g.GetHDC();
    SelectObject(dc, ui::Font(11));
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, ui::Cref(0xCCFFFFFF));
    RECT sf = {side.left + 16, H - 60, side.right - 8, H - 34};
    DrawTextW(dc, L"Edición como PowerPoint,\nen español y 100 % nativo", -1, &sf,
              DT_LEFT | DT_WORDBREAK);
    g.ReleaseHDC(dc);

    // ---- área principal: título + buscador + tarjetas ----
    RECT main{220, 44, W, H - 26};
    Gdiplus::SolidBrush cb(ui::ToColor(ui::CanvasBg));
    g.FillRectangle(&cb, (Gdiplus::REAL)(main.left), (Gdiplus::REAL)(main.top),  (Gdiplus::REAL)(main.right - main.left), 
                    (Gdiplus::REAL)(main.bottom - main.top));
    dc = g.GetHDC();
    SelectObject(dc, ui::Font(26, FW_SEMIBOLD));
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, ui::Cref(ui::Ink));
    RECT tt = {main.left + 24, main.top + 18, main.left + 300, main.top + 54};
    DrawTextW(dc, L"Nuevo", -1, &tt, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);
    // buscador
    RECT sq{main.right - 300, main.top + 20, main.right - 24, main.top + 48};
    ui::Field(g, sq, GetFocus() == edtSearch_, false);
    PlaceEdit(edtSearch_, RECT{sq.left + 26, sq.top + 1, sq.right - 2, sq.bottom - 1}, true);
    if (ui::Icon("search", ui::TintInk))
        g.DrawImage(ui::Icon("search", ui::TintInk), sq.left + 7, sq.top + 8, 14, 14);

    // tarjetas (4 por fila): [sesión en vivo] + [en blanco] + recientes
    struct CardD { std::wstring title, sub; int id; int a; };
    std::vector<CardD> cards;
    if (motorSnapshot_.value("hasProgram", false))
        cards.push_back({L"Sesión en vivo", L"Continuar proyectando", CMD_CONTINUE, 0});
    cards.push_back({L"Presentación en blanco", L"", CMD_NEW, 0});
    for (size_t i = 0; i < recientes_.size() && cards.size() < 15; i++)
        cards.push_back({recientes_[i].name, L"Presentación reciente", CMD_RECENT_BASE + (int)i, (int)i});

    int cw = 218, chh = 150, gap = 12;
    int perRow = std::max(1, (int)((main.right - main.left - 48) / (cw + gap)));
    RECT area{main.left + 24, main.top + 70, main.right - 24, main.bottom - 40};
    int totalH = (int)((cards.size() + perRow - 1) / perRow) * (chh + gap);
    ScrollAdd(area, scStart_, std::max(0, (int)(totalH - (area.bottom - area.top))));
    int visW = area.right - area.left;
    perRow = std::max(1, (int)(visW / (cw + gap)));
    int ax = area.left, ay = area.top - scStart_;
    int idx = 0;
    for (auto& c : cards) {
        int row = idx / perRow, col = idx % perRow;
        RECT r{area.left + col * (cw + gap), ay + row * (chh + gap),
               area.left + col * (cw + gap) + cw, ay + row * (chh + gap) + chh};
        if (r.bottom > area.top && r.top < area.bottom) {
            if (c.id == CMD_CONTINUE) ui::Card(g, r, c.title, c.sub, hoverId_ == c.id, true);
            else if (c.id == CMD_NEW) {
                // tarjeta punteada "en blanco" (web)
                Gdiplus::Pen dp(ui::ToColor(0xFFC8C6C4), 1.5f);
                dp.SetDashStyle(Gdiplus::DashStyleDash);
                g.DrawRectangle(&dp, (Gdiplus::REAL)(r.left), (Gdiplus::REAL)(r.top),  (Gdiplus::REAL)(cw - 1),  (Gdiplus::REAL)(chh - 1));
                if (ui::Icon("plus", ui::TintInk))
                    g.DrawImage(ui::Icon("plus", ui::TintInk), r.left + cw / 2 - 10,
                                r.top + 34, 20, 20);
                HDC dcx = g.GetHDC();
                SelectObject(dcx, ui::Font(12, FW_MEDIUM));
                SetBkMode(dcx, TRANSPARENT);
                SetTextColor(dcx, ui::Cref(ui::Ink));
                RECT tr{r.left + 8, r.top + 62, r.right - 8, r.top + 84};
                DrawTextW(dcx, L"Presentación en blanco", -1, &tr, DT_CENTER | DT_WORDBREAK);
                g.ReleaseHDC(dcx);
            } else {
                ui::Card(g, r, c.title, c.sub, hoverId_ == c.id, false);
            }
            HitAdd(c.id, c.a, 0, r);
        }
        idx++;
    }
    // botones inferiores
    RECT br{main.left + 24, main.bottom - 36, main.left + 240, main.bottom - 6};
    ui::Chip(g, br, L"Abrir archivo (.ahp)", L"", "folder-open",
             (hoverId_ == CMD_OPEN ? ui::kHot : 0));
    HitAdd(CMD_OPEN, 0, 0, br);

    // ---- pie ----
    RECT foot{0, H - 26, W, H};
    Gdiplus::SolidBrush fb(ui::ToColor(ui::Paper));
    g.FillRectangle(&fb, (Gdiplus::REAL)(0),  (Gdiplus::REAL)(H - 26),  (Gdiplus::REAL)W, (Gdiplus::REAL)(26));
    g.DrawLine(&lp, (Gdiplus::REAL)(0),  (Gdiplus::REAL)(H - 26) + 0.5f,  (Gdiplus::REAL)W,  (Gdiplus::REAL)(H - 26) + 0.5f);
    dc = g.GetHDC();
    SelectObject(dc, ui::Font(11));
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, ui::Cref(ui::Gray));
    RECT f1 = {12, H - 26, 400, H};
    DrawTextW(dc, (std::to_wstring(recientes_.size()) + L" presentaciones recientes").c_str(),
              -1, &f1, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    RECT f2 = {W - 420, H - 26, W - 12, H};
    DrawTextW(dc, L"N nuevo · ? métodos abreviados · Datos en este equipo", -1, &f2,
              DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);
}

void NativeStudio::PlaceEdit(HWND ed, const RECT& r, bool show) {
    if (!ed) return;
    if (!show) {
        if (IsWindowVisible(ed)) ShowWindow(ed, SW_HIDE);
        return;
    }
    RECT cur;
    GetWindowRect(ed, &cur);
    MapWindowPoints(nullptr, hwnd_, (LPPOINT)&cur, 2);
    if (cur.left != r.left || cur.top != r.top ||
        cur.right != r.right || cur.bottom != r.bottom)
        MoveWindow(ed, r.left, r.top, r.right - r.left, r.bottom - r.top, TRUE);
    if (!IsWindowVisible(ed)) ShowWindow(ed, SW_SHOW);
}

// ================================================================== MODALES
// Réplica de ShortcutsModal + SettingsModal (App.tsx).
void NativeStudio::PaintModal(Gdiplus::Graphics& g, const RECT& cli) {
    // velo
    Gdiplus::SolidBrush veil(0x66000000);
    g.FillRectangle(&veil, (Gdiplus::REAL)(0), (Gdiplus::REAL)(0),  (Gdiplus::REAL)cli.right,  (Gdiplus::REAL)cli.bottom);

    int W = 460, H = shortcutsOpen_ ? 560 : 430;
    int x = (cli.right - W) / 2, y = (cli.bottom - H) / 2;
    RECT card{x, y, x + W, y + H};
    Gdiplus::SolidBrush wb(ui::ToColor(ui::Paper));
    Gdiplus::Pen bp(ui::ToColor(ui::Border2), 1.0f);
    g.FillRectangle(&wb, (Gdiplus::REAL)(card.left), (Gdiplus::REAL)(card.top),  (Gdiplus::REAL)W,  (Gdiplus::REAL)H);
    g.DrawRectangle(&bp, (Gdiplus::REAL)(card.left), (Gdiplus::REAL)(card.top),  (Gdiplus::REAL)(W - 1),  (Gdiplus::REAL)(H - 1));

    HDC dc = g.GetHDC();
    SetBkMode(dc, TRANSPARENT);
    SelectObject(dc, ui::Font(15, FW_SEMIBOLD));
    SetTextColor(dc, ui::Cref(ui::Ink));
    RECT tt{x + 20, y + 14, x + W - 60, y + 40};
    DrawTextW(dc, shortcutsOpen_ ? L"Métodos abreviados" : L"Opciones", -1, &tt,
              DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    g.ReleaseHDC(dc);
    RECT xb{x + W - 44, y + 10, x + W - 12, y + 42};
    ui::IconButton(g, xb, "x", hoverId_ == CMD_MODAL_CLOSE ? ui::kHot : 0);
    HitAdd(CMD_MODAL_CLOSE, 0, 0, xb);

    if (shortcutsOpen_) {
        static const struct { const wchar_t* k; const wchar_t* v; } rows[] = {
            {L"Ctrl+M", L"Nueva diapositiva de título"}, {L"Ctrl+S", L"Guardar proyecto (.ahp)"},
            {L"F5", L"Presentar desde el inicio"}, {L"Ctrl+Z", L"Deshacer"}, {L"Ctrl+Y", L"Rehacer"},
            {L"Ctrl+C", L"Copiar diapositiva"}, {L"Ctrl+V", L"Pegar diapositiva"},
            {L"Ctrl+D", L"Duplicar diapositiva"}, {L"Supr", L"Eliminar diapositiva"},
            {L"Espacio / →", L"Siguiente línea o diapositiva"}, {L"←", L"Anterior"},
            {L"B", L"Pantalla negra"}, {L"C", L"Ocultar texto (fondo permanece)"},
            {L"L", L"Mostrar logo"}, {L"G", L"Biblia rápida (presentación)"},
            {L"Esc", L"Salir de modo / cerrar"}, {L"?", L"Esta ayuda"},
        };
        int ry = y + 56, rh = 27;
        for (auto& r : rows) {
            HDC dcx = g.GetHDC();
            SelectObject(dcx, ui::Font(13));
            SetBkMode(dcx, TRANSPARENT);
            SetTextColor(dcx, ui::Cref(0xFF424242));
            RECT vr{x + 20, ry, x + 320, ry + rh};
            DrawTextW(dcx, r.v, -1, &vr, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
            g.ReleaseHDC(dcx);
            ui::Kbd(g, RECT{x + W - 130, ry + 3, x + W - 20, ry + rh - 3}, r.k);
            ry += rh;
        }
    } else {
        struct OptRow { const wchar_t* label; int id; bool on; const wchar_t* sub; };
        OptRow rows[] = {
            {L"Avance línea a línea", CMD_OPT_ADVANCE, advance_ == "line",
             L"cantos por renglón (animación)"},
            {L"Mostrar reloj", CMD_OPT_CLOCK, showClock_, L"en la consola del presentador"},
            {L"Animaciones", CMD_OPT_ANIM, animate_, L"fundidos al cambiar"},
        };
        int ry = y + 60;
        for (auto& r : rows) {
            RECT cr{x + 20, ry, x + 440, ry + 46};
            if (hoverId_ == r.id) {
                Gdiplus::SolidBrush hb(ui::ToColor(ui::HoverBg));
                g.FillRectangle(&hb, (Gdiplus::REAL)(cr.left), (Gdiplus::REAL)(cr.top),  (Gdiplus::REAL)(cr.right - cr.left), 
                                (Gdiplus::REAL)(cr.bottom - cr.top));
            }
            HDC dcx = g.GetHDC();
            SelectObject(dcx, ui::Font(13, r.on ? FW_SEMIBOLD : FW_NORMAL));
            SetBkMode(dcx, TRANSPARENT);
            SetTextColor(dcx, r.on ? ui::Cref(ui::AccentDk) : ui::Cref(ui::Ink));
            RECT lr{x + 12, ry + 4, x + 380, ry + 24};
            DrawTextW(dcx, r.label, -1, &lr, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
            SelectObject(dcx, ui::Font(11));
            SetTextColor(dcx, ui::Cref(ui::Gray));
            RECT sr{x + 12, ry + 24, x + 430, ry + 42};
            DrawTextW(dcx, r.sub, -1, &sr, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
            g.ReleaseHDC(dcx);
            // interruptor estilo web
            RECT tg{x + 384, ry + 12, x + 416, ry + 28};
            ui::RoundRect(g, tg, 8, ui::ToColor(r.on ? 0xFF8CE0A4 : 0xFFC8C6C4));
            Gdiplus::SolidBrush kb(ui::ToColor(ui::Paper));
            Gdiplus::REAL kx = r.on ? tg.right - 14 : tg.left + 2;
            g.FillEllipse(&kb, (Gdiplus::REAL)(kx),  (Gdiplus::REAL)tg.top + 2, (Gdiplus::REAL)(12), (Gdiplus::REAL)(12));
            HitAdd(r.id, 0, 0, cr);
            ry += 50;
        }
        // diagnóstico/resumen (web DiagSection)
        HDC dcx = g.GetHDC();
        SelectObject(dcx, ui::Font(11));
        SetBkMode(dcx, TRANSPARENT);
        SetTextColor(dcx, ui::Cref(ui::Gray));
        RECT dr{x + 20, ry + 6, x + 440, ry + 60};
        DrawTextW(dcx, (L"Diagnóstico: " + std::to_wstring(songs_.Count()) + L" cantos · " +
                        std::to_wstring(bibles_.Count()) + L" biblias · Motor " +
                        (motorSnapshot_.value("hasProgram", false) ? L"con programa" : L"sin programa")).c_str(),
                  -1, &dr, DT_LEFT | DT_WORDBREAK);
        g.ReleaseHDC(dcx);
        RECT cb{x + 20, ry + 66, x + 210, ry + 96};
        ui::Chip(g, cb, L"Exportar historial CSV", L"", "download",
                 hoverId_ == CMD_BS_CSV ? ui::kHot : 0);
        HitAdd(CMD_BS_CSV, 0, 0, cb);
    }
}

// ================================================================== comandos
void NativeStudio::OnCommand(int id) {
    if (shortcutsOpen_ || optionsOpen_) {
        if (id == CMD_MODAL_CLOSE) shortcutsOpen_ = optionsOpen_ = false;
        return;
    }

    // ---------------- inicio ----------------
    if (id == CMD_NEW) {
        PushUndo();
        draft_ = Draft();
        ahpPath_.clear();
        AddTitle();
        GoMode(Mode::Editor);
        return;
    }
    if (id == CMD_OPEN) { OpenAhp(); return; }
    if (id == CMD_CONTINUE) {
        if (motorSnapshot_.value("hasProgram", false)) GoMode(Mode::Present);
        else GoMode(Mode::Editor);
        return;
    }
    if (id >= CMD_RECENT_BASE && id < CMD_RECENT_BASE + 16) {
        int i = id - CMD_RECENT_BASE;
        if (i >= 0 && i < (int)recientes_.size()) {
            const std::wstring& path = recientes_[(size_t)i].path;
            NativeSession ses;
            if (ses.Load(path)) {
                // reutilizar el cargador de OpenAhp
                ahpPath_ = path;
                draft_ = Draft();
                draft_.name = ses.ProjectName();
                for (auto& it : ses.Items()) {
                    DraftItem di;
                    di.id = it.id;
                    di.label = it.title;
                    for (auto& sl : it.slides) {
                        DraftSlide ds;
                        ds.id = sl.id;
                        ds.title = sl.reference.empty() ? it.title : sl.reference;
                        ds.lines = sl.lines;
                        ds.reference = sl.reference;
                        ds.type = sl.kind == SlideKind::Verse ? "verse"
                                : sl.kind == SlideKind::Image ? "image"
                                : sl.kind == SlideKind::Video ? "video"
                                : sl.kind == SlideKind::Lower3 ? "lower3" : "text";
                        di.slides.push_back(std::move(ds));
                    }
                    if (!di.slides.empty()) draft_.items.push_back(std::move(di));
                }
                draft_.selItem = draft_.items.empty() ? -1 : 0;
                PushReciente(path, draft_.name);
                GoMode(Mode::Editor);
            } else {
                MessageBoxW(hwnd_, (L"No se pudo abrir:\n" + path).c_str(),
                            L"Fusion HP", MB_ICONINFORMATION);
            }
        }
        return;
    }

    // ---------------- cabecera editor ----------------
    if (id == CMD_SAVE) { SaveAhp(); return; }
    if (id == CMD_UNDO) { DoUndo(); return; }
    if (id == CMD_REDO) { DoRedo(); return; }
    if (id == CMD_GOHOME) { GoMode(Mode::Start); return; }
    if (id == CMD_IMPORT) { OpenAhp(); return; }
    if (id == CMD_PRESENT) { Present(); return; }

    // ---------------- pestañas cinta ----------------
    if (id >= CMD_TAB_BASE && id < CMD_TAB_BASE + TAB_COUNT) {
        ribbonTab_ = id - CMD_TAB_BASE;
        if (ribbonTab_ == TAB_ARCHIVO) backPage_ = BP_INFO;
        Repaint();
        return;
    }

    // ---------------- cinta Inicio ----------------
    switch (id) {
        case CMD_ADD_TITLE: AddTitle(); return;
        case CMD_ADD_LT: AddLowerThird(); return;
        case CMD_ADD_BLANK: AddBlankSlide(); return;
        case CMD_ADD_SONG:
            taskTab_ = 1; libTab_ = LIB_CANTOS; taskOpen_ = true;
            Repaint();
            return;
        case CMD_ADD_VERSE:
            taskTab_ = 1; libTab_ = LIB_BIBLIA; taskOpen_ = true;
            Repaint();
            return;
        case CMD_REMOVE_SECTION: RemoveItem(draft_.selItem); return;
        case CMD_DUP_PRES: {
            PushUndo();
            Draft copy = draft_;
            copy.name += L" (copia)";
            for (auto& it : copy.items) {
                it.id = NewId();
                for (auto& s : it.slides) s.id = NewId();
            }
            draft_ = copy;
            Repaint();
            return;
        }
        case CMD_PASTE: PasteSlides(); return;
        case CMD_DUP_SLIDES: DuplicateSelSlides(); return;
        case CMD_DEL_SLIDES: DeleteSelSlides(); return;
        case CMD_FONT_DOWN:
        case CMD_FONT_UP: {
            DraftSlide* sl = SelSlide();
            if (!sl) return;
            PushUndo();
            const ThemeDef* t = nullptr;
            for (auto& x : Themes()) if (x.id == (sl->themeId.empty() ? "tema-clasico" : sl->themeId)) t = &x;
            int base = sl->fontSize > 0 ? sl->fontSize : (t ? t->size : 44);
            base += (id == CMD_FONT_UP) ? 4 : -4;
            if (base < 28) base = 28;
            if (base > 96) base = 96;
            sl->fontSize = base;
            ClearThumbs();
            Repaint();
            return;
        }
        case CMD_ALIGN_L: case CMD_ALIGN_C: case CMD_ALIGN_R: {
            DraftSlide* sl = SelSlide();
            if (!sl) return;
            PushUndo();
            sl->align = (id == CMD_ALIGN_L) ? 0 : (id == CMD_ALIGN_C) ? 1 : 2;
            ClearThumbs();
            Repaint();
            return;
        }
        case CMD_TOGGLE_LT: {
            DraftSlide* sl = SelSlide();
            if (!sl) return;
            PushUndo();
            sl->lowerThird = !sl->lowerThird;
            ClearThumbs();
            Repaint();
            return;
        }
        case CMD_ITEM_UP: MoveItem(draft_.selItem, -1); return;
        case CMD_ITEM_DOWN: MoveItem(draft_.selItem, 1); return;
        case CMD_PROYECTOR: {
            // ciclar la salida entre monitores (la ventana de salida es persistente)
            int n = Monitors::Count();
            if (n > 1 && live_) {
                int nextM = (live_->MonitorIndex() + 1) % n;
                live_->ShowOnMonitor(nextM);
            }
            return;
        }
        default: break;
    }

    // ---------------- paneles/vistas ----------------
    if (id >= CMD_LIB_TAB_BASE && id < CMD_LIB_TAB_BASE + 5) {
        libTab_ = id - CMD_LIB_TAB_BASE;
        taskTab_ = 1;
        scLib_ = scVerses_ = 0;
        Repaint();
        return;
    }
    if (id == 9000) { taskTab_ = 0; Repaint(); return; }
    if (id == 9001) { taskTab_ = 1; Repaint(); return; }
    if (id == 9010) {           // ciclar biblia activa
        if (bibles_.Count() > 0) {
            bibles_.Select(((bibles_.Selected() + 1) % (int)bibles_.Count()));
            bibleBook_ = -1;
            scVerses_ = 0;
            Repaint();
        }
        return;
    }
    if (id >= CMD_SONG_BASE && id < CMD_SONG_BASE + 200) {
        int k = id - CMD_SONG_BASE;
        if (k >= 0 && k < (int)songHits_.size()) InsertSong(songHits_[(size_t)k]);
        return;
    }
    if (id >= CMD_THEME_BASE && id < CMD_THEME_BASE + 16) {
        int i = id - CMD_THEME_BASE;
        if (i < (int)Themes().size()) ApplyThemeToSel(Themes()[(size_t)i].id);
        return;
    }
    if (id >= CMD_BG_BASE && id < CMD_BG_BASE + 16) {
        int i = id - CMD_BG_BASE;
        if (i < (int)Backgrounds().size()) ApplyBgToSel(Backgrounds()[(size_t)i].id);
        return;
    }
    if (id >= CMD_MEDIA_BASE && id < CMD_MEDIA_BASE + 16) {
        int i = id - CMD_MEDIA_BASE;
        if (i < (int)Backgrounds().size()) InsertImage(Backgrounds()[(size_t)i]);
        return;
    }
    if (id == CMD_INSERT_REF) { InsertVerseRange(); return; }
    if (id == CMD_CH_PREV || id == CMD_CH_NEXT) {
        std::wstring bn = bibles_.BookName((size_t)std::max(0, bibleBook_));
        int cmax = bibles_.ChapterCount(bn);
        int c = bibleChapter_ + (id == CMD_CH_NEXT ? 1 : -1);
        if (c < 1) c = cmax;
        if (c > cmax) c = 1;
        bibleChapter_ = c;
        scVerses_ = 0;
        Repaint();
        return;
    }
    if (id == CMD_LT_TOGGLE) { bibleAsLt_ = !bibleAsLt_; Repaint(); return; }
    if (id == CMD_APPLY_BG_SECTION) {
        DraftItem* it = SelItem();
        if (!it) return;
        PushUndo();
        for (auto& s : it->slides) s.bgId = SelSlide() ? SelSlide()->bgId : "";
        ClearThumbs();
        Repaint();
        return;
    }
    if (id == CMD_APPLY_BG_ALL || id == CMD_APPLY_THEME_ALL) {
        std::string v = (id == CMD_APPLY_BG_ALL) ? (SelSlide() ? SelSlide()->bgId : "")
                                                 : (SelSlide() ? SelSlide()->themeId : "");
        PushUndo();
        for (auto& it : draft_.items)
            for (auto& s : it.slides)
                if (id == CMD_APPLY_BG_ALL) s.bgId = v; else s.themeId = v;
        ClearThumbs();
        Repaint();
        return;
    }
    if (id >= CMD_TRANS_BASE && id < CMD_TRANS_BASE + 3) {
        DraftSlide* sl = SelSlide();
        if (!sl) return;
        PushUndo();
        static const char* tv[] = {"cut", "fade", "slide"};
        sl->transition = tv[id - CMD_TRANS_BASE];
        Repaint();
        return;
    }
    if (id >= CMD_TRANSDEF_BASE && id < CMD_TRANSDEF_BASE + 3) {
        static const char* tv[] = {"cut", "fade", "slide"};
        SetDefaultTransition(tv[id - CMD_TRANSDEF_BASE]);
        Repaint();
        return;
    }
    if (id == CMD_SLIDE_PREV || id == CMD_SLIDE_NEXT) {
        int dir = (id == CMD_SLIDE_NEXT) ? 1 : -1;
        DraftItem* it = SelItem();
        if (!it) return;
        int j = draft_.selSlide + dir;
        int i = draft_.selItem;
        if (j < 0) {
            if (i > 0) { i--; j = (int)draft_.items[(size_t)i].slides.size() - 1; }
            else j = 0;
        } else if (j >= (int)it->slides.size()) {
            if (i < (int)draft_.items.size() - 1) { i++; j = 0; }
            else j = (int)it->slides.size() - 1;
        }
        draft_.selItem = i;
        draft_.selSlide = j;
        SyncEditsFromDraft();
        Repaint();
        return;
    }
    if (id == CMD_VIEW_NORMAL) { viewSorter_ = false; Repaint(); return; }
    if (id == CMD_VIEW_SORTER) { viewSorter_ = true; Repaint(); return; }
    if (id == CMD_TOGGLE_PANE) { paneOpen_ = !paneOpen_; Repaint(); return; }
    if (id == CMD_TOGGLE_NOTES) { notesOpen_ = !notesOpen_; Repaint(); return; }
    if (id == CMD_TOGGLE_TASK) { taskOpen_ = !taskOpen_; Repaint(); return; }
    if (id == CMD_SHORTCUTS) { shortcutsOpen_ = true; optionsOpen_ = false; Repaint(); return; }
    if (id == CMD_OPTIONS) { optionsOpen_ = true; shortcutsOpen_ = false; Repaint(); return; }

    // ---------------- backstage ----------------
    if (id >= CMD_BS_PAGE_BASE && id < CMD_BS_PAGE_BASE + 5) {
        backPage_ = id - CMD_BS_PAGE_BASE;
        Repaint();
        return;
    }
    if (id == CMD_BS_CLOSE) { ribbonTab_ = TAB_INICIO; Repaint(); return; }
    if (id == CMD_BS_BLANK) {
        draft_ = Draft();
        ahpPath_.clear();
        SyncEditsFromDraft();
        Repaint();
        return;
    }
    if (id == CMD_BS_DUP) { OnCommand(CMD_DUP_PRES); return; }
    if (id == CMD_BS_BROWSE) { OpenAhp(); return; }
    if (id == CMD_BS_PNG_CUR) { ExportPngCurrent(); return; }
    if (id == CMD_BS_PNG_ALL) { ExportPngAll(); return; }
    if (id == CMD_BS_JSON) { SaveAhp(); return; }
    if (id == CMD_BS_CSV) { ExportCsv(); return; }
    if (id == CMD_OPT_ADVANCE) {
        if (motor_) motor_->SetAdvance(advance_ == "line" ? "slide" : "line");
        SyncFromMotor();
        Repaint();
        return;
    }
    if (id == CMD_OPT_CLOCK) { showClock_ = !showClock_; Repaint(); return; }
    if (id == CMD_OPT_ANIM) { animate_ = !animate_; ClearThumbs(); Repaint(); return; }

    // ---------------- presentar ----------------
    switch (id) {
        case CMD_P_EDIT: GoMode(Mode::Editor); return;
        case CMD_P_RESTART:
            if (motor_) motor_->Goto(curScn_ >= 0 ? curScn_ : 0, 0, 0);
            SyncFromMotor();
            Repaint();
            return;
        case CMD_P_MONITOR: OnCommand(CMD_PROYECTOR); return;
        case CMD_P_PREV: MotorPrev(); return;
        case CMD_P_NEXT: MotorNext(); return;
        case CMD_P_BLANK_B: MotorBlank("black"); return;
        case CMD_P_BLANK_C: MotorBlank("clear"); return;
        case CMD_P_BLANK_L: MotorBlank("logo"); return;
        case CMD_P_ADVANCE:
            if (motor_) motor_->SetAdvance(advance_ == "line" ? "slide" : "line");
            SyncFromMotor();
            Repaint();
            return;
        case CMD_P_BIBLE:
            bibleOpen_ = !bibleOpen_;
            if (bibleOpen_) SetFocus(edtBible_);
            Repaint();
            return;
        default: break;
    }
    if (id == CMD_P_ITEM) {
        MotorGoto(curScn_, pressA_, 0);
        return;
    }
    if (id == CMD_P_SLIDE) {
        MotorGoto(curScn_, pressA_, 0);
        return;
    }
    if (id == CMD_P_LINE) {
        if (motor_) { motor_->SetLine(pressA_); SyncFromMotor(); Repaint(); }
        return;
    }
    if (id >= CMD_P_VERSE_BASE && id < CMD_P_VERSE_BASE + 100) {
        int k = id - CMD_P_VERSE_BASE;
        if (k >= 0 && k < (int)verseNums_.size()) {
            std::wstring bn = bibles_.BookName((size_t)std::max(0, bibleBook_));
            int v = verseNums_[(size_t)k];
            std::wstring tx = bibles_.Verse(bn, bibleChapter_, v);
            std::wstring ref = bn + L" " + std::to_wstring(bibleChapter_) + L":" + std::to_wstring(v);
            PushUndo();
            if (mode_ == Mode::Editor) {
                AddVerseToDraft(draft_, ref, tx, bibleAsLt_, "");
                SyncEditsFromDraft();
            } else {
                DraftItem it;
                it.id = NewId();
                it.type = "verse";
                it.label = ref;
                DraftSlide sl;
                sl.id = NewId();
                sl.type = "verse";
                sl.title = ref;
                sl.reference = ref;
                sl.lines = {tx};
                it.slides.push_back(std::move(sl));
                draft_.items.push_back(std::move(it));
                if (motor_) motor_->LoadProgram(DraftToProgram(draft_, true, animate_));
                SyncFromMotor();
            }
            Repaint();
        }
        return;
    }

    // ---------------- fin de comandos ----------------

    // ---------------- miniaturas / clasificador ----------------
    if (id >= CMD_T_ITEM_BASE && id < CMD_T_ITEM_BASE + 200) {
        draft_.selItem = id - CMD_T_ITEM_BASE;
        draft_.selSlide = 0;
        SyncEditsFromDraft();
        Repaint();
        return;
    }
    if (id == CMD_T_SLIDE) {
        draft_.selItem = pressA_;
        draft_.selSlide = pressB_;
        SyncEditsFromDraft();
        Repaint();
        return;
    }
    if (id == CMD_T_UP) { MoveItem(pressA_, -1); return; }
    if (id == CMD_T_DOWN) { MoveItem(pressA_, 1); return; }
    if (id == CMD_T_DEL) { RemoveItem(pressA_); return; }
    if (id == CMD_S_SLIDE) {
        draft_.selItem = pressA_;
        draft_.selSlide = pressB_;
        SyncEditsFromDraft();
        Repaint();
        return;
    }
}

// ================================================================== teclado
bool NativeStudio::OnKey(UINT vk, bool down, bool ctrl, bool shift) {
    if (!down) return false;
    // si un EDIT tiene el foco, solo interferir en Esc/Enter/F5
    HWND focus = GetFocus();
    bool typing = focus && focus != hwnd_;
    if (typing) {
        if (vk == VK_ESCAPE) { SetFocus(hwnd_); return true; }
        if (vk == VK_F5) { Present(); return true; }
        return false;
    }

    // ? → métodos abreviados
    if ((vk == VK_OEM_2 || vk == '?' || vk == VK_OEM_6) && shift) {   // ? es-ES/US
        shortcutsOpen_ = !shortcutsOpen_;
        optionsOpen_ = false;
        Repaint();
        return true;
    }

    // Esc: cadena de la web (modal → presentar → editor → inicio)
    if (vk == VK_ESCAPE) {
        if (shortcutsOpen_ || optionsOpen_) { shortcutsOpen_ = optionsOpen_ = false; }
        else if (mode_ == Mode::Present) GoMode(Mode::Editor);
        else if (mode_ == Mode::Editor && ribbonTab_ == TAB_ARCHIVO) { ribbonTab_ = TAB_INICIO; Repaint(); }
        else if (mode_ == Mode::Editor) GoMode(Mode::Start);
        return true;
    }
    if (vk == VK_F5) {
        if (mode_ == Mode::Editor) Present();
        else OnCommand(CMD_PROYECTOR);
        return true;
    }
    if (shortcutsOpen_ || optionsOpen_) return true;     // modal come el teclado

    if (mode_ == Mode::Start) {
        if (vk == 'N') { OnCommand(CMD_NEW); return true; }
        return false;
    }

    if (mode_ == Mode::Present) {
        switch (vk) {
            case VK_RIGHT: case VK_SPACE: case VK_NEXT: MotorNext(); return true;
            case VK_LEFT: case VK_PRIOR: MotorPrev(); return true;
            case 'B': MotorBlank("black"); return true;
            case 'C': MotorBlank("clear"); return true;
            case 'L': MotorBlank("logo"); return true;
            case 'G':
                if (mode_ == Mode::Present) { bibleOpen_ = !bibleOpen_; Repaint(); }
                return true;
            case 'F': OnCommand(CMD_PROYECTOR); return true;
            default: return false;
        }
    }

    if (mode_ == Mode::Editor) {
        if (ctrl) {
            switch (vk) {
                case 'M': AddTitle(); return true;
                case 'S': SaveAhp(); return true;
                case 'Z': shift ? DoRedo() : DoUndo(); return true;
                case 'Y': DoRedo(); return true;
                case 'C': CopySelSlides(); return true;
                case 'V': PasteSlides(); return true;
                case 'D': DuplicateSelSlides(); return true;
                default: return false;
            }
        }
        switch (vk) {
            case VK_DELETE: case VK_BACK: DeleteSelSlides(); return true;
            case VK_DOWN: case VK_NEXT: OnCommand(CMD_SLIDE_NEXT); return true;
            case VK_UP: case VK_PRIOR: OnCommand(CMD_SLIDE_PREV); return true;
            default: return false;
        }
    }
    return false;
}

} // namespace fusion
