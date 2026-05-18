using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WaterDrivenInfrastructure.Util
{
    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            int index = 0;
            return Value(json, ref index);
        }

        private static object Value(string text, ref int index)
        {
            Whitespace(text, ref index);
            if (index >= text.Length)
                return null;

            switch (text[index])
            {
                case '{': return Object(text, ref index);
                case '[': return Array(text, ref index);
                case '"': return String(text, ref index);
                case 't':
                case 'f': return Bool(text, ref index);
                case 'n': index += 4; return null;
                default: return Number(text, ref index);
            }
        }

        private static Dictionary<string, object> Object(string text, ref int index)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            index++;
            Whitespace(text, ref index);
            if (index < text.Length && text[index] == '}')
            {
                index++;
                return result;
            }

            while (index < text.Length)
            {
                Whitespace(text, ref index);
                var key = String(text, ref index);
                Whitespace(text, ref index);
                if (index < text.Length && text[index] == ':')
                    index++;
                result[key] = Value(text, ref index);
                Whitespace(text, ref index);
                if (index < text.Length && text[index] == ',')
                {
                    index++;
                    continue;
                }
                if (index < text.Length && text[index] == '}')
                {
                    index++;
                    return result;
                }
            }

            return result;
        }

        private static List<object> Array(string text, ref int index)
        {
            var result = new List<object>();
            index++;
            Whitespace(text, ref index);
            if (index < text.Length && text[index] == ']')
            {
                index++;
                return result;
            }

            while (index < text.Length)
            {
                result.Add(Value(text, ref index));
                Whitespace(text, ref index);
                if (index < text.Length && text[index] == ',')
                {
                    index++;
                    continue;
                }
                if (index < text.Length && text[index] == ']')
                {
                    index++;
                    return result;
                }
            }

            return result;
        }

        private static string String(string text, ref int index)
        {
            index++;
            var builder = new StringBuilder();
            while (index < text.Length)
            {
                char current = text[index];
                if (current == '\\')
                {
                    index++;
                    if (index >= text.Length)
                        return builder.ToString();

                    char escaped = text[index];
                    switch (escaped)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            builder.Append((char)Convert.ToInt32(text.Substring(index + 1, 4), 16));
                            index += 4;
                            break;
                        default:
                            builder.Append(escaped);
                            break;
                    }
                }
                else if (current == '"')
                {
                    index++;
                    return builder.ToString();
                }
                else
                {
                    builder.Append(current);
                }

                index++;
            }

            return builder.ToString();
        }

        private static object Number(string text, ref int index)
        {
            int start = index;
            if (text[index] == '-')
                index++;
            while (index < text.Length && char.IsDigit(text[index]))
                index++;

            bool isDouble = false;
            if (index < text.Length && text[index] == '.')
            {
                isDouble = true;
                index++;
                while (index < text.Length && char.IsDigit(text[index]))
                    index++;
            }

            if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
            {
                isDouble = true;
                index++;
                if (index < text.Length && (text[index] == '+' || text[index] == '-'))
                    index++;
                while (index < text.Length && char.IsDigit(text[index]))
                    index++;
            }

            var raw = text.Substring(start, index - start);
            if (isDouble)
                return double.Parse(raw, CultureInfo.InvariantCulture);
            return long.Parse(raw, CultureInfo.InvariantCulture);
        }

        private static bool Bool(string text, ref int index)
        {
            if (text[index] == 't')
            {
                index += 4;
                return true;
            }

            index += 5;
            return false;
        }

        private static void Whitespace(string text, ref int index)
        {
            while (index < text.Length && (text[index] <= ' ' || text[index] == '\ufeff'))
                index++;
        }
    }
}