using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CSFFModFramework.Patching.Diagnostics;

/// <summary>
/// Opt-in frame-time sampler (round-2 plan feature 15; the measurement P6, P7 and P9's framerate half
/// each need). While armed it logs ONE Info summary per 60 s of real time: frame count, mean, p95 and
/// worst frame, and frames over 33 ms, plus the CPU spent in <c>InGameNPC.UpdateTick</c> split into
/// agents in the player's environment and agents elsewhere (per <c>TickSteps</c> value), and the CPU
/// spent in <c>InGameNPC.SelectDuty</c> (AI selection alone), plus an agent's tick split into its
/// parts by exclusive CPU (see "NPC tick parts" below; added 2026-10-12 to find where off-environment
/// agent time goes).
///
/// "CPU" is the summed time inside MoveNext, the same reading <see cref="TrackingTimingDiagnostics"/>
/// uses: a child coroutine's later steps run in separate coroutines and are invisible to the wrapper.
///
/// Off by default, and patched only when armed, so a default boot installs nothing and logs nothing:
///   [Diagnostics] LogFrameTiming = true
/// The frame sampler is a static call from <c>Plugin.Update</c> that returns on one bool read when
/// unarmed. State the load condition (other sessions, TestHarness low-spec mode) beside every number
/// read from these lines: <c>TestHarness/Perf.cs</c> caps the frame rate when low-spec mode is on.
/// </summary>
internal static class FrameTimingDiagnostics
{
    private const double WindowSeconds = 60.0;
    private const float SlowFrameMs = 33f;
    private const int StepCount = 4; // TickSteps: EarlyAfterDurabilities, Late, All, EarlyBeforeDurabilities

    internal static bool Armed { get; private set; }

    // Frame sampler state (main thread only: Plugin.Update).
    private static float[] _frameMs = new float[4096];
    private static int _frameCount;
    private static double _windowStart = -1.0;

    // UpdateTick accumulators, indexed [step]; written from the coroutine wrapper on the main thread,
    // Interlocked anyway since the cost is negligible next to the Stopwatch.
    private static readonly long[] _inEnvTicks = new long[StepCount];
    private static readonly long[] _elsewhereTicks = new long[StepCount];
    private static readonly int[] _inEnvCalls = new int[StepCount];
    private static readonly int[] _elsewhereCalls = new int[StepCount];
    private static long _selectDutyTicks;
    private static int _selectDutyCalls;

    [ThreadStatic] private static long _selectDutyStart;

    // GameManager.ApplyRates accumulators, [0] = live ticks (_CatchupTick 0), [1] = catch-up ticks.
    // Wall = first MoveNext start to the iterator's end (includes frames it yields across); CPU = summed
    // MoveNext time. Feature 17 reads off-env UpdateTick CPU against the live tick's wall time.
    private static readonly long[] _applyWallTicks = new long[2];
    private static readonly long[] _applyCpuTicks = new long[2];
    private static readonly int[] _applyCalls = new int[2];

    public static void Configure(ConfigFile config, Harmony harmony)
    {
        var enabled = config.Bind("Diagnostics", "LogFrameTiming", false,
            "When true, logs one Info summary every 60 seconds with frame-time statistics (mean, p95, "
            + "worst frame, frames over 33 ms) and the CPU time spent in InGameNPC.UpdateTick, split "
            + "into agents in your environment and agents elsewhere, plus AI duty selection, and the wall "
            + "time and CPU of each game tick (GameManager.ApplyRates, live ticks apart from catch-up), "
            + "and an agent's tick split into its parts (stat ticks, action checks and the actions it "
            + "performs, by action name, duty work and hiding checks). A "
            + "measurement aid for performance work; patches are installed only when this is true. "
            + "Off by default.");
        if (!enabled.Value) return;

        bool a = SafePatcher.TryPatch(harmony, typeof(InGameNPC), "UpdateTick",
            postfix: new HarmonyMethod(AccessTools.Method(typeof(FrameTimingDiagnostics), nameof(UpdateTick_Postfix))));
        bool b = SafePatcher.TryPatch(harmony, typeof(InGameNPC), "SelectDuty",
            prefix: new HarmonyMethod(AccessTools.Method(typeof(FrameTimingDiagnostics), nameof(SelectDuty_Prefix))),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(FrameTimingDiagnostics), nameof(SelectDuty_Postfix))));
        bool c = SafePatcher.TryPatch(harmony, typeof(GameManager), "ApplyRates",
            postfix: new HarmonyMethod(AccessTools.Method(typeof(FrameTimingDiagnostics), nameof(ApplyRates_Postfix))));
        int parts = PatchTickParts(harmony);
        Armed = true;
        Util.Log.Info($"FrameTimingDiagnostics: enabled (UpdateTick={a}, SelectDuty={b}, ApplyRates={c}, tick parts={parts}/{PartNames.Length}); one summary per {WindowSeconds:F0} s.");
    }

    // ---- NPC tick parts: what an agent's tick spends its CPU on (feature 17 follow-up) ----
    // Each part is EXCLUSIVE time: a timed call's MoveNext (or body) minus every timed call nested inside
    // it, kept on a main-thread stack, so one agent's action that synchronously starts another agent's
    // CheckForActions is not counted twice (the first 2026-10-12 read summed inclusive time and reported
    // more action CPU than the ticks' own wall time). Iterators are read on their compiled MoveNext, never
    // the builder (a builder patch can be inlined away into an already-recompiled caller, CLAUDE.md
    // "Harmony Patching Pitfalls"). CheckForActions runs as its own coroutine after UpdateTick yields to
    // it, so most of its time is NOT inside the UpdateTick figure above. "actionConditions" is
    // GeneralCondition.ConditionsValid with an NPC user; "actionPerform" is PerformActionAsEnumerator's
    // MoveNext for an NPC user; "hide" is GameManager.NPCShouldHide; "statsZeroRate" is a stat whose CurrentRate read 0 at the call. A call not
    // made for an NPC still takes part in the nesting stack, but is not attributed.
    private static readonly string[] PartNames = { "stats", "actions", "dutyRun", "dutySet", "statsZeroRate", "actionConditions", "actionPerform", "hide",
        "allActSteps", "allSpendDtp", "allRefreshSlots", "allPassiveFx", "allCollectMods", "allPlayerEnv" };
    private const int PartStatsZeroRate = 4;
    private const int PartConditions = 5;
    private const int PartPerform = 6;
    private const int PartHide = 7;
    private static readonly long[,] _partTicks = new long[PartNames.Length, 2];   // [part, inEnv 1 / elsewhere 0]
    private static readonly int[,] _partCalls = new int[PartNames.Length, 2];
    private static int _actionsPerformed;
    private static readonly System.Collections.Generic.Dictionary<MethodBase, int> _partOf = new();
    private static readonly System.Collections.Generic.Dictionary<Type, FieldInfo> _ownerField = new();
    private static readonly System.Collections.Generic.List<long> _childTicks = new();
    // actionPerform split by action name (DefaultText): [0] exclusive ticks, [1] MoveNext steps.
    private static readonly System.Collections.Generic.Dictionary<string, long[]> _performByName = new();
    private static readonly System.Collections.Generic.Dictionary<Type, FieldInfo> _actionField = new();

    internal struct PartState { public long Start; public int Depth; }

    // Parts of the action pipeline every action pays (agent or player), exclusive CPU, reported under
    // "elsewhere" with no environment split: an idle run has no player actions, so they are agents'.
    private static readonly System.Collections.Generic.Dictionary<MethodBase, int> _globalPartOf = new();

    private static int PatchTickParts(Harmony harmony)
    {
        var targets = new (Type type, string method, int part, string owner)[]
        {
            (typeof(InGameNPCStat), "UpdateTick", 0, "<>4__this"),
            (typeof(InGameNPC), "CheckForActions", 1, "<>4__this"),
            (typeof(InGameNPC), "StartOrUpdateCurrentDuty", 2, "<>4__this"),
            (typeof(InGameNPC), "SetCurrentDuty", 3, "<>4__this"),
            (typeof(GameManager), "PerformActionAsEnumerator", PartPerform, "_User"),
        };
        var prefix = new HarmonyMethod(AccessTools.Method(typeof(FrameTimingDiagnostics), nameof(Part_Prefix)));
        var postfix = new HarmonyMethod(AccessTools.Method(typeof(FrameTimingDiagnostics), nameof(Part_Postfix)));
        int patched = 0;
        foreach (var (type, method, part, owner) in targets)
        {
            try
            {
                var builder = AccessTools.Method(type, method);
                var moveNext = builder == null ? null : AccessTools.EnumeratorMoveNext(builder);
                var ownerField = moveNext == null ? null : AccessTools.Field(moveNext.DeclaringType, owner);
                if (moveNext == null || ownerField == null)
                {
                    Util.Log.Warn($"FrameTimingDiagnostics: {type.Name}.{method} MoveNext not found; that part is not timed.");
                    continue;
                }
                _partOf[moveNext] = part;
                _ownerField[moveNext.DeclaringType] = ownerField;
                if (part == PartPerform)
                {
                    var actionField = AccessTools.Field(moveNext.DeclaringType, "_Action");
                    if (actionField != null) _actionField[moveNext.DeclaringType] = actionField;
                }
                harmony.Patch(moveNext, prefix: prefix, postfix: postfix);
                patched++;
            }
            catch (Exception ex)
            {
                Util.Log.Warn($"FrameTimingDiagnostics: timing {type.Name}.{method} failed: {ex.Message}");
            }
        }
        try
        {
            var conditions = AccessTools.Method(typeof(GeneralCondition), "ConditionsValid");
            if (conditions != null)
            {
                harmony.Patch(conditions,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(FrameTimingDiagnostics), nameof(Part_Prefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(FrameTimingDiagnostics), nameof(Conditions_Postfix))));
                patched++;
            }
            else Util.Log.Warn("FrameTimingDiagnostics: GeneralCondition.ConditionsValid not found; conditions are not timed.");
        }
        catch (Exception ex) { Util.Log.Warn($"FrameTimingDiagnostics: timing GeneralCondition.ConditionsValid failed: {ex.Message}"); }
        try
        {
            var hide = AccessTools.Method(typeof(GameManager), "NPCShouldHide", new[] { typeof(InGameNPC) });
            if (hide != null)
            {
                harmony.Patch(hide,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(FrameTimingDiagnostics), nameof(Part_Prefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(FrameTimingDiagnostics), nameof(Hide_Postfix))));
                patched++;
            }
            else Util.Log.Warn("FrameTimingDiagnostics: GameManager.NPCShouldHide not found; hiding checks are not timed.");
        }
        catch (Exception ex) { Util.Log.Warn($"FrameTimingDiagnostics: timing GameManager.NPCShouldHide failed: {ex.Message}"); }
        patched += PatchActionPipelineParts(harmony);
        try
        {
            if (SafePatcher.TryPatch(harmony, typeof(NPCAction), "ToAction",
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(FrameTimingDiagnostics), nameof(ToAction_Postfix)))))
                patched++;
        }
        catch (Exception ex) { Util.Log.Warn($"FrameTimingDiagnostics: counting NPCAction.ToAction failed: {ex.Message}"); }
        return patched;
    }

    static void Part_Prefix(out PartState __state)
    {
        __state = new PartState { Start = Stopwatch.GetTimestamp(), Depth = _childTicks.Count };
        _childTicks.Add(0);
    }

    // Closes the frame opened by Part_Prefix and returns this call's exclusive ticks. Truncating to the
    // recorded depth also heals the stack after a call that threw (Harmony skips a postfix then).
    private static long CloseFrame(PartState s)
    {
        long elapsed = Stopwatch.GetTimestamp() - s.Start;
        if (s.Depth >= _childTicks.Count) return elapsed;
        long child = _childTicks[s.Depth];
        _childTicks.RemoveRange(s.Depth, _childTicks.Count - s.Depth);
        if (s.Depth > 0) _childTicks[s.Depth - 1] += elapsed;
        return elapsed - child;
    }

    private static void Attribute(int part, InGameNPC npc, long exclusive)
    {
        int bucket = npc.CurrentEnvironment.MatchesPlayerEnv ? 1 : 0;
        _partTicks[part, bucket] += exclusive;
        _partCalls[part, bucket]++;
    }

    static void Part_Postfix(object __instance, MethodBase __originalMethod, PartState __state)
    {
        long exclusive = CloseFrame(__state);
        try
        {
            if (!_partOf.TryGetValue(__originalMethod, out int part)) return;
            var owner = _ownerField.TryGetValue(__instance.GetType(), out var f) ? f.GetValue(__instance) : null;
            InGameNPC npc = owner as InGameNPC;
            if (owner is InGameNPCOrPlayer user) npc = user.Player ? null : user.NPC;
            if (owner is InGameNPCStat stat)
            {
                npc = stat.ParentNPC;
                if (stat.CurrentRate == 0f) part = PartStatsZeroRate;
            }
            if (npc == null) return;
            Attribute(part, npc, exclusive);
            if (part == PartPerform && _actionField.TryGetValue(__instance.GetType(), out var af)
                && af.GetValue(__instance) is CardAction action)
            {
                string name = action.ActionName.DefaultText ?? "?";
                if (!_performByName.TryGetValue(name, out var acc)) _performByName[name] = acc = new long[2];
                acc[0] += exclusive;
                acc[1]++;
            }
        }
        catch (Exception ex) { Util.Log.Debug($"FrameTimingDiagnostics: tick-part read failed: {ex.Message}"); }
    }

    static void Hide_Postfix(InGameNPC _Agent, PartState __state)
    {
        long exclusive = CloseFrame(__state);
        try { if (_Agent != null) Attribute(PartHide, _Agent, exclusive); }
        catch (Exception ex) { Util.Log.Debug($"FrameTimingDiagnostics: hide read failed: {ex.Message}"); }
    }

    static void Conditions_Postfix(InGameNPC _NPCUser, PartState __state)
    {
        long exclusive = CloseFrame(__state);
        try { if (_NPCUser != null) Attribute(PartConditions, _NPCUser, exclusive); }
        catch (Exception ex) { Util.Log.Debug($"FrameTimingDiagnostics: conditions read failed: {ex.Message}"); }
    }

    static void ToAction_Postfix() => Interlocked.Increment(ref _actionsPerformed);

    private static int PatchActionPipelineParts(Harmony harmony)
    {
        var targets = new (Type type, string method, Type[] args, bool iterator, int part)[]
        {
            (typeof(GameManager), "ActionRoutineSteps", null, true, 8),
            (typeof(GameManager), "SpendDaytimePoints", null, true, 9),
            (typeof(GraphicsManager), "RefreshSlots", new[] { typeof(bool) }, false, 10),
            (typeof(GameManager), "UpdatePassiveEffects", new[] { typeof(bool), typeof(bool) }, true, 11),
            (typeof(CardAction), "CollectActionModifiers", null, false, 12),
            (typeof(InGameCardBase), "UpdatePlayerEnvironment", null, false, 13),
        };
        var prefix = new HarmonyMethod(AccessTools.Method(typeof(FrameTimingDiagnostics), nameof(Part_Prefix)));
        var postfix = new HarmonyMethod(AccessTools.Method(typeof(FrameTimingDiagnostics), nameof(Global_Postfix)));
        int patched = 0;
        foreach (var (type, method, args, iterator, part) in targets)
        {
            try
            {
                var target = args == null ? AccessTools.Method(type, method) : AccessTools.Method(type, method, args);
                if (target != null && iterator) target = AccessTools.EnumeratorMoveNext(target);
                if (target == null)
                {
                    Util.Log.Warn($"FrameTimingDiagnostics: {type.Name}.{method} not found; that action-pipeline part is not timed.");
                    continue;
                }
                _globalPartOf[target] = part;
                harmony.Patch(target, prefix: prefix, postfix: postfix);
                patched++;
            }
            catch (Exception ex)
            {
                Util.Log.Warn($"FrameTimingDiagnostics: timing {type.Name}.{method} failed: {ex.Message}");
            }
        }
        return patched;
    }

    static void Global_Postfix(MethodBase __originalMethod, PartState __state)
    {
        long exclusive = CloseFrame(__state);
        if (!_globalPartOf.TryGetValue(__originalMethod, out int part)) return;
        _partTicks[part, 0] += exclusive;
        _partCalls[part, 0]++;
    }

    // Calls count MoveNext steps (one per resume), not iterator runs.
    private static void AppendTickParts(StringBuilder sb)
    {
        if (_partOf.Count == 0) return;
        sb.Append(" | NPC tick parts, exclusive CPU (ms x steps) ");
        for (int p = 0; p < PartNames.Length; p++)
        {
            long e = _partTicks[p, 0], i = _partTicks[p, 1];
            int ec = _partCalls[p, 0], ic = _partCalls[p, 1];
            _partTicks[p, 0] = _partTicks[p, 1] = 0;
            _partCalls[p, 0] = _partCalls[p, 1] = 0;
            sb.Append($"{PartNames[p]}: elsewhere={Ms(e):F2}x{ec}, inEnv={Ms(i):F2}x{ic}; ");
        }
        sb.Append($"agent actions performed={Interlocked.Exchange(ref _actionsPerformed, 0)}");
        if (_performByName.Count > 0)
        {
            var rows = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, long[]>>(_performByName);
            rows.Sort((x, y) => y.Value[0].CompareTo(x.Value[0]));
            sb.Append(" | actionPerform by action (ms x steps): ");
            for (int r = 0; r < rows.Count && r < 15; r++)
                sb.Append($"'{rows[r].Key}'={Ms(rows[r].Value[0]):F1}x{rows[r].Value[1]}; ");
            _performByName.Clear();
        }
    }

    /// <summary>Called from <c>Plugin.Update</c> once per frame. One bool read when unarmed.</summary>
    internal static void OnFrame()
    {
        if (!Armed) return;
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (_windowStart < 0.0) _windowStart = now;
        if (_frameCount == _frameMs.Length) Array.Resize(ref _frameMs, _frameMs.Length * 2);
        _frameMs[_frameCount++] = Time.unscaledDeltaTime * 1000f;
        if (now - _windowStart < WindowSeconds) return;
        try { LogWindow(now - _windowStart); }
        catch (Exception ex) { Util.Log.Warn($"FrameTimingDiagnostics: summary failed: {ex.Message}"); }
        _frameCount = 0;
        _windowStart = now;
    }

    private static void LogWindow(double seconds)
    {
        int n = _frameCount;
        if (n == 0) return;
        var sorted = new float[n];
        Array.Copy(_frameMs, sorted, n);
        Array.Sort(sorted);
        double sum = 0; int slow = 0;
        for (int i = 0; i < n; i++) { sum += sorted[i]; if (sorted[i] > SlowFrameMs) slow++; }
        float p95 = sorted[Math.Min(n - 1, (int)Math.Ceiling(n * 0.95) - 1)];

        var sb = new StringBuilder();
        sb.Append($"FrameTiming[{seconds:F0}s]: frames={n}, mean={sum / n:F2}ms, p95={p95:F2}ms, worst={sorted[n - 1]:F2}ms, over{SlowFrameMs:F0}ms={slow}");
        sb.Append(" | UpdateTick CPU (ms x calls) ");
        string[] names = { "EarlyAfter", "Late", "All", "EarlyBefore" };
        long inTotal = 0, elseTotal = 0;
        for (int s = 0; s < StepCount; s++)
        {
            long i = Interlocked.Exchange(ref _inEnvTicks[s], 0);
            long e = Interlocked.Exchange(ref _elsewhereTicks[s], 0);
            int ic = Interlocked.Exchange(ref _inEnvCalls[s], 0);
            int ec = Interlocked.Exchange(ref _elsewhereCalls[s], 0);
            inTotal += i; elseTotal += e;
            if (ic == 0 && ec == 0) continue;
            sb.Append($"{names[s]}: inEnv={Ms(i):F2}x{ic}, elsewhere={Ms(e):F2}x{ec}; ");
        }
        double allNpc = Ms(inTotal + elseTotal);
        sb.Append($"total inEnv={Ms(inTotal):F2}, elsewhere={Ms(elseTotal):F2}");
        if (allNpc > 0) sb.Append($" (elsewhere {Ms(elseTotal) * 100.0 / allNpc:F0}% of NPC tick CPU)");
        long sd = Interlocked.Exchange(ref _selectDutyTicks, 0);
        int sdc = Interlocked.Exchange(ref _selectDutyCalls, 0);
        sb.Append($" | SelectDuty={Ms(sd):F2}ms x{sdc}");
        if (Patching.Performance.OffEnvNpcAiThrottle.Enabled)
            sb.Append($" | OffEnvNpcAiThrottle skipped={Patching.Performance.OffEnvNpcAiThrottle.Skipped}/{Patching.Performance.OffEnvNpcAiThrottle.Seen} (cumulative)");
        sb.Append(" | ApplyRates (ms x calls) ");
        string[] kinds = { "live", "catchUp" };
        for (int k = 0; k < 2; k++)
        {
            long w = Interlocked.Exchange(ref _applyWallTicks[k], 0);
            long cpu = Interlocked.Exchange(ref _applyCpuTicks[k], 0);
            int c = Interlocked.Exchange(ref _applyCalls[k], 0);
            if (c == 0) continue;
            sb.Append($"{kinds[k]}: wall={Ms(w):F2}, cpu={Ms(cpu):F2} x{c}; ");
        }
        AppendTickParts(sb);
        Util.Log.Info(sb.ToString());
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    // The env bucket is read at the first MoveNext (the iterator body runs nothing before it), which is
    // when the agent's tick actually starts. Same wrapper shape as TrackingTimingDiagnostics.
    static IEnumerator UpdateTick_Postfix(IEnumerator result, InGameNPC __instance, TickSteps _TickStep)
    {
        int step = Math.Max(0, Math.Min(StepCount - 1, (int)_TickStep));
        bool inEnv = false;
        bool bucketRead = false;
        long cpuTicks = 0;
        try
        {
            while (true)
            {
                bool hasMore;
                var sw = Stopwatch.StartNew();
                try
                {
                    if (!bucketRead)
                    {
                        bucketRead = true;
                        try { inEnv = __instance != null && __instance.CurrentEnvironment.MatchesPlayerEnv; }
                        catch (Exception ex) { Util.Log.Debug($"FrameTimingDiagnostics: env bucket read failed: {ex.Message}"); }
                    }
                    hasMore = result.MoveNext();
                }
                finally { sw.Stop(); cpuTicks += sw.ElapsedTicks; }
                if (!hasMore) break;
                yield return result.Current;
            }
        }
        finally
        {
            if (inEnv) { Interlocked.Add(ref _inEnvTicks[step], cpuTicks); Interlocked.Increment(ref _inEnvCalls[step]); }
            else { Interlocked.Add(ref _elsewhereTicks[step], cpuTicks); Interlocked.Increment(ref _elsewhereCalls[step]); }
        }
    }

    // Wall time of one ApplyRates (the per-tick step that runs every NPC step). Same wrapper shape as
    // UpdateTick_Postfix; started at the first MoveNext, which is when the tick begins.
    static IEnumerator ApplyRates_Postfix(IEnumerator result, int _CatchupTick)
    {
        int kind = _CatchupTick > 0 ? 1 : 0;
        long cpuTicks = 0;
        long start = 0;
        try
        {
            while (true)
            {
                bool hasMore;
                var sw = Stopwatch.StartNew();
                if (start == 0) start = Stopwatch.GetTimestamp();
                try { hasMore = result.MoveNext(); }
                finally { sw.Stop(); cpuTicks += sw.ElapsedTicks; }
                if (!hasMore) break;
                yield return result.Current;
            }
        }
        finally
        {
            if (start != 0) Interlocked.Add(ref _applyWallTicks[kind], Stopwatch.GetTimestamp() - start);
            Interlocked.Add(ref _applyCpuTicks[kind], cpuTicks);
            Interlocked.Increment(ref _applyCalls[kind]);
        }
    }

    static void SelectDuty_Prefix() => _selectDutyStart = Stopwatch.GetTimestamp();

    static void SelectDuty_Postfix()
    {
        long start = _selectDutyStart;
        if (start == 0) return;
        _selectDutyStart = 0;
        Interlocked.Add(ref _selectDutyTicks, Stopwatch.GetTimestamp() - start);
        Interlocked.Increment(ref _selectDutyCalls);
    }
}
