// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  lumina_api.cpp : implementación de la API C (frontera ABI). Sin excepciones
//  que crucen la frontera; búfer uniforme out/cap/needed.
// ============================================================================
#include "LuminaCore.h"
#include "SongModel.h"
#include "BibleBib.h"
#include "BibleRef.h"
#include "Chords.h"
#include "Utf8.h"

// v7.0.0 «ULTRA» — F0: incluidos EN ESTA UNIDAD (el CMake del núcleo no se
// toca: restricción explícita del Plan de Ultra Implementación).
#include "Bootstrap.cpp"    // F0.01-F0.04: detección SO/arquitectura/.NET/perfiles
#include "NativeLog.cpp"    // F0.09: log nativo estructurado §10.1
#include "IpcV1.cpp"        // F0.05: IPC ipc.v1 (códec + cola + servidor pipes)

#include <nlohmann/json.hpp>

using namespace lumina;

namespace {

/* Copia 's' al búfer del cliente (patrón out/cap/needed).
   Necesario: needed = len+1 (con NUL). OK si cap alcanza; ERR_LIMIT si no. */
LuminaStatus ReturnStr(char* out, int32_t cap, int32_t* needed, const std::string& s) {
    if (!needed) return LUMINA_ERR_ARG;
    const int32_t req = (int32_t)s.size() + 1;
    *needed = req;
    if (cap < 0 || (!out && cap > 0)) return LUMINA_ERR_ARG;
    if (cap < req) return LUMINA_ERR_LIMIT;
    if (!out) return LUMINA_ERR_ARG;
    memcpy(out, s.data(), (size_t)s.size());
    out[s.size()] = '\0';
    return LUMINA_OK;
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
LuminaStatus Api(F f) {
    try { return f(); }
    catch (const json::exception&) { return LUMINA_ERR_PARSE; }
    catch (const std::bad_alloc&)  { return LUMINA_ERR_LIMIT; }
    catch (const std::exception&)  { return LUMINA_ERR_PARSE; }
    catch (...)                     { return LUMINA_ERR_PARSE; }
}

// Variante para lumina_create (devuelve puntero, no estado).
template <typename F>
LuminaHandle ApiHandle(F f) {
    try { return f(); }
    catch (const json::exception&) { return nullptr; }
    catch (const std::bad_alloc&)  { return nullptr; }
    catch (const std::exception&)  { return nullptr; }
    catch (...)                     { return nullptr; }
}

} // namespace

/* ------------------------------------------------------------------ ciclo */
LuminaHandle lumina_create(const LuminaConfig* cfg) {
    return ApiHandle([&]() -> LuminaHandle {
        if (!cfg) return nullptr;
        LuminaConfig c = *cfg;
        if (c.structSize != (int32_t)sizeof(LuminaConfig)) return nullptr;  // ABI estricto
        return reinterpret_cast<LuminaHandle>(new Engine(c));
    });
}

void lumina_destroy(LuminaHandle h) {
    if (!h) return;
    delete reinterpret_cast<Engine*>(h);
}

int32_t lumina_version(char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> LuminaStatus {
        Engine* e = nullptr;  // version no necesita handle
        (void)e;
        return ReturnStr(out, cap, needed, std::string("LuminaCore ") + "7.1.0-operador");
    });
}

/* --------------------------------------------------- escenario / en vivo */
int32_t lumina_load_scenario(LuminaHandle h, const char* jsonText, int32_t len) {
    return Api([&]() -> LuminaStatus {
        if (!h) return LUMINA_ERR_ARG;
        std::string s;
        if (!GetIn(jsonText, len, &s)) return LUMINA_ERR_ARG;
        return reinterpret_cast<Engine*>(h)->LoadScenario(s);
    });
}

int32_t lumina_show_slide(LuminaHandle h, int32_t index) {
    if (!h) return LUMINA_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->ShowSlide(index);
}

int32_t lumina_next(LuminaHandle h) {
    if (!h) return LUMINA_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->Next();
}

int32_t lumina_prev(LuminaHandle h) {
    if (!h) return LUMINA_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->Prev();
}

int32_t lumina_black(LuminaHandle h, int32_t on) {
    if (!h) return LUMINA_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->Black(on != 0);
}

int32_t lumina_clear(LuminaHandle h) {
    if (!h) return LUMINA_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->Clear();
}

int32_t lumina_set_theme(LuminaHandle h, const char* jsonText, int32_t len) {
    return Api([&]() -> LuminaStatus {
        if (!h) return LUMINA_ERR_ARG;
        std::string s;
        if (!GetIn(jsonText, len, &s)) return LUMINA_ERR_ARG;
        return reinterpret_cast<Engine*>(h)->SetTheme(s);
    });
}

int32_t lumina_state_json(LuminaHandle h, char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> LuminaStatus {
        if (!h) return LUMINA_ERR_ARG;
        return ReturnStr(out, cap, needed, reinterpret_cast<Engine*>(h)->StateJson());
    });
}

int32_t lumina_ping(LuminaHandle h, const char* msg, int32_t len) {
    return Api([&]() -> LuminaStatus {
        if (!h) return LUMINA_ERR_ARG;
        std::string s;
        if (!GetIn(msg, len, &s)) return LUMINA_ERR_ARG;
        return reinterpret_cast<Engine*>(h)->Ping(s);
    });
}

/* ------------------------------------------------ proyección (no headless) */
int32_t lumina_projector_show(LuminaHandle h, int32_t screenIndex, int32_t fullscreen) {
    if (!h) return LUMINA_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->ProjectorShow(screenIndex, fullscreen != 0);
}

int32_t lumina_projector_hide(LuminaHandle h) {
    if (!h) return LUMINA_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->ProjectorHide();
}

// v6.0.0 «HORIZONTE»: transición entre slides (0=corte, 1=fundido; 0..5000 ms).
// Valida rango ANTES de tocar el motor (headless guarda y refleja en estado).
int32_t lumina_set_transition(LuminaHandle h, int32_t mode, int32_t durationMs) {
    if (!h) return LUMINA_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->SetTransition(mode, durationMs);
}

int32_t lumina_render_preview_png(LuminaHandle h, int32_t slideIndex,
                                  char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> LuminaStatus {
        if (!h) return LUMINA_ERR_ARG;
        std::string png;
        if (reinterpret_cast<Engine*>(h)->RenderPreviewPng(slideIndex, &png) != 0)
            return LUMINA_ERR_UNSUPPORTED;
        // binario: needed = longitud exacta (sin NUL); el cliente usa len de vuelta
        if (!needed) return LUMINA_ERR_ARG;
        *needed = (int32_t)png.size();
        if (cap < 0 || (!out && cap > 0)) return LUMINA_ERR_ARG;
        if ((int32_t)png.size() > cap) return LUMINA_ERR_LIMIT;
        if (cap > 0 && out) memcpy(out, png.data(), png.size());
        return LUMINA_OK;
    });
}

/* ---------------------------------------------------- canciones / biblia */
int32_t lumina_song_parse(const char* jsonText, int32_t len,
                          char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> LuminaStatus {
        std::string s;
        if (!GetIn(jsonText, len, &s)) return LUMINA_ERR_ARG;
        json o = json::parse(s);                       // lanza → ERR_PARSE
        json res = SongModel::ParseAndBuildSlides(o);
        return ReturnStr(out, cap, needed, res.dump());
    });
}

int32_t lumina_bib_parse(const char* data, int32_t len, const char* optionsJson,
                         char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> LuminaStatus {
        if (!data) return LUMINA_ERR_ARG;
        if (len < -1) return LUMINA_ERR_ARG;
        if (len == -1) len = (int32_t)strlen(data);   // convención NUL-terminada
        if (len <= 0) return LUMINA_ERR_ARG;
        std::string opts = optionsJson ? std::string(optionsJson) : std::string();
        long long maxBytes = 64LL * 1024 * 1024;
        if (!opts.empty()) {
            json o = json::parse(opts);
            if (o.contains("maxBytes") && o["maxBytes"].is_number())
                maxBytes = o["maxBytes"].get<long long>();
        }
        if ((long long)len > maxBytes) return LUMINA_ERR_LIMIT;
        BibleBib::Stats st;
        if (!BibleBib::Parse(std::string(data, (size_t)len), &st, nullptr, maxBytes))
            return LUMINA_ERR_PARSE;
        return ReturnStr(out, cap, needed, st.ToJson().dump());
    });
}

int32_t lumina_bible_ref_resolve(const char* refUtf8,
                                 char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> LuminaStatus {
        if (!refUtf8) return LUMINA_ERR_ARG;
        BibleRef::VerseRef r = BibleRef::Resolve(std::string(refUtf8));
        json j;
        j["book"] = r.book; j["chapter"] = r.chapter; j["verse"] = r.verse;
        j["name"] = r.bookName; j["valid"] = r.Valid() ? 1 : 0;
        return ReturnStr(out, cap, needed, j.dump());
    });
}

int32_t lumina_chords_transpose(const char* line, int32_t semitones, int32_t latin,
                                char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> LuminaStatus {
        if (!line) return LUMINA_ERR_ARG;
        std::string res = Chords::TransposeLine(std::string(line), semitones, latin != 0);
        json j; j["line"] = res;
        return ReturnStr(out, cap, needed, j.dump());
    });
}

/* ------------------------------------------------ v7.0.0 «ULTRA» ------ */

/* F0.01-F0.03: entorno + decisión de arranque. Sin handle: es previo a la
   creación del motor (el launcher la usa ANTES de cargar C#). */
int32_t lumina_env_detect(const char* probeJson, char* out, int32_t cap,
                          int32_t* needed) {
    return Api([&]() -> LuminaStatus {
        std::string probe;
        if (probeJson) probe = probeJson;
        return ReturnStr(out, cap, needed,
                         Engine::DetectEnvJson(probe, false, false,
                                               false, false, false));
    });
}

/* F0.09: log nativo. */
int32_t lumina_log_open(LuminaHandle h, const char* optsJson) {
    return Api([&]() -> LuminaStatus {
        if (!h) return LUMINA_ERR_ARG;
        return reinterpret_cast<Engine*>(h)->LogOpen(
            optsJson ? std::string(optsJson) : std::string());
    });
}

int32_t lumina_log_write(LuminaHandle h, const char* entryJson) {
    return Api([&]() -> LuminaStatus {
        if (!h) return LUMINA_ERR_ARG;
        return reinterpret_cast<Engine*>(h)->LogWrite(
            entryJson ? std::string(entryJson) : std::string());
    });
}

int32_t lumina_log_stats(LuminaHandle h, char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> LuminaStatus {
        if (!h) return LUMINA_ERR_ARG;
        return ReturnStr(out, cap, needed,
                         reinterpret_cast<Engine*>(h)->LogStatsJson());
    });
}

/* F0.05: IPC ipc.v1. */
int32_t lumina_ipc_start(LuminaHandle h, const char* optsJson) {
    return Api([&]() -> LuminaStatus {
        if (!h) return LUMINA_ERR_ARG;
        return reinterpret_cast<Engine*>(h)->IpcStart(
            optsJson ? std::string(optsJson) : std::string());
    });
}

int32_t lumina_ipc_stop(LuminaHandle h) {
    if (!h) return LUMINA_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->IpcStop();
}

int32_t lumina_ipc_stats(LuminaHandle h, char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> LuminaStatus {
        if (!h) return LUMINA_ERR_ARG;
        return ReturnStr(out, cap, needed,
                         reinterpret_cast<Engine*>(h)->IpcStatsJson());
    });
}

/* Códec puro ipc.v1 (pruebas + cliente nativo). */
int32_t lumina_ipc_frame_encode(int32_t type, const char* payloadUtf8,
                                int32_t len, uint8_t* out, int32_t cap,
                                int32_t* needed) {
    return Api([&]() -> LuminaStatus {
        std::string payload;
        if (!GetIn(payloadUtf8, len, &payload)) return LUMINA_ERR_ARG;
        std::vector<uint8_t> fr = ipc::EncodeFrame((uint16_t)type, payload);
        if (!needed) return LUMINA_ERR_ARG;
        *needed = (int32_t)fr.size();          // binario: sin NUL final
        if (cap < 0 || (!out && cap > 0)) return LUMINA_ERR_ARG;
        if ((int32_t)fr.size() > cap) return LUMINA_ERR_LIMIT;
        if (cap > 0 && out) memcpy(out, fr.data(), fr.size());
        return LUMINA_OK;
    });
}

void* lumina_ipc_decoder_new(void) {
    return (void*)new ipc::FrameDecoder();
}

void lumina_ipc_decoder_free(void* dec) {
    delete reinterpret_cast<ipc::FrameDecoder*>(dec);
}

int32_t lumina_ipc_decoder_feed(void* dec, const uint8_t* bytes, int32_t len,
                                int32_t* outType, char* out, int32_t cap,
                                int32_t* needed, int32_t* consumed) {
    return Api([&]() -> LuminaStatus {
        if (!dec) return LUMINA_ERR_ARG;
        if (len < 0 || (!bytes && len > 0)) return LUMINA_ERR_ARG;
        uint16_t type = 0; std::string payload; size_t used = 0;
        const int rc = reinterpret_cast<ipc::FrameDecoder*>(dec)->Feed(
            bytes, (size_t)len, &type, &payload, &used);
        if (consumed) *consumed = (int32_t)used;
        if (rc <= 0) return (LuminaStatus)rc;  // 0=falta, <0=protocolo
        if (outType) *outType = (int32_t)type;
        return ReturnStr(out, cap, needed, payload);
    });
}

/* F1.03: líneas. */
int32_t lumina_line_next(LuminaHandle h) {
    if (!h) return LUMINA_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->LineNext();
}

int32_t lumina_line_prev(LuminaHandle h) {
    if (!h) return LUMINA_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->LinePrev();
}

int32_t lumina_line_set(LuminaHandle h, int32_t lineIndex) {
    if (!h) return LUMINA_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->LineSet(lineIndex);
}

/* F3.04: Lower Third nativo. */
int32_t lumina_lower_third(LuminaHandle h, const char* json) {
    return Api([&]() -> LuminaStatus {
        if (!h) return LUMINA_ERR_ARG;
        if (!json) return LUMINA_ERR_ARG;
        return reinterpret_cast<Engine*>(h)->LowerThird(std::string(json));
    });
}

/* ------------------------------------------------------- almacenamiento */
int32_t lumina_db_open(LuminaHandle h, const char* pathUtf8) {
    return Api([&]() -> LuminaStatus {
        if (!h || !pathUtf8) return LUMINA_ERR_ARG;
        return reinterpret_cast<Engine*>(h)->DbOpen(std::string(pathUtf8));
    });
}

int32_t lumina_db_close(LuminaHandle h) {
    if (!h) return LUMINA_ERR_ARG;
    return reinterpret_cast<Engine*>(h)->DbClose();
}

int32_t lumina_db_exec(LuminaHandle h, const char* sqlJson,
                       char* out, int32_t cap, int32_t* needed) {
    return Api([&]() -> LuminaStatus {
        if (!h) return LUMINA_ERR_ARG;
        std::string s;
        if (!GetIn(sqlJson, -1, &s)) return LUMINA_ERR_ARG;
        if (s.empty()) return LUMINA_ERR_ARG;
        std::string res;
        const LuminaStatus st = reinterpret_cast<Engine*>(h)->DbExec(s, &res);
        if (st != LUMINA_OK) return st;
        return ReturnStr(out, cap, needed, res);
    });
}
