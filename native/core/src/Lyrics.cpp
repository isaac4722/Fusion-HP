// ============================================================================
//  Fusion-HP / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Lyrics.cpp : parser de letras estructuradas → slides (port 1:1 de la
//  edición wx v2.0.0, con M16 y la mejora del Modo Hinario del ciclo v2.0.0).
//  Formato soportado:
//    [Verso 1] / [Coro] / [Puente] / [Tag] / [Intro] / [Final]
//    Líneas de texto; líneas de acordes (cifrado) detectadas con
//    Chords::IsChordLine y adjuntas a la línea siguiente. Líneas vacías
//    separan bloques. Paginación por maxLinesPerSlide conservando la
//    agrupación por sección original.
//  Incluye TODAS las correcciones del Modo Hinario (v1.2.0 + M16 + v2.0.0):
//    (c) canción solo-coros NO se proyecta vacía (camino lineal);
//    (d) [Coro] repetido se deduplica: se usa el primer coro únicamente;
//    (e) el coro se intercala tras cada VERSO COMPLETO (todas sus páginas).
//  Si stripChords=false y la línea trae cifrado, se transpone con
//  Chords::TransposeLine(chords, transpose, latinChords).
// ============================================================================
#include "Lyrics.h"

#include "Chords.h"
#include "Utf8.h"

#include <algorithm>

namespace fusion {
namespace {

bool IsChorusTag(const std::string& tag) {
    return ToLowerAscii(Trim(tag)) == "coro";
}

// Solo agrega la sección en curso si tiene contenido.
void Flush(std::vector<LyricsSection>& sections, LyricsSection& current) {
    if (!current.lines.empty())
        sections.push_back(current);
}

// Procesa una línea cruda (port de Lyrics::ProcessLine de wx).
void ProcessLine(const std::string& rawLine, std::vector<LyricsSection>& sections,
                 LyricsSection& current, std::string& pendingChords) {
    const std::string line = rawLine;
    // Etiqueta de sección: "[texto]" sola en línea, sin ser acorde
    const std::string trimmed = Trim(line);
    if (StartsWith(trimmed, "[") && EndsWith(trimmed, "]") && trimmed.size() > 2) {
        const std::string tagText = Trim(trimmed.substr(1, trimmed.size() - 2));
        // Si el contenido de [] es un token de acorde NO es etiqueta
        const bool isChord = tagText.find(' ') == std::string::npos &&
                             Chords::IsChordToken(tagText);
        if (!tagText.empty() && !isChord) {
            Flush(sections, current);
            current = LyricsSection();
            current.tag = tagText;
            return;
        }
    }
    if (trimmed.empty())
        return;                          // separador de bloque
    if (Chords::IsChordLine(line)) {
        pendingChords = line;            // se adjunta a la próxima línea de texto
        return;
    }
    current.lines.push_back(SlideLine(line, pendingChords));
    pendingChords.clear();
}

// Convierte una sección (o bloque paginado) en slide.
Slide SectionToSlide(const LyricsSection& sec, const Song& song, const BuildOptions& opt) {
    Slide s;
    s.kind = SLIDE_TEXT;
    s.title = song.title;
    s.refLabel = sec.tag;
    for (const SlideLine& l : sec.lines) {
        if (opt.stripChords)
            s.lines.push_back(SlideLine(l.text));
        else
            s.lines.push_back(SlideLine(l.text,
                l.chords.empty() ? std::string()
                                 : Chords::TransposeLine(l.chords, opt.transpose, opt.latinChords)));
    }
    return s;
}

} // namespace

/* --------------------------------------------------------------- Parse -- */

std::vector<LyricsSection> Lyrics::Parse(const std::string& raw) {
    std::vector<LyricsSection> sections;
    LyricsSection current;
    current.tag = "Letra";
    std::string pendingChords;

    const size_t n = raw.size();
    size_t lineStart = 0;
    for (size_t pos = 0; pos <= n; ++pos) {
        const bool atEnd = (pos == n);
        const char ch = atEnd ? '\n' : raw[pos];
        // Fin de línea: '\n', o '\r' (el port wx consume '\n' tras '\r';
        // comportamiento 1:1, incluida la peculiaridad del '\r' suelto).
        if (ch != '\n' && !(ch == '\r' && pos + 1 < n && raw[pos + 1] == '\n') && ch != '\r')
            continue;
        // Extrae la línea [lineStart, pos)
        std::string line = raw.substr(lineStart, pos - lineStart);
        if (!atEnd && ch == '\r')
            ++pos;                                   // consume \n tras \r
        lineStart = pos + 1;
        ProcessLine(line, sections, current, pendingChords);
    }
    Flush(sections, current);
    if (sections.empty()) {
        LyricsSection s;
        s.tag = "Letra";
        sections.push_back(s);
    }
    return sections;
}

/* --------------------------------------------------------- BuildSlides -- */

std::vector<Slide> Lyrics::BuildSlides(const Song& song, const BuildOptions& opt) {
    std::vector<Slide> out;
    if (opt.titleSlide) {
        Slide s;
        s.kind = SLIDE_TITLE;
        s.title = song.title;
        std::string head;
        if (!song.artist.empty()) head += song.artist;
        if (!song.keyName.empty())
            head += (head.empty() ? std::string() : std::string("  ·  ")) + "Tono: " + song.keyName;
        if (song.bpm > 0)
            head += (head.empty() ? std::string() : std::string("  ·  ")) +
                    std::to_string(song.bpm) + " BPM";
        s.lines.push_back(SlideLine(song.title));
        if (!head.empty())
            s.lines.push_back(SlideLine(head));
        out.push_back(std::move(s));
    }

    // blocks manda sobre lyrics crudo (serialización "[label]\nlineas\n\n");
    // si no hay blocks, LyricsText() devuelve lyrics tal cual.
    std::vector<LyricsSection> sections = Parse(song.LyricsText());

    // Divide secciones largas en bloques de maxLinesPerSlide conservando la
    // agrupación por sección original (el intercalado del coro ocurre tras
    // cada VERSO completo, no entre bloques paginados).
    // (Defensa: el port wx confiaba del llamador; con maxLinesPerSlide < 1 el
    //  avance sería cero — se acepta 1 como mínimo.)
    const int maxLines = opt.maxLinesPerSlide > 0 ? opt.maxLinesPerSlide : 1;
    std::vector<std::vector<LyricsSection>> sectionBlocks;
    std::vector<LyricsSection> blocks;
    for (const LyricsSection& sec : sections) {
        std::vector<LyricsSection> parts;
        if ((int)sec.lines.size() <= maxLines) {
            parts.push_back(sec);
        } else {
            for (size_t i = 0; i < sec.lines.size(); i += (size_t)maxLines) {
                LyricsSection part;
                part.tag = sec.tag;
                const size_t end = std::min(i + (size_t)maxLines, sec.lines.size());
                for (size_t j = i; j < end; ++j)
                    part.lines.push_back(sec.lines[j]);
                parts.push_back(std::move(part));
            }
        }
        sectionBlocks.push_back(parts);
        for (const LyricsSection& b : parts)
            blocks.push_back(b);
    }

    // Localiza el primer coro (para el modo hinario clásico)
    int chorusIdx = -1;
    for (size_t i = 0; i < blocks.size(); ++i) {
        if (IsChorusTag(blocks[i].tag)) { chorusIdx = (int)i; break; }
    }

    if (opt.chorusInterleave && chorusIdx >= 0) {
        // Modo Hinario (v1.2.0 + M16 + mejora wx v2.0.0):
        //  (c) solo-coros → camino lineal (no se pierde la letra);
        //  (d) [Coro] repetido → usar SOLO el primer coro;
        //  (e) el coro se intercala tras el VERSO COMPLETO (todas sus
        //      páginas), no entre las páginas de un mismo verso.
        std::vector<LyricsSection> chorusBlocks;
        std::vector<std::vector<LyricsSection>> verseGroups;
        for (size_t s = 0; s < sections.size(); ++s) {
            if (IsChorusTag(sections[s].tag)) {
                if (chorusBlocks.empty())
                    chorusBlocks = sectionBlocks[s];      // (d) primer coro
            } else {
                verseGroups.push_back(sectionBlocks[s]);  // (e) verso completo
            }
        }
        if (verseGroups.empty()) {
            // (c) solo coros: proyección lineal tal cual (nunca vacía)
            for (const auto& group : sectionBlocks)
                for (const LyricsSection& b : group)
                    out.push_back(SectionToSlide(b, song, opt));
        } else {
            for (const auto& group : verseGroups) {
                for (const LyricsSection& b : group)
                    out.push_back(SectionToSlide(b, song, opt));
                for (const LyricsSection& ch : chorusBlocks)     // V1 C V2 C
                    out.push_back(SectionToSlide(ch, song, opt));
            }
        }
    } else {
        for (const auto& group : sectionBlocks)
            for (const LyricsSection& b : group)
                out.push_back(SectionToSlide(b, song, opt));
    }

    if (opt.endBlank) {
        Slide s;
        s.kind = SLIDE_BLANK;
        s.title = song.title;
        out.push_back(std::move(s));
    }
    return out;
}

/* ----------------------------------------------------------- PlainText -- */

std::string Lyrics::PlainText(const Slide& s) {
    std::string out;
    for (size_t i = 0; i < s.lines.size(); ++i) {
        if (i) out += '\n';
        out += s.lines[i].text;
    }
    return out;
}

} // namespace fusion
