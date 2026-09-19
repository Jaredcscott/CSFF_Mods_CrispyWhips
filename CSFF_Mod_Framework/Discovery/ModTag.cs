using System.Text;

namespace CSFFModFramework.Discovery;

/// <summary>
/// The short display tag a mod is known by in the UI ("CMC", "WDI", "S23"). Used by
/// <see cref="Patching.PerkOriginTagPatch"/> to mark which mod added a character perk.
///
/// <para>An explicit <c>ShortName</c> in <c>ModInfo.json</c> always wins. Without one the tag is
/// derived from the mod's <c>Name</c>, so a third-party mod that has never heard of the key still
/// gets a usable tag.</para>
///
/// <para>Pure string logic on purpose: no Unity type and no game type, so the rule can be compiled
/// and exercised outside the game.</para>
/// </summary>
internal static class ModTag
{
    /// <summary>Longest tag accepted from an explicit <c>ShortName</c>; the rest is cut off.</summary>
    internal const int MaxExplicitLength = 8;

    /// <summary>Longest tag derived from a mod <c>Name</c>.</summary>
    internal const int MaxDerivedLength = 5;

    /// <summary>Characters taken from a name that is a single word, where initials would be one letter.</summary>
    private const int SingleWordLength = 3;

    // Connectives carry no identity: "Herbs and Fungi" reads as HF, not HAF.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        { "a", "an", "and", "for", "in", "of", "on", "the", "to" };

    /// <summary>The tag to display: the sanitized <paramref name="shortName"/>, else one derived
    /// from <paramref name="name"/>. Returns an empty string when neither yields anything.</summary>
    public static string Resolve(string shortName, string name)
    {
        var declared = Sanitize(shortName, MaxExplicitLength);
        return declared.Length > 0 ? declared : Derive(name);
    }

    /// <summary>
    /// Cleans a manifest-supplied tag. Whitespace and control characters are dropped, and so are
    /// the four bracket characters: square brackets would nest inside the " [TAG]" the UI renders,
    /// and angle brackets open a TextMeshPro rich-text tag.
    /// </summary>
    public static string Sanitize(string raw, int maxLength)
    {
        if (string.IsNullOrEmpty(raw) || maxLength <= 0) return "";
        var sb = new StringBuilder(maxLength);
        foreach (var c in raw)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c)) continue;
            if (c == '[' || c == ']' || c == '<' || c == '>') continue;
            sb.Append(c);
            if (sb.Length >= maxLength) break;
        }
        return TrimDanglingSurrogate(sb);
    }

    /// <summary>
    /// Derives a tag from a mod name.
    /// <list type="bullet">
    /// <item>Words split on anything that is not a letter or digit, and inside a run on a case
    /// boundary ("WaterDrivenInfrastructure" is three words, "CSFFMod" is "CSFF" + "Mod").</item>
    /// <item>Each word gives its first character, upper-cased, plus every digit it contains, because
    /// digits are what tell "Sirus23" from "Sirus" and "Storage 2" from "Storage 3".</item>
    /// <item>Connectives (and, of, the, ...) are skipped unless that would leave under two words.</item>
    /// <item>A name that is one word gives its first <see cref="SingleWordLength"/> characters,
    /// since a one-letter tag identifies nothing. Scripts without case (CJK) land here too.</item>
    /// <item>The result is cut to <see cref="MaxDerivedLength"/> characters.</item>
    /// </list>
    /// </summary>
    public static string Derive(string name)
    {
        var words = SplitWords(name);
        if (words.Count == 0) return "";

        var significant = new List<string>(words.Count);
        foreach (var w in words)
            if (!StopWords.Contains(w)) significant.Add(w);
        if (significant.Count < 2) significant = words;

        var sb = new StringBuilder(MaxDerivedLength + 4);
        if (significant.Count == 1)
        {
            var only = significant[0];
            for (int i = 0; i < only.Length && sb.Length < SingleWordLength; i++)
                sb.Append(char.ToUpperInvariant(only[i]));
        }
        else
        {
            foreach (var w in significant)
            {
                sb.Append(char.ToUpperInvariant(w[0]));
                int rest = 1;
                // Keep both halves of an initial that is a surrogate pair.
                if (char.IsHighSurrogate(w[0]) && w.Length > 1) sb.Append(w[rest++]);
                for (int i = rest; i < w.Length; i++)
                    if (char.IsDigit(w[i])) sb.Append(w[i]);
            }
        }

        if (sb.Length > MaxDerivedLength) sb.Length = MaxDerivedLength;
        return TrimDanglingSurrogate(sb);
    }

    private static List<string> SplitWords(string name)
    {
        var words = new List<string>();
        if (string.IsNullOrEmpty(name)) return words;

        var current = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (!char.IsLetterOrDigit(c) && !char.IsSurrogate(c))
            {
                Flush(current, words);
                continue;
            }

            if (current.Length > 0 && char.IsUpper(c))
            {
                char prev = name[i - 1];
                bool afterLowerOrDigit = char.IsLower(prev) || char.IsDigit(prev);
                // "CSFFMod": the M opens a new word because a lower-case letter follows it.
                bool acronymEnds = char.IsUpper(prev) && i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (afterLowerOrDigit || acronymEnds) Flush(current, words);
            }
            current.Append(c);
        }
        Flush(current, words);
        return words;
    }

    private static void Flush(StringBuilder current, List<string> words)
    {
        if (current.Length == 0) return;
        words.Add(current.ToString());
        current.Length = 0;
    }

    // A length cut can land between the two halves of a surrogate pair; half a pair renders as a
    // replacement glyph, so drop it.
    private static string TrimDanglingSurrogate(StringBuilder sb)
    {
        if (sb.Length > 0 && char.IsHighSurrogate(sb[sb.Length - 1])) sb.Length--;
        return sb.ToString();
    }
}
