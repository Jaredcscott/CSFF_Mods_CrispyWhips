using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace HarshWilds.Patcher;

/// <summary>
/// Smithing burns (plan section 20, option a): an unskilled smith working bare-handed may burn a hand.
/// An <see cref="ActionRouter"/> after-handler on the blueprint build action of the 30 smithing
/// blueprints (Documentation/Research/HarshWilds/wave0-sleep-craft.md W0-13: the act is a blueprint
/// build whose key <c>BUILD</c> every blueprint shares, so the filter is the blueprint card's UID).
/// When the build finishes with <c>hw_switch_smithingburns</c> at 1, Skill_Smithing below
/// <see cref="SkillMax"/> and HandProtection at 0, it rolls <see cref="Chance"/> and gives the vanilla
/// burn wound through <see cref="SpawnService"/>, with one notice line. Both numbers are tunable
/// placeholders, not balanced against play.
///
/// <para>Registered ONCE per process (<see cref="RegisterOnce"/>: an early-return guard plus its
/// latch, pattern b of CLAUDE.md "Per-Run Event Registration"), from the plugin's patch pass, never
/// from <c>OnGMInitialized</c>: <c>ActionRouter.Register</c> is additive, so a registration repeated
/// on every save reload would roll the dice once per reload already behind it. The same one-time
/// registration also installs <see cref="ForageBiteHook"/>.</para>
///
/// <para>Player only: the router's <see cref="ActionContext"/> carries no user, and the build also runs
/// for a companion's duty (<c>AffectItemsDutyAction</c>, with <c>_ZeroDaytimeCost</c>). The player's
/// own build is the one the construction popup started, so <c>BlueprintPopup.DoingBuildAction</c> read
/// in the prefix (Before) is the discriminator; a build with it false never rolls. The verdict is
/// kept in this hook's own per-dispatch <see cref="DispatchFlag"/>, not in the shared
/// <c>ActionContext.Tag</c>, which another mod's handler on the same build could overwrite.</para>
///
/// <para>The burn is logged and its notice shown only once the wound card is on the board:
/// <c>SpawnService.Spawn</c> returns null whether or not it spawned, so a short coroutine counts the
/// live wound cards before the spawn and waits up to <see cref="ConfirmFrames"/> frames for one more,
/// and warns once if none appears.</para>
///
/// <para>Forcing: the hidden stat <c>hw_burn_force</c> ships at 0 (inert). A harness sets it to a dice
/// value above 0 (0.05 for "a low roll"): the roll then reads that value instead of a random one, so
/// the same stat that proves a burn also proves a protected smith is not burned (the conditions still
/// apply). A player never touches it.</para>
/// </summary>
internal static class SmithingBurnHook
{
    internal const string SwitchStat = "hw_switch_smithingburns";
    internal const string ForceStat = "hw_burn_force";

    /// <summary>The vanilla burn wound W_FireBurn.</summary>
    internal const string BurnWoundUid = "3ba0c209e55d7cb4d8da51697e34b584";

    private const string SmithingSkillUid = "5e59f7e4d27b87e45b9c0b6b7bfcbf64"; // Skill_Smithing, 0 to 150
    private const string HandProtectionUid = "7d2cf5edc2ae50146b9aa7aa235bbf89"; // 0 to 1, 1 only while gloves are worn

    /// <summary>HW_BURN_SKILL_MAX: a smith at or above this Skill_Smithing is never burned. Tunable placeholder.</summary>
    internal const float SkillMax = 40f;

    /// <summary>HW_BURN_CHANCE: the chance one qualifying build burns the smith. Tunable placeholder.</summary>
    internal const float Chance = 0.15f;

    // The 30 smithing blueprints (W0-13): the 21 Bp_Smithed_Blank* plus Bp_Hook_Metal, Bp_MetalBar,
    // Bp_Needle_Metal, Bp_ProcessIronSlag, Bp_RemakeIronBar, Bp_SplitIntoIronBars, Bp_Torc_Metal,
    // Bp_TwistedRing_Metal and Bp_Wire_Metal. A name prefix would miss the nine.
    private static readonly HashSet<string> SmithingBlueprints = new(StringComparer.OrdinalIgnoreCase)
    {
        "201544ef76a38484a92d503ad0b8b5bb", "79914aea4de38454db433fdbb79b2f2c", "6ca975f07efcd8e44bb7fac9637a9973",
        "784149c0b6b14124aa909ed86b40d6a3", "5f852e1259b488246b425f31f816ecac", "3eb8c10692167b0418fe5d061d53270e",
        "2dd7bf066fbffbd4fb7a49c5f368e898", "b413446ae93c38a4cb9423b92749a657", "7f3efdfa10338f443af44a6f4139bfdd",
        "edc293984f90e914bb198ca24c202285", "00c24af08b2402444a6ce73d603e762d", "0e4b7c0bcf9f79344a511d681d358f43",
        "dc9f6668506bcd648a16e9eaba04353e", "4b2ecfcb0fddebd41bc9738a6de89277", "2109f1bfe4a599643bc75d91c81898c0",
        "746a04e689931dd4a9ba2471e01f524a", "f89f63554fa3155499e7b8f7b016b6fb", "9ad6d83f2774f8c46ab5f9f8f9f62bec",
        "50dc4c8b43f017645820ae5894fb16f6", "2ba69f17d2e1a9d458fd2ccfac6953b0", "f5e9c7aa299405b45adb3bbe62284df7",
        "326d07d3b0bef6647b0f2c4930ffe9d2", "4d2bb7871eecb5743a21f3e9582b79bf", "df33e58402e17064aa4ba05ddadbfca4",
        "c31ea4db20c29174f90625e78c088ee6", "4e95dada824d75446a84bcf24bd79ae6", "3f66dc75fd708ce4092c044ba7e17dd5",
        "32371e91a1b7e3e46a42c364c51e93a1", "9b07a8cff38af0645b9bbe21653f7c55", "28e308c54e0462c4eac1d2cec9dcecc3",
    };

    /// <summary>HW_BURN_CONFIRM_FRAMES: how many frames the burn waits for the wound card to appear
    /// (GiveCard places it through vanilla's AddCard coroutine, so not at once).</summary>
    private const int ConfirmFrames = 120;

    private static readonly HashSet<string> Warned = new(StringComparer.Ordinal);
    private static bool _registered;

    // Whether this dispatch's build is the player's own, set in Before and read in After. A private
    // per-dispatch flag, never the shared ActionContext.Tag that every handler on the dispatch can
    // overwrite (see DispatchFlag).
    private static readonly DispatchFlag PlayerBuild = new();

    /// <summary>Installs this mod's ActionRouter handlers, once per process.</summary>
    internal static void RegisterOnce()
    {
        if (_registered) return;
        _registered = true;

        try
        {
            ActionRouter.Register(new ActionHandler
            {
                Name = "HW.SmithingBurn",
                CardPredicate = ctx => ctx.CardUid != null && SmithingBlueprints.Contains(ctx.CardUid),
                ActionKeyPrefix = "BUILD",
                ActionNamePrefix = "Build",
                Timing = ActionTiming.AfterWrapped,
                Before = ctx =>
                {
                    PlayerBuild.Set(ctx, IsPlayerBuild());
                    return false;
                },
                After = AfterBuild,
            });
            ForageBiteHook.Register();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"SmithingBurnHook: failed to register the action handlers: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    private static bool IsPlayerBuild()
    {
        try
        {
            var popup = GraphicsManager.Instance?.BlueprintPopup;
            if (popup == null)
            {
                WarnOnce("popup-null", "SmithingBurnHook: GraphicsManager.Instance.BlueprintPopup is not available, so no smithing build is read as the player's and no burn can roll.");
                return false;
            }
            return popup.DoingBuildAction;
        }
        catch (Exception ex)
        {
            WarnOnce("popup-threw", $"SmithingBurnHook: reading the construction popup threw: {ex.InnerException?.ToString() ?? ex.ToString()}");
            return false;
        }
    }

    private static void AfterBuild(ActionContext ctx)
    {
        if (!PlayerBuild.Get(ctx)) return;

        float on = Read(SwitchStat);
        if (float.IsNaN(on) || on < 0.5f) return;

        float skill = Read(SmithingSkillUid);
        float gloves = Read(HandProtectionUid);
        if (float.IsNaN(skill) || float.IsNaN(gloves))
        {
            WarnOnce("stat-unreadable", "SmithingBurnHook: Skill_Smithing or HandProtection could not be read, so no burn rolls (the framework logged which).");
            return;
        }
        if (skill >= SkillMax || gloves >= 0.5f) return;

        if (!(Dice(ForceStat) < Chance)) return;

        // Spawn returns null on success too (GiveCard is void), so the burn is reported only once the
        // wound card is really there: count the live wounds before, then watch for one more.
        var gm = GameManager.Instance;
        if (gm == null)
        {
            WarnOnce("gm-null", "SmithingBurnHook: GameManager.Instance is not available, so no burn is given.");
            return;
        }
        int before = CountLive(gm, BurnWoundUid);
        SpawnService.Spawn(BurnWoundUid);
        gm.StartCoroutine(ConfirmBurn(before, skill, ctx.CardUid));
    }

    // yield return null, never WaitForSeconds: timeScale is 0 while the game waits for the player.
    private static IEnumerator ConfirmBurn(int before, float skill, string blueprintUid)
    {
        for (int frame = 0; frame < ConfirmFrames; frame++)
        {
            yield return null;
            var gm = GameManager.Instance;
            if (gm == null) break;
            if (CountLive(gm, BurnWoundUid) > before)
            {
                Plugin.Logger.LogInfo($"SmithingBurnHook: a bare-handed smith at Skill_Smithing {skill:0.#} was burned (blueprint {blueprintUid}).");
                HwNotice.Show("HarshWilds_SmithingBurn_Notice", "The hot metal burns your bare hands.");
                yield break;
            }
        }
        WarnOnce("wound-not-spawned", $"SmithingBurnHook: the burn rolled but no burn wound ({BurnWoundUid}) appeared within {ConfirmFrames} frames, so no burn is reported (the framework logged any spawn failure).");
    }

    /// <summary>Live (not destroyed) cards of this UniqueID in the game's card list.</summary>
    private static int CountLive(GameManager gm, string uid)
    {
        var cards = gm.AllCards;
        if (cards == null) return 0;
        int n = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            if (card == null || card.Destroyed || card.CardModel == null) continue;
            if (string.Equals(card.CardModel.UniqueID, uid, StringComparison.Ordinal)) n++;
        }
        return n;
    }

    /// <summary>A stat's current value by UID, NaN when it cannot be read.</summary>
    internal static float Read(string statUid) => StatAccess.Get(statUid);

    /// <summary>The roll's dice, 0 to 1: the forcing stat's value while it is above 0, a random one otherwise.</summary>
    internal static float Dice(string forceStatUid)
    {
        float forced = StatAccess.Get(forceStatUid);
        if (!float.IsNaN(forced) && forced > 0f) return forced;
        return UnityEngine.Random.value;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (Warned.Add(cause)) Plugin.Logger.LogWarning(message);
    }
}
