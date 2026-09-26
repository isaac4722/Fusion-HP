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
        FusionButton btnSend, btnBlack, btnLogo, btnClear, btnShow, btnMsg, btnVerse, btnChords, btnAdvance, btnOutput;
        FusionIconButton ibPrevEl, ibPrevLine, ibNextLine, ibNextEl;
        FusionSearchBox txtHighlight;
        System.Collections.Generic.Dictionary<int, Bitmap> progThumbs;

        // modificación de anchos del panel derecho (v4.2.0): 252 px y chips en
        // cuadrícula 2×N — la columna antigua de 232 px apilaba TODO en vertical
        // hasta y≈1010 y «LÍNEAS DEL ELEMENTO» quedaba invisible (C2).
        const int RightWidth = 264;
        const int GridW = 116;      // (264 - 2*12 - 8) / 2

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

            // v4.2.0 (G9): la preview guarda el aspecto 16:9 — antes un alto fijo
            // de 330 px la estiraba en pantallas grandes y no encogía en pequeñas.
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
            split.Panel1MinSize = 180;
            split.Panel1.Controls.Add(previewFrame);
            split.Panel2.Controls.Add(programInner);
            center.Controls.Add(split);
            split.Panel1.Resize += delegate
            {
                int h = Math.Max(160, split.Panel1.ClientSize.Width * 9 / 16);
                if (Math.Abs(previewFrame.Height - h) > 1 && h < split.Panel1.ClientSize.Height - 24)
                    previewFrame.Height = h;      // 16:9, sin aplastar el programa
            };

            // ---------------- controles en vivo (derecha, cuadrícula compacta) ----------------
            var right = new Panel { Dock = DockStyle.Right, Width = RightWidth, BackColor = UiTheme.Panel,
                                    Padding = new Padding(10, 10, 10, 8), AutoScroll = true };
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
            // v4.2.0 (G2): la pista «Enter» era falsa (Enter no enviaba nada) —
            // se retira en lugar de secuestrar el Enter de los campos de texto.
            btnSend.Click += delegate
            {
                var scn = CurrentScenarioUi;
                if (scn != null)
                {
                    Live.SendToLive(scn);
                    StartPresentation();   // como PowerPoint: al presentar se MUESTRA la salida
                }
            };
            right.Controls.Add(btnSend); y += 40;

            var lblScreens = new Label { Text = "PANTALLAS", Font = UiTheme.SmallBold(), ForeColor = UiTheme.TextDim,
                                         Location = new Point(12, y), AutoSize = true };
            right.Controls.Add(lblScreens); y += 22;

            // v4.2.0 — SALIDA INTEGRADA (estilo PowerPoint): la ventana de salida
            // no es un programa aparte que vive ahí negro para siempre; nace
            // OCULTA y solo aparece al INICIAR la presentación. «Terminar» la
            // vuelve a ocultar (Esc también termina).
            btnOutput = MakeLiveButton("Iniciar presentación", "device-desktop", ref y, FusionButtonKind.Primary);
            btnOutput.Click += delegate { ToggleOutput(); };
            right.Controls.Add(btnOutput); y += 40;

            // v4.2.0 (C2): cuadrícula 2×2 — Negro/Logo y Ocultar/Mostrar comparten
            // filas (antes 4 chips apilados consumían 144 px y empujaban el resto)
            btnBlack = MakeGridButton(right, "Negro", "square", "B", 0, ref y, delegate
            { Live.Blank(Live.State.BlankMode == "black" ? "none" : "black"); });
            btnLogo = MakeGridButton(right, "Logo", "photo", "L", 1, ref y, delegate
            { Live.Blank(Live.State.BlankMode == "logo" ? "none" : "logo"); });
            y += 36;
            btnClear = MakeGridButton(right, "Ocultar", "eye-off", "C", 0, ref y, delegate
            { Live.Blank(Live.State.BlankMode == "clear" ? "none" : "clear"); });
            btnShow = MakeGridButton(right, "Mostrar", "eye", null, 1, ref y, delegate { Live.Blank("none"); });
            y += 40;

            var lblTools = new Label { Text = "HERRAMIENTAS", Font = UiTheme.SmallBold(), ForeColor = UiTheme.TextDim,
                                       Location = new Point(12, y), AutoSize = true };
            right.Controls.Add(lblTools); y += 22;

            btnVerse = MakeGridButton(right, "Versículo", "book", "G", 0, ref y, delegate { ShowQuickVerse(); });
            btnMsg = MakeGridButton(right, "Mensaje", "app-window-bottom", null, 1, ref y, delegate { ShowMessageDialog(); });
            y += 36;
            btnChords = MakeGridButton(right, "Acordes", "piano", null, 0, ref y, delegate { ShowChordsWindow(); });
            btnSorter = MakeGridButton(right, "Clasificador", "layout-grid", null, 1, ref y, delegate { ShowSorter(); });
            y += 36;
            btnHistory = MakeGridButton(right, "Historial", "clock", null, 0, ref y, delegate { ShowHistory(); });
            y += 40;

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
                Location = new Point(12, y), Size = new Size(RightWidth - 24, 30)
            };
            txtHighlight.Placeholder = "Palabras (p. ej. Dios amor)";
            Tips.SetToolTip(txtHighlight, "Palabras separadas por espacio. Enter aplica, vacío limpia.");
            txtHighlight.Inner.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { ApplyHighlight(); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Escape) { txtHighlight.Text = ""; ApplyHighlight(); e.SuppressKeyPress = true; }
            };
            right.Controls.Add(txtHighlight); y += 36;

            // Avance línea/diapositiva (referencia web) conmutable al vuelo
            btnAdvance = MakeLiveButton("Avance: línea por línea", "list", ref y, FusionButtonKind.Chip);
            btnAdvance.Click += delegate { ToggleAdvanceMode(); };
            right.Controls.Add(btnAdvance); y += 36;

            var lblLines = new Label { Text = "LÍNEAS DEL ELEMENTO", Font = UiTheme.SmallBold(), ForeColor = UiTheme.TextDim,
                                       Location = new Point(12, y), AutoSize = true };
            right.Controls.Add(lblLines); y += 24;
            linesPanel = new Panel { Location = new Point(12, y), Size = new Size(RightWidth - 24, 190), AutoScroll = true,
                                     BackColor = Color.White };
            linesPanel.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(UiTheme.InputBorder))
                    e.Graphics.DrawRectangle(pen, 0, 0, linesPanel.Width - 1, linesPanel.Height - 1);
            };
            right.Controls.Add(linesPanel);

            presentPanel.Controls.Add(right);
        }

        /// <summary>Botón chip de la cuadrícula 2×N del panel derecho (v4.2.0).</summary>
        FusionButton MakeGridButton(Panel parent, string text, string icon, string kbd, int col, ref int y, EventHandler onClick)
        {
            var b = new FusionButton
            {
                Text = text, IconName = icon, Kind = FusionButtonKind.Chip, Kbd = kbd,
                Location = new Point(12 + col * (GridW + 8), y), Size = new Size(GridW, 32)
            };
            b.Click += onClick;
            parent.Controls.Add(b);
            return b;
        }

        FusionIconButton MakeLiveIcon(Panel parent, string icon, string tooltip, int y, int col)
        {
            var b = new FusionIconButton
            {
                IconName = icon,
                Location = new Point(12 + col * 50, y)
            };
            Tips.SetToolTip(b, tooltip);   // v4.2.0 (G12): tooltip compartido
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
            // v4.2.0: ciclo de la salida integrada (texto/estado del botón)
            if (btnOutput != null)
            {
                bool vis = Live.State.OutputVisible;
                btnOutput.Text = vis ? "Terminar presentación" : "Iniciar presentación";
                var k = vis ? FusionButtonKind.Active : FusionButtonKind.Primary;
                if (btnOutput.Kind != k) { btnOutput.Kind = k; btnOutput.Invalidate(); }
                else btnOutput.Invalidate();   // el texto cambió: repintar igual
            }
        }

        // ------------------------------------------------------------ salida integrada (v4.2.0)
        void ToggleOutput()
        {
            if (Live.State.OutputVisible) Live.HideOutput();
            else StartPresentation();
        }

        void StartPresentation()
        {
            if (!Live.State.OutputVisible)
                Live.ShowOutput(Settings.PublicMonitor >= 0 ? Settings.PublicMonitor : -1);
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
            InvalidateProgThumbs();      // miniaturas obsoletas (paridad web: 2 columnas)
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
            // miniatura de la primera diapositiva (paridad web: programa con
            // miniaturas de 2 columnas — aquí 1 columna por fila)
            var th = GetProgThumb(e.Index);
            int textX = e.Bounds.X + 38;
            if (th != null)
            {
                var tr = new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 3, 74, 40);
                e.Graphics.DrawImage(th, tr);
                using (var pen = new Pen(UiTheme.ChipBorder))
                    e.Graphics.DrawRectangle(pen, tr);
                textX = e.Bounds.X + 92;
            }
            string icon = s != null && s.Elements.Count > 0 ? IconOfKind(s.Elements[0].KindKey) : "file-text";
            if (th == null) UiIcons.Draw(e.Graphics, icon, IconTint.Ink, e.Bounds.X + 10, e.Bounds.Y + 13);
            string title = s != null ? s.Title : "";
            using (var b = new SolidBrush(active ? UiTheme.AccentDark : UiTheme.Text))
                TextRenderer.DrawText(e.Graphics, (e.Index + 1) + ".  " + title, UiTheme.NormalBold(),
                    new Rectangle(textX, e.Bounds.Y, e.Bounds.Width - (textX - e.Bounds.X) - 56, e.Bounds.Height),
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

        /// <summary>Miniatura cacheada del primer elemento del escenario índice.</summary>
        Bitmap GetProgThumb(int idx)
        {
            try
            {
                var p = Live.Project;
                if (p == null || idx < 0 || idx >= p.Scenarios.Count) return null;
                var scn = p.Scenarios[idx];
                if (scn.Elements.Count == 0) return null;
                if (progThumbs == null) progThumbs = new System.Collections.Generic.Dictionary<int, Bitmap>();
                Bitmap bmp;
                if (progThumbs.TryGetValue(idx, out bmp)) return bmp;
                string baseDir = !string.IsNullOrEmpty(p.SourcePath)
                    ? System.IO.Path.GetDirectoryName(p.SourcePath) : Settings.ProjectsPath;
                var rs = ResolvedSlide.Resolve(scn.Elements[0], scn, p, baseDir);
                if (rs == null) return null;
                bmp = new Bitmap(148, 84);
                using (var g = Graphics.FromImage(bmp))
                    SlidePreview.RenderSlide(g, rs, 0, 148, 84);
                progThumbs[idx] = bmp;
                return bmp;
            }
            catch { return null; }
        }

        void InvalidateProgThumbs()
        {
            if (progThumbs == null) return;
            foreach (var kv in progThumbs) kv.Value.Dispose();
            progThumbs.Clear();
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
                    Size = new Size(linesPanel.ClientSize.Width - 12, 24),
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
                    if (el != null && !string.IsNullOrEmpty(el.OverlayText))
                    {
                        el.OverlayText = null;
                        Live.SendCurrent();
                    }
                    f.Close();
                };
                f.Controls.Add(lbl); f.Controls.Add(txt); f.Controls.Add(ok); f.Controls.Add(hide);
                f.AcceptButton = ok;
                if (f.ShowDialog(this) == DialogResult.OK && txt.Text.Trim().Length > 0)
                {
                    var el = Live.CurrentElement;
                    var scn = Live.CurrentScenario;
                    if (el != null)
                    {
                        // elemento activo: el aviso viaja como overlay del propio elemento
                        el.OverlayText = txt.Text.Trim();
                        Live.SendCurrent();
                    }
                    else
                    {
                        // v4.2.0 (C9): caso muerto corregido — con escenario seleccionado
                        // pero sin elemento activo el botón NO HACÍA NADA; ahora crea
                        // el lower third efímero igual que sin programa (y «Ocultar»
                        // puede retirarlo porque el elemento pasa a ser el activo).
                        var s2 = new Scenario { Title = "Aviso" };
                        s2.Elements.Add(new Element { Kind = ElementKind.LowerThird, OverlayText = txt.Text.Trim(), Lines = { txt.Text.Trim() } });
                        Live.SendToLive(s2);
                    }
                }
            }
        }
    }
}
