// ============================================================================
//  LuminaPresentation Suite — Pages/HomePage.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  HomePage: pantalla de inicio (mosaicos) del flujo PowerStudio — la
//  referencia H-P-Web-Version-Ref abre en «home» y de ahí salta a crear o
//  presentar. Muestra «Continuar» solo cuando hay último proyecto guardado.
// ============================================================================
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace lumina.wpf
{
    public partial class HomePage : UserControl
    {
        internal MainWindow Shell;

        public HomePage()
        {
            InitializeComponent();
        }

        public void Wire(MainWindow shell) { Shell = shell; }

        /// <summary>Refresca el mosaico «Continuar» (se llama al navegar a Inicio).</summary>
        internal void RefreshTiles()
        {
            string path = Shell != null ? Shell.LastProjectPath : string.Empty;
            bool has = !string.IsNullOrEmpty(path) && File.Exists(path);
            cardResume.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            txtResumePath.Text = has ? path : "—";
        }

        private void OnResumePresent(object sender, RoutedEventArgs e)
        {
            if (Shell == null) return;
            if (Shell.ResumeLastProject()) { Shell.NavigateToIndex(0); Shell.ShowProjector(); }
        }

        private void OnResumeStudio(object sender, RoutedEventArgs e)
        {
            if (Shell == null) return;
            if (Shell.ResumeLastProject()) Shell.NavigateToIndex(10);
        }

        private void OnNewStudio(object sender, RoutedEventArgs e)
        {
            if (Shell == null) return;
            Shell.NavigateToIndex(10);
            Shell.pageStudio.NewSlide();
        }

        private void OnOpenPlan(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ImportPlanJson();
        }

        private void OnImportPptx(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ImportPptxToService();
        }

        private void OnGoLive(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.NavigateToIndex(0);
        }

        private void OnProjector(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ShowProjector();
        }

        private void OnService(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.NavigateToIndex(4);
        }

        private void OnSongs(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.NavigateToIndex(1);
        }

        private void OnBible(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.NavigateToIndex(2);
        }

        private void OnThemes(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.NavigateToIndex(3);
        }

        private void OnResources(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.OpenResourceLibrary();
        }
    }
}
