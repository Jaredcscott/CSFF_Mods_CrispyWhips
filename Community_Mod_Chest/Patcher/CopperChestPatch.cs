using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    ///   3. THEFT — two paths into the SAME detection roll, because the chest's InventorySlots are
    ///      a REAL container and vanilla lets a player open any container and drag cards straight
    ///      out of its inventory popup, exactly like any ordinary player-built chest (memory:
    ///      workshop-storage-action-fallback confirms this is normal InventorySlots behavior, not a
    ///      popup bug) — that path bypasses a DismantleAction/CardInteraction entirely. (a) "Search
    ///      for valuables" — a DismantleAction that empties the WHOLE chest in one action and rolls
    ///      once. (b) Manual drag-out via the chest's own inventory popup — <see cref="RunPilferageTick"/>
    ///      polls each visited chest every 2s, diffs live contents against the last-known snapshot,
    ///      and rolls ONCE PER CARD that vanished outside of (a) or the Sell CI (both refresh the
    ///      snapshot themselves right after they run, so a paid sale or a Search is never
    ///      double-counted as pilferage). Detected -> cmcStatVillageCrime +10
    ///      (VillageCrimePatch.AddCrime), same consequence either way.
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

            // ── Trust-on-sale (N19) ───────────────────────────────────────────────
            // Selling into a chest is the one commerce verb the Trust layer previously ignored
            // (idea N19). All five residents get a bump per completed sale, but not through one
            // uniform mechanism: Miller/Weaver/Professor/Apothecary share the cmcStat<Resident>Trust
            // NPCStat family (0-100, resolved on the resident's LIVE NPC instance — see
            // AddResidentTrust). The Inn Keeper already had his own trust-equivalent BEFORE this
            // feature (cmcStatInnFriendship, a plain 0-30 PLAYER GameStat that already gates his warm
            // dialog — QuestChainSchedulePatch's own doc comment) — TrustIsNpcStat routes him through
            // the simpler GameStat-direct-write path instead of standing up a second, disconnected
            // "Inn Keeper Trust" NPCStat that no dialog would ever read (root CLAUDE.md Feature
            // Honesty: a stat nothing reads is dead content, not a fix).

            /// <summary>UID of the resident's Trust-equivalent stat.</summary>
            public string TrustStatUid;
            /// <summary>True = <see cref="TrustStatUid"/> is an NPCStat resolved on the live NPC
            /// (AgentUid). False = it's a plain player GameStat (Inn Keeper only).</summary>
            public bool TrustIsNpcStat;
            /// <summary>Trust points added per completed sale — deliberately small; sales are
            /// frequent enough that a large per-sale bump would blow past the stat's own ceiling in
            /// a few evenings of trading (§N19 "keep the per-sale bump tiny").</summary>
            public float TrustGainPerSale;
            /// <summary>Mirrors the stat's own MinMaxValue.y. <see cref="AddResidentTrust"/> clamps
            /// to this explicitly rather than trusting an unverified auto-clamp on every write path
            /// (VillageCrimePatch's own MaxCrime does the same for the same reason).</summary>
            public float TrustMaxValue;
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
                TrustStatUid = "cmcStatMillerTrust",
                TrustIsNpcStat = true,
                TrustGainPerSale = 1.5f,
                TrustMaxValue = 100f,
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
                TrustStatUid = "cmcStatWeaverTrust",
                TrustIsNpcStat = true,
                TrustGainPerSale = 1.5f,
                TrustMaxValue = 100f,
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
                // New NPCStat (N19) — the Apothecary genuinely had no Trust equivalent before
                // this feature, unlike the Inn Keeper's pre-existing cmcStatInnFriendship.
                TrustStatUid = "cmcStatApothecaryTrust",
                TrustIsNpcStat = true,
                TrustGainPerSale = 1.5f,
                TrustMaxValue = 100f,
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
                // Reuses his PRE-EXISTING trust-equivalent GameStat rather than a new NPCStat —
                // see the TrustIsNpcStat doc comment on ChestConfig for why.
                TrustStatUid = "cmcStatInnFriendship",
                TrustIsNpcStat = false,
                TrustGainPerSale = 0.5f,  // cmcStatInnFriendship's range is 0-30, not 0-100
                TrustMaxValue = 30f,
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
                TrustStatUid = "cmcStatProfessorTrust",
                TrustIsNpcStat = true,
                TrustGainPerSale = 1.5f,
                TrustMaxValue = 100f,
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

        /// <summary>Detection-chance REDUCTION while the player carries a Burglar's Kit (N14) — a
        /// prepared thief tilts the roll in their favour. Deliberately modest: a quarter of the
        /// night-patrol penalty, so the Kit shaves the odds rather than granting near-immunity;
        /// RollDetection still floors the final chance well above 0.</summary>
        private const float BurglarsKitDiscount = 0.05f;

        private const string BurglarsKitUid = "cmcBurglarsKit";

        /// <summary>Miller's chest UID, named separately from <see cref="Chests"/> because the
        /// restitution CI (N15) is registered once, outside the per-chest loop — see its own doc
        /// comment on <see cref="RestitutionAfter"/> for why it's Miller-only.</summary>
        private const string MillerChestUid = "cmcCopperChestMiller";

        private const string RestitutionActionKey = "CMC_CopperChestMiller_CI_Restitution";
        private const string RestitutionActionName = "Make It Right";

        /// <summary>Crime points paid down per point of dragged-currency value (N15). A single
        /// Salt (CurrencyValue.SaltValue = 15) pays down 3 crime; a copper Nugget (~100 raw SD4)
        /// pays down ~20 — a meaningful dent but not a full pardon on its own, consistent with
        /// JailPatch's DailyCrimePaydown (8 crime/day) being the primary route back to Clean and
        /// this being a supplementary, player-initiated one (§10.8.7.2 leaves that path intact).</summary>
        private const float CrimePerCurrencyValue = 0.2f;

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

            // Restitution ("Make It Right", N15) is Miller-only per the idea's own scope — the
            // other four chests get no such CI in this pass. One handler, not one per chest;
            // same Before-captures-value/After-applies-effect shape as CopperChestSellPayout.
            ActionRouter.Register(new ActionHandler
            {
                Name = "CopperChestMillerRestitution",
                CardUid = MillerChestUid,
                ActionKeyPrefix = RestitutionActionKey,
                ActionNamePrefix = RestitutionActionName,
                Timing = ActionTiming.AfterWrapped,
                // Captured in Before for the same reason SellPayout captures price there: the CI's
                // own GivenCardChanges.ModType 3 destroys the dragged card as part of the action, so
                // by the time After runs ctx.GivenCard's CardModel may already be gone.
                Before = ctx => { ctx.Tag = CurrencyValue.ValueOf(ctx.GivenCard); return false; },
                After = ctx => RestitutionAfter(ctx),
            });

            TickEvents.Interval(2f, RunPilferageTick, "CopperChestPilferage");

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
                // A paid, consensual sale — never pilferage. Refresh the baseline now so
                // RunPilferageTick's next poll doesn't read these currency cards leaving as an
                // unmonitored drag-out (see SyncKnownContents's doc comment).
                SyncKnownContents(cfg, ctx.Card);
                Plugin.Logger.LogInfo($"[CopperChestPatch] Sold an item to the {cfg.Name} for {price:0}; handed over {paid} currency card(s) worth {paidValue:0}. Chest wealth now {CurrentWealth(ctx.Card):0}.");

                // N19 — the one commerce verb the Trust layer previously ignored. Only on a
                // completed sale (paid > 0, reached above), never on a refused/no-op drag.
                AddResidentTrust(cfg);
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

        // ── Trust bump on sale (§N19) ─────────────────────────────────────────────

        private static MethodInfo _trustGetFromIdMethod; // UniqueIDScriptable.GetFromID(string)
        private static readonly Dictionary<string, object> _trustStatCache = new(StringComparer.Ordinal);

        /// <summary>
        /// Bumps <paramref name="cfg"/>'s owner's Trust-equivalent stat by
        /// <see cref="ChestConfig.TrustGainPerSale"/> after a completed sale. Writes the LIVE stat
        /// directly, never the read-only player-side mirror QuestChainSchedulePatch polls every 5s
        /// for blueprint gates — writing the mirror here would just get overwritten by the very next
        /// poll and would never actually raise the relationship the NPC's own dialog reads.
        ///
        /// <para>NPCStat write uses <c>InGameNPCStat.SetStatValueFromEditor(float)</c> — the SAME
        /// proven idiom GuardOutcomePatch.ClearDownedMarker already uses (confirmed correct by that
        /// file's own doc comment, "same idiom AnimalLifecycleTicker uses"). NOTE: this deliberately
        /// does NOT copy ProfessorSchedulePatch.SetNpcStatValue's lookup, which searches for a
        /// PUBLIC method literally named "SetStatValue" — the decompiled InGameNPCStat only has a
        /// PRIVATE 2-arg SetStatValue and the public 1-arg SetStatValueFromEditor, so that lookup
        /// appears to resolve to null on every call (worth checking separately; out of this file's
        /// scope to fix ProfessorSchedulePatch itself).</para>
        /// </summary>
        private static void AddResidentTrust(ChestConfig cfg)
        {
            if (cfg == null || string.IsNullOrEmpty(cfg.TrustStatUid) || cfg.TrustGainPerSale <= 0f) return;
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                if (!cfg.TrustIsNpcStat)
                {
                    AddGameStatTrust(gm, cfg);
                    return;
                }

                var npc = FindLiveNpc(gm, cfg.AgentUid);
                if (npc == null) return; // resident hasn't spawned/moved in yet — nothing to bump

                var stat = TrustStatRef(cfg.TrustStatUid);
                if (stat == null) return;

                if (Reflect.GetMember(npc, "NPCStatsDict") is not IDictionary statsDict || !statsDict.Contains(stat))
                    return;
                var inGameStat = statsDict[stat];
                if (inGameStat == null) return;

                var getStatValue = npc.GetType().GetMethod("GetStatValue",
                    BindingFlags.Instance | BindingFlags.Public, null, new[] { stat.GetType() }, null);
                float current = getStatValue?.Invoke(npc, new[] { stat }) is float f ? f : 0f;

                var setter = inGameStat.GetType().GetMethod("SetStatValueFromEditor",
                    BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(float) }, null);
                if (setter == null)
                {
                    Plugin.Logger.LogDebug("[CopperChestPatch] InGameNPCStat.SetStatValueFromEditor not found — sale trust bump inactive.");
                    return;
                }

                float next = Math.Min(cfg.TrustMaxValue, current + cfg.TrustGainPerSale);
                setter.Invoke(inGameStat, new object[] { next });
                Plugin.Logger.LogDebug($"[CopperChestPatch] {cfg.Name}'s trust +{next - current:0.0} from a sale ({current:0.0} -> {next:0.0}).");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[CopperChestPatch] AddResidentTrust failed for {cfg.Name}: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>Inn Keeper only — cmcStatInnFriendship is a plain player GameStat, not an
        /// NPCStat, so this writes it via the direct StatsDict idiom (memory:
        /// reference_gamestat_direct_csharp_write) — no live-NPC resolution needed at all.</summary>
        private static void AddGameStatTrust(object gm, ChestConfig cfg)
        {
            var stat = TrustStatRef(cfg.TrustStatUid);
            if (stat == null) return;
            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict || !statsDict.Contains(stat)) return;
            var inGameStat = statsDict[stat];
            if (inGameStat == null) return;

            float current = Reflect.GetMember(inGameStat, "SimpleCurrentValue") is float f ? f : 0f;
            float next = Math.Min(cfg.TrustMaxValue, current + cfg.TrustGainPerSale);
            Reflect.SetMember(inGameStat, "CurrentBaseValue", next);
            Plugin.Logger.LogDebug($"[CopperChestPatch] {cfg.Name}'s trust +{next - current:0.0} from a sale ({current:0.0} -> {next:0.0}).");
        }

        private static bool ResolveTrustGetFromId()
        {
            if (_trustGetFromIdMethod != null) return true;
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");
            if (uidType == null) return false;
            _trustGetFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            return _trustGetFromIdMethod != null;
        }

        private static object TrustStatRef(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            if (_trustStatCache.TryGetValue(uid, out var cached)) return cached;
            if (!ResolveTrustGetFromId()) return null;
            var stat = _trustGetFromIdMethod.Invoke(null, new object[] { uid });
            if (stat != null) _trustStatCache[uid] = stat;
            return stat;
        }

        /// <summary>Live InGameNPC for an AgentUid, or null. Same AllNPCs/NPCModel.UniqueID walk as
        /// GuardOutcomePatch's own FindLiveNpc (and, inline, this file's own IsOwnerHome below) —
        /// kept as a local copy rather than a cross-file call so this file has no compile-time
        /// dependency on GuardOutcomePatch for a four-line loop.</summary>
        private static object FindLiveNpc(object gm, string agentUid)
        {
            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs) return null;
            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                var uid = CardUtil.GetCardUniqueId(Reflect.GetMember(npc, "NPCModel"));
                if (string.Equals(uid, agentUid, StringComparison.OrdinalIgnoreCase)) return npc;
            }
            return null;
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

                // The whole chest is now empty — bring the pilferage snapshot in line with that
                // BEFORE the next poll runs, or RunPilferageTick would read this DA's own removals
                // as an unmonitored drag-out and roll a SECOND, redundant detection on top of this one.
                SyncKnownContents(cfg, chest);

                if (taken == 0)
                {
                    // An empty chest is not a crime — nothing was actually stolen, so no roll.
                    Plugin.Logger.LogDebug($"[CopperChestPatch] Search found the {cfg.Name}'s chest empty — no detection roll.");
                    return;
                }

                RollAndRecordTheft(cfg, $"robbed of {taken} card(s)");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CopperChestPatch] SearchAfter failed for the {cfg.Name}: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// One detection roll and its consequence, shared by the Search DA (one call per whole-chest
        /// sweep) and <see cref="RunPilferageTick"/> (one call per card caught vanishing outside of
        /// that DA or the Sell CI). <paramref name="sourceLabel"/> is folded into the log line only —
        /// the roll itself (<see cref="RollDetection"/>), the crime cost, and the heat bookkeeping are
        /// identical either way, so a caught burglar can't lower their risk just by switching methods.
        /// </summary>
        private static void RollAndRecordTheft(ChestConfig cfg, string sourceLabel)
        {
            bool caught = RollDetection(cfg, out string reason);
            if (caught)
            {
                SetHeat(cfg, 0f); // the heat has been spent — a caught burglar starts the ladder over
                VillageCrimePatch.AddCrime(DetectedCrimePoints);
                Plugin.Logger.LogInfo($"[CopperChestPatch] The {cfg.Name}'s chest was {sourceLabel} and the theft was DETECTED ({reason}).");
            }
            else
            {
                SetHeat(cfg, CurrentHeatCount(cfg) + 1f);
                // Undetected theft leaves no record at all (§10.8.3.6) — Debug only, so a
                // player reading LogOutput.log can't use it as an oracle.
                Plugin.Logger.LogDebug($"[CopperChestPatch] The {cfg.Name}'s chest was {sourceLabel}, undetected ({reason}).");
            }
        }

        // ── Pilferage detection — closes the direct-drag-out loophole ────────────

        /// <summary>
        /// Per-chest snapshot of the card INSTANCES last observed inside it (default reference
        /// equality — two Salt cards are different entries even with the same UniqueID), keyed by
        /// <see cref="ChestConfig.ChestUid"/>. Populated and diffed only while the player is actually
        /// standing in that chest's own interior, for the same reason <see cref="TryAccrue"/> is
        /// gated the same way: <c>Inventory.Cards</c> can only see a chest that's currently in-scene
        /// (memory: reference_allcards_env_scoped).
        /// </summary>
        private static readonly Dictionary<string, HashSet<object>> _knownContents = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Every 2s, for whichever Copper Chest(s) the player currently has in scope: diff the
        /// chest's live contents against the last poll's snapshot and roll one detection check per
        /// card that disappeared.
        ///
        /// <para><b>Why this exists</b>: the chest is a normal CT2 card with real <c>InventorySlots</c>
        /// (§10.8.3.1's "one physical container" design), and vanilla lets a player open ANY such
        /// container and drag cards straight out of its inventory popup — the exact same "normal
        /// storage" behavior <c>workshop-storage-action-fallback</c> documents for any station with
        /// populated slots. That path never goes through the "Search for valuables" DismantleAction,
        /// so before this method existed a player could empty an NPC's entire chest by hand with zero
        /// detection roll, zero crime, and therefore zero Reputation consequence — the theft mechanic
        /// (§10.8.3.6) was fully opt-in. Blocking the popup outright isn't viable: `CardData.HasInventory`
        /// has no separate "locked" flag (`.decomp/CardData.cs`), and the same physical
        /// <c>InventorySlots</c> back the live-summed wealth the Sell CI depends on (§10.8.3.4), so the
        /// slots can't simply be removed. Polling and diffing is the same idiom already proven by
        /// <see cref="VillageReputationPatch"/>'s own <c>TickEvents.Interval</c> tick, not a new
        /// pattern for this codebase.</para>
        ///
        /// <para>The Search DA and the Sell CI both call <see cref="SyncKnownContents"/> themselves
        /// the instant they finish mutating a chest, so their own (already-accounted-for) removals
        /// never show up as a diff here — only a removal this file didn't itself perform can appear
        /// as "missing."</para>
        /// </summary>
        private static void RunPilferageTick()
        {
            foreach (var cfg in Chests)
            {
                try { CheckForPilferage(cfg); }
                catch (Exception ex)
                {
                    Plugin.Logger.LogDebug($"[CopperChestPatch] Pilferage check failed for {cfg.Name}: {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
            }
        }

        private static void CheckForPilferage(ChestConfig cfg)
        {
            if (!string.Equals(GameQuery.CurrentEnvironmentUniqueId, cfg.InteriorEnvUid, StringComparison.OrdinalIgnoreCase))
            {
                // Player isn't in this chest's room right now, so nothing can be dragged from it —
                // and the chest itself isn't live/scannable anyway (reference_allcards_env_scoped).
                // Drop any stale snapshot so the NEXT visit starts fresh rather than diffing against
                // a poll from an arbitrarily long time ago.
                _knownContents.Remove(cfg.ChestUid);
                return;
            }

            var chest = CardFinder.Find(cfg.ChestUid);
            if (chest == null)
            {
                _knownContents.Remove(cfg.ChestUid);
                return;
            }

            var current = new HashSet<object>(Inventory.Cards(chest));

            if (!_knownContents.TryGetValue(cfg.ChestUid, out var known))
            {
                // First observation since entering this room — nothing to diff against yet, so this
                // poll only establishes the baseline (never accuses on the very first look).
                _knownContents[cfg.ChestUid] = current;
                return;
            }

            int missing = 0;
            foreach (var card in known)
            {
                if (current.Contains(card)) continue;

                // A card the diff no longer sees is not always one someone carried off. A
                // destroyed UnityEngine.Object is NOT C#-null under a bare object-typed
                // comparison (CLAUDE.md "Harmony Patching Pitfalls"), and a card replaced by
                // another instance (e.g. food transforming on spoil, TransformCardInPlace)
                // leaves its OLD instance destroyed rather than stolen. Neither is pilferage.
                if (!Reflect.IsAlive(card)) continue;
                if (Reflect.GetBool(card, "Destroyed")) continue;

                missing++;
            }

            _knownContents[cfg.ChestUid] = current;
            if (missing == 0) return;

            string label = missing == 1 ? "pilfered from directly" : $"pilfered from directly ({missing} cards)";
            for (int i = 0; i < missing; i++) RollAndRecordTheft(cfg, label);
        }

        /// <summary>Refreshes the pilferage baseline to the chest's CURRENT contents. Called by the
        /// Search DA and the Sell CI right after each mutates the chest, so <see cref="RunPilferageTick"/>'s
        /// next poll compares against a baseline that already reflects their own accounted-for
        /// removal rather than flagging it a second time.</summary>
        private static void SyncKnownContents(ChestConfig cfg, object chest)
        {
            if (chest == null) return;
            _knownContents[cfg.ChestUid] = new HashSet<object>(Inventory.Cards(chest));
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
            float kitDiscount = HasBurglarsKit() ? BurglarsKitDiscount : 0f;
            if (kitDiscount > 0f)
                Plugin.Logger.LogDebug($"[CopperChestPatch] Burglar's Kit present — detection chance reduced by {kitDiscount:P0} for this roll.");

            // Floored well above 0 (not just the raw sum) — a Kit shaves the odds, it doesn't grant
            // immunity even on a lucky roll with zero heat/patrol active.
            float chance = Math.Min(0.95f, Math.Max(0.01f, BaseDetectionChance + heatBonus + patrolBonus - kitDiscount));
            bool caught = UnityEngine.Random.value < chance;
            reason = $"roll {chance:P0} (base {BaseDetectionChance:P0} + heat {heatBonus:P0} + patrol {patrolBonus:P0} - kit {kitDiscount:P0})";
            return caught;
        }

        /// <summary>
        /// True when the Burglar's Kit (N14) is present with the player right now. This game has no
        /// separate "carried/pocket inventory" API to check against — confirmed absent fleet-wide
        /// (memory: reference_no_player_inventory_access — neither GameQuery, Api.Inventory, nor
        /// ActionRouter expose the player's own carried inventory). <see cref="GameQuery.CardsInPlayerEnv"/>
        /// (cards in the player's CURRENT environment) is the established substitute this codebase
        /// already uses fleet-wide for "does the player have X" presence gating — ConnectionGateService,
        /// SealableGateService, and ConditionalDropService all gate the exact same way, so this reuses
        /// that idiom rather than inventing new reflection. The player is necessarily standing in
        /// THIS chest's own interior when the Search DA fires (TryAccrue/IsOwnerHome's own
        /// CurrentEnvironmentUniqueId gate proves it), so "present in the player's env" here means
        /// "in the room with the player while they search" — a reasonable reading of "carrying" for
        /// a small hand tool, given no finer-grained equipped/held concept exists to check instead.
        /// </summary>
        private static bool HasBurglarsKit()
        {
            try
            {
                foreach (var card in GameQuery.CardsInPlayerEnv())
                    if (string.Equals(CardUtil.GetCardUniqueId(card), BurglarsKitUid, StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"[CopperChestPatch] HasBurglarsKit check failed (treated as no kit): {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
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

        // ── Restitution CI — "Make It Right" (Miller only, §N15) ──────────────────

        /// <summary>
        /// Pays down Village Crime by dragging Salt or a Metal Nugget onto the Miller's chest.
        /// Registered once (see <see cref="Initialize"/>), not per chest — the idea's own title
        /// ("Make it right with the MILLER") scopes this to one resident rather than all five;
        /// extending it later would mean giving the other four chests their own restitution CI +
        /// CardData entries, not touching this method.
        ///
        /// <para>Reuses <see cref="VillageCrimePatch.ReduceCrime"/> directly rather than
        /// reimplementing the StatsDict write — exactly what the idea text asks for ("reuses the
        /// existing... ReduceCrime(amount, reason) for a small pay-down").</para>
        /// </summary>
        private static void RestitutionAfter(ActionContext ctx)
        {
            try
            {
                float value = ctx.Tag is float captured ? captured : 0f;
                if (value <= 0f) return; // not a recognized currency card — nothing to pay down

                float amount = value * CrimePerCurrencyValue;
                float result = VillageCrimePatch.ReduceCrime(amount, "made it right with the Miller");
                if (result < 0f) return; // stat unreadable — VillageCrimePatch already logged why

                CardVisualsRefresh.RefreshOpenInventoryPopup();
                Plugin.Logger.LogInfo($"[CopperChestPatch] Restitution paid to the Miller (value {value:0}) — crime reduced by {amount:0}.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CopperChestPatch] RestitutionAfter failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Chest -> player card movement ─────────────────────────────────────────

        private static bool _slotStaticsResolved;
        private static PropertyInfo _graphicsInstanceProperty;
        private static Type _graphicsManagerType;
        private static MethodInfo _getSlotForCardMethod;   // GraphicsManager.GetSlotForCard(CardData, CardData, SlotInfo, bool, InGameNPCOrPlayer) — arity matched dynamically, see BuildGetSlotForCardArgs
        private static MethodInfo _assignCardMethod;       // DynamicLayoutSlot.AssignCard(InGameCardBase, bool)
        private static bool _transferFallbackLogged;
        // Exception TYPE + message (or a short non-exception reason) from the most recent
        // TryTransferInstance failure — folded into the once-per-session fallback notice so the
        // NEXT game-update arity drift is legible in LogOutput.log without enabling Debug logging.
        private static string _lastTransferFailureReason;

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
        ///
        /// <para>2026-09-08: GraphicsManager.GetSlotForCard gained a trailing InGameNPCOrPlayer
        /// _User parameter (EA 0.67i) that the old hardcoded 4-argument Invoke didn't know about,
        /// throwing TargetParameterCountException on every call and taking this fallback on every
        /// payout (Fleet Master Plan §1.1, T4.40/T4.38; r33 Player.log lines 2245-2285). Not yet
        /// re-verified in-game — see the CHANGELOG entry for this version.
        /// <see cref="BuildGetSlotForCardArgs"/> now matches parameters by declared TYPE off
        /// <c>GetParameters()</c> instead of a hardcoded arity, so the next such drift degrades to
        /// this same fallback instead of throwing again.</para>
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
                Plugin.Logger.LogInfo($"[CopperChestPatch] Direct card transfer unavailable ({_lastTransferFailureReason ?? "reason not captured"}) — falling back to eject-and-respawn for chest payouts (see class doc).");
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
                if (!ResolveSlotStatics())
                {
                    _lastTransferFailureReason = "GetSlotForCard/AssignCard not resolved on GraphicsManager";
                    return false;
                }

                var graphics = _graphicsInstanceProperty?.GetValue(null)
                    ?? UnityEngine.Object.FindObjectOfType(_graphicsManagerType);
                if (graphics == null)
                {
                    _lastTransferFailureReason = "GraphicsManager instance not found";
                    return false;
                }

                var cardData = CardUtil.GetCardData(card);
                if (cardData == null)
                {
                    _lastTransferFailureReason = "card has no resolvable CardData";
                    return false;
                }
                var liquidModel = Reflect.GetMember(card, "ContainedLiquidModel");
                var chestSlotInfo = Reflect.GetMember(chest, "CurrentSlotInfo");
                if (chestSlotInfo == null)
                {
                    _lastTransferFailureReason = "chest has no CurrentSlotInfo";
                    return false;
                }

                var args = BuildGetSlotForCardArgs(cardData, liquidModel, chestSlotInfo, out string buildFailure);
                if (args == null)
                {
                    _lastTransferFailureReason = buildFailure ?? "could not build GetSlotForCard arguments";
                    return false;
                }

                var slot = _getSlotForCardMethod.Invoke(graphics, args);
                if (slot == null)
                {
                    _lastTransferFailureReason = "GetSlotForCard returned no compatible slot";
                    return false;
                }

                _assignCardMethod.Invoke(slot, new[] { card, (object)true });

                // AssignCard silently refuses incompatible/occupied slots, so confirm the card
                // actually left the chest before reporting success — "Invoke didn't throw" is not
                // evidence the move happened (the same lesson QuickTransfer's pile-count check
                // encodes). The cast is deliberate: a DESTROYED UnityEngine.Object is not C#-null
                // under an object-typed comparison (CLAUDE.md §Harmony Patching Pitfalls).
                var container = Reflect.GetMember(card, "CurrentContainer");
                bool stillContained = container is UnityEngine.Object containerObj && containerObj != null;
                if (stillContained)
                {
                    _lastTransferFailureReason = "AssignCard did not move the card (slot refused it)";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                var cause = ex.InnerException ?? ex;
                _lastTransferFailureReason = $"{cause.GetType().Name}: {cause.Message}";
                Plugin.Logger.LogDebug($"[CopperChestPatch] TryTransferInstance failed for '{CardUtil.GetCardUniqueId(card) ?? "?"}': {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }

        /// <summary>
        /// Builds GetSlotForCard's argument array by matching each resolved parameter's declared
        /// TYPE (via <see cref="_getSlotForCardMethod"/>'s own <c>GetParameters()</c>), not a
        /// hardcoded arity — the exact fix for the 2026-09-08 drift (EA 0.67i added a trailing
        /// <c>InGameNPCOrPlayer _User</c> that a fixed 4-argument array didn't know about, throwing
        /// <c>TargetParameterCountException</c> on every call). CardData appears TWICE on the live
        /// signature (the card itself and its optional liquid form) so those two positions are
        /// filled in DECLARATION ORDER, not by name — reflection has no parameter names to match on
        /// on a release build.
        /// </summary>
        /// <returns>The argument array, or null if an unrecognized value-type parameter appeared
        /// (see <paramref name="failureReason"/>) and there is nothing safe to pass for it.</returns>
        private static object[] BuildGetSlotForCardArgs(object cardData, object liquidModel, object chestSlotInfo, out string failureReason)
        {
            failureReason = null;
            var pars = _getSlotForCardMethod.GetParameters();
            var args = new object[pars.Length];
            bool cardDataSlotFilled = false;

            for (int i = 0; i < pars.Length; i++)
            {
                var t = pars[i].ParameterType;
                if (t == typeof(CardData))
                {
                    args[i] = cardDataSlotFilled ? liquidModel : cardData;
                    cardDataSlotFilled = true;
                }
                else if (t == typeof(SlotInfo))
                {
                    args[i] = chestSlotInfo;
                }
                else if (t == typeof(bool))
                {
                    args[i] = false; // _OwnedByNPC — the player is receiving this card, never an NPC
                }
                else if (t == typeof(InGameNPCOrPlayer))
                {
                    // The live game dereferences this with NO null guard (CardToSlotType's
                    // "if (_User.Player)" — .decomp/GraphicsManager.cs) and it's a STRUCT, so
                    // reflection.Invoke can't bind a bare C# null to it either way. GiveToPlayer
                    // only ever moves a card INTO the player's own hands, so PlayerAgent is always
                    // the right identity here — the same literal VillageHallBoardsPatch.cs already
                    // uses for its own InGameNPCOrPlayer-typed calls in this mod.
                    args[i] = InGameNPCOrPlayer.PlayerAgent;
                }
                else if (t.IsValueType && Nullable.GetUnderlyingType(t) == null)
                {
                    // An unknown non-nullable value-type parameter has no safe default — a guessed
                    // value could silently mask a real behavior change, so bail to the fallback
                    // instead (root CLAUDE.md §Silent Catch Blocks: a default-returning path needs
                    // a breadcrumb, not a guess).
                    failureReason = $"unrecognized value-type parameter '{t.Name}' at position {i}";
                    return null;
                }
                else
                {
                    args[i] = null; // reference-type (or Nullable<T>) extra parameter — null is safe
                }
            }
            return args;
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

                // A coroutine-shaped GetSlotForCard (IEnumerator return) could never be run by a
                // bare Invoke — Invoke only constructs the compiler-generated state machine and
                // never runs the method body (root CLAUDE.md §Runtime Card Removal, the
                // TryRemoveCard/DestroyCard lesson: exactly this shape silently removed nothing
                // while still returning true). It returns DynamicLayoutSlot today; this guards the
                // NEXT drift rather than the current one.
                if (_getSlotForCardMethod != null && typeof(IEnumerator).IsAssignableFrom(_getSlotForCardMethod.ReturnType))
                {
                    Plugin.Logger.LogDebug($"[CopperChestPatch] GetSlotForCard now returns {_getSlotForCardMethod.ReturnType.Name} (a coroutine) — bare-Invoke would not run its body; chest payouts will use the eject-and-respawn fallback.");
                    _getSlotForCardMethod = null;
                }

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
