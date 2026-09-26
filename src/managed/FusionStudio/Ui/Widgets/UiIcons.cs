// ============================================================================
//  Fusion-HP · Ui/Widgets/UiIcons.cs — iconos Tabler (trazo 1.7) de la GUI.
//  Los PNG viajan con el programa en resources/img/icons (ink20/ink32/white20/
//  accent20). Tinta accent = estado activo (.ppt-iconbtn-on de la referencia
//  web). Cache global: se cargan una vez y se reutilizan sin bloquear archivos.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace Fusion.Studio.Ui.Widgets
{
    public enum IconTint { Ink, White, Accent, InkFaded }

    public static class UiIcons
    {
        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();
        static string root;

        /// <summary>Carpeta de iconos junto al ejecutable (resources/img/icons).</summary>
        public static string Root
        {
            get
            {
                if (root == null)
                {
                    try
                    {
                        root = Path.Combine(
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources"),
                            Path.Combine("img", "icons"));
                    }
                    catch { root = ""; }
                }
                return root;
            }
        }

        public static bool Available { get { return Directory.Exists(Root); } }

        /// <summary>Icono 20 px en la tinta pedida (null si no existe: la UI
        /// degrada a texto plano, nunca falla por un asset).
        /// v4.2.0 (C8): InkFaded = tinta normal al 35 % — en superficies blancas
        /// el blanco puro desaparecía (buscador/chips deshabilitados sin icono).</summary>
        public static Image Get(string name, IconTint tint)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string set = tint == IconTint.White ? "white20"
                       : tint == IconTint.Accent ? "accent20" : "ink20";
            string key = set + "/" + name + (tint == IconTint.InkFaded ? "@faded" : "");
            Image img;
            if (cache.TryGetValue(key, out img)) return img;
            try
            {
                string p = Path.Combine(Path.Combine(Root, set), name + ".png");
                if (!File.Exists(p)) return null;
                using (Image src = Image.FromFile(p))
                {
                    Bitmap copy = new Bitmap(src);   // copia: no bloquea el PNG
                    if (tint == IconTint.InkFaded)
                    {
                        // mismo tratamiento que FusionIconButton.DrawIcon: alfa 35 %
                        var tmp = new Bitmap(copy.Width, copy.Height);
                        using (var attrs = new System.Drawing.Imaging.ImageAttributes())
                        {
                            attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix
                            {
                                Matrix33 = 0.35f                        // canal alfa × 0.35
                            });
                            using (var g2 = Graphics.FromImage(tmp))
                                g2.DrawImage(copy, new Rectangle(0, 0, copy.Width, copy.Height),
                                             0, 0, copy.Width, copy.Height,
                                             GraphicsUnit.Pixel, attrs);
                        }
                        copy.Dispose();
                        copy = tmp;
                    }
                    cache[key] = copy;
                    return copy;
                }
            }
            catch { return null; }
        }

        /// <summary>Icono 32 px tinta ink (mosaicos de Inicio, diálogos).</summary>
        public static Image Get32(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string key = "ink32/" + name;
            Image img;
            if (cache.TryGetValue(key, out img)) return img;
            try
            {
                string p = Path.Combine(Path.Combine(Root, "ink32"), name + ".png");
                if (!File.Exists(p)) return null;
                using (Image src = Image.FromFile(p))
                {
                    Bitmap copy = new Bitmap(src);
                    cache[key] = copy;
                    return copy;
                }
            }
            catch { return null; }
        }

        public static void Draw(Graphics g, string name, IconTint tint, int x, int y)
        {
            Image i = Get(name, tint);
            if (i != null) g.DrawImage(i, x, y, i.Width, i.Height);
        }

        public static void Draw(Graphics g, Image icon, int x, int y)
        {
            if (icon != null) g.DrawImage(icon, x, y, icon.Width, icon.Height);
        }

        /// <summary>Dibuja el icono centrado en un rectángulo (listas, botones).</summary>
        public static void DrawCentered(Graphics g, string name, IconTint tint, Rectangle bounds)
        {
            Image i = Get(name, tint);
            if (i == null) return;
            int x = bounds.X + (bounds.Width - i.Width) / 2;
            int y = bounds.Y + (bounds.Height - i.Height) / 2;
            g.DrawImage(i, x, y, i.Width, i.Height);
        }
    }
}
