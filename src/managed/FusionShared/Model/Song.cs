// ============================================================================
//  Fusion-HP · FusionShared/Model/Song.cs — canción de la biblioteca
//  Estructura alineada con el himnario JSON de la referencia y con la
//  exportación de Holyrics [SPEC §9.4]: párrafos con descripción (Verso/Coro).
// ============================================================================
using System;
using System.Collections.Generic;

namespace Fusion.Shared.Model
{
    public class SongSection
    {
        public string Name = "Verso";
        public List<string> Lines = new List<string>();
    }

    public class Song
    {
        public string Id;
        public string Title = "";
        public string Artist = "";
        public string Author = "";
        public string Note = "";
        public string Copyright = "";
        public string Language = "es";
        public string Key_ = "";
        public double Bpm;
        public List<SongSection> Sections = new List<SongSection>();
        public List<string> Tags = new List<string>();
        public int UseCount;             // historial de uso [SPEC §7.2.1]
        public DateTime LastUsed;

        /// <summary>Convierte a Escenario proyectable (una sección = un Elemento texto).</summary>
        public Scenario ToScenario()
        {
            var sc = new Scenario();
            sc.Id = "scn-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            sc.Title = Title;
            foreach (var t in Tags) sc.Tags.Add(t);
            foreach (var sec in Sections)
            {
                var el = new Element();
                el.Id = "el-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                el.Kind = ElementKind.Text;
                el.Lines = new List<string>(sec.Lines);
                el.Note = sec.Name;
                sc.Elements.Add(el);
            }
            return sc;
        }

        public Shared.JsonValue ToJson()
        {
            var j = Shared.JsonValue.Object();
            j.Set("id", Shared.JsonValue.Make(Id));
            j.Set("title", Shared.JsonValue.Make(Title));
            j.Set("artist", Shared.JsonValue.Make(Artist));
            j.Set("author", Shared.JsonValue.Make(Author));
            j.Set("note", Shared.JsonValue.Make(Note));
            j.Set("copyright", Shared.JsonValue.Make(Copyright));
            j.Set("language", Shared.JsonValue.Make(Language));
            j.Set("key", Shared.JsonValue.Make(Key_));
            j.Set("bpm", Shared.JsonValue.Make(Bpm));
            j.Set("useCount", Shared.JsonValue.Make(UseCount));
            j.Set("lastUsed", Shared.JsonValue.Make(LastUsed.ToString("yyyy-MM-ddTHH:mm:ss")));
            if (Tags.Count > 0) { var t = Shared.JsonValue.Array(); t.AddStrings(Tags); j.Set("tags", t); }
            var lyr = Shared.JsonValue.Object();
            var paras = Shared.JsonValue.Array();
            foreach (var s in Sections)
            {
                var p = Shared.JsonValue.Object();
                p.Set("description", Shared.JsonValue.Make(s.Name));
                var ln = Shared.JsonValue.Array();
                ln.AddStrings(s.Lines);
                p.Set("text", Shared.JsonValue.Make(string.Join("\n", s.Lines.ToArray())));
                p.Set("lines", ln);
                paras.Add(p);
            }
            lyr.Set("paragraphs", paras);
            j.Set("lyrics", lyr);
            return j;
        }

        public static Song FromJson(Shared.JsonValue j)
        {
            if (j == null || j.Type != Shared.JsonValue.Kind.Object) return null;
            var s = new Song();
            s.Id = j.GetStr("id", "song-" + Guid.NewGuid().ToString("N").Substring(0, 10));
            s.Title = j.GetStr("title", "");
            s.Artist = j.GetStr("artist");
            s.Author = j.GetStr("author");
            s.Note = j.GetStr("note");
            s.Copyright = j.GetStr("copyright");
            s.Language = j.GetStr("language", "es");
            s.Key_ = j.GetStr("key");
            s.Bpm = j.GetNum("bpm", 0);
            s.UseCount = j.GetInt("useCount", 0);
            s.Tags = j.GetStringArray("tags");
            var lyr = j.Get("lyrics");
            var paras = lyr.GetArray("paragraphs");
            if (paras != null)
            {
                foreach (var p in paras)
                {
                    var sec = new SongSection();
                    sec.Name = p.GetStr("description", "Verso");
                    var lines = p.GetArray("lines");
                    if (lines != null)
                        foreach (var l in lines) if (l.Type == Shared.JsonValue.Kind.String) sec.Lines.Add(l.Str);
                    if (sec.Lines.Count == 0)
                    {
                        string full = p.GetStr("text", "");
                        foreach (var ln in full.Split(new[] { '\n' }, StringSplitOptions.None))
                        {
                            var t = ln.Trim();
                            if (t.Length > 0) sec.Lines.Add(t);
                        }
                    }
                    if (sec.Lines.Count > 0) s.Sections.Add(sec);
                }
            }
            // Fallback: full_text por bloques
            if (s.Sections.Count == 0)
            {
                string full = lyr.GetStr("full_text", "");
                if (full.Length > 0)
                {
                    foreach (var block in full.Split(new[] { "\n\n" }, StringSplitOptions.None))
                    {
                        var sec = new SongSection { Name = "Parte " + (s.Sections.Count + 1) };
                        foreach (var ln in block.Split(new[] { '\n' }, StringSplitOptions.None))
                        {
                            var t = ln.Trim();
                            if (t.Length > 0) sec.Lines.Add(t);
                        }
                        if (sec.Lines.Count > 0) s.Sections.Add(sec);
                    }
                }
            }
            if (s.Title.Length == 0) return null;
            return s;
        }
    }
}
