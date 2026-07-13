using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// Discovers and parses Animals/*.json manifests across all mods. Parse errors and validation
/// failures reject the species atomically (no partial injection); the game always loads.
/// </summary>
internal static class AnimalLoader
{
    public sealed class Result
    {
        public List<AnimalManifest> Accepted = new();
        public int Rejected;
        public HashSet<string> Mods = new(StringComparer.OrdinalIgnoreCase);
    }

    public static Result LoadAll(List<ModManifest> mods)
    {
        var result = new Result();
        var seenSpecies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // SpeciesId → mod/file

        foreach (var mod in mods)
        {
            var dir = Path.Combine(mod.DirectoryPath, "Animals");
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
            {
                var label = $"{mod.Name}/Animals/{Path.GetFileName(file)}";
                AnimalManifest manifest;
                try
                {
                    if (MiniJson.Parse(File.ReadAllText(file)) is not Dictionary<string, object> root)
                    {
                        Log.Error($"Animals: {label} REJECTED — file is not a JSON object");
                        result.Rejected++;
                        continue;
                    }
                    manifest = AnimalManifest.FromDict(root, mod.Name, label);
                }
                catch (Exception ex)
                {
                    Log.Error($"Animals: {label} REJECTED — parse failed: {Log.ExceptionText(ex)}");
                    result.Rejected++;
                    continue;
                }

                var errors = AnimalValidator.Validate(manifest, seenSpecies);
                if (errors.Count > 0)
                {
                    Log.Error($"Animals: {label} REJECTED — {errors.Count} error(s):");
                    foreach (var err in errors) Log.Error($"  {err}");
                    result.Rejected++;
                    continue;
                }

                seenSpecies[manifest.SpeciesId] = label;

                if (manifest.UnknownKeys.Count > 0)
                    Log.Warn($"Animals: {label}: unknown key(s) ignored: {string.Join(", ", manifest.UnknownKeys)}");
                if (manifest.DeferredSections.Count > 0)
                    Log.Warn($"Animals: {label}: section(s) not yet implemented, ignored: {string.Join("; ", manifest.DeferredSections)}");

                result.Accepted.Add(manifest);
                result.Mods.Add(mod.Name);
                Log.Debug($"Animals: {label}: species '{manifest.SpeciesId}' accepted");
            }
        }

        return result;
    }
}
