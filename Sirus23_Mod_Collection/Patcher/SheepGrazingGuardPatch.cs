using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Util;
using HarmonyLib;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// Keeps vanilla's "Grazing Animals (+1 Happines)" remote passive off the mod's sheep.
///
/// Every vanilla clearing and grove (97 cards in the EA 0.68b export, all identical) carries a
/// RemotePassiveEffects entry of that name: UsageRateModifier +1, additive, no other field. Its
/// AppliesTo is a list of vanilla animal tags, and the Sirus sheep carry the vanilla
/// tag_LargeAnimal (their only vanilla tag; tag_Sheep is the mod's own), so the clearing hands the
/// effect to every sheep on its board through GameManager.AddRemotePassiveEffects and
/// InGameCardBase.CheckForRemoteEffects, both of which end in AddExternalPassiveEffect. The
/// effect's Special3 range (20 or more) is NOT a test on the sheep: the effect is instantiated
/// with the clearing as its ConditionsCard, so the range reads the clearing's own Foraging stat.
///
/// Vanilla grazers (cows, boars, chickens, partridges) keep Happiness in Usage, so the +1 is
/// what it says. The Lactating Sheep keeps MILK in Usage (0.0091 a point), so on a clearing its
/// milk rate became 1.0091 a point, and the Tame Sheep, Ram, Lamb and both wild sheep have no
/// Usage stat at all. None of the mod's sheep has a Happiness stat in any slot, so there is no
/// other slot where the +1 belongs, and refusing it takes nothing from them.
///
/// Prefix on AddExternalPassiveEffect: refuses the add when the receiving card is one of the
/// sheep and the effect name starts with "Grazing Animals". Vanilla data is untouched, so vanilla
/// animals still get their grazing Happiness. The receiving card is read typed and tested first
/// (audit row R6, 2026-10-12); the effect name is still read by reflection, and only for a sheep.
/// </summary>
internal static class SheepGrazingGuardPatch
{
    private const string GrazingEffectPrefix = "Grazing Animals";

    private static readonly HashSet<string> SheepUids = new(StringComparer.Ordinal)
    {
        "sh_lactating_sheep",
        "sh_tame_sheep",
        "sh_male_sheep",
        "sh_lamb",
        "sh_wild_sheep",
        "sh_wild_male_sheep",
    };

    private static FieldInfo _effectNameField;
    private static bool _loggedFirstBlock;
    private static bool _warnedNameField;

    public static void ApplyPatch(Harmony harmony)
    {
        var cardBaseType = AccessTools.TypeByName("InGameCardBase");
        if (cardBaseType == null)
        {
            Plugin.Logger?.LogError("[SheepGrazingGuard] InGameCardBase type not found");
            return;
        }

        var method = cardBaseType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "AddExternalPassiveEffect" && m.GetParameters().Length == 2);
        if (method == null)
        {
            Plugin.Logger?.LogError("[SheepGrazingGuard] AddExternalPassiveEffect(PassiveEffect, InGameCardBase) not found");
            return;
        }

        var effectType = method.GetParameters()[0].ParameterType;
        _effectNameField = effectType.GetField("EffectName", BindingFlags.Instance | BindingFlags.Public);
        if (_effectNameField == null)
        {
            Plugin.Logger?.LogError($"[SheepGrazingGuard] {effectType.Name}.EffectName not found; guard not applied");
            return;
        }

        harmony.Patch(method, prefix: new HarmonyMethod(typeof(SheepGrazingGuardPatch), nameof(Prefix)));
    }

    // __0 is the PassiveEffect (a struct, boxed here), __1 the conditions card (the clearing).
    // Clearings hand their remote effects to every card on the board, so the receiving card's UID
    // (a typed read and one hash lookup) is tested first; only a sheep pays for the reflective
    // read of the effect's name.
    private static bool Prefix(object __instance, object __0)
    {
        try
        {
            if (__0 == null || __instance is not InGameCardBase card) return true;
            var model = card.CardModel;
            if (model is null) return true;
            string uid = model.UniqueID;
            if (uid == null || !SheepUids.Contains(uid)) return true;

            if (_effectNameField.GetValue(__0) is not string name)
            {
                if (!_warnedNameField)
                {
                    _warnedNameField = true;
                    Plugin.Logger?.LogWarning("[SheepGrazingGuard] an external passive effect has no readable EffectName; not filtered");
                }
                return true;
            }
            if (!name.StartsWith(GrazingEffectPrefix, StringComparison.Ordinal)) return true;

            if (!_loggedFirstBlock)
            {
                _loggedFirstBlock = true;
                Plugin.Logger?.LogInfo($"[SheepGrazingGuard] refused vanilla '{name}' on {uid} (its Usage is milk or unused, not Happiness); later refusals are silent.");
            }
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogError($"[SheepGrazingGuard] prefix error: {ex.InnerException?.ToString() ?? ex.ToString()}");
            return true;
        }
    }
}
