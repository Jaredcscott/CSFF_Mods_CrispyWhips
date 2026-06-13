using CSFFModFramework.Data;

namespace CSFFModFramework.Api;

/// <summary>
/// Register code-built ScriptableObject content into the game's runtime so the
/// game's own resolvers (and the rest of the framework) can find it.
///
/// <para>
/// JSON-driven mods don't need this — <see cref="Loading.JsonDataLoader"/>
/// already registers everything in <c>CardData/</c>, <c>CharacterPerk/</c>,
/// <c>ScriptableObject/</c>, etc. Use <see cref="ContentRegistry"/> only when
/// you build content programmatically and need the game to treat it as native.
/// </para>
///
/// <para>
/// Coexistence: writes are additive. If a UID is already registered (by us, by
/// vanilla, or by another plugin), <see cref="Register(UniqueIDScriptable)"/>
/// returns <c>false</c> and leaves the existing entry intact. Recommended call
/// site: subscribe to <see cref="FrameworkEvents.GameDataReady"/> and register
/// from there, so vanilla content has finished loading and your registrations
/// are visible to the rest of the load chain.
/// </para>
/// </summary>
/// <summary>
/// Outcome of a <see cref="ContentRegistry.RegisterWithResult"/> call — distinguishes
/// normal duplicate-skips from actual failures (FRAMEWORK_EVALUATION.md D2).
/// </summary>
public enum RegisterResult
{
    /// <summary>The UID was new; the object is now registered.</summary>
    Registered,
    /// <summary>The UID was already registered (by us, vanilla, or another plugin) — existing entry left in place. Normal during re-runs.</summary>
    DuplicateSkipped,
    /// <summary>Registration failed: null object, missing UniqueID, or the game's registry is unreachable. Details logged at Error.</summary>
    Failed,
}

public static class ContentRegistry
{
    /// <summary>
    /// Register a <see cref="UniqueIDScriptable"/> (CardData, CharacterPerk,
    /// GameStat, etc.) under its <c>UniqueID</c> in the game's static UID dict
    /// AND append it to <c>GameLoad.Instance.DataBase.AllData</c>.
    /// Returns <c>true</c> if the UID was new and registration succeeded.
    /// Use <see cref="RegisterWithResult"/> to distinguish duplicate-skips from failures.
    /// </summary>
    public static bool Register(UniqueIDScriptable so)
        => RegisterWithResult(so) == RegisterResult.Registered;

    /// <summary>
    /// As <see cref="Register(UniqueIDScriptable)"/>, but reports the outcome as a
    /// <see cref="RegisterResult"/> so callers can tell a normal duplicate-skip from an error.
    /// </summary>
    public static RegisterResult RegisterWithResult(UniqueIDScriptable so)
    {
        if (so == null)
        {
            FrameworkLog.Error("ContentRegistry.Register called with null — nothing registered");
            return RegisterResult.Failed;
        }
        if (string.IsNullOrEmpty(so.UniqueID))
        {
            FrameworkLog.Error($"ContentRegistry.Register: {so.name} has no UniqueID — nothing registered");
            return RegisterResult.Failed;
        }

        var dict = GameRegistry.AllUniqueObjects;
        if (dict == null)
        {
            FrameworkLog.Error($"ContentRegistry.Register: game UID registry unreachable — '{so.UniqueID}' not registered");
            return RegisterResult.Failed;
        }
        bool alreadyPresent = dict.Contains(so.UniqueID);

        var newlyRegistered = GameRegistry.TryRegister(so);
        // Append to AllData regardless of who put the UID in the dict — Add is idempotent.
        GameRegistry.TryAddToAllData(so);

        if (newlyRegistered) return RegisterResult.Registered;
        // TryRegister logs the duplicate case at Debug; a false WITHOUT a pre-existing
        // entry means the dict.Add itself failed (logged at Warn by GameRegistry).
        return alreadyPresent ? RegisterResult.DuplicateSkipped : RegisterResult.Failed;
    }

    /// <summary>
    /// Register a non-UID <see cref="ScriptableObject"/> (CardTag, ActionTag,
    /// CardTabGroup, etc.) by name into the framework's per-type and flat
    /// indexes. Returns <c>true</c> if the name was new in the framework's
    /// indexes (vanilla SOs of the same type are read by name anyway, so a
    /// duplicate name will not shadow vanilla — it is dropped).
    /// </summary>
    public static bool Register(ScriptableObject so)
    {
        if (so == null || string.IsNullOrEmpty(so.name)) return false;
        if (so is UniqueIDScriptable uid) return Register(uid);

        var type = so.GetType();
        bool wasKnown = Database.GetTypedSO(type, so.name) != null
                        || Database.AllScriptableObjectDict.ContainsKey(so.name);
        Database.RegisterTypedSO(type, so.name, so);
        return !wasKnown;
    }

    /// <summary>
    /// True if <paramref name="uniqueIdOrName"/> is currently in either the
    /// game's UID dict (UniqueIDScriptable types) or the framework's per-type
    /// ScriptableObject indexes (non-UID types). Does not differentiate who
    /// registered the entry.
    /// </summary>
    public static bool IsRegistered(string uniqueIdOrName)
    {
        if (string.IsNullOrEmpty(uniqueIdOrName)) return false;

        var dict = GameRegistry.AllUniqueObjects;
        if (dict != null && dict.Contains(uniqueIdOrName)) return true;

        return Database.AllScriptableObjectDict.ContainsKey(uniqueIdOrName);
    }
}
