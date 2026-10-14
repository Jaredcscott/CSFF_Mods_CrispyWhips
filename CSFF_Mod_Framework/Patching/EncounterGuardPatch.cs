using CSFFModFramework.Util;

namespace CSFFModFramework.Patching;

/// <summary>
/// The framework's single <c>EncounterPopup.StartEncounter</c> prefix: the suppression point for
/// encounter starts with NO NPC attached (<c>__1 == null</c>), where every <see cref="Api.EncounterGuards"/>
/// predicate is asked (source <c>NoNpc</c>). Cheap early-out when no guards are registered.
///
/// <para>A start WITH an NPC (<c>__1 != null</c>) always proceeds here. On EA 0.68b that covers both
/// the fights the player chooses (Approach, a dialog's attack answer, Give Chase) and an agent's own
/// attack, which this method cannot tell apart (both pass the NPC: W0-1). Agent attacks are therefore
/// filtered at their source by <see cref="AgentEncounterGuardPatch"/>, and only by a guard that names
/// the agent or the encounter; a player-chosen fight is never suppressed by anything.</para>
///
/// <para>A save that resumes a fight in progress (<c>CurrentSaveData.HasEncounterData</c>, the
/// <c>GameManager</c> branch after <c>OnGMInitialized</c> that restarts the saved encounter with no
/// NPC) is never cancelled (framework 2.28.0; before it, a reloaded mid-fight save in a guarded place
/// lost its fight). Same test as <see cref="WildlifeRaidPatch"/>'s no-reroll rule.</para>
///
/// <para>Note: <see cref="WildlifeRaidPatch"/> postfixes the same method; it checks
/// <c>EncounterGuards.WasSuppressedThisFrame</c> so suppressed encounters don't trigger bear raids.</para>
/// </summary>
internal static class EncounterGuardPatch
{
    private static bool _resumeCheckWarned;

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var popupType = Reflection.ReflectionCache.FindType("EncounterPopup");
            if (popupType == null)
            {
                Log.Debug("[EncounterGuardPatch] EncounterPopup type not found, guard patch skipped.");
                return;
            }

            // StartEncounter(Encounter encounter, InGameNPC npc, bool flag)
            var startEncounter = popupType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "StartEncounter" && m.GetParameters().Length == 3);
            if (startEncounter == null)
            {
                Log.Debug("[EncounterGuardPatch] StartEncounter(3-param) not found, guard patch skipped.");
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

    // __0 = Encounter, __1 = InGameNPC (null = no NPC attached), __2 = bool
    private static bool StartEncounter_Prefix(object __0, object __1)
    {
        if (__1 != null) return true;                       // player-chosen or agent start: never here
        if (Api.EncounterGuards.Count == 0) return true;    // no guards: zero-cost path
        if (IsSaveResume()) return true;                    // resuming a saved fight: never cancel
        return !Api.EncounterGuards.EvaluateWildlife(__0);
    }

    private static bool IsSaveResume()
    {
        try
        {
            var gm = MBSingleton<GameManager>.Instance;
            return gm != null && gm.CurrentSaveData != null && gm.CurrentSaveData.HasEncounterData;
        }
        catch (Exception ex)
        {
            if (!_resumeCheckWarned)
            {
                _resumeCheckWarned = true;
                Log.Warn($"[EncounterGuardPatch] save-resume check failed, guards evaluated as before: {ex.GetType().Name} {ex.Message}");
            }
            return false;
        }
    }
}
