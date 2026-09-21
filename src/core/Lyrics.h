// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Lyrics.h : Parser de letras estructuradas.
//  Formato soportado:
//    [Verso 1] / [Coro] / [Puente] / [Tag] / [Intro] / [Final]
//    Lineas de texto; lineas de acordes (cifrado) automaticamente detectadas
//    y adjuntas a la linea siguiente. Lineas en blanco separan bloques.
// ============================================================================
#ifndef LUMINA_LYRICS_H
#define LUMINA_LYRICS_H

#include "Models.h"
#include "Chords.h"

#include <QStringList>
#include <QRegularExpression>

class Lyrics
{
public:
    struct Section
    {
        QString tag;                    // "Verso 1", "Coro", ...
        QVector<SlideLine> lines;
    };

    // Parsea la letra cruda en secciones etiquetadas
    static QVector<Section> parse(const QString &raw)
    {
        QVector<Section> sections;
        const QStringList lines = raw.split(QRegularExpression(QStringLiteral("\r?\n")));
        Section current;
        current.tag = QStringLiteral("Letra");

        // Detecta etiqueta de acorde-token: p.ej "[Do]" sola en linea = etiqueta NO acorde
        static const QRegularExpression simpleTag(QStringLiteral("^\\s*\\[([^\\]]{1,40})\\]\\s*$"));

        // CORRECCION: era un miembro estatico de clase — fuga de estado entre
        // llamadas (los acordes finales de una cancion contaminaban la
        // siguiente). Ahora es estado local del parseo.
        QString pendingChords;

        for (const QString &rawLine : lines) {
            const QString line = rawLine;
            QRegularExpressionMatch mt = simpleTag.match(line);
            if (mt.hasMatch()) {
                const QString tagText = mt.captured(1).trimmed();
                // Si el contenido del corchete NO es un acorde, es etiqueta de seccion
                if (!Chords::isChordToken(tagText) || tagText.contains(QRegularExpression(QStringLiteral("\\s")))) {
                    flush(sections, current);
                    current = Section();
                    current.tag = tagText;
                    continue;
                }
            }
            if (line.trimmed().isEmpty()) {
                continue; // separador de bloque dentro de seccion
            }
            if (Chords::isChordLine(line)) {
                // linea de acordes: se adjunta a la proxima linea de texto
                pendingChords = line;
                continue;
            }
            current.lines.append(SlideLine(line, pendingChords));
            pendingChords.clear();
        }
        flush(sections, current);
        if (sections.isEmpty()) {
            Section s; s.tag = QStringLiteral("Letra");
            sections.append(s);
        }
        return sections;
    }

    // Construye las slides de una cancion para proyeccion
    struct BuildOptions
    {
        bool  titleSlide   = true;    // slide inicial con titulo/artista
        bool  endBlank     = false;   // slide final en blanco
        bool  chorusInterleave = false; // Modo Hinario: coro intercalado
        int   maxLinesPerSlide = 4;
        bool  stripChords  = true;    // audiencia no ve cifras
        bool  latinChords  = true;
        int   transpose    = 0;
    };

    static QVector<Slide> buildSlides(const Song &song, const BuildOptions &opt)
    {
        QVector<Slide> out;
        if (opt.titleSlide) {
            Slide s;
            s.kind = Slide::Title;
            s.title = song.title;
            QStringList head;
            if (!song.artist.isEmpty()) head << song.artist;
            if (!song.key.isEmpty())    head << QStringLiteral("Tono: %1").arg(song.key);
            if (song.bpm > 0)           head << QStringLiteral("%1 BPM").arg(song.bpm);
            s.lines.append(SlideLine(song.title));
            if (!head.isEmpty()) s.lines.append(SlideLine(head.join(QStringLiteral("  ·  "))));
            s.ref = QStringLiteral("song:%1").arg(song.id);
            out.append(s);
        }

        QVector<Section> sections = parse(song.lyrics);
        // Dividir secciones largas en bloques de maxLinesPerSlide.
        // v1.2.0: se conserva la AGRUPACIÓN por sección original (sectionBlocks):
        // el intercalado del coro (hinario) debe ocurrir tras cada VERSO
        // completo, no tras cada bloque paginado de un verso largo (el bloque
        // es solo un artefacto de paginación).
        QVector<QVector<Section>> sectionBlocks;
        QVector<Section> blocks;
        for (const Section &sec : sections) {
            QVector<Section> parts;
            if (sec.lines.size() <= opt.maxLinesPerSlide) {
                parts.append(sec);
            } else {
                for (int i = 0; i < sec.lines.size(); i += opt.maxLinesPerSlide) {
                    Section part;
                    part.tag = sec.tag;
                    const int end = qMin(i + opt.maxLinesPerSlide, sec.lines.size());
                    for (int j = i; j < end; ++j) part.lines.append(sec.lines.at(j));
                    parts.append(part);
                }
            }
            sectionBlocks.append(parts);
            blocks << parts;
        }
        auto isChorusTag = [](const QString &t) {
            return t.compare(QStringLiteral("coro"), Qt::CaseInsensitive) == 0;
        };

        // Localiza el primer coro para el modo hinario
        int chorusIdx = -1;
        for (int i = 0; i < blocks.size(); ++i)
            if (isChorusTag(blocks.at(i).tag)) { chorusIdx = i; break; }

        if (opt.chorusInterleave && chorusIdx >= 0) {
            // Modo Hinario (v1.2.0 corregido): el coro se intercala tras CADA
            // verso COMPLETO (todas sus slides paginadas) y los bloques de coro
            // sueltos se omiten para no cantarlo dos veces: V1 C V2 C.
            // CORRECCION v1.2.0 (a y b):
            //  (a) si el coro era más largo que maxLinesPerSlide se dividía en
            //      varios bloques y SOLO se intercalaba el primero — el resto
            //      del coro se perdía (continue los descartaba);
            //  (b) si un VERSO largo se dividía en bloques, el coro se
            //      intercalaba ENTRE los bloques del mismo verso (a mitad de
            //      la estrofa). Ahora se respeta la sección original.
            // CORRECCION v1.6.0 (M16):
            //  (c) canción cuyo ÚNICO bloque es [Coro] (cantos cortos): el
            //      bucle se saltaba todas las secciones y la letra entera
            //      desaparecía de la proyección → si no hay versos, caer al
            //      camino lineal;
            //  (d) letras con [Coro] repetido tras cada verso (formato común
            //      de letras importadas): se acumulaban TODOS los bloques de
            //      coro y se cantaban N veces seguidas tras cada verso (V1 C C
            //      V2 C C) → usar SOLO el primer bloque de coro.
            QVector<Section> chorusBlocks;
            QVector<Section> verseBlocks;
            for (int s = 0; s < sections.size(); ++s) {
                if (isChorusTag(sections.at(s).tag)) {
                    if (chorusBlocks.isEmpty())
                        chorusBlocks << sectionBlocks.at(s);   // (d) primer coro únicamente
                } else {
                    verseBlocks << sectionBlocks.at(s);
                }
            }
            if (verseBlocks.isEmpty()) {
                // (c) solo coros: proyección lineal tal cual (el coro no se pierde)
                for (const auto &group : sectionBlocks)
                    for (const Section &b : group)
                        out.append(sectionToSlide(b, song, opt));
            } else {
                for (const Section &b : verseBlocks) {
                    out.append(sectionToSlide(b, song, opt));
                    // coro tras CADA verso completo (V1 C V2 C)
                    for (const Section &ch : chorusBlocks)
                        out.append(sectionToSlide(ch, song, opt));
                }
            }
        } else {
            for (const auto &group : sectionBlocks)
                for (const Section &b : group)
                    out.append(sectionToSlide(b, song, opt));
        }

        if (opt.endBlank) {
            Slide s; s.kind = Slide::Blank; s.title = song.title;
            out.append(s);
        }
        return out;
    }

    // Texto plano de una slide (sin acordes), para listas/overlay web
    static QString plainText(const Slide &s)
    {
        QStringList out;
        for (const SlideLine &l : s.lines) out << l.text;
        return out.join(QChar('\n'));
    }

private:
    static void flush(QVector<Section> &sections, Section &current)
    {
        if (!current.lines.isEmpty()) sections.append(current);
    }

    static Slide sectionToSlide(const Section &sec, const Song &song, const BuildOptions &opt)
    {
        Slide s;
        s.kind = Slide::Text;
        s.title = song.title;
        s.refLabel = sec.tag;
        s.ref = QStringLiteral("song:%1").arg(song.id);
        for (const SlideLine &l : sec.lines) {
            if (opt.stripChords)
                s.lines.append(SlideLine(l.text));
            else
                s.lines.append(SlideLine(l.text,
                    l.chords.isEmpty() ? QString() : Chords::transposeLine(l.chords, opt.transpose, opt.latinChords)));
        }
        return s;
    }
};

#endif // LUMINA_LYRICS_H
