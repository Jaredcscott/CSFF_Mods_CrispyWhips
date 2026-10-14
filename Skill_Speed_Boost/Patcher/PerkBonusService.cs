using System.Collections;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// Which perks PerkBonusMap names the player holds right now (plan C4), and the multiplier they give a
/// skill. A perk is held when it was picked at character creation (<c>CardUtil.IsPerkEquipped</c>) OR
/// granted during the run (<c>GameManager.InRunAddedPerks</c>). The second half matters: an in-run grant
/// never enters <c>CurrentPlayerCharacter.CharacterPerks</c>, the only list <c>IsPerkEquipped</c> walks, so
/// that helper answers false for it, permanently and with no log line (root CLAUDE.md "Perks"). The
/// Academy graduate perks of Community Mod Chest are exactly that kind, granted by a Study action or, for
/// the Graduate trait, at run start. The scan open-codes the idiom CMC's AcademyCourseService.HasCourse and
/// InRunPerkHeld use, by reflection, and calls nothing of CMC's: a perk id from a mod that is not installed
/// is held by nobody, so the shipped default map is inert without it.
///
/// The held set is cached for one second, as PlayerConditionService caches its verdicts, because this runs
/// on every skill XP gain. The cache is also dropped the moment the number of in-run perks changes, so a
/// perk granted a heartbeat ago speeds the very next action instead of the one after the second runs out.
/// Every failure is a non-throwing return that warns once per cause (root CLAUDE.md "Silent Catch
/// Blocks"): a dead perk scan would otherwise read exactly like a player who has no perk.
/// </summary>
internal static class PerkBonusService
{
    private static ManualLogSource Logger => Plugin.Logger;

    private const float CacheSeconds = 1.0f;

    // What the cached set was computed for: the map (replaced whole on every change, so a reference
    // compare is a change test), the clock, and how many in-run perks there were.
    private static object _mapRead;
    private static float _readAtRealtime = float.NegativeInfinity;
    private static int _inRunCountRead = -1;
    private static List<string> _held = new List<string>();
    private static string _heldDescribed = "";

    private static readonly HashSet<string> _warnedCauses = new(StringComparer.Ordinal);

    /// <summary>
    /// The PerkBonusMap multiplier for <paramref name="skill"/>: the highest entry among the mapped perks
    /// the player holds, else 1.
    /// </summary>
    internal static float GetMultiplier(Dictionary<string, Dictionary<string, float>> map, string skill)
    {
        if (map == null || map.Count == 0 || string.IsNullOrEmpty(skill)) return 1f;
        return ScopedBonusConfig.HighestFor(skill, Held(map), map);
    }

    private static List<string> Held(Dictionary<string, Dictionary<string, float>> map)
    {
        float now = Time.realtimeSinceStartup;
        IList inRun = ReadInRunPerks();
        int inRunCount = inRun == null ? -1 : inRun.Count;
        if (ReferenceEquals(map, _mapRead) && inRunCount == _inRunCountRead && now - _readAtRealtime < CacheSeconds)
            return _held;

        var held = new List<string>();
        foreach (string perkUid in map.Keys)
        {
            // Not CardUtil.IsPerkHeld, which tests the same two lists: it exists only from framework 2.28.0,
            // and a player on an older framework would get a MissingMethodException on every skill gain
            // (re-audit 2026-10-13, M-4). Switch once 2.28.0+ is the published framework minimum.
            if (CardUtil.IsPerkEquipped(perkUid) || InRunHolds(inRun, perkUid))
                held.Add(perkUid);
        }

        _mapRead = map;
        _readAtRealtime = now;
        _inRunCountRead = inRunCount;
        _held = held;

        // One Debug line when the set changes, so a log shows that a perk was seen (or why it was not)
        // without a line per XP gain.
        string described = held.Count == 0 ? "none" : string.Join(", ", held.ToArray());
        if (described != _heldDescribed)
        {
            _heldDescribed = described;
            Logger?.LogDebug($"[PerkBonus] mapped perks held: {described}.");
        }
        return held;
    }

    // GameManager.InRunAddedPerks (a List<CharacterPerk>, built with the GameManager, so never null in a
    // run), or null with a warning when it cannot be read.
    private static IList ReadInRunPerks()
    {
        try
        {
            object gm = CardUtil.GetGameManagerInstance();
            if (gm == null)
            {
                WarnOnce("gamemanager", "GameManager could not be resolved while a skill gained XP, which should not happen inside a run. No perk bonus applies.");
                return null;
            }
            if (!Reflect.TryGetMember(gm, "InRunAddedPerks", out object list))
            {
                WarnOnce("inrunperks", "GameManager.InRunAddedPerks could not be read: the member was renamed or removed by a game update. Perks granted during a run (the Academy graduate perks) are not seen, so only perks picked at character creation can give a bonus.");
                return null;
            }
            if (list is IList perks) return perks;
            WarnOnce("inrunperks-type", "GameManager.InRunAddedPerks is no longer a list, so the perks granted during a run cannot be read. Only perks picked at character creation can give a bonus.");
            return null;
        }
        catch (Exception ex)
        {
            WarnOnce("throw", $"Reading the perks granted during this run failed: {ex.InnerException?.Message ?? ex.Message}. Only perks picked at character creation can give a bonus.");
            return null;
        }
    }

    private static bool InRunHolds(IList inRun, string perkUid)
    {
        if (inRun == null) return false;
        foreach (object perk in inRun)
        {
            if (perk == null) continue;
            // Reflect.TryGetMember reports a missing member rather than throwing, so a renamed UniqueID
            // would never reach a catch: warn here, once.
            if (!Reflect.TryGetMember(perk, "UniqueID", out object value))
            {
                WarnOnce("perkuid", "A perk in GameManager.InRunAddedPerks has no readable UniqueID: the member was renamed by a game update. Perks granted during a run are not seen, so only perks picked at character creation can give a bonus.");
                return false;
            }
            if (perkUid.Equals(value as string, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warnedCauses.Add(cause))
            Logger?.LogWarning($"[PerkBonus] {message}");
    }
}
