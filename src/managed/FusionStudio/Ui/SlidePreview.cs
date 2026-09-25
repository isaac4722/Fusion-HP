// ============================================================================
//  Fusion-HP · FusionStudio/Ui/SlidePreview.cs — previsualización del estado
//  resuelto usando el MISMO contrato de render que el núcleo [SPEC §7.4.2]:
//  la preview no es una imitación distinta, es el mismo modelo resuelto
//  dibujado con GDI+ a escala.
// ============================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Fusion.Shared.Model;

namespace Fusion.Studio.Ui
{
    public class SlidePreview : System.Windows.Forms.Control
    {
        ResolvedSlide slide;
        int activeLine;
        bool blank = true;
        string blankMode = "black";

        public SlidePreview()
        {
            SetStyle(System.Windows.Forms.ControlStyles.AllPaintingInWmPaint |
                     System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer |
                     System.Windows.Forms.ControlStyles.UserPaint |
                     System.Windows.Forms.ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
        }

        public void ShowSlide(ResolvedSlide s, int line)
        {
            slide = s;
            activeLine = line;
            blank = false;
            Invalidate();
        }

        public void ShowBlank(string mode)
        {
            blank = true;
            blankMode = mode;
            Invalidate();
        }

        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int W = Width, H = Height;
            if (W <= 0 || H <= 0) return;

            if (blank || slide == null)
            {
                using (var b = new SolidBrush(blankMode == "theme" && slide != null ? Parse(slide.Background.Color) : Color.Black))
                    g.FillRectangle(b, 0, 0, W, H);
                return;
            }

            // Fondo
            using (var b = new SolidBrush(Parse(slide.Background.Color)))
                g.FillRectangle(b, 0, 0, W, H);
            if (!string.IsNullOrEmpty(slide.Background.Image) && System.IO.File.Exists(slide.Background.Image))
            {
                try
                {
                    using (var img = Image.FromFile(slide.Background.Image))
                    {
                        double sc = (slide.Background.Fit ?? 0) == 0
                            ? Math.Max((double)W / img.Width, (double)H / img.Height)
                            : Math.Min((double)W / img.Width, (double)H / img.Height);
                        int dw = (int)(img.Width * sc), dh = (int)(img.Height * sc);
                        g.DrawImage(img, (W - dw) / 2, (H - dh) / 2, dw, dh);
                    }
                }
                catch { }
            }

            // Contenido
            if (slide.Kind == "image" && !string.IsNullOrEmpty(slide.Src) && System.IO.File.Exists(slide.Src))
            {
                try
                {
                    using (var img = Image.FromFile(slide.Src))
                    {
                        double sc = Math.Min((double)W / img.Width, (double)H / img.Height);
                        int dw = (int)(img.Width * sc), dh = (int)(img.Height * sc);
                        g.DrawImage(img, (W - dw) / 2, (H - dh) / 2, dw, dh);
                    }
                }
                catch { }
            }
            else if (slide.Kind == "video")
            {
                using (var f = new Font("Segoe UI", 11, FontStyle.Italic))
                using (var b = new SolidBrush(Color.FromArgb(200, Color.White)))
                    g.DrawString("▶ Video (se reproduce en la salida)", f, b, new RectangleF(8, H - 40, W - 16, 30));
            }
            else if (slide.Lines.Count > 0)
            {
                var st = slide.Style;
                var box = new RectangleF(
                    (float)((st.BoxX ?? 0.05) * W), (float)((st.BoxY ?? 0.08) * H),
                    (float)((st.BoxW ?? 0.9) * W), (float)((st.BoxH ?? 0.84) * H));

                // auto-ajuste de tamaño (igual que el núcleo)
                float sizePt = (float)(st.Size ?? 48) * H / 1080f;   // escala respecto a 1080p
                float lineH = sizePt * (float)(st.LineSpacing ?? 1.15) * 96f / 72f * (H / 1080f) * 1.15f;
                float total = lineH * slide.Lines.Count;
                if (total > box.Height) { sizePt *= box.Height / total; lineH = sizePt * (float)(st.LineSpacing ?? 1.15) * 96f / 72f * (H / 1080f) * 1.15f; }

                using (var f = new Font(st.Font ?? "Segoe UI", Math.Max(6, sizePt),
                                        ((st.Bold ?? false) ? FontStyle.Bold : FontStyle.Regular) |
                                        ((st.Italic ?? false) ? FontStyle.Italic : FontStyle.Regular)))
                {
                    var sf = new StringFormat();
                    sf.Alignment = (st.Align ?? 1) == 0 ? StringAlignment.Near :
                                   (st.Align ?? 1) == 2 ? StringAlignment.Far : StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Near;
                    float y = (st.VAlign ?? 1) == 0 ? box.Y : box.Y + (box.Height - lineH * slide.Lines.Count) / 2;
                    for (int i = 0; i < slide.Lines.Count; i++)
                    {
                        bool active = i == activeLine;
                        var rect = new RectangleF(box.X, y, box.Width, lineH);
                        if (st.Shadow ?? true)
                        {
                            using (var sb = new SolidBrush(Color.FromArgb(215, Color.Black)))
                                g.DrawString(slide.Lines[i], f, sb, new RectangleF(rect.X + 1.5f, rect.Y + 1.5f, rect.Width, rect.Height), sf);
                        }
                        using (var b = new SolidBrush(active ? Parse(st.ActiveColor ?? "#FFD700") : Parse(st.Color ?? "#FFFFFF")))
                            g.DrawString(slide.Lines[i], f, b, rect, sf);
                        y += lineH;
                    }
                }
                if (slide.Kind == "verse" && !string.IsNullOrEmpty(slide.Reference))
                {
                    using (var f = new Font("Segoe UI", Math.Max(7, sizePt * 0.38f), FontStyle.Italic))
                    using (var b = new SolidBrush(Parse(slide.Style.ActiveColor ?? "#FFD700")))
                    {
                        var sf = new StringFormat { Alignment = StringAlignment.Center };
                        g.DrawString(slide.Reference, f, b, new RectangleF(box.X, box.Bottom - Math.Max(16, sizePt * 0.6f), box.Width, Math.Max(18, sizePt * 0.66f)), sf);
                    }
                }
            }

            // Lower third
            if (!string.IsNullOrEmpty(slide.OverlayText))
            {
                int bandH = (int)(H * 0.16);
                using (var b = new SolidBrush(Color.FromArgb(160, Color.Black)))
                    g.FillRectangle(b, 0, H - bandH, W, bandH);
                using (var b = new SolidBrush(Parse(slide.Style.ActiveColor ?? "#FFD700")))
                    g.FillRectangle(b, 0, H - bandH, W, 3);
                using (var f = new Font("Segoe UI", bandH * 0.36f, FontStyle.Bold))
                using (var b = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat { Alignment = StringAlignmentCenter, LineAlignment = StringAlignment.Center };
                    g.DrawString(slide.OverlayText, f, b, new RectangleF(W * 0.05f, H - bandH, W * 0.9f, bandH), sf);
                }
            }
        }

        static Color Parse(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return Color.White;
                string h = hex.TrimStart('#');
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
