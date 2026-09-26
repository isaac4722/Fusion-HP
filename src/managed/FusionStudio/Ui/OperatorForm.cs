// ============================================================================
//  Fusion-HP · FusionStudio/Ui/OperatorForm.cs — MODO OPERADOR (v4.2.0).
//  Consola dedicada del operador EN VIVO (patrón Holy/PTT, vista presentador
//  de PowerPoint): lo que está EN PANTALLA grande, el SIGUIENTE elemento,
//  transporte gigante (Anterior/Siguiente), Negro/Logo/Ocultar a un toque,
//  lista del programa con búsqueda y reloj. Pensada para manejar el culto
//  con teclado (Espacio/flechas/B/C/L) sin tocar la consola completa.
//  Funciona con el MISMO motor: cada acción viaja por ipc.v1 (motor.*).
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using Fusion.Shared.Model;
using Fusion.Studio.Services;
using Fusion.Studio.Ui.Widgets;

namespace Fusion.Studio.Ui
{
    public class OperatorForm : Form
    {
        readonly MainForm owner;
        readonly LiveOrchestrator live;

        SlidePreview nowPreview;        // EN PANTALLA
        SlidePreview nextPreview;       // SIGUIENTE
        Label lblNowTitle, lblNextTitle, lblLine, lblClock, lblOutput;
        FusionSearchBox txtFind;
        ListBox progList;
        FusionButton btnPrev, btnNext, btnBlack, btnLogo, btnClear, btnOutput;
        System.Collections.Generic.List<Scenario> visibleScenarios =
            new System.Collections.Generic.List<Scenario>();
        bool syncing;

        public OperatorForm(MainForm main, LiveOrchestrator orchestrator)
        {
            owner = main;
            live = orchestrator;
            Text = "Modo Operador — Fusion HP";
            StartPosition = FormStartPosition.Manual;
            Location = new Point(Math.Max(0, main.Left + 60), Math.Max(30, main.Top + 40));
            Size = new Size(1180, 720);
            MinimumSize = new Size(960, 600);
            Font = UiTheme.Normal();
            BackColor = UiTheme.Bg;
            KeyPreview = true;
            ShowInTaskbar = false;
            TopMost = true;

            BuildUi();

            live.StateChanged += OnLiveChanged;
            FormClosed += delegate { live.StateChanged -= OnLiveChanged; };
            Load += delegate { RefreshAll(); };
        }

        void BuildUi()
        {
            // ---------------- barra superior: título · reloj · salida ----------------
            var top = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = UiTheme.Panel };
            var sep = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.Border };
            top.Controls.Add(sep);
            var title = new Label { Text = "MODO OPERADOR", Font = UiTheme.Title(), ForeColor = UiTheme.AccentDark,
                                    Location = new Point(14, 12), AutoSize = true };
            top.Controls.Add(title);
            lblOutput = new Label { Text = "salida ○ oculta", Font = UiTheme.SmallBold(), ForeColor = UiTheme.TextDim,
                                    Location = new Point(280, 20), AutoSize = true };
            top.Controls.Add(lblOutput);
            lblClock = new Label { Text = DateTime.Now.ToString("HH:mm:ss"), Font = new Font("Consolas", 18f, FontStyle.Bold),
                                   ForeColor = UiTheme.Text, AutoSize = true,
                                   Anchor = AnchorStyles.Top | AnchorStyles.Right };
            top.Controls.Add(lblClock);
            var btnClose = new FusionButton { Text = "Cerrar", IconName = "x", Kind = FusionButtonKind.Chip,
                                              Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnClose.Click += delegate { Close(); };
            top.Controls.Add(btnClose);
            top.Resize += delegate
            {
                lblClock.Location = new Point(top.Width - lblClock.PreferredWidth - 150, 12);
                btnClose.Location = new Point(top.Width - btnClose.Width - 14, 11);
            };
            var clockTimer = new Timer { Interval = 1000 };
            clockTimer.Tick += delegate { lblClock.Text = DateTime.Now.ToString("HH:mm:ss"); };
            clockTimer.Start();
            FormClosed += delegate { clockTimer.Stop(); clockTimer.Dispose(); };
            Controls.Add(top);

            // ---------------- izquierda: programa + búsqueda ----------------
            var left = new Panel { Dock = DockStyle.Left, Width = 320, BackColor = UiTheme.Panel, Padding = new Padding(10) };
            var lblProg = new Label { Text = "PROGRAMA", Dock = DockStyle.Top, Height = 22, Font = UiTheme.SmallBold(),
                                      ForeColor = UiTheme.TextDim, TextAlign = ContentAlignment.MiddleLeft };
            txtFind = new FusionSearchBox { Dock = DockStyle.Top, Height = 30 };
            txtFind.Placeholder = "Buscar elemento…";
            txtFind.InnerTextChanged += delegate { FillProgram(); };
            progList = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = UiTheme.Normal(),
                DrawMode = DrawMode.OwnerDrawVariable, IntegralHeight = false, BackColor = Color.White
            };
            progList.DrawItem += ProgDraw;
            progList.MeasureItem += delegate(object s, MeasureItemEventArgs e) { e.ItemHeight = 44; };
            progList.SelectedIndexChanged += delegate
            {
                if (syncing) return;
                int idx = IndexOfVisible(progList.SelectedIndex);
                if (idx >= 0) live.Select(idx, live.Project != null && live.Project.Scenarios[idx].Elements.Count > 0 ? 0 : -1, 0);
            };
            left.Controls.Add(progList);
            left.Controls.Add(txtFind);
            left.Controls.Add(lblProg);
            Controls.Add(left);
            left.BringToFront();

            // ---------------- derecha: transporte gigante ----------------
            var right = new Panel { Dock = DockStyle.Right, Width = 250, BackColor = UiTheme.Panel,
                                    Padding = new Padding(12) };
            int y = 14;
            btnNext = new FusionButton { Text = "SIGUIENTE  ▶", Kind = FusionButtonKind.Primary,
                                         Font = UiTheme.Big(), Location = new Point(12, y), Size = new Size(224, 64) };
            btnNext.Click += delegate { live.NextLine(); };
            right.Controls.Add(btnNext); y += 74;

            btnPrev = new FusionButton { Text = "◀  ANTERIOR", Kind = FusionButtonKind.Chip,
                                         Font = UiTheme.Big(), Location = new Point(12, y), Size = new Size(224, 56) };
            btnPrev.Click += delegate { live.PrevLine(); };
            right.Controls.Add(btnPrev); y += 70;

            var lblScreens = new Label { Text = "PANTALLAS", Font = UiTheme.SmallBold(), ForeColor = UiTheme.TextDim,
                                         Location = new Point(12, y), AutoSize = true };
            right.Controls.Add(lblScreens); y += 24;

            btnOutput = new FusionButton { Text = "Mostrar pantalla", IconName = "device-desktop",
                                           Kind = FusionButtonKind.Primary, Location = new Point(12, y), Size = new Size(224, 44) };
            btnOutput.Click += delegate
            {
                if (live.State.OutputVisible) live.HideOutput();
                else live.ShowOutput(owner.Settings.PublicMonitor >= 0 ? owner.Settings.PublicMonitor : -1);
            };
            right.Controls.Add(btnOutput); y += 54;

            btnBlack = new FusionButton { Text = "Negro", IconName = "square", Kbd = "B",
                                          Kind = FusionButtonKind.Chip, Location = new Point(12, y), Size = new Size(106, 44) };
            btnBlack.Click += delegate { live.Blank(live.State.BlankMode == "black" ? "none" : "black"); };
            right.Controls.Add(btnBlack);
            btnLogo = new FusionButton { Text = "Logo", IconName = "photo", Kbd = "L",
                                         Kind = FusionButtonKind.Chip, Location = new Point(130, y), Size = new Size(106, 44) };
            btnLogo.Click += delegate { live.Blank(live.State.BlankMode == "logo" ? "none" : "logo"); };
            right.Controls.Add(btnLogo); y += 52;

            btnClear = new FusionButton { Text = "Ocultar texto", IconName = "eye-off", Kbd = "C",
                                          Kind = FusionButtonKind.Chip, Location = new Point(12, y), Size = new Size(224, 44) };
            btnClear.Click += delegate { live.Blank(live.State.BlankMode == "clear" ? "none" : "clear"); };
            right.Controls.Add(btnClear); y += 58;

            var lblHints = new Label
            {
                Text = "Espacio/→ avanza   ·   ← retrocede\n↑/↓ elemento   ·   B/L/C pantallas\nEsc cierra el Modo Operador",
                Font = UiTheme.Small(), ForeColor = UiTheme.TextDim,
                Location = new Point(12, y), Size = new Size(224, 70)
            };
            right.Controls.Add(lblHints);
            Controls.Add(right);

            // ---------------- centro: EN PANTALLA + SIGUIENTE ----------------
            var center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            Controls.Add(center);
            center.BringToFront();

            var nowFrame = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Padding = new Padding(1) };
            nowFrame.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(UiTheme.InputBorder))
                    e.Graphics.DrawRectangle(pen, 0, 0, nowFrame.Width - 1, nowFrame.Height - 1);
            };
            nowPreview = new SlidePreview { Dock = DockStyle.Fill };
            nowFrame.Controls.Add(nowPreview);
            var nowLabel = new Label { Text = "EN PANTALLA", Dock = DockStyle.Top, Height = 24, Font = UiTheme.SmallBold(),
                                       ForeColor = UiTheme.TextDim, TextAlign = ContentAlignment.MiddleLeft,
                                       Padding = new Padding(6, 0, 0, 0), BackColor = UiTheme.Panel };
            var nowHead = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = UiTheme.Panel };
            lblNowTitle = new Label { Text = "—", Font = UiTheme.NormalBold(), ForeColor = UiTheme.Text,
                                      Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };
            lblLine = new Label { Text = "", Font = UiTheme.Small(), ForeColor = UiTheme.TextDim,
                                  Dock = DockStyle.Right, Width = 110, TextAlign = ContentAlignment.MiddleRight,
                                  Padding = new Padding(0, 0, 8, 0) };
            nowHead.Controls.Add(lblNowTitle);
            nowHead.Controls.Add(lblLine);

            var nextBlock = new Panel { Dock = DockStyle.Bottom, Height = 190, BackColor = UiTheme.Bg };
            var nextHead = new Label { Text = "SIGUIENTE", Dock = DockStyle.Top, Height = 20, Font = UiTheme.SmallBold(),
                                       ForeColor = UiTheme.TextDim, TextAlign = ContentAlignment.MiddleLeft,
                                       Padding = new Padding(6, 0, 0, 0) };
            lblNextTitle = new Label { Text = "—", Dock = DockStyle.Fill, Font = UiTheme.Small(), ForeColor = UiTheme.TextDim,
                                       TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };
            nextPreview = new SlidePreview { Dock = DockStyle.Fill };
            var nextHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Padding = new Padding(1) };
            nextHost.Controls.Add(nextPreview);
            nextBlock.Controls.Add(nextHost);
            nextBlock.Controls.Add(lblNextTitle);
            nextBlock.Controls.Add(nextHead);

            center.Controls.Add(nowFrame);
            center.Controls.Add(nowHead);
            center.Controls.Add(nextBlock);
            nowHead.BringToFront();
            nowFrame.BringToFront();

            // 16:9 para la vista grande (como la salida real)
            center.Resize += delegate
            {
                int h = Math.Max(160, (center.ClientSize.Width - 2) * 9 / 16);
                nextBlock.Height = 190;
                // nowFrame.Dock=Fill toma el resto: su altura la fija el layout;
                // limitamos por anchura re-dimensionando el host interno (padding)
                int avail = center.ClientSize.Height - nowHead.Height - nextBlock.Height - 8;
                int want = (center.ClientSize.Width - 2) * 9 / 16;
                if (want > avail && avail > 120)
                    nowFrame.Padding = new Padding((center.ClientSize.Width - avail * 16 / 9) / 2, 0,
                                                   (center.ClientSize.Width - avail * 16 / 9) / 2, 0);
                else
                    nowFrame.Padding = new Padding(1);
                h = 0; // (evita aviso de variable sin usar)
            };

            // teclado del operador (KeyPreview)
            KeyDown += OnOpKey;
        }

        void OnOpKey(object sender, KeyEventArgs e)
        {
            if (ActiveControl is TextBox || ActiveControl is ComboBox || ActiveControl is FusionInput)
                return;   // escribir en el buscador no dispara transporte
            switch (e.KeyCode)
            {
                case Keys.Right:
                case Keys.Space:
                case Keys.PageDown: live.NextLine(); e.Handled = true; break;
                case Keys.Left:
                case Keys.PageUp: live.PrevLine(); e.Handled = true; break;
                case Keys.Down: live.NextElement(); e.Handled = true; break;
                case Keys.Up: live.PrevElement(); e.Handled = true; break;
                case Keys.B: live.Blank(live.State.BlankMode == "black" ? "none" : "black"); e.Handled = true; break;
                case Keys.C: live.Blank(live.State.BlankMode == "clear" ? "none" : "clear"); e.Handled = true; break;
                case Keys.L: live.Blank(live.State.BlankMode == "logo" ? "none" : "logo"); e.Handled = true; break;
                case Keys.Escape: Close(); e.Handled = true; break;
            }
        }

        // ------------------------------------------------------------ programa
        void FillProgram()
        {
            syncing = true;
            visibleScenarios.Clear();
            progList.Items.Clear();
            string q = (txtFind.Text ?? "").Trim();
            if (live.Project != null)
            {
                foreach (var scn in live.Project.Scenarios)
                {
                    if (q.Length == 0 ||
                        scn.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        visibleScenarios.Add(scn);
                        progList.Items.Add(scn);
                    }
                }
            }
            syncing = false;
            MarkActive();
        }

        int IndexOfVisible(int visibleIdx)
        {
            if (visibleIdx < 0 || visibleIdx >= visibleScenarios.Count || live.Project == null) return -1;
            return live.Project.Scenarios.IndexOf(visibleScenarios[visibleIdx]);
        }

        void ProgDraw(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= visibleScenarios.Count) return;
            var s = visibleScenarios[e.Index];
            int realIdx = live.Project != null ? live.Project.Scenarios.IndexOf(s) : -1;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool active = realIdx == live.State.ScenarioIndex;
            using (var b = new SolidBrush(selected ? UiTheme.AccentSoft : (e.Index % 2 == 0 ? Color.White : UiTheme.Bg)))
                e.Graphics.FillRectangle(b, e.Bounds);
            if (active)
                using (var b = new SolidBrush(UiTheme.Accent))
                    e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
            using (var b = new SolidBrush(active ? UiTheme.AccentDark : UiTheme.Text))
                TextRenderer.DrawText(e.Graphics, (realIdx + 1) + ".  " + s.Title, UiTheme.NormalBold(),
                    new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 2, e.Bounds.Width - 20, 20), b.Color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, s.Elements.Count + " diapositiva(s)", UiTheme.Small(),
                new Rectangle(e.Bounds.X + 28, e.Bounds.Y + 22, e.Bounds.Width - 36, 16), UiTheme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        void MarkActive()
        {
            // resaltar el escenario activo sin robar la selección del operador
            syncing = true;
            int vi = -1;
            if (live.Project != null && live.State.ScenarioIndex >= 0 &&
                live.State.ScenarioIndex < live.Project.Scenarios.Count)
                vi = visibleScenarios.IndexOf(live.Project.Scenarios[live.State.ScenarioIndex]);
            if (vi >= 0) progList.SelectedIndex = vi;
            syncing = false;
            progList.Invalidate();
        }

        // ------------------------------------------------------------ reflejo del estado
        void OnLiveChanged()
        {
            if (IsDisposed) return;
            try { BeginInvoke((Action)RefreshAll); }
            catch { /* forma cerrada */ }
        }

        void RefreshAll()
        {
            if (IsDisposed) return;
            LiveState st = live.State;

            // programa
            if (progList.Items.Count != CountVisibleExpected()) FillProgram();
            else MarkActive();

            // EN PANTALLA (mismo contrato que la preview de la consola)
            if (st.IsBlank) nowPreview.ShowBlank(st.BlankMode);
            else if (st.Current != null) nowPreview.ShowSlide(st.Current, st.LineIndex);
            else nowPreview.ShowBlank("black");
            var scn = live.CurrentScenario;
            lblNowTitle.Text = scn != null ? scn.Title : "—";
            int total = st.Current != null ? st.Current.Lines.Count : 0;
            lblLine.Text = st.Current != null ? (st.LineIndex + 1) + " / " + total : "";

            // SIGUIENTE
            Element nxt = NextOf(live.Project, st.ScenarioIndex, st.ElementIndex);
            if (nxt != null && live.Project != null)
            {
                var nscn = ScenarioOf(live.Project, st.ScenarioIndex, st.ElementIndex, nxt);
                string baseDir = !string.IsNullOrEmpty(live.Project.SourcePath)
                    ? System.IO.Path.GetDirectoryName(live.Project.SourcePath) : owner.Settings.ProjectsPath;
                var r = ResolvedSlide.Resolve(nxt, nscn, live.Project, baseDir);
                if (string.IsNullOrEmpty(r.Transition)) r.Transition = "cut";
                nextPreview.ShowSlide(r, 0);
                lblNextTitle.Text = (nscn != null ? nscn.Title + " — " : "") +
                    (nxt.Lines.Count > 0 ? nxt.Lines[0] : (nxt.Reference ?? "Elemento"));
            }
            else
            {
                nextPreview.ShowBlank("black");
                lblNextTitle.Text = "— fin del programa —";
            }

            // botones
            bool vis = st.OutputVisible;
            lblOutput.Text = vis ? "salida ● EN VIVO" : "salida ○ oculta";
            lblOutput.ForeColor = vis ? UiTheme.LiveRed : UiTheme.TextDim;
            btnOutput.Text = vis ? "Terminar presentación" : "Mostrar pantalla";
            btnOutput.Kind = vis ? FusionButtonKind.Active : FusionButtonKind.Primary;
            SetActive(btnBlack, st.BlankMode == "black");
            SetActive(btnLogo, st.BlankMode == "logo");
            SetActive(btnClear, st.BlankMode == "clear");
        }

        int CountVisibleExpected()
        {
            // el listado se reconstruye si cambió el proyecto (ediciones del estudio)
            string q = (txtFind.Text ?? "").Trim();
            int n = 0;
            if (live.Project != null)
                foreach (var s in live.Project.Scenarios)
                    if (q.Length == 0 || s.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) n++;
            return n;
        }

        static void SetActive(FusionButton b, bool on)
        {
            var k = on ? FusionButtonKind.Active : FusionButtonKind.Chip;
            if (b.Kind != k) { b.Kind = k; b.Invalidate(); }
        }

        static Scenario ScenarioOf(AhpProject p, int scnIdx, int elIdx, Element el)
        {
            // localiza el escenario dueño del elemento «siguiente» calculado
            if (p == null) return null;
            if (scnIdx >= 0 && scnIdx < p.Scenarios.Count)
            {
                var s = p.Scenarios[scnIdx];
                if (elIdx + 1 < s.Elements.Count && s.Elements.Count > 0 && ReferenceEquals(s.Elements[elIdx + 1], el)) return s;
            }
            if (scnIdx + 1 < p.Scenarios.Count && p.Scenarios[scnIdx + 1].Elements.Count > 0 &&
                ReferenceEquals(p.Scenarios[scnIdx + 1].Elements[0], el))
                return p.Scenarios[scnIdx + 1];
            foreach (var s in p.Scenarios)
                if (s.Elements.Contains(el)) return s;
            return null;
        }

        /// <summary>Siguiente elemento del programa tras (scnIdx, elIdx): dentro del
        /// escenario, o el primero del siguiente. Público y puro para las pruebas.</summary>
        public static Element NextOf(AhpProject p, int scnIdx, int elIdx)
        {
            if (p == null || scnIdx < 0 || scnIdx >= p.Scenarios.Count) return null;
            var scn = p.Scenarios[scnIdx];
            if (elIdx + 1 < scn.Elements.Count) return scn.Elements[elIdx + 1];
            if (scnIdx + 1 < p.Scenarios.Count)
            {
                var nxt = p.Scenarios[scnIdx + 1];
                if (nxt.Elements.Count > 0) return nxt.Elements[0];
            }
            return null;
        }
    }
}
