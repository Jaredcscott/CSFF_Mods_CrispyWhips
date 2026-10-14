using CSFFModFramework.Api;
using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Loads declarative encounter guards from <c>&lt;ModFolder&gt;/EncounterGuards/*.json</c> and
/// registers them with <see cref="Api.EncounterGuards"/>. The JSON option covers the common
/// companion/ward shape ("suppress wildlife encounters while card X is in the player's environment,
/// or while the player stands in env Y") with zero mod C#. This header is the schema's only full
/// statement; the framework README's guard text follows it.
///
/// <para>Schema (one guard per file; parsed with MiniJson, never JsonUtility):</para>
/// <code>
/// {
///   "Name": "WolfCompanionGuard",
///   "GuardCardUids": ["fc_wolf_companion"],       // any of these in player env: guard present
///   "GuardEnvironmentUids": ["myEnvVillage"],      // OR: player standing in any of these envs
///   "EncounterUids": [],                          // Encounter UniqueIDs; empty = every null-NPC start
///   "AgentUids": [],                              // NPCAgent UniqueIDs whose OWN attacks it stops (2.28.0)
///   "SuppressChance": 100                         // percent, 0-100 (default 100)
/// }
/// </code>
/// <para>At least one of <c>GuardCardUids</c> or <c>GuardEnvironmentUids</c> must be present.
/// When both are set, either a matching card OR a matching environment counts as the guard being
/// present. The guard environment and the guard card are always tested against the PLAYER's
/// environment.</para>
///
/// <para><strong>Which starts a guard reaches (framework 2.28.0).</strong></para>
/// <list type="bullet">
/// <item>A start with no NPC (a drop naming no <c>EncounterNPC</c>, a generated tame-fail attack):
/// <c>EncounterUids</c> filters it, empty meaning all. A guard with an empty <c>EncounterUids</c>
/// and a non-empty <c>AgentUids</c> is agent-scoped and does not reach these.</item>
/// <item>An agent's own attack (its <c>AgentAction</c> on its tick, or a duty's encounter step, such
/// as a generated <c>Aggression</c> attack): reached only when the guard names the agent in
/// <c>AgentUids</c> OR names the encounter in a NON-EMPTY <c>EncounterUids</c>. A guard with both
/// lists empty (every companion guard, the Hunting Stand, an interiors guard) never reaches an agent
/// attack, so a Harsh Wilds bandit still fights a player with a wolf companion.</item>
/// <item>A fight the player chose (Approach, a dialog's attack answer, Give Chase) is never
/// suppressed by any guard.</item>
/// <item>A save that resumes a fight in progress is never cancelled.</item>
/// </list>
/// <para>A percent guard is rolled once per (agent, encounter, tick) on an agent attack, so against
/// an attack that repeats every tick it rolls again each time.</para>
/// </summary>
internal static class EncounterGuardLoader
{
    private sealed class GuardDef
    {
        public string Name;
        public readonly List<string> GuardCardUids = new();
        public readonly HashSet<string> GuardEnvironmentUids = new(StringComparer.Ordinal);
        public readonly HashSet<string> EncounterUids = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> AgentUids = new(StringComparer.OrdinalIgnoreCase);
        public float SuppressChance = 100f;

        /// <summary>The scope rule: only a guard that NAMES an agent or an encounter reaches agent attacks.</summary>
        public bool ReachesAgentAttacks => AgentUids.Count > 0 || EncounterUids.Count > 0;
    }

    public static void LoadAll(List<ModManifest> mods)
    {
        int loaded = 0, unresolved = 0;
        var reachingAgents = new List<string>();
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
                        Log.Warn($"[EncounterGuardLoader] {mod.Name}/{Path.GetFileName(file)}: missing GuardCardUids and GuardEnvironmentUids, skipped.");
                        continue;
                    }
                    def.Name ??= Path.GetFileNameWithoutExtension(file);
                    unresolved += LogUnresolved(mod.Name, def);
                    RegisterJsonGuard(def);
                    loaded++;
                    if (def.ReachesAgentAttacks) reachingAgents.Add(def.Name);
                    Log.Debug($"[EncounterGuardLoader] {mod.Name}: guard '{def.Name}' "
                            + $"({def.GuardCardUids.Count} guard card(s), {def.GuardEnvironmentUids.Count} guard env(s), "
                            + $"{(def.EncounterUids.Count == 0 ? "all null-NPC starts" : def.EncounterUids.Count + " encounter(s)")}, "
                            + $"{def.AgentUids.Count} agent(s), {def.SuppressChance:F0}%, "
                            + $"{(def.ReachesAgentAttacks ? "reaches agent attacks" : "null-NPC starts only")})");
                }
                catch (Exception ex)
                {
                    Log.Warn($"[EncounterGuardLoader] failed to load {file}: {Log.ExceptionText(ex)}");
                }
            }
        }
        if (loaded == 0) return;

        if (unresolved > 0)
            Log.Info($"[EncounterGuardLoader] {unresolved} unresolved EncounterUids/AgentUids reference(s) kept inert (Debug-logged)");

        // The load summary: says which guards reach agent attacks. Info only when one does, because that
        // is the guard set a player report about "a bear never attacks me here" needs to read.
        if (reachingAgents.Count > 0)
            Log.Info($"[EncounterGuardLoader] {loaded} JSON guard(s) registered; {reachingAgents.Count} reach agent attacks "
                   + $"({string.Join(", ", reachingAgents)}); agent hooks installed {Patching.AgentEncounterGuardPatch.HooksInstalled}/2.");
        else
            Log.Debug($"[EncounterGuardLoader] {loaded} JSON guard(s) registered; none reach agent attacks.");
    }

    /// <summary>
    /// Unresolved references (framework README "Unresolved references"): one Debug line per
    /// <c>EncounterUids</c> or <c>AgentUids</c> entry that names no loaded object, and the count. The
    /// entry is KEPT, not dropped: it can never match a start, while dropping it could empty
    /// <c>EncounterUids</c> and widen the guard to every null-NPC start. Guard card and environment
    /// UIDs are not checked here, because this loader runs before WorldMapInjector registers clone
    /// environment and location cards, so a clone UID would read as missing.
    /// </summary>
    private static int LogUnresolved(string modName, GuardDef def)
    {
        int unresolved = 0;
        foreach (var uid in def.EncounterUids.Concat(def.AgentUids))
        {
            if (GameRegistry.GetByUid(uid) != null) continue;
            Log.Debug($"[EncounterGuardLoader] {modName}: guard '{def.Name}' names '{uid}', which does not resolve; kept inert");
            unresolved++;
        }
        return unresolved;
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
        if (root.TryGetValue("AgentUids", out var a) && a is List<object> agentList)
            foreach (var item in agentList)
                if (item is string s && !string.IsNullOrEmpty(s)) def.AgentUids.Add(s);
        if (root.TryGetValue("SuppressChance", out var c) && c is double chance)
            def.SuppressChance = Mathf.Clamp((float)chance, 0f, 100f);
        return def;
    }

    private static void RegisterJsonGuard(GuardDef def)
    {
        EncounterGuards.Register(def.Name, ctx =>
        {
            if (ctx.Source == EncounterStartSource.NoNpc)
            {
                // Null-NPC start: the encounter filter, empty = all (unchanged since 1.x), except that a
                // guard naming only agents is agent-scoped.
                if (def.EncounterUids.Count > 0)
                {
                    if (ctx.EncounterUid == null || !def.EncounterUids.Contains(ctx.EncounterUid)) return false;
                }
                else if (def.AgentUids.Count > 0) return false;
            }
            else
            {
                // Agent attack: the guard must NAME the agent or the encounter.
                bool namesAgent = ctx.AgentUid != null && def.AgentUids.Contains(ctx.AgentUid);
                bool namesEncounter = def.EncounterUids.Count > 0
                    && ctx.EncounterUid != null && def.EncounterUids.Contains(ctx.EncounterUid);
                if (!namesAgent && !namesEncounter) return false;
            }

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
        }, def.ReachesAgentAttacks);
    }
}
