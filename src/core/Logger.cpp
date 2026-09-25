// ============================================================================
//  Fusion-HP · Logger — registro estructurado del núcleo nativo.
//  [SPEC §11.1] timestamp, severidad, módulo, mensaje, contexto; un archivo
//  por día; rotación 14 días; sin contenido sensible (ni letras ni credenciales).
//  En perfil C escribe a archivo plano; la capa C# registra en el mismo formato.
// ============================================================================
#include "Common.h"
#include "Logger.h"
#include <shlobj.h>
#include <filesystem>

namespace fs = std::filesystem;

namespace fusion {

namespace {
    std::mutex g_logMutex;
    std::wstring g_logDir;
    FILE* g_logFile = nullptr;
    int g_curDay = -1;
    int g_minLevel = 0; // 0=ERROR 1=WARN 2=INFO 3=DEBUG
}

static const char* LevelName(int lv) {
    switch (lv) {
        case 0: return "ERROR";
        case 1: return "WARN ";
        case 2: return "INFO ";
        default: return "DEBUG";
    }
}

void Logger::Init() {
    std::lock_guard<std::mutex> lk(g_logMutex);
    if (g_logDir.empty()) {
        g_logDir = DataDir() + L"\\logs";
        std::error_code ec;
        fs::create_directories(g_logDir, ec);
    }
    RotateIfNeeded();
}

void Logger::SetLevel(int lv) { g_minLevel = lv; }

void Logger::RotateIfNeeded() {
    // Llamado con lock tomado
    SYSTEMTIME stNow;
    GetLocalTime(&stNow);
    int today = stNow.wYear * 10000 + stNow.wMonth * 100 + stNow.wDay;
    if (g_logFile && today == g_curDay) return;
    if (g_logFile) { fclose(g_logFile); g_logFile = nullptr; }

    // Rotación: conservar 14 días [SPEC §11.1]
    std::error_code ec;
    std::vector<fs::path> old;
    for (auto& e : fs::directory_iterator(g_logDir, ec)) {
        if (!e.is_regular_file(ec)) continue;
        auto name = e.path().filename().wstring();
        if (name.rfind(L"core-", 0) == 0 && name.size() >= 14) {
            old.push_back(e.path());
        }
    }
    (void)0;
    if (old.size() > 14) {
        std::sort(old.begin(), old.end());
        size_t excess = old.size() - 14;
        for (size_t i = 0; i < excess; i++) fs::remove(old[i], ec);
    }

    wchar_t name[64];
    swprintf_s(name, L"core-%04d%02d%02d.log", stNow.wYear, stNow.wMonth, stNow.wDay);
    g_logFile = _wfsopen((g_logDir + L"\\" + name).c_str(), L"a, ccs=UTF-8", _SH_DENYNO);
    g_curDay = today;
}

void Logger::Write(int lv, const char* module, const std::string& message) {
    if (lv > g_minLevel) return;
    std::lock_guard<std::mutex> lk(g_logMutex);
    RotateIfNeeded();
    if (!g_logFile) return;

    SYSTEMTIME st;
    GetLocalTime(&st);
    char utc[32];
    SYSTEMTIME ust; FILETIME ft, uft;
    GetSystemTimeAsFileTime(&ft);
    FileTimeToLocalFileTime(&ft, &uft);
    FileTimeToSystemTime(&uft, &ust);
    snprintf(utc, sizeof(utc), "%04d-%02d-%02dT%02d:%02d:%02d.%03dZ",
             ust.wYear, ust.wMonth, ust.wDay, ust.wHour, ust.wMinute, ust.wSecond, ust.wMilliseconds);

    // 2026-09-25 10:00:00.123 | 2026-09-25T14:00:00.000Z | INFO  | core.render | mensaje
    fwprintf(g_logFile, L"%04d-%02d-%02d %02d:%02d:%02d.%03d | %hs | %hs | %hs | %hs\n",
             st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, st.wMilliseconds,
             utc, LevelName(lv), module ? module : "core", message.c_str());
    fflush(g_logFile);
}

void Logger::Error(const char* module, const std::string& msg)   { Write(0, module, msg); }
void Logger::Warn(const char* module, const std::string& msg)    { Write(1, module, msg); }
void Logger::Info(const char* module, const std::string& msg)    { Write(2, module, msg); }
void Logger::Debug(const char* module, const std::string& msg)   { Write(3, module, msg); }

// Stack trace de excepción SEH en texto plano (para el log técnico) [SPEC §11.1]
void Logger::LogException(const char* module, const char* where, unsigned int code, EXCEPTION_POINTERS* ep) {
    char buf[256];
    snprintf(buf, sizeof(buf), "excepcion no controlada en %s (codigo 0x%08X)", where ? where : "?", code);
    Write(0, module, buf);
    if (ep && ep->ExceptionRecord) {
        snprintf(buf, sizeof(buf), "  ExceptionAddress=%p ExceptionFlags=0x%08X NumberParameters=%u",
                 ep->ExceptionRecord->ExceptionAddress, ep->ExceptionRecord->ExceptionFlags,
                 (unsigned)ep->ExceptionRecord->NumberParameters);
        Write(0, module, buf);
    }
}

} // namespace fusion
