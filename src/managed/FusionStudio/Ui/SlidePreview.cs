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
using Fusion.Shared.Media;
using Fusion.Shared.Text;

namespace Fusion.Studio.Ui
{
    public class SlidePreview : System.Windows.Forms.Control
    {
        ResolvedSlide slide;
        int activeLine;
        bool blank = true;
        string blankMode = "black";

        /// <summary>Ruta del logo de reposo (v4.2.0): la preview de la tecla L
        /// muestra el MISMO logo que la salida — antes pintaba negro puro y el
        /// operador no podía verificar el logo sin proyectarlo.</summary>
        public static string LogoPath;

        /// <summary>Fuente de reposo "logo" compartida (una sola instancia).</summary>
        static Image cachedLogo;
        static string cachedLogoPath;

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

            if (blank && blankMode != "clear" || slide == null)
            {
                using (var b = new SolidBrush(blankMode == "theme" && slide != null ? Parse(slide.Background.Color) : Color.Black))
                    g.FillRectangle(b, 0, 0, W, H);
                if (blankMode == "logo")
                {
                    // v4.2.0: reposo «logo» dibuja el logo real configurado
                    Image logo = LoadLogo();
                    if (logo != null)
                    {
                        double sc = Math.Min((double)W / logo.Width, (double)H / logo.Height);
                        int dw = (int)(logo.Width * sc), dh = (int)(logo.Height * sc);
                        g.DrawImage(logo, (W - dw) / 2, (H - dh) / 2, dw, dh);
                    }
                }
                return;
            }
            if (blank && blankMode == "clear")
            {
                // Tecla C: el fondo permanece, el texto se oculta (referencia web)
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
                return;
            }

            RenderSlide(g, slide, activeLine, W, H);
        }

        /// <summary>Render del slide RESUELTO reutilizable: misma rutina para la
        /// preview, las miniaturas del programa y el clasificador [SPEC §7.4.2].
        /// v4.0.0: extraído de OnPaint para las miniaturas del programa.</summary>
        public static void RenderSlide(Graphics g, ResolvedSlide slide, int activeLine, int W, int H)
        {
            if (slide == null || W <= 0 || H <= 0) return;

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
                using (var b = new SolidBrush(Color.FromArgb(200, Color.White)))
                    g.DrawString("▶ Video (se reproduce en la salida)", videoFont, b, new RectangleF(8, H - 40, W - 16, 30));
            }
            else if (slide.Lines.Count > 0)
            {
                var st = slide.Style;
                var box = new RectangleF(
                    (float)((st.BoxX ?? 0.05) * W), (float)((st.BoxY ?? 0.08) * H),
                    (float)((st.BoxW ?? 0.9) * W), (float)((st.BoxH ?? 0.84) * H));

                // auto-ajuste de tamaño (igual que el núcleo)
                // v4.2.0 (C6): se ELIMINÓ la doble escala del interlineado — lineH
                // volvía a multiplicar por H/1080 después de que sizePt ya estaba
                // escalado, y las líneas quedaban solapadas ~60 % en preview,
                // miniaturas del programa y Clasificador. La fórmula ahora es la
                // del núcleo: sizePt·spacing·96/72 (y auto-ajuste si no cabe).
                float sizePt = (float)(st.Size ?? 48) * H / 1080f;   // escala respecto a 1080p
                float lineH = ComputeLineHeight(sizePt, (float)(st.LineSpacing ?? 1.15));
                float total = lineH * slide.Lines.Count;
                if (total > box.Height)
                {
                    sizePt *= box.Height / total;
                    lineH = ComputeLineHeight(sizePt, (float)(st.LineSpacing ?? 1.15));
                }

                using (var f = FontVault.Create(st.Font ?? "Segoe UI", Math.Max(6, sizePt),
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
                        if (slide.HighlightWords != null && slide.HighlightWords.Count > 0)
                        {
                            // Resaltado [SPEC §5.2 #2]: coincidencias en color de acento
                            var segs = Highlight.Split(slide.Lines[i], slide.HighlightWords);
                            var near = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                            float totalW = 0;
                            var widths = new float[segs.Count];
                            for (int k = 0; k < segs.Count; k++)
                            {
                                widths[k] = g.MeasureString(segs[k].Text, f).Width;
                                totalW += widths[k];
                            }
                            float x0 = (st.Align ?? 1) == 0 ? box.X :
                                       (st.Align ?? 1) == 2 ? box.X + box.Width - totalW :
                                       box.X + (box.Width - totalW) / 2;
                            if (st.Shadow ?? true)
                                using (var sb = new SolidBrush(Color.FromArgb(215, Color.Black)))
                                {
                                    float x = x0;
                                    for (int k = 0; k < segs.Count; k++)
                                    {
                                        g.DrawString(segs[k].Text, f, sb, new RectangleF(x + 1.5f, rect.Y + 1.5f, widths[k] + 4, rect.Height), near);
                                        x += widths[k];
                                    }
                                }
                            float xx = x0;
                            for (int k = 0; k < segs.Count; k++)
                            {
                                using (var b = new SolidBrush(segs[k].Match
                                    ? Parse(st.ActiveColor ?? "#FFD700")
                                    : (active ? Parse(st.ActiveColor ?? "#FFD700") : Parse(st.Color ?? "#FFFFFF"))))
                                {
                                    g.DrawString(segs[k].Text, f, b, new RectangleF(xx, rect.Y, widths[k] + 4, rect.Height), near);
                                    xx += widths[k];
                                }
                            }
                        }
                        else
                        {
                            if (st.Shadow ?? true)
                            {
                                using (var sb = new SolidBrush(Color.FromArgb(215, Color.Black)))
                                    g.DrawString(slide.Lines[i], f, sb, new RectangleF(rect.X + 1.5f, rect.Y + 1.5f, rect.Width, rect.Height), sf);
                            }
                            using (var b = new SolidBrush(active ? Parse(st.ActiveColor ?? "#FFD700") : Parse(st.Color ?? "#FFFFFF")))
                                g.DrawString(slide.Lines[i], f, b, rect, sf);
                        }
                        y += lineH;
                    }
                }
                if (slide.Kind == "verse" && !string.IsNullOrEmpty(slide.Reference))
                {
                    using (var f = FontVault.Create(st.Font ?? "Segoe UI", Math.Max(7, sizePt * 0.38f), FontStyle.Italic))
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
                using (var f = FontVault.Create(slide.Style.Font ?? "Segoe UI", bandH * 0.36f, FontStyle.Bold))
                using (var b = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(slide.OverlayText, f, b, new RectangleF(W * 0.05f, H - bandH, W * 0.9f, bandH), sf);
                }
            }
        }

        /// <summary>Interlineado de la preview (v4.2.0): MISMA fórmula que el núcleo
        /// C++ — pts→px (96/72) × interlineado del estilo. Pública para las pruebas
        /// de regresión de la fórmula (C6).</summary>
        public static float ComputeLineHeight(float sizePt, float lineSpacing)
        {
            return sizePt * Math.Max(0.5f, lineSpacing) * 96f / 72f;
        }

        static Image LoadLogo()
        {
            string p = LogoPath;
            if (string.IsNullOrEmpty(p) || !System.IO.File.Exists(p)) return null;
            if (cachedLogo != null && string.Equals(cachedLogoPath, p, StringComparison.OrdinalIgnoreCase))
                return cachedLogo;
            try
            {
                if (cachedLogo != null) cachedLogo.Dispose();
                using (var src = Image.FromFile(p))
                {
                    cachedLogo = new Bitmap(src);        // copia: no bloquea el archivo
                    cachedLogoPath = p;
                }
            }
            catch { cachedLogo = null; }
            return cachedLogo;
        }

        static readonly Font videoFont = new Font("Segoe UI", 11f, FontStyle.Italic);   // v4.2.0: compartida (fuga por frame)

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
