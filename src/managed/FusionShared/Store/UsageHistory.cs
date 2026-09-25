// ============================================================================
//  Fusion-HP · FusionShared/Store/UsageHistory.cs — historial de uso (beta-1
//  HistoryPanel, v1.6): registra qué se proyectó y cuándo en historial.jsonl
//  (idéntico al del estudio nativo C++), con más usadas, recientes y CSV.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Fusion.Shared
{
    public class HistoryEntry
    {
        public long T;
        public string Title = "";
        public string Kind = "";
    }

    public class HistoryCount
    {
        public string Title = "";
        public int Count;
        public long Last;
    }

    public static class UsageHistory
    {
        static List<HistoryEntry> cache;
        static string path;

        /// <summary>Carpeta de datos del usuario (la misma del cancionero).</summary>
        public static void Init(string dataDir)
        {
            path = Path.Combine(dataDir, "historial.jsonl");
            cache = null;
        }

        static void LoadIfNeeded()
        {
            if (cache != null) return;
            cache = new List<HistoryEntry>();
            if (path == null || !File.Exists(path)) return;
            try
            {
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    try
                    {
                        var j = JsonValue.Parse(line);
                        cache.Add(new HistoryEntry
                        {
                            T = (long)j.GetNum("t", 0),
                            Title = j.GetStr("title", ""),
                            Kind = j.GetStr("kind", "")
                        });
                    }
                    catch { /* línea corrupta: saltar */ }
                }
            }
            catch { /* el historial nunca tumba la app */ }
        }

        public static void Record(string title, string kind)
        {
            LoadIfNeeded();
            var e = new HistoryEntry
            {
                T = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds,
                Title = title ?? "",
                Kind = kind ?? ""
            };
            cache.Add(e);
            if (path == null) return;
            try
            {
                var j = JsonValue.Object();
                j.Set("t", JsonValue.Make(e.T));
                j.Set("title", JsonValue.Make(e.Title));
                j.Set("kind", JsonValue.Make(e.Kind));
                File.AppendAllText(path, j.ToJsonString() + "\n", Encoding.UTF8);
            }
            catch { }
        }

        public static List<HistoryEntry> Recent(int n)
        {
            LoadIfNeeded();
            var outL = new List<HistoryEntry>();
            for (int i = cache.Count - 1; i >= 0 && outL.Count < n; i--) outL.Add(cache[i]);
            return outL;
        }

        public static List<HistoryCount> Top(int n)
        {
            LoadIfNeeded();
            var map = new Dictionary<string, HistoryCount>();
            foreach (var e in cache)
            {
                HistoryCount c;
                if (!map.TryGetValue(e.Title, out c))
                {
                    c = new HistoryCount { Title = e.Title };
                    map[e.Title] = c;
                }
                c.Count++;
                if (e.T > c.Last) c.Last = e.T;
            }
            var v = new List<HistoryCount>(map.Values);
            v.Sort(delegate(HistoryCount a, HistoryCount b)
            {
                int d = b.Count.CompareTo(a.Count);
                return d != 0 ? d : b.Last.CompareTo(a.Last);
            });
            if (v.Count > n) v.RemoveRange(n, v.Count - n);
            return v;
        }

        public static bool ExportCsv(string file)
        {
            LoadIfNeeded();
            try
            {
                var sb = new StringBuilder();
                sb.Append('\uFEFF');            // BOM UTF-8 (Excel es-VE)
                sb.Append("fecha;titulo;tipo\n");
                foreach (var e in cache)
                {
                    var dt = new DateTime(1970, 1, 1).AddMilliseconds(e.T).ToLocalTime();
                    sb.Append(dt.ToString("yyyy-MM-dd HH:mm"));
                    sb.Append(';');
                    sb.Append((e.Title ?? "").Replace(';', ','));
                    sb.Append(';');
                    sb.Append((e.Kind ?? "").Replace(';', ','));
                    sb.Append('\n');
                }
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                return true;
            }
            catch { return false; }
        }
    }
}
