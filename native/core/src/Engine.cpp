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
// v7.0.0 «ULTRA»: IPC ipc.v1 (F0.05) + log estructurado (F0.09) +
// bootstrap de entorno (F0.01-F0.03). Implementaciones en lumina_api.cpp.
#include "IpcV1.h"
#include "NativeLog.h"
#include "Bootstrap.h"

#ifdef LUMINA_HAS_WIN32
#include "Projector.h"
#endif

#include <nlohmann/json.hpp>

namespace lumina {

static const char* kVersion = "7.1.0-operador";

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
    // v7.0.0 «ULTRA» (F1.01): estilo de línea activa por tema.
    j["dimInactive"]=dimInactive; j["activeLineColor"]=activeLineColor;
    j["activeLineBold"]=activeLineBold;
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
    // v7.0.0 «ULTRA» (F1.01): estilo de línea activa por tema.
    if (o.contains("dimInactive")   && o["dimInactive"].is_boolean())   t.dimInactive   = o["dimInactive"];
    if (o.contains("activeLineBold")&& o["activeLineBold"].is_boolean()) t.activeLineBold = o["activeLineBold"];
    if (o.contains("activeLineColor") && o["activeLineColor"].is_string())
        t.activeLineColor = o["activeLineColor"];
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

// v7.0.0 «ULTRA» (F1.03): helpers de sincronización por línea (definidos al
// final del archivo; declarados aquí porque Next/Prev/ShowSlide los usan).
static bool SlideHasSync(const Slide& s);
static int  FirstActiveLine(const Slide& s);
static int  NextSyncGroup(const Slide& s, int from);
static int  PrevSyncGroup(const Slide& s, int from);

Engine::Engine(const LuminaConfig& cfg) : cfg_(cfg) {
    if (cfg_.structSize != (int32_t)sizeof(LuminaConfig)) {
        // ABI distinto: aceptar pero registrar
        PushEvent(LUMINA_EV_LOG, "{\"level\":\"warn\",\"msg\":\"structSize distinto\"}");
    }
#ifdef LUMINA_HAS_WIN32
    if (!cfg_.headless) {
        projector_.reset(new Projector());
        // v6.0.0: transición por defecto (fundido 220 ms) — configurable vía
        // lumina_set_transition antes/después de crear la ventana.
        projector_->SetTransition(transitionMode_, transitionMs_);
        // v7.0.0 «ULTRA» (F0.06.8/F3.02.4): los reportes del proyector
        // (fallos HWND/DC/bitmap/video) van al log nativo §10.1 Y al canal
        // de eventos (aviso en el monitor del operador — nunca mudos).
        Projector::SetReportSink(&Engine::ProjectorReport, this);
    }
#endif
    db_.reset(new Database());
    started_ = true;
    eventThread_ = std::thread(&Engine::EventLoop, this);
}

Engine::~Engine() {
    stop_ = true;
    evCv_.notify_all();
    if (eventThread_.joinable()) eventThread_.join();
    // v7.0.0: el IPC se detiene ANTES del projector (el drain puede estar
    // sirviendo RESULTs); el log se cierra al final (bitácora del apagado).
    if (ipcServer_) { ipcServer_->Stop(); ipcServer_.reset(); }
#ifdef LUMINA_HAS_WIN32
    if (projector_) {
        Projector::SetReportSink(nullptr, nullptr);
        projector_->Close(); projector_.reset();
    }
#endif
    if (log_) { log_->Close(); log_.reset(); }
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

void Engine::ReplaceFlatSlides(std::vector<Slide> slides, std::vector<std::string> titles,
                                std::vector<int> itemIdx) {
    flat_ = std::move(slides);
    flatTitles_ = std::move(titles);
    flatItems_ = std::move(itemIdx);
    current_ = -1;
    cleared_ = false;
}

// itemIdx: para cada slide aplanada, el índice del ScenarioItem que la originó.
// Los eventos SLIDE_CHANGED/ITEM_CHANGED informan ese índice (v5.1.0: antes
// se informaba el índice de SLIDE, lo que desincronizaba video/escenario/
// activadores en cultos con más de un ítem — bug #1 del ciclo FUNDAMENTO).
void Engine::Flatten(std::vector<Slide>* out, std::vector<std::string>* titles,
                      std::vector<int>* itemIdx) {
    out->clear(); if (titles) titles->clear(); if (itemIdx) itemIdx->clear();
    int itemNumber = 0;
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
                // v5.2.0: el motor HONRA versesPerSlide del ítem (antes fijo 1)
                Scripture::BuildSlides(it.ref, verses,
                                       it.versesPerSlide > 0 ? it.versesPerSlide : 1, &sc);
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
            if (!it.structuredLines.empty()) {
                // v7.0.0 (F1.03/F2.03): las líneas estructuradas (ahp.v1)
                // NO se re-agrupan: cada slide conserva sus líneas y sus
                // syncMark (la sincronización por línea es del elemento).
                Slide s; s.kind = SLIDE_TEXT; s.title = it.title;
                s.refLabel = it.kind;
                s.lines = it.structuredLines;
                itemSlides.push_back(s);
            } else {
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
            }
        } else if (it.kind == "video") {
            // v7.0.0 (F3.01): video DirectShow desde el núcleo. El proyector
            // reproduce el archivo sobre el mismo HWND (VMR9 windowless);
            // la carga es DIFERIDA: solo la slide activa construye el grafo
            // (F3.07/F6.03: no se cargan todos los videos del proyecto).
            Slide s; s.kind = SLIDE_VIDEO; s.title = it.title;
            s.videoPath  = it.videoPath;
            s.videoVolume= it.videoVolume;
            s.videoStartAtMs = it.videoStartAtMs;
            s.videoLoop  = it.videoLoop;
            itemSlides.push_back(s);
        } else if (it.kind == "composed" && !it.composed.empty()) {
            // v7.1.0 «OPERADOR» (feedback #1/#8): slide COMPUESTA del editor
            // de lienzo libre — 1 ítem = 1 slide con TODOS sus elementos
            // posicionables (el Renderer los dibuja en sus rects: WYSIWYG).
            Slide s; s.kind = SLIDE_COMPOSED; s.title = it.title;
            s.refLabel = "compuesta";
            s.elements = it.composed;
            itemSlides.push_back(s);
        } else if (it.kind == "pptx") {
            // v7.1.0 «OPERADOR» (feedback #6): PPTX ORIGINAL sin extracción.
            // Slide marcador: título + nombre del archivo; la proyección real
            // la entrega PowerPoint vía COM desde la UI al ponerla en vivo
            // (la ventana nativa queda debajo y reaparece al salir).
            Slide s; s.kind = SLIDE_TITLE; s.title = it.title;
            s.refLabel = "presentación";
            const std::string base = it.pptxPath.empty()
                ? it.title
                // nombre del archivo sin directorio (portable: '/' y '\\')
                : it.pptxPath.substr(it.pptxPath.find_last_of("/\\") + 1);
            s.lines.push_back(SlideLine(base));
            itemSlides.push_back(s);
        } else { // blank
            Slide s; s.kind = SLIDE_BLANK; s.title = it.title;
            itemSlides.push_back(s);
        }
        for (Slide& s : itemSlides) {
            // v6.0.0: propaga el resaltado del ítem a sus slides (text/
            // scripture/image — el Renderer pinta los matches en acento).
            if (!it.highlight.empty()) s.highlight = it.highlight;
            out->push_back(s);
            if (titles) titles->push_back(it.title.empty() ? s.title : it.title);
            if (itemIdx) itemIdx->push_back(itemNumber);
        }
        ++itemNumber;
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
                it.versesPerSlide = io.value("versesPerSlide", 1);   // v5.2.0
                // v6.0.0: resaltado en proyección (palabra/frase; opcional).
                it.highlight = io.value("highlight", std::string());
                if (io.contains("song") && io["song"].is_object()) loadSong(io["song"], &it);
                else if (it.kind == "song") {
                    // canción inline (campos de canción al nivel del ítem)
                    loadSong(io, &it);
                }
                if (it.kind == "text" && io.contains("lines") && io["lines"].is_array()) {
                    // v7.0.0: 'lines' admite strings (vía clásica) u objetos
                    // {text,syncMark} (ahp.v1 — F2.03). Los objetos van a
                    // structuredLines; aquí solo se aplanan los strings.
                    bool allStrings = true;
                    for (const auto& l : io["lines"])
                        if (!l.is_string()) { allStrings = false; break; }
                    if (allStrings) {
                        std::string t;
                        for (const auto& l : io["lines"]) { if (t.size()) t += "\n"; t += l.get<std::string>(); }
                        it.text = t;
                    }
                }
                // v7.0.0 (F2.03): persistencia por línea con syncMark —
                // "lines":[{"text":"...","syncMark":N},...]. Se guardan como
                // líneas estructuradas del ítem (el motor las respeta al
                // aplanar; 'text' plano sigue siendo la vía compatible).
                if (io.contains("lines") && io["lines"].is_array() &&
                    !io["lines"].empty() && io["lines"][0].is_object()) {
                    it.structuredLines.clear();
                    for (const auto& l : io["lines"]) {
                        SlideLine sl;
                        sl.text = l.value("text", std::string());
                        if (l.contains("syncMark") && l["syncMark"].is_number())
                            sl.syncMark = l["syncMark"].get<int>();
                        it.structuredLines.push_back(sl);
                    }
                }
                // v7.0.0 (F3.01): elemento video.
                if (it.kind == "video") {
                    it.videoPath     = io.value("videoPath", std::string());
                    it.videoVolume   = io.value("videoVolume", 100);
                    it.videoStartAtMs= (int64_t)io.value("videoStartAtMs", (int64_t)0);
                    it.videoLoop     = io.value("videoLoop", false);
                }
                // v7.1.0 «OPERADOR» (feedback #1/#8): slide COMPUESTA — el
                // ítem trae "elements":[{kind,x,y,w,h,opacity,…}] del editor
                // de lienzo libre (se aplanan 1:1, sin re-agrupar).
                if (it.kind == "composed" && io.contains("elements") &&
                    io["elements"].is_array()) {
                    for (const auto& eo : io["elements"])
                        if (eo.is_object())
                            it.composed.push_back(ComposedElement::FromJson(eo));
                }
                // v7.1.0 «OPERADOR» (feedback #6): PPTX original (sin
                // extracción): solo la ruta del archivo.
                if (it.kind == "pptx") {
                    it.pptxPath = io.value("pptxPath", std::string());
                }
                sc.items.push_back(it);
            }
        }
        {
            std::lock_guard<std::mutex> lk(mx_);
            scenario_ = std::move(sc);
            theme_ = scenario_.theme;
            std::vector<Slide> flat; std::vector<std::string> titles; std::vector<int> items;
            Flatten(&flat, &titles, &items);
            flat_ = std::move(flat);
            flatTitles_ = std::move(titles);
            flatItems_ = std::move(items);
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
        // v7.0.0 (F1.03): al posar una slide, la línea activa es la primera
        // de su primer grupo de sincronización (o -1 si no navega por línea).
        activeLine_ = (index >= 0 && index < (int)flat_.size())
                      ? FirstActiveLine(flat_[(size_t)index]) : -1;
#ifdef LUMINA_HAS_WIN32
        if (projector_) {
            projector_->SetContent(flat_, flatTitles_, theme_, current_, black_);
            projector_->SetActiveLine(activeLine_);
        }
#endif
    }
    // Fuera del mutex: PostStateEvent → StateJson() vuelve a tomar mx_
    // (std::mutex NO es recursivo: dentro causaba deadlock — fix v3.0.0).
    // "item" = índice del ScenarioItem que originó la slide (v5.1.0).
    int itemNow = -1;
    { std::lock_guard<std::mutex> lk(mx_);
      if (current_ >= 0 && current_ < (int)flatItems_.size()) itemNow = flatItems_[(size_t)current_]; }
    PushEvent(LUMINA_EV_SLIDE_CHANGED,
              json{{"index", current_}, {"item", itemNow},
                   {"total", (int)flat_.size()}}.dump());
    if (current_ >= 0) {
        PushEvent(LUMINA_EV_ITEM_CHANGED,
                  json{{"item", itemNow}, {"title", flatTitles_[(size_t)current_]}}.dump());
    }
    PostStateEvent();
    return LUMINA_OK;
}

LuminaStatus Engine::Next() {
    // v7.0.0 (F1.03): «avanzar» es consciente de línea — si la slide activa
    // navega por syncMark y quedan grupos, avanza la LÍNEA (fondo y video
    // intactos); solo al agotar los grupos pasa a la siguiente slide.
    // NOTA (lección v3.0.0): PostStateEvent → StateJson() vuelve a tomar
    // mx_ — JAMÁS dentro del lock (deadlock). Se difiere al final.
    bool lineAdvanced = false;
    {
        std::lock_guard<std::mutex> lk(mx_);
        if (flat_.empty()) return LUMINA_ERR_STATE;
        if (current_ >= 0 && current_ < (int)flat_.size()) {
            const Slide& sl = flat_[(size_t)current_];
            if (activeLine_ >= 0) {
                const int nxt = NextSyncGroup(sl, activeLine_);
                if (nxt >= 0) {
                    activeLine_ = nxt;
#ifdef LUMINA_HAS_WIN32
                    if (projector_) projector_->SetActiveLine(activeLine_);
#endif
                    lineAdvanced = true;
                }
            }
        }
        if (!lineAdvanced) {
            const int n = std::min((int)flat_.size() - 1, current_ + 1);
            if (n == current_) return LUMINA_OK;
            current_ = n; black_ = false; cleared_ = false;
            activeLine_ = FirstActiveLine(flat_[(size_t)current_]);
#ifdef LUMINA_HAS_WIN32
            if (projector_) {
                projector_->SetContent(flat_, flatTitles_, theme_, current_, black_);
                projector_->SetActiveLine(activeLine_);
            }
#endif
        }
    }
    if (lineAdvanced) { PostStateEvent(); return LUMINA_OK; }
    int itemNow = -1;
    { std::lock_guard<std::mutex> lk(mx_);
      if (current_ >= 0 && current_ < (int)flatItems_.size()) itemNow = flatItems_[(size_t)current_]; }
    PushEvent(LUMINA_EV_SLIDE_CHANGED,
              json{{"index", current_}, {"item", itemNow},
                   {"total", (int)flat_.size()}}.dump());
    PushEvent(LUMINA_EV_ITEM_CHANGED,
              json{{"item", itemNow}, {"title", flatTitles_[(size_t)current_]}}.dump());
    PostStateEvent();
    return LUMINA_OK;
}

LuminaStatus Engine::Prev() {
    // v7.0.0 (F1.03): «retroceder» vuelve al grupo de sincronización previo
    // antes de saltar a la slide anterior (PostStateEvent SIEMPRE fuera del
    // lock — deadlock v3.0.0).
    bool lineRetreated = false;
    {
        std::lock_guard<std::mutex> lk(mx_);
        if (flat_.empty()) return LUMINA_ERR_STATE;
        if (current_ >= 0 && current_ < (int)flat_.size() && activeLine_ > 0) {
            const Slide& sl = flat_[(size_t)current_];
            const int prv = PrevSyncGroup(sl, activeLine_);
            if (prv >= 0) {
                activeLine_ = prv;
#ifdef LUMINA_HAS_WIN32
                if (projector_) projector_->SetActiveLine(activeLine_);
#endif
                lineRetreated = true;
            }
        }
        if (!lineRetreated) {
            const int n = std::max(-1, current_ - 1);
            if (n == current_) return LUMINA_OK;
            current_ = n;
            activeLine_ = (current_ >= 0 && current_ < (int)flat_.size())
                          ? FirstActiveLine(flat_[(size_t)current_]) : -1;
#ifdef LUMINA_HAS_WIN32
            if (projector_) {
                projector_->SetContent(flat_, flatTitles_, theme_, current_, black_);
                projector_->SetActiveLine(activeLine_);
            }
#endif
        }
    }
    if (lineRetreated) { PostStateEvent(); return LUMINA_OK; }
    int itemNow = -1;
    { std::lock_guard<std::mutex> lk(mx_);
      if (current_ >= 0 && current_ < (int)flatItems_.size()) itemNow = flatItems_[(size_t)current_]; }
    PushEvent(LUMINA_EV_SLIDE_CHANGED,
              json{{"index", current_}, {"item", itemNow},
                   {"total", (int)flat_.size()}}.dump());
    if (current_ >= 0)
        PushEvent(LUMINA_EV_ITEM_CHANGED,
                  json{{"item", itemNow}, {"title", flatTitles_[(size_t)current_]}}.dump());
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
    // v6.0.0: transición de proyección (evolución aditiva del estado).
    j["transition"] = json{{"mode", transitionMode_}, {"durationMs", transitionMs_}};
    // v7.0.0 (F1.03): índice de línea activa (-1 = slide entera).
    j["line"] = activeLine_;
    // v7.0.0: telemetría visible para Diagnóstico (F5.10/F6.01).
    j["ipc"]  = ipcServer_ ? json::parse(ipcServer_->StatsJson()) : json{{"running", 0}};
    j["log"]  = log_ ? json::parse(LogStatsJson()) : json{{"open", 0}};
#ifdef LUMINA_HAS_WIN32
    if (projector_) j["render"] = json::parse(projector_->StatsJson());
#endif
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

LuminaStatus Engine::SetTransition(int32_t mode, int32_t durationMs) {
    if (mode != 0 && mode != 1) return LUMINA_ERR_LIMIT;
    if (durationMs < 0 || durationMs > 5000) return LUMINA_ERR_LIMIT;
    {
        std::lock_guard<std::mutex> lk(mx_);
        transitionMode_ = mode;
        transitionMs_   = durationMs;
#ifdef LUMINA_HAS_WIN32
        if (projector_) projector_->SetTransition(mode, durationMs);
#endif
    }
    PostStateEvent();
    return LUMINA_OK;
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


#ifdef LUMINA_HAS_WIN32
// v7.0.0 (F0.06.8/F3.02.4): puente proyector → log §10.1 + aviso operador.
void Engine::ProjectorReport(void* user, int severity, const char* module,
                             const char* msg) {
    Engine* e = reinterpret_cast<Engine*>(user);
    if (!e || !msg) return;
    const nlog::Severity sev = severity >= 3 ? nlog::SEV_ERROR : nlog::SEV_WARN;
    if (e->log_) e->log_->Write(sev, module ? module : "render", msg);
    e->PushEvent(LUMINA_EV_ERROR,
                 json{{"where", module ? module : "render"}, {"error", msg}}.dump());
}
#endif

/* ------------------------------------------------ v7.0.0: líneas (F1.03) -- */

// ¿La slide usa navegación por syncMark? Solo si alguna línea lleva marca:
// los flujos clásicos (canciones/textos sin marcas) conservan su conducta.
static bool SlideHasSync(const Slide& s) {
    for (const SlideLine& l : s.lines)
        if (l.syncMark > 0) return true;
    return false;
}

// Índice de la línea activa al POSAR la slide: primera línea con marca
// (inicio del primer grupo) o -1 si la slide no navega por línea.
static int FirstActiveLine(const Slide& s) {
    if (!SlideHasSync(s)) return -1;
    for (size_t i = 0; i < s.lines.size(); ++i)
        if (s.lines[i].syncMark > 0) return (int)i;
    return -1;
}

// Siguiente grupo de sincronización desde 'from': primera línea posterior
// cuyo syncMark difiere del de 'from' (y > 0). -1 = no hay más grupos.
static int NextSyncGroup(const Slide& s, int from) {
    if (from < 0 || from >= (int)s.lines.size()) return -1;
    const int cur = s.lines[(size_t)from].syncMark;
    for (int i = from + 1; i < (int)s.lines.size(); ++i) {
        if (s.lines[(size_t)i].syncMark > 0 && s.lines[(size_t)i].syncMark != cur)
            return i;
    }
    return -1;
}

// Grupo anterior de sincronización (primera línea del grupo previo).
static int PrevSyncGroup(const Slide& s, int from) {
    if (from <= 0 || from >= (int)s.lines.size()) return -1;
    int last = -1;
    for (int i = 0; i < from; ++i) {
        if (s.lines[(size_t)i].syncMark > 0) {
            if (last < 0) last = i;                       // primera marca
            else if (s.lines[(size_t)i].syncMark != s.lines[(size_t)last].syncMark)
                last = i;                                  // línea de grupo nuevo
        }
    }
    return last;
}

// Publica el cambio de línea activa a la proyección (el fondo, el video y
// la ventana PERMANECEN: F1.03.6 «mantener el fondo y el video intactos»).
LuminaStatus Engine::LineSet(int32_t lineIndex) {
    bool changed = false;
    {
        std::lock_guard<std::mutex> lk(mx_);
        if (current_ < 0 || current_ >= (int)flat_.size()) return LUMINA_ERR_STATE;
        const Slide& sl = flat_[(size_t)current_];
        if (lineIndex < -1 || lineIndex >= (int)sl.lines.size())
            return LUMINA_ERR_LIMIT;
        if (lineIndex == activeLine_) return LUMINA_OK;
        activeLine_ = lineIndex;
        changed = true;
#ifdef LUMINA_HAS_WIN32
        if (projector_) projector_->SetActiveLine(activeLine_);
#endif
    }
    if (changed) PostStateEvent();    // fuera del lock (lección v3.0.0)
    return LUMINA_OK;
}

LuminaStatus Engine::LineNext() {
    int next = -1;
    const Slide* sl = nullptr;
    {
        std::lock_guard<std::mutex> lk(mx_);
        if (current_ < 0 || current_ >= (int)flat_.size()) return LUMINA_ERR_STATE;
        sl = &flat_[(size_t)current_];
        // con marcas: siguiente GRUPO; sin marcas: línea a línea.
        if (activeLine_ < 0)
            next = sl->lines.empty() ? -1 : 0;
        else if (SlideHasSync(*sl))
            next = NextSyncGroup(*sl, activeLine_);
        else
            next = (activeLine_ + 1 < (int)sl->lines.size()) ? activeLine_ + 1 : -1;
        if (next < 0) return LUMINA_ERR_LIMIT;
        activeLine_ = next;
#ifdef LUMINA_HAS_WIN32
        if (projector_) projector_->SetActiveLine(activeLine_);
#endif
    }
    PostStateEvent();                 // fuera del lock (lección v3.0.0)
    return LUMINA_OK;
}

LuminaStatus Engine::LinePrev() {
    int prev = -1;
    const Slide* slp = nullptr;
    {
        std::lock_guard<std::mutex> lk(mx_);
        if (current_ < 0 || current_ >= (int)flat_.size()) return LUMINA_ERR_STATE;
        slp = &flat_[(size_t)current_];
        prev = activeLine_ <= 0 ? -1
              : (SlideHasSync(*slp) ? PrevSyncGroup(*slp, activeLine_)
                                    : activeLine_ - 1);
        if (prev < 0) return LUMINA_ERR_LIMIT;
        activeLine_ = prev;
#ifdef LUMINA_HAS_WIN32
        if (projector_) projector_->SetActiveLine(activeLine_);
#endif
    }
    PostStateEvent();                 // fuera del lock (lección v3.0.0)
    return LUMINA_OK;
}

/* ---------------------------------------------- v7.0.0: LowerThird (F3.04) */

LuminaStatus Engine::LowerThird(const std::string& json) {
#ifdef LUMINA_HAS_WIN32
    {
        std::lock_guard<std::mutex> lk(mx_);
        if (!projector_) return LUMINA_ERR_UNSUPPORTED;
        projector_->SetLowerThird(json);
    }
    PostStateEvent();
    return LUMINA_OK;
#else
    (void)json;
    return LUMINA_ERR_UNSUPPORTED;
#endif
}

/* ------------------------------------------------- v7.0.0: IPC (F0.05) -- */

LuminaStatus Engine::IpcStart(const std::string& optsJson) {
    ipc::Server::Options o;
    try {
        if (!optsJson.empty()) {
            json j = json::parse(optsJson);
            if (j.contains("pipe") && j["pipe"].is_string())
                o.pipeName = j["pipe"].get<std::string>();
            if (j.contains("maxPayload") && j["maxPayload"].is_number())
                o.maxPayload = (uint32_t)j["maxPayload"].get<int64_t>();
            if (j.contains("maxQueued") && j["maxQueued"].is_number())
                o.queue.maxQueued = (size_t)j["maxQueued"].get<int64_t>();
            if (j.contains("maxAgeMs") && j["maxAgeMs"].is_number())
                o.queue.maxAgeMs = (int)j["maxAgeMs"].get<int64_t>();
        }
    } catch (const json::exception&) {
        return LUMINA_ERR_PARSE;
    }
    if (ipcServer_) ipcServer_->Stop();
    ipcServer_.reset(new ipc::Server());
    // El handler ejecuta los mismos comandos que la API C (una sola política
    // de verdad); desde el hilo de drenaje del servidor, NUNCA del lector.
    ipc::CommandHandler handler = [this](const std::string& action,
                                         const std::string& payload,
                                         std::string* outJson) -> int32_t {
        if (action == "next")        return Next();
        if (action == "prev")        return Prev();
        if (action == "black") {     json j = payload.empty() ? json::object()
                                     : json::parse(payload);
                                     return Black(j.value("on", true)); }
        if (action == "clear")       return Clear();
        if (action == "lowerThird") { return LowerThird(payload); }
        if (action == "lineNext")    return LineNext();
        if (action == "linePrev")    return LinePrev();
        if (action == "lineSet") {   json j = payload.empty() ? json::object()
                                     : json::parse(payload);
                                     return LineSet(j.value("line", -1)); }
        if (action == "showSlide") { json j = payload.empty() ? json::object()
                                     : json::parse(payload);
                                     return ShowSlide(j.value("index", 0)); }
        if (action == "loadScenario") { return LoadScenario(payload); }
        if (action == "setTheme")   { return SetTheme(payload); }
        if (action == "ping")       { return Ping(payload); }
        if (action == "state")      { *outJson = StateJson(); return LUMINA_OK; }
        return LUMINA_ERR_UNSUPPORTED;
    };
    ipc::StateProvider state = [this]() { return StateJson(); };
    if (!ipcServer_->Start(o, handler, state)) {
        // Linux/arnés: transporte no disponible — el NÚCLEO SIGUE EN PIE
        // (F0.05.7). Se registra y se expone en stats para Diagnóstico.
        if (log_) log_->Write(nlog::SEV_WARN, "ipc",
                              "transporte ipc.v1 no disponible (no-Windows)");
        return LUMINA_ERR_UNSUPPORTED;
    }
    if (log_)
        log_->Write(nlog::SEV_INFO, "ipc",
                    "servidor ipc.v1 escuchando en pipe '" + o.pipeName + "'");
    return LUMINA_OK;
}

LuminaStatus Engine::IpcStop() {
    if (!ipcServer_) return LUMINA_ERR_STATE;
    ipcServer_->Stop();
    return LUMINA_OK;
}

std::string Engine::IpcStatsJson() const {
    return ipcServer_ ? ipcServer_->StatsJson() : std::string("{\"running\":0}");
}

/* ------------------------------------------------- v7.0.0: log (F0.09) -- */

LuminaStatus Engine::LogOpen(const std::string& optsJson) {
    nlog::Log::Options o;
    try {
        if (!optsJson.empty()) {
            json j = json::parse(optsJson);
            if (j.contains("dir") && j["dir"].is_string())
                o.dir = j["dir"].get<std::string>();
            if (j.contains("retentionDays") && j["retentionDays"].is_number())
                o.retentionDays = (int)j["retentionDays"].get<int64_t>();
            if (j.contains("level") && j["level"].is_string()) {
                std::string lv = j["level"].get<std::string>();
                o.minLevel = lv == "DEBUG" ? nlog::SEV_DEBUG :
                             lv == "WARN"  ? nlog::SEV_WARN  :
                             lv == "ERROR" ? nlog::SEV_ERROR : nlog::SEV_INFO;
            }
        }
    } catch (const json::exception&) {
        return LUMINA_ERR_PARSE;
    }
    if (o.dir.empty()) return LUMINA_ERR_ARG;
    if (!log_) log_.reset(new nlog::Log());
    if (!log_->Open(o)) return LUMINA_ERR_IO;
    log_->Write(nlog::SEV_INFO, "log", "log nativo abierto (formato §10.1)");
    return LUMINA_OK;
}

LuminaStatus Engine::LogWrite(const std::string& entryJson) {
    if (!log_) return LUMINA_ERR_STATE;
    try {
        json j = entryJson.empty() ? json::object() : json::parse(entryJson);
        nlog::Entry e;
        std::string sev = j.value("severity", std::string("INFO"));
        e.severity = sev == "DEBUG" ? nlog::SEV_DEBUG :
                     sev == "WARN"  ? nlog::SEV_WARN  :
                     sev == "ERROR" ? nlog::SEV_ERROR : nlog::SEV_INFO;
        e.module   = j.value("module", std::string("core"));
        e.message  = j.value("message", std::string());
        e.scenarioId = j.value("scenarioId", std::string());
        e.elementId  = j.value("elementId", std::string());
        e.lineIndex  = j.value("lineIndex", -1);
        e.filePath   = j.value("filePath", std::string());
        e.exception  = j.value("exception", std::string());
        e.callStack  = j.value("callStack", std::string());
        return log_->Write(e) ? LUMINA_OK : LUMINA_ERR_IO;
    } catch (const json::exception&) {
        return LUMINA_ERR_PARSE;
    }
}

std::string Engine::LogStatsJson() const {
    if (!log_) return std::string("{\"open\":0}");
    const nlog::Log::Stats st = log_->StatsSnapshot();
    json j;
    j["open"] = 1;
    j["file"] = log_->ActiveFile();
    j["written"] = (double)st.written;
    j["failed"] = (double)st.failed;
    j["dropped"] = (double)st.dropped;
    j["bytes"] = (double)st.bytes;
    return j.dump();
}

/* --------------------------------------------- v7.0.0: entorno (F0.01-03) */

std::string Engine::DetectEnvJson(const std::string& probeJson,
                                   bool forceX86, bool forceX64,
                                   bool forceA, bool forceB, bool forceC) {
    return bootstrap::EnvDecisionToJson(bootstrap::DetectEnvironment(
        probeJson, forceX86, forceX64, forceA, forceB, forceC));
}

} // namespace lumina
