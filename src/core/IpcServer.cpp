// ============================================================================
//  Fusion-HP · IpcServer.cpp — implementación del pipe ipc.v1
// ============================================================================
#include "IpcServer.h"
#include "Logger.h"

namespace fusion {

bool IpcServer::ReadExact(HANDLE pipe, void* buf, DWORD n) {
    DWORD got = 0;
    BYTE* p = (BYTE*)buf;
    while (got < n) {
        DWORD chunk = 0;
        if (!ReadFile(pipe, p + got, n - got, &chunk, nullptr) || chunk == 0) return false;
        got += chunk;
    }
    return true;
}

bool IpcServer::WriteJson(HANDLE pipe, const Json& j) {
    std::string s = j.dump();
    if (s.size() > 8u * 1024u * 1024u) return false;
    uint32_t len = (uint32_t)s.size();
    DWORD wrote = 0;
    if (!WriteFile(pipe, &len, sizeof(len), &wrote, nullptr) || wrote != sizeof(len)) return false;
    if (!WriteFile(pipe, s.data(), len, &wrote, nullptr) || wrote != len) return false;
    FlushFileBuffers(pipe);
    return true;
}

bool IpcServer::Start(const std::wstring& pipeName) {
    pipeName_ = pipeName;
    running_ = true;
    acceptThread_ = std::thread([this] { AcceptLoop(); });
    Logger::Info("core.ipc", "servidor ipc.v1 escuchando en " + ToUtf8(pipeName));
    return true;
}

void IpcServer::Stop() {
    if (!running_.exchange(false)) return;
    // Abrir una conexión fantasma para despertar el ConnectNamedPipe bloqueante
    HANDLE wake = CreateFileW(pipeName_.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                              OPEN_EXISTING, 0, nullptr);
    if (wake != INVALID_HANDLE_VALUE) {
        uint32_t len = 2;
        DWORD w = 0;
        WriteFile(wake, &len, sizeof(len), &w, nullptr);
        WriteFile(wake, "{}", 2, &w, nullptr);
        CloseHandle(wake);
    }
    if (acceptThread_.joinable()) acceptThread_.join();
    std::lock_guard<std::mutex> lk(clientsMutex_);
    for (HANDLE h : clients_) { DisconnectNamedPipe(h); CloseHandle(h); }
    clients_.clear();
}

void IpcServer::AcceptLoop() {
    int instance = 0;
    while (running_.load()) {
        HANDLE pipe = CreateNamedPipeW(pipeName_.c_str(),
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
            kMaxClients, 64 * 1024, 64 * 1024, 5000, nullptr);
        if (pipe == INVALID_HANDLE_VALUE) {
            Logger::Error("core.ipc", "CreateNamedPipe fallo (instancia " + std::to_string(instance) + ")");
            std::this_thread::sleep_for(std::chrono::milliseconds(500));
            continue;
        }
        BOOL ok = ConnectNamedPipe(pipe, nullptr) ? TRUE : (GetLastError() == ERROR_PIPE_CONNECTED ? TRUE : FALSE);
        if (!running_.load()) { CloseHandle(pipe); break; }
        if (!ok) { CloseHandle(pipe); continue; }

        {
            std::lock_guard<std::mutex> lk(clientsMutex_);
            if ((int)clients_.size() >= kMaxClients) {
                Json busy = {{"v", 1}, {"ok", false}, {"error", "limite de clientes ipc alcanzado"}};
                WriteJson(pipe, busy);
                DisconnectNamedPipe(pipe);
                CloseHandle(pipe);
                continue;
            }
            clients_.push_back(pipe);
        }
        instance++;
        std::thread([this, pipe] { ClientLoop(pipe); }).detach();
    }
}

void IpcServer::ClientLoop(HANDLE pipe) {
    Logger::Info("core.ipc", "cliente conectado");
    while (running_.load()) {
        uint32_t len = 0;
        if (!ReadExact(pipe, &len, sizeof(len))) break;
        if (len == 0 || len > 8u * 1024u * 1024u) break;
        std::string buf(len, '\0');
        if (!ReadExact(pipe, &buf[0], len)) break;
        try {
            Json msg = Json::parse(buf);
            HandleMessage(msg, pipe);
        } catch (const std::exception& e) {
            Json err = {{"v", 1}, {"ok", false}, {"error", std::string("json invalido: ") + e.what()}};
            WriteJson(pipe, err);
        }
    }
    {
        std::lock_guard<std::mutex> lk(clientsMutex_);
        clients_.erase(std::remove(clients_.begin(), clients_.end(), pipe), clients_.end());
    }
    DisconnectNamedPipe(pipe);
    CloseHandle(pipe);
    Logger::Info("core.ipc", "cliente desconectado");
}

void IpcServer::HandleMessage(const Json& msg, HANDLE pipe) {
    if (!msg.is_object() || !msg.contains("cmd")) {
        WriteJson(pipe, {{"v", 1}, {"ok", false}, {"error", "mensaje sin cmd"}});
        return;
    }
    std::string cmd = msg["cmd"].get<std::string>();
    Json payload = msg.contains("payload") ? msg["payload"] : Json::object();
    std::string id = msg.value("id", std::string());

    if (cmd == "ping") {
        WriteJson(pipe, {{"v", 1}, {"ok", true}, {"id", id}, {"data", {{"pong", true}, {"server", FUSION_VERSION}}}});
        return;
    }
    if (cmd == "hello") {
        WriteJson(pipe, {{"v", 1}, {"ok", true}, {"id", id}, {"protocol", FUSION_IPC_PROTOCOL},
                         {"server", FUSION_VERSION},
                         {"data", {{"profile", "nucleo"}}}});
        return;
    }

    // El resto se despacha al App (que posee ventanas/video/etc.)
    Json resp;
    if (ctx_.dispatch) {
        resp = ctx_.dispatch(cmd, payload);
    } else {
        resp = {{"ok", false}, {"error", "comando no despachable"}};
    }
    resp["v"] = 1;
    resp["id"] = id;
    WriteJson(pipe, resp);
}

void IpcServer::BroadcastEvent(const std::string& evt, const Json& data) {
    Json msg = {{"v", 1}, {"evt", evt}, {"data", data}};
    std::lock_guard<std::mutex> lk(clientsMutex_);
    for (HANDLE h : clients_) WriteJson(h, msg);
}

} // namespace fusion
