// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - native/launcher/LuminaLauncher.cpp
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ----------------------------------------------------------------------------
//  Lanzador NATIVO del paquete portable híbrido v5.1.0 «FUNDAMENTO»
//  (Win32 puro: sin MFC, sin ATL, sin CRT dinámico — /MT).
//
//  Responsabilidad (docs/architecture-hybrid.md §2):
//    1. Detectar el runtime .NET Framework disponible en la máquina:
//         - .NET 4.8+ : HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full
//                       valor "Release" (DWORD) >= 528040. Se lee SIEMPRE con
//                       KEY_WOW64_64KEY porque el launcher es x86 y las claves
//                       NDP viven en la vista de registro de 64 bits (las de
//                       32 bits bajo WOW6432Node NO contienen NDP v4\Full).
//         - .NET 3.5  : HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5
//                       valor "Install" == 1 (incluido de fábrica en Win7 SP1).
//    2. Elegir el ejecutable gestionado a lanzar (v5.1.0: layout por runtime):
//         - .NET 4.8+  -> net48\LuminaPresentation.exe   (variante optimizada)
//         - .NET 3.5   -> net35\LuminaPresentation35.exe (baseline mínima)
//         - ninguno    -> MessageBox explicativo y salida 1.
//    3. VERIFICAR el paquete ANTES de lanzar (v5.1.0 — lección de v5.0.0):
//         el paquete v5.0.0 salió SIN las DLL gestionadas (Lumina.Core.dll…)
//         y la app moría con FileNotFoundException al abrir. El launcher ahora
//         exige, junto al exe elegido: LuminaCore.dll (nativa) y las DLLs
//         gestionadas Lumina.Core.dll / Lumina.Bridge.dll / Lumina.Api.dll;
//         si falta algo, el mensaje dice EXACTAMENTE qué archivo falta.
//    4. Lanzarlo con CreateProcess desde el directorio RAÍZ del paquete
//       (GetModuleFileName), esperar su fin y PROPAGAR su exit code.
//       La raíz aloja data\ (compartida por ambas variantes: settings.cs
//       DefaultBaseDir detecta el layout net48/net35 y sube un nivel).
//
//  El usuario nunca instala nada: en Win10 1903+/Win11 corre la variante net48
//  de fábrica; en Win7 SP1 corre la 3.5 (o la 4.8 si se instaló el offline).
//
//  Subsistema: WINDOWS (GUI) — no parpadea consola al hacer doble clic.
//  Compilación esperada: x86 y x64 con /MT (ver native/launcher/CMakeLists.txt).
// ============================================================================
#include <windows.h>

// ---------------------------------------------------------------------------
// Constantes
// ---------------------------------------------------------------------------

// Valor mínimo de la clave "Release" que identifica .NET Framework 4.8
// (528040 = versión oficial de RTM 4.8; 528049 = 4.8 en Win10 1903+/2004+).
static const DWORD kMinReleaseNet48 = 528040u;

// Ejecutables gestionados (v5.1.0: subcarpetas por runtime junto al launcher).
static const wchar_t* kTargetNet48 = L"net48\\LuminaPresentation.exe";
static const wchar_t* kTargetNet35 = L"net35\\LuminaPresentation35.exe";

// Componentes que DEBEN acompañar al exe elegido (v5.1.0: verificación dura —
// el paquete v5.0.0 se publicó sin ellos y la app no abría).
static const wchar_t* kRequiredSidecarDlls[] = {
    L"LuminaCore.dll",     // motor C++ nativo (/MT)
    L"Lumina.Core.dll",    // exportadores PPTX/PDF · Zefania · activadores · ajustes
    L"Lumina.Bridge.dll",  // puente P/Invoke al núcleo
    L"Lumina.Api.dll"      // API HTTP local + mando remoto
};

static const wchar_t* kWindowTitle = L"LuminaPresentation v5.1.0 «FUNDAMENTO»";

// ---------------------------------------------------------------------------
// Registro: lectura de DWORD con vista de 64 bits garantizada
// ---------------------------------------------------------------------------

// Lee un valor DWORD de HKLM bajo la vista NATIVA de 64 bits del registro.
// Devuelve true solo si la clave existe, el tipo es REG_DWORD y el tamaño es
// exactamente sizeof(DWORD).
static bool ReadRegistryDword(const wchar_t* subkey, const wchar_t* valueName,
                              DWORD* outValue)
{
    HKEY hKey = nullptr;
    // KEY_WOW64_64KEY: aunque este proceso sea x86 (bajo WoW64), abrir la
    // vista de 64 bits. Las claves de NDP se escriben en la vista nativa.
    LONG status = RegOpenKeyExW(HKEY_LOCAL_MACHINE, subkey, 0,
                                KEY_READ | KEY_WOW64_64KEY, &hKey);
    if (status != ERROR_SUCCESS)
        return false;

    DWORD type = 0;
    DWORD cbData = sizeof(DWORD);
    DWORD data = 0;
    status = RegQueryValueExW(hKey, valueName, nullptr, &type,
                              reinterpret_cast<LPBYTE>(&data), &cbData);
    RegCloseKey(hKey);

    if (status != ERROR_SUCCESS || type != REG_DWORD || cbData != sizeof(DWORD))
        return false;
    *outValue = data;
    return true;
}

// .NET Framework 4.8 (o superior) presente: clave v4\Full con Release >= 528040.
static bool IsNet48Available()
{
    DWORD release = 0;
    if (!ReadRegistryDword(L"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full",
                           L"Release", &release))
        return false;
    return release >= kMinReleaseNet48;
}

// .NET Framework 3.5 presente: clave v3.5 con Install == 1.
static bool IsNet35Available()
{
    DWORD installed = 0;
    if (!ReadRegistryDword(L"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v3.5",
                           L"Install", &installed))
        return false;
    return installed == 1;
}

// ---------------------------------------------------------------------------
// Utilidades de ruta y mensajería
// ---------------------------------------------------------------------------

// Recorta "C:\dir\file.exe" -> "C:\dir\" en el búfer de salida (sin shlwapi).
static void DirNameOf(const wchar_t* path, wchar_t* dirOut, size_t cchOut)
{
    size_t len = 0;
    while (path[len] != L'\0' && len + 1 < cchOut)
        ++len;

    // Busca el último separador (acepta '\\' y '/' por robustez).
    size_t last = 0;
    bool found = false;
    for (size_t i = 0; i < len; ++i)
    {
        if (path[i] == L'\\' || path[i] == L'/')
        {
            last = i;
            found = true;
        }
    }
    if (!found)
    {
        // Sin separador: el "directorio" es el actual.
        dirOut[0] = L'.';
        dirOut[1] = L'\0';
        return;
    }
    for (size_t i = 0; i <= last; ++i)      // incluye el separador final
        dirOut[i] = path[i];
    dirOut[last + 1] = L'\0';
}

// Concatena dir + nombreDeArchivo en out; añade separador si dir no lo trae.
// false si no cabe.
static bool JoinPath(const wchar_t* dir, const wchar_t* file,
                     wchar_t* out, size_t cchOut)
{
    size_t d = 0;
    while (dir[d] != L'\0' && d + 1 < cchOut) { out[d] = dir[d]; ++d; }
    if (d > 0 && out[d - 1] != L'\\' && out[d - 1] != L'/' && d + 1 < cchOut)
        out[d++] = L'\\';
    size_t f = 0;
    while (file[f] != L'\0' && d + 1 < cchOut) { out[d++] = file[f++]; }
    if (file[f] != L'\0') return false;     // se quedó corto
    out[d] = L'\0';
    return true;
}

static void ShowErrorBox(const wchar_t* text)
{
    MessageBoxW(nullptr, text, kWindowTitle, MB_OK | MB_ICONERROR);
}

// ---------------------------------------------------------------------------
// Lanzamiento
// ---------------------------------------------------------------------------

// Lanza exePath con CreateProcess, espera INFINITE y devuelve el exit code del
// hijo por outExitCode. Devuelve 0 en éxito; otro valor = GetLastError del fallo.
static DWORD LaunchAndWait(const wchar_t* exePath, const wchar_t* workDir,
                           DWORD* outExitCode)
{
    STARTUPINFOW si;
    PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof(si));
    ZeroMemory(&pi, sizeof(pi));
    si.cb = sizeof(si);

    // Línea de comando entre comillas (rutas con espacios).
    wchar_t cmdLine[MAX_PATH + 4];
    cmdLine[0] = L'"';
    size_t i = 0;
    while (exePath[i] != L'\0' && i + 2 < (MAX_PATH + 3))
    {
        cmdLine[i + 1] = exePath[i];
        ++i;
    }
    cmdLine[i + 1] = L'"';
    cmdLine[i + 2] = L'\0';

    if (!CreateProcessW(exePath,      // aplicación (ruta completa)
                        cmdLine,      // línea de comando citada
                        nullptr, nullptr,
                        FALSE,        // no heredar manejadores
                        0,            // sin flags especiales
                        nullptr,      // mismo entorno
                        workDir,      // directorio actual = carpeta del paquete
                        &si, &pi))
    {
        return GetLastError();
    }

    WaitForSingleObject(pi.hProcess, INFINITE);
    DWORD code = 1;
    if (!GetExitCodeProcess(pi.hProcess, &code))
        code = 1;
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    *outExitCode = code;
    return 0;
}

// ---------------------------------------------------------------------------
// Punto de entrada (subsistema WINDOWS -> WinMain; APIs siempre W)
// ---------------------------------------------------------------------------

int WINAPI WinMain(HINSTANCE /*hInstance*/, HINSTANCE /*hPrevInstance*/,
                   LPSTR /*lpCmdLine*/, int /*nCmdShow*/)
{
    // Ruta completa del propio launcher (el paquete es portable: todo vive
    // relativo a esta carpeta).
    wchar_t launcherPath[MAX_PATH];
    DWORD n = GetModuleFileNameW(nullptr, launcherPath, MAX_PATH);
    if (n == 0 || n >= MAX_PATH)
    {
        ShowErrorBox(L"No se pudo determinar la carpeta del programa "
                     L"(ruta demasiado larga o error del sistema).");
        return 1;
    }

    wchar_t baseDir[MAX_PATH];
    DirNameOf(launcherPath, baseDir, MAX_PATH);

    // ---- Selección del ejecutable según el runtime disponible ----
    const wchar_t* target = nullptr;
    if (IsNet48Available())
    {
        target = kTargetNet48;                 // meta preferida: .NET 4.8
    }
    else if (IsNet35Available())
    {
        target = kTargetNet35;                 // baseline: .NET 3.5 (Win7 SP1)
    }
    else
    {
        MessageBoxW(nullptr,
            L"LuminaPresentation no encontró un runtime de .NET Framework compatible "
            L"en este equipo.\n\n"
            L"Requisitos (uno de los dos):\n\n"
            L"  • .NET Framework 4.8 — ya incluido de fábrica en "
            L"Windows 10 1903+ y Windows 11.\n"
            L"  • .NET Framework 3.5 — incluido en Windows 7 SP1 / 8.x "
            L"(actívelo en «Activar o desactivar las características de "
            L"Windows» o Panel de control).\n\n"
            L"Si su Windows no trae ninguno, descargue el instalador "
            L"OFFLINE de .NET Framework 4.8 desde:\n"
            L"  https://dotnet.microsoft.com/download/dotnet-framework/net48\n\n"
            L"Este paquete es portable: no se instaló ni modificó nada "
            L"en el sistema.",
            kWindowTitle, MB_OK | MB_ICONERROR);
        return 1;
    }

    wchar_t targetPath[MAX_PATH];
    if (!JoinPath(baseDir, target, targetPath, MAX_PATH))
    {
        ShowErrorBox(L"La ruta del programa es demasiado larga "
                     L"(mueva el paquete a una carpeta más corta).");
        return 1;
    }

    // Guardia amable: el exe debe existir junto al launcher antes de lanzar
    // (paquete incompleto, antivirus cuarentena, etc.).
    if (GetFileAttributesW(targetPath) == INVALID_FILE_ATTRIBUTES)
    {
        wchar_t msg[600];
        wsprintfW(msg,
                  L"No se encontró el ejecutable:\n\n  %s\n\n"
                  L"Verifique que el paquete esté completo y que su "
                  L"antivirus no haya puesto el archivo en cuarentena.",
                  target);
        ShowErrorBox(msg);
        return 1;
    }

    // ------------------------------------------------------------------
    // Guardias CLAVE contra «no se puede abrir» (v5.1.0 — lección v5.0.0):
    // TODOS los componentes deben existir JUNTO al exe elegido (en su
    // subcarpeta net48\ o net35\): el motor nativo LuminaCore.dll y las DLLs
    // gestionadas. El paquete v5.0.0 se publicó sin las gestionadas y la app
    // moría al abrir con FileNotFoundException; aquí se detecta ANTES con un
    // mensaje que dice EXACTAMENTE qué archivo falta.
    // ------------------------------------------------------------------
    wchar_t targetDir[MAX_PATH];
    DirNameOf(targetPath, targetDir, MAX_PATH);
    for (int i = 0; i < (int)(sizeof(kRequiredSidecarDlls) / sizeof(kRequiredSidecarDlls[0])); ++i)
    {
        wchar_t compPath[MAX_PATH];
        if (!JoinPath(targetDir, kRequiredSidecarDlls[i], compPath, MAX_PATH)) continue;
        if (GetFileAttributesW(compPath) == INVALID_FILE_ATTRIBUTES)
        {
            wchar_t msg[700];
            wsprintfW(msg,
                      L"Falta un componente del programa:\n\n"
                      L"  %s\\%s\n\n"
                      L"Causas frecuentes:\n"
                      L"  1. Ejecutar desde DENTRO del ZIP (sin extraer).\n"
                      L"  2. Extracción incompleta o antivirus que aisló el archivo.\n\n"
                      L"Solución: extraiga el ZIP COMPLETO en una carpeta propia "
                      L"y ejecute LuminaLauncher.exe desde ahí.",
                      (targetDir[0] == L'.' ? L"" : targetDir), kRequiredSidecarDlls[i]);
            MessageBoxW(nullptr, msg, kWindowTitle, MB_OK | MB_ICONWARNING);
            return 1;
        }
    }

    // Lanzar y esperar; propagar el exit code del hijo.
    // El directorio actual del hijo SIEMPRE es la carpeta del paquete:
    // la BD portable (data/) y los recursos viven relativos a ella, sin
    // depender de desde dónde el usuario lanzó el launcher.
    DWORD childExit = 1;
    DWORD err = LaunchAndWait(targetPath, baseDir, &childExit);
    if (err != 0)
    {
        wchar_t msg[512];
        // Mensaje con el código de error Win32 para soporte técnico.
        wsprintfW(msg,
                  L"No se pudo iniciar:\n\n  %s\n\n"
                  L"Código de error del sistema: %lu\n\n"
                  L"Verifique que el paquete esté completo "
                  L"(el archivo debe estar junto a LuminaLauncher.exe).",
                  target, (unsigned long)err);
        ShowErrorBox(msg);
        return 1;
    }
    return (int)childExit;
}
