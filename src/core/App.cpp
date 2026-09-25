// ============================================================================
//  Fusion-HP · App.cpp — bootstrap y ciclo principal del núcleo
// ============================================================================
#include "App.h"
#include "Logger.h"
#include "Monitors.h"

namespace fusion {

static const wchar_t* kPipeName = L"\\\\.\\pipe\\FusionHP.ipc.v1";
static const UINT WM_APP_STUDIOEXIT = WM_APP + 7;

static void UnhandledSeh(unsigned int code, EXCEPTION_POINTERS* ep) {
    fusion::Logger::LogException("core.seh", "bucle de mensajes", code, ep);
}

int App::Run(HINSTANCE inst, int showCmd) {
    inst_ = inst;
    Logger::Init();
    _set_se_translator(UnhandledSeh);   // SEH → log estructurado [SPEC §11.2.1]

    // ---- PASO 1-3: detección de entorno [SPEC §4.1] ----
    env_ = Environment::Detect();
    Logger::Info("core.bootstrap", "entorno: " + ToUtf8(env_.Summary()));
    Logger::Info("core.bootstrap", std::string("perfil activo: ") + ToUtf8(env_.profile) +
                 (env_.portable ? " (portable)" : " (instalado)"));

    // ---- Salida de proyección (siempre presente) ----
    int outMonitor = Monitors::DefaultOutputIndex();
    if (!live_.Create(outMonitor, L"Fusion HP — Salida")) {
        Logger::Error("core.bootstrap", "no se pudo crear la ventana de salida; cerrando");
        return 10;
    }
    live_.Bind(&state_);
    live_.SetVideoPlayer(&video_);
    live_.GetRenderer().SetLogoPath(DataDir() + L"\\logo.png");
    live_.RenderNow();   // pantalla de reposo negra inicial [SPEC §6.1.3]

    // ---- EL MOTOR (v2.2): dueño del estado vivo del programa ----
    // Se crea ANTES del IPC: cualquier comando motor.* encuentra el Motor listo.
    // Recupera el último servicio persistido (sesion.json): el programa vive en
    // el Motor, no en la GUI — si la GUI se cierra, la proyección continúa.
    motor_ = std::make_unique<Motor>();
    motor_->ApplySlideJson = [this](const Json& j) { ApplySlide(j); };
    motor_->ApplyBlank = [this](const std::string& m) { ApplyBlank(m); };
    motor_->PreloadSlide = [this](const Json& s) { PreloadSlideJson(s); };
    motor_->Broadcast = [this] { BroadcastMotorState(); };
    {
        std::wstring mdir = DataDir() + L"\\motor";
        CreateDirectoryW(mdir.c_str(), nullptr);          // idempotente
        motorSesionFile_ = mdir + L"\\sesion.json";
        motor_->SetPersistFile(motorSesionFile_);
    }
    if (motor_->LoadPersisted(motorSesionFile_))
        Logger::Info("core.motor", "programa anterior recuperado (motor/sesion.json)");
    live_.KeyHook = [this](UINT vk) { if (motor_) motor_->StandaloneKey(vk); };
    bootTick_ = GetTickCount();

    // ---- IPC ipc.v1 ----
    IpcContext ctx;
    ctx.state = &state_;
    ctx.dispatch = [this](const std::string& c, const Json& p) { return Dispatch(c, p); };
    ipc_ = std::make_unique<IpcServer>(std::move(ctx));
    ipc_->Start(kPipeName);

    // ---- PASO 4-5: estrategia por perfil ----
    bool launched = false;
    if (env_.net == NetRuntime::Net4x) {
        launched = RunManaged(L"FusionStudio.exe");
    } else if (env_.net == NetRuntime::Net35) {
        launched = RunManaged(L"FusionStudio.Lite.exe");
    }
    if (!launched && env_.net != NetRuntime::None) {
        Logger::Warn("core.bootstrap", "la capa administrada no pudo iniciarse; usando control nativo");
    }
    if (!launched) {
        // Perfil C: UI mínima de emergencia nativa [SPEC §4.2]
        nativeCtl_ = std::make_unique<NativeControl>(&state_, &live_, &video_);
        if (!nativeCtl_->Create()) Logger::Error("core.native", "no se pudo crear el control nativo");
    }

    // Bombeo de eventos de video (loop/fin) + vigilancia del Motor cada 250 ms
    pumpTimer_ = SetTimer(nullptr, 0, 250, nullptr);

    // ---- Bomba de mensajes ----
    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0) > 0) {
        if (msg.message == WM_TIMER && msg.wParam == pumpTimer_) {
            video_.PumpEvents();
            WatchStandalone();
            continue;
        }
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    if (pumpTimer_) KillTimer(nullptr, pumpTimer_);
    video_.Stop();
    if (ipc_) ipc_->Stop();
    live_.Destroy();
    Logger::Info("core.bootstrap", "nucleo cerrado correctamente");
    return 0;
}

int App::RunManaged(const std::wstring& exe) {
    std::wstring path = ExeDir() + L"\\" + exe;
    DWORD a = GetFileAttributesW(path.c_str());
    if (a == INVALID_FILE_ATTRIBUTES) {
        Logger::Warn("core.bootstrap", ToUtf8(exe) + " no existe junto al nucleo");
        return 0;
    }

    STARTUPINFOW si = {};
    si.cb = sizeof(si);
    PROCESS_INFORMATION pi = {};
    std::wstring cmd = L"\"" + path + L"\" --core-alive";
    std::vector<wchar_t> buf(cmd.begin(), cmd.end());
    buf.push_back(L'\0');
    // hereda handles para que Studio detecte el cierre del nucleo
    BOOL ok = CreateProcessW(path.c_str(), buf.data(), nullptr, nullptr, FALSE,
                             0, nullptr, ExeDir().c_str(), &si, &pi);
    if (!ok) {
        Logger::Warn("core.bootstrap", "CreateProcess fallo para " + ToUtf8(exe) +
                     " (err=" + std::to_string(GetLastError()) + ")");
        return 0;
    }
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    studioRunning_ = true;
    Logger::Info("core.bootstrap", ToUtf8(exe) + " lanzado (perfil " + ToUtf8(env_.profile) + ")");
    return 1;
}

void App::ApplySlide(const Json& slideJson) {
    Slide s;
    if (!SlideState::ParseSlide(slideJson, s)) return;

    if (s.kind == SlideKind::Video) {
        ShowVideo(s);
        state_.Set(s);
        state_.SetBlank(BlankMode::None);
        live_.RenderNow();
        return;
    }
    if (video_.IsPlaying()) video_.Stop();   // carga diferida: liberar el anterior
    state_.Set(s);
    state_.SetBlank(BlankMode::None);
    live_.RenderNow();
    BroadcastState();
}

void App::ShowVideo(const Slide& s) {
    HWND vh = live_.VideoHwnd();
    if (!video_.Play(s.media.src, vh, s.media.loop, s.media.volume, s.media.startAt)) {
        // Fail-safe [SPEC §6.5.4]: fondo del tema + aviso en el monitor del operador
        Logger::Error("core.media", "fail-safe de video activado para: " + ToUtf8(s.media.src));
        Slide fs = s;
        fs.kind = SlideKind::Text;
        fs.lines = {L"(El video no se pudo reproducir)", s.media.src};
        fs.style.color = 0xFFE0E0E0;
        fs.style.activeColor = 0xFFFFB0B0;
        state_.Set(fs);
        state_.SetBlank(BlankMode::None);
        live_.RenderNow();
        if (ipc_) {
            Json warn = {{"code", "video_failsafe"}, {"src", ToUtf8(s.media.src)},
                         {"message", "El video no se pudo reproducir. La salida muestra el fondo del tema."}};
            ipc_->BroadcastEvent("warning", warn);
        }
        return;
    }
    BroadcastState();
}

void App::BroadcastState() {
    if (!ipc_) return;
    ipc_->BroadcastEvent("state", state_.StateJson());
}

void App::BroadcastMotorState() {
    if (!ipc_ || !motor_) return;
    ipc_->BroadcastEvent("motor", motor_->StateJson());
}

void App::ApplyBlank(const std::string& mode) {
    if (video_.IsPlaying() && mode != "none") video_.Pause();
    if (video_.IsPlaying() && mode == "none") video_.Resume();
    state_.SetBlank(mode == "black" ? BlankMode::Black :
                    mode == "logo" ? BlankMode::Logo :
                    mode == "theme" ? BlankMode::Theme :
                    mode == "clear" ? BlankMode::Clear : BlankMode::None);
    live_.RenderNow();
    BroadcastState();
}

void App::PreloadSlideJson(const Json& slide) {
    Slide s;
    if (!SlideState::ParseSlide(slide, s)) return;
    if (!s.bg.image.empty()) renderer_preload_ = s.bg.image;
    if (!s.media.src.empty() && s.kind == SlideKind::Image) renderer_preload2_ = s.media.src;
    live_.GetRenderer().PreloadImage(renderer_preload_);
    if (!renderer_preload2_.empty()) live_.GetRenderer().PreloadImage(renderer_preload2_);
}

void App::WatchStandalone() {
    if (!motor_) return;
    int clients = ipc_ ? ipc_->ClientCount() : 0;
    if (clients > 0) {
        guiEverConnected_ = true;
        if (standalone_) {
            standalone_ = false;
            motor_->SetStandaloneKeys(false);
            live_.SetStandalone(false);
            Logger::Info("core.motor", "GUI reconectada: teclado devuelto al operador");
        }
        return;
    }
    // Perfil C tiene su propia UI nativa: nunca compite con el Motor.
    if (nativeCtl_) return;
    if (!motor_->HasProgram()) return;
    bool grace = guiEverConnected_ || (GetTickCount() - bootTick_ > 6000);
    if (grace && !standalone_) {
        standalone_ = true;
        motor_->SetStandaloneKeys(true);
        live_.SetStandalone(true);
        Logger::Info("core.motor", "sin GUI: motor autonomo (teclado sobre la salida)");
    }
}

Json App::Dispatch(const std::string& cmd, const Json& p) {
    Json ok = {{"ok", true}};

    if (cmd.rfind("motor.", 0) == 0) return DispatchMotor(cmd, p);

    if (cmd == "show") {
        if (p.contains("slide")) ApplySlide(p["slide"]);
        else ok["ok"] = false, ok["error"] = "show requiere payload.slide";
        return ok;
    }
    if (cmd == "preload") {
        // Carga diferida [SPEC §6.4]: decodifica el fondo/imagen del SIGUIENTE
        // elemento sin dibujarlo; la conmutación queda en intercambio rápido.
        if (p.contains("slide")) PreloadSlideJson(p["slide"]);
        return ok;
    }
    if (cmd == "line") {
        int n = p.value("index", -1);
        state_.SetActiveLine(n);
        live_.RenderNow();
        BroadcastState();
        return ok;
    }
    if (cmd == "blank") {
        std::string mode = p.value("mode", std::string("none"));
        ApplyBlank(mode);
        return ok;
    }
    if (cmd == "clear") {
        if (video_.IsPlaying()) video_.Stop();
        state_.SetBlank(BlankMode::Black);
        live_.RenderNow();
        BroadcastState();
        return ok;
    }
    if (cmd == "video") {
        std::string action = p.value("action", std::string(""));
        if (action == "pause") video_.Pause();
        else if (action == "resume") video_.Resume();
        else if (action == "stop") video_.Stop();
        else if (action == "volume") video_.SetVolume(p.value("value", 100));
        return ok;
    }
    if (cmd == "monitors") {
        ok["data"] = {{"monitors", Monitors::ToJsonArray()}};
        return ok;
    }
    if (cmd == "monitor") {
        int idx = p.value("index", -1);
        std::string which = p.value("output", std::string("public"));
        if (which == "stage") {
            if (!live_.StageHwnd()) live_.CreateStage(idx);
        } else {
            live_.ShowOnMonitor(idx);
        }
        return ok;
    }
    if (cmd == "env") {
        ok["data"] = env_.ToJson();
        return ok;
    }
    if (cmd == "state") {
        ok["data"] = state_.StateJson();
        return ok;
    }
    if (cmd == "quit") {
        Logger::Info("core.ipc", "cierre solicitado por la capa administrada");
        PostQuitMessage(0);
        return ok;
    }
    if (cmd == "blanklogo") {   // actualizar ruta de logo en caliente
        if (p.contains("path")) live_.GetRenderer().SetLogoPath(ToWide(p["path"].get<std::string>()));
        return ok;
    }
    return {{"ok", false}, {"error", "comando desconocido: " + cmd}};
}

// ------------------------------------------------------------ familia motor.*
Json App::DispatchMotor(const std::string& cmd, const Json& p) {
    if (!motor_) return {{"ok", false}, {"error", "motor no disponible"}};
    Json ok = {{"ok", true}};
    std::string err;

    if (cmd == "motor.load") {
        if (!motor_->LoadProgram(p, &err)) return {{"ok", false}, {"error", err}};
        return ok;
    }
    if (cmd == "motor.append") {
        if (!motor_->AppendScenario(p, &err)) return {{"ok", false}, {"error", err}};
        return ok;
    }
    if (cmd == "motor.clear") { motor_->Clear(); return ok; }
    if (cmd == "motor.next")  { motor_->Next(); return ok; }
    if (cmd == "motor.prev")  { motor_->Prev(); return ok; }
    if (cmd == "motor.nextElement") { motor_->NextElement(); return ok; }
    if (cmd == "motor.prevElement") { motor_->PrevElement(); return ok; }
    if (cmd == "motor.goto") {
        int s = p.value("scenario", -1), e = p.value("element", -1), l = p.value("line", 0);
        if (!motor_->Goto(s, e, l)) return {{"ok", false}, {"error", "seleccion invalida"}};
        return ok;
    }
    if (cmd == "motor.line") {
        if (!motor_->SetLine(p.value("index", -1)))
            return {{"ok", false}, {"error", "linea fuera de rango"}};
        return ok;
    }
    if (cmd == "motor.blank") { motor_->SetBlank(p.value("mode", std::string("none"))); return ok; }
    if (cmd == "motor.advance") { motor_->SetAdvance(p.value("mode", std::string("line"))); return ok; }
    if (cmd == "motor.highlight") {
        std::vector<std::string> words;
        if (p.contains("words") && p["words"].is_array())
            for (const auto& w : p["words"])
                if (w.is_string()) words.push_back(w.get<std::string>());
        motor_->Highlight(words);
        return ok;
    }
    if (cmd == "motor.state") {
        ok["data"] = motor_->StateJson();
        return ok;
    }
    return {{"ok", false}, {"error", "comando motor desconocido: " + cmd}};
}

} // namespace fusion
