using System.Reflection;
using CSFFModFramework.Util;
using CSFFModFramework.Wildlife;

namespace CSFFModFramework.Patching;

/// <summary>
/// Hooks EncounterPopup.StartEncounter to fire a bear raid when the bear combat encounter
/// begins. The copper chest and other sealed containers (those lacking tag_NotSafeFromBears
/// and tag_NotSafeFromAnimals) are unaffected.
/// </summary>
internal static class WildlifeRaidPatch
{
    // Fallback: Combat_EventBear_1_Explore (EA 0.64f) — the only wildlife bear combat
    // encounter in vanilla. The live list comes from Api.VanillaIds.BearEncounters,
    // regenerated per game version.
    private const string FallbackBearEncounterUID = "72c629f49c1fb974ca8491e0a7d2e5e1";

    private static bool IsBearEncounterUid(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return false;
        var registry = Api.VanillaIds.BearEncounters;
        if (registry.Count > 0)
        {
            foreach (var known in registry)
                if (uid == known) return true;
            return false;
        }
        return uid == FallbackBearEncounterUID;
    }

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            Type encounterPopupType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { encounterPopupType = asm.GetType("EncounterPopup", false); }
                catch { }
                if (encounterPopupType != null) break;
            }

            if (encounterPopupType == null)
            {
                Log.Debug("[WildlifeRaidPatch] EncounterPopup type not found — bear raid patch skipped.");
                return;
            }

            // StartEncounter(Encounter encounter, InGameNPC npc, bool flag)
            var startEncounter = encounterPopupType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "StartEncounter" && m.GetParameters().Length == 3);

            if (startEncounter == null)
            {
                Log.Debug("[WildlifeRaidPatch] StartEncounter(3-param) not found — bear raid patch skipped.");
                return;
            }

            var postfix = typeof(WildlifeRaidPatch).GetMethod(nameof(StartEncounter_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(startEncounter, postfix: new HarmonyMethod(postfix));
            Log.Debug("[WildlifeRaidPatch] bear encounter raid patch applied.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[WildlifeRaidPatch] ApplyPatch failed: {Log.ExceptionText(ex)}");
        }
    }

    // __instance = EncounterPopup, __0 = Encounter card, __1 = InGameNPC (null = wildlife), __2 = bool
    private static void StartEncounter_Postfix(object __0, object __1)
    {
        if (!WildlifeRaidService.Enabled) return;
        if (__1 != null) return; // NPC encounter — never trigger raid
        // Harmony runs postfixes even when a prefix skipped the original — an encounter
        // suppressed by EncounterGuards must not still trigger a bear raid.
        if (Api.EncounterGuards.WasSuppressedThisFrame) return;
        if (!IsBearEncounter(__0)) return;
        WildlifeRaidService.OnBearEncounter();
    }

    private static bool IsBearEncounter(object encounter)
    {
        if (encounter == null) return false;
        try
        {
            // Primary: match by UniqueID against the per-version registry list
            var t = encounter.GetType();
            var uidProp = t.GetProperty("UniqueID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (uidProp != null && IsBearEncounterUid(uidProp.GetValue(encounter) as string)) return true;
            var uidField = t.GetField("UniqueID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (uidField != null && IsBearEncounterUid(uidField.GetValue(encounter) as string)) return true;

            // Fallback: check CardName.DefaultText for "Bear" (catches future bear encounter cards)
            var cardNameProp = t.GetProperty("CardName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? (object)t.GetField("CardName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object cardName = cardNameProp is PropertyInfo pi ? pi.GetValue(encounter)
                : cardNameProp is FieldInfo fi ? fi.GetValue(encounter) : null;
            if (cardName != null)
            {
                var defProp = cardName.GetType().GetProperty("DefaultText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var defText = defProp?.GetValue(cardName) as string ?? string.Empty;
                if (defText.IndexOf("Bear", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"[WildlifeRaidPatch] IsBearEncounter check failed: {Log.ExceptionText(ex)}");
        }
        return false;
    }
}
