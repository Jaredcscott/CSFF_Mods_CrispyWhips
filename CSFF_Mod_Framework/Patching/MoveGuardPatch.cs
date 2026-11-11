using CSFFModFramework.Util;

namespace CSFFModFramework.Patching;

/// <summary>
/// The framework's single <c>GameManager.MoveNPC(InGameNPC, EnvID)</c> prefix: the refusal point every
/// <see cref="Api.Npc.RegisterMoveGuard"/> predicate flows through (plan Fleet APE Port, Row 11).
/// Community Mod Chest's <c>GuardTerritoryLeashPatch</c>/<c>WildlifeIndoorGuardPatch</c> and Far
/// Territory's <c>OwlIslandGuard</c> each used to patch this same method themselves, with the same
/// type-lookup-and-prefix skeleton; they now register a predicate here instead. Zero-cost when no
/// guard is registered.
/// </summary>
internal static class MoveGuardPatch
{
    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var gmType = Reflection.ReflectionCache.FindType("GameManager");
            var npcType = Reflection.ReflectionCache.FindType("InGameNPC");
            var envIdType = Reflection.ReflectionCache.FindType("EnvID");
            if (gmType == null || npcType == null || envIdType == null)
            {
                Log.Debug("[MoveGuardPatch] Required game types not found, move-guard patch skipped.");
                return;
            }

            var moveNpc = AccessTools.Method(gmType, "MoveNPC", new[] { npcType, envIdType });
            if (moveNpc == null)
            {
                Log.Debug("[MoveGuardPatch] GameManager.MoveNPC(InGameNPC, EnvID) not found, move-guard patch skipped.");
                return;
            }

            var prefix = typeof(MoveGuardPatch).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(moveNpc, prefix: new HarmonyMethod(prefix));
            Log.Debug("[MoveGuardPatch] move guard prefix applied.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[MoveGuardPatch] ApplyPatch failed: {Log.ExceptionText(ex)}");
        }
    }

    // __0 = InGameNPC, __1 = EnvID (the destination)
    private static bool Prefix(object __0, object __1)
    {
        if (Api.Npc.MoveGuardCount == 0) return true;
        return !Api.Npc.EvaluateMoveGuards(__0, __1);
    }
}
