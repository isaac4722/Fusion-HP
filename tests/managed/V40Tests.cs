// ============================================================================
//  Fusion-HP · tests/managed/V40Tests.cs — pruebas de la v4.0.0:
//   · producto 100 % local  → settings.json SIN claves de red (api/obs);
//   · 4 biblias completas   → RV1960, NVI, RVG y RVR1909 con 66 libros y
//     ~31 000 versículos cada una, dentro de resources/data/bibles;
//   · semántica de temas    → cambio de tema activo resuelto en caliente
//     (paridad de la pestaña «Temas» de la web);
//   · favoritos de la Biblia rápida → las 12 citas favoritas de la web
//     parsean todas (tecla G).
//  Descubiertas por reflexión (TestRunner): métodos públicos estáticos Test*.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using Fusion.Shared;
using Fusion.Shared.Bible;
using Fusion.Shared.Model;
using Fusion.Tests;

namespace Fusion.Tests
{
    static class V40Tests
    {
        // -------------------------------------------------- 100 % local
        public static void TestSettingsCarryNoNetworkKeys()
        {
            // El producto no habla por la red: settings.json no puede llevar
            // rastro de API/OBS (decisión del usuario, v4.0.0).
            string dir = TestRunner.TempDir();
            var s = new AppSettings { DataDir = dir };
            s.PushRecent("x.ahp");
            s.Save();
            string json = File.ReadAllText(Path.Combine(dir, "settings.json"));
            string low = json.ToLowerInvariant();
            TestRunner.Check(!low.Contains("api"), "settings.json sin clave api");
            TestRunner.Check(!low.Contains("obs"), "settings.json sin clave obs");
            TestRunner.Check(!low.Contains("token"), "settings.json sin token");
            TestRunner.Check(!low.Contains("remote"), "settings.json sin remote");
            // y sí conserva las claves locales que importan
            TestRunner.Check(low.Contains("publicmonitor"), "monitor público persiste");
            TestRunner.Check(low.Contains("recentprojects"), "recientes persisten");
        }

        // -------------------------------------------------- biblias completas
        static string RepoResourcesDir()
        {
            for (string d = AppDomain.CurrentDomain.BaseDirectory; d != null && d.Length > 3;
                 d = Path.GetDirectoryName(d))
            {
                string cand = Path.Combine(d, "resources");
                if (Directory.Exists(cand)) return cand;
            }
            return null;
        }

        static int CountVerses(JsonValue bible)
        {
            int n = 0;
            var books = bible.GetArray("books");
            if (books != null)
                foreach (var b in books)
                {
                    var chs = b.GetArray("chapters");
                    if (chs == null) continue;
                    foreach (var c in chs)
                        foreach (var v in c.AsArray) n++;
                }
            return n;
        }

        public static void TestBundledBiblesAreComplete()
        {
            string res = RepoResourcesDir();
            TestRunner.Check(res != null, "carpeta resources localizada");
            if (res == null) return;
            string dir = Path.Combine(Path.Combine(res, "data"), "bibles");
            string[] expected = { "rv1960", "nvi", "rvg", "rvr1909" };
            string[] names = { "RV1960", "NVI", "RVG", "RVR1909" };
            for (int i = 0; i < expected.Length; i++)
            {
                string p = Path.Combine(dir, expected[i] + ".json");
                TestRunner.Check(File.Exists(p), names[i] + " empaquetada");
                if (!File.Exists(p)) continue;
                var j = JsonValue.Parse(File.ReadAllText(p));
                var books = j.GetArray("books");
                TestRunner.Check(books != null && books.Count == 66, names[i] + ": 66 libros");
                int verses = CountVerses(j);
                TestRunner.Check(verses >= 30000, names[i] + ": ~31 000 versículos (" + verses + ")");
            }
        }

        // -------------------------------------------------- temas en caliente
        public static void TestThemeHotSwapSemantics()
        {
            var p = AhpProject.CreateDefault();
            TestRunner.CheckEq(p.Themes.Count, 6, "6 temas web (5 + Calma)");
            TestRunner.CheckEq(p.ThemeRef, "tema-clasico", "tema activo inicial");
            var scn = new Scenario { Title = "t" };
            var el = new Element { Kind = ElementKind.Text, Lines = { "línea" } };
            scn.Elements.Add(el);
            p.Scenarios.Add(scn);

            string baseDir = TestRunner.TempDir();
            var r1 = ResolvedSlide.Resolve(el, scn, p, baseDir);
            string font1 = r1.Style.Font;
            TestRunner.CheckEq(font1, "Outfit", "fuente del tema clásico");

            // paridad de «Aplicar a todo»: ThemeRef + limpiar anulaciones
            p.ThemeRef = "tema-solemne";
            foreach (var s2 in p.Scenarios)
            {
                s2.StyleOverride = new StyleOverride();
                s2.BgOverride = new BackgroundOverride();
                foreach (var e2 in s2.Elements) { e2.StyleOverride = new StyleOverride(); e2.BgOverride = new BackgroundOverride(); }
            }
            var r2 = ResolvedSlide.Resolve(el, scn, p, baseDir);
            TestRunner.CheckEq(r2.Style.Font, "Cormorant Garamond", "cambio resuelto en caliente");

            // paridad de «Al elemento»: clonar estilo del tema en el elemento
            var solemne = p.ActiveTheme();
            el.StyleOverride = solemne.Style.Clone();
            p.ThemeRef = "tema-clasico";
            var r3 = ResolvedSlide.Resolve(el, scn, p, baseDir);
            TestRunner.CheckEq(r3.Style.Font, "Cormorant Garamond", "la anulación del elemento manda sobre el tema");
        }

        // -------------------------------------------------- favoritos (tecla G)
        public static void TestQuickVerseFavoritesParse()
        {
            string[] favorites = {
                "Juan 3:16", "Salmos 23:1-6", "Filipenses 4:13", "Jeremías 29:11",
                "Romanos 8:28", "Isaías 41:10", "Proverbios 3:5-6", "Juan 14:6",
                "Efesios 2:8", "Números 6:24-26", "Mateo 11:28", "Salmos 119:105"
            };
            foreach (string f in favorites)
                TestRunner.Check(BibleReference.Parse(f).Valid, "favorito parsea: " + f);
        }
    }
}
