using System;
using System.Collections;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// D3, bribes. Two ways to buy a softer Watch, both tunable placeholders and neither able to clear crime.
    ///
    /// <para><b>Pay a guard.</b> Dragging Salt or a metal Nugget onto Guard Old Corrin, Thorne or Captain Sterling
    /// (a DragAndDropAction on each agent, key prefix <see cref="GuardBribeKeyPrefix"/>; the JSON consumes the
    /// given card) lowers THAT guard's own Suspicion by <see cref="BribeReduction"/>, scaled by her personality:
    /// the lower her starting Suspicion, the cheaper she is. The Suspicion stat's floor is her starting value
    /// (each Perk_Guard*.json), so a bribe can only take a raised Suspicion back down to her baseline, never
    /// below it. Iris Vane's agent has no such action: she does not take it.</para>
    ///
    /// <para><b>Pay the Warden.</b> The Jail Cell's "Pay the Warden" DismantleAction (JSON) is shown only while a
    /// warden is on the step (<c>cmcStatJailUnguarded</c> 0), the latch <see cref="BribeUsedStatUid"/> is 0 and
    /// at least two days remain. Its JSON sets the latch (clamped 0..1, reset on every new sentence by
    /// <see cref="JailPatch"/>); this class refuses it unless enough Salt is in hand, then takes the Salt and one
    /// day off the sentence. The fee is a base priced through <see cref="VillagePricing.Apply"/> (the crime band
    /// marks it up) and paid in whole Salt. Crime is untouched: the jail clears it only through days served.</para>
    ///
    /// <para>Registered once per process (the early return on <c>_initialized</c> is load-bearing, root CLAUDE.md
    /// "Per-Run Event Registration"). Every miss leaves a warn-once line keyed on its cause.</para>
    /// </summary>
    internal static class GuardBribePatch
    {
        /// <summary>LocalizationKey prefix shared by the three guards' bribe actions.</summary>
        internal const string GuardBribeKeyPrefix = "CMC_GuardBribe_";

        /// <summary>The Jail Cell location card that owns the Pay the Warden DismantleAction.</summary>
        internal const string JailCellLocationUid = "cmcJailCellLocation";
        internal const string PayWardenKeyPrefix = "CMC_JailCellLocation_DA_PayWarden";
        private const string PayWardenActionName = "Pay the Warden";

        /// <summary>0..1: the warden was already paid this sentence. JailPatch resets it on each sentence.</summary>
        internal const string BribeUsedStatUid = "cmcStatJailBribeUsed";

        /// <summary>TUNABLE PLACEHOLDER: Suspicion a bribe removes from a guard whose starting Suspicion is
        /// <see cref="BribeReferenceStart"/> (Captain Sterling).</summary>
        internal const float BribeBaseReduction = 12f;
        internal const float BribeReferenceStart = 40f;
        /// <summary>TUNABLE PLACEHOLDER: no bribe removes more than this, however low her start.</summary>
        internal const float BribeMaxReduction = 20f;

        /// <summary>TUNABLE PLACEHOLDER: the warden's fee before the village's crime-band markup, in currency
        /// units (a Salt is <see cref="SaltUnitValue"/>).</summary>
        internal const float WardenBaseFee = 45f;

        /// <summary>What one Salt is worth. Mirrors CurrencyValue.SaltValue (private there; CMC-Round2.Tests.ps1
        /// asserts the two agree).</summary>
        internal const float SaltUnitValue = 15f;

        private static readonly string[] BribableGuardAgentUids =
        {
            "cmcGuardSterlingAgent", "cmcGuardThorneAgent", "cmcGuardCorrinAgent",
        };

        private static bool _initialized;
        private static readonly System.Collections.Generic.HashSet<string> _warned = new System.Collections.Generic.HashSet<string>();

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning("[GuardBribePatch] " + message);
        }

        // ---- Pure, so a test or a harness probe can feed them any number ----

        /// <summary>Suspicion a guard sheds from one bribe, given her starting Suspicion: the lower the start, the
        /// larger the cut. An unusable start gives 0.</summary>
        internal static float BribeReduction(float start)
        {
            if (float.IsNaN(start) || start <= 0f) return 0f;
            return Math.Min(BribeMaxReduction, BribeBaseReduction * BribeReferenceStart / start);
        }

        /// <summary>Whole Salt the warden wants for a fee already priced by the village (at least 1).</summary>
        internal static int WardenFeeSalt(float pricedFee)
        {
            return Math.Max(1, (int)Math.Ceiling(pricedFee / SaltUnitValue));
        }

        /// <summary>The sentence left after paying: one day off, never below the one day that still has to be
        /// served, so a bribe cannot release anyone and never skips the crime clearing at the end of the term.</summary>
        internal static float SentenceAfterBribe(float remaining)
        {
            return Math.Max(1f, remaining - 1f);
        }

        // ---- Registration ----

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            ActionRouter.Register(new ActionHandler
            {
                Name = "GuardBribeApply",
                CardPredicate = ctx => ctx.ActionKey != null && ctx.ActionKey.StartsWith(GuardBribeKeyPrefix, StringComparison.Ordinal),
                ActionKeyPrefix = GuardBribeKeyPrefix,
                Timing = ActionTiming.AfterWrapped,
                After = ApplyGuardBribe,
            });

            ActionRouter.Register(new ActionHandler
            {
                Name = "JailPayWardenGate",
                CardUid = JailCellLocationUid,
                ActionKeyPrefix = PayWardenKeyPrefix,
                Timing = ActionTiming.Cancel,
                Before = ctx => !TryAssessWardenPayment(out _, out _),
            });

            ActionRouter.Register(new ActionHandler
            {
                Name = "JailPayWardenApply",
                CardUid = JailCellLocationUid,
                ActionKeyPrefix = PayWardenKeyPrefix,
                Timing = ActionTiming.AfterWrapped,
                After = ApplyWardenPayment,
            });

            TryPatchBlockedMessage(harmony);
            Plugin.Logger.LogDebug("[GuardBribePatch] initialized.");
        }

        // ---- Pay a guard ----

        private static void ApplyGuardBribe(ActionContext ctx)
        {
            try
            {
                var npc = FindGuardNpc(ctx.Card, out string agentUid);
                if (npc == null)
                {
                    WarnOnce("guard-not-found", "A guard bribe was performed but no live guard matched the receiving card, so no Suspicion was lowered.");
                    return;
                }
                if (Array.IndexOf(BribableGuardAgentUids, agentUid) < 0)
                {
                    WarnOnce("guard-not-bribable:" + agentUid, agentUid + " took a bribe action she should not carry; Suspicion left alone.");
                    return;
                }

                string cause = GuardDutyPatch.TryReadSuspicion(npc, out _, out float current, out float start);
                if (cause != null)
                {
                    WarnOnce("read-" + cause, "A guard bribe could not read the guard's Suspicion (" + cause + "), so none was lowered.");
                    return;
                }

                float cut = BribeReduction(start);
                if (!GuardWantedReactionPatch.ChangeGuardSuspicion(npc, agentUid, -cut, "GuardBribe"))
                {
                    WarnOnce("change-failed", "A guard bribe could not write the guard's Suspicion (see the GuardWantedReactionPatch line), so none was lowered.");
                    return;
                }
                Plugin.Logger.LogInfo($"[GuardBribePatch] {agentUid} pocketed the bribe: Suspicion {current:0.##} - {cut:0.##} (floor {start:0.##}).");
            }
            catch (Exception ex)
            {
                WarnOnce("apply-exception", "ApplyGuardBribe failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        /// <summary>The live guard whose board card IS <paramref name="card"/> (an agent's card has no UniqueID of
        /// its own, so it matches by reference, memory reference_actionrouter_npcagent_carduid).</summary>
        private static object FindGuardNpc(object card, out string agentUid)
        {
            agentUid = null;
            if (card == null) return null;
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) { WarnOnce("gm-null", "No GameManager, so no guard can be matched."); return null; }
            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs)
            {
                WarnOnce("allnpcs-null", "GameManager.AllNPCs is unreadable, so no guard can be matched.");
                return null;
            }
            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                if (!ReferenceEquals(Reflect.GetMember(npc, "AssociatedCard"), card)) continue;
                agentUid = CardUtil.GetCardUniqueId(Reflect.GetMember(npc, "NPCModel"));
                return npc;
            }
            return null;
        }

        // ---- Pay the Warden ----

        /// <summary>The Salt the warden wants right now and the Salt in hand. True when enough is in hand.</summary>
        private static bool TryAssessWardenPayment(out int needed, out int held)
        {
            needed = WardenFeeSalt(VillagePricing.Apply(WardenBaseFee));
            held = 0;
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) { WarnOnce("pay-gm-null", "Pay the Warden: no GameManager, so the payment is refused."); return false; }
            var salt = FindSalt(gm);
            if (salt == null) return false;
            held = salt.Count;
            return held >= needed;
        }

        /// <summary>Every live Salt card the player has (carried or in the cell). Null, with a warn-once line,
        /// when AllCards cannot be read: a refusal the player can see beats a silent free bribe.</summary>
        private static System.Collections.Generic.List<object> FindSalt(object gm)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards)
            {
                WarnOnce("pay-allcards-null", "Pay the Warden: GameManager.AllCards is unreadable, so the payment is refused.");
                return null;
            }
            var found = new System.Collections.Generic.List<object>();
            foreach (var card in allCards)
            {
                if (card == null) continue;
                if (Reflect.GetMember(card, "Destroyed") is bool destroyed && destroyed) continue;
                if (string.Equals(CardUtil.GetCardUniqueId(card), CurrencyValue.SaltGuid, StringComparison.OrdinalIgnoreCase))
                    found.Add(card);
            }
            return found;
        }

        private static void ApplyWardenPayment(ActionContext ctx)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) { WarnOnce("apply-gm-null", "Pay the Warden: no GameManager after the action, so nothing was taken."); return; }
                int needed = WardenFeeSalt(VillagePricing.Apply(WardenBaseFee));
                var salt = FindSalt(gm);
                if (salt == null) return;
                if (salt.Count < needed)
                {
                    Plugin.Logger.LogWarning($"[GuardBribePatch] Pay the Warden ran with {salt.Count} Salt in hand of {needed} wanted; the sentence was left alone.");
                    return;
                }

                float remaining = HiddenStat.Get(JailPatch.SentenceRemainingStatUid);
                if (remaining < 0f) { WarnOnce("sentence-unreadable", "Pay the Warden: the sentence stat is unreadable, so nothing was taken."); return; }

                int taken = 0;
                for (int i = 0; i < needed; i++)
                    if (CardUtil.TryRemoveCard(salt[i])) taken++;

                float next = SentenceAfterBribe(remaining);
                bool wrote = HiddenStat.Set(JailPatch.SentenceRemainingStatUid, next);
                Plugin.Logger.LogInfo($"[GuardBribePatch] The warden took {taken} Salt (wanted {needed}): sentence {remaining:0.##} -> {next:0.##} day(s){(wrote ? "" : " (WRITE FAILED)")}. Crime is unchanged.");
            }
            catch (Exception ex)
            {
                WarnOnce("pay-exception", "ApplyWardenPayment failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        // ---- Blocked-button tooltip, the Inn's native-gating shape ----

        private static void TryPatchBlockedMessage(Harmony harmony)
        {
            try
            {
                var cardActionType = AccessTools.TypeByName("CardAction");
                var method = cardActionType == null ? null : AccessTools.Method(cardActionType, "CollectActionModifiers");
                if (method == null)
                {
                    WarnOnce("blocked-msg-missing", "CardAction.CollectActionModifiers not found: a refused Pay the Warden gives no tooltip.");
                    return;
                }
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(GuardBribePatch), nameof(CollectActionModifiers_Postfix)));
            }
            catch (Exception ex)
            {
                WarnOnce("blocked-msg-patch", "TryPatchBlockedMessage failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        // __0 = _ReceivingCard, the card the DismantleAction belongs to.
        private static void CollectActionModifiers_Postfix(object __instance, object __0)
        {
            try
            {
                if (__instance == null || __0 == null) return;
                if (CardUtil.GetCardUniqueId(__0) != JailCellLocationUid) return;
                if (CardUtil.GetActionName(__instance) != PayWardenActionName) return;
                if (TryAssessWardenPayment(out int needed, out int held)) return;
                if (Reflect.GetMember(__instance, "ActionBlockedMessage") is string existing && !string.IsNullOrEmpty(existing)) return;
                Reflect.SetMember(__instance, "ActionBlockedMessage",
                    "The warden wants " + needed + " Salt in hand first. You have " + held + ".");
            }
            catch (Exception ex)
            {
                WarnOnce("blocked-msg-run", "CollectActionModifiers_Postfix failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }
    }
}
