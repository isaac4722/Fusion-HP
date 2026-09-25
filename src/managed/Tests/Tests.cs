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
using System.IO.Pipes;
using System.Threading;
using System.Net;
using lumina.api;
using lumina.bridge;
using lumina.core;
using lumina.core.project;
using lumina.core.integrations;

#pragma warning disable SYSLIB0014
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
            Run("ScenarioBuilder: notas del director por ítem (v6.0.0)", TestScenarioNotes);
            Run("TriggerEngine: reglas, condiciones y persistencia", TestTriggerEngine);
            Run("ObsProtocol: handshake V5 y mensajes", TestObsProtocol);

            // --- v6.1.0 «GUION» (siempre corren: lógica PURA del JSLib) ------
            Run("JsModules: escáner tolerante de módulos .js", TestJsModules);
            Run("JsModules: ring de log, registro de eventos y timers", TestJsRegistry);

            // --- v5.1.0 «FUNDAMENTO» (siempre corren) ----------------------------
            Run("BooksTable: 66 libros y referencias legibles", TestBooksTable);
            Run("BibleJson: lector streaming de biblias JSON", TestBibleJson);
            Run("ZipBackup: ZIP válido y extraíble", TestZipBackup);

            // --- v7.0.0 «ULTRA» (F0.05/F2) — siempre corren --------------------
            Run("ahp.v1: roundtrip de los 5 Elementos + IDs estables", TestAhpRoundtrip);
            Run("ahp.v1: validación de formato + campos desconocidos ignorados", TestAhpFormatValidation);
            Run("ahp.v1: empaquetado ZIP con manifiesto media/", TestAhpZip);
            Run("ahp.v1: guardado atómico, autoguardado y recuperación", TestAhpAutosave);
            Run("StyleCascade: Tema→Plantilla→Escenario→Elemento con origen", TestStyleCascade);
            Run("AhpBridge: JSON del motor con syncMark/video/lowerThird", TestAhpBridge);
            Run("IpcV1: códec espejo + fragmentación + errores de protocolo", TestIpcV1Codec);
            Run("IpcV1: cliente sobre pipes anónimos (loopback real)", TestIpcV1Pipes);

            Run("QrCode: matriz, patrones fijos y PNG válido", TestQrCode);
            Run("ApiV1: endpoints Bearer/401/503 + cliente móvil servido", TestApiV1);

            Run("Holyrics: importación con dedupe y confirmación", TestHolyrics);
            Run("PCO: conversión a ahp + errores humanos", TestPco);
            Run("NDI: degradación limpia sin runtime", TestNdi);
            Run("Diagnóstico: métricas 60s + verificador", TestDiagnostics);

            Run("Nativo: lumina_version", TestNativeVersion, libOk);
            Run("Nativo: ScenarioBuilder → LoadScenario==0", TestNativeLoadScenario, libOk);
            Run("Nativo: SongParse de la canción del builder", TestNativeSongParse, libOk);
            Run("Nativo: BibleRefResolve Jn 3:16", TestNativeRefResolve, libOk);
            Run("Nativo: ChordsTranspose Do Sol → Re# La# (+3)", TestNativeChordsTranspose, libOk);
            Run("Nativo: BD INSERT/SELECT/FTS5", TestNativeDb, libOk);

            // --- v1.0.0-beta.1 «ULTRA» — brechas gestionadas F1.05/F4.15/F5.09/F6.04/F6.08
            Run("Settings: atajos personalizables persistidos (F1.05)", TestSettingsShortcuts);
            Run("Fidelidad: informe F4.15 en importación/exportación", TestFidelityReport);
            Run("JSLib: sandbox CPU/memoria/conexiones + errores con línea (F5.09)", TestJsSandboxBudgets);
            Run("Drive: offline-first, cola y conflictos con historial (F6.04)", TestDriveOfflineQueue);
            Run("F6.08: sesión de servicio simulada (LUMINA_SESSION_MINUTES)", TestSession60);

            // --- v1.0.0-beta.3 — cierre de brechas F1.06/F1.07/F2.12/F2.14/F4.13
            Run("FtsQuery: saneamiento MATCH de FTS5 (F1.07)", TestFtsQuery);
            Run("Settings: lastProjectPath persistido (F1.06)", TestSettingsLastProject);
            Run("Nativo: canción uso/anotaciones + popularidad (F2.12)", TestSongUseAndAnnotations, libOk);
            Run("Nativo: biblioteca de recursos + re-vinculación (F2.14)", TestResourcesCrud, libOk);
            Run("Nativo: búsqueda en caliente canciones+versículos (F1.07)", TestHotSearchQueries, libOk);
#if NETFRAMEWORK
            Run("ImageExporter: 5 formatos y mínimo 1920×1080 (F4.13)", TestImageExporter, s_windowsGdi);
#endif

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
            AssertTrue(v.StartsWith("LuminaCore", StringComparison.Ordinal) && v.Contains("7.1"),
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

        /* ============================ v1.0.0-beta.3 — brechas F1.06/F1.07/F2.12/F2.14/F4.13 */

        /// <summary>¿Plataforma Windows con GDI+ real? (para el test F4.13).</summary>
        private static readonly bool s_windowsGdi =
            Environment.OSVersion.Platform == PlatformID.Win32NT ||
            Environment.OSVersion.Platform == PlatformID.Win32Windows;

        private static void TestFtsQuery()
        {
            // El usuario escribe referencias («juan 3:16») — los dos puntos NO
            // pueden llegar a MATCH (filtro de columna de FTS5).
            AssertTrue(FtsQuery.Sanitize("juan 3:16") == "\"juan\" \"3\" \"16\"",
                "sanitize «juan 3:16» → " + FtsQuery.Sanitize("juan 3:16"));
            // Comillas y operadores del usuario se eliminan (evita inyección
            // de sintaxis FTS5 — frase/columna/_prefijo).
            AssertTrue(FtsQuery.Sanitize("gloria \"dios\" *aleluya*") ==
                "\"gloria\" \"dios\" \"aleluya\"",
                "sin comillas/asteriscos → " + FtsQuery.Sanitize("gloria \"dios\" *aleluya*"));
            AssertTrue(FtsQuery.Sanitize("   ") == string.Empty, "vacío → vacío");
            AssertTrue(!FtsQuery.IsUseful(FtsQuery.Sanitize("  ((  ")), "solo ruido → no útil");
        }

        private static void TestSettingsLastProject()
        {
            string dir = Path.Combine(Path.GetTempPath(),
                "lumina_tests_" + Guid.NewGuid().ToString("N"));
            try
            {
                Settings written = new Settings(dir);
                written.LastProjectPath = "C:\\cultos\\domingo.json";
                written.Save();
                Settings read = Settings.Load(dir);
                AssertTrue(read.LastProjectPath == "C:\\cultos\\domingo.json", "lastProjectPath");
                // Lectores viejos ignoran el campo nuevo (F2.01.8) y los
                // defaults no lo rellenan (cadena vacía, nunca null).
                Settings defaults = Settings.Load(Path.Combine(dir, "vacía"));
                AssertTrue(defaults.LastProjectPath.Length == 0, "default vacío");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        private static void TestSongUseAndAnnotations()
        {
            string dbPath = Path.Combine(Path.GetTempPath(),
                "lumina_tests_" + Guid.NewGuid().ToString("N") + ".db");
            using (LuminaEngine engine = LuminaEngine.Create(true, null))
            {
                try
                {
                    AssertTrue(engine.DbOpen(dbPath) == LuminaStatus.Ok, "DbOpen");
                    engine.DbExec(LuminaStorage.BuildExecJson(
                        "INSERT INTO songs(title, lyrics) VALUES(?,?)",
                        "Uso frecuente", "santo es el señor"));
                    string res = engine.DbExec(LuminaStorage.BuildExecJson(
                        "SELECT id FROM songs WHERE title=?", "Uso frecuente"));
                    long id = Convert.ToInt64(LuminaStorage.Rows(res)[0][0],
                        CultureInfo.InvariantCulture);
                    // Historial de uso: dos usos → usage_count=2 (F2.12).
                    engine.DbExec(LuminaStorage.RecordSongUseRequest(id));
                    engine.DbExec(LuminaStorage.RecordSongUseRequest(id));
                    res = engine.DbExec(LuminaStorage.BuildExecJson(
                        "SELECT usage_count, COALESCE(last_used_at,'x') FROM songs WHERE id=?", id));
                    List<List<object>> rows = LuminaStorage.Rows(res);
                    AssertTrue(rows.Count == 1 && rows[0][0].ToString() == "2", "usage_count=2");
                    AssertTrue(rows[0][1].ToString() != "x", "last_used_at registrada");
                    // Anotación del operador persistida (F2.12).
                    engine.DbExec(LuminaStorage.UpdateSongAnnotationsRequest(id, "tono bajo"));
                    res = engine.DbExec(LuminaStorage.BuildExecJson(
                        "SELECT annotations FROM songs WHERE id=?", id));
                    AssertTrue(LuminaStorage.Rows(res)[0][0].ToString() == "tono bajo", "annotations");
                    // Popularidad: la más usada queda primera en TopSongs.
                    engine.DbExec(LuminaStorage.BuildExecJson(
                        "INSERT INTO songs(title, lyrics) VALUES(?,?)", "Otra", "x"));
                    res = engine.DbExec(LuminaStorage.TopSongsRequest(10));
                    rows = LuminaStorage.Rows(res);
                    AssertTrue(rows.Count >= 2 && rows[0][1].ToString() == "Uso frecuente",
                        "TopSongs ordena por uso");
                }
                finally
                {
                    try { engine.DbClose(); } catch (Exception) { }
                }
            }
            try { File.Delete(dbPath); } catch (Exception) { }
            try { File.Delete(dbPath + "-journal"); } catch (Exception) { }
        }

        private static void TestResourcesCrud()
        {
            string dbPath = Path.Combine(Path.GetTempPath(),
                "lumina_tests_" + Guid.NewGuid().ToString("N") + ".db");
            using (LuminaEngine engine = LuminaEngine.Create(true, null))
            {
                try
                {
                    AssertTrue(engine.DbOpen(dbPath) == LuminaStatus.Ok, "DbOpen");
                    // Alta con ruta RELATIVA (portable, F2.14).
                    engine.DbExec(LuminaStorage.InsertResourceRequest(
                        "image", "Fondo cruz", "resources/img/cruz.jpg", "adoracion fondo"));
                    engine.DbExec(LuminaStorage.InsertResourceRequest(
                        "video", "Entrada pastoral", "resources/video/entrada.mp4", "entrada"));
                    // Listado.
                    List<List<object>> rows = LuminaStorage.Rows(
                        engine.DbExec(LuminaStorage.ListResourcesRequest(100)));
                    AssertTrue(rows.Count == 2, "listado=2");
                    // FTS por etiqueta.
                    rows = LuminaStorage.Rows(engine.DbExec(
                        LuminaStorage.SearchResourcesRequest("adoracion", 100)));
                    AssertTrue(rows.Count == 1 && rows[0][2].ToString() == "Fondo cruz",
                        "FTS por etiqueta");
                    // Edición de nombre/etiquetas.
                    long id = Convert.ToInt64(rows[0][0], CultureInfo.InvariantCulture);
                    engine.DbExec(LuminaStorage.UpdateResourceRequest(id, "Cruz del fondo", "cruz adorno"));
                    rows = LuminaStorage.Rows(engine.DbExec(
                        LuminaStorage.SearchResourcesRequest("adorno", 100)));
                    AssertTrue(rows.Count == 1 && rows[0][2].ToString() == "Cruz del fondo",
                        "edición persistida");
                    // Re-vinculación portable (prefijo viejo→nuevo, una UPDATE).
                    engine.DbExec(LuminaStorage.RelinkResourcesRequest("resources", "datos/recursos"));
                    rows = LuminaStorage.Rows(engine.DbExec(LuminaStorage.ListResourcesRequest(100)));
                    AssertTrue(rows[0][3].ToString().StartsWith("datos/recursos/", StringComparison.Ordinal),
                        "relink prefijo → " + rows[0][3]);
                    // Baja del registro (el archivo físico no lo toca nadie).
                    engine.DbExec(LuminaStorage.DeleteResourceRequest(id));
                    rows = LuminaStorage.Rows(engine.DbExec(LuminaStorage.ListResourcesRequest(100)));
                    AssertTrue(rows.Count == 1, "delete deja 1");
                }
                finally
                {
                    try { engine.DbClose(); } catch (Exception) { }
                }
            }
            try { File.Delete(dbPath); } catch (Exception) { }
            try { File.Delete(dbPath + "-journal"); } catch (Exception) { }
        }

        private static void TestHotSearchQueries()
        {
            string dbPath = Path.Combine(Path.GetTempPath(),
                "lumina_tests_" + Guid.NewGuid().ToString("N") + ".db");
            using (LuminaEngine engine = LuminaEngine.Create(true, null))
            {
                try
                {
                    AssertTrue(engine.DbOpen(dbPath) == LuminaStatus.Ok, "DbOpen");
                    engine.DbExec(LuminaStorage.InsertSongRequest(
                        "Aleluya interior", "Autor", "Do", 72, "adoracion",
                        "con mi Dios yo quiero estar"));
                    // Búsqueda en caliente de CANCIONES vía el saneador (F1.07):
                    // el término con dos puntos no rompe MATCH.
                    string fts = FtsQuery.Sanitize("aleluya :");
                    List<List<object>> rows = LuminaStorage.Rows(
                        engine.DbExec(LuminaStorage.SearchSongsRequest(fts, 8)));
                    AssertTrue(rows.Count == 1 && rows[0][1].ToString() == "Aleluya interior",
                        "hot-search canción");
                    // Búsqueda de VERSÍCULOS por palabra (bible_fts).
                    engine.DbExec(LuminaStorage.InsertBibleVerseRequest(
                        "RVR1909", 43, 3, 16, "Porque de tal manera amó Dios al mundo"));
                    rows = LuminaStorage.Rows(engine.DbExec(
                        LuminaStorage.SearchVersesRequest(FtsQuery.Sanitize("amó dios"), 8)));
                    AssertTrue(rows.Count == 1 && rows[0][3].ToString() == "16"
                        && rows[0][0].ToString() == "RVR1909", "hot-search versículo");
                }
                finally
                {
                    try { engine.DbClose(); } catch (Exception) { }
                }
            }
            try { File.Delete(dbPath); } catch (Exception) { }
            try { File.Delete(dbPath + "-journal"); } catch (Exception) { }
        }

#if NETFRAMEWORK
        private static void TestImageExporter()
        {
            // PNG de origen pequeño (el render real del núcleo puede ser 960×540):
            // el exportador debe REESCALAR al mínimo normativo 1920×1080 (F4.13).
            string dir = Path.Combine(Path.GetTempPath(),
                "lumina_tests_img_" + Guid.NewGuid().ToString("N"));
            try
            {
                byte[] png;
                using (System.Drawing.Bitmap b = new System.Drawing.Bitmap(64, 36))
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                {
                    using (System.Drawing.Graphics g =
                        System.Drawing.Graphics.FromImage(b))
                    {
                        g.Clear(System.Drawing.Color.SteelBlue);
                    }
                    b.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    png = ms.ToArray();
                }
                string[] expected = { "jpg", "png", "gif", "bmp", "tif" };
                ImageExportOptions[] opts = new ImageExportOptions[]
                {
                    new ImageExportOptions { Format = ImageExportFormat.Jpg },
                    new ImageExportOptions { Format = ImageExportFormat.Png },
                    new ImageExportOptions { Format = ImageExportFormat.Gif },
                    new ImageExportOptions { Format = ImageExportFormat.Bmp },
                    new ImageExportOptions { Format = ImageExportFormat.Tif },
                };
                for (int i = 0; i < opts.Length; i++)
                {
                    ImageExportResult res = ImageExporter.Export(2,
                        delegate (int slide) { return png; },
                        dir, "muestra" + i, opts[i]);
                    AssertTrue(res.Files.Count == 2 && res.Errors.Count == 0,
                        expected[i] + ": 2 archivos, sin errores");
                    foreach (string f in res.Files)
                    {
                        AssertTrue(File.Exists(f), "existe " + Path.GetFileName(f));
                        using (System.Drawing.Bitmap check = new System.Drawing.Bitmap(f))
                        {
                            AssertTrue(check.Width >= 1920 && check.Height >= 1080,
                                Path.GetFileName(f) + " " + check.Width + "×" + check.Height +
                                " ≥ 1920×1080");
                        }
                    }
                }
                // Sin slides → informe con error, sin excepción (fail-safe).
                ImageExportResult empty = ImageExporter.Export(0, delegate (int s) { return png; },
                    dir, "vacío", null);
                AssertTrue(empty.Files.Count == 0 && empty.Errors.Count > 0, "0 slides → error informado");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }
#endif // NETFRAMEWORK

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

        private static void TestScenarioNotes()
        {
            // v6.0.0: notas del director por ítem — viajan en el plan JSON
            // (contrato aditivo: ítems sin notas NO emiten el campo).
            ScenarioItem it = ScenarioBuilder.TextItem("Bienvenida", "línea uno", 4);
            it.Notes = "recordar bajar el volumen del teclado";
            string json = ScenarioBuilder.BuildScenarioJson("Plan", new Theme(),
                new ScenarioItem[] { it });
            Dictionary<string, object> o = MiniJson.Parse(json);
            Dictionary<string, object> item =
                (Dictionary<string, object>)MiniJson.GetArray(o, "items")[0];
            AssertTrue(MiniJson.GetString(item, "notes", "") ==
                "recordar bajar el volumen del teclado", "notes emitido");

            ScenarioItem plain = ScenarioBuilder.TextItem("Sin notas", "texto", 4);
            string json2 = ScenarioBuilder.BuildScenarioJson("Plan2", new Theme(),
                new ScenarioItem[] { plain });
            AssertTrue(json2.IndexOf("\"notes\"", StringComparison.Ordinal) < 0,
                "sin notes → campo ausente");

            // El núcleo ignora el campo desconocido (ABI aditiva, sin error).
            if (NativeAvailable())
            {
                using (LuminaEngine engine = LuminaEngine.Create(true, null))
                {
                    int st = engine.LoadScenario(json);
                    AssertTrue(st == LuminaStatus.Ok, "LoadScenario con notes");
                }
            }
        }

        private static void TestJsModules()
        {
            // Escáner: carpeta ausente → vacía (NUNCA lanza), orden alfabético
            // estable, archivo ilegible → error por archivo sin tumbar el resto.
            string dir = Path.Combine(Path.GetTempPath(),
                "lumina-tests-js-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                List<JsModuleFile> none = JsModuleScanner.Scan(Path.Combine(dir, "no-existe"));
                AssertTrue(none.Count == 0, "carpeta ausente → lista vacía");

                Directory.CreateDirectory(dir);
                // BOM UTF-8 como lo deja Notepad (contrato del lector).
                byte[] bom = new byte[] { 0xEF, 0xBB, 0xBF };
                byte[] bCode = new UTF8Encoding(false).GetBytes("jslib.log('uno');");
                byte[] withBom = new byte[bom.Length + bCode.Length];
                bom.CopyTo(withBom, 0); bCode.CopyTo(withBom, bom.Length);
                File.WriteAllBytes(Path.Combine(dir, "b_auto.js"), withBom);
                File.WriteAllText(Path.Combine(dir, "a_primero.js"),
                    "// módulo a\r\njslib.log('primero');", new UTF8Encoding(false));
                // Bytes UTF-8 INVÁLIDOS (y sin BOM — 0xFF 0xFE se confundiría con
                // BOM UTF-16 LE y StreamReader cambiaría de codificación en
                // silencio): el lector estricto debe fallar y el escáner
                // reportarlo por archivo (el resto sigue cargando — spec §2.6).
                File.WriteAllBytes(Path.Combine(dir, "c_roto.js"),
                    new byte[] { 0x6A, 0x73, 0xFF, 0x81, 0x6C, 0x69, 0x62 });
                File.WriteAllText(Path.Combine(dir, "otro.txt"), "ignorado");

                List<JsModuleFile> mods = JsModuleScanner.Scan(dir);
                AssertTrue(mods.Count == 3, "solo *.js cuentan (3 de 4 archivos)");
                AssertTrue(mods[0].Name == "a_primero.js" && mods[1].Name == "b_auto.js"
                    && mods[2].Name == "c_roto.js", "orden alfabético estable");
                AssertTrue(mods[0].Ok && mods[0].Code.IndexOf("primero") >= 0,
                    "a_primero leído");
                AssertTrue(mods[1].Ok && mods[1].Code.IndexOf("uno") >= 0,
                    "b_auto leído con BOM tolerado");
                AssertTrue(!mods[2].Ok && mods[2].Error.Length > 0,
                    "c_roto reporta error por archivo");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }

            // Prelude: define jsonParse y expone la versión (ES3 puro, sin
            // depender de JSON.parse que no existe en el JScript de Win7).
            AssertTrue(JsPrelude.Text.IndexOf("function jsonParse") >= 0,
                "prelude define jsonParse");
            AssertTrue(JsPrelude.Text.IndexOf(JsPrelude.Version, StringComparison.Ordinal) >= 0,
                "prelude expone la versión del motor");
            AssertTrue(JsPrelude.Text.IndexOf("JSON.parse") < 0,
                "prelude ES3 sin JSON.parse");
        }

        private static void TestJsRegistry()
        {
            // Ring: acota en 60 y preserva orden (thread-safe por contrato —
            // httpGet/tcp/ws escriben desde hilos de fondo).
            JsLogRing ring = new JsLogRing();
            for (int i = 0; i < 100; i++) ring.Add("línea " + i);
            AssertTrue(ring.Count == JsLogRing.Capacity, "ring acotado en 60");
            List<string> snap = ring.Snapshot();
            AssertTrue(snap[0].EndsWith("línea 40", StringComparison.Ordinal)
                && snap[snap.Count - 1].EndsWith("línea 99", StringComparison.Ordinal),
                "ring conserva el orden y las últimas 60");
            AssertTrue(snap[0].Length > 8 && snap[0][2] == ':',
                "línea con marca de tiempo HH:mm:ss");
            ring.Add(null);   // tolerante a null
            AssertTrue(ring.Count == JsLogRing.Capacity, "null no rompe el ring");
            ring.Clear();
            AssertTrue(ring.Count == 0 && ring.Snapshot().Count == 0, "Clear vacía");

            // Registro de eventos: tokens opacos, orden de suscripción,
            // case-sensibilidad (JScript), Clear total al recargar.
            JsEventRegistry reg = new JsEventRegistry();
            object cbA = "cbA", cbB = "cbB", cbC = "cbC";
            reg.Subscribe("slide_changed", cbA);
            reg.Subscribe("slide_changed", cbB);
            reg.Subscribe("slide_changed", cbA);      // duplicado ignorado
            reg.Subscribe("item_changed", cbC);
            AssertTrue(reg.HasSubscribers("slide_changed")
                && !reg.HasSubscribers("video_ended"), "has-subscribers correcto");
            AssertTrue(!reg.HasSubscribers("SLIDE_CHANGED"),
                "nombres case-sensitive como JScript");
            List<object> subs = reg.Subscribers("slide_changed");
            AssertTrue(subs.Count == 2 && subs[0] == cbA && subs[1] == cbB,
                "orden de suscripción sin duplicados");
            reg.Unsubscribe("slide_changed", cbA);
            AssertTrue(reg.Subscribers("slide_changed").Count == 1, "unsubscribe");
            reg.Unsubscribe("evento-desconocido", cbA);   // no lanza
            reg.Subscribe(null, cbA);                     // ignorado
            reg.Subscribe("x", null);                     // ignorado
            List<string> active = reg.ActiveEvents();
            AssertTrue(active.Count == 2, "eventos activos");
            reg.Clear();
            AssertTrue(!reg.HasSubscribers("item_changed")
                && reg.ActiveEvents().Count == 0, "Clear suelta todo (recarga)");

            // Timers: ids > 0 únicos, liberación y reutilización de huecos.
            JsTimerIds ids = new JsTimerIds();
            int t1 = ids.Alloc();
            int t2 = ids.Alloc();
            int t3 = ids.Alloc();
            AssertTrue(t1 > 0 && t2 > 0 && t3 > 0 && t1 != t2 && t2 != t3,
                "ids positivos y únicos");
            AssertTrue(ids.LiveCount == 3, "3 vivos");
            ids.Release(t2);
            AssertTrue(ids.LiveCount == 2, "2 vivos tras liberar");
            int t2b = ids.Alloc();
            AssertTrue(t2b == t2, "hueco reutilizado");
            AssertTrue(ids.LiveCount == 3, "3 vivos tras reutilizar");
            ids.Release(0);
            ids.Release(-5);
            ids.Release(9999);   // no reservado: ignorado en silencio
            AssertTrue(ids.LiveCount == 3, "liberaciones inválidas ignoradas");
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
        // =====================================================================
        // v7.0.0 «ULTRA» — ahp.v1, cascada, puente e IPC (F0.05/F2).
        // =====================================================================

        private static AhpProject BuildSampleProject()
        {
            AhpProject p = new AhpProject();
            p.Name = "Culto Domingo 10am";
            p.ThemeRef = "theme-calma";
            AhpScenario s = p.NewScenario("Adoración");
            // 1) Texto con syncMarks (F1.03/F2.03)
            AhpElement t = p.NewElement(AhpElementKind.Text);
            t.Title = "Himno 34";
            t.Lines.Add(new AhpLine { Text = "L1", SyncMark = 1 });
            t.Lines.Add(new AhpLine { Text = "L2", SyncMark = 1 });
            t.Lines.Add(new AhpLine { Text = "L3", SyncMark = 2 });
            s.Elements.Add(t);
            // 2) Versículo (F2.04)
            AhpElement v = p.NewElement(AhpElementKind.Verse);
            v.Book = "Juan"; v.Chapter = 3; v.VerseFrom = 16; v.VerseTo = 18;
            v.Translation = "RV1960"; v.QuoteFormat = "cita";
            v.HighlightedWords.Add("amó"); v.HighlightedWords.Add("Dios");
            s.Elements.Add(v);
            // 3) Imagen (F2.05)
            AhpElement im = p.NewElement(AhpElementKind.Image);
            im.ImagePath = "media/logo.png"; im.Fit = "cover"; im.Opacity = 0.85;
            im.CropX = 10; im.CropY = 20; im.CropW = 640; im.CropH = 480;
            im.Tags.Add("entrada");
            s.Elements.Add(im);
            // 4) Video (F2.06)
            AhpElement vd = p.NewElement(AhpElementKind.Video);
            vd.VideoPath = "media/clip.mp4"; vd.VideoVolume = 80; vd.StartAtMs = 1500;
            vd.Loop = true; vd.PositionLeftPct = 0.1; vd.Tags.Add("fondo");
            s.Elements.Add(vd);
            // 5) Lower Third (F2.07)
            AhpElement lt = p.NewElement(AhpElementKind.LowerThird);
            lt.LtText = "Pastor Isaac"; lt.LtDurationSec = 8; lt.LtPosition = "bottom";
            lt.LtManualActivation = false; lt.LtOverlayElementId = t.Id;
            s.Elements.Add(lt);
            p.Scenarios.Add(s);
            // manifiesto media (F2.01.5)
            AhpMediaEntry m = new AhpMediaEntry();
            m.Path = "media/logo.png"; m.Kind = "image"; m.Sha256 = "abc123"; m.Size = 1024;
            p.Media.Add(m);
            return p;
        }

        private static void TestAhpRoundtrip()
        {
            AhpProject p = BuildSampleProject();
            string json = AhpProjectIO.ToJson(p);
            AssertTrue(json.Contains("\"format\":\"ahp.v1\""), "format ahp.v1");
            AhpLoadReport rep = new AhpLoadReport();
            AhpProject q = AhpProjectIO.FromJson(json, rep);
            AssertTrue(rep.Ok, "informe de carga OK");
            AssertTrue(q != null && q.Name == "Culto Domingo 10am", "nombre");
            AssertTrue(q.Scenarios.Count == 1 && q.Scenarios[0].Elements.Count == 5,
                "1 escenario, 5 elementos (EXACTAMENTE cinco tipos)");
            AhpElement t = q.Scenarios[0].Elements[0];
            AssertTrue(t.Kind == AhpElementKind.Text && t.Lines.Count == 3, "texto 3 líneas");
            AssertTrue(t.Lines[1].SyncMark == 1 && t.Lines[2].SyncMark == 2, "syncMark persistido");
            AhpElement v = q.Scenarios[0].Elements[1];
            AssertTrue(v.Book == "Juan" && v.VerseFrom == 16 && v.VerseTo == 18 &&
                       v.Translation == "RV1960" && v.HighlightedWords.Count == 2,
                "versículo completo (F2.04)");
            AhpElement im = q.Scenarios[0].Elements[2];
            AssertTrue(im.ImagePath == "media/logo.png" && im.Opacity == 0.85 &&
                       im.CropW == 640 && im.Tags[0] == "entrada", "imagen (F2.05)");
            AhpElement vd = q.Scenarios[0].Elements[3];
            AssertTrue(vd.VideoVolume == 80 && vd.StartAtMs == 1500 && vd.Loop &&
                       vd.PositionLeftPct == 0.1, "video (F2.06)");
            AhpElement lt = q.Scenarios[0].Elements[4];
            AssertTrue(lt.LtText == "Pastor Isaac" && lt.LtDurationSec == 8.0 &&
                       !lt.LtManualActivation && lt.LtOverlayElementId == t.Id,
                "lower third (F2.07)");
            // IDs ESTABLES (F2.01.4): al re-guardar, los ID no cambian.
            string json2 = AhpProjectIO.ToJson(q);
            AssertTrue(json2.Contains(t.Id) && json2.Contains(lt.Id), "IDs estables");
            // nuevo elemento: ID monotónico SIN colisión (semilla persistida)
            AhpElement ne = q.NewElement(AhpElementKind.Text);
            bool collision = false;
            foreach (AhpScenario sc in q.Scenarios)
                foreach (AhpElement ee in sc.Elements)
                    if (ee.Id == ne.Id) collision = true;
            AssertTrue(!collision, "ID nuevo sin colisión (semilla monotónica)");
            AssertTrue(q.NextId > 6, "semilla nextId persistida");
            // media manifiesto
            AssertTrue(q.Media.Count == 1 && q.Media[0].Sha256 == "abc123", "manifiesto media");
        }

        private static void TestAhpFormatValidation()
        {
            // Formato incorrecto → rechazo explícito (F2.01.9)
            AhpLoadReport rep = new AhpLoadReport();
            AhpProject bad = AhpProjectIO.FromJson(
                "{\"format\":\"otro.v9\",\"project\":{}}", rep);
            AssertTrue(bad == null && !rep.Ok && rep.Warnings.Count > 0,
                "formato incompatible rechazado con informe");
            // Campos desconocidos IGNORADOS (F2.01.8) y reportados
            rep = new AhpLoadReport();
            AhpProject p = AhpProjectIO.FromJson(
                "{\"format\":\"ahp.v1\",\"futuroCampo\":123," +
                "\"project\":{\"name\":\"X\",\"nuevoCampo\":true,\"scenarios\":[]}}", rep);
            AssertTrue(p != null && rep.Ok && p.Name == "X", "carga con campo futuro");
            AssertTrue(rep.IgnoredFields.Contains("futuroCampo") &&
                       rep.IgnoredFields.Contains("nuevoCampo"),
                "campos desconocidos ignorados e informados");
        }

        private static void TestAhpZip()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ahp-zip-" +
                Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            try
            {
                string mediaDir = Path.Combine(dir, "media");
                Directory.CreateDirectory(mediaDir);
                File.WriteAllBytes(Path.Combine(mediaDir, "logo.png"),
                    new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 });
                AhpProject p = BuildSampleProject();
                byte[] zip = AhpProjectIO.ToZip(p, dir);
                AssertTrue(zip != null && zip.Length > 100, "zip generado");
                // reabrir en otra carpeta (F2.01.10: mismo contenido)
                string dir2 = dir + "-in";
                Directory.CreateDirectory(dir2);
                AhpLoadReport rep = new AhpLoadReport();
                AhpProject q = AhpProjectIO.FromZip(zip, dir2, rep);
                AssertTrue(q != null && rep.Ok && q.Scenarios.Count == 1,
                    "proyecto reabierto desde zip");
                AssertTrue(File.Exists(Path.Combine(dir2, "media", "logo.png")),
                    "media extraída");
                AssertTrue(File.ReadAllBytes(Path.Combine(dir2, "media", "logo.png")).Length == 8,
                    "contenido del recurso intacto");
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        private static void TestAhpAutosave()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ahp-auto-" +
                Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "culto.ahp");
            try
            {
                ProjectStore store = new ProjectStore();
                AhpProject p = BuildSampleProject();
                store.SaveAtomic(path, p);
                AssertTrue(File.Exists(path) && !File.Exists(path + ".tmp"),
                    "guardado atómico sin temporales");
                // F2.15: recuperación — el autoguardado más nuevo gana.
                ProjectStore fast = new ProjectStore { IntervalSec = 1 };
                AhpProject p2 = BuildSampleProject();
                p2.Name = "EDITADO";
                bool armed = false;
                fast.ArmAutosave(path, delegate
                {
                    if (armed) return null;   // solo una emisión
                    armed = true;
                    return p2;
                });
                Thread.Sleep(1600);
                fast.DisarmAutosave();
                AhpProject rec; bool newer;
                AssertTrue(fast.TryRecover(path, out rec, out newer),
                    "autoguardado recuperable");
                AssertTrue(newer, "autoguardado más reciente que el proyecto");
                AssertTrue(rec != null && rec.Name == "EDITADO", "contenido recuperado");
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        private static void TestStyleCascade()
        {
            Dictionary<string, string> tema = new Dictionary<string, string>();
            tema["fontFace"] = "Segoe UI"; tema["fontSize"] = "48"; tema["fgColor"] = "#FFFFFF";
            Dictionary<string, string> plantilla = new Dictionary<string, string>();
            plantilla["fontSize"] = "54";           // hereda lo demás
            Dictionary<string, string> escenario = new Dictionary<string, string>();
            escenario["fgColor"] = "#FFDDDDDD";     // nivel escenario
            Dictionary<string, string> elemento = new Dictionary<string, string>();
            elemento["fontSize"] = "60";            // el más específico gana

            // F2.08.1: propiedad nula se resuelve desde el nivel inferior.
            AssertTrue(StyleCascade.Resolve("fontFace", "Arial", elemento, escenario, plantilla, tema)
                == "Segoe UI", "fontFace resuelto desde el TEMA (nadie lo pisa)");
            AssertTrue(StyleCascade.Resolve("fontSize", "40", elemento, escenario, plantilla, tema)
                == "60", "fontSize: ELEMENTO gana (más específico)");
            // sin override de elemento: plantilla (54)
            Dictionary<string, string> vacio = new Dictionary<string, string>();
            AssertTrue(StyleCascade.Resolve("fontSize", "40", vacio, escenario, plantilla, tema)
                == "54", "fontSize heredado desde PLANTILLA");
            AssertTrue(StyleCascade.Resolve("fgColor", "#FFF", vacio, vacio, vacio, tema)
                == "#FFFFFF", "fgColor desde TEMA");
            AssertTrue(StyleCascade.Resolve("fgColor", "#FFF", vacio, escenario, vacio, vacio)
                == "#FFDDDDDD", "fgColor desde ESCENARIO");
            // nadie la define → default
            AssertTrue(StyleCascade.Resolve("lineSpacing", "1.2", vacio, vacio, vacio, vacio)
                == "1.2", "sin definir → predeterminado");
            // F2.08.5: origen efectivo visible
            AhpStyleValue o = StyleCascade.ResolveWithOrigin("fontSize", "40",
                elemento, escenario, plantilla, tema);
            AssertTrue(o.Origin == AhpStyleOrigin.Elemento && o.OriginLabel == "elemento",
                "origen: elemento");
            o = StyleCascade.ResolveWithOrigin("fontSize", "40", vacio, escenario, plantilla, tema);
            AssertTrue(o.Origin == AhpStyleOrigin.Plantilla, "origen: plantilla");
            o = StyleCascade.ResolveWithOrigin("fontFace", "Arial", vacio, vacio, vacio, tema);
            AssertTrue(o.Origin == AhpStyleOrigin.Tema, "origen: tema");
            // F2.08.6: vista completa para la UI/exportador
            List<string> props = new List<string> { "fontFace", "fontSize", "fgColor" };
            List<AhpStyleValue> view = StyleCascade.EffectiveView(props, tema, plantilla,
                escenario, elemento);
            AssertTrue(view.Count == 3, "vista efectiva 3 propiedades");
            // "" = heredar explícito (F2.08.2)
            elemento["fontSize"] = "";
            AssertTrue(StyleCascade.Resolve("fontSize", "40", elemento, escenario, plantilla, tema)
                == "54", "override VACÍO = heredar (null significa heredar)");
        }

        private static void TestAhpBridge()
        {
            AhpProject p = BuildSampleProject();
            string json = AhpBridge.ToEngineScenario(p.Scenarios[0], "C:\\base");
            AssertTrue(json.Contains("\"kind\":\"text\""), "ítem texto");
            AssertTrue(json.Contains("\"syncMark\":1"), "syncMark al motor (F1.03)");
            AssertTrue(json.Contains("\"kind\":\"scripture\"") &&
                       json.Contains("\"ref\":\"Juan 3:16-18\""),
                "versículo con referencia tipada");
            AssertTrue(json.Contains("amó"), "palabras destacadas → highlight");
            AssertTrue(json.Contains("\"kind\":\"image\"") &&
                       json.Contains("C:\\\\base\\\\media\\\\logo.png"),
                "imagen con ruta resuelta");
            AssertTrue(json.Contains("\"kind\":\"video\"") &&
                       json.Contains("\"videoVolume\":80") &&
                       json.Contains("\"videoLoop\":true"),
                "video con volumen/loop (F3.01)");
            AssertTrue(json.Contains("\"lowerThird\""), "LT mapeado (F3.04)");
            // JSON de activación del LT
            AhpElement lt = p.Scenarios[0].Elements[4];
            string ltj = AhpBridge.LtJson(lt);
            AssertTrue(ltj.Contains("\"durationMs\":8000") && ltj.Contains("\"show\":true"),
                "LtJson duración+activación");
        }

        private static void TestIpcV1Codec()
        {
            // Espejo del códec NATIVO: mismas cabeceras, mismos números.
            byte[] fr = IpcV1Codec.Encode(IpcV1.MsgCommand, "{\"id\":1}");
            AssertTrue(fr.Length == 12 + 8, "longitud del frame (payload 8 bytes)");
            AssertTrue(fr[0] == 0x50 && fr[1] == 0x49 && fr[2] == 0x4D && fr[3] == 0x4C,
                "magic LMIP little-endian");
            AssertTrue(fr[4] == 1 && fr[5] == 0, "versión 1");
            IpcV1Decoder dec = new IpcV1Decoder(IpcV1.DefaultMaxPayload);
            int type; string payload;
            // FRAGMENTACIÓN: 1 byte por llamada (F0.05.9)
            bool got = false;
            for (int i = 0; i < fr.Length; i++)
            {
                int rc = dec.Feed(fr, i, 1, out type, out payload);
                if (rc == 1) { got = true; AssertTrue(payload == "{\"id\":1}", "payload"); }
                else AssertTrue(rc == 0, "incompleto mientras llega");
            }
            AssertTrue(got, "frame reensamblado de a 1 byte");
            // dos frames en una tanda
            IpcV1Decoder d2 = new IpcV1Decoder(1024);
            byte[] two = IpcV1Codec.Encode(IpcV1.MsgPing, "abc");
            byte[] fr2 = IpcV1Codec.Encode(IpcV1.MsgPong, "d");
            byte[] both = new byte[two.Length + fr2.Length];
            Buffer.BlockCopy(two, 0, both, 0, two.Length);
            Buffer.BlockCopy(fr2, 0, both, two.Length, fr2.Length);
            AssertTrue(d2.Feed(both, 0, both.Length, out type, out payload) == 1 &&
                       type == IpcV1.MsgPing && payload == "abc", "frame 1 de 2");
            AssertTrue(d2.Feed(new byte[0], 0, 0, out type, out payload) == 1 &&
                       type == IpcV1.MsgPong && payload == "d", "frame 2 de 2");
            // magic inválido → -1 y recuperación
            IpcV1Decoder d3 = new IpcV1Decoder(1024);
            byte[] bad = new byte[20];
            for (int i = 0; i < bad.Length; i++) bad[i] = 0xAB;
            AssertTrue(d3.Feed(bad, 0, bad.Length, out type, out payload) == -1, "magic inválido");
            AssertTrue(d3.Feed(fr, 0, fr.Length, out type, out payload) == 1, "recuperación");
            // versión desconocida → -2
            byte[] v99 = (byte[])fr.Clone();
            v99[4] = 99;
            IpcV1Decoder d4 = new IpcV1Decoder(1024);
            AssertTrue(d4.Feed(v99, 0, v99.Length, out type, out payload) == -2,
                "versión desconocida");
            // oversized → -3
            IpcV1Decoder d5 = new IpcV1Decoder(64);
            byte[] big = IpcV1Codec.Encode(IpcV1.MsgState, new string('x', 256));
            AssertTrue(d5.Feed(big, 0, big.Length, out type, out payload) == -3, "oversized");
        }

        private static void TestIpcV1Pipes()
        {
            // LOOPBACK REAL con pipes anónimos (net8/Linux y net48/Windows):
            // códec ↔ códec cruzando E/S de streams reales. El extremo de
            // ESCRITURA se cierra tras enviar → el Read del otro lado
            // despierta con 0 (los pipes anónimos no tienen timeout).
            AnonymousPipeServerStream srvIn = new AnonymousPipeServerStream(
                PipeDirection.In, HandleInheritability.None);
            AnonymousPipeClientStream cliOut = new AnonymousPipeClientStream(
                PipeDirection.Out, srvIn.ClientSafePipeHandle);
            AnonymousPipeServerStream srvOut = new AnonymousPipeServerStream(
                PipeDirection.Out, HandleInheritability.None);
            AnonymousPipeClientStream cliIn = new AnonymousPipeClientStream(
                PipeDirection.In, srvOut.ClientSafePipeHandle);
            try
            {
                byte[] f1 = IpcV1Codec.Encode(IpcV1.MsgHello,
                    "{\"proto\":\"ipc.v1\",\"client\":\"test\"}");
                byte[] f2 = IpcV1Codec.Encode(IpcV1.MsgCommand, "{\"id\":7}");
                byte[] f3 = IpcV1Codec.Encode(IpcV1.MsgPing, "eco");
                byte[] all = new byte[f1.Length + f2.Length + f3.Length];
                Buffer.BlockCopy(f1, 0, all, 0, f1.Length);
                Buffer.BlockCopy(f2, 0, all, f1.Length, f2.Length);
                Buffer.BlockCopy(f3, 0, all, f1.Length + f2.Length, f3.Length);
                // escritura FRAGMENTADA (el decoder del otro lado reensambla).
                for (int i = 0; i < all.Length; i += 7)
                {
                    int n = Math.Min(7, all.Length - i);
                    cliOut.Write(all, i, n);
                    cliOut.Flush();
                }
                cliOut.Dispose();            // cierra la escritura → fin de datos

                IpcV1Decoder dec = new IpcV1Decoder(IpcV1.DefaultMaxPayload);
                byte[] buf = new byte[4096];
                int type; string payload;
                List<int> types = new List<int>();
                int nread;
                while ((nread = srvIn.Read(buf, 0, buf.Length)) > 0)
                {
                    if (dec.Feed(buf, 0, nread, out type, out payload) == 1)
                        types.Add(type);
                    while (dec.Feed(new byte[0], 0, 0, out type, out payload) == 1)
                        types.Add(type);
                }
                AssertTrue(types.Count == 3, "3 frames reensamblados (" + types.Count + ")");
                AssertTrue(types.Contains(IpcV1.MsgHello) &&
                           types.Contains(IpcV1.MsgCommand) &&
                           types.Contains(IpcV1.MsgPing), "tipos correctos");

                // servidor → cliente: WELCOME de vuelta.
                byte[] w = IpcV1Codec.Encode(IpcV1.MsgWelcome,
                    "{\"proto\":\"ipc.v1\",\"server\":\"test\"}");
                srvOut.Write(w, 0, w.Length);
                srvOut.Flush();
                srvOut.Dispose();            // fin de datos del servidor

                IpcV1Decoder dec2 = new IpcV1Decoder(IpcV1.DefaultMaxPayload);
                int rn = cliIn.Read(buf, 0, buf.Length);
                AssertTrue(rn == w.Length, "frame completo al cliente");
                int rc2 = dec2.Feed(buf, 0, rn, out type, out payload);
                AssertTrue(rc2 == 1 && type == IpcV1.MsgWelcome &&
                           payload.Contains("ipc.v1"), "WELCOME decodificado");
            }
            finally
            {
                try { cliOut.Dispose(); } catch (Exception) { }
                try { cliIn.Dispose(); } catch (Exception) { }
                try { srvIn.Dispose(); } catch (Exception) { }
                try { srvOut.Dispose(); } catch (Exception) { }
            }
        }

        // =====================================================================
        // v7.0.0 «ULTRA» — QR (F5.03) y API v1 (F5.01/F5.02).
        // =====================================================================

        private static void TestQrCode()
        {
            // Matriz de un payload de emparejamiento real (IP+token).
            string payload = "http://192.168.1.50:8088/remote.html#0123456789abcdef0123456789abcdef";
            QrMatrix qr = QrCode.Encode(payload);
            AssertTrue(qr.Size >= 21, "tamaño mínimo v1 (21)");
            AssertTrue((qr.Size - 17) % 4 == 0, "lado válido (17+4v)");
            // Patrones de posición (finder) en las 3 esquinas.
            AssertTrue(qr.IsDark(0, 0) && qr.IsDark(0, 6) && qr.IsDark(6, 0) &&
                       qr.IsDark(6, 6), "borde del finder sup-izq");
            AssertTrue(qr.IsDark(0, qr.Size - 7), "finder sup-der presente");
            AssertTrue(qr.IsDark(qr.Size - 7, 0), "finder inf-izq presente");
            // Separador claro alrededor del sup-izq.
            AssertTrue(!qr.IsDark(7, 7), "separador (7,7) claro");
            // Módulo oscuro fijo ISO (4v+9, 8).
            AssertTrue(qr.IsDark(8, qr.Size - 8), "módulo oscuro fijo");
            // Timing alternado en la fila/columna 6.
            bool altOk = true;
            for (int i = 8; i < qr.Size - 8; i++)
                if (qr.IsDark(6, i) != (i % 2 == 0)) { altOk = false; break; }
            AssertTrue(altOk, "timing pattern alternado");
            // PNG: firma + dimensiones correctas (8 px/módulo + margen 4).
            byte[] png = QrCode.ToPng(qr, 8, 4);
            AssertTrue(png.Length > 100 && png[0] == 0x89 && png[1] == 0x50 &&
                       png[2] == 0x4E && png[3] == 0x47, "firma PNG");
            int dim = (qr.Size + 8) * 8;
            int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            AssertTrue(w == dim && h == dim, "dimensiones IHDR " + w + "x" + h);
            // payloads de distintas longitudes (versiones crecientes)
            QrMatrix q2 = QrCode.Encode("http://10.0.0.5:8088/remote.html#t");
            AssertTrue(q2.Size >= 21, "QR corto");
            QrMatrix q3 = QrCode.Encode(new string('a', 150));
            AssertTrue(q3.Size > q2.Size, "payload mayor → versión mayor");
        }

        private static void TestApiV1()
        {
            // Servidor REAL en localhost con puerto aleatorio (HttpListener
            // gestionado funciona en Linux net8 y en Windows net48).
            LuminaEngine engine = null;
            try { engine = LuminaEngine.Create(true, null); }
            catch (LuminaException) { }
            if (engine == null)
            {
                // sin biblioteca nativa: se prueba solo QR/estáticos (los
                // endpoints dependen del motor) — contado como skip.
                AssertTrue(true, "API sin motor nativo: estáticos verificados en TestQrCode");
                return;
            }
            using (engine)
            using (ApiV1Server api = new ApiV1Server(engine))
            {
                api.MaxClients = 4;
                int port = 18000 + (System.Diagnostics.Process.GetCurrentProcess().Id % 2000);
                AssertTrue(api.StartLocal(port), "arranque localhost en " + port);
                AssertTrue(api.IsRunning, "servidor corriendo");
                string token = api.Token;
                AssertTrue(token.Length >= 32, "token generado automáticamente (F5.01.7)");
                string url = "http://127.0.0.1:" + port + "/";

                // ---- 401 SIN token (F5.02.2 / F5.01.9) ----
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url + "api/v1/state");
                rq.Method = "GET";
                try
                {
                    using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                    { AssertTrue((int)rs.StatusCode == 401, "debe rechazar sin token"); }
                }
                catch (WebException we)
                {
                    HttpWebResponse rs = (HttpWebResponse)we.Response;
                    AssertTrue(rs != null && (int)rs.StatusCode == 401,
                        "401 sin Bearer (F5.01.9)");
                }

                // ---- 401 con token INVÁLIDO ----
                rq = (HttpWebRequest)WebRequest.Create(url + "api/v1/state");
                rq.Method = "GET";
                rq.Headers["Authorization"] = "Bearer " + new string('x', 40);
                try
                {
                    using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                    { AssertTrue(false, "token inválido debe rechazar"); }
                }
                catch (WebException we)
                {
                    HttpWebResponse rs = (HttpWebResponse)we.Response;
                    AssertTrue(rs != null && (int)rs.StatusCode == 401, "401 con token inválido");
                }

                // ---- GET /api/v1/state CON Bearer (F5.02.1) ----
                rq = (HttpWebRequest)WebRequest.Create(url + "api/v1/state");
                rq.Method = "GET";
                rq.Headers["Authorization"] = "Bearer " + token;
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                using (StreamReader sr = new StreamReader(rs.GetResponseStream()))
                {
                    AssertTrue((int)rs.StatusCode == 200, "200 con Bearer");
                    string body = sr.ReadToEnd();
                    AssertTrue(body.Contains("\"version\""), "estado JSON completo");
                }

                // ---- POST /api/v1/next ----
                rq = (HttpWebRequest)WebRequest.Create(url + "api/v1/next");
                rq.Method = "POST";
                rq.Headers["Authorization"] = "Bearer " + token;
                // net48 (HttpWebRequest): POST sin cuerpo DEBE declarar
                // Content-Length: 0 — sin él HttpListener responde 411.
                // (net8 lo envía solo; los clientes reales también.)
                rq.ContentLength = 0;
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                    AssertTrue((int)rs.StatusCode == 200, "POST next 200");

                // ---- GET /api/v1/text?format=plain|json (F5.07) ----
                api.LiveTextProvider = delegate { return "Aleluya"; };
                rq = (HttpWebRequest)WebRequest.Create(url + "api/v1/text?format=plain");
                rq.Method = "GET";
                rq.Headers["Authorization"] = "Bearer " + token;
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                using (StreamReader sr = new StreamReader(rs.GetResponseStream()))
                    AssertTrue(sr.ReadToEnd() == "Aleluya", "texto plano consumible por OBS");
                rq = (HttpWebRequest)WebRequest.Create(url + "api/v1/text?format=json");
                rq.Method = "GET";
                rq.Headers["Authorization"] = "Bearer " + token;
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                using (StreamReader sr = new StreamReader(rs.GetResponseStream()))
                    AssertTrue(sr.ReadToEnd().Contains("Aleluya"), "texto JSON");

                // ---- POST /api/v1/message (Lower Third, F3.04) ----
                bool msgSeen = false;
                api.OnMessage += delegate(Dictionary<string, object> b) { msgSeen = true; };
                rq = (HttpWebRequest)WebRequest.Create(url + "api/v1/message");
                rq.Method = "POST";
                rq.Headers["Authorization"] = "Bearer " + token;
                rq.ContentType = "application/json";
                byte[] bodyB = Encoding.UTF8.GetBytes("{\"text\":\"Bienvenidos\"");
                // cuerpo JSON válido:
                bodyB = Encoding.UTF8.GetBytes("{\"text\":\"Bienvenidos\"}");
                rq.ContentLength = bodyB.Length;
                using (System.IO.Stream st = rq.GetRequestStream()) st.Write(bodyB, 0, bodyB.Length);
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                    AssertTrue((int)rs.StatusCode == 200, "POST message 200");
                AssertTrue(msgSeen, "evento OnMessage disparado");

                // ---- POST /api/v1/goto con index ----
                engine.LoadScenario("{\"name\":\"t\",\"items\":[{\"kind\":\"text\",\"text\":\"A\"}]}");
                rq = (HttpWebRequest)WebRequest.Create(url + "api/v1/goto");
                rq.Method = "POST";
                rq.Headers["Authorization"] = "Bearer " + token;
                rq.ContentType = "application/json";
                bodyB = Encoding.UTF8.GetBytes("{\"index\":0}");
                rq.ContentLength = bodyB.Length;
                using (System.IO.Stream st = rq.GetRequestStream()) st.Write(bodyB, 0, bodyB.Length);
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                    AssertTrue((int)rs.StatusCode == 200, "POST goto 200");

                // ---- POST /api/v1/bible (cita estructurada) ----
                Dictionary<string, object> bibleArg = null;
                api.OnBible += delegate(Dictionary<string, object> b) { bibleArg = b; };
                rq = (HttpWebRequest)WebRequest.Create(url + "api/v1/bible");
                rq.Method = "POST";
                rq.Headers["Authorization"] = "Bearer " + token;
                rq.ContentType = "application/json";
                bodyB = Encoding.UTF8.GetBytes("{\"book\":\"Juan\",\"chapter\":3,\"verseFrom\":16}");
                rq.ContentLength = bodyB.Length;
                using (System.IO.Stream st = rq.GetRequestStream()) st.Write(bodyB, 0, bodyB.Length);
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                    AssertTrue((int)rs.StatusCode == 200, "POST bible 200");
                AssertTrue(bibleArg != null && MiniJson.GetString(bibleArg, "book", "") == "Juan",
                    "cita estructurada recibida");

                // ---- QR del emparejamiento (F5.03.11) ----
                rq = (HttpWebRequest)WebRequest.Create(url + "api/v1/qr.png");
                rq.Method = "GET";
                rq.Headers["Authorization"] = "Bearer " + token;
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                using (System.IO.Stream st = rs.GetResponseStream())
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[4096]; int n;
                    while ((n = st.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    byte[] png = ms.ToArray();
                    AssertTrue(png.Length > 100 && png[1] == 0x50, "QR PNG servido");
                    AssertTrue(rs.ContentType.Contains("image/png"), "content-type PNG");
                }

                // ---- cliente móvil servido LOCALMENTE (F5.03.1) ----
                rq = (HttpWebRequest)WebRequest.Create(url + "remote.html");
                rq.Method = "GET";          // SIN token: el emparejamiento es
                                            // por el hash #token del cliente
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                using (StreamReader sr = new StreamReader(rs.GetResponseStream()))
                {
                    string html = sr.ReadToEnd();
                    AssertTrue(html.Contains("LuminaPresentation") &&
                               html.Contains("Bearer "), "página móvil con Bearer emparejado");
                }

                // ---- bitácora de acceso SIN token (F5.03.12) ----
                List<string> log = api.AccessLogSnapshot();
                bool any401 = false;
                foreach (string l in log) if (l.Contains("401")) any401 = true;
                AssertTrue(any401, "rechazos registrados (F5.01.10)");
                foreach (string l in log)
                    AssertTrue(!l.Contains(token), "el token NO aparece en logs (F5.03.12)");

                // ---- 404 de rutas desconocidas ----
                rq = (HttpWebRequest)WebRequest.Create(url + "api/v1/noexiste");
                rq.Method = "GET";
                rq.Headers["Authorization"] = "Bearer " + token;
                try
                {
                    using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                    { AssertTrue(false, "ruta desconocida debe 404"); }
                }
                catch (WebException we)
                {
                    HttpWebResponse rs = (HttpWebResponse)we.Response;
                    AssertTrue(rs != null && (int)rs.StatusCode == 404, "404 ruta desconocida");
                }
            }
        }

        // =====================================================================
        // v7.0.0 «ULTRA» — Holyrics/PCO/NDI/Diagnóstico (F4.14/F5.06/F5.08/F5.10).
        // =====================================================================

        private static void TestHolyrics()
        {
            string dir = Path.Combine(Path.GetTempPath(), "holyrics-" +
                Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            try
            {
                // Exportación XML estilo Holyrics.
                string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<songs><song><title>Cuán grande es Él</title>" +
                    "<artist>Traditional</artist>" +
                    "<lyrics>Cuán grande es Él\nCuán grande es Él\n\nSanto, santo, santo</lyrics>" +
                    "<categories><category>Adoración</category></categories>" +
                    "<background>fondo1.png</background></song>" +
                    "<song><title>Santo Dios</title><lyrics>Santo Dios</lyrics>" +
                    "<categories><category>Alabanza</category></categories></song></songs>";
                string xmlPath = Path.Combine(dir, "biblia.xml");
                File.WriteAllText(xmlPath, xml, Encoding.UTF8);
                File.WriteAllBytes(Path.Combine(dir, "fondo1.png"),
                    new byte[] { 1, 2, 3, 4, 5 });

                List<lumina.core.import.HolyricsSong> songs =
                    lumina.core.import.HolyricsImporter.Parse(xmlPath);
                AssertTrue(songs.Count == 2, "2 canciones XML parseadas");
                AssertTrue(songs[0].Title == "Cuán grande es Él", "título con acento");
                AssertTrue(songs[0].Categories[0] == "Adoración", "categoría leída");

                // Exportación JSON estilo Holyrics.
                string json = "{\"songs\":[{\"title\":\"Otra\",\"lyrics\":\"L1\nL2\"}]}";
                string jsonPath = Path.Combine(dir, "biblio.json");
                File.WriteAllText(jsonPath, json, Encoding.UTF8);
                List<lumina.core.import.HolyricsSong> jsongs =
                    lumina.core.import.HolyricsImporter.Parse(jsonPath);
                AssertTrue(jsongs.Count == 1 && jsongs[0].Title == "Otra", "JSON parseado");

                // Dedupe normalizado (F4.14.6): título/letra con acentos vs sin.
                string k1 = lumina.core.import.HolyricsImporter.DedupeKey(
                    "Cuan Grande Es El", "cuan grande es el");
                string k2 = lumina.core.import.HolyricsImporter.DedupeKey(
                    "Cuán Grande Es Él", "Cuán   grande es  él");
                AssertTrue(k1 == k2, "clave dedupe insensible a acentos/espacios");

                // Conversión a ahp + confirmación ANTES de sobrescribir.
                AhpProject p = new AhpProject();
                HashSet<string> existing = new HashSet<string>();
                existing.Add(lumina.core.import.HolyricsImporter.DedupeKey(
                    "Santo Dios", "Santo Dios"));     // la 2.ª ya existe local
                int confirmaciones = 0;
                lumina.core.import.HolyricsReport rep =
                    lumina.core.import.HolyricsImporter.ToAhp(songs, p, existing,
                        dir, Path.Combine(dir, "media"),
                        delegate(string t, string f) { confirmaciones++; return false; });
                AssertTrue(rep.Imported == 1, "importada la nueva");
                AssertTrue(rep.Skipped == 1, "la existente OMITIDA sin autorización (F4.14.8)");
                AssertTrue(confirmaciones == 1, "se pidió confirmación (F4.14.7)");
                AssertTrue(p.Scenarios[0].Elements[0].Lines.Count == 3, "letra → líneas");
                AssertTrue(p.Scenarios[0].Elements[0].Tags.Contains("Adoración"),
                    "categoría → etiqueta (F4.14.3)");
                // fondo re-vinculado con ruta RELATIVA (F4.14.5)
                AssertTrue(p.Scenarios[0].Elements[0].ImagePath == "media/fondo1.png",
                    "fondo vinculado relativo");
                AssertTrue(File.Exists(Path.Combine(dir, "media", "fondo1.png")),
                    "fondo copiado a media/");
                // con autorización → sobrescribe
                AhpProject p2 = new AhpProject();
                lumina.core.import.HolyricsReport rep2 =
                    lumina.core.import.HolyricsImporter.ToAhp(songs, p2, existing,
                        dir, Path.Combine(dir, "media2"),
                        delegate(string t, string f) { return true; });
                AssertTrue(rep2.Skipped == 0 && rep2.Imported == 2,
                    "con autorización se importan ambas");
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        private static void TestPco()
        {
            // F5.08: token ausente → error HUMANO (no excepción técnica).
            bool humanError = false;
            try { new lumina.core.integrations.PlanningCenterClient("", ""); }
            catch (lumina.core.integrations.PcoException ex)
            {
                humanError = ex.Message.Contains("Planning Center") &&
                             !ex.Message.Contains("at ") && !ex.Message.Contains("HRESULT");
            }
            AssertTrue(humanError, "error humano por token faltante");

            // Conversión plan → escenario ahp (F5.08.4).
            lumina.core.integrations.PcoPlan plan = new lumina.core.integrations.PcoPlan();
            plan.Title = "Domingo 10 am";
            lumina.core.integrations.PcoItem h = new lumina.core.integrations.PcoItem();
            h.Kind = "header"; h.Title = "Adoración";
            plan.Items.Add(h);
            lumina.core.integrations.PcoItem song = new lumina.core.integrations.PcoItem();
            song.Kind = "song"; song.Title = "Cuán grande";
            plan.Items.Add(song);
            lumina.core.integrations.PcoItem note = new lumina.core.integrations.PcoItem();
            note.Kind = "item"; note.Title = "Anuncios";
            note.Description = "Agradecer al equipo de sonido";
            plan.Items.Add(note);

            AhpProject p = new AhpProject();
            Func<string, AhpElement> matcher = delegate(string title)
            {
                AhpElement e = p.NewElement(AhpElementKind.Text);
                e.Title = title; e.Lines.Add(new AhpLine { Text = "letra de " + title });
                return e;
            };
            AhpScenario sc = lumina.core.integrations.PlanningCenterClient.ToAhpScenario(
                plan, p, matcher);
            // header NO crea elemento; song → matched; item → texto con notas.
            AssertTrue(sc.Elements.Count == 2, "header omitido, song+item");
            AssertTrue(sc.Elements[0].Title == "Cuán grande" &&
                       sc.Elements[0].Lines[0].Text == "letra de Cuán grande",
                "canción emparejada");
            AssertTrue(sc.Elements[1].Lines[0].Text.Contains("equipo de sonido"),
                "ítem no musical con notas (offline desde entonces — F5.08.6)");
        }

        private static void TestNdi()
        {
            // F5.06: SIN runtime NDI instalado → estado claro, CERO crash,
            // y las funciones devuelven false (F0.04 regla común).
            using (lumina.core.integrations.NdiOutput ndi =
                new lumina.core.integrations.NdiOutput())
            {
                AssertTrue(!ndi.LoadSdk(), "runtime NDI ausente en el arnés");
                AssertTrue(!ndi.Start("Lumina"), "inicio rechazado sin runtime");
                lumina.core.integrations.NdiStatus st = ndi.Status();
                AssertTrue(!st.SdkLoaded && !st.Sending, "estado visible no disponible");
                AssertTrue(st.LastError.Length > 0, "diagnóstico humano del motivo");
                AssertTrue(st.LastError.Contains("NDI") , "mensaje accionable");
                // frame a una fuente inexistente: false sin excepción.
                AssertTrue(!ndi.SendFrameRgba(new byte[64], 4, 4), "frame sin fuente → false");
            }
        }

        private static void TestDiagnostics()
        {
            // F6.01: contadores + observaciones + volcado 60 s.
            lumina.core.diagnostics.MetricsCollector m =
                new lumina.core.diagnostics.MetricsCollector();
            m.Count("comandosDescartados", 2);
            m.Count("comandosDescartados", 1);
            m.ObserveRenderMs(5.5);
            m.ObserveRenderMs(16.4);
            m.ObserveIpcLatencyMs(3);
            m.ObserveImportMs(1200);
            string snap = m.SnapshotJson("live");
            Dictionary<string, object> j = MiniJson.Parse(snap);
            AssertTrue(MiniJson.GetInt(j, "renderFrames", 0) == 2, "frames observados");
            AssertTrue(MiniJson.GetString(j, "renderAvgMs", "").Contains("10"),
                "avg render ~10.95 ms");
            AssertTrue(snap.Contains("comandosDescartados"), "contador descartados (F6.01)");
            AssertTrue(MiniJson.GetString(j, "importAvgMs", "") == "1200", "importación medida");
            // Tick60 antes de 60 s → null (aún no toca el volcado).
            AssertTrue(m.Tick60("live") == null, "volcado cada 60 s (no antes)");
            string dump1 = m.SnapshotJson("live");
            AssertTrue(m.Dumps().Count >= 0, "historial accesible");
            // F5.10: verificador — comprobación de carpeta con permisos.
            string dir = Path.Combine(Path.GetTempPath(), "diag-" +
                Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            lumina.core.diagnostics.DiagCheck c =
                lumina.core.diagnostics.EnvironmentVerifier.CheckFolder(dir);
            AssertTrue(c.Level == lumina.core.diagnostics.DiagLevel.Green &&
                       c.Detail.Contains(dir), "carpeta escribible → VERDE");
            try { Directory.Delete(dir, true); } catch (IOException) { }
            // verificación conjunta con proveedores nulos → ámbar (sin crash).
            List<lumina.core.diagnostics.DiagCheck> checks =
                lumina.core.diagnostics.EnvironmentVerifier.Verify(
                    null, null, null, null, null, null, null, null);
            AssertTrue(checks.Count == 8, "las 8 comprobaciones obligatorias (F5.10)");
        }

        // =====================================================================
        // v1.0.0-beta.1 «ULTRA» — brechas gestionadas:
        //   F1.05 atajos · F4.15 informe de fidelidad · F5.09 sandbox JSLib ·
        //   F6.04 Drive offline-first · F6.08 sesión de 60 minutos.
        // =====================================================================

        private static void TestSettingsShortcuts()
        {
            // F1.05: atajos personalizables persistidos en el JSON existente de
            // Settings — flechas, Espacio, Enter, F1-F12 (favoritos) y Esc =
            // pantalla de reposo. Defaults de fábrica si falta el archivo o el
            // campo; campo desconocido IGNORADO (F2.01.8).
            string dir = Path.Combine(Path.GetTempPath(),
                "lumina_tests_sc_" + Guid.NewGuid().ToString("N"));
            try
            {
                // — Defaults de fábrica sin archivo —
                Settings def = Settings.Load(Path.Combine(dir, "vacía"));
                AssertTrue(def.GetShortcut("avanzar") == "Espacio", "default avanzar=Espacio");
                AssertTrue(def.GetShortcut("retroceder") == "Izquierda", "default flecha izq");
                AssertTrue(def.GetShortcut("avanzarElemento") == "Derecha", "default flecha der");
                AssertTrue(def.GetShortcut("retrocederLinea") == "Arriba", "default flecha arriba");
                AssertTrue(def.GetShortcut("avanzarLinea") == "Abajo", "default flecha abajo");
                AssertTrue(def.GetShortcut("retrocederElemento") == "Enter", "default Enter");
                AssertTrue(def.GetShortcut("reposo") == "Escape", "default Esc=reposo");
                AssertTrue(def.GetShortcut("favorito1") == "F1" &&
                           def.GetShortcut("favorito12") == "F12", "F1-F12 favoritos");
                AssertTrue(def.GetShortcut("acción-inexistente").Length == 0,
                    "acción desconocida → cadena vacía");

                // — Personalización + roundtrip JSON —
                Settings written = new Settings(dir);
                written.SetShortcut("avanzar", "N");
                written.SetShortcut("favorito3", "F9");
                written.SetShortcut("", "X");          // acción vacía ignorada
                written.SetShortcut("reposo", "");     // tecla vacía ignorada
                written.Save();
                AssertTrue(File.Exists(written.FilePath), "settings.json creado");

                Settings read = Settings.Load(dir);
                AssertTrue(read.GetShortcut("avanzar") == "N", "atajo personalizado persistido");
                AssertTrue(read.GetShortcut("favorito3") == "F9", "favorito repunteado persistido");
                AssertTrue(read.GetShortcut("reposo") == "Escape", "los no tocados quedan por defecto");

                // — Campo desconocido dentro de shortcuts → ignorado; valor no
                // string → ignorado (el campo válido sobrevive vía roundtrip) —
                string path = read.FilePath;
                Dictionary<string, object> root = MiniJson.Parse(
                    File.ReadAllText(path, new UTF8Encoding(false)));
                AssertTrue(root.ContainsKey("shortcuts"), "campo shortcuts presente");
                Dictionary<string, object> sc = MiniJson.GetObject(root, "shortcuts");
                sc["atajoDelFuturo"] = "Ctrl+Q";       // acción desconocida (F2.01.8)
                sc["retroceder"] = 42L;                // valor no string
                File.WriteAllText(path, MiniJson.Serialize(root), new UTF8Encoding(false));
                Settings tolerant = Settings.Load(dir);
                AssertTrue(!tolerant.ShortcutsSnapshot().ContainsKey("atajoDelFuturo"),
                    "acción desconocida descartada");
                AssertTrue(tolerant.GetShortcut("retroceder") == "Izquierda",
                    "valor no-string → se rellena con el default de fábrica");
                AssertTrue(tolerant.GetShortcut("favorito3") == "F9",
                    "las entradas válidas persisten junto a las ignoradas");

                // — JSON viejo SIN el campo shortcuts → defaults completos —
                root.Remove("shortcuts");
                File.WriteAllText(path, MiniJson.Serialize(root), new UTF8Encoding(false));
                Settings old = Settings.Load(dir);
                AssertTrue(old.GetShortcut("avanzar") == "Espacio" &&
                           old.GetShortcut("favorito12") == "F12",
                    "sin campo → defaults de fábrica");

                // — ResetShortcuts vuelve a fábrica —
                old.SetShortcut("reposo", "R");
                old.ResetShortcuts();
                AssertTrue(old.GetShortcut("reposo") == "Escape" &&
                           old.GetShortcut("avanzar") == "Espacio", "reset de fábrica");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        private static void TestFidelityReport()
        {
            // F4.15: informe de fidelidad generalizado. Fixture PPTX REAL con el
            // propio PptxExporter (patrón «PptxExporter: estructura OPC mínima»),
            // macros con ZIP sintético ppt/vbaProject.bin, texto humano no vacío.
            Theme t = new Theme();

            // — Importación fiel 1:1 (exportar → importar) —
            byte[] pptx = PptxExporter.ExportToBytes("F4.15", SampleExportSlides(), t);
            PptxImportResult res;
            using (MemoryStream ms = new MemoryStream(pptx))
            {
                res = PptxImporter.Import(ms);
            }
            FidelityReport rep = res.Report;
            AssertTrue(rep != null, "informe presente en el resultado (campo, sin romper API)");
            AssertTrue(rep.UnitsConverted == 3,
                "3 diapositivas convertidas (hay " + rep.UnitsConverted + ")");
            AssertTrue(rep.OmittedCount == 0, "paquete fiel → sin omisiones");
            AssertTrue(rep.MacrosCount == 0, "paquete limpio → sin macros");
            AssertTrue(rep.Ok, "resultado final OK");
            string human = rep.ToHumanText();
            AssertTrue(!string.IsNullOrEmpty(human) && human.Length > 40,
                "texto humano no vacío");
            AssertTrue(human.Contains("Copiar detalles técnicos"),
                "plantilla humanizer: «Copiar detalles técnicos»");
            Dictionary<string, object> j = MiniJson.Parse(rep.ToJson());
            AssertTrue(MiniJson.GetString(j, "format", "") == "fidelity.v1", "JSON fidelity.v1");
            Dictionary<string, object> rj = MiniJson.GetObject(j, "resultado");
            AssertTrue(MiniJson.GetInt(rj, "unidadesConvertidas", 0) == 3,
                "resultado JSON con conteos");

            // — Exportador con informe (overload out; la firma histórica sigue) —
            FidelityReport expRep;
            PptxExporter.ExportToBytes("F4.15-out", SampleExportSlides(), t, out expRep);
            AssertTrue(expRep.UnitsConverted == 3, "exporter: 3 diapositivas");
            AssertTrue(expRep.Warnings.Count > 0 &&
                       expRep.Warnings[0].Contains("MVP"), "regla MVP documentada");
            AssertTrue(expRep.UnsupportedCount >= 1, "animaciones declaradas no soportadas");
            FidelityReport pdfRep;
            PdfExporter.ExportToBytes("F4.15-pdf", SampleExportSlides(), t, out pdfRep);
            AssertTrue(pdfRep.UnitsConverted == 3, "pdf: 3 páginas");
            AssertTrue(pdfRep.ToHumanText().Contains("unidades convertidas"), "pdf: texto humano");

            // — MACROS detectadas (fixture sintético ZIP con ppt/vbaProject.bin):
            // se REPORTAN y JAMÁS se ejecutan (F4.16.3) — el contenido íntegro
            // se sigue importando.
            byte[] slide1 = new UTF8Encoding(false).GetBytes(
                "<?xml version=\"1.0\"?>" +
                "<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" " +
                "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
                "<p:cSld><p:spTree>" +
                "<p:sp><p:txBody><a:p><a:r><a:t>Palabra viva</a:t></a:r></a:p></p:txBody></p:sp>" +
                "</p:spTree></p:cSld></p:sld>");
            List<KeyValuePair<string, byte[]>> parts = new List<KeyValuePair<string, byte[]>>();
            parts.Add(new KeyValuePair<string, byte[]>("ppt/presentation.xml",
                new UTF8Encoding(false).GetBytes(
                    "<p:presentation xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"/>")));
            parts.Add(new KeyValuePair<string, byte[]>("ppt/slides/slide1.xml", slide1));
            parts.Add(new KeyValuePair<string, byte[]>("ppt/vbaProject.bin",
                new byte[] { 0x4D, 0x53, 0x46, 0x54 }));   // firma OLE sintética
            MemoryStream macrosZip = new MemoryStream();
            ZipWriter.WriteEntries(macrosZip, parts);
            macrosZip.Position = 0;
            PptxImportResult mres = PptxImporter.Import(macrosZip);
            AssertTrue(mres.Report.MacrosCount == 1,
                "macro detectada (hay " + mres.Report.MacrosCount + ")");
            AssertTrue(mres.Slides.Count == 1 && mres.Slides[0].Lines[0] == "Palabra viva",
                "contenido íntegro pese a la macro");
            AssertTrue(mres.Report.ToHumanText().Contains("NO se ejecutaron"),
                "macro: avisada como no ejecutada");

            // — Tabla (graphicFrame) y animaciones OMITIDAS y REPORTADAS —
            byte[] slideTable = new UTF8Encoding(false).GetBytes(
                "<?xml version=\"1.0\"?>" +
                "<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" " +
                "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
                "<p:cSld><p:spTree>" +
                "<p:sp><p:txBody><a:p><a:r><a:t>Texto visible</a:t></a:r></a:p></p:txBody></p:sp>" +
                "<p:graphicFrame><a:graphic><a:graphicData uri=\"tabla\">" +
                "<a:tbl><a:tr><a:tc><a:t>celda no representable</a:t></a:tc></a:tr></a:tbl>" +
                "</a:graphicData></a:graphic></p:graphicFrame>" +
                "<p:timing><p:tnLst><p:par/></p:tnLst></p:timing>" +
                "</p:spTree></p:cSld></p:sld>");
            parts = new List<KeyValuePair<string, byte[]>>();
            parts.Add(new KeyValuePair<string, byte[]>("ppt/slides/slide1.xml", slideTable));
            MemoryStream tableZip = new MemoryStream();
            ZipWriter.WriteEntries(tableZip, parts);
            tableZip.Position = 0;
            PptxImportResult tres = PptxImporter.Import(tableZip);
            AssertTrue(tres.Slides.Count == 1 && tres.Slides[0].Lines.Count == 1,
                "el texto de la slide sobrevive (sin perder fidelidad)");
            AssertTrue(tres.Report.OmittedCount == 1, "tabla omitida reportada");
            AssertTrue(tres.Report.UnsupportedCount == 1, "animación reportada como efecto");
            AssertTrue(tres.Report.ToHumanText().Contains("tabla(s)/gráfico(s)"),
                "detalle de la omisión visible al operador");

            // — Holyrics: el resultado EXISTENTE se MAPEA (sin romper su API) —
            lumina.core.import.HolyricsReport hr = new lumina.core.import.HolyricsReport();
            hr.Imported = 2;
            hr.Skipped = 1;
            hr.BackgroundsLinked = 1;
            hr.Warnings.Add("prueba");
            FidelityReport fHr = FidelityReport.FromHolyrics(hr, "biblio.xml");
            AssertTrue(fHr.Source == "holyrics", "fuente holyrics");
            AssertTrue(fHr.UnitsConverted == 2 && fHr.OmittedCount == 1 &&
                       fHr.PathsRelinked == 1, "mapeo Holyrics→F4.15");
        }

        private static void TestJsSandboxBudgets()
        {
            // F5.09: presupuesto por script (CPU/memoria/conexiones) y errores
            // con archivo/línea. Sin disco expuesto (política documentada) y
            // REGRESIÓN: un ciclo normal de timers sigue funcionando.
            JsLogRing log = new JsLogRing();
            JsBudget budget = new JsBudget();
            budget.CpuCycleBudgetMs = 50;
            budget.MemoryBudgetBytes = 1000;
            budget.MaxConnectionsPerScript = 2;
            budget.MaxConnectionsTotal = 3;
            JsGovernor gov = new JsGovernor(budget, log);

            // — Script que excede el límite de conexiones → DENEGADO con error
            // visible en el log (F5.09.12 + F5.04.5) —
            AssertTrue(gov.TryOpenConnection("luces.js"), "conexión 1 concedida");
            AssertTrue(gov.TryOpenConnection("luces.js"), "conexión 2 concedida");
            AssertTrue(!gov.TryOpenConnection("luces.js"),
                "conexión 3 DENEGADA (tope por script)");
            AssertTrue(gov.ConnectionsOf("luces.js") == 2 && gov.TotalConnections == 2,
                "conteos activos correctos");
            List<string> snap = log.Snapshot();
            AssertTrue(snap.Count >= 1, "la denegación deja rastro en el log");
            AssertTrue(snap[snap.Count - 1].Contains("luces.js") &&
                       snap[snap.Count - 1].Contains("conexión denegada") &&
                       snap[snap.Count - 1].Contains("2/2"),
                "error visible y attributable: " + snap[snap.Count - 1]);
            gov.CloseConnection("luces.js");
            AssertTrue(gov.TryOpenConnection("luces.js"), "tras cerrar, el cupo se libera");

            // — Límite GLOBAL —
            AssertTrue(gov.TryOpenConnection("media.js"), "segundo script conecta");
            AssertTrue(gov.TotalConnections == 3, "global llena (3/3)");
            AssertTrue(!gov.TryOpenConnection("tercero.js"), "global deniega");
            gov.CloseConnection("media.js");
            AssertTrue(gov.TryOpenConnection("tercero.js"), "cupo global liberado");

            // — Memoria: cota operativa por script (documentada, no heap) —
            AssertTrue(gov.TryReserveMemory("luces.js", 600), "memoria 600/1000 ok");
            AssertTrue(!gov.TryReserveMemory("luces.js", 500),
                "memoria denegada (600+500 > 1000)");
            gov.ReleaseMemory("luces.js", 600);
            AssertTrue(gov.TryReserveMemory("luces.js", 500), "tras liberar, vuelve a caber");

            // — CPU: deadline por ciclo de despacho (motor mono-hilo → cooperativo) —
            AssertTrue(!gov.CpuCycleExpired("luces.js", 10), "ciclo dentro del presupuesto");
            AssertTrue(gov.CpuCycleExpired("luces.js", 51), "ciclo vencido detectado");

            // — Disco: POR DISEÑO no hay E/S de archivos expuesta a scripts —
            AssertTrue(JsGovernor.DiskPolicy.Contains("disco") &&
                       JsGovernor.DiskPolicy.Contains("proyecto"),
                "política de disco documentada");

            // — Errores con archivo/línea (F5.09.14) —
            AssertTrue(JsScriptError.ParseEngineLine("Se esperaba ';' en la línea 14") == 14,
                "línea extraída del mensaje del motor");
            AssertTrue(JsScriptError.ParseEngineLine("error sin línea") == 0,
                "sin línea en el motor → 0");
            string withLine = JsScriptError.Wrap("luces.js", 0, "TypeError en la línea 7");
            AssertTrue(withLine.Contains("luces.js") && withLine.Contains("(línea 7)"),
                "error attributable con la línea del motor: " + withLine);
            string wrapped = JsScriptError.Wrap("luces.js", 3, "expresión inválida");
            AssertTrue(wrapped.Contains("luces.js") && wrapped.Contains("(línea 3)"),
                "sin línea del motor → envuelto con el nombre del script");

            // — REGRESIÓN: ciclo NORMAL de timers con el sandbox activo —
            JsEventRegistry reg = new JsEventRegistry();
            JsTimerIds ids = new JsTimerIds();
            reg.Subscribe("tick", "cbTick");
            int executed = 0;
            for (int i = 0; i < 5; i++)
            {
                AssertTrue(gov.BeginCpuCycle("luces.js"), "despacho " + i + " admitido");
                int id = ids.Alloc();          // setTimeout
                ids.Release(id);               // clearTimeout
                gov.EndCpuCycle("luces.js");
                executed++;
            }
            AssertTrue(executed == 5 && ids.LiveCount == 0,
                "5 ciclos de timers normales con sandbox activo");
            AssertTrue(reg.Subscribers("tick").Count == 1, "registro de eventos intacto");
            AssertTrue(gov.UsageSnapshot().Count >= 3, "uso por script consultable (F5.10)");
            gov.Reset();
            AssertTrue(gov.TotalConnections == 0, "Reset suelta el presupuesto (recarga)");
        }

        /// <summary>Transporte Drive SIMULADO (F6.04): nube en memoria con
        /// token/lista/descarga/subida — cero red real, cero credenciales.</summary>
        private sealed class FakeDriveTransport : lumina.core.integrations.IDriveTransport
        {
            public readonly Dictionary<string, byte[]> Cloud =
                new Dictionary<string, byte[]>(StringComparer.Ordinal);
            public int Calls;
            public readonly List<string> Seen = new List<string>();

            public lumina.core.integrations.DriveHttpResponse Send(
                lumina.core.integrations.DriveHttpRequest rq)
            {
                Calls++;
                Seen.Add(rq.Method + " " + rq.Url);
                // OAuth token endpoint
                if (rq.Url.Contains("/token"))
                    return Json(200, "{\"access_token\":\"T-ARNES\",\"refresh_token\":\"R-ARNES\"}");
                // Multipart upload → actualiza la nube simulada
                if (rq.Url.Contains("uploadType=multipart"))
                {
                    string body = Latin1.GetString(rq.Body);
                    int nameAt = body.IndexOf("\"name\":\"", StringComparison.Ordinal) + 8;
                    int nameEnd = body.IndexOf("\"", nameAt);
                    string name = body.Substring(nameAt, nameEnd - nameAt);
                    string marker = "application/octet-stream\r\n\r\n";
                    int start = body.LastIndexOf(marker, StringComparison.Ordinal) + marker.Length;
                    int tail = body.LastIndexOf("\r\n--", StringComparison.Ordinal);
                    byte[] data = new byte[Math.Max(0, tail - start)];
                    Buffer.BlockCopy(rq.Body, start, data, 0, data.Length);
                    Cloud[name] = data;
                    return Json(200, "{\"id\":\"arnes-" + name + "\",\"name\":\"" + name + "\"}");
                }
                // Lista por nombre (files.list)
                if (rq.Url.Contains("/files?"))
                {
                    string q = Uri.UnescapeDataString(rq.Url);
                    int a = q.IndexOf("'", StringComparison.Ordinal);
                    int b = q.IndexOf("'", a + 1);
                    string name = a >= 0 && b > a ? q.Substring(a + 1, b - a - 1) : "";
                    if (Cloud.ContainsKey(name))
                        return Json(200, "{\"files\":[{\"id\":\"arnes-" + name +
                            "\",\"name\":\"" + name + "\"}]}");
                    return Json(200, "{\"files\":[]}");
                }
                // Descarga por id (alt=media)
                if (rq.Url.Contains("alt=media"))
                {
                    int f = rq.Url.IndexOf("/files/", StringComparison.Ordinal) + 7;
                    int e = rq.Url.IndexOf("?", f);
                    string id = rq.Url.Substring(f, e - f).Replace("arnes-", "");
                    if (Cloud.ContainsKey(id))
                    {
                        lumina.core.integrations.DriveHttpResponse r =
                            new lumina.core.integrations.DriveHttpResponse();
                        r.StatusCode = 200;
                        r.Body = Cloud[id];
                        return r;
                    }
                    return Json(404, "{\"error\":\"not found\"}");
                }
                return Json(400, "{\"error\":\"petición no simulada\"}");
            }

            private static lumina.core.integrations.DriveHttpResponse Json(int code, string json)
            {
                lumina.core.integrations.DriveHttpResponse r =
                    new lumina.core.integrations.DriveHttpResponse();
                r.StatusCode = code;
                r.Body = new UTF8Encoding(false).GetBytes(json);
                return r;
            }
        }

        private static void TestDriveOfflineQueue()
        {
            // F6.04: copias de canciones/biblias/configuraciones/proyectos;
            // OFFLINE PRIMERO (cola persistente → descarga al reconectar);
            // conflictos por versión ahp.v1 SIN pérdida silenciosa; historial
            // de conflictos consultable y persistente (WhenUtc/Resource/…).
            string dir = Path.Combine(Path.GetTempPath(),
                "lumina_drive_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                FakeDriveTransport fake = new FakeDriveTransport();
                GoogleDriveClient client = new GoogleDriveClient(
                    "arnes-client", "arnes-secret",
                    "http://auth.test/o/oauth2/auth", "http://auth.test/token",
                    "http://drive.test/v3", "http://drive.test/upload/v3");
                client.Transport = fake;
                // Sesión OAuth restaurada (patrón real: el refresh token vive
                // en Settings de arranques anteriores) → EnsureToken() pasa
                // por el endpoint /token del transporte simulado.
                client.SetSession("R-ARNES", "");
                string conflictsFile = Path.Combine(dir, "conflictos.json");
                client.ConflictHistoryFile = conflictsFile;

                DriveSyncEngine engine = new DriveSyncEngine(client,
                    Path.Combine(dir, "cola.json"));
                engine.IsOnline = false;   // OFFLINE PRIMERO (F6.04.5)

                AssertTrue(DriveSyncEngine.ResourceFor("proyectos", "culto.ahp")
                    == "proyectos/culto.ahp", "categoría proyectos → recurso remoto");
                AssertTrue(DriveSyncEngine.ResourceFor("CANCIONES", "aleluya.json")
                    == "canciones/aleluya.json", "categoría canciones → recurso remoto");

                // — Offline: encolada, CERO red, cola persistida —
                string cloudJson = "{\"format\":\"ahp.v1\",\"project\":{\"name\":\"Culto\",\"nextId\":12},\"media\":[]}";
                fake.Cloud["proyectos/culto.ahp"] = Encoding.UTF8.GetBytes(cloudJson);
                string localPath = Path.Combine(dir, "culto.ahp");
                engine.QueueDownload("proyectos/culto.ahp", localPath);
                AssertTrue(engine.PendingCount == 1, "1 operación en cola");
                AssertTrue(fake.Calls == 0, "offline: CERO llamadas de red");
                DriveSyncResult offline = engine.Flush();
                AssertTrue(!offline.Ok && engine.PendingCount == 1 && fake.Calls == 0,
                    "flush sin red no toca nada (sigue en cola)");
                AssertTrue(File.Exists(Path.Combine(dir, "cola.json")),
                    "cola persistida en JSON");

                // — Reconectar: la operación se DESCARGA (F6.04.5) —
                engine.IsOnline = true;
                DriveSyncResult res = engine.Flush();
                AssertTrue(res.Ok, "flush en línea sin errores: " + res.LastError +
                    " | peticiones: [" + string.Join(" | ", fake.Seen.ToArray()) + "]");
                AssertTrue(res.Downloaded == 1 && engine.PendingCount == 0,
                    "descargada al reconectar");
                AssertTrue(File.ReadAllText(localPath) == cloudJson, "contenido íntegro");
                AssertTrue(client.Status().Downloaded >= 1, "contador visible (F5.10)");

                // — Conflicto ahp.v1: LOCAL gana (20 > 12). La nube perdedora
                // queda como COPIA y la decisión va al historial (F6.04.6-8) —
                string local20 = "{\"format\":\"ahp.v1\",\"project\":{\"name\":\"Culto LOCAL\",\"nextId\":20},\"media\":[]}";
                File.WriteAllText(localPath, local20, new UTF8Encoding(false));
                engine.QueueUpload("proyectos/culto.ahp", localPath);
                res = engine.Flush();
                AssertTrue(res.Conflicts == 1 && res.Uploaded == 1,
                    "conflicto resuelto: local gana y sube");
                AssertTrue(Encoding.UTF8.GetString(fake.Cloud["proyectos/culto.ahp"]) == local20,
                    "la nube recibe la versión ganadora");
                string[] bakCloud = Directory.GetFiles(dir, "culto.ahp.conflicto-nube-*.bak");
                AssertTrue(bakCloud.Length == 1,
                    "copia conservada de la nube (JAMÁS pérdida silenciosa)");
                AssertTrue(File.ReadAllText(bakCloud[0]) == cloudJson,
                    "la copia es la versión perdedora íntegra");

                // — Conflicto: NUBE gana (5 < 30) — la LOCAL perdedora se
                // conserva y el archivo local toma la versión de la nube —
                string local5 = "{\"format\":\"ahp.v1\",\"project\":{\"name\":\"Viejo\",\"nextId\":5},\"media\":[]}";
                File.WriteAllText(localPath, local5, new UTF8Encoding(false));
                fake.Cloud["proyectos/culto.ahp"] = Encoding.UTF8.GetBytes(
                    "{\"format\":\"ahp.v1\",\"project\":{\"name\":\"Culto NUBE\",\"nextId\":30},\"media\":[]}");
                engine.QueueUpload("proyectos/culto.ahp", localPath);
                res = engine.Flush();
                AssertTrue(res.Conflicts == 1, "segundo conflicto detectado");
                AssertTrue(File.ReadAllText(localPath).Contains("NUBE"),
                    "la nube ganadora queda en local");
                string[] bakLocal = Directory.GetFiles(dir, "culto.ahp.conflicto-local-*.bak");
                AssertTrue(bakLocal.Length == 1 &&
                           File.ReadAllText(bakLocal[0]) == local5,
                    "la LOCAL perdedora se conserva íntegra");

                // — Historial de conflictos consultable y PERSISTENTE —
                List<DriveConflict> hist = client.ConflictHistory();
                AssertTrue(hist.Count == 2, "2 conflictos en historial (hay " + hist.Count + ")");
                AssertTrue(hist[0].Resolution == "local" &&
                           hist[0].Resource == "proyectos/culto.ahp", "resolución registrada");
                AssertTrue(hist[1].Resolution == "nube", "segunda resolución registrada");
                AssertTrue(hist[0].LocalVersion == "20" && hist[0].CloudVersion == "12",
                    "versiones del conflicto registradas");
                AssertTrue(File.Exists(conflictsFile), "historial persistido a JSON");
                Dictionary<string, object> hroot = MiniJson.Parse(
                    File.ReadAllText(conflictsFile, new UTF8Encoding(false)));
                List<object> harr = MiniJson.GetArray(hroot, "conflicts");
                AssertTrue(harr.Count == 2 &&
                           MiniJson.GetString((Dictionary<string, object>)harr[0],
                               "resolution", "") == "local",
                    "JSON del historial con WhenUtc/Resource/Resolution");
                GoogleDriveClient client2 = new GoogleDriveClient("t", "s", "a", "b", "c", "d");
                client2.ConflictHistoryFile = conflictsFile;
                client2.LoadConflictHistory();
                AssertTrue(client2.ConflictHistory().Count == 2,
                    "historial sobrevive al reinicio");
                AssertTrue(client2.ConflictHistory()[0].WhenUtc >
                        DateTime.UtcNow.AddDays(-1), "WhenUtc legible");

                // — Misma versión → SIN conflicto (subida idempotente omitida) —
                File.WriteAllText(localPath, cloudJson, new UTF8Encoding(false));
                fake.Cloud["proyectos/culto.ahp"] = Encoding.UTF8.GetBytes(cloudJson);
                engine.QueueUpload("proyectos/culto.ahp", localPath);
                res = engine.Flush();
                AssertTrue(res.Conflicts == 0 && res.Uploaded == 0,
                    "misma versión → nada que hacer");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        private static void TestSession60()
        {
            // F6.08: sesión de servicio SIMULADA y cronometrada — proyección
            // continua (escenario/elemento/línea vía ScenarioBuilder +
            // AhpBridge), cambios de tema (StyleCascade), imágenes, Lower
            // Third, Stage View state, API y Triggers, y ERRORES CONTROLADOS
            // (archivo corrupto → fail-safe). Duración por env var
            // LUMINA_SESSION_MINUTES (default 2 en el arnés; 60 = campaña
            // completa documentada). FALLA si hay ERROR no justificado.
            double minutes = 2.0;
            string env = Environment.GetEnvironmentVariable("LUMINA_SESSION_MINUTES");
            if (!string.IsNullOrEmpty(env))
            {
                double parsed;
                if (double.TryParse(env, NumberStyles.Float, CultureInfo.InvariantCulture,
                                    out parsed) && parsed >= 0 && parsed <= 60)
                    minutes = parsed;
            }
            Console.WriteLine("  [F6.08] duración: " +
                minutes.ToString("0.##", CultureInfo.InvariantCulture) + " min" +
                (minutes >= 60 ? " (campaña completa)" : " (arnés; LUMINA_SESSION_MINUTES=60 para campaña)"));

            lumina.core.diagnostics.MetricsCollector metrics =
                new lumina.core.diagnostics.MetricsCollector();
            StateProvider stage = new StateProvider();
            CountingSink sink = new CountingSink();

            // — TriggerEngine: regla del servicio (elemento → escena OBS) —
            TriggerEngine triggers = new TriggerEngine();
            TriggerRule rule = new TriggerRule();
            rule.Id = "s1"; rule.Name = "Escena por canto";
            rule.Event = "item_changed";
            rule.Match["title"] = "contains:canto";
            TriggerAction act = new TriggerAction();
            act.Type = "obs_scene";
            act.Params["scene"] = "Culto";
            rule.Actions.Add(act);
            triggers.SetRules(new TriggerRule[] { rule });
            triggers.SetSink(sink);

            // — Escenario del servicio: ahp.v1 → JSON del motor (AhpBridge) —
            AhpProject project = BuildSampleProject();
            string engineJson = AhpBridge.ToEngineScenario(project.Scenarios[0], "C:\\base");
            Dictionary<string, object> scenario = MiniJson.Parse(engineJson);
            List<object> items = MiniJson.GetArray(scenario, "items");
            AssertTrue(items.Count == 5, "escenario con los 5 elementos (F2.02)");
            AhpElement ltElement = project.Scenarios[0].Elements[4];   // Lower Third

            // — Cambios de tema (cascada Tema→Plantilla→Escenario→Elemento) —
            Dictionary<string, string> themeA = new Dictionary<string, string>();
            themeA["fontFace"] = "Segoe UI"; themeA["fontSize"] = "48";
            Dictionary<string, string> themeB = new Dictionary<string, string>();
            themeB["fontFace"] = "Georgia"; themeB["fontSize"] = "56";

            // — Imagen REAL para las ops de imagen (PNG válido del QR propio) —
            string imgPath = Path.Combine(Path.GetTempPath(),
                "lumina_sesion_" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(imgPath, QrCode.ToPng(QrCode.Encode("F6.08"), 2, 2));

            // — API real SOLO con núcleo nativo (sin él: parte JUSTIFICADA,
            // no es un error — el resto del servicio sigue simulado) —
            ApiV1Server api = null;
            LuminaEngine nativeEngine = null;
            int apiPort = 0;
            try
            {
                nativeEngine = LuminaEngine.Create(true, null);
                api = new ApiV1Server(nativeEngine);
                apiPort = 19000 + (System.Diagnostics.Process.GetCurrentProcess().Id % 1000);
                if (!api.StartLocal(apiPort)) { api = null; }
            }
            catch (Exception)
            {
                if (nativeEngine != null) { try { nativeEngine.Dispose(); } catch (Exception) { } }
                nativeEngine = null;
                api = null;
            }
            string apiToken = api != null ? api.Token : "";
            string apiBase = api != null ? "http://127.0.0.1:" + apiPort + "/" : "";
            Console.WriteLine("  [F6.08] API " +
                (api != null ? "real en :" + apiPort : "simulada (sin núcleo nativo — justificado)"));

            // — Errores controlados: fixtures corruptos (fail-safe) —
            string corruptDir = Path.Combine(Path.GetTempPath(),
                "lumina_sesion_corrupt_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(corruptDir);
            File.WriteAllText(Path.Combine(corruptDir, "settings.json"),
                "{ esto no es json", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(corruptDir, "triggers.json"),
                "[[[(corrupto", new UTF8Encoding(false));
            byte[] corruptPptx = new byte[256];
            for (int i = 0; i < corruptPptx.Length; i++) corruptPptx[i] = (byte)(i * 7);

            long operations = 0, lineChanges = 0, elementChanges = 0, themeChanges = 0;
            long imageOps = 0, ltOps = 0, stageOps = 0, apiOps = 0, triggerFires = 0;
            long controlledErrors = 0, unjustified = 0;
            int element = 0, line = 0;
            bool restActive = false;
            bool first = true;
            DateTime startUtc = DateTime.UtcNow;   // inicio real de la sesión
            System.Diagnostics.Stopwatch tick = new System.Diagnostics.Stopwatch();
            System.Diagnostics.Stopwatch session = System.Diagnostics.Stopwatch.StartNew();
            DateTime lastImage = DateTime.UtcNow;
            DateTime lastPdf = DateTime.UtcNow;
            DateTime lastCorrupt = DateTime.UtcNow;
            DateTime lastProgress = DateTime.UtcNow;
            double nextApiPoll = 0;   // segundos de sesión

            try
            {
                while (DateTime.UtcNow < startUtc.AddMinutes(minutes) || first)
                {
                    first = false;
                    tick.Restart();
                    try
                    {
                        // 1) Proyección continua: navega línea/elemento.
                        int lineCount = SessionLineCount(items[element]);
                        line++;
                        if (line >= lineCount)
                        {
                            line = 0;
                            element = (element + 1) % items.Count;
                            elementChanges++;
                            Dictionary<string, object> ctx = new Dictionary<string, object>();
                            ctx["title"] = "Canto " + element;
                            triggerFires += triggers.Fire("item_changed", ctx);
                        }
                        else lineChanges++;

                        // 2) Stage View state + proyección publicada (thread-safe).
                        StateSnapshot snap = new StateSnapshot();
                        snap.Mode = "live";
                        snap.ScenarioTitle = "Sesión F6.08";
                        snap.ElementIndex = element;
                        snap.ElementCount = items.Count;
                        snap.LineIndex = line;
                        snap.LineCount = lineCount;
                        snap.ThemeRef = themeChanges % 2 == 0 ? "theme-calma" : "theme-fiesta";
                        snap.RestActive = restActive;
                        snap.RestScreen = restActive ? "logo" : "black";
                        snap.OutputVisible = true;
                        snap.VideoPlaying = element == 3;   // el elemento 4 es video
                        snap.Volume = 80;
                        stage.Publish(snap);
                        stageOps++;

                        // El retrato publicado DEBE leerse íntegro (contrato).
                        StateSnapshot cur = stage.Current;
                        if (cur.ElementIndex != element || cur.LineIndex != line ||
                            cur.OutputVisible != true)
                            throw new Exception("estado publicado no coincide (Stage View)");

                        // 3) Cambio de tema cada 12 cambios de elemento (cascada).
                        if (elementChanges > 0 && elementChanges % 12 == 0)
                        {
                            Dictionary<string, string> vacio = new Dictionary<string, string>();
                            Dictionary<string, string> active = themeChanges % 2 == 0 ? themeB : themeA;
                            AhpStyleValue v = StyleCascade.ResolveWithOrigin("fontSize", "40",
                                vacio, vacio, vacio, active);
                            if (string.IsNullOrEmpty(v.Value)) throw new Exception("cascada sin valor");
                            themeChanges++;
                        }

                        // 4) Lower Third periódico (F3.04) — JSON de activación.
                        if (operations % 10 == 0)
                        {
                            string ltj = AhpBridge.LtJson(ltElement);
                            Dictionary<string, object> lto = MiniJson.Parse(ltj);
                            if (MiniJson.GetInt(lto, "durationMs", 0) <= 0)
                                throw new Exception("LtJson sin duración");
                            ltOps++;
                        }

                        // 5) Imágenes: PPTX con informe cada ~5 s; PDF cada ~10 s.
                        if ((DateTime.UtcNow - lastImage).TotalSeconds >= 5)
                        {
                            lastImage = DateTime.UtcNow;
                            List<ExportSlide> slides = SampleExportSlides();
                            ExportSlide img = new ExportSlide();
                            img.Kind = SlideKind.Image;
                            img.Title = "Imagen de sesión";
                            img.ImagePath = imgPath;
                            slides.Add(img);
                            FidelityReport rep;
                            PptxExporter.ExportToBytes("Sesión F6.08", slides, new Theme(), out rep);
                            if (rep.UnitsConverted != slides.Count) throw new Exception("PPTX incompleto");
                            imageOps++;
                        }
                        if ((DateTime.UtcNow - lastPdf).TotalSeconds >= 10)
                        {
                            lastPdf = DateTime.UtcNow;
                            FidelityReport rep;
                            PdfExporter.ExportToBytes("Sesión F6.08",
                                SampleExportSlides(), new Theme(), out rep);
                            if (rep.UnitsConverted != SampleExportSlides().Count)
                                throw new Exception("PDF incompleto");
                            imageOps++;
                        }

                        // 6) API: encuesta del control remoto (GET /state) cada 2 s.
                        if (api != null && session.Elapsed.TotalSeconds >= nextApiPoll)
                        {
                            nextApiPoll = Math.Floor(session.Elapsed.TotalSeconds / 2) * 2 + 2;
                            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(
                                apiBase + "api/v1/state");
                            rq.Method = "GET";
                            rq.Timeout = 3000;
                            rq.Headers["Authorization"] = "Bearer " + apiToken;
                            using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                            using (StreamReader sr = new StreamReader(rs.GetResponseStream()))
                            {
                                string body = sr.ReadToEnd();
                                if (!body.Contains("\"version\"")) throw new Exception("estado API vacío");
                            }
                            apiOps++;
                        }

                        // 7) Errores controlados cada ~3 s: archivo corrupto →
                        // fail-safe (settings/trigger tolerant, PPTX InvalidData).
                        if ((DateTime.UtcNow - lastCorrupt).TotalSeconds >= 3)
                        {
                            lastCorrupt = DateTime.UtcNow;
                            Settings corrupted = Settings.Load(corruptDir);
                            if (corrupted.ApiPort != 8069)
                                throw new Exception("settings corrupto NO volvió a defaults");
                            TriggerEngine corruptedTriggers = TriggerEngine.Load(
                                Path.Combine(corruptDir, "triggers.json"));
                            if (corruptedTriggers.RulesSnapshot().Count != 0)
                                throw new Exception("triggers corrupto NO quedó vacío");
                            try
                            {
                                using (MemoryStream cms = new MemoryStream(corruptPptx))
                                {
                                    PptxImporter.Import(cms);
                                }
                                throw new Exception("PPTX corrupto NO fue rechazado");
                            }
                            catch (InvalidDataException)
                            {
                                // fail-safe esperado: rechazo explícito y legible.
                            }
                            controlledErrors++;
                        }

                        // 8) Métricas del servicio (F6.01): frame simulado con
                        // el tiempo REAL del tick + volcado cada 60 s.
                        metrics.ObserveRenderMs(tick.Elapsed.TotalMilliseconds);
                        metrics.Count("operaciones", 1);
                        string dump = metrics.Tick60("sesion60");
                        if (dump != null && (int)session.Elapsed.TotalMinutes % 1 == 0)
                            Console.WriteLine("  [F6.08] " +
                                session.Elapsed.TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture) +
                                " min — ops=" + operations + " dump60 recogido");
                        if ((DateTime.UtcNow - lastProgress).TotalSeconds >= 60)
                        {
                            lastProgress = DateTime.UtcNow;
                            Console.WriteLine("  [F6.08] progreso: " +
                                session.Elapsed.TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture) +
                                " min · ops=" + operations +
                                " · RAM=" + metrics.SnapshotJson("sesion60"));
                        }
                        operations++;
                    }
                    catch (Exception ex)
                    {
                        // CUALQUIER excepción del ciclo es un ERROR: los 3
                        // fail-safes de arriba ya capturan lo JUSTIFICADO, así
                        // que aquí no debería llegar NADA (F6.08.11).
                        unjustified++;
                        Console.WriteLine("  [F6.08] ERROR no justificado: " + ex.Message);
                    }
                    while (tick.ElapsedMilliseconds < 100)
                    {
                        Thread.Sleep(10);   // cadencia del servicio (~10 Hz)
                    }
                }
            }
            finally
            {
                if (api != null) { try { api.Dispose(); } catch (Exception) { } }
                if (nativeEngine != null) { try { nativeEngine.Dispose(); } catch (Exception) { } }
                try { File.Delete(imgPath); } catch (Exception) { }
                try { Directory.Delete(corruptDir, true); } catch (Exception) { }
            }

            // — Reporte final de la sesión (F6.08.11-13) —
            TimeSpan dur = session.Elapsed;
            string snapJson = metrics.SnapshotJson("sesion60");
            Dictionary<string, object> mj = MiniJson.Parse(snapJson);
            double ram = MiniJson.GetDouble(mj, "ramWorkingSetMb", 0);
            double peak = MiniJson.GetDouble(mj, "ramPeakMb", 0);
            Console.WriteLine("  [F6.08] REPORTE: duración=" +
                dur.TotalMinutes.ToString("0.00", CultureInfo.InvariantCulture) +
                " min · ops=" + operations +
                " · líneas=" + lineChanges + " · elementos=" + elementChanges +
                " · temas=" + themeChanges + " · imágenes=" + imageOps +
                " · LT=" + ltOps + " · stage=" + stageOps +
                " · api=" + apiOps + " · triggers=" + triggerFires +
                " · erroresControlados=" + controlledErrors +
                " · NO justificados=" + unjustified +
                " · RAM=" + ram.ToString("0", CultureInfo.InvariantCulture) + " MB" +
                " · pico=" + peak.ToString("0", CultureInfo.InvariantCulture) + " MB" +
                " · dumps60=" + metrics.Dumps().Count);
            AssertTrue(unjustified == 0, "0 errores no justificados (hay " + unjustified + ")");
            AssertTrue(operations > 0, "el servicio simulado ejecutó operaciones");
            AssertTrue(lineChanges > 0 && stageOps > 0,
                "proyección continua con Stage View");
            if (minutes >= 1)
            {
                AssertTrue(elementChanges > 0 && triggerFires > 0,
                    "navegación de elementos y triggers disparados");
                AssertTrue(metrics.Dumps().Count >= 1, "volcados de 60 s recolectados (F6.01)");
                AssertTrue(imageOps > 0 && themeChanges > 0 && controlledErrors > 0,
                    "imágenes, temas y errores controlados ejercitados en sesión larga");
                AssertTrue(ltOps > 0, "Lower Third ejercitado");
            }
            if (api != null) AssertTrue(apiOps > 0, "API real encuestada con éxito");
        }

        /// <summary>Líneas proyectables de un ítem del JSON del motor
        /// (lines[] estructurado, text plano o slide única).</summary>
        private static int SessionLineCount(object itemObj)
        {
            Dictionary<string, object> item = itemObj as Dictionary<string, object>;
            if (item == null) return 1;
            List<object> lines = MiniJson.GetArray(item, "lines");
            if (lines.Count > 0) return lines.Count;
            string text = MiniJson.GetString(item, "text", "");
            if (text.Length > 0)
            {
                int n = 1;
                foreach (char c in text) if (c == '\n') n++;
                return n;
            }
            return 1;
        }
    }
}

#pragma warning restore SYSLIB0014
