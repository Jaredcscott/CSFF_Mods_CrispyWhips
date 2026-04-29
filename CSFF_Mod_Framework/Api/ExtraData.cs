using CSFFModFramework.Data;

namespace CSFFModFramework.Api;

/// <summary>
/// Read access to sidecar extras shipped alongside content JSON files.
///
/// <para>
/// Authoring: place a file named <c>&lt;ContentFile&gt;.extras.json</c> next to
/// your content JSON. The framework reads it at load time and indexes it by
/// the content file's <c>UniqueID</c>. The sidecar's structure is free-form —
/// any valid JSON. Mods are responsible for parsing their own extras.
/// </para>
///
/// <para>
/// Example: <c>CardData/Item/Foo.json</c> with <c>UniqueID: "yourmod_foo"</c>
/// alongside <c>CardData/Item/Foo.extras.json</c> containing
/// <c>{ "myCustomBalance": 42 }</c> — query later with
/// <c>ExtraData.Get("yourmod_foo")</c>.
/// </para>
///
/// Use after <see cref="FrameworkEvents.Loaded"/>.
/// </summary>
public static class ExtraData
{
    /// <summary>
    /// Returns the raw JSON text of the sidecar associated with <paramref name="uniqueId"/>,
    /// or <c>null</c> if no sidecar exists.
    /// </summary>
    public static string Get(string uniqueId) => ExtraDataStore.Get(uniqueId);

    /// <summary>
    /// Convenience: deserializes the sidecar JSON into <typeparamref name="T"/> using
    /// Unity's <c>JsonUtility</c>. Returns <c>default</c> if no sidecar exists or if
    /// the JSON does not match <typeparamref name="T"/>.
    /// </summary>
    public static T Get<T>(string uniqueId)
    {
        var raw = ExtraDataStore.Get(uniqueId);
        if (string.IsNullOrEmpty(raw)) return default;
        try { return JsonUtility.FromJson<T>(raw); }
        catch { return default; }
    }

    /// <summary>True if a sidecar exists for the given UniqueID.</summary>
    public static bool Has(string uniqueId) => ExtraDataStore.Has(uniqueId);
}
