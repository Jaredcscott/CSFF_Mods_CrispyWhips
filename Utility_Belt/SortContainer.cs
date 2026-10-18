using System.Linq;
using UnityEngine;

namespace Utility_Belt;

/// <summary>The three steps of a sort cycle, shared by B2 (an open container) and B7 (the hand and base rows).</summary>
internal enum SortMode
{
    None = 0,
    Freshness = 1,
    Quality = 2,
}

/// <summary>
/// [Hotkeys] Sort Container (plan B2). With a container's inventory popup open, each press cycles its order:
/// freshest first, then quality high to low, then back to the order it had before the first press. Where a
/// container is in that cycle is remembered per container for the rest of the session (plan B10), so closing
/// a chest, opening another and coming back carries on from the step it was at.
/// </summary>
/// <remarks>
/// Only the open container's own CardsInInventory list is reordered, and the cards inside each pile: no card
/// changes container, so no CannotBeTransferred check applies (CLAUDE.md "Programmatic Card Movement").
/// Each card's CurrentSlotInfo.SlotIndex is set to its new slot, then InspectionPopup.SortInventory redraws:
/// it runs SetupInventory, whose InGameCardBase.SortInventory keeps slot order (it only merges same-kind piles
/// and keeps "expanded" piling-exception slots of one kind next to each other, so this sort ranks such slots
/// as one group), then assigns every card to the slot its SlotIndex names.
/// A slot holding any card that LockApi.IsLocked reports locked keeps its index and its pile order; the other
/// slots fill the remaining indices in sorted order.
/// Skipped (the key does nothing): a mini inventory (CardData.IsMiniInventory), a legacy fixed-slot inventory
/// (its slot positions can be meaningful, and SortInventory pads and trims it), a cooking container
/// (CookingCards hold slot indices), a container with a saved recipe, and one with sorting prevented.
/// "Freshest" is the spoilage bar's fraction (CurrentSpoilagePercent); "quality" is the first special stat
/// whose name reads Quality (vanilla foods use Special 2, metal parts Special 3), as a fraction of its maximum.
/// Cards without that stat go last, in their current order.
/// The per-container memory (B10) is session state only: it is never saved, it is cleared when the
/// GraphicsManager instance changes (a new run or a load), and a container that is destroyed is dropped.
/// </remarks>
internal static class SortContainer
{
    /// <summary>Where one container is in its cycle, and the order to go back to.</summary>
    private sealed class State
    {
        internal SortMode LastMode;
        internal readonly Dictionary<InGameCardBase, int> Original = new();
    }

    private static readonly HashSet<string> _warned = new();
    private static readonly Dictionary<CardData, int> _qualityStat = new();

    private static GraphicsManager _gm;
    private static readonly Dictionary<InGameCardBase, State> _states = new();

    internal static void Tick()
    {
        var key = Plugin.SortContainerKey.Value;
        if (key == KeyCode.None || !Input.GetKeyDown(key) || GraphicsManager.PlayerIsTyping)
        {
            return;
        }

        try
        {
            Press();
        }
        catch (Exception ex)
        {
            WarnOnce("exception:" + ex.GetType().Name, $"[UB] Sort Container failed: {ex}");
        }
    }

    private static void Press()
    {
        var gm = MBSingleton<GraphicsManager>.Instance;
        if (gm == null)
        {
            return; // main menu
        }

        if (gm != _gm)
        {
            // A new scene (a new run or a load): every remembered container belongs to the old one.
            _gm = gm;
            _states.Clear();
        }

        var popup = gm.InventoryInspectionPopup;
        if (popup == null)
        {
            WarnOnce("popup-null", "[UB] Sort Container: GraphicsManager.InventoryInspectionPopup is null; the key does nothing.");
            return;
        }

        if (!popup.gameObject.activeInHierarchy)
        {
            Plugin.Logger.LogDebug("[UB] Sort Container: no container is open.");
            return;
        }

        var card = popup.CurrentCard;
        if (card == null || card.CardModel == null || card.CardsInInventory == null)
        {
            return;
        }

        if (!Sortable(card, out var why))
        {
            Plugin.Logger.LogDebug($"[UB] Sort Container: {card.CardModel.name} is not sorted ({why}).");
            return;
        }

        PruneDestroyed();
        if (!_states.TryGetValue(card, out var state))
        {
            state = new State();
            _states[card] = state;
        }

        // Normalise first, exactly as every popup open does, so piles are merged before ranking.
        card.SortInventory();

        var next = NextMode(state.LastMode);
        if (state.LastMode == SortMode.None)
        {
            RememberOriginal(card, state.Original);
        }

        Apply(card, next, state.Original);
        state.LastMode = next;
        if (next == SortMode.None)
        {
            state.Original.Clear(); // the next press starts a new cycle from the order shown now
        }

        popup.SortInventory();
        Plugin.Logger.LogDebug($"[UB] Sort Container: {card.CardModel.name} sorted by {(next == SortMode.None ? "its original order" : next.ToString())}.");
    }

    /// <summary>Drops containers that no longer exist, and cards inside a remembered order that no longer exist.</summary>
    private static void PruneDestroyed()
    {
        List<InGameCardBase> dead = null;
        foreach (var kv in _states)
        {
            if (IsGone(kv.Key))
            {
                (dead ??= new List<InGameCardBase>()).Add(kv.Key);
            }
        }

        if (dead != null)
        {
            foreach (var k in dead)
            {
                _states.Remove(k);
            }
        }
    }

    /// <summary>True for a destroyed Unity object (C#-non-null but Unity-null) or a card the game marked Destroyed.</summary>
    internal static bool IsGone(InGameCardBase card)
    {
        return card is UnityEngine.Object uo && uo == null || (object)card == null || card.Destroyed;
    }

    /// <summary>The step after <paramref name="last"/>: original, then freshest, then quality, then back.</summary>
    internal static SortMode NextMode(SortMode last) => last switch
    {
        SortMode.None => SortMode.Freshness,
        SortMode.Freshness => SortMode.Quality,
        _ => SortMode.None,
    };

    private static bool Sortable(InGameCardBase card, out string why)
    {
        var model = card.CardModel;
        if (model.IsMiniInventory)
        {
            why = "mini inventory";
            return false;
        }

        if (card.IsLegacyInventory)
        {
            why = "fixed-slot inventory";
            return false;
        }

        if (model.CanCook)
        {
            why = "cooking container";
            return false;
        }

        if (card.CurrentSavedRecipe != null && card.CurrentSavedRecipe.Valid)
        {
            why = "saved recipe";
            return false;
        }

        if (card.PreventInventorySorting || card.IsBlueprintContainer || model.CardType == CardTypes.Blueprint || model.CardType == CardTypes.Explorable
            || model.CardType == CardTypes.EnvImprovement || model.CardType == CardTypes.EnvDamage || model.CardType == CardTypes.BlueprintInLocation)
        {
            why = "not a plain container";
            return false;
        }

        why = null;
        return true;
    }

    private static void RememberOriginal(InGameCardBase container, Dictionary<InGameCardBase, int> original)
    {
        original.Clear();
        var n = 0;
        foreach (var slot in container.CardsInInventory)
        {
            if (slot?.AllCards == null)
            {
                continue;
            }

            foreach (var c in slot.AllCards)
            {
                if (c != null && !original.ContainsKey(c))
                {
                    original[c] = n++;
                }
            }
        }
    }

    /// <summary>
    /// One card's sort key under <paramref name="mode"/>; larger sorts first, float.NegativeInfinity = no value
    /// (goes last). In <see cref="SortMode.None"/> the key is the card's place in <paramref name="original"/>.
    /// Shared by B2 and B7.
    /// </summary>
    internal static float CardKey(InGameCardBase c, SortMode mode, Dictionary<InGameCardBase, int> original)
    {
        if (c == null || c.CardModel == null)
        {
            return float.NegativeInfinity;
        }

        switch (mode)
        {
            case SortMode.Freshness:
                return c.CardModel.SpoilageTime && c.CardModel.SpoilageTime.Max > 0f
                    ? c.CurrentSpoilagePercent
                    : float.NegativeInfinity;
            case SortMode.Quality:
                return QualityFraction(c);
            default:
                return original != null && original.TryGetValue(c, out var i) ? -i : float.NegativeInfinity;
        }
    }

    /// <summary>
    /// The ranking B2 and B7 share: by group key (a kind ranked as one group), then by own key, both high to
    /// low, then by the current index, so equal keys keep their current order.
    /// </summary>
    internal static List<T> Rank<T>(IEnumerable<T> items, Func<T, float> groupKey, Func<T, float> key, Func<T, int> index)
    {
        return items.OrderByDescending(groupKey).ThenByDescending(key).ThenBy(index).ToList();
    }

    private static float QualityFraction(InGameCardBase c)
    {
        var model = c.CardModel;
        if (!_qualityStat.TryGetValue(model, out var idx))
        {
            idx = 0;
            for (var i = 1; i <= 4 && idx == 0; i++)
            {
                var stat = Stat(model, i);
                if (stat && IsQualityName(stat.CardStatName))
                {
                    idx = i;
                }
            }

            _qualityStat[model] = idx;
        }

        if (idx == 0)
        {
            return float.NegativeInfinity;
        }

        var s = Stat(model, idx);
        var value = idx switch
        {
            1 => c.CurrentSpecial1,
            2 => c.CurrentSpecial2,
            3 => c.CurrentSpecial3,
            _ => c.CurrentSpecial4,
        };
        return s.MaxValue > 0f ? value / s.MaxValue : value;
    }

    private static DurabilityStat Stat(CardData model, int i) => i switch
    {
        1 => model.SpecialDurability1,
        2 => model.SpecialDurability2,
        3 => model.SpecialDurability3,
        _ => model.SpecialDurability4,
    };

    private static bool IsQualityName(LocalizedString name)
    {
        var t = name.DefaultText;
        if (string.IsNullOrEmpty(t))
        {
            return false;
        }

        // English DefaultText, or the Chinese a translation plugin may have written over it.
        return t.IndexOf("Quality", StringComparison.OrdinalIgnoreCase) >= 0 || t.Contains("品质");
    }

    private sealed class Unit
    {
        public int Index;
        public InventorySlot Slot;
        public bool Locked;
        public float Key;
        public float GroupKey;
    }

    private static void Apply(InGameCardBase container, SortMode mode, Dictionary<InGameCardBase, int> original)
    {
        var slots = container.CardsInInventory;
        var units = new List<Unit>(slots.Count);
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var u = new Unit { Index = i, Slot = slot, Key = float.NegativeInfinity };
            if (slot?.AllCards != null && slot.AllCards.Count > 0)
            {
                foreach (var c in slot.AllCards)
                {
                    if (LockApi.IsLocked(c))
                    {
                        u.Locked = true;
                    }
                }

                if (!u.Locked)
                {
                    // Inside the pile: by the same key, stable on the current order.
                    var ordered = slot.AllCards
                        .Select((c, n) => (c, n, k: CardKey(c, mode, original)))
                        .OrderByDescending(t => t.k)
                        .ThenBy(t => t.n)
                        .Select(t => t.c)
                        .ToList();
                    slot.AllCards.Clear();
                    slot.AllCards.AddRange(ordered);
                }

                foreach (var c in slot.AllCards)
                {
                    u.Key = Mathf.Max(u.Key, CardKey(c, mode, original));
                }
            }

            u.GroupKey = u.Key;
            units.Add(u);
        }

        // Piling-exception slots of one kind stay together in SortInventory, so rank each such kind as a
        // group by its best member.
        var grm = MBSingleton<GraphicsManager>.Instance;
        if (grm != null)
        {
            var groups = new Dictionary<(CardData, CardData), float>();
            var members = new List<(Unit, (CardData, CardData))>();
            foreach (var u in units)
            {
                if (u.Slot == null || u.Slot.IsFree || !grm.IsPilingException(u.Slot.CardModel, u.Slot.LiquidModel, SlotsTypes.Inventory))
                {
                    continue;
                }

                var g = (u.Slot.CardModel, u.Slot.LiquidModel);
                groups[g] = groups.TryGetValue(g, out var best) ? Mathf.Max(best, u.Key) : u.Key;
                members.Add((u, g));
            }

            foreach (var (u, g) in members)
            {
                u.GroupKey = groups[g];
            }
        }

        var movable = Rank(units.Where(u => !u.Locked), u => u.GroupKey, u => u.Key, u => u.Index);

        var result = new InventorySlot[slots.Count];
        foreach (var u in units)
        {
            if (u.Locked)
            {
                result[u.Index] = u.Slot;
            }
        }

        var next = 0;
        for (var i = 0; i < result.Length; i++)
        {
            if (result[i] == null)
            {
                result[i] = movable[next++].Slot;
            }
        }

        for (var i = 0; i < result.Length; i++)
        {
            slots[i] = result[i];
            if (result[i]?.AllCards == null)
            {
                continue;
            }

            foreach (var c in result[i].AllCards)
            {
                if (c != null && c.CurrentSlotInfo != null)
                {
                    c.CurrentSlotInfo.SlotIndex = i;
                }
            }
        }
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning(message);
        }
    }
}
