using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// Backfills a null <c>CardData.InventorySlots</c> on a freshly created <see cref="InGameNPC"/>
/// model card. Vanilla's own <c>InGameNPC.CreateModelCard</c> only assigns
/// <c>_Card.InventorySlots = list.ToArray()</c> when the built list is non-empty
/// (<c>if (list.Count > 0)</c>) — an NPCAgent with a genuinely empty
/// <c>StartingInventory: []</c> (every hand-authored Animal-system agent that carries no starting
/// items) leaves <c>InventorySlots</c> null forever. The very first click on that NPC's board card
/// throws inside <c>InGameCardBase.SortInventory()</c> (<c>CardModel.InventorySlots.Length</c>)
/// BEFORE <c>NPCInspectionPopup.SetupActions()</c> ever runs, so the popup opens empty — no
/// Approach, no Tame, nothing. Confirmed for the Owl (<c>wildowl_agent</c>) 2026-07-11: the
/// `"StartingInventory": []` fix from the prior session satisfied `NPCModel.StartingInventory ==
/// null` but not this deeper guard, so the crash (and empty popup) persisted.
///
/// This postfix only ever sets a null field to an empty array — a strict no-op for every vanilla
/// NPCAgent, since none of them reach this code path with a null InventorySlots (they either have
/// real starting items or the field was already backfilled elsewhere).
/// </summary>
internal static class ModelCardInventoryFix
{
    private static bool _patched;

    public static void ApplyPatch(Harmony harmony)
    {
        if (_patched) return;
        try
        {
            var target = AccessTools.Method(typeof(InGameNPC), nameof(InGameNPC.CreateModelCards));
            if (target == null)
            {
                Log.Error("Animals: InGameNPC.CreateModelCards not found — inventory null-guard unavailable");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(ModelCardInventoryFix), nameof(Postfix)));
            _patched = true;
            Log.Debug("Animals: InGameNPC.CreateModelCards inventory null-guard armed");
        }
        catch (Exception ex)
        {
            Log.Error($"Animals: failed to patch InGameNPC.CreateModelCards: {Log.ExceptionText(ex)}");
        }
    }

    private static void Postfix(InGameNPC __instance)
    {
        if (__instance.ModelCard != null && __instance.ModelCard.InventorySlots == null)
        {
            __instance.ModelCard.InventorySlots = Array.Empty<CardData>();
        }

        if (__instance.CommissionsModelCard != null && __instance.CommissionsModelCard.InventorySlots == null)
        {
            __instance.CommissionsModelCard.InventorySlots = Array.Empty<CardData>();
        }
    }
}
