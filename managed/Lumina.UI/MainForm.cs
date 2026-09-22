// ============================================================================
//  LuminaPresentation Suite v4.2.0 «ACORDES» — managed/Lumina.UI/MainForm.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MainForm.cs : ventana principal (WinForms, todo el layout en código — sin
//  Designer). Rediseño v4.0.0: tema oscuro plano (flat), barra lateral de
//  navegación con iconos vectoriales, cabecera con chips de estado y tarjetas
//  — diseño aprobado en mockup (ver worklog Task G-1).
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
using System.Text;
using System.Threading;
using System.Windows.Forms;
using lumina.api;
using lumina.bridge;
using lumina.core;

namespace lumina.ui
{
    /* ====================================================================== */
    /*  UI — TEMA Y FÁBRICA DE CONTROLES                                      */
    /* ====================================================================== */

    /// <summary>Paleta y tipografía del tema oscuro «Lumina» (mockup G-1).</summary>
    internal static class UiTheme
    {
        // ---- superficie ----
        public static readonly Color PageBg      = FromHex(0x12141A);
        public static readonly Color BarBg       = FromHex(0x171A21);
        public static readonly Color BarBorder   = FromHex(0x262B36);
        public static readonly Color CardBg      = FromHex(0x1B1F28);
        public static readonly Color CardBorder  = FromHex(0x2A3040);
        // ---- controles ----
        public static readonly Color InputBg     = FromHex(0x10131A);
        public static readonly Color InputFocus  = FromHex(0xF0A93B);
        // ---- acentos ----
        public static readonly Color Accent      = FromHex(0xF0A93B); // ámbar Lumina
        public static readonly Color AccentHover = FromHex(0xFFC15E);
        public static readonly Color OnAccent    = FromHex(0x1A1408);
        public static readonly Color Danger      = FromHex(0xD05050);
        public static readonly Color DangerHover = FromHex(0xE06A6A);
        public static readonly Color Ok          = FromHex(0x58C08A);
        public static readonly Color Err         = FromHex(0xE05252);
        // ---- texto ----
        public static readonly Color TextPrimary   = FromHex(0xECEEF2);
        public static readonly Color TextSecondary = FromHex(0x9AA0A6);
        public static readonly Color TextDisabled  = FromHex(0x566070);
        // ---- filas ----
        public static readonly Color RowOdd      = FromHex(0x161A22);
        public static readonly Color RowEven     = FromHex(0x1B1F28);
        public static readonly Color RowSelected = FromHex(0x2F3646);
        public static readonly Color RowHover    = FromHex(0x262B36);
        public static readonly Color HoverLayer  = FromHex(0x1F242E);
        public static readonly Color PreviewBg   = Color.Black;

        // ---- tipografía (96 dpi lógico) ----
        public static readonly Font H1        = new Font("Segoe UI", 14.5F, FontStyle.Bold);
        public static readonly Font H2        = new Font("Segoe UI", 9.75F, FontStyle.Bold);
        public static readonly Font Header    = new Font("Segoe UI", 13.5F, FontStyle.Bold);
        public static readonly Font Body      = new Font("Segoe UI", 9.25F);
        public static readonly Font Small     = new Font("Segoe UI", 8.25F);
        public static readonly Font SmallBold = new Font("Segoe UI", 8.25F, FontStyle.Bold);
        public static readonly Font Mono      = new Font("Consolas", 9.75F);

        public static Color FromHex(int rgb)
        {
            return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }

        /* --------------------------------------------------- fábrica rápida */

        public static Label MkLabel(string text, Color color, Font font, bool auto)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = color;
            l.Font = font;
            l.BackColor = Color.Transparent;
            l.AutoSize = auto;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Margin = new Padding(0);
            return l;
        }

        /// <summary>Botón plano del tema. variant: "primary"|"secondary"|"danger".</summary>
        public static Button MkButton(string text, string variant, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.Font = Body;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Cursor = Cursors.Hand;
            b.Height = 30;
            b.Padding = new Padding(12, 0, 12, 0);
            b.Margin = new Padding(0, 0, 8, 0);
            b.TextAlign = ContentAlignment.MiddleCenter;
            b.UseVisualStyleBackColor = false;

            if (variant == "primary")
            {
                b.BackColor = Accent;
                b.ForeColor = OnAccent;
                b.FlatAppearance.MouseOverBackColor = AccentHover;
                b.FlatAppearance.MouseDownBackColor = Accent;
            }
            else if (variant == "danger")
            {
                b.BackColor = Danger;
                b.ForeColor = Color.White;
                b.FlatAppearance.MouseOverBackColor = DangerHover;
                b.FlatAppearance.MouseDownBackColor = Danger;
            }
            else // secondary
            {
                b.BackColor = RowHover;
                b.ForeColor = TextPrimary;
                b.FlatAppearance.MouseOverBackColor = RowSelected;
                b.FlatAppearance.MouseDownBackColor = RowSelected;
                b.FlatAppearance.BorderColor = CardBorder;
                b.FlatAppearance.BorderSize = 1;
            }
            if (onClick != null) b.Click += onClick;
            return b;
        }

        /// <summary>TextBox oscuro con foco ámbar.</summary>
        public static TextBox MkInput(bool multiline)
        {
            TextBox t = new TextBox();
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = InputBg;
            t.ForeColor = TextPrimary;
            t.Font = Body;
            t.Margin = new Padding(0);
            if (multiline)
            {
                t.Multiline = true;
                t.ScrollBars = ScrollBars.Vertical;
                t.AcceptsReturn = true;
            }
            t.GotFocus += delegate { t.BackColor = UiTheme.InputBg; };
            return t;
        }

        public static NumericUpDown MkNumeric(decimal min, decimal max, decimal value)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min; n.Maximum = max; n.Value = value;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.BackColor = InputBg;
            n.ForeColor = TextPrimary;
            n.Font = Body;
            n.Margin = new Padding(0);
            return n;
        }

        /// <summary>Tarjeta con título (mockup: bg #1B1F28, borde #2A3040).</summary>
        public static Panel MkCard(string title, out ContentPanel body)
        {
            Panel card = new Panel();
            card.BackColor = CardBg;
            card.Padding = new Padding(14);
            card.Margin = new Padding(0);
            card.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(CardBorder))
                    e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
            };

            Label lblTitle = MkLabel(title, TextPrimary, H2, true);
            lblTitle.Dock = DockStyle.Top;
            lblTitle.Height = 24;

            body = new ContentPanel();
            body.BackColor = CardBg;
            body.Dock = DockStyle.Fill;

            card.Controls.Add(body);
            card.Controls.Add(lblTitle);
            return card;
        }
    }

    /// <summary>Panel sin parpadeo (doble búfer) para interiores de tarjeta.</summary>
    internal class ContentPanel : Panel
    {
        public ContentPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    /* ====================================================================== */
    /*  UI — CHIPS DE ESTADO (cabecera / barra inferior)                      */
    /* ====================================================================== */

    /// <summary>Chip plano: punto de color + texto (estado del sistema).</summary>
    internal sealed class Chip : Control
    {
        private Color _dot;
        private string _text;
        private readonly bool _boxed;

        public Chip(string text, Color dot, bool boxed)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            _text = text;
            _dot = dot;
            _boxed = boxed;
            BackColor = Color.Transparent;
            Font = UiTheme.Small;
            Height = 22;
            Cursor = Cursors.Default;
            UpdateWidth();
        }

        public void SetState(string text, Color dot)
        {
            _text = text;
            _dot = dot;
            UpdateWidth();
            Invalidate();
        }

        private void UpdateWidth()
        {
            int textW = TextRenderer.MeasureText(_text, UiTheme.Small).Width;
            Width = 8 + 8 + 6 + textW + 12 + (_boxed ? 2 : 0); // pad + dot + gap + text + pad
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (_boxed)
            {
                using (SolidBrush bg = new SolidBrush(UiTheme.CardBg)) g.FillRectangle(bg, r);
                using (Pen pen = new Pen(UiTheme.CardBorder)) g.DrawRectangle(pen, r);
            }
            int cy = Height / 2;
            using (SolidBrush dot = new SolidBrush(_dot))
                g.FillEllipse(dot, 10, cy - 4, 8, 8);
            TextRenderer.DrawText(g, _text, UiTheme.Small,
                new Rectangle(24, 0, Width - 24, Height),
                UiTheme.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            Invalidate();
        }
    }

    /* ====================================================================== */
    /*  UI — BARRA LATERAL DE NAVEGACIÓN                                      */
    /* ====================================================================== */

    /// <summary>
    /// Navegación lateral owner-drawn: iconos vectoriales simples + texto,
    /// barra activa ámbar de 3 px (mockup G-1). Items fijos del producto.
    /// </summary>
    internal sealed class NavPanel : ContentPanel
    {
        private static readonly string[] Captions =
            { "En Vivo", "Canciones", "Biblia", "Temas", "Culto", "Ajustes" };

        private const int ItemCount = 6;

        private readonly Rectangle[] _itemRects = new Rectangle[ItemCount];
        private int _hover = -1;
        private int _active;

        /// <summary>Índice del ítem clicado (válido al dispararse NavActivated).</summary>
        public int ClickedIndex { get; private set; }

        /// <summary>Se dispara al elegir un ítem: leer ClickedIndex.</summary>
        public event EventHandler NavActivated;

        public NavPanel()
        {
            BackColor = UiTheme.BarBg;
            Width = 210;
            for (int i = 0; i < ItemCount; i++)
                _itemRects[i] = new Rectangle(0, 44 + i * 46, Width, 46);
        }

        public void SetActive(int index)
        {
            _active = index;
            Invalidate();
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < ItemCount; i++)
                if (_itemRects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = HitTest(e.Location);
            if (i >= 0 && NavActivated != null)
            {
                ClickedIndex = i;
                _active = i;
                Invalidate();
                NavActivated(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Caption de sección (mockup: "MONITOREO" 11px gris, espaciado).
            TextRenderer.DrawText(g, "MONITOREO", UiTheme.SmallBold,
                new Rectangle(20, 14, Width - 20, 18), UiTheme.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            for (int i = 0; i < ItemCount; i++)
            {
                Rectangle r = _itemRects[i];
                bool isActive = i == _active;
                bool isHover = i == _hover && !isActive;

                using (SolidBrush bg = new SolidBrush(
                    isActive ? UiTheme.RowSelected : (isHover ? UiTheme.HoverLayer : BackColor)))
                    g.FillRectangle(bg, r);

                if (isActive)
                    using (SolidBrush bar = new SolidBrush(UiTheme.Accent))
                        g.FillRectangle(bar, r.X, r.Y, 3, r.Height);

                DrawIcon(g, i, r.X + 20, r.Y + 15, isActive ? UiTheme.TextPrimary : UiTheme.TextSecondary);

                TextRenderer.DrawText(g, Captions[i], UiTheme.Body,
                    new Rectangle(r.X + 48, r.Y, r.Width - 48, r.Height),
                    isActive ? Color.White : UiTheme.TextSecondary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            // Borde derecho de la barra.
            using (Pen p = new Pen(UiTheme.BarBorder))
                g.DrawLine(p, Width - 1, 0, Width - 1, Height);
        }

        /// <summary>Iconos 16×16 con formas simples (compatibles GDI+/Win7).</summary>
        private void DrawIcon(Graphics g, int index, int x, int y, Color c)
        {
            using (SolidBrush b = new SolidBrush(c))
            using (Pen p = new Pen(c, 1.6F))
            {
                switch (index)
                {
                    case 0: // En Vivo: triángulo de reproducción
                        Point[] tri = {
                            new Point(x + 3, y + 2), new Point(x + 13, y + 8), new Point(x + 3, y + 14) };
                        g.FillPolygon(b, tri);
                        break;
                    case 1: // Canciones: nota musical
                        g.FillEllipse(b, x + 2, y + 10, 5, 4);
                        g.DrawLine(p, x + 7, y + 12, x + 7, y + 2);
                        g.FillPolygon(b, new[] {
                            new Point(x + 7, y + 2), new Point(x + 13, y + 4), new Point(x + 7, y + 7) });
                        break;
                    case 2: // Biblia: cruz
                        g.FillRectangle(b, x + 6, y + 1, 4, 14);
                        g.FillRectangle(b, x + 2, y + 5, 12, 4);
                        break;
                    case 3: // Temas: círculo de muestra (mitad rellena)
                        g.DrawEllipse(p, x + 2, y + 2, 12, 12);
                        g.FillPie(b, x + 2, y + 2, 12, 12, -90, 180);
                        break;
                    case 4: // Culto: lista
                        for (int i = 0; i < 3; i++)
                        {
                            g.FillEllipse(b, x + 1, y + 2 + i * 5, 3, 3);
                            g.DrawLine(p, x + 7, y + 3 + i * 5, x + 14, y + 3 + i * 5);
                        }
                        break;
                    case 5: // Ajustes: engranaje simplificado
                        g.DrawEllipse(p, x + 4, y + 4, 8, 8);
                        g.FillRectangle(b, x + 7, y, 2, 4);
                        g.FillRectangle(b, x + 7, y + 12, 2, 4);
                        g.FillRectangle(b, x, y + 7, 4, 2);
                        g.FillRectangle(b, x + 12, y + 7, 4, 2);
                        break;
                }
            }
        }
    }

    /* ====================================================================== */
    /*  VENTANA PRINCIPAL                                                     */
    /* ====================================================================== */

    public sealed class MainForm : Form
    {
        private const string AppName = "LuminaPresentation Suite";
        private const string AppVersion = "4.2.0";
        private const string AppTitle = AppName + " — v" + AppVersion;

        /* ------------------------------------------------------------ servicios */
        private readonly Settings _settings;
        private LuminaEngine _engine;                 // null = modo limitado (sin núcleo)
        private ApiServer _api;
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
        private CheckBox _chkThemeBold, _chkThemeUpper;
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
        private readonly Panel[] _pages = new Panel[6];
        private Chip _chipCore, _chipDb, _chipApi;

        // En Vivo
        private ListView _lvSlides;
        private Button _btnPrev, _btnLive, _btnNext, _btnBlack, _btnClear, _btnProjector;
        private ComboBox _cmbScreen;
        private CheckBox _chkFullscreen;
        private Panel _previewArea;
        private PictureBox _pbPreview;
        private Label _lblNoPreview;
        private Chip _chipLive;
        private Panel _navInfoCard;

        // Canciones
        private TextBox _txtSongTitle, _txtSongArtist, _txtSongLyrics, _txtSearch;
        private CheckBox _chkHymnMode;
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

            BuildHeader();
            BuildSidebar();
            BuildStatusBar();
            BuildContent();

            // El contexto de WinForms queda instalado al crear los controles:
            // el motor encola los eventos a este hilo vía ese contexto.
            _uiCtx = SynchronizationContext.Current ?? new SynchronizationContext();

            CreateEngine();
            ApplySettingsToControls();
            UpdateStatusBar();
        }

        /* ======================================================================
         *  CONSTRUCCIÓN — esqueleto (cabecera, sidebar, contenido, estado)
         * ==================================================================== */

        private void BuildHeader()
        {
            Panel header = new ContentPanel();
            header.Dock = DockStyle.Top;
            header.Height = 56;
            header.BackColor = UiTheme.BarBg;

            Label title = UiTheme.MkLabel(AppName, Color.White, UiTheme.Header, true);
            title.Location = new Point(24, 0);
            title.Height = 56;
            title.TextAlign = ContentAlignment.MiddleLeft;

            Chip version = new Chip("v" + AppVersion + " · HÍBRIDA", UiTheme.Accent, true);
            version.Location = new Point(24 + title.PreferredWidth + 12, 17);

            _chipCore = new Chip("Núcleo: iniciando", UiTheme.TextDisabled, true);
            _chipDb = new Chip("BD: —", UiTheme.TextDisabled, true);
            _chipApi = new Chip("API: detenida", UiTheme.Err, true);

            header.Controls.Add(title);
            header.Controls.Add(version);
            header.Controls.Add(_chipCore);
            header.Controls.Add(_chipDb);
            header.Controls.Add(_chipApi);

            header.Resize += delegate { PlaceHeaderChips(header, version); };
            header.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(UiTheme.BarBorder))
                    e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
            };

            Controls.Add(header);
        }

        private void PlaceHeaderChips(Panel header, Chip version)
        {
            int x = header.ClientSize.Width - 16;
            _chipApi.Location = new Point(x - _chipApi.Width, 17);
            x = _chipApi.Location.X - 8;
            _chipDb.Location = new Point(x - _chipDb.Width, 17);
            x = _chipDb.Location.X - 8;
            _chipCore.Location = new Point(x - _chipCore.Width, 17);
        }

        private void BuildSidebar()
        {
            _nav = new NavPanel();
            _nav.Dock = DockStyle.Left;
            _nav.NavActivated += NavigateTo;

            // Tarjeta informativa inferior (mockup): salida + identidad.
            _navInfoCard = new ContentPanel();
            _navInfoCard.Dock = DockStyle.Bottom;
            _navInfoCard.Height = 92;
            _navInfoCard.BackColor = UiTheme.BarBg;
            _navInfoCard.Paint += delegate(object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Panel info = (Panel)s;
                Rectangle card = new Rectangle(16, 8, _nav.Width - 32, info.Height - 20);
                using (SolidBrush bg = new SolidBrush(UiTheme.CardBg)) g.FillRectangle(bg, card);
                using (Pen pen = new Pen(UiTheme.CardBorder)) g.DrawRectangle(pen, card);

                // Punto verde + "Salida: Pantalla N"
                using (SolidBrush dot = new SolidBrush(_engine != null ? UiTheme.Ok : UiTheme.TextDisabled))
                    g.FillEllipse(dot, card.X + 12, card.Y + 14, 8, 8);
                string screenTxt = "Salida: Pantalla " +
                    (_cmbScreen != null && _cmbScreen.SelectedIndex > 0
                        ? _cmbScreen.SelectedIndex.ToString(CultureInfo.InvariantCulture)
                        : "0");
                TextRenderer.DrawText(g, screenTxt, UiTheme.SmallBold,
                    new Rectangle(card.X + 26, card.Y + 6, card.Width - 26, 22),
                    UiTheme.TextPrimary, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                TextRenderer.DrawText(g, AppName + " v" + AppVersion, UiTheme.Small,
                    new Rectangle(card.X + 12, card.Y + 30, card.Width - 24, 20),
                    UiTheme.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "Núcleo híbrido C++ · C#", UiTheme.Small,
                    new Rectangle(card.X + 12, card.Y + 48, card.Width - 24, 20),
                    UiTheme.TextDisabled, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            };

            _nav.Controls.Add(_navInfoCard);
            Controls.Add(_nav);
        }

        private void BuildStatusBar()
        {
            Panel status = new ContentPanel();
            status.Dock = DockStyle.Bottom;
            status.Height = 30;
            status.BackColor = UiTheme.BarBg;
            status.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(UiTheme.BarBorder))
                    e.Graphics.DrawLine(p, 0, 0, status.Width, 0);
            };

            _tsslMsg = UiTheme.MkLabel("Listo.", UiTheme.TextSecondary, UiTheme.Small, false);
            _tsslMsg.Dock = DockStyle.Fill;
            _tsslMsg.Padding = new Padding(16, 0, 0, 0);

            _tsslDb = UiTheme.MkLabel("BD: —", UiTheme.TextDisabled, UiTheme.Small, true);
            _tsslDb.Margin = new Padding(0, 0, 16, 0);
            _tsslApi = UiTheme.MkLabel("API: detenida", UiTheme.TextDisabled, UiTheme.Small, true);
            _tsslApi.Margin = new Padding(0, 0, 16, 0);
            _tsslCore = UiTheme.MkLabel("Núcleo: —", UiTheme.TextDisabled, UiTheme.Small, true);
            _tsslCore.Margin = new Padding(0, 0, 16, 0);

            Panel right = new ContentPanel();
            right.Dock = DockStyle.Right;
            right.Width = 380;
            right.BackColor = UiTheme.BarBg;
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.RightToLeft;
            flow.WrapContents = false;
            flow.Controls.Add(_tsslCore);
            flow.Controls.Add(_tsslApi);
            flow.Controls.Add(_tsslDb);
            right.Controls.Add(flow);

            status.Controls.Add(_tsslMsg);
            status.Controls.Add(right);
            Controls.Add(status);
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
            _pages[5] = BuildSettingsPage();
            for (int i = 0; i < 6; i++) _content.Controls.Add(_pages[i]);

            Controls.Add(_content);
            _content.BringToFront();
            NavigateToIndex(0);
        }

        /// <summary>Evento del panel de navegación → cambia la página visible.</summary>
        private void NavigateTo(object sender, EventArgs e)
        {
            NavigateToIndex(_nav.ClickedIndex);
        }

        private int _activeNavItem;

        private void NavigateToIndex(int index)
        {
            if (index < 0 || index >= 6) index = 0;
            _activeNavItem = index;
            for (int i = 0; i < 6; i++) _pages[i].Visible = i == index;
            if (_nav != null) _nav.SetActive(index);
            if (index == 0) RefreshPreview();
        }

        /* ======================================================================
         *  PÁGINA 1 — EN VIVO
         * ==================================================================== */

        private Panel BuildLivePage()
        {
            Panel page = NewPage("En Vivo", "Proyección en tiempo real del escenario",
                /* headerButton */ null);

            ContentPanel body = (ContentPanel)page.Tag;

            TableLayoutPanel grid = NewGrid(2,
                new ColumnStyle(SizeType.Percent, 100),
                new ColumnStyle(SizeType.Absolute, 420));

            // ---- tarjeta izquierda: lista de slides + transporte ----
            ContentPanel slidesBody;
            Panel slidesCard = UiTheme.MkCard("Escenario", out slidesBody);
            slidesCard.Dock = DockStyle.Fill;
            slidesCard.Margin = new Padding(0, 0, 16, 0);

            _lvSlides = NewDarkList();
            _lvSlides.Columns.Add("#", 48, HorizontalAlignment.Right);
            _lvSlides.Columns.Add("Referencia", 190, HorizontalAlignment.Left);
            _lvSlides.Columns.Add("Primera línea", 420, HorizontalAlignment.Left);
            _lvSlides.Dock = DockStyle.Fill;
            _lvSlides.DoubleClick += delegate { ShowSelectedSlide(); };

            FlowLayoutPanel transport = new FlowLayoutPanel();
            transport.Dock = DockStyle.Bottom;
            transport.Height = 44;
            transport.WrapContents = false;
            transport.Padding = new Padding(0, 6, 0, 0);
            _btnPrev = UiTheme.MkButton("◀ Anterior", "secondary", delegate { if (RequireEngine()) _engine.Prev(); });
            _btnLive = UiTheme.MkButton("● EN VIVO", "primary", delegate { ShowSelectedSlide(); });
            _btnLive.Width = 132;
            _btnNext = UiTheme.MkButton("Siguiente ▶", "secondary", delegate { if (RequireEngine()) _engine.Next(); });
            _btnBlack = UiTheme.MkButton("■ Negro", "danger", delegate { if (RequireEngine()) ToggleBlack(); });
            transport.Controls.Add(_btnPrev);
            transport.Controls.Add(_btnLive);
            transport.Controls.Add(_btnNext);
            transport.Controls.Add(_btnBlack);

            FlowLayoutPanel outputRow = new FlowLayoutPanel();
            outputRow.Dock = DockStyle.Bottom;
            outputRow.Height = 40;
            outputRow.WrapContents = false;
            outputRow.Padding = new Padding(0, 2, 0, 0);
            Label lblScreen = UiTheme.MkLabel("Pantalla:", UiTheme.TextSecondary, UiTheme.Small, true);
            lblScreen.Margin = new Padding(0, 10, 8, 0);
            _cmbScreen = NewDarkCombo();
            _cmbScreen.Items.Add("0");
            _cmbScreen.Items.Add("1");
            _cmbScreen.Items.Add("2");
            _cmbScreen.SelectedIndex = 0;
            _cmbScreen.Width = 56;
            _cmbScreen.SelectedIndexChanged += delegate { _navInfoCard.Invalidate(); };
            _chkFullscreen = new CheckBox();
            _chkFullscreen.Text = "Pantalla completa";
            _chkFullscreen.AutoSize = true;
            _chkFullscreen.ForeColor = UiTheme.TextSecondary;
            _chkFullscreen.Font = UiTheme.Small;
            _chkFullscreen.Margin = new Padding(10, 8, 10, 0);
            _btnProjector = UiTheme.MkButton("Mostrar proyector", "secondary", delegate { ShowProjector(); });
            _btnClear = UiTheme.MkButton("× Limpiar", "secondary", delegate { if (RequireEngine()) _engine.Clear(); });
            outputRow.Controls.Add(lblScreen);
            outputRow.Controls.Add(_cmbScreen);
            outputRow.Controls.Add(_chkFullscreen);
            outputRow.Controls.Add(_btnProjector);
            outputRow.Controls.Add(_btnClear);

            slidesBody.Controls.Add(_lvSlides);
            slidesBody.Controls.Add(transport);
            slidesBody.Controls.Add(outputRow);

            // ---- tarjeta derecha: vista previa ----
            ContentPanel previewBody;
            Panel previewCard = UiTheme.MkCard("Vista previa", out previewBody);
            previewCard.Dock = DockStyle.Fill;

            _previewArea = new ContentPanel();
            _previewArea.BackColor = UiTheme.PreviewBg;
            _previewArea.Dock = DockStyle.Top;
            _previewArea.Height = 200;
            _previewArea.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(UiTheme.CardBorder))
                    e.Graphics.DrawRectangle(p, 0, 0, _previewArea.Width - 1, _previewArea.Height - 1);
            };
            _previewArea.Resize += delegate
            {
                _previewArea.Height = Math.Max(120, (_previewArea.Width * 9) / 16);
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
            _chipLive.Parent = _previewArea;   // chip superpuesto arriba-derecha
            _previewArea.Controls.Add(_chipLive);
            _previewArea.Controls.Add(_pbPreview);
            _previewArea.Controls.Add(_lblNoPreview);
            _lblNoPreview.BringToFront();
            _previewArea.Resize += delegate
            {
                _chipLive.Location = new Point(_previewArea.Width - _chipLive.Width - 12, 10);
            };

            Button btnRefresh = UiTheme.MkButton("Refrescar vista", "secondary", delegate { RefreshPreview(); });
            btnRefresh.Dock = DockStyle.Top;
            btnRefresh.Height = 30;

            Label lblRes = UiTheme.MkLabel("Vista previa del núcleo (PNG)", UiTheme.TextDisabled, UiTheme.Small, true);
            lblRes.Dock = DockStyle.Bottom;
            lblRes.Height = 20;
            lblRes.TextAlign = ContentAlignment.MiddleRight;

            previewBody.Controls.Add(_previewArea);
            previewBody.Controls.Add(btnRefresh);
            previewBody.Controls.Add(lblRes);

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
                null);
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
            _chkHymnMode = new CheckBox();
            _chkHymnMode.Text = "Modo Hinario (coro intercalado)";
            _chkHymnMode.AutoSize = true;
            _chkHymnMode.ForeColor = UiTheme.TextPrimary;
            _chkHymnMode.Font = UiTheme.Small;
            _chkHymnMode.Margin = new Padding(0, 4, 16, 0);
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
            Panel page = NewPage("Biblia", "Pasajes y versículos para proyectar", null);
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
            importCard.Height = 132;
            importCard.Margin = new Padding(0, 16, 0, 0);

            Label info = UiTheme.MkLabel(
                "Formatos admitidos: .BIB (directivas #BIB/#VERSION/#BOOKS) y archivos JSON.\n" +
                "La importación valida el archivo con el núcleo y luego inserta los versículos en la base de datos.",
                UiTheme.TextSecondary, UiTheme.Small, false);
            info.Dock = DockStyle.Fill;
            info.Padding = new Padding(0, 2, 0, 6);

            Button btnImport = UiTheme.MkButton("Importar Biblia .BIB…", "secondary", delegate { ImportBibFile(); });
            btnImport.Dock = DockStyle.Bottom;
            btnImport.Height = 30;

            import.Controls.Add(info);
            import.Controls.Add(btnImport);

            // El ORDEN de anexado importa (docking = z-order inverso): los
            // Dock.Top se procesan del último al primero → anexar en orden
            // inverso al visual deseado.
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
            Panel page = NewPage("Temas", "Apariencia de la proyección: colores, tipografía y efectos", null);
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

        private static CheckBox MkDarkCheck(string text)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.ForeColor = UiTheme.TextPrimary;
            c.Font = UiTheme.Small;
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
            Panel page = NewPage("Culto", "Orden del servicio: canciones, pasajes y proyección", null);
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
            itemBtns.Height = 40;
            itemBtns.WrapContents = false;
            itemBtns.Padding = new Padding(0, 4, 0, 0);
            itemBtns.Controls.Add(UiTheme.MkButton("↑ Subir", "secondary", delegate { MoveItem(-1); }));
            itemBtns.Controls.Add(UiTheme.MkButton("↓ Bajar", "secondary", delegate { MoveItem(1); }));
            itemBtns.Controls.Add(UiTheme.MkButton("× Eliminar", "secondary", delegate { RemoveItem(); }));

            list.Controls.Add(_lstItems);
            list.Controls.Add(nameRow);
            list.Controls.Add(itemBtns);

            // ---- acciones ----
            ContentPanel actions;
            Panel actionCard = UiTheme.MkCard("Acciones", out actions);

            Button btnImportSong = UiTheme.MkButton("Importar canción JSON…", "secondary", delegate { ImportSongJson(); });
            btnImportSong.Dock = DockStyle.Top;
            btnImportSong.Height = 32;
            Button btnImportBib = UiTheme.MkButton("Importar Biblia .BIB…", "secondary", delegate { ImportBibFile(); });
            btnImportBib.Dock = DockStyle.Top;
            btnImportBib.Height = 32;
            btnImportBib.Margin = new Padding(0, 8, 0, 0);
            Button btnSend = UiTheme.MkButton("Enviar a proyección", "primary", delegate { SendServiceToStage(); });
            btnSend.Dock = DockStyle.Bottom;
            btnSend.Height = 44;
            btnSend.Font = UiTheme.H2;

            // Anexado inverso (docking = z-order inverso): JSON arriba, enviar abajo.
            actions.Controls.Add(btnSend);
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
            Panel page = NewPage("Ajustes", "API local, base de datos e información del sistema", null);
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
            body.Controls.Add(dbCard);
            body.Controls.Add(apiCard);
            return page;
        }

        /* --------------------------------------------------- helpers visuales */

        /// <summary>Crea la página con título/subtitle en la cabecera; Tag = cuerpo.</summary>
        private static Panel NewPage(string title, string subtitle, EventHandler headerButton)
        {
            Panel page = new ContentPanel();
            page.Dock = DockStyle.Fill;
            page.BackColor = UiTheme.PageBg;
            page.Padding = new Padding(4, 0, 0, 0);

            Panel header = new ContentPanel();
            header.Dock = DockStyle.Top;
            header.Height = 56;
            header.BackColor = UiTheme.PageBg;

            Label t = UiTheme.MkLabel(title, Color.White, UiTheme.H1, true);
            t.Dock = DockStyle.Top;
            t.Height = 34;

            Label st = UiTheme.MkLabel(subtitle, UiTheme.TextSecondary, UiTheme.Small, true);
            st.Dock = DockStyle.Top;
            st.Height = 20;

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

        /// <summary>ListView oscura (cabeceras y filas owner-drawn, mockup G-1).</summary>
        private static ListView NewDarkList()
        {
            ListView lv = new ListView();
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.MultiSelect = false;
            lv.HideSelection = true;
            lv.BorderStyle = BorderStyle.None;
            lv.BackColor = UiTheme.RowEven;
            lv.ForeColor = UiTheme.TextPrimary;
            lv.Font = UiTheme.Body;
            lv.OwnerDraw = true;
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
            TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpper(CultureInfo.InvariantCulture),
                UiTheme.SmallBold, new Rectangle(r.X + 10, r.Y, r.Width - 12, r.Height),
                UiTheme.TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
            using (Pen p = new Pen(UiTheme.CardBorder))
                e.Graphics.DrawLine(p, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);
        }

        private static void DrawDarkSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            ListView lv = (ListView)sender;
            bool selected = e.Item.Selected;
            Color bg = selected ? UiTheme.RowSelected
                : (e.ItemIndex % 2 == 0 ? UiTheme.RowOdd : UiTheme.RowEven);
            Rectangle r = new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height);
            using (SolidBrush b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, r);
            if (e.ColumnIndex == 0)
                using (Pen p = new Pen(UiTheme.CardBorder))
                    e.Graphics.DrawLine(p, r.X, r.Y, r.X, r.Bottom); // separador sutil

            string text = e.SubItem != null ? e.SubItem.Text : string.Empty;
            Color fg = selected ? Color.White : UiTheme.TextPrimary;
            if (e.ColumnIndex == 0) fg = UiTheme.TextSecondary;
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
        /// </summary>
        private void CreateEngine()
        {
            try
            {
                bool headless = !IsWindows();
                _engine = LuminaEngine.Create(headless, OnEngineEventRaw, _uiCtx);
                _engine.UiEventReceived += OnEngineUiEvent;
                _chipCore.SetState("Núcleo " + SafeCoreVersion(), UiTheme.Ok);
            }
            catch (Exception ex)
            {
                _engine = null;
                _chipCore.SetState("Núcleo: error", UiTheme.Err);
                try
                {
                    MessageBox.Show(this,
                        "El núcleo nativo (LuminaCore.dll) no pudo iniciarse:\n\n  " +
                        ex.Message + "\n\n" +
                        "La ventana abrirá en MODO LIMITADO: la interfaz funciona, pero " +
                        "proyección, base de datos e importaciones requieren el núcleo.\n\n" +
                        "Solución habitual: extraiga el ZIP completo en una carpeta propia " +
                        "y ejecute LuminaLauncher.exe desde ahí.",
                        AppName + " — núcleo no disponible",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception) { /* la ventana vale más que el diálogo */ }
            }
        }

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
        private void OnEngineEventRaw(object sender, LuminaEvent e)
        {
            if (e.Code == LuminaEvents.Error)
            {
                try { TraceLine("EV_ERROR " + e.Text); } catch (Exception) { }
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
                        HighlightCurrentSlide();
                        RefreshPreview();
                    }
                    break;
                case LuminaEvents.Error:
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
            System.Diagnostics.Trace.WriteLine("Lumina.UI: " + msg);
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
            _engine.Black(!_black);
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
        }

        /* ======================================================================
         *  ESCENARIO (builder → motor)
         * ==================================================================== */

        private void LoadScenarioFromItems(IList<ScenarioItem> items, string name)
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
            // Recordar el escenario activo: «Aplicar tema» lo reconstruye tal cual.
            _lastScenarioItems = new List<ScenarioItem>(items);
            _lastScenarioName = name;
            RefreshLiveList();
            NavigateToIndex(0);
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

        /// <summary>INSERT por lotes en transacción (BEGIN/COMMIT vía lumina_db_exec).</summary>
        private void InsertBibleRows(string version, List<object> rows, long expectedVerses)
        {
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
                        _engine.DbExec(LuminaStorage.InsertBibleVerseRequest(v, book, chapter, verse, text));
                        inserted++;
                    }
                    _engine.DbExec(LuminaStorage.RawSqlRequest("COMMIT"));
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
            }
            else
            {
                _dbOpen = false;
                Status("No se pudo abrir la BD (" + LuminaStatus.Name(st) + "): " + path);
            }
            UpdateStatusBar();
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
                if (_api != null) { try { _api.Dispose(); } catch (Exception) { } _api = null; }
                if (_engine != null) { try { _engine.Dispose(); } catch (Exception) { } _engine = null; }
            }
            base.OnFormClosing(e);
        }
    }
}
