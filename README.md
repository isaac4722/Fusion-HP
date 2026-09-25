# Fusion-HP

**Aplicación híbrida de presentación litúrgica y multimedia** para **Windows 7 SP1 x86 → Windows 11 x64**. Núcleo nativo C++ (proyección estable, cero parpadeo) + capa C#/.NET Framework (estudio, biblioteca, interoperabilidad, automatización). Sin Java, sin .NET Core, sin escribir en el Registro de Windows.

> Fuente normativa: [`spec/Aplicacion_Hibrida_TechnicalDoc_v1.1_2026-09-23.md`](spec/Aplicacion_Hibrida_TechnicalDoc_v1.1_2026-09-23.md) · Contrato de trabajo: [`AGENT.md`](AGENT.md)

## Características

| Área | Qué hace |
|---|---|
| **Motor (v2.2)** | El **núcleo nativo es el dueño del estado vivo**: carga el programa completo, secuencía líneas/elementos, pantallas y resaltado, y lo **persiste** (`motor/sesion.json`) — cerrar la GUI **no tira la proyección**: el Motor queda autónomo con teclado sobre la salida (Espacio/flechas avanzan · B/C/L pantallas · Esc negro · Alt+F4 apaga) y al reabrir la GUI todo se sincroniza |
| **Modo Presentación** | Arranque directo, salida borderless sin parpadeo (Direct2D/GDI+), sincronización **línea por línea**, pantalla de reposo (negro/logo/tema), atajos Holyrics (flechas · Espacio · Esc/B/L · G búsqueda bíblica · F5) |
| **GUI web modelada (v2.2)** | Chrome propio de la referencia web: botones tipo chip con estados hover/pressed/foco, botones de icono con estado activo, pestañas y buscadores modelados, **62 iconos Tabler** en 4 tintas y logo Lumina en la barra |
| **Modo Creación** | Editor de escenarios WPF con lienzo 16:9 arrastrable, edición directa de texto y **herencia de estilos de 4 niveles** (Tema → Plantilla → Escenario → Elemento) aplicable en caliente |
| **Biblioteca directa** | Cantos y Biblia disponibles sin buscar (paneles fijos); búsqueda instantánea opcional por cita o palabra (≤200 ms) |
| **Biblias** | Importa **Zefania XML**, **e-Sword .bib/.bblx 9+** (descifrado Twofish de columnas), **JSON** y TSV; modelo bíblico unificado con índice en disco |
| **Cantos** | Himnario JSON y respaldo de Holyrics → un elemento por sección (Verso/Coro…); historial de uso |
| **PPTX** | Proyecta el **archivo original tal cual** vía PowerPoint (COM) o lo importa a Escenarios vía OpenXML; exporta **PPTX ISO/IEC-29500**, **PDF** e **imágenes 1080p** |
| **Automatización** | API HTTP local con token (state · next/prev · goto · text para OBS · bible · message), control remoto móvil servido por el propio programa, cliente **OBS WebSocket 5.x**, motor de **Triggers** (etiqueta `lento` → tema `calma` → escena OBS) |
| **Perfiles A/B/C** | Detecta .NET 4.x / 3.5 / ninguno y degrada sin romper: sin .NET, el núcleo abre su **UI de emergencia nativa** y proyecta igualmente |
| **Diagnóstico** | Ayuda → Estado del sistema con autotest (render, núcleo, permisos, red, API, OBS) y log estructurado rotativo |

## Compilación

```bash
# Núcleo nativo (MSVC, x86 y x64 — obligatorio dual)
msbuild src/core/FusionHP.vcxproj /p:Configuration=Release /p:Platform=Win32
msbuild src/core/FusionHP.vcxproj /p:Configuration=Release /p:Platform=x64

# Capa administrada (net48 + net35)
dotnet build src/managed/FusionStudio/FusionStudio.csproj -c Release
dotnet build src/managed/FusionStudio.Lite/FusionStudio.Lite.csproj -c Release

# Tests (arnés propio, sin vstest)
msbuild tests/native/CoreTests.vcxproj /p:Configuration=Release /p:Platform=x64
dist/tests/x64/CoreTests.exe
dotnet build tests/managed/FusionTests.csproj -c Release
tests/managed/bin/Release/FusionTests.exe

# Gate de calidad + instalador + portable
bash scripts/quality_gate.sh
iscc installer/FusionHP.iss          # tras preparar staging/ (ver CI)
bash scripts/package_portable.sh x86
```

## Estructura

```
src/core/            MOTOR C++ (bootstrap · Motor del programa · render D2D/GDI+ · IPC ipc.v1 · DirectShow · UI emergencia)
src/managed/         FusionShared (modelo ahp.v1 + payload del Motor, net35) · FusionStudio (net48) · Lite (net35)
tests/               Arnés nativo y administrado + fixtures (bib e-Sword cifrada real, Zefania, himnario)
installer/           Inno Setup dual x86/x64
scripts/             quality_gate.sh · package_portable.sh
resources/           Fuentes/fondos/iconos Tabler/logo (viajan con el programa) · 4 biblias completas
docs/agent/          Documentación para agentes (arquitectura, compatibilidad, testing…)
spec/                Documento Técnico v1.1 (fuente normativa)
.agents/skills/      Skills del contrato AGENT.md
```

## Formato de proyecto `ahp.v1`

JSON versionado (`format: "ahp.v1"`) con Escenarios → Elementos (texto con marcas de sincronización, versículo, imagen, video, lower third), temas con herencia en cascada y manifiesto `media/`. Campos nuevos siempre ignorables por versiones anteriores.

## Licencia

Ver [LICENSE.md](LICENSE.md).
