using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace WaterDrivenInfrastructure.Patcher
{
    /// <summary>
    /// Bronze gears (plan feature 23). The two bronze cast blueprints are NOT in BlueprintTabs.json: that file has
    /// no condition key, so a listed UID is tabbed for every player. They enter the journal only when Advanced
    /// Copper Tools is loaded (ActCompat.IsInstalled), through this runtime path (plan FW5, INFERRED until the
    /// harness run): a prefix on NewBlueprintContent.Start and a postfix on BlueprintModelsScreen.Show, the two
    /// methods the framework's BlueprintInjector.InjectFromUI uses, appending the AllData CardData instance to
    /// the Metal Crafts CardTabGroup's IncludedCards through Api.Collections.Append (which skips an element
    /// already present by reference, so the append cannot happen twice). Never a clone: a second instance of a
    /// blueprint resets its research (root CLAUDE.md "Blueprint Research Persistence"). Research state itself
    /// does not depend on the tab: the game builds it from DataBase.AllData by UID.
    /// </summary>
    public static class BronzeGearTabPatch
    {
        // Exact TabName.LocalizationKey of the Metal Crafts subtab (CSFF_Reference.md; the key is not the file name).
        // BlueprintTabs.json files the copper gears under the same key.
        public const string MetalCraftsTabKey = "Tab_5_MetalAndClay_Subtab_3_MetalCrafts_TabName";

        private static readonly string[] BlueprintUids =
        {
            "water_sawmill_bp_cast_bronze_gear_large",
            "water_sawmill_bp_cast_bronze_gear_small",
        };

        private const int MaxShowAttempts = 8;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static ManualLogSource Logger => Plugin.Logger;
        private static bool _done;
        private static int _showAttempts;
        private static List<object> _resourceTabs;
        private static readonly HashSet<string> _warnedCauses = new HashSet<string>(StringComparer.Ordinal);

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var contentType = AccessTools.TypeByName("NewBlueprintContent");
                var startMethod = contentType == null ? null : AccessTools.Method(contentType, "Start");
                if (startMethod != null)
                    harmony.Patch(startMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(BronzeGearTabPatch), nameof(Start_Prefix))));
                else
                    Warn("start", "NewBlueprintContent.Start not found; the bronze gear blueprints will not be tabbed from the first journal open.");

                var screenType = AccessTools.TypeByName("BlueprintModelsScreen");
                var showMethod = screenType == null ? null : AccessTools.Method(screenType, "Show");
                if (showMethod != null)
                    harmony.Patch(showMethod, postfix: new HarmonyMethod(AccessTools.Method(typeof(BronzeGearTabPatch), nameof(Show_Postfix))));
                else
                    Warn("show", "BlueprintModelsScreen.Show not found; the bronze gear blueprints will not be tabbed.");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[WDI] BronzeGearTabPatch: failed to apply patches: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        static void Start_Prefix()
        {
            TryAppend(null);
        }

        static void Show_Postfix(object __instance)
        {
            TryAppend(__instance);
        }

        private static void TryAppend(object uiRoot)
        {
            try
            {
                if (_done) return;
                if (!ActCompat.IsInstalled) return;
                if (uiRoot != null)
                {
                    if (_showAttempts >= MaxShowAttempts) return;
                    _showAttempts++;
                }

                var cards = new List<object>();
                foreach (var uid in BlueprintUids)
                {
                    var card = CardUtil.GetCardDataById(uid);
                    if (card == null) { Warn("card:" + uid, $"blueprint '{uid}' is not in AllData; it cannot be tabbed."); continue; }
                    cards.Add(card);
                }
                if (cards.Count == 0) return;

                var tabs = CollectMatchingTabs(uiRoot);
                if (tabs.Count == 0)
                {
                    if (uiRoot != null) Warn("tab", $"no CardTabGroup with TabName.LocalizationKey '{MetalCraftsTabKey}' was found on journal open {_showAttempts}.");
                    return;
                }

                int added = 0;
                foreach (var tab in tabs)
                    foreach (var card in cards)
                        if (Collections.Append(tab, "IncludedCards", card)) added++;

                if (added > 0)
                    Logger?.LogInfo($"[WDI] Bronze gear blueprints tabbed under Metal Crafts ({added} append(s) across {tabs.Count} tab object(s)); Advanced Copper Tools is loaded.");

                // Only a live-UI pass sees the tab objects the journal renders; latch there, as the framework does.
                if (uiRoot != null) _done = true;
            }
            catch (Exception ex)
            {
                Warn("exception", $"tab append threw: {(ex.InnerException ?? ex)}");
            }
        }

        private static List<object> CollectMatchingTabs(object uiRoot)
        {
            var found = new List<object>();
            var seen = new HashSet<object>();

            if (_resourceTabs == null)
            {
                _resourceTabs = new List<object>();
                var tabType = AccessTools.TypeByName("CardTabGroup");
                if (tabType != null && typeof(UnityEngine.Object).IsAssignableFrom(tabType))
                {
                    foreach (var tab in UnityEngine.Resources.FindObjectsOfTypeAll(tabType))
                        if (tab != null) _resourceTabs.Add(tab);
                }
                else Warn("tabtype", "CardTabGroup type not found.");
            }
            foreach (var tab in _resourceTabs) Visit(tab, found, seen, 0);

            if (uiRoot != null && ReadMember(uiRoot, "BlueprintTabs") is IEnumerable ui)
                foreach (var tab in ui) Visit(tab, found, seen, 0);
            return found;
        }

        private static void Visit(object tab, List<object> found, HashSet<object> seen, int depth)
        {
            if (tab == null || depth > 8 || !seen.Add(tab)) return;
            if (ReadMember(ReadMember(tab, "TabName"), "LocalizationKey") is string key
                && string.Equals(key, MetalCraftsTabKey, StringComparison.Ordinal))
                found.Add(tab);
            if (ReadMember(tab, "SubGroups") is IEnumerable subs)
                foreach (var child in subs) Visit(child, found, seen, depth + 1);
        }

        private static object ReadMember(object owner, string name)
        {
            if (owner == null) return null;
            var type = owner.GetType();
            var field = type.GetField(name, Flags);
            if (field != null) return field.GetValue(owner);
            var prop = type.GetProperty(name, Flags);
            return prop?.GetValue(owner, null);
        }

        private static void Warn(string cause, string message)
        {
            if (_warnedCauses.Add(cause)) Logger?.LogWarning("[WDI] BronzeGearTabPatch: " + message);
        }
    }
}
