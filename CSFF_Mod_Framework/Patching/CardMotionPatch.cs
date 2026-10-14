using CSFFModFramework.Motion;
using CSFFModFramework.Util;

namespace CSFFModFramework.Patching;

/// <summary>
/// Postfixes that attach the card motion layer (<see cref="CardMotionService"/>):
///   CardGraphics.Setup(InGameCardBase) - every card a visual shows; visuals are pooled
///   InGameNPC.RefreshPortrait()        - writes the NPC's portrait onto its card after Setup
///
/// Registers only once <see cref="CardMotionLoader"/> found definitions and the
/// [Visuals] CardMotion config is on, so a game with no CardData/Motion content patches nothing.
/// </summary>
internal static class CardMotionPatch
{
    private static Harmony _harmony;
    private static bool _registered;

    public static void ApplyPatch(Harmony harmony) => _harmony = harmony;

    internal static void RegisterIfDefinitions()
    {
        if (_registered) return;
        if (!CardMotionService.Enabled)
        {
            Log.Debug("CardMotionPatch: [Visuals] CardMotion is off; nothing patched.");
            return;
        }
        if (!CardMotionService.HasDefinitions)
        {
            Log.Debug("CardMotionPatch: no card motion definitions loaded; nothing to patch.");
            return;
        }
        if (_harmony == null)
        {
            Log.Warn("CardMotionPatch: motion definitions loaded but ApplyPatch never ran; card motion unavailable.");
            return;
        }
        _registered = true;

        try
        {
            bool setup = SafePatcher.TryPatch(_harmony, typeof(CardGraphics), "Setup",
                postfix: new HarmonyMethod(typeof(CardMotionPatch), nameof(CardGraphics_Setup_Postfix)));
            bool portrait = SafePatcher.TryPatch(_harmony, typeof(InGameNPC), "RefreshPortrait",
                postfix: new HarmonyMethod(typeof(CardMotionPatch), nameof(InGameNPC_RefreshPortrait_Postfix)));
            // The 3-parameter overload is the one every encounter goes through (CLAUDE.md, Encounter System
            // Patching); it writes the encounter's picture onto EncounterIllustration.
            bool encounter = false;
            var start = AccessTools.Method(typeof(EncounterPopup), "StartEncounter",
                new[] { typeof(Encounter), typeof(InGameNPC), typeof(bool) });
            if (start != null)
            {
                _harmony.Patch(start, postfix: new HarmonyMethod(typeof(CardMotionPatch), nameof(EncounterPopup_StartEncounter_Postfix)));
                encounter = true;
            }
            if (!setup)
                Log.Warn("CardMotionPatch: CardGraphics.Setup not patched; card motion unavailable.");
            if (!portrait)
                Log.Warn("CardMotionPatch: InGameNPC.RefreshPortrait not patched; NPC portraits will not move.");
            if (!encounter)
                Log.Warn("CardMotionPatch: EncounterPopup.StartEncounter(Encounter, InGameNPC, bool) not found; encounter pictures will not move.");
            if (setup && portrait && encounter)
                Log.Debug("CardMotionPatch: patched CardGraphics.Setup, InGameNPC.RefreshPortrait and EncounterPopup.StartEncounter");
        }
        catch (Exception ex)
        {
            Log.Warn($"[CardMotionPatch] registration failed: {Log.ExceptionText(ex)}");
        }
    }

    // CardGraphics declares one Setup, Setup(InGameCardBase); the Setup(int) in .decomp/CardGraphics.cs
    // belongs to its nested CardInventoryInfo class, which AccessTools.Method(typeof(CardGraphics)) does not see.
    static void CardGraphics_Setup_Postfix(object __instance)
    {
        try { CardMotionService.OnCardSetup(__instance); }
        catch (Exception ex) { Log.Debug($"CardMotionPatch.CardGraphics_Setup_Postfix: {Log.ExceptionText(ex)}"); }
    }

    static void InGameNPC_RefreshPortrait_Postfix(object __instance)
    {
        try { CardMotionService.OnPortraitRefreshed(__instance); }
        catch (Exception ex) { Log.Debug($"CardMotionPatch.InGameNPC_RefreshPortrait_Postfix: {Log.ExceptionText(ex)}"); }
    }

    // Runs even when an encounter guard's prefix cancelled the start; Apply then only refreshes the
    // picture already showing.
    static void EncounterPopup_StartEncounter_Postfix(object __instance)
    {
        try { CardMotionService.OnEncounterStarted(__instance); }
        catch (Exception ex) { Log.Debug($"CardMotionPatch.EncounterPopup_StartEncounter_Postfix: {Log.ExceptionText(ex)}"); }
    }
}
