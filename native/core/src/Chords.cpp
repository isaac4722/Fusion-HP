// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Chords.cpp : transposición de acordes/cifrado en tiempo real (port 1:1 de
//  apps/native-wx-src/core/Chords.h — edición wx v2.0.0, heredada de Qt v1.6.0).
//  Misma tabla latina (solb=6, sol#=8, sol=7, do#=1, do=0…), misma anglosajona
//  (c=0…b=11) y la corrección histórica del port wx para raíces por letra
//  A-G (a=9, b=11, c=0, d=2, e=4, f=5, g=7). Sufijos válidos: caracteres de
//  "mMajindsu0123456789"; bajo con '/'; longitud máxima de token: 10.
//  Los tokens de acorde son ASCII: la longitud en bytes equivale a la
//  wxString::Length() del port wx.
// ============================================================================
#include "Chords.h"

#include "Utf8.h"

#include <vector>

namespace lumina {
namespace {

std::string LowerAsciiCopy(const std::string& s) {
    std::string o = s;
    for (size_t i = 0; i < o.size(); ++i)
        if (o[i] >= 'A' && o[i] <= 'Z') o[i] = (char)(o[i] - 'A' + 'a');
    return o;
}

// Tokeniza por espacios/tabuladores SIN producir tokens vacíos
// (equivalente a wxStringTokenizer con delimitadores " \t").
std::vector<std::string> SplitWhitespace(const std::string& s) {
    std::vector<std::string> out;
    std::string cur;
    for (size_t i = 0; i <= s.size(); ++i) {
        const char c = (i < s.size()) ? s[i] : ' ';
        if (c == ' ' || c == '\t') {
            if (!cur.empty()) { out.push_back(cur); cur.clear(); }
        } else {
            cur += c;
        }
    }
    return out;
}

/* --------------------------------------------------------- ParseToken -- */

// Parsea "Root[alter][suffix][/bass]" (port de Chords::ParseToken de wx).
//   rootOut/rootSemi : raíz reconocida (rootOut conserva el caso original)
//   tailOut          : sufijo posterior a la raíz+alteración (antes del '/')
//   bassOut          : nota del bajo tras la barra (puede ser nullptr)
bool ParseToken(const std::string& tk, std::string* rootOut, int* rootSemi,
                std::string* tailOut, std::string* bassOut) {
    if (tk.empty() || tk.size() > 10)
        return false;
    // Sufijo válido: letras del conjunto m/M/a/j/i/n/d/s/u y dígitos
    static const char* const kOkChars = "mMajindsu0123456789";
    std::string work = tk;
    std::string bass;
    const size_t slash = work.find_last_of('/');
    if (slash != std::string::npos) {
        bass = work.substr(slash + 1);
        work = work.substr(0, slash);
        if (bass.empty())
            return false;
    }
    if (work.empty())
        return false;
    const std::string lower = LowerAsciiCopy(work);

    // Raíz latina (probar las más largas primero: "sol" antes que "s", etc.)
    static const char* const kLatinRoots[7] = { "sol", "do", "re", "mi", "fa", "la", "si" };
    static const int        kLatinSemis[7]  = { 7, 0, 2, 4, 5, 9, 11 };
    int semi = -1;
    size_t rootLen = 0;
    for (int i = 0; i < 7; ++i) {
        const std::string r = kLatinRoots[i];
        if (lower.compare(0, r.size(), r) == 0) {   // prefijo (exige longitud)
            semi = kLatinSemis[i];
            rootLen = r.size();
            break;
        }
    }
    // Raíz anglosajona (letra única A-G): a=9, b=11, c=0, d=2, e=4, f=5, g=7
    // (corrección histórica del port wx — el mapeo venía intercambiado).
    if (semi < 0 && lower[0] >= 'a' && lower[0] <= 'g') {
        static const int kSemiForAG[7] = { 9, 11, 0, 2, 4, 5, 7 };
        semi = kSemiForAG[(size_t)(lower[0] - 'a')];
        rootLen = 1;
    }
    if (semi < 0)
        return false;
    // Alteración (# o b) inmediatamente después de la raíz
    if (rootLen < lower.size() && (lower[rootLen] == '#' || lower[rootLen] == 'b')) {
        semi += (lower[rootLen] == '#') ? 1 : -1;
        ++rootLen;
    }
    // En latinos, "b" alteración vs "si(b)": "dob"/"reb" ya quedan cubiertos.
    semi = ((semi % 12) + 12) % 12;
    // Sufijo: además del conjunto de caracteres, el sufijo NO vacío debe
    // EMPEZAR como un sufijo real de acorde (m/M/#/b/dígito o sus/add/dim/aug).
    // Sin esta puerta, palabras españolas comunes pasaban por acordes:
    //   "dos"  = Do+"s" · "mis" = Mi+"s" · "fue" = F+"ue" · "das" = D+"as"
    // (mejora v3.0.0 sobre el port wx; los acordes reales — Am, G7, Csus4,
    //  Cadd9, Cdim, Fa#m7, Do/Fa… — siguen reconocidos igual).
    const std::string suffix = lower.substr(rootLen);
    if (!suffix.empty()) {
        const char c0 = suffix[0];
        const bool okStart =
            c0 == 'm' || c0 == 'M' || c0 == '#' || c0 == 'b' ||
            (c0 >= '0' && c0 <= '9') ||
            StartsWithCI(suffix, "sus") || StartsWithCI(suffix, "add") ||
            StartsWithCI(suffix, "dim") || StartsWithCI(suffix, "aug");
        if (!okStart)
            return false;
    }
    // Sufijo: solo caracteres del conjunto válido (sobre el texto en minúsculas)
    for (size_t k = rootLen; k < lower.size(); ++k) {
        bool ok = false;
        for (const char* p = kOkChars; *p; ++p) {
            if (*p == lower[k]) { ok = true; break; }
        }
        if (!ok)
            return false;
    }
    // Nota: 'c' no está en kOkChars, así que "cinco"/"cuatro" (raíz C +
    // sufijo con 'c') se rechazan aquí además de por la puerta de arranque.
    // Heurística del port original: tokens de 1-2 letras sin sufijo son acordes
    // solo si la línea completa fue detectada como cifrado (IsChordLine ya
    // exige tokens válidos en toda la línea).
    if (rootOut)  *rootOut = work.substr(0, rootLen);
    if (rootSemi) *rootSemi = semi;
    if (tailOut)  *tailOut = work.substr(rootLen);
    if (bassOut)  *bassOut = bass;
    return true;
}

/* ------------------------------------------------------ NoteToSemitone - */

struct NoteEntry { const char* name; int semi; };

int NoteToSemitoneImpl(const std::string& noteRaw) {
    const std::string n = LowerAsciiCopy(Trim(noteRaw));
    if (n.empty())
        return -1;
    // Notación latina (la más larga primero: "solb", "sol#"...)
    static const NoteEntry kLatin[] = {
        { "solb", 6 }, { "sol#", 8 }, { "sol", 7 },
        { "do#", 1 },  { "dob", -1 }, { "do", 0 },
        { "reb", 1 },  { "re#", 3 },  { "re", 2 },
        { "mib", 3 },  { "mi", 4 },
        { "fa#", 6 },  { "fab", 4 },  { "fa", 5 },
        { "lab", 8 },  { "la#", 10 }, { "la", 9 },
        { "sib", 10 }, { "si", 11 },
    };
    for (const NoteEntry& e : kLatin) {
        // "dob" (-1) no casa aquí: cae al fallback de abajo, igual que wx.
        if (n == e.name && e.semi >= 0)
            return e.semi;
    }
    // Notación anglosajona completa (con enarmónicos)
    static const NoteEntry kAnglo[] = {
        { "c", 0 }, { "c#", 1 }, { "db", 1 }, { "d", 2 }, { "d#", 3 },
        { "eb", 3 }, { "e", 4 }, { "f", 5 }, { "f#", 6 }, { "gb", 6 },
        { "g", 7 }, { "g#", 8 }, { "ab", 8 }, { "a", 9 }, { "a#", 10 },
        { "bb", 10 }, { "b", 11 },
    };
    for (const NoteEntry& e : kAnglo) {
        if (n == e.name)
            return e.semi;
    }
    // Nota simple con alteración arbitraria ("b#", "fb"...)
    static const char kLetters[7] = { 'c', 'd', 'e', 'f', 'g', 'a', 'b' };
    static const int  kNatSemi[7] = { 0, 2, 4, 5, 7, 9, 11 };
    for (int i = 0; i < 7; ++i) {
        if (n[0] == kLetters[i]) {
            int base = kNatSemi[i];
            if (n.size() > 1) {
                if (n[1] == '#')      base += 1;
                else if (n[1] == 'b') base -= 1;
            }
            return ((base % 12) + 12) % 12;
        }
    }
    return -1;
}

/* -------------------------------------------------- TransposeChordToken */

std::string TransposeChordTokenImpl(const std::string& tk, int semi, bool latinNotation) {
    std::string root, tail, bass;
    int rootSemi = -1;
    if (!ParseToken(tk, &root, &rootSemi, &tail, &bass))
        return tk;                       // no es acorde válido: token original
    const int dst = ((rootSemi + semi) % 12 + 12) % 12;
    std::string out = Chords::SemitoneToNote(dst, latinNotation) + tail;
    if (!bass.empty()) {
        std::string bassRoot, bassTail;
        int bassSemi = -1;
        if (ParseToken(bass, &bassRoot, &bassSemi, &bassTail, nullptr))
            bass = Chords::SemitoneToNote(((bassSemi + semi) % 12 + 12) % 12, latinNotation) + bassTail;
        out += "/" + bass;
    }
    return out;
}

} // namespace

/* ------------------------------------------------------------ públicas - */

int Chords::NoteToSemitone(const std::string& noteRaw) {
    return NoteToSemitoneImpl(noteRaw);
}

std::string Chords::SemitoneToNote(int semi, bool latinNotation) {
    semi = ((semi % 12) + 12) % 12;
    static const char* const kLatin[12] = { "Do", "Do#", "Re", "Re#", "Mi", "Fa",
                                            "Fa#", "Sol", "Sol#", "La", "La#", "Si" };
    static const char* const kAnglo[12] = { "C", "C#", "D", "D#", "E", "F",
                                            "F#", "G", "G#", "A", "A#", "B" };
    return std::string(latinNotation ? kLatin[semi] : kAnglo[semi]);
}

bool Chords::IsChordToken(const std::string& tk) {
    std::string root, tail, bass;
    int rootSemi = 0;
    return ParseToken(tk, &root, &rootSemi, &tail, &bass);
}

bool Chords::IsChordLine(const std::string& line) {
    const std::string t = Trim(line);
    if (t.empty())
        return false;
    const std::vector<std::string> tokens = SplitWhitespace(t);
    // 1..12 tokens y TODOS válidos (igual que el port wx)
    if (tokens.empty() || tokens.size() > 12)
        return false;
    for (const std::string& tk : tokens) {
        if (!Chords::IsChordToken(tk))
            return false;
    }
    return true;
}

std::string Chords::TransposeLine(const std::string& chordLine, int semi, bool latinNotation) {
    if (semi == 0 || Trim(chordLine).empty())
        return chordLine;
    std::string out;
    out.reserve(chordLine.size());
    size_t i = 0;
    const size_t n = chordLine.size();
    while (i < n) {
        const char c = chordLine[i];
        if (c == ' ' || c == '\t') {
            out += c;
            ++i;
            continue;
        }
        size_t j = i;
        while (j < n && chordLine[j] != ' ' && chordLine[j] != '\t')
            ++j;
        const std::string token = chordLine.substr(i, j - i);
        const std::string transposed = TransposeChordTokenImpl(token, semi, latinNotation);
        out += transposed;
        // Relleno para conservar las columnas del cifrado
        for (size_t k = transposed.size(); k < token.size(); ++k)
            out += ' ';
        i = j;
    }
    return out;
}

std::string Chords::TransposeChordToken(const std::string& tk, int semi, bool latinNotation) {
    return TransposeChordTokenImpl(tk, semi, latinNotation);
}

} // namespace lumina
