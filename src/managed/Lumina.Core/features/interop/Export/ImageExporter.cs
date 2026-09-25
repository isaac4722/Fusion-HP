// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ImageExporter.cs : exportación del Escenario a ARCHIVOS DE IMAGEN
//  (F4.13). Formatos JPG/PNG/GIF/BMP/TIF; resolución configurable con
//  mínimo 1920×1080; exporta el Escenario individual (una imagen por slide).
//
//  Diseño:
//    * El render NO vive aquí: el llamador inyecta Func<int, byte[]> que
//      devuelve el PNG de la slide i (el núcleo ya lo produce con
//      lumina_render_preview_png). Esto mantiene el exportador testeable sin
//      núcleo nativo (el CI pasa un generador stub) y respeta la norma de
//      dependencias de Lumina.Core.
//    * La re-codificación a los 5 formatos usa System.Drawing (GDI+),
//      presente en .NET Framework 3.5/4.8 (las variantes que SE DISTRIBUYEN).
//      Por eso el tipo se compila SOLO en NETFRAMEWORK (#if): en net8.0 (arnés
//      de tests sobre Linux) System.Drawing.Common no está disponible y no se
//      distribuye ninguna variante net8. El test del arnés lo ejercita en la
//      corrida net48 del CI (Windows, GDI+ real).
//    * Reescalado: si el render llega más pequeño que el objetivo se hace
//      upscale con HighQualityBicubic; nunca se entrega por debajo del mínimo.
//    * Nombres: <base>-001.<ext>… (orden estable para diapositivas).
// ============================================================================
#if NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace lumina.core
{
    /// <summary>Formato de salida soportado por la exportación de imágenes.</summary>
    public enum ImageExportFormat { Jpg = 0, Png = 1, Gif = 2, Bmp = 3, Tif = 4 }

    /// <summary>Opciones de la exportación de imágenes (F4.13).</summary>
    public sealed class ImageExportOptions
    {
        public ImageExportFormat Format = ImageExportFormat.Png;
        /// <summary>Ancho objetivo (mínimo 1920; la norma del doc técnico).</summary>
        public int Width = 1920;
        /// <summary>Alto objetivo (mínimo 1080; la norma del doc técnico).</summary>
        public int Height = 1080;
        /// <summary>Calidad JPG (1-100); ignorada en los demás formatos.</summary>
        public int JpegQuality = 92;

        /// <summary>Aplica el mínimo normativo 1920×1080.</summary>
        public void ClampToMinimum()
        {
            if (Width < 1920) Width = 1920;
            if (Height < 1080) Height = 1080;
        }

        /// <summary>Extensión de archivo (sin punto) del formato activo.</summary>
        public string Extension
        {
            get
            {
                switch (Format)
                {
                    case ImageExportFormat.Jpg: return "jpg";
                    case ImageExportFormat.Png: return "png";
                    case ImageExportFormat.Gif: return "gif";
                    case ImageExportFormat.Bmp: return "bmp";
                    default: return "tif";
                }
            }
        }
    }

    /// <summary>Resultado detallado: archivos escritos y fallos por slide.</summary>
    public sealed class ImageExportResult
    {
        public List<string> Files = new List<string>();
        public List<string> Errors = new List<string>();
        /// <summary>true si al menos un archivo quedó escrito y el formato es válido.</summary>
        public bool Success { get { return Files.Count > 0 && Files.Count + Errors.Count > 0; } }
    }

    public static class ImageExporter
    {
        /// <summary>ImageCodecInfo del formato (para calidad JPG real).</summary>
        private static ImageCodecInfo CodecFor(ImageFormat fmt)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo c in codecs)
                if (c.FormatID == fmt.Guid) return c;
            return null;
        }

        /// <summary>Reescala (o deja tal cual) al tamaño objetivo.</summary>
        private static Bitmap FitTo(Bitmap src, int width, int height)
        {
            if (src.Width == width && src.Height == height) return src;
            Bitmap dst = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, width, height));
            }
            return dst;
        }

        /// <summary>
        /// Exporta cada slide a imagen. <paramref name="renderSlidePng"/> recibe
        /// el índice 0-based y devuelve PNG (o null → error de esa slide, la
        /// exportación continúa con las demás e informa).
        /// </summary>
        public static ImageExportResult Export(int slideCount, Func<int, byte[]> renderSlidePng,
                                               string outputDirectory, string baseName,
                                               ImageExportOptions options)
        {
            ImageExportResult res = new ImageExportResult();
            if (slideCount <= 0) res.Errors.Add("no hay slides que exportar");
            if (renderSlidePng == null) { res.Errors.Add("render no disponible"); return res; }
            if (options == null) options = new ImageExportOptions();
            options.ClampToMinimum();
            if (string.IsNullOrEmpty(outputDirectory)) outputDirectory = ".";
            if (string.IsNullOrEmpty(baseName)) baseName = "escenario";
            try { Directory.CreateDirectory(outputDirectory); }
            catch (Exception ex) { res.Errors.Add("carpeta: " + ex.Message); return res; }

            ImageFormat fmt;
            switch (options.Format)
            {
                case ImageExportFormat.Jpg: fmt = ImageFormat.Jpeg; break;
                case ImageExportFormat.Gif: fmt = ImageFormat.Gif; break;
                case ImageExportFormat.Bmp: fmt = ImageFormat.Bmp; break;
                case ImageExportFormat.Tif: fmt = ImageFormat.Tiff; break;
                default: fmt = ImageFormat.Png; break;
            }
            ImageCodecInfo codec = CodecFor(fmt);
            EncoderParameters eps = null;
            if (options.Format == ImageExportFormat.Jpg && codec != null)
            {
                eps = new EncoderParameters(1);
                eps.Param[0] = new EncoderParameter(Encoder.Quality,
                    (long)Math.Max(1, Math.Min(100, options.JpegQuality)));
            }

            try
            {
                for (int i = 0; i < slideCount; i++)
                {
                    string file = Path.Combine(outputDirectory,
                        baseName + "-" + (i + 1).ToString("000", System.Globalization.CultureInfo.InvariantCulture)
                        + "." + options.Extension);
                    byte[] png = null;
                    try { png = renderSlidePng(i); }
                    catch (Exception ex) { res.Errors.Add("slide " + (i + 1) + ": " + ex.Message); continue; }
                    if (png == null || png.Length == 0)
                    {
                        res.Errors.Add("slide " + (i + 1) + ": render vacío");
                        continue;
                    }
                    try
                    {
                        using (MemoryStream ms = new MemoryStream(png))
                        using (Bitmap src = new Bitmap(ms))
                        using (Bitmap target = FitTo(src, options.Width, options.Height))
                        {
                            // Guarda SIEMPRE vía codec (GDI+ escribe el contenedor real).
                            if (codec != null) target.Save(file, codec, eps);
                            else target.Save(file, fmt);
                        }
                        res.Files.Add(file);
                    }
                    catch (Exception ex)
                    {
                        res.Errors.Add("slide " + (i + 1) + ": " + ex.Message);
                    }
                }
            }
            finally
            {
                if (eps != null) eps.Dispose();
            }
            return res;
        }
    }
}
#endif // NETFRAMEWORK
