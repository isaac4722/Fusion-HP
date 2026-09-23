// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — Pages/LivePage.xaml.cs
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
    /// Vista de fila de la lista de diapositivas (presentación mínima para el
    /// DataTemplate: etiqueta de índice, referencia y primera línea).
    /// </summary>
    public sealed class SlideRowVm
    {
        public int Index;
        public string IndexLabel = "1";
        public string RefLabel = string.Empty;
        public string FirstLine = string.Empty;
    }

    public partial class LivePage : UserControl
    {
        internal MainWindow Shell;

        public LivePage()
        {
            InitializeComponent();
        }

        /// <summary>Wiring tras la construcción (la llama MainWindow).</summary>
        public void Wire(MainWindow shell)
        {
            Shell = shell;
            FillScreens();
        }

        /* -------------------------------------------------------- pantallas */

        private void FillScreens()
        {
            try
            {
                System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
                cmbScreen.Items.Clear();
                for (int i = 0; i < screens.Length; i++)
                {
                    cmbScreen.Items.Add((i + 1).ToString(CultureInfo.InvariantCulture) + " · " +
                        screens[i].Bounds.Width + "×" + screens[i].Bounds.Height);
                }
            }
            catch (Exception) { cmbScreen.Items.Add("1 · principal"); }
            if (cmbScreen.Items.Count == 0) cmbScreen.Items.Add("1 · principal");
            cmbScreen.SelectedIndex = 0;
        }

        internal void ClampScreenIndex(int wanted)
        {
            if (cmbScreen.Items.Count > 0)
                cmbScreen.SelectedIndex = Math.Max(0, Math.Min(wanted, cmbScreen.Items.Count - 1));
        }

        internal int SelectedScreenIndex()
        {
            return cmbScreen.SelectedIndex < 0 ? 0 : cmbScreen.SelectedIndex;
        }

        internal bool IsFullscreenChecked()
        {
            return chkFullscreen.IsChecked == true;
        }

        private void OnScreenChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Shell == null || Shell._settings == null) return;
            Shell._settings.ProjectionScreen = SelectedScreenIndex();
            Shell.UpdateSidebarScreen();
        }

        /* ---------------------------------------------------------- lista */

        internal void FillSlides(List<SlideView> slides)
        {
            List<SlideRowVm> vms = new List<SlideRowVm>(slides.Count);
            foreach (SlideView v in slides)
            {
                SlideRowVm vm = new SlideRowVm();
                vm.Index = v.Index;
                vm.IndexLabel = (v.Index + 1).ToString(CultureInfo.InvariantCulture);
                vm.RefLabel = v.RefLabel;
                vm.FirstLine = v.FirstLine.Length > 0 ? v.FirstLine : " ";
                vms.Add(vm);
            }
            lstSlides.ItemsSource = vms;
            emptySlides.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        internal int SelectedIndex()
        {
            SlideRowVm vm = lstSlides.SelectedItem as SlideRowVm;
            return vm != null ? vm.Index : -1;
        }

        internal void HighlightSlide(int currentIndex)
        {
            List<SlideRowVm> vms = lstSlides.ItemsSource as List<SlideRowVm>;
            if (vms == null || vms.Count == 0) return;
            if (currentIndex < 0 || currentIndex >= vms.Count)
            {
                lstSlides.SelectedItem = null;
                return;
            }
            if (!(lstSlides.SelectedItem is SlideRowVm) ||
                ((SlideRowVm)lstSlides.SelectedItem).Index != currentIndex)
            {
                lstSlides.SelectedItem = vms[currentIndex];
                lstSlides.ScrollIntoView(vms[currentIndex]);
            }
        }

        private void OnSlidesDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Shell != null) Shell.ShowSelectedSlide();
        }

        private void OnSlidesKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (Shell != null) Shell.ShowSelectedSlide();
            }
        }

        /* ------------------------------------------------------- transporte */

        private void OnPrevClick(object sender, RoutedEventArgs e)
        {
            if (Shell != null && Shell.RequireEngine()) Shell._engine.Prev();
        }

        private void OnNextClick(object sender, RoutedEventArgs e)
        {
            if (Shell != null && Shell.RequireEngine()) Shell._engine.Next();
        }

        private void OnLiveClick(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ShowSelectedSlide();
        }

        private void OnBlackClick(object sender, RoutedEventArgs e)
        {
            if (Shell != null && Shell.RequireEngine()) Shell.ToggleBlack();
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            if (Shell != null && Shell.RequireEngine()) Shell._engine.Clear();
        }

        private void OnHelpClick(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ShowShortcutsDialog();
        }

        private void OnStageClick(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.OpenStageView();
        }

        private void OnDirectorClick(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.OpenDirectorView();
        }

        private void OnThirdClick(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ShowLowerThirdDialog();
        }

        private void OnVideoPauseClick(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ToggleVideoPause();
        }

        private void OnProjectorClick(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ShowProjector();
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.RefreshPreview();
        }

        /* ------------------------------------------------------ vista previa */

        internal void UpdatePreviewChrome()
        {
            bool live = imgPreview.Source != null;
            previewBorder.BorderBrush = live ? FindResource("AccentBrush") as Brush
                                             : FindResource("CardBorderBrush") as Brush;
            chipLive.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
            txtNoPreview.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
