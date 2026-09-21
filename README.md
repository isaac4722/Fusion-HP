# LuminaPresentation Suite

**Proyección multimedia híbrida nativa (C++ / Qt 5.15 LTS)** — la convergencia entre la
agilidad operativa de Holyrics y la potencia de composición vectorial de PowerPoint,
**sin JVM, sin JavaFX, sin .NET y sin Electron**.

> ⚠️ Código fuente bajo **Licencia de Solo Lectura (View-Only)** — ver [LICENSE.md](LICENSE.md).
> Los binarios publicados en [Releases](https://github.com/isaac4722/Fusion-HP/releases) son de uso libre.

---

## 🚀 Descargar (sin instalar nada)

1. Ir a **[Releases](https://github.com/isaac4722/Fusion-HP/releases)**.
2. Descargar el paquete para tu arquitectura:
   - `LuminaPresentationSuite-v1.2.0-win64-portable.zip` (Windows 7 SP1 … Windows 11, 64 bits)
   - `LuminaPresentationSuite-v1.2.0-win32-portable.zip` (Windows 7 SP1 … Windows 10, 32 bits)
3. Descomprimir en cualquier carpeta y ejecutar `LuminaPresentationSuite.exe`.
   **Todo va incluido**: Qt, LibVLC con codecs, Biblia RVR1909 completa y la base de datos se
   crea sola en el primer arranque. No requiere permisos de administrador ni conexión a internet.

## ✨ Características

| Módulo | Detalle |
|---|---|
| 🎵 **Canciones** | SQLite + FTS5: búsqueda instantánea por título/autor/letra, editor con etiquetas `[Verso]/[Coro]`, **transposición de acordes EN VIVO en el Stage View**, **Modo Hinario configurable** (coro intercalado), densidad 2–8 líneas/slide, **🏷 sistema de etiquetas semánticas** (tags) para filtrado por categoría (lento, navidad, entrada, ofrenda…) |
| 📖 **Biblia** | **RVR1909 completa incluida** (dominio público), comandos tipados `Jn 3:16`, hasta **3 versiones en paralelo**, búsqueda por palabras FTS, **✨ resaltado de palabras en dorado** (insensible a acentos/mayúsculas) |
| 📽 **PowerPoint** | Importación **.pptx nativa (OpenXML)** sin Office: textos, formas e imágenes; exportación .pptx básica; **exportación a PDF** del escenario en vivo; items PPTX ejecutables desde la cola del culto |
| 🧾 **Exportación** | Escenario en vivo → **.pptx** (texto real) y **.pdf** (páginas 16:9 rasterizadas) — sin Office |
| 📌 **Lower Third** | Superposición inferior semitransparente con barra dorada (título + texto) — botón directo en la barra de herramientas |
| 🖥 **Salidas** | Audiencia + **Stage View** + overlay web; enrutamiento multipantalla con recuperación ante desconexión |
| 🎚 **Stage View** | Alto contraste, acordes sobre el texto con **transposición en tiempo real**, reloj, cuenta regresiva, siguiente estrofa, alertas |
| 🎬 **Medios** | LibVLC embebido (DXVA2/D3D11VA), video de fondo en bucle (sin reinicio al cambiar de slide), modos Llenar/Ajustar/Centrar (videos verticales) |
| 📱 **Control remoto** | Servidor WebSocket/HTTP embebido: panel móvil (con **navegación de la cola del culto**) y **overlay HTML5 transparente para OBS/vMix** (`http://<pc>:8088/overlay.html`) |
| 🌐 **API HTTP** | `GET /api/cmd?c=next\|prev\|black\|clear\|logo\|goto\|qnext\|qprev\|alert` con **token opcional** (Ajustes) · `/api/live.txt` (texto plano para OBS) · `/api/state` (JSON) |
| 🎨 **Temas** | Plantillas maestras desacopladas (fondo/gradiente/imagen/video, tipografía, contorno, sombra) aplicadas en vivo |
| 🖌 **Lienzo libre** | Editor vectorial (texto, formas, imágenes) estilo PowerPoint con guardado JSON — **proyectable y añadelo a la cola del culto** (v1.2.0) |
| 🗓 **Cultos** | Playlists ordenadas (canciones, biblia, avisos, pptx), exportación CSV, control remoto de la cola |
| 📣 **Comunicación** | Alertas al escenario, temporizador de sermón, bandeja **Telegram** |
| ⚙ **Triggers** | Webhooks HTTP, **OBS WebSocket v5** (cambio de escena automático), **MIDI Out** (winmm) |
| 📊 **Historial** | Estadísticas de uso y reportes CSV/PDF |

## ⌨️ Atajos de teclado

| Tecla | Acción |
|---|---|
| `F5` | Proyectar en vivo |
| `Espacio` / `→` / `PgDown` | Siguiente slide |
| `←` / `PgUp` | Slide anterior |
| `B` | Pantalla negra |
| `C` | Solo fondo (limpiar) |
| `L` | Logo |
| `F9` | Versículo rápido (no interrumpe la canción) |
| `Esc` | Cerrar versículo rápido |

## 🌐 API HTTP embebida

El servidor local (puerto HTTP 8088 por defecto) expone:

| Endpoint | Descripción |
|---|---|
| `GET /api/cmd?c=next` | Comandos: `next`, `prev`, `black`, `clear`, `logo`, `alert` (`&text=…`), `goto` (`&i=N`), `qnext`, `qprev` (cola del culto) |
| `GET /api/live.txt` | Texto plano del slide proyectado — pégalo como **fuente de texto** en OBS Studio |
| `GET /api/state` | Estado completo en JSON (mismo payload del overlay) |
| `GET /overlay.html` | Overlay HTML5 con transparencia (Alpha Key) para OBS/vMix |
| `GET /remote.html` | Panel de control remoto para móvil/tablet |

Si defines un **Token de la API** en Ajustes, los endpoints `/api/cmd` y `/api/live.txt`
exigen `&token=…` (estilo Holyrics). Sin token, quedan abiertos para la red local.

## 🏗 Compilar desde fuente

Requisitos: **CMake ≥ 3.16**, **Qt 5.15 LTS** (MinGW 7.3/8.1 en Windows), C++17.

```bash
cmake -S . -B build -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release
cmake --build build -j
# Despliegue portable en Windows:
windeployqt --release --no-translations build\LuminaPresentationSuite.exe
# Copiar VLC: libvlc.dll, libvlccore.dll y plugins/ -> carpeta vlc/
```

### Empaquetado portable (lo que hace el CI)
```
LuminaPresentationSuite-<arch>-portable/
├── LuminaPresentationSuite.exe   # binario nativo (runtimes estáticos)
├── libgcc_s_*-1.dll · libstdc++-6.dll · libwinpthread-1.dll   # runtime MinGW (Qt lo necesita)
├── Qt5*.dll                      # windeployqt
├── vlc/                          # libvlc.dll + libvlccore.dll + plugins/
├── LICENSE.md · README.md · SHA256SUMS.txt
```

> El CI incluye un **gate de dependencias** (`tools/verify_portable.py`) que audita las
tablas de imports PE de todos los binarios del paquete y falla el build si falta
alguna DLL — garantiza que el portable arranca en un Windows limpio.

## 🤖 CI/CD

`.github/workflows/build.yml` compila **x86 y x64** con Qt 5.15.2 (MinGW 8.1) en Windows,
descarga LibVLC 3.0.21, despliega las DLLs de Qt + el **runtime de MinGW** (verificado por el
gate `tools/verify_portable.py`) y publica automáticamente el release portable
en cada `tag v*`. La versión se define en la variable `APP_VERSION` del workflow.

**Gates de release (v1.2.0)**: además del gate de imports PE, el job de release
(1) purga drafts residuales del tag antes de publicar y (2) **verifica vía API que la
release quede PUBLICADA con sus 3 assets** — si la publicación falla, el run sale rojo
(no más releases "fantasma").

## 🧱 Arquitectura

```
src/
├── main.cpp               # salvaguardas OpenGL/ANGLE + primer arranque
├── core/
│   ├── Database.*         # SQLite3 C-API + FTS5 (amalgamation vendorizada)
│   ├── Models.h           # Song/Slide/Theme/ServiceItem + JSON
│   ├── Lyrics.h           # parser [Verso]/[Coro] + acordes
│   ├── Chords.h           # transposición (C/Do, #, b, m, 7…)
│   ├── BibleRef.h         # 66 libros + parser "Jn 3:16"
│   ├── Renderer.h         # QPainter: fondo + texto con contorno/sombra
│   ├── DisplayEngine.h    # multipantalla + crossfade + VideoHost
│   ├── MediaEngine.h      # LibVLC 3.x por carga dinámica (QLibrary)
│   ├── PptxEngine.h       # OpenXML ZIP+XML con miniz (import/export)
│   ├── Triggers.h         # Webhook + OBS WS v5 + Telegram
│   └── MidiOut.h          # MIDI winmm nativo
├── gui/                   # MainWindow + 10 paneles + StageWindow + VectorCanvas
└── net/WebServer.h        # WebSocket + HTTP (remote/overlay/api)
```

**Presupuesto de rendimiento** (spec): arranque en frío < 1.5 s · cambio de slide < 16 ms ·
RAM en reposo < 120 MB · render raster determinista con fallback de software.

---

© 2026 Isaac — Todos los derechos reservados. Uso de binarios permitido; código solo lectura.
