// ============================================================================
//  Fusion-HP · FusionStudio/Services/LiveOrchestrator.cs (v2.2) — la GUI es
// vista/controlador del MOTOR del núcleo. El Motor (C++) posee el estado
// vivo: esta clase construye el programa resuelto (motor.load), envía
// navegación (motor.next/goto/blank/highlight…) y ESPEJA el estado desde los
// eventos motor.state. Si la GUI se cierra, el Motor sigue proyectando;
// al reabrirla, la sincronización reconstruye la posición exacta.
// Resuelve la herencia de 4 niveles [SPEC §5.4] al construir el programa y
// localmente para la previsualización. NO genera PPTX para proyectar.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Fusion.Shared;
using Fusion.Shared.Ipc;
using Fusion.Shared.Model;

namespace Fusion.Studio.Services
{
    public class LiveState
    {
        public int ScenarioIndex = -1;
        public int ElementIndex = -1;
        public int LineIndex;
        public bool IsBlank = true;             // arranque en reposo [SPEC §6.1.3]
        public string BlankMode = "black";
        public ResolvedSlide Current;           // resuelto local para el preview
        public bool HasProgram;                 // el Motor tiene programa cargado
        public string AdvanceMode = "line";     // línea|diapositiva (referencia web)
        public int LineCount { get { return Current != null ? Current.Lines.Count : 0; } }
    }

    public class LiveOrchestrator : IDisposable
    {
        public readonly AppSettings Settings;
        public AhpProject Project;
        public readonly LiveState State = new LiveState();
        IpcClient ipc;
        public readonly Shared.Store.SongStore Songs;
        public readonly Shared.Bible.BibleStore Bibles;
        readonly List<string> highlight = new List<string>();   // capa activa en el Motor

        // [v3.0.0] Proyección externa «tal cual» (PPTX original): el programa en
        // vivo proviene del archivo fuente, no de un proyecto ahp del usuario.
        public string ExternalSource;        // ruta del archivo original en vivo
        public string ExternalBaseDir;       // base para resolver media/ (caché)

        /// <summary>Notifica a la UI cambios de selección/estado (hilo UI via Marshal).</summary>
        public event Action StateChanged;
        /// <summary>Evento del núcleo (advertencias, estado).</summary>
        public event Action<string, JsonValue> CoreEvent;

        public bool CoreConnected
        {
            get { return ipc != null && ipc.Connected; }
        }

        /// <summary>Palabras de resaltado activas en el Motor (para la caja de la GUI).</summary>
        public IList<string> HighlightWords { get { return highlight; } }

        public LiveOrchestrator(AppSettings settings)
        {
            Settings = settings;
            // Banco de cantos en UNA base única (cancionero.fdb): carga una vez,
            // consulta siempre, sin archivos por canto ni duplicados.
            Songs = new Shared.Store.SongStore(settings.DataDir);
            Songs.MigrateLegacy(settings.SongsPath);
            Bibles = new Shared.Bible.BibleStore(settings.DataDir);
        }

        // ---------------------------------------------------------------- núcleo
        /// <summary>Conecta al Motor; si no responde, intenta lanzarlo.</summary>
        public bool ConnectCore(int attempts)
        {
            for (int i = 0; i < attempts; i++)
            {
                if (ipc != null && ipc.Connected) return true;
                if (ipc == null || !ipc.Connected)
                {
                    if (ipc != null) ipc.Dispose();
                    ipc = new IpcClient();
                    ipc.Event += OnIpcEvent;
                    if (ipc.Connect(400))
                    {
                        SyncFromMotor();   // espejo inicial del estado del Motor
                        return true;
                    }
                }
                // Lanzar el núcleo si no está (arranque manual de Studio o fallo)
                if (i == 0)
                {
                    string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FusionHP.exe");
                    if (File.Exists(exe))
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = exe,
                                Arguments = "--core-only",
                                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                                UseShellExecute = false
                            });
                        }
                        catch { }
                    }
                }
                System.Threading.Thread.Sleep(700);
            }
            return ipc != null && ipc.Connected;
        }

        /// <summary>Pide el estado completo al Motor y lo espeja (reconexión/GUI reabierta).</summary>
        public void SyncFromMotor()
        {
            if (ipc == null || !ipc.Connected) return;
            try
            {
                var resp = ipc.Command("motor.state", JsonValue.Object(), 600);
                if (resp != null && resp.GetBool("ok", false))
                {
                    var data = resp.Get("data");
                    if (data != null && data.Type == JsonValue.Kind.Object)
                        ApplyMotorState(data);
                }
            }
            catch { }
        }

        void OnIpcEvent(string evt, JsonValue data)
        {
            if (evt == "motor" && data != null && data.Type == JsonValue.Kind.Object)
                ApplyMotorState(data);
            var h = CoreEvent;
            if (h != null) h(evt, data);
        }

        /// <summary>Espejo del estado del Motor → LiveState (hilo de lectura IPC).</summary>
        void ApplyMotorState(JsonValue st)
        {
            try
            {
                State.ScenarioIndex = st.GetInt("scenario", -1);
                State.ElementIndex = st.GetInt("element", -1);
                State.LineIndex = st.GetInt("line", 0);
                State.BlankMode = st.GetStr("blank", "black");
                State.IsBlank = State.BlankMode != "none";
                State.HasProgram = st.GetBool("hasProgram", false);
                State.AdvanceMode = st.GetStr("advance", "line");
                highlight.Clear();
                foreach (string w in st.GetStringArray("highlight")) highlight.Add(w);
                RefreshCurrentLocal();
            }
            catch { }
            FireStateChanged();
        }

        /// <summary>Resuelve localmente el elemento activo para la previsualización.</summary>
        void RefreshCurrentLocal()
        {
            var scn = CurrentScenario;
            var el = CurrentElement;
            if (scn == null || el == null)
            {
                State.Current = null;
                return;
            }
            var r = ResolvedSlide.Resolve(el, scn, Project, BaseDir());
            if (string.IsNullOrEmpty(r.Transition)) r.Transition = Settings.DefaultTransition;
            if (!Settings.Animation) r.Transition = "cut";
            r.ActiveLine = State.LineIndex;
            State.Current = r;
        }

        void IpcPost(string cmd, JsonValue payload)
        {
            if (ipc != null && ipc.Connected) ipc.Post(cmd, payload);
        }

        /// <summary>Envía un comando directo al núcleo (uso interno de servicios).</summary>
        public void PostCore(string cmd, JsonValue payload)
        {
            IpcPost(cmd, payload);
        }

        // ---------------------------------------------------------------- programa → Motor
        string BaseDir()
        {
            if (Project != null && !string.IsNullOrEmpty(Project.SourcePath))
                return Path.GetDirectoryName(Project.SourcePath);
            return Settings.ProjectsPath;
        }

        /// <summary>Construye el programa resuelto completo y lo entrega al Motor.</summary>
        public void SendProgram(int scnIdx, int elIdx, int lineIdx)
        {
            var payload = MotorPayload.Build(Project, BaseDir(),
                Settings.DefaultTransition, Settings.Animation, Settings.AdvanceMode,
                scnIdx, elIdx, lineIdx, highlight);
            IpcPost("motor.load", payload);
        }

        /// <summary>Reenvía el programa con la selección actual (ediciones, tema en caliente).</summary>
        public void SendCurrent()
        {
            SendProgram(State.ScenarioIndex, State.ElementIndex, State.LineIndex);
        }

        /// <summary>Instala en segundo plano las biblias empaquetadas (RV1960, NVI, RVG,
        /// RVR1909). Fuera del constructor para no frenar el arranque [SPEC §10.1].</summary>
        public int EnsureBundledBibles()
        {
            return Bibles.EnsureBundled(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(Path.Combine("resources", "data"), "bibles")));
        }

        /// <summary>Tema en caliente [SPEC §7.4.1]: re-resuelve el programa entero.</summary>
        public void ThemeChanged()
        {
            SendCurrent();
        }

        // ---------------------------------------------------------------- navegación → Motor
        // Espejo local OPTIMISTA: los índices se actualizan al instante (la GUI
        // y la API siguen útiles sin núcleo) y el comando viaja al Motor, que es
        // la autoridad — su evento motor.state llega después y reconcilia.
        public Scenario CurrentScenario
        {
            get
            {
                if (Project == null || State.ScenarioIndex < 0 || State.ScenarioIndex >= Project.Scenarios.Count)
                    return null;
                return Project.Scenarios[State.ScenarioIndex];
            }
        }

        public Element CurrentElement
        {
            get
            {
                var scn = CurrentScenario;
                if (scn == null || State.ElementIndex < 0 || State.ElementIndex >= scn.Elements.Count) return null;
                return scn.Elements[State.ElementIndex];
            }
        }

        public void NextLine()
        {
            if (Settings.AdvanceMode == "slide") { NextElement(); return; }
            if (State.Current == null) { NextElement(); return; }
            if (State.LineIndex + 1 < State.Current.Lines.Count) SetLine(State.LineIndex + 1);
            else NextElement();
        }

        public void PrevLine()
        {
            if (Settings.AdvanceMode == "slide") { PrevElement(); return; }
            if (State.LineIndex > 0) SetLine(State.LineIndex - 1);
            else PrevElement();
        }

        public void NextElement()
        {
            var scn = CurrentScenario;
            if (scn == null || Project == null)
            {
                if (Project != null && Project.Scenarios.Count > 0) Select(Project.Scenarios.Count - 1, 0, 0);
                return;
            }
            if (State.ElementIndex + 1 < scn.Elements.Count)
                Select(State.ScenarioIndex, State.ElementIndex + 1, 0);
            else if (State.ScenarioIndex + 1 < Project.Scenarios.Count)
                Select(State.ScenarioIndex + 1, 0, 0);
        }

        public void PrevElement()
        {
            if (State.ElementIndex > 0) Select(State.ScenarioIndex, State.ElementIndex - 1, 0);
            else if (State.ScenarioIndex > 0)
            {
                var prev = Project.Scenarios[State.ScenarioIndex - 1];
                Select(State.ScenarioIndex - 1, prev.Elements.Count - 1, 0);
            }
        }

        public void Select(int scenarioIdx, int elementIdx, int lineIdx)
        {
            State.ScenarioIndex = scenarioIdx;
            State.ElementIndex = elementIdx;
            State.LineIndex = lineIdx > 0 ? lineIdx : 0;
            var p = JsonValue.Object();
            p.Set("scenario", JsonValue.Make(scenarioIdx));
            p.Set("element", JsonValue.Make(elementIdx));
            p.Set("line", JsonValue.Make(State.LineIndex));
            IpcPost("motor.goto", p);
            RefreshCurrentLocal();
            FireStateChanged();
        }

        public void SetLine(int line)
        {
            if (State.Current == null) return;
            if (line < 0 || line >= State.Current.Lines.Count) return;
            State.LineIndex = line;
            var p = JsonValue.Object();
            p.Set("index", JsonValue.Make(line));
            IpcPost("motor.line", p);
            RefreshCurrentLocal();
            FireStateChanged();
        }

        public void Blank(string mode)
        {
            State.IsBlank = mode != "none";
            State.BlankMode = mode;
            var p = JsonValue.Object();
            p.Set("mode", JsonValue.Make(mode));
            IpcPost("motor.blank", p);
            FireStateChanged();
        }

        /// <summary>Resaltado en proyección: el Motor lo aplica y persiste.</summary>
        public void SetHighlight(IList<string> words)
        {
            highlight.Clear();
            if (words != null) foreach (string w in words) highlight.Add(w);
            var p = JsonValue.Object();
            var arr = JsonValue.Array();
            foreach (string w in highlight) arr.Add(JsonValue.Make(w));
            p.Set("words", arr);
            IpcPost("motor.highlight", p);
        }

        /// <summary>Avance línea por línea / por diapositiva (referencia web).</summary>
        public void SetAdvance(string mode)
        {
            Settings.AdvanceMode = mode == "slide" ? "slide" : "line";
            Settings.Save();
            var p = JsonValue.Object();
            p.Set("mode", JsonValue.Make(Settings.AdvanceMode));
            IpcPost("motor.advance", p);
        }

        /// <summary>Añade un Escenario al final del proyecto, lo entrega al Motor
        /// y lo proyecta [SPEC §7.5.1].</summary>
        public void SendToLive(Scenario scn)
        {
            if (Project == null) Project = AhpProject.CreateDefault();
            if (!Project.Scenarios.Contains(scn)) Project.Scenarios.Add(scn);
            int i = Project.Scenarios.IndexOf(scn);
            int el = scn.Elements.Count > 0 ? 0 : -1;
            // espejo local optimista (el Motor confirma con motor.state)
            State.ScenarioIndex = i;
            State.ElementIndex = el;
            State.LineIndex = 0;
            SendProgram(i, el, 0);
            Blank("none");   // enviar a pantalla = mostrar
            RefreshCurrentLocal();
        }

        // ---------------------------------------------------------------- persistencia
        public void SaveProject(string path)
        {
            if (Project == null) return;
            Json.WriteFile(path, Project.ToJson());
            Project.SourcePath = path;
            ExternalSource = null;              // dejar de ser proyección externa
            ExternalBaseDir = null;
            Settings.LastProjectPath = path;
            Settings.Save();
        }

        public bool LoadProject(string path)
        {
            try
            {
                Project = AhpProject.FromJson(Json.ParseFile(path));
                Project.SourcePath = path;
                ExternalSource = null;          // un .ahp del usuario reemplaza lo externo
                ExternalBaseDir = null;
                Settings.LastProjectPath = path;
                Settings.PushRecent(path);      // cargador con nombres (v3.0.0)
                Settings.Save();
                // El Motor recibe el programa completo; el reposo manda hasta que
                // el operador envíe (comportamiento beta 1: nada se proyecta solo).
                int scn = Project.Scenarios.Count > 0 ? 0 : -1;
                int el = scn >= 0 && Project.Scenarios[0].Elements.Count > 0 ? 0 : -1;
                State.ScenarioIndex = scn;
                State.ElementIndex = el;
                State.LineIndex = 0;
                SendProgram(scn, el, 0);
                Blank(Settings.RestScreen);
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    "No se pudo abrir el proyecto.\n\n" + ex.Message + "\n\nPuedes verificar que el archivo sea un proyecto .ahp válido y volver a intentarlo.",
                    "Fusion HP", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
                return false;
            }
        }

        // ---------------------------------------------------------------- externo tal cual (v3.0.0)
        /// <summary>Proyecta un contenedor EN MEMORIA (p. ej. el PPTX original leído
        /// tal cual por PptxDirectProjector). Nada se guarda; el Motor recibe el
        /// programa resuelto y la GUI opera sobre él hasta que el operador cargue
        /// otra cosa. baseDir resuelve los media/ de la caché técnica.</summary>
        public void ProjectExternal(AhpProject external, string sourcePath, string baseDir)
        {
            Project = external;
            Project.SourcePath = Path.Combine(baseDir, "proyeccion-viva.ahp");  // solo para resolver media/
            ExternalSource = sourcePath;
            ExternalBaseDir = baseDir;
            Settings.LastProjectPath = null;    // no es un proyecto ahp del usuario
            Settings.Save();
            int scn = Project.Scenarios.Count > 0 ? 0 : -1;
            int el = scn >= 0 && Project.Scenarios[0].Elements.Count > 0 ? 0 : -1;
            State.ScenarioIndex = scn;
            State.ElementIndex = el;
            State.LineIndex = 0;
            SendProgram(scn, el, 0);
            Blank("none");                      // cargar = proyectar (flujo Holyrics)
            RefreshCurrentLocal();
            FireStateChanged();
        }

        void FireStateChanged()
        {
            var h = StateChanged;
            if (h != null) h();
        }

        public void Dispose()
        {
            if (Songs != null) Songs.Dispose();   // asienta cambios pendientes de la base
            if (ipc != null) ipc.Dispose();
            ipc = null;
        }
    }
}
