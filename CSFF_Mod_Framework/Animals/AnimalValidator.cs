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

        if (m.AgentRef != null)
        {
            // Escape hatch: hand-authored NPCAgent — the manifest only fills the gaps
            // (spawn registration in M1). Generation fields are ignored.
            if (GameRegistry.GetByUid(m.AgentRef) is not NPCAgent)
                errors.Add($"Field 'Agent.Ref': \"{m.AgentRef}\" does not resolve to a loaded NPCAgent");
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
        }

        if (m.HasActivityWindow)
        {
            if (m.ActiveStart is < 0 or > 23)
                errors.Add($"Field 'ActivityWindow.ActiveHours.Start': expected 0-23, got {m.ActiveStart}");
            if (m.ActiveEnd is < 0 or > 23)
                errors.Add($"Field 'ActivityWindow.ActiveHours.End': expected 0-23, got {m.ActiveEnd}");
            if (m.Roost != "SpiritWorld")
                errors.Add($"Field 'ActivityWindow.Roost': on-stage roost envs land in M2 — expected \"SpiritWorld\", got \"{m.Roost}\"");
        }

        if (m.EncounterRef != null && GameRegistry.GetByUid(m.EncounterRef) is not Encounter)
            errors.Add($"Field 'Encounter.Ref': \"{m.EncounterRef}\" does not resolve to a loaded Encounter");

        return errors;
    }
}
