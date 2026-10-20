// Minimal JSON reader/writer (no external libraries). Objects keep the key order, like Python
// dictionaries, so files written by the Python tools and by this program look the same.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace InterfaceTools
{
    /// <summary>Ordered JSON object.</summary>
    public class JObj : IEnumerable<KeyValuePair<string, object>>
    {
        readonly List<string> keys = new List<string>();
        readonly Dictionary<string, object> map = new Dictionary<string, object>();

        public object this[string k]
        {
            get => map.TryGetValue(k, out var v) ? v : null;
            set { if (!map.ContainsKey(k)) keys.Add(k); map[k] = value; }
        }
        public bool Has(string k) => map.ContainsKey(k);
        public IEnumerable<string> Keys => keys;
        public int Count => keys.Count;
        public void Remove(string k) { if (map.Remove(k)) keys.Remove(k); }
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() { foreach (var k in keys) yield return new KeyValuePair<string, object>(k, map[k]); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public JObj Obj(string k) => this[k] as JObj;
        public List<object> Arr(string k) => this[k] as List<object>;
        public string Str(string k) => this[k] as string;
        public long Long(string k, long def = 0) => this[k] == null ? def : Json.ToLong(this[k]);
        public JObj Copy()
        {
            var o = new JObj();
            foreach (var kv in this) o[kv.Key] = kv.Value;
            return o;
        }
    }

    public class JsonError : Exception
    {
        public int Line;
        public JsonError(string m, int line) : base(m) { Line = line; }
    }

    public static class Json
    {
        public static long ToLong(object v)
        {
            switch (v)
            {
                case long l: return l;
                case double d: return (long)d;
                case bool b: return b ? 1 : 0;
                case string s: return long.Parse(s, CultureInfo.InvariantCulture);
            }
            throw new InvalidCastException("not a number: " + v);
        }

        public static object Parse(string text)
        {
            int i = 0;
            object v = Value(text, ref i);
            Ws(text, ref i);
            if (i != text.Length) throw Err(text, i, "Extra data");
            return v;
        }

        static JsonError Err(string t, int i, string msg)
        {
            int line = 1;
            for (int k = 0; k < Math.Min(i, t.Length); k++) if (t[k] == '\n') line++;
            return new JsonError(msg, line);
        }

        static void Ws(string t, ref int i) { while (i < t.Length && (t[i] == ' ' || t[i] == '\t' || t[i] == '\r' || t[i] == '\n')) i++; }

        static object Value(string t, ref int i)
        {
            Ws(t, ref i);
            if (i >= t.Length) throw Err(t, i, "Expecting value");
            char c = t[i];
            if (c == '{')
            {
                var o = new JObj();
                i++; Ws(t, ref i);
                if (i < t.Length && t[i] == '}') { i++; return o; }
                while (true)
                {
                    Ws(t, ref i);
                    if (i >= t.Length || t[i] != '"') throw Err(t, i, "Expecting property name enclosed in double quotes");
                    string k = Str(t, ref i);
                    Ws(t, ref i);
                    if (i >= t.Length || t[i] != ':') throw Err(t, i, "Expecting ':' delimiter");
                    i++;
                    o[k] = Value(t, ref i);
                    Ws(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    if (i < t.Length && t[i] == '}') { i++; return o; }
                    throw Err(t, i, "Expecting ',' delimiter");
                }
            }
            if (c == '[')
            {
                var a = new List<object>();
                i++; Ws(t, ref i);
                if (i < t.Length && t[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(t, ref i));
                    Ws(t, ref i);
                    if (i < t.Length && t[i] == ',') { i++; continue; }
                    if (i < t.Length && t[i] == ']') { i++; return a; }
                    throw Err(t, i, "Expecting ',' delimiter");
                }
            }
            if (c == '"') return Str(t, ref i);
            if (string.CompareOrdinal(t, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(t, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(t, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            if (t[i] == '-') i++;
            while (i < t.Length && "0123456789+-.eE".IndexOf(t[i]) >= 0) i++;
            string num = t.Substring(st, i - st);
            if (num.Length == 0 || num == "-") throw Err(t, st, "Expecting value");
            if (num.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && long.TryParse(num, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l)) return l;
            try { return Text.ParseDouble(num); } catch (FormatException) { throw Err(t, st, "Expecting value"); }
        }

        static string Str(string t, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (true)
            {
                if (i >= t.Length) throw Err(t, i, "Unterminated string");
                char c = t[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (i >= t.Length) throw Err(t, i, "Unterminated string");
                    char e = t[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 > t.Length) throw Err(t, i, "Invalid \\uXXXX escape");
                            sb.Append((char)Convert.ToInt32(t.Substring(i, 4), 16)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
        }

        /// <summary>Like Python json.dumps(obj, indent=indent, ensure_ascii=False).</summary>
        public static string Dump(object v, int indent = 2)
        {
            var sb = new StringBuilder();
            Write(sb, v, indent, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v, int indent, int level)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case string s: WriteStr(sb, s); return;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case uint u: sb.Append(u.ToString(CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(Text.PyReprFloat(d)); return;
                case float f: sb.Append(Text.PyReprFloat(f)); return;
                case JObj o:
                    if (o.Count == 0) { sb.Append("{}"); return; }
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in o)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append('\n').Append(' ', indent * (level + 1));
                        WriteStr(sb, kv.Key);
                        sb.Append(": ");
                        Write(sb, kv.Value, indent, level + 1);
                    }
                    sb.Append('\n').Append(' ', indent * level).Append('}');
                    return;
                case System.Collections.IEnumerable e:
                    var items = e.Cast<object>().ToList();
                    if (items.Count == 0) { sb.Append("[]"); return; }
                    sb.Append('[');
                    for (int k = 0; k < items.Count; k++)
                    {
                        if (k > 0) sb.Append(',');
                        sb.Append('\n').Append(' ', indent * (level + 1));
                        Write(sb, items[k], indent, level + 1);
                    }
                    sb.Append('\n').Append(' ', indent * level).Append(']');
                    return;
            }
            throw new ArgumentException("cannot serialize " + v.GetType());
        }

        static void WriteStr(StringBuilder sb, string s)
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
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
