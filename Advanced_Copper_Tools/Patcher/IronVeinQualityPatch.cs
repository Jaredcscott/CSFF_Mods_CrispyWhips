using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Advanced_Copper_Tools.Patcher
{
    /// <summary>
    /// Bog iron mined directly from an Iron Vein skips the vanilla Fresh → Dried
    /// drying process, so the spawned Dried Bog Iron's Quality stat (SpecialDurability2)
    /// defaults to 0 instead of the value it would have after drying. Set it to 50 so
    /// vein-mined iron is immediately usable at a reasonable (but not top) quality.
    /// </summary>
    public static class IronVeinQualityPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        private const string IronVeinUid = "act_iron_vein";
        private const string BogIronDriedUid = "b9845f4223861e44f96c7815f210f365";
        private const float MinedQuality = 50f;

        public static void ApplyPatch(Harmony _)
        {
            try
            {
                ActionRouter.Register(new ActionHandler
                {
                    Name = "IronVeinQuality",
                    CardUid = IronVeinUid,
                    Timing = ActionTiming.AfterWrapped,
                    Before = ctx => { ctx.Tag = SnapshotIds(); return true; },
                    After = ctx => ApplyQualityWithRetry((HashSet<int>)ctx.Tag),
                });
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] IronVeinQuality patch failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void ApplyQualityWithRetry(HashSet<int> preIds)
        {
            if (ApplyQuality(preIds) == 0)
                Plugin.Instance.StartCoroutine(RetryNextFrame(preIds));
        }

        // Mirrors NuggetSmelt's retry: a produced card can materialize a frame after the
        // action coroutine drains — retry once more before giving up.
        private static IEnumerator RetryNextFrame(HashSet<int> preIds)
        {
            yield return null;
            ApplyQuality(preIds);
        }

        private static int ApplyQuality(HashSet<int> preIds)
        {
            int updated = 0;
            try
            {
                foreach (var card in CardFinder.FindAll(BogIronDriedUid))
                {
                    if (card is UnityEngine.Object uo && preIds != null && preIds.Contains(uo.GetInstanceID())) continue;

                    CardUtil.SetDurability(card, "SpecialDurability2", MinedQuality);
                    updated++;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] IronVeinQuality ApplyQuality: {ex.Message}");
            }

            if (updated > 0)
                Logger.LogDebug($"[ACT] IronVeinQuality: set Quality={MinedQuality} on {updated} mined bog iron");
            return updated;
        }

        private static HashSet<int> SnapshotIds()
        {
            var ids = new HashSet<int>();
            try
            {
                foreach (var card in CardFinder.FindAll(BogIronDriedUid))
                    if (card is UnityEngine.Object uo)
                        ids.Add(uo.GetInstanceID());
            }
            catch { }
            return ids;
        }
    }
}
