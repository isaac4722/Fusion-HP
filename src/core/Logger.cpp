// ============================================================================
//  Fusion-HP · Logger — registro estructurado del núcleo nativo (spdlog).
//  [SPEC §11.1] timestamp, severidad, módulo, mensaje, contexto; un archivo
//  por día; rotación 14 días; sin contenido sensible (ni letras ni credenciales).
//  v4.1.0: motor spdlog 1.12.0 (MIT, cabecera) — sink diario SINCRÓNICO y
//  hilo-seguro con retención max_files=14; SPDLOG_NO_EXCEPTIONS (nunca lanza).
//  El formato publicado `local | ISO-UTC | LEVEL | module | msg` se compone
//  aquí y viaja por el patrón %v: ni una coma del formato se mueve.
//  Degradación igual que siempre: si el destino no sirve, se registra en
//  memoria y no se molesta al usuario (validación previa del directorio).
// ============================================================================
#include "Common.h"
#include "Logger.h"
#include <shlobj.h>
#include <filesystem>
#include <spdlog/spdlog.h>
#include <spdlog/sinks/daily_file_sink.h>

namespace fs = std::filesystem;

namespace fusion {

namespace {
    std::mutex g_initMutex;
    std::shared_ptr<spdlog::logger> g_logger;
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

// Valida que el directorio admita escritura ANTES de construir el sink:
// con SPDLOG_NO_EXCEPTIONS un fallo del sink sería fatal (abort), y el
// contrato histórico del Logger es degradar en silencio, nunca tumbar el núcleo.
static bool DirWritable(const std::wstring& dir) {
    std::error_code ec;
    fs::create_directories(dir, ec);
    if (!fs::is_directory(dir, ec)) return false;
    wchar_t probe[MAX_PATH];
    swprintf_s(probe, L"%s\\core.probe", dir.c_str());
    HANDLE h = CreateFileW(probe, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                           FILE_ATTRIBUTE_TEMPORARY, nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;
    CloseHandle(h);
    DeleteFileW(probe);
    return true;
}

void Logger::Init() {
    std::lock_guard<std::mutex> lk(g_initMutex);
    if (g_logger) return;
    std::wstring dir = DataDir() + L"\\logs";
    if (!DirWritable(dir)) return;                    // degradación silenciosa
    auto sink = std::make_shared<spdlog::sinks::daily_file_sink_mt>(
        dir + L"\\core.log", /*hour*/0, /*minute*/0, /*truncate*/false, /*max_files*/14);
    sink->set_formatter(spdlog::details::make_unique<spdlog::pattern_formatter>("%v"));
    g_logger = std::make_shared<spdlog::logger>("core", sink);
    g_logger->flush_on(spdlog::level::trace);         // durabilidad por línea
}

void Logger::SetLevel(int lv) { g_minLevel = lv; }

void Logger::Write(int lv, const char* module, const std::string& message) {
    if (lv > g_minLevel) return;
    if (!g_logger) return;

    SYSTEMTIME st;
    GetLocalTime(&st);
    // ISO-UTC genuino: GetSystemTimeAsFileTime ya es UTC (v4.1.0 corrige la
    // doble conversión local que traía la versión FILE*).
    char utc[32];
    SYSTEMTIME ust; FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
    FileTimeToSystemTime(&ft, &ust);
    snprintf(utc, sizeof(utc), "%04d-%02d-%02dT%02d:%02d:%02d.%03dZ",
             ust.wYear, ust.wMonth, ust.wDay, ust.wHour, ust.wMinute, ust.wSecond, ust.wMilliseconds);

    char line[2048];
    // 2026-09-25 10:00:00.123 | 2026-09-25T14:00:00.000Z | INFO  | core.render | mensaje
    snprintf(line, sizeof(line),
             "%04d-%02d-%02d %02d:%02d:%02d.%03d | %s | %s | %s | %s",
             st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, st.wMilliseconds,
             utc, LevelName(lv), module ? module : "core", message.c_str());
    g_logger->log(spdlog::level::trace, line);
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
