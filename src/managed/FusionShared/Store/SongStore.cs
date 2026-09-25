// ============================================================================
//  Fusion-HP · FusionShared/Store/SongStore.cs — banco de cantos en UNA sola
//  base de datos (petición central del usuario): el cancionero se carga UNA
//  vez a datos/cancionero.fdb y de ahí en adelante el programa CONSULTA sin
//  generar archivos nuevos por canto ni duplicados. El guardado es atómico a
//  través de un archivo temporal (el patrón de PowerPoint al editar): se
//  escribe cancionero.fdb.tmp y se reemplaza, de modo que un corte de luz
//  nunca corrompe la base. La importación deduplica por título+artista
//  normalizados (actualiza en vez de repetir).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Fusion.Shared;
using Fusion.Shared.Model;

namespace Fusion.Shared.Store
{
    public class SongImportReport
    {
        public int Added;
        public int Updated;
        public List<string> Warnings = new List<string>();
        public bool Ok { get { return Added + Updated > 0 || Warnings.Count == 0; } }
    }

    public class SongStore : IDisposable
    {
        const string Format = "fdb.v1";

        readonly string path;
        readonly string tmpPath;
        readonly object gate = new object();
        List<Song> songs = new List<Song>();
        Timer flushTimer;
        bool dirty;
        public event Action Changed;

        public string Path { get { return path; } }

        public SongStore(string dataDir)
        {
            path = System.IO.Path.Combine(dataDir, "cancionero.fdb");
            tmpPath = path + ".tmp";
            Load();
            // Guardado diferido (el «archivo temporal» de PowerPoint): los cambios
            // se asientan en segundos, sin escribir en cada pulsación.
            flushTimer = new Timer(delegate
            {
                lock (gate) { if (!dirty) return; }
                Save();
            }, null, 4000, 4000);
        }

        // ------------------------------------------------------------ carga única
        void Load()
        {
            if (!File.Exists(path)) return;
            try
            {
                var root = Json.ParseFile(path);
                if (root.Type == JsonValue.Kind.Object &&
                    root.GetStr("format") == Format &&
                    root.GetArray("songs") != null)
                {
                    var list = new List<Song>();
                    foreach (var sj in root.GetArray("songs"))
                    {
                        var s = Song.FromJson(sj);
                        if (s != null && !string.IsNullOrEmpty(s.Title)) list.Add(s);
                    }
                    songs = list;
                }
            }
            catch
            {
                // Base dañada: no se rompe el cancionero; queda vacía y se respalda
                try { File.Copy(path, path + ".corrupto", true); } catch { }
            }
            songs.Sort(CompareSongs);
        }

        static int CompareSongs(Song a, Song b)
        {
            return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
        }

        // ------------------------------------------------------------ consultas
        public IList<Song> All()
        {
            lock (gate) return songs.AsReadOnly();
        }

        public int Count { get { lock (gate) return songs.Count; } }

        public Song ById(string id)
        {
            lock (gate)
            {
                foreach (var s in songs) if (s.Id == id) return s;
                return null;
            }
        }

        /// <summary>Búsqueda global por título/artistas/etiquetas/letra [SPEC §7.2.1].</summary>
        public List<Song> Search(string query)
        {
            lock (gate)
            {
                if (string.IsNullOrEmpty(query)) return new List<Song>(songs);
                string q = query.Trim().ToLowerInvariant();
                var r = new List<Song>();
                foreach (var s in songs)
                {
                    if (s.Title.ToLowerInvariant().Contains(q)) { r.Add(s); continue; }
                    if ((s.Artist ?? "").ToLowerInvariant().Contains(q)) { r.Add(s); continue; }
                    bool hit = false;
                    foreach (string t in s.Tags) if (t.ToLowerInvariant().Contains(q)) { hit = true; break; }
                    if (hit) { r.Add(s); continue; }
                    foreach (var sec in s.Sections)
                        foreach (string ln in sec.Lines)
                            if (ln.ToLowerInvariant().Contains(q)) { hit = true; break; }
                    if (hit) r.Add(s);
                }
                return r;
            }
        }

        // ------------------------------------------------------------ escritura
        /// <summary>Guarda o actualiza un canto (misma clave = actualiza, no duplica).</summary>
        public void Save(Song s)
        {
            if (string.IsNullOrEmpty(s.Id)) s.Id = "song-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            lock (gate)
            {
                Song exist = null;
                foreach (var x in songs)
                    if (x.Id == s.Id || SameKey(x, s)) { exist = x; break; }
                if (exist != null)
                {
                    // conservar historial de uso del reemplazado
                    if (s.UseCount == 0) s.UseCount = exist.UseCount;
                    if (s.LastUsed == DateTime.MinValue) s.LastUsed = exist.LastUsed;
                    songs.Remove(exist);
                }
                songs.Add(s);
                songs.Sort(CompareSongs);
            }
            Flush();
            Fire();
        }

        public void Remove(Song s)
        {
            lock (gate) songs.Remove(s);
            Flush();
            Fire();
        }

        /// <summary>Registra un uso (historial) [SPEC §7.2.1]. Guardado diferido.</summary>
        public void RegisterUse(Song s)
        {
            s.UseCount++;
            s.LastUsed = DateTime.Now;
            lock (gate) dirty = true;
        }

        /// <summary>Escribe la base AHORA (atómico: tmp + reemplazo).</summary>
        public void Flush()
        {
            List<Song> snapshot;
            lock (gate)
            {
                snapshot = new List<Song>(songs);
                dirty = false;
            }
            try
            {
                var root = JsonValue.Object();
                root.Set("format", JsonValue.Make(Format));
                root.Set("saved", JsonValue.Make(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
                var arr = JsonValue.Array();
                foreach (var s in snapshot) arr.Add(s.ToJson());
                root.Set("songs", arr);
                // 1) escribir el temporal, 2) reemplazo atómico
                Json.WriteFile(tmpPath, root);
                if (File.Exists(path))
                {
                    File.Replace(tmpPath, path, path + ".bak");
                    try { File.Delete(path + ".bak"); } catch { }
                }
                else File.Move(tmpPath, path);
            }
            catch (Exception)
            {
                // El temporal nunca contamina la base: queda el .tmp para diagnóstico
            }
        }

        void Fire()
        {
            var h = Changed;
            if (h != null) h();
        }

        // ------------------------------------------------------------ importación
        /// <summary>Clave de deduplicación: título + artista normalizados.</summary>
        static string Key(Song s)
        {
            return Normalize(s.Title) + "|" + Normalize(s.Artist);
        }

        static bool SameKey(Song a, Song b) { return Key(a) == Key(b); }

        /// <summary>Normaliza para comparar: minúsculas, sin acentos, sin puntuación.</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s.Trim().ToLowerInvariant())
            {
                char f = c;
                switch (f)
                {
                    case 'á': case 'à': case 'ä': f = 'a'; break;
                    case 'é': case 'è': case 'ë': f = 'e'; break;
                    case 'í': case 'ì': case 'ï': f = 'i'; break;
                    case 'ó': case 'ò': case 'ö': f = 'o'; break;
                    case 'ú': case 'ù': case 'ü': f = 'u'; break;
                    case 'ñ': f = 'n'; break;
                }
                if (char.IsLetterOrDigit(f)) sb.Append(f);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Importa cantos evitando duplicados: si ya existe un canto con el mismo
        /// título+artista se ACTUALIZA (se conserva el historial de uso), no se
        /// repite. Formatos: JSON propio / lote JSON / exportación de la referencia.
        /// </summary>
        public SongImportReport ImportFiles(IEnumerable<string> files)
        {
            var rep = new SongImportReport();
            foreach (string file in files)
            {
                try
                {
                    JsonValue root = Json.ParseFile(file);
                    List<Song> batch = ParseSongs(root);
                    if (batch.Count == 0)
                    {
                        rep.Warnings.Add(Path.GetFileName(file) + ": no se encontraron cantos válidos");
                        continue;
                    }
                    lock (gate)
                    {
                        foreach (var s in batch)
                        {
                            if (string.IsNullOrEmpty(s.Title)) continue;
                            Song exist = null;
                            foreach (var x in songs)
                                if (SameKey(x, s)) { exist = x; break; }
                            if (exist != null)
                            {
                                if (s.UseCount == 0) s.UseCount = exist.UseCount;
                                if (s.LastUsed == DateTime.MinValue) s.LastUsed = exist.LastUsed;
                                if (string.IsNullOrEmpty(s.Id) || !string.IsNullOrEmpty(exist.Id))
                                    s.Id = exist.Id;          // id estable
                                songs.Remove(exist);
                                songs.Add(s);
                                rep.Updated++;
                            }
                            else
                            {
                                if (string.IsNullOrEmpty(s.Id))
                                    s.Id = "song-" + Guid.NewGuid().ToString("N").Substring(0, 12);
                                songs.Add(s);
                                rep.Added++;
                            }
                        }
                        songs.Sort(CompareSongs);
                    }
                }
                catch (Exception e)
                {
                    rep.Warnings.Add(Path.GetFileName(file) + ": " + e.Message);
                }
            }
            Flush();
            Fire();
            return rep;
        }

        static List<Song> ParseSongs(JsonValue root)
        {
            var list = new List<Song>();
            if (root == null) return list;
            if (root.Type == JsonValue.Kind.Array)
            {
                foreach (var s in root.Items) TryAdd(list, s);
            }
            else if (root.Type == JsonValue.Kind.Object)
            {
                var arr = root.GetArray("songs");
                if (arr != null) { foreach (var s in arr.Items) TryAdd(list, s); return list; }
                // Objeto único: canción o canción de la referencia web (title/artist/sections)
                TryAdd(list, root);
            }
            return list;
        }

        static void TryAdd(List<Song> list, JsonValue j)
        {
            try
            {
                var s = Song.FromJson(j);
                if (s != null && !string.IsNullOrEmpty(s.Title)) list.Add(s);
            }
            catch { }
        }

        /// <summary>
        /// Migración desde el esquema anterior (un .json por canto): importa todo
        /// a la base única y aparta los originales a songs/importados/ para que no
        /// queden duplicados activos. Idempotente y sin pérdidas.
        /// </summary>
        public void MigrateLegacy(string legacyDir)
        {
            try
            {
                if (!Directory.Exists(legacyDir)) return;
                string[] files = Directory.GetFiles(legacyDir, "*.json");
                if (files.Length == 0) return;
                ImportFiles(files);
                string done = System.IO.Path.Combine(legacyDir, "importados");
                Directory.CreateDirectory(done);
                foreach (string f in files)
                {
                    try { File.Move(f, System.IO.Path.Combine(done, Path.GetFileName(f))); }
                    catch { }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            Save();                       // asienta cambios pendientes
            if (flushTimer != null) flushTimer.Dispose();
        }

        /// <summary>Guarda si hay cambios pendientes (llamado al cerrar).</summary>
        public void Save()
        {
            bool need;
            lock (gate) need = dirty;
            if (need) Flush();
        }
    }
}
