# Fusion HP v4.0.0 — GUI web consolidada y producto 100 % local

**Fusion HP** es un presentador litúrgico híbrido (núcleo nativo C++ + capa C#/.NET Framework) que opera desde **Windows 7 SP1 x86** hasta **Windows 11 x64**, sin Java, sin .NET Core y sin escribir en el Registro de Windows. La distribución parte de cero: las releases anteriores fueron eliminadas.

## Qué cambió en v4.0.0

1. **Sin API, sin OBS, sin control remoto — a petición del usuario**: el servidor HTTP, el cliente obs-websocket, la página `/remote`, el Mando (móvil y nativo) y el motor de Triggers fueron **eliminados por completo** (código, configuración, diagnóstico, pruebas y documentación). El programa es **100 % local**: nada escucha ni habla por la red; la única comunicación es el IPC interno `ipc.v1` entre los propios ejecutables.
2. **GUI/UX de la web consolidada en C++ y C#** (petición: «la GUI de la web como base»):
   - Nueva pestaña **Temas** en la biblioteca C#: los 6 temas web aplicables al elemento o a todo el proyecto, re-resueltos en caliente.
   - **Biblia rápida (tecla G)** como overlay: cita directa («Jn 3:16», «1co 13»), **favoritos de la web** sin escribir nada, y casilla **Tercio** para insertar el versículo como lower third — sin salir del modo Presentar.
   - **Miniaturas** de la primera diapositiva en la lista del programa (paridad con el programa de la web).
   - **Proyectos recientes con nombre** y número de escenarios en la portada Inicio (paridad con las tarjetas de la web).
   - El estudio nativo C++ conserva la GUI web replicada (Inicio, PowerStudio con 8 pestañas y Backstage, consola Presentar con Biblia rápida G).
3. **Assets de la web dentro del programa**: las fuentes Outfit, Cormorant Garamond (Media, SemiBold y **Bold instanciado del variable de la web**) y Libre Baskerville, los 6 fondos, el logo y 62 iconos Tabler en 4 tintas viajan en `resources/` y se cargan vía PrivateFontCollection — sin instalar nada en el sistema.
4. **4 biblias completas en español incluidas** y autoinstaladas en el primer arranque: **RV1960** (31 036), **NVI** (31 103), **RVG** (31 102) y **RVR1909** (31 084 versículos), con verificación automatizada de 66 libros y conteo por versión.
5. **Canciones: la base de datos manda (flujo Holyrics)**: el banco de cantos se carga **una sola vez** en `cancionero.fdb`; proyectar es **consultar la BD → Motor** (un elemento por sección Verso/Coro), **sin generar PPTX ni archivos temporales** — una prueba automatizada garantiza 0 archivos al proyectar. El PPTX solo aparece cuando el operador lo pide (importar el original tal cual, o exportar).

## Qué se corrigió (de lo reportado al prototipo, se mantiene resuelto)

1. El cargador de Escenarios muestra **nombre, N escenarios y títulos** antes de abrir.
2. La ventana de proyección respeta el **monitor elegido** (viaja al núcleo por IPC).
3. Cerrar con la X **no duplica ni deja procesos zombi**; el Motor persiste su sesión.
4. La configuración (monitores, logo, avance, reloj) **se aplica al arrancar**.
5. El PPTX se proyecta **tal cual** (COM si existe, o lector nativo OPC sin PowerPoint).
6. Cantos y Biblia **directamente disponibles** sin necesidad de buscar (lista sin tope, 66 libros).
7. **Cero PPTX generados** al proyectar cantos (DB → Motor, verificado por prueba).

## Qué incluye

- **Núcleo nativo C++** (`FusionHP.exe`, /MT, x86 y x64): bootstrap con detección de SO/arquitectura/.NET (perfiles A/B/C), salida borderless sin parpadeo (Direct2D con fallback GDI+ de doble buffer), sincronización línea por línea, video DirectShow con fail-safe, pantalla de reposo (negro/logo/tema), Stage View de músicos y servidor IPC `ipc.v1` con 30+ comandos.
- **Estudio** (`FusionStudio.exe`, .NET Framework 4.8): modos Inicio/Estudio/Presentación, biblioteca directa (Cantos, Biblia, Escenarios, Medios, **Temas**), programa con miniaturas y sub-líneas clicables, previsualización con el mismo modelo de render que la salida, editor WPF con lienzo estilo PowerPoint, herencia de estilos de 4 niveles (Tema → Plantilla → Escenario → Elemento) aplicable en caliente, clasificador, Stage View (alertas/temporizador/tono-BPM) e historial de uso con CSV.
- **Variante Lite** (`FusionStudio.Lite.exe`, .NET 3.5 SP1): perfil B con motor Live completo, biblioteca, importadores/exportadores y la misma GUI.
- **Perfil C**: sin .NET, el núcleo abre su estudio nativo completo (Inicio/Editor/Presentar en Win32/GDI+) y proyecta igualmente.
- **Interoperabilidad**: Biblias **Zefania XML**, **e-Sword .bib/.bblx 9+** (descifrado Twofish), **JSON** y **TSV**; cantos de himnario JSON y respaldo de Holyrics; **PPTX original tal cual** (COM o nativo) e importación a Escenarios; exportación **PPTX (ISO/IEC-29500)**, **PDF** e **imágenes PNG 1080p** — siempre por decisión explícita del operador.
- **Diagnóstico**: Ayuda → Estado del sistema con autotest (render, núcleo, permisos), log estructurado rotativo y mensajes de error en lenguaje humano con «Copiar detalles técnicos».

## Instalación

1. Ejecuta el instalador: instala el binario correspondiente a tu arquitectura (x86/x64) y detecta .NET; sin permisos de administrador si instalas en tu perfil.
2. Modo **portable**: descomprime el ZIP y ejecuta `FusionHP.exe`; todos los datos quedan en la carpeta `datos` junto al programa.
3. Primer arranque: Cantos y Biblia ya están disponibles (RV1960, NVI, RVG, RVR1909 empaquetadas); el cancionero se crea como una única base de datos.

## Verificación (criterios de aceptación)

- **100 % local**: `settings.json` sin claves de red (prueba automatizada); sin HttpListener/WebSocket en el producto; el estudio nativo y la consola no exponen ninguna salida de red.
- **Biblias completas**: prueba automatizada lee las 4 versiones de `resources/data/bibles` y verifica 66 libros y ≥30 000 versículos por versión.
- **Flujo de cantos**: prueba automatizada «proyectar un canto NO crea archivos» (ni .pptx ni .ahp).
- **Temas**: prueba automatizada del cambio de tema en caliente (Clásico → Solemne → fuente Cormorant Garamond) y de la anulación por elemento.
- **Dual**: CI compila x86 + x64 (núcleo) y net48 + net35 (administrado), con suites nativas (109+ pruebas) y administradas (55+ pruebas) en verde.
