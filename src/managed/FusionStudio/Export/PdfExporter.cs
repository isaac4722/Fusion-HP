// ============================================================================
//  Fusion-HP · Export/PdfExporter.cs — exportación PDF [SPEC §9.3.2]:
// PDF con texto seleccionable (fuentes Helvetica base), fondo del tema e
// imágenes JPEG/PNG embebidas. Escritor puro sin dependencias.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using Fusion.Shared;
using Fusion.Shared.Model;

namespace Fusion.Studio.Export
{
    public class PdfExporter
    {
        const double W = 960, H = 540;   // puntos (16:9)

        public int Export(AhpProject project, string outPath, string baseDir)
        {
            var pages = new List<string>();   // contenido de cada página
            var images = new Dictionary<string, int>();   // ruta → id de objeto XObject
            var imageObjs = new List<byte[]>();
            int nextImageId = 10;

            foreach (var scn in project.Scenarios)
            {
                foreach (var el in scn.Elements)
                {
                    var resolved = ResolvedSlide.Resolve(el, scn, project, baseDir);
                    var sb = new StringBuilder();
                    long bg = Hex(resolved.Background.Color);

                    // fondo
                    sb.Append(Op("rg", Rgb(resolved.Background.Color)));
                    sb.Append(Op("re", "0", "0", Fmt(W), Fmt(H)));
                    sb.Append("f\n");

                    // imagen de fondo
                    if (!string.IsNullOrEmpty(resolved.Background.Image) && File.Exists(resolved.Background.Image))
                    {
                        int id = GetImageId(resolved.Background.Image, images, imageObjs, ref nextImageId);
                        if (id > 0)
                        {
                            sb.Append("q " + Fmt(W) + " 0 0 " + Fmt(H) + " 0 0 cm /Im" + id + " Do Q\n");
                        }
                    }

                    if (el.Kind == ElementKind.Image || el.Kind == ElementKind.Video)
                    {
                        string abs = resolved.Src;
                        if (!string.IsNullOrEmpty(abs) && File.Exists(abs))
                        {
                            int id = GetImageId(abs, images, imageObjs, ref nextImageId);
                            if (id > 0)
                            {
                                double mW = el.W * W, mH = el.H * H;
                                double x = el.X * W, y = H - el.Y * H - mH;
                                sb.Append("q " + Fmt(mW) + " 0 0 " + Fmt(mH) + " " + Fmt(x) + " " + Fmt(y) + " cm /Im" + id + " Do Q\n");
                            }
                        }
                    }
                    else
                    {
                        var st = resolved.Style;
                        double sizePt = st.Size ?? 48;
                        double lineH = sizePt * (st.LineSpacing ?? 1.15);
                        double boxX = (st.BoxX ?? 0.05) * W;
                        double boxW = (st.BoxW ?? 0.9) * W;
                        double boxYtop = (st.BoxY ?? 0.08) * H;
                        double boxH = (st.BoxH ?? 0.84) * H;
                        // auto-ajuste
                        double totalH = lineH * resolved.Lines.Count;
                        if (totalH > boxH && totalH > 0) { sizePt *= boxH / totalH; lineH = sizePt * (st.LineSpacing ?? 1.15); }
                        double yTop = H - boxYtop - (boxH - lineH * resolved.Lines.Count) / 2;

                        for (int i = 0; i < resolved.Lines.Count; i++)
                        {
                            double y = yTop - lineH * i - sizePt;
                            string color = (i == 0) ? (st.ActiveColor ?? "#FFD700") : (st.Color ?? "#FFFFFF");
                            sb.Append(Op("BT", "/F1 " + Fmt(sizePt) + " Tf", OpRgb(color),
                                         "1 0 0 1 " + Fmt(boxX + 12) + " " + Fmt(y) + " Tm",
                                         "(" + Escape(resolved.Lines[i]) + ") Tj", "ET"));
                        }
                        if (el.Kind == ElementKind.Verse && !string.IsNullOrEmpty(resolved.Reference))
                        {
                            double y = yTop - lineH * resolved.Lines.Count - sizePt * 0.5;
                            sb.Append(Op("BT", "/F2 " + Fmt(Math.Max(10, sizePt * 0.4)) + " Tf",
                                         OpRgb(st.ActiveColor ?? "#FFD700"),
                                         "1 0 0 1 " + Fmt(boxX + 12) + " " + Fmt(y) + " Tm",
                                         "(" + Escape(resolved.Reference) + ") Tj", "ET"));
                        }
                    }
                    pages.Add(sb.ToString());
                }
            }

            WritePdf(outPath, pages, imageObjs);
            return pages.Count;
        }

        static int GetImageId(string path, Dictionary<string, int> ids, List<byte[]> objs, ref int nextId)
        {
            int id;
            if (ids.TryGetValue(path, out id)) return id;
            try
            {
                using (var img = System.Drawing.Image.FromFile(path))
                {
                    byte[] data;
                    string filter;
                    if (string.Equals(Path.GetExtension(path), ".jpg", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetExtension(path), ".jpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        data = File.ReadAllBytes(path);
                        filter = "/DCTDecode";
                    }
                    else
                    {
                        using (var ms = new MemoryStream())
                        {
                            img.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                            data = ms.ToArray();
                        }
                        filter = "/DCTDecode";
                    }
                    id = nextId++;
                    ids[path] = id;
                    objs.Add(Encoding.ASCII.GetBytes(
                        id + " 0 obj\n<< /Type /XObject /Subtype /Image /Width " + img.Width +
                        " /Height " + img.Height + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter " + filter +
                        " /Length " + data.Length + " >>\nstream\n"));
                    objs.Add(data);
                    objs.Add(Encoding.ASCII.GetBytes("\nendstream\nendobj\n"));
                    return id;
                }
            }
            catch
            {
                return -1;
            }
        }

        static void WritePdf(string path, List<string> pages, List<byte[]> imageObjs)
        {
            // Cabecera binaria real (ASCII truncaría los bytes altos)
            var header = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A,
                                      0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A };
            var offsets = new List<long>();
            int imgCount = imageObjs.Count / 3;
            // Esquema de IDs: 1 catálogo · 2 páginas · 3,4 fuentes · 5..5+img-1 imágenes
            // páginas: 5+imgCount .. 5+imgCount+pages-1 · contenidos: la mitad siguiente
            int pageBase = 5 + imgCount;
            int contentBase = pageBase + pages.Count;

            using (var fs = File.Create(path))
            {
                fs.Write(header, 0, header.Length);

                int objId = 1;
                // 1: catálogo
                offsets.Add(fs.Position);
                WriteObj(fs, ref objId, "<< /Type /Catalog /Pages 2 0 R >>");
                // 2: árbol de páginas
                var kids = new StringBuilder();
                for (int i = 0; i < pages.Count; i++) kids.Append((pageBase + i) + " 0 R ");
                offsets.Add(fs.Position);
                WriteObj(fs, ref objId, "<< /Type /Pages /Kids [" + kids + "] /Count " + pages.Count + " >>");
                // 3,4: fuentes
                offsets.Add(fs.Position);
                WriteObj(fs, ref objId, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
                offsets.Add(fs.Position);
                WriteObj(fs, ref objId, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Oblique >>");
                // 5..: imágenes
                for (int i = 0; i < imgCount; i++)
                {
                    offsets.Add(fs.Position);
                    for (int k = 0; k < 3; k++)
                    {
                        var b = imageObjs[i * 3 + k];
                        fs.Write(b, 0, b.Length);
                    }
                }
                // páginas + contenidos
                for (int i = 0; i < pages.Count; i++)
                {
                    string content = pages[i];
                    offsets.Add(fs.Position);
                    WriteObjRaw(fs, ref objId,
                        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + Fmt(W) + " " + Fmt(H) + "] " +
                        "/Resources << /Font << /F1 3 0 R /F2 4 0 R >> " +
                        "/XObject << " + XObjectsFor(content) + " >> >> /Contents " + (contentBase + i) + " 0 R >>");
                }
                for (int i = 0; i < pages.Count; i++)
                {
                    string content = pages[i];
                    offsets.Add(fs.Position);
                    var bytes = Encoding.UTF8.GetBytes(content);
                    WriteObjRaw(fs, ref objId, "<< /Length " + bytes.Length + " >>\nstream\n");
                    fs.Write(bytes, 0, bytes.Length);
                    WriteObjRaw(fs, ref objId, "\nendstream");
                }
                // XRef + trailer
                long xrefPos = fs.Position;
                var sb = new StringBuilder();
                sb.Append("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
                foreach (var o in offsets) sb.Append(o.ToString("0000000000") + " 00000 n \n");
                sb.Append("trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xrefPos + "\n%%EOF");
                var xref = Encoding.ASCII.GetBytes(sb.ToString());
                fs.Write(xref, 0, xref.Length);
            }
        }

        static string XObjectsFor(string content)
        {
            var sb = new StringBuilder();
            var found = new HashSet<string>();
            int idx = 0;
            while ((idx = content.IndexOf("/Im", idx, StringComparison.Ordinal)) >= 0)
            {
                int end = idx + 3;
                while (end < content.Length && char.IsDigit(content[end])) end++;
                string name = content.Substring(idx, end - idx);
                if (found.Add(name)) sb.Append(name + " " + name.Substring(3) + " 0 R ");
                idx = end;
            }
            return sb.ToString();
        }

        static void WriteObj(FileStream fs, ref int id, string body)
        {
            var b = Encoding.ASCII.GetBytes(id + " 0 obj\n" + body + "\nendobj\n");
            fs.Write(b, 0, b.Length);
            id++;
        }

        static void WriteObjRaw(FileStream fs, ref int id, string body)
        {
            var b = Encoding.ASCII.GetBytes(id + " 0 obj\n" + body + "\nendobj\n");
            fs.Write(b, 0, b.Length);
            id++;
        }

        static string Op(params string[] parts)
        {
            return string.Join(" ", parts) + "\n";
        }

        static string Fmt(double d)
        {
            return d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        static string Rgb(string hex)
        {
            string h = (hex ?? "").TrimStart('#');
            if (h.Length == 8) h = h.Substring(2);
            if (h.Length != 6) h = "101820";
            double r = Convert.ToInt32(h.Substring(0, 2), 16) / 255.0;
            double g = Convert.ToInt32(h.Substring(2, 2), 16) / 255.0;
            double b = Convert.ToInt32(h.Substring(4, 2), 16) / 255.0;
            return Fmt(r) + " " + Fmt(g) + " " + Fmt(b);
        }

        static string OpRgb(string hex) { return Rgb(hex) + " rg"; }

        static long Hex(string hex)
        {
            string h = (hex ?? "").TrimStart('#');
            if (h.Length == 8) h = h.Substring(2);
            try { return Convert.ToInt64(h.Length == 6 ? h : "101820", 16); }
            catch { return 0; }
        }

        static string Escape(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "")
            {
                if (c > 255) sb.Append('?');
                else if (c == '(' || c == ')' || c == '\\') sb.Append('\\').Append(c);
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
