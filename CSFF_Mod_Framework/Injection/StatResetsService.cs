using CSFFModFramework.Api;
using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>
/// Declarative one-time stat resets, the game side (framework 2.29.0, fleet plan 6.7, W0.10 row 7). Every row a mod
/// declares in <c>StatResets.json</c> is tried on each <see cref="TickEvents.DtpTick"/> until its latch reads done,
/// and a <c>"When": "RunStart"</c> row is also tried at <c>GameManager.OnGMInitialized</c>, so a save is reset as it
/// loads. One try is <see cref="StatResetsTable.Attempt"/>: read the latch, read the base, move the base to the
/// target through <see cref="StatAccess.Modify(string, float)"/> (the game's own permanent change, so statuses
/// update) and verify it, and only then write the latch. This replaces Community Mod Chest's and Sirus23's
/// <c>Patcher/ComfortFixupPatch.cs</c>: CMC's tick timing, verified write and <c>StatAccess.Modify</c>, Sirus23's
/// "the stat's own default" target (<c>"Target": "default"</c>) and run-start timing (<c>"When": "RunStart"</c>,
/// now with CMC's per-tick retry behind it), each mod's own latch and log lines.
///
/// <para><b>Timing.</b> <see cref="LoadAll"/> runs in the framework's load pass, after WarpResolver, so both UIDs
/// are checked against loaded stats; it replaces the row list and subscribes both events once per process
/// (CLAUDE.md "Per-Run Event Registration", pattern b): <c>OnGMInitialized</c> re-fires on every run start, and the
/// latch, not the subscription, keeps each reset to once per save. A row is tried only while a run is in
/// progress and both its stats are in this run's <c>StatsDict</c>; until then an attempt fails quietly and counts
/// toward the one warning per cause <see cref="StatResetsTable.WarnAfterFailures"/> failures in a row give.</para>
///
/// <para><b>Log lines.</b> A row's lines carry its <c>LogTag</c> (the two Comfort rows keep <c>ComfortFixupPatch</c>
/// and <c>ComfortFixup</c>, so the lines their harness rows assert on are unchanged): the reset line at Info, the
/// "already at the target" line at BepInEx Debug whatever <c>VerboseLogging</c> says, as the mods' own were.</para>
/// </summary>
internal static class StatResetsService
{
    private sealed class Row
    {
        public StatResetDefinition Def;
        public readonly StatResetState State = new();
    }

    private static List<Row> _rows = new();
    private static bool _subscribed;
    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    internal static int ResetCount => _rows.Count;

    /// <summary>Reads every mod's <c>StatResets.json</c> in load order, validates and merges them, and arms the resets.</summary>
    public static void LoadAll(List<ModManifest> mods)
    {
        var names = new List<string>();
        var texts = new List<string>();
        foreach (var mod in mods)
        {
            var path = Path.Combine(mod.DirectoryPath, StatResetsTable.FileName);
            if (!File.Exists(path)) continue;
            try
            {
                texts.Add(File.ReadAllText(path));
                names.Add(mod.Name);
            }
            catch (Exception ex)
            {
                Log.Warn($"[StatResets] [{mod.Name}] could not read {StatResetsTable.FileName}: {Log.ExceptionText(ex)}");
            }
        }

        var table = StatResetsTable.Build(names, texts, StatExists);

        int skipped = 0, unresolved = 0;
        var missed = new List<string>();
        foreach (var issue in table.Issues)
        {
            if (issue.UnresolvedUid != null)
            {
                if (UnresolvedUid.ExpectedPresent(issue.UnresolvedUid))
                    Log.Warn($"[StatResets] {issue.Message} (UID '{issue.UnresolvedUid}' does not resolve)");
                else
                    Log.Debug($"[StatResets] {issue.Message} (UID '{issue.UnresolvedUid}' does not resolve; a partner mod not installed?)");
                unresolved++;
                missed.Add(issue.UnresolvedUid);
                continue;
            }
            Log.Warn($"[StatResets] {issue.Message}");
            skipped++;
        }

        _rows = table.Resets.Select(d => new Row { Def = d }).ToList();
        foreach (var row in _rows)
            Log.Debug($"[StatResets] [{row.Def.Mod}] {row.Def}.");

        if (_rows.Count > 0) Subscribe();

        if (skipped > 0 || unresolved > 0)
            Log.Info($"[StatResets] {_rows.Count} reset(s) loaded ({skipped} issue(s), {unresolved} unresolved"
                + (unresolved > 0 ? $": {UnresolvedUid.Sample(missed)}" : "") + ").");
        else
            Log.Info($"[StatResets] {_rows.Count} reset(s) loaded.");
    }

    // Once per process: both handlers walk the current row list, so a second LoadAll only replaces the list.
    private static void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        TickEvents.DtpTick += OnTick;
        try
        {
            GameManager.OnGMInitialized -= OnRunStart;
            GameManager.OnGMInitialized += OnRunStart;
        }
        catch (Exception ex)
        {
            WarnOnce("run-start", $"[StatResets] could not subscribe to GameManager.OnGMInitialized ({Log.ExceptionText(ex)}); \"RunStart\" resets wait for the first tick instead.");
        }
    }

    private static void OnRunStart()
    {
        foreach (var row in _rows)
            if (row.Def.When == StatResetWhen.RunStart) Try(row);
    }

    private static void OnTick()
    {
        foreach (var row in _rows) Try(row);
    }

    private static void Try(Row row)
    {
        var def = row.Def;
        var gm = CardUtil.GetGameManagerInstance() as GameManager;
        var latch = gm == null ? null : LiveStat(gm, def.Latch);
        var stat = gm == null ? null : LiveStat(gm, def.Stat);

        float target = float.NaN;
        if (!def.TargetIsDefault) target = def.TargetValue;
        else if (GameRegistry.GetByUid(def.Stat) is GameStat statDef) target = statDef.BaseValue;

        var result = StatResetsTable.Attempt(def, target, row.State,
            () => latch == null ? float.NaN : StatAccess.GetCurrentValue(latch),
            () => stat == null ? float.NaN : stat.CurrentBaseValue,
            t => SetBase(def, stat, t),
            () => latch != null && StatAccess.SetCurrentValue(latch, 1f));

        if (result.Warning != null) Log.Warn($"[{def.Mod}] {result.Warning}");
        if (result.Line == null) return;
        if (result.LineAtInfo) Log.Info(result.Line);
        else Log.DebugAlways(result.Line);
    }

    /// <summary>
    /// Moves the base to <paramref name="target"/> through the game's own permanent change and returns true only
    /// when it reads the target afterwards. If that change leaves it elsewhere, the base is written raw (the
    /// framework's setter clears the stat's condition cache) with one warning, as Community Mod Chest's
    /// <c>HiddenStat.SetBase</c> did.
    /// </summary>
    private static bool SetBase(StatResetDefinition def, InGameStat stat, float target)
    {
        if (stat == null) return false;
        float delta = target - stat.CurrentBaseValue;
        float after = float.NaN;
        if (!float.IsNaN(StatAccess.Modify(def.Stat, delta)))
        {
            after = stat.CurrentBaseValue;
            if (Math.Abs(after - target) < StatResetsTable.SameEpsilon) return true;
        }
        WarnOnce("modify-missed:" + def.Latch,
            $"[{def.Mod}] [{Tag(def)}] StatAccess.Modify('{def.Stat}', {delta:0.##}) left the base at {after:0.##}, not {target:0.##}; the base is written raw instead.");
        if (!Reflect.SetMember(stat, "CurrentBaseValue", target)) return false;
        return Math.Abs(stat.CurrentBaseValue - target) < StatResetsTable.SameEpsilon;
    }

    private static InGameStat LiveStat(GameManager gm, string uid)
    {
        if (GameRegistry.GetByUid(uid) is not GameStat def) return null;
        var dict = gm.StatsDict;
        return dict != null && dict.TryGetValue(def, out var live) && live ? live : null;
    }

    private static string Tag(StatResetDefinition def) => string.IsNullOrEmpty(def.LogTag) ? StatResetsTable.DefaultLogTag : def.LogTag;

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause)) Log.Warn(message);
    }

    private static bool StatExists(string uid) => GameRegistry.GetByUid(uid) is GameStat;
}
