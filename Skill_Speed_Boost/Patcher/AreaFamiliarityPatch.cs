using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using CSFFModFramework.Api;
using HarmonyLib;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// Hooks GameManager.ActionRoutine to track which location card the player is currently
/// interacting with. The active location UID is exposed to MorningBonusPatch's ChangeStatValue
/// postfix so it can apply a familiarity multiplier alongside any morning bonus.
/// </summary>
internal static class AreaFamiliarityPatch
{
    private static ManualLogSource Logger => Plugin.Logger;

    // Unity coroutines run on the main thread serially within an action chain — plain static is safe.
    private static string _currentLocationUid;
    private static bool _skillXpGainedThisAction;

    public static string CurrentLocationUid => _currentLocationUid;
    public static void NoteSkillXpGained() => _skillXpGainedThisAction = true;

    private static int _receivingCardArgIndex = 1;

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var gmType = Reflect.TryGetType("GameManager");
            if (gmType == null)
            {
                Logger.LogWarning("[AreaFamiliarity] GameManager type not found — area familiarity disabled.");
                return;
            }

            var method = FindActionRoutine(gmType);
            if (method == null)
            {
                Logger.LogWarning("[AreaFamiliarity] GameManager.ActionRoutine not found — area familiarity disabled.");
                return;
            }

            harmony.Patch(method,
                postfix: new HarmonyMethod(typeof(AreaFamiliarityPatch), nameof(ActionRoutine_Post)));
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AreaFamiliarity] Patch error: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    // Iterator postfix: wrap original coroutine, set/clear context around it.
    static IEnumerator ActionRoutine_Post(IEnumerator enumerator, object[] __args)
    {
        if (!Plugin.AreaFamiliarityEnabled)
        {
            yield return enumerator;
            yield break;
        }

        // Save outer context (nested actions: e.g. action triggers another action via durabilities)
        string priorUid = _currentLocationUid;
        bool priorXpFlag = _skillXpGainedThisAction;

        var receivingCard = __args != null && _receivingCardArgIndex >= 0 && _receivingCardArgIndex < __args.Length
            ? __args[_receivingCardArgIndex]
            : null;
        var newUid = TryGetLocationUid(receivingCard);
        if (!string.IsNullOrEmpty(newUid))
        {
            _currentLocationUid = newUid;
            _skillXpGainedThisAction = false;
        }

        try
        {
            yield return enumerator;
        }
        finally
        {
            // Record visit only at the action that introduced this UID, and only if XP was gained.
            if (!string.IsNullOrEmpty(newUid) && _skillXpGainedThisAction)
            {
                AreaFamiliarityService.RecordVisit(newUid);
            }

            _currentLocationUid = priorUid;
            _skillXpGainedThisAction = priorXpFlag;
        }
    }

    private static MethodInfo FindActionRoutine(Type gmType)
    {
        foreach (var method in gmType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name != "ActionRoutine" || !typeof(IEnumerator).IsAssignableFrom(method.ReturnType))
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length < 2) continue;

            _receivingCardArgIndex = 1;
            for (int i = 0; i < parameters.Length; i++)
            {
                var name = parameters[i].Name ?? string.Empty;
                if (name.IndexOf("receiving", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _receivingCardArgIndex = i;
                    break;
                }
            }

            return method;
        }

        return null;
    }

    private static string TryGetLocationUid(object receivingCard)
    {
        if (receivingCard == null) return null;
        try
        {
            var cardModel = Reflect.GetMember(receivingCard, "CardModel");
            if (cardModel == null) return null;

            // Filter to CardType=2 (locations / structures). Item-on-item drag interactions
            // produce no familiarity gain. Fallback of 2 (== the target value) replicates the
            // original behavior of skipping the filter entirely when CardType can't be read.
            int ct = Reflect.GetInt(cardModel, "CardType", 2);
            if (ct != 2) return null;

            var uid = Reflect.GetMember(cardModel, "UniqueID") as string;
            return string.IsNullOrWhiteSpace(uid) ? null : uid.Trim();
        }
        catch { }
        return null;
    }
}
