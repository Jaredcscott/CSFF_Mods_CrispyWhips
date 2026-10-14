using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using CSFFModFramework.Api;
using HarmonyLib;
using UnityEngine;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// Partner skills follow your settings (plan E2, PartnerSkillsFollowSettings, default false).
///
/// A Partner NPC's skills are NOT the player's skill GameStats: they are 32 Partner_Skill_* NPCStats,
/// and a gain to one never reaches GameManager.ChangeStatValue, so MorningBonusPatch never sees it.
/// The path, read from .decomp/ (plan W0.5): when an NPC performs a card action (a duty's
/// AffectItemsDutyAction.PerformActions runs the action's dismantle or card-on-card action with the
/// NPC as its user), CardAction.CollectActionModifiers swaps every player-stat modifier the
/// GameStatsToNPCStats asset converts (Skill_Herbalism to Partner_Skill_Herbalism, and so on, for
/// Agent_Partner) into an NPCStatInstantModifier; GameManager.ActionRoutine hands each to
/// InGameNPC.ChangeStat, which runs InGameNPCStat.ApplyInstantModifier. That coroutine applies the
/// partner stat's own staleness (its NPCStat model's UsesNovelty and StalenessMultiplier) and then
/// SetStatValue(CurrentValue + gain, Permanent), which adds to CurrentPermanentValue, clamped.
///
/// This class patches the compiled MoveNext of ApplyInstantModifier (never the iterator builder, which
/// a caller another plugin recompiled first can inline: root CLAUDE.md "Harmony Patching Pitfalls"),
/// reads CurrentPermanentValue on the first step and again when the coroutine finishes, and scales the
/// gain by the player's global or per-skill XP multiplier for the matching skill through the same
/// MorningBonusPatch.ComposeBaseTarget the player's own gains use, against the partner stat's maximum.
/// Only that multiplier applies: the situational bonuses (time of day, place, tool, streak, focus and
/// the rest) describe the PLAYER's condition and never apply to a partner. The staleness half (the
/// four novelty fields of each Partner_Skill_* model) is GameLoadPatch.ApplyStalenessTo's, like the
/// player's skills. The option is read inside the patch, so switching it applies from the next gain.
/// </summary>
internal static class PartnerSkillPatch
{
    private static ManualLogSource Logger => Plugin.Logger;

    internal const string PartnerSkillPrefix = "Partner_Skill_";

    private static readonly HashSet<string> _warnedCauses = new(StringComparer.Ordinal);

    // One entry per running ApplyInstantModifier that was a partner skill gain when it started. Keyed
    // by the coroutine's state machine; a coroutine abandoned mid-way is collected with its key.
    private static readonly ConditionalWeakTable<object, PendingGain> _pending = new();

    private sealed class PendingGain
    {
        public object NpcStat;
        public string Skill;
        public float Before;
    }

    // Partner skill name for each NPCStat model seen, null for a stat that is not a mapped partner
    // skill. Keyed by the model itself; every InGameNPCStat of one stat shares it.
    private static readonly Dictionary<object, string> _skillByModel = new(new IdentityComparer());
    private static readonly HashSet<string> _unmappedWarned = new(StringComparer.OrdinalIgnoreCase);

    private static FieldInfo _stateField;
    private static FieldInfo _thisField;
    private static FieldInfo _modelField;
    private static FieldInfo _modelStatField;
    private static FieldInfo _permanentField;
    private static PropertyInfo _minMaxProperty;
    private static Type _stateMachineType;
    private static Type _npcStatType;

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var npcStatType = Reflect.TryGetType("InGameNPCStat");
            if (npcStatType == null)
            {
                Logger.LogWarning("[PartnerSkills] InGameNPCStat not found, so PartnerSkillsFollowSettings cannot scale partner skill gains. The game has probably renamed it.");
                return;
            }

            var apply = AccessTools.Method(npcStatType, "ApplyInstantModifier");
            if (apply == null || !typeof(IEnumerator).IsAssignableFrom(apply.ReturnType))
            {
                Logger.LogWarning("[PartnerSkills] InGameNPCStat.ApplyInstantModifier not found as a coroutine, so PartnerSkillsFollowSettings cannot scale partner skill gains (their staleness still follows your settings). The game has probably changed it.");
                return;
            }

            var moveNext = AccessTools.EnumeratorMoveNext(apply);
            if (moveNext == null)
            {
                Logger.LogWarning("[PartnerSkills] InGameNPCStat.ApplyInstantModifier has no compiled MoveNext to patch, so PartnerSkillsFollowSettings cannot scale partner skill gains (their staleness still follows your settings).");
                return;
            }

            _npcStatType = npcStatType;
            harmony.Patch(moveNext,
                prefix: new HarmonyMethod(typeof(PartnerSkillPatch), nameof(MoveNext_Prefix)),
                postfix: new HarmonyMethod(typeof(PartnerSkillPatch), nameof(MoveNext_Postfix)));
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[PartnerSkills] Patching InGameNPCStat.ApplyInstantModifier failed, so partner skill gains are not scaled: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    // First step only (state 0): remember the partner stat's base before the game writes the gain.
    static void MoveNext_Prefix(object __instance)
    {
        if (!Plugin.PartnerSkillsFollowSettings) return;
        try
        {
            if (!ResolveStateMachine(__instance)) return;
            if (!(_stateField.GetValue(__instance) is int state) || state != 0) return;

            object npcStat = _thisField.GetValue(__instance);
            if (npcStat == null)
            {
                WarnOnce("this-null", "an ApplyInstantModifier coroutine had no InGameNPCStat, so that partner gain is not scaled.");
                return;
            }

            string skill = SkillFor(npcStat);
            if (skill == null) return; // not a partner skill (hunger, morale ...) or an unmapped one, warned once in Resolve

            if (!TryGetBase(npcStat, out float before)) return;

            _pending.Remove(__instance);
            _pending.Add(__instance, new PendingGain { NpcStat = npcStat, Skill = skill, Before = before });
        }
        catch (Exception ex)
        {
            WarnOnce("prefix", $"reading a partner skill before its gain failed, so that gain is not scaled: {ex.Message}");
        }
    }

    // The coroutine has finished (MoveNext returned false): the gain is written, scale it.
    static void MoveNext_Postfix(object __instance, bool __result)
    {
        if (__result) return;
        if (!_pending.TryGetValue(__instance, out var pending)) return;
        _pending.Remove(__instance);
        try
        {
            ScaleGain(pending);
        }
        catch (Exception ex)
        {
            WarnOnce("postfix", $"scaling a partner skill gain failed, so the game's own gain was kept: {ex.Message}");
        }
    }

    private static void ScaleGain(PendingGain pending)
    {
        if (!TryGetBase(pending.NpcStat, out float after)) return;
        float gain = after - pending.Before;
        if (gain <= 0.0001f) return; // a loss, nothing, or already at the maximum: not XP to scale

        int multiplier = PartnerXpMultiplier(Plugin.SkillExpMultiplier, Plugin.EnablePerSkillMultipliers,
            SkillConfigManager.GetSkillMultiplier(pending.Skill));

        float target = after;
        bool writeFailed = false;
        if (multiplier != 1)
        {
            float max = ReadMax(pending.NpcStat);
            // The player's own arithmetic: never above the maximum, never below where the base started,
            // so a skill set to 0x (multiplier 0) gives the gain back exactly.
            target = MorningBonusPatch.ComposeBaseTarget(pending.Before, after, multiplier, max);
            if (Math.Abs(target - after) > 0.0001f)
                writeFailed = !TrySetBase(pending.NpcStat, target);
        }

        if (Plugin.LogSkillXpGains)
        {
            float final = writeFailed ? after : target;
            Logger?.LogInfo(
                $"[PartnerXp] {pending.Skill}: game +{gain:F2} x{multiplier} = {final - pending.Before:+0.00;-0.00;0.00}, " +
                $"base {pending.Before:F2} -> {final:F2}" +
                (writeFailed ? " (WRITE FAILED: the game's own gain was kept)" : ""));
        }
    }

    /// <summary>
    /// The player's skill a partner stat follows, from its NPCStat model's asset name; null when the
    /// stat is not a Partner_Skill_* stat or its stem matches no skill. Cached per model.
    /// </summary>
    private static string SkillFor(object npcStat)
    {
        if (_modelField == null)
        {
            _modelField = AccessTools.Field(npcStat.GetType(), "Model");
            if (_modelField == null)
            {
                WarnOnce("Model-missing", "InGameNPCStat.Model not found, so no partner skill gain is scaled. The game has probably renamed it.");
                return null;
            }
        }

        object model = _modelField.GetValue(npcStat);
        if (model == null)
        {
            WarnOnce("Model-null", "a partner stat had no Model, so its gain is not scaled.");
            return null;
        }

        if (_modelStatField == null)
        {
            _modelStatField = AccessTools.Field(model.GetType(), "ModelStat");
            if (_modelStatField == null)
            {
                WarnOnce("ModelStat-missing", "NPCStatInstance.ModelStat not found, so no partner skill gain is scaled. The game has probably renamed it.");
                return null;
            }
        }

        object modelStat = _modelStatField.GetValue(model);
        if (modelStat == null || (modelStat is UnityEngine.Object unityStat && unityStat == null))
        {
            WarnOnce("ModelStat-null", "a partner stat had no NPCStat model, so its gain is not scaled.");
            return null;
        }

        if (_skillByModel.TryGetValue(modelStat, out var cached)) return cached;
        string skill = Resolve(modelStat, true);
        _skillByModel[modelStat] = skill;
        return skill;
    }

    /// <summary>
    /// The mapped skill for an NPCStat model, or null. A Partner_Skill_* stat whose stem matches no
    /// skill (Partner_Skill_CombatExperience: Combat Experience is not a skill this mod tracks) is
    /// skipped with one warning per name, when <paramref name="warn"/> is true (the option is on).
    /// Shared by the staleness scan in GameLoadPatch and the gain patch, so the two agree.
    /// </summary>
    internal static string Resolve(object npcStatModel, bool warn)
    {
        string assetName = (npcStatModel as UnityEngine.Object)?.name;
        if (string.IsNullOrEmpty(assetName)
            || !assetName.StartsWith(PartnerSkillPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        string skill = MapPartnerSkill(assetName, SkillConfigManager.CoreSkills);
        if (skill == null && warn && _unmappedWarned.Add(assetName))
            Logger?.LogWarning(
                $"[PartnerSkills] '{assetName}' matches no skill this mod tracks, so it keeps the game's own staleness and its gains are not scaled.");
        return skill;
    }

    /// <summary>
    /// Maps a partner stat's asset name to the player's skill name it follows: strip Partner_Skill_,
    /// then compare with every skill name ignoring spaces, underscores and letter case, because the
    /// game's partner stems are spelled differently from the skills' display names (WoodWorking for
    /// Woodworking, Clubfighting for ClubFighting, Spearfishing for Spear Fishing, KnifeFIghting for
    /// Knife Fighting). Null when nothing matches (CombatExperience). Pure, with no game types, so
    /// SkillSpeedBoost-BonusLogic.Tests.ps1 compiles and runs it: keep the body C# 5.
    /// </summary>
    internal static string MapPartnerSkill(string assetName, string[] skills)
    {
        if (string.IsNullOrEmpty(assetName) || skills == null) return null;
        const string prefix = "Partner_Skill_";
        if (!assetName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        string stem = SquashName(assetName.Substring(prefix.Length));
        if (stem.Length == 0) return null;
        for (int i = 0; i < skills.Length; i++)
        {
            if (skills[i] != null && string.Equals(SquashName(skills[i]), stem, StringComparison.OrdinalIgnoreCase))
                return skills[i];
        }
        return null;
    }

    /// <summary>A name with its spaces and underscores removed. Pure, C# 5.</summary>
    internal static string SquashName(string name)
    {
        if (name == null) return "";
        var builder = new System.Text.StringBuilder(name.Length);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c != ' ' && c != '_') builder.Append(c);
        }
        return builder.ToString();
    }

    /// <summary>
    /// The XP multiplier a partner's gain takes: the per-skill value (which already falls back to the
    /// global one, and is 0 for a skill whose levelling is off) when per-skill multipliers are on,
    /// otherwise the global SkillExpMultiplier. The same choice ChangeStat_Post makes for the player.
    /// Pure, C# 5.
    /// </summary>
    internal static int PartnerXpMultiplier(int globalMultiplier, bool perSkillEnabled, int perSkillMultiplier)
    {
        return perSkillEnabled ? perSkillMultiplier : globalMultiplier;
    }

    private static bool ResolveStateMachine(object stateMachine)
    {
        var type = stateMachine.GetType();
        if (type == _stateMachineType) return _stateField != null && _thisField != null;

        _stateMachineType = type;
        _stateField = null;
        _thisField = null;
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType == typeof(int) && field.Name.EndsWith("__state", StringComparison.Ordinal)) _stateField = field;
            else if (field.Name.EndsWith("__this", StringComparison.Ordinal) && _npcStatType != null && _npcStatType.IsAssignableFrom(field.FieldType)) _thisField = field;
        }
        if (_stateField == null || _thisField == null)
        {
            WarnOnce("state-machine", $"ApplyInstantModifier's coroutine ({type.Name}) has no readable state or owner field, so partner skill gains are not scaled.");
            return false;
        }
        return true;
    }

    private static bool ResolveBaseMembers(object npcStat)
    {
        if (_permanentField != null && _minMaxProperty != null) return true;
        var type = npcStat.GetType();
        _permanentField = _permanentField ?? AccessTools.Field(type, "CurrentPermanentValue");
        _minMaxProperty = _minMaxProperty ?? AccessTools.Property(type, "MinMaxValue");
        if (_permanentField == null)
        {
            WarnOnce("CurrentPermanentValue-missing", "InGameNPCStat.CurrentPermanentValue not found, so partner skill gains are not scaled. The game has probably renamed it.");
            return false;
        }
        if (_minMaxProperty == null)
        {
            WarnOnce("MinMaxValue-missing", "InGameNPCStat.MinMaxValue not found, so partner skill gains are not scaled. The game has probably renamed it.");
            return false;
        }
        return true;
    }

    private static bool TryGetBase(object npcStat, out float value)
    {
        value = 0f;
        if (!ResolveBaseMembers(npcStat)) return false;
        if (_permanentField.GetValue(npcStat) is float read)
        {
            value = read;
            return true;
        }
        WarnOnce("CurrentPermanentValue-type", "InGameNPCStat.CurrentPermanentValue did not read as a number, so partner skill gains are not scaled.");
        return false;
    }

    private static bool TrySetBase(object npcStat, float value)
    {
        try
        {
            _permanentField.SetValue(npcStat, value);
            return true;
        }
        catch (Exception ex)
        {
            WarnOnce("CurrentPermanentValue-write", $"writing a partner skill failed, so the game's own gain was kept: {ex.Message}");
            return false;
        }
    }

    // The partner stat's maximum, the y of MinMaxValue; a (0, 0) range is the game's "unclamped".
    private static float ReadMax(object npcStat)
    {
        if (_minMaxProperty.GetValue(npcStat, null) is Vector2 range)
            return range == Vector2.zero ? float.MaxValue : range.y;
        WarnOnce("MinMaxValue-type", "InGameNPCStat.MinMaxValue did not read as a range, so a scaled partner gain is not capped by this mod (the game still clamps it when it next writes).");
        return float.MaxValue;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warnedCauses.Add(cause))
            Logger?.LogWarning($"[PartnerSkills] {message}");
    }

    private sealed class IdentityComparer : IEqualityComparer<object>
    {
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
