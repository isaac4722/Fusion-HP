// ============================================================================
//  Fusion-HP · FusionShared/Model/MotorPayload.cs (v2.2) — constructor del
//  programa para el MOTOR del núcleo. La GUI resuelve la herencia de 4 niveles
//  [SPEC §5.4] de TODOS los elementos, hornea la transición efectiva
//  (elemento → configuración → cut si animaciones desactivadas) y entrega el
//  programa completo al Motor con motor.load. Desde ese momento el Motor es
//  el dueño del estado vivo: la GUI solo envía navegación (motor.next…).
// ============================================================================
using System;
using System.Collections.Generic;

namespace Fusion.Shared.Model
{
    public static class MotorPayload
    {
        /// <summary>Etiqueta corta de un elemento para las listas de la GUI.</summary>
        public static string TitleOf(Element el)
        {
            if (el == null) return "";
            if (!string.IsNullOrEmpty(el.Reference)) return el.Reference;
            if (el.Lines.Count > 0)
            {
                string t = el.Lines[0];
                return t.Length > 42 ? t.Substring(0, 42) + "…" : t;
            }
            if (!string.IsNullOrEmpty(el.Src))
            {
                string f = System.IO.Path.GetFileName(el.Src);
                return f.Length > 42 ? f.Substring(0, 42) + "…" : f;
            }
            return el.KindKey;
        }

        /// <summary>
        /// Programa completo resuelto (motor.load). select* = selección inicial;
        /// highlight = palabras de resaltado activas en el Motor (capa de anulación).
        /// </summary>
        public static JsonValue Build(AhpProject project, string baseDir,
                                      string defaultTransition, bool animate,
                                      string advanceMode,
                                      int selectScenario, int selectElement, int selectLine,
                                      IList<string> highlight)
        {
            var p = JsonValue.Object();
            var prog = JsonValue.Array();
            if (project != null)
            {
                foreach (var scn in project.Scenarios)
                {
                    var sj = JsonValue.Object();
                    sj.Set("id", JsonValue.Make(scn.Id != null ? scn.Id : ""));
                    sj.Set("title", JsonValue.Make(string.IsNullOrEmpty(scn.Title) ? "Escenario" : scn.Title));
                    var els = JsonValue.Array();
                    foreach (var el in scn.Elements)
                    {
                        var r = ResolvedSlide.Resolve(el, scn, project, baseDir);
                        // Transición efectiva horneada: elemento → ajuste global → cut.
                        if (string.IsNullOrEmpty(r.Transition)) r.Transition = defaultTransition;
                        if (!animate) r.Transition = "cut";
                        r.ActiveLine = 0;
                        var ej = JsonValue.Object();
                        ej.Set("id", JsonValue.Make(el.Id != null ? el.Id : ""));
                        ej.Set("title", JsonValue.Make(TitleOf(el)));
                        ej.Set("kind", JsonValue.Make(el.KindKey));
                        ej.Set("slide", r.ToIpcJson());
                        els.Add(ej);
                    }
                    sj.Set("elements", els);
                    prog.Add(sj);
                }
            }
            p.Set("program", prog);

            var sel = JsonValue.Object();
            sel.Set("scenario", JsonValue.Make(selectScenario));
            sel.Set("element", JsonValue.Make(selectElement));
            sel.Set("line", JsonValue.Make(selectLine));
            p.Set("select", sel);
            p.Set("advance", JsonValue.Make(advanceMode == "slide" ? "slide" : "line"));
            var hl = JsonValue.Array();
            if (highlight != null) hl.AddStrings(highlight);
            p.Set("highlight", hl);
            return p;
        }
    }
}
