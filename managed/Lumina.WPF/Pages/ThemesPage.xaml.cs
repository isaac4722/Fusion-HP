// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — Pages/ThemesPage.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Vista previa del tema con la MISMA técnica del núcleo (contorno por
//  silueta + sombra), en WPF: FormattedText.BuildGeometry → tres Paths
//  (sombra desplazada · contorno · relleno). Se re-dibuja con cada cambio.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfPath = System.Windows.Shapes.Path;

namespace lumina.wpf
{
    public partial class ThemesPage : UserControl
    {
        internal MainWindow Shell;

        public ThemesPage()
        {
            InitializeComponent();
        }

        public void Wire(MainWindow shell) { Shell = shell; }

        /* ------------------------------------------------------ swatches */

        internal void SetSwatches(Color bg, Color fg, Color accent,
                                  string bgHex, string fgHex, string accentHex)
        {
            swBg.Background = new SolidColorBrush(bg);
            swFg.Background = new SolidColorBrush(fg);
            swAccent.Background = new SolidColorBrush(accent);
            hexBgText = bgHex ?? string.Empty;
            hexFgText = fgHex ?? string.Empty;
            hexAccentText = accentHex ?? string.Empty;
            hexBg.Text = hexBgText.ToUpper(CultureInfo.InvariantCulture);
            hexFg.Text = hexFgText.ToUpper(CultureInfo.InvariantCulture);
            hexAccent.Text = hexAccentText.ToUpper(CultureInfo.InvariantCulture);
        }

        private string hexBgText = "#FF0B1F2A";
        private string hexFgText = "#FFFFFFFF";
        private string hexAccentText = "#FF3AA6B9";

        internal Color ColorBg() { return BrushColor(swBg.Background); }
        internal Color ColorFg() { return BrushColor(swFg.Background); }
        internal Color ColorAccent() { return BrushColor(swAccent.Background); }

        internal string HexBg() { return hexBgText; }
        internal string HexFg() { return hexFgText; }
        internal string HexAccent() { return hexAccentText; }

        private static Color BrushColor(System.Windows.Media.Brush b)
        {
            SolidColorBrush s = b as SolidColorBrush;
            if (s != null) return s.Color;
            return Colors.Black;
        }

        private void OnPickBg(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.PickThemeColor("Fondo del tema", true, false, false);
        }

        private void OnPickFg(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.PickThemeColor("Color del texto", false, true, false);
        }

        private void OnPickAccent(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.PickThemeColor("Color de acento", false, false, true);
        }

        private void OnBrowseImage(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "Imagen de fondo del tema";
            dlg.Filter = "Imágenes (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|Todos los archivos (*.*)|*.*";
            if (dlg.ShowDialog() == true)
                txtImagePath.Text = dlg.FileName;
        }

        /* ------------------------------------------------- biblioteca */

        internal void FillThemes(List<ThemeRowVm> vms, string selectName)
        {
            lstThemes.ItemsSource = vms;
            if (selectName != null)
            {
                for (int i = 0; i < vms.Count; i++)
                {
                    if (string.Equals(vms[i].Name, selectName, StringComparison.OrdinalIgnoreCase))
                    {
                        lstThemes.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        internal string SelectedTheme()
        {
            ThemeRowVm vm = lstThemes.SelectedItem as ThemeRowVm;
            return vm != null ? vm.Name : null;
        }

        private void OnSaveTheme(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.SaveThemeToLibrary();
        }

        private void OnLoadTheme(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.LoadThemeFromLibrary();
        }

        private void OnThemeDoubleClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (Shell != null) Shell.LoadThemeFromLibrary();
        }

        private void OnRenameTheme(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.RenameThemeInLibrary();
        }

        private void OnDeleteTheme(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.DeleteThemeFromLibrary();
        }

        private void OnResetTheme(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ResetThemeEditor();
        }

        private void OnSaveToSettings(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.SaveThemeToSettings();
        }

        private void OnApplyToStage(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.ApplyThemeToStage();
        }

        /* ------------------------------------------------- vista previa */

        private void OnThemeEdited(object sender, RoutedEventArgs e) { InvalidatePreview(); }
        private void OnNumericChanged(object sender, EventArgs e) { InvalidatePreview(); }
        private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs e) { InvalidatePreview(); }

        internal void InvalidatePreview()
        {
            if (themePreview != null) RepaintPreview();
        }

        /// <summary>Repinta la muestra (color de fondo, imagen opcional, texto).</summary>
        private void RepaintPreview()
        {
            Canvas c = themePreview;
            if (c == null || c.ActualWidth < 20 || c.ActualHeight < 20) return;
            c.Children.Clear();
            double w = c.ActualWidth, h = c.ActualHeight;

            Color bg = ColorBg();
            Color fg = ColorFg();
            Color ac = ColorAccent();
            c.Background = new SolidColorBrush(bg);

            // Imagen de fondo opcional (Contener/Cubrir).
            string imgPath = (txtImagePath.Text ?? string.Empty).Trim();
            if (imgPath.Length > 0 && File.Exists(imgPath))
            {
                try
                {
                    BitmapImage bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(imgPath);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    Image img = new Image { Source = bi };
                    bool cover = cmbImageMode.SelectedIndex != 0;
                    double sx = w / bi.PixelWidth, sy = h / bi.PixelHeight;
                    double s = cover ? Math.Max(sx, sy) : Math.Min(sx, sy);
                    img.Width = bi.PixelWidth * s;
                    img.Height = bi.PixelHeight * s;
                    img.SetValue(Canvas.LeftProperty, (w - img.Width) / 2.0);
                    img.SetValue(Canvas.TopProperty, (h - img.Height) / 2.0);
                    c.Children.Add(img);
                }
                catch (Exception) { /* imagen ilegible: se ignora */ }
            }

            int shadow = numShadow.IntValue;
            double outline = numOutline.Value;
            bool bold = chkBold.IsChecked == true;
            bool upper = chkUpper.IsChecked == true;
            string face = cmbFont.SelectedItem != null
                ? Convert.ToString(((ComboBoxItem)cmbFont.SelectedItem).Content, CultureInfo.InvariantCulture)
                : "Segoe UI";
            double sizePx = numSize.Value;

            // Referencia arriba-izquierda en el color de acento.
            DrawLabel(c, "Jn 3:16", 14, 12, new SolidColorBrush(ac), Math.Max(9.0, sizePx * 0.22), face);

            // El motor dibuja a 1080p lógico: escalar al alto de la muestra
            // (4x como en la versión WinForms — es una MUESTRA legible).
            double scale = h / 1080.0 * 4.0;
            string verse = upper
                ? "PORQUE DE TAL MANERA AMÓ DIOS AL MUNDO, QUE HA DADO A SU HIJO UNIGÉNITO…"
                : "Porque de tal manera amó Dios al mundo, que ha dado a su Hijo unigénito…";
            Typeface type = MakeTypeface(face, bold);
            FormattedText ft = MakeText(verse, type, Math.Max(9.0, sizePx * scale));
            ft.MaxTextWidth = w * 0.84;
            ft.TextAlignment = TextAlignment.Center;

            Geometry geo = ft.BuildGeometry(new Point(0, 0));
            Rect bounds = geo.Bounds;

            // Sombra: copia desplazada con alpha del tema.
            if (shadow > 0)
            {
                WpfPath sh = new WpfPath();
                sh.Data = geo;
                sh.Fill = new SolidColorBrush(Color.FromArgb((byte)Math.Max(0, Math.Min(255, shadow)),
                    0, 0, 0));
                double dx = Math.Max(2.0, h * 0.012);
                sh.RenderTransform = new TranslateTransform(-bounds.X + (w - bounds.Width) / 2.0 + dx,
                                                             -bounds.Y + (h - bounds.Height) / 2.0 + dx);
                c.Children.Add(sh);
            }

            // Contorno por silueta (trazo alrededor de la geometría).
            if (outline > 0)
            {
                WpfPath ol = new WpfPath();
                ol.Data = geo;
                ol.Stroke = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
                ol.StrokeThickness = Math.Max(1.2, outline * scale * 1.2);
                ol.StrokeLineJoin = PenLineJoin.Round;
                ol.Fill = null;
                ol.RenderTransform = new TranslateTransform(-bounds.X + (w - bounds.Width) / 2.0,
                                                            -bounds.Y + (h - bounds.Height) / 2.0);
                c.Children.Add(ol);
            }

            // Relleno del texto.
            WpfPath fill = new WpfPath();
            fill.Data = geo;
            fill.Fill = new SolidColorBrush(fg);
            fill.RenderTransform = new TranslateTransform(-bounds.X + (w - bounds.Width) / 2.0,
                                                          -bounds.Y + (h - bounds.Height) / 2.0);
            c.Children.Add(fill);

            // Marca «PREVIEW» abajo-derecha en acento.
            DrawLabel(c, "● PREVIEW", Math.Max(12, w - 96), Math.Max(12, h - 26),
                new SolidColorBrush(ac), 11, face);
        }

        private static Typeface MakeTypeface(string face, bool bold)
        {
            return new Typeface(new FontFamily(face), FontStyles.Normal,
                bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
        }

        private static FormattedText MakeText(string text, Typeface type, double size)
        {
            return new FormattedText(text ?? string.Empty,
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                type, size, Brushes.White, 1.0);
        }

        /// <summary>Etiqueta de texto simple sobre el lienzo (referencia/marca).</summary>
        private static void DrawLabel(Canvas c, string text, double x, double y, Brush fill,
                                      double size, string face)
        {
            TextBlock tb = new TextBlock();
            tb.Text = text ?? string.Empty;
            tb.Foreground = fill;
            tb.FontSize = size;
            tb.FontFamily = new FontFamily(face);
            tb.FontWeight = FontWeights.SemiBold;
            tb.SetValue(Canvas.LeftProperty, x);
            tb.SetValue(Canvas.TopProperty, y);
            c.Children.Add(tb);
        }
    }
}
