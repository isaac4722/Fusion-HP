// ============================================================================
//  Fusion-HP · NativeLibrary — datos del estudio nativo C++ (v2.3)
//  Acceso de SOLO LECTURA a las bases que ya viajan con el programa:
//   · cancionero.fdb (fdb.v1) — el cancionero único del usuario [v2.1]
//   · biblias JSON empaquetadas (resources/data/bibles) — RV1960/NVI/RVG/RVR1909
//   · historial de uso (datos/historial.jsonl) — función beta-1 [v1.6 HistoryPanel]
//  Además: catálogo de temas/fondos web, borrador de presentación (modelo de la
//  referencia web), constructor de programa motor.load y guardado .ahp.
//  La escritura del cancionero/biblias sigue siendo tarea de la capa C#
//  (FusionStudio); el estudio nativo consulta sin generar archivos por canto.
// ============================================================================
#pragma once
#include "Common.h"

namespace fusion {

// ------------------------------------------------------------------ temas
struct ThemeDef {
    std::string id, name;
    std::wstring font;
    int size;
    std::string color, activeColor, bg, bgImage;   // colores #RRGGBB; bgImage nombre app:
    bool shadow;
    bool align1 = true;                             // centro
};
struct BgDef {
    std::string id, name, file, color;
};
const std::vector<ThemeDef>& Themes();
const std::vector<BgDef>& Backgrounds();
std::wstring ResolveAppPath(const std::string& s);   // "app:x" o ruta → absoluta

// Transición predeterminada global (cut|fade|slide) — la usan las diapositivas
// sin transición propia (pestaña Transiciones de la web).
std::string DefaultTransition();
void SetDefaultTransition(const std::string& v);

// ------------------------------------------------------------------ borrador
struct DraftSlide {
    std::string id;
    std::wstring title, subtitle, notes;
    std::vector<std::wstring> lines;
    std::string type = "text";          // text|verse|image|video|lower3|blank
    std::string themeId, bgId;          // "" = por defecto
    std::string transition;             // "" | cut | fade | slide
    bool lowerThird = false;
    std::string src;                    // imagen/video
    std::wstring reference;             // versículo
    int fontSize = 0;                   // 0 = tamaño del tema
    int align = -1;                     // -1 tema · 0 izq · 1 centro · 2 der
};
struct DraftItem {
    std::string id;
    std::wstring label;
    std::string type = "song";          // song|verse|title|image|video|audio|blank
    std::vector<std::wstring> tags;
    std::vector<DraftSlide> slides;
};
struct Draft {
    std::wstring name = L"Presentación sin título";
    std::vector<DraftItem> items;
    int selItem = -1, selSlide = 0;
    std::vector<DraftSlide> clipSlides_;   // portapapeles de diapositivas (web)
    std::string clipType_;
};

std::string NewId();                       // uid() corto
DraftSlide DefaultTitleSlide();            // Ctrl+M
DraftSlide BlankSlide();
DraftSlide LowerThirdSlide();
void AddSongToDraft(Draft& d, const struct LibSong& s, const std::string& themeId);
void AddVerseToDraft(Draft& d, const std::wstring& ref, const std::wstring& text,
                     bool lowerThird, const std::string& themeId);

// borrador → payload motor.load (diapositivas RESUELTAS, herencia aplicada,
// transición horneada). select opcional.
Json DraftToProgram(const Draft& d, bool withSelect = true, bool animate = true);

// UNA diapositiva del borrador → slide ipc.v1 resuelta (para miniaturas y
// vista previa del editor sin pasar por el Motor).
Json SlideJsonForPreview(const DraftSlide& s);

// borrador → .ahp (ahp.v1, reabrible por NativeSession y por la capa C#)
bool SaveDraftAhp(const Draft& d, const std::wstring& path);

// estado del Motor → borrador (continuar editando lo que está en vivo)
Draft DraftFromMotorState(const Json& motorState);

// ------------------------------------------------------------------ cantos
struct LibSong {
    std::string id;
    std::wstring title, artist, key_, language;
    int bpm = 0;
    std::vector<std::wstring> tags;
    struct Sec { std::wstring name; std::vector<std::wstring> lines; };
    std::vector<Sec> sections;
    std::string lastUsed;
};

class SongLibrary {
public:
    bool Load(const std::wstring& fdbPath);          // fdb.v1
    size_t Count() const { return songs_.size(); }
    const LibSong* At(size_t i) const { return i < songs_.size() ? &songs_[i] : nullptr; }
    // índices que matchean el texto (título+artista+etiquetas), límite n
    std::vector<int> Search(const std::wstring& q, size_t n = 200) const;
    bool Loaded() const { return loaded_; }
private:
    std::vector<LibSong> songs_;
    bool loaded_ = false;
};

// ------------------------------------------------------------------ biblias
struct BibleRef { std::string id, name; std::wstring path; };

class BibleLibrary {
public:
    // carga el índice de biblias empaquetadas (resources/data/bibles/*.json)
    bool Discover(const std::wstring& biblesDir);
    size_t Count() const { return bibles_.size(); }
    const BibleRef* At(size_t i) const { return i < bibles_.size() ? &bibles_[i] : nullptr; }
    // activa una biblia (parsea el JSON completo, ~31k versículos)
    bool Select(size_t idx);
    int Selected() const { return sel_; }
    // libros y versículos
    size_t BookCount() const;
    std::wstring BookName(size_t i) const;
    // texto de un versículo; devuelve vacío si no existe
    std::wstring Verse(const std::wstring& book, int chapter, int verse) const;
    int ChapterCount(const std::wstring& book) const;
    int VerseCount(const std::wstring& book, int chapter) const;
    // rango Juan 3:16-18 → texto con saltos + referencia "Juan 3:16-18"
    bool Range(const std::wstring& q, std::wstring& ref, std::vector<std::wstring>& lines) const;
    // parseo "juan 3:16" → libro/capítulo/versículo (alias comunes sin acentos)
    static bool ParseRef(const std::wstring& q, std::wstring& book, int& ch, int& v);
private:
    std::vector<BibleRef> bibles_;
    int sel_ = -1;
    struct Bk { std::wstring name; std::vector<std::vector<std::wstring>> chapters; };
    std::vector<Bk> books_;
    bool LoadSelected();
};

// ------------------------------------------------------------------ historial
struct HistoryEntry {
    long long t = 0;                     // epoch ms
    std::wstring title, kind;
};
struct HistoryCount { std::wstring title; int count; long long last = 0; };

class History {
public:
    void SetFile(const std::wstring& path);
    void Record(const std::wstring& title, const std::wstring& kind);   // jsonl
    std::vector<HistoryEntry> Recent(size_t n = 40) const;
    std::vector<HistoryCount> Top(size_t n = 20) const;                // más usadas
    bool ExportCsv(const std::wstring& path) const;
private:
    std::wstring path_;
    mutable std::vector<HistoryEntry> cache_;
    mutable bool loaded_ = false;
    void LoadIfNeeded() const;
};

} // namespace fusion
