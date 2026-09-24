// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - native/launcher/LuminaLauncher.cpp
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ----------------------------------------------------------------------------
//  Lanzador NATIVO del paquete híbrido — v7.0.0 «ULTRA» (Plan de Ultra
//  Implementación: F0.01-F0.04, F0.08, F6.05/F6.06).
//
//  Responsabilidad:
//    1. DETECCIÓN COMPLETA antes de cargar C# (F0.01-F0.03, Sección 3.1):
//         - SO por RtlGetVersion (GetVersion PROHIBIDA — obsoleta);
//         - Win7 sin SP1 → mensaje con sugerencia de SP1 (F0.01.5);
//         - arquitectura por IsWow64Process2 con alternativa (F0.02);
//         - .NET por registro de SOLO LECTURA → perfil A/B/C (F0.03/F0.04);
//         - clasificación y decisión compartidas con el núcleo (Bootstrap.cpp:
//           UNA sola fuente de verdad — misma política que lumina_env_detect).
//    2. CONMUTADORES EXPLÍCITOS (§2.2 / F6.06):
//         /arch:x86|x64     fuerza la variante de paquete
//         /profile:A|B|C    fuerza el perfil de ejecución
//         /emergency        equivale a /profile:C
//         /open:<archivo>   sesión plana para el modo emergencia
//         /log:DEBUG|INFO   nivel del log de arranque (§10.1)
//    3. BITÁCORA DE ARRANQUE (F0.09): <paquete>\logs\lumina-AAAAMMDD.log con
//       SO, arquitectura, runtime, perfil y decisión — formato §10.1.
//    4. PERFIL A/B → lanza la variante gestionada (net48 / net35) con la
//       verificación de componentes v5.1.0 ( mensajes que dicen QUÉ falta ).
//    5. PERFIL C → MODO EMERGENCIA NATIVO (F0.08): proyección de texto,
//       imagen y VIDEO desde el propio launcher usando el MISMO Projector/
//       Renderer/DirectShow del núcleo (compilados AQUÍ — el CMake del
//       launcher no se toca). Sesión plana empaquetada (.txt), negro, logo,
//       fondo fijo, leyenda de emergencia — la proyección NO depende de C#.
//    6. Configuración instalada (F6.05): si existe <exe>\installed.marker,
//       los logs van a %APPDATA%\AppHibrida\logs (modo instalado); sin
//       marcador → portable (<exe>\logs). La app NUNCA escribe registro.
//
//  Subsistema: WINDOWS (GUI). Compilación: x86 y x64 con /MT.
// ============================================================================
// NOMINMAX/WIN32_LEAN_AND_MEAN ANTES del primer windows.h (el CMake autónomo
// del launcher no los define: sin NOMINMAX las macros min/max de Win32
// rompen std::min/std::max del Renderer incluido abajo — C2589).
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <commdlg.h>      // GetOpenFileNameW (sesión plana del modo emergencia)
#include <shellapi.h>     // CommandLineToArgvW / ShellExecuteW
#include <cwctype>        // towlower (conmutadores insensibles a mayúsculas)
#include <cstdlib>        // atoi/_atoi64 (directivas @volumen/@inicio)

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "gdiplus.lib")
#pragma comment(lib, "msimg32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "comdlg32.lib")
#pragma comment(lib, "shell32.lib")

// ---------------------------------------------------------------------------
// Módulos del NÚCLEO compilados en el launcher (UNA sola fuente de verdad):
// Bootstrap (detección/política) + NativeLog (§10.1) + Projector/Renderer/
// VideoDS (proyección nativa de emergencia). Las rutas relativas resuelven
// desde este archivo — el CMake autónomo del launcher permanece intacto.
// ---------------------------------------------------------------------------
#include "../core/src/Bootstrap.cpp"
#include "../core/src/NativeLog.cpp"
#define LUMINA_HAS_WIN32 1
#include "../core/src/Win7Compat.cpp"  // shim Win7 SP1 (import Win8+ del STL)
#include "../core/src/Highlight.cpp"
#include "../core/src/Renderer.cpp"
#include "../core/src/Projector.cpp"   // incluye VideoDS.cpp (DirectShow)

// v7.0.0: Win7Compat.cpp define el __imp_ como DATO de objeto (x64) y el
// shim Win8+/Win7. En x86 el import decorado NO llega porque Projector.cpp
// usa CreateThread puro (el STL de std::thread/join era el que arrastraba
// GetSystemTimePreciseAsFileTime — diagnóstico v5.2.0).

using namespace lumina;

// ---------------------------------------------------------------------------
// Constantes
// ---------------------------------------------------------------------------
static const wchar_t* kWindowTitle = L"LuminaLauncher v7.1.0 «OPERADOR»";

static const wchar_t* kTargetNet48 = L"net48\\LuminaPresentation.exe";
static const wchar_t* kTargetNet35 = L"net35\\LuminaPresentation35.exe";

static const wchar_t* kRequiredSidecarDlls[] = {
    L"LuminaCore.dll", L"Lumina.Core.dll", L"Lumina.Bridge.dll", L"Lumina.Api.dll"
};

// ---------------------------------------------------------------------------
// Utilidades de ruta y mensajería
// ---------------------------------------------------------------------------
static void DirNameOf(const wchar_t* path, wchar_t* dirOut, size_t cchOut) {
    size_t len = 0;
    while (path[len] != L'\0' && len + 1 < cchOut) ++len;
    size_t last = 0; bool found = false;
    for (size_t i = 0; i < len; ++i)
        if (path[i] == L'\\' || path[i] == L'/') { last = i; found = true; }
    if (!found) { dirOut[0] = L'.'; dirOut[1] = L'\0'; return; }
    for (size_t i = 0; i <= last; ++i) dirOut[i] = path[i];
    dirOut[last + 1] = L'\0';
}

static bool JoinPath(const wchar_t* dir, const wchar_t* file,
                     wchar_t* out, size_t cchOut) {
    size_t d = 0;
    while (dir[d] != L'\0' && d + 1 < cchOut) { out[d] = dir[d]; ++d; }
    if (d > 0 && out[d - 1] != L'\\' && out[d - 1] != L'/' && d + 1 < cchOut)
        out[d++] = L'\\';
    size_t f = 0;
    while (file[f] != L'\0' && d + 1 < cchOut) { out[d++] = file[f++]; }
    if (file[f] != L'\0') return false;
    out[d] = L'\0';
    return true;
}

static std::wstring ExeDir() {
    wchar_t launcherPath[MAX_PATH];
    DWORD n = GetModuleFileNameW(nullptr, launcherPath, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return L".";
    wchar_t baseDir[MAX_PATH];
    DirNameOf(launcherPath, baseDir, MAX_PATH);
    return std::wstring(baseDir);
}

static void ShowErrorBox(const wchar_t* text) {
    MessageBoxW(nullptr, text, kWindowTitle, MB_OK | MB_ICONERROR);
}

static void ShowInfoBox(const wchar_t* text) {
    MessageBoxW(nullptr, text, kWindowTitle, MB_OK | MB_ICONINFORMATION);
}

// UTF-8 → UTF-16 (rutas/contenido de la sesión plana).
static std::wstring Utf8ToW(const std::string& s) {
    if (s.empty()) return std::wstring();
    const int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0);
    if (n <= 0) return std::wstring();
    std::wstring w((size_t)n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), &w[0], n);
    return w;
}

// UTF-16 → UTF-8 (rutas de recursos con acentos, sin pérdida).
static std::string WToUtf8(const std::wstring& w) {
    if (w.empty()) return std::string();
    const int n = WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(),
                                      nullptr, 0, nullptr, nullptr);
    if (n <= 0) return std::string();
    std::string s((size_t)n, '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), &s[0], n, nullptr, nullptr);
    return s;
}

// ---------------------------------------------------------------------------
// Conmutadores de la línea de comandos (§2.2: forzar variante explícita).
// ---------------------------------------------------------------------------
struct Switches {
    bool forceX86 = false, forceX64 = false;
    bool forceA = false, forceB = false, forceC = false;
    bool emergency = false;
    std::wstring openFile;              // /open:<archivo>
    bool logDebug = false;
};

static Switches ParseSwitches() {
    Switches sw;
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (!argv) return sw;
    for (int i = 1; i < argc; ++i) {
        std::wstring a = argv[i];
        for (auto& c : a) c = (wchar_t)towlower(c);
        if (a == L"/arch:x86" || a == L"-arch:x86") sw.forceX86 = true;
        else if (a == L"/arch:x64" || a == L"-arch:x64") sw.forceX64 = true;
        else if (a == L"/profile:a" || a == L"-profile:a") sw.forceA = true;
        else if (a == L"/profile:b" || a == L"-profile:b") sw.forceB = true;
        else if (a == L"/profile:c" || a == L"-profile:c") sw.forceC = true;
        else if (a == L"/emergency" || a == L"-emergency") sw.emergency = true;
        else if (a == L"/log:debug") sw.logDebug = true;
        else if (a.rfind(L"/open:", 0) == 0)
            sw.openFile = std::wstring(argv[i]).substr(6);
    }
    LocalFree(argv);
    return sw;
}

// ---------------------------------------------------------------------------
// Log de arranque (F0.09): decide carpeta portable vs instalada (F6.05).
// ---------------------------------------------------------------------------
static std::string LogDirFor(const std::wstring& base) {
    wchar_t marker[MAX_PATH];
    if (JoinPath(base.c_str(), L"installed.marker", marker, MAX_PATH) &&
        GetFileAttributesW(marker) != INVALID_FILE_ATTRIBUTES) {
        // MODO INSTALADO (F6.05.1): %APPDATA%\AppHibrida\logs (sin shlobj:
        // la variable de entorno APPDATA la resuelve el propio Win32).
        wchar_t appdata[MAX_PATH];
        if (GetEnvironmentVariableW(L"APPDATA", appdata, MAX_PATH) > 0) {
            wchar_t full[MAX_PATH];
            if (JoinPath(appdata, L"AppHibrida\\logs", full, MAX_PATH)) {
                const int n = WideCharToMultiByte(CP_UTF8, 0, full, -1,
                    nullptr, 0, nullptr, nullptr);
                std::string out((size_t)(n > 0 ? n : 1), '\0');
                if (n > 0) WideCharToMultiByte(CP_UTF8, 0, full, -1, &out[0], n,
                    nullptr, nullptr);
                if (!out.empty() && out.back() == '\0') out.pop_back();
                return out;
            }
        }
    }
    // MODO PORTABLE (F6.05.3): <exe>\logs — §10.2, no escribe fuera.
    const std::string b8 = std::string(base.begin(), base.end());
    return b8 + "\\logs";
}

static nlog::Log g_bootLog;

// ---------------------------------------------------------------------------
// Lanzamiento de la variante gestionada (perfiles A y B)
// ---------------------------------------------------------------------------
static DWORD LaunchAndWait(const wchar_t* exePath, const wchar_t* workDir,
                           DWORD* outExitCode) {
    STARTUPINFOW si; PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof(si)); ZeroMemory(&pi, sizeof(pi));
    si.cb = sizeof(si);
    wchar_t cmdLine[MAX_PATH + 4];
    cmdLine[0] = L'"';
    size_t i = 0;
    while (exePath[i] != L'\0' && i + 2 < (MAX_PATH + 3)) { cmdLine[i + 1] = exePath[i]; ++i; }
    cmdLine[i + 1] = L'"'; cmdLine[i + 2] = L'\0';
    if (!CreateProcessW(exePath, cmdLine, nullptr, nullptr, FALSE, 0, nullptr,
                        workDir, &si, &pi))
        return GetLastError();
    WaitForSingleObject(pi.hProcess, INFINITE);
    DWORD code = 1;
    if (!GetExitCodeProcess(pi.hProcess, &code)) code = 1;
    CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
    *outExitCode = code;
    return 0;
}

static bool VerifySidecars(const wchar_t* targetPath) {
    wchar_t targetDir[MAX_PATH];
    DirNameOf(targetPath, targetDir, MAX_PATH);
    for (int i = 0; i < (int)(sizeof(kRequiredSidecarDlls) / sizeof(kRequiredSidecarDlls[0])); ++i) {
        wchar_t compPath[MAX_PATH];
        if (!JoinPath(targetDir, kRequiredSidecarDlls[i], compPath, MAX_PATH)) continue;
        if (GetFileAttributesW(compPath) == INVALID_FILE_ATTRIBUTES) {
            wchar_t msg[700];
            swprintf_s(msg, _TRUNCATE,
                L"Falta un componente del programa:\n\n  %s\\%s\n\n"
                L"Causas frecuentes:\n"
                L"  1. Ejecutar desde DENTRO del ZIP (sin extraer).\n"
                L"  2. Extracción incompleta o antivirus que aisló el archivo.\n\n"
                L"Solución: extraiga el ZIP COMPLETO en una carpeta propia y "
                L"ejecute LuminaLauncher.exe desde ahí.",
                (targetDir[0] == L'.' ? L"" : targetDir), kRequiredSidecarDlls[i]);
            MessageBoxW(nullptr, msg, kWindowTitle, MB_OK | MB_ICONWARNING);
            return false;
        }
    }
    return true;
}

// ---------------------------------------------------------------------------
// MODO EMERGENCIA NATIVO (F0.08) — perfil C
// ---------------------------------------------------------------------------
namespace emergency {

struct Session {
    std::vector<Slide> slides;
    std::wstring fileDir;      // base para rutas relativas
    std::wstring fileName;
};

static Session            g_session;
static Projector*         g_proj = nullptr;
static Theme              g_theme;
static int                g_current = -1;
static bool               g_black = false;
static HWND               g_ctrl = nullptr;

// Ruta absoluta de un recurso de sesión (relativa al archivo o absoluta).
static std::string ResolvePath(const std::string& p) {
    if (p.empty()) return p;
    if ((p.size() > 2 && (p[1] == ':' || (p[0] == '\\' && p[1] == '\\'))))
        return p;                                    // absoluta (C:\ o \\srv)
    return WToUtf8(g_session.fileDir) + "\\" + p;  // relativa a la sesión
}

// Lee el archivo completo como UTF-8 (detecta BOM UTF-16 de Notepad).
static std::string ReadFileUtf8(const std::wstring& path, bool* ok) {
    *ok = false;
    HANDLE f = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
                           OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (f == INVALID_HANDLE_VALUE) return std::string();
    LARGE_INTEGER sz;
    if (!GetFileSizeEx(f, &sz) || sz.QuadPart > (16LL * 1024 * 1024)) {
        CloseHandle(f); return std::string();
    }
    std::string raw((size_t)sz.QuadPart, '\0');
    DWORD got = 0;
    if (!ReadFile(f, &raw[0], (DWORD)raw.size(), &got, nullptr)) {
        CloseHandle(f); return std::string();
    }
    CloseHandle(f);
    raw.resize(got);
    *ok = true;
    // BOM UTF-16LE (Notepad): convertir a UTF-8.
    if (raw.size() >= 2 && (unsigned char)raw[0] == 0xFF && (unsigned char)raw[1] == 0xFE) {
        const wchar_t* w = (const wchar_t*)(raw.data() + 2);
        const size_t wn = (raw.size() - 2) / sizeof(wchar_t);
        const int n = WideCharToMultiByte(CP_UTF8, 0, w, (int)wn, nullptr, 0, nullptr, nullptr);
        if (n > 0) {
            std::string out((size_t)n, '\0');
            WideCharToMultiByte(CP_UTF8, 0, w, (int)wn, &out[0], n, nullptr, nullptr);
            return out;
        }
        return std::string();
    }
    if (raw.size() >= 3 && (unsigned char)raw[0] == 0xEF && (unsigned char)raw[1] == 0xBB)
        return raw.substr(3);                        // BOM UTF-8
    // ANSI local → UTF-8 (tildes de archivos viejos).
    const int wn = MultiByteToWideChar(CP_ACP, 0, raw.data(), (int)raw.size(), nullptr, 0);
    if (wn > 0) {
        std::wstring w((size_t)wn, L'\0');
        MultiByteToWideChar(CP_ACP, 0, raw.data(), (int)raw.size(), &w[0], wn);
        const int n = WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), nullptr, 0, nullptr, nullptr);
        if (n > 0) {
            std::string out((size_t)n, '\0');
            WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), &out[0], n, nullptr, nullptr);
            return out;
        }
    }
    return raw;
}

// Carga la sesión plana (perfil C §4.2: «sesión/proyecto plano empaquetado»).
// Formato: bloques separados por línea vacía; directivas por línea:
//   @img:<ruta>      → slide de imagen
//   @video:<ruta>    → slide de video (DirectShow)
//   @volumen:<0-100> @inicio:<ms> @loop   → ajustes del video del bloque
//   # comentario     → ignorado
// Todo lo demás: líneas de texto de la slide.
static bool LoadSession(const std::wstring& path) {
    bool ok = false;
    const std::string text = ReadFileUtf8(path, &ok);
    if (!ok) return false;
    g_session.slides.clear();
    g_session.fileName = path;
    wchar_t dir[MAX_PATH];
    DirNameOf(path.c_str(), dir, MAX_PATH);
    g_session.fileDir = dir;

    Slide cur; bool inBlock = false;
    int pendingVolume = 100; int64_t pendingStart = 0; bool pendingLoop = false;
    auto flush = [&]() {
        if (!inBlock) return;
        if (cur.kind == SLIDE_VIDEO) {
            cur.videoVolume = pendingVolume;
            cur.videoStartAtMs = pendingStart;
            cur.videoLoop = pendingLoop;
        }
        if (!cur.lines.empty() || !cur.imagePath.empty() || !cur.videoPath.empty())
            g_session.slides.push_back(cur);
        cur = Slide(); inBlock = false;
        pendingVolume = 100; pendingStart = 0; pendingLoop = false;
    };
    std::string lineBuf;
    auto handleLine = [&](const std::string& ln) {
        if (ln.empty()) { flush(); return; }
        if (ln[0] == '#') return;
        if (ln.rfind("@img:", 0) == 0) {
            if (cur.kind != SLIDE_VIDEO) { cur = Slide(); }
            cur.kind = SLIDE_IMAGE;
            cur.imagePath = ResolvePath(Trim(ln.substr(5)));
            inBlock = true; return;
        }
        if (ln.rfind("@video:", 0) == 0) {
            flush();
            cur = Slide(); cur.kind = SLIDE_VIDEO;
            cur.videoPath = ResolvePath(Trim(ln.substr(7)));
            inBlock = true; return;
        }
        if (ln.rfind("@volumen:", 0) == 0) { pendingVolume = atoi(ln.c_str() + 9); return; }
        if (ln.rfind("@inicio:", 0) == 0)  { pendingStart = _atoi64(ln.c_str() + 8); return; }
        if (ln == "@loop") { pendingLoop = true; return; }
        // línea de texto
        if (cur.kind == SLIDE_IMAGE || cur.kind == SLIDE_VIDEO) flush();
        if (!inBlock) { cur = Slide(); cur.kind = SLIDE_TEXT; inBlock = true; }
        cur.lines.push_back(SlideLine(ln));
    };
    for (char c : text) {
        if (c == '\r') continue;
        if (c == '\n') { handleLine(Trim(lineBuf)); lineBuf.clear(); }
        else lineBuf += c;
    }
    handleLine(Trim(lineBuf));
    flush();
    g_current = g_session.slides.empty() ? -1 : 0;
    return !g_session.slides.empty();
}

static void Publish() {
    if (!g_proj) return;
    std::vector<std::string> titles;
    for (const Slide& s : g_session.slides)
        titles.push_back(s.kind == SLIDE_VIDEO ? "Video" :
                         s.kind == SLIDE_IMAGE ? "Imagen" : "Texto");
    g_proj->SetContent(g_session.slides, titles, g_theme, g_current, g_black);
    if (g_ctrl) {
        wchar_t st[256];
        swprintf_s(st, _TRUNCATE, L"  %s  —  diapositiva %d / %d%s",
            g_session.fileName.empty() ? L"(sin sesión)" : g_session.fileName.c_str(),
            g_current + 1, (int)g_session.slides.size(),
            g_black ? L"  [NEGRO]" : L"");
        SetWindowTextW(g_ctrl, st);
    }
}

static void Next() {
    if (g_session.slides.empty()) return;
    g_current = (g_current + 1) % (int)g_session.slides.size();
    Publish();
}
static void Prev() {
    if (g_session.slides.empty()) return;
    g_current = g_current <= 0 ? (int)g_session.slides.size() - 1 : g_current - 1;
    Publish();
}
static void SetBlack(bool on) { g_black = on; Publish(); }

// Logo de la iglesia: <base>\data\logo.png o <base>\logo.png (F0.08.4).
static void ShowLogo() {
    const std::wstring base = ExeDir();
    const wchar_t* cands[] = { L"data\\logo.png", L"logo.png", L"data\\logo.jpg" };
    for (const wchar_t* c : cands) {
        wchar_t p[MAX_PATH];
        if (!JoinPath(base.c_str(), c, p, MAX_PATH)) continue;
        if (GetFileAttributesW(p) != INVALID_FILE_ATTRIBUTES) {
            Slide s; s.kind = SLIDE_IMAGE;
            s.imagePath = WToUtf8(std::wstring(p));
            g_session.slides.push_back(s);
            g_current = (int)g_session.slides.size() - 1;
            g_black = false;
            Publish();
            return;
        }
    }
    ShowInfoBox(L"No se encontró el logo (data\\logo.png). Coloque la imagen "
                L"en la carpeta «data» del paquete.");
}

static void OpenDialog() {
    wchar_t file[MAX_PATH] = L"";
    OPENFILENAMEW ofn; ZeroMemory(&ofn, sizeof(ofn));
    ofn.lStructSize = sizeof(ofn);
    ofn.hwndOwner = g_ctrl;
    ofn.lpstrFilter = L"Sesión de emergencia (*.txt)\0*.txt\0"
                      L"Todos los archivos (*.*)\0*.*\0";
    ofn.lpstrFile = file;
    ofn.nMaxFile = MAX_PATH;
    ofn.Flags = OFN_FILEMUSTEXIST | OFN_HIDEREADONLY;
    if (GetOpenFileNameW(&ofn)) {
        if (!LoadSession(file)) {
            ShowErrorBox(L"No se pudo leer la sesión (el archivo está vacío "
                         L"o supera 16 MB).");
        } else Publish();
    }
}

static LRESULT CALLBACK CtrlProc(HWND hwnd, UINT m, WPARAM wp, LPARAM lp) {
    switch (m) {
        case WM_COMMAND: {
            const int id = LOWORD(wp);
            if (id == 1) OpenDialog();
            else if (id == 2) Prev();
            else if (id == 3) Next();
            else if (id == 4) SetBlack(!g_black);
            else if (id == 5) ShowLogo();
            else if (id == 6) SetBlack(false);          // fondo fijo (tema)
            else if (id == 9) { DestroyWindow(hwnd); PostQuitMessage(0); }
            return 0;
        }
        case WM_KEYDOWN:
            switch (wp) {
                case VK_RIGHT: case VK_SPACE: case VK_RETURN: case 'N': Next(); return 0;
                case VK_LEFT: case VK_PRIOR: case 'P': Prev(); return 0;
                case 'B': case VK_ESCAPE: SetBlack(!g_black); return 0;   // reposo (F1.08)
                case 'L': ShowLogo(); return 0;
                case 'O': OpenDialog(); return 0;
            }
            break;
        case WM_CLOSE:
            DestroyWindow(hwnd);
            PostQuitMessage(0);
            return 0;
    }
    return DefWindowProcW(hwnd, m, wp, lp);
}

// Bucle del modo emergencia: ventana de control + proyección nativa.
// Devuelve el exit code del proceso.
static int Run(const std::wstring& base, const std::wstring& openFile) {
    // COM para DirectShow (hilo de esta ventana).
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

    g_theme.bgColor = "#FF0B1F2A";
    g_theme.fgColor = "#FFFFFFFF";
    g_theme.accentColor = "#FF3AA6B9";
    g_theme.fontFace = "Segoe UI";
    g_theme.fontSize = 54;

    // Proyección: monitor secundario si existe (índice 1), primario si no.
    g_proj = new Projector();
    g_proj->Show(1, true);

    // Ventana de control (operador) — siempre visible (topmost).
    WNDCLASSW wc = {0};
    wc.lpfnWndProc = &CtrlProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.hCursor = LoadCursorW(nullptr, (LPCWSTR)IDC_ARROW);
    wc.hbrBackground = (HBRUSH)(COLOR_BTNFACE + 1);
    wc.lpszClassName = L"LuminaEmergencyCtrl";
    RegisterClassW(&wc);
    g_ctrl = CreateWindowExW(WS_EX_TOPMOST, L"LuminaEmergencyCtrl",
        L"MODO EMERGENCIA — LuminaPresentation (perfil C, sin .NET)",
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU,
        60, 60, 560, 300, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);

    // Controles de la ventana.
    HWND hLegend = CreateWindowExW(0, L"STATIC",
        L"MODO EMERGENCIA NATIVO (perfil C)\n"
        L"El runtime .NET no está disponible: se proyecta desde el núcleo\n"
        L"nativo. Funciones de canciones/biblia/editor/API requieren .NET 4.8\n"
        L"(instalador offline: https://dotnet.microsoft.com/download/dotnet-"
        L"framework/net48).\n\n"
        L"Sesión plana: bloques separados por línea vacía; @img: y @video:\n"
        L"como primera línea del bloque.\n"
        L"Teclas: [→/Espacio/Enter] siguiente · [←] anterior · [Esc/B] negro\n"
        L"[L] logo · [O] abrir sesión",
        WS_CHILD | WS_VISIBLE, 10, 10, 530, 130, g_ctrl, (HMENU)(INT_PTR)10, nullptr, nullptr);
    (void)hLegend;
    const struct { const wchar_t* txt; int id; int x; } btns[] = {
        { L"Abrir sesión…", 1, 10 }, { L"◀ Anterior", 2, 130 },
        { L"Siguiente ▶", 3, 230 },  { L"Negro", 4, 330 },
        { L"Logo", 5, 400 },         { L"Fondo", 6, 460 },
    };
    for (const auto& b : btns)
        CreateWindowExW(0, L"BUTTON", b.txt, WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
                        b.x, 150, 100, 30, g_ctrl, (HMENU)(INT_PTR)b.id, nullptr, nullptr);
    CreateWindowExW(0, L"BUTTON", L"Salir",
                    WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON,
                    10, 200, 100, 30, g_ctrl, (HMENU)9, nullptr, nullptr);
    ShowWindow(g_ctrl, SW_SHOW);
    SetFocus(g_ctrl);

    if (!openFile.empty()) {
        if (LoadSession(openFile)) Publish();
    } else {
        // Sesión por defecto: <base>\data\sesion-emergencia.txt si existe.
        wchar_t def[MAX_PATH];
        if (JoinPath(base.c_str(), L"data\\sesion-emergencia.txt", def, MAX_PATH) &&
            GetFileAttributesW(def) != INVALID_FILE_ATTRIBUTES) {
            if (LoadSession(def)) Publish();
        }
    }

    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    g_proj->Close();
    delete g_proj;
    g_proj = nullptr;
    CoUninitialize();
    return 0;
}

} // namespace emergency

// ---------------------------------------------------------------------------
// Punto de entrada
// ---------------------------------------------------------------------------
int WINAPI WinMain(HINSTANCE, HINSTANCE, LPSTR, int) {
    const std::wstring base = ExeDir();
    const Switches sw = ParseSwitches();

    // ---- Bitácora de arranque (F0.09): portable o instalada (F6.05) ----
    nlog::Log::Options lo;
    lo.dir = LogDirFor(base);
    lo.minLevel = sw.logDebug ? nlog::SEV_DEBUG : nlog::SEV_INFO;
    g_bootLog.Open(lo);
    g_bootLog.Write(nlog::SEV_INFO, "bootstrap",
                    "LuminaLauncher v7.1.0 «OPERADOR» — arranque nativo");

    // ---- F0.01-F0.03: detección ANTES de cargar C# ----
    const bootstrap::EnvDecision d = bootstrap::DetectEnvironment(
        std::string(), sw.forceX86, sw.forceX64,
        sw.forceA, sw.forceB, sw.forceC || sw.emergency);
    g_bootLog.Write(nlog::SEV_INFO, "bootstrap", "entorno: " +
                    bootstrap::EnvDecisionToJson(d));

    // F0.01.4-5: SO no soportado → mensaje claro (antes de TODO lo demás).
    if (!bootstrap::OsSupported(d.osClass)) {
        g_bootLog.Write(nlog::SEV_WARN, "bootstrap", "SO no soportado: " +
                        std::string(bootstrap::OsClassLabel(d.osClass)));
        ShowErrorBox(Utf8ToW(d.notes).c_str());
        return 1;
    }

    // ---- F0.04: perfil C → MODO EMERGENCIA (F0.08) ----
    if (d.profile == bootstrap::RuntimeProfile::C) {
        g_bootLog.Write(nlog::SEV_WARN, "bootstrap",
                        "perfil C: sin .NET utilizable → modo emergencia nativo");
        const int rc = emergency::Run(base, sw.openFile);
        g_bootLog.Write(nlog::SEV_INFO, "bootstrap",
                        "modo emergencia terminado");
        return rc;
    }

    // ---- Perfil A/B → variante gestionada (net48 / net35) ----
    const wchar_t* target = d.profile == bootstrap::RuntimeProfile::A
                            ? kTargetNet48 : kTargetNet35;
    g_bootLog.Write(nlog::SEV_INFO, "bootstrap",
                    std::string("perfil ") + bootstrap::RuntimeProfileLabel(d.profile) +
                    " → variante " + (target == kTargetNet48 ? "net48" : "net35"));

    wchar_t targetPath[MAX_PATH];
    if (!JoinPath(base.c_str(), target, targetPath, MAX_PATH)) {
        ShowErrorBox(L"La ruta del programa es demasiado larga.");
        return 1;
    }
    if (GetFileAttributesW(targetPath) == INVALID_FILE_ATTRIBUTES) {
        wchar_t msg[600];
        swprintf_s(msg, _TRUNCATE,
            L"No se encontró el ejecutable:\n\n  %s\n\n"
            L"Verifique que el paquete esté completo y que su antivirus no "
            L"haya puesto el archivo en cuarentena.", target);
        ShowErrorBox(msg);
        return 1;
    }
    if (!VerifySidecars(targetPath)) return 1;

    DWORD childExit = 1;
    const DWORD err = LaunchAndWait(targetPath, base.c_str(), &childExit);
    if (err != 0) {
        wchar_t msg[512];
        swprintf_s(msg, _TRUNCATE,
            L"No se pudo iniciar:\n\n  %s\n\nCódigo de error del sistema: %lu",
            target, (unsigned long)err);
        ShowErrorBox(msg);
        return 1;
    }
    g_bootLog.Write(nlog::SEV_INFO, "bootstrap",
                    "variante gestionada terminada");
    return (int)childExit;
}
