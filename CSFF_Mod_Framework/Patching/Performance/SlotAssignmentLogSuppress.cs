using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace CSFFModFramework.Patching.Performance;

// Suppress the "Trying to assign X to Y" / "Y already has an assigned card! X" log spam
// emitted by DynamicLayoutSlot.AssignCard when a slot is reassigned while already occupied.
//
// Observed on late-game saves (390+ days, many placed improvements): the game logs a
// warning every time AssignCard encounters an occupied-but-reassignable slot. With many
// improvements on the board this fires repeatedly per frame during drag/drop, inventory
// shuffle, and environment transitions. Each warning allocates:
//   - an object[] (5 elements) for string.Concat formatting
//   - a boxed enum for SlotType.ToString()
//   - a boxed int for Index.ToString()
//   - the composed string
//   - a stack trace capture (Unity's Debug does this for LogWarning)
//
// All pure GC churn — the game itself recovers and reassigns cleanly. We strip the two
// Debug.LogWarning call sites via a Harmony transpiler. The method's actual assignment
// logic runs unchanged; only the two log calls are neutralized (replaced with Pop ops
// that consume the arguments already pushed on the stack).
internal static class SlotAssignmentLogSuppress
{
    public static void ApplyPatch(Harmony harmony)
    {
        var target = AccessTools.Method("DynamicLayoutSlot:AssignCard");
        if (target == null)
        {
            Util.Log.Warn("SlotAssignmentLogSuppress: DynamicLayoutSlot.AssignCard not found.");
            return;
        }

        var transpiler = new HarmonyMethod(AccessTools.Method(
            typeof(SlotAssignmentLogSuppress), nameof(StripLogWarnings)));
        try
        {
            harmony.Patch(target, transpiler: transpiler);
            Util.Log.Info("SlotAssignmentLogSuppress: stripped Debug.LogWarning calls from DynamicLayoutSlot.AssignCard.");
        }
        catch (System.Exception ex)
        {
            Util.Log.Error($"SlotAssignmentLogSuppress: failed to patch: {ex.Message}");
        }
    }

    // Transpiler: for every `call void UnityEngine.Debug::LogWarning(*)`, replace the
    // `call` with the equivalent number of `pop` instructions (one per argument).
    // This consumes the already-pushed args and drops them — the logic surrounding
    // the call remains intact.
    private static IEnumerable<CodeInstruction> StripLogWarnings(IEnumerable<CodeInstruction> instructions)
    {
        int stripped = 0;
        foreach (var ins in instructions)
        {
            if (ins.opcode == OpCodes.Call && ins.operand is System.Reflection.MethodInfo mi
                && mi.DeclaringType == typeof(UnityEngine.Debug)
                && mi.Name == "LogWarning")
            {
                int argCount = mi.GetParameters().Length;
                var replacements = new List<CodeInstruction>(argCount + 1);
                for (int i = 0; i < argCount; i++)
                    replacements.Add(new CodeInstruction(OpCodes.Pop));
                if (replacements.Count == 0)
                    replacements.Add(new CodeInstruction(OpCodes.Nop));
                // Transfer labels and exception-handler blocks from the stripped call onto
                // the first replacement — otherwise branches targeting the call site become
                // orphaned ("Label #N is not marked") and DMD compilation fails for this
                // method and every subsequent patch stacked on it.
                replacements[0].labels.AddRange(ins.labels);
                replacements[0].blocks.AddRange(ins.blocks);
                foreach (var r in replacements)
                    yield return r;
                stripped++;
                continue;
            }
            yield return ins;
        }
        Util.Log.Debug($"SlotAssignmentLogSuppress transpiler: stripped {stripped} Debug.LogWarning call(s).");
    }
}
