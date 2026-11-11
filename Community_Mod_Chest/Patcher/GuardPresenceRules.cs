using System;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The pure decision behind the crime-gated Town Watch (plan 1.1, Chiwei, Nexus c174400692: "checking
    /// if the player has a crime stat; if so, a guard NPC spawns; otherwise, the guard NPC disappears",
    /// the way vanilla spawns the Moon Wolf and Black Wolf only under a condition).
    ///
    /// <para>Captain Sterling holds the square at every crime level: the escort he sells, theft reports,
    /// the Fugitive's first warning and community service all live on him, several of them at crime 0.
    /// The three patrolling guards (Thorne, Corrin, Vane) are on duty only while the player has a Village
    /// Crime record, is serving a sentence, or has an arrest pending, so the jail warden shifts and the
    /// arrest hand-off never lose their guard. An unreadable crime stat counts as a record (the old,
    /// always-present behaviour), so a broken read never empties the village of its Watch.</para>
    ///
    /// <para>Kept free of Unity and game types, and in C# 5, so
    /// <c>Development_Tools/Tests/CMC-ChiweiOctoberFixes.Tests.ps1</c> can compile this exact file.</para>
    /// </summary>
    public static class GuardPresenceRules
    {
        public const string CaptainAgentUid = "cmcGuardSterlingAgent";

        /// <summary>The guards whose presence follows Village Crime.</summary>
        public static readonly string[] PatrolAgentUids = { "cmcGuardThorneAgent", "cmcGuardCorrinAgent", "cmcGuardVaneAgent" };

        /// <summary>True when the player's record calls the patrol out.</summary>
        /// <param name="crime">Village Crime; ignored when <paramref name="crimeReadable"/> is false.</param>
        /// <param name="crimeReadable">False when the stat could not be read.</param>
        /// <param name="sentenceRemaining">Jail days left; a negative value means unreadable.</param>
        /// <param name="arrestPending">An arrest has been raised and not yet turned into a sentence.</param>
        public static bool PatrolOnDuty(float crime, bool crimeReadable, float sentenceRemaining, bool arrestPending)
        {
            if (!crimeReadable) return true;
            if (crime > 0.0001f) return true;
            if (sentenceRemaining > 0.0001f) return true;
            return arrestPending;
        }

        /// <summary>
        /// Whether the guard with this agent UID should be standing in the village now. False for every
        /// guard under the Quiet Village trait. Sterling is always on duty otherwise; the patrol follows
        /// <see cref="PatrolOnDuty"/>; an unknown UID is left alone (true).
        /// </summary>
        public static bool ShouldBePresent(string agentUid, bool quietVillage, float crime, bool crimeReadable,
                                           float sentenceRemaining, bool arrestPending)
        {
            if (quietVillage) return false;
            if (string.Equals(agentUid, CaptainAgentUid, StringComparison.OrdinalIgnoreCase)) return true;
            if (Array.IndexOf(PatrolAgentUids, agentUid) < 0) return true;
            return PatrolOnDuty(crime, crimeReadable, sentenceRemaining, arrestPending);
        }
    }
}
