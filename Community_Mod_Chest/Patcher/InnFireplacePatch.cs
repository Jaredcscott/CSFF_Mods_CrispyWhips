using System;
using System.Collections;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The vanilla Fireplace (e50543ef8a7e7d543a42e199adeee963) dropped into the Inn
    /// interior (cmcInnInterior — see CMC_InnInterior.json DefaultEnvCardDrops) drains its
    /// FuelCapacity stat by 1/DTP tick (MaxValue 96) same as anywhere else, and at 0 its own
    /// OnZero transforms it into FireplaceExtinguished (58523f8a86c4e0347b93d4a8ff192a13),
    /// which then requires the player to manually re-feed and re-light it. The Inn is meant
    /// to feel staffed/maintained, so its hearth should never actually go cold: every DTP
    /// tick while the player is inside, top the fire back off to full the moment it drops to
    /// 20% (matches IndoorHeatCapPatch's per-tick correction idiom for the same environment).
    ///
    /// FuelCapacity decays 1/tick, and this check runs every tick while present, so under
    /// normal play the value is always caught stepping through 20%..19% and never reaches 0
    /// (no jump can skip past the threshold). The only gap is a long real-world-equivalent
    /// absence from the Inn — native tick catch-up on re-entry (ChangeEnvironment replays
    /// ApplyRates per elapsed tick, see root CLAUDE.md "ChangeEnvironment catch-up tick cost")
    /// can run the OnZero transform before this mod ever gets a poll. Cheaply covered by also
    /// reviving FireplaceExtinguished back to a full Fireplace on the same tick check.
    /// </summary>
    internal static class InnFireplacePatch
    {
        private const string InnInteriorEnvUid = "cmcInnInterior";
        private const string FireplaceLitUid = "e50543ef8a7e7d543a42e199adeee963";
        private const string FireplaceExtinguishedUid = "58523f8a86c4e0347b93d4a8ff192a13";
        private const float RefillThresholdFraction = 0.20f;
        private const string FuelStat = "FuelCapacity";

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            TickEvents.DtpTick += OnDtpTick;
            Plugin.Logger.LogDebug("[InnFireplacePatch] initialized.");
        }

        private static void OnDtpTick()
        {
            try
            {
                if (GameQuery.CurrentEnvironmentUniqueId != InnInteriorEnvUid) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                foreach (var card in FindAllLiveCards(gm, FireplaceLitUid))
                    TopOffIfLow(card);

                foreach (var card in FindAllLiveCards(gm, FireplaceExtinguishedUid))
                    Revive(card);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[InnFireplacePatch] OnDtpTick failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void TopOffIfLow(object card)
        {
            float max = CardUtil.GetDurabilityMax(card, FuelStat);
            if (float.IsNaN(max) || max <= 0f) return;

            float current = CardUtil.GetDurability(card, FuelStat);
            if (float.IsNaN(current) || current > max * RefillThresholdFraction) return;

            CardUtil.SetDurability(card, FuelStat, max);
            CardVisualsRefresh.RefreshDurabilityVisuals(card);
            CardVisualsRefresh.RefreshOpenInventoryPopup();
            Plugin.Logger.LogDebug($"[InnFireplacePatch] Topped off Inn fireplace fuel ({current:0} -> {max:0}).");
        }

        private static void Revive(object card)
        {
            float max = CardUtil.GetDurabilityMax(card, FuelStat);
            if (float.IsNaN(max) || max <= 0f) max = 96f;

            if (!CardUtil.TransformCardInPlace(card, FireplaceLitUid)) return;
            CardUtil.SetDurability(card, FuelStat, max);
            CardVisualsRefresh.RefreshDurabilityVisuals(card);
            CardVisualsRefresh.RefreshOpenInventoryPopup();
            Plugin.Logger.LogDebug("[InnFireplacePatch] Re-lit and topped off an extinguished Inn fireplace.");
        }

        // Every live in-game instance of the given card UID on the CURRENT board (AllCards is
        // current-env-scoped) — same idiom as VillageFounderPerkPatch.FindLiveCard /
        // AcademyPatch.FindAllLiveLecternCards.
        private static List<object> FindAllLiveCards(object gm, string uid)
        {
            var found = new List<object>();
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return found;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                if (CardUtil.GetCardUniqueId(card) == uid) found.Add(card);
            }
            return found;
        }
    }
}
