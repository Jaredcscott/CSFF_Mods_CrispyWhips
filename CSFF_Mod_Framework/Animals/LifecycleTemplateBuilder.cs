using CSFFModFramework.Data;
using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// Builds the generated agent's AgentActions lifecycle state machine — the encapsulated
/// fragile part (vanilla gets its own equivalents wrong in several places; see the research
/// doc's vanilla-bug catalogue).
///
/// <para>M1 shipped the minimal Hag-derived form (hour-window appear/retire teleports).
/// M2 adds the death machinery, generated from the manifest: blood-zero death (drops the
/// Carcass card + extra drops where the agent died, then retires to the Spirit World) and
/// exists-zero parking. The respawn timers that complete the cycle live in
/// <see cref="AnimalLifecycleTicker"/>, NOT here — a deliberate invariant: an AgentAction
/// carrying both drop payloads and gate-stat writes re-enters CheckForActions mid-coroutine
/// and eats its own drops (the confirmed move-before-drop race), so generated actions never
/// combine the two.</para>
///
/// <para>Vanilla enum facts (research doc §1.2/§6.2): NPCActionRepeatOptions
/// {OnlyOnce=0, Repeat=1, ResetWhenConditionsAreFalse=2, OnlyWhenTriggered=3};
/// DaysOrHours.HourIs=0 with hour windows wrap-around capable ([Start, End));
/// MoveTiming 0 = move before drops, 1 = move after drops.</para>
/// </summary>
internal static class LifecycleTemplateBuilder
{
    public static NPCAction[] BuildLifecycle(AnimalManifest m, CardData homeEnv, CardData spiritWorld,
        DutyBuilder.SpeciesStats stats, CardData carcass)
    {
        var actions = new List<NPCAction>();

        // --- on-stage window actions -------------------------------------------------
        if (!m.HasActivityWindow)
        {
            // Always-active species: one fire-once teleport out of the Spirit World.
            actions.Add(NewAction($"csffmfw_{m.SpeciesId}_appear", "Appear at home range", homeEnv,
                repeatOptions: 0 /* OnlyOnce */, window: null, aliveGate: stats));
        }
        else
        {
            // Windowed species: two complementary ResetWhenConditionsAreFalse actions. Both
            // windows are always emitted together so the agent has a deterministic handoff in
            // each direction (the engine keeps running an out-of-window duty/action state
            // otherwise — research §6.3). The appear action additionally requires the agent
            // to be alive and existing, so a dead/retired agent stays parked.
            actions.Add(NewAction($"csffmfw_{m.SpeciesId}_appear", "Move to home range for active hours", homeEnv,
                repeatOptions: 2 /* ResetWhenConditionsAreFalse */, window: (m.ActiveStart, m.ActiveEnd),
                aliveGate: stats));
            actions.Add(NewAction($"csffmfw_{m.SpeciesId}_retire", "Retire to the roost outside active hours", spiritWorld,
                repeatOptions: 2, window: (m.ActiveEnd, m.ActiveStart)));
        }

        // --- death: blood in the dead band → drop carcass, then retire off-stage ------
        // MoveTiming 1 (move AFTER drops) so the carcass lands where the agent died.
        // No NPCStatModifications on this action — see the class doc invariant.
        if (stats?.Blood != null)
        {
            var death = NewAction($"csffmfw_{m.SpeciesId}_death", "Collapse and drop carcass", spiritWorld,
                repeatOptions: 1 /* Repeat */, window: null);
            death.MoveTiming = (NPCActionMoveTiming)1;   // move AFTER drops
            AddStatCondition(death, stats.Blood, 0f, DutyBuilder.SpeciesStats.DeadBloodMax);
            death.DroppedCards = BuildCarcassDrops(m, carcass);
            actions.Add(death);
        }

        // --- exists-zero parking: a retired agent (tamed, suppressed) goes off-stage --
        if (stats?.Exists != null)
        {
            var park = NewAction($"csffmfw_{m.SpeciesId}_park", "Retire to the Spirit World", spiritWorld,
                repeatOptions: 1 /* Repeat */, window: null);
            AddStatCondition(park, stats.Exists, 0f, 0f);
            actions.Add(park);
        }

        return actions.ToArray();
    }

    private static CardsDropCollection[] BuildCarcassDrops(AnimalManifest m, CardData carcass)
    {
        if (carcass == null) return Array.Empty<CardsDropCollection>();

        var drops = new List<CardDrop> { new(carcass, new Vector2Int(1, 1)) };
        foreach (var extra in m.CarcassExtraDrops)
        {
            var card = GameRegistry.GetByUid<CardData>(extra.Card);
            if (card == null)
            {
                Log.Warn($"Animals: {m.SourceFile}: Carcass.ExtraDrops card '{extra.Card}' not found — drop skipped");
                continue;
            }
            drops.Add(new CardDrop(card, new Vector2Int(Math.Max(1, extra.Min), Math.Max(1, extra.Max))));
        }

        var collection = new CardsDropCollection
        {
            CollectionName = $"{m.SpeciesId}_death_drops",
            CollectionWeight = 1,
        };
        _collectionDropsField ??= AccessTools.Field(typeof(CardsDropCollection), "DroppedCards");
        if (_collectionDropsField == null)
        {
            Log.Error("Animals: CardsDropCollection.DroppedCards field not found — generated death action drops nothing");
            return Array.Empty<CardsDropCollection>();
        }
        _collectionDropsField.SetValue(collection, drops.ToArray());
        return new[] { collection };
    }

    private static FieldInfo _collectionDropsField;

    private static void AddStatCondition(NPCAction action, NPCStat stat, float min, float max)
    {
        var condition = action.Conditions;
        var existing = condition.RequiredNPCStatValues ?? Array.Empty<NPCStatCondition>();
        var grown = new NPCStatCondition[existing.Length + 1];
        Array.Copy(existing, grown, existing.Length);
        grown[existing.Length] = new NPCStatCondition
        {
            UseAssociatedAgent = true,
            TargetStat = stat,
            ConditionRange = new Vector2(min, max),
        };
        condition.RequiredNPCStatValues = grown;
        action.Conditions = condition;
    }

    private static NPCAction NewAction(string actionId, string debugText, CardData destination,
        int repeatOptions, (int Start, int End)? window, DutyBuilder.SpeciesStats aliveGate = null)
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

        if (aliveGate?.Exists != null)
            AddStatCondition(action, aliveGate.Exists, 1f, 1f);
        if (aliveGate?.Blood != null)
            AddStatCondition(action, aliveGate.Blood, DutyBuilder.SpeciesStats.DeadBloodMax + 1f, 999999f);

        Log.Debug($"Animals: built lifecycle action '{actionId}' → {(destination != null ? destination.name : "(none)")}"
                + (window is { } win ? $" (hours {win.Start}-{win.End})" : " (unconditional)"));
        return action;
    }
}
