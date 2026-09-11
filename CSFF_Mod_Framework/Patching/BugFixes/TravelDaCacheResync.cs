using CSFFModFramework.Injection;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Rebuilds a CT8 location card's cached <c>InGameCardBase.DismantleActions</c> array from its
/// live <c>CardModel.DismantleActions</c> List the moment the exploration popup opens on it, if
/// the two have diverged.
///
/// <para><b>Why this exists.</b> <c>ExplorationPopup.SetupButtons</c> draws the compass from the
/// CardModel LIST at index i, but <c>OnActionButtonClicked(i)</c> dispatches through the
/// InGameCardBase ARRAY - a one-shot <c>ToArray()</c> snapshot taken in <c>SetModel</c> - and
/// returns silently when <c>Length &lt;= i</c> (decompile EA 0.67i: ExplorationPopup.cs ~630-668
/// vs ~1054-1060; InGameCardBase.cs ~5253). Any List mutation after that snapshot yields a travel
/// button that renders, accepts the click, and does nothing, with no log line at any level. An
/// INJECTED travel DA is appended, so it holds the highest index and is the first casualty.
/// <see cref="ConnectionGateService.ResyncInGameDaCacheIfPresent"/> repairs the array when a gate
/// strips or restores a DA, but only for a card it can see on the player's board while no
/// environment transition is in flight; a card whose snapshot was taken by <c>LoadCards</c>
/// before the run-start gate pass restored the DA, or during a transition, kept the short array
/// until the player left and came back. Retro <c>river-bridge-east-click-noop</c>: CMC's River
/// Bridge, eastbound only - the one injected DA in the fleet that a gate ever stripped.</para>
///
/// <para><b>Scope.</b> CT8 (Location) cards only: a Liquid's array carries an extra
/// <c>EmptyLiquidAction</c> and a blueprint-slot card's array is <c>BlueprintCreationAction</c>,
/// so array != list is deliberate there. Runs once per popup open, compares by reference, and
/// costs nothing when the two already agree.</para>
///
/// <para><b>The Info line is a diagnostic, not noise.</b> A repair here is the runtime
/// confirmation of the cached-array mechanism that retro is waiting on. Leave it at
/// <c>Log.Info</c> until that retro closes (root CLAUDE.md, Debugging Discipline #2), then demote
/// to <c>Log.Debug</c> and update the retro in the same commit.</para>
/// </summary>
internal static class TravelDaCacheResync
{
    // CardTypes.Location - reflected-int compare, matching ConnectionGatePatch / WorldMapInjector.
    private const int CardTypeLocation = 8;

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var prefix = new HarmonyMethod(AccessTools.Method(typeof(TravelDaCacheResync), nameof(Setup_Prefix)));
            SafePatcher.TryPatch(harmony, "ExplorationPopup", "Setup", prefix: prefix);
        }
        catch (Exception ex)
        {
            Log.Warn($"TravelDaCacheResync: patch setup failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    // __0 = the InGameCardBase handed to ExplorationPopup.Setup(InGameCardBase). Positional, so a
    // parameter rename on a future game build cannot silently unbind the patch.
    static void Setup_Prefix(object __0)
    {
        try
        {
            if (__0 == null) return;
            var model = CardUtil.GetCardData(__0);
            if (model == null) return;
            var cardType = CardUtil.GetMemberValue(model, "CardType");
            if (cardType == null || Convert.ToInt32(cardType) != CardTypeLocation) return;

            if (ConnectionGateService.ResyncCardDaCache(__0, out int before, out int after))
            {
                Log.Info($"[TravelDaCacheResync] '{CardUtil.GetCardUniqueId(__0)}': cached DismantleActions array ({before}) " +
                         $"diverged from the CardModel list ({after}) - rebuilt before the compass opened. A travel button on " +
                         "this card would otherwise have clicked into nothing (the list was mutated after this card's " +
                         "SetModel snapshot: gate strip/restore or a later DA injection).");
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"TravelDaCacheResync: prefix threw: {Log.ExceptionText(ex)}");
        }
    }
}
