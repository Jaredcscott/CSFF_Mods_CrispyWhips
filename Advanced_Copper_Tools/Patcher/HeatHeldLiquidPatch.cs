using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;

namespace Advanced_Copper_Tools.Patcher
{
    /// <summary>
    /// Heats liquid held directly inside a lit Tea Blending Station (no kettle needed).
    /// Vanilla CSFF only heats held liquid in containers placed inside another fire
    /// source's slot — there is no JSON-only path for a station that IS the heat
    /// source AND holds its own liquid. This patch closes that gap by bumping the
    /// station's LiquidFuelValue once per daytime point while it is lit and contains
    /// a heatable liquid. The held liquid's own OnFull (e.g. LQ_Water → BoiledWater)
    /// fires naturally when LiquidFuelValue reaches max.
    /// </summary>
    public static class HeatHeldLiquidPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;
        private const string LitStationUID = "advanced_copper_tools_tea_blending_station_lit";

        // Counteracts the vanilla LQ_Water CoolDown passive (-100/dtp) and yields
        // +100/dtp net heating. Liquid reaches max temp (200) from cold in ~2 dtp
        // (30 in-game minutes), matching the player's expectation of "lit kettle".
        private const float HeatPerDtp = 200f;

        private static Type _gmType;
        private static Type _cardBaseType;
        private static PropertyInfo _gmInstanceProp;
        private static PropertyInfo _gmDtpProp;
        private static FieldInfo _gmDtpField;
        private static FieldInfo _liquidFuelField;
        private static PropertyInfo _liquidFuelProp;
        private static FieldInfo _curLiquidQtyField;
        private static PropertyInfo _curLiquidQtyProp;
        private static FieldInfo _cardModelField;
        private static PropertyInfo _cardModelProp;
        private static FieldInfo _uidField;
        private static int _lastDtp = int.MinValue;
        private static bool _disabled;
        private static int _logCount;

        private const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                _gmType = AccessTools.TypeByName("GameManager");
                _cardBaseType = AccessTools.TypeByName("InGameCardBase");
                if (_gmType == null || _cardBaseType == null)
                {
                    Logger?.LogError("[HeatHeldLiquid] type lookup failed (GameManager/InGameCardBase)");
                    return;
                }

                // Per-frame hook; gates internally on DayTimePoints change so the work
                // only fires once per dtp tick.
                var update = AccessTools.Method(_gmType, "Update")
                          ?? AccessTools.Method(_gmType, "LateUpdate");
                if (update == null)
                {
                    Logger?.LogError("[HeatHeldLiquid] GameManager.Update/LateUpdate not found");
                    return;
                }
                harmony.Patch(update, postfix: new HarmonyMethod(typeof(HeatHeldLiquidPatch), nameof(Update_Postfix)));

                _gmInstanceProp = _gmType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                _gmDtpProp = _gmType.GetProperty("DayTimePoints", Flags)
                          ?? _gmType.GetProperty("CurrentDayTimePoints", Flags);
                _gmDtpField = _gmType.GetField("DayTimePoints", Flags)
                           ?? _gmType.GetField("CurrentDayTimePoints", Flags);

                Logger?.LogDebug($"[HeatHeldLiquid] patched (Instance={_gmInstanceProp != null}, DtpProp={_gmDtpProp != null}, DtpField={_gmDtpField != null})");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[HeatHeldLiquid] ApplyPatch failed: {ex}");
            }
        }

        private static void Update_Postfix()
        {
            if (_disabled) return;
            try
            {
                int dtp = ReadCurrentDtp();
                if (dtp == int.MinValue) return;
                if (dtp == _lastDtp) return;
                _lastDtp = dtp;
                TickAllLitStations();
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[HeatHeldLiquid] tick error: {ex.InnerException?.Message ?? ex.Message}");
                _disabled = true;
            }
        }

        private static int ReadCurrentDtp()
        {
            try
            {
                var gm = _gmInstanceProp?.GetValue(null);
                if (gm == null) return int.MinValue;
                object v = _gmDtpProp != null ? _gmDtpProp.GetValue(gm)
                         : _gmDtpField?.GetValue(gm);
                if (v == null) return int.MinValue;
                return Convert.ToInt32(v);
            }
            catch { return int.MinValue; }
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
                Logger?.LogInfo($"[HeatHeldLiquid] heated held liquid on {touched} lit station(s) at dtp={_lastDtp}");
        }

        private static bool IsLitStation(object card)
        {
            try
            {
                if (card == null) return false;
                if (_cardModelProp == null && _cardModelField == null)
                {
                    var t = card.GetType();
                    _cardModelProp = t.GetProperty("CardModel", Flags);
                    _cardModelField = t.GetField("CardModel", Flags);
                }
                object cardData = _cardModelProp?.GetValue(card)
                               ?? _cardModelField?.GetValue(card);
                if (cardData == null) return false;

                if (_uidField == null)
                    _uidField = cardData.GetType().GetField("UniqueID", Flags);
                var uid = _uidField?.GetValue(cardData) as string;
                return uid == LitStationUID;
            }
            catch { return false; }
        }

        private static bool TryHeat(object card)
        {
            try
            {
                var t = card.GetType();
                if (_curLiquidQtyField == null && _curLiquidQtyProp == null)
                {
                    _curLiquidQtyField = t.GetField("CurrentLiquidQuantity", Flags);
                    _curLiquidQtyProp  = t.GetProperty("CurrentLiquidQuantity", Flags);
                }
                float qty = ReadFloat(card, _curLiquidQtyField, _curLiquidQtyProp);
                if (qty <= 0f) return false; // no liquid held

                if (_liquidFuelField == null && _liquidFuelProp == null)
                {
                    _liquidFuelField = t.GetField("LiquidFuelValue", Flags);
                    _liquidFuelProp  = t.GetProperty("LiquidFuelValue", Flags);
                    if (_liquidFuelField == null && _liquidFuelProp == null)
                    {
                        Logger?.LogError("[HeatHeldLiquid] LiquidFuelValue not found on InGameCardBase — patch disabled");
                        _disabled = true;
                        return false;
                    }
                }

                float current = ReadFloat(card, _liquidFuelField, _liquidFuelProp);
                if (current >= 200f) return false; // already at max for vanilla heatable liquids
                float next = current + HeatPerDtp;
                if (next > 200f) next = 200f;
                WriteFloat(card, _liquidFuelField, _liquidFuelProp, next);
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[HeatHeldLiquid] TryHeat failed: {ex.Message}");
                return false;
            }
        }

        private static float ReadFloat(object obj, FieldInfo f, PropertyInfo p)
        {
            try
            {
                object v = f != null ? f.GetValue(obj) : p?.GetValue(obj);
                return v == null ? 0f : Convert.ToSingle(v);
            }
            catch { return 0f; }
        }

        private static void WriteFloat(object obj, FieldInfo f, PropertyInfo p, float v)
        {
            try
            {
                if (f != null) { f.SetValue(obj, v); return; }
                if (p != null && p.CanWrite) p.SetValue(obj, v);
            }
            catch (Exception ex) { Logger?.Log(LogLevel.Debug, $"[HeatHeldLiquid] WriteFloat failed: {ex.Message}"); }
        }
    }
}
