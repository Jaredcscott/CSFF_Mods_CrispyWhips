using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CSFFModFramework.Patching;

/// <summary>
/// Encounter guards reach the agent attacks they name (framework 2.28.0, plan section 1).
///
/// <para><strong>The gap.</strong> On EA 0.68b wildlife are agents. An agent's own attack drops its
/// encounter with <c>EncounterNPC</c> set (<c>NPCAction.ToAction</c> hands the action its
/// <c>ProducedCards</c> by reference; <c>StartEncounterDutyAction.StartDutyAction</c> builds a
/// collection with <c>EncounterNPC = _OnNPC.NPCModel</c>), so <c>EncounterPopup.StartEncounter</c>
/// receives a non-null <c>InGameNPC</c>, exactly as it does for the player's Approach button.
/// <see cref="EncounterGuardPatch"/> lets every non-null start through (a player-chosen fight must
/// never be cancelled), so before this no guard stopped any predator, including CMC's village guard
/// whose README promised it.</para>
///
/// <para><strong>The fix: filter at the agent's SOURCE, never at <c>StartEncounter</c>.</strong></para>
/// <list type="bullet">
/// <item>A postfix on <c>NPCAction.ToAction(InGameNPC, InGameCardBase)</c>: when a collection of the
/// returned action's <c>ProducedCards</c> drops an encounter a guard registered with agent starts
/// suppresses, the result gets a NEW array without that collection. The NPCAction's own drop array is
/// never written (the result aliases it, and it is shared vanilla data: root CLAUDE.md "Vanilla Data
/// Protection"); the element objects are reused because <c>ToAction</c> writes
/// <c>PotentialContainers</c> on them. W0-2 read the play path: an empty or shorter array completes
/// the action with no exception and no encounter (<c>SelectCardCollection</c> returns null, every
/// reader is length-guarded), and the action is spent exactly like a delivered one
/// (<c>CheckForActions</c> marks it played before <c>ToAction</c>).</item>
/// <item>A postfix on <c>StartEncounterDutyAction.CanBePerformed</c>: false for a suppressed duty
/// encounter, so a step-0 encounter duty (a generated <c>Aggression</c> attack) is left unselected and
/// retried, not spent. <see cref="Animals.AttackWaitsForPlayerAction"/> postfixes the same method; the
/// two only ever turn true into false, so their order does not matter.</item>
/// </list>
/// The player's Approach (<c>NPCInspectionPopup.DoInteraction</c>), a dialog fight (<c>DialogAction</c>)
/// and a chase never pass through either method, so they stay unguarded. W0-1 found no live
/// explore-event path that starts a guard-named encounter, so no named-start extension of the
/// <c>StartEncounter</c> layer ships.
///
/// <para><strong>Verdict cache.</strong> One verdict per (agent instance, encounter UID, game tick), so a
/// percent guard rolls once per attempt even though <c>CanBePerformed</c> is asked from duty selection,
/// the step run and the inspection UI. Cleared on every <c>GameManager.OnGMInitialized</c> (it re-fires
/// on every save-reload: root CLAUDE.md "Per-Run Event Registration"; subscribed once per process with
/// unsubscribe-before-subscribe and a latch).</para>
///
/// <para><strong>Inlining.</strong> Both targets are safe from the Mono inlining trap (root CLAUDE.md
/// "Harmony Patching Pitfalls"): <c>ToAction</c> is a large method with a loop, far past the JIT's inline
/// size, called from <c>InGameNPC.CheckForActions</c>; <c>CanBePerformed</c> is a virtual override
/// reached only through virtual dispatch. The probe counters below are read by <c>reflect</c> in the
/// harness legs to prove each postfix fires from the agent TICK path, not only from a direct call.</para>
///
/// <para>Logging: patch install at Debug (the load summary is the guard loader's one Info line), a
/// Debug line once per (agent, action) suppressed, and a warn-once per cause on every failure, after
/// which the vanilla result stands (an attack that fires beats one silently lost).</para>
/// </summary>
internal static class AgentEncounterGuardPatch
{
    /// <summary>How many of the two hooks installed (named in the guard loader's load summary).</summary>
    internal static int HooksInstalled;

    // Probe counters (read by reflect in the harness legs; never reset, so a reading taken across
    // ticks shows the tick path firing).
    internal static int ToActionSeen;
    internal static int ToActionSuppressed;
    internal static int DutyChecksSeen;
    internal static int DutySuppressed;

    private struct Verdict
    {
        public int Tick;
        public bool Suppress;
    }

    private static readonly Dictionary<string, Verdict> _verdicts = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _announced = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private static bool _runResetSubscribed;

    public static void ApplyPatch(Harmony harmony)
    {
        HooksInstalled = 0;
        try
        {
            var toAction = AccessTools.Method(typeof(NPCAction), "ToAction", new[] { typeof(InGameNPC), typeof(InGameCardBase) });
            if (toAction == null)
                Log.Warn("AgentEncounterGuardPatch: NPCAction.ToAction(InGameNPC, InGameCardBase) not found; encounter guards cannot stop an agent's AgentAction attack.");
            else
            {
                harmony.Patch(toAction, postfix: new HarmonyMethod(AccessTools.Method(typeof(AgentEncounterGuardPatch), nameof(ToAction_Postfix))));
                HooksInstalled++;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"AgentEncounterGuardPatch: NPCAction.ToAction patch failed: {Log.ExceptionText(ex)}");
        }

        try
        {
            var canBe = AccessTools.Method(typeof(StartEncounterDutyAction), "CanBePerformed");
            if (canBe == null)
                Log.Warn("AgentEncounterGuardPatch: StartEncounterDutyAction.CanBePerformed not found; encounter guards cannot stop an agent duty's attack.");
            else
            {
                harmony.Patch(canBe, postfix: new HarmonyMethod(AccessTools.Method(typeof(AgentEncounterGuardPatch), nameof(CanBePerformed_Postfix))));
                HooksInstalled++;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"AgentEncounterGuardPatch: StartEncounterDutyAction.CanBePerformed patch failed: {Log.ExceptionText(ex)}");
        }

        EnsureRunReset();
        Log.Debug($"AgentEncounterGuardPatch: {HooksInstalled}/2 agent encounter hooks installed.");
    }

    private static void EnsureRunReset()
    {
        if (_runResetSubscribed) return;
        try
        {
            GameManager.OnGMInitialized -= OnRunStart;
            GameManager.OnGMInitialized += OnRunStart;
            _runResetSubscribed = true;
        }
        catch (Exception ex)
        {
            WarnOnce("run-reset", $"AgentEncounterGuardPatch: could not subscribe the per-run verdict reset ({ex.GetType().Name}: {ex.Message}); verdicts still expire every tick.");
        }
    }

    private static void OnRunStart() => _verdicts.Clear();

    // __0 = InGameNPC _FromNPC (null for a non-agent use), __result = the CardAction about to be performed.
    private static void ToAction_Postfix(NPCAction __instance, InGameNPC __0, CardAction __result)
    {
        ToActionSeen++;
        if (EncounterGuards.AgentStartCount == 0) return;
        if (__result == null || __0 == null) return;
        try
        {
            var drops = __result.ProducedCards;
            if (drops == null || drops.Length == 0) return;

            List<CardsDropCollection> kept = null;
            for (int i = 0; i < drops.Length; i++)
            {
                var c = drops[i];
                bool suppress = c != null && c.DroppedEncounter != null
                    && IsSuppressed(__0, c.DroppedEncounter, EncounterStartSource.AgentAction);
                if (suppress)
                {
                    if (kept == null)
                    {
                        kept = new List<CardsDropCollection>(drops.Length);
                        for (int j = 0; j < i; j++) kept.Add(drops[j]);
                    }
                }
                else if (kept != null)
                {
                    kept.Add(c);
                }
            }
            if (kept == null) return;

            // A NEW array on the result only; the agent's own drop array is never written.
            __result.ProducedCards = kept.ToArray();
            ToActionSuppressed++;
            Announce(__0, __instance != null ? __instance.ActionID : "?", "action");
        }
        catch (Exception ex)
        {
            WarnOnce("toaction-" + Log.CauseKey(ex), $"AgentEncounterGuardPatch: agent action check failed, attack left as vanilla built it: {Log.ExceptionText(ex)}");
        }
    }

    // __0 = InGameNPC _FromNPC.
    private static void CanBePerformed_Postfix(StartEncounterDutyAction __instance, InGameNPC __0, ref bool __result)
    {
        DutyChecksSeen++;
        if (!__result || EncounterGuards.AgentStartCount == 0) return;
        if (__instance == null || __0 == null || __instance.DroppedEncounter == null) return;
        try
        {
            if (!IsSuppressed(__0, __instance.DroppedEncounter, EncounterStartSource.AgentDuty)) return;
            __result = false;
            DutySuppressed++;
            Announce(__0, __instance.name, "duty step");
        }
        catch (Exception ex)
        {
            WarnOnce("duty-" + Log.CauseKey(ex), $"AgentEncounterGuardPatch: agent duty check failed, vanilla answer kept: {Log.ExceptionText(ex)}");
        }
    }

    private static bool IsSuppressed(InGameNPC npc, Encounter encounter, EncounterStartSource source)
    {
        string agentUid = npc.NPCModel != null ? npc.NPCModel.UniqueID : null;
        string encUid = encounter.UniqueID;
        var gm = MBSingleton<GameManager>.Instance;
        int tick = gm != null ? gm.CurrentTickInfo.z : int.MinValue;
        string key = npc.GetInstanceID() + "|" + (encUid ?? encounter.name);

        if (gm != null && _verdicts.TryGetValue(key, out var cached) && cached.Tick == tick)
            return cached.Suppress;

        bool suppress = EncounterGuards.Evaluate(new EncounterContext
        {
            Encounter = encounter,
            EncounterUid = encUid,
            AgentUid = agentUid,
            Source = source,
        });
        if (gm != null) _verdicts[key] = new Verdict { Tick = tick, Suppress = suppress };
        return suppress;
    }

    private static void Announce(InGameNPC npc, string what, string kind)
    {
        string agent = npc.NPCModel != null ? npc.NPCModel.UniqueID : npc.name;
        if (!_announced.Add(agent + "|" + what)) return;
        Log.Debug($"AgentEncounterGuardPatch: suppressed agent {kind} '{what}' of '{agent}' (an encounter guard names it).");
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause)) Log.Warn(message);
    }
}
