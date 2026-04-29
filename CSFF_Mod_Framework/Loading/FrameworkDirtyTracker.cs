using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Tracks the set of UniqueIDScriptable objects that any framework service has
/// modified during a load pass. <see cref="NullReferenceCompactor"/> reads this
/// set and walks ONLY these objects instead of every loaded ScriptableObject —
/// the previous full sweep visited ~188K nested objects to find ~700 nulls in
/// ~220 collections.
///
/// Services must call <see cref="MarkDirty"/> for every object they mutate.
/// JSON-loaded mod content is bulk-marked from <see cref="JsonDataLoader.JsonByUniqueId"/>
/// at the end of the JSON load step. Services that touch vanilla objects
/// (GameSourceModifier, PerkInjector, SmeltingRecipeInjector) must call
/// <see cref="MarkDirty"/> per modification target.
/// </summary>
internal static class FrameworkDirtyTracker
{
    private static readonly HashSet<UniqueIDScriptable> _dirty = new();

    public static IReadOnlyCollection<UniqueIDScriptable> DirtyObjects => _dirty;

    public static void Reset() => _dirty.Clear();

    /// <summary>
    /// Drain the current dirty set into a snapshot and clear the tracker. Used by
    /// <see cref="NullReferenceCompactor"/> so a subsequent compaction pass picks
    /// up only objects modified since the last drain.
    /// </summary>
    public static List<UniqueIDScriptable> Drain()
    {
        var snapshot = new List<UniqueIDScriptable>(_dirty);
        _dirty.Clear();
        return snapshot;
    }

    public static void MarkDirty(UniqueIDScriptable obj)
    {
        if (obj != null) _dirty.Add(obj);
    }

    /// <summary>
    /// Bulk-mark every loaded mod object as dirty. Called once after JsonDataLoader
    /// finishes — every mod-loaded ScriptableObject is implicitly modified by
    /// JsonUtility.FromJsonOverwrite + WarpResolver.
    /// </summary>
    public static void MarkAllModObjects(IEnumerable allData)
    {
        var modUids = JsonDataLoader.AllModUniqueIds;
        if (modUids.Count == 0) return;

        int added = 0;
        foreach (var item in allData)
        {
            if (item is UniqueIDScriptable uid
                && !string.IsNullOrEmpty(uid.UniqueID)
                && modUids.Contains(uid.UniqueID))
            {
                if (_dirty.Add(uid)) added++;
            }
        }
        Log.Debug($"FrameworkDirtyTracker: marked {added} mod-loaded objects as dirty");
    }
}
