// ============================================================================
//  Fusion-HP · Motor.cpp — implementación del Motor del programa (v2.2)
// ============================================================================
#include "Motor.h"
#include "Logger.h"

#include <fstream>

namespace fusion {

// ---------------------------------------------------------------- helpers
static int LineCountOf(const Json& slide) {
    if (slide.contains("lines") && slide["lines"].is_array())
        return (int)slide["lines"].size();
    return 0;
}

static std::string BlankToggle(const std::string& current, const std::string& mode) {
    return current == mode ? std::string("none") : mode;
}

// ---------------------------------------------------------------- carga
bool Motor::LoadProgram(const Json& payload, std::string* err) {
    if (!payload.contains("program") || !payload["program"].is_array()) {
        if (err) *err = "motor.load requiere payload.program";
        return false;
    }
    std::vector<MotorScenario> parsed;
    for (const auto& sj : payload["program"]) {
        MotorScenario scn;
        scn.id = sj.value("id", std::string());
        scn.title = sj.value("title", std::string("Escenario"));
        if (sj.contains("elements") && sj["elements"].is_array()) {
            for (const auto& ej : sj["elements"]) {
                MotorElement el;
                el.id = ej.value("id", std::string());
                el.title = ej.value("title", std::string());
                el.kindHint = ej.value("kind", std::string("text"));
                if (!ej.contains("slide")) continue;   // sin diapositiva no hay elemento
                el.slide = ej["slide"];
                el.lineCount = LineCountOf(el.slide);
                scn.elements.push_back(std::move(el));
            }
        }
        parsed.push_back(std::move(scn));
    }

    std::string advance = payload.value("advance", advance_);
    std::vector<std::string> highlight;
    if (payload.contains("highlight") && payload["highlight"].is_array())
        for (const auto& w : payload["highlight"])
            if (w.is_string()) highlight.push_back(w.get<std::string>());

    int selScn = -1, selEl = -1, selLine = 0;
    if (payload.contains("select") && payload["select"].is_object()) {
        selScn = payload["select"].value("scenario", -1);
        selEl = payload["select"].value("element", -1);
        selLine = payload["select"].value("line", 0);
    }

    Json slideCopy;           // elemento a mostrar (fuera del lock)
    std::string prevId, curId;
    int selLineOut = 0;
    bool show = false;
    {
        std::lock_guard<std::mutex> lk(m_);
        scenarios_ = std::move(parsed);
        advance_ = advance;
        highlight_ = highlight;
        scnIdx_ = elIdx_ = -1;
        lineIdx_ = 0;
        prevId = lastShownId_;
        if (selScn >= 0 && selScn < (int)scenarios_.size()) {
            scnIdx_ = selScn;
            if (selEl >= 0 && selEl < (int)scenarios_[selScn].elements.size()) {
                elIdx_ = selEl;
                int lc = scenarios_[selScn].elements[selEl].lineCount;
                lineIdx_ = (selLine > 0 && selLine < lc) ? selLine : 0;
                slideCopy = scenarios_[selScn].elements[selEl].slide;
                selLineOut = lineIdx_;
                show = true;
                curId = scenarios_[selScn].elements[selEl].id;
                lastShownId_ = curId;
            }
        }
    }
    SavePersisted(persistFile_);

    // Mostrar la selección SIN transición si es el mismo elemento (hot-reload
    // de tema/configuración): evita un fundido espurio sobre contenido igual.
    if (show) {
        Json withHl = SlideWithHighlight(slideCopy);
        withHl["activeLine"] = selLineOut;
        if (curId == prevId) withHl["transition"] = "cut";
        if (ApplySlideJson) ApplySlideJson(withHl);
        // precarga del siguiente [SPEC §6.4]
        Json next;
        {
            std::lock_guard<std::mutex> lk(m_);
            int n = elIdx_ + 1;
            if (scnIdx_ >= 0 && scnIdx_ < (int)scenarios_.size()) {
                if (n < (int)scenarios_[scnIdx_].elements.size())
                    next = scenarios_[scnIdx_].elements[n].slide;
                else if (scnIdx_ + 1 < (int)scenarios_.size() &&
                         !scenarios_[scnIdx_ + 1].elements.empty())
                    next = scenarios_[scnIdx_ + 1].elements[0].slide;
            }
        }
        if (!next.is_null() && PreloadSlide) PreloadSlide(next);
    }
    if (Broadcast) Broadcast();
    return true;
}

bool Motor::AppendScenario(const Json& payload, std::string* err) {
    if (!payload.contains("scenario")) {
        if (err) *err = "motor.append requiere payload.scenario";
        return false;
    }
    const Json& sj = payload["scenario"];
    MotorScenario scn;
    scn.id = sj.value("id", std::string());
    scn.title = sj.value("title", std::string("Escenario"));
    if (sj.contains("elements") && sj["elements"].is_array()) {
        for (const auto& ej : sj["elements"]) {
            if (!ej.contains("slide")) continue;
            MotorElement el;
            el.id = ej.value("id", std::string());
            el.title = ej.value("title", std::string());
            el.kindHint = ej.value("kind", std::string("text"));
            el.slide = ej["slide"];
            el.lineCount = LineCountOf(el.slide);
            scn.elements.push_back(std::move(el));
        }
    }
    int scnIdx, elIdx;
    {
        std::lock_guard<std::mutex> lk(m_);
        scenarios_.push_back(std::move(scn));
        scnIdx = (int)scenarios_.size() - 1;
        elIdx = (int)scenarios_[scnIdx].elements.size() > 0 ? 0 : -1;
    }
    SavePersisted(persistFile_);
    if (elIdx >= 0) Goto(scnIdx, elIdx, 0);
    if (Broadcast) Broadcast();
    return true;
}

void Motor::Clear() {
    {
        std::lock_guard<std::mutex> lk(m_);
        scenarios_.clear();
        scnIdx_ = elIdx_ = -1;
        lineIdx_ = 0;
        highlight_.clear();
        lastShownId_.clear();
    }
    SavePersisted(persistFile_);
    if (Broadcast) Broadcast();
}

// ---------------------------------------------------------------- persistencia
void Motor::SavePersisted(const std::wstring& file) {
    if (file.empty()) return;
    Json out;
    {
        std::lock_guard<std::mutex> lk(m_);
        out["v"] = 1;
        out["advance"] = advance_;
        out["blank"] = blank_;
        Json sel = Json::object();
        sel["scenario"] = scnIdx_;
        sel["element"] = elIdx_;
        sel["line"] = lineIdx_;
        out["select"] = sel;
        Json hl = Json::array();
        for (const auto& w : highlight_) hl.push_back(w);
        out["highlight"] = hl;
        Json prog = Json::array();
        for (const auto& s : scenarios_) {
            Json sj = Json::object();
            sj["id"] = s.id;
            sj["title"] = s.title;
            Json els = Json::array();
            for (const auto& e : s.elements) {
                Json ej = Json::object();
                ej["id"] = e.id;
                ej["title"] = e.title;
                ej["kind"] = e.kindHint;
                ej["slide"] = e.slide;
                els.push_back(ej);
            }
            sj["elements"] = els;
            prog.push_back(sj);
        }
        out["program"] = prog;
    }
    // Escritura atómica: tmp + reemplazo (como el cancionero de la GUI).
    std::wstring tmp = file + L".tmp";
    try {
        std::ofstream f(tmp, std::ios::binary | std::ios::trunc);
        f << out.dump();
        if (!f.good()) return;
        f.close();
        MoveFileExW(tmp.c_str(), file.c_str(), MOVEFILE_REPLACE_EXISTING);
    } catch (...) {
        // sin persistir no se rompe la proyección
    }
}

bool Motor::LoadPersisted(const std::wstring& file) {
    if (file.empty()) return false;
    std::ifstream f(file, std::ios::binary);
    if (!f.good()) return false;
    try {
        std::string buf((std::istreambuf_iterator<char>(f)), std::istreambuf_iterator<char>());
        Json j = Json::parse(buf);
        Json payload = Json::object();
        payload["program"] = j["program"];
        if (j.contains("select")) payload["select"] = j["select"];
        if (j.contains("advance")) payload["advance"] = j["advance"];
        if (j.contains("highlight")) payload["highlight"] = j["highlight"];
        std::string err;
        if (!LoadProgram(payload, &err)) return false;
        std::lock_guard<std::mutex> lk(m_);
        if (j.contains("blank")) blank_ = j["blank"].get<std::string>();
        return true;
    } catch (...) {
        return false;
    }
}

// ---------------------------------------------------------------- navegación
void Motor::Next() {
    bool byElement;
    int line, lineCount;
    {
        std::lock_guard<std::mutex> lk(m_);
        byElement = advance_ == "slide";
        line = lineIdx_;
        lineCount = ValidSelectionLocked()
                        ? scenarios_[scnIdx_].elements[elIdx_].lineCount : 0;
    }
    if (byElement || lineCount == 0) { NextElement(); return; }
    if (line + 1 < lineCount) SetLine(line + 1);
    else NextElement();
}

void Motor::Prev() {
    bool byElement;
    int line;
    {
        std::lock_guard<std::mutex> lk(m_);
        byElement = advance_ == "slide";
        line = lineIdx_;
    }
    if (byElement) { PrevElement(); return; }
    if (line > 0) SetLine(line - 1);
    else PrevElement();
}

void Motor::NextElement() {
    int scn, el, scnCount, elCount;
    {
        std::lock_guard<std::mutex> lk(m_);
        scn = scnIdx_; el = elIdx_;
        scnCount = (int)scenarios_.size();
        elCount = (scn >= 0 && scn < scnCount) ? (int)scenarios_[scn].elements.size() : 0;
    }
    if (scnCount == 0) return;
    if (scn < 0) { Goto(0, 0, 0); return; }
    if (el + 1 < elCount) { Goto(scn, el + 1, 0); return; }
    if (scn + 1 < scnCount) { Goto(scn + 1, 0, 0); return; }
    // al final del programa: se queda (sin envoltura sorpresiva)
}

void Motor::PrevElement() {
    int scn, el;
    {
        std::lock_guard<std::mutex> lk(m_);
        scn = scnIdx_; el = elIdx_;
    }
    if (scn < 0) return;
    if (el > 0) { Goto(scn, el - 1, 0); return; }
    if (scn > 0) {
        int last;
        {
            std::lock_guard<std::mutex> lk(m_);
            last = (int)scenarios_[scn - 1].elements.size() - 1;
        }
        Goto(scn - 1, last, 0);
    }
}

bool Motor::Goto(int scnIdx, int elIdx, int lineIdx) {
    Json slideCopy;
    std::string prevId, newId;
    int lineOut = 0;
    {
        std::lock_guard<std::mutex> lk(m_);
        if (scnIdx < 0 || scnIdx >= (int)scenarios_.size()) return false;
        if (elIdx < 0 || elIdx >= (int)scenarios_[scnIdx].elements.size()) return false;
        int lc = scenarios_[scnIdx].elements[elIdx].lineCount;
        scnIdx_ = scnIdx;
        elIdx_ = elIdx;
        lineIdx_ = (lineIdx > 0 && lineIdx < lc) ? lineIdx : 0;
        lineOut = lineIdx_;
        slideCopy = scenarios_[scnIdx].elements[elIdx].slide;
        prevId = lastShownId_;
        lastShownId_ = newId = scenarios_[scnIdx].elements[elIdx].id;
    }
    Json withHl = SlideWithHighlight(slideCopy);
    withHl["activeLine"] = lineOut;
    if (newId == prevId) withHl["transition"] = "cut";
    if (ApplySlideJson) ApplySlideJson(withHl);

    Json next;
    {
        std::lock_guard<std::mutex> lk(m_);
        int n = elIdx_ + 1;
        if (scnIdx_ >= 0 && scnIdx_ < (int)scenarios_.size()) {
            if (n < (int)scenarios_[scnIdx_].elements.size())
                next = scenarios_[scnIdx_].elements[n].slide;
            else if (scnIdx_ + 1 < (int)scenarios_.size() &&
                     !scenarios_[scnIdx_ + 1].elements.empty())
                next = scenarios_[scnIdx_ + 1].elements[0].slide;
        }
    }
    if (!next.is_null() && PreloadSlide) PreloadSlide(next);
    if (Broadcast) Broadcast();
    return true;
}

bool Motor::SetLine(int line) {
    {
        std::lock_guard<std::mutex> lk(m_);
        if (!ValidSelectionLocked()) return false;
        int lc = scenarios_[scnIdx_].elements[elIdx_].lineCount;
        if (line < 0 || line >= lc) return false;
        lineIdx_ = line;
    }
    if (Broadcast) Broadcast();
    return true;
}

// ---------------------------------------------------------------- pantalla
void Motor::SetBlank(const std::string& mode) {
    {
        std::lock_guard<std::mutex> lk(m_);
        blank_ = mode;
    }
    if (ApplyBlank) ApplyBlank(mode);
    if (Broadcast) Broadcast();
}

void Motor::SetAdvance(const std::string& mode) {
    {
        std::lock_guard<std::mutex> lk(m_);
        advance_ = mode == "slide" ? "slide" : "line";
    }
    SavePersisted(persistFile_);
    if (Broadcast) Broadcast();
}

void Motor::Highlight(const std::vector<std::string>& words) {
    Json slideCopy;
    bool hasCurrent = false;
    int lineOut = 0;
    {
        std::lock_guard<std::mutex> lk(m_);
        highlight_ = words;
        if (ValidSelectionLocked()) {
            slideCopy = scenarios_[scnIdx_].elements[elIdx_].slide;
            lineOut = lineIdx_;
            hasCurrent = true;
        }
    }
    SavePersisted(persistFile_);
    if (hasCurrent) {
        // mismo contenido, solo color de palabras: sin transición
        Json withHl = SlideWithHighlight(slideCopy);
        withHl["activeLine"] = lineOut;
        withHl["transition"] = "cut";
        if (ApplySlideJson) ApplySlideJson(withHl);
    }
    if (Broadcast) Broadcast();
}

// ---------------------------------------------------------------- consultas
bool Motor::HasProgram() const {
    std::lock_guard<std::mutex> lk(m_);
    return !scenarios_.empty();
}

bool Motor::ValidSelection() const {
    std::lock_guard<std::mutex> lk(m_);
    return ValidSelectionLocked();
}

bool Motor::ValidSelectionLocked() const {
    return scnIdx_ >= 0 && scnIdx_ < (int)scenarios_.size() &&
           elIdx_ >= 0 && elIdx_ < (int)scenarios_[scnIdx_].elements.size();
}

Json Motor::SlideWithHighlight(const Json& src) const {
    Json out = src;   // copia
    std::lock_guard<std::mutex> lk(m_);
    if (!highlight_.empty()) {
        Json hl = Json::array();
        for (const auto& w : highlight_) hl.push_back(w);
        out["highlight"] = hl;
    }
    return out;
}

Json Motor::StateJson() const {
    std::lock_guard<std::mutex> lk(m_);
    Json j = Json::object();
    j["hasProgram"] = !scenarios_.empty();
    j["scenario"] = scnIdx_;
    j["element"] = elIdx_;
    j["line"] = lineIdx_;
    j["lineCount"] = 0;
    j["blank"] = blank_;
    j["advance"] = advance_;
    Json hl = Json::array();
    for (const auto& w : highlight_) hl.push_back(w);
    j["highlight"] = hl;

    Json prog = Json::array();
    for (const auto& s : scenarios_) {
        Json sj = Json::object();
        sj["title"] = s.title;
        sj["count"] = s.elements.size();
        prog.push_back(sj);
    }
    j["program"] = prog;

    Json els = Json::array();
    if (ValidSelectionLocked()) {
        for (const auto& e : scenarios_[scnIdx_].elements) {
            Json ej = Json::object();
            ej["title"] = e.title;
            ej["kind"] = e.kindHint;
            els.push_back(ej);
        }
        j["lineCount"] = scenarios_[scnIdx_].elements[elIdx_].lineCount;
        j["slide"] = scenarios_[scnIdx_].elements[elIdx_].slide;
    }
    j["elements"] = els;
    return j;
}

// ---------------------------------------------------------------- teclas autónomas
void Motor::StandaloneKey(UINT vk) {
    std::string curBlank;
    {
        std::lock_guard<std::mutex> lk(m_);
        curBlank = blank_;
    }
    switch (vk) {
        case VK_SPACE:
        case VK_RIGHT:
        case VK_NEXT:
            Next(); break;
        case VK_LEFT:
        case VK_PRIOR:
            Prev(); break;
        case VK_DOWN: NextElement(); break;
        case VK_UP:   PrevElement(); break;
        case 'B': SetBlank(BlankToggle(curBlank, "black")); break;
        case 'C': SetBlank(BlankToggle(curBlank, "clear")); break;
        case 'L': SetBlank(BlankToggle(curBlank, "logo")); break;
        case VK_ESCAPE: SetBlank("black"); break;   // reposo [SPEC §6.1.3]
    }
}

} // namespace fusion
