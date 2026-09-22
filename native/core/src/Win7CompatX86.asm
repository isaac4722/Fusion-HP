; ============================================================================
;  LuminaPresentation Suite v5.2.0 «MOTOR» — native/core/src/Win7CompatX86.asm
;  Copyright (c) 2026 Isaac. Licencia View-Only.
; ============================================================================
;  Solo x86 + MSVC (se agrega a LUMINA_CORE_SOURCES cuando
;  CMAKE_SIZEOF_VOID_P == 4). Define el símbolo DECORADO que el CRT/STL
;  estático referencia vía dllimport:
;
;      __imp__GetSystemTimePreciseAsFileTime@4     (WinAPI stdcall, 1 puntero)
;
;  La definición de objeto SIEMPRE gana al stub de kernel32.lib → el import
;  estático de esa API (que NO existe en Windows 7 SP1) jamás llega a la
;  tabla de importaciones del DLL → LoadLibrary funciona en Win7.
;  El apuntador lleva al shim LuminaGetSystemTimePrecise (Win7Compat.cpp):
;  API real en Win8+, fallback GetSystemTimeAsFileTime en Win7.
;
;  En C++ no se puede declarar un identificador con '@' — por eso MASM.
; ============================================================================
.686
.model flat

EXTERN _LuminaGetSystemTimePrecise@4:PROC

PUBLIC __imp__GetSystemTimePreciseAsFileTime@4

.data
__imp__GetSystemTimePreciseAsFileTime@4 DWORD _LuminaGetSystemTimePrecise@4

END
