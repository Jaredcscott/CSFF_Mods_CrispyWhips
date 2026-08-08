using System;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The Village Academy (cmcAcademyInterior) and Village Inn (cmcInnInterior) both
    /// apply an unconditional "Hearth-Warmed" PassiveEffect (RateModifier +15/tick on
    /// Body Temperature, no season gate). CannotModifyBeyond can't cap this in JSON —
    /// it only clamps GameManager.ChangeStat's instant ValueModifier path, not an
    /// ongoing RateModifier drift (confirmed in decompiled GameManager.ChangeStat).
    /// Left unchecked the player cooks into Hyperthermia even in mild weather; a cold
    /// player entering from outside is left to warm up passively at whatever the
    /// ambient RateModifier allows.
    ///
    /// This actively manages Body Temperature to a 0.75 target fraction of
    /// CurrentMinMaxValue (already reflects live player modifiers — perks, equipment,
    /// PassiveEffects — since it reads the resolved runtime stat, not a static JSON
    /// base value) while the player is inside either interior: heats a cold player up
    /// to the target, cools an overheated player down to the target. Checked once per
    /// DTP tick (TickEvents.DtpTick, every 15 in-game minutes).
    /// </summary>
    internal static class IndoorHeatCapPatch
    {
        private static readonly string[] CappedEnvUids = { "cmcAcademyInterior", "cmcInnInterior" };
        private const float TargetFraction = 0.75f;

        private static bool _initialized;
        private static string _bodyTempGuid;
        private static object _bodyTempStat;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _bodyTempGuid = VanillaIds.GetStat("BodyTemperature");
            if (string.IsNullOrEmpty(_bodyTempGuid))
            {
                Plugin.Logger.LogWarning("[IndoorHeatCapPatch] BodyTemperature GUID not found in VanillaIds — indoor heat cap inactive.");
                return;
            }

            TickEvents.DtpTick += OnDtpTick;
            Plugin.Logger.LogDebug("[IndoorHeatCapPatch] initialized.");
        }

        private static void OnDtpTick()
        {
            try
            {
                var envUid = GameQuery.CurrentEnvironmentUniqueId;
                if (string.IsNullOrEmpty(envUid) || !CappedEnvUids.Contains(envUid)) return;

                EnsureStatCached();
                if (_bodyTempStat == null) return;

                var minMax = Reflect.GetMember(_bodyTempStat, "CurrentMinMaxValue");
                if (!TryGetVector(minMax, out var min, out var max) || max <= min) return;

                float targetValue = min + TargetFraction * (max - min);
                float current = StatAccess.GetCurrentValue(_bodyTempStat);
                if (float.IsNaN(current) || current == targetValue) return;

                StatAccess.SetCurrentValue(_bodyTempStat, targetValue);
                string direction = current < targetValue ? "heated" : "cooled";
                Plugin.Logger.LogDebug($"[IndoorHeatCapPatch] {direction} Body Temperature in '{envUid}' ({current:0.#} -> {targetValue:0.#}).");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[IndoorHeatCapPatch] tick threw: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void EnsureStatCached()
        {
            if (_bodyTempStat != null) return;
            try
            {
                var uidType = AccessTools.TypeByName("UniqueIDScriptable");
                var gameStatType = AccessTools.TypeByName("GameStat");
                if (uidType == null || gameStatType == null) return;

                var getFromId = uidType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "GetFromID" && m.IsGenericMethodDefinition);
                if (getFromId == null) return;

                var statModel = getFromId.MakeGenericMethod(gameStatType).Invoke(null, new object[] { _bodyTempGuid });
                if (statModel == null) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                var statsDict = Reflect.GetMember(gm, "StatsDict");
                if (statsDict is System.Collections.IDictionary dict && dict.Contains(statModel))
                    _bodyTempStat = dict[statModel];
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[IndoorHeatCapPatch] EnsureStatCached failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool TryGetVector(object vector, out float x, out float y)
        {
            x = 0f; y = 0f;
            if (vector == null) return false;
            var xVal = Reflect.GetMember(vector, "x");
            var yVal = Reflect.GetMember(vector, "y");
            if (xVal == null || yVal == null) return false;
            x = CardUtil.ToFloat(xVal);
            y = CardUtil.ToFloat(yVal);
            return true;
        }
    }
}
