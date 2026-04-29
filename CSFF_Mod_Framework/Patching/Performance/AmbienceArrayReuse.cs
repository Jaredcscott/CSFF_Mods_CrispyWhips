using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace CSFFModFramework.Patching.Performance;

// AmbienceImageEffect.Update allocates a fresh `new float[3]` every frame to pass to the
// shader. At 60 fps that's ~180 three-element arrays per second — ~1.4 KB/sec of pure GC
// churn, purely for a buffer whose contents are overwritten before each use.
//
// Transpiler: replace the `ldc.i4.3; newarr System.Single` pair with `ldsfld <cached>`.
// The cached array is a process-lifetime static we own — the shader consumer only reads
// the three slots after the game writes to them, so reuse is safe.
//
// Defensive: only replace `newarr` of System.Single preceded by a literal 3 push. If the
// IL shape ever changes (different size, different element type), the transpiler leaves
// it alone and logs that zero substitutions were made.
internal static class AmbienceArrayReuse
{
    internal static readonly float[] Cached = new float[3];

    public static void ApplyPatch(Harmony harmony)
    {
        var target = AccessTools.Method("AmbienceImageEffect:Update");
        if (target == null)
        {
            Util.Log.Debug("AmbienceArrayReuse: AmbienceImageEffect.Update not found, skipping.");
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
            Util.Log.Error($"AmbienceArrayReuse: failed to patch: {ex.Message}");
        }
    }

    private static IEnumerable<CodeInstruction> ReuseArrayTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var cachedField = AccessTools.Field(typeof(AmbienceArrayReuse), nameof(Cached));
        var list = new List<CodeInstruction>(instructions);
        int swaps = 0;

        for (int i = 0; i < list.Count - 1; i++)
        {
            var cur = list[i];
            var next = list[i + 1];

            if (next.opcode != OpCodes.Newarr) continue;
            if (!(next.operand is System.Type t) || t != typeof(float)) continue;
            if (!IsLoadConst3(cur)) continue;

            // Replace `ldc.i4.3` with `ldsfld Cached`, keep the original labels,
            // and turn the `newarr` into a Nop so nothing else shifts.
            list[i] = new CodeInstruction(OpCodes.Ldsfld, cachedField) { labels = cur.labels, blocks = cur.blocks };
            list[i + 1] = new CodeInstruction(OpCodes.Nop) { labels = next.labels, blocks = next.blocks };
            swaps++;
        }

        if (swaps > 0)
            Util.Log.Info($"AmbienceArrayReuse: replaced {swaps} per-frame float[3] allocation(s) in AmbienceImageEffect.Update.");
        else
            Util.Log.Debug("AmbienceArrayReuse: no `ldc.i4.3; newarr System.Single` pattern matched — IL shape may have changed.");

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
