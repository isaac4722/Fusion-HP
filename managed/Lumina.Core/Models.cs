// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Models.cs : clases C# espejo de native/core/src/Models.h (contrato fijo del
//  núcleo). MAPEO documentado por campo; los nombres JSON siguen el esquema del
//  núcleo (docs/architecture-hybrid.md §5):
//
//    Models.h (C++)                 aquí (C#)                JSON
//    -----------------------------------------------------------------------
//    Slide::kind (SLIDE_*)          Models.SlideKind         "kind" (int)
//    SlideLine {text, chords}       Models.SlideLine         "lines": [ str | {"text","chords"} ]
//    Slide {kind,title,refLabel,
//           lines,imagePath}        Models.Slide             (solo lectura: vía SongParse)
//    Theme {name,bgColor,…}         Models.Theme             objeto tema (Theme::ToJson)
//    SongBlock {label,lines,repeat} Models.SongBlock         "blocks":[{"label","lines","repeat"}]
//    Song {id,title,artist,keyName,
//          tags,lyrics,bpm,
//          latinChords,chorusInterleave,
//          titleSlide,endBlank,stripChords,
//          maxLinesPerSlide,transpose,
//          blocks}                  Models.Song             {"type","title","artist","key","bpm","tags","blocks"| "lyrics", + BuildOptions}
//    ScenarioItem {kind,title,song,
//                  ref,version,text,
//                  imagePath,maxLinesPerSlide}  Models.ScenarioItem  {"kind","title","song","ref","version","text","imagePath","maxLinesPerSlide"}
//
//  Aquí NO se duplica lógica del núcleo (construcción de slides, trasposición…):
//  son contenedores de datos para el editor y el builder de escenarios.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using lumina.core;

namespace lumina.core
{
    /// <summary>Especie de slide — espejo del enum SlideKind de Models.h.</summary>
    public static class SlideKind
    {
        public const int Title    = 0;   // SLIDE_TITLE: título/artista/tono
        public const int Text     = 1;   // SLIDE_TEXT: letra/contenido
        public const int Blank    = 2;   // SLIDE_BLANK: en blanco
        public const int Scripture= 3;   // SLIDE_SCRIPTURE: texto bíblico con referencia
        public const int Image    = 4;   // SLIDE_IMAGE: imagen con texto opcional
    }

    /// <summary>Línea de slide con cifrado opcional — espejo de SlideLine (Models.h).</summary>
    public sealed class SlideLine
    {
        public string Text = string.Empty;   // text
        public string Chords = string.Empty; // chords (puede ser vacío)

        public SlideLine() {}
        public SlideLine(string text) { Text = text ?? string.Empty; }
        public SlideLine(string text, string chords) { Text = text ?? string.Empty; Chords = chords ?? string.Empty; }
    }

    /// <summary>Slide ya construida (lectura, p. ej. desde lumina_song_parse) — espejo de Slide.</summary>
    public sealed class Slide
    {
        public int Kind = SlideKind.Text;    // kind
        public string Title = string.Empty;  // title
        public string RefLabel = string.Empty; // refLabel ("Verso 1", "Juan 3:16"…)
        public List<SlideLine> Lines = new List<SlideLine>(); // lines
        public string ImagePath = string.Empty;               // imagePath (solo IMAGE)

        /// <summary>Interpreta el JSON de una slide emitido por el núcleo (SlideToJson).</summary>
        public static Slide FromDict(Dictionary<string, object> o)
        {
            Slide s = new Slide();
            s.Kind = (int)MiniJson.GetInt(o, "kind", SlideKind.Text);
            s.Title = MiniJson.GetString(o, "title", string.Empty);
            s.RefLabel = MiniJson.GetString(o, "refLabel", string.Empty);
            foreach (object lo in MiniJson.GetArray(o, "lines"))
            {
                Dictionary<string, object> ld = lo as Dictionary<string, object>;
                if (ld != null)
                    s.Lines.Add(new SlideLine(MiniJson.GetString(ld, "text", string.Empty),
                                              MiniJson.GetString(ld, "chords", string.Empty)));
                else if (lo is string)
                    s.Lines.Add(new SlideLine((string)lo));
            }
            s.ImagePath = MiniJson.GetString(o, "imagePath", string.Empty);
            return s;
        }
    }

    /// <summary>Tema visual — espejo de Theme (Models.h) con los mismos nombres JSON.</summary>
    public sealed class Theme
    {
        public string Name        = "Predeterminado"; // name
        public string BgColor     = "#FF0B1F2A";      // bgColor   (AARRGGBB)
        public string FgColor     = "#FFFFFFFF";      // fgColor
        public string AccentColor = "#FF3AA6B9";      // accentColor
        public string FontFace    = "Segoe UI";       // fontFace
        public int    FontSize    = 54;               // fontSize  (px @1080p lógico)
        public bool   Bold        = false;            // bold
        public string ImagePath   = string.Empty;     // imagePath (fondo opcional)
        public int    ImageMode   = 1;                // imageMode (0=contain 1=cover)
        public double OutlineWidth= 2.0;              // outlineWidth
        public int    ShadowAlpha = 140;              // shadowAlpha
        public bool   Uppercase   = false;            // uppercase
        public double LineSpacing = 1.18;             // lineSpacing

        public Dictionary<string, object> ToDict()
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["name"] = Name; o["bgColor"] = BgColor; o["fgColor"] = FgColor;
            o["accentColor"] = AccentColor; o["fontFace"] = FontFace; o["fontSize"] = FontSize;
            o["bold"] = Bold; o["imagePath"] = ImagePath; o["imageMode"] = ImageMode;
            o["outlineWidth"] = OutlineWidth; o["shadowAlpha"] = ShadowAlpha;
            o["uppercase"] = Uppercase; o["lineSpacing"] = LineSpacing;
            return o;
        }

        public static Theme FromDict(Dictionary<string, object> o)
        {
            Theme t = new Theme();
            if (o == null) return t;
            t.Name = MiniJson.GetString(o, "name", t.Name);
            t.BgColor = MiniJson.GetString(o, "bgColor", t.BgColor);
            t.FgColor = MiniJson.GetString(o, "fgColor", t.FgColor);
            t.AccentColor = MiniJson.GetString(o, "accentColor", t.AccentColor);
            t.FontFace = MiniJson.GetString(o, "fontFace", t.FontFace);
            t.FontSize = (int)MiniJson.GetInt(o, "fontSize", t.FontSize);
            t.Bold = MiniJson.GetBool(o, "bold", t.Bold);
            t.ImagePath = MiniJson.GetString(o, "imagePath", t.ImagePath);
            t.ImageMode = (int)MiniJson.GetInt(o, "imageMode", t.ImageMode);
            t.OutlineWidth = MiniJson.GetDouble(o, "outlineWidth", t.OutlineWidth);
            t.ShadowAlpha = (int)MiniJson.GetInt(o, "shadowAlpha", t.ShadowAlpha);
            t.Uppercase = MiniJson.GetBool(o, "uppercase", t.Uppercase);
            t.LineSpacing = MiniJson.GetDouble(o, "lineSpacing", t.LineSpacing);
            return t;
        }
    }

    /// <summary>Bloque de letra — espejo de SongBlock (Models.h).</summary>
    public sealed class SongBlock
    {
        public string Label = string.Empty;            // label ("Verso 1", "Coro"…)
        public List<string> Lines = new List<string>(); // lines (pueden incluir cifrado)
        public int Repeat = 1;                          // repeat
    }

    /// <summary>Canción — espejo de Song (Models.h). Nombres JSON según §5 de la arquitectura.</summary>
    public sealed class Song
    {
        public string Id = string.Empty;       // id        (BD; vacío = nueva)
        public string Title = string.Empty;    // title
        public string Artist = string.Empty;   // artist
        public string KeyName = string.Empty;  // key       (tonalidad)
        public string Tags = string.Empty;     // tags
        public string Lyrics = string.Empty;   // lyrics    (crudo si no hay blocks)
        public int Bpm = 0;                    // bpm
        public bool LatinChords = true;        // latinChords (cifrado latina/anglosajona)
        public bool ChorusInterleave = false;  // chorusInterleave («Modo Hinario»)
        public bool TitleSlide = true;         // titleSlide
        public bool EndBlank = false;          // endBlank
        public bool StripChords = true;        // stripChords
        public int MaxLinesPerSlide = 4;       // maxLinesPerSlide
        public int Transpose = 0;              // transpose (semitonos)
        public List<SongBlock> Blocks = new List<SongBlock>(); // blocks (manda sobre lyrics)

        /// <summary>Letra cruda desde blocks (espejo de Song::LyricsText).</summary>
        public string LyricsText()
        {
            if (Blocks.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (SongBlock b in Blocks)
                {
                    if (!string.IsNullOrEmpty(b.Label)) sb.Append('[').Append(b.Label).Append("]\n");
                    foreach (string l in b.Lines) sb.Append(l).Append('\n');
                    sb.Append('\n');
                }
                return sb.ToString();
            }
            return Lyrics;
        }
    }

    /// <summary>Ítem de escenario — espejo de ScenarioItem (Models.h).</summary>
    public sealed class ScenarioItem
    {
        // kind: "song" | "scripture" | "blank" | "image" | "text"
        public string Kind = "blank";
        public string Title = string.Empty;
        public Song Song;                 // kind=song (objeto anidado "song" en JSON)
        public string Ref = string.Empty; // kind=scripture ("Jn 3:16-18")
        public string Version = string.Empty; // kind=scripture: versión bíblica (si hay BD)
        public string Text = string.Empty;    // kind=text/scripture crudo (versos \n)
        public string ImagePath = string.Empty; // kind=image
        public int MaxLinesPerSlide = 4;        // kind=text
        /// <summary>
        /// v6.0.0 «HORIZONTE»: palabra/frase a RESALTAR EN PROYECCIÓN (color de
        /// acento). El núcleo la propaga a las slides de text/scripture/image;
        /// típico: el término de la búsqueda bíblica que originó el ítem.
        /// Cadena vacía = sin resaltado.
        /// </summary>
        public string Highlight = string.Empty;
        /// <summary>
        /// v6.0.0 «HORIZONTE»: notas del DIRECTOR para este ítem (avisos internos
        /// del servicio — no se proyectan). Persisten en el plan JSON del culto
        /// («notes») y se muestran en la pantalla del director al llegar al ítem.
        /// </summary>
        public string Notes = string.Empty;
        /// <summary>
        /// v5.0.0: ruta del video (kind=video). Campo C#-side: el NÚCLEO lo
        /// ignora (kind desconocido → slide en blanco) y la UI lo intercepta
        /// para reproducirlo sobre la pantalla del proyector (VideoPlayerForm).
        /// </summary>
        public string VideoPath = string.Empty;
        /// <summary>v5.1.0: repetir el video en bucle (kind=video, UI-side).</summary>
        public bool VideoLoop = false;
        /// <summary>v5.1.0: volumen inicial del video 0..100 (kind=video, UI-side).</summary>
        public int VideoVolume = 100;
        // Reservado (el núcleo hoy fija 1 verso por slide en escritura vía
        // Scripture::BuildSlides; se emite para compatibilidad futura).
        public int VersesPerSlide = 1;
    }
}
