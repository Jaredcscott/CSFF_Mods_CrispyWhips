using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Village Crime — the negative counterpart to VillageRenownPatch's civic score
    /// (Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md §10.8.2). Owns cmcStatVillageCrime, a hidden
    /// 0-100 notoriety counter that rises as the village Watch detects thefts and attacks.
    ///
    /// Deliberately NOT connected to cmcStatVillageRenown: Renown is recomputed from scratch each
    /// tick from civic inputs, Crime is an accumulating incident record. The two axes are
    /// narratively related and mechanically independent.
    ///
    /// This class defines the stat's band structure and its passive decay. Every increase is an
    /// authored JSON StatModifications addition per §10.8.2, and every C# write to the stat lives
    /// in this file: the daily decay, the stuck-tester override, and the three entry points other
    /// patches call rather than reimplementing the StatsDict dance — AddCrime (theft detection,
    /// §10.8.3.6), ReduceCrime (a day served in the jail, §10.8.7.2) and ClearCrime (the guard
    /// gauntlet, §10.8.6, and release from the jail).
    ///
    /// The 100 ceiling lives on the stat's own MinMaxValue and is load-bearing, not cosmetic.
    /// StatModifier has no set-to-value operation (.decomp/StatModifier.cs — additive
    /// ValueModifier only), so the planned "killing a villager = instant Banished" penalty is
    /// authored as a +100 additive that relies on the stat clamping itself at 100.
    ///
    /// Banishment's travel lock is NOT enforced here — it is a declarative
    /// ConnectionGates.LockConditions StatThreshold on cmcEnvVillagePath in WorldMap/MapNodes.json
    /// (requires framework >= 2.20.2). RequiredStatValues on the travel DAs would not work:
    /// WorldMapInjector strips that field from every travel DA it injects.
    ///
    /// Graceful degradation (feedback_subsystem_graceful_degradation): an unreadable stat reads as
    /// Clean, matching the framework's own StatThreshold evaluation, which fails open. A broken
    /// stat must never brand the player a criminal or lock them out of the village.
    /// </summary>
    internal static class VillageCrimePatch
    {
        internal const string CrimeStatUid = "cmcStatVillageCrime";

        /// <summary>Lowest crime score in each band (§10.8.2): 0 Clean, 1-24 Suspected,
        /// 25-59 Wanted, 60+ Enemy/Banished. The Banished floor is the value the
        /// MapNodes.json LockConditions StatThreshold also uses — keep the two in step.</summary>
        internal const float SuspectedFloor = 1f;
        internal const float WantedFloor = 25f;
        internal const float BanishedFloor = 60f;

        /// <summary>Mirrors the stat's own MinMaxValue.y. See class doc.</summary>
        internal const float MaxCrime = 100f;

        private const float DecayPerDay = 1f;

        internal enum CrimeBand { Clean, Suspected, Wanted, Banished }

        private static bool _initialized;
        private static MethodInfo _getFromIdMethod;
        private static object _crimeStat;
        private static bool _loggedOverride;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.DayRollover += OnDayRollover;
            TickEvents.DtpTick += OnDtpTick;
            Plugin.Logger.LogDebug("[VillageCrimePatch] initialized.");
        }

        // ── Queries for later chunks ──────────────────────────────────────────────

        /// <summary>Current crime score, or 0 when the stat cannot be read (see class doc).</summary>
        public static float CurrentCrime()
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return 0f;
            float crime = ReadCrime(gm);
            return crime < 0f ? 0f : crime;
        }

        public static CrimeBand BandOf(float crime) =>
            crime >= BanishedFloor ? CrimeBand.Banished
            : crime >= WantedFloor ? CrimeBand.Wanted
            : crime >= SuspectedFloor ? CrimeBand.Suspected
            : CrimeBand.Clean;

        public static CrimeBand CurrentBand() => BandOf(CurrentCrime());

        public static bool IsBanished => CurrentBand() == CrimeBand.Banished;

        // ── Incident recording ────────────────────────────────────────────────────

        /// <summary>
        /// Adds <paramref name="amount"/> crime points (§10.8.2 point table) and returns the new
        /// score, or -1 when the stat is not readable (graceful degradation — a broken stat must
        /// never brand the player a criminal, so an unwritable stat is a no-op, not a retry).
        ///
        /// <para>This is the ONLY C# write path that RAISES the stat; the class doc's "C# writes
        /// the stat in exactly two places" refers to the two places that LOWER or reset it (daily
        /// decay + stuck-tester override). Detected incidents call in here rather than
        /// reimplementing the StatsDict/reflection dance — the first caller is the Copper Chest
        /// theft roll (CopperChestPatch, §10.8.3.6, +10 on detection).</para>
        /// </summary>
        public static float AddCrime(float amount)
        {
            if (amount <= 0f) return CurrentCrime();
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return -1f;

                float crime = ReadCrime(gm);
                if (crime < 0f)
                {
                    Plugin.Logger.LogWarning("[VillageCrimePatch] AddCrime: crime stat not readable — incident not recorded.");
                    return -1f;
                }

                float next = Math.Min(MaxCrime, Math.Max(0f, crime + amount));
                if (Math.Abs(next - crime) < 0.001f) return crime; // already at the ceiling
                WriteCrime(gm, next);
                Plugin.Logger.LogInfo($"[VillageCrimePatch] +{amount:0} crime ({crime:0} -> {next:0}, band {BandOf(next)}).");
                return next;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[VillageCrimePatch] AddCrime failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return -1f;
            }
        }

        /// <summary>
        /// Pays down <paramref name="amount"/> crime points and returns the new score, or -1 when
        /// the stat is not readable. Unlike the daily decay this deliberately DOES work inside the
        /// Banished band — it is the "serving the sentence is what clears the record" path
        /// (§10.8.7.2), and gating it the way decay is gated would make a jailed player's days
        /// count for nothing until the very last one.
        /// </summary>
        /// <param name="reason">Logged verbatim; say WHY the score went down.</param>
        public static float ReduceCrime(float amount, string reason)
        {
            if (amount <= 0f) return CurrentCrime();
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return -1f;

                float crime = ReadCrime(gm);
                if (crime < 0f)
                {
                    Plugin.Logger.LogWarning($"[VillageCrimePatch] ReduceCrime({reason}): crime stat not readable — not reduced.");
                    return -1f;
                }

                float next = Math.Max(0f, crime - amount);
                if (Math.Abs(next - crime) < 0.001f) return crime;
                WriteCrime(gm, next);
                Plugin.Logger.LogInfo($"[VillageCrimePatch] -{amount:0} crime ({crime:0} -> {next:0}, band {BandOf(next)}): {reason}.");
                return next;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[VillageCrimePatch] ReduceCrime failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return -1f;
            }
        }

        /// <summary>
        /// Resets the score to Clean and returns true if anything changed. The only sanctioned
        /// way back down from Banished (§10.8.6/§10.8.7) — passive decay deliberately refuses to
        /// touch that band, so a caller that reimplemented the StatsDict write here would be
        /// bypassing the design, not just duplicating code.
        /// </summary>
        /// <param name="reason">Logged verbatim; say WHY the slate was wiped, not that it was.</param>
        public static bool ClearCrime(string reason)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return false;

                float crime = ReadCrime(gm);
                if (crime < 0f)
                {
                    Plugin.Logger.LogWarning($"[VillageCrimePatch] ClearCrime({reason}): crime stat not readable — not cleared.");
                    return false;
                }
                if (crime <= 0f) return false;

                WriteCrime(gm, 0f);
                Plugin.Logger.LogInfo($"[VillageCrimePatch] Crime cleared ({crime:0} -> 0): {reason}.");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[VillageCrimePatch] ClearCrime failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }

        // ── Passive decay ─────────────────────────────────────────────────────────

        private static void OnDayRollover()
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                float crime = ReadCrime(gm);
                if (crime < 0f) return;                    // unreadable — nothing to decay
                if (crime <= 0f) return;                   // already Clean
                if (crime >= BanishedFloor) return;        // Banished clears only via §10.8.6/§10.8.7

                float next = StepDown(crime);
                if (Math.Abs(next - crime) < 0.001f) return;
                WriteCrime(gm, next);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[VillageCrimePatch] OnDayRollover failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>One day of decay. A single step never drops more than one point below the
        /// band it lands in, so a score crossing a threshold always spends at least a day in the
        /// tier below rather than chaining straight past it.</summary>
        private static float StepDown(float crime)
        {
            float next = Math.Max(0f, crime - DecayPerDay);
            var landed = BandOf(next);
            return landed == BandOf(crime) ? next : Math.Max(next, TopOf(landed));
        }

        private static float TopOf(CrimeBand band) => band switch
        {
            CrimeBand.Clean => 0f,
            CrimeBand.Suspected => WantedFloor - 1f,
            CrimeBand.Wanted => BanishedFloor - 1f,
            _ => MaxCrime,
        };

        // ── Stuck-tester override (§10.8.9 R3) ────────────────────────────────────

        // Risk R3: the village content behind a Banished lock (cottages, Hall boards, Well,
        // market stall) is a large fraction of what this mod ships. Set ForceClearVillageCrime
        // in the CMC config to zero the stat without a save edit — the connection gate
        // re-evaluates within 5 s and the village path reopens. Held at 0 while the flag is on,
        // so remember to turn it back off before testing crime itself.
        private static void OnDtpTick()
        {
            if (!Plugin.ForceClearVillageCrime.Value) return;
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;
                float crime = ReadCrime(gm);
                if (crime <= 0f) return;

                WriteCrime(gm, 0f);
                if (!_loggedOverride)
                {
                    _loggedOverride = true;
                    Plugin.Logger.LogInfo($"[VillageCrimePatch] ForceClearVillageCrime is on — crime cleared ({crime:0} -> 0) and held at 0.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[VillageCrimePatch] Force-clear failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Stat access (the proven StatsDict idiom, as in VillageRenownPatch) ────

        private static bool ResolveGetFromId()
        {
            if (_getFromIdMethod != null) return true;
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");
            if (uidType == null) return false;
            _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            return _getFromIdMethod != null;
        }

        private static object ResolveStatInstance(object gm)
        {
            if (!ResolveGetFromId()) return null;
            _crimeStat ??= _getFromIdMethod.Invoke(null, new object[] { CrimeStatUid });
            if (_crimeStat == null) return null;

            if (CardUtil.GetCachedField(gm.GetType(), "StatsDict")?.GetValue(gm) is not IDictionary statsDict) return null;
            if (!statsDict.Contains(_crimeStat)) return null;
            return statsDict[_crimeStat];
        }

        /// <summary>Current crime value, or -1 when the stat is not readable yet (stat SO missing,
        /// StatsDict not built, or the stat not materialized for this run).</summary>
        private static float ReadCrime(object gm)
        {
            var instance = ResolveStatInstance(gm);
            if (instance == null) return -1f;
            float value = StatAccess.GetCurrentValue(instance);
            return float.IsNaN(value) ? -1f : value;
        }

        private static void WriteCrime(object gm, float value)
        {
            var instance = ResolveStatInstance(gm);
            if (instance != null) StatAccess.SetCurrentValue(instance, value);
        }
    }
}
