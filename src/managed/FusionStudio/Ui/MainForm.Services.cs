// ============================================================================
//  Fusion-HP · MainForm.Services.cs — servicios de plataforma [SPEC §8]:
// API HTTP, cliente OBS y motor de Triggers, activados desde la configuración
// (desactivados por defecto [SPEC §8]) e iniciados bajo demanda [SPEC §10.3.5].
// ============================================================================
using System;
using Fusion.Studio.Services;

namespace Fusion.Studio.Ui
{
    public partial class MainForm
    {
        ApiServer api;
        ObsClient obs;
        TriggerEngine triggers;
        bool obsTextHooked;

        public TriggerEngine Triggers
        {
            get
            {
                if (triggers == null)
                {
                    triggers = new TriggerEngine();
                    triggers.ObsSceneChanger = delegate(string scene)
                    {
                        if (obs != null && obs.Connected) { obs.SetScene(scene); return true; }
                        return false;
                    };
                    triggers.ThemeChanger = delegate(string name)
                    {
                        Live.ThemeChanged();       // re-resuelve en caliente [SPEC §7.4.1]
                    };
                    triggers.MessageShower = delegate(string text)
                    {
                        var el = Live.CurrentElement;
                        if (el != null) { el.OverlayText = text; Live.SendCurrent(); }
                    };
                    triggers.Load(Live);
                }
                return triggers;
            }
        }

        /// <summary>Aplica cambios de configuración (desde SettingsForm).</summary>
        public void ApplySettings()
        {
            // ---- API
            if (Settings.ApiEnabled)
            {
                if (api == null)
                {
                    api = new ApiServer(Live, Settings);
                    api.Logged += delegate(string m) { AppendStatusLog(m); };
                }
                api.Start();
            }
            else if (api != null)
            {
                api.Stop();
            }

            // ---- OBS
            if (Settings.ObsEnabled)
            {
                if (obs == null)
                {
                    obs = new ObsClient(Settings);
                    obs.Logged += delegate(string m) { AppendStatusLog(m); };
                }
                if (!obsTextHooked)
                {
                    obsTextHooked = true;
                    // El texto activo se empuja a OBS en cada cambio de línea [SPEC §8.4.3]
                    Live.StateChanged += delegate
                    {
                        if (obs != null && obs.Connected && Live.State.Current != null &&
                            !string.IsNullOrEmpty(Settings.ObsTextSource))
                        {
                            var cur = Live.State.Current;
                            string text = cur.Lines.Count > 0 && Live.State.LineIndex < cur.Lines.Count
                                ? cur.Lines[Live.State.LineIndex] : "";
                            obs.PushText(text);
                        }
                    };
                }
                obs.Start();
            }
            else if (obs != null)
            {
                obs.Stop();
            }

            // ---- Pantalla de reposo del logo
            if (!string.IsNullOrEmpty(Settings.LogoPath))
            {
                var lp = JsonValue.Object();
                lp.Set("path", JsonValue.Make(Settings.LogoPath));
                Live.PostCore("blanklogo", lp);
            }

            UpdateStatus();
        }

        void AppendStatusLog(string m)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(Settings.LogsPath, "studio.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | cs.services | " + m + "\n");
            }
            catch { }
        }

        /// <summary>Suscribir en el constructor (ver MainForm.cs): libera servicios al cerrar.</summary>
        internal void HandleFormClosedForServices(object sender, EventArgs e)
        {
            if (api != null) api.Stop();
            if (obs != null) obs.Stop();
        }
    }
}
