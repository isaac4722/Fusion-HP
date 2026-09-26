# Reestructuración v3.0.0 — Decisiones y trazabilidad

**Fecha:** 2026-09-26 · **Fuente normativa:** `spec/Aplicacion_Hibrida_TechnicalDoc_v1.1_2026-09-23.md`

## Contexto

El prototipo v2.3.0 compilaba en CI con 151 pruebas verdes, pero fallaba en 7 puntos de
experiencia de usuario reportados por el propietario. La reestructuración v3.0.0 partió
de cero **en cuanto a distribución** (todas las releases y tags anteriores fueron
eliminadas) y reescribió los subsistemas defectuosos, conservando el motor probado
donde la estabilidad lo exige (principio «fiabilidad por encima de complejidad»,
SPEC §3.1). Cada pieza se cerró con un commit convencional y pruebas.

## Los 7 defectos → causas raíz → corrección

| # | Defecto reportado | Causa raíz (diagnóstico) | Corrección v3.0.0 |
|---|---|---|---|
| 1 | Cargador de Escenarios no muestra nombres | `OpenFileDialog` crudo; recientes solo en C++ | `ProjectOpenForm` + `AhpProjectInfo` (cabecera ahp.v1: nombre, títulos) + `RecentProjects` persistidos |
| 2 | Ventana de proyección no respetaba pantalla | `SettingsForm` guardaba `PublicMonitor` pero NADIE enviaba el comando IPC `monitor` | `ApplySettings` envía `monitor {index,output}` al núcleo; aviso de un solo monitor |
| 3 | Cerrar con la X duplicaba / dejaba proceso vivo | `NativeStudio` sin `WM_CLOSE`: `DefWindowProc` destruía la ventana sin `PostQuitMessage` → bucle zombi | `WM_CLOSE` persiste borrador y destruye; `WM_DESTROY` → `PostQuitMessage`; `App::Run` guarda sesión del Motor en toda salida |
| 4 | Modo API no funciona | `ApplySettings()` solo se invocaba al guardar Configuración → la API nunca arrancaba sola; OBS había sido eliminado en v2.3 | `ApplySettings()` al abrir la app; interruptor rápido en consola; `ObsClient` obs-websocket 5.x restaurado (SHA-256, reconexión); `TriggerEngine.obs.scene` |
| 5 | PPTX extraía en vez de cargar el original | Única vía sin PowerPoint era conversión a Escenarios; exigir PowerPoint era callejón | `PptxDirectProjector`: lee el ORIGINAL (OPC, herencia 4 niveles, EMU) y entrega el programa al Motor sin persistir nada; COM como vía preferente si existe |
| 6 | Bibliotecas requerían búsqueda | C# ya directo; C++ con tope de 200 cantos en `SongLibrary::Search` | Tope eliminado (lista directa completa); prueba nativa con 250 cantos |
| 7 | Flujo generaba PPTX por cada ocasión | Inexistente ya (DB→Motor) pero sin prueba que lo garantizara | Prueba `TestProjectionDoesNotGeneratePptx`: proyectar canto crea 0 archivos |

## Piezas (commits convencionales)

1. `fix(nucleo)` — ciclo de vida nativo + biblioteca directa sin tope.
2. `fix(api)` — API desde el arranque + monitor aplicado + OBS WebSocket.
3. `feat(pptx)` — proyección del original tal cual sin PowerPoint.
4. `feat(gui)` — cargador de Escenarios con nombres.
5. `test(v3.0.0)` — cobertura de los 7 bugs.
6. `docs+release` — versión 3.0.0, notas, README, esta documentación.

## Referencia GUI/UX

La referencia web (`H-P-Web-Version-Ref`, clon Fluent de PowerPoint en español:
acento terracota `#C43E1C`, Segoe UI, cinta de 8 pestañas, lista directa de cantos,
biblia rápida con `G`, sub-líneas, pausas B/C/L) sigue siendo el estándar visual.
La capa C# y el estudio nativo C++ replican su retícula y microcopy; v3.0.0 corrige
los flujos donde el prototipo se quedaba corto (cargador, PPTX, API, bibliotecas).
