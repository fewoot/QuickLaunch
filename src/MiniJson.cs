using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace QuickLaunch
{
    /// <summary>
    /// 极简 JSON 读写器。故意不依赖 Newtonsoft/DataContractJsonSerializer，
    /// 这样编译出来是零依赖单文件，配置格式也可读可手改。
    /// </summary>
    internal static class Json
    {
        // ---------------------------------------------------------------- 解析

        public static object Parse(string text)
        {
            if (text == null) return null;
            int i = 0;
            SkipWs(text, ref i);
            if (i >= text.Length) return null;
            object v = ParseValue(text, ref i);
            return v;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                break;
            }
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (s.Substring(i).StartsWith("true", StringComparison.Ordinal)) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false", StringComparison.Ordinal)) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null", StringComparison.Ordinal)) { i += 4; return null; }
            return ParseNumber(s, ref i);
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            i++; // {
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == '}') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                object val = ParseValue(s, ref i);
                if (key != null) d[key] = val;
            }
            return d;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == ']') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                list.Add(ParseValue(s, ref i));
            }
            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '"') return null;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
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
                        if (i + 4 <= s.Length)
                        {
                            int code;
                            if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                else break;
            }
            string t = s.Substring(start, i - start);
            double d;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            long l;
            if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return (double)l;
            return null;
        }

        // ------------------------------------------------------- 取值助手

        public static Dictionary<string, object> AsMap(object o)
        {
            return o as Dictionary<string, object>;
        }

        public static List<object> AsList(object o)
        {
            return o as List<object>;
        }

        public static string Str(Dictionary<string, object> m, string key, string def)
        {
            object v;
            if (m != null && m.TryGetValue(key, out v) && v != null)
            {
                string s = v as string;
                if (s != null) return s;
                return Convert.ToString(v, CultureInfo.InvariantCulture);
            }
            return def;
        }

        public static int Int(Dictionary<string, object> m, string key, int def)
        {
            object v;
            if (m != null && m.TryGetValue(key, out v) && v != null)
            {
                if (v is double) return (int)Math.Round((double)v);
                if (v is bool) return ((bool)v) ? 1 : 0;
                int n;
                if (int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out n)) return n;
            }
            return def;
        }

        public static bool Bool(Dictionary<string, object> m, string key, bool def)
        {
            object v;
            if (m != null && m.TryGetValue(key, out v) && v != null)
            {
                if (v is bool) return (bool)v;
                if (v is double) return Math.Abs((double)v) > 0.0001;
                string s = v as string;
                if (s != null)
                    return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
            }
            return def;
        }

        // ---------------------------------------------------------------- 生成

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object v, int indent)
        {
            if (v == null) { sb.Append("null"); return; }

            string s = v as string;
            if (s != null) { WriteString(sb, s); return; }

            if (v is bool) { sb.Append(((bool)v) ? "true" : "false"); return; }

            if (v is int || v is long) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            if (v is double || v is float || v is decimal)
            {
                sb.Append(Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture));
                return;
            }

            var map = v as IDictionary<string, object>;
            if (map != null) { WriteObject(sb, map, indent); return; }

            var list = v as System.Collections.IEnumerable;
            if (list != null) { WriteArray(sb, list, indent); return; }

            WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        private static void WriteObject(StringBuilder sb, IDictionary<string, object> map, int indent)
        {
            if (map.Count == 0) { sb.Append("{}"); return; }
            sb.Append("{");
            bool first = true;
            foreach (KeyValuePair<string, object> kv in map)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append("\r\n").Append(Pad(indent + 1));
                WriteString(sb, kv.Key);
                sb.Append(": ");
                WriteValue(sb, kv.Value, indent + 1);
            }
            sb.Append("\r\n").Append(Pad(indent)).Append("}");
        }

        private static void WriteArray(StringBuilder sb, System.Collections.IEnumerable list, int indent)
        {
            var items = new List<object>();
            foreach (object o in list) items.Add(o);
            if (items.Count == 0) { sb.Append("[]"); return; }
            // 简单标量数组压成一行，读起来清爽
            bool simple = true;
            foreach (object o in items)
                if (o is IDictionary<string, object> || o is System.Collections.IEnumerable && !(o is string)) { simple = false; break; }

            if (simple)
            {
                sb.Append("[");
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    WriteValue(sb, items[i], indent);
                }
                sb.Append("]");
                return;
            }

            sb.Append("[");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append("\r\n").Append(Pad(indent + 1));
                WriteValue(sb, items[i], indent + 1);
            }
            sb.Append("\r\n").Append(Pad(indent)).Append("]");
        }

        private static string Pad(int n) { return new string(' ', n * 2); }

        private static void WriteString(StringBuilder sb, string s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
