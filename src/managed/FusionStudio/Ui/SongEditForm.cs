// ============================================================================
//  Fusion-HP · SongEditForm — editor de cantos [SPEC §7.2.1]:
// título, autor, etiquetas y secciones (Verso/Coro/…) con líneas editables.
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using Fusion.Shared.Model;

namespace Fusion.Studio.Ui
{
    public class SongEditForm : Form
    {
        public Song Result;
        readonly Song original;
        TextBox txtTitle, txtArtist, txtTags;
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
            y += 36;

            L("Secciones", 14, y);
            btnAddSection = new Button { Text = "+ Sección", Location = new Point(90, y - 4), AutoSize = true, FlatStyle = FlatStyle.Flat };
            btnAddSection.Click += delegate
            {
                var s = Result != null ? Result : Build();
                s.Sections.Add(new SongSection { Name = "Verso " + (s.Sections.Count + 1), Lines = { "" } });
                LoadFrom(s);
            };
            Controls.Add(btnAddSection);
            btnDelSection = new Button { Text = "Quitar", Location = new Point(180, y - 4), AutoSize = true, FlatStyle = FlatStyle.Flat };
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
            var btnSave = new Button { Text = "Guardar", Location = new Point(450, y), Size = new Size(80, 32),
                                       BackColor = UiTheme.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
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
            var btnCancel = new Button { Text = "Cancelar", Location = new Point(538, y), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat };
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
