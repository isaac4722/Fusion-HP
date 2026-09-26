---
name: openxml-pptx
description: Importación y exportación PPTX conforme a OPC/OpenXML (ISO/IEC-29500) con herencia de 4 niveles, unidades EMU, centésimas de punto y mitigación XXE. Usar siempre que se toque el importador o exportador PPTX.
---

# openxml-pptx — OPC/PresentationML para el MVP [SPEC §9.2, §9.3]

## Orden normativo de recorrido (importación)

`[Content_Types].xml` → `_rels/` → `docProps/` → `ppt/presentation.xml`
(orden de diapositivas) → `slides/` → `slideLayouts/` → `slideMasters/` →
`theme/` → `media/`. Nunca otro orden.

## Herencia de 4 niveles

Cada propiedad visual se resuelve: Diapositiva → Diseño → Maestro → Tema.
Los placeholders del diseño fijan la semántica (título, subtítulo, contenido,
texto, fecha, número, pie).

## Unidades

- EMU: `914400 EMU = 1 pulgada`; convertir coordenadas/dimensiones al lienzo propio.
- Fuente: `sz="3200"` → 32 pt (centésimas de punto).
- Probar coordenadas negativas, cero y extremas.

## Seguridad obligatoria

- Entidades externas deshabilitadas; resolución XML nula; DTD bloqueado.
- Límite de expansión de entidades (protección billion laughs).
- Límite de tamaño y de entradas del ZIP (ZIP bombs).
- `.pptm` se importa SIN ejecutar macros; detectar `vbaProject.bin` y avisar.
- Namespaces desconocidos (`p14`, `pc2`…) se IGNORAN, nunca abortan.

## Mapeo al modelo ahp.v1

Diapositiva→Escenario · caja de texto→Texto Formateado · imagen→Imagen ·
video incrustado→Video · fondo de diseño→fondo del Escenario ·
tabla/gráfico/animación→omitido CON informe.

## Exportación

- OPC completo: `presentation.xml`, `slides/`, layouts/masters mínimos,
  `theme/` derivado del tema ahp.v1, `media/` empaquetada.
- EMU y centésimas de punto en dirección inversa.
- Sincronización por línea = una diapositiva por línea (regla MVP; el diálogo
  de exportación lo explica).
- Toda operación produce un **informe de fidelidad** (F4.15).
