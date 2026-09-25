// ============================================================================
//  Fusion-HP · tests/managed/TestRunner.cs — arnés de pruebas auto-hospedado
//  (compatible net35→net48, sin vstest/NUnit: cero dependencias [SPEC §3.5]).
//  Cada prueba es un método Test* que usa Check(). Salida: nombre + veredicto
//  y código de salida 0 solo si TODO pasa.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Fusion.Tests
{
    static class TestRunner
    {
        [STAThread]
        static int Main(string[] args)
        {
            string filter = args.Length > 0 ? args[0] : null;
            var failures = new List<string>();
            int passed = 0, failed = 0;
            var types = Assembly.GetExecutingAssembly().GetTypes();
            var methods = new List<MethodInfo>();
            foreach (var t in types)
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    if (m.Name.StartsWith("Test", StringComparison.Ordinal) && m.GetParameters().Length == 0)
                        methods.Add(m);
            methods.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

            foreach (var m in methods)
            {
                if (filter != null && m.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                Console.Write(m.Name.PadRight(52));
                try
                {
                    m.Invoke(null, null);
                    Console.WriteLine("[OK]");
                    passed++;
                }
                catch (TargetInvocationException tie)
                {
                    Console.WriteLine("[FALLO] " + (tie.InnerException != null ? tie.InnerException.Message : tie.Message));
                    failed++;
                    failures.Add(m.Name + ": " + (tie.InnerException != null ? tie.InnerException.ToString() : tie.ToString()));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[FALLO] " + ex.Message);
                    failed++;
                    failures.Add(m.Name + ": " + ex);
                }
            }
            Console.WriteLine();
            Console.WriteLine("Resultado: " + passed + " OK · " + failed + " FALLO");
            if (failed > 0)
            {
                Console.WriteLine();
                foreach (var f in failures) Console.WriteLine("--- " + f);
            }
            return failed == 0 ? 0 : 1;
        }

        public static void Check(bool cond, string msg)
        {
            if (!cond) throw new Exception(msg);
        }
        public static void CheckEq<T>(T a, T b, string msg)
        {
            if (!EqualityComparer<T>.Default.Equals(a, b))
                throw new Exception(msg + " (esperado=" + b + " obtenido=" + a + ")");
        }
        public static string FixturePath(string name)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", name);
        }
        public static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "fusion-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }
    }
}
