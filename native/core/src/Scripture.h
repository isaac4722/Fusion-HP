// Contrato: slides de escritura (rango de versículos → SLIDE_SCRIPTURE).
#ifndef LUMINA_SCRIPTURE_H
#define LUMINA_SCRIPTURE_H

#include "Models.h"

namespace lumina {
class Scripture {
public:
    // Cada verso = una slide (refLabel "Libro C:V"); maxVersesPerSlide>=1
    // agrupa. ref vacío o inválido → devuelve false y no genera slides.
    static bool BuildSlides(const std::string& ref,
                            const std::vector<std::string>& verseTexts,
                            int maxVersesPerSlide,
                            std::vector<Slide>* out);
};
} // namespace lumina
#endif
