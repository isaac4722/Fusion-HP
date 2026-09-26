// ============================================================================
//  Fusion-HP · Program.cs — entrada de la capa administrada.
//  Manejo de excepciones no controladas [SPEC §11.2.1], DPI y arranque limpio.
// ============================================================================
using System;
using System.Windows.Forms;
using Fusion.Studio.Ui;

namespace Fusion.Studio
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Excepciones no controladas: log + diálogo humano (nunca stack crudo) [SPEC §11.2]
            // v4.1.0: NLog (LogService) — mismo studio-errors.log, con niveles y rotación.
            Fusion.Studio.Ui.LogService.Init();
            Fusion.Studio.Ui.LogService.Info("cs.app", "estudio iniciado");
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Fusion.Studio.Ui.LogService.Error("cs.ui", e.Exception);
                MessageBox.Show(
                    "Ocurrió un problema inesperado y la operación fue cancelada.\n\n" +
                    "Qué puedes hacer: repite la acción. Si vuelve a fallar, usa «Ayuda → Estado del sistema » " +
                    "Verificar entorno» y comparte el diagnóstico.\n\n" +
                    "(El detalle técnico quedó registrado en el log)",
                    "Fusion HP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                var ex = e.ExceptionObject as Exception;
                Fusion.Studio.Ui.LogService.Error("cs.domain", ex ?? new Exception(Convert.ToString(e.ExceptionObject)));
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Instancia única del estudio (el núcleo lanza este proceso una sola vez)
            bool created;
            var mutex = new System.Threading.Mutex(true, "FusionHP.Studio.Instance", out created);
            if (!created)
            {
                MessageBox.Show("El estudio de Fusion HP ya está abierto.",
                    "Fusion HP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Application.Run(new MainForm());
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
    }
}
