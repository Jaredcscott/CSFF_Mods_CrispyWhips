namespace CSFFModFramework.Data;

/// <summary>
/// Storage for sidecar extras (raw JSON text from <c>Foo.extras.json</c> files
/// adjacent to content JSON). Keyed by the sibling content file's UniqueID.
/// Populated by <see cref="Loading.JsonDataLoader"/> at load time; queried
/// through <see cref="Api.ExtraData"/>.
/// </summary>
internal static class ExtraDataStore
{
    private static readonly Dictionary<string, string> _byUniqueId
        = new(StringComparer.OrdinalIgnoreCase);

    internal static void Store(string uniqueId, string rawJson)
    {
        if (string.IsNullOrEmpty(uniqueId) || rawJson == null) return;
        _byUniqueId[uniqueId] = rawJson;
    }

    internal static string Get(string uniqueId)
    {
        if (string.IsNullOrEmpty(uniqueId)) return null;
        return _byUniqueId.TryGetValue(uniqueId, out var json) ? json : null;
    }

    internal static bool Has(string uniqueId)
        => !string.IsNullOrEmpty(uniqueId) && _byUniqueId.ContainsKey(uniqueId);

    internal static int Count => _byUniqueId.Count;

    internal static void Clear() => _byUniqueId.Clear();
}
