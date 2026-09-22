// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Lyrics.h : Parser de letras estructuradas (port de la edicion Qt v1.6.0).
//  Formato soportado:
//    [Verso 1] / [Coro] / [Puente] / [Tag] / [Intro] / [Final]
//    Lineas de texto; lineas de acordes (cifrado) detectadas automaticamente
//    y adjuntas a la linea siguiente. Lineas en blanco separan bloques.
//  Incluye TODAS las correcciones del Modo Hinario (v1.2.0 + M16 v1.6.0):
//    (c) cancion solo-coros NO se proyecta vacia (camino lineal);
//    (d) [Coro] repetido se deduplica: se usa el primer coro unicamente.
// ============================================================================
#ifndef LUMINA_LYRICS_H
#define LUMINA_LYRICS_H

#include "Types.h"
#include "Chords.h"

#include <vector>
#include <algorithm>

class Lyrics
{
public:
    struct Section
    {
        wxString tag;                    // "Verso 1", "Coro", ...
        std::vector<SlideLine> lines;
    };

    struct BuildOptions
    {
        bool  titleSlide       = true;   // slide inicial con titulo/artista
        bool  endBlank         = false;  // slide final en blanco
        bool  chorusInterleave = false;  // Modo Hinario: coro intercalado
        int   maxLinesPerSlide = 4;
        bool  stripChords      = true;   // audiencia no ve cifras
        bool  latinChords      = true;
        int   transpose        = 0;
    };

    // Parsea la letra cruda en secciones etiquetadas
    static std::vector<Section> Parse(const wxString &raw)
    {
        std::vector<Section> sections;
        Section current;
        current.tag = "Letra";
        wxString pendingChords;

        const int n = (int)raw.Length();
        int lineStart = 0;
        for (int pos = 0; pos <= n; ++pos) {
            const bool atEnd = (pos == n);
            const wxChar ch = atEnd ? wxChar('\n') : wxChar(raw[pos]);
            if (ch != '\n' && !(ch == '\r' && pos + 1 < n && raw[pos + 1] == '\n') && ch != '\r')
                continue;
            // Extrae la linea [lineStart, pos)
            wxString line = raw.Mid(lineStart, pos - lineStart);
            if (!atEnd && ch == '\r')
                ++pos;                                   // consume \n tras \r
            lineStart = pos + 1;

            ProcessLine(line, sections, current, pendingChords);
        }
        Flush(sections, current);
        if (sections.empty()) {
            Section s;
            s.tag = "Letra";
            sections.push_back(s);
        }
        return sections;
    }

    // Construye las slides de una cancion para proyeccion
    static std::vector<Slide> BuildSlides(const Song &song, const BuildOptions &opt)
    {
        std::vector<Slide> out;
        if (opt.titleSlide) {
            Slide s;
            s.kind = Slide::Title;
            s.title = song.title;
            wxString head;
            if (!song.artist.empty()) head += song.artist;
            if (!song.key.empty())    head += (head.empty() ? wxString() : wxString(L"  ·  ")) + wxString("Tono: ") + song.key;
            if (song.bpm > 0)         head += (head.empty() ? wxString() : wxString(L"  ·  ")) + wxString::Format("%d BPM", song.bpm);
            s.lines.push_back(SlideLine(song.title));
            if (!head.empty())
                s.lines.push_back(SlideLine(head));
            out.push_back(s);
        }

        std::vector<Section> sections = Parse(song.lyrics);

        // Divide secciones largas en bloques de maxLinesPerSlide conservando la
        // agrupacion por seccion original (el intercalado del coro ocurre tras
        // cada VERSO completo, no entre bloques paginados).
        std::vector<std::vector<Section>> sectionBlocks;
        std::vector<Section> blocks;
        for (const Section &sec : sections) {
            std::vector<Section> parts;
            if ((int)sec.lines.size() <= opt.maxLinesPerSlide) {
                parts.push_back(sec);
            } else {
                for (size_t i = 0; i < sec.lines.size(); i += (size_t)opt.maxLinesPerSlide) {
                    Section part;
                    part.tag = sec.tag;
                    const size_t end = std::min(i + (size_t)opt.maxLinesPerSlide, sec.lines.size());
                    for (size_t j = i; j < end; ++j)
                        part.lines.push_back(sec.lines[j]);
                    parts.push_back(part);
                }
            }
            sectionBlocks.push_back(parts);
            for (const Section &b : parts)
                blocks.push_back(b);
        }

        auto isChorusTag = [](const wxString &t) {
            return t.CmpNoCase("coro") == 0;
        };

        // Localiza el primer coro (para el modo hinario clasico)
        int chorusIdx = -1;
        for (size_t i = 0; i < blocks.size(); ++i) {
            if (isChorusTag(blocks[i].tag)) { chorusIdx = (int)i; break; }
        }

        if (opt.chorusInterleave && chorusIdx >= 0) {
            // Modo Hinario (v1.2.0 + M16 + mejora wx v2.0.0):
            //  (c) solo-coros -> camino lineal (no se pierde la letra);
            //  (d) [Coro] repetido -> usar SOLO el primer coro;
            //  (e) el coro se intercala tras el VERSO COMPLETO (todas sus
            //      paginas), no entre las paginas de un mismo verso: se
            //      preserva la agrupacion por verso (grupos completos).
            std::vector<Section> chorusBlocks;
            std::vector<std::vector<Section>> verseGroups;
            for (size_t s = 0; s < sections.size(); ++s) {
                if (isChorusTag(sections[s].tag)) {
                    if (chorusBlocks.empty())
                        chorusBlocks = sectionBlocks[s];      // (d) primer coro
                } else {
                    verseGroups.push_back(sectionBlocks[s]);  // (e) verso completo
                }
            }
            if (verseGroups.empty()) {
                // (c) solo coros: proyeccion lineal tal cual
                for (const auto &group : sectionBlocks)
                    for (const Section &b : group)
                        out.push_back(SectionToSlide(b, song, opt));
            } else {
                for (const auto &group : verseGroups) {
                    for (const Section &b : group)
                        out.push_back(SectionToSlide(b, song, opt));
                    for (const Section &ch : chorusBlocks)     // V1 C V2 C
                        out.push_back(SectionToSlide(ch, song, opt));
                }
            }
        } else {
            for (const auto &group : sectionBlocks)
                for (const Section &b : group)
                    out.push_back(SectionToSlide(b, song, opt));
        }

        if (opt.endBlank) {
            Slide s;
            s.kind = Slide::Blank;
            s.title = song.title;
            out.push_back(s);
        }
        return out;
    }

    // Texto plano de una slide (sin acordes), para listas/overlay web
    static wxString PlainText(const Slide &s)
    {
        wxString out;
        for (size_t i = 0; i < s.lines.size(); ++i) {
            if (i) out += '\n';
            out += s.lines[i].text;
        }
        return out;
    }

private:
    static void ProcessLine(const wxString &rawLine, std::vector<Section> &sections,
                            Section &current, wxString &pendingChords)
    {
        const wxString line = rawLine;
        // Etiqueta de seccion: "[texto]" sola en linea, sin ser acorde
        const wxString trimmed = wxString(line).Trim(true).Trim(false);
        if (trimmed.StartsWith("[") && trimmed.EndsWith("]") && trimmed.Length() > 2) {
            const wxString tagText = wxString(trimmed.Mid(1, trimmed.Length() - 2)).Trim(true).Trim(false);
            const bool isChord = !tagText.Contains(' ') && Chords::IsChordToken(tagText);
            if (!tagText.empty() && !isChord) {
                Flush(sections, current);
                current = Section();
                current.tag = tagText;
                return;
            }
        }
        if (trimmed.empty())
            return;                     // separador de bloque
        if (Chords::IsChordLine(line)) {
            pendingChords = line;       // se adjunta a la proxima linea de texto
            return;
        }
        current.lines.push_back(SlideLine(line, pendingChords));
        pendingChords.clear();
    }

    static void Flush(std::vector<Section> &sections, Section &current)
    {
        if (!current.lines.empty())
            sections.push_back(current);
    }

    static Slide SectionToSlide(const Section &sec, const Song &song, const BuildOptions &opt)
    {
        Slide s;
        s.kind = Slide::Text;
        s.title = song.title;
        s.refLabel = sec.tag;
        for (const SlideLine &l : sec.lines) {
            if (opt.stripChords)
                s.lines.push_back(SlideLine(l.text));
            else
                s.lines.push_back(SlideLine(l.text,
                    l.chords.empty() ? wxString()
                                     : Chords::TransposeLine(l.chords, opt.transpose, opt.latinChords)));
        }
        return s;
    }
};

#endif // LUMINA_LYRICS_H
