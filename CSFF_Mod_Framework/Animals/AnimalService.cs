using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// Declarative animal system orchestrator (design: Documentation/Design/Animal_System_Plan.md,
/// schema: Documentation/Design/Animals_Schema.md). Mods ship Animals/&lt;Species&gt;.json +
/// PNGs; the framework generates the NPCAgent graph and registers spawning — zero mod C#.
///
/// <para>Runs as LoadOrchestrator phase 5g2 (post-WarpResolver, before
/// NPCAgentActivationService so generated agents get its validation/normalization pass).
/// Config-gated by Animals.AnimalsEnabled (Plugin.Awake, WildlifeRaidService pattern).</para>
///
/// <para>Milestone status: M1 — schema loader/validator, minimal Hag-style lifecycle
/// (appear at home env during active hours / park in Spirit World otherwise), spawn
/// registration via <see cref="SpawnRegistrar"/>, Approach-button encounter smoke test.</para>
/// </summary>
internal static class AnimalService
{
    public static bool Enabled = true;

    private static readonly List<(string SpeciesId, NPCAgent Agent)> _species = new();
    private static bool _subscribed;

    public static void LoadAll(List<ModManifest> mods)
    {
        if (!Enabled)
        {
            Log.Info("Animals: disabled via config (Animals.AnimalsEnabled=false) — skipping");
            return;
        }

        _species.Clear();
        AnimalLifecycleTicker.Clear();
        var result = AnimalLoader.LoadAll(mods);

        foreach (var manifest in result.Accepted)
        {
            var agent = AnimalAssetFactory.BuildAgent(manifest);
            if (agent == null)
            {
                result.Rejected++;
                continue;
            }
            SpawnRegistrar.Queue(agent);
            _species.Add((manifest.SpeciesId, agent));
        }

        if (_species.Count == 0 && result.Rejected == 0) return;

        Log.Info($"Animals: {_species.Count} species loaded from {result.Mods.Count} mod(s)"
               + (result.Rejected > 0 ? $", {result.Rejected} rejected (see errors above)" : ""));

        if (_species.Count > 0) SubscribeRunStartCheck();
    }

    /// <summary>Run-start confirmation: verifies each species produced a live, ticking InGameNPC.
    /// Info-level during M1/M2 bring-up (debugging-discipline rule 3); demote to Debug once the
    /// spawn path is runtime-confirmed.</summary>
    private static void SubscribeRunStartCheck()
    {
        if (_subscribed) return;
        GameManager.OnGMInitialized += OnGmInitialized;
        _subscribed = true;
    }

    private static void OnGmInitialized()
    {
        try
        {
            var gm = MBSingleton<GameManager>.Instance;
            if (gm == null)
            {
                Log.Warn("Animals: GameManager.Instance unavailable at OnGMInitialized — live-agent check skipped");
                return;
            }

            int live = 0;
            foreach (var (speciesId, agent) in _species)
            {
                var npc = gm.AllNPCs?.FirstOrDefault(n => n != null && n.NPCModel == agent);
                if (npc != null)
                {
                    live++;
                    Log.Debug($"Animals: '{speciesId}' live as InGameNPC (env {npc.CurrentEnvironment})");
                }
                else
                    Log.Error($"Animals: '{speciesId}' has NO live InGameNPC — spawn registration failed (check for the 'registered N agent(s) in WorldSettings' line)");
            }
            if (_species.Count > 0)
                Log.Info($"Animals: {live}/{_species.Count} species confirmed live as InGameNPC at run start");
        }
        catch (Exception ex)
        {
            Log.Warn($"Animals: run-start check failed: {Log.ExceptionText(ex)}");
        }
    }
}
