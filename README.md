# Fusion-HP

**Aplicación híbrida de presentación litúrgica y multimedia** para **Windows 7 SP1 x86 → Windows 11 x64**. Núcleo nativo C++ (proyección estable, cero parpadeo) + capa C#/.NET Framework (estudio, biblioteca, interoperabilidad, automatización). Sin Java, sin .NET Core, sin escribir en el Registro de Windows.

> Fuente normativa: [`spec/Aplicacion_Hibrida_TechnicalDoc_v1.1_2026-09-23.md`](spec/Aplicacion_Hibrida_TechnicalDoc_v1.1_2026-09-23.md) · Contrato de trabajo: [`AGENT.md`](AGENT.md)

## v3.0.0 — Reestructuración: los 7 bugs del prototipo, resueltos

| # | Defecto reportado | Resolución |
|---|---|---|
| 1 | El cargador de Escenarios no muestra nombres | Cargador dedicado: recientes con **nombre**, N escenarios y **títulos visibles** antes de abrir (`ProjectOpenForm` + `AhpProjectInfo`) |
| 2 | La ventana de proyección no respetaba la pantalla | El monitor elegido **viaja al núcleo** (IPC `monitor`) y la salida borderless se posiciona donde manda el operador |
| 3 | Cerrar con la X duplicaba la ventana | `WM_CLOSE`/`WM_DESTROY` correctos + sesión del Motor persistida en toda salida; sin procesos zombi ni duplicación |
| 4 | El modo API no funciona | La API se aplica **al arrancar**, interruptor rápido en consola, 6 endpoints con token y **OBS WebSocket 5.x restaurado** (`obs.scene` en Triggers) |
| 5 | La importación PPTX extrae en vez de cargar el original | **Proyector PPTX directo**: lee el ORIGINAL tal cual y lo entrega al Motor, sin convertir ni guardar — con o sin PowerPoint |
| 6 | Bibliotecas (Cantos y Biblia) requieren búsqueda | **Lista directa completa** de cantos (sin tope 200) y árbol bíblico de 66 libros, siempre visibles |
| 7 | El flujo genera PPTX para cada ocasión | Flujo Holyrics: cantos desde la BD **directo al Motor**; prueba automatizada garantiza **0 archivos** generados al proyectar |

Detalle técnico completo: [`docs/agent/REESTRUCTURACION_v3.md`](docs/agent/REESTRUCTURACION_v3.md).

## Características

| Área | Qué hace |
|---|---|
| **Motor (v2.2)** | El **núcleo nativo es el dueño del estado vivo**: carga el programa completo, secuencía líneas/elementos, pantallas y resaltado, y lo **persiste** (`motor/sesion.json`) — cerrar la GUI **no tira la proyección**: el Motor queda autónomo con teclado sobre la salida (Espacio/flechas avanzan · B/C/L pantallas · Esc negro · Alt+F4 apaga) y al reabrir la GUI todo se sincroniza |
| **Modo Presentación** | Arranque directo, salida borderless sin parpadeo (Direct2D/GDI+), sincronización **línea por línea**, pantalla de reposo (negro/logo/tema), atajos Holyrics (flechas · Espacio · Esc/B/L · G búsqueda bíblica · F5) |
| **GUI nativa modelada (v2.2)** | Controles **100 % nativos C#/GDI+** que replican el diseño de la referencia web (sin navegador ni motor web): botones tipo chip con estados hover/pressed/foco, botones de icono con estado activo, pestañas y buscadores modelados, **62 iconos Tabler** en 4 tintas y logo Lumina en la barra |
| **Estudio nativo C++ (v2.3)** | `FusionHP.exe --gui=native` (o perfil C sin .NET) abre el **estudio 100 % C++** con la GUI web replicada: pantalla de Inicio con tarjetas y recientes, editor **PowerStudio** (barra de título con acceso rápido, cinta de 8 pestañas con Backstage de Archivo, miniaturas por secciones, lienzo con edición directa + notas, panel Formato/Biblioteca con Cantos/Biblia/Medios/Temas, barra de estado con vistas Normal/Clasificador), consola **Presentar** (programa con sub-líneas, Biblia rápida G, transporte con avance conmutable, EN VIVO + reloj) y **Mando** (réplica del control remoto móvil) — botones modelados, iconos Tabler y atajos web completos (F5 · Ctrl+M/S/Z/Y/C/V/D · Supr · B/C/L/G · ? · Esc) |
| **Modo Creación** | Editor de escenarios WPF con lienzo 16:9 arrastrable, edición directa de texto y **herencia de estilos de 4 niveles** (Tema → Plantilla → Escenario → Elemento) aplicable en caliente |
| **Biblioteca directa** | Cantos y Biblia disponibles sin buscar (paneles fijos); búsqueda instantánea opcional por cita o palabra (≤200 ms) |
| **Biblias** | Importa **Zefania XML**, **e-Sword .bib/.bblx 9+** (descifrado Twofish de columnas), **JSON** y TSV; modelo bíblico unificado con índice en disco |
| **Cantos** | Himnario JSON y respaldo de Holyrics → un elemento por sección (Verso/Coro…); historial de uso |
| **PPTX** | Proyecta el **archivo original tal cual** vía PowerPoint (COM) o lo importa a Escenarios vía OpenXML; exporta **PPTX ISO/IEC-29500**, **PDF** e **imágenes 1080p** |
| **Automatización** | **API HTTP de control remoto** con token (state · next/prev · goto · text · bible · message), control remoto móvil servido por el propio programa (`/remote`), motor de **Triggers** (etiqueta `lento` → tema `calma`). *(v2.3: el cliente OBS WebSocket fue **eliminado por decisión del usuario** — queda solo la API de control remoto)* |
| **Escenario de músicos (v2.3)** | **Stage View beta-1**: monitor de retorno de alto contraste con reloj, **tono/BPM del canto**, alertas doradas, **temporizador regresivo** y vista previa del siguiente elemento — controlado desde la consola (C# o estudio C++) con los comandos `stage.*` |
| **Historial (v2.3)** | Función beta-1: `historial.jsonl` compartido — **más usadas**, uso reciente y **exportación CSV** (C# y C++ leen y escriben el mismo archivo) |
| **Perfiles A/B/C** | Detecta .NET 4.x / 3.5 / ninguno y degrada sin romper: sin .NET, el núcleo abre su **estudio nativo completo** (v2.3) y proyecta igualmente |
| **Diagnóstico** | Ayuda → Estado del sistema con autotest (render, núcleo, permisos, red, API) y log estructurado rotativo |

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

## Arquitectura: dos versiones, 100 % nativas

El programa se compone de **dos ejecutables nativos** que colaboran por IPC (`ipc.v1`, named pipe JSON) — **sin navegador, sin Chromium/CEF, sin WebView, sin Java, sin .NET Core**:

| Ejecutable | Lenguaje | Rol |
|---|---|---|
| `FusionHP.exe` | **C++ puro** (Win32, `/MT`) | **MOTOR**: dueño del estado vivo, proyección Direct2D/GDI+ sin parpadeo, video DirectShow, persistencia, autonomía sin GUI, y desde v2.3 **estudio nativo completo** (GUI web replicada en Win32/GDI+: `--gui=native` o perfil C) |
| `FusionStudio.exe` / `FusionStudio.Lite.exe` | **C# puro** (WinForms/WPF, .NET FW 3.5–4.8) | **GUI**: estudio, biblioteca, editor, automatización — todos los controles están modelados a mano en GDI+ replicando el diseño de la referencia web (`H-P-Web-Version-Ref`) |

La **referencia web se usa únicamente como diseño a replicar** — su apariencia (chips, iconos, temas, tipografías) fue recreada con controles nativos dibujados a mano; nada del código o runtime web forma parte del programa.

## Licencia

Ver [LICENSE.md](LICENSE.md).
