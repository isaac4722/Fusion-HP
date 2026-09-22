# Formato .BIB (biblias) — especificación y compatibilidad

El núcleo (`fusion_bib_parse`) acepta archivos `.bib` de biblias en texto plano con
**detección automática** de variante. Objetivo: que cualquier biblia exportada a este
formato simple sea importable sin conversión manual.

## 1. Estructura general

```text
#BIB 1
#VERSION RVR1909
#NAME Reina-Valera 1909
#BOOKS 66
Génesis	1	1	En el principio creó Dios los cielos y la tierra.
Génesis	1	2	Y la tierra estaba desordenada y vacía…
…
Apocalipsis	22	21	La gracia de nuestro Señor Jesucristo sea con vosotros. Amén.
```

* Líneas que comienzan con `#` son **directivas**:
  * `#BIB <n>` — marca de formato (opcional; versión `1`).
  * `#VERSION <código>` — código corto de la versión (RVR1909, NVI…). Si falta, se
    deriva del nombre de archivo (saneado: mayúsculas, sin caracteres no alfanuméricos, máx. 16).
  * `#NAME <texto>` — nombre visible (opcional; por defecto `VERSION`).
  * `#BOOKS <n>` — informativo (validación amable, no bloqueante).
* Cada fila de versículo: **`libro` SEP `capítulo` SEP `versículo` SEP `texto`**
  con SEP = TAB **o** ` | ` **o** `;;` (detectado por mayoría de líneas; la primera
  línea de datos decide).
* Variante alternativa (sin columnas): **`Libro capítulo:versículo texto…`**
  (`Génesis 1:1 En el principio…`) — aceptada si las filas con columnas no alcanzan
  el 50 % de las líneas de datos.

## 2. Codificaciones

* UTF-8 (con o sin BOM) — preferida.
* CP1252/Latin-1 — detectada automáticamente (bytes inválidos UTF-8 + presencia de
  acentos latinos típicos) y convertida a UTF-8.

## 3. Validación y estadísticas (`fusion_bib_parse`)

Devuelve JSON:

```json
{"ok":1,"verses":31104,"books":66,"version":"RVR1909","name":"Reina-Valera 1909",
 "encoding":"utf-8","separator":"tab","sample":["Génesis 1:1 En el principio…"],
 "errors":0}
```

* `errors` cuenta filas descartadas (capítulo/versículo no numéricos, columnas insuficientes).
* `options.maxBytes` (por defecto 64 MB) evita OOM en equipos de 4 GB (herencia §2.5 spec).
* Nombres de libro se normalizan con la tabla canónica de 66 libros (BibleRef):
  `Gn/Gen/1sam/1 sam` → número canónico 1..66 en la BD.

## 4. Almacenamiento

`bible(version, book, chapter, verse, text)` con `UNIQUE(version,book,chapter,verse)`
+ dedupe idempotente previo a la creación del índice (heredado del ciclo v1.6.0) —
re-importar la misma biblia **no duplica** versículos.

## 5. Generador de muestra

`tools/make_sample_bib.py` convierte `resources/data/bible_rvr1909.json` a
`sample/rvr1909.bib` (formato TAB + directivas) — usado por los tests y como ejemplo
para iguales/congregaciones.
