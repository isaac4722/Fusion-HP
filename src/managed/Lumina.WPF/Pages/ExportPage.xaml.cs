// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — Pages/ExportPage.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
using System.Windows;
using System.Windows.Controls;

namespace lumina.wpf
{
    public partial class ExportPage : UserControl
    {
        internal MainWindow Shell;

        public ExportPage()
        {
            InitializeComponent();
        }

        public void Wire(MainWindow shell) { Shell = shell; }

        private void OnExportPptx(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ExportScenarioPptx();
        }

        private void OnExportPdf(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ExportScenarioPdf();
        }

        /// <summary>F4.13: exportación a archivos de imagen (5 formatos).</summary>
        private void OnExportImages(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ExportScenarioImages();
        }
    }
}
