using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Fugitive: known to the Watch (CMC 1.69.0, Documentation/Design/CMC_Content_Update_1_69_0_As_Built.md feature 6).
    /// A character who took the Fugitive trait (traits_perk_fugitive, a character-creation perk,
    /// so CardUtil.IsPerkEquipped sees it) is marked by the Watch the first time they stand in
    /// the village: their crime score is raised to <see cref="SeedCrime"/> (Suspected), so Captain
    /// Sterling gives his one formal warning and the Town board notes a stranger. It then decays
    /// at the usual 1 per day.
    ///   - Raised TO the seed, never BY it: a Fugitive who already carries crime from elsewhere
    ///     gains only the difference, so the mark can never push anyone into Wanted.
    ///   - Seeded from C# on arrival rather than through the perk's StartingStatModifiers, which
    ///     clamp onto the stat at character creation (CLAUDE.md "Perks"), and because the village
    ///     does not exist yet at character creation.
    ///   - The cmcStatFugitiveSeeded latch is written only AFTER the crime write succeeded
    ///     (CLAUDE.md "never latch handled before doing the work"); a failed write retries on
    ///     the next poll. On an existing save whose Fugitive has already visited, the mark lands
    ///     once on the next village visit (disclosed in the 1.69.0 CHANGELOG).
    /// </summary>
    internal static class FugitiveWatchPatch
    {
        private const string PerkUid = "traits_perk_fugitive";
        private const string VillageEnvUid = "cmcEnvVillage";
        private const string SeededStatUid = "cmcStatFugitiveSeeded";
        internal const float SeedCrime = 12f;

        private static bool _initialized;
        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            TickEvents.Interval(3f, CheckFugitiveArrival, "FugitiveWatchArrival");
            Plugin.Logger.LogDebug("[FugitiveWatchPatch] initialized.");
        }

        private static void CheckFugitiveArrival()
        {
            try
            {
                if (GameQuery.CurrentEnvironmentUniqueId != VillageEnvUid) return;
                if (!CardUtil.IsPerkEquipped(PerkUid)) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                float seeded = ReadLatch(gm);
                if (seeded < 0f)
                {
                    WarnOnce("latch-read", $"[FugitiveWatchPatch] {SeededStatUid} is not readable - the Fugitive mark cannot be applied.");
                    return;
                }
                if (seeded >= 0.5f) return; // already marked on this save

                float crime = VillageCrimePatch.CurrentCrime();
                float after = crime;
                if (crime < SeedCrime)
                {
                    after = VillageCrimePatch.AddCrime(SeedCrime - crime);
                    if (after < 0f)
                    {
                        WarnOnce("crime-write", "[FugitiveWatchPatch] AddCrime failed - will retry on the next poll.");
                        return; // latch stays 0: retry
                    }
                }

                if (!WriteLatch(gm, 1f))
                {
                    WarnOnce("latch-write", $"[FugitiveWatchPatch] {SeededStatUid} write failed - the mark may be re-applied on a later poll.");
                    return;
                }
                Plugin.Logger.LogInfo($"[FugitiveWatchPatch] The Watch knows this Fugitive: crime {crime:0} -> {after:0} on first village visit.");
            }
            catch (Exception ex)
            {
                WarnOnce("exception", $"[FugitiveWatchPatch] CheckFugitiveArrival failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Delegates to the framework's own by-run cache (fleet plan section 10, W0.10 row 1 READ side)
        // instead of hand-rolling UniqueIDScriptable.GetFromID + GameManager.StatsDict here.
        private static object LatchEntry(object gm) => StatAccess.Resolve(SeededStatUid);

        private static float ReadLatch(object gm)
        {
            var entry = LatchEntry(gm);
            if (entry == null) return -1f;
            return Reflect.GetMember(entry, "SimpleCurrentValue") is float f ? f : -1f;
        }

        // Delegates to the framework's StatAccess (fleet plan section 10, W0.10 row 1 WRITE side)
        // instead of hand-rolling GameManager.StatsDict here.
        private static bool WriteLatch(object gm, float value) => StatAccess.Set(SeededStatUid, value);

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
