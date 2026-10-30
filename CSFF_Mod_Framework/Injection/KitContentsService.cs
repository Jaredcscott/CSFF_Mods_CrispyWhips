using CSFFModFramework.Api;
using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>
/// Declarative kit contents, the game side (framework 2.29.0, fleet plan 6.7, W0.10 row 4): every kit a mod
/// declares in <c>KitContents.json</c> gets one <see cref="ActionRouter"/> handler on its kit card, at
/// <see cref="ActionTiming.AfterWrapped"/>, matching the declared action prefix. When the player's action on the
/// kit ends (the kit's own "Place" DismantleAction, which destroys it), the handler spawns the kit's items on the
/// CURRENT board through <see cref="SpawnService.Spawn(string, IDictionary{string, float})"/>, one copy per call,
/// in the listed order, with each item's stat overrides. This is the pattern Community Mod Chest and
/// HomesteadPerks each carried as <c>Patcher/HomesteadKitPatch.cs</c> until 2.29.0, with HomesteadPerks' two
/// fixes made general: stat overrides on any item, and <c>OnlyIfCardExists</c> (one lookup up front instead of
/// one spawn error per copy). <see cref="KitContentsTable"/> validates the files and plans each unpack.
///
/// <para><b>Timing.</b> <see cref="LoadAll"/> runs once per process in the framework's load pass, after
/// WarpResolver, so the kit and item UIDs are checked against loaded cards. The handlers are process-lifetime,
/// as the mods' own were (registered from plugin Awake); <see cref="LoadAll"/> still unregisters any it
/// registered before (CLAUDE.md "Per-Run Event Registration", pattern a).</para>
///
/// <para><b>Log lines.</b> A kit's lines go out at BepInEx Debug level whatever <c>VerboseLogging</c> says, as a
/// mod's own <c>Logger.LogDebug</c> lines did, and carry the kit's <c>LogTag</c>: the two migrated kits keep
/// <c>HomesteadKitPatch</c>, so the lines their harness rows assert on are unchanged.</para>
/// </summary>
internal static class KitContentsService
{
    private static readonly List<ActionHandler> _handlers = new();

    internal static int KitCount => _handlers.Count;

    /// <summary>Reads every mod's <c>KitContents.json</c> in load order, validates and merges them, and registers one handler per kit.</summary>
    public static void LoadAll(List<ModManifest> mods)
    {
        foreach (var handler in _handlers) ActionRouter.Unregister(handler);
        _handlers.Clear();

        var names = new List<string>();
        var texts = new List<string>();
        foreach (var mod in mods)
        {
            var path = Path.Combine(mod.DirectoryPath, KitContentsTable.FileName);
            if (!File.Exists(path)) continue;
            try
            {
                texts.Add(File.ReadAllText(path));
                names.Add(mod.Name);
            }
            catch (Exception ex)
            {
                Log.Warn($"[KitContents] [{mod.Name}] could not read {KitContentsTable.FileName}: {Log.ExceptionText(ex)}");
            }
        }
        if (names.Count == 0) return;

        var table = KitContentsTable.Build(names, texts, CardExists);

        int skipped = 0, unresolved = 0;
        var missed = new List<string>();
        foreach (var issue in table.Issues)
        {
            if (issue.UnresolvedUid != null)
            {
                if (UnresolvedUid.ExpectedPresent(issue.UnresolvedUid))
                    Log.Warn($"[KitContents] {issue.Message} (UID '{issue.UnresolvedUid}' does not resolve)");
                else
                    Log.Debug($"[KitContents] {issue.Message} (UID '{issue.UnresolvedUid}' does not resolve; a partner mod not installed?)");
                unresolved++;
                missed.Add(issue.UnresolvedUid);
                continue;
            }
            Log.Warn($"[KitContents] {issue.Message}");
            skipped++;
        }

        int items = 0;
        foreach (var kit in table.Kits)
        {
            var def = kit;
            var handler = ActionRouter.Register(new ActionHandler
            {
                Name = $"KitContents:{def.Kit}",
                CardUid = def.Kit,
                ActionNamePrefix = def.ActionNamePrefix,
                ActionKeyPrefix = def.ActionKeyPrefix,
                Timing = ActionTiming.AfterWrapped,
                After = _ => Unpack(def),
            });
            if (handler == null)
            {
                Log.Warn($"[KitContents] [{def.Mod}] {def}: the action handler was refused; the kit unpacks nothing.");
                skipped++;
                continue;
            }
            _handlers.Add(handler);
            items += def.Items.Count;
            Log.Debug($"[KitContents] [{def.Mod}] {def} on action '{def.ActionKeyPrefix ?? def.ActionNamePrefix}'.");
        }

        if (skipped > 0 || unresolved > 0)
            Log.Info($"[KitContents] {_handlers.Count} kit(s) loaded, {items} item row(s) ({skipped} issue(s), {unresolved} unresolved"
                + (unresolved > 0 ? $": {UnresolvedUid.Sample(missed)}" : "") + ").");
        else
            Log.Info($"[KitContents] {_handlers.Count} kit(s) loaded, {items} item row(s).");
    }

    private static void Unpack(KitDefinition kit)
    {
        try
        {
            foreach (var step in KitContentsTable.Plan(kit, uid => CardUtil.GetCardDataById(uid) != null))
            {
                if (step.Card != null)
                    for (int i = 0; i < step.Count; i++)
                        SpawnService.Spawn(step.Card, step.Stats);
                if (step.Log != null)
                    Log.DebugAlways(step.Log);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[KitContents] [{kit.Mod}] unpacking '{kit.Kit}' threw: {Log.ExceptionText(ex)}");
        }
    }

    private static bool CardExists(string uid) => GameRegistry.GetByUid(uid) is CardData;
}
