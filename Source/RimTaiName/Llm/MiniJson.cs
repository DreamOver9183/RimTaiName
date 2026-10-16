using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RimTaiName.Llm
{
    /// <summary>
    /// Minimal JSON reader/writer for the Gemini requests and responses (the game ships no JSON library).
    /// Objects parse to <see cref="Dictionary{TKey,TValue}"/> with ordinal keys, arrays to <see cref="List{T}"/>,
    /// integers to <see cref="long"/>, other numbers to <see cref="double"/>.
    /// </summary>
    internal static class MiniJson
    {
        private const int MaxDepth = 64;

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            return sb.ToString();
        }

        /// <exception cref="FormatException">The text is not valid JSON.</exception>
        public static object Parse(string json)
        {
            if (json == null)
                throw new FormatException("JSON text is null");
            var reader = new Reader(json);
            object value = reader.ReadValue(0);
            reader.SkipWhitespace();
            if (!reader.AtEnd)
                throw reader.Error("unexpected trailing characters");
            return value;
        }

        public static Dictionary<string, object> AsObject(object value) => value as Dictionary<string, object>;

        public static List<object> AsArray(object value) => value as List<object>;

        /// <summary>The string value of <paramref name="name"/>, or null when missing or not a string.</summary>
        public static string GetString(Dictionary<string, object> obj, string name) =>
            Get(obj, name) as string;

        public static object Get(Dictionary<string, object> obj, string name) =>
            obj != null && name != null && obj.TryGetValue(name, out object value) ? value : null;

        private static void Write(StringBuilder sb, object value, int depth)
        {
            if (depth > MaxDepth)
                throw new ArgumentException("JSON value nested too deeply");
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d))
                        throw new ArgumentException("JSON cannot represent NaN or infinity");
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case IDictionary<string, object> obj:
                    sb.Append('{');
                    bool firstMember = true;
                    foreach (var pair in obj)
                    {
                        if (!firstMember)
                            sb.Append(',');
                        firstMember = false;
                        WriteString(sb, pair.Key);
                        sb.Append(':');
                        Write(sb, pair.Value, depth + 1);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (object item in list)
                    {
                        if (!firstItem)
                            sb.Append(',');
                        firstItem = false;
                        Write(sb, item, depth + 1);
                    }
                    sb.Append(']');
                    break;
                default:
                    throw new ArgumentException("unsupported JSON value type " + value.GetType().Name);
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
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Reader
        {
            private readonly string text;

            private int pos;

            public Reader(string text)
            {
                this.text = text;
            }

            public bool AtEnd => pos >= text.Length;

            public FormatException Error(string message) =>
                new FormatException($"Invalid JSON at position {pos}: {message}");

            public void SkipWhitespace()
            {
                while (pos < text.Length && (text[pos] == ' ' || text[pos] == '\t' || text[pos] == '\n' || text[pos] == '\r'))
                    pos++;
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth)
                    throw Error("nested too deeply");
                SkipWhitespace();
                if (AtEnd)
                    throw Error("unexpected end of input");
                char c = text[pos];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': return ReadLiteral("true", true);
                    case 'f': return ReadLiteral("false", false);
                    case 'n': return ReadLiteral("null", null);
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return ReadNumber();
                        throw Error($"unexpected character '{c}'");
                }
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var obj = new Dictionary<string, object>(StringComparer.Ordinal);
                pos++; // '{'
                SkipWhitespace();
                if (!AtEnd && text[pos] == '}')
                {
                    pos++;
                    return obj;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || text[pos] != '"')
                        throw Error("expected a member name");
                    string name = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    obj[name] = ReadValue(depth + 1);
                    SkipWhitespace();
                    if (AtEnd)
                        throw Error("unterminated object");
                    if (text[pos] == ',')
                    {
                        pos++;
                        continue;
                    }
                    Expect('}');
                    return obj;
                }
            }

            private List<object> ReadArray(int depth)
            {
                var list = new List<object>();
                pos++; // '['
                SkipWhitespace();
                if (!AtEnd && text[pos] == ']')
                {
                    pos++;
                    return list;
                }
                while (true)
                {
                    list.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd)
                        throw Error("unterminated array");
                    if (text[pos] == ',')
                    {
                        pos++;
                        continue;
                    }
                    Expect(']');
                    return list;
                }
            }

            private string ReadString()
            {
                pos++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                        throw Error("unterminated string");
                    char c = text[pos++];
                    if (c == '"')
                        return sb.ToString();
                    if (c < 0x20)
                        throw Error("control character in string");
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd)
                        throw Error("unterminated escape");
                    char e = text[pos++];
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
                            if (pos + 4 > text.Length ||
                                !int.TryParse(text.Substring(pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                                throw Error("invalid \\u escape");
                            sb.Append((char)code);
                            pos += 4;
                            break;
                        default:
                            throw Error($"invalid escape '\\{e}'");
                    }
                }
            }

            private object ReadNumber()
            {
                int start = pos;
                if (text[pos] == '-')
                    pos++;
                bool isInteger = true;
                while (pos < text.Length)
                {
                    char c = text[pos];
                    if (c >= '0' && c <= '9')
                    {
                        pos++;
                    }
                    else if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        isInteger = false;
                        pos++;
                    }
                    else
                    {
                        break;
                    }
                }
                string token = text.Substring(start, pos - start);
                if (isInteger && long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l))
                    return l;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    return d;
                throw Error($"invalid number '{token}'");
            }

            private object ReadLiteral(string literal, object value)
            {
                // Check the length first: Mono's CompareOrdinal may throw instead of comparing a truncated tail.
                if (pos + literal.Length > text.Length || string.CompareOrdinal(text, pos, literal, 0, literal.Length) != 0)
                    throw Error("invalid literal");
                pos += literal.Length;
                return value;
            }

            private void Expect(char c)
            {
                if (AtEnd || text[pos] != c)
                    throw Error($"expected '{c}'");
                pos++;
            }
        }
    }
}
