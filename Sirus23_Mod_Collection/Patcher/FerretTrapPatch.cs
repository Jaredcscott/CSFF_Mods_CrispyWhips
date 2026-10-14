using System;
using System.Collections.Generic;
using BepInEx.Logging;
using CSFFModFramework.Util;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// Feature 29 (a ferret for hare burrows): at GameDataReady, append ONE CardOrTagIndividualDropChanceModifier keyed on
/// the Ferret card to the Snare and the Log Trap, the only two vanilla traps that can catch a hare (W0-20: the hare
/// is caught through OnInteractActions[0].AgentActionChance, not through any collection; the Deadfall and Pit Trap
/// carry hare cunning 10000, so no weight helps there). The entry is the all-flags-false form, so a Ferret anywhere
/// the player carries it in the trap's environment adds its bonus to the trap's catch chance.
///
/// C# and not GameSourceModify because CardsModifiers is a struct array three levels down an action list, which no
/// GameSourceModify patch can append to. Shape copied from the framework's TrapIntegrator.JoinTriggerInteractors:
/// OnInteractActions is a List, the action is found by TriggerActionInAgent, the old array is copied and one entry
/// appended (append only, never replace), the pass is idempotent (an existing entry for the ferret is left alone),
/// and every early return logs its cause. ONE entry per array, because vanilla's CurrentDropChance double counts a
/// second matching entry (W0-20). A starving ferret helps less: ScaleByDurabilities carries the Special1 (Hunger) bit.
/// </summary>
internal static class FerretTrapPatch
{
    private const string FerretUid = "sir_ferret";
    private const string TrapActionId = "Interact with a Trap";

    // Vanilla Snare and Log Trap. INFERRED balance placeholders anchored to vanilla's own +25 and +15 for a
    // supernatural helper (W0-20): Snare 25-65 becomes 45-85 (+20), Log Trap 12.5-32.5 becomes 22.5-42.5 (+10).
    private const string SnareUid = "ece4346feb9fd2c46b6703a117d0bd3b";
    private const string LogTrapUid = "7ee6dbdbddc8e7e4e9eb065f6a22df39";
    internal const float SnareBonus = 20f;
    internal const float LogTrapBonus = 10f;

    private static ManualLogSource Logger => Plugin.Logger;

    internal static void Apply()
    {
        try
        {
            var ferret = CardUtil.GetCardDataById(FerretUid) as CardData;
            if (ferret == null)
            {
                Logger?.LogWarning($"[FerretTrap] {FerretUid} not loaded; hare traps get no ferret bonus.");
                return;
            }

            int ok = AppendTo(SnareUid, "Snare", ferret, SnareBonus);
            ok += AppendTo(LogTrapUid, "Log Trap", ferret, LogTrapBonus);
            if (ok == 0)
                Logger?.LogWarning("[FerretTrap] Neither the Snare nor the Log Trap took the ferret entry; hare traps get no ferret bonus.");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[FerretTrap] Hare traps get no ferret bonus: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    /// <summary>
    /// Reusable append (feature 37's truffle hog calls this on a CardDrop.DropChance): adds ONE card-keyed modifier
    /// (all place flags false, MaxStack 1) to a CardDropChance struct passed by ref, allocating the array when it is
    /// null (H&amp;F's truffle drops carry a null CardsModifiers). Append only, idempotent: returns false and changes
    /// nothing when an entry keyed on the same card is already there. The caller writes the struct back.
    /// </summary>
    internal static bool AppendCardModifier(ref CardDropChance chance, CardData keyCard, float bonus, DurabilitiesTypes scaleBy)
    {
        var old = chance.CardsModifiers ?? Array.Empty<CardOrTagIndividualDropChanceModifier>();
        foreach (var entry in old)
        {
            if (ReferenceEquals(entry.Card.Target, keyCard)) return false;
        }
        var grown = new CardOrTagIndividualDropChanceModifier[old.Length + 1];
        Array.Copy(old, grown, old.Length);
        grown[old.Length] = new CardOrTagIndividualDropChanceModifier
        {
            Card = new CardOrTagRef { Target = keyCard },
            MaxChancePerCard = bonus,
            MaxStack = 1,
            ScaleByDurabilities = scaleBy,
        };
        chance.CardsModifiers = grown;
        return true;
    }

    /// <summary>Returns 1 when the trap now carries the ferret entry (added or already there), else 0.</summary>
    private static int AppendTo(string trapUid, string label, CardData ferret, float bonus)
    {
        try
        {
            var trap = CardUtil.GetCardDataById(trapUid) as CardData;
            if (trap == null)
            {
                Logger?.LogWarning($"[FerretTrap] Vanilla {label} ({trapUid}) not found; it gets no ferret bonus.");
                return 0;
            }

            // OnInteractActions is a List<OnInteractAction>, not an array.
            List<OnInteractAction> actions = trap.OnInteractActions;
            if (actions == null || actions.Count == 0)
            {
                Logger?.LogWarning($"[FerretTrap] Vanilla {label} has no OnInteractActions; it gets no ferret bonus.");
                return 0;
            }

            OnInteractAction trapAction = null;
            foreach (var action in actions)
            {
                if (action != null && string.Equals(action.TriggerActionInAgent, TrapActionId, StringComparison.Ordinal))
                {
                    trapAction = action;
                    break;
                }
            }
            if (trapAction == null)
            {
                Logger?.LogWarning($"[FerretTrap] Vanilla {label} has no \"{TrapActionId}\" action (a game update may have renamed it); it gets no ferret bonus.");
                return 0;
            }

            // AgentActionChance is a struct: read it into a local, append, write it back.
            var chance = trapAction.AgentActionChance;
            int before = chance.CardsModifiers?.Length ?? 0;
            bool added = AppendCardModifier(ref chance, ferret, bonus, DurabilitiesTypes.Special1);
            if (!added)
            {
                Logger?.Log(LogLevel.Debug, $"[FerretTrap] {label} already carries the ferret entry; nothing added.");
                return 1;
            }
            trapAction.AgentActionChance = chance;
            Logger?.Log(LogLevel.Debug, $"[FerretTrap] {label}: appended the ferret entry (+{bonus}, {before} -> {before + 1}).");
            return 1;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[FerretTrap] {label} gets no ferret bonus: {ex.InnerException?.ToString() ?? ex.ToString()}");
            return 0;
        }
    }
}
