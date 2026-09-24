// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ChordUtil : detección de líneas de acordes y tonalidad en la capa gestionada.
//  Port 1:1 de Chords.cpp (ParseToken/IsChordLine) — se usa para saber QUÉ
//  líneas del editor son cifrado antes de llamar a lumina_chords_transpose
//  (el núcleo transpone el token; aquí decidimos si la línea es acorde).
//  El MISMO criterio del núcleo evita falsos positivos en español:
//  "dos", "mis", "fue", "das", "cinco", "cuatro" NO son acordes.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;

namespace lumina.core
{
    public static class ChordUtil
    {
        private static readonly string[] LatinRoots = { "sol", "do", "re", "mi", "fa", "la", "si" };
        private static readonly int[] LatinSemis = { 7, 0, 2, 4, 5, 9, 11 };
        private static readonly int[] SemiForAG = { 9, 11, 0, 2, 4, 5, 7 }; // a b c d e f g
        private const string OkChars = "mMajindsug0123456789"; // v4.2.0: +g para Caug/Caug7

        private static bool StartsWithCI(string s, string prefix)
        {
            return s != null && s.Length >= prefix.Length &&
                   string.Compare(s, 0, prefix, 0, prefix.Length, true, CultureInfo.InvariantCulture) == 0;
        }

        private static string LowerAsciiCopy(string s)
        {
            char[] a = s.ToCharArray();
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] >= 'A' && a[i] <= 'Z') a[i] = (char)(a[i] + 32);
            }
            return new string(a);
        }

        /// <summary>
        /// Parsea un token tipo "Fa#m7/C#" → (raíz "Fa#", semitono 6, cola "m7", bajo "C#").
        /// Devuelve false si el token NO es un acorde válido (criterio del núcleo).
        /// </summary>
        public static bool ParseToken(string tk, out string root, out int rootSemi, out string tail, out string bass)
        {
            root = null; rootSemi = -1; tail = null; bass = null;
            if (string.IsNullOrEmpty(tk) || tk.Length > 10) return false;

            string work = tk;
            string bassPart = null;
            int slash = work.LastIndexOf('/');
            if (slash >= 0)
            {
                bassPart = work.Substring(slash + 1);
                work = work.Substring(0, slash);
                if (bassPart.Length == 0) return false;
            }
            if (work.Length == 0) return false;

            string lower = LowerAsciiCopy(work);
            int semi = -1;
            int rootLen = 0;

            // Raíz latina (prefijos más largos primero: "sol" antes que "s"…)
            for (int i = 0; i < LatinRoots.Length; i++)
            {
                string r = LatinRoots[i];
                if (lower.Length >= r.Length && string.Compare(lower, 0, r, 0, r.Length, StringComparison.Ordinal) == 0)
                {
                    semi = LatinSemis[i];
                    rootLen = r.Length;
                    break;
                }
            }
            // Raíz anglosajona A-G (letra única)
            if (semi < 0 && lower[0] >= 'a' && lower[0] <= 'g')
            {
                semi = SemiForAG[lower[0] - 'a'];
                rootLen = 1;
            }
            if (semi < 0) return false;

            // Alteración (# o b) inmediata tras la raíz
            if (rootLen < lower.Length && (lower[rootLen] == '#' || lower[rootLen] == 'b'))
            {
                semi += (lower[rootLen] == '#') ? 1 : -1;
                rootLen++;
            }
            semi = ((semi % 12) + 12) % 12;

            // Puerta de arranque del sufijo (evita "dos", "mis", "fue", "das"…)
            string suffix = lower.Substring(rootLen);
            if (suffix.Length > 0)
            {
                char c0 = suffix[0];
                bool okStart =
                    c0 == 'm' || c0 == 'M' || c0 == '#' || c0 == 'b' ||
                    (c0 >= '0' && c0 <= '9') ||
                    StartsWithCI(suffix, "sus") || StartsWithCI(suffix, "add") ||
                    StartsWithCI(suffix, "dim") || StartsWithCI(suffix, "aug");
                if (!okStart) return false;
            }
            // Sufijo: solo caracteres del conjunto válido
            for (int k = rootLen; k < lower.Length; k++)
            {
                bool ok = false;
                for (int p = 0; p < OkChars.Length; p++)
                {
                    if (OkChars[p] == lower[k]) { ok = true; break; }
                }
                if (!ok) return false;
            }

            root = work.Substring(0, rootLen);
            rootSemi = semi;
            tail = work.Substring(rootLen);
            bass = bassPart;
            return true;
        }

        public static bool IsChordToken(string tk)
        {
            string r, t, b; int s;
            return ParseToken(tk, out r, out s, out t, out b);
        }

        /// <summary>
        /// Una línea es cifrado si tiene 1..12 tokens y TODOS son acordes válidos
        /// (idéntico al núcleo Chords::IsChordLine).
        /// </summary>
        public static bool IsChordLine(string line)
        {
            if (line == null) return false;
            string t = line.Trim();
            if (t.Length == 0) return false;
            string[] tokens = t.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || tokens.Length > 12) return false;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!IsChordToken(tokens[i])) return false;
            }
            return true;
        }

        private static readonly string[] LatinNames = { "Do", "Do#", "Re", "Re#", "Mi", "Fa", "Fa#", "Sol", "Sol#", "La", "La#", "Si" };

        /// <summary>
        /// Tono detectado de un bloque de texto: primer acorde de la primera
        /// línea de cifrado, en notación latina ("Sol", "Mi", "Do#"…). "—" si no hay.
        /// </summary>
        public static string DetectKey(string rawLyrics)
        {
            if (string.IsNullOrEmpty(rawLyrics)) return "—";
            string[] lines = rawLyrics.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!IsChordLine(lines[i])) continue;
                string tk = lines[i].Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries)[0];
                string root, tail, bass; int semi;
                if (ParseToken(tk, out root, out semi, out tail, out bass) && semi >= 0)
                    return LatinNames[semi];
                return "—";
            }
            return "—";
        }
    }
}
