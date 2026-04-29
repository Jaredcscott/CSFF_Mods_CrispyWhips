using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
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
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Unity coroutines run on the main thread serially within an action chain — plain static is safe.
    private static string _currentLocationUid;
    private static bool _skillXpGainedThisAction;
    private static int _depth;

    public static string CurrentLocationUid => _currentLocationUid;
    public static void NoteSkillXpGained() => _skillXpGainedThisAction = true;

    private static FieldInfo _cardModelField;
    private static PropertyInfo _cardModelProp;
    private static FieldInfo _uniqueIdField;
    private static FieldInfo _cardTypeField;
    private static bool _reflectedCard;

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var gmType = AccessTools.TypeByName("GameManager");
            if (gmType == null)
            {
                Logger.LogWarning("[AreaFamiliarity] GameManager type not found — area familiarity disabled.");
                return;
            }

            var method = AccessTools.Method(gmType, "ActionRoutine");
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
            Logger.LogError($"[AreaFamiliarity] Patch error: {ex.Message}");
        }
    }

    // Iterator postfix: wrap original coroutine, set/clear context around it.
    // Param name `_ReceivingCard` matches the game's ActionRoutine parameter.
    static IEnumerator ActionRoutine_Post(IEnumerator enumerator, object _ReceivingCard)
    {
        if (!Plugin.AreaFamiliarityEnabled)
        {
            yield return enumerator;
            yield break;
        }

        // Save outer context (nested actions: e.g. action triggers another action via durabilities)
        string priorUid = _currentLocationUid;
        bool priorXpFlag = _skillXpGainedThisAction;
        _depth++;

        var newUid = TryGetLocationUid(_ReceivingCard);
        if (!string.IsNullOrEmpty(newUid))
        {
            _currentLocationUid = newUid;
            _skillXpGainedThisAction = false;
        }

        yield return enumerator;

        // Record visit only at the action that introduced this UID, and only if XP was gained
        if (!string.IsNullOrEmpty(newUid) && _skillXpGainedThisAction)
        {
            AreaFamiliarityService.RecordVisit(newUid);
        }

        _currentLocationUid = priorUid;
        _skillXpGainedThisAction = priorXpFlag;
        _depth--;
    }

    private static string TryGetLocationUid(object receivingCard)
    {
        if (receivingCard == null) return null;
        try
        {
            EnsureCardReflection(receivingCard.GetType());

            object cardModel = null;
            if (_cardModelProp != null)
                cardModel = _cardModelProp.GetValue(receivingCard, null);
            else if (_cardModelField != null)
                cardModel = _cardModelField.GetValue(receivingCard);
            if (cardModel == null) return null;

            // Filter to CardType=2 (locations / structures). Item-on-item drag interactions
            // produce no familiarity gain.
            if (_cardTypeField != null)
            {
                var ctVal = _cardTypeField.GetValue(cardModel);
                int ct;
                if (ctVal is int iv) ct = iv;
                else { try { ct = Convert.ToInt32(ctVal); } catch { return null; } }
                if (ct != 2) return null;
            }

            if (_uniqueIdField != null)
            {
                var uid = _uniqueIdField.GetValue(cardModel) as string;
                return string.IsNullOrWhiteSpace(uid) ? null : uid.Trim();
            }
        }
        catch { }
        return null;
    }

    private static void EnsureCardReflection(Type cardType)
    {
        if (_reflectedCard) return;

        // CardModel is an auto-property on InGameCardBase; try property first, fall back to backing field.
        var t = cardType;
        while (t != null && t != typeof(object))
        {
            if (_cardModelProp == null) _cardModelProp = t.GetProperty("CardModel", Flags);
            if (_cardModelField == null) _cardModelField = t.GetField("CardModel", Flags);
            if (_cardModelField == null) _cardModelField = t.GetField("<CardModel>k__BackingField", Flags);
            if (_cardModelProp != null || _cardModelField != null) break;
            t = t.BaseType;
        }

        Type cardDataType = null;
        if (_cardModelProp != null) cardDataType = _cardModelProp.PropertyType;
        else if (_cardModelField != null) cardDataType = _cardModelField.FieldType;

        if (cardDataType != null)
        {
            _uniqueIdField = cardDataType.GetField("UniqueID", Flags);
            _cardTypeField = cardDataType.GetField("CardType", Flags);
            // Some game builds back CardType as an auto-property
            if (_cardTypeField == null)
                _cardTypeField = cardDataType.GetField("<CardType>k__BackingField", Flags);
        }

        _reflectedCard = true;
    }
}
