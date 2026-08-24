using System;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Util;
using HarmonyLib;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// Blocks Wolf/Fox/Owl companions from being stored inside any container's inventory.
///
/// `CannotBeTransferred: true` on the companion CardData only gates vanilla LIQUID
/// transfer (InGameCardBase.CanTransferLiquids) — it is never read by the solid-item
/// drag-and-drop or right-click "quick move" paths. Both of those converge on
/// InGameCardBase.CanReceiveInInventoryInstance(InGameCardBase), called on the
/// RECEIVING container with the moved card as the argument — the single choke point
/// for "can this card go into this container's inventory," used by both
/// DynamicLayoutSlot.CanReceiveCard (drag-and-drop) and GraphicsManager.CanFindSlot
/// (right-click quick move). Patching there closes both paths without touching every
/// SlotsTypes-specific caller. See Documentation/Retrospectives/companion-container-prevention.md
/// — the prior fix only stopped QuickTransfer's own repeat-click automation from
/// padding extra moves; it never blocked the underlying single move itself.
/// </summary>
internal static class CompanionContainerGuardPatch
{
    private static readonly string[] ProtectedUids =
    {
        WolfTickPatch.WolfId,
        WolfTickPatch.FoxId,
        WolfTickPatch.OwlId,
    };

    public static void ApplyPatch(Harmony harmony)
    {
        var cardBaseType = AccessTools.TypeByName("InGameCardBase");
        if (cardBaseType == null)
        {
            Plugin.Logger?.LogError("[CompanionContainerGuard] InGameCardBase type not found");
            return;
        }

        var method = cardBaseType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "CanReceiveInInventoryInstance" && m.GetParameters().Length == 1);
        if (method == null)
        {
            Plugin.Logger?.LogError("[CompanionContainerGuard] CanReceiveInInventoryInstance not found");
            return;
        }

        harmony.Patch(method, postfix: new HarmonyMethod(typeof(CompanionContainerGuardPatch), nameof(Postfix)));
    }

    private static void Postfix(object _Card, ref bool __result)
    {
        if (!__result) return;
        try
        {
            string uid = CardUtil.GetCardUniqueId(_Card);
            if (uid != null && ProtectedUids.Contains(uid))
                __result = false;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogError($"[CompanionContainerGuard] postfix error: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }
}
