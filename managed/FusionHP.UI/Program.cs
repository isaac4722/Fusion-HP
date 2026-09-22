// ============================================================================
//  Fusion-HP / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Program.cs : punto de entrada WinForms. STA obligatorio para GDI+/
//  System.Windows.Forms y para el callback de eventos del motor cuando la UI
//  interactúa con controles.
// ============================================================================
using System;
using System.Windows.Forms;

namespace fusion.ui
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (MainForm form = new MainForm())
            {
                Application.Run(form);
            }
        }
    }
}
