# Arquitectura Híbrida — Fusion-HP / LuminaPresentation Suite v3.0.0 «HÍBRIDA»

> Decisión estratégica 2026-09: evolucionar de un ejecutable C++ monolítico (v1.x Qt, v2.0 wxWidgets)
> a una arquitectura **híbrida C++ + .NET (C#)** que conserva el motor nativo y moderniza la capa
> de interfaz, datos y API sin sacrificar rendimiento ni compatibilidad Win7 x32 → Win11+.

## 1. Reparto de responsabilidades

| Capa | Lenguaje | Contenido | Motivo |
|------|----------|-----------|--------|
| **Núcleo / Motor** | **C++17** (sin wx, sin Qt) | Motor de proyección en tiempo real (ventanas nativas Win32 + GDI doble búfer), modelo de escenario, parser/acordes/letras/referencias bíblicas, parser **JSON de canciones** y **.BIB de biblias**, almacenamiento embebido **SQLite + FTS5** (estático /MT) | Fluidez, baja latencia, cero dependencias externas, un solo renderizador fuente de verdad |
| **Interfaz (Editor de Escenarios)** | **C# WinForms** | Editor de escenarios, bibliotecas (canciones/biblias/medios), asistentes de importación (JSON/.BIB), control En Vivo, temas, ajustes | Productividad de desarrollo, UI/UX rica, binomio WinForms estable en .NET Framework |
| **Gestión de datos** | **C#** (orquestación) + C++ (motor SQLite vía API) | CRUD, import/export, respaldos; el C# SIEMPRE usa parámetros enlazados (`fusion_db_exec` con `params`) | Lógica de negocio rápida de evolucionar; el binario SQLite vive en el núcleo (sin dependencias extra) |
| **API local (remoto + OBS)** | **C#** | `HttpListener` (disponible desde .NET 2.0): `/api/state`, `/api/cmd`, `/api/live.txt`, webhook OBS | Sin runtimes externos (nada de Java/Node); escucha solo en localhost |

La interfaz gráfica de proyección **no se duplica**: C# pide vistas previas renderizadas al motor
(`fusion_render_preview_png`) y la proyección real ocurre en ventanas nativas del motor.

## 2. Versiones .NET y compatibilidad de SO (evaluación)

* **.NET Framework 3.8 no existe**: el mínimo real evaluable es **3.5 SP1** (incluido de fábrica en
  Windows 7 SP1). La duality elegida:
  * **Baseline: `net35`** — corre en Win7/8 sin instalar nada (el CLR 2.0/3.5 viene con el SO).
  * **Meta preferida: `net48`** — corre en Win10 1903+/Win11 de fábrica (y en Win7 con instalador 4.8).
* Una única base de código C# multi-target `net35;net48` (compilación condicional mínima).
* **Empaquetado**: `FusionLauncher.exe` (nativo, /MT) detecta el runtime disponible (registro
  `HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP`) y lanza `FusionHP.exe` (net48) o
  `FusionHP35.exe` (net35). **El usuario nunca instala nada**: en Win7 SP1 corre la variante 3.5
  integrada; en Win10/11 la 4.8 integrada.
* El núcleo C++ se compila **/MT estático** (x86 + x64), sin CRT redistribuible.
* Ningún runtime de terceros (sin Java, sin Node, sin Qt, sin wx en el binario híbrido).

## 3. Vías de integración evaluadas (con PoC)

| Vía | Veredicto | Evidencia |
|-----|-----------|-----------|
| **A. C# dueño del proceso + DLL nativa con exports C planos (P/Invoke)** | ✅ **ELEGIDA** | `poc/PoC.Managed` (net48 + net35, x86 + x64): marshaling UTF-8, structs, callbacks y JSON ida/vuelta validados como TEST ejecutable |
| B. C++/CLI (IJW) | ⚠️ Viable pero descartada como puente principal: acopla todo a MSVC+C++/CLI, complica el multi-target net35 y no aporta nada cuando el proceso es del lado C# | job informativo opcional en CI (`continue-on-error`) |
| C. CLR Hosting (nativo hospeda CLR) | ✅ Viable — **PoC implementado** (`poc-clrhost`): `CLRCreateInstance` → `ICLRMetaHost` → `ICorRuntimeHost` → `_AppDomain::CreateInstanceFrom` → facade COM-visible invocada desde C++ puro | Test en CI (Windows real con .NET Framework 4.8) |
| D. COM Interop con registro | ❌ Descartada: registro COM incompatible con portabilidad «cero instalaciones» (requeriría reg-free COM con manifiestos frágiles). La facade del PoC C es COM-visible **sin registro** (activada por hosting API) | — |

La vía C queda disponible como fallback arquitectónico (si algún día el proceso debe ser C++),
validada por el PoC; el producto usa la vía A por simplicidad, robustez y ABI estable.

## 4. Contrato de frontera (ABI)

* **`native/core/include/fusion/fusion.h`** — API C plana, estable, sin excepciones cruzando la
  frontera, sin objetos CRT compartidos, **todo UTF-8**, errores como códigos (`FUSION_OK=0…`).
* Patrón de búfer uniforme para strings de salida: `fn(..., char* out, int32_t cap, int32_t* needed)`
  (llamada con `cap=0` para medir; sin asignaciones dentro de la DLL).
* **Eventos** nativo→C#: `FusionEventFn` (un solo delegado, mantenido vivo con `GCHandle` en C#),
  disparados en un hilo dedicado del motor y serializados; el código gestionado **nunca** llama al
  motor re-entrántemente desde el callback (encola en su hilo de UI vía `SynchronizationContext`).
* Modelos internos fijos: `native/core/src/Models.h` (Song/Slide/Theme/Scenario, `std::string` UTF-8).

## 5. Formatos de datos incluidos en el núcleo

* **Canciones (JSON)** — esquema propio + importación tolerante (subconjunto OpenLP):
  `{type,title,artist,key,bpm,tags,blocks:[{label,lines[],repeat}]}` → el motor genera slides
  (`Lyrics::BuildSlides`, modo hinario con coro intercalado tras cada verso completo, dedupe del
  primer coro, canción solo-coros nunca vacía; acordes transponibles latina/anglosajona).
* **Biblias (.BIB)** — formato texto con detección automática (ver `docs/bib-format.md`):
  directivas de cabecera `#BIB/#VERSION/...` + filas `libro<sep>capítulo<sep>versículo<sep>texto`
  (separadores TAB, `|`, `;;`), alternativa por línea `Libro 1:1 texto`, UTF-8 (BOM opcional) y
  CP1252, con validación y estadísticas (`fusion_bib_parse`).
* **Almacenamiento**: SQLite estático con FTS5 (`songs_fts`), índice UNIQUE de biblia por
  `(version,book,chapter,verse)` y dedupe idempotente heredado del ciclo v1.6.0.

## 6. Proceso de build / release

* `native/` — CMake (MSVC en CI: x86+x64 `/MT`; GCC local para las partes portables + tests).
* `managed/` — dotnet SDK projects multi-target (`net35;net48`, ref assemblies por paquete NuGet
  `Microsoft.NETFramework.ReferenceAssemblies`), tests adicionales en `net8.0` para desarrollo local.
* CI: jobs `native-windows` (x86+x64 + selftest), `native-linux`, `managed`, **`interop`** (PoC real
  contra las DLL nativas, x86 y x64), `clrhost-poc`, `cppcli-poc` (informativo), `package`
  (portable + SHA256 + verify) y `release` en tags `v*`.

## 7. Historial conservado

* v1.x (Qt) y v2.0.0 «HORIZONTE» (wxWidgets) quedan en el historial git y en `apps/native-wx-src`
  (referencia de algoritmos); el CI de esa línea se retira al fusionarse esta arquitectura.
