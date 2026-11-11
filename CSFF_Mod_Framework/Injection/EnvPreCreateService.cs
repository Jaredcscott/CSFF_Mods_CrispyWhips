using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>
/// Declarative non-clone environment pre-create, the game side (fleet plan section 10, W0.10 row 3). See
/// <see cref="EnvPreCreateTable"/> for the JSON shape and what this replaces.
///
/// <para><b>Timing.</b> <see cref="LoadAll"/> runs once per process in the framework's load pass, after
/// WarpResolver, so every listed UID is checked against loaded cards. The actual pre-create must happen at
/// <c>GameManager.OnGMInitialized</c>, same as the two deleted patches and the framework's own clone-node
/// pre-create (<see cref="WorldMapInjector"/>): <c>EnvironmentsData</c> is a per-save runtime structure, not
/// load-time data. The subscription is made once per process (CLAUDE.md "Per-Run Event Registration", pattern
/// a); <c>OnGMInitialized</c> re-fires on every run start, and <see cref="EnvPreCreateTable.Run"/>'s own
/// already-present check keeps a re-run harmless.</para>
/// </summary>
internal static class EnvPreCreateService
{
    private const BindingFlags InstanceAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static List<EnvPreCreateEntry> _entries = new();
    private static bool _subscribed;
    private static MethodInfo _getEnvSaveData;

    internal static int EntryCount => _entries.Count;

    /// <summary>Reads every mod's <c>EnvPreCreate.json</c> in load order, validates and merges them, and arms the pre-create.</summary>
    public static void LoadAll(List<ModManifest> mods)
    {
        var names = new List<string>();
        var texts = new List<string>();
        foreach (var mod in mods)
        {
            var path = Path.Combine(mod.DirectoryPath, EnvPreCreateTable.FileName);
            if (!File.Exists(path)) continue;
            try
            {
                texts.Add(File.ReadAllText(path));
                names.Add(mod.Name);
            }
            catch (Exception ex)
            {
                Log.Warn($"[EnvPreCreate] [{mod.Name}] could not read {EnvPreCreateTable.FileName}: {Log.ExceptionText(ex)}");
            }
        }
        if (names.Count == 0) return;

        var table = EnvPreCreateTable.Build(names, texts, EnvExists);

        int skipped = 0, unresolved = 0;
        var missed = new List<string>();
        foreach (var issue in table.Issues)
        {
            if (issue.UnresolvedUid != null)
            {
                if (UnresolvedUid.ExpectedPresent(issue.UnresolvedUid))
                    Log.Warn($"[EnvPreCreate] {issue.Message} (UID '{issue.UnresolvedUid}' does not resolve)");
                else
                    Log.Debug($"[EnvPreCreate] {issue.Message} (UID '{issue.UnresolvedUid}' does not resolve; a partner mod not installed?)");
                unresolved++;
                missed.Add(issue.UnresolvedUid);
                continue;
            }
            Log.Warn($"[EnvPreCreate] {issue.Message}");
            skipped++;
        }

        _entries = table.Entries;
        foreach (var e in _entries)
            Log.Debug($"[EnvPreCreate] [{e.Mod}] {e}.");

        if (_entries.Count > 0) Subscribe();

        if (skipped > 0 || unresolved > 0)
            Log.Info($"[EnvPreCreate] {_entries.Count} environment(s) registered ({skipped} issue(s), {unresolved} unresolved"
                + (unresolved > 0 ? $": {UnresolvedUid.Sample(missed)}" : "") + ").");
        else
            Log.Info($"[EnvPreCreate] {_entries.Count} environment(s) registered.");
    }

    // Once per process: OnRunStart walks the current entry list, so a second LoadAll only replaces it.
    private static void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        GameManager.OnGMInitialized -= OnRunStart;
        GameManager.OnGMInitialized += OnRunStart;
    }

    private static void OnRunStart()
    {
        try
        {
            var gm = CardUtil.GetGameManagerInstance() as GameManager;
            if (gm == null)
            {
                Log.Warn("[EnvPreCreate] GameManager.Instance is null at OnGMInitialized - skipping this run.");
                return;
            }

            var method = ResolveGetEnvSaveData(gm);
            if (method == null)
            {
                Log.Warn("[EnvPreCreate] GameManager.GetEnvSaveData(EnvID,bool) not found - skipping this run.");
                return;
            }

            var result = EnvPreCreateTable.Run(_entries,
                uid => Invoke(method, gm, uid, false) != null,
                uid =>
                {
                    try { return Invoke(method, gm, uid, true) != null; }
                    catch (Exception ex)
                    {
                        Log.Warn($"[EnvPreCreate] pre-create failed for '{uid}': {Log.ExceptionText(ex)}");
                        return false;
                    }
                });

            if (result.Failed > 0)
                Log.Warn($"[EnvPreCreate] pre-created {result.Created}, already present {result.AlreadyPresent}, failed {result.Failed} of {_entries.Count}.");
            else
                Log.Debug($"[EnvPreCreate] pre-created {result.Created}, already present {result.AlreadyPresent} of {_entries.Count}.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[EnvPreCreate] failed: {Log.ExceptionText(ex)}");
        }
    }

    private static object Invoke(MethodInfo method, GameManager gm, string uid, bool createIfNull)
        => method.Invoke(gm, new object[] { new EnvID(uid), createIfNull });

    // GetEnvSaveData has a zero-arg convenience overload too (GetEnvSaveData() -> GetEnvSaveData(CurrentEnvironment,
    // true)); filter on arity so the 2-arg one this needs is never ambiguous (WorldMapInjector.PreCreateCloneEnvSaveData
    // does the same for the same reason).
    private static MethodInfo ResolveGetEnvSaveData(GameManager gm)
    {
        if (_getEnvSaveData != null) return _getEnvSaveData;
        _getEnvSaveData = gm.GetType().GetMethods(InstanceAll)
            .FirstOrDefault(m => m.Name == "GetEnvSaveData" && m.GetParameters().Length == 2);
        return _getEnvSaveData;
    }

    private static bool EnvExists(string uid) => GameRegistry.GetByUid(uid) is CardData;
}
