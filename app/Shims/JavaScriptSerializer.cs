using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

// The shared code (../src) was written for .NET Framework, whose System.Web.Extensions has JavaScriptSerializer.
// .NET 8 has no such class; this one gives the same results for what the shared code does with it:
// objects become Dictionary<string, object>, arrays object[], whole numbers int (long when larger), other numbers
// decimal, and Serialize writes dictionaries, lists, arrays and plain values.
namespace System.Web.Script.Serialization
{
    public class JavaScriptSerializer
    {
        public int MaxJsonLength { get; set; }

        static readonly JsonDocumentOptions Options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 256 };

        public object DeserializeObject(string input)
        {
            if (input == null) throw new ArgumentNullException("input");
            input = input.Trim();
            if (input.Length > 0 && input[0] == '﻿') input = input.Substring(1);
            using (var doc = JsonDocument.Parse(input, Options)) return Convert(doc.RootElement);
        }

        public T Deserialize<T>(string input)
        {
            object o = DeserializeObject(input);
            if (o == null) return default(T);
            if (o is T) return (T)o;
            throw new InvalidOperationException("JSON of type " + o.GetType().Name + " cannot become " + typeof(T).Name + ".");
        }

        static object Convert(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    var d = new Dictionary<string, object>();
                    foreach (var p in e.EnumerateObject()) d[p.Name] = Convert(p.Value);
                    return d;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var x in e.EnumerateArray()) list.Add(Convert(x));
                    return list.ToArray();
                case JsonValueKind.String: return e.GetString();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Number:
                    int i; long l; decimal m;
                    if (e.TryGetInt32(out i)) return i;
                    if (e.TryGetInt64(out l)) return l;
                    if (e.TryGetDecimal(out m)) return m;
                    return e.GetDouble();
                default: return null;
            }
        }

        public string Serialize(object obj)
        {
            var sb = new StringBuilder();
            Write(sb, obj, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object o, int depth)
        {
            if (depth > 200) throw new InvalidOperationException("The object is nested too deeply.");
            if (o == null || o is DBNull) { sb.Append("null"); return; }
            if (o is string) { WriteString(sb, (string)o); return; }
            if (o is char) { WriteString(sb, o.ToString()); return; }
            if (o is bool) { sb.Append((bool)o ? "true" : "false"); return; }
            if (o is Enum) { sb.Append(System.Convert.ToInt64(o, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); return; }
            if (o is double || o is float)
            {
                double v = System.Convert.ToDouble(o, CultureInfo.InvariantCulture);
                sb.Append(double.IsNaN(v) || double.IsInfinity(v) ? "null" : v.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (o is decimal) { sb.Append(((decimal)o).ToString(CultureInfo.InvariantCulture)); return; }
            if (o is sbyte || o is byte || o is short || o is ushort || o is int || o is uint || o is long || o is ulong)
            { sb.Append(System.Convert.ToString(o, CultureInfo.InvariantCulture)); return; }
            if (o is DateTime) { WriteString(sb, ((DateTime)o).ToString("o", CultureInfo.InvariantCulture)); return; }
            var dict = o as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, System.Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    Write(sb, kv.Value, depth + 1);
                }
                sb.Append('}');
                return;
            }
            var seq = o as IEnumerable;
            if (seq != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var x in seq)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, x, depth + 1);
                }
                sb.Append(']');
                return;
            }
            // Any other object: its public properties and fields, as JavaScriptSerializer does.
            sb.Append('{');
            bool f = true;
            foreach (var p in o.GetType().GetProperties())
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                if (!f) sb.Append(',');
                f = false;
                WriteString(sb, p.Name); sb.Append(':'); Write(sb, p.GetValue(o, null), depth + 1);
            }
            foreach (var fi in o.GetType().GetFields())
            {
                if (fi.IsStatic) continue;
                if (!f) sb.Append(',');
                f = false;
                WriteString(sb, fi.Name); sb.Append(':'); Write(sb, fi.GetValue(o), depth + 1);
            }
            sb.Append('}');
        }

        static void WriteString(StringBuilder sb, string s)
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
                        // The same characters JavaScriptSerializer escapes, so files written here look the same.
                        if (c < 0x20 || c == '<' || c == '>' || c == '&' || c == '\'') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
