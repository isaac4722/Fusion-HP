// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Engine.cpp : motor del núcleo híbrido — estado, escenario, hilo de eventos.
// ============================================================================
#include "LuminaCore.h"
#include "Utf8.h"
#include "SongModel.h"
#include "Lyrics.h"
#include "Scripture.h"
#include "Storage.h"
#include "BibleRef.h"

#ifdef LUMINA_HAS_WIN32
#include "Projector.h"
#endif

#include <nlohmann/json.hpp>

namespace lumina {

static const char* kVersion = "4.2.0";

/* ------------------------------------------------------------- helpers -- */
// (H-a: SlideToJson no usado fue retirado — warning -Wunused-function;
//  la serialización de slides vive en SongModel.cpp / SongModel::SlideToJson.)

json Theme::ToJson() const {
    json j;
    j["name"]=name; j["bgColor"]=bgColor; j["fgColor"]=fgColor; j["accentColor"]=accentColor;
    j["fontFace"]=fontFace; j["fontSize"]=fontSize; j["bold"]=bold;
    j["imagePath"]=imagePath; j["imageMode"]=imageMode;
    j["outlineWidth"]=outlineWidth; j["shadowAlpha"]=shadowAlpha;
    j["uppercase"]=uppercase; j["lineSpacing"]=lineSpacing;
    return j;
}

Theme Theme::FromJson(const json& o, const Theme& fallback) {
    Theme t = fallback;
    auto gs = [&](const char* k, std::string* d) {
        if (o.contains(k) && o[k].is_string()) *d = o[k].get<std::string>();
    };
    gs("name",&t.name); gs("bgColor",&t.bgColor); gs("fgColor",&t.fgColor);
    gs("accentColor",&t.accentColor); gs("fontFace",&t.fontFace); gs("imagePath",&t.imagePath);
    if (o.contains("fontSize")   && o["fontSize"].is_number_integer()) t.fontSize   = o["fontSize"];
    if (o.contains("imageMode")  && o["imageMode"].is_number_integer()) t.imageMode = o["imageMode"];
    if (o.contains("shadowAlpha")&& o["shadowAlpha"].is_number_integer()) t.shadowAlpha = o["shadowAlpha"];
    if (o.contains("bold")       && o["bold"].is_boolean())   t.bold       = o["bold"];
    if (o.contains("uppercase")  && o["uppercase"].is_boolean()) t.uppercase = o["uppercase"];
    if (o.contains("outlineWidth") && o["outlineWidth"].is_number()) t.outlineWidth = o["outlineWidth"];
    if (o.contains("lineSpacing")  && o["lineSpacing"].is_number()) t.lineSpacing  = o["lineSpacing"];
    return t;
}

std::string Song::LyricsText() const {
    if (!blocks.empty()) {
        std::string out;
        for (const SongBlock& b : blocks) {
            if (!b.label.empty()) out += "[" + b.label + "]\n";
            for (const std::string& l : b.lines) out += l + "\n";
            out += "\n";
        }
        return out;
    }
    return lyrics;
}

/* --------------------------------------------------------------- Engine -- */
Engine::Engine(const LuminaConfig& cfg) : cfg_(cfg) {
    if (cfg_.structSize != (int32_t)sizeof(LuminaConfig)) {
        // ABI distinto: aceptar pero registrar
        PushEvent(LUMINA_EV_LOG, "{\"level\":\"warn\",\"msg\":\"structSize distinto\"}");
    }
#ifdef LUMINA_HAS_WIN32
    if (!cfg_.headless) projector_.reset(new Projector());
#endif
    db_.reset(new Database());
    started_ = true;
    eventThread_ = std::thread(&Engine::EventLoop, this);
}

Engine::~Engine() {
    stop_ = true;
    evCv_.notify_all();
    if (eventThread_.joinable()) eventThread_.join();
#ifdef LUMINA_HAS_WIN32
    if (projector_) { projector_->Close(); projector_.reset(); }
#endif
    db_.reset();
}

std::string Engine::Version() const { return kVersion; }

void Engine::PushEvent(int32_t code, const std::string& payload) {
    if (!cfg_.onEvent) return;
    std::lock_guard<std::mutex> lk(evMx_);
    if (evQueue_.size() > 4096) evQueue_.pop_front();   // no crecer sin límite
    evQueue_.push_back(Event{code, payload});
    evCv_.notify_one();
}

void Engine::EventLoop() {
    for (;;) {
        Event ev;
        {
            std::unique_lock<std::mutex> lk(evMx_);
            evCv_.wait(lk, [&] { return stop_ || !evQueue_.empty(); });
            if (stop_ && evQueue_.empty()) return;
            ev = evQueue_.front();
            evQueue_.pop_front();
        }
        cfg_.onEvent(cfg_.user, ev.code, ev.payload.data(), (int32_t)ev.payload.size());
    }
}

void Engine::PostStateEvent() {
    PushEvent(LUMINA_EV_STATE, StateJson());
}

void Engine::ReplaceFlatSlides(std::vector<Slide> slides, std::vector<std::string> titles) {
    flat_ = std::move(slides);
    flatTitles_ = std::move(titles);
    current_ = -1;
    cleared_ = false;
}

void Engine::Flatten(std::vector<Slide>* out, std::vector<std::string>* titles) {
    out->clear(); if (titles) titles->clear();
    for (const ScenarioItem& it : scenario_.items) {
        std::vector<Slide> itemSlides;
        if (it.kind == "song") {
            BuildOptions o;
            o.titleSlide = it.song.titleSlide;
            o.endBlank = it.song.endBlank;
            o.chorusInterleave = it.song.chorusInterleave;
            o.maxLinesPerSlide = it.song.maxLinesPerSlide;
            o.stripChords = it.song.stripChords;
            o.latinChords = it.song.latinChords;
            o.transpose = it.song.transpose;
            itemSlides = Lyrics::BuildSlides(it.song, o);
        } else if (it.kind == "scripture") {
            std::vector<std::string> verses;
            if (!it.text.empty()) {
                for (const std::string& l : Split(it.text, '\n')) {
                    std::string t = Trim(l);
                    if (!t.empty()) verses.push_back(t);
                }
            } else if (db_ && db_->IsOpen() && !it.ref.empty()) {
                BibleRef::VerseRef r = BibleRef::Resolve(it.ref);
                if (r.Valid()) {
                    int vFrom = r.verse > 0 ? r.verse : 1;
                    const int kMaxVerses = 120;
                    std::string err;
                    std::string outJson;
                    // Consulta por rango con binds:
                    char sql[256];
                    snprintf(sql, sizeof(sql),
                        "SELECT text FROM bible WHERE version=?1 AND book=?2 AND chapter=?3 AND verse>=?4 AND verse<=?5 ORDER BY verse");
                    json q;
                    q["sql"] = sql;
                    json arr = json::array();
                    arr.push_back(it.version);
                    arr.push_back(r.book);
                    arr.push_back(r.chapter);
                    arr.push_back(vFrom);
                    arr.push_back(vFrom + kMaxVerses - 1);
                    q["params"] = arr;
                    if (db_->ExecJson(q.dump(), &outJson, &err)) {
                        try {
                            json rows = json::parse(outJson);
                            if (rows.contains("rows") && rows["rows"].is_array()) {
                                for (const auto& row : rows["rows"]) {
                                    if (row.is_array() && !row.empty() && row[0].is_string())
                                        verses.push_back(row[0].get<std::string>());
                                }
                            }
                        } catch (...) {}
                    }
                }
            }
            std::vector<Slide> sc;
            if (!it.ref.empty() && !verses.empty()) {
                Scripture::BuildSlides(it.ref, verses, 1, &sc);
            } else if (!verses.empty()) {
                // texto libre agrupado
                Slide s; s.kind = SLIDE_SCRIPTURE; s.title = it.title;
                for (const std::string& v : verses) s.lines.push_back(SlideLine(v));
                sc.push_back(s);
            }
            itemSlides = std::move(sc);
        } else if (it.kind == "image") {
            Slide s; s.kind = SLIDE_IMAGE; s.title = it.title; s.imagePath = it.imagePath;
            if (!it.text.empty()) {
                for (const std::string& l : Split(it.text, '\n'))
                    if (!Trim(l).empty()) s.lines.push_back(SlideLine(Trim(l)));
            }
            itemSlides.push_back(s);
        } else if (it.kind == "text") {
            std::vector<std::string> lines;
            for (const std::string& l : Split(it.text, '\n')) {
                std::string t = Trim(l);
                if (!t.empty()) lines.push_back(t);
            }
            for (size_t i = 0; i < lines.size(); i += (size_t)std::max(1, it.maxLinesPerSlide)) {
                Slide s; s.kind = SLIDE_TEXT; s.title = it.title; s.refLabel = it.kind;
                const size_t e = std::min(i + (size_t)std::max(1, it.maxLinesPerSlide), lines.size());
                for (size_t k = i; k < e; ++k) s.lines.push_back(SlideLine(lines[k]));
                itemSlides.push_back(s);
            }
        } else { // blank
            Slide s; s.kind = SLIDE_BLANK; s.title = it.title;
            itemSlides.push_back(s);
        }
        for (const Slide& s : itemSlides) {
            out->push_back(s);
            if (titles) titles->push_back(it.title.empty() ? s.title : it.title);
        }
    }
}

LuminaStatus Engine::LoadScenario(const std::string& jsonText) {
    try {
        json j = json::parse(jsonText);
        Scenario sc;
        if (j.contains("name") && j["name"].is_string()) sc.name = j["name"].get<std::string>();
        if (j.contains("theme") && j["theme"].is_object())
            sc.theme = Theme::FromJson(j["theme"], theme_);

        auto loadSong = [&](const json& so, ScenarioItem* it) {
            SongModel::FromJson(so, &it->song);
            it->kind = "song";
            it->title = it->song.title;
        };

        // Comodines: {"song":{...}} | {"songs":[...]} | {"items":[...]}
        if (j.contains("song") && j["song"].is_object()) {
            ScenarioItem it; loadSong(j["song"], &it); sc.items.push_back(it);
        }
        if (j.contains("songs") && j["songs"].is_array()) {
            for (const auto& so : j["songs"]) {
                ScenarioItem it; loadSong(so, &it); sc.items.push_back(it);
            }
        }
        if (j.contains("items") && j["items"].is_array()) {
            for (const auto& io : j["items"]) {
                ScenarioItem it;
                it.kind = io.value("kind", std::string("blank"));
                it.title = io.value("title", std::string());
                it.ref = io.value("ref", std::string());
                it.version = io.value("version", std::string());
                it.text = io.value("text", std::string());
                it.imagePath = io.value("imagePath", std::string());
                it.maxLinesPerSlide = io.value("maxLinesPerSlide", 4);
                if (io.contains("song") && io["song"].is_object()) loadSong(io["song"], &it);
                else if (it.kind == "song") {
                    // canción inline (campos de canción al nivel del ítem)
                    loadSong(io, &it);
                }
                if (it.kind == "text" && io.contains("lines") && io["lines"].is_array()) {
                    std::string t;
                    for (const auto& l : io["lines"]) { if (t.size()) t += "\n"; t += l.get<std::string>(); }
                    it.text = t;
                }
                sc.items.push_back(it);
            }
        }
        {
            std::lock_guard<std::mutex> lk(mx_);
            scenario_ = std::move(sc);
            theme_ = scenario_.theme;
            std::vector<Slide> flat; std::vector<std::string> titles;
            Flatten(&flat, &titles);
            flat_ = std::move(flat);
            flatTitles_ = std::move(titles);
            current_ = -1;
            black_ = false;
            cleared_ = flat_.empty();
#ifdef LUMINA_HAS_WIN32
            if (projector_) projector_->SetContent(flat_, flatTitles_, theme_, -1, false);
#endif
        }
        PostStateEvent();
        return LUMINA_OK;
    } catch (const json::exception&) {
        return LUMINA_ERR_PARSE;
    } catch (const std::exception&) {
        return LUMINA_ERR_PARSE;
    }
}

LuminaStatus Engine::ShowSlide(int index) {
    {
        std::lock_guard<std::mutex> lk(mx_);
        if (index < -1 || index >= (int)flat_.size()) return LUMINA_ERR_LIMIT;
        current_ = index;
        black_ = false;
        cleared_ = (index < 0);
#ifdef LUMINA_HAS_WIN32
        if (projector_) projector_->SetContent(flat_, flatTitles_, theme_, current_, black_);
#endif
    }
    // Fuera del mutex: PostStateEvent → StateJson() vuelve a tomar mx_
    // (std::mutex NO es recursivo: dentro causaba deadlock — fix v3.0.0).
    PushEvent(LUMINA_EV_SLIDE_CHANGED,
              json{{"index", current_}, {"total", (int)flat_.size()}}.dump());
    if (current_ >= 0) {
        PushEvent(LUMINA_EV_ITEM_CHANGED,
                  json{{"item", current_}, {"title", flatTitles_[(size_t)current_]}}.dump());
    }
    PostStateEvent();
    return LUMINA_OK;
}

LuminaStatus Engine::Next() {
    {
        std::lock_guard<std::mutex> lk(mx_);
        if (flat_.empty()) return LUMINA_ERR_STATE;
        const int n = std::min((int)flat_.size() - 1, current_ + 1);
        if (n == current_) return LUMINA_OK;
        current_ = n; black_ = false; cleared_ = false;
#ifdef LUMINA_HAS_WIN32
        if (projector_) projector_->SetContent(flat_, flatTitles_, theme_, current_, black_);
#endif
    }
    PushEvent(LUMINA_EV_SLIDE_CHANGED,
              json{{"index", current_}, {"total", (int)flat_.size()}}.dump());
    PushEvent(LUMINA_EV_ITEM_CHANGED,
              json{{"item", current_}, {"title", flatTitles_[(size_t)current_]}}.dump());
    PostStateEvent();
    return LUMINA_OK;
}

LuminaStatus Engine::Prev() {
    {
        std::lock_guard<std::mutex> lk(mx_);
        if (flat_.empty()) return LUMINA_ERR_STATE;
        const int n = std::max(-1, current_ - 1);
        if (n == current_) return LUMINA_OK;
        current_ = n;
#ifdef LUMINA_HAS_WIN32
        if (projector_) projector_->SetContent(flat_, flatTitles_, theme_, current_, black_);
#endif
    }
    PushEvent(LUMINA_EV_SLIDE_CHANGED,
              json{{"index", current_}, {"total", (int)flat_.size()}}.dump());
    if (current_ >= 0)
        PushEvent(LUMINA_EV_ITEM_CHANGED,
                  json{{"item", current_}, {"title", flatTitles_[(size_t)current_]}}.dump());
    PostStateEvent();
    return LUMINA_OK;
}

LuminaStatus Engine::Black(bool on) {
    {
        std::lock_guard<std::mutex> lk(mx_);
        black_ = on;
#ifdef LUMINA_HAS_WIN32
        if (projector_) projector_->SetContent(flat_, flatTitles_, theme_, current_, black_);
#endif
    }
    PostStateEvent();
    return LUMINA_OK;
}

LuminaStatus Engine::Clear() {
    {
        std::lock_guard<std::mutex> lk(mx_);
        current_ = -1;
        cleared_ = true;
#ifdef LUMINA_HAS_WIN32
        if (projector_) projector_->SetContent(flat_, flatTitles_, theme_, -1, black_);
#endif
    }
    PostStateEvent();
    return LUMINA_OK;
}

LuminaStatus Engine::SetTheme(const std::string& jsonText) {
    try {
        json j = json::parse(jsonText);
        std::lock_guard<std::mutex> lk(mx_);
        theme_ = Theme::FromJson(j, theme_);
        scenario_.theme = theme_;
#ifdef LUMINA_HAS_WIN32
        if (projector_) projector_->SetContent(flat_, flatTitles_, theme_, current_, black_);
#endif
        PostStateEvent();
        return LUMINA_OK;
    } catch (const json::exception&) {
        return LUMINA_ERR_PARSE;
    }
}

std::string Engine::StateJson() const {
    std::lock_guard<std::mutex> lk(mx_);
    json j;
    j["version"] = kVersion;
    j["scenario"] = scenario_.name;
    j["itemCount"] = (int)scenario_.items.size();
    j["slideCount"] = (int)flat_.size();
    j["current"] = current_;
    j["black"] = black_;
    j["cleared"] = cleared_;
    j["headless"] = cfg_.headless ? 1 : 0;
    j["items"] = json::array();
    for (const ScenarioItem& it : scenario_.items)
        j["items"].push_back(json{{"kind", it.kind}, {"title", it.title}});
    j["theme"] = theme_.ToJson();
    return j.dump();
}

LuminaStatus Engine::Ping(const std::string& msg) {
    PushEvent(LUMINA_EV_PONG, msg);
    return LUMINA_OK;
}

LuminaStatus Engine::ProjectorShow(int screenIndex, bool fullscreen) {
#ifdef LUMINA_HAS_WIN32
    if (!projector_) return LUMINA_ERR_UNSUPPORTED;
    return projector_->Show(screenIndex, fullscreen != 0) ? LUMINA_OK : LUMINA_ERR_IO;
#else
    (void)screenIndex; (void)fullscreen;
    return LUMINA_ERR_UNSUPPORTED;
#endif
}

LuminaStatus Engine::ProjectorHide() {
#ifdef LUMINA_HAS_WIN32
    if (!projector_) return LUMINA_ERR_UNSUPPORTED;
    projector_->Hide();
    return LUMINA_OK;
#else
    return LUMINA_ERR_UNSUPPORTED;
#endif
}

int Engine::RenderPreviewPng(int slideIndex, std::string* pngOut) {
#ifdef LUMINA_HAS_WIN32
    if (slideIndex < 0 || slideIndex >= (int)flat_.size()) return -1;
    std::lock_guard<std::mutex> lk(mx_);
    return Projector::RenderSlidePng(flat_[(size_t)slideIndex], theme_, 640, 360, pngOut) ? 0 : -1;
#else
    (void)slideIndex; (void)pngOut;
    return -1;
#endif
}

LuminaStatus Engine::DbOpen(const std::string& pathUtf8) {
    std::string err;
    if (!db_->Open(pathUtf8, &err)) {
        PushEvent(LUMINA_EV_ERROR, json{{"where","db.open"},{"error",err}}.dump());
        return LUMINA_ERR_IO;
    }
    return LUMINA_OK;
}

LuminaStatus Engine::DbClose() { db_->Close(); return LUMINA_OK; }

LuminaStatus Engine::DbExec(const std::string& sqlJson, std::string* outJson) {
    std::string err;
    if (!db_->ExecJson(sqlJson, outJson, &err)) {
        PushEvent(LUMINA_EV_ERROR, json{{"where","db.exec"},{"error",err}}.dump());
        return LUMINA_ERR_IO;
    }
    return LUMINA_OK;
}

} // namespace lumina
