using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// Sheep husbandry vanilla-data patching: sheep butter (fresh, salted and clarified)
/// reuses vanilla butter's CardInteractions and is added to the vanilla raw-filling CIs that
/// already accept vanilla butter, and tame sheep gain cattle-style delayed pregnancy (cloned
/// from the vanilla cow's SpecialDurability4 + passives, retargeted to tag_Ram). The vanilla
/// animal trader's two stock actions also gain a tame ewe and a ram drop.
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
    // Vanilla "Yarn" (YarnFiber). Required by the six cloth blueprints on the Weighted Loom and
    // the Treadle Loom; sheep wool yarn is added to those slots as an alternate ingredient.
    private const string VanillaYarnUid = "7adecfbfd3ccd8c4aa34cd61d8c98076";
    private const string WoolYarnUid = "sh_yarn";
    // Vanilla "Fat". Lanolin stands in for it in SIX named vanilla recipes only (plan section 8): the
    // salves, the candle and the soap. Never in the other Fat recipes, which include Bp_Fatchunk (Fat
    // into a Fat Chunk that cuts back to Fat: an alternate there would turn lanolin into Fat) and
    // Bp_Dynamite, so the call is the framework's allow-list form. Each GUID below is the UniqueID of
    // the export file named beside it (UniqueIDScriptableJsonDataWithWarpLitAllInOne/CardData), read
    // from the file 2026-10-03; every one carries exactly one Fat slot.
    private const string VanillaFatUid = "0eb3634f696991b4d97b91626270e035";
    private const string LanolinUid = "sh_lanolin";
    // Feature 40: the Lanolin Salve alone takes these Herbs and Fungi herbs in place of Frostleaf Powder.
    private const string VanillaFrostleafPowderUid = "29bd2a2312e1acb428d25481352186bc";
    private const string LanolinSalveBlueprintUid = "sh_bp_lanolin_salve";
    private static readonly string[] SalveHerbAlternateUids = { "herbs_fungi_yarrow_ground", "herbs_fungi_chamomile_dried" };
    private static readonly string[] LanolinFatBlueprintUids =
    {
        "8e1d35a402a2fb5439dde3e7aa1ccf82", // Bp_HealingSalve
        "22a032e93bdadfc46966fe02a3282456", // Bp_PineSalve
        "03c3c9d5670cc85419c99708da8aedd1", // Bp_SoothingSalve
        "82f1b8259d11ea24495c7dd22c1342a0", // Bp_BugRepellentSalve
        "f1c5253873c055d4592fb3c1dba631a1", // Bp_Candle
        "2eb595a65e1a00f46878793c1db819ee", // Bp_Soap
    };
    private const string SheepButterUid = "sh_butter";
    private const string SaltedButterUid = "sh_salted_butter";
    private const string ClarifiedButterUid = "sh_clarified_butter";
    // Vanilla cards that already list MilkButter as a CardInteractions trigger (export walk
    // 2026-09-30): BloodCurd, FishMeatFattyRaw, FishMeatLeanRaw and MeatMincedRaw take it as a
    // raw-filling ingredient. The CARD is named here, the list inside it is found by what it holds,
    // never by index. The three cooking containers' "Melt Butter" recipe is a separate pass
    // (PatchSheepButterMelt), because it melts butter by raising a Liquidity bar the sheep butters
    // now carry in their own JSON.
    private static readonly string[] ButterTriggerHostUids =
    {
        "3da4350265ffc644d80910ffc4aa7fd0", // BloodCurd
        "3103c059d34608a4bb95c426d9ac77e0", // FishMeatFattyRaw
        "272fe32c20eebef4d82290c7a31ac885", // FishMeatLeanRaw
        "8942f341438ddd54a9f8ffc0765adf7b", // MeatMincedRaw
    };
    private const int ExpectedButterHookLists = 4;
    // Vanilla Agent_TraderAnimal, the travelling animal trader at Green Glade. Its two stock actions
    // are found by ActionID, never by index (read 2026-10-01 and again 2026-10-02: nine actions, the
    // stock ones at indices 2 and 6). The Autumn ID keeps vanilla's own spelling "arival".
    private const string AnimalTraderUid = "914f211bd854bd249810f9983ff168ed";
    private static readonly string[] TraderStockActionIds =
    {
        "Prepare items for Arrive at Green Glade Spring_1",
        "Prepare regular for arival at Green Glade Autumn_1",
    };
    // CardDropChance.BaseDropChance is on a 0 to 100 scale (CurrentDropChance returns 100 when Active
    // is false). Plan open question 4: one ewe and one ram per visit, each on its own 50 percent roll.
    // A tunable placeholder, not a final-tuned number.
    private const float TraderSheepDropChance = 50f;
    // Feature 29: the trader also stocks the Ferret, on a rarer roll (a placeholder until Sirus reviews).
    private const float TraderFerretDropChance = 25f;
    private const string FerretUid = "sir_ferret";
    private const string TameSheepUid = "sh_tame_sheep";
    private const string LactatingSheepUid = "sh_lactating_sheep";
    private const string MaleSheepUid = "sh_male_sheep";
    private const string LambUid = "sh_lamb";
    // The two pregnancy effects this class owns; PatchSheepReproduction keeps every OTHER effect.
    private const string ImpregnateEffectPrefix = "Impregnate if Ram Nearby";
    private const string PregnantEffectPrefix = "Pregnant (Pregnancy +2)";
    // tag_Ram required — wild rams must be tamed before they can breed (intentional design).
    private const string RamTag = "tag_Ram";
    private const float SheepGestationMax = 2880f;
    private const float SheepImpregnationThreshold = 96f;
    // v1.22.0 twin lambs: the birth OnFull rolls one of two weighted collections, so twins come
    // in TwinBirthWeight / (SingleBirthWeight + TwinBirthWeight) = 3 / 20 = 15% of births.
    private const int SingleBirthWeight = 17;
    private const int TwinBirthWeight = 3;

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
            PatchWoolYarnAtLoom();
            // Catches its own exceptions (including a framework that predates the call it makes), so a
            // throw here cannot skip the passes below.
            PatchLanolinAtFatBlueprints();
            // Catches its own exceptions, so a throw here cannot skip the pass below.
            PatchSalveHerbAlternates();
            // Catches its own exceptions, so a throw here cannot skip the pass below.
            OwletNestPatch.Apply();
            // Catches its own exceptions, so a throw here cannot skip the pass below.
            PatchAnimalTraderStock();
            // Catches its own exceptions, so a throw here cannot skip the pass below.
            FerretTrapPatch.Apply();
            // Catches its own exceptions, so a throw here cannot skip the pass below.
            PatchSheepButterMelt();
            // Last, so nothing above is skipped if the vanilla lists ever throw.
            PatchSheepButterAtVanillaHooks();
        }
        catch (Exception ex)
        {
            Logger?.LogError($"[SheepHusbandry] Error: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    private static void PatchSheepButterInteractions()
    {
        var vanillaButter = CardUtil.GetCardDataById(VanillaButterUid);
        if (vanillaButter == null)
        {
            Logger?.LogError("[SheepButterCompat] Vanilla MilkButter card not found; no sheep butter card gains its cooking interactions");
            return;
        }

        // APPEND vanilla butter's interactions (its three "Add" cooking CIs) after each sheep butter
        // card's own. Until 1.21.7 this ASSIGNED vanilla's array over sh_butter's, which deleted every CI
        // Butter.json authors (Clarify, Butter the Roots, Salt the Butter) at every load and left the two
        // cards sharing one array: r39 harness T2.125 and T2.202 found no "Butter the Roots" dish and no
        // salted butter. AppendAll builds a new array (own first, then vanilla's) and skips anything
        // already present by reference, so a second GameDataReady adds nothing. Vanilla is only read.
        if (!(Reflect.GetMember(vanillaButter, "CardInteractions") is IEnumerable vanillaInteractions))
        {
            Logger?.LogError("[SheepButterCompat] Vanilla MilkButter has no CardInteractions member; no sheep butter card gains its cooking interactions");
            return;
        }

        // The preserved butters (v1.22.0) ship no CardInteractions of their own, so without this
        // they could not go into dough the way the fresh butter they were made from does.
        AppendButterInteractions(SheepButterUid, "Sheep butter", vanillaInteractions);
        AppendButterInteractions(SaltedButterUid, "Salted butter", vanillaInteractions);
        AppendButterInteractions(ClarifiedButterUid, "Clarified butter", vanillaInteractions);
    }

    // One card's share of PatchSheepButterInteractions. Each null exit names its own cause, because
    // the caller cannot tell a missing card from a card that already holds every interaction.
    private static void AppendButterInteractions(string cardUid, string label, IEnumerable vanillaInteractions)
    {
        var card = CardUtil.GetCardDataById(cardUid);
        if (card == null)
        {
            Logger?.LogError($"[SheepButterCompat] {label} card '{cardUid}' not found; it will not offer vanilla butter's cooking interactions");
            return;
        }

        int added = Collections.AppendAll(card, "CardInteractions", vanillaInteractions);
        int total = Reflect.GetMember(card, "CardInteractions") is ICollection all ? all.Count : -1;
        Logger?.Log(LogLevel.Debug, $"[SheepButterCompat] {label} keeps its own interactions and gains {added} of vanilla butter's ({total} total)");
    }

    // v1.22.0 (round-2 plan feature 7, raw-filling half): the three sheep butters go wherever vanilla
    // butter is already accepted as the OTHER card of a raw-filling CI on four vanilla foods (butter
    // dropped onto minced meat, fish or blood curd). Those CIs destroy the butter and transform the
    // food from its OWN stats, so a sheep butter's missing bars change nothing. The host cards are
    // named by UID, but each list inside is found by what it holds (it contains vanilla butter), never
    // by index, so a game update that reorders a card's CIs cannot move the edit onto the wrong one.
    // Vanilla data is touched only to append this mod's cards; nothing is initialised or rebuilt.
    //
    // Re-sync (W0-4): CardInteractionTrigger.TriggerCards is read live on every drag, so a plain
    // append is enough. The trigger is a STRUCT, so the grown copy must be written back to its
    // CardOnCardAction. A repeated GameDataReady appends nothing: a card already in a list is skipped.
    private static void PatchSheepButterAtVanillaHooks()
    {
        var vanillaButter = CardUtil.GetCardDataById(VanillaButterUid) as CardData;
        if (vanillaButter == null)
        {
            Logger?.LogWarning("[SheepButterHooks] Vanilla MilkButter card not found; sheep butter will not go onto minced meat, fish or blood curd");
            return;
        }

        var sheepButters = new List<CardData>(3);
        foreach (var uid in new[] { SheepButterUid, SaltedButterUid, ClarifiedButterUid })
        {
            if (CardUtil.GetCardDataById(uid) is CardData butter) sheepButters.Add(butter);
            else Logger?.LogWarning($"[SheepButterHooks] Sheep butter card '{uid}' not found; it will not be added to vanilla butter's lists");
        }
        if (sheepButters.Count == 0)
        {
            Logger?.LogWarning("[SheepButterHooks] No sheep butter card found; vanilla butter's lists are unchanged");
            return;
        }

        int lists = 0, added = 0;

        foreach (var hostUid in ButterTriggerHostUids)
        {
            if (!(CardUtil.GetCardDataById(hostUid) is CardData host))
            {
                Logger?.LogWarning($"[SheepButterHooks] Vanilla card '{hostUid}' not found; its butter interaction is unchanged");
                continue;
            }
            AppendToButterInteractions(host, vanillaButter, sheepButters, ref lists, ref added);
        }

        // A silent zero is this feature's failure mode: a game update that moved butter out of these
        // lists, or renamed these cards, would leave every sheep butter unable to make a raw filling.
        if (lists == 0)
        {
            Logger?.LogWarning("[SheepButterHooks] No vanilla list holding butter was found; sheep butter will not go onto minced meat, fish or blood curd");
            return;
        }

        Logger?.Log(LogLevel.Debug, $"[SheepButterHooks] {lists} vanilla butter list(s) accept the sheep butters ({ExpectedButterHookLists} expected on 2026-09-30); {added} card(s) added this pass");
    }

    // The raw-filling CI: butter is a TRIGGER card of CardInteractions[n].CompatibleCards.
    private static void AppendToButterInteractions(CardData host, CardData vanillaButter,
        List<CardData> sheepButters, ref int lists, ref int added)
    {
        var interactions = host.CardInteractions;
        if (interactions == null) return;

        foreach (var interaction in interactions)
        {
            if (interaction == null) continue;

            var trigger = interaction.CompatibleCards; // a struct: this is a copy
            if (!ListHolds(trigger.TriggerCards, vanillaButter)) continue;

            lists++;
            var grown = GrowWith(trigger.TriggerCards, sheepButters, out int count);
            if (grown == null) continue;

            trigger.TriggerCards = grown;
            interaction.CompatibleCards = trigger; // write the copy back
            added += count;
        }
    }

    private static bool ListHolds(CardData[] list, CardData card)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Length; i++)
            if (ReferenceEquals(list[i], card)) return true;
        return false;
    }

    // A copy of <list> with every card in <additions> it does not already hold appended, or null when
    // there is nothing to add (the second GameDataReady). <count> is how many were appended.
    private static CardData[] GrowWith(CardData[] list, List<CardData> additions, out int count)
    {
        var missing = new List<CardData>(additions.Count);
        foreach (var card in additions)
            if (!ListHolds(list, card)) missing.Add(card);

        count = missing.Count;
        if (count == 0) return null;

        var grown = new CardData[list.Length + count];
        Array.Copy(list, grown, list.Length);
        missing.CopyTo(grown, list.Length);
        return grown;
    }

    // v1.22.0 (round-2 plan feature 7, melt half): the three sheep butters melt in a Clay Bowl, Clay
    // Cauldron or Cooking Pot like vanilla butter. Vanilla's "Melt Butter" recipe on each container
    // holds MilkButter in its CompatibleCards and is found by that content, never by index. The recipe
    // pushes the ingredient's Liquidity bar up by one, and the OnFull each sheep butter now carries on
    // its own SpecialDurability1 (CardData/Item/Butter.json, SaltedButter.json, ClarifiedButter.json)
    // turns it into vanilla liquid butter, so adding the card without those bars would only accept it
    // and never melt it. The bars and these entries ship together. CookingRecipe.CompatibleCards is a
    // plain array read live (W0-4), so a grown copy written back to the recipe is enough. A second
    // GameDataReady appends nothing, because a card already in the list is skipped. Vanilla data is
    // touched only to append this mod's cards; nothing is initialised or rebuilt.
    private static readonly string[] MeltButterContainerUids =
    {
        "a968f3eaffc6b9743b82982b5af2ab8c", // ClayBowl
        "b1b5f3f02a453df42acaa396d61738f0", // ClayCauldron
        "5d5ebb1bb10a9904fa2dfd8d58f21048", // CookingPot
    };
    private const int ExpectedMeltButterRecipes = 3;

    private static void PatchSheepButterMelt()
    {
        try
        {
            var vanillaButter = CardUtil.GetCardDataById(VanillaButterUid) as CardData;
            if (vanillaButter == null)
            {
                Logger?.LogWarning("[SheepButterMelt] Vanilla MilkButter card not found; sheep butter will not melt in a pot or bowl");
                return;
            }

            var sheepButters = new List<CardData>(3);
            foreach (var uid in new[] { SheepButterUid, SaltedButterUid, ClarifiedButterUid })
            {
                if (CardUtil.GetCardDataById(uid) is CardData butter) sheepButters.Add(butter);
                else Logger?.LogWarning($"[SheepButterMelt] Sheep butter card '{uid}' not found; it will not melt");
            }
            if (sheepButters.Count == 0)
            {
                Logger?.LogWarning("[SheepButterMelt] No sheep butter card found; the Melt Butter recipes are unchanged");
                return;
            }

            int recipes = 0, added = 0;
            foreach (var containerUid in MeltButterContainerUids)
            {
                if (!(CardUtil.GetCardDataById(containerUid) is CardData container))
                {
                    Logger?.LogWarning($"[SheepButterMelt] Vanilla container '{containerUid}' not found; its Melt Butter recipe is unchanged");
                    continue;
                }
                var cookingRecipes = container.CookingRecipes;
                if (cookingRecipes == null)
                {
                    Logger?.LogWarning($"[SheepButterMelt] Vanilla container '{containerUid}' has no CookingRecipes; its Melt Butter recipe is unchanged");
                    continue;
                }

                foreach (var recipe in cookingRecipes)
                {
                    if (recipe == null || !ListHolds(recipe.CompatibleCards, vanillaButter)) continue;

                    recipes++;
                    var grown = GrowWith(recipe.CompatibleCards, sheepButters, out int count);
                    if (grown == null) continue;

                    recipe.CompatibleCards = grown;
                    added += count;
                }
            }

            // A silent zero is this feature's failure mode: a game update that renamed these cards or
            // moved butter out of their recipes would leave every sheep butter unable to melt.
            if (recipes == 0)
            {
                Logger?.LogWarning("[SheepButterMelt] No vanilla recipe holding butter was found; sheep butter will not melt in a pot or bowl");
                return;
            }

            Logger?.Log(LogLevel.Debug, $"[SheepButterMelt] {recipes} Melt Butter recipe(s) accept the sheep butters ({ExpectedMeltButterRecipes} expected on 2026-10-03); {added} card(s) added this pass");
        }
        catch (Exception ex)
        {
            Logger?.LogError($"[SheepButterMelt] Error: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    // v1.22.0 (round-2 plan feature 9, W0-5): the travelling animal trader can bring a tame ewe and a
    // ram. Each of the trader's two "Prepare" actions (spring and autumn) drops one weighted collection
    // into the trader's own inventory (DropCardsInsideInventory), and the trade window lists whatever
    // sits in that inventory, so a drop appended to that collection is a sale with no further
    // registration. GameSourceModify cannot reach it (its append works only on a top-level list of
    // UniqueID objects), so the collection's private CardDrop[] is rebuilt one entry longer.
    // A boxed CardDrop struct never compares equal to another, so Collections.AppendAll cannot dedup
    // them; a card already stocked is skipped here by its DroppedCard, which is what keeps a second
    // GameDataReady from stacking another sheep. Vanilla data is touched only to append this mod's
    // cards; nothing is initialised or rebuilt, and the array written back is never null.
    private static void PatchAnimalTraderStock()
    {
        try
        {
            var sheep = new List<CardData>(3);
            foreach (var uid in new[] { TameSheepUid, MaleSheepUid, FerretUid })
            {
                if (CardUtil.GetCardDataById(uid) is CardData card) sheep.Add(card);
                else Logger?.LogWarning($"[TraderSheep] Stock card '{uid}' not found; the animal trader will not stock it");
            }
            if (sheep.Count == 0)
            {
                Logger?.LogWarning("[TraderSheep] No sheep card found; the animal trader's stock is unchanged");
                return;
            }

            var trader = GameContent.Find<NPCAgent>(AnimalTraderUid);
            if (trader == null || trader.AgentActions == null)
            {
                Logger?.LogWarning($"[TraderSheep] Vanilla animal trader '{AnimalTraderUid}' (or its actions) not found; it will not sell sheep");
                return;
            }

            // CardsDropCollection.DroppedCards is private, so it is reached by reflection.
            var stockField = typeof(CardsDropCollection).GetField("DroppedCards",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (stockField == null || stockField.FieldType != typeof(CardDrop[]))
            {
                Logger?.LogWarning("[TraderSheep] CardsDropCollection.DroppedCards is not a CardDrop[] field; the animal trader will not sell sheep");
                return;
            }

            int actions = 0, added = 0, present = 0;
            foreach (var actionId in TraderStockActionIds)
            {
                var stockCollection = FindTraderStockCollection(trader, actionId);
                if (stockCollection == null)
                {
                    Logger?.LogWarning($"[TraderSheep] Trader action '{actionId}' or its stock collection not found; that visit will not offer sheep");
                    continue;
                }
                actions++;

                var stock = stockField.GetValue(stockCollection) as CardDrop[] ?? new CardDrop[0];
                var missing = new List<CardDrop>(sheep.Count);
                foreach (var card in sheep)
                {
                    if (StockHolds(stock, card)) { present++; continue; }

                    // Quantity (1,1) through the public constructor, never the private (0,0) default;
                    // BaseDropChance is on the 0 to 100 scale.
                    var drop = new CardDrop(card, new Vector2Int(1, 1));
                    float chance = string.Equals(card.UniqueID, FerretUid, StringComparison.Ordinal) ? TraderFerretDropChance : TraderSheepDropChance;
                    drop.DropChance = new CardDropChance { Active = true, BaseDropChance = chance };
                    missing.Add(drop);
                }
                if (missing.Count == 0) continue;

                var grown = new CardDrop[stock.Length + missing.Count];
                Array.Copy(stock, grown, stock.Length);
                missing.CopyTo(grown, stock.Length);
                stockField.SetValue(stockCollection, grown);
                added += missing.Count;
            }

            // A silent zero is this feature's failure mode: a game update that renamed the trader's
            // actions or emptied their collections would leave the trader unable to sell sheep. A
            // second GameDataReady appends nothing and still finds the entries already there.
            if (added + present == 0)
            {
                Logger?.LogWarning("[TraderSheep] No trader stock collection took a sheep drop; the animal trader will not sell sheep");
                return;
            }

            Logger?.Log(LogLevel.Debug, $"[TraderSheep] {added} sheep drop(s) appended to {actions} of {TraderStockActionIds.Length} trader stock action(s); {present} already present ({sheep.Count} sheep card(s), {TraderSheepDropChance}% each)");
        }
        catch (Exception ex)
        {
            Logger?.LogError($"[TraderSheep] Error: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    // The action is found by its ActionID, and its stock collection by what it does (it drops into the
    // trader's own inventory), not by index.
    private static CardsDropCollection FindTraderStockCollection(NPCAgent trader, string actionId)
    {
        foreach (var action in trader.AgentActions)
        {
            if (action == null || !string.Equals(action.ActionID, actionId, StringComparison.Ordinal)) continue;
            if (action.DroppedCards == null) return null;
            foreach (var collection in action.DroppedCards)
                if (collection != null && collection.DropCardsInsideInventory) return collection;
            return null;
        }
        return null;
    }

    private static bool StockHolds(CardDrop[] stock, CardData card)
    {
        for (int i = 0; i < stock.Length; i++)
            if (ReferenceEquals(stock[i].DroppedCard, card)) return true;
        return false;
    }

    // v1.22.0: wool yarn works wherever a vanilla loom asks for Yarn. Forward direction only:
    // the helper patches EVERY element whose RequiredCard is the primary, so making sh_yarn the
    // primary would also let flax yarn knit "Wool Socks". Only elements requiring exactly
    // YarnFiber are touched (vanilla data is modified solely to inject this mod's card), and
    // repeated calls reuse the same "alt_tab_<uid>" group, so a second GameDataReady adds nothing.
    private static void PatchWoolYarnAtLoom()
    {
        var allData = GetAllData();
        if (allData == null) return;

        int patched = BlueprintAlternates.AddAlternateIngredient(
            allData, VanillaYarnUid, WoolYarnUid, "Vanilla Yarn / Sirus Wool Yarn");

        // A silent zero is this feature's failure mode: both cards present but no slot patched
        // means a game update moved the cloth recipes off YarnFiber or gave them a tab group.
        if (patched == 0)
        {
            bool vanillaYarnLoaded = CardUtil.GetCardDataById(VanillaYarnUid) != null;
            bool woolYarnLoaded = CardUtil.GetCardDataById(WoolYarnUid) != null;
            Logger?.LogWarning($"[WoolYarnLoom] No loom slot accepts wool yarn (vanilla yarn loaded={vanillaYarnLoaded}, wool yarn loaded={woolYarnLoaded}); cloth recipes still take vanilla yarn only");
            return;
        }

        Logger?.Log(LogLevel.Debug, $"[WoolYarnLoom] Wool yarn accepted in {patched} vanilla blueprint slot(s) that require Yarn");
    }

    // v1.22.0: lanolin stands in for vanilla Fat in the six salve, candle and soap recipes (plan section 8).
    // Two methods on purpose. AddLanolinAsFatAlternate holds the ONE typed call to
    // BlueprintAlternates.AddAlternateIngredientIn, an overload the framework gained in 2.28.0; this
    // wrapper holds the try/catch. On a framework that predates the overload (a 2.28.0 build made before
    // the overload went in has the same version string and lacks it), the typed call to a missing member
    // throws MissingMethodException when the
    // CALLING method is JIT-compiled, so with the call inline in OnGameDataReady (or in this wrapper) it
    // would take every load-time patch down with it. Kept out of line, the failure is confined to this
    // one feature and the wrapper turns it into one warning that names the cause. A repeated
    // GameDataReady adds nothing: the framework reuses each blueprint's private tab group.
    private static void PatchLanolinAtFatBlueprints()
    {
        try
        {
            var allData = GetAllData();
            if (allData == null)
            {
                Logger?.LogWarning("[LanolinFat] No game data at GameDataReady (the cause is logged above); lanolin will not stand in for Fat");
                return;
            }

            AddLanolinAsFatAlternate(allData);
        }
        catch (MissingMemberException ex)
        {
            Logger?.LogWarning($"[LanolinFat] The CSFF Mod Framework in use has no BlueprintAlternates.AddAlternateIngredientIn (needs 2.28.0 or later); lanolin will not stand in for Fat, and everything else loads as usual: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[LanolinFat] Lanolin will not stand in for Fat: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    // NoInlining is load-bearing: it keeps the JIT from folding this method, and its typed framework
    // call, into the wrapper above, which would move a missing-member failure out of the try/catch.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddLanolinAsFatAlternate(IEnumerable allData)
    {
        int patched = BlueprintAlternates.AddAlternateIngredientIn(
            allData, VanillaFatUid, LanolinUid, LanolinFatBlueprintUids, "Vanilla Fat / Sirus Lanolin");

        // A silent zero is this feature's failure mode: both cards present but no slot patched means a
        // game update moved these recipes off vanilla Fat or gave their Fat slot a tab group (the
        // framework logs that per blueprint at Debug).
        if (patched == 0)
        {
            bool vanillaFatLoaded = CardUtil.GetCardDataById(VanillaFatUid) != null;
            bool lanolinLoaded = CardUtil.GetCardDataById(LanolinUid) != null;
            Logger?.LogWarning($"[LanolinFat] No blueprint accepts lanolin in place of Fat (vanilla Fat loaded={vanillaFatLoaded}, lanolin loaded={lanolinLoaded}); the salves, candle and soap still take vanilla Fat only");
            return;
        }

        if (patched < LanolinFatBlueprintUids.Length)
        {
            Logger?.LogWarning($"[LanolinFat] Lanolin was accepted in only {patched} of the {LanolinFatBlueprintUids.Length} named vanilla blueprints' Fat slots; a game update may have moved one of those recipes");
        }

        Logger?.Log(LogLevel.Debug, $"[LanolinFat] Lanolin accepted in the Fat slot of {patched} vanilla blueprint slot(s) (the {LanolinFatBlueprintUids.Length} named: Healing Salve, Pine Salve, Soothing Salve, Bug Repellent Salve, Candle, Soap)");
    }

    // Feature 40: with Herbs and Fungi installed, ground yarrow or dried chamomile can stand in for
    // Frostleaf Powder in the Lanolin Salve ONLY. Same two-method shape as the lanolin pass above (the typed
    // allow-list call stays out of line under the wrapper's try/catch), and never the unfiltered overload:
    // an alternate registered against vanilla FrostleafPowder without the allow-list would reach every
    // vanilla recipe that needs it. Vanilla stays the primary, so the salve works without H&F.
    private static void PatchSalveHerbAlternates()
    {
        try
        {
            var allData = GetAllData();
            if (allData == null)
            {
                Logger?.LogWarning("[SalveHerbs] No game data at GameDataReady (the cause is logged above); the Lanolin Salve takes Frostleaf Powder only");
                return;
            }

            AddSalveHerbAlternates(allData);
        }
        catch (MissingMemberException ex)
        {
            Logger?.LogWarning($"[SalveHerbs] The CSFF Mod Framework in use has no BlueprintAlternates.AddAlternateIngredientIn (needs 2.28.0 or later); the Lanolin Salve takes Frostleaf Powder only: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"[SalveHerbs] The Lanolin Salve takes Frostleaf Powder only: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddSalveHerbAlternates(IEnumerable allData)
    {
        int patched = 0;
        foreach (var herbUid in SalveHerbAlternateUids)
        {
            // Presence gate: a herb that does not resolve (H&F absent, or it renamed the card) is skipped.
            if (CardUtil.GetCardDataById(herbUid) == null) continue;
            patched += BlueprintAlternates.AddAlternateIngredientIn(
                allData, VanillaFrostleafPowderUid, herbUid, new[] { LanolinSalveBlueprintUid },
                "Vanilla Frostleaf Powder / " + herbUid);
        }

        if (patched == 0)
        {
            Logger?.Log(LogLevel.Debug, "[SalveHerbs] No Herbs and Fungi herb accepted in the Lanolin Salve (H&F absent, or its herbs not loaded)");
            return;
        }

        Logger?.Log(LogLevel.Debug, $"[SalveHerbs] {patched} Herbs and Fungi herb slot(s) accepted in the Lanolin Salve in place of Frostleaf Powder");
    }

    // DataBase.AllData, reached the same way WDI's GameLoadPatch does. Each null exit logs its
    // own cause, because the caller cannot tell them apart from a legitimate empty result.
    private static IEnumerable GetAllData()
    {
        var gameLoadType = CardUtil.FindGameType("GameLoad");
        if (gameLoadType == null)
        {
            Logger?.LogWarning("[WoolYarnLoom] 'GameLoad' type not found; wool yarn will not work at vanilla looms");
            return null;
        }

        var gameLoad = UnityEngine.Object.FindObjectOfType(gameLoadType);
        if (gameLoad == null)
        {
            Logger?.LogWarning("[WoolYarnLoom] No GameLoad instance at GameDataReady; wool yarn will not work at vanilla looms");
            return null;
        }

        var dataBase = Reflect.GetMember(gameLoad, "DataBase");
        if (dataBase == null)
        {
            Logger?.LogWarning("[WoolYarnLoom] GameLoad.DataBase is null; wool yarn will not work at vanilla looms");
            return null;
        }

        if (Reflect.GetMember(dataBase, "AllData") is not IEnumerable allData)
        {
            Logger?.LogWarning("[WoolYarnLoom] DataBase.AllData is null or not enumerable; wool yarn will not work at vanilla looms");
            return null;
        }

        return allData;
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

        // The tame ewe's JSON now authors passives of its own (well fed, spring, winter), so a blind
        // SetCollection would erase them at every GameDataReady. Keep everything the JSON authored,
        // drop only the two effects this method owns (by EffectName prefix) and append them fresh:
        // idempotent on the first fire and on every re-fire. Never AppendAll: boxed PassiveEffect
        // structs cannot dedup by reference, so each re-fire would add a duplicate pair.
        var cSharpOwned = BuildSheepPregnancyPassives(vanillaCow, ramTagObject);
        if (cSharpOwned.Count > 0)
        {
            var merged = new List<object>();
            if (Reflect.GetMember(tameSheep, "PassiveEffects") is IEnumerable current)
                foreach (var effect in current)
                {
                    var name = Reflect.GetMember(effect, "EffectName") as string;
                    bool owned = name != null
                        && (name.StartsWith(ImpregnateEffectPrefix, StringComparison.Ordinal)
                            || name.StartsWith(PregnantEffectPrefix, StringComparison.Ordinal));
                    if (!owned) merged.Add(effect);
                }
            int authored = merged.Count;
            merged.AddRange(cSharpOwned);
            Collections.SetCollection(tameSheep, "PassiveEffects", merged);
            // Read-back: a wiped JSON effect shows up here as 0 authored.
            Logger?.LogInfo($"[SheepReproduction] {TameSheepUid} PassiveEffects: {authored} authored + {cSharpOwned.Count} C#-owned.");
            LogWinterPenResolution(merged);
            Logger?.Log(LogLevel.Debug, "[SheepReproduction] Tame sheep now use cattle-style delayed pregnancy");
        }
        else
            Logger?.LogWarning("[SheepReproduction] no C#-owned pregnancy passives were built from the vanilla cow (see the Impregnate/Pregnant warnings above); the tame sheep's pregnancy stat is set but will never fill.");
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

        SetLocalizedText(durability, "CardStatName",
            "sh_tame_sheep_SpecialDurability4.CardStatName",
            "Pregnancy");

        var onFull = Reflect.GetMember(durability, "OnFull");
        if (onFull == null) return;

        SetLocalizedText(onFull, "ActionDescription",
            "sh_tame_sheep_SpecialDurability4.OnFull.ActionDescription",
            "Sheep has given birth!");

        var birthCollections = BuildBirthCollections(Reflect.GetMember(onFull, "ProducedCards"));
        if (birthCollections != null)
            Reflect.SetMember(onFull, "ProducedCards", birthCollections);

        ConfigureBirthReceivingChanges(onFull);
    }

    // One-time read-back for the winter effect's pen exemption: RequiredNOTContainerWarpData is a
    // card-valued field with no vanilla or in-repo precedent, and a failure to resolve is silent
    // (the exemption would simply never apply). The fallback is a tag on sh_sheep_pen.
    private static void LogWinterPenResolution(List<object> effects)
    {
        foreach (var effect in effects)
        {
            var name = Reflect.GetMember(effect, "EffectName") as string;
            if (name == null || !name.StartsWith("Winter Hunger", StringComparison.Ordinal)) continue;
            var conditions = Reflect.GetMember(effect, "Conditions");
            var notContainer = conditions != null ? Reflect.GetMember(conditions, "RequiredNOTContainer") as IEnumerable : null;
            int count = 0;
            string first = "none";
            if (notContainer != null)
                foreach (var entry in notContainer)
                {
                    count++;
                    if (count == 1) first = entry is UnityEngine.Object uo && uo != null ? uo.name : "null";
                }
            Logger?.LogInfo($"[SheepReproduction] winter pen exemption on {TameSheepUid}: RequiredNOTContainer has {count} entr{(count == 1 ? "y" : "ies")} (first: {first}).");
            if (count == 0 || first == "null")
                Logger?.LogWarning("[SheepReproduction] the Winter Hunger pen exemption did not resolve; penned sheep will pay the winter drain. Fall back to a tag on sh_sheep_pen with RequiredNOTContainerTagsWarpData.");
            return;
        }
        Logger?.LogWarning("[SheepReproduction] no Winter Hunger effect found on the tame sheep; the winter drain is missing.");
    }

    private static List<object> BuildSheepPregnancyPassives(object vanillaCow, object ramTagObject)
    {
        var passives = new List<object>();
        var impregnate = Reflect.DeepClone(FindPassiveEffectByPrefix(vanillaCow, "Impregnate"));
        var pregnant = Reflect.DeepClone(FindPassiveEffectByPrefix(vanillaCow, "Pregnant"));

        if (impregnate != null)
        {
            Reflect.SetMember(impregnate, "EffectName", ImpregnateEffectPrefix + " (pregnancy +2)");
            ConfigurePregnancyPassive(impregnate, new[] { ramTagObject }, new[] { RamTag },
                0f, SheepImpregnationThreshold, 2f);
            passives.Add(impregnate);
        }
        else
            Logger?.LogWarning("[SheepReproduction] vanilla cow 'Impregnate' PassiveEffect not found; tame sheep will not conceive (a game update may have renamed it).");

        if (pregnant != null)
        {
            Reflect.SetMember(pregnant, "EffectName", PregnantEffectPrefix);
            ConfigurePregnancyPassive(pregnant, Array.Empty<object>(), Array.Empty<string>(),
                SheepImpregnationThreshold + 1f, SheepGestationMax, 2f);
            passives.Add(pregnant);
        }
        else
            Logger?.LogWarning("[SheepReproduction] vanilla cow 'Pregnant' PassiveEffect not found; tame sheep pregnancy will not progress (a game update may have renamed it).");

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
                // DurabilitiesConditions and GeneralCondition are structs: GetMember returned boxed
                // copies, so write each back or every edit above is lost (found 2026-10-06).
                if (!Reflect.SetMember(conditions, "ReceivingRequiredDurabilityRanges", ranges))
                    Logger?.LogWarning("[SheepReproduction] Could not write the pregnancy passive's durability ranges back");
            }
            if (!Reflect.SetMember(passiveEffect, "Conditions", conditions))
                Logger?.LogWarning("[SheepReproduction] Could not write the pregnancy passive's conditions back");
        }

        SetActiveFloat(Reflect.GetMember(passiveEffect, "Special4RateModifier"), true, special4Rate);
        Reflect.SetMember(passiveEffect, "MultiplySpecial4Rate", false);
    }

    private static object BuildBirthCollections(object fallbackCollections)
    {
        var sourceCollection = GetFirstItem(fallbackCollections);
        if (sourceCollection == null) return fallbackCollections;

        var existingDrops = Reflect.GetMember(sourceCollection, "DroppedCards");
        var templateDrop = GetFirstItem(existingDrops);
        if (templateDrop == null) return fallbackCollections;

        // WarpResolver ran before GameDataReady — set the resolved reference directly.
        var lambCardData = CardUtil.GetCardDataById(LambUid);
        var birthCollection = BuildLambCollection(sourceCollection, existingDrops, templateDrop,
            "Birth", SingleBirthWeight, 1, lambCardData);
        var twinsCollection = BuildLambCollection(sourceCollection, existingDrops, templateDrop,
            "Twins", TwinBirthWeight, 2, lambCardData);
        SetTwinsMessage(twinsCollection);

        // Built fresh from the vanilla cow's collection on every call (PatchSheepReproduction
        // deep-clones the cow's pregnancy first), so a repeated GameDataReady cannot accumulate
        // collections.
        return Collections.CreateLike(fallbackCollections, new[] { birthCollection, twinsCollection });
    }

    private static object BuildLambCollection(object sourceCollection, object existingDrops, object templateDrop,
        string collectionName, int weight, int lambCount, object lambCardData)
    {
        var collection = Reflect.DeepClone(sourceCollection);
        Reflect.SetMember(collection, "CollectionName", collectionName);
        Reflect.SetMember(collection, "CollectionWeight", weight);
        ClearDurabilityWeightModifier(collection);

        var lambDrop = Reflect.DeepClone(templateDrop);
        Reflect.SetMember(lambDrop, "DroppedCardWarpData", LambUid);
        Reflect.SetMember(lambDrop, "DroppedCardWarpType", 3);
        Reflect.SetMember(lambDrop, "Quantity", new Vector2Int(lambCount, lambCount));
        if (lambCardData != null)
            Reflect.SetMember(lambDrop, "DroppedCard", lambCardData);

        Reflect.SetMember(collection, "DroppedCards",
            Collections.CreateLike(existingDrops, new List<object> { lambDrop }));
        return collection;
    }

    // The collection cloned is the cow's FIRST one, "Bad Pregancy" (vanilla spelling), whose
    // DurabilitiesDropChanceModifier adds up to +100 weight as the cow's Usage (Happiness) falls
    // (GetCollectionDropsReport adds it to CollectionWeight). A tame sheep's Usage is inactive
    // and reads 0, the full +100, so on both sheep collections it would turn the 17/3 split into
    // 117/103. The modifier is a struct of structs: a default instance is every entry inactive,
    // the same shape vanilla's own unmodified collections carry.
    private static void ClearDurabilityWeightModifier(object collection)
    {
        var modifier = Reflect.GetMember(collection, "DurabilitiesDropChanceModifier");
        if (modifier == null || !Reflect.SetMember(collection, "DurabilitiesDropChanceModifier", Activator.CreateInstance(modifier.GetType())))
            Logger?.LogWarning("[SheepReproduction] Could not clear the birth collection's durability weight modifier; twin odds may differ from 15%");
    }

    // Shown on the ewe after the lambs drop (GameManager plays the chosen collection's message);
    // the Birth collection keeps the cow's empty list.
    private static void SetTwinsMessage(object twinsCollection)
    {
        var existing = Reflect.GetMember(twinsCollection, "CollectionMessages");
        var message = LocalizedStringBuilder.Create(
            "sh_tame_sheep_SpecialDurability4.OnFull.TwinsMessage", "Twin lambs!", TameSheepUid);
        if (existing == null || message == null
            || !Reflect.SetMember(twinsCollection, "CollectionMessages", Collections.CreateLike(existing, new List<object> { message })))
            Logger?.LogWarning("[SheepReproduction] Could not set the twin-birth message; twins still drop, silently");
    }

    // LocalizedString is a struct, so Reflect.GetMember returns a boxed COPY: populating that copy
    // changes nothing on the owner. Until 1.22.0 the pregnancy stat name and birth message were
    // set that way and never landed, so sheep births announced the cloned cow's "Cow has given
    // birth!". Build a fresh value and write it back.
    private static void SetLocalizedText(object owner, string memberName, string key, string text)
    {
        var fresh = LocalizedStringBuilder.CreateLike(Reflect.GetMember(owner, memberName), key, text, TameSheepUid);
        if (fresh == null || !Reflect.SetMember(owner, memberName, fresh))
            Logger?.LogWarning($"[SheepReproduction] Could not set {memberName} to '{text}'");
    }

    // ReceivingCardChanges is a CardStateChange STRUCT, so Reflect.GetMember hands back a boxed
    // COPY: every write below lands on the copy and must be written back to the owner. Until 1.22.0
    // this edited the copy only and used ModType 1 (DurabilityChanges), so no ewe that gave birth
    // ever became a Lactating Sheep (T2.403: 20 births, 0 Lactating Sheep).
    private static void ConfigureBirthReceivingChanges(object onFull)
    {
        if (onFull == null) return;
        var receivingChanges = Reflect.GetMember(onFull, "ReceivingCardChanges");
        if (receivingChanges == null)
        {
            Logger?.LogWarning("[SheepReproduction] OnFull has no ReceivingCardChanges; a ewe will not become a Lactating Sheep after birth");
            return;
        }

        // CardModifications: None 0, DurabilityChanges 1, Transform 2, Destroy 3 (by name, not ordinal).
        var modType = Reflect.GetMember(receivingChanges, "ModType");
        if (modType == null || !modType.GetType().IsEnum
            || !Reflect.SetMember(receivingChanges, "ModType", Enum.Parse(modType.GetType(), "Transform")))
        {
            Logger?.LogWarning("[SheepReproduction] Could not set the birth swap's ModType to Transform; a ewe will not become a Lactating Sheep after birth");
            return;
        }
        Reflect.SetMember(receivingChanges, "TransformIntoWarpData", LactatingSheepUid);
        Reflect.SetMember(receivingChanges, "TransformIntoWarpType", 3);
        // WarpResolver already ran — set the resolved reference directly.
        var lactatingSheepData = CardUtil.GetCardDataById(LactatingSheepUid);
        if (lactatingSheepData != null)
            Reflect.SetMember(receivingChanges, "TransformInto", lactatingSheepData);
        // Nourishment lives on SpoilageTime: carry it across the swap so birth is not a free refill.
        Reflect.SetMember(receivingChanges, "TransferSpoilage", true);
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

        if (!Reflect.SetMember(onFull, "ReceivingCardChanges", receivingChanges))
            Logger?.LogWarning("[SheepReproduction] Could not write the birth swap back to OnFull; a ewe will not become a Lactating Sheep after birth");
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
