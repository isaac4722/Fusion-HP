// ============================================================================
//  Fusion-HP · FusionStudio/Ui/MainForm.cs — ventana principal (WinForms
//  estable y ligera [SPEC §7.1]). Tres modos: Inicio (mosaicos), Estudio
//  (editor embebido) y Presentación (arranque por defecto [SPEC §6.5.3]).
//  UX igual o superior a la referencia H-P-Web-Version-Ref (PowerStudio):
//  biblioteca directa sin búsqueda obligatoria, lista de programa con
//  miniaturas y sub-líneas, atajos Holyrics (flechas/Espacio/Esc/B/C/L/G/F5).
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using Fusion.Shared;
using Fusion.Studio.Services;

namespace Fusion.Studio.Ui
{
    /// <summary>Color corporativo (idéntico al acento de la referencia).</summary>
    public static class UiTheme
    {
        public static readonly Color Accent = ColorTranslator.FromHtml("#C43E1C");
        public static readonly Color AccentDark = ColorTranslator.FromHtml("#8A1F11");
        public static readonly Color AccentSoft = ColorTranslator.FromHtml("#F3E9E4");
        public static readonly Color Bg = ColorTranslator.FromHtml("#F3F2F1");
        public static readonly Color Panel = Color.White;
        public static readonly Color Border = ColorTranslator.FromHtml("#EDEBE9");
        public static readonly Color Text = ColorTranslator.FromHtml("#201F1E");
        public static readonly Color TextDim = ColorTranslator.FromHtml("#605E5C");
        public static readonly Color LiveRed = ColorTranslator.FromHtml("#D13438");

        public static Font Normal() { return new Font("Segoe UI", 9f); }
        public static Font NormalBold() { return new Font("Segoe UI", 9f, FontStyle.Bold); }
        public static Font Small() { return new Font("Segoe UI", 8.25f); }
        public static Font Title() { return new Font("Segoe UI", 14f, FontStyle.Bold); }
        public static Font Big() { return new Font("Segoe UI", 12f, FontStyle.Bold); }
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
        Button btnModeHome, btnModeStudio, btnModePresent;
        Label lblClock;
        Label lblLive;
        Panel content;
        Panel status;
        Label lblStatus;
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
        TabControl libTabs;
        ListBox songsList, scenariosList, mediaList;
        TreeView bibleTree;
        TextBox bibleSearch;
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
                p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "img", "app.ico");
                if (System.IO.File.Exists(p)) return new Icon(p);
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------ barra superior
        void BuildTopBar()
        {
            topBar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = UiTheme.Panel };
            var sep = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.Border };
            topBar.Controls.Add(sep);

            var logo = new Panel { Size = new Size(28, 28), Location = new Point(12, 9), BackColor = UiTheme.Accent };
            var logoTxt = new Label { Text = "F", ForeColor = Color.White, Dock = DockStyle.Fill,
                                      TextAlign = ContentAlignment.MiddleCenter, Font = UiTheme.Big() };
            logo.Controls.Add(logoTxt);
            topBar.Controls.Add(logo);

            var title = new Label { Text = "Fusion HP", Font = UiTheme.Title(), Location = new Point(48, 10),
                                    AutoSize = true, ForeColor = UiTheme.Text };
            topBar.Controls.Add(title);

            lblProject = new Label { Text = "Sin proyecto", Font = UiTheme.Small(), ForeColor = UiTheme.TextDim,
                                     Location = new Point(160, 22), AutoSize = true };
            topBar.Controls.Add(lblProject);

            btnModeHome = MakeModeButton("Inicio", 460);
            btnModeStudio = MakeModeButton("Estudio", 530);
            btnModePresent = MakeModeButton("Presentación", 606);
            btnModeHome.Click += delegate { SetMode(Mode.Home); };
            btnModeStudio.Click += delegate { SetMode(Mode.Studio); };
            btnModePresent.Click += delegate { SetMode(Mode.Present); };

            lblLive = new Label { Text = "● EN VIVO", Font = UiTheme.SmallBold(), ForeColor = UiTheme.LiveRed,
                                  Location = new Point(730, 15), AutoSize = true, Visible = false };
            topBar.Controls.Add(lblLive);

            var btnSettings = MakeToolButton("⚙ Configuración", 730);
            btnSettings.Click += delegate
            {
                using (var f = new SettingsForm(this)) f.ShowDialog(this);
            };
            topBar.Controls.Add(btnSettings);

            var btnDiag = MakeToolButton("Estado del sistema", 880);
            btnDiag.Click += delegate
            {
                using (var f = new DiagForm(this)) f.ShowDialog(this);
            };
            topBar.Controls.Add(btnDiag);

            lblClock = new Label { Text = DateTime.Now.ToString("HH:mm:ss"), Font = new Font("Consolas", 10f),
                                   ForeColor = UiTheme.TextDim, Location = new Point(1120, 14), AutoSize = true,
                                   Anchor = AnchorStyles.Top | AnchorStyles.Right };
            topBar.Controls.Add(lblClock);

            Controls.Add(topBar);
        }

        Button MakeModeButton(string text, int x)
        {
            var b = new Button { Text = text, Location = new Point(x, 8), Size = new Size(text.Length * 7 + 24, 30),
                                 FlatStyle = FlatStyle.Flat, BackColor = UiTheme.Bg, ForeColor = UiTheme.Text,
                                 Font = UiTheme.Normal(), UseVisualStyleBackColor = false,
                                 Anchor = AnchorStyles.Top | AnchorStyles.Left };
            b.FlatAppearance.BorderSize = 0;
            topBar.Controls.Add(b);
            return b;
        }

        Button MakeToolButton(string text, int x)
        {
            var b = new Button { Text = text, Location = new Point(x, 8), AutoSize = true, Padding = new Padding(8, 0, 8, 0),
                                 FlatStyle = FlatStyle.Flat, BackColor = UiTheme.Panel, ForeColor = UiTheme.TextDim,
                                 Font = UiTheme.Small(), UseVisualStyleBackColor = false, Height = 30 };
            b.FlatAppearance.BorderSize = 0;
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
            Controls.Add(status);
        }

        // ------------------------------------------------------------ modo
        void SetMode(Mode m)
        {
            mode = m;
            btnModeHome.BackColor = m == Mode.Home ? UiTheme.AccentSoft : UiTheme.Bg;
            btnModeHome.ForeColor = m == Mode.Home ? UiTheme.AccentDark : UiTheme.Text;
            btnModeStudio.BackColor = m == Mode.Studio ? UiTheme.AccentSoft : UiTheme.Bg;
            btnModeStudio.ForeColor = m == Mode.Studio ? UiTheme.AccentDark : UiTheme.Text;
            btnModePresent.BackColor = m == Mode.Present ? UiTheme.AccentSoft : UiTheme.Bg;
            btnModePresent.ForeColor = m == Mode.Present ? UiTheme.AccentDark : UiTheme.Text;

            homePanel.Visible = m == Mode.Home;
            presentPanel.Visible = m == Mode.Present;
            studioPanel.Visible = m == Mode.Studio;

            if (m == Mode.Present) RefreshProgram();
            if (m == Mode.Studio) ActivateEditor();
        }

        void OnFormLoad(object sender, EventArgs e)
        {
            // Conexión al núcleo en segundo plano (arranque ≤3 s [SPEC §10.1])
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = Live.ConnectCore(1);   // núcleo ya lanzado por el bootstrap
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
            string core = Live.CoreConnected ? "núcleo ● conectado" : "núcleo ○ desconectado";
            string api = Settings.ApiEnabled ? ("API :" + Settings.ApiPort) : "API apagada";
            int scen = Live.Project != null ? Live.Project.Scenarios.Count : 0;
            string proj = Live.Project != null ? Live.Project.Name : "Sin proyecto";
            lblProject.Text = proj;
            lblStatus.Text = core + "   ·   " + api + "   ·   " + scen + " escenarios   ·   perfil " + DetectProfile();
            lblLive.Visible = !Live.State.IsBlank;
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
                case Keys.L:
                    Live.Blank(Live.State.BlankMode == "logo" ? "none" : "logo"); e.Handled = true; break;
                case Keys.G:
                    FocusBibleSearch(); e.Handled = true; break;
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
            // Corrección del prototipo: cerrar con la X debe cerrar TODO el
            // sistema (núcleo incluido), sin duplicar ventanas ni procesos.
            clockTimer.Stop();
            try
            {
                Live.Dispose();       // cierra el pipe ipc.v1
            }
            catch { }
            try
            {
                // pedir apagado ordenado del núcleo si sigue vivo
                var p = System.Diagnostics.Process.GetProcessesByName("FusionHP");
                foreach (var proc in p)
                {
                    try
                    {
                        if (!proc.CloseMainWindow()) proc.Kill();
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
