// ============================================================================
//  Fusion-HP · MainForm.Present.cs — modo Presentación [SPEC §6]:
//  biblioteca a la izquierda (acceso directo), previsualización y programa
//  al centro, controles en vivo a la derecha. Sincronización línea por línea
//  visible como sub-lista bajo el elemento activo [SPEC §6.2].
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using Fusion.Shared;
using Fusion.Shared.Model;

namespace Fusion.Studio.Ui
{
    public partial class MainForm
    {
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
                                           Padding = new Padding(1), BorderStyle = BorderStyle.FixedSingle };
            preview = new SlidePreview { Dock = DockStyle.Fill };
            var previewLabel = new Label { Text = "Previsualización", Dock = DockStyle.Bottom, Height = 22,
                                           Font = UiTheme.Small(), ForeColor = UiTheme.TextDim,
                                           TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0),
                                           BackColor = UiTheme.Panel };
            preview.Controls.Add(previewLabel);
            previewFrame.Controls.Add(preview);
            center.Controls.Add(previewFrame);

            // programa con miniaturas y sub-líneas
            programPanel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Panel, Padding = new Padding(8),
                                       BorderStyle = BorderStyle.FixedSingle, AutoScroll = true };
            var programTitle = new Label { Text = "Programa", Dock = DockStyle.Top, Height = 26, Font = UiTheme.NormalBold(),
                                           ForeColor = UiTheme.Text, TextAlign = ContentAlignment.MiddleLeft };
            programList = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
                                        DrawMode = DrawMode.OwnerDrawVariable, ItemHeight = 46,
                                        IntegralHeight = false, Font = UiTheme.Normal() };
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

            // ---------------- controles en vivo (derecha) ----------------
            var right = new Panel { Dock = DockStyle.Right, Width = 220, BackColor = UiTheme.Panel,
                                    Padding = new Padding(10), BorderStyle = BorderStyle.FixedSingle };
            int y = 12;
            var lblCtl = new Label { Text = "CONTROL EN VIVO", Font = UiTheme.SmallBold(), ForeColor = UiTheme.TextDim,
                                     Location = new Point(10, y), AutoSize = true };
            right.Controls.Add(lblCtl); y += 28;

            var btnSend = MakeLiveButton("Enviar a pantalla", y, UiTheme.Accent, Color.White);
            btnSend.Click += delegate
            {
                var scn = CurrentScenarioUi;
                if (scn != null) Live.SendToLive(scn);
            };
            right.Controls.Add(btnSend); y += 42;

            var btnBlack = MakeLiveButton("Negro (Esc/B)", y, Color.Black, Color.White);
            btnBlack.Click += delegate { Live.Blank(Live.State.BlankMode == "black" ? "none" : "black"); };
            right.Controls.Add(btnBlack); y += 38;

            var btnLogo = MakeLiveButton("Logo (L)", y, UiTheme.Bg, UiTheme.Text);
            btnLogo.Click += delegate { Live.Blank(Live.State.BlankMode == "logo" ? "none" : "logo"); };
            right.Controls.Add(btnLogo); y += 38;

            var btnClear = MakeLiveButton("Ocultar texto (C)", y, UiTheme.Bg, UiTheme.Text);
            btnClear.Click += delegate { Live.Blank(Live.State.BlankMode == "clear" ? "none" : "clear"); };
            right.Controls.Add(btnClear); y += 44;

            var btnShow = MakeLiveButton("Mostrar", y, UiTheme.Bg, UiTheme.Text);
            btnShow.Click += delegate { Live.Blank("none"); };
            right.Controls.Add(btnShow); y += 44;

            var btnPrevEl = MakeLiveButton("◀ Elemento", y, UiTheme.Bg, UiTheme.Text);
            btnPrevEl.Click += delegate { Live.PrevElement(); };
            right.Controls.Add(btnPrevEl); y += 38;
            var btnNextEl = MakeLiveButton("Elemento ▶", y, UiTheme.Bg, UiTheme.Text);
            btnNextEl.Click += delegate { Live.NextElement(); };
            right.Controls.Add(btnNextEl); y += 44;

            var btnPrevLine = MakeLiveButton("◀ Línea", y, UiTheme.Bg, UiTheme.Text);
            btnPrevLine.Click += delegate { Live.PrevLine(); };
            right.Controls.Add(btnPrevLine); y += 38;
            var btnNextLine = MakeLiveButton("Línea ▶ (Espacio)", y, UiTheme.AccentSoft, UiTheme.AccentDark);
            btnNextLine.Click += delegate { Live.NextLine(); };
            right.Controls.Add(btnNextLine); y += 46;

            var btnMsg = MakeLiveButton("Mensaje (lower third)", y, UiTheme.Bg, UiTheme.Text);
            btnMsg.Click += delegate { ShowMessageDialog(); };
            right.Controls.Add(btnMsg); y += 44;

            var btnVerse = MakeLiveButton("Versículo rápido (G)", y, UiTheme.Bg, UiTheme.Text);
            btnVerse.Click += delegate { FocusBibleSearch(); };
            right.Controls.Add(btnVerse); y += 38;

            var btnChords = MakeLiveButton("Acordes (músicos)", y, UiTheme.Bg, UiTheme.Text);
            btnChords.Click += delegate { ShowChordsWindow(); };
            right.Controls.Add(btnChords); y += 44;

            // Resaltado en proyección [SPEC §5.2 #2]: palabras que se dibujan
            // en color de acento (port de las betas 1). Enter aplica, vacío limpia.
            var lblHl = new Label { Text = "RESALTAR EN PANTALLA", Font = UiTheme.SmallBold(),
                                    ForeColor = UiTheme.TextDim, Location = new Point(10, y), AutoSize = true };
            right.Controls.Add(lblHl); y += 22;
            txtHighlight = new TextBox { Location = new Point(10, y), Size = new Size(196, 24),
                                         Font = UiTheme.Normal(), BorderStyle = BorderStyle.FixedSingle };
            ToolTip tipHl = new ToolTip();
            tipHl.SetToolTip(txtHighlight, "Palabras separadas por espacio. Enter aplica, vacío limpia. Ej.: Dios amor");
            txtHighlight.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { ApplyHighlight(); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Escape) { txtHighlight.Text = ""; ApplyHighlight(); e.SuppressKeyPress = true; }
            };
            right.Controls.Add(txtHighlight); y += 30;

            // Avance línea/diapositiva (referencia web) conmutable al vuelo
            btnAdvance = MakeLiveButton("Avance: línea por línea", y, UiTheme.AccentSoft, UiTheme.AccentDark);
            btnAdvance.Click += delegate { ToggleAdvanceMode(); };
            right.Controls.Add(btnAdvance); y += 38;
            UpdateAdvanceButton();

            var lblLines = new Label { Text = "LÍNEAS DEL ELEMENTO", Font = UiTheme.SmallBold(), ForeColor = UiTheme.TextDim,
                                       Location = new Point(10, y), AutoSize = true };
            right.Controls.Add(lblLines); y += 24;
            linesPanel = new Panel { Location = new Point(10, y), Size = new Size(196, 240), AutoScroll = true,
                                     BackColor = UiTheme.Panel, BorderStyle = BorderStyle.FixedSingle };
            right.Controls.Add(linesPanel);

            presentPanel.Controls.Add(right);
        }

        Button MakeLiveButton(string text, int y, Color bg, Color fg)
        {
            var b = new Button { Text = text, Location = new Point(10, y), Size = new Size(196, 32),
                                 FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = fg,
                                 Font = UiTheme.Normal(), UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        TextBox txtHighlight;
        Button btnAdvance;
        ChordsForm chordsForm;

        void ToggleAdvanceMode()
        {
            Settings.AdvanceMode = Settings.AdvanceMode == "line" ? "slide" : "line";
            Settings.Save();
            UpdateAdvanceButton();
        }

        void UpdateAdvanceButton()
        {
            if (btnAdvance == null) return;
            btnAdvance.Text = Settings.AdvanceMode == "line" ? "Avance: línea por línea" : "Avance: por diapositiva";
        }

        void ApplyHighlight()
        {
            var el = Live.CurrentElement;
            if (el == null) return;
            el.HighlightWords.Clear();
            string t = txtHighlight.Text.Trim();
            if (t.Length > 0)
                foreach (string w in t.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                    el.HighlightWords.Add(w);
            Live.SendCurrent();
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
                    string label = (i + 1) + ".  " + s.Title;
                    if (s.Elements.Count > 0) label += "   [" + s.Elements.Count + "]";
                    programList.Items.Add(label);
                }
            }
            if (Live.State.ScenarioIndex >= 0 && Live.State.ScenarioIndex < programList.Items.Count)
                programList.SelectedIndex = Live.State.ScenarioIndex;
            suppressPreviewRefresh = false;
            RefreshLines();
            UpdatePreview();
        }

        void ProgramListDraw(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool active = e.Index == Live.State.ScenarioIndex;
            using (var b = new SolidBrush(selected ? UiTheme.AccentSoft : (e.Index % 2 == 0 ? Color.White : UiTheme.Bg)))
                e.Graphics.FillRectangle(b, e.Bounds);
            if (active)
                using (var b = new SolidBrush(UiTheme.Accent))
                    e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
            string text = e.Index < programList.Items.Count ? (string)programList.Items[e.Index] : "";
            using (var b = new SolidBrush(active ? UiTheme.AccentDark : UiTheme.Text))
                e.Graphics.DrawString(text, UiTheme.Normal(), b, e.Bounds.X + 10, e.Bounds.Y + 12);
        }

        void RefreshLines()
        {
            if (linesPanel == null || linesPanel.IsDisposed) return;
            linesPanel.Controls.Clear();
            var el = Live.CurrentElement;
            if (el == null) return;
            int y = 4;
            for (int i = 0; i < el.Lines.Count; i++)
            {
                int idx = i;
                var ln = new Label
                {
                    Text = el.Lines[i],
                    AutoSize = false,
                    Size = new Size(184, 24),
                    Location = new Point(4, y),
                    Font = UiTheme.Small(),
                    ForeColor = i == Live.State.LineIndex ? UiTheme.AccentDark : UiTheme.TextDim,
                    BackColor = i == Live.State.LineIndex ? UiTheme.AccentSoft : Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(4, 0, 0, 0),
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
        }

        // ------------------------------------------------------------ diálogo mensaje
        void ShowMessageDialog()
        {
            using (var f = new Form())
            {
                f.Text = "Mensaje en pantalla (lower third)";
                f.Size = new Size(460, 170);
                f.StartPosition = FormStartPosition.CenterParent;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false;
                var lbl = new Label { Text = "Texto del aviso:", Location = new Point(12, 14), AutoSize = true };
                var txt = new TextBox { Location = new Point(12, 36), Size = new Size(420, 24) };
                var ok = new Button { Text = "Mostrar", DialogResult = DialogResult.OK, Location = new Point(260, 80), Size = new Size(80, 30) };
                var hide = new Button { Text = "Ocultar", Location = new Point(348, 80), Size = new Size(80, 30) };
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
