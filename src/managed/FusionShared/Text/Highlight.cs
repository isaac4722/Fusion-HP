// ============================================================================
//  Fusion-HP · FusionShared/Text/Highlight.cs — resaltado de palabras para la
//  PROYECCIÓN y la vista previa (función única heredada de las betas 1, port
//  de native/core/src/Highlight.{h,cpp} «llevar el resaltado de la búsqueda a
//  pantalla»). Parte una línea en segmentos marcando las ocurrencias de las
//  palabras buscadas con coincidencia INSENSIBLE a mayúsculas y a acentos
//  latinos (á→a, É→e, ü→u, ñ→n…) y con frontera de palabra: no marca «Dios»
//  dentro de «Diosas». El texto de cada segmento se conserva VERBATIM.
//  En .NET los caracteres precompuestos son un solo char: el plegado conserva
//  los índices y no hacen falta offsets de byte como en el port UTF-8.
// ============================================================================
using System;
using System.Collections.Generic;

namespace Fusion.Shared.Text
{
    public struct HlSegment
    {
        public string Text;
        public bool Match;
        public HlSegment(string text, bool match) { Text = text; Match = match; }
    }

    public static class Highlight
    {
        /// <summary>Plegado Latin-1 básico (cubre el español sin ICU ni locale).</summary>
        static char Fold(char c)
        {
            if (c >= 'A' && c <= 'Z') return (char)(c - 'A' + 'a');
            switch (c)
            {
                case 'á': case 'à': case 'â': case 'ã': case 'ä': case 'å': return 'a';
                case 'Á': case 'À': case 'Â': case 'Ã': case 'Ä': case 'Å': return 'a';
                case 'ç': case 'Ç': return 'c';
                case 'é': case 'è': case 'ê': case 'ë': return 'e';
                case 'É': case 'È': case 'Ê': case 'Ë': return 'e';
                case 'í': case 'ì': case 'î': case 'ï': return 'i';
                case 'Í': case 'Ì': case 'Î': case 'Ï': return 'i';
                case 'ñ': case 'Ñ': return 'n';
                case 'ó': case 'ò': case 'ô': case 'õ': case 'ö': return 'o';
                case 'Ó': case 'Ò': case 'Ô': case 'Õ': case 'Ö': return 'o';
                case 'ú': case 'ù': case 'û': case 'ü': return 'u';
                case 'Ú': case 'Ù': case 'Û': case 'Ü': return 'u';
                case 'ý': case 'ÿ': case 'Ý': return 'y';
                default: return c;
            }
        }

        static string FoldWord(string s)
        {
            var chars = new char[s.Length];
            for (int i = 0; i < s.Length; i++) chars[i] = Fold(s[i]);
            return new string(chars);
        }

        static bool IsWordChar(char c)
        {
            c = Fold(c);
            return (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
        }

        /// <summary>
        /// Parte <paramref name="text"/> en segmentos marcando las ocurrencias de
        /// <paramref name="words"/> (frontera de palabra, sin distinción de
        /// mayúsculas ni acentos). Palabras vacías se ignoran.
        /// </summary>
        public static List<HlSegment> Split(string text, ICollection<string> words)
        {
            var outSegs = new List<HlSegment>();
            if (text == null) text = "";
            if (words == null || words.Count == 0)
            {
                outSegs.Add(new HlSegment(text, false));
                return outSegs;
            }
            // Plegar palabras válidas
            var folded = new List<string>();
            foreach (string w in words)
            {
                if (string.IsNullOrEmpty(w)) continue;
                string fw = FoldWord(w.Trim());
                if (fw.Length > 0) folded.Add(fw);
            }
            if (folded.Count == 0)
            {
                outSegs.Add(new HlSegment(text, false));
                return outSegs;
            }

            char[] fold = new char[text.Length];
            for (int i = 0; i < text.Length; i++) fold[i] = Fold(text[i]);

            var marks = new bool[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0 && IsWordChar(text[i - 1])) continue;   // frontera izquierda
                foreach (string fw in folded)
                {
                    if (i + fw.Length > text.Length) continue;
                    bool hit = true;
                    for (int k = 0; k < fw.Length; k++)
                        if (fold[i + k] != fw[k]) { hit = false; break; }
                    if (!hit) continue;
                    int end = i + fw.Length;
                    if (end < text.Length && IsWordChar(text[end])) continue;   // frontera derecha
                    for (int k = i; k < end; k++) marks[k] = true;
                    break;
                }
            }

            // Segmentos contiguos por marca
            var sb = new System.Text.StringBuilder();
            bool cur = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (i == 0) { cur = marks[0]; sb.Append(text[i]); continue; }
                if (marks[i] == cur) { sb.Append(text[i]); continue; }
                outSegs.Add(new HlSegment(sb.ToString(), cur));
                sb.Length = 0;
                cur = marks[i];
                sb.Append(text[i]);
            }
            if (sb.Length > 0) outSegs.Add(new HlSegment(sb.ToString(), cur));
            if (outSegs.Count == 0) outSegs.Add(new HlSegment(text, false));
            return outSegs;
        }

        /// <summary>Variante de una sola palabra.</summary>
        public static List<HlSegment> Split(string text, string word)
        {
            var l = new List<string>();
            if (!string.IsNullOrEmpty(word)) l.Add(word);
            return Split(text, l);
        }

        /// <summary>¿Hay al menos una ocurrencia? (para vistas compactas)</summary>
        public static bool AnyMatch(string text, string word)
        {
            foreach (var s in Split(text, word))
                if (s.Match) return true;
            return false;
        }
    }
}
