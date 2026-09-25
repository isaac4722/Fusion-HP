// ============================================================================
//  Fusion-HP · tests/managed/V21FeaturesTests.cs — pruebas de las funciones
// de la fusión v2.1: transposición de acordes (port betas 1), resaltado
// acento-insensible, base única de cantos con deduplicación, biblias
// completas empaquetadas y extensión del modelo (transición + resaltado).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Fusion.Shared;
using Fusion.Shared.Model;
using Fusion.Shared.Music;
using Fusion.Shared.Text;
using Fusion.Shared.Store;

namespace Fusion.Tests
{
    static class V21FeaturesTests
    {
        // ------------------------------------------------------------ acordes
        public static void TestChordsAngloTranspose()
        {
            TestRunner.CheckEq(Chords.TransposeChordToken("C", 2, false), "D", "C+2=D");
            TestRunner.CheckEq(Chords.TransposeChordToken("G", 5, false), "C", "G+5=C");
            TestRunner.CheckEq(Chords.TransposeChordToken("Am", 3, false), "Cm", "Am+3=Cm");
            TestRunner.CheckEq(Chords.TransposeChordToken("F#m7", -1, false), "Fm7", "F#m7-1=Fm7");
            TestRunner.CheckEq(Chords.TransposeChordToken("Bb", 2, false), "C", "Bb+2=C");
            TestRunner.CheckEq(Chords.TransposeChordToken("C/E", 4, false), "E/G#", "C/E+4=E/G#");
        }

        public static void TestChordsLatinTranspose()
        {
            TestRunner.CheckEq(Chords.TransposeChordToken("Do", 2, true), "Re", "Do+2=Re");
            TestRunner.CheckEq(Chords.TransposeChordToken("Sol", 5, true), "Do", "Sol+5=Do");
            TestRunner.CheckEq(Chords.TransposeChordToken("Lam", 2, true), "Sim", "Lam+2=Sim");
            TestRunner.CheckEq(Chords.TransposeChordToken("Sol/Fa", 2, true), "La/Sol", "Sol/Fa+2=La/Sol");
        }

        public static void TestChordsLineDetection()
        {
            TestRunner.Check(Chords.IsChordLine("C    G    Am    F"), "línea de cifrado anglo");
            TestRunner.Check(Chords.IsChordLine("Do   Sol  Lam"), "línea de cifrado latino");
            TestRunner.Check(!Chords.IsChordLine("Alabadle con cantos nuevos"), "letra NO es cifrado");
            TestRunner.Check(!Chords.IsChordLine("Él es Dios y rey"), "letra con acentos NO es cifrado");
        }

        public static void TestChordsColumnAlignment()
        {
            // El relleno conserva las columnas del cifrado
            string r = Chords.TransposeLine("C     G", 2, false);
            TestRunner.Check(r.StartsWith("D") && r.IndexOf("A") == 6, "columnas conservadas: '" + r + "'");
            TestRunner.CheckEq(Chords.TransposeKey("C", 2, false), "D", "tonalidad C+2");
            TestRunner.CheckEq(Chords.TransposeKey("Do", 1, true), "Do#", "tonalidad Do+1");
            TestRunner.Check(Chords.LooksLikeKey("Am"), "Am es tonalidad");
            TestRunner.Check(Chords.LooksLikeKey("Lam"), "Lam es tonalidad");
        }

        // ------------------------------------------------------------ resaltado
        public static void TestHighlightAccentInsensitive()
        {
            var segs = Highlight.Split("Porque tanto AMÓ Dios al mundo", "amo");
            bool any = segs.Any(s => s.Match);
            TestRunner.Check(any, "AMÓ coincide con 'amo' sin acentos");
        }

        public static void TestHighlightWordBoundary()
        {
            var segs = Highlight.Split("Diosas y Dios y diosdad", "Dios");
            int matches = segs.Count(s => s.Match);
            TestRunner.CheckEq(matches, 1, "frontera de palabra: solo 'Dios' único");
            foreach (var s in segs)
                TestRunner.Check(s.Text != null, "segmento verbatim");
        }

        public static void TestHighlightVerbatimText()
        {
            var segs = Highlight.Split("Él es DIOS sobre todo", "dios");
            var joined = string.Join("", segs.Select(s => s.Text).ToArray());
            TestRunner.CheckEq(joined, "Él es DIOS sobre todo", "el texto se reconstruye VERBATIM");
        }

        public static void TestHighlightMultipleWords()
        {
            var words = new List<string> { "Dios", "amor" };
            var segs = Highlight.Split("El amor de Dios es eterno", words);
            TestRunner.CheckEq(segs.Count(s => s.Match), 2, "dos coincidencias");
        }

        // ------------------------------------------------------------ SongStore
        static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "fhp-v21-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        public static void TestSongStoreSingleFileNoDuplication()
        {
            string dir = TempDir();
            try
            {
                var store = new SongStore(dir);
                var s1 = new Song { Title = "Gran Amor", Artist = "Grupo X" };
                s1.Sections.Add(new SongSection { Name = "Coro", Lines = { "Su amor es grande" } });
                store.Save(s1);
                store.Dispose();

                // UN solo archivo .fdb, cero .json por canto
                TestRunner.Check(File.Exists(Path.Combine(dir, "cancionero.fdb")), "cancionero.fdb existe");
                TestRunner.CheckEq(Directory.GetFiles(dir, "*.json").Length, 0, "sin archivos por canto");

                // Reabrir y re-importar el MISMO canto: actualiza, no duplica
                var store2 = new SongStore(dir);
                TestRunner.CheckEq(store2.Count, 1, "1 canto tras reabrir");
                string json = Path.Combine(dir, "reimport.json");
                Json.WriteFile(json, s1.ToJson());
                var rep = store2.ImportFiles(new[] { json });
                TestRunner.CheckEq(rep.Updated, 1, "re-importación = actualización");
                TestRunner.CheckEq(rep.Added, 0, "re-importación no agrega duplicados");
                TestRunner.CheckEq(store2.Count, 1, "sigue habiendo UN canto");
                store2.Dispose();
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static void TestSongStoreWebFormatImport()
        {
            string dir = TempDir();
            try
            {
                // formato de la referencia web: sections[{name,lines}]
                var web = JsonValue.Object();
                web.Set("title", JsonValue.Make("Santo Espíritu"));
                web.Set("artist", JsonValue.Make("Referencia"));
                var sections = JsonValue.Array();
                var sec = JsonValue.Object();
                sec.Set("name", JsonValue.Make("Verso 1"));
                var lines = JsonValue.Array();
                lines.Add(JsonValue.Make("Ven Espíritu Santo"));
                sec.Set("lines", lines);
                sections.Add(sec);
                web.Set("sections", sections);

                string f = Path.Combine(dir, "web.json");
                Json.WriteFile(f, web);
                var store = new SongStore(dir);
                var rep = store.ImportFiles(new[] { f });
                TestRunner.CheckEq(rep.Added, 1, "canto web importado");
                var song = store.Search("Santo").First();
                TestRunner.CheckEq(song.Sections[0].Lines[0], "Ven Espíritu Santo", "línea web leída");
                store.Dispose();
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        public static void TestSongStoreAtomicTempFile()
        {
            string dir = TempDir();
            try
            {
                var store = new SongStore(dir);
                store.Save(new Song { Title = "A", Artist = "x" });
                // el guardado atómico usa .tmp y nunca deja basura
                store.Dispose();
                TestRunner.Check(!File.Exists(Path.Combine(dir, "cancionero.fdb.tmp")),
                    "sin temporal residual (patrón PowerPoint)");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ------------------------------------------------------------ modelo v2.1
        public static void TestElementTransitionRoundTrip()
        {
            var el = new Element { Kind = ElementKind.Text, Transition = "slide" };
            el.Lines.Add("Hola");
            var j = el.ToJson();
            var back = Element.FromJson(j);
            TestRunner.CheckEq(back.Transition, "slide", "transition ida y vuelta");
        }

        public static void TestResolvedSlideSendsTransitionAndHighlight()
        {
            var p = AhpProject.CreateDefault();
            var scn = new Scenario { Title = "prueba" };
            var el = new Element { Kind = ElementKind.Verse, Transition = "fade" };
            el.Lines.Add("Porque de tal manera amó Dios");
            el.HighlightWords.Add("Dios");
            scn.Elements.Add(el);
            p.Scenarios.Add(scn);

            var r = ResolvedSlide.Resolve(el, scn, p, null);
            TestRunner.CheckEq(r.Transition, "fade", "transición resuelta");
            TestRunner.CheckEq(r.HighlightWords.Count, 1, "resaltado resuelto");
            var ipc = r.ToIpcJson();
            TestRunner.CheckEq(ipc.GetStr("transition"), "fade", "ipc.v1 lleva transition");
            TestRunner.CheckEq(ipc.GetArray("highlight").Count, 1, "ipc.v1 lleva highlight");
        }

        public static void TestDefaultThemesMatchWebReference()
        {
            var p = AhpProject.CreateDefault();
            TestRunner.CheckEq(p.Themes.Count, 6, "5 temas web + Calma");
            string[] names = p.Themes.Select(t => t.Name).ToArray();
            TestRunner.Check(names.Contains("Clásico lumínico") && names.Contains("Escritura") &&
                             names.Contains("Broadcast") && names.Contains("Moderno liviano") &&
                             names.Contains("Solemne"), "nombres de la referencia presentes");
            var clasico = p.Themes.First(t => t.Name == "Clásico lumínico");
            TestRunner.CheckEq(clasico.Style.Font, "Outfit", "fuente Outfit (incrustada)");
            TestRunner.CheckEq(clasico.Style.ActiveColor, "#E8C872", "acento dorado de la web");
            TestRunner.Check(clasico.Background.Image != null && clasico.Background.Image.StartsWith("app:"),
                "fondo empaquetado con prefijo app:");
        }

        public static void TestClearModeSettings()
        {
            // Ajustes nuevos de la referencia web
            var s = new AppSettings();
            TestRunner.CheckEq(s.AdvanceMode, "line", "avance por defecto: línea");
            TestRunner.Check(s.ShowClock, "reloj visible por defecto");
            TestRunner.Check(s.Animation, "animaciones por defecto");
            TestRunner.CheckEq(s.DefaultTransition, "fade", "transición por defecto: fade");
        }

        // ------------------------------------------------------------ biblias
        public static void TestBundledBiblesImportable()
        {
            // localiza resources/data/bibles relativo al exe de pruebas
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dir = baseDir;
            for (int i = 0; i < 6 && !Directory.Exists(Path.Combine(dir, "resources")); i++)
                dir = Path.GetDirectoryName(dir);
            string bibles = Path.Combine(Path.Combine(Path.Combine(dir, "resources"), "data"), "bibles");
            if (!Directory.Exists(bibles))
            {
                TestRunner.Check(false, "carpeta resources/data/bibles no hallada desde " + baseDir);
                return;
            }
            string[] files = Directory.GetFiles(bibles, "*.json");
            TestRunner.CheckEq(files.Length, 4, "4 biblias empaquetadas (RV1960, NVI, RVG, RVR1909)");

            string dataDir = TempDir();
            try
            {
                var store = new Shared.Bible.BibleStore(dataDir);
                foreach (string f in files)
                {
                    var rep = store.ImportJson(f);
                    TestRunner.Check(rep.Ok, Path.GetFileName(f) + " importable: " + rep.Verses + " versos");
                    TestRunner.Check(rep.Verses > 30000, Path.GetFileName(f) + " biblia COMPLETA (>" + 30000 + ")");
                }
                var list = store.List();
                TestRunner.CheckEq(list.Count, 4, "4 biblias instaladas");
                // dedupe: reinstalar no duplica
                store.EnsureBundled(bibles);
                TestRunner.CheckEq(store.List().Count, 4, "EnsureBundled no duplica");

                // texto verbatim: Juan 3:16 legible en las 4
                foreach (var b in list)
                {
                    var jn = Shared.Bible.BookNames.Find("Juan");
                    var passage = store.GetPassage(b, new Shared.Bible.BibleReference { Book = jn, Chapter = 3, VerseStart = 16, VerseEnd = 16 });
                    TestRunner.Check(passage != null && passage.Count > 0, b.Name + " Juan 3:16 presente");
                    if (passage != null && passage.Count > 0)
                        TestRunner.Check(passage[0].ToLowerInvariant().Contains("am"),
                            b.Name + " Jn3:16 contiene 'amó': " + passage[0].Substring(0, Math.Min(40, passage[0].Length)));
                }
            }
            finally { try { Directory.Delete(dataDir, true); } catch { } }
        }
    }
}
