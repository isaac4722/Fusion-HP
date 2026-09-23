// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — managed/Lumina.WPF/MainWindow.Integrations.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MainWindow.Integrations.cs (4/4): API local, OBS WebSocket 5, MIDI
//  (entrada + salida), mando remoto LAN, activadores (reglas + acciones),
//  ajustes/respaldo y el editor de temas (aplicado al escenario).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using lumina.api;
using lumina.bridge;
using lumina.core;
using lumina.wpf.scripting;

namespace lumina.wpf
{
    public partial class MainWindow : Window
    {
        private lumina.ui.ObsClient _obs;

        /* v6.1.0 «GUION»: motor de scripts JSLib (IActiveScript) + temporizador
           de refresco del registro en la página Integraciones. */
        private JsEngine _js;
        private DispatcherTimer _jsLogTimer;

        /* ======================================================================
         *  API LOCAL
         * ==================================================================== */

        internal void ToggleApi()
        {
            if (_api != null && _api.IsRunning)
            {
                _api.Stop();
                Status("API detenida.");
                UpdateApiStatus();
                return;
            }
            if (!RequireEngine()) return;
            try
            {
                if (_api != null) { try { _api.Dispose(); } catch (Exception) { } _api = null; }
                int port = pageSettings.numPort.IntValue;
                string token = pageSettings.txtToken.Text;
                _api = new ApiServer(_engine, port, token);
                _api.SetSlideCatalog(_slides);
                _api.Start();
                Status("API escuchando en http://127.0.0.1:" + port + "/" +
                       (token.Length > 0 ? " (con token)" : " (sin token)"));
            }
            catch (Exception ex)
            {
                Status("No se pudo iniciar la API: " + ex.Message);
            }
            UpdateApiStatus();
        }

        private void UpdateApiStatus()
        {
            bool running = _api != null && _api.IsRunning;
            pageSettings.btnApiToggle.Text = running ? "Parar API" : "Iniciar API";
            pageSettings.lblApiState.Text = running
                ? "● EN EJECUCIÓN (puerto " + _api.Port + ")"
                : "● DETENIDA";
            pageSettings.lblApiState.Foreground = running ? OkBrush : ErrBrush;
            txtStApi.Text = running ? "API: 127.0.0.1:" + _api.Port : "API: detenida";
            chipApi.SetState(running ? "API " + _api.Port : "API: detenida",
                             running ? OkBrush : ErrBrush);
        }

        /* ======================================================================
         *  BASE DE DATOS
         * ==================================================================== */

        internal void BrowseDbPath()
        {
            Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.Title = "Base de datos (crear o abrir)";
            dlg.Filter = "Base de datos (*.db)|*.db|Todos los archivos (*.*)|*.*";
            dlg.FileName = Path.GetFileName(pageSettings.txtDbPath.Text);
            if (dlg.ShowDialog(this) == true)
                pageSettings.txtDbPath.Text = dlg.FileName;
        }

        internal void OpenDatabase()
        {
            if (!RequireEngine()) return;
            string path = pageSettings.txtDbPath.Text.Trim();
            if (path.Length == 0) { Status("Indica la ruta de la BD."); return; }
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception) { }
            int st;
            try { st = _engine.DbOpen(path); }
            catch (ObjectDisposedException) { return; }
            if (st == LuminaStatus.Ok)
            {
                _dbOpen = true;
                Status("BD abierta: " + path);
                MaybeSeedFactoryBible();
            }
            else
            {
                _dbOpen = false;
                Status("No se pudo abrir la BD (" + LuminaStatus.Name(st) + "): " + path);
            }
            UpdateStatusBar();
        }

        /// <summary>RVR1909 de fábrica si la BD está vacía y el paquete la trae.</summary>
        private void MaybeSeedFactoryBible()
        {
            try
            {
                string biblePath = Path.Combine(Path.Combine(Path.Combine(
                    Settings.DefaultBaseDir(), "resources"), "data"), "bible_rvr1909.json");
                if (!File.Exists(biblePath)) return;

                object countObj = LuminaStorage.Scalar(
                    _engine.DbExec(LuminaStorage.BuildExecJson("SELECT count(*) FROM bible")));
                long count = 0;
                if (countObj != null) long.TryParse(Convert.ToString(countObj,
                    CultureInfo.InvariantCulture), out count);
                if (count > 0) return;

                if (MessageBox.Show(this,
                        "La base de datos está vacía y este paquete incluye la Biblia\n" +
                        "Reina-Valera 1909 completa (dominio público).\n\n" +
                        "¿Instalarla ahora? (~31.000 versículos; tarda unos segundos)",
                        "Biblia incluida", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    != MessageBoxResult.Yes) return;

                BibleJsonResult r = BibleJson.ParseFile(biblePath, 0);
                if (r.VerseCount == 0)
                {
                    Status("La biblia incluida no produjo versículos (¿archivo dañado?).");
                    return;
                }
                pageBible.txtVersion.Text = r.Version.Length > 0 ? r.Version : "RVR1909";
                _settings.LastBibleVersion = pageBible.txtVersion.Text;
                InsertBibleRows(r.Version.Length > 0 ? r.Version : "RVR1909", r.Rows, r.VerseCount);
            }
            catch (Exception ex)
            {
                Status("No se pudo instalar la biblia incluida: " + ex.Message);
            }
        }

        /* ======================================================================
         *  AJUSTES / RESPALDO
         * ==================================================================== */

        internal void ApplySettingsToControls()
        {
            pageSettings.numPort.Value = _settings.ApiPort;
            pageSettings.txtToken.Text = _settings.ApiToken;
            pageBible.txtVersion.Text = _settings.LastBibleVersion;
            pageSettings.txtDbPath.Text = Path.Combine(_settings.DataDir, "lumina.db");
            pageSettings.txtBackupFolder.Text = _settings.BackupFolder;
            pageSettings.chkAutoBackup.IsChecked = _settings.AutoBackupOnExit;
            // v6.0.0: transición de proyección (fundido).
            pageSettings.chkFade.IsChecked = _settings.TransitionFade;
            pageSettings.numFadeMs.Value = _settings.TransitionMs;

            // Integraciones
            pageIntegrations.txtObsUrl.Text = _settings.ObsUrl;
            pageIntegrations.txtObsPassword.Password = _settings.ObsPassword;
            pageIntegrations.txtObsTextSource.Text = _settings.ObsTextSource;
            pageIntegrations.chkObsAutoConnect.IsChecked = _settings.ObsAutoConnect;
            pageIntegrations.chkRemoteEnabled.IsChecked = _settings.RemoteEnabled;
            pageIntegrations.numRemotePort.Value = _settings.RemotePort;
            pageIntegrations.txtRemoteToken.Text = _settings.RemoteToken;
            pageIntegrations.chkMidiEnabled.IsChecked = _settings.MidiEnabled;
            pageIntegrations.chkAutoAdvanceVideo.IsChecked = _settings.AutoAdvanceVideo;
            // v6.1.0 «GUION»: motor de scripts. Al final del bloque porque el
            // evento Checked dispara JsAutoApply → SaveIntegrationsFromControls
            // (los demás controles ya deben estar sincronizados — mismo patrón
            // que chkMidiEnabled).
            pageIntegrations.txtJsDir.Text = _settings.ModulesDir;
            pageIntegrations.chkJsEnabled.IsChecked = _settings.JsEnabled;

            // Pantalla del proyector persistida.
            pageLive.ClampScreenIndex(_settings.ProjectionScreen);

            // Tema persistido (JSON completo en settings.json).
            if (_settings.ThemeJson.Length > 0)
            {
                try { _theme = Theme.FromDict(MiniJson.Parse(_settings.ThemeJson)); }
                catch (Exception) { _theme = new Theme(); }
            }
            ApplyThemeToControls();
            _appliedThemeName = _settings.Theme;
            RefreshThemeLibrary(_settings.Theme);
        }

        internal void SaveSettings()
        {
            _settings.ApiPort = pageSettings.numPort.IntValue;
            _settings.ApiToken = pageSettings.txtToken.Text;
            _settings.Theme = _theme != null ? _theme.Name : "Predeterminado";
            _settings.LastBibleVersion = pageBible.txtVersion.Text.Trim();
            _settings.ThemeJson = _theme != null ? MiniJson.Serialize(_theme.ToDict()) : string.Empty;
            // v6.0.0: transición de proyección — se persiste Y se aplica al núcleo.
            _settings.TransitionFade = pageSettings.chkFade.IsChecked == true;
            _settings.TransitionMs = pageSettings.numFadeMs.IntValue;
            _settings.Save();
            ApplyTransitionToEngine();
        }

        /// <summary>v6.0.0: aplica la transición persistida al núcleo (si vive).</summary>
        internal void ApplyTransitionToEngine()
        {
            if (_engine == null) return;
            try
            {
                int mode = _settings.TransitionFade ? 1 : 0;
                int st = _engine.SetTransition(mode, _settings.TransitionMs);
                if (st != 0)
                    Status("Transición no aplicada (código " + st + ").");
            }
            catch (Exception)
            {
                // Núcleo caído a mitad de sesión: sin efecto, sin ruido.
            }
        }

        internal void SaveIntegrationsFromControls()
        {
            _settings.ObsUrl = pageIntegrations.txtObsUrl.Text.Trim();
            _settings.ObsPassword = pageIntegrations.txtObsPassword.Password;
            _settings.ObsTextSource = pageIntegrations.txtObsTextSource.Text.Trim();
            _settings.ObsAutoConnect = pageIntegrations.chkObsAutoConnect.IsChecked == true;
            _settings.RemoteEnabled = pageIntegrations.chkRemoteEnabled.IsChecked == true;
            _settings.RemotePort = pageIntegrations.numRemotePort.IntValue;
            _settings.RemoteToken = pageIntegrations.txtRemoteToken.Text.Trim();
            _settings.MidiEnabled = pageIntegrations.chkMidiEnabled.IsChecked == true;
            if (pageIntegrations.cmbMidiDevice.SelectedIndex >= 0)
                _settings.MidiDevice = pageIntegrations.cmbMidiDevice.SelectedIndex;
            _settings.AutoAdvanceVideo = pageIntegrations.chkAutoAdvanceVideo.IsChecked == true;
            _settings.JsEnabled = pageIntegrations.chkJsEnabled.IsChecked == true;
            SaveSettings();
        }

        internal void StartIntegrationsFromSettings()
        {
            _triggers = TriggerEngine.Load(_settings.TriggersFile);
            _triggers.SetSink(new TriggerSink(this));
            RefreshTriggersList();

            if (_settings.RemoteEnabled) StartRemote();
            if (_settings.MidiEnabled)
            {
                MidiRefreshDevices();
                StartMidi();
            }
            // v6.1.0 «GUION»: el checkbox (sincronizado en ApplySettingsToControls)
            // ya arrancó el motor vía evento; esto es la red de seguridad.
            if (_settings.JsEnabled && _js == null) StartJs();
            UpdateJsState();
            StartJsLogTimer();
            UpdateRemoteInfo();
            UpdateObsState();
            if (_settings.ObsAutoConnect && _settings.ObsUrl.Length > 0)
            {
                if (_obs == null) _obs = new lumina.ui.ObsClient();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { _obs.Connect(_settings.ObsUrl, _settings.ObsPassword, 5000); }
                    catch (Exception) { }
                    try
                    {
                        Dispatcher.BeginInvoke((Action)(delegate { UpdateObsState(); }));
                    }
                    catch (Exception) { }
                });
            }
        }

        /* ------------------------------------------------ respaldo v5.1.0 */

        internal void BrowseBackupFolder()
        {
            using (System.Windows.Forms.FolderBrowserDialog dlg =
                   new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Carpeta de respaldo (Google Drive / OneDrive / USB…)";
                dlg.ShowNewFolderButton = true;
                try
                {
                    string cur = pageSettings.txtBackupFolder.Text.Trim();
                    if (Directory.Exists(cur)) dlg.SelectedPath = cur;
                }
                catch (Exception) { }
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    pageSettings.txtBackupFolder.Text = dlg.SelectedPath;
                    _settings.BackupFolder = dlg.SelectedPath;
                    SaveSettings();
                }
            }
        }

        internal void RunBackupNow()
        {
            _settings.BackupFolder = pageSettings.txtBackupFolder.Text.Trim();
            _settings.AutoBackupOnExit = pageSettings.chkAutoBackup.IsChecked == true;
            SaveSettings();
            string err = PerformBackup();
            if (err == null) Status("Respaldo creado en: " + _settings.BackupFolder);
            else Status("Respaldo falló: " + err);
        }

        internal string PerformBackup()
        {
            try
            {
                string folder = _settings.BackupFolder;
                if (folder.Length == 0) return null;
                if (!Directory.Exists(folder))
                {
                    try { Directory.CreateDirectory(folder); }
                    catch (Exception ex) { return "la carpeta no existe: " + ex.Message; }
                }
                string dataDir = _settings.DataDir;
                if (!Directory.Exists(dataDir)) return null;
                string zipPath = Path.Combine(folder,
                    "lumina-backup-" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".zip");
                string err = ZipBackup.CreateFromDirectory(dataDir, zipPath);
                if (err != null) return err;
                try
                {
                    string[] old = Directory.GetFiles(folder, "lumina-backup-*.zip");
                    Array.Sort(old, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i + 10 < old.Length; i++)
                        try { File.Delete(old[i]); } catch (Exception) { }
                }
                catch (Exception) { }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /* ======================================================================
         *  OBS STUDIO (obs-websocket 5.x)
         * ==================================================================== */

        internal void ObsConnectClick()
        {
            SaveIntegrationsFromControls();
            if (_obs != null && _obs.IsReady)
            {
                _obs.Disconnect();
                UpdateObsState();
                return;
            }
            if (_obs == null) _obs = new lumina.ui.ObsClient();
            Status("Conectando con OBS…");
            UpdateObsState();
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = _obs.Connect(_settings.ObsUrl, _settings.ObsPassword, 5000);
                try
                {
                    Dispatcher.BeginInvoke((Action)(delegate
                    {
                        Status(ok ? "OBS conectado (" + _settings.ObsUrl + ")."
                                  : "OBS no conectado: " + _obs.LastError);
                        UpdateObsState();
                    }));
                }
                catch (Exception) { }
            });
        }

        private void UpdateObsState()
        {
            bool ready = _obs != null && _obs.IsReady;
            pageIntegrations.lblObsState.Text = ready ? "● CONECTADO Y AUTENTICADO" : "● SIN CONECTAR";
            pageIntegrations.lblObsState.Foreground = ready ? OkBrush : TextDisabledBrush;
            pageIntegrations.btnObsConnect.Text = ready ? "Desconectar de OBS" : "Conectar con OBS";
        }

        private void ObsSendLiveText(string text)
        {
            if (_obs == null || !_obs.IsReady) return;
            if (_settings.ObsTextSource.Length == 0 || text == null) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { _obs.SetInputText(_settings.ObsTextSource, text); }
                catch (Exception) { }
            });
        }

        /* ======================================================================
         *  MIDI (entrada → activadores · salida como acción)
         * ==================================================================== */

        internal void MidiAutoApply()
        {
            SaveIntegrationsFromControls();
            if (_settings.MidiEnabled) StartMidi();
            else StopMidi();
        }

        private void StartMidi()
        {
            StopMidi();
            if (_midi == null) _midi = new lumina.ui.MidiInput();
            _midi.MessageReceived += OnMidiMessage;
            int dev = pageIntegrations.cmbMidiDevice.SelectedIndex >= 0
                ? pageIntegrations.cmbMidiDevice.SelectedIndex : _settings.MidiDevice;
            bool ok = _midi.Open(dev);
            pageIntegrations.lblMidiState.Text = ok
                ? "● ENTRADA ACTIVA (" + dev + ": " +
                  (pageIntegrations.cmbMidiDevice.SelectedIndex >= 0 &&
                   pageIntegrations.cmbMidiDevice.Text.Length > 0
                      ? pageIntegrations.cmbMidiDevice.Text : "dispositivo " + dev) + ")"
                : "● ERROR: " + _midi.LastError;
            pageIntegrations.lblMidiState.Foreground = ok ? OkBrush : ErrBrush;
        }

        private void StopMidi()
        {
            if (_midi != null)
            {
                _midi.MessageReceived -= OnMidiMessage;
                try { _midi.Dispose(); } catch (Exception) { }
                _midi = null;
            }
            if (pageIntegrations != null && pageIntegrations.lblMidiState != null)
            {
                pageIntegrations.lblMidiState.Text = "● ENTRADA DETENIDA";
                pageIntegrations.lblMidiState.Foreground = TextDisabledBrush;
            }
        }

        internal void MidiRefreshDevices()
        {
            string[] names = lumina.ui.MidiInput.DeviceNames();
            pageIntegrations.cmbMidiDevice.Items.Clear();
            foreach (string n in names) pageIntegrations.cmbMidiDevice.Items.Add(n);
            if (pageIntegrations.cmbMidiDevice.Items.Count > 0)
            {
                int idx = Math.Max(0, Math.Min(_settings.MidiDevice,
                    pageIntegrations.cmbMidiDevice.Items.Count - 1));
                pageIntegrations.cmbMidiDevice.SelectedIndex = idx;
            }
            Status(names.Length > 0
                ? names.Length + " dispositivo(s) MIDI detectados."
                : "No hay dispositivos MIDI de entrada conectados.");
        }

        // Hilo del driver winmm → activadores por el hilo de UI.
        private void OnMidiMessage(object sender, lumina.ui.MidiEventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke((Action)(delegate { FireMidiTriggers(e); }));
            }
            catch (Exception) { }
        }

        private void FireMidiTriggers(lumina.ui.MidiEventArgs e)
        {
            Dictionary<string, object> ctx = new Dictionary<string, object>();
            ctx["channel"] = e.Channel;
            string evt = null;
            if (e.Command == lumina.ui.MidiInput.CmdNote)
            {
                ctx["note"] = e.Data1;
                ctx["velocity"] = e.Data2;
                evt = "midi_note";
            }
            else if (e.Command == lumina.ui.MidiInput.CmdCc)
            {
                ctx["controller"] = e.Data1;
                ctx["value"] = e.Data2;
                evt = "midi_cc";
            }
            else if (e.Command == lumina.ui.MidiInput.CmdProgram)
            {
                ctx["program"] = e.Data1;
                evt = "midi_program";
            }
            if (evt == null) return;
            // v6.1.0: mismos eventos a los módulos JS (antes que las reglas —
            // el orden es determinístico y documentado en JsEngine.md).
            FireJsEvent(evt, ctx);
            if (_triggers == null || !_settings.TriggersEnabled) return;
            _triggers.Fire(evt, ctx);
        }

        /* ======================================================================
         *  MÓDULOS JS (JSLib — v6.1.0 «GUION»)
         *  Motor IActiveScript/JScript de Windows: módulos .js en data\modules\,
         *  retransmisión de eventos junto a los activadores y acciones
         *  cmd/showText hacia la propia aplicación (ver docs/api/JsEngine.md).
         * ==================================================================== */

        /// <summary>Checbox Activar/Desactivar (persiste y arranca/detiene).</summary>
        internal void JsAutoApply()
        {
            SaveIntegrationsFromControls();
            if (_settings.JsEnabled) StartJs();
            else StopJs();
            UpdateJsState();
        }

        /// <summary>Botón «Recargar módulos» (motor nuevo, conexiones cerradas).</summary>
        internal void JsReloadClick()
        {
            SaveIntegrationsFromControls();
            if (!_settings.JsEnabled)
            {
                Status("El motor JSLib está desactivado — actívelo primero.");
                return;
            }
            if (_js == null)
            {
                StartJs();
                UpdateJsState();
                return;
            }
            bool ok = _js.Reload();
            Status(ok ? "Módulos JS recargados (" + _settings.ModulesDir + ")."
                      : "Módulos recargados con errores: " + JsErrorsText());
            UpdateJsState();
        }

        private void StartJs()
        {
            StopJs();
            _js = JsEngine.Create(Dispatcher, new MainWindowJsSink(this));
            bool ok = _js.LoadModules(_settings.ModulesDir);
            if (ok)
            {
                Status("Motor JSLib listo — " + _js.LoadedModules().Count + " módulo/s ("
                       + _settings.ModulesDir + ").");
            }
            else
            {
                Status("Motor JSLib listo con errores: " + JsErrorsText());
            }
        }

        private void StopJs()
        {
            if (_js != null)
            {
                try { _js.Dispose(); }
                catch (Exception) { }
                _js = null;
            }
        }

        private string JsErrorsText()
        {
            List<string> e = _js != null ? _js.Errors() : new List<string>();
            return e.Count == 0 ? "(sin detalle)" : string.Join(" | ", e.ToArray());
        }

        private void UpdateJsState()
        {
            bool on = _js != null && _js.IsAlive;
            int n = on ? _js.LoadedModules().Count : 0;
            pageIntegrations.lblJsState.Text = on
                ? "● MOTOR ACTIVO — " + n + " módulo/s" + (_js.Errors().Count > 0 ? " (con errores)" : "")
                : "● MOTOR DETENIDO";
            pageIntegrations.lblJsState.Foreground = on ? OkBrush : TextDisabledBrush;
            pageIntegrations.txtJsDir.Text = _settings.ModulesDir;
        }

        /// <summary>Refresco del registro (ring de 60) mientras la app viva.
        /// Barato: 1 Hz y solo asigna ItemsSource (la lista acota sola).</summary>
        private void StartJsLogTimer()
        {
            if (_jsLogTimer != null) return;
            _jsLogTimer = new DispatcherTimer(TimeSpan.FromSeconds(1),
                DispatcherPriority.Background, delegate(object s, EventArgs e) { JsTick(); }, Dispatcher);
            _jsLogTimer.Start();
        }

        private void JsTick()
        {
            if (_closing) return;
            if (_js == null)
            {
                if (pageIntegrations.lstJsLog.Items.Count > 0)
                    pageIntegrations.lstJsLog.ItemsSource = null;
                return;
            }
            pageIntegrations.lstJsLog.ItemsSource = _js.Log.Snapshot();
        }

        /// <summary>Retransmite un evento del presentador a los módulos JS
        /// (junto a los activadores — mismos nombres y contexto, payload JSON
        /// vía MiniJson para jsonParse() del preámbulo).</summary>
        internal void FireJsEvent(string name, Dictionary<string, object> ctx)
        {
            if (_js == null || !_settings.JsEnabled) return;
            try
            {
                string payload = ctx == null || ctx.Count == 0 ? "{}" : MiniJson.Serialize(ctx);
                _js.FireEvent(name, payload);
            }
            catch (Exception) { }
        }

        internal void StopJsForShutdown()
        {
            if (_jsLogTimer != null)
            {
                try { _jsLogTimer.Stop(); }
                catch (Exception) { }
                _jsLogTimer = null;
            }
            StopJs();
        }

        // ---- MIDI OUT mínimo (winmm) para la acción midi_out ----
        [DllImport("winmm.dll", EntryPoint = "midiOutOpen")]
        private static extern int midiOutOpen(out IntPtr handle, uint device, IntPtr callback,
                                               IntPtr instance, uint flags);
        [DllImport("winmm.dll", EntryPoint = "midiOutShortMsg")]
        private static extern int midiOutShortMsg(IntPtr handle, int message);
        [DllImport("winmm.dll", EntryPoint = "midiOutClose")]
        private static extern int midiOutClose(IntPtr handle);

        private IntPtr _midiOut;
        private readonly object _midiOutLock = new object();

        private bool MidiOutSend(int status, int data1, int data2)
        {
            lock (_midiOutLock)
            {
                try
                {
                    if (_midiOut == IntPtr.Zero)
                    {
                        int mmr = midiOutOpen(out _midiOut, 0, IntPtr.Zero, IntPtr.Zero, 0);
                        if (mmr != 0) return false;
                    }
                    int msg = (status & 0xFF) | ((data1 & 0x7F) << 8) | ((data2 & 0x7F) << 16);
                    return midiOutShortMsg(_midiOut, msg) == 0;
                }
                catch (Exception) { return false; }
            }
        }

        private void CloseMidiOut()
        {
            lock (_midiOutLock)
            {
                if (_midiOut != IntPtr.Zero)
                {
                    try { midiOutClose(_midiOut); } catch (Exception) { }
                    _midiOut = IntPtr.Zero;
                }
            }
        }

        /* ======================================================================
         *  MANDO REMOTO MÓVIL (LAN)
         * ==================================================================== */

        internal void RemoteApplyClick()
        {
            SaveIntegrationsFromControls();
            if (_settings.RemoteEnabled) StartRemote();
            else StopRemote();
        }

        private void StartRemote()
        {
            StopRemote();
            if (!RequireEngine()) return;
            try
            {
                _remote = new RemoteServer(_engine, _settings.RemotePort, _settings.RemoteToken);
                _remote.SetSlideCatalog(_slides);
                _remote.LiveTextProvider = BuildLiveText;
                _remote.NoticeReceived += OnRemoteNotice;
                _remote.Start();
                Status("Mando remoto: http://" + (_remote.RemoteUrl()) + " — puerto " + _settings.RemotePort);
            }
            catch (Exception ex)
            {
                Status("No se pudo abrir el mando remoto: " + ex.Message);
            }
            UpdateRemoteInfo();
        }

        private void StopRemote()
        {
            if (_remote != null)
            {
                _remote.NoticeReceived -= OnRemoteNotice;
                try { _remote.Dispose(); } catch (Exception) { }
                _remote = null;
            }
            if (pageIntegrations != null) UpdateRemoteInfo();
        }

        private void UpdateRemoteInfo()
        {
            bool running = _remote != null && _remote.IsRunning;
            pageIntegrations.lblRemoteState.Text = running
                ? "● MANDO ACTIVO (puerto " + _settings.RemotePort + ")" : "● MANDO DETENIDO";
            pageIntegrations.lblRemoteState.Foreground = running ? OkBrush : TextDisabledBrush;
            pageIntegrations.lblRemoteUrl.Text = running
                ? "Abre en el móvil: " + _remote.RemoteUrl() +
                  (_settings.RemoteToken.Length == 0
                      ? "  ⚠ sin token: cualquier equipo de la red puede controlar"
                      : string.Empty)
                : "El mando se abre con el botón o al activarlo en ajustes.";
            pageIntegrations.lblRemoteUrl.Foreground =
                (running && _settings.RemoteToken.Length == 0) ? WarningBrush : TextSecondaryBrush;
        }

        // Hilo del servidor remoto → zócalo en pantalla (hilo de UI).
        private void OnRemoteNotice(object sender, string text)
        {
            try
            {
                Dispatcher.BeginInvoke((Action)(delegate
                {
                    ShowLowerThird(text, string.Empty, 8);
                    Status("Aviso recibido del mando remoto.");
                }));
            }
            catch (Exception) { }
        }

        /* ======================================================================
         *  ACTIVADORES
         * ==================================================================== */

        internal void RefreshTriggersList()
        {
            List<TriggerRowVm> vms = new List<TriggerRowVm>();
            if (_triggers != null)
            {
                foreach (TriggerRule r in _triggers.RulesSnapshot())
                {
                    TriggerRowVm vm = new TriggerRowVm();
                    vm.Id = r.Id;
                    vm.Name = r.Name.Length > 0 ? r.Name : r.Id;
                    vm.Event = r.Event;
                    vm.Match = TriggerMatchText(r);
                    vm.Actions = TriggerActionsText(r);
                    vm.Enabled = r.Enabled;
                    vms.Add(vm);
                }
            }
            pageTriggers.FillTriggers(vms);
        }

        private static string TriggerMatchText(TriggerRule r)
        {
            if (r.Match.Count == 0) return "(siempre)";
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, object> kv in r.Match)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(kv.Key).Append('=').Append(kv.Value);
            }
            return sb.ToString();
        }

        private static string TriggerActionsText(TriggerRule r)
        {
            if (r.Actions.Count == 0) return "(ninguna)";
            StringBuilder sb = new StringBuilder();
            foreach (lumina.core.TriggerAction a in r.Actions)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(a.Type);
                if (a.Params.Count > 0)
                {
                    sb.Append('(');
                    int n = 0;
                    foreach (KeyValuePair<string, object> kv in a.Params)
                    {
                        if (n++ > 0) sb.Append(", ");
                        sb.Append(kv.Key).Append(':').Append(kv.Value);
                    }
                    sb.Append(')');
                }
            }
            return sb.ToString();
        }

        internal void TriggerAdd() { TriggerDialog(null); }

        internal void TriggerEdit()
        {
            TriggerRowVm sel = pageTriggers.SelectedTrigger();
            if (sel == null) { Status("Selecciona una regla para editarla."); return; }
            foreach (TriggerRule r in _triggers.RulesSnapshot())
            {
                if (r.Id == sel.Id) { TriggerDialog(r); return; }
            }
        }

        internal void TriggerToggleEnable()
        {
            TriggerRowVm sel = pageTriggers.SelectedTrigger();
            if (sel == null) { Status("Selecciona una regla."); return; }
            List<TriggerRule> rules = _triggers.RulesSnapshot();
            foreach (TriggerRule r in rules)
            {
                if (r.Id == sel.Id) { r.Enabled = !r.Enabled; break; }
            }
            _triggers.SetRules(rules);
            _triggers.Save(_settings.TriggersFile);
            RefreshTriggersList();
        }

        internal void TriggerDelete()
        {
            TriggerRowVm sel = pageTriggers.SelectedTrigger();
            if (sel == null) { Status("Selecciona una regla."); return; }
            List<TriggerRule> rules = _triggers.RulesSnapshot();
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].Id == sel.Id) { rules.RemoveAt(i); break; }
            }
            _triggers.SetRules(rules);
            _triggers.Save(_settings.TriggersFile);
            RefreshTriggersList();
            Status("Regla eliminada.");
        }

        /// <summary>Diálogo de creación/edición de reglas (WPF, en código).</summary>
        private void TriggerDialog(TriggerRule edit)
        {
            bool isNew = edit == null;
            TriggerRule r = isNew ? new TriggerRule() : edit;

            Window dlg = new Window();
            dlg.Title = isNew ? "Nueva regla de activación" : "Editar regla";
            dlg.Owner = this;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dlg.ResizeMode = ResizeMode.NoResize;
            dlg.Width = 580; dlg.Height = 460;
            dlg.Background = FindResource("PageBrush") as Brush;

            StackPanel sp = new StackPanel { Margin = new Thickness(18) };
            sp.Children.Add(new TextBlock { Text = "Nombre de la regla:", Style = (Style)FindResource("TxtLabel"), Margin = new Thickness(0, 0, 0, 4) });
            TextBox txtName = new TextBox { Text = r.Name };
            sp.Children.Add(txtName);

            sp.Children.Add(new TextBlock { Text = "Evento que la dispara:", Style = (Style)FindResource("TxtLabel"), Margin = new Thickness(0, 10, 0, 4) });
            ComboBox cmbEvent = new ComboBox();
            cmbEvent.Items.Add("item_changed"); cmbEvent.Items.Add("slide_changed");
            cmbEvent.Items.Add("song_started"); cmbEvent.Items.Add("video_ended");
            cmbEvent.Items.Add("midi_note"); cmbEvent.Items.Add("midi_cc");
            cmbEvent.Items.Add("midi_program"); cmbEvent.Items.Add("api_webhook");
            cmbEvent.SelectedItem = r.Event.Length > 0 ? (object)r.Event : "item_changed";
            sp.Children.Add(cmbEvent);

            sp.Children.Add(new TextBlock
            {
                Text = "Condición (clave=valor, separada por «;». Vacío = siempre. " +
                       "Ej.: title=Ofrenda · note=60 · contains:Alabanza)",
                Style = (Style)FindResource("TxtLabel"),
                Margin = new Thickness(0, 10, 0, 4), TextWrapping = TextWrapping.Wrap
            });
            TextBox txtMatch = new TextBox { Text = MatchToText(r.Match) };
            sp.Children.Add(txtMatch);

            sp.Children.Add(new TextBlock { Text = "Acción:", Style = (Style)FindResource("TxtLabel"), Margin = new Thickness(0, 10, 0, 4) });
            ComboBox cmbAction = new ComboBox();
            cmbAction.Items.Add("obs_scene"); cmbAction.Items.Add("obs_source_text");
            cmbAction.Items.Add("play_audio"); cmbAction.Items.Add("show_text");
            cmbAction.Items.Add("set_theme"); cmbAction.Items.Add("api_cmd");
            cmbAction.Items.Add("midi_out"); cmbAction.Items.Add("script");
            if (r.Actions.Count > 0) cmbAction.SelectedItem = r.Actions[0].Type;
            else cmbAction.SelectedIndex = 0;
            sp.Children.Add(cmbAction);

            sp.Children.Add(new TextBlock
            {
                Text = "Parámetros de la acción (clave=valor, «;»). Ej.: scene=Culto · text=¡Bienvenido! · " +
                       "source=Titulo · path=C:\\audio\\clipe.mp3 · theme=Soleado · action=next · status=192 · data1=7 · " +
                       "function=alIniciar · data={\"bloque\":\"Alabanza\"}",
                Style = (Style)FindResource("TxtLabel"),
                Margin = new Thickness(0, 10, 0, 4), TextWrapping = TextWrapping.Wrap
            });
            TextBox txtParams = new TextBox
            {
                Text = r.Actions.Count > 0 && r.Actions[0].Params.Count > 0
                    ? MatchToText(r.Actions[0].Params) : string.Empty
            };
            sp.Children.Add(txtParams);

            StackPanel btns = new StackPanel
            { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
            LumButton ok = new LumButton { Text = "Guardar regla", Variant = "Primary", MinWidth = 130, Margin = new Thickness(0, 0, 8, 0) };
            LumButton cancel = new LumButton { Text = "Cancelar", Variant = "Secondary", MinWidth = 96 };
            btns.Children.Add(ok);
            btns.Children.Add(cancel);
            sp.Children.Add(btns);
            dlg.Content = sp;

            ok.Click += delegate
            {
                r.Name = txtName.Text.Trim();
                r.Event = Convert.ToString(cmbEvent.SelectedItem, CultureInfo.InvariantCulture);
                r.Match = TextToMatch(txtMatch.Text);
                lumina.core.TriggerAction a = new lumina.core.TriggerAction();
                a.Type = Convert.ToString(cmbAction.SelectedItem, CultureInfo.InvariantCulture);
                a.Params = TextToMatch(txtParams.Text);
                r.Actions = new List<lumina.core.TriggerAction>(new lumina.core.TriggerAction[] { a });
                if (isNew)
                {
                    r.Id = "r" + DateTime.UtcNow.ToString("yyyyMMddHHmmssff",
                        CultureInfo.InvariantCulture);
                    r.Enabled = true;
                    List<TriggerRule> rules = _triggers.RulesSnapshot();
                    rules.Add(r);
                    _triggers.SetRules(rules);
                }
                _triggers.Save(_settings.TriggersFile);
                RefreshTriggersList();
                dlg.Close();
                Status("Regla guardada (" + r.Name + ").");
            };
            cancel.Click += delegate { dlg.Close(); };
            dlg.ShowDialog();
        }

        private static string MatchToText(Dictionary<string, object> d)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, object> kv in d)
            {
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(kv.Key).Append('=').Append(kv.Value);
            }
            return sb.ToString();
        }

        private static Dictionary<string, object> TextToMatch(string text)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            if (string.IsNullOrEmpty(text)) return d;
            foreach (string part in text.Split(';', ';'))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                int eq = p.IndexOf('=');
                if (eq <= 0) continue;
                d[p.Substring(0, eq).Trim()] = p.Substring(eq + 1).Trim();
            }
            return d;
        }

        // -------------------------------------------------- sink (ejecución) ----

        /// <summary>Sink JSLib → MainWindow (v6.1.0): cmd reutiliza el dispatcher
        /// de acciones de los activadores (api_cmd) y showText el zócalo.</summary>
        private sealed class MainWindowJsSink : IJsLibSink
        {
            private readonly MainWindow _owner;
            internal MainWindowJsSink(MainWindow owner) { _owner = owner; }

            public string Command(string action, int index)
            {
                lumina.core.TriggerAction a = new lumina.core.TriggerAction();
                a.Type = "api_cmd";
                a.Params["action"] = action;
                if (index >= 0) a.Params["index"] = index;
                return _owner.ExecuteTriggerActionCore(a);
            }

            public void ShowText(string text, int seconds)
            {
                _owner.ShowLowerThird(text, string.Empty, seconds <= 0 ? 6 : seconds);
            }

            public void Notify(string title, string text)
            {
                _owner.Status(string.IsNullOrEmpty(title) ? "JSLib: " + text
                                                          : "JSLib — " + title + ": " + text);
            }
        }

        private sealed class TriggerSink : ITriggerSink
        {
            private readonly MainWindow _owner;
            public TriggerSink(MainWindow owner) { _owner = owner; }

            public string ExecuteTriggerAction(TriggerRule rule, lumina.core.TriggerAction action)
            {
                try { return _owner.ExecuteTriggerActionCore(action); }
                catch (Exception ex)
                {
                    return "Error ejecutando acción " + action.Type + ": " + ex.Message;
                }
            }
        }

        private string ExecuteTriggerActionCore(lumina.core.TriggerAction a)
        {
            switch (a.Type)
            {
                case "obs_scene":
                    if (_obs == null || !_obs.IsReady) return "OBS no está conectado";
                    string scene = a.GetString("scene", a.GetString("value", string.Empty));
                    return _obs.SetScene(scene) ? "Escena OBS: " + scene : "Fallo enviando a OBS";
                case "obs_source_text":
                    if (_obs == null || !_obs.IsReady) return "OBS no está conectado";
                    string src = a.GetString("source", _settings.ObsTextSource);
                    if (src.Length == 0) return "Fuente OBS sin configurar";
                    return _obs.SetInputText(src, a.GetString("text", string.Empty))
                        ? "Texto OBS enviado" : "Fallo enviando texto a OBS";
                case "play_audio":
                    {
                        string path = a.GetString("path", string.Empty);
                        if (path.Length == 0 || !File.Exists(path))
                            return "Audio no encontrado: " + path;
                        PlayAudioHidden(path, a.GetInt("volume", 80));
                        return "Audio: " + Path.GetFileName(path);
                    }
                case "show_text":
                    {
                        string text = a.GetString("text", string.Empty);
                        ShowLowerThird(text, a.GetString("subtitle", string.Empty), a.GetInt("seconds", 6));
                        return "Aviso en pantalla";
                    }
                case "set_theme":
                    {
                        string themeName = a.GetString("theme", string.Empty);
                        if (themeName.Length == 0) return "Theme sin nombre";
                        ApplyThemeByName(themeName);
                        return "Tema: " + themeName;
                    }
                case "api_cmd":
                    {
                        if (!RequireEngine()) return "Sin núcleo";
                        string action = a.GetString("action", "next");
                        int st;
                        switch (action)
                        {
                            case "prev": st = _engine.Prev(); break;
                            case "clear": st = _engine.Clear(); break;
                            case "black": st = _engine.Black(a.GetInt("on", 1) != 0); break;
                            case "show": st = _engine.ShowSlide(a.GetInt("index", 0)); break;
                            default: st = _engine.Next(); break;
                        }
                        return st == LuminaStatus.Ok ? "Comando: " + action : "Comando falló";
                    }
                case "midi_out":
                    {
                        int status = a.GetInt("status", 192);
                        int d1 = a.GetInt("data1", 0);
                        int d2 = a.GetInt("data2", 0);
                        return MidiOutSend(status, d1, d2) ? "MIDI OUT enviado" : "MIDI OUT falló";
                    }
                case "script":
                    {
                        // v6.1.0 «GUION»: invoca una FUNCIÓN GLOBAL de un módulo JS
                        // con un payload JSON (function=nombre · data={"k":v}).
                        if (_js == null || !_js.IsAlive) return "Motor JSLib detenido";
                        string fn = a.GetString("function", a.GetString("value", string.Empty));
                        if (fn.Length == 0) return "Script sin función";
                        _js.CallGlobal(fn, a.GetString("data", "{}"));
                        return "Script: " + fn;
                    }
                default:
                    return "Acción desconocida: " + a.Type;
            }
        }
    }
}
