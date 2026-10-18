namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Drops expired animal tracks from every environment immediately before the game writes a save,
/// so a long run's save stops growing by thousands of dead tracks and the autosave stops freezing
/// the game for seconds.
///
/// <para><strong>The failure</strong> (EA 0.69c decompile). A wandering animal adds an
/// <c>NPCTrackingInfo</c> to the save data of the environment it is in
/// (<c>.decomp/InGameNPC.cs</c> lines 2156 and 2260, <c>.decomp/MoveDutyAction.cs</c> line 137),
/// whether or not the player is there. The only full sweep of that list is
/// <c>EnvironmentSaveDataByReference.CheckForTracks(false)</c>, which <c>GameManager</c> runs for the
/// player's CURRENT environment on arrival (<c>.decomp/GameManager.cs</c> line 11566); the other
/// calls pass <c>_OnlyLastDroppedTracks: true</c> and look at the newest track alone. So every place
/// the player does not return to keeps every track ever left in it, and
/// <c>GameLoad.SaveGameByReference</c> serializes all of them on every autosave. Measured on a
/// day-408 save (2026-10-17): 47,648 tracks, 20.5 MB of a 28.9 MB AutoSave.json, 99.8% of them past
/// their lifetime, median age about 108 in-game days. Player report 2026-10-16 (Chiwei): the game
/// freezes for a few seconds every two in-game hours, on the "Success!" lines the game logs as it
/// writes the save files.</para>
///
/// <para><strong>Why this changes nothing in play.</strong> <c>CheckForTracks</c> removes a track
/// whose age (<c>CurrentTickInfo.z - TrackTick</c>) is at least its <c>TrackLifetime</c>, when that
/// lifetime is above zero, before any discovery roll and without raising an event. This prefix
/// applies that same test, to every environment, a little earlier. A track with no lifetime (0 or
/// less) never expires in vanilla and is kept. Live tracks are kept, in their order.</para>
///
/// <para>The prefix returns void, so it can never skip the save, and it never throws: any failure
/// is logged and the save runs on the tracks as they were.</para>
/// </summary>
internal static class ExpiredTrackPruner
{
    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var original = AccessTools.Method(typeof(GameLoad), nameof(GameLoad.SaveGameByReference));
            if (original == null)
            {
                Util.Log.Warn("ExpiredTrackPruner: GameLoad.SaveGameByReference not found; expired animal tracks will not be pruned before saving.");
                return;
            }

            var prefix = new HarmonyMethod(AccessTools.Method(typeof(ExpiredTrackPruner), nameof(Prefix)));
            harmony.Patch(original, prefix: prefix);
            Util.Log.Debug("ExpiredTrackPruner: patched GameLoad.SaveGameByReference.");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"ExpiredTrackPruner: patch setup failed: {Util.Log.ExceptionText(ex)}");
        }
    }

    // void, never bool: this prefix cannot skip the original save.
    private static void Prefix()
    {
        try
        {
            var gm = MBSingleton<GameManager>.Instance;
            if (gm == null || gm.EnvironmentsData == null) return;

            int now = gm.CurrentTickInfo.z;
            int pruned = 0, kept = 0, envs = 0;
            foreach (var env in gm.EnvironmentsData.Values)
            {
                var tracks = env?.NPCTracks;
                if (tracks == null || tracks.Count == 0) continue;
                int removed = tracks.RemoveAll(t => IsExpired(t, now));
                if (removed > 0) envs++;
                pruned += removed;
                kept += tracks.Count;
            }

            // Info only when it matters: the first save of an old run, not every autosave.
            if (pruned >= 1000)
                Util.Log.Info($"ExpiredTrackPruner: removed {pruned} expired animal tracks from {envs} environment(s) before saving "
                    + $"({kept} live tracks kept). The game only clears tracks in the place you are standing, so places you "
                    + "do not revisit kept every track ever left in them, and each one was written on every autosave.");
            else if (pruned > 0)
                Util.Log.Debug($"ExpiredTrackPruner: removed {pruned} expired animal track(s) from {envs} environment(s) ({kept} kept).");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"ExpiredTrackPruner: prune failed; saving with the tracks unchanged: {Util.Log.ExceptionText(ex)}");
        }
    }

    // The same test CheckForTracks applies (.decomp/EnvironmentSaveDataByReference.cs line 491).
    internal static bool IsExpired(NPCTrackingInfo track, int now)
        => track != null && track.TrackLifetime > 0 && now - track.TrackTick >= track.TrackLifetime;
}
