// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — Support/EmptyState.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace lumina.wpf
{
    /// <summary>Estado vacío con icono + título + nota explicativa.</summary>
    public partial class EmptyState : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(EmptyState),
                new FrameworkPropertyMetadata(string.Empty));
        public static readonly DependencyProperty NoteProperty =
            DependencyProperty.Register("Note", typeof(string), typeof(EmptyState),
                new FrameworkPropertyMetadata(string.Empty));
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register("Icon", typeof(Geometry), typeof(EmptyState),
                new FrameworkPropertyMetadata(null));

        public string Title { get { return (string)GetValue(TitleProperty); } set { SetValue(TitleProperty, value); } }
        public string Note { get { return (string)GetValue(NoteProperty); } set { SetValue(NoteProperty, value); } }
        public Geometry Icon { get { return (Geometry)GetValue(IconProperty); } set { SetValue(IconProperty, value); } }

        public EmptyState() { InitializeComponent(); }
    }
}
