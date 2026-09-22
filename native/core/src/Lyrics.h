// Contrato: letras estructuradas → slides (port de Lyrics.h wx v2.0.0,
// con M16 y la mejora de intercalado del coro tras el verso completo).
#ifndef FUSION_LYRICS_H
#define FUSION_LYRICS_H

#include <string>
#include <vector>
#include "Models.h"

namespace fusion {
struct LyricsSection {
    std::string tag;                       // "Verso 1", "Coro"…
    std::vector<SlideLine> lines;
};

class Lyrics {
public:
    static std::vector<LyricsSection> Parse(const std::string& raw);
    static std::vector<Slide> BuildSlides(const Song& song, const BuildOptions& opt);
    static std::string PlainText(const Slide& s);
};
} // namespace fusion
#endif
