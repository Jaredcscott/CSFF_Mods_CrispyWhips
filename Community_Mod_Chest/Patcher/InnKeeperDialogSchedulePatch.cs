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

        private static bool ResolveTypes()
        {
            if (_gmType != null && _getFromIdMethod != null) return true;

            _gmType = CardUtil.FindGameType("GameManager");
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");
            if (_gmType == null || uidType == null) return false;

            _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            return _getFromIdMethod != null;
        }

        private static void RunSchedule()
        {
            try
            {
                if (!ResolveTypes()) return;
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
            if (ash < 0f) return;       // stat not found yet
            if (ash >= 0.5f) return;    // already armed (>=1) or resolved (>=2)
            if (VillageClock.ReadStat(gm, VillageClock.PhaseStatUid) < 0.5f) return; // never pre-intro
            if (VillageClock.WeeksSinceIntro(gm) < AshTakeoverWeek) return;
            if (!CardExistsAnywhere(gm, WellImprovementUid)) return;

            SetStat(gm, ref _ashStat, AshTakeoverStatUid, 1f);
            Plugin.Logger.LogInfo("[InnKeeperDialogSchedulePatch] Ash takeover armed — village week 6 reached and the Village Well is built.");
        }

        private static void CheckSeasonStory(object gm)
        {
            if (VillageClock.ReadStat(gm, VillageClock.PhaseStatUid) < 0.5f) return; // never pre-intro
            if (VillageClock.WeeksSinceIntro(gm) < StoryPoolStartWeek) return;

            string season = GameQuery.CurrentSeason;
            if (string.IsNullOrEmpty(season)) return;
            int seasonIdx = SeasonToIndex(season);
            if (seasonIdx <= 0) return;

            float lastSeason = ReadStat(gm, ref _lastSeasonStat, LastSeasonStatUid);
            if (lastSeason < 0f) return; // stat not found yet
            if ((int)Math.Round(lastSeason) == seasonIdx) return; // no change this tick

            int previousStory = (int)Math.Round(ReadStat(gm, ref _storyIndexStat, StoryIndexStatUid));
            int newStory = previousStory;
            while (newStory == previousStory)
                newStory = Rng.Next(1, StoryCount + 1);

            SetStat(gm, ref _storyIndexStat, StoryIndexStatUid, newStory);
            SetStat(gm, ref _storyStepStat, StoryStepStatUid, 0f);
            SetStat(gm, ref _lastSeasonStat, LastSeasonStatUid, seasonIdx);

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

        private static void SetStat(object gm, ref object cachedStat, string statUid, float value)
        {
            cachedStat ??= _getFromIdMethod.Invoke(null, new object[] { statUid });
            if (cachedStat == null) return;

            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return;
            if (!statsDict.Contains(cachedStat)) return;
            var inGameStat = statsDict[cachedStat];
            if (inGameStat == null) return;
            Reflect.SetMember(inGameStat, "CurrentBaseValue", value);
        }

        private static bool CardExistsAnywhere(object gm, string uid)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return false;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                var model = Reflect.GetMember(card, "CardModel");
                if (model == null) continue;
                if (Reflect.GetMember(model, "UniqueID") as string == uid) return true;
            }
            return false;
        }
    }
}
