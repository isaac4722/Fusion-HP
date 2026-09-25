// ============================================================================
//  Fusion-HP · Common.cpp — utilidades comunes (rutas, cadenas, color)
// ============================================================================
#include "Common.h"
#include <shlobj.h>
#include <filesystem>

namespace fs = std::filesystem;

namespace fusion {

std::wstring ToWide(const std::string& utf8) {
    if (utf8.empty()) return std::wstring();
    int n = MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), (int)utf8.size(), nullptr, 0);
    std::wstring out((size_t)n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), (int)utf8.size(), &out[0], n);
    return out;
}

std::string ToUtf8(const std::wstring& wide) {
    if (wide.empty()) return std::string();
    int n = WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), (int)wide.size(), nullptr, 0, nullptr, nullptr);
    std::string out((size_t)n, '\0');
    WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), (int)wide.size(), &out[0], n, nullptr, nullptr);
    return out;
}

std::string NarrowCopy(const std::string& s) { return s; }

uint32_t ParseColor(const std::string& hex, uint32_t fallback) {
    if (hex.size() != 7 && hex.size() != 9) return fallback;
    if (hex[0] != '#') return fallback;
    try {
        if (hex.size() == 7) { // #RRGGBB → alpha 255
            uint32_t v = (uint32_t)std::stoul(hex.substr(1), nullptr, 16);
            return 0xFF000000u | v;
        }
        // #AARRGGBB
        return (uint32_t)std::stoul(hex.substr(1), nullptr, 16);
    } catch (...) {
        return fallback;
    }
}

bool IsPortableMode() {
    wchar_t exe[MAX_PATH];
    if (!GetModuleFileNameW(nullptr, exe, MAX_PATH)) return false;
    fs::path p(exe);
    fs::path flag = p.parent_path() / L"portable.flag";
    fs::path datos = p.parent_path() / L"datos";
    std::error_code ec;
    return fs::exists(flag, ec) || fs::exists(datos, ec);
}

std::wstring ExeDir() {
    wchar_t exe[MAX_PATH];
    if (!GetModuleFileNameW(nullptr, exe, MAX_PATH)) return L".";
    fs::path p(exe);
    return p.parent_path().wstring();
}

std::wstring DataDir() {
    // [SPEC §4.4] %APPDATA%\FusionHP instalado · carpeta del programa (datos/) portable.
    // NUNCA el Registro [REQ].
    if (IsPortableMode()) {
        std::error_code ec;
        fs::create_directories(ExeDir() + L"\\datos", ec);
        return ExeDir() + L"\\datos";
    }
    PWSTR roaming = nullptr;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_RoamingAppData, KF_FLAG_DEFAULT, nullptr, &roaming)) && roaming) {
        fs::path dir = fs::path(roaming) / L"FusionHP";
        CoTaskMemFree(roaming);
        std::error_code ec;
        fs::create_directories(dir, ec);
        return dir.wstring();
    }
    return ExeDir();
}

} // namespace fusion
