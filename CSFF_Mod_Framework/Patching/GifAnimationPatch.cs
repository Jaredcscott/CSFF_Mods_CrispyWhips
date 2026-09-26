using CSFFModFramework.Gif;
using CSFFModFramework.Util;

namespace CSFFModFramework.Patching;

/// <summary>
/// Harmony postfixes that drive GIF animation on card visuals. Two hooks, both on CardGraphics:
///   CardGraphics.Setup(InGameCardBase)  - a card visual is (re)initialised; graphics are pooled
///   CardGraphics.RefreshCookingStatus() - cooking or slot state changed; vanilla rewrites the art here
///
/// Registration waits until GifLoader has read the definitions: LoadOrchestrator calls
/// RegisterIfDefinitions right after its GifLoader phase, so a game without GIF content patches
/// nothing. Before 2.26.7 the definitions check ran in Plugin.Awake, before any definition had
/// loaded, so these patches never registered; the cooking hook also named
/// InGameCardBase.RefreshCookingStatus, which does not exist (the method is on CardGraphics and
/// takes no parameter, .decomp/CardGraphics.cs).
/// </summary>
internal static class GifAnimationPatch
{
    private static Harmony _harmony;
    private static bool _registered;

    /// <summary>Called from Plugin.Awake; only records the Harmony instance (see class remarks).</summary>
    public static void ApplyPatch(Harmony harmony) => _harmony = harmony;

    /// <summary>Called once GifLoader has run. Patches only when a mod shipped GIF definitions.</summary>
    internal static void RegisterIfDefinitions()
    {
        if (_registered) return;
        if (!GifAnimationService.HasDefinitions)
        {
            Log.Debug("GifAnimationPatch: no GIF card definitions loaded; nothing to patch.");
            return;
        }
        if (_harmony == null)
        {
            Log.Warn("GifAnimationPatch: GIF definitions loaded but ApplyPatch never ran; GIF animation unavailable.");
            return;
        }
        _registered = true;

        try
        {
            bool setup = SafePatcher.TryPatch(_harmony, typeof(CardGraphics), "Setup",
                postfix: new HarmonyMethod(typeof(GifAnimationPatch), nameof(CardGraphics_Setup_Postfix)));
            bool cooking = SafePatcher.TryPatch(_harmony, typeof(CardGraphics), "RefreshCookingStatus",
                postfix: new HarmonyMethod(typeof(GifAnimationPatch), nameof(CardGraphics_RefreshCookingStatus_Postfix)));

            if (setup && cooking)
                Log.Debug("GifAnimationPatch: patched CardGraphics.Setup and CardGraphics.RefreshCookingStatus");
            else if (setup)
                Log.Warn("GifAnimationPatch: CardGraphics.RefreshCookingStatus not patched; GIFs will not switch on cooking and vanilla art refreshes will pause a GIF until its next frame");
            else
                Log.Warn("GifAnimationPatch: CardGraphics.Setup not patched; card GIF animation unavailable");
        }
        catch (Exception ex)
        {
            Log.Warn($"[GifAnimationPatch] registration failed: {Log.ExceptionText(ex)}");
        }
    }

    // -------------------------------------------------------------------------
    // Patch methods
    // -------------------------------------------------------------------------

    // CardGraphics.Setup(InGameCardBase _From): __instance = CardGraphics, __0 = the card it now shows.
    static void CardGraphics_Setup_Postfix(object __instance, object __0)
    {
        try
        {
            GifAnimationService.OnCardSetup(__instance, __0);
        }
        catch (Exception ex)
        {
            Log.Debug($"GifAnimationPatch.CardGraphics_Setup_Postfix: {Log.ExceptionText(ex)}");
        }
    }

    // CardGraphics.RefreshCookingStatus(): __instance = CardGraphics. The card and its cooking state
    // are read from CardGraphics.CardLogic.
    static void CardGraphics_RefreshCookingStatus_Postfix(object __instance)
    {
        try
        {
            GifAnimationService.OnRefreshCookingStatus(__instance);
        }
        catch (Exception ex)
        {
            Log.Debug($"GifAnimationPatch.CardGraphics_RefreshCookingStatus_Postfix: {Log.ExceptionText(ex)}");
        }
    }
}
