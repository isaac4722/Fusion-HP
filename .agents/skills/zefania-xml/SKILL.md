---
name: zefania-xml
description: Importador de Biblias Zefania XML, .bib (e-Sword) y JSON {reference,text} con modelo bíblico unificado, indexación incremental y búsqueda instantánea ≤200 ms. Usar siempre que se toque el importador de Biblias.
---

# zefania-xml — Biblias para el MVP [SPEC §9.1]

## Modelo bíblico unificado (F4.01)

Campos comunes a los tres formatos: libro canónico + alias, capítulo,
versículo, traducción, texto. Todos los importadores convergen aquí.

## Zefania XML

- Parseo incremental (Biblias > 100 MB descomprimidos) — streaming, nunca
  cargar el DOM completo.
- Indexar por libro/capítulo/versículo DURANTE la importación.
- Leer metadatos `<name>` (traducción) y `<copyright>`.
- Validación de esquema con informe de errores línea a línea.
- Prueba de referencia: Reina Valera 1960.

## .bib (módulos e-Sword)

- Extraer texto plano con referencias y normalizar al modelo común.
- Informar limitaciones: notas al pie y números de Strong se descartan en el MVP.
- Insertar TODAS las filas en la base (nada de "validado pero no insertado").

## JSON

- Aceptar el arreglo `[{"reference": "Juan 3:16", "text": "..."}]`.
- Parseo incremental, normalización de alias, inserción por lotes, índice de búsqueda.

## Rendimiento y acceso

- Las Biblias se acceden por índice en disco — jamás precargadas completas
  (carga diferida, F3.07).
- Búsqueda por palabra ≤ 200 ms sobre RV1960 indexada (tabla 10.1).
- Búsqueda instantánea por cita y por palabra con resaltado (F2.13).
