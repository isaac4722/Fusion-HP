// ============================================================================
//  Fusion-HP · Monitors — gestión multi-pantalla [SPEC §6.1, §8.5]
// ============================================================================
#pragma once
#include "Common.h"

namespace fusion {

struct MonitorInfo {
    int index = 0;
    RECT rect = {};
    bool primary = false;
    std::wstring name;
};

class Monitors {
public:
    static std::vector<MonitorInfo> Enumerate() {
        std::vector<MonitorInfo> out;
        struct Ctx { std::vector<MonitorInfo>* v; int i; } ctx{&out, 0};
        EnumDisplayMonitors(nullptr, nullptr, [](HMONITOR hm, HDC, LPRECT rc, LPARAM lp) -> BOOL {
            Ctx* c = (Ctx*)lp;
            MonitorInfo mi;
            mi.index = c->i++;
            mi.rect = *rc;
            MONITORINFOEXW mix = {};
            mix.cbSize = sizeof(mix);
            if (GetMonitorInfoW(hm, &mix)) {
                mi.primary = (mix.dwFlags & MONITORINFOF_PRIMARY) != 0;
                mi.name = mix.szDevice;
            }
            c->v->push_back(mi);
            return TRUE;
        }, (LPARAM)&ctx);
        // primario primero (estable para selección por defecto)
        std::sort(out.begin(), out.end(), [](const MonitorInfo& a, const MonitorInfo& b) {
            if (a.primary != b.primary) return a.primary;
            return a.index < b.index;
        });
        for (int i = 0; i < (int)out.size(); i++) out[(size_t)i].index = i;
        return out;
    }

    // Índice del monitor "de salida": el primero no primario; si solo hay uno, el primario.
    static int DefaultOutputIndex() {
        auto list = Enumerate();
        for (auto& m : list) if (!m.primary) return m.index;
        return list.empty() ? 0 : list[0].index;
    }

    static int Count() { return (int)Enumerate().size(); }

    static bool RectOf(int index, RECT* out) {
        auto list = Enumerate();
        for (auto& m : list) if (m.index == index) { *out = m.rect; return true; }
        return false;
    }

    static Json ToJsonArray() {
        Json arr = Json::array();
        for (auto& m : Enumerate()) {
            Json j = Json::object();
            j["index"] = m.index;
            j["x"] = (long long)m.rect.left;
            j["y"] = (long long)m.rect.top;
            j["w"] = (long long)(m.rect.right - m.rect.left);
            j["h"] = (long long)(m.rect.bottom - m.rect.top);
            j["primary"] = m.primary;
            j["name"] = ToUtf8(m.name);
            arr.push_back(j);
        }
        return arr;
    }
};

} // namespace fusion
