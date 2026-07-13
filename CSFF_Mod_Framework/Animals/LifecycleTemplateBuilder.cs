using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// Builds the generated agent's AgentActions lifecycle state machine.
///
/// <para>M1 ships the minimal Hag-derived form (vanilla `Agent_Hag.json`): pure
/// move-to-environment actions gated by hour windows — appear at the home env during active
/// hours, park in the Spirit World outside them. An always-active species gets a single
/// fire-once appear action. The full duck-derived machine (deaths, carcass drops, population
/// writes, respawn tiers, winter despawn) lands in M2 and lives here too, keeping the fragile
/// part in one place.</para>
///
/// <para>Vanilla enum facts (research doc §1.2/§6.2): NPCActionRepeatOptions
/// {OnlyOnce=0, Repeat=1, ResetWhenConditionsAreFalse=2, OnlyWhenTriggered=3};
/// DaysOrHours.HourIs=0 with hour windows wrap-around capable ([Start, End)).</para>
/// </summary>
internal static class LifecycleTemplateBuilder
{
    public static NPCAction[] BuildMinimalLifecycle(AnimalManifest m, CardData homeEnv, CardData spiritWorld)
    {
        if (!m.HasActivityWindow)
        {
            // Always-active species: one fire-once teleport out of the Spirit World.
            return new[]
            {
                NewAction($"csffmfw_{m.SpeciesId}_appear", "Appear at home range", homeEnv,
                    repeatOptions: 0 /* OnlyOnce */, window: null),
            };
        }

        // Windowed species: two complementary ResetWhenConditionsAreFalse actions. Both windows
        // are always emitted together so the agent has a deterministic handoff in each direction
        // (the engine keeps running an out-of-window duty/action state otherwise — research §6.3).
        return new[]
        {
            NewAction($"csffmfw_{m.SpeciesId}_appear", "Move to home range for active hours", homeEnv,
                repeatOptions: 2 /* ResetWhenConditionsAreFalse */, window: (m.ActiveStart, m.ActiveEnd)),
            NewAction($"csffmfw_{m.SpeciesId}_retire", "Retire to the Spirit World outside active hours", spiritWorld,
                repeatOptions: 2, window: (m.ActiveEnd, m.ActiveStart)),
        };
    }

    private static NPCAction NewAction(string actionId, string debugText, CardData destination,
        int repeatOptions, (int Start, int End)? window)
    {
        var action = new NPCAction
        {
            ActionID = actionId,
            ActionLocalizedName = new LocalizedString
            {
                ParentObjectID = "",
                LocalizationKey = "IGNOREKEY",
                DefaultText = debugText,
            },
            ActionPopupText = new LocalizedString { ParentObjectID = "", LocalizationKey = "", DefaultText = "" },
            RepeatOptions = (NPCActionRepeatOptions)repeatOptions,
            CanBePerformedDuringTriggeredActions = new List<string>(),
            Conditions = AnimalAssetFactory.EmptyCondition(),
            StatModifications = Array.Empty<StatModifier>(),
            NPCStatModifications = Array.Empty<NPCStatInstantModifier>(),
            DroppedCards = Array.Empty<CardsDropCollection>(),
            InventoryActions = Array.Empty<NPCInventoryAction>(),
            MoveToEnvironment = destination,
            MoveTiming = 0,
            SetNPCHome = Array.Empty<NPCChangeHome>(),
            TriggerHidingGroups = Array.Empty<NPCHidingGroup>(),
        };

        if (window is { } w)
        {
            var condition = action.Conditions;
            AnimalAssetFactory.SetConditionTimes(ref condition, w.Start, w.End);
            action.Conditions = condition;
        }

        Log.Debug($"Animals: built lifecycle action '{actionId}' → {destination.name}"
                + (window is { } win ? $" (hours {win.Start}-{win.End})" : " (unconditional)"));
        return action;
    }
}
