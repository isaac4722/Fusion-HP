// ============================================================================
//  Fusion-HP · Ui/Chrome/FusionIconButton.cs — botón de SOLO icono (v2.2).
//  Réplica de .ppt-iconbtn de la referencia: 34 px, esquinas 5 px, hover
//  #F3F2F1, y estado ACTIVO (.ppt-iconbtn-on): borde terracota + fondo
//  #FDF3F0 + icono en tinta de acento. Tooltip integrado.
// ============================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Fusion.Studio.Ui.Chrome
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
            UiIcons.DrawCentered(g, IconName, Enabled ? tint : IconTint.Ink, r);
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
