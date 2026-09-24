// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  DirectorForm.cs (v5.1.0 «FUNDAMENTO») : TERCERA SALIDA del spec §3.3
//  («Gestión Multi-Pantalla / Multiview — hasta tres salidas»):
//    1. Pantalla pública       → proyector nativo (LuminaCore)
//    2. Monitor de escenario   → StageViewForm (músicos)
//    3. Pantalla del DIRECTOR  → este formulario: texto completo del ítem
//       actual y del siguiente, próximo ítem del culto, reloj + CRONÓMETRO
//       (iniciar/pausar/reiniciar con doble clic o teclas) y un panel de
//       NOTAS editable (avisos internos del servicio — no se proyectan).
//
//  WinForms + GDI+ doble búfer (net35-compatible, sin dependencias). Se llena
//  por SetState() desde el hilo de UI. Escape la cierra; F11 alterna pantalla
//  completa; Espacio inicia/pausa el cronómetro; R lo reinicia.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace lumina.ui
{
    public sealed class DirectorForm : Form
    {
        private List<string> _current = new List<string>();
        private List<string> _next = new List<string>();
        private string _itemTitle = string.Empty;
        private string _nextItemTitle = string.Empty;
        private string _refLabel = string.Empty;
        private readonly System.Windows.Forms.Timer _clock;

        // Cronómetro del servicio (transcurrido desde el arranque del ítem).
        private DateTime _chronoStart;
        private TimeSpan _chronoElapsed = TimeSpan.Zero;
        private bool _chronoRunning;

        // Panel de notas del director (avisos internos del servicio — no se
        // proyectan). v6.0.0: las notas PERSISTEN en el ítem del culto: al
        // cambiar de ítem se cargan las suyas y cada edición se notifica a
        // MainWindow (evento NotesEdited) para guardarlas en el ScenarioItem.
        private TextBox _notes;
        private bool _loadingNotes;
        /// <summary>v6.0.0: dispara en cada edición del panel (texto nuevo).</summary>
        public event Action<string> NotesEdited;

        private readonly Font _clockFont;
        private readonly Font _titleFont;
        private readonly Font _mainFont;
        private readonly Font _nextFont;
        private readonly Font _smallFont;
        private readonly Font _chronoFont;

        public DirectorForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(16, 18, 26);
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.F11) ToggleFullscreen();
                else if (e.KeyCode == Keys.Space && !NotesFocused) { ToggleChrono(); e.Handled = true; }
                else if (e.KeyCode == Keys.R && !NotesFocused) ResetChrono();
            };
            MouseDoubleClick += delegate
            {
                if (!NotesFocused) ToggleChrono();
            };

            _clockFont = new Font("Consolas", 26F, FontStyle.Bold, GraphicsUnit.Point);
            _chronoFont = new Font("Consolas", 42F, FontStyle.Bold, GraphicsUnit.Point);
            _titleFont = new Font("Segoe UI", 15F, FontStyle.Bold, GraphicsUnit.Point);
            _mainFont = new Font("Segoe UI", 24F, FontStyle.Bold, GraphicsUnit.Point);
            _nextFont = new Font("Segoe UI", 16F, FontStyle.Regular, GraphicsUnit.Point);
            _smallFont = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point);

            // Panel de notas (abajo): editable, sin salir de la pantalla.
            _notes = new TextBox();
            _notes.Multiline = true;
            _notes.Dock = DockStyle.Bottom;
            _notes.Height = 110;
            _notes.BorderStyle = BorderStyle.FixedSingle;
            _notes.BackColor = Color.FromArgb(24, 27, 38);
            _notes.ForeColor = Color.FromArgb(210, 214, 226);
            _notes.Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point);
            _notes.TextChanged += delegate(object sender, EventArgs e)
            {
                if (_loadingNotes) return;
                Action<string> h = NotesEdited;
                if (h != null) h(_notes.Text);
            };
            _notes.KeyPress += delegate(object s, KeyPressEventArgs e)
            {
                // Evita que Escape cierre la ventana mientras se escriben notas.
                if (e.KeyChar == (char)27) e.Handled = true;
            };
            Controls.Add(_notes);

            _clock = new System.Windows.Forms.Timer();
            _clock.Interval = 250;
            _clock.Tick += delegate { Invalidate(); };
            _clock.Start();
        }

        private bool NotesFocused
        {
            get { return ActiveControl == _notes; }
        }

        private void ToggleFullscreen()
        {
            if (FormBorderStyle == FormBorderStyle.None)
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                Bounds = _savedBounds;
            }
        }

        private Rectangle _savedBounds;

        /// <summary>Abre a pantalla completa en la pantalla indicada.</summary>
        public void OpenOn(Screen screen)
        {
            Rectangle b = screen != null ? screen.Bounds : Screen.PrimaryScreen.Bounds;
            _savedBounds = b;
            Bounds = b;
            WindowState = FormWindowState.Normal;
            if (!Visible) Show();
            Bounds = b;
            Activate();
        }

        private void ToggleChrono()
        {
            if (_chronoRunning)
            {
                _chronoElapsed += DateTime.Now - _chronoStart;
                _chronoRunning = false;
            }
            else
            {
                _chronoStart = DateTime.Now;
                _chronoRunning = true;
            }
        }

        private void ResetChrono()
        {
            _chronoElapsed = TimeSpan.Zero;
            _chronoStart = DateTime.Now;
        }

        /// <summary>
        /// v6.0.0: carga las notas del ítem ACTUAL (llamar solo al CAMBIAR de
        /// ítem — no en cada slide, o pisaría lo que el director escribe).
        /// </summary>
        public void LoadNotes(string notes)
        {
            _loadingNotes = true;
            try { _notes.Text = notes ?? string.Empty; }
            finally { _loadingNotes = false; }
        }

        /// <summary>
        /// Estado del director (llamar SIEMPRE desde el hilo de UI). Recibe TODAS
        /// las líneas del ítem actual (no solo la slide visible) y las del próximo.
        /// </summary>
        public void SetState(string itemTitle, string nextItemTitle, string refLabel,
                             IList<string> currentLines, IList<string> nextLines)
        {
            _itemTitle = itemTitle ?? string.Empty;
            _nextItemTitle = nextItemTitle ?? string.Empty;
            _refLabel = refLabel ?? string.Empty;
            _current = currentLines != null ? new List<string>(currentLines) : new List<string>();
            _next = nextLines != null ? new List<string>(nextLines) : new List<string>();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = Width, h = Height - _notes.Height;
            Color fg = Color.FromArgb(232, 234, 240);
            Color dim = Color.FromArgb(128, 136, 156);
            Color accent = Color.FromArgb(84, 200, 124);   // verde «director»

            // Cabecera: ítem actual + reloj de pared.
            TextRenderer.DrawText(g,
                _itemTitle.Length > 0 ? _itemTitle : "LuminaPresentation · Director",
                _titleFont, new Rectangle(24, 16, w - 460, 28), accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            string hhmmss = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            TextRenderer.DrawText(g, hhmmss, _clockFont, new Rectangle(w - 230, 8, 206, 42),
                dim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            // Cronómetro (columna izquierda, siempre visible).
            TimeSpan el = _chronoElapsed;
            if (_chronoRunning) el += DateTime.Now - _chronoStart;
            string chrono = string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}",
                (int)el.TotalHours, el.Minutes, el.Seconds);
            TextRenderer.DrawText(g, chrono, _chronoFont, new Rectangle(24, h / 2 - 60, 300, 74),
                _chronoRunning ? accent : dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "Espacio: iniciar/pausar · R: reiniciar", _smallFont,
                new Rectangle(24, h / 2 + 18, 300, 18), dim, TextFormatFlags.Left);

            using (Pen p = new Pen(Color.FromArgb(42, 47, 60)))
                g.DrawLine(p, 360, 60, 360, h - 16);

            // Texto COMPLETO del ítem actual (columna derecha).
            int y = 64;
            if (_refLabel.Length > 0)
            {
                TextRenderer.DrawText(g, _refLabel, _smallFont,
                    new Rectangle(380, y, w - 404, 20), accent, TextFormatFlags.Left);
                y += 24;
            }
            if (_current.Count > 0)
            {
                for (int i = 0; i < _current.Count && y < h - 120; i++)
                {
                    TextRenderer.DrawText(g, _current[i], _mainFont,
                        new Rectangle(380, y, w - 404, 40), fg,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    y += 42;
                }
            }
            else
            {
                TextRenderer.DrawText(g, "— sin proyección —", _nextFont,
                    new Rectangle(380, y, w - 404, 30), dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                y += 34;
            }

            // Próximo ítem + primeras líneas.
            if (_nextItemTitle.Length > 0)
            {
                int ny = h - 118;
                TextRenderer.DrawText(g, "PRÓXIMO ÍTEM — " + _nextItemTitle, _smallFont,
                    new Rectangle(380, ny, w - 404, 20), dim, TextFormatFlags.Left);
                ny += 22;
                for (int i = 0; i < _next.Count && i < 2; i++)
                {
                    TextRenderer.DrawText(g, _next[i], _nextFont,
                        new Rectangle(380, ny, w - 404, 26), Color.FromArgb(160, 166, 180),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    ny += 28;
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _clock.Stop();
            _clockFont.Dispose(); _titleFont.Dispose(); _mainFont.Dispose();
            _nextFont.Dispose(); _smallFont.Dispose(); _chronoFont.Dispose();
            if (_notes != null) _notes.Dispose();
            base.OnFormClosing(e);
        }
    }
}
