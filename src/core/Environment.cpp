// ============================================================================
//  Fusion-HP · Environment.cpp — implementación de detección [SPEC §4.1]
// ============================================================================
#include "Environment.h"

namespace fusion {

typedef LONG (WINAPI* RtlGetVersionFn)(PRTL_OSVERSIONINFOW);
typedef BOOL (WINAPI* IsWow64Process2Fn)(HANDLE, PUSHORT, PUSHORT);

void Environment::DetectOs(EnvironmentReport& r) {
    // PASO 1 — RtlGetVersion (no mentido por manifest de compatibilidad)
    RTL_OSVERSIONINFOEXW vi = {};
    vi.dwOSVersionInfoSize = sizeof(vi);
    HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    if (ntdll) {
        RtlGetVersionFn f = (RtlGetVersionFn)GetProcAddress(ntdll, "RtlGetVersion");
        if (f) f((PRTL_OSVERSIONINFOW)&vi);
    }
    r.build = vi.dwBuildNumber;
    r.isServer = (vi.wProductType != VER_NT_WORKSTATION);
    DWORD spMajor = vi.wServicePackMajor;
    r.sp1OrHigher = (spMajor >= 1);

    wchar_t sp[64] = L"";
    if (spMajor > 0) swprintf_s(sp, L" SP%u", (unsigned)spMajor);

    if (vi.dwMajorVersion == 6 && vi.dwMinorVersion == 1)
        r.osName = std::wstring(L"Windows 7") + sp;
    else if (vi.dwMajorVersion == 6 && vi.dwMinorVersion == 2)
        r.osName = std::wstring(r.isServer ? L"Windows Server 2012" : L"Windows 8") + sp;
    else if (vi.dwMajorVersion == 6 && vi.dwMinorVersion == 3)
        r.osName = std::wstring(r.isServer ? L"Windows Server 2012 R2" : L"Windows 8.1") + sp;
    else if (vi.dwMajorVersion == 10 && vi.dwBuildNumber >= 22000)
        r.osName = std::wstring(L"Windows 11") + sp;
    else if (vi.dwMajorVersion == 10)
        r.osName = std::wstring(r.isServer ? L"Windows Server 2016+" : L"Windows 10") + sp;
    else
        r.osName = std::wstring(L"Windows (") + std::to_wstring(vi.dwMajorVersion) + L"." +
                   std::to_wstring(vi.dwMinorVersion) + L")" + sp;
}

void Environment::DetectArch(EnvironmentReport& r) {
    // PASO 2 — proceso y SO
#if defined(_WIN64)
    r.archProcess = L"x64";
    r.archOs = L"x64";
    r.wow64 = false;
#else
    r.archProcess = L"x86";
    BOOL wow = FALSE;
    IsWow64Process(GetCurrentProcess(), &wow);
    r.wow64 = (wow == TRUE);
    r.archOs = r.wow64 ? L"x64" : L"x86";
    // IsWow64Process2 daría el machine real del SO; con IsWow64Process basta para elegir binario
#endif
}

static bool FileExistsW(const std::wstring& p) {
    DWORD a = GetFileAttributesW(p.c_str());
    return (a != INVALID_FILE_ATTRIBUTES && !(a & FILE_ATTRIBUTE_DIRECTORY));
}
static bool DirExistsW(const std::wstring& p) {
    DWORD a = GetFileAttributesW(p.c_str());
    return (a != INVALID_FILE_ATTRIBUTES && (a & FILE_ATTRIBUTE_DIRECTORY));
}

void Environment::DetectNet(EnvironmentReport& r) {
    // PASO 3 — detección de runtime por sistema de archivos, SOLO LECTURA [REQ][SPEC §4.1].
    // Nota: el hook clr (ICLRRuntimeInfo) exigiría mscoree + COM; la presencia de
    // carpetas de Framework es el indicador público equivalente y sin Registro.
    wchar_t root[MAX_PATH];
    UINT n = GetWindowsDirectoryW(root, MAX_PATH);
    if (n == 0 || n >= MAX_PATH - 40) { r.net = NetRuntime::None; r.netDetail = L""; return; }

    // .NET 4.x (CLR4): %windir%\Microsoft.NET\Framework[64]\v4.0.30319\clr.dll
    // En x64 preferimos Framework64 si el proceso es x64; ambos indican instalación.
    struct Prove { const wchar_t* sub; };
    const wchar_t* fw64 = L"Microsoft.NET\\Framework64\\v4.0.30319\\clr.dll";
    const wchar_t* fw32 = L"Microsoft.NET\\Framework\\v4.0.30319\\clr.dll";
    std::wstring base(root);
    bool hasNet4 = FileExistsW(base + L"\\" + fw64) || FileExistsW(base + L"\\" + fw32);

    // .NET 3.5 (CLR2): %windir%\Microsoft.NET\Framework[64]\v3.5\ + mscorwks en v2.0.50727
    const wchar_t* f3564 = L"Microsoft.NET\\Framework64\\v3.5";
    const wchar_t* f3532 = L"Microsoft.NET\\Framework\\v3.5";
    const wchar_t* clr264 = L"Microsoft.NET\\Framework64\\v2.0.50727\\mscorwks.dll";
    const wchar_t* clr232 = L"Microsoft.NET\\Framework\\v2.0.50727\\mscorwks.dll";
    bool hasNet35 = (DirExistsW(base + L"\\" + f3564) || DirExistsW(base + L"\\" + f3532)) &&
                    (FileExistsW(base + L"\\" + clr264) || FileExistsW(base + L"\\" + clr232));

    if (hasNet4) {
        r.net = NetRuntime::Net4x;
        r.netDetail = L"4.x (CLR4)";
    } else if (hasNet35) {
        r.net = NetRuntime::Net35;
        r.netDetail = L"3.5 (CLR2)";
    } else {
        r.net = NetRuntime::None;
        r.netDetail = L"";
    }
}

EnvironmentReport Environment::Detect() {
    EnvironmentReport r;
    DetectOs(r);
    DetectArch(r);
    DetectNet(r);
    r.portable = IsPortableMode();

    // PASO 4 — perfil [SPEC §4.2]
    if (r.net == NetRuntime::Net4x) r.profile = L"A";
    else if (r.net == NetRuntime::Net35) r.profile = L"B";
    else r.profile = L"C";
    return r;
}

Json EnvironmentReport::ToJson() const {
    Json j = Json::object();
    j["os"] = ToUtf8(osName);
    j["build"] = build;
    j["archProcess"] = ToUtf8(archProcess);
    j["archOs"] = ToUtf8(archOs);
    j["wow64"] = wow64;
    j["net"] = (net == NetRuntime::Net4x ? "4.x" : net == NetRuntime::Net35 ? "3.5" : "none");
    j["netDetail"] = ToUtf8(netDetail);
    j["profile"] = ToUtf8(profile);
    j["portable"] = portable;
    j["version"] = FUSION_VERSION;
    return j;
}

std::wstring EnvironmentReport::Summary() const {
    return osName + L" · proceso " + archProcess + L" · SO " + archOs + L" · .NET " +
           (net == NetRuntime::Net4x ? L"4.x" : net == NetRuntime::Net35 ? L"3.5" : L"ausente") +
           L" · perfil " + profile;
}

} // namespace fusion
