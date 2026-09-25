// ============================================================================
//  Fusion-HP · tests/managed/V30Tests.cs — pruebas de la reestructuración
//  v3.0.0 (bugs corregidos del prototipo):
//   · cargador de Escenarios con nombres  → AhpProjectInfo (bug #1);
//   · OBS WebSocket restaurado           → hash de autenticación obs-websocket
//     5.x con vector precalculado (bug #4, SPEC §8.4);
//   · PPTX original tal cual             → PptxDirectProjector proyecta el
//     archivo fuente sin PowerPoint y sin escribir proyectos (bug #5);
//   · flujo Holyrics sin PPTX            → proyectar un canto NO genera
//     archivos .pptx ni convertir nada (bug #7).
//  Descubiertas por reflexión (TestRunner): métodos públicos estáticos Test*.
// ============================================================================
using System;
using System.IO;
using Fusion.Shared;
using Fusion.Shared.Model;
using Fusion.Studio.Export;
using Fusion.Studio.Import;
using Fusion.Studio.Services;
using Fusion.Tests;

namespace Fusion.Tests
{
    static class V30Tests
    {
        // ------------------------------------------------------------ bug #1
        public static void TestAhpProjectInfoHeader()
        {
            var p = AhpProject.CreateDefault();
            p.Name = "Culto Domingo 10am";
            var s1 = new Scenario { Title = "Grande es Tu Fidelidad" };
            s1.Elements.Add(new Element { Kind = ElementKind.Text, Lines = { "una", "dos" } });
            var s2 = new Scenario { Title = "Juan 3:16" };
            s2.Elements.Add(new Element { Kind = ElementKind.Verse, Lines = { "Porque de tal manera amó Dios…" } });
            p.Scenarios.Add(s1);
            p.Scenarios.Add(s2);
            string path = Path.Combine(TestRunner.TempDir(), "info.ahp");
            Json.WriteFile(path, p.ToJson());

            var info = AhpProjectInfo.ReadHeader(path);
            TestRunner.CheckEq(info.Name, "Culto Domingo 10am", "nombre del proyecto");
            TestRunner.CheckEq(info.ScenarioCount, 2, "cantidad de escenarios");
            TestRunner.CheckEq(info.ScenarioTitles[0], "Grande es Tu Fidelidad", "título 1");
            TestRunner.CheckEq(info.ScenarioTitles[1], "Juan 3:16", "título 2");
        }

        public static void TestAhpProjectInfoRejectsInvalid()
        {
            string dir = TestRunner.TempDir();
            string bad = Path.Combine(dir, "malo.ahp");
            File.WriteAllText(bad, "{ \"format\": \"otro.v9\", \"project\": {} }");
            bool threw = false;
            try { AhpProjectInfo.ReadHeader(bad); }
            catch (InvalidDataException) { threw = true; }
            TestRunner.Check(threw, "formato no ahp.v1 → InvalidDataException");

            string roto = Path.Combine(dir, "roto.ahp");
            File.WriteAllText(roto, "{ esto no es json");
            threw = false;
            try { AhpProjectInfo.ReadHeader(roto); }
            catch (InvalidDataException) { threw = true; }
            TestRunner.Check(threw, "JSON roto → InvalidDataException con mensaje humano");
        }

        public static void TestRecentProjectsPush()
        {
            var s = new AppSettings { DataDir = TestRunner.TempDir() };
            for (int i = 0; i < 15; i++) s.PushRecent("p" + i + ".ahp");
            TestRunner.CheckEq(s.RecentProjects.Count, 12, "recientes limitados a 12");
            TestRunner.CheckEq(s.RecentProjects[0], "p14.ahp", "el más reciente primero");
            s.PushRecent("p10.ahp");
            TestRunner.CheckEq(s.RecentProjects[0], "p10.ahp", "reabierto sube al frente");
            TestRunner.CheckEq(s.RecentProjects.Count, 12, "sin duplicados ni crecimiento");
        }

        // ------------------------------------------------------------ bug #4
        public static void TestObsAuthHash()
        {
            // Vector precalculado (obs-websocket 5.x): base64(sha256(
            //   base64(sha256(password + salt)) + challenge ))
            string hash = ObsClient.AuthHash("s3cr3t!", "salt123", "challenge456");
            TestRunner.CheckEq(hash, "b8gcoKWKnLgJIb7zI9P73PQEAraKQkLW8kvEU026PcQ=",
                "hash de autenticación obs-websocket 5.x");
            // casos borde sin lanzar
            string empty = ObsClient.AuthHash("", "", "");
            TestRunner.Check(!string.IsNullOrEmpty(empty), "hash con entradas vacías");
        }

        // ------------------------------------------------------------ bug #5
        public static void TestPptxDirectProjection()
        {
            // 1) fabricar un PPTX real con el exportador (contenedor OPC válido)
            var src = AhpProject.CreateDefault();
            var scn = new Scenario { Title = "Diapositiva directa" };
            scn.Elements.Add(new Element { Kind = ElementKind.Text, Lines = { "uno", "dos", "tres" } });
            src.Scenarios.Add(scn);
            string dir = TestRunner.TempDir();
            string pptx = Path.Combine(dir, "directo.pptx");
            new PptxExporter().Export(src, pptx, dir);

            // 2) proyectarlo TAL CUAL sin PowerPoint: motor + fuente externa
            var settings = new AppSettings { DataDir = Path.Combine(dir, "datos") };
            var live = new LiveOrchestrator(settings);
            var rep = PptxDirectProjector.ProjectOriginal(pptx, live);
            TestRunner.Check(rep.Slides >= 1, "diapositivas del original: " + rep.Slides);
            TestRunner.CheckEq(rep.SourcePath, pptx, "fuente = archivo original");
            TestRunner.CheckEq(live.ExternalSource, pptx, "ExternalSource = original");
            TestRunner.Check(live.Project != null && live.Project.Scenarios.Count >= 1,
                "el programa en vivo proviene del PPTX");
            // nada se guardó como proyecto del usuario: no .ahp en el dir del archivo
            bool ahp = false;
            foreach (string f in Directory.GetFiles(dir))
                if (f.EndsWith(".ahp", StringComparison.OrdinalIgnoreCase)) ahp = true;
            TestRunner.Check(!ahp, "sin conversión persistente: cero .ahp generados");
            // los medios (si los hubo) van a la caché técnica, nunca al proyecto
            TestRunner.Check(Directory.Exists(Path.Combine(settings.DataDir,
                Path.Combine("media-cache"))), "caché técnica de medios");
        }

        // ------------------------------------------------------------ bug #7
        public static void TestProjectionDoesNotGeneratePptx()
        {
            // Flujo Holyrics: cargar cantos desde la BD y SOLO proyectar.
            string dir = TestRunner.TempDir();
            var settings = new AppSettings { DataDir = dir };
            var live = new LiveOrchestrator(settings);
            int before;
            {
                var dirs = new System.Collections.Generic.List<string>();
                Snapshot(dir, dirs);
                before = dirs.Count;
            }
            var song = new Fusion.Shared.Model.Song { Title = "Sublime Gracia" };
            song.Sections.Add(new Fusion.Shared.Model.SongSection
            {
                Name = "Verso 1",
                Lines = { "Sublime gracia de un Salvador", "que salvó a un infeliz como yo" }
            });
            live.SendToLive(song.ToScenario());
            var after = new System.Collections.Generic.List<string>();
            Snapshot(dir, after);
            TestRunner.CheckEq(after.Count, before, "proyectar un canto NO crea archivos");
            bool pptx = false;
            foreach (string f in after)
                if (f.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".ahp", StringComparison.OrdinalIgnoreCase)) pptx = true;
            TestRunner.Check(!pptx, "cero PPTX/AHP generados al proyectar");
            TestRunner.Check(live.State.HasProgram || live.Project != null,
                "el canto entró al programa en vivo (DB → Motor)");
        }

        static void Snapshot(string dir, System.Collections.Generic.List<string> into)
        {
            if (!Directory.Exists(dir)) return;
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                into.Add(f);
        }
    }
}
