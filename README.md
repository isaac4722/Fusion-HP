# LuminaPresentation Suite v5.1.1 «APERTURA»

**Proyección para iglesias con arquitectura híbrida: núcleo C++17 nativo + interfaz C# (.NET mínimo 3.5 — usa 4.8 si está disponible)**
— motor de proyección en tiempo real con la agilidad de desarrollo de .NET,
**sin JVM, sin Electron, sin redistributables y sin instalaciones**.

> ⚠️ Código fuente bajo **Licencia de Solo Lectura (View-Only)** — ver [LICENSE.md](LICENSE.md).
> Los binarios publicados en [Releases](https://github.com/isaac4722/Fusion-HP/releases) son de uso libre.

---

## 🚀 Descargar (sin instalar nada)

1. Ir a **[Releases](https://github.com/isaac4722/Fusion-HP/releases)**.
2. Descargar el paquete para tu arquitectura:
   - `LuminaPresentation-5.1.1-win-x86.zip` — **Windows 7 SP1 … Windows 11 (32 bits)**
   - `LuminaPresentation-5.1.1-win-x64.zip` — **Windows 7 SP1 … Windows 11+ (64 bits)**
3. Descomprimir el ZIP **completo** y ejecutar **`LuminaLauncher.exe`**.
   **El usuario nunca instala nada**:
   - Windows 10 1903+ / Windows 11 → **.NET Framework 4.8** integrado en el SO
     (el launcher usa `net48\LuminaPresentation.exe`).
   - Windows 7 SP1 / 8.x → **.NET Framework 3.5 SP1** integrado de fábrica
     (el launcher usa `net35\LuminaPresentation35.exe`, la línea base mínima).
   - El motor C++ (`LuminaCore.dll`) va enlazado estáticamente (/MT): **cero redistributables**.
   - El launcher **verifica el paquete completo antes de lanzar** (exe + DLL nativa
     + las 3 DLL gestionadas en la carpeta de la variante elegida) y explica con
     claridad qué hacer si falta algo.
   - Ambas variantes comparten la misma carpeta de datos `data\`.

## 🌟 Novedades v5.1.1 «APERTURA»

> **Corrección crítica**: en v5.1.0, el control `Chip` de la cabecera asignaba
> `BackColor = Color.Transparent` **sin activar antes**
> `ControlStyles.SupportsTransparentBackColor` → `ArgumentException: Control
> does not support transparent background colors` en `MainForm.BuildHeader` →
> **la app no abría** (reportado en Win7 SP1 x86, bajo .NET 4.8 y 3.5 por
> igual). v5.1.1 activa el estilo ANTES de asignar el color, pinta el fondo
> con el color sólido real del padre (defensa en profundidad — nunca depende
> de la transparencia simulada) y añade el **gate `--uicheck` en la CI**: ambas
> variantes construyen la ventana principal completa de forma headless ANTES
> de empaquetar. Un paquete cuya ventana no se pueda construir ya no puede
> publicarse.

## 🌟 Novedades v5.1.0 «FUNDAMENTO»

> **Corrección crítica**: el paquete v5.0.0 se publicó sin las DLL gestionadas
> y la app no abría. v5.1.0 endurece el proceso de extremo a extremo: paquete
> completo + gate de humo en CI (`--selfcheck` de ambas variantes ANTES de
> empaquetar) + verificación dura del layout (`verify_portable` exige los 15
> archivos del contrato). Además, **.NET mínimo 3.5 con 4.8 opcional** (dos
> variantes por paquete) y el grueso de huecos del spec cerrados:

- **Teclado en vivo** (requisito §3.1): → / ← / Espacio / AvPág / RePág línea a
  línea, **B** negro, **L** limpiar, **P** pausa de video, **F5** proyector,
  **F6** monitor de escenario, **F7** director, **F8** zócalo.
- **Acordes proyectables**: la línea de cifrado se dibuja en color de acento
  sobre la letra (modo músicos de Holyrics).
- **Búsqueda bíblica por palabra** (FTS5 del núcleo) con resaltado del término
  y doble clic → el versículo se carga al escenario.
- **Biblia de fábrica**: el paquete incluye la **Reina-Valera 1909 completa**;
  al abrir la BD, la app ofrece instalarla (importación por lotes, ~31k versículos).
- **Tercera pantalla — Director** (`DirectorForm`): texto completo del ítem
  actual y próximo, reloj, **cronómetro** y panel de notas internas.
- **Editor de culto completo**: botones para agregar canción actual, pasaje
  bíblico, imagen (JPG/PNG/GIF/BMP/TIF), texto/aviso, blanco e **importación
  de planes JSON** (formato documentado en `docs/api/PlanningCenter.md`).
- **Video con bucle/volumen/pausa** (antes hardcodeado) y **pantallas reales
  del sistema** en el selector (antes 0/1/2 fijo).
- **Respaldo ZIP a carpeta** compatible con Google Drive/OneDrive (manual o
  automático al salir) — motor ZIP propio, sin dependencias.
- **Fixes**: índice de ítem real en los eventos del motor (video/monitor de
  escenario/activadores desincronizados en cultos multi-ítem), fuga GDI del
  zócalo, errores del núcleo al log de sesión, `SetWindowLongPtr` seguro en x64.
- **Sin WindowsBase**: el OPC/PPTX se empaqueta con motor ZIP propio → la
  variante net35 corre idéntica bajo CLR 2.0 y CLR 4.0 (cero GAC-dependencies).

## 🌟 Novedades v5.0.0 «SINERGIA»

> La síntesis de las dos filosofías del spec: el **presentador en vivo** de
> Holyrics + el **creador de contenidos** de PowerPoint, sin las debilidades
> de ninguno (cero Java, cero instalaciones, Win7 x32 → Win11 x64).

- **Exportación PPTX real** (página «Exportar»): PresentationML ISO/IEC-29500
  con unidades EMU exactas (12192000×6858000), tipografía en centipuntos y
  el tema de LuminaPresentation aplicado 1:1 (fondo, fuente, acentos). Se
  abre en PowerPoint 2007+, WPS y LibreOffice — **validado en CI con
  python-pptx** (el gate abre el archivo real y comprueba slides/texto).
- **Exportación PDF 1.4 con escritor propio** (cero dependencias): fuentes
  base-14 Helvetica con WinAnsi (acentos perfectos), métricas AFM para
  centrado real, JPEG por DCTDecode y RGB por FlateDecode, xref verificada
  — **validado en CI con pypdf**.
- **Video en la salida**: ítems de video en el culto que se reproducen a
  pantalla completa sobre el proyector con **Windows Media Player del SO**
  (enlace tardío COM: compilla en cualquier entorno, cero instalaciones;
  en ediciones N se avisa del Media Feature Pack). Auto-avance configurable
  al terminar y evento `video_ended` para activadores.
- **Monitor de escenario** (Stage View): 2ª pantalla para el equipo de
  músicos con letra actual grande, siguiente, rótulo, próximo ítem y reloj.
- **Zócalos Lower Third**: avisos semitransparentes con barra de acento y
  fundidos (botón «Aviso», acción `show_text`, y avisos desde el mando móvil).
- **Importador ZEFania XML** (página Biblia): el estándar de Holyrics/OpenLP
  — Reina Valera 1960 y cualquier módulo. Parser **streaming** (memoria
  constante con 31.084+ versículos), `<BR/>` respetado, filas inválidas
  reportadas sin abortar.
- **Activadores** (página «Activadores»): reglas **evento → condición →
  acción** persistentes (`data\triggers\triggers.json`). Eventos: ítem,
  slide, canción iniciada, fin de video, MIDI (nota/CC/programa) y webhook.
  Condiciones con `contains:`/`gte:`/`lte:`/`*`. Acciones: escena OBS,
  texto a fuente OBS, audio, aviso en pantalla, tema, comando del motor y
  MIDI OUT.
- **OBS Studio vía obs-websocket 5.x**: handshake completo Hello → Identify
  → Identified con **autenticación SHA256** del protocolo, cambio de escena
  y envío de letra/versículos en tiempo real a una fuente de texto.
- **Entrada MIDI** (winmm.dll, presente en Win7→Win11): notas/CC/programa
  alimentan los activadores; **salida MIDI** como acción.
- **Mando remoto móvil por red local**: la app sirve una página táctil en
  español (lista de diapositivas, transporte, negro/limpiar, avisos) en
  `http://TU-IP:8070/remote` con token. **TcpListener propio — sin URLACL
  de administrador** (el firewall solo pregunta una vez «Permitir acceso»).

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
| **Interfaz** | **C# WinForms** en 2 variantes (`net48\LuminaPresentation.exe` optimizada · `net35\LuminaPresentation35.exe` línea base) | Editor de escenarios, bibliotecas (canciones/biblia con búsqueda FTS por palabra), control En Vivo con teclado, vista previa renderizada por el motor, temas, importadores JSON/.BIB/**ZEFania XML**, exportadores **PPTX/PDF**, activadores, ajustes |
| **Exportación** | **C# propio** | **PPTX** (OPC empaquetado con `ZipWriter` propio — sin WindowsBase, idéntico en CLR2/CLR4) y **PDF 1.4** (escritor íntegro: AFM+base-14+zlib) — ambos sin NuGet en los binarios distribuidos |
| **Datos** | **C#** (orquestación) + SQLite nativo | CRUD siempre con **parámetros enlazados**; canciones con FTS5, biblia con índice UNIQUE + dedupe idempotente |
| **API local** | **C# HttpListener** | `/api/state`, `/api/cmd`, `/api/live.txt` y webhook OBS — solo localhost, token opcional |
| **Remoto LAN** | **C# TcpListener** | Página del mando móvil + `/remote`, `/api/catalog`, token en LAN — sin permisos de administrador |
| **Integraciones** | **C#** | obs-websocket 5.x (ClientWebSocket, net48), MIDI IN/OUT (winmm P/Invoke), activadores (reglas evento→acción) |
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
- **`Lumina.Tests`** (C#, net8/net48): MiniJson, builder de escenarios, settings,
  **exportadores PPTX/PDF (estructura ZIP/xref + contenido), Zefania XML,
  activadores y protocolo OBS** → **20/20**.
- **GATE externo `tools/validate_pptx.py`**: python-pptx + pypdf abren los
  archivos exportados por el propio arnés y verifican slides, EMU, páginas y
  acentos — la prueba de que PowerPoint/lectores reales los aceptan.

## 📖 Formatos

- **Canciones JSON**: esquema propio `{title,artist,key,bpm,blocks:[{label,lines}],…}`
  con importación tolerante estilo OpenLP (`authors`, `lyrics` con `[Verso 1]`…).
- **Biblias .BIB**: [docs/bib-format.md](docs/bib-format.md) — directivas `#VERSION/#NAME`,
  filas `libro<sep>cap<sep>vers<sep>texto` (TAB / `|` / `;;`), variante `Libro 1:1 texto`,
  UTF-8/CP1252 automático. Muestra completa incluida: `resources/data/sample/rvr1909.bib`
  (generada con `tools/make_sample_bib.py` desde la RVR1909 de 31.084 versículos).
- **Biblias ZEFania XML** (v5.0.0): `<XMLBIBLE><BIBLEBOOK bnumber…><CHAPTER cnumber…>`
  `<VERSE vnumber…>` — el estándar de Holyrics/OpenLP (RVR1960 y miles de módulos).
  Importación desde la página Biblia (parser streaming).
- **Exportación**: [docs/export.md](docs/export.md) — PPTX (OpenXML/EMU/centipoints)
  y PDF 1.4 (base-14/AFM/WinAnsi/DCT/Flate).

## 🛠 Compilar

```bash
# Núcleo nativo (Linux/Windows: partes portables + tests)
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build --config Release
./build/native/lumina_selftest && ./build/native/lumina_poc_native

# Capa gestionada (net35+net48+net8 en Core; funciona también en Linux)
dotnet build managed/Lumina.sln -c Release
dotnet run --project managed/Tests --framework net8.0   # con libLuminaCore.so junto al exe: tests completos
# Muestras + validación externa de exportadores:
LUMINA_EXPORT_SAMPLES=/tmp/samples LUMINA_SKIP_NATIVE=1 \
  dotnet run --project managed/Tests --framework net8.0
python tools/validate_pptx.py /tmp/samples/muestra.pptx /tmp/samples/muestra.pdf
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
- **v4.2.0 «ACORDES»** — **biblioteca de temas múltiples**
  (guardar/cargar/renombrar/eliminar en `data\themes`) y **transposición de acordes
  en vivo** en el editor con detección de tonalidad (criterio compartido núcleo/UI).
- **v5.0.0 «SINERGIA»** — síntesis Holyrics+PowerPoint del spec: **exportación
  PPTX/PDF**, **video en la salida**, **monitor de escenario**, **lower thirds**,
  **ZEFania XML**, **activadores**, **OBS WebSocket 5**, **MIDI** y **mando
  remoto móvil** — ver [docs/roadmap.md](docs/roadmap.md) para lo diferido.
- **v5.1.0 «FUNDAMENTO»** — **reparación crítica del paquete portable** (DLL
  gestionadas incluidas + gate de humo `--selfcheck` + `verify_portable` duro),
  **.NET mínimo 3.5 con 4.8 opcional** (layout por runtime con `data\` compartida),
  teclado en vivo, acordes proyectables, búsqueda bíblica por palabra, Biblia
  RVR1909 de fábrica, tercera pantalla del director, editor de culto completo,
  video con bucle/volumen/pausa, pantallas reales, respaldo ZIP Drive-compatible
  y fixes de robustez (índice de ítem, fuga GDI, log del núcleo).
