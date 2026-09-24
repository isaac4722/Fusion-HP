// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  PdfExporter.cs : exportación de Escenarios a PDF 1.4 con escritor PROPIO
//  (cero dependencias externas — requisito del proyecto).
//
//    * Fuentes base-14 Helvetica / Helvetica-Bold con WinAnsiEncoding:
//      no se incrustan → PDF pequeños, válidos en cualquier visor Win7→Win11.
//    * Métricas AFM (PdfFont) para centrado real y ajuste automático de tamaño.
//    * Compresión FlateDecode: zlib (RFC1950) envolviendo DeflateStream con
//      cabecera 0x78 0x01 + Adler32 final.
//    * Imágenes: JPEG → DCTDecode directo (bytes tal cual); PNG/BMP → decodificado
//      por el decodificador instalable (UI: GDI+). RGB crudo disponible para tests.
//    * Página 16:9 (960×540 pt), una por slide; tema aplicado (fondo, tipografía,
//      MAYÚSCULAS, interlineado, acentos); sombra simulada con texto desplazado.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace lumina.core
{
    /// <summary>Imagen ya lista para el PDF (el llamador la decodifica como quiera).</summary>
    public sealed class PdfImage
    {
        public int Width, Height;
        public byte[] JpegBytes;   // si != null → DCTDecode directo
        public byte[] Rgb;         // si != null → RGB 8-bit crudo (FlateDecode)

        public PdfImage() {}

        public PdfImage(int width, int height, byte[] rgb)
        {
            Width = width; Height = height; Rgb = rgb;
        }
    }

    public static class PdfExporter
    {
        // Página 16:9 en puntos (1 pt = 1/72 in)
        private const double PageW = 960.0;
        private const double PageH = 540.0;
        private const double Margin = 48.0;

        /// <summary>
        /// Decodificador de imágenes por ruta (PNG/BMP/GIF/TIF → PdfImage RGB).
        /// Lo instala la UI con GDI+ (solo Windows); en tests se usa RGB crudo.
        /// </summary>
        public static Func<string, PdfImage> ImageDecoder;

        /// <summary>Exporta a archivo. null = OK; si falla, mensaje claro.</summary>
        public static string ExportToFile(string path, string scenarioName,
                                          IList<ExportSlide> slides, Theme theme)
        {
            try
            {
                byte[] pdf = ExportToBytes(scenarioName, slides, theme);
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    fs.Write(pdf, 0, pdf.Length);
                return null;
            }
            catch (Exception ex)
            {
                return "No se pudo escribir el PDF (" + ex.Message + ").";
            }
        }

        /// <summary>F4.15: variante CON informe de fidelidad (out). La firma
        /// histórica sigue válida y delega aquí (el informe se descarta).</summary>
        public static byte[] ExportToBytes(string scenarioName, IList<ExportSlide> slides, Theme theme)
        {
            FidelityReport rep;
            return ExportToBytes(scenarioName, slides, theme, out rep);
        }

        /// <summary>Exporta y reporta (F4.15): páginas convertidas, imágenes
        /// omitidas (ilegibles) y la regla MVP de una página por diapositiva.</summary>
        public static byte[] ExportToBytes(string scenarioName, IList<ExportSlide> slides,
                                           Theme theme, out FidelityReport report)
        {
            if (slides == null) slides = new List<ExportSlide>();
            if (theme == null) theme = new Theme();
            report = new FidelityReport();
            report.Source = "pdf";

            // IDs: 1=Catalog 2=Pages 3=F1 4=F2 5=Info, luego por página: page, contents, [imgs]
            PdfWriter w = new PdfWriter();

            int catalogId = 1, pagesId = 2, f1Id = 3, f2Id = 4, infoId = 5;
            w.AddObject(catalogId, "<< /Type /Catalog /Pages " + pagesId + " 0 R >>");
            w.AddObject(f1Id, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            w.AddObject(f2Id, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
            w.AddObject(infoId, "<< /Title (" + PdfText(scenarioName) + ") " +
                "/Producer (LuminaPresentation Suite) /Creator (LuminaPresentation Suite) >>");

            List<int> pageIds = new List<int>();
            int nextId = 6;
            int pageNo = 0;
            foreach (ExportSlide s in slides)
            {
                ExportSlide sl = s ?? new ExportSlide();
                pageNo++;
                int pageId = nextId++;
                int contentId = nextId++;
                List<int> imgIds = new List<int>();

                string content = BuildPageContent(sl, theme, w, ref nextId, imgIds, report, pageNo);
                w.AddTextStream(contentId, content);

                StringBuilder pg = new StringBuilder();
                pg.Append("<< /Type /Page /Parent " + pagesId + " 0 R " +
                    "/MediaBox [0 0 " + Fmt(PageW) + " " + Fmt(PageH) + "] " +
                    "/Resources << /Font << /F1 " + f1Id + " 0 R /F2 " + f2Id + " 0 R >>");
                if (imgIds.Count > 0)
                {
                    pg.Append(" /XObject << ");
                    for (int i = 0; i < imgIds.Count; i++)
                        pg.Append("/Im" + (i + 1) + " " + imgIds[i] + " 0 R ");
                    pg.Append(">>");
                }
                pg.Append(" >> /Contents " + contentId + " 0 R >>");
                w.AddObject(pageId, pg.ToString());
                pageIds.Add(pageId);
            }

            StringBuilder kids = new StringBuilder();
            foreach (int id in pageIds) kids.Append(id).Append(" 0 R ");
            w.AddObject(pagesId, "<< /Type /Pages /Kids [" + kids.ToString() + "] /Count " + pageIds.Count + " >>");

            // F4.15: resultado final — una diapositiva = una página (regla MVP),
            // herencia del tema aplicada en cada página (fondo/texto/acento/fuente).
            report.AddConverted(pageIds.Count, "páginas PDF escritas (una por diapositiva — regla MVP)");
            if (pageIds.Count > 0)
                report.AddInheritance(pageIds.Count * 4,
                    "fondo/texto/acento/tipografía heredados del tema en cada página");
            report.AddUnsupported("animaciones y transiciones (el PDF es papel estático)");
            return w.Finish(catalogId, infoId);
        }

        // ================================================================ página

        private static string BuildPageContent(ExportSlide s, Theme t, PdfWriter w,
                                               ref int nextId, List<int> imgIds,
                                               FidelityReport report, int pageNo)
        {
            StringBuilder c = new StringBuilder();
            double bgR, bgG, bgB;
            ParseColor(t.BgColor, 0x0B / 255.0, 0x1F / 255.0, 0x2A / 255.0, out bgR, out bgG, out bgB);
            double fgR, fgG, fgB;
            ParseColor(t.FgColor, 1, 1, 1, out fgR, out fgG, out fgB);
            double acR, acG, acB;
            ParseColor(t.AccentColor, 0x3A / 255.0, 0xA6 / 255.0, 0xB9 / 255.0, out acR, out acG, out acB);

            // Fondo
            c.Append("q " + Rgb(bgR, bgG, bgB) + " 0 0 " + Fmt(PageW) + " " + Fmt(PageH) + " re f Q\n");

            // Imagen de fondo del tema (a pantalla completa)
            PdfImage bgImg = LoadImage(t.ImagePath);
            if (bgImg != null)
            {
                int id = AddImageObject(w, ref nextId, bgImg);
                if (id > 0)
                {
                    imgIds.Add(id);
                    int imgNo = imgIds.Count;
                    c.Append("q " + Fmt(PageW) + " 0 0 " + Fmt(PageH) + " 0 0 cm /Im" + imgNo + " Do Q\n");
                }
            }
            else if (!string.IsNullOrEmpty(t.ImagePath))
            {
                // F4.15: fondo del tema ilegible → omitido y REPORTADO.
                report.AddOmission("página " + pageNo + ": imagen de fondo del tema no decodificable (" + t.ImagePath + ")");
            }

            // Imagen de la slide (área útil centrada)
            PdfImage img = LoadImage(s.ImagePath);
            if (img != null)
            {
                int id = AddImageObject(w, ref nextId, img);
                if (id > 0)
                {
                    imgIds.Add(id);
                    int imgNo = imgIds.Count;
                    double scale = Math.Min((PageW - 2 * Margin) / img.Width, (PageH - 2 * Margin) / img.Height);
                    double iw = img.Width * scale, ih = img.Height * scale;
                    double ox = (PageW - iw) / 2.0, oy = (PageH - ih) / 2.0;
                    c.Append("q " + Fmt(iw) + " 0 0 " + Fmt(ih) + " " + Fmt(ox) + " " + Fmt(oy) + " cm /Im" + imgNo + " Do Q\n");
                }
            }
            else if (!string.IsNullOrEmpty(s.ImagePath))
            {
                // F4.15: imagen de la diapositiva ilegible → omitida y REPORTADA.
                report.AddOmission("página " + pageNo + ": imagen de la diapositiva no decodificable (" + s.ImagePath + ")");
            }

            int kind = s.Kind;

            // ---- slide TÍTULO: título grande + subtítulo
            if (kind == SlideKind.Title && s.Title.Length > 0)
            {
                List<string> title = Wrap(s.Title, t.Bold, t.FontSize > 40 ? t.FontSize : 80, PageW - 2 * Margin);
                DrawLinesCentered(c, title, t.Bold ? f2 : f1, t.FontSize > 40 ? t.FontSize : 80,
                    PageH * 0.78, fgR, fgG, fgB, shadow: true);
                if (s.Subtitle.Length > 0)
                {
                    List<string> sub = Wrap(s.Subtitle, false, t.FontSize, PageW - 2 * Margin);
                    DrawLinesCentered(c, sub, f1, t.FontSize, PageH * 0.34, acR, acG, acB, shadow: true);
                }
                return c.ToString();
            }

            // ---- cuerpo
            List<string> lines = new List<string>();
            foreach (string l in s.Lines)
            {
                string txt = l == null ? string.Empty : l.Trim();
                if (txt.Length > 0) lines.Add(t.Uppercase ? txt.ToUpperInvariant() : txt);
            }
            if (lines.Count > 0)
            {
                bool bold = t.Bold;
                double spacing = t.LineSpacing <= 0 ? 1.18 : t.LineSpacing;
                int maxLines = (int)Math.Floor((PageH * 0.62) / (t.FontSize * spacing * 0.5));
                int size = t.FontSize;
                List<string> wrapped = new List<string>();
                while (size >= 10)
                {
                    wrapped.Clear();
                    foreach (string l in lines) wrapped.AddRange(Wrap(l, bold, size, PageW - 2 * Margin));
                    if (wrapped.Count <= Math.Max(4, maxLines)) break;
                    size = (int)(size * 0.88);
                }
                double lh = size * 0.5 * spacing;                 // px→pt: 1 px = 0.5 pt
                double blockH = wrapped.Count * lh;
                double top = PageH / 2.0 + blockH / 2.0 - lh * 0.36; // centro vertical
                double y = top;
                bool left = kind == SlideKind.Scripture;
                double x = left ? Margin : PageW / 2.0;
                foreach (string ln in wrapped)
                {
                    DrawTextLine(c, ln, bold ? f2 : f1, size, x, y, fgR, fgG, fgB, true, !left);
                    y -= lh;
                }
            }

            // ---- referencia bíblica al pie (acento)
            if (kind == SlideKind.Scripture && s.RefLabel.Length > 0)
            {
                DrawTextLine(c, s.RefLabel, f1, 20, PageW - Margin, Margin + 8, acR, acG, acB,
                    false, true);
            }
            else if (kind != SlideKind.Title && s.RefLabel.Length > 0 && lines.Count > 0)
            {
                // rótulo de bloque arriba (canciones)
                DrawTextLine(c, s.RefLabel, f1, 20, PageW / 2.0, PageH - Margin, acR, acG, acB,
                    true, false);
            }
            return c.ToString();
        }

        private const string f1 = "F1";
        private const string f2 = "F2";

        // ================================================================ texto

        /// <summary>Envuelve por palabras con métricas AFM reales.</summary>
        private static List<string> Wrap(string text, bool bold, int sizePt, double maxWidth)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(text)) { result.Add(string.Empty); return result; }
            int[] widths = bold ? PdfFont.HelveticaBold : PdfFont.Helvetica;
            string[] words = text.Split(' ');
            string line = string.Empty;
            foreach (string word in words)
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                byte[] cb = PdfFont.ToWinAnsi(candidate);
                int wpx = PdfFont.StringWidth(widths, cb, sizePt / 2);
                if (wpx > 0 && wpx <= maxWidth)
                {
                    line = candidate;
                }
                else
                {
                    if (line.Length > 0) result.Add(line);
                    // palabra más larga que la línea: cortar por caracteres
                    string rest = word;
                    while (rest.Length > 0)
                    {
                        byte[] rb = PdfFont.ToWinAnsi(rest);
                        if (PdfFont.StringWidth(widths, rb, sizePt / 2) <= maxWidth || rest.Length <= 1) break;
                        rest = rest.Substring(0, rest.Length - 1);
                    }
                    line = rest;
                }
            }
            if (line.Length > 0) result.Add(line);
            if (result.Count == 0) result.Add(string.Empty);
            return result;
        }

        private static void DrawLinesCentered(StringBuilder c, List<string> lines, string font, int sizePx,
                                              double topY, double r, double g, double b, bool shadow)
        {
            double lh = sizePx * 0.5 * 1.2;
            double y = topY;
            foreach (string ln in lines)
            {
                DrawTextLine(c, ln, font, sizePx, PageW / 2.0, y, r, g, b, shadow, true);
                y -= lh;
            }
        }

        private static void DrawTextLine(StringBuilder c, string line, string font, int sizePx,
                                         double x, double y, double r, double g, double b,
                                         bool shadow, bool center, bool rightAlign = false)
        {
            int sizePt = Math.Max(6, sizePx / 2);
            byte[] bytes = PdfFont.ToWinAnsi(line);
            string txt = Escaped(bytes);
            if (txt.Length == 0) return;

            double width = PdfFont.StringWidth(font == f2 ? PdfFont.HelveticaBold : PdfFont.Helvetica,
                                               bytes, sizePt);
            double tx = x;
            if (center) tx = x - width / 2.0;
            else if (rightAlign) tx = x - width;

            // Sombra (si el tema la trae): texto desplazado en gris oscuro
            if (shadow)
            {
                c.Append("BT " + Rgb(0.05, 0.05, 0.08) + " /" + font + " " + sizePt + " Tf " +
                    Fmt(tx + sizePt * 0.05) + " " + Fmt(y - sizePt * 0.05) + " Td (" + txt + ") Tj ET\n");
            }
            c.Append("BT " + Rgb(r, g, b) + " /" + font + " " + sizePt + " Tf " +
                Fmt(tx) + " " + Fmt(y) + " Td (" + txt + ") Tj ET\n");
        }

        // ================================================================ imágenes

        private static PdfImage LoadImage(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                byte[] data = File.ReadAllBytes(path);
                if (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8)
                {
                    PdfImage img = new PdfImage();
                    img.JpegBytes = data;
                    if (!JpegSize(data, out img.Width, out img.Height)) return null;
                    return img;
                }
                // PNG/BMP/... → decodificador instalable (GDI+ en la UI)
                Func<string, PdfImage> dec = ImageDecoder;
                if (dec != null) return dec(path);
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int AddImageObject(PdfWriter w, ref int nextId, PdfImage img)
        {
            if (img == null || img.Width <= 0 || img.Height <= 0) return 0;
            int id = nextId++;
            if (img.JpegBytes != null && img.JpegBytes.Length > 0)
            {
                w.AddBinaryStream(id, "<< /Type /XObject /Subtype /Image /Width " + img.Width +
                    " /Height " + img.Height + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode >>",
                    img.JpegBytes);
            }
            else if (img.Rgb != null && img.Rgb.Length >= img.Width * img.Height * 3)
            {
                byte[] flate = ZlibCompress(img.Rgb);
                w.AddBinaryStream(id, "<< /Type /XObject /Subtype /Image /Width " + img.Width +
                    " /Height " + img.Height + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode >>",
                    flate);
            }
            else return 0;
            return id;
        }

        /// <summary>Tamaño de un JPEG desde sus marcadores SOF.</summary>
        private static bool JpegSize(byte[] d, out int width, out int height)
        {
            width = height = 0;
            int i = 2;
            while (i + 9 < d.Length)
            {
                if (d[i] != 0xFF) { i++; continue; }
                byte marker = d[i + 1];
                if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2 || marker == 0xC3)
                {
                    height = (d[i + 5] << 8) | d[i + 6];
                    width = (d[i + 7] << 8) | d[i + 8];
                    return width > 0 && height > 0;
                }
                int len = (d[i + 2] << 8) | d[i + 3];
                i += 2 + len;
            }
            return false;
        }

        // ================================================================ zlib

        /// <summary>zlib (RFC1950): 0x78 0x01 + deflate + adler32 (PDF FlateDecode).</summary>
        private static byte[] ZlibCompress(byte[] data)
        {
            using (MemoryStream raw = new MemoryStream())
            {
                using (DeflateStream ds = new DeflateStream(raw, CompressionMode.Compress, true))
                {
                    ds.Write(data, 0, data.Length);
                }
                byte[] deflated = raw.ToArray();
                byte[] outB = new byte[deflated.Length + 6];
                outB[0] = 0x78; outB[1] = 0x01;
                Buffer.BlockCopy(deflated, 0, outB, 2, deflated.Length);
                uint adler = Adler32(data);
                outB[outB.Length - 4] = (byte)(adler >> 24);
                outB[outB.Length - 3] = (byte)(adler >> 16);
                outB[outB.Length - 2] = (byte)(adler >> 8);
                outB[outB.Length - 1] = (byte)adler;
                return outB;
            }
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte x in data)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        // ================================================================ utils

        private static void ParseColor(string hex, double defR, double defG, double defB,
                                       out double r, out double g, out double b)
        {
            r = defR; g = defG; b = defB;
            if (string.IsNullOrEmpty(hex)) return;
            string h = hex.TrimStart('#');
            if (h.Length == 8) h = h.Substring(2);
            if (h.Length != 6) return;
            try
            {
                r = Convert.ToInt32(h.Substring(0, 2), 16) / 255.0;
                g = Convert.ToInt32(h.Substring(2, 2), 16) / 255.0;
                b = Convert.ToInt32(h.Substring(4, 2), 16) / 255.0;
            }
            catch (Exception) { r = defR; g = defG; b = defB; }
        }

        private static string Rgb(double r, double g, double b)
        {
            return Fmt(r) + " " + Fmt(g) + " " + Fmt(b) + " rg";
        }

        private static string Fmt(double v)
        {
            return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string PdfText(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            byte[] bytes = PdfFont.ToWinAnsi(s);
            return Escaped(bytes);
        }

        private static string Escaped(byte[] winAnsi)
        {
            StringBuilder sb = new StringBuilder(winAnsi.Length + 8);
            foreach (byte x in winAnsi)
            {
                if (x == (byte)'(' || x == (byte)')' || x == (byte)'\\') sb.Append('\\').Append((char)x);
                else if (x < 32) sb.Append(' ');
                else sb.Append((char)x);
            }
            return sb.ToString();
        }

        // ================================================================ writer

        private sealed class PdfObject
        {
            public int Id;
            public string Body;        // dict (o dict+stream de texto)
            public byte[] StreamBytes; // binario del stream (imágenes)
        }

        private sealed class PdfWriter
        {
            // WinAnsi → byte por carácter: SIEMPRE Latin-1 al escribir (1 byte
            // por char ≤ 0xFF; UTF-8 corrompería acentos y descuadraría /Length).
#if NET8_0
            private static readonly Encoding PdfEncoding = Encoding.Latin1;
#else
            private static readonly Encoding PdfEncoding = Encoding.GetEncoding("ISO-8859-1");
#endif

            private readonly Dictionary<int, PdfObject> _byId = new Dictionary<int, PdfObject>();

            public void AddObject(int id, string dict)
            {
                PdfObject o = new PdfObject();
                o.Id = id;
                o.Body = dict;
                _byId[id] = o;
            }

            public void AddTextStream(int id, string streamText)
            {
                PdfObject o = new PdfObject();
                o.Id = id;
                o.Body = "<< /Length " + PdfEncoding.GetByteCount(streamText) + " >>\nstream\n" +
                         streamText + "\nendstream";
                _byId[id] = o;
            }

            public void AddBinaryStream(int id, string dict, byte[] data)
            {
                PdfObject o = new PdfObject();
                o.Id = id;
                o.Body = dict + " /Length " + data.Length + " >>\nstream\n";
                o.StreamBytes = data;
                _byId[id] = o;
            }

            public byte[] Finish(int catalogId, int infoId)
            {
                MemoryStream ms = new MemoryStream();
                // Cabecera + comentario binario (bytes literales, nunca UTF-8)
                ms.WriteByte((byte)'%'); ms.WriteByte((byte)'P'); ms.WriteByte((byte)'D');
                ms.WriteByte((byte)'F'); ms.WriteByte((byte)'-'); ms.WriteByte((byte)'1');
                ms.WriteByte((byte)'.'); ms.WriteByte((byte)'4'); ms.WriteByte((byte)'\n');
                ms.WriteByte((byte)'%'); ms.WriteByte(0xE2); ms.WriteByte(0xE3);
                ms.WriteByte(0xCF); ms.WriteByte(0xD3); ms.WriteByte((byte)'\n');

                Dictionary<int, int> offsets = new Dictionary<int, int>();
                List<int> ids = new List<int>(_byId.Keys);
                ids.Sort();
                foreach (int id in ids)
                {
                    offsets[id] = (int)ms.Length;
                    PdfObject o = _byId[id];
                    WriteText(ms, id + " 0 obj\n" + o.Body);
                    if (o.StreamBytes != null)
                    {
                        ms.Write(o.StreamBytes, 0, o.StreamBytes.Length);
                        WriteText(ms, "\nendstream");
                    }
                    WriteText(ms, "\nendobj\n");
                }

                int xrefPos = (int)ms.Length;
                int maxId = ids.Count > 0 ? ids[ids.Count - 1] : 0;
                WriteText(ms, "xref\n0 " + (maxId + 1) + "\n");
                WriteText(ms, "0000000000 65535 f \n");
                for (int id = 1; id <= maxId; id++)
                {
                    int off;
                    string s = offsets.TryGetValue(id, out off)
                        ? off.ToString("0000000000") : "0000000000";
                    WriteText(ms, s + " 00000 n \n");
                }
                WriteText(ms, "trailer\n<< /Size " + (maxId + 1) + " /Root " + catalogId +
                    " 0 R /Info " + infoId + " 0 R >>\nstartxref\n" + xrefPos + "\n%%EOF\n");
                return ms.ToArray();
            }

            private static void WriteText(MemoryStream ms, string s)
            {
                byte[] b = PdfEncoding.GetBytes(s);
                ms.Write(b, 0, b.Length);
            }
        }
    }
}
