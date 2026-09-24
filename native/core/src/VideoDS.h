// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  VideoDS.h : reproducción de video DirectShow desde el NÚCLEO (F3.01).
//
//  Grafo: source → … → VMR9 WINDOWLESS enlazado al HWND del proyector.
//   * volumen inicial (IBasicAudio, 0..-10000 dB),
//   * startAt (IMediaSeeking, unidades 100 ns),
//   * loop (EC_COMPLETE → seek 0 + Run),
//   * liberación DETERMINISTA (Stop() libera todos los filtros y COM),
//   * fail-safe (F3.02): Start() falla → LastError() humano + el proyector
//     SIGUE pintando el fondo del tema (nunca negro sin aviso).
//  Compilado solo en Windows, incluido por Projector.cpp (unidad única:
//  el CMake del núcleo no se toca).
// ============================================================================
#ifndef LUMINA_VIDEODS_H
#define LUMINA_VIDEODS_H

#ifdef LUMINA_HAS_WIN32

#include <string>
#include <cstdint>

namespace lumina {

class VideoPlayerDS {
public:
    struct Options {
        std::string path;         // UTF-8
        int    volume     = 100;  // 0..100
        int64_t startAtMs = 0;
        bool   loop       = false;
    };

    VideoPlayerDS() {}
    ~VideoPlayerDS() { Stop(); }

    // Construye el grafo y arranca la reproducción sobre hwnd.
    // false → fail-safe: consultar LastError() (mensaje humano, sin HRESULT
    // crudo — política §11 del plan).
    bool Start(void* hwnd, const Options& o);

    // Liberación determinista de todos los recursos (F3.01.7).
    void Stop();

    bool Playing() const { return running_; }

    // Mantener posición (F3.01.6): pausa SIN liberar el grafo.
    void Pause();
    void Resume();

    // Loop: sondea EC_COMPLETE (lo llama el bucle de ventana ~60 fps).
    void PollEvents();

    // Repintado dirigido por el VMR9 (WM_PAINT del proyector).
    void OnPaint(void* hdc, int w, int h);

    // Último error humano ("" si ninguno). El proyector lo reporta al
    // operador por el canal de eventos (aviso en monitor del operador).
    std::string LastError() const { return lastError_; }

    int Volume() const;
    bool SetVolume(int v01);

private:
    void* graph_ = nullptr;          // IGraphBuilder* (opaco: sin dshow.h aquí)
    void* control_ = nullptr;        // IMediaControl*
    void* seek_ = nullptr;           // IMediaSeeking*
    void* audio_ = nullptr;          // IBasicAudio*
    void* wless_ = nullptr;          // IVMRWindowlessControl9*
    void* hwnd_ = nullptr;           // HWND del proyector (RepaintVideo)
    bool  running_ = false;
    bool  wantLoop_ = false;
    int   volume_ = 100;
    std::string lastError_;
};

} // namespace lumina

#endif // LUMINA_HAS_WIN32
#endif // LUMINA_VIDEODS_H
