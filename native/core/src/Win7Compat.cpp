// ============================================================================
//  LuminaPresentation Suite v5.2.0 «MOTOR» — native/core/src/Win7Compat.cpp
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Compatibilidad Windows 7 SP1 x86/x64 del CRT/STL estático de MSVC.
//
//  PROBLEMA REAL (diagnóstico de campo, v5.1.1 en Win7 SP1 x86):
//  El objeto del STL que implementa std::condition_variable referencia
//  GetSystemTimePreciseAsFileTime — API que SOLO existe desde Windows 8 —
//  como import ESTÁTICO (símbolo __imp__, estilo dllimport). En Win7 SP1,
//  LoadLibrary("LuminaCore.dll") falla con ERROR_PROCEDURE_NOT_FOUND aunque
//  nuestro código jamás ejecute ese camino (solo usamos wait() sin timeout):
//  el huésped C# entra en «modo limitado» (núcleo null) y la aplicación
//  «carga la GUI pero más nada».
//
//  SOLUCIÓN (determinista, a nivel de ENLACE — valida en ambas arquitecturas):
//  Definir NOSOTROS el símbolo __imp__GetSystemTimePreciseAsFileTime como
//  DATO propio apuntando a un shim. Las definiciones de objetos SIEMPRE
//  ganan a los stubs de las .lib → el import estático de kernel32 NUNCA se
//  genera y el DLL carga en Win7 SP1. El shim usa la API real en Win8+
//  (vía GetProcAddress) y un fallback sobre GetSystemTimeAsFileTime (grano
//  ~15 ms) en Win7 — de sobra para timeouts de condition_variable.
//
//  Detalle de decoración (x86): el CRT referencia el símbolo CON decoración
//  stdcall: __imp__GetSystemTimePreciseAsFileTime@4 — no es un identificador
//  C++ válido, por lo que lo define Win7CompatX86.asm (MASM, solo x86+MSVC).
//  En x64 (sin decoración) basta esta definición en C++ puro.
//
//  El gate tools/verify_win7_imports.py en CI certifica el resultado sobre
//  el binario final: ninguna API Win8+ como import estático, jamás.
// ============================================================================
#include <windows.h>

#ifdef _WIN32

// El shim debe ser extern "C" WINAPI para que MASM (x86) pueda tomar su
// dirección con el nombre decorado _LuminaGetSystemTimePrecise@4.
extern "C" void WINAPI LuminaGetSystemTimePrecise(LPFILETIME ft)
{
    // Win8+: la API real (precisión µs). Win7: GetSystemTimeAsFileTime.
    HMODULE k32 = GetModuleHandleW(L"kernel32.dll");
    if (k32 != nullptr)
    {
        typedef void (WINAPI *Fn)(LPFILETIME);
        Fn real = reinterpret_cast<Fn>(reinterpret_cast<void *>(
            GetProcAddress(k32, "GetSystemTimePreciseAsFileTime")));
        if (real != nullptr)
        {
            real(ft);
            return;
        }
    }
    GetSystemTimeAsFileTime(ft);
}

// Definición del símbolo __imp__ que el CRT/STL referencia (dllimport).
// En x64 (y como respaldo en x86, sin decorar): si ALGÚN toolset futuro
// vuelve a referenciarla, el enlazador toma ESTA definición de objeto —
// el import estático de kernel32 no se genera.
extern "C" void (WINAPI* const __imp_GetSystemTimePreciseAsFileTime)(LPFILETIME) =
    &LuminaGetSystemTimePrecise;

#endif // _WIN32
