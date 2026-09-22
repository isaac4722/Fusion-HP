// Contrato: parser .BIB (docs/bib-format.md) — detección automática.
#ifndef FUSION_BIBLEBIB_H
#define FUSION_BIBLEBIB_H

#include "Models.h"
#include <nlohmann/json.hpp>

namespace fusion {
struct BibVerse { int book=0; int chapter=0; int verse=0; std::string text; std::string bookName; };

class BibleBib {
public:
    struct Stats {
        json ToJson() const;
        int  verses = 0, books = 0, errors = 0;
        std::string version, name, encoding, separator;
        std::vector<std::string> sample;   // hasta 3 muestras "Libro C:V texto"
    };

    // Analiza un .BIB en memoria (UTF-8 ya convertido si venía CP1252).
    // maxVerses limita la memoria (0 = sin límite).
    static bool Parse(const std::string& bytes, Stats* stats,
                      std::vector<BibVerse>* verses /*opcional*/,
                      long long maxBytes = 64LL*1024*1024);
};
} // namespace fusion
#endif
