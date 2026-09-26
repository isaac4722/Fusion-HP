// ============================================================================
//  Fusion-HP · FusionStudio/Ui/SorterForm.cs — CLASIFICADOR (vista de la
//  referencia web): cuadrícula con TODAS las diapositivas del proyecto y sus
//  miniaturas; clic selecciona y proyecta, doble clic vuelve al editor.
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using Fusion.Shared;
using Fusion.Shared.Model;
using Fusion.Studio.Services;
using Fusion.Studio.Ui.Widgets;

namespace Fusion.Studio.Ui
{
    public class SorterForm : Form
    {
        readonly MainForm owner;
        readonly LiveOrchestrator live;
        FlowLayoutPanel grid;
        Timer timer;

        public SorterForm(MainForm main, LiveOrchestrator orchestrator)
        {
            owner = main;
            live = orchestrator;
            Text = "Clasificador — todas las diapositivas";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(980, 640);
            MinimumSize = new Size(640, 420);
            Font = UiTheme.Normal();
            BackColor = UiTheme.Bg;

            var top = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = UiTheme.Panel };
            var lbl = new Label { Text = "Clasificador", Font = UiTheme.NormalBold(), ForeColor = UiTheme.Text,
                                  Location = new Point(12, 12), AutoSize = true };
            top.Controls.Add(lbl);
            var back = new FusionButton { Text = "Volver a Normal", IconName = "heading", Kind = FusionButtonKind.Chip,
                                          Location = new Point(160, 8), Size = new Size(160, 32) };
            back.Click += delegate { Close(); };
            top.Controls.Add(back);
            Controls.Add(top);

            grid = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, BackColor = UiTheme.Bg, Padding = new Padding(12),
                AutoScroll = true
            };
            Controls.Add(grid);

            timer = new Timer { Interval = 1200 };
            timer.Tick += delegate { MarkActive(); };
            timer.Start();
            BuildGrid();
        }

        void BuildGrid()
        {
            UiTheme.DisposeChildren(grid);
            if (live.Project == null) return;
            foreach (var scn in live.Project.Scenarios)
            {
                var header = new Label
                {
                    Text = scn.Title.ToUpperInvariant() + "  ·  " + scn.Elements.Count + " diapositivas",
                    Font = UiTheme.SmallBold(), ForeColor = UiTheme.TextDim,
                    Width = grid.ClientSize.Width - 40, Height = 22, Margin = new Padding(2, 10, 0, 2)
                };
                grid.Controls.Add(header);
                foreach (var el in scn.Elements)
                {
                    var card = MakeCard(scn, el);
                    grid.Controls.Add(card);
                }
            }
        }

        Control MakeCard(Scenario scn, Element el)
        {
            var card = new Panel
            {
                Size = new Size(190, 132), Margin = new Padding(6), BackColor = UiTheme.Panel,
                Cursor = Cursors.Hand, Tag = scn.Title
            };
            card.Paint += delegate(object s, PaintEventArgs e)
            {
                bool active = IsActive(scn, el);
                using (var pen = new Pen(active ? UiTheme.Accent : UiTheme.ChipBorder, active ? 2f : 1f))
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };
            var prev = new SlidePreview { Dock = DockStyle.Top, Height = 98 };
            try
            {
                var slide = Fusion.Shared.Model.ResolvedSlide.Resolve(el, scn, live.Project,
                    System.IO.Path.GetDirectoryName(owner.Settings.LastProjectPath ?? ""));
                if (slide != null) prev.ShowSlide(slide, 0);
            }
            catch { /* miniatura: nunca tumba el clasificador */ }
            card.Controls.Add(prev);
            var cap = new Label
            {
                Dock = DockStyle.Fill, Font = UiTheme.Small(), ForeColor = UiTheme.Text,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 2, 0),
                Text = ElementCaption(scn, el)
            };
            card.Controls.Add(cap);
            card.Click += delegate { SelectCard(scn, el); };
            prev.Click += delegate { SelectCard(scn, el); };
            cap.Click += delegate { SelectCard(scn, el); };
            card.DoubleClick += delegate { SelectCard(scn, el); Close(); };
            return card;
        }

        static string ElementCaption(Scenario scn, Element el)
        {
            // Element no lleva título propio: primera línea del contenido o título de la sección
            if (el.Lines.Count > 0 && !string.IsNullOrEmpty(el.Lines[0])) return el.Lines[0];
            if (!string.IsNullOrEmpty(el.Reference)) return el.Reference;
            return scn.Title;
        }

        bool IsActive(Scenario scn, Element el)
        {
            return live.State.ScenarioIndex >= 0 &&
                   live.Project != null &&
                   live.State.ScenarioIndex < live.Project.Scenarios.Count &&
                   ReferenceEquals(live.Project.Scenarios[live.State.ScenarioIndex], scn) &&
                   live.CurrentElement == el;
        }

        void SelectCard(Scenario scn, Element el)
        {
            if (live.Project == null) return;
            int si = live.Project.Scenarios.IndexOf(scn);
            int ei = scn.Elements.IndexOf(el);
            if (si >= 0 && ei >= 0) live.Select(si, ei, 0);
            foreach (Control c in grid.Controls)
                if (c is Panel) c.Invalidate();
        }

        void MarkActive()
        {
            if (IsDisposed) return;
            foreach (Control c in grid.Controls)
                if (c is Panel) c.Invalidate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (timer != null) timer.Stop();
            base.OnFormClosed(e);
        }
    }
}
