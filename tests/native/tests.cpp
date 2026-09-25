// ============================================================================
//  Fusion-HP · tests/native/tests.cpp — arnés de pruebas del núcleo nativo
// (auto-hospedado, sin vstest). Verifica: JSON, contrato ipc.v1 (parseo del
// Slide), sesión ahp.v1 del perfil C, bucle IPC real (pipe con nombre) y
// detección de entorno. Código de salida 0 solo si TODO pasa.
// ============================================================================
#include "../../src/core/Common.h"
#include "../../src/core/Logger.h"
#include "../../src/core/Environment.h"
#include "../../src/core/SlideState.h"
#include "../../src/core/NativeSession.h"
#include "../../src/core/IpcServer.h"
#include <cstdio>
#include <cstring>
#include <cassert>
#include <filesystem>
#include <iostream>
#include <iomanip>

using namespace fusion;

static int g_fail = 0, g_pass = 0;
#define CHECK(cond, msg) do { \
    std::cout << std::left << std::setw(52) << std::string(msg); \
    if (cond) { std::cout << "[OK]" << std::endl; g_pass++; } \
    else { std::cout << "[FALLO]" << std::endl; g_fail++; } \
} while (0)

static void TestEnvironment()
{
    auto env = Environment::Detect();
    CHECK(!env.osName.empty(), "Environment: nombre de SO presente");
    CHECK(env.archProcess == L"x86" || env.archProcess == L"x64", "Environment: arquitectura");
    CHECK(env.profile == L"A" || env.profile == L"B" || env.profile == L"C", "Environment: perfil A/B/C");
    Json j = env.ToJson();
    CHECK(j.contains("os") && j.contains("profile"), "Environment: JSON con campos");

    // Color
    CHECK(ParseColor("#FF0000") == 0xFFFF0000u, "ParseColor #FF0000");
    CHECK(ParseColor("#80FFFFFF") == 0x80FFFFFFu, "ParseColor #80FFFFFF");
    CHECK(ParseColor("mal") == 0xFF000000u, "ParseColor inválido → fallback");
}

static void TestJson()
{
    auto j = Json::parse("{\"nombre\":\"Culto\",\"n\":42,\"ok\":true,\"lista\":[1,2,3]}");
    CHECK(j["nombre"] == "Culto", "Json: string");
    CHECK(j["n"] == 42, "Json: entero");
    CHECK(j["ok"] == true, "Json: bool");
    CHECK(j["lista"].size() == 3, "Json: arreglo");
    std::string s = j.dump();
    auto j2 = Json::parse(s);
    CHECK(j2["nombre"] == "Culto", "Json: round-trip");
}

static void TestSlideState()
{
    // Contrato ipc.v1: exactamente lo que envía ResolvedSlide.ToIpcJson() en C#
    Json slide = Json::parse(R"({
      "id":"el-x","kind":"text","activeLine":1,
      "lines":["uno","dos","tres"],
      "style":{"font":"Segoe UI","size":44,"bold":false,"italic":false,
               "color":"#FFFFFF","activeColor":"#FFD700","align":1,"vAlign":1,
               "shadow":true,"outline":false,"lineSpacing":1.15,
               "box":{"x":0.05,"y":0.08,"w":0.9,"h":0.84}},
      "bg":{"color":"#101820","fit":0,"opacity":1.0}
    })");
    Slide s;
    bool ok = SlideState::ParseSlide(slide, s);
    CHECK(ok, "SlideState: parseo del contrato");
    CHECK(s.lines.size() == 3, "SlideState: líneas");
    CHECK(s.activeLine == 1, "SlideState: línea activa");
    CHECK(s.style.color == 0xFFFFFFFFu, "SlideState: color");
    CHECK(s.style.activeColor == 0xFFFFD700u, "SlideState: color activo");
    CHECK(s.style.font == L"Segoe UI", "SlideState: fuente");

    SlideState st;
    st.Set(s);
    CHECK(st.Get().lines.size() == 3, "SlideState: set/get");
    st.SetActiveLine(2);
    CHECK(st.Get().activeLine == 2, "SlideState: cambio de línea");
    st.SetBlank(BlankMode::Black);
    CHECK(st.Blank() == BlankMode::Black, "SlideState: reposo");

    // JSON con overlay (lower third)
    Json withOverlay = Json::parse(R"({
      "id":"e2","kind":"lower3","lines":["x"],
      "overlay":{"present":true,"lines":["aviso"],"position":0,"duration":0},
      "style":{"font":"Segoe UI","size":40,"color":"#FFFFFF","activeColor":"#FFD700","align":1,"vAlign":1,"shadow":true,"outline":false,"lineSpacing":1.15,"box":{"x":0.05,"y":0.08,"w":0.9,"h":0.84}},
      "bg":{"color":"#000000","fit":0,"opacity":1.0}
    })");
    Slide s2;
    CHECK(SlideState::ParseSlide(withOverlay, s2), "SlideState: overlay parsea");
    CHECK(s2.overlay.present && s2.overlay.lines[0] == L"aviso", "SlideState: overlay contenido");
}

static void TestNativeSession()
{
    NativeSession session;
    // fixtures/ junto al ejecutable
    std::wstring path = L"fixtures\\sesion.ahp";
    if (!std::filesystem::exists(path)) path = L"fixtures/sesion.ahp";
    bool ok = session.Load(path);
    CHECK(ok, "NativeSession: carga ahp.v1 (" + session.LastError() + ")");
    if (ok)
    {
        CHECK(session.Items().size() == 2, "NativeSession: 2 escenarios");
        CHECK(session.Items()[0].slides.size() == 1, "NativeSession: elementos");
        CHECK(session.Items()[0].slides[0].lines.size() == 2, "NativeSession: líneas de texto");
        // Herencia aplicada: el escenario baja a 40 sobre 48 del tema
        CHECK(session.Items()[0].slides[0].style.size == 40.0, "NativeSession: herencia tamaño 40");
        CHECK(session.Items()[0].slides[0].style.color == 0xFFFFFFFFu, "NativeSession: color del tema");
        CHECK(session.Items()[1].slides[0].style.color == 0xFFFFE9C8u, "NativeSession: override del elemento");
        CHECK(session.Items()[0].tags.size() == 2, "NativeSession: etiquetas");
    }
}

// Cliente de pipe mínimo para probar el bucle ipc.v1
static bool IpcRoundTrip(const std::wstring& pipeName)
{
    HANDLE pipe = CreateFileW(pipeName.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                              OPEN_EXISTING, 0, nullptr);
    if (pipe == INVALID_HANDLE_VALUE) return false;
    DWORD mode = PIPE_READMODE_BYTE;
    SetNamedPipeHandleMode(pipe, PIPE_READMODE_BYTE, &mode, nullptr);

    auto writeMsg = [&](const Json& j) {
        std::string s = j.dump();
        uint32_t len = (uint32_t)s.size();
        DWORD w;
        if (!WriteFile(pipe, &len, 4, &w, nullptr) || w != 4) return false;
        if (!WriteFile(pipe, s.data(), len, &w, nullptr) || w != len) return false;
        return true;
    };
    auto readMsg = [&](Json& out) {
        uint32_t len = 0;
        DWORD r = 0;
        if (!ReadFile(pipe, &len, 4, &r, nullptr) || r != 4) return false;
        std::string buf(len, '\0');
        if (!ReadFile(pipe, &buf[0], len, &r, nullptr) || r != (DWORD)len) return false;
        out = Json::parse(buf);
        return true;
    };

    Json hello = {{"v", 1}, {"id", "t1"}, {"cmd", "hello"}, {"payload", Json::object()}};
    if (!writeMsg(hello)) { CloseHandle(pipe); return false; }
    Json resp;
    if (!readMsg(resp)) { CloseHandle(pipe); return false; }
    bool okHello = resp.value("ok", false) && resp.value("protocol", std::string("")) == "ipc.v1";
    CloseHandle(pipe);
    return okHello;
}

static void TestIpcLoop()
{
    SlideState state;
    IpcContext ctx;
    ctx.state = &state;
    ctx.dispatch = [](const std::string& cmd, const Json& p) -> Json {
        if (cmd == "ping") return {{"ok", true}, {"data", {{"pong", true}}}};
        if (cmd == "line") { /* el App real actualiza el estado */ return {{"ok", true}}; }
        return {{"ok", false}, {"error", "desconocido"}};
    };
    IpcServer server(std::move(ctx));
    std::wstring name = L"\\\\.\\pipe\\FusionHP.tests." + std::to_wstring(GetCurrentProcessId());
    server.Start(name);
    CHECK(server.Running(), "IpcServer: servidor iniciado");
    Sleep(300);   // esperar hilo de aceptación
    CHECK(IpcRoundTrip(name), "IpcServer: hello/respuesta ipc.v1");
    server.Stop();
}

int main()
{
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

    TestEnvironment();
    TestJson();
    TestSlideState();
    TestNativeSession();
    TestIpcLoop();

    std::cout << "\nResultado: " << g_pass << " OK · " << g_fail << " FALLO" << std::endl;
    CoUninitialize();
    return g_fail == 0 ? 0 : 1;
}
