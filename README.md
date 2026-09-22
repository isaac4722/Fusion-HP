# LuminaPresentation Suite — Fusion-HP v3.0.0 «HÍBRIDA»

**Proyección para iglesias con arquitectura híbrida: núcleo C++17 nativo + interfaz C# (.NET Framework 4.8/3.5)**
— motor de proyección en tiempo real con la agilidad de desarrollo de .NET,
**sin JVM, sin Electron, sin redistributables y sin instalaciones**.

> ⚠️ Código fuente bajo **Licencia de Solo Lectura (View-Only)** — ver [LICENSE.md](LICENSE.md).
> Los binarios publicados en [Releases](https://github.com/isaac4722/Fusion-HP/releases) son de uso libre.

---

## 🚀 Descargar (sin instalar nada)

1. Ir a **[Releases](https://github.com/isaac4722/Fusion-HP/releases)**.
2. Descargar el paquete para tu arquitectura:
   - `Fusion-HP-3.0.0-win-x86.zip` — **Windows 7 SP1 … Windows 11 (32 bits)**
   - `Fusion-HP-3.0.0-win-x64.zip` — **Windows 7 SP1 … Windows 11+ (64 bits)**
3. Descomprimir y ejecutar **`FusionLauncher.exe`**: detecta el runtime .NET disponible
   y lanza la interfaz correcta. **El usuario nunca instala nada**:
   - Windows 10 1903+ / Windows 11 → **.NET Framework 4.8** integrado en el SO.
   - Windows 7 SP1 / 8.x → **.NET Framework 3.5 SP1** integrado (variante de línea base).
   - El motor C++ (`FusionCore.dll`) va enlazado estáticamente (/MT): **cero redistributables**.

## 🧩 Arquitectura híbrida (v3.0.0)

| Capa | Tecnología | Contenido |
|---|---|---|
| **Motor / Núcleo** | **C++17** (`FusionCore.dll`, /MT) | Proyección nativa Win32+GDI (doble búfer, contorno+sombra, ajuste tipográfico), modelo de escenario, acordes (latina/anglosajona + transposición), letras con Modo Hinario, referencias bíblicas (66 libros), **parser JSON de canciones** (esquema propio + subconjunto OpenLP) y **parser .BIB de biblias** (detección automática), **SQLite+FTS5** embebido, eventos por callbacks, API C plana estable (`fusion.h`) |
| **Interfaz** | **C# WinForms** (`FusionHP.exe`, net48; línea base net35) | Editor de escenarios, bibliotecas (canciones/biblia), control En Vivo, vista previa renderizada por el motor, temas, importadores JSON/.BIB, ajustes |
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
- **`fusion_poc_native`** (C++): 37 verificaciones del API C en modo headless.
- **`fusion_selftest`** (C++): 138 checks de lógica (acordes, hinario, .BIB, SQLite…).
- **`fusion_poc_clrhost`** (C++): hospedaje del CLR desde C++ puro + facade COM-visible.
- **`FusionHP.Tests`** (C#, net8/net48): MiniJson, builder de escenarios, settings.

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
./build/native/fusion_selftest && ./build/native/fusion_poc_native

# Capa gestionada (net35+net48 con ref assemblies; funciona también en Linux)
dotnet build managed/FusionHP.sln -c Release
dotnet run --project managed/Tests --framework net8.0   # con libFusionCore.so junto al exe: tests completos
```

En Windows, `native/` compila con MSVC (`-A Win32` o `-A x64`) y produce `FusionCore.dll` /MT.

## 📜 Historial

- **v1.x (Qt 5.15)** — serie «NEXO/ESTABILIDAD»: auditoría profunda (37 defectos corregidos),
  API HTTP+WS, PPTX, MIDI, OBS, Planning Center, Drive.
- **v2.0.0 «HORIZONTE» (wxWidgets)** — reescritura nativa completa (código archivado en
  `apps/native-wx-src` con su historial).
- **v3.0.0 «HÍBRIDA»** — la presente: núcleo C++ puro + capa .NET, CI de 7 jobs con gates
  de interop, empaquetado portable x86/x64 y releases automáticos.
