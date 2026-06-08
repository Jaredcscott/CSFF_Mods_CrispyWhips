using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// API for sorting items within a container's inventory slots.
///
/// Pass any InGameCardBase that has an <c>InventorySlots</c> field (e.g. a chest, barrel,
/// or workstation). Slots are reordered in-place; the item counts are not changed.
/// </summary>
public static class ContainerSort
{
    /// <summary>Durability axis to sort by.</summary>
    public enum Axis
    {
        Usage,
        Quality,
        Spoilage,
        Special1,
        Special2,
        Special3,
        Special4
    }

    // ── Convenience wrappers ──────────────────────────────────────────────────

    /// <summary>Sort so highest-usage (most worn) items come first. Useful for consuming damaged items.</summary>
    public static void SortByUsage(object container, bool descending = true) =>
        Sort(container, Axis.Usage, descending);

    /// <summary>Sort so soonest-expiring items come first (ascending = nearest expiry first).</summary>
    public static void SortBySpoilage(object container, bool descending = false) =>
        Sort(container, Axis.Spoilage, descending);

    /// <summary>Sort so highest-quality items come first.</summary>
    public static void SortByQuality(object container, bool descending = true) =>
        Sort(container, Axis.Quality, descending);

    /// <summary>Sort slots by a standard durability axis.</summary>
    public static void Sort(object container, Axis axis, bool descending = true) =>
        SortCustom(container, slot => ReadDurability(slot, axis), descending);

    /// <summary>
    /// Sort slots by a custom key extracted from the first card inside each slot.
    /// <paramref name="keySelector"/> receives an InventorySlot object; use
    /// <see cref="CardUtil.GetMemberValue"/> or <see cref="GetFirstCard"/> to inspect it.
    /// </summary>
    public static void SortCustom(object container, Func<object, float> keySelector, bool descending = true)
    {
        if (container == null || keySelector == null) return;
        try
        {
            var slots = GetSlotsList(container);
            if (slots == null || slots.Count == 0) return;

            var items = new List<object>(slots.Count);
            foreach (var s in slots) items.Add(s);

            items.Sort((a, b) =>
            {
                float ka = TryKey(keySelector, a);
                float kb = TryKey(keySelector, b);
                // Push null/empty slots to the end regardless of direction
                bool emptyA = a == null || GetFirstCard(a) == null;
                bool emptyB = b == null || GetFirstCard(b) == null;
                if (emptyA && emptyB) return 0;
                if (emptyA) return 1;
                if (emptyB) return -1;
                return descending ? kb.CompareTo(ka) : ka.CompareTo(kb);
            });

            // Write sorted order back into the existing list in-place
            for (int i = 0; i < items.Count; i++)
            {
                try { slots[i] = items[i]; }
                catch (NotSupportedException) { return; } // read-only list; give up
            }
        }
        catch (Exception ex) { Log.Debug($"[ContainerSort] sort failed: {ex.GetType().Name} {ex.Message}"); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns the first InGameCardBase inside an InventorySlot, or null if empty.</summary>
    public static object GetFirstCard(object slot)
    {
        if (slot == null) return null;
        foreach (var name in new[] { "AllCards", "ContainedCards", "Cards" })
        {
            var inner = CardUtil.GetMemberValue(slot, name);
            if (inner is IList list && list.Count > 0) return list[0];
        }
        return null;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private static IList GetSlotsList(object container)
    {
        var t = container.GetType();
        foreach (var name in new[] { "InventorySlots", "Slots", "CardSlots" })
        {
            var field = ReflectionHelpers.FindField(t, name);
            if (field == null) continue;
            var val = field.GetValue(container) as IList;
            if (val != null) return val;
        }
        return null;
    }

    private static readonly Dictionary<(Type, Axis), MemberInfo> _durCache = new();

    private static float ReadDurability(object slot, Axis axis)
    {
        var card = GetFirstCard(slot);
        if (card == null) return 0f;

        var t = card.GetType();
        var key = (t, axis);
        if (!_durCache.TryGetValue(key, out var member))
        {
            member = FindDurabilityMember(t, axis);
            _durCache[key] = member;
        }
        if (member == null) return 0f;

        try
        {
            object val = member is PropertyInfo pi ? pi.GetValue(card, null)
                       : member is FieldInfo fi ? fi.GetValue(card)
                       : null;
            return val != null ? Convert.ToSingle(val) : 0f;
        }
        catch { return 0f; }
    }

    private static MemberInfo FindDurabilityMember(Type t, Axis axis)
    {
        string[] candidates = axis switch
        {
            Axis.Usage    => new[] { "UsageDurability" },
            Axis.Quality  => new[] { "QualityDurability" },
            Axis.Spoilage => new[] { "SpoilageTime", "SpoilageTimer", "Spoilage" },
            Axis.Special1 => new[] { "SpecialDurability1", "Special1" },
            Axis.Special2 => new[] { "SpecialDurability2", "Special2" },
            Axis.Special3 => new[] { "SpecialDurability3", "Special3" },
            Axis.Special4 => new[] { "SpecialDurability4", "Special4" },
            _             => Array.Empty<string>()
        };
        foreach (var name in candidates)
        {
            var f = ReflectionHelpers.FindField(t, name);
            if (f != null) return f;
            var p = ReflectionHelpers.FindProperty(t, name);
            if (p != null) return p;
        }
        return null;
    }

    private static float TryKey(Func<object, float> fn, object slot)
    {
        try { return fn(slot); }
        catch { return 0f; }
    }
}
