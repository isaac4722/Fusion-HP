// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Scripture.cpp : slides de escritura bíblica — resuelve la referencia con
//  BibleRef y agrupa maxVersesPerSlide versos por slide. refLabel = "Libro
//  C:V" del primer verso del grupo (con rango "C:V1-V2" si agrupa varios).
//  kind = SLIDE_SCRIPTURE, title vacío (el pie lo pone la capa de UI).
//  Ref inválido o lista de versos vacía → false y no genera slides.
// ============================================================================
#include "Scripture.h"

#include "BibleRef.h"

#include <algorithm>

namespace lumina {

bool Scripture::BuildSlides(const std::string& ref,
                            const std::vector<std::string>& verseTexts,
                            int maxVersesPerSlide,
                            std::vector<Slide>* out) {
    if (!out) return false;
    out->clear();
    const BibleRef::VerseRef r = BibleRef::Resolve(ref);
    if (!r.Valid() || verseTexts.empty())
        return false;

    // Agrupa en bloques de 'per' versos (mínimo 1 por slide).
    const int per = maxVersesPerSlide >= 1 ? maxVersesPerSlide : 1;
    // Ref de solo capítulo ("Juan 3") → el primer verso mostrado es el 1.
    const int firstVerse = r.verse > 0 ? r.verse : 1;

    for (size_t i = 0; i < verseTexts.size(); i += (size_t)per) {
        const size_t end = std::min(i + (size_t)per, verseTexts.size());
        Slide s;
        s.kind = SLIDE_SCRIPTURE;
        s.title = "";                        // contrato: sin título
        // refLabel del PRIMER verso del grupo: "Libro C:V" (+ "-V2" si agrupa)
        s.refLabel = r.bookName + " " + std::to_string(r.chapter) + ":" +
                     std::to_string(firstVerse + (int)i);
        if (end - i > 1)
            s.refLabel += "-" + std::to_string(firstVerse + (int)end - 1);
        for (size_t k = i; k < end; ++k)
            s.lines.push_back(SlideLine(verseTexts[k]));
        out->push_back(std::move(s));
    }
    return true;
}

} // namespace lumina
