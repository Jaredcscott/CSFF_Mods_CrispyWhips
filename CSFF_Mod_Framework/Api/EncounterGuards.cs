using CSFFModFramework.Util;

namespace CSFFModFramework.Api;

/// <summary>
/// Context handed to encounter guard predicates. Only wildlife/ambient encounters are
/// ever evaluated — NPC encounters are never suppressed (CLAUDE.md §Encounter System).
/// </summary>
public sealed class EncounterContext
{
    /// <summary>The Encounter ScriptableObject being started.</summary>
    public object Encounter { get; internal set; }

    /// <summary>UniqueID of the encounter, or null if it could not be read.</summary>
    public string EncounterUid { get; internal set; }
}

/// <summary>
/// Framework-owned wildlife encounter suppression (Centralization Tier 2, P9).
/// The framework holds the ONE <c>EncounterPopup.StartEncounter</c> prefix; mods
/// register predicates instead of re-implementing the patch (replaces Sirus
/// EncounterGuardPatch and the same shape in every future ward/companion mod).
///
/// <para>NPC encounters (<c>_WithNPC != null</c>) bypass guards entirely — the
/// framework never consults predicates for them.</para>
///
/// <para>Declarative alternative: ship <c>EncounterGuards/*.json</c> in the mod folder —
/// see <c>Loading.EncounterGuardLoader</c> for the schema. JSON guards suppress wildlife
/// encounters while a guard card (e.g. a companion) is in the player's environment.</para>
///
/// <code>
/// var handle = EncounterGuards.Register("WolfGuard",
///     ctx => MyWolfIsInPlayerEnv());
/// EncounterGuards.Unregister(handle);
/// </code>
/// </summary>
public static class EncounterGuards
{
    /// <summary>Opaque registration token returned by <see cref="Register"/>.</summary>
    public sealed class GuardHandle
    {
        internal string Name;
        internal Func<EncounterContext, bool> ShouldSuppress;
    }

    private static readonly List<GuardHandle> _guards = new();
    private static GuardHandle[] _snapshot = Array.Empty<GuardHandle>();

    /// <summary>Number of registered guards (API + JSON).</summary>
    public static int Count => _snapshot.Length;

    /// <summary>
    /// Registers a wildlife-encounter guard. The predicate returns true to suppress
    /// the encounter. Predicates run on the main thread when an encounter is about to
    /// start; exceptions are caught and logged per guard.
    /// </summary>
    public static GuardHandle Register(string name, Func<EncounterContext, bool> shouldSuppress)
    {
        if (shouldSuppress == null) return null;
        var handle = new GuardHandle
        {
            Name = string.IsNullOrEmpty(name) ? "Guard" : name,
            ShouldSuppress = shouldSuppress,
        };
        lock (_guards)
        {
            _guards.Add(handle);
            _snapshot = _guards.ToArray();
        }
        Log.Debug($"[EncounterGuards] registered guard '{handle.Name}' ({_snapshot.Length} total)");
        return handle;
    }

    /// <summary>Removes a guard registered with <see cref="Register"/>.</summary>
    public static void Unregister(GuardHandle handle)
    {
        if (handle == null) return;
        lock (_guards)
        {
            if (_guards.Remove(handle))
                _snapshot = _guards.ToArray();
        }
    }

    // ── Framework internals ──────────────────────────────────────────────────

    private static int _suppressedFrame = -1;

    /// <summary>
    /// True when a guard suppressed an encounter during the current rendered frame.
    /// Consumed by <c>WildlifeRaidPatch</c>'s postfix (Harmony still runs postfixes
    /// after a prefix skips the original) so a suppressed bear encounter does not
    /// trigger a bear raid.
    /// </summary>
    internal static bool WasSuppressedThisFrame => _suppressedFrame == Time.frameCount;

    /// <summary>
    /// Evaluates all guards for a wildlife encounter. Returns true if any guard
    /// suppresses it. Called only by the framework's StartEncounter prefix.
    /// </summary>
    internal static bool EvaluateWildlife(object encounter)
    {
        var snapshot = _snapshot;
        if (snapshot.Length == 0) return false;

        var ctx = new EncounterContext
        {
            Encounter = encounter,
            EncounterUid = CardUtil.GetCardUniqueId(encounter),
        };

        foreach (var g in snapshot)
        {
            try
            {
                if (!g.ShouldSuppress(ctx)) continue;
                _suppressedFrame = Time.frameCount;
                Log.Info($"[EncounterGuards] '{g.Name}' suppressed wildlife encounter '{ctx.EncounterUid ?? "?"}'.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"[EncounterGuards] guard '{g.Name}' threw: {Log.ExceptionText(ex)}");
            }
        }
        return false;
    }
}
