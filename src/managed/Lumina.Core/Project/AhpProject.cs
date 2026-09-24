// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Project/AhpProject.cs : contenedor de proyecto «ahp.v1» (F2.01-F2.07 y
//  F2.15 del Plan de Ultra Implementación — Secciones 5.1/5.3/12.4).
//
//  CONTRATO del formato (documento técnico §5.1):
//    { "format": "ahp.v1",
//      "project": { "name": "...", "themeRef": "...", "scenarios": [...] },
//      "media":   [ {"path": "media/x.png", "kind": "image", "sha256": "..."} ] }
//
//  Reglas implementadas:
//   * F2.01.1  «format» OBLIGATORIO (validación de compatibilidad F2.01.9).
//   * F2.01.3  Escenarios ORDENADOS (lista = orden del culto).
//   * F2.01.4  IDs ESTABLES (generador monotónico persistido en nextId).
//   * F2.01.5  manifiesto «media/» con hash + tamaño.
//   * F2.01.6  recursos externos opcionales (rutas absolutas permitidas).
//   * F2.01.7  empaquetado ZIP (project.json + media/*).
//   * F2.01.8  campos desconocidos IGNORADOS (evolución sin ruptura).
//   * F2.01.10 mismo contenido x86/x64 SIN conversión: JSON puro, enteros
//              serializados con CultureInfo invariante.
//   * F2.02    EXACTAMENTE cinco tipos de Elemento (enum cerrado).
//   * F2.03    por línea: texto, formato, syncMark, estilo y estado activo.
//   * F2.04    versículos: libro/capítulo/versículos/traducción/palabras
//              destacadas/formato de cita.
//   * F2.05    imágenes: ruta relativa, ajuste, opacidad, recorte, etiquetas
//              semánticas.
//   * F2.06    videos: ruta, volumen inicial, startAt, loop, posición,
//              etiquetas.
//   * F2.07    Lower Third: texto, estilo, duración, posición, elemento
//              superpuesto, activación manual o Trigger.
//   * F2.15    guardado ATÓMICO (temporal + renombrado), autoguardado
//              configurable y recuperación tras fallo.
//
//  Multi-target net35/net48/net8 con C# 7.3 (sin async: runtime 2.0).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace lumina.core.project
{
    /// <summary>Identificador del formato (F2.01.1/F2.01.9).</summary>
    public static class AhpFormat
    {
        public const string V1 = "ahp.v1";
    }

    /// <summary>Informe de carga: advertencias + compatibilidad (F2.01.9).</summary>
    public sealed class AhpLoadReport
    {
        public bool Ok;
        public string Format = "";
        public List<string> Warnings = new List<string>();
        public List<string> IgnoredFields = new List<string>();
        public void Warn(string m) { Warnings.Add(m); }
    }

    /// <summary>F2.03: una línea de texto con su sincronización y estilo.</summary>
    public sealed class AhpLine
    {
        public string Text = "";
        public string Format = "";      // negrita/cursiva/... (libre, por tema)
        public int SyncMark;            // 0 = sin marca (F1.03)
        public string StyleId = "";     // "" = heredar (cascada F2.08)
        public bool Active;             // estado activo persistido (F2.03.5)

        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["text"] = Text ?? "";
            if (!string.IsNullOrEmpty(Format)) o["format"] = Format;
            if (SyncMark > 0) o["syncMark"] = (long)SyncMark;
            if (!string.IsNullOrEmpty(StyleId)) o["style"] = StyleId;
            if (Active) o["active"] = true;
            return o;
        }
        public static AhpLine FromJson(Dictionary<string, object> o)
        {
            AhpLine l = new AhpLine();
            l.Text = MiniJson.GetString(o, "text", "");
            l.Format = MiniJson.GetString(o, "format", "");
            l.SyncMark = (int)MiniJson.GetInt(o, "syncMark", 0);
            l.StyleId = MiniJson.GetString(o, "style", "");
            l.Active = MiniJson.GetBool(o, "active", false);
            return l;
        }
    }

    /// <summary>F2.02: EXACTAMENTE cinco tipos (MVP cerrado — sin tipos extra).</summary>
    public enum AhpElementKind { Text = 0, Verse = 1, Image = 2, Video = 3, LowerThird = 4 }

    /// <summary>Elemento proyectable con los campos de los cinco tipos (F2.02-F2.07).</summary>
    public sealed class AhpElement
    {
        public string Id = "";                  // F2.01.4: ID estable
        public AhpElementKind Kind = AhpElementKind.Text;
        public string Title = "";
        public string StyleOverride = "";       // estilo plano; "" = heredar

        // ---- F2.03: Texto Formateado ----
        public List<AhpLine> Lines = new List<AhpLine>();

        // ---- F2.04: Versículo Bíblico ----
        public string Book = "";
        public int Chapter;
        public int VerseFrom;
        public int VerseTo;
        public string Translation = "";
        public List<string> HighlightedWords = new List<string>();
        public string QuoteFormat = "";         // formato de cita

        // ---- F2.05: Imagen ----
        public string ImagePath = "";           // RELATIVA al proyecto (F2.05.1)
        public string Fit = "cover";            // llenar/ajustar (F2.05.2)
        public double Opacity = 1.0;
        public int CropX, CropY, CropW, CropH;  // 0 = sin recorte (F2.05.4)
        public List<string> Tags = new List<string>();   // etiquetas semánticas

        // ---- F2.06: Video ----
        public string VideoPath = "";
        public int VideoVolume = 100;           // volumen inicial
        public long StartAtMs;                  // startAt
        public bool Loop;
        public double PositionLeftPct = 0.5, PositionTopPct = 0.5;  // posición
        public double PositionWidthPct = 1.0, PositionHeightPct = 1.0;

        // ---- F2.07: Lower Third ----
        public string LtText = "";
        public string LtStyleId = "";
        public double LtDurationSec = 6.0;
        public string LtPosition = "bottom";    // bottom/top/... (F2.07.4)
        public string LtOverlayElementId = "";  // Elemento superpuesto
        public bool LtManualActivation = true;  // manual o por Trigger (F2.07.6)

        public bool IsKind(AhpElementKind k) { return Kind == k; }
    }

    /// <summary>Escenario: colección ORDENADA de Elementos (F2.11).</summary>
    public sealed class AhpScenario
    {
        public string Id = "";
        public string Name = "";
        public string TemplateRef = "";         // plantilla (F2.11.5/F2.08)
        public string Group = "";               // agrupación (F2.11.4)
        public string StyleOverride = "";       // nivel Escenario (cascada)
        public List<AhpElement> Elements = new List<AhpElement>();
    }

    /// <summary>Entrada del manifiesto media/ (F2.01.5).</summary>
    public sealed class AhpMediaEntry
    {
        public string Path = "";                // "media/xxx.png"
        public string Kind = "";                // image/video/font
        public string Sha256 = "";
        public long Size;
    }

    /// <summary>Proyecto ahp.v1 (contenedor de Escenarios, tema y recursos).</summary>
    public sealed class AhpProject
    {
        public string Name = "";
        public string ThemeRef = "";
        public string StyleOverride = "";       // nivel Proyecto/tema aplicado
        public List<AhpScenario> Scenarios = new List<AhpScenario>();
        public List<AhpMediaEntry> Media = new List<AhpMediaEntry>();
        public long NextId = 1;                 // semilla de IDs estables

        /// <summary>Genera un ID estable nuevo (prefijo + contador monotónico).</summary>
        public string NewId(string prefix)
        {
            string id = prefix + NextId.ToString(CultureInfo.InvariantCulture);
            NextId++;
            return id;
        }

        public AhpElement NewElement(AhpElementKind kind)
        {
            AhpElement e = new AhpElement();
            e.Id = NewId("el-");
            e.Kind = kind;
            return e;
        }
        public AhpScenario NewScenario(string name)
        {
            AhpScenario s = new AhpScenario();
            s.Id = NewId("sc-");
            s.Name = name ?? "";
            return s;
        }

        public AhpElement FindElement(string id)
        {
            foreach (AhpScenario s in Scenarios)
                foreach (AhpElement e in s.Elements)
                    if (e.Id == id) return e;
            return null;
        }
        public AhpScenario FindScenario(string id)
        {
            foreach (AhpScenario s in Scenarios) if (s.Id == id) return s;
            return null;
        }
    }

    /// <summary>
    /// F2.15: guardado ATÓMICO + autoguardado configurable + recuperación.
    /// Atomicidad: se escribe en «&lt;nombre&gt;.tmp» y se renombra sobre el
    /// destino — un corte de luz jamás deja un proyecto a medias.
    /// </summary>
    public sealed class ProjectStore
    {
        private readonly object _gate = new object();

        /// <summary>Guardado atómico en JSON (F2.15.3).</summary>
        public void SaveAtomic(string path, AhpProject project)
        {
            string json = AhpProjectIO.ToJson(project);
            byte[] data = Encoding.UTF8.GetBytes(json);
            SaveAtomicBytes(path, data);
        }

        private static void SaveAtomicBytes(string path, byte[] data)
        {
            string tmp = path + ".tmp";
            using (FileStream fs = new FileStream(tmp, FileMode.Create,
                                                  FileAccess.Write, FileShare.None))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush();
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>Carga con validación e informe (F2.01.8/F2.01.9).</summary>
        public AhpProject Load(string path, AhpLoadReport report)
        {
            string json;
            using (StreamReader sr = new StreamReader(path, Encoding.UTF8))
                json = sr.ReadToEnd();
            return AhpProjectIO.FromJson(json, report);
        }

        // ---- autoguardado (F2.15.2/F2.15.4) -------------------------------

        private System.Threading.Timer _timer;
        private Func<AhpProject> _snapshot;
        private string _path;
        private string _autosavePath;
        public int IntervalSec = 120;           // 0 = desactivado (configurable)
        public DateTime LastAutosaveUtc;

        /// <summary>Arma el autoguardado (la snapshot se invoca en el hilo del
        /// timer; debe ser thread-safe respecto de la edición).</summary>
        public void ArmAutosave(string projectPath, Func<AhpProject> snapshot)
        {
            lock (_gate)
            {
                _path = projectPath;
                _autosavePath = projectPath + ".autosave";
                _snapshot = snapshot;
                if (_timer != null) { _timer.Dispose(); _timer = null; }
                if (IntervalSec <= 0) return;
                _timer = new System.Threading.Timer(Tick, null,
                    IntervalSec * 1000, IntervalSec * 1000);
            }
        }

        public void DisarmAutosave()
        {
            lock (_gate)
            {
                if (_timer != null) { _timer.Dispose(); _timer = null; }
            }
        }

        private void Tick(object state)
        {
            AhpProject p;
            lock (_gate)
            {
                if (_snapshot == null || _path == null) return;
                p = _snapshot();
            }
            if (p == null) return;
            try
            {
                SaveAtomic(_autosavePath, p);
                lock (_gate) LastAutosaveUtc = DateTime.UtcNow;
            }
            catch (IOException)
            {
                // Tolerante: el autoguardado JAMÁS interrumpe la proyección.
            }
        }

        /// <summary>F2.15.4: recuperación tras fallo. Devuelve el autoguardado
        /// si es MÁS RECIENTE que el proyecto (el usuario decide con qué
        /// continuar). true = hay autoguardado recuperable.</summary>
        public bool TryRecover(string projectPath, out AhpProject recovered,
                               out bool autosaveIsNewer)
        {
            recovered = null;
            autosaveIsNewer = false;
            string auto = projectPath + ".autosave";
            if (!File.Exists(auto)) return false;
            AhpLoadReport rep = new AhpLoadReport();
            try { recovered = Load(auto, rep); }
            catch (Exception) { return false; }
            if (recovered == null || !rep.Ok) return false;
            autosaveIsNewer = !File.Exists(projectPath) ||
                File.GetLastWriteTimeUtc(auto) > File.GetLastWriteTimeUtc(projectPath);
            return true;
        }
    }

    /// <summary>Serialización / empaquetado ZIP de ahp.v1.</summary>
    public static class AhpProjectIO
    {
        // ------------------------------------------------------------ save --
        public static string ToJson(AhpProject p)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["format"] = AhpFormat.V1;
            Dictionary<string, object> proj = new Dictionary<string, object>();
            proj["name"] = p.Name ?? "";
            proj["themeRef"] = p.ThemeRef ?? "";
            if (!string.IsNullOrEmpty(p.StyleOverride)) proj["style"] = p.StyleOverride;
            proj["nextId"] = p.NextId;                        // semilla de IDs
            List<object> scen = new List<object>();
            foreach (AhpScenario s in p.Scenarios) scen.Add(ScenarioToJson(s));
            proj["scenarios"] = scen;
            root["project"] = proj;
            List<object> media = new List<object>();
            foreach (AhpMediaEntry m in p.Media)
            {
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["path"] = m.Path; o["kind"] = m.Kind;
                if (!string.IsNullOrEmpty(m.Sha256)) o["sha256"] = m.Sha256;
                o["size"] = m.Size;
                media.Add(o);
            }
            root["media"] = media;
            return MiniJson.Serialize(root);
        }

        private static Dictionary<string, object> ScenarioToJson(AhpScenario s)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["id"] = s.Id;
            o["name"] = s.Name ?? "";
            if (!string.IsNullOrEmpty(s.TemplateRef)) o["templateRef"] = s.TemplateRef;
            if (!string.IsNullOrEmpty(s.Group)) o["group"] = s.Group;
            if (!string.IsNullOrEmpty(s.StyleOverride)) o["style"] = s.StyleOverride;
            List<object> els = new List<object>();
            foreach (AhpElement e in s.Elements) els.Add(ElementToJson(e));
            o["elements"] = els;
            return o;
        }

        private static Dictionary<string, object> ElementToJson(AhpElement e)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["id"] = e.Id;
            o["kind"] = KindToString(e.Kind);
            if (!string.IsNullOrEmpty(e.Title)) o["title"] = e.Title;
            if (!string.IsNullOrEmpty(e.StyleOverride)) o["style"] = e.StyleOverride;
            if (e.Kind == AhpElementKind.Text)
            {
                List<object> ls = new List<object>();
                foreach (AhpLine l in e.Lines) ls.Add(l.ToJson());
                o["lines"] = ls;
            }
            else if (e.Kind == AhpElementKind.Verse)
            {
                o["book"] = e.Book ?? "";
                o["chapter"] = (long)e.Chapter;
                o["verseFrom"] = (long)e.VerseFrom;
                o["verseTo"] = (long)e.VerseTo;
                if (!string.IsNullOrEmpty(e.Translation)) o["translation"] = e.Translation;
                if (e.HighlightedWords.Count > 0)
                {
                    List<object> hs = new List<object>();
                    foreach (string h in e.HighlightedWords) hs.Add(h);
                    o["highlight"] = hs;
                }
                if (!string.IsNullOrEmpty(e.QuoteFormat)) o["quoteFormat"] = e.QuoteFormat;
            }
            else if (e.Kind == AhpElementKind.Image)
            {
                o["path"] = e.ImagePath ?? "";
                o["fit"] = e.Fit ?? "cover";
                o["opacity"] = e.Opacity;
                if (e.CropW > 0 && e.CropH > 0)
                {
                    List<object> crop = new List<object>();
                    crop.Add((long)e.CropX); crop.Add((long)e.CropY);
                    crop.Add((long)e.CropW); crop.Add((long)e.CropH);
                    o["crop"] = crop;
                }
                if (e.Tags.Count > 0)
                {
                    List<object> ts = new List<object>();
                    foreach (string t in e.Tags) ts.Add(t);
                    o["tags"] = ts;
                }
            }
            else if (e.Kind == AhpElementKind.Video)
            {
                o["path"] = e.VideoPath ?? "";
                o["volume"] = (long)e.VideoVolume;
                o["startAt"] = e.StartAtMs;
                o["loop"] = e.Loop;
                Dictionary<string, object> pos = new Dictionary<string, object>();
                pos["leftPct"] = e.PositionLeftPct; pos["topPct"] = e.PositionTopPct;
                pos["widthPct"] = e.PositionWidthPct; pos["heightPct"] = e.PositionHeightPct;
                o["position"] = pos;
                if (e.Tags.Count > 0)
                {
                    List<object> ts = new List<object>();
                    foreach (string t in e.Tags) ts.Add(t);
                    o["tags"] = ts;
                }
            }
            else if (e.Kind == AhpElementKind.LowerThird)
            {
                o["text"] = e.LtText ?? "";
                if (!string.IsNullOrEmpty(e.LtStyleId)) o["ltStyle"] = e.LtStyleId;
                o["duration"] = e.LtDurationSec;
                o["position"] = e.LtPosition ?? "bottom";
                if (!string.IsNullOrEmpty(e.LtOverlayElementId))
                    o["overlayElement"] = e.LtOverlayElementId;
                o["manual"] = e.LtManualActivation;
            }
            return o;
        }

        public static string KindToString(AhpElementKind k)
        {
            switch (k)
            {
                case AhpElementKind.Text: return "text";
                case AhpElementKind.Verse: return "verse";
                case AhpElementKind.Image: return "image";
                case AhpElementKind.Video: return "video";
                case AhpElementKind.LowerThird: return "lowerThird";
            }
            return "text";
        }

        public static AhpElementKind KindFromString(string s)
        {
            if (s == "verse") return AhpElementKind.Verse;
            if (s == "image") return AhpElementKind.Image;
            if (s == "video") return AhpElementKind.Video;
            if (s == "lowerThird") return AhpElementKind.LowerThird;
            return AhpElementKind.Text;
        }

        // ------------------------------------------------------------ load --
        public static AhpProject FromJson(string json, AhpLoadReport report)
        {
            report = report ?? new AhpLoadReport();
            Dictionary<string, object> root = MiniJson.Parse(json);
            report.Format = MiniJson.GetString(root, "format", "");
            if (report.Format != AhpFormat.V1)
            {
                // F2.01.9: validación de compatibilidad EXPLÍCITA.
                report.Ok = false;
                report.Warn("Formato no compatible: se requiere ahp.v1 y se encontró «"
                            + (report.Format == "" ? "(ausente)" : report.Format) + "».");
                return null;
            }
            report.Ok = true;
            AhpProject p = new AhpProject();
            Dictionary<string, object> proj = MiniJson.GetObject(root, "project");
            if (proj == null)
            {
                report.Warn("Falta el objeto «project»; se carga un proyecto vacío.");
                return p;
            }
            p.Name = MiniJson.GetString(proj, "name", "");
            p.ThemeRef = MiniJson.GetString(proj, "themeRef", "");
            p.StyleOverride = MiniJson.GetString(proj, "style", "");
            p.NextId = Math.Max(1, MiniJson.GetInt(proj, "nextId", 1));
            foreach (object so in MiniJson.GetArray(proj, "scenarios"))
            {
                Dictionary<string, object> sm = so as Dictionary<string, object>;
                if (sm == null) { report.Warn("escenario inválido ignorado"); continue; }
                p.Scenarios.Add(ScenarioFromJson(sm, report));
            }
            foreach (object mo in MiniJson.GetArray(root, "media"))
            {
                Dictionary<string, object> mm = mo as Dictionary<string, object>;
                if (mm == null) continue;
                AhpMediaEntry m = new AhpMediaEntry();
                m.Path = MiniJson.GetString(mm, "path", "");
                m.Kind = MiniJson.GetString(mm, "kind", "");
                m.Sha256 = MiniJson.GetString(mm, "sha256", "");
                m.Size = MiniJson.GetInt(mm, "size", 0);
                if (m.Path.Length > 0) p.Media.Add(m);
            }
            report.IgnoredFields.AddRange(CollectUnknown(root, KnownRootKeys));
            report.IgnoredFields.AddRange(CollectUnknown(proj, KnownProjectKeys));
            return p;
        }

        private static AhpScenario ScenarioFromJson(Dictionary<string, object> o,
                                                    AhpLoadReport report)
        {
            AhpScenario s = new AhpScenario();
            s.Id = MiniJson.GetString(o, "id", "");
            s.Name = MiniJson.GetString(o, "name", "");
            s.TemplateRef = MiniJson.GetString(o, "templateRef", "");
            s.Group = MiniJson.GetString(o, "group", "");
            s.StyleOverride = MiniJson.GetString(o, "style", "");
            foreach (object eo in MiniJson.GetArray(o, "elements"))
            {
                Dictionary<string, object> em = eo as Dictionary<string, object>;
                if (em == null) { report.Warn("elemento inválido ignorado"); continue; }
                s.Elements.Add(ElementFromJson(em));
            }
            return s;
        }

        private static AhpElement ElementFromJson(Dictionary<string, object> o)
        {
            AhpElement e = new AhpElement();
            e.Id = MiniJson.GetString(o, "id", "");
            e.Kind = KindFromString(MiniJson.GetString(o, "kind", "text"));
            e.Title = MiniJson.GetString(o, "title", "");
            e.StyleOverride = MiniJson.GetString(o, "style", "");
            if (e.Kind == AhpElementKind.Text)
            {
                foreach (object lo in MiniJson.GetArray(o, "lines"))
                {
                    Dictionary<string, object> lm = lo as Dictionary<string, object>;
                    if (lm != null) e.Lines.Add(AhpLine.FromJson(lm));
                    else e.Lines.Add(new AhpLine { Text = Convert.ToString(lo, CultureInfo.InvariantCulture) });
                }
            }
            else if (e.Kind == AhpElementKind.Verse)
            {
                e.Book = MiniJson.GetString(o, "book", "");
                e.Chapter = (int)MiniJson.GetInt(o, "chapter", 0);
                e.VerseFrom = (int)MiniJson.GetInt(o, "verseFrom", 0);
                e.VerseTo = (int)MiniJson.GetInt(o, "verseTo", e.VerseFrom);
                e.Translation = MiniJson.GetString(o, "translation", "");
                foreach (object h in MiniJson.GetArray(o, "highlight"))
                    e.HighlightedWords.Add(Convert.ToString(h, CultureInfo.InvariantCulture));
                e.QuoteFormat = MiniJson.GetString(o, "quoteFormat", "");
            }
            else if (e.Kind == AhpElementKind.Image)
            {
                e.ImagePath = MiniJson.GetString(o, "path", "");
                e.Fit = MiniJson.GetString(o, "fit", "cover");
                e.Opacity = MiniJson.GetDouble(o, "opacity", 1.0);
                List<object> crop = MiniJson.GetArray(o, "crop");
                if (crop.Count >= 4)
                {
                    e.CropX = Convert.ToInt32(crop[0], CultureInfo.InvariantCulture);
                    e.CropY = Convert.ToInt32(crop[1], CultureInfo.InvariantCulture);
                    e.CropW = Convert.ToInt32(crop[2], CultureInfo.InvariantCulture);
                    e.CropH = Convert.ToInt32(crop[3], CultureInfo.InvariantCulture);
                }
                foreach (object t in MiniJson.GetArray(o, "tags"))
                    e.Tags.Add(Convert.ToString(t, CultureInfo.InvariantCulture));
            }
            else if (e.Kind == AhpElementKind.Video)
            {
                e.VideoPath = MiniJson.GetString(o, "path", "");
                e.VideoVolume = (int)MiniJson.GetInt(o, "volume", 100);
                e.StartAtMs = MiniJson.GetInt(o, "startAt", 0);
                e.Loop = MiniJson.GetBool(o, "loop", false);
                Dictionary<string, object> pos = MiniJson.GetObject(o, "position");
                if (pos != null)
                {
                    e.PositionLeftPct = MiniJson.GetDouble(pos, "leftPct", 0.5);
                    e.PositionTopPct = MiniJson.GetDouble(pos, "topPct", 0.5);
                    e.PositionWidthPct = MiniJson.GetDouble(pos, "widthPct", 1.0);
                    e.PositionHeightPct = MiniJson.GetDouble(pos, "heightPct", 1.0);
                }
                foreach (object t in MiniJson.GetArray(o, "tags"))
                    e.Tags.Add(Convert.ToString(t, CultureInfo.InvariantCulture));
            }
            else if (e.Kind == AhpElementKind.LowerThird)
            {
                e.LtText = MiniJson.GetString(o, "text", "");
                e.LtStyleId = MiniJson.GetString(o, "ltStyle", "");
                e.LtDurationSec = MiniJson.GetDouble(o, "duration", 6.0);
                e.LtPosition = MiniJson.GetString(o, "position", "bottom");
                e.LtOverlayElementId = MiniJson.GetString(o, "overlayElement", "");
                e.LtManualActivation = MiniJson.GetBool(o, "manual", true);
            }
            return e;
        }

        private static readonly List<string> KnownRootKeys = new List<string>
            { "format", "project", "media" };
        private static readonly List<string> KnownProjectKeys = new List<string>
            { "name", "themeRef", "style", "nextId", "scenarios" };

        // F2.01.8: campos desconocidos IGNORADOS (se informan para el usuario).
        private static List<string> CollectUnknown(Dictionary<string, object> o,
                                                   List<string> known)
        {
            List<string> unknown = new List<string>();
            foreach (string k in o.Keys)
                if (!known.Contains(k)) unknown.Add(k);
            return unknown;
        }

        // ------------------------------------------------------------- zip --
        /// <summary>F2.01.7: empaquetado ZIP = project.json + media/* (bytes
        /// de cada recurso se leen de baseDir). Las rutas del proyecto son
        /// RELATIVAS al paquete (F2.01.10: idéntico en x86/x64).</summary>
        public static byte[] ToZip(AhpProject p, string baseDir)
        {
            List<KeyValuePair<string, byte[]>> entries =
                new List<KeyValuePair<string, byte[]>>();
            entries.Add(new KeyValuePair<string, byte[]>(
                "project.json", Encoding.UTF8.GetBytes(ToJson(p))));
            foreach (AhpMediaEntry m in p.Media)
            {
                string full = Path.Combine(baseDir, m.Path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full))
                    entries.Add(new KeyValuePair<string, byte[]>(m.Path, File.ReadAllBytes(full)));
            }
            using (MemoryStream ms = new MemoryStream())
            {
                ZipWriter.WriteEntries(ms, entries);
                return ms.ToArray();
            }
        }

        /// <summary>Abre un paquete ahp.v1 (.zip) extrayendo media/ a baseDir.</summary>
        public static AhpProject FromZip(byte[] zip, string baseDir, AhpLoadReport report)
        {
            using (MemoryStream ms = new MemoryStream(zip))
            {
                List<ZipEntryData> entries = ZipReader.ReadAll(ms);
                AhpProject p = null;
                foreach (ZipEntryData en in entries)
                {
                    string norm = en.Name.Replace('\\', '/');
                    if (norm == "project.json")
                        p = FromJson(Encoding.UTF8.GetString(en.Data), report);
                    else if (norm.StartsWith("media/", StringComparison.OrdinalIgnoreCase))
                    {
                        string dest = Path.Combine(baseDir,
                            norm.Replace('/', Path.DirectorySeparatorChar));
                        string dir = Path.GetDirectoryName(dest);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        File.WriteAllBytes(dest, en.Data);
                    }
                }
                if (p == null)
                {
                    report.Ok = false;
                    report.Warn("El paquete no contiene project.json.");
                }
                return p;
            }
        }
    }
}
