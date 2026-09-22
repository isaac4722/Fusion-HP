// ============================================================================
//  LuminaPresentation Suite v5.1.0 «FUNDAMENTO» — managed/Lumina.UI/Program.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Program.cs : punto de entrada WinForms con blindaje anti-crash.
//
//  Contrato: la aplicación SIEMPRE debe abrir. Si algo falla (núcleo ausente,
//  permisos, datos corruptos…), el usuario ve un diálogo claro en español con
//  la causa y la ruta del log — NUNCA el críptico «dejó de funcionar».
//
//  Capas de defensa:
//   1) Application.ThreadException   → excepciones del hilo de UI (WinForms
//      puede continuar tras el diálogo: el usuario no pierde la ventana).
//   2) AppDomain.UnhandledException  → excepciones en hilos de fondo (motor,
//      API, importadores): se registra y se avisa.
//   3) try/catch en Main             → fallos DURANTE la construcción de la
//      ventana principal (el peor caso: nada que mostrar).
//
//  v5.1.0 «FUNDAMENTO»:
//   * Modo  --selfcheck : arranque SIN ventanas que valida el proceso completo
//     (carga de LuminaCore.dll + BD temporal + parser de canciones + acordes +
//     referencias bíblicas + exportadores PPTX/PDF) y sale con código 0/2.
//     Es el GATE de humo del paquete portable: la CI lo ejecuta en ambas
//     variantes (net48 y net35) ANTES de empaquetar — así un paquete con DLLs
//     gestionadas ausentes (el bug de v5.0.0) NO puede volver a publicarse.
//   * LogLine(string) público: MainForm lleva los errores del núcleo al
//     archivo de sesión (antes solo iban a Trace).
//
//  Todo queda registrado en  data/logs/lumina-AAAA-MM-DD_HHMMSS.log  con el
//  entorno completo (SO, bits, .NET, presencia de LuminaCore.dll, rutas y
//  permisos) para soporte remoto sin herramientas del usuario.
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using lumina.bridge;      // v5.1.0: LuminaStorage en el selfcheck (BD SQLite)
using lumina.core;

namespace lumina.ui
{
    internal static class Program
    {
        private const string AppName = "LuminaPresentation Suite";
        private const string AppVersion = "5.1.0";

        /// <summary>Ruta del log de la sesión en curso (null si aún no hay).</summary>
        private static string _logPath;

        [STAThread]
        private static void Main()
        {
            // ---- 0) Modo de verificación automática (CI / soporte técnico) ----
            string[] args = Environment.GetCommandLineArgs();
            bool selfcheck = false;
            foreach (string a in args)
                if (string.Equals(a, "--selfcheck", StringComparison.OrdinalIgnoreCase))
                    selfcheck = true;
            if (selfcheck)
            {
                Environment.ExitCode = RunSelfCheck();
                return;
            }

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

        /* --------------------------------------------------------- selfcheck */

        // WinExe no tiene consola: adjuntarse a la del proceso padre (la CI)
        // hace visible la salida del selfcheck en el log del runner. Si no hay
        // consola padre (doble clic), queda silencioso — el archivo de log
        // siempre queda en data\logs.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

        private static void AttachParentConsole()
        {
            try
            {
                if (AttachConsole(ATTACH_PARENT_PROCESS))
                {
                    // Redirigir los writers estándar a la consola recién adjunta.
                    StreamWriter so = new StreamWriter(Console.OpenStandardOutput());
                    so.AutoFlush = true;
                    Console.SetOut(so);
                    StreamWriter se = new StreamWriter(Console.OpenStandardError());
                    se.AutoFlush = true;
                    Console.SetError(se);
                }
            }
            catch (Exception) { /* diagnóstico best-effort */ }
        }

        /// <summary>
        /// Verificación integral SIN GUI: motor nativo, BD temporal, parsers del
        /// núcleo y exportadores PPTX/PDF. Devuelve 0 si TODO pasa; 2 si algo
        /// falla (cada paso deja constancia en consola y en el log de sesión).
        /// Requisito de la salida: NO crear ventanas ni hilos de UI.
        /// </summary>
        private static int RunSelfCheck()
        {
            AttachParentConsole();   // mejor esfuerzo: ver la salida en la CI
            WriteSessionLog();
            int failures = 0;

            // 1) Núcleo nativo (la DLL debe estar junto al exe).
            try
            {
                string v = lumina.bridge.LuminaEngine.Version();
                Console.WriteLine("selfcheck: núcleo " + v);
                LogLine("selfcheck: núcleo " + v);
            }
            catch (Exception ex)
            {
                Console.WriteLine("selfcheck FALLO núcleo: " + ex.Message);
                LogLine("selfcheck FALLO núcleo: " + ex);
                failures++;
            }

            // 2) Motor headless + ciclo de vida completo.
            lumina.bridge.LuminaEngine engine = null;
            try
            {
                engine = lumina.bridge.LuminaEngine.Create(true, null, null);
                string songJson = "{\"title\":\"Selfcheck\",\"blocks\":[{\"label\":\"V\","
                    + "\"lines\":[\"línea uno\",\"línea dos\"]}]}";
                string parsed = lumina.bridge.LuminaEngine.SongParse(songJson);
                if (parsed.IndexOf("\"ok\":1", StringComparison.Ordinal) < 0 &&
                    parsed.IndexOf("\"ok\": 1", StringComparison.Ordinal) < 0)
                    throw new FormatException("SongParse no devolvió ok=1: " + parsed);
                Console.WriteLine("selfcheck: SongParse OK");

                string chords = lumina.bridge.LuminaEngine.ChordsTranspose("Do Sol", 2, true);
                if (chords.IndexOf("Re", StringComparison.Ordinal) < 0)
                    throw new FormatException("ChordsTranspose inesperado: " + chords);
                Console.WriteLine("selfcheck: ChordsTranspose OK");

                string refr = lumina.bridge.LuminaEngine.BibleRefResolve("jn 3:16");
                if (refr.IndexOf("\"book\":43", StringComparison.Ordinal) < 0 &&
                    refr.IndexOf("\"book\": 43", StringComparison.Ordinal) < 0)
                    throw new FormatException("BibleRefResolve inesperado: " + refr);
                Console.WriteLine("selfcheck: BibleRefResolve OK");

                int st = engine.LoadScenario(ScenarioBuilder.BuildScenarioJson(
                    "selfcheck", new Theme(),
                    new ScenarioItem[] { ScenarioBuilder.FromSong(
                        ScenarioBuilder.SongFromEditor("Selfcheck", "", "línea uno\nlínea dos", false, 0)) }));
                if (st != 0) throw new InvalidOperationException("LoadScenario=" + st);
                st = engine.Next();
                if (st != 0) throw new InvalidOperationException("Next=" + st);
                Console.WriteLine("selfcheck: escenario + Next OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine("selfcheck FALLO motor: " + ex.Message);
                LogLine("selfcheck FALLO motor: " + ex);
                failures++;
            }

            // 3) BD temporal (SQLite + FTS5 del núcleo).
            try
            {
                string dbPath = Path.Combine(Path.GetTempPath(),
                    "lumina-selfcheck-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".db");
                int st = engine.DbOpen(dbPath);
                if (st != 0) throw new InvalidOperationException("DbOpen=" + st);
                string res = engine.DbExec(LuminaStorage.InsertSongRequest(
                    "Selfcheck", "", "", 0, "", "letra de prueba"));
                if (res == null) throw new InvalidOperationException("INSERT sin respuesta");
                Console.WriteLine("selfcheck: BD SQLite+FTS OK");
                try { File.Delete(dbPath); } catch (Exception) { }
            }
            catch (Exception ex)
            {
                Console.WriteLine("selfcheck FALLO BD: " + ex.Message);
                LogLine("selfcheck FALLO BD: " + ex);
                failures++;
            }

            // 4) Exportadores PPTX/PDF (módulos gestionados: Lumina.Core.dll).
            try
            {
                string tmp = Path.GetTempPath();
                SlideView sv = new SlideView();
                sv.Index = 0;
                sv.Title = "Selfcheck";
                sv.RefLabel = "Jn 3:16";
                sv.Lines.Add("Porque de tal manera amó Dios…");
                System.Collections.Generic.List<ExportSlide> slides =
                    new System.Collections.Generic.List<ExportSlide>();
                slides.Add(new ExportSlide(sv));
                string pptx = Path.Combine(tmp, "lumina-selfcheck.pptx");
                string pdf = Path.Combine(tmp, "lumina-selfcheck.pdf");
                string e1 = PptxExporter.ExportToFile(pptx, "Selfcheck", slides, new Theme());
                string e2 = PdfExporter.ExportToFile(pdf, "Selfcheck", slides, new Theme());
                if (e1 != null) throw new InvalidOperationException("PPTX: " + e1);
                if (e2 != null) throw new InvalidOperationException("PDF: " + e2);
                if (!File.Exists(pptx) || new FileInfo(pptx).Length < 2000)
                    throw new InvalidOperationException("PPTX sospechosamente pequeño");
                if (!File.Exists(pdf) || new FileInfo(pdf).Length < 500)
                    throw new InvalidOperationException("PDF sospechosamente pequeño");
                Console.WriteLine("selfcheck: exportadores PPTX/PDF OK");
                try { File.Delete(pptx); File.Delete(pdf); } catch (Exception) { }
            }
            catch (Exception ex)
            {
                // Es el fallo del bug v5.0.0 (FileNotFoundException Lumina.Core):
                // aquí lo detectamos ANTES de empaquetar/publicar.
                Console.WriteLine("selfcheck FALLO exportadores: " + ex.Message);
                LogLine("selfcheck FALLO exportadores: " + ex);
                failures++;
            }

            if (engine != null) { try { engine.Dispose(); } catch (Exception) { } }

            Console.WriteLine(failures == 0
                ? "SELFCHECK PASS (" + AppVersion + ")"
                : "SELFCHECK FAIL (" + failures + " fallo/s)");
            LogLine(failures == 0 ? "SELFCHECK PASS" : "SELFCHECK FAIL(" + failures + ")");
            return failures == 0 ? 0 : 2;
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
                sb.AppendLine("  • Extraiga el ZIP COMPLETO en una carpeta propia y ejecute");
                sb.AppendLine("    LuminaLauncher.exe desde esa carpeta (nunca desde dentro del ZIP).");
                sb.AppendLine("  • El paquete debe contener, junto a LuminaLauncher.exe, las carpetas");
                sb.AppendLine("    net48\\ y net35\\ con todos sus archivos (exe + Lumina.Core.dll +");
                sb.AppendLine("    Lumina.Bridge.dll + Lumina.Api.dll + LuminaCore.dll).");
                sb.AppendLine("  • Evite carpetas de solo lectura (p. ej., «Archivos de programa»).");
                sb.AppendLine("  • Si el antivirus aisló archivos del paquete, restáurelos y");
                sb.AppendLine("    excluya la carpeta del programa.");

                MessageBox.Show(sb.ToString(), AppName + " " + AppVersion,
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception)
            {
                // Hasta el diálogo falló (WinForms roto): nada más que hacer.
            }
        }

        /* ----------------------------------------------------------- logging */

        /// <summary>
        /// v5.1.0: línea de log pública (timestamp + texto) — la usa MainForm
        /// para llevar los errores del núcleo al archivo de sesión.
        /// Best-effort: nunca lanza.
        /// </summary>
        public static void LogLine(string message)
        {
            AppendLog(message);
        }

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
                sb.AppendLine("Carpeta base : " + baseDir);
                sb.AppendLine("Carpeta exe  : " + AppDomain.CurrentDomain.BaseDirectory);
                sb.AppendLine("LuminaCore   : "
                    + (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LuminaCore.dll"))
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

        // Carpeta de logs:  <raíz-del-paquete>/data/logs  (portable; compartida
        // por las variantes net48/net35 gracias a Settings.DefaultBaseDir()).
        // Si no se puede escribir ahí (carpeta protegida), %TEMP%\LuminaPresentation.
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
