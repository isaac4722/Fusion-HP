// ============================================================================
//  Fusion-HP · FusionShared/Media/FontVault.cs — fuentes de la GUI incrustadas
//  (petición del usuario: los assets de la web VAN con el programa). Las
//  familias Outfit, Cormorant Garamond y Libre Baskerville viajan en
//  resources/fonts y se cargan UNA vez vía PrivateFontCollection: así la
//  vista previa C#, el editor WPF y el núcleo C++ (que también las carga)
//  renderizan igual que la referencia web SIN instalar nada en el sistema
//  (cero dependencias, cero Registro [SPEC §11.4]).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;

namespace Fusion.Shared.Media
{
    public static class FontVault
    {
        static PrivateFontCollection pfc;
        static readonly List<string> families = new List<string>();
        static bool tried;

        public const string Outfit = "Outfit";
        public const string OutfitSemiBold = "Outfit SemiBold";
        public const string Cormorant = "Cormorant Garamond";
        public const string LibreBaskerville = "Libre Baskerville";

        /// <summary>Carga resources/fonts/*.ttf una sola vez (idempotente).</summary>
        public static void EnsureLoaded()
        {
            if (tried) return;
            tried = true;
            try
            {
                string dir = Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources"), "fonts");
                if (!Directory.Exists(dir)) return;
                pfc = new PrivateFontCollection();
                foreach (string f in Directory.GetFiles(dir, "*.ttf"))
                {
                    try
                    {
                        pfc.AddFontFile(f);
                    }
                    catch { /* archivo de fuente dañado: se omite sin romper la app */ }
                }
                families.Clear();
                if (pfc != null)
                    foreach (FontFamily ff in pfc.Families)
                        families.Add(ff.Name);
            }
            catch
            {
                pfc = null;
            }
        }

        /// <summary>Familias incrustadas disponibles (para selectores de fuente).</summary>
        public static IList<string> LoadedFamilies()
        {
            EnsureLoaded();
            return families.AsReadOnly();
        }

        /// <summary>
        /// Crea una Font resolviendo la familia incrustada si existe; si no,
        /// cae a la fuente del sistema con el mismo nombre. Nunca falla.
        /// </summary>
        public static Font Create(string familyName, float size, FontStyle style)
        {
            EnsureLoaded();
            if (pfc != null && !string.IsNullOrEmpty(familyName))
            {
                try
                {
                    foreach (FontFamily ff in pfc.Families)
                    {
                        if (string.Compare(ff.Name, familyName, StringComparison.OrdinalIgnoreCase) != 0) continue;
                        FontStyle want = style;
                        if (!ff.IsStyleAvailable(want))
                        {
                            // degradación elegante: negrita→regular, cursiva→regular
                            if ((want & FontStyle.Bold) != 0 && !ff.IsStyleAvailable(want & ~FontStyle.Bold))
                                want &= ~FontStyle.Bold;
                            if ((want & FontStyle.Italic) != 0 && !ff.IsStyleAvailable(want & ~FontStyle.Italic))
                                want &= ~FontStyle.Italic;
                        }
                        return new Font(ff, size, want, GraphicsUnit.Point);
                    }
                }
                catch { }
            }
            try { return new Font(familyName ?? "Segoe UI", size, style, GraphicsUnit.Point); }
            catch { return new Font("Segoe UI", size, style, GraphicsUnit.Point); }
        }

        public static Font Create(string familyName, float size)
        {
            return Create(familyName, size, FontStyle.Regular);
        }

        /// <summary>Nombre de familia presentable con respaldo (para UI).</summary>
        public static string FamilyOrFallback(string familyName)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(familyName)) return "Segoe UI";
            foreach (string f in families)
                if (string.Compare(f, familyName, StringComparison.OrdinalIgnoreCase) == 0) return f;
            return familyName;
        }
    }
}
