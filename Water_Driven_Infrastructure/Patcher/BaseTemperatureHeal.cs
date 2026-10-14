using System;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace WaterDrivenInfrastructure.Patcher
{
    /// <summary>
    /// Repairs saves warmed for good by the Forge and Workshop's Blast (Nexus reports 2026-09-18 and
    /// 2026-10-11: "Perceived Temperature permanently increases"). Until 1.12.3 every Blast added +160 to
    /// vanilla BaseTemperature through a CardAction StatModification, which is a permanent write to the
    /// stat's base. BaseTemperature is the WORLD temperature (seasons, wind, weather and the Everwinter /
    /// Eversummer perks all reach it as modifiers), its BaseRatePerTick is 0, and its range is -80..80, so
    /// one Blast pinned the base at +80 for the rest of the run.
    ///
    /// Vanilla never writes this stat's base (EA 0.68b: no CardAction StatModification names it, and its
    /// TimeOfDayMods apply as GlobalModifier), so its base is always its BaseValue, 0. On each run start this
    /// moves a base above 0 back to 0 through the game's own permanent change (StatAccess.Modify), so the
    /// stat's statuses update. A base BELOW 0 is left alone: nothing WDI did can lower it.
    ///
    /// GameManager.OnGMInitialized fires at the end of FinishInitializing, after LoadCards has restored the
    /// save's stats, and re-fires on every save reload; the handler is idempotent and the subscription is
    /// made once per process.
    /// </summary>
    internal static class BaseTemperatureHeal
    {
        private const string BaseTemperatureUid = "d875c7e4eea52f54ca5076f256ccd16c"; // vanilla BaseTemperature
        private const float VanillaBaseValue = 0f;  // GameStat BaseTemperature.BaseValue, EA 0.68b

        private static bool _subscribed;

        public static void Initialize()
        {
            if (_subscribed) return;

            var gmType = CardUtil.FindGameType("GameManager");
            var field = gmType?.GetField("OnGMInitialized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(Action))
            {
                Plugin.Logger?.LogWarning("[BaseTemperatureHeal] GameManager.OnGMInitialized not found (game update?): a save warmed by an old Blast keeps its raised temperature.");
                return;
            }

            var current = (Action)field.GetValue(null);
            field.SetValue(null, (Action)Delegate.Combine(current, (Action)OnRunStart));
            _subscribed = true;
        }

        private static void OnRunStart()
        {
            try
            {
                var stat = StatAccess.Resolve(BaseTemperatureUid);
                if (stat == null) return; // StatAccess.Resolve already warned once with the cause

                float excess = stat.CurrentBaseValue - VanillaBaseValue;
                if (excess <= 0.01f) return;

                float now = StatAccess.Modify(BaseTemperatureUid, -excess);
                Plugin.Logger?.LogInfo($"[BaseTemperatureHeal] World temperature base was +{excess:F0} (left by Blast before 1.12.3); reset to {VanillaBaseValue:F0}. Temperature now reads {now:F1}.");
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogWarning($"[BaseTemperatureHeal] could not check the world temperature base: {ex.InnerException?.Message ?? ex.Message}");
            }
        }
    }
}
