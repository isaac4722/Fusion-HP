// Contrato: canción JSON (esquema propio + subconjunto OpenLP) → Song + slides.
#ifndef FUSION_SONGMODEL_H
#define FUSION_SONGMODEL_H

#include "Models.h"
#include <nlohmann/json.hpp>

namespace fusion {
class SongModel {
public:
    // Rellena song desde JSON; lanza nlohmann::json::exception ante tipos
    // imposibles (la llamada de API captura y devuelve FUSION_ERR_PARSE).
    static void FromJson(const json& o, Song* song);
    static json  ToJson(const Song& s);

    // Resultado fusion_song_parse:
    // {"ok":1,"song":{...},"slides":[...]} — construye slides con BuildOptions
    // tomadas del propio JSON (titleSlide/endBlank/chorusInterleave/…).
    static json ParseAndBuildSlides(const json& o);
};
} // namespace fusion
#endif
