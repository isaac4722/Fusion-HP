# Decisiones v4.0.0 — Fusion-HP

Registro de decisiones del ciclo de reestructuración v4.0.0 (26/09/2026). Este
documento es la fuente de verdad de los cambios de alcance respecto al
Documento Técnico v1.1.

## D1 — Eliminación total de la automatización de red (revoca SPEC §8)

**Decisión del usuario:** «Elimina la API, que no quede registro total para nada
(No OBS o RemoteControl)».

Alcance eliminado:

| Pieza | Archivos afectados |
|---|---|
| Servidor API HTTP (6 endpoints + token) | `Services/ApiServer.cs` (eliminado) |
| Cliente obs-websocket 5.x | `Services/ObsClient.cs` (eliminado) |
| Motor de Triggers (eventos → acciones) | `Services/TriggerEngine.cs` (eliminado) |
| Cableado OBS de la GUI | `Ui/MainForm.Obs.cs` (eliminado) |
| Mando móvil en C# | `Ui/MandoForm.cs` (eliminado) |
| Modo Mando del estudio nativo | `NativeStudio.{h,cpp}`, `.Editor.cpp`, `.Present.cpp` (sin `Mode::Mando`, sin `CMD_MANDO`, sin `PaintMando`) |
| Claves de configuración | `AppSettings`: `apiEnabled/apiPort/apiToken/maxApiClients/obsEnabled/obsHost/obsPort/obsPassword` (eliminadas) |
| Diagnóstico | autotest de API eliminado; queda la comprobación genérica de la pila TCP |
| Pruebas | `TestApiServerEndpoints`, `TestObsAuthHash`, tests de Triggers (eliminados) |

**Criterio sustituto (F5):** el proceso no abre sockets de escucha ni clientes
de red. `V40Tests.TestSettingsCarryNoNetworkKeys` verifica que `settings.json`
no contiene rastro de red; la superficie de red del producto es cero.

## D2 — GUI/UX de la web como base, en C++ y C#

La GUI de la referencia web (`H-P-Web-Version-Ref`) es el modelo: el estudio
nativo C++ ya la replicaba (v2.3) y en v4.0.0 la GUI C# cierra las brechas
restantes:

1. Pestaña **Temas** en la biblioteca (aplicar al elemento o a todo, en caliente).
2. **Biblia rápida G** como overlay (`QuickVerseForm`) con favoritos de la web y
   modo **Tercio** (versículo como lower third).
3. **Miniaturas** en la lista del programa (`SlidePreview.RenderSlide` estático).
4. **Recientes con nombre** en Inicio (tarjetas de la portada web).

Las funciones únicas de las betas 1 se conservan (son del repo, no de la web):
acordes con transposición, resaltado en pantalla, historial compartido
C#/C++, Stage View de músicos (alertas/temporizador/tono-BPM), KeepEngineAlive
y teclado autónomo del Motor.

## D3 — Assets de la web dentro del programa

`resources/` viaja con el instalador y el portable: fuentes (Outfit Regular/
SemiBold/Bold, Cormorant Garamond Medium/**SemiBold/Bold** —instanciados del
variable de la web—, Libre Baskerville Regular/Italic), 6 fondos JPG, logo
Lumina y 62 iconos Tabler × 4 tintas. Carga única vía PrivateFontCollection /
carga nativa; nada se instala en el sistema.

## D4 — Biblias: 4 versiones completas en español

Empaquetadas y autoinstaladas en el primer arranque: **RV1960 (31 036), NVI
(31 103), RVG (31 102), RVR1909 (31 084)**. `V40Tests.TestBundledBiblesAreComplete`
verifica 66 libros y ≥30 000 versículos por versión. Importación adicional:
Zefania XML, e-Sword .bib/.bblx (Twofish), JSON, TSV.

## D5 — Cantos: la BD manda, cero PPTX

El cancionero vive en **una sola base de datos** (`cancionero.fdb`) cargada una
vez al arrancar. Proyectar = **consulta BD → Motor** (IPC `motor.load`), un
elemento por sección (Verso/Coro). No se generan PPTX ni archivos temporales;
`TestProjectionDoesNotGeneratePptx` lo garantiza. El PPTX solo interviene por
decisión explícita del operador (proyectar el original tal cual, o exportar).
La alternativa «archivo temporal» del enunciado queda documentada como
innecesaria: la consulta directa de la BD evita cualquier duplicación.

## D6 — Versiones de runtime

Mínimos inalterados: .NET Framework **3.5 SP1** (perfil B, `FusionStudio.Lite.exe`)
y **4.8** (perfil A, `FusionStudio.exe`); perfil C = estudio nativo C++ sin .NET.
Prohibidos Java/JRE y .NET Core/5+.
