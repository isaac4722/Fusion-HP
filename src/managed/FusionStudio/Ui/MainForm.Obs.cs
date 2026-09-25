// ============================================================================
//  Fusion-HP · FusionStudio/Ui/MainForm.Obs.cs — integración OBS Studio en la
//  ventana principal [v3.0.0, SPEC §8.4]. Aplica la configuración (arranque y
//  SettingsForm), conecta el cliente obs-websocket 5.x y lo expone al motor
//  de Triggers para la acción "obs.scene" (p. ej. etiqueta "lento" → escena
//  "camara-pastor"). Compilado fuera en la variante Lite (net35).
// ============================================================================
#if !LITE
using System;
using Fusion.Studio.Services;

namespace Fusion.Studio.Ui
{
    public partial class MainForm
    {
        ObsClient obs;

        /// <summary>Aplica la configuración OBS (llamado desde ApplySettings).</summary>
        void ApplyObs()
        {
            if (Settings.ObsEnabled)
            {
                if (obs == null)
                {
                    obs = new ObsClient();
                    obs.Logged += delegate(string m) { AppendStatusLog("obs | " + m); };
                    obs.ConnectedChanged += delegate(bool on)
                    {
                        try
                        {
                            BeginInvoke((Action)delegate { UpdateStatus(); });
                        }
                        catch { }
                    };
                }
                obs.Configure(Settings.ObsHost, Settings.ObsPort, Settings.ObsPassword);
                if (!obs.Connected) obs.Start();
                // acción obs.scene disponible para los Triggers [SPEC §8.3.2]
                Triggers.ObsSceneChanger = delegate(string scene) { obs.SetScene(scene); };
            }
            else
            {
                StopObs();
            }
        }

        void StopObs()
        {
            if (obs != null) { obs.Stop(); obs = null; }
        }
    }
}
#endif
