using System;
using System.Reflection;
using HarmonyLib;

namespace ForestLore;

/// <summary>
/// Keeps every card a save stored in the Homestead inside it on load, even when they weigh more than
/// its 4000 capacity (a Homestead filled under 0.1.4's 10000). See <see cref="HomesteadLoadKeep"/> for
/// the cause and the rules. Two patches:
/// <list type="bullet">
/// <item>GameManager.LoadInventoryCard (private): a prefix and a finalizer count how deep a load is.
/// The base game re-adds a container's stored cards inside that call, synchronously: LoadCard starts
/// AddCard as a coroutine, and AddCard reaches its container placement before its first yield for an
/// ordinary card (.decomp/GameManager.cs, LoadInventoryCard and AddCard).</item>
/// <item>InGameCardBase.GetIndexForInventory(int, CardData, CardData, float): a postfix that, while a
/// load is in progress, replaces a refusal for the Homestead with the slot the card would have taken
/// had it fitted. Outside a load nothing changes, so adding a card to an over-full Homestead is still
/// refused until it is under 4000.</item>
/// </list>
/// </summary>
internal static class HomesteadLoadKeepPatch
{
    private static int _loadDepth;

    internal static void ApplyPatch(Harmony harmony)
    {
        MethodInfo load = AccessTools.Method(typeof(GameManager), "LoadInventoryCard");
        MethodInfo index = AccessTools.Method(typeof(InGameCardBase), "GetIndexForInventory",
            new[] { typeof(int), typeof(CardData), typeof(CardData), typeof(float) });
        if (load == null || index == null)
        {
            throw new MissingMethodException("Forest Lore: GameManager.LoadInventoryCard or InGameCardBase.GetIndexForInventory not found; an over-full Homestead may lose items on load.");
        }
        harmony.Patch(load,
            prefix: new HarmonyMethod(typeof(HomesteadLoadKeepPatch), nameof(Load_Prefix)),
            finalizer: new HarmonyMethod(typeof(HomesteadLoadKeepPatch), nameof(Load_Finalizer)));
        harmony.Patch(index,
            postfix: new HarmonyMethod(typeof(HomesteadLoadKeepPatch), nameof(Index_Postfix)));
    }

    private static void Load_Prefix()
    {
        _loadDepth++;
    }

    private static Exception Load_Finalizer(Exception __exception)
    {
        if (_loadDepth > 0) _loadDepth--;
        return __exception;
    }

    private static void Index_Postfix(InGameCardBase __instance, CardData _ForCard, CardData _WithLiquid, ref int __result)
    {
        if (__result >= 0 || _loadDepth <= 0 || __instance == null) return;
        CardData model = __instance.CardModel;
        if (model == null || model.UniqueID != HomesteadLoadKeep.HomesteadUid) return;

        bool hasCard = _ForCard != null;
        bool canReceive = hasCard && __instance.CanReceiveInInventory(_ForCard, _WithLiquid);
        bool weightInventory = __instance.IsInventoryCard && !__instance.IsLegacyInventory
            && model.CardType != CardTypes.Blueprint && model.CardType != CardTypes.EnvImprovement
            && model.CardType != CardTypes.EnvDamage && model.CardType != CardTypes.BlueprintInLocation;
        if (!HomesteadLoadKeep.ShouldKeep(__result, _loadDepth, model.UniqueID, hasCard, canReceive, weightInventory)) return;

        var slots = __instance.CardsInInventory;
        if (slots == null) return;
        GraphicsManager graphics = MBSingleton<GraphicsManager>.Instance;
        bool cooks = model.CanCook;
        __result = HomesteadLoadKeep.PickSlot(slots.Count,
            k => slots[k] != null && slots[k].IsFree,
            k => slots[k] != null && slots[k].CardModel == _ForCard && slots[k].LiquidModel == _WithLiquid
                 && !(graphics != null && graphics.IsPilingException(slots[k].MainCard)) && !cooks);
    }
}
