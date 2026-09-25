// ============================================================================
//  Fusion-HP · MainForm.Services.cs — servicios de plataforma [SPEC §8]:
// API HTTP de control remoto y motor de Triggers, activados desde la
// configuración (desactivados por defecto [SPEC §8]) e iniciados bajo demanda
// [SPEC §10.3.5].
// v2.3: el cliente OBS fue ELIMINADO por decisión del usuario — queda solo la
// API de control remoto (HTTP + /remote).
// ============================================================================
using System;
using Fusion.Shared;
using Fusion.Studio.Services;

namespace Fusion.Studio.Ui
{
    public partial class MainForm
    {
        ApiServer api;
        TriggerEngine triggers;

        public TriggerEngine Triggers
        {
            get
            {
                if (triggers == null)
                {
                    triggers = new TriggerEngine();
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
            // ---- presentación (referencia web)
            if (lblClock != null) lblClock.Visible = Settings.ShowClock;
            UpdateAdvanceButton();
            UpdateLiveButtons();

            // ---- Motor: avance y transiciones viven en el núcleo → sincronizar
            // (re-entrega el programa con la transición efectiva nueva)
            try
            {
                Live.SetAdvance(Settings.AdvanceMode);
                if (Live.Project != null) Live.SendCurrent();
            }
            catch { }

            // ---- API de control remoto
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
        }
    }
}
