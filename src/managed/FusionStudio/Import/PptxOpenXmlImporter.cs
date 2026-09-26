// ============================================================================
//  Fusion-HP · Import/PptxOpenXmlImporter.cs — importador PPTX [SPEC §9.2]
//  Recorrido normativo del contenedor, resolución de herencia
//  Diapositiva → Diseño → Maestro → Tema, conversión EMU → fracciones,
//  tamaños sz/100 → pt, tolerancia a extensiones y mitigación XXE.
//  Los .pptm se importan SIN ejecutar macros, con aviso [SPEC §9.2.4].
//
//  v4.1.0 — dos motores por perfil:
//  · FusionStudio (net48): DocumentFormat.OpenXml 2.7.2 (MIT) — modelo tipado
//    del SDK, herencia de estilos REAL en 4 niveles y resolución de colores de
//    tema; el SDK es OPC estricto y no resuelve entidades externas (anti-XXE
//    por construcción). PptxDirectProjector queda intacto.
//  · FusionStudio.Lite (net35): System.IO.Packaging (WindowsBase), código
//    histórico intacto — Lite nunca carga el SDK (rompería net35).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Fusion.Shared;
using Fusion.Shared.Model;
#if !LITE
using DOP = DocumentFormat.OpenXml.Packaging;
using DP = DocumentFormat.OpenXml.Presentation;
using DA = DocumentFormat.OpenXml.Drawing;
#else
using System.IO.Packaging;
using System.Xml;
#endif

namespace Fusion.Studio.Import
{
    public class PptxImportReport
    {
        public int Scenarios;
        public string MediaOutDir;          // carpeta donde se extraen los medios
        public List<string> Warnings = new List<string>();
    }

#if !LITE
    // =========================================================================
    //  Motor DocumentFormat.OpenXml (FusionStudio, net48)
    // =========================================================================

    public class PptxOpenXmlImporter
    {
        /// <summary>Importa un PPTX a Escenarios. mediaOutDir permite redirigir la
        /// extracción de medios (v3.0.0: la proyección directa usa una caché propia).</summary>
        public PptxImportReport Import(string path, AhpProject project, string mediaOutDir = null)
        {
            var rep = new PptxImportReport();
            string baseDir = !string.IsNullOrEmpty(project.SourcePath)
                ? Path.GetDirectoryName(project.SourcePath)
                : Path.Combine(Path.GetDirectoryName(path), "proyecto-fusion");
            rep.MediaOutDir = mediaOutDir ?? Path.Combine(baseDir, "media");
            if (string.Equals(Path.GetExtension(path), ".pptm", StringComparison.OrdinalIgnoreCase))
                rep.Warnings.Add("El archivo contiene macros: se importan SIN ejecutarlas [SPEC §9.2.4].");

            using (var doc = DOP.PresentationDocument.Open(path, false))
            {
                var presPart = doc.PresentationPart;
                if (presPart == null || presPart.Presentation == null)
                {
                    rep.Warnings.Add("El archivo no es una presentación válida.");
                    return rep;
                }

                var sldSz = presPart.Presentation.GetFirstChild<DP.SlideSize>();
                double presW = sldSz != null && sldSz.Cx != null ? sldSz.Cx.Value : 12192000;
                double presH = sldSz != null && sldSz.Cy != null ? sldSz.Cy.Value : 6858000;

                var slideIds = presPart.Presentation.SlideIdList;
                if (slideIds == null || !slideIds.HasChildren)
                {
                    rep.Warnings.Add("El archivo no contiene diapositivas.");
                    return rep;
                }

                int idx = 0;
                foreach (DP.SlideId id in slideIds.ChildElements)
                {
                    idx++;
                    string relId = id.RelationshipId;
                    if (string.IsNullOrEmpty(relId)) continue;
                    var slidePart = presPart.GetPartById(relId) as DOP.SlidePart;
                    if (slidePart == null) continue;
                    var scn = ImportSlide(slidePart, presW, presH, rep, idx);
                    if (scn != null)
                    {
                        project.Scenarios.Add(scn);
                        rep.Scenarios++;
                    }
                }
                if (rep.Scenarios > 0)
                    rep.Warnings.Add("Animaciones y transiciones no se importan en el MVP [SPEC §9.2.6].");
            }
            return rep;
        }

        Scenario ImportSlide(DOP.SlidePart slidePart, double presW, double presH, PptxImportReport rep, int index)
        {
            var slide = slidePart.Slide;
            if (slide == null) return null;

            var scn = new Scenario();
            scn.Id = "scn-pptx-" + index + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            scn.Title = "Diapositiva " + index;

            // Layout y Master de esta diapositiva (herencia 4 niveles)
            DOP.SlideLayoutPart layoutPart = slidePart.SlideLayoutPart;
            DOP.SlideMasterPart masterPart = layoutPart != null ? layoutPart.SlideMasterPart : null;
            var theme = masterPart != null ? masterPart.ThemePart : null;

            bool hasContent = false;

            // Título (placeholder de tipo title/ctrTitle)
            foreach (var sp in slide.Descendants<DP.Shape>())
            {
                var ph = GetPh(sp);
                if (IsTitle(ph))
                {
                    string t = TextOf(sp.TextBody);
                    if (!string.IsNullOrEmpty(t)) { scn.Title = t; break; }
                }
            }

            // Cajas de texto → Elemento Texto [SPEC §9.2.6]
            foreach (var sp in slide.Descendants<DP.Shape>())
            {
                var lines = TextLines(sp.TextBody);
                if (lines.Count == 0) continue;
                var geom = ShapeGeom(sp, presW, presH);
                var el = new Element
                {
                    Id = "el-" + Guid.NewGuid().ToString("N").Substring(0, 10),
                    Kind = ElementKind.Text,
                    Lines = lines,
                    X = geom.X, Y = geom.Y, W = geom.W, H = geom.H
                };
                ApplyRunStyle(sp, el, layoutPart, masterPart, theme);
                scn.Elements.Add(el);
                hasContent = true;
            }

            // Imágenes incrustadas → media/ del proyecto [SPEC §9.2.6]
            foreach (var pic in slide.Descendants<DP.Picture>())
            {
                var blip = pic.BlipFill != null ? pic.BlipFill.Blip : null;
                if (blip == null || blip.Embed == null) continue;
                try
                {
                    var part = slidePart.GetPartById(blip.Embed.Value) as DOP.ImagePart;
                    if (part == null) continue;
                    string outName = "pptx-" + Guid.NewGuid().ToString("N").Substring(0, 8) +
                                     Path.GetExtension(part.Uri.OriginalString);
                    if (rep.MediaOutDir != null)
                    {
                        Directory.CreateDirectory(rep.MediaOutDir);
                        using (var s = part.GetStream())
                        using (var o = File.Create(Path.Combine(rep.MediaOutDir, outName)))
                            CopyStream(s, o);
                        var geom2 = ShapeGeom(pic, presW, presH);
                        scn.Elements.Add(new Element
                        {
                            Id = "el-" + Guid.NewGuid().ToString("N").Substring(0, 10),
                            Kind = ElementKind.Image,
                            Src = "media/" + outName,
                            X = geom2.X, Y = geom2.Y, W = geom2.W, H = geom2.H
                        });
                        hasContent = true;
                    }
                }
                catch { /* imagen no referenciada o parte ausente: se ignora */ }
            }

            if (!hasContent) return null;
            return scn;
        }

        // --------------------------------------------------- herencia 4 niveles
        class EffStyle
        {
            public double? Size;        // pt reales (sz/100)
            public bool? Bold, Italic;
            public string Color;        // #RRGGBB
        }

        /// <summary>Run → cuerpo → layout → maestro → tema [SPEC §9.2.3].</summary>
        void ApplyRunStyle(DP.Shape sp, Element el, DOP.SlideLayoutPart layoutPart,
                           DOP.SlideMasterPart masterPart, DOP.ThemePart theme)
        {
            var eff = new EffStyle();
            var body = sp.TextBody;

            // 1) run rPr — primera rPr con datos
            if (body != null)
                foreach (var r in body.Descendants<DA.Run>())
                {
                    var rPr = r.RunProperties;
                    if (rPr == null) continue;
                    if (eff.Size == null && rPr.FontSize != null) eff.Size = rPr.FontSize.Value / 100.0;
                    if (eff.Bold == null && rPr.Bold != null) eff.Bold = rPr.Bold.Value;
                    if (eff.Italic == null && rPr.Italic != null) eff.Italic = rPr.Italic.Value;
                    if (eff.Color == null) eff.Color = SrgbOf(rPr);
                    if (eff.Size != null && eff.Bold != null && eff.Italic != null && eff.Color != null) break;
                }

            // 2) cuerpo: lstStyle nivel 1
            if (body != null && body.ListStyle != null)
                FillFromTextBodyListStyle(body.ListStyle, eff);

            // 3) layout (placeholder homólogo) y 4) maestro
            FillFromAncestor(layoutPart, sp, eff);
            FillFromAncestor(masterPart, sp, eff);

            // color heredado del tema (esquema de colores) como último recurso
            if (eff.Color == null && theme != null && theme.Theme != null)
            {
                var scheme = theme.Theme.GetFirstChild<DA.Theme>();
                if (scheme != null && scheme.ThemeElements != null && scheme.ThemeElements.ColorScheme != null)
                {
                    var cs = scheme.ThemeElements.ColorScheme;
                    // texto por defecto: tx1 → dk1
                    var dk1 = cs.Dark1Color != null ? cs.Dark1Color.RgbColorModelHex : null;
                    if (dk1 != null && dk1.Val != null) eff.Color = "#" + dk1.Val.Value;
                }
            }

            // aplicar (misma conversión que el motor histórico)
            if (eff.Size != null)
            {
                double szPt = Math.Max(10, Math.Min(200, eff.Size.Value));
                el.StyleOverride.Size = Math.Max(10, Math.Min(120, szPt * (1080.0 / 540.0)));
            }
            if (eff.Bold != null) el.StyleOverride.Bold = eff.Bold.Value;
            if (eff.Italic != null) el.StyleOverride.Italic = eff.Italic.Value;
        }

        static void FillFromTextBodyListStyle(DA.ListStyle lst, EffStyle eff)
        {
            if (lst == null) return;
            var lvl1 = lst.GetFirstChild<DA.Level1ParagraphProperties>();
            var def = lvl1 != null ? lvl1.GetFirstChild<DA.DefaultRunProperties>() : null;
            if (def == null) return;
            if (eff.Size == null && def.FontSize != null) eff.Size = def.FontSize.Value / 100.0;
            if (eff.Bold == null && def.Bold != null) eff.Bold = def.Bold.Value;
            if (eff.Italic == null && def.Italic != null) eff.Italic = def.Italic.Value;
            if (eff.Color == null) eff.Color = SrgbOf(def);
        }

        void FillFromAncestor(DOP.SlideLayoutPart part, DP.Shape shape, EffStyle eff)
        {
            FillFromAncestorTyped(part, shape, eff, true);
        }

        void FillFromAncestor(DOP.SlideMasterPart part, DP.Shape shape, EffStyle eff)
        {
            FillFromAncestorTyped(part, shape, eff, false);
        }

        void FillFromAncestorTyped(object part, DP.Shape shape, EffStyle eff, bool isLayout)
        {
            if (part == null) return;
            DP.ShapeTree tree = null;
            if (isLayout)
            {
                var lp = part as DOP.SlideLayoutPart;
                tree = lp.SlideLayout != null && lp.SlideLayout.CommonSlideData != null
                       ? lp.SlideLayout.CommonSlideData.ShapeTree : null;
            }
            else
            {
                var mp = part as DOP.SlideMasterPart;
                tree = mp.SlideMaster != null && mp.SlideMaster.CommonSlideData != null
                       ? mp.SlideMaster.CommonSlideData.ShapeTree : null;
            }
            if (tree == null) return;

            var phSrc = GetPh(shape);
            uint wantIdx = phSrc != null && phSrc.Index != null ? phSrc.Index.Value : 0;
            string wantType = phSrc != null && phSrc.Type != null ? phSrc.Type.ToString() : null;

            foreach (var s2 in tree.Descendants<DP.Shape>())
            {
                var ph2 = GetPh(s2);
                if (ph2 == null) continue;
                uint idx2 = ph2.Index != null ? ph2.Index.Value : 0;
                string type2 = ph2.Type != null ? ph2.Type.ToString() : null;
                bool match = (wantIdx > 0 && idx2 == wantIdx) ||
                             (wantIdx == 0 && wantType != null && wantType == type2);
                if (!match) continue;
                if (s2.TextBody != null) FillFromTextBodyListStyle(s2.TextBody.ListStyle, eff);
                break;
            }
        }

        static string SrgbOf(DA.RunProperties rPr)
        {
            if (rPr == null) return null;
            var solid = rPr.GetFirstChild<DA.SolidFill>();
            var hex = solid != null ? solid.GetFirstChild<DA.RgbColorModelHex>() : null;
            return (hex != null && hex.Val != null) ? "#" + hex.Val.Value : null;
        }

        static string SrgbOf(DA.DefaultRunProperties def)
        {
            if (def == null) return null;
            var solid = def.GetFirstChild<DA.SolidFill>();
            var hex = solid != null ? solid.GetFirstChild<DA.RgbColorModelHex>() : null;
            return (hex != null && hex.Val != null) ? "#" + hex.Val.Value : null;
        }

        // ----------------------------------------------------------------- varios
        static DP.PlaceholderShape GetPh(DP.Shape sp)
        {
            return sp.NonVisualShapeProperties != null &&
                   sp.NonVisualShapeProperties.ApplicationNonVisualDrawingProperties != null
                   ? sp.NonVisualShapeProperties.ApplicationNonVisualDrawingProperties.PlaceholderShape
                   : null;
        }

        static bool IsTitle(DP.PlaceholderShape ph)
        {
            if (ph == null || ph.Type == null) return false;
            var t = ph.Type.Value;
            return t == DP.PlaceholderValues.Title || t == DP.PlaceholderValues.CenteredTitle;
        }

        static string TextOf(DP.TextBody body)
        {
            var sb = new StringBuilder();
            foreach (var t in TextLines(body)) sb.Append(t);
            return sb.ToString().Trim();
        }

        static List<string> TextLines(DP.TextBody body)
        {
            var lines = new List<string>();
            if (body == null) return lines;
            foreach (var p in body.Descendants<DA.Paragraph>())
            {
                var sb = new StringBuilder();
                foreach (var t in p.Descendants<DA.Text>()) sb.Append(t.Text);
                string line = sb.ToString().Trim();
                if (line.Length > 0) lines.Add(line);
            }
            return lines;
        }

        class Geom { public double X, Y, W, H; }

        static Geom ShapeGeom(DP.Shape shape, double presW, double presH) { return GeomOf(shape.ShapeProperties, presW, presH); }
        static Geom ShapeGeom(DP.Picture pic, double presW, double presH) { return GeomOf(pic.ShapeProperties, presW, presH); }

        /// <summary>EMU → fracciones con normalización [SPEC §9.2.3].</summary>
        static Geom GeomOf(DP.ShapeProperties spPr, double presW, double presH)
        {
            double x = 0.05, y = 0.08, w = 0.9, h = 0.84;
            var xfrm = spPr != null ? spPr.GetFirstChild<DA.Transform2D>() : null;
            if (xfrm != null && xfrm.Offset != null && xfrm.Extents != null && presW > 0 && presH > 0)
            {
                long ox = xfrm.Offset.X != null ? xfrm.Offset.X.Value : 0;
                long oy = xfrm.Offset.Y != null ? xfrm.Offset.Y.Value : 0;
                long ex = xfrm.Extents.Cx != null ? xfrm.Extents.Cx.Value : 0;
                long ey = xfrm.Extents.Cy != null ? xfrm.Extents.Cy.Value : 0;
                x = ox / presW; y = oy / presH; w = ex / presW; h = ey / presH;
            }
            x = Math.Max(0, Math.Min(0.95, x)); y = Math.Max(0, Math.Min(0.95, y));
            w = Math.Max(0.05, Math.Min(1 - x, w)); h = Math.Max(0.05, Math.Min(1 - y, h));
            return new Geom { X = x, Y = y, W = w, H = h };
        }

        static void CopyStream(Stream src, Stream dst)
        {
            var buf = new byte[8192];
            int n;
            while ((n = src.Read(buf, 0, buf.Length)) > 0) dst.Write(buf, 0, n);
        }
    }

#else
    // =========================================================================
    //  Motor System.IO.Packaging (FusionStudio.Lite, net35) — código histórico
    // =========================================================================

    public class PptxOpenXmlImporter
    {
        /// <summary>Importa un PPTX a Escenarios.</summary>
        public PptxImportReport Import(string path, AhpProject project, string mediaOutDir = null)
        {
            var rep = new PptxImportReport();
            string baseDir = !string.IsNullOrEmpty(project.SourcePath)
                ? Path.GetDirectoryName(project.SourcePath)
                : Path.Combine(Path.GetDirectoryName(path), "proyecto-fusion");
            rep.MediaOutDir = mediaOutDir ?? Path.Combine(baseDir, "media");
            if (string.Equals(Path.GetExtension(path), ".pptm", StringComparison.OrdinalIgnoreCase))
                rep.Warnings.Add("El archivo contiene macros: se importan SIN ejecutarlas [SPEC §9.2.4].");

            using (Package pkg = Package.Open(path, FileMode.Open, FileAccess.Read))
            {
                var slideUris = OrderedSlideUris(pkg);
                if (slideUris.Count == 0)
                {
                    rep.Warnings.Add("El archivo no contiene diapositivas.");
                    return rep;
                }

                double presW = 12192000, presH = 6858000;
                var presPart = pkg.GetPart(new Uri("/ppt/presentation.xml", UriKind.Relative));
                using (var r = presPart.GetStream(FileMode.Open, FileAccess.Read))
                {
                    var doc = ReadXml(r);
                    var sldSz = doc.SelectSingleNode("/*[local-name()='presentation']/*[local-name()='sldSz']");
                    if (sldSz != null)
                    {
                        presW = GetDouble(sldSz, "cx", presW);
                        presH = GetDouble(sldSz, "cy", presH);
                    }
                }

                int idx = 0;
                foreach (var uri in slideUris)
                {
                    idx++;
                    var scn = ImportSlide(pkg, uri, presW, presH, rep, idx);
                    if (scn != null)
                    {
                        project.Scenarios.Add(scn);
                        rep.Scenarios++;
                    }
                }
                if (rep.Scenarios > 0)
                    rep.Warnings.Add("Animaciones y transiciones no se importan en el MVP [SPEC §9.2.6].");
            }
            return rep;
        }

        static List<Uri> OrderedSlideUris(Package pkg)
        {
            var list = new List<Uri>();
            var presPart = pkg.GetPart(new Uri("/ppt/presentation.xml", UriKind.Relative));
            using (var r = presPart.GetStream(FileMode.Open, FileAccess.Read))
            {
                var doc = ReadXml(r);
                var rels = doc.SelectNodes("/*[local-name()='presentation']/*[local-name()='sldIdLst']/*[local-name()='sldId']");
                if (rels == null) return list;
                var relPartUri = new Uri("/ppt/_rels/presentation.xml.rels", UriKind.Relative);
                var relPart = pkg.GetPart(relPartUri);
                var relDoc = ReadXml(relPart.GetStream(FileMode.Open, FileAccess.Read));
                foreach (XmlNode sldId in rels)
                {
                    string rid = sldId.Attributes["r:id"] != null ? sldId.Attributes["r:id"].Value : "";
                    var rel = relDoc.SelectSingleNode("/*[local-name()='Relationships']/*[local-name()='Relationship'][@Id='" + rid + "']");
                    if (rel == null) continue;
                    string target = rel.Attributes["Target"].Value;
                    if (!target.StartsWith("/", StringComparison.Ordinal)) target = "/ppt/" + target;
                    list.Add(new Uri(target, UriKind.Relative));
                }
            }
            return list;
        }

        Scenario ImportSlide(Package pkg, Uri slideUri, double presW, double presH, PptxImportReport rep, int index)
        {
            var scn = new Scenario();
            scn.Id = "scn-pptx-" + index + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            scn.Title = "Diapositiva " + index;

            var part = pkg.GetPart(slideUri);
            var doc = ReadXml(part.GetStream(FileMode.Open, FileAccess.Read));

            string title = null;
            var shapes = doc.SelectNodes("//*[local-name()='sp']");
            foreach (XmlNode sp in shapes)
            {
                string phType = PlaceholderType(sp);
                if (phType == "title" || phType == "ctrTitle")
                {
                    string t = TextOf(sp);
                    if (!string.IsNullOrEmpty(t)) { title = t; break; }
                }
            }
            if (!string.IsNullOrEmpty(title)) scn.Title = title;

            bool hasContent = false;
            foreach (XmlNode sp in shapes)
            {
                var txt = TextLines(sp);
                if (txt.Count == 0) continue;
                var geom = ShapeGeom(sp, presW, presH);
                var el = new Element
                {
                    Id = "el-" + Guid.NewGuid().ToString("N").Substring(0, 10),
                    Kind = ElementKind.Text,
                    Lines = txt,
                    X = geom.X, Y = geom.Y, W = geom.W, H = geom.H
                };
                ApplyRunStyle(sp, el);
                scn.Elements.Add(el);
                hasContent = true;
            }

            var pics = doc.SelectNodes("//*[local-name()='pic']");
            if (pics.Count > 0)
            {
                string relPath = slideUri.ToString().Replace("/ppt/slides/", "/ppt/slides/_rels/") + ".rels";
                var relPart = pkg.GetPart(new Uri(relPath, UriKind.Relative));
                var relDoc = ReadXml(relPart.GetStream(FileMode.Open, FileAccess.Read));
                foreach (XmlNode pic in pics)
                {
                    var blip = pic.SelectSingleNode(".//*[local-name()='blip']");
                    if (blip == null) continue;
                    var embed = blip.Attributes["r:embed"];
                    if (embed == null) continue;
                    var rel = relDoc.SelectSingleNode("/*[local-name()='Relationships']/*[local-name()='Relationship'][@Id='" + embed.Value + "']");
                    if (rel == null) continue;
                    string target = rel.Attributes["Target"].Value;
                    string abs = target.StartsWith("/", StringComparison.Ordinal) ? target : "/ppt/" + target;
                    var mediaPart = pkg.GetPart(new Uri(abs, UriKind.Relative));
                    string ext = Path.GetExtension(abs);
                    string outName = "pptx-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ext;
                    string mediaDir = rep.MediaOutDir;
                    if (mediaDir != null)
                    {
                        Directory.CreateDirectory(mediaDir);
                        using (var s = mediaPart.GetStream(FileMode.Open, FileAccess.Read))
                        using (var o = File.Create(Path.Combine(mediaDir, outName)))
                            CopyStream(s, o);
                        var geom2 = ShapeGeom(pic, presW, presH);
                        scn.Elements.Add(new Element
                        {
                            Id = "el-" + Guid.NewGuid().ToString("N").Substring(0, 10),
                            Kind = ElementKind.Image,
                            Src = "media/" + outName,
                            X = geom2.X, Y = geom2.Y, W = geom2.W, H = geom2.H
                        });
                        hasContent = true;
                    }
                }
            }

            if (!hasContent) return null;
            return scn;
        }

        static string PlaceholderType(XmlNode sp)
        {
            var ph2 = sp.SelectSingleNode(".//*[local-name()='ph']");
            return ph2 != null && ph2.Attributes["type"] != null ? ph2.Attributes["type"].Value : "";
        }

        static string TextOf(XmlNode sp)
        {
            var sb = new StringBuilder();
            foreach (XmlNode p in sp.SelectNodes(".//*[local-name()='p']"))
                foreach (XmlNode t in p.SelectNodes(".//*[local-name()='t']"))
                    sb.Append(t.InnerText);
            return sb.ToString().Trim();
        }

        static List<string> TextLines(XmlNode sp)
        {
            var lines = new List<string>();
            foreach (XmlNode p in sp.SelectNodes(".//*[local-name()='p']"))
            {
                var sb = new StringBuilder();
                foreach (XmlNode t in p.SelectNodes(".//*[local-name()='t']")) sb.Append(t.InnerText);
                string line = sb.ToString().Trim();
                if (line.Length > 0) lines.Add(line);
            }
            return lines;
        }

        private class Geom { public double X, Y, W, H; }

        static Geom ShapeGeom(XmlNode shape, double presW, double presH)
        {
            var off = shape.SelectSingleNode(".//*[local-name()='off']");
            var ext = shape.SelectSingleNode(".//*[local-name()='ext']");
            double x = 0.05, y = 0.08, w = 0.9, h = 0.84;
            if (off != null && ext != null && presW > 0 && presH > 0)
            {
                double ox = GetDouble(off, "x", 0), oy = GetDouble(off, "y", 0);
                double ex = GetDouble(ext, "cx", 0), ey = GetDouble(ext, "cy", 0);
                x = ox / presW; y = oy / presH;
                w = ex / presW; h = ey / presH;
            }
            x = Math.Max(0, Math.Min(0.95, x)); y = Math.Max(0, Math.Min(0.95, y));
            w = Math.Max(0.05, Math.Min(1 - x, w)); h = Math.Max(0.05, Math.Min(1 - y, h));
            return new Geom { X = x, Y = y, W = w, H = h };
        }

        static void ApplyRunStyle(XmlNode sp, Element el)
        {
            var rPr = sp.SelectSingleNode(".//*[local-name()='rPr'][@sz]");
            if (rPr != null)
            {
                double szPt = GetDouble(rPr, "sz", 3200) / 100.0;
                el.StyleOverride.Size = Math.Max(10, Math.Min(120, szPt * (1080.0 / 540.0)));
                if (rPr.Attributes["b"] != null) el.StyleOverride.Bold = rPr.Attributes["b"].Value == "1";
                if (rPr.Attributes["i"] != null) el.StyleOverride.Italic = rPr.Attributes["i"].Value == "1";
            }
        }

        static double GetDouble(XmlNode n, string attr, double def)
        {
            var a = n.Attributes[attr];
            double v;
            return a != null && double.TryParse(a.Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v) ? v : def;
        }

        static void CopyStream(Stream src, Stream dst)
        {
            var buf = new byte[8192];
            int n;
            while ((n = src.Read(buf, 0, buf.Length)) > 0) dst.Write(buf, 0, n);
        }

        static XmlDocument ReadXml(Stream s)
        {
            var settings = new XmlReaderSettings();
#pragma warning disable 618
            settings.ProhibitDtd = true;
#pragma warning restore 618
            settings.XmlResolver = null;
            using (var r = XmlReader.Create(s, settings))
            {
                var doc = new XmlDocument();
                doc.Load(r);
                return doc;
            }
        }
    }
#endif
}
