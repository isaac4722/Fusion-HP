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
using Fusion.Studio.Ui.Widgets;

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

        /// <summary>Aplica cambios de configuración (desde SettingsForm) y al arrancar
        /// [v3.0.0 — bug «el modo API no funciona»]: antes la API solo se iniciaba al
        /// guardar Configuración; ahora también se aplica al abrir la app.</summary>
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

            // ---- [v3.0.0 — bug «ventana de proyección»] El monitor elegido en
            // Configuración AHORA se aplica al núcleo (antes se guardaba y nunca
            // se enviaba: la salida ignoraba la selección del operador).
            try
            {
                if (Settings.PublicMonitor >= 0)
                {
                    var mp = JsonValue.Object();
                    mp.Set("index", JsonValue.Make(Settings.PublicMonitor));
                    mp.Set("output", JsonValue.Make("public"));
                    Live.PostCore("monitor", mp);
                }
                if (Settings.StageMonitor >= 0)
                {
                    var ms = JsonValue.Object();
                    ms.Set("index", JsonValue.Make(Settings.StageMonitor));
                    ms.Set("output", JsonValue.Make("stage"));
                    Live.PostCore("monitor", ms);
                }
            }
            catch { }

            // ---- API de control remoto (6 endpoints) + cliente OBS WebSocket
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
#if !LITE
            ApplyObs();
#endif

            // ---- Pantalla de reposo del logo
            if (!string.IsNullOrEmpty(Settings.LogoPath))
            {
                var lp = JsonValue.Object();
                lp.Set("path", JsonValue.Make(Settings.LogoPath));
                Live.PostCore("blanklogo", lp);
            }

            UpdateApiButton();
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
#if !LITE
            StopObs();
#endif
        }

        // ------------------------------------------------------------ API rápida (v3.0.0)
        void ToggleApi()
        {
            Settings.ApiEnabled = !Settings.ApiEnabled;
            Settings.Save();
            ApplySettings();
        }

        void UpdateApiButton()
        {
            if (btnApi == null) return;
            btnApi.Text = Settings.ApiEnabled
                ? "API activa :" + Settings.ApiPort + " (OBS y móvil)"
                : "API para OBS y móvil apagada";
            var k = Settings.ApiEnabled ? FusionButtonKind.Active : FusionButtonKind.Chip;
            if (btnApi.Kind != k) { btnApi.Kind = k; btnApi.Invalidate(); }
        }
    }
}
