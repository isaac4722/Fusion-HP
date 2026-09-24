// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — Pages/SettingsPage.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
using System;
using System.Windows;
using System.Windows.Controls;

namespace lumina.wpf
{
    public partial class SettingsPage : UserControl
    {
        internal MainWindow Shell;

        public SettingsPage()
        {
            InitializeComponent();
            try
            {
                lblAboutUi.Text = "WPF · .NET Framework " + Environment.Version + " (" +
                    (IntPtr.Size == 4 ? "32" : "64") + " bits)";
            }
            catch (Exception) { }
        }

        public void Wire(MainWindow shell) { Shell = shell; }

        // v7.1.0 «OPERADOR» (feedback #5): el «modo API» local se ELIMINÓ de
        // Ajustes (no funcionaba y estaba orientado a OBS/control externo).
        // El control remoto vive en Integraciones (mando móvil por LAN).

        private void OnBrowseDb(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.BrowseDbPath();
        }

        private void OnOpenDb(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.OpenDatabase();
        }

        private void OnSaveSettings(object sender, RoutedEventArgs e)
        {
            if (Shell != null)
            {
                Shell.SaveSettings();
                Shell.Status("Ajustes guardados.");
            }
        }

        private void OnBrowseBackup(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.BrowseBackupFolder();
        }

        private void OnBackupNow(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.RunBackupNow();
        }
    }
}
