using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using CSFFModFramework.Util;
using HarmonyLib;

namespace Advanced_Copper_Tools.Patcher
{
    // Hides a pairing-only ACT recipe when the partner card it needs does not exist (Cross-Mod Doctrine HIDE;
    // Fleet plan feature 6). Each row is (ACT blueprint UID, required partner card UID, what is missing):
    //  - Copper Shears needs Sirus23's shears card, since only Sirus23's sheep have a Shear action (it
    //    triggers on tag_Shears). act_copper_shears is an END PRODUCT that no blueprint consumes.
    // (Render Hemp Seed Oil, the second row until 1.18.13, was retired: every tag_Oil card already fuels the
    // lamps and coats iron, so turning hemp seed oil into ACT Oil did nothing.)
    // The row satisfies CLAUDE.md "A C# blueprint lock may only ever hide an END PRODUCT". Shape: CMC
    // AcademyCourseService.HideIfDependencyMissing, run from a GameManager.InitializeStatsAndActions postfix,
    // the method that registers every blueprint into BlueprintModelStates and UnlockableCards. On a loaded
    // save the research replay (LoadCards -> SetBpAvailable) runs after it and re-enables a researched
    // blueprint, so a save that already learned a recipe keeps it. Research:
    // Documentation/Research/AdvancedCopperTools/wave0-tools.md section 4 and
    // Documentation/Research/Fleet/cross_mod_metal_mods.md (AdvancedCopperTools -> HerbsAndFungi).
    public static class PartnerBlueprintGatePatch
    {
        private struct GateRow
        {
            public string BlueprintUid;
            public string RequiredUid;
            public string Label;     // player-facing recipe name, for the Info line
            public string Partner;   // the mod that ships RequiredUid, for the Info line
        }

        private static readonly GateRow[] Rows =
        {
            new GateRow { BlueprintUid = "act_bp_copper_shears", RequiredUid = "sh_shears", Label = "Copper Shears", Partner = "Sirus23" },
        };

        private const int StateAvailable = 0;   // BlueprintModelState: 0 Available, 1 Purchasable, 2 Locked, 3 Hidden
        private const int StateHidden = 3;

        private static ManualLogSource Logger => Plugin.Logger;
        private static readonly HashSet<string> _warned = new HashSet<string>();
        private static FieldInfo _disabledField;

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Logger?.LogWarning("[ACT] partner blueprint gate: " + message + " (" + cause + ")");
        }

        public static void ApplyPatch(Harmony harmony)
        {
            var gmType = CardUtil.FindGameType("GameManager");
            var method = gmType == null ? null : AccessTools.Method(gmType, "InitializeStatsAndActions");
            if (method == null)
            {
                Logger?.LogError("[ACT] GameManager.InitializeStatsAndActions not found; partner blueprint gate not applied.");
                return;
            }
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(PartnerBlueprintGatePatch), nameof(InitializeStatsAndActions_Postfix)));
        }

        // Runs once per GameManager.Awake. The patch is applied once per process, so nothing accumulates
        // across save reloads; the body is idempotent.
        static void InitializeStatsAndActions_Postfix(object __instance)
        {
            try
            {
                IDictionary states = null;
                IList unlockables = null;
                var hidden = new List<string>();
                foreach (var row in Rows)
                {
                    // Partner present: leave the recipe to its normal CardsOnBoard gate. A miss is the expected
                    // "partner not installed" case, not a failure, so it is reported in the one Info line below.
                    if (CardUtil.GetCardDataById(row.RequiredUid) != null) continue;

                    if (states == null)
                    {
                        states = CardUtil.GetMemberValue(__instance, "BlueprintModelStates") as IDictionary;
                        if (states == null) { WarnOnce("BlueprintModelStates-null", "GameManager.BlueprintModelStates unreadable; recipes left as authored"); return; }
                        unlockables = CardUtil.GetMemberValue(__instance, "UnlockableCards") as IList;
                        if (unlockables == null) { WarnOnce("UnlockableCards-null", "GameManager.UnlockableCards unreadable; recipes left as authored"); return; }
                    }
                    if (Hide(row, states, unlockables)) hidden.Add(row.Label + " (no " + row.Partner + ")");
                }
                if (hidden.Count > 0 && _warned.Add("hidden-info"))
                    Logger?.LogInfo("[ACT] partner not installed, recipes hidden: " + string.Join(", ", hidden.ToArray()) + ".");
            }
            catch (Exception ex)
            {
                // Logged directly (not through WarnOnce) so the breadcrumb is visible in this block.
                Logger?.LogWarning("[ACT] partner blueprint gate failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        // Returns true when the row's blueprint is now hidden; every other exit leaves it as authored and,
        // where that is a failure rather than a choice, says why once per cause.
        private static bool Hide(GateRow row, IDictionary states, IList unlockables)
        {
            var bp = CardUtil.GetCardDataById(row.BlueprintUid);
            if (bp == null) { WarnOnce("blueprint-null:" + row.BlueprintUid, row.BlueprintUid + " did not resolve; recipe left as authored"); return false; }
            if (!states.Contains(bp)) { WarnOnce("state-missing:" + row.BlueprintUid, row.BlueprintUid + " has no BlueprintModelStates entry; recipe left as authored"); return false; }
            if (Convert.ToInt32(states[bp]) == StateAvailable) return false;      // never take away an available blueprint

            states[bp] = Enum.ToObject(states[bp].GetType(), StateHidden);
            foreach (var entry in unlockables)
            {
                if (entry == null) continue;
                if (!ReferenceEquals(CardUtil.GetMemberValue(entry, "UnlockedCard"), bp)) continue;
                _disabledField ??= entry.GetType().GetField("Disabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_disabledField == null) { WarnOnce("Disabled-field-null", "UnlockableCards entry has no Disabled field; state hidden, unlock entry left live"); return true; }
                _disabledField.SetValue(entry, true);
                return true;
            }
            WarnOnce("unlock-entry-missing:" + row.BlueprintUid, "no UnlockableCards entry for " + row.BlueprintUid + "; state hidden only");
            return true;
        }
    }
}
