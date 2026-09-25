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
#include "../../src/core/Highlight.h"
#include "../../src/core/NativeSession.h"
#include "../../src/core/IpcServer.h"
#include "../../src/core/Motor.h"
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

static void TestHighlight()
{
    // Port de las betas 1: acentos insensibles + frontera de palabra + verbatim
    auto segs = Highlight::Split(L"Porque tanto AMÓ Dios al mundo", {L"amo"});
    bool any = false;
    for (auto& sg : segs) if (sg.match) any = true;
    CHECK(any, "Highlight: AMO coincide con 'amo' sin acento");

    segs = Highlight::Split(L"Diosas y Dios y diosdad", {L"Dios"});
    int matches = 0;
    for (auto& sg : segs) if (sg.match) matches++;
    CHECK(matches == 1, "Highlight: frontera de palabra (solo 'Dios')");

    std::wstring joined;
    segs = Highlight::Split(L"Él es DIOS sobre todo", {L"dios"});
    for (auto& sg : segs) joined += sg.text;
    CHECK(joined == L"Él es DIOS sobre todo", "Highlight: texto VERBATIM");
}

static void TestSlideStateV21()
{
    // Contrato v2.1: transición + resaltado + modo clear
    Json slide = Json::parse(R"({
      "id":"el-v21","kind":"verse","activeLine":0,
      "lines":["Porque de tal manera amó Dios"],
      "transition":"slide",
      "highlight":["Dios"],
      "style":{"font":"Outfit","size":46,"bold":false,"italic":false,
               "color":"#FFF8EC","activeColor":"#E8C872","align":1,"vAlign":1,
               "shadow":true,"outline":false,"lineSpacing":1.2,
               "box":{"x":0.05,"y":0.08,"w":0.9,"h":0.84}},
      "bg":{"color":"#0A0704","fit":0,"opacity":1.0}
    })");
    Slide s;
    CHECK(SlideState::ParseSlide(slide, s), "SlideState v2.1: parseo");
    CHECK(s.transition == "slide", "SlideState v2.1: transición slide");
    CHECK(s.highlight.size() == 1 && s.highlight[0] == L"Dios", "SlideState v2.1: resaltado");
    CHECK(s.style.font == L"Outfit", "SlideState v2.1: fuente incrustada");
    SlideState st;
    st.Set(s);
    st.SetBlank(BlankMode::Clear);
    CHECK(st.Blank() == BlankMode::Clear, "SlideState v2.1: modo clear (tecla C)");
}

static void TestNativeSession()
{
    NativeSession session;
    // fixtures/ junto al ejecutable (robusto ante cualquier CWD)
    std::wstring path = L"fixtures\\sesion.ahp";
    if (!std::filesystem::exists(path)) path = L"fixtures/sesion.ahp";
    if (!std::filesystem::exists(path)) {
        wchar_t self[MAX_PATH];
        GetModuleFileNameW(nullptr, self, MAX_PATH);
        std::filesystem::path base = std::filesystem::path(self).parent_path();
        path = (base / L"fixtures" / L"sesion.ahp").wstring();
    }
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
    // modo BYTE es el predeterminado en clientes de este protocolo

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

// ---------------------------------------------------------------- MOTOR v2.2
// El Motor es el dueño del estado vivo: estos tests verifican la máquina de
// estados (carga, avance línea/elemento, pantallas, resaltado, avance por
// diapositiva) y la persistencia de sesión sin GUI conectada.
static Json TestMotorSlide(const std::string& id, const std::string& a, const std::string& b = "",
                           const std::string& c = "")
{
    Json lines = Json::array();
    if (!a.empty()) lines.push_back(a);
    if (!b.empty()) lines.push_back(b);
    if (!c.empty()) lines.push_back(c);
    return Json{{"id", id}, {"kind", "text"}, {"lines", lines},
                {"style", {"font", "Segoe UI"}, {"size", 44}, {"color", "#FFFFFF"}}};
}

static void TestMotor()
{
    // programa: 2 escenarios × (2 y 1 elementos); el primero tiene 3 líneas
    Json prog = Json::array();
    {
        Json els = Json::array();
        Json e1, e2;
        e1["id"] = "e1"; e1["title"] = "Himno v1"; e1["kind"] = "text";
        e1["slide"] = TestMotorSlide("e1", "línea A", "línea B", "línea C");
        e2["id"] = "e2"; e2["title"] = "Himno v2"; e2["kind"] = "text";
        e2["slide"] = TestMotorSlide("e2", "coro", "coro 2");
        els.push_back(e1); els.push_back(e2);
        prog.push_back(Json{{"id", "s1"}, {"title", "Adoración"}, {"elements", els}});
    }
    {
        Json els = Json::array();
        Json e3;
        e3["id"] = "e3"; e3["title"] = "Anuncio"; e3["kind"] = "lower3";
        e3["slide"] = TestMotorSlide("e3", " Bienvenidos");
        els.push_back(e3);
        prog.push_back(Json{{"id", "s2"}, {"title", "Avisos"}, {"elements", els}});
    }

    Motor m;
    std::vector<Json> applied;          // diapositivas que el Motor manda aplicar
    m.ApplySlideJson = [&](const Json& j) { applied.push_back(j); };

    Json payload = Json::object();
    payload["program"] = prog;
    payload["select"] = Json{{"scenario", 0}, {"element", 0}, {"line", 0}};
    payload["advance"] = "line";
    CHECK(m.LoadProgram(payload), "Motor: carga de programa");

    Json st = m.StateJson();
    CHECK(st.value("hasProgram", false), "Motor: hasProgram");
    CHECK(st.value("scenario", -1) == 0 && st.value("element", -1) == 0, "Motor: selección inicial");
    CHECK(st.value("lineCount", 0) == 3, "Motor: lineCount del elemento");
    CHECK(st["program"].size() == 2, "Motor: programa con 2 escenarios");
    CHECK(st["program"][1].value("count", 0) == 1, "Motor: conteo del 2º escenario");
    CHECK(!applied.empty() && applied.back().value("activeLine", -1) == 0, "Motor: aplica diapositiva inicial");

    // ---- avance línea por línea (Espacio): 0→1→2→ elemento siguiente
    m.Next(); m.Next();
    st = m.StateJson();
    CHECK(st.value("line", -1) == 2, "Motor: Next avanza línea");
    m.Next();                        // fin del elemento → siguiente elemento, línea 0
    st = m.StateJson();
    CHECK(st.value("element", -1) == 1 && st.value("line", -1) == 0, "Motor: Next cruza a elemento");
    CHECK(applied.back().value("activeLine", -1) == 0, "Motor: elemento nuevo aplica línea 0");
    m.Next(); m.Next();              // fin e2 → escenario 2, elemento 0
    st = m.StateJson();
    CHECK(st.value("scenario", -1) == 1 && st.value("element", -1) == 0, "Motor: Next cruza escenario");
    m.Next();                        // último elemento: se queda (sin envolver)
    st = m.StateJson();
    CHECK(st.value("scenario", -1) == 1 && st.value("element", -1) == 0, "Motor: al final se queda");

    // ---- retroceso elemento: vuelve al escenario 1, ÚLTIMO elemento
    m.PrevElement();
    st = m.StateJson();
    CHECK(st.value("scenario", -1) == 0 && st.value("element", -1) == 1, "Motor: PrevElement a último");
    CHECK(m.SetLine(1), "Motor: SetLine válido");
    CHECK(!m.SetLine(99), "Motor: SetLine fuera de rango rechazado");

    // ---- pantallas (B/C/L/Esc)
    m.SetBlank("black");
    CHECK(m.StateJson().value("blank", "") == "black", "Motor: blank negro");
    m.SetBlank("none");
    CHECK(m.StateJson().value("blank", "") == "none", "Motor: blank none");

    // ---- resaltado: capa de anulación sobre la diapositiva aplicada
    m.SetBlank("none");
    m.Goto(0, 0, 0);
    m.Highlight({"Dios", "amor"});
    st = m.StateJson();
    CHECK(st["highlight"].size() == 2, "Motor: resaltado en estado");
    CHECK(applied.back().contains("highlight") && applied.back()["highlight"].size() == 2,
          "Motor: resaltado aplicado a la diapositiva");
    m.Highlight({});                   // vaciar
    CHECK(m.StateJson()["highlight"].size() == 0, "Motor: resaltado se limpia");

    // ---- avance por diapositiva (referencia web)
    m.SetAdvance("slide");
    CHECK(m.StateJson().value("advance", "") == "slide", "Motor: modo slide");
    m.Goto(0, 0, 0);
    m.Next();                        // con slide: salta el elemento entero
    st = m.StateJson();
    CHECK(st.value("element", -1) == 1, "Motor: slide avanza elemento completo");
    m.SetAdvance("line");

    // ---- teclas autónomas (sin GUI): Espacio/B/C/L/Esc
    m.Goto(0, 0, 0);
    m.StandaloneKey('B');
    CHECK(m.StateJson().value("blank", "") == "black", "Motor: tecla B → negro");
    m.StandaloneKey('B');
    CHECK(m.StateJson().value("blank", "") == "none", "Motor: tecla B alterna");
    m.StandaloneKey(VK_SPACE);
    CHECK(m.StateJson().value("line", -1) == 1, "Motor: tecla Espacio avanza");
    m.StandaloneKey(VK_ESCAPE);
    CHECK(m.StateJson().value("blank", "") == "black", "Motor: Esc → reposo");

    // ---- añadir escenario (Enviar a vivo)
    Json nuevo = Json{{"scenario", Json{{"id", "s3"}, {"title", "Nuevo"},
        {"elements", Json::array({Json{{"id", "n1"}, {"kind", "text"},
            {"slide", TestMotorSlide("n1", "nueva")}}})}}}};
    CHECK(m.AppendScenario(nuevo), "Motor: append escenario");
    st = m.StateJson();
    CHECK(st["program"].size() == 3 && st.value("scenario", -1) == 2, "Motor: append selecciona el nuevo");

    // ---- persistencia: el Motor recuerda el programa entre ejecuciones
    std::wstring tmp = std::filesystem::temp_directory_path().wstring() +
                       L"\\FusionHP.motor.test." + std::to_wstring(GetCurrentProcessId()) + L".json";
    DeleteFileW(tmp.c_str());
    m.SetPersistFile(tmp);
    m.Goto(1, 0, 0);
    m.SavePersisted(tmp);
    Motor m2;
    CHECK(m2.LoadPersisted(tmp), "Motor: LoadPersisted lee sesion.json");
    st = m2.StateJson();
    CHECK(st["program"].size() == 3, "Motor: programa persistido completo");
    CHECK(st.value("scenario", -1) == 1 && st.value("element", -1) == 0, "Motor: selección persistida");
    CHECK(m2.HasProgram(), "Motor: HasProgram tras persistir");
    m2.Clear();
    CHECK(!m2.HasProgram(), "Motor: Clear vacía el programa");
    DeleteFileW(tmp.c_str());
}

int main()
{
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

    TestEnvironment();
    TestJson();
    TestSlideState();
    TestHighlight();
    TestSlideStateV21();
    TestNativeSession();
    TestIpcLoop();
    TestMotor();

    std::cout << "\nResultado: " << g_pass << " OK · " << g_fail << " FALLO" << std::endl;
    CoUninitialize();
    return g_fail == 0 ? 0 : 1;
}
