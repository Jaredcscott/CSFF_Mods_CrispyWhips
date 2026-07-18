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
        ValidateCustomDuties(m, errors);
        ValidateHourCoverage(m, errors);

        if (m.EncounterRef != null && GameRegistry.GetByUid(m.EncounterRef) is not Encounter)
            errors.Add($"Field 'Encounter.Ref': \"{m.EncounterRef}\" does not resolve to a loaded Encounter");

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
                        errors.Add($"Field '{aWhere}.Type': AffectItems lands with the M4 feed duty — not yet supported");
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
