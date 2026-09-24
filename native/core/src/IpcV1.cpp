// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  IpcV1.cpp : implementación ipc.v1 (F0.05). Códec puro + cola + servidor
//  Win32 con OVERLAPPED. Incluido desde lumina_api.cpp (misma unidad de
//  traducción del ABI: el CMake del núcleo permanece INTACTO — restricción
//  del plan sobre archivos CMake).
// ============================================================================
#include "IpcV1.h"

#include <cstring>
#include <cstdio>
#include <atomic>
#include <thread>
#include <algorithm>

#include <nlohmann/json.hpp>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#endif

namespace lumina {
namespace ipc {

using json = nlohmann::json;

/* ------------------------------------------------ codificación pura ---- */

static void PutU16(std::vector<uint8_t>& b, uint16_t v) {
    b.push_back((uint8_t)(v & 0xFF));
    b.push_back((uint8_t)((v >> 8) & 0xFF));
}
static void PutU32(std::vector<uint8_t>& b, uint32_t v) {
    b.push_back((uint8_t)(v & 0xFF));
    b.push_back((uint8_t)((v >> 8) & 0xFF));
    b.push_back((uint8_t)((v >> 16) & 0xFF));
    b.push_back((uint8_t)((v >> 24) & 0xFF));
}
static uint32_t GetU32(const uint8_t* p) {
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) |
           ((uint32_t)p[3] << 24);
}
static uint16_t GetU16(const uint8_t* p) {
    return (uint16_t)((uint16_t)p[0] | ((uint16_t)p[1] << 8));
}

std::vector<uint8_t> EncodeFrame(uint16_t type, const std::string& payload) {
    std::vector<uint8_t> out;
    out.reserve(kHeaderSize + payload.size());
    PutU32(out, kMagic);
    PutU16(out, kVersion);
    PutU16(out, type);
    PutU32(out, (uint32_t)payload.size());
    out.insert(out.end(), payload.begin(), payload.end());
    return out;
}

int FrameDecoder::Feed(const uint8_t* data, size_t n, uint16_t* outType,
                       std::string* out, size_t* consumed) {
    if (consumed) *consumed = 0;
    if (!data && n > 0) return -1;
    buf_.insert(buf_.end(), data, data + n);
    while (true) {
        if (buf_.size() < kHeaderSize) return 0;      // cabecera incompleta
        const uint32_t magic = GetU32(buf_.data());
        if (magic != kMagic) {
            // Frame dañado: el decodificador se resetea y la SESIÓN decide
            // (ERROR BADFRAME al cliente y continúa — nunca tumba el núcleo).
            Reset();
            return -1;
        }
        const uint16_t ver = GetU16(buf_.data() + 4);
        if (ver != kVersion) {
            Reset();
            return -2;                                 // versión desconocida
        }
        const uint32_t len = GetU32(buf_.data() + 8);
        if (len > maxPayload_) {
            Reset();
            return -3;                                 // oversized
        }
        if (buf_.size() < kHeaderSize + (size_t)len) return 0;  // esperando
        if (outType) *outType = GetU16(buf_.data() + 6);
        if (out) out->assign((const char*)buf_.data() + kHeaderSize, len);
        const size_t used = kHeaderSize + (size_t)len;
        if (consumed) *consumed = used;
        buf_.erase(buf_.begin(), buf_.begin() + (std::ptrdiff_t)used);
        return 1;                                      // frame completo
    }
}

void FrameDecoder::Reset() { buf_.clear(); }

/* --------------------------------------------- cola no bloqueante ------ */

bool CommandQueue::TryPush(int64_t id, const std::string& action,
                           const std::string& payload) {
    std::lock_guard<std::mutex> lk(mtx_);
    if (items_.size() >= opt_.maxQueued) {
        stats_.rejectedFull++;
        return false;
    }
    Item it;
    it.id = id;
    it.action = action;
    it.payload = payload;
    it.enqueuedAt = std::chrono::steady_clock::now();
    items_.push_back(std::move(it));
    stats_.enqueued++;
    return true;
}

bool CommandQueue::TryPop(Item* out) {
    std::lock_guard<std::mutex> lk(mtx_);
    const auto now = std::chrono::steady_clock::now();
    // Descarta vencidos por edad (límite de latencia explícito, F0.05.6).
    while (!items_.empty()) {
        auto age = std::chrono::duration_cast<std::chrono::milliseconds>(
                       now - items_.front().enqueuedAt).count();
        if (age > (long long)opt_.maxAgeMs) {
            items_.erase(items_.begin());
            stats_.expiredDropped++;
            continue;
        }
        break;
    }
    if (items_.empty()) return false;
    if (out) *out = std::move(items_.front());
    items_.erase(items_.begin());
    stats_.executed++;
    return true;
}

CommandQueue::Stats CommandQueue::StatsSnapshot() const {
    std::lock_guard<std::mutex> lk(mtx_);
    return stats_;
}

size_t CommandQueue::Depth() const {
    std::lock_guard<std::mutex> lk(mtx_);
    return items_.size();
}

/* ------------------------------------------------------- servidor ------ */

struct Server::Impl {
    Options opt;
    CommandHandler handler;
    StateProvider  state;

    std::atomic<bool> running{false};
    std::thread acceptThread;
    std::thread drainThread;

    std::mutex writeMx;                 // serializa escrituras al pipe
    CommandQueue queue;

    std::atomic<int64_t> connections{0};
    std::atomic<int64_t> framesIn{0}, framesOut{0};
    std::atomic<int64_t> bytesIn{0}, bytesOut{0};
    std::atomic<int64_t> protoErrors{0};
    std::atomic<int64_t> busyRejects{0};
    std::string lastError;              // bajo writeMx
    bool notSupported = false;

#ifdef _WIN32
    HANDLE pipe = INVALID_HANDLE_VALUE;   // sesión activa (aceptador)
#endif

    void SetErr(const std::string& e) {
        std::lock_guard<std::mutex> lk(writeMx);
        lastError = e;
    }

#ifdef _WIN32
    /* Envío seguro de un frame (exclusión lector/drenaje; timeouts duros). */
    bool SendFrame(uint16_t type, const std::string& payload) {
        std::vector<uint8_t> fr = EncodeFrame(type, payload);
        std::lock_guard<std::mutex> lk(writeMx);
        HANDLE p = pipe;
        if (p == INVALID_HANDLE_VALUE) return false;
        OVERLAPPED ov; ZeroMemory(&ov, sizeof(ov));
        ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!ov.hEvent) return false;
        DWORD written = 0;
        BOOL ok = WriteFile(p, fr.data(), (DWORD)fr.size(), &written, &ov);
        if (!ok && GetLastError() == ERROR_IO_PENDING) {
            if (WaitForSingleObject(ov.hEvent, 5000) == WAIT_OBJECT_0)
                ok = GetOverlappedResult(p, &ov, &written, FALSE);
            else
                ok = FALSE;
        }
        CloseHandle(ov.hEvent);
        if (ok) { framesOut++; bytesOut += (int64_t)written; }
        return ok != FALSE;
    }

    void ReplyResult(int64_t id, bool ok, int32_t status,
                     const std::string& dataJson, const std::string& err) {
        json j;
        j["id"] = (double)id;
        j["ok"] = ok;
        j["status"] = (double)status;
        if (!dataJson.empty()) {
            try { j["data"] = json::parse(dataJson); }
            catch (...) { j["data"] = dataJson; }
        }
        if (!err.empty()) j["error"] = err;
        SendFrame(MSG_RESULT, j.dump());
    }

    void ReplyError(const std::string& code, const std::string& msg) {
        protoErrors++;
        json j;
        j["code"] = code;
        j["msg"] = msg;
        SendFrame(MSG_ERROR, j.dump());
    }

    /* Bucle de sesión (hilo propio): lee a bloques y alimenta el decoder. */
    void SessionLoop(HANDLE client) {
        {
            std::lock_guard<std::mutex> lk(writeMx);
            pipe = client;              // sesión activa para SendFrame
        }
        connections++;
        FrameDecoder dec(opt.maxPayload);
        uint8_t chunk[8192];
        bool alive = true;
        while (alive && running.load()) {
            OVERLAPPED ov; ZeroMemory(&ov, sizeof(ov));
            ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            if (!ov.hEvent) break;
            DWORD got = 0;
            BOOL ok = ReadFile(client, chunk, sizeof(chunk), &got, &ov);
            if (!ok && GetLastError() == ERROR_IO_PENDING) {
                HANDLE hs[2] = { ov.hEvent, nullptr };
                // espera cancelable por Stop() (cierra handle → error)
                DWORD w = WaitForSingleObject(ov.hEvent, INFINITE);
                if (w == WAIT_OBJECT_0) ok = GetOverlappedResult(client, &ov, &got, FALSE);
                else ok = FALSE;
                (void)hs;
            }
            CloseHandle(ov.hEvent);
            if (!ok || got == 0) break;              // desconexión del cliente
            bytesIn += (int64_t)got;

            uint16_t type = 0; std::string payload; size_t used = 0;
            const uint8_t* p = chunk;
            size_t left = got;
            int rc = 0;
            while (left > 0 || rc == 1) {
                rc = dec.Feed(p, left, &type, &payload, &used);
                if (rc == 0) break;                  // incompleto: espera más
                if (rc < 0) {
                    // magic/version/oversize: la sesión SIGUE (F0.05.7).
                    if (rc == -1) ReplyError("BADFRAME", "magic inválido");
                    if (rc == -2) ReplyError("BADVERSION", "frame de versión desconocida");
                    if (rc == -3) ReplyError("OVERSIZED", "payload excede el máximo");
                    break;
                }
                framesIn++;
                if (!HandleFrame(type, payload)) { alive = false; break; }
                p += used; left -= used;
                if (left == 0) break;
            }
            // frame completo contenido íntegramente en el búfer pendiente
            while (rc >= 0) {
                rc = dec.Feed(nullptr, 0, &type, &payload, &used);
                if (rc == 0) break;
                if (rc < 0) {
                    if (rc == -1) ReplyError("BADFRAME", "magic inválido");
                    if (rc == -2) ReplyError("BADVERSION", "frame de versión desconocida");
                    if (rc == -3) ReplyError("OVERSIZED", "payload excede el máximo");
                    break;
                }
                framesIn++;
                if (!HandleFrame(type, payload)) { alive = false; break; }
            }
        }
        {
            std::lock_guard<std::mutex> lk(writeMx);
            if (pipe == client) pipe = INVALID_HANDLE_VALUE;
        }
        // La proyección y los atajos NO dependen del cliente (F0.05.8):
        // solo se cierra la sesión; el aceptador reabrirá el pipe.
    }

    /* Un frame completo decodificado. false = terminar sesión. */
    bool HandleFrame(uint16_t type, const std::string& payload) {
        if (type == MSG_PING) {
            return SendFrame(MSG_PONG, payload);
        }
        if (type == MSG_HELLO) {
            std::string proto;
            try {
                json j = json::parse(payload);
                if (j.contains("proto") && j["proto"].is_string())
                    proto = j["proto"].get<std::string>();
            } catch (...) {
                ReplyError("BADFRAME", "HELLO no es JSON válido");
                return true;                      // sesión continúa
            }
            if (proto != "ipc.v1") {
                // Versión de protocolo desconocida: rechazo EXPLÍCITO y
                // sesión viva (compatibilidad evolutiva sin romper ipc.v1).
                ReplyError("BADVERSION",
                           "protocolo solicitado no soportado: '" + proto +
                           "' (el servidor habla ipc.v1)");
                return true;
            }
            json w;
            w["proto"] = "ipc.v1";
            w["server"] = "LuminaCore/7.0.0";
            w["maxPayload"] = (double)opt.maxPayload;
            w["queue"] = {{"maxQueued", (double)opt.queue.maxQueued},
                          {"maxAgeMs",  (double)opt.queue.maxAgeMs}};
            return SendFrame(MSG_WELCOME, w.dump());
        }
        if (type == MSG_COMMAND) {
            int64_t id = 0;
            std::string action, inner;
            try {
                json j = json::parse(payload);
                if (j.contains("id"))     id = j["id"].get<int64_t>();
                if (j.contains("action") && j["action"].is_string())
                    action = j["action"].get<std::string>();
                if (j.contains("payload"))
                    inner = j["payload"].is_string()
                        ? j["payload"].get<std::string>() : j["payload"].dump();
            } catch (...) {
                ReplyError("BADFRAME", "COMMAND no es JSON válido");
                return true;                      // el núcleo sigue en pie
            }
            if (action.empty()) {
                ReplyResult(id, false, -1, "", "acción vacía");
                return true;
            }
            if (!queue.TryPush(id, action, inner)) {
                busyRejects++;
                ReplyResult(id, false, -5, "", "BUSY: cola llena");
            }
            return true;
        }
        if (type == MSG_WELCOME || type == MSG_RESULT || type == MSG_STATE ||
            type == MSG_EVENT || type == MSG_PONG) {
            ReplyError("BADTYPE", "tipo de mensaje inesperado del cliente");
            return true;
        }
        ReplyError("BADTYPE", "tipo desconocido");
        return true;
    }

    /* Aceptador: crea el pipe, espera cliente, sirve la sesión, repite. */
    void AcceptLoop() {
        while (running.load()) {
            const std::wstring name = PipePath(opt.pipeName);
            HANDLE p = CreateNamedPipeW(
                name.c_str(),
                PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
                PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
                1,                       // 1 instancia: 1 cliente por vez
                64 * 1024, 64 * 1024, 0, nullptr);
            if (p == INVALID_HANDLE_VALUE) {
                SetErr("CreateNamedPipe falló (errno " +
                       std::to_string((long long)GetLastError()) + ")");
                std::this_thread::sleep_for(std::chrono::milliseconds(500));
                continue;
            }
            OVERLAPPED ov; ZeroMemory(&ov, sizeof(ov));
            ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
            if (!ov.hEvent) { CloseHandle(p); continue; }
            BOOL connected = ConnectNamedPipe(p, &ov);
            if (!connected && GetLastError() == ERROR_IO_PENDING) {
                // Espera cancelable: Stop() cierra el handle del aceptador
                // via CancelIo… al no tenerlo aquí, se sondea running.
                while (running.load()) {
                    DWORD w = WaitForSingleObject(ov.hEvent, 200);
                    if (w == WAIT_OBJECT_0) {
                        connected = GetOverlappedResult(p, &ov, nullptr, FALSE);
                        break;
                    }
                    if (w == WAIT_FAILED) { connected = FALSE; break; }
                }
                if (!running.load()) connected = FALSE;
            } else if (!connected && GetLastError() != ERROR_PIPE_CONNECTED) {
                connected = FALSE;
            }
            CloseHandle(ov.hEvent);
            if (!connected) {
                CloseHandle(p);
                continue;
            }
            SessionLoop(p);
            // Limpieza determinista de la sesión terminada.
            CancelIoEx(p, nullptr);
            FlushFileBuffers(p);
            DisconnectNamedPipe(p);
            CloseHandle(p);
        }
    }

    /* Drenaje: ejecuta comandos encolados y emite RESULT + STATE. */
    void DrainLoop() {
        while (running.load()) {
            CommandQueue::Item it;
            bool had = false;
            while (queue.TryPop(&it)) {
                had = true;
                std::string data;
                const int32_t st = handler
                    ? handler(it.action, it.payload, &data)
                    : (int32_t)-6;
                ReplyResult(it.id, st == 0, st, data,
                            st == 0 ? "" : "estado " + std::to_string((long long)st));
                // push de estado tras cada comando (F0.05.4: transporta
                // comandos, estado, carga de proyecto y resultados).
                if (state) SendFrame(MSG_STATE, state());
            }
            if (!had)
                std::this_thread::sleep_for(std::chrono::milliseconds(10));
        }
    }

    static std::wstring PipePath(const std::string& name) {
        std::string full = "\\\\.\\pipe\\";
        if (name.rfind("\\\\.\\pipe\\", 0) == 0) full = name;
        else full += name;
        const int n = MultiByteToWideChar(CP_UTF8, 0, full.c_str(),
                                          (int)full.size(), nullptr, 0);
        std::wstring w((size_t)(n > 0 ? n : 1), L'\0');
        if (n > 0)
            MultiByteToWideChar(CP_UTF8, 0, full.c_str(), (int)full.size(),
                                &w[0], n);
        return w;
    }
#endif   // _WIN32
};

Server::Server() : impl_(new Impl) {}
Server::~Server() { Stop(); }

bool Server::Start(const Options& opt, CommandHandler handler,
                   StateProvider state) {
    if (impl_->running.exchange(true)) return true;   // ya corriendo
    impl_->opt = opt;
    impl_->handler = std::move(handler);
    impl_->state = std::move(state);
    impl_->queue.Configure(opt.queue);

#ifdef _WIN32
    impl_->acceptThread = std::thread([i = impl_.get()]() { i->AcceptLoop(); });
    impl_->drainThread  = std::thread([i = impl_.get()]() { i->DrainLoop(); });
    return true;
#else
    // Linux/arnés: sin transporte Win32. El proceso NO falla (F0.05.7:
    // un fallo del IPC jamás tumba el núcleo ni la proyección).
    impl_->notSupported = true;
    impl_->SetErr("transporte de pipes Win32 no disponible en esta plataforma");
    return false;
#endif
}

void Server::Stop() {
    if (!impl_->running.exchange(false)) return;
#ifdef _WIN32
    {
        std::lock_guard<std::mutex> lk(impl_->writeMx);
        if (impl_->pipe != INVALID_HANDLE_VALUE) {
            CancelIoEx(impl_->pipe, nullptr);
            DisconnectNamedPipe(impl_->pipe);
            CloseHandle(impl_->pipe);
            impl_->pipe = INVALID_HANDLE_VALUE;
        }
    }
    if (impl_->acceptThread.joinable()) impl_->acceptThread.join();
    if (impl_->drainThread.joinable())  impl_->drainThread.join();
#endif
}

bool Server::Running() const { return impl_->running.load(); }

std::string Server::StatsJson() const {
    const auto q = impl_->queue.StatsSnapshot();
    std::string last;
    {
        std::lock_guard<std::mutex> lk(impl_->writeMx);
        last = impl_->lastError;
    }
    json j;
    j["running"] = impl_->running.load() ? 1 : 0;
    j["notSupported"] = impl_->notSupported ? 1 : 0;
    j["connections"] = (double)impl_->connections.load();
    j["framesIn"] = (double)impl_->framesIn.load();
    j["framesOut"] = (double)impl_->framesOut.load();
    j["bytesIn"] = (double)impl_->bytesIn.load();
    j["bytesOut"] = (double)impl_->bytesOut.load();
    j["protoErrors"] = (double)impl_->protoErrors.load();
    j["busyRejects"] = (double)impl_->busyRejects.load();
    j["queue"] = {
        {"enqueued", (double)q.enqueued}, {"executed", (double)q.executed},
        {"expiredDropped", (double)q.expiredDropped},
        {"rejectedFull", (double)q.rejectedFull},
        {"depth", (double)impl_->queue.Depth()},
        {"maxQueued", (double)impl_->opt.queue.maxQueued},
        {"maxAgeMs", (double)impl_->opt.queue.maxAgeMs},
    };
    j["lastError"] = last;
    return j.dump();
}

} // namespace ipc
} // namespace lumina
