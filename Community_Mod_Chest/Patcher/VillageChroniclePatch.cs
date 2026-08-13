using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Town Hall Boards — date stamping (Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md §3.5/§10.6
    /// §4). Stamps cmcStatChronicle&lt;Structure&gt;Day = CurrentDay + 1 the first tick each of the
    /// 7 tracked village structures is found to exist (same +1-unset-sentinel idiom as
    /// VillageClock.EpochDayStatUid — 0 always means "not built yet"). Read by the Town Board's
    /// Reputation/Contribution entries and the Construction Board's per-structure status entries
    /// (both gated declaratively via RequiredStatValues on the stat itself — no runtime
    /// LocalizedString mutation, no C# string assembly).
    ///
    /// Piggybacks a single new TickEvents.Interval, same justification as QuestChainSchedulePatch's
    /// doc comment: this is a distinct concern (village-wide construction chronicle) from the
    /// existing polls (Inn dialog scheduling, cottage move-in timers, Renown recompute), so folding
    /// it into one of those would be a layering violation rather than a real poll-count saving.
    ///
    /// Structure-existence check reuses CardFinder.Find (same idiom as VillageReputationPatch,
    /// which already tracks 6 of these same 7 UIDs for the civic-total contribution formula) — deliberately
    /// consistent with that patch's established (scene-scoped) card-existence semantics rather than
    /// introducing a second, differently-scoped check for the same structures.
    /// </summary>
    internal static class VillageChroniclePatch
    {
        private sealed class Structure
        {
            public string Name;
            public string CardUid;
            public string ChronicleStatUid;
        }

        private static readonly Structure[] Structures =
        {
            new Structure { Name = "Miller's Cottage", CardUid = "cmcCottageMiller", ChronicleStatUid = "cmcStatChronicleMillerCottageDay" },
            new Structure { Name = "Weaver's Cottage", CardUid = "cmcCottageWeaver", ChronicleStatUid = "cmcStatChronicleWeaverCottageDay" },
            new Structure { Name = "Apothecary's Cabin", CardUid = "cmcApothecaryCabin", ChronicleStatUid = "cmcStatChronicleApothecaryCabinDay" },
            new Structure { Name = "Village Hall", CardUid = "cmcVillageHall", ChronicleStatUid = "cmcStatChronicleVillageHallDay" },
            new Structure { Name = "Village Well", CardUid = "cmcimpwell", ChronicleStatUid = "cmcStatChronicleWellDay" },
            new Structure { Name = "River Bridge", CardUid = "cmcimpriverbridge", ChronicleStatUid = "cmcStatChronicleRiverBridgeDay" },
            new Structure { Name = "Market Stall", CardUid = "cmcmarketstall", ChronicleStatUid = "cmcStatChronicleMarketStallDay" },
        };

        private static bool _initialized;
        private static MethodInfo _getFromIdMethod;
        private static readonly Dictionary<string, object> _statCache = new(StringComparer.Ordinal);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, RunTick, "VillageChronicle");
            Plugin.Logger.LogDebug("[VillageChroniclePatch] initialized.");
        }

        private static void RunTick()
        {
            try
            {
                if (!ResolveGetFromId()) return;
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                foreach (var structure in Structures)
                    CheckAndStamp(gm, structure);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[VillageChroniclePatch] RunTick failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void CheckAndStamp(object gm, Structure structure)
        {
            var stat = ResolveStatInstance(gm, structure.ChronicleStatUid);
            if (stat == null) return; // stat not materialized yet (boot) — try again next poll

            float day = StatAccess.GetCurrentValue(stat);
            if (float.IsNaN(day) || day >= 0.5f) return; // already stamped

            if (CardFinder.Find(structure.CardUid) == null) return; // not built yet

            float stamp = GameQuery.CurrentDay + 1;
            StatAccess.SetCurrentValue(stat, stamp);
            Plugin.Logger.LogInfo($"[VillageChroniclePatch] {structure.Name} complete — chronicled on day {GameQuery.CurrentDay} (stat {stamp}).");
        }

        private static bool ResolveGetFromId()
        {
            if (_getFromIdMethod != null) return true;
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");
            if (uidType == null) return false;
            _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            return _getFromIdMethod != null;
        }

        private static object ResolveStatInstance(object gm, string uid)
        {
            if (!_statCache.TryGetValue(uid, out var statSo))
            {
                statSo = _getFromIdMethod.Invoke(null, new object[] { uid });
                if (statSo != null) _statCache[uid] = statSo;
            }
            if (statSo == null) return null;

            if (CardUtil.GetCachedField(gm.GetType(), "StatsDict")?.GetValue(gm) is not IDictionary statsDict) return null;
            if (!statsDict.Contains(statSo)) return null;
            return statsDict[statSo];
        }
    }
}
