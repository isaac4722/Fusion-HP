// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — Pages/SongsPage.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using lumina.core;

namespace lumina.wpf
{
    public partial class SongsPage : UserControl
    {
        internal MainWindow Shell;

        public SongsPage()
        {
            InitializeComponent();
            UpdateKeyLabel();
        }

        public void Wire(MainWindow shell) { Shell = shell; }

        internal void FillResults(List<SongRowVm> vms)
        {
            lstResults.ItemsSource = vms;
        }

        internal SongRowVm SelectedResult()
        {
            return lstResults.SelectedItem as SongRowVm;
        }

        /// <summary>Tonalidad detectada en vivo (criterio del núcleo, ChordUtil).</summary>
        private void UpdateKeyLabel()
        {
            lblSongKey.Text = "Tono: " + ChordUtil.DetectKey(txtLyrics.Text ?? string.Empty);
        }

        private void OnLyricsChanged(object sender, TextChangedEventArgs e)
        {
            UpdateKeyLabel();
        }

        private void OnTransposeNow(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.TransposeEditorNow();
        }

        private void OnLoadToStage(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.LoadSongToStage();
        }

        private void OnSaveToDb(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.SaveSongToDb();
        }

        private void OnSearchClick(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.SearchSongs();
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (Shell != null) Shell.SearchSongs();
            }
        }

        private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Shell != null) Shell.LoadResultToEditor();
        }
    }
}
