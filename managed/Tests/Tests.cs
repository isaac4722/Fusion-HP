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
        private static bool _nativeOk;   // fija Main() tras ProbeNativeLibrary
        private static int _pass;
        private static int _skip;
        private static readonly List<string> Failures = new List<string>();

        private static int Main()
        {
            Console.WriteLine("Lumina.Tests — selftest de la capa gestionada");

            // --- Sondeo del núcleo nativo (antes de los Run: TestScenarioHighlight
            //     del grupo «siempre corren» también lo usa) ----------------------
            bool skipNative = string.Equals(
                Environment.GetEnvironmentVariable("LUMINA_SKIP_NATIVE"), "1", StringComparison.Ordinal);
            bool libOk = !skipNative && ProbeNativeLibrary();
            _nativeOk = libOk;
            if (skipNative) Console.WriteLine("  (LUMINA_SKIP_NATIVE=1 → chequeos nativos omitidos)");
            else if (!libOk) Console.WriteLine("  (biblioteca nativa ausente → chequeos nativos omitidos)");

            // --- Solo gestionado (siempre corren) --------------------------------
            Run("MiniJson: roundtrip completo con acentos y escapes", TestMiniJsonRoundtrip);
            Run("MiniJson: números long/double y literales", TestMiniJsonNumbers);
            Run("MiniJson: tolerancia a BOM UTF-8", TestMiniJsonBom);
            Run("ScenarioBuilder: estructura del JSON de escenario", TestScenarioStructure);
            Run("ScenarioBuilder: highlight en proyección (v6.0.0)", TestScenarioHighlight);
            Run("ChordUtil: IsChordLine con el criterio del núcleo", TestChordUtilIsChordLine);
            Run("ChordUtil: DetectKey y tonalidad latina", TestChordUtilDetectKey);
            Run("Settings: roundtrip portable", TestSettingsRoundtrip);

            // --- v5.0.0 «SINERGIA» (siempre corren) -----------------------------
            Run("PptxExporter: estructura OPC mínima válida", TestPptxStructure);
            Run("PptxExporter: texto y tema en slide1.xml", TestPptxSlideContent);
            Run("PdfExporter: estructura PDF 1.4 y xref coherente", TestPdfStructure);
            Run("PdfExporter: páginas, acentos WinAnsi e imagen DCTDecode", TestPdfContent);
            Run("ZefaniaBible: parseo streaming y tolerancia", TestZefania);
            Run("OsisBible: OSIS XML académico (v6.0.0)", TestOsis);
            Run("PptxImporter: round-trip exportar→importar (v6.0.0)", TestPptxImport);
            Run("TriggerEngine: reglas, condiciones y persistencia", TestTriggerEngine);
            Run("ObsProtocol: handshake V5 y mensajes", TestObsProtocol);

            // --- v5.1.0 «FUNDAMENTO» (siempre corren) ----------------------------
            Run("BooksTable: 66 libros y referencias legibles", TestBooksTable);
            Run("BibleJson: lector streaming de biblias JSON", TestBibleJson);
            Run("ZipBackup: ZIP válido y extraíble", TestZipBackup);

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

                // Muestras para el gate de validación externa del CI (python-pptx / pypdf):
                // LUMINA_EXPORT_SAMPLES=<dir> → exporta muestra.pptx y muestra.pdf.
                string sampleDir = Environment.GetEnvironmentVariable("LUMINA_EXPORT_SAMPLES");
                if (!string.IsNullOrEmpty(sampleDir))
                {
                    try
                    {
                        Directory.CreateDirectory(sampleDir);
                        Theme t = new Theme();
                        List<ExportSlide> slides = SampleExportSlides();
                        File.WriteAllBytes(Path.Combine(sampleDir, "muestra.pptx"),
                            PptxExporter.ExportToBytes("Muestra CI", slides, t));
                        File.WriteAllBytes(Path.Combine(sampleDir, "muestra.pdf"),
                            PdfExporter.ExportToBytes("Muestra CI", slides, t));
                        Console.WriteLine("  muestras escritas en " + sampleDir +
                            " (muestra.pptx / muestra.pdf)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  aviso: no se pudieron escribir las muestras: " + ex.Message);
                    }
                }
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

        private static bool NativeAvailable() { return _nativeOk; }

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

        private static void TestScenarioHighlight()
        {
            // v6.0.0: «highlight» viaja al núcleo y regresa íntegro (contrato
            // aditivo: ítems sin highlight NO emiten el campo).
            ScenarioItem it = ScenarioBuilder.TextItem("Aviso", "linea uno\nlinea dos", 4);
            it.Highlight = "misericordia";
            string json = ScenarioBuilder.BuildScenarioJson("HL", new Theme(),
                new ScenarioItem[] { it });
            Dictionary<string, object> o = MiniJson.Parse(json);
            List<object> items = MiniJson.GetArray(o, "items");
            AssertTrue(items.Count == 1, "1 ítem");
            Dictionary<string, object> item = (Dictionary<string, object>)items[0];
            AssertTrue(MiniJson.GetString(item, "highlight", "") == "misericordia",
                "highlight emitido: " + MiniJson.GetString(item, "highlight", ""));

            ScenarioItem plain = ScenarioBuilder.TextItem("Sin resaltado", "texto", 4);
            string json2 = ScenarioBuilder.BuildScenarioJson("HL2", new Theme(),
                new ScenarioItem[] { plain });
            AssertTrue(json2.IndexOf("\"highlight\"", StringComparison.Ordinal) < 0,
                "sin highlight → campo ausente (contrato aditivo)");

            // El núcleo (si está disponible) acepta el JSON con highlight,
            // aplana las slides y las cuenta en el estado (patrón de los tests
            // nativos: engine local desechable, headless).
            if (NativeAvailable())
            {
                using (LuminaEngine engine = LuminaEngine.Create(true, null))
                {
                    int st = engine.LoadScenario(json);
                    AssertTrue(st == LuminaStatus.Ok, "LoadScenario=" + LuminaStatus.Name(st));
                    Dictionary<string, object> state = MiniJson.Parse(engine.StateJson());
                    AssertTrue(MiniJson.GetInt(state, "slideCount", 0) > 0, "slideCount>0");
                }
            }
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
            AssertTrue(v.StartsWith("LuminaCore", StringComparison.Ordinal) && v.Contains("5.2"),
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

        /* ============================================== v5.0.0 «SINERGIA» == */

        /// <summary>Slides de prueba reutilizadas por exportadores.</summary>
        private static List<ExportSlide> SampleExportSlides()
        {
            List<ExportSlide> slides = new List<ExportSlide>();
            ExportSlide title = new ExportSlide();
            title.Kind = SlideKind.Title;
            title.Title = "Culto de prueba";
            title.Subtitle = "Iglesia — Sala Mayor";
            slides.Add(title);

            ExportSlide verse = new ExportSlide();
            verse.Kind = SlideKind.Scripture;
            verse.RefLabel = "Juan 3:16";
            verse.Lines.Add("Porque de tal manera amó Dios al mundo");
            verse.Lines.Add("que ha dado a su Hijo unigénito…");
            slides.Add(verse);

            ExportSlide blank = new ExportSlide();
            blank.Kind = SlideKind.Blank;
            slides.Add(blank);
            return slides;
        }

        // ---- lector ZIP mínimo (EOCD → directorio central): cero dependencias

        private static List<string> ZipEntries(byte[] zip)
        {
            List<string> names = new List<string>();
            int eocd = FindEocd(zip);
            int count = zip[eocd + 10] | (zip[eocd + 11] << 8);
            int cdOff = BitConverter.ToInt32(zip, eocd + 16);
            int p = cdOff;
            for (int i = 0; i < count; i++)
            {
                AssertTrue(zip[p] == 0x50 && zip[p + 1] == 0x4B && zip[p + 2] == 0x01 && zip[p + 3] == 0x02,
                    "firma de entrada " + i + " del directorio central");
                int nameLen = zip[p + 28] | (zip[p + 29] << 8);
                int extraLen = zip[p + 30] | (zip[p + 31] << 8);
                int commLen = zip[p + 32] | (zip[p + 33] << 8);
                names.Add(Encoding.ASCII.GetString(zip, p + 46, nameLen));
                p += 46 + nameLen + extraLen + commLen;
            }
            return names;
        }

        private static int FindEocd(byte[] zip)
        {
            for (int i = zip.Length - 22; i >= 0 && i >= zip.Length - 22 - 65535; i--)
            {
                if (zip[i] == 0x50 && zip[i + 1] == 0x4B && zip[i + 2] == 0x05 && zip[i + 3] == 0x06) return i;
            }
            return -1;
        }

        /// <summary>Latin-1 para inspección de bytes WinAnsi (net8: Latin1; Framework: ISO-8859-1).</summary>
        private static Encoding Latin1
        {
            get
            {
#if NET8_0
                return Encoding.Latin1;
#else
                return Encoding.GetEncoding("ISO-8859-1");
#endif
            }
        }

        /// <summary>
        /// Extrae el contenido de una entrada del ZIP (STORED o DEFLATE crudo —
        /// el deflate de ZIP es RFC1951, justo lo que Desinfla DeflateStream).
        /// </summary>
        private static string ExtractEntry(byte[] zip, string entryName)
        {
            int eocd = FindEocd(zip);
            AssertTrue(eocd >= 0, "EOCD encontrado");
            int count = zip[eocd + 10] | (zip[eocd + 11] << 8);
            int p = BitConverter.ToInt32(zip, eocd + 16);
            for (int i = 0; i < count; i++)
            {
                int nameLen = zip[p + 28] | (zip[p + 29] << 8);
                int extraLen = zip[p + 30] | (zip[p + 31] << 8);
                int commLen = zip[p + 32] | (zip[p + 33] << 8);
                string name = Encoding.ASCII.GetString(zip, p + 46, nameLen);
                int method = zip[p + 10] | (zip[p + 11] << 8);
                int localOff = BitConverter.ToInt32(zip, p + 42);
                int csize = BitConverter.ToInt32(zip, p + 20);
                int usize = BitConverter.ToInt32(zip, p + 24);
                if (name == entryName)
                {
                    int lnl = zip[localOff + 26] | (zip[localOff + 27] << 8);
                    int lel = zip[localOff + 28] | (zip[localOff + 29] << 8);
                    int data = localOff + 30 + lnl + lel;
                    if (method == 0)
                        return Encoding.UTF8.GetString(zip, data, csize);
                    if (method == 8)
                    {
                        using (MemoryStream src = new MemoryStream(zip, data, csize))
                        using (System.IO.Compression.DeflateStream ds =
                            new System.IO.Compression.DeflateStream(src,
                                System.IO.Compression.CompressionMode.Decompress))
                        using (MemoryStream outMs = new MemoryStream())
                        {
                            byte[] buf = new byte[8192];
                            int n;
                            while ((n = ds.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, n);
                            AssertTrue(outMs.Length == usize, "tamaño tras inflar coincide (" +
                                outMs.Length + " vs " + usize + ")");
                            return Encoding.UTF8.GetString(outMs.ToArray());
                        }
                    }
                    throw new Exception("método ZIP no soportado: " + method);
                }
                p += 46 + nameLen + extraLen + commLen;
            }
            throw new Exception("entrada no encontrada: " + entryName);
        }

        private static void TestPptxStructure()
        {
            Theme t = new Theme();
            byte[] zip = PptxExporter.ExportToBytes("Escenario ñ", SampleExportSlides(), t);
            AssertTrue(zip.Length > 2000, "tamaño razonable: " + zip.Length);
            AssertTrue(zip[0] == 0x50 && zip[1] == 0x4B, "firma PK");

            List<string> names = ZipEntries(zip);
            string[] required = new string[]
            {
                "[Content_Types].xml", "_rels/.rels", "docProps/core.xml", "docProps/app.xml",
                "ppt/presentation.xml", "ppt/_rels/presentation.xml.rels",
                "ppt/theme/theme1.xml",
                "ppt/slideMasters/slideMaster1.xml", "ppt/slideMasters/_rels/slideMaster1.xml.rels",
                "ppt/slideLayouts/slideLayout1.xml", "ppt/slideLayouts/_rels/slideLayout1.xml.rels",
                "ppt/slides/slide1.xml", "ppt/slides/_rels/slide1.xml.rels",
                "ppt/slides/slide2.xml", "ppt/slides/_rels/slide2.xml.rels",
                "ppt/slides/slide3.xml", "ppt/slides/_rels/slide3.xml.rels"
            };
            foreach (string req in required)
            {
                AssertTrue(names.Contains(req), "parte presente: " + req);
            }
            AssertTrue(names.Count == required.Length, "sin partes extra: " + names.Count +
                " → [" + string.Join(", ", names.ToArray()) + "]");
        }

        private static void TestPptxSlideContent()
        {
            Theme t = new Theme();
            t.BgColor = "#FF112233";
            t.AccentColor = "#FFAABBCC";
            t.FontSize = 54;
            byte[] zip = PptxExporter.ExportToBytes("Título", SampleExportSlides(), t);

            // La slide1 (TÍTULO) debe llevar el texto, la tipografía y el acento
            string slide1 = ExtractEntry(zip, "ppt/slides/slide1.xml");
            AssertTrue(slide1.Contains("Culto de prueba"), "título en slide1");
            AssertTrue(slide1.Contains("sz=\"2700\""), "tipografía 27 pt (54 px/2, centipuntos)");
            AssertTrue(slide1.Contains("typeface=\"" + t.FontFace + "\""), "fuente del tema");
            // slide2 (ESCRITURA): referencia acentuada al pie
            string slide2 = ExtractEntry(zip, "ppt/slides/slide2.xml");
            AssertTrue(slide2.Contains("Juan 3:16"), "referencia en slide2");
            AssertTrue(slide2.Contains("AABBCC"), "color de acento del tema en slide2");
            // tema aplicado al fondo del master
            string master = ExtractEntry(zip, "ppt/slideMasters/slideMaster1.xml");
            AssertTrue(master.Contains("112233"), "bg del tema en el master");
            // presentation.xml: tamaño 16:9 en EMU + 3 slides
            string pres = ExtractEntry(zip, "ppt/presentation.xml");
            AssertTrue(pres.Contains("cx=\"12192000\""), "ancho 16:9 en EMU");
            AssertTrue(pres.Contains("cy=\"6858000\""), "alto 16:9 en EMU");
            string ct = ExtractEntry(zip, "[Content_Types].xml");
            AssertTrue(ct.Contains("slides/slide3.xml"), "override slide3 en Content_Types");
        }

        private static void TestPdfStructure()
        {
            Theme t = new Theme();
            byte[] pdf = PdfExporter.ExportToBytes("Escenario ñ", SampleExportSlides(), t);
            string head = Encoding.ASCII.GetString(pdf, 0, 9);
            AssertTrue(head == "%PDF-1.4\n", "cabecera %PDF-1.4");

            // EOF + startxref presente y el offset apunta a 'xref'
            string tail = Encoding.ASCII.GetString(pdf, pdf.Length - 64, 64);
            AssertTrue(tail.Contains("%%EOF"), "%%EOF al final");
            int sx = tail.LastIndexOf("startxref");
            string num = tail.Substring(sx + 9).TrimStart('\n', '\r', ' ');
            int end = num.IndexOfAny(new char[] { '\n', '\r', ' ' });
            int xrefPos = int.Parse(num.Substring(0, end), CultureInfo.InvariantCulture);
            AssertTrue(Encoding.ASCII.GetString(pdf, xrefPos, 4) == "xref", "startxref → tabla xref");

            // Cabecera "0 N" + TODOS los offsets del xref apuntan a "N 0 obj"
            // (la primera línea es "xref", la segunda "0 N", luego las entradas)
            int nl1 = Array.IndexOf(pdf, (byte)'\n', xrefPos);
            int nl2 = Array.IndexOf(pdf, (byte)'\n', nl1 + 1);
            string hdr = Encoding.ASCII.GetString(pdf, nl1 + 1, nl2 - nl1 - 1);
            AssertTrue(hdr.StartsWith("0 ", StringComparison.Ordinal), "subtabla desde 0: " + hdr);
            int entries = int.Parse(hdr.Substring(2), CultureInfo.InvariantCulture);
            AssertTrue(entries >= 6, "al menos 6 entradas (hay " + entries + ")");
            for (int id = 1; id < entries; id++)
            {
                // entry 0 (libre) ocupa 20 bytes; la entrada de «id» empieza en id*20
                string o = Encoding.ASCII.GetString(pdf, nl2 + 1 + id * 20, 10);
                int pos = int.Parse(o, CultureInfo.InvariantCulture);
                string marker = Encoding.ASCII.GetString(pdf, pos, 8);
                AssertTrue(marker == (id + " 0 obj ") ||
                           marker.StartsWith(id + " 0 obj", StringComparison.Ordinal),
                    "offset del objeto " + id + " exacto");
            }
        }

        private static void TestPdfContent()
        {
            Theme t = new Theme();
            List<ExportSlide> slides = SampleExportSlides();

            // Slide de IMAGEN: JPEG 1×1 (DCTDecode directo, bytes reales en tmp)
            // — valida la rama de imágenes SIN depender de GDI+ (multiplataforma).
            string jpg = Path.Combine(Path.GetTempPath(), "lumina_1x1_" +
                Guid.NewGuid().ToString("N") + ".jpg");
            // JPEG 1×1 REAL (generado con PIL, determinista): DCTDecode directo
            File.WriteAllBytes(jpg, Convert.FromBase64String(
                "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD9U6KKKAP/2Q=="));
            try
            {
                ExportSlide img = new ExportSlide();
                img.Kind = SlideKind.Image;
                img.Title = "Prueba de imagen";
                img.ImagePath = jpg;
                slides.Add(img);

                byte[] pdf = PdfExporter.ExportToBytes("Título ñ", slides, t);
                string all = Latin1.GetString(pdf);

                AssertTrue(ContainsByte(pdf, (byte)'ó'), "acento 'ó' presente (WinAnsi 0xF3)");
                int pages = CountOccurrences(all, "/Type /Page");
                AssertTrue(pages == 4, "páginas == 4 (hay " + pages + ")");
                AssertTrue(all.Contains("/BaseFont /Helvetica"), "fuente base-14 Helvetica");
                AssertTrue(all.Contains("/Producer (LuminaPresentation Suite)"), "metadatos Producer");
                AssertTrue(all.Contains("/WinAnsiEncoding"), "WinAnsiEncoding");
                AssertTrue(all.Contains("/MediaBox [0 0 960 540]"), "MediaBox 16:9");
                AssertTrue(all.Contains("/DCTDecode"), "imagen JPEG embebida (DCTDecode)");
                AssertTrue(all.Contains("/Width 1 ") || all.Contains("/Width 1/"), "ancho leído del SOF");
            }
            finally
            {
                try { File.Delete(jpg); } catch (Exception) { }
            }
        }

        private static bool ContainsByte(byte[] data, byte b)
        {
            foreach (byte x in data) if (x == b) return true;
            return false;
        }

        private static int CountOccurrences(string s, string sub)
        {
            // "/Type /Page" sin contar "/Pages": se busca con espacio final
            int n = 0, i = 0;
            string needle = sub.EndsWith("/Page", StringComparison.Ordinal) ? sub + " " : sub;
            while ((i = s.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        // -------------------------------------------------- v5.1.0 «FUNDAMENTO»

        private static void TestBooksTable()
        {
            // 66 libros con numeración canónica continua 1..66.
            AssertTrue(BooksTable.NameOf(1) == "Génesis", "libro 1 debe ser Génesis");
            AssertTrue(BooksTable.NameOf(66) == "Apocalipsis", "libro 66 debe ser Apocalipsis");
            AssertTrue(BooksTable.NameOf(67).Length == 0, "fuera de rango debe dar vacío");
            AssertTrue(BooksTable.NameOf(43) == "Juan" && BooksTable.AbbrOf(43) == "jn",
                "libro 43 = Juan / jn (espejo de BibleRef.cpp)");
            // Referencias legibles que el núcleo debe resolver de vuelta.
            AssertTrue(BooksTable.Reference(43, 3, 16, 16) == "jn 3:16", "referencia simple");
            AssertTrue(BooksTable.Reference(43, 3, 16, 18) == "jn 3:16-18", "referencia con rango");
            AssertTrue(BooksTable.Reference(19, 23, 1, 1) == "sal 23:1", "salmos abrevian 'sal'");
            // Coherencia con el resolutor del núcleo (si hay biblioteca).
            if (ProbeNativeLibrary())
            {
                foreach (int book in new int[] { 1, 19, 43, 66 })
                {
                    string refTxt = BooksTable.Reference(book, 1, 1, 1);
                    string resJson = LuminaEngine.BibleRefResolve(refTxt);
                    Dictionary<string, object> o = MiniJson.Parse(resJson);
                    AssertTrue(MiniJson.GetInt(o, "valid", 0) == 1 &&
                               MiniJson.GetInt(o, "book", 0) == book,
                        "el núcleo debe resolver '" + refTxt + "' al libro " + book);
                }
            }
        }

        private static void TestBibleJson()
        {
            // Mini-biblia en el formato del paquete (resources/data/*.json).
            string json =
                "{\"version\":\"TEST\",\"name\":\"Prueba\",\"license\":\"PD\"," +
                "\"books\":[" +
                "{\"n\":1,\"name\":\"Génesis\",\"abbr\":\"Gén\",\"chapters\":[[" +
                "\"EN el principio crió Dios los cielos y la tierra.\"," +
                "\"Y la tierra estaba desordenada y vacía.\"]]}," +
                "{\"n\":43,\"name\":\"Juan\",\"abbr\":\"Jn\",\"chapters\":[[" +
                "\"Porque de tal manera amó Dios al mundo…\"," +
                "\"línea con \\\"escape\\\" y \\n salto\"]," +
                "[\"capítulo dos verso uno\"]]}]}";
            string dir = Path.Combine(Path.GetTempPath(), "lumina-tests-" +
                Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            try
            {
                string file = Path.Combine(dir, "bible.json");
                File.WriteAllText(file, json, new UTF8Encoding(false));
                BibleJsonResult r = BibleJson.ParseFile(file, 0);

                AssertTrue(r.Version == "TEST" && r.Name == "Prueba", "metadatos leídos");
                AssertTrue(r.BookCount == 2, "2 libros contados");
                AssertTrue(r.VerseCount == 5, "5 versículos (haya " + r.VerseCount + ")");
                AssertTrue(r.SkippedRows == 0, "sin descartes");
                // Fila [version, book, chapter, verse, text] en orden.
                List<object> first = (List<object>)r.Rows[0];
                AssertTrue(Convert.ToString(first[0]) == "TEST" &&
                           Convert.ToInt64(first[1], CultureInfo.InvariantCulture) == 1 &&
                           Convert.ToInt64(first[2], CultureInfo.InvariantCulture) == 1 &&
                           Convert.ToInt64(first[3], CultureInfo.InvariantCulture) == 1 &&
                           first[4].ToString().StartsWith("EN el principio", StringComparison.Ordinal),
                    "primera fila correcta");
                // El escape \" debe quedar como comilla.
                List<object> esc = (List<object>)r.Rows[3];
                AssertTrue(esc[4].ToString().IndexOf("\"escape\"", StringComparison.Ordinal) >= 0,
                    "escape de comillas procesado");
                // Capítulo 2 de Juan (índice correcto tras arrays anidados).
                List<object> cap2 = (List<object>)r.Rows[4];
                AssertTrue(Convert.ToInt64(cap2[2], CultureInfo.InvariantCulture) == 2,
                    "capítulo 2 bien numerado");
                // maxVerses recorta y cuenta el resto como descartado.
                BibleJsonResult clipped = BibleJson.ParseFile(file, 2);
                AssertTrue(clipped.VerseCount == 2 && clipped.SkippedRows == 3,
                    "límite de versículos respetado");
                // JSON corrupto → FormatException, no colgado.
                File.WriteAllText(file, "{\"version\":\"X\",\"books\":[{", new UTF8Encoding(false));
                bool threw = false;
                try { BibleJson.ParseFile(file, 0); }
                catch (FormatException) { threw = true; }
                AssertTrue(threw, "JSON truncado debe lanzar FormatException");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        private static void TestZipBackup()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lumina-zip-" +
                Guid.NewGuid().ToString("N").Substring(0, 8));
            string sub = Path.Combine(dir, "temas");
            Directory.CreateDirectory(sub);
            string zipPath = dir + ".zip";
            try
            {
                File.WriteAllText(Path.Combine(dir, "settings.json"),
                    "{\"apiPort\":8069}", new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(sub, "Adventista.json"),
                    "ñ acentos y \"comillas\"", new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(dir, "vacio.txt"), "");

                string err = ZipBackup.CreateFromDirectory(dir, zipPath);
                AssertTrue(err == null, "creación sin errores: " + err);
                AssertTrue(File.Exists(zipPath) && new FileInfo(zipPath).Length > 150,
                    "el ZIP existe y tiene contenido");

                // Validación ESTRICTA con un lector ZIP real (net8/net48:
                // System.IO.Compression.ZipFile está disponible en el arnés).
                using (System.IO.Compression.ZipArchive za =
                           System.IO.Compression.ZipFile.OpenRead(zipPath))
                {
                    AssertTrue(za.Entries.Count == 3, "3 entradas (haya " + za.Entries.Count + ")");
                    foreach (System.IO.Compression.ZipArchiveEntry e in za.Entries)
                    {
                        // La longitud descomprimida y el CRC los valida el lector.
                        AssertTrue(e.Length >= 0, "entrada legible: " + e.FullName);
                        if (e.FullName == "settings.json")
                        {
                            using (StreamReader rd = new StreamReader(e.Open(), new UTF8Encoding(false)))
                                AssertTrue(rd.ReadToEnd().Contains("8069"),
                                    "contenido íntegro tras descomprimir");
                        }
                        if (e.FullName == "temas/Adventista.json")
                        {
                            using (StreamReader rd = new StreamReader(e.Open(), new UTF8Encoding(false)))
                                AssertTrue(rd.ReadToEnd().Contains("ñ acentos"),
                                    "UTF-8 con ñ íntegro");
                        }
                    }
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
                try { File.Delete(zipPath); } catch (Exception) { }
            }
        }

        private static void TestZefania()
        {
            string xml =
"<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
"<XMLBIBLE biblename=\"Reina Valera 1960\" type=\"x-bible\">" +
"<PROLOG>prefacio ignorado</PROLOG>" +
"<BIBLEBOOK bnumber=\"43\" bname=\"Juan\" bsname=\"Jn\">" +
"<CHAPTER cnumber=\"3\">" +
"<VERSE vnumber=\"16\">Porque de tal manera amó Dios<BR/>al mundo, que ha dado…</VERSE>" +
"<VERSE vnumber=\"17\">Porque no envió Dios a su Hijo</VERSE>" +
"</CHAPTER></BIBLEBOOK>" +
"<BIBLEBOOK bnumber=\"99\" bname=\"Inválido\"><CHAPTER cnumber=\"1\">" +
"<VERSE vnumber=\"1\">fila que debe descartarse</VERSE>" +
"</CHAPTER></BIBLEBOOK></XMLBIBLE>";

            ZefaniaResult res;
            using (MemoryStream ms = new MemoryStream(new UTF8Encoding(false).GetBytes(xml)))
            {
                res = ZefaniaBible.Parse(ms, null, "fallback");
            }
            AssertTrue(res.VersionName == "Reina Valera 1960", "biblename leído: " + res.VersionName);
            AssertTrue(res.Verses.Count == 2, "2 versículos (hay " + res.Verses.Count + ")");
            AssertTrue(res.SkippedRows == 1, "1 fila descartada (bnumber 99)");
            ZefaniaVerse v0 = res.Verses[0];
            AssertTrue(v0.Book == 43 && v0.Chapter == 3 && v0.Verse == 16, "coords 43/3/16");
            AssertTrue(v0.Text.Contains("\n"), "BR → salto de línea");
            AssertTrue(v0.Text.Contains("amó"), "texto con acento intacto");
            AssertTrue(res.BookCount == 1, "1 libro válido");
        }

        private static void TestOsis()
        {
            // v6.0.0: OSIS XML (formato académico) — streaming + tolerante.
            // Cubre: osisIDWork, <work><title>, <w>, <note> descartado, <lb/>,
            // osisID compuesto (libro/cap/vers), div type="book", fila inválida.
            string xml =
"<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
"<osis><osisText osisRefWork=\"Bible\" osisIDWork=\"KJV\">" +
"<header><work osisWork=\"KJV\"><title>King James Version</title></work></header>" +
"<div type=\"book\" osisID=\"Gen\">" +
"<chapter osisID=\"Gen.1\">" +
"<verse osisID=\"Gen.1.1\">In the beginning God <w>created</w> the heaven<lb/>and the earth.</verse>" +
"<verse osisID=\"Gen.1.2\">And the earth <note>esto es editorial</note>was without form.</verse>" +
"</chapter></div>" +
"<div type=\"book\" osisID=\"John\">" +
"<chapter osisID=\"John.3\">" +
"<verse osisID=\"John.3.16\">For God so <w>loved</w> the world.</verse>" +
"</chapter></div>" +
"<div type=\"section\" osisID=\"Otro\">" +
"<chapter osisID=\"Otro.9\"><verse osisID=\"Otro.9.9\">div que NO es libro → descartado</verse>" +
"</chapter></div>" +
"</osisText></osis>";

            OsisResult res;
            using (MemoryStream ms = new MemoryStream(new UTF8Encoding(false).GetBytes(xml)))
            {
                res = OsisBible.Parse(ms, null, "fallback");
            }
            AssertTrue(res.VersionName == "King James Version",
                "work/title gana: " + res.VersionName);
            AssertTrue(res.Verses.Count == 3, "3 versículos (hay " + res.Verses.Count + ")");
            AssertTrue(res.SkippedRows == 1, "1 fila descartada (div no-book)");
            OsisVerse v0 = res.Verses[0];
            AssertTrue(v0.Book == 1 && v0.Chapter == 1 && v0.Verse == 1, "Gen → coords 1/1/1");
            AssertTrue(v0.Text.Contains("created") && v0.Text.Contains("In the beginning"),
                "texto con <w> aplanado");
            AssertTrue(v0.Text.Contains("\n"), "lb → salto de línea");
            AssertTrue(!v0.Text.Contains("editorial"), "note descartado");
            OsisVerse v2 = res.Verses[2];
            AssertTrue(v2.Book == 43 && v2.Chapter == 3 && v2.Verse == 16, "John → 43/3/16");
            AssertTrue(res.BookCount == 2, "2 libros válidos");
            AssertTrue(res.BooksSeen.Contains("Gen") && res.BooksSeen.Contains("John"),
                "códigos vistos");
        }

        private static void TestPptxImport()
        {
            // v6.0.0: importación PPTX — round-trip con el PROPIO exportador
            // (ZipReader + sldIdLst + rels + sp/txBody/a:p/a:t).
            Theme t = new Theme();
            List<ExportSlide> slides = SampleExportSlides();
            byte[] pptx = PptxExporter.ExportToBytes("Muestra CI", slides, t);

            PptxImportResult res;
            using (MemoryStream ms = new MemoryStream(pptx))
            {
                res = PptxImporter.Import(ms);
            }
            // 3 diapositivas (la blank sin texto también — fidelidad 1:1).
            AssertTrue(res.Slides.Count == 3, "3 slides (hay " + res.Slides.Count + ")");
            // Slide 1 (Título): título + subtítulo, en orden visual.
            AssertTrue(res.Slides[0].Lines.Count == 2, "slide1: 2 líneas (hay " +
                res.Slides[0].Lines.Count + ")");
            AssertTrue(res.Slides[0].Lines[0] == "Culto de prueba", "slide1 línea 1");
            AssertTrue(res.Slides[0].Lines[1] == "Iglesia — Sala Mayor", "slide1 línea 2");
            // Slide 2 (Escritura): 2 líneas de texto + referencia al pie.
            AssertTrue(res.Slides[1].Lines.Count == 3, "slide2: 3 líneas (hay " +
                res.Slides[1].Lines.Count + ")");
            AssertTrue(res.Slides[1].Lines[0].Contains("amó"), "slide2 acento intacto");
            AssertTrue(res.Slides[1].Lines[2] == "Juan 3:16", "referencia al pie");
            // Slide 3 (blank): sin líneas.
            AssertTrue(res.Slides[2].Lines.Count == 0, "slide3 (blank) sin líneas");

            // Título de la presentación desde docProps/core.xml.
            AssertTrue(res.Title == "Muestra CI", "core title: " + res.Title);

            // ZIP dañado → excepción descriptiva (no crash mudo).
            try
            {
                byte[] junk = new byte[64];
                using (MemoryStream ms = new MemoryStream(junk))
                {
                    PptxImporter.Import(ms);
                }
                AssertTrue(false, "ZIP basura debió lanzar");
            }
            catch (InvalidDataException)
            {
                AssertTrue(true, "InvalidDataException para ZIP basura");
            }
        }

        private sealed class CountingSink : ITriggerSink
        {
            public int Executed;
            public readonly List<TriggerAction> Seen = new List<TriggerAction>();
            public string ExecuteTriggerAction(TriggerRule rule, TriggerAction action)
            {
                Executed++;
                Seen.Add(action);
                return "ok";
            }
        }

        private static void TestTriggerEngine()
        {
            TriggerEngine e = new TriggerEngine();

            TriggerRule r1 = new TriggerRule();
            r1.Id = "r1"; r1.Name = "Ofrenda"; r1.Event = "item_changed";
            r1.Match["title"] = "Ofrenda";
            TriggerAction a1 = new TriggerAction();
            a1.Type = "obs_scene";
            a1.Params["scene"] = "Culto";
            r1.Actions.Add(a1);

            TriggerRule r2 = new TriggerRule();
            r2.Id = "r2"; r2.Name = "Nota 60"; r2.Event = "midi_note";
            r2.Match["note"] = 60;
            TriggerAction a2 = new TriggerAction();
            a2.Type = "api_cmd";
            a2.Params["action"] = "next";
            r2.Actions.Add(a2);

            TriggerRule r3 = new TriggerRule();
            r3.Id = "r3"; r3.Name = "Deshabilitada"; r3.Enabled = false;
            r3.Event = "item_changed";
            r3.Actions.Add(a1);

            e.SetRules(new TriggerRule[] { r1, r2, r3 });
            CountingSink sink = new CountingSink();
            e.SetSink(sink);

            Dictionary<string, object> ctx = new Dictionary<string, object>();
            ctx["item"] = 2;
            ctx["title"] = "ofrenda";          // case-insensitive
            int fired = e.Fire("item_changed", ctx);
            AssertTrue(fired == 1, "solo r1 dispara (r3 deshabilitada)");
            AssertTrue(sink.Executed == 1, "1 acción ejecutada");
            AssertTrue(sink.Seen[0].Type == "obs_scene", "acción correcta");

            Dictionary<string, object> midi = new Dictionary<string, object>();
            midi["note"] = 60;
            fired = e.Fire("midi_note", midi);
            AssertTrue(fired == 1 && sink.Executed == 2, "r2 dispara por nota 60");

            Dictionary<string, object> midiOther = new Dictionary<string, object>();
            midiOther["note"] = 61;
            fired = e.Fire("midi_note", midiOther);
            AssertTrue(fired == 0 && sink.Executed == 2, "nota 61 no dispara");

            // Condiciones con prefijos
            Dictionary<string, object> any = new Dictionary<string, object>();
            any["title"] = "Alabanza al Señor";
            TriggerRule r4 = new TriggerRule();
            r4.Id = "r4"; r4.Event = "item_changed";
            r4.Match["title"] = "contains:alabanza";
            r4.Actions.Add(a1);
            e.SetRules(new TriggerRule[] { r4 });
            fired = e.Fire("item_changed", any);
            AssertTrue(fired == 1, "contains: insensible a mayúsculas");
            AssertTrue(TriggerEngine.ValueMatches("gte:5", 7), "gte numérico");
            AssertTrue(!TriggerEngine.ValueMatches("lte:5", 7), "lte numérico");
            AssertTrue(TriggerEngine.ValueMatches("*", "lo que sea"), "comodín");

            // Persistencia roundtrip
            e.SetRules(new TriggerRule[] { r1, r2 });
            string tmp = Path.Combine(Path.GetTempPath(), "lumina_trig_" + Guid.NewGuid().ToString("N") + ".json");
            e.Save(tmp);
            TriggerEngine loaded = TriggerEngine.Load(tmp);
            List<TriggerRule> rules = loaded.RulesSnapshot();
            AssertTrue(rules.Count == 2, "2 reglas tras roundtrip");
            AssertTrue(rules[0].Id == "r1" && rules[0].Event == "item_changed", "r1 intacta");
            AssertTrue(rules[0].Match["title"] is string && (string)rules[0].Match["title"] == "Ofrenda", "match intacto");
            AssertTrue(rules[1].Actions.Count == 1 && rules[1].Actions[0].Type == "api_cmd", "acciones intactas");
            try { File.Delete(tmp); } catch (Exception) { }
        }

        private static void TestObsProtocol()
        {
            // Handshake: Hello con auth → Identify con la cadena V5 correcta
            string pw = "s3cr3t", salt = "MTIzNDU2Nzg5", challenge = "MTIzNDU2Nzg5MDEyMzQ1Njc4";
            string expected = Convert.ToBase64String(System.Security.Cryptography.SHA256.Create()
                .ComputeHash(Encoding.UTF8.GetBytes(Convert.ToBase64String(
                    System.Security.Cryptography.SHA256.Create().ComputeHash(
                        Encoding.UTF8.GetBytes(pw + salt))) + challenge)));
            string hello = "{\"op\":0,\"d\":{\"obsWebSocketVersion\":\"5.4.2\",\"rpcVersion\":1," +
                "\"authentication\":{\"challenge\":\"" + challenge + "\",\"salt\":\"" + salt + "\"}}}";
            AssertTrue(ObsProtocol.IsHello(hello), "IsHello");
            string identify = ObsProtocol.BuildIdentify(hello, pw, 1, 0);
            Dictionary<string, object> id = MiniJson.Parse(identify);
            AssertTrue(MiniJson.GetInt(id, "op", -1) == 1, "op=1 (Identify)");
            Dictionary<string, object> d = MiniJson.GetObject(id, "d");
            string auth = MiniJson.GetString(d, "authentication", null);
            AssertTrue(auth == expected, "auth V5 = base64(sha256(base64(sha256(pw+salt))+challenge))");

            // Hello SIN auth → Identify sin authentication
            string hello2 = "{\"op\":0,\"d\":{\"obsWebSocketVersion\":\"5.4.2\",\"rpcVersion\":1}}";
            string identify2 = ObsProtocol.BuildIdentify(hello2, pw, 1, 0);
            AssertTrue(!identify2.Contains("authentication"), "sin auth cuando no se exige");

            // Request + respuesta
            string setScene = ObsProtocol.BuildSetScene("req-1", "Culto");
            Dictionary<string, object> sr = MiniJson.Parse(setScene);
            Dictionary<string, object> srd = MiniJson.GetObject(sr, "d");
            AssertTrue(MiniJson.GetInt(sr, "op", -1) == 6, "op=6 (Request)");
            AssertTrue(MiniJson.GetString(srd, "requestType", "") == "SetCurrentProgramScene", "requestType");
            AssertTrue(MiniJson.GetString(MiniJson.GetObject(srd, "requestData"), "sceneName", "") == "Culto",
                "sceneName");

            string setInput = ObsProtocol.BuildSetInputText("req-2", "Titulo", "Aleluya");
            AssertTrue(setInput.Contains("SetInputSettings") && setInput.Contains("Titulo") &&
                       setInput.Contains("Aleluya"), "SetInputSettings");

            string resp = "{\"op\":7,\"d\":{\"requestType\":\"SetCurrentProgramScene\"," +
                "\"requestId\":\"req-1\",\"requestStatus\":{\"result\":true,\"code\":100}," +
                "\"responseData\":{\"sceneName\":\"Culto\"}}}";
            ObsProtocol.ObsResponse r = ObsProtocol.ParseResponse(resp);
            AssertTrue(r != null && r.Result && r.RequestId == "req-1", "respuesta parseada");

            string evt = "{\"op\":5,\"d\":{\"eventType\":\"CurrentProgramSceneChanged\"," +
                "\"eventIntent\":64,\"eventData\":{\"sceneName\":\"Cámara 1\"}}}";
            string evType;
            Dictionary<string, object> evData;
            AssertTrue(ObsProtocol.ParseEvent(evt, out evType, out evData) &&
                       evType == "CurrentProgramSceneChanged", "evento parseado");
            AssertTrue(ObsProtocol.IsIdentified("{\"op\":2,\"d\":{\"negotiatedRpcVersion\":1}}"), "IsIdentified");
        }
    }
}
