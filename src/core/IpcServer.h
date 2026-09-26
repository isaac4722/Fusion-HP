// ============================================================================
//  Fusion-HP · IpcServer — puente C# ↔ C++ [SPEC §3.4]
//  Pipe con nombre \\.\pipe\FusionHP.ipc.v1, protocolo binario simple
//  (longitud prefijada u32 LE + JSON UTF-8), versionado ipc.v1.
//  Comandos desde la capa C#; eventos de estado hacia todos los clientes.
// ============================================================================
#pragma once
#include "Common.h"
#include "SlideState.h"
#include <functional>

namespace fusion {

struct IpcContext {
    SlideState* state = nullptr;
    std::function<void(const Json&)> onCommand;   // dispatch externo (App)
    std::function<Json(const std::string&, const Json&)> dispatch; // devuelve respuesta
};

class IpcServer {
public:
    explicit IpcServer(IpcContext ctx) : ctx_(std::move(ctx)) {}
    ~IpcServer() { Stop(); }

    bool Start(const std::wstring& pipeName);
    void Stop();
    bool Running() const { return running_.load(); }

    // Difunde un evento a todos los clientes conectados {v:1, evt:..., data:...}
    void BroadcastEvent(const std::string& evt, const Json& data);

    // Clientes GUI conectados ahora mismo (0 => el Motor puede ser autónomo)
    int ClientCount()
    {
        std::lock_guard<std::mutex> lk(clientsMutex_);
        return (int)clients_.size();
    }

private:
    void AcceptLoop();
    void ClientLoop(HANDLE pipe);
    void HandleMessage(const Json& msg, HANDLE pipe);
    static bool WriteJson(HANDLE pipe, const Json& j);
    static bool ReadExact(HANDLE pipe, void* buf, DWORD n);

    IpcContext ctx_;
    std::wstring pipeName_;
    std::atomic<bool> running_{false};
    std::thread acceptThread_;
    std::vector<HANDLE> clients_;
    std::mutex clientsMutex_;
    static constexpr int kMaxClients = 4;
};

} // namespace fusion
