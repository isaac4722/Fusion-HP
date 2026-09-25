// ============================================================================
//  Fusion-HP · FusionShared/Music/Chords.cs — transposición de acordes en
//  tiempo real (función única heredada de las betas 1 «Lumina», port fiel de
//  apps/native-wx-src/core/Chords.h). Soporta notación anglosajona (C, D, E…)
//  y latina (Do, Re, Mi…), alteraciones (#, b), sufijos (m, 7, maj, sus, dim,
//  add…) y bajo con barra ("C/E", "Sol/Fa"), conservando la alineación
//  espacial del cifrado (una columna por acorde).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;

namespace Fusion.Shared.Music
{
    public static class Chords
    {
        // ------------------------------------------------------------ notas
        static readonly string[] LatinNotes = { "Do", "Do#", "Re", "Re#", "Mi", "Fa",
                                                "Fa#", "Sol", "Sol#", "La", "La#", "Si" };
        static readonly string[] AngloNotes = { "C", "C#", "D", "D#", "E", "F",
                                                 "F#", "G", "G#", "A", "A#", "B" };

        static int NoteToSemitone(string noteRaw)
        {
            if (string.IsNullOrEmpty(noteRaw)) return -1;
            string n = noteRaw.Trim().ToLowerInvariant();
            if (n.Length == 0) return -1;
            // Latinas (la más larga primero: "sol#")
            string[] latin = { "solb", "sol#", "sol", "do#", "dob", "do", "reb", "re#",
                               "re", "mib", "mi", "fa#", "fab", "fa", "lab", "la#",
                               "la", "sib", "si" };
            int[] latinSemi = { 6, 8, 7, 1, -1, 0, 1, 3, 2, 3, 4, 6, 4, 5, 8, 10, 9, 10, 11 };
            for (int i = 0; i < latin.Length; i++)
                if (n == latin[i] && latinSemi[i] >= 0) return latinSemi[i];
            // Anglo con enarmónicos
            string[] anglo = { "c", "c#", "db", "d", "d#", "eb", "e", "f", "f#", "gb",
                               "g", "g#", "ab", "a", "a#", "bb", "b" };
            int[] angloSemi = { 0, 1, 1, 2, 3, 3, 4, 5, 6, 6, 7, 8, 8, 9, 10, 10, 11 };
            for (int i = 0; i < anglo.Length; i++)
                if (n == anglo[i]) return angloSemi[i];
            // Nota simple con alteración arbitraria
            if (n.Length >= 1 && "cdefgab".IndexOf(n[0]) >= 0)
            {
                int[] nat = { 0, 2, 4, 5, 7, 9, 11 };
                int semi = nat["cdefgab".IndexOf(n[0])];
                if (n.Length > 1)
                {
                    if (n[1] == '#') semi += 1;
                    else if (n[1] == 'b') semi -= 1;
                }
                return ((semi % 12) + 12) % 12;
            }
            return -1;
        }

        public static string SemitoneToNote(int semi, bool latinNotation)
        {
            semi = ((semi % 12) + 12) % 12;
            return latinNotation ? LatinNotes[semi] : AngloNotes[semi];
        }

        // ------------------------------------------------------------ token
        /// <summary>Parsea "Raíz[alter][sufijo][/bajo]" (raíz latina o anglo).</summary>
        static bool ParseToken(string tk, out string root, out int rootSemi,
                               out string tail, out string bass)
        {
            root = ""; tail = ""; bass = ""; rootSemi = -1;
            if (string.IsNullOrEmpty(tk) || tk.Length > 10) return false;
            const string okChars = "mMajindsu0123456789";
            string work = tk;
            int slash = work.LastIndexOf('/');
            if (slash >= 0)
            {
                bass = work.Substring(slash + 1);
                work = work.Substring(0, slash);
                if (bass.Length == 0) return false;
            }
            if (work.Length == 0) return false;
            string lower = work.ToLowerInvariant();
            // Raíz latina (más largas primero)
            string[] latinRoots = { "sol", "do", "re", "mi", "fa", "la", "si" };
            int[] latinSemis = { 7, 0, 2, 4, 5, 9, 11 };
            int semi = -1, rootLen = 0;
            for (int i = 0; i < latinRoots.Length; i++)
                if (lower.StartsWith(latinRoots[i])) { semi = latinSemis[i]; rootLen = latinRoots[i].Length; break; }
            // Raíz anglo (letra A-G)
            if (semi < 0 && lower[0] >= 'a' && lower[0] <= 'g')
            {
                int[] semiAG = { 9, 11, 0, 2, 4, 5, 7 };   // a b c d e f g
                semi = semiAG[lower[0] - 'a'];
                rootLen = 1;
            }
            if (semi < 0) return false;
            // Alteración
            if (rootLen < lower.Length && (lower[rootLen] == '#' || lower[rootLen] == 'b'))
            {
                if (lower[rootLen] == '#') semi += 1; else semi -= 1;
                rootLen++;
            }
            semi = ((semi % 12) + 12) % 12;
            // Sufijo: solo caracteres válidos
            string suffix = lower.Substring(rootLen);
            for (int k = 0; k < suffix.Length; k++)
                if (okChars.IndexOf(suffix[k]) < 0) return false;
            root = work.Substring(0, rootLen);
            tail = work.Substring(rootLen);
            rootSemi = semi;
            return true;
        }

        public static bool IsChordToken(string tk)
        {
            string r, t, b; int s;
            return ParseToken(tk, out r, out s, out t, out b);
        }

        /// <summary>¿Es una línea completa de cifrado (todos los tokens son acordes)?</summary>
        public static bool IsChordLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            string t = line.Trim();
            if (t.Length == 0) return false;
            string[] toks = t.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length < 1 || toks.Length > 12) return false;
            foreach (string tk in toks)
                if (!IsChordToken(tk)) return false;
            return true;
        }

        /// <summary>Transpone un token; devuelve el original si no es acorde válido.</summary>
        public static string TransposeChordToken(string tk, int semi, bool latinNotation)
        {
            string root, tail, bass; int rootSemi;
            if (!ParseToken(tk, out root, out rootSemi, out tail, out bass)) return tk;
            int dst = ((rootSemi + semi) % 12 + 12) % 12;
            string result = SemitoneToNote(dst, latinNotation) + tail;
            if (bass.Length > 0)
            {
                string bRoot, bTail; int bSemi; string bDiscard;
                if (ParseToken(bass, out bRoot, out bSemi, out bTail, out bDiscard))
                    bass = SemitoneToNote(((bSemi + semi) % 12 + 12) % 12, latinNotation) + bTail;
                result += "/" + bass;
            }
            return result;
        }

        /// <summary>
        /// Transpone una línea de acordes conservando la alineación por columnas
        /// (el relleno con espacios mantiene el cifrado sobre su sílaba).
        /// </summary>
        public static string TransposeLine(string chordLine, int semi, bool latinNotation)
        {
            if (semi == 0 || string.IsNullOrEmpty(chordLine)) return chordLine;
            var sb = new StringBuilder(chordLine.Length + 16);
            int i = 0, n = chordLine.Length;
            while (i < n)
            {
                char c = chordLine[i];
                if (c == ' ' || c == '\t') { sb.Append(c); i++; continue; }
                int j = i;
                while (j < n && chordLine[j] != ' ' && chordLine[j] != '\t') j++;
                string token = chordLine.Substring(i, j - i);
                string moved = TransposeChordToken(token, semi, latinNotation);
                sb.Append(moved);
                for (int k = moved.Length; k < token.Length; k++) sb.Append(' ');
                i = j;
            }
            return sb.ToString();
        }

        /// <summary>Transpone la tonalidad ("C" o "Do") n semitonos.</summary>
        public static string TransposeKey(string key, int semi, bool latinNotation)
        {
            int s = NoteToSemitone(key ?? "");
            if (s < 0) return key;
            return SemitoneToNote(s + semi, latinNotation);
        }

        /// <summary>¿Parece tonalidad válida ("C", "Do", "Am", "Lam")?</summary>
        public static bool LooksLikeKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            string k = key.Trim();
            string root = k.Length > 1 && (k[1] == '#' || k[1] == 'b') ? k.Substring(0, 2) : k.Substring(0, 1);
            int s = NoteToSemitone(root);
            if (s < 0) return false;
            string rest = k.Substring(root.Length).ToLowerInvariant();
            return rest == "" || rest == "m" || rest == "menor" || rest == "maj" || rest == "mayor";
        }

        /// <summary>Notación latina de una tonalidad dada en anglo ("Am"→"Lam").</summary>
        public static string ToLatinKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            string k = key.Trim();
            string root = k.Length > 1 && (k[1] == '#' || k[1] == 'b') ? k.Substring(0, 2) : k.Substring(0, 1);
            int s = NoteToSemitone(root);
            if (s < 0) return key;
            string rest = k.Substring(root.Length);
            if (rest.ToLowerInvariant() == "m") return LatinNotes[s] + "m";
            return LatinNotes[s];
        }
    }
}
