// ============================================================================
//  Fusion-HP · Program.cs — entrada de la capa administrada.
//  Manejo de excepciones no controladas [SPEC §11.2.1], DPI y arranque limpio.
//  v4.1.1: resolución de último recurso para terceros (PdfSharp dual y la
//  cadena System.* de PDFsharp 6.1.1) — sin redirecciones de enlace.
// ============================================================================
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Fusion.Studio.Ui;

namespace Fusion.Studio
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // v4.1.1: el resolvedor se instala PRIMERO, antes de que el primer
            // JIT cargue NLog/PdfSharp/SQLite. Regla de oro #1: sin app.config.
            AppDomain.CurrentDomain.AssemblyResolve += OnThirdPartyResolve;

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

        /// <summary>
        /// v4.1.1 — resolución de último recurso de terceros (solo si el enlace
        /// estándar FALLA; nunca se invoca cuando la DLL correcta está junto al
        /// exe). Cubre dos casos reales:
        ///   1) Lite (net35) pide PdfSharp 1.50 y junto al exe está el 6.1.1
        ///      (netstandard2.0, que CLR2 ni siquiera puede leer): la copia
        ///      net35 viaja en la subcarpeta PdfSharp-1.50\.
        ///   2) La cadena de PDFsharp 6.1.1 (System.Memory → CompilerServices.
        ///      Unsafe 4.0.5) pide versiones menores que las vendorizadas:
        ///      se sirve el archivo local sin importar la versión pedida.
        /// Debe compilar en net35: solo mscorlib/System, sin LINQ.
        /// </summary>
        static Assembly OnThirdPartyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                string simple = new AssemblyName(args.Name).Name;
                if (string.IsNullOrEmpty(simple)) return null;

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string candidate;
                if (simple == "PdfSharp" && System.Environment.Version.Major < 4)
                {
                    // Lite (CLR2): copia net35 de PDFsharp 1.50 en su subcarpeta
                    // (net35 no tiene Path.Combine de 3 argumentos)
                    candidate = Path.Combine(Path.Combine(baseDir, "PdfSharp-1.50"), "PdfSharp.dll");
                }
                else
                {
                    candidate = Path.Combine(baseDir, simple + ".dll");
                }
                if (!File.Exists(candidate)) return null;
                return Assembly.LoadFrom(candidate);
            }
            catch { return null; } // sin el resolvedor no se tumba la app
        }
    }
}
