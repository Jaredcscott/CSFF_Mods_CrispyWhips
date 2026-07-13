using CSFFModFramework.Data;
using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// The spawn-registration gap-filler: gets framework/mod NPCAgents into
/// `WorldSettings.NPCAgents` so `GameManager.Awake` creates a live InGameNPC for them
/// (saved runs route through LoadNPC with the same registry entry).
///
/// <para>Mechanism: a compile-time Harmony postfix on `GameManager.InitializeStatsAndActions`
/// appends queued agents to `GameManager.CurrentGamemode.World.NPCAgents` — gamemode-correct,
/// pre-`CreateNPC`/`CreateDutiesDict`, and re-applied idempotently every run start. StartingEnv
/// is always Env_SpiritWorld; the agent's own lifecycle actions move it on-stage (vanilla
/// registry pattern: 28 of 29 entries start in the Spirit World).</para>
///
/// <para>Why `InitializeStatsAndActions` and not an `Awake` prefix: `CurrentGamemode` is assigned
/// PARTWAY THROUGH `GameManager.Awake` (decomp: from `CurrentGameData.CurrentGamemode`), and the
/// `World.NPCAgents` consumption loop runs LATER in that same Awake. `InitializeStatsAndActions()`
/// is the game's own call on the statement immediately before that loop — so a postfix on it runs
/// after `CurrentGamemode.World` is populated and before the game consumes it. An Awake *prefix*
/// runs before the gamemode is even resolved (CurrentGamemode == null → skip). OnGMInitialized is
/// far too late — consumption already happened inside Awake (research §2.6).</para>
///
/// <para>String-based `SafePatcher.TryPatch("GameManager", ...)` is documented to fail
/// (ModCore ships a shadowing GameManager type); the compile-time `typeof(GameManager)`
/// reference is the proven form (root CLAUDE.md §Harmony Patching Pitfalls).</para>
/// </summary>
internal static class SpawnRegistrar
{
    /// <summary>Env_SpiritWorld CT4 — the off-stage parking environment.</summary>
    public const string SpiritWorldUid = "db69b1711fb0190419ce0c5466591772";

    private static readonly List<NPCAgent> _pending = new();
    private static bool _patched;

    public static void Queue(NPCAgent agent)
    {
        if (agent != null && !_pending.Contains(agent)) _pending.Add(agent);
    }

    public static void ApplyPatch(Harmony harmony)
    {
        if (_patched) return;
        try
        {
            var target = AccessTools.Method(typeof(GameManager), "InitializeStatsAndActions");
            if (target == null)
            {
                Log.Error("Animals: GameManager.InitializeStatsAndActions not found — spawn registration unavailable");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(SpawnRegistrar), nameof(InitStatsPostfix)));
            _patched = true;
            Log.Debug("Animals: GameManager.InitializeStatsAndActions spawn-registration postfix armed");
        }
        catch (Exception ex)
        {
            Log.Error($"Animals: failed to patch GameManager.InitializeStatsAndActions: {Log.ExceptionText(ex)}");
        }
    }

    private static void InitStatsPostfix()
    {
        try { InjectIntoCurrentWorld(); }
        catch (Exception ex) { Log.Error($"Animals: spawn registration failed: {Log.ExceptionText(ex)}"); }
    }

    private static void InjectIntoCurrentWorld()
    {
        if (_pending.Count == 0) return;

        var world = GameManager.CurrentGamemode ? GameManager.CurrentGamemode.World : null;
        if (world == null)
        {
            Log.Warn("Animals: InitializeStatsAndActions ran with no CurrentGamemode.World — spawn registration skipped this run");
            return;
        }

        var spiritWorld = GameRegistry.GetByUid<CardData>(SpiritWorldUid);
        if (spiritWorld == null)
        {
            Log.Error($"Animals: Env_SpiritWorld ({SpiritWorldUid}) not in registry — spawn registration skipped");
            return;
        }

        var entries = world.NPCAgents ?? Array.Empty<NPCAgentSpawnSettings>();
        int appended = 0;
        foreach (var agent in _pending)
        {
            if (entries.Any(e => ReferenceEquals(e.SpawnedAgent, agent))) continue;

            var grown = new NPCAgentSpawnSettings[entries.Length + 1];
            Array.Copy(entries, grown, entries.Length);
            grown[entries.Length] = new NPCAgentSpawnSettings { SpawnedAgent = agent, StartingEnv = spiritWorld };
            entries = grown;
            appended++;
        }

        if (appended > 0)
        {
            world.NPCAgents = entries;
            Log.Info($"Animals: registered {appended} agent(s) in WorldSettings '{world.name}' ({entries.Length} total) — GameManager.Awake will create them");
        }
    }
}
