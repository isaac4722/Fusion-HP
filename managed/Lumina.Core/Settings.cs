// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Settings : ajustes portables de la aplicación en JSON junto al ejecutable
//  (carpeta "data"), sin registro de Windows ni rutas de usuario — requisito
//  de portabilidad del proyecto (un solo directorio copiable).
//  Contenido: {apiPort, apiToken, theme, lastBibleVersion, themeJson,
//              obs*, remote*, midi*, stageScreen, autoAdvanceVideo, triggersEnabled}.
//  BasePath: derivado de Environment.GetCommandLineArgs()[0] (portable incluso
//  si el exe se lanza por ruta relativa); sobreescribible para tests.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using lumina.core;

namespace lumina.core
{
    public sealed class Settings
    {
        private const int DefaultPort = 8069;
        private readonly string _dataDir;

        public int ApiPort = DefaultPort;            // puerto del ApiServer (1024..65535)
        public string ApiToken = string.Empty;       // token opcional ("" = sin token)
        public string Theme = "Predeterminado";      // tema activo (nombre)
        public string LastBibleVersion = string.Empty; // última versión bíblica usada
        public string ThemeJson = string.Empty;      // tema completo serializado (v4.1.0; "" = default)

        // ---- v5.0.0 «SINERGIA» ----
        public string ObsUrl = "ws://127.0.0.1:4455"; // obs-websocket 5.x
        public string ObsPassword = string.Empty;
        public string ObsTextSource = string.Empty;   // fuente de texto OBS para letra en vivo
        public bool ObsAutoConnect = false;           // conectar al arrancar
        public int RemotePort = 8070;                 // control remoto LAN (TcpListener)
        public bool RemoteEnabled = false;
        public string RemoteToken = string.Empty;     // recomendado SIEMPRE en LAN
        public bool MidiEnabled = false;              // entrada MIDI → activadores
        public int MidiDevice = 0;
        public int StageScreen = 1;                   // pantalla del monitor de escenario
        public bool AutoAdvanceVideo = true;          // al terminar un video → siguiente slide
        public bool TriggersEnabled = true;           // motor de activadores

        public string TriggersFile
        {
            get { return Path.Combine(Path.Combine(_dataDir, "triggers"), "triggers.json"); }
        }

        public Settings() : this(null) {}

        /// <summary>baseDirOverride: para tests (directorio temporal).</summary>
        public Settings(string baseDirOverride)
        {
            string baseDir = baseDirOverride;
            if (string.IsNullOrEmpty(baseDir))
            {
                baseDir = DefaultBaseDir();
            }
            _dataDir = Path.Combine(baseDir, "data");
        }

        /// <summary>Directorio del ejecutable (portable). Nunca null.</summary>
        public static string DefaultBaseDir()
        {
            try
            {
                string exe = Environment.GetCommandLineArgs()[0];
                string dir = Path.GetDirectoryName(Path.GetFullPath(exe));
                if (!string.IsNullOrEmpty(dir)) return dir;
            }
            catch (Exception)
            {
                // GetCommandLineArgs vacío o ruta rara (hosting raro): fallback.
            }
            return AppDomain.CurrentDomain.BaseDirectory ?? ".";
        }

        public string DataDir { get { return _dataDir; } }
        public string FilePath { get { return Path.Combine(_dataDir, "settings.json"); } }

        public void Normalize()
        {
            if (ApiPort < 1024) ApiPort = 1024;
            if (ApiPort > 65535) ApiPort = 65535;
            if (ApiToken == null) ApiToken = string.Empty;
            if (Theme == null) Theme = "Predeterminado";
            if (LastBibleVersion == null) LastBibleVersion = string.Empty;
            if (ThemeJson == null) ThemeJson = string.Empty;
            if (ObsUrl == null) ObsUrl = "ws://127.0.0.1:4455";
            if (ObsPassword == null) ObsPassword = string.Empty;
            if (ObsTextSource == null) ObsTextSource = string.Empty;
            if (RemotePort < 1024) RemotePort = 1024;
            if (RemotePort > 65535) RemotePort = 65535;
            if (RemotePort == ApiPort) RemotePort = ApiPort + 1 > 65535 ? 8070 : ApiPort + 1;
            if (RemoteToken == null) RemoteToken = string.Empty;
            if (MidiDevice < 0) MidiDevice = 0;
            if (StageScreen < 0) StageScreen = 0;
        }

        /// <summary>Carga desde el directorio dado (o default). Tolerante a errores: devuelve defaults.</summary>
        public static Settings Load()
        {
            return Load(null);
        }

        public static Settings Load(string baseDirOverride)
        {
            Settings s = new Settings(baseDirOverride);
            try
            {
                if (!File.Exists(s.FilePath)) return s;
                Dictionary<string, object> o = MiniJson.Parse(File.ReadAllText(s.FilePath, new UTF8Encoding(false)));
                s.ApiPort = (int)MiniJson.GetInt(o, "apiPort", DefaultPort);
                s.ApiToken = MiniJson.GetString(o, "apiToken", string.Empty);
                s.Theme = MiniJson.GetString(o, "theme", "Predeterminado");
                s.LastBibleVersion = MiniJson.GetString(o, "lastBibleVersion", string.Empty);
                s.ThemeJson = MiniJson.GetString(o, "themeJson", string.Empty);
                s.ObsUrl = MiniJson.GetString(o, "obsUrl", s.ObsUrl);
                s.ObsPassword = MiniJson.GetString(o, "obsPassword", string.Empty);
                s.ObsTextSource = MiniJson.GetString(o, "obsTextSource", string.Empty);
                s.ObsAutoConnect = MiniJson.GetBool(o, "obsAutoConnect", false);
                s.RemotePort = (int)MiniJson.GetInt(o, "remotePort", 8070);
                s.RemoteEnabled = MiniJson.GetBool(o, "remoteEnabled", false);
                s.RemoteToken = MiniJson.GetString(o, "remoteToken", string.Empty);
                s.MidiEnabled = MiniJson.GetBool(o, "midiEnabled", false);
                s.MidiDevice = (int)MiniJson.GetInt(o, "midiDevice", 0);
                s.StageScreen = (int)MiniJson.GetInt(o, "stageScreen", 1);
                s.AutoAdvanceVideo = MiniJson.GetBool(o, "autoAdvanceVideo", true);
                s.TriggersEnabled = MiniJson.GetBool(o, "triggersEnabled", true);
                s.Normalize();
            }
            catch (Exception)
            {
                // Archivo corrupto/ilegible: se conservan los defaults (nunca bloquear el arranque).
                s = new Settings(baseDirOverride);
            }
            return s;
        }

        /// <summary>Guarda el JSON (crea la carpeta "data" si falta).</summary>
        public void Save()
        {
            Normalize();
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["apiPort"] = ApiPort;
            o["apiToken"] = ApiToken;
            o["theme"] = Theme;
            o["lastBibleVersion"] = LastBibleVersion;
            if (ThemeJson.Length > 0) o["themeJson"] = ThemeJson;   // v4.1.0: tema completo
            o["obsUrl"] = ObsUrl;                                   // v5.0.0
            if (ObsPassword.Length > 0) o["obsPassword"] = ObsPassword;
            if (ObsTextSource.Length > 0) o["obsTextSource"] = ObsTextSource;
            o["obsAutoConnect"] = ObsAutoConnect;
            o["remotePort"] = RemotePort;
            o["remoteEnabled"] = RemoteEnabled;
            if (RemoteToken.Length > 0) o["remoteToken"] = RemoteToken;
            o["midiEnabled"] = MidiEnabled;
            o["midiDevice"] = MidiDevice;
            o["stageScreen"] = StageScreen;
            o["autoAdvanceVideo"] = AutoAdvanceVideo;
            o["triggersEnabled"] = TriggersEnabled;
            string dir = _dataDir;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, MiniJson.Serialize(o) + "\n", new UTF8Encoding(false));
        }

        /// <summary>Culture-neutral para logs (sin efectos).</summary>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Settings(port={0}, token={1}, theme={2}, bible={3})",
                ApiPort, ApiToken.Length > 0 ? "***" : "(sin)", Theme, LastBibleVersion);
        }
    }
}
