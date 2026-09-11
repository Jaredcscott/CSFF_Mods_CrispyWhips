using BepInEx.Configuration;
using HarmonyLib;

namespace CSFFModFramework.Patching.Performance;

// Phase 2 of the catch-up performance plan (Documentation/Plans/CSFFModFramework/
// CatchUp_Performance_Plan.md). Every card the catch-up loop processes already
// passed GameManager.ApplyRates' `!IndependentFromEnv` guard, and a card in an
// Equipment/Item slot is IndependentFromEnv by definition
// (.decomp/InGameCardBase.cs:1061-1063) — so CharacterScreen.HasCardEquipped
// should provably return false for every card the catch-up loop's 8 rate
// getters query it with (.decomp/InGameCardBase.cs:1722-1724). Short-circuiting
// it during IsCatchingUp would remove ~27,000 linear equipment-slot scans per
// catch-up tick on the measured heavy save.
//
// NOT short-circuited yet. Per CLAUDE.md Debugging Discipline ("diagnostics
// before fixes") and the plan's own explicit gate, this ships first as a
// LOG-ONLY verification pass: a postfix that flags any call during IsCatchingUp
// whose result was actually TRUE (i.e. a case the short-circuit would get
// wrong). One play session with zero hits confirms the assumption; only then
// should ChangeEnvironment_Prefix-style short-circuit be added in a follow-up
// build. Do not flip this to a real short-circuit without that verification —
// the safety claim here is asserted from the travel-flow guard chain, not
// exhaustively proven for every code path that might query equipment state
// while IsCatchingUp is true.
internal static class CatchUpEquipmentScanSkip
{
    private static bool _enabled;

    public static void Configure(ConfigFile config, Harmony harmony)
    {
        var enabledCfg = config.Bind(
            "Performance", "LogCatchUpEquipmentScanHits", true,
            "Diagnostic for the Phase 2 catch-up performance verify pass (see "
            + "Documentation/Plans/CSFFModFramework/CatchUp_Performance_Plan.md). Logs "
            + "an Info line any time CharacterScreen.HasCardEquipped returns true for a "
            + "card while GameManager.IsCatchingUp is true — that would be the one case "
            + "where a proposed catch-up short-circuit (not yet shipped) would give the "
            + "wrong answer. A play session with zero hits is the ship gate for the "
            + "actual short-circuit. Cheap (one bool check per call; only logs on the "
            + "rare unsafe case), safe to leave on.");
        _enabled = enabledCfg.Value;
        if (!_enabled) return;

        var postfix = new HarmonyMethod(AccessTools.Method(typeof(CatchUpEquipmentScanSkip), nameof(HasCardEquipped_Postfix)));
        bool ok = SafePatcher.TryPatch(harmony, typeof(CharacterScreen), "HasCardEquipped", postfix: postfix);
        if (ok)
            Util.Log.Debug("CatchUpEquipmentScanSkip: diagnostic enabled (log-only; no short-circuit shipped yet).");
        else
            Util.Log.Warn("CatchUpEquipmentScanSkip: failed to patch CharacterScreen.HasCardEquipped; diagnostic inactive.");
    }

    private static void HasCardEquipped_Postfix(InGameCardBase _Card, bool __result)
    {
        if (!__result) return;
        var gm = MBSingleton<GameManager>.Instance;
        if (gm == null || !gm.IsCatchingUp) return;

        string uid = "?";
        try { uid = _Card != null && _Card.CardModel != null ? _Card.CardModel.UniqueID : "?"; }
        catch (Exception ex) { Util.Log.Debug($"CatchUpEquipmentScanSkip: could not read CardModel.UniqueID: {ex.Message}"); }

        Util.Log.Info($"CatchUpEquipmentScanSkip: HasCardEquipped returned TRUE during IsCatchingUp for '{uid}' — "
                      + "a proposed short-circuit here would be UNSAFE for this call; do not ship it until this stops appearing.");
    }
}
