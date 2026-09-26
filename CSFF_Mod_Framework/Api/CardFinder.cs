using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Cached whole-scene card lookup (Centralization Tier 3). Scans
/// <c>UnityEngine.Object.FindObjectsOfType(InGameCardBase)</c> once and reuses the
/// result until the card objects in <c>GameManager.AllCards</c> change (count plus instance ids;
/// a count alone missed vanilla Transforms, which keep it unchanged). It replaces
/// the uncached per-call scene scans independently duplicated in Community_Mod_Chest
/// (InnPatch/MarketStallPatch), RepeatAction, and QuickTransfer. Sirus's
/// WolfTickPatch already used exactly this cache-until-board-changes shape; this
/// lifts it to the framework so other mods don't reinvent it.
/// </summary>
public static class CardFinder
{
    private static Type _cardBaseType;
    private static object[] _cache = Array.Empty<object>();
    private static bool _cacheValid;
    private static long _lastFingerprint;

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
    public static void Invalidate() => _cacheValid = false;

    private static void RefreshIfBoardChanged()
    {
        _cardBaseType ??= CardUtil.FindGameType("InGameCardBase");
        if (_cardBaseType == null) { _cache = Array.Empty<object>(); return; }

        long fingerprint = GetAllCardsFingerprint();
        if (_cacheValid && fingerprint == _lastFingerprint) return;
        _cacheValid = true;
        _lastFingerprint = fingerprint;

        _cache = UnityEngine.Object.FindObjectsOfType(_cardBaseType) ?? Array.Empty<object>();
    }

    // A vanilla Transform is RemoveCard + AddCard, which leaves AllCards.Count unchanged, so a
    // count-only key kept the transformed card invisible until an unrelated card entered or
    // left the board. Summing instance ids changes whenever any card object is replaced.
    private static long GetAllCardsFingerprint()
    {
        var gm = CardUtil.GetGameManagerInstance();
        if (gm == null) return -1;
        if (Reflect.GetMember(gm, "AllCards") is not IEnumerable cards) return -1;
        long count = 0, idSum = 0;
        foreach (var c in cards)
        {
            count++;
            if (c is UnityEngine.Object uo) idSum += uo.GetInstanceID();
        }
        return unchecked(count * 1000003L + idSum);
    }

    private static bool SafeMatch(Func<object, bool> predicate, object card)
    {
        try { return predicate(card); }
        catch (Exception ex) { Log.Debug($"[CardFinder] match predicate threw: {Log.ExceptionText(ex)}"); return false; }
    }
}
