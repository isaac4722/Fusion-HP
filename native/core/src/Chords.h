// Contrato: transposición de acordes (port 1:1 de la edición wx v2.0.0).
#ifndef FUSION_CHORDS_H
#define FUSION_CHORDS_H

#include <string>

namespace fusion {
class Chords {
public:
    // Semitono (0..11) de una nota; -1 si inválida. Latina y anglosajona.
    static int NoteToSemitone(const std::string& noteRaw);
    static std::string SemitoneToNote(int semi, bool latinNotation);
    static bool IsChordToken(const std::string& tk);
    static bool IsChordLine(const std::string& line);
    static std::string TransposeLine(const std::string& chordLine, int semi, bool latin);
    static std::string TransposeChordToken(const std::string& tk, int semi, bool latin);
};
} // namespace fusion
#endif
