// ============================================================================
//  LuminaPresentation Suite v5.3.0 «INTERFAZ» — managed/Lumina.UI/MainForm.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MainForm.cs : ventana principal (WinForms, todo el layout en código — sin
//  Designer).
//
//  REDISEÑO v5.3.0 «Lumina Studio» (skill GUI/UX limpia):
//    * Sistema de diseño en UiKit.cs (tarjetas redondeadas, botones con
//      variantes/iconos/estados, casillas oscuras, navegación agrupada).
//    * Carcasa nueva: cabecera con marca + chips + acceso rápido al proyector,
//      sidebar por secciones (PRESENTACIÓN/BIBLIOTECA/EXTENSIÓN/SISTEMA),
//      barra de estado con punto de severidad.
//    * Página «En Vivo» rehecha: transporte compacto con iconos + tooltips +
//      atajos, lista de diapositivas alta con hover/insignias/estado vacío,
//      vista previa con borde «en vivo» e información de diapositiva.
//    * Foco visible por teclado en todo control propio; Ctrl+1…9 navega.
//
//  REGLA DE ARQUITECTURA: la UI NO duplica lógica de negocio. Todo pasa por
//  Lumina.Bridge (P/Invoke al núcleo) y Lumina.Core (builder/JSON local).
//
//  BLINDAJE v4.0.0 «la app SIEMPRE abre»:
//    * Si LuminaCore.dll no carga, la ventana ABRE en «modo limitado»: los
//      paneles funcionan, el chip Núcleo se pinta en rojo y cada acción que
//      necesita el motor avisa por la barra de estado (RequireEngine()).
//    * Ningún error de arranque llega sin capturar (ver Program.cs).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using lumina.api;
using lumina.bridge;
using lumina.core;

namespace lumina.ui
{
    /* ====================================================================== */
    /*  VENTANA PRINCIPAL                                                     */
    /* ====================================================================== */

    public sealed class MainForm : Form
    {
        private const string AppName = "LuminaPresentation Suite";
        private const string AppVersion = "5.4.0";
        private const string AppTitle = AppName + " — v" + AppVersion;

        /* ------------------------------------------------------------ servicios */
        private readonly Settings _settings;
        private LuminaEngine _engine;                 // null = modo limitado (sin núcleo)
        private ApiServer _api;
        private RemoteServer _remote;                 // v5.0.0: mando móvil LAN
        private TriggerEngine _triggers;              // v5.0.0: activadores
        private MidiInput _midi;                      // v5.0.0: entrada MIDI
        private readonly SynchronizationContext _uiCtx;

        /* --------------------------------------------------------- estado vivo */
        private readonly List<SlideView> _slides = new List<SlideView>();
        private int _currentIndex = -1;
        private bool _black;
        private bool _dbOpen;
        private Theme _theme = new Theme();   // no-readonly: el editor de Temas lo reemplaza

        // Temas (editor v4.1.0)
        private TextBox _txtThemeName;
        private Button _swBg, _swFg, _swAccent;
        private Label _hexBg, _hexFg, _hexAccent;
        private ComboBox _cmbThemeFont;
        private NumericUpDown _numThemeSize, _numThemeLine, _numThemeOutline, _numThemeShadow;
        private LuminaCheck _chkThemeBold, _chkThemeUpper;
        private TextBox _txtThemeImagePath;
        private ComboBox _cmbThemeImageMode;
        private ContentPanel _themePreview;

        // Biblioteca de temas (v4.2.0) — data\themes\*.json
        private ListBox _lstThemes;
        private Label _lblThemeApplied;
        private string _appliedThemeName = "—";

        // Transposición de acordes en el editor (v4.2.0)
        private Label _lblSongKey;

        /* --------------------------------------------------------------- culto */
        private readonly List<ScenarioItem> _serviceItems = new List<ScenarioItem>();

        // Último escenario cargado (para re-aplicar el tema sin reconstruirlo).
        private IList<ScenarioItem> _lastScenarioItems;
        private string _lastScenarioName = string.Empty;

        /* ------------------------------------------------------------ estructura */
        private NavPanel _nav;
        private Panel _content;
        private readonly Panel[] _pages = new Panel[9];
        private Chip _chipCore, _chipDb, _chipApi;

        // v5.3.0 — mapeo navegación ↔ páginas (el sidebar agrupa por secciones:
        // PRESENTACIÓN(En Vivo, Culto) · BIBLIOTECA(Canciones, Biblia, Temas) ·
        // EXTENSIÓN(Exportar, Integraciones, Activadores) · SISTEMA(Ajustes)).
        private static readonly int[] NavToPage = { 0, 4, 1, 2, 3, 5, 6, 7, 8 };

        private static int PageToNav(int page)
        {
            for (int i = 0; i < NavToPage.Length; i++)
                if (NavToPage[i] == page) return i;
            return 0;
        }

        // v5.3.0 — asistencia de la interfaz
        private ToolTip _tips;                       // tooltips de la app
        private Button _btnHeaderProjector;         // acceso rápido al proyector
        private Panel _statusBar;                    // pinta el punto de severidad
        private Color _statusColor = UiTheme.TextSecondary;
        private Label _headerTitle;                  // para colocar el chip de versión

        // En Vivo
        private ListView _lvSlides;
        private Button _btnPrev, _btnLive, _btnNext, _btnBlack, _btnClear, _btnProjector;
        private ComboBox _cmbScreen;
        private LuminaCheck _chkFullscreen;
        private Panel _previewArea;
        private PictureBox _pbPreview;
        private Label _lblNoPreview;
        private Label _lblSlidesEmpty;               // v5.3.0: estado vacío de la lista
        private Label _lblPreviewInfo;               // v5.3.0: «Jn 3:16 · 2 de 5»
        private Chip _chipLive;
        private Panel _navInfoCard;

        // Canciones
        private TextBox _txtSongTitle, _txtSongArtist, _txtSongLyrics, _txtSearch;
        private LuminaCheck _chkHymnMode;
        private NumericUpDown _numTranspose;
        private ListView _lvResults;
        private Label _lblResultCount;

        // Biblia
        private TextBox _txtRef, _txtVersion;
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
        private Label _lblAboutCore, _lblAboutUi;

        // v5.0.0 «SINERGIA» — salidas y multimedia
        private VideoPlayerForm _videoForm;           // video en la salida
        private StageViewForm _stageForm;             // monitor de escenario
        private LowerThirdsOverlay _lowerThird;       // zócalo inferior
        private DirectorForm _directorForm;           // v5.1.0: 3ª pantalla (director)
        private int _videoSlideIndex = -1;            // slide de video activa (-1 = ninguna)

        // v5.1.0 — Biblia: búsqueda por palabra (FTS5 del núcleo)
        private TextBox _txtBibleSearch;
        private ListView _lvBibleResults;
        private Label _lblBibleSearchInfo;

        // v5.1.0 — Respaldo (Drive/OneDrive-compatible)
        private TextBox _txtBackupFolder;
        private LuminaCheck _chkAutoBackup;

        // v5.0.0 — Exportar
        private Label _lblExportInfo;

        // v5.0.0 — Integraciones (OBS + MIDI + remoto)
        private TextBox _txtObsUrl, _txtObsPassword, _txtObsTextSource;
        private LuminaCheck _chkObsAutoConnect, _chkMidiEnabled, _chkRemoteEnabled, _chkAutoAdvanceVideo;
        private NumericUpDown _numRemotePort;
        private TextBox _txtRemoteToken;
        private ComboBox _cmbMidiDevice;
        private Button _btnObsConnect, _btnMidiRefresh, _btnRemoteToggle;
        private Label _lblObsState, _lblMidiState, _lblRemoteState, _lblRemoteUrl;
#if !LUMINA_NET35
        private ObsClient _obs;                       // cliente obs-websocket 5.x (net48)
#endif

        // v5.0.0 — Activadores
        private ListView _lvTriggers;
        private Label _lblTriggersInfo;
        private Button _btnTrigAdd, _btnTrigDel, _btnTrigToggle, _btnTrigEdit;

        // Barra inferior
        private Label _tsslMsg;
        private Label _tsslDb, _tsslApi, _tsslCore;

        public MainForm()
        {
            Text = AppTitle;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1150, 740);
            MinimumSize = new Size(960, 640);
            BackColor = UiTheme.PageBg;
            ForeColor = UiTheme.TextPrimary;
            Font = UiTheme.Body;

            try
            {
                // Icono de la app (recurso incrustado por csproj).
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception) { Icon = SystemIcons.Application; }

            _settings = Settings.Load();

            // v5.3.0: tooltips de toda la aplicación (se configuran al construir
            // cada página; componente único, se libera con el formulario).
            _tips = new ToolTip();
            _tips.AutoPopDelay = 9000;
            _tips.InitialDelay = 500;
            _tips.ReshowDelay = 150;
            _tips.ShowAlways = false;

            BuildHeader();
            BuildSidebar();
            BuildStatusBar();
            BuildContent();

            // El contexto de WinForms queda instalado al crear los controles:
            // el motor encola los eventos a este hilo vía ese contexto.
            _uiCtx = SynchronizationContext.Current ?? new SynchronizationContext();

            CreateEngine();
            UpdateBlackButton();          // v5.3.0: estado inicial del botón «Negro»
            ApplySettingsToControls();
            StartIntegrationsFromSettings();   // v5.0.0: activadores/remoto/MIDI/OBS
            UpdateStatusBar();
        }

        /* ======================================================================
         *  CONSTRUCCIÓN — esqueleto (cabecera, sidebar, contenido, estado)
         * ==================================================================== */

        /// <summary>Registra el tooltip de un control (v5.3.0).</summary>
        private void Tip(Control c, string text)
        {
            if (c == null || _tips == null) return;
            try { _tips.SetToolTip(c, text ?? string.Empty); }
            catch (Exception) { }
        }

        private void BuildHeader()
        {
            Panel header = new ContentPanel();
            header.Dock = DockStyle.Top;
            header.Height = 54;
            header.BackColor = UiTheme.BarBg;

            Label title = UiTheme.MkLabel(AppName, Color.White, UiTheme.Header, true);
            title.Location = new Point(60, 0);
            title.Height = 54;
            title.TextAlign = ContentAlignment.MiddleLeft;
            _headerTitle = title;

            Chip version = new Chip("v" + AppVersion + " · HÍBRIDA", UiTheme.Accent, true);

            _chipCore = new Chip("Núcleo: iniciando", UiTheme.TextDisabled, true);
            _chipDb = new Chip("BD: —", UiTheme.TextDisabled, true);
            _chipApi = new Chip("API: detenida", UiTheme.Err, true);

            // v5.3.0: acceso rápido al proyector desde CUALQUIER página (F5).
            _btnHeaderProjector = UiTheme.MkButtonWithIcon("Proyector",
                IconKind.Projector, "secondary", delegate { ShowProjector(); });
            Tip(_btnHeaderProjector, "Mostrar la ventana de proyección (F5)");

            header.Controls.Add(title);
            header.Controls.Add(version);
            header.Controls.Add(_chipCore);
            header.Controls.Add(_chipDb);
            header.Controls.Add(_chipApi);
            header.Controls.Add(_btnHeaderProjector);

            header.Resize += delegate { PlaceHeaderChips(header, version); };
            header.Paint += delegate(object s, PaintEventArgs e)
            {
                // Marca de la app (faro ámbar) — pintada, sin control extra.
                BrandGlyph.Draw(e.Graphics, 18, 12, 30);
                using (Pen p = new Pen(UiTheme.BarBorder))
                    e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
            };

            Controls.Add(header);
        }

        private void PlaceHeaderChips(Panel header, Chip version)
        {
            int x = header.ClientSize.Width - 16;
            _btnHeaderProjector.Location = new Point(x - _btnHeaderProjector.Width, 12);
            x = _btnHeaderProjector.Left - 10;
            _chipApi.Location = new Point(x - _chipApi.Width, 16);
            x = _chipApi.Location.X - 8;
            _chipDb.Location = new Point(x - _chipDb.Width, 16);
            x = _chipDb.Location.X - 8;
            _chipCore.Location = new Point(x - _chipCore.Width, 16);
            int tw = 236;
            try { if (_headerTitle != null && _headerTitle.PreferredWidth > 0) tw = _headerTitle.PreferredWidth; }
            catch (Exception) { }
            version.Location = new Point(60 + tw + 10, 16);
        }

        private void BuildSidebar()
        {
            _nav = new NavPanel();
            _nav.Dock = DockStyle.Left;
            _nav.NavActivated += NavigateTo;

            // Tarjeta informativa inferior (v5.3.0): salida de proyección +
            // estado del núcleo, en tarjeta redondeada.
            _navInfoCard = new ContentPanel();
            _navInfoCard.Dock = DockStyle.Bottom;
            _navInfoCard.Height = 78;
            _navInfoCard.BackColor = UiTheme.BarBg;
            _navInfoCard.Paint += delegate(object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Panel info = (Panel)s;
                Rectangle card = new Rectangle(14, 10, _nav.Width - 28, info.Height - 20);
                using (GraphicsPath path = UiTheme.RoundPath(card, 8))
                {
                    using (SolidBrush bg = new SolidBrush(UiTheme.CardBg)) g.FillPath(bg, path);
                    using (Pen pen = new Pen(UiTheme.CardBorder)) g.DrawPath(pen, path);
                }

                // Punto de estado + «Salida · Pantalla N»
                using (SolidBrush dot = new SolidBrush(_engine != null ? UiTheme.Ok : UiTheme.TextDisabled))
                    g.FillEllipse(dot, card.X + 12, card.Y + 15, 8, 8);
                string screenTxt = "Salida · Pantalla " +
                    ((_cmbScreen != null && _cmbScreen.SelectedIndex > 0)
                        ? _cmbScreen.SelectedIndex.ToString(CultureInfo.InvariantCulture)
                        : "1");
                TextRenderer.DrawText(g, screenTxt, UiTheme.SmallBold,
                    new Rectangle(card.X + 26, card.Y + 6, card.Width - 30, 22),
                    UiTheme.TextPrimary, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                TextRenderer.DrawText(g,
                    _engine != null ? "Núcleo C++ · activo" : "Núcleo no disponible",
                    UiTheme.Small,
                    new Rectangle(card.X + 12, card.Y + 32, card.Width - 24, 20),
                    _engine != null ? UiTheme.TextSecondary : UiTheme.Err,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };

            _nav.Controls.Add(_navInfoCard);
            Controls.Add(_nav);
        }

        private void BuildStatusBar()
        {
            _statusBar = new ContentPanel();
            _statusBar.Dock = DockStyle.Bottom;
            _statusBar.Height = 28;
            _statusBar.BackColor = UiTheme.BarBg;
            _statusBar.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(UiTheme.BarBorder))
                    e.Graphics.DrawLine(p, 0, 0, _statusBar.Width, 0);
                // Punto de severidad del último mensaje (v5.3.0).
                using (SolidBrush b = new SolidBrush(_statusColor))
                    e.Graphics.FillEllipse(b, 14, 10, 8, 8);
            };

            _tsslMsg = UiTheme.MkLabel("Listo.", UiTheme.TextSecondary, UiTheme.Small, false);
            _tsslMsg.Dock = DockStyle.Fill;
            _tsslMsg.Padding = new Padding(30, 0, 0, 0);

            _tsslDb = UiTheme.MkLabel("BD: —", UiTheme.TextDisabled, UiTheme.Small, true);
            _tsslDb.Margin = new Padding(0, 0, 16, 0);
            _tsslApi = UiTheme.MkLabel("API: detenida", UiTheme.TextDisabled, UiTheme.Small, true);
            _tsslApi.Margin = new Padding(0, 0, 16, 0);
            _tsslCore = UiTheme.MkLabel("Núcleo: —", UiTheme.TextDisabled, UiTheme.Small, true);
            _tsslCore.Margin = new Padding(0, 0, 16, 0);

            Panel right = new ContentPanel();
            right.Dock = DockStyle.Right;
            right.Width = 400;
            right.BackColor = UiTheme.BarBg;
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.RightToLeft;
            flow.WrapContents = false;
            flow.Controls.Add(_tsslCore);
            flow.Controls.Add(_tsslApi);
            flow.Controls.Add(_tsslDb);
            right.Controls.Add(flow);

            _statusBar.Controls.Add(_tsslMsg);
            _statusBar.Controls.Add(right);
            Controls.Add(_statusBar);
        }

        private void BuildContent()
        {
            _content = new ContentPanel();
            _content.Dock = DockStyle.Fill;
            _content.BackColor = UiTheme.PageBg;
            _content.Padding = new Padding(20, 18, 20, 14);

            _pages[0] = BuildLivePage();
            _pages[1] = BuildSongsPage();
            _pages[2] = BuildBiblePage();
            _pages[3] = BuildTemasPage();
            _pages[4] = BuildCultoPage();
            _pages[5] = BuildExportPage();       // v5.0.0
            _pages[6] = BuildIntegrationsPage(); // v5.0.0
            _pages[7] = BuildTriggersPage();     // v5.0.0
            _pages[8] = BuildSettingsPage();
            for (int i = 0; i < 9; i++) _content.Controls.Add(_pages[i]);

            Controls.Add(_content);
            _content.BringToFront();
            NavigateToIndex(0);
        }

        /// <summary>Evento del panel de navegación → cambia la página visible.</summary>
        private void NavigateTo(object sender, EventArgs e)
        {
            int navIndex = _nav != null ? _nav.ClickedIndex : 0;
            NavigateToIndex(navIndex >= 0 && navIndex < NavToPage.Length ? NavToPage[navIndex] : 0);
        }

        private int _activeNavItem;

        private void NavigateToIndex(int index)
        {
            if (index < 0 || index >= 9) index = 0;
            _activeNavItem = index;
            for (int i = 0; i < 9; i++) _pages[i].Visible = i == index;
            if (_nav != null) _nav.SetActive(PageToNav(index));
            if (index == 0) RefreshPreview();
        }

        /* ======================================================================
         *  PÁGINA 1 — EN VIVO
         * ==================================================================== */

        private Panel BuildLivePage()
        {
            Panel page = NewPage("En Vivo", "Proyección en tiempo real del escenario",
                IconKind.Live);

            ContentPanel body = (ContentPanel)page.Tag;

            TableLayoutPanel grid = NewGrid(2,
                new ColumnStyle(SizeType.Percent, 100),
                new ColumnStyle(SizeType.Absolute, 412));

            // ---- tarjeta izquierda: escenario + transporte compacto ----
            ContentPanel slidesBody;
            Panel slidesCard = UiTheme.MkCard("Escenario", IconKind.Slides, out slidesBody);
            slidesCard.Dock = DockStyle.Fill;
            slidesCard.Margin = new Padding(0, 0, 14, 0);

            _lvSlides = NewDarkList();
            _lvSlides.Columns.Add("#", 54, HorizontalAlignment.Center);
            _lvSlides.Columns.Add("Referencia", 168, HorizontalAlignment.Left);
            _lvSlides.Columns.Add("Primera línea", 400, HorizontalAlignment.Left);
            _lvSlides.Dock = DockStyle.Fill;
            _lvSlides.DoubleClick += delegate { ShowSelectedSlide(); };
            _lvSlides.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ShowSelectedSlide(); }
            };

            // v5.3.0: estado vacío — la lista explica QUÉ hacer cuando no hay
            // escenario cargado (antes parecía «rota»).
            _lblSlidesEmpty = UiTheme.MkLabel(
                "Sin escenario cargado\r\n" +
                "Carga una canción, un pasaje bíblico o el culto desde las otras páginas",
                UiTheme.TextDisabled, UiTheme.Small, false);
            _lblSlidesEmpty.Dock = DockStyle.Fill;
            _lblSlidesEmpty.TextAlign = ContentAlignment.MiddleCenter;
            _lblSlidesEmpty.BackColor = UiTheme.RowEven;
            _lblSlidesEmpty.Enabled = false;
            _lvSlides.Controls.Add(_lblSlidesEmpty);

            // --- fila 1: transporte (iconos compactos + EN VIVO + Negro) ---
            FlowLayoutPanel transport = new FlowLayoutPanel();
            transport.Dock = DockStyle.Bottom;
            transport.Height = 40;
            transport.WrapContents = false;
            transport.Padding = new Padding(0, 5, 0, 0);

            _btnPrev = UiTheme.MkIconButton(IconKind.Prev, "secondary",
                delegate { if (RequireEngine()) _engine.Prev(); });
            Tip(_btnPrev, "Diapositiva anterior (← / RePág)");
            _btnPrev.Margin = new Padding(0, 0, 6, 0);

            _btnLive = UiTheme.MkButtonWithIcon("EN VIVO", IconKind.Live, "primary",
                delegate { ShowSelectedSlide(); });
            _btnLive.Width = 118;
            Tip(_btnLive, "Proyectar la diapositiva seleccionada (Enter o doble clic en la lista)");
            _btnLive.Margin = new Padding(0, 0, 6, 0);

            _btnNext = UiTheme.MkIconButton(IconKind.Next, "secondary",
                delegate { if (RequireEngine()) _engine.Next(); });
            Tip(_btnNext, "Diapositiva siguiente (→ / Espacio / AvPág)");
            _btnNext.Margin = new Padding(0, 0, 6, 0);

            _btnBlack = UiTheme.MkButtonWithIcon("Negro", IconKind.Black, "danger",
                delegate { if (RequireEngine()) ToggleBlack(); });
            _btnBlack.Width = 96;
            Tip(_btnBlack, "Ocultar la salida en negro (B) — el botón se enciende en rojo");
            _btnBlack.Margin = new Padding(0, 0, 6, 0);

            Button btnShortcuts = UiTheme.MkIconButton(IconKind.Info, "ghost",
                delegate { ShowShortcutsDialog(); });
            Tip(btnShortcuts, "Atajos de teclado (F1)");
            btnShortcuts.Margin = new Padding(0, 0, 0, 0);

            transport.Controls.Add(_btnPrev);
            transport.Controls.Add(_btnLive);
            transport.Controls.Add(_btnNext);
            transport.Controls.Add(_btnBlack);
            transport.Controls.Add(btnShortcuts);

            // --- fila 2: herramientas de salida (escenario, director, aviso…) ---
            FlowLayoutPanel toolsRow = new FlowLayoutPanel();
            toolsRow.Dock = DockStyle.Bottom;
            toolsRow.Height = 38;
            toolsRow.WrapContents = false;
            toolsRow.Padding = new Padding(0, 4, 0, 0);

            Button btnStage = UiTheme.MkIconButton(IconKind.Stage, "secondary",
                delegate { OpenStageView(); });
            Tip(btnStage, "Monitor de escenario (F6)");
            Button btnDirector = UiTheme.MkIconButton(IconKind.Director, "secondary",
                delegate { OpenDirectorView(); });
            Tip(btnDirector, "Ventana de director (F7)");
            Button btnThird = UiTheme.MkIconButton(IconKind.Third, "secondary",
                delegate { ShowLowerThirdDialog(); });
            Tip(btnThird, "Zócalo inferior / aviso (F8)");
            Button btnVideoPause = UiTheme.MkIconButton(IconKind.Pause, "secondary",
                delegate { ToggleVideoPause(); });
            Tip(btnVideoPause, "Pausar el video en la salida (P)");
            _btnClear = UiTheme.MkIconButton(IconKind.Close, "danger",
                delegate { if (RequireEngine()) _engine.Clear(); });
            Tip(_btnClear, "Limpiar la salida (L)");
            btnStage.Margin = new Padding(0, 0, 6, 0);
            btnDirector.Margin = new Padding(0, 0, 6, 0);
            btnThird.Margin = new Padding(0, 0, 6, 0);
            btnVideoPause.Margin = new Padding(0, 0, 6, 0);
            _btnClear.Margin = new Padding(0, 0, 0, 0);

            toolsRow.Controls.Add(btnStage);
            toolsRow.Controls.Add(btnDirector);
            toolsRow.Controls.Add(btnThird);
            toolsRow.Controls.Add(btnVideoPause);
            toolsRow.Controls.Add(_btnClear);

            // --- fila 3: salida (pantalla + proyección) ---
            FlowLayoutPanel outputRow = new FlowLayoutPanel();
            outputRow.Dock = DockStyle.Bottom;
            outputRow.Height = 40;
            outputRow.WrapContents = false;
            outputRow.Padding = new Padding(0, 4, 0, 0);

            _cmbScreen = NewDarkCombo();
            // v5.1.0: pantallas REALES del sistema (antes 0/1/2 fijo — con 4+
            // monitores no se podía elegir la 4ª). Formato: "N · 1920×1080".
            try
            {
                Screen[] screens = Screen.AllScreens;
                for (int i = 0; i < screens.Length; i++)
                {
                    string label = (i + 1).ToString(CultureInfo.InvariantCulture) + " · " +
                        screens[i].Bounds.Width + "×" + screens[i].Bounds.Height;
                    _cmbScreen.Items.Add(label);
                }
            }
            catch (Exception) { _cmbScreen.Items.Add("1 · principal"); }
            if (_cmbScreen.Items.Count == 0) _cmbScreen.Items.Add("1 · principal");
            _cmbScreen.SelectedIndex = 0;
            _cmbScreen.Width = 128;
            Tip(_cmbScreen, "Pantalla donde se abre la ventana de proyección");
            _cmbScreen.Margin = new Padding(0, 6, 8, 0);
            _cmbScreen.SelectedIndexChanged += delegate
            {
                _settings.ProjectionScreen = _cmbScreen.SelectedIndex;
                if (_navInfoCard != null) _navInfoCard.Invalidate();
            };

            _chkFullscreen = UiTheme.MkCheck("Pantalla completa");
            _chkFullscreen.Margin = new Padding(2, 8, 10, 0);
            Tip(_chkFullscreen, "Proyección sin bordes, a pantalla completa");

            _btnProjector = UiTheme.MkButtonWithIcon("Proyector", IconKind.Projector,
                "secondary", delegate { ShowProjector(); });
            Tip(_btnProjector, "Mostrar la ventana de proyección (F5)");

            outputRow.Controls.Add(_cmbScreen);
            outputRow.Controls.Add(_chkFullscreen);
            outputRow.Controls.Add(_btnProjector);

            // Acoplado (z-order inverso): lista rellena, transporte encima de
            // herramientas y herramientas encima de la fila de salida.
            slidesBody.Controls.Add(_lvSlides);
            slidesBody.Controls.Add(transport);
            slidesBody.Controls.Add(toolsRow);
            slidesBody.Controls.Add(outputRow);

            // ---- tarjeta derecha: vista previa (16:9 + info de diapositiva) ----
            ContentPanel previewBody;
            Panel previewCard = UiTheme.MkCard("Vista previa", IconKind.Eye, out previewBody);
            previewCard.Dock = DockStyle.Fill;

            _previewArea = new ContentPanel();
            _previewArea.BackColor = UiTheme.PreviewBg;
            _previewArea.Dock = DockStyle.Top;
            _previewArea.Height = 200;
            _previewArea.Padding = new Padding(4);   // respeta las esquinas redondeadas
            _previewArea.Paint += delegate(object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, _previewArea.Width - 1, _previewArea.Height - 1);
                using (GraphicsPath path = UiTheme.RoundPath(r, 8))
                {
                    using (SolidBrush bg = new SolidBrush(UiTheme.PreviewBg)) g.FillPath(bg, path);
                    // Borde «en vivo»: ámbar cuando hay imagen, neutro si no.
                    using (Pen pen = new Pen(_pbPreview != null && _pbPreview.Image != null
                        ? UiTheme.Accent : UiTheme.CardBorder))
                        g.DrawPath(pen, path);
                }
            };
            _previewArea.Resize += delegate
            {
                _previewArea.Height = Math.Max(120, (_previewArea.Width * 9) / 16);
                if (_chipLive != null)
                    _chipLive.Location = new Point(_previewArea.Width - _chipLive.Width - 14, 10);
            };

            _pbPreview = new PictureBox();
            _pbPreview.Dock = DockStyle.Fill;
            _pbPreview.BackColor = UiTheme.PreviewBg;
            _pbPreview.SizeMode = PictureBoxSizeMode.Zoom;

            _lblNoPreview = UiTheme.MkLabel("Vista previa no disponible",
                UiTheme.TextDisabled, UiTheme.Small, false);
            _lblNoPreview.Dock = DockStyle.Fill;
            _lblNoPreview.TextAlign = ContentAlignment.MiddleCenter;
            _lblNoPreview.BackColor = UiTheme.PreviewBg;

            _chipLive = new Chip("EN VIVO", UiTheme.Ok, true);
            _chipLive.Visible = false;
            _previewArea.Controls.Add(_chipLive);    // chip superpuesto arriba-derecha
            _previewArea.Controls.Add(_pbPreview);
            _previewArea.Controls.Add(_lblNoPreview);
            _lblNoPreview.BringToFront();

            // v5.3.0: «Jn 3:16 · Diapositiva 2 de 5» bajo la vista previa.
            _lblPreviewInfo = UiTheme.MkLabel("Sin diapositivas en el escenario",
                UiTheme.TextSecondary, UiTheme.Small, false);
            _lblPreviewInfo.Dock = DockStyle.Top;
            _lblPreviewInfo.Height = 24;
            _lblPreviewInfo.Padding = new Padding(2, 4, 0, 0);
            _lblPreviewInfo.TextAlign = ContentAlignment.MiddleLeft;

            // Pie: refrescar (baja prominencia) + nota de la fuente del PNG.
            Panel previewFoot = new ContentPanel();
            previewFoot.Dock = DockStyle.Top;
            previewFoot.Height = 36;
            previewFoot.BackColor = UiTheme.CardBg;

            Button btnRefresh = UiTheme.MkButtonWithIcon("Refrescar", IconKind.Refresh,
                "ghost", delegate { RefreshPreview(); });
            btnRefresh.Dock = DockStyle.Left;
            btnRefresh.Height = 30;
            Tip(btnRefresh, "Volver a pedir la vista previa al núcleo");

            Label lblRes = UiTheme.MkLabel("Vista previa del núcleo (PNG)", UiTheme.TextDisabled, UiTheme.Small, true);
            lblRes.Dock = DockStyle.Fill;
            lblRes.Height = 36;
            lblRes.Padding = new Padding(0, 8, 2, 0);
            lblRes.TextAlign = ContentAlignment.MiddleRight;

            previewFoot.Controls.Add(lblRes);
            previewFoot.Controls.Add(btnRefresh);

            // Acoplado (z-order inverso): preview arriba, luego info, luego pie.
            previewBody.Controls.Add(previewFoot);
            previewBody.Controls.Add(_lblPreviewInfo);
            previewBody.Controls.Add(_previewArea);

            grid.Controls.Add(slidesCard, 0, 0);
            grid.Controls.Add(previewCard, 1, 0);
            body.Controls.Add(grid);
            return page;
        }

        /* ======================================================================
         *  PÁGINA 2 — CANCIONES
         * ==================================================================== */

        private Panel BuildSongsPage()
        {
            Panel page = NewPage("Canciones", "Editor de canciones con acordes y búsqueda en la biblioteca",
                IconKind.Song);
            ContentPanel body = (ContentPanel)page.Tag;

            TableLayoutPanel grid = NewGrid(2,
                new ColumnStyle(SizeType.Percent, 62),
                new ColumnStyle(SizeType.Percent, 38));

            // ---- editor ----
            ContentPanel editor;
            Panel editorCard = UiTheme.MkCard("Editor de canción", out editor);

            Label l1 = UiTheme.MkLabel("Título", UiTheme.TextSecondary, UiTheme.Small, true);
            l1.Dock = DockStyle.Top; l1.Height = 18;
            _txtSongTitle = UiTheme.MkInput(false);
            _txtSongTitle.Dock = DockStyle.Top;

            Label l2 = UiTheme.MkLabel("Artista", UiTheme.TextSecondary, UiTheme.Small, true);
            l2.Dock = DockStyle.Top; l2.Height = 22;
            _txtSongArtist = UiTheme.MkInput(false);
            _txtSongArtist.Dock = DockStyle.Top;

            Label l3 = UiTheme.MkLabel("Letras  (bloques: [Verso 1] · [Coro] — acordes sobre la línea anterior)",
                UiTheme.TextSecondary, UiTheme.Small, true);
            l3.Dock = DockStyle.Top; l3.Height = 22;
            _txtSongLyrics = UiTheme.MkInput(true);
            _txtSongLyrics.Font = UiTheme.Mono;
            _txtSongLyrics.Dock = DockStyle.Fill;
            _txtSongLyrics.Text = "[Verso 1]\nPrimera línea de la letra\nsegunda línea\n\n[Coro]\nAleluya, aleluya\n";

            FlowLayoutPanel opts = new FlowLayoutPanel();
            opts.Dock = DockStyle.Bottom;
            opts.Height = 112;
            opts.WrapContents = false;
            opts.FlowDirection = FlowDirection.TopDown;
            opts.Padding = new Padding(0, 4, 0, 0);

            FlowLayoutPanel row1 = new FlowLayoutPanel();
            row1.WrapContents = false;
            row1.Height = 30;
            _chkHymnMode = UiTheme.MkCheck("Modo Hinario (coro intercalado)");
            _chkHymnMode.Margin = new Padding(0, 1, 16, 0);
            Label lt = UiTheme.MkLabel("Transponer:", UiTheme.TextSecondary, UiTheme.Small, true);
            lt.Margin = new Padding(0, 9, 8, 0);
            _numTranspose = UiTheme.MkNumeric(-11, 11, 0);
            _numTranspose.Width = 52;
            row1.Controls.Add(_chkHymnMode);
            row1.Controls.Add(lt);
            row1.Controls.Add(_numTranspose);

            // v4.2.0 — tonalidad detectada + transposición en vivo del texto
            FlowLayoutPanel rowT = new FlowLayoutPanel();
            rowT.WrapContents = false;
            rowT.Height = 36;
            rowT.Padding = new Padding(0, 2, 0, 0);
            _lblSongKey = UiTheme.MkLabel("Tono: —", UiTheme.TextSecondary, UiTheme.Small, true);
            _lblSongKey.Width = 230;
            _lblSongKey.TextAlign = ContentAlignment.MiddleLeft;
            _lblSongKey.Margin = new Padding(0, 9, 12, 0);
            Button btnTransposeNow = UiTheme.MkButton("Transponer ahora", "secondary", delegate { TransposeEditorNow(); });
            btnTransposeNow.Width = 150;
            rowT.Controls.Add(_lblSongKey);
            rowT.Controls.Add(btnTransposeNow);

            FlowLayoutPanel row2 = new FlowLayoutPanel();
            row2.WrapContents = false;
            row2.Height = 34;
            row2.Padding = new Padding(0, 2, 0, 0);
            Button btnLoad = UiTheme.MkButton("Cargar al escenario", "primary", delegate { LoadSongToStage(); });
            Button btnSave = UiTheme.MkButton("Guardar en BD", "secondary", delegate { SaveSongToDb(); });
            row2.Controls.Add(btnLoad);
            row2.Controls.Add(btnSave);

            opts.Controls.Add(row1);
            opts.Controls.Add(rowT);
            opts.Controls.Add(row2);

            editor.Controls.Add(_txtSongLyrics);
            editor.Controls.Add(l3);
            editor.Controls.Add(_txtSongArtist);
            editor.Controls.Add(l2);
            editor.Controls.Add(_txtSongTitle);
            editor.Controls.Add(l1);
            editor.Controls.Add(opts);
            _txtSongLyrics.BringToFront();

            // v4.2.0 — tonalidad en vivo del editor (detección del primer acorde)
            _txtSongLyrics.TextChanged += delegate
            {
                _lblSongKey.Text = "Tono: " + ChordUtil.DetectKey(_txtSongLyrics.Text);
            };
            _lblSongKey.Text = "Tono: " + ChordUtil.DetectKey(_txtSongLyrics.Text);

            // ---- búsqueda ----
            ContentPanel search;
            Panel searchCard = UiTheme.MkCard("Buscar en la biblioteca", out search);

            FlowLayoutPanel searchRow = new FlowLayoutPanel();
            searchRow.Dock = DockStyle.Top;
            searchRow.Height = 34;
            searchRow.WrapContents = false;
            _txtSearch = UiTheme.MkInput(false);
            _txtSearch.Width = 190;
            _txtSearch.Margin = new Padding(0, 0, 8, 0);
            UiTheme.SetCueBanner(_txtSearch, "Buscar por título, autor o letra…");
            Tip(_txtSearch, "Buscar en la biblioteca (FTS5) — Enter para ejecutar");
            _txtSearch.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SearchSongs(); }
            };
            Button btnSearch = UiTheme.MkButton("Buscar", "secondary", delegate { SearchSongs(); });
            searchRow.Controls.Add(_txtSearch);
            searchRow.Controls.Add(btnSearch);

            _lvResults = NewDarkList();
            _lvResults.Columns.Add("ID", 44, HorizontalAlignment.Right);
            _lvResults.Columns.Add("Título", 130, HorizontalAlignment.Left);
            _lvResults.Columns.Add("Autor", 110, HorizontalAlignment.Left);
            _lvResults.Columns.Add("Letra (inicio)", 180, HorizontalAlignment.Left);
            _lvResults.Dock = DockStyle.Fill;
            _lvResults.DoubleClick += delegate { LoadResultToEditor(); };

            _lblResultCount = UiTheme.MkLabel("Busca por título, autor o letra.", UiTheme.TextDisabled, UiTheme.Small, true);
            _lblResultCount.Dock = DockStyle.Bottom;
            _lblResultCount.Height = 20;

            search.Controls.Add(_lvResults);
            search.Controls.Add(searchRow);
            search.Controls.Add(_lblResultCount);

            grid.Controls.Add(editorCard, 0, 0);
            grid.Controls.Add(searchCard, 1, 0);
            body.Controls.Add(grid);
            return page;
        }

        /* ======================================================================
         *  PÁGINA 3 — BIBLIA
         * ==================================================================== */

        private Panel BuildBiblePage()
        {
            Panel page = NewPage("Biblia", "Pasajes y versículos para proyectar", IconKind.Book);
            ContentPanel body = (ContentPanel)page.Tag;

            ContentPanel passage;
            Panel passageCard = UiTheme.MkCard("Cargar pasaje", out passage);
            passageCard.Height = 252;
            passageCard.Dock = DockStyle.Top;

            TableLayoutPanel form = NewGrid(2,
                new ColumnStyle(SizeType.Absolute, 120),
                new ColumnStyle(SizeType.Percent, 100));

            form.Controls.Add(FieldLabel("Referencia"), 0, 0);
            _txtRef = UiTheme.MkInput(false);
            _txtRef.Dock = DockStyle.Fill;
            _txtRef.Text = "Jn 3:16";
            form.Controls.Add(_txtRef, 1, 0);

            form.Controls.Add(FieldLabel("Versión"), 0, 1);
            _txtVersion = UiTheme.MkInput(false);
            _txtVersion.Dock = DockStyle.Fill;
            form.Controls.Add(_txtVersion, 1, 1);

            form.Controls.Add(FieldLabel("Versos por slide"), 0, 2);
            _numVersesPerSlide = UiTheme.MkNumeric(1, 4, 1);
            _numVersesPerSlide.Width = 56;
            form.Controls.Add(_numVersesPerSlide, 1, 2);

            FlowLayoutPanel btns = new FlowLayoutPanel();
            btns.Dock = DockStyle.Fill;
            btns.WrapContents = false;
            btns.Padding = new Padding(0, 8, 0, 0);
            Button btnLoad = UiTheme.MkButton("Cargar al escenario", "primary", delegate { LoadScriptureToStage(); });
            Button btnResolve = UiTheme.MkButton("Resolver referencia", "secondary", delegate { ResolveReference(); });
            btns.Controls.Add(btnLoad);
            btns.Controls.Add(btnResolve);
            form.Controls.Add(btns, 1, 3);

            _lblResolve = UiTheme.MkLabel("Ejemplos: Jn 3:16 · Sal 23:1-4 · Ap 21:4",
                UiTheme.TextSecondary, UiTheme.Small, true);
            _lblResolve.Dock = DockStyle.Top;
            _lblResolve.Height = 24;
            _lblResolve.Padding = new Padding(120, 0, 0, 0);

            for (int i = 0; i < 3; i++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            form.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            passage.Controls.Add(form);
            passage.Controls.Add(_lblResolve);

            ContentPanel import;
            Panel importCard = UiTheme.MkCard("Importar biblias", out import);
            importCard.Dock = DockStyle.Top;
            importCard.Height = 164;
            importCard.Margin = new Padding(0, 16, 0, 0);

            Label info = UiTheme.MkLabel(
                "Formatos admitidos: .BIB (directivas #BIB/#VERSION/#BOOKS), ZEFania XML " +
                "(Reina Valera 1960, etc.) y archivos JSON.\n" +
                "La importación valida el archivo con el núcleo y luego inserta los versículos en la base de datos.",
                UiTheme.TextSecondary, UiTheme.Small, false);
            info.Dock = DockStyle.Fill;
            info.Padding = new Padding(0, 2, 0, 6);

            Button btnImport = UiTheme.MkButton("Importar Biblia .BIB…", "secondary", delegate { ImportBibFile(); });
            Button btnImportXml = UiTheme.MkButton("Importar ZEFania XML…", "secondary", delegate { ImportZefaniaXml(); });
            btnImport.Dock = DockStyle.Bottom;
            btnImport.Height = 30;
            btnImportXml.Dock = DockStyle.Bottom;
            btnImportXml.Height = 30;

            import.Controls.Add(info);
            import.Controls.Add(btnImport);
            import.Controls.Add(btnImportXml);

            // ---- v5.1.0: búsqueda por PALABRA (FTS5 del núcleo, tabla bible_fts) ----
            ContentPanel search;
            Panel searchCard = UiTheme.MkCard("Buscar en la Biblia por palabra", out search);
            searchCard.Dock = DockStyle.Fill;

            FlowLayoutPanel searchRow = new FlowLayoutPanel();
            searchRow.Dock = DockStyle.Top;
            searchRow.Height = 34;
            searchRow.WrapContents = false;
            _txtBibleSearch = UiTheme.MkInput(false);
            _txtBibleSearch.Width = 240;
            _txtBibleSearch.Margin = new Padding(0, 0, 8, 0);
            UiTheme.SetCueBanner(_txtBibleSearch, "Buscar palabra o frase…");
            Tip(_txtBibleSearch, "Búsqueda por palabra en TODAS las versiones (índice FTS5) — Enter para ejecutar");
            _txtBibleSearch.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SearchBibleWords(); }
            };
            Button btnBibleSearch = UiTheme.MkButton("Buscar", "secondary", delegate { SearchBibleWords(); });
            searchRow.Controls.Add(_txtBibleSearch);
            searchRow.Controls.Add(btnBibleSearch);

            _lvBibleResults = NewDarkList();
            _lvBibleResults.Columns.Add("Referencia", 170, HorizontalAlignment.Left);
            _lvBibleResults.Columns.Add("Versículo", 620, HorizontalAlignment.Left);
            _lvBibleResults.Dock = DockStyle.Fill;
            _lvBibleResults.DoubleClick += delegate { LoadBibleResultToStage(); };

            _lblBibleSearchInfo = UiTheme.MkLabel(
                "Busca en TODAS las versiones instaladas (índice FTS5). El término " +
                "aparece resaltado con «…». Doble clic → carga el pasaje al escenario.",
                UiTheme.TextDisabled, UiTheme.Small, true);
            _lblBibleSearchInfo.Dock = DockStyle.Bottom;
            _lblBibleSearchInfo.Height = 20;

            search.Controls.Add(_lvBibleResults);
            search.Controls.Add(searchRow);
            search.Controls.Add(_lblBibleSearchInfo);

            // El ORDEN de anexado importa (docking = z-order inverso): los
            // Dock.Top se procesan del último al primero → anexar en orden
            // inverso al visual deseado (arriba: import · medio: pasaje · resto: búsqueda).
            body.Controls.Add(searchCard);
            body.Controls.Add(importCard);
            body.Controls.Add(passageCard);
            return page;
        }

        /* ======================================================================
         *  PÁGINA 4 — TEMAS (editor visual v4.1.0)
         * ==================================================================== */

        private static readonly string[] ThemeFonts =
            { "Segoe UI", "Arial", "Georgia", "Verdana", "Trebuchet MS", "Tahoma" };

        private Panel BuildTemasPage()
        {
            Panel page = NewPage("Temas", "Apariencia de la proyección: colores, tipografía y efectos", IconKind.Palette);
            ContentPanel body = (ContentPanel)page.Tag;

            TableLayoutPanel grid = NewGrid(2,
                new ColumnStyle(SizeType.Percent, 55),
                new ColumnStyle(SizeType.Percent, 45));

            // ---------------- tarjeta izquierda: propiedades ----------------
            ContentPanel props;
            Panel propsCard = UiTheme.MkCard("Propiedades del tema", out props);
            propsCard.Dock = DockStyle.Fill;
            propsCard.Margin = new Padding(0, 0, 16, 0);

            TableLayoutPanel form = new TableLayoutPanel();
            form.Dock = DockStyle.Fill;
            form.AutoScroll = true;
            form.ColumnCount = 1;
            form.Padding = new Padding(0);
            form.BackColor = UiTheme.CardBg;

            // --- Nombre
            form.Controls.Add(ThemeSection("Nombre"));
            _txtThemeName = UiTheme.MkInput(false);
            _txtThemeName.Width = 260;
            _txtThemeName.TextChanged += delegate { _themePreview.Invalidate(); };
            form.Controls.Add(WrapTop(_txtThemeName, 30));

            // --- Colores
            form.Controls.Add(ThemeSection("Colores"));
            FlowLayoutPanel colors = new FlowLayoutPanel();
            colors.WrapContents = false;
            colors.Height = 56;
            colors.Margin = new Padding(0);
            _swBg = MkSwatch(colors, "Fondo", out _hexBg);
            _swFg = MkSwatch(colors, "Texto", out _hexFg);
            _swAccent = MkSwatch(colors, "Acento", out _hexAccent);
            _swBg.Click += delegate { PickThemeColor("Fondo del tema", _swBg, _hexBg); };
            _swFg.Click += delegate { PickThemeColor("Color del texto", _swFg, _hexFg); };
            _swAccent.Click += delegate { PickThemeColor("Color de acento", _swAccent, _hexAccent); };
            form.Controls.Add(colors);

            // --- Tipografía
            form.Controls.Add(ThemeSection("Tipografía"));
            FlowLayoutPanel typo = new FlowLayoutPanel();
            typo.WrapContents = false;
            typo.Height = 40;
            typo.Margin = new Padding(0);
            Label lf = UiTheme.MkLabel("Fuente:", UiTheme.TextSecondary, UiTheme.Small, true);
            lf.Margin = new Padding(0, 12, 8, 0);
            _cmbThemeFont = NewDarkCombo();
            foreach (string f in ThemeFonts) _cmbThemeFont.Items.Add(f);
            _cmbThemeFont.Width = 170;
            _cmbThemeFont.SelectedIndexChanged += delegate { _themePreview.Invalidate(); };
            Label ls = UiTheme.MkLabel("Tamaño:", UiTheme.TextSecondary, UiTheme.Small, true);
            ls.Margin = new Padding(12, 12, 8, 0);
            _numThemeSize = UiTheme.MkNumeric(12, 140, 54);
            _numThemeSize.Width = 70;
            _numThemeSize.ValueChanged += delegate { _themePreview.Invalidate(); };
            typo.Controls.Add(lf);
            typo.Controls.Add(_cmbThemeFont);
            typo.Controls.Add(ls);
            typo.Controls.Add(_numThemeSize);
            form.Controls.Add(typo);

            FlowLayoutPanel flags = new FlowLayoutPanel();
            flags.WrapContents = false;
            flags.Height = 36;
            flags.Margin = new Padding(0);
            _chkThemeBold = MkDarkCheck("Negrita");
            _chkThemeBold.CheckedChanged += delegate { _themePreview.Invalidate(); };
            _chkThemeUpper = MkDarkCheck("MAYÚSCULAS");
            _chkThemeUpper.CheckedChanged += delegate { _themePreview.Invalidate(); };
            Label ll = UiTheme.MkLabel("Interlineado:", UiTheme.TextSecondary, UiTheme.Small, true);
            ll.Margin = new Padding(12, 9, 8, 0);
            _numThemeLine = new NumericUpDown();
            _numThemeLine.Minimum = 0.8M; _numThemeLine.Maximum = 2M;
            _numThemeLine.DecimalPlaces = 2; _numThemeLine.Increment = 0.02M;
            _numThemeLine.Value = 1.18M;
            _numThemeLine.Width = 64;
            StyleNumeric(_numThemeLine);
            _numThemeLine.ValueChanged += delegate { _themePreview.Invalidate(); };
            flags.Controls.Add(_chkThemeBold);
            flags.Controls.Add(_chkThemeUpper);
            flags.Controls.Add(ll);
            flags.Controls.Add(_numThemeLine);
            form.Controls.Add(flags);

            // --- Efectos
            form.Controls.Add(ThemeSection("Efectos"));
            FlowLayoutPanel fx = new FlowLayoutPanel();
            fx.WrapContents = false;
            fx.Height = 40;
            fx.Margin = new Padding(0);
            Label lo = UiTheme.MkLabel("Contorno:", UiTheme.TextSecondary, UiTheme.Small, true);
            lo.Margin = new Padding(0, 12, 8, 0);
            _numThemeOutline = new NumericUpDown();
            _numThemeOutline.Minimum = 0; _numThemeOutline.Maximum = 10;
            _numThemeOutline.DecimalPlaces = 1; _numThemeOutline.Increment = 0.5M;
            _numThemeOutline.Value = 2M;
            _numThemeOutline.Width = 64;
            StyleNumeric(_numThemeOutline);
            _numThemeOutline.ValueChanged += delegate { _themePreview.Invalidate(); };
            Label lsh = UiTheme.MkLabel("Sombra (0-255):", UiTheme.TextSecondary, UiTheme.Small, true);
            lsh.Margin = new Padding(12, 12, 8, 0);
            _numThemeShadow = UiTheme.MkNumeric(0, 255, 140);
            _numThemeShadow.Width = 64;
            _numThemeShadow.ValueChanged += delegate { _themePreview.Invalidate(); };
            fx.Controls.Add(lo);
            fx.Controls.Add(_numThemeOutline);
            fx.Controls.Add(lsh);
            fx.Controls.Add(_numThemeShadow);
            form.Controls.Add(fx);
            Label fxNote = UiTheme.MkLabel(
                "Contorno por silueta (técnica del núcleo) y sombra suave bajo el texto.",
                UiTheme.TextDisabled, UiTheme.Small, false);
            fxNote.Height = 18;
            form.Controls.Add(WrapTop(fxNote, 18));

            // --- Imagen de fondo
            form.Controls.Add(ThemeSection("Imagen de fondo (opcional)"));
            FlowLayoutPanel img = new FlowLayoutPanel();
            img.WrapContents = false;
            img.Height = 36;
            img.Margin = new Padding(0);
            _txtThemeImagePath = UiTheme.MkInput(false);
            _txtThemeImagePath.Width = 250;
            _cmbThemeImageMode = NewDarkCombo();
            _cmbThemeImageMode.Items.Add("Contener");
            _cmbThemeImageMode.Items.Add("Cubrir");
            _cmbThemeImageMode.SelectedIndex = 1;
            _cmbThemeImageMode.Width = 90;
            Button btnImg = UiTheme.MkButton("Examinar…", "secondary", delegate
            {
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Title = "Imagen de fondo del tema";
                    dlg.Filter = "Imágenes (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|Todos los archivos (*.*)|*.*";
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        _txtThemeImagePath.Text = dlg.FileName;
                }
            });
            img.Controls.Add(_txtThemeImagePath);
            img.Controls.Add(_cmbThemeImageMode);
            img.Controls.Add(btnImg);
            form.Controls.Add(img);

            // ---------------- biblioteca de temas (v4.2.0) ----------------
            form.Controls.Add(ThemeSection("Biblioteca de temas (data\\themes)"));
            _lblThemeApplied = UiTheme.MkLabel("En uso: —", UiTheme.TextSecondary, UiTheme.Small, true);
            _lblThemeApplied.Height = 18;
            form.Controls.Add(WrapTop(_lblThemeApplied, 18));

            _lstThemes = new ListBox();
            _lstThemes.BackColor = UiTheme.InputBg;
            _lstThemes.ForeColor = UiTheme.TextPrimary;
            _lstThemes.BorderStyle = BorderStyle.FixedSingle;
            _lstThemes.IntegralHeight = false;
            _lstThemes.Height = 96;
            _lstThemes.Font = UiTheme.Small;
            _lstThemes.DoubleClick += delegate { LoadThemeFromLibrary(); };
            form.Controls.Add(WrapTop(_lstThemes, 96));

            FlowLayoutPanel themeLibBtns = new FlowLayoutPanel();
            themeLibBtns.WrapContents = false;
            themeLibBtns.Height = 34;
            themeLibBtns.Padding = new Padding(0, 2, 0, 0);
            themeLibBtns.Controls.Add(UiTheme.MkButton("Guardar", "secondary", delegate { SaveThemeToLibrary(); }));
            themeLibBtns.Controls.Add(UiTheme.MkButton("Cargar", "secondary", delegate { LoadThemeFromLibrary(); }));
            themeLibBtns.Controls.Add(UiTheme.MkButton("Renombrar", "secondary", delegate { RenameThemeInLibrary(); }));
            themeLibBtns.Controls.Add(UiTheme.MkButton("✕ Eliminar", "danger", delegate { DeleteThemeFromLibrary(); }));
            form.Controls.Add(themeLibBtns);

            props.Controls.Add(form);

            // ---------------- tarjeta derecha: vista previa ----------------
            ContentPanel prev;
            Panel prevCard = UiTheme.MkCard("Vista previa en vivo", out prev);

            _themePreview = new ContentPanel();
            _themePreview.BackColor = UiTheme.PreviewBg;
            _themePreview.Dock = DockStyle.Top;
            _themePreview.Height = 200;
            _themePreview.Paint += PaintThemePreview;
            _themePreview.Resize += delegate
            {
                _themePreview.Height = Math.Max(120, (_themePreview.Width * 9) / 16);
            };

            Label prevNote = UiTheme.MkLabel(
                "Se re-dibuja con cada cambio. «Aplicar al escenario» reconstruye el " +
                "escenario activo con este tema y refresca la proyección y la vista " +
                "previa del núcleo (PNG).",
                UiTheme.TextSecondary, UiTheme.Small, false);
            prevNote.Dock = DockStyle.Top;
            prevNote.Height = 58;
            prevNote.Padding = new Padding(0, 4, 0, 0);

            FlowLayoutPanel btns = new FlowLayoutPanel();
            btns.Dock = DockStyle.Bottom;
            btns.Height = 40;
            btns.WrapContents = false;
            btns.Padding = new Padding(0, 4, 0, 0);
            btns.Controls.Add(UiTheme.MkButton("Restaurar", "secondary", delegate
            {
                _theme = new Theme();
                ApplyThemeToControls();
                Status("Tema restaurado al predeterminado (usa «Guardar en ajustes» para conservarlo).");
            }));
            btns.Controls.Add(UiTheme.MkButton("Guardar en ajustes", "secondary", delegate
            {
                _theme = BuildThemeFromControls();
                SaveSettings();
                Status("Tema guardado en data/settings.json.");
            }));
            btns.Controls.Add(UiTheme.MkButton("Aplicar al escenario", "primary", delegate
            {
                ApplyThemeToStage();
            }));

            prev.Controls.Add(_themePreview);
            prev.Controls.Add(prevNote);
            prev.Controls.Add(btns);

            grid.Controls.Add(propsCard, 0, 0);
            grid.Controls.Add(prevCard, 1, 0);
            body.Controls.Add(grid);
            return page;
        }

        /* ---------------------------------------------------- helpers de Temas */

        private static Label ThemeSection(string text)
        {
            Label l = UiTheme.MkLabel(text, Color.White, UiTheme.H2, true);
            l.Height = 30;
            l.Padding = new Padding(0, 6, 0, 0);
            return l;
        }

        private static Panel WrapTop(Control c, int height)
        {
            Panel p = new ContentPanel();
            p.Height = height + 6;
            p.BackColor = UiTheme.CardBg;
            c.Dock = DockStyle.Top;
            p.Controls.Add(c);
            return p;
        }

        /// <summary>Muestra de color (botón plano del color + hex) dentro del panel.</summary>
        private Button MkSwatch(FlowLayoutPanel parent, string caption, out Label hexLabel)
        {
            FlowLayoutPanel wrap = new FlowLayoutPanel();
            wrap.FlowDirection = FlowDirection.TopDown;
            wrap.WrapContents = false;
            wrap.Width = 96;
            wrap.Height = 56;
            wrap.Margin = new Padding(0, 0, 12, 0);

            Label title = UiTheme.MkLabel(caption, UiTheme.TextSecondary, UiTheme.Small, false);
            title.Height = 16;
            Button sw = new Button();
            sw.Width = 90;
            sw.Height = 26;
            sw.FlatStyle = FlatStyle.Flat;
            sw.FlatAppearance.BorderSize = 1;
            sw.FlatAppearance.BorderColor = UiTheme.CardBorder;
            sw.Cursor = Cursors.Hand;
            sw.Margin = new Padding(0);
            hexLabel = new Label();
            hexLabel.AutoSize = false;
            hexLabel.Width = 90;
            hexLabel.Height = 14;
            hexLabel.Font = UiTheme.Small;
            hexLabel.ForeColor = UiTheme.TextSecondary;
            hexLabel.TextAlign = ContentAlignment.MiddleLeft;
            hexLabel.Margin = new Padding(0, 2, 0, 0);

            wrap.Controls.Add(title);
            wrap.Controls.Add(sw);
            wrap.Controls.Add(hexLabel);
            parent.Controls.Add(wrap);
            return sw;
        }

        private static LuminaCheck MkDarkCheck(string text)
        {
            // v5.3.0: la casilla del tema es la MISMA del sistema de diseño.
            LuminaCheck c = UiTheme.MkCheck(text);
            c.Margin = new Padding(0, 7, 14, 0);
            return c;
        }

        private static void StyleNumeric(NumericUpDown n)
        {
            n.BorderStyle = BorderStyle.FixedSingle;
            n.BackColor = UiTheme.InputBg;
            n.ForeColor = UiTheme.TextPrimary;
            n.Font = UiTheme.Body;
            n.Margin = new Padding(0);
        }

        /// <summary>ColorDialog nativo → actualiza swatch + hex. Formato del tema: #AARRGGBB.</summary>
        private void PickThemeColor(string title, Button swatch, Label hexLabel)
        {
            using (ColorDialog dlg = new ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.Color = swatch.BackColor;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                swatch.BackColor = dlg.Color;
                hexLabel.Text = "#" + dlg.Color.ToArgb().ToString("X8", CultureInfo.InvariantCulture);
                _themePreview.Invalidate();
            }
        }

        /// <summary>Parsea "#RRGGBB" o "#AARRGGBB" (tolerante; negro si es inválido).</summary>
        private static Color ParseHexColor(string hex)
        {
            try
            {
                string s = (hex ?? string.Empty).Trim().TrimStart('#');
                if (s.Length == 6) s = "FF" + s;
                if (s.Length != 8) return Color.Black;
                return Color.FromArgb(
                    Convert.ToInt32(s.Substring(0, 2), 16),
                    Convert.ToInt32(s.Substring(2, 2), 16),
                    Convert.ToInt32(s.Substring(4, 2), 16),
                    Convert.ToInt32(s.Substring(6, 2), 16));
            }
            catch (Exception) { return Color.Black; }
        }

        private static string ToHexArgb(Color c)
        {
            return "#" + c.ToArgb().ToString("X8", CultureInfo.InvariantCulture);
        }

        private void ApplyThemeToControls()
        {
            _txtThemeName.Text = _theme.Name;
            _swBg.BackColor = ParseHexColor(_theme.BgColor);
            _hexBg.Text = _theme.BgColor.ToUpper(CultureInfo.InvariantCulture);
            _swFg.BackColor = ParseHexColor(_theme.FgColor);
            _hexFg.Text = _theme.FgColor.ToUpper(CultureInfo.InvariantCulture);
            _swAccent.BackColor = ParseHexColor(_theme.AccentColor);
            _hexAccent.Text = _theme.AccentColor.ToUpper(CultureInfo.InvariantCulture);
            int fi = Array.IndexOf(ThemeFonts, _theme.FontFace);
            _cmbThemeFont.SelectedIndex = fi >= 0 ? fi : 0;
            _numThemeSize.Value = Math.Max(12, Math.Min(140, _theme.FontSize));
            _chkThemeBold.Checked = _theme.Bold;
            _chkThemeUpper.Checked = _theme.Uppercase;
            _numThemeLine.Value = (decimal)Math.Max(0.8, Math.Min(2.0, _theme.LineSpacing));
            _numThemeOutline.Value = (decimal)Math.Max(0, Math.Min(10, (double)_theme.OutlineWidth));
            _numThemeShadow.Value = Math.Max(0, Math.Min(255, _theme.ShadowAlpha));
            _txtThemeImagePath.Text = _theme.ImagePath;
            _cmbThemeImageMode.SelectedIndex = _theme.ImageMode == 0 ? 0 : 1;
            _themePreview.Invalidate();
        }

        private Theme BuildThemeFromControls()
        {
            Theme t = new Theme();
            t.Name = (_txtThemeName.Text ?? string.Empty).Trim().Length == 0
                ? "Predeterminado" : _txtThemeName.Text.Trim();
            t.BgColor = ToHexArgb(_swBg.BackColor);
            t.FgColor = ToHexArgb(_swFg.BackColor);
            t.AccentColor = ToHexArgb(_swAccent.BackColor);
            t.FontFace = _cmbThemeFont.SelectedItem != null
                ? Convert.ToString(_cmbThemeFont.SelectedItem, CultureInfo.InvariantCulture)
                : "Segoe UI";
            t.FontSize = (int)_numThemeSize.Value;
            t.Bold = _chkThemeBold.Checked;
            t.Uppercase = _chkThemeUpper.Checked;
            t.LineSpacing = (double)_numThemeLine.Value;
            t.OutlineWidth = (double)_numThemeOutline.Value;
            t.ShadowAlpha = (int)_numThemeShadow.Value;
            t.ImagePath = _txtThemeImagePath.Text.Trim();
            t.ImageMode = _cmbThemeImageMode.SelectedIndex == 0 ? 0 : 1;
            return t;
        }

        /// <summary>Reconstruye el escenario activo con el tema editado y refresca todo.</summary>
        private void ApplyThemeToStage()
        {
            if (!RequireEngine()) return;
            _theme = BuildThemeFromControls();
            _appliedThemeName = _theme.Name;
            SaveSettings();
            RefreshThemeLibrary(_theme.Name);
            if (_lastScenarioItems == null || _lastScenarioItems.Count == 0)
            {
                Status("Tema listo. Carga una canción o pasaje para verlo con el nuevo tema.");
                return;
            }
            LoadScenarioFromItems(_lastScenarioItems, _lastScenarioName);
            Status("Tema «" + _theme.Name + "» aplicado al escenario activo.");
        }

        /* ======================================================================
         *  BIBLIOTECA DE TEMAS (v4.2.0) — data\themes\*.json
         * ==================================================================== */

        private string ThemesDirPath()
        {
            return Path.Combine(_settings.DataDir, "themes");
        }

        /// <summary>Nombre de archivo seguro (portable) para un tema.</summary>
        private static string SafeThemeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Sin nombre";
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == '\\' || chars[i] == '/')
                    chars[i] = '_';
            }
            string s = new string(chars).Trim();
            if (s.Length == 0) s = "Sin nombre";
            if (s.Length > 60) s = s.Substring(0, 60);
            return s;
        }

        /// <summary>Recarga el ListBox desde data\themes; selecciona selectName si existe.</summary>
        private void RefreshThemeLibrary(string selectName)
        {
            if (_lstThemes == null) return;
            _lstThemes.BeginUpdate();
            _lstThemes.Items.Clear();
            try
            {
                string dir = ThemesDirPath();
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "*.json");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (string f in files)
                        _lstThemes.Items.Add(Path.GetFileNameWithoutExtension(f));
                }
            }
            catch (Exception)
            {
                // Lectura de directorio fallida: la biblioteca queda vacía (no bloquea).
            }
            _lstThemes.EndUpdate();
            if (selectName != null)
            {
                for (int i = 0; i < _lstThemes.Items.Count; i++)
                {
                    if (string.Equals(Convert.ToString(_lstThemes.Items[i], CultureInfo.InvariantCulture),
                                      selectName, StringComparison.OrdinalIgnoreCase))
                    {
                        _lstThemes.SelectedIndex = i;
                        break;
                    }
                }
            }
            if (_lblThemeApplied != null)
                _lblThemeApplied.Text = "En uso: " + _appliedThemeName;
        }

        private void SaveThemeToLibrary()
        {
            Theme t = BuildThemeFromControls();
            _theme = t;
            try
            {
                string dir = ThemesDirPath();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, SafeThemeFileName(t.Name) + ".json");
                File.WriteAllText(path, MiniJson.Serialize(t.ToDict()) + "\n", new UTF8Encoding(false));
                SaveSettings();
                RefreshThemeLibrary(t.Name);
                Status("Tema «" + t.Name + "» guardado en la biblioteca (data\\themes).");
            }
            catch (Exception ex)
            {
                Status("No se pudo guardar el tema: " + ex.Message);
            }
        }

        private void LoadThemeFromLibrary()
        {
            if (_lstThemes == null || _lstThemes.SelectedItem == null)
            {
                Status("Selecciona un tema de la biblioteca (o doble clic para cargarlo).");
                return;
            }
            string name = Convert.ToString(_lstThemes.SelectedItem, CultureInfo.InvariantCulture);
            string path = Path.Combine(ThemesDirPath(), SafeThemeFileName(name) + ".json");
            try
            {
                Dictionary<string, object> o = MiniJson.Parse(File.ReadAllText(path, new UTF8Encoding(false)));
                _theme = Theme.FromDict(o);
                ApplyThemeToControls();
                Status("Tema «" + _theme.Name + "» cargado al editor.");
            }
            catch (Exception ex)
            {
                Status("No se pudo cargar el tema «" + name + "»: " + ex.Message);
            }
        }

        private void RenameThemeInLibrary()
        {
            if (_lstThemes == null || _lstThemes.SelectedItem == null)
            {
                Status("Selecciona un tema de la biblioteca.");
                return;
            }
            string old = Convert.ToString(_lstThemes.SelectedItem, CultureInfo.InvariantCulture);
            string neu = (_txtThemeName.Text ?? string.Empty).Trim();
            if (neu.Length == 0 || string.Equals(neu, old, StringComparison.OrdinalIgnoreCase))
            {
                Status("Escribe el nuevo nombre en el campo «Nombre» y pulsa Renombrar.");
                return;
            }
            try
            {
                string dir = ThemesDirPath();
                string oldPath = Path.Combine(dir, SafeThemeFileName(old) + ".json");
                string newPath = Path.Combine(dir, SafeThemeFileName(neu) + ".json");
                if (!File.Exists(oldPath)) { Status("El archivo del tema ya no está (¿se movió?)."); return; }
                if (File.Exists(newPath)) { Status("Ya existe un tema llamado «" + neu + "»."); return; }
                Dictionary<string, object> o = MiniJson.Parse(File.ReadAllText(oldPath, new UTF8Encoding(false)));
                o["name"] = neu;
                File.WriteAllText(newPath, MiniJson.Serialize(o) + "\n", new UTF8Encoding(false));
                File.Delete(oldPath);
                if (string.Equals(_appliedThemeName, old, StringComparison.OrdinalIgnoreCase)) _appliedThemeName = neu;
                if (string.Equals(_settings.Theme, old, StringComparison.OrdinalIgnoreCase))
                {
                    _settings.Theme = neu;
                    SaveSettings();
                }
                RefreshThemeLibrary(neu);
                _txtThemeName.Text = neu;
                Status("Tema «" + old + "» renombrado a «" + neu + "».");
            }
            catch (Exception ex)
            {
                Status("No se pudo renombrar: " + ex.Message);
            }
        }

        private void DeleteThemeFromLibrary()
        {
            if (_lstThemes == null || _lstThemes.SelectedItem == null)
            {
                Status("Selecciona un tema de la biblioteca.");
                return;
            }
            string name = Convert.ToString(_lstThemes.SelectedItem, CultureInfo.InvariantCulture);
            if (string.Equals(name, "Predeterminado", StringComparison.OrdinalIgnoreCase))
            {
                Status("El tema «Predeterminado» no se puede eliminar.");
                return;
            }
            try
            {
                string path = Path.Combine(ThemesDirPath(), SafeThemeFileName(name) + ".json");
                if (File.Exists(path)) File.Delete(path);
                if (string.Equals(_appliedThemeName, name, StringComparison.OrdinalIgnoreCase))
                    _appliedThemeName = "—";
                RefreshThemeLibrary(null);
                Status("Tema «" + name + "» eliminado de la biblioteca.");
            }
            catch (Exception ex)
            {
                Status("No se pudo eliminar: " + ex.Message);
            }
        }

        /* ======================================================================
         *  TRANSPOSICIÓN EN VIVO DEL EDITOR (v4.2.0)
         * ==================================================================== */

        /// <summary>
        /// Aplica la transposición del NumericUpDown AL TEXTO del editor: cada
        /// línea de acordes (criterio del núcleo, ChordUtil.IsChordLine) se
        /// reescribe vía lumina_chords_transpose (notación latina). La letra
        /// pasa intacta; el desplazamiento vuelve a 0 tras aplicar.
        /// </summary>
        private void TransposeEditorNow()
        {
            if (!RequireEngine()) return;
            string raw = _txtSongLyrics.Text ?? string.Empty;
            if (raw.Length == 0) { Status("La letra está vacía: nada que transponer."); return; }
            int semi = (int)_numTranspose.Value;
            if (semi == 0) { Status("Desplazamiento 0: ajusta «Transponer» antes de aplicar."); return; }

            string[] lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder sb = new StringBuilder();
            int changed = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                if (ChordUtil.IsChordLine(ln))
                {
                    try
                    {
                        Dictionary<string, object> o = MiniJson.Parse(LuminaEngine.ChordsTranspose(ln, semi, true));
                        ln = MiniJson.GetString(o, "line", ln);
                        changed++;
                    }
                    catch (Exception)
                    {
                        // Sin núcleo accesible para esta línea: se deja tal cual.
                    }
                }
                sb.Append(ln);
                if (i < lines.Length - 1) sb.Append("\r\n");
            }
            if (changed == 0)
            {
                Status("No hay líneas de acordes: escribe el cifrado sobre la letra (p. ej. «Do Sol»).");
                return;
            }
            _txtSongLyrics.Text = sb.ToString();
            _numTranspose.Value = 0;
            Status("Acordes transpuestos " + (semi > 0 ? "+" : "") + semi +
                   " semitonos (notación latina) — " + changed + " línea(s).");
        }

        /// <summary>
        /// Vista previa aproximada del tema (el render REAL es el del núcleo vía
        /// render_preview_png: mismo esquema de colores, contorno por silueta y sombra).
        /// </summary>
        private void PaintThemePreview(object sender, PaintEventArgs e)
        {
            ContentPanel p = (ContentPanel)sender;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color bg = _swBg != null ? _swBg.BackColor : UiTheme.PreviewBg;
            Color fg = _swFg != null ? _swFg.BackColor : Color.White;
            Color ac = _swAccent != null ? _swAccent.BackColor : UiTheme.Accent;
            int shadow = _numThemeShadow != null ? (int)_numThemeShadow.Value : 140;
            double outline = _numThemeOutline != null ? (double)_numThemeOutline.Value : 2.0;
            bool bold = _chkThemeBold != null && _chkThemeBold.Checked;
            bool upper = _chkThemeUpper != null && _chkThemeUpper.Checked;
            string face = _cmbThemeFont != null && _cmbThemeFont.SelectedItem != null
                ? Convert.ToString(_cmbThemeFont.SelectedItem, CultureInfo.InvariantCulture)
                : "Segoe UI";
            float sizePx = _numThemeSize != null ? (float)_numThemeSize.Value : 54f;

            Rectangle r = new Rectangle(0, 0, p.Width, p.Height);
            using (SolidBrush b = new SolidBrush(bg)) g.FillRectangle(b, r);
            using (Pen pen = new Pen(UiTheme.CardBorder))
                g.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);

            // El motor dibuja a 1080p lógico: escalar al alto de la vista previa.
            float scale = p.Height / 1080f * 4f;   // 4× para que sea legible (es una MUESTRA)
            string verse = upper
                ? "PORQUE DE TAL MANERA AMÓ DIOS AL MUNDO, QUE HA DADO A SU HIJO UNIGÉNITO…"
                : "Porque de tal manera amó Dios al mundo, que ha dado a su Hijo unigénito…";
            Font sample = null;
            try { sample = new Font(face, Math.Max(9f, sizePx * scale), bold ? FontStyle.Bold : FontStyle.Regular); }
            catch (Exception) { sample = new Font("Segoe UI", 14f); }

            try
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                Rectangle textRect = new Rectangle((int)(r.Width * 0.08), 0,
                    (int)(r.Width * 0.84), r.Height);

                // Referencia arriba-izquierda en el color de acento.
                using (Font refFont = new Font(sample.FontFamily, Math.Max(8f, sizePx * scale * 0.45f), FontStyle.Bold))
                using (SolidBrush ab = new SolidBrush(ac))
                    g.DrawString("Jn 3:16", refFont, ab, 14, 10);

                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddString(verse, sample.FontFamily, (int)sample.Style,
                        sample.Size * 96f / 72f, textRect, sf);

                    // Sombra (offset proporcional + alpha del tema).
                    if (shadow > 0)
                    {
                        int dx = Math.Max(2, (int)(p.Height * 0.012));
                        using (GraphicsPath sh = (GraphicsPath)path.Clone())
                        {
                            var m = new System.Drawing.Drawing2D.Matrix();
                            m.Translate(dx, dx);
                            sh.Transform(m);
                            using (SolidBrush sb = new SolidBrush(Color.FromArgb(shadow, 0, 0, 0)))
                                g.FillPath(sb, sh);
                        }
                    }

                    // Contorno por silueta (el núcleo usa 8 direcciones; aquí Pen con join redondo).
                    if (outline > 0)
                    {
                        using (Pen op = new Pen(Color.FromArgb(200, 0, 0, 0),
                            Math.Max(1.5f, (float)outline * scale * 1.5f)))
                        {
                            op.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                            g.DrawPath(op, path);
                        }
                    }

                    using (SolidBrush tb = new SolidBrush(fg))
                        g.FillPath(tb, path);
                }
            }
            finally
            {
                sample.Dispose();
            }

            // Marca "PREVIEW" abajo-derecha en acento.
            TextRenderer.DrawText(g, "● PREVIEW", UiTheme.SmallBold,
                new Rectangle(r.Right - 110, r.Bottom - 26, 100, 20), ac,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        /* ======================================================================
         *  PÁGINA 5 — CULTO
         * ==================================================================== */

        private Panel BuildCultoPage()
        {
            Panel page = NewPage("Culto", "Orden del servicio: canciones, pasajes y proyección", IconKind.ServiceList);
            ContentPanel body = (ContentPanel)page.Tag;

            TableLayoutPanel grid = NewGrid(2,
                new ColumnStyle(SizeType.Percent, 65),
                new ColumnStyle(SizeType.Percent, 35));

            // ---- lista del culto ----
            ContentPanel list;
            Panel listCard = UiTheme.MkCard("Ítems del culto", out list);
            listCard.Dock = DockStyle.Fill;
            listCard.Margin = new Padding(0, 0, 16, 0);

            FlowLayoutPanel nameRow = new FlowLayoutPanel();
            nameRow.Dock = DockStyle.Top;
            nameRow.Height = 34;
            nameRow.WrapContents = false;
            Label ln = UiTheme.MkLabel("Nombre:", UiTheme.TextSecondary, UiTheme.Small, true);
            ln.Margin = new Padding(0, 9, 8, 0);
            _txtServiceName = UiTheme.MkInput(false);
            _txtServiceName.Width = 240;
            _txtServiceName.Text = "Culto";
            nameRow.Controls.Add(ln);
            nameRow.Controls.Add(_txtServiceName);

            _lstItems = new ListBox();
            _lstItems.DrawMode = DrawMode.OwnerDrawFixed;
            _lstItems.ItemHeight = 30;
            _lstItems.BorderStyle = BorderStyle.FixedSingle;
            _lstItems.BackColor = UiTheme.InputBg;
            _lstItems.ForeColor = UiTheme.TextPrimary;
            _lstItems.Font = UiTheme.Body;
            _lstItems.HorizontalScrollbar = true;
            _lstItems.Dock = DockStyle.Fill;
            _lstItems.DrawItem += DrawDarkListItem;

            FlowLayoutPanel itemBtns = new FlowLayoutPanel();
            itemBtns.Dock = DockStyle.Bottom;
            itemBtns.Height = 118;                // v5.3.0: hasta 3 filas de botones con icono, sin recorte
            itemBtns.WrapContents = true;
            itemBtns.Padding = new Padding(0, 4, 0, 0);
            itemBtns.Controls.Add(UiTheme.MkButtonWithIcon("Subir", IconKind.ArrowUp, "secondary", delegate { MoveItem(-1); }));
            itemBtns.Controls.Add(UiTheme.MkButtonWithIcon("Bajar", IconKind.ArrowDown, "secondary", delegate { MoveItem(1); }));
            itemBtns.Controls.Add(UiTheme.MkButtonWithIcon("Eliminar", IconKind.Trash, "secondary", delegate { RemoveItem(); }));
            itemBtns.Controls.Add(UiTheme.MkButtonWithIcon("Video…", IconKind.Play, "secondary", delegate { AddVideoItemToService(); }));
            // v5.1.0 «FUNDAMENTO»: el editor de culto COMPLETO del spec §3.2 —
            // canciones, pasajes, imágenes, textos/avisos y blancos, sin salir
            // de la página (antes solo video + JSON externo).
            itemBtns.Controls.Add(UiTheme.MkButtonWithIcon("Canción actual", IconKind.Song, "primary", delegate { AddCurrentSongToService(); }));
            itemBtns.Controls.Add(UiTheme.MkButtonWithIcon("Pasaje bíblico", IconKind.Book, "primary", delegate { AddCurrentScriptureToService(); }));
            itemBtns.Controls.Add(UiTheme.MkButtonWithIcon("Imagen…", IconKind.Image, "secondary", delegate { AddImageItemToService(); }));
            itemBtns.Controls.Add(UiTheme.MkButtonWithIcon("Texto/aviso…", IconKind.Third, "secondary", delegate { AddTextItemToService(); }));
            itemBtns.Controls.Add(UiTheme.MkButtonWithIcon("En blanco", IconKind.Black, "secondary", delegate { AddBlankItemToService(); }));

            list.Controls.Add(_lstItems);
            list.Controls.Add(nameRow);
            list.Controls.Add(itemBtns);

            // ---- acciones ----
            ContentPanel actions;
            Panel actionCard = UiTheme.MkCard("Acciones", out actions);

            Button btnImportSong = UiTheme.MkButtonWithIcon("Importar canción JSON…", IconKind.Song, "secondary", delegate { ImportSongJson(); });
            btnImportSong.Dock = DockStyle.Top;
            btnImportSong.Height = 32;
            Button btnImportBib = UiTheme.MkButtonWithIcon("Importar Biblia .BIB…", IconKind.Book, "secondary", delegate { ImportBibFile(); });
            btnImportBib.Dock = DockStyle.Top;
            btnImportBib.Height = 32;
            btnImportBib.Margin = new Padding(0, 8, 0, 0);
            Button btnImportPlan = UiTheme.MkButtonWithIcon("Importar plan JSON…", IconKind.ServiceList, "secondary", delegate { ImportPlanJson(); });
            btnImportPlan.Dock = DockStyle.Top;
            btnImportPlan.Height = 32;
            btnImportPlan.Margin = new Padding(0, 8, 0, 0);
            Button btnSend = UiTheme.MkButtonWithIcon("Enviar a proyección", IconKind.Play, "primary", delegate { SendServiceToStage(); });
            btnSend.Dock = DockStyle.Bottom;
            btnSend.Height = 44;
            btnSend.Font = UiTheme.H2;

            // Anexado inverso (docking = z-order inverso): JSON arriba, enviar abajo.
            actions.Controls.Add(btnSend);
            actions.Controls.Add(btnImportPlan);
            actions.Controls.Add(btnImportBib);
            actions.Controls.Add(btnImportSong);

            grid.Controls.Add(listCard, 0, 0);
            grid.Controls.Add(actionCard, 1, 0);
            body.Controls.Add(grid);
            RefreshServiceList();
            return page;
        }

        /* ======================================================================
         *  PÁGINA 5 — AJUSTES
         * ==================================================================== */

        private Panel BuildSettingsPage()
        {
            Panel page = NewPage("Ajustes", "API local, base de datos e información del sistema", IconKind.Gear);
            ContentPanel body = (ContentPanel)page.Tag;

            // ---- API local ----
            ContentPanel api;
            Panel apiCard = UiTheme.MkCard("API local (control remoto · OBS)", out api);
            apiCard.Dock = DockStyle.Top;
            apiCard.Height = 170;

            TableLayoutPanel apiForm = NewGrid(3,
                new ColumnStyle(SizeType.Absolute, 120),
                new ColumnStyle(SizeType.Percent, 100),
                new ColumnStyle(SizeType.Absolute, 170));

            apiForm.Controls.Add(FieldLabel("Puerto API"), 0, 0);
            _numPort = UiTheme.MkNumeric(1024, 65535, 8069);
            _numPort.Width = 110;
            apiForm.Controls.Add(_numPort, 1, 0);

            apiForm.Controls.Add(FieldLabel("Token API"), 0, 1);
            _txtToken = UiTheme.MkInput(false);
            _txtToken.Dock = DockStyle.Fill;
            apiForm.Controls.Add(_txtToken, 1, 1);

            _btnApiToggle = UiTheme.MkButton("Iniciar API", "primary", delegate { ToggleApi(); });
            _btnApiToggle.Width = 150;
            _btnApiToggle.Margin = new Padding(0, 2, 0, 0);
            apiForm.Controls.Add(_btnApiToggle, 2, 0);

            _lblApiState = UiTheme.MkLabel("● DETENIDA", UiTheme.Err, UiTheme.SmallBold, true);
            _lblApiState.Dock = DockStyle.Fill;
            _lblApiState.TextAlign = ContentAlignment.MiddleLeft;
            apiForm.Controls.Add(_lblApiState, 2, 1);

            for (int i = 0; i < 2; i++) apiForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            apiForm.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            api.Controls.Add(apiForm);

            // ---- base de datos ----
            ContentPanel db;
            Panel dbCard = UiTheme.MkCard("Base de datos", out db);
            dbCard.Dock = DockStyle.Top;
            dbCard.Height = 170;
            dbCard.Margin = new Padding(0, 16, 0, 0);

            TableLayoutPanel dbForm = NewGrid(2,
                new ColumnStyle(SizeType.Absolute, 120),
                new ColumnStyle(SizeType.Percent, 100));

            dbForm.Controls.Add(FieldLabel("Ruta de la BD"), 0, 0);
            _txtDbPath = UiTheme.MkInput(false);
            _txtDbPath.Dock = DockStyle.Fill;
            dbForm.Controls.Add(_txtDbPath, 1, 0);

            FlowLayoutPanel dbBtns = new FlowLayoutPanel();
            dbBtns.Dock = DockStyle.Fill;
            dbBtns.WrapContents = false;
            dbBtns.Padding = new Padding(0, 8, 0, 0);
            Button btnBrowseDb = UiTheme.MkButton("Explorar…", "secondary", delegate { BrowseDbPath(); });
            Button btnDbOpen = UiTheme.MkButton("Crear / Abrir BD", "primary", delegate { OpenDatabase(); });
            Button btnSave = UiTheme.MkButton("Guardar ajustes", "secondary", delegate
            {
                SaveSettings();
                Status("Ajustes guardados.");
            });
            dbBtns.Controls.Add(btnBrowseDb);
            dbBtns.Controls.Add(btnDbOpen);
            dbBtns.Controls.Add(btnSave);
            dbForm.Controls.Add(dbBtns, 1, 1);

            dbForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            dbForm.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            db.Controls.Add(dbForm);

            // ---- v5.1.0: respaldo / sincronización por carpeta ----
            ContentPanel backup;
            Panel backupCard = UiTheme.MkCard("Respaldo y sincronización", out backup);
            backupCard.Dock = DockStyle.Top;
            backupCard.Height = 170;
            backupCard.Margin = new Padding(0, 16, 0, 0);

            TableLayoutPanel bkForm = NewGrid(2,
                new ColumnStyle(SizeType.Absolute, 170),
                new ColumnStyle(SizeType.Percent, 100));

            bkForm.Controls.Add(FieldLabel("Carpeta de respaldo"), 0, 0);
            FlowLayoutPanel bkRow = new FlowLayoutPanel();
            bkRow.Dock = DockStyle.Fill;
            bkRow.WrapContents = false;
            bkRow.Padding = new Padding(0, 4, 0, 0);
            _txtBackupFolder = UiTheme.MkInput(false);
            _txtBackupFolder.Width = 330;
            Button btnBrowseBk = UiTheme.MkButton("Explorar…", "secondary", delegate { BrowseBackupFolder(); });
            Button btnBackupNow = UiTheme.MkButton("Respaldar ahora", "primary", delegate { RunBackupNow(); });
            bkRow.Controls.Add(_txtBackupFolder);
            bkRow.Controls.Add(btnBrowseBk);
            bkRow.Controls.Add(btnBackupNow);
            bkForm.Controls.Add(bkRow, 1, 0);

            _chkAutoBackup = UiTheme.MkCheck("Respaldar automáticamente al cerrar la aplicación " +
                "(ZIP de la carpeta data\\ en la carpeta elegida)");
            bkForm.Controls.Add(_chkAutoBackup, 1, 1);

            Label bkNote = UiTheme.MkLabel(
                "Compatible con Google Drive / OneDrive: apunte la carpeta a la del " +
                "cliente de sincronización instalado (sin OAuth ni contraseñas) y sus " +
                "canciones, biblias, temas y ajustes quedarán respaldados en la nube.",
                UiTheme.TextDisabled, UiTheme.Small, false);
            bkNote.Dock = DockStyle.Fill;
            bkNote.Padding = new Padding(0, 4, 0, 0);
            bkForm.Controls.Add(bkNote, 1, 2);

            for (int i = 0; i < 2; i++) bkForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            bkForm.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            backup.Controls.Add(bkForm);

            // ---- acerca de ----
            ContentPanel about;
            Panel aboutCard = UiTheme.MkCard("Acerca de", out about);
            aboutCard.Dock = DockStyle.Top;
            aboutCard.Height = 150;
            aboutCard.Margin = new Padding(0, 16, 0, 0);

            TableLayoutPanel aboutForm = NewGrid(2,
                new ColumnStyle(SizeType.Absolute, 160),
                new ColumnStyle(SizeType.Percent, 100));

            aboutForm.Controls.Add(FieldLabel("Núcleo (C++)"), 0, 0);
            _lblAboutCore = UiTheme.MkLabel("…", UiTheme.TextPrimary, UiTheme.Small, true);
            aboutForm.Controls.Add(_lblAboutCore, 1, 0);

            aboutForm.Controls.Add(FieldLabel("Interfaz"), 0, 1);
            _lblAboutUi = UiTheme.MkLabel(
                "WinForms · .NET Framework " + Environment.Version + " (" +
                (IntPtr.Size == 4 ? "32" : "64") + " bits)", UiTheme.TextPrimary, UiTheme.Small, true);
            aboutForm.Controls.Add(_lblAboutUi, 1, 1);

            aboutForm.Controls.Add(FieldLabel("Proyección"), 0, 2);
            Label proj = UiTheme.MkLabel(
                "Nativa GDI vía LuminaCore (Windows 7 SP1 → Windows 11)", UiTheme.TextPrimary, UiTheme.Small, true);
            aboutForm.Controls.Add(proj, 1, 2);

            for (int i = 0; i < 3; i++) aboutForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            about.Controls.Add(aboutForm);

            // Anexado inverso (docking = z-order inverso): about queda abajo.
            body.Controls.Add(aboutCard);
            body.Controls.Add(backupCard);
            body.Controls.Add(dbCard);
            body.Controls.Add(apiCard);
            return page;
        }

        /* ------------------------------------------------ v5.1.0: respaldo -- */

        /// <summary>Elige la carpeta de respaldo (p. ej. la de Google Drive).</summary>
        private void BrowseBackupFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Carpeta de respaldo (Google Drive / OneDrive / USB…)";
                dlg.ShowNewFolderButton = true;
                try
                {
                    string cur = _txtBackupFolder.Text.Trim();
                    if (Directory.Exists(cur)) dlg.SelectedPath = cur;
                }
                catch (Exception) { }
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _txtBackupFolder.Text = dlg.SelectedPath;
                    _settings.BackupFolder = dlg.SelectedPath;
                    SaveSettings();
                }
            }
        }

        /// <summary>Respaldar data\ → ZIP con sello de tiempo en la carpeta elegida.</summary>
        private void RunBackupNow()
        {
            _settings.BackupFolder = _txtBackupFolder.Text.Trim();
            _settings.AutoBackupOnExit = _chkAutoBackup.Checked;
            SaveSettings();
            string err = PerformBackup();
            if (err == null) Status("Respaldo creado en: " + _settings.BackupFolder);
            else Status("Respaldo falló: " + err);
        }

        /// <summary>
        /// Ejecuta el respaldo (compartido por el botón y el cierre automático).
        /// Devuelve null si OK / no aplicaba; si falla, el mensaje.
        /// </summary>
        private string PerformBackup()
        {
            try
            {
                string folder = _settings.BackupFolder;
                if (folder.Length == 0) return null;   // sin carpeta: no aplica
                if (!Directory.Exists(folder))
                {
                    try { Directory.CreateDirectory(folder); }
                    catch (Exception ex) { return "la carpeta no existe: " + ex.Message; }
                }
                string dataDir = _settings.DataDir;
                if (!Directory.Exists(dataDir)) return null;   // nada que respaldar
                string zipPath = Path.Combine(folder,
                    "lumina-backup-" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".zip");
                string err = ZipBackup.CreateFromDirectory(dataDir, zipPath);
                if (err != null) return err;
                // Retención ligera: conservar los 10 más recientes.
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

        /* --------------------------------------------------- helpers visuales */

        /// <summary>Crea la página con cabecera (badge de icono + título +
        /// subtítulo + línea divisoria); Tag = cuerpo.</summary>
        private static Panel NewPage(string title, string subtitle, IconKind icon)
        {
            Panel page = new ContentPanel();
            page.Dock = DockStyle.Fill;
            page.BackColor = UiTheme.PageBg;
            page.Padding = new Padding(6, 0, 0, 0);

            Panel header = new ContentPanel();
            header.Dock = DockStyle.Top;
            header.Height = 62;
            header.BackColor = UiTheme.PageBg;
            header.Paint += delegate(object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                // Badge del icono: cuadrado redondeado con tinte ámbar.
                Rectangle badge = new Rectangle(14, 13, 36, 36);
                using (GraphicsPath p = UiTheme.RoundPath(badge, 9))
                {
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(24, 240, 169, 59)))
                        g.FillPath(bg, p);
                    using (Pen pen = new Pen(Color.FromArgb(56, 240, 169, 59)))
                        g.DrawPath(pen, p);
                }
                if (icon != IconKind.None)
                    IconPainter.Draw(g, icon, 22, 21, 20, UiTheme.Accent);
                using (Pen p = new Pen(UiTheme.CardBorder))
                    g.DrawLine(p, 14, header.Height - 2, header.Width - 14, header.Height - 2);
            };

            Label t = UiTheme.MkLabel(title, Color.White, UiTheme.H1, true);
            t.Location = new Point(62, 5);
            t.Height = 30;
            t.TextAlign = ContentAlignment.MiddleLeft;

            Label st = UiTheme.MkLabel(subtitle, UiTheme.TextSecondary, UiTheme.Small, true);
            st.Location = new Point(62, 34);
            st.Height = 18;
            st.TextAlign = ContentAlignment.MiddleLeft;

            header.Controls.Add(st);
            header.Controls.Add(t);

            ContentPanel body = new ContentPanel();
            body.Dock = DockStyle.Fill;
            body.BackColor = UiTheme.PageBg;

            page.Controls.Add(body);
            page.Controls.Add(header);
            page.Tag = body;
            return page;
        }

        private static TableLayoutPanel NewGrid(int cols, params ColumnStyle[] colStyles)
        {
            TableLayoutPanel g = new TableLayoutPanel();
            g.Dock = DockStyle.Fill;
            g.ColumnCount = cols;
            g.RowCount = 1;
            foreach (ColumnStyle cs in colStyles) g.ColumnStyles.Add(cs);
            g.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            return g;
        }

        private static Label FieldLabel(string text)
        {
            Label l = UiTheme.MkLabel(text, UiTheme.TextSecondary, UiTheme.Small, true);
            l.Dock = DockStyle.Fill;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Padding = new Padding(0, 0, 8, 0);
            return l;
        }

        /// <summary>Estado de «fila bajo el ratón» por lista (v5.3.0).</summary>
        private sealed class ListHover
        {
            public int Index = -1;
        }

        /// <summary>
        /// ListView oscura v2: cabeceras y filas owner-drawn, filas ALTAS
        /// (34 px vía StateImageList), hover con repintado, selección con barra
        /// ámbar e insignia numérica en la primera columna cuando es corta.
        /// </summary>
        private static ListView NewDarkList()
        {
            ListView lv = new ListView();
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.MultiSelect = false;
            lv.HideSelection = false;   // la selección (slide en curso) se ve sin foco
            lv.BorderStyle = BorderStyle.None;
            lv.BackColor = UiTheme.RowEven;
            lv.ForeColor = UiTheme.TextPrimary;
            lv.Font = UiTheme.Body;
            lv.OwnerDraw = true;

            // Filas de 34 px (truco clásico: la altura sigue al StateImageList).
            try
            {
                ImageList sizing = new ImageList();
                sizing.ImageSize = new Size(1, 34);
                sizing.Images.Add(new Bitmap(1, 34, System.Drawing.Imaging.PixelFormat.Format32bppArgb));
                lv.StateImageList = sizing;
            }
            catch (Exception) { /* altura por defecto: sin consecuencia */ }

            ListHover hover = new ListHover();
            lv.Tag = hover;
            lv.MouseMove += delegate(object s, MouseEventArgs e)
            {
                ListView l = (ListView)s;
                int idx = -1;
                try
                {
                    ListViewHitTestInfo ht = l.HitTest(e.Location);
                    if (ht != null && ht.Item != null) idx = ht.Item.Index;
                }
                catch (Exception) { }
                if (idx != hover.Index) { hover.Index = idx; l.Invalidate(); }
            };
            lv.MouseLeave += delegate(object s, EventArgs e)
            {
                ListView l = (ListView)s;
                if (hover.Index != -1) { hover.Index = -1; l.Invalidate(); }
            };

            lv.DrawColumnHeader += DrawDarkColumnHeader;
            lv.DrawItem += delegate(object s, DrawListViewItemEventArgs e)
            {
                if (lv.View == View.Details) e.DrawDefault = false; // lo pintan los subitems
                else e.DrawDefault = true;
            };
            lv.DrawSubItem += DrawDarkSubItem;
            return lv;
        }

        private static void DrawDarkColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            Rectangle r = new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height);
            using (SolidBrush b = new SolidBrush(UiTheme.CardBg)) e.Graphics.FillRectangle(b, r);
            TextRenderer.DrawText(e.Graphics, e.Header != null && e.Header.Text != null
                ? e.Header.Text.ToUpper(CultureInfo.InvariantCulture) : string.Empty,
                UiTheme.SmallBold, new Rectangle(r.X + 10, r.Y, r.Width - 12, r.Height),
                UiTheme.TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
            using (Pen p = new Pen(UiTheme.CardBorder))
                e.Graphics.DrawLine(p, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);
        }

        /// <summary>¿La primera columna es un número corto (insignia)?</summary>
        private static bool IsBadgeNumber(string text)
        {
            if (text == null || text.Length == 0 || text.Length > 3) return false;
            foreach (char ch in text) if (ch < '0' || ch > '9') return false;
            return true;
        }

        private static void DrawDarkSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            ListView lv = (ListView)sender;
            bool selected = e.Item != null && e.Item.Selected;
            ListHover hover = lv.Tag as ListHover;
            bool hovered = hover != null && hover.Index == e.ItemIndex && !selected;
            Color bg = selected ? UiTheme.RowSelected
                : hovered ? UiTheme.RowHover
                : (e.ItemIndex % 2 == 0 ? UiTheme.RowOdd : UiTheme.RowEven);
            Rectangle r = new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height);
            using (SolidBrush b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, r);

            // Barra ámbar al inicio de la fila seleccionada (la «diapositiva en curso»).
            if (selected && e.ColumnIndex == 0)
                using (SolidBrush bar = new SolidBrush(UiTheme.Accent))
                    e.Graphics.FillRectangle(bar, r.X, r.Y + 3, 3, r.Height - 6);

            string text = e.SubItem != null ? e.SubItem.Text : string.Empty;

            if (e.ColumnIndex == 0 && IsBadgeNumber(text))
            {
                // Insignia: pastilla ámbar si la fila está seleccionada, neutra si no.
                Size tsz = TextRenderer.MeasureText(text, UiTheme.SmallBold);
                int bw = Math.Max(24, tsz.Width + 10);
                Rectangle badge = new Rectangle(r.X + (r.Width - bw) / 2,
                    r.Y + (r.Height - 20) / 2, bw, 20);
                using (GraphicsPath p = UiTheme.RoundPath(badge, 6))
                {
                    using (SolidBrush fill = new SolidBrush(
                        selected ? UiTheme.Accent : UiTheme.HoverLayer))
                        e.Graphics.FillPath(fill, p);
                }
                TextRenderer.DrawText(e.Graphics, text, UiTheme.SmallBold,
                    new Rectangle(badge.X - 2, badge.Y, badge.Width + 4, badge.Height),
                    selected ? UiTheme.OnAccent : UiTheme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.NoPrefix);
                return;
            }

            Color fg = selected ? Color.White
                : hovered ? UiTheme.TextPrimary : UiTheme.TextPrimary;
            if (e.ColumnIndex == 0) fg = selected ? Color.White : UiTheme.TextSecondary;
            TextRenderer.DrawText(e.Graphics, text, lv.Font,
                new Rectangle(r.X + 10, r.Y, r.Width - 12, r.Height), fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        private static void DrawDarkListItem(object sender, DrawItemEventArgs e)
        {
            ListBox lb = (ListBox)sender;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color bg = selected ? UiTheme.RowSelected
                : (e.Index % 2 == 0 ? UiTheme.RowOdd : UiTheme.RowEven);
            using (SolidBrush b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, e.Bounds);
            string text = e.Index >= 0 && e.Index < lb.Items.Count
                ? Convert.ToString(lb.Items[e.Index], CultureInfo.InvariantCulture)
                : string.Empty;
            TextRenderer.DrawText(e.Graphics, text, lb.Font,
                new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                selected ? Color.White : UiTheme.TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        /// <summary>ComboBox oscura owner-drawn (pantallas 0/1/2).</summary>
        private static ComboBox NewDarkCombo()
        {
            ComboBox cb = new ComboBox();
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            cb.FlatStyle = FlatStyle.Flat;
            cb.BackColor = UiTheme.InputBg;
            cb.ForeColor = UiTheme.TextPrimary;
            cb.Font = UiTheme.Body;
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            cb.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                ComboBox c = (ComboBox)s;
                bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                using (SolidBrush b = new SolidBrush(selected ? UiTheme.RowSelected : UiTheme.InputBg))
                    e.Graphics.FillRectangle(b, e.Bounds);
                if (e.Index >= 0)
                {
                    string text = Convert.ToString(c.Items[e.Index], CultureInfo.InvariantCulture);
                    TextRenderer.DrawText(e.Graphics, text, c.Font, e.Bounds, UiTheme.TextPrimary,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                }
            };
            return cb;
        }

        /* ======================================================================
         *  MOTOR / MODO LIMITADO
         * ==================================================================== */

        /// <summary>Windows real → motor con proyección nativa; otro SO (dev/CI) → headless.</summary>
        private static bool IsWindows()
        {
            PlatformID id = Environment.OSVersion.Platform;
            return id == PlatformID.Win32NT || id == PlatformID.Win32Windows;
        }

        /// <summary>
        /// Crea el motor nativo. Si falla (DLL ausente, bitness, memoria…), la
        /// ventana ABRE igual en «modo limitado»: chip Núcleo rojo + avisos en
        /// cada acción. Jamás deja tirar la aplicación por el núcleo.
        /// v5.2.0 «MOTOR»: el fallo queda COMPLETO en el log de sesión
        /// (excepción + Win32 GetLastError de un LoadLibrary manual) — el bug
        /// v5.1.1 en campo («carga la GUI pero más nada») no dejaba rastro
        /// del POR QUÉ; ahora el log responde en la primera línea de soporte.
        /// </summary>
        private void CreateEngine()
        {
            try
            {
                bool headless = !IsWindows();
                _engine = LuminaEngine.Create(headless, OnEngineEventRaw, _uiCtx);
                _engine.UiEventReceived += OnEngineUiEvent;
                _chipCore.SetState("Núcleo " + SafeCoreVersion(), UiTheme.Ok);
                Program.LogLine("Núcleo: ACTIVO (" + SafeCoreVersion()
                    + (headless ? ", headless)" : ", proyección Win32)"));
            }
            catch (Exception ex)
            {
                _engine = null;
                _chipCore.SetState("Núcleo: error", UiTheme.Err);
                // v5.2.0: diagnóstico completo en el log — con esto un log de
                // campo identifica la causa sin preguntar al usuario nada.
                Program.LogLine("Núcleo: FALLO — " + ex.GetType().Name + ": " + ex.Message);
                Program.LogLine("Núcleo: detalle «" + ex + "»");
                Program.LogLine("Núcleo: diagnóstico LoadLibrary: " + NativeLoadDiagnostic());
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
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception) { /* la ventana vale más que el diálogo */ }
            }
        }

        /// <summary>
        /// v5.2.0: sondeo manual de carga de LuminaCore.dll para el log —
        /// traduce el GetLastError del loader a texto accionable
        /// (126 = dependencia ausente, 127 = export inexistente → API Win8+
        /// colada como import estático, 193 = bitness incorrecto).
        /// Solo Windows; best-effort, nunca lanza.
        /// </summary>
        private static string NativeLoadDiagnostic()
        {
            if (!IsWindows()) return "(no Windows: motor headless de desarrollo)";
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string path = System.IO.Path.Combine(dir, "LuminaCore.dll");
                if (!File.Exists(path))
                    return "LuminaCore.dll AUSENTE en " + dir;
                uint err = LoadLibraryForDiagnostic(path);
                if (err == 0) return "LoadLibrary OK — el fallo es de inicialización, no de carga";
                string reason;
                switch (err)
                {
                    case 126: reason = "126 ERROR_MOD_NOT_FOUND: falta una DLL dependiente"; break;
                    case 127: reason = "127 ERROR_PROC_NOT_FOUND: la DLL importa una API que este Windows no tiene (¿import Win8+ estático?)"; break;
                    case 193: reason = "193 ERROR_BAD_EXE_FORMAT: bitness incorrecto (x86/x64)"; break;
                    case 5:   reason = "5 ERROR_ACCESS_DENIED: antivirus o permisos"; break;
                    default:  reason = err + " (ver documentación de GetLastError)"; break;
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

        /// <summary>Carga y libera la DLL nativa: devuelve GetLastError (0 = OK).</summary>
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
        private bool RequireEngine()
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

        // Hilo del MOTOR: solo diagnóstico (el camino de la UI es UiEventReceived).
        // v5.1.0: los errores del núcleo van también al LOG de sesión (antes solo
        // a Trace — un fallo en proyección no dejaba rastro para soporte).
        private void OnEngineEventRaw(object sender, LuminaEvent e)
        {
            if (e.Code == LuminaEvents.Error)
            {
                try { TraceLine("EV_ERROR " + e.Text); } catch (Exception) { }
                try { Program.LogLine("ERROR núcleo: " + e.Text); } catch (Exception) { }
            }
        }

        // Hilo de UI (encolado por el SynchronizationContext capturado en el puente).
        private void OnEngineUiEvent(object sender, LuminaEvent e)
        {
            if (InvokeRequired)
            {
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke(new SendOrPostCallback(delegate { HandleEngineEvent(e); })); }
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
                        SyncLiveExtras(itemIdx);   // v5.0.0: video/escenario/OBS/activadores
                    }
                    break;
                case LuminaEvents.ItemChanged:
                    // El estado llega justo después: se aprovecha en SyncLiveExtras.
                    break;
                case LuminaEvents.Error:
                    Status("Error del núcleo: " + e.Text);
                    break;
                default:
                    break; // LOG/PONG: ruido para la UI principal
            }
        }

        private void ApplyStateJson(string json)
        {
            Dictionary<string, object> st = null;
            try { st = MiniJson.Parse(json); } catch (FormatException) { }
            if (st == null) return;
            _currentIndex = (int)MiniJson.GetInt(st, "current", _currentIndex);
            _black = MiniJson.GetBool(st, "black", _black);
            UpdateBlackButton();          // v5.3.0: el botón «Negro» refleja el estado real
            HighlightCurrentSlide();
            RefreshPreview();
        }

        private void Status(string msg)
        {
            _tsslMsg.Text = msg;
            // v5.3.0: punto de severidad — rojo para fallos, ámbar para avisos.
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
                _statusColor = err ? UiTheme.Err : warn ? UiTheme.Warning : UiTheme.TextSecondary;
                if (_statusBar != null) _statusBar.Invalidate();
            }
            catch (Exception) { }
        }

        private static void TraceLine(string msg)
        {
            System.Diagnostics.Trace.WriteLine("Lumina.UI: " + msg);
        }

        /* ======================================================================
         *  v5.0.0 «SINERGIA» — EXPORTAR · INTEGRACIONES · ACTIVADORES · SALIDAS
         * ==================================================================== */

        // ------------------------------------------------------------- Exportar

        private Panel BuildExportPage()
        {
            Panel page = NewPage("Exportar", "Escenario → PPTX (OpenXML) / PDF (escritor propio)",
                IconKind.Export);
            ContentPanel body = (ContentPanel)page.Tag;

            ContentPanel cardBody;
            Panel card = UiTheme.MkCard("Exportación del escenario activo", out cardBody);
            card.Dock = DockStyle.Top;
            card.Height = 216;

            _lblExportInfo = UiTheme.MkLabel("Carga un escenario (En Vivo) y expórtalo con el tema actual.\n" +
                "PPTX: PresentationML ISO/IEC-29500 (abre en PowerPoint 2007+, WPS, LibreOffice).\n" +
                "PDF: 1.4 propio con fuentes Helvetica (cero dependencias).",
                UiTheme.TextSecondary, UiTheme.Small, false);
            _lblExportInfo.Dock = DockStyle.Top;
            _lblExportInfo.Height = 64;
            _lblExportInfo.Padding = new Padding(0, 2, 0, 8);

            FlowLayoutPanel btns = new FlowLayoutPanel();
            btns.Dock = DockStyle.Fill;
            btns.WrapContents = false;
            btns.Padding = new Padding(0, 10, 0, 0);
            Button btnPptx = UiTheme.MkButton("Exportar PPTX…", "primary", delegate { ExportScenarioPptx(); });
            btnPptx.Width = 150;
            Button btnPdf = UiTheme.MkButton("Exportar PDF…", "secondary", delegate { ExportScenarioPdf(); });
            btnPdf.Width = 150;
            btns.Controls.Add(btnPptx);
            btns.Controls.Add(btnPdf);

            cardBody.Controls.Add(btns);
            cardBody.Controls.Add(_lblExportInfo);
            body.Controls.Add(card);
            return page;
        }

        /// <summary>Slides del escenario activo en formato de exportación.</summary>
        private List<ExportSlide> BuildExportSlides()
        {
            List<ExportSlide> result = new List<ExportSlide>();
            foreach (SlideView v in _slides)
            {
                ExportSlide es = new ExportSlide(v);
                if (v.RefLabel == "video" || v.RefLabel == "en blanco")
                {
                    es.Kind = SlideKind.Title;
                    es.Subtitle = v.RefLabel == "video" ? "Video (reproducir en vivo)" : string.Empty;
                }
                result.Add(es);
            }
            return result;
        }

        private void ExportScenarioPptx()
        {
            if (_slides.Count == 0)
            {
                Status("No hay escenario cargado que exportar.");
                MessageBox.Show(this, "Carga primero un escenario (canción, pasaje o culto) en «En Vivo».",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "Exportar escenario a PPTX";
                dlg.Filter = "Presentación PowerPoint (*.pptx)|*.pptx";
                dlg.FileName = SafeFileName(_lastScenarioName.Length > 0 ? _lastScenarioName : "Escenario") + ".pptx";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string err = PptxExporter.ExportToFile(dlg.FileName, _lastScenarioName,
                    BuildExportSlides(), _theme);
                if (err == null)
                {
                    Status("PPTX exportado: " + dlg.FileName);
                    MessageBox.Show(this, "Exportado correctamente:\n" + dlg.FileName,
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    Status("Fallo al exportar PPTX.");
                    MessageBox.Show(this, err, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void ExportScenarioPdf()
        {
            if (_slides.Count == 0)
            {
                Status("No hay escenario cargado que exportar.");
                MessageBox.Show(this, "Carga primero un escenario (canción, pasaje o culto) en «En Vivo».",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "Exportar escenario a PDF";
                dlg.Filter = "Documento PDF (*.pdf)|*.pdf";
                dlg.FileName = SafeFileName(_lastScenarioName.Length > 0 ? _lastScenarioName : "Escenario") + ".pdf";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                // Decodificador de imágenes con GDI+ (solo Windows en ejecución real)
                PdfImageDecoder.Install();
                string err = PdfExporter.ExportToFile(dlg.FileName, _lastScenarioName,
                    BuildExportSlides(), _theme);
                if (err == null)
                {
                    Status("PDF exportado: " + dlg.FileName);
                    MessageBox.Show(this, "Exportado correctamente:\n" + dlg.FileName,
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    Status("Fallo al exportar PDF.");
                    MessageBox.Show(this, err, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>Nombre de archivo seguro (sin caracteres problemáticos).</summary>
        private static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Escenario";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Length > 60 ? name.Substring(0, 60) : name;
        }

        // ------------------------------------------------------- Integraciones

        private Panel BuildIntegrationsPage()
        {
            Panel page = NewPage("Integraciones", "OBS Studio · MIDI · Control remoto móvil (LAN)",
                IconKind.Nodes);
            ContentPanel body = (ContentPanel)page.Tag;

            // ---- OBS Studio ----
            ContentPanel obsBody;
            Panel obsCard = UiTheme.MkCard("OBS Studio (obs-websocket 5.x)", out obsBody);
            obsCard.Dock = DockStyle.Top;
            obsCard.Height = 300;

            TableLayoutPanel obsForm = NewGrid(2,
                new ColumnStyle(SizeType.Absolute, 150),
                new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) obsForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            obsForm.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            obsForm.Controls.Add(FieldLabel("Servidor WebSocket"), 0, 0);
            _txtObsUrl = UiTheme.MkInput(false);
            _txtObsUrl.Dock = DockStyle.Fill;
            obsForm.Controls.Add(_txtObsUrl, 1, 0);

            obsForm.Controls.Add(FieldLabel("Contraseña (opcional)"), 0, 1);
            _txtObsPassword = UiTheme.MkInput(false);
            _txtObsPassword.Dock = DockStyle.Fill;
            _txtObsPassword.UseSystemPasswordChar = true;
            obsForm.Controls.Add(_txtObsPassword, 1, 1);

            obsForm.Controls.Add(FieldLabel("Fuente de texto en vivo"), 0, 2);
            _txtObsTextSource = UiTheme.MkInput(false);
            _txtObsTextSource.Dock = DockStyle.Fill;
            obsForm.Controls.Add(_txtObsTextSource, 1, 2);

            _chkObsAutoConnect = UiTheme.MkCheck("Conectar automáticamente al abrir la aplicación");
            obsForm.Controls.Add(_chkObsAutoConnect, 1, 3);

            FlowLayoutPanel obsBtns = new FlowLayoutPanel();
            obsBtns.Dock = DockStyle.Fill;
            obsBtns.WrapContents = false;
            obsBtns.Padding = new Padding(0, 8, 0, 0);
            _btnObsConnect = UiTheme.MkButton("Conectar con OBS", "primary", delegate { ObsConnectClick(); });
            _btnObsConnect.Width = 160;
            obsBtns.Controls.Add(_btnObsConnect);
            obsForm.Controls.Add(obsBtns, 1, 4);

            _lblObsState = UiTheme.MkLabel("● SIN CONECTAR", UiTheme.TextDisabled, UiTheme.Small, true);
            _lblObsState.Dock = DockStyle.Bottom;
            _lblObsState.Height = 22;

            obsBody.Controls.Add(obsForm);
            obsBody.Controls.Add(_lblObsState);
            body.Controls.Add(obsCard);

            // ---- MIDI ----
            ContentPanel midiBody;
            Panel midiCard = UiTheme.MkCard("Entrada MIDI (activadores)", out midiBody);
            midiCard.Dock = DockStyle.Top;
            midiCard.Height = 176;
            midiCard.Margin = new Padding(0, 14, 0, 0);

            TableLayoutPanel midiForm = NewGrid(2,
                new ColumnStyle(SizeType.Absolute, 150),
                new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 2; i++) midiForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            midiForm.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _chkMidiEnabled = UiTheme.MkCheck("Activar entrada MIDI");
            _chkMidiEnabled.CheckedChanged += delegate { MidiAutoApply(); };
            midiForm.Controls.Add(_chkMidiEnabled, 0, 0);
            midiForm.Controls.Add(FieldLabel(""), 0, 1);

            _cmbMidiDevice = new ComboBox();
            _cmbMidiDevice.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbMidiDevice.Dock = DockStyle.Fill;
            _cmbMidiDevice.FlatStyle = FlatStyle.Flat;
            _cmbMidiDevice.BackColor = UiTheme.InputBg;
            _cmbMidiDevice.ForeColor = UiTheme.TextPrimary;
            midiForm.Controls.Add(_cmbMidiDevice, 1, 0);

            FlowLayoutPanel midiBtns = new FlowLayoutPanel();
            midiBtns.Dock = DockStyle.Fill;
            midiBtns.WrapContents = false;
            midiBtns.Padding = new Padding(0, 8, 0, 0);
            _btnMidiRefresh = UiTheme.MkButton("Buscar dispositivos", "secondary", delegate { MidiRefreshDevices(); });
            _btnMidiRefresh.Width = 150;
            midiBtns.Controls.Add(_btnMidiRefresh);
            midiForm.Controls.Add(midiBtns, 1, 1);

            _lblMidiState = UiTheme.MkLabel("● ENTRADA DETENIDA", UiTheme.TextDisabled, UiTheme.Small, true);
            _lblMidiState.Dock = DockStyle.Bottom;
            _lblMidiState.Height = 22;

            midiBody.Controls.Add(midiForm);
            midiBody.Controls.Add(_lblMidiState);
            body.Controls.Add(midiCard);

            // ---- Control remoto móvil ----
            ContentPanel remBody;
            Panel remCard = UiTheme.MkCard("Control remoto móvil (red local)", out remBody);
            remCard.Dock = DockStyle.Top;
            remCard.Height = 240;
            remCard.Margin = new Padding(0, 14, 0, 0);

            TableLayoutPanel remForm = NewGrid(2,
                new ColumnStyle(SizeType.Absolute, 150),
                new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 2; i++) remForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            remForm.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            remForm.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _chkRemoteEnabled = UiTheme.MkCheck("Abrir el mando a los dispositivos de la red");
            remForm.Controls.Add(_chkRemoteEnabled, 0, 0);
            remForm.Controls.Add(FieldLabel("Puerto"), 0, 1);

            _numRemotePort = UiTheme.MkNumeric(1024, 65535, 8070);
            _numRemotePort.Width = 90;
            remForm.Controls.Add(_numRemotePort, 1, 1);

            remForm.Controls.Add(FieldLabel("Token de acceso"), 0, 2);
            _txtRemoteToken = UiTheme.MkInput(false);
            _txtRemoteToken.Dock = DockStyle.Fill;
            remForm.Controls.Add(_txtRemoteToken, 1, 2);

            FlowLayoutPanel remBtns = new FlowLayoutPanel();
            remBtns.Dock = DockStyle.Fill;
            remBtns.WrapContents = false;
            remBtns.Padding = new Padding(0, 8, 0, 0);
            _btnRemoteToggle = UiTheme.MkButton("Aplicar y abrir mando", "primary", delegate { RemoteApplyClick(); });
            _btnRemoteToggle.Width = 180;
            remBtns.Controls.Add(_btnRemoteToggle);
            remForm.Controls.Add(remBtns, 1, 3);

            _lblRemoteUrl = UiTheme.MkLabel("", UiTheme.TextSecondary, UiTheme.Small, true);
            _lblRemoteUrl.Dock = DockStyle.Bottom;
            _lblRemoteUrl.Height = 24;
            _lblRemoteState = UiTheme.MkLabel("● MANDO DETENIDO", UiTheme.TextDisabled, UiTheme.Small, true);
            _lblRemoteState.Dock = DockStyle.Bottom;
            _lblRemoteState.Height = 22;

            remBody.Controls.Add(remForm);
            remBody.Controls.Add(_lblRemoteUrl);
            remBody.Controls.Add(_lblRemoteState);
            body.Controls.Add(remCard);

            // ---- extras en vivo ----
            ContentPanel exBody;
            Panel exCard = UiTheme.MkCard("Opciones de proyección", out exBody);
            exCard.Dock = DockStyle.Top;
            exCard.Height = 92;
            exCard.Margin = new Padding(0, 14, 0, 0);
            _chkAutoAdvanceVideo = UiTheme.MkCheck(
                "Al terminar un video, avanzar automáticamente a la siguiente diapositiva");
            _chkAutoAdvanceVideo.Dock = DockStyle.Fill;
            exBody.Controls.Add(_chkAutoAdvanceVideo);
            body.Controls.Add(exCard);

            return page;
        }

        // ------------------------------------------------------------ OBS ----

        private bool ObsAvailable()
        {
#if LUMINA_NET35
            return false;
#else
            return true;
#endif
        }

        private void ObsConnectClick()
        {
#if LUMINA_NET35
            MessageBox.Show(this, "La integración con OBS requiere .NET Framework 4.8 " +
                "(Windows 10/11 lo traen integrado; en Windows 7 SP1 instale el paquete offline de .NET 4.8).",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
#else
            SaveIntegrationsFromControls();
            if (!ObsAvailable()) return;
            if (_obs != null && _obs.IsReady)
            {
                _obs.Disconnect();
                UpdateObsState();
                return;
            }
            if (_obs == null) _obs = new ObsClient();
            Status("Conectando con OBS…");
            UpdateObsState();
            // El handshake bloquea unos ms: fuera del hilo de UI
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = _obs.Connect(_settings.ObsUrl, _settings.ObsPassword, 5000);
                try
                {
                    BeginInvoke(new SendOrPostCallback(delegate
                    {
                        Status(ok ? "OBS conectado (" + _settings.ObsUrl + ")."
                                  : "OBS no conectado: " + _obs.LastError);
                        UpdateObsState();
                    }), null);
                }
                catch (Exception) { }
            });
#endif
        }

        private void UpdateObsState()
        {
#if !LUMINA_NET35
            bool ready = _obs != null && _obs.IsReady;
            _lblObsState.Text = ready ? "● CONECTADO Y AUTENTICADO" : "● SIN CONECTAR";
            _lblObsState.ForeColor = ready ? UiTheme.Ok : UiTheme.TextDisabled;
            _btnObsConnect.Text = ready ? "Desconectar de OBS" : "Conectar con OBS";
#endif
        }

        /// <summary>Envía el texto en vivo a la fuente OBS configurada (hilo de fondo).</summary>
        private void ObsSendLiveText(string text)
        {
#if !LUMINA_NET35
            if (_obs == null || !_obs.IsReady) return;
            if (_settings.ObsTextSource.Length == 0 || text == null) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { _obs.SetInputText(_settings.ObsTextSource, text); }
                catch (Exception) { }
            });
#endif
        }

        // ------------------------------------------------------------ MIDI ----

        private void MidiAutoApply()
        {
            SaveIntegrationsFromControls();
            if (_settings.MidiEnabled)
            {
                StartMidi();
            }
            else
            {
                StopMidi();
            }
        }

        private void StartMidi()
        {
            StopMidi();
            if (_midi == null) _midi = new MidiInput();
            _midi.MessageReceived += OnMidiMessage;
            int dev = _cmbMidiDevice != null && _cmbMidiDevice.SelectedIndex >= 0
                ? _cmbMidiDevice.SelectedIndex : _settings.MidiDevice;
            bool ok = _midi.Open(dev);
            _lblMidiState.Text = ok
                ? "● ENTRADA ACTIVA (" + dev + ": " + ((_cmbMidiDevice != null && _cmbMidiDevice.SelectedIndex >= 0 && _cmbMidiDevice.Text.Length > 0) ? _cmbMidiDevice.Text : "dispositivo " + dev) + ")"
                : "● ERROR: " + _midi.LastError;
            _lblMidiState.ForeColor = ok ? UiTheme.Ok : UiTheme.Err;
        }

        private void StopMidi()
        {
            if (_midi != null)
            {
                _midi.MessageReceived -= OnMidiMessage;
                try { _midi.Dispose(); } catch (Exception) { }
                _midi = null;
            }
            if (_lblMidiState != null)
            {
                _lblMidiState.Text = "● ENTRADA DETENIDA";
                _lblMidiState.ForeColor = UiTheme.TextDisabled;
            }
        }

        private void MidiRefreshDevices()
        {
            string[] names = MidiInput.DeviceNames();
            _cmbMidiDevice.Items.Clear();
            foreach (string n in names) _cmbMidiDevice.Items.Add(n);
            if (_cmbMidiDevice.Items.Count > 0)
            {
                int idx = Math.Max(0, Math.Min(_settings.MidiDevice, _cmbMidiDevice.Items.Count - 1));
                _cmbMidiDevice.SelectedIndex = idx;
            }
            Status(names.Length > 0
                ? names.Length + " dispositivo(s) MIDI detectados."
                : "No hay dispositivos MIDI de entrada conectados.");
        }

        // Hilo del driver winmm: se llega a los activadores por el hilo de UI.
        private void OnMidiMessage(object sender, MidiEventArgs e)
        {
            try
            {
                BeginInvoke(new SendOrPostCallback(delegate
                {
                    FireMidiTriggers(e);
                }), null);
            }
            catch (Exception) { }
        }

        private void FireMidiTriggers(MidiEventArgs e)
        {
            if (_triggers == null || !_settings.TriggersEnabled) return;
            Dictionary<string, object> ctx = new Dictionary<string, object>();
            ctx["channel"] = e.Channel;
            if (e.Command == MidiInput.CmdNote)
            {
                ctx["note"] = e.Data1;
                ctx["velocity"] = e.Data2;
                _triggers.Fire("midi_note", ctx);
            }
            else if (e.Command == MidiInput.CmdCc)
            {
                ctx["controller"] = e.Data1;
                ctx["value"] = e.Data2;
                _triggers.Fire("midi_cc", ctx);
            }
            else if (e.Command == MidiInput.CmdProgram)
            {
                ctx["program"] = e.Data1;
                _triggers.Fire("midi_program", ctx);
            }
        }

        // -------------------------------------------------------- Remoto ----

        private void RemoteApplyClick()
        {
            SaveIntegrationsFromControls();
            if (_settings.RemoteEnabled)
            {
                StartRemote();
            }
            else
            {
                StopRemote();
            }
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
            UpdateRemoteInfo();
        }

        private void UpdateRemoteInfo()
        {
            bool running = _remote != null && _remote.IsRunning;
            _lblRemoteState.Text = running ? "● MANDO ACTIVO (puerto " + _settings.RemotePort + ")" : "● MANDO DETENIDO";
            _lblRemoteState.ForeColor = running ? UiTheme.Ok : UiTheme.TextDisabled;
            _lblRemoteUrl.Text = running
                ? "Abre en el móvil: " + _remote.RemoteUrl() +
                  (_settings.RemoteToken.Length == 0 ? "  ⚠ sin token: cualquier equipo de la red puede controlar" : string.Empty)
                : "El mando se abre con el botón o al activarlo en ajustes.";
            if (running && _settings.RemoteToken.Length == 0)
                _lblRemoteUrl.ForeColor = UiTheme.Warning;
            else
                _lblRemoteUrl.ForeColor = UiTheme.TextSecondary;
        }

        // Hilo del servidor remoto → zócalo en pantalla (hilo de UI).
        private void OnRemoteNotice(object sender, string text)
        {
            try
            {
                BeginInvoke(new SendOrPostCallback(delegate
                {
                    ShowLowerThird(text, string.Empty, 8);
                    Status("Aviso recibido del mando remoto.");
                }), null);
            }
            catch (Exception) { }
        }

        /// <summary>Texto en vivo para /api/live.txt (API y mando remoto).</summary>
        private string BuildLiveText()
        {
            if (_currentIndex >= 0 && _currentIndex < _slides.Count)
            {
                SlideView v = _slides[_currentIndex];
                StringBuilder sb = new StringBuilder();
                if (v.RefLabel.Length > 0) sb.Append(v.RefLabel).Append('\n');
                foreach (string l in v.Lines) sb.Append(l).Append('\n');
                return sb.ToString().TrimEnd('\n');
            }
            return string.Empty;
        }

        private void SaveIntegrationsFromControls()
        {
            if (_txtObsUrl != null) _settings.ObsUrl = _txtObsUrl.Text.Trim();
            if (_txtObsPassword != null) _settings.ObsPassword = _txtObsPassword.Text;
            if (_txtObsTextSource != null) _settings.ObsTextSource = _txtObsTextSource.Text.Trim();
            if (_chkObsAutoConnect != null) _settings.ObsAutoConnect = _chkObsAutoConnect.Checked;
            if (_chkRemoteEnabled != null) _settings.RemoteEnabled = _chkRemoteEnabled.Checked;
            if (_numRemotePort != null) _settings.RemotePort = (int)_numRemotePort.Value;
            if (_txtRemoteToken != null) _settings.RemoteToken = _txtRemoteToken.Text.Trim();
            if (_chkMidiEnabled != null) _settings.MidiEnabled = _chkMidiEnabled.Checked;
            if (_cmbMidiDevice != null && _cmbMidiDevice.SelectedIndex >= 0)
                _settings.MidiDevice = _cmbMidiDevice.SelectedIndex;
            if (_chkAutoAdvanceVideo != null) _settings.AutoAdvanceVideo = _chkAutoAdvanceVideo.Checked;
            SaveSettings();
        }

        /// <summary>Arranque de integraciones según ajustes (llamar tras ApplySettingsToControls).</summary>
        private void StartIntegrationsFromSettings()
        {
            // Activadores
            _triggers = TriggerEngine.Load(_settings.TriggersFile);
            _triggers.SetSink(new TriggerSink(this));
            RefreshTriggersList();

            if (_settings.RemoteEnabled) StartRemote();
            if (_settings.MidiEnabled)
            {
                MidiRefreshDevices();
                StartMidi();
            }
            UpdateRemoteInfo();
            UpdateObsState();
#if !LUMINA_NET35
            if (_settings.ObsAutoConnect && _settings.ObsUrl.Length > 0)
            {
                if (_obs == null) _obs = new ObsClient();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { _obs.Connect(_settings.ObsUrl, _settings.ObsPassword, 5000); }
                    catch (Exception) { }
                    try
                    {
                        BeginInvoke(new SendOrPostCallback(delegate { UpdateObsState(); }), null);
                    }
                    catch (Exception) { }
                });
            }
#endif
        }

        // ------------------------------------------------------ Activadores ----

        private Panel BuildTriggersPage()
        {
            Panel page = NewPage("Activadores", "Reglas automáticas: evento → condición → acción",
                IconKind.Bolt);
            ContentPanel body = (ContentPanel)page.Tag;

            ContentPanel listBody;
            Panel listCard = UiTheme.MkCard("Reglas (data\\triggers\\triggers.json)", out listBody);
            listCard.Dock = DockStyle.Fill;

            _lvTriggers = NewDarkList();
            _lvTriggers.Columns.Add("Regla", 240, HorizontalAlignment.Left);
            _lvTriggers.Columns.Add("Evento", 130, HorizontalAlignment.Left);
            _lvTriggers.Columns.Add("Condición", 220, HorizontalAlignment.Left);
            _lvTriggers.Columns.Add("Acciones", 300, HorizontalAlignment.Left);
            _lvTriggers.Columns.Add("Activa", 60, HorizontalAlignment.Center);
            _lvTriggers.Dock = DockStyle.Fill;
            _lvTriggers.DoubleClick += delegate { TriggerEdit(); };

            FlowLayoutPanel btns = new FlowLayoutPanel();
            btns.Dock = DockStyle.Bottom;
            btns.Height = 44;
            btns.WrapContents = false;
            btns.Padding = new Padding(0, 6, 0, 0);
            _btnTrigAdd = UiTheme.MkButtonWithIcon("Nueva regla", IconKind.Plus, "primary", delegate { TriggerAdd(); });
            _btnTrigEdit = UiTheme.MkButtonWithIcon("Editar", IconKind.Info, "secondary", delegate { TriggerEdit(); });
            _btnTrigToggle = UiTheme.MkButtonWithIcon("Activar/Desactivar", IconKind.Check, "secondary", delegate { TriggerToggleEnable(); });
            _btnTrigDel = UiTheme.MkButtonWithIcon("Eliminar", IconKind.Trash, "danger", delegate { TriggerDelete(); });
            _btnTrigAdd.Width = 130; _btnTrigEdit.Width = 90; _btnTrigToggle.Width = 150; _btnTrigDel.Width = 100;
            btns.Controls.Add(_btnTrigAdd);
            btns.Controls.Add(_btnTrigEdit);
            btns.Controls.Add(_btnTrigToggle);
            btns.Controls.Add(_btnTrigDel);

            _lblTriggersInfo = UiTheme.MkLabel(
                "Eventos: item_changed · slide_changed · song_started · video_ended · " +
                "midi_note · midi_cc · midi_program · api_webhook",
                UiTheme.TextSecondary, UiTheme.Small, true);
            _lblTriggersInfo.Dock = DockStyle.Bottom;
            _lblTriggersInfo.Height = 22;

            listBody.Controls.Add(_lvTriggers);
            listBody.Controls.Add(_lblTriggersInfo);
            listBody.Controls.Add(btns);
            body.Controls.Add(listCard);
            return page;
        }

        private void RefreshTriggersList()
        {
            if (_lvTriggers == null || _triggers == null) return;
            _lvTriggers.BeginUpdate();
            try
            {
                _lvTriggers.Items.Clear();
                foreach (TriggerRule r in _triggers.RulesSnapshot())
                {
                    ListViewItem it = new ListViewItem(r.Name.Length > 0 ? r.Name : r.Id);
                    it.SubItems.Add(r.Event);
                    it.SubItems.Add(TriggerMatchText(r));
                    it.SubItems.Add(TriggerActionsText(r));
                    it.SubItems.Add(r.Enabled ? "Sí" : "No");
                    it.Tag = r.Id;
                    _lvTriggers.Items.Add(it);
                }
            }
            finally
            {
                _lvTriggers.EndUpdate();
            }
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
            foreach (TriggerAction a in r.Actions)
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

        private void TriggerAdd() { TriggerDialog(null); }

        private void TriggerEdit()
        {
            if (_lvTriggers.SelectedIndices.Count == 0)
            {
                Status("Selecciona una regla para editarla.");
                return;
            }
            string id = (string)_lvTriggers.SelectedItems[0].Tag;
            foreach (TriggerRule r in _triggers.RulesSnapshot())
            {
                if (r.Id == id) { TriggerDialog(r); return; }
            }
        }

        /// <summary>Diálogo de creación/edición de reglas (funcional y directo).</summary>
        private void TriggerDialog(TriggerRule edit)
        {
            bool isNew = edit == null;
            TriggerRule r = isNew ? new TriggerRule() : edit;

            Form dlg = new Form();
            dlg.Text = isNew ? "Nueva regla de activación" : "Editar regla";
            dlg.StartPosition = FormStartPosition.CenterParent;
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
            dlg.MinimizeBox = dlg.MaximizeBox = false;
            dlg.ClientSize = new Size(560, 420);
            dlg.Font = UiTheme.Body;
            dlg.BackColor = UiTheme.PageBg;
            dlg.ForeColor = UiTheme.TextPrimary;

            Label lblName = UiTheme.MkLabel("Nombre de la regla:", UiTheme.TextSecondary, UiTheme.Small, true);
            lblName.Dock = DockStyle.Top; lblName.Height = 20;
            TextBox txtName = UiTheme.MkInput(false);
            txtName.Dock = DockStyle.Top; txtName.Text = r.Name;

            Label lblEvent = UiTheme.MkLabel("Evento que la dispara:", UiTheme.TextSecondary, UiTheme.Small, true);
            lblEvent.Dock = DockStyle.Top; lblEvent.Height = 20;
            ComboBox cmbEvent = new ComboBox();
            cmbEvent.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbEvent.Dock = DockStyle.Top;
            cmbEvent.Items.AddRange(new object[] {
                "item_changed", "slide_changed", "song_started", "video_ended",
                "midi_note", "midi_cc", "midi_program", "api_webhook" });
            cmbEvent.SelectedItem = r.Event.Length > 0 ? (object)r.Event : "item_changed";

            Label lblMatch = UiTheme.MkLabel(
                "Condición (clave=valor, separada por «;». Vacío = siempre. " +
                "Ej.: title=Ofrenda · note=60 · contains:Alabanza)",
                UiTheme.TextSecondary, UiTheme.Small, false);
            lblMatch.Dock = DockStyle.Top; lblMatch.Height = 34;
            TextBox txtMatch = UiTheme.MkInput(false);
            txtMatch.Dock = DockStyle.Top;
            txtMatch.Text = MatchToText(r.Match);

            Label lblAction = UiTheme.MkLabel("Acción:", UiTheme.TextSecondary, UiTheme.Small, true);
            lblAction.Dock = DockStyle.Top; lblAction.Height = 20;
            ComboBox cmbAction = new ComboBox();
            cmbAction.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbAction.Dock = DockStyle.Top;
            cmbAction.Items.AddRange(new object[] {
                "obs_scene", "obs_source_text", "play_audio", "show_text", "set_theme", "api_cmd", "midi_out" });
            if (r.Actions.Count > 0) cmbAction.SelectedItem = r.Actions[0].Type;

            Label lblParams = UiTheme.MkLabel(
                "Parámetros de la acción (clave=valor, «;»). Ej.: scene=Culto · text=¡Bienvenido! · " +
                "source=Titulo · path=C:\\audio\\clipe.mp3 · theme=Soleado · action=next · status=192 · data1=7",
                UiTheme.TextSecondary, UiTheme.Small, false);
            lblParams.Dock = DockStyle.Top; lblParams.Height = 44;
            TextBox txtParams = UiTheme.MkInput(false);
            txtParams.Dock = DockStyle.Top;
            if (r.Actions.Count > 0 && r.Actions[0].Params.Count > 0)
                txtParams.Text = MatchToText(r.Actions[0].Params);

            FlowLayoutPanel btns = new FlowLayoutPanel();
            btns.Dock = DockStyle.Bottom;
            btns.Height = 46;
            btns.Padding = new Padding(0, 10, 0, 0);
            Button ok = UiTheme.MkButton("Guardar regla", "primary", null);
            ok.Width = 140;
            Button cancel = UiTheme.MkButton("Cancelar", "secondary", null);
            cancel.Width = 100;
            btns.Controls.Add(ok);
            btns.Controls.Add(cancel);

            ok.Click += delegate
            {
                r.Name = txtName.Text.Trim();
                r.Event = cmbEvent.Text;
                r.Match = TextToMatch(txtMatch.Text);
                TriggerAction a = new TriggerAction();
                a.Type = cmbAction.Text;
                a.Params = TextToMatch(txtParams.Text);
                r.Actions = new List<TriggerAction>(new TriggerAction[] { a });
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

            dlg.Controls.Add(txtParams);
            dlg.Controls.Add(lblParams);
            dlg.Controls.Add(cmbAction);
            dlg.Controls.Add(lblAction);
            dlg.Controls.Add(txtMatch);
            dlg.Controls.Add(lblMatch);
            dlg.Controls.Add(cmbEvent);
            dlg.Controls.Add(lblEvent);
            dlg.Controls.Add(txtName);
            dlg.Controls.Add(lblName);
            dlg.Controls.Add(btns);
            dlg.ShowDialog(this);
        }

        /// <summary>dict → "k=v; k2=v2"</summary>
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

        /// <summary>"k=v; k2=v2" → dict (tolerante: sin valor → "")</summary>
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

        private void TriggerToggleEnable()
        {
            if (_lvTriggers.SelectedIndices.Count == 0) { Status("Selecciona una regla."); return; }
            string id = (string)_lvTriggers.SelectedItems[0].Tag;
            List<TriggerRule> rules = _triggers.RulesSnapshot();
            foreach (TriggerRule r in rules)
            {
                if (r.Id == id) { r.Enabled = !r.Enabled; break; }
            }
            _triggers.SetRules(rules);
            _triggers.Save(_settings.TriggersFile);
            RefreshTriggersList();
        }

        private void TriggerDelete()
        {
            if (_lvTriggers.SelectedIndices.Count == 0) { Status("Selecciona una regla."); return; }
            string id = (string)_lvTriggers.SelectedItems[0].Tag;
            List<TriggerRule> rules = _triggers.RulesSnapshot();
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].Id == id) { rules.RemoveAt(i); break; }
            }
            _triggers.SetRules(rules);
            _triggers.Save(_settings.TriggersFile);
            RefreshTriggersList();
            Status("Regla eliminada.");
        }

        // -------------------------------------------------- sink (ejecución) ----

        /// <summary>Sink de activadores: ejecuta las acciones en el hilo de UI.</summary>
        private sealed class TriggerSink : ITriggerSink
        {
            private readonly MainForm _owner;
            public TriggerSink(MainForm owner) { _owner = owner; }

            public string ExecuteTriggerAction(TriggerRule rule, TriggerAction action)
            {
                try
                {
                    return _owner.ExecuteTriggerActionCore(action);
                }
                catch (Exception ex)
                {
                    return "Error ejecutando acción " + action.Type + ": " + ex.Message;
                }
            }
        }

        /// <summary>Ejecuta UNA acción (hilo de UI). Devuelve mensaje de estado.</summary>
        private string ExecuteTriggerActionCore(TriggerAction a)
        {
            switch (a.Type)
            {
                case "obs_scene":
#if LUMINA_NET35
                    return "OBS requiere .NET 4.8";
#else
                    if (_obs == null || !_obs.IsReady) return "OBS no está conectado";
                    string scene = a.GetString("scene", a.GetString("value", string.Empty));
                    return _obs.SetScene(scene) ? "Escena OBS: " + scene : "Fallo enviando a OBS";
#endif
                case "obs_source_text":
#if LUMINA_NET35
                    return "OBS requiere .NET 4.8";
#else
                    if (_obs == null || !_obs.IsReady) return "OBS no está conectado";
                    string src = a.GetString("source", _settings.ObsTextSource);
                    if (src.Length == 0) return "Fuente OBS sin configurar";
                    return _obs.SetInputText(src, a.GetString("text", string.Empty))
                        ? "Texto OBS enviado" : "Fallo enviando texto a OBS";
#endif
                case "play_audio":
                    {
                        string path = a.GetString("path", string.Empty);
                        if (path.Length == 0 || !System.IO.File.Exists(path))
                            return "Audio no encontrado: " + path;
                        PlayAudioHidden(path, a.GetInt("volume", 80));
                        return "Audio: " + System.IO.Path.GetFileName(path);
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
                        int status = a.GetInt("status", 192);       // 0xC0 program change
                        int d1 = a.GetInt("data1", 0);
                        int d2 = a.GetInt("data2", 0);
                        return MidiOutSend(status, d1, d2) ? "MIDI OUT enviado" : "MIDI OUT falló";
                    }
                default:
                    return "Acción desconocida: " + a.Type;
            }
        }

        // ---- MIDI OUT mínimo (winmm) para la acción midi_out ----
        [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "midiOutOpen")]
        private static extern int midiOutOpen(out IntPtr handle, uint device, IntPtr callback,
                                              IntPtr instance, uint flags);
        [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "midiOutShortMsg")]
        private static extern int midiOutShortMsg(IntPtr handle, int message);
        [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "midiOutClose")]
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
                catch (Exception)
                {
                    return false;
                }
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

        // ------------------------------------------------ salidas (video/…) ----

        /// <summary>Pantalla del proyector (la del combo En Vivo).</summary>
        private Rectangle ProjectorScreenBounds()
        {
            try
            {
                Screen[] screens = Screen.AllScreens;
                int idx = _cmbScreen != null && _cmbScreen.SelectedIndex >= 0 ? _cmbScreen.SelectedIndex : 0;
                if (screens.Length == 0) return Screen.PrimaryScreen.Bounds;
                return screens[Math.Min(idx, screens.Length - 1)].Bounds;
            }
            catch (Exception)
            {
                return Screen.PrimaryScreen.Bounds;
            }
        }

        /// <summary>
        /// Sincroniza las salidas extra tras cada cambio de slide: video en vivo,
        /// monitor de escenario, texto OBS y disparo de activadores.
        /// </summary>
        private void SyncLiveExtras(int itemIndex)
        {
            // ---- ítem actual ----
            ScenarioItem curItem = null;
            if (_lastScenarioItems != null && itemIndex >= 0 && itemIndex < _lastScenarioItems.Count)
                curItem = _lastScenarioItems[itemIndex];

            // ---- VIDEO: interceptar ítems de video ----
            if (curItem != null && curItem.Kind == "video" && curItem.VideoPath.Length > 0 &&
                _currentIndex >= 0 && !_black && IsWindows())
            {
                if (_videoForm == null)
                {
                    _videoForm = new VideoPlayerForm();
                    _videoForm.MediaEnded += OnVideoEnded;
                }
                if (_videoSlideIndex != _currentIndex)
                {
                    _videoSlideIndex = _currentIndex;
                    // v5.1.0: loop/volumen del índice (antes hardcodeado false/100).
                    bool ok = _videoForm.Play(curItem.VideoPath, ProjectorScreenBounds(),
                        loop: curItem.VideoLoop, volume0to100: curItem.VideoVolume);
                    Status(ok ? "Reproduciendo video: " + System.IO.Path.GetFileName(curItem.VideoPath)
                              : "El video no pudo iniciarse (¿Windows Media Player?).");
                }
            }
            else
            {
                StopVideo();
            }

            // ---- escenario (monitor) + director (3ª salida) ----
            UpdateStageView(curItem);
            UpdateDirectorView(curItem);

            // ---- texto OBS en vivo ----
            if (_settings.ObsTextSource.Length > 0)
            {
                ObsSendLiveText(BuildLiveText());
            }

            // ---- activadores ----
            if (_triggers != null && _settings.TriggersEnabled)
            {
                Dictionary<string, object> ctx = new Dictionary<string, object>();
                ctx["index"] = _currentIndex;
                ctx["item"] = itemIndex;
                if (curItem != null)
                {
                    ctx["kind"] = curItem.Kind;
                    ctx["title"] = curItem.Title;
                    ctx["path"] = curItem.Kind == "video" ? curItem.VideoPath : curItem.ImagePath;
                }
                _triggers.Fire("slide_changed", ctx);
                if (curItem != null)
                {
                    _triggers.Fire("item_changed", ctx);
                    if (curItem.Kind == "song" && _lastItemIndex != itemIndex)
                        _triggers.Fire("song_started", ctx);
                }
            }
            _lastItemIndex = itemIndex;
        }

        private int _lastItemIndex = -1;

        private void StopVideo()
        {
            if (_videoForm != null && _videoSlideIndex >= 0)
            {
                _videoForm.StopAndHide();
                _videoSlideIndex = -1;
            }
        }

        private void OnVideoEnded(object sender, EventArgs e)
        {
            try
            {
                BeginInvoke(new SendOrPostCallback(delegate
                {
                    string path = _videoForm != null ? _videoForm.CurrentPath : string.Empty;
                    // activador video_ended
                    if (_triggers != null && _settings.TriggersEnabled)
                    {
                        Dictionary<string, object> ctx = new Dictionary<string, object>();
                        ctx["item"] = _lastItemIndex;
                        ctx["path"] = path;
                        ctx["title"] = System.IO.Path.GetFileName(path);
                        _triggers.Fire("video_ended", ctx);
                    }
                    if (_settings.AutoAdvanceVideo && _videoSlideIndex >= 0 && RequireEngine())
                    {
                        _videoSlideIndex = -1;
                        _engine.Next();   // avance automático
                    }
                    else
                    {
                        StopVideo();
                    }
                }), null);
            }
            catch (Exception) { }
        }

        /// <summary>Monitor de escenario: abre en la pantalla configurada.</summary>
        private void OpenStageView()
        {
            if (!IsWindows()) { Status("El monitor de escenario requiere Windows."); return; }
            if (_stageForm == null || _stageForm.IsDisposed)
                _stageForm = new StageViewForm();
            Screen[] screens = Screen.AllScreens;
            int idx = Math.Max(0, Math.Min(_settings.StageScreen, screens.Length - 1));
            _stageForm.OpenOn(screens[idx]);
            UpdateStageView(null);
            Status("Monitor de escenario abierto (pantalla " + idx + "). Escape para cerrar.");
        }

        private void UpdateStageView(ScenarioItem curItem)
        {
            if (_stageForm == null || _stageForm.IsDisposed || !_stageForm.Visible) return;
            List<string> cur = new List<string>();
            if (_currentIndex >= 0 && _currentIndex < _slides.Count)
                cur = _slides[_currentIndex].Lines;
            List<string> next = new List<string>();
            if (_currentIndex + 1 < _slides.Count)
                next = _slides[_currentIndex + 1].Lines;
            string refLabel = _currentIndex >= 0 && _currentIndex < _slides.Count
                ? _slides[_currentIndex].RefLabel : string.Empty;
            string nextItemTitle = string.Empty;
            if (_lastScenarioItems != null && curItem != null)
            {
                int nextIdx = _lastItemIndex + 1;
                if (nextIdx >= 0 && nextIdx < _lastScenarioItems.Count)
                    nextItemTitle = _lastScenarioItems[nextIdx].Title;
            }
            _stageForm.SetState(curItem != null ? curItem.Title : string.Empty,
                nextItemTitle, refLabel, cur, next);
        }

        /// <summary>v5.1.0: abre la 3ª salida (pantalla del Director).</summary>
        private void OpenDirectorView()
        {
            if (!IsWindows()) { Status("La pantalla del director requiere Windows."); return; }
            if (_directorForm == null || _directorForm.IsDisposed)
                _directorForm = new DirectorForm();
            Screen[] screens = Screen.AllScreens;
            int idx = Math.Max(0, Math.Min(_settings.DirectorScreen, screens.Length - 1));
            _directorForm.OpenOn(screens[idx]);
            UpdateDirectorView(null);
            Status("Pantalla del director abierta (pantalla " + idx + "). Escape para cerrar; " +
                   "Espacio/R = cronómetro; notas abajo.");
        }

        /// <summary>
        /// v5.1.0: refresca la pantalla del director: texto COMPLETO del ítem
        /// actual (todas sus líneas, para leer adelantado) + próximo ítem.
        /// </summary>
        private void UpdateDirectorView(ScenarioItem curItem)
        {
            if (_directorForm == null || _directorForm.IsDisposed || !_directorForm.Visible) return;
            List<string> cur = new List<string>();
            if (curItem != null) cur.AddRange(DirectorItemLines(curItem));
            List<string> next = new List<string>();
            string nextTitle = string.Empty;
            if (_lastScenarioItems != null && _lastItemIndex + 1 >= 0 &&
                _lastItemIndex + 1 < _lastScenarioItems.Count)
            {
                ScenarioItem nextItem = _lastScenarioItems[_lastItemIndex + 1];
                nextTitle = nextItem.Title;
                next.AddRange(DirectorItemLines(nextItem));
            }
            string refLabel = _currentIndex >= 0 && _currentIndex < _slides.Count
                ? _slides[_currentIndex].RefLabel : string.Empty;
            _directorForm.SetState(curItem != null ? curItem.Title : string.Empty,
                nextTitle, refLabel, cur, next);
        }

        /// <summary>Todas las líneas legibles de un ítem (para el director).</summary>
        private static List<string> DirectorItemLines(ScenarioItem it)
        {
            List<string> lines = new List<string>();
            if (it == null) return lines;
            if (it.Kind == "song" && it.Song != null)
            {
                string raw = it.Song.LyricsText();
                foreach (string l in raw.Replace("\r\n", "\n").Split('\n'))
                {
                    string t = l.Trim();
                    if (t.Length > 0 && !t.StartsWith("[", StringComparison.Ordinal))
                        lines.Add(t);
                }
            }
            else if (it.Kind == "text" && it.Text.Length > 0)
            {
                foreach (string l in it.Text.Replace("\r\n", "\n").Split('\n'))
                    if (l.Trim().Length > 0) lines.Add(l.Trim());
            }
            else if (it.Kind == "scripture" && it.Text.Length > 0)
            {
                foreach (string l in it.Text.Replace("\r\n", "\n").Split('\n'))
                    if (l.Trim().Length > 0) lines.Add(l.Trim());
            }
            else if (it.Kind == "video")
            {
                lines.Add("(video en reproducción: " +
                    System.IO.Path.GetFileName(it.VideoPath) + ")");
            }
            else if (it.Kind == "image")
            {
                lines.Add("(imagen: " + System.IO.Path.GetFileName(it.ImagePath) + ")");
                if (it.Text.Length > 0) lines.Add(it.Text);
            }
            else if (it.Title.Length > 0)
            {
                lines.Add(it.Title);
            }
            return lines;
        }

        /// <summary>v5.1.0: pausa/reanuda el video en vivo (tecla P / botón).</summary>
        private void ToggleVideoPause()
        {
            if (_videoForm == null || _videoSlideIndex < 0)
            {
                Status("No hay video en reproducción.");
                return;
            }
            _videoForm.PauseOrResume();
            Status("Video en pausa/reanudado (P alterna).");
        }

        /// <summary>Muestra el zócalo (avisos/acciones show_text).</summary>
        private void ShowLowerThird(string line1, string line2, int seconds)
        {
            if (!IsWindows()) { Status("El zócalo requiere Windows."); return; }
            if (_lowerThird == null || _lowerThird.IsDisposed)
                _lowerThird = new LowerThirdsOverlay();
            _lowerThird.Show(line1, line2, ProjectorScreenBounds(), seconds);
            Status("Zócalo: " + line1);
        }

        /// <summary>Audio oculto (acción play_audio) vía el mismo WMP late-bound.</summary>
        private void PlayAudioHidden(string path, int volume)
        {
            if (!IsWindows()) return;
            if (_audioForm == null)
            {
                _audioForm = new VideoPlayerForm();
                _audioForm.Visible = false;   // nunca se muestra
            }
            _audioForm.PlayAudio(path, volume);
        }

        private VideoPlayerForm _audioForm;

        /// <summary>Aplica un tema por nombre desde data\themes (acción set_theme).</summary>
        private void ApplyThemeByName(string name)
        {
            try
            {
                string dir = System.IO.Path.Combine(_settings.DataDir, "themes");
                string file = System.IO.Path.Combine(dir, SafeThemeFileName(name) + ".json");
                if (!System.IO.File.Exists(file))
                {
                    Status("Tema no encontrado: " + name);
                    return;
                }
                Dictionary<string, object> o = MiniJson.Parse(
                    System.IO.File.ReadAllText(file, new UTF8Encoding(false)));
                _theme = Theme.FromDict(o);
                if (_engine != null) _engine.SetTheme(MiniJson.Serialize(_theme.ToDict()));
                _settings.ThemeJson = MiniJson.Serialize(_theme.ToDict());
                _settings.Theme = _theme.Name;
                SaveSettings();
                ApplyThemeToControls();
                Status("Tema aplicado: " + _theme.Name);
            }
            catch (Exception ex)
            {
                Status("No se pudo aplicar el tema: " + ex.Message);
            }
        }

        /* ======================================================================
         *  EN VIVO
         * ==================================================================== */

        private void ShowSelectedSlide()
        {
            if (!RequireEngine()) return;
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
            if (!RequireEngine()) return;   // v5.2.0: defensa interna
            _engine.Black(!_black);
        }

        /* ======================================================================
         *  v5.1.0 — TECLADO GLOBAL EN VIVO (requisito spec §3.1: «atajos de
         *  teclado fluidos» / «utilizando el teclado, control remoto o móviles»)
         *  → / Espacio / AvPág : siguiente slide
         *  ← / RePág           : slide anterior
         *  B                    : negro (ocultar salida)
         *  L                    : limpiar salida
         *  F5                   : mostrar proyector
         *  F6 / F7 / F8         : monitor de escenario / director / zócalo
         *  No intercepta cuando el foco está en un control editable (el texto
         *  de búsqueda, el editor de canciones…) — las flechas siguen siendo
         *  del usuario mientras edita.
         * ==================================================================== */
        private static bool IsEditable(Control c)
        {
            if (c == null) return false;
            // v5.3.0: LuminaCheck también posee Espacio/flechas (casilla enfocada).
            return c is TextBox || c is ComboBox || c is NumericUpDown || c is ListBox
                || c is LuminaCheck;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Las teclas de función SIEMPRE funcionan (no colisionan con edición).
            if (keyData == Keys.F1) { ShowShortcutsDialog(); return true; }
            if (keyData == Keys.F5) { ShowProjector(); return true; }
            if (keyData == Keys.F6) { OpenStageView(); return true; }
            if (keyData == Keys.F7) { OpenDirectorView(); return true; }
            if (keyData == Keys.F8) { ShowLowerThirdDialog(); return true; }

            // v5.3.0: Ctrl+1…9 salta a la página indicada (en cualquier foco:
            // Ctrl+dígito no interfiere con la edición de texto).
            if ((keyData & Keys.Control) == Keys.Control)
            {
                Keys pure = keyData & Keys.KeyCode;
                if (pure >= Keys.D1 && pure <= Keys.D9)
                {
                    int navIndex = (int)(pure - Keys.D1);
                    if (navIndex < NavToPage.Length)
                    {
                        NavigateToIndex(NavToPage[navIndex]);
                        return true;
                    }
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            // El resto respeta el foco editable.
            if (IsEditable(ActiveControl)) return base.ProcessCmdKey(ref msg, keyData);

            switch (keyData)
            {
                case Keys.Right:
                case Keys.PageDown:
                    if (_engine != null) { _engine.Next(); return true; }
                    break;
                case Keys.Space:
                    if (ActiveControl is Button) break;      // barra espaciadora activa botones
                    if (_engine != null) { _engine.Next(); return true; }
                    break;
                case Keys.Left:
                case Keys.PageUp:
                    if (_engine != null) { _engine.Prev(); return true; }
                    break;
                case Keys.B:
                    if (_engine != null) { ToggleBlack(); return true; }
                    break;
                case Keys.L:
                    if (_engine != null) { _engine.Clear(); return true; }
                    break;
                case Keys.P:
                    ToggleVideoPause();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowProjector()
        {
            if (!RequireEngine()) return;
            int screen = _cmbScreen.SelectedIndex < 0 ? 0 : _cmbScreen.SelectedIndex;
            int st = _engine.ProjectorShow(screen, _chkFullscreen.Checked);
            Status(st == LuminaStatus.Ok
                ? "Proyector mostrado (pantalla " + screen + (_chkFullscreen.Checked ? ", completa)." : ").")
                : "El núcleo no puede proyectar (" + LuminaStatus.Name(st) + "). " +
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
            if (_lblSlidesEmpty != null) _lblSlidesEmpty.Visible = _slides.Count == 0;
            HighlightCurrentSlide();
        }

        private void HighlightCurrentSlide()
        {
            if (_currentIndex < 0 || _currentIndex >= _lvSlides.Items.Count)
            {
                UpdatePreviewInfo();
                return;
            }
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
            UpdatePreviewInfo();
        }

        /// <summary>v5.3.0: «Jn 3:16 · Diapositiva 2 de 5» bajo la vista previa.</summary>
        private void UpdatePreviewInfo()
        {
            if (_lblPreviewInfo == null) return;
            try
            {
                int n = _slides != null ? _slides.Count : 0;
                if (n == 0)
                {
                    _lblPreviewInfo.Text = "Sin diapositivas en el escenario";
                    return;
                }
                if (_currentIndex < 0 || _currentIndex >= n)
                {
                    _lblPreviewInfo.Text = "Diapositiva — de " +
                        n.ToString(CultureInfo.InvariantCulture);
                    return;
                }
                SlideView v = _slides[_currentIndex];
                string refLabel = v != null && v.RefLabel != null && v.RefLabel.Length > 0
                    ? v.RefLabel : "Diapositiva";
                _lblPreviewInfo.Text = refLabel + "  ·  Diapositiva " +
                    (_currentIndex + 1).ToString(CultureInfo.InvariantCulture) + " de " +
                    n.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                // informativo: jamás rompe por texto
            }
        }

        /// <summary>v5.3.0: refleja el estado «negro» en el botón (se enciende).</summary>
        private void UpdateBlackButton()
        {
            try
            {
                LuminaButton lb = _btnBlack as LuminaButton;
                if (lb != null) lb.Active = _black;
            }
            catch (Exception) { }
        }

        /// <summary>v5.3.0: diálogo con el mapa completo de atajos (F1).</summary>
        private void ShowShortcutsDialog()
        {
            try
            {
                string text =
                    "Atajos de teclado\r\n\r\n" +
                    "  F1      · esta ayuda\r\n" +
                    "  F5      · mostrar proyector\r\n" +
                    "  F6      · monitor de escenario\r\n" +
                    "  F7      · ventana de director\r\n" +
                    "  F8      · zócalo inferior / aviso\r\n" +
                    "  → / Espacio / AvPág  · siguiente diapositiva\r\n" +
                    "  ← / RePág             · diapositiva anterior\r\n" +
                    "  B       · ocultar la salida en negro\r\n" +
                    "  L       · limpiar la salida\r\n" +
                    "  P       · pausar el video en la salida\r\n" +
                    "  Enter   · proyectar la diapositiva seleccionada\r\n" +
                    "  Ctrl+1…9 · ir a la página n de la navegación\r\n\r\n" +
                    "(las letras solo actúan cuando NO se está editando texto)";
                MessageBox.Show(this, text,
                    AppName + " — atajos de teclado",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception) { }
        }

        /// <summary>Pide el PNG al motor y lo pinta (nueva Bitmap para desacoplar el stream).</summary>
        private void RefreshPreview()
        {
            if (_pbPreview == null) return;
            Image old = _pbPreview.Image;
            _pbPreview.Image = null;
            if (old != null)
            {
                old.Dispose();
                old = null;
            }
            byte[] png = null;
            if (_engine != null && _currentIndex >= 0)
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
            _lblNoPreview.Text = _engine == null
                ? "Vista previa no disponible (núcleo inactivo)"
                : (_currentIndex >= 0
                    ? "Vista previa no disponible para esta slide"
                    : "Carga una canción o pasaje para ver la vista previa");
            _lblNoPreview.Visible = _pbPreview.Image == null;
            _chipLive.Visible = _pbPreview.Image != null;
            if (_previewArea != null) _previewArea.Invalidate();   // borde «en vivo»
        }

        /* ======================================================================
         *  ESCENARIO (builder → motor)
         * ==================================================================== */

        private void LoadScenarioFromItems(IList<ScenarioItem> items, string name)
        {
            // v5.2.0 «MOTOR» — blindaje del bug de campo v5.1.1: el flujo
            // Biblia → «Cargar al escenario» llegaba aquí con el núcleo caído
            // (modo limitado) y _engine.LoadScenario lanzaba
            // NullReferenceException. Contrato del método: NUNCA lanza;
            // sin datos o sin núcleo → mensaje claro en la barra de estado.
            if (!RequireEngine()) return;                      // núcleo caído → aviso, no crash
            if (items == null || items.Count == 0)
            {
                Status("No hay ítems que cargar al escenario.");
                return;
            }
            name = name ?? string.Empty;
            if (_theme == null) _theme = new Theme();           // defensa adicional
            try
            {
                LoadScenarioFromItemsCore(items, name);
            }
            catch (Exception ex)
            {
                // La ventana vale más que la operación: registrar y avisar.
                Status("No se pudo cargar el escenario: " + ex.Message);
                try { Program.LogLine("LoadScenarioFromItems FALLO: " + ex); }
                catch (Exception) { }
            }
        }

        private void LoadScenarioFromItemsCore(IList<ScenarioItem> items, string name)
        {
            string json = ScenarioBuilder.BuildScenarioJson(name, _theme, items);
            int st = _engine.LoadScenario(json);
            if (st != LuminaStatus.Ok)
            {
                Status("El núcleo rechazó el escenario (" + LuminaStatus.Name(st) + ").");
                return;
            }
            _slides.Clear();
            _slides.AddRange(ScenarioBuilder.FlattenScenario(json,
                delegate(string songJson) { return LuminaEngine.SongParse(songJson); }));
            if (_api != null) _api.SetSlideCatalog(_slides);
            if (_remote != null) _remote.SetSlideCatalog(_slides);   // v5.0.0: mando remoto
            StopVideo();                                            // escenario nuevo: sin video activo
            // Recordar el escenario activo: «Aplicar tema» lo reconstruye tal cual.
            _lastScenarioItems = new List<ScenarioItem>(items);
            _lastScenarioName = name;
            RefreshLiveList();
            NavigateToIndex(0);
            if (_lblExportInfo != null)
            {
                _lblExportInfo.Text = "Escenario activo: «" + name + "» — " + _slides.Count +
                    " diapositiva(s) listas para exportar (página Exportar).";
            }
            Status("Escenario «" + name + "» cargado: " + _slides.Count + " slide(s).");
        }

        private void LoadSongToStage()
        {
            if (!RequireEngine()) return;   // v5.2.0: sin núcleo → aviso, no crash
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
            if (!RequireEngine()) return;   // v5.2.0: sin núcleo → aviso, no crash (bug de campo v5.1.1)
            string reference = _txtRef.Text.Trim();
            if (reference.Length == 0)
            {
                Status("Escribe una referencia (p. ej. Jn 3:16-18).");
                return;
            }
            string version = _txtVersion.Text.Trim();
            // v5.2.0 «MOTOR»: resolver los versículos AQUÍ (cuando hay BD) y
            // entregarlos como texto del ítem. Así la lista «En vivo», el
            // monitor de escenario y la exportación PPTX/PDF muestran el
            // pasaje REAL — no solo lo proyecta el núcleo. El motor usa el
            // texto tal cual (misma consulta que haría él internamente).
            string versesText = ResolveScriptureText(reference, version);
            ScenarioItem it = ScenarioBuilder.ScriptureItem(
                reference, version, (int)_numVersesPerSlide.Value, versesText);
            if (!_dbOpen)
                Status("Nota: sin BD abierta el núcleo no puede resolver los versículos.");
            LoadScenarioFromItems(new ScenarioItem[] { it }, reference);
            if (version.Length > 0)
            {
                _settings.LastBibleVersion = version;
                SaveSettings();
            }
        }

        /// <summary>
        /// v5.2.0: texto de los versículos de una referencia, desde la BD del
        /// motor (mismo criterio que Engine::Flatten — bind por versión, rango
        /// desde el verso inicial, tope 120). Sin BD o referencia inválida →
        /// "" (el motor intentará su propia resolución al aplanar).
        /// </summary>
        private string ResolveScriptureText(string reference, string version)
        {
            if (_engine == null || !_dbOpen) return string.Empty;
            try
            {
                string resJson = LuminaEngine.BibleRefResolve(reference);
                Dictionary<string, object> o = MiniJson.Parse(resJson);
                if (MiniJson.GetInt(o, "valid", 0) != 1) return string.Empty;
                long book = MiniJson.GetInt(o, "book", 0);
                long chapter = MiniJson.GetInt(o, "chapter", 0);
                long verse = MiniJson.GetInt(o, "verse", 0);
                long vFrom = verse > 0 ? verse : 1;
                const int kMaxVerses = 120;
                string q = LuminaStorage.BuildExecJson(
                    "SELECT text FROM bible WHERE version=?1 AND book=?2 AND chapter=?3 " +
                    "AND verse>=?4 AND verse<=?5 ORDER BY verse",
                    new object[] { version, book, chapter, vFrom, vFrom + kMaxVerses - 1 });
                string rows = _engine.DbExec(q);
                List<List<object>> r = LuminaStorage.Rows(rows);
                StringBuilder sb = new StringBuilder();
                foreach (List<object> row in r)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(Cell(row, 0));
                }
                return sb.ToString();
            }
            catch (Exception)
            {
                return string.Empty;   // el motor hará su propio intento
            }
        }

        private void ResolveReference()
        {
            if (!RequireEngine()) return;
            string reference = _txtRef.Text.Trim();
            if (reference.Length == 0) { _lblResolve.Text = "Escribe una referencia."; return; }
            try
            {
                string resJson = LuminaEngine.BibleRefResolve(reference);
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
                _lblResolve.ForeColor = valid == 1 ? UiTheme.Ok : UiTheme.Err;
            }
            catch (LuminaException ex)
            {
                _lblResolve.Text = "Error del núcleo: " + ex.Message;
                _lblResolve.ForeColor = UiTheme.Err;
            }
            catch (FormatException)
            {
                _lblResolve.Text = "Respuesta inválida del núcleo.";
                _lblResolve.ForeColor = UiTheme.Err;
            }
            catch (Exception ex)
            {
                _lblResolve.Text = "Núcleo no disponible: " + ex.Message;
                _lblResolve.ForeColor = UiTheme.Err;
            }
        }

        /* ======================================================================
         *  v5.1.0 — BIBLIA: BÚSQUEDA POR PALABRA (FTS5 · tabla bible_fts)
         *  El índice lo crea el núcleo al abrir la BD (Storage.cpp) y lo
         *  mantienen los triggers bible_ai/bible_ad; solo faltaba la consulta
         *  y la UI — requisito spec §3.2: «búsqueda instantánea por cita o
         *  palabra clave y opción de resaltado».
         * ==================================================================== */

        /// <summary>Sanitiza el término para FTS5 (solo palabra + comodín prefijo).</summary>
        private static string FtsTerm(string raw)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in (raw ?? string.Empty))
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (char.IsWhiteSpace(c)) break;   // solo la primera palabra
            }
            return sb.ToString().ToLowerInvariant();
        }

        /// <summary>Resalta el término con «…» (resaltado tolerante a mayúsculas).</summary>
        private static string HighlightTerm(string text, string term)
        {
            if (string.IsNullOrEmpty(text) || term.Length == 0) return text;
            int idx = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return text;
            return text.Substring(0, idx) + "«" + text.Substring(idx, term.Length) + "»" +
                   text.Substring(idx + term.Length);
        }

        private void SearchBibleWords()
        {
            if (!RequireEngine() || !RequireDb()) return;
            string term = FtsTerm(_txtBibleSearch.Text);
            if (term.Length == 0)
            {
                Status("Escribe una palabra para buscar en la Biblia (p. ej. «misericordia»).");
                return;
            }
            try
            {
                // FTS5 del núcleo: MATCH por prefijo (term*) sobre la columna
                // indexada "text"; version/book/chapter/verse vienen UNINDEXED.
                string sql = "SELECT version, book, chapter, verse, text FROM bible_fts " +
                             "WHERE bible_fts MATCH ?1 ORDER BY book, chapter, verse LIMIT 200";
                string req = LuminaStorage.BuildExecJson(sql, term + "*");
                string res = _engine.DbExec(req);
                List<List<object>> rows = LuminaStorage.Rows(res);
                _lvBibleResults.BeginUpdate();
                try
                {
                    _lvBibleResults.Items.Clear();
                    foreach (List<object> row in rows)
                    {
                        string version = Cell(row, 0);
                        long book, chapter, verse;
                        long.TryParse(Cell(row, 1), out book);
                        long.TryParse(Cell(row, 2), out chapter);
                        long.TryParse(Cell(row, 3), out verse);
                        string text = Cell(row, 4);
                        // Referencia legible con la tabla de 66 libros (espejo C++).
                        string name = BooksTable.NameOf((int)book);
                        string abbr = BooksTable.AbbrOf((int)book);
                        string refTxt = (abbr.Length > 0 ? abbr : "libro " + book) + " " +
                            chapter + ":" + verse + (version.Length > 0 ? " (" + version + ")" : "");
                        ListViewItem it = new ListViewItem(refTxt);
                        it.SubItems.Add(HighlightTerm(text, term));
                        it.Tag = BooksTable.Reference((int)book, (int)chapter, (int)verse, (int)verse)
                                 + "|" + version + "|" + (name.Length > 0 ? name : "?");
                        _lvBibleResults.Items.Add(it);
                    }
                }
                finally
                {
                    _lvBibleResults.EndUpdate();
                }
                _lblBibleSearchInfo.Text = rows.Count + " versículo(s) con «" + term + "»" +
                    (rows.Count >= 200 ? " (límite alcanzado — afina la búsqueda)" : "") +
                    " · doble clic carga el pasaje";
                Status("Búsqueda bíblica «" + term + "»: " + rows.Count + " resultado(s).");
            }
            catch (LuminaException ex)
            {
                Status("Búsqueda bíblica falló: " + ex.Message);
            }
        }

        /// <summary>Doble clic en un resultado → ese versículo al escenario.</summary>
        private void LoadBibleResultToStage()
        {
            if (_lvBibleResults.SelectedItems.Count == 0) return;
            string tag = Convert.ToString(_lvBibleResults.SelectedItems[0].Tag,
                CultureInfo.InvariantCulture);
            string[] parts = (tag ?? string.Empty).Split('|');
            if (parts.Length < 2 || parts[0].Length == 0)
            {
                Status("No se pudo derivar la referencia de ese versículo.");
                return;
            }
            _txtRef.Text = parts[0];
            _txtVersion.Text = parts[1];
            LoadScriptureToStage();
        }

        /* ======================================================================
         *  CANCIONES — BD
         * ==================================================================== */

        private void SaveSongToDb()
        {
            if (!RequireEngine() || !RequireDb()) return;
            string lyrics = _txtSongLyrics.Text ?? string.Empty;
            string title = _txtSongTitle.Text.Trim();
            if (title.Length == 0) { Status("La canción necesita título."); return; }
            try
            {
                string res = _engine.DbExec(LuminaStorage.InsertSongRequest(
                    title, _txtSongArtist.Text.Trim(), string.Empty, 0, string.Empty, lyrics));
                long id = LuminaStorage.LastId(res);
                Status("Canción guardada en la BD (id " + id + ").");
            }
            catch (LuminaException ex)
            {
                Status("No se pudo guardar: " + ex.Message);
            }
        }

        private void SearchSongs()
        {
            if (!RequireEngine() || !RequireDb()) return;
            string term = _txtSearch.Text.Trim();
            if (term.Length == 0) { Status("Escribe un término de búsqueda."); return; }
            try
            {
                string res = _engine.DbExec(LuminaStorage.SearchSongsRequest(term, 50));
                List<List<object>> rows = LuminaStorage.Rows(res);
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
                _lblResultCount.Text = rows.Count + " resultado(s) · biblioteca local";
                Status("Búsqueda «" + term + "»: " + rows.Count + " resultado(s).");
            }
            catch (LuminaException ex)
            {
                Status("Búsqueda falló: " + ex.Message);
            }
        }

        private void LoadResultToEditor()
        {
            if (!RequireEngine() || !RequireDb() || _lvResults.SelectedItems.Count == 0) return;
            long id;
            if (!long.TryParse(_lvResults.SelectedItems[0].Text, out id)) return;
            try
            {
                string res = _engine.DbExec(LuminaStorage.SelectSongByIdRequest(id));
                List<List<object>> rows = LuminaStorage.Rows(res);
                if (rows.Count == 0) { Status("La canción ya no existe."); return; }
                List<object> row = rows[0];
                _txtSongTitle.Text = row.Count > 1 ? Cell(row, 1) : string.Empty;
                _txtSongArtist.Text = row.Count > 2 ? Cell(row, 2) : string.Empty;
                _txtSongLyrics.Text = row.Count > 6 ? Cell(row, 6) : string.Empty;
                Status("Canción #" + id + " cargada al editor.");
            }
            catch (LuminaException ex)
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
            Status("Abre (o crea) una base de datos en Ajustes → Base de datos.");
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
            if (!RequireEngine()) return;
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Importar canción JSON";
                dlg.Filter = "Canción JSON (*.json)|*.json|Todos los archivos (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string json = MiniJson.Utf8BytesToString(File.ReadAllBytes(dlg.FileName));
                    string parsed = LuminaEngine.SongParse(json);   // valida ANTES de agregar
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
                catch (LuminaException ex)
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
            if (!RequireEngine()) return;
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
                    statsJson = LuminaEngine.BibParse(data, "{\"wantRows\":1}");
                }
                catch (LuminaException ex)
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

        /// <summary>
        /// v5.0.0: importa una Biblia ZEFania XML (estándar Holyrics/OpenLP:
        /// RVR1960…) vía parser streaming propio → filas → misma tabla bible.
        /// </summary>
        private void ImportZefaniaXml()
        {
            if (!RequireEngine()) return;
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Importar Biblia ZEFania XML";
                dlg.Filter = "Biblia ZEFania (*.xml)|*.xml|Todos los archivos (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                ZefaniaResult res;
                try
                {
                    res = ZefaniaBible.Parse(dlg.FileName, null);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "El XML no se pudo leer: " + ex.Message, "Importar",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (res.Verses.Count == 0)
                {
                    MessageBox.Show(this,
                        "No se encontraron versículos.\n\n" +
                        "El formato esperado es ZEFania XML:\n" +
                        "  <XMLBIBLE biblename=\"…\">\n" +
                        "    <BIBLEBOOK bnumber=\"1\" …><CHAPTER cnumber=\"1\">\n" +
                        "      <VERSE vnumber=\"1\">texto…</VERSE>",
                        "Importar ZEFania", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string version = res.VersionName.Length > 0 ? res.VersionName : "Zefania";
                MessageBox.Show(this,
                    "Biblia ZEFania validada:\n" +
                    "  Versión: " + version + "\n" +
                    "  Libros: " + res.BookCount + "\n" +
                    "  Versículos: " + res.Verses.Count + "\n" +
                    "  Filas descartadas: " + res.SkippedRows,
                    "Importar ZEFania", MessageBoxButtons.OK, MessageBoxIcon.Information);

                if (MessageBox.Show(this, "¿Insertar esta biblia en la base de datos?",
                        "Importar ZEFania", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                    != DialogResult.Yes) return;
                if (!RequireDb()) return;

                // Filas en el mismo contrato que InsertBibleRows:
                // [version, book, chapter, verse, text]
                List<object> rows = new List<object>(res.Verses.Count);
                foreach (ZefaniaVerse v in res.Verses)
                {
                    List<object> row = new List<object>(5);
                    row.Add(version);
                    row.Add(v.Book);
                    row.Add(v.Chapter);
                    row.Add(v.Verse);
                    row.Add(v.Text);
                    rows.Add(row);
                }
                InsertBibleRows(version, rows, res.Verses.Count);
            }
        }

        /// <summary>Aviso manual (lower third) desde En Vivo.</summary>
        private void ShowLowerThirdDialog()
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "Aviso en pantalla (zócalo)";
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MinimizeBox = dlg.MaximizeBox = false;
                dlg.ClientSize = new Size(460, 170);
                dlg.Font = UiTheme.Body;
                dlg.BackColor = UiTheme.PageBg;
                dlg.ForeColor = UiTheme.TextPrimary;

                Label l1 = UiTheme.MkLabel("Línea principal:", UiTheme.TextSecondary, UiTheme.Small, true);
                l1.Dock = DockStyle.Top; l1.Height = 20;
                TextBox t1 = UiTheme.MkInput(false);
                t1.Dock = DockStyle.Top;
                Label l2 = UiTheme.MkLabel("Línea secundaria (opcional):", UiTheme.TextSecondary, UiTheme.Small, true);
                l2.Dock = DockStyle.Top; l2.Height = 20;
                TextBox t2 = UiTheme.MkInput(false);
                t2.Dock = DockStyle.Top;

                FlowLayoutPanel btns = new FlowLayoutPanel();
                btns.Dock = DockStyle.Bottom;
                btns.Height = 46;
                btns.Padding = new Padding(0, 10, 0, 0);
                Button ok = UiTheme.MkButton("Mostrar", "primary", delegate
                {
                    if (t1.Text.Trim().Length == 0) { dlg.Close(); return; }
                    ShowLowerThird(t1.Text.Trim(), t2.Text.Trim(), 8);
                    dlg.Close();
                });
                ok.Width = 120;
                Button hide = UiTheme.MkButton("Ocultar zócalo", "secondary", delegate
                {
                    if (_lowerThird != null) _lowerThird.HideNow();
                    dlg.Close();
                });
                hide.Width = 130;
                btns.Controls.Add(ok);
                btns.Controls.Add(hide);

                dlg.Controls.Add(t2);
                dlg.Controls.Add(l2);
                dlg.Controls.Add(t1);
                dlg.Controls.Add(l1);
                dlg.Controls.Add(btns);
                dlg.ShowDialog(this);
            }
        }

        /// <summary>v5.0.0: agrega un ítem de VIDEO al culto (reproducción en la salida).</summary>
        private void AddVideoItemToService()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Agregar video al culto";
                dlg.Filter = "Video (*.mp4;*.wmv;*.avi;*.m4v)|*.mp4;*.wmv;*.avi;*.m4v|" +
                             "Todos los archivos (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                // v5.1.0: opciones de reproducción (bucle/volumen) — antes
                // hardcodeadas (sin bucle, 100) y sin exponer en la UI.
                bool loop = false;
                int volume = 100;
                using (Form opt = new Form())
                {
                    opt.Text = "Opciones del video";
                    opt.StartPosition = FormStartPosition.CenterParent;
                    opt.FormBorderStyle = FormBorderStyle.FixedDialog;
                    opt.MinimizeBox = opt.MaximizeBox = false;
                    opt.ClientSize = new Size(360, 128);
                    opt.Font = UiTheme.Body;
                    opt.BackColor = UiTheme.PageBg;
                    opt.ForeColor = UiTheme.TextPrimary;

                    LuminaCheck chkLoop = UiTheme.MkCheck("Repetir en bucle");
                    chkLoop.Dock = DockStyle.Top;
                    chkLoop.Height = 28;

                    Label lv = UiTheme.MkLabel("Volumen (0-100):", UiTheme.TextSecondary, UiTheme.Small, true);
                    lv.Dock = DockStyle.Top; lv.Height = 20;
                    NumericUpDown numVol = UiTheme.MkNumeric(0, 100, 100);
                    numVol.Dock = DockStyle.Top;
                    numVol.Height = 26;

                    FlowLayoutPanel btns = new FlowLayoutPanel();
                    btns.Dock = DockStyle.Bottom;
                    btns.Height = 40;
                    Button ok = UiTheme.MkButton("Agregar", "primary", delegate { opt.Close(); });
                    ok.Width = 110;
                    btns.Controls.Add(ok);

                    opt.Controls.Add(numVol);
                    opt.Controls.Add(lv);
                    opt.Controls.Add(chkLoop);
                    opt.Controls.Add(btns);
                    if (opt.ShowDialog(this) == DialogResult.OK)
                    {
                        loop = chkLoop.Checked;
                        volume = (int)numVol.Value;
                    }
                }

                ScenarioItem it = ScenarioBuilder.VideoItem(
                    "Video: " + Path.GetFileName(dlg.FileName), dlg.FileName, loop, volume);
                _serviceItems.Add(it);
                RefreshServiceList();
                Status("Video agregado al culto: " + it.Title +
                       (loop ? " (bucle, vol " + volume + ")" : " (vol " + volume + ")"));
            }
        }

        /* ------------------------------------------------------------------
         *  v5.1.0 «FUNDAMENTO» — editor de culto COMPLETO (spec §3.2)
         * ------------------------------------------------------------------ */

        /// <summary>Agrega la canción que está en el editor (página Canciones).</summary>
        private void AddCurrentSongToService()
        {
            Song s = ScenarioBuilder.SongFromEditor(
                _txtSongTitle.Text, _txtSongArtist.Text, _txtSongLyrics.Text,
                _chkHymnMode.Checked, (int)_numTranspose.Value);
            if (s.Title.Length == 0) s.Title = "Sin título";
            if (s.Blocks.Count == 0)
            {
                Status("El editor de canciones está vacío: escribe la letra primero.");
                return;
            }
            _serviceItems.Add(ScenarioBuilder.FromSong(s));
            RefreshServiceList();
            Status("Canción «" + s.Title + "» agregada al culto (" + _serviceItems.Count + " ítems).");
        }

        /// <summary>Agrega el pasaje bíblico actual de la página Biblia.</summary>
        private void AddCurrentScriptureToService()
        {
            string reference = _txtRef.Text.Trim();
            if (reference.Length == 0)
            {
                Status("Escribe la referencia en la página Biblia (p. ej. Sal 23:1-6).");
                return;
            }
            ScenarioItem it = ScenarioBuilder.ScriptureItem(
                reference, _txtVersion.Text.Trim(), (int)_numVersesPerSlide.Value, string.Empty);
            _serviceItems.Add(it);
            RefreshServiceList();
            Status("Pasaje «" + reference + "» agregado al culto (" + _serviceItems.Count + " ítems).");
        }

        /// <summary>Agrega una imagen (JPG/PNG/GIF/BMP/TIF) con pie opcional.</summary>
        private void AddImageItemToService()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Agregar imagen al culto";
                dlg.Filter = "Imágenes (*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tif;*.tiff)|" +
                             "*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tif;*.tiff|" +
                             "Todos los archivos (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string caption = string.Empty;
                // Pie de imagen opcional (etiqueta semántica del spec §3.2).
                using (Form f = new Form())
                {
                    f.Text = "Pie de imagen (opcional)";
                    f.StartPosition = FormStartPosition.CenterParent;
                    f.FormBorderStyle = FormBorderStyle.FixedDialog;
                    f.MinimizeBox = f.MaximizeBox = false;
                    f.ClientSize = new Size(380, 118);
                    f.Font = UiTheme.Body;
                    f.BackColor = UiTheme.PageBg;
                    f.ForeColor = UiTheme.TextPrimary;
                    Label l = UiTheme.MkLabel("Texto sobre la imagen (opcional):",
                        UiTheme.TextSecondary, UiTheme.Small, true);
                    l.Dock = DockStyle.Top; l.Height = 20;
                    TextBox t = UiTheme.MkInput(false);
                    t.Dock = DockStyle.Top;
                    FlowLayoutPanel btns = new FlowLayoutPanel();
                    btns.Dock = DockStyle.Bottom; btns.Height = 40;
                    Button ok = UiTheme.MkButton("Agregar", "primary", delegate { f.Close(); });
                    ok.Width = 110;
                    btns.Controls.Add(ok);
                    f.Controls.Add(t); f.Controls.Add(l); f.Controls.Add(btns);
                    if (f.ShowDialog(this) == DialogResult.OK) caption = t.Text.Trim();
                }
                ScenarioItem it = ScenarioBuilder.ImageItem(
                    Path.GetFileName(dlg.FileName), dlg.FileName, caption);
                _serviceItems.Add(it);
                RefreshServiceList();
                Status("Imagen agregada al culto: " + it.Title);
            }
        }

        /// <summary>Agrega un ítem de texto/aviso con varias líneas.</summary>
        private void AddTextItemToService()
        {
            string text = string.Empty;
            using (Form f = new Form())
            {
                f.Text = "Texto / aviso para el culto";
                f.StartPosition = FormStartPosition.CenterParent;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MinimizeBox = f.MaximizeBox = false;
                f.ClientSize = new Size(420, 240);
                f.Font = UiTheme.Body;
                f.BackColor = UiTheme.PageBg;
                f.ForeColor = UiTheme.TextPrimary;
                Label l = UiTheme.MkLabel("Líneas a proyectar (una por slide agrupada):",
                    UiTheme.TextSecondary, UiTheme.Small, true);
                l.Dock = DockStyle.Top; l.Height = 20;
                TextBox t = UiTheme.MkInput(true);
                t.Dock = DockStyle.Fill;
                t.Text = "Bienvenidos a la casa del Señor";
                FlowLayoutPanel btns = new FlowLayoutPanel();
                btns.Dock = DockStyle.Bottom; btns.Height = 40;
                Button ok = UiTheme.MkButton("Agregar", "primary", delegate { f.Close(); });
                ok.Width = 110;
                btns.Controls.Add(ok);
                f.Controls.Add(t); f.Controls.Add(l); f.Controls.Add(btns);
                if (f.ShowDialog(this) == DialogResult.OK) text = t.Text;
            }
            if (text.Trim().Length == 0) { Status("El texto quedó vacío: no se agregó nada."); return; }
            string title = text.Trim();
            int nl = title.IndexOf('\n');
            if (nl > 0) title = title.Substring(0, nl).Trim();
            _serviceItems.Add(ScenarioBuilder.TextItem(title, text, 4));
            RefreshServiceList();
            Status("Texto agregado al culto: " + title);
        }

        /// <summary>Agrega una slide en blanco (pausa visual del culto).</summary>
        private void AddBlankItemToService()
        {
            _serviceItems.Add(ScenarioBuilder.BlankItem("En blanco"));
            RefreshServiceList();
            Status("Ítem en blanco agregado (" + _serviceItems.Count + " ítems).");
        }

        /// <summary>
        /// v5.1.0: importa un PLAN de servicio JSON (formato documentado en
        /// docs/api/PlanningCenter.md — pensado para listas exportadas de
        /// Planning Center Online u otros servicios, y para compartir cultos
        /// entre equipos). Cada elemento puede ser:
        ///   {"type":"song","title":"…","artist":"…","lyrics":"[V1]\n…"}
        ///   {"type":"scripture","ref":"jn 3:16-18","version":"RVR1909"}
        ///   {"type":"text","title":"…","text":"…"}
        ///   {"type":"blank","title":"…"}
        /// </summary>
        private void ImportPlanJson()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Importar plan de servicio JSON";
                dlg.Filter = "Plan JSON (*.json)|*.json|Todos los archivos (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Dictionary<string, object> plan = MiniJson.Parse(
                        File.ReadAllText(dlg.FileName, new UTF8Encoding(false)));
                    List<object> items = MiniJson.GetArray(plan, "items");
                    if (items.Count == 0)
                    {
                        MessageBox.Show(this, "El plan no trae elementos (se espera \"items\": [...]).",
                            "Importar plan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    int added = 0;
                    foreach (object ro in items)
                    {
                        Dictionary<string, object> io = ro as Dictionary<string, object>;
                        if (io == null) continue;
                        string type = MiniJson.GetString(io, "type", "blank").ToLowerInvariant();
                        string title = MiniJson.GetString(io, "title", string.Empty);
                        if (type == "song")
                        {
                            Song s = new Song();
                            s.Title = title.Length > 0 ? title : "Canción";
                            s.Artist = MiniJson.GetString(io, "artist", string.Empty);
                            s.Lyrics = MiniJson.GetString(io, "lyrics", string.Empty);
                            List<SongBlock> blocks = ScenarioBuilder.ParseLyricsBlocks(s.Lyrics);
                            if (blocks.Count > 0) s.Blocks = blocks;
                            _serviceItems.Add(ScenarioBuilder.FromSong(s));
                        }
                        else if (type == "scripture")
                        {
                            string refr = MiniJson.GetString(io, "ref", string.Empty);
                            if (refr.Length == 0) refr = title;
                            _serviceItems.Add(ScenarioBuilder.ScriptureItem(refr,
                                MiniJson.GetString(io, "version", _settings.LastBibleVersion),
                                1, MiniJson.GetString(io, "text", string.Empty)));
                        }
                        else if (type == "text")
                        {
                            _serviceItems.Add(ScenarioBuilder.TextItem(title,
                                MiniJson.GetString(io, "text", title), 4));
                        }
                        else
                        {
                            _serviceItems.Add(ScenarioBuilder.BlankItem(
                                title.Length > 0 ? title : "En blanco"));
                        }
                        added++;
                    }
                    string planName = MiniJson.GetString(plan, "name", string.Empty);
                    if (planName.Length > 0) _txtServiceName.Text = planName;
                    RefreshServiceList();
                    Status("Plan importado: " + added + " ítems agregados al culto.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "El plan no se pudo leer: " + ex.Message,
                        "Importar plan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>
        /// INSERT por lotes en transacción. v5.1.0: multi-VALUES de 100 filas
        /// por sentencia (antes 1 DbExec POR VERSÍCULO — la RVR1909 completa
        /// exigía ~31.000 llamadas nativas con un búfer de 1 MB cada una).
        /// </summary>
        private void InsertBibleRows(string version, List<object> rows, long expectedVerses)
        {
            if (_engine == null) { Status("Sin núcleo no se puede importar."); return; }   // v5.2.0
            try
            {
                string beginRes = _engine.DbExec(LuminaStorage.RawSqlRequest("BEGIN"));
                if (beginRes == null)
                {
                    Status("No se pudo iniciar la transacción.");
                    return;
                }
                long inserted = 0;
                try
                {
                    const int batchSize = 100;
                    int total = rows.Count;
                    for (int start = 0; start < total; start += batchSize)
                    {
                        int count = Math.Min(batchSize, total - start);
                        StringBuilder sql = new StringBuilder(
                            "INSERT OR IGNORE INTO bible(version,book,chapter,verse,text) VALUES ");
                        List<object> pars = new List<object>(count * 5);
                        for (int i = 0; i < count; i++)
                        {
                            if (i > 0) sql.Append(',');
                            sql.Append("(?,?,?,?,?)");
                            List<object> row = rows[start + i] as List<object>;
                            if (row == null)
                            {
                                pars.Add(version); pars.Add(0); pars.Add(0); pars.Add(0); pars.Add("");
                                continue;
                            }
                            // Formatos tolerados: [book,ch,verse,text] o [version,book,ch,verse,text]
                            string v = row.Count >= 5 ? Cell(row, 0) : version;
                            int off = row.Count >= 5 ? 1 : 0;
                            long book, chapter, verse;
                            if (!long.TryParse(Cell(row, off), out book) ||
                                !long.TryParse(Cell(row, off + 1), out chapter) ||
                                !long.TryParse(Cell(row, off + 2), out verse))
                            {
                                pars.Add(v); pars.Add(0); pars.Add(0); pars.Add(0); pars.Add("");
                                continue;
                            }
                            pars.Add(v);
                            pars.Add(book);
                            pars.Add(chapter);
                            pars.Add(verse);
                            pars.Add(Cell(row, off + 3));
                            inserted++;
                        }
                        _engine.DbExec(LuminaStorage.BuildExecJson(sql.ToString(), pars.ToArray()));
                    }
                    _engine.DbExec(LuminaStorage.RawSqlRequest("COMMIT"));
                    Status("Biblia «" + version + "» importada (" + inserted + " versículos).");
                    MessageBox.Show(this,
                        "Insertados " + inserted + " versículos" +
                        (expectedVerses > 0 && inserted != expectedVerses
                            ? " (esperados " + expectedVerses + "; los duplicados se ignoran)."
                            : "."),
                        "Importar Biblia", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception)
                {
                    _engine.DbExec(LuminaStorage.RawSqlRequest("ROLLBACK"));
                    throw;
                }
            }
            catch (LuminaException ex)
            {
                MessageBox.Show(this, "Fallo insertando la biblia: " + ex.Message, "Importar",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SendServiceToStage()
        {
            if (!RequireEngine()) return;
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
            _txtDbPath.Text = Path.Combine(_settings.DataDir, "lumina.db");
            // v5.1.0: pantalla del proyector persistida (clamp al combo real).
            if (_cmbScreen != null && _cmbScreen.Items.Count > 0)
                _cmbScreen.SelectedIndex = Math.Max(0,
                    Math.Min(_settings.ProjectionScreen, _cmbScreen.Items.Count - 1));
            // v5.1.0: respaldo
            _txtBackupFolder.Text = _settings.BackupFolder;
            _chkAutoBackup.Checked = _settings.AutoBackupOnExit;
            // v5.0.0: integraciones
            _txtObsUrl.Text = _settings.ObsUrl;
            _txtObsPassword.Text = _settings.ObsPassword;
            _txtObsTextSource.Text = _settings.ObsTextSource;
            _chkObsAutoConnect.Checked = _settings.ObsAutoConnect;
            _chkRemoteEnabled.Checked = _settings.RemoteEnabled;
            _numRemotePort.Value = _settings.RemotePort;
            _txtRemoteToken.Text = _settings.RemoteToken;
            _chkMidiEnabled.Checked = _settings.MidiEnabled;
            _chkAutoAdvanceVideo.Checked = _settings.AutoAdvanceVideo;
            // Tema persistido (v4.1.0): JSON completo en settings.json.
            if (_settings.ThemeJson.Length > 0)
            {
                try
                {
                    _theme = Theme.FromDict(MiniJson.Parse(_settings.ThemeJson));
                }
                catch (Exception)
                {
                    _theme = new Theme();   // JSON corrupto → tema por defecto
                }
            }
            ApplyThemeToControls();
            // Biblioteca de temas (v4.2.0): escanea data\themes y marca el tema en uso.
            _appliedThemeName = _settings.Theme;
            RefreshThemeLibrary(_settings.Theme);
        }

        private void SaveSettings()
        {
            _settings.ApiPort = (int)_numPort.Value;
            _settings.ApiToken = _txtToken.Text;
            _settings.Theme = _theme.Name;
            _settings.LastBibleVersion = _txtVersion.Text.Trim();
            _settings.ThemeJson = MiniJson.Serialize(_theme.ToDict());
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
            if (!RequireEngine()) return;
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
            _lblApiState.ForeColor = running ? UiTheme.Ok : UiTheme.Err;
            _tsslApi.Text = running ? "API: 127.0.0.1:" + _api.Port : "API: detenida";
            if (_chipApi != null)
                _chipApi.SetState(running ? "API " + _api.Port : "API: detenida",
                                  running ? UiTheme.Ok : UiTheme.Err);
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
            if (!RequireEngine()) return;
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
            if (st == LuminaStatus.Ok)
            {
                _dbOpen = true;
                Status("BD abierta: " + path);
                MaybeSeedFactoryBible();   // v5.1.0: RVR1909 de fábrica si la BD está vacía
            }
            else
            {
                _dbOpen = false;
                Status("No se pudo abrir la BD (" + LuminaStatus.Name(st) + "): " + path);
            }
            UpdateStatusBar();
        }

        /// <summary>
        /// v5.1.0 «FUNDAMENTO»: si la BD no tiene versículos y el paquete trae
        /// la Reina-Valera 1909 (resources/data/bible_rvr1909.json), se ofrece
        /// instalarla — el paquete portable incluye una Biblia COMPLETA lista
        /// para proyectar sin que el usuario busque archivos externos.
        /// Lector streaming propio (BibleJson): memoria O(1).
        /// </summary>
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
                if (count > 0) return;    // ya hay biblias: no molestar

                if (MessageBox.Show(this,
                        "La base de datos está vacía y este paquete incluye la Biblia\n" +
                        "Reina-Valera 1909 completa (dominio público).\n\n" +
                        "¿Instalarla ahora? (~31.000 versículos; tarda unos segundos)",
                        "Biblia incluida", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                    != DialogResult.Yes) return;

                BibleJsonResult r = BibleJson.ParseFile(biblePath, 0);
                if (r.VerseCount == 0)
                {
                    Status("La biblia incluida no produjo versículos (¿archivo dañado?).");
                    return;
                }
                _txtVersion.Text = r.Version.Length > 0 ? r.Version : "RVR1909";
                _settings.LastBibleVersion = _txtVersion.Text;
                InsertBibleRows(r.Version.Length > 0 ? r.Version : "RVR1909", r.Rows, r.VerseCount);
            }
            catch (Exception ex)
            {
                Status("No se pudo instalar la biblia incluida: " + ex.Message);
            }
        }

        private void UpdateStatusBar()
        {
            _tsslDb.Text = _dbOpen ? "BD: abierta" : "BD: —";
            _chipDb.SetState(_dbOpen ? "BD abierta" : "BD: —",
                             _dbOpen ? UiTheme.Ok : UiTheme.TextDisabled);
            _chipCore.SetState(_engine != null ? "Núcleo " + SafeCoreVersion() : "Núcleo: error",
                               _engine != null ? UiTheme.Ok : UiTheme.Err);
            _tsslCore.Text = _engine != null ? "Núcleo: " + SafeCoreVersion() : "Núcleo: error";
            _lblAboutCore.Text = _engine != null ? SafeCoreVersion() : "no disponible (modo limitado)";
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
                if (_tips != null) { try { _tips.Dispose(); } catch (Exception) { } _tips = null; }
                StopVideo();
                if (_videoForm != null) { try { _videoForm.Dispose(); } catch (Exception) { } _videoForm = null; }
                if (_audioForm != null) { try { _audioForm.Dispose(); } catch (Exception) { } _audioForm = null; }
                if (_stageForm != null) { try { _stageForm.Close(); } catch (Exception) { } _stageForm = null; }
                if (_directorForm != null) { try { _directorForm.Close(); } catch (Exception) { } _directorForm = null; }
                if (_lowerThird != null) { try { _lowerThird.Dispose(); } catch (Exception) { } _lowerThird = null; }
                StopMidi();
                CloseMidiOut();
                StopRemote();
#if !LUMINA_NET35
                if (_obs != null) { try { _obs.Dispose(); } catch (Exception) { } _obs = null; }
#endif
                if (_api != null) { try { _api.Dispose(); } catch (Exception) { } _api = null; }
                if (_engine != null) { try { _engine.Dispose(); } catch (Exception) { } _engine = null; }
                // v5.1.0: respaldo automático al salir (si está configurado).
                if (_settings.AutoBackupOnExit && _settings.BackupFolder.Length > 0)
                {
                    try { PerformBackup(); } catch (Exception) { }
                }
            }
            base.OnFormClosing(e);
        }
    }
}
