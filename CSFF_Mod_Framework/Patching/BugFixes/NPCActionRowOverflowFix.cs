using UnityEngine.UI;

namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// NPCInspectionPopup's Talk/Trade/Commissions button row (DismantleOptionsParent)
/// is only ever exercised at up to 2 active buttons by vanilla NPCs (Trade XOR
/// Commissions). The Professor (Community_Mod_Chest) is CanTrade AND
/// HasVisibleCommissions simultaneously, producing a 3rd button in a row whose
/// HorizontalLayoutGroup (if childControlWidth/childForceExpandWidth are off, as
/// authored for the 2-button case) doesn't shrink children to fit — the
/// Commissions button spills past the popup's visible panel. Scoped to the exact
/// CanTrade && HasVisibleCommissions case so no other NPC popup's layout changes.
/// </summary>
internal static class NPCActionRowOverflowFix
{
    private static bool _loggedOnce;

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            SafePatcher.TryPatch(harmony, "NPCInspectionPopup", "SetupActions",
                postfix: new HarmonyMethod(typeof(NPCActionRowOverflowFix), nameof(SetupActions_Postfix)));
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"NPCActionRowOverflowFix: patch setup failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    static void SetupActions_Postfix(object __instance)
    {
        try
        {
            var type = __instance.GetType();
            // CurrentNPC is an auto-property, not a field — try the property first so
            // ReflectionCache.GetField doesn't log a guaranteed-miss Warning on every popup open.
            var npc = AccessTools.Property(type, "CurrentNPC")?.GetValue(__instance)
                      ?? Reflection.ReflectionCache.GetField(type, "CurrentNPC")?.GetValue(__instance);
            if (npc == null) return;

            var npcType = npc.GetType();
            bool canTrade = AccessTools.Property(npcType, "CanTrade")?.GetValue(npc) as bool? ?? false;
            bool hasCommissions = AccessTools.Property(npcType, "HasVisibleCommissions")?.GetValue(npc) as bool? ?? false;
            if (!canTrade || !hasCommissions) return;

            var parentField = Reflection.ReflectionCache.GetField(type, "DismantleOptionsParent");
            if (parentField?.GetValue(__instance) is not RectTransform rowParent) return;

            var layout = rowParent.GetComponent<HorizontalLayoutGroup>();
            if (!layout)
            {
                Util.Log.Warn("[NPCActionRowOverflowFix] DismantleOptionsParent has no HorizontalLayoutGroup — cannot safely correct overflow, skipping.");
                return;
            }

            if (!_loggedOnce)
            {
                _loggedOnce = true;
                Util.Log.Info($"[NPCActionRowOverflowFix] Before: rectWidth={rowParent.rect.width} "
                    + $"childCount={rowParent.childCount} childControlWidth={layout.childControlWidth} "
                    + $"childForceExpandWidth={layout.childForceExpandWidth} spacing={layout.spacing}");
            }

            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rowParent);

            Util.Log.Debug($"[NPCActionRowOverflowFix] Corrected row layout for CanTrade+HasVisibleCommissions NPC.");
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"[NPCActionRowOverflowFix] SetupActions postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }
}
