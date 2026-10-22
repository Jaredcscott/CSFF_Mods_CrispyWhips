using System.Runtime.CompilerServices;
using CSFFModFramework.Api;

namespace HarshWilds.Patcher;

/// <summary>
/// Who performs an action the <see cref="ActionRouter"/> dispatches: the player or someone else (a
/// companion's duty, an event, a stat trigger). The router's <see cref="ActionContext"/> carries no
/// user, but the three game methods it patches all receive one: <c>GameManager.ActionRoutine</c>,
/// <c>PerformActionAsEnumerator</c> and <c>PerformStackActionRoutine</c> take an
/// <c>InGameNPCOrPlayer _User</c> (<c>.decomp/GameManager.cs</c>: the player's own buttons pass
/// <c>InGameNPCOrPlayer.PlayerAgent</c>, from <c>InspectionPopup</c> and <c>ExplorationPopup</c>; a
/// partner's duty passes its NPC, <c>.decomp/AffectItemsDutyAction.cs</c> lines 422 and 662).
///
/// <para>A prefix on each of the three, at <see cref="Priority.First"/>, runs ahead of the router's
/// own prefix (<see cref="Priority.High"/>) on the same call and records that call's action and user.
/// A handler's <c>Before</c>, which the router runs synchronously inside its prefix, then reads the
/// record through <see cref="IsPlayer"/>, which answers only for the action object just recorded and
/// only on the frame it was recorded: anything else is "not the player", with one warning per cause,
/// so a hook that hurts the player fails closed rather than biting for someone else's action.</para>
///
/// <para>Installed once per process from the plugin's patch pass (Harmony patches; nothing here is
/// per-run).</para>
/// </summary>
internal static class ActionUser
{
    private static readonly string[] Methods = { "ActionRoutine", "PerformActionAsEnumerator", "PerformStackActionRoutine" };
    private static readonly HashSet<string> Warned = new(StringComparer.Ordinal);

    private static int _installed;
    private static object _lastAction;
    private static bool _lastIsPlayer;
    private static int _lastFrame = -1;

    internal static void ApplyPatch(Harmony harmony)
    {
        var prefix = new HarmonyMethod(typeof(ActionUser), nameof(Record_Prefix)) { priority = Priority.First };
        foreach (var name in Methods)
        {
            var target = AccessTools.Method(typeof(GameManager), name);
            if (target == null)
            {
                Plugin.Logger.LogWarning($"ActionUser: GameManager.{name} not found, so an action arriving through it is never read as the player's.");
                continue;
            }
            harmony.Patch(target, prefix: prefix);
            _installed++;
        }
    }

    // Harmony binds by parameter name; all three methods name these two the same way.
    private static void Record_Prefix(CardAction _Action, InGameNPCOrPlayer _User)
    {
        _lastAction = _Action;
        _lastIsPlayer = _User.Player;
        _lastFrame = UnityEngine.Time.frameCount;
    }

    /// <summary>
    /// True only when the action being dispatched was started by the player. Call it from a handler's
    /// <c>Before</c> (the router's prefix), never from <c>After</c>, which runs after other actions may
    /// have been recorded.
    /// </summary>
    internal static bool IsPlayer(ActionContext ctx, string hook)
    {
        if (_installed == 0)
        {
            WarnOnce("not-installed", $"ActionUser: no action user recorder is installed, so {hook} never reads an action as the player's.");
            return false;
        }
        if (ctx == null || ctx.Action == null || !ReferenceEquals(ctx.Action, _lastAction) || _lastFrame != UnityEngine.Time.frameCount)
        {
            WarnOnce("unmatched:" + hook, $"ActionUser: {hook} saw an action with no recorded user (route {ctx?.Route ?? "?"}), so it is not read as the player's.");
            return false;
        }
        return _lastIsPlayer;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (Warned.Add(cause)) Plugin.Logger.LogWarning(message);
    }
}

/// <summary>
/// One hook's own per-dispatch flag, set in <c>Before</c> and read in <c>After</c>. Keyed by the
/// dispatch's <see cref="ActionContext"/> in a table only its owning hook holds, so it never shares
/// the context's single <see cref="ActionContext.Tag"/> slot, which every handler matching the same
/// dispatch reads and writes. Weak keys: an entry goes when the router drops the context.
/// </summary>
internal sealed class DispatchFlag
{
    private sealed class Box
    {
        internal bool Value;
    }

    private readonly ConditionalWeakTable<ActionContext, Box> _flags = new();

    internal void Set(ActionContext ctx, bool value)
    {
        if (ctx == null) return;
        _flags.Remove(ctx);
        _flags.Add(ctx, new Box { Value = value });
    }

    /// <summary>The flag this hook set for this dispatch; false when it set none.</summary>
    internal bool Get(ActionContext ctx) => ctx != null && _flags.TryGetValue(ctx, out var box) && box.Value;
}
