// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Highlight.cpp : implementación del resaltado en proyección (ver Highlight.h).
//  Estrategia de offsets: el texto original y la palabra se plegan a un flujo
//  de UN byte por carácter lógico (UTF-8 → ASCII plegado); la búsqueda de
//  ocurrencias se hace sobre el flujo plegado (mismos índices en texto y
//  palabra) y cada posición del flujo recuerda el offset de BYTE original,
//  para cortar los segmentos en el UTF-8 intacto.
// ============================================================================
#include "Highlight.h"

#include <cstring>

namespace lumina {
namespace {

/* ---------------------------------------------- plegado Latin-1 básico -- */

char FoldAscii(char c) {
    if (c >= 'A' && c <= 'Z') return (char)(c - 'A' + 'a');
    return c;
}

// Segundo byte de una secuencia UTF-8 0xC3 0x80..0xBF (Latin-1 supplement).
// Devuelve el ASCII plegado o 0 si no es plegable.
char FoldLatin1Supp(unsigned char c2) {
    switch (c2) {
        // À Á Â Ã Ä Å → a (0x80..0x85)
        case 0x80: case 0x81: case 0x82: case 0x83: case 0x84: case 0x85:
        // à á â ã ä å → a (0xA0..0xA5)
        case 0xA0: case 0xA1: case 0xA2: case 0xA3: case 0xA4: case 0xA5:
            return 'a';
        // Ç → c · ç → c
        case 0x87: case 0xA7:
            return 'c';
        // È É Ê Ë → e (0x88..0x8B) · è é ê ë → e (0xA8..0xAB)
        case 0x88: case 0x89: case 0x8A: case 0x8B:
        case 0xA8: case 0xA9: case 0xAA: case 0xAB:
            return 'e';
        // Ì Í Î Ï → i (0x8C..0x8F) · ì í î ï → i (0xAC..0xAF)
        case 0x8C: case 0x8D: case 0x8E: case 0x8F:
        case 0xAC: case 0xAD: case 0xAE: case 0xAF:
            return 'i';
        // Ñ → n · ñ → n
        case 0x91: case 0xB1:
            return 'n';
        // Ò Ó Ô Õ Ö → o (0x92..0x96) · ò ó ô õ ö → o (0xB2..0xB6)
        case 0x92: case 0x93: case 0x94: case 0x95: case 0x96:
        case 0xB2: case 0xB3: case 0xB4: case 0xB5: case 0xB6:
            return 'o';
        // Ù Ú Û Ü → u (0x99..0x9C) · ù ú û ü → u (0xB9..0xBC)
        case 0x99: case 0x9A: case 0x9B: case 0x9C:
        case 0xB9: case 0xBA: case 0xBB: case 0xBC:
            return 'u';
        // Ý → y · ý ÿ → y
        case 0x9D: case 0xBD: case 0xBF:
            return 'y';
        default:
            return 0;
    }
}

// Longitud de un carácter UTF-8 desde su byte inicial (0 si es byte suelto).
size_t Utf8Len(unsigned char c) {
    if (c < 0x80) return 1;
    if (c >= 0xC2 && c <= 0xDF) return 2;
    if (c >= 0xE0 && c <= 0xEF) return 3;
    if (c >= 0xF0 && c <= 0xF4) return 4;
    return 1;   // byte de continuación huérfano: tratar como unidad
}

bool IsAlnumAscii(char c) {
    return (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
}

// Flujo plegado: cada carácter lógico → un byte ASCII + offset de byte
// original. «kNeutral» para caracteres no plegables/no latinos.
const char kNeutral = '\x01';

void FoldStream(const std::string& s, std::string* folded, std::vector<size_t>* offsets) {
    folded->clear();
    if (offsets) offsets->clear();
    folded->reserve(s.size());
    if (offsets) offsets->reserve(s.size());
    size_t i = 0;
    while (i < s.size()) {
        const unsigned char c = (unsigned char)s[i];
        const size_t len = Utf8Len(c);
        if (len == 1 && c < 0x80) {
            folded->push_back(FoldAscii((char)c));
            if (offsets) offsets->push_back(i);
        } else if (len == 2 && c == 0xC3 && i + 1 < s.size()) {
            const char f = FoldLatin1Supp((unsigned char)s[i + 1]);
            folded->push_back(f ? f : kNeutral);
            if (offsets) offsets->push_back(i);
        } else {
            folded->push_back(kNeutral);
            if (offsets) offsets->push_back(i);
        }
        i += len;
    }
}

} // namespace

/* ------------------------------------------------------------- Split ----- */

std::vector<HlSegment> Highlight::Split(const std::string& text, const std::string& word) {
    std::vector<HlSegment> out;
    if (word.empty()) {
        out.push_back(HlSegment(text, false));
        return out;
    }

    std::string foldedText, foldedWord;
    std::vector<size_t> offsets;
    FoldStream(text, &foldedText, &offsets);
    FoldStream(word, &foldedWord, nullptr);
    if (foldedWord.find(kNeutral) != std::string::npos) {
        // La palabra plegada quedó con caracteres no ASCII (p. ej. palabra en
        // cirílico): el plegado Latin no la representa; sin resaltado.
        out.push_back(HlSegment(text, false));
        return out;
    }

    const size_t n = foldedText.size();
    const size_t m = foldedWord.size();
    if (m == 0 || m > n) {
        out.push_back(HlSegment(text, false));
        return out;
    }

    // Marcas de coincidencia por posición del flujo plegado.
    std::vector<bool> match(n, false);
    size_t lastEnd = 0;   // fin (exclusivo) del último match aceptado
    for (size_t start = 0; start + m <= n; ++start) {
        // Frontera izquierda: palabra completa.
        if (start > 0 && IsAlnumAscii(foldedText[start - 1])) continue;
        if (std::memcmp(foldedText.data() + start, foldedWord.data(), m) != 0) continue;
        const size_t end = start + m;
        // Frontera derecha.
        if (end < n && IsAlnumAscii(foldedText[end])) continue;
        // Solapamiento con un match previo: se descarta (greedy izquierda→derecha).
        if (start < lastEnd) continue;
        for (size_t k = start; k < end; ++k) match[k] = true;
        lastEnd = end;
    }

    // Construcción de segmentos en bytes ORIGINALES vía offsets.
    size_t segStart = 0;      // índice del flujo
    bool inMatch = false;
    for (size_t k = 0; k <= n; ++k) {
        const bool isM = (k < n) ? match[k] : !inMatch;   // cierre al final
        if (k == n || isM != inMatch) {
            // El segmento cubre el flujo [segStart, k): bytes originales
            // [offsets[segStart], k < n ? offsets[k] : text.size()).
            if (k > segStart) {
                const size_t from = offsets[segStart];
                const size_t to = (k < n) ? offsets[k] : text.size();
                if (to > from)
                    out.push_back(HlSegment(text.substr(from, to - from), inMatch));
            }
            segStart = k;
            inMatch = isM;
        }
    }
    if (out.empty()) out.push_back(HlSegment(text, false));
    return out;
}

/* --------------------------------------------------------- FoldLatin ---- */

std::string Highlight::FoldLatin(const std::string& s) {
    std::string folded;
    std::vector<size_t> offsets;
    FoldStream(s, &folded, &offsets);
    return folded;
}

} // namespace lumina
