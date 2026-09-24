// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — Pages/LivePage.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
        /// <summary>v7.1.0: título del ítem que originó la slide.</summary>
        public string Title = string.Empty;
        /// <summary>v7.1.0: índice del ítem que originó la slide (-1 n/d).</summary>
        public int ItemIndex = -1;
        /// <summary>v7.1.0: todas las líneas (para el panel de texto completo).</summary>
        public List<string> Lines = new List<string>();
    }

    /// <summary>
    /// v7.1.0 «OPERADOR» (feedback #4): fila de la lista de ÍTEMS del
    /// escenario — lista de dos niveles estilo Holyrics: clic en el ítem para
    /// ver sus slides (sin proyectar), doble clic para proyectar la primera.
    /// </summary>
    public sealed class StageItemVm
    {
        public int ItemIndex;
        public int FirstSlideIndex;
        public string Number = "1";
        public string KindLabel = "Ítem";
        public string Title = string.Empty;
        public string SlideCount = string.Empty;

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

        /// <summary>
        /// v7.1.0 «OPERADOR»: nombre del escenario en la cabecera de la lista
        /// (feedback #2 — el nombre SIEMPRE visible y correcto).
        /// </summary>
        internal void SetScenarioName(string name)
        {
            txtScenarioName.Text = string.IsNullOrEmpty(name)
                ? "Sin escenario cargado"
                : "Escenario «" + name + "»";
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
            // v7.1.0 «OPERADOR» (feedback #4): la ventana de proyección cambia
            // de monitor SIN abrir otra — ProjectorShow sobre el estado actual.
            Shell.ReopenProjectorOnScreenChange();
        }

        /// <summary>
        /// v7.1.0 «OPERADOR» (feedback #3): persiste el ajuste de pantalla
        /// completa (la ventana nativa es SIEMPRE sin bordes desde v7.1.0).
        /// </summary>
        internal void OnFullscreenChanged(object sender, RoutedEventArgs e)
        {
            if (Shell == null || Shell._settings == null) return;
            Shell._settings.ProjectionFullscreen = IsFullscreenChecked();
        }

        /// <summary>
        /// v7.1.0 «OPERADOR» (feedback #3): refleja en el botón si la salida
        /// está visible (el cierre con X/ESC del usuario también se detecta
        /// por sondeo del estado del núcleo).
        /// </summary>
        internal void SetProjectorVisible(bool visible)
        {
            if (btnProjector == null) return;
            btnProjector.Text = visible ? "Ocultar proyector" : "Proyector";
            btnProjector.ToolTip = visible
                ? "Ocultar la ventana de proyección (F5) — también se cierra con X o ESC sin duplicarse"
                : "Mostrar la ventana de proyección (F5) — sin bordes; cerrarla con X o ESC no la duplica y se puede reabrir";
        }

        /* ---------------------------------------------------------- lista */

        private List<SlideRowVm> _allSlides = new List<SlideRowVm>();

        /// <summary>
        /// v7.1.0 «OPERADOR» (feedback #4): construye la lista de ÍTEMS del
        /// escenario a partir de las vistas de slide (agrupadas por ItemIndex)
        /// y refresca la lista de slides del ítem seleccionado.
        /// </summary>
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
                vm.Title = v.Title;
                vm.ItemIndex = v.ItemIndex;
                vm.Lines = v.Lines;
                vms.Add(vm);
            }
            _allSlides = vms;
            emptySlides.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefillItems();
        }

        /// <summary>Agrupa las slides por ítem y llena la lista superior.</summary>
        private void RefillItems()
        {
            List<StageItemVm> items = new List<StageItemVm>();
            for (int i = 0; i < _allSlides.Count; i++)
            {
                SlideRowVm vm = _allSlides[i];
                StageItemVm item = null;
                if (items.Count > 0)
                {
                    StageItemVm last = items[items.Count - 1];
                    if (last.ItemIndex == vm.ItemIndex) item = last;
                }
                if (item == null)
                {
                    item = new StageItemVm();
                    item.ItemIndex = vm.ItemIndex;
                    item.FirstSlideIndex = vm.Index;
                    item.Number = (items.Count + 1).ToString(CultureInfo.InvariantCulture);
                    item.KindLabel = _itemsSource != null && vm.ItemIndex >= 0 && vm.ItemIndex < _itemsSource.Count
                        ? StageItemVm.KindLabelOf(_itemsSource[vm.ItemIndex])
                        : "Ítem";
                    // Nombre del ítem: título de la vista, respaldo en la
                    // fuente de ítems, respaldo en la etiqueta de referencia.
                    item.Title = vm.Title.Length > 0 ? vm.Title : ItemTitle(vm.ItemIndex);
                    if (item.Title.Length == 0 && vm.RefLabel.Length > 0) item.Title = vm.RefLabel;
                    if (item.Title.Length == 0) item.Title = "Ítem";
                    items.Add(item);
                }
                item.SlideCount = (vm.Index - item.FirstSlideIndex + 1)
                    .ToString(CultureInfo.InvariantCulture);
            }
            lstItems.ItemsSource = items;
            if (items.Count > 0 && lstItems.SelectedIndex < 0) lstItems.SelectedIndex = 0;
            RefillSlidesForSelected();
        }

        private string ItemTitle(int itemIndex)
        {
            if (_itemsSource == null || itemIndex < 0 || itemIndex >= _itemsSource.Count)
                return string.Empty;
            ScenarioItem it = _itemsSource[itemIndex];
            if (it == null) return string.Empty;
            if (it.Title.Length > 0) return it.Title;
            if (it.Kind == "scripture" && it.Ref.Length > 0) return it.Ref;
            return "(sin nombre)";
        }

        private IList<ScenarioItem> _itemsSource;

        /// <summary>v7.1.0: el MainWindow publica los ítems para las etiquetas.</summary>
        internal void SetItemsSource(IList<ScenarioItem> items)
        {
            _itemsSource = items;
            RefillItems();
        }

        /// <summary>Slides del ítem seleccionado (con índices GLOBALES).</summary>
        private void RefillSlidesForSelected()
        {
            StageItemVm sel = lstItems.SelectedItem as StageItemVm;
            List<SlideRowVm> shown = new List<SlideRowVm>();
            if (sel != null)
            {
                foreach (SlideRowVm vm in _allSlides)
                    if (vm.ItemIndex == sel.ItemIndex) shown.Add(vm);
            }
            lstSlides.ItemsSource = shown;
            UpdateFullText();
        }

        private void OnItemsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefillSlidesForSelected();
        }

        private void OnItemsDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Doble clic en el ítem → proyectar su PRIMERA slide.
            StageItemVm sel = lstItems.SelectedItem as StageItemVm;
            if (sel != null && Shell != null) Shell.ShowSlideIndex(sel.FirstSlideIndex);
        }

        /// <summary>
        /// v7.1.0 «OPERADOR» (feedback #4): panel de TEXTO COMPLETO de la slide
        /// seleccionada — se lee sin proyectar (la Biblia entera, la letra…).
        /// </summary>
        private void UpdateFullText()
        {
            SlideRowVm vm = lstSlides.SelectedItem as SlideRowVm;
            if (vm == null)
            {
                txtFullTextLabel.Text = "Texto completo (sin proyectar)";
                txtFullText.Text = string.Empty;
                return;
            }
            txtFullTextLabel.Text = vm.RefLabel.Length > 0
                ? "Texto completo · " + vm.RefLabel
                : "Texto completo (sin proyectar)";
            StringBuilder sb = new StringBuilder();
            foreach (string l in vm.Lines)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(l == null ? string.Empty : l.Trim());
            }
            txtFullText.Text = sb.ToString();
        }

        internal int SelectedIndex()
        {
            SlideRowVm vm = lstSlides.SelectedItem as SlideRowVm;
            return vm != null ? vm.Index : -1;
        }

        internal void HighlightSlide(int currentIndex)
        {
            // v7.1.0 «OPERADOR»: la lista de slides muestra SOLO el ítem
            // seleccionado — si la slide EN VIVO pertenece a otro ítem, se
            // selecciona ese ítem (seguimiento en vivo estilo Holyrics).
            if (currentIndex >= 0 && currentIndex < _allSlides.Count)
            {
                int liveItem = _allSlides[currentIndex].ItemIndex;
                StageItemVm sel = lstItems.SelectedItem as StageItemVm;
                if (sel == null || sel.ItemIndex != liveItem)
                {
                    for (int i = 0; i < lstItems.Items.Count; i++)
                    {
                        StageItemVm cand = lstItems.Items[i] as StageItemVm;
                        if (cand != null && cand.ItemIndex == liveItem)
                        {
                            lstItems.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }
            List<SlideRowVm> shown = lstSlides.ItemsSource as List<SlideRowVm>;
            if (shown == null || shown.Count == 0)
            {
                lstSlides.SelectedItem = null;
                UpdateFullText();
                return;
            }
            SlideRowVm live = null;
            foreach (SlideRowVm vm in shown)
                if (vm.Index == currentIndex) { live = vm; break; }
            if (live == null)
            {
                lstSlides.SelectedItem = null;
                UpdateFullText();
                return;
            }
            if (!(lstSlides.SelectedItem is SlideRowVm) ||
                ((SlideRowVm)lstSlides.SelectedItem).Index != currentIndex)
            {
                lstSlides.SelectedItem = live;
                lstSlides.ScrollIntoView(live);
            }
            UpdateFullText();
        }

        private void OnSlidesSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFullText();
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
            if (Shell != null) Shell.LivePrev();
        }

        private void OnNextClick(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.LiveNext();
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
