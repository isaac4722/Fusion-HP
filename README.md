# LuminaPresentation Suite

**Proyección multimedia híbrida nativa (C++17 / wxWidgets 3.2 estático)** — la convergencia entre
la agilidad operativa de Holyrics y la potencia de composición de PowerPoint,
**sin JVM, sin JavaFX, sin .NET, sin Electron y sin redistributables**.

> ⚠️ Código fuente bajo **Licencia de Solo Lectura (View-Only)** — ver [LICENSE.md](LICENSE.md).
> Los binarios publicados en [Releases](https://github.com/isaac4722/Fusion-HP/releases) son de uso libre.

---

## 🚀 Descargar (sin instalar nada)

1. Ir a **[Releases](https://github.com/isaac4722/Fusion-HP/releases)**.
2. Descargar el paquete para tu arquitectura:
   - `LuminaPresentationSuite-v2.0.0-win-x86-portable.zip` — **Windows 7 SP1 … Windows 11 (32 bits)**
   - `LuminaPresentationSuite-v2.0.0-win-x64-portable.zip` — **Windows 7 SP1 … Windows 11+ (64 bits)**
3. Descomprimir en cualquier carpeta y ejecutar `LuminaPresentationSuite.exe`.
   **Todo va incluido**: wxWidgets estático (runtime /MT), SQLite+FTS5 y la Biblia RVR1909
   completa. La base de datos se crea sola en `Data\` en el primer arranque.
   **No requiere permisos de administrador, redistributables ni internet.**

## ✨ Características (v2.0.0 «Horizonte» — edición wxWidgets)

| Módulo | Detalle |
|---|---|
| 🎨 **UI/UX nueva** | Biblioteca en pestañas (Canciones / Biblia / Medios / Temas), panel de culto persistente con reordenamiento (Alt+↑/↓), **previsualización dual EN VIVO / SIGUIENTE con render fiel al proyector**, toolbar de control con transposición en vivo, barra de estado informativa y **iconos vectoriales nítidos a cualquier DPI** |
| 🎵 **Canciones** | SQLite + **FTS5** (búsqueda instantánea por título/autor/letra, sin acentos), **etiquetas** (lento, navidad, entrada…), editor con vista previa del reparto en slides, densidad 2–8 líneas/slide, **transposición de acordes EN VIVO** (latinos Do/Re/Mi y anglosajonos, con bajo «Sol/Fa»), **Modo Hinario** (coro intercalado tras cada verso completo, coros repetidos deduplicados) |
| 📖 **Biblia** | **RVR1909 completa incluida** (31.084 versículos, dominio público), referencias tipadas `Jn 3:16`, `salmo 23:1-4`, `1 co 13, 4-7`, búsqueda por palabras (FTS sin acentos), **versículo rápido F9** desde cualquier parte, importación de otras versiones en JSON |
| 🖥 **Salida fullscreen** | Multi-pantalla (selector de proyector o ventana de prueba), fondo sólido/gradiente/imagen (Llenar/Ajustar), texto con **contorno + sombra y ajuste tipográfico automático**, contador de slide opcional, **Lower Third** con barra dorada, video por DirectShow (`wxMediaCtrl`) |
| 🎬 **Medios** | Biblioteca de imágenes/videos con etiquetas, envío a pantalla (fondo en vivo) y al culto, soporte PNG/JPG/GIF/BMP/TIFF/WebP y MP4/WMV/AVI/MOV/MKV |
| 🖌 **Temas** | Plantillas maestras desacopladas (fondo + tipografía título/cuerpo + contorno + sombra + alineación + MAYÚSCULAS), editor con **previsualización en vivo** y **comprobador de contraste WCAG** (ratio AA/AAA), tema predeterminado |
| 🗓 **Cultos** | Playlists ordenadas (canciones, biblia, avisos, imágenes, videos) guardadas en la BD con **persistencia automática**, abrir/guardar `.json`, exportación CSV con escapado correcto, avance automático al terminar un ítem |
| 📱 **Control remoto** | Servidor HTTP embebido: **panel móvil** (tema oscuro, estado en vivo cada 2,5 s), `GET /api/cmd?c=next\|prev\|black\|clear\|logo\|goto\|alert`, `/api/state` (JSON), `/api/live.txt` (texto plano para OBS), **token opcional** |
| 💾 **Datos portables** | Modo portable (carpeta `Data\` junto al exe) con respaldo de un clic (API `sqlite3_backup`), respaldos en `Data\backups\` |
| ⌨ **Atajos** | F5 en vivo · F6 negro · F7 limpiar · F8 logo · **F9 versículo rápido** · F11 modo presentación · Ctrl+F buscar · Ctrl+N nueva canción · Ctrl+S/O guardar/abrir culto. En la pantalla de salida: →/Espacio avanza, ← retrocede, Esc limpia |

## 🏗 Arquitectura

```
src/
├── main.cpp            Aplicación (instancia única, semillas, arranque seguro)
├── core/               Núcleo sin dependencias de UI
│   ├── Types.h         Modelos + JSON (nlohmann, UTF-8 explícito)
│   ├── Database.{h,cpp} SQLite amalgamation + FTS5 (canciones, biblia,
│   │                    temas, medios, cultos, tags, ajustes, respaldo)
│   ├── Chords.h        Transposición de acordes (latinos + anglosajones)
│   ├── Lyrics.h        Parser [Verso]/[Coro] + reparto en slides + hinario
│   ├── BibleRef.h      Tabla canónica de 66 libros + parser de referencias
│   ├── Renderer.{h,cpp} Motor de render (wxGraphicsContext): fondos,
│   │                    contorno/sombra, auto-ajuste, WCAG, cache LRU
│   └── AppPaths.h      Datos portables + wxConfig
├── net/WebServer.{h,cpp}  Servidor HTTP remoto (wxSockets, sin hilos)
└── ui/                 Interfaz wxWidgets
    ├── MainFrame.*     Consola del operador + motor EN VIVO
    ├── OutputFrame.*   Ventana de salida fullscreen (modos + video)
    ├── PreviewPanel.*  Previsualización dual
    ├── SongsPanel · BiblePanel · MediaPanel · ThemesPanel · ServicePanel
    ├── SongEditor · ThemeEditor · SettingsDialog
    ├── Icons.*         Fábrica de iconos vectoriales (sin assets)
    └── AppEvents.*     Eventos panel→frame (desacoplamiento)
third_party/            SQLite amalgamation + nlohmann/json (header-only)
resources/              Biblia RVR1909 (JSON), icono, logo, recursos .rc
tools/                  verify_portable.py (gate PE) + selftest.cpp (63 checks)
```

**Decisiones técnicas clave:**
- **wxWidgets 3.2 estático + runtime /MT** → el exe no depende de ninguna DLL externa:
  verificado por `tools/verify_portable.py` (auditoría de imports PE) en cada build de CI.
- **Compatibilidad Win7 SP1 → Win11+**: wx 3.2 soporta Win7+, `_WIN32_WINNT=0x0601`,
  manifest con `PerMonitorV2` (proyección nítida en escalados 125–150 %).
- **Sin hilos en el servidor remoto**: wxSocket por eventos en el hilo principal —
  los comandos se despachan vía cola de eventos del frame (sin condiciones de carrera,
  cierre seguro con clientes conectados).
- **JSON siempre en UTF-8 explícito** (`ToUtf8`/`FromUtf8`): inmune al locale del sistema.

## 🔨 Compilar desde fuente

```bash
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release -DwxWidgets_ROOT_DIR=<wx-estático>
cmake --build build --config Release
```

En Linux (GTK) basta `sudo apt install libwxgtk3.2-dev libwxgtk-media3.2-dev` y `cmake -S . -B build`.
El CI (`.github/workflows/build.yml`) compila wxWidgets 3.2.8.1 estático con /MT para x86 y x64.

## 🧪 Pruebas

`tools/selftest.cpp` ejecuta **63 checks** del núcleo (acordes y transposición, parser de
letras y Modo Hinario con las correcciones M16, referencias bíblicas, SQLite+FTS5,
tags, importación sin duplicados, cultos, ajustes). El gate `tools/verify_portable.py`
audita el paquete portable antes de publicar.

## 📜 Licencia

Código fuente bajo **Licencia de Solo Lectura (View-Only)** — ver [LICENSE.md](LICENSE.md).
Los binarios publicados en Releases son de **uso libre**.
Biblia RVR1909: dominio público.
