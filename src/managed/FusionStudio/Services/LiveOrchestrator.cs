// ============================================================================
//  Fusion-HP · FusionStudio/Services/LiveOrchestrator.cs — cerebro del modo
//  Live [SPEC §6]: mantiene el proyecto activo, la selección (escenario,
//  elemento, línea), resuelve la herencia de 4 niveles y envía el estado al
//  núcleo por ipc.v1. Carga diferida: resuelve el siguiente elemento y lo
//  precarga [SPEC §6.4]. Tema en caliente: re-resuelve y re-envía [SPEC §7.4].
//  NO genera PPTX para proyectar: proyecta desde la biblioteca (fix del
//  prototipo anterior).
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
        public bool IsBlank = true;         // arranque en reposo [SPEC §6.1.3]
        public string BlankMode = "black";
        public ResolvedSlide Current;
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

        /// <summary>Notifica a la UI cambios de selección/estado (hilo UI via Marshal).</summary>
        public event Action StateChanged;
        /// <summary>Evento del núcleo (advertencias, estado).</summary>
        public event Action<string, JsonValue> CoreEvent;

        public bool CoreConnected
        {
            get { return ipc != null && ipc.Connected; }
        }

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
        /// <summary>Conecta al núcleo; si no responde, intenta lanzarlo.</summary>
        public bool ConnectCore(int attempts)
        {
            for (int i = 0; i < attempts; i++)
            {
                if (ipc != null && ipc.Connected) return true;
                if (ipc == null || !ipc.Connected)
                {
                    if (ipc != null) ipc.Dispose();
                    ipc = new IpcClient();
                    ipc.Event += OnCoreEvent;
                    if (ipc.Connect(400)) return true;
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

        void OnCoreEvent(string evt, JsonValue data)
        {
            var h = CoreEvent;
            if (h != null) h(evt, data);
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

        // ---------------------------------------------------------------- proyección
        string BaseDir()
        {
            if (Project != null && !string.IsNullOrEmpty(Project.SourcePath))
                return Path.GetDirectoryName(Project.SourcePath);
            return Settings.ProjectsPath;
        }

        public void SendCurrent()
        {
            if (Project == null) { Blank("black"); return; }
            var scn = CurrentScenario;
            if (scn == null) { Blank(Settings.RestScreen); return; }
            var el = CurrentElement;
            if (el == null) { Blank(Settings.RestScreen); return; }

            var resolved = ResolvedSlide.Resolve(el, scn, Project, BaseDir());
            if (string.IsNullOrEmpty(resolved.Transition)) resolved.Transition = Settings.DefaultTransition;
            if (!Settings.Animation) resolved.Transition = "cut";
            resolved.ActiveLine = State.LineIndex;
            State.Current = resolved;
            State.IsBlank = false;

            var payload = JsonValue.Object();
            payload.Set("slide", resolved.ToIpcJson());
            IpcPost("show", payload);
            PreloadNext();
            FireStateChanged();
        }

        /// <summary>Carga diferida estricta [SPEC §6.4]: precarga el siguiente elemento.</summary>
        void PreloadNext()
        {
            var scn = CurrentScenario;
            if (scn == null || Project == null) return;
            int next = State.ElementIndex + 1;
            if (next < 0 || next >= scn.Elements.Count) return;
            var resolved = ResolvedSlide.Resolve(scn.Elements[next], scn, Project, BaseDir());
            var payload = JsonValue.Object();
            payload.Set("slide", resolved.ToIpcJson());
            IpcPost("preload", payload);
        }

        public void SetLine(int line)
        {
            if (State.Current == null) return;
            if (line < 0 || line >= State.Current.Lines.Count) return;
            State.LineIndex = line;
            var p = JsonValue.Object();
            p.Set("index", JsonValue.Make(line));
            IpcPost("line", p);
            FireStateChanged();
        }

        public void Blank(string mode)
        {
            State.IsBlank = mode != "none";
            State.BlankMode = mode;
            var p = JsonValue.Object();
            p.Set("mode", JsonValue.Make(mode));
            IpcPost("blank", p);
            FireStateChanged();
        }

        /// <summary>
        /// Instala en segundo plano las biblias empaquetadas (RV1960, NVI, RVG,
        /// RVR1909). Fuera del constructor para no frenar el arranque [SPEC §10.1];
        /// en ejecuciones posteriores es una verificación rápida sin trabajo.
        /// </summary>
        public int EnsureBundledBibles()
        {
            return Bibles.EnsureBundled(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(Path.Combine("resources", "data"), "bibles")));
        }

        /// <summary>Tema en caliente [SPEC §7.4.1]: re-resuelve el elemento activo.</summary>
        public void ThemeChanged()
        {
            if (!State.IsBlank) SendCurrent();
        }

        // ---------------------------------------------------------------- navegación
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

        /// <summary>Siguiente línea; al terminar el elemento pasa al siguiente [SPEC §6.2.1].
        /// Con AdvanceMode="slide" (referencia web) avanza el elemento completo.</summary>
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
            if (State.ElementIndex > 0) Select(State.ScenarioIndex, State.ElementIndex - 0 - 1, 0);
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
            State.LineIndex = lineIdx;
            SendCurrent();
        }

        public void GoLive(Scenario scn)
        {
            if (Project == null || scn == null) return;
            int i = Project.Scenarios.IndexOf(scn);
            if (i >= 0) Select(i, scn.Elements.Count > 0 ? 0 : -1, 0);
        }

        /// <summary>Añade un Escenario al final de la lista y lo proyecta [SPEC §7.5.1].</summary>
        public void SendToLive(Scenario scn)
        {
            if (Project == null) Project = AhpProject.CreateDefault();
            if (!Project.Scenarios.Contains(scn)) Project.Scenarios.Add(scn);
            GoLive(scn);
        }

        // ---------------------------------------------------------------- persistencia
        public void SaveProject(string path)
        {
            if (Project == null) return;
            Json.WriteFile(path, Project.ToJson());
            Project.SourcePath = path;
            Settings.LastProjectPath = path;
            Settings.Save();
        }

        public bool LoadProject(string path)
        {
            try
            {
                Project = AhpProject.FromJson(Json.ParseFile(path));
                Project.SourcePath = path;
                State.ScenarioIndex = Project.Scenarios.Count > 0 ? 0 : -1;
                State.ElementIndex = Project.Scenarios.Count > 0 && Project.Scenarios[0].Elements.Count > 0 ? 0 : -1;
                State.LineIndex = 0;
                Blank(Settings.RestScreen);
                Settings.LastProjectPath = path;
                Settings.Save();
                FireStateChanged();
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
