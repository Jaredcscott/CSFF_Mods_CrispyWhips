using CSFFModFramework.Data;
using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// NPCDuty + NPCDutyAction factory — the M2 keystone. Vanilla duty ActionSequences are binary
/// Unity sub-assets (not JSON-authorable); this builder CreateInstances the plain-SO
/// NPCDutyAction subclasses (research §2.7: public fields, no lifecycle init), assembles
/// <c>NPCDuty.ActionSequence</c>, and appends an <c>NPCDutyRef</c> to the agent's
/// <c>AgentDuties</c>. Compiles both the generator's Movement/ActivityWindow duties and the
/// manifest's raw <c>CustomDuties</c> DSL (Move | Wait | StartEncounter in v1;
/// AffectItems is validated-out until the M4 feed duty lands).
///
/// <para>Timing law: everything here runs in the LoadOrchestrator phase (before
/// <c>GameManager.Awake</c>), so <c>CreateDutiesDict()</c> — built per-NPC during Awake's
/// CreateNPC — always sees the final AgentDuties array. Never call this after
/// OnGMInitialized without re-invoking CreateDutiesDict on the live NPC.</para>
///
/// <para>Generator invariants (each learned from a multi-session owl bug):
/// duty actions never carry NPCStatModifications that can zero a gate stat (the
/// move-before-drop race — stat writes belong to <see cref="AnimalLifecycleTicker"/>);
/// every array field is materialized non-null; duty selection is gated on the species'
/// exists/blood stats so a dead or retired agent never gets a move duty selected.</para>
/// </summary>
internal static class DutyBuilder
{
    /// <summary>Resolved per-species stat context the generated duties gate on. Either the
    /// hand-authored stats named by the manifest's Agent.Stats map (Ref path) or the
    /// generated/shared instances wired by AnimalAssetFactory (generated path).</summary>
    internal sealed class SpeciesStats
    {
        public NPCStat Exists;
        public NPCStat Blood;
        public NPCStat RespawnTimer;
        public NPCStat SuppressRespawnTimer;
        /// <summary>Blood at or below this value counts as dead (matches the generated/owl
        /// death action's 0–4 trigger band).</summary>
        public const float DeadBloodMax = 4f;
    }

    /// <summary>Builds every generated + custom duty for the species and appends them to
    /// <paramref name="agent"/>.AgentDuties (idempotent by duty UniqueID). Returns the number
    /// of duties attached.</summary>
    public static int AttachDuties(NPCAgent agent, AnimalManifest m, SpeciesStats stats, CardData roostEnv)
    {
        var refs = new List<NPCDutyRef>();

        // --- seek-player duty (Movement.PlayerAttraction) -------------------------------
        if (m.PlayerAttractionEnabled || m.FleeFromPlayer)
        {
            var uid = AnimalUid.For(m.SpeciesId, AnimalUid.PartDutySeekPlayer);
            var duty = NewDuty(uid, $"csffmfw_{m.SpeciesId}_seekplayer",
                m.FleeFromPlayer ? "Avoiding the player" : "Seeking the player", m);
            if (duty != null)
            {
                if (m.PAOnlyDuringActiveHours && m.HasActivityWindow)
                    duty.ValidTimesOfDay = new[] { AnimalAssetFactory.MakeHourWindow(m.ActiveStart, m.ActiveEnd) };
                SetDutyGate(duty, stats);

                var move = NewAction<MoveDutyAction>($"csffmfw_{m.SpeciesId}_seekplayer_0_move");
                move.MovementType = m.PAMovementType == "Teleport"
                    ? MoveDutyAction.MovementTypes.Teleport
                    : MoveDutyAction.MovementTypes.Pathfind;
                move.MoveDestination = MoveDutyAction.MoveDutyOptions.MoveToPlayer;
                move.MoveAwayFromDestination = m.FleeFromPlayer;
                move.LeaveTracks = true;
                duty.ActionSequence = new NPCDutyAction[] { move };

                refs.Add(NewDutyRef(duty, m.PlayerAttractionBaseWeight, paDistanceWeight: m));
            }
        }

        // --- wander duty (Movement.WanderEnvs) ------------------------------------------
        if (m.WanderEnvs.Count > 0)
        {
            var envs = m.WanderEnvs
                .Select(GameRegistry.GetByUid<CardData>)
                .Where(e => e != null)
                .ToArray();
            if (envs.Length > 0)
            {
                var uid = AnimalUid.For(m.SpeciesId, AnimalUid.PartDutyWander);
                var duty = NewDuty(uid, $"csffmfw_{m.SpeciesId}_wander", "Wandering the home range", m);
                if (duty != null)
                {
                    if (m.HasActivityWindow)
                        duty.ValidTimesOfDay = new[] { AnimalAssetFactory.MakeHourWindow(m.ActiveStart, m.ActiveEnd) };
                    SetDutyGate(duty, stats);

                    var move = NewAction<MoveDutyAction>($"csffmfw_{m.SpeciesId}_wander_0_move");
                    move.MovementType = MoveDutyAction.MovementTypes.Pathfind;
                    move.MoveDestination = MoveDutyAction.MoveDutyOptions.MoveToSpecificEnvironment;
                    move.MoveToEnvironments = envs;
                    move.DestinationSelection = MoveDutyAction.TargetEnvSelectionOptions.Random;
                    move.IgnoreCurrentLocation = true;
                    move.LeaveTracks = true;
                    var wait = NewAction<WaitDutyAction>($"csffmfw_{m.SpeciesId}_wander_1_wait");
                    wait.WaitFor = new Vector2Int(2, 4);
                    duty.ActionSequence = new NPCDutyAction[] { move, wait };

                    refs.Add(NewDutyRef(duty, m.WanderWeight));
                }
            }
        }

        // --- roost duty (ActivityWindow off-window coverage) ----------------------------
        // Guarantees a valid selectable duty in the inactive window so the engine never keeps
        // running an out-of-window duty (research §6.3 pitfall). For an off-stage
        // (SpiritWorld) roost this is a pure Wait — the actual relocation is
        // AnimalLifecycleTicker's synchronous MoveNPC at the window edge (the Attempt-12
        // lesson: direct MoveNPC, not stat-writes-and-hope). An on-stage roost env adds a
        // teleport move to that env first.
        if (m.HasActivityWindow)
        {
            var uid = AnimalUid.For(m.SpeciesId, AnimalUid.PartDutyRoost);
            var duty = NewDuty(uid, $"csffmfw_{m.SpeciesId}_roost", "Roosting", m);
            if (duty != null)
            {
                duty.ValidTimesOfDay = new[] { AnimalAssetFactory.MakeHourWindow(m.ActiveEnd, m.ActiveStart) };
                SetDutyGate(duty, stats);

                var actions = new List<NPCDutyAction>();
                bool onStageRoost = roostEnv != null && m.Roost != "SpiritWorld";
                if (onStageRoost)
                {
                    var move = NewAction<MoveDutyAction>($"csffmfw_{m.SpeciesId}_roost_0_move");
                    move.MovementType = MoveDutyAction.MovementTypes.Teleport;
                    move.MoveDestination = MoveDutyAction.MoveDutyOptions.MoveToSpecificEnvironment;
                    move.MoveToEnvironments = new[] { roostEnv };
                    move.DestinationSelection = MoveDutyAction.TargetEnvSelectionOptions.Closest;
                    actions.Add(move);
                }
                var wait = NewAction<WaitDutyAction>($"csffmfw_{m.SpeciesId}_roost_{actions.Count}_wait");
                wait.WaitFor = new Vector2Int(4, 8);
                actions.Add(wait);
                duty.ActionSequence = actions.ToArray();

                refs.Add(NewDutyRef(duty, baseWeight: 10));
            }
        }

        // --- CustomDuties DSL ------------------------------------------------------------
        foreach (var custom in m.CustomDuties)
        {
            var uid = AnimalUid.For(m.SpeciesId, AnimalUid.PartDutyCustomPrefix + custom.Name);
            var duty = NewDuty(uid, $"csffmfw_{m.SpeciesId}_custom_{custom.Name}", custom.Name, m);
            if (duty == null) continue;

            if (custom.HasValidHours)
                duty.ValidTimesOfDay = new[] { AnimalAssetFactory.MakeHourWindow(custom.ValidStart, custom.ValidEnd) };
            SetDutyGate(duty, stats);

            var sequence = new List<NPCDutyAction>();
            for (int i = 0; i < custom.Actions.Count; i++)
            {
                var built = BuildDslAction(custom.Actions[i], $"csffmfw_{m.SpeciesId}_custom_{custom.Name}_{i}", m);
                if (built == null)
                {
                    sequence.Clear();
                    break;  // validator already rejected malformed actions; belt-and-braces
                }
                sequence.Add(built);
            }
            if (sequence.Count == 0)
            {
                Log.Warn($"Animals: {m.SourceFile}: CustomDuty '{custom.Name}' produced no actions — duty skipped");
                continue;
            }
            duty.ActionSequence = sequence.ToArray();

            refs.Add(NewDutyRef(duty, custom.BaseWeight));
        }

        if (refs.Count == 0) return 0;

        // Append idempotently (by duty UID) so a reload/second pass never duplicates entries.
        var existing = agent.AgentDuties ?? Array.Empty<NPCDutyRef>();
        var merged = new List<NPCDutyRef>(existing);
        int attached = 0;
        foreach (var dutyRef in refs)
        {
            if (merged.Any(r => r.TargetDuty != null && dutyRef.TargetDuty != null
                             && r.TargetDuty.UniqueID == dutyRef.TargetDuty.UniqueID))
                continue;
            merged.Add(dutyRef);
            attached++;
        }
        agent.AgentDuties = merged.ToArray();

        Log.Debug($"Animals: {m.SpeciesId}: {attached} generated dut(y/ies) attached ({agent.AgentDuties.Length} total on agent)");
        return attached;
    }

    // ------------------------------------------------------------------ pieces ---

    private static NPCDuty NewDuty(string uid, string soName, string displayText, AnimalManifest m)
    {
        var duty = ScriptableObject.CreateInstance<NPCDuty>();
        duty.name = soName;
        duty.hideFlags = HideFlags.DontUnloadUnusedAsset;
        if (!AnimalAssetFactory.SetUniqueId(duty, uid))
        {
            UnityEngine.Object.Destroy(duty);
            return null;
        }

        duty.DutyName = new LocalizedString { ParentObjectID = "", LocalizationKey = "IGNOREKEY", DefaultText = displayText };
        duty.ValidTimesOfDay = Array.Empty<InGameTimeCondition>();
        duty.CanOnlyPerformAtHome = false;
        duty.CannotPerformWhileHidden = false;
        duty.MaxPerformPerDay = 0;
        duty.DutyExecutionOptions = DutyExecutionOptions.Early;
        duty.GiveUpOptions = DutyGiveUpOptions.GiveUpInstantly;
        duty.ActionSequence = Array.Empty<NPCDutyAction>();

        SetPrivateCondition(duty, "DutyConditions", AnimalAssetFactory.EmptyCondition());
        SetPrivateCondition(duty, "WhenPerformedConditions", AnimalAssetFactory.EmptyCondition());

        AnimalAssetFactory.RegisterGenerated(duty, m);
        return duty;
    }

    private static T NewAction<T>(string soName) where T : NPCDutyAction
    {
        var action = ScriptableObject.CreateInstance<T>();
        action.name = soName;
        action.hideFlags = HideFlags.DontUnloadUnusedAsset;
        action.RequiredForSelectingDuty = false;

        // Null-array hygiene on the move subclass — the engine iterates these without guards.
        if (action is MoveDutyAction move)
        {
            move.MoveToEnvironments = Array.Empty<CardData>();
            move.MoveToTags = Array.Empty<CardTag>();
            move.MoveToItems = Array.Empty<CardOrTagRefWithDurabilities>();
            move.MoveCosts = Array.Empty<NPCStatInstantModifier>();
            move.CostRequirements = MoveDutyAction.CostsBehavior.IgnoreCost;
        }
        return action;
    }

    private static NPCDutyAction BuildDslAction(AnimalManifest.DutyAction a, string soName, AnimalManifest m)
    {
        switch (a.Type)
        {
            case "Move":
            {
                var move = NewAction<MoveDutyAction>(soName + "_move");
                move.MovementType = a.MovementType == "Teleport"
                    ? MoveDutyAction.MovementTypes.Teleport
                    : MoveDutyAction.MovementTypes.Pathfind;
                move.MoveAwayFromDestination = a.AwayFrom;
                move.LeaveTracks = a.LeaveTracks;
                move.DestinationSelection = a.Selection switch
                {
                    "Furthest" => MoveDutyAction.TargetEnvSelectionOptions.Furthest,
                    "Random" => MoveDutyAction.TargetEnvSelectionOptions.Random,
                    _ => MoveDutyAction.TargetEnvSelectionOptions.Closest,
                };
                switch (a.Destination)
                {
                    case "Player":
                        move.MoveDestination = MoveDutyAction.MoveDutyOptions.MoveToPlayer;
                        break;
                    case "Home":
                        move.MoveDestination = MoveDutyAction.MoveDutyOptions.MoveToHome;
                        break;
                    case "Env":
                        move.MoveDestination = MoveDutyAction.MoveDutyOptions.MoveToSpecificEnvironment;
                        var env = GameRegistry.GetByUid<CardData>(a.Env);
                        if (env == null)
                        {
                            Log.Warn($"Animals: {m.SourceFile}: Move action env '{a.Env}' not found — action skipped");
                            return null;
                        }
                        move.MoveToEnvironments = new[] { env };
                        break;
                    case "EnvTag":
                        move.MoveDestination = MoveDutyAction.MoveDutyOptions.MoveToEnvTag;
                        var tag = Database.GetTypedSO(typeof(CardTag), a.Tag) as CardTag;
                        if (tag == null)
                        {
                            Log.Warn($"Animals: {m.SourceFile}: Move action tag '{a.Tag}' not found — action skipped");
                            return null;
                        }
                        move.MoveToTags = new[] { tag };
                        break;
                    default:
                        Log.Warn($"Animals: {m.SourceFile}: Move action Destination '{a.Destination}' unsupported — action skipped");
                        return null;
                }
                return move;
            }
            case "Wait":
            {
                var wait = NewAction<WaitDutyAction>(soName + "_wait");
                wait.WaitFor = new Vector2Int(Math.Max(1, a.TicksMin), Math.Max(1, a.TicksMax));
                return wait;
            }
            case "StartEncounter":
            {
                if (GameRegistry.GetByUid(a.Encounter) is not Encounter encounter)
                {
                    Log.Warn($"Animals: {m.SourceFile}: StartEncounter '{a.Encounter}' not found — action skipped");
                    return null;
                }
                var start = NewAction<StartEncounterDutyAction>(soName + "_encounter");
                start.DroppedEncounter = encounter;
                start.SkipEncounterEvent = false;
                start.CanStartFromDifferentLocation = !a.RequirePlayerEnv;
                return start;
            }
            default:
                Log.Warn($"Animals: {m.SourceFile}: duty action type '{a.Type}' unsupported in this milestone — action skipped");
                return null;
        }
    }

    /// <summary>Gates duty *selection* on the species being alive and on-stage: exists in
    /// [1,1] and (when a blood stat is known) blood above the dead band. A dead, trapped, or
    /// retired agent's duties simply stop being selected — the exact fix that ended the owl's
    /// "dies repeatedly after respawning at Duck Point" loop.</summary>
    private static void SetDutyGate(NPCDuty duty, SpeciesStats stats)
    {
        var conditions = new List<NPCStatCondition>();
        if (stats?.Exists != null)
            conditions.Add(new NPCStatCondition
            {
                UseAssociatedAgent = true,
                TargetStat = stats.Exists,
                ConditionRange = new Vector2(1f, 1f),
            });
        if (stats?.Blood != null)
            conditions.Add(new NPCStatCondition
            {
                UseAssociatedAgent = true,
                TargetStat = stats.Blood,
                ConditionRange = new Vector2(SpeciesStats.DeadBloodMax + 1f, 999999f),
            });
        if (conditions.Count == 0) return;

        var condition = AnimalAssetFactory.EmptyCondition();
        condition.RequiredNPCStatValues = conditions.ToArray();
        SetPrivateCondition(duty, "DutyConditions", condition);
    }

    private static FieldInfo _dutyConditionsField;
    private static FieldInfo _whenPerformedField;

    private static void SetPrivateCondition(NPCDuty duty, string fieldName, GeneralCondition value)
    {
        var field = fieldName == "DutyConditions"
            ? _dutyConditionsField ??= AccessTools.Field(typeof(NPCDuty), "DutyConditions")
            : _whenPerformedField ??= AccessTools.Field(typeof(NPCDuty), "WhenPerformedConditions");
        if (field == null)
        {
            Log.Error($"Animals: NPCDuty.{fieldName} field not found — generated duties will be ungated");
            return;
        }
        field.SetValue(duty, value);
    }

    private static NPCDutyRef NewDutyRef(NPCDuty duty, int baseWeight, AnimalManifest paDistanceWeight = null)
    {
        var weights = new NPCDutyWeights
        {
            BaseWeight = baseWeight,
            CardWeightModifiers = Array.Empty<CardBasedDropChanceModifier>(),
            StatWeightModifiers = Array.Empty<StatBasedDropChanceModifier>(),
            AgentStatsWeightModifiers = Array.Empty<NPCStatBasedDropChanceModifier>(),
            TimeOfDayWeightModifiers = Array.Empty<TimeOfDayDropChanceModifier>(),
            DistanceToOthersWeightModifiers = Array.Empty<DistanceToAgentWeightModifier>(),
            DistanceToEnvTagWeightModifiers = Array.Empty<DistanceToTagWeightModifier>(),
            DistanceToItemWeightModifiers = Array.Empty<DistanceToItemWeightModifier>(),
        };

        if (paDistanceWeight is { HasPlayerAttractionDistanceWeight: true } m)
        {
            var modifier = new DistanceToAgentWeightModifier
            {
                Target = MakePlayerTarget(),
                DistanceRange = new Vector2((float)m.PADistanceMin, (float)m.PADistanceMax),
                InterpWeightRange = new Vector2Int(m.PAWeightNear, m.PAWeightFar),
                WhenOutOfRange = StatDropChanceModOutOfRange.Add0Weight,
            };
            weights.DistanceToOthersWeightModifiers = new[] { modifier };
        }

        return new NPCDutyRef
        {
            TargetDuty = duty,
            ActivatingMode = NPCDutyActiveSettings.ActivateAutomatically,
            StartActive = true,
            ActivatingConditions = AnimalAssetFactory.EmptyCondition(),
            PreferenceWeights = weights,
        };
    }

    private static FieldInfo _targetPlayerField;

    /// <summary>NPCTargetSelection with the private [SerializeField] TargetPlayer flag set —
    /// the wolf/Hollow "distance to player" weight target.</summary>
    private static NPCTargetSelection MakePlayerTarget()
    {
        _targetPlayerField ??= AccessTools.Field(typeof(NPCTargetSelection), "TargetPlayer");
        object boxed = new NPCTargetSelection { UsePathfinding = true };
        if (_targetPlayerField != null)
            _targetPlayerField.SetValue(boxed, true);
        else
            Log.Error("Animals: NPCTargetSelection.TargetPlayer field not found — player-distance duty weight will be inert");
        return (NPCTargetSelection)boxed;
    }
}
