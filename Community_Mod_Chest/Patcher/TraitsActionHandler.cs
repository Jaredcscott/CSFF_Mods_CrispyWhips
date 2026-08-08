using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// ActionRouter handlers for consumption-triggered character traits:
    ///
    ///   LactoseIntolerant — detects dairy consumption via SaturationDairy stat delta.
    ///                       Applies Nausea+40, Pain+10 when a dairy product is eaten/drunk.
    ///
    ///   Drunkard          — detects alcohol consumption via AlcoholEffect stat delta.
    ///                       Doubles the AlcoholEffect change and adds Stress+15.
    ///                       (Sobriety craving is handled in TraitsTickHandler.)
    ///
    /// Both use AfterWrapped timing: Before captures baseline stats → After computes deltas
    /// and applies extra effects. Triggers fire only on Eat/Drink actions on any card.
    ///
    /// Note: SaturationDairy changes when the consumed item has a StatModification for
    /// that stat. Vanilla has no dairy items; this perk becomes active with SheepHusbandry
    /// or other mods that add dairy-tagged food.
    /// </summary>
    internal static class TraitsActionHandler
    {
        // ── Perk UIDs ───────────────────────────────────────────────────────────
        private const string LactosePerkUid  = "traitsperklactoseintolerant";
        private const string DrunkardPerkUid = "traitsperkdrunkard";

        // ── Stat GUIDs — resolved via VanillaIds (all five are vanilla stat names) ─
        private static readonly string GuidNausea          = VanillaIds.GetStat("Nausea");
        private static readonly string GuidPain            = VanillaIds.GetStat("Pain");
        private static readonly string GuidStress          = VanillaIds.GetStat("Stress");
        private static readonly string GuidAlcoholEffect   = VanillaIds.GetStat("AlcoholEffect");
        private static readonly string GuidSaturationDairy = VanillaIds.GetStat("SaturationDairy");

        // ── State ───────────────────────────────────────────────────────────────
        private static bool _initialized;
        // Stat objects cached after first use
        private static object _statNausea;
        private static object _statPain;
        private static object _statStress;
        private static object _statAlcohol;
        private static object _statDairy;
        private static bool   _statsCached;
        // Perk detection reuses TraitsTickHandler infrastructure via reflection
        private static FieldInfo _allPerksField;

        // Tag passed in context.Tag between Before and After
        private sealed class Snapshot
        {
            public float Dairy;
            public float Alcohol;
        }

        // ── Registration ────────────────────────────────────────────────────────

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (!ResolveReflection())
            {
                Plugin.Logger.LogWarning("[TraitsActionHandler] reflection init failed — LactoseIntolerant/Drunkard inactive.");
                return;
            }

            // One handler covers both "Eat" and "Drink" by action-name predicate in
            // CardPredicate (CardUid or CardPredicate required by ActionRouter).
            ActionRouter.Register(new ActionHandler
            {
                Name          = "TraitsConsumption",
                CardPredicate = IsEatOrDrinkAction,
                Timing        = ActionTiming.AfterWrapped,
                Before        = ConsumptionBefore,
                After         = ConsumptionAfter,
            });

            Plugin.Logger.LogDebug("[TraitsActionHandler] LactoseIntolerant/Drunkard consumption handlers active.");
        }

        // ── Predicate ───────────────────────────────────────────────────────────

        // CardPredicate fires once per action dispatch. We repurpose it as an action-name
        // filter: only intercept Eat and Drink actions (case-insensitive prefix match).
        private static bool IsEatOrDrinkAction(ActionContext ctx)
        {
            var name = ctx.ActionName;
            if (name == null) return false;
            return name.StartsWith("Eat",   StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Drink", StringComparison.OrdinalIgnoreCase);
        }

        // ── Before / After ──────────────────────────────────────────────────────

        private static bool ConsumptionBefore(ActionContext ctx)
        {
            EnsureStatsCached();
            ctx.Tag = new Snapshot
            {
                Dairy   = StatAccess.GetCurrentValue(_statDairy),
                Alcohol = StatAccess.GetCurrentValue(_statAlcohol),
            };
            return false; // Before returning false = don't suppress (AfterWrapped ignores return value)
        }

        private static void ConsumptionAfter(ActionContext ctx)
        {
            if (ctx.Tag is not Snapshot snap) return;

            float dairyDelta   = StatAccess.GetCurrentValue(_statDairy)   - snap.Dairy;
            float alcoholDelta = StatAccess.GetCurrentValue(_statAlcohol) - snap.Alcohol;

            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return;

            if (dairyDelta > 0f && HasPerk(gm, LactosePerkUid))
            {
                // Dairy consumed — apply Nausea+40, Pain+10
                StatAccess.ModifyCurrentValue(_statNausea, +40f);
                StatAccess.ModifyCurrentValue(_statPain,   +10f);
            }

            if (alcoholDelta > 0f && HasPerk(gm, DrunkardPerkUid))
            {
                // Alcohol consumed — double the intoxication delta, add Stress+15
                StatAccess.ModifyCurrentValue(_statAlcohol, alcoholDelta); // +1× extra = 2× total
                StatAccess.ModifyCurrentValue(_statStress,  +15f);
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        // Calls UniqueIDScriptable.GetFromID<GameStat>(guid) via the generic method.
        // Legitimate direct call (not hand-rolled CurrentValue/MaxValue reflection) — kept as-is.
        private static object GetStatById(string guid)
        {
            try
            {
                var uidType = AccessTools.TypeByName("UniqueIDScriptable");
                if (uidType == null) return null;
                var getFromId = System.Linq.Enumerable
                    .FirstOrDefault(uidType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
                                    m => m.Name == "GetFromID" && m.IsGenericMethodDefinition);
                if (getFromId == null) return null;
                var gameStat = AccessTools.TypeByName("GameStat");
                if (gameStat == null) return null;
                return getFromId.MakeGenericMethod(gameStat).Invoke(null, new object[] { guid });
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[TraitsActionHandler] GetStatById reflection failed for guid={guid}: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return null;
            }
        }

        private static void EnsureStatsCached()
        {
            if (_statsCached) return;
            _statsCached   = true;
            _statNausea    = GetStatById(GuidNausea);
            _statPain      = GetStatById(GuidPain);
            _statStress    = GetStatById(GuidStress);
            _statAlcohol   = GetStatById(GuidAlcoholEffect);
            _statDairy     = GetStatById(GuidSaturationDairy);
        }

        private static bool HasPerk(object gm, string uid)
        {
            var perks = _allPerksField?.GetValue(gm) as IEnumerable;
            if (perks == null) return false;
            foreach (var perk in perks)
                if (perk is UniqueIDScriptable u && uid.Equals(u.UniqueID, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static bool ResolveReflection()
        {
            var asm    = AppDomain.CurrentDomain.GetAssemblies()
                             .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            var gmType = asm?.GetType("GameManager");
            if (gmType == null) { Plugin.Logger.LogWarning("[TraitsActionHandler] GameManager type not found"); return false; }
            _allPerksField = AccessTools.Field(gmType, "AllPerks")
                ?? gmType.GetField("<AllPerks>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_allPerksField == null) { Plugin.Logger.LogWarning("[TraitsActionHandler] GameManager.AllPerks field not found"); return false; }
            return true;
        }
    }
}
