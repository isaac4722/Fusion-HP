// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — Support/PageHeader.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace lumina.wpf
{
    /// <summary>Cabecera de página: icono + título + subtítulo.</summary>
    public partial class PageHeader : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(PageHeader),
                new FrameworkPropertyMetadata(string.Empty));
        public static readonly DependencyProperty SubtitleProperty =
            DependencyProperty.Register("Subtitle", typeof(string), typeof(PageHeader),
                new FrameworkPropertyMetadata(string.Empty));
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register("Icon", typeof(Geometry), typeof(PageHeader),
                new FrameworkPropertyMetadata(null));

        public string Title { get { return (string)GetValue(TitleProperty); } set { SetValue(TitleProperty, value); } }
        public string Subtitle { get { return (string)GetValue(SubtitleProperty); } set { SetValue(SubtitleProperty, value); } }
        public Geometry Icon { get { return (Geometry)GetValue(IconProperty); } set { SetValue(IconProperty, value); } }

        public PageHeader() { InitializeComponent(); }
    }
}
