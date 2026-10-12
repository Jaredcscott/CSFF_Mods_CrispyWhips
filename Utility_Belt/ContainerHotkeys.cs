using UnityEngine;

namespace Utility_Belt;

/// <summary>
/// [Hotkeys] Cycle Equipped Containers and Open Equipped Container 1 to 5 (plan B4). Read from
/// Plugin.Update behind GraphicsManager.PlayerIsTyping. The cycle key opens the next equipped card that has
/// an inventory, in equipment-slot order, wrapping around; a per-bag key opens the Nth one.
/// </summary>
/// <remarks>
/// The open goes through GraphicsManager.InspectCard(card), the call InGameCardBase.OnPointerClick makes for
/// a left click (.decomp/InGameCardBase.cs, OnPointerClick: "GraphicsM.InspectCard(this)"), so every vanilla
/// guard applies (an action in progress, a popup that cannot close) and opening a bag that is already open
/// closes it, as a second click does. The list is read the way GraphicsManager.GetEquippedCards reads it
/// (CharacterWindow.EquipmentSlotsLine.Slots[i].AssignedCard), but into our own list: GetEquippedCards
/// clears and refills a list the game keeps for its own callers. "Has an inventory" is CardData.HasInventory
/// (a weight capacity or inventory slots), so a liquid-only waterskin is not a stop.
/// The GraphicsManager singleton is resolved only after a bound key went down: MBSingleton.Instance falls
/// back to FindObjectOfType when it has none, which must not run every frame on the main menu.
/// </remarks>
internal static class ContainerHotkeys
{
    internal const int PerBagKeyCount = 5;

    private static readonly List<InGameCardBase> _bags = new();
    private static readonly HashSet<string> _warned = new();

    internal static void Tick()
    {
        var cycle = Plugin.CycleContainersKey.Value;
        var pressed = -1; // -1 none, 0 cycle, 1..5 per-bag
        if (cycle != KeyCode.None && Input.GetKeyDown(cycle))
        {
            pressed = 0;
        }
        else
        {
            for (var i = 0; i < PerBagKeyCount; i++)
            {
                var k = Plugin.OpenContainerKeys[i].Value;
                if (k != KeyCode.None && Input.GetKeyDown(k))
                {
                    pressed = i + 1;
                    break;
                }
            }
        }

        if (pressed < 0 || GraphicsManager.PlayerIsTyping)
        {
            return;
        }

        try
        {
            var gm = MBSingleton<GraphicsManager>.Instance;
            if (gm == null)
            {
                return; // main menu: no board to open anything on
            }

            if (!CollectBags(gm))
            {
                return;
            }

            if (_bags.Count == 0)
            {
                return;
            }

            InGameCardBase target;
            if (pressed == 0)
            {
                target = _bags[(IndexOfOpenBag(gm) + 1) % _bags.Count];
            }
            else
            {
                if (pressed > _bags.Count)
                {
                    return; // fewer bags equipped than the key's number
                }

                target = _bags[pressed - 1];
            }

            gm.InspectCard(target);
        }
        catch (Exception ex)
        {
            WarnOnce("exception", $"[UB] Container hotkey failed: {ex}");
        }
    }

    private static bool CollectBags(GraphicsManager gm)
    {
        _bags.Clear();
        var window = gm.CharacterWindow;
        if (window == null)
        {
            WarnOnce("CharacterWindow-null", "[UB] GraphicsManager.CharacterWindow is null; container hotkeys do nothing.");
            return false;
        }

        var line = window.EquipmentSlotsLine;
        if (line == null || line.Slots == null)
        {
            WarnOnce("EquipmentSlots-null", "[UB] CharacterWindow.EquipmentSlotsLine (or its Slots) is null; container hotkeys do nothing.");
            return false;
        }

        for (var i = 0; i < line.Slots.Count; i++)
        {
            var card = line.Slots[i]?.AssignedCard;
            if (card == null || card.Destroyed || card.CardModel == null)
            {
                continue;
            }

            if (card.CardModel.HasInventory)
            {
                _bags.Add(card);
            }
        }

        return true;
    }

    /// <summary>
    /// Index in _bags of the open bag, or of the bag holding the open card (walking up its containers);
    /// -1 when none.
    /// </summary>
    private static int IndexOfOpenBag(GraphicsManager gm)
    {
        var card = gm.InspectedCard;
        for (var depth = 0; card != null && depth < 8; depth++)
        {
            var idx = _bags.IndexOf(card);
            if (idx >= 0)
            {
                return idx;
            }

            card = card.CurrentContainer;
        }

        return -1;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning(message);
        }
    }
}
