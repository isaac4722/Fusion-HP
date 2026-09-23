// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Models.h : estructuras internas del núcleo (contrato fijo entre módulos).
//  Todo std::string en UTF-8. Serialización JSON con nlohmann (third_party).
// ============================================================================
#ifndef LUMINA_MODELS_H
#define LUMINA_MODELS_H

#include <string>
#include <vector>

#include <nlohmann/json.hpp>

namespace lumina {

using json = nlohmann::json;

/* ------------------------------- Slides -------------------------------- */
enum SlideKind {
    SLIDE_TITLE = 0,     // título/artista/tono
    SLIDE_TEXT  = 1,     // letra/contenido
    SLIDE_BLANK = 2,     // en blanco
    SLIDE_SCRIPTURE = 3, // texto bíblico con referencia
    SLIDE_IMAGE = 4      // imagen con texto opcional
};

struct SlideLine {
    std::string text;    // texto visible (UTF-8)
    std::string chords;  // línea de cifrado adjunta (puede ser vacía)
    SlideLine() {}
    explicit SlideLine(const std::string& t) : text(t) {}
    SlideLine(const std::string& t, const std::string& c) : text(t), chords(c) {}
};

struct Slide {
    int kind = SLIDE_TEXT;
    std::string title;      // título de la canción/ítem (pie o superposición)
    std::string refLabel;   // etiqueta de referencia ("Verso 1", "Juan 3:16")
    std::vector<SlideLine> lines;
    std::string imagePath;  // solo SLIDE_IMAGE
    // v6.0.0 «HORIZONTE»: palabra/frase a resaltar en PROYECCIÓN (color de
    // acento, coincidencia insensible a mayúsculas/acento — ver Highlight.h).
    // Cadena vacía = sin resaltado. Evolución ADITIVA del contrato de slide.
    std::string highlight;
};

/* -------------------------------- Tema --------------------------------- */
struct Theme {
    std::string name = "Predeterminado";
    std::string bgColor    = "#FF0B1F2A";   // AARRGGBB
    std::string fgColor    = "#FFFFFFFF";
    std::string accentColor= "#FF3AA6B9";
    std::string fontFace   = "Segoe UI";
    int    fontSize    = 54;                // px @1080p lógico
    bool   bold        = false;
    std::string imagePath;                  // fondo de imagen opcional
    int    imageMode   = 1;                 // 0=contain 1=cover
    double outlineWidth= 2.0;
    int    shadowAlpha = 140;
    bool   uppercase   = false;
    double lineSpacing = 1.18;

    json ToJson() const;
    static Theme FromJson(const json& o, const Theme& fallback);
};

/* ------------------------------- Canción ------------------------------- */
struct SongBlock {
    std::string label;                  // "Verso 1", "Coro"…
    std::vector<std::string> lines;     // líneas crudas (pueden incluir cifrado)
    int repeat = 1;
};

struct Song {
    std::string id, title, artist, keyName, tags, lyrics;
    int bpm = 0;
    bool  latinChords      = true;
    bool  chorusInterleave = false;     // Modo Hinario
    bool  titleSlide       = true;
    bool  endBlank         = false;
    bool  stripChords      = true;
    int   maxLinesPerSlide = 4;
    int   transpose        = 0;
    std::vector<SongBlock> blocks;

    // lyrics crudo desde blocks (si blocks no está vacío manda sobre lyrics)
    std::string LyricsText() const;
};

/* ------------------------------- Escenario ------------------------------ */
struct ScenarioItem {
    std::string kind;         // "song" | "scripture" | "blank" | "image" | "text" | "video"
    std::string title;
    Song       song;          // kind=song
    std::string ref;          // kind=scripture ("Jn 3:16-18")
    std::string version;      // kind=scripture: versión bíblica (si hay BD)
    std::string text;         // kind=text/scripture crudo (versos separados por \n)
    std::string imagePath;    // kind=image
    int   maxLinesPerSlide = 4;  // kind=text
    int   versesPerSlide = 1;    // kind=scripture (v5.2.0: era "reservado" — el
                                 // motor ahora lo HONRA; antes siempre 1/slide)
    // v6.0.0: palabra/frase a resaltar en proyección (p. ej. término de la
    // búsqueda bíblica que originó el ítem). El motor la propaga a las slides
    // de text/scripture/image; el Renderer pinta los matches en acento.
    std::string highlight;
};

struct Scenario {
    std::string name;
    Theme theme;
    std::vector<ScenarioItem> items;
};

/* ----------------------- Construcción de slides ------------------------ */
struct BuildOptions {
    bool  titleSlide       = true;
    bool  endBlank         = false;
    bool  chorusInterleave = false;
    int   maxLinesPerSlide = 4;
    bool  stripChords      = true;
    bool  latinChords      = true;
    int   transpose        = 0;
};

} // namespace lumina
#endif // LUMINA_MODELS_H
