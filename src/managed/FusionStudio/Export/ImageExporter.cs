// ============================================================================
//  Fusion-HP · Export/ImageExporter.cs — exportación de Escenarios a imágenes
//  [SPEC §9.3.3]: PNG a resolución configurable (mínimo 1920×1080), render
//  con GDI+ del estado resuelto (el mismo modelo que el núcleo proyecta).
// ============================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Fusion.Shared.Model;

namespace Fusion.Studio.Export
{
    public class ImageExporter
    {
        public int Width = 1920;
        public int Height = 1080;

        public int Export(AhpProject project, string outPath, string baseDir)
        {
            int n = 0;
            string dir = Path.GetDirectoryName(outPath);
            string baseName = Path.GetFileNameWithoutExtension(outPath);
            Directory.CreateDirectory(dir);
            foreach (var scn in project.Scenarios)
            {
                foreach (var el in scn.Elements)
                {
                    var resolved = ResolvedSlide.Resolve(el, scn, project, baseDir);
                    using (var bmp = new Bitmap(Width, Height))
                    {
                        var g = Graphics.FromImage(bmp);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                        Render(g, resolved, Width, Height, baseDir);
                        string file = Path.Combine(dir, baseName + "-" + (n + 1).ToString("000") + ".png");
                        bmp.Save(file, ImageFormat.Png);
                        n++;
                    }
                }
            }
            return n;
        }

        static void Render(Graphics g, ResolvedSlide s, int W, int H, string baseDir)
        {
            using (var b = new SolidBrush(Parse(s.Background.Color)))
                g.FillRectangle(b, 0, 0, W, H);
            if (!string.IsNullOrEmpty(s.Background.Image) && File.Exists(s.Background.Image))
            {
                try
                {
                    using (var img = Image.FromFile(s.Background.Image))
                    {
                        double sc = (s.Background.Fit ?? 0) == 0
                            ? Math.Max((double)W / img.Width, (double)H / img.Height)
                            : Math.Min((double)W / img.Width, (double)H / img.Height);
                        int dw = (int)(img.Width * sc), dh = (int)(img.Height * sc);
                        g.DrawImage(img, (W - dw) / 2, (H - dh) / 2, dw, dh);
                    }
                }
                catch { }
            }
            if (s.Kind == "image" && !string.IsNullOrEmpty(s.Src) && File.Exists(s.Src))
            {
                try
                {
                    using (var img = Image.FromFile(s.Src))
                    {
                        double sc = Math.Min((double)W / img.Width, (double)H / img.Height);
                        int dw = (int)(img.Width * sc), dh = (int)(img.Height * sc);
                        g.DrawImage(img, (W - dw) / 2, (H - dh) / 2, dw, dh);
                    }
                }
                catch { }
            }
            else if (s.Lines.Count > 0)
            {
                var st = s.Style;
                var box = new RectangleF((float)((st.BoxX ?? 0.05) * W), (float)((st.BoxY ?? 0.08) * H),
                                         (float)((st.BoxW ?? 0.9) * W), (float)((st.BoxH ?? 0.84) * H));
                // v4.2.0 (C6): MISMA fórmula que la preview y el núcleo (antes H/720
                // + factor 1.35 propio: lo exportado no coincidía con lo previsualizado)
                float sizePt = (float)(st.Size ?? 48) * H / 1080f;
                float lineH = Fusion.Studio.Ui.SlidePreview.ComputeLineHeight(sizePt, (float)(st.LineSpacing ?? 1.15));
                float total = lineH * s.Lines.Count;
                if (total > box.Height)
                {
                    sizePt *= box.Height / total;
                    lineH = Fusion.Studio.Ui.SlidePreview.ComputeLineHeight(sizePt, (float)(st.LineSpacing ?? 1.15));
                }
                using (var f = Fusion.Shared.Media.FontVault.Create(st.Font ?? "Segoe UI", Math.Max(8, sizePt),
                                        ((st.Bold ?? false) ? FontStyle.Bold : FontStyle.Regular)))
                {
                    var sf = new StringFormat();
                    sf.Alignment = (st.Align ?? 1) == 0 ? StringAlignment.Near :
                                   (st.Align ?? 1) == 2 ? StringAlignment.Far : StringAlignment.Center;
                    float y = (st.VAlign ?? 1) == 0 ? box.Y : box.Y + (box.Height - lineH * s.Lines.Count) / 2;
                    for (int i = 0; i < s.Lines.Count; i++)
                    {
                        var rect = new RectangleF(box.X, y, box.Width, lineH);
                        using (var sh = new SolidBrush(Color.FromArgb(200, Color.Black)))
                            g.DrawString(s.Lines[i], f, sh, new RectangleF(rect.X + 2, rect.Y + 2, rect.Width, rect.Height), sf);
                        using (var b = new SolidBrush(Parse(i == 0 ? (st.ActiveColor ?? "#FFD700") : (st.Color ?? "#FFFFFF"))))
                            g.DrawString(s.Lines[i], f, b, rect, sf);
                        y += lineH;
                    }
                }
            }
            if (!string.IsNullOrEmpty(s.OverlayText))
            {
                int bandH = (int)(H * 0.16);
                using (var b = new SolidBrush(Color.FromArgb(160, Color.Black)))
                    g.FillRectangle(b, 0, H - bandH, W, bandH);
                using (var f = new Font("Segoe UI", bandH * 0.36f, FontStyle.Bold))
                using (var b = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(s.OverlayText, f, b, new RectangleF(W * 0.05f, H - bandH, W * 0.9f, bandH), sf);
                }
            }
        }

        static Color Parse(string hex)
        {
            try
            {
                string h = (hex ?? "").TrimStart('#');
                if (h.Length == 6) return Color.FromArgb(255, Convert.ToInt32(h.Substring(0, 2), 16),
                    Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
                if (h.Length == 8) return Color.FromArgb(Convert.ToInt32(h.Substring(0, 2), 16),
                    Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16),
                    Convert.ToInt32(h.Substring(6, 2), 16));
            }
            catch { }
            return Color.White;
        }
    }
}
