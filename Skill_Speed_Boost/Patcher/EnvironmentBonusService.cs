using System.Linq;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// The environment EnvironmentBonusMap is looked up by (plan C2): the UniqueID
/// GameQuery.CurrentEnvironmentUniqueId returns, and the asset name of that environment's card
/// (Env_CaveOldHollow, the export file stem), read from the CardData model UniqueIDScriptable.GetFromID
/// returns. Plan W0.2, confirmed live 2026-10-02: the model's name is the asset name, while
/// GM.CurrentEnvironmentCard's GameObject is renamed "0_Env_..." and must not be read for this.
///
/// Read when a skill gains XP, never on environment entry, so the "retry until the board is ready"
/// rule for entry handlers does not apply. Every failure is a non-throwing return that warns once per
/// cause (root CLAUDE.md "Silent Catch Blocks"): an unreadable environment would otherwise read
/// exactly like a place the map does not list.
/// </summary>
internal static class EnvironmentBonusService
{
    private static ManualLogSource Logger => Plugin.Logger;

    // The caller runs on every skill XP gain while the map is non-empty or LogSkillXpGains is on, and
    // the player changes environment far less often, so a one-second cache costs nothing in accuracy.
    private const float CacheSeconds = 1.0f;
    private static float _readAtRealtime = float.NegativeInfinity;
    private static string _uid;
    private static string _name;

    // An environment's asset name never changes within a session; null is cached too, after its warning.
    private static readonly Dictionary<string, string> _namesByUid = new(StringComparer.Ordinal);
    private static MethodInfo _getFromIdMethod;

    private static readonly HashSet<string> _warnedCauses = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _unlistedEnvironments = new(StringComparer.Ordinal);

    /// <summary>The current environment's UniqueID and asset name; either is null when it cannot be read.</summary>
    internal static void Current(out string uid, out string name)
    {
        float now = Time.realtimeSinceStartup;
        if (now - _readAtRealtime >= CacheSeconds)
        {
            _readAtRealtime = now;
            _uid = ReadUid();
            _name = _uid == null ? null : ReadName(_uid);
        }
        uid = _uid;
        name = _name;
    }

    /// <summary>
    /// The EnvironmentBonusMap multiplier for <paramref name="skill"/> where the player is, or 1. An
    /// environment no place in the map names leaves one Debug breadcrumb, so a prefix that a game
    /// update renamed away can be found without logging every gain.
    /// </summary>
    internal static float GetMultiplier(Dictionary<string, Dictionary<string, float>> map, string skill)
    {
        Current(out string uid, out string name);
        if (uid == null) return 1f;
        if (!ScopedBonusConfig.EnvironmentListed(map, uid, name) && _unlistedEnvironments.Add(uid))
            Logger?.LogDebug($"[EnvironmentBonus] No EnvironmentBonusMap place names env {uid} ({name ?? "no asset name"}), so no environment bonus applies there.");
        return ScopedBonusConfig.LookupEnvironment(map, uid, name, skill);
    }

    /// <summary>The [XpGain] suffix naming where a gain happened, or "" when the environment cannot be read.</summary>
    internal static string DescribeForXpLog()
    {
        Current(out string uid, out string name);
        if (uid == null) return "";
        return name == null ? $" in env {uid}" : $" in env {uid} ({name})";
    }

    private static string ReadUid()
    {
        string uid;
        try
        {
            uid = GameQuery.CurrentEnvironmentUniqueId;
        }
        catch (Exception ex)
        {
            WarnOnce("throw", $"Reading the current environment failed: {ex.InnerException?.Message ?? ex.Message}. No environment bonus applies.");
            return null;
        }

        if (string.IsNullOrEmpty(uid))
        {
            WarnOnce("null", "The current environment could not be read while a skill gained XP (GameQuery.CurrentEnvironmentUniqueId returned nothing). " +
                "The game has probably renamed GameManager.CurrentEnvironment or its MainEnvCard. No environment bonus applies.");
            return null;
        }
        return uid;
    }

    private static string ReadName(string uid)
    {
        if (_namesByUid.TryGetValue(uid, out string cached)) return cached;

        string name = null;
        try
        {
            if (!ResolveGetFromId())
            {
                WarnOnce("getfromid", "Could not reflect UniqueIDScriptable.GetFromID(string): the type or that overload was renamed by a game update. " +
                    "Only EnvironmentBonusMap places written as a UniqueID can match.");
            }
            else if (_getFromIdMethod.Invoke(null, new object[] { uid }) is UnityEngine.Object model && model != null)
            {
                name = model.name;
            }
            else
            {
                // A clean null, not an exception: GetFromID does not know this UniqueID.
                WarnOnce("model:" + uid, $"GetFromID resolved no card for environment {uid}, so only a place written as that UniqueID can match there.");
            }
        }
        catch (Exception ex)
        {
            // InnerException first: GetFromID is a reflective Invoke.
            WarnOnce("namethrow:" + uid, $"Reading the asset name of environment {uid} failed: {ex.InnerException?.Message ?? ex.Message}. " +
                "Only a place written as that UniqueID can match there.");
        }

        _namesByUid[uid] = name;
        return name;
    }

    // The same lookup PlayerConditionService.ResolveGetFromId makes: the non-generic GetFromID(string).
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

    private static void WarnOnce(string cause, string message)
    {
        if (_warnedCauses.Add(cause))
            Logger?.LogWarning($"[EnvironmentBonus] {message}");
    }
}
