using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Portal;

/// <summary>
/// Discovers <c>MapMod.json</c> files from mod directories and registers each as a
/// <see cref="PortalWorld"/> in <see cref="PortalRegistry"/>.
///
/// <para>Schema (<c>MapMod.json</c> at the mod root) — the ONE supported portal registration
/// path (2026-07-02): a mod ships this file and a build-anywhere Portal Kit (see
/// <c>Documentation/Portal_Hub_System.md</c>) does the rest. No mod C# required.</para>
/// <code>
/// {
///   "WorldName": "The Iron Wastes",
///   "EnvironmentUID": "mymod_env_hub"
/// }
/// </code>
///
/// <para><c>EnvironmentUID</c> (preferred, a CT4) or <c>SacredSiteUID</c> (legacy CT8 fallback)
/// is the UniqueID of the environment card <see cref="PortalService.RegisterHubTravelHandlers"/>
/// travels the player to when they click this world's button on the shared Portal Hub card.</para>
/// <para>An earlier, never-adopted <c>PortalAnchorEnvUID</c>/<c>LandingNodeUID</c>/<c>PortalCardUID</c>
/// schema (fixed-location-per-mod portals) and the dead <c>HubEdges</c> field were removed
/// 2026-07-02 — see <c>Documentation/Design/Unified_Map_Expansion_Design.md</c> §9's staleness
/// banner for why the build-anywhere Portal Hub is the permanent design instead.</para>
/// <para>Runs at Phase 5k — after WarpResolver, before PortalService.InjectDAs.</para>
/// </summary>
internal static class MapModLoader
{
    public static void LoadAll(IReadOnlyList<Discovery.ModManifest> mods)
    {
        PortalRegistry.Clear();

        int loaded = 0;
        foreach (var mod in mods)
        {
            var path = Path.Combine(mod.DirectoryPath, "MapMod.json");
            if (!File.Exists(path)) continue;
            try
            {
                LoadMapMod(path, mod.Name);
                loaded++;
            }
            catch (Exception ex)
            {
                Log.Error($"[MapModLoader] failed to read {path}: {Log.ExceptionText(ex)}");
            }
        }

        if (loaded == 0)
            Log.Debug("[MapModLoader] no mod ships MapMod.json — portal will register Fantasy Forest only");
        else
            Log.Info($"[MapModLoader] {loaded} portal world(s) registered (total: {PortalRegistry.Worlds.Count})");
    }

    private static void LoadMapMod(string path, string modName)
    {
        var json = File.ReadAllText(path);
        var parsed = MiniJson.Parse(json) as Dictionary<string, object>;
        if (parsed == null)
        {
            Log.Warn($"[MapModLoader] {modName}: MapMod.json root must be a JSON object");
            return;
        }

        var worldName = parsed.TryGetValue("WorldName", out var wn) && wn is string s && !string.IsNullOrWhiteSpace(s)
            ? s : modName;

        var sacredSiteUID = parsed.TryGetValue("SacredSiteUID", out var ss) && ss is string ssu && !string.IsNullOrWhiteSpace(ssu)
            ? ssu : null;

        var environmentUID = parsed.TryGetValue("EnvironmentUID", out var eu) && eu is string eus && !string.IsNullOrWhiteSpace(eus)
            ? eus : null;

        if (string.IsNullOrEmpty(environmentUID) && string.IsNullOrEmpty(sacredSiteUID))
            Log.Warn($"[MapModLoader] {modName}: MapMod.json has no 'EnvironmentUID' or 'SacredSiteUID' — portal cannot teleport here");

        Log.Debug($"[MapModLoader] registered portal world '{worldName}' (mod: {modName}, target: {environmentUID ?? sacredSiteUID ?? "<none>"})");

        PortalRegistry.Register(new PortalWorld
        {
            WorldName      = worldName,
            ModName        = modName,
            EnvironmentUID = environmentUID,
            SacredSiteUID  = sacredSiteUID,
        });
    }
}
