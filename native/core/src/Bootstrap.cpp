// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Bootstrap.cpp : capa pura de clasificación + sondas Win32 de solo lectura.
//  Incluido por lumina_api.cpp (ABI del núcleo) y por LuminaLauncher.cpp
//  (el lanzador necesita EXACTAMENTE la misma política — una sola fuente de
//  verdad, sin tocar CMake: inclusión directa, no nuevo target).
// ============================================================================
#include "Bootstrap.h"

#include <cstring>
#include <cstdio>
#include <cstdlib>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#endif

// ----------------------------------------------------------------------------
// Parser JSON mínimo (el amalgamado nlohmann vive en el núcleo, pero el
// LAUNCHER no puede depender de él: es un binario autónomo. Aquí se parsea
// solo un objeto plano de números/booleanos).
// ----------------------------------------------------------------------------
namespace lumina {
namespace bootstrap {
namespace {

// Busca "clave":valor (numérico o true/false) en un objeto plano. Robusto y
// deliberadamente simple: solo lo usa DetectEnvironment con JSON del propio
// programa (tests) — nunca archivos de usuario.
bool FindNumber(const char* s, const char* key, double* out) {
    if (!s) return false;
    char pat[64];
    std::snprintf(pat, sizeof(pat), "\"%s\"", key);
    const char* p = std::strstr(s, pat);
    if (!p) return false;
    p += std::strlen(pat);
    while (*p == ' ' || *p == '\t') ++p;
    if (*p != ':') return false;
    ++p;
    while (*p == ' ' || *p == '\t') ++p;
    char* end = nullptr;
    double v = std::strtod(p, &end);
    if (end == p) return false;
    *out = v;
    return true;
}
bool FindBool(const char* s, const char* key, bool* out) {
    if (!s) return false;
    char pat[64];
    std::snprintf(pat, sizeof(pat), "\"%s\"", key);
    const char* p = std::strstr(s, pat);
    if (!p) return false;
    p += std::strlen(pat);
    while (*p == ' ' || *p == '\t') ++p;
    if (*p != ':') return false;
    ++p;
    while (*p == ' ' || *p == '\t') ++p;
    if (std::strncmp(p, "true", 4) == 0)  { *out = true;  return true; }
    if (std::strncmp(p, "false", 5) == 0) { *out = false; return true; }
    return false;
}

// Escapa un string para JSON de salida.
void Jstr(std::string& out, const std::string& s) {
    out += '"';
    for (char c : s) {
        switch (c) {
            case '"':  out += "\\\""; break;
            case '\\': out += "\\\\"; break;
            case '\n': out += "\\n";  break;
            case '\r': out += "\\r";  break;
            case '\t': out += "\\t";  break;
            default:
                if ((unsigned char)c < 0x20) {
                    char b[8]; std::snprintf(b, sizeof(b), "\\u%04x", c);
                    out += b;
                } else out += c;
        }
    }
    out += '"';
}

} // namespace

/* ------------------------------------------------------------- F0.01 --- */

OsClass ClassifyOs(int major, int minor, int build, bool sp1Present) {
    // Windows 11: mismo 10.0, build 22000 o posterior (tabla del doc §4.3).
    if (major == 10 && build >= 22000) return OsClass::Win11;
    if (major == 10) return OsClass::Win10;
    if (major == 6 && minor == 3)  return OsClass::Win81;
    if (major == 6 && minor == 2)  return OsClass::Win8;
    if (major == 6 && minor == 1)  return sp1Present ? OsClass::Win7Sp1
                                                     : OsClass::Win7NoSp1;
    if (major > 10) return OsClass::Newer;                       // futuro
    if (major == 6 && minor > 3) return OsClass::Newer;
    return OsClass::Older;             // < Win7 (Vista/XP): no soportado
}

const char* OsClassLabel(OsClass c) {
    switch (c) {
        case OsClass::Older:    return "older";
        case OsClass::Win7NoSp1:return "win7-nosp1";
        case OsClass::Win7Sp1:  return "win7sp1";
        case OsClass::Win8:     return "win8";
        case OsClass::Win81:    return "win8.1";
        case OsClass::Win10:    return "win10";
        case OsClass::Win11:    return "win11";
        case OsClass::Newer:    return "newer";
    }
    return "?";
}

bool OsSupported(OsClass c) {
    return c == OsClass::Win7Sp1 || c == OsClass::Win8 || c == OsClass::Win81 ||
           c == OsClass::Win10  || c == OsClass::Win11 || c == OsClass::Newer;
}

/* ------------------------------------------------------------- F0.02 --- */

ArchChoice ChooseArch(const ArchFacts& f, bool forceX86, bool forceX64) {
    if (forceX86) return ArchChoice::X86;   // conmutador explícito (§2.2)
    if (forceX64) return ArchChoice::X64;
    // Default: binario del proceso. El launcher es la variante misma que
    // corre; en WOW64 con paquete x86 disponible se prefiere x86 (el SO
    // puede ejecutar ambos; el doc §1.5 mantiene x86 como mínimo común).
    if (f.osBitness == 64 && f.processBitness == 64) return ArchChoice::X64;
    return ArchChoice::X86;
}

const char* ArchChoiceLabel(ArchChoice a) {
    return a == ArchChoice::X64 ? "x64" : "x86";
}

/* ------------------------------------------------------------- F0.03 --- */

RuntimeProfile ClassifyProfile(const DotnetFacts& f) {
    // Perfil A: 4.7.2+ (Release >= 461808, tabla NDP; 4.7.1=461310 queda B).
    if (f.v4Full && f.v4Release >= 461808) return RuntimeProfile::A;
    // Perfil B: 3.5 SP1 - 4.6.x (cualquier 4.x por debajo de 4.7.2, o solo
    // 3.5 SP1).
    if (f.net35) return RuntimeProfile::B;
    if (f.v4Full && f.v4Release > 0 && f.v4Release < 461808)
        return RuntimeProfile::B;
    // Perfil C: ausencia o unusable.
    return RuntimeProfile::C;
}

const char* RuntimeProfileLabel(RuntimeProfile p) {
    switch (p) {
        case RuntimeProfile::A: return "A";
        case RuntimeProfile::B: return "B";
        case RuntimeProfile::C: return "C";
    }
    return "?";
}

std::string DotnetReleaseLabel(const DotnetFacts& f) {
    if (f.v4Full && f.v4Release >= 528040) return "4.8+";
    if (f.v4Full && f.v4Release >= 461808) return "4.7.2+";
    if (f.v4Full && f.v4Release >= 394802) return "4.6.2";
    if (f.v4Full && f.v4Release >= 393295) return "4.6.1";
    if (f.v4Full && f.v4Release > 0)       return "4.x";
    if (f.net35)                            return "3.5";
    return "ninguno";
}

/* -------------------------------------------------- entorno completo --- */

EnvDecision DetectEnvironment(const std::string& probeJson,
                              bool forceX86, bool forceX64,
                              bool forceA, bool forceB, bool forceC) {
    EnvDecision d;

    int   osMajor = 0, osMinor = 0, osBuild = 0;
    bool  osSp1 = false;
    int   procBitness = 32, osBitness = 32;
    bool  wow64 = false, hasWow2 = false;
    bool  net35 = false, v4Full = false;
    double rel = 0.0;

    const bool injected = !probeJson.empty();
    if (injected) {
        double v;
        if (FindNumber(probeJson.c_str(), "osMajor", &v)) osMajor = (int)v;
        if (FindNumber(probeJson.c_str(), "osMinor", &v)) osMinor = (int)v;
        if (FindNumber(probeJson.c_str(), "osBuild", &v)) osBuild = (int)v;
        if (FindBool(probeJson.c_str(),  "osSp1", &osSp1)) {}
        if (FindNumber(probeJson.c_str(), "procBitness", &v)) procBitness = (int)v;
        if (FindNumber(probeJson.c_str(), "osBitness", &v))   osBitness = (int)v;
        if (FindBool(probeJson.c_str(),  "wow64", &wow64))    {}
        if (FindBool(probeJson.c_str(),  "hasWow2", &hasWow2)){}
        if (FindBool(probeJson.c_str(),  "net35", &net35))    {}
        if (FindBool(probeJson.c_str(),  "net4Full", &v4Full)){}
        if (FindNumber(probeJson.c_str(), "net4Release", &rel)) {}
    } else {
#ifdef _WIN32
        // ---- F0.01: RtlGetVersion (GetVersion PROHIBIDA: obsoleta) -------
        typedef LONG (WINAPI *RtlGetVersionFn)(void*);
        HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
        if (ntdll) {
            RtlGetVersionFn fn = (RtlGetVersionFn)(void*)GetProcAddress(
                ntdll, "RtlGetVersion");
            if (fn) {
                // RTL_OSVERSIONINFOW == OSVERSIONINFOW con dwOSVersionInfoSize
                OSVERSIONINFOW vi; ZeroMemory(&vi, sizeof(vi));
                vi.dwOSVersionInfoSize = sizeof(vi);
                if (fn(&vi) == 0) {
                    osMajor = (int)vi.dwMajorVersion;
                    osMinor = (int)vi.dwMinorVersion;
                    osBuild = (int)vi.dwBuildNumber;
                    // szCSDVersion = "Service Pack 1" en Win7 SP1.
                    if (vi.szCSDVersion && vi.szCSDVersion[0])
                        osSp1 = (wcsstr(vi.szCSDVersion, L"Service Pack") != nullptr);
                }
            }
        }
        // Win7 sin SP1 no rellena CSDVersion por RtlGetVersion en algunos
        // parches: confirmación de solo lectura desde el registro (jamás se
        // ESCRIBE registro).
        if (osMajor == 6 && osMinor == 1 && !osSp1) {
            HKEY k = nullptr;
            if (RegOpenKeyExW(HKEY_LOCAL_MACHINE,
                    L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", 0,
                    KEY_READ | KEY_WOW64_64KEY, &k) == ERROR_SUCCESS) {
                WCHAR sp[64]; DWORD cb = sizeof(sp); DWORD type = 0;
                if (RegQueryValueExW(k, L"CSDVersion", nullptr, &type,
                        (LPBYTE)sp, &cb) == ERROR_SUCCESS && type == REG_SZ) {
                    osSp1 = (wcsstr(sp, L"Service Pack 1") != nullptr);
                }
                RegCloseKey(k);
            }
        }

        // ---- F0.02: IsWow64Process2 con alternativa compatible -----------
        BOOL wow = FALSE;
        typedef BOOL (WINAPI *Wow2Fn)(HANDLE, USHORT*, USHORT*);
        HMODULE k32 = GetModuleHandleW(L"kernel32.dll");
        if (k32) {
            Wow2Fn wow2 = (Wow2Fn)(void*)GetProcAddress(k32, "IsWow64Process2");
            if (wow2) {
                USHORT process = 0, machine = 0;
                if (wow2(GetCurrentProcess(), &process, &machine)) {
                    hasWow2 = true;
                    // IMAGE_FILE_MACHINE_UNKNOWN en x86-on-x64 → WOW64.
                    if (process == 0xFFFF && machine != 0) {
                        wow64 = true;
                        procBitness = 32;
                        osBitness = (machine == 0x8664) ? 64 : 64;
                    } else {
                        procBitness = (process == 0x8664) ? 64 : 32;
                        osBitness = procBitness;
                    }
                }
            }
        }
        if (!hasWow2) {
            // Alternativa compatible (Win7+): IsWow64Process clásico.
            if (IsWow64Process(GetCurrentProcess(), &wow) && wow) {
                wow64 = true;
                procBitness = 32;
                osBitness = 64;
            } else {
                SYSTEM_INFO si; GetNativeSystemInfo(&si);
                osBitness = (si.wProcessorArchitecture ==
                             PROCESSOR_ARCHITECTURE_AMD64) ? 64 : 32;
                procBitness = sizeof(void*) * 8;
            }
        }

        // ---- F0.03: plumbing CLR de SOLO LECTURA (nunca se activa) -------
        // .NET 3.5: NDP\v3.5 Install == 1
        HKEY k = nullptr;
        if (RegOpenKeyExW(HKEY_LOCAL_MACHINE,
                L"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v3.5", 0,
                KEY_READ | KEY_WOW64_64KEY, &k) == ERROR_SUCCESS) {
            DWORD inst = 0, cb = sizeof(inst), type = 0;
            if (RegQueryValueExW(k, L"Install", nullptr, &type,
                    (LPBYTE)&inst, &cb) == ERROR_SUCCESS && type == REG_DWORD)
                net35 = (inst == 1);
            RegCloseKey(k);
        }
        // .NET 4.x: NDP\v4\Full Release (mínimo 4.0 = 0/clave sin Release).
        if (RegOpenKeyExW(HKEY_LOCAL_MACHINE,
                L"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full", 0,
                KEY_READ | KEY_WOW64_64KEY, &k) == ERROR_SUCCESS) {
            DWORD r = 0, cb = sizeof(r), type = 0;
            if (RegQueryValueExW(k, L"Release", nullptr, &type,
                    (LPBYTE)&r, &cb) == ERROR_SUCCESS && type == REG_DWORD) {
                v4Full = true;
                rel = (double)r;
            }
            RegCloseKey(k);
        }
#else
        // Linux (arnés): entorno no-Windows declarado con nota explícita.
        d.notes = "entorno no Windows (arnés): detección Win32 no aplicable";
        osMajor = 0; osMinor = 0; osBuild = 0;
        procBitness = (int)(sizeof(void*) * 8);
        osBitness = procBitness;
#endif
    }

    d.osMajor = osMajor; d.osMinor = osMinor; d.osBuild = osBuild;
    d.osSp1 = osSp1;
    d.osClass = ClassifyOs(osMajor, osMinor, osBuild, osSp1);
    d.arch.processBitness = procBitness;
    d.arch.osBitness = osBitness;
    d.arch.wow64 = wow64;
    d.arch.hasWow2 = hasWow2;
    d.archChoice = ChooseArch(d.arch, forceX86, forceX64);
    d.forcedArch = forceX86 || forceX64;
    d.net.net35 = net35;
    d.net.v4Full = v4Full;
    d.net.v4Release = (int)rel;
    d.profile = ClassifyProfile(d.net);
    d.forcedProfile = forceA || forceB || forceC;
    if (forceA) d.profile = RuntimeProfile::A;
    else if (forceB) d.profile = RuntimeProfile::B;
    else if (forceC) d.profile = RuntimeProfile::C;

    // Mensajes humanos (nunca stack crudo — política §11 del plan).
    switch (d.osClass) {
        case OsClass::Win7NoSp1:
            d.notes = "Windows 7 sin Service Pack 1: el programa exige Win7 "
                      "SP1. Instale SP1 (gratuito, Microsoft) y vuelva a "
                      "ejecutar. No se modificó nada del sistema.";
            break;
        case OsClass::Older:
            d.notes = "Sistema operativo anterior a Windows 7: no compatible. "
                      "Se requiere Windows 7 SP1 o posterior.";
            break;
        default: break;
    }
    if (d.profile == RuntimeProfile::C && OsSupported(d.osClass) &&
        d.notes.empty()) {
        d.notes = "Sin .NET Framework utilizable: se inicia el MODO EMERGENCIA "
                  "nativo (perfil C) — texto, imagen y video desde el núcleo. "
                  "Para todas las funciones instale .NET 4.8 offline "
                  "(https://dotnet.microsoft.com/download/dotnet-framework/"
                  "net48). El programa nunca descarga ni instala .NET por sí "
                  "solo.";
    }
    return d;
}

std::string EnvDecisionToJson(const EnvDecision& d) {
    char buf[512];
    std::string out = "{";
    std::snprintf(buf, sizeof(buf),
        "\"os\":{\"major\":%d,\"minor\":%d,\"build\":%d,\"sp1\":%s,"
        "\"class\":\"%s\",\"supported\":%s},",
        d.osMajor, d.osMinor, d.osBuild, d.osSp1 ? "true" : "false",
        OsClassLabel(d.osClass), OsSupported(d.osClass) ? "true" : "false");
    out += buf;
    std::snprintf(buf, sizeof(buf),
        "\"arch\":{\"process\":%d,\"os\":%d,\"wow64\":%s,"
        "\"wow2\":%s,\"choice\":\"%s\",\"forced\":%s},",
        d.arch.processBitness, d.arch.osBitness,
        d.arch.wow64 ? "true" : "false", d.arch.hasWow2 ? "true" : "false",
        ArchChoiceLabel(d.archChoice), d.forcedArch ? "true" : "false");
    out += buf;
    std::snprintf(buf, sizeof(buf),
        "\"net\":{\"net35\":%s,\"v4Full\":%s,\"release\":%d,"
        "\"label\":\"%s\",\"profile\":\"%s\",\"forced\":%s},",
        d.net.net35 ? "true" : "false", d.net.v4Full ? "true" : "false",
        d.net.v4Release, DotnetReleaseLabel(d.net).c_str(),
        RuntimeProfileLabel(d.profile), d.forcedProfile ? "true" : "false");
    out += buf;
    out += "\"notes\":"; Jstr(out, d.notes);
    out += "}";
    return out;
}

} // namespace bootstrap
} // namespace lumina
