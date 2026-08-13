using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Village Reputation — a hidden GameStat that nets the village's civic score against the
    /// player's own crime notoriety. Renamed and extended from the former
    /// <c>VillageRenownPatch</c>/<c>cmcStatVillageRenown</c> (2026-08-12 merge): the civic-score
    /// half (<see cref="ComputeCivicTotal"/>) is unchanged from the original Renown formula;
    /// the only new logic is subtracting <see cref="VillageCrimePatch.CurrentCrime"/> from it.
    ///
    /// <see cref="VillageCrimePatch"/> itself, its `cmcStatVillageCrime` GameStat, and every
    /// Guards/Jail/Banishment consumer of that stat are UNCHANGED by this merge and keep reading
    /// `cmcStatVillageCrime` directly — merging Renown and Crime into one player-facing number
    /// does not require touching the (built-but-never-verified-in-game) Guards system at all.
    /// Crime's own 0-100 unsigned ledger still exists internally; it simply gained a second,
    /// derived consumer.
    ///
    /// Civic-score inputs read the persistent cmcStatChronicle&lt;Structure&gt;Day stats
    /// VillageChroniclePatch already maintains (day &gt;= 0.5 == built) instead of a live
    /// CardFinder scene scan. Miller's Cottage / Weaver's Cottage / Well / River Bridge are all
    /// documented as permanent, no-dismantle civic structures (README.md), so a persistent
    /// day-stamp is a faithful model, not a workaround — and it fixes a real bug the original
    /// CardFinder-based version had. CardFinder.AllCards() (CSFFModFramework.Api.CardFinder) is a
    /// live scene scan (UnityEngine.Object.FindObjectsOfType(InGameCardBase)), and decompiled
    /// GameManager.ChangeEnvironment() physically removes every regular board card from the
    /// scene the instant the player leaves that environment (AllCards.Clear() +
    /// RemoveCard(..., RemoveOption.RemoveAll)). Because RunTick fires every 5 real-time
    /// seconds regardless of player location, the old scene-scoped checks read every Village
    /// structure as "doesn't exist" the moment the player travelled away from the Village —
    /// silently resetting MarketStallPatch.RevenueMultiplier to 1x well before the Market
    /// Stall's own (location-independent) daily sale on TickEvents.DayRollover ever got to use
    /// it. The Village Hall's own Notice Board display looked correct whenever actually viewed
    /// (it only ever updates while standing at the Hall, where every other Village structure
    /// is also in-scene), which is exactly why this went unnoticed. Fixed 2026-07-27.
    ///
    /// The Market Stall's revenue-threshold contribution is different: SpecialDurability1 (its
    /// accrued Sales Revenue) is a live per-card value only readable while the stall happens to
    /// be in-scene, and isn't independently persisted by this mod. Once the threshold is
    /// observed (necessarily while the player is standing at the Village), this patch latches a
    /// permanent cmcStatQuestRenownMarketStallSales flag — the same one-shot idiom the
    /// errand/commission quest flags below already use, and consistent with
    /// VillageChroniclePatch's own one-way "built" stamp for this same card (Chronicle doesn't
    /// unstamp Market Stall if packed up either). This trades "the bonus could theoretically
    /// un-earn itself if the stall is later dismantled" (never actually enforced elsewhere in
    /// this mod, and not a README promise) for "the bonus survives being away from the
    /// Village" (the actual, constantly-hit bug it replaces).
    ///
    /// Mirrored onto Village Hall's own SpecialDurability1 bar (its Notice Board) once
    /// the Hall exists (CardFinder.Find is correct there — no reason to write a Unity
    /// durability field on a card that isn't currently instantiated), and gates a small real
    /// payoff (Market Stall revenue bonus) once full — see MarketStallPatch.RevenueMultiplier.
    /// A durability bar can't render a negative value, so the mirrored bar floors the real
    /// signed reputation at 0 for display only; every gate/threshold in this class (and every
    /// JSON consumer) still reads the true signed value.
    ///
    /// The NPC enrichment pass adds one-shot quest-completion flags (player GameStats set to 1
    /// when the Miller/Weaver/Apothecary errands and the Professor's specimen commission and
    /// Phase-P4 tasks (Field Samples, Applied Herbalism) are finished). Each contributes a flat
    /// 10 to the civic total once its flag reads &gt;= 1.0. The specimen flag is set via
    /// Bp_CMC_ProfSpecimen's BlueprintStatModifications, which can re-fire on the repeatable
    /// weekly commission; the &gt;= 1.0 test makes that harmless. The two P4 task flags are
    /// genuinely one-shot (their blueprints gate out permanently via a Claimed NPCStat once
    /// completed).
    /// </summary>
    internal static class VillageReputationPatch
    {
        private const string ReputationStatUid = "cmcStatVillageReputation";
        private const string MillerCottageChronicleUid = "cmcStatChronicleMillerCottageDay";
        private const string WeaverCottageChronicleUid = "cmcStatChronicleWeaverCottageDay";
        private const string WellChronicleUid = "cmcStatChronicleWellDay";
        private const string BridgeChronicleUid = "cmcStatChronicleRiverBridgeDay";
        private const string MarketStallUid = "cmcmarketstall";
        private const string MarketStallMilestoneUid = "cmcStatQuestRenownMarketStallSales";
        private const string VillageHallUid = "cmcVillageHall";

        private const string QuestFlagMillerGrainUid = "cmcStatQuestRenownMillerGrain";
        private const string QuestFlagWeaverFlaxUid = "cmcStatQuestRenownWeaverFlax";
        private const string QuestFlagApothecaryHerbsUid = "cmcStatQuestRenownApothecaryHerbs";
        private const string QuestFlagProfSpecimenUid = "cmcStatQuestRenownProfSpecimen";
        private const string QuestFlagProfFieldSamplesUid = "cmcStatQuestRenownProfFieldSamples";
        private const string QuestFlagProfAppliedHerbalismUid = "cmcStatQuestRenownProfAppliedHerbalism";

        private const float MillerContribution = 25f;
        private const float WeaverContribution = 25f;
        private const float WellContribution = 15f;
        private const float BridgeContribution = 15f;
        private const float MarketStallContribution = 20f;
        private const float MarketStallRevenueThreshold = 240f; // half of Market Stall's 480 SD1 max

        private const float QuestFlagContribution = 10f; // each completed errand/commission adds this

        private const float CivicTotalMax = 100f;
        private const float ReputationMin = -100f;
        private const float ReputationMax = 100f;

        // Market Stall bonus threshold — unchanged absolute value from the old FullRenown
        // constant, but now requires a maxed civic total AND zero crime to actually reach 100 net.
        private const float FullReputation = 100f;

        private static bool _initialized;
        private static Type _uidType;
        private static MethodInfo _getFromIdMethod;
        private static object _reputationStat;
        private static object _millerCottageChronicleStat;
        private static object _weaverCottageChronicleStat;
        private static object _wellChronicleStat;
        private static object _bridgeChronicleStat;
        private static object _marketStallMilestoneStat;
        private static object _questFlagMillerGrainStat;
        private static object _questFlagWeaverFlaxStat;
        private static object _questFlagApothecaryHerbsStat;
        private static object _questFlagProfSpecimenStat;
        private static object _questFlagProfFieldSamplesStat;
        private static object _questFlagProfAppliedHerbalismStat;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, RunTick, "VillageReputation");
            Plugin.Logger.LogDebug("[VillageReputationPatch] initialized.");
        }

        /// <summary>
        /// Current net Reputation value, or <see cref="float.NaN"/> when the stat is not yet
        /// readable (SO missing, StatsDict not built). Unlike <see cref="VillageCrimePatch.CurrentCrime"/>'s
        /// 0-default graceful degradation, 0 is a legitimate middle value for Reputation —
        /// callers must check <see cref="float.IsNaN(float)"/> rather than treat a read failure
        /// as "neutral." Public so other display code (e.g. VillageHallBoardsPatch) doesn't
        /// duplicate the StatsDict resolve dance.
        /// </summary>
        public static float CurrentReputation()
        {
            if (!ResolveTypes()) return float.NaN;
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return float.NaN;
            var statInstance = ResolveStatInstance(gm);
            if (statInstance == null) return float.NaN;
            return StatAccess.GetCurrentValue(statInstance);
        }

        private static bool ResolveTypes()
        {
            if (_getFromIdMethod != null) return true;

            _uidType = CardUtil.FindGameType("UniqueIDScriptable");
            if (_uidType == null) return false;

            _getFromIdMethod = _uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            return _getFromIdMethod != null;
        }

        private static void RunTick()
        {
            try
            {
                if (!ResolveTypes()) return;
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                var statInstance = ResolveStatInstance(gm);
                if (statInstance == null) return;

                // Clamp the civic total to its old intended 0-100 cap BEFORE subtracting crime.
                // The pre-merge Renown sum was never explicitly clamped (structures + all 6
                // quest flags can total up to 160 raw) — harmless before because every consumer
                // read through the engine's own MinMaxValue-clamped SimpleCurrentValue, but
                // clamping only at the end here would let up to 60 points of crime hide behind
                // an unclamped civic overage with zero visible effect on the net value.
                float civicTotal = Math.Min(CivicTotalMax, Math.Max(0f, ComputeCivicTotal(gm)));
                float crime = VillageCrimePatch.CurrentCrime();
                float reputation = Math.Min(ReputationMax, Math.Max(ReputationMin, civicTotal - crime));
                StatAccess.SetCurrentValue(statInstance, reputation);

                var hall = CardFinder.Find(VillageHallUid);
                if (hall != null) CardUtil.SetDurability(hall, "SpecialDurability1", Math.Max(0f, reputation));

                MarketStallPatch.RevenueMultiplier = reputation >= FullReputation ? MarketStallPatch.FullRenownRevenueMultiplier : 1f;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[VillageReputationPatch] RunTick failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static object ResolveStatInstance(object gm)
        {
            _reputationStat ??= _getFromIdMethod.Invoke(null, new object[] { ReputationStatUid });
            if (_reputationStat == null) return null;

            if (CardUtil.GetCachedField(gm.GetType(), "StatsDict")?.GetValue(gm) is not IDictionary statsDict) return null;
            if (!statsDict.Contains(_reputationStat)) return null;
            return statsDict[_reputationStat];
        }

        /// <summary>The civic half of Reputation — unchanged from the pre-merge Renown formula.</summary>
        private static float ComputeCivicTotal(object gm)
        {
            float total = 0f;
            if (GetStatValue(gm, ref _millerCottageChronicleStat, MillerCottageChronicleUid) >= 0.5f) total += MillerContribution;
            if (GetStatValue(gm, ref _weaverCottageChronicleStat, WeaverCottageChronicleUid) >= 0.5f) total += WeaverContribution;
            if (GetStatValue(gm, ref _wellChronicleStat, WellChronicleUid) >= 0.5f) total += WellContribution;
            if (GetStatValue(gm, ref _bridgeChronicleStat, BridgeChronicleUid) >= 0.5f) total += BridgeContribution;

            if (MarketStallMilestoneReached(gm)) total += MarketStallContribution;

            // One-shot quest-completion flags (player GameStats). Each finished errand/commission
            // reads >= 1.0 and adds a flat contribution. >= (not ==) so a repeatable commission
            // whose BlueprintStatModifications re-fires past 1.0 stays a single 10-point award.
            if (GetStatValue(gm, ref _questFlagMillerGrainStat, QuestFlagMillerGrainUid) >= 1.0f) total += QuestFlagContribution;
            if (GetStatValue(gm, ref _questFlagWeaverFlaxStat, QuestFlagWeaverFlaxUid) >= 1.0f) total += QuestFlagContribution;
            if (GetStatValue(gm, ref _questFlagApothecaryHerbsStat, QuestFlagApothecaryHerbsUid) >= 1.0f) total += QuestFlagContribution;
            if (GetStatValue(gm, ref _questFlagProfSpecimenStat, QuestFlagProfSpecimenUid) >= 1.0f) total += QuestFlagContribution;
            if (GetStatValue(gm, ref _questFlagProfFieldSamplesStat, QuestFlagProfFieldSamplesUid) >= 1.0f) total += QuestFlagContribution;
            if (GetStatValue(gm, ref _questFlagProfAppliedHerbalismStat, QuestFlagProfAppliedHerbalismUid) >= 1.0f) total += QuestFlagContribution;

            return total;
        }

        /// <summary>
        /// True once the Market Stall's Sales Revenue (SpecialDurability1) has ever been
        /// observed at or above <see cref="MarketStallRevenueThreshold"/>. The stall's
        /// durability value can only be read while it happens to be in-scene (the player
        /// standing at the Village), so the first observation latches a permanent
        /// cmcStatQuestRenownMarketStallSales flag and every later tick — from anywhere — reads
        /// that flag instead of re-scanning the scene.
        /// </summary>
        private static bool MarketStallMilestoneReached(object gm)
        {
            if (GetStatValue(gm, ref _marketStallMilestoneStat, MarketStallMilestoneUid) >= 1.0f) return true;
            if (_marketStallMilestoneStat == null) return false; // stat not registered yet — retry next tick

            var stall = CardFinder.Find(MarketStallUid);
            if (stall == null || CardUtil.GetDurability(stall, "SpecialDurability1") < MarketStallRevenueThreshold) return false;

            if (CardUtil.GetCachedField(gm.GetType(), "StatsDict")?.GetValue(gm) is not IDictionary statsDict) return false;
            if (!statsDict.Contains(_marketStallMilestoneStat)) return false;
            StatAccess.SetCurrentValue(statsDict[_marketStallMilestoneStat], 1f);
            return true;
        }

        /// <summary>
        /// Reads a hidden GameStat's current value by UID, resolving and caching the stat
        /// instance the same way <see cref="ResolveStatInstance"/> does for the main Reputation
        /// stat. Returns 0 if the stat is not yet registered/present so a missing or not-yet-
        /// built flag simply contributes nothing. Used for both the one-shot errand/commission
        /// quest flags and the persistent structure-completion Chronicle stats.
        /// </summary>
        private static float GetStatValue(object gm, ref object cachedStat, string uid)
        {
            cachedStat ??= _getFromIdMethod.Invoke(null, new object[] { uid });
            if (cachedStat == null) return 0f;

            if (CardUtil.GetCachedField(gm.GetType(), "StatsDict")?.GetValue(gm) is not IDictionary statsDict) return 0f;
            if (!statsDict.Contains(cachedStat)) return 0f;
            var inGameStat = statsDict[cachedStat];
            if (inGameStat == null) return 0f;
            return StatAccess.GetCurrentValue(inGameStat);
        }
    }
}
