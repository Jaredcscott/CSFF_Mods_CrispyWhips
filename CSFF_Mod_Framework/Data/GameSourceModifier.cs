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
        // Build lookup of all UniqueIDScriptable objects by UniqueID
        var objectMap = new Dictionary<string, UniqueIDScriptable>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in allData)
        {
            if (item is UniqueIDScriptable uid && !string.IsNullOrEmpty(uid.UniqueID))
                objectMap[uid.UniqueID] = uid;
        }

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
                            ApplyPatch(target, json, mod.Name, file, objectMap);
                            totalApplied++;
                        }
                    }
                    else if (!string.IsNullOrEmpty(matchTypeStr) && int.TryParse(matchTypeStr, out int matchType))
                    {
                        // Apply to all cards of this type
                        var targets = DataMap.GetCardsByType(matchType);
                        foreach (var target in targets)
                        {
                            ApplyPatch(target, json, mod.Name, file, objectMap);
                            totalApplied++;
                        }
                    }
                    else
                    {
                        // Standard: file name or UniqueID field is the target
                        var targetUid = PathUtil.QuickExtractString(json, "UniqueID") ?? fileName;

                        if (objectMap.TryGetValue(targetUid, out var target))
                        {
                            ApplyPatch(target, json, mod.Name, file, objectMap);
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
                    Log.Error($"GameSourceModify: error processing {file}: {ex.Message}");
                }
            }
        }

        Log.Info($"GameSourceModify: applied {totalApplied} patches across {mods.Count} mod(s)");
    }

    private static void ApplyPatch(UnityEngine.Object target, string json, string modName, string filePath,
        Dictionary<string, UniqueIDScriptable> objectMap)
    {
        JsonUtility.FromJsonOverwrite(json, target);

        // Re-resolve WarpData on the patched object and apply _appendArrays
        try
        {
            var tree = MiniJson.Parse(json);
            if (tree is Dictionary<string, object> root)
            {
                WarpResolver.Walk(target, root);
                ApplyAppendArrays(target, root, objectMap, modName);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"GameSourceModify: WarpData re-resolve failed for {target.name}: {ex.Message}");
        }

        // Mark patched object as dirty so NullReferenceCompactor picks it up.
        if (target is UniqueIDScriptable uidTarget)
            Loading.FrameworkDirtyTracker.MarkDirty(uidTarget);

        Log.Debug($"GameSourceModify: [{modName}] patched {target.name} via {Path.GetFileName(filePath)}");
    }

    /// <summary>
    /// Handles the "_appendArrays" section of a GameSourceModify JSON patch.
    /// Appends items to existing IList fields on the target object without overwriting them.
    /// Each entry maps a field name to an array of UniqueIDs to resolve and append.
    /// </summary>
    private static void ApplyAppendArrays(UnityEngine.Object target, Dictionary<string, object> root,
        Dictionary<string, UniqueIDScriptable> objectMap, string modName)
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

                if (!objectMap.TryGetValue(id, out var resolved))
                {
                    Log.Warn($"GameSourceModify._appendArrays: '{id}' not found in AllData (target: {target.name}.{fieldName})");
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
                        Log.Warn($"GameSourceModify._appendArrays: failed to append '{id}' to {target.name}.{fieldName}: {ex.Message}");
                    }
                }
            }

            if (appended > 0)
                Log.Debug($"GameSourceModify: [{modName}] appended {appended} items to {target.name}.{fieldName}");
        }
    }
}
