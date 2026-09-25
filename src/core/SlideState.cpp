// ============================================================================
//  Fusion-HP · SlideState.cpp — parseo del contrato de render desde ipc.v1
// ============================================================================
#include "SlideState.h"

namespace fusion {

SlideKind SlideState::KindFromString(const std::string& k) {
    if (k == "text") return SlideKind::Text;
    if (k == "image") return SlideKind::Image;
    if (k == "video") return SlideKind::Video;
    if (k == "verse") return SlideKind::Verse;
    if (k == "lower3") return SlideKind::Lower3;
    return SlideKind::None;
}

void SlideState::ParseStyle(const Json& j, TextStyle& st) {
    if (!j.is_object()) return;
    if (j.contains("font")) st.font = ToWide(j["font"].get<std::string>());
    if (j.contains("size")) st.size = j["size"].get<double>();
    if (j.contains("bold")) st.bold = j["bold"].get<bool>();
    if (j.contains("italic")) st.italic = j["italic"].get<bool>();
    if (j.contains("color")) st.color = ParseColor(j["color"].get<std::string>(), st.color);
    if (j.contains("activeColor")) st.activeColor = ParseColor(j["activeColor"].get<std::string>(), st.activeColor);
    if (j.contains("align")) st.align = j["align"].get<int>();
    if (j.contains("vAlign")) st.vAlign = j["vAlign"].get<int>();
    if (j.contains("shadow")) st.shadow = j["shadow"].get<bool>();
    if (j.contains("outline")) st.outline = j["outline"].get<bool>();
    if (j.contains("lineSpacing")) st.lineSpacing = j["lineSpacing"].get<double>();
    if (j.contains("box") && j["box"].is_object()) {
        const Json& b = j["box"];
        if (b.contains("x")) st.x = b["x"].get<double>();
        if (b.contains("y")) st.y = b["y"].get<double>();
        if (b.contains("w")) st.w = b["w"].get<double>();
        if (b.contains("h")) st.h = b["h"].get<double>();
    }
}

void SlideState::ParseBg(const Json& j, BackgroundStyle& bg) {
    if (!j.is_object()) return;
    if (j.contains("color")) bg.color = ParseColor(j["color"].get<std::string>(), bg.color);
    if (j.contains("image")) bg.image = ToWide(j["image"].get<std::string>());
    if (j.contains("fit")) bg.fit = j["fit"].get<int>();
    if (j.contains("opacity")) bg.opacity = j["opacity"].get<double>();
}

bool SlideState::ParseSlide(const Json& j, Slide& out) {
    if (!j.is_object()) return false;
    Slide s;
    if (j.contains("id")) s.id = j["id"].get<std::string>();
    s.kind = SlideKind::None;
    if (j.contains("kind")) s.kind = KindFromString(j["kind"].get<std::string>());
    if (j.contains("lines") && j["lines"].is_array()) {
        for (auto& l : j["lines"]) s.lines.push_back(ToWide(l.get<std::string>()));
    }
    if (j.contains("activeLine")) s.activeLine = j["activeLine"].get<int>();
    if (j.contains("reference")) s.reference = ToWide(j["reference"].get<std::string>());
    if (j.contains("transition")) s.transition = j["transition"].get<std::string>();
    if (j.contains("highlight") && j["highlight"].is_array())
        for (auto& h : j["highlight"]) s.highlight.push_back(ToWide(h.get<std::string>()));
    if (j.contains("style")) ParseStyle(j["style"], s.style);
    if (j.contains("bg")) ParseBg(j["bg"], s.bg);
    if (j.contains("media") && j["media"].is_object()) {
        const Json& m = j["media"];
        if (m.contains("src")) s.media.src = ToWide(m["src"].get<std::string>());
        if (m.contains("loop")) s.media.loop = m["loop"].get<bool>();
        if (m.contains("volume")) s.media.volume = m["volume"].get<int>();
        if (m.contains("startAt")) s.media.startAt = m["startAt"].get<double>();
    }
    if (j.contains("overlay") && j["overlay"].is_object()) {
        const Json& o = j["overlay"];
        s.overlay.present = o.value("present", false);
        if (o.contains("lines") && o["lines"].is_array())
            for (auto& l : o["lines"]) s.overlay.lines.push_back(ToWide(l.get<std::string>()));
        if (o.contains("style")) ParseStyle(o["style"], s.overlay.style);
        if (o.contains("position")) s.overlay.position = o["position"].get<int>();
        if (o.contains("duration")) s.overlay.duration = o["duration"].get<double>();
    }
    out = s;
    return true;
}

} // namespace fusion
