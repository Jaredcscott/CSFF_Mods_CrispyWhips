using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Utility_Belt;

/// <summary>
/// [Capacity] Extra Hand Slots and Extra Carry Weight, the option form of Card Size Reduce's two ModCore perks.
/// Neither writes save data: the hand bar's slot count is GraphicsManager.MaxItemSlots, a scene value read once
/// by GraphicsManager.Init, and the carry limit is the player Weight stat's max modifier, which StatSaveData
/// does not save (the game rebuilds it from perks every run). So turning an option off, or removing Utility
/// Belt, leaves nothing behind in a save.
/// </summary>
internal static class Capacity
{
    private static readonly ConditionalWeakTable<GraphicsManager, object> SlotsRaised = new();

    private static GameManager _weightGm;
    private static int _weightApplied;
    private static MethodInfo _changeMinMax;
    private static bool _warnedMinMax;

    /// <summary>GraphicsManager.Init prefix: raises the hand bar's slot count before Init builds the slots.</summary>
    internal static void BeforeGraphicsInit(GraphicsManager graphics)
    {
        if (!graphics || CardSize.OtherModOwnsIt)
        {
            return;
        }

        var extra = Plugin.ExtraHandSlots.Value;
        // MaxItemSlots 0 means the game is running an unlimited hand bar; there is nothing to raise.
        if (extra <= 0 || graphics.MaxItemSlots <= 0 || SlotsRaised.TryGetValue(graphics, out _))
        {
            return;
        }

        SlotsRaised.Add(graphics, null);
        graphics.MaxItemSlots += extra;
    }

    /// <summary>GameManager.OnGMInitialized (once per run start): applies Extra Carry Weight to the new run.</summary>
    internal static void OnRunStarted()
    {
        ApplyCarryWeight();
    }

    /// <summary>
    /// Brings the player Weight stat's max to vanilla plus Extra Carry Weight. Tracks what it added to THIS
    /// GameManager, so a change mid-run applies only the difference and a new run starts from zero.
    /// </summary>
    internal static void ApplyCarryWeight()
    {
        if (CardSize.OtherModOwnsIt)
        {
            return;
        }

        var gm = MBSingleton<GameManager>.Instance;
        if (!gm || !gm.InGamePlayerWeight)
        {
            return;
        }

        if (gm != _weightGm)
        {
            _weightGm = gm;
            _weightApplied = 0;
        }

        var delta = Plugin.ExtraCarryWeight.Value - _weightApplied;
        if (delta == 0)
        {
            return;
        }

        _changeMinMax ??= AccessTools.Method(typeof(GameManager), "ChangeStatMinMaxValues", new[] { typeof(InGameStat), typeof(Vector2) });
        if (_changeMinMax == null)
        {
            if (!_warnedMinMax)
            {
                _warnedMinMax = true;
                Plugin.Logger.LogWarning("[UB] GameManager.ChangeStatMinMaxValues not found; Extra Carry Weight stays off.");
            }

            return;
        }

        // An IEnumerator: Invoke only builds it, so the game's own coroutine runner must drive it (CLAUDE.md
        // "Runtime Card Removal"). It also refreshes the Weight stat's statuses against the new limit.
        if (_changeMinMax.Invoke(gm, new object[] { gm.InGamePlayerWeight, new Vector2(0f, delta) }) is IEnumerator routine)
        {
            gm.StartCoroutine(routine);
            _weightApplied += delta;
        }
    }
}
