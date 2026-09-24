// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ScenarioBuilder : construye el JSON de escenario que acepta el núcleo
//  (Engine::LoadScenario) a partir de los modelos C#. La CONSTRUCCIÓN de slides
//  reales vive en el núcleo (Lyrics::BuildSlides / Scripture::BuildSlides) —
//  aquí solo se serializa el contrato {"name","theme",…,"items":[…]}.
//
//  Además ofrece FlattenScenario(): aplana un escenario a una lista de vistas
//  de slide (para la UI y para /api/live.txt) usando lumina_song_parse cuando
//  el llamador provee el parseador de canciones (evita duplicar Lyrics).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using lumina.core;

namespace lumina.core
{
    /// <summary>Vista plana de una slide para listas de UI y live.txt.</summary>
    public sealed class SlideView
    {
        public int Index;                       // índice físico (0..N-1)
        public string Title = string.Empty;     // título del ítem
        public string RefLabel = string.Empty;  // etiqueta ("Verso 1", "Juan 3:16"…)
        public string FirstLine = string.Empty; // primera línea visible (o vacía)
        public List<string> Lines = new List<string>();
        /// <summary>v5.0.0: ruta del video si la slide lo representa ("video").</summary>
        public string VideoPath = string.Empty;
        /// <summary>v5.0.0: true si esta vista es un ítem de video.</summary>
        public bool IsVideo;
        /// <summary>
        /// v7.1.0 «OPERADOR»: índice del ScenarioItem que originó esta slide
        /// (para el modo Operador: lista de ítems + slides por ítem, estilo
        /// Holyrics). -1 si no se conoce.
        /// </summary>
        public int ItemIndex = -1;
        /// <summary>v7.1.0: true si esta vista es un ítem PPTX original.</summary>
        public bool IsPptx;
        /// <summary>v7.1.0: ruta del PPTX original (si IsPptx).</summary>
        public string PptxPath = string.Empty;
        /// <summary>v7.1.0: elementos del lienzo libre (slide compuesta).</summary>
        public List<ComposedElement> Composed;
    }

    public static class ScenarioBuilder
    {
        /* ------------------------------------------------- canción (editor) -- */

        /// <summary>Convierte el texto del editor ([Verso 1] / [Coro]…) en bloques.</summary>
        public static List<SongBlock> ParseLyricsBlocks(string raw)
        {
            List<SongBlock> blocks = new List<SongBlock>();
            SongBlock cur = null;
            if (raw == null) return blocks;
            string[] lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i] != null ? lines[i].Trim() : string.Empty;
                if (l.Length == 0) continue;
                if (l[0] == '[' && l[l.Length - 1] == ']' && l.Length > 2)
                {
                    cur = new SongBlock();
                    cur.Label = l.Substring(1, l.Length - 2).Trim();
                    blocks.Add(cur);
                    continue;
                }
                if (cur == null) { cur = new SongBlock(); cur.Label = "Verso 1"; blocks.Add(cur); }
                cur.Lines.Add(l);
            }
            return blocks;
        }

        /// <summary>Arma una Song desde el editor (título/artista/letra cruda/modo hinario).</summary>
        public static Song SongFromEditor(string title, string artist, string rawLyrics,
                                          bool hymnMode, int transpose)
        {
            Song s = new Song();
            s.Title = title != null ? title.Trim() : string.Empty;
            s.Artist = artist != null ? artist.Trim() : string.Empty;
            s.Lyrics = rawLyrics ?? string.Empty;
            s.Blocks = ParseLyricsBlocks(s.Lyrics);
            s.ChorusInterleave = hymnMode;
            s.Transpose = transpose;
            return s;
        }

        /* ------------------------------------------------------ modelo→JSON -- */

        /// <summary>Song → dict JSON (esquema del núcleo; ver Models.cs para el mapeo).</summary>
        public static Dictionary<string, object> SongToDict(Song s)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["type"] = "song";                      // marca del esquema propio
            o["title"] = s.Title;
            o["artist"] = s.Artist;
            o["key"] = s.KeyName;
            o["bpm"] = s.Bpm;
            o["tags"] = s.Tags;
            o["lyrics"] = s.LyricsText();            // crudo ( blocks manda sobre lyrics )
            // BuildOptions tomadas del propio JSON por SongModel::ParseAndBuildSlides:
            o["titleSlide"] = s.TitleSlide;
            o["endBlank"] = s.EndBlank;
            o["chorusInterleave"] = s.ChorusInterleave;
            o["maxLinesPerSlide"] = s.MaxLinesPerSlide;
            o["stripChords"] = s.StripChords;
            o["latinChords"] = s.LatinChords;
            o["transpose"] = s.Transpose;
            List<object> blocks = new List<object>();
            foreach (SongBlock b in s.Blocks)
            {
                Dictionary<string, object> bd = new Dictionary<string, object>();
                bd["label"] = b.Label;
                bd["lines"] = new List<object>(b.Lines.ToArray());
                bd["repeat"] = b.Repeat;
                blocks.Add(bd);
            }
            if (blocks.Count > 0) o["blocks"] = blocks;
            return o;
        }

        /// <summary>Dict JSON de canción → Song (tolerante: campos faltantes = default).</summary>
        public static Song SongFromDict(Dictionary<string, object> o)
        {
            Song s = new Song();
            if (o == null) return s;
            s.Id = MiniJson.GetString(o, "id", string.Empty);
            s.Title = MiniJson.GetString(o, "title", string.Empty);
            s.Artist = MiniJson.GetString(o, "artist", string.Empty);
            s.KeyName = MiniJson.GetString(o, "key", string.Empty);
            s.Tags = MiniJson.GetString(o, "tags", string.Empty);
            s.Lyrics = MiniJson.GetString(o, "lyrics", string.Empty);
            s.Bpm = (int)MiniJson.GetInt(o, "bpm", 0);
            s.LatinChords = MiniJson.GetBool(o, "latinChords", true);
            s.ChorusInterleave = MiniJson.GetBool(o, "chorusInterleave", false);
            s.TitleSlide = MiniJson.GetBool(o, "titleSlide", true);
            s.EndBlank = MiniJson.GetBool(o, "endBlank", false);
            s.StripChords = MiniJson.GetBool(o, "stripChords", true);
            s.MaxLinesPerSlide = (int)MiniJson.GetInt(o, "maxLinesPerSlide", 4);
            s.Transpose = (int)MiniJson.GetInt(o, "transpose", 0);
            foreach (object bo in MiniJson.GetArray(o, "blocks"))
            {
                Dictionary<string, object> bd = bo as Dictionary<string, object>;
                if (bd == null) continue;
                SongBlock b = new SongBlock();
                b.Label = MiniJson.GetString(bd, "label", string.Empty);
                b.Repeat = (int)MiniJson.GetInt(bd, "repeat", 1);
                foreach (object lo in MiniJson.GetArray(bd, "lines"))
                    b.Lines.Add(lo is string ? (string)lo : Convert.ToString(lo, CultureInfo.InvariantCulture));
                s.Blocks.Add(b);
            }
            return s;
        }

        /* ------------------------------------------------------- ítem→JSON -- */

        /// <summary>ScenarioItem → dict JSON (contrato de Engine::LoadScenario).</summary>
        public static Dictionary<string, object> ItemToDict(ScenarioItem it)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["kind"] = it.Kind;
            o["title"] = it.Title;
            // v6.0.0: resaltado en proyección (el núcleo lo propaga a las slides).
            if (!string.IsNullOrEmpty(it.Highlight)) o["highlight"] = it.Highlight;
            // v6.0.0: notas del director (UI-side; el núcleo las ignora).
            if (!string.IsNullOrEmpty(it.Notes)) o["notes"] = it.Notes;
            if (it.Kind == "song" && it.Song != null)
                o["song"] = SongToDict(it.Song);   // objeto anidado (LoadScenario lo lee)
            if (it.Kind == "scripture")
            {
                o["ref"] = it.Ref;
                o["version"] = it.Version;
                if (!string.IsNullOrEmpty(it.Text)) o["text"] = it.Text;
                o["versesPerSlide"] = it.VersesPerSlide; // v5.2.0: el núcleo lo honra (agrupa versos/slide)
            }
            if (it.Kind == "text")
            {
                if (!string.IsNullOrEmpty(it.Text)) o["text"] = it.Text;
                o["maxLinesPerSlide"] = it.MaxLinesPerSlide;
            }
            if (it.Kind == "image")
            {
                o["imagePath"] = it.ImagePath;
                if (!string.IsNullOrEmpty(it.Text)) o["text"] = it.Text;
            }
            if (it.Kind == "video")
            {
                // v5.0.0: el núcleo ignora "videoPath" (kind desconocido → blank);
                // la UI lo usa para reproducir el video sobre la salida.
                o["videoPath"] = it.VideoPath;
                // v5.1.0: opciones de reproducción (UI-side; el núcleo las ignora).
                o["videoLoop"] = it.VideoLoop;
                o["videoVolume"] = it.VideoVolume;
            }
            // v7.1.0 «OPERADOR» (feedback #6): PPTX ORIGINAL sin extracción —
            // solo la ruta del archivo (la proyección la entrega PowerPoint).
            if (it.Kind == "pptx") o["pptxPath"] = it.PptxPath;
            // v7.1.0 «OPERADOR» (feedback #1/#8): slide COMPUESTA del lienzo
            // libre — elementos posicionables 1:1 (el núcleo los dibuja).
            if (it.Kind == "composed" && it.Composed != null && it.Composed.Count > 0)
            {
                List<object> els = new List<object>(it.Composed.Count);
                foreach (ComposedElement el in it.Composed) els.Add(el.ToDict());
                o["elements"] = els;
            }
            // "blank": solo kind+title
            return o;
        }

        /// <summary>Construye el JSON completo del escenario.</summary>
        public static string BuildScenarioJson(string name, Theme theme, IList<ScenarioItem> items)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["name"] = name ?? string.Empty;
            o["theme"] = (theme ?? new Theme()).ToDict();
            List<object> arr = new List<object>();
            if (items != null)
                foreach (ScenarioItem it in items) arr.Add(ItemToDict(it));
            o["items"] = arr;
            return MiniJson.Serialize(o);
        }

        /// <summary>Atajos de fábrica de ítems.</summary>
        public static ScenarioItem FromSong(Song s)
        {
            ScenarioItem it = new ScenarioItem();
            it.Kind = "song";
            it.Title = s.Title;
            it.Song = s;
            return it;
        }

        public static ScenarioItem ScriptureItem(string @ref, string version, int versesPerSlide, string versesText)
        {
            ScenarioItem it = new ScenarioItem();
            it.Kind = "scripture";
            it.Title = @ref ?? string.Empty;
            it.Ref = @ref ?? string.Empty;
            it.Version = version ?? string.Empty;
            it.VersesPerSlide = versesPerSlide;
            it.Text = versesText ?? string.Empty;
            return it;
        }

        public static ScenarioItem TextItem(string title, string text, int maxLinesPerSlide)
        {
            ScenarioItem it = new ScenarioItem();
            it.Kind = "text";
            it.Title = title ?? string.Empty;
            it.Text = text ?? string.Empty;
            it.MaxLinesPerSlide = maxLinesPerSlide <= 0 ? 4 : maxLinesPerSlide;
            return it;
        }

        public static ScenarioItem ImageItem(string title, string imagePath, string caption)
        {
            ScenarioItem it = new ScenarioItem();
            it.Kind = "image";
            it.Title = title ?? string.Empty;
            it.ImagePath = imagePath ?? string.Empty;
            it.Text = caption ?? string.Empty;
            return it;
        }

        /// <summary>v5.0.0: ítem de video (reproducción en la salida desde la UI).</summary>
        public static ScenarioItem VideoItem(string title, string videoPath)
        {
            ScenarioItem it = new ScenarioItem();
            it.Kind = "video";
            it.Title = title ?? string.Empty;
            it.VideoPath = videoPath ?? string.Empty;
            return it;
        }

        /// <summary>v5.1.0: ítem de video con opciones de reproducción.</summary>
        public static ScenarioItem VideoItem(string title, string videoPath, bool loop, int volume0to100)
        {
            ScenarioItem it = VideoItem(title, videoPath);
            it.VideoLoop = loop;
            it.VideoVolume = Math.Max(0, Math.Min(100, volume0to100));
            return it;
        }

        public static ScenarioItem BlankItem(string title)
        {
            ScenarioItem it = new ScenarioItem();
            it.Kind = "blank";
            it.Title = title ?? string.Empty;
            return it;
        }

        /// <summary>
        /// v7.1.0 «OPERADOR» (feedback #1/#8): ítem COMPUESTO — una slide del
        /// editor de lienzo libre con sus elementos posicionables.
        /// </summary>
        public static ScenarioItem ComposedItem(string title, List<ComposedElement> elements)
        {
            ScenarioItem it = new ScenarioItem();
            it.Kind = "composed";
            it.Title = title ?? string.Empty;
            it.Composed = elements ?? new List<ComposedElement>();
            return it;
        }

        /// <summary>
        /// v7.1.0 «OPERADOR» (feedback #6): ítem PPTX ORIGINAL — el archivo se
        /// proyecta vía PowerPoint COM, SIN extracción previa.
        /// </summary>
        public static ScenarioItem PptxItem(string title, string path)
        {
            ScenarioItem it = new ScenarioItem();
            it.Kind = "pptx";
            it.Title = title ?? string.Empty;
            it.PptxPath = path ?? string.Empty;
            return it;
        }

        /* ---------------------------------------- aplanado para UI/live.txt -- */

        /// <summary>
        /// Aplana un escenario JSON a vistas de slide. Los ítems "song" se resuelven
        /// con <paramref name="songParse"/> (debe ser Bridge.SongParse: JSON→JSON con
        /// {"slides":[…]}) para NO duplicar Lyrics::BuildSlides en C#. Si el parser
        /// falla o es null, se usan las líneas crudas del bloque como respaldo.
        /// Los ítems "scripture" resueltos por la BD del núcleo no tienen texto aquí:
        /// quedan con refLabel y sin líneas (la vista en vivo del motor es la verdad).
        /// </summary>
        public static List<SlideView> FlattenScenario(string scenarioJson, Func<string, string> songParse)
        {
            List<SlideView> views = new List<SlideView>();
            Dictionary<string, object> root;
            try { root = MiniJson.Parse(scenarioJson); }
            catch (FormatException) { return views; }

            int itemNumber = 0;
            foreach (object io in MiniJson.GetArray(root, "items"))
            {
                Dictionary<string, object> item = io as Dictionary<string, object>;
                if (item == null) { itemNumber++; continue; }
                string kind = MiniJson.GetString(item, "kind", "blank");
                string title = MiniJson.GetString(item, "title", string.Empty);
                List<string> lines = new List<string>();
                string refLabel = string.Empty;

                if (kind == "song")
                {
                    Dictionary<string, object> song = MiniJson.GetObject(item, "song");
                    if (song == null) song = item; // canción inline (campos al nivel del ítem)
                    string songJson = null;
                    if (songParse != null)
                    {
                        try { songJson = songParse(MiniJson.Serialize(song)); }
                        catch (Exception) { songJson = null; }
                    }
                    bool resolved = false;
                    if (songJson != null)
                    {
                        Dictionary<string, object> res;
                        try { res = MiniJson.Parse(songJson); }
                        catch (FormatException) { res = null; }
                        if (res != null && MiniJson.GetInt(res, "ok", 0) == 1)
                        {
                            foreach (object so in MiniJson.GetArray(res, "slides"))
                            {
                                Slide sl = Slide.FromDict(so as Dictionary<string, object>);
                                if (sl == null) continue;
                                views.Add(MakeView(views.Count, itemNumber, title, sl.RefLabel, LinesToTexts(sl.Lines)));
                            }
                            resolved = true;
                        }
                    }
                    if (!resolved)
                    {
                        // Respaldo: líneas crudas de los bloques (sin partir slides).
                        Song s = SongFromDict(song);
                        foreach (SongBlock b in s.Blocks)
                            lines.AddRange(b.Lines);
                        if (lines.Count == 0 && s.Lyrics.Length > 0)
                            lines.AddRange(s.Lyrics.Replace("\r\n", "\n").Split('\n'));
                        views.Add(MakeView(views.Count, itemNumber, title, string.Empty, lines));
                    }
                    itemNumber++;
                    continue;
                }

                if (kind == "scripture")
                {
                    refLabel = MiniJson.GetString(item, "ref", string.Empty);
                    string text = MiniJson.GetString(item, "text", string.Empty);
                    if (text.Length > 0)
                        lines.AddRange(text.Replace("\r\n", "\n").Split('\n'));
                    // v5.2.0: agrupar por versículosPorSlide — igual que el
                    // motor (que ahora HONRA el campo): la lista «En vivo» y la
                    // proyección quedan ALINEADAS en número de slides.
                    int per = (int)MiniJson.GetInt(item, "versesPerSlide", 1);
                    if (per < 1) per = 1;
                    if (lines.Count == 0)
                    {
                        // sin texto (sin BD): una entrada informativa con la referencia
                        views.Add(MakeView(views.Count, itemNumber, title, refLabel, lines));
                    }
                    else
                    {
                        for (int i = 0; i < lines.Count; i += per)
                        {
                            List<string> group = new List<string>(per);
                            for (int k = i; k < i + per && k < lines.Count; k++)
                                group.Add(lines[k]);
                            views.Add(MakeView(views.Count, itemNumber, title, refLabel, group));
                        }
                    }
                    itemNumber++;
                    continue;
                }

                if (kind == "text")
                {
                    string text = MiniJson.GetString(item, "text", string.Empty);
                    if (text.Length == 0)
                    {
                        // variante "lines":[…]
                        foreach (object lo in MiniJson.GetArray(item, "lines"))
                            lines.Add(lo is string ? (string)lo : Convert.ToString(lo, CultureInfo.InvariantCulture));
                    }
                    else lines.AddRange(text.Replace("\r\n", "\n").Split('\n'));
                    refLabel = "texto";
                    views.Add(MakeView(views.Count, itemNumber, title, refLabel, lines));
                    itemNumber++;
                    continue;
                }

                if (kind == "image")
                {
                    string text = MiniJson.GetString(item, "text", string.Empty);
                    if (text.Length > 0) lines.AddRange(text.Replace("\r\n", "\n").Split('\n'));
                    views.Add(MakeView(views.Count, itemNumber, title, "imagen", lines));
                    itemNumber++;
                    continue;
                }

                if (kind == "video")
                {
                    // v5.0.0: slide única representando el video (el núcleo la
                    // proyecta en blanco; la UI reproduce el archivo encima).
                    SlideView v = MakeView(views.Count, itemNumber, title, "video", lines);
                    v.VideoPath = MiniJson.GetString(item, "videoPath", string.Empty);
                    v.IsVideo = true;
                    views.Add(v);
                    itemNumber++;
                    continue;
                }

                if (kind == "pptx")
                {
                    // v7.1.0 «OPERADOR» (feedback #6): PPTX ORIGINAL — slide
                    // marcador; la proyección real la entrega PowerPoint COM.
                    SlideView v = MakeView(views.Count, itemNumber, title,
                        "presentación", lines);
                    v.PptxPath = MiniJson.GetString(item, "pptxPath", string.Empty);
                    v.IsPptx = true;
                    if (v.PptxPath.Length > 0)
                    {
                        v.Lines.Add(Path.GetFileName(v.PptxPath));
                        v.FirstLine = Path.GetFileName(v.PptxPath);
                    }
                    views.Add(v);
                    itemNumber++;
                    continue;
                }

                if (kind == "composed")
                {
                    // v7.1.0 «OPERADOR» (feedback #1): slide COMPUESTA del lienzo
                    // libre — los elementos viajan completos para la vista de
                    // texto del operador (mini-preview sin render PNG).
                    List<ComposedElement> els = new List<ComposedElement>();
                    foreach (object eo in MiniJson.GetArray(item, "elements"))
                    {
                        ComposedElement el = ComposedElement.FromDict(eo as Dictionary<string, object>);
                        if (el != null) els.Add(el);
                    }
                    SlideView v = MakeView(views.Count, itemNumber, title,
                        "compuesta", lines);
                    v.Composed = els;
                    // Vista de texto del operador: líneas de los elementos de texto.
                    foreach (ComposedElement el in els)
                        if (el.Kind == ComposedElement.KindText)
                            foreach (string l in el.Lines) v.Lines.Add(l);
                    if (v.Lines.Count > 0 && v.FirstLine.Length == 0)
                        v.FirstLine = v.Lines[0];
                    views.Add(v);
                    itemNumber++;
                    continue;
                }

                // blank
                views.Add(MakeView(views.Count, itemNumber, title, "en blanco", lines));
                itemNumber++;
            }
            return views;
        }

        private static List<string> LinesToTexts(IList<SlideLine> lines)
        {
            List<string> texts = new List<string>();
            if (lines != null)
                foreach (SlideLine l in lines) texts.Add(l == null ? string.Empty : l.Text);
            return texts;
        }

        private static SlideView MakeView(int index, int itemIndex, string title, string refLabel, List<string> lines)
        {
            SlideView v = new SlideView();
            v.Index = index;
            v.ItemIndex = itemIndex;
            v.Title = title ?? string.Empty;
            v.RefLabel = refLabel ?? string.Empty;
            if (lines != null)
            {
                v.Lines = lines;
                for (int i = 0; i < lines.Count; i++)
                {
                    string t = lines[i];
                    if (t == null) continue;
                    t = t.Trim();
                    if (t.Length > 0) { v.FirstLine = t; break; }
                }
            }
            return v;
        }
    }
}
