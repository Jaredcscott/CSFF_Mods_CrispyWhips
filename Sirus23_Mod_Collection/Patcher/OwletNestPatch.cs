using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using CSFFModFramework.Util;
using HarmonyLib;
using UnityEngine;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// Features 33 (raise an owlet) and 28 (raise an eyas): at GameDataReady, append low-weight collections to the
/// vanilla Event_BirdNest "Climb" action. The Owlet collection comes up about one successful climb in twenty;
/// the Eyas collection (a young hawk) the same, but only in spring (a rare spring nest find).
///
/// C# and not JSON because a collection nested inside an action's ProducedCards is addressable by neither
/// GameSourceModify (top-level lists only) nor DropInjections.json (producedCards[0] only) - W0-23. The
/// pass finds the action by NAME, copies the old array and appends (never replaces), and is idempotent: a
/// repeated GameDataReady, or another mod having added a collection, adds nothing. Every exit logs its cause.
/// Each produced young bird starts at zero stats, so its drop's TransferRules ask for its full Growth.
/// </summary>
internal static class OwletNestPatch
{
    private const string BirdNestUid = "554e6bf7267da2a44b98109c7fd7d7dc";   // vanilla Event_BirdNest
    private const string ClimbActionName = "Climb";

    private const string OwletUid = "fc_owlet";
    private const string OwlCompanionUid = "fc_owl_companion";
    private const string OwletCollection = "SirusOwlet";
    // Weight against the Climb action's other collections (W0-23: about one successful climb in twenty). A balance
    // placeholder until Sirus reviews.
    private const int OwletWeight = 55;
    private const float OwletGrowth = 1920f;   // keep equal to Owlet.json's SpoilageTime MaxValue

    private const string EyasUid = "fc_eyas";
    private const string HawkCompanionUid = "fc_hawk_companion";
    private const string EyasCollection = "SirusEyas";
    // Same weight as the owlet, but the collection is cancelled outside spring (open question 14: a rare spring
    // find). A balance placeholder until Sirus reviews.
    private const int EyasWeight = 55;
    private const float EyasGrowth = 1920f;    // keep equal to Eyas.json's SpoilageTime MaxValue

    // Vanilla GameStat "SeasonCounter_Spring" (range 0..2880; its own not-enough text reads "Only available in
    // Spring."). INFERRED, not measured: that it reads 1 or more throughout spring and 0 outside it.
    private const string SpringStatUid = "6f3f0faaf50a7974e915ae0915f7e241";

    private static ManualLogSource Logger => Plugin.Logger;

    private static CardOrTagIndividualDropChanceModifier Blocker(CardData card) => new()
    {
        Card = new CardOrTagRef { Target = card },
        MaxChancePerCard = -100f,
        MaxStack = 1,
    };

    internal static void Apply()
    {
        try
        {
            var nest = CardUtil.GetCardDataById(BirdNestUid) as CardData;
            if (nest == null)
            {
                Logger?.LogWarning("[OwletNest] Event_BirdNest not loaded; no owlet or eyas will be found at nests.");
                return;
            }

            var actions = nest.DismantleActions;
            if (actions == null)
            {
                Logger?.LogWarning("[OwletNest] Event_BirdNest has no DismantleActions; no owlet or eyas will be found at nests.");
                return;
            }

            CardAction climb = null;
            foreach (var a in actions)
            {
                if (a != null && a.ActionName.DefaultText == ClimbActionName) { climb = a; break; }
            }
            if (climb == null)
            {
                Logger?.LogWarning($"[OwletNest] Event_BirdNest has no \"{ClimbActionName}\" action (a game update may have renamed it); no owlet or eyas will be found at nests.");
                return;
            }

            AddYoungBird(climb, "owlet", OwletCollection, OwletUid, OwlCompanionUid, OwletWeight, OwletGrowth, springOnly: false);
            AddYoungBird(climb, "eyas", EyasCollection, EyasUid, HawkCompanionUid, EyasWeight, EyasGrowth, springOnly: true);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[OwletNest] No owlet or eyas will be found at nests: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    /// <summary>Appends one collection that drops a young bird to the Climb action. Catches its own
    /// exceptions, so a failure on one bird cannot skip the other.</summary>
    private static void AddYoungBird(CardAction climb, string label, string collectionName, string youngUid,
        string companionUid, int weight, float growth, bool springOnly)
    {
        try
        {
            var old = climb.ProducedCards ?? Array.Empty<CardsDropCollection>();
            if (old.Any(c => c != null && c.CollectionName == collectionName))
            {
                Logger?.Log(LogLevel.Debug, $"[OwletNest] The {label} collection is already on the Climb action; nothing added.");
                return;
            }

            var young = CardUtil.GetCardDataById(youngUid) as CardData;
            var companion = CardUtil.GetCardDataById(companionUid) as CardData;
            if (young == null || companion == null)
            {
                Logger?.LogWarning($"[OwletNest] {youngUid} loaded={young != null}, {companionUid} loaded={companion != null}; no {label} will be found at nests.");
                return;
            }

            var statMods = Array.Empty<StatBasedDropChanceModifier>();
            if (springOnly)
            {
                var spring = UniqueIDScriptable.GetFromID<GameStat>(SpringStatUid);
                if (spring == null)
                {
                    Logger?.LogWarning($"[OwletNest] GameStat {SpringStatUid} (SeasonCounter_Spring) not found; the {label} cannot be limited to spring, so none will be found at nests.");
                    return;
                }
                // In range (1 to 2880) the modifier adds 0 weight; out of range CancelDrop removes the collection.
                statMods = new[]
                {
                    new StatBasedDropChanceModifier
                    {
                        Stat = spring,
                        StatRange = new Vector2(1f, 2880f),
                        InterpWeightRange = new Vector2Int(0, 0),
                        WhenOutOfRange = StatDropChanceModOutOfRange.CancelDrop,
                    },
                };
            }

            var blockers = new[] { Blocker(companion), Blocker(young) };

            var collection = new CardsDropCollection
            {
                CollectionName = collectionName,
                CountsAsSuccess = true,
                RevealInventory = false,
                CollectionUses = new Vector2Int(1, 1),
                CollectionWeight = weight,
                StatsDropChanceModifiers = statMods,
                NPCStatsDropChanceModifiers = Array.Empty<NPCStatBasedDropChanceModifier>(),
                CardDropChanceModifiers = Array.Empty<CardBasedDropChanceModifier>(),
                StatModifications = Array.Empty<ConditionalStatModifier>(),
            };

            // ctor sets Quantity = (1,1); a field-initialised CardDrop would read (0,0) and drop nothing.
            var drop = new CardDrop(young, new Vector2Int(1, 1))
            {
                DropChance = new CardDropChance
                {
                    Active = true,
                    BaseDropChance = 100f,
                    DurabilitiesModifiers = Array.Empty<DurabilityInterpolatedValue>(),
                    StatsModifiers = Array.Empty<StatInterpolatedValue>(),
                    // One bird of a kind at a time: a live companion or young bird of the same kind on the board takes
                    // this drop's 100 percent down by 100 (additive per card, one card counted), so no second one is found.
                    CardsModifiers = blockers,
                    NPCStatsModifiers = Array.Empty<NPCStatInterpolatedValue>(),
                },
                DropsDurabilityMultipliers = Array.Empty<DurabilityInterpolatedValue>(),
                DropsStatMultipliers = Array.Empty<StatInterpolatedValue>(),
                AddedFlavours = Array.Empty<FlavourAndIntensitySetup>(),
                AddedSpiceTags = Array.Empty<SpiceTag>(),
            };
            // Full Growth at birth: the drop would otherwise start at zero and the young bird would grow up at once.
            var rules = drop.TransferRules;
            rules.Spoilage = TransferToDropTypes.SetValue;
            rules.SpoilageValue = growth;
            drop.TransferRules = rules;

            var field = AccessTools.Field(typeof(CardsDropCollection), "DroppedCards");
            if (field == null)
            {
                Logger?.LogWarning($"[OwletNest] CardsDropCollection.DroppedCards field not found (a game update may have renamed it); no {label} will be found at nests.");
                return;
            }
            field.SetValue(collection, new[] { drop });

            var grown = new CardsDropCollection[old.Length + 1];
            Array.Copy(old, grown, old.Length);
            grown[old.Length] = collection;
            climb.ProducedCards = grown;
            Logger?.Log(LogLevel.Debug, $"[OwletNest] Added the {label} collection (weight {weight}{(springOnly ? ", spring only" : "")}) to Event_BirdNest's Climb ({old.Length} collections before).");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[OwletNest] No {label} will be found at nests: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }
}
