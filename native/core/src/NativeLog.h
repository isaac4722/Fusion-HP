// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  NativeLog.h : log nativo estructurado (F0.09, plan §10).
//
//  Formato de entrada (§10.1 — una línea = un registro, campos con '|'):
//    ts_utc | ts_local | severity | module | message | scenario_id |
//    element_id | line_index | file_path | exception | call_stack
//  Severities: ERROR, WARN, INFO, DEBUG. En Live el nivel mínimo es INFO;
//  DEBUG se activa desde Diagnóstico (SetLevel).
//
//  §10.2 Ubicación: portable «.\logs», instalado «%APPDATA%\AppHibrida\logs»
//    (la ruta la decide el llamador y se inyecta por Open()).
//  §10.3 Rotación: un archivo por día (lumina-YYYYMMDD.log), retención 14
//    días (compresión de los anteriores la realiza la capa C# con Deflate al
//    arrancar — el núcleo no incluye zlib: separación documentada).
//  §10.4 Privacidad: NUNCA letras, versículos, credenciales, tokens ni
//    contraseñas. Redacción activa de patrones (Bearer, token=, password=)
//    y campo message limitado en tamaño.
//  §11: severidad + módulo + contexto + call stack de excepciones no
//    controladas (CaptureContext() usa CaptureStackBackTrace en Win32;
//    direcciones hex — sin dbghelp, sin dependencias nuevas).
// ============================================================================
#ifndef LUMINA_NATIVELOG_H
#define LUMINA_NATIVELOG_H

#include <string>
#include <cstdint>

namespace lumina {
namespace nlog {

enum Severity { SEV_DEBUG = 0, SEV_INFO = 1, SEV_WARN = 2, SEV_ERROR = 3 };

// Entrada estructurada completa (§10.1). Los campos opcionales se dejan
// vacíos; el serializador omite los vacíos EXCEPTO los 5 obligatorios
// (ts_utc, ts_local, severity, module, message).
struct Entry {
    Severity    severity = SEV_INFO;
    std::string module;      // "bootstrap" | "render" | "ipc" | "engine" ...
    std::string message;     // técnico; JAMÁS contenido proyectable
    std::string scenarioId;
    std::string elementId;
    int32_t     lineIndex = -1;
    std::string filePath;    // ruta (permitida por §10.4 «Incluir: rutas»)
    std::string exception;   // resumen de excepción no controlada
    std::string callStack;   // direcciones hex (CaptureContext)
};

// Escapa una línea de log a un solo renglón seguro ('\n' → "\\n").
std::string SanitizeLine(const std::string& s);

// Redacción §10.4: neutraliza patrones de credenciales/tokens en cualquier
// mensaje. "Authorization: Bearer ABC123" → "Authorization: Bearer [REDACT]".
std::string Redact(const std::string& s);

// Serializa la entrada completa a la línea del día (formato §10.1).
std::string FormatEntry(const Entry& e);

// Día actual en UTC "YYYYMMDD" (independiente de plataforma).
std::string TodayUtcStamp();

// Fecha ISO-8601 UTC ("2026-09-24T00:00:00Z") y local con offset.
std::string TimestampUtcIso();
std::string TimestampLocalIso();

// Captura de contexto de excepción no controlada (Win32:
// CaptureStackBackTrace → hasta 32 direcciones hex; otras plataformas:
// cadena vacía). Para el manejador de crash del núcleo y del launcher.
std::string CaptureContext();

// Registro en disco con rotación y retención. Sin estado global: el
// llamador posee el handle (POO plana, ABI-safe).
class Log {
public:
    struct Options {
        std::string dir;           // carpeta de logs (§10.2, la decide el host)
        int         retentionDays = 14;   // §10.3
        Severity    minLevel = SEV_INFO;  // §10.1: Live ⇒ INFO mínimo
        int64_t     maxMessageChars = 4096;
    };

    Log() {}
    ~Log() { Close(); }

    // Abre/crea la carpeta y el archivo del día. Tolerante a fallos de
    // disco (§10.3): si el directorio no puede crearse, el log queda
    // «mejor-esfuerzo» (Write() devuelve false sin abortar NUNCA al núcleo).
    bool Open(const Options& opt);
    bool IsOpen() const { return state_ != nullptr; }

    // Escribe una entrada (rota al cambiar de día; purga >14 días).
    // Devuelve false solo si el disco falló (quedando operativo).
    bool Write(const Entry& e);

    // Conveniencia: Write con severity/module/message.
    bool Write(Severity sev, const char* module, const std::string& msg);

    // Cambia el nivel mínimo (Diagnóstico activa DEBUG — §10.1).
    void SetLevel(Severity s) { opt_.minLevel = s; }

    // Ruta del archivo activo (para Diagnóstico empaquetado §10.5).
    std::string ActiveFile() const;

    // Estadísticas para «Estado del sistema» (F5.10): entradas escritas,
    // bytes, fallos de escritura, entradas descartadas por nivel.
    struct Stats {
        int64_t written = 0, failed = 0, dropped = 0, bytes = 0;
    };
    Stats StatsSnapshot() const { return stats_; }

    void Close();

private:
    Options opt_;
    struct State;              // FILE* + día abierto (oculto: sin <stdio> en ABI)
    State* state_ = nullptr;
    Stats  stats_;
};

} // namespace nlog
} // namespace lumina

#endif // LUMINA_NATIVELOG_H
