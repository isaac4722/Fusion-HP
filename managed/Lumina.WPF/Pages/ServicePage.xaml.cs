// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — Pages/ServicePage.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace lumina.wpf
{
    public partial class ServicePage : UserControl
    {
        internal MainWindow Shell;

        public ServicePage()
        {
            InitializeComponent();
        }

        public void Wire(MainWindow shell) { Shell = shell; }

        internal void FillItems(List<string> labels)
        {
            lstItems.ItemsSource = labels;
        }

        internal int SelectedIndex()
        {
            return lstItems.SelectedIndex;
        }

        internal void SelectIndex(int i)
        {
            if (i >= 0 && i < lstItems.Items.Count) lstItems.SelectedIndex = i;
        }

        private void OnMoveUp(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.MoveItem(-1);
        }

        private void OnMoveDown(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.MoveItem(1);
        }

        private void OnRemove(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.RemoveItem();
        }

        private void OnAddVideo(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.AddVideoItemToService();
        }

        private void OnAddCurrentSong(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.AddCurrentSongToService();
        }

        private void OnAddCurrentScripture(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.AddCurrentScriptureToService();
        }

        private void OnAddImage(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.AddImageItemToService();
        }

        private void OnAddText(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.AddTextItemToService();
        }

        private void OnAddBlank(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.AddBlankItemToService();
        }

        private void OnImportSongJson(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ImportSongJson();
        }

        private void OnImportBib(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ImportBibFile();
        }

        private void OnImportPptx(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ImportPptxToService();
        }

        private void OnImportPlan(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ImportPlanJson();
        }

        private void OnSendToStage(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.SendServiceToStage();
        }
    }
}
