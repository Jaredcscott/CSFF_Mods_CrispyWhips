using System.Text.RegularExpressions;
using CSFFModFramework.Data;

namespace CSFFModFramework.Animals;

/// <summary>
/// Collect-all-errors validation for one manifest. Returns every problem (field path +
/// expected/actual) rather than failing fast; an empty list means the species is accepted.
/// Runs after WarpResolver, so UID references resolve against the live registry.
/// </summary>
internal static class AnimalValidator
{
    private static readonly Regex SpeciesIdPattern = new("^[a-z0-9_]+$", RegexOptions.Compiled);
    private static readonly Regex DutyNamePattern = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    private static readonly HashSet<string> StatMapRoles = new(StringComparer.Ordinal)
        { "Exists", "Blood", "RespawnTimer", "SuppressRespawnTimer" };

    public static List<string> Validate(AnimalManifest m, Dictionary<string, string> seenSpecies)
    {
        var errors = new List<string>();

        if (m.SchemaVersion == -1)
            errors.Add("Field 'SchemaVersion': required, missing");
        else if (m.SchemaVersion > AnimalManifest.SupportedSchemaVersion)
            errors.Add($"Field 'SchemaVersion': {m.SchemaVersion} requires a newer CSFFModFramework (this build supports {AnimalManifest.SupportedSchemaVersion})");
        else if (m.SchemaVersion < 1)
            errors.Add($"Field 'SchemaVersion': expected {AnimalManifest.SupportedSchemaVersion}, got {m.SchemaVersion}");

        if (string.IsNullOrEmpty(m.SpeciesId))
            errors.Add("Field 'SpeciesId': required, missing");
        else if (!SpeciesIdPattern.IsMatch(m.SpeciesId))
            errors.Add($"Field 'SpeciesId': expected [a-z0-9_]+, got \"{m.SpeciesId}\"");
        else if (seenSpecies.TryGetValue(m.SpeciesId, out var firstFile))
            errors.Add($"Field 'SpeciesId': \"{m.SpeciesId}\" already declared by {firstFile}");

        ValidateAgent(m, errors);
        ValidateActivityWindow(m, errors);
        ValidateMovement(m, errors);
        ValidateSpawnLifecycle(m, errors);
        ValidateCarcass(m, errors);
        ValidateTracks(m, errors);
        ValidateTraps(m, errors);
        ValidateEncounter(m, errors);
        ValidateInteractions(m, errors);
        ValidateCompanion(m, errors);
        ValidateCustomDuties(m, errors);
        ValidateHourCoverage(m, errors);

        return errors;
    }

    private static void ValidateAgent(AnimalManifest m, List<string> errors)
    {
        if (m.AgentRef != null)
        {
            // Escape hatch: hand-authored NPCAgent — the manifest only fills the gaps
            // (spawn registration, generated duties, lifecycle ticker).
            if (GameRegistry.GetByUid(m.AgentRef) is not NPCAgent)
                errors.Add($"Field 'Agent.Ref': \"{m.AgentRef}\" does not resolve to a loaded NPCAgent");

            if (m.AgentStatMap != null)
            {
                foreach (var kv in m.AgentStatMap)
                {
                    if (!StatMapRoles.Contains(kv.Key))
                        errors.Add($"Field 'Agent.Stats.{kv.Key}': unknown role (expected one of {string.Join("/", StatMapRoles)})");
                    else if (GameRegistry.GetByUid<NPCStat>(kv.Value) == null)
                        errors.Add($"Field 'Agent.Stats.{kv.Key}': \"{kv.Value}\" does not resolve to a loaded NPCStat");
                }
            }

            // Generated duties gate on the exists stat; without it a dead/retired agent's
            // move duties keep getting selected (the owl's "dies repeatedly" loop class).
            bool wantsDuties = m.PlayerAttractionEnabled || m.FleeFromPlayer
                || m.WanderEnvs.Count > 0 || m.CustomDuties.Count > 0;
            if (wantsDuties && (m.AgentStatMap == null || !m.AgentStatMap.ContainsKey("Exists")))
                errors.Add("Field 'Agent.Stats.Exists': required when a Ref-path manifest requests generated duties (Movement/CustomDuties) — duties must gate on the agent's exists stat");
        }
        else
        {
            if (string.IsNullOrEmpty(m.DisplayName))
                errors.Add("Field 'DisplayName': required (generated agents need a player-facing name)");

            if (string.IsNullOrEmpty(m.HomeEnv))
                errors.Add("Field 'Spawn.HomeEnv': required, missing");
            else if (GameRegistry.GetByUid<CardData>(m.HomeEnv) is not { } env)
                errors.Add($"Field 'Spawn.HomeEnv': \"{m.HomeEnv}\" does not resolve to a CardData");
            else if ((int)env.CardType != 4)
                errors.Add($"Field 'Spawn.HomeEnv': \"{env.name}\" is CardType {(int)env.CardType}, expected 4 (CT4 environment)");

            if (m.AgentStatMap != null)
                errors.Add("Field 'Agent.Stats': only valid with 'Agent.Ref' (generated agents wire their own stats)");
        }
    }

    private static void ValidateActivityWindow(AnimalManifest m, List<string> errors)
    {
        if (!m.HasActivityWindow)
        {
            if (m.HiddenWhileRoosting)
                errors.Add("Field 'ActivityWindow.HiddenWhileRoosting': requires ActivityWindow.ActiveHours");
            return;
        }

        if (m.ActiveStart is < 0 or > 23)
            errors.Add($"Field 'ActivityWindow.ActiveHours.Start': expected 0-23, got {m.ActiveStart}");
        if (m.ActiveEnd is < 0 or > 23)
            errors.Add($"Field 'ActivityWindow.ActiveHours.End': expected 0-23, got {m.ActiveEnd}");
        if (m.ActiveStart == m.ActiveEnd)
            errors.Add("Field 'ActivityWindow.ActiveHours': Start == End is a zero-length window (omit ActivityWindow for an always-active species)");

        if (m.Roost != "SpiritWorld")
        {
            if (GameRegistry.GetByUid<CardData>(m.Roost) is not { } roost)
                errors.Add($"Field 'ActivityWindow.Roost': \"{m.Roost}\" is neither \"SpiritWorld\" nor a loaded CardData UID");
            else if ((int)roost.CardType != 4)
                errors.Add($"Field 'ActivityWindow.Roost': \"{roost.name}\" is CardType {(int)roost.CardType}, expected 4 (CT4 environment)");
        }
    }

    private static void ValidateMovement(AnimalManifest m, List<string> errors)
    {
        if (!m.HasMovement) return;

        if (m.FleeFromPlayer && m.PlayerAttractionEnabled)
            errors.Add("Field 'Movement': FleeFromPlayer and PlayerAttraction.Enabled are mutually exclusive");

        if (m.PAMovementType is not ("Pathfind" or "Teleport"))
            errors.Add($"Field 'Movement.PlayerAttraction.MovementType': expected Pathfind|Teleport, got \"{m.PAMovementType}\"");

        for (int i = 0; i < m.WanderEnvs.Count; i++)
        {
            if (GameRegistry.GetByUid<CardData>(m.WanderEnvs[i]) is not { } env)
                errors.Add($"Field 'Movement.WanderEnvs[{i}]': \"{m.WanderEnvs[i]}\" does not resolve to a CardData");
            else if ((int)env.CardType != 4)
                errors.Add($"Field 'Movement.WanderEnvs[{i}]': \"{env.name}\" is CardType {(int)env.CardType}, expected 4 (CT4 environment)");
        }

        if ((m.PlayerAttractionEnabled || m.FleeFromPlayer) && m.PlayerAttractionBaseWeight <= 0
            && !m.HasPlayerAttractionDistanceWeight)
            errors.Add("Field 'Movement.PlayerAttraction': BaseWeight <= 0 with no DistanceWeight — the duty would never be selected");
    }

    private static void ValidateSpawnLifecycle(AnimalManifest m, List<string> errors)
    {
        if (m.DeathRespawnTicks < 0)
            errors.Add($"Field 'Spawn.DeathRespawnTicks': expected >= 0, got {m.DeathRespawnTicks}");
        if (m.SuppressedRespawnTicks < 0)
            errors.Add($"Field 'Spawn.SuppressedRespawnTicks': expected >= 0, got {m.SuppressedRespawnTicks}");

        if (m.SuppressWhileCardOnBoard != null
            && GameRegistry.GetByUid<CardData>(m.SuppressWhileCardOnBoard) == null)
            errors.Add($"Field 'Spawn.SuppressWhileCardOnBoard': \"{m.SuppressWhileCardOnBoard}\" does not resolve to a CardData");

        if (m.AgentRef != null && m.DeathRespawnTicks > 0)
        {
            if (m.AgentStatMap == null || !m.AgentStatMap.ContainsKey("Blood") || !m.AgentStatMap.ContainsKey("RespawnTimer"))
                errors.Add("Field 'Spawn.DeathRespawnTicks': on the Agent.Ref path this requires Agent.Stats.Blood and Agent.Stats.RespawnTimer");
        }
        if (m.AgentRef != null && m.SuppressWhileCardOnBoard != null)
        {
            if (m.AgentStatMap == null || !m.AgentStatMap.ContainsKey("Exists") || !m.AgentStatMap.ContainsKey("SuppressRespawnTimer"))
                errors.Add("Field 'Spawn.SuppressWhileCardOnBoard': on the Agent.Ref path this requires Agent.Stats.Exists and Agent.Stats.SuppressRespawnTimer");
        }
    }

    private static void ValidateCarcass(AnimalManifest m, List<string> errors)
    {
        if (m.CarcassCard != null && GameRegistry.GetByUid<CardData>(m.CarcassCard) == null)
            errors.Add($"Field 'Carcass.Card': \"{m.CarcassCard}\" does not resolve to a CardData");

        for (int i = 0; i < m.CarcassExtraDrops.Count; i++)
        {
            var drop = m.CarcassExtraDrops[i];
            if (string.IsNullOrEmpty(drop.Card))
                errors.Add($"Field 'Carcass.ExtraDrops[{i}].Card': required, missing");
            else if (GameRegistry.GetByUid<CardData>(drop.Card) == null)
                errors.Add($"Field 'Carcass.ExtraDrops[{i}].Card': \"{drop.Card}\" does not resolve to a CardData");
            if (drop.Min < 0 || drop.Max < drop.Min)
                errors.Add($"Field 'Carcass.ExtraDrops[{i}].Amount': expected [min, max] with 0 <= min <= max, got [{drop.Min}, {drop.Max}]");
        }

        if (m.AgentRef != null && m.CarcassCard != null)
            errors.Add("Field 'Carcass': only valid without 'Agent.Ref' — a hand-authored agent's own death action defines its drops");
    }

    /// <summary>M3 Tracks. Three of these rules exist because the engine fails silently or
    /// crashes far from the manifest that caused it — see TrackBuilder's class doc-comment:
    /// a null WeightCategory NREs in the track card's description path once Skill_Tracking
    /// reaches 2; a 0 TrackLifetime means "never expires", not "expires now"; and BloodTrail
    /// needs a blood NPCStat that only the generated path wires automatically.</summary>
    private static void ValidateTracks(AnimalManifest m, List<string> errors)
    {
        if (!m.HasTracks || !m.TracksEnabled) return;

        if (m.TrackCard != null && GameRegistry.GetByUid<CardData>(m.TrackCard) == null)
            errors.Add($"Field 'Tracks.TrackCard': \"{m.TrackCard}\" does not resolve to a CardData");

        if (m.TrackLifetimeMin < 1)
            errors.Add($"Field 'Tracks.LifetimeTicks.Min': expected >= 1, got {m.TrackLifetimeMin} — the engine treats a rolled lifetime of 0 as \"never expires\", leaving an immortal pending track record in the environment's save data");
        if (m.TrackLifetimeMax < m.TrackLifetimeMin)
            errors.Add($"Field 'Tracks.LifetimeTicks': expected Min <= Max, got [{m.TrackLifetimeMin}, {m.TrackLifetimeMax}]");

        if (m.TrackRevealSkill != null
            && GameRegistry.GetByUid<GameStat>(m.TrackRevealSkill) == null
            && Database.GetTypedSO(typeof(GameStat), m.TrackRevealSkill) == null)
            errors.Add($"Field 'Tracks.RevealSkill': \"{m.TrackRevealSkill}\" does not resolve to a GameStat (UID or runtime name)");

        if (m.HasTrackRevealCurve && m.TrackRevealStatMin >= m.TrackRevealStatMax)
            errors.Add($"Field 'Tracks.RevealSkillCurve.StatRange': expected Min < Max, got [{m.TrackRevealStatMin}, {m.TrackRevealStatMax}]");

        // Blood trails snapshot the agent's blood NPCStat. The generated path always wires one;
        // the Ref path only knows about it through Agent.Stats.Blood.
        if (m.TrackBloodTrail && m.AgentRef != null
            && (m.AgentStatMap == null || !m.AgentStatMap.ContainsKey("Blood")))
            errors.Add("Field 'Tracks.BloodTrail': on the Agent.Ref path this requires Agent.Stats.Blood (the stat snapshotted onto the track card's Special1)");

        // Weight category: LocalizedString.TrackingWeight(agent.WeightCategory) dereferences
        // CategoryName with no null guard, and NPCTrackingInfo.ToDescription calls it as soon as
        // the player's Skill_Tracking clears the Weight info step (2 in vanilla). A track-leaving
        // agent without one is a NullReferenceException waiting on the inspect-popup path.
        if (m.AgentRef != null)
        {
            if (GameRegistry.GetByUid(m.AgentRef) is NPCAgent refAgent && refAgent.WeightCategory == null)
                errors.Add($"Field 'Tracks.Enabled': the referenced agent \"{m.AgentRef}\" has no WeightCategory — a discovered track's description dereferences it (LocalizedString.TrackingWeight) once Skill_Tracking >= 2 and would throw. Set WeightCategoryWarpData/WeightCategoryWarpType on the agent JSON (Light|Medium|Heavy)");
        }
        else if (string.IsNullOrEmpty(m.WeightCategory))
        {
            errors.Add("Field 'WeightCategory': required when 'Tracks.Enabled' is true — a discovered track's description dereferences the agent's WeightCategory (LocalizedString.TrackingWeight) once Skill_Tracking >= 2 and would throw. Expected a runtime AgentWeightCategory name (Light|Medium|Heavy)");
        }
        else if (Database.GetTypedSO(typeof(AgentWeightCategory), m.WeightCategory) == null)
        {
            errors.Add($"Field 'WeightCategory': \"{m.WeightCategory}\" is not a loaded AgentWeightCategory (expected Light|Medium|Heavy) — required while 'Tracks.Enabled' is true (see above)");
        }
    }

    /// <summary>M4 Traps. These rules exist because the failure mode of each is a silently
    /// non-functional trap rather than an error: a species joined to a vanilla trap's interactor
    /// list with no catch result springs the trap and yields nothing, and a trap type named
    /// wrongly is simply never matched.</summary>
    private static void ValidateTraps(AnimalManifest m, List<string> errors)
    {
        if (!m.HasTraps || !m.TrapsEnabled) return;

        if (m.TrapWariness is < 0 or > 100)
            errors.Add($"Field 'Traps.Wariness': expected 0-100 (base AgentTrapCunning), got {m.TrapWariness}");

        var known = new[] { "Snare", "Deadfall", "LogTrap", "PitTrap" };
        foreach (var kv in m.TrapPerType)
        {
            if (!known.Contains(kv.Key, StringComparer.Ordinal))
                errors.Add($"Field 'Traps.PerType': \"{kv.Key}\" is not a land trap — expected one of {string.Join(" | ", known)} "
                         + "(FunnelTrap has no agent path and cannot be opted into)");
            if (kv.Value < 0)
                errors.Add($"Field 'Traps.PerType.{kv.Key}': expected >= 0 (0 = fully catchable, 10000 = immune), got {kv.Value}");
        }

        var catchable = m.TrapPerType.Where(kv => kv.Value < TrapIntegrator.ImmuneCunning).Select(kv => kv.Key).ToList();
        if (catchable.Count == 0)
            errors.Add("Field 'Traps.PerType': every trap type is absent or immune (>= 10000) — the species would opt into nothing. "
                     + "Omit the whole Traps section instead, or set at least one type below 10000");

        foreach (var result in m.CatchResults)
        {
            if (string.IsNullOrEmpty(result.TrapType))
            {
                errors.Add("Field 'Traps.CatchResults[].TrapType': required, missing");
                continue;
            }
            if (!known.Contains(result.TrapType, StringComparer.Ordinal))
                errors.Add($"Field 'Traps.CatchResults[].TrapType': \"{result.TrapType}\" is not one of {string.Join(" | ", known)}");
            if (result.Drops.Count == 0)
                errors.Add($"Field 'Traps.CatchResults[{result.TrapType}].Drops': required, empty — a catch with no drop leaves an empty sprung trap");

            foreach (var drop in result.Drops)
            {
                if (string.IsNullOrEmpty(drop.Card))
                    errors.Add($"Field 'Traps.CatchResults[{result.TrapType}].Drops[].Card': required, missing");
                else if (GameRegistry.GetByUid<CardData>(drop.Card) == null)
                    errors.Add($"Field 'Traps.CatchResults[{result.TrapType}].Drops[].Card': \"{drop.Card}\" does not resolve to a CardData");
                if (drop.Weight <= 0)
                    errors.Add($"Field 'Traps.CatchResults[{result.TrapType}].Drops[].Weight': expected > 0 (a zero-weight collection is never selected), got {drop.Weight}");
            }
        }

        // Every trap the species can actually be caught by needs a result, or that trap springs
        // and produces nothing.
        foreach (var trapType in catchable)
            if (!m.CatchResults.Any(r => string.Equals(r.TrapType, trapType, StringComparison.Ordinal)))
                errors.Add($"Field 'Traps.CatchResults': PerType makes the species catchable by \"{trapType}\" but no CatchResults entry "
                         + "covers it — the trap would spring, consume the animal and drop nothing");

        foreach (var tagName in m.BaitTags)
            if (Database.GetTypedSO(typeof(CardTag), tagName) == null)
                errors.Add($"Field 'Traps.BaitTags': \"{tagName}\" does not resolve to a loaded CardTag");
        foreach (var cardUid in m.BaitCards)
            if (GameRegistry.GetByUid<CardData>(cardUid) == null)
                errors.Add($"Field 'Traps.BaitCards': \"{cardUid}\" does not resolve to a CardData");

        if (m.FeedDutyWeight <= 0)
            errors.Add($"Field 'Traps.Bait.DutyWeight': expected > 0 (a zero-weight duty is never selected), got {m.FeedDutyWeight}");

        // A feed duty weighted below any concurrently-selectable movement duty is never selected
        // at all, and the trap can never fire. See HeaviestMovementDutyWeight for the mechanism.
        int heaviestRival = HeaviestMovementDutyWeight(m);
        if (heaviestRival > m.FeedDutyWeight)
            errors.Add($"Field 'Traps.Bait.DutyWeight': {m.FeedDutyWeight} is below this species' heaviest movement duty ({heaviestRival}). "
                     + "Duty selection is highest-weight-wins (ties broken uniformly), NOT weighted-random, so the feed duty would never be "
                     + "selected and no trap could ever fire. Set it >= the movement weight");

        // A trapped agent is retired via exists, never via blood, so the framework's kill-respawn
        // timer (which is keyed on blood) cannot bring it back. Today the only respawn path that
        // keys on exists is the suppressed-respawn one.
        if (m.SuppressWhileCardOnBoard == null)
            errors.Add("Field 'Spawn.SuppressWhileCardOnBoard': required when 'Traps' is enabled. A trapped agent is retired by setting "
                     + "exists to 0 (never by blood), and the kill-respawn timer keys on blood — so without the suppressed-respawn path "
                     + "the species is permanently retired the first time it is caught, with no log and no recovery");
    }

    /// <summary>M5 Encounter. Ref path: only resolution + the optional BodyTemplate override are
    /// checked. Generated path: BodyTemplate MUST resolve — EncounterPopup.GenerateEnemyWound
    /// dereferences EnemyBodyTemplate.Head/.Torso/... with NO null guard the first time the
    /// player lands a hit, so an unresolved name is a confirmed crash, not a cosmetic gap.
    /// Aggression needs SOME encounter (Ref or generated) to start.</summary>
    private static void ValidateEncounter(AnimalManifest m, List<string> errors)
    {
        if (m.EncounterRef != null)
        {
            if (GameRegistry.GetByUid(m.EncounterRef) is not Encounter)
                errors.Add($"Field 'Encounter.Ref': \"{m.EncounterRef}\" does not resolve to a loaded Encounter");
            if (m.EncounterBodyTemplate != null && Database.GetTypedSO(typeof(BodyTemplate), m.EncounterBodyTemplate) == null)
                errors.Add($"Field 'Encounter.BodyTemplate': \"{m.EncounterBodyTemplate}\" does not resolve to a loaded BodyTemplate by runtime name (overrides the Ref'd encounter's own template)");
        }
        else if (m.HasEncounterGeneration)
        {
            if (string.IsNullOrEmpty(m.EncounterBodyTemplate))
                errors.Add("Field 'Encounter.BodyTemplate': required when generating an encounter (no Ref) — EncounterPopup.GenerateEnemyWound dereferences it unconditionally on the first successful hit");
            else if (Database.GetTypedSO(typeof(BodyTemplate), m.EncounterBodyTemplate) == null)
                errors.Add($"Field 'Encounter.BodyTemplate': \"{m.EncounterBodyTemplate}\" does not resolve to a loaded BodyTemplate by runtime name (check Documentation/GameData/.../ScriptableObjectObjectName/BodyTemplate.txt for the exact spelling/spacing)");

            if (m.EncounterBlood <= 0)
                errors.Add($"Field 'Encounter.Blood': expected > 0, got {m.EncounterBlood}");
            if (m.EncounterSize <= 0)
                errors.Add($"Field 'Encounter.Size': expected > 0, got {m.EncounterSize}");
            if (m.EncounterAwarenessMin > m.EncounterAwarenessMax)
                errors.Add($"Field 'Encounter.Awareness': expected [min, max] with min <= max, got [{m.EncounterAwarenessMin}, {m.EncounterAwarenessMax}]");
            if (m.EncounterCoverMin > m.EncounterCoverMax)
                errors.Add($"Field 'Encounter.Cover': expected [min, max] with min <= max, got [{m.EncounterCoverMin}, {m.EncounterCoverMax}]");
            if (m.EncounterStealthMin > m.EncounterStealthMax)
                errors.Add($"Field 'Encounter.Stealth': expected [min, max] with min <= max, got [{m.EncounterStealthMin}, {m.EncounterStealthMax}]");
        }

        if (m.AggressionEnabled)
        {
            if (m.EncounterRef == null && !m.HasEncounterGeneration)
                errors.Add("Field 'Encounter.Aggression.Enabled': requires either 'Encounter.Ref' or generation fields — there is no encounter for the attack duty to start");
            if (m.HasAggressionHours && (m.AggressionStart is < 0 or > 23 || m.AggressionEnd is < 0 or > 23))
                errors.Add($"Field 'Encounter.Aggression.Hours': expected 0-23, got [{m.AggressionStart}, {m.AggressionEnd}]");
            if (m.AggressionBaseWeight <= 0)
                errors.Add($"Field 'Encounter.Aggression.BaseWeight': expected > 0 (a zero-weight duty is never selected), got {m.AggressionBaseWeight}");
            if (m.AggressionMaxPerDay < 0)
                errors.Add($"Field 'Encounter.Aggression.MaxPerDay': expected >= 0 (0 = unlimited), got {m.AggressionMaxPerDay}");

            // Same winner-take-all trap the feed duty hit in M4: an attack duty weighted below a
            // concurrently-selectable movement duty is never selected, so the species can never
            // attack. Silent both ways - the duty IS generated and IS eligible, it just always
            // loses the sort. Guarded here because the attack window normally sits inside the
            // activity window that gates the movement duties, so they compete every tick.
            int heaviestRival = HeaviestMovementDutyWeight(m);
            if (m.AggressionBaseWeight > 0 && heaviestRival > m.AggressionBaseWeight)
                errors.Add($"Field 'Encounter.Aggression.BaseWeight': {m.AggressionBaseWeight} is below this species' heaviest movement duty ({heaviestRival}). "
                         + "Duty selection is highest-weight-wins (ties broken uniformly), NOT weighted-random, so the attack duty would never be "
                         + "selected and the species could never attack. Set it >= the movement weight, or set 'Encounter.Aggression.Enabled': false");
        }
    }

    /// <summary>The heaviest weight among this species' concurrently-selectable MOVEMENT duties.
    /// Duty selection is WINNER-TAKE-ALL, not weighted-random: InGameNPC.SelectDuty sorts the
    /// eligible duties by weight descending and expands the random-pick group only while the next
    /// weight is EQUAL to the top one (.decomp/InGameNPC.cs lines 2316-2320), so a duty weighted
    /// below this number is never selected at all - with no error and no log. A tie IS enough.
    /// MoveDutyAction.CanBePerformed returns true unconditionally for MovementTypes.Teleport
    /// (.decomp/MoveDutyAction.cs lines 252-253), so a teleporting movement duty never yields its
    /// slot by becoming undoable either.</summary>
    private static int HeaviestMovementDutyWeight(AnimalManifest m) => Math.Max(
        m.PlayerAttractionEnabled || m.FleeFromPlayer ? m.PlayerAttractionBaseWeight : 0,
        m.WanderEnvs.Count > 0 ? m.WanderWeight : 0);

    /// <summary>M6 Interactions. An Interaction with no resolvable OnSuccess.GiveCard is
    /// pointless (nothing for the player to actually earn), and a bare skill UID that
    /// resolves to nothing silently ungates the roll (SuccessWeight alone would still work,
    /// but the author's intent — a skill-scaled chance — would silently not apply).</summary>
    private static void ValidateInteractions(AnimalManifest m, List<string> errors)
    {
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < m.Interactions.Count; i++)
        {
            var ix = m.Interactions[i];
            string where = $"Interactions[{i}]";

            if (string.IsNullOrEmpty(ix.Name))
                errors.Add($"Field '{where}.Name': required, missing");
            else if (!seenNames.Add(ix.Name))
                errors.Add($"Field '{where}.Name': \"{ix.Name}\" duplicated within this manifest");

            if (ix.SuccessWeight <= 0)
                errors.Add($"Field '{where}.SuccessWeight': expected > 0, got {ix.SuccessWeight}");
            if (ix.FailWeight <= 0)
                errors.Add($"Field '{where}.FailWeight': expected > 0, got {ix.FailWeight}");
            if (ix.DaytimeCost < 0)
                errors.Add($"Field '{where}.DaytimeCost': expected >= 0, got {ix.DaytimeCost}");

            if (ix.Skill != null
                && GameRegistry.GetByUid<GameStat>(ix.Skill) == null
                && Database.GetTypedSO(typeof(GameStat), ix.Skill) == null)
                errors.Add($"Field '{where}.Skill': \"{ix.Skill}\" does not resolve to a GameStat (UID or runtime name)");

            if (ix.HasSkillBonus)
            {
                if (ix.Skill == null)
                    errors.Add($"Field '{where}.SkillBonus': requires 'Skill' to be set");
                if (ix.SkillStatMin >= ix.SkillStatMax)
                    errors.Add($"Field '{where}.SkillBonus.StatRange': expected Min < Max, got [{ix.SkillStatMin}, {ix.SkillStatMax}]");
            }

            foreach (var cardUid in ix.BaitCards)
                if (GameRegistry.GetByUid<CardData>(cardUid) == null)
                    errors.Add($"Field '{where}.BaitCards': \"{cardUid}\" does not resolve to a CardData");
            foreach (var tagName in ix.BaitTags)
                if (Database.GetTypedSO(typeof(CardTag), tagName) == null)
                    errors.Add($"Field '{where}.BaitTags': \"{tagName}\" does not resolve to a loaded CardTag");

            if (string.IsNullOrEmpty(ix.GiveCard))
                errors.Add($"Field '{where}.OnSuccess.GiveCard': required, missing — an interaction with no reward does nothing");
            else if (GameRegistry.GetByUid<CardData>(ix.GiveCard) is not { } given)
                errors.Add($"Field '{where}.OnSuccess.GiveCard': \"{ix.GiveCard}\" does not resolve to a CardData");
            else if ((int)given.CardType != 0)
                errors.Add($"Field '{where}.OnSuccess.GiveCard': \"{given.name}\" is CardType {(int)given.CardType}, expected 0 (CT0 item — the framework never generates item cards)");

            if (ix.AttackChance is < 0 or > 100)
                errors.Add($"Field '{where}.OnFail.AttackChance': expected 0-100, got {ix.AttackChance}");
            if (ix.AttackChance > 0 && m.EncounterRef == null && !m.HasEncounterGeneration)
                errors.Add($"Field '{where}.OnFail.AttackChance': > 0 requires 'Encounter.Ref' or Encounter generation fields — there is no encounter to fire on an aggressive fail");
        }
    }

    /// <summary>M6 Companion. Only validates the card reference itself — CompanionService's
    /// retire/init behavior is driven per-Interaction (OnSuccess.GiveCard), so a Companion
    /// section with no matching Interaction is legal (e.g. a future perk-granted companion).</summary>
    private static void ValidateCompanion(AnimalManifest m, List<string> errors)
    {
        if (!m.HasCompanion) return;

        if (GameRegistry.GetByUid<CardData>(m.CompanionCard) is not { } companion)
            errors.Add($"Field 'Companion.Card': \"{m.CompanionCard}\" does not resolve to a CardData");
        else if ((int)companion.CardType != 0)
            errors.Add($"Field 'Companion.Card': \"{companion.name}\" is CardType {(int)companion.CardType}, expected 0 (CT0 item)");
    }

    private static void ValidateCustomDuties(AnimalManifest m, List<string> errors)
    {
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < m.CustomDuties.Count; i++)
        {
            var duty = m.CustomDuties[i];
            string where = $"CustomDuties[{i}]";

            if (string.IsNullOrEmpty(duty.Name))
                errors.Add($"Field '{where}.Name': required, missing");
            else if (!DutyNamePattern.IsMatch(duty.Name))
                errors.Add($"Field '{where}.Name': expected [A-Za-z0-9_]+, got \"{duty.Name}\"");
            else if (!seenNames.Add(duty.Name))
                errors.Add($"Field '{where}.Name': \"{duty.Name}\" duplicated within this manifest");

            if (duty.BaseWeight <= 0)
                errors.Add($"Field '{where}.BaseWeight': expected > 0 (a zero-weight duty is never selected), got {duty.BaseWeight}");

            if (duty.HasValidHours)
            {
                if (duty.ValidStart is < 0 or > 23)
                    errors.Add($"Field '{where}.ValidHours.Start': expected 0-23, got {duty.ValidStart}");
                if (duty.ValidEnd is < 0 or > 23)
                    errors.Add($"Field '{where}.ValidHours.End': expected 0-23, got {duty.ValidEnd}");
            }

            if (duty.Actions.Count == 0)
            {
                errors.Add($"Field '{where}.Actions': required, empty (a duty needs at least one action)");
                continue;
            }

            for (int j = 0; j < duty.Actions.Count; j++)
            {
                var a = duty.Actions[j];
                string aWhere = $"{where}.Actions[{j}]";
                switch (a.Type)
                {
                    case "Move":
                        switch (a.Destination)
                        {
                            case "Player":
                            case "Home":
                                break;
                            case "Env":
                                if (string.IsNullOrEmpty(a.Env))
                                    errors.Add($"Field '{aWhere}.Env': required for Destination=Env");
                                else if (GameRegistry.GetByUid<CardData>(a.Env) == null)
                                    errors.Add($"Field '{aWhere}.Env': \"{a.Env}\" does not resolve to a CardData");
                                break;
                            case "EnvTag":
                                if (string.IsNullOrEmpty(a.Tag))
                                    errors.Add($"Field '{aWhere}.Tag': required for Destination=EnvTag");
                                else if (Database.GetTypedSO(typeof(CardTag), a.Tag) == null)
                                    errors.Add($"Field '{aWhere}.Tag': CardTag \"{a.Tag}\" not found (runtime asset name)");
                                break;
                            default:
                                errors.Add($"Field '{aWhere}.Destination': expected Env|EnvTag|Player|Home, got \"{a.Destination}\"");
                                break;
                        }
                        if (a.Selection is not ("Closest" or "Furthest" or "Random"))
                            errors.Add($"Field '{aWhere}.Selection': expected Closest|Furthest|Random, got \"{a.Selection}\"");
                        if (a.MovementType is not ("Pathfind" or "Teleport"))
                            errors.Add($"Field '{aWhere}.MovementType': expected Pathfind|Teleport, got \"{a.MovementType}\"");
                        break;
                    case "Wait":
                        if (a.TicksMin < 1 || a.TicksMax < a.TicksMin)
                            errors.Add($"Field '{aWhere}': Wait needs Ticks >= 1 (or TicksRange [min, max]), got [{a.TicksMin}, {a.TicksMax}]");
                        break;
                    case "StartEncounter":
                        if (string.IsNullOrEmpty(a.Encounter))
                            errors.Add($"Field '{aWhere}.Encounter': required for StartEncounter");
                        else if (GameRegistry.GetByUid(a.Encounter) is not Encounter)
                            errors.Add($"Field '{aWhere}.Encounter': \"{a.Encounter}\" does not resolve to a loaded Encounter");
                        break;
                    case "AffectItems":
                        if (a.ItemTags.Count == 0 && a.ItemCards.Count == 0)
                            errors.Add($"Field '{aWhere}': AffectItems requires at least one ItemTags or ItemCards entry (an empty item pool selects nothing)");
                        foreach (var tagName in a.ItemTags)
                            if (Database.GetTypedSO(typeof(CardTag), tagName) == null)
                                errors.Add($"Field '{aWhere}.ItemTags': \"{tagName}\" does not resolve to a loaded CardTag");
                        foreach (var cardUid in a.ItemCards)
                            if (GameRegistry.GetByUid<CardData>(cardUid) == null)
                                errors.Add($"Field '{aWhere}.ItemCards': \"{cardUid}\" does not resolve to a CardData");
                        if (!string.Equals(a.Affect, "Destroy", StringComparison.Ordinal))
                            errors.Add($"Field '{aWhere}.Affect': only \"Destroy\" is supported in v1, got \"{a.Affect}\"");
                        break;
                    default:
                        errors.Add($"Field '{aWhere}.Type': expected Move|Wait|StartEncounter, got \"{a.Type}\"");
                        break;
                }
            }
        }
    }

    /// <summary>The out-of-window-duty pitfall (research §6.3): with zero valid duties at some
    /// hour, the engine keeps running whatever duty is already active. Every species that has
    /// duties at all must have at least one selectable duty in every hour of the day. The
    /// generated roost duty covers the inactive window by construction; this check catches
    /// manifests relying purely on windowed CustomDuties.</summary>
    private static void ValidateHourCoverage(AnimalManifest m, List<string> errors)
    {
        bool hasSeek = m.PlayerAttractionEnabled || m.FleeFromPlayer;
        bool hasWander = m.WanderEnvs.Count > 0;
        bool anyDuty = hasSeek || hasWander || m.CustomDuties.Count > 0;
        if (!anyDuty) return;

        var covered = new bool[24];

        void Cover(int start, int end)
        {
            if (start == end) return;
            for (int h = start; h != end; h = (h + 1) % 24)
                covered[h] = true;
        }

        if (hasSeek)
        {
            if (m.PAOnlyDuringActiveHours && m.HasActivityWindow) Cover(m.ActiveStart, m.ActiveEnd);
            else for (int h = 0; h < 24; h++) covered[h] = true;
        }
        if (hasWander)
        {
            if (m.HasActivityWindow) Cover(m.ActiveStart, m.ActiveEnd);
            else for (int h = 0; h < 24; h++) covered[h] = true;
        }
        if (m.HasActivityWindow) Cover(m.ActiveEnd, m.ActiveStart);   // generated roost duty
        foreach (var duty in m.CustomDuties)
        {
            if (duty.HasValidHours) Cover(duty.ValidStart, duty.ValidEnd);
            else for (int h = 0; h < 24; h++) covered[h] = true;
        }

        var gaps = Enumerable.Range(0, 24).Where(h => !covered[h]).ToList();
        if (gaps.Count > 0)
            errors.Add($"Duty coverage: no duty is selectable during hour(s) {string.Join(",", gaps)} — the engine keeps running an out-of-window duty when nothing is valid. Add an ActivityWindow (its roost duty covers the off-hours) or an unwindowed CustomDuty.");
    }
}
