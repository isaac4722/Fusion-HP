// ============================================================================
//  Fusion-HP · Export/PptxExporter.cs — exportación PPTX conforme ISO/IEC-29500
//  [SPEC §9.3.1]: paquete OPC con presentation.xml, slides/, masters/layouts
//  mínimos, theme derivado del tema ahp.v1 y media/ empaquetada. Coordenadas
//  de vuelta a EMU (914400/pulgada); la sincronización por línea se representa
//  como UNA DIAPOSITIVA POR LÍNEA (regla MVP, visible en el diálogo).
//  Escrito con System.IO.Packaging — sin dependencias externas.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Text;
using Fusion.Shared;
using Fusion.Shared.Model;

namespace Fusion.Studio.Export
{
    public class PptxExportReport
    {
        public int Slides;
        public List<string> Warnings = new List<string>();
    }

    public class PptxExporter
    {
        const double EmuPerInch = 914400.0;
        // Lienzo 16:9 (12192000 × 6858000 EMU = 13.33" × 7.5")
        const long PresW = 12192000, PresH = 6858000;

        public PptxExportReport Export(AhpProject project, string outPath, string baseDir)
        {
            var rep = new PptxExportReport();
            if (File.Exists(outPath)) File.Delete(outPath);
            using (Package pkg = Package.Open(outPath, FileMode.Create))
            {
                // [Content_Types].xml lo genera System.IO.Packaging a partir de los
                // content types de las partes (crearla a mano colisiona en Dispose).

                // -------- _rels/.rels
                AddPart(pkg, new Uri("/_rels/.rels", UriKind.Relative), "application/vnd.openxmlformats-package.relationships+xml",
                        RelsXml(new[] { new RelEntry("rId1", "officeDocument", "ppt/presentation.xml") }));

                // -------- ppt/presentation.xml
                var slideFiles = new List<string>();
                int n = 0;
                // una diapositiva por LÍNEA de texto (regla MVP [SPEC §9.3.1]);
                // imágenes/video producen su propia diapositiva.
                foreach (var scn in project.Scenarios)
                {
                    foreach (var el in scn.Elements)
                    {
                        if (el.Kind == ElementKind.Text || el.Kind == ElementKind.Verse || el.Kind == ElementKind.LowerThird)
                        {
                            foreach (var line in el.Lines)
                            {
                                n++;
                                WriteSlide(pkg, n, line, el, scn, project, baseDir, slideFiles, rep);
                            }
                            if (el.Lines.Count == 0)
                            {
                                n++;
                                WriteSlide(pkg, n, "", el, scn, project, baseDir, slideFiles, rep);
                            }
                        }
                        else
                        {
                            n++;
                            WriteSlide(pkg, n, null, el, scn, project, baseDir, slideFiles, rep);
                        }
                    }
                }
                var sldIds = new StringBuilder();
                var presRels = new StringBuilder();
                for (int i = 0; i < slideFiles.Count; i++)
                {
                    sldIds.Append("<p:sldId id=\"" + (256 + i) + "\" r:id=\"rId" + (i + 2) + "\"/>");
                    presRels.Append(Rel("rId" + (i + 2),
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide",
                        "slides/" + slideFiles[i]));
                }
                AddPart(pkg, new Uri("/ppt/presentation.xml", UriKind.Relative),
                        "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml",
                        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                        "<p:presentation xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                        "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">" +
                        "<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst>" +
                        "<p:sldIdLst>" + sldIds + "</p:sldIdLst>" +
                        "<p:sldSz cx=\"" + PresW + "\" cy=\"" + PresH + "\"/>" +
                        "<p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>");

                // -------- relaciones de presentación
                var masterRel = Rel("rId1",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster",
                    "../slideMasters/slideMaster1.xml");
                presRels.Insert(0, masterRel);
                AddPart(pkg, new Uri("/ppt/_rels/presentation.xml.rels", UriKind.Relative),
                        "application/vnd.openxmlformats-package.relationships+xml",
                        RelsXmlRaw(presRels.ToString()));

                // -------- master + layout + theme mínimos (herencia válida)
                AddPart(pkg, new Uri("/ppt/slideMasters/slideMaster1.xml", UriKind.Relative),
                        "application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml", MasterXml());
                AddPart(pkg, new Uri("/ppt/slideMasters/_rels/slideMaster1.xml.rels", UriKind.Relative),
                        "application/vnd.openxmlformats-package.relationships+xml",
                        RelsXml(new[] {
                            new RelEntry("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme", "../theme/theme1.xml"),
                            new RelEntry("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout", "../slideLayouts/slideLayout1.xml")
                        }));
                AddPart(pkg, new Uri("/ppt/slideLayouts/slideLayout1.xml", UriKind.Relative),
                        "application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml", LayoutXml());
                AddPart(pkg, new Uri("/ppt/slideLayouts/_rels/slideLayout1.xml.rels", UriKind.Relative),
                        "application/vnd.openxmlformats-package.relationships+xml",
                        RelsXml(new[] { new RelEntry("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster", "../slideMasters/slideMaster1.xml") }));
                AddPart(pkg, new Uri("/ppt/theme/theme1.xml", UriKind.Relative),
                        "application/vnd.openxmlformats-officedocument.theme+xml", ThemeXml(project));
                rep.Slides = slideFiles.Count;
                rep.Warnings.Add("La sincronización por línea se exporta como una diapositiva por línea [SPEC §9.3.1].");
            }
            return rep;
        }

        void WriteSlide(Package pkg, int idx, string singleLine, Element el, Scenario scn, AhpProject project,
                        string baseDir, List<string> slideFiles, PptxExportReport rep)
        {
            string name = "slide" + idx + ".xml";
            slideFiles.Add(name);

            var resolved = ResolvedSlide.Resolve(el, scn, project, baseDir);
            long bg = ColorHex(resolved.Background.Color);

            var spTree = new StringBuilder();
            var slideExtraRels = new StringBuilder();

            // imagen o video → pic
            if ((el.Kind == ElementKind.Image || el.Kind == ElementKind.Video) && !string.IsNullOrEmpty(el.Src))
            {
                string abs = ResolveSrc(baseDir, el.Src);
                if (File.Exists(abs))
                {
                    string ext = Path.GetExtension(abs).ToLowerInvariant();
                    string contentType = ext == ".png" ? "image/png" : ext == ".gif" ? "image/gif" :
                                         ext == ".bmp" ? "image/bmp" : "image/jpeg";
                    string mediaPart = "/ppt/media/img" + idx + ext;
                    var mp = pkg.CreatePart(new Uri(mediaPart, UriKind.Relative), contentType);
                    using (var s = File.OpenRead(abs))
                    using (var o = mp.GetStream(FileMode.Create))
                    {
                        var buf = new byte[8192];
                        int r2;
                        while ((r2 = s.Read(buf, 0, buf.Length)) > 0) o.Write(buf, 0, r2);
                    }
                    // La relación r:embed se declara en el SLIDE (no en el paquete)
                    slideExtraRels.Append(Rel("rIdImg1",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image",
                        "../media/img" + idx + ext));
                    long cx = (long)(el.W * PresW), cy = (long)(el.H * PresH);
                    long x = (long)(el.X * PresW), y = (long)(el.Y * PresH);
                    spTree.Append("<p:pic><p:nvPicPr><p:cNvPr id=\"2\" name=\"Medio\"/>" +
                                  "<p:cNvPicPr/><p:nvPr/></p:nvPicPr>" +
                                  "<p:blipFill><a:blip r:embed=\"rIdImg1\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>" +
                                  "<p:spPr><a:xfrm><a:off x=\"" + x + "\" y=\"" + y + "\"/>" +
                                  "<a:ext cx=\"" + cx + "\" cy=\"" + cy + "\"/></a:xfrm>" +
                                  "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>");
                    if (el.Kind == ElementKind.Video)
                        rep.Warnings.Add("Los videos se exportan como fotograma estático (imagen) en el MVP.");
                }
                else rep.Warnings.Add("Medio no encontrado: " + el.Src);
            }

            // texto: la línea activa (o la única línea) en grande
            if (singleLine != null)
            {
                var st = resolved.Style;
                long sz = (long)Math.Round((st.Size ?? 48) * 100);   // centésimas de punto [SPEC §9.3.1]
                long x = (long)((st.BoxX ?? 0.05) * PresW);
                long y = (long)((st.BoxY ?? 0.08) * PresH);
                long cx = (long)((st.BoxW ?? 0.9) * PresW);
                long cy = (long)((st.BoxH ?? 0.84) * PresH);
                string align = (st.Align ?? 1) == 0 ? "l" : (st.Align ?? 1) == 2 ? "r" : "ctr";
                string escaped = EscapeXml(singleLine);
                spTree.Append("<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Texto\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>" +
                    "<p:spPr><a:xfrm><a:off x=\"" + x + "\" y=\"" + y + "\"/><a:ext cx=\"" + cx + "\" cy=\"" + cy + "\"/></a:xfrm>" +
                    "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr>" +
                    "<p:txBody><a:bodyPr anchor=\"ctr\"/><a:lstStyle/><a:p>" +
                    "<a:pPr algn=\"" + align + "\"/>" +
                    "<a:r><a:rPr lang=\"es-VE\" sz=\"" + sz + "\"" + ((st.Bold ?? false) ? " b=\"1\"" : "") +
                    ((st.Italic ?? false) ? " i=\"1\"" : "") + " dirty=\"0\">" +
                    "<a:solidFill><a:srgbClr val=\"" + ColorHex(st.Color ?? "#FFFFFF") + "\"/></a:solidFill>" +
                    "<a:latin typeface=\"" + EscapeXml(st.Font ?? "Segoe UI") + "\"/></a:rPr>" +
                    "<a:t>" + escaped + "</a:t></a:r></a:p></p:txBody></p:sp>");
                // referencia bíblica bajo el texto
                if (el.Kind == ElementKind.Verse && !string.IsNullOrEmpty(resolved.Reference))
                {
                    long ry = y + cy - (long)(PresH * 0.07);
                    spTree.Append("<p:sp><p:nvSpPr><p:cNvPr id=\"4\" name=\"Ref\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>" +
                        "<p:spPr><a:xfrm><a:off x=\"" + x + "\" y=\"" + ry + "\"/><a:ext cx=\"" + cx + "\" cy=\"" + (long)(PresH * 0.06) + "\"/></a:xfrm>" +
                        "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr>" +
                        "<p:txBody><a:bodyPr anchor=\"ctr\"/><a:lstStyle/><a:p><a:pPr algn=\"ctr\"/>" +
                        "<a:r><a:rPr lang=\"es-VE\" sz=\"1600\" i=\"1\" dirty=\"0\">" +
                        "<a:solidFill><a:srgbClr val=\"" + ColorHex(st.ActiveColor ?? "#FFD700") + "\"/></a:solidFill></a:rPr>" +
                        "<a:t>" + EscapeXml(resolved.Reference) + "</a:t></a:r></a:p></p:txBody></p:sp>");
                }
            }

            var slideXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<p:sld xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">" +
                "<p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val=\"" + bg + "\"/></a:solidFill>" +
                "<a:effectLst/></p:bgPr></p:bg>" +
                "<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
                "<p:grpSpPr/>" + spTree + "</p:spTree></p:cSld>" +
                "<p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>";
            AddPart(pkg, new Uri("/ppt/slides/" + name, UriKind.Relative),
                    "application/vnd.openxmlformats-officedocument.presentationml.slide+xml", slideXml);
            AddPart(pkg, new Uri("/ppt/slides/_rels/" + name + ".rels", UriKind.Relative),
                    "application/vnd.openxmlformats-package.relationships+xml",
                    RelsXmlRaw(Rel("rId1",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout",
                        "../slideLayouts/slideLayout1.xml") + slideExtraRels.ToString()));
        }

        static string ResolveSrc(string baseDir, string rel)
        {
            if (Path.IsPathRooted(rel)) return rel;
            return Path.GetFullPath(Path.Combine(baseDir != null ? baseDir : ".", rel));
        }

        static long ColorHex(string hex)
        {
            try
            {
                string h = (hex ?? "").TrimStart('#');
                if (h.Length == 8) h = h.Substring(2);
                if (h.Length != 6) return 0x000000;
                return long.Parse(h, System.Globalization.NumberStyles.HexNumber);
            }
            catch { return 0; }
        }

        static string EscapeXml(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                            .Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        static void AddPart(Package pkg, Uri uri, string contentType, string xml)
        {
            if (pkg.PartExists(uri)) pkg.DeletePart(uri);
            var part = pkg.CreatePart(uri, contentType);
            using (var w = new StreamWriter(part.GetStream(FileMode.Create), new UTF8Encoding(false)))
                w.Write(xml);
        }

        static string Rel(string id, string type, string target)
        {
            return "<Relationship Id=\"" + id + "\" Type=\"" + type + "\" Target=\"" + target + "\"/>";
        }

        private class RelEntry
        {
            public string Id, Type, Target;
            public RelEntry(string id, string type, string target) { Id = id; Type = type; Target = target; }
        }

        static string RelsXml(RelEntry[] rels)
        {
            var sb = new StringBuilder();
            foreach (var r in rels) sb.Append(Rel(r.Id, r.Type, r.Target));
            return RelsXmlRaw(sb.ToString());
        }

        static string RelsXmlRaw(string inner)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   inner + "</Relationships>";
        }

        static string MasterXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<p:sldMaster xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">" +
                "<p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
                "<p:grpSpPr/></p:spTree></p:cSld>" +
                "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" " +
                "accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
                "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId2\"/></p:sldLayoutIdLst>" +
                "</p:sldMaster>";
        }

        static string LayoutXml()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<p:sldLayout xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" type=\"blank\" " +
                "preserve=\"1\"><p:cSld name=\"En blanco\"><p:spTree>" +
                "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>" +
                "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>";
        }

        static string ThemeXml(AhpProject project)
        {
            // theme/ derivado del tema ahp.v1 [SPEC §9.3.1]
            var theme = project != null ? project.ActiveTheme() : null;
            string accent = "C43E1C";
            string bg = "101820";
            if (theme != null)
            {
                if (!string.IsNullOrEmpty(theme.Background.Color)) bg = ColorHexString(theme.Background.Color);
                if (!string.IsNullOrEmpty(theme.Style.ActiveColor)) accent = ColorHexString(theme.Style.ActiveColor);
            }
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Fusion HP\">" +
                "<a:themeElements><a:clrScheme name=\"Fusion\">" +
                "<a:dk1><a:srgbClr val=\"" + accent + "\"/></a:dk1>" +
                "<a:lt1><a:srgbClr val=\"FFFFFF\"/></a:lt1>" +
                "<a:dk2><a:srgbClr val=\"" + bg + "\"/></a:dk2>" +
                "<a:lt2><a:srgbClr val=\"" + bg + "\"/></a:lt2>" +
                "<a:accent1><a:srgbClr val=\"" + accent + "\"/></a:accent1>" +
                "<a:accent2><a:srgbClr val=\"" + accent + "\"/></a:accent2>" +
                "<a:accent3><a:srgbClr val=\"605E5C\"/></a:accent3>" +
                "<a:accent4><a:srgbClr val=\"8A8886\"/></a:accent4>" +
                "<a:accent5><a:srgbClr val=\"C8C6C4\"/></a:accent5>" +
                "<a:accent6><a:srgbClr val=\"" + bg + "\"/></a:accent6>" +
                "<a:hlink><a:srgbClr val=\"" + accent + "\"/></a:hlink>" +
                "<a:folHlink><a:srgbClr val=\"6B7A8F\"/></a:folHlink>" +
                "</a:clrScheme><a:fontScheme name=\"Fusion\">" +
                "<a:majorFont><a:latin typeface=\"Segoe UI\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
                "<a:minorFont><a:latin typeface=\"Segoe UI\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont>" +
                "</a:fontScheme><a:fmtScheme name=\"Fusion\">" +
                "<a:fillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
                "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
                "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:fillStyleLst>" +
                "<a:lnStyleLst><a:ln><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln>" +
                "<a:ln><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln>" +
                "<a:ln><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln></a:lnStyleLst>" +
                "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle>" +
                "<a:effectStyle><a:effectLst/></a:effectStyle>" +
                "<a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>" +
                "<a:bgFillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
                "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
                "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:bgFillStyleLst>" +
                "</a:fmtScheme></a:themeElements></a:theme>";
        }

        static string ColorHexString(string hex)
        {
            string h = (hex ?? "").TrimStart('#');
            if (h.Length == 8) h = h.Substring(2);
            return h.Length == 6 ? h : "101820";
        }
    }
}
