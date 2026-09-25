// ============================================================================
//  Fusion-HP · tests/managed/PlatformTests.cs — API HTTP (6 endpoints + 401),
//  Triggers, exportadores PPTX/PDF/imágenes y JSON de canciones.
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using Fusion.Shared;
using Fusion.Shared.Model;
using Fusion.Studio.Export;
using Fusion.Studio.Import;
using Fusion.Studio.Services;
using Fusion.Tests;

namespace Fusion.Tests
{
    static class PlatformTests
    {
        // ---------------------------------------------------------------- API
        class TestLive : LiveOrchestrator
        {
            public TestLive(AppSettings s) : base(s) { }
        }

        public static void TestApiServerEndpoints()
        {
            // Criterio F5 [SPEC §12.3.6]: 6 endpoints responden con token y 401 sin él
            string dir = TestRunner.TempDir();
            var settings = new AppSettings { DataDir = dir, ApiPort = 27999 + new Random().Next(200), ApiToken = "TESTTOKEN123" };
            var live = new LiveOrchestrator(settings);
            live.Project = AhpProject.CreateDefault();
            var scn = new Scenario { Title = "Canto API" };
            scn.Elements.Add(new Element { Kind = ElementKind.Text, Lines = { "línea uno", "línea dos" } });
            live.Project.Scenarios.Add(scn);
            live.Select(0, 0, 0);

            var api = new ApiServer(live, settings);
            api.Start();
            try
            {
                TestRunner.Check(api.Running, "API corriendo");
                string baseUri = "http://127.0.0.1:" + settings.ApiPort;

                // sin token → 401 [SPEC §8.1.4]
                using (var c = new WebClient())
                {
                    try
                    {
                        c.DownloadString(baseUri + "/api/v1/state");
                        TestRunner.Check(false, "debe rechazar sin token");
                    }
                    catch (WebException we)
                    {
                        var resp = (HttpWebResponse)we.Response;
                        TestRunner.CheckEq((int)resp.StatusCode, 401, "401 sin token");
                    }
                }

                // con token → 200
                string state;
                using (var c = new WebClient())
                {
                    c.Headers["Authorization"] = "Bearer " + settings.ApiToken;
                    c.Encoding = Encoding.UTF8;   // sin charset en la respuesta, WebClient asume Latin-1
                    state = c.DownloadString(baseUri + "/api/v1/state");
                }
                var sj = JsonValue.Parse(state);
                TestRunner.CheckEq(sj.GetStr("scenarioTitle"), "Canto API", "state con título");
                TestRunner.CheckEq(sj.GetStr("text"), "línea uno", "state con línea activa");

                // /api/v1/text?format=plain (para OBS) [SPEC §8.4.3]
                using (var c = new WebClient())
                {
                    c.Headers["Authorization"] = "Bearer " + settings.ApiToken;
                    c.Encoding = Encoding.UTF8;
                    string plain = c.DownloadString(baseUri + "/api/v1/text?format=plain");
                    TestRunner.CheckEq(plain, "línea uno", "texto plano para OBS");
                    string json = c.DownloadString(baseUri + "/api/v1/text?format=json");
                    TestRunner.CheckEq(JsonValue.Parse(json).GetStr("text"), "línea uno", "json");
                }

                // POST next
                using (var c = new WebClient())
                {
                    c.Headers["Authorization"] = "Bearer " + settings.ApiToken;
                    c.Headers[HttpRequestHeader.ContentType] = "application/json";
                    c.UploadString(baseUri + "/api/v1/next", "POST", "");
                    string st2 = c.DownloadString(baseUri + "/api/v1/state");
                    TestRunner.CheckEq(JsonValue.Parse(st2).GetStr("text"), "línea dos", "next avanzó línea");
                    // POST prev
                    c.UploadString(baseUri + "/api/v1/prev", "POST", "");
                    string st3 = c.DownloadString(baseUri + "/api/v1/state");
                    TestRunner.CheckEq(JsonValue.Parse(st3).GetStr("text"), "línea uno", "prev retrocedió");
                    // POST goto
                    c.UploadString(baseUri + "/api/v1/goto", "POST", "{\"scenario\":0,\"element\":0,\"line\":1}");
                    string st4 = c.DownloadString(baseUri + "/api/v1/state");
                    TestRunner.CheckEq(JsonValue.Parse(st4).GetInt("line", -1), 1, "goto por índice");
                    // POST message
                    c.UploadString(baseUri + "/api/v1/message", "POST", "{\"text\":\"Aviso de prueba\"}");
                    TestRunner.Check(live.CurrentElement != null && live.CurrentElement.OverlayText == "Aviso de prueba", "mensaje en overlay");
                }
            }
            finally
            {
                api.Stop();
            }
        }

        // ---------------------------------------------------------------- triggers
        public static void TestTriggerEtiquetaATema()
        {
            // Caso canónico: etiqueta "lento" → tema "calma" [SPEC §5.1.4, §12.3.6]
            string dir = TestRunner.TempDir();
            var settings = new AppSettings { DataDir = dir };
            var live = new LiveOrchestrator(settings);
            live.Project = AhpProject.CreateDefault();   // ya trae tema-calma
            var te = new TriggerEngine();
            var rule = new TriggerRule { Event = "tag", Tag = "lento", Action = "theme.change", Parameter = "Calma" };
            te.Add(rule);
            string changed = null;
            te.ThemeChanger = delegate(string n) { changed = n; };
            var scn = new Scenario { Title = "x" };
            scn.Tags.Add("lento");
            var el = new Element { Lines = { "x" } };
            scn.Elements.Add(el);
            te.Fire("tag", scn, el, live);
            TestRunner.CheckEq(live.Project.ThemeRef, "tema-calma", "tema activado por etiqueta");
            TestRunner.Check(changed == "Calma", "callback del tema: " + changed);
        }

        public static void TestTriggerObsScene()
        {
            var te = new TriggerEngine();
            te.Add(new TriggerRule { Event = "tag", Tag = "rápido", Action = "obs.scene", Parameter = "Escena Adboracion" });
            bool called = false;
            te.ObsSceneChanger = delegate(string s) { called = s == "Escena Adboracion"; return true; };
            var scn = new Scenario { Title = "x" };
            scn.Tags.Add("rápido");
            te.Fire("tag", scn, null, null);
            TestRunner.Check(called, "acción OBS ejecutada");
            // sin etiqueta → no dispara
            bool called2 = false;
            te.ObsSceneChanger = delegate { called2 = true; return true; };
            te.Fire("tag", new Scenario { Title = "y" }, null, null);
            TestRunner.Check(!called2, "sin coincidencia no dispara");
        }

        // ---------------------------------------------------------------- exportadores
        public static void TestPptxExporter()
        {
            var p = AhpProject.CreateDefault();
            var scn = new Scenario { Title = "Export" };
            scn.Elements.Add(new Element { Kind = ElementKind.Text, Lines = { "uno", "dos", "tres" } });
            p.Scenarios.Add(scn);
            string dir = TestRunner.TempDir();
            string outPath = Path.Combine(dir, "t.pptx");
            var rep = new PptxExporter().Export(p, outPath, dir);
            TestRunner.Check(rep.Slides == 3, "una diapositiva por línea: " + rep.Slides);
            TestRunner.Check(File.Exists(outPath), "archivo existe");
            // el contenedor es un ZIP OPC válido con [Content_Types].xml
            using (var zip = ZipFile.OpenRead(outPath))
            {
                bool hasTypes = false, hasPresentation = false, hasSlide = false;
                foreach (var e2 in zip.Entries)
                {
                    if (e2.FullName == "[Content_Types].xml") hasTypes = true;
                    if (e2.FullName == "ppt/presentation.xml") hasPresentation = true;
                    if (e2.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal)) hasSlide = true;
                }
                TestRunner.Check(hasTypes, "[Content_Types].xml");
                TestRunner.Check(hasPresentation, "presentation.xml");
                TestRunner.Check(hasSlide, "slides/");
                TestRunner.Check(zip.Entries.Count >= 5, "entradas: " + zip.Entries.Count);
            }
            // reimportable por el importador OpenXML
            var p2 = AhpProject.CreateDefault();
            var rep2 = new PptxOpenXmlImporter().Import(outPath, p2);
            TestRunner.Check(rep2.Scenarios >= 1, "reimportado: " + rep2.Scenarios);
        }

        public static void TestPdfExporter()
        {
            var p = AhpProject.CreateDefault();
            var scn = new Scenario { Title = "PDF" };
            scn.Elements.Add(new Element { Kind = ElementKind.Verse, Reference = "Juan 3:16", Lines = { "texto del verso" } });
            p.Scenarios.Add(scn);
            string dir = TestRunner.TempDir();
            string outPath = Path.Combine(dir, "t.pdf");
            int pages = new PdfExporter().Export(p, outPath, dir);
            TestRunner.CheckEq(pages, 1, "una página");
            var bytes = File.ReadAllBytes(outPath);
            string head = Encoding.ASCII.GetString(bytes, 0, 5);
            TestRunner.CheckEq(head, "%PDF-", "cabecera PDF");
            string tail = Encoding.ASCII.GetString(bytes, bytes.Length - 8, 8);
            TestRunner.Check(tail.Contains("%%EOF"), "EOF");
            TestRunner.Check(bytes.Length > 800, "tamaño razonable: " + bytes.Length);
        }

        public static void TestImageExporter()
        {
            var p = AhpProject.CreateDefault();
            var scn = new Scenario { Title = "IMG" };
            scn.Elements.Add(new Element { Kind = ElementKind.Text, Lines = { "una" } });
            scn.Elements.Add(new Element { Kind = ElementKind.Text, Lines = { "dos" } });
            p.Scenarios.Add(scn);
            string dir = TestRunner.TempDir();
            string outPath = Path.Combine(dir, "t.png");
            int n = new ImageExporter().Export(p, outPath, dir);
            TestRunner.CheckEq(n, 2, "dos imágenes");
            string f1 = Path.Combine(dir, "t-001.png");
            TestRunner.Check(File.Exists(f1), "png 1 existe");
            using (var img = System.Drawing.Image.FromFile(f1))
            {
                TestRunner.CheckEq(img.Width, 1920, "ancho 1920 [SPEC §9.3.3]");
                TestRunner.CheckEq(img.Height, 1080, "alto 1080");
            }
        }
    }
}
