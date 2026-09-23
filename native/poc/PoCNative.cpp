// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  PoCNative.cpp : PoC de consumo del API C desde C++ (equivalente nativo del
//  PoC.Managed). Ejercicio headless completo de la ABI: ciclo, UTF-8 con
//  callbacks, canción JSON, .BIB, referencias, acordes, escenario/en vivo,
//  BD+FTS5. Imprime "POC-NATIVE PASS n/n" y sale 0/1.
// ============================================================================
#include "lumina/lumina.h"

#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <string>
#include <thread>
#include <vector>

#if !defined(_WIN32)
#include <unistd.h>
#else
#include <windows.h>      // GetCurrentProcessId (nombre único de BD temporal)
#endif

namespace {

int g_checks = 0, g_failed = 0;

#define CHECK(cond)                                                            \
    do {                                                                       \
        ++g_checks;                                                            \
        if (!(cond)) {                                                         \
            ++g_failed;                                                        \
            std::printf("  FALLO: %s (linea %d)\n", #cond, __LINE__);          \
        }                                                                      \
    } while (0)

void Section(const char* name) { std::printf("%s\n", name); }

/* Patrón de búfer (solo APIs puras SIN efectos secundarios). */
template <typename Fn>
bool ApiCall(Fn fn, std::string* out) {
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

/* Una sola llamada con búfer generoso (operaciones CON efectos: db_exec). */
template <typename Fn>
bool ApiCallOnce(Fn fn, std::string* out) {
    std::vector<char> buf(1 << 20, '\0');
    int32_t needed = (int32_t)buf.size();
    const LuminaStatus s = fn(buf.data(), needed, &needed);
    if (s != LUMINA_OK) return false;
    size_t len = 0;
    while (len < buf.size() && buf[len] != '\0') ++len;
    if (out) out->assign(buf.data(), len);
    return true;
}

std::string TempDbPath() {
    char buf[256];
#if defined(_WIN32)
    std::snprintf(buf, sizeof(buf), "lumina_poc_native_%lu.db",
                  (unsigned long)GetCurrentProcessId());
#else
    std::snprintf(buf, sizeof(buf), "/tmp/lumina_poc_native_%d.db", (int)getpid());
#endif
    return std::string(buf);
}

std::atomic<int> g_stateCount{0};
std::atomic<int> g_slideCount{0};
std::atomic<int> g_pongCount{0};
std::string g_lastPong;

void OnEvent(void* /*user*/, int32_t code, const char* payload, int32_t len) {
    if (code == LUMINA_EV_STATE) g_stateCount++;
    else if (code == LUMINA_EV_SLIDE_CHANGED) g_slideCount++;
    else if (code == LUMINA_EV_PONG) {
        if (payload && len > 0) g_lastPong.assign(payload, (size_t)len);
        g_pongCount++;
    } else if (code == LUMINA_EV_ERROR && payload && len > 0) {
        std::printf("  EV_ERROR: %.120s\n", payload);
    }
}

} // namespace

int main() {
    std::printf("== PoC nativo del nucleo LuminaCore (headless) ==\n");

    /* ---------------------------------------------------------- 1 version */
    Section("[1] lumina_version");
    std::string v;
    CHECK(ApiCall([&](char* o, int32_t c, int32_t* n) {
        return lumina_version(o, c, n);
    }, &v));
    CHECK(v.find("LuminaCore") == 0 && v.find("5.2") != std::string::npos);
    std::printf("        version = \"%s\"\n", v.c_str());

    /* ---------------------------------------------------- 2 ciclo+UTF-8 */
    Section("[2] ciclo de vida + UTF-8 por eventos");
    LuminaConfig cfg;
    std::memset(&cfg, 0, sizeof(cfg));
    cfg.structSize = (int32_t)sizeof(LuminaConfig);
    cfg.headless = 1;
    cfg.onEvent = &OnEvent;
    LuminaHandle h = lumina_create(&cfg);
    CHECK(h != nullptr);
    const char* msg = "¡Canción — ñ Á é í ó ú!";
    CHECK(lumina_ping(h, msg, -1) == LUMINA_OK);
    const auto t0 = std::chrono::steady_clock::now();
    while (g_pongCount.load() < 1 &&
           std::chrono::steady_clock::now() - t0 < std::chrono::seconds(5)) {
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    CHECK(g_lastPong == msg);

    /* --------------------------------------------------- 3 canción JSON */
    Section("[3] lumina_song_parse");
    const char* songJson =
        "{\"title\":\"Cancion PoC\",\"artist\":\"Nativo\",\"key\":\"Do\",\"bpm\":90,"
        "\"blocks\":[{\"label\":\"Verso 1\",\"lines\":[\"uno\",\"dos\"]},"
        "{\"label\":\"Coro\",\"lines\":[\"coro final\"]}]}";
    std::string songRes;
    CHECK(ApiCall([&](char* o, int32_t c, int32_t* n) {
        return lumina_song_parse(songJson, -1, o, c, n);
    }, &songRes));
    CHECK(songRes.find("\"ok\":1") != std::string::npos);
    CHECK(songRes.find("\"kind\":0") != std::string::npos);   // slide de título
    std::printf("        slides en res: %s\n",
                songRes.find("\"refLabel\":\"Coro\"") != std::string::npos ? "coro OK" : "?");

    /* -------------------------------------------------------- 4 .BIB */
    Section("[4] lumina_bib_parse");
    const char* bib =
        "#BIB 1\n#VERSION TST\n#NAME Biblia PoC\n"
        "Génesis\t1\t1\tEn el principio creó Dios los cielos y la tierra.\n"
        "Génesis\t1\t2\tY la tierra estaba desordenada y vacía.\n"
        "Juan\t3\t16\tPorque de tal manera amó Dios al mundo.\n"
        "Apocalipsis\t22\t21\tLa gracia de nuestro Señor sea con vosotros. Amén.\n";
    std::string bibRes;
    CHECK(ApiCall([&](char* o, int32_t c, int32_t* n) {
        return lumina_bib_parse(bib, -1, nullptr, o, c, n);
    }, &bibRes));
    CHECK(bibRes.find("\"ok\":1") != std::string::npos);
    CHECK(bibRes.find("\"verses\":4") != std::string::npos);
    CHECK(bibRes.find("\"version\":\"TST\"") != std::string::npos);

    /* ------------------------------------------------------ 5 referencia */
    Section("[5] lumina_bible_ref_resolve");
    std::string refRes;
    CHECK(ApiCall([&](char* o, int32_t c, int32_t* n) {
        return lumina_bible_ref_resolve("Jn 3:16", o, c, n);
    }, &refRes));
    CHECK(refRes.find("\"book\":43") != std::string::npos);
    CHECK(refRes.find("\"chapter\":3") != std::string::npos);
    CHECK(refRes.find("\"verse\":16") != std::string::npos);
    CHECK(refRes.find("\"name\":\"Juan\"") != std::string::npos);

    /* -------------------------------------------------------- 6 acordes */
    Section("[6] lumina_chords_transpose");
    std::string chordRes;
    CHECK(ApiCall([&](char* o, int32_t c, int32_t* n) {
        return lumina_chords_transpose("Do  Sol", 2, 1, o, c, n);
    }, &chordRes));
    CHECK(chordRes.find("Re") != std::string::npos &&
          chordRes.find("La") != std::string::npos);

    /* ------------------------------------------- 7 escenario + en vivo */
    Section("[7] escenario + en vivo (eventos)");
    const char* scenario =
        "{\"name\":\"PoC\",\"song\":{\"title\":\"Cancion PoC\",\"lyrics\":"
        "\"[Verso 1]\\nuno\\ndos\\n\\n[Coro]\\ncoro final\\n\"}}";
    g_stateCount = 0; g_slideCount = 0;
    CHECK(lumina_load_scenario(h, scenario, -1) == LUMINA_OK);
    auto WaitStates = [&](int target) {
        const auto t = std::chrono::steady_clock::now();
        while (g_stateCount.load() < target &&
               std::chrono::steady_clock::now() - t < std::chrono::seconds(5))
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        return g_stateCount.load() >= target;
    };
    auto WaitSlides = [&](int target) {
        const auto t = std::chrono::steady_clock::now();
        while (g_slideCount.load() < target &&
               std::chrono::steady_clock::now() - t < std::chrono::seconds(5))
            std::this_thread::sleep_for(std::chrono::milliseconds(10));
        return g_slideCount.load() >= target;
    };
    CHECK(WaitStates(1));                       // estado inicial
    CHECK(lumina_next(h) == LUMINA_OK);
    CHECK(WaitSlides(1));                       // índice 0
    CHECK(lumina_next(h) == LUMINA_OK);
    CHECK(lumina_next(h) == LUMINA_OK);         // índice 2
    CHECK(WaitSlides(3));
    std::string st;
    CHECK(ApiCallOnce([&](char* o, int32_t c, int32_t* n) {
        return lumina_state_json(h, o, c, n);
    }, &st));
    CHECK(st.find("\"current\":2") != std::string::npos);
    CHECK(lumina_black(h, 1) == LUMINA_OK);
    CHECK(ApiCallOnce([&](char* o, int32_t c, int32_t* n) {
        return lumina_state_json(h, o, c, n);
    }, &st));
    CHECK(st.find("\"black\":true") != std::string::npos);

    // v6.0.0: ítem de texto con «highlight» (resaltado en proyección) — el
    // campo se acepta, el escenario aplanan las slides y el estado las cuenta.
    const char* scenarioHl =
        "{\"name\":\"HL\",\"items\":["
        "{\"kind\":\"text\",\"title\":\"Aviso\",\"text\":\"linea uno\\nlinea dos\","
        "\"maxLinesPerSlide\":1,\"highlight\":\"uno\"}]}";
    CHECK(lumina_load_scenario(h, scenarioHl, -1) == LUMINA_OK);
    CHECK(ApiCallOnce([&](char* o, int32_t c, int32_t* n) {
        return lumina_state_json(h, o, c, n);
    }, &st));
    CHECK(st.find("\"slideCount\":2") != std::string::npos);   // 2 líneas → 2 slides

    /* -------------------------------------------------------- 8 BD+FTS */
    Section("[8] lumina_db_* (SQLite+FTS5)");
    const std::string dbPath = TempDbPath();
    std::remove(dbPath.c_str());
    CHECK(lumina_db_open(h, dbPath.c_str()) == LUMINA_OK);
    std::string dbRes;
    CHECK(ApiCallOnce([&](char* o, int32_t c, int32_t* n) {
        return lumina_db_exec(h,
            "{\"sql\":\"INSERT INTO songs(title,author,lyrics,tags) VALUES(?1,?2,?3,?4)\","
            "\"params\":[\"Grande es el Senor\",\"PoC\",\"[Coro]\\naleluya\",\"test\"]}",
            o, c, n);
    }, &dbRes));
    CHECK(dbRes.find("\"changes\":1") != std::string::npos);
    CHECK(ApiCallOnce([&](char* o, int32_t c, int32_t* n) {
        return lumina_db_exec(h,
            "{\"sql\":\"SELECT title FROM songs_fts WHERE songs_fts MATCH ?1\","
            "\"params\":[\"aleluya\"]}", o, c, n);
    }, &dbRes));
    CHECK(dbRes.find("Grande es el Senor") != std::string::npos);
    CHECK(lumina_db_close(h) == LUMINA_OK);
    std::remove(dbPath.c_str());

    /* ---------------------------------------------------------- cierre */
    lumina_destroy(h);
    std::printf("\n== POC-NATIVE %s %d/%d ==\n", g_failed == 0 ? "PASS" : "FAIL",
                g_checks - g_failed, g_checks);
    return g_failed == 0 ? 0 : 1;
}
