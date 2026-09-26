// ============================================================================
//  Fusion-HP · VideoPlayer.cpp — grafo DirectShow con VMR9 windowless
//  v4.1.0: COM por RAII (WIL com_ptr_nothrow, WIL_EXCEPTION_MODE=1). Las
//  salidas anticipadas ya no pueden fugar interfaces: reset() implícito.
// ============================================================================
#include "VideoPlayer.h"
#include "Logger.h"

namespace fusion {

VideoPlayer::VideoPlayer() {}
VideoPlayer::~VideoPlayer() { Stop(); }

bool VideoPlayer::Play(const std::wstring& src, HWND hwndVideo, bool loop, int volume, double startAt) {
    Stop();
    if (!hwndVideo) return false;

    HRESULT hr = CoCreateInstance(CLSID_FilterGraph, nullptr, CLSCTX_INPROC_SERVER,
                                  __uuidof(IGraphBuilder), reinterpret_cast<void**>(graph_.put()));
    if (FAILED(hr) || !graph_) return false;

    // VMR9 windowless: render directo al hijo, sin ventana propia
    hr = CoCreateInstance(CLSID_VideoMixingRenderer9, nullptr, CLSCTX_INPROC_SERVER,
                          __uuidof(IBaseFilter), reinterpret_cast<void**>(vmr_.put()));
    if (SUCCEEDED(hr) && vmr_) {
        graph_->AddFilter(vmr_.get(), L"VMR9");
        auto cfg = vmr_.try_query<IVMRFilterConfig9>();
        if (cfg) cfg->SetRenderingMode(VMR9Mode_Windowless);
        auto wc9 = vmr_.try_query<IVMRWindowlessControl9>();
        if (wc9) {
            RECT r; GetClientRect(hwndVideo, &r);
            wc9->SetVideoClippingWindow(hwndVideo);
            wc9->SetVideoPosition(nullptr, &r);
        }
    }

    hr = graph_->RenderFile(src.c_str(), nullptr);
    if (FAILED(hr)) {
        Logger::Error("core.media", "no se pudo cargar el video: " + ToUtf8(src));
        Stop();                                // fail-safe: el llamador muestra fondo+aviso
        return false;
    }

    graph_.query_to(__uuidof(IMediaControl), control_.put_void());
    graph_.query_to(__uuidof(IMediaEventEx), events_.put_void());
    graph_.query_to(__uuidof(IMediaSeeking), seeking_.put_void());
    graph_.query_to(__uuidof(IBasicAudio), audio_.put_void());

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
        Stop();
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
    // WIL: mismos releases que el ReleaseAll() histórico, ahora automáticos.
    audio_.reset();
    seeking_.reset();
    events_.reset();
    control_.reset();
    vmr_.reset();
    graph_.reset();
    src_.clear();
}

} // namespace fusion
