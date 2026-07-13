using System;
using HarmonyLib;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Advanced_Copper_Tools.Patcher
{
    /// <summary>
    /// Legacy beta fallback for Tea Station layouts that stored real liquid directly
    /// on the station card. Current ACT stations use SpecialDurability3/4 as their
    /// reservoir, so this patch is opt-in through the BepInEx config.
    /// </summary>
    public static class HeatHeldLiquidPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;
        private const string LitStationUID = "advanced_copper_tools_tea_blending_station_lit";

        // Counteracts the vanilla LQ_Water CoolDown passive (-100/dtp) and yields
        // +100/dtp net heating. Liquid reaches max temp (200) from cold in ~2 dtp
        // (30 in-game minutes), matching the player's expectation of "lit kettle".
        private const float HeatPerDtp = 200f;

        private static Type _cardBaseType;
        private static bool _disabled;
        private static bool _liquidFuelMissingLogged;
        private static int _logCount;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                _cardBaseType = CardUtil.FindGameType("InGameCardBase");
                if (_cardBaseType == null)
                {
                    Logger?.LogError("[HeatHeldLiquid] InGameCardBase type not found");
                    return;
                }

                // Fires once per in-game DTP change (framework tick gate) instead of a
                // hand-rolled GameManager.Update patch with manual DTP-change detection.
                TickEvents.DtpTick += OnDtpTick;

                Logger?.LogDebug("[HeatHeldLiquid] subscribed to TickEvents.DtpTick");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[HeatHeldLiquid] ApplyPatch failed: {ex}");
            }
        }

        private static void OnDtpTick()
        {
            if (_disabled) return;
            try
            {
                TickAllLitStations();
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[HeatHeldLiquid] tick error: {FullException(ex)}");
                _disabled = true;
            }
        }

        private static void TickAllLitStations()
        {
            var cards = UnityEngine.Object.FindObjectsOfType(_cardBaseType);
            if (cards == null) return;
            int touched = 0;
            foreach (var c in cards)
            {
                if (!IsLitStation(c)) continue;
                if (TryHeat(c)) touched++;
            }
            if (touched > 0 && _logCount++ < 4)
                Logger?.LogDebug($"[HeatHeldLiquid] heated held liquid on {touched} lit station(s).");
        }

        private static bool IsLitStation(object card)
        {
            return card != null && CardUtil.GetCardUniqueId(card) == LitStationUID;
        }

        private static bool TryHeat(object card)
        {
            try
            {
                float qty = Reflect.GetFloat(card, "CurrentLiquidQuantity");
                if (qty <= 0f) return false; // no liquid held

                if (!Reflect.TryGetMember(card, "LiquidFuelValue", out var raw))
                {
                    if (!_liquidFuelMissingLogged)
                    {
                        Logger?.LogError("[HeatHeldLiquid] LiquidFuelValue not found on InGameCardBase — patch disabled");
                        _liquidFuelMissingLogged = true;
                        _disabled = true;
                    }
                    return false;
                }

                float current = CardUtil.ToFloat(raw);
                if (current >= 200f) return false; // already at max for vanilla heatable liquids
                float next = Math.Min(current + HeatPerDtp, 200f);
                Reflect.SetMember(card, "LiquidFuelValue", next);
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[HeatHeldLiquid] TryHeat failed: {FullException(ex)}");
                return false;
            }
        }

        private static string FullException(Exception ex)
        {
            return ex.InnerException?.ToString() ?? ex.ToString();
        }
    }
}
