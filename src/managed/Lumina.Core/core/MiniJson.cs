// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MiniJson : parser + serializador JSON propio para net35 (sin System.Text.Json
//  ni Json.NET). Soporta object/array/string (escapes \n \r \t \b \f \" \\ \/
//  y \uXXXX con pares suplentes)/number (long entero, double fraccionario)/
//  bool/null. Tolerante a BOM UTF-8 y espacios en blanco.
//
//  Representación:
//    object -> Dictionary<string, object>   (orden de inserción NO conservado;
//                                            para el API basta el contenido)
//    array  -> List<object> (también object[] al serializar)
//    string -> string
//    number -> long (enteros) | double (con punto/exponente)
//    bool   -> bool      null -> null
// ============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace lumina.core
{
    public static class MiniJson
    {
        /* ------------------------------------------------------------ parse -- */

        /// <summary>Parsea un documento JSON cuyo nivel superior DEBE ser un objeto.</summary>
        public static Dictionary<string, object> Parse(string json)
        {
            object v = ParseAny(json);
            Dictionary<string, object> o = v as Dictionary<string, object>;
            if (o == null)
                throw new FormatException("El JSON de nivel superior no es un objeto.");
            return o;
        }

        /// <summary>Parsea cualquier JSON (objeto, arreglo o escalar).</summary>
        public static object ParseAny(string json)
        {
            if (json == null) throw new FormatException("JSON nulo.");
            int i = 0;
            // BOM UTF-8 decodificado (U+FEFF) y espacios iniciales.
            if (json.Length > 0 && json[0] == '\uFEFF') i = 1;
            object v = ParseValue(json, ref i);
            SkipWs(json, ref i);
            if (i != json.Length)
                throw new FormatException("Contenido sobrante tras el JSON (pos. " + i + ").");
            return v;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') i++;
                else break;
            }
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON truncado.");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static void Expect(string s, ref int i, string lit)
        {
            if (string.CompareOrdinal(s, i, lit, 0, lit.Length) != 0)
                throw new FormatException("Literal JSON inválido en la posición " + i + ".");
            i += lit.Length;
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            i++; // '{'
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return o; }
            for (;;)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"')
                    throw new FormatException("Se esperaba una clave de objeto en la posición " + i + ".");
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new FormatException("Se esperaba ':' en la posición " + i + ".");
                i++;
                o[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("Objeto sin cerrar.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return o; }
                throw new FormatException("Se esperaba ',' o '}' en la posición " + i + ".");
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            List<object> a = new List<object>();
            i++; // '['
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            for (;;)
            {
                a.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("Arreglo sin cerrar.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return a; }
                throw new FormatException("Se esperaba ',' o ']' en la posición " + i + ".");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++; // '"'
            for (;;)
            {
                if (i >= s.Length) throw new FormatException("Cadena sin cerrar.");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (i >= s.Length) throw new FormatException("Escape sin cerrar.");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw new FormatException("Escape \\u truncado.");
                            int cp;
                            if (!int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
                                              CultureInfo.InvariantCulture, out cp))
                                throw new FormatException("Escape \\u inválido.");
                            i += 4;
                            sb.Append((char)cp);
                            // Los pares suplentes se conservan como chars UTF-16
                            // (el \\uXXXX bajo se apila y .NET lo une al serializar).
                            break;
                        default:
                            throw new FormatException("Escape desconocido '\\" + e + "'.");
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            bool isDouble = false;
            while (i < s.Length)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') { i++; continue; }
                if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { isDouble = true; i++; continue; }
                break;
            }
            string tok = s.Substring(start, i - start);
            if (tok.Length == 0) throw new FormatException("Número vacío en la posición " + start + ".");
            if (!isDouble)
            {
                long l;
                if (long.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
                isDouble = true; // entero demasiado grande: degradar a double
            }
            double d;
            if (!double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("Número inválido '" + tok + "'.");
            return d;
        }

        /* -------------------------------------------------------- serialize -- */

        /// <summary>Serializa un grafo de objetos a JSON (UTF-8 seguro, sin BOM).</summary>
        public static string Serialize(object o)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, o);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object o)
        {
            if (o == null) { sb.Append("null"); return; }

            if (o is string) { WriteString(sb, (string)o); return; }
            if (o is bool) { sb.Append((bool)o ? "true" : "false"); return; }
            if (o is char) { WriteString(sb, ((char)o).ToString()); return; }

            // Numéricos: enteros sin punto; flotantes con cultura invariante.
            if (o is int) { sb.Append(((int)o).ToString(CultureInfo.InvariantCulture)); return; }
            if (o is long) { sb.Append(((long)o).ToString(CultureInfo.InvariantCulture)); return; }
            if (o is short) { sb.Append(((short)o).ToString(CultureInfo.InvariantCulture)); return; }
            if (o is byte) { sb.Append(((byte)o).ToString(CultureInfo.InvariantCulture)); return; }
            if (o is sbyte) { sb.Append(((sbyte)o).ToString(CultureInfo.InvariantCulture)); return; }
            if (o is uint) { sb.Append(((uint)o).ToString(CultureInfo.InvariantCulture)); return; }
            if (o is ulong) { sb.Append(((ulong)o).ToString(CultureInfo.InvariantCulture)); return; }
            if (o is float) { WriteDouble(sb, ((float)o)); return; }
            if (o is double) { WriteDouble(sb, ((double)o)); return; }
            if (o is decimal) { sb.Append(((decimal)o).ToString(CultureInfo.InvariantCulture)); return; }

            IDictionary<string, object> dict = o as IDictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key ?? string.Empty);
                    sb.Append(':');
                    WriteValue(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }

            IDictionary legacy = o as IDictionary; // Dictionary no genérico (raro pero válido)
            if (legacy != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry kv in legacy)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    WriteValue(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }

            IEnumerable en = o as IEnumerable; // List<object>, object[], …
            if (en != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in en)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item);
                }
                sb.Append(']');
                return;
            }

            // Último recurso: ToString como cadena (nunca lanzar).
            WriteString(sb, o.ToString());
        }

        private static void WriteDouble(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                // JSON no representa NaN/Inf: se emiten como null.
                sb.Append("null");
                return;
            }
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
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
                        else sb.Append(c);   // UTF-8 al codificar: los acentos van crudos (JSON válido)
                        break;
                }
            }
            sb.Append('"');
        }

        /* ----------------------------------------------------------- helpers -- */

        public static string GetString(Dictionary<string, object> o, string key, string def)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return def;
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static long GetInt(Dictionary<string, object> o, string key, long def)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return def;
            if (v is long) return (long)v;
            if (v is int) return (int)v;
            if (v is double) return (long)(double)v;
            if (v is bool) return (bool)v ? 1 : 0;
            long l;
            if (long.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out l)) return l;
            return def;
        }

        public static double GetDouble(Dictionary<string, object> o, string key, double def)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return def;
            if (v is double) return (double)v;
            if (v is long) return (long)v;
            double d;
            if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out d)) return d;
            return def;
        }

        public static bool GetBool(Dictionary<string, object> o, string key, bool def)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return def;
            if (v is bool) return (bool)v;
            if (v is long) return ((long)v) != 0;
            if (v is double) return ((double)v) != 0.0;
            return def;
        }

        /// <summary>Devuelve la lista de la clave o una lista vacía (nunca null).</summary>
        public static List<object> GetArray(Dictionary<string, object> o, string key)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return new List<object>();
            List<object> l = v as List<object>;
            if (l != null) return l;
            object[] arr = v as object[];
            if (arr != null) return new List<object>(arr);
            return new List<object>();
        }

        /// <summary>Devuelve el objeto de la clave o null.</summary>
        public static Dictionary<string, object> GetObject(Dictionary<string, object> o, string key)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return null;
            return v as Dictionary<string, object>;
        }

        /* ---------------------------------------------- carga UTF-8 con BOM -- */

        /// <summary>Decodifica bytes UTF-8 tolerando BOM (y delegando a Encoding el resto).</summary>
        public static string Utf8BytesToString(byte[] data)
        {
            if (data == null || data.Length == 0) return string.Empty;
            Encoding utf8 = new UTF8Encoding(false);
            string s = utf8.GetString(data);
            if (s.Length > 0 && s[0] == '\uFEFF') s = s.Substring(1);
            return s;
        }
    }
}
