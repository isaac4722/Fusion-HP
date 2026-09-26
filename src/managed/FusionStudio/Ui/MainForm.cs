// ============================================================================
//  Fusion-HP · FusionStudio/Ui/MainForm.cs — ventana principal (WinForms
//  estable y ligera [SPEC §7.1]). Tres modos: Inicio (mosaicos), Estudio
//  (editor embebido) y Presentación (arranque por defecto [SPEC §6.5.3]).
//  UX igual o superior a la referencia H-P-Web-Version-Ref (PowerStudio):
//  biblioteca directa sin búsqueda obligatoria, lista de programa con
//  miniaturas y sub-líneas, atajos Holyrics (flechas/Espacio/Esc/B/C/L/G/F5).
//  v2.2: controles nativos modelados (FusionButton/FusionIconButton con iconos Tabler)
//  y MOTOR como dueño del estado: cerrar la GUI NO apaga la proyección.
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using Fusion.Shared;
using Fusion.Studio.Services;
using Fusion.Studio.Ui.Widgets;

namespace Fusion.Studio.Ui
{
    /// <summary>Tokens visuales (idénticos a la referencia web Fluent claro).</summary>
    public static class UiTheme
    {
        public static readonly Color Accent = ColorTranslator.FromHtml("#C43E1C");
        public static readonly Color AccentDark = ColorTranslator.FromHtml("#8A1F11");
        public static readonly Color AccentHover = ColorTranslator.FromHtml("#A83318");
        public static readonly Color AccentSoft = ColorTranslator.FromHtml("#FDF3F0");
        public static readonly Color Bg = ColorTranslator.FromHtml("#F3F2F1");
        public static readonly Color Panel = Color.White;
        public static readonly Color Border = ColorTranslator.FromHtml("#EDEBE9");
        public static readonly Color ChipBorder = ColorTranslator.FromHtml("#E1DFDD");
        public static readonly Color Hover = ColorTranslator.FromHtml("#F3F2F1");
        public static readonly Color Pressed = ColorTranslator.FromHtml("#EDEBE9");
        public static readonly Color InputBorder = ColorTranslator.FromHtml("#C8C6C4");
        public static readonly Color Text = ColorTranslator.FromHtml("#201F1E");
        public static readonly Color TextDim = ColorTranslator.FromHtml("#605E5C");
        public static readonly Color LiveRed = ColorTranslator.FromHtml("#D13438");

        // Fuentes COMPARTIDAS de por vida del proceso (v2.2.1): los métodos se
        // llaman desde caminos de pintado (DrawItem/OnPaint/MeasureText) y desde
        // refrescos repetidos de listas — crear una Font por llamada fugaba
        // handles GDI sin liberación (ExternalException tras uso prolongado).
        // WinForms NO dispone el Font de un control al disponer el control:
        // compartir instancias es seguro. Nadie debe envolverlas en using.
        static readonly Font fontNormal = new Font("Segoe UI", 9f);
        static readonly Font fontNormalBold = new Font("Segoe UI", 9f, FontStyle.Bold);
        static readonly Font fontSmall = new Font("Segoe UI", 8.25f);
        static readonly Font fontSmallBold = new Font("Segoe UI", 8.25f, FontStyle.Bold);
        static readonly Font fontTitle = new Font("Segoe UI", 14f, FontStyle.Bold);
        static readonly Font fontBig = new Font("Segoe UI", 12f, FontStyle.Bold);
        static readonly Font fontKbd = new Font("Segoe UI", 7.5f);

        public static Font Normal() { return fontNormal; }
        public static Font NormalBold() { return fontNormalBold; }
        public static Font Small() { return fontSmall; }
        public static Font SmallBold() { return fontSmallBold; }
        public static Font Title() { return fontTitle; }
        public static Font Big() { return fontBig; }
        public static Font Kbd() { return fontKbd; }

        /// <summary>Retira y dispone los hijos de un contenedor (v2.2.1).
        /// Controls.Clear() solo desacopla: los labels recreados en cada
        /// navegación fugaban handles de ventana. Dispose los libera de verdad.</summary>
        public static void DisposeChildren(Control host)
        {
            if (host == null || host.Controls == null || host.Controls.Count == 0) return;
            Control[] old = new Control[host.Controls.Count];
            host.Controls.CopyTo(old, 0);
            host.Controls.Clear();
            foreach (Control c in old)
                try { c.Dispose(); } catch { }
        }
    }

    public partial class MainForm : Form
    {
        public readonly AppSettings Settings;
        public readonly LiveOrchestrator Live;

        // modos
        enum Mode { Home, Studio, Present }
        Mode mode = Mode.Present;

        // layout
        Panel topBar;
        Label lblProject;
        FusionButton btnModeHome, btnModeStudio, btnModePresent;
        Label lblClock;
        Label lblLive;
        Panel content;
        Panel status;
        Label lblStatus;
        Label lblMotor;
        Timer clockTimer;

        // present
        Panel presentPanel;
        SlidePreview preview;
        Panel programPanel;
        ListBox programList;
        Panel linesPanel;
        Control liveControls;

        // biblioteca
        Panel libraryPanel;
        FusionTabs libTabs;
        ListBox songsList, scenariosList, mediaList, themesList;
        TreeView bibleTree;
        FusionSearchBox bibleSearch;
        ListBox bibleResults;
        ComboBox bibleVersion;

        // estudio
        Panel studioPanel;
        Control editorHost;

        // inicio
        Panel homePanel;

        bool suppressPreviewRefresh;

        public MainForm()
        {
            Settings = AppSettings.Load();
            Live = new LiveOrchestrator(Settings);
            UsageHistory.Init(Settings.DataDir);   // beta-1: historial compartido con el estudio C++
            Text = "Fusion HP — Estudio de presentación";
            Icon = LoadIcon();
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1100, 680);
            Size = new Size(1400, 860);
            Font = UiTheme.Normal();
            BackColor = UiTheme.Bg;

            BuildTopBar();
            BuildStatus();
            content = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
            Controls.Add(content);
            content.BringToFront();

            BuildHome();
            BuildPresent();
            BuildStudio();

            Live.StateChanged += OnLiveStateChanged;
            Live.CoreEvent += OnCoreEvent;

            clockTimer = new Timer { Interval = 1000 };
            clockTimer.Tick += delegate { lblClock.Text = DateTime.Now.ToString("HH:mm:ss"); };
            clockTimer.Start();
            lblClock.Visible = Settings.ShowClock;

            KeyPreview = true;
            KeyDown += OnGlobalKey;
            FormClosing += OnFormClosing;
            Load += OnFormLoad;
            SetMode(Settings.StartInPresentMode ? Mode.Present : Mode.Home);
        }

        Icon LoadIcon()
        {
            try
            {
                string p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.ico");
                if (System.IO.File.Exists(p)) return new Icon(p);
                p = System.IO.Path.Combine(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources"),
                    System.IO.Path.Combine("img", "app.ico"));
                if (System.IO.File.Exists(p)) return new Icon(p);
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------ barra superior
        void BuildTopBar()
        {
            topBar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = UiTheme.Panel };
            var sep = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.Border };
            topBar.Controls.Add(sep);

            // Marca Lumina (logo vectorial propio de la referencia) + título
            var logo = new PictureBox { Size = new Size(32, 32), Location = new Point(12, 8),
                                        SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            try
            {
                string lp = System.IO.Path.Combine(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources"),
                    System.IO.Path.Combine("img", "lumina-logo-32.png"));
                if (System.IO.File.Exists(lp)) logo.Image = Image.FromFile(lp);
            }
            catch { }
            topBar.Controls.Add(logo);

            var title = new Label { Text = "Fusion HP", Font = UiTheme.Title(), Location = new Point(52, 11),
                                    AutoSize = true, ForeColor = UiTheme.Text, BackColor = Color.Transparent };
            topBar.Controls.Add(title);

            lblProject = new Label { Text = "Sin proyecto", Font = UiTheme.Small(), ForeColor = UiTheme.TextDim,
                                     Location = new Point(170, 24), AutoSize = true, BackColor = Color.Transparent };
            topBar.Controls.Add(lblProject);

            // Pestañas de modo (chips modelados con iconos, como la referencia)
            btnModeHome = MakeModeButton("Inicio", "home", 460);
            btnModeStudio = MakeModeButton("Estudio", "pencil", 540);
            btnModePresent = MakeModeButton("Presentación", "device-desktop", 620);
            btnModeHome.Click += delegate { SetMode(Mode.Home); };
            btnModeStudio.Click += delegate { SetMode(Mode.Studio); };
            btnModePresent.Click += delegate { SetMode(Mode.Present); };

            // En vivo (anclado a la derecha)
            lblLive = new Label { Text = "● EN VIVO", Font = UiTheme.SmallBold(), ForeColor = UiTheme.LiveRed,
                                  AutoSize = true, BackColor = Color.Transparent,
                                  Anchor = AnchorStyles.Top | AnchorStyles.Right, Visible = false };
            topBar.Controls.Add(lblLive);

            var btnDiag = new FusionIconButton { IconName = "activity", ToolTipText = "Estado del sistema",
                                                 Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnDiag.Click += delegate
            {
                using (var f = new DiagForm(this)) f.ShowDialog(this);
            };
            topBar.Controls.Add(btnDiag);

            var btnSettings = new FusionIconButton { IconName = "settings", ToolTipText = "Configuración",
                                                     Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnSettings.Click += delegate
            {
                using (var f = new SettingsForm(this)) f.ShowDialog(this);
            };
            topBar.Controls.Add(btnSettings);

            lblClock = new Label { Text = DateTime.Now.ToString("HH:mm:ss"), Font = new Font("Consolas", 10f),
                                   ForeColor = UiTheme.TextDim, AutoSize = true, BackColor = Color.Transparent,
                                   Anchor = AnchorStyles.Top | AnchorStyles.Right };
            topBar.Controls.Add(lblClock);

            // posición derecha exacta al cambiar tamaño
            topBar.Resize += delegate { LayoutTopRight(); };
            LayoutTopRight();
            Controls.Add(topBar);
        }

        void LayoutTopRight()
        {
            // De derecha a izquierda: reloj · configuración · diagnóstico · EN VIVO
            int right = topBar.Width - 12;
            lblClock.Location = new Point(right - lblClock.PreferredWidth, 16);
            right = lblClock.Left - 10;
            Control btnSet = null, btnDia = null;
            foreach (Control c in topBar.Controls)
            {
                var ib = c as FusionIconButton;
                if (ib == null) continue;
                if (ib.IconName == "settings") btnSet = ib;
                else if (ib.IconName == "activity") btnDia = ib;
            }
            if (btnSet != null) { btnSet.Location = new Point(right - 34, 7); right = btnSet.Left - 4; }
            if (btnDia != null) { btnDia.Location = new Point(right - 34, 7); right = btnDia.Left - 4; }
            lblLive.Location = new Point(right - lblLive.PreferredWidth - 4, 16);
        }

        FusionButton MakeModeButton(string text, string icon, int x)
        {
            var b = new FusionButton
            {
                Text = text, IconName = icon, Kind = FusionButtonKind.Subtle,
                Location = new Point(x, 8), AutoSize = false, Size = new Size(text.Length * 7 + 48, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            topBar.Controls.Add(b);
            return b;
        }

        // ------------------------------------------------------------ barra de estado
        void BuildStatus()
        {
            status = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = UiTheme.Panel };
            var sep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UiTheme.Border };
            status.Controls.Add(sep);
            lblStatus = new Label { Text = "Iniciando…", Dock = DockStyle.Fill, Font = UiTheme.Small(),
                                    ForeColor = UiTheme.TextDim, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
            status.Controls.Add(lblStatus);
            lblMotor = new Label { Text = "", Font = UiTheme.SmallBold(), ForeColor = UiTheme.AccentDark,
                                   AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right,
                                   BackColor = Color.Transparent };
            status.Controls.Add(lblMotor);
            status.Resize += delegate
            {
                lblMotor.Location = new Point(status.Width - lblMotor.PreferredWidth - 10, 5);
            };
            Controls.Add(status);
        }

        // ------------------------------------------------------------ modo
        void SetMode(Mode m)
        {
            mode = m;
            btnModeHome.Kind = m == Mode.Home ? FusionButtonKind.Active : FusionButtonKind.Subtle;
            btnModeStudio.Kind = m == Mode.Studio ? FusionButtonKind.Active : FusionButtonKind.Subtle;
            btnModePresent.Kind = m == Mode.Present ? FusionButtonKind.Active : FusionButtonKind.Subtle;
            btnModeHome.Invalidate();
            btnModeStudio.Invalidate();
            btnModePresent.Invalidate();

            homePanel.Visible = m == Mode.Home;
            presentPanel.Visible = m == Mode.Present;
            studioPanel.Visible = m == Mode.Studio;

            if (m == Mode.Present) RefreshProgram();
            if (m == Mode.Studio) ActivateEditor();
            if (m == Mode.Home) RefreshRecents();   // tarjetas de recientes (web)
        }

        void OnFormLoad(object sender, EventArgs e)
        {
            // Conexión al Motor en segundo plano (arranque ≤3 s [SPEC §10.1])
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                // Biblias completas empaquetadas (RV1960, NVI, RVG, RVR1909):
                // instalación única en el primer arranque, después solo consulta.
                try { Live.EnsureBundledBibles(); } catch { }
                bool ok = Live.ConnectCore(1);   // Motor ya lanzado por el bootstrap
                BeginInvoke((Action)delegate
                {
                    UpdateStatus();
                    if (ok && !string.IsNullOrEmpty(Settings.LastProjectPath) &&
                        System.IO.File.Exists(Settings.LastProjectPath))
                    {
                        Live.LoadProject(Settings.LastProjectPath);
                        RefreshLibrary();
                        RefreshProgram();
                    }
                    else if (ok)
                    {
                        // sin proyecto local: sincronizar con el Motor (p.ej. GUI
                        // reabierta tras un cierre con el servicio en marcha)
                        Live.SyncFromMotor();
                        RefreshProgram();
                    }
                    // [v3.0.0 — bug «ventana de proyección»] Aplica la configuración
                    // completa AL ARRANCAR: monitor de salida, logo de reposo, avance
                    // y reloj quedan activos sin pasar por Configuración.
                    ApplySettings();
                });
            });
            UpdateStatus();
        }

        void OnLiveStateChanged()
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke((Action)delegate
                {
                    RefreshProgram();
                    UpdatePreview();
                    UpdateStatus();
                });
            }
            catch { /* forma cerrada */ }
        }

        void OnCoreEvent(string evt, JsonValue data)
        {
            if (evt == "warning")
            {
                string msg = data.GetStr("message", "Aviso del núcleo");
                if (IsDisposed) return;
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        MessageBox.Show(this, msg + "\n\nEl operador puede continuar con otro elemento.",
                            "Fusion HP — aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    });
                }
                catch { }
            }
        }

        public void UpdateStatus()
        {
            string core = Live.CoreConnected ? "motor ● conectado" : "motor ○ desconectado";
            int scen = Live.Project != null ? Live.Project.Scenarios.Count : 0;
            string proj = Live.Project != null ? Live.Project.Name : "Sin proyecto";
            lblProject.Text = proj;
            lblStatus.Text = core + "   ·   " + scen + " escenarios   ·   perfil " + DetectProfile();
            lblLive.Visible = !Live.State.IsBlank;
            lblMotor.Text = Live.State.HasProgram ? "MOTOR ● programa activo" : "";
            lblMotor.Location = new Point(status.Width - lblMotor.PreferredWidth - 10, 5);
        }

        static string DetectProfile()
        {
            Version v = Environment.Version;
            if (v.Major >= 4) return "A";
            if (v.Major >= 2) return "B";
            return "C";
        }

        // ------------------------------------------------------------ atajos [SPEC §6.5.1]
        void OnGlobalKey(object sender, KeyEventArgs e)
        {
            bool typing = ActiveControl is TextBox || ActiveControl is ComboBox;
            if (e.KeyCode == Keys.Escape && !typing)
            {
                if (mode == Mode.Present)
                {
                    Live.Blank("black");       // pantalla de reposo [SPEC §6.1.3]
                    e.Handled = true;
                }
                return;
            }
            if (mode != Mode.Present) return;
            if (typing) return;
            switch (e.KeyCode)
            {
                case Keys.Right:
                case Keys.Space:
                case Keys.PageDown:
                    Live.NextLine(); e.Handled = true; break;
                case Keys.Left:
                case Keys.PageUp:
                    Live.PrevLine(); e.Handled = true; break;
                case Keys.Down:
                    Live.NextElement(); e.Handled = true; break;
                case Keys.Up:
                    Live.PrevElement(); e.Handled = true; break;
                case Keys.B:
                    Live.Blank(Live.State.BlankMode == "black" ? "none" : "black"); e.Handled = true; break;
                case Keys.C:
                    Live.Blank(Live.State.BlankMode == "clear" ? "none" : "clear"); e.Handled = true; break;
                case Keys.L:
                    Live.Blank(Live.State.BlankMode == "logo" ? "none" : "logo"); e.Handled = true; break;
                case Keys.G:
                    ShowQuickVerse(); e.Handled = true; break;   // biblia rápida (web)
                case Keys.F5:
                    ShowOutputInfo(); e.Handled = true; break;
            }
        }

        void ShowOutputInfo()
        {
            var monitors = System.Windows.Forms.Screen.AllScreens;
            string info = "Salida configurada: " +
                (Settings.PublicMonitor >= 0 && Settings.PublicMonitor < monitors.Length
                    ? monitors[Settings.PublicMonitor].DeviceName
                    : "automática (segundo monitor si existe)") + "\n\n" +
                "Monitores detectados: " + monitors.Length;
            MessageBox.Show(this, info, "Fusion HP — salida", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------ cierre correcto
        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // v2.2 — EL MOTOR ES LO PRINCIPAL (linaje beta 1): cerrar la GUI NO
            // tira la proyección si hay programa cargado y así está configurado.
            // El Motor queda autónomo (teclado sobre la salida, Alt+F4 apaga) y
            // al reabrir la GUI todo se sincroniza desde motor.state.
            clockTimer.Stop();
            bool keep = Settings.KeepEngineAlive && Live.State.HasProgram;
            try
            {
                if (!keep)
                {
                    var p = JsonValue.Object();
                    Live.PostCore("quit", p);   // apagado ordenado de todo el sistema
                }
                Live.Dispose();                 // cierra el pipe ipc.v1 (el Motor sigue)
            }
            catch { }
        }
    }
}
