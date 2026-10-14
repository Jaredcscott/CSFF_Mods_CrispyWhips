namespace Utility_Belt.Patcher;

/// <summary>
/// [Card Size] Two hooks for CardSize and Capacity.
/// (1) A prefix on ZoomLevelCardLine.Update, the game's zoom button that writes an added-line count onto its
/// target rows EVERY frame. While Utility Belt holds an override for one of those rows, the prefix does the
/// button's loop itself with the override in place of the game option and skips the original; with no
/// override it only records the button's rows and lets the original run, so the zoom button is untouched
/// whenever every Card Size option is Normal.
/// (2) A prefix on GraphicsManager.Init, which raises MaxItemSlots before Init builds the hand bar.
/// (3) A prefix and postfix on DynamicViewLayoutGroup.CalculateSizeAndProperties correcting vanilla's
/// multi-row length for a row with hidden slots, on Utility Belt's two-row rows only (see CalculatePrefix).
/// </summary>
/// <remarks>
/// Both targets are callers with loops, not small leaf methods, so Mono cannot have inlined them into an
/// already-compiled caller (CLAUDE.md "Harmony Patching Pitfalls"). SetAddedLines itself is deliberately
/// not patched: it is a three-line setter, exactly the shape that gets inlined.
/// </remarks>
internal static class CardSizePatch
{
    internal static void ApplyPatch(Harmony harmony)
    {
        var zoomUpdate = AccessTools.Method(typeof(ZoomLevelCardLine), "Update");
        if (zoomUpdate == null)
        {
            Plugin.Logger.LogWarning("[UB] ZoomLevelCardLine.Update not found; Card Size options on the zoom button's rows may be overwritten by it.");
        }
        else
        {
            harmony.Patch(zoomUpdate, prefix: new HarmonyMethod(typeof(CardSizePatch), nameof(ZoomUpdatePrefix)));
        }

        var calculate = AccessTools.Method(typeof(DynamicViewLayoutGroup), "CalculateSizeAndProperties");
        if (calculate == null || InactiveRef == null || AddedLinesRef == null)
        {
            Plugin.Logger.LogWarning("[UB] DynamicViewLayoutGroup size members not found; a TwoRows row with hidden slots (the blueprint row) may draw empty.");
        }
        else
        {
            harmony.Patch(
                calculate,
                prefix: new HarmonyMethod(typeof(CardSizePatch), nameof(CalculatePrefix)),
                postfix: new HarmonyMethod(typeof(CardSizePatch), nameof(CalculatePostfix)));
        }

        var init = AccessTools.Method(typeof(GraphicsManager), "Init");
        if (init == null)
        {
            Plugin.Logger.LogWarning("[UB] GraphicsManager.Init not found; Extra Hand Slots stays off.");
        }
        else
        {
            harmony.Patch(init, prefix: new HarmonyMethod(typeof(CardSizePatch), nameof(GraphicsInitPrefix)));
        }
    }

    private static bool ZoomUpdatePrefix(CardLine[] ___TargetLines)
    {
        if (___TargetLines == null || ___TargetLines.Length == 0)
        {
            return true;
        }

        var holdsOverride = false;
        foreach (var line in ___TargetLines)
        {
            if (line)
            {
                CardSize.ZoomTargets.Add(line);
                holdsOverride |= CardSize.AddedLineOverrides.ContainsKey(line);
            }
        }

        var options = GameLoad.GetGameOptions;
        if (!holdsOverride || !options)
        {
            return true;
        }

        foreach (var line in ___TargetLines)
        {
            if (line)
            {
                line.SetAddedLines(CardSize.AddedLineOverrides.TryGetValue(line, out var lines) ? lines : options.CardsExtraLine);
            }
        }

        return false;
    }

    private static readonly AccessTools.FieldRef<DynamicViewLayoutGroup, int> InactiveRef = FieldRefOrNull("InactiveElements");
    private static readonly AccessTools.FieldRef<DynamicViewLayoutGroup, int> AddedLinesRef = FieldRefOrNull("AddedLines");

    /// <summary>
    /// Vanilla's CalculateSizeAndProperties sizes a multi-row line as Spacing * ceil(Count * scale) minus
    /// Spacing * InactiveElements: the total is divided across the rows, the hidden count is not. A line with
    /// many hidden slots (the blueprint row keeps every tab's blueprints and hides all but one tab) then gets a
    /// negative length and its origin lands off screen, so the row draws empty (measured EA 0.69c: 66 elements,
    /// 65 inactive). For a line Utility Belt holds two rows on, this swaps in the hidden count that makes the
    /// game's own formula equal Spacing * ceil(active * scale), and the postfix puts the real count back.
    /// Nothing else in the method or the UpdateList it ends with reads InactiveElements.
    /// </summary>
    private static void CalculatePrefix(DynamicViewLayoutGroup __instance, out int __state)
    {
        __state = -1;
        if (!(__instance is CardLine line) || !CardSize.AddedLineOverrides.TryGetValue(line, out var lines) || lines <= 0)
        {
            return;
        }

        var addedLines = AddedLinesRef(__instance);
        if (addedLines <= 0)
        {
            return;
        }

        var inactive = InactiveRef(__instance);
        if (inactive <= 0)
        {
            return;
        }

        var scale = 1f / (addedLines + 1);
        var count = __instance.Count;
        __state = inactive;
        InactiveRef(__instance) = UnityEngine.Mathf.CeilToInt(count * scale) - UnityEngine.Mathf.CeilToInt((count - inactive) * scale);
    }

    private static void CalculatePostfix(DynamicViewLayoutGroup __instance, int __state)
    {
        if (__state >= 0)
        {
            InactiveRef(__instance) = __state;
        }
    }

    private static AccessTools.FieldRef<DynamicViewLayoutGroup, int> FieldRefOrNull(string name)
    {
        try
        {
            return AccessTools.FieldRefAccess<DynamicViewLayoutGroup, int>(name);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[UB] DynamicViewLayoutGroup.{name} not found ({ex.Message}).");
            return null;
        }
    }

    private static void GraphicsInitPrefix(GraphicsManager __instance)
    {
        Capacity.BeforeGraphicsInit(__instance);
    }
}
