using System.Collections;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Util;
using HarmonyLib;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Adds cmcStatVillageReputation to the vanilla "Mental" StatListTab so Village Reputation
    /// shows on the detailed stats screen.
    ///
    /// Previously attempted via GameSourceModify/Mental.json, which can never work: StatListTab
    /// assets are gameplay-scene objects that are not yet loaded during LoadMainGameData (menu
    /// time), so the GSM phase logged "no object found for 'Mental'" on every start. The append
    /// has to happen at game boot instead — InitializeStatsAndActions postfix, when the tab
    /// asset is guaranteed loaded. Idempotent, so re-boots and asset reloads are both safe.
    /// </summary>
    internal static class StatTabInjectionPatch
    {
        private const string TabName = "Mental";
        private const string StatUid = "cmcStatVillageReputation";

        private static bool _initialized;
        private static bool _warnedTabMissing;
        private static bool _warnedStatMissing;

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            var gmType = CardUtil.FindGameType("GameManager");
            var initStatsMethod = gmType != null ? AccessTools.Method(gmType, "InitializeStatsAndActions") : null;
            if (initStatsMethod == null)
            {
                Plugin.Logger.LogWarning("[StatTabInjectionPatch] GameManager.InitializeStatsAndActions not found — Village Reputation will not appear in the Mental stats tab.");
                return;
            }

            harmony.Patch(initStatsMethod, postfix: new HarmonyMethod(typeof(StatTabInjectionPatch), nameof(InitStats_Postfix)));
            Plugin.Logger.LogDebug("[StatTabInjectionPatch] initialized.");
        }

        private static void InitStats_Postfix()
        {
            try
            {
                var tabType = CardUtil.FindGameType("StatListTab");
                if (tabType == null) return;

                // Re-found every boot on purpose: the tab is a scene-scoped asset that can be
                // unloaded and reloaded between runs, and a cached destroyed UnityEngine.Object
                // held as 'object' does not compare as null. 9 assets — the scan is trivial.
                Object mentalTab = null;
                foreach (var obj in Resources.FindObjectsOfTypeAll(tabType))
                {
                    if (obj != null && obj.name == TabName) { mentalTab = obj; break; }
                }
                if (mentalTab == null)
                {
                    if (!_warnedTabMissing)
                    {
                        _warnedTabMissing = true;
                        Plugin.Logger.LogWarning($"[StatTabInjectionPatch] StatListTab '{TabName}' not loaded at game boot — Village Reputation stays untabbed.");
                    }
                    return;
                }

                var stat = ResolveStat();
                if (stat == null)
                {
                    if (!_warnedStatMissing)
                    {
                        _warnedStatMissing = true;
                        Plugin.Logger.LogWarning($"[StatTabInjectionPatch] GameStat '{StatUid}' not found — Village Reputation stays untabbed.");
                    }
                    return;
                }

                var containedField = CardUtil.GetCachedField(tabType, "ContainedStats");
                if (containedField?.GetValue(mentalTab) is not IList list)
                {
                    Plugin.Logger.LogDebug($"[StatTabInjectionPatch] '{TabName}' tab has no ContainedStats list — skipped.");
                    return;
                }

                if (!list.Contains(stat))
                {
                    list.Add(stat);
                    Plugin.Logger.LogDebug("[StatTabInjectionPatch] Village Reputation added to the Mental stats tab.");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogWarning($"[StatTabInjectionPatch] InitStats_Postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static object ResolveStat()
        {
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");
            var getFromId = uidType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            return getFromId?.Invoke(null, new object[] { StatUid });
        }
    }
}
