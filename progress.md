# Bitácora de turnos — LuminaPresentation Suite

> Registro acumulativo de trabajo (append-only). Formato definido en `AGENT.md`.

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
