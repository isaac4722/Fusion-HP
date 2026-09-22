// ============================================================================
//  LuminaPresentation Suite v5.2.0 «MOTOR» — native/core/src/Win7Compat.cpp
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Compatibilidad Windows 7 SP1 x86/x64 del CRT/STL estático de MSVC 2019/2022.
//
//  PROBLEMA REAL (diagnóstico de campo, v5.1.1 en Win7 SP1 x86):
//  El objeto del STL que implementa std::condition_variable::wait/timedwait
//  referencia GetSystemTimePreciseAsFileTime — API que SOLO existe desde
//  Windows 8. Como el linker la coloca en la tabla de importaciones
//  ESTÁTICA, LoadLibrary("LuminaCore.dll") falla en Win7 SP1 con
//  ERROR_PROCEDURE_NOT_FOUND (127) aunque el código jamás la llame:
//  el huésped C# entra en «modo limitado» (núcleo null) y la aplicación
//  «carga la GUI pero más nada».
//
//  SOLUCIÓN (capa doble, determinista para Win7→Win11):
//   1) /DELAYLOAD:GetSystemTimePreciseAsFileTime (CMakeLists) — la API deja
//      de ser un import estático y se resuelve AL PRIMER USO.
//   2) Este hook: si el SO la tiene (Win8+) usa la REAL (precisión µs);
//      si no (Win7 SP1), devuelve un fallback sobre GetSystemTimeAsFileTime
//      (grano ~15 ms) — de sobra para timeouts de condition_variable.
//
//  El gate tools/verify_win7_imports.py en CI impide que CUALQUIER otra
//  API Win8+/Win10+ vuelva a entrar como import estático en los binarios.
// ============================================================================
#include <windows.h>

#ifdef _WIN32

#include <delayimp.h>
#include <string.h>

namespace {

// Fallback Win7: misma firma que GetSystemTimePreciseAsFileTime.
// Precisión reducida (≈15,6 ms) — irrelevante para esperas con timeout.
void WINAPI PreciseFileTimeFallback(LPFILETIME ft)
{
    GetSystemTimeAsFileTime(ft);
}

FARPROC WINAPI Win7DelayLoadHook(unsigned dliNotify, DelayLoadInfo* pdli)
{
    if (dliNotify == dliNotePreGetProcAddress && pdli != nullptr)
    {
        if (pdli->dlp.szProcName != nullptr &&
            strcmp(pdli->dlp.szProcName, "GetSystemTimePreciseAsFileTime") == 0)
        {
            // ¿El SO trae la API de verdad? (Win8+)
            HMODULE k32 = GetModuleHandleW(L"kernel32.dll");
            FARPROC real = (k32 != nullptr)
                ? GetProcAddress(k32, "GetSystemTimePreciseAsFileTime")
                : nullptr;
            return (real != nullptr) ? real
                                     : reinterpret_cast<FARPROC>(&PreciseFileTimeFallback);
        }
    }
    return nullptr;   // comportamiento por defecto de delayimp
}

} // namespace

// Hook global de delayimp (único delay-load del binario). extern "C" porque
// delayimp lo busca por nombre C puro.
extern "C" PfnDliHook __pfnDliNotifyHook2 = &Win7DelayLoadHook;

#endif // _WIN32
