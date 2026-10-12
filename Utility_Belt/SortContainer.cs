using System.Linq;
using UnityEngine;

namespace Utility_Belt;

/// <summary>
/// [Hotkeys] Sort Container (plan B2). With a container's inventory popup open, each press cycles its order:
/// freshest first, then quality high to low, then back to the order it had before the first press. The order
/// to go back to is remembered for that container until its popup closes or another card is opened.
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
/// </remarks>
internal static class SortContainer
{
    private enum Mode
    {
        None = 0,
        Freshness = 1,
        Quality = 2,
    }

    private static readonly HashSet<string> _warned = new();
    private static readonly Dictionary<CardData, int> _qualityStat = new();

    private static GraphicsManager _gm;
    private static InGameCardBase _container;
    private static Mode _lastMode;
    private static readonly Dictionary<InGameCardBase, int> _original = new();

    internal static void Tick()
    {
        if ((object)_container != null) // also a destroyed one, so it is forgotten
        {
            ForgetIfClosed();
        }

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

    private static void ForgetIfClosed()
    {
        var popup = _gm != null ? _gm.InventoryInspectionPopup : null;
        if (_container == null || popup == null || !popup.gameObject.activeInHierarchy || popup.CurrentCard != _container)
        {
            Forget();
        }
    }

    private static void Forget()
    {
        _container = null;
        _lastMode = Mode.None;
        _original.Clear();
    }

    private static void Press()
    {
        var gm = MBSingleton<GraphicsManager>.Instance;
        if (gm == null)
        {
            return; // main menu
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

        _gm = gm;
        if (card != _container)
        {
            Forget();
            _container = card;
        }

        // Normalise first, exactly as every popup open does, so piles are merged before ranking.
        card.SortInventory();

        Mode next;
        switch (_lastMode)
        {
            case Mode.None:
                RememberOriginal(card);
                next = Mode.Freshness;
                break;
            case Mode.Freshness:
                next = Mode.Quality;
                break;
            default:
                next = Mode.None; // back to the remembered order
                break;
        }

        Apply(card, next);
        _lastMode = next;
        if (next == Mode.None)
        {
            _original.Clear(); // the next press starts a new cycle from the order shown now
        }

        popup.SortInventory();
        Plugin.Logger.LogDebug($"[UB] Sort Container: {card.CardModel.name} sorted by {(next == Mode.None ? "its original order" : next.ToString())}.");
    }

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

    private static void RememberOriginal(InGameCardBase container)
    {
        _original.Clear();
        var n = 0;
        foreach (var slot in container.CardsInInventory)
        {
            if (slot?.AllCards == null)
            {
                continue;
            }

            foreach (var c in slot.AllCards)
            {
                if (c != null && !_original.ContainsKey(c))
                {
                    _original[c] = n++;
                }
            }
        }
    }

    /// <summary>Larger sorts first. float.NegativeInfinity = no value (goes last).</summary>
    private static float CardKey(InGameCardBase c, Mode mode)
    {
        if (c == null || c.CardModel == null)
        {
            return float.NegativeInfinity;
        }

        switch (mode)
        {
            case Mode.Freshness:
                return c.CardModel.SpoilageTime && c.CardModel.SpoilageTime.Max > 0f
                    ? c.CurrentSpoilagePercent
                    : float.NegativeInfinity;
            case Mode.Quality:
                return QualityFraction(c);
            default:
                return _original.TryGetValue(c, out var i) ? -i : float.NegativeInfinity;
        }
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

    private static void Apply(InGameCardBase container, Mode mode)
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
                        .Select((c, n) => (c, n, k: CardKey(c, mode)))
                        .OrderByDescending(t => t.k)
                        .ThenBy(t => t.n)
                        .Select(t => t.c)
                        .ToList();
                    slot.AllCards.Clear();
                    slot.AllCards.AddRange(ordered);
                }

                foreach (var c in slot.AllCards)
                {
                    u.Key = Mathf.Max(u.Key, CardKey(c, mode));
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

        var movable = units.Where(u => !u.Locked)
            .OrderByDescending(u => u.GroupKey)
            .ThenByDescending(u => u.Key)
            .ThenBy(u => u.Index)
            .ToList();

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
