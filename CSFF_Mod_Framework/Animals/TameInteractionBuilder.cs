using CSFFModFramework.Data;
using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// M6 — compiles manifest <c>Interactions</c> (schema: Documentation/Design/Animals_Schema.md
/// §Interactions) into a real <see cref="CardOnCardAction"/> (bait present — drag onto the
/// agent, the proven owl vanilla path: <c>NPCAgent/Agent_WildOwl.json</c>'s hand-authored
/// "Attempt to Tame") or <see cref="DismantleCardAction"/> (no bait — click button) on
/// <paramref name="agent"/>. Runs on BOTH the generated and <c>Agent.Ref</c> paths.
///
/// <para><b>Success/fail is resolved entirely by the ENGINE's own native weighted
/// <see cref="CardsDropCollection"/> selection over <c>ProducedCards</c></b> — the same
/// mechanism vanilla already uses for skill-scaled forage/craft outcomes (root CLAUDE.md
/// §Vanilla Data Protection names <c>GameManager.GetCollectionDropsReport</c> as a method to
/// never PATCH; this builder only ever feeds it data, exactly like every other generator in
/// this codebase). No custom roll/RNG lives here: a Success collection carries
/// <c>StatsDropChanceModifiers</c> keyed on <c>Interactions[].Skill</c> (a
/// <see cref="StatBasedDropChanceModifier"/> with <c>WhenOutOfRange = UseMinMaxWeight</c> —
/// the smooth ramp that saturates at both ends of <c>SkillBonus.StatRange</c>, NOT
/// <c>Add0Weight</c>, which would collapse the bonus to zero above the configured skill cap);
/// an optional Fail-Attack collection carries the SAME <c>DroppedEncounter</c> mechanism
/// regular wildlife-ambush forage actions use, weighted by <c>OnFail.AttackChance</c> — so
/// "does a failed tame sometimes turn into a fight" needs no C# branch either.</para>
///
/// <para><b>Field-population scope, deliberately minimal.</b> <c>Agent_WildOwl.json</c>'s own
/// "Attempt to Tame" CardOnCardAction — loaded through the SAME class, and confirmed working
/// in-game (Phase 0 acid test T2.29) — omits roughly two-thirds of <see cref="CardAction"/>'s
/// fields entirely (no <c>StatModifications</c>, <c>RequiredActiveHidingGroups</c>,
/// <c>SpawnNPCS</c>, <c>BlueprintsFullUnlock</c>, ...). Every vanilla consumer of those fields
/// (<c>CardsDropCollection</c>'s own weight-modifier readers, <c>CardStateChange</c>'s
/// interpolation appliers) null-guards before touching them. This builder mirrors that PROVEN
/// field set 1:1 rather than reflect-hygiening every array on <see cref="CardAction"/> — the
/// one exception is <see cref="AnimalAssetFactory.EmptyCondition"/>-style structs
/// (<see cref="CardStateChange"/>'s own array fields), populated explicitly below, matching
/// what the JSON authors as literal empty arrays rather than omitted keys.</para>
/// </summary>
internal static class TameInteractionBuilder
{
    /// <summary>Number of Interactions attached across all species this load. Reported once
    /// per load by <see cref="AnimalService"/>.</summary>
    public static int AppliedCount { get; private set; }

    public static void Reset() => AppliedCount = 0;

    public static void Apply(NPCAgent agent, AnimalManifest m, DutyBuilder.SpeciesStats stats, Encounter encounter = null)
    {
        if (agent == null || m == null || m.Interactions.Count == 0) return;

        foreach (var ix in m.Interactions)
        {
            var giveCard = GameRegistry.GetByUid<CardData>(ix.GiveCard);
            if (giveCard == null)
            {
                Log.Error($"Animals: {m.SourceFile}: Interaction '{ix.Name}': OnSuccess.GiveCard '{ix.GiveCard}' not found — interaction skipped");
                continue;
            }

            var producedCards = BuildProducedCards(m, ix, giveCard, encounter);
            if (producedCards.Length == 0)
            {
                Log.Error($"Animals: {m.SourceFile}: Interaction '{ix.Name}': no usable outcome collections — interaction skipped");
                continue;
            }

            bool attached = ix.HasBait
                ? AttachDragAction(agent, m, ix, producedCards)
                : AttachButtonAction(agent, m, ix, producedCards);

            if (attached)
            {
                AppliedCount++;
                Log.Info($"Animals: {m.SpeciesId}: Interaction '{ix.Name}' compiled -> "
                        + $"{(ix.HasBait ? "DragAndDropActions (bait)" : "DismantleActions (button)")}, "
                        + $"{producedCards.Length} outcome collection(s)");
            }
        }
    }

    // ------------------------------------------------------------ outcome collections ---

    /// <summary>Success (+ optional skill bonus), Fail-Passive, and (when
    /// <c>OnFail.AttackChance</c> > 0 and an encounter resolved) Fail-Attack. Each is its OWN
    /// <see cref="CardsDropCollection"/> so the engine's native weighted selection decides the
    /// outcome — never a hand-rolled RNG branch (TrapIntegrator precedent: "ONE COLLECTION PER
    /// DROP, not one collection holding every drop"). <paramref name="encounter"/> is whatever
    /// <see cref="EncounterBuilder.Resolve"/> already produced for this species (Ref OR generated,
    /// M5) — resolved ONCE by <c>AnimalAssetFactory.BuildAgent</c> and threaded through, never
    /// re-derived from <c>m.EncounterRef</c> alone (that would ignore a generated encounter
    /// entirely, and a second <c>Resolve</c> call would construct a wasteful orphaned duplicate
    /// SO under the same UID).</summary>
    private static CardsDropCollection[] BuildProducedCards(AnimalManifest m, AnimalManifest.Interaction ix, CardData giveCard, Encounter encounter)
    {
        var collections = new List<CardsDropCollection>();

        var success = NewCollection($"{m.SpeciesId}_{ix.Name}_success", ix.SuccessWeight);
        SetDroppedCards(success, new[]
        {
            new CardDrop(giveCard, new Vector2Int(1, 1))
            {
                DropChance = new CardDropChance
                {
                    Active = true,
                    BaseDropChance = 100f,
                    DurabilitiesModifiers = Array.Empty<DurabilityInterpolatedValue>(),
                    StatsModifiers = Array.Empty<StatInterpolatedValue>(),
                    CardsModifiers = Array.Empty<CardOrTagIndividualDropChanceModifier>(),
                    NPCStatsModifiers = Array.Empty<NPCStatInterpolatedValue>(),
                },
                DropsDurabilityMultipliers = Array.Empty<DurabilityInterpolatedValue>(),
                DropsStatMultipliers = Array.Empty<StatInterpolatedValue>(),
                AddedFlavours = Array.Empty<FlavourAndIntensitySetup>(),
                AddedSpiceTags = Array.Empty<SpiceTag>(),
            },
        });

        if (ix.HasSkillBonus)
        {
            var skillStat = ResolveSkill(ix.Skill);
            if (skillStat != null)
            {
                success.StatsDropChanceModifiers = new[]
                {
                    new StatBasedDropChanceModifier
                    {
                        Stat = skillStat,
                        StatRange = new Vector2((float)ix.SkillStatMin, (float)ix.SkillStatMax),
                        InterpWeightRange = new Vector2Int(ix.SkillWeightMin, ix.SkillWeightMax),
                        // Saturates at InterpWeightRange.x/.y outside StatRange rather than
                        // collapsing to 0 — see class doc comment.
                        WhenOutOfRange = StatDropChanceModOutOfRange.UseMinMaxWeight,
                    },
                };
            }
            else
            {
                Log.Warn($"Animals: {m.SourceFile}: Interaction '{ix.Name}': SkillBonus.Skill '{ix.Skill}' not found — success chance is flat SuccessWeight vs FailWeight, no skill scaling");
            }
        }
        collections.Add(success);

        double attackChance = Math.Max(0, Math.Min(100, ix.AttackChance));
        Encounter failEncounter = attackChance > 0 ? encounter : null;
        if (attackChance > 0 && failEncounter == null)
            Log.Warn($"Animals: {m.SourceFile}: Interaction '{ix.Name}': OnFail.AttackChance > 0 but no Encounter (Ref or generated) resolved for this species — every fail is passive");

        if (failEncounter != null)
        {
            int attackWeight = Math.Max(1, (int)Math.Round(ix.FailWeight * attackChance / 100.0));
            int passiveWeight = Math.Max(0, ix.FailWeight - attackWeight);

            var failAttack = NewCollection($"{m.SpeciesId}_{ix.Name}_fail_attack", attackWeight);
            failAttack.DroppedEncounter = failEncounter;
            failAttack.EncounterNPC = null;
            failAttack.SkipEncounterEvent = false;
            SetDroppedCards(failAttack, Array.Empty<CardDrop>());
            collections.Add(failAttack);

            if (passiveWeight > 0)
            {
                var failPassive = NewCollection($"{m.SpeciesId}_{ix.Name}_fail", passiveWeight);
                SetDroppedCards(failPassive, Array.Empty<CardDrop>());
                collections.Add(failPassive);
            }
        }
        else
        {
            var fail = NewCollection($"{m.SpeciesId}_{ix.Name}_fail", ix.FailWeight);
            SetDroppedCards(fail, Array.Empty<CardDrop>());
            collections.Add(fail);
        }

        return collections.ToArray();
    }

    private static GameStat ResolveSkill(string skillRef)
    {
        if (string.IsNullOrEmpty(skillRef)) return null;
        return GameRegistry.GetByUid<GameStat>(skillRef)
            ?? Database.GetTypedSO(typeof(GameStat), skillRef) as GameStat;
    }

    private static CardsDropCollection NewCollection(string name, int weight) => new()
    {
        CollectionName = name,
        CountsAsSuccess = true,
        RevealInventory = false,
        CollectionUses = new Vector2Int(1, 1),
        CollectionWeight = Math.Max(1, weight),
        StatsDropChanceModifiers = Array.Empty<StatBasedDropChanceModifier>(),
        NPCStatsDropChanceModifiers = Array.Empty<NPCStatBasedDropChanceModifier>(),
        CardDropChanceModifiers = Array.Empty<CardBasedDropChanceModifier>(),
        StatModifications = Array.Empty<ConditionalStatModifier>(),
    };

    private static FieldInfo _droppedCardsField;

    private static void SetDroppedCards(CardsDropCollection collection, CardDrop[] drops)
    {
        _droppedCardsField ??= AccessTools.Field(typeof(CardsDropCollection), "DroppedCards");
        if (_droppedCardsField == null)
        {
            Log.Error("Animals: CardsDropCollection.DroppedCards field not found — Interaction outcomes will be empty");
            return;
        }
        _droppedCardsField.SetValue(collection, drops);
    }

    // ------------------------------------------------------------------- drag (bait) ---

    private static bool AttachDragAction(NPCAgent agent, AnimalManifest m, AnimalManifest.Interaction ix, CardsDropCollection[] producedCards)
    {
        var baitCards = new List<CardData>();
        foreach (var uid in ix.BaitCards)
        {
            var card = GameRegistry.GetByUid<CardData>(uid);
            if (card != null) baitCards.Add(card);
            else Log.Warn($"Animals: {m.SourceFile}: Interaction '{ix.Name}': BaitCards '{uid}' not found — omitted");
        }
        var baitTags = new List<CardTag>();
        foreach (var name in ix.BaitTags)
        {
            var tag = Database.GetTypedSO(typeof(CardTag), name) as CardTag;
            if (tag != null) baitTags.Add(tag);
            else Log.Warn($"Animals: {m.SourceFile}: Interaction '{ix.Name}': BaitTags '{name}' not found — omitted");
        }
        if (baitCards.Count == 0 && baitTags.Count == 0)
        {
            Log.Error($"Animals: {m.SourceFile}: Interaction '{ix.Name}': every BaitCards/BaitTags entry failed to resolve — drag action skipped (nothing can trigger it)");
            return false;
        }

        var action = new CardOnCardAction();
        PopulateCommon(action, m, ix, producedCards);
        SetDaytimeCost(action, ix.DaytimeCost);

        action.CompatibleCards = new CardInteractionTrigger { TriggerCards = baitCards.ToArray(), TriggerTags = baitTags.ToArray() };
        action.NOTCompatibleCards = new CardInteractionTrigger { TriggerCards = Array.Empty<CardData>(), TriggerTags = Array.Empty<CardTag>() };
        action.WorksInverted = false;
        action.WorksBothWays = false;
        action.CarryOverGivenCard = false;
        action.GivenCardChanges = EmptyStateChange(CardModifications.Destroy);
        action.ReceivingCardChanges = EmptyStateChange(CardModifications.None);
        action.CreatedLiquidInGivenCard = default;
        action.RequiredGivenDurabilities = default;
        action.RequiredGivenContainer = Array.Empty<CardData>();
        action.RequiredGivenContainerTag = Array.Empty<CardTag>();
        action.RequiredGivenContainerDurabilities = default;
        action.RequiredGivenLiquidContent = default;

        var existing = (agent.DragAndDropActions ?? Array.Empty<CardOnCardAction>()).ToList();
        if (existing.Any(a => a != null && string.Equals(a.ActionName.DefaultText, action.ActionName.DefaultText, StringComparison.Ordinal)))
        {
            Log.Debug($"Animals: {m.SpeciesId}: DragAndDropActions already carries '{action.ActionName.DefaultText}' — keeping the existing entry");
            return false;
        }
        existing.Add(action);
        agent.DragAndDropActions = existing.ToArray();
        return true;
    }

    // ----------------------------------------------------------------- button (no bait) ---

    private static bool AttachButtonAction(NPCAgent agent, AnimalManifest m, AnimalManifest.Interaction ix, CardsDropCollection[] producedCards)
    {
        var action = new DismantleCardAction();
        PopulateCommon(action, m, ix, producedCards);
        SetDaytimeCost(action, ix.DaytimeCost);

        // Bypasses CanAppear()'s WillHaveAnEffect gate: ReceivingCardChanges.ModType is None
        // on a pure "attempt" button (the outcome lives entirely in ProducedCards), which the
        // base gate does not treat as an effect on its own (root CLAUDE.md §DismantleAction
        // Visibility — the confirmed silent-invisible-button class).
        action.AlwaysShow = true;
        action.ReceivingCardChanges = EmptyStateChange(CardModifications.None);

        var existing = (agent.DismantleActions ?? Array.Empty<DismantleCardAction>()).ToList();
        if (existing.Any(a => a != null && string.Equals(a.ActionName.DefaultText, action.ActionName.DefaultText, StringComparison.Ordinal)))
        {
            Log.Debug($"Animals: {m.SpeciesId}: DismantleActions already carries '{action.ActionName.DefaultText}' — keeping the existing entry");
            return false;
        }
        existing.Add(action);
        agent.DismantleActions = existing.ToArray();
        return true;
    }

    // --------------------------------------------------------------------- shared bits ---

    private static void PopulateCommon(CardAction action, AnimalManifest m, AnimalManifest.Interaction ix, CardsDropCollection[] producedCards)
    {
        var displayName = ix.ActionName ?? ix.Name;
        action.ActionName = new LocalizedString { ParentObjectID = "", LocalizationKey = ix.LocalizationKey ?? "IGNOREKEY", DefaultText = displayName };
        action.ActionDescription = new LocalizedString { ParentObjectID = "", LocalizationKey = "", DefaultText = ix.ActionDescription ?? "" };
        action.VictorySettings = new VictoryCondition { Victory = false, SpecialEnding = false, VictoryMessage = new LocalizedString { ParentObjectID = "", LocalizationKey = "", DefaultText = "" } };
        action.CustomConfirmText = new LocalizedString { ParentObjectID = "", LocalizationKey = "", DefaultText = "" };
        action.NotInterruptedByEncounters = false;
        action.StackCompatible = false;
        action.UseMiniTicks = MiniTicksBehavior.DefaultBehavior;

        var handAction = Database.GetTypedSO(typeof(ActionTag), "HandAction") as ActionTag;
        action.ActionTags = handAction != null ? new[] { handAction } : Array.Empty<ActionTag>();

        action.RequiredStatValues = Array.Empty<StatValueTrigger>();
        action.RequiredNPCStatValues = Array.Empty<NPCStatCondition>();
        action.RequiredCardsOnBoard = Array.Empty<CardOnBoardCondition>();
        action.RequiredTagsOnBoard = Array.Empty<TagOnBoardCondition>();
        action.RequiredAlreadyVisitedEnvironments = Array.Empty<CardData>();
        action.RequiredReceivingContainer = Array.Empty<CardData>();
        action.RequiredReceivingContainerTag = Array.Empty<CardTag>();
        action.RequiredReceivingContainerDurabilities = default;
        action.RequiredReceivingDurabilities = default;
        action.RequiredReceivingLiquidContent = default;
        action.CompatibleNPCDuties = Array.Empty<NPCDutyOrDutyTagRef>();

        action.ProducedCards = producedCards;

        SetActionSounds(action);
    }

    private static CardStateChange EmptyStateChange(CardModifications modType) => new()
    {
        ModType = modType,
        AddedFlavours = Array.Empty<FlavourAndIntensitySetup>(),
        AddedSpiceTags = Array.Empty<SpiceTag>(),
        StatInterpolatedDurabilityChanges = Array.Empty<StatInterpolatedDurabilityModifier>(),
        DurabilityInterpolatedTransfers = Array.Empty<DurabilityInterpolatedDurabilityModifier>(),
    };

    private static FieldInfo _daytimeCostField;
    private static FieldInfo _actionSoundsField;

    private static void SetDaytimeCost(CardAction action, int value)
    {
        _daytimeCostField ??= AccessTools.Field(typeof(CardAction), "DaytimeCost");
        if (_daytimeCostField == null)
        {
            Log.Error("Animals: CardAction.DaytimeCost field not found — Interaction daytime cost will read 0");
            return;
        }
        _daytimeCostField.SetValue(action, value);
    }

    private static void SetActionSounds(CardAction action)
    {
        _actionSoundsField ??= AccessTools.Field(typeof(CardAction), "ActionSounds");
        _actionSoundsField?.SetValue(action, Array.Empty<AudioClip>());
    }
}
