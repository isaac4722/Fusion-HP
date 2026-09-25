// ============================================================================
//  Fusion-HP · MainForm.Library.cs — biblioteca lateral [SPEC §7.2]:
//  Cantos, Biblias, Escenarios y Medios accesibles DIRECTAMENTE (corrección
//  del prototipo: las bibliotecas ya no exigen búsqueda). Búsqueda en caliente
//  opcional (Ctrl+K / G para Biblia) [SPEC §6.5.2]. Biblias con búsqueda
//  instantánea por cita o palabra [SPEC §7.2.2].
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Fusion.Shared;
using Fusion.Shared.Bible;
using Fusion.Shared.Model;

namespace Fusion.Studio.Ui
{
    public partial class MainForm
    {
        void BuildLibrary(Panel parent)
        {
            libraryPanel = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = UiTheme.Panel,
                                       Padding = new Padding(0, 8, 0, 0), BorderStyle = BorderStyle.FixedSingle };
            parent.Controls.Add(libraryPanel);

            var searchBox = new TextBox { Location = new Point(10, 8), Size = new Size(270, 24), Font = UiTheme.Normal(),
                                          ForeColor = UiTheme.TextDim, Text = "Buscar (Ctrl+K)…",
                                          BorderStyle = BorderStyle.FixedSingle };
            searchBox.Enter += delegate
            {
                if (searchBox.Text == "Buscar (Ctrl+K)…") { searchBox.Text = ""; searchBox.ForeColor = UiTheme.Text; }
            };
            searchBox.Leave += delegate
            {
                if (searchBox.Text.Length == 0) { searchBox.Text = "Buscar (Ctrl+K)…"; searchBox.ForeColor = UiTheme.TextDim; }
            };
            searchBox.TextChanged += delegate
            {
                string q = searchBox.Text == "Buscar (Ctrl+K)…" ? "" : searchBox.Text;
                RefreshSongs(q);
            };
            libraryPanel.Controls.Add(searchBox);

            libTabs = new TabControl { Location = new Point(2, 40), Size = new Size(292, 2000),
                                       Font = UiTheme.Small(), Alignment = TabAlignment.Top };
            libTabs.SelectedIndexChanged += delegate { LibraryTabChanged(); };
            libraryPanel.Controls.Add(libTabs);
            libraryPanel.SizeChanged += delegate { libTabs.Size = new Size(libraryPanel.Width - 4, libraryPanel.Height - 46); };

            // ---- pestaña Cantos
            var tabSongs = new TabPage("Cantos");
            songsList = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = UiTheme.Normal(),
                                      DrawMode = DrawMode.OwnerDrawVariable, IntegralHeight = false };
            songsList.DrawItem += SongsListDraw;
            songsList.MeasureItem += delegate(object s, MeasureItemEventArgs e) { e.ItemHeight = 44; };
            songsList.DoubleClick += delegate { PlaySelectedSong(); };
            var songBar = new Panel { Dock = DockStyle.Bottom, Height = 34 };
            var btnNewSong = new Button { Text = "+ Nuevo", Dock = DockStyle.Left, Width = 80, FlatStyle = FlatStyle.Flat,
                                          Font = UiTheme.Small() };
            btnNewSong.Click += delegate { EditSong(null); };
            var btnEditSong = new Button { Text = "Editar", Dock = DockStyle.Left, Width = 70, FlatStyle = FlatStyle.Flat,
                                           Font = UiTheme.Small() };
            btnEditSong.Click += delegate { EditSong(SelectedSong()); };
            var btnImportSongs = new Button { Text = "Importar", Dock = DockStyle.Right, Width = 80, FlatStyle = FlatStyle.Flat,
                                              Font = UiTheme.Small() };
            btnImportSongs.Click += delegate { ImportSongs(); };
            songBar.Controls.Add(btnNewSong); songBar.Controls.Add(btnEditSong); songBar.Controls.Add(btnImportSongs);
            tabSongs.Controls.Add(songsList);
            tabSongs.Controls.Add(songBar);
            libTabs.TabPages.Add(tabSongs);

            // ---- pestaña Biblia
            var tabBible = new TabPage("Biblia");
            bibleVersion = new ComboBox { Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat, Font = UiTheme.Normal(),
                                          DropDownStyle = ComboBoxStyle.DropDownList };
            bibleVersion.SelectedIndexChanged += delegate { RefreshBibleTree(); };
            bibleSearch = new TextBox { Dock = DockStyle.Top, Font = UiTheme.Normal(), BorderStyle = BorderStyle.FixedSingle,
                                        ForeColor = UiTheme.TextDim, Text = "Cita o palabra (G)…" };
            bibleSearch.Enter += delegate
            {
                if (bibleSearch.Text == "Cita o palabra (G)…") { bibleSearch.Text = ""; bibleSearch.ForeColor = UiTheme.Text; }
            };
            bibleSearch.Leave += delegate
            {
                if (bibleSearch.Text.Length == 0) { bibleSearch.Text = "Cita o palabra (G)…"; bibleSearch.ForeColor = UiTheme.TextDim; }
            };
            bibleSearch.TextChanged += delegate { BibleSearchChanged(); };
            bibleTree = new TreeView { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = UiTheme.Normal(),
                                       ShowLines = false, HideSelection = false };
            bibleTree.NodeMouseDoubleClick += delegate(object s, TreeNodeMouseClickEventArgs e)
            {
                if (e.Node.Tag is string) BibleGoRef(e.Node.Tag as string);
            };
            bibleResults = new ListBox { Dock = DockStyle.Bottom, Height = 180, BorderStyle = BorderStyle.None,
                                         Font = UiTheme.Small(), IntegralHeight = false };
            bibleResults.DrawItem += BibleResultsDraw;
            bibleResults.MeasureItem += delegate(object s, MeasureItemEventArgs e) { e.ItemHeight = 34; };
            bibleResults.DoubleClick += delegate { BibleGoResult(); };
            tabBible.Controls.Add(bibleTree);
            tabBible.Controls.Add(bibleResults);
            tabBible.Controls.Add(bibleSearch);
            tabBible.Controls.Add(bibleVersion);
            libTabs.TabPages.Add(tabBible);

            // ---- pestaña Escenarios
            var tabScn = new TabPage("Escenarios");
            scenariosList = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = UiTheme.Normal(),
                                          IntegralHeight = false };
            scenariosList.DoubleClick += delegate { Live.SendToLive(SelectedScenario()); };
            var scnBar = new Panel { Dock = DockStyle.Bottom, Height = 34 };
            var btnNewScn = new Button { Text = "+ Nuevo", Dock = DockStyle.Left, Width = 80, FlatStyle = FlatStyle.Flat, Font = UiTheme.Small() };
            btnNewScn.Click += delegate
            {
                if (Live.Project == null) Live.Project = AhpProject.CreateDefault();
                var scn = new Scenario { Title = "Escenario nuevo" };
                scn.Elements.Add(new Element { Lines = { "Texto de prueba" } });
                Live.Project.Scenarios.Add(scn);
                RefreshLibrary();
                RefreshProgram();
            };
            var btnDelScn = new Button { Text = "Quitar", Dock = DockStyle.Left, Width = 70, FlatStyle = FlatStyle.Flat, Font = UiTheme.Small() };
            btnDelScn.Click += delegate
            {
                var scn = SelectedScenario();
                if (scn != null && Live.Project != null)
                {
                    Live.Project.Scenarios.Remove(scn);
                    RefreshLibrary();
                    RefreshProgram();
                }
            };
            scnBar.Controls.Add(btnNewScn); scnBar.Controls.Add(btnDelScn);
            tabScn.Controls.Add(scenariosList);
            tabScn.Controls.Add(scnBar);
            libTabs.TabPages.Add(tabScn);

            // ---- pestaña Medios
            var tabMedia = new TabPage("Medios");
            mediaList = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = UiTheme.Normal(),
                                      IntegralHeight = false };
            mediaList.DoubleClick += delegate { SendSelectedMedia(); };
            var medBar = new Panel { Dock = DockStyle.Bottom, Height = 34 };
            var btnAddMedia = new Button { Text = "+ Añadir medio", Dock = DockStyle.Left, Width = 120, FlatStyle = FlatStyle.Flat, Font = UiTheme.Small() };
            btnAddMedia.Click += delegate { AddMedia(); };
            var btnImportBible = new Button { Text = "Importar Biblia", Dock = DockStyle.Right, Width = 110, FlatStyle = FlatStyle.Flat, Font = UiTheme.Small() };
            btnImportBible.Click += delegate { ImportBible(); };
            medBar.Controls.Add(btnAddMedia); medBar.Controls.Add(btnImportBible);
            tabMedia.Controls.Add(mediaList);
            tabMedia.Controls.Add(medBar);
            libTabs.TabPages.Add(tabMedia);
        }

        void LibraryTabChanged() { }

        public void RefreshLibrary()
        {
            RefreshSongs("");
            RefreshBibles();
            RefreshScenarios();
            RefreshMedia();
        }

        // ------------------------------------------------------------ cantos
        void RefreshSongs(string query)
        {
            if (songsList == null) return;
            var sel = SelectedSong();
            songsList.Items.Clear();
            foreach (var s in Live.Songs.Search(query)) songsList.Items.Add(s);
            if (sel != null && songsList.Items.Contains(sel)) songsList.SelectedItem = sel;
        }

        void SongsListDraw(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var s = songsList.Items[e.Index] as Song;
            if (s == null) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? UiTheme.AccentSoft : Color.White))
                e.Graphics.FillRectangle(b, e.Bounds);
            using (var b = new SolidBrush(UiTheme.Text))
                e.Graphics.DrawString(s.Title, UiTheme.NormalBold(), b, e.Bounds.X + 8, e.Bounds.Y + 4);
            string sub = (string.IsNullOrEmpty(s.Artist) ? "" : s.Artist + " · ") +
                         s.Sections.Count + " partes" +
                         (s.UseCount > 0 ? " · usada " + s.UseCount + "×" : "");
            using (var b = new SolidBrush(UiTheme.TextDim))
                e.Graphics.DrawString(sub, UiTheme.Small(), b, e.Bounds.X + 8, e.Bounds.Y + 24);
        }

        Song SelectedSong()
        {
            return songsList != null ? songsList.SelectedItem as Song : null;
        }

        void PlaySelectedSong()
        {
            var s = SelectedSong();
            if (s == null) return;
            Live.Songs.RegisterUse(s);
            Live.SendToLive(s.ToScenario());
            RefreshSongs("");
            SetPresentMode();
        }

        void SetPresentMode() { SetMode(Mode.Present); }

        void EditSong(Song song)
        {
            using (var f = new SongEditForm(song))
            {
                if (f.ShowDialog(this) == DialogResult.OK && f.Result != null)
                {
                    Live.Songs.Save(f.Result);
                    RefreshSongs("");
                }
            }
        }

        void ImportSongs()
        {
            using (var d = new OpenFileDialog())
            {
                d.Title = "Importar cantos (JSON de himnario o respaldo Holyrics)";
                d.Filter = "Cantos (*.json;*.hjsong)|*.json;*.hjsong|Todos (*.*)|*.*";
                d.Multiselect = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var rep = Live.Songs.ImportFiles(d.FileNames);
                RefreshSongs("");
                string msg = "Cantos nuevos: " + rep.Added +
                             "\nCantos actualizados (mismo título+artista): " + rep.Updated +
                             "\n\nEl cancionero vive en UNA base única (cancionero.fdb): " +
                             "no se crean archivos por canto ni duplicados." +
                             (rep.Warnings.Count > 0 ? "\n\nAvisos:\n- " + string.Join("\n- ", rep.Warnings.ToArray()) : "");
                MessageBox.Show(this, msg, "Fusion HP — importación", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // ------------------------------------------------------------ biblia
        void RefreshBibles()
        {
            if (bibleVersion == null) return;
            bibleVersion.Items.Clear();
            var bibles = Live.Bibles.List();
            foreach (var b in bibles) bibleVersion.Items.Add(b);
            if (bibleVersion.Items.Count > 0) bibleVersion.SelectedIndex = 0;
            bool has = bibles.Count > 0;
            bibleSearch.Enabled = has;
            bibleTree.Enabled = has;
        }

        InstalledBible CurrentBible()
        {
            return bibleVersion != null ? bibleVersion.SelectedItem as InstalledBible : null;
        }

        void RefreshBibleTree()
        {
            if (bibleTree == null) return;
            bibleTree.Nodes.Clear();
            foreach (var b in BookNames.Books)
            {
                var node = new TreeNode(b.Name) { Tag = b.Name + " 1" };
                bibleTree.Nodes.Add(node);
            }
        }

        void BibleSearchChanged()
        {
            var b = CurrentBible();
            string q = bibleSearch.Text;
            if (b == null || q == "Cita o palabra (G)…" || q.Trim().Length < 2) { bibleResults.Items.Clear(); return; }
            var hits = Live.Bibles.Search(b, q.Trim(), 30);
            bibleResults.Items.Clear();
            foreach (var h in hits) bibleResults.Items.Add(new BibleHit { Cite = h.Key, Text = h.Value });
        }

        class BibleHit
        {
            public string Cite, Text;
        }

        void BibleResultsDraw(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var h = bibleResults.Items[e.Index] as BibleHit;
            if (h == null) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(sel ? UiTheme.AccentSoft : Color.White))
                e.Graphics.FillRectangle(bg, e.Bounds);
            using (var b = new SolidBrush(UiTheme.AccentDark))
                e.Graphics.DrawString(h.Cite, UiTheme.NormalBold(), b, e.Bounds.X + 6, e.Bounds.Y + 2);
            string t = h.Text.Length > 64 ? h.Text.Substring(0, 64) + "…" : h.Text;
            using (var b = new SolidBrush(UiTheme.TextDim))
                e.Graphics.DrawString(t, UiTheme.Small(), b, e.Bounds.X + 6, e.Bounds.Y + 19);
        }

        void BibleGoResult()
        {
            var h = bibleResults.SelectedItem as BibleHit;
            if (h != null) BibleGoRef(h.Cite);
        }

        void BibleGoRef(string reference)
        {
            var b = CurrentBible();
            if (b == null) return;
            var refr = BibleReference.Parse(reference);
            if (!refr.Valid) return;
            var verses = Live.Bibles.GetPassage(b, refr);
            if (verses.Count == 0) return;
            var scn = new Scenario { Title = refr.ToString() };
            // Agrupar versos en bloques de hasta 4 líneas [SPEC §5.2 #2]
            for (int i = 0; i < verses.Count; i += 3)
            {
                var el = new Element { Kind = ElementKind.Verse, Reference = refr.ToString() };
                for (int k = i; k < i + 3 && k < verses.Count; k++) el.Lines.Add(verses[k]);
                scn.Elements.Add(el);
            }
            Live.SendToLive(scn);
            SetPresentMode();
        }

        void FocusBibleSearch()
        {
            SetPresentMode();
            libTabs.SelectedIndex = 1;
            bibleSearch.Focus();
            bibleSearch.SelectAll();
        }

        // ------------------------------------------------------------ escenarios
        void RefreshScenarios()
        {
            if (scenariosList == null) return;
            scenariosList.Items.Clear();
            if (Live.Project == null) return;
            foreach (var s in Live.Project.Scenarios) scenariosList.Items.Add(s);
        }

        Scenario SelectedScenario()
        {
            return scenariosList != null ? scenariosList.SelectedItem as Scenario : null;
        }

        // ------------------------------------------------------------ medios
        void RefreshMedia()
        {
            if (mediaList == null) return;
            mediaList.Items.Clear();
            try
            {
                Directory.CreateDirectory(Settings.MediaPath);
                foreach (var f in Directory.GetFiles(Settings.MediaPath))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif" || ext == ".bmp" ||
                        ext == ".mp4" || ext == ".avi" || ext == ".wmv" || ext == ".mov")
                        mediaList.Items.Add(new MediaItem { Path = f, Name = Path.GetFileName(f) });
                }
            }
            catch { }
        }

        class MediaItem
        {
            public string Path, Name;
            public bool IsVideo { get { var e = System.IO.Path.GetExtension(Path).ToLowerInvariant(); return e == ".mp4" || e == ".avi" || e == ".wmv" || e == ".mov"; } }
            public override string ToString() { return (IsVideo ? "▶ " : "🖼 ") + Name; }
        }

        void AddMedia()
        {
            using (var d = new OpenFileDialog())
            {
                d.Title = "Añadir imágenes o videos";
                d.Filter = "Imágenes y videos|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.mp4;*.avi;*.wmv;*.mov|Todos (*.*)|*.*";
                d.Multiselect = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Directory.CreateDirectory(Settings.MediaPath);
                    foreach (var f in d.FileNames)
                        File.Copy(f, Path.Combine(Settings.MediaPath, Path.GetFileName(f)), true);
                    RefreshMedia();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "No se pudo copiar el archivo.\n\n" + ex.Message,
                        "Fusion HP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        void SendSelectedMedia()
        {
            var m = mediaList != null ? mediaList.SelectedItem as MediaItem : null;
            if (m == null) return;
            var scn = new Scenario { Title = Path.GetFileNameWithoutExtension(m.Path) };
            scn.Elements.Add(new Element
            {
                Kind = m.IsVideo ? ElementKind.Video : ElementKind.Image,
                Src = m.Path
            });
            Live.SendToLive(scn);
            SetPresentMode();
        }

        void ImportBible()
        {
            using (var d = new OpenFileDialog())
            {
                d.Title = "Importar Biblia (Zefania XML · e-Sword .bib/.bblx · JSON · TSV)";
                d.Filter = "Biblias|*.xml;*.bib;*.bblx;*.bibx;*.json;*.txt|Todos (*.*)|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var rep = Live.Bibles.ImportAny(d.FileName);
                    string msg = rep.Ok
                        ? "Biblia importada: " + rep.Books + " libros, " + rep.Verses + " versículos."
                        : "No se pudo importar la Biblia.";
                    foreach (var w in rep.Warnings) msg += "\n• " + w;
                    MessageBox.Show(this, msg, "Fusion HP", MessageBoxButtons.OK,
                        rep.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Information);
                    RefreshBibles();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "La importación falló.\n\nDetalle técnico (copiable): " + ex.Message,
                        "Fusion HP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
    }
}
