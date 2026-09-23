using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Util;
using HarmonyLib;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Keeps CMC's composite trait stats equal to the stat they read, across travel.
    ///
    /// Reported by Chiwei (Nexus, 2026-09-22): light a campfire with Nightcrawler (CMC 1.68.36,
    /// then a composite of Light), leave the map without putting it out, and Sunlight Exposure keeps
    /// the fire's +25 until a reload; Nyctophobia, also a composite of Light, does the same.
    ///
    /// Vanilla forwards every change of a source stat to a composite as a DELTA.
    /// GameManager.ChangeStatValue captures prevValue = CurrentValue(NotInBase), applies the change,
    /// waits for the status update, then invokes OnValueChanged(CurrentValue(NotInBase) - prevValue),
    /// which InGameStat.UpdateCompositeValue adds to CurrentCompositeValue. Every vanilla fire
    /// (Campfire, FirePit, Fireplace, Hearth, HearthAwakened, SaunaStove, AlembicOn, the fire ritual
    /// pyre) feeds Light with AffectStatsOnlyOnBase, i.e. as an AtBaseModifier, and
    /// CurrentValue(_NotAtBase: true) leaves AtBase modifiers out. Travel is an out-of-base action
    /// (GameManager.ActionRoutine sets NotInBase = true for its whole duration), and ChangeEnvironment
    /// cancels the old map's fire and applies the new map's fire inside it, so both changes reach the
    /// composite as a delta of exactly 0. Hideout and ImprovisedShelter feed SunProtection the same
    /// way, which is what Nightcrawler's Sun Protection stat reads. A reload hides the drift because
    /// InGameStat.InitCompositeValue rebuilds every composite from scratch.
    ///
    /// For CMC's own composites this prefix replaces the delta with an absolute resync: the composite
    /// is set to the sum of its sources' CurrentValue(_NotAtBase: false), which is the value a reload
    /// builds (InitCompositeValue reads SimpleCurrentValue while NotInBase is false). In the steady
    /// state that is exactly what vanilla holds; it differs only where vanilla loses a delta, and it
    /// is idempotent, so two overlapping changes to one source cannot double-count either. It goes
    /// through the game's own GameManager.ChangeStat(CompositeModifier), exactly as vanilla does, so
    /// statuses, the stat breakdown and any composite of this composite update as usual.
    ///
    /// Scope: stats whose UniqueID starts with ScopePrefix. Every composite stat any mod ships today
    /// is a CMC trait stat; Development_Tools/Tests/Trait-CompositeResyncScope.Tests.ps1 fails if a
    /// mod ever ships one outside this scope, or one with a Scale, which this patch hands back to
    /// vanilla rather than guess at (vanilla scales the deltas but InitCompositeValue does not).
    /// Vanilla composites keep vanilla behaviour. Any reflection miss falls back to vanilla, with one
    /// warning per cause.
    /// </summary>
    internal static class TraitCompositeResyncPatch
    {
        internal const string ScopePrefix = "cmcStat";

        private const float Epsilon = 0.001f;
        private static readonly object[] IncludeAtBase = { false };

        private static bool _applied;
        private static bool _ready;
        private static bool _firstCorrectionLogged;

        private static FieldInfo _statModelField;        // InGameStat.StatModel (GameStat)
        private static FieldInfo _compositeValueField;   // InGameStat.CurrentCompositeValue (float)
        private static MethodInfo _currentValueMethod;   // InGameStat.CurrentValue(bool)
        private static FieldInfo _uniqueIdField;         // UniqueIDScriptable.UniqueID (string)
        private static FieldInfo _compositeStatsField;   // GameStat.CompositeStats (CompositeStatModifier[])
        private static FieldInfo _compStatField;         // CompositeStatModifier.Stat (GameStat)
        private static FieldInfo _compScaleField;        // CompositeStatModifier.Scale (OptionalFloatValue)
        private static FieldInfo _optionalActiveField;   // OptionalValue.Active (protected bool)
        private static FieldInfo _statsDictField;        // GameManager.StatsDict (Dictionary<GameStat, InGameStat>)
        private static MethodInfo _changeStatMethod;     // GameManager.ChangeStat(StatModifier, StatModification, ...)
        private static Type _statModifierType;           // StatModifier (struct)
        private static FieldInfo _modStatField;          // StatModifier.Stat
        private static FieldInfo _modValueField;         // StatModifier.ValueModifier (Vector2)
        private static object _compositeModification;    // StatModification.CompositeModifier (boxed enum)

        // Keyed by GameStat asset; UnityEngine.Object hashes and compares by instance id.
        private static readonly Dictionary<object, bool> _inScope = new Dictionary<object, bool>();
        private static readonly HashSet<string> _warned = new HashSet<string>();

        public static void Apply(Harmony harmony)
        {
            if (_applied) return;
            _applied = true;

            var inGameStatType = CardUtil.FindGameType("InGameStat");
            var gameManagerType = CardUtil.FindGameType("GameManager");
            var gameStatType = CardUtil.FindGameType("GameStat");
            var compositeType = CardUtil.FindGameType("CompositeStatModifier");
            var optionalType = CardUtil.FindGameType("OptionalValue");
            var uniqueIdType = CardUtil.FindGameType("UniqueIDScriptable");
            var modificationType = CardUtil.FindGameType("StatModification");
            _statModifierType = CardUtil.FindGameType("StatModifier");

            if (inGameStatType == null || gameManagerType == null || gameStatType == null || compositeType == null ||
                optionalType == null || uniqueIdType == null || modificationType == null || _statModifierType == null)
            {
                Plugin.Logger.LogWarning("[TraitCompositeResyncPatch] a game type was not found (InGameStat, GameManager, GameStat, " +
                                         "CompositeStatModifier, OptionalValue, UniqueIDScriptable, StatModification or StatModifier) — " +
                                         "trait composites fall back to vanilla and can drift across travel.");
                return;
            }

            var target = AccessTools.Method(inGameStatType, "UpdateCompositeValue", new[] { typeof(float), inGameStatType });
            if (target == null)
            {
                Plugin.Logger.LogWarning("[TraitCompositeResyncPatch] InGameStat.UpdateCompositeValue(float, InGameStat) not found — " +
                                         "trait composites fall back to vanilla and can drift across travel.");
                return;
            }

            _statModelField = AccessTools.Field(inGameStatType, "StatModel");
            _compositeValueField = AccessTools.Field(inGameStatType, "CurrentCompositeValue");
            _currentValueMethod = AccessTools.Method(inGameStatType, "CurrentValue", new[] { typeof(bool) });
            _uniqueIdField = AccessTools.Field(uniqueIdType, "UniqueID");
            _compositeStatsField = AccessTools.Field(gameStatType, "CompositeStats");
            _compStatField = AccessTools.Field(compositeType, "Stat");
            _compScaleField = AccessTools.Field(compositeType, "Scale");
            _optionalActiveField = AccessTools.Field(optionalType, "Active");
            _statsDictField = AccessTools.Field(gameManagerType, "StatsDict");
            _modStatField = AccessTools.Field(_statModifierType, "Stat");
            _modValueField = AccessTools.Field(_statModifierType, "ValueModifier");
            _changeStatMethod = gameManagerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "ChangeStat" && m.GetParameters().Length == 8 &&
                                     m.GetParameters()[0].ParameterType == _statModifierType);
            try { _compositeModification = Enum.Parse(modificationType, "CompositeModifier"); }
            catch (Exception ex) { Plugin.Logger.LogDebug($"[TraitCompositeResyncPatch] StatModification.CompositeModifier not parsed: {ex.Message}"); }

            var missing = new List<string>();
            if (_statModelField == null) missing.Add("InGameStat.StatModel");
            if (_compositeValueField == null) missing.Add("InGameStat.CurrentCompositeValue");
            if (_currentValueMethod == null) missing.Add("InGameStat.CurrentValue(bool)");
            if (_uniqueIdField == null) missing.Add("UniqueIDScriptable.UniqueID");
            if (_compositeStatsField == null) missing.Add("GameStat.CompositeStats");
            if (_compStatField == null) missing.Add("CompositeStatModifier.Stat");
            if (_compScaleField == null) missing.Add("CompositeStatModifier.Scale");
            if (_optionalActiveField == null) missing.Add("OptionalValue.Active");
            if (_statsDictField == null) missing.Add("GameManager.StatsDict");
            if (_modStatField == null) missing.Add("StatModifier.Stat");
            if (_modValueField == null) missing.Add("StatModifier.ValueModifier");
            if (_changeStatMethod == null) missing.Add("GameManager.ChangeStat(StatModifier, ...8 params)");
            if (_compositeModification == null) missing.Add("StatModification.CompositeModifier");
            if (missing.Count > 0)
            {
                Plugin.Logger.LogWarning($"[TraitCompositeResyncPatch] not found: {string.Join(", ", missing)} — " +
                                         "trait composites fall back to vanilla and can drift across travel.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(typeof(TraitCompositeResyncPatch), nameof(UpdateCompositeValue_Prefix)));
            _ready = true;
            Plugin.Logger.LogDebug("[TraitCompositeResyncPatch] applied.");
        }

        // Returns true to let vanilla forward its delta, false once this resync has been applied.
        private static bool UpdateCompositeValue_Prefix(object __instance, float _Delta, object _FromStat)
        {
            bool applied = false;
            try
            {
                if (!_ready || __instance == null || _FromStat == null) return true;
                var model = _statModelField.GetValue(__instance);
                if (model == null || !InScope(model)) return true;

                if (_compositeStatsField.GetValue(model) is not Array composites || composites.Length == 0)
                {
                    WarnOnce("no-composites", $"{Describe(model)} reached UpdateCompositeValue with no CompositeStats; left to vanilla.");
                    return true;
                }

                var gm = CardUtil.GetGameManagerInstance();
                if (gm is not MonoBehaviour gmBehaviour)
                {
                    WarnOnce("gm-null", "GameManager instance not resolved; composite left to vanilla.");
                    return true;
                }
                if (_statsDictField.GetValue(gm) is not IDictionary statsDict)
                {
                    WarnOnce("statsdict-null", "GameManager.StatsDict not readable; composite left to vanilla.");
                    return true;
                }

                var fromModel = _statModelField.GetValue(_FromStat);
                float target = 0f;
                object matched = null;
                foreach (var composite in composites)
                {
                    var source = _compStatField.GetValue(composite);
                    if (source == null || ReferenceEquals(source, model)) continue; // vanilla skips these too
                    var scale = _compScaleField.GetValue(composite);
                    if (scale != null && _optionalActiveField.GetValue(scale) is true)
                    {
                        WarnOnce("scaled:" + Describe(model), $"{Describe(model)} has a scaled composite; left to vanilla (see class comment).");
                        return true;
                    }
                    if (!statsDict.Contains(source) || statsDict[source] == null)
                    {
                        WarnOnce("source-missing:" + Describe(model), $"{Describe(model)} reads {Describe(source)}, which has no live stat; left to vanilla.");
                        return true;
                    }
                    if (_currentValueMethod.Invoke(statsDict[source], IncludeAtBase) is not float value)
                    {
                        WarnOnce("value-unreadable:" + Describe(model), $"CurrentValue(false) unreadable on {Describe(source)}; left to vanilla.");
                        return true;
                    }
                    target += value;
                    if (matched == null && ReferenceEquals(source, fromModel)) matched = composite;
                }
                if (matched == null) return true; // vanilla does nothing for a source it does not list either

                if (_compositeValueField.GetValue(__instance) is not float current)
                {
                    WarnOnce("current-unreadable", "CurrentCompositeValue unreadable; composite left to vanilla.");
                    return true;
                }
                float drift = target - current;

                var modifier = Activator.CreateInstance(_statModifierType); // boxed struct, filled in place
                _modStatField.SetValue(modifier, model);
                _modValueField.SetValue(modifier, Vector2.one * drift);
                string fromName = _FromStat is UnityEngine.Object fromObject ? fromObject.name : "?";
                if (_changeStatMethod.Invoke(gm, new object[]
                    {
                        modifier, _compositeModification, $"Composite stat modifier from {fromName}", null, 0, matched, null, false
                    }) is not IEnumerator routine)
                {
                    WarnOnce("changestat-null", "GameManager.ChangeStat returned no coroutine; composite left to vanilla.");
                    return true;
                }
                gmBehaviour.StartCoroutine(routine);
                applied = true;

                if (Mathf.Abs(drift - _Delta) > Epsilon)
                {
                    string line = $"[TraitCompositeResyncPatch] {Describe(model)}: vanilla would have forwarded {_Delta:0.##} from {fromName}; " +
                                  $"resynced by {drift:0.##} to {target:0.##} (a delta lost while out of base, e.g. a fire left behind on travel).";
                    if (!_firstCorrectionLogged)
                    {
                        _firstCorrectionLogged = true;
                        Plugin.Logger.LogInfo(line);
                    }
                    else
                    {
                        Plugin.Logger.LogDebug(line);
                    }
                }

                // The resync is only idempotent if ChangeStat writes CurrentCompositeValue before its
                // first yield, which it does in EA 0.68a (no min/max change, so nothing waits before
                // ChangeStatValue's += ). Say so if that ever stops being true.
                if (Mathf.Abs(drift) > Epsilon && _compositeValueField.GetValue(__instance) is float after &&
                    Mathf.Abs(after - target) > Epsilon)
                {
                    WarnOnce("deferred-write", $"{Describe(model)} did not reach {target:0.##} synchronously (reads {after:0.##}); " +
                                               "two source changes in one frame could now double-apply. Re-check GameManager.ChangeStat.");
                }
                return false;
            }
            catch (Exception ex)
            {
                WarnOnce("exception:" + ex.GetType().Name, $"prefix threw ({(applied ? "after" : "before")} applying): {ex.InnerException?.ToString() ?? ex.ToString()}");
                return !applied;
            }
        }

        private static bool InScope(object model)
        {
            if (_inScope.TryGetValue(model, out bool cached)) return cached;
            bool inScope = _uniqueIdField.GetValue(model) is string uid && uid.StartsWith(ScopePrefix, StringComparison.Ordinal);
            _inScope[model] = inScope;
            return inScope;
        }

        private static string Describe(object model)
        {
            if (model == null) return "(null)";
            if (_uniqueIdField != null && _uniqueIdField.GetValue(model) is string uid && uid.Length > 0) return uid;
            return model is UnityEngine.Object unityObject ? unityObject.name : model.ToString();
        }

        private static void WarnOnce(string cause, string message)
        {
            if (!_warned.Add(cause)) return;
            Plugin.Logger.LogWarning($"[TraitCompositeResyncPatch] {message}");
        }
    }
}
