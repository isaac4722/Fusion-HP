// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Settings : ajustes portables de la aplicación en JSON junto al ejecutable
//  (carpeta "data"), sin registro de Windows ni rutas de usuario — requisito
//  de portabilidad del proyecto (un solo directorio copiable).
//  Contenido: {apiPort, apiToken, theme, lastBibleVersion, themeJson,
//              obs*, remote*, midi*, stageScreen, autoAdvanceVideo, triggersEnabled,
//              shortcuts (F1.05: atajos personalizables acción→tecla)}.
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

        // ---- v5.1.0 «FUNDAMENTO» ----
        public int ProjectionScreen = 0;              // pantalla del proyector (combo En Vivo)
        /// <summary>
        /// v7.1.0 «OPERADOR» (feedback #3): proyección a PANTALLA COMPLETA por
        /// defecto (la ventana nativa es SIEMPRE sin bordes; este ajuste decide
        /// si cubre el monitor o se muestra 960×540 centrada).
        /// </summary>
        public bool ProjectionFullscreen = true;
        public int DirectorScreen = 2;                // pantalla del Director (3ª salida)
        public string BackupFolder = string.Empty;    // carpeta de respaldo (Drive/OneDrive local)
        public bool AutoBackupOnExit = false;         // respaldar data\ al cerrar
        // v6.0.0 «HORIZONTE»: transición de proyección (fundido entre slides)
        public bool TransitionFade = true;            // false = corte inmediato
        public int  TransitionMs   = 220;             // 0..5000 ms
        public bool JsEnabled = false;                // v6.1.0 «GUION»: motor de scripts JSLib (WPF)

        // ---- v1.0.0-beta.1 «ULTRA» — F1.05: atajos personalizables ----
        // Diccionario acción→tecla (valores de tecla legibles es-VE). Los
        // nombres de acción son estables (contrato con la UI y los triggers);
        // las teclas son datos, no texto de UI. Defaults de fábrica en
        // DefaultShortcuts(): flechas, Espacio, Enter, F1-F12 (favoritos) y
        // Esc = pantalla de reposo. Persistido bajo "shortcuts" en el JSON
        // existente — los lectores viejos lo IGNORAN (F2.01.8: campo nuevo
        // ignorable hacia atrás) y este lector ignora acciones desconocidas.
        private readonly Dictionary<string, string> _shortcuts =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Catálogo FIJO de acciones de atajo (orden estable para la UI).</summary>
        public static readonly string[] ShortcutActions = new string[]
        {
            "avanzar", "retroceder",
            "avanzarElemento", "retrocederElemento",
            "avanzarLinea", "retrocederLinea",
            "reposo",
            "favorito1", "favorito2", "favorito3", "favorito4", "favorito5",
            "favorito6", "favorito7", "favorito8", "favorito9", "favorito10",
            "favorito11", "favorito12",
        };

        /// <summary>
        /// Defaults de fábrica (F1.05): Espacio avanza, flechas navegan
        /// elemento/línea, Enter confirma el elemento elegido, Esc activa la
        /// pantalla de reposo y F1-F12 disparan los favoritos.
        /// </summary>
        public static Dictionary<string, string> DefaultShortcuts()
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
            d["avanzar"] = "Espacio";
            d["retroceder"] = "Izquierda";
            d["avanzarElemento"] = "Derecha";
            d["retrocederElemento"] = "Enter";
            d["avanzarLinea"] = "Abajo";
            d["retrocederLinea"] = "Arriba";
            d["reposo"] = "Escape";
            for (int i = 1; i <= 12; i++)
                d["favorito" + i.ToString(CultureInfo.InvariantCulture)] =
                    "F" + i.ToString(CultureInfo.InvariantCulture);
            return d;
        }

        /// <summary>Tecla asignada a una acción ("" si la acción es desconocida
        /// o no tiene tecla — p. ej. el usuario la desasignó).</summary>
        public string GetShortcut(string action)
        {
            if (string.IsNullOrEmpty(action)) return string.Empty;
            string key;
            lock (_shortcuts)
            {
                if (!_shortcuts.TryGetValue(action, out key)) return string.Empty;
            }
            return key ?? string.Empty;
        }

        /// <summary>Asigna la tecla de una acción (acción/tecla vacías se
        /// ignoran). Crea la entrada si la acción es del catálogo.</summary>
        public void SetShortcut(string action, string key)
        {
            if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(key)) return;
            lock (_shortcuts) { _shortcuts[action] = key; }
        }

        /// <summary>Restaura los defaults de fábrica (F1.05).</summary>
        public void ResetShortcuts()
        {
            lock (_shortcuts)
            {
                _shortcuts.Clear();
                foreach (KeyValuePair<string, string> kv in DefaultShortcuts())
                    _shortcuts[kv.Key] = kv.Value;
            }
        }

        /// <summary>Copia del mapa actual (para la UI y los tests).</summary>
        public Dictionary<string, string> ShortcutsSnapshot()
        {
            lock (_shortcuts) { return new Dictionary<string, string>(_shortcuts, StringComparer.Ordinal); }
        }

        /// <summary>Rellena huecos con defaults y descarta acciones desconocidas
        /// (F1.05: campo desconocido ignorado — el catálogo es el contrato).</summary>
        internal void NormalizeShortcuts()
        {
            Dictionary<string, string> defaults = DefaultShortcuts();
            lock (_shortcuts)
            {
                List<string> unknown = new List<string>();
                foreach (KeyValuePair<string, string> kv in _shortcuts)
                    if (!defaults.ContainsKey(kv.Key)) unknown.Add(kv.Key);
                foreach (string u in unknown) _shortcuts.Remove(u);
                foreach (KeyValuePair<string, string> kv in defaults)
                    if (!_shortcuts.ContainsKey(kv.Key)) _shortcuts[kv.Key] = kv.Value;
            }
        }

        public string TriggersFile
        {
            get { return Path.Combine(Path.Combine(_dataDir, "triggers"), "triggers.json"); }
        }

        /// <summary>v6.1.0 «GUION»: módulos .js del usuario (cargados en
        /// caliente por el motor IActiveScript — data\modules\automatizacion.js…).</summary>
        public string ModulesDir
        {
            get { return Path.Combine(_dataDir, "modules"); }
        }

        public Settings() : this(null) {}

        /// <summary>baseDirOverride: para tests (directorio temporal).</summary>
        public Settings(string baseDirOverride)
        {
            ResetShortcuts();                          // F1.05: defaults de fábrica
            string baseDir = baseDirOverride;
            if (string.IsNullOrEmpty(baseDir))
            {
                baseDir = DefaultBaseDir();
            }
            _dataDir = Path.Combine(baseDir, "data");
        }

        /// <summary>
        /// Directorio base portable. v5.1.0: el paquete distribuye la interfaz
        /// en subcarpetas por runtime (net48\ · net35\) junto a un launcher en
        /// la raíz; SI el exe vive en una de ellas y la raíz contiene
        /// LuminaLauncher.exe, la base de datos/logs/temas/respaldos (data\)
        /// se comparte en la RAÍZ del paquete — así un usuario que pase de 3.5
        /// a 4.8 no "pierde" sus datos. En cualquier otro layout se usa la
        /// carpeta del exe (portable clásico).
        /// </summary>
        public static string DefaultBaseDir()
        {
            string exeDir;
            try
            {
                string exe = Environment.GetCommandLineArgs()[0];
                exeDir = Path.GetDirectoryName(Path.GetFullPath(exe));
                if (string.IsNullOrEmpty(exeDir))
                    exeDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
            }
            catch (Exception)
            {
                exeDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
            }
            try
            {
                string name = Path.GetFileName(exeDir);
                if (name == "net48" || name == "net35")
                {
                    string parent = Path.GetDirectoryName(exeDir.TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (!string.IsNullOrEmpty(parent) &&
                        File.Exists(Path.Combine(parent, "LuminaLauncher.exe")))
                        return parent;
                }
            }
            catch (Exception) { /* layout no estándar: carpeta del exe */ }
            return exeDir;
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
            if (ProjectionScreen < 0) ProjectionScreen = 0;
            if (ProjectionScreen > 32) ProjectionScreen = 32;
            if (DirectorScreen < 0) DirectorScreen = 0;
            if (BackupFolder == null) BackupFolder = string.Empty;
            if (TransitionMs < 0) TransitionMs = 0;
            if (TransitionMs > 5000) TransitionMs = 5000;
            NormalizeShortcuts();                          // F1.05
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
                s.ProjectionScreen = (int)MiniJson.GetInt(o, "projectionScreen", 0);
                // v7.1.0 «OPERADOR»: pantalla completa persistida (default true).
                s.ProjectionFullscreen = MiniJson.GetBool(o, "projectionFullscreen", true);
                s.DirectorScreen = (int)MiniJson.GetInt(o, "directorScreen", 2);
                s.BackupFolder = MiniJson.GetString(o, "backupFolder", string.Empty);
                s.AutoBackupOnExit = MiniJson.GetBool(o, "autoBackupOnExit", false);
                s.TransitionFade = MiniJson.GetBool(o, "transitionFade", true);
                s.TransitionMs = (int)MiniJson.GetInt(o, "transitionMs", 220);
                s.JsEnabled = MiniJson.GetBool(o, "jsEnabled", false);     // v6.1.0
                // F1.05: atajos persistidos — entradas con tecla válida; la
                // normalización descarta acciones desconocidas y rellena huecos.
                Dictionary<string, object> sc = MiniJson.GetObject(o, "shortcuts");
                if (sc != null)
                {
                    foreach (KeyValuePair<string, object> kv in sc)
                    {
                        string key = kv.Value as string;
                        if (!string.IsNullOrEmpty(key)) s.SetShortcut(kv.Key, key);
                    }
                }
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
            o["projectionScreen"] = ProjectionScreen;              // v5.1.0
            o["projectionFullscreen"] = ProjectionFullscreen;      // v7.1.0
            o["directorScreen"] = DirectorScreen;
            if (BackupFolder.Length > 0) o["backupFolder"] = BackupFolder;
            o["autoBackupOnExit"] = AutoBackupOnExit;
            o["transitionFade"] = TransitionFade;                  // v6.0.0
            o["transitionMs"] = TransitionMs;
            o["jsEnabled"] = JsEnabled;                            // v6.1.0 «GUION»
            // F1.05: mapa de atajos acción→tecla (SIEMPRE completo tras
            // Normalize; los lectores antiguos ignoran el campo — F2.01.8).
            Dictionary<string, object> sc = new Dictionary<string, object>();
            lock (_shortcuts)
            {
                foreach (KeyValuePair<string, string> kv in _shortcuts)
                    sc[kv.Key] = kv.Value;
            }
            o["shortcuts"] = sc;
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
