// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Highlight.h (v6.0.0 «HORIZONTE») : resaltado en PROYECCIÓN (roadmap:
//  «llevar el resaltado de la búsqueda a pantalla»). Módulo PORTABLE (Linux +
//  Win32): parte una línea de texto en segmentos marcando las ocurrencias de
//  una palabra/frase con coincidencia INSENSIBLE a mayúsculas y a acentos
//  latinos (á→a, É→e, ü→u, ñ→n…) y con frontera de palabra (no matchea
//  «Dios» dentro de «Diosas»).
//
//  El Renderer (Win32) dibuja los segmentos coincidentes en color de acento;
//  el resto conserva el estilo del tema. El plegado Latin-1 básico cubre el
//  español (idioma del producto) sin dependencias (sin ICU ni locale).
// ============================================================================
#ifndef LUMINA_HIGHLIGHT_H
#define LUMINA_HIGHLIGHT_H

#include <string>
#include <vector>

namespace lumina {

// Un segmento de línea: texto tal cual del original + marca de coincidencia.
struct HlSegment {
    std::string text;   // porción del texto original (UTF-8 intacto)
    bool        match;  // true = coincide con la palabra buscada
    HlSegment() : match(false) {}
    HlSegment(const std::string& t, bool m) : text(t), match(m) {}
};

class Highlight {
public:
    // Parte «text» en segmentos según las ocurrencias de «word». Si word está
    // vacía (o no hay coincidencias) devuelve UN segmento no-match con todo
    // el texto. La coincidencia es por palabra/frase COMPLETA (fronteras:
    // carácter no alfanumérico o extremos), plegando mayúsculas y acentos
    // latinos de AMBOS lados (texto y palabra). No lanza.
    static std::vector<HlSegment> Split(const std::string& text, const std::string& word);

    // Plegado case/acento-insensible a un flujo de BYTES ASCII (un byte por
    // carácter lógico): usado por Split y disponible para pruebas. Caracteres
    // no latinos se plegan a '\x01' (no alfanumérico: frontera de palabra).
    static std::string FoldLatin(const std::string& s);
};

} // namespace lumina
#endif // LUMINA_HIGHLIGHT_H
