namespace CSFFModFramework.Util;

/// <summary>
/// Decides the log level of a reference that did not resolve (framework README "Unresolved references").
/// A miss on a UID the game itself would own, or one a loaded mod registered, can only be an authoring
/// error or a game update, so it stays at Warn. Any other miss is most often a partner mod that is simply
/// not installed, so it logs at Debug and the caller counts it in its Info summary, which names the UIDs
/// (Fleet plan feature 8, framework plan section 32). A typo of a partner UID therefore stays visible.
/// </summary>
internal static class UnresolvedUid
{
    /// <summary>
    /// True when <paramref name="uid"/> is something the game or a loaded mod should have supplied: a
    /// 32-character hex GUID (every vanilla UniqueID has that shape), a vanilla card or stat name from the
    /// generated <see cref="Api.VanillaIds"/> table, or a UID a loaded mod's JSON registered.
    /// </summary>
    internal static bool ExpectedPresent(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return true;
        if (IsHexGuid(uid)) return true;
        if (Loading.JsonDataLoader.UniqueIdToModName.ContainsKey(uid)) return true;
        return Api.VanillaIds.Get(uid) != null || Api.VanillaIds.GetStat(uid) != null;
    }

    internal static bool IsHexGuid(string uid)
    {
        if (uid == null || uid.Length != 32) return false;
        foreach (var c in uid)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
        return true;
    }

    /// <summary>First few UIDs of a miss list, for an Info summary line ("a, b, c (+2 more)").</summary>
    internal static string Sample(IReadOnlyList<string> uids, int max = 5)
    {
        if (uids == null || uids.Count == 0) return "";
        var shown = uids.Distinct(StringComparer.Ordinal).ToList();
        var head = string.Join(", ", shown.Take(max));
        return shown.Count > max ? $"{head} (+{shown.Count - max} more)" : head;
    }
}
