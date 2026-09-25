// ============================================================================
//  Fusion-HP · FusionShared/Bible/BookNames.cs — libros canónicos con nombres
//  en español y alias de búsqueda [SPEC §9.1.4: modelo bíblico interno unificado]
// ============================================================================
using System;
using System.Collections.Generic;

namespace Fusion.Shared.Bible
{
    /// <summary>Libro canónico con número (1-66) y alias en español.</summary>
    public class BibleBook
    {
        public int Number;
        public string Name;
        public string[] Aliases;
        public int Chapters;

        public BibleBook(int n, string name, int chapters, string[] aliases)
        {
            Number = n; Name = name; Chapters = chapters; Aliases = aliases;
        }
    }

    public static class BookNames
    {
        public static readonly BibleBook[] Books = new BibleBook[]
        {
            new BibleBook(1, "Génesis", 50, new[]{"gn", "gen", "ge"}),
            new BibleBook(2, "Éxodo", 40, new[]{"ex", "exo"}),
            new BibleBook(3, "Levítico", 27, new[]{"lv", "lev"}),
            new BibleBook(4, "Números", 36, new[]{"nm", "num", "numero", "números"}),
            new BibleBook(5, "Deuteronomio", 34, new[]{"dt", "deut"}),
            new BibleBook(6, "Josué", 24, new[]{"jos", "joshua"}),
            new BibleBook(7, "Jueces", 21, new[]{"jue", "jud"}),
            new BibleBook(8, "Rut", 4, new[]{"rt", "ruth"}),
            new BibleBook(9, "1 Samuel", 31, new[]{"1s", "1sa", "1 sam"}),
            new BibleBook(10, "2 Samuel", 24, new[]{"2s", "2sa", "2 sam"}),
            new BibleBook(11, "1 Reyes", 22, new[]{"1r", "1re"}),
            new BibleBook(12, "2 Reyes", 25, new[]{"2r", "2re"}),
            new BibleBook(13, "1 Crónicas", 29, new[]{"1cr", "1 cro"}),
            new BibleBook(14, "2 Crónicas", 36, new[]{"2cr", "2 cro"}),
            new BibleBook(15, "Esdras", 10, new[]{"esd", "ezra"}),
            new BibleBook(16, "Nehemías", 13, new[]{"neh", "ne"}),
            new BibleBook(17, "Ester", 10, new[]{"est", "esth"}),
            new BibleBook(18, "Job", 42, new[]{"jb", "job"}),
            new BibleBook(19, "Salmos", 150, new[]{"sal", "ps", "psalm", "salmo"}),
            new BibleBook(20, "Proverbios", 31, new[]{"pr", "prov", "proverbio"}),
            new BibleBook(21, "Eclesiastés", 12, new[]{"ec", "eccl", "eclesiastes"}),
            new BibleBook(22, "Cantares", 8, new[]{"cnt", "cant", "cantar de los cantares"}),
            new BibleBook(23, "Isaías", 66, new[]{"is", "isa"}),
            new BibleBook(24, "Jeremías", 52, new[]{"jr", "jer"}),
            new BibleBook(25, "Lamentaciones", 5, new[]{"lm", "lam"}),
            new BibleBook(26, "Ezequiel", 48, new[]{"ez", "eze", "ezeq"}),
            new BibleBook(27, "Daniel", 12, new[]{"dn", "dan"}),
            new BibleBook(28, "Oseas", 14, new[]{"os", "hos", "osea"}),
            new BibleBook(29, "Joel", 3, new[]{"jl", "joel"}),
            new BibleBook(30, "Amós", 9, new[]{"am", "amos"}),
            new BibleBook(31, "Obadías", 1, new[]{"ob", "obad", "obadias"}),
            new BibleBook(32, "Jonás", 4, new[]{"jon", "jnah"}),
            new BibleBook(33, "Miqueas", 7, new[]{"mi", "mic"}),
            new BibleBook(34, "Nahúm", 3, new[]{"nah", "na"}),
            new BibleBook(35, "Habacuc", 3, new[]{"hab", "hb"}),
            new BibleBook(36, "Sofonías", 3, new[]{"sof", "zeph", "sofonia"}),
            new BibleBook(37, "Hageo", 2, new[]{"hg", "hag"}),
            new BibleBook(38, "Zacarías", 14, new[]{"zc", "zech", "zacarias"}),
            new BibleBook(39, "Malaquías", 4, new[]{"ml", "mal", "malaquias"}),
            new BibleBook(40, "Mateo", 28, new[]{"mt", "mat"}),
            new BibleBook(41, "Marcos", 16, new[]{"mc", "mar", "mark"}),
            new BibleBook(42, "Lucas", 24, new[]{"lc", "luc", "luk"}),
            new BibleBook(43, "Juan", 21, new[]{"jn", "ju", "jua"}),
            new BibleBook(44, "Hechos", 28, new[]{"hch", "hech", "acts", "acto"}),
            new BibleBook(45, "Romanos", 16, new[]{"ro", "rom"}),
            new BibleBook(46, "1 Corintios", 16, new[]{"1co", "1 cor"}),
            new BibleBook(47, "2 Corintios", 13, new[]{"2co", "2 cor"}),
            new BibleBook(48, "Gálatas", 6, new[]{"ga", "gal"}),
            new BibleBook(49, "Efesios", 6, new[]{"ef", "eph"}),
            new BibleBook(50, "Filipenses", 4, new[]{"fil", "php", "flp"}),
            new BibleBook(51, "Colosenses", 4, new[]{"col", "cl"}),
            new BibleBook(52, "1 Tesalonicenses", 5, new[]{"1ts", "1 tes"}),
            new BibleBook(53, "2 Tesalonicenses", 3, new[]{"2ts", "2 tes"}),
            new BibleBook(54, "1 Timoteo", 6, new[]{"1tm", "1 tim"}),
            new BibleBook(55, "2 Timoteo", 4, new[]{"2tm", "2 tim"}),
            new BibleBook(56, "Tito", 3, new[]{"tt", "tit"}),
            new BibleBook(57, "Filemón", 1, new[]{"flm", "phm", "filemon"}),
            new BibleBook(58, "Hebreos", 13, new[]{"heb", "hb2"}),
            new BibleBook(59, "Santiago", 5, new[]{"stg", "sant", "jas"}),
            new BibleBook(60, "1 Pedro", 5, new[]{"1p", "1pe", "1 ped"}),
            new BibleBook(61, "2 Pedro", 3, new[]{"2p", "2pe", "2 ped"}),
            new BibleBook(62, "1 Juan", 5, new[]{"1jn", "1 juan"}),
            new BibleBook(63, "2 Juan", 1, new[]{"2jn", "2 juan"}),
            new BibleBook(64, "3 Juan", 1, new[]{"3jn", "3 juan"}),
            new BibleBook(65, "Judas", 1, new[]{"jud", "jd"}),
            new BibleBook(66, "Apocalipsis", 22, new[]{"ap", "apo", "rev", "apoc"}),
        };

        static readonly Dictionary<string, BibleBook> byKey = BuildIndex();

        static Dictionary<string, BibleBook> BuildIndex()
        {
            var d = new Dictionary<string, BibleBook>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in Books)
            {
                d[Normalize(b.Name)] = b;
                foreach (var a in b.Aliases) d[Normalize(a)] = b;
            }
            // Números con espacio: "1 Samuel" → "1samuel" ya cubierto por Normalize
            return d;
        }

        public static string Normalize(string s)
        {
            if (s == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        public static BibleBook ByNumber(int n)
        {
            if (n >= 1 && n <= 66) return Books[n - 1];
            return null;
        }

        public static BibleBook Find(string nameOrAlias)
        {
            if (string.IsNullOrEmpty(nameOrAlias)) return null;
            BibleBook b;
            return byKey.TryGetValue(Normalize(nameOrAlias), out b) ? b : null;
        }
    }

    /// <summary>Cita bíblica referenciada y validada.</summary>
    public struct BibleReference
    {
        public BibleBook Book;
        public int Chapter;
        public int VerseStart;
        public int VerseEnd;

        public bool Valid { get { return Book != null && Chapter > 0 && VerseStart > 0; } }

        public override string ToString()
        {
            if (!Valid) return "";
            if (VerseEnd > VerseStart) return Book.Name + " " + Chapter + ":" + VerseStart + "-" + VerseEnd;
            return Book.Name + " " + Chapter + ":" + VerseStart;
        }

        /// <summary>Parsea "Juan 3:16", "1 Co 13:4-7", "sal 23", "genesis 1".</summary>
        public static BibleReference Parse(string text)
        {
            var r = new BibleReference();
            if (string.IsNullOrEmpty(text)) return r;
            string t = text.Trim();
            int idx = -1;
            // buscar el separador capítulo:versículo desde el final
            for (int i = t.Length - 1; i >= 0; i--)
            {
                if (t[i] == ':') { idx = i; break; }
            }
            string bookPart, chapPart, versePart = null;
            if (idx > 0)
            {
                versePart = t.Substring(idx + 1);
                string left = t.Substring(0, idx);
                int sp = left.LastIndexOf(' ');
                if (sp > 0)
                {
                    bookPart = left.Substring(0, sp);
                    chapPart = left.Substring(sp + 1);
                }
                else { bookPart = left; chapPart = "1"; }
            }
            else
            {
                int sp = t.LastIndexOf(' ');
                if (sp <= 0) return r;
                bookPart = t.Substring(0, sp);
                chapPart = t.Substring(sp + 1);
            }
            int dash = versePart != null ? versePart.IndexOf('-') : -1;
            int vs = 1, ve = 1;
            if (dash > 0)
            {
                int a, b2;
                if (int.TryParse(versePart.Substring(0, dash).Trim(), out a) &&
                    int.TryParse(versePart.Substring(dash + 1).Trim(), out b2)) { vs = a; ve = b2; }
            }
            else if (versePart != null)
            {
                int a;
                if (int.TryParse(versePart.Trim(), out a)) { vs = a; ve = a; }
            }
            int ch;
            if (!int.TryParse(chapPart.Trim(), out ch)) ch = 1;
            r.Book = BookNames.Find(bookPart);
            r.Chapter = ch;
            r.VerseStart = vs;
            r.VerseEnd = Math.Max(vs, ve);
            return r;
        }
    }
}
