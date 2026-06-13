using CSFFModFramework.Api;
using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>
/// Gap Audit Phase 5: attaches mod QuestLogs to PlayerCharacter quest lists.
///
/// <para>A quest is authored as <c>Objective/*.json</c> + <c>QuestLog/*.json</c>
/// (loaded and warp-resolved since framework 2.1.0; a QuestLog references its
/// Objective via <c>QuestObjectiveWarpData</c> and its parent via <c>ParentQuest</c>).
/// What vanilla does NOT do for mods is list the QuestLog on any character — the
/// game copies <c>PlayerCharacter.Quests</c> into the run at new-game start, so an
/// unlisted quest never appears. This injector closes that gap, driven by a
/// <c>Quests.json</c> manifest at the mod root:</para>
///
/// <code>
/// {
///   "Quests": [
///     { "QuestLog": "my_mod_quest_main", "Characters": [] },          // all characters
///     { "QuestLog": "my_mod_quest_alt",  "Characters": ["Huntsman"] } // name or UniqueID
///   ]
/// }
/// </code>
///
/// <para>Character matching: PlayerCharacter UniqueID, asset name, or
/// CharacterName.DefaultText (all case-insensitive). Empty/omitted list = every
/// non-editor character. Appends are idempotent (by-reference dedup in
/// <see cref="Collections.Append"/>) and run once per session — the same SO graph
/// persists for the session and new runs copy from it, so per-run re-injection is
/// not needed (the M8 single-load guard is safe here).</para>
///
/// <para>Save-compat note (Part 4 risk): quests serialize into saves. Removing a
/// quest mod from an existing save requires the documented add → save → remove →
/// load test before shipping content built on this.</para>
/// </summary>
internal static class QuestInjector
{
    public static void InjectAll(IEnumerable allData, List<ModManifest> mods)
    {
        // Collect all PlayerCharacters once (shared across mods and entries).
        var characters = CollectPlayerCharacters(allData);
        if (characters.Count == 0)
        {
            Log.Warn("[QuestInjector] no PlayerCharacter objects in AllData — quests not attached.");
            return;
        }

        int attached = 0, entries = 0;
        foreach (var mod in mods)
        {
            var manifestPath = Path.Combine(mod.DirectoryPath, "Quests.json");
            if (!File.Exists(manifestPath)) continue;

            List<QuestEntry> parsed;
            try { parsed = ParseManifest(File.ReadAllText(manifestPath)); }
            catch (Exception ex)
            {
                Log.Warn($"[QuestInjector] {mod.Name}: failed to parse Quests.json: {Log.ExceptionText(ex)}");
                continue;
            }

            foreach (var entry in parsed)
            {
                entries++;
                var questLog = GameRegistry.GetByUid(entry.QuestLogUid);
                if (questLog == null)
                {
                    Log.Warn($"[QuestInjector] {mod.Name}: QuestLog '{entry.QuestLogUid}' not found in registry — skipped.");
                    continue;
                }
                if (questLog.GetType().Name != "QuestLog")
                {
                    Log.Warn($"[QuestInjector] {mod.Name}: '{entry.QuestLogUid}' is a {questLog.GetType().Name}, not a QuestLog — skipped.");
                    continue;
                }

                var targets = ResolveCharacters(characters, entry.Characters, mod.Name, entry.QuestLogUid);
                foreach (var character in targets)
                {
                    if (Collections.Append(character, "Quests", questLog))
                    {
                        attached++;
                        Log.Debug($"[QuestInjector] {mod.Name}: '{entry.QuestLogUid}' → {CharacterLabel(character)}");
                    }
                }
            }
        }

        if (entries > 0)
            Log.Debug($"[QuestInjector] {entries} manifest entr(ies) processed, {attached} attachment(s).");
    }

    // ── Manifest ─────────────────────────────────────────────────────────────

    private sealed class QuestEntry
    {
        public string QuestLogUid;
        public readonly List<string> Characters = new();
    }

    private static List<QuestEntry> ParseManifest(string json)
    {
        var result = new List<QuestEntry>();
        if (MiniJson.Parse(json) is not Dictionary<string, object> root) return result;
        if (!root.TryGetValue("Quests", out var q) || q is not List<object> list) return result;

        foreach (var item in list)
        {
            if (item is not Dictionary<string, object> obj) continue;
            var entry = new QuestEntry();
            if (obj.TryGetValue("QuestLog", out var uid) && uid is string s && !string.IsNullOrEmpty(s))
                entry.QuestLogUid = s;
            if (obj.TryGetValue("Characters", out var chars) && chars is List<object> charList)
                foreach (var c in charList)
                    if (c is string cs && !string.IsNullOrEmpty(cs)) entry.Characters.Add(cs);
            if (entry.QuestLogUid != null) result.Add(entry);
        }
        return result;
    }

    // ── Character resolution ─────────────────────────────────────────────────

    internal static List<UniqueIDScriptable> CollectPlayerCharacters(IEnumerable allData)
    {
        var result = new List<UniqueIDScriptable>();
        foreach (var item in allData)
            if (item is UniqueIDScriptable so && so.GetType().Name == "PlayerCharacter")
                result.Add(so);
        return result;
    }

    private static List<UniqueIDScriptable> ResolveCharacters(
        List<UniqueIDScriptable> all, List<string> filters, string modName, string questUid)
    {
        if (filters.Count == 0)
        {
            // "All characters" excludes editor-only ones (DemoCharacter, EnvTestsGuy, ...).
            var playable = new List<UniqueIDScriptable>();
            foreach (var c in all)
                if (!Reflect.GetBool(c, "OnlyExistsInEditor"))
                    playable.Add(c);
            return playable;
        }

        var result = new List<UniqueIDScriptable>();
        foreach (var filter in filters)
        {
            var match = FindCharacter(all, filter);
            if (match != null) result.Add(match);
            else Log.Warn($"[QuestInjector] {modName}: character '{filter}' not found for quest '{questUid}'.");
        }
        return result;
    }

    internal static UniqueIDScriptable FindCharacter(List<UniqueIDScriptable> all, string uidOrName)
    {
        foreach (var c in all)
        {
            if (string.Equals(c.UniqueID, uidOrName, StringComparison.OrdinalIgnoreCase)) return c;
            if (string.Equals(c.name, uidOrName, StringComparison.OrdinalIgnoreCase)) return c;
            var displayName = Reflect.GetMember(c, "CharacterName") is { } nameObj
                ? Reflect.GetMember(nameObj, "DefaultText") as string : null;
            if (!string.IsNullOrEmpty(displayName)
                && string.Equals(displayName, uidOrName, StringComparison.OrdinalIgnoreCase)) return c;
        }
        return null;
    }

    internal static string CharacterLabel(UniqueIDScriptable character)
        => !string.IsNullOrEmpty(character.name) ? character.name : character.UniqueID;
}
