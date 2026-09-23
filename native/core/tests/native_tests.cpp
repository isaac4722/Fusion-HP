// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  native_tests.cpp : arnés de pruebas del núcleo nativo (sin GUI, sin wx).
//  Valida Chords, Lyrics (Modo Hinario M16), BibleRef, SongModel (esquema
//  propio + subconjunto OpenLP), BibleBib (.BIB con detección automática),
//  Scripture y Storage (SQLite+FTS5 a través de la API C de lumina.h, que
//  valida también el ABI). 0 fallos = verde (imprime "OK n/n", devuelve 0/1).
//  Compila en Linux (GCC, -Wall -Wextra -Wpedantic) y Windows (MSVC /W4).
// ============================================================================
#include "lumina/lumina.h"

#include "Models.h"
#include "Utf8.h"
#include "Chords.h"
#include "Lyrics.h"
#include "BibleRef.h"
#include "SongModel.h"
#include "BibleBib.h"
#include "Scripture.h"
#include "Highlight.h"

#include <nlohmann/json.hpp>

#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

#if !defined(_WIN32)
#include <unistd.h>   // getpid() → nombre único de la BD temporal
#endif

using json = nlohmann::json;
using namespace lumina;

static int g_checks = 0, g_failed = 0;

#define CHECK(cond)                                                            \
    do {                                                                       \
        ++g_checks;                                                            \
        if (!(cond)) {                                                         \
            ++g_failed;                                                        \
            std::printf("  FALLO: %s (linea %d)\n", #cond, __LINE__);          \
        }                                                                      \
    } while (0)

static void Section(const char* name) { std::printf("%s\n", name); }

/* --------------------------- helpers de API C -------------------------- */

// Patrón de búfer de la API C para funciones PURAS: medir con cap=0 y volver
// a llamar. (NUNCA usar con operaciones con efectos como INSERT: ejecutaría
// dos veces; para esas está ApiCallOnce.)
template <typename Fn>
static bool ApiCall(Fn fn, std::string* out) {
    int32_t needed = 0;
    const LuminaStatus s1 = fn(nullptr, 0, &needed);
    if (needed <= 0) return false;
    if (s1 != LUMINA_OK && s1 != LUMINA_ERR_LIMIT) return false;
    std::vector<char> buf((size_t)needed, '\0');
    const LuminaStatus s2 = fn(buf.data(), needed, &needed);
    if (s2 != LUMINA_OK) return false;
    size_t len = 0;
    while (len < (size_t)needed && buf[len] != '\0') ++len;
    if (out) out->assign(buf.data(), len);
    return true;
}

// Una sola llamada con búfer generoso (operaciones con efectos: db_exec).
template <typename Fn>
static bool ApiCallOnce(Fn fn, std::string* out) {
    std::vector<char> buf(1 << 20, '\0');
    int32_t needed = (int32_t)buf.size();
    const LuminaStatus s = fn(buf.data(), needed, &needed);
    if (s != LUMINA_OK) return false;
    size_t len = 0;
    while (len < buf.size() && buf[len] != '\0') ++len;
    if (out) out->assign(buf.data(), len);
    return true;
}

static std::string TempDbPath() {
#if defined(_WIN32)
    return "lumina_selftest_tmp.sqlite3";
#else
    char buf[64];
    std::snprintf(buf, sizeof(buf), "/tmp/lumina_selftest_%ld.sqlite3", (long)getpid());
    return std::string(buf);
#endif
}

/* ------------------------------------------------------------------ main */

int main() {
    std::printf("== Selftest del nucleo nativo - LuminaPresentation v6.0.0 ==\n");

    /* ================================================================ 1 == */
    Section("[1] Chords (port wx 1:1)");
    CHECK(Chords::NoteToSemitone("Do") == 0);
    CHECK(Chords::NoteToSemitone("Sol") == 7);
    CHECK(Chords::NoteToSemitone("F#") == 6);
    CHECK(Chords::NoteToSemitone("Bb") == 10);
    CHECK(Chords::NoteToSemitone("solb") == 6);
    CHECK(Chords::NoteToSemitone("sol#") == 8);
    CHECK(Chords::NoteToSemitone("do#") == 1);
    CHECK(Chords::NoteToSemitone("d") == 2);
    CHECK(Chords::NoteToSemitone("zzz") == -1);
    CHECK(Chords::IsChordLine("Do Sol Am"));
    CHECK(!Chords::IsChordLine("Aleluya poder"));
    CHECK(Chords::IsChordLine("Do    Sol    Lam   Fa"));
    CHECK(!Chords::IsChordLine("mi casa es tu casa"));
    CHECK(!Chords::IsChordToken("Rey"));
    CHECK(Chords::IsChordToken("Sol/Fa"));
    CHECK(Chords::IsChordToken("C/E"));
    CHECK(Chords::IsChordToken("Am7"));
    CHECK(Chords::IsChordToken("Csus4"));
    CHECK(Chords::IsChordToken("Cadd9"));
    CHECK(Chords::IsChordToken("Cdim"));
    CHECK(Chords::IsChordToken("Caug"));      // v4.2.0: 'g' de «aug» en kOkChars
    CHECK(Chords::IsChordToken("Caug7"));
    CHECK(Chords::IsChordLine("Csus4  Cadd9  Cdim  Caug"));
    CHECK(!Chords::IsChordToken("cinco"));    // raíz C + sufijo con 'c' → rechazado
    CHECK(!Chords::IsChordToken("cuatro"));   // raíz C + "uatro" → 'u' no arranque de sufijo
    // Alineación preservada: mismos espacios interiores + relleno que conserva
    // la columna del token ("Sol"→"La" es más corto → relleno final).
    const std::string tl = Chords::TransposeLine("Do  Sol", 2, true);
    CHECK(tl == "Re  La ");
    CHECK(Trim(tl) == "Re  La");
    // Bajo con barra: Sol(7)+1=8=Sol#; Fa(5)+1=6=Fa#  →  "Sol#/Fa#"
    CHECK(Chords::TransposeChordToken("Sol/Fa", 1, true) == "Sol#/Fa#");
    // El mismo token a +3 semitonos produce el par La#/Sol# (Sol 7+3=10=La#)
    CHECK(Chords::TransposeChordToken("Sol/Fa", 3, true) == "La#/Sol#");
    // Anglosajona: C(0)+2=D; E(4)+2=F#
    CHECK(Chords::TransposeChordToken("C/E", 2, false) == "D/F#");

    /* ================================================================ 2 == */
    Section("[2] Lyrics (Modo Hinario M16)");
    Song song;
    song.title = "Prueba";
    song.lyrics = "[Verso 1]\nGrande es el Senor\nDigno de alabar\n\n"
                  "[Coro]\nSanto, santo, santo\n\n"
                  "[Verso 2]\nToda lengua confesara\n\n"
                  "[Coro]\nSanto, santo, santo\n";
    BuildOptions plain;
    plain.titleSlide = false;
    const std::vector<Slide> sl = Lyrics::BuildSlides(song, plain);
    CHECK(sl.size() == 4);                       // V1 C V2 C (lineal)
    CHECK(sl[0].refLabel == "Verso 1" && sl[1].refLabel == "Coro");
    CHECK(sl[2].refLabel == "Verso 2" && sl[3].refLabel == "Coro");

    BuildOptions hymn;
    hymn.titleSlide = false;
    hymn.chorusInterleave = true;
    const std::vector<Slide> sh = Lyrics::BuildSlides(song, hymn);
    // [Coro] repetido → SOLO el primer coro se usa: V1 C V2 C (4 slides, no 6)
    CHECK(sh.size() == 4);
    if (sh.size() == 4) {
        CHECK(sh[1].lines[0].text == "Santo, santo, santo");   // coro tras V1
        CHECK(sh[3].lines[0].text == "Santo, santo, santo");   // coro tras V2
    }

    // Canción solo-coros: proyección lineal (nunca vacía)
    Song coros;
    coros.title = "Solo coro";
    coros.lyrics = "[Coro]\nAleluya, aleluya\nAleluya";
    const std::vector<Slide> sc = Lyrics::BuildSlides(coros, hymn);
    CHECK(sc.size() == 1 && sc[0].lines[0].text == "Aleluya, aleluya");

    // Paginación: verso de 5 líneas con maxLinesPerSlide=2 → 3 slides en orden
    Song largo;
    largo.title = "Largo";
    largo.lyrics = "[Verso 1]\nuno\ndos\ntres\ncuatro\ncinco\n";
    BuildOptions p2;
    p2.titleSlide = false;
    p2.maxLinesPerSlide = 2;
    const std::vector<Slide> sl2 = Lyrics::BuildSlides(largo, p2);
    CHECK(sl2.size() == 3);
    if (sl2.size() == 3) {
        CHECK(sl2[0].lines.size() == 2 && sl2[0].lines[0].text == "uno" &&
              sl2[0].lines[1].text == "dos");
        CHECK(sl2[1].lines.size() == 2 && sl2[1].lines[0].text == "tres");
        CHECK(sl2[2].lines.size() == 1 && sl2[2].lines[0].text == "cinco");
        CHECK(sl2[0].refLabel == "Verso 1" && sl2[2].refLabel == "Verso 1");
    }

    // Cifrado adjunto + transposición con stripChords=false
    Song cif;
    cif.title = "Cifrado";
    cif.lyrics = "[Verso 1]\nDo  Sol\naqui va la letra\n";
    BuildOptions keep;
    keep.titleSlide = false;
    keep.stripChords = false;
    keep.transpose = 2;
    keep.latinChords = true;
    const std::vector<Slide> scif = Lyrics::BuildSlides(cif, keep);
    CHECK(scif.size() == 1);
    if (scif.size() == 1) {
        CHECK(scif[0].lines[0].chords == "Re  La ");   // transpuesta + relleno
        CHECK(scif[0].lines[0].text == "aqui va la letra");
    }
    // Con stripChords=true (default) la audiencia no ve cifrado
    BuildOptions strip;
    strip.titleSlide = false;
    const std::vector<Slide> sstrip = Lyrics::BuildSlides(cif, strip);
    CHECK(sstrip.size() == 1 && sstrip[0].lines[0].chords.empty());
    // Slide de título por defecto
    BuildOptions tit;
    const std::vector<Slide> stout = Lyrics::BuildSlides(song, tit);
    CHECK(stout.size() == 5 && stout[0].kind == SLIDE_TITLE &&
          stout[0].lines[0].text == "Prueba");

    /* ================================================================ 3 == */
    Section("[3] BibleRef");
    const BibleRef::VerseRef r1 = BibleRef::Resolve("Jn 3:16");
    CHECK(r1.book == 43 && r1.chapter == 3 && r1.verse == 16);
    CHECK(r1.bookName == "Juan");
    const BibleRef::VerseRef r2 = BibleRef::Resolve("salmo 23");
    CHECK(r2.book == 19 && r2.chapter == 23 && r2.verse == 0);
    const BibleRef::VerseRef r3 = BibleRef::Resolve("1 co 13,4-7");
    CHECK(r3.book == 46 && r3.chapter == 13 && r3.verse == 4);
    const BibleRef::VerseRef r4 = BibleRef::Resolve("1sam 3:16");
    CHECK(r4.book == 9 && r4.chapter == 3 && r4.verse == 16);
    const BibleRef::VerseRef r5 = BibleRef::Resolve("Génesis 1");
    CHECK(r5.book == 1 && r5.chapter == 1 && r5.verse == 0);
    const BibleRef::VerseRef r6 = BibleRef::Resolve("1sam 3");
    CHECK(r6.book == 9 && r6.chapter == 3 && r6.verse == 0);
    const BibleRef::VerseRef r7 = BibleRef::Resolve("1 juan 2");
    CHECK(r7.book == 62 && r7.chapter == 2);
    CHECK(!BibleRef::Resolve("3:16").Valid());   // sin libro es inválida
    CHECK(BibleRef::Resolve("Jn 3:16").Valid());
    CHECK(BibleRef::FormatRef(r1) == "Juan 3:16");
    CHECK(BibleRef::FormatRef(r2) == "Salmos 23");
    CHECK(BibleRef::FormatRef(BibleRef::VerseRef()).empty());
    CHECK(BibleRef::Books().size() == 66);

    /* ================================================================ 4 == */
    Section("[4] SongModel (esquema propio + OpenLP)");
    // — esquema propio con blocks
    const json js = json::parse(R"({
        "id":"c1","title":"Cancion Uno","artist":"Autor","key":"Do","bpm":96,
        "tags":"adoracion","stripChords":true,"latinChords":true,
        "transpose":0,"maxLinesPerSlide":4,
        "blocks":[{"label":"Verso 1","lines":["Uno","Dos"]},
                  {"label":"Coro","lines":["Coro aqui"]}]
    })");
    Song sg;
    SongModel::FromJson(js, &sg);
    CHECK(sg.title == "Cancion Uno" && sg.keyName == "Do" && sg.bpm == 96);
    CHECK(sg.blocks.size() == 2 && sg.blocks[0].lines[1] == "Dos");
    CHECK(sg.blocks[1].repeat == 1);
    // LyricsText serializa blocks a texto crudo "[label]\nlineas\n\n"
    const std::string lt = sg.LyricsText();
    CHECK(lt.find("[Verso 1]") != std::string::npos &&
          lt.find("[Coro]") != std::string::npos && lt.find("Coro aqui") != std::string::npos);
    // ToJson inverso razonable (round-trip con FromJson)
    const json js2 = SongModel::ToJson(sg);
    Song sg2;
    SongModel::FromJson(js2, &sg2);
    CHECK(sg2.title == sg.title && sg2.keyName == sg.keyName &&
          sg2.blocks.size() == sg.blocks.size() && sg2.bpm == sg.bpm);
    // ParseAndBuildSlides: título + V1 + Coro
    const json res = SongModel::ParseAndBuildSlides(js);
    CHECK(res["ok"] == 1);
    CHECK(res["slides"].is_array() && res["slides"].size() == 3);
    CHECK(res["slides"][0]["kind"] == SLIDE_TITLE);
    CHECK(res["slides"][1]["refLabel"] == "Verso 1");
    CHECK(res["slides"][2]["refLabel"] == "Coro");
    CHECK(res["song"]["title"] == "Cancion Uno");

    // — estilo OpenLP (subconjunto tolerante)
    const json olp = json::parse(R"({
        "title":"OpenLP Song","alternate_title":"Alt",
        "authors":["Uno","Dos"],"copyright":"(c) X",
        "verse_order_list":["V1","C1"],
        "lyrics":"[V 1]\nlinea uno\n\n[C]\ncoro openlp\n"
    })");
    Song so;
    SongModel::FromJson(olp, &so);
    CHECK(so.title == "OpenLP Song");            // "title" manda sobre alt
    CHECK(so.artist == "Uno, Dos");              // authors array → artist
    CHECK(so.lyrics.find("[V 1]") != std::string::npos);   // lyrics tal cual
    const json res2 = SongModel::ParseAndBuildSlides(olp);
    CHECK(res2["ok"] == 1);
    CHECK(res2["slides"].is_array() && res2["slides"].size() >= 2);
    CHECK(res2["slides"][1]["lines"][0] == "linea uno");

    // — OpenLP con authors como string
    const json olp2 = json::parse(R"({"title":"S2","authors":"Autor Unico"})");
    Song so2;
    SongModel::FromJson(olp2, &so2);
    CHECK(so2.artist == "Autor Unico");

    // — cifrado preservado cuando stripChords=false
    const json jc = json::parse(R"({
        "title":"ConCifrado","stripChords":false,"transpose":0,
        "lyrics":"[Verso 1]\nDo  Sol\nletra con acordes\n"
    })");
    const json rc = SongModel::ParseAndBuildSlides(jc);
    CHECK(rc["ok"] == 1);
    const json& sl1 = rc["slides"][1];           // tras el título
    CHECK(sl1["lines"][0].is_object());
    CHECK(sl1["lines"][0]["chords"] == "Do  Sol");
    CHECK(sl1["lines"][0]["text"] == "letra con acordes");
    // con stripChords por defecto → solo texto (strings)
    const json jd = json::parse(R"({
        "title":"SinCifrado",
        "lyrics":"[Verso 1]\nDo  Sol\nletra con acordes\n"
    })");
    const json rd = SongModel::ParseAndBuildSlides(jd);
    CHECK(rd["slides"][1]["lines"][0].is_string());

    /* ================================================================ 5 == */
    Section("[5] BibleBib (.BIB)");
    // — fixture 1: directivas + TAB + acentos UTF-8
    const std::string bib1 =
        "#BIB 1\n"
        "#VERSION TST\n"
        "#NAME Biblia de prueba\n"
        "#BOOKS 2\n"
        "Génesis\t1\t1\tEn el principio creó Dios\n"
        "Génesis\t1\t2\tY la tierra estaba desordenada\n"
        "Juan\t3\t16\tPorque de tal manera amó Dios\n";
    BibleBib::Stats st1;
    std::vector<BibVerse> vs1;
    CHECK(BibleBib::Parse(bib1, &st1, &vs1));
    CHECK(st1.verses == 3 && st1.books == 2 && st1.errors == 0);
    CHECK(st1.version == "TST" && st1.name == "Biblia de prueba");
    CHECK(st1.encoding == "utf-8" && st1.separator == "tab");
    CHECK(vs1.size() == 3 && vs1[0].book == 1 && vs1[2].book == 43);
    CHECK(vs1[2].text == "Porque de tal manera amó Dios");
    CHECK(st1.sample.size() == 3);
    CHECK(st1.sample[0] == "Génesis 1:1 En el principio creó Dios");

    // — fixture 2: separador ";;"
    const std::string bib2 =
        "#VERSION T2\n"
        "Génesis;;1;;1;;Al principio\n"
        "Apocalipsis;;22;;21;;La gracia sea con vosotros\n";
    BibleBib::Stats st2;
    std::vector<BibVerse> vs2;
    CHECK(BibleBib::Parse(bib2, &st2, &vs2));
    CHECK(st2.separator == "semicolon" && st2.verses == 2 && st2.books == 2);
    CHECK(vs2[1].book == 66 && vs2[1].chapter == 22 && vs2[1].verse == 21);

    // — fixture 3: CP1252 (0xC1 0xE9 = "Á é") → convertida a UTF-8
    std::string bib3 = "#VERSION CP\nJuan\t3\t16\t";
    bib3 += (char)0xC1;
    bib3 += ' ';
    bib3 += (char)0xE9;
    bib3 += "\n";
    BibleBib::Stats st3;
    std::vector<BibVerse> vs3;
    CHECK(BibleBib::Parse(bib3, &st3, &vs3));
    CHECK(st3.encoding == "cp1252" && st3.separator == "tab");
    CHECK(vs3.size() == 1 && vs3[0].book == 43);
    CHECK(vs3[0].text == "Á é");

    // — fixture 4: variante por línea "Libro cap:ver texto"
    const std::string bib4 =
        "#VERSION COL\n"
        "Juan 3:16 Porque de tal manera amó Dios\n"
        "Juan 3:17 Para que todo aquel que en él cree\n";
    BibleBib::Stats st4;
    std::vector<BibVerse> vs4;
    CHECK(BibleBib::Parse(bib4, &st4, &vs4));
    CHECK(st4.separator == "colon" && st4.verses == 2 && st4.errors == 0);
    CHECK(vs4[0].book == 43 && vs4[0].chapter == 3 && vs4[0].verse == 16);
    CHECK(st4.sample[0] == "Juan 3:16 Porque de tal manera amó Dios");

    // — fixture 5: filas inválidas → errors>0 sin abortar
    const std::string bib5 =
        "#VERSION ERR\n"
        "Génesis\t1\t1\tVálido\n"
        "Génesis\tX\t2\tCapítulo no numérico\n"
        "Libro inventado\t1\t1\tNo resuelve\n";
    BibleBib::Stats st5;
    std::vector<BibVerse> vs5;
    CHECK(BibleBib::Parse(bib5, &st5, &vs5));
    CHECK(st5.errors == 2 && st5.verses == 1 && vs5[0].book == 1);
    CHECK(st5.ToJson()["ok"] == 1 && st5.ToJson()["errors"] == 2);

    // — maxBytes excedido → false
    CHECK(!BibleBib::Parse(bib1, nullptr, nullptr, 8));

    // — BOM UTF-8 se ignora y sigue parseando
    const std::string bib6 = "\xEF\xBB\xBF" + bib1;
    BibleBib::Stats st6;
    CHECK(BibleBib::Parse(bib6, &st6, nullptr));
    CHECK(st6.verses == 3 && st6.encoding == "utf-8");

    /* ================================================================ 6 == */
    Section("[6] Scripture");
    std::vector<Slide> scr;
    CHECK(Scripture::BuildSlides("Juan 3:16-17", {"Texto uno", "Texto dos"}, 1, &scr));
    CHECK(scr.size() == 2);
    CHECK(scr[0].kind == SLIDE_SCRIPTURE && scr[0].refLabel == "Juan 3:16");
    CHECK(scr[1].refLabel == "Juan 3:17" && scr[1].lines[0].text == "Texto dos");
    CHECK(scr[0].title.empty() && scr[0].lines.size() == 1);
    // agrupación de 2 versos por slide → rango
    std::vector<Slide> scr2;
    CHECK(Scripture::BuildSlides("Juan 3:16-17", {"a", "b"}, 2, &scr2));
    CHECK(scr2.size() == 1 && scr2[0].refLabel == "Juan 3:16-17" &&
          scr2[0].lines.size() == 2);
    // referencia inválida → false, sin slides
    std::vector<Slide> scr3;
    CHECK(!Scripture::BuildSlides("no es un libro 5", {"x"}, 1, &scr3));
    CHECK(scr3.empty());
    // versos vacíos → false
    std::vector<Slide> scr4;
    CHECK(!Scripture::BuildSlides("Juan 3:16", {}, 1, &scr4));

    /* ================================================================ 6b == */
    Section("[6b] Highlight (resaltado en proyección, v6.0.0)");
    // FoldLatin: mayúsculas + acentos latinos → ASCII plegado
    CHECK(Highlight::FoldLatin("Porque Amó Él") == "porque amo el");
    CHECK(Highlight::FoldLatin("Espíritu") == "espiritu");
    CHECK(Highlight::FoldLatin("ÑANDÚ") == "nandu");
    // Split: match con acento en AMBOS lados (texto «amó», palabra «amo»)
    {
        const std::vector<HlSegment> s1 = Highlight::Split("Porque de tal manera amó Dios", "amo");
        CHECK(s1.size() == 3);
        CHECK(!s1[0].match && s1[0].text == "Porque de tal manera ");
        CHECK(s1[1].match  && s1[1].text == "amó");
        CHECK(!s1[2].match && s1[2].text == " Dios");
    }
    // Match insensible a mayúsculas (Dios/dios)
    {
        const std::vector<HlSegment> s2 = Highlight::Split("Dios es Dios", "dios");
        CHECK(s2.size() == 3);
        CHECK(s2[0].match && s2[0].text == "Dios");
        CHECK(s2[2].match && s2[2].text == "Dios");
    }
    // Frontera de palabra: «Dios» NO matchea dentro de «Diosas»
    {
        const std::vector<HlSegment> s3 = Highlight::Split("Las Diosas cantan", "Dios");
        CHECK(s3.size() == 1 && !s3[0].match);
    }
    // Palabra al inicio y al final del texto
    {
        const std::vector<HlSegment> s4 = Highlight::Split("amor eterno amor", "amor");
        CHECK(s4.size() == 3 && s4[0].match && s4[2].match && !s4[1].match);
    }
    // Palabra vacía → un segmento sin marca
    {
        const std::vector<HlSegment> s5 = Highlight::Split("texto", "");
        CHECK(s5.size() == 1 && !s5[0].match && s5[0].text == "texto");
    }
    // Sin coincidencias → un segmento sin marca
    {
        const std::vector<HlSegment> s6 = Highlight::Split("texto sin la palabra", "zzz");
        CHECK(s6.size() == 1 && !s6[0].match);
    }
    // Frase multi-palabra con acentos y mayúsculas
    {
        const std::vector<HlSegment> s7 =
            Highlight::Split("Porque AMÓ Dios al mundo", "amó dios");
        CHECK(s7.size() == 3);
        CHECK(s7[1].match && s7[1].text == "AMÓ Dios");
    }
    // Concatenación de segmentos == texto original (bytes intactos)
    {
        const std::vector<HlSegment> s8 = Highlight::Split("El Señor es mi pastor", "señor");
        std::string joined;
        for (const HlSegment& sg : s8) joined += sg.text;
        CHECK(joined == "El Señor es mi pastor");
        CHECK(s8[1].match && s8[1].text == "Señor");
    }


    /* ================================================================ 7 == */
    Section("[7] API C (ABI): version/song/chords/bib/ref");
    std::string apiOut;
    CHECK(ApiCall([](char* o, int32_t c, int32_t* n) { return lumina_version(o, c, n); },
                  &apiOut));
    CHECK(apiOut.compare(0, 10, "LuminaCore") == 0);

    // lumina_song_parse con una canción del esquema propio
    const json apiSong = json::parse(R"({
        "title":"API Song",
        "lyrics":"[Verso 1]\nuno\ndos\n"
    })");
    const std::string songJson = apiSong.dump();
    CHECK(ApiCall([&](char* o, int32_t c, int32_t* n) {
        return lumina_song_parse(songJson.data(), (int32_t)songJson.size(), o, c, n);
    }, &apiOut));
    const json rp = json::parse(apiOut);
    CHECK(rp["ok"] == 1 && rp["slides"].is_array() && rp["slides"].size() == 2);

    // lumina_chords_transpose conserva la alineación (JSON {"line":...})
    CHECK(ApiCall([&](char* o, int32_t c, int32_t* n) {
        return lumina_chords_transpose("Do  Sol", 2, 1, o, c, n);
    }, &apiOut));
    const json jt = json::parse(apiOut);
    CHECK(jt["line"] == "Re  La ");

    // lumina_bib_parse con el fixture TAB
    CHECK(ApiCall([&](char* o, int32_t c, int32_t* n) {
        return lumina_bib_parse(bib1.data(), (int32_t)bib1.size(), nullptr, o, c, n);
    }, &apiOut));
    const json jb = json::parse(apiOut);
    CHECK(jb["ok"] == 1 && jb["verses"] == 3 && jb["separator"] == "tab");
    CHECK(jb["sample"].is_array() && jb["sample"].size() == 3);

    // lumina_bible_ref_resolve
    CHECK(ApiCall([&](char* o, int32_t c, int32_t* n) {
        return lumina_bible_ref_resolve("Jn 3:16", o, c, n);
    }, &apiOut));
    const json jr = json::parse(apiOut);
    CHECK(jr["book"] == 43 && jr["chapter"] == 3 && jr["verse"] == 16 &&
          jr["name"] == "Juan");

    /* ================================================================ 8 == */
    Section("[8] Storage (SQLite+FTS vía lumina_db_*)");
    const std::string dbPath = TempDbPath();
    std::remove(dbPath.c_str());
    LuminaConfig cfg;
    std::memset(&cfg, 0, sizeof(cfg));
    cfg.structSize = (int32_t)sizeof(LuminaConfig);
    cfg.headless = 1;
    LuminaHandle h = lumina_create(&cfg);
    CHECK(h != nullptr);
    CHECK(lumina_db_open(h, dbPath.c_str()) == LUMINA_OK);

    // INSERT con parámetros enlazados → changes/lastId
    json qi;
    qi["sql"] = "INSERT INTO songs(title,author,lyrics,tags) VALUES(?1,?2,?3,?4)";
    qi["params"] = json::array({"Grande es el Señor", "Tradicional",
                                "[Coro]\nGrande es el Senor", "adoracion"});
    const std::string qiS = qi.dump();
    CHECK(ApiCallOnce([&](char* o, int32_t c, int32_t* n) {
        return lumina_db_exec(h, qiS.c_str(), o, c, n);
    }, &apiOut));
    const json ri = json::parse(apiOut);
    CHECK(ri["changes"] == 1 && ri["lastId"] >= 1);

    // FTS5: MATCH encuentra (y sin acentos también, unicode61)
    json qs;
    qs["sql"] = "SELECT title FROM songs_fts WHERE songs_fts MATCH ?1";
    qs["params"] = json::array({"grande"});
    const std::string qsS = qs.dump();
    CHECK(ApiCallOnce([&](char* o, int32_t c, int32_t* n) {
        return lumina_db_exec(h, qsS.c_str(), o, c, n);
    }, &apiOut));
    const json rs = json::parse(apiOut);
    CHECK(rs["rows"].is_array() && rs["rows"].size() == 1);
    CHECK(rs["rows"][0][0] == "Grande es el Señor");

    json qs2;
    qs2["sql"] = "SELECT title FROM songs_fts WHERE songs_fts MATCH ?1";
    qs2["params"] = json::array({"senor"});
    const std::string qs2S = qs2.dump();
    CHECK(ApiCallOnce([&](char* o, int32_t c, int32_t* n) {
        return lumina_db_exec(h, qs2S.c_str(), o, c, n);
    }, &apiOut));
    const json rs2 = json::parse(apiOut);
    CHECK(rs2["rows"].is_array() && rs2["rows"].size() == 1);   // 'senor'→'Señor'

    // Biblia: INSERT OR IGNORE dos veces con la misma fila → count==1
    json qb;
    qb["sql"] = "INSERT OR IGNORE INTO bible(version,book,chapter,verse,text)"
                " VALUES('TST',43,3,16,'Porque de tal manera amó Dios')";
    const std::string qbS = qb.dump();
    CHECK(ApiCallOnce([&](char* o, int32_t c, int32_t* n) {
        return lumina_db_exec(h, qbS.c_str(), o, c, n);
    }, &apiOut));
    CHECK(json::parse(apiOut)["changes"] == 1);
    CHECK(ApiCallOnce([&](char* o, int32_t c, int32_t* n) {
        return lumina_db_exec(h, qbS.c_str(), o, c, n);
    }, &apiOut));
    CHECK(json::parse(apiOut)["changes"] == 0);   // ignorada por UNIQUE

    json qc;
    qc["sql"] = "SELECT count(*) FROM bible WHERE version='TST'";
    const std::string qcS = qc.dump();
    CHECK(ApiCallOnce([&](char* o, int32_t c, int32_t* n) {
        return lumina_db_exec(h, qcS.c_str(), o, c, n);
    }, &apiOut));
    CHECK(json::parse(apiOut)["rows"][0][0] == 1);

    // Cierre y limpieza
    CHECK(lumina_db_close(h) == LUMINA_OK);
    lumina_destroy(h);
    std::remove(dbPath.c_str());

    /* ------------------------------------------------------------- fin -- */
    std::printf("== OK %d/%d ==%s\n", g_checks - g_failed, g_checks,
                g_failed ? " - HAY FALLOS" : "");
    std::fflush(stdout);
    return g_failed ? 1 : 0;
}
