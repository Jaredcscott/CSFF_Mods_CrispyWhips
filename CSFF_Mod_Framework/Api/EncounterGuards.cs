using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>Where an encounter start came from, as the guard layer sees it (framework 2.28.0).</summary>
public enum EncounterStartSource
{
    /// <summary>
    /// A start with no NPC attached (<c>EncounterPopup.StartEncounter</c> with a null <c>InGameNPC</c>):
    /// a drop that names no <c>EncounterNPC</c>, an explore event, a generated tame-fail attack. Every
    /// guard sees these.
    /// </summary>
    NoNpc = 0,

    /// <summary>An agent's own <c>AgentAction</c> attack (<c>NPCAction.ToAction</c>, the agent tick).</summary>
    AgentAction = 1,

    /// <summary>An agent duty's encounter step (<c>StartEncounterDutyAction</c>, e.g. a generated Aggression attack).</summary>
    AgentDuty = 2,
}

/// <summary>
/// Context handed to encounter guard predicates. A player-chosen fight (Approach, a dialog's
/// "Attack" answer, Give Chase) is never evaluated. An agent's own attack is evaluated only by
/// guards registered with <c>includeAgentStarts: true</c> (root CLAUDE.md "Encounter System Patching").
/// </summary>
public sealed class EncounterContext
{
    /// <summary>The Encounter ScriptableObject being started.</summary>
    public object Encounter { get; internal set; }

    /// <summary>UniqueID of the encounter, or null if it could not be read.</summary>
    public string EncounterUid { get; internal set; }

    /// <summary>
    /// NPCAgent UniqueID of the agent starting the fight (<see cref="EncounterStartSource.AgentAction"/>
    /// and <see cref="EncounterStartSource.AgentDuty"/>); null for <see cref="EncounterStartSource.NoNpc"/>.
    /// </summary>
    public string AgentUid { get; internal set; }

    /// <summary>Which start path is asking. Predicates registered without agent starts only ever see <c>NoNpc</c>.</summary>
    public EncounterStartSource Source { get; internal set; }
}

/// <summary>
/// Framework-owned encounter suppression (Centralization Tier 2, P9; agent starts since 2.28.0).
/// Mods register predicates instead of patching <c>EncounterPopup.StartEncounter</c> themselves
/// (replaces Sirus EncounterGuardPatch and the same shape in every future ward/companion mod).
///
/// <para>Three start paths, one snapshot array, one evaluator:</para>
/// <list type="bullet">
/// <item><c>NoNpc</c>: <c>Patching.EncounterGuardPatch</c>, the one <c>StartEncounter</c> prefix,
/// for a start whose <c>InGameNPC</c> is null. Every guard is evaluated. A save that resumes a fight
/// in progress (<c>CurrentSaveData.HasEncounterData</c>) is never cancelled.</item>
/// <item><c>AgentAction</c> / <c>AgentDuty</c>: <c>Patching.AgentEncounterGuardPatch</c>, at the
/// agent's own source (<c>NPCAction.ToAction</c>, <c>StartEncounterDutyAction.CanBePerformed</c>).
/// Only guards registered with <c>includeAgentStarts: true</c> are evaluated.</item>
/// <item>A start with an NPC attached that did NOT come from the agent's tick (Approach, a dialog
/// fight, a chase) is player-chosen and is never evaluated by anything.</item>
/// </list>
/// On EA 0.68b wildlife are agents, so an agent's bear or wolf attack arrives at <c>StartEncounter</c>
/// WITH its NPC, exactly like the player's Approach; the start source, not the NPC, is what tells
/// them apart, which is why agent starts are filtered at the source.
///
/// <para>Declarative alternative: ship <c>EncounterGuards/*.json</c> in the mod folder; see
/// <c>Loading.EncounterGuardLoader</c> for the schema and for which JSON guards reach agent attacks
/// (only one naming the agent in <c>AgentUids</c> or the encounter in a non-empty <c>EncounterUids</c>).</para>
///
/// <code>
/// // Null-NPC starts only (unchanged since 1.x):
/// var handle = EncounterGuards.Register("WolfGuard", ctx => MyWolfIsInPlayerEnv());
/// // Also an agent's own attack: the predicate must decide by ctx.AgentUid / ctx.EncounterUid.
/// var h2 = EncounterGuards.Register("WardGuard",
///     ctx => ctx.AgentUid == "myAgentUid" &amp;&amp; MyWardIsInPlayerEnv(), includeAgentStarts: true);
/// EncounterGuards.Unregister(handle);
/// </code>
/// <para>A percent predicate on an agent attack is asked once per (agent, encounter, tick): a
/// repeating attack rolls again on each tick it fires, a once-per-enrage attack is spent either way.</para>
/// </summary>
public static class EncounterGuards
{
    /// <summary>Opaque registration token returned by <see cref="Register(string, Func{EncounterContext, bool})"/>.</summary>
    public sealed class GuardHandle
    {
        internal string Name;
        internal Func<EncounterContext, bool> ShouldSuppress;
        internal bool IncludeAgentStarts;
    }

    private static readonly List<GuardHandle> _guards = new();
    private static GuardHandle[] _snapshot = Array.Empty<GuardHandle>();
    private static int _agentStartCount;

    /// <summary>Number of registered guards (API + JSON).</summary>
    public static int Count => _snapshot.Length;

    /// <summary>Number of registered guards that also see agent starts (zero-cost check for the agent hooks).</summary>
    public static int AgentStartCount => _agentStartCount;

    /// <summary>
    /// Registers a guard for null-NPC starts only (an agent's own attack and every player-chosen
    /// fight are never shown to it). The predicate returns true to suppress the encounter.
    /// Predicates run on the main thread; exceptions are caught and logged per guard.
    /// </summary>
    public static GuardHandle Register(string name, Func<EncounterContext, bool> shouldSuppress)
        => Register(name, shouldSuppress, false);

    /// <summary>
    /// Registers a guard. With <paramref name="includeAgentStarts"/> true it is also asked about an
    /// agent's own attack (<see cref="EncounterContext.Source"/> <c>AgentAction</c> or <c>AgentDuty</c>,
    /// <see cref="EncounterContext.AgentUid"/> set), so it must decide by the agent or encounter UID:
    /// a predicate that returns true for everything would stop every predator and bandit in range.
    /// </summary>
    public static GuardHandle Register(string name, Func<EncounterContext, bool> shouldSuppress, bool includeAgentStarts)
    {
        if (shouldSuppress == null) return null;
        var handle = new GuardHandle
        {
            Name = string.IsNullOrEmpty(name) ? "Guard" : name,
            ShouldSuppress = shouldSuppress,
            IncludeAgentStarts = includeAgentStarts,
        };
        lock (_guards)
        {
            _guards.Add(handle);
            Rebuild();
        }
        Log.Debug($"[EncounterGuards] registered guard '{handle.Name}'{(includeAgentStarts ? " (includes agent starts)" : "")} ({_snapshot.Length} total)");
        return handle;
    }

    /// <summary>Removes a guard registered with either <c>Register</c> overload.</summary>
    public static void Unregister(GuardHandle handle)
    {
        if (handle == null) return;
        lock (_guards)
        {
            if (_guards.Remove(handle))
                Rebuild();
        }
    }

    private static void Rebuild()
    {
        var snap = _guards.ToArray();
        int agent = 0;
        foreach (var g in snap) if (g.IncludeAgentStarts) agent++;
        _snapshot = snap;
        _agentStartCount = agent;
    }

    // ── Framework internals ──────────────────────────────────────────────────

    private static int _suppressedFrame = -1;

    /// <summary>
    /// True when a guard suppressed a null-NPC encounter during the current rendered frame.
    /// Consumed by <c>WildlifeRaidPatch</c>'s postfix (Harmony still runs postfixes after a
    /// prefix skips the original) so a suppressed bear encounter does not trigger a bear raid.
    /// An agent attack suppressed at its source never reaches <c>StartEncounter</c>, so it does
    /// not set this.
    /// </summary>
    internal static bool WasSuppressedThisFrame => _suppressedFrame == Time.frameCount;

    /// <summary>Null-NPC start (the <c>StartEncounter</c> prefix). True when any guard suppresses it.</summary>
    internal static bool EvaluateWildlife(object encounter)
    {
        if (_snapshot.Length == 0) return false;
        return Evaluate(new EncounterContext
        {
            Encounter = encounter,
            EncounterUid = CardUtil.GetCardUniqueId(encounter),
            Source = EncounterStartSource.NoNpc,
        });
    }

    /// <summary>
    /// The one evaluator. A <c>NoNpc</c> context asks every guard; an agent context asks only guards
    /// registered with agent starts. Returns true when any guard suppresses.
    /// </summary>
    internal static bool Evaluate(EncounterContext ctx)
    {
        var snapshot = _snapshot;
        if (snapshot.Length == 0 || ctx == null) return false;
        bool agentStart = ctx.Source != EncounterStartSource.NoNpc;

        foreach (var g in snapshot)
        {
            if (agentStart && !g.IncludeAgentStarts) continue;
            try
            {
                if (!g.ShouldSuppress(ctx)) continue;
                if (!agentStart) _suppressedFrame = Time.frameCount;
                Log.Debug($"[EncounterGuards] '{g.Name}' suppressed encounter '{ctx.EncounterUid ?? "?"}' ({ctx.Source}{(ctx.AgentUid != null ? ", agent " + ctx.AgentUid : "")}).");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"[EncounterGuards] guard '{g.Name}' threw: {Log.ExceptionText(ex)}");
            }
        }
        return false;
    }
}
