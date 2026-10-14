using System.Collections;
using System.Linq;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// Read-only view of the three player condition stats SSB's situational XP features depend on
/// (Energy, Satiation, Hydration), expressed as a fraction of each stat's LIVE maximum. Two
/// consumers read it from opposite ends: the well-rested bonus asks whether all three are at or
/// above a threshold, the low-condition penalty whether any one of them has fallen below one.
/// A third consumer, the In the Flow bonus (plan C5), reads Focus through the same ResolveInstance
/// and asks whether it is in the top band of Focus's own statuses (IsInFlow).
///
/// There is no framework helper for "read a player stat by UniqueID" — this is the same
/// open-coded idiom Community Mod Chest's HiddenStat uses: resolve the GameStat MODEL through
/// UniqueIDScriptable.GetFromID, then look the model up in GameManager.StatsDict to get the
/// runtime InGameStat instance. Both lookups are cached; only the composite verdict is
/// recomputed, and even that is rate-limited, because the caller runs on every XP gain.
///
/// The two GUIDs this shares with CMC's JailPatch (Satiation, Hydration) were cross-checked
/// against the vanilla GameStat GUID table rather than copied on trust; Energy came from the
/// same table.
/// </summary>
internal static class PlayerConditionService
{
    private static ManualLogSource Logger => Plugin.Logger;

    // Vanilla GameStat UniqueIDs, from
    // Documentation/GameData/CSFF-JsonData_Current/UniqueIDScriptableGUID/GameStat.json.
    private const string EnergyStatUid = "9e03b1c645e4c1c40b382ceac8387d4e";
    private const string SatiationStatUid = "930cf914322e9f145af1315d96f85a28";
    private const string HydrationStatUid = "95ca7c21ffad5e647acc3d9cb5bfcde6";

    private static readonly string[] RequiredStats = { EnergyStatUid, SatiationStatUid, HydrationStatUid };
    private static readonly Dictionary<string, object> _statModels = new(StringComparer.Ordinal);

    private static MethodInfo _getFromIdMethod;

    // One breadcrumb per distinct cause per session. A single bool was not enough: EVERY realistic
    // drift path in ResolveInstance returns null without throwing, so a catch-based latch can never
    // report them and the bonus stops applying with nothing in the log (audit A11 - the same defect
    // class fd99f7f40 closed on the XP write path). This runs on every skill XP gain, so a cause
    // must never log twice.
    private static readonly HashSet<string> _warnedCauses = new(StringComparer.Ordinal);

    /// <summary>
    /// Which feature a read was made for. Diagnostics only: it picks the log tag and the sentence
    /// naming what will not apply, and it prefixes the WarnOnce key so the two consumers cannot
    /// silence each other. One shared key set would mean whichever feature hit a cause first left
    /// the other with no log line at all, which is the A11 failure mode again one level up.
    /// </summary>
    private readonly struct Consumer
    {
        public readonly string Tag;
        public readonly string Effect;

        public Consumer(string tag, string effect)
        {
            Tag = tag;
            Effect = effect;
        }
    }

    private static readonly Consumer WellRestedReader =
        new("WellRested", "The well-rested bonus will not apply.");

    // An unnoticed no-op is worse on the penalty than on the bonus: a dead penalty looks exactly
    // like a character in good condition, so no amount of play-testing reveals it.
    private static readonly Consumer LowConditionReader =
        new("LowCondition", "The low-condition XP penalty will not apply.");

    // The third consumer (plan C5) reads one stat, Focus, against the top band of its OWN statuses.
    private static readonly Consumer FlowReader =
        new("FocusBonus", "The In the Flow XP bonus will not apply.");

    private static void WarnOnce(Consumer consumer, string cause, string message, string detail = null)
    {
        if (!_warnedCauses.Add(consumer.Tag + ":" + cause)) return;
        Logger?.LogWarning(
            $"[{consumer.Tag}] {message} {consumer.Effect}" + (detail != null ? " " + detail : ""));
    }

    // The caller fires on every skill XP gain; condition stats move on the DTP tick, so a
    // sub-second cache costs nothing in accuracy and keeps the hot path off reflection.
    private const float CacheSeconds = 1.0f;

    /// <summary>
    /// One cached verdict plus the threshold it was computed for. Each predicate owns its own
    /// instance: they are asked different questions at different thresholds on the SAME XP gain,
    /// so a single shared slot would answer one caller with the other's verdict and thrash the
    /// cache besides.
    /// </summary>
    private sealed class VerdictCache
    {
        private float _atRealtime = float.NegativeInfinity;
        private float _threshold = float.NaN;
        private bool _verdict;

        public bool TryGet(float now, float thresholdPercent, out bool verdict)
        {
            if (now - _atRealtime < CacheSeconds && thresholdPercent == _threshold)
            {
                verdict = _verdict;
                return true;
            }

            verdict = false;
            return false;
        }

        public bool Store(float now, float thresholdPercent, bool verdict)
        {
            _atRealtime = now;
            _threshold = thresholdPercent;
            _verdict = verdict;
            return verdict;
        }
    }

    private static readonly VerdictCache _wellRestedCache = new();
    private static readonly VerdictCache _lowConditionCache = new();
    private static readonly VerdictCache _flowCache = new();

    // Vanilla Focus (GameStat.json): 0 to 96, statuses Very Unfocused 0, Unfocused 1-24, Focused
    // 25-71, In the Flow 72-96 (EA 0.68b export). The band floor is NOT hardcoded: it is read from
    // the model's own Statuses once per model and kept, so a game update that moves the band moves
    // the bonus with it.
    private const string FocusStatUid = "4c5ed70723dc40e439720acab06e6af7";
    private static object _flowFloorModel;
    private static int _flowFloor;

    /// <summary>How the three per-stat readings fold into one verdict.</summary>
    private enum Mode
    {
        /// <summary>True only when every stat is at or above the threshold.</summary>
        AllAtOrAbove,

        /// <summary>True as soon as any one stat is below the threshold.</summary>
        AnyBelow,
    }

    /// <summary>
    /// True when Energy, Satiation AND Hydration are each at or above
    /// <paramref name="thresholdPercent"/> percent of their current maximum.
    ///
    /// Fails CLOSED: if any of the three cannot be read, this returns false rather than
    /// handing out a bonus on unverified state. A read failure warns once per session
    /// (fleet silent-catch rule) so a future field rename is visible rather than looking
    /// like the player simply never qualifies. That promise covers the NON-throwing null
    /// returns too - each one calls WarnOnce, because the catch alone could never see them
    /// (audit A11). "Below threshold" is the normal negative answer and stays silent.
    /// </summary>
    public static bool IsWellRested(float thresholdPercent)
    {
        return Resolve(_wellRestedCache, thresholdPercent, Mode.AllAtOrAbove, WellRestedReader);
    }

    /// <summary>
    /// True when ANY of Energy, Satiation or Hydration is below
    /// <paramref name="thresholdPercent"/> percent of its current maximum: the trigger for the
    /// low-condition XP penalty, and the exact inverse of <see cref="IsWellRested"/> when both are
    /// asked about the same threshold.
    ///
    /// Fails CLOSED in the same direction, which on a penalty path means NOT punishing the player:
    /// an unreadable stat returns false, so nothing is suppressed on unverified state. Every
    /// failure path breadcrumbs once per session under its own [LowCondition] tag rather than
    /// borrowing the bonus's entries.
    /// </summary>
    public static bool IsAnyConditionBelow(float thresholdPercent)
    {
        return Resolve(_lowConditionCache, thresholdPercent, Mode.AnyBelow, LowConditionReader);
    }

    /// <summary>
    /// True while vanilla Focus is in its top band ("In the Flow"): the status with the highest
    /// ValueRange.x in the Focus model's own Statuses, read with the same rounding the game uses
    /// to show that status (StatStatus.IsInRange through ExtraMath.RoundOrFloor), so the bonus
    /// applies exactly while the game shows the player In the Flow (plan C5).
    ///
    /// Fails CLOSED like the other two predicates: an unreadable Focus, or a Statuses list whose
    /// shape cannot be read, returns false and warns once per cause under [FocusBonus], the
    /// non-throwing null returns included (root CLAUDE.md "Silent Catch Blocks").
    /// </summary>
    public static bool IsInFlow()
    {
        float now = Time.realtimeSinceStartup;
        // One question, no threshold: the cache's threshold slot holds a constant 0.
        if (_flowCache.TryGet(now, 0f, out bool cached)) return cached;

        return _flowCache.Store(now, 0f, EvaluateFlow());
    }

    private static bool EvaluateFlow()
    {
        var instance = ResolveInstance(FocusStatUid, FlowReader);
        if (instance == null) return false;

        if (!TryGetFlowFloor(out int floor)) return false;

        float current = StatAccess.GetCurrentValue(instance);
        if (float.IsNaN(current))
        {
            WarnOnce(FlowReader, "statvalue:" + FocusStatUid,
                $"Focus ('{FocusStatUid}') read back as NaN; StatAccess could not resolve a usable value " +
                "(likely a renamed InGameStat field).");
            return false;
        }

        return IsAtOrAboveFloor(current, floor);
    }

    /// <summary>
    /// The In the Flow floor, read from the Focus model's Statuses the first time and kept for that
    /// model. Every way the read can fail returns false with its own warning, because Reflect.TryGetMember
    /// reports a missing member rather than throwing.
    /// </summary>
    private static bool TryGetFlowFloor(out int floor)
    {
        floor = 0;
        if (!_statModels.TryGetValue(FocusStatUid, out var model) || model == null)
        {
            WarnOnce(FlowReader, "flow-model",
                "The Focus GameStat model was not cached after its instance resolved, so its statuses cannot be read.");
            return false;
        }

        if (ReferenceEquals(model, _flowFloorModel))
        {
            floor = _flowFloor;
            return true;
        }

        if (!Reflect.TryGetMember(model, "Statuses", out var raw) || raw == null)
        {
            WarnOnce(FlowReader, "statuses-missing",
                "GameStat.Statuses could not be read on Focus - the field is missing or empty.");
            return false;
        }

        if (raw is not IList statuses)
        {
            WarnOnce(FlowReader, "statuses-type",
                $"GameStat.Statuses on Focus is a {raw.GetType().Name}, not a list, so its bands cannot be read.");
            return false;
        }

        var starts = new int[statuses.Count];
        var usesPercent = new bool[statuses.Count];
        for (int i = 0; i < statuses.Count; i++)
        {
            var status = statuses[i];
            if (status == null
                || !Reflect.TryGetMember(status, "ValueRange", out var range) || range == null
                || !Reflect.TryGetMember(range, "x", out var start) || start == null)
            {
                WarnOnce(FlowReader, "statuses-range",
                    $"Focus status {i} has no readable ValueRange.x (StatStatus has probably changed shape).");
                return false;
            }
            starts[i] = CardUtil.ToInt(start);
            usesPercent[i] = Reflect.GetBool(status, "UsePercentageForRange", false);
        }

        int top = FloorFromStatuses(starts, out int topIndex);
        if (topIndex < 0)
        {
            WarnOnce(FlowReader, "statuses-empty", "Focus has no statuses, so it has no In the Flow band.");
            return false;
        }

        // A percentage band is measured against the live minimum and maximum, not the value. Vanilla
        // Focus has none; reading one as a plain value would put the floor in the wrong place.
        if (usesPercent[topIndex])
        {
            WarnOnce(FlowReader, "statuses-percent",
                $"Focus's top status measures a percentage of its range (UsePercentageForRange), which this bonus does not read.");
            return false;
        }

        _flowFloorModel = model;
        _flowFloor = top;
        Logger?.LogDebug($"[FocusBonus] Focus's top status starts at {top} (status {topIndex} of {starts.Length}): the bonus applies from there.");
        floor = top;
        return true;
    }

    /// <summary>
    /// The floor of the top band: the highest ValueRange.x among a stat's statuses (the first one on
    /// a tie), with its index, or index -1 when there is none. Pure, with no game types, so
    /// SkillSpeedBoost-BonusLogic.Tests.ps1 compiles and runs it: keep the body C# 5.
    /// </summary>
    internal static int FloorFromStatuses(int[] rangeStarts, out int statusIndex)
    {
        statusIndex = -1;
        int floor = 0;
        if (rangeStarts == null) return floor;

        for (int i = 0; i < rangeStarts.Length; i++)
        {
            if (statusIndex < 0 || rangeStarts[i] > floor)
            {
                floor = rangeStarts[i];
                statusIndex = i;
            }
        }
        return floor;
    }

    /// <summary>
    /// True when a stat value is in a band starting at <paramref name="floor"/>, read the way the
    /// game reads a status band: StatStatus.IsInRange rounds the value with ExtraMath.RoundOrFloor
    /// (up only past .5), so 71.5 is still Focused and 71.51 is In the Flow. Pure, C# 5.
    /// </summary>
    internal static bool IsAtOrAboveFloor(float value, int floor)
    {
        int whole = (int)Math.Floor(value);
        if (value - whole > 0.5f) whole++;
        return whole >= floor;
    }

    private static bool Resolve(VerdictCache cache, float thresholdPercent, Mode mode, Consumer consumer)
    {
        float now = Time.realtimeSinceStartup;
        if (cache.TryGet(now, thresholdPercent, out bool cached)) return cached;

        return cache.Store(now, thresholdPercent, Evaluate(thresholdPercent, mode, consumer));
    }

    private static bool Evaluate(float thresholdPercent, Mode mode, Consumer consumer)
    {
        float required = thresholdPercent / 100f;

        for (int i = 0; i < RequiredStats.Length; i++)
        {
            var instance = ResolveInstance(RequiredStats[i], consumer);
            if (instance == null) return false;

            float current = StatAccess.GetCurrentValue(instance);
            float max = StatAccess.GetMaxValue(instance);
            if (float.IsNaN(current) || float.IsNaN(max) || max <= 0f)
            {
                // Unreadable, as distinct from "below threshold" below - that is the normal answer
                // for both consumers and is deliberately never logged.
                WarnOnce(consumer, "statvalue:" + RequiredStats[i],
                    $"Stat '{RequiredStats[i]}' read back current={current}, max={max}; StatAccess could not " +
                    "resolve a usable value (likely a renamed InGameStat field).");
                return false;
            }

            bool atOrAbove = current / max >= required;
            if (mode == Mode.AllAtOrAbove)
            {
                if (!atOrAbove) return false;
            }
            else if (!atOrAbove)
            {
                return true;
            }
        }

        // Every stat came back at or above the threshold: that IS the well-rested verdict, and it is
        // precisely the negation of the low-condition one.
        return mode == Mode.AllAtOrAbove;
    }

    /// <summary>
    /// The shared stat locator: the live InGameStat for any GameStat UniqueID, read the same way as the
    /// three condition stats and Focus, with every failure warned once per cause under
    /// <paramref name="tag"/> and <paramref name="effect"/> naming what will not happen. XP overflow
    /// (plan E1) finds a skill's group-mates through it.
    /// </summary>
    internal static object LocateStat(string statUid, string tag, string effect)
    {
        return ResolveInstance(statUid, new Consumer(tag, effect));
    }

    private static object ResolveInstance(string statUid, Consumer consumer)
    {
        try
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null)
            {
                WarnOnce(consumer, "gamemanager",
                    "GameManager could not be resolved while a skill stat was changing, which should not " +
                    "happen inside a run.");
                return null;
            }

            if (!_statModels.TryGetValue(statUid, out var model) || model == null)
            {
                if (!ResolveGetFromId())
                {
                    WarnOnce(consumer, "getfromid",
                        "Could not reflect UniqueIDScriptable.GetFromID(string) - the type or that overload " +
                        "was renamed by a game update.");
                    return null;
                }

                model = _getFromIdMethod.Invoke(null, new object[] { statUid });
                if (model == null)
                {
                    // Clean null, NOT an exception, so the catch below never sees this: GetFromID
                    // simply does not know this UniqueID. The three hardcoded vanilla condition-stat
                    // GUIDs are the version-drift suspect. Without this breadcrumb the feature that
                    // asked fails closed with zero log output and reads as "the player never
                    // qualifies" (fleet D17 silent-failure rule). Latched per consumer and cause -
                    // this sits on every skill XP gain.
                    WarnOnce(consumer, "model:" + statUid,
                        $"GetFromID resolved no GameStat for '{statUid}'; this vanilla stat GUID has " +
                        "probably changed in a game update.");
                    return null;
                }
                _statModels[statUid] = model;
            }

            if (CardUtil.GetCachedField(gm.GetType(), "StatsDict")?.GetValue(gm) is not IDictionary statsDict)
            {
                // GetCachedField returns null rather than throwing (CSFFModFramework/Util/CardUtil.cs),
                // so a renamed field can never reach the catch below.
                WarnOnce(consumer, "statsdict",
                    "GameManager.StatsDict could not be read - the field is missing, or is no longer a " +
                    "dictionary.");
                return null;
            }

            if (!statsDict.Contains(model))
            {
                WarnOnce(consumer, "dictmiss:" + statUid,
                    $"GameStat '{statUid}' resolved to a model absent from GameManager.StatsDict - most likely " +
                    "a duplicate ScriptableObject instance (CLAUDE.md Runtime Card State Caching).");
                return null;
            }

            return statsDict[model];
        }
        catch (Exception ex)
        {
            // InnerException first: the GetFromID call above is a reflective Invoke, and a
            // TargetInvocationException's own Message is always the generic "Exception has
            // been thrown by the target of an invocation", which names nothing. Same idiom
            // as the ApplyPatch catches in GameLoadPatch/MorningBonusPatch/AreaFamiliarityPatch.
            WarnOnce(consumer, "throw:" + statUid,
                $"Could not read stat '{statUid}'.",
                ex.InnerException?.ToString() ?? ex.ToString());
            return null;
        }
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
}
