using CSFFModFramework.Util;

namespace CSFFModFramework.Patching;

/// <summary>
/// The framework's single <c>EncounterPopup.StartEncounter</c> prefix — the one
/// suppression point all <see cref="Api.EncounterGuards"/> predicates flow through.
/// NPC encounters (<c>__1 != null</c>) always proceed; only wildlife/ambient
/// encounters are evaluated. Cheap early-out when no guards are registered.
///
/// <para>Note: <see cref="WildlifeRaidPatch"/> postfixes the same method; it checks
/// <c>EncounterGuards.WasSuppressedThisFrame</c> so suppressed encounters don't
/// trigger bear raids.</para>
/// </summary>
internal static class EncounterGuardPatch
{
    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var popupType = Reflection.ReflectionCache.FindType("EncounterPopup");
            if (popupType == null)
            {
                Log.Debug("[EncounterGuardPatch] EncounterPopup type not found — guard patch skipped.");
                return;
            }

            // StartEncounter(Encounter encounter, InGameNPC npc, bool flag)
            var startEncounter = popupType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "StartEncounter" && m.GetParameters().Length == 3);
            if (startEncounter == null)
            {
                Log.Debug("[EncounterGuardPatch] StartEncounter(3-param) not found — guard patch skipped.");
                return;
            }

            var prefix = typeof(EncounterGuardPatch).GetMethod(nameof(StartEncounter_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(startEncounter, prefix: new HarmonyMethod(prefix) { priority = Priority.High });
            Log.Debug("[EncounterGuardPatch] encounter guard prefix applied.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[EncounterGuardPatch] ApplyPatch failed: {Log.ExceptionText(ex)}");
        }
    }

    // __0 = Encounter, __1 = InGameNPC (null = wildlife/ambient), __2 = bool
    private static bool StartEncounter_Prefix(object __0, object __1)
    {
        if (__1 != null) return true;                       // NPC encounter — never suppress
        if (Api.EncounterGuards.Count == 0) return true;    // no guards — zero-cost path
        return !Api.EncounterGuards.EvaluateWildlife(__0);
    }
}
