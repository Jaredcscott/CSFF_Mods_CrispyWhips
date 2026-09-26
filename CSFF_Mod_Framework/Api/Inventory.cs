using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Container inventory traversal, counting, consumption, and ejection
/// (Centralization Tier 1, P5). Replaces the nested slot→AllCards loops in
/// WDI ActionInterceptPatch (~7 sites), ACT TeaStationPatch, and Sirus WolfTickPatch.
///
/// <para>Container inventories hold InventorySlot wrappers (EA 0.63+), each wrapping
/// N stacked cards; older versions returned cards directly. All methods handle both
/// shapes — callers never drill the slot layer themselves.</para>
///
/// <para>Matching by <c>uidOrTag</c>: a value starting with <c>tag_</c> matches cards
/// whose CardModel carries that CardTag (by runtime <c>.name</c>, per CLAUDE.md
/// CardTag-identity rule); anything else is compared against the card's UniqueID.</para>
/// </summary>
public static class Inventory
{
    // Cached once (never re-resolved to null, per CardUtil.FindGameType) so the pre-0.63
    // fallback below can tell a real card from an empty InventorySlot wrapper without
    // paying reflection cost per slot per call.
    private static Type _cardBaseType;
    private static bool _cardBaseTypeResolveAttempted;
    private static bool _cardBaseTypeMissingWarned;

    private static bool IsCardInstance(object obj)
    {
        if (!_cardBaseTypeResolveAttempted)
        {
            _cardBaseTypeResolveAttempted = true;
            _cardBaseType = CardUtil.FindGameType("InGameCardBase");
        }
        if (_cardBaseType == null)
        {
            // Silent Catch Blocks rule: a data-read path that fails closed needs a
            // breadcrumb, not a quiet behaviour change. Fail closed here (treat the
            // slot as not-a-card) rather than resurrecting the pre-fix phantom-add.
            if (!_cardBaseTypeMissingWarned)
            {
                _cardBaseTypeMissingWarned = true;
                Log.Warn("[Api.Inventory] Cards: InGameCardBase type not found; the pre-0.63 " +
                    "slot-is-card fallback is disabled for this session (nothing is added for " +
                    "an unwrapped slot with no inner list).");
            }
            return false;
        }
        return _cardBaseType.IsInstanceOfType(obj);
    }

    /// <summary>
    /// All individual cards inside <paramref name="container"/>, flattened across
    /// InventorySlot wrappers and stacks. Returns an empty list when the container
    /// has no inventory.
    /// </summary>
    public static List<object> Cards(object container)
    {
        var result = new List<object>();
        var slots = CardUtil.GetInventoryList(container);
        if (slots == null) return result;

        foreach (var slot in slots)
        {
            if (slot == null) continue;
            var inner = CardUtil.GetInventoryList(slot);
            if (inner != null)
            {
                // EA 0.68b InventorySlot shape: always resolves its own (possibly empty)
                // AllCards list. An empty slot legitimately contributes zero cards here -
                // it is NOT itself a card (bug: it was being added as one, see CLAUDE.md
                // "Harmony Patching Pitfalls" / CMC's copper-chest false-theft report).
                foreach (var card in inner)
                    if (card != null) result.Add(card);
            }
            else if (IsCardInstance(slot))
            {
                // Pre-0.63 shape: the "slot" IS the card (no wrapper, no inner list).
                result.Add(slot);
            }
            // else: neither an InventorySlot wrapper (no inner list resolved) nor a real
            // card instance - nothing to contribute.
        }
        return result;
    }

    /// <summary>Cards inside the container matching a UniqueID or <c>tag_*</c> name.</summary>
    public static List<object> Find(object container, string uidOrTag)
    {
        var result = new List<object>();
        if (string.IsNullOrEmpty(uidOrTag)) return result;
        foreach (var card in Cards(container))
            if (Matches(card, uidOrTag))
                result.Add(card);
        return result;
    }

    /// <summary>Cards inside the container matching a predicate.</summary>
    public static List<object> Find(object container, Func<object, bool> predicate)
    {
        var result = new List<object>();
        if (predicate == null) return result;
        foreach (var card in Cards(container))
            if (predicate(card))
                result.Add(card);
        return result;
    }

    /// <summary>Number of cards inside the container matching a UniqueID or <c>tag_*</c> name.</summary>
    public static int Count(object container, string uidOrTag)
        => Find(container, uidOrTag).Count;

    /// <summary>
    /// Removes up to <paramref name="count"/> cards matching <paramref name="uidOrTag"/>
    /// from the container's slot lists and deactivates them (spawn-eject pattern — no
    /// OnDestroy fires, so nothing relocates to adjacent containers). Returns the number
    /// of cards consumed. Spawn replacement outputs separately via GiveCard.
    /// </summary>
    public static int Consume(object container, string uidOrTag, int count)
    {
        if (count <= 0) return 0;
        var matches = Find(container, uidOrTag);
        if (matches.Count == 0) return 0;
        if (matches.Count > count) matches.RemoveRange(count, matches.Count - count);
        return Eject(container, matches);
    }

    /// <summary>
    /// Removes the given cards from the container's InventorySlot lists and deactivates
    /// them; empty slots are removed from the slot list. This is the proven
    /// spawn-eject removal path (memory: reference_spawn_eject_pattern) — it avoids
    /// the OnDestroy relocation hazard of DestroyCard. Returns the number ejected.
    /// </summary>
    public static int Eject(object container, IEnumerable<object> cards)
    {
        if (container == null || cards == null) return 0;
        int ejected = 0;
        try
        {
            var cardSet = new HashSet<object>(cards);
            if (cardSet.Count == 0) return 0;
            var slots = CardUtil.GetInventoryList(container);
            if (slots == null) return 0;
            var emptySlots = new List<object>();

            foreach (var slot in slots)
            {
                if (slot == null) continue;
                var inner = CardUtil.GetInventoryList(slot);
                if (inner != null)
                {
                    var toRemove = new List<object>();
                    foreach (var c in inner)
                        if (c != null && cardSet.Contains(c)) toRemove.Add(c);
                    foreach (var c in toRemove)
                    {
                        inner.Remove(c);
                        TrySetActive(c, false);
                        ejected++;
                    }
                    if (inner.Count == 0 && toRemove.Count > 0) emptySlots.Add(slot);
                }
                else if (cardSet.Contains(slot))
                {
                    emptySlots.Add(slot);
                    TrySetActive(slot, false);
                    ejected++;
                }
            }
            foreach (var slot in emptySlots) slots.Remove(slot);
        }
        catch (Exception ex)
        {
            Log.Warn($"[Api.Inventory] Eject failed: {Log.ExceptionText(ex)}");
        }
        return ejected;
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private static bool Matches(object card, string uidOrTag)
    {
        if (uidOrTag.StartsWith("tag_", StringComparison.Ordinal))
        {
            var model = CardUtil.GetCardData(card);
            if (model == null) return false;
            return Reflect.GetMember(model, "CardTags") is IList tags && HasTagByName(tags, uidOrTag);
        }
        return string.Equals(CardUtil.GetCardUniqueId(card), uidOrTag, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasTagByName(IList tags, string tagName)
    {
        foreach (var t in tags)
            if (t is UnityEngine.Object uo && uo.name == tagName)
                return true;
        return false;
    }

    private static void TrySetActive(object card, bool active)
    {
        try
        {
            if (card is Component comp && comp != null && comp.gameObject != null)
                comp.gameObject.SetActive(active);
        }
        catch (Exception ex) { Log.Debug($"[Api.Inventory] TrySetActive failed: {ex.GetType().Name} {ex.Message}"); }
    }
}
