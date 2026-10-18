namespace Utility_Belt.Patcher;

/// <summary>
/// [Display] Container Filter (plan B12): re-applies the filter after every <c>InspectionPopup.SetupInventory</c>,
/// which turns every slot back on (on open, RefreshInventory and SortInventory).
/// </summary>
/// <remarks>
/// B3's <see cref="InspectionReadoutPatch"/> postfixes the same method. The two write different things (B3 the
/// description text, this one slot IsActive flags), so order does not change either result; this postfix is
/// registered at Priority.Low so it runs after B3's (Priority.Normal) by declaration rather than by patch order.
/// SetupInventory is a large virtual method, never a small inlined helper (CLAUDE.md "Harmony Patching Pitfalls").
/// </remarks>
internal static class ContainerFilterPatch
{
    internal static void ApplyPatch(Harmony harmony)
    {
        var setup = AccessTools.Method(typeof(InspectionPopup), "SetupInventory");
        if (setup == null)
        {
            Plugin.Logger.LogWarning("[UB] InspectionPopup.SetupInventory not found; the container filter is off.");
            return;
        }

        harmony.Patch(setup, postfix: new HarmonyMethod(typeof(ContainerFilterPatch), nameof(SetupInventoryPostfix)) { priority = Priority.Low });
    }

    private static void SetupInventoryPostfix(InspectionPopup __instance)
    {
        try
        {
            ContainerFilter.Apply(__instance);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug($"[UB] Container filter: SetupInventory postfix failed ({ex.GetType().Name}: {ex.Message}).");
        }
    }
}
