// ============================================================================
//  Fusion-HP · tests/managed/BibleImporterTests.cs — Twofish (vectores
//  oficiales), lector SQLite, importador e-Sword completo, Zefania, JSON y
//  búsqueda ≤200 ms [SPEC §12.3.5].
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using Fusion.Shared;
using Fusion.Shared.Bible;
using Fusion.Tests;

namespace Fusion.Tests
{
    static class BibleImporterTests
    {
        public static void TestTwofishVectoresOficiales()
        {
            // Vectores ECB oficiales del Twofish (tabla de la presentación AES,
            // verificados contra la implementación de referencia):
            //   E_K(P) = C; probamos DecryptBlock(C) == P
            var tf0 = new Twofish128(new byte[16]);
            var c0 = HexBytes("9F589F5CF6122C32B6BFEC2F2AE8C35A");
            tf0.DecryptBlock(c0, 0);
            TestRunner.CheckEq(Hex(c0), "00000000000000000000000000000000", "ECB I=0 (E_0(0)=9F58…)");

            var tf1 = new Twofish128(HexBytes("9F589F5CF6122C32B6BFEC2F2AE8C35A"));
            var c1 = HexBytes("D491DB16E7B1C39E86CB086B789F5419");
            tf1.DecryptBlock(c1, 0);
            TestRunner.CheckEq(Hex(c1), "E00384D1AE4805907FDF415ACFC22137", "ECB I=1");

            var tf2 = new Twofish128(HexBytes("D491DB16E7B1C39E86CB086B789F5419"));
            var c2 = HexBytes("019F98037C0FDB58062D60B65BB08447");
            tf2.DecryptBlock(c2, 0);
            TestRunner.CheckEq(Hex(c2), "22C8B77F8C11EFFCD74E1DEB986A8DEA", "ECB I=2");
        }

        static string Hex(byte[] b)
        {
            var sb = new System.Text.StringBuilder();
            foreach (byte x in b) sb.Append(x.ToString("X2"));
            return sb.ToString();
        }
        static byte[] HexBytes(string h)
        {
            var r = new byte[h.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = Convert.ToByte(h.Substring(i * 2, 2), 16);
            return r;
        }

        public static void TestSqliteLector()
        {
            string fixture = TestRunner.FixturePath("esword_rv1960_mini.bib");
            using (var db = new SQLiteFileReader(fixture))
            {
                TestRunner.Check(db.HasTable("Bible"), "tabla Bible");
                TestRunner.Check(db.HasTable("Details"), "tabla Details");
                var rows = db.ReadTable("Bible");
                TestRunner.CheckEq(rows.Count, 5, "5 versos");
                TestRunner.CheckEq(Convert.ToInt32(rows[0].Values[0]), 1, "libro Génesis");
                TestRunner.CheckEq(Convert.ToInt32(rows[3].Values[1]), 3, "Juan cap 3");
                var details = db.ReadTable("Details");
                TestRunner.Check(details.Count > 0, "details presente");
            }
        }

        public static void TestEswordImporterCompleto()
        {
            // SQLite + Twofish("Heb_4:12") + contenedor SQLitePlus + zlib [SPEC §9.1.2]
            string dir = TestRunner.TempDir();
            var store = new BibleStore(dir);
            var rep = store.ImportEsword(TestRunner.FixturePath("esword_rv1960_mini.bib"));
            TestRunner.Check(rep.Ok, "importación OK; avisos: " + string.Join("; ", rep.Warnings.ToArray()));
            TestRunner.CheckEq(rep.Verses, 5, "5 versos importados");
            var bibles = store.List();
            TestRunner.CheckEq(bibles.Count, 1, "biblia instalada");
            TestRunner.Check(bibles[0].Name.Contains("Reina Valera"), "nombre: " + bibles[0].Name);
            // pasaje exacto
            var refr = BibleReference.Parse("Génesis 1:1");
            var passage = store.GetPassage(bibles[0], refr);
            TestRunner.CheckEq(passage.Count, 1, "Génesis 1:1");
            TestRunner.Check(passage[0].StartsWith("En el principio"), "texto: " + passage[0]);
            var j316 = store.GetPassage(bibles[0], BibleReference.Parse("Juan 3:16"));
            TestRunner.Check(j316[0].Contains("de tal manera"), "Juan 3:16");
        }

        public static void TestZefaniaImporter()
        {
            string dir = TestRunner.TempDir();
            var store = new BibleStore(dir);
            var rep = store.ImportZefania(TestRunner.FixturePath("zefania_sample.xml"));
            TestRunner.Check(rep.Ok, "zefania OK");
            TestRunner.CheckEq(rep.Verses, 5, "5 versos");
            TestRunner.CheckEq(rep.Books, 3, "3 libros");
            var b = store.List()[0];
            var passage = store.GetPassage(b, BibleReference.Parse("Sal 23:1"));
            TestRunner.CheckEq(passage.Count, 1, "Sal 23:1");
            TestRunner.Check(passage[0].StartsWith("Jehová es mi pastor"), "texto salmo");
        }

        public static void TestTsvLegadoImporter()
        {
            string dir = TestRunner.TempDir();
            var store = new BibleStore(dir);
            var rep = store.ImportTsv(TestRunner.FixturePath("rvr1909_tsv.bib"));
            TestRunner.Check(rep.Ok, "TSV OK: " + string.Join("; ", rep.Warnings.ToArray()));
            TestRunner.Check(rep.Verses > 30000, "biblia completa: " + rep.Verses);
            var b = store.List()[0];
            var passage = store.GetPassage(b, BibleReference.Parse("Juan 3:16"));
            TestRunner.CheckEq(passage.Count, 1, "Jn 3:16 presente");
        }

        public static void TestBusquedaBiblicaRapida()
        {
            // Criterio F4 [SPEC §12.3.5]: búsqueda por palabra ≤ 200 ms
            string dir = TestRunner.TempDir();
            var store = new BibleStore(dir);
            store.ImportTsv(TestRunner.FixturePath("rvr1909_tsv.bib"));
            var b = store.List()[0];
            var sw = Stopwatch.StartNew();
            var hits = store.Search(b, "pastor", 20);
            sw.Stop();
            TestRunner.Check(hits.Count > 0, "resultados: " + hits.Count);
            Console.Write("(" + sw.ElapsedMilliseconds + " ms) ");
            TestRunner.Check(sw.ElapsedMilliseconds <= 200, "búsqueda ≤ 200 ms (tomó " + sw.ElapsedMilliseconds + " ms)");
        }

        public static void TestModuloCifradoCompletoMensaje()
        {
            // El .bib de la referencia (es_rv1960.bib) está cifrado a nivel de archivo:
            // el importador debe informarlo con mensaje humano, sin excepción [SPEC §11.2.3]
            byte[] head = new byte[16];
            bool isSqlite;
            using (var f = File.OpenRead(TestRunner.FixturePath("esword_rv1960_mini.bib")))
            {
                f.Read(head, 0, 16);
                isSqlite = System.Text.Encoding.ASCII.GetString(head, 0, 16) == "SQLite format 3\0";
            }
            TestRunner.Check(isSqlite, "detección SQLite del fixture");

            // Simular archivo cifrado: bytes aleatorios sin firma
            string dir = TestRunner.TempDir();
            string fake = Path.Combine(dir, "cifrado.bib");
            var rnd = new Random(42);
            var bytes = new byte[2048];
            rnd.NextBytes(bytes);
            File.WriteAllBytes(fake, bytes);
            var store = new BibleStore(dir);
            var rep = store.ImportEsword(fake);
            TestRunner.Check(!rep.Ok, "importación rechazada");
            TestRunner.Check(rep.Warnings.Count > 0, "con aviso");
            TestRunner.Check(rep.Warnings[0].Contains("cifrado completo"), "mensaje humano: " + rep.Warnings[0]);
        }
    }
}
