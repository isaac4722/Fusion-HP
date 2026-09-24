// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — Pages/BiblePage.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using lumina.core;

namespace lumina.wpf
{
    /// <summary>
    /// v7.1.0 «OPERADOR»: índice del LIBRO en el combo (1..66 — la tabla
    /// BooksTable es el espejo exacto del núcleo).
    /// </summary>
    public sealed class BookComboVm
    {
        public int Number;
        public string Label = string.Empty;
    }

    public partial class BiblePage : UserControl
    {
        internal MainWindow Shell;

        public BiblePage()
        {
            InitializeComponent();
            FillBooks();
        }

        public void Wire(MainWindow shell) { Shell = shell; }

        /// <summary>v7.1.0 (feedback #7): los 66 libros SIEMPRE visibles.</summary>
        private void FillBooks()
        {
            List<BookComboVm> books = new List<BookComboVm>(66);
            for (int n = 1; n <= 66; n++)
            {
                BookComboVm vm = new BookComboVm();
                vm.Number = n;
                vm.Label = BooksTable.NameOf(n);
                books.Add(vm);
            }
            cboBook.ItemsSource = books;
            cboBook.DisplayMemberPath = "Label";
            if (cboBook.Items.Count > 43) cboBook.SelectedIndex = 43; // Juan
        }

        /// <summary>v7.1.0 (feedback #7): versiones instaladas en la BD.</summary>
        internal void FillVersions(List<string> versions)
        {
            cboVersion.ItemsSource = versions;
            cboVersion2.ItemsSource = new List<string>(versions);
            if (versions.Count > 0 && cboVersion.SelectedIndex < 0)
                cboVersion.SelectedIndex = 0;
            if (versions.Count > 1 && cboVersion2.SelectedIndex < 0)
                cboVersion2.SelectedIndex = 1;
        }

        /// <summary>Libro seleccionado (1..66; -1 si nada).</summary>
        internal int SelectedBook()
        {
            BookComboVm vm = cboBook.SelectedItem as BookComboVm;
            return vm != null ? vm.Number : -1;
        }

        internal int SelectedChapter()
        {
            return numChapter.IntValue;
        }

        internal string SelectedVersion()
        {
            object v = cboVersion.SelectedItem;
            return v != null ? v.ToString() : txtVersion.Text.Trim();
        }

        internal string SelectedVersion2()
        {
            object v = cboVersion2.SelectedItem;
            return v != null ? v.ToString() : string.Empty;
        }

        internal bool IsCompareChecked()
        {
            return chkCompare.IsChecked == true;
        }

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

        /// <summary>v7.1.0 (feedback #7): leer capítulo sin proyectar.</summary>
        private void OnReadChapter(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ReadBibleChapter();
        }

        /// <summary>v7.1.0: capítulo (o comparación) al escenario.</summary>
        private void OnLoadChapterToStage(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.LoadBibleChapterToStage();
        }

        private void OnImportBib(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ImportBibFile();
        }

        private void OnImportZefania(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ImportZefaniaXml();
        }

        private void OnImportOsis(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ImportOsisXml();
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
