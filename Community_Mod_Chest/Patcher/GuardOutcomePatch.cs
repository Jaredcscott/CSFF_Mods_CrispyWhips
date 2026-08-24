using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Village Guards — gauntlet bookkeeping (Village_Master_Plan.md §10.8.6).
    ///
    /// <para><b>What this class deliberately does NOT do.</b> It does not decide who won a fight,
    /// mark a guard as beaten, or record that the player lost. All three are declarative, on the
    /// guard's own Encounter asset:</para>
    /// <list type="bullet">
    /// <item><c>EnemyDefeatedEffects</c> / <c>EnemyEscapedEffects</c> carry an
    /// <c>NPCStatChanges</c> entry with <c>UseAssociatedAgent</c>, which writes
    /// <c>cmcNpcStatGuardDowned = 1</c> onto whichever guard just lost — a kill and a forced rout
    /// both count as the player besting them (§10.8.4.3).</item>
    /// <item><c>PlayerDemoralizedEffects.StatChanges</c> raises <c>cmcStatArrestPending</c>, the
    /// clamped 0/1 marker <see cref="JailPatch"/> (§10.8.7.2) consumes, and the same block's
    /// <c>MovePlayer</c>/<c>MovePlayerToSpecificEnvironment</c> fields carry the player into the
    /// jail cell through vanilla's own post-encounter travel path.</item>
    /// <item><c>SaveEncounterToNPC</c> on every result the guard survives persists their remaining
    /// Blood/Stamina/Morale, which is what makes four sequential duels read as one running battle
    /// (.decomp/EncounterPopup.cs:2826-2828, restored at :456-459).</item>
    /// </list>
    ///
    /// <para><b>Why there is no <c>GameManager.OnEncounter*</c> subscription.</b> Those four static
    /// events are real and stable (.decomp/GameManager.cs:52-58, fired from
    /// EncounterPopup.PressContinue at :2311/2321/2331/2341), and the original plan reserved a
    /// subscriber for the two things effect blocks "cannot express": the all-four-are-down
    /// transition and the arrest handoff. Neither turned out to need one. The markers the effect
    /// blocks write are readable at any time, so this class polls them instead — which is strictly
    /// more robust, because a poll also reconciles state after a save/load, whereas an event only
    /// fires at the instant a fight ends. The arrest handoff is likewise a GameStat rather than a
    /// C# event, so a JSON-authored quest/dialog gate in Prompt 7 can read it directly instead of
    /// having to subscribe from C#.</para>
    ///
    /// <para><b>What genuinely needs C#</b>, and is all this class owns:</para>
    /// <list type="number">
    /// <item><b>The season respawn timer.</b> Arms when a guard's downed marker first appears,
    /// clears the marker one season later. Same stat-timer idiom as the Animal System's
    /// <c>AnimalLifecycleTicker</c> (framework <c>Animals/AnimalLifecycleTicker.cs</c>), with one
    /// deliberate difference: the timer lives in a hidden PLAYER GameStat, not in the guard's own
    /// NPCStats. An NPCStat dies with its NPC, and a killed guard is exactly the case the timer
    /// exists to handle.</item>
    /// <item><b>The aggregate-down latch.</b> All four down at once sets
    /// <c>cmcStatGuardsAllDown</c>, the ELIGIBILITY gate for the actual pardon — a confession
    /// dialog with the Inn Keeper (<c>CMC_InnKeeperTalk_GuardsConfession</c>), who has to warn
    /// the player about what they have done before Village Crime actually clears (owner decision,
    /// 2026-08-15: killing guards must stay punished on the numbers — see the per-kill
    /// <c>cmcStatVillageCrime</c> StatChanges already on every guard Encounter's
    /// <c>EnemyDefeatedEffects</c> — until the player deliberately comes clean; an automatic
    /// reset the instant the fourth guard falls undid that penalty before it meant anything).
    /// This class does NOT clear the crime stat itself anymore — it only latches/unlatches
    /// eligibility, exactly like every other declarative gate in this codebase.</item>
    /// <item><b>Respawn suppression.</b> <see cref="IsRespawnSuppressed"/> keeps
    /// <c>GuardSpawnPatch</c> from FIRST-PLACING a guard who is serving out a season.</item>
    /// </list>
    ///
    /// <para><b>What "down" means, stated plainly:</b> a routed guard (Morale hit zero,
    /// <c>EnemyEscapedEffects</c>) stays standing on the board — "beaten, but not dead" is the
    /// guard's own line, and the marker alone is what takes her out of the chase until the season
    /// timer clears it. A KILLED guard (Blood hit zero, <c>EnemyDefeatedEffects</c>) is different:
    /// her body is despawned the instant the kill resolves by <see cref="OnGuardDefeated"/>
    /// (subscribed to <c>GameManager.OnEncounterEnemyDefeated</c>), and <see cref="CheckKilledGuards"/>
    /// polls <c>cmcNpcStatGuardKilled</c> on a still-live NPC only as a save/load reconciliation
    /// backstop — the marker is what the engine writes late (via a time-costed end-of-encounter
    /// action, .decomp/EncounterPopup.cs:2778-2791), which is why relying on the poll alone left the
    /// body standing for ~30 in-game minutes after the kill. That marker is separate from <c>cmcNpcStatGuardDowned</c>
    /// deliberately — both effects set the shared downed marker (it also arms the respawn timer
    /// either way), but only <c>EnemyDefeatedEffects</c> also sets the killed one, which is how
    /// this class tells a corpse from a rout without touching the Encounter's own dispatch.
    /// <c>EncounterResultEffect</c> (the class backing both effect blocks) has no
    /// <c>DeleteNPC</c>-equivalent field of its own — that struct only exists on
    /// <c>CardAction</c>/<c>DialogAction</c>/<c>NPCAction</c> — so the removal cannot be authored
    /// declaratively on the Encounter and has to happen here. Suppression is wired to first
    /// placement only, and deliberately NOT to <c>GuardSpawnPatch</c>'s restore path: skipping a
    /// restore would make a merely-routed guard vanish on the next reload.</para>
    ///
    /// <para><b>A killed guard's cooldown is shorter than a rout's, and she comes back through
    /// the Jail, not her post (owner request, 2026-08-15).</b> <see cref="KilledSeasonDays"/> (7)
    /// applies instead of the normal season whenever <see cref="WasLastDownKilled"/> is true for
    /// that guard — set alongside a fresh (re-)arm of her DownDay timer, in the SAME poll tick and
    /// BEFORE <see cref="CheckKilledGuards"/> removes her body, and checked on EVERY poll rather
    /// than only while she is not yet armed (see <see cref="Run"/>'s ordering comment — a
    /// rout-then-kill, the mod's own advertised common case, was Attempt 2's remaining bug: her
    /// DownDay timer was already armed from the rout, so the old "only on first arm" gate skipped
    /// the kill entirely). Unlike the DownDay timer, the killed flag is NOT cleared when the 7 days
    /// are up — it stays set until <c>GuardSpawnPatch</c> actually re-places her in the Jail,
    /// because <c>GameManager.CurrentSaveData</c> (<c>=&gt; CurrentGameData</c>,
    /// .decomp/GameManager.cs:734) is a snapshot taken once at load/new-game (assigned only at
    /// .decomp/GameManager.cs:2039/2048/2053/2638) and never refreshed mid-session — a killed
    /// guard's entry in it still describes where she stood before she died for the rest of that
    /// session, even though the actual save FILE is correctly rebuilt fresh from live
    /// <c>AllNPCs</c> on every save (.decomp/GameLoad.cs:742). Restoring from that stale in-memory
    /// snapshot would resurrect her at her old post the instant suppression lifts, via
    /// <c>GuardSpawnPatch</c>'s env-independent restore path. <c>GuardSpawnPatch</c> reads the flag
    /// twice: to redirect first placement to the Jail instead of her normal post, and to skip that
    /// stale-save restore for as long as it is set.</para>
    ///
    /// <para>Season length is <c>GameManager.DaysPerMoon</c> (30). <c>DaysPerStar</c> is 120
    /// (.decomp/GameManager.cs:116-118), so a year is exactly four moons and one moon is one
    /// season; there is no separate season-length constant in the game or in
    /// <see cref="VillageClock"/>. <see cref="KilledSeasonDays"/> (7) is an independent literal
    /// chosen to read as "a week," not a shared constant with anything else.</para>
    ///
    /// <para><b>Known, deliberately unaddressed interaction, flagged rather than silently
    /// fixed:</b> shortening a killed guard's cooldown to 7 days shrinks the window in which all
    /// four guards can be simultaneously down for <see cref="CheckAllDown"/>'s pardon-eligibility
    /// latch. If a player kills guards spread more than ~7 days apart, an earlier kill's window can
    /// close before a later one opens, and the Inn Keeper confession path never becomes available
    /// for that playthrough. This is a real design tradeoff of the owner's explicit "7 days"
    /// request, not an oversight — surfaced here rather than silently worked around, since fixing
    /// it would mean either not shortening the killed cooldown (contradicting the request) or
    /// redefining "all down" to not require simultaneity (a separate, larger design change).</para>
    /// </summary>
    internal static class GuardOutcomePatch
    {
        /// <summary>
        /// The four guards. MUST stay in sync with <c>GuardDutyPatch.Beats</c>,
        /// <c>GuardSpawnPatch.Guards</c> and <c>GuardWitnessPatch.Guards</c> — a guard missing here
        /// never arms a timer, and (worse) can never satisfy the all-four-down pardon, silently
        /// stranding the player at Banished forever.
        /// </summary>
        private static readonly (string AgentUid, string Label, string DownDayStatUid, string KilledFlagStatUid)[] Guards =
        {
            ("cmcGuardSterlingAgent", "Captain Reeve Sterling", "cmcStatGuardDownDaySterling", "cmcStatGuardKilledFlagSterling"),
            ("cmcGuardThorneAgent",  "Guard Nella Thorne",    "cmcStatGuardDownDayThorne",    "cmcStatGuardKilledFlagThorne"),
            ("cmcGuardCorrinAgent",  "Guard Old Corrin",      "cmcStatGuardDownDayCorrin",    "cmcStatGuardKilledFlagCorrin"),
            ("cmcGuardVaneAgent",    "Guard Iris Vane",       "cmcStatGuardDownDayVane",      "cmcStatGuardKilledFlagVane"),
        };

        /// <summary>
        /// Every guard-kill Encounter UID → its guard's index in <see cref="Guards"/>. Lets
        /// <see cref="OnGuardDefeated"/> despawn the right guard the instant her fight resolves,
        /// instead of waiting for the polled <c>cmcNpcStatGuardKilled</c> marker (which the engine
        /// only writes via a time-costed end-of-encounter <c>DismantleCardAction</c>). Sterling owns
        /// three encounters; the arrest asset forces <c>PlayerDemoralized</c> so its entry never
        /// actually resolves to <c>EnemyDefeated</c>, but mapping it costs nothing and is robust.
        /// MUST stay in sync with <c>GuardDutyPatch.Beats</c>'s per-guard Encounter UIDs.
        /// </summary>
        private static readonly Dictionary<string, int> KillEncounterToGuard = new(StringComparer.OrdinalIgnoreCase)
        {
            { "cmcEncounterGuardSterling",   0 },
            { "cmcEncounterSterlingConverge", 0 },
            { "cmcEncounterSterlingArrest",  0 },
            { "cmcEncounterGuardThorne",     1 },
            { "cmcEncounterGuardCorrin",     2 },
            { "cmcEncounterGuardVane",       3 },
        };

        private const string AllDownLatchStatUid = "cmcStatGuardsAllDown";

        /// <summary>Set to 1 ONLY by <c>EnemyDefeatedEffects</c> (a kill), never by
        /// <c>EnemyEscapedEffects</c> (a rout) — see the class doc. <see cref="CheckKilledGuards"/>
        /// reads it once per still-live NPC and never clears it; the NPC is gone by the next poll.</summary>
        private const string GuardKilledStatUid = "cmcNpcStatGuardKilled";

        /// <summary>A killed guard's cooldown, distinct from and shorter than a routed guard's full
        /// season (owner request, 2026-08-15) — killing one is meant to read as a heavier but
        /// faster-resolving consequence than merely breaking her morale, tied to the Jail rather
        /// than her old post. See <see cref="WasLastDownKilled"/>.</summary>
        private const int KilledSeasonDays = 7;

        /// <summary>Set to 1 by every guard Encounter's <c>PlayerDemoralizedEffects</c>.
        /// <see cref="JailPatch"/> (§10.8.7.2) consumes it; see <see cref="ArrestPending"/>.</summary>
        internal const string ArrestPendingStatUid = "cmcStatArrestPending";

        /// <summary>
        /// Hidden count of guards killed since the last full jail sentence (owner request,
        /// 2026-08-15: killing a guard should carry a heavier, distinct jail sentence — 7 days per
        /// guard killed — rather than being folded into the same crime/8 formula every other
        /// incident uses, since a single kill already maxes crime to its 100 ceiling and would
        /// otherwise always read as "the same 8-day cap as starting one fight"). Incremented here,
        /// in both kill-detection sites (<see cref="OnGuardDefeated"/> and <see cref="Run"/>'s
        /// inline block), the same tick <see cref="GuardKilledStatUid"/>-equivalent per-guard flag
        /// is armed. <see cref="JailPatch"/> reads it to pick the sentence formula and resets it to
        /// 0 once that sentence is served in full — see <c>GameStat/CMC_GuardKillsPending.json</c>.
        /// </summary>
        internal const string GuardKillsPendingStatUid = "cmcStatGuardKillsPending";

        /// <summary>Fallback if <c>GameManager.DaysPerMoon</c> cannot be read — the shipped value.</summary>
        private const int DefaultSeasonDays = 30;

        private static bool _initialized;
        private static object _guardDownedStat;         // NPCStat SO, resolved once
        private static object _guardKilledStat;          // NPCStat SO, resolved once
        private static MethodInfo _getFromIdMethod;
        private static MethodInfo _removeNpcMethod;       // GameManager.RemoveNPC(InGameNPC), resolved once
        private static bool _lastArrestPending;
        private static bool _loggedDownedStatMissing;
        private static bool _loggedKilledStatMissing;
        private static bool _loggedRemoveNpcMissing;

        /// <summary>
        /// Last observed <c>cmcStatSterlingEscapeCount</c>, for the transition log in
        /// <see cref="CheckSterlingChances"/>. -1 = never read (pre-GameManager ticks).
        /// </summary>
        private static float _lastSterlingEscapeCount = -1f;

        // ── Extension point for the arrest-and-sentence chunk (§10.8.7.2) ─────────

        /// <summary>
        /// True while the player has lost a fight to a Town Watch guard and has not yet been
        /// processed. <see cref="JailPatch"/> gates on this and calls
        /// <see cref="ClearArrestPending"/> as it hands down the sentence.
        /// </summary>
        internal static bool ArrestPending => HiddenStat.Get(ArrestPendingStatUid) >= 0.5f;

        /// <summary>
        /// Raised on the 0 -> 1 transition of <see cref="ArrestPending"/>, for a subscriber that
        /// wants the moment rather than the state. Deliberately raised from this poll and NOT from
        /// a <c>GameManager.OnEncounter*</c> subscription, so <see cref="JailPatch"/> has exactly
        /// one place to hook and cannot end up double-handling the same arrest.
        /// </summary>
        internal static event Action ArrestPendingRaised;

        /// <summary>Clears the marker. <see cref="JailPatch"/> calls this as the sentence begins.</summary>
        internal static void ClearArrestPending()
        {
            if (HiddenStat.Set(ArrestPendingStatUid, 0f)) _lastArrestPending = false;
        }

        /// <summary>
        /// True while this guard is serving out their season. Read by <c>GuardSpawnPatch</c> on
        /// its FIRST-PLACEMENT path only — see the class doc for why the restore path must not
        /// honour it.
        /// </summary>
        internal static bool IsRespawnSuppressed(string agentUid)
        {
            var guard = Guards.FirstOrDefault(g =>
                string.Equals(g.AgentUid, agentUid, StringComparison.OrdinalIgnoreCase));
            if (guard.AgentUid == null) return false;
            // Unreadable (-1) must not suppress: a stat that failed to load would otherwise
            // permanently delete the Watch (feedback_subsystem_graceful_degradation).
            return HiddenStat.Get(guard.DownDayStatUid) >= 0.5f;
        }

        /// <summary>
        /// True while this guard's most recent defeat was a kill rather than a rout — see the
        /// class doc's "killed guard's cooldown" section. Stays true past the point
        /// <see cref="IsRespawnSuppressed"/> goes false (the 7-day cooldown ending does NOT clear
        /// this flag) until <c>GuardSpawnPatch</c> actually re-places her in the Jail and calls
        /// <see cref="ClearKilledFlag"/>. <c>GuardSpawnPatch</c> reads this to redirect first
        /// placement to the Jail and to skip her stale pre-kill save data on the restore path.
        /// </summary>
        internal static bool WasLastDownKilled(string agentUid)
        {
            var guard = Guards.FirstOrDefault(g =>
                string.Equals(g.AgentUid, agentUid, StringComparison.OrdinalIgnoreCase));
            if (guard.AgentUid == null) return false;
            return HiddenStat.Get(guard.KilledFlagStatUid) >= 0.5f;
        }

        /// <summary>One-shot: <c>GuardSpawnPatch</c> calls this once it has actually placed this
        /// guard in the Jail again, so subsequent restores/first-placements treat her normally
        /// until her next kill.</summary>
        internal static void ClearKilledFlag(string agentUid)
        {
            var guard = Guards.FirstOrDefault(g =>
                string.Equals(g.AgentUid, agentUid, StringComparison.OrdinalIgnoreCase));
            if (guard.AgentUid == null) return;
            HiddenStat.Set(guard.KilledFlagStatUid, 0f);
        }

        // ── Poll ──────────────────────────────────────────────────────────────────

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, Run, "GuardOutcome");
            try
            {
                GameManager.OnEncounterEnemyDefeated += OnGuardDefeated;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning(
                    "[GuardOutcomePatch] failed to subscribe to GameManager.OnEncounterEnemyDefeated — " +
                    "killed guards will fall back to the slower poll-based despawn: " +
                    (ex.InnerException?.ToString() ?? ex.ToString()));
            }
            Plugin.Logger.LogDebug("[GuardOutcomePatch] initialized.");
        }

        /// <summary>
        /// Despawns a guard the instant her kill resolves, instead of waiting for the 5s poll to
        /// observe the NPCStat marker. The marker (<c>cmcNpcStatGuardKilled</c>) is written ONLY by
        /// the Encounter's <c>EnemyDefeatedEffects.NPCStatChanges</c>, which the engine bundles into
        /// a time-costed end-of-encounter <c>DismantleCardAction</c> (<c>SetDayTimeCost(1)</c>,
        /// .decomp/EncounterPopup.cs:2778-2791) performed only after the player presses Continue — so
        /// the body used to linger until game time advanced (~30 in-game minutes, owner report
        /// 2026-08-15). <c>OnEncounterEnemyDefeated</c> fires the moment Blood hits 0
        /// (.decomp/EncounterPopup.cs:2311), before that action, so arming + removal here is
        /// immediate. A rout fires <c>OnEncounterEnemyEscaped</c> instead, so this never runs for a
        /// merely-routed guard — the kill/rout distinction is free. The 5s <see cref="Run"/> poll
        /// stays as the save/load reconciliation backstop (and a no-op once the NPC is gone: its
        /// kill block is gated on <c>KilledFlag &lt; 0.5</c>, already set here). Removing the NPC
        /// while the result popup is still open is a supported case, not a race — vanilla's own
        /// end-of-encounter path null-guards <c>AssociatedNPC</c> throughout
        /// (.decomp/EncounterPopup.cs:2614/2778/2826). Same plain-<c>+=</c> precedent as
        /// <see cref="GuardCombatDiagnosticPatch"/> and vanilla's <c>CombatSubObjective</c>.
        /// </summary>
        private static void OnGuardDefeated(Encounter model)
        {
            try
            {
                if (model == null || !KillEncounterToGuard.TryGetValue(model.UniqueID, out int gi)) return;
                var guard = Guards[gi];

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;
                var npc = FindLiveNpc(gm, guard.AgentUid);
                if (npc == null) return; // her NPC isn't spawned (already removed, or off-board)

                // Arm the kill state now, from the day of the kill — identical to Run()'s kill
                // block, but without waiting for the marker. A kill overrides any prior rout arming,
                // so DownDay is re-armed fresh (the 7-day countdown starts at the moment of death,
                // not wherever a prior rout's 30-day countdown had reached).
                int today = GameQuery.CurrentDay;
                HiddenStat.Set(guard.DownDayStatUid, today + 1);
                if (!HiddenStat.Set(guard.KilledFlagStatUid, 1f))
                    Plugin.Logger.LogWarning(
                        $"[GuardOutcomePatch] {guard.Label}'s killed flag failed to write on the defeat " +
                        "event — she will be treated as merely routed (season-length cooldown, own post).");
                IncrementGuardKillsPending(guard.Label);
                Plugin.Logger.LogInfo(
                    $"[GuardOutcomePatch] {guard.Label} was killed in combat — despawning immediately (day {today}); " +
                    $"back on duty from the Village Jail in {KilledSeasonDays} days.");

                RemoveGuardNpc(gm, npc, guard.Label);
                AlertNearbyGuards(guard.Label);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardOutcomePatch] OnGuardDefeated failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// Owner report, 2026-08-16: killing a guard maxes <c>cmcStatVillageCrime</c> (each guard's
        /// own <c>EnemyDefeatedEffects.StatChanges</c>) and, as of the same fix, sets
        /// <c>cmcStatGuardsSummoned</c> so Captain Sterling's own summon duty can finally respond to
        /// a kill directly instead of only to Vane's alarm or losing to Thorne/Corrin — but neither
        /// stat change is itself a re-check signal for the OTHER guards standing right there. Their
        /// own chase duty gates on Crime via <c>GeneralCondition</c>, re-evaluated only on the next
        /// natural per-DTP-tick duty selection (up to 15 in-game minutes) unless something forces an
        /// immediate re-check — <c>GuardWitnessPatch.ReportIncident</c> already does exactly that for
        /// every guard standing in the incident's environment, but it previously only fired from
        /// <c>StartEncounter_Postfix</c> (the moment a fight STARTS, before a kill has raised Crime
        /// at all). Calling it again here, at kill resolution, closes that gap for any co-located
        /// guard: same environment-generic entry point <c>GuardWitnessPatch</c>'s own class doc
        /// already advertises as safe for exactly this ("victim- and environment-generic by design,
        /// not guard-chase-specific"). Deliberately NOT wired into the <see cref="Run"/> poll's kill
        /// block — that path only reconciles state after a save/load, when there is no "moment of
        /// the kill" left for a co-located guard to react to.
        /// </summary>
        private static void AlertNearbyGuards(string victimLabel)
        {
            var environmentUid = GameQuery.CurrentEnvironmentUniqueId;
            if (string.IsNullOrEmpty(environmentUid))
            {
                Plugin.Logger.LogDebug($"[GuardOutcomePatch] {victimLabel}'s kill environment unknown — nearby guards will notice on their next natural tick instead.");
                return;
            }
            GuardWitnessPatch.ReportIncident(null, null, environmentUid);
        }

        private static void Run()
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                CheckArrestPending();
                CheckSterlingChances();

                int seasonDays = Math.Max(1, GameQuery.DaysPerMoon > 0 ? GameQuery.DaysPerMoon : DefaultSeasonDays);
                int today = GameQuery.CurrentDay;

                bool allDown = true;
                foreach (var guard in Guards)
                {
                    // Detect a kill on this tick's still-live NPC FIRST, unconditionally — not
                    // only while she is not yet armed. A guard who was ROUTED earlier already has
                    // DownDayStatUid armed; nothing stops the player attacking her again
                    // (Agent_GuardThorne.json's Attack interaction has no Conditions gate) and
                    // actually killing her this time (EncounterPopup resolves EnemyDefeated before
                    // EnemyEscaped, so a finishing blow reads as a kill). Gating this on "not yet
                    // armed" was Attempt 2's bug: a rout-then-kill — the mod's own advertised
                    // common case, "guards break and flee... actually killing one takes real
                    // determination" — skipped this block entirely, so CheckKilledGuards (below)
                    // still removed her body with no cooldown re-armed and no killed flag set,
                    // reproducing the original instant-respawn bug via the stale-save restore path.
                    // Re-arms DownDayStatUid fresh from today even if it was already armed, so the
                    // 7-day countdown starts at the moment of the kill rather than wherever a prior
                    // rout's 30-day countdown had reached (which could be past day 7 already).
                    if (HiddenStat.Get(guard.KilledFlagStatUid) < 0.5f
                        && ReadKilledMarkerByAgent(gm, guard.AgentUid) >= 0.5f)
                    {
                        HiddenStat.Set(guard.DownDayStatUid, today + 1);
                        if (!HiddenStat.Set(guard.KilledFlagStatUid, 1f))
                            Plugin.Logger.LogWarning(
                                $"[GuardOutcomePatch] {guard.Label}'s killed flag failed to write — " +
                                "she will be treated as merely routed (season-length cooldown, own post) " +
                                "instead of the shorter Jail cooldown.");
                        IncrementGuardKillsPending(guard.Label);
                        Plugin.Logger.LogInfo(
                            $"[GuardOutcomePatch] {guard.Label} is down (day {today}) — killed; back on duty from the Village Jail in {KilledSeasonDays} days.");
                    }

                    float armed = HiddenStat.Get(guard.DownDayStatUid);
                    if (armed < 0f) { allDown = false; continue; } // stat not readable yet

                    if (armed < 0.5f)
                    {
                        allDown = false;
                        // Arm on the first poll that sees this guard's declarative ROUT marker set
                        // (a kill was already handled, and already re-armed, above). +1 so 0 stays
                        // an unambiguous "not down" sentinel, the same offset VillageClock uses for
                        // its epoch day.
                        if (ReadDownedMarker(gm, guard.AgentUid) >= 0.5f
                            && HiddenStat.Set(guard.DownDayStatUid, today + 1))
                            Plugin.Logger.LogInfo(
                                $"[GuardOutcomePatch] {guard.Label} is down (day {today}) — back on duty in {seasonDays} days.");
                        continue;
                    }

                    bool wasKilled = HiddenStat.Get(guard.KilledFlagStatUid) >= 0.5f;
                    int seasonDaysForGuard = wasKilled ? KilledSeasonDays : seasonDays;
                    if (today - ((int)Math.Round(armed) - 1) < seasonDaysForGuard) continue; // still serving

                    ClearDownedMarker(gm, guard.AgentUid);
                    HiddenStat.Set(guard.DownDayStatUid, 0f);
                    // KilledFlagStatUid is deliberately NOT cleared here — see class doc. It stays
                    // set (and keeps redirecting GuardSpawnPatch to the Jail / blocking the stale
                    // restore) until she is actually re-placed.
                    allDown = false;
                    Plugin.Logger.LogInfo(wasKilled
                        ? $"[GuardOutcomePatch] {guard.Label}'s Jail cooldown has ended — she will return to the Watch the next time she is found in the Village Jail."
                        : $"[GuardOutcomePatch] {guard.Label} has recovered and returns to the Watch.");
                }

                // Runs AFTER the arming loop above, deliberately — see that loop's comment. Any
                // guard removed here already had her markers read and her cooldown armed earlier
                // in this same tick, so nothing downstream still needs her live NPC.
                CheckKilledGuards(gm);

                CheckAllDown(allDown);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardOutcomePatch] Run failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void CheckArrestPending()
        {
            bool pending = ArrestPending;
            if (pending && !_lastArrestPending)
            {
                Plugin.Logger.LogDebug(
                    "[GuardOutcomePatch] The Watch has beaten the player — handing the arrest to JailPatch.");
                try { ArrestPendingRaised?.Invoke(); }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[GuardOutcomePatch] ArrestPendingRaised subscriber threw: {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
            }
            _lastArrestPending = pending;
        }

        /// <summary>
        /// §10.8.11.9 diagnostics. The escape counter itself is written declaratively by
        /// <c>cmcEncounterGuardSterling</c>'s <c>EscapeOptionStatChanges</c> and cleared by
        /// <c>cmcEncounterSterlingArrest</c>'s <c>PlayerDemoralizedEffects</c> — no C# touches it.
        /// That means the ONLY way to confirm the mechanic from a log is to observe the value
        /// change, so this logs every transition at Info (root CLAUDE.md: diagnostics meant to be
        /// read in <c>LogOutput.log</c> must be <c>LogInfo</c>; <c>LogDebug</c> is filtered out by
        /// default and costs a whole restart cycle to discover).
        ///
        /// <para>Polled rather than hooked to <c>GameManager.OnEncounterSkipped</c> deliberately,
        /// for the same reason this class polls the arrest marker instead of subscribing to the
        /// four <c>OnEncounter*</c> events (see the class doc): a poll also reconciles the value
        /// after a save/load, and <c>OnEncounterSkipped</c> is a plain static field rather than a
        /// C# event, so any other mod assigning to it with <c>=</c> would silently drop us.</para>
        /// </summary>
        private static void CheckSterlingChances()
        {
            float count = HiddenStat.Get(GuardDutyPatch.SterlingEscapeCountStatUid);
            if (count < 0f) return; // stat not readable yet

            if (_lastSterlingEscapeCount < 0f) { _lastSterlingEscapeCount = count; return; } // first read
            if (Math.Abs(count - _lastSterlingEscapeCount) < 0.01f) return;

            float previous = _lastSterlingEscapeCount;
            _lastSterlingEscapeCount = count;

            if (count < previous)
            {
                Plugin.Logger.LogInfo(
                    $"[GuardOutcomePatch] Captain Sterling's leniency counter reset ({previous:0} -> {count:0}) " +
                    "— the arrest resolved, so the next manhunt starts with a full set of chances.");
                return;
            }

            int remaining = Math.Max(0, 3 - (int)Math.Round(count));
            if (remaining > 0)
                Plugin.Logger.LogInfo(
                    $"[GuardOutcomePatch] The player thought better of fighting Captain Sterling " +
                    $"({count:0} of 3 used, {remaining} left).");
            else
                Plugin.Logger.LogInfo(
                    "[GuardOutcomePatch] Captain Sterling is out of chances to give (3 of 3 used) — " +
                    "his own Attack button now opens cmcEncounterSterlingArrest directly (Agent_GuardSterling.json's " +
                    "second Interactions entry), and Duty_SterlingForceArrest is the selectable summon-response " +
                    "duty if he's instead sent after the player via cmcStatCaptainSummoned (losing to Thorne or " +
                    "Corrin). Either path forces the arrest with no escape option.");
        }

        /// <summary>
        /// Despawns any guard whose live NPC has <c>cmcNpcStatGuardKilled</c> set — see the class
        /// doc for why a kill needs this and a rout does not. Cheap even when nobody was just
        /// killed: <see cref="FindLiveNpc(object,string)"/> only walks <c>AllNPCs</c>, which stays
        /// tiny (four guards plus whatever else is spawned).
        /// </summary>
        private static void CheckKilledGuards(object gm)
        {
            foreach (var guard in Guards)
            {
                var npc = FindLiveNpc(gm, guard.AgentUid);
                if (npc == null) continue; // never spawned, or already removed

                if (ReadKilledMarker(npc) < 0.5f) continue;

                RemoveGuardNpc(gm, npc, guard.Label);
            }
        }

        /// <summary>Same as <see cref="ReadKilledMarker(object)"/> but resolves the live NPC by
        /// agent UID first — used by the arming loop in <see cref="Run"/>, which only has the
        /// agent UID at that point, before <see cref="CheckKilledGuards"/> runs.</summary>
        private static float ReadKilledMarkerByAgent(object gm, string agentUid)
        {
            var npc = FindLiveNpc(gm, agentUid);
            return npc == null ? 0f : ReadKilledMarker(npc);
        }

        private static float ReadKilledMarker(object npc)
        {
            var stat = ResolveKilledStat();
            if (stat == null) return 0f;

            var getStatValue = npc.GetType().GetMethod("GetStatValue",
                BindingFlags.Instance | BindingFlags.Public, null, new[] { stat.GetType() }, null);
            if (getStatValue == null)
            {
                Plugin.Logger.LogDebug("[GuardOutcomePatch] InGameNPC.GetStatValue(NPCStat) not found — killed markers unreadable.");
                return 0f;
            }
            return getStatValue.Invoke(npc, new[] { stat }) is float f ? f : 0f;
        }

        private static object ResolveKilledStat()
        {
            if (_guardKilledStat != null) return _guardKilledStat;
            if (_getFromIdMethod == null) ResolveDownedStat(); // shares the lazy UniqueIDScriptable.GetFromID lookup
            if (_getFromIdMethod == null) return null;

            _guardKilledStat = _getFromIdMethod.Invoke(null, new object[] { GuardKilledStatUid });
            if (_guardKilledStat == null && !_loggedKilledStatMissing)
            {
                _loggedKilledStatMissing = true;
                Plugin.Logger.LogWarning(
                    $"[GuardOutcomePatch] NPCStat '{GuardKilledStatUid}' not found — killed guards will stay standing on the board.");
            }
            return _guardKilledStat;
        }

        /// <summary>
        /// Despawns a guard's InGameNPC the same way vanilla removes one for a <c>DeleteNPC</c>
        /// card action (.decomp/GameManager.cs:9876 <c>RemoveNPC</c>): drops it from
        /// <c>AllNPCs</c>, runs <c>InGameNPC.DestroyNPC</c>, and removes/destroys the associated
        /// board card. Reflection is required because the method is private — same
        /// resolve-once-and-invoke idiom <see cref="GuardDutyPatch.OnAttackWitnessed"/> uses for
        /// <c>InGameNPC.CheckForDuties</c>. <c>AllNPCs.Remove</c> runs synchronously inside
        /// <c>RemoveNPC</c> before its first <c>yield</c>, and <c>StartCoroutine</c> drives an
        /// iterator's body up to that first yield immediately — so by the time this call returns,
        /// <see cref="FindLiveNpc(object,string)"/> can no longer find this NPC, and the next poll
        /// will not try to remove it again.
        /// </summary>
        private static void RemoveGuardNpc(object gm, object npc, string label)
        {
            if (gm is not MonoBehaviour gmBehaviour)
            {
                Plugin.Logger.LogWarning($"[GuardOutcomePatch] GameManager instance is not a MonoBehaviour — cannot despawn {label}.");
                return;
            }

            if (_removeNpcMethod == null)
            {
                _removeNpcMethod = gm.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "RemoveNPC" && m.GetParameters().Length == 1);
                if (_removeNpcMethod == null)
                {
                    if (!_loggedRemoveNpcMissing)
                    {
                        _loggedRemoveNpcMissing = true;
                        Plugin.Logger.LogWarning("[GuardOutcomePatch] GameManager.RemoveNPC(InGameNPC) not found — killed guards will stay standing on the board.");
                    }
                    return;
                }
            }

            if (_removeNpcMethod.Invoke(gm, new[] { npc }) is not IEnumerator routine)
            {
                Plugin.Logger.LogWarning($"[GuardOutcomePatch] RemoveNPC did not return a coroutine for {label} — despawn skipped.");
                return;
            }

            gmBehaviour.StartCoroutine(routine);
            Plugin.Logger.LogInfo($"[GuardOutcomePatch] {label}'s body is removed from the board.");
        }

        /// <summary>
        /// Latches <c>cmcStatGuardsAllDown</c> while all four guards are simultaneously down.
        /// Deliberately does NOT clear Village Crime — that used to happen automatically here, but
        /// it meant a lethal, crime-maxing kill of the fourth guard was wiped clean in the same
        /// tick, before the player ever felt the weight of it. The actual pardon is now the
        /// Inn Keeper confession dialog (<c>CMC_InnKeeperTalk_GuardsConfession</c>, gated on this
        /// same latch plus crime &gt; 0), which resets crime itself via a large negative
        /// StatModifications entry relying on the stat's own floor clamp — no C# write needed.
        /// </summary>
        private static void CheckAllDown(bool allDown)
        {
            float latch = HiddenStat.Get(AllDownLatchStatUid);
            if (latch < 0f) return;

            if (allDown && latch < 0.5f)
            {
                HiddenStat.Set(AllDownLatchStatUid, 1f);
                Plugin.Logger.LogInfo("[GuardOutcomePatch] The entire Town Watch is down — the player can now confess to the Inn Keeper to clear their name.");
            }
            else if (!allDown && latch >= 0.5f)
            {
                HiddenStat.Set(AllDownLatchStatUid, 0f);
            }
        }

        /// <summary>
        /// +1 to <see cref="GuardKillsPendingStatUid"/>, called from both kill-detection sites the
        /// moment a kill is confirmed. The stat's own <c>MinMaxValue.y</c> (4, one per guard) clamps
        /// the read, so no explicit cap is needed here. <see cref="JailPatch"/> resets it to 0 once
        /// the sentence it drove has been served in full.
        /// </summary>
        private static void IncrementGuardKillsPending(string label)
        {
            float current = HiddenStat.Get(GuardKillsPendingStatUid);
            if (current < 0f)
            {
                Plugin.Logger.LogDebug(
                    $"[GuardOutcomePatch] '{GuardKillsPendingStatUid}' not readable — {label}'s kill will not " +
                    "add to the jail sentence formula (falls back to the crime-based formula instead).");
                return;
            }
            HiddenStat.Set(GuardKillsPendingStatUid, current + 1f);
        }

        // ── NPCStat access on a live guard ────────────────────────────────────────

        /// <summary>
        /// This guard's <c>cmcNpcStatGuardDowned</c>, or 0 when they have no live NPC. Returning 0
        /// for an absent guard is correct rather than convenient: the player-side day stat is what
        /// keeps a killed guard down, and it is already armed by the time their NPC disappears.
        /// </summary>
        private static float ReadDownedMarker(object gm, string agentUid)
        {
            var npc = FindLiveNpc(gm, agentUid);
            if (npc == null) return 0f;

            var stat = ResolveDownedStat();
            if (stat == null) return 0f;

            var getStatValue = npc.GetType().GetMethod("GetStatValue",
                BindingFlags.Instance | BindingFlags.Public, null, new[] { stat.GetType() }, null);
            if (getStatValue == null)
            {
                Plugin.Logger.LogDebug("[GuardOutcomePatch] InGameNPC.GetStatValue(NPCStat) not found — downed markers unreadable.");
                return 0f;
            }
            return getStatValue.Invoke(npc, new[] { stat }) is float f ? f : 0f;
        }

        /// <summary>
        /// Zeroes the marker on the live NPC. Same <c>InGameNPCStat.SetStatValueFromEditor</c>
        /// write AnimalLifecycleTicker uses for its own lifecycle stats. A no-op when the guard has
        /// no live NPC — they will come back through GuardSpawnPatch with a fresh stat set once
        /// suppression lifts.
        /// </summary>
        private static void ClearDownedMarker(object gm, string agentUid)
        {
            var npc = FindLiveNpc(gm, agentUid);
            if (npc == null) return;

            var stat = ResolveDownedStat();
            if (stat == null) return;

            if (Reflect.GetMember(npc, "NPCStatsDict") is not IDictionary statsDict) return;
            if (!statsDict.Contains(stat)) return;
            var inGameStat = statsDict[stat];
            if (inGameStat == null) return;

            var setter = inGameStat.GetType().GetMethod("SetStatValueFromEditor",
                BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(float) }, null);
            if (setter == null)
            {
                Plugin.Logger.LogWarning("[GuardOutcomePatch] InGameNPCStat.SetStatValueFromEditor not found — downed markers cannot be cleared.");
                return;
            }
            setter.Invoke(inGameStat, new object[] { 0f });
        }

        private static object ResolveDownedStat()
        {
            if (_guardDownedStat != null) return _guardDownedStat;
            if (_getFromIdMethod == null)
            {
                var uidType = CardUtil.FindGameType("UniqueIDScriptable");
                if (uidType == null) return null;
                _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
                if (_getFromIdMethod == null) return null;
            }

            _guardDownedStat = _getFromIdMethod.Invoke(null, new object[] { GuardDutyPatch.GuardDownedStatUid });
            if (_guardDownedStat == null && !_loggedDownedStatMissing)
            {
                _loggedDownedStatMissing = true;
                Plugin.Logger.LogWarning(
                    $"[GuardOutcomePatch] NPCStat '{GuardDutyPatch.GuardDownedStatUid}' not found — " +
                    "guards will never be marked down and the gauntlet cannot clear Village Crime.");
            }
            return _guardDownedStat;
        }

        private static object FindLiveNpc(object gm, string agentUid)
        {
            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs) return null;
            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                var uid = CardUtil.GetCardUniqueId(Reflect.GetMember(npc, "NPCModel"));
                if (string.Equals(uid, agentUid, StringComparison.OrdinalIgnoreCase)) return npc;
            }
            return null;
        }
    }
}
