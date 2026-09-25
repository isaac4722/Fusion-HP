// ============================================================================
//  Fusion-HP · MainForm.Present.cs — modo Presentación [SPEC §6]:
//  biblioteca a la izquierda (acceso directo), previsualización y programa
//  al centro, controles en vivo a la derecha (controles modelados v2.2: chips con
//  iconos, transporte tipo reproductor, estados activos .ppt-iconbtn-on).
//  Sincronización línea por línea visible como sub-lista [SPEC §6.2].
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using Fusion.Shared;
using Fusion.Shared.Model;
using Fusion.Studio.Ui.Widgets;

namespace Fusion.Studio.Ui
{
    public partial class MainForm
    {
        FusionButton btnSend, btnBlack, btnLogo, btnClear, btnShow, btnMsg, btnVerse, btnChords, btnAdvance;
        FusionIconButton ibPrevEl, ibPrevLine, ibNextLine, ibNextEl;
        FusionSearchBox txtHighlight;

        void BuildPresent()
        {
            presentPanel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg, Visible = false };
            content.Controls.Add(presentPanel);

            // ---------------- biblioteca (izquierda) ----------------
            BuildLibrary(presentPanel);

            // ---------------- centro: preview + programa ----------------
            var center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            presentPanel.Controls.Add(center);
            center.BringToFront();

            var previewFrame = new Panel { Dock = DockStyle.Top, Height = 330, BackColor = UiTheme.Panel,
                                           Padding = new Padding(1) };
            previewFrame.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(UiTheme.InputBorder))
                    e.Graphics.DrawRectangle(pen, 0, 0, previewFrame.Width - 1, previewFrame.Height - 1);
            };
            preview = new SlidePreview { Dock = DockStyle.Fill };
            var previewLabel = new Label { Text = "PREVISUALIZACIÓN", Dock = DockStyle.Bottom, Height = 22,
                                           Font = UiTheme.SmallBold(), ForeColor = UiTheme.TextDim,
                                           TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0),
                                           BackColor = UiTheme.Panel };
            preview.Controls.Add(previewLabel);
            previewFrame.Controls.Add(preview);
            center.Controls.Add(previewFrame);

            // programa con iconos de tipo y sub-líneas
            programPanel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Panel, Padding = new Padding(8),
                                       AutoScroll = true };
            programPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(UiTheme.InputBorder))
                    e.Graphics.DrawRectangle(pen, 0, 0, programPanel.Width - 1, programPanel.Height - 1);
            };
            var programTitle = new Label { Text = "Programa", Dock = DockStyle.Top, Height = 26, Font = UiTheme.NormalBold(),
                                           ForeColor = UiTheme.Text, TextAlign = ContentAlignment.MiddleLeft };
            programList = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
                                        DrawMode = DrawMode.OwnerDrawVariable, ItemHeight = 46,
                                        IntegralHeight = false, Font = UiTheme.Normal(), BackColor = Color.White };
            programList.DrawItem += ProgramListDraw;
            programList.MeasureItem += delegate(object s, MeasureItemEventArgs e) { e.ItemHeight = 46; };
            programList.SelectedIndexChanged += delegate
            {
                if (suppressPreviewRefresh) return;   // actualización interna: no re-seleccionar
                if (programList.SelectedIndex >= 0)
                {
                    int idx = programList.SelectedIndex;
                    var scn = Live.Project != null && idx < Live.Project.Scenarios.Count ? Live.Project.Scenarios[idx] : null;
                    if (scn != null && (Live.State.ScenarioIndex != idx || Live.State.ElementIndex != 0))
                        Live.Select(idx, scn.Elements.Count > 0 ? 0 : -1, 0);
                }
            };
            var programInner = new Panel { Dock = DockStyle.Fill };
            programInner.Controls.Add(programList);
            programInner.Controls.Add(programTitle);
            programTitle.BringToFront();
            programList.BringToFront();

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                                             SplitterDistance = 330, FixedPanel = FixedPanel.Panel1,
                                             BackColor = UiTheme.Bg };
            split.Panel1.Controls.Add(previewFrame);
            split.Panel2.Controls.Add(programInner);
            center.Controls.Add(split);

            // ---------------- controles en vivo (derecha, modelados) ----------------
            var right = new Panel { Dock = DockStyle.Right, Width = 232, BackColor = UiTheme.Panel,
                                    Padding = new Padding(10, 10, 10, 8) };
            right.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(UiTheme.InputBorder))
                    e.Graphics.DrawRectangle(pen, 0, 0, right.Width - 1, right.Height - 1);
            };
            int y = 12;

            var lblCtl = new Label { Text = "CONTROL EN VIVO", Font = UiTheme.SmallBold(), ForeColor = UiTheme.TextDim,
                                     Location = new Point(12, y), AutoSize = true };
            right.Controls.Add(lblCtl); y += 26;

            // Transporte tipo reproductor (línea y elemento) — fila de 4
            ibPrevEl = MakeLiveIcon(right, "chevrons-left", "Elemento anterior (↑)", y, 0);
            ibPrevLine = MakeLiveIcon(right, "player-skip-back", "Línea anterior (←)", y, 1);
            ibNextLine = MakeLiveIcon(right, "player-skip-forward", "Línea siguiente (Espacio)", y, 2);
            ibNextEl = MakeLiveIcon(right, "chevrons-right", "Elemento siguiente (↓)", y, 3);
            ibPrevEl.Click += delegate { Live.PrevElement(); };
            ibPrevLine.Click += delegate { Live.PrevLine(); };
            ibNextLine.Click += delegate { Live.NextLine(); };
            ibNextEl.Click += delegate { Live.NextElement(); };
            y += 42;

            btnSend = MakeLiveButton("Enviar a pantalla", "player-play", ref y, FusionButtonKind.Primary);
            btnSend.Kbd = "Enter";
            btnSend.Click += delegate
            {
                var scn = CurrentScenarioUi;
                if (scn != null) Live.SendToLive(scn);
            };
            right.Controls.Add(btnSend); y += 40;

            var lblScreens = new Label { Text = "PANTALLAS", Font = UiTheme.SmallBold(), ForeColor = UiTheme.TextDim,
                                         Location = new Point(12, y), AutoSize = true };
            right.Controls.Add(lblScreens); y += 22;

            btnBlack = MakeLiveButton("Negro", "square", ref y, FusionButtonKind.Chip);
            btnBlack.Kbd = "B";
            btnBlack.Click += delegate { Live.Blank(Live.State.BlankMode == "black" ? "none" : "black"); };
            right.Controls.Add(btnBlack); y += 36;

            btnLogo = MakeLiveButton("Logo", "photo", ref y, FusionButtonKind.Chip);
            btnLogo.Kbd = "L";
            btnLogo.Click += delegate { Live.Blank(Live.State.BlankMode == "logo" ? "none" : "logo"); };
            right.Controls.Add(btnLogo); y += 36;

            btnClear = MakeLiveButton("Ocultar texto", "eye-off", ref y, FusionButtonKind.Chip);
            btnClear.Kbd = "C";
            btnClear.Click += delegate { Live.Blank(Live.State.BlankMode == "clear" ? "none" : "clear"); };
            right.Controls.Add(btnClear); y += 36;

            btnShow = MakeLiveButton("Mostrar", "eye", ref y, FusionButtonKind.Chip);
            btnShow.Click += delegate { Live.Blank("none"); };
            right.Controls.Add(btnShow); y += 40;

            var lblTools = new Label { Text = "HERRAMIENTAS", Font = UiTheme.SmallBold(), ForeColor = UiTheme.TextDim,
                                       Location = new Point(12, y), AutoSize = true };
            right.Controls.Add(lblTools); y += 22;

            btnVerse = MakeLiveButton("Versículo rápido", "book", ref y, FusionButtonKind.Chip);
            btnVerse.Kbd = "G";
            btnVerse.Click += delegate { FocusBibleSearch(); };
            right.Controls.Add(btnVerse); y += 36;

            btnMsg = MakeLiveButton("Mensaje en pantalla", "app-window-bottom", ref y, FusionButtonKind.Chip);
            btnMsg.Click += delegate { ShowMessageDialog(); };
            right.Controls.Add(btnMsg); y += 36;

            btnChords = MakeLiveButton("Acordes (músicos)", "piano", ref y, FusionButtonKind.Chip);
            btnChords.Click += delegate { ShowChordsWindow(); };
            right.Controls.Add(btnChords); y += 36;

            // Mando / Clasificador / Historial (GUI web + función beta-1)
            BuildWebExtras(right, ref y);

            // Escenario de músicos (Stage View beta-1): alerta, temporizador, tono/BPM
            BuildStageTools(right, ref y);

            // Resaltado en proyección [SPEC §5.2 #2] (port de las betas 1):
            // el MOTOR aplica y persiste las palabras; Enter aplica, vacío limpia.
            var lblHl = new Label { Text = "RESALTAR EN PANTALLA", Font = UiTheme.SmallBold(),
                                    ForeColor = UiTheme.TextDim, Location = new Point(12, y), AutoSize = true };
            right.Controls.Add(lblHl); y += 22;
            txtHighlight = new FusionSearchBox
            {
                LeftIcon = "wand",
                Location = new Point(12, y), Size = new Size(206, 30)
            };
            txtHighlight.Placeholder = "Palabras (p. ej. Dios amor)";
            ToolTip tipHl = new ToolTip();
            tipHl.SetToolTip(txtHighlight, "Palabras separadas por espacio. Enter aplica, vacío limpia.");
            txtHighlight.Inner.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { ApplyHighlight(); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Escape) { txtHighlight.Text = ""; ApplyHighlight(); e.SuppressKeyPress = true; }
            };
            right.Controls.Add(txtHighlight); y += 36;

            // Avance línea/diapositiva (referencia web) conmutable al vuelo
            btnAdvance = MakeLiveButton("Avance: línea por línea", "list", ref y, FusionButtonKind.Chip);
            btnAdvance.Click += delegate { ToggleAdvanceMode(); };
            right.Controls.Add(btnAdvance); y += 38;

            var lblLines = new Label { Text = "LÍNEAS DEL ELEMENTO", Font = UiTheme.SmallBold(), ForeColor = UiTheme.TextDim,
                                       Location = new Point(12, y), AutoSize = true };
            right.Controls.Add(lblLines); y += 24;
            linesPanel = new Panel { Location = new Point(12, y), Size = new Size(206, 220), AutoScroll = true,
                                     BackColor = Color.White };
            linesPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(UiTheme.InputBorder))
                    e.Graphics.DrawRectangle(pen, 0, 0, linesPanel.Width - 1, linesPanel.Height - 1);
            };
            right.Controls.Add(linesPanel);

            presentPanel.Controls.Add(right);
        }

        FusionIconButton MakeLiveIcon(Panel parent, string icon, string tooltip, int y, int col)
        {
            var b = new FusionIconButton
            {
                IconName = icon,
                Location = new Point(12 + col * 50, y)
            };
            new ToolTip().SetToolTip(b, tooltip);
            parent.Controls.Add(b);
            return b;
        }

        FusionButton MakeLiveButton(string text, string icon, ref int y, FusionButtonKind kind)
        {
            var b = new FusionButton
            {
                Text = text, IconName = icon, Kind = kind,
                Location = new Point(12, y), Size = new Size(206, 32)
            };
            return b;
        }

        ChordsForm chordsForm;

        void ToggleAdvanceMode()
        {
            Live.SetAdvance(Settings.AdvanceMode == "line" ? "slide" : "line");
        }

        void UpdateAdvanceButton()
        {
            if (btnAdvance == null) return;
            btnAdvance.Text = Settings.AdvanceMode == "line" ? "Avance: línea por línea" : "Avance: por diapositiva";
            btnAdvance.Invalidate();
        }

        /// <summary>Sincroniza los estados activos de los botones con el Motor.</summary>
        void UpdateLiveButtons()
        {
            if (btnBlack == null) return;
            string b = Live.State.BlankMode;
            SetActiveKind(btnBlack, b == "black");
            SetActiveKind(btnLogo, b == "logo");
            SetActiveKind(btnClear, b == "clear");
            SetActiveKind(btnAdvance, Settings.AdvanceMode == "line");
            UpdateAdvanceButton();
        }

        static void SetActiveKind(FusionButton b, bool on)
        {
            var k = on ? FusionButtonKind.Active : FusionButtonKind.Chip;
            if (b.Kind != k) { b.Kind = k; b.Invalidate(); }
        }

        void ApplyHighlight()
        {
            string t = txtHighlight.Text.Trim();
            var words = new System.Collections.Generic.List<string>();
            foreach (string w in t.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                words.Add(w);
            Live.SetHighlight(words);
        }

        void ShowChordsWindow()
        {
            if (chordsForm == null || chordsForm.IsDisposed)
                chordsForm = new ChordsForm(this, Live);
            chordsForm.Show(this);
            chordsForm.BringToFront();
        }

        Scenario CurrentScenarioUi
        {
            get
            {
                return Live.CurrentScenario;
            }
        }

        // ------------------------------------------------------------ programa
        void RefreshProgram()
        {
            if (programList == null || programList.IsDisposed) return;
            suppressPreviewRefresh = true;
            programList.Items.Clear();
            if (Live.Project != null)
            {
                for (int i = 0; i < Live.Project.Scenarios.Count; i++)
                {
                    var s = Live.Project.Scenarios[i];
                    programList.Items.Add(s);
                }
            }
            if (Live.State.ScenarioIndex >= 0 && Live.State.ScenarioIndex < programList.Items.Count)
                programList.SelectedIndex = Live.State.ScenarioIndex;
            suppressPreviewRefresh = false;
            RefreshLines();
            UpdatePreview();
            UpdateLiveButtons();
        }

        static string IconOfKind(string kind)
        {
            switch (kind)
            {
                case "verse": return "book";
                case "image": return "photo";
                case "video": return "movie";
                case "lower3": return "app-window-bottom";
                default: return "file-text";
            }
        }

        void ProgramListDraw(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var s = programList.Items[e.Index] as Scenario;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool active = e.Index == Live.State.ScenarioIndex;
            using (var b = new SolidBrush(selected ? UiTheme.AccentSoft : (e.Index % 2 == 0 ? Color.White : UiTheme.Bg)))
                e.Graphics.FillRectangle(b, e.Bounds);
            if (active)
                using (var b = new SolidBrush(UiTheme.Accent))
                    e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
            string icon = s != null && s.Elements.Count > 0 ? IconOfKind(s.Elements[0].KindKey) : "file-text";
            UiIcons.Draw(e.Graphics, icon, IconTint.Ink, e.Bounds.X + 10, e.Bounds.Y + 13);
            string title = s != null ? s.Title : "";
            using (var b = new SolidBrush(active ? UiTheme.AccentDark : UiTheme.Text))
                TextRenderer.DrawText(e.Graphics, (e.Index + 1) + ".  " + title, UiTheme.NormalBold(),
                    new Rectangle(e.Bounds.X + 38, e.Bounds.Y, e.Bounds.Width - 92, e.Bounds.Height),
                    b.Color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (s != null && s.Elements.Count > 0)
            {
                string count = s.Elements.Count.ToString();
                var cr = new Rectangle(e.Bounds.Right - 46, e.Bounds.Y + 14, 34, 18);
                using (var pen = new Pen(UiTheme.ChipBorder))
                    e.Graphics.DrawRectangle(pen, cr);
                TextRenderer.DrawText(e.Graphics, count, UiTheme.Small(), cr, UiTheme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        void RefreshLines()
        {
            if (linesPanel == null || linesPanel.IsDisposed) return;
            // v2.2.1: disponer los labels retirados — Clear() solo desacopla y
            // cada navegación recreaba el tira de líneas (fuga de handles).
            UiTheme.DisposeChildren(linesPanel);
            var el = Live.CurrentElement;
            if (el == null) return;
            int y = 4;
            for (int i = 0; i < el.Lines.Count; i++)
            {
                int idx = i;
                bool on = i == Live.State.LineIndex;
                var ln = new Label
                {
                    Text = el.Lines[i],
                    AutoSize = false,
                    Size = new Size(194, 24),
                    Location = new Point(4, y),
                    Font = on ? UiTheme.SmallBold() : UiTheme.Small(),
                    ForeColor = on ? UiTheme.AccentDark : UiTheme.TextDim,
                    BackColor = on ? UiTheme.AccentSoft : Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(6, 0, 0, 0),
                    Cursor = Cursors.Hand
                };
                ln.Click += delegate { Live.SetLine(idx); };
                linesPanel.Controls.Add(ln);
                y += 26;
            }
        }

        void UpdatePreview()
        {
            if (preview == null || preview.IsDisposed) return;
            if (Live.State.IsBlank)
            {
                preview.ShowBlank(Live.State.BlankMode);
            }
            else if (Live.State.Current != null)
            {
                preview.ShowSlide(Live.State.Current, Live.State.LineIndex);
            }
            RefreshLines();
            UpdateLiveButtons();
            RecordHistoryFromLive();      // beta-1: historial + tono/BPM al escenario
        }

        // ------------------------------------------------------------ diálogo mensaje
        void ShowMessageDialog()
        {
            using (var f = new Form())
            {
                f.Text = "Mensaje en pantalla (lower third)";
                f.Size = new Size(470, 190);
                f.StartPosition = FormStartPosition.CenterParent;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false;
                f.Font = UiTheme.Normal();
                f.BackColor = UiTheme.Bg;
                var lbl = new Label { Text = "Texto del aviso:", Location = new Point(14, 16), AutoSize = true };
                var txt = new FusionInput { Location = new Point(14, 40), Size = new Size(428, 30) };
                var ok = new FusionButton { Text = "Mostrar", IconName = "check", Kind = FusionButtonKind.Primary,
                                            DialogResult = DialogResult.OK, Location = new Point(262, 88), Size = new Size(88, 32) };
                var hide = new FusionButton { Text = "Ocultar", IconName = "eye-off", Kind = FusionButtonKind.Chip,
                                              Location = new Point(356, 88), Size = new Size(86, 32) };
                hide.Click += delegate
                {
                    // quitar el overlay del elemento actual
                    var el = Live.CurrentElement;
                    if (el != null) { el.OverlayText = null; Live.SendCurrent(); }
                    f.Close();
                };
                f.Controls.Add(lbl); f.Controls.Add(txt); f.Controls.Add(ok); f.Controls.Add(hide);
                f.AcceptButton = ok;
                if (f.ShowDialog(this) == DialogResult.OK && txt.Text.Trim().Length > 0)
                {
                    var el = Live.CurrentElement;
                    var scn = Live.CurrentScenario;
                    if (el == null && scn == null)
                    {
                        // sin elemento activo: crear elemento lower3 efímero
                        var s2 = new Scenario { Title = "Aviso" };
                        s2.Elements.Add(new Element { Kind = ElementKind.LowerThird, OverlayText = txt.Text.Trim(), Lines = { txt.Text.Trim() } });
                        Live.SendToLive(s2);
                    }
                    else if (el != null)
                    {
                        el.OverlayText = txt.Text.Trim();
                        Live.SendCurrent();
                    }
                }
            }
        }
    }
}
