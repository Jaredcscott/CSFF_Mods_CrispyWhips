using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Self-healing for renamed UniqueIDs. A mod that renames or merges a card ships a
/// <c>UidAliases.json</c> next to <c>ModInfo.json</c>:
/// <code>{ "old_uid": "new_uid", "older_uid": "new_uid" }</code>
/// Two things then keep working with no player action:
/// <list type="number">
/// <item>Any <c>*WarpData</c> reference to the old UID (this mod's, or another mod's still naming it)
/// resolves to the new card: <see cref="GameRegistry.GetByUid(string)"/> consults <see cref="Resolve"/>
/// after a miss.</item>
/// <item>Saved games: the game finds a saved card, blueprint or perk by an exact lookup in
/// <c>UniqueIDScriptable.AllUniqueObjects</c>, so at the end of load each old UID is added there as a
/// second key for the new object (<see cref="RegisterInGameRegistry"/>). Without it a card saved under
/// the old UID is dropped on load.</item>
/// </list>
/// Only UIDs that no longer exist are aliased: an old UID that is still a real card is never shadowed.
/// Chains (a to b, b to c) are followed. Never throws.
/// </summary>
internal static class UidAliasService
{
    internal const string FileName = "UidAliases.json";
    private const int MaxChain = 8;

    private static readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> _aliasOwner = new(StringComparer.Ordinal);

    internal static int Count => _aliases.Count;

    internal static void LoadAll(IReadOnlyList<ModManifest> mods)
    {
        _aliases.Clear();
        _aliasOwner.Clear();
        foreach (var mod in mods)
        {
            try
            {
                var path = Path.Combine(mod.DirectoryPath, FileName);
                if (!File.Exists(path)) continue;
                if (MiniJson.Parse(File.ReadAllText(path)) is not Dictionary<string, object> map)
                {
                    Log.Warn($"UidAliasService: {mod.Name}: {FileName} is not a JSON object of old-uid to new-uid, ignored");
                    continue;
                }
                int added = 0;
                foreach (var kv in map)
                {
                    var oldUid = kv.Key?.Trim();
                    var newUid = (kv.Value as string)?.Trim();
                    if (string.IsNullOrEmpty(oldUid) || string.IsNullOrEmpty(newUid) || oldUid == newUid) continue;
                    if (_aliases.TryGetValue(oldUid, out var prior) && prior != newUid)
                        Log.Warn($"UidAliasService: '{oldUid}' is aliased by {_aliasOwner[oldUid]} and {mod.Name} to different targets; keeping the first");
                    else if (!_aliases.ContainsKey(oldUid)) { _aliases[oldUid] = newUid; _aliasOwner[oldUid] = mod.Name; added++; }
                }
                Log.Debug($"UidAliasService: {mod.Name}: {added} alias(es)");
            }
            catch (Exception ex)
            {
                Log.Warn($"UidAliasService: {mod.Name}: {FileName} unreadable: {Log.ExceptionText(ex)}");
            }
        }
        if (_aliases.Count > 0) Log.Info($"UidAliasService: {_aliases.Count} renamed-UID alias(es) loaded");
    }

    /// <summary>The current UID for <paramref name="uid"/> when it is a known old name, else null.</summary>
    internal static string Resolve(string uid)
    {
        if (_aliases.Count == 0 || string.IsNullOrEmpty(uid)) return null;
        string cur = uid;
        for (int i = 0; i < MaxChain && _aliases.TryGetValue(cur, out var next); i++)
        {
            if (next == uid) return null; // cycle back to the start
            cur = next;
        }
        return cur == uid ? null : cur;
    }

    /// <summary>Add each old UID to the game's UID table as another key for the new object, so saves resolve. Call last.</summary>
    internal static void RegisterInGameRegistry()
    {
        if (_aliases.Count == 0) return;
        var dict = GameRegistry.AllUniqueObjects;
        if (dict == null)
        {
            Log.Warn("UidAliasService: game UID table unreachable, old saves will not resolve renamed UIDs");
            return;
        }
        int registered = 0, dangling = 0;
        foreach (var oldUid in _aliases.Keys.ToList())
        {
            if (dict.Contains(oldUid)) continue; // still a live card: never shadow it
            var target = Resolve(oldUid);
            var so = target != null && dict.Contains(target) ? dict[target] : null;
            if (so == null)
            {
                dangling++;
                Log.Warn($"UidAliasService: alias '{oldUid}' -> '{target}' ({_aliasOwner[oldUid]}) points at a UID that does not exist; saves holding '{oldUid}' will still lose it");
                continue;
            }
            dict[oldUid] = so;
            registered++;
        }
        Log.Info($"UidAliasService: {registered} old UID(s) now resolve to their renamed card" + (dangling > 0 ? $", {dangling} dangling" : ""));
    }
}
