// ============================================================================
//  LuminaPresentation Suite v4.0.0 — managed/Lumina.UI/Program.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Program.cs : punto de entrada WinForms con blindaje anti-crash.
//
//  Contrato v4.0.0 «LUMINA»: la aplicación SIEMPRE debe abrir. Si algo falla
//  (núcleo ausente, permisos, datos corruptos…), el usuario ve un diálogo
//  claro en español con la causa y la ruta del log — NUNCA el críptico
//  «dejó de funcionar» de Windows.
//
//  Capas de defensa:
//   1) Application.ThreadException   → excepciones del hilo de UI (WinForms
//      puede continuar tras el diálogo: el usuario no pierde la ventana).
//   2) AppDomain.UnhandledException  → excepciones en hilos de fondo (motor,
//      API, importadores): se registra y se avisa.
//   3) try/catch en Main             → fallos DURANTE la construcción de la
//      ventana principal (el peor caso: nada que mostrar).
//
//  Todo queda registrado en  data/logs/lumina-AAAA-MM-DD_HHMMSS.log  con el
//  entorno completo (SO, bits, .NET, presencia de LuminaCore.dll, rutas y
//  permisos) para soporte remoto sin herramientas del usuario. El logging
//  está rodeado de try/catch: jamás provoca un crash por sí mismo.
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using lumina.core;

namespace lumina.ui
{
    internal static class Program
    {
        private const string AppName = "LuminaPresentation Suite";
        private const string AppVersion = "5.0.0";

        /// <summary>Ruta del log de la sesión en curso (null si aún no hay).</summary>
        private static string _logPath;

        [STAThread]
        private static void Main()
        {
            // ---- 1) Blindaje global ANTES de tocar cualquier cosa WinForms ----
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            Application.ThreadException += OnThreadException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            WriteSessionLog();

            try
            {
                using (MainForm form = new MainForm())
                {
                    Application.Run(form);
                }
            }
            catch (Exception ex)
            {
                // Fallo al CONSTRUIR la ventana: el peor caso. Registrar y
                // mostrar diagnóstico; salir limpio (sin WER de fondo).
                AppendLog("FATAL en Main (construcción de la ventana): " + ex);
                ShowFatalDialog("No se pudo iniciar " + AppName, ex);
            }
        }

        /* ----------------------------------------------------------- handlers */

        // Hilo de UI: WinForms invoca esto por cada excepción no manejada en
        // un manejador de eventos. Con CatchException el proceso SOBREVIVE:
        // mostramos el aviso y el usuario puede seguir trabajando.
        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            AppendLog("Excepción en el hilo de UI: " + e.Exception);
            ShowFatalDialog("Error inesperado en la interfaz", e.Exception);
        }

        // Hilos de fondo (motor de eventos, API HTTP, importadores). Aquí el
        // CLR va a terminar el proceso; solo nos queda dejar constancia y
        // avisar con un mensaje comprensible.
        private static void OnDomainUnhandledException(object sender,
                                                       UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            AppendLog("FATAL en hilo de fondo (terminating=" + e.IsTerminating + "): "
                      + (ex != null ? ex.ToString() : "(objeto no-Exception)"));
            ShowFatalDialog("Error crítico en un servicio interno",
                            ex ?? new Exception("(excepción no administrada)"));
        }

        /* ------------------------------------------------------------ diálogo */

        private static void ShowFatalDialog(string headline, Exception ex)
        {
            try
            {
                string details = ex != null
                    ? ex.GetType().Name + ": " + ex.Message
                    : "(sin detalle)";

                StringBuilder sb = new StringBuilder();
                sb.AppendLine(headline);
                sb.AppendLine();
                sb.AppendLine("  " + details);
                sb.AppendLine();
                if (!string.IsNullOrEmpty(_logPath) && File.Exists(_logPath))
                {
                    sb.AppendLine("El detalle técnico completo quedó guardado en:");
                    sb.AppendLine("  " + _logPath);
                    sb.AppendLine();
                }
                sb.AppendLine("Sugerencias rápidas:");
                sb.AppendLine("  • Si falta «LuminaCore.dll», extraiga el ZIP completo y " +
                              "ejecute LuminaLauncher.exe desde la carpeta extraída.");
                sb.AppendLine("  • Evite carpetas de solo lectura (p. ej. «Archivos de programa»): " +
                              "mueva el paquete a una carpeta propia como «Documentos».");
                sb.AppendLine("  • Si el antivirus aisló archivos del paquete, restáurelos y " +
                              "excluya la carpeta del programa.");

                MessageBox.Show(sb.ToString(), AppName + " " + AppVersion,
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception)
            {
                // Hasta el diálogo falló (WinForms roto): nada más que hacer.
            }
        }

        /* ----------------------------------------------------------- logging */

        // Log de arranque: entorno completo. Si algo falla en una máquina del
        // usuario, este archivo responde el 90% de las preguntas de soporte.
        private static void WriteSessionLog()
        {
            try
            {
                string logDir = ResolveLogDir();
                if (logDir == null) return;
                try { Directory.CreateDirectory(logDir); } catch (Exception) { }

                _logPath = Path.Combine(logDir,
                    "lumina-" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".log");

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("=== " + AppName + " " + AppVersion + " — inicio ===");
                sb.AppendLine("Fecha        : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                try
                {
                    sb.AppendLine("SO           : " + Environment.OSVersion.VersionString);
                }
                catch (Exception) { }
                sb.AppendLine("Proceso      : " + (IntPtr.Size == 4 ? "32 bits" : "64 bits")
                              + "  (CLR " + Environment.Version + ")");
                try
                {
                    sb.AppendLine(".NET 4.8+    : " + IsNet48Plus());
                }
                catch (Exception) { }

                string baseDir = Settings.DefaultBaseDir();
                sb.AppendLine("Carpeta exe  : " + baseDir);
                sb.AppendLine("LuminaCore   : "
                    + (File.Exists(Path.Combine(baseDir, "LuminaCore.dll"))
                        ? "presente" : "*** AUSENTE ***"));
                string dataDir = Path.Combine(baseDir, "data");
                sb.AppendLine("Carpeta data : "
                    + (Directory.Exists(dataDir) ? "existe" : "no existe (se creará)")
                    + " — escribible: " + CanWrite(dataDir));

                AppendLogRaw(sb.ToString());
            }
            catch (Exception)
            {
                // Logging es best-effort: nunca rompe el arranque.
            }
        }

        private static void AppendLog(string message)
        {
            try { AppendLogRaw("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message); }
            catch (Exception) { }
        }

        private static void AppendLogRaw(string text)
        {
            if (string.IsNullOrEmpty(_logPath)) return;
            File.AppendAllText(_logPath, text + Environment.NewLine, new UTF8Encoding(false));
        }

        // Carpeta de logs:  <exe>/data/logs  (portable). Si no se puede
        // escribir ahí (carpeta protegida), %TEMP%\LuminaPresentation.
        private static string ResolveLogDir()
        {
            string primary = Path.Combine(Path.Combine(Settings.DefaultBaseDir(), "data"), "logs");
            if (CanWrite(primary) || CanWrite(CreateTemp(primary))) return primary;
            try
            {
                string fallback = Path.Combine(Path.Combine(Path.GetTempPath(), "LuminaPresentation"), "logs");
                Directory.CreateDirectory(fallback);
                return fallback;
            }
            catch (Exception) { return null; }
        }

        private static string CreateTemp(string dir)
        {
            try { Directory.CreateDirectory(dir); return dir; }
            catch (Exception) { return null; }
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return false;
                if (!Directory.Exists(dir)) return false;
                string probe = Path.Combine(dir, ".escritura-prueba");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (Exception) { return false; }
        }

        // .NET 4.8+ presente (clave Release >= 528040). Informativo para el log.
        private static bool IsNet48Plus()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine
                    .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (key == null) return false;
                    object v = key.GetValue("Release");
                    return v is int && (int)v >= 528040;
                }
            }
            catch (Exception) { return false; }
        }
    }
}
