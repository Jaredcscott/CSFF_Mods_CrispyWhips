using System;
using System.Collections.Generic;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The pure decision behind <see cref="StrayLocationCardRepairPatch"/>: is this card one of CMC's
    /// twelve WorldMap clone location cards (CT8), and is it sitting on a board that is not its own?
    ///
    /// <para>Kept free of Unity and game types, and written in C# 5, so
    /// <c>Development_Tools/Tests/CMC-StrayLocationCard.Tests.ps1</c> can compile this exact file with
    /// <c>Add-Type</c> and run the rule against save-shaped fixtures, and can hold <see cref="Nodes"/>
    /// equal to the shipped <c>WorldMap/MapNodes.json</c> (EnvironmentUID, LocationUID) pairs.</para>
    ///
    /// <para>Keys: a save names a card <c>"cmcLocVillage(cmcLocVillage)"</c> and an environment
    /// <c>"2b19b942a09fdd148a43798e942a74eb(Env_River_ClearingOak_RiverClearing)"</c>
    /// (<c>UniqueIDScriptable.AddNamesToEnvKey</c>); an instanced room's key carries its parent chain
    /// after the room's own UID. Every UID involved is a bare token (CMC env UIDs never contain an
    /// underscore, root CLAUDE.md WorldMap rules; vanilla ones are hex), so the leading token up to the
    /// first '(' , '_' or '=' is the UID.</para>
    /// </summary>
    public static class StrayLocationCardRules
    {
        /// <summary>(EnvironmentUID, LocationUID) for every node in WorldMap/MapNodes.json.</summary>
        public static readonly string[][] Nodes =
        {
            new[] { "cmcEnvVillagePath",     "cmcLocVillagePath" },
            new[] { "cmcEnvHighGrove",       "cmcLocHighGrove" },
            new[] { "cmcEnvPineTrail",       "cmcLocPineTrail" },
            new[] { "cmcEnvVillage",         "cmcLocVillage" },
            new[] { "cmcEnvVillageFarm",     "cmcLocVillageFarm" },
            new[] { "cmcEnvClayFlats",       "cmcLocClayFlats" },
            new[] { "cmcEnvMarshHollow",     "cmcLocMarshHollow" },
            new[] { "cmcEnvMossyClearing",   "cmcLocMossyClearing" },
            new[] { "cmcEnvForagingForest",  "cmcLocForagingForest" },
            new[] { "cmcEnvHuntersCrossing", "cmcLocHuntersCrossing" },
            new[] { "cmcEnvDeerMeadow",      "cmcLocDeerMeadow" },
            new[] { "cmcEnvBadgerWarren",    "cmcLocBadgerWarren" },
        };

        private static Dictionary<string, string> _homeByLocation;

        private static Dictionary<string, string> HomeByLocation
        {
            get
            {
                if (_homeByLocation == null)
                {
                    var map = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var pair in Nodes) map[pair[1]] = pair[0];
                    _homeByLocation = map;
                }
                return _homeByLocation;
            }
        }

        /// <summary>The leading UID token of a save CardID or environment key; null for null or empty.</summary>
        public static string BareUid(string idOrKey)
        {
            if (string.IsNullOrEmpty(idOrKey)) return null;
            int cut = idOrKey.IndexOfAny(new[] { '(', '_', '=' });
            string bare = cut < 0 ? idOrKey : idOrKey.Substring(0, cut);
            bare = bare.Trim();
            return bare.Length == 0 ? null : bare;
        }

        /// <summary>The home environment UID of a CMC clone location card, or null when the card is not one.</summary>
        public static string HomeEnvOf(string cardId)
        {
            string bare = BareUid(cardId);
            if (bare == null) return null;
            string home;
            return HomeByLocation.TryGetValue(bare, out home) ? home : null;
        }

        /// <summary>
        /// True when <paramref name="cardId"/> is a CMC clone location card and <paramref name="envKeyOrUid"/>
        /// names a different environment. False for every other card, for a card at home, and when either
        /// value is missing (an unreadable key is never a reason to delete anything).
        /// </summary>
        public static bool IsStray(string cardId, string envKeyOrUid)
        {
            string home = HomeEnvOf(cardId);
            if (home == null) return false;
            string env = BareUid(envKeyOrUid);
            if (env == null) return false;
            return !string.Equals(home, env, StringComparison.Ordinal);
        }
    }
}
