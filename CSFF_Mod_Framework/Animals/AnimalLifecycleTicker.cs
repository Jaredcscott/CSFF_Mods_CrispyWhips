using CSFFModFramework.Api;
using CSFFModFramework.Injection;
using CSFFModFramework.Loading;
using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// Per-species DTP-tick lifecycle service — the framework absorption of what
/// Sirus's WildOwlLifecyclePatch proved out in mod C# (2026-07-11..13 debugging cycle):
///
/// <list type="bullet">
/// <item><b>Window relocation</b> — at the active→inactive hour transition (and at run start,
/// if the game loads outside the window with the agent on stage) the agent is moved to its
/// roost via a direct, synchronous <c>GameManager.MoveNPC</c> call. Stat-writes-and-wait leave
/// the board card interactive for several player actions (the Attempt-12 interactivity gap);
/// MoveNPC from a tick/GMInit context is safe — the move-before-drop reentrancy race only
/// exists for stat writes issued INSIDE GameManager's own ActionRoutine stat block.</item>
/// <item><b>Kill-respawn timer</b> — while blood sits in the dead band the respawn NPCStat
/// counts DTP ticks; at <c>Spawn.DeathRespawnTicks</c> blood is healed to max so the agent's
/// duty gates (exists==1, blood above dead band) make it eligible to reappear. The death
/// action itself never touches the exists stat — duty gating plus this timer replace the
/// "dies repeatedly on arrival" loop.</item>
/// <item><b>Suppressed respawn</b> — while <c>Spawn.SuppressWhileCardOnBoard</c> (e.g. a tamed
/// companion) is on the board, the wild agent is kept retired (exists forced to 0, with an
/// immediate relocate as the safety net for a missed post-tame retirement). Once the card is
/// gone, a second timer counts to <c>Spawn.SuppressedRespawnTicks</c>, then restores exists
/// and heals blood — a fresh wild individual.</item>
/// <item><b>Spawn conditions</b> (2.27.0): while any <c>Spawn.Conditions</c> entry is false the
/// agent is suppressed the same way, parked in the Spirit World. When nothing suppresses it any
/// more it is restored after <c>Spawn.ConditionRespawnTicks</c> (0 = the first tick), or after
/// <c>SuppressedRespawnTicks</c> for a species that also has a suppressor card. Conditions are
/// re-read every tick and never latched, so a stat a mod writes after run start is picked up
/// on the next tick.</item>
/// </list>
///
/// <para>Timers persist across save/load for free because they live in the agent's own
/// NPCStats (saved in NPCStatSaveData), not in framework memory.</para>
/// </summary>
internal static class AnimalLifecycleTicker
{
    internal sealed class SpeciesLifecycle
    {
        public string SpeciesId;
        public NPCAgent Agent;
        public DutyBuilder.SpeciesStats Stats;
        public bool HasActivityWindow;
        public int ActiveStart, ActiveEnd;
        public CardData RoostEnv;                  // SpiritWorld card when roost is off-stage
        public int DeathRespawnTicks;              // 0 = kill-respawn timer disabled
        public string SuppressCardUid;             // null = suppression disabled
        public int SuppressedRespawnTicks;
        /// <summary>Spawn.Conditions; null = none. All must hold for the agent to be in the world.</summary>
        public List<WorldMapLoader.GateConditionDefinition> Conditions;
        public int ConditionRespawnTicks;
        /// <summary>Where a condition retirement parks the agent: the Spirit World, never an
        /// on-stage roost, so a retired animal is off every board.</summary>
        public CardData SpiritWorldEnv;
        /// <summary>M4: the vanilla AgentTrapType stat, set only for species that opted into
        /// traps. Non-null enables the per-tick stamp clear below.</summary>
        public NPCStat TrapTypeStat;

        // Per-run state (reset at OnGMInitialized)
        internal InGameNPC Npc;
        internal bool WasActiveWindow;
        internal bool WasDead;
    }

    private static readonly List<SpeciesLifecycle> _species = new();
    private static bool _subscribed;

    public static void Register(SpeciesLifecycle lifecycle)
    {
        if (lifecycle?.Agent == null) return;
        _species.RemoveAll(s => s.SpeciesId == lifecycle.SpeciesId);
        _species.Add(lifecycle);

        if (_subscribed) return;
        TickEvents.DtpTick += OnTick;
        GameManager.OnGMInitialized += OnGmInitialized;
        _subscribed = true;
        Log.Debug("Animals: lifecycle ticker subscribed (DtpTick + OnGMInitialized)");
    }

    public static void Clear() => _species.Clear();

    // ------------------------------------------------------------ run start ---

    private static void OnGmInitialized()
    {
        foreach (var s in _species)
        {
            try
            {
                // Clear cached per-run state — a stale InGameNPC from a previous run makes
                // every stat read/write silently target the dead object (the exact stale-cache
                // bug WildOwlLifecyclePatch documented).
                s.Npc = null;
                s.WasDead = false;
                s.WasActiveWindow = false;

                if (!TryResolveNpc(s)) continue;

                // Suppression takes precedence: a companion already on the board at run start
                // (e.g. granted by a perk), or a spawn condition that is false, means no wild
                // individual should be on stage. A condition that turns true later is picked up
                // by the tick; nothing here is latched.
                if (SuppressionCause(s) is { } cause)
                {
                    if (TryGetStat(s, s.Stats.Exists, out var existsStat) && existsStat.CurrentValue >= 1f)
                    {
                        existsStat.SetStatValueFromEditor(0f);
                        RetireOffStage(s, cause);
                        Log.Debug($"Animals: {s.SpeciesId}: retired at run start (exists -> 0): {Describe(s, cause)}");
                    }
                    continue;
                }

                if (!s.HasActivityWindow) continue;
                if (TryGetStat(s, s.Stats.Exists, out var exists) && exists.CurrentValue < 1f) continue;
                if (TryGetStat(s, s.Stats.Blood, out var blood) && blood.CurrentValue <= DutyBuilder.SpeciesStats.DeadBloodMax) continue;

                bool active = InActiveWindow(s);
                s.WasActiveWindow = active;
                if (!active && Relocate(s))
                    Log.Debug($"Animals: {s.SpeciesId}: loaded outside active hours — relocated to roost");
            }
            catch (Exception ex)
            {
                Log.Warn($"Animals: {s.SpeciesId}: run-start reconciliation failed: {Log.ExceptionText(ex)}");
            }
        }
    }

    // ----------------------------------------------------------------- tick ---

    private static void OnTick()
    {
        foreach (var s in _species)
        {
            try
            {
                if (!TryResolveNpc(s)) continue;
                TickWindowRelocation(s);
                TickKillRespawn(s);
                TickSuppressedRespawn(s);
                TickTrapTypeReset(s);
            }
            catch (Exception ex)
            {
                Log.Warn($"Animals: {s.SpeciesId}: lifecycle tick failed: {Log.ExceptionText(ex)}");
            }
        }
    }

    private static void TickWindowRelocation(SpeciesLifecycle s)
    {
        if (!s.HasActivityWindow) return;
        if (!TryGetStat(s, s.Stats.Exists, out var exists) || exists.CurrentValue < 1f) { s.WasActiveWindow = false; return; }
        if (TryGetStat(s, s.Stats.Blood, out var blood) && blood.CurrentValue <= DutyBuilder.SpeciesStats.DeadBloodMax)
        {
            s.WasActiveWindow = false;
            return;
        }

        bool active = InActiveWindow(s);
        if (active) { s.WasActiveWindow = true; return; }

        // Inactive hour; only relocate on the transition out of the window (not every tick).
        if (!s.WasActiveWindow) return;
        s.WasActiveWindow = false;
        if (Relocate(s))
            Log.Debug($"Animals: {s.SpeciesId}: active window closed — relocated to roost");
    }

    private static void TickKillRespawn(SpeciesLifecycle s)
    {
        if (s.DeathRespawnTicks <= 0) return;
        if (!TryGetStat(s, s.Stats.Blood, out var blood)) return;
        if (!TryGetStat(s, s.Stats.RespawnTimer, out var timer)) return;

        bool dead = blood.CurrentValue <= DutyBuilder.SpeciesStats.DeadBloodMax;
        if (!dead)
        {
            if (s.WasDead) Log.Debug($"Animals: {s.SpeciesId}: blood healthy — kill-respawn timer cleared");
            s.WasDead = false;
            if (timer.CurrentValue != 0f) timer.SetStatValueFromEditor(0f);
            return;
        }

        if (!s.WasDead)
        {
            s.WasDead = true;
            timer.SetStatValueFromEditor(0f);
            Log.Debug($"Animals: {s.SpeciesId}: blood depleted — {s.DeathRespawnTicks}-tick respawn timer started");
        }

        float elapsed = timer.CurrentValue + 1f;
        if (elapsed >= s.DeathRespawnTicks)
        {
            HealToMax(blood);
            timer.SetStatValueFromEditor(0f);
            s.WasDead = false;
            Log.Debug($"Animals: {s.SpeciesId}: respawn timer elapsed — healed, eligible to reappear");
        }
        else
        {
            timer.SetStatValueFromEditor(elapsed);
            Log.Debug($"Animals: {s.SpeciesId}: kill-respawn timer {elapsed:F0}/{s.DeathRespawnTicks}");
        }
    }

    /// <summary>M4: clears a leftover <c>AgentTrapType</c> stamp.
    ///
    /// <para>The stamp lands on EVERY trap spring, including one whose catch roll failed — and a
    /// failed roll produces no catch action, so nothing clears it. Left alone it accumulates
    /// (20 + 20 = 40), and 40 is a DIFFERENT trap's type code, so the next successful catch would
    /// select the wrong trap's catch card entirely.</para>
    ///
    /// <para>This deliberately lives on the framework tick rather than in a generated
    /// <c>Repeat</c> agent action. An agent action is eligible in the engine's pre-request
    /// <c>CheckForActions</c> sweep on the very tick a trap springs, and would zero the stamp
    /// before the catch action reads it — the race documented in
    /// <see cref="TrapIntegrator"/>. The framework tick runs outside that sweep, so it cannot
    /// race the catch; the successful-catch path clears its own stamp inline.</para></summary>
    private static void TickTrapTypeReset(SpeciesLifecycle s)
    {
        if (s.TrapTypeStat == null) return;
        if (!TryGetStat(s, s.TrapTypeStat, out var stamp)) return;
        if (stamp.CurrentValue <= 0f) return;

        Log.Debug($"Animals: {s.SpeciesId}: clearing leftover AgentTrapType stamp {stamp.CurrentValue:F0} "
                + "(trap sprang without a successful catch)");
        stamp.SetStatValueFromEditor(0f);
    }

    private static void TickSuppressedRespawn(SpeciesLifecycle s)
    {
        if (s.SuppressCardUid == null && s.Conditions == null) return;
        if (!TryGetStat(s, s.Stats.Exists, out var exists))
        {
            WarnOnce(s, "exists", "its Exists stat has no live instance on the agent; suppression and spawn conditions are not applied");
            return;
        }
        // The timer is only needed to count a restore delay, so its absence no longer skips the
        // retire side (before 2.27.0 a missing timer instance skipped the force-retire too).
        TryGetStat(s, s.Stats.SuppressRespawnTimer, out var timer);

        var cause = SuppressionCause(s);

        if (exists.CurrentValue >= 1f || cause != null)
        {
            // Retire: a suppressor card on board (the safety net for a missed post-tame
            // retirement: stale cache, predicate miss) or a spawn condition gone false.
            if (exists.CurrentValue >= 1f && cause != null)
            {
                Log.Debug($"Animals: {s.SpeciesId}: retired (exists -> 0): {Describe(s, cause)}");
                exists.SetStatValueFromEditor(0f);
                RetireOffStage(s, cause);
            }
            if (timer != null && timer.CurrentValue != 0f) timer.SetStatValueFromEditor(0f);
            return;
        }

        // Retired and nothing suppresses it any more. A species with a suppressor card keeps its
        // one delay (0 = never respawns, as shipped); a conditions-only species uses
        // ConditionRespawnTicks, where 0 means now.
        bool bySuppressor = s.SuppressCardUid != null;
        int delay = bySuppressor ? s.SuppressedRespawnTicks : s.ConditionRespawnTicks;
        if (bySuppressor && delay <= 0) return;
        if (timer == null && delay > 0)
        {
            // A suppressor species keeps its shipped fault behaviour (stays retired); a
            // conditions-only one comes back without its delay, the lesser harm.
            WarnOnce(s, "timer", "its restore-delay timer stat has no live instance on the agent; "
                + (bySuppressor ? "it stays retired" : "restoring without the delay"));
            if (bySuppressor) return;
            delay = 0;
        }
        if (delay <= 0)
        {
            Restore(s, exists, timer, "its spawn conditions hold again");
            return;
        }

        if (timer.CurrentValue <= 0f)
            Log.Debug($"Animals: {s.SpeciesId}: {(bySuppressor ? "suppressor card gone" : "spawn conditions hold again")}: {delay}-tick respawn timer started");

        float elapsed = timer.CurrentValue + 1f;
        if (elapsed >= delay)
        {
            Restore(s, exists, timer, bySuppressor ? "suppressed-respawn timer elapsed" : "condition-respawn timer elapsed");
        }
        else
        {
            timer.SetStatValueFromEditor(elapsed);
            Log.Debug($"Animals: {s.SpeciesId}: {(bySuppressor ? "suppressed" : "condition")}-respawn timer {elapsed:F0}/{delay}");
        }
    }

    private static void Restore(SpeciesLifecycle s, InGameNPCStat exists, InGameNPCStat timer, string why)
    {
        exists.SetStatValueFromEditor(1f);
        if (TryGetStat(s, s.Stats.Blood, out var blood)) HealToMax(blood);
        if (timer != null) timer.SetStatValueFromEditor(0f);
        Log.Debug($"Animals: {s.SpeciesId}: restored (exists -> 1, blood healed): {why}; a fresh wild agent is eligible to appear");
    }

    // ------------------------------------------------------ suppression cause ---

    /// <summary>What keeps the wild agent retired right now: -1 for the suppressor card, the index
    /// of the first false Spawn.Conditions entry, or null when nothing does. An index rather than
    /// a string, so a species that stays retired for a whole season allocates nothing per tick.</summary>
    private static int? SuppressionCause(SpeciesLifecycle s)
    {
        if (s.SuppressCardUid != null && CardOnBoard(s.SuppressCardUid)) return -1;
        if (s.Conditions == null) return null;
        for (int i = 0; i < s.Conditions.Count; i++)
            if (!ConnectionGateService.EvalSingleGateCond(s.Conditions[i])) return i;
        return null;
    }

    private static string Describe(SpeciesLifecycle s, int? cause)
    {
        if (cause is not { } i) return "nothing suppresses it";
        if (i < 0) return $"suppressor card '{s.SuppressCardUid}' is on the board";
        var c = s.Conditions[i];
        string detail = c.Type?.Trim().ToLowerInvariant() switch
        {
            "statthreshold"    => $"StatThreshold '{c.UID}' {c.Comparison} {c.Value} is false",
            "season"           => $"Season '{c.UID}' is false (current season '{GameQuery.CurrentSeason ?? "unknown"}')",
            "improvementbuilt" => $"ImprovementBuilt '{c.UID}' in '{c.EnvUID}' is false",
            _                  => $"{c.Type} '{c.UID}' is false",
        };
        return $"Spawn.Conditions[{i}] {detail}";
    }

    /// <summary>A suppressor retirement keeps its shipped destination (the roost); a condition
    /// retirement goes to the Spirit World, so the animal is off every board.</summary>
    private static void RetireOffStage(SpeciesLifecycle s, int? cause)
    {
        if (cause is >= 0 && s.SpiritWorldEnv != null && s.Npc != null)
        {
            var gm = MBSingleton<GameManager>.Instance;
            if (gm != null) { gm.MoveNPC(s.Npc, new EnvID(s.SpiritWorldEnv, false)); return; }
        }
        Relocate(s);
    }

    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    private static void WarnOnce(SpeciesLifecycle s, string cause, string problem)
    {
        if (_warned.Add(s.SpeciesId + ":" + cause))
            Log.Warn($"Animals: {s.SpeciesId}: {problem}");
    }

    // -------------------------------------------------------------- helpers ---

    private static bool TryResolveNpc(SpeciesLifecycle s)
    {
        if (s.Npc != null) return true;
        var gm = MBSingleton<GameManager>.Instance;
        if (gm?.AllNPCs == null) return false;
        s.Npc = gm.AllNPCs.FirstOrDefault(n => n != null && n.NPCModel == s.Agent);
        if (s.Npc != null) Log.Debug($"Animals: {s.SpeciesId}: resolved live InGameNPC");
        return s.Npc != null;
    }

    /// <summary>Fetches the live per-NPC stat instance through <see cref="StatAccess.ResolveNpc(InGameNPC, NPCStat)"/>
    /// (2.28.0; it used to read the private NPCStatsDict by reflection). A null stat (the species
    /// declares none) or no resolved NPC yet is the normal "no" and stays silent here.
    /// InGameNPCStat's CurrentValue/SetStatValueFromEditor/MinMaxValue members are public. (EA 0.66:
    /// the direct-float SetStatValue overload became a private IEnumerator taking an extra
    /// NPCStatModifierTypes argument; SetStatValueFromEditor is the public fire-and-forget wrapper
    /// that starts that same coroutine with modifier type Permanent - despite the name, it carries no
    /// editor-only behavior, confirmed by decompile.)</summary>
    private static bool TryGetStat(SpeciesLifecycle s, NPCStat stat, out InGameNPCStat instance)
    {
        instance = null;
        if (stat == null || s.Npc == null) return false;
        instance = StatAccess.ResolveNpc(s.Npc, stat);
        return instance != null;
    }

    private static void HealToMax(InGameNPCStat stat)
    {
        float max = stat.MinMaxValue.y;
        stat.SetStatValueFromEditor(max > 0f ? max : 100f);
    }

    // ActiveStart/ActiveEnd are CLOCK hours: the same two numbers are handed to the engine as
    // InGameTimeCondition hour windows (AnimalAssetFactory.MakeHourWindow, DutyBuilder), which
    // compare against GameManager.HourOfTheDayValue. GameQuery.HourOfDay returns that clock hour
    // since the 2026-09-17 D6 fix; before it this window ran DayStartingHour (4) hours behind the
    // species' own duties, so the roost relocation fired at 10:00 for an owl whose duties end at 6.
    private static bool InActiveWindow(SpeciesLifecycle s)
    {
        int hour = (int)GameQuery.HourOfDay;
        return s.ActiveStart > s.ActiveEnd
            ? hour >= s.ActiveStart || hour < s.ActiveEnd     // wrap-around (e.g. 20 → 6)
            : hour >= s.ActiveStart && hour < s.ActiveEnd;
    }

    private static bool CardOnBoard(string cardUid)
    {
        var gm = MBSingleton<GameManager>.Instance;
        if (gm?.AllCards == null) return false;
        foreach (var card in gm.AllCards)
        {
            if (card == null || card.CardModel == null) continue;
            if (card.CardModel.UniqueID == cardUid) return true;
        }
        return false;
    }

    private static bool Relocate(SpeciesLifecycle s)
    {
        if (s.RoostEnv == null || s.Npc == null) return false;
        var gm = MBSingleton<GameManager>.Instance;
        if (gm == null) return false;
        gm.MoveNPC(s.Npc, new EnvID(s.RoostEnv, false));
        return true;
    }
}
