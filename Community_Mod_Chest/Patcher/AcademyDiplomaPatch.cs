using System;
using System.Collections;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Graduation diplomas (CMC round 2, J3): passing an Academy course hands the player a framed diploma, one
    /// CT0 card per course (<c>CardData/Item/CMC_Diploma*.json</c>, a small Comfort passive, no trade value).
    ///   - The grant path is <see cref="OnCourseCompleted"/>, called from AcademyPatch.StudyCompleted on a Final
    ///     Exam, through SpawnService.Spawn (the player stands at the lectern, so the current-board rule holds).
    ///     The graduate perk's AddedCardsWarpData route is NOT used: the 1.69.0 as-built rejected it, since an
    ///     in-run perk's AddedCards path is unproven.
    ///   - One hidden clamped GameStat per course (<c>cmcStatDiploma*</c>, 0 or 1) latches the grant. The latch is
    ///     written BEFORE the spawn, so a reload or a second completion can never hand out a second card; a spawn
    ///     that leaves no card on the board puts the latch back to 0 so the backfill below retries it.
    ///   - A backfill (5 s interval, only while the player stands in the Academy interior) hands a diploma to an
    ///     earlier graduate: the perk is held (AcademyCourseService.HasCourse, which sees an in-run perk that
    ///     CardUtil.IsPerkEquipped cannot) and the latch reads 0. It defers to the next Academy visit because
    ///     SpawnService.Spawn places on the current board only.
    /// The table below is the single source: each course maps to exactly one diploma card and one latch stat.
    /// Registration runs once per process under an _initialized guard (pattern b).
    /// </summary>
    internal static class AcademyDiplomaPatch
    {
        internal readonly struct Row
        {
            public readonly string GradPerk;
            public readonly string DiplomaUid;
            public readonly string LatchStat;
            public Row(string gradPerk, string diplomaUid, string latchStat)
            {
                GradPerk = gradPerk; DiplomaUid = diplomaUid; LatchStat = latchStat;
            }
        }

        internal static readonly Row[] Table =
        {
            new Row(AcademyCourseService.GradArchitecture, "cmcDiplomaArchitecture", "cmcStatDiplomaArchitecture"),
            new Row(AcademyCourseService.GradMetallurgy,   "cmcDiplomaMetallurgy",   "cmcStatDiplomaMetallurgy"),
            new Row(AcademyCourseService.GradHerbalism,    "cmcDiplomaHerbalism",    "cmcStatDiplomaHerbalism"),
            new Row(AcademyCourseService.GradFishing,      "cmcDiplomaFishing",      "cmcStatDiplomaFishing"),
            new Row(AcademyCourseService.GradArmorer,      "cmcDiplomaArmorer",      "cmcStatDiplomaArmorer"),
            new Row(AcademyCourseService.GradMedicine,     "cmcDiplomaMedicine",     "cmcStatDiplomaMedicine"),
            new Row(AcademyCourseService.GradCarpentry,    "cmcDiplomaCarpentry",    "cmcStatDiplomaCarpentry"),
        };

        private static bool _initialized;
        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, Backfill, "AcademyDiplomaBackfill");
            Plugin.Logger.LogDebug("[AcademyDiplomaPatch] initialized.");
        }

        /// <summary>The table row for a graduate perk, or null for an unknown perk.</summary>
        internal static Row? RowFor(string gradPerk)
        {
            if (string.IsNullOrEmpty(gradPerk)) return null;
            foreach (var row in Table)
                if (string.Equals(row.GradPerk, gradPerk, StringComparison.Ordinal)) return row;
            return null;
        }

        /// <summary>Called when a Final Exam completes: hands out that course's diploma once.</summary>
        internal static void OnCourseCompleted(string gradPerk)
        {
            try
            {
                var row = RowFor(gradPerk);
                if (row == null)
                {
                    WarnOnce("unknown-perk:" + gradPerk, $"[AcademyDiplomaPatch] no diploma is listed for graduate perk '{gradPerk}'.");
                    return;
                }
                Grant(row.Value);
            }
            catch (Exception ex)
            {
                WarnOnce("complete-exception", $"[AcademyDiplomaPatch] OnCourseCompleted failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void Backfill()
        {
            try
            {
                if (!string.Equals(GameQuery.CurrentEnvironmentUniqueId, AcademyGuestPatch.AcademyInteriorUid, StringComparison.OrdinalIgnoreCase))
                    return;

                foreach (var row in Table)
                {
                    if (!AcademyCourseService.HasCourse(row.GradPerk)) continue;
                    float latch = HiddenStat.Get(row.LatchStat);
                    if (latch < 0f)
                    {
                        WarnOnce("latch-unreadable:" + row.LatchStat, $"[AcademyDiplomaPatch] {row.LatchStat} is not readable - the {row.DiplomaUid} backfill waits for it.");
                        continue;
                    }
                    if (latch >= 0.5f) continue;
                    Grant(row);
                }
            }
            catch (Exception ex)
            {
                WarnOnce("backfill-exception", $"[AcademyDiplomaPatch] Backfill failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void Grant(Row row)
        {
            float latch = HiddenStat.Get(row.LatchStat);
            if (latch < 0f)
            {
                WarnOnce("latch-unreadable:" + row.LatchStat, $"[AcademyDiplomaPatch] {row.LatchStat} is not readable - no {row.DiplomaUid} is handed out until it is.");
                return;
            }
            if (latch >= 0.5f) return; // already handed out

            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return;

            // Latch first: a failed write hands out nothing rather than risking a second card.
            if (!HiddenStat.Set(row.LatchStat, 1f))
            {
                WarnOnce("latch-write:" + row.LatchStat, $"[AcademyDiplomaPatch] {row.LatchStat} write failed - no {row.DiplomaUid} is handed out.");
                return;
            }

            int before = CountOnBoard(gm, row.DiplomaUid);
            SpawnService.Spawn(row.DiplomaUid);
            // SpawnService.Spawn returns null on success (GiveCard is void this game version): re-query the board.
            if (CountOnBoard(gm, row.DiplomaUid) > before)
            {
                Plugin.Logger.LogInfo($"[AcademyDiplomaPatch] Handed out {row.DiplomaUid}.");
                return;
            }

            HiddenStat.Set(row.LatchStat, 0f);
            WarnOnce("spawn-missing:" + row.DiplomaUid, $"[AcademyDiplomaPatch] {row.DiplomaUid} did not appear after the spawn - latch put back, the next Academy visit retries. Is the card loaded?");
        }

        /// <summary>Counts every current-board card of this UID, never stopping at the first.</summary>
        private static int CountOnBoard(object gm, string uid)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return 0;
            int count = 0;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                if (CardUtil.GetCardUniqueId(card) == uid) count++;
            }
            return count;
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
