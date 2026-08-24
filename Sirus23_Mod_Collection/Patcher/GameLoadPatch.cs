using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// Sheep husbandry vanilla-data patching: sheep butter reuses vanilla butter's
/// CardInteractions, and tame sheep gain cattle-style delayed pregnancy (cloned
/// from the vanilla cow's SpecialDurability4 + passives, retargeted to tag_Ram).
///
/// v1.1.0: runs on <see cref="FrameworkEvents.GameDataReady"/> (no local Harmony
/// patch on LoadMainGameData), with the local reflection stack — GetMember/SetMember,
/// DeepClone, CreateCollectionLike — replaced by the framework's
/// <see cref="Reflect"/> / <see cref="Collections"/> / <see cref="GameContent"/> APIs.
/// GameDataReady fires after WarpResolver, so resolved SO references are set
/// directly (CLAUDE.md §Post-WarpResolver SO References).
/// </summary>
internal static class GameLoadPatch
{
    private const string VanillaButterUid = "e2c13510dfb6fde418f280891ce797a3";
    private const string VanillaCowFemaleUid = "bbcbea777c204bf448d4c81c320288e9";
    private const string SheepButterUid = "sh_butter";
    private const string TameSheepUid = "sh_tame_sheep";
    private const string LactatingSheepUid = "sh_lactating_sheep";
    private const string MaleSheepUid = "sh_male_sheep";
    private const string LambUid = "sh_lamb";
    // tag_Ram required — wild rams must be tamed before they can breed (intentional design).
    private const string RamTag = "tag_Ram";
    private const float SheepGestationMax = 2880f;
    private const float SheepImpregnationThreshold = 96f;

    private static ManualLogSource Logger => Plugin.Logger;

    public static void Register()
    {
        FrameworkEvents.GameDataReady += OnGameDataReady;
    }

    private static void OnGameDataReady()
    {
        try
        {
            PatchSheepButterInteractions();
            PatchSheepReproduction();
        }
        catch (Exception ex)
        {
            Logger?.LogError($"[SheepHusbandry] Error: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    private static void PatchSheepButterInteractions()
    {
        var vanillaButter = CardUtil.GetCardDataById(VanillaButterUid);
        var sheepButter = CardUtil.GetCardDataById(SheepButterUid);
        if (vanillaButter == null) { Logger?.LogError("[SheepButterCompat] Vanilla MilkButter card not found"); return; }
        if (sheepButter == null) { Logger?.LogError("[SheepButterCompat] Sheep butter card not found"); return; }

        var vanillaInteractions = Reflect.GetMember(vanillaButter, "CardInteractions");
        if (vanillaInteractions == null)
        {
            Logger?.LogError("[SheepButterCompat] Vanilla MilkButter has no CardInteractions member");
            return;
        }
        if (!Reflect.SetMember(sheepButter, "CardInteractions", vanillaInteractions))
        {
            Logger?.LogError("[SheepButterCompat] Could not assign CardInteractions to sheep butter");
            return;
        }
        Logger?.Log(LogLevel.Debug, "[SheepButterCompat] Sheep butter now uses vanilla butter interactions");
    }

    private static void PatchSheepReproduction()
    {
        var vanillaCow = CardUtil.GetCardDataById(VanillaCowFemaleUid);
        var tameSheep = CardUtil.GetCardDataById(TameSheepUid);
        var maleSheep = CardUtil.GetCardDataById(MaleSheepUid);
        if (vanillaCow == null || tameSheep == null || maleSheep == null)
        {
            Logger?.LogError("[SheepReproduction] Required vanilla cow, tame sheep, or ram card not found");
            return;
        }

        var ramTagObject = FindCardTag(maleSheep, RamTag)
            ?? GameContent.Find(CardUtil.FindGameType("CardTag"), RamTag);
        if (ramTagObject == null)
        {
            Logger?.LogError("[SheepReproduction] Resolved ram tag not found");
            return;
        }

        var cowPregnancy = Reflect.GetMember(vanillaCow, "SpecialDurability4");
        if (cowPregnancy == null)
        {
            Logger?.LogError("[SheepReproduction] Vanilla cow pregnancy durability not found");
            return;
        }

        var sheepPregnancy = Reflect.DeepClone(cowPregnancy);
        ConfigureSheepPregnancyDurability(sheepPregnancy);
        Reflect.SetMember(tameSheep, "SpecialDurability4", sheepPregnancy);

        var passiveEffects = BuildSheepPregnancyPassives(vanillaCow, ramTagObject);
        if (passiveEffects.Count > 0)
            Collections.SetCollection(tameSheep, "PassiveEffects", passiveEffects);

        Logger?.Log(LogLevel.Debug, "[SheepReproduction] Tame sheep now use cattle-style delayed pregnancy");
    }

    private static void ConfigureSheepPregnancyDurability(object durability)
    {
        Reflect.SetMember(durability, "Active", true);
        Reflect.SetMember(durability, "FloatValue", 0f);
        Reflect.SetMember(durability, "MaxValue", SheepGestationMax);
        Reflect.SetMember(durability, "RatePerDaytimePoint", -1f);
        Reflect.SetMember(durability, "HidingOptions", 3);
        Reflect.SetMember(durability, "OverrideIconWarpData", "Pregnancy_Old");
        Reflect.SetMember(durability, "OverrideIconWarpType", 3);
        Reflect.SetMember(durability, "HasActionOnFull", true);
        Reflect.SetMember(durability, "RepeatActionOnFull", false);
        Reflect.SetMember(durability, "ShowPopupOnFull", false);
        Reflect.SetMember(durability, "OnFullNotification", 1);

        LocalizedStringBuilder.Populate(Reflect.GetMember(durability, "CardStatName"),
            "sh_tame_sheep_SpecialDurability4.CardStatName",
            "Pregnancy",
            TameSheepUid);

        var onFull = Reflect.GetMember(durability, "OnFull");
        if (onFull == null) return;

        LocalizedStringBuilder.Populate(Reflect.GetMember(onFull, "ActionDescription"),
            "sh_tame_sheep_SpecialDurability4.OnFull.ActionDescription",
            "Sheep has given birth!",
            TameSheepUid);

        var birthCollections = BuildBirthCollections(Reflect.GetMember(onFull, "ProducedCards"));
        if (birthCollections != null)
            Reflect.SetMember(onFull, "ProducedCards", birthCollections);

        ConfigureBirthReceivingChanges(Reflect.GetMember(onFull, "ReceivingCardChanges"));
    }

    private static List<object> BuildSheepPregnancyPassives(object vanillaCow, object ramTagObject)
    {
        var passives = new List<object>();
        var impregnate = Reflect.DeepClone(FindPassiveEffectByPrefix(vanillaCow, "Impregnate"));
        var pregnant = Reflect.DeepClone(FindPassiveEffectByPrefix(vanillaCow, "Pregnant"));

        if (impregnate != null)
        {
            Reflect.SetMember(impregnate, "EffectName", "Impregnate if Ram Nearby (pregnancy +2)");
            ConfigurePregnancyPassive(impregnate, new[] { ramTagObject }, new[] { RamTag },
                0f, SheepImpregnationThreshold, 2f);
            passives.Add(impregnate);
        }

        if (pregnant != null)
        {
            Reflect.SetMember(pregnant, "EffectName", "Pregnant (Pregnancy +2)");
            ConfigurePregnancyPassive(pregnant, Array.Empty<object>(), Array.Empty<string>(),
                SheepImpregnationThreshold + 1f, SheepGestationMax, 2f);
            passives.Add(pregnant);
        }

        return passives;
    }

    private static void ConfigurePregnancyPassive(object passiveEffect, object[] requiredTagObjects,
        string[] requiredTagsOnBoard, float rangeStart, float rangeEnd, float special4Rate)
    {
        var conditions = Reflect.GetMember(passiveEffect, "Conditions");
        if (conditions != null)
        {
            Collections.SetCollection(conditions, "RequiredTagsOnBoard", requiredTagObjects);
            Reflect.SetMember(conditions, "RequiredTagsOnBoardWarpData", requiredTagsOnBoard);
            Reflect.SetMember(conditions, "RequiredTagsOnBoardWarpType", 3);

            var ranges = Reflect.GetMember(conditions, "ReceivingRequiredDurabilityRanges");
            if (ranges != null)
            {
                SetRangeMember(ranges, "UsageRange", 0f, 0f);
                SetRangeMember(ranges, "FuelRange", 0f, 0f);
                SetRangeMember(ranges, "ProgressRange", 0f, 0f);
                SetRangeMember(ranges, "Special1Range", 0f, 0f);
                SetRangeMember(ranges, "Special2Range", 0f, 0f);
                SetRangeMember(ranges, "Special3Range", 0f, 0f);
                SetRangeMember(ranges, "Special4Range", rangeStart, rangeEnd);
            }
        }

        SetActiveFloat(Reflect.GetMember(passiveEffect, "Special4RateModifier"), true, special4Rate);
        Reflect.SetMember(passiveEffect, "MultiplySpecial4Rate", false);
    }

    private static object BuildBirthCollections(object fallbackCollections)
    {
        var sourceCollection = GetFirstItem(fallbackCollections);
        if (sourceCollection == null) return fallbackCollections;

        var birthCollection = Reflect.DeepClone(sourceCollection);
        Reflect.SetMember(birthCollection, "CollectionName", "Birth");
        Reflect.SetMember(birthCollection, "CollectionWeight", 1);

        var existingDrops = Reflect.GetMember(birthCollection, "DroppedCards");
        var templateDrop = GetFirstItem(existingDrops);
        if (templateDrop == null) return fallbackCollections;

        var lambDrop = Reflect.DeepClone(templateDrop);
        Reflect.SetMember(lambDrop, "DroppedCardWarpData", LambUid);
        Reflect.SetMember(lambDrop, "DroppedCardWarpType", 3);
        Reflect.SetMember(lambDrop, "Quantity", new Vector2Int(1, 1));
        // WarpResolver ran before GameDataReady — set the resolved reference directly.
        var lambCardData = CardUtil.GetCardDataById(LambUid);
        if (lambCardData != null)
            Reflect.SetMember(lambDrop, "DroppedCard", lambCardData);

        Reflect.SetMember(birthCollection, "DroppedCards",
            Collections.CreateLike(existingDrops, new List<object> { lambDrop }));
        return Collections.CreateLike(fallbackCollections, new[] { birthCollection });
    }

    private static void ConfigureBirthReceivingChanges(object receivingChanges)
    {
        if (receivingChanges == null) return;

        Reflect.SetMember(receivingChanges, "ModType", 1);
        Reflect.SetMember(receivingChanges, "TransformIntoWarpData", LactatingSheepUid);
        Reflect.SetMember(receivingChanges, "TransformIntoWarpType", 3);
        // WarpResolver already ran — set the resolved reference directly.
        var lactatingSheepData = CardUtil.GetCardDataById(LactatingSheepUid);
        if (lactatingSheepData != null)
            Reflect.SetMember(receivingChanges, "TransformInto", lactatingSheepData);
        Reflect.SetMember(receivingChanges, "TransferSpoilage", false);
        Reflect.SetMember(receivingChanges, "TransferUsage", false);
        Reflect.SetMember(receivingChanges, "TransferFuel", false);
        Reflect.SetMember(receivingChanges, "TransferCharges", false);
        Reflect.SetMember(receivingChanges, "TransferSpecial1", false);
        Reflect.SetMember(receivingChanges, "TransferSpecial2", false);
        Reflect.SetMember(receivingChanges, "TransferSpecial3", false);
        Reflect.SetMember(receivingChanges, "TransferSpecial4", false);
        SetRangeMember(receivingChanges, "SpoilageChange", 0f, 0f);
        SetRangeMember(receivingChanges, "UsageChange", 0f, 0f);
        SetRangeMember(receivingChanges, "FuelChange", 0f, 0f);
        SetRangeMember(receivingChanges, "ChargesChange", 0f, 0f);
        SetRangeMember(receivingChanges, "Special1Change", 0f, 0f);
        SetRangeMember(receivingChanges, "Special2Change", 0f, 0f);
        SetRangeMember(receivingChanges, "Special3Change", 0f, 0f);
        SetRangeMember(receivingChanges, "Special4Change", 0f, 0f);
    }

    // ── Small local helpers (shapes specific to this patch) ──────────────────

    private static object FindPassiveEffectByPrefix(object card, string effectNamePrefix)
    {
        if (Reflect.GetMember(card, "PassiveEffects") is not IEnumerable effects) return null;
        foreach (var passiveEffect in effects)
        {
            if (Reflect.GetMember(passiveEffect, "EffectName") is string name
                && name.StartsWith(effectNamePrefix, StringComparison.OrdinalIgnoreCase))
                return passiveEffect;
        }
        return null;
    }

    private static object FindCardTag(object card, string tagName)
    {
        if (Reflect.GetMember(card, "CardTags") is not IEnumerable tags) return null;
        foreach (var tag in tags)
            if (tag is UnityEngine.Object uo
                && string.Equals(uo.name, tagName, StringComparison.OrdinalIgnoreCase))
                return tag;
        return null;
    }

    private static object GetFirstItem(object collection)
    {
        if (collection is IEnumerable enumerable && collection is not string)
            foreach (var item in enumerable)
                return item;
        return null;
    }

    private static void SetActiveFloat(object optionalFloat, bool active, float value)
    {
        if (optionalFloat == null) return;
        Reflect.SetMember(optionalFloat, "Active", active);
        Reflect.SetMember(optionalFloat, "FloatValue", value);
    }

    private static void SetRangeMember(object instance, string memberName, float rangeMin, float rangeMax)
    {
        if (instance == null) return;

        var current = Reflect.GetMember(instance, memberName);
        if (current is Vector2)
        {
            Reflect.SetMember(instance, memberName, new Vector2(rangeMin, rangeMax));
            return;
        }
        if (current == null) return;

        // Boxed struct or class with x/y members — mutate then write back (struct copies).
        Reflect.SetMember(current, "x", rangeMin);
        Reflect.SetMember(current, "y", rangeMax);
        Reflect.SetMember(instance, memberName, current);
    }
}
