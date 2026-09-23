// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — Pages/BiblePage.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace lumina.wpf
{
    public partial class BiblePage : UserControl
    {
        internal MainWindow Shell;

        public BiblePage()
        {
            InitializeComponent();
        }

        public void Wire(MainWindow shell) { Shell = shell; }

        internal void FillResults(List<BibleRowVm> vms)
        {
            lstResults.ItemsSource = vms;
        }

        internal BibleRowVm SelectedResult()
        {
            return lstResults.SelectedItem as BibleRowVm;
        }

        private void OnLoadToStage(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.LoadScriptureToStage();
        }

        private void OnResolve(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ResolveReference();
        }

        private void OnImportBib(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ImportBibFile();
        }

        private void OnImportZefania(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ImportZefaniaXml();
        }

        private void OnSearchClick(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.SearchBibleWords();
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (Shell != null) Shell.SearchBibleWords();
            }
        }

        private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Shell != null) Shell.LoadBibleResultToStage();
        }
    }
}
