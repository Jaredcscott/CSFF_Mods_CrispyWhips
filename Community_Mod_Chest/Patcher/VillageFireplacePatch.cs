using System;
using System.Collections;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The Village Inn (cmcInnInterior), Village Academy (cmcAcademyInterior), and Village Hall
    /// (cmcVillageHallInterior) each carry their own vanilla Fireplace (e50543ef8a7e7d543a42e199adeee963),
    /// dropped via each interior's DefaultEnvCardDrops. Warmth comes entirely from vanilla's own
    /// Fireplace mechanism now — the "Body Temp"/"Indoors Temp" PassiveEffects present only on the
    /// lit CardData, absent on FireplaceExtinguished — the exact same source every player-built
    /// fireplace uses, so it naturally caps at a comfortable level instead of being able to cook a
    /// player to death. This REPLACES the old "Hearth-Warmed" PassiveEffect (an unconditional
    /// RateModifier directly on Body Temperature, independent of whether any fire was actually lit)
    /// and its compensating IndoorHeatCapPatch (a C# patch that force-set Body Temperature to a
    /// target value every tick) — both removed in CMC 1.65.2. See CHANGELOG 1.65.2 for the incident
    /// that prompted the redesign (a player died of heat stroke in the Inn despite the force-set
    /// patch already running).
    ///
    /// Each hearth is meant to feel staffed/maintained, so none of the three should ever actually
    /// go cold. The vanilla Fireplace dropped into each interior drains its FuelCapacity stat by
    /// 1/DTP tick (MaxValue 96) same as anywhere else, and at 0 its own OnZero transforms it into
    /// FireplaceExtinguished (58523f8a86c4e0347b93d4a8ff192a13), which then requires the player to
    /// manually re-feed and re-light it. Every DTP tick while the player is inside one of the three
    /// interiors, top that interior's fire back off to full the moment it drops to 20%.
    ///
    /// FuelCapacity decays 1/tick, and this check runs every tick while present, so under normal
    /// play the value is always caught stepping through 20%..19% and never reaches 0 (no jump can
    /// skip past the threshold). The only gap is a long real-world-equivalent absence from the
    /// building — native tick catch-up on re-entry (ChangeEnvironment replays ApplyRates per
    /// elapsed tick, see root CLAUDE.md "ChangeEnvironment catch-up tick cost") can run the OnZero
    /// transform before this mod ever gets a poll. Cheaply covered by also reviving
    /// FireplaceExtinguished back to a full Fireplace on the same tick check.
    ///
    /// UID SCOPING (CMC 1.65.2 fix, carried over from the Inn-only original): FireplaceLitUid/
    /// FireplaceExtinguishedUid are the vanilla Fireplace UIDs — the SAME UID any player can build
    /// anywhere, and UniqueOnBoard is false on vanilla Fireplace, so a player can place their own
    /// fireplace inside any of these three interiors too. Matching "every card on this board with
    /// this UID" can't distinguish a building's own built-in hearth from a player-owned one and
    /// will force-refill both — the forced SetDurability doesn't go through the normal ignition
    /// path, so a caught fireplace looks fully fueled but produces no heat until manually
    /// extinguished and relit (reported bug: player's own fireplace going "infinite fuel, can't
    /// heat up"). Since there's no stable per-instance identity to key on here (object references
    /// don't survive a save/reload, and none of the three hearths carries a distinguishing marker),
    /// this patch only acts when EXACTLY ONE Fireplace/FireplaceExtinguished card is present on the
    /// current board — the expected count for that building's own hearth. If a player adds a
    /// second one, auto-maintenance backs off entirely for both rather than guessing which is the
    /// building's, since silently touching a player-owned fireplace is worse than a hearth
    /// occasionally needing a manual re-light.
    /// </summary>
    internal static class VillageFireplacePatch
    {
        private static readonly string[] HearthEnvUids = { "cmcInnInterior", "cmcAcademyInterior", "cmcVillageHallInterior" };
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
            Plugin.Logger.LogDebug("[VillageFireplacePatch] initialized.");
        }

        private static void OnDtpTick()
        {
            try
            {
                var envUid = GameQuery.CurrentEnvironmentUniqueId;
                if (string.IsNullOrEmpty(envUid) || Array.IndexOf(HearthEnvUids, envUid) < 0) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                var litCards = FindAllLiveCards(gm, FireplaceLitUid);
                var extinguishedCards = FindAllLiveCards(gm, FireplaceExtinguishedUid);

                if (litCards.Count + extinguishedCards.Count != 1)
                {
                    Plugin.Logger.LogDebug($"[VillageFireplacePatch] Skipping '{envUid}' — {litCards.Count + extinguishedCards.Count} fireplace-type cards present (expected 1); a player-placed fireplace may be sharing the board, so auto-maintenance is disabled to avoid touching it.");
                    return;
                }

                foreach (var card in litCards)
                    TopOffIfLow(card, envUid);

                foreach (var card in extinguishedCards)
                    Revive(card, envUid);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[VillageFireplacePatch] OnDtpTick failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void TopOffIfLow(object card, string envUid)
        {
            float max = CardUtil.GetDurabilityMax(card, FuelStat);
            if (float.IsNaN(max) || max <= 0f) return;

            float current = CardUtil.GetDurability(card, FuelStat);
            if (float.IsNaN(current) || current > max * RefillThresholdFraction) return;

            CardUtil.SetDurability(card, FuelStat, max);
            CardVisualsRefresh.RefreshDurabilityVisuals(card);
            CardVisualsRefresh.RefreshOpenInventoryPopup();
            Plugin.Logger.LogDebug($"[VillageFireplacePatch] Topped off '{envUid}' fireplace fuel ({current:0} -> {max:0}).");
        }

        private static void Revive(object card, string envUid)
        {
            float max = CardUtil.GetDurabilityMax(card, FuelStat);
            if (float.IsNaN(max) || max <= 0f) max = 96f;

            if (!CardUtil.TransformCardInPlace(card, FireplaceLitUid)) return;
            CardUtil.SetDurability(card, FuelStat, max);
            CardVisualsRefresh.RefreshDurabilityVisuals(card);
            CardVisualsRefresh.RefreshOpenInventoryPopup();
            Plugin.Logger.LogDebug($"[VillageFireplacePatch] Re-lit and topped off an extinguished '{envUid}' fireplace.");
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
