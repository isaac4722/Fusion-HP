// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Import/HolyricsImporter.cs : compatibilidad con Holyrics (F4.14 del Plan
//  de Ultra Implementación — Sección 9.6).
//
//  Reglas del plan:
//   1. Importar biblioteca y listas JSON/XML.
//   2. Mapear letras a Texto Formateado (ahp.v1).
//   3. Mapear categorías a etiquetas.
//   4. Importar fondos (media).
//   5. Re-vincular rutas relativas.
//   6. Deduplicar por título y letra normalizada.
//   7. Confirmación ANTES de sobrescribir (callback de decisión).
//   8. NUNCA modificar la biblioteca local sin aceptación explícita.
//
//  Formatos aceptados (exportaciones reales de Holyrics):
//   JSON: {"songs":[{...}]} | [ {...}, ... ]  (canciones con "lyrics")
//   XML : <songs><song><title>…</title><lyrics>…</lyrics>…</song></songs>
//         (listas: <playlist><item>…</item></playlist> con referencias)
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using lumina.core;
using lumina.core.project;

namespace lumina.core.import
{
    /// <summary>Canción importada (antes de fusionar con la biblioteca).</summary>
    public sealed class HolyricsSong
    {
        public string Title = "";
        public string Artist = "";
        public string Lyrics = "";
        public List<string> Categories = new List<string>();
        public string Background = "";          // ruta del fondo exportado
        public string SourceFile = "";
    }

    /// <summary>Resultado de la importación (informe de fidelidad F4.15).</summary>
    public sealed class HolyricsReport
    {
        public int Imported, Duplicated, Skipped, BackgroundsLinked;
        public List<string> Warnings = new List<string>();
        public List<AhpLine> LinesBuilt;
    }

    /// <summary>
    /// Decisión de sobrescritura (F4.14.7-8): devuelva true para REEMPLAZAR la
    /// canción local existente; false la omite (contada como Skipped). Sin
    /// callback → NUNCA se sobrescribe (política por defecto).
    /// </summary>
    public delegate bool HolyricsOverwriteDecision(string title, string sourceFile);

    public static class HolyricsImporter
    {
        /// <summary>Lee la exportación (JSON o XML) a canciones crudas.</summary>
        public static List<HolyricsSong> Parse(string path)
        {
            string text = File.ReadAllText(path, DetectEncoding(path));
            if (Path.GetExtension(path).ToLowerInvariant() == ".xml" ||
                text.TrimStart().StartsWith("<", StringComparison.Ordinal))
                return ParseXml(text, path);
            return ParseJson(text, path);
        }

        private static Encoding DetectEncoding(string path)
        {
            // BOMs comunes; sin BOM se asume UTF-8 (exportaciones modernas).
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                int b0 = fs.ReadByte(), b1 = fs.ReadByte(), b2 = fs.ReadByte();
                if (b0 == 0xFF && b1 == 0xFE) return Encoding.Unicode;
                if (b0 == 0xFE && b1 == 0xFF) return Encoding.BigEndianUnicode;
                if (b0 == 0xEF && b1 == 0xBB && b2 == 0xBF) return Encoding.UTF8;
            }
            return Encoding.UTF8;
        }

        // ------------------------------------------------------------ JSON --
        private static List<HolyricsSong> ParseJson(string text, string srcFile)
        {
            List<HolyricsSong> outList = new List<HolyricsSong>();
            object any = MiniJson.ParseAny(text);
            List<object> arr = null;
            if (any is List<object>) arr = (List<object>)any;
            else if (any is Dictionary<string, object>)
            {
                Dictionary<string, object> o = (Dictionary<string, object>)any;
                arr = MiniJson.GetArray(o, "songs");
                if (arr == null) arr = MiniJson.GetArray(o, "items");
            }
            if (arr == null) return outList;
            foreach (object eo in arr)
            {
                Dictionary<string, object> m = eo as Dictionary<string, object>;
                if (m == null) continue;
                HolyricsSong s = new HolyricsSong();
                s.Title = MiniJson.GetString(m, "title",
                    MiniJson.GetString(m, "name", ""));
                s.Artist = MiniJson.GetString(m, "artist",
                    MiniJson.GetString(m, "author", ""));
                s.Lyrics = MiniJson.GetString(m, "lyrics",
                    MiniJson.GetString(m, "text", ""));
                // categorías: string o array
                object cat;
                if (m.TryGetValue("categories", out cat) ||
                    m.TryGetValue("category", out cat))
                {
                    List<object> cats = cat as List<object>;
                    if (cats != null)
                        foreach (object c in cats) s.Categories.Add(Convert.ToString(c));
                    else if (cat is string && ((string)cat).Length > 0)
                        s.Categories.Add((string)cat);
                }
                s.Background = MiniJson.GetString(m, "background",
                    MiniJson.GetString(m, "imagePath", ""));
                s.SourceFile = srcFile;
                if (s.Title.Length > 0 && s.Lyrics.Length > 0) outList.Add(s);
            }
            return outList;
        }

        // ------------------------------------------------------------- XML --
        private static List<HolyricsSong> ParseXml(string text, string srcFile)
        {
            List<HolyricsSong> outList = new List<HolyricsSong>();
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(text);
            XmlNodeList songs = doc.SelectNodes("//song");
            if (songs == null || songs.Count == 0)
                songs = doc.SelectNodes("//item");
            if (songs == null) return outList;
            foreach (XmlNode sn in songs)
            {
                HolyricsSong s = new HolyricsSong();
                s.Title = XmlText(sn, "title") ?? XmlText(sn, "name") ?? "";
                s.Artist = XmlText(sn, "artist") ?? XmlText(sn, "author") ?? "";
                s.Lyrics = XmlText(sn, "lyrics") ?? XmlText(sn, "text") ?? "";
                s.SourceFile = srcFile;
                XmlNodeList cats = sn.SelectNodes("categories/category");
                if (cats != null)
                    foreach (XmlNode c in cats)
                        if (!string.IsNullOrEmpty(c.InnerText)) s.Categories.Add(c.InnerText);
                s.Background = XmlText(sn, "background") ?? "";
                if (s.Title.Length > 0 && s.Lyrics.Length > 0) outList.Add(s);
            }
            return outList;
        }

        private static string XmlText(XmlNode parent, string tag)
        {
            XmlNode n = parent.SelectSingleNode(tag);
            return n == null ? null : n.InnerText;
        }

        // ------------------------------------------------- deduplicación --
        /// <summary>Clave normalizada (F4.14.6): minúsculas, sin acentos, sin
        /// espacios repetidos — título + letra.</summary>
        public static string DedupeKey(string title, string lyrics)
        {
            return Normalize(title) + "|" + Normalize(lyrics);
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            bool lastWs = true;
            foreach (char c in s.ToLowerInvariant())
            {
                char k = Fold(c);
                bool ws = char.IsWhiteSpace(k);
                if (ws) { if (!lastWs) sb.Append(' '); lastWs = true; }
                else { sb.Append(k); lastWs = false; }
            }
            return sb.ToString().Trim();
        }

        private static char Fold(char c)
        {
            switch (c)
            {
                case 'á': case 'à': case 'ä': case 'â': return 'a';
                case 'é': case 'è': case 'ë': case 'ê': return 'e';
                case 'í': case 'ì': case 'ï': case 'î': return 'i';
                case 'ó': case 'ò': case 'ö': case 'ô': return 'o';
                case 'ú': case 'ù': case 'ü': case 'û': return 'u';
                case 'ñ': return 'n';
                case 'ç': return 'c';
            }
            return c;
        }

        // ------------------------------------------------- conversión ahp --
        /// <summary>
        /// Convierte canciones Holyrics a elementos Texto Formateado de un
        /// proyecto ahp.v1 (F4.14.2-3: letras → texto, categorías → etiquetas).
        /// dedupe: claves EXISTENTES (de la biblioteca local) — un match se
        /// deduplica (o se pide confirmación). existingKeys puede ser null.
        /// </summary>
        public static HolyricsReport ToAhp(List<HolyricsSong> songs, AhpProject project,
                                           HashSet<string> existingKeys,
                                           string mediaSourceDir, string mediaTargetDir,
                                           HolyricsOverwriteDecision confirm)
        {
            HolyricsReport rep = new HolyricsReport();
            rep.LinesBuilt = new List<AhpLine>();
            if (songs == null) return rep;
            HashSet<string> seen = new HashSet<string>();
            AhpScenario sc = project.NewScenario("Importación Holyrics");
            foreach (HolyricsSong s in songs)
            {
                string key = DedupeKey(s.Title, s.Lyrics);
                if (seen.Contains(key))
                {
                    rep.Duplicated++;
                    continue;                       // duplicado DENTRO del lote
                }
                seen.Add(key);
                if (existingKeys != null && existingKeys.Contains(key))
                {
                    // F4.14.7-8: confirmación ANTES de sobrescribir.
                    if (confirm == null || !confirm(s.Title, s.SourceFile))
                    {
                        rep.Skipped++;              // no se toca la biblioteca local
                        rep.Warnings.Add("Omitida (ya existe y no se autorizó sobrescribir): " + s.Title);
                        continue;
                    }
                    rep.Warnings.Add("Sobrescrita con autorización: " + s.Title);
                }
                AhpElement e = project.NewElement(AhpElementKind.Text);
                e.Title = s.Title;
                // Letra → líneas: los bloques [Coro]/(puente) se conservan
                // como líneas; una línea vacía NO corta (el elemento es una
                // sola canción — la sincronización es por línea).
                int mark = 1; bool pendingMark = false;
                foreach (string raw in s.Lyrics.Replace("\r\n", "\n").Split('\n'))
                {
                    string ln = raw.Trim();
                    if (ln.Length == 0) { pendingMark = true; continue; }
                    AhpLine al = new AhpLine { Text = ln };
                    if (pendingMark || mark == 1) { al.SyncMark = mark; pendingMark = false; }
                    else al.SyncMark = mark;
                    e.Lines.Add(al);
                    mark++;
                }
                // F4.14.3: categorías → etiquetas.
                foreach (string c in s.Categories) e.Tags.Add(c);
                // F4.14.4-5: fondo vinculado con ruta relativa (media/).
                if (!string.IsNullOrEmpty(s.Background))
                {
                    string linked = LinkBackground(s.Background, mediaSourceDir,
                        mediaTargetDir, project, rep);
                    if (linked.Length > 0) e.ImagePath = linked;
                }
                sc.Elements.Add(e);
                rep.Imported++;
            }
            if (sc.Elements.Count > 0) project.Scenarios.Add(sc);
            return rep;
        }

        // Copia el fondo a media/ del proyecto y devuelve la ruta RELATIVA
        // (F4.14.5: re-vinculación con rutas relativas).
        private static string LinkBackground(string bg, string srcDir, string targetDir,
                                             AhpProject project, HolyricsReport rep)
        {
            try
            {
                string full = Path.IsPathRooted(bg) ? bg : Path.Combine(srcDir ?? "", bg);
                if (!File.Exists(full)) return "";
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                string name = "media/" + Path.GetFileName(full);
                string dest = Path.Combine(targetDir, Path.GetFileName(full));
                File.Copy(full, dest, true);
                AhpMediaEntry me = new AhpMediaEntry();
                me.Path = name; me.Kind = "image";
                me.Size = new FileInfo(dest).Length;
                project.Media.Add(me);
                rep.BackgroundsLinked++;
                return name;
            }
            catch (IOException)
            {
                rep.Warnings.Add("Fondo no copiado: " + bg);
                return "";
            }
        }
    }
}
