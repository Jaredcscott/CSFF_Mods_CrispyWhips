using CSFFModFramework.Api;
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

        // Per-run state (reset at OnGMInitialized)
        internal InGameNPC Npc;
        internal bool WasActiveWindow;
        internal bool WasDead;
    }

    private static readonly List<SpeciesLifecycle> _species = new();
    private static bool _subscribed;
    private static FieldInfo _statsDictField;

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
                // (e.g. granted by a perk) means no wild individual should be on stage.
                if (s.SuppressCardUid != null && CardOnBoard(s.SuppressCardUid))
                {
                    if (TryGetStat(s, s.Stats.Exists, out var existsStat) && existsStat.CurrentValue >= 1f)
                    {
                        existsStat.SetStatValue(0f);
                        Relocate(s);
                        Log.Info($"Animals: {s.SpeciesId}: suppressor card on board at run start — wild spawn suppressed until it departs");
                    }
                    continue;
                }

                if (!s.HasActivityWindow) continue;
                if (TryGetStat(s, s.Stats.Exists, out var exists) && exists.CurrentValue < 1f) continue;
                if (TryGetStat(s, s.Stats.Blood, out var blood) && blood.CurrentValue <= DutyBuilder.SpeciesStats.DeadBloodMax) continue;

                bool active = InActiveWindow(s);
                s.WasActiveWindow = active;
                if (!active && Relocate(s))
                    Log.Info($"Animals: {s.SpeciesId}: loaded outside active hours — relocated to roost");
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
            Log.Info($"Animals: {s.SpeciesId}: active window closed — relocated to roost");
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
            if (timer.CurrentValue != 0f) timer.SetStatValue(0f);
            return;
        }

        if (!s.WasDead)
        {
            s.WasDead = true;
            timer.SetStatValue(0f);
            Log.Info($"Animals: {s.SpeciesId}: blood depleted — {s.DeathRespawnTicks}-tick respawn timer started");
        }

        float elapsed = timer.CurrentValue + 1f;
        if (elapsed >= s.DeathRespawnTicks)
        {
            HealToMax(blood);
            timer.SetStatValue(0f);
            s.WasDead = false;
            Log.Info($"Animals: {s.SpeciesId}: respawn timer elapsed — healed, eligible to reappear");
        }
        else
        {
            timer.SetStatValue(elapsed);
            Log.Debug($"Animals: {s.SpeciesId}: kill-respawn timer {elapsed:F0}/{s.DeathRespawnTicks}");
        }
    }

    private static void TickSuppressedRespawn(SpeciesLifecycle s)
    {
        if (s.SuppressCardUid == null) return;
        if (!TryGetStat(s, s.Stats.Exists, out var exists)) return;
        if (!TryGetStat(s, s.Stats.SuppressRespawnTimer, out var timer)) return;

        bool suppressorPresent = CardOnBoard(s.SuppressCardUid);

        if (exists.CurrentValue >= 1f || suppressorPresent)
        {
            // Safety net: suppressor on board while the wild agent still exists — a missed
            // post-tame retirement (stale cache, predicate miss). Force-retire now.
            if (exists.CurrentValue >= 1f && suppressorPresent)
            {
                Log.Info($"Animals: {s.SpeciesId}: wild agent still exists while suppressor card is on board — force-retiring");
                exists.SetStatValue(0f);
                Relocate(s);
            }
            if (timer.CurrentValue != 0f) timer.SetStatValue(0f);
            return;
        }

        if (s.SuppressedRespawnTicks <= 0) return;

        if (timer.CurrentValue <= 0f)
            Log.Info($"Animals: {s.SpeciesId}: suppressor card gone — {s.SuppressedRespawnTicks}-tick respawn timer started");

        float elapsed = timer.CurrentValue + 1f;
        if (elapsed >= s.SuppressedRespawnTicks)
        {
            exists.SetStatValue(1f);
            if (TryGetStat(s, s.Stats.Blood, out var blood)) HealToMax(blood);
            timer.SetStatValue(0f);
            Log.Info($"Animals: {s.SpeciesId}: suppressed-respawn timer elapsed — a fresh wild agent is eligible to appear");
        }
        else
        {
            timer.SetStatValue(elapsed);
            Log.Debug($"Animals: {s.SpeciesId}: suppressed-respawn timer {elapsed:F0}/{s.SuppressedRespawnTicks}");
        }
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

    /// <summary>Fetches the live per-NPC stat instance. NPCStatsDict is a private field on
    /// InGameNPC; InGameNPCStat's CurrentValue/SetStatValue/MinMaxValue members are public.</summary>
    private static bool TryGetStat(SpeciesLifecycle s, NPCStat stat, out InGameNPCStat instance)
    {
        instance = null;
        if (stat == null || s.Npc == null) return false;
        _statsDictField ??= AccessTools.Field(typeof(InGameNPC), "NPCStatsDict");
        if (_statsDictField?.GetValue(s.Npc) is not Dictionary<NPCStat, InGameNPCStat> dict) return false;
        return dict.TryGetValue(stat, out instance) && instance != null;
    }

    private static void HealToMax(InGameNPCStat stat)
    {
        float max = stat.MinMaxValue.y;
        stat.SetStatValue(max > 0f ? max : 100f);
    }

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
