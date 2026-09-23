// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — managed/Lumina.WPF/Support/Widgets.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Widgets.cs : controles propios del sistema de diseño «Lumina Studio» (WPF).
//
//    * LumButton : botón con variante (Primary/Secondary/Danger/Ghost),
//      icono vectorial opcional, estado toggle (Active) y foco visible.
//      Los colores los resuelven los TRIGGERS de Controls.xaml (Variant es
//      DependencyProperty → los estilos pueden condicionar por ella).
//    * Chip     : píldora de estado (cabecera: Núcleo/BD/API; EN VIVO…).
//    * NavItem  : botón de navegación del sidebar con píldora activa ámbar.
//
//  Regla de la skill qt-ui-design §1.3: teclado y foco visibles en TODO
//  control interactivo (los estilos pintan el borde de foco).
// ============================================================================
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace lumina.wpf
{
    /// <summary>Botón del sistema de diseño (variante + icono + estado activo).</summary>
    public class LumButton : Button
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(LumButton),
                new FrameworkPropertyMetadata(string.Empty, OnContentChanged));
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register("Icon", typeof(Geometry), typeof(LumButton),
                new FrameworkPropertyMetadata(null, OnContentChanged));
        public static readonly DependencyProperty VariantProperty =
            DependencyProperty.Register("Variant", typeof(string), typeof(LumButton),
                new FrameworkPropertyMetadata("Secondary", OnVariantChanged));
        public static readonly DependencyProperty ActiveProperty =
            DependencyProperty.Register("Active", typeof(bool), typeof(LumButton),
                new FrameworkPropertyMetadata(false));

        public string Text { get { return (string)GetValue(TextProperty); } set { SetValue(TextProperty, value); } }
        public Geometry Icon { get { return (Geometry)GetValue(IconProperty); } set { SetValue(IconProperty, value); } }
        public string Variant { get { return (string)GetValue(VariantProperty); } set { SetValue(VariantProperty, value); } }
        /// <summary>Estado toggle (p. ej. «Negro» encendido) — se pinta por triggers.</summary>
        public bool Active { get { return (bool)GetValue(ActiveProperty); } set { SetValue(ActiveProperty, value); } }

        static LumButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(LumButton),
                new FrameworkPropertyMetadata(typeof(LumButton)));
        }

        private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LumButton)d).RebuildContent();
        }

        private static void OnVariantChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // La variante la pintan los triggers del estilo: nada que reconstruir.
        }

        /// <summary>Contenido = icono + texto (icono solo si no hay texto).</summary>
        private void RebuildContent()
        {
            if (Icon == null)
            {
                Content = CreateText();
                return;
            }
            Path icon = new Path();
            icon.Data = Icon;
            icon.Width = 17; icon.Height = 17;
            icon.Stretch = Stretch.Uniform;
            icon.StrokeThickness = 2;
            icon.StrokeStartLineCap = PenLineCap.Round;
            icon.StrokeEndLineCap = PenLineCap.Round;
            icon.StrokeLineJoin = PenLineJoin.Round;
            icon.VerticalAlignment = VerticalAlignment.Center;
            // El icono sigue SIEMPRE al primer plano del botón (estados incluidos).
            icon.SetBinding(Path.StrokeProperty,
                new System.Windows.Data.Binding("Foreground") { Source = this });
            if (string.IsNullOrEmpty(Text))
            {
                Content = icon;
                return;
            }
            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            sp.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(0, 1, 7, 1);
            sp.Children.Add(icon);
            sp.Children.Add(CreateText());
            Content = sp;
        }

        private TextBlock CreateText()
        {
            TextBlock tb = new TextBlock();
            tb.Text = Text ?? string.Empty;
            tb.FontFamily = new FontFamily("Segoe UI");
            tb.FontSize = 12.5;
            tb.FontWeight = FontWeights.SemiBold;
            tb.VerticalAlignment = VerticalAlignment.Center;
            tb.SetBinding(TextBlock.ForegroundProperty,
                new System.Windows.Data.Binding("Foreground") { Source = this });
            return tb;
        }
    }

    /// <summary>
    /// Tarjeta del sistema de diseño: ContentControl con plantilla (cabecera
    /// con icono + título, y ContentPresenter para el contenido de la página).
    /// Como Control con Template (no UserControl) admite hijos declarados en
    /// el XAML de la página que la usa.
    /// </summary>
    public class Card : ContentControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(Card),
                new FrameworkPropertyMetadata(string.Empty));
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register("Icon", typeof(Geometry), typeof(Card),
                new FrameworkPropertyMetadata(null));
        public static readonly DependencyProperty HeaderExtraProperty =
            DependencyProperty.Register("HeaderExtra", typeof(object), typeof(Card),
                new FrameworkPropertyMetadata(null));

        public string Title { get { return (string)GetValue(TitleProperty); } set { SetValue(TitleProperty, value); } }
        public Geometry Icon { get { return (Geometry)GetValue(IconProperty); } set { SetValue(IconProperty, value); } }
        public object HeaderExtra { get { return (object)GetValue(HeaderExtraProperty); } set { SetValue(HeaderExtraProperty, value); } }

        static Card()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(Card),
                new FrameworkPropertyMetadata(typeof(Card)));
        }
    }

    /// <summary>Píldora de estado (chips de la cabecera y «EN VIVO»).</summary>
    public class Chip : Control
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(Chip),
                new FrameworkPropertyMetadata(string.Empty));

        public string Text { get { return (string)GetValue(TextProperty); } set { SetValue(TextProperty, value); } }

        static Chip()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(Chip),
                new FrameworkPropertyMetadata(typeof(Chip)));
            ForegroundProperty.OverrideMetadata(typeof(Chip),
                new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xB5))));
            BorderBrushProperty.OverrideMetadata(typeof(Chip),
                new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))));
        }

        /// <summary>Texto + color de estado (los pinta Foreground/BorderBrush).</summary>
        public void SetState(string text, Brush state)
        {
            Text = text ?? string.Empty;
            if (state != null)
            {
                Foreground = state;
                BorderBrush = state;
            }
        }
    }

    /// <summary>Botón de navegación del sidebar (píldora activa ámbar por estilo).</summary>
    public class NavItem : Button
    {
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register("Icon", typeof(Geometry), typeof(NavItem),
                new FrameworkPropertyMetadata(null));
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label", typeof(string), typeof(NavItem),
                new FrameworkPropertyMetadata(string.Empty));
        public static readonly DependencyProperty IsCurrentProperty =
            DependencyProperty.Register("IsCurrent", typeof(bool), typeof(NavItem),
                new FrameworkPropertyMetadata(false));

        public Geometry Icon { get { return (Geometry)GetValue(IconProperty); } set { SetValue(IconProperty, value); } }
        public string Label { get { return (string)GetValue(LabelProperty); } set { SetValue(LabelProperty, value); } }
        /// <summary>Página activa: píldora ámbar (lo pintan los triggers del estilo).</summary>
        public bool IsCurrent { get { return (bool)GetValue(IsCurrentProperty); } set { SetValue(IsCurrentProperty, value); } }

        static NavItem()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(NavItem),
                new FrameworkPropertyMetadata(typeof(NavItem)));
        }
    }
}
