using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Town Wood Pile — the village's communal fuel stack and the shared pool the six automatic
    /// village hearths draw on (Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md
    /// §10.11 Chunk G).
    ///
    /// The pile carries TWO independent counters:
    ///   - Wood Stockpile (SpecialDurability1 / <see cref="FuelStatUid"/>) — the Fuel pool the six
    ///     hearths actually burn. Canonical value lives in a hidden GameStat (see the class comment
    ///     below on why) and the card's SD1 bar is a display mirror.
    ///   - Player's Pay (SpecialDurability2) — a running balance of the same fuel value, credited
    ///     alongside the Fuel pool every time wood is stocked, but spent independently. Lives
    ///     directly on the card (SD2), never a hidden stat: unlike Fuel, nothing needs to read it
    ///     from another environment — it is only ever touched from a CI/DA fired on the pile card
    ///     itself, so the card IS the canonical copy (same idiom as <see cref="MarketStallPatch"/>'s
    ///     Sales Revenue).
    ///
    /// Mechanics:
    ///   - Stocking: the "Stock the Wood Pile" CardInteraction accepts Twigs / Long Stick / Wood
    ///     (all three species UIDs) / Firewood. The CI's own GivenCardChanges.ModType 3 destroys the
    ///     dragged card; this class reads its UID in Before (see below) and credits BOTH counters by
    ///     the same fuel value — Fuel pool via <see cref="ReadPool"/>/<see cref="WritePool"/>, Pay
    ///     directly on <c>ctx.Card</c>'s SD2.
    ///   - Withdrawing: "Collect as Copper"/"Collect as Salt" DismantleActions (modelled on
    ///     <see cref="MarketStallPatch"/>'s drop-resource -> accrue-meter -> collect-as-currency
    ///     loop) drain Player's Pay ONLY, at <see cref="CostPerNugget"/>/<see cref="CostPerSalt"/>
    ///     pay per unit, paying out every affordable unit in one click. This is the fix for the
    ///     ORIGINAL version of this feature, which pointed the same two actions at the Fuel pool —
    ///     every nugget cashed out was fuel the six hearths no longer had. Player's Pay is a
    ///     completely separate balance, so cashing out can never put a hearth out.
    ///   - Spending: <see cref="TryConsume"/> is what VillageFireplacePatch calls before every
    ///     top-off/revive. All-or-nothing by design (§10.11 Chunk G item 6): a top-off the pool
    ///     cannot fully cover is SKIPPED, not partially filled, so the hearth cools on its own
    ///     normal FuelCapacity schedule instead of being held at a permanent trickle.
    ///
    /// WHY THE CANONICAL POOL IS A GameStat AND NOT THE CARD'S OWN BAR (load-bearing):
    /// The pile card lives in cmcEnvVillage, but the six hearths it feeds live in six INTERIOR
    /// environments. GameManager.AllCards is current-environment-scoped (root CLAUDE.md "Runtime
    /// Card Spawning" / memory reference_allcards_env_scoped), so while the player is standing in
    /// the Inn the pile card is simply not on the board and its SpecialDurability1 cannot be read
    /// at all. The pool therefore lives in the persistent hidden stat cmcStatTownWoodPileFuel
    /// (GameStat/CMC_TownWoodPileFuel.json) — readable and writable from any environment — and the
    /// card's "Wood Stockpile" bar is a DISPLAY MIRROR resynced from the stat whenever the player
    /// is in the village. This is the same migration VillageReputationPatch.cs already made away
    /// from scene-scoped live-card scans, and for the same reason.
    ///
    /// Stat access uses the proven StatsDict idiom copied from VillageCrimePatch.ResolveStatInstance
    /// rather than a reimplementation.
    /// </summary>
    internal static class TownWoodPilePatch
    {
        internal const string PileUid       = "cmcTownWoodPile";
        internal const string FuelStatUid   = "cmcStatTownWoodPileFuel";
        private  const string VillageEnvUid = "cmcEnvVillage";

        private const string NuggetGuid = "4b0f4937a5ecb90499428c8c10288afc";
        private const string SaltGuid   = "f91b5676fc26c0e48a4ee6fd9dfc2ffa";

        /// <summary>Pay-value cost of one payout — same exchange rate Market Stall's Sales Revenue
        /// uses.</summary>
        private const float CostPerNugget = 50f;
        private const float CostPerSalt   = 30f;

        /// <summary>Player's Pay ceiling. Mirrors the card's SpecialDurability2.MaxValue — same
        /// ceiling Market Stall uses for its own Sales Revenue bar.</summary>
        private const float PayMax = 480f;

        /// <summary>Pool ceiling. Mirrors the card's SpecialDurability1.MaxValue AND the stat's own
        /// MinMaxValue.y — all three must stay in step.
        ///
        /// <para>672 == 7 × 96, deliberately ABOVE the 576 (6 × 96) it costs to re-light all six
        /// hearths from cold at once. A 480 ceiling (MarketStall's SD1 ceiling, the original
        /// choice) was silently unwinnable: a player who let every hearth go out could never
        /// recover them even from a completely full pile.</para></summary>
        internal const float PoolMax = 672f;

        /// <summary>The cards the pile accepts. Deliberately WOOD only — twigs, long sticks, wood
        /// (all three species UIDs) and firewood. Peat and Charcoal are real vanilla fireplace
        /// fuels and are deliberately NOT accepted: this is a wood pile, and "it takes wood" is a
        /// cleaner line for a player to hold than an arbitrary subset of everything that burns.
        /// All three Wood species are listed because they all display in-game as a card literally
        /// named "Wood" — accepting only the base UID would leave a player holding pine wood
        /// staring at a "Wood" card the pile silently refuses.</summary>
        private static readonly HashSet<string> AcceptedFuelUids = new HashSet<string>
        {
            "758be01db25eb4649a288d64b7600b9b", // Firewood
            "692afc638c39e32428629da58f56136a", // Wood
            "3b98f1dd27ae5634db3f3aa04cad7ebb", // WoodPine   (displays as "Wood")
            "42ea6fec87c332849bf5863b6e1fc8df", // WoodAlder  (displays as "Wood")
            "3db4c94184af274409f0d3eb16870f64", // StickLong  ("Long Stick")
            "fa8834c3111a71f47bdd23d66b33cf61", // Twigs
        };

        /// <summary>
        /// Fallback credit for accepted cards that carry NO live <c>FuelCapacity</c> of their own,
        /// so <see cref="FuelValueOf"/> has nothing to read. Values verified against vanilla
        /// `CardData/Fireplace.json` this pass — an earlier version of this file claimed Wood 7 /
        /// Long Stick 4 were "lifted verbatim" from it and that was simply WRONG, so the real
        /// mapping is spelled out here:
        ///
        /// <list type="bullet">
        /// <item>Vanilla feeds Wood and Firewood through the "Feed Firewood" CI with
        /// <c>GetFuelFromOtherCard: true</c> — i.e. the card donates its OWN FuelCapacity, which is
        /// 35 for Wood and 21 for WoodPine/WoodAlder. That path is handled live in
        /// <see cref="FuelValueOf"/>; these entries are only the floor for a card whose stat reads
        /// zero.</item>
        /// <item>The FuelChange 7 / 4 "Feed Fuel" CIs are TAG-gated (`Image_7721` / `Image_7723`),
        /// not card-gated. **Twigs carries `Image_7721`, so Twigs is the 7** — not Wood.</item>
        /// <item>**Long Stick is not a vanilla fireplace fuel at all** (its only tag is
        /// `Slider_7627`; the only Fireplace CI naming it is "Make Rustic Spear"). Crediting it 4
        /// is a deliberate CMC choice — a wood pile plausibly takes sticks even though a hearth
        /// won't — NOT a vanilla-derived number.</item>
        /// </list></summary>
        private static readonly Dictionary<string, float> FallbackFuelValues = new Dictionary<string, float>
        {
            { "fa8834c3111a71f47bdd23d66b33cf61",  7f }, // Twigs      — vanilla "Feed Fuel" (Image_7721)
            { "3db4c94184af274409f0d3eb16870f64",  4f }, // StickLong  — CMC choice, not vanilla fuel
            { "692afc638c39e32428629da58f56136a", 35f }, // Wood       — vanilla FuelCapacity floor
            { "3b98f1dd27ae5634db3f3aa04cad7ebb", 21f }, // WoodPine   — vanilla FuelCapacity floor
            { "42ea6fec87c332849bf5863b6e1fc8df", 21f }, // WoodAlder  — vanilla FuelCapacity floor
            { "758be01db25eb4649a288d64b7600b9b", 21f }, // Firewood   — base JSON value is 0; real
                                                        //   cards get theirs from the action that
                                                        //   split them, read live above. 21 is a
                                                        //   floor so a zero-fuel Firewood is not
                                                        //   consumed for literally nothing.
        };

        private static MethodInfo _getFromIdMethod;
        private static bool _initialized;

        // ── Registration ─────────────────────────────────────────────────────────

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (CardUtil.FindGameType("InGameCardBase") == null)
            {
                Plugin.Logger.LogWarning("[TownWoodPilePatch] InGameCardBase type not found — town wood pile mechanics inactive.");
                return;
            }

            // Stocking. The value MUST be captured in Before: the CI's own GivenCardChanges.ModType
            // 3 destroys the dragged card as part of the action, so by the time After runs
            // ctx.GivenCard's CardModel may already be gone and the UID lookup would silently miss.
            // Same shape as CopperChestPatch's CopperChestMillerRestitution handler.
            // GUARD (runs first, and is the authoritative one): cancel the stock action outright
            // when it would destroy the dragged card for nothing — either the pool is already full,
            // or the card carries no fuel at all. The CI's own GivenCardChanges.ModType 3 destroys
            // the dragged card unconditionally once the action fires, so refusing to let it fire is
            // the ONLY place this can be prevented; clamping the credit afterwards (as the first
            // version did) still eats the card. The CardData carries a matching
            // RequiredReceivingDurabilities gate so the action also hides in the UI, but that gate
            // reads the card's display-mirror bar, which can lag the real pool by up to one tick —
            // this check reads the canonical stat, so it is the one that must be correct.
            ActionRouter.Register(new ActionHandler
            {
                Name             = "TownWoodPileStockGuard",
                CardUid          = PileUid,
                ActionKeyPrefix  = "CMC_TownWoodPile_CI_Stock",
                ActionNamePrefix = "Stock the Wood Pile",
                Timing           = ActionTiming.Cancel,
                Before           = StockGuardBefore,
            });

            ActionRouter.Register(new ActionHandler
            {
                Name             = "TownWoodPileStock",
                CardUid          = PileUid,
                ActionKeyPrefix  = "CMC_TownWoodPile_CI_Stock",
                ActionNamePrefix = "Stock the Wood Pile",
                Timing           = ActionTiming.AfterWrapped,
                Before           = ctx => { ctx.Tag = FuelValueOf(ctx.GivenCard); return false; },
                After            = StockAfter,
            });

            ActionRouter.Register(new ActionHandler
            {
                Name             = "TownWoodPileCollectCopper",
                CardUid          = PileUid,
                ActionKeyPrefix  = "CMC_TownWoodPile_DA_CollectCopper",
                ActionNamePrefix = "Collect as Copper",
                Timing           = ActionTiming.AfterWrapped,
                After            = ctx => CollectAfter(ctx, CostPerNugget, NuggetGuid, "copper nugget"),
            });

            ActionRouter.Register(new ActionHandler
            {
                Name             = "TownWoodPileCollectSalt",
                CardUid          = PileUid,
                ActionKeyPrefix  = "CMC_TownWoodPile_DA_CollectSalt",
                ActionNamePrefix = "Collect as Salt",
                Timing           = ActionTiming.AfterWrapped,
                After            = ctx => CollectAfter(ctx, CostPerSalt, SaltGuid, "salt unit"),
            });

            TickEvents.DtpTick += OnDtpTick;

            Plugin.Logger.LogDebug("[TownWoodPilePatch] initialized.");
        }

        // ── Public pool API (VillageFireplacePatch is the only consumer) ──────────

        /// <summary>Current pool value, or -1 when the stat is not readable yet (stat SO missing,
        /// StatsDict not built, or the stat not materialized for this run).</summary>
        internal static float CurrentFuel()
        {
            var gm = CardUtil.GetGameManagerInstance();
            return gm == null ? -1f : ReadPool(gm);
        }

        /// <summary>
        /// Debit <paramref name="amount"/> from the shared pool, all-or-nothing. Returns false —
        /// having changed nothing — when the pool cannot cover the full amount, which is what makes
        /// a hearth go cold instead of being held at a trickle (§10.11 Chunk G item 6).
        ///
        /// <para>Fails OPEN on an unreadable stat: if the pool cannot be read at all (-1) the
        /// caller is allowed to proceed. A broken stat must not silently freeze a player out of
        /// every village hearth — the same graceful-degradation stance VillageCrimePatch takes.</para>
        /// </summary>
        internal static bool TryConsume(float amount, string reason)
        {
            if (amount <= 0f) return true;
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return true;

                float pool = ReadPool(gm);
                if (pool < 0f)
                {
                    Plugin.Logger.LogDebug($"[TownWoodPilePatch] Pool unreadable — allowing '{reason}' rather than freezing the hearths.");
                    return true; // fail open
                }

                if (pool < amount)
                {
                    Plugin.Logger.LogDebug($"[TownWoodPilePatch] Pool {pool:0} cannot cover {amount:0} for '{reason}' — skipped, hearth left to burn down.");
                    return false;
                }

                WritePool(gm, pool - amount);
                SyncCardBar(gm, pool - amount);
                // Info: fires only on an actual debit, which is at most once per hearth per drain
                // cycle, so it cannot flood. This is the line that proves the pool is really being
                // spent rather than the hearths being tended for free.
                Plugin.Logger.LogInfo($"[TownWoodPilePatch] Spent {amount:0} on '{reason}' ({pool:0} -> {pool - amount:0}).");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[TownWoodPilePatch] TryConsume('{reason}') failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return true; // fail open — never brick the hearths on an exception
            }
        }

        // ── Action handlers ───────────────────────────────────────────────────────

        /// <summary>Returns true to CANCEL the stock action. See the registration comment.</summary>
        private static bool StockGuardBefore(ActionContext ctx)
        {
            try
            {
                float value = FuelValueOf(ctx.GivenCard);
                if (value <= 0f)
                {
                    Plugin.Logger.LogDebug("[TownWoodPilePatch] Stock cancelled — dragged card carries no usable fuel; not consuming it.");
                    return true;
                }

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return false; // fail open — never block on a missing GameManager

                float pool = ReadPool(gm);
                if (pool < 0f) return false;   // fail open — unreadable stat, let vanilla proceed

                if (pool >= PoolMax)
                {
                    Plugin.Logger.LogDebug($"[TownWoodPilePatch] Stock cancelled — pile already full ({pool:0}/{PoolMax:0}); dragged fuel left intact.");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[TownWoodPilePatch] StockGuardBefore failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false; // fail open
            }
        }

        private static void StockAfter(ActionContext ctx)
        {
            try
            {
                float value = ctx.Tag is float captured ? captured : 0f;
                if (value <= 0f) return; // not a recognized fuel card — nothing credited

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                float pool = ReadPool(gm);
                if (pool < 0f)
                {
                    Plugin.Logger.LogWarning("[TownWoodPilePatch] Fuel stat not readable — stocked fuel was NOT credited.");
                    return;
                }

                float updated = Mathf.Min(pool + value, PoolMax);
                WritePool(gm, updated);
                SyncCardBar(gm, updated);

                CreditPay(ctx.Card, value);

                CardVisualsRefresh.RefreshOpenInventoryPopup();
                // Info, not Debug: this fires only on a deliberate player action (dragging fuel
                // onto the pile), so it cannot flood, and it is the ONLY way to confirm from a log
                // that stocking credits the canonical pool at all. Playthrough item T2.135 has
                // never once been completed (blocked in r31, skipped in r33), so there is still no
                // evidence this path works end to end; without a visible line the next run cannot
                // produce that evidence either.
                Plugin.Logger.LogInfo($"[TownWoodPilePatch] Stocked +{value:0} fuel ({pool:0} -> {updated:0}).");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[TownWoodPilePatch] StockAfter failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>Credits <paramref name="addedValue"/> onto the pile card's own Player's Pay
        /// bar (SpecialDurability2), clamped to <see cref="PayMax"/>.</summary>
        private static void CreditPay(object pileCard, float addedValue)
        {
            float pay = CardUtil.GetDurability(pileCard, "SpecialDurability2");
            if (float.IsNaN(pay)) pay = 0f;
            float updated = Mathf.Min(pay + addedValue, PayMax);
            CardUtil.SetDurability(pileCard, "SpecialDurability2", updated);
        }

        /// <summary>
        /// Withdraws Player's Pay (SpecialDurability2) as currency, paying out every affordable
        /// unit in one click — same idiom as <see cref="MarketStallPatch"/>'s Sales Revenue collect
        /// actions. Reads/writes <c>ctx.Card</c> directly: Player's Pay is never mirrored through a
        /// hidden stat (see class comment), so the card the player clicked IS the canonical value.
        /// Deliberately never touches the Fuel pool — the whole point of splitting Pay out from it.
        /// </summary>
        private static void CollectAfter(ActionContext ctx, float costEach, string payoutGuid, string label)
        {
            try
            {
                float pay = CardUtil.GetDurability(ctx.Card, "SpecialDurability2");
                if (float.IsNaN(pay) || pay < costEach) return;

                int count = (int)(pay / costEach);
                float remainder = pay - count * costEach;
                CardUtil.SetDurability(ctx.Card, "SpecialDurability2", remainder);
                CardVisualsRefresh.RefreshDurabilityVisuals(ctx.Card);

                for (int i = 0; i < count; i++) SpawnService.Spawn(payoutGuid);

                CardVisualsRefresh.RefreshOpenInventoryPopup();
                Plugin.Logger.LogDebug($"[TownWoodPilePatch] Collected {count} {label}(s) (pay was {pay:0}, now {remainder:0}).");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[TownWoodPilePatch] CollectAfter failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Display mirror ────────────────────────────────────────────────────────

        /// <summary>
        /// Keeps the pile card's "Wood Stockpile" bar in step with the canonical stat while the
        /// player is in the village. Cheap early-out everywhere else: outside cmcEnvVillage the
        /// pile card is not on the board at all, so there is nothing to sync (see the class comment
        /// on why the stat, not the bar, is canonical).
        /// </summary>
        private static void OnDtpTick()
        {
            try
            {
                if (GameQuery.CurrentEnvironmentUniqueId != VillageEnvUid) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                float pool = ReadPool(gm);
                if (pool < 0f) return;

                SyncCardBar(gm, pool);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[TownWoodPilePatch] OnDtpTick failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void SyncCardBar(object gm, float pool)
        {
            // Reconcile EVERY matching instance, never the first match only — a UniqueOnBoard card
            // can still end up with two live instances on one board (memory
            // reference_uniqueonboard_duplicate_instance_desync), and a first-match resolver can
            // silently update the orphan while the player stares at the stale one.
            foreach (var card in FindAllPileCards(gm))
            {
                float shown = CardUtil.GetDurability(card, "SpecialDurability1");
                if (!float.IsNaN(shown) && Mathf.Approximately(shown, pool)) continue;

                CardUtil.SetDurability(card, "SpecialDurability1", pool);
                CardVisualsRefresh.RefreshDurabilityVisuals(card);
            }
        }

        // Environment-filtered for the same reason as VillageFireplacePatch.FindAllLiveCards:
        // AllCards is re-seeded on every environment change with cardsRemainingInBG (carried
        // inventory, IndependentFromEnv cards) as well as the current board
        // (.decomp/GameManager.cs:10321-10328), so a bare UID match can return a card that is not
        // in this room. Here that would mean writing the display-mirror bar onto an off-board pile
        // instance. The game's own lookups always pair the UID test with an environment test
        // (.decomp/GameManager.cs:1657-1659).
        private static List<object> FindAllPileCards(object gm)
        {
            var found = new List<object>();
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return found;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                if (CardUtil.GetCardUniqueId(card) != PileUid) continue;
                if (!IsInCurrentEnv(card)) continue;
                found.Add(card);
            }
            return found;
        }

        /// <summary>
        /// True when the card is on the player's current board. Same two-step read the framework's
        /// own <c>GameQuery.IsInPlayerEnv</c> uses (that one is private, so this mirrors it rather
        /// than calling it): <c>CardEnvironment</c> then its <c>MatchesPlayerEnv</c> property.
        /// </summary>
        private static bool IsInCurrentEnv(object card)
        {
            var env = CardUtil.GetMemberValue(card, "CardEnvironment");
            if (env == null) return false;
            return CardUtil.GetMemberValue(env, "MatchesPlayerEnv") is bool inEnv && inEnv;
        }

        // ── Fuel valuation ────────────────────────────────────────────────────────

        /// <summary>
        /// What the dragged card is worth to the pile, in FuelCapacity units.
        ///
        /// <para>Mirrors vanilla's own "Feed Firewood" semantics (<c>GetFuelFromOtherCard</c>): a
        /// card carrying live FuelCapacity donates exactly what it would have given the fire, so a
        /// half-burnt log is worth half a fresh one and Firewood is worth whatever the action that
        /// split it actually put on the card. Only when that read comes back zero/NaN do we fall
        /// back to <see cref="FallbackFuelValues"/>.</para>
        /// </summary>
        private static float FuelValueOf(object card)
        {
            if (card == null) return 0f;
            string uid = CardUtil.GetCardUniqueId(card);
            if (string.IsNullOrEmpty(uid) || !AcceptedFuelUids.Contains(uid)) return 0f;

            float own = CardUtil.GetDurability(card, "FuelCapacity");
            if (!float.IsNaN(own) && own > 0f) return own;

            return FallbackFuelValues.TryGetValue(uid, out float value) ? value : 0f;
        }

        // ── Stat access (the proven StatsDict idiom, as in VillageCrimePatch) ─────

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

        /// <summary>Keyed by stat UID — currently only <see cref="FuelStatUid"/> resolves through
        /// here; Player's Pay lives directly on the card (see class summary), not a GameStat.</summary>
        private static readonly Dictionary<string, object> _statSoCache = new Dictionary<string, object>();

        private static object ResolveStatInstance(object gm, string statUid)
        {
            if (!ResolveGetFromId()) return null;
            if (!_statSoCache.TryGetValue(statUid, out object statSo) || statSo == null)
            {
                statSo = _getFromIdMethod.Invoke(null, new object[] { statUid });
                if (statSo == null) return null;
                _statSoCache[statUid] = statSo;
            }

            if (CardUtil.GetCachedField(gm.GetType(), "StatsDict")?.GetValue(gm) is not IDictionary statsDict) return null;
            if (!statsDict.Contains(statSo)) return null;
            return statsDict[statSo];
        }

        private static float ReadStat(object gm, string statUid)
        {
            var instance = ResolveStatInstance(gm, statUid);
            if (instance == null) return -1f;
            float value = StatAccess.GetCurrentValue(instance);
            return float.IsNaN(value) ? -1f : value;
        }

        private static void WriteStat(object gm, string statUid, float value, float min, float max)
        {
            var instance = ResolveStatInstance(gm, statUid);
            if (instance != null) StatAccess.SetCurrentValue(instance, Mathf.Clamp(value, min, max));
        }

        private static float ReadPool(object gm) => ReadStat(gm, FuelStatUid);

        private static void WritePool(object gm, float value) => WriteStat(gm, FuelStatUid, value, 0f, PoolMax);
    }
}
