using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>When a stat reset is first tried in a run.</summary>
internal enum StatResetWhen
{
    /// <summary>On the first in-game tick (<c>TickEvents.DtpTick</c>) after a run starts, when saved stats are certainly in place.</summary>
    FirstTick,

    /// <summary>At run start (<c>GameManager.OnGMInitialized</c>), so a save is reset as it loads; retried on every tick until done.</summary>
    RunStart,
}

/// <summary>One stat a mod resets once per save, latched by a hidden saved stat of its own.</summary>
internal sealed class StatResetDefinition
{
    public string Mod;
    public string Stat;
    public string Latch;

    /// <summary>Reset the base to the stat's own default (<c>GameStat.BaseValue</c>) instead of <see cref="TargetValue"/>.</summary>
    public bool TargetIsDefault = true;
    public float TargetValue;

    public StatResetWhen When = StatResetWhen.FirstTick;
    public string LogTag;

    /// <summary>The Info line written when the reset ran; <c>{before}</c> and <c>{target}</c> are filled in. Null = a default line.</summary>
    public string LogReset;

    /// <summary>
    /// The Debug line written when the base already read the target; null = write <see cref="LogReset"/> at Info
    /// either way (one line per save whatever it found, as Sirus23's own reset did).
    /// </summary>
    public string LogUnchanged;

    public override string ToString()
        => $"'{Stat}' to {(TargetIsDefault ? "its default" : TargetValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))}, latch '{Latch}', {When}";
}

/// <summary>A row or field that was left out, and why. <see cref="UnresolvedUid"/> is set only when a UID named no stat.</summary>
internal sealed class StatResetIssue
{
    public string Mod;
    public string UnresolvedUid;
    public string Message;

    public override string ToString() => Message;
}

/// <summary>What one attempt did.</summary>
internal enum StatResetOutcome
{
    /// <summary>The latch already reads done: nothing was read or written.</summary>
    AlreadyLatched,

    /// <summary>The base already read the target: the latch was written, the base was not.</summary>
    Unchanged,

    /// <summary>The base was moved to the target and verified, then the latch was written.</summary>
    Reset,

    /// <summary>A read or write failed: nothing is latched and the next tick tries again.</summary>
    Failed,
}

/// <summary>The result of <see cref="StatResetsTable.Attempt"/>: the outcome, the log line to write (if any) and its level.</summary>
internal sealed class StatResetResult
{
    public StatResetOutcome Outcome;

    /// <summary>The cause key of a failure (<c>latch-unreadable</c>, <c>stat-unreadable</c>, <c>target-unreadable</c>, <c>stat-write</c>, <c>latch-write</c>, <c>exception</c>).</summary>
    public string Cause;

    /// <summary>A line to write, already tagged "<c>[LogTag] </c>", or null.</summary>
    public string Line;

    /// <summary>True: <see cref="Line"/> goes out at Info. False: at Debug (always, whatever VerboseLogging says).</summary>
    public bool LineAtInfo;

    /// <summary>A warning to write (the eighth failed attempt in a row, once per cause), or null.</summary>
    public string Warning;
}

/// <summary>The per-row retry state <see cref="StatResetsTable.Attempt"/> keeps between ticks.</summary>
internal sealed class StatResetState
{
    public int FailedAttempts;
    public readonly HashSet<string> Warned = new(StringComparer.Ordinal);
}

/// <summary>
/// Declarative one-time stat resets (framework 2.29.0, fleet plan 6.7, W0.10 row 7). A mod that once wrote a stat
/// permanently, and stopped, cannot undo what older versions left in a save by removing the write: it resets the
/// stat's BASE once per save, latched by a hidden saved stat of its own. Community Mod Chest and Sirus23 each did
/// this for vanilla Comfort with a <c>Patcher/ComfortFixupPatch.cs</c> of their own (two independent
/// implementations, not a fork); a mod now lists its resets in <c>StatResets.json</c> beside <c>ModInfo.json</c>:
/// <code>
/// [ { "Stat": "&lt;GameStat UID&gt;", "Latch": "&lt;hidden saved GameStat UID, 0..1&gt;", "Target": "default",
///     "When": "FirstTick", "LogTag": "...", "LogReset": "... base {before} -> {target} ...", "LogUnchanged": "..." } ]
/// </code>
/// <see cref="Build"/> validates and merges every mod's file in load order; <see cref="Attempt"/> is one try at a
/// row (read the latch, read the base, write and verify the base, then write the latch, in that order). Both are
/// pure (BCL types only, every game read and write is a delegate), so
/// <c>Development_Tools/Tests/Framework-StatResets.Tests.ps1</c> runs them outside the game.
/// <see cref="StatResetsService"/> is the game side. The line-level diff of the two copies and which fix came from
/// which: <c>Documentation/Research/Fleet/APE_Port/W0.10_redundant_code.md</c> ("As built, Prompt 26").
/// </summary>
internal sealed class StatResetsTable
{
    /// <summary>The file a mod ships beside <c>ModInfo.json</c>.</summary>
    internal const string FileName = "StatResets.json";

    /// <summary>The tag a row's lines carry when its <c>LogTag</c> is not set.</summary>
    internal const string DefaultLogTag = "StatResets";

    /// <summary>Failed attempts in a row before a cause is warned about (once per cause). A booting run fails a few.</summary>
    internal const int WarnAfterFailures = 8;

    /// <summary>A latch at or above this reads "done".</summary>
    internal const float LatchDone = 0.5f;

    /// <summary>A base this close to the target needs no write.</summary>
    internal const float WriteEpsilon = 0.0001f;

    /// <summary>A base this close to the target counts as "already at the target" for the log line, and as a verified write.</summary>
    internal const float SameEpsilon = 0.01f;

    private static readonly string[] RowKeys = { "Stat", "Latch", "Target", "When", "LogTag", "LogReset", "LogUnchanged" };

    public readonly List<StatResetDefinition> Resets = new();
    public readonly List<StatResetIssue> Issues = new();

    // A latch is a per-save "done" flag: two rows sharing one would let the first latch the second's work away.
    private readonly Dictionary<string, StatResetDefinition> _byLatch = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The row latched by a stat UID (any case), or null.</summary>
    internal StatResetDefinition FindByLatch(string latchUid)
        => !string.IsNullOrEmpty(latchUid) && _byLatch.TryGetValue(latchUid, out var row) ? row : null;

    /// <summary>
    /// Validates and merges the files of several mods, given in load order (<paramref name="modNames"/>[i] shipped
    /// <paramref name="jsonTexts"/>[i]). <paramref name="statExists"/> says whether a UID names a loaded GameStat.
    /// A row is left out, with one issue, when: it is not an object; <c>Stat</c> or <c>Latch</c> is missing;
    /// <c>Latch</c> equals <c>Stat</c>; an earlier row (any mod) already uses that latch (any case);
    /// <c>Target</c> is neither <c>"default"</c> nor a finite number; <c>When</c> is neither <c>FirstTick</c> nor
    /// <c>RunStart</c>; or the stat or the latch names no loaded GameStat. Two rows may reset the same stat with
    /// different latches (Community Mod Chest and Sirus23 both reset Comfort): each runs once per save, and the
    /// second finds the base already at its target. An unknown key (one not starting with <c>_</c>, which marks
    /// a comment) is reported and ignored; a log key that is not a string is reported and read as absent.
    /// </summary>
    internal static StatResetsTable Build(IList<string> modNames, IList<string> jsonTexts, Func<string, bool> statExists)
    {
        var table = new StatResetsTable();
        if (modNames == null || jsonTexts == null) return table;
        int n = Math.Min(modNames.Count, jsonTexts.Count);
        for (int i = 0; i < n; i++)
            table.AddFile(modNames[i] ?? "", jsonTexts[i], statExists);
        return table;
    }

    /// <summary>
    /// One try at a row, in this order: read the latch (unreadable: fail; done: stop); read the base (unreadable:
    /// fail); unless the base is within <see cref="WriteEpsilon"/> of <paramref name="target"/>, write it and stop
    /// on a failed write (<paramref name="setBase"/> returns true only when the base reads the target
    /// afterwards); only then write the latch. So a failed write retries on the next tick instead of latching a
    /// save that was not reset, and a failed latch write leaves the base reset (the next attempt finds it at the
    /// target and only latches). <paramref name="state"/> counts failed attempts in a row: the
    /// <see cref="WarnAfterFailures"/>th and later produce one warning per cause; a success clears the count.
    /// Never throws (a throwing delegate is a failure with cause <c>exception</c>).
    /// </summary>
    internal static StatResetResult Attempt(StatResetDefinition row, float target, StatResetState state,
        Func<float> readLatch, Func<float> readBase, Func<float, bool> setBase, Func<bool> setLatch)
    {
        state ??= new StatResetState();
        var tag = Tag(row);
        try
        {
            float latch = readLatch();
            if (float.IsNaN(latch) || latch < 0f)
                return Fail(state, tag, "latch-unreadable", $"the latch '{row.Latch}' is not readable");
            if (latch >= LatchDone)
            {
                state.FailedAttempts = 0;
                return new StatResetResult { Outcome = StatResetOutcome.AlreadyLatched };
            }

            float before = readBase();
            if (float.IsNaN(before))
                return Fail(state, tag, "stat-unreadable", $"the stat '{row.Stat}' is not readable");
            if (float.IsNaN(target) || float.IsInfinity(target))
                return Fail(state, tag, "target-unreadable", $"the stat '{row.Stat}' has no readable default to reset to");

            if (Math.Abs(target - before) >= WriteEpsilon && !setBase(target))
                return Fail(state, tag, "stat-write", $"the base of '{row.Stat}' could not be set to {Num(target)}");

            if (!setLatch())
                return Fail(state, tag, "latch-write", $"the latch '{row.Latch}' could not be written (the reset runs again next tick, which is harmless)");

            state.FailedAttempts = 0;
            bool unchanged = Math.Abs(before - target) < SameEpsilon;
            var result = new StatResetResult { Outcome = unchanged ? StatResetOutcome.Unchanged : StatResetOutcome.Reset };
            if (unchanged && row.LogUnchanged != null)
            {
                result.Line = tag + Fill(row.LogUnchanged, before, target);
                result.LineAtInfo = false;
            }
            else
            {
                result.Line = tag + Fill(row.LogReset ?? "One-time reset of '{stat}': base {before} -> {target}.", before, target).Replace("{stat}", row.Stat);
                result.LineAtInfo = true;
            }
            return result;
        }
        catch (Exception ex)
        {
            return Fail(state, tag, "exception", "the attempt threw: " + (ex.InnerException?.ToString() ?? ex.ToString()));
        }
    }

    /// <summary>Fills <c>{before}</c> and <c>{target}</c> (two decimals at most, invariant culture).</summary>
    internal static string Fill(string template, float before, float target)
        => (template ?? "").Replace("{before}", Num(before)).Replace("{target}", Num(target));

    private static string Num(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string Tag(StatResetDefinition row)
        => $"[{(row == null || string.IsNullOrEmpty(row.LogTag) ? DefaultLogTag : row.LogTag)}] ";

    private static StatResetResult Fail(StatResetState state, string tag, string cause, string what)
    {
        var result = new StatResetResult { Outcome = StatResetOutcome.Failed, Cause = cause };
        if (++state.FailedAttempts >= WarnAfterFailures && state.Warned.Add(cause))
            result.Warning = $"{tag}{what}; the one-time reset keeps retrying each tick.";
        return result;
    }

    private void AddFile(string mod, string json, Func<string, bool> statExists)
    {
        object root;
        try { root = MiniJson.Parse(json ?? ""); }
        catch (Exception ex)
        {
            Issue(mod, null, $"[{mod}] {FileName} is not valid JSON ({ex.GetType().Name}: {ex.Message}); the whole file is skipped.");
            return;
        }
        if (root is not List<object> list)
        {
            Issue(mod, null, $"[{mod}] {FileName} must be a JSON array of reset objects ({{ \"Stat\", \"Latch\" }}); the whole file is skipped.");
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            var where = $"[{mod}] {FileName} row {i}";
            if (list[i] is not Dictionary<string, object> obj)
            {
                Issue(mod, null, $"{where} is not an object; skipped.");
                continue;
            }

            var stat = ReadString(obj, "Stat");
            var latch = ReadString(obj, "Latch");
            if (string.IsNullOrEmpty(stat) || string.IsNullOrEmpty(latch))
            {
                Issue(mod, null, $"{where} needs a \"Stat\" and a \"Latch\" GameStat UID; skipped.");
                continue;
            }
            where = $"{where} ('{stat}', latch '{latch}')";
            ReportUnknownKeys(mod, where, obj, RowKeys);

            if (string.Equals(stat, latch, StringComparison.OrdinalIgnoreCase))
            {
                Issue(mod, null, $"{where}: the latch is the stat itself, so the reset would clear its own latch; skipped.");
                continue;
            }

            var row = new StatResetDefinition { Mod = mod, Stat = stat, Latch = latch };

            if (obj.TryGetValue("Target", out var rawTarget) && rawTarget != null)
            {
                if (rawTarget is string ts && string.Equals(ts, "default", StringComparison.OrdinalIgnoreCase))
                {
                    row.TargetIsDefault = true;
                }
                else if (rawTarget is double td && !double.IsNaN(td) && !double.IsInfinity(td) && Math.Abs(td) <= float.MaxValue)
                {
                    row.TargetIsDefault = false;
                    row.TargetValue = (float)td;
                }
                else
                {
                    Issue(mod, null, $"{where}: \"Target\" must be \"default\" or a number; skipped.");
                    continue;
                }
            }

            if (obj.TryGetValue("When", out var rawWhen) && rawWhen != null)
            {
                if (rawWhen is string ws && string.Equals(ws, "FirstTick", StringComparison.OrdinalIgnoreCase)) row.When = StatResetWhen.FirstTick;
                else if (rawWhen is string ws2 && string.Equals(ws2, "RunStart", StringComparison.OrdinalIgnoreCase)) row.When = StatResetWhen.RunStart;
                else
                {
                    Issue(mod, null, $"{where}: \"When\" must be \"FirstTick\" or \"RunStart\"; skipped.");
                    continue;
                }
            }

            // Before the stat checks: "already used" is the useful report whether or not that spelling resolves.
            var taken = FindByLatch(latch);
            if (taken != null)
            {
                Issue(mod, null, $"{where}: the latch is already used by [{taken.Mod}]'s reset of '{taken.Stat}', which loads first and keeps it; "
                    + $"one latch cannot record two resets, so [{mod}]'s is skipped.");
                continue;
            }

            if (statExists == null || !statExists(stat))
            {
                Issue(mod, stat, $"{where}: the stat '{stat}' names no loaded GameStat; skipped.");
                continue;
            }
            if (!statExists(latch))
            {
                Issue(mod, latch, $"{where}: the latch '{latch}' names no loaded GameStat (a mod ships it as a hidden saved stat, 0 to 1); skipped.");
                continue;
            }

            row.LogTag = ReadOptionalString(mod, where, obj, "LogTag");
            row.LogReset = ReadOptionalString(mod, where, obj, "LogReset");
            row.LogUnchanged = ReadOptionalString(mod, where, obj, "LogUnchanged");

            Resets.Add(row);
            _byLatch[latch] = row;
        }
    }

    private static string ReadString(Dictionary<string, object> obj, string key)
        => obj.TryGetValue(key, out var v) ? v as string : null;

    // A present non-string value is reported and read as absent.
    private string ReadOptionalString(string mod, string where, Dictionary<string, object> obj, string key)
    {
        if (!obj.TryGetValue(key, out var v) || v == null) return null;
        if (v is string s) return s;
        Issue(mod, null, $"{where}: \"{key}\" must be a string; ignored.");
        return null;
    }

    private void ReportUnknownKeys(string mod, string where, Dictionary<string, object> obj, string[] known)
    {
        foreach (var key in obj.Keys)
        {
            if (key.StartsWith("_", StringComparison.Ordinal) || Array.IndexOf(known, key) >= 0) continue;
            Issue(mod, null, $"{where}: unknown key \"{key}\" ignored (expected {string.Join(", ", known)}).");
        }
    }

    private void Issue(string mod, string unresolvedUid, string message)
        => Issues.Add(new StatResetIssue { Mod = mod, UnresolvedUid = unresolvedUid, Message = message });
}
