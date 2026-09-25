// ============================================================================
//  Fusion-HP · MainForm.Home.cs — modo Inicio (mosaicos, como la referencia)
//  y modo Estudio (editor de escenarios embebido). El editor usa el MISMO
//  motor de render para previsualizar [SPEC §7.4.2] y no bloquea el modo
//  Presentación [SPEC §7.5.3].
// ============================================================================
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Fusion.Shared;
using Fusion.Shared.Model;
using Fusion.Studio.Import;
using Fusion.Studio.Export;

namespace Fusion.Studio.Ui
{
    public partial class MainForm
    {
        void BuildHome()
        {
            homePanel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg, Visible = false };
            content.Controls.Add(homePanel);

            var title = new Label { Text = "¿Qué vas a hacer hoy?", Font = UiTheme.Title(), ForeColor = UiTheme.Text,
                                    Location = new Point(40, 36), AutoSize = true };
            homePanel.Controls.Add(title);
            homePanel.Resize += delegate
            {
                RepositionTiles();
            };

            AddTile("Nuevo proyecto", "Crea un servicio desde cero", "layout-grid", delegate
            {
                Live.Project = AhpProject.CreateDefault();
                Live.Project.Name = "Culto " + DateTime.Now.ToString("dd/MM");
                RefreshLibrary();
                RefreshProgram();
                SetMode(Mode.Studio);
            }, 0);

            AddTile("Abrir proyecto", "Recientes con nombres y escenarios", "folder-open", delegate
            {
                // [v3.0.0 — bug «el cargador de Escenarios no muestra nombres»]
                // Diálogo dedicado: recientes con nombre, N escenarios y títulos
                // visibles antes de abrir.
                using (var f = new ProjectOpenForm(Settings))
                {
                    if (f.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(f.SelectedPath))
                    {
                        if (Live.LoadProject(f.SelectedPath))
                        {
                            RefreshLibrary();
                            RefreshProgram();
                            SetMode(Mode.Present);
                        }
                    }
                }
            }, 0);

            AddTile("Cargar PPTX original", "Proyecta el archivo tal cual (COM o nativo)", "movie", delegate
            {
                ImportPptxAsIs();
            }, 1);

            AddTile("Importar PPTX a Escenarios", "Convierte diapositivas al modelo ahp.v1", "stack-2", delegate
            {
                ImportPptxConvert();
            }, 1);

            AddTile("Importar Biblia", "Zefania XML · e-Sword .bib · JSON", "book", delegate
            {
                ImportBible();
            }, 2);

            AddTile("Importar cantos", "Himnario JSON o respaldo Holyrics", "music", delegate
            {
                ImportSongs();
            }, 2);

            AddTile("Exportar proyecto", "PPTX · PDF · imágenes", "download", delegate
            {
                ExportProject();
            }, 3);
        }

        void AddTile(string title, string subtitle, string icon, EventHandler onClick, int column)
        {
            var tile = new Panel { Size = new Size(250, 112), BackColor = UiTheme.Panel,
                                   Cursor = Cursors.Hand, Tag = column };
            tile.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var pen = new Pen(UiTheme.ChipBorder))
                    e.Graphics.DrawRectangle(pen, 0, 0, tile.Width - 1, tile.Height - 1);
                using (var b = new SolidBrush(UiTheme.Accent))
                    e.Graphics.FillRectangle(b, 0, 0, 4, tile.Height);
                Fusion.Studio.Ui.Widgets.UiIcons.Draw(e.Graphics,
                    Fusion.Studio.Ui.Widgets.UiIcons.Get32(icon), 18, 14);
            };
            var t = new Label { Text = title, Font = UiTheme.NormalBold(), ForeColor = UiTheme.Text,
                                Location = new Point(60, 14), AutoSize = true };
            var sub = new Label { Text = subtitle, Font = UiTheme.Small(), ForeColor = UiTheme.TextDim,
                                  Location = new Point(60, 40), Size = new Size(178, 44) };
            tile.Controls.Add(sub); tile.Controls.Add(t);
            tile.Click += onClick;
            foreach (Control c in tile.Controls) c.Click += delegate { onClick(null, null); };
            tile.MouseEnter += delegate { tile.BackColor = UiTheme.AccentSoft; };
            tile.MouseLeave += delegate { tile.BackColor = UiTheme.Panel; };
            homePanel.Controls.Add(tile);
        }

        void RepositionTiles()
        {
            int x = 40, y = 90;
            int col = -1;
            foreach (Control c in homePanel.Controls)
            {
                if (!(c is Panel) || c.Width != 250 || c.Height != 112) continue;
                int thisCol = (int)c.Tag;
                if (thisCol != col) { if (col >= 0) { y += 126; x = 40; } col = thisCol; }
                c.Location = new Point(x, y);
                x += 264;
            }
        }

        // ------------------------------------------------------------ estudio
        void BuildStudio()
        {
            studioPanel = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg, Visible = false };
            content.Controls.Add(studioPanel);
        }

        void ActivateEditor()
        {
#if !LITE
            if (editorHost == null || editorHost.IsDisposed)
            {
                var host = new System.Windows.Forms.Integration.ElementHost();
                host.Dock = DockStyle.Fill;
                var editor = new Fusion.Studio.Editor.Wpf.ScenarioEditorControl();
                editor.RequestSendToLive += delegate(Scenario scn) { Live.SendToLive(scn); SetPresentMode(); };
                host.Child = editor;
                editorHost = host;
                studioPanel.Controls.Add(host);
                studioPanel.BringToFront();
            }
            var ed = FindEditor();
            if (ed != null) ed.BindProject(Live.Project, Live);
#else
            if (editorHost == null || editorHost.IsDisposed)
            {
                var canvas = new Fusion.Studio.Editor.WinForms.ScenarioCanvas();
                canvas.Dock = DockStyle.Fill;
                canvas.RequestSendToLive += delegate(Fusion.Shared.Model.Scenario scn) { Live.SendToLive(scn); SetPresentMode(); };
                editorHost = canvas;
                studioPanel.Controls.Add(canvas);
            }
            var wf = editorHost as Fusion.Studio.Editor.WinForms.ScenarioCanvas;
            if (wf != null) wf.BindProject(Live.Project, Live);
#endif
        }

#if !LITE
        Editor.Wpf.ScenarioEditorControl FindEditor()
        {
            var host = editorHost as System.Windows.Forms.Integration.ElementHost;
            return host != null ? host.Child as Editor.Wpf.ScenarioEditorControl : null;
        }
#endif

        // ------------------------------------------------------------ PPTX (v3.0.0)
        /// <summary>Carga el PPTX ORIGINAL y lo proyecta tal cual. Cadena de
        /// fidelidad: (1) PowerPoint vía COM si está instalado; (2) proyección
        /// NATIVA leyendo el archivo original (System.IO.Packaging → Motor), sin
        /// convertir ni guardar nada — ya NO exige PowerPoint [bug del prototipo:
        /// «extrae en vez de cargar el original»].</summary>
        void ImportPptxAsIs()
        {
            using (var d = new OpenFileDialog())
            {
                d.Filter = "PowerPoint (*.pptx;*.pptm;*.ppt)|*.pptx;*.pptm;*.ppt";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var loader = new PptxComLoader();
                if (loader.PowerPointAvailable())
                {
                    try
                    {
                        loader.PresentOriginal(d.FileName, Settings.PublicMonitor);
                        SetMode(Mode.Present);
                        return;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this,
                            "PowerPoint no pudo abrir la presentación (" + ex.Message + ").\n\n" +
                            "Se intentará con el proyector nativo de Fusion HP, que lee el archivo original.",
                            "Fusion HP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                // Vía nativa: el ORIGINAL tal cual, sin PowerPoint.
                if (string.Equals(Path.GetExtension(d.FileName), ".ppt", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this,
                        "El formato .ppt (binario antiguo) requiere PowerPoint instalado para proyectarse.\n\n" +
                        "Qué puedes hacer: guárdalo como .pptx desde PowerPoint, o instala PowerPoint y vuelve a intentarlo.",
                        "Fusion HP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try
                {
                    var rep = PptxDirectProjector.ProjectOriginal(d.FileName, Live);
                    RefreshLibrary();
                    RefreshProgram();
                    SetMode(Mode.Present);
                    string msg = "PPTX original en vivo: " + rep.Slides + " diapositiva(s).";
                    if (rep.Warnings.Count > 0) msg += "\n\nInforme de fidelidad:";
                    foreach (var w in rep.Warnings) msg += "\n• " + w;
                    MessageBox.Show(this, msg, "Fusion HP — PPTX original",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        "No se pudo proyectar el archivo PPTX.\n\nQué puedes hacer: verifica que el archivo " +
                        "no esté dañado y que sea un .pptx/.pptm válido, o usa «Importar PPTX a Escenarios».\n\n" + ex.Message,
                        "Fusion HP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        void ImportPptxConvert()
        {
            using (var d = new OpenFileDialog())
            {
                d.Filter = "PowerPoint (*.pptx;*.pptm)|*.pptx;*.pptm";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var importer = new PptxOpenXmlImporter();
                    var report = importer.Import(d.FileName, Live.Project != null ? Live.Project : (Live.Project = AhpProject.CreateDefault()));
                    RefreshLibrary();
                    RefreshProgram();
                    string msg = "Diapositivas importadas: " + report.Scenarios + "." +
                                 (report.Warnings.Count > 0 ? "\n\nInforme de fidelidad:" : "");
                    foreach (var w in report.Warnings) msg += "\n• " + w;
                    MessageBox.Show(this, msg, "Fusion HP — importación PPTX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    SetMode(Mode.Studio);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this,
                        "No se pudo importar la presentación.\n\nQué puedes hacer: verifica que el archivo sea un " +
                        ".pptx válido (no dañado) y vuelve a intentarlo.\n\n" + ex.Message,
                        "Fusion HP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        void ExportProject()
        {
            if (Live.Project == null || Live.Project.Scenarios.Count == 0)
            {
                MessageBox.Show(this, "No hay escenarios para exportar. Añade cantos o elementos primero.",
                    "Fusion HP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var d = new SaveFileDialog())
            {
                d.Filter = "PowerPoint (*.pptx)|*.pptx|PDF (*.pdf)|*.pdf|Imágenes PNG (*.png)|*.png";
                d.FileName = Path.ChangeExtension(PathSafe(Live.Project.Name), ".pptx");
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string ext = Path.GetExtension(d.FileName).ToLowerInvariant();
                    if (ext == ".pptx")
                    {
                        var rep = new PptxExporter().Export(Live.Project, d.FileName, BaseDirForExport());
                        MessageBox.Show(this, "PPTX generado.\nDiapositivas: " + rep.Slides + "\n" +
                            (rep.Warnings.Count > 0 ? "Informe de fidelidad:\n• " + string.Join("\n• ", rep.Warnings.ToArray()) : ""),
                            "Fusion HP — exportación", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else if (ext == ".pdf")
                    {
                        int pages = new PdfExporter().Export(Live.Project, d.FileName, BaseDirForExport());
                        MessageBox.Show(this, "PDF generado con " + pages + " páginas.",
                            "Fusion HP — exportación", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else if (ext == ".png")
                    {
                        int n = new ImageExporter().Export(Live.Project, d.FileName, BaseDirForExport());
                        MessageBox.Show(this, "Imágenes generadas: " + n + " (una por elemento).",
                            "Fusion HP — exportación", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "La exportación falló.\n\n" + ex.Message,
                        "Fusion HP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        string BaseDirForExport()
        {
            return Live.Project != null && !string.IsNullOrEmpty(Live.Project.SourcePath)
                ? Path.GetDirectoryName(Live.Project.SourcePath)
                : Settings.ProjectsPath;
        }

        static string PathSafe(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_') sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "proyecto";
        }
    }
}
