// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  StageViewForm.cs : MONITOR DE ESCENARIO (requisito spec §3.3 «Stage View /
//  Multiview — hasta tres salidas»): segunda pantalla para el equipo de músicos
//  con la letra ACTUAL grande + siguiente + reloj + próximo ítem.
//
//  WinForms + GDI+ doble búfer (net35-compatible, sin dependencias). Se llena
//  por SetState() desde el hilo de UI (eventos del motor ya encolados).
//  Escape o doble clic la cierra. F11 alterna pantalla completa.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace lumina.ui
{
    public sealed class StageViewForm : Form
    {
        private List<string> _current = new List<string>();
        private List<string> _next = new List<string>();
        private string _itemTitle = string.Empty;
        private string _nextItemTitle = string.Empty;
        private string _refLabel = string.Empty;
        private readonly System.Windows.Forms.Timer _clock;
        private readonly Font _clockFont;
        private readonly Font _titleFont;
        private readonly Font _mainFont;
        private readonly Font _nextFont;
        private readonly Font _smallFont;

        public StageViewForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(12, 14, 20);
            DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };
            MouseDoubleClick += delegate { Close(); };

            _clockFont = new Font("Consolas", 30F, FontStyle.Bold, GraphicsUnit.Point);
            _titleFont = new Font("Segoe UI", 16F, FontStyle.Bold, GraphicsUnit.Point);
            _mainFont = new Font("Segoe UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
            _nextFont = new Font("Segoe UI", 20F, FontStyle.Regular, GraphicsUnit.Point);
            _smallFont = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point);

            _clock = new System.Windows.Forms.Timer();
            _clock.Interval = 500;
            _clock.Tick += delegate { Invalidate(); };
            _clock.Start();
        }

        /// <summary>Abre a pantalla completa en la pantalla indicada.</summary>
        public void OpenOn(Screen screen)
        {
            Rectangle b = screen != null ? screen.Bounds : Screen.PrimaryScreen.Bounds;
            Bounds = b;
            WindowState = FormWindowState.Normal;
            if (!Visible) Show();
            Bounds = b;
            Activate();
        }

        /// <summary>
        /// Estado del monitor (llamar SIEMPRE desde el hilo de UI).
        /// current/next: líneas; refLabel: rótulo del bloque ("Coro"…).
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
            int w = Width, h = Height;
            Color fg = Color.FromArgb(232, 234, 240);
            Color dim = Color.FromArgb(122, 130, 150);
            Color accent = Color.FromArgb(240, 169, 59);

            // Cabecera: ítem + reloj
            TextRenderer.DrawText(g, _itemTitle.Length > 0 ? _itemTitle : "LuminaPresentation · Escenario",
                _titleFont, new Rectangle(24, 18, w - 220, 30), accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            string hhmmss = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            TextRenderer.DrawText(g, hhmmss, _clockFont, new Rectangle(w - 210, 10, 186, 46),
                dim, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            using (Pen p = new Pen(Color.FromArgb(38, 43, 54)))
                g.DrawLine(p, 24, 62, w - 24, 62);

            // Línea actual (grande, centrada verticalmente)
            int mainY = (int)(h * 0.30);
            if (_current.Count > 0)
            {
                foreach (string line in _current)
                {
                    if (_refLabel.Length > 0 && ReferenceEquals(line, _current[0]))
                    {
                        TextRenderer.DrawText(g, _refLabel, _smallFont,
                            new Rectangle(24, mainY - 26, w - 48, 20), accent,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    }
                    TextRenderer.DrawText(g, line, _mainFont,
                        new Rectangle(24, mainY, w - 48, 60), fg,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    mainY += 64;
                }
            }
            else
            {
                TextRenderer.DrawText(g, "— sin proyección —", _nextFont,
                    new Rectangle(24, mainY, w - 48, 44), dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // Siguiente (atenuado, abajo)
            if (_next.Count > 0)
            {
                int y = (int)(h * 0.72);
                TextRenderer.DrawText(g, "SIGUIENTE", _smallFont,
                    new Rectangle(24, y, w - 48, 18), dim, TextFormatFlags.Left);
                y += 22;
                int shown = 0;
                foreach (string line in _next)
                {
                    if (shown >= 2) break;   // máx. 2 líneas de adelanto
                    TextRenderer.DrawText(g, line, _nextFont,
                        new Rectangle(24, y, w - 48, 32), Color.FromArgb(150, 156, 170),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    y += 34;
                    shown++;
                }
                if (_nextItemTitle.Length > 0)
                    TextRenderer.DrawText(g, "→ " + _nextItemTitle, _smallFont,
                        new Rectangle(24, h - 34, w - 48, 20), dim,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _clock.Stop();
            _clockFont.Dispose(); _titleFont.Dispose(); _mainFont.Dispose();
            _nextFont.Dispose(); _smallFont.Dispose();
            base.OnFormClosing(e);
        }
    }
}
