# Fusion HP v3.0.0 — Reestructuración completa (los 7 bugs del prototipo, resueltos)

**Fusion HP** es un presentador litúrgico híbrido (núcleo nativo C++ + capa C#/.NET Framework) que opera desde **Windows 7 SP1 x86** hasta **Windows 11 x64**, sin Java, sin .NET Core y sin escribir en el Registro de Windows. Esta versión parte de cero en cuanto a distribución: las releases anteriores fueron eliminadas y el prototipo fue reestructurado para resolver los defectos reportados.

## Qué se corrigió (de lo reportado)

1. **El cargador de Escenarios no mostraba nombres** → nuevo cargador `Abrir proyecto`: recientes con el NOMBRE del proyecto, número de escenarios y los TÍTULOS de cada escenario visibles antes de abrir (lectura rápida de cabecera ahp.v1). Ya no se elige un archivo a ciegas.
2. **La ventana de proyección no respetaba la pantalla elegida** → el monitor seleccionado en Configuración ahora viaja al núcleo (IPC `monitor public|stage`) y la salida borderless fullscreen se posiciona donde el operador manda; con un solo monitor, Configuración avisa honestamente.
3. **Cerrar con la X dejaba el proceso vivo / parecía duplicarse** → el estudio nativo maneja `WM_CLOSE` (persiste borrador y recientes) y termina el bucle del núcleo limpiamente; el Motor guarda su sesión en TODOS los caminos de salida; los mutex de instancia evitan duplicación real.
4. **La configuración no se aplicaba al arrancar** → monitores de salida, logo de reposo, avance y reloj se aplican al abrir la app, sin pasar por Configuración. *(v4.0.0: la API HTTP, el control remoto y OBS fueron eliminados por decisión del usuario — producto 100 % local.)*
5. **La importación PPTX extraía en vez de cargar el original** → nuevo **proyector PPTX directo**: lee el archivo ORIGINAL tal cual (contenedor OPC, herencia de 4 niveles, EMUs) y lo entrega al Motor SIN convertir ni guardar nada — cada carga re-lee el original, con PowerPoint o SIN él (ya no exige PowerPoint; el COM se usa solo si existe, por fidelidad). Informe de fidelidad incluido.
6. **Las bibliotecas exigían búsqueda para mostrar algo** → Cantos y Biblia están disponibles DIRECTAMENTE: lista completa de cantos sin tope (el tope de 200 del estudio nativo fue eliminado), árbol bíblico completo de 66 libros, búsqueda instantánea opcional (≤200 ms).
7. **El flujo generaba PPTX para cada ocasión** → flujo Holyrics consolidado: los cantos se cargan desde la base de datos (`cancionero.fdb`) y se proyectan directamente (DB → Motor por IPC `motor.load`); una prueba automatizada verifica que proyectar un canto NO genera ningún archivo.

## Qué incluye

- **Núcleo nativo C++** (`FusionHP.exe`, /MT, x86 y x64): bootstrap con detección de SO/arquitectura/.NET (perfiles A/B/C), salida borderless sin parpadeo (Direct2D con fallback GDI+ de doble buffer), sincronización línea por línea, video DirectShow con fail-safe, pantalla de reposo (negro/logo/tema), Stage View de músicos y servidor IPC `ipc.v1` con 30+ comandos.
- **Estudio** (`FusionStudio.exe`, .NET Framework 4.8): modos Inicio/Estudio/Presentación, biblioteca directa de Cantos y Biblia, programa con sub-líneas clicables, previsualización con el mismo modelo de render que la salida, editor WPF con lienzo estilo PowerPoint, herencia de estilos de 4 niveles (Tema → Plantilla → Escenario → Elemento) aplicable en caliente, clasificador e historial de uso.
- **Variante Lite** (`FusionStudio.Lite.exe`, .NET 3.5 SP1): perfil B con motor Live completo, importadores/exportadores y API.
- **Perfil C**: sin .NET, el núcleo abre su estudio nativo completo (Inicio/Editor/Presentar en Win32/GDI+) y proyecta igualmente.
- **Interoperabilidad**: Biblias **Zefania XML**, **e-Sword .bib/.bblx 9+** (descifrado Twofish), **JSON** y **TSV**; cantos de himnario JSON y respaldo de Holyrics; **PPTX original tal cual** (COM o nativo) e importación a Escenarios; exportación **PPTX (ISO/IEC-29500)**, **PDF** e **imágenes PNG 1080p**.
- **100 % local (v4.0.0)**: sin API de red, sin OBS, sin control remoto y sin Triggers — eliminados por decisión del usuario; nada escucha ni habla por la red.
- **Diagnóstico**: Ayuda → Estado del sistema con autotest, log estructurado rotativo y mensajes de error en lenguaje humano con «Copiar detalles técnicos».

## Instalación

1. Ejecuta el instalador: instala el binario correspondiente a tu arquitectura (x86/x64) y detecta .NET; sin permisos de administrador si instalas en tu perfil.
2. Modo **portable**: descomprime el ZIP y ejecuta `FusionHP.exe`; todos los datos quedan en la carpeta `datos` junto al programa.
3. Primer arranque: Cantos y Biblia ya están disponibles (RV1960, NVI, RVG, RVR1909 empaquetadas).

## Verificación (criterios de aceptación 12.3)

- F0/F6: arranque dual x86/x64 con detección automática; perfil C sin .NET proyecta texto/imágenes/video.
- F1: transición ≤ 16 ms sin frame negro (ventanas persistentes + doble buffer); cambio de línea ≤ 1 frame.
- F4: PPTX con herencia de 4 niveles y EMUs se proyecta/importa con informe de fidelidad; `.pptm` sin ejecutar macros; RV1960 con búsqueda ≤ 200 ms.
- F5 (sustituido en v4.0.0): verificación automatizada de que proyectar no abre puertos ni genera archivos.
- Tests: núcleo 113 comprobaciones + capa C# (net48 y net35) en el arnés propio; gate de calidad `quality_gate.sh` en cada commit; CI x86+x64.

## Limitaciones conocidas

- Los módulos e-Sword con cifrado completo de archivo (comprados/protegidos) no pueden leerse: se informa y sugiere la edición Zefania equivalente.
- Los `.pptm` se proyectan/importan sin ejecutar macros; animaciones y transiciones de PowerPoint no se importan (MVP).
- Exportación a video MP4, coautoría en nube y salida NDI quedan para fases posteriores.

## Referencia normativa

Documento Técnico v1.1 (2026-09-23): `spec/Aplicacion_Hibrida_TechnicalDoc_v1.1_2026-09-23.md` en el repositorio.
