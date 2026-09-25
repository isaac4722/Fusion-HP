// ============================================================================
//  Fusion-HP · FusionShared/Bible/BibleStore.cs — biblioteca bíblica unificada
//  [SPEC §9.1]: importa Zefania XML, e-Sword (.bib SQLite 9+ con Scripture
//  cifrada Twofish + TSV legado) y JSON; normaliza al modelo interno; acceso
//  por índice en disco (búsqueda ≤200 ms [SPEC §10.1]).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Fusion.Shared.Bible
{
    /// <summary>Biblia instalada en el almacén local.</summary>
    public class InstalledBible
    {
        public string Id;
        public string Name;
        public string Version;
        public string Copyright;
        public string DataPath;      // .fbi (Fusion Bible Index)
        public int VerseCount;
    }

    public struct VerseRef
    {
        public int Book, Chapter, Verse;
    }

    /// <summary>Resultado de fidelidad de una importación [SPEC §9.3.4].</summary>
    public class ImportReport
    {
        public int Verses;
        public int Books;
        public List<string> Warnings = new List<string>();
        public bool Ok { get { return Verses > 0; } }
    }

    public class BibleStore
    {
        readonly string dir;
        public BibleStore(string dataDir)
        {
            dir = Path.Combine(dataDir, "bibles");
            Directory.CreateDirectory(dir);
        }

        // ---------------------------------------------------------------- instalar
        void WriteBible(string id, string name, string version, string copyright,
                        List<KeyValuePair<VerseRef, string>> verses)
        {
            // data: textos UTF-8 concatenados · index: registros de 16 bytes
            string dataPath = Path.Combine(dir, id + ".fbi");
            string idxPath = Path.Combine(dir, id + ".fbi.idx");
            using (var data = new BinaryWriter(File.Create(dataPath)))
            using (var idx = new BinaryWriter(File.Create(idxPath)))
            {
                foreach (var kv in verses)
                {
                    byte[] txt = Encoding.UTF8.GetBytes(kv.Value);
                    idx.Write((byte)kv.Key.Book);
                    idx.Write((byte)kv.Key.Chapter);
                    idx.Write((ushort)kv.Key.Verse);
                    idx.Write((uint)data.BaseStream.Position);
                    idx.Write((uint)txt.Length);
                    data.Write(txt);
                }
            }
            var meta = JsonValue.Object();
            meta.Set("id", JsonValue.Make(id));
            meta.Set("name", JsonValue.Make(name));
            meta.Set("version", JsonValue.Make(version));
            meta.Set("copyright", JsonValue.Make(copyright));
            meta.Set("verses", JsonValue.Make(verses.Count));
            Json.WriteFile(Path.Combine(dir, id + ".fbi.json"), meta);
        }

        public List<InstalledBible> List()
        {
            var r = new List<InstalledBible>();
            foreach (var f in Directory.GetFiles(dir, "*.fbi.json"))
            {
                try
                {
                    var j = Json.ParseFile(f);
                    r.Add(new InstalledBible
                    {
                        Id = j.GetStr("id", Path.GetFileNameWithoutExtension(f)),
                        Name = j.GetStr("name", "?"),
                        Version = j.GetStr("version", ""),
                        Copyright = j.GetStr("copyright", ""),
                        DataPath = Path.Combine(dir, j.GetStr("id", "") + ".fbi"),
                        VerseCount = j.GetInt("verses", 0)
                    });
                }
                catch { }
            }
            return r;
        }

        // ---------------------------------------------------------------- lectura
        readonly Dictionary<string, IndexEntry[]> indexCache = new Dictionary<string, IndexEntry[]>(StringComparer.OrdinalIgnoreCase);

        IndexEntry[] LoadIndex(InstalledBible b)
        {
            IndexEntry[] cached;
            lock (indexCache)
            {
                if (indexCache.TryGetValue(b.DataPath, out cached)) return cached;
            }
            string idx = b.DataPath + ".idx";
            var bytes = File.ReadAllBytes(idx);
            int n = bytes.Length / 16;
            var r = new IndexEntry[n];
            for (int i = 0; i < n; i++)
            {
                int o = i * 16;
                r[i].Book = bytes[o];
                r[i].Chapter = bytes[o + 1];
                r[i].Verse = (bytes[o + 2] << 8) | bytes[o + 3];
                r[i].Offset = (uint)(bytes[o + 4] << 24 | bytes[o + 5] << 16 | bytes[o + 6] << 8 | bytes[o + 7]);
                r[i].Length = (uint)(bytes[o + 8] << 24 | bytes[o + 9] << 16 | bytes[o + 10] << 8 | bytes[o + 11]);
            }
            lock (indexCache) indexCache[b.DataPath] = r;
            return r;
        }

        struct IndexEntry
        {
            public int Book, Chapter, Verse;
            public uint Offset, Length;
        }

        string ReadVerseText(InstalledBible b, IndexEntry e)
        {
            using (var fs = File.OpenRead(b.DataPath))
            {
                fs.Seek(e.Offset, SeekOrigin.Begin);
                var buf = new byte[e.Length];
                int got = 0;
                while (got < buf.Length) { int n = fs.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
                return Encoding.UTF8.GetString(buf);
            }
        }

        /// <summary>Lee un pasaje completo [SPEC §5.2 #2]. Devuelve (versos, referencia).</summary>
        public List<string> GetPassage(InstalledBible b, BibleReference reference)
        {
            var r = new List<string>();
            if (b == null || !reference.Valid) return r;
            var idx = LoadIndex(b);
            foreach (var e in idx)
            {
                if (e.Book == reference.Book.Number && e.Chapter == reference.Chapter &&
                    e.Verse >= reference.VerseStart && e.Verse <= reference.VerseEnd)
                    r.Add(ReadVerseText(b, e));
            }
            return r;
        }

        /// <summary>Búsqueda instantánea por palabra o cita [SPEC §7.2.2]. ≤200 ms.</summary>
        public List<KeyValuePair<string, string>> Search(InstalledBible b, string query, int limit)
        {
            var r = new List<KeyValuePair<string, string>>();
            if (b == null || string.IsNullOrEmpty(query)) return r;
            // ¿Es una cita? ("Juan 3:16")
            var refr = BibleReference.Parse(query);
            if (refr.Valid)
            {
                var passage = GetPassage(b, refr);
                foreach (var v in passage) r.Add(new KeyValuePair<string, string>(refr.ToString(), v));
                if (r.Count > 0) return r;
            }
            // Búsqueda por palabra: exploración secuencial del archivo de datos
            string norm = NormalizeText(query);
            var idx = LoadIndex(b);
            using (var fs = File.OpenRead(b.DataPath))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                long pos = 0;
                for (int i = 0; i < idx.Length && r.Count < limit; i++)
                {
                    var e = idx[i];
                    if (e.Offset != pos) { fs.Seek(e.Offset, SeekOrigin.Begin); reader.DiscardBufferedData(); }
                    var buf = new char[e.Length];
                    int got = 0;
                    while (got < buf.Length) { int n = reader.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
                    pos = e.Offset + e.Length;
                    string text = new string(buf);
                    if (NormalizeText(text).IndexOf(norm, StringComparison.Ordinal) >= 0)
                    {
                        var book = BookNames.ByNumber(e.Book);
                        string cite = (book != null ? book.Name : "?" + e.Book) + " " + e.Chapter + ":" + e.Verse;
                        r.Add(new KeyValuePair<string, string>(cite, text));
                    }
                }
            }
            return r;
        }

        static string NormalizeText(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").ToLowerInvariant())
            {
                if (c == 'á') sb.Append('a'); else if (c == 'é') sb.Append('e');
                else if (c == 'í') sb.Append('i'); else if (c == 'ó') sb.Append('o');
                else if (c == 'ú') sb.Append('u'); else if (c == 'ü') sb.Append('u');
                else if (c == 'ñ') sb.Append('n');
                else if (!char.IsPunctuation(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        // ================================================================ IMPORTADORES
        /// <summary>Importa cualquier formato soportado, detectando por contenido.</summary>
        public ImportReport ImportAny(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".xml") return ImportZefania(path);
            if (ext == ".json") return ImportJson(path);
            if (ext == ".bib" || ext == ".bblx" || ext == ".bibx" || ext == ".bbl")
                return ImportEsword(path);
            // heurística por contenido
            using (var f = File.OpenText(path))
            {
                char[] head = new char[200];
                int n = f.Read(head, 0, 200);
                string s = new string(head, 0, n);
                if (s.TrimStart().StartsWith("<")) return ImportZefania(path);
                if (s.TrimStart().StartsWith("{") || s.TrimStart().StartsWith("[")) return ImportJson(path);
            }
            if (ext == ".txt") return ImportTsv(path);
            var rep = new ImportReport();
            rep.Warnings.Add("Formato no reconocido. Soportados: Zefania XML, e-Sword .bib/.bblx, JSON, TSV.");
            return rep;
        }

        // ---------------------------------------------------------------- Zefania
        /// <summary>Zefania XML [SPEC §9.1.1]: parseo incremental con XmlReader.</summary>
        public ImportReport ImportZefania(string path)
        {
            var rep = new ImportReport();
            var verses = new List<KeyValuePair<VerseRef, string>>();
            string name = Path.GetFileNameWithoutExtension(path);
            string version = "", copyright = "";

            var settings = new XmlReaderSettings();
#pragma warning disable 618 // ProhibitDtd existe en net35 y sigue vigente en 4.x
            settings.ProhibitDtd = true;                          // sin entidades externas [SPEC §9.2.4]
#pragma warning restore 618
            settings.XmlResolver = null;
            int curBook = 0, curChap = 0;
            var sb = new StringBuilder();

            using (var reader = XmlReader.Create(path, settings))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        switch (reader.LocalName)
                        {
                            case "XMLBIBLE":
                            case "bible":
                                if (reader.HasAttributes)
                                    while (reader.MoveToNextAttribute())
                                    {
                                        if (reader.LocalName == "biblename") name = reader.Value;
                                        if (reader.LocalName == "type") version = reader.Value;
                                    }
                                break;
                            case "INFORMATION":
                                // metadatos internos
                                break;
                            case "BIBLEBOOK":
                                curBook = GetAttrInt(reader, "bnumber", 0);
                                if (curBook == 0) curBook = BookNumberByName(GetAttr(reader, "bname"));
                                break;
                            case "CHAPTER":
                                curChap = GetAttrInt(reader, "cnumber", 1);
                                break;
                            case "VERSES":
                            case "VERSE":
                                {
                                    int vn = GetAttrInt(reader, "vnumber", 1);
                                    sb.Length = 0;
                                    // contenido mixto: texto + elementos de estilo internos
                                    int depth = reader.Depth;
                                    bool done = false;
                                    while (!done && reader.Read())
                                    {
                                        if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.CDATA ||
                                            reader.NodeType == XmlNodeType.SignificantWhitespace)
                                            sb.Append(reader.Value);
                                        else if (reader.NodeType == XmlNodeType.EndElement)
                                        {
                                            if (reader.Depth == depth && (reader.LocalName == "VERSES" || reader.LocalName == "VERSE")) done = true;
                                        }
                                    }
                                    string text = CleanVerse(sb.ToString());
                                    if (curBook > 0 && vn > 0 && text.Length > 0)
                                        verses.Add(new KeyValuePair<VerseRef, string>(
                                            new VerseRef { Book = curBook, Chapter = curChap, Verse = vn }, text));
                                }
                                break;
                            case "title":
                            case "TITLE":
                                if (reader.IsStartElement() && !reader.IsEmptyElement && reader.Read() && reader.NodeType == XmlNodeType.Text)
                                {
                                    string t = reader.Value.Trim();
                                    if (t.Length > 0 && t.Length < 80 && name == Path.GetFileNameWithoutExtension(path)) { /* posible nombre */ }
                                }
                                break;
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "FORMAT")
                    {
                        // ignorado
                    }
                }
            }

            // metadatos de INFORMATION (segunda pasada barata solo si falta nombre)
            if (verses.Count == 0)
            {
                rep.Warnings.Add("El XML no contiene versos en formato Zefania (se esperaban BIBLEBOOK/CHAPTER/VERSES).");
                return rep;
            }
            string id = MakeId(name);
            WriteBible(id, name, version, copyright, verses);
            rep.Verses = verses.Count;
            rep.Books = CountBooks(verses);
            return rep;
        }

        static string GetAttr(XmlReader r, string name)
        {
            return r.GetAttribute(name) ?? "";
        }
        static int GetAttrInt(XmlReader r, string name, int def)
        {
            int v;
            return int.TryParse(r.GetAttribute(name), out v) ? v : def;
        }
        static int BookNumberByName(string name)
        {
            var b = BookNames.Find(name);
            return b != null ? b.Number : 0;
        }
        static string CleanVerse(string s)
        {
            return (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        }
        static int CountBooks(List<KeyValuePair<VerseRef, string>> verses)
        {
            var set = new HashSet<int>();
            foreach (var v in verses) set.Add(v.Key.Book);
            return set.Count;
        }
        static string MakeId(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            if (sb.Length == 0) sb.Append("bible");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- e-Sword
        /// <summary>
        /// e-Sword [SPEC §9.1.2]: módulos 9+ (.bib/.bblx SQLite) con columna
        /// Scripture cifrada (Twofish-128 "Heb_4:12" + contenedor SQLitePlus +
        /// zlib); variante TSV de texto plano legada; detección de módulos con
        /// cifrado completo de archivo (mensaje claro al operador).
        /// </summary>
        public ImportReport ImportEsword(string path)
        {
            // 1) ¿SQLite plano?
            byte[] head = new byte[16];
            using (var f = File.OpenRead(path)) f.Read(head, 0, 16);
            bool isSqlite = Encoding.ASCII.GetString(head, 0, 16) == "SQLite format 3\0";
            if (isSqlite) return ImportEswordSqlite(path);

            // 2) ¿TSV legado (#BIB …)?
            string first;
            using (var f = File.OpenText(path)) first = f.ReadLine() ?? "";
            if (first.StartsWith("#BIB")) return ImportTsv(path);

            // 3) Cifrado completo de archivo: módulo protegido — informar sin stack [SPEC §11.2.3]
            var rep = new ImportReport();
            rep.Warnings.Add("El módulo e-Sword está protegido con cifrado completo de archivo y no puede leerse directamente. " +
                             "Consejo: abre el módulo en e-Sword y expórtalo, o consigue la edición equivalente en formato Zefania XML (.xml).");
            return rep;
        }

        ImportReport ImportEswordSqlite(string path)
        {
            var rep = new ImportReport();
            var verses = new List<KeyValuePair<VerseRef, string>>();
            string name = Path.GetFileNameWithoutExtension(path), version = "", copyright = "";
            var tf = new Twofish128("Heb_4:12");

            using (var db = new SQLiteFileReader(path))
            {
                if (!db.HasTable("Bible"))
                {
                    rep.Warnings.Add("La base e-Sword no contiene la tabla Bible.");
                    return rep;
                }
                if (db.HasTable("Details"))
                {
                    foreach (var row in db.ReadTable("Details"))
                    {
                        if (row.Values.Count >= 1)
                        {
                            var desc = row.Values[0] as string;
                            if (!string.IsNullOrEmpty(desc)) name = desc;
                        }
                        if (row.Values.Count >= 2)
                        {
                            var ab = row.Values[1] as string;
                            if (!string.IsNullOrEmpty(ab)) version = ab;
                        }
                    }
                }
                foreach (var row in db.ReadTable("Bible"))
                {
                    if (row.Values.Count < 4) continue;
                    int book = Convert.ToInt32(row.Values[0]);
                    int chapter = Convert.ToInt32(row.Values[1]);
                    int verse = Convert.ToInt32(row.Values[2]);
                    string text = null;
                    var raw = row.Values[3];
                    if (raw is string) text = (string)raw;                    // versión sin cifrar
                    else if (raw is byte[]) text = DecryptScripture(tf, (byte[])raw);
                    if (text == null) { rep.Warnings.Add("Verso " + book + "." + chapter + ":" + verse + " ilegible (cifrado no resuelto)."); continue; }
                    text = StripRtf(text);
                    if (text.Trim().Length == 0) continue;
                    verses.Add(new KeyValuePair<VerseRef, string>(
                        new VerseRef { Book = book, Chapter = chapter, Verse = verse }, text));
                }
            }
            if (verses.Count == 0) return rep;
            WriteBible(MakeId(name), name, version, copyright, verses);
            rep.Verses = verses.Count;
            rep.Books = CountBooks(verses);
            if (rep.Warnings.Count == 0)
                rep.Warnings.Add("Importado desde e-Sword: las notas al pie y números Strong se descartan en el MVP [SPEC §9.1.2].");
            return rep;
        }

        /// <summary>Descifra una columna Scripture: Twofish-ECB + contenedor SQLitePlus + inflate.</summary>
        internal static string DecryptScripture(Twofish128 tf, byte[] blob)
        {
            try
            {
                if (blob == null || blob.Length < 16 || (blob.Length % 16) != 0) return null;
                byte[] dec = tf.DecryptEcb(blob);

                int pos = 0;
                pos += 1;                                        // flags/checksum
                int compSize = dec[pos] | (dec[pos + 1] << 8);   // 2B LE
                pos += 2;
                if (pos + 6 > dec.Length) return null;
                if (dec[pos] != 0x00 || dec[pos + 1] != 0x1F) return null;   // marca del contenedor
                pos += 2;
                uint expected = (uint)(dec[pos] | (dec[pos + 1] << 8) | (dec[pos + 2] << 16) | (dec[pos + 3] << 24));
                pos += 4;
                compSize -= 4;
                if (compSize <= 0 || pos + compSize > dec.Length) return null;

                // zlib: 2 bytes de cabecera + deflate + adler32
                byte[] zlib = new byte[compSize];
                Array.Copy(dec, pos, zlib, 0, compSize);
                if (zlib.Length < 6) return null;
                using (var ms = new MemoryStream(zlib, 2, zlib.Length - 6))
                using (var inf = new DeflateStream(ms, CompressionMode.Decompress))
                using (var outMs = new MemoryStream())
                {
                    byte[] buf = new byte[4096];
                    int n;
                    while ((n = inf.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, n);
                    byte[] plain = outMs.ToArray();
                    if (expected != 0 && plain.Length != expected) return null;   // verificación de tamaño
                    return Encoding.UTF8.GetString(plain);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Elimina marcas RTF básicas del texto de e-Sword.</summary>
        internal static string StripRtf(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.IndexOf("{\\rtf", StringComparison.OrdinalIgnoreCase) < 0 && s.IndexOf("\\par", StringComparison.Ordinal) < 0)
                return s.Replace("\\pard", "").Trim();
            var sb = new StringBuilder();
            bool escape = false;
            int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (escape)
                {
                    escape = false;
                    if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                    {
                        // palabra de control: saltar hasta delimitador
                        while (i + 1 < s.Length && ((s[i + 1] >= 'a' && s[i + 1] <= 'z') || char.IsDigit(s[i + 1]) || s[i + 1] == '-')) i++;
                        continue;
                    }
                    if (c == '\'')
                    {
                        // \'hh → carácter escapado
                        if (i + 2 < s.Length)
                        {
                            string hex = s.Substring(i + 1, 2);
                            int code;
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out code))
                                sb.Append((char)code);
                            i += 2;
                        }
                        continue;
                    }
                    continue;
                }
                if (c == '\\') { escape = true; continue; }
                if (c == '{') { depth++; continue; }
                if (c == '}') { depth--; if (depth < 0) depth = 0; continue; }
                sb.Append(c);
            }
            string r = sb.ToString();
            r = r.Replace("\\par", "\n").Replace("\\line", "\n");
            return r.Trim();
        }

        // ---------------------------------------------------------------- JSON
        /// <summary>JSON [SPEC §9.1.3]: {reference, text}[] o el formato de libros anidado.</summary>
        public ImportReport ImportJson(string path)
        {
            var rep = new ImportReport();
            var verses = new List<KeyValuePair<VerseRef, string>>();
            string name = Path.GetFileNameWithoutExtension(path), version = "", copyright = "";
            var root = Json.ParseFile(path);

            if (root.Type == JsonValue.Kind.Object && root.GetArray("books") != null)
            {
                version = root.GetStr("version", "");
                name = root.GetStr("name", name);
                copyright = root.GetStr("license", "");
                foreach (var bj in root.GetArray("books"))
                {
                    var book = BookNames.Find(bj.GetStr("name", bj.GetStr("n", "")));
                    if (book == null) continue;
                    var chapters = bj.GetArray("chapters");
                    if (chapters == null) continue;
                    for (int c = 0; c < chapters.Count; c++)
                    {
                        var ch = chapters[c];
                        if (ch.Type == JsonValue.Kind.Array)
                        {
                            for (int v = 0; v < ch.Items.Count; v++)
                            {
                                var txt = ch.Items[v].Type == JsonValue.Kind.String ? ch.Items[v].Str : ch.Items[v].GetStr("text", "");
                                if (!string.IsNullOrEmpty(txt))
                                    verses.Add(new KeyValuePair<VerseRef, string>(
                                        new VerseRef { Book = book.Number, Chapter = c + 1, Verse = v + 1 }, txt));
                            }
                        }
                        else if (ch.Type == JsonValue.Kind.Object)
                        {
                            var vv = ch.GetArray("verses");
                            if (vv == null) continue;
                            foreach (var vj in vv)
                            {
                                int vn = vj.GetInt("number", vj.GetInt("n", 0));
                                string txt = vj.GetStr("text", vj.GetStr("t", ""));
                                if (vn > 0 && !string.IsNullOrEmpty(txt))
                                    verses.Add(new KeyValuePair<VerseRef, string>(
                                        new VerseRef { Book = book.Number, Chapter = c + 1, Verse = vn }, txt));
                            }
                        }
                    }
                }
            }
            else if (root.Type == JsonValue.Kind.Array)
            {
                foreach (var item in root.AsArray)
                {
                    string reference = item.GetStr("reference", item.GetStr("ref", ""));
                    string text = item.GetStr("text", item.GetStr("verse", ""));
                    var refr = BibleReference.Parse(reference);
                    if (refr.Valid && !string.IsNullOrEmpty(text))
                        verses.Add(new KeyValuePair<VerseRef, string>(new VerseRef { Book = refr.Book.Number, Chapter = refr.Chapter, Verse = refr.VerseStart }, text));
                }
            }

            if (verses.Count == 0)
            {
                rep.Warnings.Add("El JSON no tiene la forma esperada ({reference,text}[] o {books:[…]}).");
                return rep;
            }
            WriteBible(MakeId(name), name, version, copyright, verses);
            rep.Verses = verses.Count;
            rep.Books = CountBooks(verses);
            return rep;
        }

        // ---------------------------------------------------------------- TSV legado
        /// <summary>TSV legado del prototipo anterior: #BIB/#VERSION/#NAME/#BOOKS + "Libro\tcap\tver\ttexto".</summary>
        public ImportReport ImportTsv(string path)
        {
            var rep = new ImportReport();
            var verses = new List<KeyValuePair<VerseRef, string>>();
            string name = Path.GetFileNameWithoutExtension(path), version = "";
            var cache = new Dictionary<string, int>();
            using (var f = new StreamReader(path, Encoding.UTF8, true))
            {
                string line;
                while ((line = f.ReadLine()) != null)
                {
                    if (line.Length == 0) continue;
                    if (line.StartsWith("#"))
                    {
                        if (line.StartsWith("#NAME")) name = line.Substring(5).Trim();
                        else if (line.StartsWith("#VERSION")) version = line.Substring(8).Trim();
                        continue;
                    }
                    var parts = line.Split('\t');
                    if (parts.Length < 4) continue;
                    int bn;
                    if (!cache.TryGetValue(parts[0], out bn))
                    {
                        var b = BookNames.Find(parts[0]);
                        bn = b != null ? b.Number : 0;
                        cache[parts[0]] = bn;
                    }
                    int ch, vs;
                    if (bn == 0 || !int.TryParse(parts[1], out ch) || !int.TryParse(parts[2], out vs)) continue;
                    verses.Add(new KeyValuePair<VerseRef, string>(
                        new VerseRef { Book = bn, Chapter = ch, Verse = vs }, parts[3].Trim()));
                }
            }
            if (verses.Count == 0)
            {
                rep.Warnings.Add("El TSV no contiene versos válidos.");
                return rep;
            }
            WriteBible(MakeId(name), name, version, "", verses);
            rep.Verses = verses.Count;
            rep.Books = CountBooks(verses);
            return rep;
        }
    }
}
