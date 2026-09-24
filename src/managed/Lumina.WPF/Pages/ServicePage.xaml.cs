// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — Pages/ServicePage.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using lumina.core;

namespace lumina.wpf
{
    /// <summary>
    /// v7.1.0 «OPERADOR» (feedback #2): fila del culto con nombres CORRECTOS —
    /// tipo en español, título y detalle útil (artista, versión, archivo…).
    /// </summary>
    public sealed class ServiceItemVm
    {
        public string Number = "1";
        public string KindLabel = "Ítem";
        public string Title = string.Empty;
        public string Detail = string.Empty;

        public static string KindLabelOf(ScenarioItem it)
        {
            if (it == null) return "Ítem";
            switch (it.Kind)
            {
                case "song":      return "Canción";
                case "scripture": return "Pasaje";
                case "text":      return "Texto";
                case "image":     return "Imagen";
                case "video":     return "Video";
                case "pptx":      return "Presentación";
                case "composed":  return "Diseño";
                case "blank":     return "Blanco";
                default:           return it.Kind;
            }
        }

        public static string DetailOf(ScenarioItem it)
        {
            if (it == null) return string.Empty;
            if (it.Kind == "song" && it.Song != null)
            {
                string d = it.Song.Artist;
                if (it.Song.Blocks.Count > 0) d = AppendInfo(d, it.Song.Blocks.Count + " bloques");
                return d;
            }
            if (it.Kind == "scripture")
                return it.Ref.Length > 0
                    ? (it.Version.Length > 0 ? it.Ref + " · " + it.Version : it.Ref)
                    : it.Version;
            if (it.Kind == "pptx" && it.PptxPath.Length > 0)
                return System.IO.Path.GetFileName(it.PptxPath);
            if (it.Kind == "image" && it.ImagePath.Length > 0)
                return System.IO.Path.GetFileName(it.ImagePath);
            if (it.Kind == "video" && it.VideoPath.Length > 0)
                return System.IO.Path.GetFileName(it.VideoPath);
            if (it.Kind == "composed" && it.Composed != null)
                return it.Composed.Count + " elemento" + (it.Composed.Count == 1 ? "" : "s");
            if (it.Kind == "text" && it.Text.Length > 0)
            {
                string first = it.Text;
                int nl = first.IndexOf('\n');
                if (nl > 0) first = first.Substring(0, nl);
                return first.Length > 60 ? first.Substring(0, 60) + "…" : first;
            }
            return string.Empty;
        }

        private static string AppendInfo(string base_, string more)
        {
            if (base_ == null || base_.Length == 0) return more;
            return base_ + " · " + more;
        }
    }

    public partial class ServicePage : UserControl
    {
        internal MainWindow Shell;

        public ServicePage()
        {
            InitializeComponent();
        }

        public void Wire(MainWindow shell) { Shell = shell; }

        internal void FillItems(List<ServiceItemVm> rows)
        {
            lstItems.ItemsSource = rows;
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

        /// <summary>v7.1.0 «OPERADOR» (feedback #1): editor de diapositivas de lienzo libre.</summary>
        private void OnNewSlide(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.NewComposedSlide();
        }

        /// <summary>F2.14: biblioteca de recursos (imágenes/videos + etiquetas + re-vinculación).</summary>
        private void OnResourceLibrary(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.OpenResourceLibrary();
        }

        /// <summary>v7.1.0: doble clic en un ítem «Diseño» → reabrir el editor con sus elementos.</summary>
        private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Shell != null) Shell.EditSelectedServiceItem();
        }

        /// <summary>v7.1.0: Enter en un ítem «Diseño» → editar; en PPTX → sin extracción ya está listo.</summary>
        private void OnItemsKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (Shell != null) Shell.EditSelectedServiceItem();
            }
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

        private void OnImportPptxText(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ImportPptxTextToService();
        }

        private void OnSavePlan(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.SavePlanJson();
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
