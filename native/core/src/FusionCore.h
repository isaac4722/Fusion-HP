// ============================================================================
//  Fusion-HP / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  FusionCore.h : motor del núcleo híbrido (impl. en Engine.cpp / Windows en
//  Projector.cpp+Renderer.cpp). Estado + hilo de eventos + proyección.
// ============================================================================
#ifndef FUSION_CORE_H
#define FUSION_CORE_H

#include <string>
#include <vector>
#include <memory>
#include <mutex>
#include <deque>
#include <atomic>
#include <thread>
#include <condition_variable>

#include "fusion/fusion.h"
#include "Models.h"

namespace fusion {

struct Event {
    int32_t code = 0;
    std::string payload;          // UTF-8 o binario seguro (PREVIEW: bytes PNG)
};

#ifdef FUSION_HAS_WIN32
class Projector;   // Windows-only (Projector.cpp)
#endif
class Database;    // Storage.cpp

class Engine {
public:
    explicit Engine(const FusionConfig& cfg);
    ~Engine();

    /* ------------ API invocada desde fusion_api.cpp (thread-safe) ------- */
    std::string Version() const;

    FusionStatus LoadScenario(const std::string& json);
    FusionStatus ShowSlide(int index);
    FusionStatus Next();
    FusionStatus Prev();
    FusionStatus Black(bool on);
    FusionStatus Clear();
    FusionStatus SetTheme(const std::string& json);
    std::string  StateJson() const;
    FusionStatus Ping(const std::string& msg);

    FusionStatus ProjectorShow(int screenIndex, bool fullscreen);
    FusionStatus ProjectorHide();
    int  RenderPreviewPng(int slideIndex, std::string* pngOut);   // -1=err

    FusionStatus DbOpen(const std::string& pathUtf8);
    FusionStatus DbClose();
    FusionStatus DbExec(const std::string& sqlJson, std::string* outJson);

    /* ------------ utilidades para módulos ------------------------------ */
    void PushEvent(int32_t code, const std::string& payload);
    // Aplana todo el escenario a una lista de slides "físicas"
    void Flatten(std::vector<Slide>* out, std::vector<std::string>* itemTitles);
    const Theme& theme() const { return theme_; }

    // Establece la lista de slides planas (llamado por LoadScenario/scripture)
    void ReplaceFlatSlides(std::vector<Slide> slides, std::vector<std::string> titles);

private:
    void EventLoop();
    void PostStateEvent();

    FusionConfig   cfg_;
#ifdef FUSION_HAS_WIN32
    std::unique_ptr<Projector> projector_;   // no-headless
#endif
    std::unique_ptr<Database>  db_;

    mutable std::mutex mx_;
    Scenario   scenario_;
    Theme      theme_;
    std::vector<Slide>        flat_;         // slides físicas actuales
    std::vector<std::string>  flatTitles_;   // título de ítem por slide
    int   current_ = -1;                     // índice de slide en vivo
    bool  black_   = false;
    bool  cleared_ = true;

    /* hilo de eventos */
    std::thread            eventThread_;
    std::mutex             evMx_;
    std::condition_variable evCv_;
    std::deque<Event>      evQueue_;
    std::atomic<bool>      stop_{false};
    bool started_ = false;
};

} // namespace fusion
#endif // FUSION_CORE_H
