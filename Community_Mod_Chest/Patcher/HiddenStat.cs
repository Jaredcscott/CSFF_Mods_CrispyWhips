using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Read/write access to CMC's hidden persistent GameStats by UniqueID — the
    /// GetFromID + GameManager.StatsDict + StatAccess idiom already proven in
    /// VillageReputationPatch / VillageCrimePatch / CottageResidentSpawnPatch, lifted here so
    /// a third and fourth copy of the reflection dance don't accumulate
    /// (memory: reference_gamestat_direct_csharp_write).
    ///
    /// <para>C# writes a hidden GameStat only where the value is something JSON
    /// StatModifications cannot compute — an absolute calendar day, a season index, a
    /// roll outcome. Authored StatModifications remain the default for everything else.</para>
    ///
    /// <para>Graceful degradation: an unresolvable stat reads as <c>-1</c> (distinct from a
    /// legitimate 0) and writes are dropped with a Debug breadcrumb rather than throwing.
    /// Callers treat -1 as "not readable yet" — pre-GameManager-init ticks hit this path
    /// on every fresh load.</para>
    /// </summary>
    internal static class HiddenStat
    {
        private static MethodInfo _getFromIdMethod;
        private static readonly Dictionary<string, object> _statModels = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Current value of the stat, or -1 when it is not readable yet.</summary>
        public static float Get(string statUid)
        {
            var instance = ResolveInstance(statUid);
            if (instance == null) return -1f;
            float value = StatAccess.GetCurrentValue(instance);
            return float.IsNaN(value) ? -1f : value;
        }

        /// <summary>Current live maximum (<c>CurrentMinMaxValue.y</c> — perks can shift this at
        /// runtime, unlike the JSON <c>MinMaxValue</c> default) of the stat, or -1 when it is not
        /// readable yet. Added for the Town Achievement Board's Spiritual Overcrowding detector,
        /// which needs the LIVE ceiling of the vanilla Spiritual Noise stat, not a hardcoded
        /// constant.</summary>
        public static float GetMax(string statUid)
        {
            var instance = ResolveInstance(statUid);
            if (instance == null) return -1f;
            float value = StatAccess.GetMaxValue(instance);
            return float.IsNaN(value) ? -1f : value;
        }

        /// <summary>Writes the stat's current value. Returns false when it is not writable yet.</summary>
        public static bool Set(string statUid, float value)
        {
            var instance = ResolveInstance(statUid);
            if (instance == null)
            {
                Plugin.Logger.LogDebug($"[HiddenStat] '{statUid}' not resolvable — write of {value:0.##} dropped.");
                return false;
            }
            return StatAccess.SetCurrentValue(instance, value);
        }

        /// <summary>The stat's BASE value (<c>InGameStat.CurrentBaseValue</c>: what permanent writes moved, with no
        /// passive or status modifier in it), or NaN when it is not readable yet. Used by the one-time Comfort
        /// fix-up, which must clear what actions wrote without touching what worn garments add.</summary>
        public static float GetBase(string statUid)
        {
            var instance = ResolveInstance(statUid);
            if (instance == null) return float.NaN;
            return Reflect.GetMember(instance, "CurrentBaseValue") is float f ? f : float.NaN;
        }

        private static bool _gameModifyResolved;
        private static MethodInfo _gameModify;

        /// <summary>
        /// Moves the stat's base value to <paramref name="baseValue"/>. Prefers the framework's
        /// <c>StatAccess.Modify(string, float)</c> (2.28.0+, the game's own permanent change, so statuses update),
        /// resolved by reflection for the same reason as <see cref="SetWithStatuses"/>; falls back to a raw
        /// <c>CurrentBaseValue</c> write with one warning. Returns true only when the base reads the target afterwards.
        /// </summary>
        public static bool SetBase(string statUid, float baseValue)
        {
            float current = GetBase(statUid);
            if (float.IsNaN(current)) return false;
            float delta = baseValue - current;
            if (Math.Abs(delta) < 0.0001f) return true;

            if (!_gameModifyResolved)
            {
                _gameModifyResolved = true;
                _gameModify = typeof(StatAccess).GetMethod("Modify", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string), typeof(float) }, null);
            }
            if (_gameModify != null)
            {
                try
                {
                    _gameModify.Invoke(null, new object[] { statUid, delta });
                    float after = GetBase(statUid);
                    if (!float.IsNaN(after) && Math.Abs(after - baseValue) < 0.01f) return true;
                    WarnOnce("modify-missed:" + statUid, $"StatAccess.Modify('{statUid}', {delta:0.##}) left the base at {after:0.##}, not {baseValue:0.##}; the base is written raw instead.");
                }
                catch (Exception ex)
                {
                    WarnOnce("modify-threw:" + statUid, $"StatAccess.Modify('{statUid}') threw: {ex.InnerException?.ToString() ?? ex.ToString()}. The base is written raw instead.");
                }
            }
            else
            {
                WarnOnce("modify-missing", $"CSFFModFramework.Api.StatAccess.Modify(string, float) is not in the installed framework (it needs CSFFModFramework 2.28.0 or newer), so '{statUid}' has its base written raw and its statuses update on its next change.");
            }
            var instance = ResolveInstance(statUid);
            if (instance == null) return false;
            Reflect.SetMember(instance, "CurrentBaseValue", baseValue);
            float raw = GetBase(statUid);
            return !float.IsNaN(raw) && Math.Abs(raw - baseValue) < 0.01f;
        }

        private static bool _gameSetResolved;
        private static MethodInfo _gameSet;
        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning("[HiddenStat] " + message);
        }

        /// <summary>
        /// Writes the stat through the game's own permanent stat change, so its STATUSES update. Use this,
        /// not <see cref="Set"/>, for any stat whose statuses the player is meant to get. <see cref="Set"/> is
        /// a raw field write that never reaches vanilla's <c>UpdateStatStatuses</c>, which adds a status
        /// only when a change moves the value INTO its range from outside it (<c>.decomp/GameManager.cs</c>,
        /// <c>UpdateStatStatuses</c>), so a raw write into a band never attaches that band's status (T2.965:
        /// Layered Warmth read 3 and "Well Layered" never appeared).
        ///
        /// <para>The route is framework 2.28.0's <c>CSFFModFramework.Api.StatAccess.Set(string, float)</c>
        /// (vanilla <c>ChangeStatValue</c>, <c>StatModification.Permanent</c>, started as a coroutine: the
        /// value is clamped to the stat's live range, statuses update, triggers fire). It is resolved by
        /// reflection because this mod compiles against an older framework copy
        /// (<c>HerbsAndFungi/lib/CSFFModFramework.dll</c>, which lacks it). When it is missing, throws or
        /// reports failure, the value is still written through <see cref="Set"/>, with one warning per cause
        /// saying the statuses will not update.</para>
        /// </summary>
        public static bool SetWithStatuses(string statUid, float value)
        {
            if (!_gameSetResolved)
            {
                _gameSetResolved = true;
                _gameSet = typeof(StatAccess).GetMethod("Set", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string), typeof(float) }, null);
            }
            if (_gameSet == null || _gameSet.ReturnType != typeof(bool))
            {
                WarnOnce("statsset-missing", $"CSFFModFramework.Api.StatAccess.Set(string, float) is not in the installed framework (it needs CSFFModFramework 2.28.0 or newer), so '{statUid}' and every other stat written this way is set by a raw write: the value lands but the stat's statuses (and their effects) do not update.");
                return Set(statUid, value);
            }
            try
            {
                if (_gameSet.Invoke(null, new object[] { statUid, value }) is bool ok && ok) return true;
                WarnOnce("statsset-false:" + statUid, $"StatAccess.Set('{statUid}', {value:0.##}) reported failure (the framework's own warning says why), so the value is written raw instead and the stat's statuses do not update.");
            }
            catch (Exception ex)
            {
                WarnOnce("statsset-threw:" + statUid, $"StatAccess.Set('{statUid}') threw: {ex.InnerException?.ToString() ?? ex.ToString()}. The value is written raw instead and the stat's statuses do not update.");
            }
            return Set(statUid, value);
        }

        private static object ResolveInstance(string statUid)
        {
            if (string.IsNullOrEmpty(statUid)) return null;
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return null;

                if (!_statModels.TryGetValue(statUid, out var model) || model == null)
                {
                    if (!ResolveGetFromId()) return null;
                    model = _getFromIdMethod.Invoke(null, new object[] { statUid });
                    if (model == null)
                    {
                        Plugin.Logger.LogDebug($"[HiddenStat] GameStat '{statUid}' not found in AllData.");
                        return null;
                    }
                    _statModels[statUid] = model;
                }

                if (CardUtil.GetCachedField(gm.GetType(), "StatsDict")?.GetValue(gm) is not IDictionary statsDict) return null;
                if (!statsDict.Contains(model)) return null;
                return statsDict[model];
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[HiddenStat] ResolveInstance('{statUid}') failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return null;
            }
        }

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
    }
}
