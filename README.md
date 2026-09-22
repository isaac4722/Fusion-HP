# LuminaPresentation Suite v4.2.0 «ACORDES»

**Proyección para iglesias con arquitectura híbrida: núcleo C++17 nativo + interfaz C# (.NET Framework 4.8/3.5)**
— motor de proyección en tiempo real con la agilidad de desarrollo de .NET,
**sin JVM, sin Electron, sin redistributables y sin instalaciones**.

> ⚠️ Código fuente bajo **Licencia de Solo Lectura (View-Only)** — ver [LICENSE.md](LICENSE.md).
> Los binarios publicados en [Releases](https://github.com/isaac4722/Fusion-HP/releases) son de uso libre.

---

## 🚀 Descargar (sin instalar nada)

1. Ir a **[Releases](https://github.com/isaac4722/Fusion-HP/releases)**.
2. Descargar el paquete para tu arquitectura:
   - `LuminaPresentation-4.2.0-win-x86.zip` — **Windows 7 SP1 … Windows 11 (32 bits)**
   - `LuminaPresentation-4.2.0-win-x64.zip` — **Windows 7 SP1 … Windows 11+ (64 bits)**
3. Descomprimir y ejecutar **`LuminaLauncher.exe`**: detecta el runtime .NET disponible
   y lanza la interfaz correcta. **El usuario nunca instala nada**:
   - Windows 10 1903+ / Windows 11 → **.NET Framework 4.8** integrado en el SO.
   - Windows 7 SP1 / 8.x → **.NET Framework 3.5 SP1** integrado (variante de línea base).
   - El motor C++ (`LuminaCore.dll`) va enlazado estáticamente (/MT): **cero redistributables**.

## 🌟 Novedades v4.2.0 «ACORDES»

- **Biblioteca de temas múltiples** (nuevo): guarda, carga (doble clic), renombra y
  elimina temas en `data\themes\*.json` — ya no hay un solo tema activo, se pueden
  preparar paletas por canción o por servicio. El tema en uso queda marcado («En uso»)
  y se protege el tema «Predeterminado» de eliminación accidental.
- **Transposición de acordes en vivo** (nuevo): en el editor de canciones se detecta
  la **tonalidad** del cifrado en tiempo real («Tono: Sol») con el MISMO criterio del
  núcleo (ChordUtil — sin falsos positivos en español: «dos», «mis», «fue» no son
  acordes). El botón **«Transponer ahora»** reescribe las líneas de cifrado del editor
  vía `lumina_chords_transpose` (notación latina Do-Re-Mi), conservando la alineación
  de la letra columna por columna; la letra pasa intacta.
- **Núcleo**: `lumina_chords_transpose` con puerta de acordes endurecida (sufijos
  sus/add/dim/aug, alteraciones, slash-chords «Fa#m7/C#») y pruebas del arnés
  nativo + PoC + Tests gestionados ampliadas.

## 🌟 Novedades v4.1.0 «LUMINA»

- **Editor de Temas visual** (nuevo): colores (fondo/texto/acento con selector nativo),
  tipografía (fuente, tamaño, negrita, MAYÚSCULAS, interlineado), efectos (contorno por
  silueta y sombra con alpha) e imagen de fondo opcional (contener/cubrir) — con **vista
  previa en vivo** en GDI+ y botón «Aplicar al escenario» que reconstruye el escenario
  activo con el nuevo tema (proyección + vista previa del núcleo se refrescan solas).
  El tema se persiste en `data/settings.json` (themeJson) y se recarga al arrancar.
- **Nuevo nombre**: el producto es **LuminaPresentation Suite** (antes Fusion-HP).
  Binarios renombrados: `LuminaLauncher.exe`, `LuminaPresentation.exe` (interfaz) y
  `LuminaCore.dll` (núcleo nativo).
- **Interfaz rediseñada por completo**: tema oscuro plano profesional, barra lateral
  de navegación con iconos vectoriales, cabecera con chips de estado (Núcleo/BD/API),
  tarjetas, listas owner-drawn y botones planos con hover — diseñada en mockup y
  transcrita 1:1 a WinForms (funciona igual en Windows 7 que en Windows 11).
- **«La app siempre abre» (blindaje anti-crash)**:
  - Manejadores globales de excepciones (hilo de UI + hilos de fondo + `Main`)
    con diálogo claro en español — **adiós al críptico «dejó de funcionar»**.
  - **Modo limitado**: si `LuminaCore.dll` no carga, la ventana abre igual, el chip
    Núcleo se pinta en rojo y cada acción avisa qué falta (nunca un crash).
  - **Pre-chequeo del launcher**: si ejecutas desde DENTRO del ZIP (causa típica del
    crash), se explica cómo extraer el paquete ANTES de que falle algo.
  - **Log de diagnóstico** automático en `data/logs/lumina-*.log` (SO, bits, .NET,
    presencia de la DLL, permisos) para soporte remoto.
- Icono propio de la aplicación y metadatos de versión.

## 🧩 Arquitectura híbrida

| Capa | Tecnología | Contenido |
|---|---|---|
| **Motor / Núcleo** | **C++17** (`LuminaCore.dll`, /MT) | Proyección nativa Win32+GDI (doble búfer, contorno+sombra, ajuste tipográfico), modelo de escenario, acordes (latina/anglosajona + transposición), letras con Modo Hinario, referencias bíblicas (66 libros), **parser JSON de canciones** (esquema propio + subconjunto OpenLP) y **parser .BIB de biblias** (detección automática), **SQLite+FTS5** embebido, eventos por callbacks, API C plana estable (`lumina.h`) |
| **Interfaz** | **C# WinForms** (`LuminaPresentation.exe`, net48; línea base net35) | Editor de escenarios, bibliotecas (canciones/biblia), control En Vivo, vista previa renderizada por el motor, temas, importadores JSON/.BIB, ajustes |
| **Datos** | **C#** (orquestación) + SQLite nativo | CRUD siempre con **parámetros enlazados**; canciones con FTS5, biblia con índice UNIQUE + dedupe idempotente |
| **API local** | **C# HttpListener** | `/api/state`, `/api/cmd`, `/api/live.txt` y webhook OBS — solo localhost, token opcional |
| **Puente** | **P/Invoke** (vía A elegida) | ABI C plana: UTF-8, sin excepciones cruzando la frontera, búfer uniforme `out/cap/needed`, eventos en hilo dedicado |

**Decisiones documentadas** (matriz completa en [docs/architecture-hybrid.md](docs/architecture-hybrid.md)):
las vías **C++/CLI**, **COM Interop con registro** y **CLR Hosting** fueron evaluadas;
la vía A (C# dueño del proceso + DLL nativa) es la elegida por robustez y ABI estable.
El **CLR Hosting queda implementado y validado como PoC** (`native/poc-clrhost`) —
fallback arquitectónico probado en CI («CLRHOST PASS»).

## ✅ PoC de comunicación C++ ↔ C# (validado en tests)

El encargo pedía validar la viabilidad **antes** de construir la interfaz. El PoC es
ejecutable y forma parte del CI:

- **`PoC.Managed`** (C#, net35+net48+net8.0): 12 verificaciones — carga de la DLL,
  UTF-8 ida/vuelta, callbacks desde el hilo del motor, canción JSON, .BIB, referencias,
  acordes, BD+FTS5, escenario/en vivo y estrés (1000 pings + 200 next/prev).
  Gate del CI: **«POC PASS 12/12»** en x86 y x64 contra las DLL reales.
- **`lumina_poc_native`** (C++): 37 verificaciones del API C en modo headless.
- **`lumina_selftest`** (C++): 138 checks de lógica (acordes, hinario, .BIB, SQLite…).
- **`lumina_poc_clrhost`** (C++): hospedaje del CLR desde C++ puro + facade COM-visible.
- **`Lumina.Tests`** (C#, net8/net48): MiniJson, builder de escenarios, settings.

## 📖 Formatos

- **Canciones JSON**: esquema propio `{title,artist,key,bpm,blocks:[{label,lines}],…}`
  con importación tolerante estilo OpenLP (`authors`, `lyrics` con `[Verso 1]`…).
- **Biblias .BIB**: [docs/bib-format.md](docs/bib-format.md) — directivas `#VERSION/#NAME`,
  filas `libro<sep>cap<sep>vers<sep>texto` (TAB / `|` / `;;`), variante `Libro 1:1 texto`,
  UTF-8/CP1252 automático. Muestra completa incluida: `resources/data/sample/rvr1909.bib`
  (generada con `tools/make_sample_bib.py` desde la RVR1909 de 31.084 versículos).

## 🛠 Compilar

```bash
# Núcleo nativo (Linux/Windows: partes portables + tests)
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build --config Release
./build/native/lumina_selftest && ./build/native/lumina_poc_native

# Capa gestionada (net35+net48 con ref assemblies; funciona también en Linux)
dotnet build managed/Lumina.sln -c Release
dotnet run --project managed/Tests --framework net8.0   # con libLuminaCore.so junto al exe: tests completos
```

En Windows, `native/` compila con MSVC (`-A Win32` o `-A x64`) y produce `LuminaCore.dll` /MT.

## 📜 Historial

- **v1.x (Qt 5.15)** — serie «NEXO/ESTABILIDAD»: auditoría profunda (37 defectos corregidos),
  API HTTP+WS, PPTX, MIDI, OBS, Planning Center, Drive.
- **v2.0.0 «HORIZONTE» (wxWidgets)** — reescritura nativa completa (código archivado en
  `apps/native-wx-src` con su historial).
- **v3.0.0 «HÍBRIDA»** — núcleo C++ puro + capa .NET, PoC de interop validado (12/12),
  CI de 7 jobs con gates, empaquetado portable x86/x64 y releases automáticos.
- **v4.0.0 «LUMINA»** — producto renombrado a LuminaPresentation Suite,
  interfaz rediseñada (tema oscuro, navegación lateral, chips de estado) y blindaje
  completo anti-crash (modos degradados, pre-chequeos del launcher y logs de diagnóstico).
- **v4.1.0 «LUMINA»** — editor de Temas visual con vista previa en vivo
  (colores, tipografía, contorno/sombra, imagen de fondo) y persistencia del tema en ajustes.
- **v4.2.0 «ACORDES»** — la presente: **biblioteca de temas múltiples**
  (guardar/cargar/renombrar/eliminar en `data\themes`) y **transposición de acordes
  en vivo** en el editor con detección de tonalidad (criterio compartido núcleo/UI).
