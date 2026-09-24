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

// v7.0.0 «ULTRA»: ruta RELATIVA al amalgamado — el launcher compila el
// Projector/Renderer para el MODO EMERGENCIA (F0.08) con sus propios CMake
// (sin los include_dirs del núcleo) y resuelve nlohmann por esta vía; el
// núcleo sigue encontrando el mismo archivo (la ruta relativa parte de
// core/src/ en ambos casos).
#include "../../../third_party/nlohmann/json.hpp"

namespace lumina {

using json = nlohmann::json;

/* ------------------------------- Slides -------------------------------- */
enum SlideKind {
    SLIDE_TITLE = 0,     // título/artista/tono
    SLIDE_TEXT  = 1,     // letra/contenido
    SLIDE_BLANK = 2,     // en blanco
    SLIDE_SCRIPTURE = 3, // texto bíblico con referencia
    SLIDE_IMAGE = 4,     // imagen con texto opcional
    SLIDE_VIDEO = 5      // video DirectShow (v7.0.0 — F3.01)
};

struct SlideLine {
    std::string text;    // texto visible (UTF-8)
    std::string chords;  // línea de cifrado adjunta (puede ser vacía)
    // v7.0.0 «ULTRA» (F1.03/F2.03): marca de sincronización por línea.
    // 0 = sin marca. >0 = grupo de sincronización: «avanzar» salta a la
    // primera línea del siguiente grupo (autoavance por syncMark). Las
    // líneas contiguas con la MISMA marca comparten paso de sincronización.
    int syncMark = 0;
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
    // v7.0.0 «ULTRA» (F3.01): elemento Video desde el núcleo (DirectShow).
    std::string videoPath;         // ruta del archivo de video
    int    videoVolume  = 100;     // volumen inicial 0..100 (-1 = silencio)
    int64_t videoStartAtMs = 0;    // posición inicial (startAt)
    bool   videoLoop     = false;  // repetir al llegar al final
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
    // v7.0.0 «ULTRA» (F1.01): estilo de LÍNEA ACTIVA configurable por tema.
    // dimInactive: atenúa las líneas no activas (alpha atenuado) cuando el
    // motor informa línea activa (syncMark). activeLineColor: color de la
    // línea activa ("" = usar accentColor). activeLineBold: negrita extra.
    bool   dimInactive   = false;   // false = comportamiento clásico
    std::string activeLineColor;    // "" o "#AARRGGBB"
    bool   activeLineBold = false;

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
    // v7.0.0 «ULTRA» (F3.01): video DirectShow desde el núcleo.
    std::string videoPath;
    int    videoVolume   = 100;
    int64_t videoStartAtMs = 0;
    bool   videoLoop      = false;
    // v7.0.0 «ULTRA» (F2.03): líneas estructuradas con syncMark (formato
    // ahp.v1). Vacío = se usa 'text' plano (compatibilidad clásica).
    std::vector<SlideLine> structuredLines;
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
