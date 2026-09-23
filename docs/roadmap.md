# Hoja de ruta — diferido y evolución (post v5.1.0 «FUNDAMENTO»)

> **v6.0.0 «HORIZONTE» (2026-09-24)**: los ítems factibles de esta hoja ya
> están IMPLEMENTADOS y verificados — resaltado en PROYECCIÓN, transiciones
> (fundido), OSIS XML, importación PPTX y notas persistentes del director.
> Quedan pendientes solo los que requieren decisiones/credenciales de
> terceros (OAuth de PCO/Drive, firma de código) y los marcados abajo.

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

* **OSIS XML** (biblias): ✅ **CUBIERTO en v6.0.0** — `managed/Lumina.Core/Bible/OsisBible.cs`
  (streaming, tabla de códigos OSIS 1..66, `<w>` aplanado, `<note>` descartado).
* **Resaltado en PROYECCIÓN** (spec §3.2): ✅ **CUBIERTO en v6.0.0** — el ítem
  de escenario lleva `highlight` (término de la búsqueda), el motor lo
  propaga a las slides y el Renderer pinta los matches en color de acento
  (módulo portable `Highlight` con coincidencia case/acento-insensible y
  frontera de palabra; contrato de slide evolucionado de forma aditiva).
* **Importación PPTX** (hoy solo exportación): ✅ **CUBIERTO en v6.0.0** —
  `managed/Lumina.Core/Import/` (ZipReader + PptxImporter): orden real por
  `sldIdLst`→`rels`, `sp`/`txBody`/`a:p`/`a:t` mapeados a ítems de texto
  (1 diapositiva = 1 ítem), lector ZIP/OPC sin dependencias del GAC.

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
* **Transiciones entre slides** (fade): ✅ **CUBIERTO en v6.0.0** — crossfade
  `AlphaBlend` en `Projector.cpp` desde el último fotograma cacheado (cache
  `lastFrame`), animación ~60 fps impulsada por el bucle de ventana, ajuste
  persistente (Ajustes › Proyección) y API `lumina_set_transition`.
  Pendiente: verificación visual en GUI real (los gates cubren ABI/estado).

## Tercera salida (spec §3.3 «hasta tres salidas»)

**CUBIERTA en v5.1.0**: pantalla pública (nativa) + monitor de escenario
(StageViewForm) + **pantalla del director** (`DirectorForm`, ver
`docs/api/DirectorWindow.md`). ✅ **Notas persistentes por ítem CUBIERTAS en
v6.0.0**: `ScenarioItem.Notes` viaja en el plan JSON («Guardar plan JSON…»
en la página Culto) y el panel del director se carga/guarda por ítem.

## Infra

* **clrhost en runners CI**: probar `windows-2019` (imagen con .NET FX en
  rutas estándar) para volver a gatear la vía C — hoy sigue INFORMATIVO.
* **Firma de código** del exe/dll (evita SmartScreen en Win10/11):
  requiere certificado del propietario.
