// ============================================================================
//  Fusion-HP · tests/managed/JsonModelTests.cs — JSON propio, modelo ahp.v1,
//  herencia de 4 niveles y contrato ipc.
// ============================================================================
using System;
using System.IO;
using System.Linq;
using Fusion.Shared;
using Fusion.Shared.Model;
using Fusion.Tests;

namespace Fusion.Tests
{
    static class JsonModelTests
    {
        public static void TestJsonRoundTrip()
        {
            var src = "{\"nombre\":\"Culto\",\"n\":42,\"ok\":true,\"lista\":[1,2,3],\"anidado\":{\"x\":\"ñá\"}}";
            var v = JsonValue.Parse(src);
            TestRunner.CheckEq(v.GetStr("nombre"), "Culto", "string");
            TestRunner.CheckEq(v.GetInt("n", 0), 42, "entero");
            TestRunner.Check(v.GetBool("ok", false), "bool");
            TestRunner.CheckEq(v.GetArray("lista").Count, 3, "arreglo");
            TestRunner.CheckEq(v.Get("anidado").GetStr("x"), "ñá", "unicode");
            var out2 = JsonValue.Parse(v.ToJsonString());
            TestRunner.CheckEq(out2.GetStr("nombre"), "Culto", "re-parse");
        }

        public static void TestJsonEscapes()
        {
            var s = "línea1\nlínea2 \"comillas\" \\slash\\ \t tab";
            var v = JsonValue.Object().Set("t", JsonValue.Make(s));
            var back = JsonValue.Parse(v.ToJsonString()).GetStr("t");
            TestRunner.CheckEq(back, s, "escapes");
            var j2 = JsonValue.Parse(" { \"a\" : \"b\" , \"c\" : [ ] } ");
            TestRunner.CheckEq(j2.GetStr("a"), "b", "espacios");
            TestRunner.Check(j2.GetArray("c").Count == 0, "arreglo vacío");
        }

        public static void TestJsonNumeros()
        {
            var v = JsonValue.Parse("{\"a\":1.5,\"b\":-3,\"c\":1e3}");
            TestRunner.CheckEq(v.GetNum("a", 0), 1.5, "decimal");
            TestRunner.CheckEq(v.GetInt("b", 0), -3, "negativo");
            TestRunner.CheckEq(v.GetNum("c", 0), 1000, "exponente");
        }

        public static void TestAhpRoundTrip()
        {
            var p = AhpProject.CreateDefault();
            p.Name = "Prueba RT";
            var scn = new Scenario { Id = "scn-x", Title = "Canto" };
            scn.Tags.Add("lento");
            var el = new Element { Id = "el-x", Kind = ElementKind.Text };
            el.Lines.Add("Línea uno");
            el.Lines.Add("Línea dos");
            el.SyncMarks.Add(0);
            el.SyncMarks.Add(12.5);
            scn.Elements.Add(el);
            p.Scenarios.Add(scn);

            string dir = TestRunner.TempDir();
            string path = Path.Combine(dir, "t.ahp");
            Json.WriteFile(path, p.ToJson());
            var p2 = AhpProject.FromJson(Json.ParseFile(path));
            TestRunner.CheckEq(p2.Name, "Prueba RT", "nombre");
            TestRunner.CheckEq(p2.Scenarios.Count, 1, "escenarios");
            TestRunner.CheckEq(p2.Scenarios[0].Elements[0].Id, "el-x", "ID estable");
            TestRunner.CheckEq(p2.Scenarios[0].Elements[0].Lines[1], "Línea dos", "líneas");
            TestRunner.CheckEq(p2.Scenarios[0].Tags[0], "lento", "tags");
            TestRunner.CheckEq(p2.ThemeRef, "tema-calma", "tema");
        }

        public static void TestHerencia4Niveles()
        {
            // Tema → Plantilla → Escenario → Elemento [SPEC §5.4]
            var p = AhpProject.CreateDefault();   // tema: fuente Segoe UI, 48, blanco, dorado
            var tpl = new ScenarioTemplate { Id = "tpl-1", Name = "T1" };
            tpl.Style.Size = 40;                  // nivel 2 baja a 40
            tpl.Style.Color = "#FFEEBB";
            p.Templates.Add(tpl);
            var scn = new Scenario { Id = "s", Title = "S", TemplateId = "tpl-1" };
            scn.StyleOverride.Color = "#FFDDAA";  // nivel 3 cambia color
            var el = new Element { Id = "e", Kind = ElementKind.Text };
            el.Lines.Add("x");
            el.StyleOverride.Bold = true;         // nivel 4 solo negrita
            scn.Elements.Add(el);
            p.Scenarios.Add(scn);

            var r = ResolvedSlide.Resolve(el, scn, p, ".");
            TestRunner.CheckEq(r.Style.Size ?? 0, 40, "tamaño heredado de plantilla");
            TestRunner.CheckEq(r.Style.Color, "#FFDDAA", "color del escenario gana");
            TestRunner.CheckEq(r.Style.Bold, true, "negrita del elemento gana");
            TestRunner.CheckEq(r.Style.Font, "Segoe UI", "fuente del tema como base");
            TestRunner.CheckEq(r.Style.ActiveColor, "#FFD700", "línea activa del tema");
        }

        public static void TestTemaEnCaliente()
        {
            // Cambiar el tema con la salida activa re-resuelve [SPEC §7.4.1, criterio F2]
            var p = AhpProject.CreateDefault();
            var scn = new Scenario { Id = "s", Title = "S" };
            var el = new Element { Id = "e", Kind = ElementKind.Text };
            el.Lines.Add("x");
            scn.Elements.Add(el);
            p.Scenarios.Add(scn);
            var r1 = ResolvedSlide.Resolve(el, scn, p, ".");
            var tema = p.ActiveTheme();
            tema.Style.Color = "#00FFCC";
            var r2 = ResolvedSlide.Resolve(el, scn, p, ".");
            TestRunner.CheckEq(r1.Style.Color, "#FFFFFF", "antes");
            TestRunner.CheckEq(r2.Style.Color, "#00FFCC", "después");
        }

        public static void TestIpcContract()
        {
            var p = AhpProject.CreateDefault();
            var scn = new Scenario { Id = "s", Title = "S" };
            var el = new Element { Id = "e", Kind = ElementKind.Verse, Reference = "Juan 3:16" };
            el.Lines.Add("versículo");
            el.OverlayText = "aviso";
            scn.Elements.Add(el);
            p.Scenarios.Add(scn);
            var r = ResolvedSlide.Resolve(el, scn, p, ".");
            var j = r.ToIpcJson();
            TestRunner.CheckEq(j.GetStr("kind"), "verse", "kind");
            TestRunner.CheckEq(j.GetStr("reference"), "Juan 3:16", "referencia");
            TestRunner.CheckEq(j.GetInt("activeLine", -1), 0, "línea");
            var st = j.Get("style");
            TestRunner.CheckEq(st.GetStr("font"), "Segoe UI", "fuente resuelta");
            TestRunner.Check(j.Get("overlay").GetBool("present", false), "overlay presente");
            // El núcleo C++ parsea exactamente este contrato (ver CoreTests TestSlideStateParse)
        }

        public static void TestSongFromHimnario()
        {
            var j = Json.ParseFile(TestRunner.FixturePath("himnario.json"));
            var s = Song.FromJson(j);
            TestRunner.Check(s != null, "canto parseado");
            TestRunner.CheckEq(s.Title, "HIMNO DE BIENVENIDA", "título");
            TestRunner.Check(s.Sections.Count >= 4, "secciones: " + s.Sections.Count);
            TestRunner.CheckEq(s.Sections[1].Name, "Coro", "coro");
            // conversión a escenario proyectable
            var scn = s.ToScenario();
            TestRunner.CheckEq(scn.Elements.Count, s.Sections.Count, "un elemento por sección");
            TestRunner.Check(scn.Elements[0].Lines.Count > 0, "líneas");
        }

        public static void TestBibleReferenceParse()
        {
            var r = Fusion.Shared.Bible.BibleReference.Parse("Juan 3:16");
            TestRunner.Check(r.Valid, "válida");
            TestRunner.CheckEq(r.Book.Number, 43, "juan=43");
            TestRunner.CheckEq(r.Chapter, 3, "cap");
            TestRunner.CheckEq(r.ToString(), "Juan 3:16", "tostring");
            var r2 = Fusion.Shared.Bible.BibleReference.Parse("1 Co 13:4-7");
            TestRunner.CheckEq(r2.Book.Number, 46, "1 Corintios");
            TestRunner.CheckEq(r2.VerseEnd, 7, "rango");
            var r3 = Fusion.Shared.Bible.BibleReference.Parse("sal 23");
            TestRunner.CheckEq(r3.Book.Number, 19, "salmos por alias");
        }
    }
}
