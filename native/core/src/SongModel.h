// Contrato: canción JSON (esquema propio + subconjunto OpenLP) → Song + slides.
#ifndef LUMINA_SONGMODEL_H
#define LUMINA_SONGMODEL_H

#include "Models.h"
#include <nlohmann/json.hpp>

namespace lumina {
class SongModel {
public:
    // Rellena song desde JSON; lanza nlohmann::json::exception ante tipos
    // imposibles (la llamada de API captura y devuelve LUMINA_ERR_PARSE).
    static void FromJson(const json& o, Song* song);
    static json  ToJson(const Song& s);

    // Resultado lumina_song_parse:
    // {"ok":1,"song":{...},"slides":[...]} — construye slides con BuildOptions
    // tomadas del propio JSON (titleSlide/endBlank/chorusInterleave/…).
    static json ParseAndBuildSlides(const json& o);
};
} // namespace lumina
#endif
