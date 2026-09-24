// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Project/AhpBridge.cs : conversión del modelo ahp.v1 (F2) al JSON del
//  MOTOR NATIVO (escenario del núcleo — F1/F3). Una sola dirección de
//  verdad: el proyecto persiste en ahp.v1; el núcleo recibe su proyección.
//
//  Mapeos (los cinco Elementos, F2.02):
//    Text        → ítem "text" con lines[] {text, syncMark} (F1.03/F2.03)
//    Verse       → ítem "scripture" {ref, version, highlight}
//    Image       → ítem "image" {imagePath, title, text?}
//    Video       → ítem "video" {videoPath, videoVolume, videoStartAtMs, loop}
//    LowerThird  → ítem "text" especial + activación por lumina_lower_third
//                  (F3.04: el LT se superpone en el pipeline nativo, no es
//                  una slide propia)
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace lumina.core.project
{
    public static class AhpBridge
    {
        /// <summary>Serializa un Escenario ahp.v1 al JSON del motor nativo.</summary>
        public static string ToEngineScenario(AhpScenario s, string baseDir)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["name"] = s.Name ?? "";
            List<object> items = new List<object>();
            foreach (AhpElement e in s.Elements)
            {
                Dictionary<string, object> it = ToEngineItem(e, baseDir);
                if (it != null) items.Add(it);
            }
            root["items"] = items;
            return MiniJson.Serialize(root);
        }

        /// <summary>Resuelve la ruta de un recurso (relativa al proyecto o
        /// absoluta — F2.01.6) al camino real del sistema de archivos.</summary>
        public static string ResolveResource(string relOrAbs, string baseDir)
        {
            if (string.IsNullOrEmpty(relOrAbs)) return relOrAbs;
            if (relOrAbs.Length > 2 && relOrAbs[1] == ':') return relOrAbs;    // C:\
            if (relOrAbs.StartsWith("\\\\", StringComparison.Ordinal)) return relOrAbs;
            if (string.IsNullOrEmpty(baseDir)) return relOrAbs;
            return baseDir.TrimEnd('\\', '/') + "\\" + relOrAbs.Replace('/', '\\');
        }

        private static Dictionary<string, object> ToEngineItem(AhpElement e, string baseDir)
        {
            Dictionary<string, object> it = new Dictionary<string, object>();
            it["title"] = e.Title ?? "";
            switch (e.Kind)
            {
                case AhpElementKind.Text:
                {
                    it["kind"] = "text";
                    // F2.03/F1.03: líneas con syncMark persistido.
                    List<object> lines = new List<object>();
                    bool anyMark = false;
                    foreach (AhpLine l in e.Lines)
                    {
                        Dictionary<string, object> lo = new Dictionary<string, object>();
                        lo["text"] = l.Text ?? "";
                        if (l.SyncMark > 0) { lo["syncMark"] = (long)l.SyncMark; anyMark = true; }
                        lines.Add(lo);
                    }
                    // Solo se envía como líneas estructuradas si hay marcas;
                    // si no, la vía clásica (text plano) conserva la
                    // agrupación maxLinesPerSlide del motor.
                    if (anyMark) it["lines"] = lines;
                    else
                    {
                        StringBuilder sb = new StringBuilder();
                        foreach (AhpLine l in e.Lines)
                        {
                            if (sb.Length > 0) sb.Append('\n');
                            sb.Append(l.Text ?? "");
                        }
                        it["text"] = sb.ToString();
                        it["maxLinesPerSlide"] = 99L;   // el autor ya agrupó
                    }
                    break;
                }
                case AhpElementKind.Verse:
                {
                    it["kind"] = "scripture";
                    // Referencia tipada: "Libro cap:v" o "Libro cap:v-v".
                    string rf = (e.Book ?? "") + " " +
                        e.Chapter.ToString(CultureInfo.InvariantCulture) + ":" +
                        e.VerseFrom.ToString(CultureInfo.InvariantCulture);
                    if (e.VerseTo > e.VerseFrom)
                        rf += "-" + e.VerseTo.ToString(CultureInfo.InvariantCulture);
                    it["ref"] = rf;
                    if (!string.IsNullOrEmpty(e.Translation)) it["version"] = e.Translation;
                    if (e.HighlightedWords.Count > 0)
                    {
                        // F2.04: palabras destacadas → highlight del motor.
                        it["highlight"] = e.HighlightedWords.Count == 1
                            ? e.HighlightedWords[0]
                            : string.Join(" ", e.HighlightedWords.ToArray());
                    }
                    if (!string.IsNullOrEmpty(e.QuoteFormat)) it["refLabel"] = e.QuoteFormat;
                    break;
                }
                case AhpElementKind.Image:
                {
                    it["kind"] = "image";
                    it["imagePath"] = ResolveResource(e.ImagePath, baseDir);
                    if (!string.IsNullOrEmpty(e.Title)) it["text"] = e.Title;
                    break;
                }
                case AhpElementKind.Video:
                {
                    it["kind"] = "video";
                    it["videoPath"] = ResolveResource(e.VideoPath, baseDir);
                    it["videoVolume"] = (long)e.VideoVolume;
                    it["videoStartAtMs"] = e.StartAtMs;
                    it["videoLoop"] = e.Loop;
                    break;
                }
                case AhpElementKind.LowerThird:
                {
                    // F3.04: el LT se proyecta como banda nativa superpuesta.
                    // Como ítem de la lista queda el texto plano (visible al
                    // operador); la activación usa lumina_lower_third.
                    it["kind"] = "text";
                    it["title"] = string.IsNullOrEmpty(e.Title) ? "Lower Third" : e.Title;
                    it["text"] = e.LtText ?? "";
                    it["lowerThird"] = LtJson(e);
                    break;
                }
            }
            return it;
        }

        /// <summary>JSON de activación Lower Third para lumina_lower_third.</summary>
        public static string LtJson(AhpElement e)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["text"] = e.LtText ?? "";
            o["position"] = string.IsNullOrEmpty(e.LtPosition) ? "bottom" : e.LtPosition;
            o["durationMs"] = (long)Math.Round(e.LtDurationSec * 1000.0);
            o["show"] = true;
            return MiniJson.Serialize(o);
        }
    }
}
