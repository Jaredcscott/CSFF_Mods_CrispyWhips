using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>One non-clone environment a mod wants <c>EnvironmentsData</c> pre-created for at run start.</summary>
internal sealed class EnvPreCreateEntry
{
    public string Mod;
    public string EnvUid;
    public override string ToString() => $"'{EnvUid}'";
}

/// <summary>A listed UID that was left out, and why. <see cref="UnresolvedUid"/> is set only when a UID named no loaded card.</summary>
internal sealed class EnvPreCreateIssue
{
    public string Mod;
    public string UnresolvedUid;
    public string Message;
    public override string ToString() => Message;
}

/// <summary>What one run-start pass over the list did.</summary>
internal sealed class EnvPreCreateResult
{
    public int Created;
    public int AlreadyPresent;
    public int Failed;
}

/// <summary>
/// Declarative non-clone environment pre-create (fleet plan section 10, W0.10 row 3). A mod lists
/// its non-clone environment UIDs in <c>EnvPreCreate.json</c> beside <c>ModInfo.json</c>:
/// <code>[ "&lt;environment card UID&gt;", "&lt;environment card UID&gt;", ... ]</code>
/// This replaces Community Mod Chest's <c>Patcher/InteriorEnvSaveDataPatch.cs</c> (its 9 interior UIDs) and Far
/// Territory's <c>Patcher/EnvSaveDataPreCreate.cs</c> (its ~50 board UIDs): same fix, only the UID list differed.
/// The framework already does this exact thing for clone nodes
/// (<c>Injection/WorldMapInjector.PreCreateCloneEnvSaveData</c>); this generalizes it to a declarative list any mod
/// can ship, WITHOUT that method's extra empty-entry reseed logic: neither deleted patch had it, so this keeps
/// the same first-seed behavior those patches had (a board's first seed is frozen at the version that first
/// created it, per Far Territory's own comment).
///
/// <para><see cref="Build"/> validates and merges every mod's file in load order (pure, BCL types only, so
/// <c>Development_Tools/Tests/Framework-EnvPreCreate.Tests.ps1</c> runs it outside the game); <see cref="Run"/> is
/// the one run-start pass over the merged list: pre-create whatever is missing, leave whatever already exists
/// untouched (the game's own <c>GetEnvSaveData(_CreateIfNull:true)</c> is also idempotent, but checking first lets
/// this report the two counts separately and lets a test exercise the idempotency without the game).
/// <see cref="EnvPreCreateService"/> is the game side.</para>
/// </summary>
internal sealed class EnvPreCreateTable
{
    /// <summary>The file a mod ships beside <c>ModInfo.json</c>.</summary>
    internal const string FileName = "EnvPreCreate.json";

    public readonly List<EnvPreCreateEntry> Entries = new();
    public readonly List<EnvPreCreateIssue> Issues = new();

    // A UID declared twice (same mod or two mods) would otherwise just look like one entry done twice; both
    // serve no purpose, so the second is flagged and skipped like KitContentsTable's "already declared by".
    private readonly Dictionary<string, EnvPreCreateEntry> _byUid = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The entry for an environment UID (any case), or null.</summary>
    internal EnvPreCreateEntry FindByUid(string uid)
        => !string.IsNullOrEmpty(uid) && _byUid.TryGetValue(uid, out var e) ? e : null;

    /// <summary>
    /// Validates and merges the files of several mods, given in load order (<paramref name="modNames"/>[i] shipped
    /// <paramref name="jsonTexts"/>[i]). <paramref name="envExists"/> says whether a UID names a loaded card. An
    /// entry is left out, with one issue, when: the file as a whole is not a JSON array; an entry is not a
    /// non-empty string; the same UID (any case) was already listed by an earlier mod (or earlier in the same
    /// file); or the UID names no loaded card.
    /// </summary>
    internal static EnvPreCreateTable Build(IList<string> modNames, IList<string> jsonTexts, Func<string, bool> envExists)
        => BuildWithTypeCheck(modNames, jsonTexts, envExists, null);

    /// <summary>
    /// <see cref="Build"/>, plus a refusal of a UID that resolves to something other than an Environment card.
    /// <paramref name="describeNonEnvironment"/> returns null for an Environment card and a short description of
    /// what the UID names otherwise ("a card of CardType Item", "an object of type CharacterPerk"); such an entry is left out with one issue
    /// naming the mod, the file, the entry and the UID (its <see cref="EnvPreCreateIssue.UnresolvedUid"/> stays
    /// null: the UID did resolve, to the wrong kind of object). The game builds the run-start EnvID from the UID
    /// string, a constructor with none of the <c>CardTypes.Environment</c> guard its CardData constructor has
    /// (<c>.decomp/EnvID.cs</c>), so without this an item or liquid UID typed here would add a junk
    /// <c>EnvironmentsData</c> entry to every save. Null = no type check (what <see cref="Build"/> passes).
    /// A separate name, not an overload: <c>Development_Tools/Tests/Framework-EnvPreCreate.Tests.ps1</c> finds
    /// <c>Build</c> by name and an overload would make that lookup ambiguous.
    /// </summary>
    internal static EnvPreCreateTable BuildWithTypeCheck(IList<string> modNames, IList<string> jsonTexts,
        Func<string, bool> envExists, Func<string, string> describeNonEnvironment)
    {
        var table = new EnvPreCreateTable();
        if (modNames == null || jsonTexts == null) return table;
        int n = Math.Min(modNames.Count, jsonTexts.Count);
        for (int i = 0; i < n; i++)
            table.AddFile(modNames[i] ?? "", jsonTexts[i], envExists, describeNonEnvironment);
        return table;
    }

    /// <summary>
    /// One run-start pass over <paramref name="entries"/>: for each, <paramref name="exists"/> says whether its
    /// <c>EnvironmentsData</c> entry is already there (left untouched, counted <see cref="EnvPreCreateResult.AlreadyPresent"/>);
    /// if not, <paramref name="create"/> is called (its own return says whether the create succeeded) and the
    /// entry is counted <see cref="EnvPreCreateResult.Created"/> or <see cref="EnvPreCreateResult.Failed"/>. Pure:
    /// both delegates are supplied by the caller, so this runs outside the game with fakes.
    /// </summary>
    internal static EnvPreCreateResult Run(IEnumerable<EnvPreCreateEntry> entries, Func<string, bool> exists, Func<string, bool> create)
    {
        var result = new EnvPreCreateResult();
        if (entries == null) return result;
        foreach (var entry in entries)
        {
            if (exists != null && exists(entry.EnvUid)) { result.AlreadyPresent++; continue; }
            if (create != null && create(entry.EnvUid)) result.Created++;
            else result.Failed++;
        }
        return result;
    }

    private void AddFile(string mod, string json, Func<string, bool> envExists, Func<string, string> describeNonEnvironment)
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
            Issue(mod, null, $"[{mod}] {FileName} must be a JSON array of environment UID strings; the whole file is skipped.");
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            var where = $"[{mod}] {FileName} entry {i}";
            if (list[i] is not string uid || string.IsNullOrEmpty(uid))
            {
                Issue(mod, null, $"{where} must be a non-empty string environment UID; skipped.");
                continue;
            }

            var taken = FindByUid(uid);
            if (taken != null)
            {
                Issue(mod, null, $"{where} ('{uid}') is already listed by [{taken.Mod}], which loads first and keeps it; skipped.");
                continue;
            }

            if (envExists == null || !envExists(uid))
            {
                Issue(mod, uid, $"{where} ('{uid}') names no loaded environment card; skipped.");
                continue;
            }

            var wrongKind = describeNonEnvironment?.Invoke(uid);
            if (!string.IsNullOrEmpty(wrongKind))
            {
                Issue(mod, null, $"{where} ('{uid}') names {wrongKind}, not an Environment card (CardType 4), so no environment can be pre-created for it; skipped.");
                continue;
            }

            var entry = new EnvPreCreateEntry { Mod = mod, EnvUid = uid };
            Entries.Add(entry);
            _byUid[uid] = entry;
        }
    }

    private void Issue(string mod, string unresolvedUid, string message)
        => Issues.Add(new EnvPreCreateIssue { Mod = mod, UnresolvedUid = unresolvedUid, Message = message });
}
