// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  VideoDS.cpp : implementación DirectShow (F3.01/F3.02). Incluido por
//  Projector.cpp — CMake intacto (restricción del plan).
//
//  DECISIONES:
//   * VMR9 en modo WINDOWLESS enlazado al HWND del proyector: el video se
//     compone sobre la MISMA ventana de proyección (cero parpadeo: no se
//     crean ventanas hijas que flasheen).
//   * Carga DIFERIDA: solo la slide ACTIVA construye el grafo (F3.07:
//     no se cargan todos los videos del proyecto).
//   * Sin descodificación por bloques manual: DirectShow transmite por
//     bloques internamente (el grafo solo mantiene sus búferes de muestra);
//     la memoria queda acotada por el propio VMR9 (F6.03.2).
//   * Traducción de errores a lenguaje humano (F3.02.5-6): HRESULT →
//     mensaje accionable, SIN exponer el código crudo al operador.
// ============================================================================
#include "VideoDS.h"

#ifdef LUMINA_HAS_WIN32

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <objbase.h>
#include <dshow.h>
#include <d3d9.h>      // vmr9.h usa IDirect3DSurface9 (requiere d3d9 ANTES)
#include <vmr9.h>

#include <mutex>

#pragma comment(lib, "strmiids.lib")   // CLSIDs/IIDs de DirectShow (fuente: pragma, CMake intacto)

namespace lumina {

namespace {

// HRESULT → mensaje humano accionable (F3.02). El código técnico va al LOG,
// no al operador (política §11: cero stack crudo como único mensaje).
std::string HresultToHuman(HRESULT hr) {
    switch (hr) {
        case VFW_E_CANNOT_LOAD_SOURCE_FILTER:
        case VFW_E_NOT_FOUND:
            return "No se encontró el archivo de video (¿se movió o borró la carpeta?).";
        case VFW_E_UNSUPPORTED_STREAM:
            return "El formato del video no es compatible con los descodificadores instalados.";
        default:
            break;
    }
    // genérico por clases
    if (hr == (HRESULT)0x80070002L) return "No se encontró el archivo de video (ruta inexistente).";
    if (hr == (HRESULT)0x8007000EL) return "No hay memoria suficiente para reproducir este video.";
    if (hr == (HRESULT)0x80040266L) return "El formato del video no es compatible con los "
                                          "descodificadores instalados.";
    if (hr == (HRESULT)0x80040217L) return "No hay descodificador disponible para este video "
                                          "(instale los códecs del sistema).";
    char buf[128];
    _snprintf_s(buf, sizeof(buf), _TRUNCATE,
                "El video no pudo iniciarse (código interno 0x%08X).", (unsigned)hr);
    return buf;
}

} // namespace

bool VideoPlayerDS::Start(void* hwndVoid, const Options& o) {
    Stop();
    if (o.path.empty()) {
        lastError_ = "Elemento de video sin archivo asignado.";
        return false;
    }
    // Ruta UTF-8 → UTF-16 (soporte rutas con acentos).
    const int n = MultiByteToWideChar(CP_UTF8, 0, o.path.c_str(), (int)o.path.size(),
                                      nullptr, 0);
    std::wstring wpath((size_t)(n > 0 ? n : 1), L'\0');
    if (n > 0) MultiByteToWideChar(CP_UTF8, 0, o.path.c_str(), (int)o.path.size(),
                                   &wpath[0], n);
    else { lastError_ = "Ruta de video inválida."; return false; }
    hwnd_ = hwndVoid;

    // COM por grafo (CoInitialize en el hilo de ventana — ya lo hace el
    // proyector; aquí CoCreateInstance basta bajo MTA del llamador).
    HRESULT hr = S_OK;

    IGraphBuilder* graph = nullptr;
    hr = CoCreateInstance(CLSID_FilterGraph, nullptr, CLSCTX_INPROC_SERVER,
                          IID_IGraphBuilder, (void**)&graph);
    if (FAILED(hr) || !graph) {
        lastError_ = "No se pudo iniciar el motor de video del sistema.";
        return false;
    }

    // VMR9 windowless ANTES de RenderFile (así el grafo lo elige).
    IBaseFilter* vmr = nullptr;
    hr = CoCreateInstance(CLSID_VideoMixingRenderer9, nullptr, CLSCTX_INPROC_SERVER,
                          IID_IBaseFilter, (void**)&vmr);
    IVMRFilterConfig9* cfg = nullptr;
    if (SUCCEEDED(hr) && vmr) hr = vmr->QueryInterface(IID_IVMRFilterConfig9, (void**)&cfg);
    if (SUCCEEDED(hr) && cfg) hr = cfg->SetRenderingMode(VMR9Mode_Windowless);
    if (cfg) cfg->Release();
    if (SUCCEEDED(hr)) hr = graph->AddFilter(vmr, L"VMR9 (Lumina)");
    IVMRWindowlessControl9* wless = nullptr;
    if (SUCCEEDED(hr) && vmr)
        hr = vmr->QueryInterface(IID_IVMRWindowlessControl9, (void**)&wless);
    if (SUCCEEDED(hr) && wless)
        hr = wless->SetVideoClippingWindow((HWND)hwndVoid);
    if (vmr) vmr->Release();
    if (FAILED(hr)) {
        if (wless) wless->Release();
        graph->Release();
        lastError_ = "No se pudo preparar la salida de video para la proyección.";
        return false;
    }

    // Grafo automático desde el archivo (source + demux + decoders → VMR9).
    hr = graph->RenderFile(wpath.c_str(), nullptr);
    if (FAILED(hr)) {
        wless->Release();
        graph->Release();
        lastError_ = HresultToHuman(hr);
        return false;
    }

    IMediaControl* control = nullptr;
    IMediaSeeking* seek = nullptr;
    IBasicAudio*   audio = nullptr;
    graph->QueryInterface(IID_IMediaControl, (void**)&control);
    graph->QueryInterface(IID_IMediaSeeking, (void**)&seek);
    graph->QueryInterface(IID_IBasicAudio, (void**)&audio);

    // Volumen inicial (0..100 → 0..-10000 dB; 0% = silencio).
    if (audio) {
        volume_ = o.volume < 0 ? 0 : (o.volume > 100 ? 100 : o.volume);
        const long db = (long)((100.0 - volume_) * -100.0);   // 100%→0, 0%→-10000
        audio->put_Volume(volume_ <= 0 ? -10000 : db);
    }
    // startAt (unidades de 100 ns).
    if (seek && o.startAtMs > 0) {
        const LONGLONG pos = (LONGLONG)o.startAtMs * 10000LL;
        seek->SetPositions(&pos, AM_SEEKING_AbsolutePositioning,
                           nullptr, AM_SEEKING_NoPositioning);
    }
    wantLoop_ = o.loop;

    hr = control ? control->Run() : E_FAIL;
    if (FAILED(hr)) {
        if (control) control->Release();
        if (seek) seek->Release();
        if (audio) audio->Release();
        wless->Release();
        graph->Release();
        lastError_ = HresultToHuman(hr);
        return false;
    }

    graph_   = graph;
    control_ = control;
    seek_    = seek;
    audio_   = audio;
    wless_   = wless;
    running_ = true;
    lastError_.clear();
    return true;
}

void VideoPlayerDS::Stop() {
    if (control_) {
        ((IMediaControl*)control_)->Stop();
    }
    // Liberación determinista y ORDENADA (F3.01.7): control → seeking →
    // audio → windowless → graph. El grafo libera los filtros al soltar la
    // última referencia; sin contar con el GC (F6.03.5).
    if (control_) { ((IMediaControl*)control_)->Release(); control_ = nullptr; }
    if (seek_)    { ((IMediaSeeking*)seek_)->Release();     seek_ = nullptr; }
    if (audio_)   { ((IBasicAudio*)audio_)->Release();      audio_ = nullptr; }
    if (wless_)   { ((IVMRWindowlessControl9*)wless_)->Release(); wless_ = nullptr; }
    if (graph_)   { ((IGraphBuilder*)graph_)->Release();    graph_ = nullptr; }
    running_ = false;
}

void VideoPlayerDS::Pause() {
    if (control_ && running_) ((IMediaControl*)control_)->Pause();
}

void VideoPlayerDS::Resume() {
    if (control_ && running_) ((IMediaControl*)control_)->Run();
}

void VideoPlayerDS::PollEvents() {
    if (!running_ || !graph_) return;
    // EC_COMPLETE + loop → reinicio (el plan exige loop en el NÚCLEO).
    IMediaEventEx* ev = nullptr;
    if (SUCCEEDED(((IGraphBuilder*)graph_)->QueryInterface(IID_IMediaEventEx,
                                                           (void**)&ev)) && ev) {
        LONG code = 0; LONG_PTR p1 = 0, p2 = 0;
        while (SUCCEEDED(ev->GetEvent(&code, &p1, &p2, 0))) {
            ev->FreeEventParams(code, p1, p2);
            if (code == EC_COMPLETE) {
                if (wantLoop_ && seek_) {
                    LONGLONG z = 0;
                    seek_->SetPositions(&z, AM_SEEKING_AbsolutePositioning,
                                        nullptr, AM_SEEKING_NoPositioning);
                    if (control_) control_->Run();
                }
            }
        }
        ev->Release();
    }
}

void VideoPlayerDS::OnPaint(void* hdc, int w, int h) {
    if (!wless_ || !hdc || !hwnd_) return;
    // VMR9 windowless: repinta el video en el DC de la ventana (la posición
    // nativa del video se respeta con DisplayModePosition 0,0,w,h).
    ((IVMRWindowlessControl9*)wless_)->RepaintVideo((HWND)hwnd_, (HDC)hdc);
    (void)w; (void)h;
}

int VideoPlayerDS::Volume() const { return volume_; }

bool VideoPlayerDS::SetVolume(int v01) {
    if (!audio_) return false;
    volume_ = v01 < 0 ? 0 : (v01 > 100 ? 100 : v01);
    const long db = (long)((100.0 - volume_) * -100.0);
    return SUCCEEDED(((IBasicAudio*)audio_)->put_Volume(volume_ <= 0 ? -10000 : db));
}

} // namespace lumina

#endif // LUMINA_HAS_WIN32
