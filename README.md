# LuminaPresentation Suite v7.1.0 «OPERADOR» — BETA

> **v7.1.0 «OPERADOR»**: correcciones y ajustes de enfoque del prototipo
> probado en campo — **enfoque Holyrics/PowerPoint**: **editor de
> diapositivas de lienzo libre** (arrastrar/redimensionar/editar in situ,
> WYSIWYG con la proyección), **ventana de proyección sin bordes** con
> cierre sano (X/ESC sin duplicar, reabrible), **modo operador** de dos
> niveles con texto completo sin proyectar, **Biblia por libro/capítulo sin
> buscar + comparación de dos versiones lado a lado**, **biblioteca de
> cantos completa sin búsqueda**, **PPTX original sin extracción**
> (PowerPoint lo proyecta vía COM) y **cultos sin generar archivos pptx**
> (solo-cargar: la lista del culto + la BD). La «API local» de OBS/control
> se retiró de la interfaz; el control remoto es el **mando móvil** por LAN.

> **v7.0.0 «ULTRA»**: aplica el **Plan de Ultra Implementación** completo
> (80 ítems F0–F6): bootstrap nativo con perfiles A/B/C, **IPC ipc.v1** por
> pipes con nombre, **modo emergencia nativo** (perfil C proyecta texto/
> imagen/video SIN .NET), proyecto **ahp.v1** con IDs estables y autoguardado,
> sincronización por línea con **syncMark**, video **DirectShow** desde el
> núcleo, **Lower Third** nativo, **API v1 con Bearer + QR + mando móvil**,
> **Holyrics/Planning Center/NDI/Drive**, diagnóstico integrado y auditoría
> automática de prohibiciones — trazabilidad íntegra en
> `docs/plan-ultra-trazabilidad.md`. **Ningún CMake fue modificado**
> (restricción del plan).

**Proyección para iglesias con arquitectura híbrida: núcleo C++17 nativo + interfaz WPF (.NET 4.8) con baseline WinForms (.NET mínimo 3.5)**
— motor de proyección en tiempo real con la agilidad de desarrollo de .NET,
**sin JVM, sin Electron, sin redistributables y sin instalaciones**.

> ⚠️ Código fuente bajo **Licencia de Solo Lectura (View-Only)** — ver [LICENSE.md](LICENSE.md).
> Los binarios publicados en [Betas](https://github.com/isaac4722/Fusion-HP/releases) son de uso libre.
> **Todas las publicaciones actuales son BETAS (pre-releases de evaluación).**

---

## 🚀 Descargar (sin instalar nada)

1. Ir a **[Betas](https://github.com/isaac4722/Fusion-HP/releases)** (pre-releases de evaluación).
2. Descargar el paquete para tu arquitectura:
   - `LuminaPresentation-7.1.0-beta.1-win-x86.zip` — **Windows 7 SP1 … Windows 11 (32 bits)**
   - `LuminaPresentation-7.1.0-beta.1-win-x64.zip` — **Windows 7 SP1 … Windows 11+ (64 bits)**
3. Descomprimir el ZIP **completo** y ejecutar **`LuminaLauncher.exe`**.
   **El usuario nunca instala nada**:
   - Windows 10 1903+ / Windows 11 → **.NET Framework 4.8** integrado en el SO
     (el launcher usa `net48\LuminaPresentation.exe`: **interfaz WPF**).
   - Windows 7 SP1 / 8.x → **.NET Framework 3.5 SP1** integrado de fábrica
     (el launcher usa `net35\LuminaPresentation35.exe`, la línea base mínima
     con interfaz WinForms).
   - El motor C++ (`LuminaCore.dll`) va enlazado estáticamente (/MT): **cero redistributables**.
   - El launcher **verifica el paquete completo antes de lanzar** (exe + DLL nativa
     + las 3 DLL gestionadas en la carpeta de la variante elegida) y explica con
     claridad qué hacer si falta algo.
   - Ambas variantes comparten la misma carpeta de datos `data\`.

## 🌟 Novedades v7.1.0 «OPERADOR» — Enfoque Holyrics/PowerPoint

> Correcciones del prototipo probado por el usuario: **menos generación de
> archivos, más aplicación nativa**. La creación/edición de diapositivas se
> siente como PowerPoint y la operación en vivo como Holyrics:

> - **Editor de diapositivas de LIENZO LIBRE** («Culto › Diapositiva
>   (lienzo)…»): elementos de texto/imagen que se **arrastran** por el
>   lienzo 16:9, se **redimensionan por las esquinas**, se **editan con
>   doble clic en el sitio** (Supr/Ctrl+D/flechas/snap a retícula y
>   centros), propiedades (alineación, color, opacidad, tamaño fijo o
>   auto) y capas reordenables. **WYSIWYG total**: el editor usa el mismo
>   contrato de coordenadas que el motor nativo (cada elemento se dibuja
>   en su rect en la proyección). Doble clic en un ítem «Diseño» del culto
>   lo reabre; editar con el culto **en vivo recarga conservando la slide
>   actual**.
> - **Ventana de proyección SIEMPRE sin bordes** (`WS_POPUP`): a pantalla
>   completa (por defecto) o 960×540 centrada. El botón/F5 es un
>   **interruptor con estado real** (sondeo del núcleo — el cierre con X o
>   ESC del usuario se refleja). **Cerrar con X/ESC ya no duplica la
>   ventana** y se puede reabrir cuantas veces sea; cambiar de monitor
>   **recoloca la misma ventana** sin abrir otra.
> - **Modo OPERADOR (En Vivo) de dos niveles** estilo Holyrics: lista de
>   ÍTEMS (canción/pasaje/texto/presentación/diseño…) con sus slides;
>   **clic = leer sin proyectar**, doble clic = proyectar; seguimiento en
>   vivo del ítem proyectado y panel de **texto completo** (la Biblia
>   entera, la letra) para leer sin proyectar nada.
> - **Biblia por libro y capítulo SIN buscar**: los 66 libros siempre
>   visibles + selector de capítulo + versiones instaladas (cargadas al
>   abrir la BD); «Leer capítulo» muestra los versículos sin proyectar.
>   **Comparación de DOS versiones lado a lado** por versículo (y
>   proyección de ambas juntas, etiquetadas).
> - **Cantos sin búsqueda**: la biblioteca COMPLETA aparece al abrir la BD
>   (ordenada por título); el cuadro queda como filtro opcional con
>   «Mostrar todo».
> - **PPTX ORIGINAL sin extracción**: «Agregar PPTX…» conserva el archivo
>   tal cual y **PowerPoint lo proyecta completo** (COM en modo kiosco
>   sobre el monitor del proyector) al ponerlo en vivo — sin PowerPoint,
>   el ítem muestra el nombre del archivo. La extracción de solo-texto
>   queda como opción secundaria.
> - **API local retirada**: el «modo API» loopback orientado a OBS/control
>   externo se eliminó de Ajustes (no funcionaba); el control remoto
>   sigue siendo el **mando móvil por red local** (Integraciones).
> - **Cultos/escenarios SIN generar pptx**: armar el culto se siente como
>   crear un pptx (lista de ítems, editor de diapositivas, orden) pero
>   todo vive en la app + la BD (**solo-cargar**, como Holyrics). La
>   exportación a PPTX/PDF/imágenes queda como opción (Exportar).

## 🌟 Novedades v6.1.0 «GUION» — Motor de scripts JSLib

> **El último requisito §3.3 del spec llega completo**: automatización e
> integración con otros sistemas (mezcladores, OBS, servicios web) mediante
> módulos `.js` cargados en caliente — **sin recompilar ni instalar nada**
> (IActiveScript/JScript del propio Windows, ES3, cero despliegue):
> - **Módulos del usuario**: los `.js` de `data\modules\` se cargan al activar
>   el motor o con «Recargar» (tarjeta **Módulos JS** en *Integraciones*:
>   carpeta, estado y registro en vivo de 60 líneas). Un módulo roto no impide
>   que el resto cargue (error con archivo y línea, como el spec §2.6 exige).
> - **API `jslib` completa**: `log/notify`, `httpGet(url, cb)` (15 s),
>   `tcp(id, host, puerto, onLine)` + `tcpSend/tcpClose`, `ws(id, url,
>   onMessage)` + `wsSend/wsClose` (net48), `cmd('next'|'prev'|'black'|
>   'clear'|'show')`, `showText(texto)`, `onEvent(nombre, cb)` y
>   `setTimeout/clearTimeout` — callbacks SIEMPRE en el hilo de UI (el motor
>   COM es apartamento-hilo; sockets en hilos propios sin async/await).
> - **Eventos en tiempo real**: los módulos ven los mismos eventos que los
>   activadores (`slide_changed`, `item_changed`, `song_started`,
>   `video_ended`, `midi_note/cc/program`) con payload JSON (helper
>   `jsonParse` del preámbulo ES3).
> - **Activadores → scripts**: nueva acción `script` (`function=nombre ·
>   data={"k":v}`) invoca funciones globales de los módulos.
> - **Gate REAL en CI**: el `--selfcheck` crea el motor COM en el runner de
>   Windows y ejercita módulos, funciones globales, eventos, `cmd`,
>   `showText` y un `setTimeout` asíncrono (bombeo del dispatcher) — la
>   ABI nativa NO cambió (JSLib es capa gestionada pura).

## 🌟 Novedades v6.0.0 «HORIZONTE» — BETA

> **Las funciones diferidas del spec llegan al motor y a la capa de datos**,
> conservando la arquitectura híbrida probada (C++17 /MT + WPF/WinForms) y
> TODOS los gates de CI (selfcheck + uicheck + flowcheck + Win7-imports,
> ambas variantes y arquitecturas):
> - **Transiciones de proyección**: fundido (crossfade) entre diapositivas —
>   AlphaBlend sobre el doble búfer GDI desde el **último fotograma
>   proyectado** (continuidad visual exacta, cero destello de escritorio).
>   Configurable en *Ajustes › Proyección* (corte / fundido, 0..5000 ms,
>   persistente) y por la nueva API `lumina_set_transition` (ABI aditiva,
>   reflejada en el estado del motor).
> - **Resaltado en PROYECCIÓN**: cuando un pasaje llega al escenario desde la
>   **búsqueda bíblica por palabra**, la palabra buscada se proyecta en
>   **color de acento** (coincidencia insensible a mayúsculas y acentos
>   latinos, con frontera de palabra — módulo portable `Highlight` con
>   tests propios).
> - **Importación OSIS XML**: segundo formato académico de biblias junto a
>   ZEFania/.BIB/JSON — streaming con memoria constante, códigos de libro
>   OSIS 1..66, notas editoriales descartadas (`.w`/`.seg` aplanados).
> - **Importación PPTX**: las diapositivas de texto de un PowerPoint entran a
>   la cola del culto (**1 diapositiva = 1 ítem**, fidelidad de proyección) —
>   lector ZIP/OPC propio (`ZipReader`, cero dependencias del GAC), orden real
>   vía `sldIdLst`→`rels`, round-trip contra el propio exportador verificado
>   por tests.
> - **Notas del director persistentes**: el panel de notas de la tercera
>   pantalla se guarda **por ítem** del culto y viaja en el plan JSON; nuevo
>   **«Guardar plan JSON…»** exporta el culto completo (ítems + notas +
>   resaltados) re-importable.

## 🌟 Novedades v5.4.0 «ESTUDIO» — BETA

> **La GUI principal pasa a WPF (Windows Presentation Foundation)** sobre
> .NET Framework 4.8, conservando el motor probado, la arquitectura híbrida
> y TODOS los gates de CI (selfcheck + uicheck + flowcheck + Win7-imports,
> ambas variantes y arquitecturas). La baseline net35 (WinForms) se conserva
> para Windows 7 SP1 sin .NET 4.8. Proyecto `managed/Lumina.WPF`:
> - **Diseño «Lumina Studio» rehecho en XAML**: tokens semánticos de color
>   (superficies/texto/marca/estados), tarjetas redondeadas, botones con
>   variantes (primary/secondary/danger/ghost) y estados completos
>   (hover/pulsado/foco visible/deshabilitado/toggle activo), navegación
>   lateral con píldora ámbar y barra de estado con punto de severidad.
> - **94 iconos vectoriales Feather (licencia MIT)** convertidos a geometrías
>   XAML (`Theme/Icons.xaml`) — nitidez perfecta en Win7 y Win11, sin
>   dependencias de archivos externos.
> - **Vista previa del tema con contorno por silueta y sombra REAL**
>   (FormattedText.BuildGeometry: la MISMA técnica del motor nativo).
> - **UX transversal**: F1 mapa de atajos · Ctrl+1…9 navegación · foco
>   visible por teclado · tooltips con atajos · estados vacíos explicativos.
> - **Ventanas auxiliares intactas** (video WMP, monitor de escenario,
>   director, zócalo): HWNDs WinForms coexisten con WPF en el mismo proceso.
> - **Mismo contrato de paquete**: el launcher y verify_portable no cambian —
>   `net48\LuminaPresentation.exe` ahora es el exe WPF.

## 🌟 Novedades v5.3.0 «INTERFAZ»

> **Rediseño completo de la UI/UX** («Lumina Studio»), conservando el motor
> probado de v5.2.0 y los mismos gates de CI (selfcheck + uicheck +
> flowcheck + Win7-imports, ambas variantes y arquitecturas):
> - **Sistema de diseño nuevo** (`managed/Lumina.UI/UiKit.cs`): paleta con
>   gradación de profundidad, **tarjetas redondeadas** con brillo superior,
>   botones owner-drawn con variantes (primario/secondary/danger/ghost) y
>   estados (hover/pulsado/foco/deshabilitado/activo), **32 iconos
>   vectoriales escalables** dibujados con GDI+ (idénticos en Win7 y Win11) y
>   casillas oscuras coherentes con el tema.
> - **Carcasa nueva**: cabecera con logotipo «faro» + chips de estado +
>   acceso rápido al proyector desde cualquier página; navegación lateral
>   **agrupada por secciones** (PRESENTACIÓN · BIBLIOTECA · EXTENSIÓN ·
>   SISTEMA) con píldora activa ámbar; barra de estado con punto de severidad
>   (rojo = fallo, ámbar = aviso).
> - **Página «En Vivo» rehecha**: transporte compacto con iconos + tooltips
>   que muestran los atajos, botón «Negro» que se enciende cuando la salida
>   está oculta, lista de diapositivas alta (34 px) con insignias numéricas,
>   hover y barra ámbar en la diapositiva en curso, vista previa 16:9 con
>   borde «en vivo» e información «Jn 3:16 · Diapositiva 2 de 5», y estados
>   vacíos que explican qué hacer.
> - **UX transversal**: **F1** abre el mapa de atajos, **Ctrl+1…9** salta
>   entre páginas, foco visible por teclado en todos los controles propios,
>   marcas de agua en las búsquedas y tooltips en las acciones principales.

## 🌟 Novedades v5.2.0 «MOTOR»

> **Corrección crítica** (reporte de campo en Win7 SP1 x86 con .NET 4.8):
> la app abría pero «**solo cargaba la GUI, más nada**» — ninguna acción
> respondía y «Cargar al escenario» (Biblia) lanzaba `NullReferenceException`
> en `LoadScenarioFromItems`. **Causa raíz**: el CRT/STL estático de MSVC
> enlazaba `GetSystemTimePreciseAsFileTime` (API que **solo existe desde
> Windows 8**) como import **estático** de `LuminaCore.dll` → `LoadLibrary`
> fallaba en Win7 SP1 con `ERROR_PROCEDURE_NOT_FOUND` → núcleo null →
> «modo limitado». v5.2.0:
> - **Fix nativo de raíz**: `/DELAYLOAD` + hook (`Win7Compat.cpp`) — API
>   real en Win8+, fallback seguro sobre `GetSystemTimeAsFileTime` en Win7.
> - **Blindaje total de flujos**: toda acción que toca el núcleo avisa en la
>   barra de estado, **jamás lanza** (además, el fallo del núcleo queda con
>   diagnóstico completo `LoadLibrary`+`GetLastError` en el log de sesión).
> - **Gate Win7 en CI** (`tools/verify_win7_imports.py`): audita las tablas
>   de importación de cada binario publicado — cero APIs Win8+ estáticas.
> - **Gate de flujos** (`--flowcheck`): Biblia→Escenario completo y en modo
>   limitado simulado, ANTES de empaquetar.

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
| **Interfaz** | **C# WPF** (`net48\LuminaPresentation.exe`, v5.4.0 «ESTUDIO») + **baseline WinForms** (`net35\LuminaPresentation35.exe`) | Editor de escenarios, bibliotecas (canciones/biblia con búsqueda FTS por palabra), control En Vivo con teclado, vista previa renderizada por el motor, temas, importadores JSON/.BIB/**ZEFania XML**, exportadores **PPTX/PDF**, activadores, ajustes |
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

# Capa gestionada (net35+net48+WPF+net8 en Core; funciona también en Linux)
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
  CI de 7 jobs con gates, empaquetado portable x86/x64 y betas automáticas (pre-releases).
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
