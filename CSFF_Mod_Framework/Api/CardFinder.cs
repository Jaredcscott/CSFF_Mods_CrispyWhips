using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Cached whole-scene card lookup (Centralization Tier 3). Scans
/// <c>UnityEngine.Object.FindObjectsOfType(InGameCardBase)</c> once and reuses the
/// result until <c>GameManager.AllCards.Count</c> changes (a cheap signal) — replaces
/// the uncached per-call scene scans independently duplicated in Community_Mod_Chest
/// (InnPatch/MarketStallPatch), RepeatAction, and QuickTransfer. Sirus's
/// WolfTickPatch already used exactly this cache-until-board-changes shape; this
/// lifts it to the framework so other mods don't reinvent it.
/// </summary>
public static class CardFinder
{
    private static Type _cardBaseType;
    private static object[] _cache = Array.Empty<object>();
    private static int _lastAllCardsCount = int.MinValue;

    /// <summary>All live InGameCardBase instances in the scene, cached until the board changes.</summary>
    public static IReadOnlyList<object> AllCards()
    {
        RefreshIfBoardChanged();
        return _cache;
    }

    /// <summary>First live card whose UniqueID matches (case-insensitive). Null if none found.</summary>
    public static object Find(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return null;
        foreach (var card in AllCards())
            if (string.Equals(CardUtil.GetCardUniqueId(card), uid, StringComparison.OrdinalIgnoreCase))
                return card;
        return null;
    }

    /// <summary>All live cards whose UniqueID matches (case-insensitive).</summary>
    public static List<object> FindAll(string uid)
    {
        var result = new List<object>();
        if (string.IsNullOrEmpty(uid)) return result;
        foreach (var card in AllCards())
            if (string.Equals(CardUtil.GetCardUniqueId(card), uid, StringComparison.OrdinalIgnoreCase))
                result.Add(card);
        return result;
    }

    /// <summary>First live card matching a custom predicate. Null if none found.</summary>
    public static object Find(Func<object, bool> predicate)
    {
        if (predicate == null) return null;
        foreach (var card in AllCards())
            if (SafeMatch(predicate, card)) return card;
        return null;
    }

    /// <summary>All live cards matching a custom predicate.</summary>
    public static List<object> FindAll(Func<object, bool> predicate)
    {
        var result = new List<object>();
        if (predicate == null) return result;
        foreach (var card in AllCards())
            if (SafeMatch(predicate, card)) result.Add(card);
        return result;
    }

    /// <summary>
    /// Forces the next <see cref="AllCards"/> call to re-scan even if the board's card
    /// count hasn't changed — e.g. after an in-place CardModel swap that doesn't add or
    /// remove a card (CLAUDE.md's runtime-card-state-caching rule applies to this cache too).
    /// </summary>
    public static void Invalidate() => _lastAllCardsCount = int.MinValue;

    private static void RefreshIfBoardChanged()
    {
        _cardBaseType ??= CardUtil.FindGameType("InGameCardBase");
        if (_cardBaseType == null) { _cache = Array.Empty<object>(); return; }

        int count = GetAllCardsCount();
        if (_lastAllCardsCount != int.MinValue && count == _lastAllCardsCount) return;
        _lastAllCardsCount = count;

        _cache = UnityEngine.Object.FindObjectsOfType(_cardBaseType) ?? Array.Empty<object>();
    }

    private static int GetAllCardsCount()
    {
        var gm = CardUtil.GetGameManagerInstance();
        if (gm == null) return -1;
        return Reflect.GetMember(gm, "AllCards") is ICollection c ? c.Count : -1;
    }

    private static bool SafeMatch(Func<object, bool> predicate, object card)
    {
        try { return predicate(card); }
        catch { return false; }
    }
}
