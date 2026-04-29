using CSFFModFramework.Gif;
using CSFFModFramework.Reflection;
using CSFFModFramework.Util;

namespace CSFFModFramework.Patching;

/// <summary>
/// Harmony postfixes that drive GIF animation on card visuals. Two hooks:
///   CardGraphics.Setup           — runs after the card image is initialized
///   InGameCardBase.RefreshCookingStatus — runs when cooking state flips
///
/// Both patches use string-based type resolution (deferred one frame via coroutine)
/// because Assembly-CSharp types aren't reliably indexed during BepInEx Awake.
/// </summary>
internal static class GifAnimationPatch
{
    public static void ApplyPatch(Harmony harmony)
    {
        if (!GifAnimationService.HasDefinitions)
        {
            Log.Debug("GifAnimationPatch: no GIF card definitions found — skipping patch registration.");
            return;
        }

        Plugin.Instance.StartCoroutine(DeferredPatch(harmony));
    }

    private static System.Collections.IEnumerator DeferredPatch(Harmony harmony)
    {
        yield return null; // one frame — Assembly-CSharp types reliably indexed after this

        // --- CardGraphics.Setup postfix ---
        var cardGraphicsType = ReflectionCache.FindType("CardGraphics");
        if (cardGraphicsType != null)
        {
            bool patched = SafePatcher.TryPatch(
                harmony, cardGraphicsType, "Setup",
                postfix: new HarmonyMethod(typeof(GifAnimationPatch), nameof(CardGraphics_Setup_Postfix)));

            if (patched)
                Log.Debug("GifAnimationPatch: patched CardGraphics.Setup");
            else
                Log.Warn("GifAnimationPatch: failed to patch CardGraphics.Setup");
        }
        else
        {
            Log.Warn("GifAnimationPatch: CardGraphics type not found — card GIF support unavailable");
        }

        // --- InGameCardBase.RefreshCookingStatus postfix ---
        var inGameCardBaseType = ReflectionCache.FindType("InGameCardBase");
        if (inGameCardBaseType != null)
        {
            bool patched = SafePatcher.TryPatch(
                harmony, inGameCardBaseType, "RefreshCookingStatus",
                postfix: new HarmonyMethod(typeof(GifAnimationPatch), nameof(InGameCardBase_RefreshCookingStatus_Postfix)));

            if (patched)
                Log.Debug("GifAnimationPatch: patched InGameCardBase.RefreshCookingStatus");
            else
                Log.Warn("GifAnimationPatch: failed to patch InGameCardBase.RefreshCookingStatus — cooking GIF switching may not work");
        }
    }

    // -------------------------------------------------------------------------
    // Patch methods
    // -------------------------------------------------------------------------

    // CardGraphics.Setup(InGameCardBase card) — fires when a card's visual is set up.
    // __instance = CardGraphics component; first parameter is the InGameCardBase owning the card.
    // We pass both so GifAnimationService can search either for the Image component.
    static void CardGraphics_Setup_Postfix(object __instance, object __0)
    {
        try
        {
            GifAnimationService.OnCardSetup(__instance, __0);
        }
        catch (Exception ex)
        {
            Log.Debug($"GifAnimationPatch.CardGraphics_Setup_Postfix: {ex.Message}");
        }
    }

    // InGameCardBase.RefreshCookingStatus(bool isCooking)
    // __instance = InGameCardBase; first bool parameter = new cooking state.
    static void InGameCardBase_RefreshCookingStatus_Postfix(object __instance, bool __0)
    {
        try
        {
            GifAnimationService.OnRefreshCookingStatus(__instance, __0);
        }
        catch (Exception ex)
        {
            Log.Debug($"GifAnimationPatch.InGameCardBase_RefreshCookingStatus_Postfix: {ex.Message}");
        }
    }
}
