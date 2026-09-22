// ============================================================================
//  Fusion-HP / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  BibleBib.cpp : parser .BIB con detección automática (docs/bib-format.md).
//    * UTF-8 con BOM opcional; CP1252/Latin-1 detectada (bytes UTF-8
//      inválidos + acentos latinos típicos) y convertida a UTF-8 con una
//      tabla explícita y determinista (0x80..0x9F de CP1252 + Latin-1).
//    * Directivas de cabecera: líneas que empiezan por '#'
//      (#BIB/#VERSION/#NAME/#BOOKS — #BIB y #BOOKS informativas).
//    * Filas "libro<sep>cap<sep>ver<sep>texto" con sep = TAB, " | " o ";;"
//      detectado por mayoría de líneas de datos.
//    * Variante por línea "Libro cap:ver texto…" (o "Libro cap texto…")
//      aceptada si las filas con columnas no alcanzan el 50 % de las líneas
//      de datos.
//    * El libro se normaliza con BibleRef::Resolve (número canónico 1..66);
//      si no resuelve → errors++ y la fila se descarta (sin abortar).
//    * maxBytes: si el búfer excede el límite, devuelve false.
// ============================================================================
#include "BibleBib.h"

#include "BibleRef.h"
#include "Utf8.h"

#include <set>

namespace fusion {
namespace {

/* ------------------------- codificación CP1252 ------------------------- */

// Byte CP1252 0x80..0x9F → Unicode (tabla explícita; los 5 bytes sin
// asignación 0x81/0x8D/0x8F/0x90/0x9D se mapean a su control C1 — mapeo
// determinista 1 byte → 1 codepoint). 0xA0..0xFF es Latin-1 idéntico.
uint32_t Cp1252ToCodepoint(unsigned char b) {
    static const uint16_t kHigh[0x20] = {
        0x20AC, 0x0081, 0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021,
        0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0x008D, 0x017D, 0x008F,
        0x0090, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014,
        0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0x009D, 0x017E, 0x0178
    };
    if (b < 0x80) return b;
    if (b < 0xA0) return kHigh[b - 0x80];
    return b;                                  // 0xA0..0xFF = Latin-1
}

void AppendUtf8(std::string& out, uint32_t cp) {
    if (cp < 0x80) {
        out += (char)cp;
    } else if (cp < 0x800) {
        out += (char)(0xC0 | (cp >> 6));
        out += (char)(0x80 | (cp & 0x3F));
    } else if (cp < 0x10000) {
        out += (char)(0xE0 | (cp >> 12));
        out += (char)(0x80 | ((cp >> 6) & 0x3F));
        out += (char)(0x80 | (cp & 0x3F));
    } else {
        out += (char)(0xF0 | (cp >> 18));
        out += (char)(0x80 | ((cp >> 12) & 0x3F));
        out += (char)(0x80 | ((cp >> 6) & 0x3F));
        out += (char)(0x80 | (cp & 0x3F));
    }
}

std::string Cp1252ToUtf8(const std::string& in) {
    std::string out;
    out.reserve(in.size() + in.size() / 8);
    for (size_t i = 0; i < in.size(); ++i)
        AppendUtf8(out, Cp1252ToCodepoint((unsigned char)in[i]));
    return out;
}

// ¿Hay bytes que NO decodifican como UTF-8 válido?
bool HasInvalidUtf8(const std::string& s) {
    size_t i = 0;
    while (i < s.size()) {
        if (DecodeUtf8(s, i) == 0xFFFD)
            return true;
    }
    return false;
}

// Acentos latinos y signos tipográficos típicos de CP1252 (0xC0..0xFF salvo
// ×/÷, más comillas/guiones del rango 0x80..0x9F).
bool HasCp1252Accents(const std::string& s) {
    for (size_t i = 0; i < s.size(); ++i) {
        const unsigned char b = (unsigned char)s[i];
        if (b >= 0xC0 && b != 0xD7 && b != 0xF7)
            return true;
        if (b >= 0x80 && b <= 0x9F) {
            switch (b) {
                case 0x82: case 0x84: case 0x85: case 0x8B:
                case 0x91: case 0x92: case 0x93: case 0x94:
                case 0x96: case 0x97: case 0x9B:
                    return true;
                default:
                    break;
            }
        }
    }
    return false;
}

/* ---------------------------- utilidades ------------------------------- */

bool IsAllDigits(const std::string& s) {
    if (s.empty()) return false;
    for (size_t i = 0; i < s.size(); ++i)
        if (s[i] < '0' || s[i] > '9') return false;
    return true;
}

long ParseLong(const std::string& s) {
    long v = 0;
    for (size_t i = 0; i < s.size(); ++i) {
        if (s[i] < '0' || s[i] > '9') return 0;
        v = v * 10 + (s[i] - '0');
        if (v > 1000000) v = 1000000;
    }
    return v;
}

// Recorta un texto largo respetando codepoints UTF-8 y agrega "…".
std::string TruncateUtf8(const std::string& s, size_t maxBytes) {
    if (s.size() <= maxBytes) return s;
    std::string out;
    size_t i = 0;
    while (i < s.size()) {
        size_t next = i;
        (void)DecodeUtf8(s, next);            // avanza un codepoint completo
        if (next > maxBytes) break;           // no cabe completo
        out.append(s, i, next - i);
        i = next;
    }
    out += "…";
    return out;
}

/* ------------------------- separadores de fila ------------------------- */

enum Sep { kSepTab = 0, kSepPipe = 1, kSepSemi = 2, kSepNone = 3 };

size_t FindSep(const std::string& s, size_t from, Sep sep) {
    switch (sep) {
        case kSepTab:  return s.find('\t', from);
        case kSepPipe: return s.find(" | ", from);
        default:       return s.find(";;", from);
    }
}

size_t SepLen(Sep sep) {
    return sep == kSepTab ? 1 : (sep == kSepPipe ? 3 : 2);
}

const char* SepName(Sep sep) {
    switch (sep) {
        case kSepTab:  return "tab";
        case kSepPipe: return "pipe";
        case kSepSemi: return "semicolon";
        default:       return "colon";       // variante por línea "Libro C:V"
    }
}

// Parsea una fila en variante por línea: "Libro cap:ver texto…" o
// "Libro cap texto…". Devuelve false (sin tocar nada) si no calza.
bool ParseColonRow(const std::string& ln, BibleRef::VerseRef* r, std::string* textOut) {
    std::string prefix;
    const size_t colon = ln.find(':');
    if (colon != std::string::npos) {
        // El prefijo termina en el primer espacio posterior a los dos puntos
        const size_t sp = ln.find(' ', colon);
        if (sp == std::string::npos)
            return false;
        prefix = Trim(ln.substr(0, sp));
        *textOut = Trim(ln.substr(sp + 1));
    } else {
        // "Libro cap texto…": el capítulo es el primer token numérico
        std::vector<std::string> toks;
        for (const std::string& tk : Split(ln, ' ')) {
            if (!tk.empty()) toks.push_back(tk);
        }
        size_t idx = std::string::npos;
        for (size_t i = 1; i < toks.size(); ++i) {   // índice 0 nunca es el cap.
            if (IsAllDigits(toks[i])) { idx = i; break; }
        }
        if (idx == std::string::npos)
            return false;
        for (size_t i = 0; i < idx; ++i) {
            if (i) prefix += ' ';
            prefix += toks[i];
        }
        for (size_t i = idx + 1; i < toks.size(); ++i) {
            if (!textOut->empty()) *textOut += ' ';
            *textOut += toks[i];
        }
    }
    if (prefix.empty() || textOut->empty())
        return false;
    *r = BibleRef::Resolve(prefix);        // "Juan 3:16" / "1 sam 3:16" / "Génesis 1"
    return r->Valid();                     // Valid exige chapter > 0
}

} // namespace

/* ---------------------------------------------------------------- Stats -- */

json BibleBib::Stats::ToJson() const {
    json j;
    j["ok"]        = 1;
    j["verses"]    = verses;
    j["books"]     = books;
    j["version"]   = version;
    j["name"]      = name;
    j["encoding"]  = encoding;
    j["separator"] = separator;
    j["sample"]    = json::array();
    for (const std::string& s : sample)
        j["sample"].push_back(s);
    j["errors"]    = errors;
    return j;
}

/* ---------------------------------------------------------------- Parse -- */

bool BibleBib::Parse(const std::string& bytes, Stats* stats,
                     std::vector<BibVerse>* verses, long long maxBytes) {
    if ((long long)bytes.size() > maxBytes)
        return false;
    Stats st;

    /* --------------------------- codificación -------------------------- */
    std::string text = bytes;
    if (text.size() >= 3 && (unsigned char)text[0] == 0xEF &&
        (unsigned char)text[1] == 0xBB && (unsigned char)text[2] == 0xBF) {
        text = text.substr(3);                       // BOM UTF-8
        st.encoding = "utf-8";
    } else if (HasInvalidUtf8(text) && HasCp1252Accents(text)) {
        text = Cp1252ToUtf8(text);                   // CP1252/Latin-1 → UTF-8
        st.encoding = "cp1252";
    } else {
        st.encoding = "utf-8";
    }

    /* ------------------------ líneas y cabecera ------------------------ */
    std::vector<std::string> dataLines;
    {
        for (std::string ln : Split(text, '\n')) {
            if (!ln.empty() && ln.back() == '\r') ln.pop_back();
            const std::string t = Trim(ln);
            if (t.empty()) continue;
            if (t[0] == '#') {
                // Directiva: #BIB / #VERSION / #NAME / #BOOKS (solo las dos
                // del medio aportan datos; el resto es informativa).
                const std::string dir = t.substr(1);
                const size_t sp = dir.find(' ');
                const std::string key = ToLowerAscii(
                    sp == std::string::npos ? dir : dir.substr(0, sp));
                const std::string val =
                    sp == std::string::npos ? std::string() : Trim(dir.substr(sp + 1));
                if (key == "version")    st.version = val;
                else if (key == "name")  st.name = val;
                continue;
            }
            dataLines.push_back(t);
        }
    }

    /* ---------------------- detección de separador --------------------- */
    int counts[3] = { 0, 0, 0 };
    if (!dataLines.empty()) {
        for (const std::string& ln : dataLines) {
            if (ln.find('\t')    != std::string::npos) ++counts[kSepTab];
            if (ln.find(" | ")   != std::string::npos) ++counts[kSepPipe];
            if (ln.find(";;")    != std::string::npos) ++counts[kSepSemi];
        }
    }
    // Mayoría de líneas de datos; empate → gana el de índice menor
    // (TAB > " | " > ";;", orden de chequeo de la primera línea del doc).
    Sep sep = kSepNone;
    int best = 0;
    for (int s = 0; s < 3; ++s) {
        if (counts[s] > best) { best = counts[s]; sep = (Sep)s; }
    }

    // ¿Cuántas líneas de datos tendrían 4 columnas con ese separador?
    size_t colLines = 0;
    if (sep != kSepNone) {
        for (const std::string& ln : dataLines) {
            const size_t p1 = FindSep(ln, 0, sep);
            if (p1 == std::string::npos) continue;
            const size_t p2 = FindSep(ln, p1 + SepLen(sep), sep);
            if (p2 == std::string::npos) continue;
            const size_t p3 = FindSep(ln, p2 + SepLen(sep), sep);
            if (p3 != std::string::npos) ++colLines;
        }
    }
    // Variante por línea si las filas con columnas no alcanzan el 50 %.
    const bool columnMode = (sep != kSepNone) && (colLines * 2 >= dataLines.size());

    st.separator = dataLines.empty() ? std::string()
                                     : std::string(SepName(columnMode ? sep : kSepNone));

    /* ----------------------------- filas ------------------------------- */
    std::vector<BibVerse> parsed;
    std::set<int> bookSet;
    for (const std::string& ln : dataLines) {
        if (columnMode) {
            // libro<sep>cap<sep>ver<sep>texto — el texto conserva cualquier
            // separador interno (todo lo que sigue a la 3.ª columna).
            const size_t p1 = FindSep(ln, 0, sep);
            if (p1 == std::string::npos) { ++st.errors; continue; }
            const size_t p2 = FindSep(ln, p1 + SepLen(sep), sep);
            if (p2 == std::string::npos) { ++st.errors; continue; }
            const size_t p3 = FindSep(ln, p2 + SepLen(sep), sep);
            if (p3 == std::string::npos) { ++st.errors; continue; }
            const std::string bookS = Trim(ln.substr(0, p1));
            const std::string capS =
                Trim(ln.substr(p1 + SepLen(sep), p2 - (p1 + SepLen(sep))));
            const std::string verS =
                Trim(ln.substr(p2 + SepLen(sep), p3 - (p2 + SepLen(sep))));
            const std::string textS = Trim(ln.substr(p3 + SepLen(sep)));
            if (bookS.empty() || capS.empty() || verS.empty() || textS.empty() ||
                !IsAllDigits(capS) || !IsAllDigits(verS)) {
                ++st.errors;                         // columnas insuficientes/no numéricas
                continue;
            }
            const BibleRef::VerseRef r = BibleRef::Resolve(bookS);
            if (r.book <= 0) {                       // libro no resoluble
                ++st.errors;
                continue;
            }
            BibVerse v;
            v.book     = r.book;                 // número canónico 1..66
            v.bookName = r.bookName;
            v.chapter  = (int)ParseLong(capS);
            v.verse    = (int)ParseLong(verS);
            v.text     = textS;
            bookSet.insert(v.book);
            parsed.push_back(std::move(v));
        } else {
            // Variante por línea: "Libro cap:ver texto…" / "Libro cap texto…"
            BibleRef::VerseRef r;
            std::string textS;
            if (!ParseColonRow(ln, &r, &textS)) {
                ++st.errors;
                continue;
            }
            BibVerse v;
            v.book     = r.book;
            v.bookName = r.bookName;
            v.chapter  = r.chapter;
            v.verse    = r.verse;
            v.text     = textS;
            bookSet.insert(v.book);
            parsed.push_back(std::move(v));
        }
    }

    st.verses = (int)parsed.size();
    st.books  = (int)bookSet.size();
    for (size_t i = 0; i < parsed.size() && st.sample.size() < 3; ++i) {
        const BibVerse& v = parsed[i];
        std::string s = v.bookName + " " + std::to_string(v.chapter);
        if (v.verse > 0) s += ":" + std::to_string(v.verse);
        s += " " + v.text;
        st.sample.push_back(TruncateUtf8(s, 96));
    }

    if (verses) *verses = std::move(parsed);
    if (stats) *stats = st;
    return true;
}

} // namespace fusion
