// ============================================================================
//  Fusion-HP · Ui/Widgets/FusionButton.cs — botón MODELADO (v2.2).
//  Réplica WinForms del chip de la referencia web (.ppt-chip / .ppt-iconbtn-on
//  / .print-toolbar-btn): rectángulo redondeado 5 px, borde 1 px, estados
//  hover/pressed/foco, icono Tabler a la izquierda y pista de teclado (kbd)
//  a la derecha. Nunca un botón plano del sistema.
//  Variantes: Chip (blanco con borde) · Primary (terracota) · Subtle
//  (transparente) · Active (borde terracota + fondo suave, estado activo).
// ============================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Fusion.Studio.Ui.Widgets;

namespace Fusion.Studio.Ui.Widgets
{
    public enum FusionButtonKind { Chip, Primary, Subtle, Active }

    public class FusionButton : Button
    {
        public FusionButtonKind Kind = FusionButtonKind.Chip;
        /// <summary>Icono Tabler a la izquierda (16 px; white20 en Primary).</summary>
        public string IconName;
        /// <summary>Pista de atajo dibujada como chip kbd a la derecha (p.ej. "Espacio").</summary>
        public string Kbd;

        bool hovered, pressed;

        public FusionButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            TabStop = false;
            Height = 32;
            Cursor = Cursors.Hand;
            Font = UiTheme.Normal();
            ForeColor = UiTheme.Text;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true; Invalidate(); base.OnMouseEnter(e);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = true; Invalidate(); base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false; Invalidate(); base.OnMouseUp(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        static GraphicsPath Rounded(Rectangle r, int d)
        {
            var p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) { p.AddRectangle(r); return p; }
            d = Math.Min(d, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : UiTheme.Bg);

            bool disabled = !Enabled;
            Color bg, border, fg;
            switch (Kind)
            {
                case FusionButtonKind.Primary:
                    bg = pressed ? UiTheme.AccentDark : hovered ? UiTheme.AccentHover : UiTheme.Accent;
                    border = bg;
                    fg = Color.White;
                    break;
                case FusionButtonKind.Subtle:
                    bg = pressed ? UiTheme.Pressed : hovered ? UiTheme.Hover : Color.Transparent;
                    border = Color.Transparent;
                    fg = UiTheme.Text;
                    break;
                case FusionButtonKind.Active:
                    bg = UiTheme.AccentSoft;
                    border = UiTheme.Accent;
                    fg = UiTheme.AccentDark;
                    break;
                default:   // Chip
                    bg = pressed ? UiTheme.Pressed : hovered ? UiTheme.Hover : Color.White;
                    border = UiTheme.ChipBorder;
                    fg = UiTheme.Text;
                    break;
            }
            // v4.2.0: disabled VISIBLE — fondo y borde perceptibles (antes quedaba
            // un botón fantasma blanco sobre blanco, parecía roto).
            if (disabled) { bg = Color.FromArgb(250, 249, 248); border = UiTheme.ChipBorder; fg = UiTheme.TextDim; }

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Rounded(r, 5))
            {
                if (bg != Color.Transparent)
                    using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                using (var pen = new Pen(border)) g.DrawPath(pen, path);
                // foco visible (análogo :focus-visible de la referencia)
                if (Focused && !disabled)
                    using (var pen = new Pen(UiTheme.Accent))
                        g.DrawPath(pen, Rounded(new Rectangle(2, 2, Width - 5, Height - 5), 4));
            }

            // ---- contenido: icono + texto (+ kbd)
            int midY = Height / 2;
            int x = 11;
            if (!string.IsNullOrEmpty(IconName))
            {
                IconTint tint = Kind == FusionButtonKind.Primary && !disabled ? IconTint.White : IconTint.Ink;
                Image ic = UiIcons.Get(IconName, tint);
                if (ic != null)
                {
                    g.DrawImageUnscaled(ic, x, midY - ic.Height / 2);
                    x += ic.Width + 7;
                }
            }
            int right = Width - 11;
            SizeF kbdSize = SizeF.Empty;
            Font kbdFont = null;
            if (!string.IsNullOrEmpty(Kbd) && Kind != FusionButtonKind.Primary && !disabled)
            {
                kbdFont = UiTheme.Kbd();
                kbdSize = g.MeasureString(Kbd, kbdFont);
                right -= (int)kbdSize.Width + 12;
            }
            int textW = right - x;
            if (textW > 4)
            {
                Color textCol = disabled ? UiTheme.TextDim : fg;
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, textW, Height),
                    textCol, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            if (kbdFont != null && kbdSize.Width > 0)
            {
                Rectangle kr = new Rectangle(Width - 11 - (int)kbdSize.Width - 8,
                                             (Height - 17) / 2, (int)kbdSize.Width + 8, 17);
                using (var kp = Rounded(kr, 4))
                {
                    using (var b = new SolidBrush(UiTheme.Bg)) g.FillPath(b, kp);
                    using (var pen = new Pen(UiTheme.ChipBorder)) g.DrawPath(pen, kp);
                }
                TextRenderer.DrawText(g, Kbd, kbdFont, kr, UiTheme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix);
            }
        }

        /// <summary>Tamaño preferido a partir del contenido (icono + texto + kbd).</summary>
        public override Size GetPreferredSize(Size proposed)
        {
            int w = 24;
            if (!string.IsNullOrEmpty(IconName)) w += 20 + 7;
            Size t = TextRenderer.MeasureText(Text, Font);
            w += t.Width;
            if (!string.IsNullOrEmpty(Kbd)) w += TextRenderer.MeasureText(Kbd, UiTheme.Kbd()).Width + 20;
            return new Size(Math.Max(w, 60), Height);
        }
    }
}
