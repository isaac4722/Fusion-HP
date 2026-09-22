// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  TriggerEngine.cs : motor de ACTIVADORES (requisito del spec §3.3, espejo del
//  sistema de Holyrics): reglas "evento → condiciones → acciones" evaluadas en
//  tiempo real. Lógica PURA (sin UI, sin red): la UI/integraciones alimentan los
//  eventos y ejecutan las acciones (ITriggerSink) → 100% testeable en el arnés.
//
//  Eventos de entrada (los publica el que corresponda):
//    item_changed   {item, title, kind}        — motor (LUMINA_EV_ITEM_CHANGED)
//    slide_changed  {index, item, total}       — motor (LUMINA_EV_SLIDE_CHANGED)
//    song_started   {item, title}              — primer slide de un ítem canción
//    video_ended    {item, title, path}        — reproductor de video
//    midi_note      {channel, note, velocity}  — winmm MIDI IN
//    midi_cc        {channel, controller, value}
//    midi_program   {channel, program}
//    api_webhook    {event, payload}           — webhook OBS (ApiServer)
//
//  Condiciones (Match): pares clave/valor; el valor puede llevar prefijos:
//    "equals" (por defecto, case-insensitive) · "contains:X" · "gte:N" · "lte:N"
//
//  Acciones (las ejecuta el ITriggerSink de la UI):
//    obs_scene      {scene}                    — cambia escena en OBS Studio
//    obs_source_text {source, text}            — texto en vivo a una fuente OBS
//    play_audio     {path, volume}             — audio vía WMP oculto
//    show_text      {text, seconds}            — aviso en pantalla (lower third)
//    set_theme      {theme}                    — cambia el tema activo
//    api_cmd        {action, index, on}        — comando al motor (next/black/…)
//    midi_out       {status, data1, data2}     — mensaje MIDI corto de salida
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace lumina.core
{
    /// <summary>Una acción ejecutable (el sink decide cómo).</summary>
    public sealed class TriggerAction
    {
        public string Type = string.Empty;        // obs_scene | play_audio | …
        public Dictionary<string, object> Params = new Dictionary<string, object>();

        public string GetString(string key, string def)
        {
            object v;
            if (Params.TryGetValue(key, out v) && v != null) return Convert.ToString(v, CultureInfo.InvariantCulture);
            return def;
        }

        public int GetInt(string key, int def)
        {
            object v;
            if (Params.TryGetValue(key, out v) && v != null)
            {
                int i;
                if (v is int) return (int)v;
                if (int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out i)) return i;
            }
            return def;
        }

        public Dictionary<string, object> ToDict()
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["type"] = Type;
            o["params"] = Params;
            return o;
        }

        public static TriggerAction FromDict(Dictionary<string, object> o)
        {
            TriggerAction a = new TriggerAction();
            if (o == null) return a;
            a.Type = MiniJson.GetString(o, "type", string.Empty);
            Dictionary<string, object> p = MiniJson.GetObject(o, "params");
            if (p != null) a.Params = p;
            return a;
        }
    }

    /// <summary>Regla: evento + condiciones + acciones.</summary>
    public sealed class TriggerRule
    {
        public string Id = string.Empty;          // identificador estable
        public string Name = string.Empty;        // legible
        public bool Enabled = true;
        public string Event = string.Empty;       // nombre del evento
        public Dictionary<string, object> Match = new Dictionary<string, object>();
        public List<TriggerAction> Actions = new List<TriggerAction>();

        public Dictionary<string, object> ToDict()
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["id"] = Id;
            o["name"] = Name;
            o["enabled"] = Enabled;
            o["event"] = Event;
            o["match"] = Match;
            List<object> acts = new List<object>();
            foreach (TriggerAction a in Actions) acts.Add(a.ToDict());
            o["actions"] = acts;
            return o;
        }

        public static TriggerRule FromDict(Dictionary<string, object> o)
        {
            TriggerRule r = new TriggerRule();
            if (o == null) return r;
            r.Id = MiniJson.GetString(o, "id", string.Empty);
            r.Name = MiniJson.GetString(o, "name", string.Empty);
            r.Enabled = MiniJson.GetBool(o, "enabled", true);
            r.Event = MiniJson.GetString(o, "event", string.Empty);
            Dictionary<string, object> m = MiniJson.GetObject(o, "match");
            if (m != null) r.Match = m;
            foreach (object ao in MiniJson.GetArray(o, "actions"))
            {
                Dictionary<string, object> ad = ao as Dictionary<string, object>;
                if (ad != null) r.Actions.Add(TriggerAction.FromDict(ad));
            }
            return r;
        }
    }

    /// <summary>Destino de las acciones (lo implementa la UI).</summary>
    public interface ITriggerSink
    {
        /// <summary>Ejecuta una acción. Devuelve mensaje de estado (o null).</summary>
        string ExecuteTriggerAction(TriggerRule rule, TriggerAction action);
    }

    public sealed class TriggerEngine
    {
        private readonly List<TriggerRule> _rules = new List<TriggerRule>();
        private readonly object _sync = new object();
        private ITriggerSink _sink;

        /// <summary>Se dispara (hilo del llamador) cuando una regla coincide.</summary>
        public event Action<TriggerRule, TriggerAction> ActionFired;

        public void SetSink(ITriggerSink sink) { lock (_sync) { _sink = sink; } }

        public List<TriggerRule> RulesSnapshot()
        {
            lock (_sync) { return new List<TriggerRule>(_rules); }
        }

        public void SetRules(IEnumerable<TriggerRule> rules)
        {
            lock (_sync)
            {
                _rules.Clear();
                if (rules != null) _rules.AddRange(rules);
            }
        }

        // ------------------------------------------------------------- disco

        /// <summary>Carga data/triggers.json (tolerante: archivo ausente/corrupto → vacío).</summary>
        public static TriggerEngine Load(string filePath)
        {
            TriggerEngine e = new TriggerEngine();
            try
            {
                if (File.Exists(filePath))
                {
                    Dictionary<string, object> o = MiniJson.Parse(File.ReadAllText(filePath, new UTF8Encoding(false)));
                    foreach (object ro in MiniJson.GetArray(o, "rules"))
                    {
                        Dictionary<string, object> rd = ro as Dictionary<string, object>;
                        if (rd != null) e._rules.Add(TriggerRule.FromDict(rd));
                    }
                }
            }
            catch (Exception)
            {
                // JSON corrupto: reglas vacías (nunca bloquear el arranque).
                e._rules.Clear();
            }
            return e;
        }

        public void Save(string filePath)
        {
            try
            {
                Dictionary<string, object> o = new Dictionary<string, object>();
                List<object> arr = new List<object>();
                List<TriggerRule> snap = RulesSnapshot();
                foreach (TriggerRule r in snap) arr.Add(r.ToDict());
                o["rules"] = arr;
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(filePath, MiniJson.Serialize(o) + "\n", new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // Sin permisos/espacio: el llamador lo comunicará por otros medios.
            }
        }

        // ------------------------------------------------------------- fuego

        /// <summary>
        /// Evalúa todas las reglas del evento dado contra el contexto y ejecuta
        /// las acciones que coincidan. Devuelve cuántas reglas dispararon.
        /// </summary>
        public int Fire(string eventName, Dictionary<string, object> context)
        {
            if (string.IsNullOrEmpty(eventName)) return 0;
            if (context == null) context = new Dictionary<string, object>();
            List<TriggerRule> matches = new List<TriggerRule>();
            lock (_sync)
            {
                foreach (TriggerRule r in _rules)
                {
                    if (!r.Enabled) continue;
                    if (!string.Equals(r.Event, eventName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (RuleMatches(r, context)) matches.Add(r);
                }
            }
            foreach (TriggerRule r in matches)
            {
                foreach (TriggerAction a in r.Actions)
                {
                    Action<TriggerRule, TriggerAction> ev = ActionFired;
                    if (ev != null)
                    {
                        try { ev(r, a); } catch (Exception) { }
                    }
                    ITriggerSink sink;
                    lock (_sync) { sink = _sink; }
                    if (sink != null)
                    {
                        try { sink.ExecuteTriggerAction(r, a); }
                        catch (Exception) { }
                    }
                }
            }
            return matches.Count;
        }

        /// <summary>¿La regla coincide con el contexto? (público para tests).</summary>
        public static bool RuleMatches(TriggerRule rule, Dictionary<string, object> context)
        {
            foreach (KeyValuePair<string, object> kv in rule.Match)
            {
                object ctxVal;
                if (!context.TryGetValue(kv.Key, out ctxVal)) return false;
                if (!ValueMatches(kv.Value, ctxVal)) return false;
            }
            return true;
        }

        /// <summary>Comparador con prefijos contains:/gte:/lte: (público para tests).</summary>
        public static bool ValueMatches(object pattern, object value)
        {
            string pat = pattern == null ? string.Empty : Convert.ToString(pattern, CultureInfo.InvariantCulture);
            string val = value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);

            if (string.Equals(pat, "*", StringComparison.Ordinal)) return true;

            if (pat.StartsWith("contains:", StringComparison.OrdinalIgnoreCase))
            {
                return val.IndexOf(pat.Substring(9), StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (pat.StartsWith("gte:", StringComparison.OrdinalIgnoreCase))
            {
                double a, b;
                if (!TryNum(pat.Substring(4), out a) || !TryNum(val, out b)) return false;
                return b >= a;
            }
            if (pat.StartsWith("lte:", StringComparison.OrdinalIgnoreCase))
            {
                double a, b;
                if (!TryNum(pat.Substring(4), out a) || !TryNum(val, out b)) return false;
                return b <= a;
            }
            // equals numérico si ambos son números
            double x, y;
            if (TryNum(pat, out x) && TryNum(val, out y)) return Math.Abs(x - y) < 0.000001;
            return string.Equals(pat, val, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryNum(string s, out double v)
        {
            v = 0;
            if (string.IsNullOrEmpty(s)) return false;
            return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
