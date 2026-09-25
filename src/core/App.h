// ============================================================================
//  Fusion-HP · App — orquestación del núcleo [SPEC §4.1]
//  Secuencia fija: detectar entorno → crear salida → IPC → lanzar capa C#
//  según perfil (A/B) o UI nativa (C) → bomba de mensajes con bombeo de video.
// ============================================================================
#pragma once
#include "Common.h"
#include "Environment.h"
#include "SlideState.h"
#include "LiveWindow.h"
#include "IpcServer.h"
#include "VideoPlayer.h"
#include "NativeControl.h"

namespace fusion {

class App {
public:
    static App& Get() { static App a; return a; }

    int Run(HINSTANCE inst, int showCmd);

    // Despachador ipc.v1 — devuelve la respuesta JSON
    Json Dispatch(const std::string& cmd, const Json& payload);

private:
    int RunManaged(const std::wstring& exe);       // lanza FusionStudio(.Lite).exe
    void BroadcastState();
    void ApplySlide(const Json& slideJson);
    void ShowVideo(const Slide& s);
    std::wstring renderer_preload_, renderer_preload2_;   // rutas precargadas [SPEC §6.4]

    HINSTANCE inst_ = nullptr;
    EnvironmentReport env_;
    SlideState state_;
    LiveWindow live_;
    VideoPlayer video_;
    std::unique_ptr<IpcServer> ipc_;
    std::unique_ptr<NativeControl> nativeCtl_;
    std::atomic<bool> studioRunning_{false};
    UINT_PTR pumpTimer_ = 0;
};

} // namespace fusion
