// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Bootstrap.h : detección de entorno del arranque (F0.01-F0.04 del Plan de
//  Ultra Implementación, Secciones 3.1/4.1/4.2/4.3 del documento técnico).
//
//  DISEÑO (regla del plan: núcleo detecta ANTES de cargar C#):
//   * Capa PURA de clasificación (compila y se prueba en Linux y Windows):
//       - OsClass ClassifyOs(...)            (F0.01)
//       - ArchChoice ChooseArch(...)         (F0.02)
//       - RuntimeProfile ClassifyProfile(...) (F0.03)
//     Toda la política vive aquí, sin tocar Win32: los tests del arnés
//     (native_tests.cpp) la ejercen con límites exactos (Win11 22000,
//     4.7.2=release 461808, 3.5 SP1, WOW64...).
//   * Capa de SONDAS Win32 (solo Windows): RtlGetVersion (PROHIBIDO
//     GetVersion — obsoleta), IsWow64Process2 con alternativa, CSDVersion
//     del registro para SP1 (LECTURA; la app nunca escribe registro).
//   * Sondeables también "por inyección": DetectEnvironment() acepta un
//     JSON de entrada con los valores de sonda → en Linux/CI se prueba la
//     cadena completa de decisión sin Windows.
// ============================================================================
#ifndef LUMINA_BOOTSTRAP_H
#define LUMINA_BOOTSTRAP_H

#include <string>
#include <cstdint>

namespace lumina {
namespace bootstrap {

/* ------------------------------------------------------------- F0.01 --- */
enum class OsClass {
    Older,      // < Win7: no soportado, mensaje explícito
    Win7NoSp1,  // Win7 sin SP1: mensaje de incompatibilidad + sugerir SP1
    Win7Sp1,    // mínimo soportado
    Win8,       // 8.0
    Win81,      // 8.1
    Win10,      // build < 22000
    Win11,      // build >= 22000
    Newer       // futuro: soportado como Win11+
};

// Clasifica (major, minor, build) con la política del documento técnico:
// Win7 SP1 / 8.1 / 10 / 11 (build 22000+). sp1Present solo importa en Win7.
OsClass ClassifyOs(int major, int minor, int build, bool sp1Present);

// Etiqueta corta para logs/UI: "win7sp1", "win7-nosp1", "win8.1", "win10",
// "win11", "older", "newer".
const char* OsClassLabel(OsClass c);

// true si la combinación es arrancable (Win7 SP1 en adelante).
bool OsSupported(OsClass c);

/* ------------------------------------------------------------- F0.02 --- */
// Arquitectura del proceso vs. del SO y elección de paquete x86/x64.
struct ArchFacts {
    int  processBitness;   // 32 o 64
    int  osBitness;        // 32 o 64
    bool wow64;            // proceso x86 sobre SO x64
    bool hasWow2;          // IsWow64Process2 disponible (Win10 1511+)
};
enum class ArchChoice { X86, X64 };

// Política: por defecto x64 si el SO es x64 y el proceso lo permite;
// los conmutadores explícitos del lanzador fuerzan la variante (plan §2.2).
ArchChoice ChooseArch(const ArchFacts& f, bool forceX86, bool forceX64);
const char* ArchChoiceLabel(ArchChoice a);

/* ------------------------------------------------------------- F0.03 --- */
// Hechos del runtime .NET (solo lectura — jamás se activa ni instala).
struct DotnetFacts {
    bool net35;      // NDP\v3.5 Install == 1
    bool v4Full;     // NDP\v4\Full existe
    int  v4Release;  // DWORD Release (0 si no hay)
    // net35 con SP1 se asume: Win7 SP1 lo trae de fábrica; el installer
    // 3.5 SP1 es el único distribuido desde 2011.
};
enum class RuntimeProfile { A, B, C };

// Umbrales del documento técnico §4.2 (tabla oficial NDP "Release"):
//   Perfil A = .NET 4.8 (528040) o 4.7.2+ (461808 en todos los SO con
//   installer offline; 4.7.1 es 461310 → queda fuera de A).
//   Perfil B = 3.5 SP1 … 4.6.x (4.6.2 = 394802; el máximo 4.6.x).
//   Perfil C = ausencia o unusable (nada de lo anterior).
RuntimeProfile ClassifyProfile(const DotnetFacts& f);
const char* RuntimeProfileLabel(RuntimeProfile p);

// Versión legible del runtime detectado ("4.8", "4.7.2", "4.6.2", "3.5",
// "ninguno") — para log de arranque y «Estado del sistema».
std::string DotnetReleaseLabel(const DotnetFacts& f);

/* -------------------------------------------------- entorno completo --- */
// Resultado de la detección completa (F0.01+F0.02+F0.03) + la decisión
// tomada (perfil y variante elegidas) — es exactamente lo que el plan
// pide REGISTRAR en el log de arranque (F0.09) y mostrar en
// Ayuda → Estado del sistema (F5.10).
struct EnvDecision {
    // sonda
    int         osMajor = 0, osMinor = 0, osBuild = 0;
    bool        osSp1 = false;
    OsClass     osClass = OsClass::Older;
    ArchFacts   arch;
    ArchChoice  archChoice = ArchChoice::X86;
    DotnetFacts net;
    RuntimeProfile profile = RuntimeProfile::C;
    bool        forcedArch = false;   // vino por conmutador explícito
    bool        forcedProfile = false;
    // diagnóstico humano (sin stack crudo — plan §11)
    std::string notes;                // p.ej. "Win7 sin SP1: instalar SP1"
};

// Detecta y decide. probeJson (opcional, UTF-8): inyecta los hechos para
// pruebas/Linux:
//   {"osMajor":10,"osMinor":0,"osBuild":19045,"osSp1":true,
//    "procBitness":64,"osBitness":64,"wow64":false,"hasWow2":true,
//    "net35":true,"net4Full":true,"net4Release":528040}
// Vacío → sondas reales del sistema (Windows) o el entorno actual
// (Linux: clasificado como no-Windows con nota explícita).
EnvDecision DetectEnvironment(const std::string& probeJson,
                              bool forceX86, bool forceX64,
                              bool forceProfileA, bool forceProfileB,
                              bool forceProfileC);

// Serialización JSON del entorno+decisión (log de arranque + ABI
// lumina_env_detect + Estado del sistema).
std::string EnvDecisionToJson(const EnvDecision& d);

} // namespace bootstrap
} // namespace lumina

#endif // LUMINA_BOOTSTRAP_H
