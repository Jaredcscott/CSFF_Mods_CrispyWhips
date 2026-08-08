using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher;

/// <summary>
/// Registers CMC armor cards in GameManager.ArmorCards before each combat wound roll.
///
/// ArmorValues + equipped-only PassiveEffects cover the STATS screen, but encounter
/// damage reduction only consults GameManager.ArmorCards — a list vanilla populates
/// for its own armor at load time. Modded armor never gets added, so without this
/// repair the items display protection values that do nothing in combat.
/// Same fix as ACT copper armor (GameLoadPatch.RepairCopperArmorCards).
/// </summary>
public static class ArmorCardsPatch
{
    private static ManualLogSource Logger => Plugin.Logger;

    private static readonly HashSet<string> ArmorUids = new(StringComparer.Ordinal)
    {
        "combatandarmorquiltedcap",
        "combatandarmorquiltedvest",
        "combatandarmorbonelamellar",
        "combatandarmorwoodenshield",
        "apparelchaperon",
        "apparelclothcoat",
        "apparelfootwraps",
        "apparelhandwraps",
        "apparelleatherapron",
        "apparelleathersandals",
        "apparelstrawhat",
    };

    private static bool _loggedError;

    public static void ApplyPatch(Harmony harmony)
    {
        var encounterPopupType = AccessTools.TypeByName("EncounterPopup");
        if (encounterPopupType == null)
        {
            Logger?.LogError("[ArmorCards] EncounterPopup type not found; armor combat repair not applied.");
            return;
        }

        var method = AccessTools.Method(encounterPopupType, "GenerateAndApplyPlayerWound");
        if (method == null)
        {
            Logger?.LogError("[ArmorCards] EncounterPopup.GenerateAndApplyPlayerWound not found; armor combat repair not applied.");
            return;
        }

        harmony.Patch(method, prefix: new HarmonyMethod(typeof(ArmorCardsPatch), nameof(Encounter_Prefix)));
    }

    private static void Encounter_Prefix(object __instance)
    {
        try
        {
            var gameManager = Reflect.GetMember(__instance, "GM") ?? CardUtil.GetGameManagerInstance();
            if (gameManager == null) return;

            if (Reflect.GetMember(gameManager, "ArmorCards") is not IList armorCards) return;

            int added = AddFromCards(Reflect.GetMember(gameManager, "AllCards") as IEnumerable, armorCards);

            var graphicsManager = Reflect.GetMember(__instance, "GraphicsManager")
                               ?? Reflect.GetMember(gameManager, "GameGraphics", "GraphicsManager");
            var characterWindow = Reflect.GetMember(graphicsManager, "CharacterWindow");
            added += AddEquipped(characterWindow, armorCards);

            if (added > 0)
                Logger?.LogDebug($"[ArmorCards] {added} armor card(s) registered for combat.");
        }
        catch (Exception ex)
        {
            if (_loggedError) return;
            _loggedError = true;
            Logger?.LogError($"[ArmorCards] combat repair failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    private static int AddFromCards(IEnumerable cards, IList armorCards)
    {
        if (cards == null) return 0;
        int added = 0;
        foreach (var card in cards)
        {
            if (card == null) continue;
            if (!ArmorUids.Contains(CardUtil.GetCardUniqueId(card))) continue;
            if (armorCards.Contains(card)) continue;
            armorCards.Add(card);
            added++;
        }
        return added;
    }

    private static int AddEquipped(object characterWindow, IList armorCards)
    {
        var equipmentLine = Reflect.GetMember(characterWindow, "EquipmentSlotsLine");
        if (Reflect.GetMember(equipmentLine, "Slots") is not IEnumerable slots) return 0;

        int added = 0;
        foreach (var slot in slots)
        {
            var card = Reflect.GetMember(slot, "AssignedCard");
            if (card == null) continue;
            if (!ArmorUids.Contains(CardUtil.GetCardUniqueId(card))) continue;
            if (armorCards.Contains(card)) continue;
            armorCards.Add(card);
            added++;
        }
        return added;
    }
}
