using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// A guarded crossing (CMC Part II row N4; session-inferred design, the fee and the duration are tunable
    /// placeholders). Since 1.69.17 the four outskirts tiles let predators through half the time (plan row
    /// 3.4.1). Dragging a Salt onto Captain Sterling ("Hire an Escort", JSON on Agent_GuardSterling.json)
    /// sets the hidden countdown <see cref="StatUid"/> to its maximum (24 ticks, about six hours). While it is
    /// above 0 AND the player stands on an outskirts tile, the half-chance becomes a certainty. The stat
    /// decays by itself (BaseRatePerTick -1), and <see cref="ClearOnReturn"/> zeroes it when the player is
    /// back in the village core after standing in the outskirts, so it can never become a permanent safe zone.
    ///
    /// Built as option (b), a stat-gated guard, not a summoned guard that walks beside the player: a guard
    /// agent is leashed to the village (GuardTerritoryLeashPatch, GuardDutyPatch posts), so one that left his
    /// territory for the trip would fight the leash and break the Watch's coverage of the square.
    ///
    /// Both layers are covered (root CLAUDE.md "Encounter System Patching"):
    ///   1. Encounter starts: one C# guard (<c>EncounterGuards.Register</c> with includeAgentStarts) naming the
    ///      same four predator encounters as EncounterGuards/CMC_VillageOutskirtsWildlife.json. It stops a
    ///      null-NPC start AND an agent's own attack on those encounters. It never sees a player-chosen fight
    ///      (Approach, Give Chase, a dialog Attack): the framework does not evaluate those at all.
    ///   2. Agent arrival: <see cref="WildlifeIndoorGuardPatch"/>'s "outskirts" roll asks
    ///      <see cref="IsEscorted"/> and refuses the move outright while it is true.
    /// What it does NOT stop: a predator already standing on the tile when the escort is bought (a refusal
    /// never evicts), encounters outside the four named, bandits, and any fight the player picks.
    /// </summary>
    internal static class OutskirtsEscortPatch
    {
        internal const string StatUid = "cmcStatOutskirtsEscort";
        /// <summary>TUNABLE PLACEHOLDER: ticks the escort lasts (stat maximum in GameStat/CMC_OutskirtsEscort.json,
        /// and the ValueModifier of the "Hire an Escort" action). One tick is about 15 minutes.</summary>
        internal const float EscortTicks = 24f;
        internal const string HireActionKey = "CMC_Sterling_Escort_Hire";

        internal static readonly HashSet<string> OutskirtsEnvUids = new(StringComparer.OrdinalIgnoreCase)
        {
            "cmcEnvHuntersCrossing", "cmcEnvPineTrail", "cmcEnvHighGrove", "cmcEnvDeerMeadow",
        };

        private static readonly HashSet<string> CoreEnvUids = new(StringComparer.OrdinalIgnoreCase)
        {
            "cmcEnvVillage", "cmcEnvVillagePath", "cmcEnvVillageFarm",
        };

        // Same four encounters as EncounterGuards/CMC_VillageOutskirtsWildlife.json; Development_Tools/Tests/CMC-OutskirtsEscort.Tests.ps1
        // holds this list, OutskirtsEnvUids and CoreEnvUids equal to their JSON guards.
        internal static readonly string[] PredatorEncounterUids =
        {
            "4ce4b386231d17741aa540b11fd8e832",
            "d47e145147d460a4c80d739af904fe8a",
            "e1d6892e60fcc124cb82494be81f2352",
            "27c2b0c2193750344bf5230e72b11d7c",
        };

        private static bool _initialized;
        private static bool _seenInOutskirts;
        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        /// <summary>True while a paid escort is running. An unreadable stat (-1) is not an escort.</summary>
        internal static bool IsEscorted() => HiddenStat.Get(StatUid) > 0f;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            var uids = new HashSet<string>(PredatorEncounterUids, StringComparer.OrdinalIgnoreCase);
            EncounterGuards.Register("CMC_OutskirtsEscort", ctx =>
            {
                try
                {
                    if (ctx.EncounterUid == null || !uids.Contains(ctx.EncounterUid)) return false;
                    var env = GameQuery.CurrentEnvironmentUniqueId;
                    if (env == null || !OutskirtsEnvUids.Contains(env)) return false;
                    return IsEscorted();
                }
                catch (Exception ex)
                {
                    WarnOnce("guard-exception", "The escort guard threw, so the encounter was allowed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
                    return false;
                }
            }, includeAgentStarts: true);

            TickEvents.Interval(3f, ClearOnReturn, "OutskirtsEscortClear");
            Plugin.Logger.LogDebug("[OutskirtsEscortPatch] initialized.");
        }

        /// <summary>Clears the countdown once the player has stood in the outskirts and is back in the village core.</summary>
        private static void ClearOnReturn()
        {
            try
            {
                // Only an escort that is running can be "seen in the outskirts": a flag left over from an
                // earlier trip must not clear one bought in the village a moment ago.
                if (!IsEscorted()) { _seenInOutskirts = false; return; }
                var env = GameQuery.CurrentEnvironmentUniqueId;
                if (env == null) return;
                if (OutskirtsEnvUids.Contains(env)) { _seenInOutskirts = true; return; }
                if (!_seenInOutskirts || !CoreEnvUids.Contains(env)) return;
                _seenInOutskirts = false;
                if (HiddenStat.Set(StatUid, 0f))
                    Plugin.Logger.LogInfo("[OutskirtsEscortPatch] The crossing is done: the escort is cleared.");
            }
            catch (Exception ex)
            {
                WarnOnce("clear-exception", "ClearOnReturn failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning("[OutskirtsEscortPatch] " + message);
        }
    }
}
