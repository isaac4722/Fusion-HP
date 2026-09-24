// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — managed/Lumina.WPF/MainWindow.Themes.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MainWindow.Themes.cs (5/5): editor de temas (colores/tipografía/efectos/
//  imagen de fondo), biblioteca data\themes y aplicación al escenario.
//  La vista previa en vivo la pinta ThemesPage con geometría de texto WPF
//  (FormattedText.BuildGeometry → contorno por silueta + sombra, la MISMA
//  técnica del núcleo).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using lumina.core;

namespace lumina.wpf
{
    public partial class MainWindow : Window
    {
        internal string _appliedThemeName = "—";

        private static readonly string[] ThemeFonts =
            { "Segoe UI", "Arial", "Georgia", "Verdana", "Trebuchet MS", "Tahoma" };

        private string ThemesDirPath()
        {
            return Path.Combine(_settings.DataDir, "themes");
        }

        /// <summary>Nombre de archivo seguro (portable) para un tema.</summary>
        private static string SafeThemeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Sin nombre";
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == '\\' || chars[i] == '/')
                    chars[i] = '_';
            }
            string s = new string(chars).Trim();
            if (s.Length == 0) s = "Sin nombre";
            if (s.Length > 60) s = s.Substring(0, 60);
            return s;
        }

        /// <summary>Recarga la lista desde data\themes; selecciona selectName.</summary>
        internal void RefreshThemeLibrary(string selectName)
        {
            List<ThemeRowVm> vms = new List<ThemeRowVm>();
            try
            {
                string dir = ThemesDirPath();
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "*.json");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (string f in files)
                    {
                        ThemeRowVm vm = new ThemeRowVm();
                        vm.Name = Path.GetFileNameWithoutExtension(f);
                        vm.InUse = string.Equals(vm.Name, _appliedThemeName, StringComparison.OrdinalIgnoreCase);
                        vms.Add(vm);
                    }
                }
            }
            catch (Exception) { }
            pageThemes.FillThemes(vms, selectName);
            pageThemes.lblThemeApplied.Text = "En uso: " + _appliedThemeName;
        }

        internal void SaveThemeToLibrary()
        {
            Theme t = BuildThemeFromControls();
            _theme = t;
            try
            {
                string dir = ThemesDirPath();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, SafeThemeFileName(t.Name) + ".json");
                File.WriteAllText(path, MiniJson.Serialize(t.ToDict()) + "\n", new UTF8Encoding(false));
                SaveSettings();
                RefreshThemeLibrary(t.Name);
                Status("Tema «" + t.Name + "» guardado en la biblioteca (data\\themes).");
            }
            catch (Exception ex)
            {
                Status("No se pudo guardar el tema: " + ex.Message);
            }
        }

        internal void LoadThemeFromLibrary()
        {
            string name = pageThemes.SelectedTheme();
            if (name == null)
            {
                Status("Selecciona un tema de la biblioteca (o doble clic para cargarlo).");
                return;
            }
            string path = Path.Combine(ThemesDirPath(), SafeThemeFileName(name) + ".json");
            try
            {
                Dictionary<string, object> o = MiniJson.Parse(File.ReadAllText(path, new UTF8Encoding(false)));
                _theme = Theme.FromDict(o);
                ApplyThemeToControls();
                Status("Tema «" + _theme.Name + "» cargado al editor.");
            }
            catch (Exception ex)
            {
                Status("No se pudo cargar el tema «" + name + "»: " + ex.Message);
            }
        }

        internal void RenameThemeInLibrary()
        {
            string old = pageThemes.SelectedTheme();
            if (old == null)
            {
                Status("Selecciona un tema de la biblioteca.");
                return;
            }
            string neu = (pageThemes.txtThemeName.Text ?? string.Empty).Trim();
            if (neu.Length == 0 || string.Equals(neu, old, StringComparison.OrdinalIgnoreCase))
            {
                Status("Escribe el nuevo nombre en el campo «Nombre» y pulsa Renombrar.");
                return;
            }
            try
            {
                string dir = ThemesDirPath();
                string oldPath = Path.Combine(dir, SafeThemeFileName(old) + ".json");
                string newPath = Path.Combine(dir, SafeThemeFileName(neu) + ".json");
                if (!File.Exists(oldPath)) { Status("El archivo del tema ya no está (¿se movió?)."); return; }
                if (File.Exists(newPath)) { Status("Ya existe un tema llamado «" + neu + "»."); return; }
                Dictionary<string, object> o = MiniJson.Parse(File.ReadAllText(oldPath, new UTF8Encoding(false)));
                o["name"] = neu;
                File.WriteAllText(newPath, MiniJson.Serialize(o) + "\n", new UTF8Encoding(false));
                File.Delete(oldPath);
                if (string.Equals(_appliedThemeName, old, StringComparison.OrdinalIgnoreCase)) _appliedThemeName = neu;
                if (string.Equals(_settings.Theme, old, StringComparison.OrdinalIgnoreCase))
                {
                    _settings.Theme = neu;
                    SaveSettings();
                }
                RefreshThemeLibrary(neu);
                pageThemes.txtThemeName.Text = neu;
                Status("Tema «" + old + "» renombrado a «" + neu + "».");
            }
            catch (Exception ex)
            {
                Status("No se pudo renombrar: " + ex.Message);
            }
        }

        internal void DeleteThemeFromLibrary()
        {
            string name = pageThemes.SelectedTheme();
            if (name == null)
            {
                Status("Selecciona un tema de la biblioteca.");
                return;
            }
            if (string.Equals(name, "Predeterminado", StringComparison.OrdinalIgnoreCase))
            {
                Status("El tema «Predeterminado» no se puede eliminar.");
                return;
            }
            try
            {
                string path = Path.Combine(ThemesDirPath(), SafeThemeFileName(name) + ".json");
                if (File.Exists(path)) File.Delete(path);
                if (string.Equals(_appliedThemeName, name, StringComparison.OrdinalIgnoreCase))
                    _appliedThemeName = "—";
                RefreshThemeLibrary(null);
                Status("Tema «" + name + "» eliminado de la biblioteca.");
            }
            catch (Exception ex)
            {
                Status("No se pudo eliminar: " + ex.Message);
            }
        }

        /* ------------------------------------------------------- editor (form) */

        /// <summary>Parsea "#RRGGBB" o "#AARRGGBB" (tolerante; negro si es inválido).</summary>
        internal static Color ParseHexColor(string hex)
        {
            try
            {
                string s = (hex ?? string.Empty).Trim().TrimStart('#');
                if (s.Length == 6) s = "FF" + s;
                if (s.Length != 8) return Colors.Black;
                return Color.FromArgb(
                    Convert.ToByte(s.Substring(0, 2), 16),
                    Convert.ToByte(s.Substring(2, 2), 16),
                    Convert.ToByte(s.Substring(4, 2), 16),
                    Convert.ToByte(s.Substring(6, 2), 16));
            }
            catch (Exception) { return Colors.Black; }
        }

        internal static string ToHexArgb(Color c)
        {
            return "#" + c.ToString();
        }

        /// <summary>Vuelca el tema activo a los controles del editor.</summary>
        internal void ApplyThemeToControls()
        {
            Theme t = _theme ?? new Theme();
            pageThemes.txtThemeName.Text = t.Name;
            pageThemes.SetSwatches(ParseHexColor(t.BgColor), ParseHexColor(t.FgColor),
                                   ParseHexColor(t.AccentColor), t.BgColor, t.FgColor, t.AccentColor);
            int fi = Array.IndexOf(ThemeFonts, t.FontFace);
            pageThemes.cmbFont.SelectedIndex = fi >= 0 ? fi : 0;
            pageThemes.numSize.Value = Math.Max(12, Math.Min(140, t.FontSize));
            pageThemes.chkBold.IsChecked = t.Bold;
            pageThemes.chkUpper.IsChecked = t.Uppercase;
            pageThemes.numLine.Value = Math.Max(0.8, Math.Min(2.0, t.LineSpacing));
            pageThemes.numOutline.Value = Math.Max(0, Math.Min(10, t.OutlineWidth));
            pageThemes.numShadow.Value = Math.Max(0, Math.Min(255, t.ShadowAlpha));
            pageThemes.txtImagePath.Text = t.ImagePath;
            pageThemes.cmbImageMode.SelectedIndex = t.ImageMode == 0 ? 0 : 1;
            pageThemes.InvalidatePreview();
        }

        /// <summary>Tema desde los controles del editor.</summary>
        internal Theme BuildThemeFromControls()
        {
            Theme t = new Theme();
            t.Name = (pageThemes.txtThemeName.Text ?? string.Empty).Trim().Length == 0
                ? "Predeterminado" : pageThemes.txtThemeName.Text.Trim();
            t.BgColor = pageThemes.HexBg();
            t.FgColor = pageThemes.HexFg();
            t.AccentColor = pageThemes.HexAccent();
            t.FontFace = pageThemes.cmbFont.SelectedItem != null
                ? Convert.ToString(pageThemes.cmbFont.SelectedItem, CultureInfo.InvariantCulture)
                : "Segoe UI";
            t.FontSize = pageThemes.numSize.IntValue;
            t.Bold = pageThemes.chkBold.IsChecked == true;
            t.Uppercase = pageThemes.chkUpper.IsChecked == true;
            t.LineSpacing = pageThemes.numLine.Value;
            t.OutlineWidth = pageThemes.numOutline.Value;
            t.ShadowAlpha = pageThemes.numShadow.IntValue;
            t.ImagePath = pageThemes.txtImagePath.Text.Trim();
            t.ImageMode = pageThemes.cmbImageMode.SelectedIndex == 0 ? 0 : 1;
            return t;
        }

        /// <summary>Restaura el editor al tema predeterminado.</summary>
        internal void ResetThemeEditor()
        {
            _theme = new Theme();
            ApplyThemeToControls();
            Status("Tema restaurado al predeterminado (usa «Guardar en ajustes» para conservarlo).");
        }

        /// <summary>Guarda el tema editado en settings.json (sin tocar el escenario).</summary>
        internal void SaveThemeToSettings()
        {
            _theme = BuildThemeFromControls();
            SaveSettings();
            Status("Tema guardado en data/settings.json.");
        }

        /// <summary>Reconstruye el escenario activo con el tema editado.</summary>
        internal void ApplyThemeToStage()
        {
            if (!RequireEngine()) return;
            _theme = BuildThemeFromControls();
            _appliedThemeName = _theme.Name;
            SaveSettings();
            RefreshThemeLibrary(_theme.Name);
            if (_lastScenarioItems == null || _lastScenarioItems.Count == 0)
            {
                Status("Tema listo. Carga una canción o pasaje para verlo con el nuevo tema.");
                return;
            }
            LoadScenarioFromItems(_lastScenarioItems, _lastScenarioName);
            Status("Tema «" + _theme.Name + "» aplicado al escenario activo.");
        }

        /// <summary>Aplica un tema por nombre desde data\themes (acción set_theme).</summary>
        internal void ApplyThemeByName(string name)
        {
            try
            {
                string dir = Path.Combine(_settings.DataDir, "themes");
                string file = Path.Combine(dir, SafeThemeFileName(name) + ".json");
                if (!File.Exists(file))
                {
                    Status("Tema no encontrado: " + name);
                    return;
                }
                Dictionary<string, object> o = MiniJson.Parse(
                    File.ReadAllText(file, new UTF8Encoding(false)));
                _theme = Theme.FromDict(o);
                if (_engine != null) _engine.SetTheme(MiniJson.Serialize(_theme.ToDict()));
                _settings.ThemeJson = MiniJson.Serialize(_theme.ToDict());
                _settings.Theme = _theme.Name;
                SaveSettings();
                ApplyThemeToControls();
                Status("Tema aplicado: " + _theme.Name);
            }
            catch (Exception ex)
            {
                Status("No se pudo aplicar el tema: " + ex.Message);
            }
        }

        /// <summary>Diálogo de color nativo (WinForms ColorDialog desde WPF).</summary>
        internal void PickThemeColor(string title, bool isBg, bool isFg, bool isAccent)
        {
            using (System.Windows.Forms.ColorDialog dlg = new System.Windows.Forms.ColorDialog())
            {
                dlg.FullOpen = true;
                Color cur = isBg ? pageThemes.ColorBg()
                          : isFg ? pageThemes.ColorFg()
                          : pageThemes.ColorAccent();
                dlg.Color = System.Drawing.Color.FromArgb(cur.A, cur.R, cur.G, cur.B);
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                Color c = Color.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
                pageThemes.SetSwatches(
                    isBg ? c : pageThemes.ColorBg(),
                    isFg ? c : pageThemes.ColorFg(),
                    isAccent ? c : pageThemes.ColorAccent(),
                    isBg ? ToHexArgb(c) : pageThemes.HexBg(),
                    isFg ? ToHexArgb(c) : pageThemes.HexFg(),
                    isAccent ? ToHexArgb(c) : pageThemes.HexAccent());
                pageThemes.InvalidatePreview();
            }
        }
    }
}
