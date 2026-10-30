using System;
using System.Globalization;
using BepInEx.Logging;
using CSFFModFramework.Api;
using HarmonyLib;

namespace Advanced_Copper_Tools.Patcher
{
    /// <summary>
    /// The Large Saw keeps the smithing quality of the metal sheets and nails it was built from (ACT 1.18.15, fleet plan
    /// 6.3: the first consumer of the framework's per-card saved values, <c>CSFFModFramework.Api.CardState</c>, owner
    /// decision K5).
    ///
    /// <para><b>Why this one.</b> Framework 2.29.0 gave the framework Metal Sheet and Metal Nail a smithing Quality
    /// (Special3, raised by striking a heated part with a hard hammer, fleet plan 5.1). The Large Saw is made from
    /// three sheets and two nails, but its own eight stats are all taken (Special1 Dullness, Special3 Oil Coat because
    /// the framework's InheritMetal smelt rule reads Special2 as Metal Quality, Special4 Metal Type; W0.7 "As built"),
    /// so the parts' quality had nowhere to go: Additional Processing Equipment writes a Quality onto its saw, ACT's
    /// merge dropped it. A card value is the ninth slot. It is the plan's own example ("metal origin or heat history on
    /// the metal parts, carried through to the saw") and the smallest consumer that exercises all three paths: a seed
    /// read from the ingredients' durability, the blueprint merge, and the save.</para>
    ///
    /// <para><b>What it does.</b> One name, <see cref="PartQuality"/>: averaged over every framework sheet and nail
    /// consumed by the Large Saw recipe (a part that already holds the value gives that, any other gives its Special3),
    /// stored on the saw only, kept through a transform, and shown as one line under the saw's description. It changes
    /// no stat: the saw fells and wears exactly as before. A saw built before this version, or from parts of another
    /// mod that carry no Quality, has no value and shows no line.</para>
    ///
    /// <para>Declarations are made once, in <c>RegisterPatches</c> (plugin Awake); <c>CardState.DeclareCarry</c> is
    /// idempotent anyway. The description line is a postfix on <c>InGameCardBase.CardDescription</c>, which returns a
    /// fresh string on every call: nothing shared is mutated, so no capture or restore is needed.</para>
    /// </summary>
    public static class LargeSawPartQualityPatch
    {
        /// <summary>The card value: the mean smithing Quality (0-100) of the sheets and nails the saw was built from.</summary>
        public const string PartQuality = "act.large_saw.part_quality";

        internal const string LargeSawUid = "advanced_copper_tools_large_saw";
        internal const string MetalSheetUid = "advanced_copper_tools_metal_sheet";
        internal const string MetalNailUid = "advanced_copper_tools_copper_nails";
        internal const string LineKey = "Advanced_Copper_Tools_LargeSaw_PartQualityLine";
        internal const string LineDefault = "Its sheets and nails were smithed to quality {0}.";

        private static ManualLogSource Logger => Plugin.Logger;
        private static bool _lineWarned;

        public static void ApplyPatch(Harmony harmony)
        {
            bool carry = CardState.DeclareCarry(PartQuality, true, CardStateMerge.Average, LargeSawUid);
            bool seed = CardState.DeclareBlueprintSeed(PartQuality, "SpecialDurability3", MetalSheetUid, MetalNailUid);
            if (!carry || !seed)
                Logger?.LogWarning("[LargeSawPartQuality] the framework refused the card value declaration; the Large Saw does not keep its parts' quality.");

            var description = AccessTools.Method(typeof(InGameCardBase), nameof(InGameCardBase.CardDescription), new[] { typeof(bool) });
            if (description == null)
            {
                Logger?.LogWarning("[LargeSawPartQuality] InGameCardBase.CardDescription(bool) not found; the saw keeps its parts' quality but does not show it.");
                return;
            }
            harmony.Patch(description, postfix: new HarmonyMethod(typeof(LargeSawPartQualityPatch), nameof(CardDescription_Postfix)));
        }

        private static void CardDescription_Postfix(InGameCardBase __instance, bool _IgnoreLiquid, ref string __result)
        {
            try
            {
                if (!__instance || __instance.CardModel == null || __instance.CardModel.UniqueID != LargeSawUid) return;
                float q = CardState.Get(__instance, PartQuality);
                if (float.IsNaN(q)) return;
                __result = AppendLine(__result, FormatLine(Localize(), q));
            }
            catch (Exception ex)
            {
                if (_lineWarned) return;
                _lineWarned = true;
                Logger?.LogWarning($"[LargeSawPartQuality] the description line threw; the saw's description shows no quality line: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static string Localize()
        {
            // A fresh LocalizedString per call: ToString caches the looked-up text in the instance, so a kept one would
            // stay in the first language it was read in.
            var text = new LocalizedString { LocalizationKey = LineKey, DefaultText = LineDefault }.ToString();
            return string.IsNullOrEmpty(text) || text.IndexOf("{0}", StringComparison.Ordinal) < 0 ? LineDefault : text;
        }

        /// <summary>The line for a quality: rounded to a whole number, as the game shows durabilities.</summary>
        internal static string FormatLine(string template, float quality)
            => string.Format(CultureInfo.InvariantCulture, template, Math.Round(quality, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture));

        internal static string AppendLine(string description, string line)
            => string.IsNullOrEmpty(description) ? line : description + "\n" + line;
    }
}
