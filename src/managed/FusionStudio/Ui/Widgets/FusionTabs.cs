// ============================================================================
//  Fusion-HP · Ui/Widgets/FusionTabs.cs — pestañas MODELADAS (v2.2).
//  Tira de chips con icono + título (como los tabs de la referencia web):
//  seleccionado = blanco con borde; no seleccionado = texto tenue; hover
//  suave. Contenido con separador superior. Sustituye al TabControl plano.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Fusion.Studio.Ui.Widgets
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
        // v4.2.0 (C1): chips ADAPTATIVOS — cuando la tira natural no cabe en el
        // ancho del control, cada chip se recorta con elipsis en lugar de quedar
        // FUERA del área de clic (los títulos largos dejaban pestañas inalcanzables).
        // ToolTip por chip para ver el título completo recortado.
        readonly ToolTip tips = new ToolTip();
        int minChipW = 52;

        /// <summary>Ancho mínimo de chip (icono + espacio de elipsis).</summary>
        public int MinChipWidth { get { return minChipW; } set { minChipW = Math.Max(36, value); MeasureTabs(); Invalidate(); } }

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
            // v4.1.0: contracto claro — sin página no hay pestaña fantasma
            if (page == null) throw new ArgumentNullException("page");
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

        // v4.2.0: liberar el ToolTip del control
        protected override void Dispose(bool disposing)
        {
            if (disposing && tips != null) tips.Dispose();
            base.Dispose(disposing);
        }

        void SelectPage()
        {
            for (int i = 0; i < tabs.Count; i++)
                tabs[i].Page.Visible = (i == selected);
        }

        void MeasureTabs()
        {
            // Fuente compartida de UiTheme (v2.2.1): NUNCA disponer — se cachea
            // a nivel de proceso y se usa en cada repintado.
            Font f = UiTheme.Normal();
            int gap = 6;
            int avail = Math.Max(0, Width - 4);

            // 1) anchos naturales
            int natural = 0;
            foreach (var t in tabs)
            {
                Size ts = TextRenderer.MeasureText(t.Title, f);
                t.W = Math.Max(minChipW, ts.Width + 20 + (string.IsNullOrEmpty(t.Icon) ? 0 : 24));
                natural += t.W;
            }
            natural += Math.Max(0, tabs.Count - 1) * gap;

            // 2) desbordamiento → recortar proporcionalmente hacia el mínimo
            //    (el título ya se dibuja con EndEllipsis, así que un chip más
            //    estrecho solo acorta el texto visible, nunca rompe el clic)
            if (natural > avail && tabs.Count > 0)
            {
                int needed = natural - avail;                       // px a liberar
                int excessTotal = 0;
                foreach (var t in tabs) excessTotal += Math.Max(0, t.W - minChipW);
                if (excessTotal > 0)
                {
                    foreach (var t in tabs)
                    {
                        int excess = Math.Max(0, t.W - minChipW);
                        int cut = (int)((long)excess * needed / excessTotal);
                        t.W = Math.Max(minChipW, t.W - Math.Min(excess, cut));
                    }
                }
                // pasada final: mientras no quepa, recorta 1 px del más ancho
                for (int guard = 0; guard < 4096; guard++)
                {
                    int used = 0;
                    foreach (var t in tabs) used += t.W;
                    used += Math.Max(0, tabs.Count - 1) * gap;
                    if (used <= avail) break;
                    int widest = -1, w = minChipW;
                    for (int i = 0; i < tabs.Count; i++)
                        if (tabs[i].W > w) { w = tabs[i].W; widest = i; }
                    if (widest < 0) break;                          // todo al mínimo: se sale, pero clicable
                    tabs[widest].W--;
                }
            }

            // 3) posiciones (el tooltip completo se pone en OnMouseMove)
            int px = 2;
            foreach (var t in tabs)
            {
                t.X = px;
                px += t.W + gap;
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
            MeasureTabs();   // v4.2.0 (C1): re-medir al cambiar el ancho disponible
            LayoutContent();
        }

        // v4.1.0: DPI/cambio de fuente → re-medir chips (antes quedaban descuadrados)
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            MeasureTabs();
            Invalidate();
        }

        // v4.1.0: Enabled=false — sin hover ni selección por ratón
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (!Enabled) foreach (var t in tabs) t.Hover = false;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool changed = false;
            TabInfo under = null;
            foreach (var t in tabs)
            {
                bool hov = Enabled && e.Y >= 6 && e.Y < HeaderHeight - 6 &&
                           e.X >= t.X && e.X < t.X + t.W;   // v4.2.0: sin hover en disabled
                if (t.Hover != hov) { t.Hover = hov; changed = true; }
                if (hov) under = t;
            }
            // v4.2.0: tooltip del chip bajo el cursor (títulos recortados)
            tips.SetToolTip(this, under != null ? under.Title : null);
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
            if (!Enabled) { base.OnMouseDown(e); return; }
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
                    // v4.2.0 (C8): en disabled el icono pasa a tinta tenue (35 %) —
                    // el blanco desaparecía sobre el fondo blanco del chip.
                    UiIcons.Draw(g, t.Icon, Enabled ? IconTint.Ink : IconTint.InkFaded, x, r.Y + (r.Height - 20) / 2);
                    x += 20 + 4;
                }
                Color fg = !Enabled ? UiTheme.TextDim : (on ? UiTheme.Text : UiTheme.TextDim);
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
