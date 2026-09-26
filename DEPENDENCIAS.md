# DEPENDENCIAS.md — Lista normativa de componentes de terceros (v4.1.0)

> **Carácter normativo [LICENSE.md §4]:** esta lista se actualiza con cada release y
> **prevalece sobre cualquier divergencia** con la sección 4 de LICENSE.md.
> Regla de oro permanente: solo componentes **permisivos** (MIT, BSD, Apache-2.0,
> Zlib, dominio público, SIL OFL). **Prohibido GPL/AGPL/LGPL** en código enlazado
> estáticamente o derivado, salvo licencia comercial comprada o LGPL con enlace
> dinámico y cláusula de reemplazo.
> Verificación: cada artefacto entra al árbol con hash SHA-256 registrado aquí.

## 1. C++ (`src/core/`, C++17 `/MT`, Win7 SP1 x86+)

| Componente | Versión | Licencia | Uso | Origen | SHA-256 (parcial) |
|---|---|---|---|---|---|
| Microsoft WIL | 1.0.240803.1 | MIT | RAII de HANDLE/COM (`WIL_EXCEPTION_MODE=1`, sin excepciones) | github.com/microsoft/wil | resource.h `45a8fa700ba62f1c` |
| doctest | 2.4.11 | MIT | Aserciones dentro de CoreTests (arnés propio intacto) | github.com/doctest/doctest | doctest.h `44faa038e9c3f972` |
| SQLite amalgamation | 3.45.1 | Dominio público | Lector nativo fdb/biblias con SQL real (B-trees, WAL, encodings) | sqlite.org/2024 | sqlite3.c `1a206854aa9fe0cc` |
| nlohmann/json | 3.11.x | MIT | JSON del núcleo (cancionero/sesiones) | github.com/nlohmann/json | (preexistente) |

## 2. C# (`src/managed/`, net35 → net48, DLL vendorizadas con HintPath)

| Componente | Versión | Licencia | TFM del binario | Uso | SHA-256 (parcial) |
|---|---|---|---|---|---|
| Newtonsoft.Json | 13.0.3 | MIT | net35 | Motor detrás de la fachada `JsonValue` (API pública intacta) | `c69b18993d8236e5` |
| NLog | 4.7.15 | BSD-3 | net35 | Log estructurado C# (rotación 14 días; sin letras/versículos) | `8510b7d8e5d5e455` |
| PDFsharp | 1.50.5147 | MIT | net20 | Motor PDF de **FusionStudio.Lite** (perfil B) | `2fa0893c6a1a8e64` |
| **PDFsharp** | **6.1.1** | MIT | netstandard2.0 | Motor PDF de **FusionStudio** (net48) — **desviación de versión autorizada**: la 1.50 trae `XPrivateFontCollection` stub (NotImplementedException verificado con dnfile) y su build GDI sustituye familias privadas, por lo que NO puede incrustar las fuentes del producto; la 6.1.1 con `IFontResolver` sí | dll `6ab52dd47537fdef` · System `d8b8426655d22c13` |
| Microsoft.Extensions.Logging.Abstractions | 6.0.0 | MIT | netstandard2.0 | Dependencia de PDFsharp 6.1.1 | `fef2acbc613d9353` |
| System.Buffers | 4.5.1 | MIT | net461 | Cadena transitiva de PDFsharp 6.1.1 (v4.1.1) | `accccfbe45d9f08f` |
| System.Memory | 4.5.4 | MIT | net461 | Cadena transitiva de PDFsharp 6.1.1 (v4.1.1) | `8e76318e8b06692a` |
| System.Numerics.Vectors | 4.5.0 | MIT | net46 | Cadena transitiva de System.Memory (v4.1.1) | `1d3ef8698281e7cf` |
| System.Runtime.CompilerServices.Unsafe | 6.0.0 | MIT | net461 | Cadena transitiva de PDFsharp 6.1.1 (v4.1.1) | `37768488e8ef4572` |
| System.Data.SQLite.Core | 1.0.118 | Dominio público/MIT | net20 (+ interop x86/x64) | Vía ADO.NET de `SQLiteFileReader`; lector puro B-Tree de fallback (perfil B) | managed `069d8c7bb2e6d08d` · x86 `1236cb079fd94557` · x64 `abd11bd7c1bed1cc` |
| DocumentFormat.OpenXml | 2.7.2 | MIT | net40 | Importador PPTX de FusionStudio (herencia 4 niveles, anti-XXE por construcción). **Nunca** en FusionShared/Lite (net35) | `211c0231d9bae955` |
| Ookii.Dialogs | 1.0.0 | BSD-3 | net35 | `VistaFolderBrowserDialog` (Medios → Carpeta). Solo diálogos; sin JumpLists | `04b8cb55ff481a4f` |
| Portable.BouncyCastle | 1.8.9 | MIT | net40 | Motor cripto disponible para formatos futuros; `Twofish.cs` propio sigue en uso (vectores I=1,2,3 vigentes) | `0bd5fdc4f438ffa4` |

## 3. Assets incluidos

| Componente | Licencia | Uso |
|---|---|---|
| Outfit, Cormorant Garamond, Libre Baskerville | SIL OFL 1.1 | Fuentes del producto (`resources/fonts`, TTF instanciados por el propio proyecto a partir del variable de la referencia web) |
| Tabler Icons | MIT | 248 PNG (62 glifos × 4 tintas) en `resources/img/icons` |
| RVR1909 (módulo de muestra e-Sword) | Dominio público (texto) | `resources/data/sample/rvr1909.bib` para probar importación |

## 4. Descartadas definitivamente

libmpv, SkiaSharp, Dear ImGui, HarfBuzz/FreeType (sustituirían Direct2D/GDI+/DirectShow, arquitectura normativa); System.Text.Json, Serilog, Microsoft.Data.Sqlite, Microsoft.Bcl.Async (sin net35 → rompen perfil B); ObjectListView (GPLv3), iTextSharp (AGPL), libdshowcapture (GPLv2) (copyleft incompatible); NAudio en el núcleo (violaría la Regla de Oro #1).

## 5. Componentes C++ adicionales y verificación Win7

| Componente | Versión | Licencia | Uso | SHA-256 (parcial) |
|---|---|---|---|---|
| spdlog (header-only, con fmt embebido) | 1.12.0 | MIT | Motor del `Logger.cpp`: sink diario SINCRÓNICO (`daily_file_sink_mt`, retención 14 días, hilo-seguro), `SPDLOG_NO_EXCEPTIONS`, formato SPEC §11.1.5 byte a byte vía patrón `%v` | (cabeceras vendorizadas) |

- **Verificación Win7 SP1 automatizada [condición spdlog]:** gate CI con
  `dumpbin /IMPORTS` sobre FusionHP.exe y CoreTests.exe (x86 y x64) que FALLA si
  aparece cualquier import solo-Win8+ (WaitOnAddress, WakeByAddress*,
  GetSystemTimePreciseAsFileTime, CreateFile2, GetOverlappedResultEx, PathCch*,
  Ro*). Con `_WIN32_WINNT=0x0601` la STL de MSVC condiciona `chrono` y el gate
  pasa: **0 imports Win8+ en ambas arquitecturas** (run CI v4.1.0).
- **Tamaño del exe [condición SQLite]:** FusionHP.exe x64 = 2.24 MB con
  sqlite3.c compilado dentro (LTCG elimina lo no referenciado; el coste real
  aparece al usar el lector). Reportado por el mismo job de CI en cada run.
- La verificación manual final en un equipo Win7 x86 real sin .NET (perfil C)
  queda como paso del despliegue del propietario: el gate de imports es el
  proxy automatizado más estricto disponible en CI.
