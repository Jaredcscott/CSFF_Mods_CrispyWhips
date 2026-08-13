using System;
using System.Collections.Generic;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The village Copper Chests (Village_Master_Plan.md §10.8.3) — one CT2 container inside each
    /// named NPC's own interior that is simultaneously that NPC's savings, their spending power,
    /// and a burglary target. Rolled out to all five residents in CMC 1.46.0; the Miller-only
    /// prototype (1.38.0) was §10.8.3.7's "prove it on one cottage first" step.
    ///
    /// Three mechanics, ONE physical inventory per chest (§10.8.3.1 — a single source of truth by
    /// construction, so robbing a chest also, automatically, makes its owner unable to afford
    /// anything until the next accrual):
    ///
    ///   1. ACCRUAL — <see cref="TryAccrue"/>, called once per resident by whichever patcher already
    ///      owns that NPC's tick (see the ACCRUAL OWNERSHIP table below). It fires the resident's own
    ///      weekly AgentActions with the CHEST as the receiving card instead of the NPC's satchel
    ///      (§10.8.3.3 "retarget, don't duplicate" — R6). The caps live on <see cref="ChestConfig"/>
    ///      because they are read off the chest's live contents, not off a tracked number.
    ///   2. SELL — a drag-and-drop CardInteraction on each chest ("Sell to the &lt;NPC&gt;"). Priced
    ///      with MarketStallPatch's own ObjectWeight/10 formula so an item is worth the same at
    ///      the player's stall and at any NPC's chest. Afford-gated on <see cref="CurrentWealth"/>
    ///      via ActionTiming.Cancel (InnPatch.BalanceGate's shape), paid out of the chest's own
    ///      currency via ActionTiming.AfterWrapped.
    ///   3. THEFT — a "Search for valuables" DismantleAction that empties the chest to the player
    ///      and rolls for detection. Detected -> cmcStatVillageCrime +10 (VillageCrimePatch.AddCrime).
    ///
    /// <para><b>ACCRUAL OWNERSHIP</b> — each resident's weekly drop is driven by the patcher that
    /// already owns that NPC's poll, so no new spawn mechanism is introduced anywhere:
    /// Miller + Weaver -> CottageResidentSpawnPatch.CheckRestock; Apothecary ->
    /// ApothecarySchedulePatch.RunScheduler; Inn Keeper -> InnKeeperSpawnPatch.CheckRestock;
    /// Professor -> ProfessorSchedulePatch.RunScheduler. Each passes its OWN proven FireAgentAction
    /// in as the <paramref name="fireAgentAction"/> delegate. Exactly one caller per config — a
    /// second caller would be the R6 double-drop bug.</para>
    ///
    /// <para><b>Why the DA needs C#</b>: a DismantleAction's static JSON cannot express "whatever
    /// happens to be inside right now", and ReceivingCardChanges.CardsToCreate is not processed on
    /// DismantleActions at all (root CLAUDE.md). DaytimeCost 4 + AlwaysShow keep it past
    /// CanAppear()/WillHaveAnEffect() so the button actually renders.</para>
    ///
    /// <para><b>Self-contained home detection</b>: <see cref="IsOwnerHome"/> resolves the resident's
    /// live NPC and compares its CurrentEnvironment against the chest's own interior, rather than
    /// calling CottageResidentSchedulePatch.IsResidentHome — that helper only knows the two cottage
    /// residents, so a shared call would have silently returned "nobody home" (never an instant
    /// catch) for the Apothecary, Inn Keeper and Professor.</para>
    /// </summary>
    internal static class CopperChestPatch
    {
        /// <summary>
        /// Everything that differs between the five chests. Adding a sixth resident is a new entry
        /// in <see cref="Chests"/> plus one <see cref="TryAccrue"/> call from that NPC's own poll —
        /// no changes anywhere else in this file.
        /// </summary>
        internal sealed class ChestConfig
        {
            /// <summary>Log label and possessive used in player-facing log lines.</summary>
            public string Name;

            public string ChestUid;
            /// <summary>The chest's own interior env. Doubles as "home" for the theft roll.</summary>
            public string InteriorEnvUid;
            public string AgentUid;

            /// <summary>AgentActions[].ActionID dropping currency into the chest.</summary>
            public string CurrencyActionId;
            /// <summary>AgentActions[].ActionID dropping goods into the chest.</summary>
            public string GoodsActionId;

            /// <summary>Hidden GameStat holding the absolute day of the last accrual.</summary>
            public string RestockDayStat;
            /// <summary>Hidden GameStat: undetected thefts of THIS chest this season.</summary>
            public string TheftsStat;
            /// <summary>Hidden GameStat: the season <see cref="TheftsStat"/> was last written in.</summary>
            public string TheftSeasonStat;

            // Two-tier action identity (CLAUDE.md §Harmony Patching Pitfalls): the LocalizationKey is
            // the primary match, the DefaultText a fallback if the key is ever stripped. Both must
            // stay in step with this chest's CardData/Location/*.json and SimpEn.csv. The KEYS are
            // per-chest and therefore unique; the Search DA's DefaultText is deliberately the same
            // flavour-neutral "Search for valuables" on every chest (§10.8.3.6 wants a non-spoiler
            // label), which is safe because dispatch also matches CardUid and no two chests can
            // ever share a board (each is UniqueOnBoard inside its own interior).
            public string SellActionKey;
            public string SellActionName;
            public string SearchActionKey;
            public string SearchActionName;

            /// <summary>Live-summed currency ceiling (§10.8.3.3). Tiered per NPC: the Inn Keeper is
            /// the richest buyer in the village, Miller/Weaver mid, Apothecary/Professor thinner
            /// purses that make up for it in rarer goods. PLACEHOLDERS pending §10.8.10's tuning
            /// pass, same as the Miller's original 300.</summary>
            public float CurrencyCap;

            /// <summary>Separate goods ceiling, counted in cards rather than value so a heavy stack
            /// can't crowd the currency out of the chest's ten slots.</summary>
            public int GoodsCap;
        }

        // Weekly cadence, shared by every chest. Matches CottageResidentSpawnPatch's own satchel
        // restock interval so a resident with a chest accrues on the same rhythm they used to.
        private const int RestockIntervalDays = 7;

        internal static readonly ChestConfig[] Chests =
        {
            // Salt is 15 value (CurrencyValue.SaltValue), so these caps are ~6-8 weeks of accrual
            // from empty on every chest — the tiering changes the CEILING and the goods mix, not
            // the pacing, so no NPC feels dead for months while another fills in a fortnight.
            new ChestConfig
            {
                Name = "Miller",
                ChestUid = "cmcCopperChestMiller",
                InteriorEnvUid = "cmcMillerCottageInterior",
                AgentUid = "cmcMillerAgent",
                CurrencyActionId = "MillerChestCurrency",
                GoodsActionId = "MillerChestGoods",
                RestockDayStat = "cmcStatMillerChestRestockDay",
                TheftsStat = "cmcStatMillerChestThefts",
                TheftSeasonStat = "cmcStatMillerChestTheftSeason",
                SellActionKey = "CMC_CopperChestMiller_CI_Sell",
                SellActionName = "Sell to the Miller",
                SearchActionKey = "CMC_CopperChestMiller_DA_Search",
                SearchActionName = "Search for valuables",
                CurrencyCap = 300f,   // 20 Salt; 3 Salt/week -> ~7 weeks from empty
                GoodsCap = 10,
            },
            new ChestConfig
            {
                Name = "Weaver",
                ChestUid = "cmcCopperChestWeaver",
                InteriorEnvUid = "cmcWeaverCottageInterior",
                AgentUid = "cmcWeaverAgent",
                CurrencyActionId = "WeaverChestCurrency",
                GoodsActionId = "WeaverChestGoods",
                RestockDayStat = "cmcStatWeaverChestRestockDay",
                TheftsStat = "cmcStatWeaverChestThefts",
                TheftSeasonStat = "cmcStatWeaverChestTheftSeason",
                SellActionKey = "CMC_CopperChestWeaver_CI_Sell",
                SellActionName = "Sell to the Weaver",
                SearchActionKey = "CMC_CopperChestWeaver_DA_Search",
                SearchActionName = "Search for valuables",
                CurrencyCap = 300f,   // same working-trade tier as the Miller
                GoodsCap = 10,
            },
            new ChestConfig
            {
                Name = "Apothecary",
                ChestUid = "cmcCopperChestApothecary",
                InteriorEnvUid = "cmcApothecaryCabinInterior",
                AgentUid = "cmcApothecaryAgent",
                CurrencyActionId = "ApothecaryChestCurrency",
                GoodsActionId = "ApothecaryChestGoods",
                RestockDayStat = "cmcStatApothecaryChestRestockDay",
                TheftsStat = "cmcStatApothecaryChestThefts",
                TheftSeasonStat = "cmcStatApothecaryChestTheftSeason",
                SellActionKey = "CMC_CopperChestApothecary_CI_Sell",
                SellActionName = "Sell to the Apothecary",
                SearchActionKey = "CMC_CopperChestApothecary_DA_Search",
                SearchActionName = "Search for valuables",
                CurrencyCap = 180f,   // 12 Salt; 2 Salt/week -> ~6 weeks. Thin purse, rarer goods.
                GoodsCap = 8,
            },
            new ChestConfig
            {
                Name = "Inn Keeper",
                ChestUid = "cmcCopperChestInnKeeper",
                InteriorEnvUid = "cmcInnInterior",
                AgentUid = "cmcInnKeeperAgent",
                CurrencyActionId = "InnKeeperChestCurrency",
                GoodsActionId = "InnKeeperChestGoods",
                RestockDayStat = "cmcStatInnKeeperChestRestockDay",
                TheftsStat = "cmcStatInnKeeperChestThefts",
                TheftSeasonStat = "cmcStatInnKeeperChestTheftSeason",
                SellActionKey = "CMC_CopperChestInnKeeper_CI_Sell",
                SellActionName = "Sell to the Inn Keeper",
                SearchActionKey = "CMC_CopperChestInnKeeper_DA_Search",
                SearchActionName = "Search for valuables",
                CurrencyCap = 500f,   // richest buyer in the village; 5 Salt/week -> ~7 weeks
                GoodsCap = 10,
            },
            new ChestConfig
            {
                Name = "Professor",
                ChestUid = "cmcCopperChestProfessor",
                InteriorEnvUid = "cmcAcademyInterior",
                AgentUid = "cmcProfessorAgent",
                CurrencyActionId = "ProfessorChestCurrency",
                GoodsActionId = "ProfessorChestGoods",
                RestockDayStat = "cmcStatProfessorChestRestockDay",
                TheftsStat = "cmcStatProfessorChestThefts",
                TheftSeasonStat = "cmcStatProfessorChestTheftSeason",
                SellActionKey = "CMC_CopperChestProfessor_CI_Sell",
                SellActionName = "Sell to the Professor",
                SearchActionKey = "CMC_CopperChestProfessor_DA_Search",
                SearchActionName = "Search for valuables",
                CurrencyCap = 180f,   // a scholar's private savings; specimens carry the value
                GoodsCap = 8,
            },
        };

        /// <summary>The chest config owned by <paramref name="agentUid"/>, or null if that NPC has
        /// no chest. Callers use this to opt IN to driving accrual — a null return means this file
        /// knows nothing about that NPC and no accrual should be attempted.</summary>
        internal static ChestConfig ForAgent(string agentUid)
        {
            if (string.IsNullOrEmpty(agentUid)) return null;
            foreach (var cfg in Chests)
                if (string.Equals(cfg.AgentUid, agentUid, StringComparison.OrdinalIgnoreCase)) return cfg;
            return null;
        }

        /// <summary>The config for a chest card UniqueID, or null.</summary>
        internal static ChestConfig ForChest(string chestUid)
        {
            if (string.IsNullOrEmpty(chestUid)) return null;
            foreach (var cfg in Chests)
                if (string.Equals(cfg.ChestUid, chestUid, StringComparison.OrdinalIgnoreCase)) return cfg;
            return null;
        }

        /// <summary>Detection chance when the owner is out, before modifiers. PLACEHOLDER —
        /// §10.8.3.6 flags 15% as unconfirmed pending the owner's tuning pass (§10.8.10).</summary>
        private const float BaseDetectionChance = 0.15f;

        /// <summary>Added per PRIOR undetected theft of THIS chest in the current season, so
        /// repeat burglary of the same target stops being risk-free (§10.8.3.6). Heat is tracked
        /// per chest — five chests must not share one counter, or robbing the Miller would
        /// endanger a later visit to the Academy.</summary>
        private const float HeatPerPriorTheft = 0.05f;

        /// <summary>Cap on the accumulated heat bonus, so the roll never becomes a certainty
        /// on its own (an owner standing right there is the only guaranteed catch).</summary>
        private const float MaxHeatBonus = 0.40f;

        /// <summary>Added while Iris Vane's night watch is out (§10.8.3.6, placeholder +20%).</summary>
        private const float NightPatrolBonus = 0.20f;

        private const string NightGuardAgentUid = "cmcGuardVaneAgent";

        // Mirrors GuardDutyPatch's own (private) Iris Vane shift window, 20:00-04:59. See
        // NightPatrolActive's doc comment for why this is duplicated rather than referenced.
        private const float NightShiftStartHour = 20f;
        private const float NightShiftEndHour = 5f;

        /// <summary>Crime points a detected burglary costs (§10.8.2 point table).</summary>
        private const float DetectedCrimePoints = 10f;

        private static bool _initialized;

        // ── Registration ──────────────────────────────────────────────────────────

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (CardUtil.FindGameType("InGameCardBase") == null)
            {
                Plugin.Logger.LogWarning("[CopperChestPatch] InGameCardBase type not found — Copper Chest mechanics inactive.");
                return;
            }

            // One set of three handlers PER CHEST. ActionRouter dispatches on an exact
            // (case-insensitive) CardUid match, so a handler registered for the Miller's chest can
            // never fire on the Professor's — this loop is purely data-driven, no new mechanics.
            foreach (var config in Chests)
            {
                var cfg = config; // explicit capture — these lambdas outlive the loop

                // Registration order matters, exactly as in InnPatch: the afford-gate must be able
                // to short-circuit before the payout handler runs, so the dragged item is never
                // consumed for a sale the NPC cannot pay for.
                ActionRouter.Register(new ActionHandler
                {
                    Name = $"CopperChestSellGate:{cfg.Name}",
                    CardUid = cfg.ChestUid,
                    ActionKeyPrefix = cfg.SellActionKey,
                    ActionNamePrefix = cfg.SellActionName,
                    Timing = ActionTiming.Cancel,
                    Before = ctx => SellGate(cfg, ctx),
                });

                ActionRouter.Register(new ActionHandler
                {
                    Name = $"CopperChestSellPayout:{cfg.Name}",
                    CardUid = cfg.ChestUid,
                    ActionKeyPrefix = cfg.SellActionKey,
                    ActionNamePrefix = cfg.SellActionName,
                    Timing = ActionTiming.AfterWrapped,
                    // The price MUST be read in the prefix: the CI's own GivenCardChanges.ModType 3
                    // destroys the dragged card as part of the action, so by the time After runs
                    // ctx.GivenCard's CardModel may already be gone and PriceOf would silently fall
                    // back to 1 (InnPatch's firewood handler captures SpecialDurability4 for exactly
                    // this reason).
                    Before = ctx => { ctx.Tag = PriceOf(ctx.GivenCard); return false; },
                    After = ctx => SellPayout(cfg, ctx),
                });

                ActionRouter.Register(new ActionHandler
                {
                    Name = $"CopperChestSearch:{cfg.Name}",
                    CardUid = cfg.ChestUid,
                    ActionKeyPrefix = cfg.SearchActionKey,
                    ActionNamePrefix = cfg.SearchActionName,
                    Timing = ActionTiming.AfterWrapped,
                    After = ctx => SearchAfter(cfg, ctx),
                });
            }

            Plugin.Logger.LogDebug($"[CopperChestPatch] initialized for {Chests.Length} chest(s).");
        }

        // ── Live-summed wealth (§10.8.3.4) ────────────────────────────────────────

        /// <summary>
        /// An NPC's current spending power: CurrencyValue.ValueOf summed over every currency card
        /// physically inside their chest, recomputed on every call. Deliberately NOT mirrored into
        /// a stat — a second source of truth can drift from the chest's actual contents, and the
        /// whole point of §10.8.3.1's one-container design is that it cannot.
        /// </summary>
        public static float CurrentWealth(object chestCard)
        {
            if (chestCard == null) return 0f;
            float total = 0f;
            foreach (var card in Inventory.Cards(chestCard))
                total += CurrencyValue.ValueOf(card);
            return total;
        }

        /// <summary>Number of NON-currency cards in the chest — the goods half of the accrual cap.</summary>
        public static int GoodsCount(object chestCard)
        {
            if (chestCard == null) return 0;
            int count = 0;
            foreach (var card in Inventory.Cards(chestCard))
                if (CurrencyValue.ValueOf(card) <= 0f) count++;
            return count;
        }

        /// <summary>The live chest card for a config, or null when the player is not standing in
        /// that chest's interior. CardFinder scans live scene objects only, and cards belonging to
        /// any environment other than the player's current one are serialized out rather than live
        /// (memory: reference_allcards_env_scoped) — so this is null almost everywhere.</summary>
        public static object FindLiveChest(ChestConfig cfg) => cfg == null ? null : CardFinder.Find(cfg.ChestUid);

        // ── Weekly accrual (§10.8.3.3) ────────────────────────────────────────────

        // Warn once per missing ActionID rather than once per qualifying visit — a mod shipped with
        // a typo'd AgentAction should say so, but not every 30 s for the rest of the run.
        private static readonly HashSet<string> _missingActionWarned = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// One week's Copper Chest accrual for <paramref name="cfg"/>'s owner, if it is due and the
        /// player is standing in that chest's interior. Called by whichever patcher already owns
        /// that NPC's poll (see the ACCRUAL OWNERSHIP note on the class), passing its OWN proven
        /// FireAgentAction as <paramref name="fireAgentAction"/> — this method deliberately owns no
        /// spawn mechanism of its own, only the gating.
        ///
        /// <para>Why this is gated on the PLAYER's location, unlike a satchel restock: an NPC's
        /// satchel follows the NPC and is reachable from anywhere, but a chest is a board-bound card
        /// in an interior. CardFinder scans live scene objects, and GameManager.ChangeEnvironment
        /// serialises every non-current environment's cards out of the scene
        /// (memory: reference_allcards_env_scoped), so CardFinder.Find returns null for the chest
        /// unless the player is standing inside that interior at that instant. Firing the drop from
        /// a location-independent poll would therefore silently no-op almost every week. Instead the
        /// accrual is deferred to a visit and latched into a persistent GameStat, the same
        /// defer-poll-latch shape as reference_spawn_targets_current_board.</para>
        ///
        /// <para>One week's worth per qualifying visit, deliberately NOT a catch-up for every missed
        /// week: the caps already bound the total, so banking owed weeks would add state for no
        /// reachable outcome.</para>
        /// </summary>
        /// <param name="fireAgentAction">(npc, receivingCard, actionId) -> fired. The caller's own
        /// ToAction+PerformAction helper; returns false when the ActionID isn't on that agent.</param>
        /// <returns>True when something was actually dropped this call.</returns>
        public static bool TryAccrue(ChestConfig cfg, object npc, int currentDay, Func<object, object, string, bool> fireAgentAction)
        {
            if (cfg == null || npc == null || fireAgentAction == null) return false;

            if (!string.Equals(GameQuery.CurrentEnvironmentUniqueId, cfg.InteriorEnvUid, StringComparison.OrdinalIgnoreCase))
                return false;

            var chest = CardFinder.Find(cfg.ChestUid);
            if (chest == null) return false; // interior entered but the chest hasn't spawned yet

            float lastDay = HiddenStat.Get(cfg.RestockDayStat);
            if (lastDay < 0f) return false;                                          // stat not readable yet
            if (lastDay > 0.5f && currentDay - lastDay < RestockIntervalDays) return false; // not due

            // Each half pauses independently once its own ceiling is reached (§10.8.3.3: "the
            // weekly currency drop pauses; goods accrual may continue up to its own separate cap").
            bool currencyDue = CurrentWealth(chest) < cfg.CurrencyCap;
            bool goodsDue = GoodsCount(chest) < cfg.GoodsCap;

            if (currencyDue && !fireAgentAction(npc, chest, cfg.CurrencyActionId))
                WarnMissingAction(cfg, cfg.CurrencyActionId, "currency");
            if (goodsDue && !fireAgentAction(npc, chest, cfg.GoodsActionId))
                WarnMissingAction(cfg, cfg.GoodsActionId, "goods");

            // Stamped even when both halves are capped, so a full chest doesn't re-check on every
            // poll for the rest of the visit. Floored at 1 so day 0 can't read back as "never".
            HiddenStat.Set(cfg.RestockDayStat, Math.Max(1, currentDay));

            if (currencyDue || goodsDue)
            {
                Plugin.Logger.LogInfo($"[CopperChestPatch] The {cfg.Name} added to the Copper Chest (day {currentDay}; currency {(currencyDue ? "yes" : "capped")}, goods {(goodsDue ? "yes" : "capped")}).");
                return true;
            }
            return false;
        }

        private static void WarnMissingAction(ChestConfig cfg, string actionId, string half)
        {
            if (!_missingActionWarned.Add(actionId)) return;
            Plugin.Logger.LogWarning($"[CopperChestPatch] '{actionId}' not found on {cfg.AgentUid}'s AgentActions — chest {half} accrual inactive for the {cfg.Name}.");
        }

        // ── Sell CI (§10.8.3.5) ───────────────────────────────────────────────────

        /// <summary>MarketStallPatch.GetItemValue's formula, minus the player's own stall-renown
        /// multiplier (that is a reward on the player's stall, not on an NPC's purse).</summary>
        private static float PriceOf(object card)
        {
            var cardData = CardUtil.GetCardData(card);
            if (cardData == null) return 1f;
            int weight = Reflect.GetInt(cardData, "ObjectWeight");
            return Math.Max(1f, weight / 10f);
        }

        // Return true = cancel the action entirely; the dragged card is never consumed.
        private static bool SellGate(ChestConfig cfg, ActionContext ctx)
        {
            if (ctx.GivenCard == null) return false; // nothing dragged — let vanilla handle it

            float price = PriceOf(ctx.GivenCard);
            float wealth = CurrentWealth(ctx.Card);
            bool affordable = wealth >= price;
            if (!affordable)
                Plugin.Logger.LogDebug($"[CopperChestPatch] Sale refused — price {price:0} exceeds the {cfg.Name}'s chest wealth {wealth:0}.");
            return !affordable;
        }

        private static void SellPayout(ChestConfig cfg, ActionContext ctx)
        {
            try
            {
                float price = ctx.Tag is float captured ? captured : PriceOf(ctx.GivenCard);
                if (price <= 0f) return;

                int paid = PayFromChest(ctx.Card, price, out float paidValue);
                if (paid == 0)
                {
                    Plugin.Logger.LogWarning($"[CopperChestPatch] Sale completed but no currency could be drawn from the {cfg.Name}'s chest (price {price:0}) — the item was consumed for nothing.");
                    return;
                }

                CardVisualsRefresh.RefreshOpenInventoryPopup();
                Plugin.Logger.LogInfo($"[CopperChestPatch] Sold an item to the {cfg.Name} for {price:0}; handed over {paid} currency card(s) worth {paidValue:0}. Chest wealth now {CurrentWealth(ctx.Card):0}.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CopperChestPatch] SellPayout failed for the {cfg.Name}: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// Draws currency out of the chest smallest-value-first until the running total covers
        /// <paramref name="price"/>, and hands those cards to the player. Smallest-first (not
        /// largest-first) so the owner doesn't surrender their one big nugget for a cheap trinket.
        /// Overshoot is accepted rather than blocking the sale when the mix can't make exact
        /// change (§10.8.9 R15) — this mod rounds in the player's favour everywhere else too.
        /// </summary>
        private static int PayFromChest(object chest, float price, out float paidValue)
        {
            paidValue = 0f;
            var currency = new List<(object Card, float Value)>();
            foreach (var card in Inventory.Cards(chest))
            {
                float value = CurrencyValue.ValueOf(card);
                if (value > 0f) currency.Add((card, value));
            }
            if (currency.Count == 0) return 0;

            currency.Sort((a, b) => a.Value.CompareTo(b.Value));

            var payout = new List<object>();
            foreach (var entry in currency)
            {
                payout.Add(entry.Card);
                paidValue += entry.Value;
                if (paidValue >= price) break;
            }

            int handed = 0;
            foreach (var card in payout)
                if (GiveToPlayer(chest, card)) handed++;
            return handed;
        }

        // ── Theft DA (§10.8.3.6) ──────────────────────────────────────────────────

        private static void SearchAfter(ChestConfig cfg, ActionContext ctx)
        {
            try
            {
                var chest = ctx.Card;
                if (chest == null) return;

                var contents = Inventory.Cards(chest);
                int taken = 0;
                foreach (var card in contents)
                    if (GiveToPlayer(chest, card)) taken++;

                CardVisualsRefresh.RefreshOpenInventoryPopup();

                if (taken == 0)
                {
                    // An empty chest is not a crime — nothing was actually stolen, so no roll.
                    Plugin.Logger.LogDebug($"[CopperChestPatch] Search found the {cfg.Name}'s chest empty — no detection roll.");
                    return;
                }

                bool caught = RollDetection(cfg, out string reason);
                if (caught)
                {
                    SetHeat(cfg, 0f); // the heat has been spent — a caught burglar starts the ladder over
                    VillageCrimePatch.AddCrime(DetectedCrimePoints);
                    Plugin.Logger.LogInfo($"[CopperChestPatch] The {cfg.Name}'s chest was robbed of {taken} card(s) and the theft was DETECTED ({reason}).");
                }
                else
                {
                    SetHeat(cfg, CurrentHeatCount(cfg) + 1f);
                    // Undetected theft leaves no record at all (§10.8.3.6) — Debug only, so a
                    // player reading LogOutput.log can't use it as an oracle.
                    Plugin.Logger.LogDebug($"[CopperChestPatch] The {cfg.Name}'s chest was robbed of {taken} card(s), undetected ({reason}).");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CopperChestPatch] SearchAfter failed for the {cfg.Name}: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool RollDetection(ChestConfig cfg, out string reason)
        {
            // Instant catch: the owner is standing in the very room being robbed.
            if (IsOwnerHome(cfg))
            {
                reason = $"the {cfg.Name} was home";
                return true;
            }

            float heatBonus = Math.Min(MaxHeatBonus, CurrentHeatCount(cfg) * HeatPerPriorTheft);
            float patrolBonus = NightPatrolActive() ? NightPatrolBonus : 0f;

            float chance = Math.Min(0.95f, BaseDetectionChance + heatBonus + patrolBonus);
            bool caught = UnityEngine.Random.value < chance;
            reason = $"roll {chance:P0} (base {BaseDetectionChance:P0} + heat {heatBonus:P0} + patrol {patrolBonus:P0})";
            return caught;
        }

        /// <summary>
        /// True when this chest's owner is currently standing inside this chest's own interior.
        ///
        /// <para>Self-contained by design: the older Miller-only build delegated to
        /// CottageResidentSchedulePatch.IsResidentHome, which only knows the two cottage residents
        /// and would have returned a flat false — i.e. never an instant catch — for the Apothecary,
        /// Inn Keeper and Professor. The comparison itself is the same one that helper makes: the
        /// NPC's ACTUAL CurrentEnvironment, not their scheduled destination, so a resident mid-
        /// commute has not arrived yet and can't catch anyone.</para>
        ///
        /// <para>Matched by UniqueID string rather than SO reference: a duplicated CardData/NPCAgent
        /// instance for the same UID (root CLAUDE.md § Pikachu ModLoader Coexistence) would make a
        /// reference-only check silently and permanently false. Returns false whenever anything is
        /// unresolvable — a broken query must not manufacture a crime
        /// (feedback_subsystem_graceful_degradation).</para>
        /// </summary>
        private static bool IsOwnerHome(ChestConfig cfg)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return false;
                if (Reflect.GetMember(gm, "AllNPCs") is not System.Collections.IEnumerable allNpcs) return false;

                foreach (var npc in allNpcs)
                {
                    if (npc == null) continue;
                    var model = Reflect.GetMember(npc, "NPCModel");
                    if (model == null) continue;
                    if (!string.Equals(Reflect.GetMember(model, "UniqueID") as string, cfg.AgentUid, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var env = Reflect.GetMember(npc, "CurrentEnvironment");
                    if (env == null || Reflect.GetMember(env, "IsNull") is true) return false;
                    string envUid = CardUtil.GetCardUniqueId(Reflect.GetMember(env, "EnvCard"));
                    return string.Equals(envUid, cfg.InteriorEnvUid, StringComparison.OrdinalIgnoreCase);
                }
                return false; // hasn't spawned/moved in — nobody home by definition
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[CopperChestPatch] IsOwnerHome('{cfg.AgentUid}') failed (treated as away): {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }

        /// <summary>
        /// True while Iris Vane's night watch is actually running: her shift hours AND her NPC
        /// existing in the run. Both halves matter — on a save where the Town Watch never spawned
        /// there is no patrol to be caught by, and a bare clock check would penalise the player for
        /// a guard who isn't there.
        ///
        /// <para>Deliberately duplicates the shift window rather than reading GuardDutyPatch's
        /// private constant: that file is under active development in a parallel workstream and
        /// exposes no on-duty query yet. TODO(guards): replace this with a proper
        /// GuardDutyPatch.IsOnDuty(agentUid) / nearest-guard-Suspicion lookup once that API
        /// settles, so the modifier tracks where a guard actually IS rather than only the clock —
        /// and keep <see cref="NightShiftStartHour"/>/<see cref="NightShiftEndHour"/> in step with
        /// GuardDutyPatch until then.</para>
        /// </summary>
        private static bool NightPatrolActive()
        {
            try
            {
                float hour = GameQuery.HourOfDay;
                if (hour < 0f) return false;
                bool onShift = hour >= NightShiftStartHour || hour < NightShiftEndHour;
                if (!onShift) return false;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return false;
                if (Reflect.GetMember(gm, "AllNPCs") is not System.Collections.IEnumerable allNpcs) return false;

                // Matched by UniqueID string rather than SO reference (CardExistsAnywhere's idiom),
                // so this needs no GetFromID resolution and no handle on the guard's own SO — and
                // therefore no coupling to GuardSpawnPatch beyond one content UID.
                foreach (var npc in allNpcs)
                {
                    if (npc == null) continue;
                    var model = Reflect.GetMember(npc, "NPCModel");
                    if (model == null) continue;
                    if (Reflect.GetMember(model, "UniqueID") as string == NightGuardAgentUid) return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[CopperChestPatch] NightPatrolActive check failed (treated as no patrol): {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }

        // ── Seasonal "heat" counter (per chest) ───────────────────────────────────

        /// <summary>Prior UNDETECTED thefts of THIS chest in the current season. Reading it also
        /// performs the season rollover check, so the counter resets exactly once per season
        /// without needing its own tick subscription.</summary>
        private static float CurrentHeatCount(ChestConfig cfg)
        {
            int season = SeasonIndex();
            float storedSeason = HiddenStat.Get(cfg.TheftSeasonStat);
            float heat = HiddenStat.Get(cfg.TheftsStat);
            if (heat < 0f) return 0f; // stat unreadable — treat as no heat rather than guessing

            // season == 0 means GameQuery couldn't resolve the season this frame; don't reset on
            // an unknown, or a transient null would silently clear the player's accumulated heat.
            if (season > 0 && Math.Abs(storedSeason - season) > 0.001f && heat > 0f)
            {
                HiddenStat.Set(cfg.TheftsStat, 0f);
                HiddenStat.Set(cfg.TheftSeasonStat, season);
                Plugin.Logger.LogDebug($"[CopperChestPatch] Season changed — the {cfg.Name}'s chest theft heat reset to 0.");
                return 0f;
            }
            return heat;
        }

        private static void SetHeat(ChestConfig cfg, float value)
        {
            HiddenStat.Set(cfg.TheftsStat, Math.Max(0f, value));
            int season = SeasonIndex();
            if (season > 0) HiddenStat.Set(cfg.TheftSeasonStat, season);
        }

        // GameQuery.CurrentSeason is a NAME keyed off the run's SeasonID, not an enum ordinal, and
        // returns null before the season SO resolves (memory: reference_gamequery_currentseason_null)
        // — 0 means "unknown this frame", which callers treat as "don't roll the season over".
        private static int SeasonIndex()
        {
            string season = GameQuery.CurrentSeason;
            if (string.IsNullOrEmpty(season)) return 0;
            if (season.Equals("Spring", StringComparison.OrdinalIgnoreCase)) return 1;
            if (season.Equals("Summer", StringComparison.OrdinalIgnoreCase)) return 2;
            if (season.Equals("Autumn", StringComparison.OrdinalIgnoreCase)) return 3;
            if (season.Equals("Winter", StringComparison.OrdinalIgnoreCase)) return 4;
            Plugin.Logger.LogDebug($"[CopperChestPatch] Unrecognized season name '{season}' — theft heat rollover skipped this call.");
            return 0;
        }

        // ── Chest -> player card movement ─────────────────────────────────────────

        private static bool _slotStaticsResolved;
        private static PropertyInfo _graphicsInstanceProperty;
        private static Type _graphicsManagerType;
        private static MethodInfo _getSlotForCardMethod;   // GraphicsManager.GetSlotForCard(CardData, CardData, SlotInfo, bool)
        private static MethodInfo _assignCardMethod;       // DynamicLayoutSlot.AssignCard(InGameCardBase, bool)
        private static bool _transferFallbackLogged;

        /// <summary>
        /// Moves one card out of a chest and into the player's hands.
        ///
        /// <para>Preferred path is a REAL transfer of the same physical card instance —
        /// <c>GraphicsManager.GetSlotForCard(...).AssignCard(card)</c>, which is vanilla's own
        /// container-spills-its-contents idiom (GameManager.RemoveCard, RemoveOption.Standard).
        /// DynamicLayoutSlot.AssignCard -> InGameCardBase.SetSlot performs the
        /// RemoveCardFromInventory + SetCurrentContainer(null) + reparent for us, so the player
        /// visibly receives the merchant's own hoarded coin rather than freshly conjured currency
        /// (§10.8.3.5 step 3).</para>
        ///
        /// <para>Fallback is the proven spawn-eject pattern (Api.Inventory.Eject +
        /// SpawnService.Spawn — MarketStallPatch's shape). It preserves the ECONOMICS exactly
        /// (the chest is drained, an equivalent card reaches the player) but not instance
        /// identity, and a respawned metal Nugget/Coin reverts to its default metal type because
        /// GiveCard returns void in this game version and stat overrides cannot be applied to the
        /// spawn (SpawnService's own documented gap). That only ever affects currency the PLAYER
        /// stashed in a chest — accrual deposits Salt, which is flat-valued.</para>
        /// </summary>
        private static bool GiveToPlayer(object chest, object card)
        {
            if (chest == null || card == null) return false;

            if (TryTransferInstance(chest, card)) return true;

            // Ambiguity guard: AssignCard may have relocated the card even though the
            // CurrentContainer probe couldn't confirm it. Falling back unconditionally could then
            // spawn a SECOND copy of a card that already left. Only take the fallback when the
            // card is provably still sitting in the chest.
            if (!IsInChest(chest, card)) return true;

            string uid = CardUtil.GetCardUniqueId(card);
            if (uid == null) return false;
            if (Inventory.Eject(chest, new[] { card }) == 0) return false;
            SpawnService.Spawn(uid);

            if (!_transferFallbackLogged)
            {
                _transferFallbackLogged = true;
                Plugin.Logger.LogInfo("[CopperChestPatch] Direct card transfer unavailable — falling back to eject-and-respawn for chest payouts (see class doc).");
            }
            return true;
        }

        private static bool IsInChest(object chest, object card)
        {
            foreach (var c in Inventory.Cards(chest))
                if (ReferenceEquals(c, card)) return true;
            return false;
        }

        private static bool TryTransferInstance(object chest, object card)
        {
            try
            {
                if (!ResolveSlotStatics()) return false;

                var graphics = _graphicsInstanceProperty?.GetValue(null)
                    ?? UnityEngine.Object.FindObjectOfType(_graphicsManagerType);
                if (graphics == null) return false;

                var cardData = CardUtil.GetCardData(card);
                if (cardData == null) return false;
                var liquidModel = Reflect.GetMember(card, "ContainedLiquidModel");
                var chestSlotInfo = Reflect.GetMember(chest, "CurrentSlotInfo");
                if (chestSlotInfo == null) return false;

                var slot = _getSlotForCardMethod.Invoke(graphics, new[] { cardData, liquidModel, chestSlotInfo, (object)false });
                if (slot == null) return false;

                _assignCardMethod.Invoke(slot, new[] { card, (object)true });

                // AssignCard silently refuses incompatible/occupied slots, so confirm the card
                // actually left the chest before reporting success — "Invoke didn't throw" is not
                // evidence the move happened (the same lesson QuickTransfer's pile-count check
                // encodes). The cast is deliberate: a DESTROYED UnityEngine.Object is not C#-null
                // under an object-typed comparison (CLAUDE.md §Harmony Patching Pitfalls).
                var container = Reflect.GetMember(card, "CurrentContainer");
                bool stillContained = container is UnityEngine.Object containerObj && containerObj != null;
                return !stillContained;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[CopperChestPatch] TryTransferInstance failed for '{CardUtil.GetCardUniqueId(card) ?? "?"}': {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }

        private static bool ResolveSlotStatics()
        {
            if (_slotStaticsResolved) return _getSlotForCardMethod != null && _assignCardMethod != null;
            _slotStaticsResolved = true;

            // Wrapped: AccessTools.Method can throw AmbiguousMatchException on an overloaded name,
            // and a throw here would surface as a failed payout rather than a quiet downgrade to
            // the proven fallback.
            try
            {
                _graphicsManagerType = AccessTools.TypeByName("GraphicsManager");
                if (_graphicsManagerType == null)
                {
                    Plugin.Logger.LogDebug("[CopperChestPatch] GraphicsManager type not found — chest payouts will use the eject-and-respawn fallback.");
                    return false;
                }
                _graphicsInstanceProperty = _graphicsManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                _getSlotForCardMethod = AccessTools.Method(_graphicsManagerType, "GetSlotForCard");

                var slotType = AccessTools.TypeByName("DynamicLayoutSlot");
                _assignCardMethod = slotType == null ? null : AccessTools.Method(slotType, "AssignCard");
            }
            catch (Exception ex)
            {
                _getSlotForCardMethod = null;
                _assignCardMethod = null;
                Plugin.Logger.LogDebug($"[CopperChestPatch] Slot-transfer reflection failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }

            if (_getSlotForCardMethod == null || _assignCardMethod == null)
                Plugin.Logger.LogDebug("[CopperChestPatch] GetSlotForCard/AssignCard not resolved — chest payouts will use the eject-and-respawn fallback.");

            return _getSlotForCardMethod != null && _assignCardMethod != null;
        }
    }
}
