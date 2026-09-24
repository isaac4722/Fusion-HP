// ============================================================================
//  LuminaPresentation Suite — Pages/IntegrationsPage.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
using System.Windows;
using System.Windows.Controls;

namespace lumina.wpf
{
    public partial class IntegrationsPage : UserControl
    {
        internal MainWindow Shell;

        public IntegrationsPage()
        {
            InitializeComponent();
        }

        public void Wire(MainWindow shell) { Shell = shell; }

        private void OnMidiAutoApply(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.MidiAutoApply();
        }

        private void OnMidiRefresh(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.MidiRefreshDevices();
        }

        private void OnRemoteApply(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.RemoteApplyClick();
        }

        private void OnJsAutoApply(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.JsAutoApply();
        }

        private void OnJsReload(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.JsReloadClick();
        }
    }
}
