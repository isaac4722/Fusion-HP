// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  NativeLog.cpp : implementación del log nativo estructurado (F0.09/§10).
//  Incluido por lumina_api.cpp (núcleo) y LuminaLauncher.cpp (bitácora de
//  arranque: SO, arquitectura, runtime, perfil y decisión — exactamente lo
//  que F0.09 exige registrar).
// ============================================================================
#include "NativeLog.h"

#include <cstdio>
#include <cstring>
#include <ctime>
#include <cctype>
#include <algorithm>
#include <mutex>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#endif

namespace lumina {
namespace nlog {

namespace {

std::mutex g_logMutex;   // el logger es thread-safe (cola de render + IPC)

bool EnsureDir(const std::string& dir) {
    if (dir.empty()) return false;
#ifdef _WIN32
    // UTF-8 → UTF-16 y CreateDirectoryW recursivo por segmentos.
    const int n = MultiByteToWideChar(CP_UTF8, 0, dir.c_str(), (int)dir.size(),
                                      nullptr, 0);
    if (n <= 0) return false;
    std::wstring w((size_t)n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, dir.c_str(), (int)dir.size(), &w[0], n);
    DWORD at = GetFileAttributesW(w.c_str());
    if (at != INVALID_FILE_ATTRIBUTES && (at & FILE_ATTRIBUTE_DIRECTORY))
        return true;
    // Crea segmento a segmento (sin SHCreatePath — shell32 innecesario).
    for (size_t i = 1; i < w.size(); ++i) {
        if (w[i] == L'\\' || w[i] == L'/') {
            std::wstring part = w.substr(0, i);
            if (!part.empty()) {
                at = GetFileAttributesW(part.c_str());
                if (at == INVALID_FILE_ATTRIBUTES)
                    CreateDirectoryW(part.c_str(), nullptr);
            }
        }
    }
    at = GetFileAttributesW(w.c_str());
    return at != INVALID_FILE_ATTRIBUTES && (at & FILE_ATTRIBUTE_DIRECTORY);
#else
    std::string cmd = "mkdir -p '";
    for (char c : dir) { if (c == '\'') cmd += "'\\''"; else cmd += c; }
    cmd += "'";
    if (std::system(cmd.c_str()) != 0) {
        // verificar existencia (mkdir -p falla raramente)
    }
    return true;   // mejor-esfuerzo; fopen fallará después si no existe
#endif
}

} // namespace

std::string SanitizeLine(const std::string& s) {
    std::string out;
    out.reserve(s.size());
    for (unsigned char c : s) {
        if (c == '\n') out += "\\n";
        else if (c == '\r') out += "\\r";
        else if (c == '\t') out += ' ';
        else out += (char)c;
    }
    return out;
}

std::string Redact(const std::string& s) {
    // Neutraliza credenciales/tokens §10.4. Coincidencias simples y
    // conservadoras (case-insensitive sobre ASCII).
    static const char* kPat[][2] = {
        {"bearer ",   "bearer [REDACTED]"},
        {"token=",    "token=[REDACTED]"},
        {"token: ",   "token: [REDACTED]"},
        {"password=", "password=[REDACTED]"},
        {"password: ","password: [REDACTED]"},
        {"secret=",   "secret=[REDACTED]"},
        {"authorization:", "authorization: [REDACTED]"},
    };
    std::string out = s;
    std::string low;
    low.reserve(s.size());
    for (char c : s) low += (char)std::tolower((unsigned char)c);
    for (auto& p : kPat) {
        const std::string needle(p[0]);
        size_t pos = 0;
        while ((pos = low.find(needle, pos)) != std::string::npos) {
            // Redactar hasta el final del token (espacio/comilla/fin).
            size_t end = pos + needle.size();
            while (end < out.size() && out[end] != ' ' && out[end] != '"' &&
                   out[end] != '\'' && out[end] != ',' && out[end] != ';')
                ++end;
            out.replace(pos, end - pos, p[1]);
            low = out;
            for (auto& ch : low) ch = (char)std::tolower((unsigned char)ch);
            pos += std::strlen(p[1]);
            if (pos > out.size()) break;
        }
    }
    return out;
}

std::string TodayUtcStamp() {
    char buf[48];
    time_t t = time(nullptr);
    tm utc{};
#ifdef _WIN32
    gmtime_s(&utc, &t);
#else
    gmtime_r(&t, &utc);
#endif
    std::snprintf(buf, sizeof(buf), "%04d%02d%02d",
                  utc.tm_year + 1900, utc.tm_mon + 1, utc.tm_mday);
    return buf;
}

std::string TimestampUtcIso() {
    char buf[64];
    time_t t = time(nullptr);
    tm utc{};
#ifdef _WIN32
    gmtime_s(&utc, &t);
#else
    gmtime_r(&t, &utc);
#endif
    std::snprintf(buf, sizeof(buf), "%04d-%02d-%02dT%02d:%02d:%02dZ",
                  utc.tm_year + 1900, utc.tm_mon + 1, utc.tm_mday,
                  utc.tm_hour, utc.tm_min, utc.tm_sec);
    return buf;
}

std::string TimestampLocalIso() {
    char buf[96];
    time_t t = time(nullptr);
    tm lt{};
    tm* ok = nullptr;
#ifdef _WIN32
    ok = localtime_s(&lt, &t) == 0 ? &lt : nullptr;
#else
    ok = localtime_r(&t, &lt);
#endif
    if (!ok) return TimestampUtcIso();
    long offMin = 0;
#ifdef _WIN32
    // _get_timezone devuelve segundos hacia UTC (GMT - local).
    long tz = 0; _get_timezone(&tz); offMin = -(tz / 60);
#else
    offMin = lt.tm_gmtoff / 60;
#endif
    char sign = offMin < 0 ? '-' : '+';
    long a = offMin < 0 ? -offMin : offMin;
    std::snprintf(buf, sizeof(buf), "%04d-%02d-%02dT%02d:%02d:%02d%c%02ld:%02ld",
                  lt.tm_year + 1900, lt.tm_mon + 1, lt.tm_mday,
                  lt.tm_hour, lt.tm_min, lt.tm_sec, sign, a / 60, a % 60);
    return buf;
}

std::string CaptureContext() {
#ifdef _WIN32
    void* frames[32];
    USHORT n = CaptureStackBackTrace(0, 32, frames, nullptr);
    std::string out;
    char b[24];
    for (USHORT i = 0; i < n; ++i) {
        if (i) out += ",";
        std::snprintf(b, sizeof(b), "%p", frames[i]);
        out += b;
    }
    return out;
#else
    return "";
#endif
}

std::string FormatEntry(const Entry& e) {
    std::string msg = Redact(SanitizeLine(e.message));
    if (msg.size() > 4096) msg = msg.substr(0, 4093) + "...";
    const char* sev = e.severity == SEV_DEBUG ? "DEBUG" :
                      e.severity == SEV_INFO  ? "INFO"  :
                      e.severity == SEV_WARN  ? "WARN"  : "ERROR";
    std::string line = TimestampUtcIso();
    line += '|'; line += TimestampLocalIso();
    line += '|'; line += sev;
    line += '|'; line += SanitizeLine(e.module);
    line += '|'; line += msg;
    if (!e.scenarioId.empty()) { line += '|'; line += e.scenarioId; }
    if (!e.elementId.empty())  { line += '|'; line += e.elementId; }
    if (e.lineIndex >= 0) {
        char b[24]; std::snprintf(b, sizeof(b), "%d", e.lineIndex);
        line += '|'; line += b;
    }
    if (!e.filePath.empty())   { line += '|'; line += SanitizeLine(e.filePath); }
    if (!e.exception.empty())  { line += '|'; line += Redact(SanitizeLine(e.exception)); }
    if (!e.callStack.empty())  { line += '|'; line += e.callStack; }
    return line;
}

/* ------------------------------------------------------------------ Log -- */

struct Log::State {
    FILE*   f = nullptr;
    std::string day;      // día abierto (YYYYMMDD)
    std::string path;     // archivo activo
};

bool Log::Open(const Options& opt) {
    std::lock_guard<std::mutex> lk(g_logMutex);
    Close();
    opt_ = opt;
    if (!EnsureDir(opt_.dir)) return false;
    state_ = new State();
    state_->day = TodayUtcStamp();
    // Nombre §10.3: lumina-YYYYMMDD.log
    std::string p = opt_.dir;
    if (p.empty() || (p.back() != '/' && p.back() != '\\')) p += '/';
    p += "lumina-" + state_->day + ".log";
#ifdef _WIN32
    const int n = MultiByteToWideChar(CP_UTF8, 0, p.c_str(), (int)p.size(),
                                      nullptr, 0);
    std::wstring w((size_t)(n > 0 ? n : 1), L'\0');
    if (n > 0)
        MultiByteToWideChar(CP_UTF8, 0, p.c_str(), (int)p.size(), &w[0], n);
    FILE* f = nullptr;
    if (_wfopen_s(&f, w.c_str(), L"a") == 0 && f) state_->f = f;
#else
    state_->f = std::fopen(p.c_str(), "a");
#endif
    state_->path = p;
    return state_->f != nullptr;
}

bool Log::Write(const Entry& e) {
    std::lock_guard<std::mutex> lk(g_logMutex);
    if (!state_) return false;
    if ((int)e.severity < (int)opt_.minLevel) { stats_.dropped++; return true; }
    // Rotación al cambiar el día (§10.3: un archivo por día).
    std::string today = TodayUtcStamp();
    if (today != state_->day) {
        Close();
        if (!Open(opt_)) return false;
        // Retención: purga de archivos > retentionDays (el núcleo borra;
        // la compresión de los intermedios la hace C# con Deflate).
        if (opt_.retentionDays > 0) {
            for (int i = opt_.retentionDays + 1; i < opt_.retentionDays + 32;
                 ++i) {
                // días candidatos a purgar: lumina-<hace i días>.log
                time_t t = time(nullptr) - (time_t)i * 86400;
                tm utc{};
#ifdef _WIN32
                gmtime_s(&utc, &t);
#else
                gmtime_r(&t, &utc);
#endif
                char day[48];
                std::snprintf(day, sizeof(day), "%04d%02d%02d",
                              utc.tm_year + 1900, utc.tm_mon + 1, utc.tm_mday);
                std::string old = opt_.dir;
                if (!old.empty() && old.back() != '/' && old.back() != '\\')
                    old += '/';
                old += "lumina-"; old += day; old += ".log";
#ifdef _WIN32
                const int n = MultiByteToWideChar(CP_UTF8, 0, old.c_str(),
                        (int)old.size(), nullptr, 0);
                if (n > 0) {
                    std::wstring w((size_t)n, L'\0');
                    MultiByteToWideChar(CP_UTF8, 0, old.c_str(), (int)old.size(),
                                        &w[0], n);
                    DeleteFileW(w.c_str());
                }
#else
                std::remove(old.c_str());
#endif
            }
        }
    }
    std::string line = FormatEntry(e);
    line += "\n";
    if (state_->f) {
        size_t n = std::fwrite(line.data(), 1, line.size(), state_->f);
        if (n == line.size()) {
            std::fflush(state_->f);           // tolerante: flush por línea
            stats_.written++;
            stats_.bytes += (int64_t)n;
            return true;
        }
        stats_.failed++;                      // disco falló: NUNCA abortar
        return false;
    }
    stats_.failed++;
    return false;
}

bool Log::Write(Severity sev, const char* module, const std::string& msg) {
    Entry e;
    e.severity = sev;
    e.module = module ? module : "";
    e.message = msg;
    return Write(e);
}

std::string Log::ActiveFile() const {
    return state_ ? state_->path : std::string();
}

void Log::Close() {
    if (state_) {
        if (state_->f) std::fclose(state_->f);
        delete state_;
        state_ = nullptr;
    }
}

} // namespace nlog
} // namespace lumina
