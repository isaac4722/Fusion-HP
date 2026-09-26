// ============================================================================
//  Fusion-HP · NativeSession.cpp
// ============================================================================
#include "NativeSession.h"
#include "Logger.h"
#include <filesystem>

namespace fs = std::filesystem;

namespace fusion {

void NativeSession::ApplyTheme(Json& style, const Json& theme, const Json& scenario) const {
    // Herencia (subconjunto nativo): Tema → Plantilla opcional → Escenario → Elemento
    // Las claves ausentes en el nivel bajo heredan del alto [SPEC §5.4]
    Json merged = Json::object();
    for (const Json* layer : {&theme, &scenario}) {
        if (!layer->is_object()) continue;
        for (auto it = layer->begin(); it != layer->end(); ++it) {
            if (!it.value().is_null()) merged[it.key()] = it.value();
        }
    }
    style = merged;
}

bool NativeSession::Load(const std::wstring& path) {
    error_.clear();
    items_.clear();

    FILE* f = _wfsopen(path.c_str(), L"rb", _SH_DENYNO);
    if (!f) { error_ = "no se pudo abrir el archivo"; return false; }
    std::string data;
    char buf[65536];
    size_t n;
    while ((n = fread(buf, 1, sizeof(buf), f)) > 0) data.append(buf, n);
    fclose(f);

    Json root;
    try {
        root = Json::parse(data);
    } catch (const std::exception& e) {
        error_ = std::string("json invalido: ") + e.what();
        return false;
    }
    if (!root.is_object() || !root.contains("format") || root["format"] != "ahp.v1") {
        error_ = "formato no reconocido (se espera ahp.v1)";
        return false;
    }

    baseDir_ = fs::path(path).parent_path().wstring();
    const Json& proj = root.contains("project") ? root["project"] : root;

    if (proj.contains("name")) name_ = ToWide(proj["name"].get<std::string>());

    // Tema del proyecto (nivel 1 de la herencia)
    Json theme = Json::object();
    if (proj.contains("themes") && proj["themes"].is_array() && proj["themes"].size() > 0) {
        std::string ref = proj.value("themeRef", std::string());
        for (auto& t : proj["themes"]) {
            if (t.is_object() && t.value("id", std::string()) == ref) { theme = t; break; }
        }
        if (theme.empty() && proj["themes"][0].is_object()) theme = proj["themes"][0];
    }
    Json themeStyle = theme.contains("style") && theme["style"].is_object() ? theme["style"] : Json::object();
    Json themeBg = theme.contains("bg") && theme["bg"].is_object() ? theme["bg"] : Json::object();

    if (!proj.contains("scenarios") || !proj["scenarios"].is_array()) {
        error_ = "el proyecto no contiene escenarios";
        return false;
    }

    for (auto& sc : proj["scenarios"]) {
        if (!sc.is_object()) continue;
        NativeItem item;
        item.id = sc.value("id", std::string());
        item.title = ToWide(sc.value("title", std::string("Escenario")));
        if (sc.contains("tags") && sc["tags"].is_array())
            for (auto& t : sc["tags"]) item.tags.push_back(t.get<std::string>());

        // Estilo del escenario (nivel 3)
        Json scStyle = sc.contains("styleOverride") && sc["styleOverride"].is_object()
                       ? sc["styleOverride"] : Json::object();
        Json scBg = sc.contains("bgOverride") && sc["bgOverride"].is_object()
                    ? sc["bgOverride"] : Json::object();

        Json resolvedStyle = Json::object(), resolvedBg = Json::object();
        ApplyTheme(resolvedStyle, themeStyle, scStyle);
        ApplyTheme(resolvedBg, themeBg, scBg);

        if (!sc.contains("elements") || !sc["elements"].is_array()) continue;
        for (auto& el : sc["elements"]) {
            if (!el.is_object()) continue;
            Slide s;
            s.id = el.value("id", std::string());
            std::string type = el.value("type", std::string("text"));
            s.kind = SlideState::KindFromString(type);

            // Nivel 4: override del elemento
            Json elStyle = el.contains("styleOverride") && el["styleOverride"].is_object()
                           ? el["styleOverride"] : Json::object();
            Json finalStyle = resolvedStyle;
            for (auto it = elStyle.begin(); it != elStyle.end(); ++it)
                if (!it.value().is_null()) finalStyle[it.key()] = it.value();
            SlideState::ParseStyle(finalStyle, s.style);

            Json elBg = el.contains("bgOverride") && el["bgOverride"].is_object()
                        ? el["bgOverride"] : Json::object();
            Json finalBg = resolvedBg;
            for (auto it = elBg.begin(); it != elBg.end(); ++it)
                if (!it.value().is_null()) finalBg[it.key()] = it.value();

            if (s.kind == SlideKind::Text) {
                if (el.contains("lines") && el["lines"].is_array()) {
                    for (auto& l : el["lines"]) {
                        if (l.is_string()) s.lines.push_back(ToWide(l.get<std::string>()));
                        else if (l.is_object() && l.contains("text"))
                            s.lines.push_back(ToWide(l["text"].get<std::string>()));
                    }
                }
            } else if (s.kind == SlideKind::Verse) {
                if (el.contains("verses") && el["verses"].is_array()) {
                    for (auto& l : el["verses"]) s.lines.push_back(ToWide(l.get<std::string>()));
                }
                if (el.contains("reference")) s.reference = ToWide(el["reference"].get<std::string>());
            } else if (s.kind == SlideKind::Image || s.kind == SlideKind::Video) {
                if (el.contains("src")) {
                    std::wstring rel = ToWide(el["src"].get<std::string>());
                    fs::path p(rel);
                    s.media.src = p.is_absolute() ? rel : (fs::path(baseDir_) / rel).wstring();
                }
                if (el.contains("loop")) s.media.loop = el["loop"].get<bool>();
                if (el.contains("volume")) s.media.volume = el["volume"].get<int>();
                if (el.contains("startAt")) s.media.startAt = el["startAt"].get<double>();
            }

            // Fondo con ruta relativa resuelta contra la carpeta del proyecto
            std::wstring bgImg;
            if (finalBg.contains("image") && finalBg["image"].is_string()) {
                std::wstring rel = ToWide(finalBg["image"].get<std::string>());
                fs::path p(rel);
                bgImg = p.is_absolute() ? rel : (fs::path(baseDir_) / rel).wstring();
            }
            SlideState::ParseBg(finalBg, s.bg);
            if (!bgImg.empty()) s.bg.image = bgImg;

            item.slides.push_back(s);
        }
        items_.push_back(std::move(item));
    }

    Logger::Info("core.session", "sesion nativa cargada: " + std::to_string(items_.size()) + " escenarios");
    return !items_.empty();
}

} // namespace fusion
