// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  PptxExporter.cs : exportación de Escenarios a PPTX (ISO/IEC-29500 OpenXML)
//  SIN dependencias externas — OPC empaquetado por ZipWriter PROPIO:
//    * v5.1.0: System.IO.Packaging (WindowsBase.dll) eliminado de raíz:
//      su ensamblado resuelve distinto entre CLR2 (3.0) y CLR4 (4.0) y ataba
//      la variante net35 al GAC 3.x. El paquete OPC se escribe ahora con el
//      mismo motor ZIP del respaldo (ZipWriter: PKWARE APPNOTE.TXT) y corre
//      IDÉNTICO bajo .NET 3.5 y .NET 4.8.
//
//  Cumple el estándar PresentationML mínimo VÁLIDO para PowerPoint:
//    [Content_Types].xml · _rels/.rels · ppt/presentation.xml (+rels)
//    ppt/slideMasters/slideMaster1.xml (+rels) · ppt/slideLayouts/slideLayout1.xml (+rels)
//    ppt/theme/theme1.xml · ppt/slides/slideN.xml (+rels) · ppt/media/imageN.{png|jpg}
//
//  Unidades (requisito del spec): EMU — 1 in = 914400 EMU; tipografía en
//  centésimas de punto (sz="3200" = 32 pt). Diapositiva 16:9 = 12192000×6858000.
//
//  El tema de LuminaPresentation se traduce 1:1: fondo (color o imagen a
//  pantalla completa), tipografía (familia/tamaño/negrita/MAYÚSCULAS/interlineado),
//  color de texto y acento (referencia bíblica / rótulos).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace lumina.core
{
    /// <summary>Datos de una diapositiva para exportar (vía SlideView o manual).</summary>
    public sealed class ExportSlide
    {
        public int Kind = SlideKind.Text;      // SlideKind.*
        public string Title = string.Empty;    // título (slide TITLE: grande)
        public string Subtitle = string.Empty; // artista/tono (slide TITLE: pequeño)
        public string RefLabel = string.Empty; // referencia (SCRIPTURE: pie)
        public List<string> Lines = new List<string>();
        public string ImagePath = string.Empty; // imagen de la slide (IMAGE)

        public ExportSlide() {}

        public ExportSlide(SlideView v)
        {
            if (v == null) return;
            Kind = SlideKind.Text;
            Title = v.Title;
            RefLabel = v.RefLabel;
            Lines = new List<string>(v.Lines);
        }
    }

    public static class PptxExporter
    {
        // ---------------- geometría (EMU) ----------------
        private const long SlideW = 12192000;   // 13.333 in (16:9)
        private const long SlideH = 6858000;    // 7.5 in
        private const long Margin = 457200;     // 0.5 in

        /// <summary>
        /// Exporta a un archivo .pptx. Devuelve null si OK; si falla, mensaje
        /// claro (el llamador decide cómo mostrarlo).
        /// </summary>
        public static string ExportToFile(string path, string scenarioName,
                                          IList<ExportSlide> slides, Theme theme)
        {
            try
            {
                byte[] zip = ExportToBytes(scenarioName, slides, theme);
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(zip, 0, zip.Length);
                }
                return null;
            }
            catch (Exception ex)
            {
                return "No se pudo escribir el PPTX (" + ex.Message + ").";
            }
        }

        /// <summary>Exporta en memoria (tests / validación de estructura).</summary>
        public static byte[] ExportToBytes(string scenarioName, IList<ExportSlide> slides, Theme theme)
        {
            if (slides == null) slides = new List<ExportSlide>();
            if (theme == null) theme = new Theme();

            // v5.1.0: OPC propio (dict nombre→(tipo, contenido)) — sin WindowsBase.
            OpcParts parts = new OpcParts();
            {
                string bgImagePart = AddImageIfNeeded(parts, theme.ImagePath);
                string[] slideImageParts = new string[slides.Count];
                for (int i = 0; i < slides.Count; i++)
                {
                    ExportSlide s = slides[i] ?? new ExportSlide();
                    slideImageParts[i] = AddImageIfNeeded(parts, s.ImagePath);
                }

                AddXmlPart(parts, "/_rels/.rels", CtRels,
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"ppt/presentation.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                    "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/>" +
                    "</Relationships>");

                // docProps (metadatos: requisito del spec §4.1)
                AddXmlPart(parts, "/docProps/core.xml", CtCore,
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                    "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
                    "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                    "<dc:title>" + XmlText(scenarioName) + "</dc:title>" +
                    "<dc:creator>LuminaPresentation Suite</dc:creator>" +
                    "<cp:lastModifiedBy>LuminaPresentation Suite</cp:lastModifiedBy>" +
                    "</cp:coreProperties>");
                AddXmlPart(parts, "/docProps/app.xml", CtApp,
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" " +
                    "xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
                    "<Application>LuminaPresentation Suite</Application>" +
                    "</Properties>");

                // ppt/presentation.xml (+ rels)
                StringBuilder pres = new StringBuilder();
                pres.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<p:presentation xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                    "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                    "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" " +
                    "saveSubsetFonts=\"1\">" +
                    "<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst>" +
                    "<p:sldIdLst>");
                for (int i = 1; i <= slides.Count; i++)
                    pres.Append("<p:sldId id=\"" + (256 + i) + "\" r:id=\"rId" + (i + 1) + "\"/>");
                pres.Append("</p:sldIdLst>" +
                    "<p:sldSz cx=\"" + SlideW + "\" cy=\"" + SlideH + "\"/>" +
                    "<p:notesSz cx=\"" + SlideH + "\" cy=\"" + SlideW + "\"/>" +
                    "</p:presentation>");
                AddXmlPart(parts, "/ppt/presentation.xml", CtPresentation, pres.ToString());

                StringBuilder presRels = new StringBuilder();
                presRels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster\" Target=\"slideMasters/slideMaster1.xml\"/>");
                for (int i = 1; i <= slides.Count; i++)
                    presRels.Append("<Relationship Id=\"rId" + (i + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide" + i + ".xml\"/>");
                presRels.Append("<Relationship Id=\"rId" + (slides.Count + 2) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme\" Target=\"theme/theme1.xml\"/>" +
                    "</Relationships>");
                AddXmlPart(parts, "/ppt/_rels/presentation.xml.rels", CtRels, presRels.ToString());

                WriteTheme(parts, theme);
                WriteMasterAndLayout(parts, theme);

                for (int i = 0; i < slides.Count; i++)
                {
                    ExportSlide s = slides[i] ?? new ExportSlide();
                    string slideRels;
                    string xml = BuildSlideXml(s, theme, slideImageParts[i], bgImagePart, out slideRels);
                    AddXmlPart(parts, "/ppt/slides/slide" + (i + 1) + ".xml", CtSlide, xml);
                    AddXmlPart(parts, "/ppt/slides/_rels/slide" + (i + 1) + ".xml.rels", CtRels, slideRels);
                }
            }
            return BuildOpcZip(parts);
        }

        // ---------------- contenedor OPC propio (v5.1.0, sin WindowsBase) ----

        private sealed class OpcPart
        {
            public string ContentType;
            public byte[] Data;
        }

        private sealed class OpcParts
        {
            public readonly Dictionary<string, OpcPart> Map =
                new Dictionary<string, OpcPart>(StringComparer.Ordinal);

            public void Add(string name, string contentType, byte[] data)
            {
                OpcPart p = new OpcPart();
                p.ContentType = contentType;
                p.Data = data;
                Map[name] = p;
            }

            public int CountMedia()
            {
                int n = 0;
                foreach (string k in Map.Keys)
                    if (k.StartsWith("/ppt/media/image", StringComparison.Ordinal)) n++;
                return n;
            }
        }

        /// <summary>
        /// Genera [Content_Types].xml (Default por extensión + Override por parte
        /// XML) y serializa el paquete con ZipWriter — la MISMA estructura que
        /// producía System.IO.Packaging, ahora verificada por python-pptx en CI.
        /// </summary>
        private static byte[] BuildOpcZip(OpcParts parts)
        {
            StringBuilder types = new StringBuilder();
            types.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            bool hasRels = false, hasPng = false, hasJpeg = false, hasXml = false;
            foreach (KeyValuePair<string, OpcPart> kv in parts.Map)
            {
                string k = kv.Key.ToLowerInvariant();
                if (k.EndsWith(".rels", StringComparison.Ordinal)) hasRels = true;
                else if (k.EndsWith(".png", StringComparison.Ordinal)) hasPng = true;
                else if (k.EndsWith(".jpeg", StringComparison.Ordinal)) hasJpeg = true;
                else if (k.EndsWith(".xml", StringComparison.Ordinal)) hasXml = true;
            }
            types.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            if (hasPng) types.Append("<Default Extension=\"png\" ContentType=\"image/png\"/>");
            if (hasJpeg) types.Append("<Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>");
            if (hasXml) types.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            // Override por cada parte XML con su tipo REAL (más específico que el
            // Default «application/xml»: es la regla del estándar OPC).
            foreach (KeyValuePair<string, OpcPart> kv in parts.Map)
            {
                if (kv.Key.EndsWith(".xml", StringComparison.Ordinal))
                    types.Append("<Override PartName=\"" + kv.Key + "\" ContentType=\"" +
                                 kv.Value.ContentType + "\"/>");
            }
            types.Append("</Types>");

            List<KeyValuePair<string, byte[]>> entries =
                new List<KeyValuePair<string, byte[]>>(parts.Map.Count + 1);
            entries.Add(new KeyValuePair<string, byte[]>("[Content_Types].xml",
                new UTF8Encoding(false).GetBytes(types.ToString())));
            foreach (KeyValuePair<string, OpcPart> kv in parts.Map)
                entries.Add(new KeyValuePair<string, byte[]>(kv.Key, kv.Value.Data));

            MemoryStream ms = new MemoryStream();
            ZipWriter.WriteEntries(ms, entries);
            return ms.ToArray();
        }

        // ---------------- content types reales (para [Content_Types].xml) ----
        private const string CtRels = "application/vnd.openxmlformats-package.relationships+xml";
        private const string CtCore = "application/vnd.openxmlformats-package.core-properties+xml";
        private const string CtApp = "application/vnd.openxmlformats-officedocument.extended-properties+xml";
        private const string CtPresentation = "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml";
        private const string CtSlide = "application/vnd.openxmlformats-officedocument.presentationml.slide+xml";
        private const string CtMaster = "application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml";
        private const string CtLayout = "application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml";
        private const string CtTheme = "application/vnd.openxmlformats-officedocument.theme+xml";

        // ================================================================ media

        /// <summary>
        /// Añade la imagen (si existe y es PNG/JPEG) y devuelve su nombre de parte
        /// ("/ppt/media/imageN.ext") o null.
        /// </summary>
        private static string AddImageIfNeeded(OpcParts parts, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch (Exception) { return null; }
            string ext, ctype;
            if (!SniffImage(data, out ext, out ctype)) return null;

            string partName = "/ppt/media/image" + (parts.CountMedia() + 1) + "." + ext;
            parts.Add(partName, ctype, data);
            return partName;
        }

        /// <summary>Detecta PNG/JPEG por firma (tolerante: lo demás se ignora).</summary>
        private static bool SniffImage(byte[] d, out string ext, out string ctype)
        {
            ext = null; ctype = null;
            if (d == null || d.Length < 8) return false;
            if (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)
            { ext = "png"; ctype = "image/png"; return true; }
            if (d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF)
            { ext = "jpeg"; ctype = "image/jpeg"; return true; }
            return false;
        }

        // ================================================================ tema

        private static void WriteTheme(OpcParts parts, Theme t)
        {
            string accent = HexNoAlpha(t.AccentColor, "3AA6B9");
            string fg = HexNoAlpha(t.FgColor, "FFFFFF");
            string bg = HexNoAlpha(t.BgColor, "0B1F2A");
            string themeXml =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Lumina\">" +
                "<a:themeElements>" +
                "<a:clrScheme name=\"Lumina\">" +
                "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1>" +
                "<a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
                "<a:dk2><a:srgbClr val=\"" + bg + "\"/></a:dk2>" +
                "<a:lt2><a:srgbClr val=\"" + accent + "\"/></a:lt2>" +
                "<a:accent1><a:srgbClr val=\"" + accent + "\"/></a:accent1>" +
                "<a:accent2><a:srgbClr val=\"" + fg + "\"/></a:accent2>" +
                "<a:accent3><a:srgbClr val=\"" + accent + "\"/></a:accent3>" +
                "<a:accent4><a:srgbClr val=\"" + bg + "\"/></a:accent4>" +
                "<a:accent5><a:srgbClr val=\"" + accent + "\"/></a:accent5>" +
                "<a:accent6><a:srgbClr val=\"" + fg + "\"/></a:accent6>" +
                "<a:hlink><a:srgbClr val=\"" + accent + "\"/></a:hlink>" +
                "<a:folHlink><a:srgbClr val=\"" + accent + "\"/></a:folHlink>" +
                "</a:clrScheme>" +
                "<a:fontScheme name=\"Lumina\">" +
                "<a:majorFont><a:latin typeface=\"" + XmlAttr(t.FontFace) + "\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
                "<a:minorFont><a:latin typeface=\"" + XmlAttr(t.FontFace) + "\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont>" +
                "</a:fontScheme>" +
                "<a:fmtScheme name=\"Lumina\">" +
                "<a:fillStyleLst>" +
                "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
                "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
                "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
                "</a:fillStyleLst>" +
                "<a:lnStyleLst>" +
                "<a:ln w=\"6350\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/></a:ln>" +
                "<a:ln w=\"12700\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/></a:ln>" +
                "<a:ln w=\"19050\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/></a:ln>" +
                "</a:lnStyleLst>" +
                "<a:effectStyleLst>" +
                "<a:effectStyle><a:effectLst/></a:effectStyle>" +
                "<a:effectStyle><a:effectLst/></a:effectStyle>" +
                "<a:effectStyle><a:effectLst/></a:effectStyle>" +
                "</a:effectStyleLst>" +
                "<a:bgFillStyleLst>" +
                "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
                "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
                "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
                "</a:bgFillStyleLst>" +
                "</a:fmtScheme>" +
                "</a:themeElements>" +
                "</a:theme>";
            AddXmlPart(parts, "/ppt/theme/theme1.xml", CtTheme, themeXml);
        }

        private static void WriteMasterAndLayout(OpcParts parts, Theme t)
        {
            string bg = HexNoAlpha(t.BgColor, "0B1F2A");
            string master =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<p:sldMaster xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">" +
                "<p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val=\"" + bg + "\"/></a:solidFill><a:effectLst/></p:bgPr></p:bg>" +
                "<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
                "<p:grpSpPr/></p:spTree></p:cSld>" +
                "<p:clrMap bg1=\"dk1\" tx1=\"lt1\" bg2=\"dk2\" tx2=\"lt2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
                "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst>" +
                "</p:sldMaster>";
            AddXmlPart(parts, "/ppt/slideMasters/slideMaster1.xml", CtMaster, master);
            AddXmlPart(parts, "/ppt/slideMasters/_rels/slideMaster1.xml.rels", CtRels,
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme\" Target=\"../theme/theme1.xml\"/>" +
                "</Relationships>");

            string layout =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<p:sldLayout xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" type=\"blank\" preserve=\"1\">" +
                "<p:cSld name=\"Lumina\">" +
                "<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/></p:spTree>" +
                "</p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>";
            AddXmlPart(parts, "/ppt/slideLayouts/slideLayout1.xml", CtLayout, layout);
            AddXmlPart(parts, "/ppt/slideLayouts/_rels/slideLayout1.xml.rels", CtRels,
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster\" Target=\"../slideMasters/slideMaster1.xml\"/>" +
                "</Relationships>");
        }

        // ================================================================ slides

        private static string BuildSlideXml(ExportSlide s, Theme t, string imgPart, string bgImagePart,
                                            out string rels)
        {
            StringBuilder r = new StringBuilder();
            r.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/>");
            int relNext = 2;

            StringBuilder b = new StringBuilder();
            b.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<p:sld xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">" +
                "<p:cSld><p:bg>");
            if (bgImagePart != null)
            {
                string rid = "rId" + (relNext++);
                r.Append("<Relationship Id=\"" + rid + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"" +
                    bgImagePart.Replace("/ppt/", "../") + "\"/>");
                b.Append("<p:bgPr><a:blipFill><a:blip r:embed=\"" + rid + "\"/><a:stretch><a:fillRect/></a:stretch></a:blipFill><a:effectLst/></p:bgPr>");
            }
            else
            {
                b.Append("<p:bgPr><a:solidFill><a:srgbClr val=\"" + HexNoAlpha(t.BgColor, "0B1F2A") +
                    "\"/></a:solidFill><a:effectLst/></p:bgPr>");
            }
            b.Append("</p:bg><p:spTree>" +
                "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>");

            long shapeId = 2;

            // Imagen de la slide (área útil central con margen)
            if (imgPart != null)
            {
                string rid = "rId" + (relNext++);
                r.Append("<Relationship Id=\"" + rid + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"" +
                    imgPart.Replace("/ppt/", "../") + "\"/>");
                b.Append("<p:pic><p:nvPicPr><p:cNvPr id=\"" + (shapeId++) + "\" name=\"Imagen\"/>" +
                    "<p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>" +
                    "<p:blipFill><a:blip r:embed=\"" + rid + "\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>" +
                    "<p:spPr><a:xfrm><a:off x=\"" + Margin + "\" y=\"" + Margin + "\"/>" +
                    "<a:ext cx=\"" + (SlideW - 2 * Margin) + "\" cy=\"" + (SlideH - 2 * Margin) + "\"/></a:xfrm>" +
                    "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>");
            }

            // Texto
            List<string> lines = new List<string>();
            string mainTitle = string.Empty;
            int kind = s.Kind;
            if (kind == SlideKind.Title)
            {
                mainTitle = s.Title;
                if (s.Subtitle.Length > 0) lines.Add(s.Subtitle);
            }
            else
            {
                foreach (string l in s.Lines)
                {
                    string txt = l == null ? string.Empty : l.Trim();
                    if (txt.Length > 0) lines.Add(t.Uppercase ? txt.ToUpperInvariant() : txt);
                }
            }

            if (kind == SlideKind.Title && mainTitle.Length > 0)
            {
                b.Append(TextBoxXml(shapeId++, Margin, SlideH / 6,
                    SlideW - 2 * Margin, SlideH / 2, "ctr",
                    ParagraphRun(mainTitle, t, t.FontSize > 40 ? t.FontSize : 80, true, false)));
            }
            if (lines.Count > 0)
            {
                long bodyTop = kind == SlideKind.Title ? (long)(SlideH * 0.55) : (long)(SlideH * 0.18);
                long bodyH = (long)(SlideH * (kind == SlideKind.Title ? 0.30 : 0.64));
                StringBuilder body = new StringBuilder();
                for (int i = 0; i < lines.Count; i++)
                    body.Append(ParagraphRun(lines[i], t, t.FontSize, false, i > 0));
                b.Append(TextBoxXml(shapeId++, Margin, bodyTop, SlideW - 2 * Margin, bodyH,
                    kind == SlideKind.Scripture ? "l" : "ctr", body.ToString()));
            }

            // Referencia bíblica al pie (acentuada — espejo del motor nativo)
            if (kind == SlideKind.Scripture && s.RefLabel.Length > 0)
            {
                b.Append(TextBoxXml(shapeId++, Margin, SlideH - SlideH / 9,
                    SlideW - 2 * Margin, SlideH / 12, "r",
                    ParagraphRun(s.RefLabel, t, 20, false, false, true)));
            }
            // Rótulo de bloque sobre el cuerpo (canciones)
            else if (kind != SlideKind.Title && kind != SlideKind.Scripture &&
                     s.RefLabel.Length > 0 && lines.Count > 0)
            {
                b.Append(TextBoxXml(shapeId++, Margin, SlideH / 14, SlideW - 2 * Margin, SlideH / 10,
                    "ctr", ParagraphRun(s.RefLabel, t, 24, false, false, true)));
            }

            b.Append("</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>");
            r.Append("</Relationships>");
            rels = r.ToString();
            return b.ToString();
        }

        /// <summary>Un párrafo con un run. sizePx en px@1080p → puntos (1 px = 0.5 pt).</summary>
        private static string ParagraphRun(string text, Theme t, int sizePx, bool bold, bool spacingBefore,
                                           bool accent = false)
        {
            string color = accent ? HexNoAlpha(t.AccentColor, "3AA6B9") : HexNoAlpha(t.FgColor, "FFFFFF");
            bool isBold = bold || t.Bold;
            int pt = Math.Max(8, Math.Min(400, sizePx / 2));
            int sz = pt * 100;   // centésimas de punto (spec §4.2)
            double spacing = t.LineSpacing <= 0 ? 1.18 : t.LineSpacing;
            int lnPct = (int)Math.Round(spacing * 100000.0);
            int spcB = spacingBefore ? (int)(pt * 0.6 * 100.0 * (spacing - 1.0)) : 0;
            return "<a:p>" +
                   "<a:pPr" + (spcB > 0 ? " spcBef=\"" + spcB + "\"" : string.Empty) + ">" +
                   "<a:lnSpc><a:spcPct val=\"" + lnPct + "\"/></a:lnSpc></a:pPr>" +
                   "<a:r><a:rPr lang=\"es-VE\" sz=\"" + sz + "\"" + (isBold ? " b=\"1\"" : string.Empty) + ">" +
                   "<a:solidFill><a:srgbClr val=\"" + color + "\"/></a:solidFill>" +
                   "<a:latin typeface=\"" + XmlAttr(t.FontFace) + "\"/></a:rPr>" +
                   "<a:t>" + XmlText(text) + "</a:t></a:r></a:p>";
        }

        private static string TextBoxXml(long id, long x, long y, long cx, long cy,
                                         string align, string paragraphs)
        {
            return "<p:sp><p:nvSpPr><p:cNvPr id=\"" + id + "\" name=\"Texto " + id + "\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr>" +
                   "<p:spPr><a:xfrm><a:off x=\"" + x + "\" y=\"" + y + "\"/><a:ext cx=\"" + cx + "\" cy=\"" + cy + "\"/></a:xfrm>" +
                   "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/></p:spPr>" +
                   "<p:txBody><a:bodyPr wrap=\"square\" anchor=\"ctr\" vert=\"horz\"/><a:lstStyle/>" +
                   paragraphs + "</p:txBody></p:sp>";
        }

        // ================================================================ utils

        private static string HexNoAlpha(string argb, string fallback)
        {
            if (string.IsNullOrEmpty(argb)) return fallback;
            string h = argb.TrimStart('#');
            if (h.Length == 8) h = h.Substring(2);
            if (h.Length != 6) return fallback;
            foreach (char c in h)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return fallback;
            }
            return h.ToUpperInvariant();
        }

        private static string XmlText(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string XmlAttr(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return XmlText(s).Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private static void AddXmlPart(OpcParts parts, string name, string contentType, string xml)
        {
            parts.Add(name, contentType, new UTF8Encoding(false).GetBytes(xml));
        }
    }
}
