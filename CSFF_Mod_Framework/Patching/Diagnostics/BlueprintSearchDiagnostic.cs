using System;
using System.Collections.Generic;
using HarmonyLib;

namespace CSFFModFramework.Patching.Diagnostics;

/// <summary>
/// Opt-in diagnostic for investigating "mod blueprints show under their own crafting-journal
/// tab but never appear when using the Search box" (reported 2026-08-15, fleet-wide across
/// mods). BlueprintModelsScreen.OnSearchStringValueChanged only lights up a slot when
/// FindTabFor(card).x != -1; FindTabFor returns x=-1,y=-1 if GameManager.BlueprintModelStates
/// has no entry for that exact CardData instance, or x=-1,y=-2 if the card isn't in ANY
/// BlueprintTabs[].IncludedCards (own tab or subgroup) — the same array normal tab browsing
/// reads, so a "found=own tab, missing=Search" split narrows straight to one of those two
/// paths. This patch logs every miss (once per UID) with which of the two it is, so the fix
/// can target the actual cause instead of guessing.
///
/// Off by default. Enable via BepInEx config:
///   [Diagnostics] LogBlueprintSearchMisses = true
/// </summary>
internal static class BlueprintSearchDiagnostic
{
    private static readonly HashSet<string> _loggedMisses = new(StringComparer.OrdinalIgnoreCase);

    public static void Configure(BepInEx.Configuration.ConfigFile config, Harmony harmony)
    {
        var enabled = config.Bind("Diagnostics", "LogBlueprintSearchMisses", false,
            "When true, logs every mod blueprint that BlueprintModelsScreen.FindTabFor fails "
            + "to resolve (which is why it's invisible in the crafting journal's Search box), "
            + "along with the specific reason (no BlueprintModelStates entry vs. not present "
            + "in any tab's IncludedCards). Open the crafting journal and use Search to produce "
            + "log entries. Off by default.");
        if (!enabled.Value) return;

        var postfix = new HarmonyMethod(AccessTools.Method(typeof(BlueprintSearchDiagnostic), nameof(FindTabFor_Postfix)));
        bool patched = SafePatcher.TryPatch(harmony, "BlueprintModelsScreen", "FindTabFor", postfix: postfix);
        Util.Log.Info($"BlueprintSearchDiagnostic: enabled (patched={patched}). Open the crafting journal and search to produce entries.");
    }

    static void FindTabFor_Postfix(CardData _Card, ref Vector2Int __result)
    {
        try
        {
            if (!_Card || string.IsNullOrEmpty(_Card.UniqueID)) return;
            if (__result.x != -1) return; // resolved fine — not our concern
            if (!Loading.JsonDataLoader.AllModUniqueIds.Contains(_Card.UniqueID)) return; // vanilla card
            if (!_loggedMisses.Add(_Card.UniqueID)) return; // log each mod blueprint UID once

            var gm = MBSingleton<GameManager>.Instance;
            bool hasState = gm && gm.BlueprintModelStates.ContainsKey(_Card);
            string stateText = hasState ? gm.BlueprintModelStates[_Card].ToString() : "<no entry>";
            string reason = __result.y == -1
                ? "GameManager.BlueprintModelStates has no entry for this CardData instance"
                : "not found in ANY BlueprintTabs[].IncludedCards (own tab or subgroup)";
            Util.Log.Info($"[BlueprintSearchDiag] '{_Card.UniqueID}' MISSING from Search — reason: {reason} "
                          + $"(result=({__result.x},{__result.y}), BlueprintModelStates.ContainsKey={hasState}, state={stateText})");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"[BlueprintSearchDiag] postfix failed: {ex}");
        }
    }
}
