using System;
using System.Collections.Generic;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The pure decision behind <see cref="SeasonalObstacleCleanupPatch"/>: is this card one of CMC's
    /// seasonal road obstacles (the challenge card of a <c>SealableGates</c> entry whose
    /// <c>SealTrigger</c> is a Season), and is its season over?
    ///
    /// <para>Kept free of Unity and game types, and written in C# 5, so
    /// <c>Development_Tools/Tests/CMC-ChiweiOctoberFixes.Tests.ps1</c> can compile this exact file with
    /// <c>Add-Type</c>, run the rule against save-shaped card ids, and hold <see cref="Obstacles"/> equal
    /// to the Season-triggered gates in the shipped <c>WorldMap/MapNodes.json</c>.</para>
    /// </summary>
    public static class SeasonalObstacleRules
    {
        /// <summary>(ChallengeCardUID, season name as GameQuery.CurrentSeason reads it).</summary>
        public static readonly string[][] Obstacles =
        {
            new[] { "cmcDeadfallSouth",   "Autumn" },
            new[] { "cmcSnowDriftNorth",  "Winter" },
            new[] { "cmcSnowDriftSouth",  "Winter" },
            new[] { "cmcSnowDriftEast",   "Winter" },
            new[] { "cmcClayShoalFlood",  "Spring" },
            new[] { "cmcBrambleThicket",  "Summer" },
        };

        private static Dictionary<string, string> _seasonByCard;

        private static Dictionary<string, string> SeasonByCard
        {
            get
            {
                if (_seasonByCard == null)
                {
                    var map = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var pair in Obstacles) map[pair[0]] = pair[1];
                    _seasonByCard = map;
                }
                return _seasonByCard;
            }
        }

        /// <summary>The leading UID token of a save CardID such as <c>"cmcSnowDriftNorth(cmcSnowDriftNorth)"</c>.</summary>
        public static string BareUid(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            int cut = cardId.IndexOfAny(new[] { '(', '_', '=' });
            string bare = (cut < 0 ? cardId : cardId.Substring(0, cut)).Trim();
            return bare.Length == 0 ? null : bare;
        }

        /// <summary>The season an obstacle belongs to, or null when the card is not a seasonal obstacle.</summary>
        public static string SeasonOf(string cardId)
        {
            string bare = BareUid(cardId);
            if (bare == null) return null;
            string season;
            return SeasonByCard.TryGetValue(bare, out season) ? season : null;
        }

        /// <summary>
        /// True when <paramref name="cardId"/> is a seasonal obstacle and <paramref name="currentSeason"/>
        /// is a different season. False for any other card and whenever the season is unknown (null or
        /// empty): an unreadable season is never a reason to remove anything.
        /// </summary>
        public static bool IsOutOfSeason(string cardId, string currentSeason)
        {
            if (string.IsNullOrEmpty(currentSeason)) return false;
            string season = SeasonOf(cardId);
            if (season == null) return false;
            return !string.Equals(season, currentSeason.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
