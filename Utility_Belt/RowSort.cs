using UnityEngine;

namespace Utility_Belt;

/// <summary>
/// [Hotkeys] Sort Hand Row and Sort Base Row (plan B7). Each press cycles the order of the always-visible
/// item bar (GraphicsManager.ItemSlotsLine) or base row (BaseSlotsLine): freshest first, then quality high
/// to low, then back to the order it had before the first press, with the same ranking as Sort Container
/// (<see cref="SortContainer.Rank{T}"/> and <see cref="SortContainer.CardKey"/>).
/// </summary>
/// <remarks>
/// <para>The rows are CardLines, so the sort goes through the game's own <c>CardLine.SortSlots</c>, then
/// <c>CardLine.RefreshSlotIndices(0)</c>: SortSlots does not refresh indices (AddSlot and MoveSlot do), and the
/// <c>DynamicLayoutSlot.Index</c> setter is what writes SlotIndex onto every card in the slot's pile, which is
/// what the save reads (CardSaveData via CurrentSlot.ToInfo). Skipping it leaves the saved order and the
/// screen order disagreeing. Visibility is then redrawn by the public <c>GraphicsManager.RefreshSlots(false)</c>,
/// which runs RefreshSlotLines and UpdateSlotsVisibility on the next LateUpdate.</para>
/// <para>A slot holding a locked card (LockApi.IsLocked) or a pinned card keeps its index; empty slots go after
/// every filled one (the item bar has a fixed slot count when the game limits it, raised by Extra Hand Slots).
/// A slot's pile is ranked by its best card; the order inside a pile is the game's own and is not changed.</para>
/// <para>Ignored while typing, while an action runs (GameManager.PerformingAction) and while a card is being
/// dragged. Each row remembers where it is in its cycle until the GraphicsManager instance changes.</para>
/// </remarks>
internal static class RowSort
{
    private enum Row
    {
        Hand,
        Base,
    }

    private sealed class State
    {
        internal SortMode LastMode;
        internal readonly Dictionary<InGameCardBase, int> Original = new();
    }

    private static readonly HashSet<string> _warned = new();
    private static readonly State[] _states = { new(), new() };
    private static GraphicsManager _gm;

    internal static void Tick()
    {
        var hand = Plugin.SortHandRowKey.Value;
        var bas = Plugin.SortBaseRowKey.Value;
        Row row;
        if (hand != KeyCode.None && Input.GetKeyDown(hand))
        {
            row = Row.Hand;
        }
        else if (bas != KeyCode.None && Input.GetKeyDown(bas))
        {
            row = Row.Base;
        }
        else
        {
            return;
        }

        if (GraphicsManager.PlayerIsTyping || GameManager.PerformingAction || GameManager.DraggedCard != null)
        {
            return;
        }

        try
        {
            Press(row);
        }
        catch (Exception ex)
        {
            WarnOnce("exception:" + ex.GetType().Name, $"[UB] Sort {row} Row failed: {ex}");
        }
    }

    private static void Press(Row row)
    {
        var gm = MBSingleton<GraphicsManager>.Instance;
        if (gm == null)
        {
            return; // main menu
        }

        if (gm != _gm)
        {
            _gm = gm;
            foreach (var s in _states)
            {
                s.LastMode = SortMode.None;
                s.Original.Clear();
            }
        }

        var line = row == Row.Hand ? gm.ItemSlotsLine : gm.BaseSlotsLine;
        if (line == null || line.Slots == null)
        {
            WarnOnce("line-null:" + row, $"[UB] Sort {row} Row: GraphicsManager.{(row == Row.Hand ? "ItemSlotsLine" : "BaseSlotsLine")} is not set; the key does nothing.");
            return;
        }

        var state = _states[(int)row];
        var next = SortContainer.NextMode(state.LastMode);
        if (state.LastMode == SortMode.None)
        {
            RememberOriginal(line, state.Original);
        }

        var rank = Order(line, next, state.Original);
        line.SortSlots((a, b) => Rank(rank, a).CompareTo(Rank(rank, b)));
        line.RefreshSlotIndices(0);
        gm.RefreshSlots(false);

        state.LastMode = next;
        if (next == SortMode.None)
        {
            state.Original.Clear(); // the next press starts a new cycle from the order shown now
        }

        Plugin.Logger.LogDebug($"[UB] Sort {row} Row: sorted by {(next == SortMode.None ? "its original order" : next.ToString())}.");
    }

    private static int Rank(Dictionary<DynamicLayoutSlot, int> rank, DynamicLayoutSlot slot)
    {
        return slot != null && rank.TryGetValue(slot, out var r) ? r : int.MaxValue;
    }

    private sealed class Unit
    {
        public int Index;
        public DynamicLayoutSlot Slot;
        public bool Fixed;
        public bool Empty;
        public float Key;
    }

    /// <summary>The new position of every slot: fixed slots at their own index, the others in ranked order, empty ones last.</summary>
    internal static Dictionary<DynamicLayoutSlot, int> Order(CardLine line, SortMode mode, Dictionary<InGameCardBase, int> original)
    {
        var slots = line.Slots;
        var units = new List<Unit>(slots.Count);
        var pile = new List<InGameCardBase>();
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var u = new Unit { Index = i, Slot = slot, Key = float.NegativeInfinity };
            pile.Clear();
            slot?.CopyCardPile(pile);
            u.Empty = slot == null || slot.AssignedCard == null;
            if (!u.Empty)
            {
                u.Fixed = slot.PinnedCard != null;
                foreach (var c in pile)
                {
                    if (c == null)
                    {
                        continue;
                    }

                    if (LockApi.IsLocked(c))
                    {
                        u.Fixed = true;
                    }

                    u.Key = Mathf.Max(u.Key, SortContainer.CardKey(c, mode, original));
                }
            }

            units.Add(u);
        }

        var filled = SortContainer.Rank(units.FindAll(u => !u.Fixed && !u.Empty), u => u.Key, u => u.Key, u => u.Index);
        var empties = units.FindAll(u => !u.Fixed && u.Empty);
        var result = new DynamicLayoutSlot[slots.Count];
        foreach (var u in units)
        {
            if (u.Fixed)
            {
                result[u.Index] = u.Slot;
            }
        }

        int f = 0, e = 0;
        for (var i = 0; i < result.Length; i++)
        {
            if (result[i] != null)
            {
                continue;
            }

            result[i] = f < filled.Count ? filled[f++].Slot : empties[e++].Slot;
        }

        var rank = new Dictionary<DynamicLayoutSlot, int>(result.Length);
        for (var i = 0; i < result.Length; i++)
        {
            if (result[i] != null)
            {
                rank[result[i]] = i;
            }
        }

        return rank;
    }

    private static void RememberOriginal(CardLine line, Dictionary<InGameCardBase, int> original)
    {
        original.Clear();
        var pile = new List<InGameCardBase>();
        var n = 0;
        foreach (var slot in line.Slots)
        {
            pile.Clear();
            slot?.CopyCardPile(pile);
            foreach (var c in pile)
            {
                if (c != null && !original.ContainsKey(c))
                {
                    original[c] = n++;
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
