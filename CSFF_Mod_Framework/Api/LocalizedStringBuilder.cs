using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Programmatic LocalizedString construction (Centralization Tier 1). Replaces the
/// inline Activator + reflection builders in H&amp;F (truffle action name), ACT, and
/// Sirus (pregnancy stat name).
///
/// <para>Field-vs-property safe across game versions: all writes go through
/// <see cref="Reflect.SetMember"/>, which falls back from property setter to field to
/// auto-property backing field. Always build a FRESH LocalizedString for injected
/// actions — never mutate the template's (CLAUDE.md §Runtime DismantleAction Injection).</para>
/// </summary>
public static class LocalizedStringBuilder
{
    private static Type _localizedStringType;
    private static bool _typeResolved;

    /// <summary>
    /// Creates a new LocalizedString instance (game type resolved by name) with the
    /// given key and default text. Returns null if the game type cannot be resolved
    /// or instantiated. The CSV row for <paramref name="localizationKey"/> remains
    /// authoritative at runtime; <paramref name="defaultText"/> is the fallback.
    /// </summary>
    public static object Create(string localizationKey, string defaultText, string parentObjectId = "")
    {
        if (!_typeResolved)
        {
            _typeResolved = true;
            _localizedStringType = Reflect.TryGetType("LocalizedString");
        }
        if (_localizedStringType == null) return null;
        try
        {
            var ls = Activator.CreateInstance(_localizedStringType);
            return Populate(ls, localizationKey, defaultText, parentObjectId) ? ls : null;
        }
        catch (Exception ex) { Log.Debug($"LocalizedStringBuilder.Create: failed for key '{localizationKey}': {ex.GetType().Name} {ex.Message}"); return null; }
    }

    /// <summary>
    /// Creates a new LocalizedString of the SAME runtime type as <paramref name="template"/>
    /// (use when cloning an action and replacing its ActionName — guarantees type
    /// compatibility with the field being assigned). Returns null on failure.
    /// </summary>
    public static object CreateLike(object template, string localizationKey, string defaultText, string parentObjectId = "")
    {
        if (template == null) return Create(localizationKey, defaultText, parentObjectId);
        try
        {
            var ls = Activator.CreateInstance(template.GetType());
            return Populate(ls, localizationKey, defaultText, parentObjectId) ? ls : null;
        }
        catch (Exception ex) { Log.Debug($"LocalizedStringBuilder.CreateLike: failed for key '{localizationKey}' (template type {template.GetType().Name}): {ex.GetType().Name} {ex.Message}"); return null; }
    }

    /// <summary>
    /// Sets LocalizationKey, DefaultText, and ParentObjectID on an existing
    /// LocalizedString instance. Returns true if the key or text was written.
    /// </summary>
    public static bool Populate(object localizedString, string localizationKey, string defaultText, string parentObjectId = "")
    {
        if (localizedString == null) return false;
        bool keyOk = Reflect.SetMember(localizedString, "LocalizationKey", localizationKey ?? "");
        bool textOk = Reflect.SetMember(localizedString, "DefaultText", defaultText ?? "");
        Reflect.SetMember(localizedString, "ParentObjectID", parentObjectId ?? "");
        return keyOk || textOk;
    }

    /// <summary>Reads DefaultText from a LocalizedString (field or property). Null on failure.</summary>
    public static string GetDefaultText(object localizedString)
        => Reflect.GetMember(localizedString, "DefaultText") as string;

    /// <summary>Reads LocalizationKey from a LocalizedString (field or property). Null on failure.</summary>
    public static string GetLocalizationKey(object localizedString)
        => Reflect.GetMember(localizedString, "LocalizationKey") as string;
}
