using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Stockpiles (CMC round 2, plan feature I1): one placed card that holds many stat-less cards as a count.
    /// Three CT2 stockpiles (Stone, Twigs, Bones), each with a Fullness bar on SpecialDurability1 (0 to
    /// <see cref="MaxCount"/>). The materials are exactly the ones Documentation/Research/Community_Mod_Chest/
    /// world_and_crafting_data.md W0.11 measured as safe to store as a bare number: SpawnService.Spawn(uid)
    /// with no stat overrides gives back a card identical to the one dropped in. Logs, clay, hay and straw,
    /// the Long Stick and anything else that carries a stat players care about stay out.
    ///
    /// The card's own JSON does the stacking surface (a CardInteraction keyed on the one material GUID whose
    /// GivenCardChanges.ModType 3 consumes the dragged card, StackCompatible so a held stack drops in one
    /// gesture) and three DismantleActions: Take One, Take Five and Take Apart. A DA can never create a card
    /// from JSON (CardsToCreate in a DA is not processed), so the handlers below do the arithmetic:
    ///   - a CANCEL gate refuses a stack that would destroy a card for nothing (the pile is full, or a bone is
    ///     part boiled and would come back at Boil 0) and a Take on an empty pile, before any time is spent;
    ///   - Stack, AfterWrapped: add 1 to Fullness (the CI fires once per dragged card, and ActionRouter's
    ///     dispatch key includes the given card, so a dropped stack counts every card);
    ///   - Take One / Take Five, AfterWrapped: spawn min(n, count) cards through SpawnService.Spawn onto the
    ///     current board (where the stockpile stands) and lower Fullness by that many;
    ///   - Take Apart: the DA destroys the structure and returns its planks; Before captures the count into
    ///     ctx.Tag (the card is gone by After) and After spawns the whole count back.
    ///
    /// Registration runs once per process from Plugin.Awake under an _initialized guard (root CLAUDE.md
    /// "Per-Run Event Registration", pattern b): ActionRouter.Register is purely additive.
    /// </summary>
    internal static class StockpilePatch
    {
        /// <summary>Fullness ceiling. Mirrors SpecialDurability1.MaxValue on all three cards.</summary>
        internal const float MaxCount = 50f;

        internal const string KeyPrefix = "CMC_Stockpile";
        internal const string StackSuffix = "_CI_Stack";
        internal const string TakeOneSuffix = "_DA_TakeOne";
        internal const string TakeFiveSuffix = "_DA_TakeFive";
        internal const string TakeApartSuffix = "_DA_TakeApart";

        internal const string EmptyKey = "CMC_Stockpile_Empty";
        internal const string BoiledBoneKey = "CMC_Stockpile_BoiledBone";

        /// <summary>stockpile card UID -> the one material GUID it stores (W0.11 DECISION).</summary>
        private static readonly Dictionary<string, string> Materials = new(StringComparer.OrdinalIgnoreCase)
        {
            ["cmcStockpileStone"] = "a7384e5147b23a642809451cc4ef24fb", // StoneSmall
            ["cmcStockpileTwigs"] = "fa8834c3111a71f47bdd23d66b33cf61", // Twigs
            ["cmcStockpileBones"] = "674b640f46671f1418af4559b259b442", // Bones
        };

        private static bool _initialized;
        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            Func<ActionContext, bool> isStockpile = ctx => !string.IsNullOrEmpty(ctx.CardUid) && Materials.ContainsKey(ctx.CardUid);

            ActionRouter.Register(new ActionHandler
            {
                Name = "StockpileGate",
                CardPredicate = isStockpile,
                ActionKeyPrefix = KeyPrefix,
                Timing = ActionTiming.Cancel,
                Before = Gate,
            });
            ActionRouter.Register(new ActionHandler
            {
                Name = "StockpileAction",
                CardPredicate = isStockpile,
                ActionKeyPrefix = KeyPrefix,
                Timing = ActionTiming.AfterWrapped,
                Before = Capture,
                After = AfterAction,
            });
            Plugin.Logger.LogDebug("[StockpilePatch] initialized.");
        }

        private static bool Ends(string key, string suffix) =>
            !string.IsNullOrEmpty(key) && key.EndsWith(suffix, StringComparison.Ordinal);

        private static float Count(object card)
        {
            float v = CardUtil.GetDurability(card, "SpecialDurability1");
            return float.IsNaN(v) ? -1f : v;
        }

        /// <summary>Cancel gate: true refuses the action before the card is consumed or any time is spent.</summary>
        private static bool Gate(ActionContext ctx)
        {
            try
            {
                if (ctx.Card == null) return false;
                string key = ctx.ActionKey;
                float count = Count(ctx.Card);
                if (count < 0f) return false; // unreadable: let it through, never trap a player's items

                if (Ends(key, StackSuffix))
                {
                    if (count >= MaxCount)
                    {
                        Plugin.Logger.LogDebug("[StockpilePatch] Stack refused: the stockpile is full, the dragged card is left intact.");
                        return true;
                    }
                    if (string.Equals(ctx.CardUid, "cmcStockpileBones", StringComparison.OrdinalIgnoreCase) && ctx.GivenCard != null)
                    {
                        float boil = CardUtil.GetDurability(ctx.GivenCard, "SpecialDurability4");
                        if (!float.IsNaN(boil) && boil > 0f)
                        {
                            Plugin.Logger.LogDebug("[StockpilePatch] Stack refused: a part-boiled bone would come back at Boil 0.");
                            DyeVatPatch.ShowMessage(BoiledBoneKey);
                            return true;
                        }
                    }
                    return false;
                }

                if (Ends(key, TakeOneSuffix) || Ends(key, TakeFiveSuffix))
                {
                    if (count < 1f)
                    {
                        Plugin.Logger.LogDebug("[StockpilePatch] Take refused: the stockpile is empty.");
                        DyeVatPatch.ShowMessage(EmptyKey);
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                WarnOnce("gate-exception", $"[StockpilePatch] Gate failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }

        /// <summary>Take Apart destroys the card, so its count is read now and carried to After in ctx.Tag.</summary>
        private static bool Capture(ActionContext ctx)
        {
            try
            {
                if (ctx.Card != null && Ends(ctx.ActionKey, TakeApartSuffix)) ctx.Tag = Count(ctx.Card);
            }
            catch (Exception ex)
            {
                WarnOnce("capture-exception", $"[StockpilePatch] Capture failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
            return false;
        }

        private static void AfterAction(ActionContext ctx)
        {
            try
            {
                string key = ctx.ActionKey;
                if (!Materials.TryGetValue(ctx.CardUid ?? "", out var material)) return;

                if (Ends(key, StackSuffix))
                {
                    if (ctx.Card == null) return;
                    float count = Count(ctx.Card);
                    if (count < 0f)
                    {
                        WarnOnce("fullness-unreadable", "[StockpilePatch] Fullness could not be read, so a stacked card was NOT counted.");
                        return;
                    }
                    SetCount(ctx.Card, Math.Min(count + 1f, MaxCount));
                }
                else if (Ends(key, TakeOneSuffix) || Ends(key, TakeFiveSuffix))
                {
                    if (ctx.Card == null) return;
                    int want = Ends(key, TakeFiveSuffix) ? 5 : 1;
                    float count = Count(ctx.Card);
                    if (count < 1f) return;
                    int give = (int)Math.Min(want, Math.Floor(count));
                    SetCount(ctx.Card, count - give);
                    for (int i = 0; i < give; i++) SpawnService.Spawn(material);
                    Plugin.Logger.LogDebug($"[StockpilePatch] Took {give} from {ctx.CardUid} ({count:0} -> {count - give:0}).");
                }
                else if (Ends(key, TakeApartSuffix))
                {
                    int give = ctx.Tag is float held && held >= 1f ? (int)Math.Floor(held) : 0;
                    for (int i = 0; i < give; i++) SpawnService.Spawn(material);
                    Plugin.Logger.LogDebug($"[StockpilePatch] Take Apart handed back {give} from {ctx.CardUid}.");
                }
            }
            catch (Exception ex)
            {
                WarnOnce("after-exception", $"[StockpilePatch] AfterAction failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void SetCount(object card, float value)
        {
            if (!CardUtil.SetDurability(card, "SpecialDurability1", value))
                WarnOnce("fullness-write", "[StockpilePatch] Fullness could not be written, so the count did not change.");
            CardVisualsRefresh.RefreshDurabilityVisuals(card);
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
