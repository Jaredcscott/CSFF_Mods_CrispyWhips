using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Data;

/// <summary>
/// Applies GameSourceModify/ JSON patches to vanilla game objects.
/// Each JSON file's name (without extension) should be a UniqueID matching a game object.
///
/// Supports standard field overwrite (via JsonUtility.FromJsonOverwrite) plus:
/// - MatchTagWarpData: apply to all cards with a given tag
/// - MatchTypeWarpData: apply to all cards of a given CardType int
/// - _appendArrays: append items to existing list fields without overwriting them
///   Format: "_appendArrays": { "FieldName": ["UniqueID1", "UniqueID2", ...] }
///   Each UniqueID is resolved from AllData and appended to the named field (must be IList).
/// </summary>
internal static class GameSourceModifier
{
    public static void ApplyAll(List<ModManifest> mods, IEnumerable allData)
    {
        // No local objectMap needed — GameRegistry.AllUniqueObjects already contains every
        // vanilla + mod UniqueIDScriptable by this point in the load sequence. Using it directly
        // eliminates one full allData sweep (~4000 items) and a dictionary allocation per load.
        int totalApplied = 0;

        foreach (var mod in mods)
        {
            var gsmDir = Path.Combine(mod.DirectoryPath, "GameSourceModify");
            // Fallback: some third-party mods (e.g. NPCTracker) nest GameSourceModify under Data/
            if (!Directory.Exists(gsmDir))
                gsmDir = Path.Combine(mod.DirectoryPath, "Data", "GameSourceModify");
            if (!Directory.Exists(gsmDir)) continue;

            foreach (var file in Directory.GetFiles(gsmDir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var fileName = Path.GetFileNameWithoutExtension(file);

                    // Check for bulk modification via MatchTagWarpData or MatchTypeWarpData
                    var matchTag = PathUtil.QuickExtractString(json, "MatchTagWarpData");
                    var matchTypeStr = PathUtil.QuickExtractString(json, "MatchTypeWarpData");

                    if (!string.IsNullOrEmpty(matchTag))
                    {
                        // Apply to all cards with this tag
                        var targets = DataMap.GetCardsByTag(matchTag);
                        foreach (var target in targets)
                        {
                            ApplyPatch(target, json, mod.Name, file);
                            totalApplied++;
                        }
                    }
                    else if (!string.IsNullOrEmpty(matchTypeStr) && int.TryParse(matchTypeStr, out int matchType))
                    {
                        // Apply to all cards of this type
                        var targets = DataMap.GetCardsByType(matchType);
                        foreach (var target in targets)
                        {
                            ApplyPatch(target, json, mod.Name, file);
                            totalApplied++;
                        }
                    }
                    else
                    {
                        // Standard: file name or UniqueID field is the target.
                        // Try the UID registry first (CardData, CharacterPerk, GameStat, ...),
                        // then fall back to the non-UID name registry (WeaponMove, DamageType,
                        // CardTag, ActionTag, ...) — same two-tier lookup WarpResolver.Lookup
                        // already uses for non-UID types (WarpResolver.cs Lookup()). This is what
                        // lets a mod patch an existing vanilla WeaponMove/DamageType in place
                        // instead of only being able to create new ones.
                        var targetUid = PathUtil.QuickExtractString(json, "UniqueID") ?? fileName;
                        UnityEngine.Object target = GameRegistry.GetByUid(targetUid);
                        if (target == null && Database.AllScriptableObjectDict.TryGetValue(targetUid, out var byName))
                            target = byName;

                        if (target != null)
                        {
                            ApplyPatch(target, json, mod.Name, file);
                            totalApplied++;
                        }
                        else
                        {
                            Log.Warn($"GameSourceModify: no object found for '{targetUid}' ({file})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"GameSourceModify: error processing {file}: {Log.ExceptionText(ex)}");
                }
            }
        }

        Log.Debug($"GameSourceModify: applied {totalApplied} patches across {mods.Count} mod(s)");
    }

    private static void ApplyPatch(UnityEngine.Object target, string json, string modName, string filePath)
    {
        // Parse the tree before FromJsonOverwrite so we can warn about accidental
        // collection zeroing while the target still holds its pre-patch values.
        Dictionary<string, object> root = null;
        try { root = MiniJson.Parse(json) as Dictionary<string, object>; }
        catch (Exception ex)
        {
            Log.Warn($"GameSourceModify: failed to parse {Path.GetFileName(filePath)}: {Log.ExceptionText(ex)}");
        }

        // FromJsonOverwrite replaces any collection present in the JSON — an empty
        // "Foo": [] erases the target's entire vanilla Foo array. That is occasionally
        // intended, but far more often the author meant _appendArrays. Warn so the
        // erasure is visible instead of silently breaking vanilla behavior.
        if (root != null)
            WarnOnEmptyArrayZeroing(target, root, modName, filePath);

        JsonUtility.FromJsonOverwrite(json, target);

        // Re-resolve WarpData on the patched object and apply _appendArrays
        try
        {
            if (root != null)
            {
                WarpResolver.Walk(target, root);
                ApplyAppendArrays(target, root, modName);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"GameSourceModify: WarpData re-resolve failed for {target.name}: {Log.ExceptionText(ex)}");
        }

        // Mark patched object as dirty so NullReferenceCompactor picks it up.
        if (target is UniqueIDScriptable uidTarget)
            Loading.FrameworkDirtyTracker.MarkDirty(uidTarget);

        Log.Debug($"GameSourceModify: [{modName}] patched {target.name} via {Path.GetFileName(filePath)}");
    }

    /// <summary>
    /// Warns when a patch JSON contains an empty array for a field that is currently
    /// non-empty on the target — FromJsonOverwrite will erase the whole collection.
    /// Detection only; the patch is still applied (zeroing may be intentional).
    /// </summary>
    private static void WarnOnEmptyArrayZeroing(UnityEngine.Object target, Dictionary<string, object> root,
        string modName, string filePath)
    {
        try
        {
            var type = target.GetType();
            foreach (var kvp in root)
            {
                if (kvp.Value is not List<object> { Count: 0 }) continue;

                var field = type.GetField(kvp.Key,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var current = field?.GetValue(target);
                int count = current switch
                {
                    Array arr => arr.Length,
                    ICollection col => col.Count,
                    _ => 0,
                };
                if (count > 0)
                    Log.Warn($"GameSourceModify: [{modName}] {Path.GetFileName(filePath)} sets '{kvp.Key}': [] "
                           + $"on {target.name}, erasing {count} existing entr{(count == 1 ? "y" : "ies")}. "
                           + "If you meant to add entries, use _appendArrays instead.");
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"GameSourceModify: zeroing check failed for {target.name}: {Log.ExceptionText(ex)}");
        }
    }

    /// <summary>
    /// Handles the "_appendArrays" section of a GameSourceModify JSON patch.
    /// Appends items to existing IList fields on the target object without overwriting them.
    /// Each entry maps a field name to an array of UniqueIDs to resolve and append.
    /// Uses GameRegistry.GetByUid for item lookup — no separate objectMap needed.
    /// </summary>
    private static void ApplyAppendArrays(UnityEngine.Object target, Dictionary<string, object> root,
        string modName)
    {
        if (!root.TryGetValue("_appendArrays", out var appendObj)) return;
        if (appendObj is not Dictionary<string, object> appendDict) return;

        var targetType = target.GetType();

        foreach (var kvp in appendDict)
        {
            var fieldName = kvp.Key;
            if (kvp.Value is not List<object> idsToAppend || idsToAppend.Count == 0) continue;

            var field = targetType.GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                Log.Warn($"GameSourceModify._appendArrays: field '{fieldName}' not found on {targetType.Name}");
                continue;
            }

            if (field.GetValue(target) is not IList list)
            {
                Log.Warn($"GameSourceModify._appendArrays: '{fieldName}' is null or not IList on {target.name}");
                continue;
            }

            int appended = 0;
            foreach (var idObj in idsToAppend)
            {
                var id = idObj?.ToString();
                if (string.IsNullOrEmpty(id)) continue;

                var resolved = GameRegistry.GetByUid(id);
                if (resolved == null)
                {
                    Log.Warn($"GameSourceModify._appendArrays: '{id}' not found in registry (target: {target.name}.{fieldName})");
                    continue;
                }

                // Avoid duplicates
                bool exists = false;
                foreach (var existing in list)
                    if (ReferenceEquals(existing, resolved)) { exists = true; break; }

                if (!exists)
                {
                    try
                    {
                        list.Add(resolved);
                        appended++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"GameSourceModify._appendArrays: failed to append '{id}' to {target.name}.{fieldName}: {Log.ExceptionText(ex)}");
                    }
                }
            }

            if (appended > 0)
                Log.Debug($"GameSourceModify: [{modName}] appended {appended} items to {target.name}.{fieldName}");
        }
    }
}
