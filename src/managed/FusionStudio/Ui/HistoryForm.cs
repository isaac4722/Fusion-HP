// ============================================================================
//  Fusion-HP · FusionStudio/Ui/HistoryForm.cs — HISTORIAL (función beta-1,
//  v1.6 HistoryPanel): canciones/elementos más usados, uso reciente y
//  exportación CSV. Comparte historial.jsonl con el estudio nativo C++.
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using Fusion.Shared;
using Fusion.Studio.Ui.Widgets;

namespace Fusion.Studio.Ui
{
    public class HistoryForm : Form
    {
        ListView top, recent;
        Timer timer;

        public HistoryForm(MainForm owner)
        {
            Text = "Historial de uso";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(760, 560);
            MinimumSize = new Size(600, 420);
            Font = UiTheme.Normal();
            BackColor = UiTheme.Bg;

            var lblTop = new Label { Text = "MÁS USADAS", Dock = DockStyle.Top, Height = 26, Font = UiTheme.SmallBold(),
                                     ForeColor = UiTheme.TextDim, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };
            top = MkList();
            var lblRec = new Label { Text = "USO RECIENTE", Dock = DockStyle.Top, Height = 26, Font = UiTheme.SmallBold(),
                                     ForeColor = UiTheme.TextDim, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };
            recent = MkList();

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                                             SplitterDistance = 250, BackColor = UiTheme.Bg };
            var p1 = new Panel { Dock = DockStyle.Fill };
            p1.Controls.Add(top); p1.Controls.Add(lblTop);
            var p2 = new Panel { Dock = DockStyle.Fill };
            p2.Controls.Add(recent); p2.Controls.Add(lblRec);
            split.Panel1.Controls.Add(p1);
            split.Panel2.Controls.Add(p2);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = UiTheme.Bg };
            var btnCsv = new FusionButton { Text = "Exportar CSV", IconName = "download", Kind = FusionButtonKind.Primary,
                                            Location = new Point(12, 8), Size = new Size(150, 32) };
            btnCsv.Click += delegate { ExportCsv(); };
            var btnClose = new FusionButton { Text = "Cerrar", IconName = "x", Kind = FusionButtonKind.Chip,
                                              Location = new Point(640, 8), Size = new Size(96, 32) };
            btnClose.Click += delegate { Close(); };
            bottom.Controls.Add(btnCsv); bottom.Controls.Add(btnClose);
            bottom.Resize += delegate { btnClose.Left = bottom.Width - btnClose.Width - 12; };

            Controls.Add(split);
            Controls.Add(bottom);

            timer = new Timer { Interval = 2000 };
            timer.Tick += delegate { LoadData(); };
            timer.Start();
            LoadData();
        }

        static ListView MkList()
        {
            var lv = new ListView
            {
                View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None,
                GridLines = false, MultiSelect = false, HideSelection = true,
                Font = UiTheme.Normal(), BackColor = Color.White
            };
            lv.Columns.Add("#", 40);
            lv.Columns.Add("Título", 420);
            lv.Columns.Add("Veces", 70);
            return lv;
        }

        void LoadData()
        {
            if (IsDisposed) return;
            top.BeginUpdate(); recent.BeginUpdate();
            try
            {
                top.Items.Clear();
                int i = 1;
                foreach (var c in UsageHistory.Top(40))
                    top.Items.Add(new ListViewItem(new[] { (i++).ToString(), c.Title, c.Count.ToString() }));
                recent.Items.Clear();
                recent.Columns[2].Text = "Cuándo";
                var recentList = UsageHistory.Recent(60);
                var ep = new DateTime(1970, 1, 1);
                int j = 1;
                foreach (var e in recentList)
                    recent.Items.Add(new ListViewItem(new[] { (j++).ToString(), e.Title,
                        ep.AddMilliseconds(e.T).ToLocalTime().ToString("dd/MM HH:mm") }));
                var empty = top.Items.Count == 0;
                if (empty)
                {
                    var it = new ListViewItem(new[] { "", "Sin uso registrado todavía — proyecta algo primero", "" });
                    it.ForeColor = UiTheme.TextDim;
                    top.Items.Add(it);
                }
            }
            finally { top.EndUpdate(); recent.EndUpdate(); }
        }

        void ExportCsv()
        {
            using (var d = new SaveFileDialog())
            {
                d.Filter = "CSV (*.csv)|*.csv";
                d.FileName = "historial.csv";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (UsageHistory.ExportCsv(d.FileName))
                    MessageBox.Show(this, "Historial exportado a:\n" + d.FileName, "Fusion HP",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show(this, "No se pudo exportar el historial.", "Fusion HP",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (timer != null) timer.Stop();
            base.OnFormClosed(e);
        }
    }
}
