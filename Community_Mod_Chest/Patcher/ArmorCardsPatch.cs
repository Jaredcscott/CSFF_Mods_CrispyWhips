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
        "cmcwoolquiltedcap",
        "cmcwoolquiltedvest",
        "combatandarmorbonelamellar",
        "combatandarmorwoodenshield",
        "apparelchaperon",
        "apparelclothcoat",
        "apparelfootwraps",
        "apparelhandwraps",
        "apparelleatherapron",
        "apparelleathersandals",
        "apparelstrawhat",
        // Reed Hand Wraps (Nexus c174134808) and the dyed variants and Long Johns, which carry
        // ArmorValues but were never listed, so their armour did nothing in combat.
        "apparelreedhandwraps",
        "apparellongjohns",
        "apparelchaperondyed",
        "apparelchaperondyedbillberry",
        "apparelchaperondyedochre",
        "apparelclothcoatdyed",
        "apparelclothcoatdyedbillberry",
        "apparelclothcoatdyedochre",
        "apparelleatheraprondyedbillberry",
        "apparelleatheraprondyedcharcoal",
        "apparelleatheraprondyedochre",
        "apparelstrawhatdyedbillberry",
        "apparelstrawhatdyedcharcoal",
        "apparelstrawhatdyedochre",
        "combatandarmorquiltedcapdyedbillberry",
        "combatandarmorquiltedcapdyedcharcoal",
        "combatandarmorquiltedcapdyedochre",
    };

    // One latch per cause (root CLAUDE.md Silent Catch Blocks, gear G5). Reflect.GetMember returns null on a
    // renamed member rather than throwing, so every null exit below names its member once: a game update that
    // renames GM, ArmorCards, AllCards, CharacterWindow, EquipmentSlotsLine or Slots would otherwise switch
    // off every CMC armor piece in combat with nothing in the log.
    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause)) Logger?.LogWarning("[ArmorCards] " + message + " Logged once.");
    }

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
            if (gameManager == null)
            {
                WarnOnce("gm-null", "EncounterPopup.GM read null and no GameManager instance was found; CMC armor is not registered for this wound roll.");
                return;
            }

            if (Reflect.GetMember(gameManager, "ArmorCards") is not IList armorCards)
            {
                WarnOnce("armorcards-unreadable", "GameManager.ArmorCards is missing or is not a list; CMC armor gives no protection in combat.");
                return;
            }

            if (Reflect.GetMember(gameManager, "AllCards") is not IEnumerable allCards)
            {
                WarnOnce("allcards-unreadable", "GameManager.AllCards is missing or is not enumerable; only equipped CMC armor is registered for combat.");
                allCards = null;
            }
            int added = AddFromCards(allCards, armorCards);

            var graphicsManager = Reflect.GetMember(__instance, "GraphicsManager")
                               ?? Reflect.GetMember(gameManager, "GameGraphics", "GraphicsManager");
            if (graphicsManager == null)
                WarnOnce("graphicsmanager-null", "EncounterPopup.GraphicsManager and GameManager.GameGraphics both read null; equipped CMC armor is found only through the card list.");
            var characterWindow = Reflect.GetMember(graphicsManager, "CharacterWindow");
            if (graphicsManager != null && characterWindow == null)
                WarnOnce("characterwindow-null", "GraphicsManager.CharacterWindow read null; equipped CMC armor is found only through the card list.");
            added += AddEquipped(characterWindow, armorCards);

            if (added > 0)
                Logger?.LogDebug($"[ArmorCards] {added} armor card(s) registered for combat.");
        }
        catch (Exception ex)
        {
            // Keyed by exception type, so a second, different failure is not hidden behind the first.
            if (_warned.Add("exception:" + (ex.InnerException ?? ex).GetType().Name))
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
        if (characterWindow == null) return 0; // the caller has already said why
        var equipmentLine = Reflect.GetMember(characterWindow, "EquipmentSlotsLine");
        if (equipmentLine == null)
        {
            WarnOnce("equipmentslotsline-null", "CharacterWindow.EquipmentSlotsLine read null; equipped CMC armor is found only through the card list.");
            return 0;
        }
        if (Reflect.GetMember(equipmentLine, "Slots") is not IEnumerable slots)
        {
            WarnOnce("slots-unreadable", "EquipmentSlotsLine.Slots is missing or is not enumerable; equipped CMC armor is found only through the card list.");
            return 0;
        }

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
