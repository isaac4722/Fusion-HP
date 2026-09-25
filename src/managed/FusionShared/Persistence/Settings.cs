// ============================================================================
//  Fusion-HP · FusionShared/Persistence/Settings.cs — configuración de usuario
//  Archivos JSON en %APPDATA%\FusionHP (o carpeta portable) [SPEC §4.4].
//  NUNCA el Registro [REQ][SPEC §11.4].
// ============================================================================
using System;
using System.IO;

namespace Fusion.Shared
{
    /// <summary>Configuración persistida como settings.json.</summary>
    public class AppSettings
    {
        public string DataDir;
        public bool Portable;

        // Pantallas [SPEC §8.5]
        public int PublicMonitor = -1;          // -1 = automático (segundo monitor)
        public int StageMonitor = -1;           // -1 = desactivado
        public bool MultiviewWindow;

        // API HTTP [SPEC §8.1]
        public bool ApiEnabled;
        public int ApiPort = 27117;
        public string ApiToken = "";            // autogenerado al activar


        // Comportamiento
        public string RestScreen = "black";     // negro|logo|theme
        public string LogoPath = "";
        public bool StartInPresentMode = true;  // [SPEC §6.5.3]
        public string LastProjectPath = "";
        public int MaxApiClients = 8;           // [SPEC §8.1.4]

        // Diagnóstico
        public int LogLevel = 2;                // 0 ERROR..3 DEBUG
        public string AdvanceMode = "line";     // line | slide (referencia web)
        public bool ShowClock = true;           // reloj en la consola
        public bool Animation = true;           // transiciones animadas (fade/slide)
        public string DefaultTransition = "fade";   // cut | fade | slide
        // Motor (v2.2): al cerrar la GUI, el Motor sigue proyectando el programa
        // cargado (comportamiento beta 1) con teclado sobre la salida. Alt+F4
        // sobre la salida lo apaga. Si es false, cerrar la GUI apaga todo.
        public bool KeepEngineAlive = true;

        // OBS Studio [SPEC §8.4] — restaurado en v3.0.0 (WebSocket 5.x, obs-websocket):
        // cambiar escenas desde Triggers y desde la consola. Desactivado por defecto.
        public bool ObsEnabled;
        public string ObsHost = "127.0.0.1";
        public int ObsPort = 4455;
        public string ObsPassword = "";

        public static AppSettings Load()
        {
            var s = new AppSettings();
            s.Portable = Directory.Exists(AppDomain.CurrentDomain.BaseDirectory + "datos") ||
                         File.Exists(AppDomain.CurrentDomain.BaseDirectory + "portable.flag");
            s.DataDir = s.Portable
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "datos")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FusionHP");
            try
            {
                Directory.CreateDirectory(s.DataDir);
                string p = Path.Combine(s.DataDir, "settings.json");
                if (File.Exists(p))
                {
                    var j = Json.ParseFile(p);
                    s.PublicMonitor = j.GetInt("publicMonitor", -1);
                    s.StageMonitor = j.GetInt("stageMonitor", -1);
                    s.MultiviewWindow = j.GetBool("multiviewWindow", false);
                    s.ApiEnabled = j.GetBool("apiEnabled", false);
                    s.ApiPort = j.GetInt("apiPort", 27117);
                    s.ApiToken = j.GetStr("apiToken", "");
                    s.RestScreen = j.GetStr("restScreen", "black");
                    s.LogoPath = j.GetStr("logoPath", "");
                    s.StartInPresentMode = j.GetBool("startInPresentMode", true);
                    s.LastProjectPath = j.GetStr("lastProjectPath", "");
                    s.MaxApiClients = j.GetInt("maxApiClients", 8);
                    s.LogLevel = j.GetInt("logLevel", 2);
                    s.AdvanceMode = j.GetStr("advanceMode", "line");
                    s.ShowClock = j.GetBool("showClock", true);
                    s.Animation = j.GetBool("animation", true);
                    s.DefaultTransition = j.GetStr("defaultTransition", "fade");
                    s.KeepEngineAlive = j.GetBool("keepEngineAlive", true);
                    s.ObsEnabled = j.GetBool("obsEnabled", false);
                    s.ObsHost = j.GetStr("obsHost", "127.0.0.1");
                    s.ObsPort = j.GetInt("obsPort", 4455);
                    s.ObsPassword = j.GetStr("obsPassword", "");
                }
            }
            catch
            {
                // Configuración corrupta → valores por defecto, sin romper el arranque
            }
            return s;
        }

        public void Save()
        {
            try
            {
                var j = JsonValue.Object();
                j.Set("publicMonitor", JsonValue.Make(PublicMonitor));
                j.Set("stageMonitor", JsonValue.Make(StageMonitor));
                j.Set("multiviewWindow", JsonValue.Make(MultiviewWindow));
                j.Set("apiEnabled", JsonValue.Make(ApiEnabled));
                j.Set("apiPort", JsonValue.Make(ApiPort));
                j.Set("apiToken", JsonValue.Make(ApiToken));
                j.Set("restScreen", JsonValue.Make(RestScreen));
                j.Set("logoPath", JsonValue.Make(LogoPath));
                j.Set("startInPresentMode", JsonValue.Make(StartInPresentMode));
                j.Set("lastProjectPath", JsonValue.Make(LastProjectPath));
                j.Set("maxApiClients", JsonValue.Make(MaxApiClients));
                j.Set("logLevel", JsonValue.Make(LogLevel));
                j.Set("advanceMode", JsonValue.Make(AdvanceMode));
                j.Set("showClock", JsonValue.Make(ShowClock));
                j.Set("animation", JsonValue.Make(Animation));
                j.Set("defaultTransition", JsonValue.Make(DefaultTransition));
                j.Set("keepEngineAlive", JsonValue.Make(KeepEngineAlive));
                j.Set("obsEnabled", JsonValue.Make(ObsEnabled));
                j.Set("obsHost", JsonValue.Make(ObsHost));
                j.Set("obsPort", JsonValue.Make(ObsPort));
                j.Set("obsPassword", JsonValue.Make(ObsPassword));
                Json.WriteFile(Path.Combine(DataDir, "settings.json"), j);
            }
            catch
            {
                // Sin interrumpir la operación por fallos de guardado
            }
        }

        public string SongsPath { get { return Path.Combine(DataDir, "songs"); } }
        public string BiblesPath { get { return Path.Combine(DataDir, "bibles"); } }
        public string MediaPath { get { return Path.Combine(DataDir, "media"); } }
        public string ProjectsPath { get { return Path.Combine(DataDir, "proyectos"); } }
        public string LogsPath { get { return Path.Combine(DataDir, "logs"); } }
    }
}
