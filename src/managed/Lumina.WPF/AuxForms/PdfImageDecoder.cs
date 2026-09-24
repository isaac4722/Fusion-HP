// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  PdfImageDecoder.cs : decodificador de imágenes para PdfExporter usando
//  GDI+ (System.Drawing — integrado en Windows). Convierte PNG/BMP/GIF/TIFF
//  a RGB crudo para /Filter /FlateDecode. Los JPEG van directos por DCTDecode
//  (el propio PdfExporter los detecta por firma).
// ============================================================================
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using lumina.core;

namespace lumina.ui
{
    internal static class PdfImageDecoder
    {
        private static bool _installed;

        /// <summary>Instala el decodificador en PdfExporter (idempotente).</summary>
        internal static void Install()
        {
            if (_installed) return;
            _installed = true;
            PdfExporter.ImageDecoder = delegate(string path)
            {
                using (Bitmap bmp = new Bitmap(path))
                {
                    Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                    BitmapData d = bmp.LockBits(rect, ImageLockMode.ReadOnly,
                        PixelFormat.Format24bppRgb);
                    try
                    {
                        byte[] rgb = new byte[d.Width * d.Height * 3];
                        int rowBytes = d.Width * 3;
                        for (int y = 0; y < d.Height; y++)
                        {
                            IntPtr src = new IntPtr(d.Scan0.ToInt64() + y * d.Stride);
                            Marshal.Copy(src, rgb, y * rowBytes, rowBytes);
                        }
                        return new PdfImage(d.Width, d.Height, rgb);
                    }
                    finally
                    {
                        bmp.UnlockBits(d);
                    }
                }
            };
        }
    }
}
