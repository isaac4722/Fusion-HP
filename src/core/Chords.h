// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Chords.h : Transposicion de acordes/cifras musicales en tiempo real.
//  Soporta notacion anglosajona (C, D, E...) y latina (Do, Re, Mi...), con
//  alteraciones (#, b), sufijos (m, 7, sus...) y bajo slash ("Sol/Fa", "C/E").
//  Port de la edicion Qt (v1.6.0) — misma tabla de semitonos y sufijos.
// ============================================================================
#ifndef LUMINA_CHORDS_H
#define LUMINA_CHORDS_H

#include <wx/string.h>
#include <wx/tokenzr.h>

#include <vector>

class Chords
{
public:
    // Semitono relativo a C (0..11) de una nota; -1 si invalida.
    static int NoteToSemitone(const wxString &noteRaw)
    {
        const wxString n = wxString(noteRaw).Trim(true).Trim(false).Lower();
        if (n.empty())
            return -1;
        // Notacion latina (la mas larga primero: "solb", "sol#"...)
        struct LatinEntry { const char *name; int semi; };
        static const LatinEntry latin[] = {
            { "solb", 6 }, { "sol#", 8 }, { "sol", 7 },
            { "do#", 1 },  { "dob", -1 }, { "do", 0 },
            { "reb", 1 },  { "re#", 3 },  { "re", 2 },
            { "mib", 3 },  { "mi", 4 },
            { "fa#", 6 },  { "fab", 4 },  { "fa", 5 },
            { "lab", 8 },  { "la#", 10 }, { "la", 9 },
            { "sib", 10 }, { "si", 11 },
        };
        for (const LatinEntry &e : latin) {
            if (n == e.name && e.semi >= 0)
                return e.semi;
        }
        // Notacion anglosajona completa (con enarmonicos)
        struct AngloEntry { const char *name; int semi; };
        static const AngloEntry anglo[] = {
            { "c", 0 }, { "c#", 1 }, { "db", 1 }, { "d", 2 }, { "d#", 3 },
            { "eb", 3 }, { "e", 4 }, { "f", 5 }, { "f#", 6 }, { "gb", 6 },
            { "g", 7 }, { "g#", 8 }, { "ab", 8 }, { "a", 9 }, { "a#", 10 },
            { "bb", 10 }, { "b", 11 },
        };
        for (const AngloEntry &e : anglo) {
            if (n == e.name)
                return e.semi;
        }
        // Nota simple con alteracion arbitraria ("b#", "fb"...)
        if (n.Length() >= 1 && wxString("cdefgab").Find(n[0]) != wxNOT_FOUND) {
            static const int natSemi[7] = { 0, 2, 4, 5, 7, 9, 11 };
            int idx = wxString("cdefgab").Find(n[0]);
            int base = natSemi[idx];
            if (n.Length() > 1) {
                if (n[1] == '#') base += 1;
                else if (n[1] == 'b') base -= 1;
            }
            return ((base % 12) + 12) % 12;
        }
        return -1;
    }

    static wxString SemitoneToNote(int semi, bool latinNotation)
    {
        semi = ((semi % 12) + 12) % 12;
        if (latinNotation) {
            static const wxString l[12] = { "Do", "Do#", "Re", "Re#", "Mi", "Fa",
                                            "Fa#", "Sol", "Sol#", "La", "La#", "Si" };
            return l[semi];
        }
        static const wxString a[12] = { "C", "C#", "D", "D#", "E", "F",
                                        "F#", "G", "G#", "A", "A#", "B" };
        return a[semi];
    }

    // Valida un token de acorde: raiz (latina o anglo) + alteracion +
    // sufijo (m/maj/min/dim/aug/sus/add + digitos) + bajo opcional "/nota".
    static bool IsChordToken(const wxString &tk)
    {
        wxString root, tail, bass;
        int rootSemi = 0;
        return ParseToken(tk, &root, &rootSemi, &tail, &bass);
    }

    // Determina si una linea completa es una linea de acordes (cifrado).
    static bool IsChordLine(const wxString &line)
    {
        const wxString t = wxString(line).Trim(true).Trim(false);
        if (t.empty())
            return false;
        wxStringTokenizer tok(t, " \t");
        const int count = tok.CountTokens();
        if (count > 12 || count < 1)
            return false;
        int ok = 0;
        while (tok.HasMoreTokens()) {
            if (IsChordToken(tok.GetNextToken()))
                ok++;
        }
        return ok == count;
    }

    // Transpone una linea de acordes completa 'semi' semitonos, conservando
    // la alineacion espacial (reemplazo token a token con relleno).
    static wxString TransposeLine(const wxString &chordLine, int semi, bool latinNotation)
    {
        if (semi == 0 || wxString(chordLine).Trim(true).Trim(false).empty())
            return chordLine;
        wxString out;
        out.reserve(chordLine.size());
        int i = 0;
        const int n = (int)chordLine.Length();
        while (i < n) {
            const wxChar c = chordLine[i];
            if (c == ' ' || c == '\t') {
                out += c;
                ++i;
                continue;
            }
            int j = i;
            while (j < n && chordLine[j] != ' ' && chordLine[j] != '\t')
                ++j;
            const wxString token = chordLine.Mid(i, j - i);
            const wxString transposed = TransposeChordToken(token, semi, latinNotation);
            out += transposed;
            // Relleno para conservar columnas del cifrado
            for (int k = (int)transposed.Length(); k < (int)token.Length(); ++k)
                out += ' ';
            i = j;
        }
        return out;
    }

    // Transpone un token de acorde; devuelve el original si no es acorde valido.
    static wxString TransposeChordToken(const wxString &tk, int semi, bool latinNotation)
    {
        wxString root, tail, bass;
        int rootSemi = -1;
        if (!ParseToken(tk, &root, &rootSemi, &tail, &bass))
            return tk;
        const int dst = ((rootSemi + semi) % 12 + 12) % 12;
        wxString out = SemitoneToNote(dst, latinNotation) + tail;
        if (!bass.empty()) {
            wxString bassRoot, bassTail;
            int bassSemi = -1;
            if (ParseToken(bass, &bassRoot, &bassSemi, &bassTail, nullptr))
                bass = SemitoneToNote(((bassSemi + semi) % 12 + 12) % 12, latinNotation) + bassTail;
            out += "/" + bass;
        }
        return out;
    }

private:
    // Parsea "Root[alter][suffix][/bass]".
    //   rootOut/rootSemi : raiz reconocida
    //   tailOut          : sufijo posterior a la raiz+alteracion (antes del /)
    //   bassOut          : nota del bajo tras la barra (puede ser nullptr)
    static bool ParseToken(const wxString &tk, wxString *rootOut, int *rootSemi,
                           wxString *tailOut, wxString *bassOut)
    {
        if (tk.empty() || tk.Length() > 10)
            return false;
        // Sufijo valido: letras del conjunto m/M/a/j/i/n/d/s/u y digitos
        static const wxString okChars = "mMajindsu0123456789";
        wxString work = tk;
        wxString bass;
        const int slash = work.Find('/', true);
        if (slash != wxNOT_FOUND) {
            bass = work.Mid(slash + 1);
            work = work.Left(slash);
            if (bass.empty())
                return false;
        }
        if (work.empty())
            return false;
        // Validacion de sufijo posterior a la raiz
        // (la raiz se detecta primero; aqui se comprueba el resto)
        // Raiz latina (probar las mas largas primero)
        static const wxString latinRoots[7] = { "sol", "do", "re", "mi", "fa", "la", "si" };
        static const int latinSemis[7] = { 7, 0, 2, 4, 5, 9, 11 };
        const wxString lower = work.Lower();
        int semi = -1;
        size_t rootLen = 0;
        for (int i = 0; i < 7; ++i) {
            if (lower.StartsWith(latinRoots[i])) {
                semi = latinSemis[i];
                rootLen = latinRoots[i].Length();
                break;
            }
        }
        // Raiz anglosajona (letra unica A-G): a=9, b=11, c=0, d=2, e=4, f=5, g=7
        if (semi < 0 && lower[0] >= 'a' && lower[0] <= 'g') {
            static const int semiForAG[7] = { 9, 11, 0, 2, 4, 5, 7 };
            semi = semiForAG[(int)(lower[0] - 'a')];
            rootLen = 1;
        }
        if (semi < 0)
            return false;
        // Alteracion
        if (rootLen < lower.Length() && (lower[rootLen] == '#' || lower[rootLen] == 'b')) {
            if (lower[rootLen] == '#')
                semi += 1;
            else
                semi -= 1;
            ++rootLen;
        }
        // En latinos, "b" alteracion vs "si(b)"… "dob/reb" ya cubiertos; ok.
        semi = ((semi % 12) + 12) % 12;
        // Sufijo: caracteres validos solamente
        const wxString suffix = lower.Mid(rootLen);
        for (size_t k = 0; k < suffix.Length(); ++k) {
            if (okChars.Find(suffix[k]) == wxNOT_FOUND)
                return false;
        }
        // No puede ser una palabra de texto comun que empiece igual (ej: "Fa" de una
        // letra con mayuscula). Heuristica del port original: tokens de 1-2 letras
        // sin sufijo son acordes solo si la linea completa fue detectada como cifrado
        // (IsChordLine ya exige tokens validos en toda la linea).
        if (rootOut)   *rootOut = work.Left(rootLen);
        if (rootSemi)  *rootSemi = semi;
        if (tailOut)   *tailOut = work.Mid(rootLen);
        if (bassOut)   *bassOut = bass;
        return true;
    }
};

#endif // LUMINA_CHORDS_H
