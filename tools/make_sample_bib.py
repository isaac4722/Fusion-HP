#!/usr/bin/env python3
# ============================================================================
#  LuminaPresentation / LuminaPresentation Suite - tools/make_sample_bib.py
#  Copyright (c) 2026 Isaac. Licencia View-Only.
# ----------------------------------------------------------------------------
#  Convierte resources/data/bible_rvr1909.json (formato interno del repo) al
#  formato .BIB que parsea el núcleo (lumina_bib_parse — docs/bib-format.md):
#
#      #BIB 1
#      #VERSION RVR1909
#      #NAME Reina-Valera 1909
#      #BOOKS 66
#      Génesis<TAB>1<TAB>1<TAB>EN el principio crió Dios los cielos y la tierra.
#      ...
#
#  Propiedades:
#    * DETERMINISTA: recorre libros por número canónico (n), capítulos y
#      versículos en orden; misma entrada -> mismos bytes de salida.
#    * UTF-8 sin BOM, finales de línea LF (nuevo: newline='\n').
#    * Limpia caracteres que romperían las columnas TAB (saltos de línea y
#      tabs dentro del texto del versículo -> un espacio) y recorta bordes.
#    * Sin dependencias externas (solo stdlib).
#
#  Uso:  python3 tools/make_sample_bib.py [entrada.json] [salida.bib]
#  Default entrada: resources/data/bible_rvr1909.json
#  Default salida:  resources/data/sample/rvr1909.bib
# ============================================================================
import json
import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_IN = os.path.join(REPO_ROOT, 'resources', 'data', 'bible_rvr1909.json')
DEFAULT_OUT = os.path.join(REPO_ROOT, 'resources', 'data', 'sample', 'rvr1909.bib')


def clean_text(text):
    """Texto apto para la columna final de un .BIB de TABs.

    Sustituye tab/saltos de línea por un espacio simple (romperían el
    formato de columnas), colapsa espacios que quedaron dobles y recorta.
    Devuelve (texto_limpio, hubo_ajuste)."""
    adjusted = False
    out = []
    prev_space = False
    for ch in text:
        if ch in ' \t\r\n':
            if ch != ' ':
                adjusted = True
            if not prev_space:
                out.append(' ')
            prev_space = True
        else:
            out.append(ch)
            prev_space = False
    cleaned = ''.join(out).strip()
    return cleaned, adjusted


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_IN
    dst = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_OUT

    if not os.path.isfile(src):
        print(f'ERROR: no existe el JSON de entrada: {src}')
        return 2

    with open(src, 'r', encoding='utf-8') as f:
        bib = json.load(f)

    version = str(bib.get('version', '')).strip() or 'BIB'
    name = str(bib.get('name', '')).strip() or version
    books = bib.get('books')
    if not isinstance(books, list) or not books:
        print('ERROR: el JSON no tiene la lista "books"')
        return 2

    # Determinismo: orden canónico por número de libro (si viene con "n").
    def book_key(b):
        try:
            return (0, int(b.get('n', 0)))
        except (TypeError, ValueError):
            return (1, str(b.get('name', '')))
    books_sorted = sorted(books, key=book_key)

    os.makedirs(os.path.dirname(dst) or '.', exist_ok=True)

    total_verses = 0
    total_books = 0
    adjusted = 0
    # newline='\n' -> LF puro incluso si algún día se corre en Windows.
    with open(dst, 'w', encoding='utf-8', newline='\n') as f:
        f.write('#BIB 1\n')
        f.write(f'#VERSION {version}\n')
        f.write(f'#NAME {name}\n')
        f.write(f'#BOOKS {len(books_sorted)}\n')
        for book in books_sorted:
            book_name = str(book.get('name', '')).strip()
            chapters = book.get('chapters', [])
            if not book_name or not isinstance(chapters, list):
                continue
            total_books += 1
            for ch_idx, verses in enumerate(chapters, start=1):
                if not isinstance(verses, list):
                    continue
                for vs_idx, text in enumerate(verses, start=1):
                    line, was_adj = clean_text(str(text))
                    if was_adj:
                        adjusted += 1
                    f.write(f'{book_name}\t{ch_idx}\t{vs_idx}\t{line}\n')
                    total_verses += 1

    size = os.path.getsize(dst)
    print(f'[make_sample_bib] {src} -> {dst}')
    print(f'  libros: {total_books} · versículos: {total_verses} · '
          f'textos ajustados (tab/salto interno): {adjusted}')
    print(f'  bytes: {size} · codificación: UTF-8 (sin BOM) · EOL: LF')
    return 0


if __name__ == '__main__':
    sys.exit(main())
