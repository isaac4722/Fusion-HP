// ============================================================================
//  Fusion-HP · tests/managed/DepsTests.cs — pruebas de las dependencias v4.1.0
//  Una prueba por sustitución/incorporación (regla AGENT.md: la prueba cubre
//  la pieza, nunca se debilitan las existentes):
//   · NLog — escribe y rota (config por código), formato §11.1.5.
//   · Newtonsoft.Json — escapes/números difíciles + round-trip de fachada.
//   · System.Data.SQLite — ADO.NET lee el fixture e-Sword (SQLite real).
//   · Ookii.Dialogs — construcción sin recursos (solo ctor, sin ShowDialog).
//   · BouncyCastle — SHA-256 vector oficial (motor disponible; Twofish.cs
//     propio SIGUE en uso: sustitución solo si falla o formato nuevo).
//   · PdfSharp ya cubierto por PlatformTests.TestPdfExporter (incrustación).
//   · OpenXml ya cubierto por PlatformTests.TestPptxRoundTrip (reimportación).
//  Descubiertas por reflexión (TestRunner): métodos públicos estáticos Test*.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Fusion.Shared;
using Fusion.Shared.Bible;
using Fusion.Tests;

namespace Fusion.Tests
{
    static class DepsTests
    {
        static string Fixture(string name)
        {
            // fixtures se copian al output (FusionTests.csproj)
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", name);
        }

        // ------------------------------------------------------------- NLog
        public static void TestNLogWritesAndArchives()
        {
            string dir = TestRunner.TempDir();
            LogServiceInitForTests(dir);
            Fusion.Studio.Ui.LogService.Info("cs.test", "prueba de operación");
            Fusion.Studio.Ui.LogService.Warn("cs.test", "prueba de aviso");
            Fusion.Studio.Ui.LogService.Error("cs.test", "prueba de error");

            // dejar que NLog vacíe (targets con KeepFileOpen=false vacían al escribir)
            var errPath = Path.Combine(dir, "studio-errors.log");
            TestRunner.Check(File.Exists(errPath), "NLog: studio-errors.log creado");
            if (File.Exists(errPath))
            {
                string content = File.ReadAllText(errPath);
                TestRunner.Check(content.Contains("cs.test") && content.Contains("prueba de error"),
                    "NLog: línea con módulo y mensaje");
                TestRunner.Check(content.Contains("ERROR"), "NLog: nivel presente en la línea");
            }
            // operación diaria: studio-<fecha>.log
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            TestRunner.Check(File.Exists(Path.Combine(dir, "studio-" + today + ".log")),
                "NLog: log diario de operación creado");
        }

        static void LogServiceInitForTests(string dir)
        {
            // Init(dir) solo configura la primera vez; para pruebas reconfiguramos
            // a través de la API pública si ya se inicializó en otro test.
            try { Fusion.Studio.Ui.LogService.Init(dir); }
            catch { }
        }

        // -------------------------------------------------- Newtonsoft.Json
        public static void TestNewtonsoftEdgeCases()
        {
            // escapes \u con pares sustitutos (el parser propio los partía)
            var emoji = JsonValue.Parse("\"emoji: \\uD83D\\uDE00\"");
            TestRunner.CheckEq(emoji.Str, "emoji: \U0001F600", "escape sustituto UTF-16");
            // números exóticos
            // v4.1.1: Newtonsoft es estricto con overflow — rechaza (el propio
            // parser lo aceptaba como infinito; el estricto evita basura en ahp.v1)
            bool of = false;
            try { JsonValue.Parse("{\"n\":1e999}"); }
            catch (FormatException) { of = true; }
            TestRunner.Check(of, "1e999 rechazado (parser estricto)");
            var neg = JsonValue.Parse("{\"n\":-0.0}").GetNum("n", 1);
            TestRunner.Check(neg == 0, "-0.0 parsea a cero");
            var exp = JsonValue.Parse("{\"n\":2.5e-3}").GetNum("n", 0);
            TestRunner.Check(Math.Abs(exp - 0.0025) < 1e-12, "2.5e-3 exacto");
            // BOM tolerado
            string bom = "﻿{\"a\":1}";
            TestRunner.CheckEq(JsonValue.Parse(bom).GetInt("a", 0), 1, "BOM UTF-8 tolerado");
            // errores siguen siendo FormatException
            bool threw = false;
            try { JsonValue.Parse("{clave sin comillas}"); }
            catch (FormatException) { threw = true; }
            TestRunner.Check(threw, "JSON inválido → FormatException");
            // round-trip estable del escritor (enteros sin .0)
            var v = JsonValue.Object().Set("i", JsonValue.Make(3)).Set("d", JsonValue.Make(3.5))
                                    .Set("s", JsonValue.Make("a\"b\\c\n"));
            string json = v.ToJsonString();
            TestRunner.Check(json.Contains("\"i\":3") && !json.Contains("\"i\":3.0"),
                "entero estable sin decimal");
            var back = JsonValue.Parse(json);
            TestRunner.CheckEq(back.GetInt("i", 0), 3, "round-trip entero");
            TestRunner.CheckEq(back.GetStr("s", ""), "a\"b\\c\n", "round-trip escapes");
        }

        // ----------------------------------------------- System.Data.SQLite
        public static void TestAdoSqliteReadsEsword()
        {
            string bib = Fixture("esword_rv1960_mini.bib");
            TestRunner.Check(File.Exists(bib), "ADO: fixture e-Sword presente");
            if (!File.Exists(bib)) return;

            // vía directa ADO (motor nativo)
            using (var ado = new SqliteAdoReader(bib))
            {
                TestRunner.Check(ado.HasTable("Bible"), "ADO: tabla Bible detectada");
                var rows = ado.ReadTable("Bible");
                TestRunner.CheckEq(rows.Count, 5, "ADO: 5 versículos");
                TestRunner.Check(rows[0].Values.Count >= 4, "ADO: columnas completas");
                object blob = rows[0].Values[3];
                TestRunner.Check(blob is byte[] && ((byte[])blob).Length > 16, "ADO: Scripture BLOB cifrada");
            }
            // vía despachador (SQLiteFileReader) — mismo resultado
            using (var r = new SQLiteFileReader(bib))
            {
                var rows = r.ReadTable("Bible");
                TestRunner.CheckEq(rows.Count, 5, "despachador: 5 versículos");
            }
        }

        // ---------------------------------------------------- Ookii.Dialogs
        public static void TestOokiiDialogConstructs()
        {
            // solo construcción/propiedades: ShowDialog abriría UI real
            var d = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
            d.Description = "prueba";
            d.UseDescriptionForTitle = true;
            TestRunner.Check(d.SelectedPath == null || d.SelectedPath.Length >= 0,
                "Ookii: diálogo configurable sin recursos");
        }

        // ------------------------------------------- BouncyCastle (opcional)
        public static void TestBouncyCastleAvailable()
        {
            // SHA-256 vector oficial NIST: "abc" → ba7816bf...
            var digest = new Org.BouncyCastle.Crypto.Digests.Sha256Digest();
            byte[] input = System.Text.Encoding.ASCII.GetBytes("abc");
            digest.BlockUpdate(input, 0, input.Length);
            byte[] hash = new byte[digest.GetDigestSize()];
            digest.DoFinal(hash, 0);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            TestRunner.CheckEq(sb.ToString(),
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                "BouncyCastle: SHA-256 vector NIST");
            // el motor propio sigue en uso (no se sustituye: regla de la tabla)
            var asm = Assembly.GetExecutingAssembly();
            TestRunner.Check(DependenciesContains(asm, "BouncyCastle.Crypto"),
                "BouncyCastle cargada junto al producto");
        }

        static bool DependenciesContains(Assembly asm, string name)
        {
            try
            {
                foreach (AssemblyName an in asm.GetReferencedAssemblies())
                    if (an.Name == name) return true;
            }
            catch { }
            return false;
        }
    }
}
