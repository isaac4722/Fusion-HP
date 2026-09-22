# Bitácora de turnos — LuminaPresentation Suite

> Registro acumulativo de trabajo (append-only). Formato definido en `AGENT.md`.

---
## [CIERRE-2026-09-21-N] v1.6.0 «ESTABILIDAD» publicada y verificada de extremo a extremo · 2026-09-21 UTC
- Agente: Z.ai Code (GLM) — cierre del ciclo (pasos 8-9 de AGENT.md)
- Hecho:
  - Runs del tag v1.6.0 (35659827527) y de main (35659814706): **success — 0 errores / 0 warnings**, los 2 GATES de release en verde (purga de drafts + verificación post-publicación). CI verde al PRIMER intento del tag.
  - **Release v1.6.0 PUBLICADA** (id 393322886, draft=false, 21:57:02Z) con sus 3 assets: `LuminaPresentationSuite-v1.6.0-x64-portable.zip` (89.0 MB), `-x86-portable.zip` (84.5 MB) y `SHA256SUMS.txt`.
  - **Verificación post-publicación (descarga real)**: ambos zips descargados; **SHA256 idénticos** a los publicados (197441f3… x64 · b855b700… x86); **gate `verify_portable.py` OK en ambos** (392 binarios PE auditados por arquitectura, todas las dependencias satisfechas dentro del paquete); **VS_FIXEDFILEINFO 1.6.0.0** confirmado en ambos exe; componentes presentes (Qt5Qml.dll, qtiff.dll, LibVLC).
- Gates: CI=success (runs main + tag, primer intento) · release=publicada con 3 assets · SHA256=exactos · verify_portable=OK x64 y x86 · versión exe=1.6.0.0 · harness local=45/45 · build local=0 err/0 warn
- Bloqueos: ninguno
- Siguiente: ninguna — v1.6.0 «ESTABILIDAD» cerrada: auditoría profunda del código nuevo de 3 versiones, 37 defectos corregidos (3 críticos reproducidos antes del fix), suite de regresión permanente de 45 checks, release verificada de extremo a extremo.
---
## [QA-2026-09-21-M] v1.5.0 → v1.6.0 «ESTABILIDAD» · Auditoría profunda de v1.3.0→v1.5.0: 3 críticos + 19 medios + 15 bajos corregidos · 2026-09-21 UTC
- Agente: Z.ai Code (GLM) — ciclo completo con 3 auditores paralelos + 5 lotes de corrección + suite de regresión nueva
- Hecho:
  - **Auditoría en 3 frentes paralelos** (red/integraciones · motores core · GUI/integración) del código añadido en v1.3.0→v1.5.0, con verificación de cada hallazgo contra llamadores y pruebas empíricas (el crash del servidor se REPRODUJO con SIGABRT antes del fix).
  - **CRÍTICOS**: (C1) WebServer::start() re-entrada con clientes WS → doble free/corrupción de heap (móvil conectado + Ajustes › Aplicar = proceso muerto); clientes ahora se desconectan/cierran/setParent(nullptr)/deleteLater ANTES de destruir el servidor, también en stop() y para sockets HTTP. (C2) MediaEngine::pollState usaba m_main tras emitir finished() (el handler lo libera) → NULL deref al terminar CUALQUIER video/audio; guard + emisión protegida. (C3) PptxEngine::renderToSlides producía Slide::Pptx que ni Renderer ni DisplayEngine dibujan → PPTX de la cola del culto proyectaba en blanco; ahora Slide::Image (mismo patrón del lienzo libre v1.2.0).
  - **Servidor endurecido**: token exigido también en upgrade WS y endpoints de estado (/api/state, /api/overlay.json, /api/director.json) cuando está configurado (páginas web lo reenvían); buffer HTTP limitado a 64 KB (DoS de RAM imposible; pipelining conserva el sobrante); índice de query sobre el target crudo; rutas desconocidas → 404.
  - **Integraciones**: Drive multipart 5 MB → subida resumable en streaming desde QFile (sin duplicar el vault en RAM); rotate usa el parámetro accessToken y registra errores; connectAccount ya no borra el refresh token antes de tiempo; loopback con guard + deleteLater. JsEngine: recreate() ya no fuga el QJSEngine viejo; señales retransmisoras jsLogChanged/jsNotify (el log en vivo sobrevive a la recarga); httpGet con redirect policy. Telegram filtra por chat_id y con timeout + anti-solapamiento de encuestas; webhook con QUrlQuery + percent-encoding; ObsClient registra errores/close codes (4001 = contraseña).
  - **Core**: importador ZEFania con guard anti-duplicado + índice UNIQUE (version,book,chapter,verse) + dedupe migración única + INSERT OR IGNORE; versionCode derivado del biblename (fallback archivo); FTS con bind parameters (términos «100%1» ya no corrompen el SQL); transacciones con rollback al primer fallo. PPTX: filtro de extracción (XML/imagenes, ≤20 MB — videos embebidos ya no van a RAM), addFile verificado, txBody vacío emite <a:p>, xmlEscape limpia controles, grupos con pila de transformación chOff/chExt, tablas como cajas virtuales, flush final. Renderer: DPR del destino (proyección nítida en escalados 125–150 %), ruta rich con contorno por silueta + sombra del tema + kLineSpacing compartida, cache LRU de 3 entradas (fondo+actual+siguiente) con recencia real en hit.
  - **GUI/flujo**: video principal en pantalla se detiene/oculta al proyectar contenido nuevo (el audio puro NO se interrumpe); triggers JSON del arranque ahora restaura MIDI In (mapa nota→comando incluido); Modo Hinario: canciones solo-coros ya no proyectan vacías y el coro repetido se deduplica (V1 C V2 C exacto); startService posiciona fila 0 (el primer qnext remoto/MIDI avanza, no repite); PCO refleja solo en la cola del culto cargado (m_loadedPlaylistId) y refresca el combo al crear el culto fallback; pantallas guardadas se restauran en la toolbar al arrancar (+ screen_director persistida); closeOverlay con estado vacío limpia la salida y sincroniza el estado web; chip del servidor muestra «apagado» inicial; acento del tema aplicado a la Pantalla Director (applyAccent conectado); StageWindow cachea el tema (sin re-pulido de QSS por slide); PCO sin doble fetchPlans; CSV con escapado; errata «ESCENARIO (músicos)»; código muerto eliminado (publishDirectorState, normalizeTitle).
  - **Suite de regresión NUEVA (45 checks, fuera del repo)**: WebServer (re-entry C1 con cliente conectado — antes SIGABRT —, token WS/estado, 400 overflow, 404), Lyrics hinario (solo-coros, dedupe coro, orden), Database (biblia no duplicada + versionCode biblename + FTS %1 + tags), PptxEngine (kind Image, chars de control, txBody vacío, roundtrip), Renderer cache (3 entradas + recencia + miss bajo demanda), Chords/BibleRef base. **45/45 OK.**
  - Smoke offscreen de la app real: arranque limpio, seed, Biblia importada, auto-backup, servidor ws:8765/http:8088, /api/state, /api/cmd, remote.html y /api/director.json OK.
  - Version bump 1.6.0 (CMakeLists, main.cpp, workflow APP_VERSION fallback) + notas de release v1.6.0 + README (descarga + resumen de estabilidad).
- Decisiones:
  - **Cache de imágenes 3 entradas (no 2)**: la decisión v1.5.0 de «exactamente 2» desalojaba el fondo de imagen (usado en TODAS las slides) en cada avance y la slide recién precargada moría antes de usarse; 3 = fondo + actual + siguiente es el mínimo que cumple la intención de la spec §2.5 con memoria acotada. Deviation documentada.
  - **stopMain condicionado a host de video VISIBLE**: detener el medio en cada slide mataría la pista de audio bajo las letras (flujo intencional); isVideoVisible() distingue video que tapa de audio que acompaña.
  - **Token aplicado a WS/estado solo si está configurado**: sin token, LAN abierta como siempre (comportamiento original intacto); con token, blindaje completo — las páginas lo reenvían desde location.search.
  - **Drive resumable con QFile streaming**: multipart está limitado a 5 MB por Google y duplicaba el vault en RAM; el PUT de un QIODevice cumple ambos requisitos sin dependencias nuevas.
  - **Biblia: dedupe dentro de la migración del índice único**: la BD de usuarios de v1.5.0 puede tener duplicados reales; el DELETE GROUP BY corre UNA vez (solo si el índice no existe) y el índice UNIQUE previene el regreso.
- Gates: build=OK local Linux Qt 5.15.2 gcc **0 errores / 0 warnings** · harness v1.6.0=**45/45 OK** · smoke offscreen app real=OK (API, cmd, remote.html, director.json) · CI Windows x86+x64 → pendiente del run del tag v1.6.0
- Bloqueos: ninguno (1 bug introducido y corregido en el propio interleave durante el desarrollo — detectado por el harness antes del commit: el coro quedaba al final en vez de intercalado)
- Siguiente: commit → push → tag v1.6.0 → CI → verificación de release publicada (API + descarga + SHA256 + verify_portable + FileVersion 1.6.0.0)
---
## [CIERRE-2026-09-22-L] v1.5.0 «NEXO» publicada y verificada de extremo a extremo · 2026-09-22 UTC
- Agente: Super Z (GLM) — cierre del ciclo
- Hecho:
  - Runs del tag v1.5.0 (35648836950) y de main (35648834126): **success** — compilación Windows x86+x64 con 0 errores y los 2 GATES de release en verde (purga de drafts + verificación post-publicación).
  - **Release v1.5.0 PUBLICADA** (id 393260232, draft=false, 20:07:42Z) con sus 3 assets: `LuminaPresentationSuite-v1.5.0-x64-portable.zip` (89.0 MB), `-x86-portable.zip` (84.5 MB) y `SHA256SUMS.txt`. Página pública HTTP 200.
  - **Verificación post-publicación (descarga real)**: ambos zips descargados; **SHA256 idénticos** a los publicados (19184030… x64 · f01ee4ac… x86); **gate `verify_portable.py` OK en ambos** (392 binarios PE auditados por arquitectura, todas las dependencias satisfechas dentro del paquete); **FileVersion 1.5.0.0** confirmado en ambos exe.
  - **Componentes v1.5.0 presentes en los paquetes**: `Qt5Qml.dll` (motor JS/JSLib), `imageformats\qtiff.dll` (soporte TIF) y `director.html` embebida vía qrc en el exe — verificados en el contenido real.
- Gates: CI=success (runs main + tag) · release=publicada con 3 assets · SHA256=exactos · verify_portable=OK x64 y x86 · versión exe=1.5.0.0 · componentes NEXO=presentes
- Bloqueos: ninguno
- Siguiente: ninguna — v1.5.0 «NEXO» cerrada: especificación-programa-completo.md cumplida en lo funcional (componentes 1-4 + formatos §4 + calidad §2.6) sobre el stack C++/Qt portable (decisión documentada), skills Qt instaladas y aplicadas

---
## [FEAT-2026-09-22-K] v1.4.0 → v1.5.0 «NEXO» · Integraciones de la especificación completa (Pantalla Director + PCO + Drive + JSLib + PPTX 4 niveles) · 2026-09-22 UTC
- Agente: Super Z (GLM) — desarrollo con las skills Qt solicitadas (qt-ui-design · qt-cpp-review · qt-cpp-docs · qt-cmake-project)
- Hecho:
  - **Instalación de las skills Qt pedidas por el usuario**: clonación de `TheQtCompanyRnD/agent-skills` (fuente canónica; `liueggy/qt5-skills` es su fork) e instalación de `qt-ui-design`, `qt-cpp-review`, `qt-cpp-docs` y `qt-cmake-project` en las 4 ubicaciones de skills (`~/.agents/skills/`, `skills/`, `.agents/skills/`, `.claude/skills/`) + `skills-lock.json`; **residuos eliminados** (repos clonados completos y README de repositorio por skill; se conserva SKILL.md + references/platforms + LICENSE de atribución).
  - **Gap analysis de `especificacion-programa-completo.md` vs v1.4.0**: confirmados 8 huecos funcionales (pantalla 3 del multiview, Planning Center, Google Drive, JS/JSLib, TIF, herencia PPTX 4 niveles, lazy loading explícito, log estructurado/captura de crashes); verificados como ya cubiertos: WebSocket bidireccional real (QWebSocketServer), overlay OBS + /api/live.txt, VLC integrado como backend de medios, búsqueda FTS por palabras, volumen/bucle/posición inicial de video, triggers MIDI/video-fin, cronómetro del Stage.
  - **🧭 Pantalla 3 «Director / Instrucciones»** (spec §3.3: *hasta tres salidas independientes*): `DirectorWindow` (ventana nativa frameless: ítem actual + texto + notas + SIGUIENTE ítem con preview + reloj + temporizador + registro PERSISTENTE de los últimos 4 mensajes del operador; tipografía ≥14-26 pt para ~1,5 m según qt-ui-design) + selector de pantalla en la toolbar (3.ª pantalla sugerida con 3+ monitores) + **contraparte web** `/director.html` + `/api/director.json` (estado espejo + historial de 10 mensajes + cuenta atrás) — mensajes/alertas llegan a ambas vías.
  - **📅 Planning Center Online** (spec §3.3): `PlanningCenter` (API v2, PAT id:secret por HTTP Basic, timeouts 15 s, errores amigables no técnicos por spec §2.6, validación JSON explícita, errores TLS registrados) + UI en Comunicación (ministerios → planes futuros → importar) + `MainWindow::onPcoImportItems`: emparejamiento de canciones por título normalizado (insensible a mayúsculas/acentos) contra `searchSongs`, ítems no musicales → avisos con sus notas, reflejo inmediato en la cola del dock + persistencia en el culto activo.
  - **☁ Respaldo en Google Drive** (spec §3.4): `DriveBackup` con **OAuth 2.0 loopback** (QTcpServer en 127.0.0.1:puerto-aleatorio + apertura del navegador; sin códigos copiados a mano ni secretos embebidos), alcance mínimo `drive.file` sobre `appDataFolder` (carpeta oculta de la app), renovación automática de access token, subida `multipart`, **rotación de 4 copias**, descarga de la última copia y desconexión; señal nueva `Database::autoBackupCreated(path)` para subir la copia semanal automáticamente cuando `drive_auto_upload=1`; UI completa en Ajustes (guía de creación de credenciales en el README).
  - **⚙ Módulos JavaScript / JSLib** (spec §3.3): `JsEngine` (QJSEngine del módulo Qt Qml) + `JsLibHost` (objeto `jslib` con Q_INVOKABLEs: sockets **TCP y WebSocket persistentes** por id, `httpGet` con callback y timeout, `setTimeout/clearTimeout`, `log/notify`, `onEvent`) — carga de `<datos>/modules/*.js` al arrancar, recarga en caliente (estado limpio, sockets cerrados), errores de sintaxis con archivo:línea, registro en vivo en Comunicación › Módulos JS; `Triggers::fireEvent` retransmite TODOS los eventos a los `onEvent` de JS (acción «script» vía `callFunction` con payload).
  - **🖼 TIF en imágenes** (spec §3.2 «JPG, PNG, GIF, BMP, TIF»): filtro de medios + drag & drop ampliados (gif/bmp/tif/tiff) y `imageformats\qtiff.dll` empaquetado en el CI; TIFF (no decodificable por LibVLC) se proyecta por la ruta nativa Qt (Slide::Image) — verificado con carga real de un .tif vía plugin.
  - **📊 PPTX con herencia de 4 niveles** (spec §4.3 Tema→Maestro→Diseño→Diapositiva): la exportación ahora escribe `theme1.xml` (clr/font/fmtScheme completos derivados del tema activo), `slideMaster1.xml` (+rels → theme + layout), `slideLayout1.xml` «blank» (+rels → master) y `slides/_rels/slideN.xml.rels` → layout; `[Content_Types].xml` registra las 3 partes; `presentation.xml` declara `sldMasterIdLst` (maestro = rId1, slides desde rId2, id ≥ 2147483648); round-trip de importación verificado.
  - **🧠 Lazy loading real** (spec §2.5 «solo el elemento actual y el inmediato siguiente»): `Renderer::preloadImage/cachedImage` con **cache LRU de exactamente 2 entradas**; MainWindow precarga la siguiente slide de imagen + fondo del tema + imagen del siguiente ítem de la cola; fuera de cache se decodifica bajo demanda SIN cachear (memoria acotada para 4 GB).
  - **📋 Log estructurado + captura de crashes** (spec §2.6): handler con `timestamp + nivel + categoría del módulo emisor` (context.category), **rotación** al superar 2 MB (una generación `lumina.old.log`) y `SetUnhandledExceptionFilter` en Windows que registra código/dirección/módulo de la excepción antes de terminar.
  - **Skills aplicadas al desarrollo**: lint determinista **qt-cpp-review** (60+ reglas) sobre los fuentes nuevos/cambiados — corregidos los hallazgos reales (sslErrors registrados en PCO/Drive, validación JSON explícita, cuenta atrás en UTC DEP-11, timeout del webhook ERR-5); documentado el triaje de falsos positivos (URIs de namespaces XML ≠ URLs http, `.arg(a,b)` multi-argumento, TMO-1 de API Qt5); **qt-ui-design** aplicado a DirectorWindow y secciones nuevas (tipografía por distancia, 3-4 tamaños por pantalla, doble pista de estado); **qt-cpp-docs** para la documentación de referencia (`docs/api/JsEngine.md`, `PlanningCenter.md`, `DriveBackup.md`, `DirectorWindow.md`); **qt-cmake-project** para el componente Qml en CMake.
  - **Version bump 1.5.0** (CMakeLists, main.cpp, workflow) + `Qt5Qml.dll` y `qtiff.dll` en el despliegue del CI (verify_portable auditará sus imports) + notas de release + README (descarga v1.5.0, tabla de características, guías de Drive/JS/PCO).
- Decisiones:
  - **Stack: se mantiene C++/Qt 5.15 (NO .NET Framework 4.8)**: la sección 2.1 del MD nuevo pide .NET 4.8, pero (a) .NET 4.8 NO viene preinstalado en Windows 7/8 — instalarlo rompería el requisito PRIMARIO e innegociable del usuario («autocontenido, el usuario final no instala nada», Win7 x32 → Win11); (b) el propio MD se contradice (§5.1 prescribe Flutter+pubspec); (c) la base C++/Qt portable es la decisión registrada desde v1.0.1 y revalidada en cada versión; (d) el usuario solicitó expresamente skills de **Qt** para este desarrollo. La especificación se cumple ÍNTEGRAMENTE en lo funcional (componentes 1-4, formatos §4, calidad §2.6); el sustituto técnico equivalente que satisface simultáneamente Win7 x32 + cero instalación es Qt 5.15 estático-portable.
  - **TIF por la ruta Qt, GIF/PNG/BMP por VLC**: LibVLC no decodifica TIFF; el GIF animado exige VLC. Cada formato va por la vía que funciona, sin regresiones.
  - **OAuth loopback con credenciales del usuario (no embebidas)**: los «installed app» secrets de Google no son confidenciales de todas formas, pero pedirlos al usuario evita que un mismo client_id quede revocado para toda la base de usuarios y mantiene el binario neutro.
  - **`appDataFolder` con `drive.file`**: copias de la app en carpeta oculta, sin acceso al resto del Drive — mínimo privilegio visible para el usuario.
  - **Mensajes del director persistentes (no expiran)**: la alerta del Stage es efímera por diseño; el director debe poder releer el aviso — registro con hora de los últimos 4.
  - **Cache de imágenes de 2 entradas exactas**: la spec pide «actual e inmediato siguiente» — una cache mayor sería invento; la decodificación fuera de cache sigue siendo lazy pura.
  - **Contrato loadModules=false con módulo roto**: el módulo válido sigue cargado y el error queda reportado con archivo/línea; false no aborta nada.
- Gates: build=OK local Linux Qt 5.15.2 gcc **0 errores / 0 warnings** · harness v1.5.0=**58/58 OK** (JSLib 16 · PCO 4 · Drive 6 · auto-backup 2 · lazy loading 8 · PPTX 4 niveles 18 · WebServer Director 4) · **regresión harness v1.4.0=49/49 OK** · smoke offscreen app real=**6/6 OK** (API 1.5.0, /director.html, /api/director.json completo, módulo JS cargado al arrancar, log estructurado con categoría) · TIFF cargado vía qtiff (formatos soportados verificados) · lint qt-cpp-review: hallazgos reales corregidos, falsos positivos documentados · CI Windows x86+x64 → pendiente del run del tag v1.5.0
- Bloqueos: ninguno (2 hallazgos del harness eran del PROPIO test: `QStringList::count()` cuenta coincidencias exactas, no subcadenas; y el qrc no estaba compilado en el binario de pruebas — ambos corregidos en el harness; el repro mínimo confirmó que el motor JS era correcto)
- Siguiente: commit → push → tag v1.5.0 → CI → verificación de release publicada (API + descarga + SHA256 + verify_portable + FileVersion 1.5.0.0)

---
## [CIERRE-2026-09-22-J] v1.4.0 «PRISMA» publicada y verificada de extremo a extremo · 2026-09-22 UTC
- Agente: Super Z (GLM) — cierre del ciclo
- Hecho:
  - Runs del tag v1.4.0 (35641219014) y de main (35641216067): **success** — compilación Windows x86+x64 con 0 errores, GATES de release en verde.
  - **Release v1.4.0 PUBLICADA** (id 393214129, draft=false, 18:55:23Z) con sus 3 assets: `LuminaPresentationSuite-v1.4.0-x64-portable.zip` (86.8 MB), `-x86-portable.zip` (82.2 MB) y `SHA256SUMS.txt`. Páginas públicas HTTP 200.
  - **Verificación post-publicación (descarga real)**: ambos zips descargados; **SHA256 idénticos** a los publicados (eaa44ab3… x64 · bd7243b8… x86); 396 archivos por paquete; **gate `verify_portable.py` OK en ambos** (390 binarios PE auditados por arquitectura, todas las dependencias satisfechas dentro del paquete); **FileVersion 1.4.0.0** confirmado en ambos exe.
- Gates: CI=success (runs main + tag) · release=publicada con 3 assets · SHA256=exactos · verify_portable=OK x64 y x86 · versión exe=1.4.0.0
- Bloqueos: ninguno
- Siguiente: ninguna — v1.4.0 «PRISMA» cerrada: automatización semántica + tags extendidos + MIDI In + accesibilidad WCAG, release verificada

---
## [FEAT-2026-09-22-I] v1.3.0 → v1.4.0 «PRISMA» · Automatización semántica + accesibilidad + MIDI In · 2026-09-22 UTC
- Agente: Super Z (GLM) — continuación del desarrollo con skills instaladas (cpp-pro: patrones C++ de producción · hallmark: disciplina de diseño de GUI)
- Hecho:
  - **Instalación de skills solicitadas por el usuario**: `hallmark` (nutlope/hallmark — diseño anti-slop, 4 ubicaciones + skills-lock.json; el CLI npx funcionó parcialmente y se completó el registro manualmente) y `cpp-pro` (0xharryriddle/codex-field-kit — C++ producción; instalación manual por clonación directa al no exponer el CLI el path `skills-hermes/`).
  - **🧠 Motor de automatización semántica por etiquetas** (spec Holyrics §Personalización Avanzada: «si se reproduce una canción con la etiqueta 'lento', aplicar el tema 'calma' y seleccionar un fondo con la etiqueta 'ocaso'»):
    1. DB: tablas `resource_tags` (kind theme/media), `media` (biblioteca de fondos) y `tag_rules`; API completa con StmtGuard RAII (cpp-pro) — setThemeTags/themeTags/allTagNames/addMedia/removeMedia/mediaLibrary/setMediaTags/mediaTags/mediaByTag/addTagRule/deleteTagRule/setTagRuleEnabled/tagRules.
    2. MainWindow::applySemanticRules enganchado en goLiveSong ANTES de construir slides (cero parpadeo): matching case/acento-insensible, primera regla que casa gana, fondo por etiqueta determinista (primer match alfabético — la aleatoriedad en vivo es un defecto, no una feature), tema aplicado en vivo SIN tocar la plantilla (mismo criterio que la transposición en vivo), aviso en statusbar + log.
    3. Interruptor maestro `semantics_on` + tabla de reglas editable en Comunicación › Automatización semántica (crear/eliminar/alternar con un clic).
  - **🏷 Tags extendidos a temas y fondos** (spec Holyrics): campo Etiquetas en el editor de temas (autocompletado con todos los tags del vault, QSignalBlocker en la carga, guardado junto al tema, herencia en «Guardar como nuevo») y **Biblioteca de fondos** en el panel Temas: añadir imágenes/videos, etiquetarlos, filtrar por etiqueta («buscar 'agua' y ver la galería») y doble clic → fondo del tema en vivo.
  - **🎹 MIDI In (winmm)** (spec Holyrics: eventos de disparo EXTERNOS «recibir un comando MIDI»): clase MidiIn con callback estático de winmm + marshalling seguro hilo→hilo Qt (QMetaObject::invokeMethod QueuedConnection, dispatch Q_INVOKABLE), RAII en el handle (midiInReset+Close en destructor/stop), ignorar note-off y notas fuera de rango; mapa nota→comando editable (defaults didácticos: octava de Do = next/prev/black/clear/logo/qnext/qprev) persistido en el JSON de triggers; los comandos usan el MISMO dispatcher que el control remoto web (onRemoteCommand). UI en Comunicación con chip de estado (dispositivos detectados/escuchando/error) y botón «Probar nota».
  - **♿ Comprobador de accesibilidad WCAG** (spec PowerPoint §Accesibilidad — Accessibility Checker con severidades Error/Advertencia/Correcto): Renderer::contrastRatio/relativeLuminance/contrastLevel (sRGB linealizado, 21:1 máximo), UI en panel Temas con chips ok/warn/err que se recomputan con CADA cambio de color/tipo; gradientes evaluados por peor caso; imagen/video → advertencia con recomendación.
  - **🎬 FIX defecto latente**: cambiar de un tema con fondo de video a otro SIN video dejaba el video anterior reproduciéndose y visible encima del nuevo tema. MediaEngine::backgroundActive() (const noexcept, cpp-pro) + else en showSlideIndex que detiene el fondo activo. La automatización semántica habría hecho este defecto cotidiano.
  - **Calidad (disciplinas de las skills)**: StmtGuard RAII en todo el SQL nuevo (sqlite3_finalize garantizado incluso en returns tempranos), Q_INVOKABLE + marshalling para el callback de hilo winmm, const noexcept en consultas, QSS con chips de estado via property+repolish (tokens Aurora, cero estilos inline), Comunicación envuelta en QScrollArea para netbooks 1366×768, sección 15 de aurora.qss (chips genéricos + notas).
  - **Version bump 1.4.0** (CMakeLists, main.cpp, workflow) + notas de release v1.4.0 + README (descarga, tabla de características con las 4 features nuevas).
- Decisiones:
  - **Fondo por regla determinista (primer match alfabético)**: en un servicio en vivo la sorpresa aleatoria es un defecto; la elección múltiple se resuelve etiquetando más fino.
  - **El tema de la regla se aplica en vivo sin guardar la plantilla**: la regla es una selección de proyección (como la transposición del Stage), no una edición del activo.
  - **MIDI In al dispatcher central (onRemoteCommand)**: un único vocabulario de comandos (web+móvil+MIDI) = menos código, menos divergencias, mismas garantías.
  - **Matching acento-insensible**: los tags los escribe el usuario en dos sitios distintos (canción/regla); «adoración» == «ADORACIÓN» == «adoracion» evita la frustración silenciosa.
  - **StmtGuard solo en código nuevo**: los 1000+ lines de SQL existentes están probados por 3 versiones de harness; refactor masivo = riesgo sin beneficio medible.
- Gates: build=OK local Linux Qt 5.15.2 gcc **0 errores / 0 warnings** · harness v1.4.0=**49/49 OK** (tags temas 7 · biblioteca fondos 8 · reglas semánticas 10 · WCAG 10 · MIDI In dispatch 5 · DB base 9) · smoke offscreen=OK (arranque 1.4.0, seed, servidor 8765/8088, /api/state versión 1.4.0, remote.html servido) · CI Windows x86+x64 → pendiente del run del tag v1.4.0
- Bloqueos: ninguno (1 expectativa del harness corregida: el orden alfabético de mediaByTag pone nubes_loop.mp4 antes que ocaso_01.png — el código estaba bien, el test asumía el orden inverso)
- Siguiente: commit → push → tag v1.4.0 → CI → verificación de release publicada (API + descarga + SHA256 + verify_portable + FileVersion)

---
## [CIERRE-2026-09-21-H] v1.3.0 «AURORA» publicada y verificada de extremo a extremo · 2026-09-21 UTC
- Agente: Super Z (GLM) — cierre del ciclo
- Hecho:
  - Runs del tag v1.3.0 (35631644269) y de main (35631641674): **success** — compilación Windows x86+x64 con 0 errores.
  - **Release v1.3.0 PUBLICADA** (id 393153989, draft=false, 17:25:48Z) con sus 3 assets: `LuminaPresentationSuite-v1.3.0-x64-portable.zip` (86.8 MB), `-x86-portable.zip` (82.2 MB) y `SHA256SUMS.txt`.
  - **Verificación post-publicación (descarga real)**: ambos zips descargados; **SHA256 idénticos** a los publicados; **gate `verify_portable.py` OK en ambos** (390 binarios PE auditados por arquitectura, todas las dependencias satisfechas dentro del paquete); **FileVersion 1.3.0.0** confirmado en ambos .exe; **aurora.qss embebida** verificada en el binario.
- Gates: CI=success (2/2 runs) · release=publicada con 3 assets · SHA256=exactos · verify_portable=OK x64 y x86 · versión exe=1.3.0.0
- Bloqueos: ninguno
- Siguiente: ninguna — v1.3.0 «AURORA» cerrada: GUI completa rediseñada + 9 features de los MDs implementadas y verificadas

---
## [FEAT-2026-09-21-G] v1.2.0 → v1.3.0 «AURORA» · Nueva GUI completa + 9 features de los MDs · 2026-09-21 UTC
- Agente: Super Z (GLM) — ciclo super plan: análisis de MDs → plan → implementación → verificación
- Hecho:
  - **Recuperación y análisis de los 4 MD del usuario** (Requerimientos.md «la Mezcla», holyrics-spec.md, powerpoint-spec.md, síntesis comparativa) desde el cache de sesión; extracción del «Prompt Integral» (spec maestra) y mapeo de gaps vs código v1.2.0.
  - **Auditoría de supuestos defectos**: verifiqué con hexdump un falso positivo de corrupción en `build.yml` (`branches: [main, master]` estaba CORRECTO — un artefacto de renderizado ANSI del visor se comía el `[m` del grep). El workflow está sano.
  - **🎨 GUI «Aurora» (la estrella del plan)**:
    1. `resources/styles/aurora.qss` (~470 líneas) embebida en el ejecutable vía qrc: cobertura TOTAL de widgets (toolbar+toolbuttons, sidebar, tablas+headers, tabs, combos+popups, inputs, spinboxes con flechas custom, checks/radios planos, sliders completos, groupbox-tarjeta, scrollbars finos, menús, statusbar, docks, tooltips, progressbars) + paleta base nueva en main.cpp (con estados Disabled/Placeholder/ToolTip).
    2. **Sidebar con secciones**: BIBLIOTECA / DISEÑO / SERVICIO / SISTEMA (items encabezado no seleccionables; índice de panel viaja en Qt::UserRole — onNavChanged inmune a la posición física).
    3. **Dock «PROYECCIÓN EN VIVO» rediseñado**: chip EN VIVO + info + reloj monoespaciado; preview 16:9 con **marco dorado dinámico** al proyectar (property live + unpolish/polish); **mini-preview de la SIGUIENTE slide** (vista de moderador, spec PowerPoint); **multiview** con miniatura textual del Stage View con acordes (spec Holyrics); **chip de cuenta regresiva** (espejo de la deadline del StageWindow); barra de transporte grande para el Modo Presentación.
    4. **Modo Presentación (F11)** (spec maestro: interfaz minimalista): oculta toolbar y widget central → el dock se expande a TODA la ventana (QMainWindow expande docks sin central) con transporte grande (Anterior/Siguiente/Negro/Logo/Fondo/Versículo). Dock no cerrable. F11 restaura.
    5. Limpieza de estilos inline de 7 paneles → propiedades `class`/objectNames del QSS global (coherencia visual total).
  - **Features de los MDs**:
    1. **Atajos personalizables** (spec Holyrics): 9 acciones (next/prev/golive/black/clear/logo/quickverse/lowerthird/presentation) editables con QKeySequenceEdit en Ajustes › Atajos, con «Restablecer valores de fábrica»; persistidas como PortableText; MainWindow::buildShortcuts los lee con fallback seguro al default (shortcutSetting()).
    2. **Backup/Restauración del vault** (alternativa offline-safe a Google Drive del spec Holyrics): Database::backupTo/restoreFrom con la **Online Backup API de SQLite** (consistente sin bloquear), validación de archivo no-SQLite ANTES de tocar el vault, snapshot pre_restore automático, y **autoBackupIfNeeded semanal con rotación de 4** (llamado en main.cpp tras el seed).
    3. **Mensajes a los remotos** («Custom Messages» spec Holyrics): WebServer::broadcastMessage → toast en remote.html (los eventos `alert` también muestran toast); sección en CommsPanel con título opcional.
    4. **Importador ZEFania XML** (formato del ecosistema Holyrics — miles de versiones libres): Database::importBibleFromZefaniaXml (QXmlStreamReader por nombres LOCALES, soporta BR/STYLE anidados, transacción atómica, código de versión desde el nombre de archivo ≤16 chars) + botón en BiblePanel con cursor de espera y selección automática de la versión importada.
    5. **Exportar en vivo a PNG** (spec PowerPoint: «exportar diapositivas como imágenes»): MainWindow::exportLivePng (1920×1080, numeración 01..NN, sanitización de nombre) + botón en PptxPanel junto al PDF.
    6. **Drag & Drop global** (spec Holyrics: «importar videos directamente arrastrándolos»): imagen → fondo del tema en vivo; video → fondo en bucle; .txt → importa canción (directivas @titulo/@autor/@tono/@bpm con regex multilinea + fallback a nombre de archivo).
    7. **Posición inicial de medios** (spec Holyrics: «posición de inicio»): QSpinBox «Iniciar en (segundos)» con seek diferido 400 ms en MediaPanel.
    8. **Chip de estado del servidor** en statusbar (verde/rojo con puerto real, property on + repolish) + **diálogo Acerca de** estilizado.
  - **Version bump 1.3.0**: CMakeLists (project VERSION), main.cpp (setApplicationVersion), build.yml (fallback APP_VERSION) + **notas de release v1.3.0 completas** + README (nueva tabla de características con GUI Aurora/Modo Presentación/atajos/backup/ZEFania/PNG/drag&drop, atajos F10/F11, nota de personalizabilidad).
- Decisiones:
  - **Modo Presentación ocultando el widget central** (en vez de reparentar widgets o crear una ventana aparte): QMainWindow expande las áreas de dock cuando no hay central visible — cero duplicación de widgets, cero riesgo de state desincronizado, y el dock ya contiene preview+slides+cola.
  - **Backup con Online Backup API** (no copia de archivo ni VACUUM INTO): consistente incluso con la BD en uso, funciona con sqlite3_close pendiente, y permite restaurar EN VIVO (los paneles recargan al reiniciar; el aviso al usuario lo explica).
  - **Código de versión ZEFania ≤16 chars desde el nombre de archivo**: idempotente y predecible (el usuario controla el nombre del archivo); mismo cálculo en Database y BiblePanel para que el botón seleccione exactamente la versión importada.
  - **Toast en remote.html para alert+message**: unifica la UX móvil (antes los eventos alert ni se mostraban en el remoto).
  - Los estilos por-widget (color swatches del ThemePanel) se conservan: son dinámicos por diseño.
- Gates: build=OK **local Linux Qt 5.15.2 gcc — 0 errores / 0 warnings** · harness v1.3.0=**13/13 OK** (ZEFania import + código de versión + Gn 1:1/Jn 3:16 + rechazo de XML corrupto + backup consistente + restore roundtrip + rechazo de no-SQLite + auto-backup sin duplicar) · smoke offscreen=OK (arranque v1.3.0, seed Biblia+5 canciones, **auto-backup creado**, HTTP /api/state versión 1.3.0, /api/cmd ok, remote.html con toast servido)
- Bloqueos: ninguno (2 iteraciones de compilación: include de WebServer en CommsPanel y QString::remove(QRegularExpressionMatch) inexistente en Qt 5.15 → remove(start,length); el único FAIL del harness era un bug del PROPIO archivo de prueba XML — comilla extra — que el importador rechazó correctamente)
- Siguiente: commit → push → tag v1.3.0 → CI Windows x86+x64 → verificación de release publicada (API + descarga + SHA256 + verify_portable)

---
## [FEAT-2026-09-21-C] v1.0.2 → v1.0.3 · Sistema de etiquetas semánticas para canciones · 2026-09-21 UTC
- Agente: Super Z (GLM) — ciclo de mejora incremental sobre la base v1.0.2 funcional
- Hecho:
  - **Auditoría previa al cambio**: verifiqué que el release v1.0.2 publicado funciona — descargué los 2 zips (`LuminaPresentationSuite-v1.0.2-x64-portable.zip` 90.9 MB y `-x86-portable.zip` 86.1 MB), comprobé los SHA256 contra `SHA256SUMS.txt` (coinciden exactos), extraje los paquetes (396 archivos cada uno) y ejecuté el gate `tools/verify_portable.py` contra ambos: 390 PE binarios auditados, todas las dependencias satisfechas dentro del paquete, 0 DLLs externas faltantes. El bug de v1.0.1 (libwinpthread-1.dll ausente) está confirmado arreglado.
  - **Auditoría del build v1.0.2**: revisé los logs del run #35595183820 (CI en `windows-latest` con Qt 5.15.2 MinGW 8.1 + LibVLC 3.0.21) — los 3 jobs (Build x64, Build x86, Create Release) finalizaron en `success` con 0 warnings y 0 errores en la compilación de los 7897 LOC.
  - **Identificación del gap vs specs**: comparé los MD del usuario (Requerimientos.md / holyrics-spec.md / powerpoint-spec.md) contra el código existente. La gran mayoría de features descritas ya están implementadas (Canciones + FTS5, Biblia RVR1909, PPTX import/export, Stage View, Control remoto móvil, Overlay OBS, Triggers + OBS WebSocket v5 + MIDI, Temas, Lienzo vectorial, Cultos, Historial). La feature más mencionada como "inteligente y subestimada" en `holyrics-spec.md` que NO estaba en el código era el **sistema de etiquetas (tags) semánticas** para canciones.
  - **Implementación del sistema de etiquetas**:
    1. **Esquema DB** (`Database.cpp::ensureSchema`): añadidas dos tablas nuevas con `CREATE TABLE IF NOT EXISTS` (migración segura sobre BDs existentes — no rompe instalaciones previas):
       - `tags(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT UNIQUE NOT NULL COLLATE NOCASE)`
       - `song_tags(song_id, tag_id, PRIMARY KEY(song_id, tag_id), FK CASCADE)` + índice `idx_song_tags_tag`
    2. **API de DB** (`Database.h/.cpp`): 5 métodos nuevos:
       - `int addTag(name)` — idempotente (INSERT OR IGNORE + SELECT)
       - `bool setSongTags(songId, tags[])` — transaccional (BEGIN/COMMIT), reemplaza todas las etiquetas
       - `QStringList songTags(songId)` — etiquetas ordenadas
       - `QVector<QPair<int,QString>> allTags()` — ordenado por uso (LEFT JOIN + COUNT + GROUP BY)
       - `QVector<SongRow> searchByTag(tag)` — canciones con esa etiqueta
    3. **UI** (`SongPanel.cpp/.h`):
       - Fila nueva arriba de la búsqueda: combo `🏷 Etiqueta` con `(todas)` + todas las etiquetas existentes.
       - Tabla ampliada de 4 → 5 columnas (añadida `Etiquetas` al final, en azul claro).
       - `reload()` ahora combina filtro por etiqueta + búsqueda FTS por texto (intersección con `QSet<int>` construido manualmente, sin `toSet()` que está deprecado en Qt 5.15).
       - Editor de canciones: nuevo campo `Etiquetas (separadas por coma)` con `QCompleter` que sugiere las etiquetas ya usadas en la biblioteca (autocompletado caso-insensible).
       - `onAdd`/`onEdit`/`onDuplicate` actualizados para persistir/leer/copiar las etiquetas.
    4. **Importación masiva**: `onImportText` reconoce ahora la directiva `@tags a,b,c` en archivos `.txt` y la persiste junto a la canción importada.
  - **Version bump 1.0.2 → 1.0.3**: actualizado en `CMakeLists.txt` (project VERSION), `src/main.cpp` (QApplication::setApplicationVersion) y `.github/workflows/build.yml` (fallback APP_VERSION y tag_name del release).
  - **Notas de release v1.0.3** añadidas al `body` del step `Create Release` en `build.yml`.
  - **README** actualizado: feature row de Canciones ampliado + descarga apunta a 1.0.3.
- Decisiones:
  - **Migración additive, no destructiva**: las dos tablas nuevas usan `CREATE TABLE IF NOT EXISTS` y el resto de la lógica es estrictamente aditiva. Cualquier usuario con una BD v1.0.2 existente la conserva intacta y simplemente obtiene la nueva columna de etiquetas vacía por defecto.
  - **Sin dependencias nuevas**: solo se usan módulos Qt ya enlazados (`Qt5Widgets` provee `QCompleter`, `Qt5Core` provee `QSet`). El binario final no cambia de tamaño significativamente ni añade imports PE.
  - **Especificación cumplida**: el `holyrics-spec.md` describe las etiquetas como "inteligentes y subestimadas" para búsqueda semántica ("buscar 'agua' en vez de 'agua_ondas_azul_oscuro.jpg'"). Esta implementación replica ese patrón para canciones: el operador puede etiquetar con `navidad`, `entrada`, `ofrenda`, `lento`, `rapido`, etc., y filtrar la biblioteca con un clic.
- Gates: build=OK (CI v1.0.3 verde en x64 + x86, 0 warnings / 0 errors) · gate portable=OK (390 PE binarios auditados, todas las dependencias satisfechas en ambos paquetes) · smoke test de arranque=OK (ProductVersion=1.0.3 confirmado en ambos .exe, SHA256SUMS verificados)
- Bloqueos: ninguno (primer intento de build falló por `const QString n.replace()` — corregido con binding de parámetros `sqlite3_bind_text`, patrón consistente con `bindVariant()` existente)
- Siguiente: ninguna — v1.0.3 release publicada y verificada de extremo a extremo

---
## [FIX-2026-09-21-B] v1.0.1 → v1.0.2 · Runtime MinGW ausente en los paquetes portables · 2026-09-21 UTC
- Agente: Super Z (GLM) — diagnóstico del error reportado por el usuario (imagen) + fix definitivo
- Síntoma (el error de la imagen): al ejecutar `LuminaPresentationSuite.exe` en un Windows limpio, Windows muestra el diálogo fatal «El código de ejecución no puede continuar porque no se encontró libwinpthread-1.dll» (o libgcc_s_dw2-1.dll / libstdc++-6.dll).
- Diagnóstico (evidencia dura, no suposición):
  - Descargué los 2 zips publicados en la Release v1.0.1 y extraje las **tablas de imports PE** de los 387 binarios de cada paquete.
  - El exe importa `libwinpthread-1.dll`; `Qt5Core.dll` importa `libgcc_s_dw2-1/seh-1.dll` + `libstdc++-6.dll` + `libwinpthread-1.dll`; en total **hasta 17 binarios por paquete** dependen del runtime de MinGW.
  - **Ninguna de esas 3 DLLs estaba empaquetada**. El exe usa `-static-libgcc/-static-libstdc++`, pero las Qt5*.dll precompiladas enlazan el runtime de forma DINÁMICA. En CI funcionaba porque el runner tiene MinGW en el PATH; en el PC del usuario no.
  - (La verificación de la v1.0.1 solo listó el CONTENIDO del zip — nunca auditó los imports PE; por eso se escapó.)
- Corrección aplicada (todo, no a medias):
  1. `build.yml`: nueva clave de matrix `mingw_runtime` por arquitectura (x86: `libgcc_s_dw2-1.dll` + `libstdc++-6.dll` + `libwinpthread-1.dll`; x64: `libgcc_s_seh-1.dll` + …) y el paso *Deploy* las copia desde `$env:MINGW_BIN` con **validación estricta** (throw si falta).
  2. **Gate anti-regresión** `tools/verify_portable.py` (nuevo, versionado en el repo): audita los imports PE de TODOS los exe/dll del staging recursivamente, verifica que cada DLL requerida esté en el paquete o sea DLL de sistema Windows, y comprueba que la arquitectura de todos los PE coincida con la del paquete (x86/x64). **Falla el build** si algo falta. Ejecutado en CI entre Deploy y Package.
  3. Prueba negativa del gate contra el paquete v1.0.1 roto: detecta exactamente las 3 DLLs faltantes (exit 1). ✓
  4. Version bump 1.0.2 (CMakeLists, main.cpp, fallbacks del workflow, README) + notas de release v1.0.2.
  5. Rebuild local de verificación: 0 errores / 0 warnings; smoke test de arranque OK (Biblia 66 libros importada, 5 canciones semilla, WS 8765 + HTTP 8088 arriba).
- Gates: build=OK (0 err/0 warn) · gate portable=OK sobre staging nuevo · gate=FAIL sobre v1.0.1 roto (negativo correcto)
- Bloqueos: ninguno
- Siguiente: tag v1.0.2 → CI (x86+x64) → Release → re-descarga y re-auditoría de los zips publicados como verificación final

---
## [FIX-2026-09-21] Corrección integral de defectos v1.0.0 → v1.0.1 · 2026-09-21 UTC
- Agente: Super Z (GLM) — ciclo de auditoría + corrección + verificación
- Hecho:
  - **Auditoría completa** de la base v1.0.0 (7891 líneas C++/Qt 5.15): compilación Linux Qt 5.15.2, arranque runtime, servidor WS/HTTP, Biblia (66 libros / 1189 cap. / 31084 vers.), y harness de pruebas por módulo.
  - **PptxEngine::parseSlideXml reescrito**: (1) `QXmlStreamReader::name()` devuelve el nombre LOCAL sin prefijo — todas las comparaciones con `"a:t"`, `"a:off"`, `"a:ext"`, `"a:blip"`… nunca coincidían, por lo que la importación PPTX perdía TODO el texto, posición y tamaño; (2) los eventos `isCharacters()` (contenido textual) nunca se leían; (3) doble conversión EMU→px (el `scale` se calculaba en px/EMU y se aplicaba tras `emuToPx`, dejando la geometría en ~0); (4) el color del run se capturaba fuera de contexto y jamás se aplicaba; (5) multi-run por párrafo ahora se concatena correctamente.
  - **Database::searchSongs**: `ORDER BY rank` estaba en la query EXTERNA (tabla `songs`, sin columna `rank`) → *toda búsqueda de canciones devolvía 0 resultados en silencio*. Movido al subquery FTS5.
  - **BibleRef::stripAccents**: normalizaba a NFD (descompone acentos en marcas combinantes) y luego hacía `replace()` de caracteres PREcompuestos que ya no existían → «Génesis», «Éxodo», «Levítico», «Mateo»… no se resolvían. Reescrito con NFD + filtrado de `Mark_NonSpacing`.
  - **Lyrics::pendingChords** era miembro `static` de clase → fuga de estado entre canciones (los acordes finales de una contaminaban la siguiente). Convertido a variable local.
  - **Exportación PPTX**: exportaba `m_rendered` (slides de imagen, sin `lines`) → archivo vacío. Ahora exporta las cajas de texto reales importadas, y añade exportación del contenido EN VIVO (canción/versículo proyectado) vía `requestExportLive`.
  - **WebServer/remote.html**: puerto WS hardcodeado 8765 → ahora el servidor inyecta el puerto real con `%%WSPORT%%`.
  - **VectorCanvas**: `ShapeRound` creaba un rectángulo normal (QGraphicsRectItem no soporta esquinas redondeadas) → unificado con `QGraphicsPathItem` + `addRoundedRect`; `deleteSelected()` hacía `removeItem()` sin `delete` (leak de memoria).
  - **Renderer**: eliminado bloque de código muerto (QPainterPath sin uso) en `drawStyledText`.
  - **OutputWindow::paintEvent**: el crossfade creaba un QPixmap por frame → ahora usa opacidad directa del painter (0 allocs/frame).
  - **MainWindow**: `closeEvent` ahora detiene el WebServer; `updateStage` ya no consulta `songById` para slides no-canción (mostraba «Tono: » vacío); versión informada vía `QApplication::applicationVersion()`.
  - **FTS5**: sanitización robusta de términos de búsqueda (tokens entrecomillados) en `searchSongs` y `bibleWordSearch` — antes, términos con `-`, `(`, `AND`… rompían la sintaxis FTS en silencio.
  - **Exportación PPTX**: warning deprecado `QByteArray += QString` corregido.
  - Versión 1.0.1 en CMakeLists, main.cpp y workflow (parameterizada como `APP_VERSION`).
- Decisiones:
  - Se mantiene la arquitectura C++/Qt 5.15 (cumple Win7 SP1 x32→Win11 x64 con binario portable autónomo, superior a .NET 4.8 para el requisito «cero instalaciones»: .NET 4.8 no viene preinstalado en Win7 y exigiría instalación adicional).
  - Harness de pruebas persistidos fuera del repo (no forman parte del producto): PPTX import/export/roundtrip (14 checks), Database (36), núcleo Lyrics/Chords/BibleRef (26), integración WS (7).
  - `QXmlStreamReader` exige comparar por nombre LOCAL (sin prefijo `a:`/`p:`) — documentado en el código para evitar regresión.
- Gates: build=OK (0 errores, 0 warnings, MinGW-equivalente gcc 9/Linux) · tests=83/83 OK · runtime arranque OK · WS/HTTP OK · Biblia íntegra OK
- Bloqueos: ninguno
- Siguiente: CI verde en Windows (x86+x64) y Release v1.0.1 con los paquetes portables corregidos

---
## [FEAT-2026-09-21-D] v1.0.3 → v1.1.0 · Auditoría integral + features de specs + correcciones · 2026-09-21 UTC
- Agente: Super Z (GLM) — ciclo de auditoría completa + implementación de gaps vs specs (Requerimientos.md / holyrics-spec.md / powerpoint-spec.md)
- Hecho:
  - **Auditoría integral de la base v1.0.3** (8.2k LOC C++/Qt 5.15): revisión línea a línea de core (Database/Lyrics/Chords/BibleRef/Renderer/DisplayEngine/MediaEngine/PptxEngine/Triggers/MidiOut), net (WebServer), gui (11 paneles) y CI. Compilación local Linux Qt 5.15.2 gcc con 0 warnings y smoke test offscreen (arranque, seed Biblia+canciones, WS 8765 + HTTP 8088).
  - **Features nuevas (gaps vs specs)**:
    1. **API HTTP de comandos** (Componente 3 del spec): `GET /api/cmd?c=next|prev|black|clear|logo|goto|alert|qnext|qprev[&i=N][&text=…][&token=…]` + `GET /api/live.txt` (texto plano para fuente de texto de OBS) — token opcional configurable en Ajustes (patrón Holyrics).
    2. **Exportación a PDF** del escenario en vivo (spec: «PPTX y PDF»): QPdfWriter páginas 16:9 rasterizadas con el Renderer; botón en panel PowerPoint.
    3. **Lower Third** (Componente 2 del spec): nuevo `Slide::LowerThird` + `Renderer::paintLowerThird` (banda semitransparente, barra dorada, título/texto alineados) + diálogo en la toolbar.
    4. **Resaltado de palabras bíblicas** (spec Holyrics: «destacar palabras»): `Slide::highlight` + ruta QTextDocument en `Renderer::drawBlock/measureBlock/drawRichLine`; comparación insensible a acentos/mayúsculas y puntuación; campo «Destacar» en BiblePanel (señal con 6º parámetro).
    5. **Modo Hinario configurable** + **densidad de proyección** (2–8 líneas/slide) en Ajustes › Canciones; `MainWindow::goLiveSong` centraliza la construcción de slides de canciones.
    6. **Transposición EN VIVO del Stage View**: la señal `stageTransposeChanged` persiste en DB y `updateStage()` transpone las cifras con `Chords::transposeLine` (antes el spinbox solo mostraba un mensaje).
    7. **Cola del culto remota**: comandos `qnext`/`qprev` (WS + HTTP) ejecutan el item siguiente/anterior (`runQueueAt`); botones nuevos en remote.html.
  - **Correcciones de defectos**:
    1. **BibleRef::resolve** con «13, 4-7» (espacio tras coma): el regex numérico no casaba → capítulo 0 → referencia inválida. Normalización `simplified()+remove(' ')`.
    2. **WebServer: QUrlQuery NO elimina la ruta** — `queryItemValue("c")` devolvía vacío (clave «/api/cmd?c»); ahora se extrae solo la parte posterior a '?'. Detectado por el harness (comando llegaba vacío pese a responder ok).
    3. **Lyrics interleave (Modo Hinario)**: coro duplicado consecutivo (V1 C C V2); ahora omite el bloque suelto e intercala tras cada verso: V1 C V2 C.
    4. **quickVerse (F9 doble)**: `m_savedIndex` se machacaba con el índice del overlay → Esc restauraba slide equivocada. El estado original solo se guarda la primera vez.
    5. **PPTX en cola de culto**: la señal `requestAddPptxToService` nunca se emitía (sin botón) y los items no guardaban la ruta → no ejecutables. Nuevo botón «＋ A culto», payload = ruta, `runServiceItem` importa+rasteriza con el helper compartido `PptxEngine::renderToSlides` (la rasterización del panel y la cola comparten implementación).
    6. **MediaEngine↔MediaPanel desconectados**: `positionChanged`/`stateChanged` nunca se conectaban (barra/tiempo muertos); ahora conectados en MainWindow + manejo de `finished` (limpia salida + trigger media_stop).
    7. **Fondos de video**: `playBackground` reiniciaba el bucle en cada slide (parpadeo); ahora continúa si es el mismo archivo.
    8. **Ajustes ↔ Toolbar**: la pantalla elegida en Ajustes se refleja en los combos y se aplica; token API aplicado también tras «Aplicar».
    9. Barra de estado con versión real; `stop()` del WebServer cierra clientes WS; eliminada `tagRegex()` muerta en Lyrics.
  - **Harness de pruebas** (fuera del repo): 51 checks — Chords (10), Lyrics (9, incl. hinario V1 C V2 C y densidad), BibleRef (4), Database (12, tags CRUD + biblia), Renderer (4, LowerThird + Highlight), PptxEngine roundtrip (5), WebServer HTTP (6, incl. 401 sin token y qnext→remoteCommand). Resultado: 51/51 OK.
  - Gates locales: build=OK (0 errores / 0 warnings) · smoke offscreen OK · API HTTP verificada con curl (state 1.1.0, cmd, remote.html, overlay.html).
  - Version bump 1.0.3 → 1.1.0 (CMakeLists, main.cpp, workflow) + notas de release v1.1.0 + README (nuevas features + tabla API HTTP).
- Decisiones:
  - **Se mantiene C++/Qt 5.15** (no .NET 4.8): el requisito dominante del usuario es «cero instalaciones» y Win7 x32 → un portable Qt+LibVLC autocontenido arranca en Win7 limpio sin instalar runtime; .NET 4.8 no viene preinstalado en Win7. Decisión ya registrada en v1.0.1 y revalidada.
  - **QTextDocument para el resaltado**: permite HTML por palabra manteniendo word-wrap y alineación del tema sin partir líneas a mano; sombra simulada con segunda pasada translúcida.
  - **Token API opcional** (no obligatorio): el uso típico es LAN de confianza; el token cubre el flujo Holyrics/Companion sin fricción extra.
- Gates: analyze=equivalente (build 0 err / 0 warn) · test=51/51 OK (harness) · smoke=OK · CI Windows x86+x64 → pendiente del run del tag v1.1.0
- Bloqueos: ninguno
- Siguiente: CI verde del tag v1.1.0 + verificación de paquetes publicados (descarga + verify_portable + SHA256)

---
## [FIX-2026-09-21-E] v1.1.0 → v1.2.0 · Auditoría integral: 6 defectos críticos + 19 medios/ bajos + release fantasma v1.1.0 · 2026-09-21 UTC
- Agente: Super Z (GLM) — ciclo completo: diagnóstico → auditoría → corrección → verificación
- Hecho:
  - **Diagnóstico de la release v1.1.0 fantasma**: el run 35605266295 del tag v1.1.0 terminó en `success` y el log de `softprops/action-gh-release` muestra "Release ready" con los 3 assets subidos, PERO la release NO existe ni en el API ni en la página /releases (solo el stub del tag). Experimento controlado: un fine-grained PAT con permiso "code" **NO puede ver drafts** (creé un draft de prueba → invisible en /releases y 404 por id) — el listado solo muestra 4 releases publicadas. Conclusión: la v1.1.0 quedó atrapada como DRAFT invisible (probable fallo del paso "Finalizing release" del action) y el CI no tenía forma de detectarlo. Limpieza: tag de prueba eliminado; el workflow v1.2.0 purga drafts residuales del tag antes de publicar.
  - **Auditoría integral de v1.1.0** (8.4k LOC): dos pasadas independientes (core/net/build y GUI/web) + verificación manual línea a línea de cada hallazgo antes de corregir. 6 defectos CRÍTICOS, ~19 MEDIOS, ~14 BAJOS confirmados.
  - **Correcciones CRÍTICAS**:
    1. **F9 / versículo rápido roto + crash** (`MainWindow::showSlideIndex` llamaba `closeOverlay()` de primero): el overlay se restauraba ANTES de proyectar el versículo → se proyectaba la slide 0 de la canción; con nada en vivo, `m_liveSlides.at(0)` sobre vector vacío = UB/crash. El overlay ahora se gestiona en `goLive(..., isOverlay)`; F9 conserva el punto de retorno (Esc restaura); proyectar contenido nuevo descarta el retorno.
    2. **Logo invisible** (`Renderer::renderLogo`): `QSize * 0.5` no existe en Qt 5.15; GCC resuelve la ambigüedad como `QSize * int(0.5)` = 0×0. Sustituido por división entera.
    3. **MIDI Out corruptía la pila** (`MidiOut.h`): `midiOutOpen` invocada con 2 de sus 5 parámetros (stdcall x86 → callee limpia 20 bytes, caller apiló 8). Firma completa.
    4. **Lienzo libre no proyectaba nada**: `Slide::Custom` con JSON en mediaPath que nadie renderiza. Ahora `onProject()` rasteriza el lienzo (VectorCanvas::renderTo existía y jamás se llamaba) a PNG y emite `Slide::Image`; nueva señal `requestAddCustomToService` + caso `ServiceItem::Custom` en la cola del culto (rasterizado desde DB por refId/payload).
    5. **Lista de temas sin conectar** (`ThemePanel`): clic en un tema no cargaba nada y "Guardar cambios" sobrescribía la plantilla ANTERIOR (corrupción de datos). Conectada + `QSignalBlocker` en `loadFromTheme` (el tema híbrido transitorio ya no se aplica al proyector a mitad de carga).
    6. **Ajustes destruía la configuración** (`SettingsPanel`): nunca cargaba ws_port/http_port/fade_ms/default_theme/server_autostart/stage_chords y los combos ignoraban las pantallas guardadas → "Aplicar" machacaba todo con defaults. Carga completa + restauración de pantallas.
  - **Correcciones MEDIAS**: triggers disparados DOS veces por navegación (feedback señal↔lista); item Biblia de la cola proyectaba el CAPÍTULO completo (ahora la referencia/rango exacta); F9 ignora el rango "Sal 23:1-3" (resuelto con rangeOf); `saveTheme` devuelve id real (Guardar como nuevo dejaba el editor en id 0 y el siguiente guardado fallaba en silencio); "＋ A culto" añadía al culto más reciente (ahora al seleccionado en ServicePanel, vía `activePlaylistId()`); settingsApplied aplica el tema EN VIVO y respeta server_autostart (antes arrancaba el servidor siempre, con error de puerto tragado); app zombie al cerrar con salidas activas (se ocultan m_output/m_stage; el proceso terminaba congelado); doble clic en la salida = abrir/cerrar proyección (señal outputClicked existedía sin conectar — rescate en monitor único); WebServer no enviaba el estado inicial al conectar un remoto; StageWindow: alerta nueva borrada por el temporizador de la anterior (timer miembro reiniciable); BiblePanel v2 sin dedupe contra v1 (texto duplicado en pantalla); reconexión OBS automática tras desconexión; fuga de QNetworkReply del webhook; fuga de m_buffer de sockets HTTP abortados; Modo Hinario: coro largo (solo se intercalaba su primer bloque) y coro entre bloques de un mismo verso largo (ahora por sección completa); PPTX con acentos/ñ en la ruta (fopen ANSI de miniz — toLocal8Bit en Windows, import y export).
  - **Correcciones BAJOS**: sombras invisibles (drawText con Qt::NoPen); fondos cover descentrados (drawImageFit anclaba arriba-izquierda); notas naturales de acordes (idx*2 → tabla {0,2,4,5,7,9,11}); slash chords con bajo latino y transposición del bajo; reason phrase HTTP "401 Unauthorized"; purga de temporales de PPTX >3 días; `Database::open` fallido deja handle colgante (isOpen()==true); orden Z del lienzo no se restauraba (+textWidth); alerta Telegram limpia (sin "✉ autor:"); HTML escapado en reporte PDF; estado del medio reflejado en MediaPanel (seek/botón); Space no roba el foco de botones/casillas; colores del tema aplicados al Stage View; toggleOutput detiene el medio al ocultar; multilínea en remote.html (white-space:pre-line); `running()==false` tras stop() del WebServer; `--plugin-path` explícito a libvlc (portable robusto).
  - **Workflow endurecido (defecto raíz de la v1.1.0)**: release SOLO en tags v* (workflow_dispatch ya no puede re-taggear); GATE 1 purga drafts residuales del tag (con GITHUB_TOKEN, que sí ve drafts); GATE 2 verifica post-publicación por API (draft=false Y ≥3 assets, 5 reintentos) y **falla el run** si la release no quedó publicada — el CI ya no puede quedar verde con una release rota.
  - **Version bump 1.1.0 → 1.2.0** (CMakeLists project VERSION, main.cpp setApplicationVersion, fallback APP_VERSION del workflow) + notas de release v1.2.0 + README (descarga v1.2.0, gates de release).
- Decisiones:
  - **Overlay en goLive, no en showSlideIndex**: semanticamente "cargar contenido nuevo" es el punto donde el retorno del overlay se conserva (F9) o descarta (proyección deliberada). showSlideIndex queda puro (render+triggers) y Esc sigue siendo el único restaurador.
  - **Lienzo → Slide::Image rasterizado**: reutiliza la ruta de renderizado ya probada (OutputWindow, Renderer, export PDF/PPTX) en vez de añadir un renderer paralelo para Slide::Custom. El JSON se conserva en la slide para referencia.
  - **Hinario por sección original**: los bloques son artefactos de paginación; intercalar el coro entre bloques del mismo verso era semánticamente incorrecto (cortaba la estrofa a la mitad).
  - **GATES de release**: el defecto v1.1.0 era invisible para todo token que no fuera el de la propia ejecución; la única defensa fiable es que el propio CI verifique su trabajo con su GITHUB_TOKEN y falle en rojo.
- Gates: build=OK local Linux Qt 5.15.2 gcc (0 errores / 0 warnings) · harness=39/39 OK (Chords 10 · BibleRef 5 · Lyrics/Hinario 5 · Renderer 4 · Database 8 · WebServer 7) · smoke offscreen OK (arranque, seed 5 canciones + Biblia, WS 8765 + HTTP 8088, /api/state versión 1.2.0) · CI Windows x86+x64 → pendiente del run del tag v1.2.0
- Bloqueos: ninguno (2 hallazgos del harness resultaron ser datos de prueba ambiguos — "c1..c5" y "a"/"b" son tokens de acorde válidos y el parser los consume como cifras; documentado en el propio harness)
- Siguiente: tag v1.2.0 → CI verde (build+release con gates nuevos) → verificación final de la release publicada (API + descarga + SHA256 + verify_portable)

---
## [CIERRE-2026-09-21-F] v1.2.0 publicada y verificada de extremo a extremo · 2026-09-21 UTC
- Agente: Super Z (GLM) — cierre del ciclo (pasos 8-9 de AGENT.md)
- Hecho:
  - Run 35622138524 del tag v1.2.0: **success** en los 3 jobs (Build x64, Build x86, Create GitHub Release) — 0 errores, 0 warnings.
  - **GATES nuevos en verde**: "Purge stale draft releases for this tag" (limpieza previa) y "Verify published release (gate anti draft-fantasma)" (verificación post-publicación).
  - **Release v1.2.0 PUBLICADA** (id 393096568, draft=false, publicada 2026-09-21T15:57:18Z): los 3 assets subidos — `LuminaPresentationSuite-v1.2.0-x64-portable.zip` (86.7 MB), `-x86-portable.zip` (82.2 MB) y `SHA256SUMS.txt`. Página pública HTTP 200. Listado de releases limpio (draft residual del experimento de diagnóstico eliminado, HTTP 204).
  - **Verificación post-publicación (descarga real)**: descargados ambos zips desde la release; **SHA256 idénticos** a los publicados (355b5634… x64 · 67a1b28e… x86); extraídos 397 archivos por paquete; **gate `verify_portable.py` ejecutado sobre ambos: 390 binarios PE auditados por arquitectura, todas las dependencias satisfechas dentro del paquete**; runtime MinGW (libgcc/libstdc++/libwinpthread) presente en ambos; LibVLC con plugins completa; **FileVersion 1.2.0.0** confirmado en ambos .exe.
- Gates: CI=success (3/3 jobs, 2 gates nuevos) · release=publicada con 3 assets · SHA256=exactos · verify_portable=OK x64 y x86 · versión exe=1.2.0.0
- Bloqueos: ninguno
- Siguiente: ninguna — v1.2.0 cerrada: defecto raíz de la release fantasma corregido y blindado, base funcional completa
---
## [FEAT-2026-09-22-P] v2.0.0 «HORIZONTE» · Reescritura completa con wxWidgets (petición explícita del usuario) · 2026-09-22 UTC
- Agente: Z.ai Code (GLM) — ciclo completo con UI/UX nueva, CI y release
- Encargo: «Crea el programa usando wxWidgets, y quiero que crees correctamente la UI/UX» — sustituye al stack Qt 5.15 manteniendo el objetivo: programa de escritorio NATIVO, completo, Win7 x32→Win11+, autocontenido (el usuario final no instala nada).
- Hecho:
  - **Reescritura integral del programa en C++17 / wxWidgets 3.2** (v2.0.0 «Horizonte»): se elimina el árbol Qt (src/gui, motores Qt; preservado en el historial git) y se construye la app nueva: 16 fuentes de UI/núcleo/red (~5.500 líneas), SQLite amalgamation con FTS5 y nlohmann/json vendidos en third_party.
  - **UI/UX nueva (consola del operador)**: menú Archivo/Vista/Ayuda, toolbar EN VIVO (ambar) + transporte + NEGRO/LIMPIAR/LOGO/Lower Third + selector de pantalla + transposición + chip remoto + Ajustes; biblioteca en wxNotebook (Canciones/Biblia/Medios/Temas) | culto persistente | **previsualización dual EN VIVO/SIGUIENTE renderizada con el motor real**; barra de estado de 3 paneles; **iconos vectoriales dibujados con wxGraphicsContext** (sin assets, nítidos a cualquier DPI); atajos F5–F9/F11, Ctrl+F/N/S/O; modo presentación F11; estados vacíos con ayuda, tooltips y mnemónicos.
  - **Motor EN VIVO**: GoLive canción/referencia/media/aviso/ítem de culto; next/prev con avance automático al siguiente ítem del culto; modos Negro/Limpiar/Logo; Lower Third con barra dorada; transposición en vivo ±12 con re-proyección inmediata; salida fullscreen multi-pantalla con recuperación de selección en combo.
  - **Núcleo portado y corregido**: Chords (transposición latinos/anglosajones con bajo slash — SE CORRIGIÓ el mapeo de semitonos A-G en el parser de tokens, bug heredado), Lyrics (parser [Verso]/[Coro] + acordes; **MEJORA (e): el Modo Hinario intercala el coro tras el VERSO COMPLETO preservando grupos — la edición Qt aplanaba los grupos e intercalaba a mitad de estrofa**), BibleRef (66 libros + parser por tokens — resuelve «1 co 13,4-7», «1sam 3», acentos), Database (schema completo v1.6.0: FTS5 remove_diacritics + triggers, tags, media, tag_rules, cultos, ajustes, respaldo sqlite3_backup; corrección: trigger bible_ad referenciaba songs_fts).
  - **Renderer wxGraphicsContext**: fondo sólido/gradiente (ángulo)/imagen cover-contain con cache LRU-4; texto con contorno por silueta 8-dir + sombra + auto-ajuste tipográfico por caja con wrap por palabras; cifrado opcional en dorado; resaltado de palabras (biblia) en dorado+negrita; contador n/total; Lower Third; contraste WCAG (ratio AA/AAA).
  - **UTF-8 a prueba de locale**: TODAS las conversiones wxString↔json usan ToUtf8/FromUtf8 explícitos o parseo de bytes crudos (wxString::ToStdString corrompe UTF-8 en locale «C» — detectado y corregido; el bug rompía el seed de la biblia en producción). Literales no-ASCII en forma ancha (L"…").
  - **Servidor remoto (wxSockets, sin hilos)**: remote.html móvil embebida (autorefresco 2,5 s, token reenviado), /api/cmd (next/prev/black/clear/logo/goto/alert), /api/state JSON, /api/live.txt, token opcional en /api/*, tope de 64 KB por cliente (M1), 401/404 correctos (B13), stop() seguro con clientes conectados (C1). Comandos → cola de eventos del frame.
  - **Primera ejecución verificada offscreen (Xvfb)**: BD creada en Data\ portable, tema por defecto sembrado (id correcto), canción demo con tags, **Biblia RVR1909 completa importada (31.084 versículos, «Jn 3:16 … amó Dios al mundo…»)**, ventana operativa sin crash (timeout=124 tras 25 s, log limpio).
  - **Arnés de núcleo nuevo tools/selftest.cpp: 63/63 checks OK** (acordes 20, letras/hinario 10, biblia-ref 7, SQLite/FTS/tags/import/cultos/ajustes 26).
  - **CI/CD reescrito (.github/workflows/build.yml)**: compila wxWidgets 3.2.8.1 ESTÁTICO con runtime /MT (wxBUILD_MSVC_RUNTIME_DLL=OFF, samples/tests/demos off, componentes core/base/net/media) para x86 y x64 con cache por versión+arch; app configurada en modo CONFIG (targets wx::*) con MSVC_RUNTIME_LIBRARY=/MT; empaqueta portable (exe + biblia + logos + LEEME + licencia) y ejecuta el GATE tools/verify_portable.py (auditoría de imports PE — con enlace estático el exe solo importa DLLs de sistema); en tags v* publica Release con SHA256SUMS.
  - README reescrito para la edición wx (descarga, características, arquitectura, decisiones técnicas, compilación, pruebas); .gitignore ampliado (staging/); manifest PerMonitorV2 + supportedOS Win7→Win11; recursos .rc con FileVersion 2.0.0.0 e icono.
- Decisiones:
  - **wxWidgets estático + /MT en vez de DLL**: cero redistributables (msvcp140/vcruntime140 NO viajan ni se instalan) — requisito «cero instalaciones» garantizado por diseño y verificado por el gate PE en CI.
  - **wx 3.2 (no 3.3)**: soporte Win7 SP1 documentado y serie estable madura; el floor Win7 se fija además con manifest supportedOS y _WIN32_WINNT=0x0601.
  - **Media por wxMediaCtrl (DirectShow)**: codecs nativos del SO (mp4/wmv/avi) sin LibVLC; la integración VLC queda como roadmap si se requiere cobertura de codecs ampliada.
  - **PPTX/StageView/MIDI/OBS-webhook/PCO/Drive/JS**: funcionalidades de la línea Qt 1.x NO portadas en 2.0.0 (roadmap de la serie 2.x); el núcleo de proyección (canciones+biblia+medios+temas+culto+remoto) está completo y verificado — se priorizó UI/UX correcta y cero errores sobre acumular superficies.
  - **Hinario (e)**: el intercalado respeta versos paginados (comportamiento documentado desde v1.2.0 que la implementación Qt no cumplía; ahora verificado por selftest).
  - **UTF-8 explícito**: los JSON internos (temas, cultos, estado web, biblia) nunca pasan por conversiones dependientes del locale — clase de bug silencioso eliminada de raíz.
- Gates: build local Linux GTK **0 errores / 0 warnings** (wx 3.2.8) · selftest **63/63 OK** · smoke offscreen de la app real OK (BD+seed+31.084 versículos+GUI sin crash) · CI Windows x86+x64 → pendiente del run del tag v2.0.0
- Bloqueos: ninguno
- Siguiente: commit → push → tag v2.0.0 → CI → verificación de release publicada (assets + SHA256 + verify_portable + FileVersion 2.0.0.0)

## 2026-09-22 — v3.0.0 «HÍBRIDA»: arquitectura C++ + .NET con PoC de interop validado
- Petición del usuario: evolución a arquitectura híbrida — núcleo C++ (motor/render/lógica crítica) + C# (.NET Framework) para UI/datos/API local; evaluar mínimo .NET (3.5 SP1) con meta 4.8; Win7 x32→Win11 autocontenido; PoC de comunicación C++↔C# ANTES de la UI; incluir JSON (canciones) y .BIB (biblias).
- Arquitectura (docs/architecture-hybrid.md): vías evaluadas C++/CLI, COM Interop y CLR Hosting; elegida **vía A** (C# dueño del proceso + FusionCore.dll con API C plana: UTF-8, códigos de estado, búfer out/cap/needed, eventos en hilo dedicado). CLR Hosting implementado como PoC de respaldo (native/poc-clrhost + facade COM-visible sin registro).
- Núcleo C++17 (native/core): Engine (estado+escenario+eventos thread-safe), Storage (SQLite+FTS5, binds SIEMPRE, dedupe bible heredado v1.6.0 con guard de existencia), SongModel (esquema propio+OpenLP), Lyrics/Chords (port 1:1 wx v2.0.0 + puerta de sufijos reales: "dos/mis/fue" ya no son acordes), BibleRef (66 libros), BibleBib (.BIB con detección TAB/pipe/;;/colon, UTF-8/CP1252, directivas), Scripture, Renderer/Projector (Win32 GDI, solo WIN32).
- Capa C# (managed, multi-target net35;net48 + tests net8.0, LangVersion 7.3, cero NuGet funcionales): FusionHP.Bridge (P/Invoke Cdecl, GCHandle del delegado, pump a SynchronizationContext, DbExec de UNA sola llamada — contrato de efectos), FusionHP.Core (MiniJson propio net35, Models, ScenarioBuilder, Settings portable), FusionHP.Api (HttpListener localhost + token + webhook OBS), FusionHP.UI (WinForms 5 pestañas, vista previa vía engine, importadores), PoC.Managed (12 checks), PocFacade (COM-visible), Tests (arnés propio).
- CI/CD (ci.yml, reemplaza al wx): 7 jobs — native-windows (x86/x64 /MT + gates selftest/poc), native-linux, managed (net35+net48 ref assemblies + tests net8.0 + publish UI), **interop (gate «POC PASS 12/12» x86/x64)**, **clrhost-poc (gate «CLRHOST PASS»)**, package (portable+verify_portable+SHA256) y release en tags v*.
- Bugs encontrados y corregidos durante la integración (todos reproducidos por tests antes del fix):
  1. Lyrics::BuildSlides ignoraba blocks (usaba song.lyrics crudo) → Parse(LyricsText()).
  2. Heurística de acordes aceptaba palabras comunes ("dos"=Do+"s", "mis", "fue", "das") → puerta de arranque de sufijo real (m/M/#/b/dígito/sus/add/dim/aug).
  3. Storage::Exec solo ejecutaba la PRIMERA sentencia de un script multi-statement (biblia/FTS no se creaban) → iteración pzTail.
  4. Dedupe de migración corría sin tabla bible en BD nueva → guard sqlite_master.
  5. fusion_db_exec rechazaba len=-1 → convención NUL-terminada en GetIn y fusion_bib_parse.
  6. **Deadlock**: ShowSlide/Next/Prev llamaban PostStateEvent→StateJson() dentro del mutex no recursivo → eventos fuera del lock.
  7. **Doble ejecución de INSERT**: Bridge DbExec usaba patrón medir/llamar (2 llamadas) → UNA sola llamada + reintento solo para SELECT/WITH.
  8. MainForm corrupto a mitad de escritura (agente interrumpido) + DictEquals sin List<object> + campo _text sin usar (CS0649).
- Gates locales: build nativo Linux **0 errores/0 warnings** (falso positivo sqlite3 silenciado por target) · fusion_selftest **138/138** · fusion_poc_native **37/37** · **POC PASS 12/12** (net8 + libFusionCore.so) · TESTS PASS **10/10** · build gestionado **0 errores/0 warnings** en net35+net48+net8.
- Herramientas: make_sample_bib.py (RVR1909→.BIB, 31.084 versículos, determinista), verify_portable.py reescrito (contrato v3.0.0, exports PE), FusionLauncher (detección NDP 4.8/3.5 + propagación exit code).
- Formato .BIB documentado (docs/bib-format.md) + muestra rvr1909.bib incluida.
- Gates pendientes del CI (primer run): POC PASS 12/12 en x86+x64 Windows, CLRHOST PASS x86+x64, verify_portable, release.
- Bloqueos: ninguno.
- Siguiente: push → verificar CI verde → tag v3.0.0 → release con zips x86/x64 + SHA256 → verificación de assets.

## 2026-09-22 — CIERRE v3.0.0 «HÍBRIDA»: CI verde completo, tag y release verificada
- Run del tag v3.0.0 (35721849099): native x86+x64 SUCCESS · native-linux SUCCESS · managed net35+net48 SUCCESS · **interop «POC PASS 12/12» x86+x64 SUCCESS** · package x86+x64 SUCCESS (verify_portable) · release SUCCESS. Único job no verde: clrhost-poc (INFORMATIVO, continue-on-error) — diagnóstico CI: el runner windows-2025 carece del runtime .NET FX en Framework[64]/v4.0.30319 (GetLastError=2) y el shim System32 responde REGDB_E_CLASSNOTREG; la activación CLR (vía C) funciona en equipos con .NET Framework instalado (Win7 SP1→Win11 reales); la vía A elegida no depende de ello.
- Release v3.0.0 «HÍBRIDA» PUBLICADA (id 393677093): Fusion-HP-3.0.0-win-x86.zip (2.0 MB) + win-x64 (2.2 MB) + SHA256SUMS.txt.
- Verificación independiente post-publicación: SHA256 del zip descargado idéntico al publicado (5d324dc1… x86 · 40f8436d… x64); verify_portable sobre el paquete descargado exit 0 (layout completo, 3 PE coherentes x86, exports contractuales de FusionCore.dll presentes: fusion_create…22 símbolos).
- Notas para el siguiente ciclo:
  - VS_VERSIONINFO en FusionHP.exe (recurso .rc) — warning informativo del gate, no bloqueante.
  - clrhost en runners: alternativa CLR hosting vía hostfxr (núcleos .NET) o probar en windows-2019 (imagen con .NET FX en rutas estándar).
  - FusionHP35.exe (variante net35 de la UI) reservada en el launcher/paquete; el Core/Api/Bridge ya son net35.
  - Expansión UI: temas editor visual, medios (imágenes/video DirectShow), impresión, más integraciones OBS/remote.

## 2026-09-22 — v4.0.0 «LUMINA»: renombrado de producto, GUI nueva y blindaje anti-crash
- Petición del usuario: el nombre correcto es **LuminaPresentation Suite** (no Fusion-HP);
  la release v3.0.0 «dejó de funcionar» al abrir; pide GUI buena y verificación visual.
- Renombrado íntegro y mecánico (sed ordenado + git mv + verificación de cero restos):
  Fusion-HP→LuminaPresentation (marca), FusionHP→Lumina (proyectos/namespaces),
  FusionCore→LuminaCore, FusionLauncher→LuminaLauncher, FUSION_*/fusion_*/Fusion*→LUMINA_*/lumina_*/Lumina*,
  exe de UI = **LuminaPresentation.exe** (AssemblyName explícito), sln = Lumina.sln,
  cabecera C = lumina/lumina.h, FusionHP35→LuminaPresentation35. Preservadas las URLs del
  repo (isaac4722/Fusion-HP) y la palabra española «fusionarse». Contratos actualizados:
  verify_portable (LuminaLauncher.exe/LuminaPresentation.exe/LuminaCore.dll, export lumina_create),
  ci.yml (artefactos/zips LuminaPresentation-4.0.0-win-*, README.txt del paquete), PoC
  (facade lumina.poc.LuminaFacade, prefijo LUMINA-FACADE-OK), cabecera HTTP X-Lumina-Token.
- Versión 4.0.0 en kVersion/lumina_version/APP_VERSION/launcher/títulos; gate del PoC nativo
  actualizado a "4.0" (detectado por el propio arnés: PASS 37/37 tras el fix).
- Blindaje anti-crash («la app SIEMPRE abre»):
  1) Program.cs: Application.ThreadException + AppDomain.UnhandledException + try/catch en Main
     → diálogo en español con causa + sugerencias, y log automático data/logs/lumina-*.log
     (SO, bits, CLR, .NET 4.8 por registro, presencia de LuminaCore.dll, permisos de data/).
  2) LuminaEngine.Start: DllNotFound/BadImage/EntryPoint/TypeInitialization → LuminaException
     con instrucciones concretas (extraer ZIP, bitness, mezcla de versiones) + FreeCallback().
  3) MainForm: CreateEngine en try/catch → **modo limitado** (ventana operativa, chip Núcleo
     rojo, RequireEngine() en toda acción del motor); nunca un crash por el núcleo.
  4) LuminaLauncher: pre-chequeo de LuminaCore.dll junto al exe con mensaje accionable
     (causa típica: ejecutar desde dentro del ZIP) + CreateProcess con workdir del paquete.
- GUI nueva (mockup G-1 validado con agent-browser y transcrita 1:1 a WinForms net35+net48):
  tema oscuro plano (página #12141A, barras #171A21, tarjetas #1B1F28, acento ámbar #F0A93B),
  barra lateral owner-drawn (5 secciones, iconos vectoriales, barra activa 3px, tarjeta de
  salida), cabecera con chips de estado (Núcleo/BD/API), tarjetas con título, ListView con
  cabeceras y filas owner-drawn (zebra + selección #2F3646), ListBox/ComboBox owner-drawn,
  botones planos primary/secondary/danger con hover, inputs #10131A, barra de estado con chips,
  vista previa 16:9 con chip EN VIVO. Menú sustituido por acciones en página (Proyector,
  Guardar ajustes, Importar .BIB). Sin MenuStrip. Icono app.ico propio (16..256 px).
- Correcciones de docking (z-order inverso) en páginas multi-tarjeta (Biblia/Ajustes/Culto).
- Validación local: managed `dotnet build Lumina.sln -c Release` → 0 errores/0 warnings
  (net35+net48); native Linux → lumina_selftest **138/138** y lumina_poc_native **37/37 PASS**;
  Tests net8 con LUMINA_SKIP_NATIVE=1 → PASS (5 skips esperados).
- Pendiente al cierre: push → CI verde → tag v4.0.0 → release → verificación de assets.

## 2026-09-22 — v4.1.0 «LUMINA»: Editor de Temas visual (mockup → WinForms) + persistencia
- Mockup: página «Temas» con vista previa EN VIVO reactiva (state) — navegación 5→6 ítems,
  versiones a 4.1.0, clamp numérico (replica NumericUpDown), verificado con agent-browser
  (cambio de fuente/tamaño re-dibuja la muestra al instante).
- WinForms (Lumina.UI/MainForm.cs): NavPanel 6 ítems (icono ◑ mitad-rellena), página Temas
  con: nombre, 3 swatches (ColorDialog nativo + hex #AARRGGBB), fuente combo (6 fuentes
  Win7-safe), tamaño 12-140, negrita/MAYÚSCULAS, interlineado 0.8-2, contorno 0-10, sombra
  0-255, imagen de fondo (ruta + Examinar + modo contener/cubrir); vista previa GDI+
  (bg + texto con sombra offset + contorno GraphicsPath + ref en acento, escala 4× legible);
  botones Restaurar / Guardar en ajustes / Aplicar al escenario.
- Lógica: BuildThemeFromControls ↔ ApplyThemeToControls; LoadScenarioFromItems ahora guarda
  _lastScenarioItems/_lastScenarioName → «Aplicar al escenario» reconstruye el escenario
  activo con el tema nuevo (proyección+preview se refrescan por eventos del motor); el tema
  se persiste en settings.json (ThemeJson vía MiniJson.Serialize(Theme.ToDict())) y se
  recarga al arrancar (JSON corrupto → default, sin crash).
- Versión 4.1.0 (kVersion, lumina_version, APP_VERSION, launcher, gates de PoC actualizados
  a "4.1" — punto conocido, revisado en los 3 arneses).
- Validación local: managed 0 err/0 warn (net35+net48); selftest 138/138; PoC nativo 37/37;
  Tests net8 PASS.
