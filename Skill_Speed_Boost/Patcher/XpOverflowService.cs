using System;
using System.Collections.Generic;
using BepInEx.Logging;
using CSFFModFramework.Api;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// XP overflow (plan E1): the part of a skill gain the game's clamp ate because the skill was at its
/// maximum is redirected, at XpOverflowFraction, to a related skill that is still below its own.
///
/// GameManager.ChangeStatValue applies a Permanent change as CurrentBaseValue += value - (num - num2),
/// where num2 is the requested end value clamped into CurrentMinMaxValue, so the excess a gain could
/// not land is the requested value minus the gain that did (MorningBonusPatch measures both). "Related"
/// is the synergy group table, overrides included (SkillSynergies.GroupsOf, plan B3): every registered
/// skill sharing a group with the source is a candidate, and the one with the lowest base / maximum
/// wins (ties by name). The amount is excess * XpOverflowFraction * the target's own global or
/// per-skill multiplier: a FLAT redirect that carries no situational bonus and claims no first-use,
/// streak, bank, milestone or synergy state, because those describe practice that did not happen. One
/// hop, written straight to the target's base as Math.Min(base + amount, max), so the target never
/// passes its maximum and nothing is chained (a direct field write does not reach ChangeStatValue).
/// The skill UI polls the base every frame, so the write shows without raising OnValueChanged (plan
/// W0.7, REFRESHES-ON-NEXT-TICK, read live 2026-10-02).
///
/// A source set to 0x (per-skill "disable levelling") routes nothing, as its normal gains are
/// reverted. A source with no eligible group-mate is a quiet no-op, logged once per source at Debug.
/// The magnitude is passed in by ChangeStat_Post (which reads XpOverflowFraction behind the
/// XpOverflowEnabled flag); everything else is read here.
/// </summary>
internal static class XpOverflowService
{
    private static ManualLogSource Logger => Plugin.Logger;

    // Below this an excess is float noise from the game's clamp arithmetic, not XP.
    private const float Epsilon = 0.0001f;

    private static readonly HashSet<string> _warnedCauses = new(StringComparer.Ordinal);

    // Skill name -> UniqueID, the reverse of GameLoadPatch.SkillNamesByUniqueId. Rebuilt whenever that
    // map's size changes (the load scan fills it once; a rescan replaces it).
    private static Dictionary<string, string> _uidsByName = new(StringComparer.OrdinalIgnoreCase);
    private static int _uidsByNameSource = -1;

    /// <summary>
    /// Redirects the excess of one gain. <paramref name="requested"/> is the value ChangeStatValue was
    /// asked to add, <paramref name="gained"/> what reached the source's base (0 when the source was
    /// already at its maximum). Returns true when a group-mate's base was written.
    /// </summary>
    internal static bool Route(string sourceName, float requested, float gained, float fraction)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceName)) return false;
            float excess = requested - Math.Max(gained, 0f);
            if (!(excess > Epsilon) || !(fraction > 0f)) return false;

            if (TargetMultiplier(sourceName) <= 0f)
            {
                DebugOnce("source0x:" + sourceName, $"[XpOverflow] {sourceName} is set to 0x, so its excess XP goes nowhere.");
                return false;
            }

            string[] sourceGroups = SkillSynergies.GroupsOf(sourceName);
            if (sourceGroups == null || sourceGroups.Length == 0)
            {
                DebugOnce("nogroup:" + sourceName, $"[XpOverflow] {sourceName} is in no synergy group, so its excess XP has no related skill to go to.");
                return false;
            }

            RefreshUidMap();
            var names = new List<string>();
            var uids = new List<string>();
            var stats = new List<object>();
            var bases = new List<float>();
            var maxes = new List<float>();
            var mults = new List<float>();

            foreach (var pair in _uidsByName)
            {
                string candidate = pair.Key;
                if (string.Equals(candidate, sourceName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!SharesGroup(sourceGroups, SkillSynergies.GroupsOf(candidate))) continue;

                float mult = TargetMultiplier(candidate);
                if (mult <= 0f) continue;

                object stat = PlayerConditionService.LocateStat(pair.Value, "XpOverflow",
                    $"XP overflow will not reach {candidate}.");
                if (stat == null) continue;
                if (!MorningBonusPatch.TryGetBaseValue(stat, out float baseValue)) continue;
                float max = StatAccess.GetMaxValue(stat);
                if (float.IsNaN(max))
                {
                    // StatAccess returns NaN rather than throwing, so this would otherwise be a silent skip.
                    WarnOnce("max:" + candidate, $"[XpOverflow] {candidate}'s maximum could not be read, so XP overflow never sends it XP (a renamed InGameStat field, probably).");
                    continue;
                }

                names.Add(candidate);
                uids.Add(pair.Value);
                stats.Add(stat);
                bases.Add(baseValue);
                maxes.Add(max);
                mults.Add(mult);
            }

            int pick = PickTarget(names.ToArray(), bases.ToArray(), maxes.ToArray(), mults.ToArray());
            if (pick < 0)
            {
                DebugOnce("none:" + sourceName, $"[XpOverflow] {sourceName}: no related skill is below its maximum (or every one is set to 0x), so its excess XP goes nowhere.");
                return false;
            }

            float amount = OverflowAmount(requested, gained, fraction, mults[pick]);
            float before = bases[pick];
            float after = OverflowTarget(before, amount, maxes[pick]);
            if (!(after > before + Epsilon)) return false;

            bool written = MorningBonusPatch.TrySetBaseValue(stats[pick], after);
            if (Plugin.LogSkillXpGains)
                Logger?.LogInfo(
                    $"[XpOverflow] {sourceName}: excess +{excess:F2} x{fraction:0.##} x{mults[pick]:0.##} to {names[pick]}, " +
                    (written ? $"base {before:F2} -> {after:F2}" : $"WRITE FAILED (base stays {before:F2})"));
            return written;
        }
        catch (Exception ex)
        {
            WarnOnce("route:" + ex.GetType().Name, $"[XpOverflow] could not redirect excess XP, so none is redirected: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The configured multiplier a redirected amount carries for <paramref name="skillName"/>: the
    /// per-skill value when per-skill multipliers are on, else the global one; 0 for a skill set to 0x
    /// (never a target), and never below 1 otherwise, the way ChangeStat_Post applies it.
    /// </summary>
    private static float TargetMultiplier(string skillName)
    {
        int mult = Plugin.SkillExpMultiplier;
        if (Plugin.EnablePerSkillMultipliers)
            mult = SkillConfigManager.GetSkillMultiplier(skillName);
        if (mult == 0) return 0f;
        return Math.Max(1, mult);
    }

    private static void RefreshUidMap()
    {
        var source = GameLoadPatch.SkillNamesByUniqueId;
        if (source.Count == _uidsByNameSource) return;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Value) || map.ContainsKey(pair.Value)) continue;
            map[pair.Value] = pair.Key;
        }
        _uidsByName = map;
        _uidsByNameSource = source.Count;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warnedCauses.Add(cause)) Logger?.LogWarning(message);
    }

    private static void DebugOnce(string cause, string message)
    {
        if (_warnedCauses.Add(cause)) Logger?.LogDebug(message);
    }

    /// <summary>
    /// The amount redirected: the excess (requested minus what landed, never counting a loss as a
    /// gain) times the fraction times the target's multiplier, or 0 when the excess is float noise or
    /// either factor is not positive. Pure, with no game types, so SkillSpeedBoost-BonusLogic.Tests.ps1
    /// compiles and runs it: keep the body C# 5.
    /// </summary>
    internal static float OverflowAmount(float requested, float delta, float fraction, float targetMult)
    {
        float landed = delta > 0f ? delta : 0f;
        float excess = requested - landed;
        if (!(excess > 0.0001f) || !(fraction > 0f) || !(targetMult > 0f)) return 0f;
        return excess * fraction * targetMult;
    }

    /// <summary>
    /// The index of the group-mate that receives the excess: among the candidates with a name, a
    /// positive multiplier, a real maximum and a base below it, the one with the lowest base / maximum,
    /// ties broken by name (ignoring letter case). -1 when none is eligible. Pure, C# 5.
    /// </summary>
    internal static int PickTarget(string[] names, float[] bases, float[] maxes, float[] mults)
    {
        int best = -1;
        float bestRatio = 0f;
        if (names == null || bases == null || maxes == null || mults == null) return best;
        int count = Math.Min(Math.Min(names.Length, bases.Length), Math.Min(maxes.Length, mults.Length));
        for (int i = 0; i < count; i++)
        {
            if (string.IsNullOrEmpty(names[i])) continue;
            if (!(mults[i] > 0f)) continue;
            float max = maxes[i];
            if (!(max > 0f) || float.IsInfinity(max) || max >= float.MaxValue) continue;
            if (!(bases[i] < max - 0.0001f)) continue;

            float ratio = bases[i] / max;
            if (best < 0 || ratio < bestRatio - 0.000001f
                || (Math.Abs(ratio - bestRatio) <= 0.000001f
                    && string.Compare(names[i], names[best], StringComparison.OrdinalIgnoreCase) < 0))
            {
                best = i;
                bestRatio = ratio;
            }
        }
        return best;
    }

    /// <summary>The target's base after the redirect: never above its maximum. Pure, C# 5.</summary>
    internal static float OverflowTarget(float targetBase, float amount, float targetMax)
    {
        return Math.Min(targetBase + amount, targetMax);
    }

    /// <summary>True when the two group lists share a group name (ignoring letter case). Pure, C# 5.</summary>
    internal static bool SharesGroup(string[] a, string[] b)
    {
        if (a == null || b == null) return false;
        for (int i = 0; i < a.Length; i++)
            for (int j = 0; j < b.Length; j++)
                if (!string.IsNullOrEmpty(a[i]) && string.Equals(a[i], b[j], StringComparison.OrdinalIgnoreCase))
                    return true;
        return false;
    }
}
