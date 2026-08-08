using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

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
    /// <item><b>The aggregate pardon.</b> All four down at once resets Village Crime to 0 — the
    /// plan's Clean-tier default, since open question 2 (a "pardoned but known" tier) is
    /// unresolved. Latched through <c>cmcStatGuardsAllDown</c> so it fires once per gauntlet
    /// rather than every poll, which would otherwise erase any new crime committed while the
    /// Watch is still down.</item>
    /// <item><b>Respawn suppression.</b> <see cref="IsRespawnSuppressed"/> keeps
    /// <c>GuardSpawnPatch</c> from FIRST-PLACING a guard who is serving out a season.</item>
    /// </list>
    ///
    /// <para><b>What "down" currently means, stated plainly:</b> nothing in the shipped content
    /// removes a guard from the world. Neither guard Encounter's <c>EnemyDefeatedEffects</c>
    /// destroys a card or deletes an NPC, so even the "killed" result leaves the guard standing on
    /// the board with their marker set — the marker, not their absence, is what takes them out of
    /// the chase. The season timer therefore does real work (it is what un-downs them) but the
    /// plan's "re-spawn the guard at its home patrol node" branch has no trigger yet. Suppression
    /// is wired to first placement only, and deliberately NOT to <c>GuardSpawnPatch</c>'s restore
    /// path: skipping a restore would make a merely-routed guard vanish on the next reload. If a
    /// later chunk adds real guard removal, this is the hook it should extend.</para>
    ///
    /// <para>Season length is <c>GameManager.DaysPerMoon</c> (30). <c>DaysPerStar</c> is 120
    /// (.decomp/GameManager.cs:116-118), so a year is exactly four moons and one moon is one
    /// season; there is no separate season-length constant in the game or in
    /// <see cref="VillageClock"/>, which only derives 7-day weeks.</para>
    /// </summary>
    internal static class GuardOutcomePatch
    {
        /// <summary>
        /// The four guards. MUST stay in sync with <c>GuardDutyPatch.Beats</c>,
        /// <c>GuardSpawnPatch.Guards</c> and <c>GuardWitnessPatch.Guards</c> — a guard missing here
        /// never arms a timer, and (worse) can never satisfy the all-four-down pardon, silently
        /// stranding the player at Banished forever.
        /// </summary>
        private static readonly (string AgentUid, string Label, string DownDayStatUid)[] Guards =
        {
            ("cmcGuardSterlingAgent", "Captain Reeve Sterling", "cmcStatGuardDownDaySterling"),
            ("cmcGuardThorneAgent",  "Guard Nella Thorne",    "cmcStatGuardDownDayThorne"),
            ("cmcGuardCorrinAgent",  "Guard Old Corrin",      "cmcStatGuardDownDayCorrin"),
            ("cmcGuardVaneAgent",    "Guard Iris Vane",       "cmcStatGuardDownDayVane"),
        };

        private const string AllDownLatchStatUid = "cmcStatGuardsAllDown";

        /// <summary>Set to 1 by every guard Encounter's <c>PlayerDemoralizedEffects</c>.
        /// <see cref="JailPatch"/> (§10.8.7.2) consumes it; see <see cref="ArrestPending"/>.</summary>
        internal const string ArrestPendingStatUid = "cmcStatArrestPending";

        /// <summary>Fallback if <c>GameManager.DaysPerMoon</c> cannot be read — the shipped value.</summary>
        private const int DefaultSeasonDays = 30;

        private static bool _initialized;
        private static object _guardDownedStat;         // NPCStat SO, resolved once
        private static MethodInfo _getFromIdMethod;
        private static bool _lastArrestPending;
        private static bool _loggedDownedStatMissing;

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

        // ── Poll ──────────────────────────────────────────────────────────────────

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, Run, "GuardOutcome");
            Plugin.Logger.LogDebug("[GuardOutcomePatch] initialized.");
        }

        private static void Run()
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                CheckArrestPending();

                int seasonDays = Math.Max(1, GameQuery.DaysPerMoon > 0 ? GameQuery.DaysPerMoon : DefaultSeasonDays);
                int today = GameQuery.CurrentDay;

                bool allDown = true;
                foreach (var guard in Guards)
                {
                    float armed = HiddenStat.Get(guard.DownDayStatUid);
                    if (armed < 0f) { allDown = false; continue; } // stat not readable yet

                    if (armed < 0.5f)
                    {
                        allDown = false;
                        // Arm on the first poll that sees this guard's declarative marker set.
                        // +1 so 0 stays an unambiguous "not down" sentinel, the same offset
                        // VillageClock uses for its epoch day.
                        if (ReadDownedMarker(gm, guard.AgentUid) >= 0.5f
                            && HiddenStat.Set(guard.DownDayStatUid, today + 1))
                            Plugin.Logger.LogInfo(
                                $"[GuardOutcomePatch] {guard.Label} is down (day {today}) — back on duty in {seasonDays} days.");
                        continue;
                    }

                    if (today - ((int)Math.Round(armed) - 1) < seasonDays) continue; // still serving

                    ClearDownedMarker(gm, guard.AgentUid);
                    HiddenStat.Set(guard.DownDayStatUid, 0f);
                    allDown = false;
                    Plugin.Logger.LogInfo($"[GuardOutcomePatch] {guard.Label} has recovered and returns to the Watch.");
                }

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

        private static void CheckAllDown(bool allDown)
        {
            float latch = HiddenStat.Get(AllDownLatchStatUid);
            if (latch < 0f) return;

            if (allDown && latch < 0.5f)
            {
                HiddenStat.Set(AllDownLatchStatUid, 1f);
                VillageCrimePatch.ClearCrime("the player fought through the entire Town Watch");
            }
            else if (!allDown && latch >= 0.5f)
            {
                HiddenStat.Set(AllDownLatchStatUid, 0f);
            }
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
