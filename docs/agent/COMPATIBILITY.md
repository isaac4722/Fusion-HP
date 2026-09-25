# COMPATIBILITY.md — Escalera .NET y perfiles A/B/C [SPEC §4]

## Detección (solo lectura, sin Registro)

| Paso | Qué | Cómo |
|---|---|---|
| 1 | SO y build | `RtlGetVersion` (NTDLL) — nunca `GetVersion` |
| 2 | Arquitectura | compile-time `_WIN64` + `IsWow64Process` |
| 3 | .NET | existencia de archivos en `%windir%\Microsoft.NET\Framework[64]\` |

- .NET 4.x ⇒ `Framework[64]\v4.0.30319\clr.dll`
- .NET 3.5 ⇒ carpeta `v3.5` + `v2.0.50727\mscorwks.dll`

## Perfiles

| Perfil | Runtime | Qué se ejecuta |
|---|---|---|
| **A** | 4.x | `FusionStudio.exe` (net48, editor WPF completo) |
| **B** | 3.5 SP1 | `FusionStudio.Lite.exe` (net35, editor WinForms) |
| **C** | ninguno | `FusionHP.exe` muestra la UI de emergencia nativa |

Regla normativa: ninguna función falla silenciosamente entre perfiles [SPEC §4.2].
El editor WPF no existe en B → el Lite compila el mismo modelo con lienzo WinForms
(documentado como desviación funcional equivalente; misma edición de contenido/estilos).

## Win7 x86 (objetivo mínimo)

- Toolset v143, `_WIN32_WINNT=0x0601`, CRT estático `/MT` (sin VC redist).
- El binario x86 corre en Win7/8/10/11 y bajo WOW64.
- Direct2D y WIC existen desde Win7; DirectShow con VMR9 idem.
- `IsWow64Process2` es Win10+ → se usa `IsWow64Process` (Win7+).

## Cambios que exigen verificación en Win7 x86 sin .NET (perfil C)

Cualquier cambio en `src/core/` (bootstrap, render, IPC, sesión nativa). El
criterio F0 [SPEC §12.3.1] es el cierre: arranca, proyecta texto/imagen y el
log registra SO/arquitectura/perfil correctos.

## Instalador y portable [SPEC §4.4]

- Inno Setup dual: `ArchitecturesInstallIn64BitMode=x64compatible`; instala el
  binario correcto por arquitectura.
- Portable: `portable.flag` (o carpeta `datos/`) junto al exe ⇒ modo portable;
  la configuración vive en `datos/`, nunca en `%APPDATA%`.
- .NET 4.8 es un componente OPCIONAL de descarga consentida [SPEC §4.1]: el
  programa nunca lo descarga en segundo plano por su cuenta.
