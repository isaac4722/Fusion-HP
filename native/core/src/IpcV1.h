// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  IpcV1.h : protocolo ipc.v1 (F0.05 del Plan de Ultra Implementación,
//  Sección 3.4 del documento técnico).
//
//  CONTRATO (§2.1 del plan):
//   * pipes con nombre (Win32: \\.\pipe\<name>);
//   * mensajes BINARIOS con longitud prefijada;
//   * versión del protocolo: «ipc.v1»;
//   * cola NO bloqueante de comandos hacia render, con límite de latencia
//     explícito (maxAgeMs; los vencidos se descartan y se cuentan — F6.01);
//   * validación de mensajes inválidos SIN tumbar el núcleo (F0.05.7);
//   * la proyección y los atajos sobreviven si C# se desconecta (F0.05.8).
//
//  FRAME (12 bytes de cabecera + payload):
//   [u32 magic=0x4C4D4950 "LMIP"][u16 version=1][u16 type][u32 length]
//
//  TIPOS:
//   1 HELLO    c→s {"proto":"ipc.v1","client":"..."}
//   2 WELCOME  s→c {"proto":"ipc.v1","server":"...","maxPayload":N}
//   3 COMMAND  c→s {"id":N,"action":"...","payload":{...}}
//   4 RESULT   s→c {"id":N,"ok":true,"status":0,"data":{...}}
//   5 STATE    s→c estado completo JSON (push tras cada comando)
//   6 EVENT    s→c {"code":N,"payload":"..."}   (eco del bus de eventos)
//   7 PING     cualquiera (payload opaco)
//   8 PONG     respuesta espejo
//   9 ERROR    s→c {"code":"BADVERSION|BADFRAME|OVERSIZED|BADTYPE|BUSY",
//                    "msg":"..."}
//
//  CAPAS (para probar TODO en el arnés Linux y ejercitar el pipe REAL en el
//  runner Windows):
//   * FrameDecoder / EncodeFrame  — puro, multi-plataforma (fragmentación,
//     magic malo, versión desconocida, payload grande).
//   * CommandQueue               — puro (capacidad, expiración, métricas).
//   * Server                     — transporte Win32 (overlapped, reconexión,
//     hilo lector + hilo de drenaje); en Linux Start() informa «no
//     disponible» sin fallar el proceso.
// ============================================================================
#ifndef LUMINA_IPCV1_H
#define LUMINA_IPCV1_H

#include <cstdint>
#include <chrono>
#include <functional>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

namespace lumina {
namespace ipc {

constexpr uint32_t kMagic   = 0x4C4D4950u;    // "LMIP"
constexpr uint16_t kVersion = 1;
constexpr size_t   kHeaderSize = 12;
constexpr uint32_t kDefaultMaxPayload = 8u * 1024 * 1024;   // 8 MB

enum MsgType : uint16_t {
    MSG_HELLO   = 1,
    MSG_WELCOME = 2,
    MSG_COMMAND = 3,
    MSG_RESULT  = 4,
    MSG_STATE   = 5,
    MSG_EVENT   = 6,
    MSG_PING    = 7,
    MSG_PONG    = 8,
    MSG_ERROR   = 9,
};

/* ------------------------------------------------ codificación pura ---- */

// Codifica un frame completo (cabecera + payload). payload puede ser vacío.
std::vector<uint8_t> EncodeFrame(uint16_t type, const std::string& payload);

/* Decodificador con reensamblado: admite Feed de cualquier tamaño (incluso
   de a 1 byte — prueba de fragmentación F0.05.9).
   Return:  0 = necesita más bytes
            1 = frame completo (outType/out rellenos; *consumed bytes usados)
           -1 = magic inválido (sesión continúa; decoder se resetea)
           -2 = versión de frame desconocida
           -3 = payload excede maxPayload
*/
class FrameDecoder {
public:
    explicit FrameDecoder(uint32_t maxPayload = kDefaultMaxPayload)
        : maxPayload_(maxPayload) {}

    int Feed(const uint8_t* data, size_t n, uint16_t* outType,
             std::string* out, size_t* consumed);

    void   Reset();
    size_t Buffered() const { return buf_.size(); }

private:
    uint32_t maxPayload_ = kDefaultMaxPayload;
    std::vector<uint8_t> buf_;
};

/* --------------------------------------- cola no bloqueante (F0.05.5) -- */

class CommandQueue {
public:
    struct Options {
        size_t maxQueued = 256;    // capacidad explícita (backpressure)
        int    maxAgeMs  = 50;     // límite de latencia explícito
    };
    struct Item {
        int64_t id = 0;
        std::string action;
        std::string payload;
        std::chrono::steady_clock::time_point enqueuedAt;
    };
    struct Stats {
        int64_t enqueued = 0, executed = 0, expiredDropped = 0, rejectedFull = 0;
    };

    CommandQueue() {}
    explicit CommandQueue(const Options& o) : opt_(o) {}

    // Reconfigura capacidad/latencia en caliente (sin mover el mutex).
    void Configure(const Options& o) {
        std::lock_guard<std::mutex> lk(mtx_);
        opt_ = o;
    }

    // No bloquea JAMÁS: rechaza con false si la cola está llena
    // (el servidor responde RESULT BUSY — política visible, no silencio).
    bool TryPush(int64_t id, const std::string& action,
                 const std::string& payload);
    // Extrae el más antiguo NO vencido; descarta y cuenta los vencidos
    // (F6.01 «comandos descartados»).
    bool TryPop(Item* out);

    Stats  StatsSnapshot() const;
    size_t Depth() const;

private:
    Options opt_;
    mutable std::mutex mtx_;
    std::vector<Item> items_;
    Stats stats_;
};

/* ------------------------------------------------------- servidor ------ */

// El handler ejecuta un comando del cliente contra el motor. Devuelve el
// estado LuminaStatus; resultJson recibe la data de RESULT.
typedef std::function<int32_t(const std::string& action,
                              const std::string& payload,
                              std::string* resultJson)> CommandHandler;
typedef std::function<std::string()> StateProvider;   // estado completo JSON

class Server {
public:
    struct Options {
        std::string pipeName = "lumina-ipc-v1";   // \\.\pipe\<pipeName>
        uint32_t    maxPayload = kDefaultMaxPayload;
        CommandQueue::Options queue;
    };

    Server();
    ~Server();

    // Arranca el hilo aceptador. handler/state se invocan desde el hilo de
    // drenaje del servidor (nunca desde el lector de pipes).
    bool Start(const Options& opt, CommandHandler handler, StateProvider state);
    void Stop();
    bool Running() const;

    // Métricas para Diagnóstico/F6.01: conexiones, frames, bytes, cola,
    // errores de protocolo, último error humano.
    std::string StatsJson() const;

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace ipc
} // namespace lumina

#endif // LUMINA_IPCV1_H
