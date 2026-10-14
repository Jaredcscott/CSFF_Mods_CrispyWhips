using UnityEngine;

namespace Utility_Belt;

/// <summary>How one row of cards is drawn (the Card Size options).</summary>
internal enum CardRowLayout
{
    /// <summary>The game's own layout, including its zoom button where the row has one.</summary>
    Normal,
    /// <summary>One row of cards drawn smaller (Smaller Size), so more fit across the screen.</summary>
    Smaller,
    /// <summary>Two rows of half-size cards.</summary>
    TwoRows,
    /// <summary>Normal, switching to two rows while the row holds more cards than Crowded Above.</summary>
    TwoRowsWhenCrowded,
}

/// <summary>
/// [Card Size] Per-row card size and row count, the in-house replacement for Pikachu's Card Size Reduce
/// (which needed ModCore). Two rows uses the game's OWN multi-row layout (DynamicViewLayoutGroup.SetAddedLines,
/// the path its zoom button drives through ZoomLevelCardLine), so slot placement, scrolling and drag targeting
/// stay vanilla code. Smaller scales the row's parent RectTransform and widens it by the same factor so the
/// row still spans the screen. Everything is display only: no card, slot or save data changes, and a row set
/// back to Normal is restored to the exact scale and size it had.
/// </summary>
/// <remarks>
/// Ticked every frame from Plugin.Update, so option changes apply within a frame. A row the zoom button also
/// drives is never written here (the button would overwrite it every frame); Patcher.CardSizePatch takes that
/// write over instead, and only while this class holds an override for one of its rows.
/// All of it stays off while Card Size Reduce itself is loaded, so the two can never scale one row twice.
/// </remarks>
internal static class CardSize
{
    internal enum Row
    {
        Location,
        Base,
        Hand,
        Blueprint,
        Container,
        Equipment,
        TradeInventory,
        TradeOffer,
    }

    internal static readonly string[] RowKeys =
    {
        "Location Row", "Base Row", "Hand Row", "Blueprint Row", "Container Row", "Equipment Row", "Trade Inventory Row", "Trade Offer Row",
    };

    internal static readonly string[] RowDescriptions =
    {
        "The top row: the place you are in and what stands there (trees, structures, water).",
        "The middle row: cards lying on the ground where you are.",
        "Your item bar at the bottom of the screen.",
        "The blueprint row while the blueprint screen is open.",
        "The cards inside an open container (a chest, a bag, a shelf).",
        "The equipment row in the character window.",
        "The trade window's inventory row.",
        "The trade window's offer row.",
    };

    internal const string CardSizeReduceGuid = "Pikachu.CSFF.CardSizeReduce";

    private sealed class ScaledParent
    {
        public RectTransform Target;
        public Vector3 Scale;
        public Vector2 SizeDelta;
        public float Width;
    }

    // Per GraphicsManager: every entry belongs to the current scene's lines and is dropped when it changes.
    private static GraphicsManager _graphics;
    private static readonly Dictionary<CardLine, ScaledParent> Scaled = new();
    private static readonly HashSet<CardLine> RowsWritten = new();

    /// <summary>Rows that vanilla's zoom button drives; filled by Patcher.CardSizePatch.</summary>
    internal static readonly HashSet<CardLine> ZoomTargets = new();

    /// <summary>The added-line count this class wants on a row, for the rows that have an override.</summary>
    internal static readonly Dictionary<CardLine, int> AddedLineOverrides = new();

    private static readonly AccessTools.FieldRef<DynamicViewLayoutGroup, bool> RecalculateSizeRef = ResolveRecalculateSize();

    private static bool? _otherModOwnsIt;

    /// <summary>
    /// True while Card Size Reduce is loaded. Chainloader.PluginInfos is complete only after every plugin's
    /// Awake, so this is asked lazily (first Tick, first run start), never from Awake.
    /// </summary>
    internal static bool OtherModOwnsIt
    {
        get
        {
            if (_otherModOwnsIt == null)
            {
                _otherModOwnsIt = Chainloader.PluginInfos.ContainsKey(CardSizeReduceGuid);
                if (_otherModOwnsIt.Value && AnyOptionSet())
                {
                    Plugin.Logger.LogWarning("[UB] Card Size Reduce is installed, so Utility Belt's Card Size, Extra Hand Slots and Extra Carry Weight options stay off. Remove Card Size Reduce to use them.");
                }
            }

            return _otherModOwnsIt.Value;
        }
    }

    internal static void Tick()
    {
        if (OtherModOwnsIt)
        {
            return;
        }

        var graphics = MBSingleton<GraphicsManager>.Instance;
        if (!graphics)
        {
            return;
        }

        if (graphics != _graphics)
        {
            // A new scene: the old lines are gone with it, so nothing is restored, only forgotten.
            _graphics = graphics;
            Scaled.Clear();
            RowsWritten.Clear();
            ZoomTargets.Clear();
            AddedLineOverrides.Clear();
        }

        for (var i = 0; i < RowKeys.Length; i++)
        {
            var line = Resolve(graphics, (Row)i);
            if (!line)
            {
                continue;
            }

            Apply(line, Plugin.CardRowLayouts[i].Value);
        }
    }

    private static void Apply(CardLine line, CardRowLayout layout)
    {
        int? addedLines = layout switch
        {
            CardRowLayout.Smaller => 0,
            CardRowLayout.TwoRows => 1,
            CardRowLayout.TwoRowsWhenCrowded => OccupiedSlots(line) > Plugin.CrowdedAbove.Value ? 1 : 0,
            _ => null,
        };

        if (addedLines.HasValue)
        {
            AddedLineOverrides[line] = addedLines.Value;
            if (!ZoomTargets.Contains(line))
            {
                line.SetAddedLines(addedLines.Value);
            }

            RowsWritten.Add(line);
        }
        else
        {
            AddedLineOverrides.Remove(line);
            // Back to Normal: undo what this class wrote, once. A zoom-button row gets the game's own value
            // back from the button on its next frame.
            if (RowsWritten.Remove(line) && !ZoomTargets.Contains(line))
            {
                line.SetAddedLines(0);
            }
        }

        if (layout == CardRowLayout.Smaller)
        {
            ScaleParent(line, Mathf.Clamp(Plugin.SmallerSize.Value, 0.4f, 0.95f));
        }
        else if (Scaled.TryGetValue(line, out var scaled))
        {
            if (scaled.Target)
            {
                scaled.Target.localScale = scaled.Scale;
                scaled.Target.sizeDelta = scaled.SizeDelta;
            }

            Scaled.Remove(line);
            MarkForRelayout(line);
        }
    }

    private static void ScaleParent(CardLine line, float factor)
    {
        if (!Scaled.TryGetValue(line, out var scaled))
        {
            if (!(line.transform.parent is RectTransform parent))
            {
                return;
            }

            scaled = new ScaledParent { Target = parent, Scale = parent.localScale, SizeDelta = parent.sizeDelta, Width = parent.rect.width };
            Scaled[line] = scaled;
        }

        if (!scaled.Target)
        {
            Scaled.Remove(line);
            return;
        }

        var wantScale = new Vector3(scaled.Scale.x * factor, scaled.Scale.y * factor, scaled.Scale.z);
        // Re-checked every frame rather than applied once: a tween or the game re-laying the window out can
        // put the parent back, and the row then has to follow.
        if (Mathf.Approximately(scaled.Target.localScale.x, wantScale.x) && Mathf.Approximately(scaled.Target.localScale.y, wantScale.y))
        {
            return;
        }

        scaled.Target.localScale = wantScale;
        // Widen by the inverse factor so the shrunk row still spans the width it had (stretch or fixed anchors alike).
        scaled.Target.sizeDelta = new Vector2(scaled.SizeDelta.x + scaled.Width * (1f / factor - 1f), scaled.SizeDelta.y);
        MarkForRelayout(line);
    }

    private static void MarkForRelayout(CardLine line)
    {
        if (RecalculateSizeRef != null)
        {
            RecalculateSizeRef(line) = true;
        }
    }

    private static int OccupiedSlots(CardLine line)
    {
        var slots = line.Slots;
        if (slots == null)
        {
            return 0;
        }

        var count = 0;
        for (var i = 0; i < slots.Count; i++)
        {
            if (slots[i] != null && slots[i].AssignedCard != null)
            {
                count++;
            }
        }

        return count;
    }

    private static CardLine Resolve(GraphicsManager graphics, Row row)
    {
        switch (row)
        {
            case Row.Location: return graphics.LocationSlotsLine;
            case Row.Base: return graphics.BaseSlotsLine;
            case Row.Hand: return graphics.ItemSlotsLine;
            case Row.Blueprint: return graphics.BlueprintSlotsLine;
            case Row.Container: return graphics.InventoryInspectionPopup ? graphics.InventoryInspectionPopup.InventorySlotsLine : null;
            case Row.Equipment: return graphics.CharacterWindow ? graphics.CharacterWindow.EquipmentSlotsLine : null;
            case Row.TradeInventory: return graphics.NPCPopup && graphics.NPCPopup.TradingWindow ? graphics.NPCPopup.TradingWindow.InventorySlotsLine : null;
            case Row.TradeOffer: return graphics.NPCPopup && graphics.NPCPopup.TradingWindow ? graphics.NPCPopup.TradingWindow.TradingLine : null;
            default: return null;
        }
    }

    private static bool AnyOptionSet()
    {
        foreach (var entry in Plugin.CardRowLayouts)
        {
            if (entry.Value != CardRowLayout.Normal)
            {
                return true;
            }
        }

        return Plugin.ExtraHandSlots.Value != 0 || Plugin.ExtraCarryWeight.Value != 0;
    }

    private static AccessTools.FieldRef<DynamicViewLayoutGroup, bool> ResolveRecalculateSize()
    {
        try
        {
            return AccessTools.FieldRefAccess<DynamicViewLayoutGroup, bool>("RecalculateSize");
        }
        catch (Exception ex)
        {
            // Only costs a frame's delay: SetAddedLines marks the row itself, and a scaled row re-lays out on
            // the game's next size change. Said once so a rename is visible.
            Plugin.Logger.LogWarning($"[UB] DynamicViewLayoutGroup.RecalculateSize not found ({ex.Message}); a Smaller row may re-lay out a moment late.");
            return null;
        }
    }
}
