// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  BibleJson.cs (v5.1.0 «FUNDAMENTO») : lector STREAMING del formato de biblia
//  JSON distribuido con el paquete portable (resources/data/bible_rvr1909.json):
//
//    {"version":"RVR1909","name":"Reina-Valera 1909","license":"…",
//     "books":[{"n":1,"name":"Génesis","abbr":"Gén",
//               "chapters":[["verso 1","verso 2", …], [ … segundo capítulo … ]]}, …]}
//
//  ¿Por qué un lector propio y no MiniJson.Parse(archivo)?
//    La RVR1909 pesa ~4 MB y contiene ~31.000 versículos: parsearla a un
//    Dictionary en memoria genera cientos de miles de objetos intermedios y
//    picos de GC innecesarios. Este lector consume el TextReader carácter a
//    carácter y emite cada versículo a una fila — memoria O(1) respecto al
//    tamaño de la biblia y sin dependencias (net35-safe).
//
//  CONTRATO DEL CURSOR (clave para no perder separadores):
//    * c.Ch SIEMPRE apunta al próximo carácter SIN consumir.
//    * ReadString(): entra con c.Ch=='"' y sale con c.Ch = carácter siguiente
//      a la comilla de cierre (tampoco consumido).
//    * ReadNumber(): sale con c.Ch = primer carácter no numérico.
//    * Quien detecta '}' o ']' de cierre es quien lo consume (c.Next()).
//
//  Salida (mismo contrato que ZefaniaBible): filas
//  [versión, libro, capítulo, verso, texto] listas para InsertBibleRows.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace lumina.core
{
    public sealed class BibleJsonResult
    {
        public string Version = string.Empty;
        public string Name = string.Empty;
        public string License = string.Empty;
        public int BookCount;
        public long VerseCount;
        public long SkippedRows;
        public List<object> Rows = new List<object>();   // [version, book, chapter, verse, text]
    }

    public static class BibleJson
    {
        /// <summary>
        /// Parsea la biblia JSON (ruta de archivo). Tolerante: los libros fuera
        /// de 1..66 y los valores no válidos se descartan y cuentan en
        /// SkippedRows. Solo lanza FormatException (contenido) o IOException (E/S).
        /// </summary>
        public static BibleJsonResult ParseFile(string path, int maxVerses)
        {
            BibleJsonResult res = new BibleJsonResult();
            using (StreamReader rd = new StreamReader(path, new UTF8Encoding(false)))
            {
                Cursor c = new Cursor(rd);
                c.SkipWs();
                Expect(c, '{');
                c.Next();                                   // consume '{'
                while (true)
                {
                    c.SkipWs();
                    if (c.Ch == '}') { c.Next(); break; }   // fin del objeto raíz
                    if (c.Ch == ',') { c.Next(); continue; }
                    if (c.Ch == -1) throw new FormatException("JSON truncado (línea " + c.Line + ").");
                    string key = ReadString(c);
                    c.SkipWs();
                    Expect(c, ':');
                    c.Next();                               // consume ':'
                    c.SkipWs();
                    if (key == "version") res.Version = ReadString(c);
                    else if (key == "name") res.Name = ReadString(c);
                    else if (key == "license") res.License = ReadString(c);
                    else if (key == "books") ReadBooks(c, res, maxVerses <= 0 ? int.MaxValue : maxVerses);
                    else SkipValue(c);
                }
            }
            return res;
        }

        // ------------------------------------------------------------ books --

        private static void ReadBooks(Cursor c, BibleJsonResult res, int maxVerses)
        {
            Expect(c, '[');
            c.Next();                                       // consume '['
            while (true)
            {
                c.SkipWs();
                if (c.Ch == ']') { c.Next(); return; }      // fin de "books"
                if (c.Ch == ',') { c.Next(); continue; }
                if (c.Ch == -1) throw new FormatException("JSON truncado en books (línea " + c.Line + ").");
                Expect(c, '{');
                c.Next();                                   // consume '{' del libro
                int n = 0;
                while (true)
                {
                    c.SkipWs();
                    if (c.Ch == '}') { c.Next(); break; }   // fin del libro
                    if (c.Ch == ',') { c.Next(); continue; }
                    if (c.Ch == -1) throw new FormatException("JSON truncado en libro (línea " + c.Line + ").");
                    string key = ReadString(c);
                    c.SkipWs();
                    Expect(c, ':');
                    c.Next();
                    c.SkipWs();
                    if (key == "n") n = (int)ReadNumber(c);
                    else if (key == "chapters") ReadChapters(c, res, n, maxVerses);
                    else SkipValue(c);                      // name/abbr/… informativos
                }
                if (n >= 1 && n <= 66) res.BookCount++;
            }
        }

        // --------------------------------------------------------- chapters --

        private static void ReadChapters(Cursor c, BibleJsonResult res, int book, int maxVerses)
        {
            Expect(c, '[');
            c.Next();                                       // consume '[' de chapters
            int chapter = 0;
            while (true)
            {
                c.SkipWs();
                if (c.Ch == ']') { c.Next(); return; }      // fin de chapters
                if (c.Ch == ',') { c.Next(); continue; }
                if (c.Ch == -1) throw new FormatException("JSON truncado en chapters (línea " + c.Line + ").");
                chapter++;
                Expect(c, '[');                             // abre el capítulo
                c.Next();
                int verse = 0;
                while (true)
                {
                    c.SkipWs();
                    if (c.Ch == ']') { c.Next(); break; }   // fin del capítulo
                    if (c.Ch == ',') { c.Next(); continue; }
                    if (c.Ch == -1) throw new FormatException("JSON truncado en versos (línea " + c.Line + ").");
                    verse++;
                    if (c.Ch == '"')
                    {
                        string text = ReadString(c);
                        if (book >= 1 && book <= 66 && text.Length > 0 && res.VerseCount < maxVerses)
                        {
                            List<object> row = new List<object>(5);
                            row.Add(res.Version.Length > 0 ? res.Version : "JSON");
                            row.Add(book);
                            row.Add(chapter);
                            row.Add(verse);
                            row.Add(text);
                            res.Rows.Add(row);
                            res.VerseCount++;
                        }
                        else res.SkippedRows++;
                    }
                    else
                    {
                        SkipValue(c);
                        res.SkippedRows++;
                    }
                }
            }
        }

        // ------------------------------------------------------- primitivas --

        private sealed class Cursor
        {
            public readonly TextReader Rd;
            public int Ch;                  // próximo carácter SIN consumir
            public long Line = 1;
            public Cursor(TextReader rd) { Rd = rd; Ch = rd.Read(); }
            public int Next() { Ch = Rd.Read(); if (Ch == '\n') ++Line; return Ch; }
            public void SkipWs()
            {
                while (Ch == ' ' || Ch == '\t' || Ch == '\r' || Ch == '\n') Next();
            }
        }

        private static void Expect(Cursor c, char ch)
        {
            if (c.Ch != ch)
                throw new FormatException("Se esperaba '" + ch + "' (línea " + c.Line + ").");
        }

        private static string ReadString(Cursor c)
        {
            Expect(c, '"');
            StringBuilder sb = new StringBuilder(96);
            while (true)
            {
                c.Next();
                if (c.Ch == -1) throw new FormatException("Cadena sin cerrar (línea " + c.Line + ").");
                if (c.Ch == '"') { c.Next(); return sb.ToString(); }
                if (c.Ch == '\\')
                {
                    c.Next();
                    switch (c.Ch)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            {
                                int v = 0;
                                for (int i = 0; i < 4; i++)
                                {
                                    c.Next();
                                    int h = HexVal(c.Ch);
                                    if (h < 0) throw new FormatException("\\u inválido (línea " + c.Line + ").");
                                    v = (v << 4) | h;
                                }
                                sb.Append((char)v);
                                break;
                            }
                        default:
                            throw new FormatException("Escape desconocido \\" + (char)c.Ch + " (línea " + c.Line + ").");
                    }
                }
                else
                {
                    sb.Append((char)c.Ch);
                }
            }
        }

        private static double ReadNumber(Cursor c)
        {
            StringBuilder sb = new StringBuilder(16);
            while ((c.Ch >= '0' && c.Ch <= '9') || c.Ch == '-' || c.Ch == '+' ||
                   c.Ch == '.' || c.Ch == 'e' || c.Ch == 'E')
            {
                sb.Append((char)c.Ch);
                c.Next();
            }
            double v;
            if (!double.TryParse(sb.ToString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out v)) v = 0;
            return v;
        }

        private static int HexVal(int ch)
        {
            if (ch >= '0' && ch <= '9') return ch - '0';
            if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
            if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
            return -1;
        }

        /// <summary>Salta cualquier valor JSON (objeto/array/cadena/número/literal).</summary>
        private static void SkipValue(Cursor c)
        {
            c.SkipWs();
            if (c.Ch == '"') { ReadString(c); return; }
            if (c.Ch == '{' || c.Ch == '[')
            {
                int depth = 0;
                while (c.Ch != -1)
                {
                    if (c.Ch == '{' || c.Ch == '[') { depth++; c.Next(); }
                    else if (c.Ch == '}' || c.Ch == ']')
                    {
                        depth--;
                        c.Next();
                        if (depth == 0) return;
                    }
                    else if (c.Ch == '"') ReadString(c);   // deja c.Ch tras la comilla de cierre
                    else c.Next();
                }
                return;
            }
            while (c.Ch != ',' && c.Ch != '}' && c.Ch != ']' && c.Ch != -1) c.Next();
        }
    }
}
