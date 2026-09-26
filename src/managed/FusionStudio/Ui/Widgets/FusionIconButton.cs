// ============================================================================
//  Fusion-HP · Ui/Widgets/FusionIconButton.cs — botón de SOLO icono (v2.2).
//  Réplica de .ppt-iconbtn de la referencia: 34 px, esquinas 5 px, hover
//  #F3F2F1, y estado ACTIVO (.ppt-iconbtn-on): borde terracota + fondo
//  #FDF3F0 + icono en tinta de acento. Tooltip integrado.
// ============================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Fusion.Studio.Ui.Widgets
{
    public class FusionIconButton : Control
    {
        /// <summary>Icono Tabler (20 px; tinta ink, o accent si Active).</summary>
        public string IconName;
        /// <summary>Estado activo (.ppt-iconbtn-on): borde + fondo + acento.</summary>
        public bool Active;

        string toolTipText;
        /// <summary>Texto de ayuda flotante (se aplica al asignarlo).</summary>
        public string ToolTipText
        {
            get { return toolTipText; }
            set { toolTipText = value; tips.SetToolTip(this, value ?? ""); }
        }

        static readonly ToolTip tips = new ToolTip();
        bool hovered, pressed;

        public FusionIconButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(34, 34);
            Cursor = Cursors.Hand;
            TabStop = false;
            // v2.2.1: el ToolTip compartido es estático — al disponer el botón
            // se desasocia para no retener la referencia (GC correcto).
            Disposed += delegate { try { tips.SetToolTip(this, null); } catch { } };
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
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : UiTheme.Bg);

            Color bg = Color.Transparent, border = Color.Transparent;
            if (!Enabled) { bg = Color.Transparent; border = UiTheme.Border; }
            else if (Active) { bg = UiTheme.AccentSoft; border = UiTheme.Accent; }
            else if (pressed) bg = UiTheme.Pressed;
            else if (hovered) bg = UiTheme.Hover;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var p = new GraphicsPath())
            {
                int d = Math.Min(5, Math.Min(r.Width, r.Height));
                if (r.Width > 0 && r.Height > 0)
                {
                    p.AddArc(r.X, r.Y, d, d, 180, 90);
                    p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                    p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                    p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                    p.CloseFigure();
                }
                if (bg != Color.Transparent)
                    using (var b = new SolidBrush(bg)) g.FillPath(b, p);
                if (border != Color.Transparent)
                    using (var pen = new Pen(border)) g.DrawPath(pen, p);
            }

            IconTint tint = Active && Enabled ? IconTint.Accent : IconTint.Ink;
            DrawIcon(g, tint, r, Enabled ? 1f : 0.35f);
        }

        /// <summary>Dibuja el icono con opacidad (v4.1.0: disabled = 35 %).</summary>
        void DrawIcon(Graphics g, IconTint tint, Rectangle r, float opacity)
        {
            var img = UiIcons.Get(IconName, tint);
            if (img == null) return;
            int x = r.X + (r.Width - img.Width) / 2;
            int y = r.Y + (r.Height - img.Height) / 2;
            if (opacity >= 1f)
            {
                g.DrawImage(img, x, y, img.Width, img.Height);
                return;
            }
            var cm = new System.Drawing.Imaging.ColorMatrix
            {
                Matrix33 = opacity   // canal alfa escalado
            };
            using (var ia = new System.Drawing.Imaging.ImageAttributes())
            {
                ia.SetColorMatrix(cm, System.Drawing.Imaging.ColorMatrixFlag.Default,
                                  System.Drawing.Imaging.ColorAdjustType.Bitmap);
                var dst = new Rectangle(x, y, img.Width, img.Height);
                g.DrawImage(img, dst, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
        }

        public void SetActive(bool on)
        {
            if (Active == on) return;
            Active = on;
            Invalidate();
        }
    }
}
