// ============================================================================
//  Fusion-HP / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  BibleRef.cpp : tabla canónica de los 66 libros (nombres y abreviaturas en
//  español) y parser de referencias tipadas ("Jn 3:16", "salmo 23:1-6",
//  "1 co 13,4-7"). Port 1:1 de la edición wx v2.0.0 (heredada de Qt v1.6.0).
//  Resolución local < 1 ms. Coincidencia exacta de abreviatura primero y luego
//  prefijo de nombre sin acentos ni espacios. Los tokens ordinales "1"/"2"/"3"
//  seguidos de letras son parte del libro ("1 co", "1 sam", "1 juan").
// ============================================================================
#include "BibleRef.h"

#include "Utf8.h"

#include <algorithm>

namespace fusion {
namespace {

// ¿Todos dígitos y no vacío? (equivalente wx IsNumber para este uso)
bool IsAllDigits(const std::string& s) {
    if (s.empty()) return false;
    for (size_t i = 0; i < s.size(); ++i)
        if (s[i] < '0' || s[i] > '9') return false;
    return true;
}

bool HasLetters(const std::string& tk) {
    for (size_t i = 0; i < tk.size(); ++i)
        if (tk[i] >= 'a' && tk[i] <= 'z') return true;
    return false;
}

// Dígitos → long con tope (sin excepciones; entradas cortas).
long ParseLong(const std::string& s) {
    long v = 0;
    for (size_t i = 0; i < s.size(); ++i) {
        if (s[i] < '0' || s[i] > '9') return 0;
        v = v * 10 + (s[i] - '0');
        if (v > 1000000) v = 1000000;
    }
    return v;
}

// Elimina TODOS los espacios (equivalente wx WithoutSpaces).
std::string WithoutSpaces(const std::string& s) {
    std::string o;
    o.reserve(s.size());
    for (size_t i = 0; i < s.size(); ++i)
        if (s[i] != ' ') o += s[i];
    return o;
}

// Extrae tokens numéricos de un texto ("13:4-7" → ["13","4","7"]).
std::vector<std::string> NumberTokens(const std::string& s) {
    std::vector<std::string> out;
    std::string cur;
    for (size_t i = 0; i <= s.size(); ++i) {
        const char c = (i < s.size()) ? s[i] : ' ';
        if (c >= '0' && c <= '9') {
            cur += c;
        } else if (!cur.empty()) {
            out.push_back(cur);
            cur.clear();
        }
    }
    return out;
}

void AddBook(std::vector<BibleRef::BookInfo>& t, int n, const char* name,
             std::initializer_list<const char*> abbrs) {
    BibleRef::BookInfo b;
    b.number = n;
    b.name = name;
    for (const char* a : abbrs)
        b.abbrs.push_back(ToLowerAscii(std::string(a)));
    t.push_back(std::move(b));
}

std::vector<BibleRef::BookInfo> BuildTable() {
    std::vector<BibleRef::BookInfo> t;
    // Tabla completa de 66 libros — mismas abreviaturas que la edición wx.
    AddBook(t, 1,  "Génesis",        {"gn", "gen", "ge"});
    AddBook(t, 2,  "Éxodo",          {"ex", "exo", "exod"});
    AddBook(t, 3,  "Levítico",       {"lv", "lev"});
    AddBook(t, 4,  "Números",        {"nm", "num"});
    AddBook(t, 5,  "Deuteronomio",   {"dt", "deu", "deut"});
    AddBook(t, 6,  "Josué",          {"jos", "josu"});
    AddBook(t, 7,  "Jueces",         {"jue", "jueces"});
    AddBook(t, 8,  "Rut",            {"rt", "rut"});
    AddBook(t, 9,  "1 Samuel",       {"1s", "1sa", "1 sam", "1sam"});
    AddBook(t, 10, "2 Samuel",       {"2s", "2sa", "2 sam", "2sam"});
    AddBook(t, 11, "1 Reyes",        {"1r", "1re", "1 rey", "1rey"});
    AddBook(t, 12, "2 Reyes",        {"2r", "2re", "2 rey", "2rey"});
    AddBook(t, 13, "1 Crónicas",     {"1cr", "1cro", "1 cr", "1cron"});
    AddBook(t, 14, "2 Crónicas",     {"2cr", "2cro", "2 cr", "2cron"});
    AddBook(t, 15, "Esdras",         {"esd"});
    AddBook(t, 16, "Nehemías",       {"neh"});
    AddBook(t, 17, "Ester",          {"est"});
    AddBook(t, 18, "Job",            {"job"});
    AddBook(t, 19, "Salmos",         {"sal", "salmo", "salmos", "ps"});
    AddBook(t, 20, "Proverbios",     {"pr", "prov", "pv"});
    AddBook(t, 21, "Eclesiastés",    {"ec", "ecle"});
    AddBook(t, 22, "Cantares",       {"cnt", "cant"});
    AddBook(t, 23, "Isaías",         {"is", "isa"});
    AddBook(t, 24, "Jeremías",       {"jer"});
    AddBook(t, 25, "Lamentaciones",  {"lam"});
    AddBook(t, 26, "Ezequiel",       {"ez", "eze"});
    AddBook(t, 27, "Daniel",         {"dn", "dan"});
    AddBook(t, 28, "Oseas",          {"os", "ose"});
    AddBook(t, 29, "Joel",           {"jl", "joel"});
    AddBook(t, 30, "Amós",           {"am", "amos"});
    AddBook(t, 31, "Obadías",        {"ob", "obd"});
    AddBook(t, 32, "Jonás",          {"jon"});
    AddBook(t, 33, "Miqueas",        {"miq"});
    AddBook(t, 34, "Nahúm",          {"nah"});
    AddBook(t, 35, "Habacuc",        {"hab"});
    AddBook(t, 36, "Sofonías",       {"sof"});
    AddBook(t, 37, "Hageo",          {"hag"});
    AddBook(t, 38, "Zacarías",       {"zac"});
    AddBook(t, 39, "Malaquías",      {"mal"});
    AddBook(t, 40, "Mateo",          {"mt", "mat"});
    AddBook(t, 41, "Marcos",         {"mr", "mar"});
    AddBook(t, 42, "Lucas",          {"lc", "luc"});
    AddBook(t, 43, "Juan",           {"jn", "ju", "jua"});
    AddBook(t, 44, "Hechos",         {"hch", "hech"});
    AddBook(t, 45, "Romanos",        {"ro", "rom", "rm"});
    AddBook(t, 46, "1 Corintios",    {"1co", "1 cor", "1cor"});
    AddBook(t, 47, "2 Corintios",    {"2co", "2 cor", "2cor"});
    AddBook(t, 48, "Gálatas",        {"gal"});
    AddBook(t, 49, "Efesios",        {"ef", "efe"});
    AddBook(t, 50, "Filipenses",     {"fil"});
    AddBook(t, 51, "Colosenses",     {"col"});
    AddBook(t, 52, "1 Tesalonicenses", {"1ts", "1 te", "1tes"});
    AddBook(t, 53, "2 Tesalonicenses", {"2ts", "2 te", "2tes"});
    AddBook(t, 54, "1 Timoteo",      {"1ti", "1 ti", "1tim"});
    AddBook(t, 55, "2 Timoteo",      {"2ti", "2 ti", "2tim"});
    AddBook(t, 56, "Tito",           {"tit"});
    AddBook(t, 57, "Filemón",        {"flm"});
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

} // namespace

/* --------------------------------------------------------------- Books -- */

const std::vector<BibleRef::BookInfo>& BibleRef::Books() {
    static const std::vector<BookInfo> table = BuildTable();
    return table;
}

/* -------------------------------------------------------------- Resolve -- */

BibleRef::VerseRef BibleRef::Resolve(const std::string& input) {
    VerseRef out;
    std::string s = StripAccents(input);
    ReplaceAll(s, ".", "");
    s = Simplified(s);
    s = ToLowerAscii(s);
    if (s.empty())
        return out;

    // Separación libro/resto por tokens: el libro son los tokens de solo
    // letras (u ordinales "1"/"2"/"3" seguidos de letras) que anteceden al
    // primer token con dígitos de capítulo ("3:16", "13", "16-18"...).
    std::vector<std::string> tokens;
    for (const std::string& tk : Split(s, ' ')) {
        if (!tk.empty()) tokens.push_back(tk);
    }
    std::vector<std::string> bookTokens, restTokens;
    bool restStarted = false;
    for (size_t i = 0; i < tokens.size(); ++i) {
        const std::string& tk = tokens[i];
        if (restStarted) {
            restTokens.push_back(tk);
            continue;
        }
        if (IsAllDigits(tk)) {                       // ordinal ("1","2","3")
            if (i + 1 < tokens.size() && HasLetters(tokens[i + 1]))
                bookTokens.push_back(tk);
            else {
                restStarted = true;
                restTokens.push_back(tk);
            }
            continue;
        }
        if (HasLetters(tk) && tk.find(':') == std::string::npos)
            bookTokens.push_back(tk);
        else {
            restStarted = true;
            restTokens.push_back(tk);
        }
    }
    std::string bookPart;
    for (size_t i = 0; i < bookTokens.size(); ++i) {
        if (i) bookPart += ' ';
        bookPart += bookTokens[i];
    }
    std::string rest;
    for (size_t i = 0; i < restTokens.size(); ++i) {
        if (i) rest += ' ';
        rest += restTokens[i];
    }
    if (bookPart.empty())
        return out;

    const std::vector<BookInfo>& table = Books();
    int bookNum = 0;
    std::string bookName;
    // 1) coincidencia exacta de abreviatura (primer hit en orden de tabla)
    for (const BookInfo& b : table) {
        if (bookNum) break;
        for (const std::string& a : b.abbrs) {
            if (a == bookPart) {
                bookNum = b.number;
                bookName = b.name;
                break;
            }
        }
    }
    // 2) prefijo de nombre completo (sin acentos ni espacios)
    if (!bookNum) {
        const std::string bp = WithoutSpaces(StripAccents(bookPart));
        for (const BookInfo& b : table) {
            // nombre en minúsculas y sin acentos (equivalente wx name.Lower()
            // + StripAccents: el orden es indiferente para el español)
            const std::string nm = WithoutSpaces(ToLowerAscii(StripAccents(b.name)));
            if (bp.size() <= nm.size() && nm.compare(0, bp.size(), bp) == 0) {
                bookNum = b.number;
                bookName = b.name;
                break;
            }
        }
    }
    if (!bookNum)
        return out;

    out.book = bookNum;
    out.bookName = bookName;
    if (!rest.empty()) {
        ReplaceAll(rest, ",", ":");
        rest = WithoutSpaces(rest);
        // Formatos: "3", "3:16", "3:16-18", "3:16-3:18"
        long chap = 0, v1 = 0, v2 = -1, chap2 = -1;
        const std::vector<std::string> nums = NumberTokens(rest);
        if (nums.size() >= 1) chap = ParseLong(nums[0]);
        if (nums.size() >= 2) v1 = ParseLong(nums[1]);
        if (nums.size() >= 3) v2 = ParseLong(nums[2]);
        if (nums.size() >= 4) { chap2 = v2; v2 = ParseLong(nums[3]); }
        out.chapter = (int)chap;
        if (chap2 >= 0)
            out.verse = (int)v1;      // "3:16-3:18" → usa el primer versículo
        else if (v2 >= 0)
            out.verse = (int)v1;
        else if (v1 > 0)
            out.verse = (int)v1;
        else
            out.verse = 0;
    }
    return out;
}

/* ------------------------------------------------------------ FormatRef -- */

std::string BibleRef::FormatRef(const VerseRef& r) {
    if (!r.Valid())
        return std::string();
    if (r.verse > 0)
        return r.bookName + " " + std::to_string(r.chapter) + ":" + std::to_string(r.verse);
    return r.bookName + " " + std::to_string(r.chapter);
}

} // namespace fusion
