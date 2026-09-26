// ============================================================================
//  Fusion-HP · Ui/Widgets/FusionInput.cs — campo de texto MODELADO (v2.2).
//  Borde 1 px #C8C6C4, esquinas 5 px, fondo blanco, foco terracota (análogo
//  .ppt-input). Variantes: FusionInput (campo) y FusionSearchBox (con lupa y
//  placeholder tipo cue banner nativo). El TextBox interno sin borde se
//  hospeda dentro del control pintado.
// ============================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Fusion.Studio.Ui.Widgets
{
    public class FusionInput : Control
    {
        protected readonly TextBox inner;
        bool focused;

        public event EventHandler InnerTextChanged;

        public FusionInput()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.White;

            // v4.0.1 (NRE al arrancar, cs.domain 2026-09-25): el TextBox interno
            // se crea ANTES de tocar Bounds. Fijar Height dispara SetBounds →
            // UpdateBounds → OnSizeChanged → OnResize, y OnResize es virtual:
            // desde el ctor de la base ya se despacha al OnResize de la clase
            // derivada (FusionSearchBox) y al de esta, que leían «inner» aún
            // nulo. Orden correcto: campos → eventos → Bounds.
            inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = UiTheme.Normal(),
                ForeColor = UiTheme.Text,
                Multiline = false
            };
            inner.GotFocus += delegate { focused = true; Invalidate(); };
            inner.LostFocus += delegate { focused = false; Invalidate(); };
            inner.TextChanged += delegate
            {
                var h = InnerTextChanged;
                if (h != null) h(this, EventArgs.Empty);
            };
            Controls.Add(inner);

            Height = 30;
            Cursor = Cursors.IBeam;
            Click += delegate { inner.Focus(); };
        }

        public override string Text
        {
            get { return inner.Text; }
            set { inner.Text = value; }
        }

        // v4.1.0: Enabled=false VISIBLE — borde tenue, fondo gris y TextBox
        // interno deshabilitado en sincronía (antes el contenedor no cambiaba).
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (inner != null)
            {
                inner.Enabled = Enabled;
                inner.ForeColor = Enabled ? UiTheme.Text : UiTheme.TextDim;
            }
            Invalidate();
        }

        public bool ReadOnly
        {
            get { return inner.ReadOnly; }
            set { inner.ReadOnly = value; }
        }

        public bool UseSystemPasswordChar
        {
            get { return inner.UseSystemPasswordChar; }
            set { inner.UseSystemPasswordChar = value; }
        }

        public TextBox Inner { get { return inner; } }

        public void SelectAll() { inner.SelectAll(); }
        public new void Focus() { inner.Focus(); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (inner == null) return; // v4.0.1: guardia — OnResize puede despacharse desde el ctor
            inner.Location = new Point(10, (Height - inner.PreferredHeight) / 2);
            inner.Width = Math.Max(0, Width - 18);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
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
                Color back = Enabled ? Color.White : UiTheme.Bg;
                Color border = !Enabled ? UiTheme.ChipBorder
                             : focused ? UiTheme.Accent
                             : UiTheme.InputBorder;
                using (var b = new SolidBrush(back)) g.FillPath(b, p);
                using (var pen = new Pen(border)) g.DrawPath(pen, p);
            }
        }
    }

    /// <summary>Buscador modelado: lupa Tabler + placeholder (cue banner).</summary>
    public class FusionSearchBox : FusionInput
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        const int EM_SETCUEBANNER = 0x1501;

        /// <summary>Icono Tabler a la izquierda (por defecto la lupa).</summary>
        public string LeftIcon = "search";

        string cue;

        public FusionSearchBox()
        {
            inner.Width = Math.Max(0, Width - 34);
            // v2.2.1: el cue banner se pierde cuando WinForms recrea el handle
            // (reparentado/dpi) — se reaplica en cada HandleCreated.
            inner.HandleCreated += delegate { ApplyCue(); };
            Placeholder = "Buscar";
        }

        public string Placeholder
        {
            set { cue = value; ApplyCue(); }
        }

        void ApplyCue()
        {
            if (!inner.IsHandleCreated) return;   // pendiente para HandleCreated
            try { SendMessage(inner.Handle, EM_SETCUEBANNER, (IntPtr)1, cue ?? ""); }
            catch { }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (inner == null) return; // v4.0.1: guardia — OnResize puede despacharse desde el ctor
            inner.Location = new Point(32, (Height - inner.PreferredHeight) / 2);
            inner.Width = Math.Max(0, Width - 40);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // v4.1.0: icono tenue cuando el control está deshabilitado
            UiIcons.Draw(e.Graphics, string.IsNullOrEmpty(LeftIcon) ? "search" : LeftIcon,
                Enabled ? IconTint.Ink : IconTint.White, 10, (Height - 20) / 2);
        }
    }
}
