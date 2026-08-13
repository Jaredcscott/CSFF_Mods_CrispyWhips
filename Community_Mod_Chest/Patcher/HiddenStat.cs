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
