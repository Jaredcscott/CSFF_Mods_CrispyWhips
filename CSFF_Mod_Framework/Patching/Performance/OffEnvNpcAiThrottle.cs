using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;

namespace CSFFModFramework.Patching.Performance;

// InGameNPC.UpdateTick runs for every agent on every game tick, in up to three steps, and
// each step runs AI selection (CheckForDuties -> SelectDuty) before its duty block. Measured
// 2026-10-10 on a 41-agent year-4 save (plan section 15, Measurements): agents OUTSIDE the
// player's environment account for about a third of a tick's wall time, and none of that is
// visible to the player. This patch halves the selection part for those agents.
//
// What it skips: the AI-selection iterator `InGameNPC.CheckForDuties(TickSteps)`, on every
// second tick, for an agent that is outside the player's environment. What it never skips:
//   * the stat pass (it runs before CheckForDuties, at EarlyAfterDurabilities),
//   * the duty block, CheckForActions and FindCardToEquip (duty waits and give-up counters are
//     tick-counted there, and agent actions drive other mods' restock),
//   * the EarlyBeforeDurabilities step (skipping its selection stalls that step's duty execution),
//   * a companion (`AlliedWithPlayer`): a follower is off-env right after every travel until its
//     move duty runs, and Community Mod Chest relocates partners on a poll,
//   * any tick while the game is catching up an environment or in an environment transition.
// SelectDuty has a second, UI caller (NPCInspectionPopup), so it is not patched.
//
// The patch is on the compiler-generated MoveNext of CheckForDuties, not on the iterator
// builder: UpdateTick's own MoveNext is recompiled by Harmony whenever anything else patches
// it, and Mono inlines a tiny builder into that copy, so a builder patch applied later would be
// skipped silently (CLAUDE.md, "Harmony Patching Pitfalls"). MoveNext is reached only through
// interface dispatch and is never inlined. Applied after every other patch for the same reason.
//
// Default off: with the key off nothing is patched. The cadence is per agent: its own counter
// turns over at the EarlyAfterDurabilities step and the Late step of the same tick reuses that
// verdict, so an agent either selects on both steps of a tick or on neither.
internal static class OffEnvNpcAiThrottle
{
    internal static bool Enabled;
    internal static long Seen;
    internal static long Skipped;

    // [0] = counter turned over at EarlyAfterDurabilities, [1] = 1 when this tick's selection is skipped.
    // Read through GetValue with a callback, never GetOrCreateValue: that builds the value with a
    // parameterless constructor, which int[] does not have, so it threw MissingMethodException on
    // every call and the fail-open catch below ran vanilla selection for every agent (T2.1249,
    // 2026-10-11: skipped=0/0 across about 100 live ticks with the key on).
    private static readonly ConditionalWeakTable<InGameNPC, int[]> _cadence = new();
    private static readonly ConditionalWeakTable<InGameNPC, int[]>.CreateValueCallback _newCadence = _ => new int[2];
    private static bool _warnedPrefixFailure;

    private static FieldInfo _fState;
    private static FieldInfo _fThis;
    private static FieldInfo _fStep;
    private static PropertyInfo _pCatchingUp;
    private static PropertyInfo _pTransition;

    public static void Configure(ConfigFile config, Harmony harmony)
    {
        var cfg = config.Bind(
            "Performance", "OffEnvNpcAiThrottle", false,
            "Run AI selection for NPC agents outside your current environment on every second game tick "
            + "instead of every tick. Their stats, duty execution and actions still run every tick, and "
            + "companions, catch-up ticks and environment transitions are never throttled; an agent can "
            + "start its next duty one tick (15 in-game minutes) later. Measured: agents elsewhere cost "
            + "about a third of a tick's time on a late-game save with 41 agents. Default false. A change "
            + "needs a full quit to desktop and relaunch, because this file is read once at startup.");
        Enabled = cfg.Value;
        if (!Enabled)
        {
            return;
        }

        try
        {
            var original = AccessTools.Method(typeof(InGameNPC), "CheckForDuties", new[] { typeof(TickSteps) });
            var moveNext = original == null ? null : AccessTools.EnumeratorMoveNext(original);
            if (moveNext == null)
            {
                Util.Log.Warn("OffEnvNpcAiThrottle: InGameNPC.CheckForDuties (or its MoveNext) not found - throttle not installed.");
                Enabled = false;
                return;
            }

            var sm = moveNext.DeclaringType;
            _fState = AccessTools.Field(sm, "<>1__state");
            _fThis = AccessTools.Field(sm, "<>4__this");
            _fStep = AccessTools.Field(sm, "_TickStep");
            _pCatchingUp = typeof(GameManager).GetProperty("IsCatchingUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            // A property (`public bool EnvironmentTransition { get; private set; }`), like IsCatchingUp:
            // a GetField lookup returned null and the transition guard never applied.
            _pTransition = typeof(GameManager).GetProperty("EnvironmentTransition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_fState == null || _fThis == null || _fStep == null || _pCatchingUp == null || _pTransition == null)
            {
                Util.Log.Warn($"OffEnvNpcAiThrottle: CheckForDuties state machine shape changed (state={_fState != null}, this={_fThis != null}, step={_fStep != null}, IsCatchingUp={_pCatchingUp != null}, EnvironmentTransition={_pTransition != null}) - throttle not installed.");
                Enabled = false;
                return;
            }

            harmony.Patch(moveNext, prefix: new HarmonyMethod(AccessTools.Method(typeof(OffEnvNpcAiThrottle), nameof(MoveNext_Prefix))));
            Util.Log.Info("OffEnvNpcAiThrottle: on - off-environment agents run AI selection every second tick.");
        }
        catch (Exception ex)
        {
            Enabled = false;
            Util.Log.Warn($"OffEnvNpcAiThrottle: patch setup failed, throttle not installed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    // MoveNext of the CheckForDuties iterator. Only the first call (state 0) can skip; a skipped
    // call ends the iterator exactly as its own `yield break` would (state -2, no more items).
    static bool MoveNext_Prefix(object __instance, ref bool __result)
    {
        try
        {
            if ((int)_fState.GetValue(__instance) != 0)
            {
                return true;
            }

            var step = (TickSteps)_fStep.GetValue(__instance);
            if (step != TickSteps.EarlyAfterDurabilities && step != TickSteps.Late)
            {
                return true;
            }

            var npc = _fThis.GetValue(__instance) as InGameNPC;
            var gm = MBSingleton<GameManager>.Instance;
            if (npc == null || gm == null || npc.NPCModel == null)
            {
                return true;
            }

            if (npc.NPCModel.AlliedWithPlayer || npc.CurrentEnvironment.MatchesPlayerEnv)
            {
                return true;
            }

            if ((bool)_pCatchingUp.GetValue(gm) || (bool)_pTransition.GetValue(gm))
            {
                return true;
            }

            var c = _cadence.GetValue(npc, _newCadence);
            if (step == TickSteps.EarlyAfterDurabilities)
            {
                c[0]++;
                c[1] = (c[0] & 1) == 1 ? 1 : 0;
            }

            Seen++;
            if (c[1] == 0)
            {
                return true;
            }

            Skipped++;
            _fState.SetValue(__instance, -2);
            __result = false;
            return false;
        }
        catch (Exception ex)
        {
            // Fail open: a throttle that cannot decide runs the vanilla selection. Warn once: a failure
            // that repeats on every call turns the whole option into a silent no-op (it did, until the
            // cadence lookup above was fixed).
            if (!_warnedPrefixFailure)
            {
                _warnedPrefixFailure = true;
                Util.Log.Warn($"OffEnvNpcAiThrottle: prefix failed, running vanilla selection (warned once): {ex}");
            }
            else
            {
                Util.Log.Debug($"OffEnvNpcAiThrottle: prefix failed, running vanilla selection: {ex.Message}");
            }
            return true;
        }
    }
}
