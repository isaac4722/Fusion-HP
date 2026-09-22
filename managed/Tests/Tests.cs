// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Tests.cs : arnés de pruebas propio (estilo selftest, SIN frameworks externos
//  — el repo no admite dependencias adicionales). Compatible net8.0 y net48.
//
//  Cobertura:
//    * MiniJson roundtrip (ñ/á/✓, escapes \n \" \\ \uXXXX, números long/double,
//      bool/null, arreglos anidados, BOM)
//    * ScenarioBuilder → JSON que el builder nativo acepta (LoadScenario==0 si
//      hay biblioteca nativa; si no, SKIPPED)
//    * Settings roundtrip portable (directorio temporal)
//    * Núcleo nativo: version, song_parse, bible_ref_resolve, db+FTS5 (SKIPPED
//      sin biblioteca o con LUMINA_SKIP_NATIVE=1)
//
//  Salida: "TESTS PASS n/n (s skips)" y código 0, o "TESTS FAIL …" y código 1.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using lumina.bridge;
using lumina.core;

namespace lumina.tests
{
    internal static class Tests
    {
        private static int _pass;
        private static int _skip;
        private static readonly List<string> Failures = new List<string>();

        private static int Main()
        {
            Console.WriteLine("Lumina.Tests — selftest de la capa gestionada");

            // --- Solo gestionado (siempre corren) --------------------------------
            Run("MiniJson: roundtrip completo con acentos y escapes", TestMiniJsonRoundtrip);
            Run("MiniJson: números long/double y literales", TestMiniJsonNumbers);
            Run("MiniJson: tolerancia a BOM UTF-8", TestMiniJsonBom);
            Run("ScenarioBuilder: estructura del JSON de escenario", TestScenarioStructure);
            Run("ChordUtil: IsChordLine con el criterio del núcleo", TestChordUtilIsChordLine);
            Run("ChordUtil: DetectKey y tonalidad latina", TestChordUtilDetectKey);
            Run("Settings: roundtrip portable", TestSettingsRoundtrip);

            // --- Núcleo nativo (condicionales) -----------------------------------
            bool skipNative = string.Equals(
                Environment.GetEnvironmentVariable("LUMINA_SKIP_NATIVE"), "1", StringComparison.Ordinal);
            bool libOk = !skipNative && ProbeNativeLibrary();
            if (skipNative) Console.WriteLine("  (LUMINA_SKIP_NATIVE=1 → chequeos nativos omitidos)");
            else if (!libOk) Console.WriteLine("  (biblioteca nativa ausente → chequeos nativos omitidos)");

            Run("Nativo: lumina_version", TestNativeVersion, libOk);
            Run("Nativo: ScenarioBuilder → LoadScenario==0", TestNativeLoadScenario, libOk);
            Run("Nativo: SongParse de la canción del builder", TestNativeSongParse, libOk);
            Run("Nativo: BibleRefResolve Jn 3:16", TestNativeRefResolve, libOk);
            Run("Nativo: ChordsTranspose Do Sol → Re# La# (+3)", TestNativeChordsTranspose, libOk);
            Run("Nativo: BD INSERT/SELECT/FTS5", TestNativeDb, libOk);

            int failed = Failures.Count;
            int total = _pass + _skip + failed;
            if (failed == 0)
            {
                Console.WriteLine("TESTS PASS " + _pass + "/" + total + " (" + _skip + " skips)");
                return 0;
            }
            Console.WriteLine("TESTS FAIL " + failed + "/" + total + " (" + _skip + " skips) detalle:");
            foreach (string f in Failures) Console.WriteLine("  - " + f);
            return 1;
        }

        /* -------------------------------------------------- arnés mínimo ---- */

        private static void Run(string name, Action test, bool enabled = true)
        {
            if (!enabled)
            {
                _skip++;
                Console.WriteLine("  [SKIP] " + name);
                return;
            }
            try
            {
                test();
                _pass++;
                Console.WriteLine("  [ OK ] " + name);
            }
            catch (Exception ex)
            {
                Failures.Add(name + " → " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine("  [FAIL] " + name + " (" + ex.Message + ")");
            }
        }

        private static void AssertTrue(bool cond, string what)
        {
            if (!cond) throw new Exception("falló: " + what);
        }

        /// <summary>Prueba ligera de resolución de DllImport sin crear motor.</summary>
        private static bool ProbeNativeLibrary()
        {
            try
            {
                string v = LuminaEngine.Version();
                return v != null && v.Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /* ------------------------------------------------- MiniJson --------- */

        private static Dictionary<string, object> SampleObject()
        {
            Dictionary<string, object> inner = new Dictionary<string, object>();
            inner["texto"] = "ñ Á é í ó ú ✓ — «comillas»";
            inner["escapes"] = "salto\ncomilla\"barra\\tab\tunicode\u0007";
            inner["entero"] = 42L;
            inner["negativo"] = -7L;
            inner["flotante"] = 1.18;
            inner["si"] = true;
            inner["no"] = false;
            inner["nulo"] = null;
            List<object> arr = new List<object>();
            arr.Add("uno");
            arr.Add(2L);
            arr.Add(inner);
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["nombre"] = "Canción ñ ✓";
            root["items"] = arr;
            root["meta"] = inner;
            return root;
        }

        private static bool DictEquals(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            foreach (KeyValuePair<string, object> kv in a)
            {
                object v2;
                if (!b.TryGetValue(kv.Key, out v2)) return false;
                if (!ValueEquals(kv.Value, v2)) return false;
            }
            return true;
        }

        /// <summary>Comparación profunda: diccionarios, listas y escalares.</summary>
        private static bool ValueEquals(object x, object y)
        {
            if (x == null || y == null) return x == null && y == null;
            Dictionary<string, object> d1 = x as Dictionary<string, object>;
            Dictionary<string, object> d2 = y as Dictionary<string, object>;
            if (d1 != null || d2 != null)
            {
                if (d1 == null || d2 == null || d1.Count != d2.Count) return false;
                foreach (KeyValuePair<string, object> kv in d1)
                {
                    object v2;
                    if (!d2.TryGetValue(kv.Key, out v2)) return false;
                    if (!ValueEquals(kv.Value, v2)) return false;
                }
                return true;
            }
            System.Collections.Generic.List<object> l1 = x as System.Collections.Generic.List<object>;
            System.Collections.Generic.List<object> l2 = y as System.Collections.Generic.List<object>;
            if (l1 != null || l2 != null)
            {
                if (l1 == null || l2 == null || l1.Count != l2.Count) return false;
                for (int i = 0; i < l1.Count; i++)
                    if (!ValueEquals(l1[i], l2[i])) return false;
                return true;
            }
            return x.Equals(y);
        }

        private static void TestMiniJsonRoundtrip()
        {
            Dictionary<string, object> original = SampleObject();
            string json = MiniJson.Serialize(original);
            Dictionary<string, object> parsed = MiniJson.Parse(json);
            AssertTrue(DictEquals(original, parsed), "roundtrip idéntico");

            // Casos específicos pedidos:
            object texto;
            AssertTrue(parsed.TryGetValue("nombre", out texto) &&
                       (string)texto == "Canción ñ ✓", "acentos/emoji preservados");
            object meta = parsed["meta"];
            Dictionary<string, object> m = (Dictionary<string, object>)meta;
            AssertTrue((string)m["escapes"] == "salto\ncomilla\"barra\\tab\tunicode\u0007",
                "escapes \\n \\\" \\\\ \\t \\uXXXX");
        }

        private static void TestMiniJsonNumbers()
        {
            Dictionary<string, object> o = MiniJson.Parse(
                "{\"a\":42,\"b\":-7,\"c\":1.18,\"d\":0.5,\"e\":2e3,\"f\":true,\"g\":null}");
            AssertTrue(MiniJson.GetInt(o, "a", 0) == 42, "long 42");
            AssertTrue(MiniJson.GetInt(o, "b", 0) == -7, "long -7");
            AssertTrue(MiniJson.GetDouble(o, "c", 0) == 1.18, "double 1.18");
            AssertTrue(MiniJson.GetDouble(o, "d", 0) == 0.5, "double 0.5");
            AssertTrue(MiniJson.GetDouble(o, "e", 0) == 2000.0, "notación exponencial");
            AssertTrue(MiniJson.GetBool(o, "f", false), "true");
            AssertTrue(o.ContainsKey("g") && o["g"] == null, "null");
            // Serialización invariante (coma decimal nunca).
            string s = MiniJson.Serialize(new Dictionary<string, object> { { "x", 1.18 } });
            AssertTrue(s == "{\"x\":1.18}", "serialización invariante: " + s);
        }

        private static void TestMiniJsonBom()
        {
            Dictionary<string, object> o = MiniJson.Parse("\uFEFF{\"saludo\":\"hóla ñ\"}");
            AssertTrue(MiniJson.GetString(o, "saludo", "") == "hóla ñ", "BOM tolerado");
        }

        /* --------------------------------------------- ScenarioBuilder ------ */

        private static string SongJsonForTests()
        {
            Song s = new Song();
            s.Title = "Canción de pruebas ✓";
            s.Artist = "LuminaPresentation";
            s.Blocks.Add(Block("Verso 1", "Primera línea ñ", "segunda línea"));
            s.Blocks.Add(Block("Verso 2", "Otra línea áéíóú", "y otra más"));
            s.Blocks.Add(Block("Coro", "Coro de prueba", "aleluya"));
            return MiniJson.Serialize(ScenarioBuilder.SongToDict(s));
        }

        private static SongBlock Block(string label, string l1, string l2)
        {
            SongBlock b = new SongBlock();
            b.Label = label;
            b.Lines.Add(l1);
            b.Lines.Add(l2);
            return b;
        }

        private static string ScenarioJsonForTests()
        {
            List<ScenarioItem> items = new List<ScenarioItem>();
            items.Add(ScenarioBuilder.FromSong(
                ScenarioBuilder.SongFromDict(MiniJson.Parse(SongJsonForTests()))));
            items.Add(ScenarioBuilder.ScriptureItem("Jn 3:16", "", 1, string.Empty));
            items.Add(ScenarioBuilder.TextItem("Bienvenida", "Bienvenidos\nhermanos", 2));
            items.Add(ScenarioBuilder.BlankItem("Silencio"));
            return ScenarioBuilder.BuildScenarioJson("Culto de pruebas", new Theme(), items);
        }

        private static void TestScenarioStructure()
        {
            string json = ScenarioJsonForTests();
            Dictionary<string, object> o = MiniJson.Parse(json);
            AssertTrue(MiniJson.GetString(o, "name", "") == "Culto de pruebas", "name");
            List<object> items = MiniJson.GetArray(o, "items");
            AssertTrue(items.Count == 4, "4 ítems");
            Dictionary<string, object> theme = MiniJson.GetObject(o, "theme");
            AssertTrue(theme != null && MiniJson.GetString(theme, "name", "") == "Predeterminado", "theme");
            Dictionary<string, object> first = (Dictionary<string, object>)items[0];
            AssertTrue(MiniJson.GetString(first, "kind", "") == "song", "ítem 0 = song");
            AssertTrue(MiniJson.GetObject(first, "song") != null, "song anidada");
        }

        /* ------------------------------------------------ Settings ---------- */

        private static void TestSettingsRoundtrip()
        {
            string dir = Path.Combine(Path.GetTempPath(),
                "lumina_tests_" + Guid.NewGuid().ToString("N"));
            try
            {
                Settings written = new Settings(dir);
                written.ApiPort = 9137;
                written.ApiToken = "secreto-ñ-✓";
                written.Theme = "Noche";
                written.LastBibleVersion = "RVR1909";
                written.Save();

                AssertTrue(File.Exists(written.FilePath), "archivo creado en " + written.FilePath);

                Settings read = Settings.Load(dir);
                AssertTrue(read.ApiPort == 9137, "apiPort");
                AssertTrue(read.ApiToken == "secreto-ñ-✓", "apiToken");
                AssertTrue(read.Theme == "Noche", "theme");
                AssertTrue(read.LastBibleVersion == "RVR1909", "lastBibleVersion");

                // Defaults cuando no existe el archivo.
                Settings defaults = Settings.Load(Path.Combine(dir, "vacía"));
                AssertTrue(defaults.ApiPort == 8069, "default port");
                AssertTrue(defaults.ApiToken.Length == 0, "default token");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        /* ----------------------------------------------- Núcleo nativo ------ */

        private static void TestNativeVersion()
        {
            string v = LuminaEngine.Version();
            AssertTrue(v.StartsWith("LuminaCore", StringComparison.Ordinal) && v.Contains("4.2"),
                "version=\"" + v + "\"");
        }

        private static void TestNativeLoadScenario()
        {
            using (LuminaEngine engine = LuminaEngine.Create(true, null))
            {
                string json = ScenarioJsonForTests();
                int st = engine.LoadScenario(json);
                AssertTrue(st == LuminaStatus.Ok, "LoadScenario=" + LuminaStatus.Name(st));
                Dictionary<string, object> state = MiniJson.Parse(engine.StateJson());
                AssertTrue(MiniJson.GetInt(state, "slideCount", 0) > 0, "slideCount>0");
            }
        }

        private static void TestNativeSongParse()
        {
            string res = LuminaEngine.SongParse(SongJsonForTests());
            Dictionary<string, object> o = MiniJson.Parse(res);
            AssertTrue(MiniJson.GetInt(o, "ok", 0) == 1, "ok==1");
            AssertTrue(MiniJson.GetArray(o, "slides").Count > 0, "slides>0");
        }

        private static void TestNativeRefResolve()
        {
            Dictionary<string, object> o = MiniJson.Parse(LuminaEngine.BibleRefResolve("Jn 3:16"));
            AssertTrue(MiniJson.GetInt(o, "book", 0) == 43, "book 43");
            AssertTrue(MiniJson.GetInt(o, "chapter", 0) == 3, "chapter 3");
            AssertTrue(MiniJson.GetInt(o, "verse", 0) == 16, "verse 16");
        }

        private static void TestNativeChordsTranspose()
        {
            // +3 semitonos en notación latina: Do Sol → Re# La# · Am Fa → Dom Sol#
            Dictionary<string, object> a = MiniJson.Parse(LuminaEngine.ChordsTranspose("Do   Sol", 3, true));
            AssertTrue(MiniJson.GetString(a, "line", "") == "Re#   La#",
                "Do Sol +3 → «" + MiniJson.GetString(a, "line", "") + "»");
            Dictionary<string, object> b = MiniJson.Parse(LuminaEngine.ChordsTranspose("Am             Fa", 3, true));
            AssertTrue(MiniJson.GetString(b, "line", "") == "Dom             Sol#",
                "Am Fa +3 → «" + MiniJson.GetString(b, "line", "") + "»");
            // Los tokens que no son acordes pasan intactos (comportamiento del núcleo).
            Dictionary<string, object> c = MiniJson.Parse(LuminaEngine.ChordsTranspose("Aleluya, aleluya", 3, true));
            AssertTrue(MiniJson.GetString(c, "line", "") == "Aleluya, aleluya",
                "letra sin acordes intacta");
        }

        /* ----------------------------------------------- ChordUtil (v4.2.0) - */

        private static void TestChordUtilIsChordLine()
        {
            // Positivos: cifrados reales (latinos y anglosajones)
            AssertTrue(ChordUtil.IsChordLine("Do      Sol"), "Do Sol");
            AssertTrue(ChordUtil.IsChordLine("Am             Fa"), "Am Fa");
            AssertTrue(ChordUtil.IsChordLine("Fa#m7/C#"), "Fa#m7/C#");
            AssertTrue(ChordUtil.IsChordLine("Csus4  Cadd9  Cdim  Caug"), "sufijos");
            AssertTrue(ChordUtil.IsChordLine("Sol7"), "Sol7");
            // Negativos: palabras españolas que NO son acordes (puerta del núcleo)
            AssertTrue(!ChordUtil.IsChordLine("dos"), "dos");
            AssertTrue(!ChordUtil.IsChordLine("mis"), "mis");
            AssertTrue(!ChordUtil.IsChordLine("fue"), "fue");
            AssertTrue(!ChordUtil.IsChordLine("das"), "das");
            AssertTrue(!ChordUtil.IsChordLine("Primera línea de la letra"), "frase normal");
            AssertTrue(!ChordUtil.IsChordLine("Aleluya, aleluya"), "Aleluya, aleluya");
            AssertTrue(!ChordUtil.IsChordLine(""), "vacía");
            AssertTrue(!ChordUtil.IsChordLine(null), "null");
        }

        private static void TestChordUtilDetectKey()
        {
            AssertTrue(ChordUtil.DetectKey("[Verso 1]\nDo           Sol\nAleluya, aleluya") == "Do", "Do");
            AssertTrue(ChordUtil.DetectKey("Mi             Si\nJehová es mi pastor") == "Mi", "Mi");
            AssertTrue(ChordUtil.DetectKey("Fa#m7/C#\ntexto") == "Fa#", "Fa#m7/C#");
            AssertTrue(ChordUtil.DetectKey("solo letra sin acordes") == "—", "sin acordes");
            AssertTrue(ChordUtil.DetectKey("") == "—", "vacío");
        }

        private static void TestNativeDb()
        {
            string dbPath = Path.Combine(Path.GetTempPath(),
                "lumina_tests_" + Guid.NewGuid().ToString("N") + ".db");
            using (LuminaEngine engine = LuminaEngine.Create(true, null))
            {
                try
                {
                    AssertTrue(engine.DbOpen(dbPath) == LuminaStatus.Ok, "DbOpen");
                    engine.DbExec(LuminaStorage.BuildExecJson(
                        "INSERT INTO songs(title,lyrics) VALUES(?,?)",
                        "Canción FTS", "Santo aleluya al Señor"));
                    string res = engine.DbExec(LuminaStorage.BuildExecJson(
                        "SELECT rowid FROM songs_fts WHERE songs_fts MATCH ?1", "aleluya"));
                    AssertTrue(LuminaStorage.Rows(res).Count >= 1, "FTS MATCH 'aleluya'");
                }
                finally
                {
                    try { engine.DbClose(); } catch (Exception) { }
                }
            }
            try { File.Delete(dbPath); } catch (Exception) { }
            try { File.Delete(dbPath + "-journal"); } catch (Exception) { }
        }
    }
}
