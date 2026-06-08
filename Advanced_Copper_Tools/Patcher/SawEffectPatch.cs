using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using CSFFModFramework.Util;

namespace Advanced_Copper_Tools.Patcher
{
    /// <summary>
    /// Enhances the Large Copper Saw's tree-cutting effectiveness.
    /// When the saw is dragged onto a large tree, an extra -25 Progress is applied
    /// before the normal Cut Tree action (which also does -25), totalling -50 per chop.
    /// Result: Pine/Willow in 1 action, Oak in 2 actions (50% per hit).
    /// </summary>
    public static class SawEffectPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        private const string SawUniqueID = "advanced_copper_tools_large_saw";

        private static readonly HashSet<string> LargeTreeGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "41fbf7771da1b9f4ea13af0bc1ea4341", // TreePineLarge
            "14201221856a7b34fb86d602e0359b83", // TreeOakLarge
            "e8287a79ea2ea4245b4a83ce727c4c9d", // TreeBirchLarge
            "f27a6838066ae10428aa6df6d6259221"  // TreeWillowLarge
        };

        // No local reflection caches — CardUtil handles card identity and method resolution.

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gameManagerType = AccessTools.TypeByName("GameManager");
                if (gameManagerType == null)
                {
                    Logger.LogError("[SawEffect] GameManager type not found.");
                    return;
                }

                var actionRoutine = FindActionRoutine(gameManagerType);
                if (actionRoutine == null)
                {
                    Logger.LogError("[SawEffect] GameManager.ActionRoutine not found.");
                    return;
                }

                var prefix = AccessTools.Method(typeof(SawEffectPatch), nameof(ActionRoutine_Prefix));
                harmony.Patch(actionRoutine, prefix: new HarmonyMethod(prefix));
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SawEffect] Failed to apply patch: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static MethodInfo FindActionRoutine(Type gameManagerType)
        {
            // EA 0.63+: drag-onto-card handler renamed to CardOnCardActionRoutine; fall back to ActionRoutine.
            return CardUtil.FindMethodBySignature(gameManagerType, "CardOnCardActionRoutine", "CardOnCardAction", "InGameCardBase")
                ?? CardUtil.FindMethodBySignature(gameManagerType, "ActionRoutine", "CardAction", "InGameCardBase");
        }

        /// <summary>
        /// Prefix on GameManager.ActionRoutine. When the Large Saw is dragged onto a large tree,
        /// apply an extra -25 to the tree's Progress before the normal action runs.
        /// </summary>
        static void ActionRoutine_Prefix(
            object _Action,
            object _ReceivingCard,
            object _GivenCard)
        {
            try
            {
                if (_GivenCard == null || _ReceivingCard == null) return;

                string givenUid = CardUtil.GetCardUniqueId(_GivenCard);
                if (givenUid != SawUniqueID) return;

                string recvUid = CardUtil.GetCardUniqueId(_ReceivingCard);
                if (recvUid == null || !LargeTreeGuids.Contains(recvUid)) return;

                const float extraDamage = -25f;
                if (CardUtil.ModifyDurabilityStat(_ReceivingCard, "CurrentProgress", extraDamage))
                    Logger.LogDebug($"[SawEffect] Applied extra {extraDamage} Progress to {recvUid}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SawEffect] Prefix error: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

    }
}
