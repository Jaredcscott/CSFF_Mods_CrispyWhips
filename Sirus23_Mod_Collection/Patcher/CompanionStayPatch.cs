using System;
using System.Reflection;
using CSFFModFramework.Util;
using HarmonyLib;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// Backs the "Stay Here" / "Follow Me" DismantleActions on the Wolf/Fox/Owl companions, and
/// automatically leaves the Owl behind at the last outdoor spot when the player heads into a
/// cave or man-made structure (birds don't belong underground/indoors — the Owl otherwise has
/// no way to express that on its own).
///
/// The companion's follow behavior is vanilla `AlwaysUpdate: true` on the CardData, which
/// forces InGameCardBase.IndependentFromEnv to true — GameManager.ChangeEnvironment reads
/// that property (not a static AlwaysUpdate flag lookup) to decide whether a card rides
/// along with the player or gets saved into the environment it's currently in. Companions
/// don't have per-instance CardData (it's the shared template), so we can't flip
/// AlwaysUpdate itself without affecting every instance of that UID — instead we postfix
/// the property getter and force it false when either (a) the card's own SpecialDurability2
/// "stay flag" (toggled by the DAs via Special2Change) reads 1, or (b) for the Owl only, the
/// GameManager.NextEnvironment destination is a known cave/mine/tunnel or man-made structure
/// (checked primarily by UniqueID against <see cref="IndoorOrCaveEnv"/>'s allowlist, with a
/// CardTags scan as a secondary/future-proofing signal — see that class's doc comments).
/// This works identically whether the destination is reached by normal map travel or by
/// Portal Hub teleport (CSFFModFramework/Portal/PortalService.cs) — both set NextEnvironment
/// before ChangeEnvironment's per-card IndependentFromEnv pass runs. When false, the very next
/// environment change treats the companion like any other bound item: it's saved into the
/// current environment's card list and removed from the live board, exactly like every
/// vanilla non-AlwaysUpdate card already works. Toggling "Follow Me" later just clears the
/// stay flag so the normal AlwaysUpdate path resumes next transition; the indoor/cave check
/// is re-evaluated fresh on every transition and isn't sticky.
/// </summary>
internal static class CompanionStayPatch
{
    private static readonly string[] StayCapableCompanionUids =
    {
        WolfTickPatch.WolfId,
        WolfTickPatch.FoxId,
        WolfTickPatch.OwlId,
    };

    private static FieldInfo _nextEnvironmentField;
    private static PropertyInfo _envCardProperty;

    public static void ApplyPatch(Harmony harmony)
    {
        var cardBaseType = AccessTools.TypeByName("InGameCardBase");
        if (cardBaseType == null)
        {
            Plugin.Logger?.LogError("[CompanionStay] InGameCardBase type not found");
            return;
        }

        var getter = AccessTools.PropertyGetter(cardBaseType, "IndependentFromEnv");
        if (getter == null)
        {
            Plugin.Logger?.LogError("[CompanionStay] IndependentFromEnv getter not found");
            return;
        }

        harmony.Patch(getter, postfix: new HarmonyMethod(typeof(CompanionStayPatch), nameof(Postfix)));
    }

    private static void Postfix(object __instance, ref bool __result)
    {
        if (!__result) return;
        try
        {
            string uid = CardUtil.GetCardUniqueId(__instance);
            if (uid == null || Array.IndexOf(StayCapableCompanionUids, uid) < 0) return;

            float stayFlag = CardUtil.GetDurability(__instance, "SpecialDurability2");
            if (!float.IsNaN(stayFlag) && stayFlag >= 1f)
            {
                __result = false;
                return;
            }

            if (uid == WolfTickPatch.OwlId && DestinationIsIndoorOrCave())
            {
                __result = false;
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogError($"[CompanionStay] postfix error: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    /// <summary>
    /// True when GameManager.NextEnvironment's destination CardData is a known cave/mine/tunnel
    /// or man-made structure — see <see cref="IndoorOrCaveEnv"/> for the UID/tag check itself.
    /// Reflection-only (this mod's Assembly-CSharp reference is the nstrip build — see
    /// WildOwlLifecyclePatch's class doc comment for why direct typed access is avoided here).
    /// Field/property lookups are cached per CLAUDE.md performance rules.
    /// Logs at Info (not the usual per-transition Debug level) while this fix is still being
    /// verified in-game — demote to Debug once confirmed working across a real cave + a real
    /// modded building visit.
    /// </summary>
    private static bool DestinationIsIndoorOrCave()
    {
        try
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return false;

            _nextEnvironmentField ??= gm.GetType().GetField("NextEnvironment",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object envId = _nextEnvironmentField?.GetValue(gm);
            if (envId == null) return false;

            _envCardProperty ??= envId.GetType().GetProperty("EnvCard",
                BindingFlags.Instance | BindingFlags.Public);
            if (_envCardProperty?.GetValue(envId) is not UnityEngine.Object envCard || envCard == null)
            {
                Plugin.Logger?.LogInfo("[CompanionStay] Owl destination check: NextEnvironment.EnvCard is null — cannot evaluate this transition.");
                return false;
            }

            // DO NOT demote these three lines to LogDebug. They read as leftover bring-up
            // instrumentation now that T2.75 (owl biome-gated follow) is recorded PASS, and the
            // 2026-09-05 audit did recommend demoting them on exactly that reasoning - but the
            // CLAUDE.md demotion guard applies here and that adjudication was wrong. The open
            // retro `Documentation/Retrospectives/river-bridge-east-click-noop.md` names this
            // postfix under "Investigation Tooling" as a free travel tracer: it prints
            // GameManager.NextEnvironment.EnvCard at Info on EVERY transition, so any player's
            // LogOutput.log carries a complete env-to-env movement trace with nothing enabled,
            // and that is how the retro established its westbound-works/eastbound-never asymmetry.
            // At Debug it would need VerboseLogging, which player logs do not have set.
            // Re-check that retro's status before revisiting. Re-adjudicated 2026-09-07.
            bool match = IndoorOrCaveEnv.Matches(envCard);
            Plugin.Logger?.LogInfo(match
                ? $"[CompanionStay] Owl left behind — destination '{CardUtil.GetCardUniqueId(envCard)}' is a known cave/structure."
                : $"[CompanionStay] Owl destination '{CardUtil.GetCardUniqueId(envCard)}' not recognized as indoor/cave.");
            return match;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogDebug($"[CompanionStay] DestinationIsIndoorOrCave check failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            return false;
        }
    }
}
