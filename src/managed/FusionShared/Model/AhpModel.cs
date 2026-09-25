// ============================================================================
//  Fusion-HP · FusionShared/Model/AhpModel.cs — modelo de datos ahp.v1
//  [SPEC §5]: Proyecto → Escenarios → Elementos (5 tipos) + Temas con
//  herencia de 4 niveles Tema → Plantilla → Escenario → Elemento [SPEC §5.4].
//  IDs estables entre sesiones [SPEC §5.3c]; campos ignorables hacia atrás.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;

namespace Fusion.Shared.Model
{
    // ---------------------------------------------------------------- estilo
    /// <summary>Estilo visual parcial: null = hereda del nivel superior.</summary>
    public class StyleOverride
    {
        public string Font;
        public double? Size;
        public bool? Bold, Italic, Shadow, Outline;
        public string Color;          // "#RRGGBB"
        public string ActiveColor;    // color de línea activa [SPEC §6.2.3]
        public int? Align;            // 0 izq 1 centro 2 der
        public int? VAlign;           // 0 arriba 1 medio
        public double? LineSpacing;
        public double? BoxX, BoxY, BoxW, BoxH;   // fracciones

        public StyleOverride Clone()
        {
            return (StyleOverride)MemberwiseClone();
        }

        /// <summary>Cascada: lo no nulo de 'upper' se aplica donde 'this' es nulo.</summary>
        public void InheritFrom(StyleOverride upper)
        {
            if (upper == null) return;
            if (Font == null) Font = upper.Font;
            if (Size == null) Size = upper.Size;
            if (Bold == null) Bold = upper.Bold;
            if (Italic == null) Italic = upper.Italic;
            if (Shadow == null) Shadow = upper.Shadow;
            if (Outline == null) Outline = upper.Outline;
            if (Color == null) Color = upper.Color;
            if (ActiveColor == null) ActiveColor = upper.ActiveColor;
            if (Align == null) Align = upper.Align;
            if (VAlign == null) VAlign = upper.VAlign;
            if (LineSpacing == null) LineSpacing = upper.LineSpacing;
            if (BoxX == null) BoxX = upper.BoxX;
            if (BoxY == null) BoxY = upper.BoxY;
            if (BoxW == null) BoxW = upper.BoxW;
            if (BoxH == null) BoxH = upper.BoxH;
        }

        public Shared.JsonValue ToJson()
        {
            var j = Shared.JsonValue.Object();
            if (Font != null) j.Set("font", Shared.JsonValue.Make(Font));
            if (Size != null) j.Set("size", Shared.JsonValue.Make(Size.Value));
            if (Bold != null) j.Set("bold", Shared.JsonValue.Make(Bold.Value));
            if (Italic != null) j.Set("italic", Shared.JsonValue.Make(Italic.Value));
            if (Shadow != null) j.Set("shadow", Shared.JsonValue.Make(Shadow.Value));
            if (Outline != null) j.Set("outline", Shared.JsonValue.Make(Outline.Value));
            if (Color != null) j.Set("color", Shared.JsonValue.Make(Color));
            if (ActiveColor != null) j.Set("activeColor", Shared.JsonValue.Make(ActiveColor));
            if (Align != null) j.Set("align", Shared.JsonValue.Make(Align.Value));
            if (VAlign != null) j.Set("vAlign", Shared.JsonValue.Make(VAlign.Value));
            if (LineSpacing != null) j.Set("lineSpacing", Shared.JsonValue.Make(LineSpacing.Value));
            if (BoxX != null || BoxY != null || BoxW != null || BoxH != null)
            {
                var b = Shared.JsonValue.Object();
                b.Set("x", Shared.JsonValue.Make(BoxX != null ? BoxX.Value : 0.05));
                b.Set("y", Shared.JsonValue.Make(BoxY != null ? BoxY.Value : 0.08));
                b.Set("w", Shared.JsonValue.Make(BoxW != null ? BoxW.Value : 0.90));
                b.Set("h", Shared.JsonValue.Make(BoxH != null ? BoxH.Value : 0.84));
                j.Set("box", b);
            }
            return j;
        }

        public static StyleOverride FromJson(Shared.JsonValue j)
        {
            if (j == null || j.Type != Shared.JsonValue.Kind.Object) return null;
            var s = new StyleOverride();
            s.Font = j.GetStr("font");
            s.Size = j.Has("size") && j.Get("size").Type == Shared.JsonValue.Kind.Number ? (double?)j.GetNum("size", 44) : null;
            s.Bold = j.Has("bold") ? (bool?)j.GetBool("bold", false) : null;
            s.Italic = j.Has("italic") ? (bool?)j.GetBool("italic", false) : null;
            s.Shadow = j.Has("shadow") ? (bool?)j.GetBool("shadow", true) : null;
            s.Outline = j.Has("outline") ? (bool?)j.GetBool("outline", false) : null;
            s.Color = j.GetStr("color");
            s.ActiveColor = j.GetStr("activeColor");
            if (j.Has("align") && j.Get("align").Type == Shared.JsonValue.Kind.Number) s.Align = (int?)j.GetInt("align", 1);
            if (j.Has("vAlign") && j.Get("vAlign").Type == Shared.JsonValue.Kind.Number) s.VAlign = (int?)j.GetInt("vAlign", 1);
            s.LineSpacing = j.Has("lineSpacing") && j.Get("lineSpacing").Type == Shared.JsonValue.Kind.Number ? (double?)j.GetNum("lineSpacing", 1.15) : null;
            var box = j.Get("box");
            if (box.Type == Shared.JsonValue.Kind.Object)
            {
                s.BoxX = box.GetNum("x", 0.05); s.BoxY = box.GetNum("y", 0.08);
                s.BoxW = box.GetNum("w", 0.90); s.BoxH = box.GetNum("h", 0.84);
            }
            return s;
        }
    }

    // ---------------------------------------------------------------- fondo
    public class BackgroundOverride
    {
        public string Color;
        public string Image;      // ruta relativa al proyecto (media/…)
        public int? Fit;          // 0 llenar 1 ajustar
        public double? Opacity;

        public void InheritFrom(BackgroundOverride upper)
        {
            if (upper == null) return;
            if (Color == null) Color = upper.Color;
            if (Image == null) Image = upper.Image;
            if (Fit == null) Fit = upper.Fit;
            if (Opacity == null) Opacity = upper.Opacity;
        }

        public Shared.JsonValue ToJson()
        {
            var j = Shared.JsonValue.Object();
            if (Color != null) j.Set("color", Shared.JsonValue.Make(Color));
            if (Image != null) j.Set("image", Shared.JsonValue.Make(Image));
            if (Fit != null) j.Set("fit", Shared.JsonValue.Make(Fit.Value));
            if (Opacity != null) j.Set("opacity", Shared.JsonValue.Make(Opacity.Value));
            return j;
        }

        public static BackgroundOverride FromJson(Shared.JsonValue j)
        {
            if (j == null || j.Type != Shared.JsonValue.Kind.Object) return null;
            var b = new BackgroundOverride();
            b.Color = j.GetStr("color");
            b.Image = j.GetStr("image");
            if (j.Has("fit") && j.Get("fit").Type == Shared.JsonValue.Kind.Number) b.Fit = (int?)j.GetInt("fit", 0);
            if (j.Has("opacity") && j.Get("opacity").Type == Shared.JsonValue.Kind.Number) b.Opacity = (double?)j.GetNum("opacity", 1.0);
            return b;
        }
    }

    // ---------------------------------------------------------------- elementos
    public enum ElementKind { Text = 0, Verse, Image, Video, LowerThird }

    /// <summary>Elemento proyectable [SPEC §5.2]. Unidad mínima del modelo.</summary>
    public class Element
    {
        public string Id;                     // estable [SPEC §5.3c]
        public ElementKind Kind;
        public List<string> Lines = new List<string>();     // texto (con marcas de sync implícitas por orden)
        public List<double> SyncMarks = new List<double>(); // segundos opcionales
        public string Reference;              // Versículo: "Juan 3:16"
        public List<string> HighlightWords = new List<string>(); // resaltado [SPEC §5.2 #2]
        public string Src;                    // media: ruta relativa
        public bool Loop;
        public int Volume = 100;
        public double StartAt;
        public string OverlayText;            // Lower Third
        public StyleOverride StyleOverride = new StyleOverride();
        public BackgroundOverride BgOverride = new BackgroundOverride();
        public List<string> Tags = new List<string>();
        public string Note;                   // notas del operador
        public string Transition = "";       // cut | fade | slide (vacío = transición por defecto)
        public double X = 0.05, Y = 0.10, W = 0.90, H = 0.80;   // posición libre en el lienzo

        public string KindKey
        {
            get
            {
                switch (Kind)
                {
                    case ElementKind.Verse: return "verse";
                    case ElementKind.Image: return "image";
                    case ElementKind.Video: return "video";
                    case ElementKind.LowerThird: return "lower3";
                    default: return "text";
                }
            }
        }

        public Shared.JsonValue ToJson()
        {
            var j = Shared.JsonValue.Object();
            j.Set("id", Shared.JsonValue.Make(Id));
            j.Set("type", Shared.JsonValue.Make(KindKey));
            if (Lines.Count > 0)
            {
                var arr = Shared.JsonValue.Array();
                for (int i = 0; i < Lines.Count; i++)
                {
                    var ln = Shared.JsonValue.Object();
                    ln.Set("text", Shared.JsonValue.Make(Lines[i]));
                    if (i < SyncMarks.Count && SyncMarks[i] > 0) ln.Set("syncMark", Shared.JsonValue.Make(SyncMarks[i]));
                    arr.Add(ln);
                }
                j.Set("lines", arr);
            }
            if (Reference != null) j.Set("reference", Shared.JsonValue.Make(Reference));
            if (HighlightWords.Count > 0)
            {
                var hl = Shared.JsonValue.Array();
                hl.AddStrings(HighlightWords);
                j.Set("highlight", hl);
            }
            if (Src != null) j.Set("src", Shared.JsonValue.Make(Src));
            if (Loop) j.Set("loop", Shared.JsonValue.Make(true));
            if (Volume != 100) j.Set("volume", Shared.JsonValue.Make(Volume));
            if (StartAt > 0) j.Set("startAt", Shared.JsonValue.Make(StartAt));
            if (OverlayText != null) j.Set("overlayText", Shared.JsonValue.Make(OverlayText));
            if (Tags.Count > 0) { var t = Shared.JsonValue.Array(); t.AddStrings(Tags); j.Set("tags", t); }
            if (Note != null) j.Set("note", Shared.JsonValue.Make(Note));
            if (!string.IsNullOrEmpty(Transition)) j.Set("transition", Shared.JsonValue.Make(Transition));
            j.Set("x", Shared.JsonValue.Make(X));
            j.Set("y", Shared.JsonValue.Make(Y));
            j.Set("w", Shared.JsonValue.Make(W));
            j.Set("h", Shared.JsonValue.Make(H));
            j.Set("styleOverride", StyleOverride != null ? StyleOverride.ToJson() : Shared.JsonValue.Null());
            j.Set("bgOverride", BgOverride != null ? BgOverride.ToJson() : Shared.JsonValue.Null());
            return j;
        }

        public static Element FromJson(Shared.JsonValue j)
        {
            if (j == null || j.Type != Shared.JsonValue.Kind.Object) return null;
            var e = new Element();
            e.Id = j.GetStr("id", Guid.NewGuid().ToString("N").Substring(0, 10));
            string t = j.GetStr("type", "text");
            switch (t)
            {
                case "verse": e.Kind = ElementKind.Verse; break;
                case "image": e.Kind = ElementKind.Image; break;
                case "video": e.Kind = ElementKind.Video; break;
                case "lower3": e.Kind = ElementKind.LowerThird; break;
                default: e.Kind = ElementKind.Text; break;
            }
            var lines = j.GetArray("lines");
            if (lines != null)
                foreach (var l in lines)
                {
                    if (l.Type == Shared.JsonValue.Kind.String) { e.Lines.Add(l.Str); e.SyncMarks.Add(0); }
                    else if (l.Type == Shared.JsonValue.Kind.Object)
                    {
                        e.Lines.Add(l.GetStr("text", ""));
                        e.SyncMarks.Add(l.GetNum("syncMark", 0));
                    }
                }
            e.Reference = j.GetStr("reference");
            e.HighlightWords = j.GetStringArray("highlight");
            e.Src = j.GetStr("src");
            e.Loop = j.GetBool("loop", false);
            e.Volume = j.GetInt("volume", 100);
            e.StartAt = j.GetNum("startAt", 0);
            e.OverlayText = j.GetStr("overlayText");
            e.Tags = j.GetStringArray("tags");
            e.Note = j.GetStr("note");
                e.Transition = j.GetStr("transition", "");
            e.X = j.GetNum("x", 0.05); e.Y = j.GetNum("y", 0.10);
            e.W = j.GetNum("w", 0.90); e.H = j.GetNum("h", 0.80);
            e.StyleOverride = StyleOverride.FromJson(j.Get("styleOverride")) ?? new StyleOverride();
            e.BgOverride = BackgroundOverride.FromJson(j.Get("bgOverride")) ?? new BackgroundOverride();
            return e;
        }
    }

    // ---------------------------------------------------------------- escenario
    public class Scenario
    {
        public string Id;
        public string Title;
        public string TemplateId;             // plantilla de escenario (nivel 2) [SPEC §5.4]
        public List<string> Tags = new List<string>();
        public List<Element> Elements = new List<Element>();
        public StyleOverride StyleOverride = new StyleOverride();
        public BackgroundOverride BgOverride = new BackgroundOverride();

        public Shared.JsonValue ToJson()
        {
            var j = Shared.JsonValue.Object();
            j.Set("id", Shared.JsonValue.Make(Id));
            j.Set("title", Shared.JsonValue.Make(Title));
            if (TemplateId != null) j.Set("templateId", Shared.JsonValue.Make(TemplateId));
            if (Tags.Count > 0) { var t = Shared.JsonValue.Array(); t.AddStrings(Tags); j.Set("tags", t); }
            var els = Shared.JsonValue.Array();
            foreach (var e in Elements) els.Add(e.ToJson());
            j.Set("elements", els);
            j.Set("styleOverride", StyleOverride.ToJson());
            j.Set("bgOverride", BgOverride.ToJson());
            return j;
        }

        public static Scenario FromJson(Shared.JsonValue j)
        {
            if (j == null || j.Type != Shared.JsonValue.Kind.Object) return null;
            var s = new Scenario();
            s.Id = j.GetStr("id", Guid.NewGuid().ToString("N").Substring(0, 10));
            s.Title = j.GetStr("title", "Escenario");
            s.TemplateId = j.GetStr("templateId");
            s.Tags = j.GetStringArray("tags");
            var els = j.GetArray("elements");
            if (els != null) foreach (var e in els) { var el = Element.FromJson(e); if (el != null) s.Elements.Add(el); }
            s.StyleOverride = StyleOverride.FromJson(j.Get("styleOverride")) ?? new StyleOverride();
            s.BgOverride = BackgroundOverride.FromJson(j.Get("bgOverride")) ?? new BackgroundOverride();
            return s;
        }
    }

    // ---------------------------------------------------------------- tema
    /// <summary>Tema (nivel 1 de la herencia) [SPEC §5.4, §7.4]. Modificable en caliente.</summary>
    public class Theme
    {
        public string Id;
        public string Name;
        public StyleOverride Style = new StyleOverride();
        public BackgroundOverride Background = new BackgroundOverride();
        public string RestScreen = "black";   // negro | logo | theme [SPEC §6.1.3]

        public Shared.JsonValue ToJson()
        {
            var j = Shared.JsonValue.Object();
            j.Set("id", Shared.JsonValue.Make(Id));
            j.Set("name", Shared.JsonValue.Make(Name));
            j.Set("style", Style.ToJson());
            j.Set("bg", Background.ToJson());
            j.Set("restScreen", Shared.JsonValue.Make(RestScreen));
            return j;
        }

        public static Theme FromJson(Shared.JsonValue j)
        {
            if (j == null || j.Type != Shared.JsonValue.Kind.Object) return null;
            var t = new Theme();
            t.Id = j.GetStr("id", "tema-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            t.Name = j.GetStr("name", "Tema");
            t.Style = StyleOverride.FromJson(j.Get("style")) ?? new StyleOverride();
            t.Background = BackgroundOverride.FromJson(j.Get("bg")) ?? new BackgroundOverride();
            t.RestScreen = j.GetStr("restScreen", "black");
            return t;
        }
    }

    /// <summary>Plantilla de Escenario (nivel 2 de la herencia).</summary>
    public class ScenarioTemplate
    {
        public string Id;
        public string Name;
        public StyleOverride Style = new StyleOverride();
        public BackgroundOverride Background = new BackgroundOverride();

        public Shared.JsonValue ToJson()
        {
            var j = Shared.JsonValue.Object();
            j.Set("id", Shared.JsonValue.Make(Id));
            j.Set("name", Shared.JsonValue.Make(Name));
            j.Set("style", Style.ToJson());
            j.Set("bg", Background.ToJson());
            return j;
        }

        public static ScenarioTemplate FromJson(Shared.JsonValue j)
        {
            if (j == null || j.Type != Shared.JsonValue.Kind.Object) return null;
            var t = new ScenarioTemplate();
            t.Id = j.GetStr("id", "tpl-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            t.Name = j.GetStr("name", "Plantilla");
            t.Style = StyleOverride.FromJson(j.Get("style")) ?? new StyleOverride();
            t.Background = BackgroundOverride.FromJson(j.Get("bg")) ?? new BackgroundOverride();
            return t;
        }
    }

    // ---------------------------------------------------------------- proyecto
    /// <summary>Proyecto ahp.v1 [SPEC §5.3].</summary>
    public class AhpProject
    {
        public const string FormatVersion = "ahp.v1";
        public string Name = "Proyecto sin título";
        public string ThemeRef;
        public List<Theme> Themes = new List<Theme>();
        public List<ScenarioTemplate> Templates = new List<ScenarioTemplate>();
        public List<Scenario> Scenarios = new List<Scenario>();
        public List<string> MediaManifest = new List<string>();
        public string SourcePath;             // ruta del .ahp guardado

        public static AhpProject CreateDefault()
        {
            var p = new AhpProject();
            // Temas de la referencia web (H-P-Web-Version-Ref): mismas familias
            // (Outfit / Cormorant Garamond / Libre Baskerville incrustadas en
            // resources/fonts) y mismos fondos (resources/backgrounds).
            p.Themes.Add(WebTheme("tema-clasico", "Clásico lumínico", "Outfit", 46,
                "#FFF8EC", "#E8C872", true, "gold-rays.jpg", "#0A0704"));
            p.Themes.Add(WebTheme("tema-escritura", "Escritura", "Libre Baskerville", 42,
                "#F4EFE4", "#D4B56A", true, "blue-depth.jpg", "#050814"));
            p.Themes.Add(WebTheme("tema-broadcast", "Broadcast", "Outfit", 40,
                "#FFFFFF", "#7DD3FC", true, "purple-haze.jpg", "#120814"));
            p.Themes.Add(WebTheme("tema-moderno", "Moderno liviano", "Outfit", 44,
                "#F8FAFC", "#FDBA74", false, "emerald.jpg", "#06110C"));
            p.Themes.Add(WebTheme("tema-solemne", "Solemne", "Cormorant Garamond", 52,
                "#F3E6C8", "#C4A35A", true, "cross-dawn.jpg", "#1A0E08"));
            var calma = new Theme { Id = "tema-calma", Name = "Calma" };
            calma.Style.Font = "Segoe UI";
            calma.Style.Size = 48;
            calma.Style.Color = "#FFFFFF";
            calma.Style.ActiveColor = "#FFD700";
            calma.Style.Align = 1;
            calma.Style.VAlign = 1;
            calma.Style.Shadow = true;
            calma.Background.Color = "#101820";
            p.Themes.Add(calma);
            p.ThemeRef = "tema-clasico";
            return p;
        }

        static Theme WebTheme(string id, string name, string font, int size,
            string color, string accent, bool shadow, string bgImage, string bgColor)
        {
            var t = new Theme { Id = id, Name = name };
            t.Style.Font = font;
            t.Style.Size = size;
            t.Style.Color = color;
            t.Style.ActiveColor = accent;
            t.Style.Align = 1;
            t.Style.VAlign = 1;
            t.Style.Shadow = shadow;
            t.Style.LineSpacing = 1.2;
            t.Background.Color = bgColor;
            t.Background.Image = "app:resources/backgrounds/" + bgImage;
            t.RestScreen = "black";
            return t;
        }

        public Theme ActiveTheme()
        {
            foreach (var t in Themes) if (t.Id == ThemeRef) return t;
            return Themes.Count > 0 ? Themes[0] : null;
        }

        public Shared.JsonValue ToJson()
        {
            var root = Shared.JsonValue.Object();
            root.Set("format", Shared.JsonValue.Make(FormatVersion));
            var proj = Shared.JsonValue.Object();
            proj.Set("name", Shared.JsonValue.Make(Name));
            proj.Set("themeRef", Shared.JsonValue.Make(ThemeRef));
            var th = Shared.JsonValue.Array();
            foreach (var t in Themes) th.Add(t.ToJson());
            proj.Set("themes", th);
            var tp = Shared.JsonValue.Array();
            foreach (var t in Templates) tp.Add(t.ToJson());
            proj.Set("templates", tp);
            var sc = Shared.JsonValue.Array();
            foreach (var s in Scenarios) sc.Add(s.ToJson());
            proj.Set("scenarios", sc);
            var mm = Shared.JsonValue.Array();
            mm.AddStrings(MediaManifest);
            proj.Set("media", mm);
            root.Set("project", proj);
            return root;
        }

        public static AhpProject FromJson(Shared.JsonValue root)
        {
            if (root == null || root.Type != Shared.JsonValue.Kind.Object)
                throw new InvalidDataException("El archivo no contiene un objeto JSON raíz.");
            string fmt = root.GetStr("format", "");
            if (fmt != FormatVersion)
                throw new InvalidDataException("Formato no soportado: se espera ahp.v1 y se encontró '" + fmt + "'.");
            var proj = root.Get("project");
            var p = new AhpProject();
            p.Name = proj.GetStr("name", "Proyecto");
            p.ThemeRef = proj.GetStr("themeRef");
            foreach (var t in proj.GetArray("themes") ?? new List<Shared.JsonValue>())
            { var x = Theme.FromJson(t); if (x != null) p.Themes.Add(x); }
            foreach (var t in proj.GetArray("templates") ?? new List<Shared.JsonValue>())
            { var x = ScenarioTemplate.FromJson(t); if (x != null) p.Templates.Add(x); }
            foreach (var s in proj.GetArray("scenarios") ?? new List<Shared.JsonValue>())
            { var x = Scenario.FromJson(s); if (x != null) p.Scenarios.Add(x); }
            p.MediaManifest = proj.GetStringArray("media");
            return p;
        }

        public ScenarioTemplate FindTemplate(string id)
        {
            if (id == null) return null;
            foreach (var t in Templates) if (t.Id == id) return t;
            return null;
        }
    }

    // ---------------------------------------------------------------- resolved
    /// <summary>Estado RESUELTO de un elemento para proyección (el núcleo solo dibuja).</summary>
    public class ResolvedSlide
    {
        public string Id;
        public string Kind;                        // text|image|video|verse|lower3
        public List<string> Lines = new List<string>();
        public int ActiveLine;
        public string Reference;
        public string Src;                         // ruta absoluta
        public bool Loop;
        public int Volume = 100;
        public double StartAt;
        public string OverlayText;
        public StyleOverride Style = new StyleOverride();
        public BackgroundOverride Background = new BackgroundOverride();
        public string Transition = "";               // cut | fade | slide
        public List<string> HighlightWords = new List<string>();   // resaltado [SPEC §5.2 #2]

        /// <summary>Resuelve la herencia de 4 niveles [SPEC §5.4]:
        /// Tema → Plantilla de Escenario → Escenario → Elemento.</summary>
        public static ResolvedSlide Resolve(Element el, Scenario scn, AhpProject project, string baseDir)
        {
            var r = new ResolvedSlide();
            r.Id = el.Id;
            r.Kind = el.KindKey;
            r.Lines = new List<string>(el.Lines);
            r.Reference = el.Reference;
            r.OverlayText = el.OverlayText;
            r.Transition = el.Transition;
            r.HighlightWords = new List<string>(el.HighlightWords);
            r.Loop = el.Loop;
            r.Volume = el.Volume;
            r.StartAt = el.StartAt;
            if (el.Src != null)
                r.Src = ResolvePath(baseDir, el.Src);

            // Herencia en cascada [SPEC §5.4]: el nivel más bajo GANA.
            // Se parte del elemento (nivel 4) y se rellenan los null subiendo:
            // Elemento → Escenario → Plantilla → Tema (nivel 1 como base).
            var theme = project != null ? project.ActiveTheme() : null;
            var tpl = project != null ? project.FindTemplate(scn != null ? scn.TemplateId : null) : null;
            var s = new StyleOverride();
            s.InheritFrom(el.StyleOverride);                              // nivel 4
            s.InheritFrom(scn != null ? scn.StyleOverride : null);        // nivel 3
            s.InheritFrom(tpl != null ? tpl.Style : null);                // nivel 2
            s.InheritFrom(theme != null ? theme.Style : null);            // nivel 1
            if (s.Size == null) s.Size = 48;
            if (s.Align == null) s.Align = 1;
            if (s.VAlign == null) s.VAlign = 1;
            if (s.LineSpacing == null) s.LineSpacing = 1.15;
            if (s.Shadow == null) s.Shadow = true;
            if (s.Font == null) s.Font = "Segoe UI";
            if (s.Color == null) s.Color = "#FFFFFF";
            if (s.ActiveColor == null) s.ActiveColor = "#FFD700";
            if (s.BoxX == null) s.BoxX = el.X;
            if (s.BoxY == null) s.BoxY = el.Y;
            if (s.BoxW == null) s.BoxW = el.W;
            if (s.BoxH == null) s.BoxH = el.H;
            r.Style = s;

            var b = new BackgroundOverride();
            b.InheritFrom(el.BgOverride);
            b.InheritFrom(scn != null ? scn.BgOverride : null);
            b.InheritFrom(tpl != null ? tpl.Background : null);
            b.InheritFrom(theme != null ? theme.Background : null);
            if (b.Color == null) b.Color = "#101820";
            if (b.Fit == null) b.Fit = 0;
            if (b.Opacity == null) b.Opacity = 1.0;
            if (b.Image != null) b.Image = ResolvePath(baseDir, b.Image);
            r.Background = b;
            return r;
        }

        static string ResolvePath(string baseDir, string rel)
        {
            if (Path.IsPathRooted(rel)) return rel;
            // "app:..." = recurso incrustado de la instalación (fondos del programa)
            if (rel.StartsWith("app:", StringComparison.Ordinal))
                return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    rel.Substring(4).Replace('/', Path.DirectorySeparatorChar)));
            return Path.GetFullPath(Path.Combine(baseDir != null ? baseDir : ".", rel));
        }

        /// <summary>Contrato de render ipc.v1 (el mismo que consume el núcleo C++).</summary>
        public Shared.JsonValue ToIpcJson()
        {
            var j = Shared.JsonValue.Object();
            j.Set("id", Shared.JsonValue.Make(Id));
            j.Set("kind", Shared.JsonValue.Make(Kind));
            var lines = Shared.JsonValue.Array();
            lines.AddStrings(Lines);
            j.Set("lines", lines);
            j.Set("activeLine", Shared.JsonValue.Make(ActiveLine));
            if (Reference != null) j.Set("reference", Shared.JsonValue.Make(Reference));
            if (!string.IsNullOrEmpty(Transition)) j.Set("transition", Shared.JsonValue.Make(Transition));
            if (HighlightWords.Count > 0)
            {
                var hl = Shared.JsonValue.Array();
                hl.AddStrings(HighlightWords);
                j.Set("highlight", hl);
            }

            var st = Shared.JsonValue.Object();
            st.Set("font", Shared.JsonValue.Make(Style.Font));
            st.Set("size", Shared.JsonValue.Make(Style.Size ?? 48));
            st.Set("bold", Shared.JsonValue.Make(Style.Bold ?? false));
            st.Set("italic", Shared.JsonValue.Make(Style.Italic ?? false));
            st.Set("color", Shared.JsonValue.Make(Style.Color));
            st.Set("activeColor", Shared.JsonValue.Make(Style.ActiveColor));
            st.Set("align", Shared.JsonValue.Make(Style.Align ?? 1));
            st.Set("vAlign", Shared.JsonValue.Make(Style.VAlign ?? 1));
            st.Set("shadow", Shared.JsonValue.Make(Style.Shadow ?? true));
            st.Set("outline", Shared.JsonValue.Make(Style.Outline ?? false));
            st.Set("lineSpacing", Shared.JsonValue.Make(Style.LineSpacing ?? 1.15));
            var box = Shared.JsonValue.Object();
            box.Set("x", Shared.JsonValue.Make(Style.BoxX ?? 0.05));
            box.Set("y", Shared.JsonValue.Make(Style.BoxY ?? 0.08));
            box.Set("w", Shared.JsonValue.Make(Style.BoxW ?? 0.90));
            box.Set("h", Shared.JsonValue.Make(Style.BoxH ?? 0.84));
            st.Set("box", box);
            j.Set("style", st);

            var bg = Shared.JsonValue.Object();
            bg.Set("color", Shared.JsonValue.Make(Background.Color));
            if (Background.Image != null) bg.Set("image", Shared.JsonValue.Make(Background.Image));
            bg.Set("fit", Shared.JsonValue.Make(Background.Fit ?? 0));
            bg.Set("opacity", Shared.JsonValue.Make(Background.Opacity ?? 1.0));
            j.Set("bg", bg);

            if (Kind == "image" || Kind == "video")
            {
                var m = Shared.JsonValue.Object();
                if (Src != null) m.Set("src", Shared.JsonValue.Make(Src));
                m.Set("loop", Shared.JsonValue.Make(Loop));
                m.Set("volume", Shared.JsonValue.Make(Volume));
                m.Set("startAt", Shared.JsonValue.Make(StartAt));
                j.Set("media", m);
            }
            if (OverlayText != null)
            {
                var o = Shared.JsonValue.Object();
                o.Set("present", Shared.JsonValue.Make(true));
                var ol = Shared.JsonValue.Array();
                ol.Add(Shared.JsonValue.Make(OverlayText));
                o.Set("lines", ol);
                var os = Shared.JsonValue.Object();
                os.Set("font", Shared.JsonValue.Make(Style.Font));
                os.Set("color", Shared.JsonValue.Make("#FFFFFF"));
                os.Set("activeColor", Shared.JsonValue.Make(Style.ActiveColor));
                o.Set("style", os);
                o.Set("position", Shared.JsonValue.Make(0));
                o.Set("duration", Shared.JsonValue.Make(0));
                j.Set("overlay", o);
            }
            return j;
        }
    }
}
