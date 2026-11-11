using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Util;
using UnityEngine;

namespace CSFFModFramework.Api;

/// <summary>
/// Public NPCDuty/NPCDutyAction graft surface (2.29.0, Fleet APE Port row 2): the same reflection
/// idiom every duty-graft site already uses (CMC's <c>AshPartnerDutyPatch.cs</c>,
/// <c>CottageResidentWorkDutyPatch.cs</c>, <c>GuardDutyPatch.cs</c>; WaterDrivenInfrastructure's
/// <c>MillDutyPatch.cs</c>), centralized so a game-version field rename needs fixing once, not four
/// times (Documentation/Research/Fleet/APE_Port/W0.10_redundant_code.md, row 2).
///
/// <para><b>Why reflection, not <see cref="CSFFModFramework.Animals.DutyBuilder"/>'s typed
/// construction.</b> A content mod's Assembly-CSharp reference is the stripped compile-time stub
/// (root CLAUDE.md "Harmony Patching Pitfalls") and cannot name <c>NPCDuty</c>/<c>MoveDutyAction</c>/
/// <c>NPCDutyRef</c> at all, so every public member here takes and returns <c>object</c> and names an
/// enum value by its string, matching every existing duty-graft site's own convention, not
/// DutyBuilder's (which the framework itself can afford only because it, unlike a content mod,
/// compiles against the real types). Reusing each site's PROVEN reflection path byte-for-byte, rather
/// than reimplementing the same fields through DutyBuilder's typed path, is also what keeps this an
/// equivalence-preserving dedup instead of a second, untested construction route.</para>
///
/// <para><b>Equivalence, not new behavior.</b> <see cref="NewDuty"/> and <see cref="NewMoveAction"/>
/// set only the fields every one of the four call sites already set identically, plus the null-array
/// hygiene DutyBuilder's own header documents as an invariant ("every array field is materialized
/// non-null"): an unset array field reads as C# <c>null</c>, which the engine's own duty-selection
/// loops do not all guard, so filling it with an empty array instead can only remove a latent
/// NullReferenceException, never change a schedule. Every per-duty field a caller's CURRENT code sets
/// explicitly (gate conditions, <c>CannotPerformWhileHidden</c>, <c>CanPerformWhileInDialog</c>, hour
/// windows, move destinations) is left for the caller to set the same way it always did, via
/// <see cref="Reflect.SetMember"/> or <see cref="SetEnumField"/>.</para>
///
/// <para><b>Testability.</b> <c>ScriptableObject.CreateInstance(Type)</c> on a real engine type throws
/// "ECall methods must be packaged into a system module" outside the live Unity process (confirmed by
/// <c>Framework-BlueprintAlternates.Tests.ps1</c>'s own header for the same reason), so, like
/// <c>Api.BlueprintAlternates</c>, the one seam that touches it (<see cref="CreateScriptableObject"/>)
/// is swappable, and the resolved <see cref="Type"/> fields below can be reflect-set directly by a test
/// before first use, bypassing the by-name scan so a test's own Add-Type replica classes stand in for
/// the real game types. <c>Development_Tools/Tests/Framework-NpcDuties.Tests.ps1</c> drives the real
/// compiled methods this way and asserts the resulting duty graphs field-by-field.</para>
/// </summary>
public static class NpcDuties
{
    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    private static void WarnOnce(string key, string message)
    {
        if (!_warned.Add(key)) return;
        Log.Warn(message);
    }

    // ── type resolution, memoized: the same CardUtil.FindGameType idiom every duty-graft site
    // already uses once, instead of four times. A test may reflect-set these fields directly (bypassing
    // the by-name scan) before first use, to substitute its own replica types. ────────────────────────
    private static Type _npcDutyType, _npcDutyActionType, _moveDutyActionType, _npcDutyRefType,
        _generalConditionType, _npcDutyWeightsType, _inGameTimeConditionType, _localizedStringType,
        _cardDataType, _cardTagType, _cardOrTagRefWithDurabilitiesType, _npcStatInstantModifierType;

    private static bool ResolveTypes()
    {
        if (_npcDutyType != null) return true;

        var npcDutyType = CardUtil.FindGameType("NPCDuty");
        var npcDutyActionType = CardUtil.FindGameType("NPCDutyAction");
        var moveDutyActionType = CardUtil.FindGameType("MoveDutyAction");
        var npcDutyRefType = CardUtil.FindGameType("NPCDutyRef");
        var generalConditionType = CardUtil.FindGameType("GeneralCondition");
        var npcDutyWeightsType = CardUtil.FindGameType("NPCDutyWeights");
        var inGameTimeConditionType = CardUtil.FindGameType("InGameTimeCondition");
        var localizedStringType = CardUtil.FindGameType("LocalizedString");
        var cardDataType = CardUtil.FindGameType("CardData");
        var cardTagType = CardUtil.FindGameType("CardTag");
        var cardOrTagRefWithDurabilitiesType = CardUtil.FindGameType("CardOrTagRefWithDurabilities");
        var npcStatInstantModifierType = CardUtil.FindGameType("NPCStatInstantModifier");

        if (npcDutyType == null || npcDutyActionType == null || moveDutyActionType == null
            || npcDutyRefType == null || generalConditionType == null || npcDutyWeightsType == null
            || inGameTimeConditionType == null || localizedStringType == null || cardDataType == null
            || cardTagType == null || cardOrTagRefWithDurabilitiesType == null || npcStatInstantModifierType == null)
        {
            WarnOnce("ResolveTypes", "NpcDuties.ResolveTypes: one or more NPCDuty-graph game types were not found, so duty construction is unavailable.");
            return false;
        }

        _npcDutyType = npcDutyType;
        _npcDutyActionType = npcDutyActionType;
        _moveDutyActionType = moveDutyActionType;
        _npcDutyRefType = npcDutyRefType;
        _generalConditionType = generalConditionType;
        _npcDutyWeightsType = npcDutyWeightsType;
        _inGameTimeConditionType = inGameTimeConditionType;
        _localizedStringType = localizedStringType;
        _cardDataType = cardDataType;
        _cardTagType = cardTagType;
        _cardOrTagRefWithDurabilitiesType = cardOrTagRefWithDurabilitiesType;
        _npcStatInstantModifierType = npcStatInstantModifierType;
        return true;
    }

    /// <summary>
    /// The one seam that touches the live Unity engine (<c>ScriptableObject.CreateInstance</c>):
    /// swappable so a test can substitute a plain <c>Activator.CreateInstance</c> over its own replica
    /// types instead. Not part of the public contract; reflect-set it (BindingFlags Static|NonPublic).
    /// </summary>
    private static Func<Type, object> CreateScriptableObject = RealCreateScriptableObject;

    private static object RealCreateScriptableObject(Type t) => ScriptableObject.CreateInstance(t);

    // ── duty / action / ref construction ──────────────────────────────────────

    /// <summary>
    /// A fresh <c>NPCDuty</c> with the base every grafted duty needs: a stamped UniqueID (NOT
    /// registered into the game's UID table, since these ad-hoc duties live only on the one agent's
    /// <c>AgentDuties</c>, same as the shipped Ash/Cottage/Guard pattern), a localized
    /// <c>DutyName</c>, <c>MaxPerformPerDay = 0</c>, <c>DutyExecutionOptions =
    /// EarlyBeforeDurabilities</c>, <c>GiveUpOptions = GiveUpInstantly</c>, empty
    /// <c>ValidTimesOfDay</c>/<c>ActionSequence</c>, and the private <c>DutyConditions</c> field
    /// defaulted to an always-true <see cref="EmptyCondition"/>. Returns null (after one warning) if
    /// the game types cannot be resolved. Set anything else (gate conditions, <c>CanOnlyPerformAtHome</c>,
    /// <c>CannotPerformWhileHidden</c>, <c>CanPerformWhileInDialog</c>, <c>ValidTimesOfDay</c> via
    /// <see cref="BuildHourWindow"/>) with <see cref="Reflect.SetMember"/> after this call, exactly as
    /// each duty-graft site already does.
    /// </summary>
    public static object NewDuty(string uid, string soName, string displayText)
    {
        if (!ResolveTypes()) return null;

        var duty = CreateScriptableObject(_npcDutyType);
        Reflect.SetMember(duty, "name", soName); // UnityEngine.Object.name is a real property, not reflection-fragile.
        Reflect.SetMemberAny(duty, uid, "UniqueID", "uniqueID", "m_UniqueID");

        var localizedName = Activator.CreateInstance(_localizedStringType);
        Reflect.SetMember(localizedName, "ParentObjectID", "");
        Reflect.SetMember(localizedName, "LocalizationKey", "IGNOREKEY");
        Reflect.SetMember(localizedName, "DefaultText", displayText);
        Reflect.SetMember(duty, "DutyName", localizedName);

        Reflect.SetMember(duty, "ValidTimesOfDay", Array.CreateInstance(_inGameTimeConditionType, 0));
        Reflect.SetMember(duty, "CanOnlyPerformAtHome", false);
        Reflect.SetMember(duty, "CannotPerformWhileHidden", false);
        Reflect.SetMember(duty, "MaxPerformPerDay", 0);
        SetEnumField(duty, "DutyExecutionOptions", "EarlyBeforeDurabilities");
        SetEnumField(duty, "GiveUpOptions", "GiveUpInstantly");
        Reflect.SetMember(duty, "ActionSequence", Array.CreateInstance(_npcDutyActionType, 0));

        if (!SetDutyConditions(duty, EmptyCondition()))
            WarnOnce($"NewDuty.conditions.{soName}", $"NpcDuties.NewDuty: NPCDuty.DutyConditions not found on '{soName}', so the duty will be ungated.");

        return duty;
    }

    /// <summary>
    /// A fresh <c>MoveDutyAction</c> with <c>RequiredForSelectingDuty = false</c> and every array
    /// field the engine iterates without a null guard materialized empty (<c>MoveToEnvironments</c>,
    /// <c>MoveToTags</c>, <c>MoveToItems</c>, <c>MoveToDutyCards</c>, <c>MoveCosts</c>) plus
    /// <c>CostRequirements = IgnoreCost</c>. Returns null (after one warning) if the game types cannot
    /// be resolved. Set <c>MovementType</c>, <c>MoveDestination</c>, <c>DestinationSelection</c> and
    /// the actual destination (<c>MoveToEnvironments</c>, ...) after this call with
    /// <see cref="SetEnumField"/> / <see cref="Reflect.SetMember"/>.
    /// </summary>
    public static object NewMoveAction(string soName)
    {
        if (!ResolveTypes()) return null;

        var action = CreateScriptableObject(_moveDutyActionType);
        Reflect.SetMember(action, "name", soName);
        Reflect.SetMember(action, "RequiredForSelectingDuty", false);
        Reflect.SetMember(action, "MoveToEnvironments", Array.CreateInstance(_cardDataType, 0));
        Reflect.SetMember(action, "MoveToTags", Array.CreateInstance(_cardTagType, 0));
        Reflect.SetMember(action, "MoveToItems", Array.CreateInstance(_cardOrTagRefWithDurabilitiesType, 0));
        Reflect.SetMember(action, "MoveToDutyCards", Array.CreateInstance(_npcDutyType, 0));
        Reflect.SetMember(action, "MoveCosts", Array.CreateInstance(_npcStatInstantModifierType, 0));
        SetEnumField(action, "CostRequirements", "IgnoreCost");
        return action;
    }

    /// <summary>Sets <c>NPCDuty.ActionSequence</c> to the given actions, in order.</summary>
    public static void SetActionSequence(object duty, params object[] actions)
    {
        if (!ResolveTypes() || duty == null) return;
        var sequence = Array.CreateInstance(_npcDutyActionType, actions?.Length ?? 0);
        for (int i = 0; i < sequence.Length; i++) sequence.SetValue(actions[i], i);
        Reflect.SetMember(duty, "ActionSequence", sequence);
    }

    /// <summary>
    /// Boxes the given <see cref="BuildDutyRef"/> results into an <c>NPCDutyRef[]</c> ready to
    /// assign straight onto <c>NPCAgent.AgentDuties</c> via <see cref="Reflect.SetMember"/>, the
    /// field's exact array type, which a caller that cannot name <c>NPCDutyRef</c> has no other way
    /// to construct.
    /// </summary>
    public static object ToDutyRefArray(params object[] dutyRefs)
    {
        if (!ResolveTypes()) return null;
        var array = Array.CreateInstance(_npcDutyRefType, dutyRefs?.Length ?? 0);
        for (int i = 0; i < array.Length; i++) array.SetValue(dutyRefs[i], i);
        return array;
    }

    /// <summary>
    /// A fresh <c>NPCDutyRef</c> pointing at <paramref name="duty"/>: <c>ActivatingMode =
    /// ActivateAutomatically</c> (not <c>AlwaysActive</c>, since that branch is skipped by the per-tick
    /// reactivation loop, so this is what re-activates an unregistered duty after a save/reload, per
    /// every existing duty-graft site's own doc comment), <c>StartActive = true</c>, an always-true
    /// <see cref="EmptyCondition"/> gate, and flat <see cref="EmptyWeights"/> at <paramref
    /// name="baseWeight"/>.
    /// </summary>
    public static object BuildDutyRef(object duty, int baseWeight)
    {
        if (!ResolveTypes()) return null;
        var dutyRef = Activator.CreateInstance(_npcDutyRefType);
        Reflect.SetMember(dutyRef, "TargetDuty", duty);
        SetEnumField(dutyRef, "ActivatingMode", "ActivateAutomatically");
        Reflect.SetMember(dutyRef, "StartActive", true);
        Reflect.SetMember(dutyRef, "ActivatingConditions", EmptyCondition());
        Reflect.SetMember(dutyRef, "PreferenceWeights", EmptyWeights(baseWeight));
        return dutyRef;
    }

    // ── shared struct builders ───────────────────────────────────────────────

    /// <summary>An always-true <c>GeneralCondition</c> (every array field materialized empty, matching
    /// what Unity/JSON deserialization always produces: <c>default(GeneralCondition)</c> has null
    /// arrays, which not every engine consumer null-checks).</summary>
    public static object EmptyCondition()
    {
        if (!ResolveTypes()) return null;
        object boxed = Activator.CreateInstance(_generalConditionType);
        foreach (var field in _generalConditionType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!field.FieldType.IsArray) continue;
            field.SetValue(boxed, Array.CreateInstance(field.FieldType.GetElementType(), 0));
        }
        return boxed;
    }

    /// <summary>Flat <c>NPCDutyWeights</c> at <paramref name="baseWeight"/>: every distance/card/stat
    /// weight-modifier array empty, so weight never varies with anything but the caller's tie-break
    /// value.</summary>
    public static object EmptyWeights(int baseWeight)
    {
        if (!ResolveTypes()) return null;
        object boxed = Activator.CreateInstance(_npcDutyWeightsType);
        foreach (var field in _npcDutyWeightsType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!field.FieldType.IsArray) continue;
            field.SetValue(boxed, Array.CreateInstance(field.FieldType.GetElementType(), 0));
        }
        var baseWeightField = _npcDutyWeightsType.GetField("BaseWeight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        baseWeightField?.SetValue(boxed, baseWeight);
        return boxed;
    }

    /// <summary>A one-entry <c>[startHour, endHour)</c> wrap-around-capable <c>InGameTimeCondition[]</c>,
    /// ready to assign straight onto <c>NPCDuty.ValidTimesOfDay</c> via <see cref="Reflect.SetMember"/>.</summary>
    public static object BuildHourWindow(int startHour, int endHour)
    {
        if (!ResolveTypes()) return null;
        var condition = Activator.CreateInstance(_inGameTimeConditionType);
        SetEnumField(condition, "TimeType", "HourIs");
        Reflect.SetMember(condition, "StartValue", startHour);
        Reflect.SetMember(condition, "EndValue", endHour);
        var arr = Array.CreateInstance(_inGameTimeConditionType, 1);
        arr.SetValue(condition, 0);
        return arr;
    }

    /// <summary>
    /// Sets an enum-typed field by its value's name (plain <see cref="Reflect.SetMember"/> cannot
    /// parse an enum from a string). Instance/public/non-public, declared-only (matches every existing
    /// duty-graft site's own copy). Warns once per (declaring type, field name) on a miss or a bad
    /// value name, never throws.
    /// </summary>
    public static void SetEnumField(object instance, string fieldName, string enumValueName)
    {
        if (instance == null || string.IsNullOrEmpty(fieldName)) return;
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
        {
            WarnOnce($"SetEnumField.missing.{instance.GetType().Name}.{fieldName}",
                $"NpcDuties.SetEnumField: enum field '{fieldName}' not found on {instance.GetType().Name}.");
            return;
        }
        try { field.SetValue(instance, Enum.Parse(field.FieldType, enumValueName)); }
        catch (Exception ex)
        {
            WarnOnce($"SetEnumField.badvalue.{instance.GetType().Name}.{fieldName}.{enumValueName}",
                $"NpcDuties.SetEnumField: failed to set {instance.GetType().Name}.{fieldName} = '{enumValueName}': {Log.ExceptionText(ex)}");
        }
    }

    // ── NPCDuty's private condition field ────────────────────────────────────

    private static FieldInfo _dutyConditionsField;

    /// <summary>
    /// Sets <paramref name="duty"/>'s <c>DutyConditions</c> (private [SerializeField]) to
    /// <paramref name="condition"/>, the gate a caller builds on top of <see cref="EmptyCondition"/>
    /// (e.g. a <c>StatValueTrigger</c> range). Returns false if the field is missing.
    /// </summary>
    public static bool SetDutyConditions(object duty, object condition)
    {
        if (!ResolveTypes() || duty == null) return false;
        _dutyConditionsField ??= _npcDutyType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(f => f.Name == "DutyConditions");
        if (_dutyConditionsField == null) return false;
        _dutyConditionsField.SetValue(duty, condition);
        return true;
    }
}
