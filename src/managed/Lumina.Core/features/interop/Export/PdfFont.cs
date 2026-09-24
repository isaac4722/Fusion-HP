// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  PdfFont.cs : métricas AFM de las fuentes base-14 Helvetica / Helvetica-Bold
//  (PDF estándar, SIN incrustar fuentes → archivos pequeños y compatibles con
//  cualquier visor de Windows 7 a Windows 11).
//
//  Anchos en unidades de 1/1000 del tamaño de la fuente (formato AFM estándar).
//  El mapa cubre WinAnsi (codepage 1252): los caracteres acentuados usan el
//  ancho de su letra base (aproximación documentada, suficiente para centrar).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;

namespace lumina.core
{
    internal static class PdfFont
    {
        /// <summary>Ancho por byte WinAnsi (32..255) en 1/1000 em.</summary>
        public static readonly int[] Helvetica = BuildHelvetica();
        public static readonly int[] HelveticaBold = BuildHelveticaBold();

        /// <summary>Ancho de una cadena WinAnsi ya codificada (bytes).</summary>
        public static int StringWidth(int[] widths, byte[] winAnsi, int size)
        {
            int total = 0;
            foreach (byte b in winAnsi)
            {
                int idx = b - 32;
                total += (idx >= 0 && idx < widths.Length) ? widths[idx] : 556;
            }
            return (int)Math.Round(total * (size / 1000.0));
        }

        /// <summary>
        /// Unicode → WinAnsi (cp1252). Devuelve '?' para lo no representable
        /// (con falsa transliteración mínima: “ ” ’ ‘ → " " ' ').
        /// </summary>
        public static byte[] ToWinAnsi(string s)
        {
            if (string.IsNullOrEmpty(s)) return new byte[0];
            List<byte> outB = new List<byte>(s.Length);
            foreach (char c in s)
            {
                if (c == '\u00A0') { outB.Add(0xA0); continue; }   // nbsp
                if (c < 0x80) { outB.Add((byte)c); continue; }
                int cp = (int)c;
                byte b;
                if (TryCp1252(cp, out b)) { outB.Add(b); continue; }
                outB.Add((byte)'?');
            }
            return outB.ToArray();
        }

        private static bool TryCp1252(int cp, out byte b)
        {
            // Latin-1 directo (0xA0..0xFF)
            if (cp >= 0xA0 && cp <= 0xFF) { b = (byte)cp; return true; }
            // Bloque cp1252 específico (0x80..0x9F)
            switch (cp)
            {
                case 0x201A: b = 0x82; return true;
                case 0x0192: b = 0x83; return true;
                case 0x201E: b = 0x84; return true;
                case 0x2026: b = 0x85; return true;
                case 0x2020: b = 0x86; return true;
                case 0x2021: b = 0x87; return true;
                case 0x02C6: b = 0x88; return true;
                case 0x2030: b = 0x89; return true;
                case 0x0160: b = 0x8A; return true;
                case 0x2039: b = 0x8B; return true;
                case 0x0152: b = 0x8C; return true;
                case 0x017D: b = 0x8E; return true;
                case 0x2018: b = 0x91; return true;
                case 0x2019: b = 0x92; return true;
                case 0x201C: b = 0x93; return true;
                case 0x201D: b = 0x94; return true;
                case 0x2022: b = 0x95; return true;
                case 0x2013: b = 0x96; return true;
                case 0x2014: b = 0x97; return true;
                case 0x02DC: b = 0x98; return true;
                case 0x2122: b = 0x99; return true;
                case 0x0161: b = 0x9A; return true;
                case 0x203A: b = 0x9B; return true;
                case 0x0153: b = 0x9C; return true;
                case 0x017E: b = 0x9E; return true;
                case 0x0178: b = 0x9F; return true;
                default: b = 0; return false;
            }
        }

        // ------------------------------------------------------------ tablas

        private static int[] BuildHelvetica()
        {
            int[] w = new int[224];
            // 32..126 (AFM Helvetica oficial)
            int[] ascii = new int[]
            {
                278,278,355,556,556,889,667,191,333,333,389,584,278,333,278,278,
                556,556,556,556,556,556,556,556,556,556,278,278,584,584,584,556,
                1015,667,667,722,722,667,611,778,722,278,500,667,556,833,722,778,
                667,778,722,667,611,722,667,944,667,667,611,278,278,278,469,556,
                333,556,556,500,556,556,278,556,556,222,222,500,222,833,556,556,
                556,556,333,500,278,556,500,722,500,500,500,334,260,334,584
            };
            for (int i = 0; i < ascii.Length && i < 95; i++) w[i] = ascii[i];
            // 127..159: no usados en WinAnsi imprimible
            for (int i = 95; i < 128; i++) w[i] = 556;
            // 160..255 (WinAnsi): aprox. por letra base (diacríticos + símbolos)
            int[] upper = new int[224];
            Array.Copy(w, upper, Math.Min(w.Length, upper.Length));
            for (int c = 0xA0; c <= 0xFF; c++)
            {
                int idx = c - 32;
                switch (c)
                {
                    case 0xA1: w[idx] = 333; break;  // ¡
                    case 0xBF: w[idx] = 556; break;  // ¿
                    case 0xAB: w[idx] = 556; break;  // «
                    case 0xBB: w[idx] = 556; break;  // »
                    case 0xA6: w[idx] = 260; break;  // ¦
                    case 0xA9: w[idx] = 737; break;  // ©
                    case 0xAE: w[idx] = 737; break;  // ®
                    case 0xB0: w[idx] = 400; break;  // °
                    case 0xB7: w[idx] = 278; break;  // ·
                    case 0xA4: w[idx] = 556; break;  // ¤
                    case 0xB1: w[idx] = 584; break;  // ±
                    case 0xBD: w[idx] = 834; break;  // ½
                    case 0xBC: w[idx] = 834; break;  // ¼
                    case 0xD7: w[idx] = 584; break;  // ×
                    case 0xF7: w[idx] = 584; break;  // ÷
                    default:
                        w[idx] = BaseWidth(ascii, c);
                        break;
                }
            }
            return w;
        }

        private static int[] BuildHelveticaBold()
        {
            int[] w = new int[224];
            int[] ascii = new int[]
            {
                278,333,474,556,556,889,722,238,333,333,389,584,278,333,278,278,
                556,556,556,556,556,556,556,556,556,556,333,333,584,584,584,611,
                975,722,722,722,722,667,611,778,722,278,556,722,611,833,722,778,
                667,778,722,667,611,722,667,944,667,667,611,333,278,333,584,556,
                333,556,611,556,611,556,333,611,611,278,278,556,278,889,611,611,
                611,611,389,556,333,611,556,778,556,556,500,389,280,389,584
            };
            for (int i = 0; i < ascii.Length && i < 95; i++) w[i] = ascii[i];
            for (int i = 95; i < 128; i++) w[i] = 556;
            for (int c = 0xA0; c <= 0xFF; c++)
            {
                int idx = c - 32;
                switch (c)
                {
                    case 0xA1: w[idx] = 333; break;
                    case 0xBF: w[idx] = 611; break;
                    case 0xAB: w[idx] = 556; break;
                    case 0xBB: w[idx] = 556; break;
                    case 0xA6: w[idx] = 280; break;
                    case 0xA9: w[idx] = 737; break;
                    case 0xAE: w[idx] = 737; break;
                    case 0xB0: w[idx] = 400; break;
                    case 0xB7: w[idx] = 278; break;
                    case 0xA4: w[idx] = 556; break;
                    case 0xB1: w[idx] = 584; break;
                    case 0xBD: w[idx] = 834; break;
                    case 0xBC: w[idx] = 834; break;
                    case 0xD7: w[idx] = 584; break;
                    case 0xF7: w[idx] = 584; break;
                    default:
                        w[idx] = BaseWidth(ascii, c);
                        break;
                }
            }
            return w;
        }

        /// <summary>Ancho aproximado del carácter WinAnsi según su letra base.</summary>
        private static int BaseWidth(int[] ascii, int winAnsiChar)
        {
            char baseChar;
            switch (winAnsiChar)
            {
                case 0xC0: case 0xC1: case 0xC2: case 0xC3: case 0xC4: case 0xC5: case 0xE0: case 0xE1:
                case 0xE2: case 0xE3: case 0xE4: case 0xE5: baseChar = 'a'; break;
                case 0xC6: case 0xE6: baseChar = 'a'; break;
                case 0xC7: case 0xE7: baseChar = 'c'; break;
                case 0xC8: case 0xC9: case 0xCA: case 0xCB: case 0xE8: case 0xE9: case 0xEA: case 0xEB: baseChar = 'e'; break;
                case 0xCC: case 0xCD: case 0xCE: case 0xCF: case 0xEC: case 0xED: case 0xEE: case 0xEF: baseChar = 'i'; break;
                case 0xD1: case 0xF1: baseChar = 'n'; break;
                case 0xD2: case 0xD3: case 0xD4: case 0xD5: case 0xD6: case 0xF2: case 0xF3:
                case 0xF4: case 0xF5: case 0xF6: baseChar = 'o'; break;
                case 0xD9: case 0xDA: case 0xDB: case 0xDC: case 0xF9: case 0xFA: case 0xFB: case 0xFC: baseChar = 'u'; break;
                case 0xDD: case 0xFD: case 0xFF: baseChar = 'y'; break;
                default: return 556;
            }
            bool lower = winAnsiChar >= 0xE0;
            char c = lower ? baseChar : char.ToUpperInvariant(baseChar);
            int idx = c - 32;
            return (idx >= 0 && idx < 95) ? ascii[idx] : 556;
        }
    }
}
