using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// A generated animal night attack (<c>Encounter.Aggression</c> in <c>Animals/*.json</c>) waits until
/// the player's current action is on its last tick instead of cancelling it (framework 2.28.0).
///
/// <para><strong>The failure.</strong> <see cref="DutyBuilder"/> builds the attack as a
/// <c>StartEncounterDutyAction</c> whose encounter has default starting options, so
/// <c>CannotInterruptActions</c> is false. NPC duties are chosen inside the player's own tick loop
/// (<c>GameManager.SpendDaytimePoints</c> -&gt; <c>ApplyRates</c> -&gt; each NPC's tick), so the attack
/// always lands while the player is performing something. When a dropped encounter arrives with the
/// player's root action still holding ticks (<c>RootActionRemainingTicks &gt; 0</c>), vanilla calls
/// <c>CancelAction()</c> (<c>.decomp/GameManager.cs</c>, the <c>collectionToDrop.DroppedEncounter</c>
/// branch in the action routine, lines 5160-5168 on EA 0.68b), and a cancelled action skips its whole
/// result block: a travel action loses every tick already spent and leaves the player where they
/// started. Player report (Chiwei, 2026-09-27): a 45-minute trip to another map ended with the owl
/// encounter and no movement at all. Vanilla predators interrupt the same way, but only when
/// provoked (starving, enraged, or a perk), while the Sirus23 owl seeks the player every night.</para>
///
/// <para><strong>The fix.</strong> A postfix on <c>StartEncounterDutyAction.CanBePerformed</c> turns a
/// true result false for a generated attack action while the player's root action has ticks left.
/// <c>NPCDuty</c> (line 185) will not select a duty whose first action cannot be performed, so the
/// held attack consumes nothing, and <c>MaxPerformPerDay</c> counts only attacks that happen. On the
/// action's last tick the remaining count is 0, the attack may be selected, and vanilla's other branch
/// (lines 5171-5175) queues the encounter to start once the action has completed, so a trip finishes
/// before the fight begins. An attack whose whole window passes during one long action is skipped for
/// that night. Encounters the player starts (Approach, a failed tame) are not generated attacks and are
/// untouched. <c>CanBePerformed</c> is a virtual override reached only through virtual dispatch, so the
/// JIT cannot inline it past this patch.</para>
/// </summary>
internal static class AttackWaitsForPlayerAction
{
    private static CardAction _lastHeldFor;

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var original = AccessTools.Method(typeof(StartEncounterDutyAction), nameof(StartEncounterDutyAction.CanBePerformed));
            if (original == null)
            {
                Log.Warn("AttackWaitsForPlayerAction: StartEncounterDutyAction.CanBePerformed not found; generated animal attacks can still cancel the player's action.");
                return;
            }
            harmony.Patch(original, postfix: new HarmonyMethod(AccessTools.Method(typeof(AttackWaitsForPlayerAction), nameof(Postfix))));
            Log.Debug("AttackWaitsForPlayerAction: patched StartEncounterDutyAction.CanBePerformed.");
        }
        catch (Exception ex)
        {
            Log.Warn($"AttackWaitsForPlayerAction: patch setup failed: {Log.ExceptionText(ex)}");
        }
    }

    private static void Postfix(StartEncounterDutyAction __instance, ref bool __result)
    {
        if (!__result || !DutyBuilder.IsGeneratedAttackAction(__instance)) return;
        try
        {
            var gm = MBSingleton<GameManager>.Instance;
            if (gm == null) return;
            var root = gm.RootAction;
            if (root == null || gm.RootActionRemainingTicks <= 0) return;

            __result = false;
            if (!ReferenceEquals(root, _lastHeldFor))
            {
                _lastHeldFor = root;
                Log.Info($"AttackWaitsForPlayerAction: '{__instance.name}' held until the player's current action ends.");
            }
        }
        catch (Exception ex)
        {
            // On any failure keep vanilla's answer: an attack that interrupts beats one that never fires.
            Log.Debug($"AttackWaitsForPlayerAction: check failed, vanilla result kept: {ex.GetType().Name} {ex.Message}");
        }
    }
}
