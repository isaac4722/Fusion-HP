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

// v7.0.0 «ULTRA»: módulos incluidos por lumina_api.cpp (misma unidad de
// traducción — el CMake queda intacto). Declaraciones para uso del Engine.
namespace ipc { class Server; }
namespace nlog { class Log; }

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

    // v6.0.0 «HORIZONTE»: transición entre slides de la proyección.
    // mode: 0=corte, 1=fundido; durationMs 0..5000. En headless la
    // configuración se guarda y se refleja en el estado (la ventana la
    // aplica cuando exista). Fuera de rango → LUMINA_ERR_LIMIT.
    LuminaStatus SetTransition(int32_t mode, int32_t durationMs);

    // v7.0.0 «ULTRA» — F1.03: sincronización por línea.
    // Next()/Prev() son conscientes de línea: si la slide activa tiene
    // líneas con syncMark, avanzan POR LÍNEA primero (el fondo y el video
    // quedan intactos: solo cambia el estado de línea activa).
    LuminaStatus LineNext();
    LuminaStatus LinePrev();
    LuminaStatus LineSet(int32_t lineIndex);

    // v7.0.0 «ULTRA» — F0.05: servidor IPC ipc.v1 (pipes con nombre).
    // optsJson: {"pipe":"lumina-ipc-v1","maxPayload":N,
    //            "maxQueued":N,"maxAgeMs":N}
    LuminaStatus IpcStart(const std::string& optsJson);
    LuminaStatus IpcStop();
    std::string  IpcStatsJson() const;

    // v7.0.0 «ULTRA» — F0.09: log nativo estructurado (§10.1).
    // optsJson: {"dir":"<carpeta logs>","retentionDays":14,
    //            "level":"INFO"}
    LuminaStatus LogOpen(const std::string& optsJson);
    LuminaStatus LogWrite(const std::string& entryJson);
    std::string  LogStatsJson() const;

    // v7.0.0 «ULTRA» — F0.01/02/03: entorno + decisión de arranque
    // (JSON para Estado del sistema; probeJson opcional inyecta hechos).
    static std::string DetectEnvJson(const std::string& probeJson,
                                     bool forceX86, bool forceX64,
                                     bool forceA, bool forceB, bool forceC);

    LuminaStatus DbOpen(const std::string& pathUtf8);
    LuminaStatus DbClose();
    LuminaStatus DbExec(const std::string& sqlJson, std::string* outJson);

    /* ------------ utilidades para módulos ------------------------------ */
    void PushEvent(int32_t code, const std::string& payload);
    // Aplana todo el escenario a una lista de slides "físicas". itemIdx
    // devuelve, por slide, el índice del ScenarioItem que la originó (v5.1.0).
    void Flatten(std::vector<Slide>* out, std::vector<std::string>* itemTitles,
                 std::vector<int>* itemIdx = nullptr);
    const Theme& theme() const { return theme_; }

    // Establece la lista de slides planas (llamado por LoadScenario/scripture)
    void ReplaceFlatSlides(std::vector<Slide> slides, std::vector<std::string> titles,
                            std::vector<int> itemIdx = std::vector<int>());

private:
    void EventLoop();
    void PostStateEvent();
#ifdef LUMINA_HAS_WIN32
    static void ProjectorReport(void* user, int severity, const char* module,
                                const char* msg);
#endif

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
    std::vector<int>          flatItems_;    // índice de ítem por slide (v5.1.0)
    int   current_ = -1;                     // índice de slide en vivo
    bool  black_   = false;
    bool  cleared_ = true;
    // v6.0.0: transición de proyección (mode 0=corte, 1=fundido; ms 0..5000)
    int   transitionMode_ = 1;
    int   transitionMs_   = 220;
    // v7.0.0 «ULTRA» (F1.03): índice de línea activa dentro de la slide
    // actual (-1 = sin navegación por línea activa: slide entera).
    int   activeLine_ = -1;
    // v7.0.0 «ULTRA»: servidor IPC ipc.v1 + log estructurado.
    std::unique_ptr<ipc::Server> ipcServer_;
    std::unique_ptr<nlog::Log>   log_;

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
