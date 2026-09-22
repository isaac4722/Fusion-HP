// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  LowerThirdsOverlay.cs : SUPERPOSICIÓN LOWER THIRD (requisito spec §3.2:
// «zócalo inferior semitransparente para títulos o textos de transmisión»).
//
//  Ventana transparente sin activación sobre TODAS las pantallas del proyector:
//    * TransparencyKey para el fondo + banda semitransparente pintada con GDI+
//      (alpha real en la banda: el color clave solo se usa en el resto).
//    * WS_EX_NOACTIVATE + TopMost: nunca roba el foco al operador.
//    * Auto-ocultado tras N segundos (configurable) con fundido de salida.
//  Se usa para: avisos del mando remoto, acción «show_text» de los activadores
//  y como zócalo manual desde En Vivo.
// ============================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace lumina.ui
{
    public sealed class LowerThirdsOverlay : Form
    {
        private const int WsExNoActivate = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private readonly System.Windows.Forms.Timer _life;
        private readonly System.Windows.Forms.Timer _fade;
        private string _line1 = string.Empty;
        private string _line2 = string.Empty;
        private Color _accent = Color.FromArgb(240, 169, 59);
        private double _opacity = 0.0;          // fundido de entrada
        private int _fadeDir = 1;

        public LowerThirdsOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(255, 0, 255);    // clave de transparencia (magenta)
            TransparencyKey = Color.FromArgb(255, 0, 255);
            DoubleBuffered = true;

            _life = new System.Windows.Forms.Timer();
            _life.Tick += delegate
            {
                _life.Stop();
                _fadeDir = -1;                   // fundido de salida
                _fade.Start();
            };

            _fade = new System.Windows.Forms.Timer();
            _fade.Interval = 40;
            _fade.Tick += delegate
            {
                _opacity += 0.08 * _fadeDir;
                if (_opacity >= 1.0) { _opacity = 1.0; _fade.Stop(); }
                if (_opacity <= 0.0)
                {
                    _opacity = 0.0;
                    _fade.Stop();
                    if (Visible) Hide();
                }
                Invalidate();
            };

            // Nunca robar el foco (WS_EX_NOACTIVATE)
            HandleCreated += delegate
            {
                try
                {
                    int ex = GetWindowLong(Handle, -20);
                    SetWindowLong(Handle, -20, ex | WsExNoActivate);
                }
                catch (Exception) { }
            };
        }

        /// <summary>Muestra el zócalo sobre la pantalla indicada.</summary>
        public void Show(string line1, string line2, Rectangle screenBounds, int seconds)
        {
            _line1 = line1 ?? string.Empty;
            _line2 = line2 ?? string.Empty;
            Bounds = screenBounds;
            if (!Visible) Visible = true;
            WindowState = FormWindowState.Normal;
            Bounds = screenBounds;
            _opacity = 0.0;
            _fadeDir = 1;
            _fade.Stop();
            _fade.Start();
            _life.Stop();
            if (seconds > 0)
            {
                _life.Interval = seconds * 1000;
                _life.Start();
            }
            Invalidate();
        }

        public void HideNow()
        {
            _life.Stop();
            _fade.Stop();
            _opacity = 0.0;
            if (Visible) Hide();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_opacity <= 0.01) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = Width, h = Height;
            // Banda: ancho 78%, alto según contenido, pegada abajo con margen 7%
            int bandW = (int)(w * 0.78);
            int bandH = _line2.Length > 0 ? (int)(h * 0.155) : (int)(h * 0.105);
            int bandX = (w - bandW) / 2;
            int bandY = h - bandH - (int)(h * 0.07);

            int alpha = (int)(225 * _opacity);
            using (GraphicsPath path = RoundedRect(new Rectangle(bandX, bandY, bandW, bandH), 10))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(alpha, 10, 14, 22)))
            {
                g.FillPath(bg, path);
                using (Pen pen = new Pen(Color.FromArgb((int)(70 * _opacity), 240, 169, 59), 1.4F))
                    g.DrawPath(pen, path);
            }
            // Barra de acento a la izquierda
            using (SolidBrush bar = new SolidBrush(Color.FromArgb((int)(255 * _opacity), _accent)))
                g.FillRectangle(bar, bandX + 14, bandY + 12, 6, bandH - 24);

            int textAlpha = (int)(255 * _opacity);
            Color fg = Color.FromArgb(textAlpha, 240, 242, 248);
            Color fg2 = Color.FromArgb((int)(210 * _opacity), 178, 184, 198);

            if (_line2.Length > 0)
            {
                TextRenderer.DrawText(g, _line1, new Font("Segoe UI", 15F, FontStyle.Bold, GraphicsUnit.Point),
                    new Rectangle(bandX + 34, bandY + 10, bandW - 48, bandH / 2 - 12), fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, _line2, new Font("Segoe UI", 11.5F, FontStyle.Regular, GraphicsUnit.Point),
                    new Rectangle(bandX + 34, bandY + bandH / 2, bandW - 48, bandH / 2 - 12), fg2,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            else
            {
                TextRenderer.DrawText(g, _line1, new Font("Segoe UI", 15F, FontStyle.Bold, GraphicsUnit.Point),
                    new Rectangle(bandX + 34, bandY, bandW - 48, bandH), fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _life.Stop();
            _fade.Stop();
            base.OnFormClosing(e);
        }
    }
}
