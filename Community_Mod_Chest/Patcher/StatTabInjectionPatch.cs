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
    /// Appends mod GameStats onto vanilla detailed-stat StatListTabs so they show on the
    /// detailed stats screen: cmcStatVillageReputation onto "Mental", cmcSkillMeleeFighting
    /// onto "Skills".
    ///
    /// Previously attempted via GameSourceModify/Mental.json, which can never work: StatListTab
    /// assets are gameplay-scene objects that are not yet loaded during LoadMainGameData (menu
    /// time), so the GSM phase logged "no object found for 'Mental'" on every start. The append
    /// has to happen at game boot instead — InitializeStatsAndActions postfix, when the tab
    /// assets are guaranteed loaded. Idempotent, so re-boots and asset reloads are both safe.
    /// </summary>
    internal static class StatTabInjectionPatch
    {
        private static readonly (string TabName, string StatUid)[] Injections =
        {
            ("Mental", "cmcStatVillageReputation"),
            ("Skills", "cmcSkillMeleeFighting"),
        };

        private static bool _initialized;
        private static readonly HashSet<string> _warnedTabMissing = new HashSet<string>();
        private static readonly HashSet<string> _warnedStatMissing = new HashSet<string>();

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            var gmType = CardUtil.FindGameType("GameManager");
            var initStatsMethod = gmType != null ? AccessTools.Method(gmType, "InitializeStatsAndActions") : null;
            if (initStatsMethod == null)
            {
                Plugin.Logger.LogWarning("[StatTabInjectionPatch] GameManager.InitializeStatsAndActions not found — no stat tab injections will run.");
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
                if (tabType == null)
                {
                    // The one exit both injections depend on (modwide A13 and G5): once, by name.
                    if (_warnedTabMissing.Add("type:StatListTab"))
                        Plugin.Logger.LogWarning("[StatTabInjectionPatch] StatListTab type not resolved at game boot (renamed by a game update?): no stat-tab injections run, so Village Reputation and Melee Fighting stay off the detailed stats screen. Logged once.");
                    return;
                }

                // Re-found every boot on purpose: StatListTab assets are scene-scoped and can be
                // unloaded and reloaded between runs, and a cached destroyed UnityEngine.Object
                // held as 'object' does not compare as null. One scan feeds every pair below —
                // 9 assets, so the scan is trivial either way.
                var tabsByName = new Dictionary<string, Object>();
                foreach (var obj in Resources.FindObjectsOfTypeAll(tabType))
                {
                    if (obj != null && !tabsByName.ContainsKey(obj.name))
                    {
                        tabsByName[obj.name] = obj;
                    }
                }

                foreach (var injection in Injections)
                {
                    InjectStat(tabType, tabsByName, injection.TabName, injection.StatUid);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Logger.LogWarning($"[StatTabInjectionPatch] InitStats_Postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void InjectStat(System.Type tabType, Dictionary<string, Object> tabsByName, string tabName, string statUid)
        {
            if (!tabsByName.TryGetValue(tabName, out var tab) || tab == null)
            {
                if (_warnedTabMissing.Add(tabName))
                {
                    Plugin.Logger.LogWarning($"[StatTabInjectionPatch] StatListTab '{tabName}' not loaded at game boot — {statUid} stays untabbed.");
                }
                return;
            }

            var stat = ResolveStat(statUid);
            if (stat == null)
            {
                if (_warnedStatMissing.Add(statUid))
                {
                    Plugin.Logger.LogWarning($"[StatTabInjectionPatch] GameStat '{statUid}' not found — stays untabbed.");
                }
                return;
            }

            var containedField = CardUtil.GetCachedField(tabType, "ContainedStats");
            if (containedField?.GetValue(tab) is not IList list)
            {
                // Was a Debug line on every boot; a StatListTab without a ContainedStats list is a game-update
                // rename, so it is a warning, once per tab, naming the member.
                if (_warnedTabMissing.Add("member:ContainedStats:" + tabName))
                    Plugin.Logger.LogWarning($"[StatTabInjectionPatch] StatListTab.ContainedStats {(containedField == null ? "did not resolve" : "is not a list")} on the '{tabName}' tab (renamed by a game update?): {statUid} stays untabbed. Logged once.");
                return;
            }

            if (!list.Contains(stat))
            {
                list.Add(stat);
                Plugin.Logger.LogDebug($"[StatTabInjectionPatch] {statUid} added to the {tabName} stats tab.");
            }
        }

        private static object ResolveStat(string statUid)
        {
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");
            var getFromId = uidType?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            return getFromId?.Invoke(null, new object[] { statUid });
        }
    }
}
