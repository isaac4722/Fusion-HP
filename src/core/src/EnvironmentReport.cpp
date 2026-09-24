// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  EnvironmentReport.cpp : informe de entorno portable (F0.09/F6.01).
//  Windows: delega en bootstrap::DetectEnvironment (sondas reales). Linux:
//  camino PURA de Bootstrap con hechos inyectados + uname(2) para etiquetas.
//  La política de perfiles A/B/C vive SOLO en Bootstrap.cpp (una fuente).
// ============================================================================
#include "EnvironmentReport.h"

#include "Bootstrap.h"
#include "NativeLog.h"

#include <nlohmann/json.hpp>

#include <cstdio>

#ifdef _WIN32
#include <windows.h>
#else
#include <sys/utsname.h>
#include <cstring>
#endif

namespace lumina {

using json = nlohmann::json;

namespace {

// Normaliza el nombre de máquina a la etiqueta del paquete (x86/x64/arm64).
std::string NormalizeMachine(const std::string& raw) {
#ifdef _WIN32
    (void)raw;
    return std::string();   // en Windows se llena desde ArchFacts
#else
    if (raw == "x86_64" || raw == "amd64") return "x64";
    if (raw == "i686" || raw == "i586" || raw == "i486" || raw == "i386")
        return "x86";
    if (raw == "aarch64" || raw == "arm64") return "arm64";
    return raw;
#endif
}

// Etiqueta de decisión de arranque (escalera §4.2 — texto humano es-VE,
// mismo registro que el log de arranque F0.09.2).
std::string BootDecisionLabel(bootstrap::RuntimeProfile p, bool osSupported) {
    if (!osSupported)
        return "No compatible: aviso explícito y salida ordenada";
    switch (p) {
        case bootstrap::RuntimeProfile::A:
            return "Arranque gestionado completo (perfil A: .NET 4.7.2+)";
        case bootstrap::RuntimeProfile::B:
            return "Arranque gestionado reducido (perfil B: .NET 3.5-4.6.x)";
        case bootstrap::RuntimeProfile::C:
            return "Modo emergencia nativo (perfil C: sin .NET utilizable)";
    }
    return "?";
}

} // namespace

EnvironmentReport MakeEnvironmentReport(const std::string& probeJson,
                                        bool forceX86, bool forceX64,
                                        bool forceA, bool forceB,
                                        bool forceC) {
    EnvironmentReport r;
    r.startedAtUtc = nlog::TimestampUtcIso();

    // La DECISIÓN siempre viene de Bootstrap (una sola fuente de verdad):
    // con probeJson es el camino puro; en Windows sin probe ejecuta las
    // sondas reales; en Linux sin probe declara el entorno no-Windows.
    const bootstrap::EnvDecision d = bootstrap::DetectEnvironment(
        probeJson, forceX86, forceX64, forceA, forceB, forceC);

    r.osClass     = bootstrap::OsClassLabel(d.osClass);
    r.osSupported = bootstrap::OsSupported(d.osClass);
    r.archProcess = d.arch.processBitness == 64 ? "x64" : "x86";
    r.archMachine = d.arch.osBitness == 64 ? "x64" : "x86";
    r.wow64       = d.arch.wow64;
    r.netVersion  = bootstrap::DotnetReleaseLabel(d.net);
    r.profile     = bootstrap::RuntimeProfileLabel(d.profile);
    r.forcedProfile = d.forcedProfile;
    r.forcedArch    = d.forcedArch;
    r.notes         = d.notes;
    r.bootDecision  = BootDecisionLabel(d.profile, r.osSupported);

#ifdef _WIN32
    // Etiqueta humana del SO (sonda real disponible).
    char buf[96];
    std::snprintf(buf, sizeof(buf), "Windows %s (build %d.%d.%d)",
                  bootstrap::OsClassLabel(d.osClass), d.osMajor, d.osMinor,
                  d.osBuild);
    r.osLabel = buf;
    if (d.osClass == bootstrap::OsClass::Win7NoSp1)
        r.osLabel = "Windows 7 sin SP1 (no compatible)";
    // En WOW64 el SO anfitrión es x64 aunque el proceso sea x86.
    if (d.arch.wow64) r.archMachine = "x64";
#else
    // Linux (arnés/tests): etiqueta desde uname(2) — sin tocar la política.
    struct utsname u;
    if (uname(&u) == 0) {
        r.osLabel = std::string(u.sysname) + " " + u.release;
        const std::string m = NormalizeMachine(u.machine);
        if (!m.empty()) r.archMachine = m;
    } else {
        r.osLabel = "POSIX (uname no disponible)";
    }
    if (r.osClass == "older" && d.osMajor == 0)
        r.osClass = "linux";   // sin versión: etiqueta de plataforma, no "older"
    if (probeJson.empty()) {
        r.netVersion = "n/a";  // sin .NET en el entorno de pruebas Linux
        r.profile = "";        // sin .NET en el entorno de pruebas
        r.bootDecision =
            "No aplica: entorno de pruebas no Windows (hechos inyectables)";
    }
    // Con probeJson NO se pisa nada: es el camino puro de hechos inyectados
    // (mismo contrato que Windows — una sola política, F0.03/F0.04).
#endif

    return r;
}

std::string EnvironmentReport::ToJson() const {
    json j;
    json os;
    os["label"]     = osLabel;
    os["class"]     = osClass;
    os["supported"] = osSupported;
    j["os"] = os;

    json arch;
    arch["process"] = archProcess;
    arch["machine"] = archMachine;
    arch["wow64"]   = wow64;
    arch["forced"]  = forcedArch;
    j["arch"] = arch;

    json net;
    net["version"] = netVersion;
    net["profile"] = profile;
    net["forced"]  = forcedProfile;
    j["net"] = net;

    j["bootDecision"] = bootDecision;
    j["startedAtUtc"] = startedAtUtc;
    j["notes"]        = notes;
    return j.dump();
}

} // namespace lumina
