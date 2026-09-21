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
   - `LuminaPresentationSuite-v1.6.0-win64-portable.zip` (Windows 7 SP1 … Windows 11, 64 bits)
   - `LuminaPresentationSuite-v1.6.0-win32-portable.zip` (Windows 7 SP1 … Windows 10, 32 bits)
3. Descomprimir en cualquier carpeta y ejecutar `LuminaPresentationSuite.exe`.
   **Todo va incluido**: Qt, LibVLC con codecs, Biblia RVR1909 completa y la base de datos se
   crea sola en el primer arranque. No requiere permisos de administrador ni conexión a internet.

> 🛡 **v1.6.0 «ESTABILIDAD»**: auditoría profunda del código (3 defectos críticos + 19 medios
> + 15 bajos corregidos — crash del servidor remoto con clientes conectados, crash al terminar
> videos, PPTX en blanco en la cola, seguridad del API/WS con token, Google Drive con vaults
> grandes, Modo Hinario, importador de Biblias, proyección nítida en escalados 125–150 %…).
> Suite de regresión automatizada de 45 checks en verde.

## ✨ Características

| Módulo | Detalle |
|---|---|
| 🎨 **GUI «Aurora»** | **Tema visual global profesional v1.3.0**: sidebar con secciones (BIBLIOTECA/DISEÑO/SERVICIO/SISTEMA), botones primarios/dorado, tablas con zebra, scrollbars finas, diálogos y tooltips rediseñados |
| 🖥 **Modo Presentación (F11)** | Consola mínima del operador (spec maestro): el dock de proyección ocupa toda la ventana con transporte grande — F11 restaura la consola completa |
| 🎵 **Canciones** | SQLite + FTS5: búsqueda instantánea por título/autor/letra, editor con etiquetas `[Verso]/[Coro]`, **transposición de acordes EN VIVO en el Stage View**, **Modo Hinario configurable** (coro intercalado), densidad 2–8 líneas/slide, **🏷 sistema de etiquetas semánticas** (tags) para filtrado por categoría (lento, navidad, entrada, ofrenda…) y **🧠 automatización semántica v1.4.0** (reglas etiqueta → tema + fondo) |
| 📖 **Biblia** | **RVR1909 completa incluida** (dominio público), comandos tipados `Jn 3:16`, hasta **3 versiones en paralelo**, búsqueda por palabras FTS, **✨ resaltado de palabras en dorado**, **importador ZEFania XML v1.3.0** (RVR1960, NVI, KJV… miles de versiones libres) |
| 📽 **PowerPoint** | Importación **.pptx nativa (OpenXML)** sin Office: textos, formas e imágenes; exportación .pptx básica; **exportación a PDF** y **PNG (1920×1080) v1.3.0** del escenario en vivo; items PPTX ejecutables desde la cola del culto |
| 🧾 **Exportación** | Escenario en vivo → **.pptx** (texto real), **.pdf** (páginas 16:9) e **imágenes .png** — sin Office |
| 📌 **Lower Third** | Superposición inferior semitransparente con barra dorada (título + texto) — botón directo en la barra de herramientas |
| 🖥 **Salidas** | Audiencia + **Stage View** + overlay web + **🧭 Pantalla Director v1.5.0** (3.ª salida independiente para el director: ítem actual y siguiente con preview, notas, reloj, temporizador y registro persistente de mensajes; también vía navegador `http://<pc>:8088/director.html`); enrutamiento multipantalla con recuperación ante desconexión |
| 🎚 **Stage View** | Alto contraste, acordes sobre el texto con **transposición en tiempo real**, reloj, cuenta regresiva, siguiente estrofa, alertas |
| 🎬 **Medios** | LibVLC embebido (DXVA2/D3D11VA), video de fondo en bucle (sin reinicio al cambiar de slide), modos Llenar/Ajustar/Centrar (videos verticales), **posición inicial de reproducción v1.3.0** |
| 📱 **Control remoto** | Servidor WebSocket/HTTP embebido: panel móvil (con navegación de la cola del culto) y **overlay HTML5 transparente para OBS/vMix** (`http://<pc>:8088/overlay.html`); **mensajes del operador a los remotos (toast) v1.3.0** |
| 🌐 **API HTTP** | `GET /api/cmd?c=next\|prev\|black\|clear\|logo\|goto\|qnext\|qprev\|alert` con **token opcional** (Ajustes) · `/api/live.txt` (texto plano para OBS) · `/api/state` (JSON) |
| 🎨 **Temas** | Plantillas maestras desacopladas (fondo/gradiente/imagen/video, tipografía, contorno, sombra) aplicadas en vivo · **🏷 etiquetas v1.4.0** · **🖼 biblioteca de fondos filtrable por etiqueta v1.4.0** · **♿ comprobador de contraste WCAG v1.4.0** |
| 🖌 **Lienzo libre** | Editor vectorial (texto, formas, imágenes) estilo PowerPoint con guardado JSON — **proyectable y añadible a la cola del culto** |
| 🗓 **Cultos** | Playlists ordenadas (canciones, biblia, avisos, pptx), exportación CSV, control remoto de la cola · **📅 importación de planes de Planning Center Online v1.5.0** (empareja canciones con tu biblioteca, insensible a mayúsculas/acentos) |
| 📣 **Comunicación** | Alertas al escenario, temporizador de sermón, bandeja **Telegram**, **mensajes a remotos conectados** |
| ⌨ **Atajos** | **9 atajos personalizables v1.3.0** (Ajustes › Atajos, con restauración de fábrica) |
| 🧠 **Automatización semántica** | **Reglas v1.4.0**: «canción con etiqueta X → tema Y + fondo con etiqueta Z», aplicadas EN VIVO al proyectar (matching insensible a mayúsculas/acentos, interruptor maestro) |
| 💾 **Copia de seguridad** | **Backup/restauración del vault v1.3.0** + auto-backup semanal con rotación · **☁ Google Drive v1.5.0**: OAuth por navegador, subida manual y automática, descarga de la última copia, rotación de 4 en la carpeta oculta de la app en Drive |
| ⚙ **Módulos JS (JSLib) v1.5.0** | Archivos `.js` en `<datos>/modules/`: sockets **TCP** y **WebSocket persistentes**, `httpGet` con callback, temporizadores y suscripción a eventos (`jslib.onEvent('slide_next', …)`) — automatización e integraciones sin recompilar |
| 🖱 **Drag & Drop** | Arrastra imagen/video → fondo en vivo; arrastra .txt → importa la canción — **formatos de imagen: JPG, PNG, GIF, BMP y TIF v1.5.0** |
| ⚙ **Triggers** | Webhooks HTTP, **OBS WebSocket v5** (cambio de escena automático), **MIDI Out** (winmm), **🎹 MIDI In v1.4.0** (pedal/pad → comandos del presentador, mapa nota→comando editable) |
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
| `F10` | Lower Third (superposición de títulos) |
| `F11` | **Modo Presentación** (consola mínima) |
| `Esc` | Cerrar versículo rápido |

> **v1.3.0**: los 9 atajos principales son **personalizables** desde Ajustes › Atajos de teclado.

## 🌐 API HTTP embebida

El servidor local (puerto HTTP 8088 por defecto) expone:

| Endpoint | Descripción |
|---|---|
| `GET /api/cmd?c=next` | Comandos: `next`, `prev`, `black`, `clear`, `logo`, `alert` (`&text=…`), `goto` (`&i=N`), `qnext`, `qprev` (cola del culto) |
| `GET /api/live.txt` | Texto plano del slide proyectado — pégalo como **fuente de texto** en OBS Studio |
| `GET /api/state` | Estado completo en JSON (mismo payload del overlay) |
| `GET /overlay.html` | Overlay HTML5 con transparencia (Alpha Key) para OBS/vMix |
| `GET /remote.html` | Panel de control remoto para móvil/tablet |
| `GET /director.html` | **Pantalla Director v1.5.0** (mensajes/notas para el director, en cualquier navegador de la LAN) |
| `GET /api/director.json` | Estado del director: ítem/texto/siguiente/notas/mensajes/cuenta regresiva |

Si defines un **Token de la API** en Ajustes, los endpoints `/api/cmd` y `/api/live.txt`
exigen `&token=…` (estilo Holyrics). Sin token, quedan abiertos para la red local.

## ☁ Respaldo en Google Drive (v1.5.0)

1. En [Google Cloud Console](https://console.cloud.google.com/) crea un proyecto y, en *APIs y servicios → Credenciales*, un **ID de cliente OAuth de tipo «Aplicación de escritorio»**. Habilita la **Google Drive API**.
2. Copia su **Client ID** y **Client Secret** en *Ajustes → Respaldo en Google Drive* y pulsa **Conectar cuenta de Google**: se abre el navegador, autorizas y vuelves — no hay códigos que copiar a mano (OAuth *loopback*).
3. Desde ese momento: **Respaldar ahora** sube tu vault a la carpeta oculta de la aplicación en Drive (`appDataFolder`, invisible para el resto de tu Drive), con **rotación de 4 copias**; **Descargar última copia** la trae de vuelta para restaurarla con el botón local.
4. Activa *«Subir también la copia automática semanal»* y cada auto-backup local se sube solo a la nube.

Alcance mínimo: `drive.file` — la aplicación solo ve los archivos que ella misma crea, nunca el resto de tu Drive.

## ⚙ Módulos JavaScript — JSLib (v1.5.0)

Coloca archivos `.js` en la carpeta `modules/` dentro del directorio de datos
(Ajustes → *Abrir carpeta de datos*; botón «📂 Abrir carpeta de módulos» en
Comunicación). Se cargan al arrancar y puedes recargarlos sin reiniciar.

```js
// <datos>/modules/mi-automatizacion.js
jslib.log('módulo listo');

// Reacciona a los eventos del presentador (slide_next, slide_prev,
// media_play, media_stop, black, clear, logo, golive, alert, slide…)
jslib.onEvent('slide_next', function (data) {
    jslib.log('cambio de slide: ' + data.item);
});

// Socket TCP persistente (p. ej. enviar el texto a un mezclador)
jslib.tcpConnect('mixer', '192.168.1.50', 5000);
jslib.onTcpMessage('mixer', function (texto) { jslib.log('mixer: ' + texto); });
jslib.tcpSend('mixer', 'SLIDE: ' + (data && data.item || ''));

// WebSocket persistente + HTTP con callback
jslib.wsConnect('obs', 'ws://127.0.0.1:4455');
jslib.httpGet('https://api.example.com/estado', function (status, body) {
    jslib.log('HTTP ' + status);
});

// Temporizadores
jslib.setTimeout(15000, function () { jslib.notify('Aviso', '15 minutos'); });
```

API completa: `log`, `notify`, `httpGet(url, cb)`, `tcpConnect/tcpSend/tcpClose/onTcpMessage`,
`wsConnect/wsSend/wsClose/onWsMessage`, `onEvent(evento, cb)`, `setTimeout/clearTimeout`.
El registro en vivo (logs y errores de sintaxis con archivo y línea) está en
**Comunicación → Módulos JavaScript**.

## 📅 Planning Center Online (v1.5.0)

1. En [planningcenteronline.com](https://planningcenteronline.com/) → *Perfil → Personal Access Tokens*, genera un token y copia su **Application ID** y **Application Secret**.
2. Pégalo en **Comunicación → Planning Center Online** y pulsa **Conectar y cargar planes**: aparecen tus ministerios y los planes futuros.
3. **Importar plan a la cola**: los ítems de canción se emparejan con las canciones de tu biblioteca (comparación insensible a mayúsculas/acentos) y el resto entra como ítems de texto con sus notas — listo para proyectar.

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
