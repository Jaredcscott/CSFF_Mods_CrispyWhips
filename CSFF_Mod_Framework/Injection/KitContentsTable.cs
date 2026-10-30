using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>One card a kit hands over when it is unpacked: <see cref="Count"/> copies of <see cref="Card"/>.</summary>
internal sealed class KitItem
{
    public string Card;
    public int Count;

    /// <summary>Durability values each copy spawns with (JSON-side stat names), or null for the card's defaults.</summary>
    public Dictionary<string, float> Stats;

    /// <summary>Spawn only when the card is loaded when the kit is unpacked; otherwise skip it with a Debug line.</summary>
    public bool OnlyIfCardExists;

    /// <summary>The Debug line written when an <see cref="OnlyIfCardExists"/> item is spawned; null = a default line.</summary>
    public string LogIfPresent;

    /// <summary>The Debug line written when an <see cref="OnlyIfCardExists"/> item is skipped; null = a default line.</summary>
    public string LogIfMissing;
}

/// <summary>One kit card and what unpacking it spawns.</summary>
internal sealed class KitDefinition
{
    public string Mod;
    public string Kit;
    public string Name;
    public string LogTag;
    public string ActionNamePrefix;
    public string ActionKeyPrefix;
    public readonly List<KitItem> Items = new();

    public override string ToString() => $"{Kit} ({Items.Count} item row(s))";
}

/// <summary>A kit, item or field that was left out, and why. <see cref="UnresolvedUid"/> is set only when a UID named no card.</summary>
internal sealed class KitContentsIssue
{
    public string Mod;
    public string UnresolvedUid;
    public string Message;

    public override string ToString() => Message;
}

/// <summary>
/// One step of an unpack, in order: spawn <see cref="Count"/> copies of <see cref="Card"/> with <see cref="Stats"/>
/// (when <see cref="Card"/> is set), then write <see cref="Log"/> at Debug level (when it is set).
/// </summary>
internal sealed class KitStep
{
    public string Card;
    public int Count;
    public Dictionary<string, float> Stats;
    public string Log;
}

/// <summary>
/// Declarative kit contents (framework 2.29.0, fleet plan 6.7, W0.10 row 4). A kit is a card whose own action
/// (a "Place" DismantleAction that destroys the kit) hands over a fixed list of cards. Vanilla never processes
/// <c>CardsToCreate</c> on a DismantleAction's <c>ReceivingCardChanges</c>, so until 2.29.0 each mod spawned the
/// list from its own <c>ActionRouter.AfterWrapped</c> handler (Community Mod Chest's and HomesteadPerks'
/// <c>Patcher/HomesteadKitPatch.cs</c>, a drifted fork). A mod now lists its kits in <c>KitContents.json</c>
/// beside <c>ModInfo.json</c>:
/// <code>
/// [ { "Kit": "&lt;kit card UID&gt;", "ActionNamePrefix": "Place", "Name": "Homestead", "LogTag": "Kits",
///     "Items": [ { "Card": "&lt;UID&gt;", "Count": 15, "Stats": { "SpecialDurability4": 1 } },
///                { "Card": "&lt;UID&gt;", "Count": 20, "OnlyIfCardExists": true,
///                  "LogIfPresent": "...", "LogIfMissing": "..." } ] } ]
/// </code>
/// <see cref="Build"/> validates and merges every mod's file in load order; <see cref="Plan"/> is the unpack
/// itself (which cards to spawn, in order, and the Debug lines to write). Both are pure (BCL types only, the
/// card lookups are delegates), so <c>Development_Tools/Tests/Framework-KitContents.Tests.ps1</c> runs them
/// outside the game. <see cref="KitContentsService"/> is the game side. Research and the line-level diff of the
/// two forks: <c>Documentation/Research/Fleet/APE_Port/W0.10_redundant_code.md</c> ("As built, Prompt 25").
/// </summary>
internal sealed class KitContentsTable
{
    /// <summary>The file a mod ships beside <c>ModInfo.json</c>.</summary>
    internal const string FileName = "KitContents.json";

    /// <summary>The tag a kit's log lines carry when its <c>LogTag</c> is not set.</summary>
    internal const string DefaultLogTag = "Kits";

    /// <summary>
    /// The most copies one item row may spawn. A kit spawns every copy in the frame its action ends; the
    /// largest real row is 20.
    /// </summary>
    internal const int MaxCount = 100;

    /// <summary>
    /// The stat names <c>SpawnService.Spawn</c> applies (its <c>StatNames</c>, held equal by the Pester spec).
    /// Any other name would be ignored with a Warn on every spawn, so it is dropped here once, at load.
    /// </summary>
    internal static readonly string[] StatNames =
    {
        "SpoilageTime", "UsageDurability", "FuelCapacity", "Progress",
        "SpecialDurability1", "SpecialDurability2", "SpecialDurability3", "SpecialDurability4",
    };

    private static readonly string[] KitKeys = { "Kit", "Name", "LogTag", "ActionNamePrefix", "ActionKeyPrefix", "Items" };
    private static readonly string[] ItemKeys = { "Card", "Count", "Stats", "OnlyIfCardExists", "LogIfPresent", "LogIfMissing" };

    public readonly List<KitDefinition> Kits = new();
    public readonly List<KitContentsIssue> Issues = new();

    // ActionRouter matches a handler's CardUid case-insensitively, so two kits whose UIDs differ only in case
    // would both fire on one card.
    private readonly Dictionary<string, KitDefinition> _byKit = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The kit declared for a card UID (any case), or null.</summary>
    internal KitDefinition Find(string kitUid)
        => !string.IsNullOrEmpty(kitUid) && _byKit.TryGetValue(kitUid, out var kit) ? kit : null;

    /// <summary>
    /// Validates and merges the files of several mods, given in load order (<paramref name="modNames"/>[i]
    /// shipped <paramref name="jsonTexts"/>[i]). <paramref name="cardExists"/> says whether a UID names a loaded
    /// card. A kit is left out, with one issue, when: it is not an object; <c>Kit</c> is missing; it names
    /// neither <c>ActionNamePrefix</c> nor <c>ActionKeyPrefix</c> (the handler would fire on every action on the
    /// card); <c>Items</c> is not a non-empty array; an earlier kit (any mod) already declared that card (any
    /// case); the kit card names no loaded card; or none of its items survives. An item is left out when: it is not an object;
    /// <c>Card</c> is missing; <c>Count</c> is not a whole number from 1 to <see cref="MaxCount"/>;
    /// <c>OnlyIfCardExists</c> is not true or false; or its card names no loaded card and it is not
    /// <c>OnlyIfCardExists</c> (that one is checked again when the kit is unpacked). A stat that is not one of
    /// <see cref="StatNames"/>, or whose value is not a finite number, is dropped from its item. An unknown key
    /// (one not starting with <c>_</c>, which marks a comment) is reported and ignored.
    /// </summary>
    internal static KitContentsTable Build(IList<string> modNames, IList<string> jsonTexts, Func<string, bool> cardExists)
    {
        var table = new KitContentsTable();
        if (modNames == null || jsonTexts == null) return table;
        int n = Math.Min(modNames.Count, jsonTexts.Count);
        for (int i = 0; i < n; i++)
            table.AddFile(modNames[i] ?? "", jsonTexts[i], cardExists);
        return table;
    }

    /// <summary>
    /// The unpack of one kit, in the order its items are listed: each item becomes a spawn step, except an
    /// <see cref="KitItem.OnlyIfCardExists"/> item whose card <paramref name="cardExists"/> says is not loaded,
    /// which is skipped. An <see cref="KitItem.OnlyIfCardExists"/> item adds one log step after its spawn (or in
    /// place of it), and the last step logs "<c>{Name} contents spawned.</c>". Every log line starts with
    /// "<c>[{LogTag}] </c>".
    /// </summary>
    internal static List<KitStep> Plan(KitDefinition kit, Func<string, bool> cardExists)
    {
        var steps = new List<KitStep>();
        if (kit == null) return steps;
        var tag = $"[{(string.IsNullOrEmpty(kit.LogTag) ? DefaultLogTag : kit.LogTag)}] ";
        var name = string.IsNullOrEmpty(kit.Name) ? kit.Kit : kit.Name;

        foreach (var item in kit.Items)
        {
            if (!item.OnlyIfCardExists)
            {
                steps.Add(new KitStep { Card = item.Card, Count = item.Count, Stats = item.Stats });
                continue;
            }

            // One lookup per item, up front: a missing card would otherwise log one spawn error per copy.
            if (cardExists != null && cardExists(item.Card))
            {
                steps.Add(new KitStep { Card = item.Card, Count = item.Count, Stats = item.Stats });
                steps.Add(new KitStep { Log = tag + (item.LogIfPresent ?? $"{name}: '{item.Card}' is loaded: spawned {item.Count}.") });
            }
            else
            {
                steps.Add(new KitStep { Log = tag + (item.LogIfMissing ?? $"{name}: '{item.Card}' is not loaded: {item.Count} skipped.") });
            }
        }

        steps.Add(new KitStep { Log = tag + $"{name} contents spawned." });
        return steps;
    }

    private void AddFile(string mod, string json, Func<string, bool> cardExists)
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
            Issue(mod, null, $"[{mod}] {FileName} must be a JSON array of kit objects ({{ \"Kit\", \"ActionNamePrefix\", \"Items\" }}); the whole file is skipped.");
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            var where = $"[{mod}] {FileName} kit {i}";
            if (list[i] is not Dictionary<string, object> obj)
            {
                Issue(mod, null, $"{where} is not an object; skipped.");
                continue;
            }

            var kitUid = ReadString(obj, "Kit");
            if (string.IsNullOrEmpty(kitUid))
            {
                Issue(mod, null, $"{where} needs a \"Kit\" card UID; skipped.");
                continue;
            }
            where = $"{where} ('{kitUid}')";
            ReportUnknownKeys(mod, where, obj, KitKeys);

            var namePrefix = ReadOptionalString(mod, where, obj, "ActionNamePrefix");
            var keyPrefix = ReadOptionalString(mod, where, obj, "ActionKeyPrefix");
            if (string.IsNullOrEmpty(namePrefix) && string.IsNullOrEmpty(keyPrefix))
            {
                Issue(mod, null, $"{where} names neither \"ActionNamePrefix\" nor \"ActionKeyPrefix\": it would unpack on every action on the card; skipped.");
                continue;
            }

            if (!obj.TryGetValue("Items", out var rawItems) || rawItems is not List<object> items || items.Count == 0)
            {
                Issue(mod, null, $"{where}: \"Items\" must be a non-empty array; skipped.");
                continue;
            }

            // Before the card check: a redeclaration in another case names the same card to ActionRouter, and
            // "already declared" is the useful report whether or not that spelling resolves.
            var taken = Find(kitUid);
            if (taken != null)
            {
                Issue(mod, null, $"{where}: the kit card is already declared by [{taken.Mod}], which loads first and keeps it; "
                    + $"two declarations would unpack it twice, so [{mod}]'s is skipped.");
                continue;
            }

            if (cardExists == null || !cardExists(kitUid))
            {
                Issue(mod, kitUid, $"{where}: the kit card '{kitUid}' names no loaded card; skipped.");
                continue;
            }

            var kit = new KitDefinition
            {
                Mod = mod,
                Kit = kitUid,
                Name = ReadOptionalString(mod, where, obj, "Name"),
                LogTag = ReadOptionalString(mod, where, obj, "LogTag"),
                ActionNamePrefix = string.IsNullOrEmpty(namePrefix) ? null : namePrefix,
                ActionKeyPrefix = string.IsNullOrEmpty(keyPrefix) ? null : keyPrefix,
            };

            for (int j = 0; j < items.Count; j++)
            {
                var item = ReadItem(mod, $"{where} item {j}", items[j], cardExists);
                if (item != null) kit.Items.Add(item);
            }

            if (kit.Items.Count == 0)
            {
                Issue(mod, null, $"{where}: none of its items is usable; skipped.");
                continue;
            }

            Kits.Add(kit);
            _byKit[kitUid] = kit;
        }
    }

    private KitItem ReadItem(string mod, string where, object raw, Func<string, bool> cardExists)
    {
        if (raw is not Dictionary<string, object> obj)
        {
            Issue(mod, null, $"{where} is not an object; skipped.");
            return null;
        }

        var card = ReadString(obj, "Card");
        if (string.IsNullOrEmpty(card))
        {
            Issue(mod, null, $"{where} needs a \"Card\" UID; skipped.");
            return null;
        }
        where = $"{where} ('{card}')";
        ReportUnknownKeys(mod, where, obj, ItemKeys);

        if (!obj.TryGetValue("Count", out var rawCount) || rawCount is not double d
            || double.IsNaN(d) || d < 1 || d > MaxCount || d != Math.Floor(d))
        {
            Issue(mod, null, $"{where}: \"Count\" must be a whole number from 1 to {MaxCount}; skipped.");
            return null;
        }

        bool onlyIfExists = false;
        if (obj.TryGetValue("OnlyIfCardExists", out var rawOnly) && rawOnly != null)
        {
            if (rawOnly is not bool b)
            {
                Issue(mod, null, $"{where}: \"OnlyIfCardExists\" must be true or false; skipped.");
                return null;
            }
            onlyIfExists = b;
        }

        if (!onlyIfExists && (cardExists == null || !cardExists(card)))
        {
            Issue(mod, card, $"{where}: '{card}' names no loaded card; skipped (set \"OnlyIfCardExists\": true for a card another mod may not supply).");
            return null;
        }

        var item = new KitItem
        {
            Card = card,
            Count = (int)d,
            OnlyIfCardExists = onlyIfExists,
            LogIfPresent = ReadOptionalString(mod, where, obj, "LogIfPresent"),
            LogIfMissing = ReadOptionalString(mod, where, obj, "LogIfMissing"),
        };
        if (!onlyIfExists && (item.LogIfPresent != null || item.LogIfMissing != null))
            Issue(mod, null, $"{where}: \"LogIfPresent\" and \"LogIfMissing\" are written only for an \"OnlyIfCardExists\" item; ignored.");

        if (obj.TryGetValue("Stats", out var rawStats) && rawStats != null)
        {
            if (rawStats is not Dictionary<string, object> statObj)
            {
                Issue(mod, null, $"{where}: \"Stats\" must be an object of stat name to number; the item spawns at its card defaults.");
            }
            else
            {
                var stats = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (var kvp in statObj)
                {
                    if (kvp.Key.StartsWith("_", StringComparison.Ordinal)) continue;
                    if (Array.IndexOf(StatNames, kvp.Key) < 0)
                    {
                        Issue(mod, null, $"{where}: stat '{kvp.Key}' is not one of {string.Join(", ", StatNames)}; dropped.");
                        continue;
                    }
                    if (kvp.Value is not double v || double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > float.MaxValue)
                    {
                        Issue(mod, null, $"{where}: stat '{kvp.Key}' must be a finite number; dropped.");
                        continue;
                    }
                    stats[kvp.Key] = (float)v;
                }
                if (stats.Count > 0) item.Stats = stats;
            }
        }

        return item;
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
        => Issues.Add(new KitContentsIssue { Mod = mod, UnresolvedUid = unresolvedUid, Message = message });
}
