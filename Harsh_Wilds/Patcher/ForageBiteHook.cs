using CSFFModFramework.Api;

namespace HarshWilds.Patcher;

/// <summary>
/// Insect bites while foraging overgrown ground (plan section 21, W0-14 branch a). An
/// <see cref="ActionRouter"/> after-handler on the vanilla "Forage" action of the 25 thicket places
/// (the UID set is the place filter, because a Forage action on a grove or clearing must not bite).
/// Outside winter, with <c>hw_switch_foragebites</c> at 1, it rolls <see cref="Chance"/> lowered by
/// FootProtection, HandProtection and BugProtection (never LegProtection: no live card writes it,
/// W0-14), and on a bite adds <see cref="Bites"/> to vanilla BugBites with one notice line. Every number
/// is a tunable placeholder, not balanced against play.
///
/// <para>BugBites has a base rate of -1 (0 to 384, first status band 96), so the write decays on its own:
/// it is not the base-rate-0 permanence trap of CLAUDE.md "Food Items and EdibleStats Pattern". No
/// Nausea and no Pain is written here.</para>
///
/// <para>Registered by <see cref="SmithingBurnHook.RegisterOnce"/>, which holds the once-per-process
/// latch, so this class has no latch of its own. Forcing is the same as the burn's: the hidden stat
/// <c>hw_bite_force</c> ships at 0 and, set above 0, replaces the dice value, so protection stats can be
/// shown to lower the odds against one fixed roll.</para>
///
/// <para>Player only: only the player's own Forage rolls. A partner's Forage duty runs the same
/// thicket action through the router, so the <c>Before</c> reads the action's real user from
/// <see cref="ActionUser"/> into a private per-dispatch <see cref="DispatchFlag"/>, and a Forage that
/// is not the player's never rolls.</para>
/// </summary>
internal static class ForageBiteHook
{
    internal const string SwitchStat = "hw_switch_foragebites";
    internal const string ForceStat = "hw_bite_force";

    private const string BugBitesUid = "5c42aacf9a2584f4fb44e69e4da8e369";
    private const string FootProtectionUid = "384d7070fef781a40aba1f0f16dd5fa6"; // 0 to 10; shoes 8, sandals 5
    private const string HandProtectionUid = "7d2cf5edc2ae50146b9aa7aa235bbf89"; // 0 to 1; gloves
    private const string BugProtectionUid = "57ae811e531f6cd45994063eaff2d724"; // cloth +1, hoodie +1, bee suit +5

    /// <summary>HW_FORAGE_BITE_CHANCE: the chance one forage in a thicket bites a fully unprotected forager. Tunable placeholder.</summary>
    internal const float Chance = 0.25f;

    /// <summary>HW_FORAGE_BITES: BugBites added by one bite (its first status band starts at 96). Tunable placeholder.</summary>
    internal const float Bites = 120f;

    // Share of the odds each protection removes at its full reading (a placeholder): shoes (8) and gloves
    // (1) are the common ones, a bee suit (5) the rare one. Together they leave a tenth of the odds.
    private const float FootShare = 0.4f, FootFull = 8f;
    private const float HandShare = 0.3f, HandFull = 1f;
    private const float BugShare = 0.2f, BugFull = 5f;

    // The 25 CT8 thicket places with a Forage action (W0-14).
    private static readonly HashSet<string> Thickets = new(StringComparer.OrdinalIgnoreCase)
    {
        "a37bdecbef22b354bbddd37732a54a03", "945a359be2dda5144a0a91ae81ba140f", "6f1df28efe418cd4caeb66c8228583f1",
        "392726f7792a24849b9f3af9148c0871", "5428355fcca1b464e9dfad18d69bf4f6", "e2fe83127f87959488d0bc35d65760a4",
        "30a11e1f41c65774fbdb291aadd64ccf", "b32c74532ed8c634d991a6028bb435e2", "5aba809c3bd15904d80260034b976c24",
        "7eb4ea3c101f2824686e53241133d0cc", "d64538c6aa0ac014e83221fda09ea8eb", "cb861225c880cdf4f8d07bdf99abae51",
        "8c6c0c29e4de0d741b40a2f19e8380b8", "c780ea86ceddfa949bd99e709c287d08", "48cf028fc67cdb44cab666ebfa143789",
        "c021b70a40c0caa438322dbab799719e", "569186c2715d16441bd502fdbedcb0e3", "fe69f0d7203eeda4c95b78b76d1deb36",
        "c155e398639f55040b52d30305468336", "9b72ab8ae43c10345a6d9168d3096b50", "4a85ea00396fbe84bb494ddc7abe0de2",
        "f516c75849230e04e8b386d9f3b25d53", "343475c3f2d73c84f8ffbbbe1f288414", "1761b5a45b73fee43a68f6dcec138ef6",
        "6940d62a86fc2974da75ce0431ca44e8",
    };

    private static readonly HashSet<string> Warned = new(StringComparer.Ordinal);

    // Whether this dispatch's Forage is the player's own, set in Before and read in After. A private
    // per-dispatch flag, never the shared ActionContext.Tag (see DispatchFlag).
    private static readonly DispatchFlag PlayerForage = new();

    internal static void Register()
    {
        ActionRouter.Register(new ActionHandler
        {
            Name = "HW.ForageBite",
            CardPredicate = ctx => ctx.CardUid != null && Thickets.Contains(ctx.CardUid),
            ActionNamePrefix = "Forage",
            Timing = ActionTiming.AfterWrapped,
            // Player only, as SmithingBurnHook's build: a partner's Forage duty on a marked thicket reaches
            // the router through the same PerformActionAsEnumerator (.decomp/AffectItemsDutyAction.cs:662)
            // and must not bite the player. ActionUser reads the action's real user in the router's prefix.
            Before = ctx =>
            {
                PlayerForage.Set(ctx, ActionUser.IsPlayer(ctx, "ForageBiteHook"));
                return false;
            },
            After = AfterForage,
        });
    }

    /// <summary>The bite chance for a forager with these protection readings (pure, so a test can read it).</summary>
    internal static float OddsFor(float foot, float hand, float bug)
    {
        float keep = 1f
            - FootShare * Clamp01(foot / FootFull)
            - HandShare * Clamp01(hand / HandFull)
            - BugShare * Clamp01(bug / BugFull);
        return Chance * keep;
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    private static void AfterForage(ActionContext ctx)
    {
        if (!PlayerForage.Get(ctx)) return;

        float on = SmithingBurnHook.Read(SwitchStat);
        if (float.IsNaN(on) || on < 0.5f) return;
        if (GameQuery.IsWinter) return;

        float foot = SmithingBurnHook.Read(FootProtectionUid);
        float hand = SmithingBurnHook.Read(HandProtectionUid);
        float bug = SmithingBurnHook.Read(BugProtectionUid);
        if (float.IsNaN(foot) || float.IsNaN(hand) || float.IsNaN(bug))
        {
            if (Warned.Add("stat-unreadable"))
                Plugin.Logger.LogWarning("ForageBiteHook: a protection stat could not be read, so no bite rolls (the framework logged which).");
            return;
        }

        if (!(SmithingBurnHook.Dice(ForceStat) < OddsFor(foot, hand, bug))) return;

        float now = StatAccess.Modify(BugBitesUid, Bites);
        if (float.IsNaN(now))
        {
            if (Warned.Add("bites-write"))
                Plugin.Logger.LogWarning("ForageBiteHook: could not write BugBites, so the bite had no effect (the framework logged why).");
            return;
        }
        Plugin.Logger.LogInfo($"ForageBiteHook: foraging in {ctx.CardUid} bit the player; BugBites now {now:0}.");
        HwNotice.Show("HarshWilds_ForageBite_Notice", "Insects swarm you as you push through the thicket.");
    }
}
