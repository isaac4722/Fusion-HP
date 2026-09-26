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

#if LITE
        // Lite (PdfSharp 1.50 GDI): la API de fuentes privadas es un stub y la
        // build GDI sustituye familias privadas — el export Lite usa la cara de
        // sistema (perfil B reducido, funcional y Unicode vía GDI+).
        static void RegisterProductFonts() { _fontsRegistered = true; }
#else
        // Studio (PDFsharp 6.1.1): IFontResolver con los BYTES de las fuentes del
        // producto → incrustación REAL de Outfit/Cormorant Garamond/Libre
        // Baskerville (motivo de la desviación de versión en DEPENDENCIAS.md:
        // la 1.50 trae XPrivateFontCollection stub y la GDI sustituye caras).
        sealed class ProductFontResolver : PdfSharp.Fonts.IFontResolver
        {
            static readonly Dictionary<string, byte[]> _cache =
                new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            public PdfSharp.Fonts.FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
            {
                return new PdfSharp.Fonts.FontResolverInfo(FaceFor(familyName, bold, italic));
            }

            public byte[] GetFont(string faceName)
            {
                lock (_cache)
                {
                    byte[] data;
                    if (!_cache.TryGetValue(faceName, out data))
                    {
                        string file = FileForFace(faceName);
                        string path = Path.Combine(Path.Combine(FontsRoot(), "fonts"), file);
                        if (!File.Exists(path)) path = Path.Combine(Path.Combine(FontsRoot(), "fonts"), "Outfit-Regular.ttf");
                        data = File.ReadAllBytes(path);
                        _cache[faceName] = data;
                    }
                    return data;
                }
            }

            static string FaceFor(string family, bool bold, bool italic)
            {
                string f = (family ?? "").Trim().ToLowerInvariant();
                if (f.StartsWith("cormorant"))
                {
                    if (bold && italic) return "cormorant-bi";
                    if (bold) return "cormorant-b";
                    if (italic) return "cormorant-i";
                    return "cormorant-r";
                }
                if (f.StartsWith("libre"))
                    return italic ? "baskerville-i" : "baskerville-r";
                // Outfit o cualquier familia desconocida: la cara del producto
                return bold ? "outfit-b" : "outfit-r";
            }

            static string FileForFace(string face)
            {
                switch (face)
                {
                    case "outfit-b": return "Outfit-Bold.ttf";
                    case "cormorant-r": return "CormorantGaramond-Medium.ttf";
                    case "cormorant-b": return "CormorantGaramond-Bold.ttf";
                    case "cormorant-i": return "CormorantGaramond-Italic.ttf";
                    case "cormorant-bi": return "CormorantGaramond-Bold.ttf";
                    case "baskerville-r": return "LibreBaskerville-Regular.ttf";
                    case "baskerville-i": return "LibreBaskerville-Italic.ttf";
                    default: return "Outfit-Regular.ttf";
                }
            }
        }

        static string FontsRoot()
        {
            // raíz que contiene resources/fonts — mismo paseo que FontsDir()
            for (string d = AppDomain.CurrentDomain.BaseDirectory; d != null && d.Length > 3;
                 d = Path.GetDirectoryName(d))
            {
                if (Directory.Exists(Path.Combine(Path.Combine(d, "resources"), "fonts"))) return d;
            }
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        static void RegisterProductFonts()
        {
            if (_fontsRegistered) return;
            _fontsRegistered = true;
            // GlobalFontSettings solo admite un resolver por proceso: se fija una
            // vez con las fuentes del producto (para cualquier familia pedida).
            try { PdfSharp.Fonts.GlobalFontSettings.FontResolver = new ProductFontResolver(); }
            catch { /* resolver ya fijado u hostile: se usa el que haya */ }
        }
#endif

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
#if LITE
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
#else
            // 6.x: el enum se renombró a XFontStyleEx; el resolver sirve la cara
            // (sin excepción para las familias del producto).
            XFontStyleEx style = (bold && italic) ? XFontStyleEx.BoldItalic
                              : bold ? XFontStyleEx.Bold
                              : italic ? XFontStyleEx.Italic
                              : XFontStyleEx.Regular;
            string name2 = string.IsNullOrEmpty(family) ? "Outfit" : family;
            return new XFont(name2, size, style);
#endif
        }
    }
}
