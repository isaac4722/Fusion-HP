// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  tools/selftest.cpp : Arnés de pruebas de núcleo (SIN GUI). Valida Chords,
//  Lyrics (Modo Hinario con las correcciones M16), BibleRef, Database
//  (FTS + tags + importación) y ServiceIO. 0 fallos = OK.
//
//  Compilación (ejemplo Linux):
//    g++ -std=c++17 tools/selftest.cpp src/core/Database.cpp \
//        third_party/sqlite3/sqlite3.c -I src -I third_party/sqlite3 \
//        -I third_party/nlohmann $(wx-config --cxxflags --libs base) -o selftest
// ============================================================================
#include "core/BibleRef.h"
#include "core/Chords.h"
#include "core/Database.h"
#include "core/Lyrics.h"
#include "core/Types.h"

#include <wx/wx.h>   // wxBase mínimo para consola

#include <clocale>
#include <cstdio>
#include <functional>

static int g_checks = 0, g_failed = 0;
static void Check(bool cond, const char *name)
{
    ++g_checks;
    if (!cond) {
        ++g_failed;
        std::printf("  FALLO: %s\n", name);
    }
}

int main()
{
    // Sin GUI: evita la inicializacion de GTK del wxAppCore enlazado
    std::setvbuf(stdout, nullptr, _IONBF, 0);
    std::setlocale(LC_ALL, "C.UTF-8");
    setenv("LC_ALL", "C.UTF-8", 1);
    std::printf("== Selftest de núcleo — LuminaPresentation Suite v2.0 ==\n");

    // ------------------------------------------------------------------ Chords
    std::printf("[1] Chords\n");
    Check(Chords::NoteToSemitone("do") == 0, "do=0");
    Check(Chords::NoteToSemitone("Do") == 0, "Do=0");
    Check(Chords::NoteToSemitone("sol") == 7, "sol=7");
    Check(Chords::NoteToSemitone("sib") == 10, "sib=10");
    Check(Chords::NoteToSemitone("C") == 0, "C=0");
    Check(Chords::NoteToSemitone("B") == 11, "B=11");
    Check(Chords::NoteToSemitone("F#") == 6, "F#=6");
    Check(Chords::NoteToSemitone("d") == 2, "d=2 (natural, fix v1.2.0)");
    Check(Chords::IsChordToken("Sol"), "Sol token");
    Check(Chords::IsChordToken("Sol/Fa"), "Sol/Fa token");
    Check(Chords::IsChordToken("C/E"), "C/E token");
    Check(Chords::IsChordToken("Am7"), "Am7 token");
    Check(Chords::IsChordToken("Fa#m"), "Fa#m token");
    Check(!Chords::IsChordToken("Rey"), "Rey NO acorde");
    Check(!Chords::IsChordToken("vida"), "vida NO acorde");
    Check(Chords::IsChordLine("Do    Sol    Lam   Fa"), "línea de acordes");
    Check(!Chords::IsChordLine("mi casa es tu casa"), "letra no es cifrado");
    Check(Chords::TransposeLine("Do Sol", 2, true).Trim() == "Re La", "Do Sol +2 => Re La");
    Check(Chords::TransposeLine("C E G", 2, false).Trim() == "D F# A", "C E G +2 (anglo)");
    Check(Chords::TransposeLine("Sol/Fa", 2, true) == "La/Sol", "slash bass +2 (fix v1.2.0)");

    // ------------------------------------------------------------------ Lyrics
    std::printf("[2] Lyrics (Modo Hinario M16)\n");
    Song song;
    song.id = 1;
    song.title = "Prueba";
    song.lyrics = "[Verso 1]\nGrande es el Senor\nDigno de alabar\n\n[Coro]\nSanto, santo, santo\n\n"
                  "[Verso 2]\nToda lengua confesara\n\n[Coro]\nSanto, santo, santo\n";

    Lyrics::BuildOptions plain;
    plain.titleSlide = false;
    const auto slidesPlain = Lyrics::BuildSlides(song, plain);
    Check(slidesPlain.size() == 4, "lineal: V1 C V2 C (coro repetido se mantiene)");

    Lyrics::BuildOptions hymn;
    hymn.titleSlide = false;
    hymn.chorusInterleave = true;
    const auto slidesHymn = Lyrics::BuildSlides(song, hymn);
    // Modo Hinario: SOLO primer coro; V1 C V2 C pero el coro [Coro] repetido
    // del final se deduplica -> el resultado debe ser V1 C V2 C con UN coro
    // usado (no V1 C C V2 C C).
    Check(slidesHymn.size() == 4, "hinario: 4 slides (V1 C V2 C), sin coros dobles");
    if (slidesHymn.size() == 4) {
        Check(slidesHymn[1].lines[0].text == "Santo, santo, santo", "hinario: coro tras V1");
        Check(slidesHymn[3].lines[0].text == "Santo, santo, santo", "hinario: coro tras V2");
    }

    Song chorusOnly;
    chorusOnly.id = 2;
    chorusOnly.title = "Solo coro";
    chorusOnly.lyrics = "[Coro]\nAleluya, aleluya\nAleluya";
    const auto slidesChorusOnly = Lyrics::BuildSlides(chorusOnly, hymn);
    Check(slidesChorusOnly.size() == 1 &&
              slidesChorusOnly[0].lines[0].text == "Aleluya, aleluya",
          "solo-coros NO se proyecta vacío (fix M16-c)");

    Song longVerse;
    longVerse.id = 3;
    longVerse.title = "Verso largo";
    longVerse.lyrics = "[Coro]\nAleluya gloria a Dios\n\n[Verso 1]\n"
                       "Uno\nSegundo\nTercero\nCuarto\nQuinto\nSexto\n";
    const auto slidesLong = Lyrics::BuildSlides(longVerse, hymn);
    // V1 largo pagina en 2 bloques; el coro debe salir tras el VERSO completo
    Check(slidesLong.size() == 3, "verso largo: 2 bloques + coro al final del verso");
    if (slidesLong.size() == 3) {
        Check(slidesLong[2].lines[0].text == "Aleluya gloria a Dios",
              "coro tras el verso COMPLETO (fix v1.2.0-b)");
    }

    // ------------------------------------------------------------------ BibleRef
    std::printf("[3] BibleRef\n");
    const auto r1 = BibleRef::Resolve("Jn 3:16");
    Check(r1.book == 43 && r1.chapter == 3 && r1.verse == 16, "Jn 3:16");
    const auto r2 = BibleRef::Resolve("salmo 23:1-6");
    Check(r2.book == 19 && r2.chapter == 23 && r2.verse == 1, "salmo 23:1-6");
    const auto r3 = BibleRef::Resolve("1 co 13, 4-7");
    Check(r3.book == 46 && r3.chapter == 13 && r3.verse == 4, "1 co 13,4-7");
    const auto r4 = BibleRef::Resolve("Génesis 1");
    Check(r4.book == 1 && r4.chapter == 1 && r4.verse == 0, "Génesis 1 (acentos)");
    const auto r5 = BibleRef::Resolve("1 sam 3:16");
    Check(r5.book == 9 && r5.chapter == 3 && r5.verse == 16, "1 sam 3:16");
    const auto r6 = BibleRef::Resolve("1sam 3");
    Check(r6.book == 9 && r6.chapter == 3, "1sam 3");
    Check(!BibleRef::Resolve("3:16").Valid(), "sin libro es inválida");

    // ------------------------------------------------------------------ Database
    std::printf("[4] Database (SQLite + FTS5)\n");
    const wxString dbPath = "/tmp/lumina_selftest.sqlite3";
    wxRemoveFile(dbPath);
    Database db;
    wxString err;
    Check(db.Open(dbPath, &err), "apertura de BD");
    if (!db.IsOpen()) {
        std::printf("  (sin BD no se puede continuar: %s)\n", (const char *)err.utf8_str());
        std::printf("== %d/%d checks OK ==\n", g_checks - g_failed, g_checks);
        return g_failed ? 1 : 0;
    }

    Song s1;
    s1.title = "Grande es el Señor";
    s1.artist = "Tradicional";
    s1.key = "Do";
    s1.lyrics = "[Coro]\nGrande es el Senor";
    const int id1 = db.AddSong(s1);
    Check(id1 > 0, "INSERT canción");
    Song s2;
    s2.title = "Amor de Dios";
    s2.artist = "Autor X";
    const int id2 = db.AddSong(s2);
    Check(id2 > 0 && id2 != id1, "segunda canción");

    // FTS con búsqueda por prefijos y acento-insensible
    auto rows = db.SearchSongs({"grande", ""});
    Check(rows.size() == 1 && rows[0].id == id1, "FTS: 'grande'");
    rows = db.SearchSongs({"senor", ""});
    Check(rows.size() == 1, "FTS sin acentos: 'senor' halla 'Señor'");
    // B2: término con %1 no corrompe la consulta
    rows = db.SearchSongs({"100%1", ""});
    Check(rows.empty(), "FTS: '100%1' no corrompe SQL");

    db.SetSongTags(id1, {"lento", "Adoración"});
    auto tags = db.SongTags(id1);
    if (tags.size() != 2)
        std::printf("  (debug tags: %d obtenidas, error BD: %s)\n",
                    (int)tags.size(), (const char *)db.LastError().utf8_str());
    Check(tags.size() == 2, "tags asignadas");
    rows = db.SearchSongs({"", "Lento"});
    Check(rows.size() == 1, "filtro por tag (case-insensitive)");
    auto allTags = db.AllTagNames();
    Check(allTags.size() >= 2, "AllTagNames");

    s1.id = id1;
    Check(db.UpdateSong(s1), "UPDATE canción");
    Check(db.SongById(id1).key == "Do", "SELECT por id");
    db.LogSongUse(id1);
    db.LogSongUse(id1);
    Check(db.SongUseCount(id1) == 2, "estadísticas de uso");

    // Biblia: importación + dedupe (M21) + pasaje + búsqueda
    std::vector<Database::BibleRow> bible;
    for (int v = 1; v <= 10; ++v)
        bible.push_back({ 43, 3, v, wxString::Format("Versículo %d de Juan", v) });
    Check(db.ImportBible("TEST", "Biblia de prueba", bible), "importación biblia");
    Check(db.BibleVerseCount("TEST") == 10, "versículos importados");
    // reimportar NO duplica (borrado previo de la versión)
    Check(db.ImportBible("TEST", "Biblia de prueba", bible), "reimportación");
    Check(db.BibleVerseCount("TEST") == 10, "sin duplicados tras reimportar");
    auto passage = db.BiblePassage("TEST", 43, 3, 1, 3);
    Check(passage.size() == 3, "pasaje Jn 3:1-3");
    auto found = db.BibleSearch("juan", "TEST");
    Check(found.size() == 10, "búsqueda FTS biblia (sin acentos)");

    // Temas
    Theme t = Theme::DefaultTheme();
    t.name = "Tema de prueba";           // el predeterminado ya lo siembra la BD
    const int tid = db.SaveTheme(t);
    Check(tid > 0, "guardar tema");
    auto themes = db.Themes();
    Check(themes.size() >= 1, "listar temas");
    Theme t2 = db.ThemeById(tid);
    Check(t2.name == t.name, "leer tema JSON round-trip");
    db.SetDefaultTheme(tid);
    Check(db.DefaultThemeId() == tid, "tema predeterminado");

    // Cultos
    const int plId = db.CreatePlaylist("Culto test");
    ServiceItem it;
    it.kind = ServiceItem::Song;
    it.refId = id1;
    it.label = s1.title;
    std::vector<ServiceItem> items{ it };
    db.ReplaceItems(plId, items);
    auto loaded = db.PlaylistItems(plId);
    Check(loaded.size() == 1 && loaded[0].label == s1.title, "items de culto round-trip");

    // Medios + tags de recursos
    db.AddMedia("/tmp/fondo.png", 0);
    db.AddMedia("/tmp/video.mp4", 1);
    auto media = db.MediaLibrary();
    Check(media.size() == 2, "biblioteca de medios");
    db.SetResourceTags("media", "/tmp/fondo.png", {"agua", "ocaso"});
    auto mtags = db.ResourceTags("media", "/tmp/fondo.png");
    Check(mtags.size() == 2, "tags de medios");

    // Ajustes
    db.SetSetting("server_port", "8088");
    Check(db.GetSetting("server_port") == "8088", "settings round-trip");
    db.SetSettingInt("server_port", 9090);
    Check(db.GetSettingInt("server_port", 0) == 9090, "settings int");

    // Limpieza de canción (trigger FTS)
    Check(db.DeleteSong(id2), "DELETE canción");
    Check(db.SearchSongs({"amor", ""}).empty(), "FTS sincronizado tras DELETE");

    db.Close();
    wxRemoveFile(dbPath);

    std::printf("== %d/%d checks OK%s ==\n", g_checks - g_failed, g_checks,
                g_failed ? " — HAY FALLOS" : "");
    std::fflush(stdout);
    return g_failed ? 1 : 0;
}
