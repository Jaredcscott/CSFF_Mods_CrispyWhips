using CSFFModFramework.Api;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Loads declarative wildlife-encounter guards from <c>&lt;ModFolder&gt;/EncounterGuards/*.json</c>
/// and registers them with <see cref="Api.EncounterGuards"/>. The JSON option covers the
/// common companion/ward shape — "suppress wildlife encounters while card X is in the
/// player's environment" — with zero mod C#.
///
/// <para>Schema (one guard per file; parsed with MiniJson, never JsonUtility):</para>
/// <code>
/// {
///   "Name": "WolfCompanionGuard",
///   "GuardCardUids": ["fc_wolf_companion"],       // any of these in player env → suppress
///   "GuardEnvironmentUids": ["myEnvVillage"],      // OR: player standing in any of these envs → suppress
///   "EncounterUids": [],                          // empty = all wildlife encounters
///   "SuppressChance": 100                         // percent, 0-100 (default 100)
/// }
/// </code>
/// <para>At least one of <c>GuardCardUids</c> or <c>GuardEnvironmentUids</c> must be present.
/// When both are set, either a matching card OR a matching environment triggers suppression.</para>
/// </summary>
internal static class EncounterGuardLoader
{
    private sealed class GuardDef
    {
        public string Name;
        public readonly List<string> GuardCardUids = new();
        public readonly HashSet<string> GuardEnvironmentUids = new(StringComparer.Ordinal);
        public readonly HashSet<string> EncounterUids = new(StringComparer.OrdinalIgnoreCase);
        public float SuppressChance = 100f;
    }

    public static void LoadAll(List<ModManifest> mods)
    {
        int loaded = 0;
        foreach (var mod in mods)
        {
            var dir = Path.Combine(mod.DirectoryPath, "EncounterGuards");
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var def = ParseGuard(File.ReadAllText(file));
                    if (def == null || (def.GuardCardUids.Count == 0 && def.GuardEnvironmentUids.Count == 0))
                    {
                        Log.Warn($"[EncounterGuardLoader] {mod.Name}/{Path.GetFileName(file)}: missing GuardCardUids and GuardEnvironmentUids — skipped.");
                        continue;
                    }
                    def.Name ??= Path.GetFileNameWithoutExtension(file);
                    RegisterJsonGuard(def);
                    loaded++;
                    Log.Debug($"[EncounterGuardLoader] {mod.Name}: guard '{def.Name}' "
                            + $"({def.GuardCardUids.Count} guard card(s), {def.GuardEnvironmentUids.Count} guard env(s), "
                            + $"{(def.EncounterUids.Count == 0 ? "all wildlife" : def.EncounterUids.Count + " encounter(s)")}, "
                            + $"{def.SuppressChance:F0}%)");
                }
                catch (Exception ex)
                {
                    Log.Warn($"[EncounterGuardLoader] failed to load {file}: {Log.ExceptionText(ex)}");
                }
            }
        }
        if (loaded > 0)
            Log.Debug($"[EncounterGuardLoader] {loaded} JSON guard(s) registered.");
    }

    private static GuardDef ParseGuard(string json)
    {
        if (MiniJson.Parse(json) is not Dictionary<string, object> root) return null;

        var def = new GuardDef();
        if (root.TryGetValue("Name", out var n) && n is string name && !string.IsNullOrEmpty(name))
            def.Name = name;
        if (root.TryGetValue("GuardCardUids", out var g) && g is List<object> guardList)
            foreach (var item in guardList)
                if (item is string s && !string.IsNullOrEmpty(s)) def.GuardCardUids.Add(s);
        if (root.TryGetValue("GuardEnvironmentUids", out var ge) && ge is List<object> envList)
            foreach (var item in envList)
                if (item is string s && !string.IsNullOrEmpty(s)) def.GuardEnvironmentUids.Add(s);
        if (root.TryGetValue("EncounterUids", out var e) && e is List<object> encList)
            foreach (var item in encList)
                if (item is string s && !string.IsNullOrEmpty(s)) def.EncounterUids.Add(s);
        if (root.TryGetValue("SuppressChance", out var c) && c is double chance)
            def.SuppressChance = Mathf.Clamp((float)chance, 0f, 100f);
        return def;
    }

    private static void RegisterJsonGuard(GuardDef def)
    {
        EncounterGuards.Register(def.Name, ctx =>
        {
            // Encounter filter: empty = all wildlife encounters.
            if (def.EncounterUids.Count > 0
                && (ctx.EncounterUid == null || !def.EncounterUids.Contains(ctx.EncounterUid)))
                return false;

            // Environment guard: is the player standing in one of the guarded environments?
            bool guardPresent = false;
            if (def.GuardEnvironmentUids.Count > 0)
            {
                var curEnv = GameQuery.CurrentEnvironmentUniqueId;
                if (curEnv != null && def.GuardEnvironmentUids.Contains(curEnv))
                    guardPresent = true;
            }

            // Card guard: any guard card present in the player's environment?
            if (!guardPresent && def.GuardCardUids.Count > 0)
            {
                foreach (var card in GameQuery.CardsInPlayerEnv())
                {
                    var uid = CardUtil.GetCardUniqueId(card);
                    if (uid == null) continue;
                    foreach (var guardUid in def.GuardCardUids)
                    {
                        if (!string.Equals(uid, guardUid, StringComparison.OrdinalIgnoreCase)) continue;
                        guardPresent = true;
                        break;
                    }
                    if (guardPresent) break;
                }
            }
            if (!guardPresent) return false;

            return def.SuppressChance >= 100f
                || UnityEngine.Random.value * 100f < def.SuppressChance;
        });
    }
}
