// ============================================================================
//  Fusion-HP · tests/managed/MotorPayloadTests.cs (v2.2) — verifica el
//  constructor del programa para el MOTOR: resolución de todos los elementos,
//  transición efectiva horneada (elemento → ajuste global → cut sin animación),
//  selección, avance y capa de resaltado. El formato es exactamente el que
//  consume Motor::LoadProgram en el núcleo C++.
// ============================================================================
using System;
using System.Collections.Generic;
using Fusion.Shared;
using Fusion.Shared.Model;

namespace Fusion.Tests
{
    static class MotorPayloadTests
    {
        public static void TestMotorPayloadBuildsFullProgram()
        {
            var proj = AhpProject.CreateDefault();
            proj.Name = "Culto de prueba";
            var scn1 = new Scenario { Title = "Adoración" };
            scn1.Elements.Add(new Element { Lines = { "Dios está aquí", "tan cierto como el aire" } });
            scn1.Elements.Add(new Element { Kind = ElementKind.Verse, Reference = "Juan 3:16",
                                            Lines = { "Porque de tal manera amó Dios…" } });
            var scn2 = new Scenario { Title = "Avisos" };
            scn2.Elements.Add(new Element { Kind = ElementKind.LowerThird, OverlayText = "Bienvenidos",
                                            Lines = { "Bienvenidos" } });
            proj.Scenarios.Add(scn1);
            proj.Scenarios.Add(scn2);

            var hl = new List<string> { "Dios" };
            var payload = MotorPayload.Build(proj, "C:\\base", "fade", true, "line", 1, 0, 0, hl);

            var prog = payload.GetArray("program");
            TestRunner.Check(prog != null && prog.Count == 2, "MotorPayload: 2 escenarios");
            var s0 = prog[0];
            TestRunner.CheckEq(s0.GetStr("title", ""), "Adoración", "MotorPayload: título");
            var els = s0.GetArray("elements");
            TestRunner.Check(els != null && els.Count == 2, "MotorPayload: elementos del 1º");
            TestRunner.CheckEq(els[0].GetStr("kind", ""), "text", "MotorPayload: kind text");
            TestRunner.CheckEq(els[1].GetStr("kind", ""), "verse", "MotorPayload: kind verse");
            TestRunner.CheckEq(els[0].GetStr("title", ""), "Dios está aquí", "MotorPayload: título derivado del elemento");
            var slide = els[0].Get("slide");
            TestRunner.Check(slide != null && slide.Type == JsonValue.Kind.Object, "MotorPayload: slide resuelta");
            TestRunner.CheckEq(slide.GetStr("transition", ""), "fade", "MotorPayload: transición global horneada");
            var lines = slide.GetArray("lines");
            TestRunner.Check(lines != null && lines.Count == 2, "MotorPayload: líneas de la slide");
            TestRunner.CheckEq(els[1].GetStr("title", ""), "Juan 3:16", "MotorPayload: título de versículo = referencia");

            var sel = payload.Get("select");
            TestRunner.CheckEq(sel.GetInt("scenario", -1), 1, "MotorPayload: selección de escenario");
            TestRunner.CheckEq(sel.GetInt("element", -1), 0, "MotorPayload: selección de elemento");
            TestRunner.CheckEq(payload.GetStr("advance", ""), "line", "MotorPayload: avance línea");
            var hla = payload.GetStringArray("highlight");
            TestRunner.Check(hla.Count == 1 && hla[0] == "Dios", "MotorPayload: resaltado viaja con el programa");
        }

        public static void TestMotorPayloadBakesCutWithoutAnimation()
        {
            var proj = AhpProject.CreateDefault();
            var scn = new Scenario { Title = "S" };
            scn.Elements.Add(new Element { Lines = { "A" }, Transition = "" });        // usa el global
            scn.Elements.Add(new Element { Lines = { "B" }, Transition = "slide" });   // explícito
            proj.Scenarios.Add(scn);

            // Animaciones OFF → todo cut (comportamiento determinista en vivo)
            var p = MotorPayload.Build(proj, ".", "fade", false, "slide", 0, 0, 0, null);
            var els = p.GetArray("program")[0].GetArray("elements");
            TestRunner.CheckEq(els[0].Get("slide").GetStr("transition", ""), "cut", "MotorPayload: sin animación → cut");
            TestRunner.CheckEq(els[1].Get("slide").GetStr("transition", ""), "cut", "MotorPayload: explícita también → cut");

            // Animaciones ON → global o la del elemento
            p = MotorPayload.Build(proj, ".", "slide", true, "line", 0, 0, 0, null);
            els = p.GetArray("program")[0].GetArray("elements");
            TestRunner.CheckEq(els[0].Get("slide").GetStr("transition", ""), "slide", "MotorPayload: global horneada");
            TestRunner.CheckEq(els[1].Get("slide").GetStr("transition", ""), "slide", "MotorPayload: la del elemento gana");

            p = MotorPayload.Build(proj, ".", "fade", true, "line", 0, 0, 0, null);
            els = p.GetArray("program")[0].GetArray("elements");
            TestRunner.CheckEq(els[1].Get("slide").GetStr("transition", ""), "slide", "MotorPayload: elemento sobre global");
        }

        public static void TestMotorPayloadEmptyProjectIsSafe()
        {
            var p = MotorPayload.Build(null, ".", "fade", true, "line", -1, -1, 0, null);
            var prog = p.GetArray("program");
            TestRunner.Check(prog != null && prog.Count == 0, "MotorPayload: proyecto nulo → programa vacío");
            TestRunner.CheckEq(p.Get("select").GetInt("scenario", -99), -1, "MotorPayload: selección -1 sin proyecto");
        }
    }
}
