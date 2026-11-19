using CSFFModFramework.Util;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;

namespace CSFFModFramework.Patching.Performance;

// AmbienceImageEffect.Update allocates a fresh `new float[3]` every frame to pass to the
// shader. At 60 fps that's ~180 three-element arrays per second - ~1.4 KB/sec of pure GC
// churn, purely for a buffer whose contents are overwritten before each use.
//
// Transpiler: replace the `ldc.i4.3; newarr System.Single` pair with `ldsfld <cached>`.
// The cached array is a process-lifetime static we own - the shader consumer only reads
// the three slots after the game writes to them, so reuse is safe.
//
// Defensive: only replace `newarr` of System.Single preceded by a literal 3 push. If the
// IL shape ever changes (different size, different element type), the transpiler leaves
// it alone and logs that zero substitutions were made.
//
// Game versions: EA 0.69c made the allocation (Documentation/Benches/Framework_Perf_069c, "replaced 1").
// EA 0.69e's Update sets each shader float with its own SetFloat call and allocates no float array
// (.decomp/AmbienceImageEffect.cs), so there the transpiler finds nothing to reuse and changes nothing.
// That is the game having removed the cost itself, not a broken patch: it is a Debug line. A float
// array that IS still allocated but no longer in the float[3] shape stays a Warn.
internal static class AmbienceArrayReuse
{
    internal static readonly float[] Cached = new float[3];

    public static void ApplyPatch(Harmony harmony, ConfigFile config)
    {
        var enabledCfg = config.Bind(
            "Performance", "AmbienceArrayReuseEnabled", true,
            "On a game version whose AmbienceImageEffect.Update allocates a float[3] every frame (EA 0.69c did), "
            + "reuse a cached array instead. EA 0.69e no longer makes that allocation, so this does nothing there. "
            + "Set false to disable for diagnostic isolation.",
            "在 AmbienceImageEffect.Update 每帧分配 float[3] 的游戏版本上（EA 0.69c 如此），改为复用缓存的数组。"
            + "EA 0.69e 已不再进行该分配，因此此项在该版本上不起作用。设为 false 可在诊断时单独排除此项。");
        if (!enabledCfg.Value)
        {
            Util.Log.Debug("AmbienceArrayReuse: disabled via config.");
            return;
        }

        var target = AccessTools.Method("AmbienceImageEffect:Update");
        if (target == null)
        {
            Util.Log.Warn("AmbienceArrayReuse: AmbienceImageEffect.Update not found (renamed by a game update?); per-frame float[3] reuse is off.");
            return;
        }

        var transpiler = new HarmonyMethod(AccessTools.Method(
            typeof(AmbienceArrayReuse), nameof(ReuseArrayTranspiler)));
        try
        {
            harmony.Patch(target, transpiler: transpiler);
        }
        catch (System.Exception ex)
        {
            Util.Log.Error($"AmbienceArrayReuse: failed to patch: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    private static IEnumerable<CodeInstruction> ReuseArrayTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        // Plain reflection, not AccessTools: the field is this class's own, and it keeps the transpiler runnable
        // outside the game (Development_Tools/Tests/Framework-WarningSweep-2026-11-14.Tests.ps1 feeds it IL).
        var cachedField = typeof(AmbienceArrayReuse).GetField(nameof(Cached),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var list = new List<CodeInstruction>(instructions);
        int swaps = 0, floatArrays = 0;

        for (int i = 0; i < list.Count - 1; i++)
        {
            var cur = list[i];
            var next = list[i + 1];

            if (next.opcode != OpCodes.Newarr) continue;
            if (!(next.operand is System.Type t) || t != typeof(float)) continue;
            floatArrays++;
            if (!IsLoadConst3(cur)) continue;

            // Replace `ldc.i4.3` with `ldsfld Cached`, keep the original labels,
            // and turn the `newarr` into a Nop so nothing else shifts.
            list[i] = new CodeInstruction(OpCodes.Ldsfld, cachedField) { labels = cur.labels, blocks = cur.blocks };
            list[i + 1] = new CodeInstruction(OpCodes.Nop) { labels = next.labels, blocks = next.blocks };
            swaps++;
        }

        if (swaps > 0)
            Util.Log.Debug($"AmbienceArrayReuse: replaced {swaps} per-frame float[3] allocation(s) in AmbienceImageEffect.Update.");
        else if (floatArrays == 0)
            Util.Log.Debug("AmbienceArrayReuse: AmbienceImageEffect.Update allocates no float array on this game version (EA 0.69e sets each shader float on its own); nothing to reuse, method left as it is.");
        else
            Util.Log.Warn($"AmbienceArrayReuse: AmbienceImageEffect.Update allocates {floatArrays} float array(s), but none is the `ldc.i4.3; newarr System.Single` pair this patch reuses (IL shape changed by a game update?); per-frame float[3] reuse is off.");

        return list;
    }

    private static bool IsLoadConst3(CodeInstruction ins)
    {
        if (ins.opcode == OpCodes.Ldc_I4_3) return true;
        if (ins.opcode == OpCodes.Ldc_I4_S && ins.operand is sbyte sb && sb == 3) return true;
        if (ins.opcode == OpCodes.Ldc_I4 && ins.operand is int i && i == 3) return true;
        return false;
    }
}
