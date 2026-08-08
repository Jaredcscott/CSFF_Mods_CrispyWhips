using System;
using System.Collections;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The Village Jail — sentencing, warden-absence rolls, and the release-blocking ration/
    /// starvation safety net (Village_Master_Plan.md §10.8.7.2/§10.8.7.4, Risk R9,
    /// Prompt 7 deliverables 3+5 and Prompt 8 in full).
    ///
    /// <para><b>Sentence length — resolved, not the plan's raw placeholder.</b> Both
    /// <c>cmcStatJailSentenceRemaining</c> and <c>cmcStatJailSentenceOriginal</c> were already
    /// authored (a prior session) with <c>MinMaxValue.y: 8.0</c> — matching the jail cell door's
    /// own 8 pre-built "N more days" DAs. Confirmed via decompile
    /// (<c>InGameStat.SimpleCurrentValue</c> → <c>CurrentValue()</c> →
    /// <c>Mathf.Clamp(num, CurrentMinMaxValue.x, CurrentMinMaxValue.y)</c>, .decomp/InGameStat.cs:62-180)
    /// that EVERY read of a GameStat through the standard accessor clamps to its own
    /// <c>MinMaxValue</c> — so writing the plan's raw placeholder (crime/8, up to 12.5 at
    /// crime's own 100-point ceiling) would silently read back as 8 anyway. This class clamps
    /// explicitly in C# rather than relying on that implicit clamp, so the written and read
    /// values always agree. <b>Effective, enforced worst-case sentence: 8 days (768 DTP ticks).</b>
    /// </para>
    ///
    /// <para><b>The ration/starvation math (Risk R9) — verified against real vanilla GameStat
    /// JSON this session, not estimated:</b></para>
    /// <list type="bullet">
    /// <item>Weight (<see cref="WeightStatUid"/>) decays via a FIXED per-band self-RateModifier
    /// (-12/tick near the floor up to -23/tick at Severely Obese; one tick = one DTP = 15 game
    /// minutes, 96 ticks/day) — confirmed via <c>Weight.json</c>'s own <c>Statuses[].EffectsOnStats</c>.
    /// <b>Contrary to the plan's assumption, Satiation's low tiers do NOT modify Weight's rate</b> —
    /// grepped both <c>Satiation.json</c> and <c>Thirst.json</c> for Weight's GUID: zero hits, and
    /// no C# in <c>.decomp/</c> references any of the three GUIDs either (this whole subsystem is
    /// declarative). The real link is Appetite (<c>42aefae2...</c>): Weight's low tiers raise
    /// Appetite (+50/+100/+150), and Appetite drains Satiation (-1 to -4/tick) — Satiation feeds
    /// back onto Appetite's own rate, never onto Weight directly.</item>
    /// <item><b>A second, more urgent GameOver floor the plan's own GUID list did not name:</b>
    /// <see cref="HydrationStatUid"/> ("Hydration", distinct from "Thirst") has its own
    /// <c>"Dead of Thirst"</c> status at value 0, <c>GameOver: true</c>. It decays at a flat
    /// <c>BaseRatePerTick: -1.0</c> (-96/day) from a 192 max — zero water kills in 2 days, far
    /// faster than starvation. "Thirst" (the GUID the plan named) has NO GameOver tier at all;
    /// its low end only blocks actions for a few ticks. Both are covered by the ration and the
    /// safety net below.</item>
    /// <item>Digestion pipeline (verified via <c>Calories_Stomach.json</c> +
    /// <c>Calories_Intestines.json</c>, for context/cross-check even though the shipped ration
    /// item bypasses it — see next bullet): eating adds Calories_Stomach (0-200), which digests
    /// 1:1 into Calories_Intestines (0-1000), which converts into Weight at a rate that scales
    /// with its own fill tier (20/tick at 1-8 up to 160/tick at 169-1000) while self-draining at a
    /// matching tier (-1 to -8/tick) — the ratio of Weight gained to Intestines drained is a
    /// CONSTANT 20:1 at every tier.</item>
    /// <item><b>Worst case shown with real numbers</b> (8 days = 768 ticks, ration granted once/day).
    /// The shipped ration is <c>cmcJailRationFood</c> (built separately this session — an instant,
    /// unbuffered +2,500 Weight and +35 Satiation on Eat, bypassing the digestion pipeline above
    /// entirely) plus <c>cmcJailWaterJug</c> (+150 Hydration, +150 Thirst on Drink, this class's
    /// own addition — nothing covered Hydration/Thirst before it, a real gap given Hydration's
    /// GameOver floor). Weight: -18/tick (fastest rate a non-crisis "Normal Weight" prisoner can
    /// have) × 768 = -13,824 passive over the full sentence; ration gives +2,500/day × 8 = +20,000
    /// — net +6,176 (Weight case is not just safe, it can't even decline under this ration; any
    /// prisoner not already starving at arrest is nowhere near the 293,999 floor). Hydration:
    /// -96/day × 8 = -768 passive; ration gives +150/day × 8 = +1,200; net +432 (flat to slightly
    /// up). Satiation: -~192/day (baseline Appetite) × 8 = -1,536 passive; ration gives +35/day ×
    /// 8 = +280; net -1,256 — Satiation will run low for most of a longer sentence (no GameOver
    /// risk; Satiation has none), which is exactly the "meager, doesn't let you thrive" feel, and
    /// is covered further by the safety net's own Satiation branch. Thirst: -192/day × 8 = -1,536;
    /// ration +150/day × 8 = +1,200; net -336, comfort/action-block only (no GameOver tier).</item>
    /// <item><b>What this math does NOT cover, and why the safety net is load-bearing rather than
    /// decorative:</b> Weight's RateModifier stacks additively with unrelated conditions this class
    /// never models — Hypothermia (-8/tick), high Fever (up to -7.5/tick), Parasites (up to
    /// -16/tick) all subtract further from the same stat. A cold, feverish, parasite-ridden
    /// prisoner could see decay well beyond the -18/tick baseline used above. The reactive safety
    /// net in <see cref="RunSafetyNet"/> monitors the PLAYER'S ACTUAL Weight/Hydration values, not
    /// a static projection, and fires on real numbers regardless of cause — this is the actual
    /// guarantee against R9, not the ration sizing alone.</item>
    /// </list>
    ///
    /// <para>Everything here piggybacks a single new <c>TickEvents.DtpTick</c> subscription (fires
    /// once per 15 in-game minutes, only while the player is actively spending DTP — exactly when
    /// "does a day/hour pass" questions are meaningful) plus the existing <c>DayRollover</c> event,
    /// per the same doctrine as every other CMC schedule patch.</para>
    /// </summary>
    internal static class JailPatch
    {
        // ── Vanilla stat GUIDs (Documentation/CSFF_Reference.md does not yet carry these —
        // add on next reference sweep) ──────────────────────────────────────────
        private const string WeightStatUid = "16af37a364285d14586629e3e0700e55";
        private const string SatiationStatUid = "930cf914322e9f145af1315d96f85a28";
        private const string HydrationStatUid = "95ca7c21ffad5e647acc3d9cb5bfcde6";

        // ── This mod's Jail GameStats ────────────────────────────────────────────
        internal const string SentenceRemainingStatUid = "cmcStatJailSentenceRemaining";
        internal const string SentenceOriginalStatUid = "cmcStatJailSentenceOriginal";
        internal const string UnguardedStatUid = "cmcStatJailUnguarded";
        private const string RationDayStatUid = "cmcStatJailRationDay";

        private const string JailCellEnvUid = "cmcEnvJailCell";
        private const string RationFoodUid = "cmcJailRationFood";
        private const string WaterJugUid = "cmcJailWaterJug";

        // ── Sentencing (§10.8.7.2) ───────────────────────────────────────────────
        private const float CrimeDivisor = 8f;

        /// <summary>Mirrors the stat's own authored MinMaxValue.y (both
        /// <see cref="SentenceRemainingStatUid"/> and <see cref="SentenceOriginalStatUid"/>) —
        /// matches the jail cell door's 8 pre-built "N more days" DAs. Consumed by
        /// <see cref="JailEscapePatch"/>'s recapture penalty clamp.</summary>
        internal const int MaxSentenceDays = 8;

        /// <summary>Crime points paid down per day served — matches CrimeDivisor so a full
        /// sentence pays off approximately the crime score that produced it; the exact remainder
        /// is swept by the guaranteed ClearCrime on the day the sentence reaches 0.</summary>
        private const float DailyCrimePaydown = CrimeDivisor;

        // ── Warden-absence window (§10.8.8.2) ────────────────────────────────────
        /// <summary>Placeholder per-hour probability the whole warden rotation steps off post —
        /// unconfirmed by the owner (open question 11); build to this number, flagged tunable.</summary>
        private const float WardenGapRollChance = 0.08f;
        private const int WardenGapWindowHours = 2;
        private static readonly System.Random _rng = new System.Random();
        private static int _lastSeenHour = -1;

        // ── Ration safety-net thresholds (Risk R9) ───────────────────────────────
        private const float WeightSafetyThreshold = 350000f;   // deep inside "Severely Underweight" (343000-360499) — ~56,000 above the 293,999 GameOver floor
        private const float WeightEmergencyTopUp = 5000f;
        private const float HydrationSafetyThreshold = 80f;    // inside "Dehydrated" (61-95) — 80 units above the 0 GameOver floor
        private const float HydrationEmergencyTopUp = 100f;
        private const float SatiationSafetyThreshold = 50f;    // "Very Hungry" — no GameOver risk, comfort-only
        private const float SatiationEmergencyTopUp = 100f;

        private static bool _weightBelowThreshold;
        private static bool _hydrationBelowThreshold;
        private static bool _satiationBelowThreshold;

        private static readonly string[] GuardAgentUids =
        {
            "cmcGuardSterlingAgent", "cmcGuardThorneAgent", "cmcGuardCorrinAgent", "cmcGuardVaneAgent",
        };

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            GuardOutcomePatch.ArrestPendingRaised += OnArrestPendingRaised;
            TickEvents.DayRollover += OnDayRollover;
            TickEvents.DtpTick += OnDtpTick;
            Plugin.Logger.LogDebug("[JailPatch] initialized.");
        }

        // ── Sentencing ────────────────────────────────────────────────────────────

        private static void OnArrestPendingRaised()
        {
            try
            {
                float crime = VillageCrimePatch.CurrentCrime();
                int days = Mathf.Clamp(Mathf.RoundToInt(crime / CrimeDivisor), 1, MaxSentenceDays);

                bool ok = HiddenStat.Set(SentenceRemainingStatUid, days);
                ok &= HiddenStat.Set(SentenceOriginalStatUid, days);
                if (!ok)
                {
                    Plugin.Logger.LogWarning(
                        $"[JailPatch] Arrest processed but the sentence stat(s) could not be written " +
                        $"(crime {crime:0} -> {days}d) — the cell's Exit DA may never unlock. Not clearing " +
                        "ArrestPending so a later poll can retry.");
                    return;
                }

                GuardOutcomePatch.ClearArrestPending();
                Plugin.Logger.LogInfo($"[JailPatch] Sentenced: crime {crime:0} -> {days} day(s) in the Village Jail.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[JailPatch] OnArrestPendingRaised failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void OnDayRollover()
        {
            try
            {
                float remaining = HiddenStat.Get(SentenceRemainingStatUid);
                if (remaining < 0f || remaining <= 0f) return; // unreadable or not serving

                float next = Math.Max(0f, remaining - 1f);
                HiddenStat.Set(SentenceRemainingStatUid, next);

                if (next <= 0f)
                {
                    VillageCrimePatch.ClearCrime("jail sentence served in full");
                    Plugin.Logger.LogInfo("[JailPatch] Sentence complete — crime cleared, cell Exit unlocked.");
                }
                else
                {
                    VillageCrimePatch.ReduceCrime(DailyCrimePaydown, "a day served in the Village Jail");
                    Plugin.Logger.LogInfo($"[JailPatch] Sentence: {next:0} day(s) remaining.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[JailPatch] OnDayRollover failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Per-DTP-tick work: warden-absence roll, jail-unguarded poll, ration + safety net ──

        private static void OnDtpTick()
        {
            RollWardenGap();
            UpdateUnguarded();
            RunRationAndSafetyNet();
        }

        private static void RollWardenGap()
        {
            try
            {
                int hour = Mathf.FloorToInt(GameQuery.HourOfDay);
                if (hour == _lastSeenHour) return;
                _lastSeenHour = hour;

                float gap = HiddenStat.Get(GuardDutyPatch.WardenGapStatUid);
                if (gap < 0f) return; // stat not ready yet

                if (gap > 0.5f)
                {
                    HiddenStat.Set(GuardDutyPatch.WardenGapStatUid, Math.Max(0f, gap - 1f));
                    return;
                }

                if (_rng.NextDouble() < WardenGapRollChance)
                {
                    HiddenStat.Set(GuardDutyPatch.WardenGapStatUid, WardenGapWindowHours);
                    Plugin.Logger.LogInfo($"[JailPatch] Warden-absence window opened for {WardenGapWindowHours}h.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[JailPatch] RollWardenGap failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// Writes <c>cmcStatJailUnguarded</c> from each guard's LIVE
        /// <c>InGameNPC.CurrentPerformingDuty</c> (the field that reflects what a guard is
        /// actually doing right now, unlike <c>NPCDutyRef.IsActive</c> — see
        /// <see cref="GuardDutyPatch"/>'s class doc) compared against
        /// <see cref="GuardDutyPatch.WardenDuties"/>. No consumer exists yet (Prompt 9's Hidden
        /// Tunnel Dig DA is unbuilt), but the stat is already shipped and described as functional
        /// in its own GameStat JSON — leaving it unwritten would be exactly the "advertised dead
        /// code" the root CLAUDE.md's Feature Honesty rule flags.
        /// </summary>
        private static void UpdateUnguarded()
        {
            try
            {
                if (GuardDutyPatch.WardenDuties.Count == 0) return; // duties not built yet
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;
                if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs) return;

                bool anyOnPost = false;
                foreach (var agentUid in GuardAgentUids)
                {
                    if (!GuardDutyPatch.WardenDuties.TryGetValue(agentUid, out var wardenDuty) || wardenDuty == null)
                        continue;

                    object npc = FindLiveNpc(allNpcs, agentUid);
                    if (npc == null) continue;

                    var performing = Reflect.GetMember(npc, "CurrentPerformingDuty");
                    if (ReferenceEquals(performing, wardenDuty))
                    {
                        anyOnPost = true;
                        break;
                    }
                }
                HiddenStat.Set(UnguardedStatUid, anyOnPost ? 0f : 1f);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[JailPatch] UpdateUnguarded failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static object FindLiveNpc(IEnumerable allNpcs, string agentUid)
        {
            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                var uid = CardUtil.GetCardUniqueId(Reflect.GetMember(npc, "NPCModel"));
                if (string.Equals(uid, agentUid, StringComparison.OrdinalIgnoreCase)) return npc;
            }
            return null;
        }

        // ── Ration Tray (§10.8.7.4) + safety net (Risk R9) ───────────────────────

        private static void RunRationAndSafetyNet()
        {
            try
            {
                float remaining = HiddenStat.Get(SentenceRemainingStatUid);
                if (remaining < 0f || remaining <= 0f) return; // unreadable or not serving

                GrantDailyRationIfDue();

                if (Plugin.EnableJailSafetyNet == null || Plugin.EnableJailSafetyNet.Value)
                    RunSafetyNet();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[JailPatch] RunRationAndSafetyNet failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void GrantDailyRationIfDue()
        {
            if (GameQuery.CurrentEnvironmentUniqueId != JailCellEnvUid) return;

            int today = GameQuery.CurrentDay;
            float stored = HiddenStat.Get(RationDayStatUid);
            if (stored < 0f) return; // stat not ready

            int storedDay = stored < 0.5f ? -1 : (int)Math.Round(stored) - 1; // +1-offset "0 = never" idiom
            if (storedDay == today) return; // already given today

            SpawnService.Spawn(RationFoodUid);
            SpawnService.Spawn(WaterJugUid);
            HiddenStat.Set(RationDayStatUid, today + 1);
            Plugin.Logger.LogInfo("[JailPatch] Today's ration placed in the cell.");
        }

        /// <summary>
        /// Reactive circuit breaker, disableable via <c>Plugin.EnableJailSafetyNet</c> for the
        /// acceptance test that must prove the BASELINE ration alone is safe. Monitors the
        /// player's actual Weight/Hydration/Satiation, not a projection — see class doc for why
        /// this, not the ration sizing, is the real guarantee against Risk R9.
        /// </summary>
        private static void RunSafetyNet()
        {
            CheckThreshold(WeightStatUid, WeightSafetyThreshold, WeightEmergencyTopUp,
                ref _weightBelowThreshold, "Weight");
            CheckThreshold(HydrationStatUid, HydrationSafetyThreshold, HydrationEmergencyTopUp,
                ref _hydrationBelowThreshold, "Hydration");
            CheckThreshold(SatiationStatUid, SatiationSafetyThreshold, SatiationEmergencyTopUp,
                ref _satiationBelowThreshold, "Satiation");
        }

        private static void CheckThreshold(string statUid, float threshold, float topUp, ref bool wasBelow, string label)
        {
            float current = HiddenStat.Get(statUid);
            if (current < 0f) return; // unreadable

            bool isBelow = current <= threshold;
            if (isBelow && !wasBelow)
            {
                HiddenStat.Set(statUid, current + topUp);
                Plugin.Logger.LogInfo(
                    $"[JailPatch] SAFETY NET: {label} at {current:0} (<= {threshold:0}) — emergency top-up " +
                    $"+{topUp:0} applied.");
            }
            wasBelow = isBelow;
        }
    }
}
