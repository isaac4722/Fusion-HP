// ============================================================================
//  Fusion-HP / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  fusion_api.cpp : implementación de la API C (frontera ABI). Sin excepciones
//  que crucen la frontera; búfer uniforme out/cap/needed.
// ============================================================================
#include "FusionCore.h"
#include "SongModel.h"
#include "BibleBib.h"
#include "BibleRef.h"
#include "Chords.h"
#include "Utf8.h"

#include <nlohmann/json.hpp>

using namespace fusion;

namespace {

/* Copia 's' al búfer del cliente (patrón out/cap/needed).
   Necesario: needed = len+1 (con NUL). OK si cap alcanza; ERR_LIMIT si no. */
FusionStatus ReturnStr(char* out, int32_t cap, int32_t* needed, const std::string& s) {
    if (!needed) return FUSION_ERR_ARG;
    const int32_t req = (int32_t)s.size() + 1;
    *needed = req;
    if (cap < 0 || (!out && cap > 0)) return FUSION_ERR_ARG;
    if (cap < req) return FUSION_ERR_LIMIT;
    if (!out) return FUSION_ERR_ARG;
    memcpy(out, s.data(), (size_t)s.size());
    out[s.size()] = '\0';
    return FUSION_OK;
}

// Extrae texto UTF-8 del cliente (sin len = NUL-terminado; -1 = medir con strlen).
bool GetIn(const char* p, int32_t len, std::string* out) {
    if (!p) return false;
    if (len < -1) return false;
    if (len == -1) {
        if (!p) return false;
        const size_t n = strlen(p);
        out->assign(p, n);
        return true;
    }
    if (len == 0) { *out = std::string(); return true; }
    out->assign(p, (size_t)len);
    return true;
}

template <typename F>
FusionStatus Api(F f) {
    try { return f(); }
    catch (const json::exception&) { return FUSION_ERR_PARSE; }
    catch (const std::bad_alloc&)  { return FUSION_ERR_LIMIT; }
    catch (const std::exception&)  { return FUSION_ERR_PARSE; }
    catch (...)                     { return FUSION_ERR_PARSE; }
}

// Variante para fusion_create (devuelve puntero, no estado).
template <typename F>
FusionHandle ApiHandle(F f) {
    try { return f(); }
    catch (const json::exception&) { return nullptr; }
    catch (const std::bad_alloc&)  { return nullptr; }
    catch (const std::exception&)  { return nullptr; }
    catch (...)                     { return nullptr; }
}

} // namespace

/* ------------------------------------------------------------------ ciclo */
FusionHandle fusion_create(const FusionConfig* cfg) {
    return ApiHandle([&]() -> FusionHandle {
        if (!cfg) return nullptr;
        FusionConfig c = *cfg;
        if (c.structSize != (int32_t)sizeof(FusionConfig)) return nullptr;  // ABI estricto
        return reinterpret_cast<FusionHandle>(new Engine(c));
    });
}

void fusion_destroy(FusionHandle h) {
    if (!h) return;
    delete reinterpret_cast<Engine*>(h);
}

int32_t fusion_version(char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> FusionStatus {
        Engine* e = nullptr;  // version no necesita handle
        (void)e;
        return ReturnStr(out, cap, needed, std::string("FusionCore ") + "3.0.0");
    });
}

/* --------------------------------------------------- escenario / en vivo */
int32_t fusion_load_scenario(FusionHandle h, const char* jsonText, int32_t len) {
    return Api([&]() -> FusionStatus {
        if (!h) return FUSION_ERR_ARG;
        std::string s;
        if (!GetIn(jsonText, len, &s)) return FUSION_ERR_ARG;
        return reinterpret_cast<Engine*>(h)->LoadScenario(s);
    });
}

int32_t fusion_show_slide(FusionHandle h, int32_t index) {
    if (!h) return FUSION_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->ShowSlide(index);
}

int32_t fusion_next(FusionHandle h) {
    if (!h) return FUSION_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->Next();
}

int32_t fusion_prev(FusionHandle h) {
    if (!h) return FUSION_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->Prev();
}

int32_t fusion_black(FusionHandle h, int32_t on) {
    if (!h) return FUSION_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->Black(on != 0);
}

int32_t fusion_clear(FusionHandle h) {
    if (!h) return FUSION_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->Clear();
}

int32_t fusion_set_theme(FusionHandle h, const char* jsonText, int32_t len) {
    return Api([&]() -> FusionStatus {
        if (!h) return FUSION_ERR_ARG;
        std::string s;
        if (!GetIn(jsonText, len, &s)) return FUSION_ERR_ARG;
        return reinterpret_cast<Engine*>(h)->SetTheme(s);
    });
}

int32_t fusion_state_json(FusionHandle h, char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> FusionStatus {
        if (!h) return FUSION_ERR_ARG;
        return ReturnStr(out, cap, needed, reinterpret_cast<Engine*>(h)->StateJson());
    });
}

int32_t fusion_ping(FusionHandle h, const char* msg, int32_t len) {
    return Api([&]() -> FusionStatus {
        if (!h) return FUSION_ERR_ARG;
        std::string s;
        if (!GetIn(msg, len, &s)) return FUSION_ERR_ARG;
        return reinterpret_cast<Engine*>(h)->Ping(s);
    });
}

/* ------------------------------------------------ proyección (no headless) */
int32_t fusion_projector_show(FusionHandle h, int32_t screenIndex, int32_t fullscreen) {
    if (!h) return FUSION_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->ProjectorShow(screenIndex, fullscreen != 0);
}

int32_t fusion_projector_hide(FusionHandle h) {
    if (!h) return FUSION_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->ProjectorHide();
}

int32_t fusion_render_preview_png(FusionHandle h, int32_t slideIndex,
                                  char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> FusionStatus {
        if (!h) return FUSION_ERR_ARG;
        std::string png;
        if (reinterpret_cast<Engine*>(h)->RenderPreviewPng(slideIndex, &png) != 0)
            return FUSION_ERR_UNSUPPORTED;
        // binario: needed = longitud exacta (sin NUL); el cliente usa len de vuelta
        if (!needed) return FUSION_ERR_ARG;
        *needed = (int32_t)png.size();
        if (cap < 0 || (!out && cap > 0)) return FUSION_ERR_ARG;
        if ((int32_t)png.size() > cap) return FUSION_ERR_LIMIT;
        if (cap > 0 && out) memcpy(out, png.data(), png.size());
        return FUSION_OK;
    });
}

/* ---------------------------------------------------- canciones / biblia */
int32_t fusion_song_parse(const char* jsonText, int32_t len,
                          char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> FusionStatus {
        std::string s;
        if (!GetIn(jsonText, len, &s)) return FUSION_ERR_ARG;
        json o = json::parse(s);                       // lanza → ERR_PARSE
        json res = SongModel::ParseAndBuildSlides(o);
        return ReturnStr(out, cap, needed, res.dump());
    });
}

int32_t fusion_bib_parse(const char* data, int32_t len, const char* optionsJson,
                         char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> FusionStatus {
        if (!data) return FUSION_ERR_ARG;
        if (len < -1) return FUSION_ERR_ARG;
        if (len == -1) len = (int32_t)strlen(data);   // convención NUL-terminada
        if (len <= 0) return FUSION_ERR_ARG;
        std::string opts = optionsJson ? std::string(optionsJson) : std::string();
        long long maxBytes = 64LL * 1024 * 1024;
        if (!opts.empty()) {
            json o = json::parse(opts);
            if (o.contains("maxBytes") && o["maxBytes"].is_number())
                maxBytes = o["maxBytes"].get<long long>();
        }
        if ((long long)len > maxBytes) return FUSION_ERR_LIMIT;
        BibleBib::Stats st;
        if (!BibleBib::Parse(std::string(data, (size_t)len), &st, nullptr, maxBytes))
            return FUSION_ERR_PARSE;
        return ReturnStr(out, cap, needed, st.ToJson().dump());
    });
}

int32_t fusion_bible_ref_resolve(const char* refUtf8,
                                 char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> FusionStatus {
        if (!refUtf8) return FUSION_ERR_ARG;
        BibleRef::VerseRef r = BibleRef::Resolve(std::string(refUtf8));
        json j;
        j["book"] = r.book; j["chapter"] = r.chapter; j["verse"] = r.verse;
        j["name"] = r.bookName; j["valid"] = r.Valid() ? 1 : 0;
        return ReturnStr(out, cap, needed, j.dump());
    });
}

int32_t fusion_chords_transpose(const char* line, int32_t semitones, int32_t latin,
                                char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> FusionStatus {
        if (!line) return FUSION_ERR_ARG;
        std::string res = Chords::TransposeLine(std::string(line), semitones, latin != 0);
        json j; j["line"] = res;
        return ReturnStr(out, cap, needed, j.dump());
    });
}

/* ------------------------------------------------------- almacenamiento */
int32_t fusion_db_open(FusionHandle h, const char* pathUtf8) {
    return Api([&]() -> FusionStatus {
        if (!h || !pathUtf8) return FUSION_ERR_ARG;
        return reinterpret_cast<Engine*>(h)->DbOpen(std::string(pathUtf8));
    });
}

int32_t fusion_db_close(FusionHandle h) {
    if (!h) return FUSION_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->DbClose();
}

int32_t fusion_db_exec(FusionHandle h, const char* sqlJson,
                       char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> FusionStatus {
        if (!h) return FUSION_ERR_ARG;
        std::string s;
        if (!GetIn(sqlJson, -1, &s)) return FUSION_ERR_ARG;
        if (s.empty()) return FUSION_ERR_ARG;
        std::string res;
        const FusionStatus st = reinterpret_cast<Engine*>(h)->DbExec(s, &res);
        if (st != FUSION_OK) return st;
        return ReturnStr(out, cap, needed, res);
    });
}
