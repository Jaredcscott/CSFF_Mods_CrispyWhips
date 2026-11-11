using System;

namespace Sirus23ModCollection.Patcher
{
    /// <summary>
    /// The Wolf Companion's Health rule for one DTP tick (15 in-game minutes), called by
    /// WolfTickPatch.TryManageHealth. The fox, owl and hawk carry the same Health bar, but theirs drains from
    /// JSON PassiveEffects ("Starving Health Drain" on Hunger, "Parched Health Drain" on Thirst, each -2 per
    /// tick while its bar is in {0, 0.05}) and they die through the bar's OnZero action; the wolf's Health is
    /// managed here in C#, and its death (a wolf carcass) by the caller.
    ///
    /// Hunger keeps the wolf's own numbers (drain below a tenth, mend 0.5 above 60%). Thirst follows the fox's
    /// "Parched Health Drain" exactly (bound to FoxCompanion.json by Development_Tools/Tests/Sirus23-WolfCare.Tests.ps1),
    /// and the two drains add up as the fox's two PassiveEffects do. A parched wolf does not mend.
    ///
    /// Host-free on purpose: no BepInEx or UnityEngine reference, and C# 5 syntax only, so the test compiles
    /// this file as shipped with Add-Type and drives it directly.
    /// </summary>
    public static class WolfHealthRule
    {
        public const float StarvingBelow = 0.10f;
        public const float ParchedAtOrBelow = 0.05f;
        public const float MendAbove = 0.60f;
        public const float DrainPerEmptyBar = 2f;
        public const float MendPerTick = 0.5f;

        /// <summary>The wolf's Health after one tick, from its Hunger and Thirst as fractions of their maximums.</summary>
        public static float NextHealth(float health, float healthMax, float hungerRatio, float thirstRatio)
        {
            float drain = 0f;
            if (hungerRatio < StarvingBelow) drain += DrainPerEmptyBar;
            if (thirstRatio <= ParchedAtOrBelow) drain += DrainPerEmptyBar;
            if (drain > 0f) return Math.Max(0f, health - drain);

            if (hungerRatio > MendAbove && health < healthMax) return Math.Min(healthMax, health + MendPerTick);
            return health;
        }
    }
}
