// ============================================================================
//  Fusion-HP · NativeLibrary.cpp — datos del estudio nativo
// ============================================================================
#include "NativeLibrary.h"
#include "SlideState.h"
#include "Logger.h"
#include <filesystem>
#include <fstream>
#include <sstream>
#include <chrono>
#include <atomic>
#include <cwctype>
#include <ctime>

namespace fs = std::filesystem;

namespace fusion {

// ================================================================== temas
static std::vector<ThemeDef> sThemes;
static std::vector<BgDef> sBgs;

const std::vector<ThemeDef>& Themes() {
    if (sThemes.empty()) {
        sThemes = {
            {"tema-clasico",   "Clásico lumínico",  L"Outfit",             46, "#FFF8EC", "#E8C872", "#0A0704", "gold-rays.jpg",  true},
            {"tema-escritura", "Escritura",         L"Libre Baskerville",  42, "#F4EFE4", "#D4B56A", "#050814", "blue-depth.jpg", true},
            {"tema-broadcast", "Broadcast",         L"Outfit",             40, "#FFFFFF", "#7DD3FC", "#120814", "purple-haze.jpg",true},
            {"tema-moderno",   "Moderno liviano",   L"Outfit",             44, "#F8FAFC", "#FDBA74", "#06110C", "emerald.jpg",    false},
            {"tema-solemne",   "Solemne",           L"Cormorant Garamond", 52, "#F3E6C8", "#C4A35A", "#1A0E08", "cross-dawn.jpg", true},
            {"tema-calma",     "Calma",             L"Segoe UI",           48, "#FFFFFF", "#FFD700", "#101820", "",               true},
        };
    }
    return sThemes;
}
const std::vector<BgDef>& Backgrounds() {
    if (sBgs.empty()) {
        sBgs = {
            {"fondo-dorado",  "Rayos de oro",   "gold-rays.jpg",   "#0A0704"},
            {"fondo-azul",    "Profundidad",    "blue-depth.jpg",  "#050814"},
            {"fondo-violeta", "Niebla púrpura", "purple-haze.jpg", "#120814"},
            {"fondo-esmeralda","Esmeralda",     "emerald.jpg",     "#06110C"},
            {"fondo-cruz",    "Cruz al alba",   "cross-dawn.jpg",  "#1A0E08"},
            {"fondo-santuario","Santuario",     "sanctuary.jpg",   "#0B0B10"},
        };
    }
    return sBgs;
}

std::wstring ResolveAppPath(const std::string& s) {
    if (s.rfind("app:", 0) == 0) {
        std::string rel = s.substr(4);
        if (rel.rfind("resources/", 0) == 0)
            return ExeDir() + L"\\" + ToWide(rel);
        return ExeDir() + L"\\resources\\" + ToWide(rel);
    }
    return ToWide(s);
}

static std::string sDefTransition = "fade";
std::string DefaultTransition() { return sDefTransition; }
void SetDefaultTransition(const std::string& v) {
    if (v == "cut" || v == "fade" || v == "slide") sDefTransition = v;
}

// ================================================================== ids
static std::atomic<int> sUid{0};
std::string NewId() {
    auto now = std::chrono::steady_clock::now().time_since_epoch().count();
    return "n" + std::to_string(now % 100000000) + std::to_string(++sUid);
}

// ================================================================== borrador
DraftSlide DefaultTitleSlide() {
    DraftSlide s;
    s.id = NewId();
    s.type = "text";
    s.title = L"Título";
    s.subtitle = L"Subtítulo";
    s.lines = {L"Título"};
    return s;
}
DraftSlide BlankSlide() {
    DraftSlide s;
    s.id = NewId();
    s.type = "blank";
    s.lines.clear();
    return s;
}
DraftSlide LowerThirdSlide() {
    DraftSlide s;
    s.id = NewId();
    s.type = "lower3";
    s.title = L"Tercio inferior";
    s.lines = {L"Línea principal", L"Detalle"};
    s.lowerThird = true;
    return s;
}

void AddSongToDraft(Draft& d, const LibSong& s, const std::string& themeId) {
    DraftItem it;
    it.id = NewId();
    it.type = "song";
    it.label = s.title;
    if (!s.artist.empty()) it.label += L" — " + s.artist;
    for (auto& t : s.tags) it.tags.push_back(t);
    for (auto& sec : s.sections) {
        DraftSlide sl;
        sl.id = NewId();
        sl.type = "text";
        sl.title = s.title;
        sl.subtitle = sec.name;
        sl.lines = sec.lines;
        sl.themeId = themeId;
        it.slides.push_back(std::move(sl));
    }
    if (it.slides.empty()) {           // canto sin secciones: una diapositiva
        it.slides.push_back(DefaultTitleSlide());
        it.slides[0].title = s.title;
        it.slides[0].lines = {s.title};
    }
    d.items.push_back(std::move(it));
    d.selItem = (int)d.items.size() - 1;
    d.selSlide = 0;
}

void AddVerseToDraft(Draft& d, const std::wstring& ref, const std::wstring& text,
                     bool lowerThird, const std::string& themeId) {
    DraftItem it;
    it.id = NewId();
    it.type = "verse";
    it.label = ref;
    DraftSlide sl;
    sl.id = NewId();
    sl.type = "verse";
    sl.reference = ref;
    sl.title = ref;
    // cortar el texto en renglones de ~78 caracteres por frase/oración
    std::wstring line;
    for (size_t i = 0; i <= text.size(); i++) {
        wchar_t c = i < text.size() ? text[i] : L' ';
        line += c;
        bool punto = c == L'.' || c == L';' || c == L'?';
        if ((punto && line.size() > 40) || line.size() > 86) {
            while (!line.empty() && (line.back() == L' ' || line.back() == L'\n')) line.pop_back();
            if (!line.empty()) sl.lines.push_back(line);
            line.clear();
        }
    }
    while (!line.empty() && (line.back() == L' ' || line.back() == L'\n')) line.pop_back();
    if (!line.empty()) sl.lines.push_back(line);
    sl.themeId = themeId;
    sl.lowerThird = lowerThird;
    it.slides.push_back(std::move(sl));
    d.items.push_back(std::move(it));
    d.selItem = (int)d.items.size() - 1;
    d.selSlide = 0;
}

// ---- helpers de resolución (herencia: tema del proyecto → del elemento) ----
static const ThemeDef* FindTheme(const std::string& id) {
    for (auto& t : Themes()) if (t.id == id) return &t;
    return nullptr;
}
static const BgDef* FindBg(const std::string& id) {
    for (auto& b : Backgrounds()) if (b.id == id) return &b;
    return nullptr;
}

static Json StyleJsonOf(const DraftSlide& s) {
    const ThemeDef* t = FindTheme(s.themeId.empty() ? "tema-clasico" : s.themeId);
    Json st = Json::object();
    if (t) {
        st["font"] = ToUtf8(t->font);
        st["size"] = t->size;
        st["color"] = t->color;
        st["activeColor"] = t->activeColor;
        st["align"] = t->align1 ? 1 : 0;
        st["vAlign"] = 1;
        st["shadow"] = t->shadow;
        st["lineSpacing"] = 1.2;
    } else {
        st["font"] = "Segoe UI";
        st["size"] = 46;
        st["color"] = "#FFFFFF";
        st["activeColor"] = "#FFD700";
        st["align"] = 1;
        st["vAlign"] = 1;
        st["shadow"] = true;
    }
    if (s.fontSize > 0) st["size"] = s.fontSize;      // override de la diapositiva
    if (s.align >= 0) st["align"] = s.align;
    return st;
}

static Json BgJsonOf(const DraftSlide& s) {
    Json bg = Json::object();
    const ThemeDef* t = FindTheme(s.themeId.empty() ? "tema-clasico" : s.themeId);
    const BgDef* b = FindBg(s.bgId);
    if (b) {
        bg["color"] = b->color;
        bg["image"] = "app:resources/backgrounds/" + b->file;
    } else if (t && !t->bgImage.empty()) {
        bg["color"] = t->bg;
        bg["image"] = "app:resources/backgrounds/" + t->bgImage;
    } else {
        bg["color"] = t ? t->bg : "#000000";
    }
    return bg;
}

static Json SlideJsonOf(const DraftSlide& s, bool animate) {
    Json x = Json::object();
    x["id"] = s.id;
    std::string kind = s.type == "verse" ? "verse"
                     : s.type == "lower3" ? "lower3"
                     : s.type == "image" ? "image"
                     : s.type == "video" ? "video" : "text";
    if (s.lowerThird) kind = "lower3";
    x["kind"] = kind;
    Json lines = Json::array();
    for (auto& l : s.lines) lines.push_back(ToUtf8(l));
    x["lines"] = lines;
    if (!s.reference.empty()) x["reference"] = ToUtf8(s.reference);
    x["style"] = StyleJsonOf(s);
    x["bg"] = BgJsonOf(s);
    x["transition"] = s.transition.empty()
                     ? (animate ? sDefTransition : "cut")
                     : (animate ? s.transition : "cut");
    if (s.lowerThird) {
        Json ov = Json::object();
        ov["present"] = true;
        Json ol = Json::array();
        for (auto& l : s.lines) ol.push_back(ToUtf8(l));
        ov["lines"] = ol;
        ov["position"] = 0;
        x["overlay"] = ov;
    }
    if (s.type == "image" || s.type == "video") {
        Json m = Json::object();
        m["src"] = s.src;
        x["media"] = m;
    }
    return x;
}

Json SlideJsonForPreview(const DraftSlide& s) { return SlideJsonOf(s, true); }

Json DraftToProgram(const Draft& d, bool withSelect, bool animate) {
    Json program = Json::array();
    for (auto& it : d.items) {
        Json scn = Json::object();
        scn["id"] = it.id;
        scn["title"] = ToUtf8(it.label);
        Json els = Json::array();
        for (auto& sl : it.slides) {
            if (sl.type == "blank") continue;        // pausa = sin diapositiva
            Json el = Json::object();
            el["id"] = sl.id;
            el["title"] = ToUtf8(sl.title.empty() ? it.label : sl.title);
            el["slide"] = SlideJsonOf(sl, animate);
            els.push_back(el);
        }
        scn["elements"] = els;
        program.push_back(scn);
    }
    Json payload = Json::object();
    payload["program"] = program;
    if (withSelect) {
        Json sel = Json::object();
        sel["scenario"] = d.selItem;
        sel["element"] = 0;
        sel["line"] = 0;
        payload["select"] = sel;
    }
    payload["advance"] = "line";
    return payload;
}

bool SaveDraftAhp(const Draft& d, const std::wstring& path) {
    try {
        Json root;
        root["format"] = "ahp.v1";
        Json proj;
        proj["name"] = ToUtf8(d.name);
        Json themes = Json::array();
        for (auto& t : Themes()) {
            Json tj;
            tj["id"] = t.id;
            tj["name"] = t.name;
            Json st; st["font"] = ToUtf8(t.font); st["size"] = t.size;
            st["color"] = t.color; st["activeColor"] = t.activeColor;
            st["align"] = 1; st["vAlign"] = 1; st["shadow"] = t.shadow;
            st["lineSpacing"] = 1.2;
            tj["style"] = st;
            Json bg; bg["color"] = t.bg;
            if (!t.bgImage.empty()) bg["image"] = "app:resources/backgrounds/" + t.bgImage;
            tj["bg"] = bg;
            themes.push_back(tj);
        }
        proj["themes"] = themes;
        proj["themeRef"] = "tema-clasico";
        Json scns = Json::array();
        for (auto& it : d.items) {
            Json sc;
            sc["id"] = it.id;
            sc["title"] = ToUtf8(it.label);
            Json tags = Json::array();
            for (auto& tg : it.tags) tags.push_back(ToUtf8(tg));
            if (!tags.empty()) sc["tags"] = tags;
            Json els = Json::array();
            for (auto& sl : it.slides) {
                Json el;
                el["id"] = sl.id;
                el["type"] = sl.type == "blank" ? "text" : sl.type;
                if (sl.type == "verse") {
                    Json vs = Json::array();
                    for (auto& l : sl.lines) vs.push_back(ToUtf8(l));
                    el["verses"] = vs;
                    el["reference"] = ToUtf8(sl.reference);
                } else if (sl.type == "image" || sl.type == "video") {
                    el["src"] = sl.src;
                } else {
                    Json ls = Json::array();
                    for (auto& l : sl.lines) ls.push_back(ToUtf8(l));
                    el["lines"] = ls;
                }
                if (!sl.themeId.empty() || !sl.bgId.empty() || !sl.transition.empty()) {
                    Json ov;
                    if (!sl.themeId.empty()) {
                        const ThemeDef* t = FindTheme(sl.themeId);
                        if (t) {
                            Json st; st["font"] = ToUtf8(t->font); st["size"] = t->size;
                            st["color"] = t->color; st["activeColor"] = t->activeColor;
                            ov["style"] = st;
                        }
                    }
                    if (!sl.bgId.empty()) {
                        const BgDef* b = FindBg(sl.bgId);
                        if (b) {
                            Json bg; bg["color"] = b->color;
                            bg["image"] = "app:resources/backgrounds/" + b->file;
                            ov["bg"] = bg;
                        }
                    }
                    if (!sl.transition.empty()) ov["transition"] = sl.transition;
                    if (!ov.empty()) el["styleOverride"] = ov;
                }
                els.push_back(el);
            }
            sc["elements"] = els;
            scns.push_back(sc);
        }
        proj["scenarios"] = scns;
        root["project"] = proj;

        std::wstring tmp = path + L".tmp";
        std::ofstream f(fs::path(tmp), std::ios::binary);
        if (!f) return false;
        std::string out = root.dump(1);
        f.write(out.data(), (std::streamsize)out.size());
        f.close();
        fs::rename(tmp, path);          // atómico
        return true;
    } catch (const std::exception&) {
        return false;
    }
}

Draft DraftFromMotorState(const Json& motorState) {
    Draft d;
    try {
        if (motorState.contains("program") && motorState["program"].is_array()) {
            for (auto& sc : motorState["program"]) {
                DraftItem it;
                it.id = sc.value("id", std::string());
                it.label = ToWide(sc.value("title", std::string("Elemento")));
                if (sc.contains("elements") && sc["elements"].is_array()) {
                    for (auto& el : sc["elements"]) {
                        DraftSlide sl;
                        sl.id = el.value("id", std::string());
                        sl.title = ToWide(el.value("title", std::string()));
                        sl.type = el.value("kind", std::string("text"));
                        if (el.contains("slide") && el["slide"].is_object()) {
                            const Json& s = el["slide"];
                            if (s.contains("lines") && s["lines"].is_array())
                                for (auto& l : s["lines"])
                                    if (l.is_string()) sl.lines.push_back(ToWide(l.get<std::string>()));
                            if (s.contains("reference")) sl.reference = ToWide(s["reference"].get<std::string>());
                            if (s.contains("transition")) sl.transition = s["transition"].get<std::string>();
                            if (s.contains("overlay") && s["overlay"].value("present", false))
                                sl.lowerThird = true;
                            if (s.contains("bg") && s["bg"].contains("image") &&
                                s["bg"]["image"].is_string()) {
                                std::string im = s["bg"]["image"].get<std::string>();
                                for (auto& b : Backgrounds())
                                    if (im.find(b.file) != std::string::npos) { sl.bgId = b.id; break; }
                            }
                        }
                        it.slides.push_back(std::move(sl));
                    }
                }
                if (!it.slides.empty()) d.items.push_back(std::move(it));
            }
        }
        if (motorState.contains("select") && motorState["select"].is_object()) {
            d.selItem = motorState["select"].value("scenario", -1);
            d.selSlide = motorState["select"].value("element", 0);
            if (d.selItem >= (int)d.items.size()) d.selItem = (int)d.items.size() - 1;
            if (d.selItem < 0 && !d.items.empty()) d.selItem = 0;
        }
    } catch (const std::exception&) {
        // estado corrupto → borrador vacío
    }
    return d;
}

// ================================================================== cantos
static std::wstring Norm(std::wstring s) {
    // minúsculas + sin acentos (búsqueda tolerante tipo web normalize)
    for (auto& c : s) {
        c = towlower(c);
        switch (c) {
            case L'á': c = L'a'; break; case L'é': c = L'e'; break;
            case L'í': c = L'i'; break; case L'ó': c = L'o'; break;
            case L'ú': c = L'u'; break; case L'ñ': c = L'n'; break;
            case L'ü': c = L'u'; break;
        }
    }
    return s;
}

bool SongLibrary::Load(const std::wstring& fdbPath) {
    loaded_ = false;
    songs_.clear();
    try {
        std::ifstream f(fs::path(fdbPath), std::ios::binary);
        if (!f) return false;
        Json root = Json::parse(f);
        if (!root.is_object() || !root.contains("songs") || !root["songs"].is_array())
            return false;
        for (auto& sj : root["songs"]) {
            if (!sj.is_object()) continue;
            LibSong s;
            s.id = sj.value("id", std::string());
            s.title = ToWide(sj.value("title", std::string()));
            s.artist = ToWide(sj.value("artist", std::string()));
            s.language = ToWide(sj.value("language", std::string("es")));
            s.key_ = ToWide(sj.value("key", std::string()));
            s.bpm = (int)sj.value("bpm", 0);
            s.lastUsed = sj.value("lastUsed", std::string());
            if (sj.contains("tags") && sj["tags"].is_array())
                for (auto& t : sj["tags"]) if (t.is_string()) s.tags.push_back(ToWide(t.get<std::string>()));
            if (sj.contains("sections") && sj["sections"].is_array()) {
                for (auto& sec : sj["sections"]) {
                    if (!sec.is_object()) continue;
                    LibSong::Sec sc;
                    sc.name = ToWide(sec.value("name", std::string()));
                    if (sec.contains("lines") && sec["lines"].is_array())
                        for (auto& l : sec["lines"])
                            if (l.is_string()) sc.lines.push_back(ToWide(l.get<std::string>()));
                    s.sections.push_back(std::move(sc));
                }
            } else if (sj.contains("lyrics") && sj["lyrics"].is_array()) {
                // variante legada: líneas planas → una sección
                LibSong::Sec sc;
                sc.name = L"Letra";
                for (auto& l : sj["lyrics"])
                    if (l.is_string()) sc.lines.push_back(ToWide(l.get<std::string>()));
                s.sections.push_back(std::move(sc));
            }
            if (!s.title.empty()) songs_.push_back(std::move(s));
        }
        loaded_ = true;
        return true;
    } catch (const std::exception& e) {
        Logger::Warn("core.library", std::string("cancionero no legible: ") + e.what());
        return false;
    }
}

std::vector<int> SongLibrary::Search(const std::wstring& q, size_t n) const {
    std::vector<int> out;
    std::wstring nq = Norm(q);
    for (size_t i = 0; i < songs_.size() && out.size() < n; i++) {
        if (nq.empty()) { out.push_back((int)i); continue; }
        std::wstring hay = songs_[i].title + L" " + songs_[i].artist;
        for (auto& t : songs_[i].tags) hay += L" " + t;
        if (Norm(hay).find(nq) != std::wstring::npos) out.push_back((int)i);
    }
    return out;
}

// ================================================================== biblias
bool BibleLibrary::Discover(const std::wstring& dir) {
    bibles_.clear();
    std::error_code ec;
    if (!fs::exists(dir, ec)) return false;
    for (auto& e : fs::directory_iterator(dir, ec)) {
        if (!e.is_regular_file()) continue;
        if (e.path().extension() != L".json") continue;
        BibleRef r;
        r.path = e.path().wstring();
        // leer solo la cabecera (los ~4 KB iniciales contienen version/name):
        // evita parsear 4 biblias completas de 30-60 MB solo para el índice.
        try {
            std::ifstream f(fs::path(r.path), std::ios::binary);
            std::string head(4096, '\0');
            f.read(&head[0], (std::streamsize)head.size());
            head.resize((size_t)f.gcount());
            auto Qs = [&](const char* key) -> std::string {
                auto pos = head.find(key);
                if (pos == std::string::npos) return std::string();
                pos = head.find(':', pos + strlen(key) - 1);
                if (pos == std::string::npos) return std::string();
                pos = head.find('"', pos);
                if (pos == std::string::npos) return std::string();
                std::string out;
                for (size_t i = pos + 1; i < head.size() && head[i] != '"'; i++) {
                    if (head[i] == '\\' && i + 1 < head.size()) i++;   // \" escape
                    out += head[i];
                }
                return out;
            };
            std::string ver = Qs("\"version\"");
            std::string nm = Qs("\"name\"");
            r.id = ver.empty() ? e.path().stem().string() : ver;
            r.name = nm.empty() ? e.path().stem().string() : nm;
        } catch (...) {
            continue;
        }
        bibles_.push_back(std::move(r));
    }
    return !bibles_.empty();
}

bool BibleLibrary::Select(size_t idx) {
    if (idx >= bibles_.size()) return false;
    sel_ = (int)idx;
    return LoadSelected();
}

bool BibleLibrary::LoadSelected() {
    books_.clear();
    if (sel_ < 0 || sel_ >= (int)bibles_.size()) return false;
    try {
        std::ifstream f(fs::path(bibles_[(size_t)sel_].path), std::ios::binary);
        Json root = Json::parse(f);
        if (!root.contains("books") || !root["books"].is_array()) return false;
        for (auto& bj : root["books"]) {
            Bk b;
            b.name = ToWide(bj.value("name", std::string()));
            if (bj.contains("chapters") && bj["chapters"].is_array())
                for (auto& cj : bj["chapters"]) {
                    std::vector<std::wstring> vs;
                    if (cj.is_array())
                        for (auto& v : cj)
                            if (v.is_string()) vs.push_back(ToWide(v.get<std::string>()));
                    b.chapters.push_back(std::move(vs));
                }
            books_.push_back(std::move(b));
        }
        return !books_.empty();
    } catch (const std::exception& e) {
        Logger::Warn("core.bible", std::string("biblia no legible: ") + e.what());
        return false;
    }
}

size_t BibleLibrary::BookCount() const { return books_.size(); }
std::wstring BibleLibrary::BookName(size_t i) const {
    return i < books_.size() ? books_[i].name : std::wstring();
}
int BibleLibrary::ChapterCount(const std::wstring& book) const {
    for (auto& b : books_) if (Norm(b.name) == Norm(book)) return (int)b.chapters.size();
    return 0;
}
int BibleLibrary::VerseCount(const std::wstring& book, int chapter) const {
    for (auto& b : books_)
        if (Norm(b.name) == Norm(book) && chapter >= 1 && chapter <= (int)b.chapters.size())
            return (int)b.chapters[(size_t)chapter - 1].size();
    return 0;
}
std::wstring BibleLibrary::Verse(const std::wstring& book, int chapter, int verse) const {
    for (auto& b : books_)
        if (Norm(b.name) == Norm(book) && chapter >= 1 && chapter <= (int)b.chapters.size()) {
            auto& vs = b.chapters[(size_t)chapter - 1];
            if (verse >= 1 && verse <= (int)vs.size()) return vs[(size_t)verse - 1];
        }
    return std::wstring();
}

bool BibleLibrary::ParseRef(const std::wstring& q, std::wstring& book, int& ch, int& v) {
    // "juan 3:16" | "1 co 13:4-7" | "salmos 23"
    std::wstring s = q;
    for (auto& c : s) if (c == L',' || c == L'.') c = L':';
    // separar: texto del libro = prefijo no numérico
    size_t i = 0;
    while (i < s.size() && !iswdigit(s[i]) && s[i] != L':') i++;
    // retroceder si el número forma parte del nombre (1 Juan, 2 Samuel…)
    while (i > 0 && (s[i - 1] == L' ' || iswalpha(s[i - 1]))) {
        // mirar si tras i hay "libro" conocido: si el prefijo ya termina en dígito+espacio
        bool digitName = i >= 2 && iswdigit(s[i - 2]) && s[i - 1] == L' ';
        if (!digitName) break;
        // el dígito pertenece al nombre solo si lo que sigue no es cap:verso
        break;
    }
    std::wstring b = s.substr(0, i);
    while (!b.empty() && b.back() == L' ') b.pop_back();
    std::wstring rest = s.substr(i);
    int c1 = 0, v1 = 0;
    if (!rest.empty()) {
        if (swscanf(rest.c_str(), L"%d:%d", &c1, &v1) < 1) return false;
    }
    if (b.empty() && c1 == 0) return false;
    book = b; ch = c1; v = v1;
    return !book.empty() && ch > 0;
}

bool BibleLibrary::Range(const std::wstring& q, std::wstring& ref,
                         std::vector<std::wstring>& lines) const {
    std::wstring book; int ch = 0, v = 0;
    if (!ParseRef(q, book, ch, v)) return false;
    // rango "3:16-18"
    int v2 = v;
    size_t dash = q.find_last_of(L'-');
    if (dash != std::wstring::npos && v > 0) {
        int end = 0;
        if (swscanf(q.substr(dash + 1).c_str(), L"%d", &end) == 1 && end >= v) v2 = end;
    }
    // localizar libro (normalizado, con alias)
    const Bk* bk = nullptr;
    std::wstring nq = Norm(book);
    for (auto& b : books_) if (Norm(b.name) == nq) { bk = &b; break; }
    if (!bk) for (auto& b : books_) if (Norm(b.name).find(nq) == 0) { bk = &b; break; }
    if (!bk) return false;
    if (ch < 1 || ch > (int)bk->chapters.size()) return false;
    auto& vs = bk->chapters[(size_t)ch - 1];
    if (v < 1) v = 1;
    if (v2 > (int)vs.size()) v2 = (int)vs.size();
    std::wstring text;
    for (int i = v; i <= v2 && i <= (int)vs.size(); i++) {
        if (!text.empty()) text += L" ";
        text += vs[(size_t)i - 1];
    }
    if (text.empty()) return false;
    ref = bk->name + L" " + std::to_wstring(ch) + L":" + std::to_wstring(v);
    if (v2 > v) ref += L"-" + std::to_wstring(v2);
    lines.push_back(text);
    return true;
}

// ================================================================== historial
void History::SetFile(const std::wstring& path) { path_ = path; }

void History::Record(const std::wstring& title, const std::wstring& kind) {
    LoadIfNeeded();
    HistoryEntry e;
    e.t = std::chrono::system_clock::to_time_t(std::chrono::system_clock::now()) * 1000;
    e.title = title; e.kind = kind;
    cache_.push_back(e);
    if (path_.empty()) return;
    try {
        std::ofstream f(fs::path(path_), std::ios::app | std::ios::binary);
        if (!f) return;
        Json j;
        j["t"] = e.t;
        j["title"] = ToUtf8(title);
        j["kind"] = ToUtf8(kind);
        f << j.dump() << "\n";
    } catch (...) { /* el historial nunca tumba la app */ }
}

void History::LoadIfNeeded() const {
    if (loaded_ || path_.empty()) { loaded_ = true; return; }
    loaded_ = true;
    try {
        std::ifstream f{fs::path(path_)};
        if (!f) return;
        std::string line;
        while (std::getline(f, line)) {
            if (line.empty()) continue;
            try {
                Json j = Json::parse(line);
                HistoryEntry e;
                e.t = j.value("t", (long long)0);
                e.title = ToWide(j.value("title", std::string()));
                e.kind = ToWide(j.value("kind", std::string()));
                cache_.push_back(e);
            } catch (...) { /* línea corrupta: saltar */ }
        }
    } catch (...) {}
}

std::vector<HistoryEntry> History::Recent(size_t n) const {
    LoadIfNeeded();
    std::vector<HistoryEntry> out;
    for (size_t i = cache_.size(); i-- > 0 && out.size() < n;)
        out.push_back(cache_[i]);
    return out;
}

std::vector<HistoryCount> History::Top(size_t n) const {
    LoadIfNeeded();
    std::map<std::wstring, HistoryCount> m;
    for (auto& e : cache_) {
        auto& c = m[e.title];
        c.title = e.title;
        c.count++;
        if (e.t > c.last) c.last = e.t;
    }
    std::vector<HistoryCount> v;
    for (auto& kv : m) v.push_back(kv.second);
    std::sort(v.begin(), v.end(), [](const HistoryCount& a, const HistoryCount& b) {
        return a.count != b.count ? a.count > b.count : a.last > b.last;
    });
    if (v.size() > n) v.resize(n);
    return v;
}

bool History::ExportCsv(const std::wstring& path) const {
    LoadIfNeeded();
    try {
        std::ofstream f(fs::path(path), std::ios::binary);
        if (!f) return false;
        f << "\xEF\xBB\xBF";           // BOM UTF-8 (Excel es-VE)
        f << "fecha;titulo;tipo\n";
        for (auto& e : cache_) {
            char buf[32];
            time_t t = (time_t)(e.t / 1000);
            struct tm lt;
            localtime_s(&lt, &t);
            strftime(buf, sizeof(buf), "%Y-%m-%d %H:%M", &lt);
            std::string title = ToUtf8(e.title), kind = ToUtf8(e.kind);
            for (auto& c : title) if (c == ';') c = ',';
            for (auto& c : kind) if (c == ';') c = ',';
            f << buf << ";" << title << ";" << kind << "\n";
        }
        return true;
    } catch (...) { return false; }
}

} // namespace fusion
