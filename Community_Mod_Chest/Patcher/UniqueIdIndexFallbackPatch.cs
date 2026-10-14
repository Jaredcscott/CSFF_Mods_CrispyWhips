using System;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Fallback for a framework older than 2.28.0 (Nexus report 2026-10-13, Kasi123456: every village
    /// building's "Enter" lands in the Inn). The game numbers <c>UniqueIDIndex</c> once, before mod cards
    /// register, so every JSON-loaded mod room keeps index 0 - and an environment's saved board AND the
    /// travel lookup are keyed by that index, so all seven CMC interiors collapse onto one room (the first
    /// match, the Inn) and share Env_BearCave's key. Framework 2.28.0's <c>UniqueIdIndexRepair</c> fixes it;
    /// this runs the identical, idempotent pass as a <c>GameManager.Awake</c> prefix so CMC does not depend
    /// on the framework version. With the framework fix present it finds nothing to change and is silent.
    /// </summary>
    internal static class UniqueIdIndexFallbackPatch
    {
        public static void ApplyPatch(Harmony harmony)
        {
            var awake = AccessTools.Method(typeof(GameManager), "Awake");
            if (awake == null)
            {
                Plugin.Logger.LogWarning("[UniqueIdIndexFallback] GameManager.Awake not found; village interiors may all open the Inn");
                return;
            }
            harmony.Patch(awake, prefix: new HarmonyMethod(typeof(UniqueIdIndexFallbackPatch), nameof(Awake_Prefix)));
        }

        private static void Awake_Prefix()
        {
            try
            {
                var list = UniqueIDScriptable.AllUniqueObjectsAsInts;
                if (list == null)
                {
                    Plugin.Logger.LogWarning("[UniqueIdIndexFallback] AllUniqueObjectsAsInts unavailable");
                    return;
                }
                int changed = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var obj = list[i];
                    if (obj == null || obj.UniqueIDIndex == i) continue;
                    obj.UniqueIDIndex = i;
                    changed++;
                }
                if (changed > 0)
                    Plugin.Logger.LogInfo($"[UniqueIdIndexFallback] assigned {changed} index(es) the framework had not (village interiors need their own, or every Enter opens the Inn)");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[UniqueIdIndexFallback] failed: {ex}");
            }
        }
    }
}
