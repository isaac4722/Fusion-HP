// ============================================================================
//  Fusion-HP · FusionStudio/Services/TriggerEngine.cs — motor de Triggers
//  [SPEC §8.3]: reglas evento → condiciones → acciones, definibles como JSON
//  (sin código) con orden de evaluación determinista. Acciones MVP: cambiar
//  escena en OBS, cambiar tema, mostrar mensaje. Las etiquetas semánticas
//  ("lento" → tema "calma") son el caso canónico [SPEC §5.1.4].
// ============================================================================
using System;
using System.Collections.Generic;
using Fusion.Shared;
using Fusion.Shared.Model;

namespace Fusion.Studio.Services
{
    public class TriggerRule
    {
        public string Id;
        public string Event;                 // "projection" | "tag" | "video.start" | "video.end" | "api" | "schedule"
        public string Tag;                   // etiqueta semántica (evento "tag")
        public List<string> Conditions = new List<string>();   // "theme:calma", "element:text"
        public string Action;                // "obs.scene" | "theme.change" | "message"
        public string Parameter;             // nombre de escena / tema / texto
        public bool Enabled = true;

        public static TriggerRule FromJson(JsonValue j)
        {
            var r = new TriggerRule();
            r.Id = j.GetStr("id", "trg-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            r.Event = j.GetStr("event", "projection");
            r.Tag = j.GetStr("tag");
            r.Conditions = j.GetStringArray("conditions");
            r.Action = j.GetStr("action", "");
            r.Parameter = j.GetStr("param", "");
            r.Enabled = j.GetBool("enabled", true);
            return r;
        }

        public JsonValue ToJson()
        {
            var j = JsonValue.Object();
            j.Set("id", JsonValue.Make(Id));
            j.Set("event", JsonValue.Make(Event));
            if (Tag != null) j.Set("tag", JsonValue.Make(Tag));
            var c = JsonValue.Array();
            c.AddStrings(Conditions);
            j.Set("conditions", c);
            j.Set("action", JsonValue.Make(Action));
            j.Set("param", JsonValue.Make(Parameter));
            j.Set("enabled", JsonValue.Make(Enabled));
            return j;
        }
    }

    public class TriggerEngine
    {
        readonly List<TriggerRule> rules = new List<TriggerRule>();
        public event Action<string> Logged;
        public Func<string, bool> ObsSceneChanger;     // devuelve true si OK
        public Action<string> ThemeChanger;
        public Action<string> MessageShower;

        public IList<TriggerRule> Rules { get { return rules.AsReadOnly(); } }

        public void Load(LiveOrchestrator live)
        {
            rules.Clear();
            if (live.Project == null) return;
            string path = System.IO.Path.Combine(live.Settings.DataDir, "triggers.json");
            if (System.IO.File.Exists(path))
            {
                try
                {
                    var root = Json.ParseFile(path);
                    var arr = root.GetArray("rules");
                    if (arr != null) foreach (var r in arr) rules.Add(TriggerRule.FromJson(r));
                }
                catch (Exception ex)
                {
                    Log("triggers.json ilegible: " + ex.Message);
                }
            }
        }

        public void Save(LiveOrchestrator live)
        {
            var root = JsonValue.Object();
            var arr = JsonValue.Array();
            foreach (var r in rules) arr.Add(r.ToJson());
            root.Set("rules", arr);
            Json.WriteFile(System.IO.Path.Combine(live.Settings.DataDir, "triggers.json"), root);
        }

        public void Add(TriggerRule r) { rules.Add(r); }

        /// <summary>Evalúa un evento con contexto. Orden determinista [SPEC §8.3.3].</summary>
        public void Fire(string evt, Scenario scn, Element el, LiveOrchestrator live)
        {
            string themeName = live.Project != null && live.Project.ActiveTheme() != null ? live.Project.ActiveTheme().Name : "";
            string elementKind = el != null ? el.KindKey : "";
            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                if (!r.Enabled) continue;
                bool match = false;
                if (r.Event == "projection") match = (evt == "projection");
                else if (r.Event == "tag") match = (evt == "tag" && scn != null && scn.Tags.Contains(r.Tag)) ||
                                                 (el != null && el.Tags.Contains(r.Tag));
                else if (r.Event == "video.start") match = (evt == "video.start");
                else if (r.Event == "video.end") match = (evt == "video.end");
                else if (r.Event == "api") match = (evt == "api");
                else if (r.Event == "schedule") match = (evt == "schedule");
                if (!match) continue;

                bool condOk = true;
                foreach (var c in r.Conditions)
                {
                    if (c.StartsWith("theme:") && !string.Equals(themeName, c.Substring(6), StringComparison.OrdinalIgnoreCase)) condOk = false;
                    if (c == "element:text" && elementKind != "text") condOk = false;
                    if (c == "element:video" && elementKind != "video") condOk = false;
                    if (c == "element:verse" && elementKind != "verse") condOk = false;
                }
                if (!condOk) continue;

                switch (r.Action)
                {
                    case "obs.scene":
                        {
                            var f = ObsSceneChanger;
                            bool ok = f != null && f(r.Parameter);
                            Log(ok ? "Trigger " + r.Id + ": escena OBS → " + r.Parameter
                                   : "Trigger " + r.Id + ": OBS no disponible");
                            break;
                        }
                    case "theme.change":
                        {
                            if (live.Project != null)
                            {
                                foreach (var t in live.Project.Themes)
                                    if (string.Equals(t.Name, r.Parameter, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(t.Id, r.Parameter, StringComparison.OrdinalIgnoreCase))
                                    {
                                        live.Project.ThemeRef = t.Id;
                                        var tc = ThemeChanger;
                                        if (tc != null) tc(t.Name);
                                        Log("Trigger " + r.Id + ": tema → " + t.Name);
                                        break;
                                    }
                            }
                            break;
                        }
                    case "message":
                        {
                            var m = MessageShower;
                            if (m != null) { m(r.Parameter); Log("Trigger " + r.Id + ": mensaje en pantalla"); }
                            break;
                        }
                }
            }
        }

        void Log(string m)
        {
            var h = Logged;
            if (h != null) h(m);
        }
    }
}
