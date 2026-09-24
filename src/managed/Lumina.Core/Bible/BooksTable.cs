// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  BooksTable.cs (v5.1.0 «FUNDAMENTO») : tabla canónica de los 66 libros de la
//  Biblia en español — espejo EXACTO de BibleRef.cpp (mismos nombres y
//  abreviaturas) para que la UI C# pueda:
//    * formatear referencias legibles en los resultados de búsqueda FTS
//      ("Jn 3:16" en vez de "libro 43, cap 3, ver 16");
//    * construir una referencia tipada a partir de (libro, capítulo, verso)
//      que el núcleo resuelve (BibleRef::Resolve) para cargar el pasaje.
//  Solo datos estáticos: sin dependencias, net35-safe.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;

namespace lumina.core
{
    public static class BooksTable
    {
        /// <summary>Un libro: número canónico (1..66), nombre y abreviaturas.</summary>
        public sealed class Book
        {
            public readonly int Number;
            public readonly string Name;
            public readonly string[] Abbrs;
            internal Book(int number, string name, string[] abbrs)
            {
                Number = number; Name = name; Abbrs = abbrs;
            }
        }

        private static readonly Book[] Books = BuildTable();

        private static Book[] BuildTable()
        {
            List<Book> t = new List<Book>(66);
            Add(t, 1,  "Génesis",          "gn", "gen", "ge");
            Add(t, 2,  "Éxodo",            "ex", "exo", "exod");
            Add(t, 3,  "Levítico",         "lv", "lev");
            Add(t, 4,  "Números",          "nm", "num");
            Add(t, 5,  "Deuteronomio",     "dt", "deu", "deut");
            Add(t, 6,  "Josué",            "jos", "josu");
            Add(t, 7,  "Jueces",           "jue", "jueces");
            Add(t, 8,  "Rut",              "rt", "rut");
            Add(t, 9,  "1 Samuel",         "1s", "1sa", "1 sam", "1sam");
            Add(t, 10, "2 Samuel",         "2s", "2sa", "2 sam", "2sam");
            Add(t, 11, "1 Reyes",          "1r", "1re", "1 rey", "1rey");
            Add(t, 12, "2 Reyes",          "2r", "2re", "2 rey", "2rey");
            Add(t, 13, "1 Crónicas",       "1cr", "1cro", "1 cr", "1cron");
            Add(t, 14, "2 Crónicas",       "2cr", "2cro", "2 cr", "2cron");
            Add(t, 15, "Esdras",           "esd");
            Add(t, 16, "Nehemías",         "neh");
            Add(t, 17, "Ester",            "est");
            Add(t, 18, "Job",              "job");
            Add(t, 19, "Salmos",           "sal", "salmo", "salmos", "ps");
            Add(t, 20, "Proverbios",       "pr", "prov", "pv");
            Add(t, 21, "Eclesiastés",      "ec", "ecle");
            Add(t, 22, "Cantares",         "cnt", "cant");
            Add(t, 23, "Isaías",           "is", "isa");
            Add(t, 24, "Jeremías",         "jer");
            Add(t, 25, "Lamentaciones",    "lam");
            Add(t, 26, "Ezequiel",         "ez", "eze");
            Add(t, 27, "Daniel",           "dn", "dan");
            Add(t, 28, "Oseas",            "os", "ose");
            Add(t, 29, "Joel",             "jl", "joel");
            Add(t, 30, "Amós",             "am", "amos");
            Add(t, 31, "Obadías",          "ob", "obd");
            Add(t, 32, "Jonás",            "jon");
            Add(t, 33, "Miqueas",          "miq");
            Add(t, 34, "Nahúm",            "nah");
            Add(t, 35, "Habacuc",          "hab");
            Add(t, 36, "Sofonías",         "sof");
            Add(t, 37, "Hageo",            "hag");
            Add(t, 38, "Zacarías",         "zac");
            Add(t, 39, "Malaquías",        "mal");
            Add(t, 40, "Mateo",            "mt", "mat");
            Add(t, 41, "Marcos",           "mr", "mar");
            Add(t, 42, "Lucas",            "lc", "luc");
            Add(t, 43, "Juan",             "jn", "ju", "jua");
            Add(t, 44, "Hechos",           "hch", "hech");
            Add(t, 45, "Romanos",          "ro", "rom", "rm");
            Add(t, 46, "1 Corintios",      "1co", "1 cor", "1cor");
            Add(t, 47, "2 Corintios",      "2co", "2 cor", "2cor");
            Add(t, 48, "Gálatas",          "gal");
            Add(t, 49, "Efesios",          "ef", "efe");
            Add(t, 50, "Filipenses",       "fil");
            Add(t, 51, "Colosenses",       "col");
            Add(t, 52, "1 Tesalonicenses", "1ts", "1 te", "1tes");
            Add(t, 53, "2 Tesalonicenses", "2ts", "2 te", "2tes");
            Add(t, 54, "1 Timoteo",        "1ti", "1 ti", "1tim");
            Add(t, 55, "2 Timoteo",        "2ti", "2 ti", "2tim");
            Add(t, 56, "Tito",             "tit");
            Add(t, 57, "Filemón",          "flm");
            Add(t, 58, "Hebreos",          "heb");
            Add(t, 59, "Santiago",         "stg", "sgt", "sant");
            Add(t, 60, "1 Pedro",          "1p", "1pe", "1 ped", "1ped");
            Add(t, 61, "2 Pedro",          "2p", "2pe", "2 ped", "2ped");
            Add(t, 62, "1 Juan",           "1jn", "1 juan", "1juan");
            Add(t, 63, "2 Juan",           "2jn", "2 juan", "2juan");
            Add(t, 64, "3 Juan",           "3jn", "3 juan", "3juan");
            Add(t, 65, "Judas",            "jud", "judas");
            Add(t, 66, "Apocalipsis",      "ap", "apo", "apoc");
            return t.ToArray();
        }

        private static void Add(List<Book> t, int n, string name, params string[] abbrs)
        {
            // Abreviatura principal en minúsculas (convención del núcleo).
            string[] a = new string[abbrs.Length];
            for (int i = 0; i < abbrs.Length; i++)
                a[i] = abbrs[i].ToLowerInvariant();
            t.Add(new Book(n, name, a));
        }

        /// <summary>Libro por número (1..66); null si está fuera de rango.</summary>
        public static Book ByNumber(int n)
        {
            if (n < 1 || n > Books.Length) return null;
            return Books[n - 1];
        }

        /// <summary>Nombre del libro ("Juan"); "" si el número no es válido.</summary>
        public static string NameOf(int n)
        {
            Book b = ByNumber(n);
            return b != null ? b.Name : string.Empty;
        }

        /// <summary>Abreviatura corta preferida ("jn"); "" si no es válido.</summary>
        public static string AbbrOf(int n)
        {
            Book b = ByNumber(n);
            return (b != null && b.Abbrs.Length > 0) ? b.Abbrs[0] : string.Empty;
        }

        /// <summary>
        /// Referencia tipada compacta para (libro, capítulo, verso):
        /// "jn 3:16" — lista de abreviaturas aceptadas por el núcleo. Con toChapter
        /// != toVerse produce un rango "jn 3:16-18".
        /// </summary>
        public static string Reference(int book, int chapter, int verse, int toVerse)
        {
            string ab = AbbrOf(book);
            if (ab.Length == 0) return string.Empty;
            if (toVerse > verse)
                return string.Format(CultureInfo.InvariantCulture, "{0} {1}:{2}-{3}",
                    ab, chapter, verse, toVerse);
            return string.Format(CultureInfo.InvariantCulture, "{0} {1}:{2}", ab, chapter, verse);
        }
    }
}
