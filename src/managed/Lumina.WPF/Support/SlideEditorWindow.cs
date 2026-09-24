// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  SlideEditorWindow.cs : v7.1.0 «OPERADOR» (feedback #1).
//  Editor de diapositivas estilo POWERPOINT / Holyrics: LIENZO LIBRE con
//  elementos (texto/imagen) posicionables con el ratón — arrastrar para
//  mover, esquinas para redimensionar, DOBLE CLIC para editar el texto
//  IN SITU, suprimir/duplicar con el teclado. Nada de formularios rígidos:
//  lo que se ve en el lienzo es EXACTAMENTE lo que proyecta el motor
//  (mismo contrato de coordenadas por fracción que el Renderer nativo:
//  DrawComposedElements dibuja cada elemento en su rect x·w, y·h).
//
//  Salida: ScenarioItem «composed» (kind=composed, elements=[]) — se
//  aplana a UNA slide SLIDE_COMPOSED que el núcleo dibuja WYSIWYG.
//  WPF puro (sin referencias nuevas), C# 7.3, net48.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using lumina.core;

namespace lumina.wpf
{
    /// <summary>
    /// Editor de diapositivas de lienzo libre (feedback #1). Se abre con
    /// ShowDialog(); DialogResult=true cuando se guarda: leer Elements y
    /// SlideTitle. El fondo y los valores por defecto de texto provienen del
    /// TEMA ACTIVO de la app (mismo aspecto que la proyección).
    /// </summary>
    internal sealed class SlideEditorWindow : Window
    {
        // Lienzo lógico (el mismo 16:9 del motor; las fuentes se escalan con
        // la altura exactamente como DrawComposedElements).
        private const double CanvasW = 960.0;
        private const double CanvasH = 540.0;

        private readonly Theme _theme;
        private readonly List<ComposedElement> _elements;
        private readonly MainWindow _shell;

        private int _selected = -1;          // índice en _elements (-1 = nada)
        private Canvas _canvas;
        private readonly List<Border> _elementAdorners = new List<Border>();
        private TextBox _titleBox;
        private ListBox _lstLayers;
        private ComboBox _cmbAlign, _cmbColor, _cmbFit;
        private LumNumeric _numFontSize, _numOpacity;
        private TextBlock _lblProps;
        private TextBox _inlineEditor;       // edición in-situ de texto
        private int _inlineIndex = -1;

        private const double Snap = 1.0 / 24.0;   // retícula ~4% (como F2.10)
        private const double Guide = 0.006;       // tolerancia guías de centro

        /// <summary>Elementos resultantes (válidos si DialogResult=true).</summary>
        internal List<ComposedElement> Elements { get { return _elements; } }

        /// <summary>Título de la diapositiva resultante.</summary>
        internal string SlideTitle { get; private set; }

        internal SlideEditorWindow(MainWindow shell, Theme theme,
                                   string title, List<ComposedElement> existing)
        {
            _shell = shell;
            _theme = theme ?? new Theme();
            _elements = existing != null
                ? CloneElements(existing)
                : new List<ComposedElement>();
            SlideTitle = title ?? string.Empty;
            BuildUi();
            RenderAll();
            Title = "Editor de diapositivas — LuminaPresentation";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            MinWidth = 1080; MinHeight = 700;
            Width = 1200; Height = 780;
            PreviewKeyDown += OnEditorKeyDown;
        }

        private static List<ComposedElement> CloneElements(List<ComposedElement> src)
        {
            List<ComposedElement> copy = new List<ComposedElement>(src.Count);
            foreach (ComposedElement e in src)
            {
                ComposedElement c = new ComposedElement();
                c.Kind = e.Kind; c.X = e.X; c.Y = e.Y; c.W = e.W; c.H = e.H;
                c.Opacity = e.Opacity; c.FontSizePct = e.FontSizePct;
                c.Align = e.Align; c.Color = e.Color; c.ImagePath = e.ImagePath;
                c.Cover = e.Cover; c.Lines = new List<string>(e.Lines);
                copy.Add(c);
            }
            return copy;
        }

        /* ================================================== UI (code-only) == */

        private void BuildUi()
        {
            Background = TryFindResource("PageBrush") as SolidColorBrush;
            if (Background == null) Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x11, 0x16));

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            root.Children.Add(BuildToolbar());

            Grid middle = new Grid();
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            Grid.SetRow(middle, 1);

            // --- izquierda: capas ---
            Border layersCard = Card("Elementos (z de abajo arriba)");
            _lstLayers = new ListBox();
            _lstLayers.SelectionChanged += OnLayersSelectionChanged;
            _lstLayers.Margin = new Thickness(0, 8, 0, 0);
            SetItemStyle(_lstLayers);
            StackPanel layersSp = new StackPanel();
            layersSp.Children.Add(_lstLayers);
            StackPanel layerBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            layerBtns.Children.Add(MiniButton("Subir", delegate { MoveLayer(1); }));
            layerBtns.Children.Add(MiniButton("Bajar", delegate { MoveLayer(-1); }));
            layersSp.Children.Add(layerBtns);
            CardBody(layersCard, layersSp);
            Grid.SetColumn(layersCard, 0);
            middle.Children.Add(layersCard);

            // --- centro: lienzo ---
            Border canvasWrap = Card(null);
            Viewbox vb = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
            _canvas = new Canvas
            {
                Width = CanvasW, Height = CanvasH,
                Background = MakeThemeBackground(),
            };
            _canvas.MouseLeftButtonDown += OnCanvasBackgroundDown;
            vb.Child = _canvas;
            canvasWrap.Child = vb;
            Grid.SetColumn(canvasWrap, 1);
            middle.Children.Add(canvasWrap);

            // --- derecha: propiedades ---
            Border propsCard = Card("Propiedades");
            StackPanel props = new StackPanel();
            _lblProps = new TextBlock { Text = "Nada seleccionado", Opacity = 0.7,
                                        Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            props.Children.Add(_lblProps);

            props.Children.Add(Label("Tamaño de letra (% de la altura · 0 = auto)"));
            _numFontSize = new LumNumeric { Min = 0, Max = 40, Value = 0, Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
            _numFontSize.ValueChangedByUser += delegate { ApplyPropsToSelection(); };
            props.Children.Add(_numFontSize);

            props.Children.Add(Label("Alineación"));
            _cmbAlign = new ComboBox();
            _cmbAlign.Items.Add("Izquierda"); _cmbAlign.Items.Add("Centro"); _cmbAlign.Items.Add("Derecha");
            _cmbAlign.SelectedIndex = 1;
            _cmbAlign.SelectionChanged += delegate { ApplyPropsToSelection(); };
            props.Children.Add(_cmbAlign);

            props.Children.Add(Label("Color del texto"));
            _cmbColor = new ComboBox();
            _cmbColor.Items.Add("Del tema");
            _cmbColor.Items.Add("Blanco"); _cmbColor.Items.Add("Negro");
            _cmbColor.Items.Add("Ámbar"); _cmbColor.Items.Add("Celeste");
            _cmbColor.Items.Add("Verde"); _cmbColor.Items.Add("Rojo");
            _cmbColor.SelectedIndex = 0;
            _cmbColor.SelectionChanged += delegate { ApplyPropsToSelection(); };
            props.Children.Add(_cmbColor);

            props.Children.Add(Label("Opacidad (%)"));
            _numOpacity = new LumNumeric { Min = 10, Max = 100, Value = 100, Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
            _numOpacity.ValueChangedByUser += delegate { ApplyPropsToSelection(); };
            props.Children.Add(_numOpacity);

            props.Children.Add(Label("Ajuste de imagen"));
            _cmbFit = new ComboBox();
            _cmbFit.Items.Add("Contener"); _cmbFit.Items.Add("Cubrir");
            _cmbFit.SelectedIndex = 0;
            _cmbFit.SelectionChanged += delegate { ApplyPropsToSelection(); };
            props.Children.Add(_cmbFit);

            TextBlock hint = new TextBlock
            {
                Text = "Arrastra para mover · tira de las esquinas para redimensionar · "
                     + "doble clic en un texto para editarlo en el sitio · Supr elimina · "
                     + "Ctrl+D duplica · flechas ajustan a mano",
                Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
            };
            props.Children.Add(hint);
            CardBody(propsCard, props);
            Grid.SetColumn(propsCard, 2);
            middle.Children.Add(propsCard);

            root.Children.Add(middle);
            root.Children.Add(BuildStatusBar());

            Content = root;
        }

        private Border Card(string title)
        {
            Border b = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = TryFindResource("CardBorderBrush") as SolidColorBrush,
                Background = TryFindResource("CardBrush") as SolidColorBrush,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(4)
            };
            if (b.BorderBrush == null) b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x2D, 0x3A));
            if (b.Background == null) b.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x23));
            if (title != null)
            {
                DockPanel dp = new DockPanel();
                TextBlock t = new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 6),
                    Foreground = TryFindResource("TextSecondaryBrush") as SolidColorBrush
                };
                if (t.Foreground == null) t.Foreground = Brushes.Gray;
                DockPanel.SetDock(t, Dock.Top);
                dp.Children.Add(t);
                b.Child = dp;
                // el contenido real se cuelga luego: guardamos el DockPanel
                b.Tag = dp;
            }
            return b;
        }

        /// <summary>Inserta el cuerpo dentro de la tarjeta sin machacar el título (Card crea un DockPanel con la cabecera en Tag).</summary>
        private void CardBody(Border card, UIElement body)
        {
            DockPanel dp = card.Tag as DockPanel;
            if (dp != null) dp.Children.Add(body);
            else card.Child = body;
        }

        private TextBlock Label(string text)
        {
            TextBlock t = new TextBlock
            {
                Text = text, Opacity = 0.75, Margin = new Thickness(0, 12, 0, 4)
            };
            return t;
        }

        private void SetItemStyle(ListBox lb)
        {
            lb.Background = Brushes.Transparent;
            lb.BorderThickness = new Thickness(0);
            lb.MaxHeight = 300;
        }

        private Grid BuildToolbar()
        {
            Grid g = new Grid();
            g.Margin = new Thickness(8, 8, 8, 0);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            TextBlock lbl = new TextBlock { Text = "Título:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            _titleBox = new TextBox { Width = 170 };
            _titleBox.Text = SlideTitle;
            left.Children.Add(lbl); left.Children.Add(_titleBox);
            Grid.SetColumn(left, 0);
            g.Children.Add(left);

            StackPanel center = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            center.Children.Add(ToolButton("Agregar texto", delegate { AddTextElement(); }));
            center.Children.Add(ToolButton("Agregar imagen…", delegate { AddImageElement(); }));
            center.Children.Add(ToolButton("Duplicar (Ctrl+D)", delegate { DuplicateSelected(); }));
            center.Children.Add(ToolButton("Quitar (Supr)", delegate { DeleteSelected(); }));
            Grid.SetColumn(center, 1);
            g.Children.Add(center);

            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(ToolButton("Cancelar", delegate { DialogResult = false; Close(); }));
            Button save = ToolButton("Guardar en el culto", delegate { Save(); });
            save.FontWeight = FontWeights.Bold;
            right.Children.Add(save);
            Grid.SetColumn(right, 2);
            g.Children.Add(right);
            return g;
        }

        private TextBlock BuildStatusBar()
        {
            return new TextBlock
            {
                Text = "Editor de diapositivas · lienzo libre 16:9 · el fondo y el texto por defecto "
                     + "siguen el TEMA ACTIVO de la proyección",
                Opacity = 0.55, Margin = new Thickness(12, 6, 12, 10)
            };
        }

        private Button ToolButton(string text, RoutedEventHandler onClick)
        {
            Button b = new Button
            {
                Content = text, Margin = new Thickness(4, 0, 4, 0), Padding = new Thickness(10, 6, 10, 6),
                MinWidth = 60
            };
            b.Click += onClick;
            return b;
        }

        private Button MiniButton(string text, RoutedEventHandler onClick)
        {
            Button b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 3, 8, 3) };
            b.Click += onClick;
            return b;
        }

        private Brush MakeThemeBackground()
        {
            Color bg = ParseColor(_theme.BgColor, Color.FromRgb(0x0B, 0x1F, 0x2A));
            // fondo del tema: color (imagen de fondo del tema se dibuja encima si existe)
            return new SolidColorBrush(bg);
        }

        private void OnCanvasBackgroundDown(object sender, MouseButtonEventArgs e)
        {
            Select(-1);
        }

        /* =================================================== render (WYSIWYG) */

        /// <summary>Redibuja el lienzo completo (además del fondo del tema).</summary>
        private void RenderAll()
        {
            _canvas.Children.Clear();
            _elementAdorners.Clear();
            DrawThemeBackgroundImage();
            for (int i = 0; i < _elements.Count; i++)
                DrawElement(i);
            RefreshLayers();
            RefreshProps();
        }

        private void DrawThemeBackgroundImage()
        {
            if (_theme == null || _theme.ImagePath.Length == 0) return;
            try
            {
                if (!File.Exists(_theme.ImagePath)) return;
                ImageBrush ib = new ImageBrush();
                ib.ImageSource = LoadImage(_theme.ImagePath);
                ib.Stretch = _theme.ImageMode == 1 ? Stretch.UniformToFill : Stretch.Uniform;
                ib.AlignmentX = AlignmentX.Center; ib.AlignmentY = AlignmentY.Center;
                Rectangle rc = new Rectangle
                {
                    Width = CanvasW, Height = CanvasH, Fill = ib
                };
                Canvas.SetLeft(rc, 0); Canvas.SetTop(rc, 0);
                _canvas.Children.Add(rc);
            }
            catch (Exception) { }
        }

        private static ImageSource LoadImage(string path)
        {
            return new System.Windows.Media.Imaging.BitmapImage(new Uri(path));
        }

        private Border DrawElement(int index)
        {
            ComposedElement e = _elements[index];
            FrameworkElement visual = e.Kind == ComposedElement.KindImage
                ? BuildImageVisual(e)
                : (FrameworkElement)BuildTextVisual(e);
            visual.Opacity = e.Opacity;

            Border host = new Border();
            host.Child = visual;
            host.Tag = index;
            double px = e.X * CanvasW, py = e.Y * CanvasH;
            double pw = e.W * CanvasW, ph = e.H * CanvasH;
            Canvas.SetLeft(host, px); Canvas.SetTop(host, py);
            host.Width = pw; host.Height = ph;

            // selección: borde ámbar + asas de esquina
            if (index == _selected)
            {
                SolidColorBrush amber = TryFindResource("AccentBrush") as SolidColorBrush;
                if (amber == null) amber = new SolidColorBrush(Color.FromRgb(0xF0, 0xA9, 0x3B));
                host.BorderBrush = amber;
                host.BorderThickness = new Thickness(1.5);
                AddHandle(index, host, HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE);
                AddHandle(index, host, HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW);
                AddHandle(index, host, HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW);
                AddHandle(index, host, HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE);
            }
            else
            {
                host.BorderThickness = new Thickness(0);
            }

            host.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs ev)
            {
                ev.Handled = true;
                BeginDrag(index, s as Border, ev);
            };
            host.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs ev)
            {
                EndDrag(s as FrameworkElement);
            };
            host.MouseMove += delegate(object s, MouseEventArgs ev)
            {
                MoveDrag(index, s as FrameworkElement, ev);
            };
            // doble clic → edición in-situ del texto (handledEventsToo: el
            // arrastre marca el evento como Handled en el primer clic)
            host.AddHandler(UIElement.MouseLeftButtonDownEvent,
                (MouseButtonEventHandler)delegate(object s, MouseButtonEventArgs ev)
                {
                    if (ev.ClickCount == 2)
                    {
                        ev.Handled = true;
                        if (_elements[index].Kind == ComposedElement.KindText) BeginInlineEdit(index);
                    }
                }, true);

            _canvas.Children.Add(host);
            _elementAdorners.Insert(index, host);
            return host;
        }

        private void AddHandle(int index, Border host, HorizontalAlignment ha, VerticalAlignment va, Cursor cursor)
        {
            SolidColorBrush amber = TryFindResource("AccentBrush") as SolidColorBrush;
            if (amber == null) amber = new SolidColorBrush(Color.FromRgb(0xF0, 0xA9, 0x3B));
            Rectangle h = new Rectangle
            {
                Width = 11, Height = 11, Fill = amber,
                HorizontalAlignment = ha, VerticalAlignment = va,
                Cursor = cursor, Stroke = Brushes.Black, StrokeThickness = 1
            };
            Thickness hm = new Thickness();
            if (ha == HorizontalAlignment.Right) hm.Right = -6;
            if (va == VerticalAlignment.Bottom) hm.Bottom = -6;
            h.Margin = hm;
            // Las asas van en un Grid contenedor para no reemplazar el visual:
            Grid g = host.Child as Grid;
            if (g == null)
            {
                g = new Grid();
                UIElement cur = host.Child;
                host.Child = null;
                g.Children.Add(cur);
                host.Child = g;
            }
            g.Children.Add(h);
            h.Tag = va == VerticalAlignment.Top
                ? (ha == HorizontalAlignment.Left ? "nw" : "ne")
                : (ha == HorizontalAlignment.Left ? "sw" : "se");
            h.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs ev)
            {
                ev.Handled = true;
                BeginResize(index, (string)h.Tag, s as FrameworkElement, ev);
            };
            h.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs ev)
            {
                EndDrag(s as FrameworkElement);
            };
            h.MouseMove += delegate(object s, MouseEventArgs ev)
            {
                MoveResize(index, (string)h.Tag, s as FrameworkElement, ev);
            };
        }

        /* ------------------------------------------- visuales de elementos -- */

        private FrameworkElement BuildTextVisual(ComposedElement e)
        {
            // WYSIWYG: mismo contrato que Renderer::DrawComposedElements —
            // líneas centradas verticalmente en el rect, alineación horizontal,
            // tamaño auto (h/lines·0.82 con shrink 10%) o fijo (% de la altura).
            List<string> lines = NormalizeLines(e);
            TextBlock tb = new TextBlock();
            foreach (string ln in lines)
            {
                if (tb.Inlines.Count > 0) tb.Inlines.Add(new System.Windows.Documents.LineBreak());
                tb.Inlines.Add(ln.Length == 0 ? " " : ln);
            }
            Brush fg = string.IsNullOrEmpty(e.Color)
                ? new SolidColorBrush(ParseColor(_theme.FgColor, Colors.White))
                : new SolidColorBrush(ParseColor(e.Color, Colors.White));
            tb.Foreground = fg;
            tb.TextAlignment = e.Align == 0 ? TextAlignment.Left
                : (e.Align == 2 ? TextAlignment.Right : TextAlignment.Center);
            tb.FontFamily = new FontFamily(string.IsNullOrEmpty(_theme.FontFace) ? "Segoe UI" : _theme.FontFace);
            tb.FontWeight = _theme.Bold ? FontWeights.Bold : FontWeights.Normal;
            double rectH = Math.Max(12, e.H * CanvasH);
            double rectW = Math.Max(12, e.W * CanvasW);
            double fontPx = e.FontSizePct > 0
                ? e.FontSizePct / 100.0 * CanvasH
                : rectH / Math.Max(1, lines.Count) * 0.82;
            if (fontPx < 8) fontPx = 8;
            // shrink hasta caber (límite de 24 pasos, como el nativo)
            for (int iter = 0; iter < 24; iter++)
            {
                tb.FontSize = fontPx;
                tb.Measure(new Size(rectW, double.PositiveInfinity));
                if (tb.DesiredSize.Width <= rectW + 1 && tb.DesiredSize.Height <= rectH + 1)
                    break;
                fontPx *= 0.9;
                if (fontPx < 8) { fontPx = 8; break; }
            }
            tb.FontSize = fontPx;
            tb.VerticalAlignment = VerticalAlignment.Center;
            if (lines.Count > 1) tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            // sombra discreta como el nativo
            if (_theme.ShadowAlpha > 0)
            {
                tb.Effect = new DropShadowEffect
                {
                    BlurRadius = 4, ShadowDepth = 2, Direction = 315,
                    Opacity = _theme.ShadowAlpha / 255.0, Color = Colors.Black
                };
            }
            return tb;
        }

        private List<string> NormalizeLines(ComposedElement e)
        {
            List<string> lines = new List<string>();
            if (e.Lines == null || e.Lines.Count == 0) lines.Add("Texto");
            else foreach (string l in e.Lines) lines.Add(_theme.Uppercase ? l.ToUpperInvariant() : l);
            return lines;
        }

        private FrameworkElement BuildImageVisual(ComposedElement e)
        {
            Image img = new Image();
            try
            {
                if (e.ImagePath.Length > 0 && File.Exists(e.ImagePath))
                    img.Source = LoadImage(e.ImagePath);
            }
            catch (Exception) { }
            if (img.Source == null)
            {
                // marcador de imagen no encontrada
                Border ph = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
                    CornerRadius = new CornerRadius(4)
                };
                TextBlock t = new TextBlock
                {
                    Text = "Imagen\n" + System.IO.Path.GetFileName(e.ImagePath),
                    Foreground = Brushes.White, Opacity = 0.6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center
                };
                ph.Child = t;
                return ph;
            }
            img.Stretch = e.Cover ? Stretch.UniformToFill : Stretch.Uniform;
            return img;
        }

        private static Color ParseColor(string s, Color fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            try
            {
                s = s.Trim();
                if (s.Length == 9 && (s[0] == '#'))
                    return Color.FromArgb(Convert.ToByte(s.Substring(1, 2), 16),
                        Convert.ToByte(s.Substring(3, 2), 16),
                        Convert.ToByte(s.Substring(5, 2), 16),
                        Convert.ToByte(s.Substring(7, 2), 16));
                if (s.Length == 7 && (s[0] == '#'))
                    return Color.FromRgb(Convert.ToByte(s.Substring(1, 2), 16),
                        Convert.ToByte(s.Substring(3, 2), 16),
                        Convert.ToByte(s.Substring(5, 2), 16));
            }
            catch (Exception) { }
            return fallback;
        }

        /* ================================================== interacción ratón */

        private int _dragIndex = -1;
        private Point _dragStart;
        private Rect _dragOrig;            // x,y,w,h (fracciones) al iniciar
        private bool _resizing;
        private string _resizeCorner = "se";

        private void BeginDrag(int index, FrameworkElement el, MouseButtonEventArgs e)
        {
            Select(index);
            if (index < 0 || index >= _elements.Count) return;
            _dragIndex = index;
            _resizing = false;
            _dragStart = e.GetPosition(_canvas);
            ComposedElement c = _elements[index];
            _dragOrig = new Rect(c.X, c.Y, c.W, c.H);
            el.CaptureMouse();
        }

        private void BeginResize(int index, string corner, FrameworkElement el, MouseButtonEventArgs e)
        {
            Select(index);
            if (index < 0 || index >= _elements.Count) return;
            _dragIndex = index;
            _resizing = true;
            _resizeCorner = corner;
            _dragStart = e.GetPosition(_canvas);
            ComposedElement c = _elements[index];
            _dragOrig = new Rect(c.X, c.Y, c.W, c.H);
            el.CaptureMouse();
        }

        private void MoveDrag(int index, FrameworkElement el, MouseEventArgs e)
        {
            if (_dragIndex != index || el == null || !el.IsMouseCaptured || _resizing) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;
            Point p = e.GetPosition(_canvas);
            double dx = (p.X - _dragStart.X) / CanvasW;
            double dy = (p.Y - _dragStart.Y) / CanvasH;
            ComposedElement c = _elements[index];
            double nx = _dragOrig.X + dx, ny = _dragOrig.Y + dy;
            // snap: retícula + guías de centro (como el lienzo F2.10)
            nx = SnapValue(nx, c.W);
            ny = SnapValue(ny, c.H);
            c.X = Math.Max(0, Math.Min(1 - c.W, nx));
            c.Y = Math.Max(0, Math.Min(1 - c.H, ny));
            RepositionAdorner(index, false);
        }

        private void MoveResize(int index, string corner, FrameworkElement el, MouseEventArgs e)
        {
            if (_dragIndex != index || el == null || !el.IsMouseCaptured || !_resizing) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;
            Point p = e.GetPosition(_canvas);
            double dx = (p.X - _dragStart.X) / CanvasW;
            double dy = (p.Y - _dragStart.Y) / CanvasH;
            ComposedElement c = _elements[index];
            double x = _dragOrig.X, y = _dragOrig.Y, w = _dragOrig.Width, h = _dragOrig.Height;
            if (corner == "se") { w = _dragOrig.Width + dx; h = _dragOrig.Height + dy; }
            else if (corner == "sw") { x = _dragOrig.X + dx; w = _dragOrig.Width - dx; h = _dragOrig.Height + dy; }
            else if (corner == "ne") { y = _dragOrig.Y + dy; w = _dragOrig.Width + dx; h = _dragOrig.Height - dy; }
            else if (corner == "nw") { x = _dragOrig.X + dx; y = _dragOrig.Y + dy; w = _dragOrig.Width - dx; h = _dragOrig.Height - dy; }
            w = Math.Max(0.04, Math.Min(1.0, w));
            h = Math.Max(0.04, Math.Min(1.0, h));
            x = Math.Max(0, Math.Min(1 - w, x));
            y = Math.Max(0, Math.Min(1 - h, y));
            c.X = x; c.Y = y; c.W = w; c.H = h;
            RepositionAdorner(index, true);
        }

        private void EndDrag(FrameworkElement el)
        {
            if (el != null && el.IsMouseCaptured) el.ReleaseMouseCapture();
            _dragIndex = -1;
            _resizing = false;
        }

        /// <summary>Snap a la retícula; además las guías de CENTRO alinean el
        /// elemento al centro del lienzo con tolerancia fina.</summary>
        private static double SnapValue(double v, double size)
        {
            double snapped = Math.Round(v / Snap) * Snap;
            if (Math.Abs(v - 0.5 - size / 2.0) < Guide) return 0.5 - size / 2.0;   // centrado horizontal
            if (Math.Abs(v - (0.5 - size / 2.0)) < Guide) return 0.5 - size / 2.0;
            return snapped;
        }

        private void RepositionAdorner(int index, bool reflowText)
        {
            if (index < 0 || index >= _elementAdorners.Count) return;
            Border host = _elementAdorners[index];
            ComposedElement c = _elements[index];
            Canvas.SetLeft(host, c.X * CanvasW);
            Canvas.SetTop(host, c.Y * CanvasH);
            host.Width = c.W * CanvasW;
            host.Height = c.H * CanvasH;
            if (reflowText)
            {
                // el texto re-fluye (auto-ajuste): reconstruir el visual
                ComposedElement e = _elements[index];
                FrameworkElement visual = e.Kind == ComposedElement.KindImage
                    ? BuildImageVisual(e)
                    : (FrameworkElement)BuildTextVisual(e);
                visual.Opacity = e.Opacity;
                Grid g = host.Child as Grid;
                if (g != null && g.Children.Count > 0)
                {
                    // el primer hijo es el visual; los demás son asas
                    g.Children.RemoveAt(0);
                    g.Children.Insert(0, visual);
                }
                else host.Child = visual;
            }
        }
        /* ==================================================== edición in-situ */

        private void BeginInlineEdit(int index)
        {
            if (_inlineEditor != null) CommitInlineEdit();
            ComposedElement e = _elements[index];
            Border host = index < _elementAdorners.Count ? _elementAdorners[index] : null;
            if (host == null) return;
            _inlineIndex = index;
            _inlineEditor = new TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                Foreground = Brushes.White, BorderBrush = Brushes.Orange,
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4),
                FontFamily = new FontFamily(string.IsNullOrEmpty(_theme.FontFace) ? "Segoe UI" : _theme.FontFace)
            };
            List<string> ls = e.Lines != null ? e.Lines : new List<string>();
            _inlineEditor.Text = string.Join("\n", ls.ToArray());
            Grid g = host.Child as Grid;
            if (g != null && g.Children.Count > 0) { g.Children.RemoveAt(0); g.Children.Insert(0, _inlineEditor); }
            else host.Child = _inlineEditor;
            _inlineEditor.Focus();
            _inlineEditor.SelectAll();
            _inlineEditor.LostFocus += delegate { CommitInlineEdit(); };
            _inlineEditor.PreviewKeyDown += delegate(object s, KeyEventArgs ev)
            {
                if (ev.Key == Key.Escape) { ev.Handled = true; CommitInlineEdit(false); }
                else if (ev.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                { ev.Handled = true; CommitInlineEdit(); }
            };
        }

        private void CommitInlineEdit(bool apply = true)
        {
            if (_inlineEditor == null || _inlineIndex < 0) return;
            int idx = _inlineIndex;
            TextBox editor = _inlineEditor;
            _inlineEditor = null; _inlineIndex = -1;
            if (apply && idx < _elements.Count)
            {
                ComposedElement e = _elements[idx];
                List<string> lines = new List<string>();
                foreach (string raw in editor.Text.Replace("\r\n", "\n").Split('\n'))
                    lines.Add(raw.TrimEnd());
                while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
                e.Lines = lines;
            }
            RenderAll();
            Select(idx);
        }

        /* ====================================================== capa selección */

        private void Select(int index)
        {
            if (_inlineEditor != null) CommitInlineEdit();
            _selected = index;
            RenderAll();
            if (index >= 0 && _lstLayers != null && index < _lstLayers.Items.Count)
                _lstLayers.SelectedIndex = index;
        }

        private void RefreshLayers()
        {
            if (_lstLayers == null) return;
            List<string> names = new List<string>(_elements.Count);
            for (int i = 0; i < _elements.Count; i++)
            {
                ComposedElement e = _elements[i];
                string label = e.Kind == ComposedElement.KindImage
                    ? "Imagen · " + System.IO.Path.GetFileName(e.ImagePath)
                    : "Texto · " + FirstLine(e);
                names.Add((i + 1).ToString(CultureInfo.InvariantCulture) + ". " + label);
            }
            _lstLayers.ItemsSource = names;
            if (_selected >= 0 && _selected < names.Count) _lstLayers.SelectedIndex = _selected;
        }

        private static string FirstLine(ComposedElement e)
        {
            if (e.Lines == null || e.Lines.Count == 0) return "(vacío)";
            string s = e.Lines[0];
            return s.Length > 34 ? s.Substring(0, 34) + "…" : s;
        }

        private void OnLayersSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_lstLayers == null) return;
            int sel = _lstLayers.SelectedIndex;
            if (sel >= 0 && sel < _elements.Count && sel != _selected)
            {
                _selected = sel;
                RenderAll();
            }
        }

        private void MoveLayer(int delta)
        {
            if (_selected < 0) return;
            int target = _selected + delta;
            if (target < 0 || target >= _elements.Count) return;
            ComposedElement tmp = _elements[_selected];
            _elements[_selected] = _elements[target];
            _elements[target] = tmp;
            _selected = target;
            RenderAll();
        }

        private void RefreshProps()
        {
            bool has = _selected >= 0 && _selected < _elements.Count;
            bool isText = has && _elements[_selected].Kind == ComposedElement.KindText;
            _lblProps.Text = !has ? "Nada seleccionado"
                : (isText ? "Elemento de TEXTO" : "Elemento de IMAGEN");
            _numFontSize.IsEnabled = isText;
            _cmbAlign.IsEnabled = isText;
            _cmbColor.IsEnabled = isText;
            _numOpacity.IsEnabled = has;
            _cmbFit.IsEnabled = has && !isText;
            if (has)
            {
                ComposedElement e = _elements[_selected];
                if (isText)
                {
                    _numFontSize.Value = e.FontSizePct;
                    _cmbAlign.SelectedIndex = Math.Max(0, Math.Min(2, e.Align));
                    _cmbColor.SelectedIndex = ColorIndex(e.Color);
                }
                _numOpacity.Value = (int)Math.Round(e.Opacity * 100);
                _cmbFit.SelectedIndex = e.Cover ? 1 : 0;
            }
        }

        private static int ColorIndex(string c)
        {
            switch ((c ?? string.Empty).ToUpperInvariant())
            {
                case "": return 0;
                case "#FFFFFFFF": return 1;
                case "#FF000000": return 2;
                case "#FFF0A93B": return 3;
                case "#FF5AA9E6": return 4;
                case "#FF54C87C": return 5;
                case "#FFE5484D": return 6;
                default: return 0;
            }
        }

        private static string ColorOf(int idx)
        {
            switch (idx)
            {
                case 1: return "#FFFFFFFF";
                case 2: return "#FF000000";
                case 3: return "#FFF0A93B";
                case 4: return "#FF5AA9E6";
                case 5: return "#FF54C87C";
                case 6: return "#FFE5484D";
                default: return string.Empty;
            }
        }

        private void ApplyPropsToSelection()
        {
            if (_selected < 0 || _selected >= _elements.Count) return;
            ComposedElement e = _elements[_selected];
            if (e.Kind == ComposedElement.KindText)
            {
                e.FontSizePct = (int)Math.Round(_numFontSize.Value);
                e.Align = _cmbAlign.SelectedIndex < 0 ? 1 : _cmbAlign.SelectedIndex;
                e.Color = ColorOf(_cmbColor.SelectedIndex < 0 ? 0 : _cmbColor.SelectedIndex);
            }
            else
            {
                e.Cover = _cmbFit.SelectedIndex == 1;
            }
            e.Opacity = Math.Max(0.1, Math.Min(1.0, _numOpacity.Value / 100.0));
            RenderAll();
        }

        /* ============================================================ acciones */

        private void AddTextElement()
        {
            ComposedElement e = new ComposedElement();
            e.Kind = ComposedElement.KindText;
            e.Lines = new List<string>(new string[] { "Nuevo texto" });
            e.X = 0.15; e.Y = 0.38; e.W = 0.7; e.H = 0.24;
            _elements.Add(e);
            RenderAll();
            Select(_elements.Count - 1);
        }

        private void AddImageElement()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "Agregar imagen";
            dlg.Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|Todos (*.*)|*.*";
            if (dlg.ShowDialog(this) != true) return;
            ComposedElement e = new ComposedElement();
            e.Kind = ComposedElement.KindImage;
            e.ImagePath = dlg.FileName;
            e.X = 0.3; e.Y = 0.2; e.W = 0.4; e.H = 0.5;
            e.Cover = false;
            _elements.Add(e);
            RenderAll();
            Select(_elements.Count - 1);
        }

        private void DuplicateSelected()
        {
            if (_selected < 0 || _selected >= _elements.Count) return;
            ComposedElement src = _elements[_selected];
            ComposedElement copy = new ComposedElement();
            copy.Kind = src.Kind; copy.X = Math.Min(0.95, src.X + 0.02); copy.Y = Math.Min(0.95, src.Y + 0.02);
            copy.W = src.W; copy.H = src.H; copy.Opacity = src.Opacity;
            copy.FontSizePct = src.FontSizePct; copy.Align = src.Align; copy.Color = src.Color;
            copy.ImagePath = src.ImagePath; copy.Cover = src.Cover;
            copy.Lines = new List<string>(src.Lines);
            _elements.Add(copy);
            RenderAll();
            Select(_elements.Count - 1);
        }

        private void DeleteSelected()
        {
            if (_selected < 0 || _selected >= _elements.Count) return;
            _elements.RemoveAt(_selected);
            _selected = -1;
            RenderAll();
        }

        private void OnEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (_inlineEditor != null && _inlineEditor.IsKeyboardFocused) return; // edición activa
            if (e.Key == Key.Delete || e.Key == Key.Back) { DeleteSelected(); e.Handled = true; }
            else if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            { DuplicateSelected(); e.Handled = true; }
            else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            { Save(); e.Handled = true; }
            else if (e.Key >= Key.Left && e.Key <= Key.Down)
            {
                if (_selected < 0 || _selected >= _elements.Count) return;
                double step = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 0.02 : 0.005;
                ComposedElement c = _elements[_selected];
                if (e.Key == Key.Left) c.X = Math.Max(0, c.X - step);
                if (e.Key == Key.Right) c.X = Math.Min(1 - c.W, c.X + step);
                if (e.Key == Key.Up) c.Y = Math.Max(0, c.Y - step);
                if (e.Key == Key.Down) c.Y = Math.Min(1 - c.H, c.Y + step);
                RepositionAdorner(_selected, false);
                e.Handled = true;
            }
        }

        private void Save()
        {
            if (_elements.Count == 0)
            {
                MessageBox.Show(this, "Agrega al menos un elemento de texto o imagen antes de guardar.",
                    "Editor de diapositivas", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_inlineEditor != null) CommitInlineEdit();
            SlideTitle = _titleBox.Text.Trim();
            DialogResult = true;
            Close();
        }
    }
}
