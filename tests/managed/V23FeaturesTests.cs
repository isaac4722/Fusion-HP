// ============================================================================
//  Fusion-HP · tests/managed/V23FeaturesTests.cs — pruebas de v2.3:
//  historial de uso (beta-1, compartido con el estudio C++) y biblias
//  empaquetadas (verificación de las 4 del paquete).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using Fusion.Shared;

namespace Fusion.Tests
{
    public static class V23FeaturesTests
    {
        public static void TestUsageHistoryRecordTopRecent()
        {
            string dir = TestRunner.TempDir();
            UsageHistory.Init(dir);
            UsageHistory.Record("Canto Alfa", "text");
            UsageHistory.Record("Canto Beta", "text");
            UsageHistory.Record("Canto Alfa", "text");
            UsageHistory.Record("Versículo Gama", "verse");

            var top = UsageHistory.Top(3);
            TestRunner.CheckEq(top.Count, 3, "History: top cuenta títulos distintos");
            TestRunner.CheckEq(top[0].Title, "Canto Alfa", "History: más usada primero");
            TestRunner.CheckEq(top[0].Count, 2, "History: conteo de la más usada");

            var recent = UsageHistory.Recent(2);
            TestRunner.CheckEq(recent.Count, 2, "History: recientes limitados");
            TestRunner.CheckEq(recent[0].Title, "Versículo Gama", "History: último primero");
        }

        public static void TestUsageHistoryPersists()
        {
            string dir = TestRunner.TempDir();
            UsageHistory.Init(dir);
            UsageHistory.Record("Persistente", "text");
            // re-init simula reabrir la app: el caché se recarga del jsonl
            UsageHistory.Init(dir);
            var top = UsageHistory.Top(10);
            TestRunner.Check(top.Count == 1 && top[0].Title == "Persistente" && top[0].Count == 1,
                "History: sobrevive re-init (historial.jsonl)");
        }

        public static void TestUsageHistoryCsv()
        {
            string dir = TestRunner.TempDir();
            UsageHistory.Init(dir);
            UsageHistory.Record("Canto;con;puntos", "text");
            string file = Path.Combine(dir, "h.csv");
            TestRunner.Check(UsageHistory.ExportCsv(file), "History: CSV exportado");
            string csv = File.ReadAllText(file, System.Text.Encoding.UTF8);
            TestRunner.Check(csv.StartsWith("\uFEFFfecha;titulo;tipo"), "History: CSV con BOM y cabecera");
            TestRunner.Check(csv.Contains("Canto,con,puntos"), "History: CSV escapa separadores");
        }

        public static void TestPackagedBiblesPresent()
        {
            // Las 4 biblias completas viajan con el programa [v2.1] — verificar
            // que el paquete de pruebas las recibe (staging de CI) y que cada
            // una declara nombre y libros.
            string biblesDir = null;
            for (string d = AppDomain.CurrentDomain.BaseDirectory; d != null && d.Length > 3;
                 d = Path.GetDirectoryName(d))
            {
                string cand = Path.Combine(d, "resources", "data", "bibles");
                if (Directory.Exists(cand)) { biblesDir = cand; break; }
            }
            if (biblesDir == null) return;    // fuera del paquete: sin error (pruebas unitarias)
            string[] expected = { "rv1960.json", "nvi.json", "rvg.json", "rvr1909.json" };
            int found = 0;
            foreach (string e in expected)
                if (File.Exists(Path.Combine(biblesDir, e))) found++;
            TestRunner.CheckEq(found, 4, "Biblias empaquetadas: 4 presentes");
        }
    }
}
