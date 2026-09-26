// ============================================================================
//  Fusion-HP · Editor/WinForms/ScenarioCanvas.cs — variante funcional del
// editor para el perfil B (net35, sin WPF) [SPEC §4.2]: lienzo 16:9 con
// elementos arrastrables, selección, propiedades y envío a la salida.
// Conserva la edición completa de contenido y estilos [SPEC §7.1.2].
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Fusion.Shared.Model;

namespace Fusion.Studio.Editor.WinForms
{
    public class ScenarioCanvas : Control
    {
        AhpProject project;
        Fusion.Studio.Services.LiveOrchestrator live;
        Scenario current;
        Element selected;
        bool dragging;
        Point dragOrigin;
        double origX, origY;

        public event Action<Scenario> RequestSendToLive;

        // mini paleta de herramientas
        Panel toolbar;
        ListBox scenarioList;
        PropertyGrid props;
        TextBox lines;
        SplitContainer split;

        public ScenarioCanvas()
        {
            // El control completo se compone aquí (se aloja en el panel Estudio)
            Dock = DockStyle.Fill;
            BackColor = ColorTranslator.FromHtml("#F3F2F1");

            split = new SplitContainer { Dock = DockStyle.Fill };
            split.Panel1MinSize = 180; split.Panel2MinSize = 240;

            // izquierda: escenarios
            scenarioList = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9f) };
            scenarioList.SelectedIndexChanged += delegate
            {
                current = scenarioList.SelectedItem as Scenario;
                selected = null;
                RefreshCanvasItems();
            };
            var scnBar = new Panel { Dock = DockStyle.Bottom, Height = 60 };
            var btnNew = new Button { Text = "+ Escenario", Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat };
            btnNew.Click += delegate
            {
                if (project == null) project = AhpProject.CreateDefault();
                var scn = new Scenario { Title = "Escenario " + (project.Scenarios.Count + 1) };
                scn.Elements.Add(new Element { Lines = { "Texto del escenario" } });
                project.Scenarios.Add(scn);
                BindProject(project, live);
            };
            var btnLive = new Button { Text = "▶ Enviar a pantalla", Dock = DockStyle.Bottom, FlatStyle = FlatStyle.Flat,
                                       BackColor = ColorTranslator.FromHtml("#C43E1C"), ForeColor = Color.White };
            btnLive.Click += delegate
            {
                if (current != null)
                {
                    var h = RequestSendToLive;
                    if (h != null) h(current);
                }
            };
            scnBar.Controls.Add(btnNew);
            scnBar.Controls.Add(btnLive);
            split.Panel1.Controls.Add(scenarioList);
            split.Panel1.Controls.Add(scnBar);

            // centro: lienzo
            var center = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            var canvasHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), AutoScroll = true };
            canvasHost.Controls.Add(new CanvasSurface(this) { Location = new Point(20, 20), Size = new Size(960, 540) });
            toolbar = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Color.White };
            string[] tools = { "+ Texto", "+ Versículo", "+ Imagen…", "+ Video…", "+ Lower", "Quitar" };
            int x = 8;
            foreach (var t in tools)
            {
                string action = t;
                var b = new Button { Text = t, Location = new Point(x, 4), AutoSize = true, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8.5f) };
                b.Click += delegate { ToolAction(action); };
                toolbar.Controls.Add(b);
                x += b.PreferredSize.Width + 8;
            }
            center.Panel1.Controls.Add(canvasHost);
            center.Panel1.Controls.Add(toolbar);

            // derecha: propiedades
            var right = new Panel { Dock = DockStyle.Fill };
            props = new PropertyGrid { Dock = DockStyle.Top, Height = 220, SelectedObject = null };
            var lbl = new Label { Text = "Líneas (una por verso):", Dock = DockStyle.Top, Height = 20, Font = new Font("Segoe UI", 8.5f) };
            lines = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical,
                                  Font = new Font("Segoe UI", 9.5f), AcceptsReturn = true };
            lines.TextChanged += delegate
            {
                if (selected == null) return;
                selected.Lines.Clear();
                foreach (var ln in lines.Text.Split('\n'))
                {
                    var t2 = ln.Trim();
                    if (t2.Length > 0) selected.Lines.Add(t2);
                }
                canvasHost.Invalidate();
            };
            right.Controls.Add(lines);
            right.Controls.Add(lbl);
            right.Controls.Add(props);
            center.Panel2.Controls.Add(right);
            split.Panel2.Controls.Add(center);

            Controls.Add(split);
        }

        public void BindProject(AhpProject project, Fusion.Studio.Services.LiveOrchestrator live)
        {
            this.project = project;
            this.live = live;
            scenarioList.Items.Clear();
            if (project != null)
                foreach (var s in project.Scenarios) scenarioList.Items.Add(s);
            if (scenarioList.Items.Count > 0) scenarioList.SelectedIndex = 0;
        }

        void ToolAction(string action)
        {
            if (current == null) { MessageBox.Show("Selecciona un escenario.", "Fusion HP"); return; }
            switch (action)
            {
                case "+ Texto":
                    current.Elements.Add(new Element { Lines = { "Nuevo texto" } });
                    break;
                case "+ Versículo":
                    current.Elements.Add(new Element { Kind = ElementKind.Verse, Reference = "Juan 3:16",
                        Lines = { "Porque de tal manera amó Dios al mundo…" } });
                    break;
                case "+ Imagen…":
                case "+ Video…":
                    using (var d = new OpenFileDialog())
                    {
                        d.Filter = action.Contains("Imagen") ? "Imágenes|*.jpg;*.png;*.gif;*.bmp" : "Videos|*.mp4;*.avi;*.wmv";
                        if (d.ShowDialog() != DialogResult.OK) return;
                        current.Elements.Add(new Element
                        {
                            Kind = action.Contains("Imagen") ? ElementKind.Image : ElementKind.Video,
                            Src = d.FileName
                        });
                    }
                    break;
                case "+ Lower":
                    current.Elements.Add(new Element { Kind = ElementKind.LowerThird, OverlayText = "Mensaje" });
                    break;
                case "Quitar":
                    if (selected != null) current.Elements.Remove(selected);
                    break;
            }
            RefreshCanvasItems();
        }

        public void SelectElement(Element el)
        {
            selected = el;
            props.SelectedObject = el != null ? new ElementProxy(el) : null;
            lines.Text = el != null ? string.Join("\n", el.Lines.ToArray()) : "";
            Invalidate();
        }

        void RefreshCanvasItems()
        {
            foreach (Control c in Controls)
                if (c is CanvasSurface) c.Invalidate();
        }

        class CanvasSurface : Control
        {
            readonly ScenarioCanvas owner;
            public CanvasSurface(ScenarioCanvas owner)
            {
                this.owner = owner;
                BackColor = ColorTranslator.FromHtml("#101820");
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var scn = owner.current;
                if (scn == null) return;
                foreach (var el in scn.Elements)
                {
                    var rect = new RectangleF((float)(el.X * Width), (float)(el.Y * Height),
                                              (float)(el.W * Width), (float)(el.H * Height));
                    using (var b = new SolidBrush(Color.FromArgb(16, Color.White)))
                        g.FillRectangle(b, rect);
                    using (var p = new Pen(el == owner.selected ? ColorTranslator.FromHtml("#C43E1C") : Color.FromArgb(120, Color.White), el == owner.selected ? 2.5f : 1f))
                        g.DrawRectangle(p, rect.X, rect.Y, rect.Width, rect.Height);
                    string preview = el.Lines.Count > 0 ? el.Lines[0] :
                                     el.Kind == ElementKind.Image ? "🖼 " + System.IO.Path.GetFileName(el.Src ?? "") :
                                     el.Kind == ElementKind.Video ? "▶ " + System.IO.Path.GetFileName(el.Src ?? "") : "Elemento";
                    using (var b = new SolidBrush(Color.White))
                        g.DrawString(preview, new Font("Segoe UI", 11), b, new RectangleF(rect.X + 6, rect.Y + 6, rect.Width - 12, rect.Height - 12));
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                var scn = owner.current;
                if (scn == null) return;
                Element hit = null;
                for (int i = scn.Elements.Count - 1; i >= 0; i--)
                {
                    var el = scn.Elements[i];
                    var rect = new RectangleF((float)(el.X * Width), (float)(el.Y * Height),
                                              (float)(el.W * Width), (float)(el.H * Height));
                    if (rect.Contains(e.Location)) { hit = el; break; }
                }
                owner.SelectElement(hit);
                if (hit != null)
                {
                    owner.dragging = true;
                    owner.dragOrigin = e.Location;
                    owner.origX = hit.X; owner.origY = hit.Y;
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (owner.dragging && owner.selected != null)
                {
                    owner.selected.X = Math.Max(0, Math.Min(0.95, owner.origX + (e.X - owner.dragOrigin.X) / (double)Width));
                    owner.selected.Y = Math.Max(0, Math.Min(0.95, owner.origY + (e.Y - owner.dragOrigin.Y) / (double)Height));
                    Invalidate();
                }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                owner.dragging = false;
                if (owner.selected != null) owner.props.SelectedObject = new ElementProxy(owner.selected);
            }
        }

        /// <summary>Adaptador de propiedades editables del elemento.</summary>
        public class ElementProxy
        {
            readonly Element el;
            public ElementProxy(Element el) { this.el = el; }

            public string Texto
            {
                get { return string.Join("\n", el.Lines.ToArray()); }
                set
                {
                    el.Lines.Clear();
                    foreach (var ln in value.Split('\n'))
                    {
                        var t = ln.Trim();
                        if (t.Length > 0) el.Lines.Add(t);
                    }
                }
            }
            public double X { get { return el.X; } set { el.X = Math.Max(0, Math.Min(0.95, value)); } }
            public double Y { get { return el.Y; } set { el.Y = Math.Max(0, Math.Min(0.95, value)); } }
            public double Ancho { get { return el.W; } set { el.W = Math.Max(0.05, Math.Min(1, value)); } }
            public double Alto { get { return el.H; } set { el.H = Math.Max(0.05, Math.Min(1, value)); } }
            public string Cita { get { return el.Reference ?? ""; } set { el.Reference = value; } }
            [System.ComponentModel.Browsable(false)]
            public Element Element { get { return el; } }
        }
    }
}
