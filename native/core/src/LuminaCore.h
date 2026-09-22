// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  LuminaCore.h : motor del núcleo híbrido (impl. en Engine.cpp / Windows en
//  Projector.cpp+Renderer.cpp). Estado + hilo de eventos + proyección.
// ============================================================================
#ifndef LUMINA_CORE_H
#define LUMINA_CORE_H

#include <string>
#include <vector>
#include <memory>
#include <mutex>
#include <deque>
#include <atomic>
#include <thread>
#include <condition_variable>

#include "lumina/lumina.h"
#include "Models.h"

namespace lumina {

struct Event {
    int32_t code = 0;
    std::string payload;          // UTF-8 o binario seguro (PREVIEW: bytes PNG)
};

#ifdef LUMINA_HAS_WIN32
class Projector;   // Windows-only (Projector.cpp)
#endif
class Database;    // Storage.cpp

class Engine {
public:
    explicit Engine(const LuminaConfig& cfg);
    ~Engine();

    /* ------------ API invocada desde lumina_api.cpp (thread-safe) ------- */
    std::string Version() const;

    LuminaStatus LoadScenario(const std::string& json);
    LuminaStatus ShowSlide(int index);
    LuminaStatus Next();
    LuminaStatus Prev();
    LuminaStatus Black(bool on);
    LuminaStatus Clear();
    LuminaStatus SetTheme(const std::string& json);
    std::string  StateJson() const;
    LuminaStatus Ping(const std::string& msg);

    LuminaStatus ProjectorShow(int screenIndex, bool fullscreen);
    LuminaStatus ProjectorHide();
    int  RenderPreviewPng(int slideIndex, std::string* pngOut);   // -1=err

    LuminaStatus DbOpen(const std::string& pathUtf8);
    LuminaStatus DbClose();
    LuminaStatus DbExec(const std::string& sqlJson, std::string* outJson);

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

    LuminaConfig   cfg_;
#ifdef LUMINA_HAS_WIN32
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

} // namespace lumina
#endif // LUMINA_CORE_H
