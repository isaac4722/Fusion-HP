// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  PptxImporter.cs (v6.0.0 «HORIZONTE») : importación de PPTX (roadmap:
//  «leer PresentationML con el mismo motor OPC propio y mapear sp de texto
//  a ítems de texto»). Lee el paquete con ZipReader (motor ZIP casero) y
//  extrae las diapositivas en ORDEN: ppt/presentation.xml → sldIdLst →
//  rels → slideN.xml exacto (el orden real de PowerPoint, NO el numérico).
//
//  Contrato de salida: PptxSlide {Lines} — cada <a:p> (párrafo) de cada
//  <p:sp> de la diapositiva es una línea (runs <a:t> concatenados). Una
//  diapositiva 1:1 con un ítem de texto del culto mantiene la fidelidad de
//  proyección (MaxLinesPerSlide = líneas de la slide).
//
//  Tolerante a la basura de PowerPoint real:
//   * [Content_Types].xml puede faltar (los nombres se validan por patrón).
//   * Espacios de nombres estándar p:/a:/r: con prefijos arbitrarios (se
//     resuelven por LocalName, no por prefijo literal).
//   * <a:br/> → '\n'; <a:fld> (campos) aporta su texto; formas sin txBody
//     (imágenes/gráficos) se omiten silenciosamente.
//   * Entradas de slide de tamaño absurdo → descartadas (defensa).
//
//  F4.15 (v1.0.0-beta.1): el resultado expone `Report` (FidelityReport) con
//  unidades convertidas, omisiones (tablas/gráficos SmartArt), animaciones
//  descartadas y MACROS detectadas (ppt/vbaProject.bin — detectadas y JAMÁS
//  ejecutadas, F4.16.3). Las firmas existentes NO cambian: el informe viaja
//  como campo del resultado ya existente.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace lumina.core
{
    /// <summary>Una diapositiva importada: líneas de texto en orden visual.</summary>
    public sealed class PptxSlide
    {
        /// <summary>Líneas (un <a:p> por línea; runs concatenados).</summary>
        public List<string> Lines = new List<string>();
    }

    /// <summary>Resultado de la importación.</summary>
    public sealed class PptxImportResult
    {
        /// <summary>Diapositivas en el ORDEN de presentación (sldIdLst).</summary>
        public List<PptxSlide> Slides = new List<PptxSlide>();
        /// <summary>Título de la presentación (dc:title del core.xml o vacío).</summary>
        public string Title = string.Empty;
        /// <summary>F4.15: informe de fidelidad de la importación (siempre
        /// presente; sin omisiones si el paquete era fiel 1:1).</summary>
        public FidelityReport Report = new FidelityReport();
    }

    public static class PptxImporter
    {
        private const int MaxSlideXmlBytes = 16 * 1024 * 1024;   // 16 MB por slide

        /// <summary>Importa desde un archivo .pptx. Lanza excepciones descriptivas.</summary>
        public static PptxImportResult Import(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Ruta vacía.");
            if (!File.Exists(path)) throw new FileNotFoundException("No se encuentra el archivo .pptx.", path);
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return Import(fs);
            }
        }

        /// <summary>Importa desde el stream (se lee completo; ver ZipReader.ReadAll).</summary>
        public static PptxImportResult Import(Stream input)
        {
            List<ZipEntryData> entries = ZipReader.ReadAll(input);
            Dictionary<string, byte[]> byName = new Dictionary<string, byte[]>(entries.Count, StringComparer.OrdinalIgnoreCase);
            foreach (ZipEntryData e in entries)
            {
                if (e.Name != null && !byName.ContainsKey(e.Name)) byName[e.Name] = e.Data;
            }

            PptxImportResult res = new PptxImportResult();
            res.Title = ReadCoreTitle(byName);
            res.Report.Source = "pptx-import";

            // F4.15 — MACROS detectadas (ppt/vbaProject.bin o partes .bin de
            // VBA): se REPORTAN y NO se ejecutan (F4.16.3: pptm sin macros).
            foreach (KeyValuePair<string, byte[]> kv in byName)
            {
                string n = kv.Key == null ? "" : kv.Key.Replace('\\', '/').ToLowerInvariant();
                if (n.EndsWith("/vbaproject.bin", StringComparison.Ordinal) ||
                    n.EndsWith("vbaproject.bin", StringComparison.Ordinal))
                {
                    res.Report.AddMacro("proyecto VBA detectado en " + kv.Key +
                        " (detectado, no ejecutado)");
                }
            }

            // Orden REAL de las diapositivas: presentation.xml.sldIdLst →
            // presentation.xml.rels (rId → ppt/slides/slideN.xml).
            List<string> ordered = SlideOrder(byName);
            int slideNo = 0;
            foreach (string slideName in ordered)
            {
                byte[] data;
                if (!byName.TryGetValue(slideName, out data)) continue;
                if (data == null || data.Length == 0 || data.Length > MaxSlideXmlBytes)
                {
                    res.Report.AddOmission("diapositiva " + (slideNo + 1) +
                        " descartada (parte vacía o demasiado grande)");
                    slideNo++;
                    continue;
                }
                slideNo++;
                PptxSlide slide = ParseSlideXml(data, slideNo, res.Report);
                if (slide != null) res.Slides.Add(slide);
            }
            // F4.15: resultado final — unidades convertidas + herencia (el
            // tema viaja 1:1 con el paquete; la herencia se resuelve al
            // proyectar, aquí se reporta la del tema leído del master).
            res.Report.AddConverted(res.Slides.Count,
                "diapositivas convertidas a texto (una slide = un ítem)");
            return res;
        }

        /* ------------------------------------------------ orden sldIdLst -- */

        private static List<string> SlideOrder(Dictionary<string, byte[]> byName)
        {
            List<string> ordered = new List<string>();
            byte[] pres;
            if (!byName.TryGetValue("ppt/presentation.xml", out pres) || pres == null)
            {
                // Sin presentation.xml (paquete raro): fallback numérico.
                foreach (string k in byName.Keys)
                    if (IsSlidePath(k)) ordered.Add(k);
                ordered.Sort(SlideNameComparer);
                return ordered;
            }

            // rels: rId → target (ppt/slides/slideN.xml)
            Dictionary<string, string> rels = ReadPresentationRels(byName);

            // sldIdLst: <p:sldId id="…" r:id="rId2"/> en orden.
            List<string> ids = new List<string>();
            XmlReaderSettings st = NewSettings();
            using (XmlReader r = XmlReader.Create(new MemoryStream(pres), st))
            {
                while (r.Read())
                {
                    if (r.NodeType != XmlNodeType.Element) continue;
                    if (!string.Equals(r.LocalName, "sldId", StringComparison.Ordinal)) continue;
                    string rid = r.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                    if (string.IsNullOrEmpty(rid)) rid = r.GetAttribute("id");
                    if (!string.IsNullOrEmpty(rid)) ids.Add(rid);
                }
            }

            foreach (string rid in ids)
            {
                string target;
                if (rels.TryGetValue(rid, out target) && IsSlidePath(target))
                    ordered.Add(NormalizeSlidePath(target));
            }
            if (ordered.Count == 0)
            {
                // sldIdLst vacío/ilegible: fallback numérico.
                foreach (string k in byName.Keys)
                    if (IsSlidePath(k)) ordered.Add(k);
                ordered.Sort(SlideNameComparer);
            }
            return ordered;
        }

        private static Dictionary<string, string> ReadPresentationRels(Dictionary<string, byte[]> byName)
        {
            Dictionary<string, string> rels = new Dictionary<string, string>(StringComparer.Ordinal);
            byte[] data;
            if (!byName.TryGetValue("ppt/_rels/presentation.xml.rels", out data) || data == null)
                return rels;
            XmlReaderSettings st = NewSettings();
            using (XmlReader r = XmlReader.Create(new MemoryStream(data), st))
            {
                while (r.Read())
                {
                    if (r.NodeType != XmlNodeType.Element) continue;
                    if (!string.Equals(r.LocalName, "Relationship", StringComparison.Ordinal)) continue;
                    string id = r.GetAttribute("Id");
                    string target = r.GetAttribute("Target");
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(target)) continue;
                    // target relativos a ppt/ (slides/slide1.xml); externos ignorados.
                    if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
                    rels[id] = NormalizeSlidePath(target);
                }
            }
            return rels;
        }

        private static bool IsSlidePath(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.Replace('\\', '/');
            return n.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) &&
                   n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                   n.IndexOf('/', 11) < 0;   // exactamente ppt/slides/slideN.xml (sin layout)
        }

        /// <summary>Normaliza el target del rel a ruta de paquete completa.</summary>
        private static string NormalizeSlidePath(string target)
        {
            string n = target.Replace('\\', '/').TrimStart('/');
            if (n.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase)) return n;
            return "ppt/" + n;   // target relativo a ppt/
        }

        private static int SlideNameComparer(string a, string b)
        {
            return SlideNumber(a).CompareTo(SlideNumber(b));
        }

        private static int SlideNumber(string name)
        {
            // «ppt/slides/slide12.xml» → 12 (los no numéricos van al final).
            string s = name.Replace('\\', '/');
            int i = s.LastIndexOf("slide", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return int.MaxValue;
            int start = i + 5, end = start;
            while (end < s.Length && char.IsDigit(s[end])) end++;
            if (end == start) return int.MaxValue;
            int v;
            return int.TryParse(s.Substring(start, end - start), out v) ? v : int.MaxValue;
        }

        /* --------------------------------------------------- core title ---- */

        private static string ReadCoreTitle(Dictionary<string, byte[]> byName)
        {
            byte[] data;
            if (!byName.TryGetValue("docProps/core.xml", out data) || data == null)
                return string.Empty;
            try
            {
                XmlReaderSettings st = NewSettings();
                using (XmlReader r = XmlReader.Create(new MemoryStream(data), st))
                {
                    while (r.Read())
                    {
                        if (r.NodeType == XmlNodeType.Element &&
                            string.Equals(r.LocalName, "title", StringComparison.Ordinal))
                        {
                            string t = r.ReadElementContentAsString();
                            return t != null ? t.Trim() : string.Empty;
                        }
                    }
                }
            }
            catch (Exception) { }
            return string.Empty;
        }

        /* ---------------------------------------------------- slide XML ---- */

        private static PptxSlide ParseSlideXml(byte[] data, int slideNo, FidelityReport report)
        {
            PptxSlide slide = new PptxSlide();
            int graphicFrames = 0, animations = 0;
            XmlReaderSettings st = NewSettings();
            using (XmlReader r = XmlReader.Create(new MemoryStream(data), st))
            {
                // Recorre TODO el spTree por documento: cada <p:sp> aporta sus
                // párrafos; el orden de aparición = orden visual del panel.
                // SEMÁNTICA XmlReader (misma lección de ParseShape): tras Skip
                // el reader YA está en el nodo siguiente — se reprocesa SIN
                // llamar Read() de nuevo (si no, se pierde ese nodo).
                bool more = r.Read();
                while (more)
                {
                    bool advanced = false;
                    if (r.NodeType == XmlNodeType.Element)
                    {
                        string ln = r.LocalName;
                        if (string.Equals(ln, "sp", StringComparison.Ordinal))
                        {
                            if (!r.IsEmptyElement) ParseShape(r, slide);
                            // tras ParseShape el reader queda en </p:sp>: el
                            // Read() del final del bucle avanza normalmente.
                        }
                        else if (string.Equals(ln, "graphicFrame", StringComparison.Ordinal))
                        {
                            // F4.15: tablas/gráficos SmartArt NO tienen
                            // representación de texto — omitidos y REPORTADOS
                            // (antes: silencio).
                            graphicFrames++;
                            if (!r.IsEmptyElement) { r.Skip(); advanced = true; }
                        }
                        else if (string.Equals(ln, "timing", StringComparison.Ordinal))
                        {
                            // F4.15: animaciones de la diapositiva → efecto
                            // no soportado.
                            animations++;
                            if (!r.IsEmptyElement) { r.Skip(); advanced = true; }
                        }
                    }
                    more = advanced ? true : r.Read();
                }
            }
            if (graphicFrames > 0)
                report.AddOmission("diapositiva " + slideNo + ": " + graphicFrames +
                    " tabla(s)/gráfico(s) sin representación de texto");
            if (animations > 0)
                report.AddUnsupported("diapositiva " + slideNo + ": animaciones descartadas");
            // Fidelidad 1:1: la slide se reporta AUNQUE no tenga texto (la UI
            // la mapea a un ítem «en blanco» — mismo número de diapositivas).
            return slide;
        }

        /// <summary>
        /// Lee el <p:sp> actual hasta su cierre: recopila los <a:p> del txBody.
        /// SEMÁNTICA XmlReader (misma lección verificada de OsisBible): tras
        /// ReadElementContentAsString/Skip el reader queda POSICIONADO en el
        /// nodo siguiente — el bucle lo reprocesa SIN llamar Read().
        /// </summary>
        private static void ParseShape(XmlReader r, PptxSlide slide)
        {
            int depth = r.Depth;   // profundidad de <p:sp>
            StringBuilder line = new StringBuilder();

            for (bool more = r.Read(); more; )
            {
                bool advanced = false;   // ¿un helper movió el reader?

                if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth)
                    break;   // </p:sp>: forma completa

                if (r.NodeType == XmlNodeType.EndElement &&
                    string.Equals(r.LocalName, "p", StringComparison.Ordinal))
                {
                    // </a:p>: sellar la línea acumulada.
                    slide.Lines.Add(line.ToString());
                    line.Length = 0;
                }
                else if (r.NodeType == XmlNodeType.Element)
                {
                    string n = r.LocalName;
                    if (string.Equals(n, "p", StringComparison.Ordinal))
                    {
                        if (r.IsEmptyElement)
                            slide.Lines.Add(string.Empty);   // <a:p/> vacío
                        // <a:p> con contenido: limpiar y dejar que los hijos acumulen.
                        line.Length = 0;
                    }
                    else if (string.Equals(n, "t", StringComparison.Ordinal) ||
                             string.Equals(n, "fld", StringComparison.Ordinal))
                    {
                        if (!r.IsEmptyElement) ReadTextContent(r, line);
                    }
                    else if (string.Equals(n, "br", StringComparison.Ordinal))
                    {
                        line.Append('\n');
                    }
                    else if (IsPropertyElement(n) && !r.IsEmptyElement)
                    {
                        r.Skip();   // propiedades: sin texto útil
                        advanced = true;
                    }
                    // Otros contenedores (txBody, r, grpSp, grpSpPr…): entrar.
                }

                // advanced → el reader YA está en el nodo siguiente: reprocesar.
                more = advanced ? true : r.Read();
            }
        }

        /// <summary>¿Elemento de propiedades sin texto? (se salta el contenido).</summary>
        private static bool IsPropertyElement(string localName)
        {
            return string.Equals(localName, "pPr", StringComparison.Ordinal) ||
                   string.Equals(localName, "rPr", StringComparison.Ordinal) ||
                   string.Equals(localName, "endParaRPr", StringComparison.Ordinal) ||
                   string.Equals(localName, "bodyPr", StringComparison.Ordinal) ||
                   string.Equals(localName, "spPr", StringComparison.Ordinal) ||
                   string.Equals(localName, "nvSpPr", StringComparison.Ordinal) ||
                   string.Equals(localName, "lstStyle", StringComparison.Ordinal) ||
                   string.Equals(localName, "style", StringComparison.Ordinal);
        }

        /// <summary>
        /// Lee el contenido del elemento ACTUAL (a:t o a:fld) acumulando texto
        /// plano. Sale con el reader en el EndElement del elemento.
        /// </summary>
        private static void ReadTextContent(XmlReader r, StringBuilder line)
        {
            int d = r.Depth;
            for (bool m = r.Read(); m; )
            {
                bool adv = false;
                if (r.NodeType == XmlNodeType.EndElement && r.Depth == d)
                    break;   // cierre del a:t / a:fld
                if (r.NodeType == XmlNodeType.Text)
                {
                    line.Append(r.Value);
                }
                else if (r.NodeType == XmlNodeType.Element &&
                         string.Equals(r.LocalName, "t", StringComparison.Ordinal) &&
                         !r.IsEmptyElement)
                {
                    // a:t anidado (dentro de a:fld): texto real.
                    line.Append(r.ReadElementContentAsString());
                    adv = true;
                }
                else if (r.NodeType == XmlNodeType.Element &&
                         IsPropertyElement(r.LocalName) && !r.IsEmptyElement)
                {
                    r.Skip();
                    adv = true;
                }
                m = adv ? true : r.Read();
            }
        }

        private static XmlReaderSettings NewSettings()
        {
            XmlReaderSettings st = new XmlReaderSettings();
            st.IgnoreComments = true;
            st.IgnoreProcessingInstructions = true;
            st.IgnoreWhitespace = true;
#if LUMINA_NET35
            st.ProhibitDtd = false;
            st.XmlResolver = null;
#else
            st.DtdProcessing = DtdProcessing.Ignore;
#endif
            st.CloseInput = false;
            return st;
        }
    }
}
