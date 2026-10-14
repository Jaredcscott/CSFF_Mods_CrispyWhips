using System.Globalization;
using System.Text;

namespace mod_update_manager
{
    /// <summary>
    /// The one decoder for the escapes inside a JSON string, shared by every hand-written reader in
    /// this mod (the changelog parser, the Nexus response cache, SimpleJson). It handles
    /// \\ \" \/ \n \r \t \b \f and \uXXXX in a single left-to-right pass, so an escaped backslash
    /// followed by an "n" stays a backslash and an "n" (a chain of string Replace calls turns that
    /// pair into a newline). An escape it does not know, or a \u not followed by four hex digits, is
    /// kept as written.
    ///
    /// Host-free: no BepInEx or UnityEngine reference. Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1
    /// compiles and drives it.
    /// </summary>
    public static class JsonText
    {
        /// <summary>Decodes the escapes in the raw text between a JSON string's quotes. Null stays null.</summary>
        public static string Unescape(string s)
        {
            if (s == null) return null;
            if (s.IndexOf('\\') < 0) return s;

            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(c);
                    continue;
                }

                switch (s[i + 1])
                {
                    case '\\': sb.Append('\\'); i++; break;
                    case '"': sb.Append('"'); i++; break;
                    case '/': sb.Append('/'); i++; break;
                    case 'n': sb.Append('\n'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    case 'b': sb.Append('\b'); i++; break;
                    case 'f': sb.Append('\f'); i++; break;
                    case 'u':
                        if (i + 5 < s.Length
                            && int.TryParse(s.Substring(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                        {
                            sb.Append((char)code);
                            i += 5;
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
