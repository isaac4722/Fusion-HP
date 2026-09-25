// ============================================================================
//  LuminaPresentation Suite — Pages/StudioPage.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  StudioPage — modo «create» del flujo PowerStudio (referencia
//  H-P-Web-Version-Ref): cinta + miniaturas + lienzo 16:9 con elementos
//  arrastrables/redimensionables + propiedades + notas.
//
//  Modelo: ComposedElement (coordenadas NORMALIZADAS 0..1 — el mismo contrato
//  que proyecta el núcleo y que exportan PptxExporter/PdfExporter). Las
//  diapositivas viajan al culto como ítems "composed" y persisten en el plan
//  JSON (round-trip completo: guardar/abrir plan).
//
//  Interacción: arrastrar = mover · tirador inferior-derecho = redimensionar ·
//  clic = seleccionar (panel de propiedades) · Supr = eliminar elemento.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using lumina.core;

namespace lumina.wpf
{
    /// <summary>Fila de la tira de miniaturas.</summary>
    public sealed class StudioSlideVm
    {
        public string Number = "1";
        public string Title = "Sin título";
        public int Index;
    }

    /// <summary>Diapositiva compuesta del Estudio (título + elementos + notas).</summary>
    public sealed class StudioSlide
    {
        public string Title = "Sin título";
        public string Notes = string.Empty;
        public List<ComposedElement> Elements = new List<ComposedElement>();

        public static StudioSlide From(string title, string notes, IList<ComposedElement> elements)
        {
            StudioSlide s = new StudioSlide();
            s.Title = title;
            s.Notes = notes;
            if (elements != null)
                foreach (ComposedElement el in elements) s.Elements.Add(CloneOf(el));
            return s;
        }

        public static ComposedElement CloneOf(ComposedElement src)
        {
            ComposedElement e = new ComposedElement();
            e.Kind = src.Kind;
            e.X = src.X; e.Y = src.Y; e.W = src.W; e.H = src.H;
            e.Opacity = src.Opacity;
            e.Lines = new List<string>(src.Lines.ToArray());
            e.FontSizePct = src.FontSizePct;
            e.Align = src.Align;
            e.Color = src.Color;
            e.ImagePath = src.ImagePath;
            e.Cover = src.Cover;
            return e;
        }
    }

    public partial class StudioPage : UserControl
    {
        internal MainWindow Shell;
        private readonly List<StudioSlide> _slides = new List<StudioSlide>();
        private int _current = -1;
        private ComposedElement _selected;
        private Border _selectedHost;
        private bool _syncing;
        private Point _dragStart;
        private Point _dragOrigin;
        private bool _moving;
        private bool _sizing;

        public StudioPage()
        {
            InitializeComponent();
        }

        public void Wire(MainWindow shell) { Shell = shell; }

        /* -------------------------------------------------- modelo ---------- */

        private StudioSlide Current
        {
            get { return (_current >= 0 && _current < _slides.Count) ? _slides[_current] : null; }
        }

        /// <summary>Ctrl+N / «Título»: nueva diapositiva con un elemento de título.</summary>
        internal void NewSlide()
        {
            StudioSlide s = new StudioSlide();
            s.Title = "Diapositiva " + (_slides.Count + 1);
            ComposedElement t = NewText();
            t.Lines.Add(s.Title);
            t.Y = 0.38; t.H = 0.22; t.FontSizePct = 14;
            s.Elements.Add(t);
            _slides.Add(s);
            Select(_slides.Count - 1);
            if (Shell != null)
                Shell.Status("Diapositiva añadida en el Estudio (" + _slides.Count + ").");
        }

        private static ComposedElement NewText()
        {
            ComposedElement e = new ComposedElement();
            e.Kind = ComposedElement.KindText;
            e.X = 0.08; e.Y = 0.30; e.W = 0.84; e.H = 0.20;
            e.Align = 1;
            e.Lines.Add("Texto nuevo");
            return e;
        }

        /* ------------------------------------------------------- cinta ------ */

        private void OnAddTitle(object sender, RoutedEventArgs e) { NewSlide(); }

        private void OnAddText(object sender, RoutedEventArgs e)
        {
            if (Current == null) NewSlide();
            Current.Elements.Add(NewText());
            RebuildCanvas();
        }

        private void OnAddImage(object sender, RoutedEventArgs e)
        {
            if (Current == null) NewSlide();
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Añadir imagen al lienzo",
                Filter = "Imágenes|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tif;*.tiff|Todos los archivos|*.*",
            };
            bool? ok;
            try { ok = dlg.ShowDialog(Window.GetWindow(this)); }
            catch (Exception) { return; }
            if (ok != true) return;
            ComposedElement el = new ComposedElement();
            el.Kind = ComposedElement.KindImage;
            el.X = 0.15; el.Y = 0.15; el.W = 0.7; el.H = 0.7;
            el.ImagePath = dlg.FileName;
            Current.Elements.Add(el);
            RebuildCanvas();
        }

        private void OnDuplicate(object sender, RoutedEventArgs e)
        {
            if (Current == null) return;
            StudioSlide copy = StudioSlide.From(Current.Title + " (copia)", Current.Notes, Current.Elements);
            _slides.Insert(_current + 1, copy);
            Select(_current + 1);
        }

        private void OnDeleteSlide(object sender, RoutedEventArgs e)
        {
            if (_current < 0) return;
            int gone = _current;
            _slides.RemoveAt(gone);
            Select(_slides.Count == 0 ? -1 : Math.Min(gone, _slides.Count - 1));
        }

        private void OnGoLive(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.NavigateToIndex(0);
        }

        /* ------------------------------------ guardar / cargar en el culto -- */

        private void OnSaveToService(object sender, RoutedEventArgs e)
        {
            if (Shell == null) return;
            if (_slides.Count == 0) { Shell.Status("El Estudio está vacío: nada que guardar."); return; }
            Shell.SaveStudioToService(_slides);
        }

        private void OnLoadFromService(object sender, RoutedEventArgs e)
        {
            if (Shell == null) return;
            int n = Shell.LoadStudioFromService(_slides);
            if (n > 0) { Select(0); Shell.Status(n + " diseño(s) cargado(s) del culto."); }
            else Shell.Status("El culto no trae diseños (lienzos) que cargar.");
        }

        /* ------------------------------------------------- miniaturas ------- */

        private void OnThumbSelected(object sender, SelectionChangedEventArgs e)
        {
            StudioSlideVm vm = lstThumbs.SelectedItem as StudioSlideVm;
            if (vm != null && vm.Index < _slides.Count && vm.Index != _current) Select(vm.Index);
        }

        private void Select(int index)
        {
            _current = index;
            _selected = null;
            _selectedHost = null;
            RefreshThumbs();
            RebuildCanvas();
            LoadProperties();
        }

        private void RefreshThumbs()
        {
            List<StudioSlideVm> vms = new List<StudioSlideVm>();
            for (int i = 0; i < _slides.Count; i++)
                vms.Add(new StudioSlideVm
                {
                    Index = i,
                    Number = (i + 1).ToString(CultureInfo.InvariantCulture),
                    Title = _slides[i].Title,
                });
            int keep = _current;
            _syncing = true;
            try
            {
                lstThumbs.ItemsSource = vms;
                if (keep >= 0 && keep < vms.Count) lstThumbs.SelectedIndex = keep;
            }
            finally { _syncing = false; }
            txtStudioCount.Text = _slides.Count + (_slides.Count == 1 ? " diapositiva" : " diapositivas");
            emptyStudio.Visibility = _slides.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /* --------------------------------------------------- lienzo --------- */

        private void RebuildCanvas()
        {
            canvasHost.Children.Clear();
            StudioSlide s = Current;
            if (s == null) return;
            foreach (ComposedElement el in s.Elements) AddElementVisual(el);
        }

        /// <summary>Construye el visual de un elemento (Border + contenido + tirador).</summary>
        private void AddElementVisual(ComposedElement el)
        {
            // --- contenido ---
            UIElement content;
            if (el.Kind == ComposedElement.KindImage)
            {
                Image img = new Image
                {
                    Stretch = el.Cover ? Stretch.UniformToFill : Stretch.Uniform,
                    Margin = new Thickness(2),
                };
                try
                {
                    if (!string.IsNullOrEmpty(el.ImagePath))
                        img.Source = new BitmapImageLoader(el.ImagePath).Source;
                }
                catch (Exception) { }
                content = img;
            }
            else
            {
                content = new TextBlock
                {
                    Text = string.Join("\n", el.Lines.ToArray()),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = el.Align == 0 ? TextAlignment.Left :
                                    el.Align == 2 ? TextAlignment.Right : TextAlignment.Center,
                    FontSize = el.FontSizePct > 0 ? 540 * el.FontSizePct / 100.0 : 34,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = TryBrush(el.Color),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6),
                };
            }

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(content, 0);
            grid.Children.Add(content);
            Rectangle grip = new Rectangle
            {
                Width = 16,
                Height = 16,
                Fill = new SolidColorBrush(Color.FromArgb(120, 120, 190, 255)),
                Cursor = Cursors.SizeNWSE,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 2, 2),
            };
            Grid.SetRow(grip, 1);
            grid.Children.Add(grip);

            Border host = new Border
            {
                Child = grid,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)),
                Cursor = Cursors.SizeAll,
                Tag = el,
            };
            Place(host, el);

            // --- mover ---
            host.MouseLeftButtonDown += delegate (object sd, MouseButtonEventArgs ev)
            {
                SelectElement(el, host);
                host.CaptureMouse();
                _dragStart = ev.GetPosition(canvasHost);
                _dragOrigin = new Point(el.X, el.Y);
                _moving = true;
                ev.Handled = true;
            };
            host.MouseMove += delegate (object sd, MouseEventArgs ev)
            {
                if (!host.IsMouseCaptured || !_moving) return;
                Point p = ev.GetPosition(canvasHost);
                el.X = Clamp01(_dragOrigin.X + (p.X - _dragStart.X) / 960.0);
                el.Y = Clamp01(_dragOrigin.Y + (p.Y - _dragStart.Y) / 540.0);
                Place(host, el);
            };
            host.MouseLeftButtonUp += delegate (object sd, MouseButtonEventArgs ev)
            {
                host.ReleaseMouseCapture();
                _moving = false;
            };

            // --- redimensionar ---
            grip.MouseLeftButtonDown += delegate (object sd, MouseButtonEventArgs ev)
            {
                SelectElement(el, host);
                grip.CaptureMouse();
                _dragStart = ev.GetPosition(canvasHost);
                _dragOrigin = new Point(el.W, el.H);
                _sizing = true;
                ev.Handled = true;
            };
            grip.MouseMove += delegate (object sd, MouseEventArgs ev)
            {
                if (!grip.IsMouseCaptured || !_sizing) return;
                Point p = ev.GetPosition(canvasHost);
                el.W = Math.Max(0.05, Math.Min(1.0, _dragOrigin.X + (p.X - _dragStart.X) / 960.0));
                el.H = Math.Max(0.05, Math.Min(1.0, _dragOrigin.Y + (p.Y - _dragStart.Y) / 540.0));
                Place(host, el);
            };
            grip.MouseLeftButtonUp += delegate (object sd, MouseButtonEventArgs ev)
            {
                grip.ReleaseMouseCapture();
                _sizing = false;
            };

            canvasHost.Children.Add(host);
        }

        private static void Place(Border b, ComposedElement el)
        {
            System.Windows.Controls.Canvas.SetLeft(b, el.X * 960.0);
            System.Windows.Controls.Canvas.SetTop(b, el.Y * 540.0);
            b.Width = el.W * 960.0;
            b.Height = el.H * 540.0;
        }

        private static double Clamp01(double v) { return Math.Max(0.0, Math.Min(1.0, v)); }

        private static Brush TryBrush(string hex)
        {
            try
            {
                if (!string.IsNullOrEmpty(hex))
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch (Exception) { }
            return Brushes.White;
        }

        private void SelectElement(ComposedElement el, Border host)
        {
            if (_selectedHost != null)
                _selectedHost.BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            _selected = el;
            _selectedHost = host;
            host.BorderBrush = Shell != null ? Shell.AccentBrush : Brushes.DeepSkyBlue;
            LoadProperties();
        }

        private void OnCanvasBackgroundClick(object sender, MouseButtonEventArgs e)
        {
            if (_selectedHost != null)
                _selectedHost.BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            _selected = null;
            _selectedHost = null;
            LoadProperties();
        }

        /* ------------------------------------------------- propiedades ------ */

        private void LoadProperties()
        {
            _syncing = true;
            try
            {
                StudioSlide s = Current;
                txtSlideTitle.Text = s == null ? string.Empty : s.Title;
                txtSlideTitle.IsEnabled = s != null;
                txtNotes.Text = s == null ? string.Empty : s.Notes;
                txtNotes.IsEnabled = s != null;
                ComposedElement el = _selected;
                bool isText = el != null && el.Kind == ComposedElement.KindText;
                txtElementText.Text = isText ? string.Join("\n", el.Lines.ToArray()) : string.Empty;
                txtElementText.IsEnabled = isText;
                txtFontSize.Text = isText && el.FontSizePct > 0
                    ? el.FontSizePct.ToString(CultureInfo.InvariantCulture) : string.Empty;
                txtFontSize.IsEnabled = isText;
                cmbAlign.SelectedIndex = isText ? (el.Align < 0 || el.Align > 2 ? 1 : el.Align) : 1;
                cmbAlign.IsEnabled = isText;
            }
            finally { _syncing = false; }
        }

        private void OnTitleChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            StudioSlide s = Current;
            if (s != null) s.Title = txtSlideTitle.Text;
        }

        private void OnNotesChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            StudioSlide s = Current;
            if (s != null) s.Notes = txtNotes.Text;
        }

        private void OnElementTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            ComposedElement el = _selected;
            if (el == null || el.Kind != ComposedElement.KindText) return;
            el.Lines = new List<string>(txtElementText.Text.Replace("\r", "").Split('\n'));
            RebuildCanvas();
        }

        private void OnFontSizeChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            ComposedElement el = _selected;
            if (el == null || el.Kind != ComposedElement.KindText) return;
            int pct;
            if (int.TryParse(txtFontSize.Text, out pct))
                el.FontSizePct = Math.Max(0, Math.Min(40, pct));
            RebuildCanvas();
        }

        private void OnAlignChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            ComposedElement el = _selected;
            if (el == null || el.Kind != ComposedElement.KindText) return;
            el.Align = cmbAlign.SelectedIndex < 0 ? 1 : cmbAlign.SelectedIndex;
            RebuildCanvas();
        }
    }

    /// <summary>Carga perezosa de imágenes del lienzo (decodifica solo al usar).</summary>
    internal sealed class BitmapImageLoader
    {
        public readonly System.Windows.Media.Imaging.BitmapImage Source;
        public BitmapImageLoader(string path)
        {
            Source = new System.Windows.Media.Imaging.BitmapImage();
            Source.BeginInit();
            Source.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            Source.UriSource = new Uri(path, UriKind.Absolute);
            Source.EndInit();
            Source.Freeze();
        }
    }
}
