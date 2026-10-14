using System;
using System.Collections;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace Herbs_And_Fungi.Patcher;

/// <summary>
/// Plan feature 41, the Academy bridge (registry S1: an ALTERNATIVE satisfier, never the only gate).
/// Mirrors H&amp;F's own hidden stat <c>herbs_fungi_stat_academy_herbalism</c> to 1 while the player holds
/// Community Mod Chest's Herbalism graduate perk (<c>cmcperkgradherbalism</c>) and to 0 otherwise. The
/// stat gates the "Identify (Academy training)" action on the Unknown Chanterelle through
/// <c>RequiredStatValues</c>, so no H&amp;F JSON names a CMC UID, and this class only READS CMC state.
///
/// <para>The perk is granted IN-RUN by CMC's Academy, so it never enters <c>CharacterPerks</c> and
/// <c>CardUtil.IsPerkEquipped</c> is permanently false for it (CLAUDE.md "Perks");
/// <c>CardUtil.IsPerkHeld</c> (framework 2.28.0) also reads <c>GameManager.InRunAddedPerks</c>.</para>
///
/// <para>Sync point (Documentation/Research/HerbsAndFungi/stations_and_gates.md question 5): one
/// <see cref="TickEvents.Interval"/> registered ONCE per process from <see cref="ApplyPatch"/>, which
/// <c>Plugin.RegisterPatches</c> calls once from the plugin's Awake; never from an OnGMInitialized
/// handler, which re-fires on every save reload (CLAUDE.md "Per-Run Event Registration"). It mirrors
/// both ways, so a save loaded without the perk, or a CMC removed between sessions, goes back to 0 on
/// the next cycle. It writes only on change. Without CMC the stat stays 0 and the action stays hidden.</para>
/// </summary>
internal static class AcademyHerbalismBridgePatch
{
    internal const string MirrorStatUid = "herbs_fungi_stat_academy_herbalism";
    internal const string GraduatePerkUid = "cmcperkgradherbalism";
    private const string CmcModName = "Community Mod Chest";
    private const float PeriodSeconds = 3f; // CMC GraduatePerkPatch's own period

    private static bool _registered;
    private static bool _perkChecked;
    private static Type _gameStatType;
    private static object _statDefinition;
    private static readonly HashSet<string> _warnedCauses = new();

    public static void ApplyPatch(Harmony harmony)
    {
        // Belt and braces: RegisterPatches runs once per process already.
        if (_registered) return;
        _registered = true;
        TickEvents.Interval(PeriodSeconds, Mirror, "HFAcademyBridge");
        Plugin.Logger.LogDebug("[AcademyBridge] Registered the Academy herbalism mirror (every 3 s).");
    }

    // One line per cause for the life of the process (CLAUDE.md "Silent Catch Blocks"): every early
    // exit below that is not the normal "no run yet" case names its cause here.
    private static void WarnOnce(string cause, string message, bool debugOnly = false)
    {
        if (!_warnedCauses.Add(cause)) return;
        if (debugOnly) Plugin.Logger.LogDebug("[AcademyBridge] " + message);
        else Plugin.Logger.LogWarning("[AcademyBridge] " + message);
    }

    private static void Mirror()
    {
        try
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null || (gm is UnityEngine.Object uo && uo == null)) return; // main menu: normal, silent

            CheckPerkResolvesOnce();

            var live = ResolveLiveStat(gm);
            if (live == null) return;

            float target = CardUtil.IsPerkHeld(GraduatePerkUid) ? 1f : 0f;
            float current = StatAccess.GetCurrentValue(live);
            if (float.IsNaN(current))
            {
                WarnOnce("read-nan", $"could not read the live value of '{MirrorStatUid}'; the Academy Identify action will not follow the perk.");
                return;
            }
            if (Math.Abs(current - target) < 0.001f) return;

            if (StatAccess.SetCurrentValue(live, target))
                Plugin.Logger.LogDebug($"[AcademyBridge] {MirrorStatUid} {current} -> {target}");
            else
                WarnOnce("write-failed", $"writing '{MirrorStatUid}' failed; the Academy Identify action will not follow the perk.");
        }
        catch (Exception ex)
        {
            WarnOnce("threw", $"mirror threw: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    // Breadcrumb only: CardUtil.IsPerkHeld is false for a perk that does not exist, so the value needs
    // no CMC check. CMC absent is normal and silent; CMC loaded with the perk UID unresolved means CMC
    // renamed or regrouped the perk and the Academy path went quietly dead (plan Risk register).
    private static void CheckPerkResolvesOnce()
    {
        if (_perkChecked) return;
        _perkChecked = true;
        if (!ModRegistry.IsLoaded(CmcModName)) return;

        var perkType = CardUtil.FindGameType("CharacterPerk");
        if (perkType == null)
        {
            WarnOnce("no-perk-type", "the CharacterPerk type was not found, so the CMC graduate perk could not be checked.", debugOnly: true);
            return;
        }
        if (GameContent.Find(perkType, GraduatePerkUid) == null)
            WarnOnce("perk-unresolved", $"Community Mod Chest is loaded but its perk '{GraduatePerkUid}' does not resolve; Identify (Academy training) will stay hidden.", debugOnly: true);
    }

    // The live InGameStat for this run, from GameManager.StatsDict keyed by the GameStat definition.
    // Null with no log while the run is still building its stats (normal for a moment at load).
    private static object ResolveLiveStat(object gm)
    {
        if (_statDefinition == null)
        {
            _gameStatType ??= CardUtil.FindGameType("GameStat");
            if (_gameStatType == null)
            {
                WarnOnce("no-stat-type", "the GameStat type was not found; the Academy mirror cannot run.");
                return null;
            }
            _statDefinition = GameContent.Find(_gameStatType, MirrorStatUid);
            if (_statDefinition == null)
            {
                WarnOnce("no-stat", $"H&F's own stat '{MirrorStatUid}' is not loaded; the Academy mirror cannot run.");
                return null;
            }
        }

        if (CardUtil.GetCachedField(gm.GetType(), "StatsDict")?.GetValue(gm) is not IDictionary statsDict)
        {
            WarnOnce("no-statsdict", "GameManager.StatsDict was not found; the Academy mirror cannot run.");
            return null;
        }
        if (!statsDict.Contains(_statDefinition))
        {
            // Usual for a moment while a run builds its stats; a Debug line once, in case it never ends.
            WarnOnce("not-in-run", $"'{MirrorStatUid}' is not (yet) in GameManager.StatsDict; mirroring waits for it.", debugOnly: true);
            return null;
        }
        var live = statsDict[_statDefinition];
        if (live == null) WarnOnce("null-live", $"'{MirrorStatUid}' has a null live instance this run.", debugOnly: true);
        return live;
    }
}
