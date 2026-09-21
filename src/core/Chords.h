// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Chords.h : Transposicion de acordes/cifras musicales en tiempo real
//  para el Stage View. Soporta notacion anglosajona (C, D, E...) y
//  latina (Do, Re, Mi...), con alteraciones (#, b) y sufijos (m, 7, sus...).
// ============================================================================
#ifndef LUMINA_CHORDS_H
#define LUMINA_CHORDS_H

#include <QString>
#include <QStringList>
#include <QRegularExpression>

class Chords
{
public:
    static const QStringList &anglo()
    {
        static const QStringList a = { QStringLiteral("c"), QStringLiteral("c#"), QStringLiteral("db"),
                                       QStringLiteral("d"), QStringLiteral("d#"), QStringLiteral("eb"),
                                       QStringLiteral("e"), QStringLiteral("f"), QStringLiteral("f#"),
                                       QStringLiteral("gb"), QStringLiteral("g"), QStringLiteral("g#"),
                                       QStringLiteral("ab"), QStringLiteral("a"), QStringLiteral("a#"),
                                       QStringLiteral("bb"), QStringLiteral("b") };
        return a;
    }

    // Convierte un nombre de nota a semitono relativo a C (0..11). -1 si invalido.
    static int noteToSemitone(const QString &noteRaw)
    {
        const QString n = noteRaw.trimmed();
        if (n.isEmpty()) return -1;
        static const QStringList latin = { QStringLiteral("do"), QStringLiteral("do#"), QStringLiteral("reb"),
                                           QStringLiteral("re"), QStringLiteral("re#"), QStringLiteral("mib"),
                                           QStringLiteral("mi"), QStringLiteral("fa"), QStringLiteral("fa#"),
                                           QStringLiteral("solb"), QStringLiteral("sol"), QStringLiteral("sol#"),
                                           QStringLiteral("lab"), QStringLiteral("la"), QStringLiteral("la#"),
                                           QStringLiteral("sib"), QStringLiteral("si") };
        static const int latinSem[17] = { 0, 1, 1, 2, 3, 3, 4, 5, 6, 6, 7, 8, 8, 9, 10, 10, 11 };
        const QString lower = n.toLower();
        for (int i = 0; i < latin.size(); ++i)
            if (lower == latin.at(i)) return latinSem[i];
        for (int i = 0; i < anglo().size(); ++i)
            if (lower == anglo().at(i)) return angloSemitone(i);
        // Nota simple anglosajona de una letra
        const QChar c = lower.at(0);
        int idx = QStringLiteral("cdefgab").indexOf(c);
        if (idx >= 0) {
            int base = angloSemitone(idx * 2);
            if (lower.size() > 1) {
                if (lower.at(1) == QChar('#')) base += 1;
                else if (lower.at(1) == QChar('b')) base -= 1;
            }
            return ((base % 12) + 12) % 12;
        }
        return -1;
    }

    static QString semitoneToNote(int semi, bool latinNotation)
    {
        semi = ((semi % 12) + 12) % 12;
        if (latinNotation) {
            static const QStringList l = { QStringLiteral("Do"), QStringLiteral("Do#"), QStringLiteral("Re"),
                                           QStringLiteral("Re#"), QStringLiteral("Mi"), QStringLiteral("Fa"),
                                           QStringLiteral("Fa#"), QStringLiteral("Sol"), QStringLiteral("Sol#"),
                                           QStringLiteral("La"), QStringLiteral("La#"), QStringLiteral("Si") };
            return l.at(semi);
        }
        static const QStringList a = { QStringLiteral("C"), QStringLiteral("C#"), QStringLiteral("D"),
                                       QStringLiteral("D#"), QStringLiteral("E"), QStringLiteral("F"),
                                       QStringLiteral("F#"), QStringLiteral("G"), QStringLiteral("G#"),
                                       QStringLiteral("A"), QStringLiteral("A#"), QStringLiteral("B") };
        return a.at(semi);
    }

    // Determina si una linea completa es una linea de acordes (cifrado)
    static bool isChordLine(const QString &line)
    {
        const QString t = line.trimmed();
        if (t.isEmpty()) return false;
        // Tokeniza por espacios; casi todos los tokens deben ser acordes validos
        const QStringList tokens = t.split(QRegularExpression(QStringLiteral("\\s+")), Qt::SkipEmptyParts);
        if (tokens.size() > 12) return false;
        int ok = 0;
        for (const QString &tk : tokens) {
            if (isChordToken(tk)) ok++;
        }
        return ok == tokens.size() && ok >= 1;
    }

    static bool isChordToken(const QString &tk)
    {
        if (tk.isEmpty() || tk.size() > 10) return false;
        static const QRegularExpression re(
            QStringLiteral("^(?:[A-G](?:#|b)?|(?:Do|Re|Mi|Fa|Sol|La|Si)(?:#|b)?)(?:m|maj|min|dim|aug|sus|add)?[0-9]*(?:/[A-G](?:#|b)?|(?:Do|Re|Mi|Fa|Sol|La|Si)(?:#|b)?)?$"),
            QRegularExpression::CaseInsensitiveOption);
        return re.match(tk).hasMatch();
    }

    // Transpone una linea de acordes completa 'semi' semitonos.
    static QString transposeLine(const QString &chordLine, int semi, bool latinNotation)
    {
        if (semi == 0 || chordLine.trimmed().isEmpty()) return chordLine;
        // Alineacion preservada: reemplaza token a token rellenando con espacios
        QString out;
        out.reserve(chordLine.size());
        int i = 0;
        const int n = chordLine.size();
        while (i < n) {
            QChar c = chordLine.at(i);
            if (c.isSpace()) { out += c; ++i; continue; }
            int j = i;
            while (j < n && !chordLine.at(j).isSpace()) ++j;
            const QString token = chordLine.mid(i, j - i);
            out += transposeChordToken(token, semi, latinNotation);
            i = j;
        }
        return out;
    }

    // Transpone un token de acorde; devuelve el original si no es acorde valido
    static QString transposeChordToken(const QString &tk, int semi, bool latinNotation)
    {
        if (!isChordToken(tk)) return tk;
        static const QRegularExpression head(
            QStringLiteral("^(Do|Re|Mi|Fa|Sol|La|Si|[A-G])(#|b)?"),
            QRegularExpression::CaseInsensitiveOption);
        QRegularExpressionMatch m = head.match(tk);
        if (!m.hasMatch()) return tk;
        QString base = m.captured(1);
        const QString alter = m.captured(2);
        const int srcSemi = noteToSemitone(base + alter);
        if (srcSemi < 0) return tk;
        const int dst = ((srcSemi + semi) % 12 + 12) % 12;
        return semitoneToNote(dst, latinNotation) + tk.mid(m.capturedLength(0));
    }

private:
    // Tabla para pares (#/b) de la lista anglo: c c# db d d# eb e f f# gb g g# ab a a# bb b
    static int angloSemitone(int idx)
    {
        static const int map[17] = { 0, 1, 1, 2, 3, 3, 4, 5, 6, 6, 7, 8, 8, 9, 10, 10, 11 };
        return map[idx];
    }
};

#endif // LUMINA_CHORDS_H
