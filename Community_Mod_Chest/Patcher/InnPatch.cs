using System;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Village Inn Counter mechanics (the counter card inside the cmcInnInterior env;
    /// the cmcInn entrance card itself only has the vanilla-cabin-style "Enter" DA):
    ///   - The counter holds an "Inn Account" balance (SpecialDurability1, max 500).
    ///     Drag Salt / a metal Nugget (any type) / a Duros Coin onto the counter (the
    ///     "Deposit Currency" CI) to add its value to the balance — see
    ///     <see cref="CurrencyValue"/>. A deposit is rejected outright (item untouched)
    ///     if the account has no headroom left at all.
    ///   - "Purchase Meal" (DaytimeCost 2) and "Stay the Night" (DaytimeCost 32) each
    ///     draw 50 from the balance; stat effects are applied via JSON StatModifications.
    ///   - "Work Odd Jobs" DA is deliberately NOT gated or paid from the account — it
    ///     PAYS the player (pure JSON ProducedCards: 1-2 vanilla 50%-quality copper
    ///     nuggets).
    ///   - CardAction.CollectActionModifiers postfix sets the native ActionBlockedMessage
    ///     when the balance is too low, so the two buttons show pre-emptively greyed out
    ///     with a clear tooltip reason (vanilla DismantleActionButton reads
    ///     ActionBlockedMessage via GameManager.CheckActionBlockers) instead of silently
    ///     no-oping on click. The Cancel/AfterWrapped hooks below remain as a safety net
    ///     (e.g. bulk "Eat All").
    /// </summary>
    internal static class InnPatch
    {
        private const string CounterUid   = "cmcInnCounter";
        private const string BalanceStat   = "SpecialDurability1";
        private const float  MealPrice     = 50f;
        private const float  SleepPrice    = 50f;
        private const float  AccountMax    = 500f;
        private const string SaltGuid      = "f91b5676fc26c0e48a4ee6fd9dfc2ffa";
        private static float _pendingFirewoodSd4 = -1f;

        private const string NoBalanceMessage =
            "Inn account balance too low — deposit Salt, coins, or nuggets on the counter. Meal/Sleep cost 50.";

        private static bool _initialized;

        // ── Registration ─────────────────────────────────────────────────────

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            if (CardUtil.FindGameType("InGameCardBase") == null)
            {
                Plugin.Logger.LogWarning("[InnPatch] InGameCardBase not found — inn account mechanics inactive.");
                return;
            }

            TryPatchBlockedMessage(harmony);

            // Cancel gate — blocks Meal/Sleep if the account balance is too low
            ActionRouter.Register(new ActionHandler
            {
                Name          = "InnSleepGate",
                CardPredicate = ctx => ctx.CardUid == CounterUid && IsSleepAction(ctx.ActionName),
                Timing        = ActionTiming.Cancel,
                Before        = ctx => BalanceGate(ctx, SleepPrice),
            });

            ActionRouter.Register(new ActionHandler
            {
                Name          = "InnMealGate",
                CardPredicate = ctx => ctx.CardUid == CounterUid && IsMealAction(ctx.ActionName),
                Timing        = ActionTiming.Cancel,
                Before        = ctx => BalanceGate(ctx, MealPrice),
            });

            // AfterWrapped — stat effects already applied by JSON; this draws from the account
            ActionRouter.Register(new ActionHandler
            {
                Name          = "InnSleepConsume",
                CardPredicate = ctx => ctx.CardUid == CounterUid && IsSleepAction(ctx.ActionName),
                Timing        = ActionTiming.AfterWrapped,
                After         = ctx => DeductBalance(ctx, SleepPrice),
            });

            ActionRouter.Register(new ActionHandler
            {
                Name          = "InnMealConsume",
                CardPredicate = ctx => ctx.CardUid == CounterUid && IsMealAction(ctx.ActionName),
                Timing        = ActionTiming.AfterWrapped,
                After         = ctx => DeductBalance(ctx, MealPrice),
            });

            // Deposit CI — Cancel gate first (registration order matters: a full account
            // must short-circuit before InnDepositApply ever runs, so the dragged item
            // is never consumed for zero value).
            ActionRouter.Register(new ActionHandler
            {
                Name          = "InnDepositGate",
                CardPredicate = ctx => ctx.CardUid == CounterUid && IsDepositAction(ctx.ActionName),
                Timing        = ActionTiming.Cancel,
                Before        = DepositGate,
            });

            // Timing.Before (not AfterWrapped): the balance MUST be written before the
            // native routine's own MiniTicksCost advance runs (UseMiniTicks:CostsAMiniTick,
            // 1 mini-tick = 3 in-game minutes) — writing it in AfterWrapped would apply the
            // new balance only after that tick's refresh pass already read the stale value
            // (memory: reference_givecard_postfix_stat_init — stat writes precede ticks).
            ActionRouter.Register(new ActionHandler
            {
                Name          = "InnDepositApply",
                CardPredicate = ctx => ctx.CardUid == CounterUid && IsDepositAction(ctx.ActionName),
                Timing        = ActionTiming.Before,
                Before        = DepositApply,
            });

            // Sell Firewood CI — capture SD4 before firewood is consumed, spawn Salt after.
            ActionRouter.Register(new ActionHandler
            {
                Name          = "InnFirewoodCaptureSd4",
                CardPredicate = ctx => ctx.CardUid == CounterUid && IsFirewoodSellAction(ctx.ActionName),
                Timing        = ActionTiming.Before,
                Before        = ctx => { _pendingFirewoodSd4 = CardUtil.GetDurability(ctx.GivenCard, "SpecialDurability4"); return false; },
            });

            ActionRouter.Register(new ActionHandler
            {
                Name          = "InnFirewoodSpawnSalt",
                CardPredicate = ctx => ctx.CardUid == CounterUid && IsFirewoodSellAction(ctx.ActionName),
                Timing        = ActionTiming.AfterWrapped,
                After         = FirewoodSpawnSalt,
            });

            Plugin.Logger.LogDebug("[InnPatch] initialized.");
        }

        // ── Action name predicates ────────────────────────────────────────────

        private static bool IsSleepAction(string name) =>
            name != null && name.Contains("Night");

        private static bool IsMealAction(string name) =>
            name != null && name.Contains("Meal");

        private static bool IsDepositAction(string name) =>
            name != null && name.Contains("Deposit");

        private static bool IsFirewoodSellAction(string name) =>
            name != null && name.Contains("Firewood");

        // ── Sell Firewood CI ───────────────────────────────────────────────────────

        private static void FirewoodSpawnSalt(ActionContext ctx)
        {
            float sd4   = _pendingFirewoodSd4;
            _pendingFirewoodSd4 = -1f;
            // Oak (SD4=1) and Alder (SD4=5) are harder to reach — pay 2 Salt; others 1.
            int count = (sd4 == 1f || sd4 == 5f) ? 2 : 1;
            for (int i = 0; i < count; i++)
                SpawnService.Spawn(SaltGuid);
            Plugin.Logger.LogDebug($"[InnPatch] Firewood sell (SD4={sd4:0}): spawned {count} salt.");
        }

        // ── Pre-emptive blocked-message tooltip (native vanilla gating) ────────

        private static void TryPatchBlockedMessage(Harmony harmony)
        {
            try
            {
                var cardActionType = AccessTools.TypeByName("CardAction");
                if (cardActionType == null)
                {
                    Plugin.Logger.LogWarning("[InnPatch] CardAction type not found — blocked-message tooltip inactive.");
                    return;
                }

                var method = AccessTools.Method(cardActionType, "CollectActionModifiers");
                if (method == null)
                {
                    Plugin.Logger.LogWarning("[InnPatch] CollectActionModifiers not found — blocked-message tooltip inactive.");
                    return;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(typeof(InnPatch), nameof(CollectActionModifiers_Postfix)));
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[InnPatch] TryPatchBlockedMessage failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // __0 = _ReceivingCard (InGameCardBase) — the card the DismantleAction belongs to.
        private static void CollectActionModifiers_Postfix(object __instance, object __0)
        {
            try
            {
                if (__instance == null || __0 == null) return;
                if (CardUtil.GetCardUniqueId(__0) != CounterUid) return;

                string actionName = CardUtil.GetActionName(__instance);
                bool isMeal = IsMealAction(actionName);
                bool isSleep = IsSleepAction(actionName);
                if (!isMeal && !isSleep) return;

                float price = isMeal ? MealPrice : SleepPrice;
                float balance = CardUtil.GetDurability(__0, BalanceStat);
                if (!float.IsNaN(balance) && balance >= price) return; // affordable — leave message untouched

                // Don't clobber a message another mechanism may have already set this pass.
                if (Reflect.GetMember(__instance, "ActionBlockedMessage") is string existing && !string.IsNullOrEmpty(existing)) return;

                Reflect.SetMember(__instance, "ActionBlockedMessage", NoBalanceMessage);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[InnPatch] CollectActionModifiers_Postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Account balance gate/consume (Meal/Sleep) ──────────────────────────

        private static bool BalanceGate(ActionContext ctx, float price)
        {
            float balance = CardUtil.GetDurability(ctx.Card, BalanceStat);
            bool affordable = !float.IsNaN(balance) && balance >= price;
            if (!affordable)
                Plugin.Logger.LogDebug($"[InnPatch] '{ctx.ActionName}' blocked — Inn account balance {balance:0} below price {price:0}.");
            return !affordable; // return true = cancel
        }

        private static void DeductBalance(ActionContext ctx, float price)
        {
            float balance = CardUtil.GetDurability(ctx.Card, BalanceStat);
            if (float.IsNaN(balance)) balance = 0f;
            CardUtil.SetDurability(ctx.Card, BalanceStat, Math.Max(0f, balance - price));
            CardVisualsRefresh.RefreshDurabilityVisuals(ctx.Card);
            CardVisualsRefresh.RefreshOpenInventoryPopup();
            Plugin.Logger.LogDebug($"[InnPatch] '{ctx.ActionName}' drew {price:0} from the Inn account (balance now {Math.Max(0f, balance - price):0}).");
        }

        // ── Deposit CI ──────────────────────────────────────────────────────────

        private static bool DepositGate(ActionContext ctx)
        {
            float balance = CardUtil.GetDurability(ctx.Card, BalanceStat);
            bool full = !float.IsNaN(balance) && balance >= AccountMax - 0.5f;
            Plugin.Logger.LogDebug($"[InnPatch] DepositGate fired: balance={balance:0} full={full}");
            return full; // return true = cancel
        }

        private static bool DepositApply(ActionContext ctx)
        {
            // CONFIRMED WORKING 2026-08-09 (log-verified: three successful deposits in a live
            // session, 0->50->100, demoted from the 2026-08-08 TEMP DIAGNOSTIC LogInfo escalation
            // per CLAUDE.md Debugging Discipline — see project_cmc_innkeeper_academy_deposit_bug
            // memory). AcademyPatch's identical DepositGate/DepositApply pair remains unconfirmed
            // (never exercised in that session) — do NOT assume it's fixed by this evidence alone.
            string givenUid = CardUtil.GetCardUniqueId(ctx.GivenCard) ?? "null";
            float givenSd4 = CardUtil.GetDurability(ctx.GivenCard, "SpecialDurability4");
            Plugin.Logger.LogDebug($"[InnPatch] DepositApply fired: GivenCard UID={givenUid} SD4={givenSd4}");

            float value = CurrencyValue.ValueOf(ctx.GivenCard);
            if (value <= 0f)
            {
                Plugin.Logger.LogWarning($"[InnPatch] Deposit triggered but the dragged card (UID={givenUid}) had no recognized currency value.");
                return true;
            }

            float balance = CardUtil.GetDurability(ctx.Card, BalanceStat);
            if (float.IsNaN(balance)) balance = 0f;
            float newBalance = Math.Min(balance + value, AccountMax);
            CardUtil.SetDurability(ctx.Card, BalanceStat, newBalance);
            CardVisualsRefresh.RefreshDurabilityVisuals(ctx.Card);
            CardVisualsRefresh.RefreshOpenInventoryPopup();
            Plugin.Logger.LogDebug($"[InnPatch] Deposited {value:0} into the Inn account (balance now {newBalance:0}/{AccountMax:0}).");
            return true;
        }
    }
}
