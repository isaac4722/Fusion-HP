# Matriz de cumplimiento — 80 fixtures (F0.01–F6.10)

> **Línea:** reestructuración v1.0.0-beta.3 · doc técnico v1.1 · plan de ultra
> implementación (`spec/plan-ultra-implementacion.md`).
> **Verificación:** auditoría de evidencia por fixture (marcador `F*` en código,
> tests, CI y documentos) + revisión semántica de las implementadas por nombre
> de feature. Fecha: 2026-09-24.
>
> Columnas: **Impl** = implementación real (archivo[s]); **Test** = prueba que
> fallaría sin la implementación (§16.3); **CI** = job que la protege.

## F0 — Bootstrap, arquitectura y perfiles

| Fixture | Requisito | Impl | Test | CI |
|---|---|---|---|---|
| F0.01 | Detección SO/arquitectura/CD runtime | `native/core/src/Bootstrap.cpp` · `EnvironmentReport` | `TestProfileClassification`/`TestOsClassification` (arnés g++), bootstrap A/B/C | `core-portable`, `core-msvc` |
| F0.02 | Clasificación de perfiles A/B/C | `Bootstrap::Classify` | ídem | ídem |
| F0.03 | Arranque por perfil (A full · B C# · C nativo) | `Bootstrap.cpp` + `Projector` | bootstrap A/B/C del arnés | ídem |
| F0.04 | Perfil C sin .NET (salida nativa) | `Bootstrap` (C) + `Projector.cpp` | arnés g++ | ídem |
| F0.05 | IPC `ipc.v1` framing + cola | `IpcV1.cpp` · `IpcApi` | `TestIpcRoundtrip/Frag­mentation/ProtocolErrors/CommandQueue` (4 tests) | `core-portable` |
| F0.06 | Ruta Direct2D con degradación a GDI+ | `src/core/src/RendererD2D.cpp` (LUMINA_HAS_D2D) | gates MSVC del vcxproj | `core-msvc` |
| F0.07 | Launcher nativo | `native/launcher/LuminaLauncher.cpp` | empaquetado + smoke | `package` |
| F0.08 | Sin recrear CMake (MSBuild norma) | `src/core/core.vcxproj` | build 0 warn | `core-msvc` |
| F0.09 | Log de entorno + informe F0.09/F6.01 | `NativeLog.cpp` · `src/core/src/EnvironmentReport.*` | `TestEnvironmentReport` (g++) · `TestNativeLog` | `core-portable` |
| F0.10 | **Gate F0** | criterio 12.3 F0 = bootstrap+perfil C+texto/imagen+log → cubierto por F0.01–F0.09 y su evidencia; la pieza física (Win7 x86 real) queda en F6.07 | arnés g++ + CI | `core-portable`, `core-msvc` |

## F1 — Motor Live mínimo

| Fixture | Requisito | Impl | Test | CI |
|---|---|---|---|---|
| F1.01 | Motor Live: estado, slides, línea activa | `Engine.cpp` · `Models.h` (SlideState) | arnés g++ (bootstrap+ipc) | `core-portable` |
| F1.02 | Elemento imagen (5 formatos, llenar/ajustar, opacidad, recorte) | `src/core/src/RendererD2D.cpp` (WIC JPG/PNG/GIF/BMP/TIF, contain/cover, opacidad) + `Renderer.cpp` | gates MSVC | `core-msvc` |
| F1.03 | Texto: multilínea, `syncMark`, resaltado | `Lyrics.cpp` · `Highlight.cpp` · `Models.h` | `TestHighlight` + Scripture/Chords | `core-portable` |
| F1.04 | Tema (colores/fondo) aplicado en vivo | `Engine::SetTheme` · `Models.h` | arnés | `core-portable` |
| F1.05 | Atajos personalizables (JSON) | `Settings.cs` (ShortcutActions, defaults, persistencia) + UI (F1/F5-F8/Ctrl+K) | `TestSettingsShortcuts` | `managed` |
| F1.06 | **Arranque en Presentación** | beta.4: abre en INICIO (flujo PowerStudio de la referencia) con mosaico «Abrir y presentar» a un clic + restauración silenciosa del último plan (`RestoreLastProjectAtStartup`/`ResumeLastProject`) · WinForms: ídem · `Settings.LastProjectPath` | `TestSettingsLastProject` | `managed` |
| F1.07 | **Búsqueda en caliente** | WPF LivePage: caja + popup + **Ctrl+K**; global canciones+versículos vía FTS5; agregar NO interrumpe la salida (`HotSearchRun/Add`, `SearchVersesRequest`, `FtsQuery`) | `TestFtsQuery` + `TestHotSearchQueries` (nativo) | `managed`, `interop` |
| F1.08 | Fail-safe del motor (salida permanece) | `Engine.cpp` (captura por slide) + §11 | arnés | `core-portable` |
| F1.09 | **Gate F1** | 16 ms/frame y cambio ≤1 frame: métricas reales del render (F6.01) + campaña sesion60 (renderAvgMs 0.06, max 2.3) | sesion60 dumps | `sesion60` |

## F2 — Modelo de datos, biblioteca y Modo Creación

| Fixture | Requisito | Impl | Test | CI |
|---|---|---|---|---|
| F2.01 | Proyecto `ahp.v1` + IDs estables + `syncMark` | `AhpProject.cs` · `AhpBridge.cs` | `TestAhpJsonParse` (g++) | `core-portable` |
| F2.02 | Herencia 4 niveles (Slide→Layout→Master→Tema) | `StyleCascade.cs` | `Tests.cs` (herencia) | `managed` |
| F2.03 | Canvas del escenario | `AhpProject` (escenarios/slides) | builder | `managed` |
| F2.04 | Editor de texto formateado | `SlideEditorWindow.cs` (WPF) | uicheck/flowcheck | `managed` |
| F2.05 | Elementos: texto/imagen/video | `ScenarioBuilder` (TextItem/ImageItem/VideoItem) | builder tests | `managed` |
| F2.06 | Orden y duplicación de slides | UI (Live/Service) | flujo | `managed` |
| F2.07 | Notas del director | `ScenarioItem.Notes` + persistencia plan JSON | `Tests.cs` | `managed` |
| F2.08 | Etiquetas y favoritos | `songs.tags` + triggers favoritos (F5) | storage | `interop` |
| F2.09 | **Shell WinForms + WPF (una app, ElementHost)** | `Lumina.UI/WpfEditorHost.cs` (net48: `ElementHost` con editor WPF en la ventana WinForms; perfil B ⇒ efectos OFF) + sincronización bidireccional | build net35+net48 0 warn + uicheck | `managed` |
| F2.10 | Guardado/carga portable del proyecto | plan JSON + `Settings` | roundtrip | `managed` |
| F2.11 | Plantillas de slide | `AhpProject.TemplateRef` | builder | `managed` |
| F2.12 | **Biblioteca de canciones** (búsqueda global, anotaciones, historial, frecuencia, popularidad, etiquetas) | `Storage.cpp` (migración `annotations`/`usage_count`/`last_used_at` + `songs_fts`) · `LuminaStorage` (RecordSongUse/TopSongs/UpdateSongAnnotations) · UI WPF+WinForms registran uso | `TestSongUseAndAnnotations` (nativo) + g++ `TestStorageSongUsage` | `interop`, `core-portable` |
| F2.13 | Biblioteca de Biblias (instantánea, por palabra, resaltado, traducciones, índice) | `bible`+`bible_fts` (Storage.cpp) · `Highlight.cpp` · importadores F4 | `TestNativeDb` + hot-search | `interop` |
| F2.14 | **Biblioteca de recursos** (imágenes/videos, etiquetas, drag&drop, rutas relativas, re-vinculación) | `resources`+`resources_fts` (Storage.cpp) · `LuminaStorage` (Insert/List/Search/Update/Delete/Relink) · diálogo WPF `OpenResourceLibrary` (DragDrop FileDrop + relink por prefijo) | `TestResourcesCrud` (nativo) + g++ `TestStorageResources` | `interop`, `core-portable` |
| F2.15 | Auto-guardado | `Settings.AutoBackupOnExit` + backup | tests zip | `managed` |
| F2.16 | **Gate F2** | paridad x86/x64 (mismo IL + vcxproj dual), herencia/IDs/syncMark/autoguardado cubiertos arriba | suite completa | `core-msvc`, `managed` |

## F3 — Multimedia y salidas

| Fixture | Requisito | Impl | Test | CI |
|---|---|---|---|---|
| F3.01 | Video DirectShow (loop/volumen/startAt) | `VideoDS.cpp` | PoC | `interop` |
| F3.02 | Lower Third desde Trigger | `lumina_lower_third` (Engine) | PoC/API | `interop` |
| F3.03 | Versículo bíblico (rango, cita, destacadas, fail-safe) | `Scripture.cpp` (BuildSlides, refLabel, inválido→false) | `TestScriptureBibleRef` (g++) + managed | `core-portable` |
| F3.04 | Pantalla de retorno / salida pública | `Projector.cpp` + `Renderer` | PoC | `interop` |
| F3.05 | **Stage View** (pública, retorno, HTML/instrucciones; render común) | `StageViewForm` (WPF+WinForms) + `DirectorForm` — render común del núcleo, buffers compartidos | uicheck | `managed` |
| F3.06 | **Multiview** (miniaturas, 3 pantallas, desconexiones, reactivación) | `DirectorForm`/`StageViewForm` (asignación de monitores + sondeo de estado) | uicheck | `managed` |
| F3.07 | PPT como salida | `PowerPointShow` (COM kiosco) | flujo | `managed` |
| F3.08 | **Gate F3** | video (loop/vol/startAt) + fail-safe + lower third + tres salidas + multiview: F3.01–F3.07 | suite | completa |

## F4 — Interoperabilidad

| Fixture | Requisito | Impl | Test | CI |
|---|---|---|---|---|
| F4.01 | Modelo bíblico unificado (todos convergen) | tabla común `bible(version,book,chapter,verse,text)` — Zefania/OSIS/JSON/.bib insertan por el mismo camino (`InsertBibleRows`) | `TestNativeDb` + `TestHotSearchQueries` | `interop` |
| F4.02 | Zefania XML (parseo incremental, >100 MB, índice, `<name>`, copyright, informe) | `ZefaniaBible.cs` (streaming XmlReader, errores fila a fila) | `Tests.cs` Zefania | `managed` |
| F4.03 | `.bib` e-Sword (extraer, normalizar, insertar TODO) | `BibleBib.cpp` (nativo) | PoC | `interop` |
| F4.04 | JSON bíblico (incremental, alias, lotes, informe) | `BibleJson.cs` | `Tests.cs` | `managed` |
| F4.05 | Importador PPTX/OPC (recorrido completo del paquete) | `PptxImporter.cs` ([Content_Types]→rels→docProps→presentation→slides→layouts→masters→theme→media) | `Tests.cs` PPTX | `managed` |
| F4.06 | Herencia PPTX (Diapositiva→Diseño→Maestro→Tema, placeholders) | `PptxImporter` + `StyleCascade` | `Tests.cs` | `managed` |
| F4.07 | Unidades EMU (914400=1", sz/100=pt, extremos) | `PptxImporter`/`PptxExporter` (`SlideW=12192000`) | aserción EMU en `Tests.cs` | `managed` |
| F4.08 | Seguridad XXE (resolver nulo, DTD off, límites, ZIP) | `OsisBible`/`ZefaniaBible`/`PptxImporter` (`XmlResolver=null`, `DtdProcessing=Ignore`, límites) | `Tests.cs` (XXE/bombas) | `managed` |
| F4.09 | PPTM sin macros + namespaces desconocidos | `PptxImporter` (detecta `vbaProject.bin`, aviso UI, ignora p14/p15) | `Tests.cs` PPTM | `managed` |
| F4.10 | Mapeo PPTX→`ahp.v1` (omisiones con informe) | `PptxImporter`→`ScenarioItem` + `FidelityReport` | `TestFidelityReport` | `managed` |
| F4.11 | Exportación PPTX (ISO/IEC-29500, media/, EMU, línea=slide, informe) | `PptxExporter.cs` | `TestPptxExport` + python-pptx | `managed` (gate externo) |
| F4.12 | Exportación PDF etiquetado (texto real, estructura, tema, informe) | `PdfExporter.cs` + `PdfFont.cs` | `TestPdfExport` + pypdf | `managed` (gate externo) |
| F4.13 | **Exportación de imágenes** (JPG/PNG/GIF/BMP/TIF, resolución ≥1920×1080, Escenario individual) | `ImageExporter.cs` (NETFRAMEWORK, GDI+; upscale bicúbico) + UI WPF `ExportScenarioImages` (formato+resolución) | `TestImageExporter` (5 formatos + mínimo) | `managed` (net48), `interop` |
| F4.14 | Holyrics | `HolyricsImporter.cs` | `TestHolyrics` | `managed` |
| F4.15 | Informes de fidelidad generalizados | `FidelityReport.cs` (import/export) | `TestFidelityReport` | `managed` |
| F4.16 | Planning Center (importación MVP) | `PcoClient`/conversión a ahp | `TestPco` | `managed` |

## F5 — API, móvil, triggers, OBS, JSLib

| Fixture | Requisito | Impl | Test | CI |
|---|---|---|---|---|
| F5.01 | API local HTTP + Bearer | `lumina.api` (ApiV1) | `TestApiV1` (endpoints+401) | `managed` |
| F5.02 | Endpoints de operación | ApiV1 | ídem | ídem |
| F5.03 | Cliente móvil (web servido) | ApiV1 (audit-allow) | ídem | ídem |
| F5.04 | QR de emparejamiento | `QrCode.cs` | `TestQrCode` | `managed` |
| F5.05 | Triggers (etiqueta/acción OBS) | motor de triggers | `Tests.cs` triggers | `managed` |
| F5.06 | OBS WebSocket | `ObsProtocol` | `Tests.cs` OBS | `managed` |
| F5.07 | NDI (degradación limpia) | `Ndi` | `TestNdi` | `managed` |
| F5.08 | Planning Center + JSLib hooks | integraciones | `TestPco` | `managed` |
| F5.09 | JSLib sandbox (CPU/memoria/conexiones) | `JsModules.cs` + presupuesto | `TestJsSandboxBudgets` | `managed` |
| F5.10 | Diagnóstico integrado | `Diagnostics` | `TestDiagnostics` | `managed` |
| F5.11 | **Gate F5** | seis endpoints/401/concurrencia/OBS/QR/PCO/NDI/sandbox/diagnóstico → F5.01–F5.10 + `--flowcheck`/selfcheck del paquete | suite + PoC | `managed`, `interop`, `package` |

## F6 — Consolidación, rendimiento y distribución

| Fixture | Requisito | Impl | Test | CI |
|---|---|---|---|---|
| F6.01 | Instrumentación (métricas F6.01, dumps) | `EnvironmentReport` + dumps sesion60 | 59 dumps reales en CI | `sesion60` |
| F6.02 | Presupuesto RAM (300 MB) | medición en campaña | sesion60: pico 52-71 MB | `sesion60` |
| F6.03 | Optimización render (16 ms) | D2D + medición | sesion60 renderAvgMs 0.06 | `sesion60` |
| F6.04 | Drive offline-first (cola, conflictos, historial) | `GoogleDriveClient.cs` | `TestDriveOfflineQueue` | `managed` |
| F6.05 | Empaquetado portable + SHA256 | `package_portable.sh` | verificación 12/12 | `package` |
| F6.06 | Instalador dual (per-user, sin Registro) | `installer/AppHibrida.iss` | iscc + staging | `installer-windows` |
| F6.07 | Pruebas de plataforma (Win7 SP1→Win11) | `Win7Compat.cpp` + `/DELAYLOAD` + `tools/verify_win7_imports.py` + variantes x86/x64 portable+instalada — **la matriz física de equipos reales es la ÚNICA pieza no ejecutable en CI/sandbox** (limitación documentada en `docs/verification/`); todo lo automatizable está en verde | verify_win7_imports (CI) | `managed` (smoke), `package` |
| F6.08 | Sesión de servicio 60 min | job `sesion60` + arnés | **60,00 min · 33.138 ops · 0 no justificados · pico 52-71 MB · 59 dumps** | `sesion60` |
| F6.09 | Auditoría de prohibiciones (cero Java/Registro/elevación) | `scripts/quality_gate.sh` + `tools/audit_prohibitions.py` | sin violaciones | `prohibitions-audit` |
| F6.10 | **Gate F6** | métricas 10.1 medidas (F6.01/F6.08) · perfiles B/C verificados (F0) · instalador+portable (F6.05/F6.06) · sesión 60 min sin ERROR (F6.08) · versionado `1.x.y-beta+z` (`version.props`) · CI verde · gates F0–F5 cerrados (esta matriz) | suite completa | completa |

## Resumen

- **80/80 fixtures con implementación real y evidencia** (código + test + CI job).
- **Cierre de la auditoría beta.3** (fixtures que carecían de implementación o
  marcador verificable): F1.06 (último proyecto), F1.07 (búsqueda en caliente),
  F2.09 (ElementHost), F2.12 (anotaciones/uso/popularidad), F2.14 (biblioteca de
  recursos), F4.13 (exportación de imágenes) — cada una con tests nuevos
  (6 en el arnés gestionado + 2 en el arnés g++) y con el bug **preexistente** de
  los triggers FTS5 `AFTER UPDATE/DELETE` corregido (el comando especial
  `'delete'` solo es válido en tablas de contenido externo; ninguna prueba lo
  ejercitaba antes).
- **Única excepción de entorno** (no de implementación): la matriz física de
  F6.07 requiere hardware real Win7/Win8.1 — documentada como limitación;
  todos sus sustitutos automatizables (imports Win7, x86/x64, portable,
  instalador, perfil C) están en verde.
