using System;
using System.Collections.Generic;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// "Tend Flock" — a herd QoL DismantleAction on the Sheep Feeder (see SheepFeeder.json
/// DismantleActions[1]) that shears every ready tame/lactating sheep in the player's
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
    private const string TendFlockNamePrefix = "Tend Flock";

    private const string TameSheepUid = "sh_tame_sheep";
    private const string LactatingSheepUid = "sh_lactating_sheep";
    private const string WoolUid = "sh_wool";
    private const string LanolinUid = "sh_lanolin";

    private const string WoolStat = "SpecialDurability1";
    private const float ShearReadyPercent = 0.5f;   // matches Shear CI's RequiredSpecial1Percent
    private const float LanolinDropChance = 0.25f;  // matches Shear CI's DropChance.BaseDropChance

    private static readonly HashSet<string> ShearableUids = new(StringComparer.Ordinal)
    {
        TameSheepUid, LactatingSheepUid,
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
            After = _ => TendFlock(),
        });
    }

    private static void TendFlock()
    {
        try
        {
            int sheared = 0, skipped = 0, lanolinDrops = 0;

            foreach (var card in GameQuery.CardsInPlayerEnv())
            {
                string uid = CardUtil.GetCardUniqueId(card);
                if (uid == null || !ShearableUids.Contains(uid)) continue;

                if (TryShear(card, uid, ref lanolinDrops)) sheared++;
                else skipped++;
            }

            Logger?.LogInfo($"[SheepFeeder] Tend Flock: sheared {sheared} sheep"
                + (lanolinDrops > 0 ? $" (+{lanolinDrops} lanolin)" : "")
                + (skipped > 0 ? $", {skipped} not ready yet." : "."));
        }
        catch (Exception ex)
        {
            Logger?.LogError($"[SheepFeeder] Tend Flock failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    /// <summary>Shears one sheep if its Wool stat is at/above the ready threshold. Mirrors
    /// TameSheep.json/LactatingSheep.json's "Shear" CardInteraction: 2x Wool, 25% chance of
    /// 1x Lanolin, Wool stat reset to 0. Returns false (no log spam) for a not-ready sheep —
    /// this is the expected common case, not an error.</summary>
    private static bool TryShear(object sheep, string uid, ref int lanolinDrops)
    {
        float current = CardUtil.GetDurability(sheep, WoolStat);
        float max = CardUtil.GetDurabilityMax(sheep, WoolStat);
        if (float.IsNaN(current) || float.IsNaN(max) || max <= 0f)
        {
            Logger?.LogDebug($"[SheepFeeder] {uid}: could not read Wool stat — skipped.");
            return false;
        }
        if (current / max < ShearReadyPercent)
        {
            Logger?.LogDebug($"[SheepFeeder] {uid} not ready to shear ({current}/{max}).");
            return false;
        }

        SpawnService.Spawn(WoolUid);
        SpawnService.Spawn(WoolUid);
        if (UnityEngine.Random.value < LanolinDropChance)
        {
            SpawnService.Spawn(LanolinUid);
            lanolinDrops++;
        }

        if (!CardUtil.SetDurability(sheep, WoolStat, 0f))
            Logger?.LogDebug($"[SheepFeeder] SetDurability(Wool, 0) failed on {uid} after shearing.");

        Logger?.LogDebug($"[SheepFeeder] sheared {uid}.");
        return true;
    }
}
