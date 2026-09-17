using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Json.cs — minimal hand-written JSON reader/writer.
// Ported from Stargazer's Json.cs. Benchmarked against JavaScriptSerializer
// (04/08/2026): 18x faster cold start (no System.Web assembly load), 2-5x
// faster parsing. Reads: object -> Dictionary<string, object>, array ->
// List<object>, string, number (double), true/false/null. Writes the exact
// mirror, compatible with any standard JSON reader.

namespace Marabook
{
    /// <summary>Raised on malformed JSON — including nesting beyond
    /// Json.MaxDepth, which would otherwise be a StackOverflowException:
    /// uncatchable in .NET, the process dies without a message or a save.</summary>
    public class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }

    public static class Json
    {
        /// <summary>Maximum nesting depth accepted, reading AND writing. Real
        /// .plot files stay under ~20; only corrupt or hostile files go deeper.</summary>
        public const int MaxDepth = 256;

        // ------------------------------------------------------- reading

        public static object Parse(string text)
        {
            var pos = 0;
            var v = ParseValue(text, ref pos, 0);
            SkipWhitespace(text, ref pos);
            if (pos < text.Length)
                throw new JsonException("JSON: trailing characters at position " + pos);
            return v;
        }

        // Convenience accessors: null (or fallback) when the type does not match.
        public static Dictionary<string, object> AsObject(object v)
        {
            return v as Dictionary<string, object>;
        }

        public static List<object> AsList(object v)
        {
            return v as List<object>;
        }

        /// <summary>Une liste JSON de chaînes → des chaînes non vides, sans
        /// doublon, dans l'ordre ; vide si absente ou d'un autre type.</summary>
        public static List<string> AsStringList(object v)
        {
            var result = new List<string>();
            var list = AsList(v);
            if (list == null) return result;
            foreach (var item in list)
            {
                var text = AsString(item);
                if (!string.IsNullOrEmpty(text) && !result.Contains(text)) result.Add(text);
            }
            return result;
        }

        public static string AsString(object v)
        {
            return v as string;
        }

        public static bool AsBool(object v, bool fallback)
        {
            return v is bool ? (bool)v : fallback;
        }

        public static int AsInt(object v, int fallback)
        {
            return v is double ? (int)(double)v : fallback;
        }

        public static double AsDouble(object v, double fallback)
        {
            return v is double ? (double)v : fallback;
        }

        public static object Field(object obj, string name)
        {
            var o = AsObject(obj);
            object v;
            if (o != null && o.TryGetValue(name, out v)) return v;
            return null;
        }

        private static void SkipWhitespace(string s, ref int pos)
        {
            while (pos < s.Length &&
                   (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\r' || s[pos] == '\n'))
                pos++;
        }

        private static object ParseValue(string s, ref int pos, int depth)
        {
            if (depth > MaxDepth)
                throw new JsonException("JSON: nesting deeper than " + MaxDepth
                    + " levels at position " + pos + " — corrupt file?");
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length) throw new JsonException("JSON: unexpected end of input");
            var c = s[pos];
            if (c == '{') return ParseObject(s, ref pos, depth);
            if (c == '[') return ParseList(s, ref pos, depth);
            if (c == '"') return ParseString(s, ref pos);
            if (c == 't') { Expect(s, ref pos, "true"); return true; }
            if (c == 'f') { Expect(s, ref pos, "false"); return false; }
            if (c == 'n') { Expect(s, ref pos, "null"); return null; }
            return ParseNumber(s, ref pos);
        }

        private static void Expect(string s, ref int pos, string word)
        {
            if (pos + word.Length > s.Length || s.Substring(pos, word.Length) != word)
                throw new Exception("JSON: expected \"" + word + "\" at position " + pos);
            pos += word.Length;
        }

        private static Dictionary<string, object> ParseObject(string s, ref int pos, int depth)
        {
            var result = new Dictionary<string, object>();
            pos++;   // '{'
            SkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == '}') { pos++; return result; }
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != '"')
                    throw new Exception("JSON: field name expected at position " + pos);
                var name = ParseString(s, ref pos);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != ':')
                    throw new Exception("JSON: \":\" expected at position " + pos);
                pos++;
                result[name] = ParseValue(s, ref pos, depth + 1);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length)
                    throw new Exception("JSON: unterminated object");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == '}') { pos++; return result; }
                throw new Exception("JSON: \",\" or \"}\" expected at position " + pos);
            }
        }

        private static List<object> ParseList(string s, ref int pos, int depth)
        {
            var result = new List<object>();
            pos++;   // '['
            SkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == ']') { pos++; return result; }
            while (true)
            {
                result.Add(ParseValue(s, ref pos, depth + 1));
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length)
                    throw new Exception("JSON: unterminated array");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == ']') { pos++; return result; }
                throw new Exception("JSON: \",\" or \"]\" expected at position " + pos);
            }
        }

        private static string ParseString(string s, ref int pos)
        {
            var sb = new StringBuilder();
            pos++;   // '"'
            while (true)
            {
                if (pos >= s.Length) throw new Exception("JSON: unterminated string");
                var c = s[pos++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (pos >= s.Length) throw new Exception("JSON: unterminated escape");
                var e = s[pos++];
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
                        if (pos + 4 > s.Length)
                            throw new Exception("JSON: truncated \\u escape");
                        sb.Append((char)ushort.Parse(s.Substring(pos, 4),
                            NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        pos += 4;
                        break;
                    default:
                        throw new Exception("JSON: unknown escape \\" + e);
                }
            }
        }

        private static object ParseNumber(string s, ref int pos)
        {
            var start = pos;
            while (pos < s.Length &&
                   (char.IsDigit(s[pos]) || s[pos] == '-' || s[pos] == '+' ||
                    s[pos] == '.' || s[pos] == 'e' || s[pos] == 'E'))
                pos++;
            if (pos == start) throw new Exception("JSON: unreadable value at position " + start);
            return double.Parse(s.Substring(start, pos - start), CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------- writing

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object v, int depth)
        {
            if (depth > MaxDepth)
                throw new JsonException("JSON: value nested deeper than " + MaxDepth
                    + " levels — cyclic or pathological structure?");
            if (v == null) { sb.Append("null"); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is int) { sb.Append(((int)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is long) { sb.Append(((long)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is double)
            {
                sb.Append(((double)v).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var dict = v as Dictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                var first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    WriteValue(sb, kv.Value, depth + 1);
                }
                sb.Append('}');
                return;
            }
            var list = v as System.Collections.IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                var first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item, depth + 1);
                }
                sb.Append(']');
                return;
            }
            throw new Exception("JSON: unsupported type for writing: " + v.GetType().Name);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
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
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
