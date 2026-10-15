using System;
using System.Collections.Generic;
using HarmonyLib;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Advanced_Copper_Tools.Patcher
{
    /// <summary>
    /// Enhances the large saws' tree-cutting effectiveness, per metal.
    /// When a saw is dragged onto a large tree, its extra Progress damage is applied
    /// before the normal Cut Tree action (which does -25 Progress and -1 saw Usage on its own).
    ///
    /// The Large Copper Saw carries a Metal Type (SpecialDurability4, ACT 1.18.0, Documentation/Design/AdvancedCopperTools_Metal_Variant_Art_As_Built.md step 3)
    /// and its felling power and wear follow it (tunable placeholders, research
    /// Documentation/Research/AdvancedCopperTools/tierb-per-metal-stats.md section 3.1):
    ///   Copper 100       -12 extra (37 per pull), wear 1.0  (100 pulls)
    ///   Ghost Bronze 110 -12 extra (37 per pull), wear 0.6  (~166 pulls)
    ///   Tin 120          -15 extra (40 per pull), wear 1.5  (~66 pulls)
    ///   Tin Bronze 130   -12 extra (37 per pull), wear 0.5  (200 pulls)
    ///   White Bronze 140 -25 extra (50 per pull), wear 1.25 (80 pulls)
    /// The Iron saw (its own card, no Metal Type) has -25 extra and vanilla's wear of 1.
    /// (ACT 1.18.9, ACT-B10: halved from -25 / -50 so a saw beats an axe without replacing it.)
    ///
    /// Dullness (SpecialDurability1, ACT 1.18.9): each pull that gets the extra adds 1; at
    /// DullnessMax the saw is blunt and fells at the vanilla axe rate (no extra) until it is
    /// sharpened on a stone or whetstone (JSON CardInteractions reset the stat to 0). The stat
    /// counts UP from 0 so a saw saved before 1.18.9, whose new slot loads at 0, starts sharp.
    ///
    /// Wear is the vanilla -1 plus a delta. Extra wear (delta below 0) is paid in Before, so the
    /// vanilla -1 that follows is what takes a worn saw to 0 and fires its own OnZero ("Saw Broken!");
    /// a direct field write never fires OnZero. A refund (delta above 0) is paid in After, once the
    /// vanilla -1 has landed, so a full saw's refund is not lost to the 100 cap, and never on a saw
    /// the action just broke.
    /// </summary>
    public static class SawEffectPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        private const string CopperSawUid = "advanced_copper_tools_large_saw";
        private const string IronSawUid = "act_iron_large_saw";

        private struct SawMetal
        {
            public float ExtraProgress;
            public float WearDelta; // added to vanilla's -1 Usage per pull: wear = 1 - WearDelta
            public SawMetal(float extraProgress, float wearDelta) { ExtraProgress = extraProgress; WearDelta = wearDelta; }
        }

        private static readonly SawMetal Copper = new SawMetal(-12f, 0f);
        private static readonly SawMetal Iron = new SawMetal(-25f, 0f);

        // Metal Type rung -> stats. Tunable placeholders.
        private static readonly Dictionary<int, SawMetal> CopperSawByMetal = new Dictionary<int, SawMetal>
        {
            { 100, Copper },
            { 110, new SawMetal(-12f, 0.4f) },   // Ghost Bronze: wear 0.6
            { 120, new SawMetal(-15f, -0.5f) },  // Tin: wear 1.5
            { 130, new SawMetal(-12f, 0.5f) },   // Tin Bronze: wear 0.5
            { 140, new SawMetal(-25f, -0.25f) }, // White Bronze: wear 1.25
        };

        // Must equal SpecialDurability1.MaxValue on LargeSaw.json and IronLargeSaw.json.
        internal const float DullnessMax = 25f;

        private static readonly HashSet<string> LargeTreeGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "41fbf7771da1b9f4ea13af0bc1ea4341", // TreePineLarge
            "14201221856a7b34fb86d602e0359b83", // TreeOakLarge
            "e8287a79ea2ea4245b4a83ce727c4c9d", // TreeBirchLarge
            "f27a6838066ae10428aa6df6d6259221"  // TreeWillowLarge
        };

        // Never let a Before-side wear write reach 0 itself: OnZero fires only on the game's own change.
        private const float MinUsageAfterPrepay = 0.01f;

        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string msg)
        {
            if (_warned.Add(cause)) Logger.LogWarning($"[SawEffect] {msg} (logged once)");
        }

        public static void ApplyPatch(Harmony _)
        {
            try
            {
                ActionRouter.Register(new ActionHandler
                {
                    Name = "SawEffect",
                    CardPredicate = ctx => ctx.CardUid != null && LargeTreeGuids.Contains(ctx.CardUid),
                    Timing = ActionTiming.AfterWrapped,
                    Before = ctx =>
                    {
                        ctx.Tag = null;
                        if (ctx.GivenCard == null) return false;
                        var sawUid = CardUtil.GetCardUniqueId(ctx.GivenCard);
                        if (sawUid == null) return false;

                        SawMetal metal;
                        if (string.Equals(sawUid, IronSawUid, StringComparison.Ordinal)) metal = Iron;
                        else if (string.Equals(sawUid, CopperSawUid, StringComparison.Ordinal)) metal = ResolveMetal(ctx.GivenCard);
                        else return false;

                        if (TryDull(ctx.GivenCard))
                        {
                            if (CardUtil.ModifyDurabilityStat(ctx.Card, "CurrentProgress", metal.ExtraProgress))
                                Logger.LogDebug($"[SawEffect] Applied extra {metal.ExtraProgress} Progress to {ctx.CardUid}");
                            else
                                WarnOnce("progress-write", $"could not write CurrentProgress on {ctx.CardUid}; the saw fells at the vanilla rate");
                        }
                        else
                            Logger.LogDebug($"[SawEffect] Saw is blunt; no extra Progress on {ctx.CardUid}");

                        if (metal.WearDelta < 0f) PrepayWear(ctx.GivenCard, metal.WearDelta);
                        else if (metal.WearDelta > 0f) ctx.Tag = metal.WearDelta;
                        return false;
                    },
                    After = ctx =>
                    {
                        if (!(ctx.Tag is float refund) || refund <= 0f || ctx.GivenCard == null) return;
                        Refund(ctx.GivenCard, refund);
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SawEffect] Failed to apply patch: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static SawMetal ResolveMetal(object saw)
        {
            float sd4 = CardUtil.GetDurability(saw, "SpecialDurability4");
            if (float.IsNaN(sd4))
            {
                WarnOnce("sd4-nan", "could not read the Large Copper Saw's Metal Type (SpecialDurability4); treating it as copper");
                return Copper;
            }
            int rung = (int)Math.Round(sd4);
            if (CopperSawByMetal.TryGetValue(rung, out var metal)) return metal;
            WarnOnce("sd4-rung-" + rung, $"Large Copper Saw has Metal Type {sd4}, not a known rung (100-140); treating it as copper");
            return Copper;
        }

        // True when the saw is sharp enough for its extra felling; adds one pull of Dullness.
        // A blunt saw (Dullness at max) returns false. An unreadable stat keeps the old
        // behaviour (extra applies) rather than silently disabling the saw.
        private static bool TryDull(object saw)
        {
            float dull = CardUtil.GetDurability(saw, "SpecialDurability1");
            if (float.IsNaN(dull))
            {
                WarnOnce("dullness-read", "could not read the saw's Dullness (SpecialDurability1); treating it as sharp");
                return true;
            }
            if (dull >= DullnessMax - 0.01f) return false;
            if (!CardUtil.SetDurability(saw, "SpecialDurability1", Math.Min(DullnessMax, dull + 1f)))
                WarnOnce("dullness-write", "could not write the saw's Dullness (SpecialDurability1); it will not blunt");
            return true;
        }

        private static void PrepayWear(object saw, float delta)
        {
            float usage = CardUtil.GetDurability(saw, "UsageDurability");
            if (float.IsNaN(usage))
            {
                WarnOnce("usage-read-before", "could not read the saw's Usage; extra wear skipped");
                return;
            }
            float applied = Math.Max(delta, MinUsageAfterPrepay - usage);
            if (applied >= 0f) return; // already at the floor: the vanilla -1 breaks it
            if (!CardUtil.ModifyDurabilityStat(saw, "CurrentUsageDurability", applied))
                WarnOnce("usage-write-before", "could not write the saw's CurrentUsageDurability; extra wear skipped");
        }

        private static void Refund(object saw, float delta)
        {
            if (saw is UnityEngine.Object uo && uo == null) return; // destroyed by the action
            float usage = CardUtil.GetDurability(saw, "UsageDurability");
            if (float.IsNaN(usage))
            {
                WarnOnce("usage-read-after", "could not read the saw's Usage; wear refund skipped");
                return;
            }
            if (usage <= 0f) return; // the pull broke it: no refund on a broken saw
            float max = CardUtil.GetDurabilityMax(saw, "UsageDurability");
            if (float.IsNaN(max))
            {
                WarnOnce("usage-max", "could not read the saw's Usage maximum; wear refund skipped");
                return;
            }
            float applied = Math.Min(delta, max - usage);
            if (applied <= 0f) return;
            if (!CardUtil.ModifyDurabilityStat(saw, "CurrentUsageDurability", applied))
                WarnOnce("usage-write-after", "could not write the saw's CurrentUsageDurability; wear refund skipped");
        }
    }
}
