namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Keeps a Portal Hub arrival's destination from being wiped mid-switch, which left the player in
/// no environment and logged a <c>NullReferenceException</c> in
/// <c>GameManager.CalculateEnvironmentWeight</c> (framework plan section 28, tracker T2.763).
///
/// <para><strong>The failure</strong> (EA 0.68b <c>.decomp/GameManager.cs</c>). The environment
/// branch of <c>AddCard</c> sets <c>NextEnvironment</c>'s card (line 9222), then yields inside
/// <c>RemoveCard</c> -> <c>ChangeEnvironment</c>; <c>RemoveCard</c> afterwards copies
/// <c>NextEnvironment</c> into <c>CurrentEnvironment</c> (10661), and <c>AddCard</c> passes that to
/// <c>CalculateEnvironmentWeight</c> (9933). <c>GetCollectionDropsReport</c> (8344-8353) replaces
/// <c>NextEnvironment</c> and clears its card whenever the action has an explicit travel target or
/// its card counts as a travelling card. Measured by TestHarness 1.4.1's <c>[S28]</c> probe on
/// 2026-10-04: during a portal trip to the Metal Mines, the departure card's own stat-triggered
/// action ("Create an Invisible Levee if its missing" on vanilla
/// <c>River_GrovePine_SouthernLakeBank</c>) ran in that window, emptied <c>NextEnvironment</c>
/// five frames before the throw, and the arrival ended with <c>CurrentEnvironment</c> empty.
/// Vanilla travel awaits the same <c>AddCard</c> inside an action routine; the portal starts it
/// from a framework handler, so the game's triggered actions run beside it.</para>
///
/// <para><strong>The guard.</strong> <see cref="Track"/> wraps the portal's arrival coroutine and
/// marks the window open until it ends. While it is open, a prefix/postfix pair on
/// <c>GetCollectionDropsReport</c> puts back the <c>NextEnvironment</c> the call found if the call
/// changed it. Outside a portal arrival the pair reads one static and returns, so every other
/// action is untouched, and it never writes anything but <c>NextEnvironment</c>. It does not touch
/// <c>CalculateEnvironmentWeight</c>: swallowing that call would hide the next cause too.</para>
/// </summary>
internal static class PortalArrivalNextEnvGuard
{
    // A coroutine stopped from outside (scene unload, StopAllCoroutines) never runs its finally,
    // so the window also closes on its own after this long. A real arrival, catch-up ticks
    // included, measured ~3.5 s on T2.763.
    private const float MaxWindowSeconds = 120f;

    private static int _depth;
    private static float _openedAt;
    private static bool _patched;

    private static bool Open => _depth > 0 && Time.realtimeSinceStartup - _openedAt < MaxWindowSeconds;

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var original = AccessTools.Method(typeof(GameManager), nameof(GameManager.GetCollectionDropsReport));
            if (original == null)
            {
                Util.Log.Warn("PortalArrivalNextEnvGuard: GameManager.GetCollectionDropsReport not found; a portal arrival can still lose its destination mid-switch.");
                return;
            }

            harmony.Patch(original,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(PortalArrivalNextEnvGuard), nameof(Prefix))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(PortalArrivalNextEnvGuard), nameof(Postfix))));
            _patched = true;
            Util.Log.Debug("PortalArrivalNextEnvGuard: patched GameManager.GetCollectionDropsReport.");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"PortalArrivalNextEnvGuard: patch setup failed: {Util.Log.ExceptionText(ex)}");
        }
    }

    /// <summary>
    /// Runs <paramref name="arrival"/> as a nested coroutine with the window open for its whole
    /// life. Start the returned enumerator in place of the arrival itself.
    /// </summary>
    internal static IEnumerator Track(IEnumerator arrival)
    {
        if (!_patched)
            Util.Log.Warn("PortalArrivalNextEnvGuard: the GetCollectionDropsReport patch is not applied; this portal arrival is unguarded.");

        _depth = Open ? _depth + 1 : 1;
        _openedAt = Time.realtimeSinceStartup;
        try
        {
            while (arrival.MoveNext())
                yield return arrival.Current;
        }
        finally
        {
            if (_depth > 0) _depth--;
        }
    }

    private static void Prefix(GameManager __instance, out EnvID __state)
    {
        __state = Open ? __instance.NextEnvironment : default;
    }

    private static void Postfix(GameManager __instance, CardAction _Action, InGameCardBase _FromCard, EnvID __state)
    {
        if (__state.IsNull || !Open) return;
        try
        {
            if (__instance.NextEnvironment.EnvCard == __state.EnvCard) return;
            __instance.NextEnvironment = __state;
            // Info until T2.763 confirms the fix in-game; at most once per triggered action during
            // a portal arrival, not a hot path.
            string action = _Action != null ? _Action.ActionName.ToString() : "null";
            string card = _FromCard && _FromCard.CardModel ? _FromCard.CardModel.UniqueID : "null";
            Util.Log.Info($"PortalArrivalNextEnvGuard: kept the portal arrival's destination '{__state.EnvCard.UniqueID}' "
                + $"after '{action}' on '{card}' cleared it mid-switch.");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"PortalArrivalNextEnvGuard: restore failed; the arrival may land in no environment: {Util.Log.ExceptionText(ex)}");
        }
    }
}
