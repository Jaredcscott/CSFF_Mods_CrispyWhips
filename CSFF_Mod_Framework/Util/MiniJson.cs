namespace CSFFModFramework.Util;

internal static class MiniJson
{
    public static object Parse(string json)
    {
        int idx = 0;
        return Value(json, ref idx);
    }

    private static object Value(string s, ref int i)
    {
        Ws(s, ref i);
        if (i >= s.Length) return null;
        switch (s[i])
        {
            case '{': return Obj(s, ref i);
            case '[': return Arr(s, ref i);
            case '"': return Str(s, ref i);
            case 't': case 'f': return Bool(s, ref i);
            case 'n': i += 4; return null;
            default: return Num(s, ref i);
        }
    }

    private static Dictionary<string, object> Obj(string s, ref int i)
    {
        var d = new Dictionary<string, object>(StringComparer.Ordinal);
        i++;
        Ws(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return d; }
        while (i < s.Length)
        {
            Ws(s, ref i);
            var key = Str(s, ref i);
            Ws(s, ref i);
            i++;
            d[key] = Value(s, ref i);
            Ws(s, ref i);
            if (s[i] == ',') { i++; continue; }
            if (s[i] == '}') { i++; return d; }
        }
        return d;
    }

    private static List<object> Arr(string s, ref int i)
    {
        var list = new List<object>();
        i++;
        Ws(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return list; }
        while (i < s.Length)
        {
            list.Add(Value(s, ref i));
            Ws(s, ref i);
            if (s[i] == ',') { i++; continue; }
            if (s[i] == ']') { i++; return list; }
        }
        return list;
    }

    private static string Str(string s, ref int i)
    {
        i++;
        var sb = new System.Text.StringBuilder();
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\\')
            {
                i++;
                char e = s[i];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            else if (c == '"')
            {
                i++;
                return sb.ToString();
            }
            else
            {
                sb.Append(c);
            }
            i++;
        }
        return sb.ToString();
    }

    private static double Num(string s, ref int i)
    {
        int start = i;
        if (s[i] == '-') i++;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        if (i < s.Length && s[i] == '.')
        {
            i++;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        }
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            i++;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        }
        return double.Parse(s.Substring(start, i - start),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool Bool(string s, ref int i)
    {
        if (s[i] == 't') { i += 4; return true; }
        i += 5; return false;
    }

    private static void Ws(string s, ref int i)
    {
        while (i < s.Length && s[i] <= ' ') i++;
    }
}
