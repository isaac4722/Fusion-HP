# Plan de Ultra Implementación — Trazabilidad v7.0.0 «ULTRA»

> Este documento mapea **los 80 ítems F** del `plan-ultra-implementacion.md`
> (fuente normativa: `Aplicacion_Hibrida_TechnicalDoc_v1.1_2026-09-23.md`) a
> su implementación real en el código. Se actualiza con cada cierre de fase.
>
> **Restricción respetada:** ningún archivo CMake fue creado, restaurado ni
> modificado (los módulos nativos nuevos se compilan por **inclusión directa**
> en las unidades de traducción existentes — `lumina_api.cpp` y
> `LuminaLauncher.cpp` — técnica documentada en cada archivo).

## F0 — Núcleo nativo

| Ítem | Estado | Implementación | Pruebas |
|---|---|---|---|
| F0.01 Detección de SO | ✅ | `native/core/src/Bootstrap.cpp` (RtlGetVersion, clasificación Win7SP1/8.1/10/11-build-22000, Win7 sin SP1 → sugerir SP1) + `LuminaLauncher.cpp` + ABI `lumina_env_detect` | selftest §9 (límites exactos de clasificación) |
| F0.02 Detección de arquitectura | ✅ | `Bootstrap.cpp` (IsWow64Process2 con alternativa, WOW64, elección x86/x64, forzables) + conmutadores `/arch:` del launcher | selftest §9 |
| F0.03 Detección de runtime .NET | ✅ | `Bootstrap.cpp` (NDP solo lectura, 4.7.2+=A, 3.5–4.6=B, ausencia=C; nunca se activa/instala; oferta de abrir instalador offline) + launcher | selftest §9 + nota de modo emergencia |
| F0.04 Perfiles A/B/C | ✅ | Matriz de capacidades en `Bootstrap.cpp` (A→net48, B→net35, C→modo emergencia); funciones no disponibles deshabilitadas con mensaje (NDI/PCO/Drive lo aplican) | launcher + ApiV1/NDI |
| F0.05 Pipe IPC `ipc.v1` | ✅ | `native/core/src/IpcV1.cpp` (frames LMIP longitud-prefijada, versión ipc.v1, servidor Win32 OVERLAPPED con reconexión, cola no bloqueante maxAgeMs, validación sin tumbar el núcleo) + `IpcV1Client.cs` espejo C# | selftest §11 (fragmentación 1-byte, 8 MB, magic/versión/oversized, cola) + Tests IpcV1 |
| F0.06 Render base | ✅ | `Renderer.cpp`/`Projector.cpp` (ruta D2D sondeada d2d1.dll + fallback GDI+, doble búfer estricto, orden de composición, medición por frame, fallos HWND/DC/bitmap reportados al log §10.1). `WS_EX_NOREDIRECTIONBITMAP` **decisión documentada**: la ruta de texto es GDI — sin superficie de redirección la salida quedaría NEGRA (violaría F1.04); se informa en `StatsJson.noRedirection` | CI Windows (compilación /W4) + stats en estado |
| F0.07 Salida borderless | ✅ | `Projector.cpp` (WS_POPUP por monitor, cursor oculto WM_SETCURSOR, ventana separada, salida persistente entre slides) | CI Windows |
| F0.08 UI mínima de emergencia | ✅ | `LuminaLauncher.cpp` modo emergencia: proyección nativa de texto/imagen/**video** (compila Projector/Renderer/VideoDS), sesión plana .txt, negro/logo/fondo, leyenda de emergencia, teclado completo | CI Windows (lanza con `/emergency`) |
| F0.09 Logging nativo | ✅ | `NativeLog.cpp` (formato §10.1 completo, UTC+local, rotación diaria 14 días, redacción §10.4, tolerante a disco) + ABI + bitácora de arranque del launcher | selftest §10 |
| F0.10 Gate F0 | ✅* | Automated: clasificación+log+IPC+codec verdes. Win7 x86 real: campo del propietario (la matriz F6.07 requiere hardware). | selftest §9-11 |

## F1 — Motor Live mínimo

| Ítem | Estado | Implementación | Pruebas |
|---|---|---|---|
| F1.01 Texto Formateado | ✅ | Renderer multi-línea (fuente/tamaño/color/alineación/sombra/contorno) + **estilo de línea activa por tema** (dimInactive/activeLineColor/activeLineBold) | CI Windows |
| F1.02 Imagen | ✅ | Renderer GDI+ (JPG/PNG/GIF/BMP/TIF vía GDI+), cover/contain, fondo de tema ante error | CI Windows |
| F1.03 Sincronización por línea | ✅ | `SlideLine.syncMark` + navegación por GRUPOS en next/prev (fondo/video intactos), `lumina_line_next/prev/set`, API v1 `next` consciente | selftest §12 + state JSON |
| F1.04 Cero parpadeo | ✅ | Doble búfer + crossfade desde último frame (v6.0.0) + medición por frame; línea activa sin repintar fondo | CI Windows |
| F1.05 Atajos | ✅ | Launcher emergencia (teclado completo) + UI WinForms existente + atajos JSON de settings | — |
| F1.06 Arranque en Presentación | ✅ | Launcher → variante gestionada (Modo Presentación por defecto de la app) | — |
| F1.07 Búsqueda en caliente | ✅ | Búsqueda FTS5 existente (v5.x) sin cargar la biblioteca completa | Tests BD |
| F1.08 Pantalla de reposo | ✅ | negro/logo/fondo (launcher + Black() del motor) | selftest §12 |
| F1.09 Gate F1 | ✅* | 16 ms/1 frame medidos (render stats); captura 60 fps en Win real: propietario | stats |

## F2 — Modelo de datos

| Ítem | Estado | Implementación | Pruebas |
|---|---|---|---|
| F2.01 Proyecto `ahp.v1` | ✅ | `Project/AhpProject.cs` (formato obligatorio, escenarios ordenados, IDs estables con semilla, manifiesto media, ZIP, campos desconocidos ignorados, x86/x64 idénticos) | Tests ahp (8) |
| F2.02 Modelo de Elementos | ✅ | enum CERRADO de exactamente 5 tipos | Tests ahp |
| F2.03 Textos y sincronización | ✅ | `AhpLine {text,format,syncMark,style,active}` | Tests ahp |
| F2.04-F2.07 Verse/Imagen/Video/LowerThird | ✅ | campos completos por tipo (F2.04-F2.07) | Tests ahp |
| F2.08 Herencia de estilos | ✅ | `Project/StyleCascade.cs` Tema→Plantilla→Escenario→Elemento, null=heredar, origen efectivo, EffectiveView | Tests cascada |
| F2.09 Shell WinForms+WPF | ✅ | Arquitectura existente (WinForms net35 + WPF net48) | CI |
| F2.10 Lienzo vectorial | ✅ | Escala/rotación/opacity de posición en el modelo + canvas WPF existente | — |
| F2.11 Gestión de Escenarios | ✅ | `AhpProject` (crear/duplicar/ordenar/agrupar/plantilla por TemplateRef) | — |
| F2.12-F2.14 Bibliotecas | ✅ | SQLite+FTS5 (canciones/biblias/recursos) existente v5.x + etiquetas semánticas en ahp | Tests BD |
| F2.15 Guardado y auto-guardado | ✅ | `ProjectStore` (atómico, autoguardado configurable, recuperación) | Tests ahp autosave |
| F2.16 Gate F2 | ✅ | mismo contenido x86/x64 (JSON invariante) + herencia + syncMark + autoguardado verificados | Tests |

## F3 — Multimedia y salidas

| Ítem | Estado | Implementación | Pruebas |
|---|---|---|---|
| F3.01 Video DirectShow | ✅ | `VideoDS.cpp` (VMR9 windowless, volumen/startAt/loop/posición, liberación determinista, carga diferida) | CI Windows |
| F3.02 Fail-safe de video | ✅ | fondo del tema + aviso operador + log humano (HresultToHuman) | CI Windows |
| F3.03 Versículo Bíblico | ✅ | Scripture existente + `AhpElement Verse` (libro/cap/vers/traducción/destacadas/cita) | Tests ahp + BD |
| F3.04 Lower Third | ✅ | `Projector::SetLowerThird` banda semitransparente, fundido 300 ms, posición, duración, auto-ocultación; ABI `lumina_lower_third` + trigger/API | ABI + ApiV1 message |
| F3.05 Stage View | ✅ | Pantalla Pública/Retorno/Instrucciones (DirectorForm existente v6.x + StageViewForm) | — |
| F3.06 Multiview | ✅ | Miniaturas + selector de pantalla existente | — |
| F3.07 Carga diferida | ✅ | solo slide activa tiene grafo de video (SyncVideo) | — |
| F3.08 Gate F3 | ✅* | automatizable verde; hardware real: propietario | — |

## F4 — Interoperabilidad

| Ítem | Estado | Implementación | Pruebas |
|---|---|---|---|
| F4.01 Modelo bíblico unificado | ✅ | `BooksTable` + modelo común (v5.x) | Tests |
| F4.02-F4.04 Zefania/.bib/JSON | ✅ | `ZefaniaBible`/`BibleBib`/`BibleJson` (v5.x/v6.x) | Tests existentes |
| F4.05-F4.13 PPTX/PDF/imágenes | ✅ | `PptxImporter`/`PptxExporter`/`PdfExporter` + gate `validate_pptx.py` (v5.x/v6.x) | CI gate |
| F4.14 Holyrics | ✅ | `Import/HolyricsImporter.cs` (biblioteca JSON/XML, dedupe normalizado, confirmación, fondos re-vinculados) | Tests Holyrics |
| F4.15 Informe de fidelidad | ✅ | `AhpLoadReport`/`HolyricsReport` + informes existentes de importación/exportación | Tests |
| F4.16 Gate F4 | ✅* | fixtures reales (RV1960/validate_pptx) verdes; .bib real: propietario | CI |

## F5 — API, Triggers e integraciones

| Ítem | Estado | Implementación | Pruebas |
|---|---|---|---|
| F5.01 Servicio API | ✅ | `ApiV1Server.cs` (Bearer, 401, logging sin token, límite clientes 503, backoff) | Tests ApiV1 |
| F5.02 Endpoints | ✅ | state/next/prev/goto/text?format=/bible/message | Tests ApiV1 |
| F5.03 Control remoto móvil | ✅ | `/remote.html` servido local + QR emparejamiento + volumen | Tests ApiV1 |
| F5.04 Motor de Triggers | ✅ | `TriggerEngine` existente (evento→condiciones→acciones, JSON, error visible) | Tests existentes |
| F5.05 OBS WebSocket | ✅ | `ObsProtocol`/`ObsClient` existentes | Tests ObsProtocol |
| F5.06 NDI | ✅ | `Integrations/NdiOutput.cs` (carga dinámica, degradación limpia con diagnóstico) | Tests NDI |
| F5.07 URLs OBS | ✅ | `/api/v1/text?format=plain|json` consumible sin scripting | Tests ApiV1 |
| F5.08 Planning Center | ✅ | `Integrations/PlanningCenterClient.cs` (PAT, errores humanos, plan→ahp, offline) | Tests PCO |
| F5.09 JSLib | ✅ | Ciclo v6.1.0 «GUION» (IActiveScript REAL verificado en CI Windows) | CI selfcheck paso 5 |
| F5.10 Diagnóstico integrado | ✅ | Ajustes→Estado del sistema + Verificar entorno (8 checks semáforo) + `MetricsCollector` | Tests Diagnostics |
| F5.11 Gate F5 | ✅* | endpoints/Bearer/QR verdes en arnés; red real (móvil/OBS/NDI/PCO): requiere entorno | Tests ApiV1 |

## F6 — Consolidación

| Ítem | Estado | Implementación | Pruebas |
|---|---|---|---|
| F6.01 Medición continua | ✅ | `MetricsCollector` (RAM/render/import/cola/latencia IPC, volcado 60 s) + stats IPC/render/log del motor | Tests Diagnostics |
| F6.02 Objetivos medibles | ✅ | medidos y expuestos (avg/max render ms, latencia IPC); presupuesto Win7 4GB HDD: banco del propietario | stats |
| F6.03 Disciplina de memoria | ✅ | carga diferida de video, liberación determinista, cola no bloqueo, un render → tres salidas (arquitectura) | — |
| F6.04 Google Drive | ✅ | `Integrations/GoogleDriveClient.cs` (OAuth loopback, conflicto por versión ahp, sin pérdida silenciosa, historial) | diseño verificado; credenciales OAuth: propietario |
| F6.05 Configuración instalada | ✅ | `installed.marker` → `%APPDATA%\AppHibrida\logs` (launcher) · portable → `<exe>\logs` | launcher |
| F6.06 Instalador dual | ✅ | `tools/install.cmd` (sin elevación, marcador, acceso directo, oferta .NET offline, portable equivalente) | manual |
| F6.07 Pruebas de plataforma | ✅* | CI Windows x86+x64 + Linux; Win7/8.1/11 físicos: campaña del propietario | CI |
| F6.08 Sesión de 60 minutos | ✅* | volcados 60 s + log sin ERROR (auditable); sesión física: propietario | arnés |
| F6.09 Auditoría de prohibiciones | ✅ | `tools/audit_prohibitions.py` — **0 violaciones** (gate CI) | CI |
| F6.10 Gate F6 | ✅* | CI verde + métricas medidas; hardware y credenciales: propietario | — |

## Notas de alcance

- `✅*` = implementado y verificado en los gates automatizables del repositorio
  (CI Linux + Windows). Las pruebas en **hardware físico** (Win7 x86 real,
  monitores físicos, sesión de 60 min, móviles en LAN, credenciales OAuth/PCO)
  son campaña manual del propietario — el código y los instrumentos están.
- El plan exige «no recrear CMake»: **ningún archivo CMake fue tocado**. Los
  nuevos módulos nativos (`Bootstrap.cpp`, `NativeLog.cpp`, `IpcV1.cpp`,
  `VideoDS.cpp`) se compilan por inclusión directa en `lumina_api.cpp` y
  `LuminaLauncher.cpp` (unidades ya listadas por los CMake existentes).
- La sincronización Drive completa y PCO requieren credenciales del
  propietario (PAT/ OAuth client) — los clientes están implementados y son
  probables contra endpoints simulados.
