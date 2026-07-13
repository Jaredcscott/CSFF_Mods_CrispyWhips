using System;
using System.Collections.Generic;
using HarmonyLib;
using BepInEx.Logging;
using CSFFModFramework.Api;
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

        public static void ApplyPatch(Harmony _)
        {
            try
            {
                ActionRouter.Register(new ActionHandler
                {
                    Name = "SawEffect",
                    CardPredicate = ctx => ctx.CardUid != null && LargeTreeGuids.Contains(ctx.CardUid),
                    Timing = ActionTiming.Before,
                    Before = ctx =>
                    {
                        if (ctx.GivenCard == null) return false;
                        if (CardUtil.GetCardUniqueId(ctx.GivenCard) != SawUniqueID) return false;
                        const float extraDamage = -25f;
                        if (CardUtil.ModifyDurabilityStat(ctx.Card, "CurrentProgress", extraDamage))
                            Logger.LogDebug($"[SawEffect] Applied extra {extraDamage} Progress to {ctx.CardUid}");
                        return false;
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SawEffect] Failed to apply patch: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
