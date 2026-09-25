// ============================================================================
//  Fusion-HP · VideoPlayer.cpp — grafo DirectShow con VMR9 windowless
// ============================================================================
#include "VideoPlayer.h"
#include "Logger.h"

namespace fusion {

VideoPlayer::VideoPlayer() {}
VideoPlayer::~VideoPlayer() { Stop(); ReleaseAll(); }

void VideoPlayer::ReleaseAll() {
    if (audio_) { audio_->Release(); audio_ = nullptr; }
    if (seeking_) { seeking_->Release(); seeking_ = nullptr; }
    if (events_) { events_->Release(); events_ = nullptr; }
    if (control_) { control_->Release(); control_ = nullptr; }
    if (vmr_) { vmr_->Release(); vmr_ = nullptr; }
    if (graph_) { graph_->Release(); graph_ = nullptr; }
}

bool VideoPlayer::Play(const std::wstring& src, HWND hwndVideo, bool loop, int volume, double startAt) {
    Stop();
    if (!hwndVideo) return false;

    HRESULT hr = CoCreateInstance(CLSID_FilterGraph, nullptr, CLSCTX_INPROC_SERVER,
                                  __uuidof(IGraphBuilder), (void**)&graph_);
    if (FAILED(hr) || !graph_) return false;

    // VMR9 windowless: render directo al hijo, sin ventana propia
    hr = CoCreateInstance(CLSID_VideoMixingRenderer9, nullptr, CLSCTX_INPROC_SERVER,
                          __uuidof(IBaseFilter), (void**)&vmr_);
    if (SUCCEEDED(hr) && vmr_) {
        graph_->AddFilter(vmr_, L"VMR9");
        IVMRFilterConfig9* cfg = nullptr;
        if (SUCCEEDED(vmr_->QueryInterface(__uuidof(IVMRFilterConfig9), (void**)&cfg))) {
            cfg->SetRenderingMode(VMR9Mode_Windowless);
            cfg->Release();
        }
        IVMRWindowlessControl9* wc9 = nullptr;
        if (SUCCEEDED(vmr_->QueryInterface(__uuidof(IVMRWindowlessControl9), (void**)&wc9))) {
            RECT r; GetClientRect(hwndVideo, &r);
            wc9->SetVideoClippingWindow(hwndVideo);
            wc9->SetVideoPosition(nullptr, &r);
            wc9->Release();
        }
    }

    hr = graph_->RenderFile(src.c_str(), nullptr);
    if (FAILED(hr)) {
        Logger::Error("core.media", "no se pudo cargar el video: " + ToUtf8(src));
        ReleaseAll();
        return false;                          // fail-safe: el llamador muestra fondo+aviso
    }

    graph_->QueryInterface(__uuidof(IMediaControl), (void**)&control_);
    graph_->QueryInterface(__uuidof(IMediaEventEx), (void**)&events_);
    graph_->QueryInterface(__uuidof(IMediaSeeking), (void**)&seeking_);
    graph_->QueryInterface(__uuidof(IBasicAudio), (void**)&audio_);

    if (audio_) {
        // volumen DirectShow: 0 (fuerte) a -10000 (mudo); 0-100% del elemento
        long db = (long)(-10000.0 * (1.0 - (std::clamp)(volume, 0, 100) / 100.0));
        audio_->put_Volume(db);
        audio_->put_Balance(0);
    }
    if (startAt > 0.0 && seeking_) {
        LONGLONG pos = (LONGLONG)(startAt * 10000000.0);   // 100 ns
        seeking_->SetPositions(&pos, AM_SEEKING_AbsolutePositioning, nullptr, 0);
    }

    src_ = src;
    loop_ = loop;
    if (events_) events_->SetNotifyWindow((OAHWND)hwndVideo, 0, 0); // sin mensajes: poleo manual

    hr = control_->Run();
    if (FAILED(hr)) {
        Logger::Error("core.media", "Run() fallo para el video: " + ToUtf8(src));
        ReleaseAll();
        return false;
    }
    playing_ = true;
    Logger::Info("core.media", "video en reproduccion (loop=" + std::string(loop ? "si" : "no") +
                 ", vol=" + std::to_string(volume) + ", start=" + std::to_string(startAt) + ")");
    return true;
}

void VideoPlayer::PumpEvents() {
    if (!events_ || !playing_) return;
    long ev = 0;
    LONG_PTR p1 = 0, p2 = 0;
    while (events_->GetEvent(&ev, &p1, &p2, 0) == S_OK) {
        events_->FreeEventParams(ev, p1, p2);
        if (ev == EC_COMPLETE) {
            if (loop_) {
                if (seeking_) {
                    LONGLONG z = 0;
                    seeking_->SetPositions(&z, AM_SEEKING_AbsolutePositioning, nullptr, 0);
                    if (control_) control_->Run();
                }
            } else {
                Stop();
            }
        }
    }
}

void VideoPlayer::Pause()   { if (control_ && playing_) control_->Pause(); }
void VideoPlayer::Resume()  { if (control_ && playing_) control_->Run(); }
void VideoPlayer::SetVolume(int v) {
    if (audio_) audio_->put_Volume((long)(-10000.0 * (1.0 - (std::clamp)(v, 0, 100) / 100.0)));
}

void VideoPlayer::Stop() {
    if (control_) control_->Stop();
    playing_ = false;
    ReleaseAll();
    src_.clear();
}

} // namespace fusion
