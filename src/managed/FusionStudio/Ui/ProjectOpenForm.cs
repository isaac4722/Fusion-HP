// ============================================================================
//  Fusion-HP · FusionStudio/Ui/ProjectOpenForm.cs — CARGADOR DE ESCENARIOS
//  con nombres [v3.0.0 — bug «el cargador de Escenarios no muestra nombres»].
//  Antes: un OpenFileDialog crudo a ciegas. Ahora:
//   · lista de proyectos RECIENTES con su NOMBRE y N escenarios (icono y ruta);
//   · «Examinar…» permite elegir un .ahp y muestra sus escenarios antes de abrir;
//   · doble clic o botón «Abrir» carga el proyecto y va a Presentación.
//  Los títulos provienen de AhpProjectInfo (lectura de cabecera ahp.v1).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Fusion.Shared;
using Fusion.Shared.Model;
using Fusion.Studio.Ui.Widgets;

namespace Fusion.Studio.Ui
{
    public class ProjectOpenForm : Form
    {
        readonly AppSettings settings;
        ListBox list;
        ListBox preview;
        Label lblPreview;
        string browsed;                     // último archivo examinado (aunque no se abra)
        FusionButton btnOpen;

        /// <summary>Ruta elegida si el diálogo termina en OK.</summary>
        public string SelectedPath { get; private set; }

        public ProjectOpenForm(AppSettings s)
        {
            settings = s;
            Text = "Abrir proyecto";
            Size = new Size(620, 540);
            StartPosition = FormStartPosition.CenterParent;
            Font = UiTheme.Normal();
            BackColor = UiTheme.Panel;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;

            var lbl = new Label { Text = "Recientes", Font = UiTheme.NormalBold(), ForeColor = UiTheme.AccentDark,
                                  Location = new Point(16, 14), AutoSize = true };
            Controls.Add(lbl);

            list = new ListBox
            {
                Location = new Point(16, 40), Size = new Size(572, 200),
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 44,
                BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false
            };
            list.DrawItem += RecentDraw;
            list.SelectedIndexChanged += delegate { ShowPreview(list.SelectedItem as RecentItem); };
            list.DoubleClick += delegate { OpenSelected(); };
            Controls.Add(list);

            var btnBrowse = new FusionButton { Text = "Examinar…", IconName = "folder-open",
                                               Location = new Point(16, 252), Size = new Size(140, 34) };
            btnBrowse.Click += delegate { Browse(); };
            Controls.Add(btnBrowse);

            var lblPrev = new Label { Text = "Escenarios del proyecto", Font = UiTheme.NormalBold(),
                                      ForeColor = UiTheme.AccentDark, Location = new Point(16, 300), AutoSize = true };
            Controls.Add(lblPrev);

            lblPreview = new Label { Text = "Selecciona un proyecto para ver sus escenarios.",
                                     Font = UiTheme.Small(), ForeColor = UiTheme.TextDim,
                                     Location = new Point(16, 324), AutoSize = true };
            Controls.Add(lblPreview);

            preview = new ListBox
            {
                Location = new Point(16, 348), Size = new Size(572, 110),
                BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false
            };
            Controls.Add(preview);

            btnOpen = new FusionButton { Text = "Abrir", IconName = "check", Kind = FusionButtonKind.Primary,
                                         Location = new Point(488, 466), Size = new Size(100, 34), Enabled = false };
            btnOpen.Click += delegate { OpenSelected(); };
            Controls.Add(btnOpen);

            var btnCancel = new FusionButton { Text = "Cancelar", IconName = "x",
                                               Location = new Point(382, 466), Size = new Size(98, 34) };
            btnCancel.Click += delegate { Close(); };
            Controls.Add(btnCancel);

            LoadRecents();
        }

        class RecentItem
        {
            public string Path;
            public string Name;
            public int Scenarios;
            public bool Readable;
        }

        void LoadRecents()
        {
            list.Items.Clear();
            var seen = new System.Collections.Generic.List<string>();
            Action<string> add = delegate(string p)
            {
                if (string.IsNullOrEmpty(p) || !File.Exists(p) || seen.Contains(p)) return;
                seen.Add(p);
                var it = new RecentItem { Path = p, Readable = false };
                try
                {
                    var info = AhpProjectInfo.ReadHeader(p);
                    it.Name = string.IsNullOrEmpty(info.Name) ? Path.GetFileNameWithoutExtension(p) : info.Name;
                    it.Scenarios = info.ScenarioCount;
                    it.Readable = true;
                }
                catch
                {
                    it.Name = Path.GetFileNameWithoutExtension(p) + "  (ilegible)";
                }
                list.Items.Add(it);
            };
            add(settings.LastProjectPath);
            foreach (string p in settings.RecentProjects) add(p);
            if (list.Items.Count == 0)
            {
                list.Items.Add(new RecentItem { Path = null, Name = "Aún no hay proyectos recientes.", Readable = false });
                list.Enabled = false;
            }
        }

        void RecentDraw(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var it = list.Items[e.Index] as RecentItem;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(selected ? UiTheme.AccentSoft : Color.White))
                e.Graphics.FillRectangle(b, e.Bounds);
            using (var b = new SolidBrush(it != null && it.Readable ? UiTheme.Accent : UiTheme.Border))
                e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y, 4, e.Bounds.Height);
            if (it == null) return;
            using (var b = new SolidBrush(UiTheme.Text))
                TextRenderer.DrawText(e.Graphics, it.Name, UiTheme.NormalBold(),
                    new Rectangle(e.Bounds.X + 14, e.Bounds.Y + 4, e.Bounds.Width - 24, 20),
                    b.Color, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            string sub = it.Path == null ? "" :
                (it.Readable ? it.Scenarios + " escenario(s)  ·  " : "No se pudo leer  ·  ") + it.Path;
            using (var b = new SolidBrush(UiTheme.TextDim))
                TextRenderer.DrawText(e.Graphics, sub, UiTheme.Small(),
                    new Rectangle(e.Bounds.X + 14, e.Bounds.Y + 24, e.Bounds.Width - 24, 16),
                    b.Color, TextFormatFlags.Left | TextFormatFlags.PathEllipsis);
        }

        void ShowPreview(RecentItem it)
        {
            preview.Items.Clear();
            btnOpen.Enabled = it != null && it.Readable;
            if (it == null || !it.Readable)
            {
                lblPreview.Text = "Selecciona un proyecto para ver sus escenarios.";
                return;
            }
            try
            {
                var info = AhpProjectInfo.ReadHeader(it.Path);
                lblPreview.Text = "«" + info.Name + "» — " + info.ScenarioCount + " escenario(s):";
                foreach (string t in info.ScenarioTitles) preview.Items.Add(t);
            }
            catch (Exception ex)
            {
                lblPreview.Text = "No se pudo leer el proyecto.";
                preview.Items.Add(ex.Message);
                btnOpen.Enabled = false;
            }
        }

        void Browse()
        {
            using (var d = new OpenFileDialog())
            {
                d.Filter = "Proyecto Fusion-HP (*.ahp;*.json)|*.ahp;*.json";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                browsed = d.FileName;
                var it = new RecentItem { Path = d.FileName };
                try
                {
                    var info = AhpProjectInfo.ReadHeader(d.FileName);
                    it.Name = string.IsNullOrEmpty(info.Name) ? Path.GetFileNameWithoutExtension(d.FileName) : info.Name;
                    it.Scenarios = info.ScenarioCount;
                    it.Readable = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        "No se pudo leer el proyecto.\n\nQué puedes hacer: verifica que sea un .ahp guardado por " +
                        "Fusion HP y que el archivo no esté dañado.\n\n" + ex.Message,
                        "Fusion HP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    it.Name = Path.GetFileNameWithoutExtension(d.FileName) + "  (ilegible)";
                }
                list.Enabled = true;
                list.Items.Insert(0, it);
                list.SelectedIndex = 0;
            }
        }

        void OpenSelected()
        {
            var it = list.SelectedItem as RecentItem;
            if (it == null || !it.Readable) return;
            SelectedPath = it.Path;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
