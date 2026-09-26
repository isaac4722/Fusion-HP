// ============================================================================
//  Fusion-HP · FusionShared/Util/Json.cs — fachada JSON del producto (v4.1.0).
//  Compatible .NET Framework 3.5 SP1 → 4.8 [SPEC §3.5]. Lectura tolerante
//  (campos nuevos ignorables [SPEC §5.3b]) y escritura estable (orden fijo).
//
//  v4.1.0: el MOTOR de parseo/serialización es Newtonsoft.Json 13.0.3 (MIT,
//  net35 — [DEPENDENCIAS.md]); la API pública de JsonValue/Json NO cambia
//  (campos Type/Bool/Number/Str/Items/Fields, fábricas, Get*, ToJsonString,
//  Parse con FormatException). Newtonsoft corrige los casos límite del parser
//  propio: escapes \u con pares sustitutos, números exóticos (1e999, -0),
//  BOM y profundidad extrema.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Fusion.Shared
{
    /// <summary>Valor JSON: null, bool, double, string, array u objeto (orden preservado).</summary>
    public sealed class JsonValue
    {
        public enum Kind { Null, Bool, Number, String, Array, Object }

        public Kind Type;
        public bool Bool;
        public double Number;
        public string Str;
        public List<JsonValue> Items;
        public List<KeyValuePair<string, JsonValue>> Fields;

        public static JsonValue Null() { return new JsonValue { Type = Kind.Null }; }
        public static JsonValue Make(bool b) { return new JsonValue { Type = Kind.Bool, Bool = b }; }
        public static JsonValue Make(double n) { return new JsonValue { Type = Kind.Number, Number = n }; }
        public static JsonValue Make(int n) { return new JsonValue { Type = Kind.Number, Number = n }; }
        public static JsonValue Make(long n) { return new JsonValue { Type = Kind.Number, Number = n }; }
        public static JsonValue Make(string s)
        {
            return s == null ? Null() : new JsonValue { Type = Kind.String, Str = s };
        }
        public static JsonValue Array()
        {
            return new JsonValue { Type = Kind.Array, Items = new List<JsonValue>() };
        }
        public static JsonValue Object()
        {
            return new JsonValue { Type = Kind.Object, Fields = new List<KeyValuePair<string, JsonValue>>() };
        }

        public JsonValue Add(JsonValue v) { Items.Add(v); return this; }
        public JsonValue AddStrings(IEnumerable<string> list)
        {
            foreach (var s in list) Items.Add(Make(s));
            return this;
        }
        public JsonValue Set(string key, JsonValue v)
        {
            for (int i = 0; i < Fields.Count; i++)
                if (Fields[i].Key == key) { Fields[i] = new KeyValuePair<string, JsonValue>(key, v); return this; }
            Fields.Add(new KeyValuePair<string, JsonValue>(key, v));
            return this;
        }

        public bool IsNull { get { return Type == Kind.Null; } }
        public bool Has(string key)
        {
            if (Type != Kind.Object) return false;
            for (int i = 0; i < Fields.Count; i++) if (Fields[i].Key == key) return true;
            return false;
        }
        public JsonValue Get(string key)
        {
            if (Type == Kind.Object)
                for (int i = 0; i < Fields.Count; i++)
                    if (Fields[i].Key == key)
                    {
                        var v = Fields[i].Value;
                        return v ?? Null();
                    }
            return Null();
        }
        public string GetStr(string key, string def)
        {
            var v = Get(key);
            return v.Type == Kind.String ? v.Str : def;
        }
        public string GetStr(string key) { return GetStr(key, null); }
        public double GetNum(string key, double def)
        {
            var v = Get(key);
            return v.Type == Kind.Number ? v.Number : def;
        }
        public int GetInt(string key, int def)
        {
            var v = Get(key);
            return v.Type == Kind.Number ? (int)v.Number : def;
        }
        public bool GetBool(string key, bool def)
        {
            var v = Get(key);
            if (v.Type == Kind.Bool) return v.Bool;
            if (v.Type == Kind.Number) return v.Number != 0;
            return def;
        }
        public List<JsonValue> GetArray(string key)
        {
            var v = Get(key);
            return v.Type == Kind.Array ? v.Items : null;
        }
        public List<string> GetStringArray(string key)
        {
            var v = Get(key);
            if (v.Type != Kind.Array) return new List<string>();
            var r = new List<string>();
            foreach (var i in v.Items) if (i.Type == Kind.String) r.Add(i.Str);
            return r;
        }
        public IEnumerable<JsonValue> AsArray
        {
            get { return Type == Kind.Array ? Items : new List<JsonValue>(); }
        }

        // ---------------- Escritura (motor Newtonsoft, reglas estables) --------
        public string ToJsonString()
        {
            var t = ToJToken(this);
            var sb = new StringBuilder();
            using (var sw = new StringWriter(sb, CultureInfo.InvariantCulture))
            using (var w = new JsonTextWriter(sw))
            {
                w.Formatting = Formatting.None;
                t.WriteTo(w);
            }
            return sb.ToString();
        }

        private static JToken ToJToken(JsonValue v)
        {
            switch (v.Type)
            {
                case Kind.Null: return JValue.CreateNull();
                case Kind.Bool: return new JValue(v.Bool);
                case Kind.Number:
                    // mismos umbrales que el escritor histórico: enteros sin .0
                    if (double.IsInfinity(v.Number) || double.IsNaN(v.Number)) return JValue.CreateNull();
                    if (v.Number == Math.Floor(v.Number) && Math.Abs(v.Number) < 9.00719925474099E+15)
                        return new JValue((long)v.Number);
                    return new JValue(v.Number);
                case Kind.String: return new JValue(v.Str);
                case Kind.Array:
                    var a = new JArray();
                    if (v.Items != null)
                        foreach (var i in v.Items) a.Add(ToJToken(i));
                    return a;
                case Kind.Object:
                    var o = new JObject();
                    if (v.Fields != null)
                        foreach (var p in v.Fields) o[p.Key] = ToJToken(p.Value ?? Null());
                    return o;
                default: return JValue.CreateNull();
            }
        }

        // ---------------- Lectura (motor Newtonsoft) ---------------------------
        public static JsonValue Parse(string text)
        {
            if (text == null) throw new FormatException("JSON nulo");
            JToken token;
            try
            {
                token = JToken.Parse(text);
            }
            catch (JsonReaderException e)
            {
                throw new FormatException(e.Message);
            }
            var v = FromJToken(token);
            if (v == null) throw new FormatException("JSON sin valor raíz");
            return v;
        }

        private static JsonValue FromJToken(JToken t)
        {
            if (t == null) return Null();
            switch (t.Type)
            {
                case JTokenType.Null: return Null();
                case JTokenType.Boolean: return Make((bool)t);
                case JTokenType.Integer:
                    long l;
                    try { l = (long)t; return Make(l); }
                    catch (OverflowException) { return Make((double)t); }
                case JTokenType.Float: return Make((double)t);
                case JTokenType.String: return Make((string)t);
                case JTokenType.Array:
                    var a = Array();
                    foreach (var i in (JArray)t) a.Add(FromJToken(i));
                    return a;
                case JTokenType.Object:
                    var o = Object();
                    foreach (var p in (JObject)t) o.Set(p.Key, FromJToken(p.Value));
                    return o;
                default:
                    // casos exóticos (fecha/bytes): se conservan como texto JSON
                    return Make(t.ToString(Formatting.None));
            }
        }
    }

    /// <summary>Utilidades de serialización del modelo ahp.v1.</summary>
    public static class Json
    {
        public static JsonValue ParseFile(string path)
        {
            return JsonValue.Parse(File.ReadAllText(path, Encoding.UTF8));
        }
        public static JsonValue ParseText(string text) { return JsonValue.Parse(text); }
        public static void WriteFile(string path, JsonValue v)
        {
            File.WriteAllText(path, v.ToJsonString(), Encoding.UTF8);
        }
    }
}
