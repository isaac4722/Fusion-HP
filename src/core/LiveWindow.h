// ============================================================================
//  Fusion-HP · LiveWindow — ventana de salida limpia [SPEC §6.1]
//  Borderless a pantalla completa sobre el monitor elegido; SIN barras, bordes
//  ni cursor visible. Ventana PERSISTENTE: nunca se destruye entre elementos
//  [SPEC §6.3.1]; el contenido se conmuta por render, no por recreación.
//  Un HWND hijo aloja el video de DirectShow (evita pelear con el render 2D).
// ============================================================================
#pragma once
#include "Common.h"
#include "Renderer.h"

namespace fusion {

class VideoPlayer;

class LiveWindow {
public:
    LiveWindow();
    ~LiveWindow();

    bool Create(int monitorIndex, const std::wstring& title);
    void ShowOnMonitor(int monitorIndex);       // reposiciona sin destruir
    void Destroy();

    HWND Hwnd() const { return hwnd_; }
    static LRESULT CALLBACK WndProc(HWND, UINT, WPARAM, LPARAM);
    HWND VideoHwnd() const { return videoHwnd_; }
    Renderer& GetRenderer() { return renderer_; }
    int MonitorIndex() const { return monitorIdx_; }

    void RenderNow();                           // dibuja el estado actual (público para eventos)
    void SetVideoChildVisible(bool v);

    // ---- modo autónomo del Motor (v2.2): sin GUI conectada ----
    // Teclas sobre la salida (las traduce el Motor). Se activa al hacer clic.
    std::function<void(UINT)> KeyHook;
    // on: la salida admite foco/teclado y muestra el aviso del Motor;
    // off: comportamiento normal (no roba foco del operador [SPEC §6.1.2]).
    void SetStandalone(bool on);

    // Estado compartido que dibuja esta ventana
    void Bind(SlideState* state) { state_ = state; }

    // Vista de retorno (Stage View) [SPEC §8.5] — beta-1 enriquecida (v2.3):
    // la info (reloj/tono/alerta/temporizador/siguiente) la posee App.
    bool CreateStage(int monitorIndex);
    HWND StageHwnd() const { return stageHwnd_; }
    void RenderStageNow();
    void SetStageInfo(const Renderer::StageInfo& si) { stageInfo_ = si; }
    Renderer::StageInfo& StageInfoRef() { return stageInfo_; }

    void SetVideoPlayer(VideoPlayer* vp) { video_ = vp; }

private:
    LRESULT Handle(HWND, UINT, WPARAM, LPARAM);
    void ShowHint(bool on);                     // aviso "MOTOR ACTIVO" (8 s)
    static LRESULT CALLBACK HintWndProc(HWND, UINT, WPARAM, LPARAM);

    HWND hwnd_ = nullptr;
    HWND videoHwnd_ = nullptr;
    HWND stageHwnd_ = nullptr;
    HWND hintHwnd_ = nullptr;
    Renderer renderer_;
    Renderer stageRenderer_;
    Renderer::StageInfo stageInfo_;
    SlideState* state_ = nullptr;
    VideoPlayer* video_ = nullptr;
    int monitorIdx_ = -1;
    std::wstring class_;
    bool standalone_ = false;
    UINT_PTR hintTimer_ = 0;
};

} // namespace fusion
