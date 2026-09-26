// ============================================================================
//  Fusion-HP · MainForm.Services.cs — servicios locales de la GUI:
//  aplicación de la configuración (monitores de salida, logo de reposo,
//  avance y transiciones) al arrancar y al guardar Configuración.
//  v4.0.0: SIN API de red, SIN OBS y SIN control remoto — eliminados por
//  decisión del usuario (no queda registro de ellos en el producto). El
//  programa funciona 100% local: IPC interno ipc.v1 hacia el núcleo C++.
// ============================================================================
using System;
using Fusion.Shared;
using Fusion.Studio.Ui.Widgets;

namespace Fusion.Studio.Ui
{
    public partial class MainForm
    {
        /// <summary>Aplica cambios de configuración (desde SettingsForm) y al arrancar
        /// [v3.0.0 — bug «ventana de proyección»]: el monitor de salida elegido se
        /// envía al núcleo al abrir la app, sin pasar por Configuración.</summary>
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

            // ---- Pantalla de reposo del logo
            if (!string.IsNullOrEmpty(Settings.LogoPath))
            {
                var lp = JsonValue.Object();
                lp.Set("path", JsonValue.Make(Settings.LogoPath));
                Live.PostCore("blanklogo", lp);
            }
            // v4.2.0: la preview del reposo «logo» dibuja el logo real
            SlidePreview.LogoPath = Settings.LogoPath;

            UpdateStatus();
        }
    }
}
