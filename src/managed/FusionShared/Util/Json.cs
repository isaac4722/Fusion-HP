// ============================================================================
//  Fusion-HP · FusionShared/Util/Json.cs — JSON mínimo sin dependencias
//  Compatible .NET Framework 3.5 SP1 → 4.8 [SPEC §3.5]. Lectura tolerante
//  (campos nuevos ignorables [SPEC §5.3b]) y escritura estable (orden fijo).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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

        // ---------------- Escritura ----------------
        public string ToJsonString()
        {
            var sb = new StringBuilder();
            Write(sb, this);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, JsonValue v)
        {
            switch (v.Type)
            {
                case Kind.Null: sb.Append("null"); break;
                case Kind.Bool: sb.Append(v.Bool ? "true" : "false"); break;
                case Kind.Number:
                    if (double.IsInfinity(v.Number) || double.IsNaN(v.Number)) sb.Append("null");
                    else if (v.Number == Math.Floor(v.Number) && Math.Abs(v.Number) < 9.00719925474099E+15)
                        sb.Append(((long)v.Number).ToString(CultureInfo.InvariantCulture));
                    else sb.Append(v.Number.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case Kind.String: WriteString(sb, v.Str); break;
                case Kind.Array:
                    sb.Append('[');
                    if (v.Items != null)
                        for (int i = 0; i < v.Items.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            Write(sb, v.Items[i]);
                        }
                    sb.Append(']');
                    break;
                case Kind.Object:
                    sb.Append('{');
                    if (v.Fields != null)
                        for (int i = 0; i < v.Fields.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            WriteString(sb, v.Fields[i].Key);
                            sb.Append(':');
                            Write(sb, v.Fields[i].Value ?? Null());
                        }
                    sb.Append('}');
                    break;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------------- Lectura ----------------
        public static JsonValue Parse(string text)
        {
            int pos = 0;
            var v = ParseValue(text, ref pos);
            SkipWs(text, ref pos);
            if (pos != text.Length) throw new FormatException("contenido adicional al final del JSON (pos " + pos + ")");
            return v;
        }

        private static void SkipWs(string s, ref int pos)
        {
            while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\n' || s[pos] == '\r')) pos++;
        }

        private static JsonValue ParseValue(string s, ref int pos)
        {
            SkipWs(s, ref pos);
            if (pos >= s.Length) throw new FormatException("JSON truncado");
            char c = s[pos];
            if (c == '{') return ParseObject(s, ref pos);
            if (c == '[') return ParseArray(s, ref pos);
            if (c == '"')
            {
                string str;
                ParseString(s, ref pos, out str);
                return Make(str);
            }
            if (c == 't' && pos + 4 <= s.Length && s.Substring(pos, 4) == "true") { pos += 4; return Make(true); }
            if (c == 'f' && pos + 5 <= s.Length && s.Substring(pos, 5) == "false") { pos += 5; return Make(false); }
            if (c == 'n' && pos + 4 <= s.Length && s.Substring(pos, 4) == "null") { pos += 4; return Null(); }
            return ParseNumber(s, ref pos);
        }

        private static JsonValue ParseObject(string s, ref int pos)
        {
            var v = Object();
            pos++; // {
            SkipWs(s, ref pos);
            if (pos < s.Length && s[pos] == '}') { pos++; return v; }
            while (true)
            {
                SkipWs(s, ref pos);
                if (pos >= s.Length || s[pos] != '"') throw new FormatException("clave de objeto esperada (pos " + pos + ")");
                string key;
                ParseString(s, ref pos, out key);
                SkipWs(s, ref pos);
                if (pos >= s.Length || s[pos] != ':') throw new FormatException("':' esperado (pos " + pos + ")");
                pos++;
                var val = ParseValue(s, ref pos);
                v.Set(key, val);
                SkipWs(s, ref pos);
                if (pos >= s.Length) throw new FormatException("objeto sin cerrar");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == '}') { pos++; return v; }
                throw new FormatException("',' o '}' esperado (pos " + pos + ")");
            }
        }

        private static JsonValue ParseArray(string s, ref int pos)
        {
            var v = Array();
            pos++; // [
            SkipWs(s, ref pos);
            if (pos < s.Length && s[pos] == ']') { pos++; return v; }
            while (true)
            {
                var val = ParseValue(s, ref pos);
                v.Add(val);
                SkipWs(s, ref pos);
                if (pos >= s.Length) throw new FormatException("arreglo sin cerrar");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == ']') { pos++; return v; }
                throw new FormatException("',' o ']' esperado (pos " + pos + ")");
            }
        }

        private static void ParseString(string s, ref int pos, out string result)
        {
            pos++; // comilla inicial
            var sb = new StringBuilder();
            while (pos < s.Length)
            {
                char c = s[pos++];
                if (c == '"') { result = sb.ToString(); return; }
                if (c == '\\')
                {
                    if (pos >= s.Length) break;
                    char e = s[pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (pos + 4 > s.Length) throw new FormatException("\\uXXXX incompleto");
                            sb.Append((char)int.Parse(s.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            pos += 4;
                            break;
                        default: throw new FormatException("escape desconocido \\" + e);
                    }
                }
                else sb.Append(c);
            }
            throw new FormatException("cadena sin cerrar");
        }

        private static JsonValue ParseNumber(string s, ref int pos)
        {
            int start = pos;
            if (pos < s.Length && (s[pos] == '-' || s[pos] == '+')) pos++;
            while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.' || s[pos] == 'e' || s[pos] == 'E' ||
                   s[pos] == '-' || s[pos] == '+')) pos++;
            if (pos == start) throw new FormatException("valor no reconocido (pos " + pos + ")");
            double d;
            if (!double.TryParse(s.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("numero invalido: " + s.Substring(start, pos - start));
            return Make(d);
        }
    }

    /// <summary>Utilidades de serialización del modelo ahp.v1.</summary>
    public static class Json
    {
        public static JsonValue ParseFile(string path)
        {
            return JsonValue.Parse(System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8));
        }
        public static JsonValue ParseText(string text) { return JsonValue.Parse(text); }
        public static void WriteFile(string path, JsonValue v)
        {
            System.IO.File.WriteAllText(path, v.ToJsonString(), System.Text.Encoding.UTF8);
        }
    }
}
