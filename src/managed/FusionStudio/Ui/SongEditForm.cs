// ============================================================================
//  Fusion-HP · SongEditForm — editor de cantos [SPEC §7.2.1]:
// título, autor, etiquetas y secciones (Verso/Coro/…) con líneas editables.
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using Fusion.Shared.Model;
using Fusion.Shared.Music;

namespace Fusion.Studio.Ui
{
    public class SongEditForm : Form
    {
        public Song Result;
        readonly Song original;
        TextBox txtTitle, txtArtist, txtTags, txtKey, txtBpm;
        ListBox sections;
        TextBox lines;
        Button btnAddSection, btnDelSection;

        public SongEditForm(Song song)
        {
            original = song;
            Text = song == null ? "Nuevo canto" : "Editar canto";
            Size = new Size(640, 560);
            StartPosition = FormStartPosition.CenterParent;
            Font = UiTheme.Normal();
            BackColor = UiTheme.Panel;

            int y = 14;
            Label L(string t, int x, int yy) { var l = new Label { Text = t, Location = new Point(x, yy), AutoSize = true, ForeColor = UiTheme.TextDim }; Controls.Add(l); return l; }

            L("Título", 14, y);
            txtTitle = new TextBox { Location = new Point(90, y - 3), Size = new Size(280, 24) };
            Controls.Add(txtTitle);

            L("Intérprete/autor", 390, y);
            txtArtist = new TextBox { Location = new Point(494, y - 3), Size = new Size(120, 24) };
            Controls.Add(txtArtist);
            y += 32;

            L("Etiquetas", 14, y);
            txtTags = new TextBox { Location = new Point(90, y - 3), Size = new Size(280, 24) };
            Controls.Add(txtTags);

            L("Tonalidad", 390, y);
            txtKey = new TextBox { Location = new Point(470, y - 3), Size = new Size(48, 24) };
            Controls.Add(txtKey);
            L("BPM", 528, y);
            txtBpm = new TextBox { Location = new Point(560, y - 3), Size = new Size(40, 24) };
            Controls.Add(txtBpm);
            y += 30;

            // Transposición del cifrado (función de las betas 1): los acordes de
            // las líneas de cifrado se reescriben conservando las columnas.
            var lblTr = new Label { Text = "Transponer cifrado:", Location = new Point(90, y), AutoSize = true,
                                    ForeColor = UiTheme.TextDim };
            Controls.Add(lblTr);
            int tx = 210;
            foreach (int step in new[] { -2, -1, 1, 2 })
            {
                int st = step;
                var b = new Fusion.Studio.Ui.Widgets.FusionButton
                {
                    Text = (step > 0 ? "+" : "") + step,
                    Location = new Point(tx, y - 4),
                    Size = new Size(40, 28), Font = UiTheme.Small()
                };
                b.Click += delegate { TransposeAll(st); };
                Controls.Add(b);
                tx += 44;
            }
            var trTip = new ToolTip();
            trTip.SetToolTip(lblTr, "Reconoce líneas de acordes (C, Do, Am, Lam, C/E, maj7, sus4…) y las transporta.");
            y += 34;

            L("Secciones", 14, y);
            btnAddSection = new Fusion.Studio.Ui.Widgets.FusionButton { Text = "Sección", IconName = "plus",
                Location = new Point(90, y - 4), Size = new Size(96, 28) };
            btnAddSection.Click += delegate
            {
                var s = Result != null ? Result : Build();
                s.Sections.Add(new SongSection { Name = "Verso " + (s.Sections.Count + 1), Lines = { "" } });
                LoadFrom(s);
            };
            Controls.Add(btnAddSection);
            btnDelSection = new Fusion.Studio.Ui.Widgets.FusionButton { Text = "Quitar", IconName = "trash",
                Location = new Point(192, y - 4), Size = new Size(90, 28) };
            btnDelSection.Click += delegate
            {
                var s = Result != null ? Result : Build();
                if (sections.SelectedIndex >= 0 && s.Sections.Count > sections.SelectedIndex)
                {
                    s.Sections.RemoveAt(sections.SelectedIndex);
                    LoadFrom(s);
                }
            };
            Controls.Add(btnDelSection);
            y += 28;

            sections = new ListBox { Location = new Point(14, y), Size = new Size(180, 300), Font = UiTheme.Normal() };
            sections.SelectedIndexChanged += delegate { SyncToModel(); LoadLines(); };
            Controls.Add(sections);

            lines = new TextBox { Location = new Point(204, y), Size = new Size(410, 300), Multiline = true,
                                  ScrollBars = ScrollBars.Vertical, Font = UiTheme.Normal(), AcceptsReturn = true };
            lines.TextChanged += delegate { SyncLines(); };
            Controls.Add(lines);

            y += 308;
            var btnSave = new Fusion.Studio.Ui.Widgets.FusionButton
            {
                Text = "Guardar", IconName = "check", Kind = Fusion.Studio.Ui.Widgets.FusionButtonKind.Primary,
                Location = new Point(444, y), Size = new Size(88, 32)
            };
            btnSave.Click += delegate
            {
                SyncToModel();
                Result = Build();
                if (string.IsNullOrEmpty(Result.Title))
                {
                    MessageBox.Show(this, "El canto necesita un título.", "Fusion HP",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnSave);
            var btnCancel = new Fusion.Studio.Ui.Widgets.FusionButton { Text = "Cancelar", IconName = "x",
                Location = new Point(538, y), Size = new Size(86, 32) };
            btnCancel.Click += delegate { Close(); };
            Controls.Add(btnCancel);

            if (song != null) LoadFrom(song);
            else LoadFrom(new Song());
        }

        void LoadFrom(Song s)
        {
            Result = s;
            txtTitle.Text = s.Title ?? "";
            txtArtist.Text = s.Artist ?? "";
            txtTags.Text = s.Tags != null ? string.Join(", ", s.Tags.ToArray()) : "";
            txtKey.Text = s.Key_ ?? "";
            txtBpm.Text = s.Bpm > 0 ? s.Bpm.ToString("0") : "";
            sections.Items.Clear();
            foreach (var sec in s.Sections) sections.Items.Add(sec.Name ?? "Verso");
            if (sections.Items.Count > 0) sections.SelectedIndex = 0;
            LoadLines();
        }

        void LoadLines()
        {
            var s = Result;
            if (s == null || sections.SelectedIndex < 0 || sections.SelectedIndex >= s.Sections.Count)
            {
                lines.Text = "";
                return;
            }
            lines.Text = string.Join(Environment.NewLine, s.Sections[sections.SelectedIndex].Lines.ToArray());
        }

        void SyncLines()
        {
            var s = Result;
            if (s == null || sections.SelectedIndex < 0 || sections.SelectedIndex >= s.Sections.Count) return;
            var sec = s.Sections[sections.SelectedIndex];
            sec.Lines.Clear();
            foreach (var ln in lines.Text.Split(new[] { '\n' }))
            {
                var t = ln.TrimEnd('\r');
                if (t.Trim().Length > 0) sec.Lines.Add(t);
            }
        }

        void SyncToModel()
        {
            var s = Result;
            if (s == null) return;
            s.Title = txtTitle.Text.Trim();
            s.Artist = txtArtist.Text.Trim();
            s.Tags.Clear();
            foreach (var t in txtTags.Text.Split(new[] { ',' }))
            {
                var tt = t.Trim();
                if (tt.Length > 0) s.Tags.Add(tt.ToLowerInvariant());
            }
            s.Key_ = txtKey.Text.Trim();
            double bpm;
            s.Bpm = double.TryParse(txtBpm.Text.Trim(), out bpm) ? bpm : 0;
        }

        /// <summary>Transpone TODAS las líneas de cifrado del canto (port betas 1).</summary>
        void TransposeAll(int semis)
        {
            var s = Result;
            if (s == null) return;
            SyncToModel();
            bool latin = Chords.LooksLikeKey(s.Key_) && s.Key_.Length >= 2 &&
                         "adefgl".IndexOf(char.ToLowerInvariant(s.Key_[0])) >= 0;   // Do/Re/Mi… o La menor
            foreach (var sec in s.Sections)
                for (int i = 0; i < sec.Lines.Count; i++)
                    if (Chords.IsChordLine(sec.Lines[i]))
                        sec.Lines[i] = Chords.TransposeLine(sec.Lines[i], semis, latin);
            s.Key_ = Chords.TransposeKey(s.Key_ ?? "", semis, latin);
            LoadFrom(s);
        }

        Song Build()
        {
            var s = Result ?? new Song();
            if (original != null && s == original) s = original;
            s.Title = txtTitle.Text.Trim();
            s.Artist = txtArtist.Text.Trim();
            s.Tags.Clear();
            foreach (var t in txtTags.Text.Split(new[] { ',' }))
            {
                var tt = t.Trim();
                if (tt.Length > 0) s.Tags.Add(tt.ToLowerInvariant());
            }
            return s;
        }
    }
}
