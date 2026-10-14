using BepInEx.Configuration;
using HarmonyLib;

namespace CSFFModFramework.Patching.Performance;

// Phase 2 of the catch-up performance work (Documentation/Design/CatchUp_Performance_As_Built.md).
// Every card the catch-up loop processes already passed GameManager.ApplyRates' `!IndependentFromEnv`
// guard (IsIndependentDuringCatchup), and a card in an Equipment/Item/Blueprint slot is
// IndependentFromEnv by definition (.decomp/InGameCardBase.cs, the IndependentFromEnv getter), so
// CharacterScreen.HasCardEquipped is false for every card the catch-up loop's rate getters query it
// with. This ships the short-circuit: while GameManager.IsCatchingUp, a prefix answers false and skips
// the linear equipment-slot scan, which on the measured heavy save was ~27,000 scans per catch-up tick.
//
// The skip is behind [Performance] CatchUpEquipmentScanSkip (default true). The verify pass that gated
// it (tracker T1.68: 26 travels, zero calls that returned TRUE during IsCatchingUp) stays as a log-only
// postfix behind [Performance] LogCatchUpEquipmentScanHits: it observes the unsafe case whenever the
// skip is OFF, because with the skip on the real result is never computed during catch-up.
internal static class CatchUpEquipmentScanSkip
{
    private static bool _skipEnabled;

    public static void Configure(ConfigFile config, Harmony harmony)
    {
        var skipCfg = config.Bind(
            "Performance", "CatchUpEquipmentScanSkip", true,
            "While the game replays missed ticks on arrival (GameManager.IsCatchingUp), answer "
            + "'not equipped' for CharacterScreen.HasCardEquipped without scanning the equipment slots. "
            + "Safe because every card the replay processes is already known not to be equipped (an "
            + "equipped card is independent of the environment and is skipped before any rate is read). "
            + "Shortens long-absence travel. Set false to restore the vanilla scan.");
        var logCfg = config.Bind(
            "Performance", "LogCatchUpEquipmentScanHits", true,
            "Diagnostic for the catch-up equipment-scan skip (see "
            + "Documentation/Design/CatchUp_Performance_As_Built.md). Logs an Info line any time "
            + "CharacterScreen.HasCardEquipped returns true for a card while GameManager.IsCatchingUp "
            + "is true: the one case where the skip would give the wrong answer. It can only observe "
            + "while CatchUpEquipmentScanSkip is false, since the skip answers before the scan runs. "
            + "Cheap (one bool check per call; only logs on the rare unsafe case), safe to leave on.");
        _skipEnabled = skipCfg.Value;
        bool logHits = logCfg.Value;
        if (!_skipEnabled && !logHits) return;

        HarmonyMethod prefix = _skipEnabled
            ? new HarmonyMethod(AccessTools.Method(typeof(CatchUpEquipmentScanSkip), nameof(HasCardEquipped_Prefix)))
            : null;
        HarmonyMethod postfix = logHits
            ? new HarmonyMethod(AccessTools.Method(typeof(CatchUpEquipmentScanSkip), nameof(HasCardEquipped_Postfix)))
            : null;
        bool ok = SafePatcher.TryPatch(harmony, typeof(CharacterScreen), "HasCardEquipped", prefix: prefix, postfix: postfix);
        if (ok)
            Util.Log.Debug($"CatchUpEquipmentScanSkip: patched HasCardEquipped (skip={_skipEnabled}, log hits={logHits}).");
        else
            Util.Log.Warn("CatchUpEquipmentScanSkip: failed to patch CharacterScreen.HasCardEquipped; catch-up scan skip and diagnostic inactive.");
    }

    // Returning false skips the original; __result then stands as set. Outside catch-up the original runs.
    private static bool HasCardEquipped_Prefix(ref bool __result)
    {
        var gm = MBSingleton<GameManager>.Instance;
        if (gm == null || !gm.IsCatchingUp) return true;
        __result = false;
        return false;
    }

    private static void HasCardEquipped_Postfix(InGameCardBase _Card, bool __result)
    {
        if (!__result) return;
        var gm = MBSingleton<GameManager>.Instance;
        if (gm == null || !gm.IsCatchingUp) return;

        string uid = "?";
        try { uid = _Card != null && _Card.CardModel != null ? _Card.CardModel.UniqueID : "?"; }
        catch (Exception ex) { Util.Log.Debug($"CatchUpEquipmentScanSkip: could not read CardModel.UniqueID: {ex.Message}"); }

        Util.Log.Info($"CatchUpEquipmentScanSkip: HasCardEquipped returned TRUE during IsCatchingUp for '{uid}'; "
                      + "the catch-up equipment-scan skip is wrong for this call. Set [Performance] CatchUpEquipmentScanSkip=false and report it.");
    }
}
