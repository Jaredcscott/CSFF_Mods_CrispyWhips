using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using CSFFModFramework.Api;
using HarmonyLib;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// Hooks GameManager.ActionRoutine to capture the context of the action the player is running: the
/// location card it is aimed at, the action itself and the tool it uses (the given card). The
/// ChangeStatValue postfix in MorningBonusPatch runs inside that coroutine and reads the context for
/// its CONSUMERS: the familiarity multiplier, the station bonus (StationBonusMap), the tool and action
/// bonuses (ToolBonusMap and ActionBonusMap, plan C3), and the structure, action and tool named at the
/// end of a LogSkillXpGains line. Capture must therefore run whenever any of them is on (plan S6);
/// gating it on familiarity alone once left the station bonus silently dead for a player who had
/// turned familiarity off (the Hardcore and Legacy presets do). Visits are only COUNTED while
/// familiarity itself is on.
/// </summary>
internal static class AreaFamiliarityPatch
{
    private static ManualLogSource Logger => Plugin.Logger;

    // Unity coroutines run on the main thread serially within an action chain — plain static is safe.
    private static string _currentLocationUid;
    private static bool _skillXpGainedThisAction;

    // The innermost running action's ActionName (default text and localization key) and its tool's
    // UniqueID and keys (that UniqueID, then its tag names). Saved and restored around nested actions
    // exactly like _currentLocationUid.
    private static string _currentActionName;
    private static string _currentActionKey;
    private static string _currentGivenUid;
    private static string[] _currentGivenKeys;

    public static string CurrentLocationUid => _currentLocationUid;
    public static string CurrentActionName => _currentActionName;
    public static string CurrentActionKey => _currentActionKey;
    public static string CurrentGivenUid => _currentGivenUid;
    /// <summary>The tool's UniqueID, then its tag names; null for an action with no tool.</summary>
    public static string[] CurrentGivenKeys => _currentGivenKeys;
    public static void NoteSkillXpGained() => _skillXpGainedThisAction = true;

    /// <summary>
    /// Clears the action context at run start. The ActionRoutine postfix restores it in a finally, but
    /// a coroutine abandoned mid-action (quit to menu, an exception in the original) never resumes to
    /// run that finally, and a stale structure uid would colour a later non-structure gain with that
    /// structure's familiarity and station bonus.
    /// </summary>
    public static void ResetActionContext()
    {
        _currentLocationUid = null;
        _skillXpGainedThisAction = false;
        _currentActionName = null;
        _currentActionKey = null;
        _currentGivenUid = null;
        _currentGivenKeys = null;
    }

    // Resolved by parameter NAME (plan W0.3: _Action at 0, _GivenCard at 6 on EA 0.68b); -1 = absent.
    private static int _receivingCardArgIndex = 1;
    private static int _actionArgIndex = -1;
    private static int _givenCardArgIndex = -1;

    // D17: fires on every ActionRoutine call while familiarity is enabled — log the first
    // failure only so a future field-rename leaves a breadcrumb without spamming the log.
    private static bool _locationUidFailureLogged;

    // One warning per cause for the action and tool reads (a renamed member reads as null, not a throw).
    private static readonly HashSet<string> _contextWarned = new HashSet<string>(StringComparer.Ordinal);

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
        if (!Plugin.AreaFamiliarityEnabled && !Plugin.StationBonusEnabled && !Plugin.LogSkillXpGains
            && !Plugin.ToolBonusEnabled && !Plugin.ActionBonusEnabled)
        {
            yield return enumerator;
            yield break;
        }

        // Save outer context (nested actions: e.g. action triggers another action via durabilities)
        string priorUid = _currentLocationUid;
        bool priorXpFlag = _skillXpGainedThisAction;
        string priorActionName = _currentActionName;
        string priorActionKey = _currentActionKey;
        string priorGivenUid = _currentGivenUid;
        string[] priorGivenKeys = _currentGivenKeys;

        var receivingCard = ArgAt(__args, _receivingCardArgIndex);
        var newUid = TryGetLocationUid(receivingCard);
        if (!string.IsNullOrEmpty(newUid))
        {
            _currentLocationUid = newUid;
            _skillXpGainedThisAction = false;
        }

        // The action and its tool belong to THIS action, so they are set even when unreadable (null):
        // a nested self-action has no tool, and must not inherit the outer action's.
        ReadAction(ArgAt(__args, _actionArgIndex), out _currentActionName, out _currentActionKey);
        _currentGivenKeys = ReadToolKeys(ArgAt(__args, _givenCardArgIndex), out _currentGivenUid);

        try
        {
            yield return enumerator;
        }
        finally
        {
            // Record visit only at the action that introduced this UID, only if XP was gained, and
            // only while familiarity is on (tracking also runs for the station bonus and the log).
            if (!string.IsNullOrEmpty(newUid) && _skillXpGainedThisAction && Plugin.AreaFamiliarityEnabled)
            {
                AreaFamiliarityService.RecordVisit(newUid);
            }

            _currentLocationUid = priorUid;
            _skillXpGainedThisAction = priorXpFlag;
            _currentActionName = priorActionName;
            _currentActionKey = priorActionKey;
            _currentGivenUid = priorGivenUid;
            _currentGivenKeys = priorGivenKeys;
        }
    }

    /// <summary>
    /// " by action 'Grind' (key ...)" then " with tool UniqueID (tags a, b)" for the end of an [XpGain]
    /// line: the keys a player copies into ActionBonusMap and ToolBonusMap (plan C3, appended last per
    /// plan S4). Empty when the running action names neither.
    /// </summary>
    internal static string DescribeForXpLog()
    {
        string text = "";
        if (!string.IsNullOrEmpty(_currentActionName) || !string.IsNullOrEmpty(_currentActionKey))
        {
            text += $" by action '{_currentActionName ?? ""}'";
            if (!string.IsNullOrEmpty(_currentActionKey)) text += $" (key {_currentActionKey})";
        }

        var keys = _currentGivenKeys;
        if (keys != null && keys.Length > 0)
        {
            var tags = new List<string>();
            foreach (string key in keys)
            {
                if (!string.Equals(key, _currentGivenUid, StringComparison.Ordinal)) tags.Add(key);
            }
            text += $" with tool {(string.IsNullOrEmpty(_currentGivenUid) ? "(no UniqueID)" : _currentGivenUid)}";
            if (tags.Count > 0) text += $" (tags {string.Join(", ", tags)})";
        }
        return text;
    }

    private static object ArgAt(object[] args, int index)
    {
        return args != null && index >= 0 && index < args.Length ? args[index] : null;
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
            _actionArgIndex = -1;
            _givenCardArgIndex = -1;
            bool receivingFound = false;
            for (int i = 0; i < parameters.Length; i++)
            {
                var name = parameters[i].Name ?? string.Empty;
                if (!receivingFound && name.IndexOf("receiving", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _receivingCardArgIndex = i;
                    receivingFound = true;
                }
                else if (_givenCardArgIndex < 0 && name.IndexOf("given", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _givenCardArgIndex = i;
                }
                else if (_actionArgIndex < 0 && string.Equals(name.Trim('_'), "Action", StringComparison.OrdinalIgnoreCase))
                {
                    _actionArgIndex = i;
                }
            }

            if (_actionArgIndex < 0)
                Logger.LogDebug("[ToolActionBonus] GameManager.ActionRoutine has no _Action parameter, so ActionBonusMap gives no bonus and [XpGain] names no action.");
            if (_givenCardArgIndex < 0)
                Logger.LogDebug("[ToolActionBonus] GameManager.ActionRoutine has no _GivenCard parameter, so ToolBonusMap gives no bonus and [XpGain] names no tool.");

            return method;
        }

        return null;
    }

    // The action's ActionName (a LocalizedString struct): its default text and its key, trimmed. A
    // null action is a legitimate no-action call; a member that no longer exists is warned about once.
    private static void ReadAction(object action, out string name, out string key)
    {
        name = null;
        key = null;
        if (action == null) return;
        try
        {
            if (!Reflect.TryGetMember(action, "ActionName", out var actionName) || actionName == null)
            {
                WarnContextOnce("ActionName", "an action has no readable ActionName, so ActionBonusMap gives no bonus.");
                return;
            }
            if (!Reflect.TryGetMember(actionName, "DefaultText", out var text))
                WarnContextOnce("DefaultText", "ActionName has no DefaultText, so ActionBonusMap matches action keys only.");
            if (!Reflect.TryGetMember(actionName, "LocalizationKey", out var localizationKey))
                WarnContextOnce("LocalizationKey", "ActionName has no LocalizationKey, so ActionBonusMap matches action names only.");
            name = Trimmed(text as string);
            key = Trimmed(localizationKey as string);
        }
        catch (Exception ex)
        {
            WarnContextOnce("action-throw", $"reading the action failed, so ActionBonusMap gives no bonus: {ex.Message}");
        }
    }

    // The tool's UniqueID, then the name of each of its tags (tag_Axe): the keys ToolBonusMap is
    // looked up by. Null for an action with no tool (a card's own button) or one already destroyed.
    private static string[] ReadToolKeys(object card, out string uid)
    {
        uid = null;
        if (card == null) return null;
        if (card is UnityEngine.Object unityCard && unityCard == null) return null;
        try
        {
            if (!Reflect.TryGetMember(card, "CardModel", out var model))
            {
                WarnContextOnce("CardModel", "a tool card has no CardModel member, so ToolBonusMap gives no bonus.");
                return null;
            }
            if (model == null)
            {
                // Legitimate: CardOnCardActionRoutine applies the given card's own change (a Destroy
                // included) before ActionRoutine starts, so a tool the action uses up can arrive empty.
                if (_contextWarned.Add("CardModel-null"))
                    Logger?.LogDebug("[ToolActionBonus] a tool card arrived with no model (its action used it up), so it gave ToolBonusMap no keys.");
                return null;
            }

            var keys = new List<string>();
            if (Reflect.TryGetMember(model, "UniqueID", out var uniqueId))
            {
                uid = Trimmed(uniqueId as string);
                if (uid != null) keys.Add(uid);
            }
            else
            {
                WarnContextOnce("UniqueID", "a tool's card has no UniqueID member, so ToolBonusMap matches tags only.");
            }

            if (Reflect.TryGetMember(model, "CardTags", out var tags))
            {
                if (tags is IEnumerable tagList)
                {
                    foreach (var tag in tagList)
                    {
                        if (tag is UnityEngine.Object tagObject && tagObject != null && !string.IsNullOrEmpty(tagObject.name))
                            keys.Add(tagObject.name);
                    }
                }
            }
            else
            {
                WarnContextOnce("CardTags", "a tool's card has no CardTags member, so ToolBonusMap matches UniqueIDs only.");
            }

            return keys.Count == 0 ? null : keys.ToArray();
        }
        catch (Exception ex)
        {
            WarnContextOnce("tool-throw", $"reading the tool failed, so ToolBonusMap gives no bonus: {ex.Message}");
            return null;
        }
    }

    private static string Trimmed(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static void WarnContextOnce(string cause, string message)
    {
        if (_contextWarned.Add(cause))
            Logger?.LogWarning($"[ToolActionBonus] {message}");
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
        catch (Exception ex)
        {
            if (!_locationUidFailureLogged)
            {
                _locationUidFailureLogged = true;
                Logger?.LogWarning($"[AreaFamiliarity] TryGetLocationUid reflection failed (familiarity tracking will silently stop): {ex.Message}");
            }
        }
        return null;
    }
}
