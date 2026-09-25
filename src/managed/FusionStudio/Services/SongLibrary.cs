// ============================================================================
//  Fusion-HP · FusionStudio/Services/SongLibrary.cs — biblioteca de canciones
//  [SPEC §7.2.1]: búsqueda global, historial de uso, persistencia JSON en
//  datos/songs. Las canciones están disponibles directamente (sin búsqueda
//  obligatoria — corrección del prototipo anterior).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using Fusion.Shared;
using Fusion.Shared.Model;

namespace Fusion.Studio.Services
{
    public class SongLibrary
    {
        readonly string dir;
        readonly List<Song> songs = new List<Song>();
        public event Action Changed;

        public SongLibrary(string dataDir)
        {
            dir = dataDir;
            Directory.CreateDirectory(dir);
            LoadAll();
        }

        void LoadAll()
        {
            songs.Clear();
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var s = Song.FromJson(Json.ParseFile(f));
                    if (s != null) songs.Add(s);
                }
                catch { /* archivo corrupto: se ignora sin romper la biblioteca */ }
            }
            songs.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));
        }

        public IList<Song> All()
        {
            return songs.AsReadOnly();
        }

        public void Save(Song s)
        {
            if (string.IsNullOrEmpty(s.Id)) s.Id = "song-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            Json.WriteFile(Path.Combine(dir, SafeName(s.Id) + ".json"), s.ToJson());
            if (!songs.Contains(s)) { songs.Add(s); Sort(); }
            Fire();
        }

        public void Remove(Song s)
        {
            try { File.Delete(Path.Combine(dir, SafeName(s.Id) + ".json")); } catch { }
            songs.Remove(s);
            Fire();
        }

        void Sort()
        {
            songs.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>Búsqueda global por título/letra/etiqueta [SPEC §7.2.1].</summary>
        public List<Song> Search(string query)
        {
            if (string.IsNullOrEmpty(query))
                return new List<Song>(songs);
            var q = query.Trim().ToLowerInvariant();
            var r = new List<Song>();
            foreach (var s in songs)
            {
                if (s.Title.ToLowerInvariant().Contains(q)) { r.Add(s); continue; }
                if ((s.Artist ?? "").ToLowerInvariant().Contains(q)) { r.Add(s); continue; }
                bool hit = false;
                foreach (var t in s.Tags) if (t.ToLowerInvariant().Contains(q)) { hit = true; break; }
                if (hit) { r.Add(s); continue; }
                foreach (var sec in s.Sections)
                    foreach (var ln in sec.Lines)
                        if (ln.ToLowerInvariant().Contains(q)) { hit = true; break; }
                if (hit) r.Add(s);
            }
            return r;
        }

        static string SafeName(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) if (char.IsLetterOrDigit(c) || c == '-') sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "song";
        }

        /// <summary>Registra un uso (historial de popularidad [SPEC §7.2.1]).</summary>
        public void RegisterUse(Song s)
        {
            s.UseCount++;
            s.LastUsed = DateTime.Now;
            try { Json.WriteFile(Path.Combine(dir, SafeName(s.Id) + ".json"), s.ToJson()); } catch { }
        }

        void Fire()
        {
            var h = Changed;
            if (h != null) h();
        }
    }
}
