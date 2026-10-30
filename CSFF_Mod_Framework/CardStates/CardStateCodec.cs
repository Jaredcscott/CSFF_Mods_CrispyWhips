using System.Globalization;
using System.Text;
using CSFFModFramework.Api;

namespace CSFFModFramework.CardStates;

/// <summary>
/// The pure half of <see cref="Api.CardState"/> (fleet plan 6.3, finding
/// <c>Documentation/Research/Fleet/APE_Port/W0.5_ingredient_memory.md</c>): the per-instance id, the sidecar file
/// format, the scan of a written save for the ids it still holds, and the carry merge. BCL types only, so the offline
/// gate (<c>Development_Tools/Tests/Framework-CardState.Tests.ps1</c>) drives every method here from the built DLL.
/// </summary>
internal static class CardStateCodec
{
    /// <summary>
    /// The <c>InGameCardBase.DroppedCollections</c> entry that carries a card's id. Vanilla saves every entry
    /// (<c>CardSaveDataByReference.CollectionUses</c>), restores every non-empty name in <c>Init</c>, and reads the
    /// dictionary only by the name of a collection the card's own data declares, so this one is inert to the game.
    /// </summary>
    internal const string IdKey = "csff.cs.id";

    /// <summary>Names a caller may not use: the framework's own entries live under it.</summary>
    internal const string ReservedPrefix = "csff.cs.";

    /// <summary>The hidden per-name count an Average carry keeps on a blueprint site between stages.</summary>
    internal const string CountPrefix = "csff.cs.n:";

    /// <summary>First line of a sidecar file; a different first line is a file this version cannot read.</summary>
    internal const string Header = "CSFFModFramework.CardState 1";

    /// <summary>Subfolder of the save slot (<c>Games/Game_N/</c>) that holds the sidecars.</summary>
    internal const string FolderName = "CSFFModFramework";

    internal const string Extension = ".cardstate";

    internal const int MaxNameLength = 128;

    // ── Id ───────────────────────────────────────────────────────────────────

    /// <summary>The 64-bit id stored as the two ints of a <c>Vector2Int</c> (x low half, y high half).</summary>
    internal static long ToId(int x, int y) => ((long)y << 32) | (uint)x;

    internal static int IdLow(long id) => unchecked((int)(id & 0xFFFFFFFFL));

    internal static int IdHigh(long id) => unchecked((int)(id >> 32));

    /// <summary>
    /// A random non-zero id. Random, not a counter: two checkpoints of one run, or a new run in the same slot, never
    /// mint the same sequence, so a sidecar can only ever match the cards of its own lineage.
    /// </summary>
    internal static long NewId()
    {
        while (true)
        {
            long id = IdFromBytes(Guid.NewGuid().ToByteArray());
            if (id != 0) return id;
        }
    }

    /// <summary>The first eight bytes as a little-endian long (0 for fewer than eight).</summary>
    internal static long IdFromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 8) return 0;
        return BitConverter.ToInt64(bytes, 0);
    }

    internal static string IdText(long id) => id.ToString("x16", CultureInfo.InvariantCulture);

    internal static bool TryParseIdText(string text, out long id)
    {
        id = 0;
        if (text == null || text.Length != 16) return false;
        if (!ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var u)) return false;
        id = unchecked((long)u);
        return id != 0;
    }

    // ── Names ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A caller's value name: 1 to <see cref="MaxNameLength"/> characters, no control character (the sidecar is one
    /// value per line, tab-separated), no leading or trailing space, and not under <see cref="ReservedPrefix"/>.
    /// Returns null when valid, else the reason.
    /// </summary>
    internal static string NameProblem(string name)
    {
        if (string.IsNullOrEmpty(name)) return "the name is empty";
        if (name.Length > MaxNameLength) return $"the name is longer than {MaxNameLength} characters";
        if (name.Trim().Length != name.Length) return "the name starts or ends with a space";
        foreach (char c in name)
            if (c < ' ' || c == '\u007f') return "the name holds a control character";
        if (name.StartsWith(ReservedPrefix, StringComparison.Ordinal)) return $"names starting '{ReservedPrefix}' are the framework's own";
        return null;
    }

    internal static bool IsCountName(string name) => name != null && name.StartsWith(CountPrefix, StringComparison.Ordinal);

    internal static string CountName(string name) => CountPrefix + name;

    // ── Sidecar file ─────────────────────────────────────────────────────────

    /// <summary>
    /// The sidecar's file name for a save file name (<c>AutoSave.json</c> -> <c>AutoSave.cardstate</c>,
    /// <c>Custom_X.json</c> -> <c>Custom_X.cardstate</c>); null for an empty name or one that is not a plain file name.
    /// </summary>
    internal static string SidecarFileName(string saveFileName)
    {
        if (string.IsNullOrEmpty(saveFileName)) return null;
        if (saveFileName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return null;
        foreach (char c in saveFileName)
            if (c < ' ') return null;
        string stem = saveFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? saveFileName.Substring(0, saveFileName.Length - ".json".Length)
            : saveFileName;
        if (stem.Length == 0 || stem == "." || stem == "..") return null;
        return stem + Extension;
    }

    /// <summary>The save file a sidecar belongs to (<c>AutoSave.cardstate</c> -> <c>AutoSave.json</c>); null if not a sidecar name.</summary>
    internal static string SaveFileNameOf(string sidecarFileName)
    {
        if (string.IsNullOrEmpty(sidecarFileName) || !sidecarFileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) return null;
        string stem = sidecarFileName.Substring(0, sidecarFileName.Length - Extension.Length);
        return stem.Length == 0 ? null : stem + ".json";
    }

    /// <summary>
    /// The sidecar text: <see cref="Header"/>, then one line per value, <c>id TAB name TAB value</c>, sorted by id
    /// then name, values in round-trip invariant form. Cards with no values write no line.
    /// </summary>
    internal static string Format(IDictionary<long, Dictionary<string, float>> state)
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        if (state == null) return sb.ToString();
        var ids = new List<long>(state.Keys);
        ids.Sort();
        foreach (var id in ids)
        {
            var values = state[id];
            if (values == null || values.Count == 0) continue;
            var names = new List<string>(values.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (var name in names)
            {
                float v = values[name];
                if (float.IsNaN(v) || float.IsInfinity(v)) continue;
                sb.Append(IdText(id)).Append('\t').Append(name).Append('\t')
                  .Append(v.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Reads a sidecar. Returns null (and <paramref name="error"/>) when the first line is not <see cref="Header"/>:
    /// the whole file is then ignored. Any other line that is not a valid <c>id TAB name TAB value</c> (a bad id, an
    /// invalid name, a value that is not a finite number) is skipped and counted in <paramref name="badLines"/>;
    /// blank lines are ignored. A name the framework writes for itself (<see cref="CountPrefix"/>) is accepted.
    /// </summary>
    internal static Dictionary<long, Dictionary<string, float>> Parse(string text, out int badLines, out string error)
    {
        badLines = 0;
        error = null;
        if (text == null) { error = "no text"; return null; }
        if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0] != Header)
        {
            error = lines.Length == 0 || lines[0].Length == 0 ? "the file is empty" : "the first line is not '" + Header + "'";
            return null;
        }
        var state = new Dictionary<long, Dictionary<string, float>>();
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            var parts = line.Split('\t');
            if (parts.Length != 3) { badLines++; continue; }
            if (!TryParseIdText(parts[0], out long id)) { badLines++; continue; }
            string name = parts[1];
            if (!(IsCountName(name) && NameProblem(name.Substring(CountPrefix.Length)) == null) && NameProblem(name) != null) { badLines++; continue; }
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) || float.IsNaN(v) || float.IsInfinity(v)) { badLines++; continue; }
            if (!state.TryGetValue(id, out var values)) state[id] = values = new Dictionary<string, float>(StringComparer.Ordinal);
            values[name] = v;
        }
        return state;
    }

    // ── Save scan ────────────────────────────────────────────────────────────

    private const string IdNeedle = "\"" + IdKey + "\"";

    /// <summary>
    /// Every card id the written save still holds: each <c>"csff.cs.id"</c> collection entry as
    /// <c>JsonUtility.ToJson</c> writes it (<c>{"CollectionName":"csff.cs.id","CollectionDrops":{"x":X,"y":Y}}</c>,
    /// whitespace tolerated), wherever the card sits in the save (a board, an inventory, another environment's saved
    /// board). An entry whose x and y cannot be read within the entry is counted in <paramref name="malformed"/>.
    /// </summary>
    internal static HashSet<long> ScanIds(string json, out int malformed)
    {
        malformed = 0;
        var ids = new HashSet<long>();
        if (string.IsNullOrEmpty(json)) return ids;
        int at = 0;
        while ((at = json.IndexOf(IdNeedle, at, StringComparison.Ordinal)) >= 0)
        {
            at += IdNeedle.Length;
            // The entry ends at the second '}' after the name (CollectionDrops' own, then the entry's); never read
            // past the next entry's name either.
            int limit = Math.Min(json.Length, at + 256);
            int next = json.IndexOf("\"CollectionName\"", at, StringComparison.Ordinal);
            if (next >= 0 && next < limit) limit = next;
            if (TryReadMember(json, at, limit, "x", out int x, out int afterX)
                && TryReadMember(json, afterX, limit, "y", out int y, out _))
            {
                long id = ToId(x, y);
                if (id != 0) ids.Add(id); else malformed++;
            }
            else malformed++;
        }
        return ids;
    }

    private static bool TryReadMember(string json, int from, int limit, string member, out int value, out int after)
    {
        value = 0;
        after = from;
        string key = "\"" + member + "\"";
        int k = json.IndexOf(key, from, StringComparison.Ordinal);
        if (k < 0 || k >= limit) return false;
        int i = k + key.Length;
        while (i < limit && char.IsWhiteSpace(json[i])) i++;
        if (i >= limit || json[i] != ':') return false;
        i++;
        while (i < limit && char.IsWhiteSpace(json[i])) i++;
        int start = i;
        if (i < limit && json[i] == '-') i++;
        while (i < limit && json[i] >= '0' && json[i] <= '9') i++;
        if (i == start || (i == start + 1 && json[start] == '-')) return false;
        if (!int.TryParse(json.Substring(start, i - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)) return false;
        after = i;
        return true;
    }

    // ── Carry ────────────────────────────────────────────────────────────────

    /// <summary>
    /// One blueprint stage's merge of a name: the site's own value so far (<paramref name="hasSite"/>,
    /// <paramref name="site"/>, built from <paramref name="siteCount"/> ingredient values; a count below 1 counts as
    /// 1) and this stage's ingredient values. Returns false (nothing to keep) for <see cref="CardStateMerge.None"/>
    /// or when there is no value at all. <see cref="CardStateMerge.First"/> keeps the site's value, else the first
    /// ingredient's; <see cref="CardStateMerge.Average"/> weights the site's value by its count, so an average over
    /// several stages equals the average over every ingredient. <paramref name="count"/> is the number of values the
    /// result stands for.
    /// </summary>
    internal static bool TryMerge(CardStateMerge mode, bool hasSite, float site, int siteCount, IList<float> inputs, out float value, out int count)
    {
        value = 0f;
        count = 0;
        int n = inputs?.Count ?? 0;
        if (mode == CardStateMerge.None) return false;
        if (!hasSite && n == 0) return false;
        int w = hasSite ? Math.Max(siteCount, 1) : 0;
        count = w + n;
        switch (mode)
        {
            case CardStateMerge.First:
                value = hasSite ? site : inputs[0];
                return true;
            case CardStateMerge.Min:
            case CardStateMerge.Max:
            {
                bool max = mode == CardStateMerge.Max;
                float best = hasSite ? site : inputs[0];
                for (int i = 0; i < n; i++)
                    best = max ? Math.Max(best, inputs[i]) : Math.Min(best, inputs[i]);
                value = best;
                return true;
            }
            case CardStateMerge.Sum:
            {
                double total = hasSite ? site : 0.0;
                for (int i = 0; i < n; i++) total += inputs[i];
                value = (float)total;
                return true;
            }
            case CardStateMerge.Average:
            {
                double total = hasSite ? (double)site * w : 0.0;
                for (int i = 0; i < n; i++) total += inputs[i];
                value = (float)(total / count);
                return true;
            }
            default:
                count = 0;
                return false;
        }
    }

    /// <summary>
    /// The names a transform removes from a card: every caller name not in <paramref name="carriedThroughTransform"/>,
    /// and every hidden count whose name is not carried.
    /// </summary>
    internal static List<string> NamesDroppedByTransform(IEnumerable<string> names, ICollection<string> carriedThroughTransform)
    {
        var drop = new List<string>();
        if (names == null) return drop;
        foreach (var name in names)
        {
            string owner = IsCountName(name) ? name.Substring(CountPrefix.Length) : name;
            if (carriedThroughTransform == null || !carriedThroughTransform.Contains(owner)) drop.Add(name);
        }
        return drop;
    }
}
