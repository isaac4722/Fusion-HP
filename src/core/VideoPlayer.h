// ============================================================================
//  Fusion-HP · VideoPlayer — reproducción DirectShow [SPEC §6.4, §5.2 #4]
//  VMR9 windowless sobre el HWND hijo de la salida. Volumen inicial, punto de
//  inicio y bucle definidos por el Elemento. Fail-safe: si el video falla, la
//  salida muestra el fondo del tema con aviso al operador [SPEC §6.5.4].
// ============================================================================
#pragma once
#include "Common.h"

#include <dshow.h>
#include <d3d9.h>
#include <vmr9.h>
#pragma comment(lib, "strmiids.lib")
#pragma comment(lib, "quartz.lib")

namespace fusion {

class VideoPlayer {
public:
    VideoPlayer();
    ~VideoPlayer();

    // Inicia la reproducción de src en hwndVideo. Devuelve false si falla (fail-safe).
    bool Play(const std::wstring& src, HWND hwndVideo, bool loop, int volume, double startAt);
    void Stop();
    void Pause();
    void Resume();
    bool IsPlaying() const { return playing_; }
    void SetVolume(int v);            // 0-100
    void PumpEvents();                // procesa EC_COMPLETE para loop/fin

private:
    IGraphBuilder* graph_ = nullptr;
    IMediaControl* control_ = nullptr;
    IMediaEventEx* events_ = nullptr;
    IMediaSeeking* seeking_ = nullptr;
    IBasicAudio* audio_ = nullptr;
    IBaseFilter* vmr_ = nullptr;
    std::wstring src_;
    bool loop_ = false;
    bool playing_ = false;
    void ReleaseAll();
};

} // namespace fusion
