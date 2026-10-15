using System;
using System.Collections;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Armor durability fix-up (vanilla balance pass, plan Part II row 2.2).
    ///
    /// <para>Before 1.69.17 every CMC armor piece carried a tool-sized Usage (60 to 100) while an enemy hit on a
    /// covered slot costs 250 Usage (EA 0.69c measurement, <c>Documentation/Research/Fleet/Balance/armor_wear_per_wound.md</c>),
    /// so each piece broke on its first hit. The JSON now carries armor-sized maxima (34560 to 46080, the Wooden
    /// Shield 5000). A new MaxValue applies on load, but a piece already in a save keeps its old current Usage, so
    /// this class lifts such a piece to its new maximum.</para>
    ///
    /// <para><b>Idempotent by value, no latch</b> (the shape ACT's plan uses for ACT-B7). An old piece that was ever
    /// hit is already destroyed (any hit took more than its whole Usage), and the old pieces had no wear rate, so
    /// every surviving old piece sits EXACTLY at its old maximum. Only a card reading exactly that value is lifted;
    /// a new piece starts at the new maximum, and the cloth pieces wear by -0.5 a tick while worn plus 250 a hit, so a
    /// worn new piece lands on an old maximum only after months of continuous wear, at its last hit. The class only
    /// ever RAISES a value that equals an old maximum and never lowers anything, so a second pass changes nothing.</para>
    ///
    /// <para><b>Every board.</b> <c>GameManager.AllCards</c> holds the current board plus what the player carries
    /// (root CLAUDE.md "An AllCards UID scan is NOT 'the cards in this room'"), so a piece left on another board is
    /// lifted when the player next stands there: the scan reruns on the first tick after every environment change
    /// and on the first tick after a load. A scan that cannot read the card list retries on the next tick.</para>
    /// </summary>
    internal static class ArmorUsageFixupPatch
    {
        private const string UsageStat = "UsageDurability";

        /// <summary>UID, old Usage maximum (pre-1.69.17), new Usage maximum (the JSON's value).</summary>
        internal static readonly (string Uid, float OldMax, float NewMax)[] Pieces =
        {
            ("combatandarmorbonelamellar", 100f, 46080f),
            ("combatandarmorquiltedcap", 60f, 34560f),
            ("combatandarmorquiltedcapdyedbillberry", 60f, 34560f),
            ("combatandarmorquiltedcapdyedcharcoal", 60f, 34560f),
            ("combatandarmorquiltedcapdyedochre", 60f, 34560f),
            ("cmcwoolquiltedcap", 60f, 34560f),
            ("combatandarmorquiltedvest", 80f, 34560f),
            ("cmcwoolquiltedvest", 80f, 34560f),
            ("combatandarmorwoodenshield", 80f, 5000f),
        };

        private static bool _initialized;
        private static string _scannedEnv;
        private static object _scannedGm;
        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.DtpTick += OnTick;
            Plugin.Logger.LogDebug("[ArmorUsageFixupPatch] initialized.");
        }

        /// <summary>The new Usage for a piece reading <paramref name="current"/>, or NaN when it must not change.
        /// Pure, so CMC tests can pin the rule.</summary>
        internal static float LiftedUsage(float oldMax, float newMax, float current)
        {
            if (float.IsNaN(current)) return float.NaN;
            return Math.Abs(current - oldMax) < 0.001f ? newMax : float.NaN;
        }

        private static void OnTick()
        {
            try
            {
                string env = GameQuery.CurrentEnvironmentUniqueId;
                if (string.IsNullOrEmpty(env) || GameQuery.IsTransitioning) return;
                // A load builds a new GameManager, so comparing the instance as well as the env rescans after every
                // load even when the run starts on the board the last scan read.
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;
                if (ReferenceEquals(gm, _scannedGm) && string.Equals(env, _scannedEnv, StringComparison.Ordinal)) return;
                if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return;

                int lifted = 0;
                var list = new List<object>();
                foreach (var c in allCards) if (c != null) list.Add(c);
                foreach (var card in list)
                {
                    string uid = CardUtil.GetCardUniqueId(card);
                    if (uid == null) continue;
                    foreach (var p in Pieces)
                    {
                        if (!string.Equals(uid, p.Uid, StringComparison.Ordinal)) continue;
                        float target = LiftedUsage(p.OldMax, p.NewMax, CardUtil.GetDurability(card, UsageStat));
                        if (float.IsNaN(target)) break;
                        if (CardUtil.SetDurability(card, UsageStat, target))
                        {
                            lifted++;
                            CardVisualsRefresh.RefreshDurabilityVisuals(card);
                        }
                        else if (_warned.Add("write:" + uid))
                            Plugin.Logger.LogWarning($"[ArmorUsageFixupPatch] Could not raise the Usage of an old {uid} to {p.NewMax:0}; it still breaks on its next hit.");
                        break;
                    }
                }
                _scannedEnv = env;
                _scannedGm = gm;
                if (lifted > 0)
                    Plugin.Logger.LogInfo($"[ArmorUsageFixupPatch] Raised {lifted} armor piece(s) from an older version to their new durability in {env}.");
            }
            catch (Exception ex)
            {
                if (_warned.Add("exception"))
                    Plugin.Logger.LogWarning($"[ArmorUsageFixupPatch] scan failed (retried next tick): {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
