# Exportación de escenarios — PPTX y PDF (v5.0.0 «SINERGIA»)

> Requisito del spec (§3.2 / §4): «El Editor de Escenarios debe incluir una
> función para exportar un Escenario completo a formatos PPTX y PDF».
> Implementación **sin dependencias externas** para el usuario final.

## Dónde

Página **«Exportar»** de la interfaz (navegación lateral). Exporta el
**escenario activo** (el último cargado en «En Vivo») aplicando el **tema
actual** (colores, tipografía, MAYÚSCULAS, interlineado, acentos).

## PPTX — PresentationML ISO/IEC-29500 real

* Escritura con **System.IO.Packaging** (OPC): en .NET Framework viene de
  **WindowsBase.dll**, integrado en Windows 7+ → **cero despliegue**.
  En el arnés de tests (net8.0) se usa el paquete `System.IO.Packaging`
  (solo desarrollo/CI: **nunca viaja en los binarios distribuidos**).
* El `[Content_Types].xml` lo **auto-genera el API OPC** a partir de los
  content types REALES declarados en cada parte (Default por extensión +
  Override por parte) — no se escribe a mano (escribirlo duplica la entrada).
* Estructura mínima VÁLIDA: `presentation.xml` (+rels), 1 slideMaster (+rels),
  1 slideLayout blank (+rels), theme1.xml, `slides/slideN.xml` (+rels),
  `docProps/core.xml` y `app.xml` (metadatos §4.1), `media/imageN.{png|jpeg}`.
* **Unidades del estándar** (spec §4.2): lienzo 16:9 = `12192000×6858000`
  EMU (1 in = 914400 EMU); tipografía en **centésimas de punto**
  (`sz="2700"` = 27 pt = 54 px@1080p ÷ 2).
* Tema 1:1: fondo (color o imagen a pantalla completa), fuente/tamaño/negrita,
  interlineado (`a:lnSpc spcPct`), color de texto y **acento** para la
  referencia bíblica y rótulos de bloque. Título grande en slides TÍTULO,
  referencia al pie en ESCRITURA (alineada a la derecha, como el motor).
* Imágenes: PNG/JPEG detectadas por firma e incrustadas en `ppt/media/`
  (ajustadas al área útil con margen de 0.5 in).

### Validación (CI)

`tools/validate_pptx.py` abre el archivo generado por el propio arnés con
**python-pptx** y comprueba: nº de diapositivas, tamaño exacto en EMU y el
texto real (incluye acentos `amó`). Si python-pptx no puede abrirlo, el CI
falla — es la prueba más cercana a «PowerPoint lo abre» sin tener PowerPoint.

## PDF — escritor 1.4 propio (cero dependencias)

Implementación íntegra en `Lumina.Core/Export/PdfExporter.cs`:

* **Fuentes base-14** Helvetica / Helvetica-Bold con `/WinAnsiEncoding` — no
  se incrustan: PDF pequeños, válidos en cualquier visor de Win7→Win11.
* **Métricas AFM** reales (tablas en `PdfFont.cs`) para centrado, envoltura
  por palabras y auto-reducción de cuerpo hasta caber.
* Texto WinAnsi: `é í ó ú ñ ¿ ¡ « »` perfectos (mapeo cp1252 propio);
  escritura **Latin-1 byte a byte** (UTF-8 corrompería acentos y /Length).
* **Sombra** del tema emulada con texto desplazado en gris oscuro.
* Imágenes: **JPEG → DCTDecode directo** (bytes tal cual, dimensiones
  parseadas del marcador SOF); PNG/BMP → RGB crudo con **FlateDecode**
  (zlib: cabecera 0x78 0x01 + DeflateStream + **Adler32** final).
* Página 960×540 pt (16:9), xref con offsets verificados por el arnés
  (todos los `N 0 obj` apuntan exacto), `/Producer` y `/Title`.

### Validación (CI)

El mismo `tools/validate_pptx.py` abre el PDF con **pypdf**: nº de páginas,
MediaBox y texto extraído (acentos incluidos).

## Límites conocidos

* El PPTX es texto+imágenes (las slides de VIDEO se exportan como título
  «Video: …» — el video no se incrusta en el PPTX).
* El PDF no incrusta la fuente del tema: siempre Helvetica (base-14);
  el tamaño/negrita/interlineado del tema sí se respetan.
