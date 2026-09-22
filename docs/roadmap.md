# Hoja de ruta — diferido y evolución (post v5.0.0 «SINERGIA»)

Criterio del proyecto: **cero errores y funciones completas antes que
superficie**. Lo que sigue quedó fuera de v5.0.0 por requerir decisiones de
terceros o infraestructura de despliegue que romperían «el usuario no instala
nada / no configura nada». Se documenta el plan técnico de cada punto.

## Requiere credenciales de aplicaciones registradas (decisión del propietario)

* **Planning Center Online** (spec §3.3): importar planes y listas de
  canciones. Plan: OAuth 2.0 + PKCE (la API REST de PCO), cache local en
  `data\pco\`, mapeo `item.plan → ScenarioItem` (canción por título contra
  la biblioteca FTS5). **Bloqueado por**: requiere crear la app en PCO
  (client_id propio del propietario del repo).
* **Google Drive** (spec §3.4): respaldo automático de canciones/biblias/
  ajustes. Plan: OAuth de escritura limitada a una carpeta de app
  (`drive.file` scope), empaque zip del directorio `data\`. **Bloqueado por**:
  registrar el proyecto en Google Cloud (client_id/secret).

  Alternativa inmediata y documentada: apuntar el respaldo a una carpeta
  local sincronizada por el cliente de Drive que el usuario ya tenga.

## Extensiones de formato (candidatos a v5.x)

* **OSIS XML** (biblias): segundo formato académico junto a ZEFania.
* **Resaltado de palabras en Biblia** (spec §3.2): requiere marcar tokens en
  `SlideLine` y soportar énfasis en el Renderer nativo (Win32+GDI: run de
  texto con color de acento). Cambio de ABI controlado (campo nuevo en el
  JSON de slide, versión menor del contrato).
* **Importación PPTX** (hoy solo exportación): leer PresentationML con el
  mismo OPC y mapear `sp` de texto a ítems de texto.

## Motor de scripts (JSLib del spec §3.3)

* Evaluación con **IActiveScript/JScript** del propio Windows (ES3, cero
  despliegue) o Lua embebido (abriría binario nativo adicional — a valorar).
* Entregar `JSLib`-like mínimo: `tcp(host, port, onLine)`, `ws(url, onMessage)`,
  `httpGet(url)`, `cmd(action)`, `showText(text)`.
* Prioridad actual: los **activadores** cubren los flujos de automatización
  del spec sin código de usuario; el scripting queda para usuarios avanzados.

## Multimedia

* **Cobertura de codecs ampliada** (mkv/webm): hoy WMP/DirectShow cubre
  mp4/wmv/avi con los codecs del SO. Si se exige mkv/webm → VLC headless
  (~60 MB por arquitectura) rompe el espíritu «ligero»; alternativa:
  documentar conversión o aceptar el coste en v6 con paquete «full» opcional.
* **Transiciones entre slides** (fade): el Renderer nativo con doble búfer
  ya lo permite (alpha blend entre buffers) — cambio acotado en Projector.cpp.

## Tercera salida (spec §3.3 «hasta tres salidas»)

Pantalla pública (nativa) + monitor de escenario (v5.0.0) ✓ + **pantalla de
instrucciones/notas para el director** (HTML local o tercera ventana WinForms
con el texto del ítem actual + notas) — trivial sobre StageViewForm.

## Infra

* **clrhost en runners CI**: probar `windows-2019` (imagen con .NET FX en
  rutas estándar) para volver a gatear la vía C — hoy sigue INFORMATIVO.
* **Firma de código** del exe/dll (evita SmartScreen en Win10/11):
  requiere certificado del propietario.
