using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Advanced_Copper_Tools.Patcher
{
    /// <summary>
    /// When tin ore (act_tin_ore) finishes smelting, its OnFull produces a vanilla MetalNugget
    /// (4b0f…) at SD4=0, SD2=0. This patch snapshots the ore's own Quality (SpecialDurability1)
    /// before it's consumed, then sets SD4=120 (tin metal type) and SD2=<ore quality> on the
    /// newly spawned nugget so vanilla blueprint SD4 gates recognise the correct metal type and
    /// the nugget's Metal Quality (SpecialDurability2 — the same slot vanilla's own MetalBarFinished/
    /// MetalWireFinished recycling recipes transfer via TransferRules.Special2) reflects the ore it
    /// came from. SpecialDurability1 on MetalNugget is "Strikes" (smithing progress), NOT quality —
    /// must never be written here.
    /// </summary>
    public static class IronNailSmeltPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private const string MetalNuggetUID  = "4b0f4937a5ecb90499428c8c10288afc";
        private const string TinOreUid  = "act_tin_ore";
        private const float  TinMetalType    = 120f;

        private readonly struct SmeltSnapshot
        {
            public readonly HashSet<int> PreExistingNuggetIds;
            public readonly float OreQuality;

            public SmeltSnapshot(HashSet<int> preExistingNuggetIds, float oreQuality)
            {
                PreExistingNuggetIds = preExistingNuggetIds;
                OreQuality = oreQuality;
            }
        }

        public static void ApplyPatch(Harmony _)
        {
            try
            {
                ActionRouter.Register(new ActionHandler
                {
                    Name = "TinOreSmeltMetalType",
                    CardUid = TinOreUid,
                    Timing = ActionTiming.AfterWrapped,
                    Before = ctx =>
                    {
                        float quality = CardUtil.GetDurability(ctx.Card, "SpecialDurability1");
                        ctx.Tag = new SmeltSnapshot(SnapshotNuggetIds(), float.IsNaN(quality) ? 0f : quality);
                        return true;
                    },
                    After = ctx => ApplyOrePropertiesWithRetry((SmeltSnapshot)ctx.Tag, TinMetalType),
                });
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] NuggetSmelt patch failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void ApplyOrePropertiesWithRetry(SmeltSnapshot snapshot, float metalType)
        {
            if (ApplyOreProperties(snapshot, metalType) == 0)
                Plugin.Instance.StartCoroutine(RetryNextFrame(snapshot, metalType));
        }

        // OnFull's spawned nugget can materialize a frame after the action coroutine drains
        // (see the original prefix/postfix pair this replaced) — retry once more before giving up.
        private static IEnumerator RetryNextFrame(SmeltSnapshot snapshot, float metalType)
        {
            yield return null;
            ApplyOreProperties(snapshot, metalType);
        }

        private static int ApplyOreProperties(SmeltSnapshot snapshot, float metalType)
        {
            int updated = 0;
            try
            {
                var preIds = snapshot.PreExistingNuggetIds;
                foreach (var card in EnumerateAllCards())
                {
                    if (CardUtil.GetCardUniqueId(card) != MetalNuggetUID) continue;
                    if (card is UnityEngine.Object uo && preIds != null && preIds.Contains(uo.GetInstanceID())) continue;

                    float current = CardUtil.GetDurability(card, "SpecialDurability4");
                    if (!float.IsNaN(current) && current > 0f) continue;

                    CardUtil.SetDurability(card, "SpecialDurability4", metalType);
                    CardUtil.SetDurability(card, "SpecialDurability2", snapshot.OreQuality);
                    updated++;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] NuggetSmelt ApplyOreProperties: {ex.Message}");
            }

            if (updated > 0)
                Logger.LogDebug($"[ACT] NuggetSmelt: set SD4={metalType}, SD2(Metal Quality)={snapshot.OreQuality} on {updated} nugget(s)");
            return updated;
        }

        private static HashSet<int> SnapshotNuggetIds()
        {
            var ids = new HashSet<int>();
            try
            {
                foreach (var card in EnumerateAllCards())
                {
                    if (CardUtil.GetCardUniqueId(card) == MetalNuggetUID && card is UnityEngine.Object uo)
                        ids.Add(uo.GetInstanceID());
                }
            }
            catch { }
            return ids;
        }

        private static IEnumerable EnumerateAllCards()
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) yield break;

            FieldInfo allCardsField = null;
            for (var t = gm.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                allCardsField = t.GetField("AllCards", Flags)
                             ?? t.GetField("<AllCards>k__BackingField", Flags);
                if (allCardsField != null) break;
            }

            var cards = allCardsField?.GetValue(gm) as IEnumerable;
            if (cards == null) yield break;

            foreach (var c in cards)
                if (c != null) yield return c;
        }
    }
}
