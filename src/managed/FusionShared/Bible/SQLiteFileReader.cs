// ============================================================================
//  Fusion-HP · FusionShared/Bible/SQLiteFileReader.cs — lector SQLite de SOLO
//  LECTURA en C# puro, sin dependencias nativas [SPEC §3.5: autocontenido].
//  Suficiente para SELECT de tablas simples (sqlite_master, Bible, Details)
//  de módulos e-Sword 9+ (.bib/.bblx): páginas B-Tree de tabla, varints,
//  registros con tipos seriales y páginas de desborde. Formato: fileformat2.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Fusion.Shared.Bible
{
    /// <summary>Fila leída de una tabla SQLite.</summary>
    public class SqliteRow
    {
        public long RowId;
        public List<object> Values = new List<object>();
    }

    /// <summary>Lector mínimo de archivos SQLite (solo lectura, tablas rowid).</summary>
    public class SQLiteFileReader : IDisposable
    {
        FileStream fs;
        int pageSize;
        int usableSize;
        int encoding;             // 1 UTF-8 · 2 UTF-16le · 3 UTF-16be
        readonly Dictionary<string, long> tableRoots = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<long, List<SqliteRow>> pageCache = new Dictionary<long, List<SqliteRow>>();

        public SQLiteFileReader(string path)
        {
            fs = File.OpenRead(path);
            var hdr = new byte[100];
            int got = fs.Read(hdr, 0, 100);
            if (got < 100 || Encoding.ASCII.GetString(hdr, 0, 16) != "SQLite format 3\0")
                throw new InvalidDataException("El archivo no es una base SQLite.");
            int ps = (hdr[16] << 8) | hdr[17];
            if (ps == 1) ps = 65536;
            pageSize = ps;
            int reserved = hdr[20];
            usableSize = pageSize - reserved;
            encoding = (hdr[56] << 24) | (hdr[57] << 16) | (hdr[58] << 8) | hdr[59];
            if (encoding == 0) encoding = 1;
            LoadSchema();
        }

        void LoadSchema()
        {
            var rows = ReadTableBtree(1, "sqlite_master");
            foreach (var r in rows)
            {
                if (r.Values.Count < 5) continue;
                string type = r.Values[0] as string;
                if (type != "table") continue;
                string name = r.Values[1] as string;
                long root = Convert.ToInt64(r.Values[3]);
                if (name != null && !tableRoots.ContainsKey(name)) tableRoots[name] = root;
            }
        }

        public bool HasTable(string name)
        {
            return tableRoots.ContainsKey(name);
        }

        public List<SqliteRow> ReadTable(string name)
        {
            long root;
            if (!tableRoots.TryGetValue(name, out root)) return new List<SqliteRow>();
            return ReadTableBtree(root, name);
        }

        byte[] ReadPage(long pageNumber)
        {
            var buf = new byte[pageSize];
            fs.Seek((pageNumber - 1) * (long)pageSize, SeekOrigin.Begin);
            int got = 0;
            while (got < pageSize)
            {
                int n = fs.Read(buf, got, pageSize - got);
                if (n <= 0) break;
                got += n;
            }
            if (got < pageSize) throw new InvalidDataException("página " + pageNumber + " fuera de rango");
            return buf;
        }

        List<SqliteRow> ReadTableBtree(long rootPage, string table)
        {
            List<SqliteRow> all = null;
            if (pageCache.TryGetValue(rootPage, out all)) return all;
            all = new List<SqliteRow>();
            WalkTablePage(rootPage, all, 0);
            pageCache[rootPage] = all;
            return all;
        }

        void WalkTablePage(long pageNumber, List<SqliteRow> into, int depth)
        {
            if (depth > 40) throw new InvalidDataException("árbol B demasiado profundo");
            var page = ReadPage(pageNumber);
            int hdrOffset = pageNumber == 1 ? 100 : 0;
            int type = page[hdrOffset];
            int cellCount = (page[hdrOffset + 3] << 8) | page[hdrOffset + 4];

            if (type == 5)
            {
                // interior de tabla: recorrer hijos
                for (int i = 0; i < cellCount; i++)
                {
                    int ptrOffset = hdrOffset + 12 + i * 2;
                    int cellOff = (page[ptrOffset] << 8) | page[ptrOffset + 1];
                    if (cellOff < 0 || cellOff + 4 > pageSize) continue;
                    long child = (long)((uint)(page[cellOff] << 24 | page[cellOff + 1] << 16 |
                                               page[cellOff + 2] << 8 | page[cellOff + 3]));
                    WalkTablePage(child, into, depth + 1);
                }
                int rightOff = hdrOffset + 8;
                long right = (long)((uint)(page[rightOff] << 24 | page[rightOff + 1] << 16 |
                                           page[rightOff + 2] << 8 | page[rightOff + 3]));
                if (right > 0) WalkTablePage(right, into, depth + 1);
            }
            else if (type == 13)
            {
                // hoja de tabla
                for (int i = 0; i < cellCount; i++)
                {
                    int ptrOffset = hdrOffset + 8 + i * 2;
                    int cellOff = (page[ptrOffset] << 8) | page[ptrOffset + 1];
                    if (cellOff < 0 || cellOff >= pageSize) continue;
                    ReadLeafCell(page, cellOff, into);
                }
            }
            // otros tipos (índices) se ignoran — lector de solo lectura
        }

        void ReadLeafCell(byte[] page, int off, List<SqliteRow> into)
        {
            int pos = off;
            long payloadLen = ReadVarint(page, ref pos);
            long rowId = ReadVarint(page, ref pos);

            int X = usableSize - 35;
            byte[] payload;
            if (payloadLen <= X)
            {
                payload = new byte[payloadLen];
                Array.Copy(page, pos, payload, 0, (int)payloadLen);
            }
            else
            {
                // desborde
                int M = ((usableSize - 12) * 32 / 255) - 23;
                long K = M + ((payloadLen - M) % (usableSize - 4));
                int local = (int)(K <= X ? K : M);
                payload = new byte[payloadLen];
                Array.Copy(page, pos, payload, 0, local);
                long overflowPage = (long)((uint)(page[pos + local] << 24 | page[pos + local + 1] << 16 |
                                                  page[pos + local + 2] << 8 | page[pos + local + 3]));
                int copied = local;
                int guard = 0;
                while (overflowPage > 0 && copied < payloadLen && guard++ < 100000)
                {
                    var op = ReadPage(overflowPage);
                    long next = (long)((uint)(op[0] << 24 | op[1] << 16 | op[2] << 8 | op[3]));
                    int take = (int)Math.Min(usableSize - 4, payloadLen - copied);
                    Array.Copy(op, 4, payload, copied, take);
                    copied += take;
                    overflowPage = next;
                }
            }

            var row = new SqliteRow { RowId = rowId };
            ParseRecord(payload, row);
            into.Add(row);
        }

        void ParseRecord(byte[] rec, SqliteRow row)
        {
            int pos = 0;
            long headerLen = ReadVarint(rec, ref pos);
            int headerEnd = (int)headerLen;
            var serials = new List<long>();
            while (pos < headerEnd)
                serials.Add(ReadVarint(rec, ref pos));
            int body = headerEnd;
            foreach (var st in serials)
            {
                if (st == 0) row.Values.Add(null);
                else if (st == 1) { row.Values.Add((sbyte)rec[body]); body += 1; }
                else if (st == 2) { row.Values.Add((short)ReadBE(rec, body, 2)); body += 2; }
                else if (st == 3) { row.Values.Add((int)ReadBE24(rec, body)); body += 3; }
                else if (st == 4) { row.Values.Add((int)ReadBE(rec, body, 4)); body += 4; }
                else if (st == 5) { row.Values.Add(ReadBE(rec, body, 6)); body += 6; }
                else if (st == 6) { row.Values.Add(ReadBE(rec, body, 8)); body += 8; }
                else if (st == 7)
                {
                    row.Values.Add(BitConverter.Int64BitsToDouble(ReadBE(rec, body, 8))); body += 8;
                }
                else if (st == 8) row.Values.Add(0);
                else if (st == 9) row.Values.Add(1);
                else if (st >= 12 && (st % 2) == 0)
                {
                    int len = (int)((st - 12) / 2);
                    var b = new byte[len];
                    Array.Copy(rec, body, b, 0, len);
                    row.Values.Add(b);
                    body += len;
                }
                else if (st >= 13)
                {
                    int len = (int)((st - 13) / 2);
                    string s;
                    if (encoding == 2) s = Encoding.Unicode.GetString(rec, body, len);
                    else if (encoding == 3) s = Encoding.BigEndianUnicode.GetString(rec, body, len);
                    else s = Encoding.UTF8.GetString(rec, body, len);
                    row.Values.Add(s);
                    body += len;
                }
            }
        }

        static long ReadBE(byte[] b, int off, int n)
        {
            long v = 0;
            for (int i = 0; i < n; i++) v = (v << 8) | b[off + i];
            return v;
        }
        static long ReadBE24(byte[] b, int off)
        {
            return (long)((b[off] << 16) | (b[off + 1] << 8) | b[off + 2]);
        }

        static long ReadVarint(byte[] b, ref int pos)
        {
            ulong v = 0;
            for (int i = 0; i < 8; i++)
            {
                if (pos >= b.Length) break;
                byte c = b[pos++];
                v = (v << 7) | (ulong)(c & 0x7F);
                if ((c & 0x80) == 0) return (long)v;
            }
            if (pos < b.Length)
            {
                v = (v << 8) | b[pos++];
            }
            return (long)v;
        }

        public void Dispose()
        {
            if (fs != null) fs.Dispose();
            fs = null;
        }
    }
}
