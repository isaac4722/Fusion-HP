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
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Fusion.Shared.AppSettings st = Fusion.Shared.AppSettings.Load();
                try { System.IO.Directory.CreateDirectory(st.LogsPath); }
                catch { }
                try
                {
                    System.IO.File.AppendAllText(System.IO.Path.Combine(st.LogsPath, "studio-errors.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | cs.ui | " + e.Exception.ToString() + "\n");
                }
                catch { }
                MessageBox.Show(
                    "Ocurrió un problema inesperado y la operación fue cancelada.\n\n" +
                    "Qué puedes hacer: repite la acción. Si vuelve a fallar, usa «Ayuda → Estado del sistema » " +
                    "Verificar entorno» y comparte el diagnóstico.\n\n" +
                    "(El detalle técnico quedó registrado en el log)",
                    "Fusion HP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                try
                {
                    var st = Fusion.Shared.AppSettings.Load();
                    System.IO.Directory.CreateDirectory(st.LogsPath);
                    System.IO.File.AppendAllText(System.IO.Path.Combine(st.LogsPath, "studio-errors.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | cs.domain | " + e.ExceptionObject + "\n");
                }
                catch { }
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
