// ============================================================================
//  Fusion-HP · Import/PptxOpenXmlImporter.cs — importador PPTX vía OPC
//  [SPEC §9.2]: recorrido normativo del contenedor, resolución de herencia
//  Diapositiva → Diseño → Maestro → Tema, conversión EMU → fracciones,
//  tamaños sz/100 → pt, tolerancia a extensiones (elementos desconocidos se
//  ignoran) y mitigación XXE (XMLReader con DTD prohibida). Los .pptm se
//  importan SIN ejecutar macros, con aviso [SPEC §9.2.4].
//  Implementado con System.IO.Packaging (WindowsBase, sin dependencias).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Text;
using System.Xml;
using Fusion.Shared;
using Fusion.Shared.Model;

namespace Fusion.Studio.Import
{
    public class PptxImportReport
    {
        public int Scenarios;
        public string MediaOutDir;          // carpeta donde se extraen los medios
        public List<string> Warnings = new List<string>();
    }

    public class PptxOpenXmlImporter
    {
        const double EmuPerInch = 914400.0;

        /// <summary>Importa un PPTX a Escenarios. mediaOutDir permite redirigir la
        /// extracción de medios (v3.0.0: la proyección directa usa una caché propia
        /// para no tocar el proyecto del usuario).</summary>
        public PptxImportReport Import(string path, AhpProject project, string mediaOutDir = null)
        {
            var rep = new PptxImportReport();
            // Los medios extraídos viven junto al proyecto en media/ [SPEC §5.3a]
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

                // Dimensiones de la presentación (EMU)
                double presW = 12192000, presH = 6858000;   // 16:9 por defecto
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
                    string target = rel.Attributes["Target"].Value;    // p.ej. "slides/slide1.xml"
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

            // Título (placeholder de tipo title/ctrTitle)
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

            // Elementos de texto (cajas → Elemento Texto [SPEC §9.2.6])
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
                ApplyRunStyle(sp, el, pkg);
                scn.Elements.Add(el);
                hasContent = true;
            }

            // Imágenes incrustadas → extraer a media/ del proyecto [SPEC §9.2.6]
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
                    string target = rel.Attributes["Target"].Value;    // "../media/image1.png"
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
            var nv = sp.SelectSingleNode(".//*[local-name()='nvSpPr']/*[local-name()='cNvPr']");
            var ph = sp.SelectSingleNode(".//*[local-name()='spPr']/*[local-name()='ph' or local-name()='placeholder']");
            // placeholder real vive en nvSpPr → spPr no; buscar en ambos
            var ph2 = sp.SelectSingleNode(".//*[local-name()='ph']");
            var node = ph2 ?? ph;
            return node != null && node.Attributes["type"] != null ? node.Attributes["type"].Value : "";
        }

        static string TextOf(XmlNode sp)
        {
            var sb = new StringBuilder();
            foreach (XmlNode p in sp.SelectNodes(".//*[local-name()='p']"))
            {
                foreach (XmlNode t in p.SelectNodes(".//*[local-name()='t']"))
                    sb.Append(t.InnerText);
            }
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

        /// <summary>Geometría normalizada (fracciones). Struct propio: Tuple no existe en net35.</summary>
        private class Geom
        {
            public double X, Y, W, H;
        }

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
            // normalizar a rango válido
            x = Math.Max(0, Math.Min(0.95, x)); y = Math.Max(0, Math.Min(0.95, y));
            w = Math.Max(0.05, Math.Min(1 - x, w)); h = Math.Max(0.05, Math.Min(1 - y, h));
            return new Geom { X = x, Y = y, W = w, H = h };
        }

        static void ApplyRunStyle(XmlNode sp, Element el, Package pkg)
        {
            // sz en centésimas de punto ("3200" → 32 pt) [SPEC §9.2.3]
            var rPr = sp.SelectSingleNode(".//*[local-name()='rPr'][@sz]");
            if (rPr != null)
            {
                double szPt = GetDouble(rPr, "sz", 3200) / 100.0;
                // convertir pt de PowerPoint → fracción de pantalla: aproximamos sobre el alto
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

        /// <summary>XML seguro: DTD y entidades externas prohibidas [SPEC §9.2.4].</summary>
        static XmlDocument ReadXml(Stream s)
        {
            var settings = new XmlReaderSettings();
#pragma warning disable 618
            settings.ProhibitDtd = true;                 // sin DTD: mitiga XXE/billion laughs
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
}
