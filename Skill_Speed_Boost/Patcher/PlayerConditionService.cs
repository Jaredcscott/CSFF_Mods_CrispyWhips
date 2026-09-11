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
                $"Could not read player condition stat '{statUid}'.",
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
