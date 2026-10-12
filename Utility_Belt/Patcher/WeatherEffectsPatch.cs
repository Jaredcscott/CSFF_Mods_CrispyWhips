namespace Utility_Belt.Patcher;

/// <summary>
/// [Accessibility] Hide Rain And Snow Effects (plan U1). Vanilla's SpecialEffectsManager.Update shows the
/// weather particle layer every frame when the player is outside and hides it inside. This postfix runs
/// after that and, when the option is on, hides the layer again through the game's own
/// AmbienceImageEffect.HideWeatherEffects, so the same hide path the game uses indoors is reused. Weather
/// sound and the weather's effect on the character are untouched.
/// </summary>
/// <remarks>
/// The patch target is Update, a large caller, never the small ShowWeatherEffects it calls (a small callee
/// can be inlined into an already-patched caller and the patch silently never fires; CLAUDE.md "Harmony
/// Patching Pitfalls"). Update is private, so it is resolved through AccessTools and a miss is logged once.
/// </remarks>
internal static class WeatherEffectsPatch
{
    private static bool _warnedHide;

    internal static void ApplyPatch(Harmony harmony)
    {
        var update = AccessTools.Method(typeof(SpecialEffectsManager), "Update");
        if (update == null)
        {
            Plugin.Logger.LogWarning("[UB] SpecialEffectsManager.Update not found; Hide Rain And Snow Effects stays off.");
            return;
        }

        harmony.Patch(update, postfix: new HarmonyMethod(typeof(WeatherEffectsPatch), nameof(UpdatePostfix)));
    }

    private static void UpdatePostfix()
    {
        if (!Plugin.HideWeatherEffects.Value)
        {
            return;
        }

        try
        {
            AmbienceImageEffect.HideWeatherEffects();
        }
        catch (Exception ex)
        {
            // The ambience singleton or one of its parents is not there (a scene change, a game update
            // renaming a field). Say so once, then keep trying silently: the next frame may have it.
            if (!_warnedHide)
            {
                _warnedHide = true;
                Plugin.Logger.LogWarning($"[UB] HideWeatherEffects failed ({ex.GetType().Name}: {ex.Message}); weather effects stay as the game draws them.");
            }
        }
    }
}
