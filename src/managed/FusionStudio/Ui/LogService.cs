// ============================================================================
//  Fusion-HP · Ui/LogService.cs — registro estructurado de la capa C# (v4.1.0)
//  Motor: NLog 4.7.15 (BSD-3, net35 — [DEPENDENCIAS.md]). Formato SPEC §11.1.5
//  alineado con el núcleo; rotación diaria con retención de 14 días.
//
//  REGLA DURA [SPEC §11.1.5]: NUNCA se registran letras, versículos, búsquedas
//  ni contenido proyectable. Solo categorías, rutas, contadores, decisiones y
//  excepciones (tipo + mensaje + pila). Configuración 100 % por código: el
//  perfil B no lee archivos de configuración extra.
// ============================================================================
using System;
using System.IO;
using System.Text;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Fusion.Studio.Ui
{
    public static class LogService
    {
        private static Logger _log;
        private static bool _init;

        /// <summary>Configura NLog con los datos del producto (AppSettings).</summary>
        public static void Init()
        {
            if (_init) return;
            try
            {
                Init(Path.Combine(Fusion.Shared.AppSettings.Load().LogsPath));
            }
            catch
            {
                _init = true; // sin logging no se tumba la app
            }
        }

        /// <summary>Configura NLog sobre una carpeta concreta (pruebas/diagnóstico).</summary>
        public static void Init(string logsDir)
        {
            if (_init) return;
            _init = true;
            try
            {
                Directory.CreateDirectory(logsDir);
                var config = new LoggingConfiguration();

                // errores: continuidad del archivo histórico studio-errors.log
                var errTarget = new FileTarget("errors")
                {
                    FileName = Path.Combine(logsDir, "studio-errors.log"),
                    Layout = "${longdate} | ${level:uppercase=true:padding=-5} | ${message}",
                    Encoding = Encoding.UTF8,
                    ArchiveEvery = FileArchivePeriod.Day,
                    MaxArchiveFiles = 14,
                    KeepFileOpen = false
                };
                // operación diaria (info/warn), un archivo por día
                var opTarget = new FileTarget("operacion")
                {
                    FileName = Path.Combine(logsDir, "studio-${shortdate}.log"),
                    Layout = "${longdate} | ${level:uppercase=true:padding=-5} | ${message}",
                    Encoding = Encoding.UTF8,
                    MaxArchiveFiles = 14,
                    KeepFileOpen = false
                };
                config.AddTarget(errTarget);
                config.AddTarget(opTarget);
                config.AddRule(LogLevel.Error, LogLevel.Fatal, errTarget, "*");
                config.AddRule(LogLevel.Debug, LogLevel.Warn, opTarget, "*");
                LogManager.Configuration = config;
                _log = LogManager.GetCurrentClassLogger();
            }
            catch { }
        }

        public static void Info(string module, string msg) { Write(LogLevel.Info, module, msg); }
        public static void Warn(string module, string msg) { Write(LogLevel.Warn, module, msg); }
        public static void Error(string module, string msg) { Write(LogLevel.Error, module, msg); }

        public static void Error(string module, Exception e)
        {
            Write(LogLevel.Error, module,
                e == null ? "excepción nula" : e.GetType().Name + ": " + e.Message +
                Environment.NewLine + (e.StackTrace ?? "sin pila"));
        }

        private static void Write(LogLevel lv, string module, string msg)
        {
            try
            {
                if (_log == null) return;
                _log.Log(lv, (module ?? "cs") + " | " + Truncar(msg));
            }
            catch { }
        }

        private static string Truncar(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            return s.Length <= 2000 ? s : s.Substring(0, 2000) + "…(truncado)";
        }
    }
}
