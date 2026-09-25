// ============================================================================
//  Fusion-HP · Environment — detección de entorno [SPEC §4.1]
//  PASO 1 SO (RtlGetVersion; NUNCA GetVersion) · PASO 2 arquitectura
//  (IsWow64Process2 con fallback) · PASO 3 runtime .NET 3.5→4.8 por sistema
//  de archivos de solo lectura (sin Registro [REQ]).
// ============================================================================
#pragma once
#include "Common.h"

namespace fusion {

enum class NetRuntime {
    None = 0,     // Perfil C — nativo
    Net35 = 35,   // Perfil B — capa reducida
    Net4x = 40,   // Perfil A — capa completa
};

struct EnvironmentReport {
    std::wstring osName;        // "Windows 7 SP1", "Windows 11", ...
    unsigned build = 0;
    bool isServer = false;
    bool sp1OrHigher = false;
    std::wstring archProcess;   // "x86" | "x64"
    std::wstring archOs;        // "x86" | "x64"
    bool wow64 = false;
    NetRuntime net = NetRuntime::None;
    std::wstring netDetail;     // "4.8", "3.5 SP1", ""
    std::wstring profile;       // "A" | "B" | "C"
    bool portable = false;

    Json ToJson() const;
    std::wstring Summary() const;
};

class Environment {
public:
    // Ejecuta los PASOS 1-3 y devuelve el informe. Solo lectura del sistema.
    static EnvironmentReport Detect();

private:
    static void DetectOs(EnvironmentReport& r);
    static void DetectArch(EnvironmentReport& r);
    static void DetectNet(EnvironmentReport& r);
};

} // namespace fusion
