# Fusion HP — notas de la versión

**Fusion HP** es un presentador litúrgico híbrido (núcleo nativo C++ + capa C#/.NET Framework) que opera desde **Windows 7 SP1 x86** hasta **Windows 11 x64**, sin Java, sin .NET Core y sin escribir en el Registro de Windows.

## Qué incluye esta versión

- **Núcleo nativo C++** (`FusionHP.exe`, compilado con /MT, x86 y x64): bootstrap con detección de entorno (SO, arquitectura, .NET), salida de proyección borderless sin parpadeo (Direct2D con fallback GDI+ de doble buffer), sincronización línea por línea, reproducción de video DirectShow con fail-safe, pantalla de reposo configurable y servidor IPC `ipc.v1`.
- **Estudio** (`FusionStudio.exe`, .NET Framework 4.8): ventana WinForms con tres modos (Inicio, Estudio, Presentación), biblioteca de Cantos y Biblia accesible directamente, lista de programa con sub-líneas, previsualización que usa el mismo modelo de render que la salida, editor de escenarios WPF con lienzo arrastrable estilo PowerPoint, y herencia de estilos de 4 niveles (Tema → Plantilla → Escenario → Elemento).
- **Variante Lite** (`FusionStudio.Lite.exe`, .NET 3.5 SP1): perfil B con motor Live completo, importadores, exportadores y API; editor funcional en WinForms.
- **Perfil C**: sin .NET, el núcleo abre su ventana de control de emergencia nativa y proyecta texto/imágenes/video desde un Escenario empaquetado `.ahp`.
- **Interoperabilidad**: importa Biblias **Zefania XML**, **e-Sword .bib/.bblx 9+** (con descifrado Twofish de columnas), **JSON** y **TSV**; importa cantos del himnario JSON; **PPTX original vía PowerPoint** o convertido a Escenarios vía OpenXML; exporta **PPTX (ISO/IEC-29500)**, **PDF** e **imágenes PNG 1080p**.
- **Automatización**: API HTTP local con token (6 endpoints: state, next/prev, goto, text para OBS, bible, message), control remoto móvil servido por el propio programa, cliente OBS WebSocket 5.x con autenticación y reconexión, y motor de Triggers (evento → condiciones → acciones).
- **Diagnóstico**: Ayuda → Estado del sistema con autotest (render, núcleo, permisos, red local, API, OBS) y log estructurado rotativo.

## Instalación

1. Ejecuta el instalador y sigue el asistente (elige carpeta; sin permisos de administrador si instalas en tu perfil).
2. El instalador instala los binarios correspondientes a tu arquitectura (x86/x64) y detecta .NET automáticamente.
3. (Opcional) Importa una Biblia desde Medios → «Importar Biblia» (Zefania XML recomendado).

## Modo portable

Descomprime el ZIP y ejecuta `FusionHP.exe`; todos los datos quedan en la carpeta `datos` junto al programa.

## Limitaciones conocidas

- Los módulos e-Sword con **cifrado completo de archivo** (comprados/protegidos) no pueden leerse directamente: el programa lo informa y sugiere la edición Zefania equivalente.
- Los **.pptm** se importan sin ejecutar macros; las animaciones y transiciones de PowerPoint no se importan (MVP).
- La exportación a video MP4 y la coautoría en la nube están fuera del alcance del MVP.
- La salida NDI queda para una fase posterior; la integración de transmisión se realiza vía OBS WebSocket.

## Referencia normativa

Documento Técnico v1.1 (2026-09-23): `spec/Aplicacion_Hibrida_TechnicalDoc_v1.1_2026-09-23.md` en el repositorio.
