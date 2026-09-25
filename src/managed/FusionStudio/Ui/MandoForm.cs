// ============================================================================
//  Fusion-HP · FusionStudio/Ui/MandoForm.cs — MANDO (réplica de RemoteView de
//  la referencia web): vista del control remoto dentro del programa, con
//  marco de móvil, vista previa, botón primario Siguiente, pausas B/C/L y
//  lista del programa. La versión real para el teléfono sigue siendo /remote
//  (API HTTP + token): esta ventana es la réplica de la GUI web en C#.
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using Fusion.Shared;
using Fusion.Studio.Services;
using Fusion.Studio.Ui.Widgets;

namespace Fusion.Studio.Ui
{
    public class MandoForm : Form
    {
        readonly LiveOrchestrator live;
        SlidePreview preview;
        Label lblItem, lblLine;
        ListBox program;
        Timer timer;
        FusionButton btnPrev, btnNext, btnB, btnC, btnL;

        public MandoForm(MainForm owner, LiveOrchestrator orchestrator)
        {
            live = orchestrator;
            Text = "Mando — control remoto";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(430, 780);
            MinimumSize = new Size(360, 560);
            Font = UiTheme.Normal();
            BackColor = Color.FromArgb(0xE6, 0xE6, 0xE6);
            ShowInTaskbar = false;

            // marco del móvil
            var phone = new Panel { BackColor = UiTheme.Panel, Dock = DockStyle.Fill, Padding = new Padding(8) };
            phone.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(UiTheme.ChipBorder))
                    e.Graphics.DrawRectangle(pen, 0, 0, phone.Width - 1, phone.Height - 1);
            };
            Controls.Add(phone);

            // cabecera
            var head = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = UiTheme.Panel };
            var back = new FusionIconButton { IconName = "chevron-left", Location = new Point(6, 10) };
            new ToolTip().SetToolTip(back, "Volver a la consola (Esc)");
            back.Click += delegate { Close(); };
            head.Controls.Add(back);
            var ht = new Label { Text = "Mando", Font = UiTheme.NormalBold(), ForeColor = UiTheme.Text,
                                 Location = new Point(150, 8), AutoSize = true };
            var hs = new Label { Text = "Sincronizado con este equipo", Font = UiTheme.Small(),
                                 ForeColor = UiTheme.TextDim, Location = new Point(110, 28), AutoSize = true };
            head.Controls.Add(ht); head.Controls.Add(hs);
            var sep = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.Border };
            head.Controls.Add(sep);
            phone.Controls.Add(head);

            // vista previa 16:9
            var prevPanel = new Panel { Dock = DockStyle.Top, Height = 210, BackColor = Color.Black,
                                        Padding = new Padding(1) };
            preview = new SlidePreview { Dock = DockStyle.Fill };
            prevPanel.Controls.Add(preview);
            phone.Controls.Add(prevPanel);

            // elemento + línea actual
            var cur = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = UiTheme.Panel, Padding = new Padding(6, 4, 6, 0) };
            lblItem = new Label { Dock = DockStyle.Top, Height = 16, Font = UiTheme.Small(),
                                  ForeColor = UiTheme.TextDim, TextAlign = ContentAlignment.MiddleLeft };
            lblLine = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                                  ForeColor = UiTheme.Text, TextAlign = ContentAlignment.MiddleLeft };
            cur.Controls.Add(lblLine); cur.Controls.Add(lblItem);
            phone.Controls.Add(cur);

            // Anterior / Siguiente
            var nav = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(8, 6, 8, 6) };
            btnPrev = new FusionButton { Text = "Anterior", IconName = "player-skip-back",
                                         Kind = FusionButtonKind.Chip, Dock = DockStyle.Left, Width = 180 };
            btnPrev.Kbd = "←";
            btnPrev.Click += delegate { live.PrevLine(); };
            btnNext = new FusionButton { Text = "Siguiente", IconName = "player-skip-forward",
                                         Kind = FusionButtonKind.Primary, Dock = DockStyle.Right, Width = 180 };
            btnNext.Kbd = "→";
            btnNext.Click += delegate { live.NextLine(); };
            nav.Controls.Add(btnPrev); nav.Controls.Add(btnNext);
            phone.Controls.Add(nav);

            // B / C / L
            var keys = new Panel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(8, 4, 8, 4) };
            btnB = MkKey("Negro", "B", delegate { ToggleBlank("black"); });
            btnC = MkKey("Limpiar", "C", delegate { ToggleBlank("clear"); });
            btnL = MkKey("Logo", "L", delegate { ToggleBlank("logo"); });
            btnB.Dock = btnC.Dock = btnL.Dock = DockStyle.Left;
            btnB.Width = btnC.Width = btnL.Width = 122;
            keys.Controls.Add(btnB); keys.Controls.Add(btnC); keys.Controls.Add(btnL);
            phone.Controls.Add(keys);

            // programa
            program = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
                                    Font = UiTheme.Normal(), BackColor = UiTheme.Panel,
                                    DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 34, IntegralHeight = false };
            program.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                var scn = program.Items[e.Index] as Fusion.Shared.Model.Scenario;
                bool sel = e.Index == live.State.ScenarioIndex;
                using (var b = new SolidBrush(sel ? UiTheme.AccentSoft : Color.Transparent))
                    e.Graphics.FillRectangle(b, e.Bounds);
                string title = scn != null ? scn.Title : "";
                using (var b = new SolidBrush(sel ? UiTheme.AccentDark : UiTheme.Text))
                    TextRenderer.DrawText(e.Graphics, (e.Index + 1) + ".  " + title,
                        sel ? UiTheme.NormalBold() : UiTheme.Normal(),
                        new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                        b.Color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            program.SelectedIndexChanged += delegate
            {
                if (program.SelectedIndex >= 0 && program.SelectedIndex != live.State.ScenarioIndex)
                    live.Select(program.SelectedIndex, 0, 0);
            };
            phone.Controls.Add(program);

            timer = new Timer { Interval = 500 };
            timer.Tick += delegate { RefreshMando(); };
            timer.Start();
            RefreshMando();
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
                if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Space) { live.NextLine(); e.Handled = true; }
                if (e.KeyCode == Keys.Left) { live.PrevLine(); e.Handled = true; }
                if (e.KeyCode == Keys.B) { ToggleBlank("black"); e.Handled = true; }
                if (e.KeyCode == Keys.C) { ToggleBlank("clear"); e.Handled = true; }
                if (e.KeyCode == Keys.L) { ToggleBlank("logo"); e.Handled = true; }
            };
        }

        FusionButton MkKey(string text, string kbd, EventHandler onClick)
        {
            var b = new FusionButton { Text = text, Kind = FusionButtonKind.Chip, Kbd = kbd };
            b.Click += onClick;
            return b;
        }

        void ToggleBlank(string mode)
        {
            live.Blank(live.State.BlankMode == mode ? "none" : mode);
        }

        void RefreshMando()
        {
            if (IsDisposed) return;
            // programa
            if (live.Project != null && program.Items.Count != live.Project.Scenarios.Count)
            {
                program.Items.Clear();
                foreach (var s in live.Project.Scenarios) program.Items.Add(s);
            }
            if (live.State.ScenarioIndex >= 0 && live.State.ScenarioIndex < program.Items.Count)
                program.SelectedIndex = live.State.ScenarioIndex;
            // preview + línea
            if (live.State.IsBlank) preview.ShowBlank(live.State.BlankMode);
            else if (live.State.Current != null) preview.ShowSlide(live.State.Current, live.State.LineIndex);
            var scn = live.CurrentScenario;
            lblItem.Text = scn != null ? scn.Title : "";
            var el = live.CurrentElement;
            string line = "";
            if (el != null && el.Lines.Count > 0 && live.State.LineIndex < el.Lines.Count)
                line = el.Lines[live.State.LineIndex];
            if (line == "" && live.State.Current != null && !string.IsNullOrEmpty(live.State.Current.Reference))
                line = live.State.Current.Reference;
            lblLine.Text = line == "" ? "Sin contenido" : line;
            // estados B/C/L
            SetKind(btnB, live.State.BlankMode == "black");
            SetKind(btnC, live.State.BlankMode == "clear");
            SetKind(btnL, live.State.BlankMode == "logo");
        }

        static void SetKind(FusionButton b, bool on)
        {
            var k = on ? FusionButtonKind.Active : FusionButtonKind.Chip;
            if (b.Kind != k) { b.Kind = k; b.Invalidate(); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (timer != null) timer.Stop();
            base.OnFormClosed(e);
        }
    }
}
