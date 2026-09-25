// ============================================================================
//  Fusion-HP · ScenarioEditorControl.xaml.cs — Modo Creación WPF [SPEC §7]:
// lienzo libre 16:9 con elementos posicionables por arrastre, edición directa
// de texto (doble clic), panel de propiedades con herencia visible y envío a
// la salida en caliente sin salir del editor [SPEC §7.5.1].
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Fusion.Shared.Model;

namespace Fusion.Studio.Editor.Wpf
{
    public partial class ScenarioEditorControl : UserControl
    {
        AhpProject project;
        Fusion.Studio.Services.LiveOrchestrator live;
        Scenario current;
        Element selected;
        bool syncing;
        Border selectionFrame;
        TextBox inlineEditor;

        public event Action<Scenario> RequestSendToLive;

        class ScenarioListItem
        {
            public string Title { get; set; }
            public string Subtitle { get; set; }
            public Scenario Scenario { get; set; }
        }

        public ScenarioEditorControl()
        {
            InitializeComponent();
            Loaded += delegate
            {
                var fonts = new[] { "Segoe UI", "Arial", "Calibri", "Georgia", "Times New Roman", "Trebuchet MS", "Verdana" };
                cmbFont.ItemsSource = fonts;
                cmbFont.SelectedIndex = 0;
            };
            canvas.MouseLeftButtonDown += OnCanvasDown;
        }

        public void BindProject(AhpProject project, Fusion.Studio.Services.LiveOrchestrator live)
        {
            this.project = project;
            this.live = live;
            RefreshScenarioList();
        }

        void RefreshScenarioList()
        {
            syncing = true;
            lstScenarios.Items.Clear();
            if (project != null)
            {
                for (int i = 0; i < project.Scenarios.Count; i++)
                {
                    var s = project.Scenarios[i];
                    lstScenarios.Items.Add(new ScenarioListItem
                    {
                        Title = (i + 1) + ". " + s.Title,
                        Subtitle = s.Elements.Count + " elemento(s)",
                        Scenario = s
                    });
                }
            }
            if (lstScenarios.Items.Count > 0) lstScenarios.SelectedIndex = 0;
            syncing = false;
            if (lstScenarios.SelectedItem == null) SelectScenario(null);
            else SelectScenario(((ScenarioListItem)lstScenarios.SelectedItem).Scenario);
        }

        void OnScenarioSelected(object sender, SelectionChangedEventArgs e)
        {
            if (syncing) return;
            var it = lstScenarios.SelectedItem as ScenarioListItem;
            SelectScenario(it != null ? it.Scenario : null);
        }

        void SelectScenario(Scenario scn)
        {
            current = scn;
            selected = null;
            RebuildCanvas();
            SyncProperties();
        }

        // ------------------------------------------------------------ lienzo
        void RebuildCanvas()
        {
            canvas.Children.Clear();
            selectionFrame = null;
            inlineEditor = null;
            if (current == null) return;

            // Fondo del lienzo según tema (mismo motor de render [SPEC §7.4.2])
            var theme = project != null ? project.ActiveTheme() : null;
            if (theme != null && !string.IsNullOrEmpty(theme.Background.Color))
            {
                canvas.Background = new SolidColorBrush(Parse(theme.Background.Color));
            }

            foreach (var el in current.Elements)
                canvas.Children.Add(MakeElementVisual(el));
        }

        Border MakeElementVisual(Element el)
        {
            var b = new Border();
            Canvas.SetLeft(b, el.X * canvas.Width);
            Canvas.SetTop(b, el.Y * canvas.Height);
            b.Width = el.W * canvas.Width;
            b.Height = el.H * canvas.Height;
            b.BorderBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0x3E, 0x1C));
            b.BorderThickness = new Thickness(1);
            b.Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255));
            b.Tag = el;
            b.CornerRadius = new CornerRadius(2);

            // contenido textual del elemento (primera línea como título)
            string preview = el.Lines.Count > 0 ? el.Lines[0] :
                             el.Kind == ElementKind.Verse ? (el.Reference ?? "Versículo") :
                             el.Kind == ElementKind.Image ? "🖼 " + System.IO.Path.GetFileName(el.Src ?? "") :
                             el.Kind == ElementKind.Video ? "▶ " + System.IO.Path.GetFileName(el.Src ?? "") :
                             el.Kind == ElementKind.LowerThird ? "▂ " + (el.OverlayText ?? "zócalo") : "Texto";
            var tb = new TextBlock
            {
                Text = preview,
                Foreground = Brushes.White,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Padding = new Thickness(6)
            };
            b.Child = tb;

            // interacción: arrastre + selección + doble clic edición directa
            bool dragging = false;
            Point origin = new Point();
            double origX = 0, origY = 0;
            b.MouseLeftButtonDown += (s, e2) =>
            {
                e2.Handled = true;
                SelectElement(el);
                dragging = true;
                origin = e2.GetPosition(canvas);
                origX = el.X; origY = el.Y;
                b.CaptureMouse();
            };
            b.MouseMove += (s, e2) =>
            {
                if (!dragging) return;
                var p = e2.GetPosition(canvas);
                double nx = origX + (p.X - origin.X) / canvas.Width;
                double ny = origY + (p.Y - origin.Y) / canvas.Height;
                el.X = Math.Max(0, Math.Min(0.95, nx));
                el.Y = Math.Max(0, Math.Min(0.95, ny));
                Canvas.SetLeft(b, el.X * canvas.Width);
                Canvas.SetTop(b, el.Y * canvas.Height);
            };
            b.MouseLeftButtonUp += (s, e2) =>
            {
                dragging = false;
                b.ReleaseMouseCapture();
                SyncProperties();
            };
            b.MouseDoubleClick += (s, e2) => StartInlineEdit(el, b);
            return b;
        }

        void OnCanvasDown(object sender, MouseButtonEventArgs e)
        {
            SelectElement(null);
        }

        void SelectElement(Element el)
        {
            selected = el;
            // marco de selección
            if (selectionFrame != null) canvas.Children.Remove(selectionFrame);
            selectionFrame = null;
            if (el != null)
            {
                selectionFrame = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0x3E, 0x1C)),
                    BorderThickness = new Thickness(2),
                    Width = el.W * canvas.Width + 8,
                    Height = el.H * canvas.Height + 8,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(selectionFrame, el.X * canvas.Width - 4);
                Canvas.SetTop(selectionFrame, el.Y * canvas.Height - 4);
                canvas.Children.Add(selectionFrame);
            }
            SyncProperties();
        }

        void StartInlineEdit(Element el, Border host)
        {
            if (inlineEditor != null) { canvas.Children.Remove(inlineEditor); inlineEditor = null; }
            if (el.Lines.Count == 0 && el.Kind != ElementKind.LowerThird) return;
            inlineEditor = new TextBox
            {
                Text = el.Kind == ElementKind.LowerThird ? (el.OverlayText ?? "") : string.Join("\n", el.Lines.ToArray()),
                AcceptsReturn = true,
                Width = host.Width,
                Height = host.Height,
                FontSize = 14,
                Background = new SolidColorBrush(Color.FromArgb(230, 32, 32, 32)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0x3E, 0x1C))
            };
            Canvas.SetLeft(inlineEditor, Canvas.GetLeft(host));
            Canvas.SetTop(inlineEditor, Canvas.GetTop(host));
            canvas.Children.Add(inlineEditor);
            inlineEditor.Focus();
            inlineEditor.LostFocus += (s, e2) => CommitInline(el);
            inlineEditor.KeyDown += (s, e2) =>
            {
                if (e2.Key == Key.Escape) { canvas.Children.Remove(inlineEditor); inlineEditor = null; }
                else if (e2.Key == Key.Enter && (e2.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0)
                    CommitInline(el);
            };
        }

        void CommitInline(Element el)
        {
            if (inlineEditor == null) return;
            string text = inlineEditor.Text;
            canvas.Children.Remove(inlineEditor);
            inlineEditor = null;
            if (el.Kind == ElementKind.LowerThird) el.OverlayText = text.Trim();
            else
            {
                el.Lines.Clear();
                foreach (var ln in text.Split(new[] { '\n' }))
                {
                    var t = ln.Trim();
                    if (t.Length > 0) el.Lines.Add(t);
                }
            }
            RebuildCanvas();
            SelectElement(el);
        }

        // ------------------------------------------------------------ propiedades
        void SyncProperties()
        {
            syncing = true;
            bool has = selected != null;
            txtLines.Text = has && selected.Kind != ElementKind.LowerThird
                ? string.Join("\n", selected.Lines.ToArray())
                : has ? (selected.OverlayText ?? "") : "";
            if (has)
            {
                sldX.Value = selected.X; sldY.Value = selected.Y;
                sldW.Value = selected.W; sldH.Value = selected.H;
                sldSize.Value = selected.StyleOverride.Size ?? 48;
                chkBold.IsChecked = selected.StyleOverride.Bold ?? false;
                chkItalic.IsChecked = selected.StyleOverride.Italic ?? false;
                chkShadow.IsChecked = selected.StyleOverride.Shadow ?? true;
                txtColor.Text = selected.StyleOverride.Color ?? "#FFFFFF";
                txtActiveColor.Text = selected.StyleOverride.ActiveColor ?? "#FFD700";
                txtTags.Text = string.Join(", ", selected.Tags.ToArray());
                var f = selected.StyleOverride.Font;
                cmbFont.SelectedItem = f != null && ((string[])cmbFont.ItemsSource).Contains(f) ? f : "Segoe UI";
            }
            EnableProperties(has);
            syncing = false;
        }

        void EnableProperties(bool on)
        {
            txtLines.IsEnabled = on; sldX.IsEnabled = on; sldY.IsEnabled = on;
            sldW.IsEnabled = on; sldH.IsEnabled = on; sldSize.IsEnabled = on;
            chkBold.IsEnabled = on; chkItalic.IsEnabled = on; chkShadow.IsEnabled = on;
            txtColor.IsEnabled = on; txtActiveColor.IsEnabled = on; txtTags.IsEnabled = on;
            cmbFont.IsEnabled = on;
        }

        void OnLinesChanged(object sender, TextChangedEventArgs e)
        {
            if (syncing || selected == null) return;
            if (selected.Kind == ElementKind.LowerThird)
            {
                selected.OverlayText = txtLines.Text.Trim();
                return;
            }
            selected.Lines.Clear();
            foreach (var ln in txtLines.Text.Split(new[] { '\n' }))
            {
                var t = ln.Trim();
                if (t.Length > 0) selected.Lines.Add(t);
            }
            RefreshVisualOf(selected);
        }

        void OnGeomChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (syncing || selected == null) return;
            selected.X = sldX.Value; selected.Y = sldY.Value;
            selected.W = sldW.Value; selected.H = sldH.Value;
            RefreshVisualOf(selected);
        }

        void OnSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (syncing || selected == null) return;
            selected.StyleOverride.Size = sldSize.Value;
        }

        void OnStyleFlag(object sender, RoutedEventArgs e)
        {
            if (syncing || selected == null) return;
            selected.StyleOverride.Bold = chkBold.IsChecked;
            selected.StyleOverride.Italic = chkItalic.IsChecked;
            selected.StyleOverride.Shadow = chkShadow.IsChecked;
        }

        void OnColorChanged(object sender, TextChangedEventArgs e)
        {
            if (syncing || selected == null) return;
            selected.StyleOverride.Color = txtColor.Text.Trim().StartsWith("#") ? txtColor.Text.Trim() : null;
            selected.StyleOverride.ActiveColor = txtActiveColor.Text.Trim().StartsWith("#") ? txtActiveColor.Text.Trim() : null;
        }

        void OnTagsChanged(object sender, TextChangedEventArgs e)
        {
            if (syncing || selected == null) return;
            selected.Tags.Clear();
            foreach (var t in txtTags.Text.Split(new[] { ',' }))
            {
                var tt = t.Trim();
                if (tt.Length > 0) selected.Tags.Add(tt.ToLowerInvariant());
            }
        }

        void OnFontChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncing || selected == null) return;
            selected.StyleOverride.Font = cmbFont.SelectedItem as string;
        }

        void RefreshVisualOf(Element el)
        {
            int idx = canvas.Children.OfType<Border>().ToList().FindIndex(b => b.Tag == el);
            if (idx < 0) return;
            canvas.Children.RemoveAt(idx);
            canvas.Children.Insert(idx, MakeElementVisual(el));
        }

        // ------------------------------------------------------------ comandos
        void OnAddText(object sender, RoutedEventArgs e) { AddElement(ElementKind.Text); }
        void OnAddVerse(object sender, RoutedEventArgs e) { AddElement(ElementKind.Verse); }
        void OnAddLower(object sender, RoutedEventArgs e) { AddElement(ElementKind.LowerThird); }

        void OnAddImage(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Añadir imagen",
                Filter = "Imágenes|*.jpg;*.jpeg;*.png;*.gif;*.bmp"
            };
            if (dlg.ShowDialog() == true) AddElement(ElementKind.Image, dlg.FileName);
        }

        void OnAddVideo(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Añadir video",
                Filter = "Videos|*.mp4;*.avi;*.wmv;*.mov"
            };
            if (dlg.ShowDialog() == true) AddElement(ElementKind.Video, dlg.FileName);
        }

        void AddElement(ElementKind kind, string src = null)
        {
            if (current == null)
            {
                MessageBox.Show("Crea o selecciona un escenario primero.", "Fusion HP");
                return;
            }
            var el = new Element
            {
                Kind = kind,
                Src = src,
                X = 0.1, Y = 0.1, W = 0.8, H = 0.5
            };
            if (kind == ElementKind.Text) el.Lines.Add("Nuevo texto");
            if (kind == ElementKind.Verse) { el.Reference = "Juan 3:16"; el.Lines.Add("Porque de tal manera amó Dios al mundo…"); }
            if (kind == ElementKind.LowerThird) el.OverlayText = "Mensaje";
            current.Elements.Add(el);
            RebuildCanvas();
            SelectElement(el);
        }

        void OnDelete(object sender, RoutedEventArgs e)
        {
            if (selected == null || current == null) return;
            current.Elements.Remove(selected);
            RebuildCanvas();
            SelectElement(null);
        }

        void OnAddScenario(object sender, RoutedEventArgs e)
        {
            if (project == null) project = AhpProject.CreateDefault();
            var scn = new Scenario { Title = "Escenario " + (project.Scenarios.Count + 1) };
            scn.Elements.Add(new Element { Lines = { "Texto del escenario" } });
            project.Scenarios.Add(scn);
            RefreshScenarioList();
        }

        void OnSendLive(object sender, RoutedEventArgs e)
        {
            if (current == null)
            {
                MessageBox.Show("Selecciona un escenario para enviar.", "Fusion HP");
                return;
            }
            var h = RequestSendToLive;
            if (h != null) h(current);
        }

        static System.Windows.Media.Color Parse(string hex)
        {
            try
            {
                string h = (hex ?? "").TrimStart('#');
                if (h.Length == 8) h = h.Substring(2);
                if (h.Length == 6)
                    return System.Windows.Media.Color.FromRgb(
                        System.Convert.ToByte(h.Substring(0, 2), 16),
                        System.Convert.ToByte(h.Substring(2, 2), 16),
                        System.Convert.ToByte(h.Substring(4, 2), 16));
            }
            catch { }
            return System.Windows.Media.Color.FromRgb(0x10, 0x18, 0x20);
        }
    }
}
