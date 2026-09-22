# Hoja de ruta — diferido y evolución (post v5.1.0 «FUNDAMENTO»)

Criterio del proyecto: **cero errores y funciones completas antes que
superficie**. Lo que sigue quedó fuera de v5.1.0 por requerir decisiones de
terceros o infraestructura de despliegue que romperían «el usuario no instala
nada / no configura nada». Se documenta el plan técnico de cada punto.

> Lo que el spec pedía y YA está cubierto en v5.1.0: teclado en vivo,
> resaltado de búsqueda bíblica, tercera pantalla del director, editor de
> culto completo, respaldo a carpeta Drive-compatible e importación de
> planes JSON (PCO por archivo). Ver [README](../README.md).

## Requiere credenciales de aplicaciones registradas (decisión del propietario)

* **Planning Center Online** (spec §3.3): la importación de planes está
  cubierta con el **formato JSON documentado** (`docs/api/PlanningCenter.md`).
  La descarga directa por OAuth 2.0 + PKCE queda bloqueada por: requiere crear
  la app en PCO (client_id propio del propietario del repo).
* **Google Drive** (spec §3.4): el **respaldo a carpeta sincronizada** está
  cubierto (`docs/api/DriveBackup.md`, motor ZIP propio). La subida directa
  (OAuth `drive.file`) queda bloqueada por: registrar el proyecto en Google
  Cloud (client_id/secret).

## Extensiones de formato (candidatos a v5.x)

* **OSIS XML** (biblias): segundo formato académico junto a ZEFania.
* **Resaltado en PROYECCIÓN** (spec §3.2): el resaltado de la búsqueda ya está
  en la lista de resultados (v5.1.0); llevarlo a pantalla requiere marcar
  tokens en `SlideLine` y soportar énfasis en el Renderer nativo (run de texto
  con color de acento). Cambio de ABI controlado (campo nuevo en el JSON de
  slide, versión menor del contrato).
* **Importación PPTX** (hoy solo exportación): leer PresentationML con el
  mismo motor OPC/ZipWriter propio y mapear `sp` de texto a ítems de texto.

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
  ya garantiza «cero destello del escritorio» (requisito del spec cubierto);
  el fundido animado entre slides es un cambio acotado en Projector.cpp
  (alpha blend entre buffers) pendiente de pruebas en GUI real.

## Tercera salida (spec §3.3 «hasta tres salidas»)

**CUBIERTA en v5.1.0**: pantalla pública (nativa) + monitor de escenario
(StageViewForm) + **pantalla del director** (`DirectorForm`, ver
`docs/api/DirectorWindow.md`). Pendiente menor: notas persistentes por ítem.

## Infra

* **clrhost en runners CI**: probar `windows-2019` (imagen con .NET FX en
  rutas estándar) para volver a gatear la vía C — hoy sigue INFORMATIVO.
* **Firma de código** del exe/dll (evita SmartScreen en Win10/11):
  requiere certificado del propietario.
