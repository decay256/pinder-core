using System;
using System.Collections.Generic;
using System.Text;

namespace Pinder.Core.Data
{
    // ---------------------------------------------------------------------------
    // Minimal recursive-descent JSON parser.
    // Handles: objects, arrays, strings, numbers, booleans, null.
    // No NuGet dependencies. Sufficient for parsing the project data files.
    // ---------------------------------------------------------------------------

    internal abstract class JsonValue { }

    internal sealed class JsonString : JsonValue
    {
        public string Value { get; }
        public JsonString(string value) => Value = value;
    }

    internal sealed class JsonNumber : JsonValue
    {
        public double Value { get; }
        public JsonNumber(double value) => Value = value;
        public int    ToInt()   => (int)Value;
        public float  ToFloat() => (float)Value;
    }

    internal sealed class JsonBool : JsonValue
    {
        public bool Value { get; }
        public JsonBool(bool value) => Value = value;
    }

    internal sealed class JsonNull : JsonValue
    {
        public static readonly JsonNull Instance = new JsonNull();
    }

    internal sealed class JsonArray : JsonValue
    {
        public List<JsonValue> Items { get; } = new List<JsonValue>();
    }

    internal sealed class JsonObject : JsonValue
    {
        public Dictionary<string, JsonValue> Properties { get; } =
            new Dictionary<string, JsonValue>(StringComparer.Ordinal);

        public string GetString(string key, string defaultValue = "")
        {
            if (Properties.TryGetValue(key, out var v) && v is JsonString s)
                return s.Value;
            return defaultValue;
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            if (Properties.TryGetValue(key, out var v) && v is JsonNumber n)
                return n.ToInt();
            return defaultValue;
        }

        public float GetFloat(string key, float defaultValue = 0f)
        {
            if (Properties.TryGetValue(key, out var v) && v is JsonNumber n)
                return n.ToFloat();
            return defaultValue;
        }

        public JsonObject? GetObject(string key)
        {
            if (Properties.TryGetValue(key, out var v) && v is JsonObject o)
                return o;
            return null;
        }

        public JsonArray? GetArray(string key)
        {
            if (Properties.TryGetValue(key, out var v) && v is JsonArray a)
                return a;
            return null;
        }

        public bool HasKey(string key) => Properties.ContainsKey(key);
    }

    internal static class JsonParser
    {
        public static JsonValue Parse(string json)
        {
            int pos = 0;
            var result = ParseValue(json, ref pos);
            return result;
        }

        private static void SkipWhitespace(string s, ref int pos)
        {
            while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' ||
                                       s[pos] == '\r' || s[pos] == '\n'))
                pos++;
        }

        private static JsonValue ParseValue(string s, ref int pos)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length)
                throw new FormatException("Unexpected end of JSON.");

            char c = s[pos];
            if (c == '{') return ParseObject(s, ref pos);
            if (c == '[') return ParseArray(s, ref pos);
            if (c == '"') return new JsonString(ParseString(s, ref pos));
            if (c == 't') { pos += 4; return new JsonBool(true);  }
            if (c == 'f') { pos += 5; return new JsonBool(false); }
            if (c == 'n') { pos += 4; return JsonNull.Instance;   }
            if (c == '-' || char.IsDigit(c)) return ParseNumber(s, ref pos);

            throw new FormatException($"Unexpected character '{c}' at position {pos}.");
        }

        private static JsonObject ParseObject(string s, ref int pos)
        {
            var obj = new JsonObject();
            pos++; // consume '{'
            SkipWhitespace(s, ref pos);

            if (pos < s.Length && s[pos] == '}') { pos++; return obj; }

            while (pos < s.Length)
            {
                SkipWhitespace(s, ref pos);
                string key = ParseString(s, ref pos);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != ':')
                    throw new FormatException($"Expected ':' at position {pos}.");
                pos++; // consume ':'
                var val = ParseValue(s, ref pos);
                obj.Properties[key] = val;
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length) break;
                if (s[pos] == '}') { pos++; return obj; }
                if (s[pos] == ',') { pos++; continue; }
                throw new FormatException($"Expected ',' or '}}' at position {pos}.");
            }
            return obj;
        }

        private static JsonArray ParseArray(string s, ref int pos)
        {
            var arr = new JsonArray();
            pos++; // consume '['
            SkipWhitespace(s, ref pos);

            if (pos < s.Length && s[pos] == ']') { pos++; return arr; }

            while (pos < s.Length)
            {
                var val = ParseValue(s, ref pos);
                arr.Items.Add(val);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length) break;
                if (s[pos] == ']') { pos++; return arr; }
                if (s[pos] == ',') { pos++; continue; }
                throw new FormatException($"Expected ',' or ']' at position {pos}.");
            }
            return arr;
        }

        private static string ParseString(string s, ref int pos)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length || s[pos] != '"')
                throw new FormatException($"Expected '\"' at position {pos}.");
            pos++; // consume opening '"'

            var sb = new StringBuilder();
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == '"') { pos++; return sb.ToString(); }
                if (c == '\\')
                {
                    pos++;
                    if (pos >= s.Length) throw new FormatException("Unexpected end after '\\'.");
                    char esc = s[pos];
                    pos++;
                    switch (esc)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/');  break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'b':  sb.Append('\b'); break;
                        case 'f':  sb.Append('\f'); break;
                        case 'u':
                            if (pos + 4 > s.Length)
                                throw new FormatException("Incomplete \\u escape.");
                            string hex = s.Substring(pos, 4);
                            pos += 4;
                            int code = Convert.ToInt32(hex, 16);
                            sb.Append((char)code);
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                    pos++;
                }
            }
            throw new FormatException("Unterminated string.");
        }

        private static JsonNumber ParseNumber(string s, ref int pos)
        {
            int start = pos;
            if (s[pos] == '-') pos++;
            while (pos < s.Length && char.IsDigit(s[pos])) pos++;
            if (pos < s.Length && s[pos] == '.')
            {
                pos++;
                while (pos < s.Length && char.IsDigit(s[pos])) pos++;
            }
            if (pos < s.Length && (s[pos] == 'e' || s[pos] == 'E'))
            {
                pos++;
                if (pos < s.Length && (s[pos] == '+' || s[pos] == '-')) pos++;
                while (pos < s.Length && char.IsDigit(s[pos])) pos++;
            }
            string numStr = s.Substring(start, pos - start);
            return new JsonNumber(double.Parse(numStr,
                System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
