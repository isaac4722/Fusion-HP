// ============================================================================
//  Fusion-HP · Export/PdfExporter.cs — exportación PDF [SPEC §9.3.2] (v4.1.0)
//  Motor: PdfSharp 1.50.5147 (MIT, net20 — [DEPENDENCIAS.md]).
//  · Texto Unicode REAL (acentos, ñ, em-dash) — se acabó el filtro Latin-1.
//  · Fuentes del producto (Outfit, Cormorant Garamond, Libre Baskerville)
//    registradas en XPrivateFontCollection y EMBEBIDAS por PdfSharp (subset
//    TrueType /FontFile2) — el PDF se ve igual en cualquier equipo.
//  · Fondos e imágenes via XImage (JPEG/PNG nativos, sin re-codificar JPEG).
//  Misma firma Export(AhpProject, path, baseDir) y misma matemática de caja
//  que el escritor histórico: callers y pruebas existentes intactos.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Fusion.Shared;
using Fusion.Shared.Model;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Fusion.Studio.Export
{
    public class PdfExporter
    {
        const double W = 960, H = 540;   // puntos (16:9)
        static bool _fontsRegistered;

        public int Export(AhpProject project, string outPath, string baseDir)
        {
            RegisterProductFonts();

            var doc = new PdfDocument();
            doc.Info.Title = project.Name ?? "Fusion HP";
            doc.Info.Creator = "Fusion HP (PdfSharp)";

            foreach (var scn in project.Scenarios)
            {
                foreach (var el in scn.Elements)
                {
                    var resolved = ResolvedSlide.Resolve(el, scn, project, baseDir);
                    var page = doc.AddPage();
                    page.Width = W;
                    page.Height = H;
                    using (var g = XGraphics.FromPdfPage(page))
                    {
                        // fondo del tema
                        g.DrawRectangle(Brush(Hex(resolved.Background.Color)), 0, 0, W, H);

                        // imagen de fondo
                        if (!string.IsNullOrEmpty(resolved.Background.Image) && File.Exists(resolved.Background.Image))
                        {
                            using (var img = SafeImage(resolved.Background.Image))
                                if (img != null) g.DrawImage(img, 0, 0, W, H);
                        }

                        if (el.Kind == ElementKind.Image || el.Kind == ElementKind.Video)
                        {
                            string abs = resolved.Src;
                            if (!string.IsNullOrEmpty(abs) && File.Exists(abs))
                            {
                                using (var img = SafeImage(abs))
                                    if (img != null)
                                    {
                                        double mW = el.W * W, mH = el.H * H;
                                        double x = el.X * W, y = H - el.Y * H - mH;
                                        g.DrawImage(img, x, y, mW, mH);
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
                            // auto-ajuste (misma regla del escritor histórico)
                            double totalH = lineH * resolved.Lines.Count;
                            if (totalH > boxH && totalH > 0) { sizePt *= boxH / totalH; lineH = sizePt * (st.LineSpacing ?? 1.15); }
                            double yTop = H - boxYtop - (boxH - lineH * resolved.Lines.Count) / 2;

                            XFont font = Font(st.Font, sizePt, st.Bold ?? false, st.Italic ?? false);
                            var sf = new XStringFormat { Alignment = XStringAlignment.Near, LineAlignment = XLineAlignment.BaseLine };

                            for (int i = 0; i < resolved.Lines.Count; i++)
                            {
                                double y = yTop - lineH * i;
                                string color = (i == 0) ? (st.ActiveColor ?? "#FFD700") : (st.Color ?? "#FFFFFF");
                                g.DrawString(resolved.Lines[i] ?? "", font, Brush(Hex(color)),
                                             new XPoint(boxX + 12, y), sf);
                            }
                            if (el.Kind == ElementKind.Verse && !string.IsNullOrEmpty(resolved.Reference))
                            {
                                double y = yTop - lineH * resolved.Lines.Count - sizePt * 0.5;
                                XFont refFont = Font(st.Font, Math.Max(10, sizePt * 0.4), st.Bold ?? false, st.Italic ?? false);
                                g.DrawString(resolved.Reference, refFont,
                                             Brush(Hex(st.ActiveColor ?? "#FFD700")),
                                             new XPoint(boxX + 12, y), sf);
                            }
                        }
                    }
                }
            }

            doc.Save(outPath);
            return doc.Pages.Count;
        }

        // ------------------------------------------------------------- helpers
        static XImage SafeImage(string path)
        {
            try { return XImage.FromFile(path); }
            catch { return null; }
        }

        static int Hex(string s)
        {
            if (string.IsNullOrEmpty(s)) return unchecked((int)0xFF000000);
            string t = s.TrimStart('#');
            if (t.Length == 6) t = "FF" + t;
            int v;
            return int.TryParse(t, System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out v)
                   ? v : unchecked((int)0xFF000000);
        }

        static XSolidBrush Brush(int argb)
        {
            return new XSolidBrush(XColor.FromArgb(argb));
        }

        static void RegisterProductFonts()
        {
            if (_fontsRegistered) return;
            _fontsRegistered = true;
            try
            {
                string dir = FontsDir();
                if (dir == null) return;
                // v4.1.0: XPrivateFontCollection de la build WPF — la typeface
                // conserva la ruta del archivo y PdfSharp la EMBEBE de verdad
                // (la build GDI sustituye fuentes privadas por la del sistema).
                foreach (string f in Directory.GetFiles(dir, "*.ttf"))
                    XPrivateFontCollection.AddFont(f);
            }
            catch { /* sin fuentes privadas se cae a las del sistema */ }
        }

        static string FontsDir()
        {
            for (string d = AppDomain.CurrentDomain.BaseDirectory; d != null && d.Length > 3;
                 d = Path.GetDirectoryName(d))
            {
                string cand = Path.Combine(Path.Combine(d, "resources"), "fonts");
                if (Directory.Exists(cand)) return cand;
            }
            return null;
        }

        static XFont Font(string family, double size, bool bold, bool italic)
        {
            XFontStyle style = (bold && italic) ? XFontStyle.BoldItalic
                             : bold ? XFontStyle.Bold
                             : italic ? XFontStyle.Italic
                             : XFontStyle.Regular;
            string name = string.IsNullOrEmpty(family) ? "Outfit" : family;
            try { return new XFont(name, size, style); }
            catch
            {
                try { return new XFont("Arial", size, style); }
                catch { return new XFont("Times New Roman", size, style); }
            }
        }
    }
}
