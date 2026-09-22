// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  BibleRef.h : Tabla canonica de los 66 libros (nombres y abreviaturas en
//  espanol) y parser de referencias tipadas ("Jn 3:16", "salmo 23:1-6",
//  "1 co 13, 4-7"). Port de la edicion Qt (v1.6.0). Resolucion local < 1 ms.
// ============================================================================
#ifndef LUMINA_BIBLEREF_H
#define LUMINA_BIBLEREF_H

#include <wx/string.h>
#include <wx/tokenzr.h>
#include <wx/arrstr.h>

#include <vector>

class BibleRef
{
public:
    struct BookInfo
    {
        int number;                    // 1..66 canonico
        wxString name;                 // nombre completo
        std::vector<wxString> abbrs;   // abreviaturas aceptadas (minusculas)
    };

    struct VerseRef
    {
        int book = 0, chapter = 0, verse = 0;
        wxString bookName;
        bool Valid() const { return book > 0 && chapter > 0; }
    };

    // Elimina acentos/diacriticos espanoles (mapeo explicito, rapido y
    // determinista — sin dependencias de normalizacion Unicode).
    static wxString StripAccents(const wxString &s)
    {
        // Mapeo explicito wchar (independiente del locale del sistema)
        wxString out;
        out.reserve(s.size());
        for (size_t i = 0; i < s.Length(); ++i) {
            const wchar_t c = (wchar_t)s[i];
            switch (c) {
                case 0xE1: case 0xE0: case 0xE4: case 0xE2: case 0xE3: out += L'a'; break; // á à ä â ã
                case 0xE9: case 0xE8: case 0xEB: case 0xEA:              out += L'e'; break; // é è ë ê
                case 0xED: case 0xEC: case 0xEF: case 0xEE:              out += L'i'; break; // í ì ï î
                case 0xF3: case 0xF2: case 0xF6: case 0xF4: case 0xF5:   out += L'o'; break; // ó ò ö ô õ
                case 0xFA: case 0xF9: case 0xFC: case 0xFB:              out += L'u'; break; // ú ù ü û
                case 0xF1: out += L'n'; break;   // ñ
                case 0xE7: out += L'c'; break;   // ç
                case 0xC1: case 0xC0: case 0xC4: case 0xC2: case 0xC3: out += L'A'; break;
                case 0xC9: case 0xC8: case 0xCB: case 0xCA:            out += L'E'; break;
                case 0xCD: case 0xCC: case 0xCF: case 0xCE:            out += L'I'; break;
                case 0xD3: case 0xD2: case 0xD6: case 0xD4: case 0xD5: out += L'O'; break;
                case 0xDA: case 0xD9: case 0xDC: case 0xDB:            out += L'U'; break;
                case 0xD1: out += L'N'; break;
                case 0xC7: out += L'C'; break;
                default: out += c; break;
            }
        }
        return out;
    }

    // Equivalente de Qt SimplifyWhiteSpace(): recorta y colapsa espacios
    static wxString Simplified(const wxString &s)
    {
        wxString out = s;
        out.Trim(true).Trim(false);
        while (out.Replace("  ", " ") > 0) { }
        return out;
    }

    // Elimina TODOS los espacios
    static wxString WithoutSpaces(const wxString &s)
    {
        wxString out = s;
        out.Replace(" ", "");
        return out;
    }

    static const std::vector<BookInfo> &Books()
    {
        static const std::vector<BookInfo> table = BuildTable();
        return table;
    }

    // Resuelve una referencia tipada. Acepta:
    //   "Jn 3:16"  "juan 3:16-18"  "salmo 23"  "1 co 13:4-7"  "mt 5,6-7"
    //   "genesis 1"  "1 sam 3:16"  "1sam 3"
    static VerseRef Resolve(const wxString &input)
    {
        VerseRef out;
        wxString s = StripAccents(input);
        s.Replace(".", "");
        s = Simplified(s);
        s.MakeLower();
        if (s.empty())
            return out;

        // Separacion libro/resto por tokens: el libro son los tokens de solo
        // letras (u ordinales "1"/"2"/"3" seguidos de letras) que anteceden al
        // primer token con digitos de capitulo ("3:16", "13", "16-18"...).
        const wxArrayString tokens = wxStringTokenize(s, " ");
        wxArrayString bookTokens, restTokens;
        bool restStarted = false;
        for (size_t i = 0; i < tokens.size(); ++i) {
            const wxString &tk = tokens[i];
            if (restStarted) {
                restTokens.Add(tk);
                continue;
            }
            if (tk.IsNumber()) {                       // ordinal ("1","2","3")
                if (i + 1 < tokens.size() && HasLetters(tokens[i + 1]))
                    bookTokens.Add(tk);
                else
                    restStarted = true, restTokens.Add(tk);
                continue;
            }
            if (HasLetters(tk) && !tk.Contains(':'))
                bookTokens.Add(tk);
            else
                restStarted = true, restTokens.Add(tk);
        }
        wxString bookPart = wxJoin(bookTokens, ' ');
        wxString rest = wxJoin(restTokens, ' ');
        if (bookPart.empty())
            return out;

        const std::vector<BookInfo> &table = Books();
        int bookNum = 0;
        wxString bookName;
        // 1) coincidencia exacta de abreviatura (mas larga primero)
        for (const BookInfo &b : table) {
            for (const wxString &a : b.abbrs) {
                if (a == bookPart) { bookNum = b.number; bookName = b.name; break; }
            }
            if (bookNum) break;
        }
        // 2) prefijo unico de nombre completo (sin acentos ni espacios)
        if (!bookNum) {
            const wxString bp = WithoutSpaces(StripAccents(bookPart));
            for (const BookInfo &b : table) {
                const wxString nm = WithoutSpaces(StripAccents(b.name.Lower()));
                if (nm.StartsWith(bp)) { bookNum = b.number; bookName = b.name; break; }
            }
        }
        if (!bookNum)
            return out;

        out.book = bookNum;
        out.bookName = bookName;
        if (!rest.empty()) {
            rest.Replace(",", ":");
            rest = WithoutSpaces(rest);
            // Formatos: "3", "3:16", "3:16-18", "3:16-3:18"
            long chap = 0, v1 = 0, v2 = -1, chap2 = -1;
            const wxArrayString nums = NumberTokens(rest);
            if (nums.size() >= 1) nums[0].ToLong(&chap);
            if (nums.size() >= 2) nums[1].ToLong(&v1);
            if (nums.size() >= 3) nums[2].ToLong(&v2);
            if (nums.size() >= 4) { chap2 = v2; nums[3].ToLong(&v2); }
            out.chapter = (int)chap;
            if (chap2 >= 0)
                out.verse = (int)v1;      // "3:16-3:18" -> usa el primer versiculo
            else if (v2 >= 0)
                out.verse = (int)v1;
            else if (v1 > 0)
                out.verse = (int)v1;
            else
                out.verse = 0;
        }
        return out;
    }

    // Parsea "cap:v1-v2" de un texto libre; devuelve (chapter, from, to)
    struct Range { int chapter = 0, from = 0, to = 0; };
    static Range RangeOf(const wxString &input)
    {
        Range r;
        const wxArrayString nums = NumberTokens(input);
        if (nums.size() >= 2) {
            long ch = 0, from = 0;
            nums[0].ToLong(&ch);
            nums[1].ToLong(&from);
            r.chapter = (int)ch;
            r.from = (int)from;
            if (nums.size() >= 3) {
                long to = from;
                nums[2].ToLong(&to);
                r.to = (int)to;
            } else {
                r.to = r.from;
            }
            if (r.to < r.from)
                std::swap(r.from, r.to);
        }
        return r;
    }

    static wxString FormatRef(const VerseRef &r)
    {
        if (!r.Valid())
            return wxString();
        if (r.verse > 0)
            return wxString::Format("%s %d:%d", r.bookName, r.chapter, r.verse);
        return wxString::Format("%s %d", r.bookName, r.chapter);
    }

private:
    static bool HasLetters(const wxString &tk)
    {
        for (size_t i = 0; i < tk.Length(); ++i) {
            if (tk[i] >= 'a' && tk[i] <= 'z')
                return true;
        }
        return false;
    }

    // Extrae tokens numericos de un texto ("13:4-7" -> ["13","4","7"])
    static wxArrayString NumberTokens(const wxString &s)
    {
        wxArrayString out;
        wxString cur;
        for (size_t i = 0; i <= s.Length(); ++i) {
            const wxChar c = (i < s.Length()) ? wxChar(s[i]) : wxChar(' ');
            if (c >= '0' && c <= '9') {
                cur += c;
            } else if (!cur.empty()) {
                out.Add(cur);
                cur.clear();
            }
        }
        return out;
    }

    static void AddBook(std::vector<BookInfo> &t, int n, const wxString &name,
                        std::initializer_list<const char *> abbrs)
    {
        BookInfo b;
        b.number = n;
        b.name = name;
        for (const char *a : abbrs)
            b.abbrs.push_back(wxString(a).Lower());
        t.push_back(b);
    }

    static std::vector<BookInfo> BuildTable()
    {
        std::vector<BookInfo> t;
        AddBook(t, 1,  L"Génesis",        {"gn", "gen", "ge"});
        AddBook(t, 2,  L"Éxodo",          {"ex", "exo", "exod"});
        AddBook(t, 3,  L"Levítico",       {"lv", "lev"});
        AddBook(t, 4,  L"Números",        {"nm", "num"});
        AddBook(t, 5,  "Deuteronomio",   {"dt", "deu", "deut"});
        AddBook(t, 6,  L"Josué",          {"jos", "josu"});
        AddBook(t, 7,  "Jueces",         {"jue", "jueces"});
        AddBook(t, 8,  "Rut",            {"rt", "rut"});
        AddBook(t, 9,  "1 Samuel",       {"1s", "1sa", "1 sam", "1sam"});
        AddBook(t, 10, "2 Samuel",       {"2s", "2sa", "2 sam", "2sam"});
        AddBook(t, 11, "1 Reyes",        {"1r", "1re", "1 rey", "1rey"});
        AddBook(t, 12, "2 Reyes",        {"2r", "2re", "2 rey", "2rey"});
        AddBook(t, 13, L"1 Crónicas",     {"1cr", "1cro", "1 cr", "1cron"});
        AddBook(t, 14, L"2 Crónicas",     {"2cr", "2cro", "2 cr", "2cron"});
        AddBook(t, 15, "Esdras",         {"esd"});
        AddBook(t, 16, L"Nehemías",       {"neh"});
        AddBook(t, 17, "Ester",          {"est"});
        AddBook(t, 18, "Job",            {"job"});
        AddBook(t, 19, "Salmos",         {"sal", "salmo", "salmos", "ps"});
        AddBook(t, 20, "Proverbios",     {"pr", "prov", "pv"});
        AddBook(t, 21, L"Eclesiastés",    {"ec", "ecle"});
        AddBook(t, 22, "Cantares",       {"cnt", "cant"});
        AddBook(t, 23, L"Isaías",         {"is", "isa"});
        AddBook(t, 24, L"Jeremías",       {"jer"});
        AddBook(t, 25, "Lamentaciones",  {"lam"});
        AddBook(t, 26, "Ezequiel",       {"ez", "eze"});
        AddBook(t, 27, "Daniel",         {"dn", "dan"});
        AddBook(t, 28, "Oseas",          {"os", "ose"});
        AddBook(t, 29, "Joel",           {"jl", "joel"});
        AddBook(t, 30, L"Amós",           {"am", "amos"});
        AddBook(t, 31, L"Obadías",        {"ob", "obd"});
        AddBook(t, 32, L"Jonás",          {"jon"});
        AddBook(t, 33, "Miqueas",        {"miq"});
        AddBook(t, 34, L"Nahúm",          {"nah"});
        AddBook(t, 35, "Habacuc",        {"hab"});
        AddBook(t, 36, L"Sofonías",       {"sof"});
        AddBook(t, 37, "Hageo",          {"hag"});
        AddBook(t, 38, L"Zacarías",       {"zac"});
        AddBook(t, 39, L"Malaquías",      {"mal"});
        AddBook(t, 40, "Mateo",          {"mt", "mat"});
        AddBook(t, 41, "Marcos",         {"mr", "mar"});
        AddBook(t, 42, "Lucas",          {"lc", "luc"});
        AddBook(t, 43, "Juan",           {"jn", "ju", "jua"});
        AddBook(t, 44, "Hechos",         {"hch", "hech"});
        AddBook(t, 45, "Romanos",        {"ro", "rom", "rm"});
        AddBook(t, 46, "1 Corintios",    {"1co", "1 cor", "1cor"});
        AddBook(t, 47, "2 Corintios",    {"2co", "2 cor", "2cor"});
        AddBook(t, 48, L"Gálatas",        {"gal"});
        AddBook(t, 49, "Efesios",        {"ef", "efe"});
        AddBook(t, 50, "Filipenses",     {"fil"});
        AddBook(t, 51, "Colosenses",     {"col"});
        AddBook(t, 52, "1 Tesalonicenses", {"1ts", "1 te", "1tes"});
        AddBook(t, 53, "2 Tesalonicenses", {"2ts", "2 te", "2tes"});
        AddBook(t, 54, "1 Timoteo",      {"1ti", "1 ti", "1tim"});
        AddBook(t, 55, "2 Timoteo",      {"2ti", "2 ti", "2tim"});
        AddBook(t, 56, "Tito",           {"tit"});
        AddBook(t, 57, L"Filemón",        {"flm"});
        AddBook(t, 58, "Hebreos",        {"heb"});
        AddBook(t, 59, "Santiago",       {"stg", "sgt", "sant"});
        AddBook(t, 60, "1 Pedro",        {"1p", "1pe", "1 ped", "1ped"});
        AddBook(t, 61, "2 Pedro",        {"2p", "2pe", "2 ped", "2ped"});
        AddBook(t, 62, "1 Juan",         {"1jn", "1 juan", "1juan"});
        AddBook(t, 63, "2 Juan",         {"2jn", "2 juan", "2juan"});
        AddBook(t, 64, "3 Juan",         {"3jn", "3 juan", "3juan"});
        AddBook(t, 65, "Judas",          {"jud"});
        AddBook(t, 66, "Apocalipsis",    {"ap", "apo", "apoc"});
        return t;
    }
};

#endif // LUMINA_BIBLEREF_H
