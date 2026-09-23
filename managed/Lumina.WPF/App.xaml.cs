// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — managed/Lumina.WPF/App.xaml.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  App.xaml.cs : punto de entrada WPF con blindaje anti-crash y los TRES
//  gates de humo del paquete portable (contrato heredado de la era WinForms):
//
//    --selfcheck : arranque SIN ventanas que valida el proceso completo
//                  (núcleo + BD temporal + parsers + exportadores) → 0/2.
//    --uicheck   : construcción headless de la ventana principal completa
//                  (XAML incluido: un recurso roto NO puede publicarse) → 0/3.
//    --flowcheck : flujos reales de usuario (modo limitado + flujo feliz
//                  Biblia→Escenario) sin excepciones → 0/4.
//
//  Contrato: la aplicación SIEMPRE abre. Fallos → diálogo claro en español
//  con la causa y la ruta del log (data\logs\lumina-*.log).
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using lumina.bridge;
using lumina.core;

namespace lumina.wpf
{
    public partial class App : Application
    {
        private const string AppName = "LuminaPresentation Suite";
        private const string AppVersion = "5.4.0";

        private static string _logPath;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ---- 0) Modos de verificación automática (CI / soporte) ----
            bool selfcheck = false, uicheck = false, flowcheck = false;
            foreach (string a in e.Args)
            {
                if (string.Equals(a, "--selfcheck", StringComparison.OrdinalIgnoreCase)) selfcheck = true;
                else if (string.Equals(a, "--uicheck", StringComparison.OrdinalIgnoreCase)) uicheck = true;
                else if (string.Equals(a, "--flowcheck", StringComparison.OrdinalIgnoreCase)) flowcheck = true;
            }
            if (selfcheck) { Environment.Exit(RunSelfCheck()); }
            if (uicheck) { RunUiCheck(); return; }        // sale por sí mismo (0/3)
            if (flowcheck) { RunFlowCheck(); return; }    // sale por sí mismo (0/4)

            // ---- 1) Blindaje global ANTES de construir la ventana ----
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            // Las ventanas auxiliares heredadas (video/escenario/director/zócalo)
            // son WinForms: visual styles ON una sola vez por proceso.
            try { System.Windows.Forms.Application.EnableVisualStyles(); }
            catch (Exception) { }

            WriteSessionLog();

            try
            {
                MainWindow = new MainWindow();
                MainWindow.Show();
            }
            catch (Exception ex)
            {
                // Fallo al CONSTRUIR la ventana: el peor caso.
                AppendLog("FATAL en OnStartup (construcción de la ventana): " + ex);
                ShowFatalDialog("No se pudo iniciar " + AppName, ex);
                Shutdown(1);
            }
        }

        /* --------------------------------------------------------- selfcheck */

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);
        private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

        private static void AttachParentConsole()
        {
            try
            {
                if (AttachConsole(ATTACH_PARENT_PROCESS))
                {
                    StreamWriter so = new StreamWriter(Console.OpenStandardOutput());
                    so.AutoFlush = true;
                    Console.SetOut(so);
                    StreamWriter se = new StreamWriter(Console.OpenStandardError());
                    se.AutoFlush = true;
                    Console.SetError(se);
                }
            }
            catch (Exception) { }
        }

        private static int RunSelfCheck()
        {
            AttachParentConsole();
            WriteSessionLog();
            int failures = 0;

            // 1) Núcleo nativo.
            try
            {
                string v = LuminaEngine.Version();
                Console.WriteLine("selfcheck: núcleo " + v);
                LogLine("selfcheck: núcleo " + v);
            }
            catch (Exception ex)
            {
                Console.WriteLine("selfcheck FALLO núcleo: " + ex.Message);
                LogLine("selfcheck FALLO núcleo: " + ex);
                failures++;
            }

            // 2) Motor headless + ciclo de vida.
            LuminaEngine engine = null;
            try
            {
                engine = LuminaEngine.Create(true, null, null);
                string songJson = "{\"title\":\"Selfcheck\",\"blocks\":[{\"label\":\"V\","
                    + "\"lines\":[\"línea uno\",\"línea dos\"]}]}";
                string parsed = LuminaEngine.SongParse(songJson);
                if (parsed.IndexOf("\"ok\":1", StringComparison.Ordinal) < 0 &&
                    parsed.IndexOf("\"ok\": 1", StringComparison.Ordinal) < 0)
                    throw new FormatException("SongParse no devolvió ok=1: " + parsed);
                Console.WriteLine("selfcheck: SongParse OK");

                string chords = LuminaEngine.ChordsTranspose("Do Sol", 2, true);
                if (chords.IndexOf("Re", StringComparison.Ordinal) < 0)
                    throw new FormatException("ChordsTranspose inesperado: " + chords);
                Console.WriteLine("selfcheck: ChordsTranspose OK");

                string refr = LuminaEngine.BibleRefResolve("jn 3:16");
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
                int st2 = engine.DbOpen(dbPath);
                if (st2 != 0) throw new InvalidOperationException("DbOpen=" + st2);
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

        /* ------------------------------------------------------------- uicheck */

        /// <summary>
        /// Construcción headless de la ventana principal WPF COMPLETA (XAML
        /// incluido: recursivamente carga Tokens/Icons/Controls y las 9 páginas).
        /// Un recurso roto, un binding de StaticResource inexistente o un crash
        /// de constructor NO puede llegar al paquete. Sale 0/3.
        /// </summary>
        private void RunUiCheck()
        {
            AttachParentConsole();
            WriteSessionLog();
            int failures = 0;
            try
            {
                // Chip en sus dos «variantes» (la primitiva de riesgo de v5.1.1,
                /// ahora en WPF: si el template no carga, salta aquí).
                Chip probe = new Chip();
                probe.Text = "uicheck";
                probe.ApplyTemplate();
                Console.WriteLine("uicheck: Chip OK (plantilla aplicada sin excepción)");
                LogLine("uicheck: Chip OK");

                // Ventana completa, sin mostrar: ejercita cabecera/chips, sidebar,
                // barra de estado, las 9 páginas, motor e integraciones.
                MainWindow form = new MainWindow();
                if (form.Content == null)
                    throw new InvalidOperationException("MainWindow construida sin contenido raíz");
                Console.WriteLine("uicheck: MainWindow construida (árbol WPF completo) OK");
                LogLine("uicheck: MainWindow construida OK");
                // Liberar el motor que el constructor haya creado.
                try { form.ForceDisposeForCheck(); } catch (Exception) { }
                Console.WriteLine("uicheck: MainWindow liberada OK");
                LogLine("uicheck: MainWindow liberada OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine("uicheck FALLO: " + ex);
                LogLine("uicheck FALLO: " + ex);
                failures++;
            }

            Console.WriteLine(failures == 0
                ? "UICHECK PASS (" + AppVersion + ")"
                : "UICHECK FAIL (" + failures + " fallo/s)");
            LogLine(failures == 0 ? "UICHECK PASS" : "UICHECK FAIL(" + failures + ")");
            // Environment.Exit (no Shutdown): corta CUALQUIER hilo de integración
            // iniciado por el constructor y evita el teardown de WPF tras una
            // excepción en la construcción (lección WinForms v5.1.1).
            Environment.Exit(failures == 0 ? 0 : 3);
        }

        /* ---------------------------------------------------------- flowcheck */

        /// <summary>
        /// Flujos de usuario headless (mismo contrato que la variante WinForms):
        ///  (1) modo limitado simulado (núcleo null): «Cargar al escenario»
        ///      avisa, JAMÁS lanza;
        ///  (2) flujo feliz: BD temporal → pasaje Jn 3:16 → slides reales.
        /// Sale 0/4.
        /// </summary>
        private void RunFlowCheck()
        {
            AttachParentConsole();
            WriteSessionLog();
            int failures = 0;
            string tempDb = null;
            try
            {
                MainWindow form = new MainWindow();
                System.Reflection.FieldInfo fEngine = FieldOf(typeof(MainWindow), "_engine");
                System.Reflection.FieldInfo fDbOpen = FieldOf(typeof(MainWindow), "_dbOpen");
                System.Reflection.FieldInfo fSlides = FieldOf(typeof(MainWindow), "_slides");
                System.Reflection.FieldInfo fTxtRef = FieldOf(typeof(MainWindow), "_txtRef");
                System.Reflection.MethodInfo mLoad = MethodOf(typeof(MainWindow),
                    "LoadScriptureToStage");

                // ---------- (1) MODO LIMITADO: núcleo caído, JAMÁS lanzar ----------
                object savedEngine = fEngine.GetValue(form);
                fEngine.SetValue(form, null);
                try
                {
                    mLoad.Invoke(form, null);
                    Console.WriteLine("flowcheck: Biblia→Escenario con núcleo caído OK (aviso, sin excepción)");
                    LogLine("flowcheck: modo limitado OK (sin NullReferenceException)");
                }
                catch (System.Reflection.TargetInvocationException tie)
                {
                    Console.WriteLine("flowcheck FALLO (núcleo caído): " + tie.InnerException);
                    LogLine("flowcheck FALLO (núcleo caído): " + tie.InnerException);
                    failures++;
                }
                finally
                {
                    fEngine.SetValue(form, savedEngine);
                }

                // ---------- (2) FLUJO FELIZ: BD temporal → pasaje → slides ----------
                try
                {
                    if (savedEngine == null)
                        throw new InvalidOperationException(
                            "el motor no arrancó en este entorno (no se puede probar el flujo feliz)");
                    LuminaEngine engine = (LuminaEngine)savedEngine;

                    tempDb = Path.Combine(Path.GetTempPath(),
                        "lumina-flowcheck-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".db");
                    int st = engine.DbOpen(tempDb);
                    if (st != 0) throw new InvalidOperationException("DbOpen=" + st);
                    string ins = engine.DbExec(LuminaStorage.BuildExecJson(
                        "INSERT INTO bible(version,book,chapter,verse,text) VALUES (?,?,?,?,?)",
                        new object[] { "TEST", (long)43, (long)3, (long)16,
                                       "Porque de tal manera amó Dios al mundo…" }));
                    if (ins == null) throw new InvalidOperationException("INSERT sin respuesta");

                    fDbOpen.SetValue(form, true);
                    System.Windows.Controls.TextBox txtRef =
                        (System.Windows.Controls.TextBox)fTxtRef.GetValue(form);
                    txtRef.Text = "Jn 3:16";
                    mLoad.Invoke(form, null);

                    System.Collections.Generic.List<SlideView> slides =
                        (System.Collections.Generic.List<SlideView>)fSlides.GetValue(form);
                    if (slides == null || slides.Count == 0)
                        throw new InvalidOperationException(
                            "el pasaje no produjo slides (¿el núcleo no resolvió el versículo?)");
                    Console.WriteLine("flowcheck: flujo feliz Biblia→Escenario OK ("
                                      + slides.Count + " slide/s)");
                    LogLine("flowcheck: flujo feliz OK (" + slides.Count + " slides)");
                }
                catch (System.Reflection.TargetInvocationException tie)
                {
                    Console.WriteLine("flowcheck FALLO (flujo feliz): " + tie.InnerException);
                    LogLine("flowcheck FALLO (flujo feliz): " + tie.InnerException);
                    failures++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("flowcheck FALLO (flujo feliz): " + ex.Message);
                    LogLine("flowcheck FALLO (flujo feliz): " + ex);
                    failures++;
                }

                try { form.ForceDisposeForCheck(); } catch (Exception) { }
                Console.WriteLine("flowcheck: MainWindow liberada OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine("flowcheck FALLO (construcción): " + ex);
                LogLine("flowcheck FALLO (construcción): " + ex);
                failures++;
            }
            finally
            {
                if (tempDb != null) { try { File.Delete(tempDb); } catch (Exception) { } }
            }

            Console.WriteLine(failures == 0
                ? "FLOWCHECK PASS (" + AppVersion + ")"
                : "FLOWCHECK FAIL (" + failures + " fallo/s)");
            LogLine(failures == 0 ? "FLOWCHECK PASS" : "FLOWCHECK FAIL(" + failures + ")");
            Environment.Exit(failures == 0 ? 0 : 4);
        }

        private static System.Reflection.FieldInfo FieldOf(System.Type t, string name)
        {
            return t.GetField(name, System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);
        }

        private static System.Reflection.MethodInfo MethodOf(System.Type t, string name)
        {
            return t.GetMethod(name, System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);
        }

        /* ----------------------------------------------------------- handlers */

        private void OnDispatcherUnhandledException(object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            AppendLog("Excepción en el hilo de UI (WPF): " + e.Exception);
            ShowFatalDialog("Error inesperado en la interfaz", e.Exception);
            e.Handled = true;   // el proceso SOBREVIVE: el usuario no pierde la ventana
        }

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
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception) { }
        }

        /* ----------------------------------------------------------- logging */

        public static void LogLine(string message) { AppendLog(message); }

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
                sb.AppendLine("=== " + AppName + " " + AppVersion + " (WPF) — inicio ===");
                sb.AppendLine("Fecha        : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                try { sb.AppendLine("SO           : " + Environment.OSVersion.VersionString); }
                catch (Exception) { }
                sb.AppendLine("Proceso      : " + (IntPtr.Size == 4 ? "32 bits" : "64 bits")
                              + "  (CLR " + Environment.Version + ")");
                try { sb.AppendLine(".NET 4.8+    : " + IsNet48Plus()); }
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
            catch (Exception) { }
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
