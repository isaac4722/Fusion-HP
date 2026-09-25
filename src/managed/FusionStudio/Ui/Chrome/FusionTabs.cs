// ============================================================================
//  Fusion-HP · Ui/Chrome/FusionTabs.cs — pestañas MODELADAS (v2.2).
//  Tira de chips con icono + título (como los tabs de la referencia web):
//  seleccionado = blanco con borde; no seleccionado = texto tenue; hover
//  suave. Contenido con separador superior. Sustituye al TabControl plano.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Fusion.Studio.Ui.Chrome
{
    public class FusionTabs : Control
    {
        class TabInfo
        {
            public string Title;
            public string Icon;
            public Control Page;
            public int X, W;
            public bool Hover;
        }

        readonly List<TabInfo> tabs = new List<TabInfo>();
        readonly Panel content;
        int selected = -1;

        public event EventHandler SelectedIndexChanged;

        public FusionTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            content = new Panel { BackColor = Color.White, Dock = DockStyle.None };
            Controls.Add(content);
            BackColor = Color.White;
        }

        public const int HeaderHeight = 40;

        public int Add(string title, string iconName, Control page)
        {
            page.Visible = false;
            page.Dock = DockStyle.Fill;
            content.Controls.Add(page);
            var t = new TabInfo { Title = title, Icon = iconName, Page = page };
            tabs.Add(t);
            if (selected < 0) selected = 0;
            MeasureTabs();
            if (tabs.Count == 1) SelectPage();
            Invalidate();
            LayoutContent();
            return tabs.Count - 1;
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value < 0 || value >= tabs.Count || value == selected) return;
                selected = value;
                SelectPage();
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        void SelectPage()
        {
            for (int i = 0; i < tabs.Count; i++)
                tabs[i].Page.Visible = (i == selected);
        }

        void MeasureTabs()
        {
            int x = 2;
            using (var f = UiTheme.Normal())
            {
                foreach (var t in tabs)
                {
                    Size ts = TextRenderer.MeasureText(t.Title, f);
                    t.W = ts.Width + 20 + (string.IsNullOrEmpty(t.Icon) ? 0 : 24);
                    t.X = x;
                    x += t.W + 6;
                }
            }
        }

        void LayoutContent()
        {
            content.SetBounds(0, HeaderHeight, Width, Math.Max(0, Height - HeaderHeight));
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutContent();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool changed = false;
            foreach (var t in tabs)
            {
                bool hov = e.Y >= 6 && e.Y < HeaderHeight - 6 &&
                           e.X >= t.X && e.X < t.X + t.W;
                if (t.Hover != hov) { t.Hover = hov; changed = true; }
            }
            if (changed) Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            foreach (var t in tabs) t.Hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                if (e.Y >= 6 && e.Y < HeaderHeight - 6 && e.X >= t.X && e.X < t.X + t.W)
                {
                    SelectedIndex = i;
                    break;
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            using (var pen = new Pen(UiTheme.Border))
                g.DrawLine(pen, 0, HeaderHeight - 1, Width, HeaderHeight - 1);

            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                bool on = (i == selected);
                var r = new Rectangle(t.X, 6, t.W, HeaderHeight - 13);
                if (on)
                {
                    using (var p = RoundRect(r, 5))
                    {
                        using (var b = new SolidBrush(Color.White)) g.FillPath(b, p);
                        using (var pen = new Pen(UiTheme.ChipBorder)) g.DrawPath(pen, p);
                    }
                }
                else if (t.Hover)
                {
                    using (var p = RoundRect(r, 5))
                        using (var b = new SolidBrush(UiTheme.Hover)) g.FillPath(b, p);
                }

                int x = r.X + 10;
                if (!string.IsNullOrEmpty(t.Icon))
                {
                    UiIcons.Draw(g, t.Icon, IconTint.Ink, x, r.Y + (r.Height - 20) / 2);
                    x += 20 + 4;
                }
                Color fg = on ? UiTheme.Text : UiTheme.TextDim;
                TextRenderer.DrawText(g, t.Title, UiTheme.Normal(),
                    new Rectangle(x, r.Y, r.Right - x, r.Height), fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        static GraphicsPath RoundRect(Rectangle r, int d)
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
    }
}
