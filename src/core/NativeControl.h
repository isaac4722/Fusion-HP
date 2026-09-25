// ============================================================================
//  Fusion-HP · NativeControl — UI mínima de emergencia del perfil C [SPEC §4.2]
//  Permite abrir un Escenario empaquetado (.ahp), navegar elementos y líneas,
//  y poner la pantalla en reposo. Es la red de seguridad si no hay .NET.
// ============================================================================
#pragma once
#include "Common.h"
#include "SlideState.h"
#include "NativeSession.h"

namespace fusion {

class LiveWindow;
class VideoPlayer;

class NativeControl {
public:
    NativeControl(SlideState* state, LiveWindow* live, VideoPlayer* video);
    ~NativeControl();

    bool Create();
    void ShowSession(const NativeSession& session);

private:
    static LRESULT CALLBACK WndProc(HWND, UINT, WPARAM, LPARAM);
    LRESULT Handle(UINT, WPARAM, LPARAM);
    void BuildUi();
    void RefreshList();
    void SelectItem(int idx);
    void ShowSlideOf(int slideIdx);
    void NextLine(int dir);
    void SetBlank(BlankMode b);
    void OpenAhp();

    SlideState* state_;
    LiveWindow* live_;
    VideoPlayer* video_;
    HWND hwnd_ = nullptr;
    HWND list_ = nullptr;
    HWND label_ = nullptr;
    std::vector<NativeItem> items_;
    int curItem_ = -1;
    int curSlide_ = -1;
};

} // namespace fusion
