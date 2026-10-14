using System.Collections.Generic;

namespace mod_update_manager
{
    /// <summary>
    /// Reads the body of Nexus's changelogs.json, {"1.2.0": ["note", "note"], "1.1.0": [...]}, into
    /// (version, notes) pairs, newest first. A small scanner instead of a pattern: it finds each
    /// "version": [ and reads the array's string elements, honoring backslash escapes, until the
    /// closing ] that sits outside a string, so a note holding [ ] { } or an escaped quote cannot
    /// end the array early or drop the version (the pattern it replaces skipped any version whose
    /// notes held a square bracket, with no log line). A version whose array is never closed (a
    /// cut-off body) is left out rather than shown half read.
    ///
    /// Notes come back decoded (JsonText.Unescape), with line breaks collapsed to a space and the
    /// ends trimmed; a note that is empty after that is dropped, a version with no notes is kept.
    /// CompatibilityChip.FindTestedBuild reads the result and expects this newest-first order.
    ///
    /// Host-free: no BepInEx or UnityEngine reference (logging goes through MumLog).
    /// Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1 compiles and drives it.
    /// </summary>
    public static class ChangelogParser
    {
        public static List<(string Version, List<string> Entries)> Parse(string json)
        {
            var result = new List<(string Version, List<string> Entries)>();
            if (string.IsNullOrEmpty(json)) return result;

            int i = 0;
            while (i < json.Length)
            {
                if (json[i] != '"')
                {
                    i++;
                    continue;
                }

                int afterKey = ReadString(json, i, out string rawKey);
                if (afterKey < 0) break;

                int colon = SkipWhitespace(json, afterKey);
                if (colon >= json.Length || json[colon] != ':')
                {
                    i = afterKey;
                    continue;
                }

                int value = SkipWhitespace(json, colon + 1);
                if (value >= json.Length || json[value] != '[')
                {
                    i = colon + 1;
                    continue;
                }

                var entries = new List<string>();
                bool closed = ReadArray(json, value, entries, out i);
                if (closed)
                    result.Add((JsonText.Unescape(rawKey), entries));
            }

            result.Sort((a, b) => VersionComparer.Compare(b.Version, a.Version));

            if (result.Count == 0 && !string.IsNullOrWhiteSpace(json) && !IsEmptyContainer(json))
                MumLog.Debug($"ChangelogParser: a {json.Length}-character body held no version-to-notes arrays.");

            return result;
        }

        // json[open] is the opening quote. Returns the index just past the closing quote and the raw
        // (still escaped) text between them, or -1 when the string never closes.
        private static int ReadString(string json, int open, out string raw)
        {
            int p = open + 1;
            while (p < json.Length)
            {
                char c = json[p];
                if (c == '\\')
                {
                    p += 2;
                    continue;
                }
                if (c == '"')
                {
                    raw = json.Substring(open + 1, p - open - 1);
                    return p + 1;
                }
                p++;
            }
            raw = null;
            return -1;
        }

        // json[open] is the opening [. Adds each string element to entries and returns true with
        // next just past the closing ]; returns false (next at the end) when the array never closes.
        // A nested array or object element is skipped whole, never read for notes.
        private static bool ReadArray(string json, int open, List<string> entries, out int next)
        {
            int p = open + 1;
            while (p < json.Length)
            {
                char c = json[p];
                if (c == ']')
                {
                    next = p + 1;
                    return true;
                }
                if (c == '"')
                {
                    int after = ReadString(json, p, out string raw);
                    if (after < 0) break;
                    AddEntry(entries, raw);
                    p = after;
                    continue;
                }
                if (c == '[' || c == '{')
                {
                    p = SkipNested(json, p);
                    continue;
                }
                p++;
            }
            next = json.Length;
            return false;
        }

        // json[open] is a [ or {. Returns the index just past its matching close, string-aware,
        // or the end of the text when it never balances.
        private static int SkipNested(string json, int open)
        {
            int depth = 0;
            int p = open;
            while (p < json.Length)
            {
                char c = json[p];
                if (c == '"')
                {
                    int after = ReadString(json, p, out string ignored);
                    if (after < 0) return json.Length;
                    p = after;
                    continue;
                }
                if (c == '[' || c == '{')
                {
                    depth++;
                }
                else if (c == ']' || c == '}')
                {
                    depth--;
                    if (depth == 0) return p + 1;
                }
                p++;
            }
            return json.Length;
        }

        private static void AddEntry(List<string> entries, string raw)
        {
            var text = JsonText.Unescape(raw).Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (text.Length > 0)
                entries.Add(text);
        }

        private static int SkipWhitespace(string json, int p)
        {
            while (p < json.Length && char.IsWhiteSpace(json[p]))
                p++;
            return p;
        }

        // {} and [] are a valid answer for a mod with no changelog, not a body that failed to parse.
        private static bool IsEmptyContainer(string json)
        {
            var compact = new System.Text.StringBuilder();
            foreach (char c in json)
                if (!char.IsWhiteSpace(c))
                    compact.Append(c);
            var text = compact.ToString();
            return text == "{}" || text == "[]";
        }
    }
}
