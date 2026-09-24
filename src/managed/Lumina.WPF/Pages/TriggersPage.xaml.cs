// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — Pages/TriggersPage.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace lumina.wpf
{
    public partial class TriggersPage : UserControl
    {
        internal MainWindow Shell;

        public TriggersPage()
        {
            InitializeComponent();
        }

        public void Wire(MainWindow shell) { Shell = shell; }

        internal void FillTriggers(List<TriggerRowVm> vms)
        {
            lstTriggers.ItemsSource = vms;
        }

        internal TriggerRowVm SelectedTrigger()
        {
            return lstTriggers.SelectedItem as TriggerRowVm;
        }

        private void OnTriggerAdd(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.TriggerAdd();
        }

        private void OnTriggerEdit(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.TriggerEdit();
        }

        private void OnTriggerDoubleClicked(object sender, MouseButtonEventArgs e)
        {
            if (Shell != null) Shell.TriggerEdit();
        }

        private void OnTriggerToggle(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.TriggerToggleEnable();
        }

        private void OnTriggerDelete(object sender, RoutedEventArgs e)
        {
            if (Shell != null) Shell.TriggerDelete();
        }
    }
}
