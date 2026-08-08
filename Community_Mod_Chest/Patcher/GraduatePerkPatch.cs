using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// "Graduate" perk (cmcperkgraduate) — grants all 6 Academy graduate perks directly at
    /// run start and backfills the Lecture Hall's course-progress stats to match, so the
    /// player carries a full Academy education without ever visiting the Academy.
    /// Split out of VillageFounderPerkPatch (2026-07-23) — Village Founder previously bundled
    /// this grant alongside its village-building content, but the two are independent
    /// character concepts; a player may want either without the other.
    ///
    /// Mechanism: identical idioms to VillageFounderPerkPatch — GameStat StatsDict
    /// SimpleCurrentValue/CurrentBaseValue read/write, InRunAddedPerks-list-append +
    /// ApplyPerk grant, and an env-arrival poll (mirrors CheckStructurePlacement) because
    /// the lectern's live in-game instance only exists once the player has stood in the
    /// Academy interior (AllCards is current-env-scoped).
    /// </summary>
    internal static class GraduatePerkPatch
    {
        private const string PerkUid = "cmcperkgraduate";
        private const string AppliedStatUid = "cmcStatGraduateApplied";
        private const string PlacedStatUid = "cmcStatGraduatePlaced";
        private const string AcademyInteriorEnvUid = "cmcAcademyInterior";
        private const string AcademyLecternUid = "cmcAcademyLectern";

        private static readonly string[] GradPerkUids =
        {
            "cmcperkgradmetallurgy",
            "cmcperkgradherbalism",
            "cmcperkgradmedicine",
            "cmcperkgradfishing",
            "cmcperkgradarchitecture",
            "cmcperkgradarmorer",
        };

        // Mirrors AcademyPatch.Courses — the lectern's course-progress stat + max hours for
        // each of the 6 graduate perks granted above. Without this the lectern's progress
        // stays at its JSON default of 0 while AcademyCourseService.HasCourse already
        // reports the course as done — every "Study ..." button then permanently shows
        // "You have already completed this course" at 0% progress (the same desync
        // originally found in Village Founder, confirmed in-game 2026-07-23).
        private static readonly (string StatName, float TotalHours)[] CourseStats =
        {
            ("SpecialDurability1", 72f), // Architecture
            ("SpecialDurability2", 60f), // Metallurgy
            ("SpecialDurability3", 30f), // Herbalism
            ("SpoilageTime",       24f), // Fishing
            ("UsageDurability",    24f), // Armorer
            ("FuelCapacity",       24f), // Medicine
        };

        private static bool _initialized;
        private static MethodInfo _getFromIdMethod;
        private static readonly Dictionary<string, object> _statCache = new(StringComparer.Ordinal);

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            TickEvents.Interval(3f, TryApply, "GraduatePerkApply");
            TickEvents.Interval(1f, CheckAcademyBackfill, "GraduatePerkAcademyBackfill");
            Plugin.Logger.LogDebug("[GraduatePerkPatch] initialized.");
        }

        private static void TryApply()
        {
            try
            {
                if (!CardUtil.IsPerkEquipped(PerkUid)) return;
                if (!ResolveGetFromId()) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                if (ReadStat(gm, AppliedStatUid) >= 0.5f) return; // already applied this save

                foreach (var perkUid in GradPerkUids)
                    GrantInRunPerk(gm, perkUid);

                WriteStat(gm, AppliedStatUid, 1f);
                Plugin.Logger.LogInfo("[GraduatePerkPatch] Graduate perk applied — all 6 Academy graduate perks granted.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GraduatePerkPatch] TryApply failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Backfills the lectern's 6 course-progress stats to their max hours the first
        // time the player stands in the Academy interior (the lectern's live in-game
        // instance only exists once that board has been seeded — AllCards is
        // current-env-scoped).
        private static void CheckAcademyBackfill()
        {
            try
            {
                if (!CardUtil.IsPerkEquipped(PerkUid)) return;
                if (!ResolveGetFromId()) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;
                if (ReadStat(gm, AppliedStatUid) < 0.5f) return; // grant hasn't run yet
                if (ReadStat(gm, PlacedStatUid) >= 0.5f) return; // already backfilled

                if (GameQuery.CurrentEnvironmentUniqueId != AcademyInteriorEnvUid) return;

                var lectern = FindLiveCard(gm, AcademyLecternUid);
                if (lectern == null) return; // board not seeded yet — retry next tick

                foreach (var (statName, totalHours) in CourseStats)
                {
                    if (!CardUtil.SetDurability(lectern, statName, totalHours))
                        Plugin.Logger.LogWarning($"[GraduatePerkPatch] Could not backfill Academy stat '{statName}' on the lectern.");
                }
                CardVisualsRefresh.RefreshDurabilityVisuals(lectern);
                CardVisualsRefresh.RefreshOpenInventoryPopup();

                if (WriteStat(gm, PlacedStatUid, 1f))
                    Plugin.Logger.LogInfo("[GraduatePerkPatch] Academy course progress backfilled to match the granted graduate perks.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GraduatePerkPatch] CheckAcademyBackfill failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── GameStat access — identical idiom to VillageFounderPerkPatch ──

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

        private static object StatRef(string uid)
        {
            if (_statCache.TryGetValue(uid, out var cached)) return cached;
            var stat = _getFromIdMethod.Invoke(null, new object[] { uid });
            if (stat != null) _statCache[uid] = stat;
            return stat;
        }

        private static float ReadStat(object gm, string uid)
        {
            var stat = StatRef(uid);
            if (stat == null) return -1f;
            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return -1f;
            if (!statsDict.Contains(stat)) return -1f;
            var inGameStat = statsDict[stat];
            if (inGameStat == null) return -1f;
            return Reflect.GetMember(inGameStat, "SimpleCurrentValue") is float f ? f : -1f;
        }

        private static bool WriteStat(object gm, string uid, float value)
        {
            var stat = StatRef(uid);
            if (stat == null) return false;
            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return false;
            if (!statsDict.Contains(stat)) return false;
            var inGameStat = statsDict[stat];
            if (inGameStat == null) return false;
            return Reflect.SetMember(inGameStat, "CurrentBaseValue", value);
        }

        // Returns the live in-game instance of the card with the given UID on the
        // CURRENT board, or null if not present there (AllCards is current-env-scoped).
        private static object FindLiveCard(object gm, string uid)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return null;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                if (CardUtil.GetCardUniqueId(card) == uid) return card;
            }
            return null;
        }

        // ── In-run perk grant — proven pattern (Sirus23 CompanionHuntPatch.GrantPackBondPerk) ──

        private static void GrantInRunPerk(object gm, string perkUid)
        {
            try
            {
                var perkSo = _getFromIdMethod.Invoke(null, new object[] { perkUid });
                if (perkSo == null)
                {
                    Plugin.Logger.LogWarning($"[GraduatePerkPatch] Perk '{perkUid}' not found in AllData.");
                    return;
                }

                if (Reflect.GetMember(gm, "InRunAddedPerks") is not IList inRunList)
                {
                    Plugin.Logger.LogWarning("[GraduatePerkPatch] GameManager.InRunAddedPerks not found — grad perks not granted.");
                    return;
                }

                foreach (var existing in inRunList)
                    if (ReferenceEquals(existing, perkSo)) return; // already granted

                inRunList.Add(perkSo);

                var applyMethod = gm.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "ApplyPerk");
                if (applyMethod != null)
                {
                    var ps = applyMethod.GetParameters();
                    object[] callArgs = ps.Length == 3 ? new object[] { perkSo, true, null } : new object[] { perkSo };
                    applyMethod.Invoke(gm, callArgs);
                }
                Plugin.Logger.LogDebug($"[GraduatePerkPatch] Granted grad perk '{perkUid}'.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GraduatePerkPatch] GrantInRunPerk('{perkUid}') failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
