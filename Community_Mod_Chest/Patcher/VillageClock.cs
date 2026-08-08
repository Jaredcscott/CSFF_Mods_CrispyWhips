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
    /// The Village Clock (Documentation/Design/Village_Event_Timeline_Plan.md §2/§4,
    /// Village_Master_Plan.md §3.1–3.2).
    ///
    /// Owns the hidden player GameStats the village timeline runs on:
    ///   - cmcStatVillagePhase    — 0 = undiscovered, 1 = introduced. Set by the final line of the
    ///                              Inn Keeper's intro conversation (JSON ActionToPerform); backfilled
    ///                              here for pre-rework saves that show interaction evidence.
    ///   - cmcStatVillageEpochDay — CurrentDay + 1, stamped ONCE on first observing phase >= 1
    ///                              (the +1 offset keeps 0 = unset).
    ///   - cmcStatVillageWeek     — ((CurrentDay - (epoch - 1)) / 7) + 1, recomputed every poll —
    ///                              derived and reload-safe, 0 until the intro is done.
    ///   - cmcStatInnOnboardStep  — anti-collapse beat sequencer (JSON gates advance it); this class
    ///                              only backfills it to 2 when a pre-rework save already has the
    ///                              Well Plans (the step-2 beat's own reward).
    ///   - cmcStatInnTraitWelcomeGiven — 0/1 latch for the CMC_InnKeeperTalk_TraitWelcome line.
    ///                              Set by that line's own JSON ActionToPerform, by the normal
    ///                              Intro conversation's closing line, and by the pre-rework-save
    ///                              backfill here — but deliberately left UNSET when phase is
    ///                              backfilled purely from the Village Inn trait, so a trait
    ///                              player still gets one welcome line instead of landing cold on
    ///                              whatever week/errand beat the phase gate opens next.
    ///
    /// Tick() piggybacks the EXISTING InnKeeperDialogSchedulePatch 5 s poll — this class deliberately
    /// registers no TickEvents.Interval of its own (CLAUDE.md: no new polls). Other patchers gate on
    /// PhaseAtLeast()/WeeksSinceIntro().
    ///
    /// Graceful degradation (feedback_subsystem_graceful_degradation): if the phase stat cannot be
    /// resolved — mod stat JSON failed to load, StatsDict unavailable — C# gates treat the village as
    /// OPEN. That matches the JSON side: GeneralCondition skips a RequiredStatValues entry whose Stat
    /// reference is null, so the dialog gates fall open too. A broken stat must never brick the
    /// Professor/Academy harder than the dialog would be.
    /// </summary>
    internal static class VillageClock
    {
        internal const string PhaseStatUid = "cmcStatVillagePhase";
        internal const string EpochDayStatUid = "cmcStatVillageEpochDay";
        internal const string WeekStatUid = "cmcStatVillageWeek";
        internal const string OnboardStepStatUid = "cmcStatInnOnboardStep";
        internal const string TraitWelcomeGivenStatUid = "cmcStatInnTraitWelcomeGiven";

        // Save back-compat evidence (Event Timeline plan §4, amended): any of these >= 1 on a
        // pre-rework save proves prior village PROGRESS — never re-lock such a run behind the
        // intro. Deliberate deviation from the plan's draft list: cmcStatInnFriendship is NOT
        // evidence, because the Chat DA stays available at phase 0 by design and grants +1
        // friendship per chat — counting it would let a single Chat click on a fresh run skip
        // the whole intro. Post-rework, Well Plans (week-3 beat) and every quest-renown flag
        // are themselves phase-gated, so none of the remaining evidence can fire spuriously.
        // A lightly-engaged old save (chatted a few times, nothing else) simply gets the
        // 60-second intro once — friendship itself is preserved. The Village Inn trait's
        // "introductions waived" head start is handled separately in Tick() via an explicit
        // perk check (CardUtil.IsPerkEquipped(VillageInnPerkUid)), NOT folded into this evidence
        // list, so it can leave TraitWelcomeGivenStatUid unset (see class doc).
        private const string VillageInnPerkUid = "cmcperkvillageinn";
        private const string WellPlansGivenStatUid = "cmcStatInnWellPlansGiven";
        private static readonly string[] QuestRenownStatUids =
        {
            "cmcStatQuestRenownMillerGrain",
            "cmcStatQuestRenownWeaverFlax",
            "cmcStatQuestRenownApothecaryHerbs",
            "cmcStatQuestRenownProfSpecimen",
        };

        private static MethodInfo _getFromIdMethod;
        private static readonly Dictionary<string, object> _statCache = new(StringComparer.Ordinal);
        private static bool _loggedPhaseUnreadable;

        // ── The poll body (called from InnKeeperDialogSchedulePatch.RunSchedule) ──

        /// <summary>Backfill phase on evidence, stamp the epoch, recompute the week. Cheap:
        /// three stat reads on settled runs, a few more only while phase is still 0.</summary>
        public static void Tick(object gm)
        {
            if (gm == null || !ResolveGetFromId()) return;

            float phase = ReadStat(gm, PhaseStatUid);
            if (phase < 0f) return; // stats not materialized yet (boot) — try again next poll

            // 1. Backfill: village progress evidence (pre-rework save) or the Village Inn trait
            //    (introductions waived) with phase still 0 → phase 1, no intro replay.
            if (phase < 0.5f)
            {
                if (HasPreReworkEvidence(gm))
                {
                    // Already has real interaction history — skip both the intro AND the
                    // trait-welcome stand-in line; nothing to introduce.
                    WriteStat(gm, PhaseStatUid, 1f);
                    WriteStat(gm, TraitWelcomeGivenStatUid, 1f);
                    phase = 1f;
                    Plugin.Logger.LogInfo("[VillageClock] Intro waived (pre-rework village progress) — phase backfilled to 1.");
                }
                else if (CardUtil.IsPerkEquipped(VillageInnPerkUid))
                {
                    // Trait skips the formal intro conversation, but the player has never
                    // actually met the Inn Keeper — leave TraitWelcomeGivenStatUid unset so
                    // CMC_InnKeeperTalk_TraitWelcome fires on their first Talk instead of
                    // landing cold on whatever week/errand line the phase gate opens next.
                    WriteStat(gm, PhaseStatUid, 1f);
                    phase = 1f;
                    Plugin.Logger.LogInfo("[VillageClock] Intro waived (Village Inn trait) — phase backfilled to 1, trait welcome pending.");
                }
            }

            if (phase < 0.5f) return; // still undiscovered — no epoch, no week, no beats

            // 2. Epoch stamp — once, on first observing phase >= 1.
            float epoch = ReadStat(gm, EpochDayStatUid);
            if (epoch >= 0f && epoch < 0.5f)
            {
                epoch = GameQuery.CurrentDay + 1;
                WriteStat(gm, EpochDayStatUid, epoch);

                // Step backfill: a save that already has the Well Plans heard both onboarding
                // beats in the pre-clock ordering — don't replay them.
                if (ReadStat(gm, WellPlansGivenStatUid) >= 0.5f)
                    WriteStat(gm, OnboardStepStatUid, 2f);

                Plugin.Logger.LogInfo($"[VillageClock] Village epoch stamped: day {GameQuery.CurrentDay} (stat {epoch}).");
            }
            if (epoch < 1f) return;

            // 3. Week recompute — derived, reload-safe; week 1 is the intro week.
            int week = WeekFromEpoch(epoch);
            float storedWeek = ReadStat(gm, WeekStatUid);
            if (storedWeek >= 0f && (int)Math.Round(storedWeek) != week)
                WriteStat(gm, WeekStatUid, week);
        }

        // ── Queries for other patchers ────────────────────────────────────────────

        /// <summary>True when the village phase is at least <paramref name="min"/> — or when the
        /// phase cannot be read at all (graceful open, see class doc).</summary>
        public static bool PhaseAtLeast(float min)
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null || !ResolveGetFromId()) return true;

            float phase = ReadStat(gm, PhaseStatUid);
            if (phase < 0f)
            {
                if (!_loggedPhaseUnreadable)
                {
                    _loggedPhaseUnreadable = true;
                    Plugin.Logger.LogDebug("[VillageClock] cmcStatVillagePhase unreadable — village gates treated as open.");
                }
                return true;
            }
            return phase >= min;
        }

        /// <summary>Whole village-weeks since the intro (week 1 = intro week); 0 before the intro
        /// or while the clock is unreadable.</summary>
        public static int WeeksSinceIntro(object gm)
        {
            if (gm == null || !ResolveGetFromId()) return 0;
            float epoch = ReadStat(gm, EpochDayStatUid);
            if (epoch < 1f) return 0;
            return WeekFromEpoch(epoch);
        }

        private static int WeekFromEpoch(float epoch)
        {
            int epochDay = (int)Math.Round(epoch) - 1; // undo the +1 unset-sentinel offset
            int elapsed = GameQuery.CurrentDay - epochDay;
            if (elapsed < 0) elapsed = 0;
            return (elapsed / 7) + 1;
        }

        private static bool HasPreReworkEvidence(object gm)
        {
            if (ReadStat(gm, WellPlansGivenStatUid) >= 1f) return true;
            foreach (var uid in QuestRenownStatUids)
                if (ReadStat(gm, uid) >= 1f) return true;
            return false;
        }

        // ── Player-GameStat access (the proven StatsDict/CurrentBaseValue idiom) ──

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
            if (stat != null) _statCache[uid] = stat; // SOs persist across saves — safe to cache
            return stat;
        }

        /// <summary>Current value of a player GameStat, or -1 when unreadable (stat SO missing,
        /// StatsDict not built yet, or the stat not materialized for this run).</summary>
        internal static float ReadStat(object gm, string uid)
        {
            var stat = StatRef(uid);
            if (stat == null) return -1f;
            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return -1f;
            if (!statsDict.Contains(stat)) return -1f;
            var inGameStat = statsDict[stat];
            if (inGameStat == null) return -1f;
            return Reflect.GetMember(inGameStat, "SimpleCurrentValue") is float f ? f : -1f;
        }

        internal static void WriteStat(object gm, string uid, float value)
        {
            var stat = StatRef(uid);
            if (stat == null) return;
            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return;
            if (!statsDict.Contains(stat)) return;
            var inGameStat = statsDict[stat];
            if (inGameStat == null) return;
            Reflect.SetMember(inGameStat, "CurrentBaseValue", value);
        }
    }
}
