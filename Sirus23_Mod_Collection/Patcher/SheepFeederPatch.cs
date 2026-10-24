using System;
using System.Collections.Generic;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// "Tend Flock" — a herd QoL DismantleAction on the Sheep Feeder (see SheepFeeder.json
/// DismantleActions[1]) that shears every ready tame/lactating sheep and ram in the player's
/// current environment in one click, instead of shearing each sheep by hand.
///
/// The DA itself is a no-op button (ReceivingCardChanges.ModType: 0, AlwaysShow: true —
/// same "JSON is just a button hook" pattern as Grind All, CLAUDE.md's "Grind All" Harmony
/// pattern); this Harmony/ActionRouter handler does the real work after the button's own
/// timer completes.
///
/// SCOPE: shearing only, NOT milking. Vanilla milk production (LactatingSheep.json's "Milk"
/// CardInteraction) fires via CreatedLiquidInGivenCard, which the engine implements by
/// running GameManager.AddCard(...) to attach a NEW, NESTED InGameCardBase liquid-card
/// instance (CardType 9, sh_sheep_milk) into the dragged container's ContainedLiquid slot —
/// confirmed by reading .decomp/InGameCardBase.cs: CurrentLiquidQuantity used for a filled
/// container reads from card.ContainedLiquid.CurrentLiquidQuantity, a SEPARATE live card
/// instance, not a flat field on the container itself. There is no existing precedent
/// anywhere in this codebase for invoking that private AddCard coroutine from C# (would need
/// reflection onto a private IEnumerator method, a hand-built TransferedDurabilities struct,
/// and a real container instance to attach to — this DA has none, since no bowl is dragged).
/// Automating it safely would need either a player-provided-bowl UX (feeder inventory would
/// have to also accept a water/dairy container tag, changing its current feed-only design) or
/// a first-of-its-kind AddCard reflection path — both are design/engineering decisions bigger
/// than this pass. Shear reuses the same simple SpawnService.Spawn + CardUtil.SetDurability
/// path every other spawn-eject patch in this codebase already uses safely (matches
/// TameSheep.json/LactatingSheep.json's own "Shear" CardInteraction 1:1: 2x Wool, 25% chance
/// of 1x Lanolin, Wool stat reset to 0). See memory reference_spawn_eject_pattern,
/// reference_grind_all_harmony_pattern.
/// </summary>
internal static class SheepFeederPatch
{
    private const string FeederUid = "sh_sheep_feeder";
    private const string TendFlockKeyPrefix = "sh_sheep_feeder_DismantleActions[1]";
    // Also matches N2's "Tend Flock with the Wolf" (DismantleActions[2]) through the name tier:
    // ActionRouter ORs the key and name prefixes against DefaultText, so ONE handler covers both
    // buttons. A second handler for [2] would also match here and run TendFlock twice per click.
    private const string TendFlockNamePrefix = "Tend Flock";

    private const string TameSheepUid = "sh_tame_sheep";
    private const string LactatingSheepUid = "sh_lactating_sheep";
    private const string MaleSheepUid = "sh_male_sheep";
    private const string WoolUid = "sh_wool";
    private const string LanolinUid = "sh_lanolin";

    private const string FeedTag = "tag_FeedHerbi";
    // Feature 32: a vanilla Dry Fodder pile on the board also feeds the flock when the feeder runs out.
    private const string DryFodderUid = "8f6be94d6c1eabd48bd75d80d76ba1a5";   // DryFodderPlaced
    private const string FodderAmountStat = "UsageDurability";                  // the pile's "Amount"
    private const float FodderAmountPerSheep = 1.0f;                            // W0-22: whole units, a pile under 1.0 is left to the cattle
    private static readonly HashSet<string> _fodderWarned = new(StringComparer.Ordinal);
    // Write failures on the sheep themselves (Nourishment top-up, Wool reset): one warning per cause per session.
    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private const string NourishmentStat = "SpoilageTime";
    private const float FeedRefillBelowPercent = 0.67f;  // top up a sheep below two thirds of the bar
    private const float FeedNourishment = 144f;          // keep equal to the Feed CI's SpoilageChange

    private const string WoolStat = "SpecialDurability1";
    private const float ShearReadyPercent = 0.5f;   // matches Shear CI's RequiredSpecial1Percent
    private const float LanolinDropChance = 0.25f;  // matches Shear CI's DropChance.BaseDropChance

    private static readonly HashSet<string> ShearableUids = new(StringComparer.Ordinal)
    {
        TameSheepUid, LactatingSheepUid, MaleSheepUid,
    };

    private static ManualLogSource Logger => Plugin.Logger;

    public static void Register()
    {
        ActionRouter.Register(new ActionHandler
        {
            Name = "TendFlock",
            CardUid = FeederUid,
            ActionKeyPrefix = TendFlockKeyPrefix,
            ActionNamePrefix = TendFlockNamePrefix,
            Timing = ActionTiming.AfterWrapped,
            After = ctx => TendFlock(ctx.Card),
        });
    }

    private static void TendFlock(object feeder)
    {
        try
        {
            int sheared = 0, skipped = 0, lanolinDrops = 0, fed = 0, resetFailed = 0;
            bool feederEmpty = false;
            int fodderDrawn = 0;

            // Dry Fodder piles in THIS environment (CardsInPlayerEnv is env-scoped, never the whole AllCards),
            // each with its Amount tracked locally: ChangeCardDurability applies on the game's own coroutine,
            // so a re-read inside this pass would still show the old value.
            var piles = new List<object>();
            var amounts = new Dictionary<object, float>();
            var envCards = GameQuery.CardsInPlayerEnv();
            foreach (var c in envCards)
            {
                if (CardUtil.GetCardUniqueId(c) != DryFodderUid) continue;
                float amt = CardUtil.GetDurability(c, FodderAmountStat);
                if (float.IsNaN(amt)) { WarnFodderOnce("amount-nan", "a Dry Fodder pile has no readable Amount (UsageDurability), so it cannot feed the flock."); continue; }
                piles.Add(c);
                amounts[c] = amt;
            }

            foreach (var card in envCards)
            {
                string uid = CardUtil.GetCardUniqueId(card);
                if (uid == null || !ShearableUids.Contains(uid)) continue;

                // Feed first, so a sheep topped up this pass already earns its well-fed bonus.
                fed += FeedFlock(feeder, card, ref feederEmpty, piles, amounts, ref fodderDrawn);

                int failedBefore = resetFailed;
                if (TryShear(card, uid, ref lanolinDrops, ref resetFailed)) sheared++;
                else if (resetFailed == failedBefore) skipped++;
            }

            Logger?.LogInfo($"[SheepFeeder] Tend Flock: fed {fed}{(feederEmpty ? " (feeder empty)" : "")}{(fodderDrawn > 0 ? $" ({fodderDrawn} from Dry Fodder)" : "")}, sheared {sheared} sheep"
                + (lanolinDrops > 0 ? $" (+{lanolinDrops} lanolin)" : "")
                + (resetFailed > 0 ? $", {resetFailed} not sheared (Wool reset failed)" : "")
                + (skipped > 0 ? $", {skipped} not ready yet." : "."));
        }
        catch (Exception ex)
        {
            Logger?.LogError($"[SheepFeeder] Tend Flock failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    /// <summary>Tops one sheep's Nourishment up from the feeder's own inventory: one feed card per sheep
    /// below two thirds of the bar, the same 144 the drag Feed CI gives. Stops for the pass once the
    /// feeder runs dry. Returns 1 if a card was spent. A card with no Nourishment bar (an older save)
    /// reads NaN or a zero max and is skipped.</summary>
    private static int FeedFlock(object feeder, object sheep, ref bool feederEmpty,
        List<object> piles, Dictionary<object, float> amounts, ref int fodderDrawn)
    {
        if (feeder == null) return 0;
        if (feederEmpty && piles.Count == 0) return 0;
        float cur = CardUtil.GetDurability(sheep, NourishmentStat);
        float max = CardUtil.GetDurabilityMax(sheep, NourishmentStat);
        if (float.IsNaN(cur) || float.IsNaN(max) || max <= 0f)
        {
            WarnOnce("nourishment-read", $"a sheep's Nourishment ({NourishmentStat}) could not be read, so Tend Flock could not feed it.");
            return 0;
        }
        if (cur / max >= FeedRefillBelowPercent) return 0;  // not hungry: the normal negative, kept silent

        // The feeder's own stock first, then a Dry Fodder pile (the same flat 144 either way: no Nutrition scaling).
        bool fed = false;
        if (!feederEmpty)
        {
            if (Inventory.Consume(feeder, FeedTag, 1) >= 1) fed = true;
            else feederEmpty = true;
        }
        if (!fed && TryDrawFodder(piles, amounts))
        {
            fed = true;
            fodderDrawn++;
        }
        if (!fed) return 0;

        if (!CardUtil.SetDurability(sheep, NourishmentStat, Math.Min(max, cur + FeedNourishment)))
            WarnOnce("nourishment-write", "a sheep's Nourishment (SpoilageTime) could not be written after a feed card was spent, so that feed was used up without feeding it.");
        return 1;
    }

    /// <summary>Takes FodderAmountPerSheep from the fullest Dry Fodder pile that still holds that much, through
    /// the game's own ChangeCardDurability so the pile's OnZero (destroy) fires when it empties; the Amount is
    /// never written directly (CardUtil.SetDurability does not run OnZero). Returns false, with the cause logged
    /// once, when no pile can be drawn from.</summary>
    private static bool TryDrawFodder(List<object> piles, Dictionary<object, float> amounts)
    {
        if (piles.Count == 0) return false;
        object best = null;
        float bestAmt = 0f;
        foreach (var pile in piles)
        {
            if (amounts[pile] >= FodderAmountPerSheep && amounts[pile] > bestAmt) { best = pile; bestAmt = amounts[pile]; }
        }
        if (best == null) return false;   // every pile is under one unit: left to the cattle

        var gm = CardUtil.GetGameManagerInstance() as GameManager;
        if (gm == null) { WarnFodderOnce("no-gm", "no live GameManager, so Dry Fodder cannot feed the flock."); return false; }
        if (best is not InGameCardBase pileCard) { WarnFodderOnce("pile-type", $"a Dry Fodder pile is a {best.GetType().Name}, not an InGameCardBase."); return false; }
        try
        {
            gm.ChangeCardDurability(pileCard, -FodderAmountPerSheep, DurabilitiesTypes.Usage, InGameNPCOrPlayer.Null);
        }
        catch (Exception ex)
        {
            WarnFodderOnce("draw-threw:" + ex.GetType().Name, $"ChangeCardDurability threw {ex.GetType().Name}, so Dry Fodder did not feed the flock: {ex.InnerException?.ToString() ?? ex.Message}");
            return false;
        }
        amounts[best] = bestAmt - FodderAmountPerSheep;
        return true;
    }

    private static void WarnFodderOnce(string cause, string message)
    {
        if (_fodderWarned.Add(cause))
            Logger?.LogWarning($"[SheepFeeder] Dry Fodder: {message} Logged once per cause.");
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
            Logger?.LogWarning($"[SheepFeeder] Tend Flock: {message} Logged once per cause.");
    }

    /// <summary>Shears one sheep if its Wool stat is at/above the ready threshold. Mirrors
    /// TameSheep.json/LactatingSheep.json/MaleSheep.json's "Shear" CardInteraction: 2x Wool, 25% chance of
    /// 1x Lanolin, Wool stat reset to 0. Returns false (no log spam) for a not-ready sheep —
    /// this is the expected common case, not an error. The Wool reset runs BEFORE any wool is
    /// spawned: if it fails the shear is aborted (counted in <paramref name="resetFailed"/>, warned
    /// once), since spawning first would let every later press mint wool from the same full bar.</summary>
    private static bool TryShear(object sheep, string uid, ref int lanolinDrops, ref int resetFailed)
    {
        float current = CardUtil.GetDurability(sheep, WoolStat);
        float max = CardUtil.GetDurabilityMax(sheep, WoolStat);
        if (float.IsNaN(current) || float.IsNaN(max) || max <= 0f)
        {
            WarnOnce("wool-read", $"the Wool stat ({WoolStat}) could not be read on {uid}, so that sheep was skipped (it counts as not ready).");
            return false;
        }
        if (current / max < ShearReadyPercent)
        {
            Logger?.LogDebug($"[SheepFeeder] {uid} not ready to shear ({current}/{max}).");
            return false;
        }

        if (!CardUtil.SetDurability(sheep, WoolStat, 0f))
        {
            resetFailed++;
            WarnOnce("wool-reset", $"the Wool stat ({WoolStat}) could not be reset on {uid}, so that sheep was not sheared and no wool was made.");
            return false;
        }

        SpawnService.Spawn(WoolUid);
        SpawnService.Spawn(WoolUid);
        if (UnityEngine.Random.value < LanolinDropChance)
        {
            SpawnService.Spawn(LanolinUid);
            lanolinDrops++;
        }

        Logger?.LogDebug($"[SheepFeeder] sheared {uid}.");
        return true;
    }
}
