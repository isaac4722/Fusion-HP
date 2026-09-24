// ============================================================================
//  LuminaPresentation Suite v5.3.0 «INTERFAZ» — managed/Lumina.UI/UiKit.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  UiKit.cs : sistema de diseño «Lumina Studio» v2 (rediseño UI/UX v5.3.0).
//
//  Contiene (movido desde MainForm.cs y ampliado):
//    * IconKind + IconPainter — set de iconos vectoriales 16×16 escalables
//      (GDI+ puro, sin dependencias: idéntico en Win7 SP1 y Win11).
//    * BrandGlyph — logotipo «faro Lumina» (cuadrado ámbar + destellos).
//    * UiTheme — paleta refinada, tipografía y fábrica de controles
//      (API pública IDÉNTICA a v5.2.0: los ~5.400 lines de MainForm.cs
//      compilan sin tocar una sola llamada a MkButton/MkCard/MkLabel…).
//    * ContentPanel — panel sin parpadeo.
//    * LuminaButton — botón plano redondeado con variantes, icono, hover,
//      pulsación, foco visible y estado «activo» (toggles p. ej. Negro).
//    * LuminaCheck — casilla oscura coherente con el tema (WinForms puro).
//    * Chip — píldora de estado (punto de color + texto).
//    * NavPanel — navegación lateral con secciones agrupadas.
//
//  REGLAS (skill winforms-gui-ux-clean):
//    * Nada de Color.Transparent sin SupportsTransparentBackColor.
//    * Defensa en profundidad: los controles pintan el fondo SÓLIDO del
//      padre; nunca dependen de la simulación de transparencia de WinForms.
//    * Todo constructor es a prueba de null: la ventana SIEMPRE se construye
//      (gate --uicheck de la CI lo verifica sobre el binario publicado).
// ============================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace lumina.ui
{
    /* ====================================================================== */
    /*  ICONOGRAFÍA VECTORIAL (16×16 lógicos, escala arbitraria)              */
    /* ====================================================================== */

    /// <summary>Iconos del producto (dibujados con GDI+, sin recursos).</summary>
    internal enum IconKind
    {
        None,
        Play, Pause, Prev, Next, Black, Live,
        Refresh, Projector, Screen, Eye,
        Stage, Director, Third, Slides,
        Song, Book, Palette, ServiceList,
        Export, Nodes, Bolt, Gear, Search,
        Plus, Trash, ArrowUp, ArrowDown, Close, Check,
        Warning, Info, Folder, Image
    }

    /// <summary>
    /// Pinta los iconos del producto dentro del rectángulo dado. Todos están
    /// diseñados sobre una retícula lógica de 16×16 y se escalan al tamaño
    /// solicitado (anti-alias siempre activo; grosor de trazo proporcional).
    /// </summary>
    internal static class IconPainter
    {
        /// <summary>Dibuja <paramref name="icon"/> centrado en el rect.</summary>
        public static void Draw(Graphics g, IconKind icon, Rectangle bounds, Color color)
        {
            if (g == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            Draw(g, icon, bounds.X, bounds.Y,
                 Math.Min(bounds.Width, bounds.Height), color);
        }

        /// <summary>Dibuja <paramref name="icon"/> en (x,y) con lado <paramref name="size"/>.</summary>
        public static void Draw(Graphics g, IconKind icon, int x, int y, int size, Color color)
        {
            if (g == null || size <= 0 || icon == IconKind.None) return;
            try { PaintIcon(g, icon, x, y, size, color); }
            catch (Exception) { /* pintar un icono JAMÁS tira la ventana */ }
        }

        private static void PaintIcon(Graphics g, IconKind icon, int x, int y, int size, Color c)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float s = size / 16f;                    // factor de escala
            float pw = Math.Max(1f, 1.6f * s);       // grosor de trazo
            // coordenadas lógicas (u,v en la retícula 16×16) → píxeles
            float X(float u) { return x + u * s; }
            float Y(float v) { return y + v * s; }

            using (Pen p = new Pen(c, pw))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                using (SolidBrush b = new SolidBrush(c))
                {
                    switch (icon)
                    {
                        case IconKind.Play:
                            FillTri(g, b, X(5), Y(3), X(13), Y(8), X(5), Y(13));
                            break;
                        case IconKind.Pause:
                            FillRectF(g, b, X(4), Y(3), 3f * s, 10f * s);
                            FillRectF(g, b, X(9), Y(3), 3f * s, 10f * s);
                            break;
                        case IconKind.Prev:
                            g.DrawLine(p, X(3.5f), Y(3), X(3.5f), Y(13));
                            FillTri(g, b, X(12), Y(3), X(6), Y(8), X(12), Y(13));
                            break;
                        case IconKind.Next:
                            g.DrawLine(p, X(12.5f), Y(3), X(12.5f), Y(13));
                            FillTri(g, b, X(4), Y(3), X(10), Y(8), X(4), Y(13));
                            break;
                        case IconKind.Black:
                            FillRectF(g, b, X(4), Y(4), 8f * s, 8f * s);
                            break;
                        case IconKind.Live:
                            g.FillEllipse(b, X(5.4f), Y(5.4f), 5.2f * s, 5.2f * s);
                            g.DrawEllipse(p, X(2.2f), Y(2.2f), 11.6f * s, 11.6f * s);
                            break;
                        case IconKind.Refresh:
                            g.DrawArc(p, X(2.5f), Y(2.5f), 11f * s, 11f * s, -30, 300);
                            FillTri(g, b, X(11), Y(1), X(15), Y(4.5f), X(10f), Y(5.5f));
                            break;
                        case IconKind.Projector:
                            g.DrawRectangle(p, X(1), Y(4), 10f * s, 7f * s);
                            g.FillEllipse(b, X(10.5f), Y(5.8f), 3.4f * s, 3.4f * s);
                            g.DrawLine(p, X(3), Y(11), X(3), Y(13.5f));
                            g.DrawLine(p, X(9), Y(11), X(9), Y(13.5f));
                            break;
                        case IconKind.Screen:
                            g.DrawRectangle(p, X(1.5f), Y(2.5f), 13f * s, 9f * s);
                            g.DrawLine(p, X(8), Y(11.5f), X(8), Y(14));
                            g.DrawLine(p, X(5), Y(14), X(11), Y(14));
                            break;
                        case IconKind.Eye:
                            g.DrawEllipse(p, X(2), Y(4), 12f * s, 8f * s);
                            g.FillEllipse(b, X(5.7f), Y(5.7f), 4.6f * s, 4.6f * s);
                            break;
                        case IconKind.Stage: // persona (monitor de escenario)
                            g.FillEllipse(b, X(6), Y(1.5f), 4f * s, 4f * s);
                            FillTri(g, b, X(3), Y(14), X(6.2f), Y(7.5f), X(9.8f), Y(7.5f));
                            FillTri(g, b, X(3), Y(14), X(9.8f), Y(7.5f), X(13), Y(14));
                            break;
                        case IconKind.Director: // claqueta
                            g.DrawRectangle(p, X(2), Y(6.5f), 12f * s, 7f * s);
                            g.DrawLine(p, X(2), Y(6.5f), X(4.5f), Y(2.5f));
                            g.DrawLine(p, X(4.5f), Y(2.5f), X(14), Y(2.5f));
                            g.DrawLine(p, X(14), Y(2.5f), X(14), Y(6.5f));
                            g.DrawLine(p, X(5.5f), Y(6.2f), X(7.5f), Y(2.8f));
                            g.DrawLine(p, X(8.5f), Y(6.2f), X(10.5f), Y(2.8f));
                            g.DrawLine(p, X(11.5f), Y(6.2f), X(13.5f), Y(2.8f));
                            break;
                        case IconKind.Third: // zócalo inferior
                            g.DrawLine(p, X(4), Y(3.5f), X(12), Y(3.5f));
                            FillRectF(g, b, X(2), Y(9), 12f * s, 3.6f * s);
                            break;
                        case IconKind.Slides: // diapositivas apiladas
                            g.DrawRectangle(p, X(4.5f), Y(1.5f), 9f * s, 7f * s);
                            g.DrawRectangle(p, X(2), Y(6), 9f * s, 7f * s);
                            FillRectF(g, b, X(4), Y(8), 5f * s, 3.4f * s);
                            break;
                        case IconKind.Song: // nota musical
                            g.FillEllipse(b, X(2.5f), Y(10), 5f * s, 4f * s);
                            g.DrawLine(p, X(7.5f), Y(12), X(7.5f), Y(2));
                            FillTri(g, b, X(7.5f), Y(2), X(13), Y(4), X(7.5f), Y(6.5f));
                            break;
                        case IconKind.Book: // biblia: libro + cruz
                            g.DrawRectangle(p, X(2.5f), Y(1.5f), 11f * s, 13f * s);
                            g.DrawLine(p, X(8), Y(1.5f), X(8), Y(14.5f));
                            FillRectF(g, b, X(10.2f), Y(4f), 1.8f * s, 6f * s);
                            FillRectF(g, b, X(9.1f), Y(5.2f), 4f * s, 1.8f * s);
                            break;
                        case IconKind.Palette: // tema: paleta
                            g.DrawEllipse(p, X(2), Y(2), 12f * s, 12f * s);
                            g.FillEllipse(b, X(5.2f), Y(5.2f), 1.9f * s, 1.9f * s);
                            g.FillEllipse(b, X(9.1f), Y(5.2f), 1.9f * s, 1.9f * s);
                            g.FillEllipse(b, X(6.8f), Y(8.6f), 1.9f * s, 1.9f * s);
                            break;
                        case IconKind.ServiceList: // lista (orden del culto)
                            for (int i = 0; i < 3; i++)
                            {
                                g.FillEllipse(b, X(1.5f), Y(2.5f + i * 4.6f), 2.4f * s, 2.4f * s);
                                g.DrawLine(p, X(6), Y(3.7f + i * 4.6f), X(14), Y(3.7f + i * 4.6f));
                            }
                            break;
                        case IconKind.Export: // hoja + flecha saliente
                            g.DrawRectangle(p, X(1.5f), Y(2), 9f * s, 12f * s);
                            g.DrawLine(p, X(3.5f), Y(5), X(8.5f), Y(5));
                            g.DrawLine(p, X(3.5f), Y(8), X(8.5f), Y(8));
                            g.DrawLine(p, X(8), Y(11.5f), X(14.5f), Y(11.5f));
                            FillTri(g, b, X(11f), Y(8.8f), X(15f), Y(11.5f), X(11f), Y(14.2f));
                            break;
                        case IconKind.Nodes: // integraciones: nodos conectados
                            g.FillEllipse(b, X(1), Y(1), 4.6f * s, 4.6f * s);
                            g.FillEllipse(b, X(10.4f), Y(1), 4.6f * s, 4.6f * s);
                            g.FillEllipse(b, X(5.6f), Y(10.2f), 4.6f * s, 4.6f * s);
                            g.DrawLine(p, X(5.6f), Y(3.3f), X(10.4f), Y(3.3f));
                            g.DrawLine(p, X(5.5f), Y(4.6f), X(7.9f), Y(10.2f));
                            g.DrawLine(p, X(10.5f), Y(4.6f), X(7.9f), Y(10.2f));
                            break;
                        case IconKind.Bolt: // activador: rayo
                            FillPoly(g, b, new PointF[] {
                                new PointF(X(9), Y(0.5f)), new PointF(X(3), Y(8.5f)),
                                new PointF(X(7), Y(8.5f)), new PointF(X(6), Y(15.5f)),
                                new PointF(X(13), Y(6.5f)), new PointF(X(8.2f), Y(6.5f)) });
                            break;
                        case IconKind.Gear: // ajustes: engranaje
                            g.DrawEllipse(p, X(4.2f), Y(4.2f), 7.6f * s, 7.6f * s);
                            g.FillEllipse(b, X(7f), Y(7f), 2f * s, 2f * s);
                            g.DrawLine(p, X(8), Y(0.5f), X(8), Y(3.5f));
                            g.DrawLine(p, X(8), Y(12.5f), X(8), Y(15.5f));
                            g.DrawLine(p, X(0.5f), Y(8), X(3.5f), Y(8));
                            g.DrawLine(p, X(12.5f), Y(8), X(15.5f), Y(8));
                            g.DrawLine(p, X(2.7f), Y(2.7f), X(4.8f), Y(4.8f));
                            g.DrawLine(p, X(11.2f), Y(11.2f), X(13.3f), Y(13.3f));
                            g.DrawLine(p, X(13.3f), Y(2.7f), X(11.2f), Y(4.8f));
                            g.DrawLine(p, X(4.8f), Y(11.2f), X(2.7f), Y(13.3f));
                            break;
                        case IconKind.Search:
                            g.DrawEllipse(p, X(1.5f), Y(1.5f), 9f * s, 9f * s);
                            g.DrawLine(p, X(9.2f), Y(9.2f), X(14.5f), Y(14.5f));
                            break;
                        case IconKind.Plus:
                            g.DrawLine(p, X(8), Y(2.5f), X(8), Y(13.5f));
                            g.DrawLine(p, X(2.5f), Y(8), X(13.5f), Y(8));
                            break;
                        case IconKind.Trash:
                            g.DrawLine(p, X(2.5f), Y(4), X(13.5f), Y(4));
                            g.DrawLine(p, X(6), Y(1.5f), X(10), Y(1.5f));
                            g.DrawLine(p, X(6.5f), Y(1.5f), X(6.5f), Y(4));
                            g.DrawLine(p, X(9.5f), Y(1.5f), X(9.5f), Y(4));
                            g.DrawRectangle(p, X(4), Y(4), 8f * s, 10.5f * s);
                            g.DrawLine(p, X(6.7f), Y(6.5f), X(6.7f), Y(12.5f));
                            g.DrawLine(p, X(9.3f), Y(6.5f), X(9.3f), Y(12.5f));
                            break;
                        case IconKind.ArrowUp:
                            g.DrawLine(p, X(8), Y(13.5f), X(8), Y(2.5f));
                            g.DrawLine(p, X(3.5f), Y(7), X(8), Y(2.5f));
                            g.DrawLine(p, X(12.5f), Y(7), X(8), Y(2.5f));
                            break;
                        case IconKind.ArrowDown:
                            g.DrawLine(p, X(8), Y(2.5f), X(8), Y(13.5f));
                            g.DrawLine(p, X(3.5f), Y(9), X(8), Y(13.5f));
                            g.DrawLine(p, X(12.5f), Y(9), X(8), Y(13.5f));
                            break;
                        case IconKind.Close:
                            g.DrawLine(p, X(3.5f), Y(3.5f), X(12.5f), Y(12.5f));
                            g.DrawLine(p, X(12.5f), Y(3.5f), X(3.5f), Y(12.5f));
                            break;
                        case IconKind.Check:
                            g.DrawLine(p, X(2.5f), Y(8.5f), X(6.5f), Y(12.5f));
                            g.DrawLine(p, X(6.5f), Y(12.5f), X(13.5f), Y(3.5f));
                            break;
                        case IconKind.Warning:
                            g.DrawPolygon(p, new PointF[] {
                                new PointF(X(8), Y(1.5f)), new PointF(X(15), Y(14f)),
                                new PointF(X(1), Y(14f)) });
                            g.DrawLine(p, X(8), Y(5.5f), X(8), Y(9.5f));
                            g.FillEllipse(b, X(7.2f), Y(11.3f), 1.6f * s, 1.6f * s);
                            break;
                        case IconKind.Info:
                            g.DrawEllipse(p, X(1.5f), Y(1.5f), 13f * s, 13f * s);
                            g.FillEllipse(b, X(7.2f), Y(4.2f), 1.6f * s, 1.6f * s);
                            g.DrawLine(p, X(8), Y(7.5f), X(8), Y(12));
                            break;
                        case IconKind.Folder:
                            g.DrawLine(p, X(1.5f), Y(4), X(1.5f), Y(13));
                            g.DrawLine(p, X(1.5f), Y(13), X(14.5f), Y(13));
                            g.DrawLine(p, X(14.5f), Y(13), X(14.5f), Y(4));
                            g.DrawLine(p, X(14.5f), Y(4), X(7), Y(4));
                            g.DrawLine(p, X(7), Y(4), X(5.5f), Y(2));
                            g.DrawLine(p, X(5.5f), Y(2), X(1.5f), Y(2));
                            g.DrawLine(p, X(1.5f), Y(2), X(1.5f), Y(4));
                            break;
                        case IconKind.Image: // marco + sol + montaña
                            g.DrawRectangle(p, X(1.5f), Y(2.5f), 13f * s, 11f * s);
                            g.FillEllipse(b, X(4f), Y(4.8f), 2.2f * s, 2.2f * s);
                            g.DrawLine(p, X(3.5f), Y(11f), X(7f), Y(7.5f));
                            g.DrawLine(p, X(7f), Y(7.5f), X(9.5f), Y(10f));
                            g.DrawLine(p, X(9.5f), Y(10f), X(11.5f), Y(8.5f));
                            g.DrawLine(p, X(11.5f), Y(8.5f), X(14.5f), Y(11f));
                            break;
                    }
                }
            }
            g.SmoothingMode = old;
        }

        private static void FillTri(Graphics g, Brush b, float x1, float y1,
                                    float x2, float y2, float x3, float y3)
        {
            FillPoly(g, b, new PointF[] {
                new PointF(x1, y1), new PointF(x2, y2), new PointF(x3, y3) });
        }

        private static void FillPoly(Graphics g, Brush b, PointF[] pts)
        {
            if (g == null || b == null || pts == null || pts.Length < 3) return;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddPolygon(pts);
                g.FillPath(b, path);
            }
        }

        private static void FillRectF(Graphics g, Brush b, float x, float y, float w, float h)
        {
            if (w <= 0 || h <= 0) return;
            g.FillRectangle(b, x, y, w, h);
        }
    }

    /* ====================================================================== */
    /*  LOGOTIPO «FARO LUMINA»                                                 */
    /* ====================================================================== */

    /// <summary>
    /// Marca de la app: cuadrado redondeado ámbar con degradado vertical +
    /// «faro» oscuro (punto central + destellos). Se pinta en la cabecera y
    /// en los diálogos. Sin recursos de imagen (idéntico Win7→Win11).
    /// </summary>
    internal static class BrandGlyph
    {
        public static void Draw(Graphics g, int x, int y, int size)
        {
            if (g == null || size <= 0) return;
            try
            {
                SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                Rectangle rect = new Rectangle(x, y, size, size);
                int radius = Math.Max(4, size * 10 / 32);
                using (GraphicsPath p = UiTheme.RoundPath(rect, radius))
                using (LinearGradientBrush lg = new LinearGradientBrush(
                    rect, UiTheme.FromHex(0xFFC768), UiTheme.FromHex(0xE0922A), 90f))
                {
                    g.FillPath(lg, p);
                }

                // Faro: punto central + 8 destellos radiales.
                float c = x + size / 2f;
                float d = y + size / 2f;
                float r = size * 0.16f;
                Color dark = UiTheme.FromHex(0x2A1806);
                using (SolidBrush b = new SolidBrush(dark))
                    g.FillEllipse(b, c - r, d - r, r * 2, r * 2);
                using (Pen pen = new Pen(dark, Math.Max(1.5f, size / 14f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        float dirX = (float)Math.Cos(a);
                        float dirY = (float)Math.Sin(a);
                        float r1 = size * 0.30f;
                        float r2 = size * 0.42f;
                        g.DrawLine(pen, c + dirX * r1, d + dirY * r1,
                                        c + dirX * r2, d + dirY * r2);
                    }
                }
                g.SmoothingMode = old;
            }
            catch (Exception) { /* la marca nunca rompe la pintura */ }
        }
    }

    /* ====================================================================== */
    /*  TEMA Y FÁBRICA DE CONTROLES                                            */
    /* ====================================================================== */

    /// <summary>
    /// Paleta y tipografía del tema oscuro «Lumina Studio» v2 (rediseño
    /// v5.3.0). API pública idéntica a la de v5.2.0 (mismos nombres de
    /// campos y firmas de fábrica) para que MainForm.cs compile intacto.
    /// </summary>
    internal static class UiTheme
    {
        // ---- superficie (gradación de profundidad: página < barras < tarjetas) ----
        public static readonly Color PageBg      = FromHex(0x0E1116);
        public static readonly Color BarBg       = FromHex(0x141821);
        public static readonly Color BarBorder   = FromHex(0x232936);
        public static readonly Color CardBg      = FromHex(0x161B23);
        public static readonly Color CardBorder  = FromHex(0x272E3B);
        public static readonly Color CardTop     = FromHex(0x1D2431);   // brillo superior sutil
        // ---- controles ----
        public static readonly Color InputBg     = FromHex(0x0B0E13);
        public static readonly Color InputBgFocus= FromHex(0x131926);
        public static readonly Color InputBorder = FromHex(0x2E3746);
        public static readonly Color InputFocus  = FromHex(0xF0A93B);
        // ---- acentos ----
        public static readonly Color Accent      = FromHex(0xF0A93B); // ámbar Lumina
        public static readonly Color AccentHover = FromHex(0xFFC15E);
        public static readonly Color AccentDim   = FromHex(0xB9832F);
        public static readonly Color OnAccent    = FromHex(0x201405);
        public static readonly Color Danger      = FromHex(0xE5484D);
        public static readonly Color DangerHover = FromHex(0xF26D70);
        public static readonly Color Ok          = FromHex(0x4CC38A);
        public static readonly Color Err         = FromHex(0xE5484D);
        public static readonly Color Info        = FromHex(0x6AA9FF);
        // ---- texto ----
        public static readonly Color TextPrimary   = FromHex(0xEDEFF3);
        public static readonly Color TextSecondary = FromHex(0x9AA3AF);
        public static readonly Color TextDisabled  = FromHex(0x59626F);
        // ---- filas ----
        public static readonly Color RowOdd      = FromHex(0x12161E);
        public static readonly Color RowEven     = FromHex(0x161B23);
        public static readonly Color RowSelected = FromHex(0x2B3342);
        public static readonly Color RowHover    = FromHex(0x212834);
        public static readonly Color HoverLayer  = FromHex(0x1E2430);
        public static readonly Color PreviewBg   = Color.Black;
        public static readonly Color Warning     = FromHex(0xE0B052);

        // ---- radios (px lógicos) ----
        public const int CardRadius = 10;
        public const int ButtonRadius = 7;
        public const int PillRadius = 11;

        // ---- tipografía (96 dpi lógico; Segoe UI: de fábrica en Win7→Win11) ----
        public static readonly Font H1        = new Font("Segoe UI", 15.5F, FontStyle.Bold);
        public static readonly Font H2        = new Font("Segoe UI", 10F, FontStyle.Bold);
        public static readonly Font Header    = new Font("Segoe UI", 13.5F, FontStyle.Bold);
        public static readonly Font Body      = new Font("Segoe UI", 9.25F);
        public static readonly Font Small     = new Font("Segoe UI", 8.25F);
        public static readonly Font SmallBold = new Font("Segoe UI", 8.25F, FontStyle.Bold);
        public static readonly Font Mono      = new Font("Consolas", 9.75F);

        public static Color FromHex(int rgb)
        {
            return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }

        /// <summary>Mezcla <paramref name="color"/> sobre un fondo oscuro con alpha.</summary>
        public static Color Tint(Color color, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color);
        }

        /// <summary>GraphicsPath de rectángulo redondeado (para botones/tarjetas).</summary>
        public static GraphicsPath RoundPath(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) { p.AddRectangle(new Rectangle(0, 0, 1, 1)); return p; }
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /* --------------------------------------------------- fábrica rápida */

        public static Label MkLabel(string text, Color color, Font font, bool auto)
        {
            Label l = new Label();
            l.Text = text ?? string.Empty;
            l.ForeColor = color;
            l.Font = font;
            l.BackColor = Color.Transparent;
            l.AutoSize = auto;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Margin = new Padding(0);
            return l;
        }

        /// <summary>
        /// Botón plano redondeado del tema. variant:
        /// "primary" | "secondary" | "danger" | "ghost". El ancho se ajusta al
        /// contenido (mín. 84 px); quien lo acopla con Dock ignora el ancho.
        /// </summary>
        public static Button MkButton(string text, string variant, EventHandler onClick)
        {
            LuminaButton b = new LuminaButton();
            b.Text = text ?? string.Empty;
            b.Variant = variant;
            if (onClick != null) b.Click += onClick;
            b.FitSize();
            return b;
        }

        /// <summary>Botón con icono + texto.</summary>
        public static Button MkButtonWithIcon(string text, IconKind icon,
                                              string variant, EventHandler onClick)
        {
            LuminaButton b = new LuminaButton();
            b.Text = text ?? string.Empty;
            b.Icon = icon;
            b.Variant = variant;
            if (onClick != null) b.Click += onClick;
            b.FitSize();
            return b;
        }

        /// <summary>Botón SOLO icono (38×30) — para barras de transporte.</summary>
        public static Button MkIconButton(IconKind icon, string variant, EventHandler onClick)
        {
            LuminaButton b = new LuminaButton();
            b.Icon = icon;
            b.Variant = variant;
            if (onClick != null) b.Click += onClick;
            b.Width = 38;
            return b;
        }

        /// <summary>TextBox oscuro con foco visible (fondo ligeramente más claro).</summary>
        public static TextBox MkInput(bool multiline)
        {
            TextBox t = new TextBox();
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = InputBg;
            t.ForeColor = TextPrimary;
            t.Font = Body;
            t.Margin = new Padding(0);
            if (multiline)
            {
                t.Multiline = true;
                t.ScrollBars = ScrollBars.Vertical;
                t.AcceptsReturn = true;
            }
            t.GotFocus += delegate { t.BackColor = InputBgFocus; };
            t.LostFocus += delegate { t.BackColor = InputBg; };
            return t;
        }

        /// <summary>Marca de agua (cue banner) para cajas de búsqueda. Best-effort.</summary>
        public static void SetCueBanner(TextBox t, string text)
        {
            if (t == null) return;
            try
            {
                IntPtr h = t.Handle;   // fuerza la creación del identificador
                if (h != IntPtr.Zero)
                    SendMessageW(h, 0x1501 /*EM_SETCUEBANNER*/, (IntPtr)1, text ?? string.Empty);
            }
            catch (Exception) { /* sin marca de agua si la API no existe */ }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern IntPtr SendMessageW(IntPtr hWnd, int msg, IntPtr wp, string lp);

        public static NumericUpDown MkNumeric(decimal min, decimal max, decimal value)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min; n.Maximum = max; n.Value = value;
            n.BorderStyle = BorderStyle.FixedSingle;
            n.BackColor = InputBg;
            n.ForeColor = TextPrimary;
            n.Font = Body;
            n.Margin = new Padding(0);
            return n;
        }

        /// <summary>Tarjeta redondeada con título (v2: icono opcional + brillo superior).</summary>
        public static Panel MkCard(string title, out ContentPanel body)
        {
            return MkCard(title, IconKind.None, out body);
        }

        public static Panel MkCard(string title, IconKind icon, out ContentPanel body)
        {
            LuminaCard card = new LuminaCard();
            card.Padding = new Padding(16, 14, 16, 16);
            card.Margin = new Padding(0);

            CardHeader head = new CardHeader(title, icon);
            head.Dock = DockStyle.Top;
            head.Height = 28;

            body = new ContentPanel();
            body.BackColor = CardBg;
            body.Dock = DockStyle.Fill;

            card.Controls.Add(body);
            card.Controls.Add(head);
            return card;
        }

        /// <summary>Casilla oscura coherente con el tema (tamaño ajustado al texto).</summary>
        public static LuminaCheck MkCheck(string text)
        {
            LuminaCheck c = new LuminaCheck();
            c.Text = text ?? string.Empty;
            c.FitSize();
            return c;
        }
    }

    /// <summary>Panel sin parpadeo (doble búfer) para interiores de tarjeta.</summary>
    internal class ContentPanel : Panel
    {
        public ContentPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    /* ====================================================================== */
    /*  CHIPS DE ESTADO (píldora)                                              */
    /* ====================================================================== */

    /// <summary>
    /// Chip píldora: punto de color + texto (estado del sistema). Mantiene la
    /// API de v5.2.0 (ctor de 3 argumentos + SetState) que verifica --uicheck.
    /// </summary>
    internal sealed class Chip : Control
    {
        private Color _dot;
        private string _text;
        private readonly bool _boxed;

        public Chip(string text, Color dot, bool boxed)
        {
            // FIX v5.1.1 (se mantiene): SupportsTransparentBackColor ANTES de
            // asignar BackColor = Transparent, o el ctor lanza ArgumentException.
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            _text = text ?? string.Empty;
            _dot = dot;
            _boxed = boxed;
            BackColor = Color.Transparent;
            Font = UiTheme.Small;
            Height = 22;
            Cursor = Cursors.Default;
            UpdateWidth();
        }

        public void SetState(string text, Color dot)
        {
            _text = text ?? string.Empty;
            _dot = dot;
            UpdateWidth();
            Invalidate();
        }

        private void UpdateWidth()
        {
            int textW;
            try { textW = TextRenderer.MeasureText(_text, UiTheme.Small).Width; }
            catch (Exception) { textW = 40; }
            Width = 10 + 8 + 7 + textW + 12 + (_boxed ? 2 : 0); // pad + dot + gap + text + pad
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            if (_boxed)
            {
                // Píldora con fondo de tarjeta y borde.
                using (GraphicsPath p = UiTheme.RoundPath(r, UiTheme.PillRadius))
                {
                    using (SolidBrush bg = new SolidBrush(UiTheme.CardBg)) g.FillPath(bg, p);
                    using (Pen pen = new Pen(UiTheme.CardBorder)) g.DrawPath(pen, p);
                }
            }
            else
            {
                // Defensa en profundidad: nunca depender de la simulación de
                // transparencia de WinForms — se pinta el fondo sólido real
                // del contenedor padre (o PageBg si no hay padre).
                Color bg = (Parent != null && Parent.BackColor.A == 255)
                           ? Parent.BackColor : UiTheme.PageBg;
                using (SolidBrush fill = new SolidBrush(bg)) g.FillRectangle(fill, r);
            }
            int cy = Height / 2;
            using (SolidBrush dot = new SolidBrush(_dot))
                g.FillEllipse(dot, 11, cy - 4, 8, 8);
            TextRenderer.DrawText(g, _text, UiTheme.Small,
                new Rectangle(26, 0, Width - 26, Height),
                UiTheme.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            Invalidate();
        }
    }

    /* ====================================================================== */
    /*  TARJETA REDONDEADA                                                     */
    /* ====================================================================== */

    /// <summary>
    /// Tarjeta del tema: fondo redondeado anti-alias, borde y brillo superior.
    /// El fondo propio se pinta con el color de la PÁGINA (para que las
    /// esquinas recortadas se fundan con el padre) y el cuerpo redondeado
    /// encima — técnica a prueba de Win7, sin transparencia simulada.
    /// </summary>
    internal sealed class LuminaCard : ContentPanel
    {
        public LuminaCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = UiTheme.PageBg;    // esquinas = fondo de la página
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = UiTheme.RoundPath(r, UiTheme.CardRadius))
            {
                using (SolidBrush bg = new SolidBrush(UiTheme.CardBg)) g.FillPath(bg, p);
                using (Pen pen = new Pen(UiTheme.CardBorder)) g.DrawPath(pen, p);
            }
            // Brillo superior (línea interior 1 px, sutileza de profundidad).
            int x1 = UiTheme.CardRadius + 4;
            if (Width - 2 * x1 > 8)
                using (Pen hl = new Pen(Color.FromArgb(16, 255, 255, 255)))
                    g.DrawLine(hl, x1, 1, Width - x1 - 1, 1);
            base.OnPaint(e);
        }
    }

    /// <summary>Fila de cabecera de tarjeta: icono ámbar + título H2.</summary>
    internal sealed class CardHeader : ContentPanel
    {
        private readonly string _title;
        private readonly IconKind _icon;

        public CardHeader(string title, IconKind icon)
        {
            _title = title ?? string.Empty;
            _icon = icon;
            BackColor = UiTheme.CardBg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cy = Height / 2;
            if (_icon != IconKind.None)
            {
                IconPainter.Draw(g, _icon, 2, cy - 8, 16, UiTheme.Accent);
                TextRenderer.DrawText(g, _title, UiTheme.H2,
                    new Rectangle(26, 0, Width - 26, Height), UiTheme.TextPrimary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            else
            {
                TextRenderer.DrawText(g, _title, UiTheme.H2,
                    new Rectangle(2, 0, Width - 2, Height), UiTheme.TextPrimary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            base.OnPaint(e);
        }
    }

    /* ====================================================================== */
    /*  BOTÓN DEL TEMA                                                         */
    /* ====================================================================== */

    /// <summary>
    /// Botón plano redondeado, 100 % owner-drawn. Variantes:
    ///   primary   — ámbar sólido (acción principal)
    ///   secondary — superficie oscura con borde
    ///   danger    — rojo (con estado <see cref="Active"/> para toggles: Negro)
    ///   ghost     — sin fondo (acciones de baja prominencia)
    /// Estados pintados: normal, hover, pulsado, deshabilitado y foco visible
    /// (anillo interior cuando el teclado lo enfoca). Compatible con todas
    /// las propiedades de Button (Click, Dock, Enabled, DialogResult…).
    /// </summary>
    internal sealed class LuminaButton : Button
    {
        private string _variant = "secondary";
        private IconKind _icon = IconKind.None;
        private bool _hover;
        private bool _pressed;
        private bool _active;

        public LuminaButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            Height = 30;
            Padding = new Padding(12, 0, 12, 0);
            Margin = new Padding(0, 0, 8, 0);
            TextAlign = ContentAlignment.MiddleCenter;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            BackColor = UiTheme.CardBg;
        }

        /// <summary>primary | secondary | danger | ghost.</summary>
        public string Variant
        {
            get { return _variant; }
            set { _variant = value ?? "secondary"; Invalidate(); }
        }

        /// <summary>Icono a la izquierda del texto (o centrado si no hay texto).</summary>
        public IconKind Icon
        {
            get { return _icon; }
            set { _icon = value; Invalidate(); }
        }

        /// <summary>Estado «encendido» para botones conmutables (p. ej. Negro).</summary>
        public bool Active
        {
            get { return _active; }
            set { if (_active != value) { _active = value; Invalidate(); } }
        }

        /// <summary>Ajusta el tamaño al contenido (icono + texto), altura 30.</summary>
        public void FitSize()
        {
            int textW = 0;
            string t = Text ?? string.Empty;
            if (t.Length > 0)
            {
                // Se mide con la fuente DEL TEMA (el botón aún no tiene padre y
                // su Font es la del sistema, más estrecha — quedaría corto).
                try { textW = TextRenderer.MeasureText(t, UiTheme.Body).Width; }
                catch (Exception) { textW = t.Length * 8; }
            }
            int iconW = _icon == IconKind.None || t.Length == 0 ? 0 : 16 + 5;
            Width = Math.Max(38, 14 + iconW + textW + 14);
            Height = 30;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _pressed = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (r.Width <= 2 || r.Height <= 2) { base.OnPaint(e); return; }

            Color bg, fg;
            Color border = Color.Empty;

            bool disabled = !Enabled;
            switch (_variant)
            {
                case "primary":
                    bg = disabled ? UiTheme.TextDisabled
                        : _pressed ? UiTheme.AccentDim
                        : _hover ? UiTheme.AccentHover
                        : UiTheme.Accent;
                    fg = disabled ? UiTheme.PageBg : UiTheme.OnAccent;
                    if (_active) { bg = UiTheme.Accent; fg = UiTheme.OnAccent; }
                    break;
                case "danger":
                    if (_active)
                    {
                        bg = _hover ? UiTheme.DangerHover : UiTheme.Danger;
                        fg = Color.White;
                    }
                    else
                    {
                        bg = _hover ? UiTheme.Tint(UiTheme.Danger, 34) : UiTheme.RowHover;
                        fg = disabled ? UiTheme.TextDisabled : UiTheme.Danger;
                        border = UiTheme.Tint(UiTheme.Danger, disabled ? 40 : 96);
                    }
                    break;
                case "ghost":
                    bg = _hover ? UiTheme.RowHover : UiTheme.CardBg;
                    fg = disabled ? UiTheme.TextDisabled
                        : _hover ? UiTheme.TextPrimary : UiTheme.TextSecondary;
                    break;
                default: // secondary
                    bg = _active ? UiTheme.RowSelected
                        : _pressed ? UiTheme.RowSelected
                        : _hover ? UiTheme.RowHover
                        : UiTheme.CardBg;
                    fg = disabled ? UiTheme.TextDisabled : UiTheme.TextPrimary;
                    border = UiTheme.CardBorder;
                    break;
            }

            int radius = Math.Min(UiTheme.ButtonRadius, Height / 2);
            using (GraphicsPath p = UiTheme.RoundPath(r, radius))
            {
                using (SolidBrush fill = new SolidBrush(bg)) g.FillPath(fill, p);
                if (border != Color.Empty)
                    using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }

            // Foco visible por teclado (anillo interior sutil).
            if (Focused && ShowFocusCues)
                using (GraphicsPath p = UiTheme.RoundPath(
                    new Rectangle(2, 2, Width - 5, Height - 5), Math.Max(3, radius - 2)))
                using (Pen pen = new Pen(_variant == "primary" ? UiTheme.AccentDim : UiTheme.Accent, 1.4f))
                    g.DrawPath(pen, p);

            // Contenido: icono + texto centrados como bloque.
            int dy = _pressed ? 1 : 0;
            string text = Text ?? string.Empty;
            int textW = 0, textH = 0;
            if (text.Length > 0)
            {
                try
                {
                    Size tsz = TextRenderer.MeasureText(text, Font);
                    textW = tsz.Width; textH = tsz.Height;
                }
                catch (Exception) { textW = text.Length * 7; textH = 16; }
            }
            const int iconSide = 16;
            bool hasIcon = _icon != IconKind.None;
            int gap = hasIcon && textW > 0 ? 5 : 0;
            int block = (hasIcon ? iconSide : 0) + gap + textW;
            int x = (Width - block) / 2;
            if (x < 4) x = 4;
            if (hasIcon)
                IconPainter.Draw(g, _icon, x, (Height - iconSide) / 2 + dy, iconSide, fg);
            if (textW > 0)
                TextRenderer.DrawText(g, text, Font,
                    new Rectangle(x + iconSide + gap - 2, 0, textW + 6, Height),
                    fg, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            base.OnPaint(e);
        }
    }

    /* ====================================================================== */
    /*  CASILLA DEL TEMA                                                       */
    /* ====================================================================== */

    /// <summary>
    /// Casilla oscura del tema: cuadrado redondeado (ámbar al marcar) + texto.
    /// Navegable por Tab, conmutable con Espacio y con foco visible.
    /// </summary>
    internal sealed class LuminaCheck : Control
    {
        private bool _checked;
        private bool _hover;

        /// <summary>Se dispara al cambiar <see cref="Checked"/>.</summary>
        public event EventHandler CheckedChanged;

        public LuminaCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 24;
            TabStop = true;
            Cursor = Cursors.Hand;
            Font = UiTheme.Body;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                EventHandler h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        /// <summary>Ajusta el tamaño al texto (alto fijo 24).</summary>
        public void FitSize()
        {
            int textW;
            string t = Text ?? string.Empty;
            try { textW = TextRenderer.MeasureText(t, UiTheme.Body).Width; }
            catch (Exception) { textW = t.Length * 7; }
            Width = 25 + textW + 2;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            FitSize();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;   // el clic conmuta ANTES de notificar Click
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Checked = !Checked;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Defensa en profundidad: fondo sólido del padre (nunca la
            // simulación de transparencia de WinForms para pintar).
            Color parentBg = UiTheme.CardBg;
            if (Parent != null && Parent.BackColor.A == 255) parentBg = Parent.BackColor;
            using (SolidBrush erase = new SolidBrush(parentBg))
                g.FillRectangle(erase, ClientRectangle);

            int side = 16;
            int by = (Height - side) / 2;
            Rectangle box = new Rectangle(0, by, side, side);
            using (GraphicsPath p = UiTheme.RoundPath(box, 4))
            {
                if (_checked)
                {
                    using (SolidBrush fill = new SolidBrush(Enabled ? UiTheme.Accent : UiTheme.TextDisabled))
                        g.FillPath(fill, p);
                }
                else
                {
                    using (SolidBrush fill = new SolidBrush(UiTheme.InputBg))
                        g.FillPath(fill, p);
                    using (Pen pen = new Pen(Enabled
                        ? (_hover ? UiTheme.InputFocus : UiTheme.InputBorder)
                        : UiTheme.TextDisabled))
                        g.DrawPath(pen, p);
                }
            }
            if (_checked)
            {
                // Marca ✓ oscura sobre el ámbar.
                using (Pen pen = new Pen(UiTheme.OnAccent, 1.9f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    g.DrawLine(pen, box.X + 3.5f, box.Y + 8.5f, box.X + 6.5f, box.Y + 11.5f);
                    g.DrawLine(pen, box.X + 6.5f, box.Y + 11.5f, box.X + 12.5f, box.Y + 4.5f);
                }
            }

            string t = Text ?? string.Empty;
            if (t.Length > 0)
                TextRenderer.DrawText(g, t, UiTheme.Body,
                    new Rectangle(25, 0, Math.Max(10, Width - 25), Height),
                    Enabled ? UiTheme.TextPrimary : UiTheme.TextDisabled,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            if (Focused && ShowFocusCues)
                using (Pen pen = new Pen(UiTheme.AccentDim, 1.2f) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, 1, 1, Width - 2, Height - 2);
            base.OnPaint(e);
        }
    }

    /* ====================================================================== */
    /*  NAVEGACIÓN LATERAL (agrupada por secciones)                            */
    /* ====================================================================== */

    /// <summary>
    /// Navegación lateral owner-drawn v2: ítems agrupados en secciones
    /// (PRESENTACIÓN · BIBLIOTECA · EXTENSIÓN · SISTEMA), ítem activo con
    /// píldora redondeada + barra ámbar, iconos vectoriales y hover suave.
    /// API compatible con v5.2.0 (ctor sin argumentos, SetActive,
    /// ClickedIndex, NavActivated).
    /// </summary>
    internal sealed class NavPanel : ContentPanel
    {
        private static readonly string[] GroupNames =
            { "PRESENTACIÓN", "BIBLIOTECA", "EXTENSIÓN", "SISTEMA" };
        private static readonly int[] GroupFirst = { 0, 2, 5, 8 };
        private static readonly int[] GroupCount = { 2, 3, 3, 1 };

        /// <summary>Ítems en ORDEN DE NAVEGACIÓN (MainForm mapea a páginas).</summary>
        private static readonly string[] Captions =
            { "En Vivo", "Culto", "Canciones", "Biblia", "Temas",
              "Exportar", "Integraciones", "Activadores", "Ajustes" };

        private static readonly IconKind[] Icons =
            { IconKind.Live, IconKind.ServiceList, IconKind.Song, IconKind.Book, IconKind.Palette,
              IconKind.Export, IconKind.Nodes, IconKind.Bolt, IconKind.Gear };

        private const int ItemCount = 9;
        private const int GroupCountTotal = 4;
        private const int ItemH = 36;
        private const int GroupH = 24;
        private const int TopPad = 14;

        private readonly Rectangle[] _itemRects = new Rectangle[ItemCount];
        private readonly Rectangle[] _groupRects = new Rectangle[GroupCountTotal];
        private int _hover = -1;
        private int _active;

        /// <summary>Índice del ítem clicado (válido al dispararse NavActivated).</summary>
        public int ClickedIndex { get; private set; }

        /// <summary>Se dispara al elegir un ítem: leer ClickedIndex.</summary>
        public event EventHandler NavActivated;

        public NavPanel()
        {
            BackColor = UiTheme.BarBg;
            Width = 224;
            int y = TopPad;
            int item = 0;
            for (int grp = 0; grp < GroupCountTotal; grp++)
            {
                _groupRects[grp] = new Rectangle(0, y, Width, GroupH);
                y += GroupH;
                for (int k = 0; k < GroupCount[grp] && item < ItemCount; k++, item++)
                {
                    _itemRects[item] = new Rectangle(0, y, Width, ItemH);
                    y += ItemH;
                }
            }
        }

        public void SetActive(int index)
        {
            if (index < 0 || index >= ItemCount) index = 0;
            _active = index;
            Invalidate();
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < ItemCount; i++)
                if (_itemRects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = HitTest(e.Location);
            if (i >= 0)
            {
                ClickedIndex = i;
                _active = i;
                Invalidate();
                EventHandler h = NavActivated;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            for (int grp = 0; grp < GroupCountTotal; grp++)
            {
                Rectangle gr = _groupRects[grp];
                TextRenderer.DrawText(g, GroupNames[grp], UiTheme.SmallBold,
                    new Rectangle(24, gr.Y, gr.Width - 24, gr.Height),
                    UiTheme.TextDisabled,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            for (int i = 0; i < ItemCount; i++)
            {
                Rectangle r = _itemRects[i];
                bool isActive = i == _active;
                bool isHover = i == _hover && !isActive;

                // Píldora del ítem (inset horizontal 10 px).
                Rectangle pill = new Rectangle(10, r.Y + 3, Width - 20, r.Height - 6);
                if (isActive || isHover)
                    using (GraphicsPath p = UiTheme.RoundPath(pill, 8))
                    using (SolidBrush bg = new SolidBrush(
                        isActive ? UiTheme.RowSelected : UiTheme.HoverLayer))
                        g.FillPath(bg, p);

                if (isActive)
                    using (SolidBrush bar = new SolidBrush(UiTheme.Accent))
                        g.FillRectangle(bar, 10, r.Y + 8, 3, r.Height - 16);

                Color iconC = isActive ? UiTheme.TextPrimary
                    : isHover ? UiTheme.TextPrimary : UiTheme.TextSecondary;
                IconPainter.Draw(g, Icons[i], 26, r.Y + (r.Height - 17) / 2, 17, iconC);

                TextRenderer.DrawText(g, Captions[i], UiTheme.Body,
                    new Rectangle(54, r.Y, r.Width - 54, r.Height),
                    isActive ? Color.White : UiTheme.TextSecondary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            // Borde derecho de la barra.
            using (Pen p = new Pen(UiTheme.BarBorder))
                g.DrawLine(p, Width - 1, 0, Width - 1, Height);
        }
    }
}
