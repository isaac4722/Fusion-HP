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
#include "NativeStudio.h"
#include "Motor.h"

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
    void ApplyBlank(const std::string& mode);       // black|logo|theme|clear|none
    void PreloadSlideJson(const Json& slide);       // carga diferida [SPEC §6.4]
    void BroadcastMotorState();                     // evento motor.state
    Json DispatchMotor(const std::string& cmd, const Json& p);   // familia motor.*
    void WatchStandalone();                        // GUI fuera => Motor autónomo
    void UpdateStage(bool force);                   // reloj/temporizador/next del escenario
    std::wstring renderer_preload_, renderer_preload2_;   // rutas precargadas [SPEC §6.4]
    std::wstring motorSesionFile_;                 // DataDir/motor/sesion.json
    Renderer::StageInfo stageInfo_;                // estado del Stage View (beta-1)
    DWORD lastStageSecond_ = 0;

    HINSTANCE inst_ = nullptr;
    EnvironmentReport env_;
    SlideState state_;
    LiveWindow live_;
    VideoPlayer video_;
    std::unique_ptr<IpcServer> ipc_;
    std::unique_ptr<NativeStudio> studio_;  // GUI web replicada en C++ (v2.3)
    std::unique_ptr<Motor> motor_;          // EL MOTOR (v2.2): dueño del estado vivo
    std::atomic<bool> studioRunning_{false};
    UINT_PTR pumpTimer_ = 0;
    DWORD bootTick_ = 0;                    // gracia antes de modo autónomo
    bool guiEverConnected_ = false;
    bool standalone_ = false;               // sin GUI: teclado en la salida
};

} // namespace fusion
