using System;
using BepInEx.Logging;
using CSFFModFramework.Util;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// S23-B6 (vanilla balance pass, 1.22.6): a one-time Comfort reset per save.
///
/// Before 1.22.6 the Lanolin Salve wrote Comfort +15 straight into the stat's BASE value, and vanilla
/// Comfort has no decay, so every salve ever applied is still in the save, pinning the player at Great
/// Comfort. The salve now raises a one-day effect stat instead (sh_stat_lanolin_soothe), which does not
/// undo what older salves already wrote. This clears Comfort's base back to the stat's own default once
/// per save, latched by the hidden saved stat sh_stat_comfort_fixup_done.
///
/// Community_Mod_Chest ships its own reset for its own old Comfort sources in the same release. Both only
/// set the base to the default, so whichever runs second finds nothing left to clear: running both on
/// one load is harmless. Comfort from worn clothing is a modifier, not base, and is untouched.
///
/// Registered once per process (pattern b of the per-run registration rule): OnGMInitialized fires on every
/// run start, so the latch, not the subscription, is what keeps it to once per save.
/// </summary>
internal static class ComfortFixupPatch
{
    private const string ComfortStatUid = "be10ed6d196f5d043b7a819f9f1fd1ff";
    private const string LatchStatUid = "sh_stat_comfort_fixup_done";

    private static bool _subscribed;
    private static ManualLogSource Logger => Plugin.Logger;

    internal static void Register()
    {
        if (_subscribed) return;
        _subscribed = true;
        GameManager.OnGMInitialized += OnRunStart;
    }

    private static void OnRunStart()
    {
        try
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.StatsDict == null)
            {
                Logger?.LogWarning("[ComfortFixup] GameManager or its StatsDict is not ready; the one-time Comfort reset is skipped this load and retried next load.");
                return;
            }

            var latchStat = UniqueIDScriptable.GetFromID<GameStat>(LatchStatUid);
            var comfortStat = UniqueIDScriptable.GetFromID<GameStat>(ComfortStatUid);
            if (latchStat == null || comfortStat == null)
            {
                Logger?.LogWarning($"[ComfortFixup] could not resolve {(latchStat == null ? LatchStatUid : "Comfort (" + ComfortStatUid + ")")}; the one-time Comfort reset is skipped.");
                return;
            }
            if (!gm.StatsDict.TryGetValue(latchStat, out var latch) || latch == null
                || !gm.StatsDict.TryGetValue(comfortStat, out var comfort) || comfort == null)
            {
                Logger?.LogWarning("[ComfortFixup] Comfort or the reset latch is not in this run's StatsDict; the one-time Comfort reset is skipped.");
                return;
            }

            if (latch.CurrentBaseValue >= 0.5f) return;  // already done in this save

            float before = comfort.CurrentBaseValue;
            float target = comfortStat.BaseValue;
            comfort.CurrentBaseValue = target;
            // Latch only after the write went through.
            latch.CurrentBaseValue = 1f;
            Logger?.LogInfo($"[ComfortFixup] one-time Comfort reset for this save: base {before:0.#} -> {target:0.#} (clears the permanent Comfort older Lanolin Salves wrote).");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[ComfortFixup] the one-time Comfort reset failed and will be retried next load: {ex}");
        }
    }
}
