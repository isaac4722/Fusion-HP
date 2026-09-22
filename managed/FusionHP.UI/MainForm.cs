// ============================================================================
//  Fusion-HP / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MainForm.cs : ventana principal (WinForms, todo el layout en código — sin
//  Designer) con 5 pestañas: En Vivo, Canciones, Biblia, Culto, Ajustes.
//
//  REGLA DE ARQUITECTURA: la UI NO duplica lógica de negocio. Todo pasa por
//  FusionHP.Bridge (P/Invoke al núcleo) y FusionHP.Core (builder/JSON local).
//  Los eventos del motor llegan por el SynchronizationContext capturado
//  (UiEventReceived) y se refrescan ListView/vista previa en el hilo de UI.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using fusion.api;
using fusion.bridge;
using fusion.core;

namespace fusion.ui
{
    public sealed class MainForm : Form
    {
        private const string AppTitle = "LuminaPresentation Suite v3.0.0 — Fusion-HP";

        /* ------------------------------------------------------------ servicios */
        private readonly Settings _settings;
        private FusionEngine _engine;                 // headless en no-Windows (ver ctor)
        private ApiServer _api;
        private readonly SynchronizationContext _uiCtx;

        /* --------------------------------------------------------- estado vivo */
        private readonly List<SlideView> _slides = new List<SlideView>();
        private int _currentIndex = -1;
        private bool _black;
        private bool _dbOpen;
        private readonly Theme _theme = new Theme();

        /* --------------------------------------------------------------- culto */
        private readonly List<ScenarioItem> _serviceItems = new List<ScenarioItem>();

        /* ------------------------------------------------------------ controles */
        private MenuStrip _menu;
        private StatusStrip _status;
        private ToolStripStatusLabel _tsslMsg;
        private ToolStripStatusLabel _tsslDb;
        private ToolStripStatusLabel _tsslApi;
        private ToolStripStatusLabel _tsslCore;
        private TabControl _tabs;

        // En Vivo
        private ListView _lvSlides;
        private Button _btnPrev;
        private Button _btnLive;
        private Button _btnNext;
        private Button _btnBlack;
        private Button _btnClear;
        private ComboBox _cmbScreen;
        private CheckBox _chkFullscreen;
        private PictureBox _pbPreview;
        private Label _lblNoPreview;
        private Button _btnRefreshPreview;
        private ToolStripMenuItem _miShowProjector;
        private ToolStripMenuItem _miHideProjector;

        // Canciones
        private TextBox _txtSongTitle;
        private TextBox _txtSongArtist;
        private TextBox _txtSongLyrics;
        private CheckBox _chkHymnMode;
        private NumericUpDown _numTranspose;
        private TextBox _txtSearch;
        private ListView _lvResults;

        // Biblia
        private TextBox _txtRef;
        private TextBox _txtVersion;
        private NumericUpDown _numVersesPerSlide;
        private Label _lblResolve;

        // Culto
        private TextBox _txtServiceName;
        private ListBox _lstItems;

        // Ajustes
        private NumericUpDown _numPort;
        private TextBox _txtToken;
        private Button _btnApiToggle;
        private Label _lblApiState;
        private TextBox _txtDbPath;
        private Label _lblEngineHeadless;

        public MainForm()
        {
            Text = AppTitle;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1150, 740);
            MinimumSize = new Size(960, 640);
            Icon = SystemIcons.Application;
            Font = new Font("Segoe UI", 9F);

            _settings = Settings.Load();

            BuildMenu();
            BuildStatusBar();
            BuildTabs();

            // El contexto de WinForms queda instalado al crear los controles:
            // el motor encola los eventos a este hilo vía ese contexto.
            _uiCtx = SynchronizationContext.Current ?? new SynchronizationContext();

            CreateEngine();
            ApplySettingsToControls();
            UpdateStatusBar();
        }

        /// <summary>Windows real → motor con proyección nativa; otro SO (dev/CI) → headless.</summary>
        private static bool IsWindows()
        {
            PlatformID id = Environment.OSVersion.Platform;
            return id == PlatformID.Win32NT || id == PlatformID.Win32Windows;
        }

        private void CreateEngine()
        {
            bool headless = !IsWindows();
            _engine = FusionEngine.Create(headless, OnEngineEventRaw, _uiCtx);
            _engine.UiEventReceived += OnEngineUiEvent;
        }

        /* ======================================================================
         *  CONSTRUCCIÓN DEL LAYOUT
         * ==================================================================== */

        private void BuildMenu()
        {
            _menu = new MenuStrip();

            ToolStripMenuItem mFile = new ToolStripMenuItem("Archivo");
            ToolStripMenuItem miSave = new ToolStripMenuItem("Guardar ajustes");
            miSave.Click += delegate { SaveSettings(); Status("Ajustes guardados."); };
            ToolStripMenuItem miExit = new ToolStripMenuItem("Salir");
            miExit.Click += delegate { Close(); };
            mFile.DropDownItems.Add(miSave);
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add(miExit);

            ToolStripMenuItem mProj = new ToolStripMenuItem("Proyectar");
            _miShowProjector = new ToolStripMenuItem("Mostrar proyector");
            _miShowProjector.Click += delegate { ShowProjector(); };
            _miHideProjector = new ToolStripMenuItem("Ocultar proyector");
            _miHideProjector.Click += delegate
            {
                int st = _engine.ProjectorHide();
                Status(st == FusionStatus.Ok
                    ? "Proyector oculto."
                    : "El núcleo no puede ocultar el proyector (" + FusionStatus.Name(st) + ").");
            };
            mProj.DropDownItems.Add(_miShowProjector);
            mProj.DropDownItems.Add(_miHideProjector);

            ToolStripMenuItem mHelp = new ToolStripMenuItem("Ayuda");
            ToolStripMenuItem miAbout = new ToolStripMenuItem("Acerca de…");
            miAbout.Click += delegate
            {
                MessageBox.Show(this,
                    "LuminaPresentation Suite v3.0.0 «HÍBRIDA»\n" +
                    "Núcleo: " + SafeCoreVersion() + "\n\n" +
                    "Arquitectura híbrida C++/C# — API local en 127.0.0.1.\n" +
                    "Copyright (c) 2026 Isaac. Licencia View-Only.",
                    "Acerca de", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            mHelp.DropDownItems.Add(miAbout);

            _menu.Items.Add(mFile);
            _menu.Items.Add(mProj);
            _menu.Items.Add(mHelp);
            MainMenuStrip = _menu;
            Controls.Add(_menu);
        }

        private void BuildStatusBar()
        {
            _status = new StatusStrip();
            _tsslMsg = new ToolStripStatusLabel("Listo.");
            _tsslMsg.Spring = true;
            _tsslMsg.TextAlign = ContentAlignment.MiddleLeft;
            _tsslDb = new ToolStripStatusLabel("BD: —");
            _tsslApi = new ToolStripStatusLabel("API: detenida");
            _tsslCore = new ToolStripStatusLabel("Núcleo: …");
            _status.Items.Add(_tsslMsg);
            _status.Items.Add(new ToolStripStatusLabel("|"));
            _status.Items.Add(_tsslDb);
            _status.Items.Add(new ToolStripStatusLabel("|"));
            _status.Items.Add(_tsslApi);
            _status.Items.Add(new ToolStripStatusLabel("|"));
            _status.Items.Add(_tsslCore);
            Controls.Add(_status);
        }

        private void BuildTabs()
        {
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.TabPages.Add(BuildLiveTab());
            _tabs.TabPages.Add(BuildSongsTab());
            _tabs.TabPages.Add(BuildBibleTab());
            _tabs.TabPages.Add(BuildServiceTab());
            _tabs.TabPages.Add(BuildSettingsTab());
            _tabs.SelectedIndexChanged += delegate
            {
                if (_tabs.SelectedIndex == 0) RefreshPreview();
            };
            Controls.Add(_tabs);
            _tabs.BringToFront();
        }

        /* ---------------------------------------------- pestaña 1: En Vivo --- */

        private TabPage BuildLiveTab()
        {
            TabPage tab = new TabPage("En Vivo");
            Padding p = new Padding(8);
            tab.Padding = p;

            // Panel derecho: vista previa del motor.
            GroupBox gbPreview = new GroupBox();
            gbPreview.Text = "Vista previa";
            gbPreview.Dock = DockStyle.Right;
            gbPreview.Width = 500;
            _pbPreview = new PictureBox();
            _pbPreview.Dock = DockStyle.Fill;
            _pbPreview.BackColor = Color.FromArgb(24, 28, 36);
            _pbPreview.SizeMode = PictureBoxSizeMode.Zoom;
            _lblNoPreview = new Label();
            _lblNoPreview.Dock = DockStyle.Fill;
            _lblNoPreview.Text = "Vista previa no disponible (headless)";
            _lblNoPreview.TextAlign = ContentAlignment.MiddleCenter;
            _lblNoPreview.ForeColor = Color.DimGray;
            _lblNoPreview.BackColor = Color.FromArgb(24, 28, 36);
            _btnRefreshPreview = new Button();
            _btnRefreshPreview.Text = "↻ Refrescar";
            _btnRefreshPreview.Dock = DockStyle.Bottom;
            _btnRefreshPreview.Click += delegate { RefreshPreview(); };
            // El label tapa al PictureBox cuando no hay imagen (semáforo por Visible).
            gbPreview.Controls.Add(_pbPreview);
            gbPreview.Controls.Add(_lblNoPreview);
            gbPreview.Controls.Add(_btnRefreshPreview);
            _lblNoPreview.BringToFront();

            // Panel inferior: controles de conducción.
            FlowLayoutPanel bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 48;
            bottom.FlowDirection = FlowDirection.LeftToRight;
            bottom.Padding = new Padding(4);
            bottom.WrapContents = false;

            _btnPrev = MkButton("◀ Anterior", delegate { _engine.Prev(); });
            _btnLive = MkButton("En Vivo", delegate { ShowSelectedSlide(); });
            _btnNext = MkButton("Siguiente ▶", delegate { _engine.Next(); });
            _btnBlack = MkButton("Negro", delegate { ToggleBlack(); });
            _btnClear = MkButton("Limpiar", delegate { _engine.Clear(); });

            Label lblScreen = MkLabel("Pantalla:");
            _cmbScreen = new ComboBox();
            _cmbScreen.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbScreen.Items.Add("0");
            _cmbScreen.Items.Add("1");
            _cmbScreen.Items.Add("2");
            _cmbScreen.SelectedIndex = 0;
            _cmbScreen.Width = 50;
            _chkFullscreen = new CheckBox();
            _chkFullscreen.Text = "Pantalla completa";
            _chkFullscreen.AutoSize = true;
            _chkFullscreen.Padding = new Padding(6, 6, 0, 0);

            bottom.Controls.Add(_btnPrev);
            bottom.Controls.Add(_btnLive);
            bottom.Controls.Add(_btnNext);
            bottom.Controls.Add(_btnBlack);
            bottom.Controls.Add(_btnClear);
            bottom.Controls.Add(lblScreen);
            bottom.Controls.Add(_cmbScreen);
            bottom.Controls.Add(_chkFullscreen);

            // Lista de slides.
            _lvSlides = new ListView();
            _lvSlides.View = View.Details;
            _lvSlides.FullRowSelect = true;
            _lvSlides.MultiSelect = false;
            _lvSlides.HideSelection = true;
            _lvSlides.Dock = DockStyle.Fill;
            _lvSlides.Columns.Add("#", 44, HorizontalAlignment.Right);
            _lvSlides.Columns.Add("Referencia", 170, HorizontalAlignment.Left);
            _lvSlides.Columns.Add("Primera línea", 380, HorizontalAlignment.Left);
            _lvSlides.DoubleClick += delegate { ShowSelectedSlide(); };

            tab.Controls.Add(_lvSlides);
            tab.Controls.Add(bottom);
            tab.Controls.Add(gbPreview);
            return tab;
        }

        /* ------------------------------------------- pestaña 2: Canciones ---- */

        private TabPage BuildSongsTab()
        {
            TabPage tab = new TabPage("Canciones");
            tab.Padding = new Padding(8);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 4;
            grid.RowCount = 5;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            grid.Controls.Add(MkLabel("Título:"), 0, 0);
            _txtSongTitle = new TextBox();
            _txtSongTitle.Dock = DockStyle.Fill;
            grid.Controls.Add(_txtSongTitle, 1, 0);
            grid.Controls.Add(MkLabel("Artista:"), 2, 0);
            _txtSongArtist = new TextBox();
            _txtSongArtist.Dock = DockStyle.Fill;
            grid.Controls.Add(_txtSongArtist, 3, 0);

            grid.Controls.Add(MkLabel("Letras:"), 0, 1);
            _txtSongLyrics = new TextBox();
            _txtSongLyrics.Multiline = true;
            _txtSongLyrics.ScrollBars = ScrollBars.Vertical;
            _txtSongLyrics.Dock = DockStyle.Fill;
            _txtSongLyrics.Text = "[Verso 1]\nPrimera línea de la letra\nsegunda línea\n\n[Coro]\nAleluya, aleluya\n";
            grid.Controls.Add(_txtSongLyrics, 1, 1);
            grid.SetColumnSpan(_txtSongLyrics, 3);

            FlowLayoutPanel opts = new FlowLayoutPanel();
            opts.Dock = DockStyle.Fill;
            opts.WrapContents = false;
            _chkHymnMode = new CheckBox();
            _chkHymnMode.Text = "Modo Hinario (coro intercalado)";
            _chkHymnMode.AutoSize = true;
            _chkHymnMode.Padding = new Padding(0, 6, 8, 0);
            Label lblTrans = MkLabel("Transponer:");
            _numTranspose = new NumericUpDown();
            _numTranspose.Minimum = -11;
            _numTranspose.Maximum = 11;
            _numTranspose.Width = 48;
            _numTranspose.Margin = new Padding(3, 6, 3, 3);
            Button btnLoad = MkButton("Cargar al escenario", delegate { LoadSongToStage(); });
            Button btnSave = MkButton("Guardar en BD", delegate { SaveSongToDb(); });
            opts.Controls.Add(_chkHymnMode);
            opts.Controls.Add(lblTrans);
            opts.Controls.Add(_numTranspose);
            opts.Controls.Add(btnLoad);
            opts.Controls.Add(btnSave);

            grid.Controls.Add(opts, 0, 2);
            grid.SetColumnSpan(opts, 4);

            FlowLayoutPanel searchRow = new FlowLayoutPanel();
            searchRow.Dock = DockStyle.Fill;
            searchRow.WrapContents = false;
            searchRow.Controls.Add(MkLabel("Buscar:"));
            _txtSearch = new TextBox();
            _txtSearch.Width = 320;
            _txtSearch.Margin = new Padding(3, 6, 3, 3);
            _txtSearch.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SearchSongs(); }
            };
            Button btnSearch = MkButton("Buscar", delegate { SearchSongs(); });
            searchRow.Controls.Add(_txtSearch);
            searchRow.Controls.Add(btnSearch);
            grid.Controls.Add(searchRow, 0, 3);
            grid.SetColumnSpan(searchRow, 4);

            _lvResults = new ListView();
            _lvResults.View = View.Details;
            _lvResults.FullRowSelect = true;
            _lvResults.MultiSelect = false;
            _lvResults.Dock = DockStyle.Fill;
            _lvResults.Columns.Add("id", 50, HorizontalAlignment.Right);
            _lvResults.Columns.Add("Título", 240, HorizontalAlignment.Left);
            _lvResults.Columns.Add("Autor", 160, HorizontalAlignment.Left);
            _lvResults.Columns.Add("Letra (inicio)", 320, HorizontalAlignment.Left);
            _lvResults.DoubleClick += delegate { LoadResultToEditor(); };

            grid.Controls.Add(_lvResults, 0, 4);
            grid.SetColumnSpan(_lvResults, 4);

            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 48));

            tab.Controls.Add(grid);
            return tab;
        }

        /* ------------------------------------------------ pestaña 3: Biblia -- */

        private TabPage BuildBibleTab()
        {
            TabPage tab = new TabPage("Biblia");
            tab.Padding = new Padding(8);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Top;
            grid.Height = 150;
            grid.ColumnCount = 2;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            grid.Controls.Add(MkLabel("Referencia:"), 0, 0);
            _txtRef = new TextBox();
            _txtRef.Dock = DockStyle.Fill;
            _txtRef.Text = "Jn 3:16";
            grid.Controls.Add(_txtRef, 1, 0);

            grid.Controls.Add(MkLabel("Versión:"), 0, 1);
            _txtVersion = new TextBox();
            _txtVersion.Dock = DockStyle.Fill;
            grid.Controls.Add(_txtVersion, 1, 1);

            FlowLayoutPanel rowVps = new FlowLayoutPanel();
            rowVps.Dock = DockStyle.Fill;
            rowVps.WrapContents = false;
            rowVps.Controls.Add(MkLabel("Versos por slide:"));
            _numVersesPerSlide = new NumericUpDown();
            _numVersesPerSlide.Minimum = 1;
            _numVersesPerSlide.Maximum = 4;
            _numVersesPerSlide.Value = 1;
            _numVersesPerSlide.Width = 44;
            _numVersesPerSlide.Margin = new Padding(3, 6, 3, 3);
            rowVps.Controls.Add(_numVersesPerSlide);
            grid.Controls.Add(rowVps, 1, 2);

            FlowLayoutPanel rowBtns = new FlowLayoutPanel();
            rowBtns.Dock = DockStyle.Fill;
            rowBtns.WrapContents = false;
            Button btnLoad = MkButton("Cargar al escenario", delegate { LoadScriptureToStage(); });
            Button btnResolve = MkButton("Resolver referencia", delegate { ResolveReference(); });
            rowBtns.Controls.Add(btnLoad);
            rowBtns.Controls.Add(btnResolve);
            grid.Controls.Add(rowBtns, 1, 3);

            grid.Controls.Add(MkLabel(""), 0, 3);
            _lblResolve = new Label();
            _lblResolve.Dock = DockStyle.Fill;
            _lblResolve.ForeColor = Color.Navy;
            grid.Controls.Add(_lblResolve, 1, 4);

            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            tab.Controls.Add(grid);
            return tab;
        }

        /* ------------------------------------------------- pestaña 4: Culto -- */

        private TabPage BuildServiceTab()
        {
            TabPage tab = new TabPage("Culto");
            tab.Padding = new Padding(8);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 2;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));

            FlowLayoutPanel top = new FlowLayoutPanel();
            top.Dock = DockStyle.Fill;
            top.WrapContents = false;
            top.Controls.Add(MkLabel("Nombre:"));
            _txtServiceName = new TextBox();
            _txtServiceName.Width = 240;
            _txtServiceName.Text = "Culto";
            top.Controls.Add(_txtServiceName);
            grid.Controls.Add(top, 0, 0);

            TableLayoutPanel listHost = new TableLayoutPanel();
            listHost.Dock = DockStyle.Fill;
            listHost.ColumnCount = 1;
            listHost.RowCount = 2;

            _lstItems = new ListBox();
            _lstItems.Dock = DockStyle.Fill;
            _lstItems.HorizontalScrollbar = true;
            listHost.Controls.Add(_lstItems, 0, 0);

            FlowLayoutPanel itemBtns = new FlowLayoutPanel();
            itemBtns.Dock = DockStyle.Fill;
            itemBtns.WrapContents = false;
            itemBtns.Controls.Add(MkButton("Subir", delegate { MoveItem(-1); }));
            itemBtns.Controls.Add(MkButton("Bajar", delegate { MoveItem(1); }));
            itemBtns.Controls.Add(MkButton("Eliminar", delegate { RemoveItem(); }));
            listHost.Controls.Add(itemBtns, 0, 1);
            listHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            listHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            grid.Controls.Add(listHost, 0, 1);

            FlowLayoutPanel side = new FlowLayoutPanel();
            side.Dock = DockStyle.Fill;
            side.FlowDirection = FlowDirection.TopDown;
            side.WrapContents = false;
            side.Padding = new Padding(6);
            Button btnImportSong = MkButton("Importar canción JSON…", delegate { ImportSongJson(); });
            btnImportSong.Width = 220;
            Button btnImportBib = MkButton("Importar Biblia .BIB…", delegate { ImportBibFile(); });
            btnImportBib.Width = 220;
            Button btnSend = MkButton("Enviar a proyección", delegate { SendServiceToStage(); });
            btnSend.Width = 220;
            Label spacer = new Label();
            spacer.Height = 12;
            side.Controls.Add(btnImportSong);
            side.Controls.Add(btnImportBib);
            side.Controls.Add(spacer);
            side.Controls.Add(btnSend);
            grid.Controls.Add(side, 1, 1);

            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            tab.Controls.Add(grid);
            RefreshServiceList();
            return tab;
        }

        /* ----------------------------------------------- pestaña 5: Ajustes -- */

        private TabPage BuildSettingsTab()
        {
            TabPage tab = new TabPage("Ajustes");
            tab.Padding = new Padding(8);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Top;
            grid.Height = 240;
            grid.ColumnCount = 3;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));

            grid.Controls.Add(MkLabel("Puerto API:"), 0, 0);
            _numPort = new NumericUpDown();
            _numPort.Minimum = 1024;
            _numPort.Maximum = 65535;
            _numPort.Value = 8069;
            _numPort.Width = 100;
            grid.Controls.Add(_numPort, 1, 0);

            grid.Controls.Add(MkLabel("Token API:"), 0, 1);
            _txtToken = new TextBox();
            _txtToken.Dock = DockStyle.Fill;
            grid.Controls.Add(_txtToken, 1, 1);

            _btnApiToggle = MkButton("Iniciar API", delegate { ToggleApi(); });
            _btnApiToggle.Width = 130;
            grid.Controls.Add(_btnApiToggle, 0, 2);
            _lblApiState = new Label();
            _lblApiState.Dock = DockStyle.Fill;
            _lblApiState.Text = "● DETENIDA";
            _lblApiState.ForeColor = Color.Firebrick;
            _lblApiState.TextAlign = ContentAlignment.MiddleLeft;
            grid.Controls.Add(_lblApiState, 1, 2);

            grid.Controls.Add(MkLabel("Base de datos:"), 0, 3);
            _txtDbPath = new TextBox();
            _txtDbPath.Dock = DockStyle.Fill;
            grid.Controls.Add(_txtDbPath, 1, 3);
            FlowLayoutPanel dbBtns = new FlowLayoutPanel();
            dbBtns.Dock = DockStyle.Fill;
            dbBtns.WrapContents = false;
            Button btnBrowseDb = MkButton("…", delegate { BrowseDbPath(); });
            btnBrowseDb.Width = 40;
            Button btnDbOpen = MkButton("Crear/Abrir BD", delegate { OpenDatabase(); });
            btnDbOpen.Width = 120;
            dbBtns.Controls.Add(btnBrowseDb);
            dbBtns.Controls.Add(btnDbOpen);
            grid.Controls.Add(dbBtns, 2, 3);

            grid.Controls.Add(MkLabel("Versión del núcleo:"), 0, 4);
            _lblEngineHeadless = new Label();
            _lblEngineHeadless.Dock = DockStyle.Fill;
            _lblEngineHeadless.TextAlign = ContentAlignment.MiddleLeft;
            _lblEngineHeadless.Text = SafeCoreVersion() +
                (IsWindows() ? "  (proyección nativa)" : "  (headless en este SO)");
            grid.Controls.Add(_lblEngineHeadless, 1, 4);

            for (int i = 0; i < 5; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

            tab.Controls.Add(grid);
            return tab;
        }

        /* --------------------------------------------------- helpers visuales */

        private static Button MkButton(string text, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.Padding = new Padding(2);
            b.Margin = new Padding(3, 6, 3, 3);
            if (onClick != null) b.Click += onClick;
            return b;
        }

        private static Label MkLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Margin = new Padding(3, 8, 3, 3);
            return l;
        }

        /* ======================================================================
         *  MOTOR / EVENTOS
         * ==================================================================== */

        // Hilo del MOTOR: solo diagnóstico (el camino de la UI es UiEventReceived).
        private void OnEngineEventRaw(object sender, FusionEvent e)
        {
            if (e.Code == FusionEvents.Error)
            {
                try { TraceLine("EV_ERROR " + e.Text); } catch (Exception) { }
            }
        }

        // Hilo de UI (encolado por el SynchronizationContext capturado en el puente).
        private void OnEngineUiEvent(object sender, FusionEvent e)
        {
            // Doble defensa: si el contexto no pudo encolar (form cerrándose),
            // el delegate podría correr fuera del hilo de UI.
            if (InvokeRequired)
            {
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke(new SendOrPostCallback(delegate { HandleEngineEvent(e); })); }
                catch (Exception) { }
                return;
            }
            HandleEngineEvent(e);
        }

        private void HandleEngineEvent(FusionEvent e)
        {
            switch (e.Code)
            {
                case FusionEvents.State:
                    ApplyStateJson(e.Text);
                    break;
                case FusionEvents.SlideChanged:
                    // {"index":N,"total":T}
                    Dictionary<string, object> o = null;
                    try { o = MiniJson.Parse(e.Text); } catch (FormatException) { }
                    if (o != null)
                    {
                        _currentIndex = (int)MiniJson.GetInt(o, "index", _currentIndex);
                        HighlightCurrentSlide();
                        RefreshPreview();
                    }
                    break;
                case FusionEvents.Error:
                    Status("Error del núcleo: " + e.Text);
                    break;
                default:
                    break; // LOG/ITEM_CHANGED/PONG: ruido para la UI principal
            }
        }

        private void ApplyStateJson(string json)
        {
            Dictionary<string, object> st = null;
            try { st = MiniJson.Parse(json); } catch (FormatException) { }
            if (st == null) return;
            _currentIndex = (int)MiniJson.GetInt(st, "current", _currentIndex);
            _black = MiniJson.GetBool(st, "black", _black);
            HighlightCurrentSlide();
            RefreshPreview();
        }

        private void Status(string msg)
        {
            _tsslMsg.Text = msg;
        }

        private static void TraceLine(string msg)
        {
            System.Diagnostics.Trace.WriteLine("FusionHP.UI: " + msg);
        }

        private string SafeCoreVersion()
        {
            try { return FusionEngine.Version(); }
            catch (Exception) { return "no disponible"; }
        }

        /* ======================================================================
         *  EN VIVO
         * ==================================================================== */

        private void ShowSelectedSlide()
        {
            if (_lvSlides.SelectedIndices.Count > 0)
            {
                int idx = _lvSlides.SelectedIndices[0];
                _engine.ShowSlide(idx);
            }
            else if (_currentIndex >= 0)
            {
                _engine.ShowSlide(_currentIndex);
            }
            else
            {
                Status("No hay slide seleccionada.");
            }
        }

        private void ToggleBlack()
        {
            _engine.Black(!_black);
        }

        private void ShowProjector()
        {
            int screen = _cmbScreen.SelectedIndex < 0 ? 0 : _cmbScreen.SelectedIndex;
            int st = _engine.ProjectorShow(screen, _chkFullscreen.Checked);
            Status(st == FusionStatus.Ok
                ? "Proyector mostrado (pantalla " + screen + (_chkFullscreen.Checked ? ", completa)." : ").")
                : "El núcleo no puede proyectar (" + FusionStatus.Name(st) + "). " +
                  "La proyección nativa requiere el núcleo Windows (no headless).");
        }

        /// <summary>Reconstruye la lista de slides (índice + refLabel + primera línea).</summary>
        private void RefreshLiveList()
        {
            _lvSlides.BeginUpdate();
            try
            {
                _lvSlides.Items.Clear();
                foreach (SlideView v in _slides)
                {
                    ListViewItem it = new ListViewItem(v.Index.ToString(CultureInfo.InvariantCulture));
                    it.SubItems.Add(v.RefLabel);
                    it.SubItems.Add(v.FirstLine);
                    _lvSlides.Items.Add(it);
                }
            }
            finally
            {
                _lvSlides.EndUpdate();
            }
            HighlightCurrentSlide();
        }

        private void HighlightCurrentSlide()
        {
            if (_currentIndex < 0 || _currentIndex >= _lvSlides.Items.Count) return;
            if (_lvSlides.SelectedIndices.Count == 0 ||
                _lvSlides.SelectedIndices[0] != _currentIndex)
            {
                _lvSlides.SelectedIndices.Clear();
                if (_currentIndex < _lvSlides.Items.Count)
                {
                    _lvSlides.Items[_currentIndex].Selected = true;
                    _lvSlides.Items[_currentIndex].Focused = true;
                    _lvSlides.EnsureVisible(_currentIndex);
                }
            }
        }

        /// <summary>Pide el PNG al motor y lo pinta (nueva Bitmap para desacoplar el stream).</summary>
        private void RefreshPreview()
        {
            Image old = _pbPreview.Image;
            _pbPreview.Image = null;
            if (old != null)
            {
                old.Dispose();
                old = null;
            }
            byte[] png = null;
            if (_currentIndex >= 0)
            {
                try { png = _engine.RenderPreviewPng(_currentIndex); }
                catch (Exception) { png = null; }
            }
            if (png != null && png.Length > 0)
            {
                try
                {
                    using (MemoryStream ms = new MemoryStream(png))
                    using (Image img = Image.FromStream(ms))
                    {
                        _pbPreview.Image = new Bitmap(img);
                    }
                }
                catch (Exception)
                {
                    _pbPreview.Image = null;
                }
            }
            _lblNoPreview.Visible = _pbPreview.Image == null;
        }

        /* ======================================================================
         *  ESCENARIO (builder → motor)
         * ==================================================================== */

        private void LoadScenarioFromItems(IList<ScenarioItem> items, string name)
        {
            string json = ScenarioBuilder.BuildScenarioJson(name, _theme, items);
            int st = _engine.LoadScenario(json);
            if (st != FusionStatus.Ok)
            {
                Status("El núcleo rechazó el escenario (" + FusionStatus.Name(st) + ").");
                return;
            }
            _slides.Clear();
            _slides.AddRange(ScenarioBuilder.FlattenScenario(json,
                delegate(string songJson) { return FusionEngine.SongParse(songJson); }));
            if (_api != null) _api.SetSlideCatalog(_slides);
            RefreshLiveList();
            _tabs.SelectedIndex = 0;
            Status("Escenario «" + name + "» cargado: " + _slides.Count + " slide(s).");
        }

        private void LoadSongToStage()
        {
            Song s = ScenarioBuilder.SongFromEditor(
                _txtSongTitle.Text, _txtSongArtist.Text, _txtSongLyrics.Text,
                _chkHymnMode.Checked, (int)_numTranspose.Value);
            if (s.Title.Length == 0) s.Title = "Sin título";
            if (s.Blocks.Count == 0)
            {
                Status("La letra está vacía: escribe bloques como [Verso 1] / [Coro].");
                return;
            }
            LoadScenarioFromItems(new ScenarioItem[] { ScenarioBuilder.FromSong(s) }, s.Title);
        }

        private void LoadScriptureToStage()
        {
            string reference = _txtRef.Text.Trim();
            if (reference.Length == 0)
            {
                Status("Escribe una referencia (p. ej. Jn 3:16-18).");
                return;
            }
            string version = _txtVersion.Text.Trim();
            ScenarioItem it = ScenarioBuilder.ScriptureItem(
                reference, version, (int)_numVersesPerSlide.Value, string.Empty);
            if (!_dbOpen)
                Status("Nota: sin BD abierta el núcleo no puede resolver los versículos.");
            LoadScenarioFromItems(new ScenarioItem[] { it }, reference);
            if (version.Length > 0)
            {
                _settings.LastBibleVersion = version;
                SaveSettings();
            }
        }

        private void ResolveReference()
        {
            string reference = _txtRef.Text.Trim();
            if (reference.Length == 0) { _lblResolve.Text = "Escribe una referencia."; return; }
            try
            {
                string resJson = FusionEngine.BibleRefResolve(reference);
                Dictionary<string, object> o = MiniJson.Parse(resJson);
                long book = MiniJson.GetInt(o, "book", 0);
                long chapter = MiniJson.GetInt(o, "chapter", 0);
                long verse = MiniJson.GetInt(o, "verse", 0);
                string name = MiniJson.GetString(o, "name", "?");
                long valid = MiniJson.GetInt(o, "valid", 0);
                _lblResolve.Text = valid == 1
                    ? string.Format(CultureInfo.InvariantCulture,
                        "{0} → libro {1}, capítulo {2}, versículo {3} ({4})",
                        reference, book, chapter, verse, name)
                    : "Referencia no reconocida: " + reference;
            }
            catch (FusionException ex)
            {
                _lblResolve.Text = "Error del núcleo: " + ex.Message;
            }
            catch (FormatException)
            {
                _lblResolve.Text = "Respuesta inválida del núcleo.";
            }
        }

        /* ======================================================================
         *  CANCIONES — BD
         * ==================================================================== */

        private void SaveSongToDb()
        {
            if (!RequireDb()) return;
            string lyrics = _txtSongLyrics.Text ?? string.Empty;
            string title = _txtSongTitle.Text.Trim();
            if (title.Length == 0) { Status("La canción necesita título."); return; }
            try
            {
                string res = _engine.DbExec(FusionStorage.InsertSongRequest(
                    title, _txtSongArtist.Text.Trim(), string.Empty, 0, string.Empty, lyrics));
                long id = FusionStorage.LastId(res);
                Status("Canción guardada en la BD (id " + id + ").");
            }
            catch (FusionException ex)
            {
                Status("No se pudo guardar: " + ex.Message);
            }
        }

        private void SearchSongs()
        {
            if (!RequireDb()) return;
            string term = _txtSearch.Text.Trim();
            if (term.Length == 0) { Status("Escribe un término de búsqueda."); return; }
            try
            {
                string res = _engine.DbExec(FusionStorage.SearchSongsRequest(term, 50));
                List<List<object>> rows = FusionStorage.Rows(res);
                _lvResults.BeginUpdate();
                try
                {
                    _lvResults.Items.Clear();
                    foreach (List<object> row in rows)
                    {
                        string id = Cell(row, 0);
                        string title = Cell(row, 1);
                        string author = Cell(row, 2);
                        string lyrics = Cell(row, 3);
                        ListViewItem it = new ListViewItem(id);
                        it.SubItems.Add(title);
                        it.SubItems.Add(author);
                        it.SubItems.Add(lyrics.Length > 80 ? lyrics.Substring(0, 80) : lyrics);
                        _lvResults.Items.Add(it);
                    }
                }
                finally
                {
                    _lvResults.EndUpdate();
                }
                Status("Búsqueda «" + term + "»: " + rows.Count + " resultado(s).");
            }
            catch (FusionException ex)
            {
                Status("Búsqueda falló: " + ex.Message);
            }
        }

        private void LoadResultToEditor()
        {
            if (!RequireDb() || _lvResults.SelectedItems.Count == 0) return;
            long id;
            if (!long.TryParse(_lvResults.SelectedItems[0].Text, out id)) return;
            try
            {
                string res = _engine.DbExec(FusionStorage.SelectSongByIdRequest(id));
                List<List<object>> rows = FusionStorage.Rows(res);
                if (rows.Count == 0) { Status("La canción ya no existe."); return; }
                List<object> row = rows[0];
                _txtSongTitle.Text = row.Count > 1 ? Cell(row, 1) : string.Empty;
                _txtSongArtist.Text = row.Count > 2 ? Cell(row, 2) : string.Empty;
                _txtSongLyrics.Text = row.Count > 6 ? Cell(row, 6) : string.Empty;
                Status("Canción #" + id + " cargada al editor.");
            }
            catch (FusionException ex)
            {
                Status("No se pudo leer la canción: " + ex.Message);
            }
        }

        private static string Cell(List<object> row, int idx)
        {
            if (row == null || idx >= row.Count || row[idx] == null) return string.Empty;
            return Convert.ToString(row[idx], CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private bool RequireDb()
        {
            if (_dbOpen) return true;
            Status("Abre (o crea) una base de datos en la pestaña Ajustes.");
            return false;
        }

        /* ======================================================================
         *  CULTO
         * ==================================================================== */

        private void RefreshServiceList()
        {
            _lstItems.BeginUpdate();
            try
            {
                _lstItems.Items.Clear();
                for (int i = 0; i < _serviceItems.Count; i++)
                {
                    ScenarioItem it = _serviceItems[i];
                    string label = (i + 1).ToString(CultureInfo.InvariantCulture) + ". [" +
                        it.Kind + "] " + it.Title;
                    if (it.Kind == "song" && it.Song != null && it.Song.Artist.Length > 0)
                        label += " — " + it.Song.Artist;
                    _lstItems.Items.Add(label);
                }
            }
            finally
            {
                _lstItems.EndUpdate();
            }
        }

        private void MoveItem(int delta)
        {
            if (_lstItems.SelectedIndex < 0) return;
            int i = _lstItems.SelectedIndex;
            int j = i + delta;
            if (j < 0 || j >= _serviceItems.Count) return;
            ScenarioItem tmp = _serviceItems[i];
            _serviceItems[i] = _serviceItems[j];
            _serviceItems[j] = tmp;
            RefreshServiceList();
            _lstItems.SelectedIndex = j;
        }

        private void RemoveItem()
        {
            if (_lstItems.SelectedIndex < 0) return;
            int i = _lstItems.SelectedIndex;
            _serviceItems.RemoveAt(i);
            RefreshServiceList();
            if (i < _lstItems.Items.Count) _lstItems.SelectedIndex = i;
        }

        private void ImportSongJson()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Importar canción JSON";
                dlg.Filter = "Canción JSON (*.json)|*.json|Todos los archivos (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string json = MiniJson.Utf8BytesToString(File.ReadAllBytes(dlg.FileName));
                    string parsed = FusionEngine.SongParse(json);   // valida ANTES de agregar
                    Dictionary<string, object> res = MiniJson.Parse(parsed);
                    if (MiniJson.GetInt(res, "ok", 0) != 1)
                    {
                        MessageBox.Show(this, "El núcleo no aceptó la canción.", "Importar",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    Dictionary<string, object> songDict = MiniJson.GetObject(res, "song");
                    Song song = ScenarioBuilder.SongFromDict(songDict);
                    if (song.Title.Length == 0) song.Title = Path.GetFileNameWithoutExtension(dlg.FileName);
                    _serviceItems.Add(ScenarioBuilder.FromSong(song));
                    RefreshServiceList();
                    Status("Canción «" + song.Title + "» agregada al culto.");
                }
                catch (FusionException ex)
                {
                    MessageBox.Show(this, "La canción no es válida: " + ex.Message, "Importar",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "No se pudo leer el archivo: " + ex.Message, "Importar",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void ImportBibFile()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Importar Biblia .BIB";
                dlg.Filter = "Biblia (*.bib)|*.bib|Todos los archivos (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                byte[] data;
                try { data = File.ReadAllBytes(dlg.FileName); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "No se pudo leer el archivo: " + ex.Message, "Importar",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string statsJson;
                try
                {
                    // wantRows:1 → si el núcleo lo soporta, devuelve además las filas.
                    statsJson = FusionEngine.BibParse(data, "{\"wantRows\":1}");
                }
                catch (FusionException ex)
                {
                    MessageBox.Show(this, "El archivo .BIB no es válido: " + ex.Message, "Importar",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Dictionary<string, object> stats;
                try { stats = MiniJson.Parse(statsJson); }
                catch (FormatException)
                {
                    MessageBox.Show(this, "Respuesta inválida del núcleo.", "Importar",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                long verses = MiniJson.GetInt(stats, "verses", 0);
                long books = MiniJson.GetInt(stats, "books", 0);
                string version = MiniJson.GetString(stats, "version", string.Empty);
                string name = MiniJson.GetString(stats, "name", string.Empty);
                long errors = MiniJson.GetInt(stats, "errors", 0);

                MessageBox.Show(this,
                    "Biblia validada:\n" +
                    "  Versión: " + version + (name.Length > 0 ? " (" + name + ")" : string.Empty) + "\n" +
                    "  Libros: " + books + "\n" +
                    "  Versículos: " + verses + "\n" +
                    "  Filas descartadas: " + errors,
                    "Importar Biblia", MessageBoxButtons.OK, MessageBoxIcon.Information);

                if (MessageBox.Show(this, "¿Insertar esta biblia en la base de datos?",
                        "Importar Biblia", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                    != DialogResult.Yes) return;

                if (!RequireDb()) return;

                List<object> rows = MiniJson.GetArray(stats, "rows");
                if (rows.Count == 0)
                {
                    // El núcleo no devolvió filas (solo estadísticas): no hay datos
                    // para insertar desde C# — avisar que SOLO se validó.
                    MessageBox.Show(this,
                        "El núcleo no devolvió filas para insertar (solo estadísticas):\n" +
                        "el archivo fue VALIDADO pero NO se insertó nada.\n\n" +
                        "Vuelve a intentarlo con un núcleo que soporte {\"wantRows\":1}.",
                        "Importar Biblia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                InsertBibleRows(version, rows, verses);
            }
        }

        /// <summary>INSERT por lotes en transacción (BEGIN/COMMIT vía fusion_db_exec).</summary>
        private void InsertBibleRows(string version, List<object> rows, long expectedVerses)
        {
            try
            {
                string beginRes = _engine.DbExec(FusionStorage.RawSqlRequest("BEGIN"));
                if (beginRes == null)
                {
                    Status("No se pudo iniciar la transacción.");
                    return;
                }
                long inserted = 0;
                try
                {
                    foreach (object ro in rows)
                    {
                        List<object> row = ro as List<object>;
                        if (row == null) continue;
                        // Formatos tolerados: [book,ch,verse,text] o [version,book,ch,verse,text]
                        string v = row.Count >= 5 ? Cell(row, 0) : version;
                        int off = row.Count >= 5 ? 1 : 0;
                        long book, chapter, verse;
                        if (!long.TryParse(Cell(row, off), out book) ||
                            !long.TryParse(Cell(row, off + 1), out chapter) ||
                            !long.TryParse(Cell(row, off + 2), out verse)) continue;
                        string text = Cell(row, off + 3);
                        _engine.DbExec(FusionStorage.InsertBibleVerseRequest(v, book, chapter, verse, text));
                        inserted++;
                    }
                    _engine.DbExec(FusionStorage.RawSqlRequest("COMMIT"));
                    MessageBox.Show(this,
                        "Insertados " + inserted + " versículos" +
                        (expectedVerses > 0 && inserted != expectedVerses
                            ? " (esperados " + expectedVerses + "; los duplicados se ignoran)."
                            : "."),
                        "Importar Biblia", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Status("Biblia «" + version + "» importada (" + inserted + " versículos).");
                }
                catch (Exception)
                {
                    _engine.DbExec(FusionStorage.RawSqlRequest("ROLLBACK"));
                    throw;
                }
            }
            catch (FusionException ex)
            {
                MessageBox.Show(this, "Fallo insertando la biblia: " + ex.Message, "Importar",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SendServiceToStage()
        {
            if (_serviceItems.Count == 0)
            {
                Status("El culto está vacío: importa canciones o agrega ítems.");
                return;
            }
            LoadScenarioFromItems(_serviceItems, _txtServiceName.Text.Trim());
        }

        /* ======================================================================
         *  AJUSTES / API / BD
         * ==================================================================== */

        private void ApplySettingsToControls()
        {
            _numPort.Value = _settings.ApiPort;
            _txtToken.Text = _settings.ApiToken;
            _txtVersion.Text = _settings.LastBibleVersion;
            _txtDbPath.Text = Path.Combine(_settings.DataDir, "fusion.db");
        }

        private void SaveSettings()
        {
            _settings.ApiPort = (int)_numPort.Value;
            _settings.ApiToken = _txtToken.Text;
            _settings.Theme = _theme.Name;
            _settings.LastBibleVersion = _txtVersion.Text.Trim();
            _settings.Save();
        }

        private void ToggleApi()
        {
            if (_api != null && _api.IsRunning)
            {
                _api.Stop();
                Status("API detenida.");
                UpdateApiStatus();
                return;
            }
            try
            {
                // Se recrea en cada arranque para honrar puerto/token actuales.
                if (_api != null) { try { _api.Dispose(); } catch (Exception) { } _api = null; }
                int port = (int)_numPort.Value;
                string token = _txtToken.Text;
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
            _btnApiToggle.Text = running ? "Parar API" : "Iniciar API";
            _lblApiState.Text = running
                ? "● EN EJECUCIÓN (puerto " + _api.Port + ")"
                : "● DETENIDA";
            _lblApiState.ForeColor = running ? Color.ForestGreen : Color.Firebrick;
            _tsslApi.Text = running ? "API: 127.0.0.1:" + _api.Port : "API: detenida";
        }

        private void BrowseDbPath()
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "Base de datos (crear o abrir)";
                dlg.Filter = "Base de datos (*.db)|*.db|Todos los archivos (*.*)|*.*";
                dlg.FileName = Path.GetFileName(_txtDbPath.Text);
                dlg.InitialDirectory = SafeDir(Path.GetDirectoryName(_txtDbPath.Text));
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _txtDbPath.Text = dlg.FileName;
            }
        }

        private static string SafeDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return AppDomain.CurrentDomain.BaseDirectory ?? ".";
            return Directory.Exists(dir) ? dir : (AppDomain.CurrentDomain.BaseDirectory ?? ".");
        }

        private void OpenDatabase()
        {
            string path = _txtDbPath.Text.Trim();
            if (path.Length == 0) { Status("Indica la ruta de la BD."); return; }
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception) { /* la BD fallará con un mensaje claro si la ruta es imposible */ }
            int st;
            try { st = _engine.DbOpen(path); }
            catch (ObjectDisposedException) { return; }
            if (st == FusionStatus.Ok)
            {
                _dbOpen = true;
                Status("BD abierta: " + path);
            }
            else
            {
                _dbOpen = false;
                Status("No se pudo abrir la BD (" + FusionStatus.Name(st) + "): " + path);
            }
            UpdateStatusBar();
        }

        private void UpdateStatusBar()
        {
            _tsslDb.Text = _dbOpen ? "BD: abierta" : "BD: —";
            _tsslCore.Text = "Núcleo: " + SafeCoreVersion();
            UpdateApiStatus();
        }

        /* ======================================================================
         *  CICLO DE VIDA
         * ==================================================================== */

        private bool _closing;

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_closing)
            {
                _closing = true;
                try { SaveSettings(); } catch (Exception) { /* no bloquear el cierre */ }
                if (_api != null) { try { _api.Dispose(); } catch (Exception) { } _api = null; }
                if (_engine != null) { try { _engine.Dispose(); } catch (Exception) { } _engine = null; }
            }
            base.OnFormClosing(e);
        }
    }
}
