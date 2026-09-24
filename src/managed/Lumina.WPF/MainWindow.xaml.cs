// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — managed/Lumina.WPF/MainWindow.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MainWindow.xaml.cs (1/4 — caparazón): cabecera/chips, navegación, barra de
//  estado, motor/modo limitado, eventos del motor y teclado global.
//  El resto de la lógica vive en MainWindow.Live.cs / .Data.cs / .Integrations.cs
//  (mismos campos y métodos que la era WinForms → el gate --flowcheck por
//  reflexión se conserva intacto: _engine/_dbOpen/_slides/_txtRef +
//  LoadScriptureToStage).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using lumina.api;
using lumina.bridge;
using lumina.core;

namespace lumina.wpf
{
    public partial class MainWindow : Window
    {
        private const string AppName = "LuminaPresentation Suite";

        /* ------------------------------------------------------------ servicios */
        internal readonly Settings _settings;
        internal LuminaEngine _engine;                 // null = modo limitado (sin núcleo)
        private RemoteServer _remote;
        private TriggerEngine _triggers;
        private lumina.ui.MidiInput _midi;
        private readonly SynchronizationContext _uiCtx;

        /* --------------------------------------------------------- estado vivo */
        internal readonly List<SlideView> _slides = new List<SlideView>();
        internal int _currentIndex = -1;
        internal bool _black;
        internal bool _dbOpen;
        internal Theme _theme = new Theme();
        /// <summary>
        /// v7.1.0 «OPERADOR» (feedback #3): la ventana de proyección ¿está
        /// visible? (interruptor del botón/F5 + sondeo del cierre con X/ESC).
        /// </summary>
        internal bool _projectorVisible;
        /// <summary>v7.1.0: sondeo del estado del proyector (cierre con X/ESC).</summary>
        private DispatcherTimer _projectorTimer;

        /* --------------------------------------------------------------- culto */
        internal readonly List<ScenarioItem> _serviceItems = new List<ScenarioItem>();
        internal IList<ScenarioItem> _lastScenarioItems;
        internal string _lastScenarioName = string.Empty;
        /// <summary>v7.1.0 «OPERADOR»: true si el escenario EN VIVO proviene de
        /// la lista del culto (ediciones de diapositivas Diseño recargan en
        /// caliente sin reenviar a mano).</summary>
        internal bool _serviceIsLive;

        /* -------- control de la página Biblia (lo usa el --flowcheck) ------- */
        internal System.Windows.Controls.TextBox _txtRef;
        /* v6.0.0: término de la última búsqueda bíblica (p. ej. «misericordia»);
        se propaga como «highlight» al cargar/agregar el pasaje → el motor lo
        resalta EN PROYECCIÓN. Se limpia al resolver una referencia manual. */
        internal string _lastBibleSearchTerm = string.Empty;

        /* -------- salidas auxiliares (WinForms heredado, HWND independiente) - */
        // v6.0.0: índice del ítem cuyas notas están cargadas en el Director (-1 = ninguna).
        internal int _directorNotesItemIndex = -1;
        private lumina.ui.VideoPlayerForm _videoForm;
        private lumina.ui.StageViewForm _stageForm;
        private lumina.ui.LowerThirdsOverlay _lowerThird;
        private lumina.ui.DirectorForm _directorForm;
        private lumina.ui.VideoPlayerForm _audioForm;
        private int _videoSlideIndex = -1;
        private int _lastItemIndex = -1;
        /// <summary>v7.1.0 «OPERADOR»: reproductor de PPTX ORIGINAL (PowerPoint COM).</summary>
        private PowerPointShow _pptxShow;
        private int _pptxSlideIndex = -1;

        /* -------- navegación: índice de página activa + mapa sidebar→página -- */
        private int _activePage;
        private static readonly int[] NavToPage = { 0, 4, 1, 2, 3, 5, 6, 7, 8 };
        private readonly NavItem[] _navItems = new NavItem[9];

        private bool _closing;

        /* -------- pinceles del sistema de diseño (recursos XAML → C#) ------- */
        internal Brush OkBrush { get { return (Brush)FindResource("OkBrush"); } }
        internal Brush ErrBrush { get { return (Brush)FindResource("ErrBrush"); } }
        internal Brush WarningBrush { get { return (Brush)FindResource("WarningBrush"); } }
        internal Brush TextDisabledBrush { get { return (Brush)FindResource("TextDisabledBrush"); } }
        internal Brush TextSecondaryBrush { get { return (Brush)FindResource("TextSecondaryBrush"); } }
        internal Brush AccentBrush { get { return (Brush)FindResource("AccentBrush"); } }

        public MainWindow()
        {
            InitializeComponent();

            _settings = Settings.Load();

            // Wire de páginas (les pasa el Shell y las deja listas).
            pageLive.Wire(this);
            pageSongs.Wire(this);
            pageBible.Wire(this);
            pageThemes.Wire(this);
            pageService.Wire(this);
            pageExport.Wire(this);
            pageIntegrations.Wire(this);
            pageTriggers.Wire(this);
            pageSettings.Wire(this);

            // Contrato del gate --flowcheck: _txtRef = TextBox de la página Biblia.
            _txtRef = pageBible.txtRef;

            // Sidebar ordenado (posición en la barra → página).
            _navItems[0] = navLive; _navItems[1] = navService; _navItems[2] = navSongs;
            _navItems[3] = navBible; _navItems[4] = navThemes; _navItems[5] = navExport;
            _navItems[6] = navIntegrations; _navItems[7] = navTriggers; _navItems[8] = navSettings;

            // Contexto de UI: el motor encola sus eventos a este hilo.
            _uiCtx = SynchronizationContext.Current ??
                     new DispatcherSynchronizationContext(Dispatcher);

            try
            {
                using (System.Drawing.Icon di = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location))
                {
                    if (di != null)
                    {
                        System.Windows.Media.Imaging.BitmapSource bs =
                            System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                di.Handle, Int32Rect.Empty,
                                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        bs.Freeze();
                        Icon = bs;
                    }
                }
            }
            catch (Exception) { /* icono opcional */ }

            CreateEngine();
            UpdateBlackButton();
            ApplySettingsToControls();
            StartIntegrationsFromSettings();
            UpdateStatusBar();
            NavigateToIndex(0);

            // F1.06 «Arranque en Presentación»: tras abrir en En Vivo, se
            // restaura el ÚLTIMO PROYECTO (silencioso, fail-safe). Defer con
            // BeginInvoke para no retrasar el primer render de la ventana.
            Dispatcher.BeginInvoke(new Action(RestoreLastProjectAtStartup),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            // v7.1.0 «OPERADOR» (feedback #3): sondeo del proyector (1,2 s):
            // detecta el cierre con X/ESC de la ventana nativa para reflejarlo
            // en el interruptor (ligero: un StateJson pequeño por tick).
            _projectorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _projectorTimer.Tick += delegate { PollProjectorState(); };
            _projectorTimer.Start();
        }

        /* ======================================================================
         *  NAVEGACIÓN
         * ==================================================================== */

        private void OnNavClick(object sender, RoutedEventArgs e)
        {
            NavItem item = sender as NavItem;
            if (item == null || item.Tag == null) return;
            int page;
            if (!int.TryParse(Convert.ToString(item.Tag, CultureInfo.InvariantCulture), out page)) return;
            NavigateToIndex(page);
        }

        /// <summary>Acceso rápido al proyector desde la cabecera (F5).</summary>
        private void OnHeaderProjectorClick(object sender, RoutedEventArgs e)
        {
            ShowProjector();
        }

        /// <summary>Cambia la página visible y sincroniza la píldora del sidebar.</summary>
        internal void NavigateToIndex(int index)
        {
            if (index < 0 || index >= 9) index = 0;
            _activePage = index;
            pageLive.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            pageSongs.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            pageBible.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
            pageThemes.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
            pageService.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
            pageExport.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;
            pageIntegrations.Visibility = index == 6 ? Visibility.Visible : Visibility.Collapsed;
            pageTriggers.Visibility = index == 7 ? Visibility.Visible : Visibility.Collapsed;
            pageSettings.Visibility = index == 8 ? Visibility.Visible : Visibility.Collapsed;
            int nav = PageToNav(index);
            for (int i = 0; i < 9; i++)
                if (_navItems[i] != null) _navItems[i].IsCurrent = i == nav;
            if (index == 0) RefreshPreview();
        }

        private static int PageToNav(int page)
        {
            for (int i = 0; i < NavToPage.Length; i++)
                if (NavToPage[i] == page) return i;
            return 0;
        }

        /* ======================================================================
         *  MOTOR / MODO LIMITADO
         * ==================================================================== */

        private static bool IsWindows()
        {
            PlatformID id = Environment.OSVersion.Platform;
            return id == PlatformID.Win32NT || id == PlatformID.Win32Windows;
        }

        /// <summary>Windows real → motor con proyección nativa; otro SO → headless.</summary>
        private void CreateEngine()
        {
            try
            {
                bool headless = !IsWindows();
                _engine = LuminaEngine.Create(headless, OnEngineEventRaw, _uiCtx);
                _engine.UiEventReceived += OnEngineUiEvent;
                // v6.0.0: transición persistida (fundido) desde el arranque.
                // Siempre se aplica: el default del núcleo es fundido 220 ms y
                // el ajuste del usuario puede ser «corte» (mode 0).
                _engine.SetTransition(_settings.TransitionFade ? 1 : 0, _settings.TransitionMs);
                SetChipCore("Núcleo " + SafeCoreVersion(), true);
                App.LogLine("Núcleo: ACTIVO (" + SafeCoreVersion()
                    + (headless ? ", headless)" : ", proyección Win32)"));
            }
            catch (Exception ex)
            {
                _engine = null;
                SetChipCore("Núcleo: error", false);
                App.LogLine("Núcleo: FALLO — " + ex.GetType().Name + ": " + ex.Message);
                App.LogLine("Núcleo: detalle «" + ex + "»");
                App.LogLine("Núcleo: diagnóstico LoadLibrary: " + NativeLoadDiagnostic());
                try
                {
                    MessageBox.Show(this,
                        "El núcleo nativo (LuminaCore.dll) no pudo iniciarse:\n\n  " +
                        ex.Message + "\n\n" +
                        "La ventana abrirá en MODO LIMITADO: la interfaz funciona, pero " +
                        "proyección, base de datos e importaciones requieren el núcleo.\n\n" +
                        "El detalle técnico quedó registrado en el log de la sesión " +
                        "(data\\logs).\n\n" +
                        "Solución habitual: extraiga el ZIP completo en una carpeta propia " +
                        "y ejecute LuminaLauncher.exe desde ahí.",
                        AppName + " — núcleo no disponible",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception) { }
            }
            UpdateSidebarCore();
        }

        private void SetChipCore(string text, bool ok)
        {
            if (chipCore != null) chipCore.SetState(text, ok ? OkBrush : ErrBrush);
            txtStCore.Text = ok ? "Núcleo: " + SafeCoreVersion() : "Núcleo: error";
            txtStCore.Foreground = ok ? OkBrush : ErrBrush;
        }

        private void UpdateSidebarCore()
        {
            if (txtSidebarCore == null) return;
            txtSidebarCore.Text = _engine != null ? "Núcleo C++ · activo" : "Núcleo no disponible";
            txtSidebarCore.Foreground = _engine != null ? OkBrush : ErrBrush;
            dotCore.Fill = _engine != null ? OkBrush : TextDisabledBrush;
        }

        /// <summary>Texto «Salida · Pantalla N» de la tarjeta del sidebar.</summary>
        internal void UpdateSidebarScreen()
        {
            if (txtSidebarScreen == null || pageLive == null) return;
            int n = pageLive.SelectedScreenIndex() + 1;
            txtSidebarScreen.Text = "Salida · Pantalla " +
                n.ToString(CultureInfo.InvariantCulture);
        }

        private static string NativeLoadDiagnostic()
        {
            if (!IsWindows()) return "(no Windows: motor headless de desarrollo)";
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string path = Path.Combine(dir, "LuminaCore.dll");
                if (!File.Exists(path)) return "LuminaCore.dll AUSENTE en " + dir;
                uint err = LoadLibraryForDiagnostic(path);
                if (err == 0) return "LoadLibrary OK — el fallo es de inicialización, no de carga";
                string reason;
                switch (err)
                {
                    case 126: reason = "126 ERROR_MOD_NOT_FOUND: falta una DLL dependiente"; break;
                    case 127: reason = "127 ERROR_PROC_NOT_FOUND: la DLL importa una API que este Windows no tiene (¿import Win8+ estático?)"; break;
                    case 193: reason = "193 ERROR_BAD_EXE_FORMAT: bitness incorrecto (x86/x64)"; break;
                    case 5: reason = "5 ERROR_ACCESS_DENIED: antivirus o permisos"; break;
                    default: reason = err + " (ver documentación de GetLastError)"; break;
                }
                return "LoadLibrary GetLastError=" + reason;
            }
            catch (Exception ex)
            {
                return "(sondeo no disponible: " + ex.Message + ")";
            }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        private static uint LoadLibraryForDiagnostic(string path)
        {
            IntPtr h = LoadLibraryW(path);
            if (h != IntPtr.Zero)
            {
                FreeLibrary(h);
                return 0;
            }
            return (uint)Marshal.GetLastWin32Error();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        /// <summary>Guardia para toda acción que necesita el motor.</summary>
        internal bool RequireEngine()
        {
            if (_engine != null) return true;
            Status("Acción no disponible: el núcleo no está activo (modo limitado).");
            return false;
        }

        private string SafeCoreVersion()
        {
            try { return LuminaEngine.Version(); }
            catch (Exception) { return "no disponible"; }
        }

        /* --------------------------------------------------- eventos del motor */

        private void OnEngineEventRaw(object sender, LuminaEvent e)
        {
            if (e.Code == LuminaEvents.Error)
            {
                try { App.LogLine("ERROR núcleo: " + e.Text); } catch (Exception) { }
            }
        }

        private void OnEngineUiEvent(object sender, LuminaEvent e)
        {
            if (!Dispatcher.CheckAccess())
            {
                try { Dispatcher.BeginInvoke((Action)(delegate { HandleEngineEvent(e); })); }
                catch (Exception) { }
                return;
            }
            HandleEngineEvent(e);
        }

        private void HandleEngineEvent(LuminaEvent e)
        {
            switch (e.Code)
            {
                case LuminaEvents.State:
                    ApplyStateJson(e.Text);
                    break;
                case LuminaEvents.SlideChanged:
                    Dictionary<string, object> o = null;
                    try { o = MiniJson.Parse(e.Text); } catch (FormatException) { }
                    if (o != null)
                    {
                        _currentIndex = (int)MiniJson.GetInt(o, "index", _currentIndex);
                        int itemIdx = (int)MiniJson.GetInt(o, "item", -1);
                        HighlightCurrentSlide();
                        RefreshPreview();
                        SyncLiveExtras(itemIdx);
                    }
                    break;
                case LuminaEvents.ItemChanged:
                    break;   // el estado llega justo después
                case LuminaEvents.Error:
                    Status("Error del núcleo: " + e.Text);
                    break;
                default:
                    break;   // LOG/PONG: ruido para la UI principal
            }
        }

        private void ApplyStateJson(string json)
        {
            Dictionary<string, object> st = null;
            try { st = MiniJson.Parse(json); } catch (FormatException) { }
            if (st == null) return;
            _currentIndex = (int)MiniJson.GetInt(st, "current", _currentIndex);
            _black = MiniJson.GetBool(st, "black", _black);
            UpdateBlackButton();
            HighlightCurrentSlide();
            RefreshPreview();
        }

        /* ======================================================================
         *  BARRA DE ESTADO
         * ==================================================================== */

        internal void Status(string msg)
        {
            txtStatus.Text = msg ?? string.Empty;
            try
            {
                string m = msg ?? string.Empty;
                bool err = m.IndexOf("fallo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           m.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           m.IndexOf("no se pudo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           m.IndexOf("rechaz", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           m.IndexOf("no disponible", StringComparison.OrdinalIgnoreCase) >= 0;
                bool warn = !err &&
                           (m.IndexOf("atención", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            m.IndexOf("advertencia", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            m.IndexOf("requiere", StringComparison.OrdinalIgnoreCase) >= 0);
                dotStatus.Fill = err ? ErrBrush : warn ? WarningBrush : TextSecondaryBrush;
            }
            catch (Exception) { }
        }

        private void UpdateStatusBar()
        {
            bool db = _dbOpen;
            txtStDb.Text = db ? "BD: abierta" : "BD: —";
            txtStDb.Foreground = db ? OkBrush : TextDisabledBrush;
            chipDb.SetState(db ? "BD abierta" : "BD: —", db ? OkBrush : TextDisabledBrush);
            SetChipCore(_engine != null ? "Núcleo " + SafeCoreVersion() : "Núcleo: error",
                        _engine != null);
            UpdateSidebarCore();
            pageSettings.lblAboutCore.Text = _engine != null
                ? SafeCoreVersion() : "no disponible (modo limitado)";
        }

        /* ======================================================================
         *  TECLADO GLOBAL (F1 · F5-F8 · Ctrl+1…9 · ←/→/Espacio · B/L/P)
         * ==================================================================== */

        private static bool IsEditable(IInputElement focused)
        {
            if (focused == null) return false;
            return focused is System.Windows.Controls.TextBox
                || focused is System.Windows.Controls.PasswordBox
                || focused is System.Windows.Controls.ComboBox
                || focused is System.Windows.Controls.ListBox
                || focused is System.Windows.Controls.CheckBox;
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Las teclas de función SIEMPRE funcionan (no colisionan con edición).
            if (e.Key == Key.F1) { ShowShortcutsDialog(); e.Handled = true; return; }
            if (e.Key == Key.F5) { ShowProjector(); e.Handled = true; return; }
            if (e.Key == Key.F6) { OpenStageView(); e.Handled = true; return; }
            if (e.Key == Key.F7) { OpenDirectorView(); e.Handled = true; return; }
            if (e.Key == Key.F8) { ShowLowerThirdDialog(); e.Handled = true; return; }

            // Ctrl+1…9 salta a la página indicada (no interfiere con la edición).
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                // F1.07: Ctrl+K → búsqueda en caliente (desde cualquier página;
                // salta a En Vivo y enfoca la caja sin tocar la proyección).
                if (e.Key == Key.K)
                {
                    NavigateToIndex(0);
                    pageLive.FocusHotSearch();
                    e.Handled = true;
                    return;
                }
                int d = DigitFromKey(e.Key);
                if (d >= 1 && d <= 9)
                {
                    if (d <= NavToPage.Length) NavigateToIndex(NavToPage[d - 1]);
                    e.Handled = true;
                    return;
                }
                return;
            }

            // El resto respeta el foco editable.
            if (IsEditable(Keyboard.FocusedElement)) return;

            switch (e.Key)
            {
                case Key.Right:
                case Key.PageDown:
                    LiveNext(); e.Handled = true;
                    break;
                case Key.Space:
                    if (Keyboard.FocusedElement is Button) break;   // Espacio activa botones
                    LiveNext(); e.Handled = true;
                    break;
                case Key.Left:
                case Key.PageUp:
                    LivePrev(); e.Handled = true;
                    break;
                case Key.B:
                    if (_engine != null) { ToggleBlack(); e.Handled = true; }
                    break;
                case Key.L:
                    if (_engine != null) { _engine.Clear(); e.Handled = true; }
                    break;
                case Key.P:
                    ToggleVideoPause();
                    e.Handled = true;
                    break;
            }
        }

        private static int DigitFromKey(Key k)
        {
            if (k >= Key.D1 && k <= Key.D9) return (int)(k - Key.D1) + 1;
            if (k >= Key.NumPad1 && k <= Key.NumPad9) return (int)(k - Key.NumPad1) + 1;
            return 0;
        }

        /* ======================================================================
         *  CICLO DE VIDA
         * ==================================================================== */

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_closing)
            {
                _closing = true;
                try
                {
                    // v7.1.0: sondeo del proyector fuera durante el cierre.
                    if (_projectorTimer != null) _projectorTimer.Stop();
                    StopPowerPoint();
                }
                catch (Exception) { }
                try { SaveSettings(); } catch (Exception) { }
                StopVideo();
                if (_videoForm != null) { try { _videoForm.Dispose(); } catch (Exception) { } _videoForm = null; }
                if (_audioForm != null) { try { _audioForm.Dispose(); } catch (Exception) { } _audioForm = null; }
                if (_stageForm != null) { try { _stageForm.Close(); } catch (Exception) { } _stageForm = null; }
                if (_directorForm != null) { try { _directorForm.Close(); } catch (Exception) { } _directorForm = null; }
                if (_lowerThird != null) { try { _lowerThird.Dispose(); } catch (Exception) { } _lowerThird = null; }
                StopMidi();
                CloseMidiOut();
                StopRemote();
                StopJsForShutdown();          // v6.1.0 «GUION»: motor JSLib
                if (_engine != null) { try { _engine.Dispose(); } catch (Exception) { } _engine = null; }
                if (_settings.AutoBackupOnExit && _settings.BackupFolder.Length > 0)
                {
                    try { PerformBackup(); } catch (Exception) { }
                }
            }
            base.OnClosing(e);
        }

        /// <summary>Liberación para los gates de humo (uicheck/flowcheck sin ventana).</summary>
        internal void ForceDisposeForCheck()
        {
            _closing = true;
            try { SaveSettings(); } catch (Exception) { }
            if (_remote != null) { try { _remote.Dispose(); } catch (Exception) { } _remote = null; }
            if (_midi != null) { try { _midi.Dispose(); } catch (Exception) { } _midi = null; }
            StopJsForShutdown();              // v6.1.0 «GUION»: motor JSLib
            if (_engine != null) { try { _engine.Dispose(); } catch (Exception) { } _engine = null; }
        }
    }
}
