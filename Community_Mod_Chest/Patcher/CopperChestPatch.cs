using System;
using System.Collections.Generic;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The Miller's Copper Chest (Village_Master_Plan.md §10.8.3) — one CT2 container inside
    /// cmcMillerCottageInterior that is simultaneously the Miller's savings, his spending power,
    /// and a burglary target. Scoped to the Miller only this pass, per §10.8.3.7's sequencing
    /// rule; the Weaver/Apothecary/Inn Keeper/Professor chests are a later chunk.
    ///
    /// Three mechanics, ONE physical inventory (§10.8.3.1 — a single source of truth by
    /// construction, so robbing the chest also, automatically, makes the Miller unable to afford
    /// anything until the next accrual):
    ///
    ///   1. ACCRUAL — owned by CottageResidentSpawnPatch.CheckRestock, which retargets the
    ///      Miller's weekly AgentAction drop from his personal satchel onto this chest
    ///      (§10.8.3.3 "retarget, don't duplicate" — R6). The caps live here
    ///      (<see cref="CurrencyCap"/> / <see cref="GoodsCap"/>) because they are read off the
    ///      chest's live contents, not off a tracked number.
    ///   2. SELL — a drag-and-drop CardInteraction on the chest ("Sell to the Miller"). Priced
    ///      with MarketStallPatch's own ObjectWeight/10 formula so an item is worth the same at
    ///      the player's stall and at the Miller's chest. Afford-gated on <see cref="CurrentWealth"/>
    ///      via ActionTiming.Cancel (InnPatch.BalanceGate's shape), paid out of the chest's own
    ///      currency via ActionTiming.AfterWrapped.
    ///   3. THEFT — a "Search for valuables" DismantleAction that empties the chest to the player
    ///      and rolls for detection. Detected -> cmcStatVillageCrime +10 (VillageCrimePatch.AddCrime).
    ///
    /// <para><b>Why the DA needs C#</b>: a DismantleAction's static JSON cannot express "whatever
    /// happens to be inside right now", and ReceivingCardChanges.CardsToCreate is not processed on
    /// DismantleActions at all (root CLAUDE.md). DaytimeCost 4 + AlwaysShow keep it past
    /// CanAppear()/WillHaveAnEffect() so the button actually renders.</para>
    /// </summary>
    internal static class CopperChestPatch
    {
        internal const string ChestUid = "cmcCopperChestMiller";
        internal const string InteriorEnvUid = "cmcMillerCottageInterior";
        internal const string MillerAgentUid = "cmcMillerAgent";

        private const string TheftsStat = "cmcStatMillerChestThefts";
        private const string TheftSeasonStat = "cmcStatMillerChestTheftSeason";

        // Two-tier action identity (CLAUDE.md §Harmony Patching Pitfalls): the LocalizationKey is
        // the primary match, the DefaultText a fallback if the key is ever stripped. Both must
        // stay in step with CardData/Location/CMC_CopperChestMiller.json and SimpEn.csv.
        private const string SellActionKey = "CMC_CopperChestMiller_CI_Sell";
        private const string SellActionName = "Sell to the Miller";
        private const string SearchActionKey = "CMC_CopperChestMiller_DA_Search";
        private const string SearchActionName = "Search for valuables";

        /// <summary>Live-summed currency ceiling (§10.8.3.3). The Miller is a mid-tier earner —
        /// 300 is 20 Salt, roughly seven weeks of accrual from empty at 3 Salt/week.</summary>
        internal const float CurrencyCap = 300f;

        /// <summary>Separate goods ceiling, counted in cards rather than value so a heavy stack
        /// can't crowd the currency out of the chest's ten slots.</summary>
        internal const int GoodsCap = 10;

        /// <summary>Detection chance when the Miller is out, before modifiers. PLACEHOLDER —
        /// §10.8.3.6 flags 15% as unconfirmed pending the owner's tuning pass (§10.8.10).</summary>
        private const float BaseDetectionChance = 0.15f;

        /// <summary>Added per PRIOR undetected theft of this chest in the current season, so
        /// repeat burglary of the same target stops being risk-free (§10.8.3.6).</summary>
        private const float HeatPerPriorTheft = 0.05f;

        /// <summary>Cap on the accumulated heat bonus, so the roll never becomes a certainty
        /// on its own (an at-home Miller is the only guaranteed catch).</summary>
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

            // Registration order matters, exactly as in InnPatch: the afford-gate must be able to
            // short-circuit before the payout handler runs, so the dragged item is never consumed
            // for a sale the Miller cannot pay for.
            ActionRouter.Register(new ActionHandler
            {
                Name = "CopperChestSellGate",
                CardUid = ChestUid,
                ActionKeyPrefix = SellActionKey,
                ActionNamePrefix = SellActionName,
                Timing = ActionTiming.Cancel,
                Before = SellGate,
            });

            ActionRouter.Register(new ActionHandler
            {
                Name = "CopperChestSellPayout",
                CardUid = ChestUid,
                ActionKeyPrefix = SellActionKey,
                ActionNamePrefix = SellActionName,
                Timing = ActionTiming.AfterWrapped,
                // The price MUST be read in the prefix: the CI's own GivenCardChanges.ModType 3
                // destroys the dragged card as part of the action, so by the time After runs
                // ctx.GivenCard's CardModel may already be gone and PriceOf would silently fall
                // back to 1 (InnPatch's firewood handler captures SpecialDurability4 for exactly
                // this reason).
                Before = ctx => { ctx.Tag = PriceOf(ctx.GivenCard); return false; },
                After = SellPayout,
            });

            ActionRouter.Register(new ActionHandler
            {
                Name = "CopperChestSearch",
                CardUid = ChestUid,
                ActionKeyPrefix = SearchActionKey,
                ActionNamePrefix = SearchActionName,
                Timing = ActionTiming.AfterWrapped,
                After = SearchAfter,
            });

            Plugin.Logger.LogDebug("[CopperChestPatch] initialized.");
        }

        // ── Live-summed wealth (§10.8.3.4) ────────────────────────────────────────

        /// <summary>
        /// The Miller's current spending power: CurrencyValue.ValueOf summed over every
        /// currency card physically inside the chest, recomputed on every call. Deliberately NOT
        /// mirrored into a stat — a second source of truth can drift from the chest's actual
        /// contents, and the whole point of §10.8.3.1's one-container design is that it cannot.
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

        /// <summary>The live chest card, or null when the player is not standing in the Miller's
        /// cottage interior. CardFinder scans live scene objects only, and cards belonging to any
        /// environment other than the player's current one are serialized out rather than live
        /// (memory: reference_allcards_env_scoped) — so this is null almost everywhere.</summary>
        public static object FindLiveChest() => CardFinder.Find(ChestUid);

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
        private static bool SellGate(ActionContext ctx)
        {
            if (ctx.GivenCard == null) return false; // nothing dragged — let vanilla handle it

            float price = PriceOf(ctx.GivenCard);
            float wealth = CurrentWealth(ctx.Card);
            bool affordable = wealth >= price;
            if (!affordable)
                Plugin.Logger.LogDebug($"[CopperChestPatch] Sale refused — price {price:0} exceeds the Miller's chest wealth {wealth:0}.");
            return !affordable;
        }

        private static void SellPayout(ActionContext ctx)
        {
            try
            {
                float price = ctx.Tag is float captured ? captured : PriceOf(ctx.GivenCard);
                if (price <= 0f) return;

                int paid = PayFromChest(ctx.Card, price, out float paidValue);
                if (paid == 0)
                {
                    Plugin.Logger.LogWarning($"[CopperChestPatch] Sale completed but no currency could be drawn from the chest (price {price:0}) — the item was consumed for nothing.");
                    return;
                }

                CardVisualsRefresh.RefreshOpenInventoryPopup();
                Plugin.Logger.LogInfo($"[CopperChestPatch] Sold an item for {price:0}; handed over {paid} currency card(s) worth {paidValue:0}. Chest wealth now {CurrentWealth(ctx.Card):0}.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CopperChestPatch] SellPayout failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// Draws currency out of the chest smallest-value-first until the running total covers
        /// <paramref name="price"/>, and hands those cards to the player. Smallest-first (not
        /// largest-first) so the Miller doesn't surrender his one big nugget for a cheap trinket.
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

        private static void SearchAfter(ActionContext ctx)
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
                    Plugin.Logger.LogDebug("[CopperChestPatch] Search found an empty chest — no detection roll.");
                    return;
                }

                bool caught = RollDetection(out string reason);
                if (caught)
                {
                    SetHeat(0f); // the heat has been spent — a caught burglar starts the ladder over
                    VillageCrimePatch.AddCrime(DetectedCrimePoints);
                    Plugin.Logger.LogInfo($"[CopperChestPatch] Chest robbed of {taken} card(s) and DETECTED ({reason}).");
                }
                else
                {
                    SetHeat(CurrentHeatCount() + 1f);
                    // Undetected theft leaves no record at all (§10.8.3.6) — Debug only, so a
                    // player reading LogOutput.log can't use it as an oracle.
                    Plugin.Logger.LogDebug($"[CopperChestPatch] Chest robbed of {taken} card(s), undetected ({reason}).");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[CopperChestPatch] SearchAfter failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool RollDetection(out string reason)
        {
            // Instant catch: the Miller is standing in his own cottage. Reuses
            // CottageResidentSchedulePatch's own "is this resident inside their cottage interior"
            // query rather than re-deriving the schedule here.
            if (CottageResidentSchedulePatch.IsResidentHome(MillerAgentUid))
            {
                reason = "the Miller was home";
                return true;
            }

            float heatBonus = Math.Min(MaxHeatBonus, CurrentHeatCount() * HeatPerPriorTheft);
            float patrolBonus = NightPatrolActive() ? NightPatrolBonus : 0f;

            float chance = Math.Min(0.95f, BaseDetectionChance + heatBonus + patrolBonus);
            bool caught = UnityEngine.Random.value < chance;
            reason = $"roll {chance:P0} (base {BaseDetectionChance:P0} + heat {heatBonus:P0} + patrol {patrolBonus:P0})";
            return caught;
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

        // ── Seasonal "heat" counter ───────────────────────────────────────────────

        /// <summary>Prior UNDETECTED thefts of this chest in the current season. Reading it also
        /// performs the season rollover check, so the counter resets exactly once per season
        /// without needing its own tick subscription.</summary>
        private static float CurrentHeatCount()
        {
            int season = SeasonIndex();
            float storedSeason = HiddenStat.Get(TheftSeasonStat);
            float heat = HiddenStat.Get(TheftsStat);
            if (heat < 0f) return 0f; // stat unreadable — treat as no heat rather than guessing

            // season == 0 means GameQuery couldn't resolve the season this frame; don't reset on
            // an unknown, or a transient null would silently clear the player's accumulated heat.
            if (season > 0 && Math.Abs(storedSeason - season) > 0.001f && heat > 0f)
            {
                HiddenStat.Set(TheftsStat, 0f);
                HiddenStat.Set(TheftSeasonStat, season);
                Plugin.Logger.LogDebug("[CopperChestPatch] Season changed — Copper Chest theft heat reset to 0.");
                return 0f;
            }
            return heat;
        }

        private static void SetHeat(float value)
        {
            HiddenStat.Set(TheftsStat, Math.Max(0f, value));
            int season = SeasonIndex();
            if (season > 0) HiddenStat.Set(TheftSeasonStat, season);
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
        /// Moves one card out of the chest and into the player's hands.
        ///
        /// <para>Preferred path is a REAL transfer of the same physical card instance —
        /// <c>GraphicsManager.GetSlotForCard(...).AssignCard(card)</c>, which is vanilla's own
        /// container-spills-its-contents idiom (GameManager.RemoveCard, RemoveOption.Standard).
        /// DynamicLayoutSlot.AssignCard -> InGameCardBase.SetSlot performs the
        /// RemoveCardFromInventory + SetCurrentContainer(null) + reparent for us, so the player
        /// visibly receives the Miller's own hoarded coin rather than freshly conjured currency
        /// (§10.8.3.5 step 3).</para>
        ///
        /// <para>Fallback is the proven spawn-eject pattern (Api.Inventory.Eject +
        /// SpawnService.Spawn — MarketStallPatch's shape). It preserves the ECONOMICS exactly
        /// (the chest is drained, an equivalent card reaches the player) but not instance
        /// identity, and a respawned metal Nugget/Coin reverts to its default metal type because
        /// GiveCard returns void in this game version and stat overrides cannot be applied to the
        /// spawn (SpawnService's own documented gap). That only ever affects currency the PLAYER
        /// stashed in the chest — accrual deposits Salt, which is flat-valued.</para>
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
