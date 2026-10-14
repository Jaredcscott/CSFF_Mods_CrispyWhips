using System;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Sit In at the Academy (CMC round 2, F1). Every 5 s this publishes cmcStatAcademyGuest: which
    /// resident is in cmcAcademyInterior with the player (1 Miller, 2 Weaver, 3 Apothecary,
    /// 4 Professor; the first of that list when several are there), or 0 whenever the player is
    /// elsewhere or nobody is. The four "Sit In" buttons on the Lecture Hall read it in JSON
    /// (RequiredStatValues), so the C# here is only the presence read.
    ///   - Presence is each resident's OWN CurrentEnvironment, through InnEveningPatch.PresentResidentsIn
    ///     (the same read that publishes cmcStatInnCompany), never an AllCards scan, which is
    ///     current-environment-scoped (Documentation/Research/Community_Mod_Chest/engine_reads.md W0.8).
    ///   - The Apothecary and the Professor only walk inside once the player has stood in the Academy
    ///     once this session (their schedule patches capture the EnvID then), so for them the button
    ///     appears only after that; the Miller (day % 7 == 1) and the Weaver (day % 7 == 4) need no capture.
    /// Registration runs once per process from Plugin.Awake under an _initialized guard (CLAUDE.md
    /// "Per-Run Event Registration", pattern b), so reloads never duplicate the poll.
    /// </summary>
    internal static class AcademyGuestPatch
    {
        internal const string AcademyInteriorUid = "cmcAcademyInterior";
        internal const string GuestStatUid = "cmcStatAcademyGuest";

        /// <summary>Resident agent UIDs in publish order: the index plus one is the stat value.</summary>
        private static readonly string[] GuestOrder =
        {
            "cmcMillerAgent", "cmcWeaverAgent", "cmcApothecaryAgent", "cmcProfessorAgent",
        };

        private static bool _initialized;
        private static readonly System.Collections.Generic.HashSet<string> _warned = new(StringComparer.Ordinal);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, PublishGuest, "AcademyGuestPublish");
            Plugin.Logger.LogDebug("[AcademyGuestPatch] initialized.");
        }

        private static void PublishGuest()
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return; // main menu: nothing to publish

                int guest = 0;
                if (string.Equals(GameQuery.CurrentEnvironmentUniqueId, AcademyInteriorUid, StringComparison.OrdinalIgnoreCase))
                {
                    var present = InnEveningPatch.PresentResidentsIn(gm, AcademyInteriorUid);
                    for (int i = 0; i < GuestOrder.Length && guest == 0; i++)
                    {
                        foreach (var pair in present)
                        {
                            if (string.Equals(pair.Key.AgentUid, GuestOrder[i], StringComparison.OrdinalIgnoreCase))
                            {
                                guest = i + 1;
                                break;
                            }
                        }
                    }
                }

                float current = InnEveningPatch.ReadStat(gm, GuestStatUid);
                if (current < 0f)
                {
                    WarnOnce("guest-read", $"[AcademyGuestPatch] {GuestStatUid} is not readable - the Sit In buttons stay hidden.");
                    return;
                }
                if (Math.Abs(current - guest) < 0.01f) return;

                if (InnEveningPatch.WriteStat(gm, GuestStatUid, guest))
                    Plugin.Logger.LogDebug($"[AcademyGuestPatch] Academy guest {current:0} -> {guest}.");
                else
                    WarnOnce("guest-write", $"[AcademyGuestPatch] {GuestStatUid} write failed - the Sit In buttons will not track who is lecturing.");
            }
            catch (Exception ex)
            {
                WarnOnce("publish-exception", $"[AcademyGuestPatch] PublishGuest failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
