using CSFFModFramework.Api;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>
/// Gap Audit Phase 5: appends mod PlayerCharacters to the game's character-select
/// rosters, driven by a <c>Characters.json</c> manifest at the mod root.
///
/// <para>The character itself is authored as <c>PlayerCharacter/*.json</c> (loaded and
/// warp-resolved since 2.1.0 — perks, starting items, environment, quests, and an
/// optional <c>EasyPackageWarpData</c> GameModifierPackage all resolve as WarpData).
/// What vanilla never does for mods is list the character on the Gamemode roster the
/// character-select screen reads (<c>Gamemode.FatesCharacters</c> /
/// <c>WaysCharacters</c>). This injector appends it:</para>
///
/// <code>
/// {
///   "Characters": [
///     { "Character": "my_mod_shepherd", "Roster": "Fates" }   // Fates | Ways | Both
///   ]
/// }
/// </code>
///
/// <para>Character matching: UniqueID, asset name, or CharacterName.DefaultText
/// (case-insensitive). Appends are idempotent and target every Gamemode SO present
/// (vanilla 0.64f ships exactly one, "CharacterList").</para>
///
/// <para>Timing note: injection runs during LoadMainGameData, before the main menu
/// builds the character-select UI, so a load-time append is expected to be visible.
/// If a future game version snapshots the roster earlier, the fallback is a UI-time
/// postfix on the character screen's Show (the BlueprintModelsScreen.Show pattern).</para>
///
/// <para>GameModifierPackage needs no injector — it is referenced from the character's
/// own <c>EasyPackageWarpData</c> and resolves via WarpResolver.</para>
/// </summary>
internal static class CharacterRosterInjector
{
    public static void InjectAll(IEnumerable allData, List<ModManifest> mods)
    {
        // Gamemode SOs (vanilla: exactly one). Collected once.
        var gamemodes = new List<UniqueIDScriptable>();
        foreach (var item in allData)
            if (item is UniqueIDScriptable so && so.GetType().Name == "Gamemode")
                gamemodes.Add(so);
        if (gamemodes.Count == 0)
        {
            Log.Warn("[CharacterRosterInjector] no Gamemode object in AllData — characters not added to rosters.");
            return;
        }

        var characters = QuestInjector.CollectPlayerCharacters(allData);

        int added = 0, entries = 0;
        foreach (var mod in mods)
        {
            var manifestPath = Path.Combine(mod.DirectoryPath, "Characters.json");
            if (!File.Exists(manifestPath)) continue;

            List<RosterEntry> parsed;
            try { parsed = ParseManifest(File.ReadAllText(manifestPath)); }
            catch (Exception ex)
            {
                Log.Warn($"[CharacterRosterInjector] {mod.Name}: failed to parse Characters.json: {Log.ExceptionText(ex)}");
                continue;
            }

            foreach (var entry in parsed)
            {
                entries++;
                var character = QuestInjector.FindCharacter(characters, entry.Character);
                if (character == null)
                {
                    Log.Warn($"[CharacterRosterInjector] {mod.Name}: PlayerCharacter '{entry.Character}' not found — skipped.");
                    continue;
                }

                foreach (var gamemode in gamemodes)
                {
                    if (entry.Fates && Collections.Append(gamemode, "FatesCharacters", character))
                    {
                        added++;
                        Log.Debug($"[CharacterRosterInjector] {mod.Name}: {QuestInjector.CharacterLabel(character)} → {gamemode.name}.FatesCharacters");
                    }
                    if (entry.Ways && Collections.Append(gamemode, "WaysCharacters", character))
                    {
                        added++;
                        Log.Debug($"[CharacterRosterInjector] {mod.Name}: {QuestInjector.CharacterLabel(character)} → {gamemode.name}.WaysCharacters");
                    }
                }
            }
        }

        if (entries > 0)
            Log.Debug($"[CharacterRosterInjector] {entries} manifest entr(ies) processed, {added} roster addition(s).");
    }

    private sealed class RosterEntry
    {
        public string Character;
        public bool Fates;
        public bool Ways;
    }

    private static List<RosterEntry> ParseManifest(string json)
    {
        var result = new List<RosterEntry>();
        if (MiniJson.Parse(json) is not Dictionary<string, object> root) return result;
        if (!root.TryGetValue("Characters", out var c) || c is not List<object> list) return result;

        foreach (var item in list)
        {
            if (item is not Dictionary<string, object> obj) continue;
            var entry = new RosterEntry();
            if (obj.TryGetValue("Character", out var ch) && ch is string s && !string.IsNullOrEmpty(s))
                entry.Character = s;

            var roster = obj.TryGetValue("Roster", out var r) && r is string rs ? rs : "Fates";
            entry.Fates = roster.Equals("Fates", StringComparison.OrdinalIgnoreCase)
                       || roster.Equals("Both", StringComparison.OrdinalIgnoreCase);
            entry.Ways = roster.Equals("Ways", StringComparison.OrdinalIgnoreCase)
                      || roster.Equals("Both", StringComparison.OrdinalIgnoreCase);

            if (entry.Character != null && (entry.Fates || entry.Ways)) result.Add(entry);
            else if (entry.Character != null)
                Log.Warn($"[CharacterRosterInjector] unknown Roster '{roster}' for '{entry.Character}' — use Fates, Ways, or Both.");
        }
        return result;
    }
}
