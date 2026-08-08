using System;
using System.Collections.Generic;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Injects an enhanced "Fish" CardInteraction onto all minnow-type river/water cards
    /// that triggers ONLY on the CMC Iron Fishing Rod, giving it a meaningful catch table:
    ///   Nothing  4000  (flat)
    ///   Minnow   7000  (flat)
    ///   Crayfish    0  + Pop_Crayfish(0-100 -> 1000-5000, Add0Weight) - always catchable
    ///   Perch       0  + Pop_Perch(10-100 -> 1000-6000, CancelDrop)  - requires pop>=10
    ///   Eel         0  + Pop_Eel(10-100 -> 500-3000,  CancelDrop)    - requires pop>=10
    ///
    /// The vanilla Fish CI is modified (via reflection) to exclude the Iron Rod via
    /// NOTCompatibleCards, preventing duplicate "Fish" buttons.
    ///
    /// Academy Fishing-degree bonus: the Fishing course's Final Exam grants
    /// cmcperkgradfishing (see AcademyPatch/AcademyCourseService). Rather than cloning a
    /// second "Fish" CI (which would re-create the exact duplicate-button problem this
    /// file already fixes for vanilla-vs-Iron-Rod), each water card's Iron Rod CI shares
    /// ONE drop-table array instance; an ActionRouter Before-hook mutates that shared
    /// table's weights in place, immediately before each roll, based on whether the
    /// degree is held — lower tiers (Nothing/Minnow) are suppressed and higher tiers
    /// (Perch/Eel/Crayfish) are boosted and their population gate is lowered.
    /// </summary>
    internal static class IronRodFishingPatch
    {
        private const string IronRodUid   = "CMCIronFishingRod";
        private const string MinnowGuid   = "992cc6a7e92f4c5438c766327a3cbfcb";
        private const string CrayfishGuid = "4d29495d7467aec4b87ac424b72a3d4b";
        private const string PerchGuid    = "e64237c9a44ea3945bc75b8b9f4735c0";
        private const string EelGuid      = "bb8a42f395c31894188ff3038d76128f";

        private const string PopCrayfishGuid = "478a22eb24d03244fa9c31911175eb6c";
        private const string PopPerchGuid    = "06a9b3a507f78374084b51efdfb5f02c";
        private const string PopEelGuid      = "800a4ffb6dc941c45b805472d3ad785d";

        // All river/water cards that have the full minnow-based fish table.
        private static readonly string[] FishingWaterUids =
        {
            "f6ba7b43e3e920b49a84510ca27f527c", // River
            "1e51165d981794c40bfceb3509391ace", // IceHoleRiver
            "e476c2de086e43748857c00465382020", // RiverBog
            "abbcffdf3cc2b7345b26bbbf9b92b514", // RiverLake
            "9f4f948fddabac94a9614bfd67be7bbd", // RiverMarsh
            "36f4783eb36b4e44b946a73bffc37cee", // RiverMire
            "476b7ac3916a37447a26e76b5758d4ee", // RiverSwamp
        };

        // NOTCompatibleCards is public in the game DLL but absent from the NStrip stub — use reflection.
        private static FieldInfo _notCompatField;

        // Every water card's injected Iron Rod CI, so the graduate-boost hook can
        // update all of them together.
        private static readonly List<CardOnCardAction> IronRodCis = new List<CardOnCardAction>();

        public static void Register()
        {
            try
            {
                // Resolve NOTCompatibleCards once here; the field may be absent in some stub configs.
                _notCompatField = typeof(CardOnCardAction).GetField("NOTCompatibleCards",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (_notCompatField == null)
                    Plugin.Logger.LogError("[CMCFishing] NOTCompatibleCards not found on CardOnCardAction — players may see two 'Fish' buttons with the Iron Rod.");

                FrameworkEvents.GameDataReady += OnGameDataReady;
                RegisterGraduateBoostHook();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[CMCFishing] Patch setup failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Academy Fishing-degree bonus ────────────────────────────────────────

        private static void RegisterGraduateBoostHook()
        {
            ActionRouter.Register(new ActionHandler
            {
                Name          = "CMCFishingGraduateBoost",
                CardPredicate = ctx => Array.IndexOf(FishingWaterUids, ctx.CardUid) >= 0
                                     && string.Equals(ctx.ActionName, "Fish", StringComparison.Ordinal)
                                     && string.Equals(CardUtil.GetCardUniqueId(ctx.GivenCard), IronRodUid, StringComparison.Ordinal),
                Timing = ActionTiming.Before,
                Before = ApplyGraduateTierWeights,
            });
        }

        /// <summary>Runs immediately before every Iron Rod "Fish" roll. Mutates the
        /// shared drop-table collections in place (all water cards' Iron Rod CIs point
        /// at the same array instances) so the boost applies everywhere at once, the
        /// moment the degree is earned — no separate CI/button needed.</summary>
        private static bool ApplyGraduateTierWeights(ActionContext ctx)
        {
            bool graduated = AcademyCourseService.HasCourse(AcademyCourseService.GradFishing);
            foreach (var ci in IronRodCis)
            {
                if (ci?.ProducedCards == null) continue;
                foreach (var collection in ci.ProducedCards)
                {
                    if (collection == null) continue;
                    switch (collection.CollectionName)
                    {
                        case "Nothing":
                            collection.CollectionWeight = graduated ? 2500 : 4000;
                            break;
                        case "Minnow":
                            collection.CollectionWeight = graduated ? 5000 : 7000;
                            break;
                        case "Crayfish":
                            if (collection.StatsDropChanceModifiers.Length > 0)
                                collection.StatsDropChanceModifiers[0].InterpWeightRange =
                                    graduated ? new Vector2Int(1500, 6000) : new Vector2Int(1000, 5000);
                            break;
                        case "Perch":
                            if (collection.StatsDropChanceModifiers.Length > 0)
                            {
                                collection.StatsDropChanceModifiers[0].StatRange =
                                    graduated ? new Vector2(5f, 100f) : new Vector2(10f, 100f);
                                collection.StatsDropChanceModifiers[0].InterpWeightRange =
                                    graduated ? new Vector2Int(1500, 8000) : new Vector2Int(1000, 6000);
                            }
                            break;
                        case "Eel":
                            if (collection.StatsDropChanceModifiers.Length > 0)
                            {
                                collection.StatsDropChanceModifiers[0].StatRange =
                                    graduated ? new Vector2(5f, 100f) : new Vector2(10f, 100f);
                                collection.StatsDropChanceModifiers[0].InterpWeightRange =
                                    graduated ? new Vector2Int(800, 4500) : new Vector2Int(500, 3000);
                            }
                            break;
                    }
                }
            }
            return true;
        }

        private static void OnGameDataReady()
        {
            try
            {
                var ironRodCard  = UniqueIDScriptable.GetFromID<CardData>(IronRodUid);
                if (ironRodCard == null) { Plugin.Logger.LogError($"[CMCFishing] Iron Rod ({IronRodUid}) not found."); return; }

                var minnowCard   = UniqueIDScriptable.GetFromID<CardData>(MinnowGuid);
                var crayfishCard = UniqueIDScriptable.GetFromID<CardData>(CrayfishGuid);
                var perchCard    = UniqueIDScriptable.GetFromID<CardData>(PerchGuid);
                var eelCard      = UniqueIDScriptable.GetFromID<CardData>(EelGuid);
                var popCrayfish  = UniqueIDScriptable.GetFromID<GameStat>(PopCrayfishGuid);
                var popPerch     = UniqueIDScriptable.GetFromID<GameStat>(PopPerchGuid);
                var popEel       = UniqueIDScriptable.GetFromID<GameStat>(PopEelGuid);

                if (minnowCard == null)   Plugin.Logger.LogWarning($"[CMCFishing] Minnow ({MinnowGuid}) not found — dropped from the Iron Rod catch table.");
                if (crayfishCard == null) Plugin.Logger.LogWarning($"[CMCFishing] Crayfish ({CrayfishGuid}) not found — dropped from the Iron Rod catch table.");
                if (perchCard == null)    Plugin.Logger.LogWarning($"[CMCFishing] Perch ({PerchGuid}) not found — dropped from the Iron Rod catch table.");
                if (eelCard == null)      Plugin.Logger.LogWarning($"[CMCFishing] Eel ({EelGuid}) not found — dropped from the Iron Rod catch table.");
                if (popCrayfish == null)  Plugin.Logger.LogWarning($"[CMCFishing] Pop_Crayfish stat ({PopCrayfishGuid}) not found — Crayfish population gate disabled.");
                if (popPerch == null)     Plugin.Logger.LogWarning($"[CMCFishing] Pop_Perch stat ({PopPerchGuid}) not found — Perch population gate disabled.");
                if (popEel == null)       Plugin.Logger.LogWarning($"[CMCFishing] Pop_Eel stat ({PopEelGuid}) not found — Eel population gate disabled.");

                int injected = 0;

                foreach (var waterUid in FishingWaterUids)
                {
                    var waterCard = UniqueIDScriptable.GetFromID<CardData>(waterUid);
                    if (waterCard == null) continue;

                    var fishCI = FindCIByName(waterCard, "Fish");
                    if (fishCI == null) continue;

                    // Idempotency: SO mutations persist for the process lifetime.
                    if (AlreadyInjected(waterCard)) continue;

                    // 1. Restrict vanilla Fish CI so it no longer triggers on the Iron Rod.
                    //    This prevents a duplicate "Fish" button appearing alongside our CI.
                    if (_notCompatField != null)
                    {
                        var notCompat = (CardInteractionTrigger)_notCompatField.GetValue(fishCI);
                        if (notCompat.TriggerCards == null || notCompat.TriggerCards.Length == 0)
                        {
                            notCompat.TriggerCards = new CardData[] { ironRodCard };
                            notCompat.TriggerTags  = Array.Empty<CardTag>();
                            _notCompatField.SetValue(fishCI, notCompat);
                        }
                    }

                    // 2. Create the Iron Rod CI by cloning the vanilla Fish CI via JsonUtility
                    //    (same mechanism as CardOnCardAction.Copy()). Value-type fields survive;
                    //    SO refs are nulled and repaired below.
                    var ironCI = JsonUtility.FromJson<CardOnCardAction>(JsonUtility.ToJson(fishCI));

                    // CompatibleCards: trigger ONLY on the Iron Rod (not via tag).
                    ironCI.CompatibleCards = new CardInteractionTrigger
                    {
                        TriggerCards = new CardData[] { ironRodCard },
                        TriggerTags  = Array.Empty<CardTag>()
                    };

                    // NOTCompatibleCards on new CI: empty (no exclusions).
                    if (_notCompatField != null)
                        _notCompatField.SetValue(ironCI, new CardInteractionTrigger
                        {
                            TriggerCards = Array.Empty<CardData>(),
                            TriggerTags  = Array.Empty<CardTag>()
                        });

                    // Stat modifications: borrow the original's array (live SO refs from WarpResolver).
                    ironCI.StatModifications = fishCI.StatModifications;

                    // Action tags: borrow the original's array.
                    ironCI.ActionTags = fishCI.ActionTags;

                    // Drop table: built from scratch — no Copy() needed.
                    ironCI.ProducedCards = BuildDropTable(
                        minnowCard, crayfishCard, perchCard, eelCard,
                        popCrayfish, popPerch, popEel);

                    // 3. Append to this water card's CardInteractions.
                    var existing = waterCard.CardInteractions;
                    var next = new CardOnCardAction[existing.Length + 1];
                    Array.Copy(existing, next, existing.Length);
                    next[existing.Length] = ironCI;
                    waterCard.CardInteractions = next;
                    IronRodCis.Add(ironCI);
                    injected++;
                }

                if (injected > 0)
                    Plugin.Logger.LogInfo($"[CMCFishing] Iron Rod enhanced Fish CI injected into {injected} water card(s).");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"[CMCFishing] {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool AlreadyInjected(CardData waterCard)
        {
            if (waterCard.CardInteractions == null) return false;
            foreach (var ci in waterCard.CardInteractions)
            {
                if (ci?.CompatibleCards.TriggerCards == null) continue;
                foreach (var tc in ci.CompatibleCards.TriggerCards)
                    if (tc != null && string.Equals(tc.UniqueID, IronRodUid, StringComparison.Ordinal))
                        return true;
            }
            return false;
        }

        // LocalizedString is a struct — access .DefaultText directly, no null-conditional.
        private static CardOnCardAction FindCIByName(CardData card, string actionName)
        {
            if (card.CardInteractions == null) return null;
            foreach (var ci in card.CardInteractions)
            {
                if (ci == null) continue;
                if (string.Equals(ci.ActionName.DefaultText, actionName, StringComparison.OrdinalIgnoreCase))
                    return ci;
            }
            return null;
        }

        private static CardsDropCollection[] BuildDropTable(
            CardData minnow, CardData crayfish, CardData perch, CardData eel,
            GameStat popCrayfish, GameStat popPerch, GameStat popEel)
        {
            var result = new List<CardsDropCollection>();

            // Nothing: flat 4000 weight (no skill mods — the rod is simply better overall).
            result.Add(new CardsDropCollection
            {
                CollectionName   = "Nothing",
                CollectionWeight = 4000,
                CollectionUses   = new Vector2Int(-1, -1),
                DroppedCards     = Array.Empty<CardDrop>(),
                StatsDropChanceModifiers = Array.Empty<StatBasedDropChanceModifier>()
            });

            // Minnow: flat 7000 weight.
            if (minnow != null)
                result.Add(new CardsDropCollection
                {
                    CollectionName   = "Minnow",
                    CollectionWeight = 7000,
                    CollectionUses   = new Vector2Int(-1, -1),
                    DroppedCards     = new[] { new CardDrop { DroppedCard = minnow, Quantity = new Vector2Int(1, 1) } },
                    StatsDropChanceModifiers = Array.Empty<StatBasedDropChanceModifier>()
                });

            // Crayfish: always catchable at any pop (Add0Weight when OOR means at pop=0, adds 0,
            // so FinalWeight=0 which is excluded; at pop=1+, scales 1000→5000).
            if (crayfish != null && popCrayfish != null)
                result.Add(MakePopCollection("Crayfish", crayfish, popCrayfish,
                    0f, 100f, 1000, 5000, StatDropChanceModOutOfRange.Add0Weight));

            // Perch: excluded below pop=10 (CancelDrop adds -1000000); scales 1000→6000 above.
            if (perch != null && popPerch != null)
                result.Add(MakePopCollection("Perch", perch, popPerch,
                    10f, 100f, 1000, 6000, StatDropChanceModOutOfRange.CancelDrop));

            // Eel: same pop gate, lower ceiling.
            if (eel != null && popEel != null)
                result.Add(MakePopCollection("Eel", eel, popEel,
                    10f, 100f, 500, 3000, StatDropChanceModOutOfRange.CancelDrop));

            return result.ToArray();
        }

        private static CardsDropCollection MakePopCollection(
            string name, CardData fish, GameStat popStat,
            float statMin, float statMax, int interpMin, int interpMax,
            StatDropChanceModOutOfRange oor)
        {
            return new CardsDropCollection
            {
                CollectionName   = name,
                CollectionWeight = 0,
                CollectionUses   = new Vector2Int(-1, -1),
                DroppedCards     = new[] { new CardDrop { DroppedCard = fish, Quantity = new Vector2Int(1, 1) } },
                StatsDropChanceModifiers = new[]
                {
                    new StatBasedDropChanceModifier
                    {
                        Stat              = popStat,
                        StatRange         = new Vector2(statMin, statMax),
                        InterpWeightRange = new Vector2Int(interpMin, interpMax),
                        WhenOutOfRange    = oor
                    }
                }
            };
        }
    }
}
