namespace CSFFModFramework.Api;

/// <summary>
/// Optional base class for content-mod plugins (Centralization Tier 2, P10). Owns the
/// boilerplate every mod Plugin.cs repeats: Harmony creation, per-patch exception
/// isolation, the canonical one-line load log, and UnpatchSelf on destroy.
///
/// <para>Subclasses keep their own attributes — BepInEx reads metadata from the
/// concrete type:</para>
/// <code>
/// [BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
/// [BepInPlugin("author.MyMod", "My Mod", "1.0.0")]
/// internal class Plugin : ContentModPlugin
/// {
///     protected override void RegisterPatches(Harmony harmony)
///     {
///         TryApply("MyPatch", () => MyPatch.ApplyPatch(harmony));
///     }
/// }
/// </code>
///
/// <para>IMPORTANT: do NOT declare <c>Awake</c>/<c>OnDestroy</c> in the subclass —
/// Unity dispatches messages to the most-derived declaration, which would silently
/// replace this base lifecycle. Override <see cref="OnModAwake"/> /
/// <see cref="RegisterPatches"/> / <see cref="OnModDestroy"/> instead. Mods that need
/// per-frame ticking should use <see cref="TickEvents"/> rather than a Unity Update.</para>
/// </summary>
public abstract class ContentModPlugin : BaseUnityPlugin
{
    /// <summary>This mod's Harmony instance (id = the plugin GUID). Created before <see cref="OnModAwake"/>.</summary>
    protected Harmony Harmony { get; private set; }

    /// <summary>This mod's BepInEx logger (alias for the protected <c>Logger</c>).</summary>
    protected ManualLogSource Log => Logger;

    private void Awake()
    {
        Harmony = new Harmony(Info.Metadata.GUID);

        try { OnModAwake(); }
        catch (Exception ex)
        { Logger.LogError($"{Info.Metadata.Name}: OnModAwake failed: {ex.InnerException?.ToString() ?? ex.ToString()}"); }

        try { RegisterPatches(Harmony); }
        catch (Exception ex)
        { Logger.LogError($"{Info.Metadata.Name}: RegisterPatches failed: {ex.InnerException?.ToString() ?? ex.ToString()}"); }

        // Mod logging norm: exactly one Info line at startup.
        Logger.LogInfo($"{Info.Metadata.Name} v{Info.Metadata.Version} loaded.");
    }

    private void OnDestroy()
    {
        try { Harmony?.UnpatchSelf(); }
        catch (Exception ex) { Logger.LogWarning($"{Info.Metadata.Name}: UnpatchSelf failed: {ex.Message}"); }
        OnModDestroy();
    }

    /// <summary>Config binding and non-patch setup. Runs before <see cref="RegisterPatches"/>.</summary>
    protected virtual void OnModAwake() { }

    /// <summary>Apply Harmony patches and register framework handlers (ActionRouter, TickEvents, EncounterGuards).</summary>
    protected virtual void RegisterPatches(Harmony harmony) { }

    /// <summary>Cleanup beyond UnpatchSelf (which the base always performs).</summary>
    protected virtual void OnModDestroy() { }

    /// <summary>
    /// Runs one patch-application step with exception isolation, so a single failing
    /// patch can't take down the rest of the mod's registration.
    /// </summary>
    protected void TryApply(string label, Action apply)
    {
        if (apply == null) return;
        try { apply(); }
        catch (Exception ex)
        { Logger.LogError($"[{label}] failed: {ex.InnerException?.ToString() ?? ex.ToString()}"); }
    }
}
