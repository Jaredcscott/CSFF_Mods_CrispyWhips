using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Village Inn Keeper — dialog scheduling for the weekly onboarding arc, the seasonal random
    /// story pool, and the Ash takeover event (Documentation/Design/Village_InnKeeper_Plan.md).
    ///
    /// Everything this patch writes feeds hidden GameStats that JSON `DialogLine.StartingConditions`
    /// gate on — the same declarative state-machine idiom as cmcStatLostCat
    /// (see reference_hidden_stat_quest_state_machine). This patch owns exactly the things JSON
    /// cannot express: the Village Clock bookkeeping (VillageClock.Tick — phase backfill, epoch
    /// stamp, week recompute), "has village-week 6 arrived AND is the well built" (arms
    /// cmcStatAshTakeover), and "has the season changed" (rerolls the active story). Every other
    /// transition — self-advancing story steps, the Ash reveal, the lost-cat arming — is authored
    /// JSON, same as the rest of the Inn Keeper's dialog.
    ///
    /// PR-1 (village rework): both C# beats here are anchored to the Village Clock, never the
    /// absolute world clock, and both additionally require village phase >= 1 — a player who has
    /// never done the Inn Keeper's intro sees no story rerolls and no Ash arming, ever.
    /// </summary>
    internal static class InnKeeperDialogSchedulePatch
    {
        private const string AshTakeoverStatUid = "cmcStatAshTakeover";
        private const string StoryIndexStatUid = "cmcStatInnStoryIndex";
        private const string StoryStepStatUid = "cmcStatInnStoryStep";
        private const string LastSeasonStatUid = "cmcStatInnLastSeason";
        private const string ErrandStateStatUid = "cmcStatInnErrandState";
        private const string WellImprovementUid = "cmcimpwell";

        private const int AshTakeoverWeek = 6;  // village-weeks since the intro (was: absolute day 42)
        private const int StoryPoolStartWeek = 4; // after the week-3 onboarding beat (was: absolute day 22)
        private const int StoryCount = 6;          // cmcStatInnStoryIndex values 1..6

        private static bool _initialized;
        private static Type _gmType;
        private static MethodInfo _getFromIdMethod;
        private static readonly Random Rng = new Random();

        // One latch per cause (CLAUDE.md Silent Catch Blocks, G5).
        private static readonly System.Collections.Generic.HashSet<string> _warned =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }

        /// <summary>Consecutive in-run ticks (30 s) a resolve or a stat read may miss before its one warning:
        /// a run's first ticks can miss while it boots, a miss that lasts is a renamed type or an unshipped stat.</summary>
        private const int MissTicksBeforeWarning = 6;
        private static readonly System.Collections.Generic.Dictionary<string, int> _missTicks =
            new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>One more consecutive miss for <paramref name="cause"/>: quiet outside a run, one warning
        /// once it has lasted <see cref="MissTicksBeforeWarning"/> ticks inside one.</summary>
        private static void NoteMiss(string cause, string message)
        {
            if (GameQuery.CurrentDay <= 0) { _missTicks.Remove(cause); return; }
            _missTicks.TryGetValue(cause, out int n);
            _missTicks[cause] = ++n;
            if (n >= MissTicksBeforeWarning) WarnOnce(cause, message);
        }

        private static void ClearMiss(string cause) => _missTicks.Remove(cause);

        private static object _ashStat;
        private static object _storyIndexStat;
        private static object _storyStepStat;
        private static object _lastSeasonStat;
        private static object _errandStateStat;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, RunSchedule, "InnKeeperDialogSchedule");
            Plugin.Logger.LogDebug("[InnKeeperDialogSchedulePatch] initialized.");
        }

        /// <summary>False, naming what is missing in <paramref name="missing"/>. The game assembly is loaded
        /// before any tick, so a miss here is a game-update rename, never a boot transient.</summary>
        private static bool ResolveTypes(out string missing)
        {
            missing = null;
            if (_gmType != null && _getFromIdMethod != null) return true;

            _gmType = CardUtil.FindGameType("GameManager");
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");
            if (_gmType == null) { missing = "GameManager"; return false; }
            if (uidType == null) { missing = "UniqueIDScriptable"; return false; }

            _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            if (_getFromIdMethod == null) { missing = "UniqueIDScriptable.GetFromID(string)"; return false; }
            return true;
        }

        private static void RunSchedule()
        {
            try
            {
                if (!ResolveTypes(out string missing))
                {
                    // Not through NoteMiss: its in-run test reads GameQuery.CurrentDay, which needs GameManager.
                    WarnOnce("types-unresolved", $"[InnKeeperDialogSchedulePatch] {missing} not found - the village clock, the Ash takeover and the seasonal story do not run.");
                    return;
                }
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                VillageClock.Tick(gm);
                CheckAshTakeover(gm);
                CheckSeasonStory(gm);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[InnKeeperDialogSchedulePatch] RunSchedule failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void CheckAshTakeover(object gm)
        {
            float ash = ReadStat(gm, ref _ashStat, AshTakeoverStatUid);
            if (ash < 0f)               // stat not found yet; warned if it stays that way
            {
                NoteMiss(AshTakeoverStatUid + ":unreadable", $"[InnKeeperDialogSchedulePatch] {AshTakeoverStatUid} is not readable - the Ash takeover does not arm until it is.");
                return;
            }
            ClearMiss(AshTakeoverStatUid + ":unreadable");
            if (ash >= 0.5f) return;    // already armed (>=1) or resolved (>=2)
            if (VillageClock.ReadStat(gm, VillageClock.PhaseStatUid) < 0.5f) return; // never pre-intro
            if (VillageClock.WeeksSinceIntro(gm) < AshTakeoverWeek) return;
            if (!CardExistsAnywhere(gm, WellImprovementUid)) return;

            if (!SetStat(gm, ref _ashStat, AshTakeoverStatUid, 1f)) return; // SetStat warned, once per stat
            Plugin.Logger.LogInfo("[InnKeeperDialogSchedulePatch] Ash takeover armed — village week 6 reached and the Village Well is built.");
        }

        private static void CheckSeasonStory(object gm)
        {
            if (VillageClock.ReadStat(gm, VillageClock.PhaseStatUid) < 0.5f) return; // never pre-intro
            if (VillageClock.WeeksSinceIntro(gm) < StoryPoolStartWeek) return;

            // Reached only mid-run, after the village clock read: a season that stays unknown is a game-update
            // rename, not a boot moment.
            string season = GameQuery.CurrentSeason;
            int seasonIdx = string.IsNullOrEmpty(season) ? 0 : SeasonToIndex(season);
            if (seasonIdx <= 0)
            {
                NoteMiss("season-unrecognised", $"[InnKeeperDialogSchedulePatch] The current season reads '{season ?? "null"}', which is not Spring, Summer, Autumn or Winter - the Inn Keeper's seasonal story and errand do not reset.");
                return;
            }
            ClearMiss("season-unrecognised");

            float lastSeason = ReadStat(gm, ref _lastSeasonStat, LastSeasonStatUid);
            if (lastSeason < 0f)         // stat not found yet; warned if it stays that way
            {
                NoteMiss(LastSeasonStatUid + ":unreadable", $"[InnKeeperDialogSchedulePatch] {LastSeasonStatUid} is not readable - the Inn Keeper's seasonal story and errand do not reset until it is.");
                return;
            }
            ClearMiss(LastSeasonStatUid + ":unreadable");
            if ((int)Math.Round(lastSeason) == seasonIdx) return; // no change this tick

            int previousStory = (int)Math.Round(ReadStat(gm, ref _storyIndexStat, StoryIndexStatUid));
            int newStory = previousStory;
            while (newStory == previousStory)
                newStory = Rng.Next(1, StoryCount + 1);

            // Stamp the season FIRST and stop if it does not take: an unstamped season reads as a new one on
            // every 5 s tick, which would re-roll the story and wipe the errand each time (the stamp-first
            // order InnOddJobPatch.RollIfNewDay uses).
            if (!SetStat(gm, ref _lastSeasonStat, LastSeasonStatUid, seasonIdx)) return;
            SetStat(gm, ref _storyIndexStat, StoryIndexStatUid, newStory);
            SetStat(gm, ref _storyStepStat, StoryStepStatUid, 0f);

            // The odd-jobs errand (Village_InnKeeper_Plan.md § Phase 7) is capped at once per
            // season rather than infinitely repeatable — a season change clears whatever state
            // it was in (offered, accepted, or delivered) so a fresh errand can be offered again.
            SetStat(gm, ref _errandStateStat, ErrandStateStatUid, 0f);
            Plugin.Logger.LogInfo($"[InnKeeperDialogSchedulePatch] New season ({season}) — Inn Keeper story #{newStory} begins.");
        }

        private static int SeasonToIndex(string season)
        {
            if (string.Equals(season, "Spring", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(season, "Summer", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(season, "Autumn", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(season, "Winter", StringComparison.OrdinalIgnoreCase)) return 4;
            return 0;
        }

        private static float ReadStat(object gm, ref object cachedStat, string statUid)
        {
            cachedStat ??= _getFromIdMethod.Invoke(null, new object[] { statUid });
            if (cachedStat == null) return -1f;

            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return -1f;
            if (!statsDict.Contains(cachedStat)) return -1f;
            var inGameStat = statsDict[cachedStat];
            if (inGameStat == null) return -1f;
            return Reflect.GetMember(inGameStat, "SimpleCurrentValue") is float f ? f : -1f;
        }

        /// <summary>False, with one warning per stat and cause, when the stat does not resolve or does not take
        /// the write. Callers only write after <see cref="ReadStat"/> found the stat, so a miss here is real.</summary>
        private static bool SetStat(object gm, ref object cachedStat, string statUid, float value)
        {
            cachedStat ??= _getFromIdMethod.Invoke(null, new object[] { statUid });
            if (cachedStat == null)
            {
                WarnOnce(statUid + ":unresolved", $"[InnKeeperDialogSchedulePatch] {statUid} did not resolve - it was not written.");
                return false;
            }
            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict)
            {
                WarnOnce("statsdict-null", "[InnKeeperDialogSchedulePatch] GameManager.StatsDict is not readable - Inn Keeper stats are not written.");
                return false;
            }
            var inGameStat = statsDict.Contains(cachedStat) ? statsDict[cachedStat] : null;
            if (inGameStat == null)
            {
                WarnOnce(statUid + ":missing", $"[InnKeeperDialogSchedulePatch] {statUid} is not in GameManager.StatsDict - it was not written.");
                return false;
            }
            if (!Reflect.SetMember(inGameStat, "CurrentBaseValue", value))
            {
                WarnOnce(statUid + ":write", $"[InnKeeperDialogSchedulePatch] {statUid} did not take the write (InGameStat.CurrentBaseValue).");
                return false;
            }
            return true;
        }

        /// <summary>True when a card of <paramref name="uid"/> is in GameManager.AllCards. An unreadable AllCards,
        /// or cards whose model or UniqueID never read, warn once: either looks like "not built" to the caller.</summary>
        private static bool CardExistsAnywhere(object gm, string uid)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards)
            {
                WarnOnce("allcards-unreadable", "[InnKeeperDialogSchedulePatch] GameManager.AllCards is not readable - the Village Well reads as not built, so the Ash takeover does not arm.");
                return false;
            }
            bool sawCard = false, sawId = false;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                sawCard = true;
                var model = Reflect.GetMember(card, "CardModel");
                if (model == null) continue;
                if (Reflect.GetMember(model, "UniqueID") is not string id) continue;
                sawId = true;
                if (id == uid) return true;
            }
            if (sawCard && !sawId)
                WarnOnce("card-id-unreadable", "[InnKeeperDialogSchedulePatch] No card's CardModel.UniqueID is readable - the Village Well reads as not built, so the Ash takeover does not arm.");
            return false;
        }
    }
}
