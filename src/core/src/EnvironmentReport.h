// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  EnvironmentReport.h : informe de entorno de arranque (F0.09/F6.01 — plan
//  §10: registrar SO, arquitectura, runtime, perfil y decisión de arranque).
//
//  DISEÑO:
//   * PORTABLE (Linux + Win32). NO duplica la lógica de detección Win32:
//     delega en lumina::bootstrap::DetectEnvironment (Bootstrap.cpp) — una
//     sola fuente de verdad para la política de perfiles A/B/C.
//   * En Windows Bootstrap hace las sondas reales (RtlGetVersion,
//     IsWow64Process2, NDP de solo lectura). En Linux el arnés portable
//     inyecta los hechos via probeJson (camino PURA) y este módulo completa
//     osLabel/archMachine con uname(2).
//   * startedAtUtc usa nlog::TimestampUtcIso (mismo formato §10.1 del log).
//   * ToJson() es el JSON que consumen el log de arranque, «Estado del
//     sistema» y los tests del arnés (docs/verification/core-local.md).
// ============================================================================
#ifndef LUMINA_ENVIRONMENTREPORT_H
#define LUMINA_ENVIRONMENTREPORT_H

#include <string>

namespace lumina {

struct EnvironmentReport {
    std::string osLabel;        // "Windows 11 (build 22631)" / "Linux 6.8.0-45-generic"
    std::string osClass;        // etiqueta bootstrap: "win11","win10","win7sp1",... "linux"
    std::string archProcess;    // "x86" | "x64" | "arm64"
    std::string archMachine;    // SO anfitrión (uname -m normalizado en Linux)
    bool        wow64 = false;  // proceso x86 sobre SO x64
    std::string netVersion;     // "4.8", "4.7.2+", "3.5", "ninguno", "n/a"
    std::string profile;        // "A" | "B" | "C" | "" (no aplica)
    bool        forcedProfile = false;   // vino por conmutador explícito
    bool        forcedArch   = false;
    bool        osSupported  = false;     // arrancable según la escalera §4.2
    std::string bootDecision;   // decisión de arranque (etiqueta es-VE)
    std::string startedAtUtc;   // ISO-8601 UTC ("2026-09-24T00:00:00Z")
    std::string notes;          // diagnóstico humano (sin stack crudo — §11)

    // Serialización JSON (estilo Bootstrap.EnvDecisionToJson):
    // {"os":{"label","class","supported"},"arch":{"process","machine","wow64","forced"},
    //  "net":{"version","profile","forced"},"bootDecision","startedAtUtc","notes"}
    std::string ToJson() const;
};

// Construye el informe. probeJson (UTF-8, opcional) inyecta los hechos de
// sonda para pruebas/Linux — mismo contrato que bootstrap::DetectEnvironment.
// Los conmutadores de fuerza replican los del lanzador (plan §2.2).
EnvironmentReport MakeEnvironmentReport(const std::string& probeJson,
                                        bool forceX86, bool forceX64,
                                        bool forceProfileA, bool forceProfileB,
                                        bool forceProfileC);

} // namespace lumina
#endif // LUMINA_ENVIRONMENTREPORT_H
