// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  SongModel.cpp : canción JSON → Song + slides. Esquema propio:
//    {id,title,artist,key|"keyName",bpm,tags,lyrics,
//     blocks:[{label,lines[],repeat}],
//     chorusInterleave,titleSlide,endBlank,stripChords,latinChords,transpose,
//     maxLinesPerSlide}
//  Si hay "blocks", Song::blocks manda y LyricsText() los serializa a texto
//  crudo "[label]\nlineas\n\n" (ver Engine.cpp).
//  Compatibilidad de importación OpenLP (subconjunto tolerante): si trae
//  "lyrics" como string con "[Verso 1]" etc. se usa tal cual; "title" /
//  "alternate_title" → title; "authors" (string o array) → artist;
//  "copyright" y "verse_order_list" se IGNORAN deliberadamente.
//  Solo se propagan nlohmann::json::exception (contrato de la API C: la
//  llamada captura y devuelve LUMINA_ERR_PARSE).
// ============================================================================
#include "SongModel.h"

#include "Lyrics.h"

namespace lumina {
namespace {

// Lecturas tolerantes: tipo distinto al esperado → valor por defecto
// (los "tipos imposibles" graves —p. ej. entrada no objeto— sí lanzan).
std::string GetStr(const json& o, const char* k, const std::string& def = std::string()) {
    const auto it = o.find(k);
    if (it != o.end() && it->is_string()) return it->get<std::string>();
    return def;
}

int GetInt(const json& o, const char* k, int def) {
    const auto it = o.find(k);
    if (it != o.end() && it->is_number_integer()) return it->get<int>();
    return def;
}

bool GetBool(const json& o, const char* k, bool def) {
    const auto it = o.find(k);
    if (it != o.end() && it->is_boolean()) return it->get<bool>();
    return def;
}

// Slide → JSON (misma forma que el estado del motor): líneas simples como
// "texto" y líneas con cifrado como {"text","chords"}.
json SlideToJson(const Slide& s) {
    json j;
    j["kind"]     = s.kind;
    j["title"]    = s.title;
    j["refLabel"] = s.refLabel;
    j["lines"]    = json::array();
    for (const SlideLine& l : s.lines) {
        if (l.chords.empty())
            j["lines"].push_back(l.text);
        else
            j["lines"].push_back(json{{"text", l.text}, {"chords", l.chords}});
    }
    if (!s.imagePath.empty())
        j["imagePath"] = s.imagePath;
    // v6.0.0: resaltado en proyección (evolución ADITIVA del contrato de
    // slide: solo se emite cuando hay valor — consumidores viejos lo ignoran).
    if (!s.highlight.empty())
        j["highlight"] = s.highlight;
    return j;
}

} // namespace

/* ------------------------------------------------------------- FromJson -- */

void SongModel::FromJson(const json& o, Song* song) {
    if (!song) return;
    if (!o.is_object())
        throw json::type_error::create(302, std::string("la canción debe ser un objeto JSON"), &o);

    song->id      = GetStr(o, "id");
    // OpenLP: "alternate_title" completa el título si falta "title".
    song->title   = GetStr(o, "title", GetStr(o, "alternate_title"));
    song->tags    = GetStr(o, "tags");
    song->lyrics  = GetStr(o, "lyrics");       // se usa tal cual si viene
    song->keyName = GetStr(o, "key", GetStr(o, "keyName"));
    song->bpm     = GetInt(o, "bpm", 0);
    song->artist  = GetStr(o, "artist");
    if (song->artist.empty()) {
        // OpenLP: "authors" como string único o array de strings.
        const auto it = o.find("authors");
        if (it != o.end() && it->is_string()) {
            song->artist = it->get<std::string>();
        } else if (it != o.end() && it->is_array()) {
            std::string joined;
            for (const auto& a : *it) {
                if (!a.is_string()) continue;
                if (!joined.empty()) joined += ", ";
                joined += a.get<std::string>();
            }
            song->artist = joined;
        }
    }
    // "copyright" y "verse_order_list": ignorados (compatibilidad importación).

    song->latinChords      = GetBool(o, "latinChords", true);
    song->chorusInterleave = GetBool(o, "chorusInterleave", false);
    song->titleSlide       = GetBool(o, "titleSlide", true);
    song->endBlank         = GetBool(o, "endBlank", false);
    song->stripChords      = GetBool(o, "stripChords", true);
    song->maxLinesPerSlide = GetInt(o, "maxLinesPerSlide", 4);
    song->transpose        = GetInt(o, "transpose", 0);

    const auto itb = o.find("blocks");
    if (itb != o.end() && itb->is_array()) {
        for (const auto& bo : *itb) {
            if (!bo.is_object()) continue;
            SongBlock b;
            b.label  = GetStr(bo, "label");
            const auto itl = bo.find("lines");
            if (itl != bo.end() && itl->is_array()) {
                for (const auto& l : *itl) {
                    if (l.is_string())          b.lines.push_back(l.get<std::string>());
                    else if (l.is_number())     b.lines.push_back(l.dump());  // tolerante
                }
            }
            b.repeat = GetInt(bo, "repeat", 1);
            song->blocks.push_back(std::move(b));
        }
    }
}

/* --------------------------------------------------------------- ToJson -- */

json SongModel::ToJson(const Song& s) {
    json j;
    j["id"]     = s.id;
    j["title"]  = s.title;
    j["artist"] = s.artist;
    j["key"]    = s.keyName;
    j["bpm"]    = s.bpm;
    j["tags"]   = s.tags;
    // blocks manda sobre el lyrics crudo (igual que Song::LyricsText)
    j["lyrics"] = s.blocks.empty() ? s.lyrics : s.LyricsText();
    j["blocks"] = json::array();
    for (const SongBlock& b : s.blocks) {
        j["blocks"].push_back(json{{"label", b.label}, {"lines", b.lines}, {"repeat", b.repeat}});
    }
    j["chorusInterleave"] = s.chorusInterleave;
    j["titleSlide"]       = s.titleSlide;
    j["endBlank"]         = s.endBlank;
    j["stripChords"]      = s.stripChords;
    j["latinChords"]      = s.latinChords;
    j["transpose"]        = s.transpose;
    j["maxLinesPerSlide"] = s.maxLinesPerSlide;
    return j;
}

/* -------------------------------------------------- ParseAndBuildSlides -- */

json SongModel::ParseAndBuildSlides(const json& o) {
    Song song;
    FromJson(o, &song);

    // BuildOptions desde los campos del propio JSON (defaults = Song).
    BuildOptions opt;
    opt.titleSlide       = song.titleSlide;
    opt.endBlank         = song.endBlank;
    opt.chorusInterleave = song.chorusInterleave;
    opt.maxLinesPerSlide = song.maxLinesPerSlide;
    opt.stripChords      = song.stripChords;
    opt.latinChords      = song.latinChords;
    opt.transpose        = song.transpose;

    const std::vector<Slide> slides = Lyrics::BuildSlides(song, opt);

    json res;
    res["ok"]     = 1;
    res["song"]   = ToJson(song);
    res["slides"] = json::array();
    for (const Slide& s : slides)
        res["slides"].push_back(SlideToJson(s));
    return res;
}

} // namespace lumina
