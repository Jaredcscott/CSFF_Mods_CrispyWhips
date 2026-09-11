using System;
using System.Collections;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The Village Inn (cmcInnInterior), Village Academy (cmcAcademyInterior), Village Hall
    /// (cmcVillageHallInterior), Miller's Cottage (cmcMillerCottageInterior), Weaver's Cottage
    /// (cmcWeaverCottageInterior), and the Apothecary's Cabin (cmcApothecaryCabinInterior) each carry
    /// their own vanilla Fireplace (e50543ef8a7e7d543a42e199adeee963), dropped via each interior's
    /// DefaultEnvCardDrops. Warmth comes entirely from vanilla's own Fireplace mechanism now — the
    /// "Body Temp"/"Indoors Temp" PassiveEffects present only on the lit CardData, absent on
    /// FireplaceExtinguished — the exact same source every player-built fireplace uses, so it
    /// naturally caps at a comfortable level instead of being able to cook a player to death. This
    /// REPLACES the old "Hearth-Warmed" PassiveEffect (an unconditional RateModifier directly on
    /// Body Temperature, independent of whether any fire was actually lit) and its compensating
    /// IndoorHeatCapPatch (a C# patch that force-set Body Temperature to a target value every tick)
    /// — both removed in CMC 1.65.2. See CHANGELOG 1.65.2 for the incident that prompted the
    /// redesign (a player died of heat stroke in the Inn despite the force-set patch already
    /// running). The three cottages/cabin were added in the same manner (Village_Master_Plan.md
    /// §10.11 Chunk C) — no unconditional RateModifier was reintroduced, only the same proven
    /// mechanism extended to three more interiors.
    ///
    /// FUEL GATE (CMC 1.68.8, Village_Master_Plan.md §10.11 Chunk G): the auto-maintenance below
    /// is no longer free. Every ONGOING top-off and every re-light of a hearth that actually
    /// burned down through real play is paid for out of the Town Wood Pile's shared pool (<see
    /// cref="TownWoodPilePatch"/>, cmcStatTownWoodPileFuel) in the same FuelCapacity units the
    /// fireplace burns, and the debit is all-or-nothing — a pool that cannot cover a whole top-off
    /// buys nothing, and the hearth burns down on its own normal schedule until a player restocks
    /// the pile in the village. This applies to ALL SIX interiors listed below, per the owner's
    /// confirmed scope call on §9 item 22 (the originating player report named only the Inn and
    /// Academy; the gate covers everything this patch covers). Reading the pool fails OPEN: an
    /// unreadable stat lets the top-off through rather than freezing every village hearth behind a
    /// broken stat.
    ///
    /// EXCEPTION — first ignition is free (CMC 1.68.13 bugfix, see TopOffIfLow): a hearth found at
    /// CurrentFuel=0 while still classified as the lit Fireplace card was never actually lit in
    /// the first place (DefaultEnvCardDrops spawns the vanilla template as-is, and that template's
    /// FuelCapacity starts at 0) — this is a construction detail of the building, not something
    /// the village's shared wood pile should have to pay for. Only fuel actually burned through
    /// real play draws on the pool.
    ///
    /// Each hearth is meant to feel staffed/maintained, so none of the six should go cold while
    /// the village pile is stocked. The vanilla Fireplace dropped into each interior drains its FuelCapacity stat by
    /// 1/DTP tick (MaxValue 96) same as anywhere else, and at 0 its own OnZero transforms it into
    /// FireplaceExtinguished (58523f8a86c4e0347b93d4a8ff192a13), which then requires the player to
    /// manually re-feed and re-light it. Every DTP tick while the player is inside one of the six
    /// interiors, top that interior's fire back off to full the moment it drops to 20%.
    ///
    /// FuelCapacity decays 1/tick, and this check runs every tick while present, so under normal
    /// play the value is always caught stepping through 20%..19% and never reaches 0 (no jump can
    /// skip past the threshold) PROVIDED at least one DtpTick actually fires while the player is
    /// standing in the room. See the ENTRY POLL note below for the case where that doesn't happen
    /// and the fire is caught cold instead. Either way, FireplaceExtinguished is revived back to a
    /// full Fireplace by the same check.
    ///
    /// UID SCOPING (CMC 1.65.2 fix, carried over from the Inn-only original): FireplaceLitUid/
    /// FireplaceExtinguishedUid are the vanilla Fireplace UIDs — the SAME UID any player can build
    /// anywhere, and UniqueOnBoard is false on vanilla Fireplace, so a player can place their own
    /// fireplace inside any of these six interiors too. Matching "every card on this board with
    /// this UID" can't distinguish a building's own built-in hearth from a player-owned one and
    /// will force-refill both — the forced SetDurability doesn't go through the normal ignition
    /// path, so a caught fireplace looks fully fueled but produces no heat until manually
    /// extinguished and relit (reported bug: player's own fireplace going "infinite fuel, can't
    /// heat up"). Since there's no stable per-instance identity to key on here (object references
    /// don't survive a save/reload, and none of the six hearths carries a distinguishing marker),
    /// this patch only acts when EXACTLY ONE Fireplace/FireplaceExtinguished card is present on the
    /// current board — the expected count for that building's own hearth. If a player adds a
    /// second one, auto-maintenance backs off entirely for both rather than guessing which is the
    /// building's, since silently touching a player-owned fireplace is worse than a hearth
    /// occasionally needing a manual re-light.
    ///
    /// ENTRY POLL (CMC 1.68.12, player report — "Academy fireplace never refills, Inn hearth
    /// stuck at 100%"): the DTP-tick hook above is necessary but was not sufficient. DayTimePoints
    /// in this game only advance on a DaytimeCost-spending action — a player who steps into a
    /// building, glances at it, and leaves without doing anything that costs time passes ZERO
    /// DtpTick events while physically present, so a hearth that spawned dead (or burned out
    /// between visits) was never caught. A save-file inspection confirmed this exactly: five of
    /// the six built-in hearths sat at FuelCapacity=0 from the tick they were first created and
    /// were never revived, because the player's visits to those rooms never happened to include a
    /// time-costing action; the sixth (the Inn, where the player lingers and interacts) had
    /// already self-corrected the same way this fix generalizes. <see cref="TickEvents.Interval"/>
    /// adds a cheap (1s, unscaled real time) poll that detects the moment
    /// <see cref="GameQuery.CurrentEnvironmentUniqueId"/> actually CHANGES into one of the six
    /// hearth envs and runs the exact same maintenance check immediately — no DTP tick required.
    /// This closes the gap without changing the fuel economy at all: it is still gated by the same
    /// pool check, the same 20% threshold, and the same exactly-one-fireplace guard; it just no
    /// longer depends on the player happening to spend time while standing in the room.
    /// </summary>
    internal static class VillageFireplacePatch
    {
        private static readonly string[] HearthEnvUids = { "cmcInnInterior", "cmcAcademyInterior", "cmcVillageHallInterior", "cmcMillerCottageInterior", "cmcWeaverCottageInterior", "cmcApothecaryCabinInterior" };
        private const string FireplaceLitUid = "e50543ef8a7e7d543a42e199adeee963";
        private const string FireplaceExtinguishedUid = "58523f8a86c4e0347b93d4a8ff192a13";
        private const float RefillThresholdFraction = 0.20f;
        private const string FuelStat = "FuelCapacity";

        private static bool _initialized;
        private static string _lastPolledEnvUid;
        private static int _pollAttempts;
        private static bool _pollSettled;

        // The environment-entry poll gets MaxEntryAttempts tries (1s apart) to reach a verdict
        // before it gives up on that visit. See OnEnvironmentPoll for why one try is not enough.
        private const int MaxEntryAttempts = 15;

        // One Info line per CHANGE of verdict per environment. RunMaintenance runs on every DTP
        // tick AND every environment change across six hearths, so an unconditional Info line here
        // floods LogOutput.log — that flooding is exactly why audit finding A10 demoted these to
        // LogDebug, which in turn left three sessions unable to diagnose this at all because
        // BepInEx excludes Debug from the disk log by default. Keying on the verdict string keeps
        // a steady state to a single line while still always reporting a transition, so the
        // subsystem stays diagnosable in a default install without re-introducing the flood.
        private static readonly Dictionary<string, string> _lastVerdict = new Dictionary<string, string>();

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            TickEvents.DtpTick += OnDtpTick;
            TickEvents.Interval(1f, OnEnvironmentPoll, "VillageFireplacePatch.EnvPoll");
            Plugin.Logger.LogDebug("[VillageFireplacePatch] initialized.");
        }

        private static void OnDtpTick() => RunMaintenance(GameQuery.CurrentEnvironmentUniqueId);

        // Edge-triggered on the current environment actually changing — catches a dead/zero-fuel
        // hearth the instant the player steps in, without waiting for a DTP-costing action.
        //
        // RETRY UNTIL SETTLED (bugfix, retro cmc-village-hearth-fuel-not-refilling Attempt 3).
        // The original form latched `_lastPolledEnvUid = envUid` BEFORE calling RunMaintenance and
        // regardless of its outcome, which gave each room entry exactly ONE attempt, taken up to a
        // second after the environment changed. That single sample can easily land while the board
        // is still being assembled: vanilla's ChangeEnvironment assigns
        // `CurrentEnvironment = NextEnvironment` (.decomp/GameManager.cs:10368) and only AFTER that
        // runs the per-missed-tick catch-up loop (one ApplyRates coroutine per tick, each awaited
        // across frames) and finally CheckForMissingDefaultCardsInEnv, which is what creates a
        // hearth that is not already in the saved card set. So GameQuery.CurrentEnvironmentUniqueId
        // reports the interior for a long window during which FindAllLiveCards can legitimately
        // find zero fireplace cards. A latch there silently skipped the entire visit, and the
        // DtpTick path could not cover for it because a player who walks in and straight back out
        // spends no DayTimePoints at all — which is the exact case the entry poll was added for.
        // Now the environment is only marked settled once RunMaintenance actually reaches a
        // verdict, with a bounded attempt count so a genuinely undecidable room cannot spin.
        private static void OnEnvironmentPoll()
        {
            var envUid = GameQuery.CurrentEnvironmentUniqueId;

            if (envUid != _lastPolledEnvUid)
            {
                Plugin.Logger.LogDebug($"[VillageFireplacePatch] EnvPoll detected change: '{_lastPolledEnvUid ?? "(null)"}' -> '{envUid ?? "(null)"}'");
                _lastPolledEnvUid = envUid;
                _pollAttempts = 0;
                _pollSettled = false;
            }

            if (_pollSettled) return;

            _pollAttempts++;
            if (RunMaintenance(envUid))
            {
                _pollSettled = true;
                return;
            }

            if (_pollAttempts >= MaxEntryAttempts)
            {
                _pollSettled = true;
                if (!string.IsNullOrEmpty(envUid) && Array.IndexOf(HearthEnvUids, envUid) >= 0)
                    LogVerdict(envUid, $"auto-tend gave up after {MaxEntryAttempts} attempts — no single hearth card ever appeared on this board.");
            }
        }

        /// <summary>
        /// Emits an Info line only when this environment's verdict CHANGES. See the
        /// <see cref="_lastVerdict"/> remarks for why this is deliberately not an unconditional log.
        /// </summary>
        private static void LogVerdict(string envUid, string verdict)
        {
            if (string.IsNullOrEmpty(envUid)) return;
            if (_lastVerdict.TryGetValue(envUid, out var previous) && previous == verdict) return;
            _lastVerdict[envUid] = verdict;
            Plugin.Logger.LogInfo($"[VillageFireplacePatch] '{envUid}': {verdict}");
        }

        /// <summary>
        /// Runs the top-off/revive check for <paramref name="envUid"/>.
        /// Returns true when a VERDICT was reached (including "this is not a hearth environment,
        /// nothing to do here"), false when the board was not in a readable state yet and the
        /// caller should try again. <see cref="OnEnvironmentPoll"/> uses that to avoid marking a
        /// room handled on a sample taken mid-environment-transition.
        /// </summary>
        private static bool RunMaintenance(string envUid)
        {
            try
            {
                if (string.IsNullOrEmpty(envUid) || Array.IndexOf(HearthEnvUids, envUid) < 0) return true;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return false;

                var litCards = FindAllLiveCards(gm, FireplaceLitUid);
                var extinguishedCards = FindAllLiveCards(gm, FireplaceExtinguishedUid);

                // Branch trace for the hearth-refill path (CMC 1.68.13 bugfix, retro
                // cmc-village-hearth-fuel-not-refilling). Was a TEMP LogInfo diagnostic while the
                // 2026-08-27 "Academy fireplace still not refilling" report was open; the
                // dead-on-arrival spawn was confirmed as the real branch, so it is now a
                // LogDebug breadcrumb. RunMaintenance runs every DTP tick AND every environment
                // change across six hearths, so this must never return to LogInfo.
                Plugin.Logger.LogDebug($"[VillageFireplacePatch] RunMaintenance('{envUid}'): lit={litCards.Count}, extinguished={extinguishedCards.Count}, pool={TownWoodPilePatch.CurrentFuel():0}");

                int total = litCards.Count + extinguishedCards.Count;

                // ZERO is "not ready", not a verdict. During ChangeEnvironment the board is empty
                // for a stretch while the current environment ALREADY reports as this interior, so
                // treating zero as a decision is what let a whole visit slip past (see
                // OnEnvironmentPoll). Report not-settled so the poll tries again next second.
                if (total == 0)
                {
                    Plugin.Logger.LogDebug($"[VillageFireplacePatch] '{envUid}': no fireplace-type card on the board yet — will retry.");
                    return false;
                }

                // TWO OR MORE is a real verdict: a player-placed fireplace is sharing the room and
                // there is no way to tell it from the building's own, so auto-maintenance backs off
                // for both. This is permanent and silent from the player's side, so it is one of
                // the few things worth an Info line.
                if (total != 1)
                {
                    LogVerdict(envUid, $"auto-tend disabled — {total} fireplace-type cards on this board (expected 1). A player-placed fireplace is probably sharing the room; remove it to restore auto-tending.");
                    return true;
                }

                foreach (var card in litCards)
                    TopOffIfLow(card, envUid);

                foreach (var card in extinguishedCards)
                    Revive(card, envUid);

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[VillageFireplacePatch] RunMaintenance failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }

        private static void TopOffIfLow(object card, string envUid)
        {
            float max = CardUtil.GetDurabilityMax(card, FuelStat);
            float currentRaw = CardUtil.GetDurability(card, FuelStat);
            Plugin.Logger.LogDebug($"[VillageFireplacePatch] TopOffIfLow('{envUid}'): max={max}, current={currentRaw}, threshold={(float.IsNaN(max) ? -1f : max * RefillThresholdFraction):0}");
            if (float.IsNaN(max) || max <= 0f) return;

            float current = currentRaw;
            if (float.IsNaN(current) || current > max * RefillThresholdFraction) return;

            // DEAD-ON-ARRIVAL SPAWN (CMC 1.68.13 bugfix — see
            // Documentation/Retrospectives/cmc-village-hearth-fuel-not-refilling.md): vanilla
            // Fireplace.json's FuelCapacity.FloatValue is 0 — a real player-built fireplace never
            // starts this way (it's always ignited via an explicit CardAction), but
            // DefaultEnvCardDrops spawns this exact template directly, so all six built-in hearths
            // begin life at CurrentFuel=0. Vanilla's own OnZero transform (.decomp/
            // InGameCardBase.cs:8338, `durabilityStat.HasActionOnZero && num2<=0.0001f &&
            // (num>0f || ...)`) only fires on a genuine positive->zero crossing — a card that
            // spawns AT zero never satisfies num>0, so it can never self-transform to
            // FireplaceExtinguished and is stuck reading as "Fireplace" at 0% forever. Critically,
            // this means current<=0 on a still-"lit"-classified card is UNAMBIGUOUS: any hearth
            // that actually burned down through real play would already have crossed that same
            // check with num>0 and be sitting in extinguishedCards (Revive's territory) by the
            // time RunMaintenance sees it, not here. So this branch can only be a hearth that was
            // never ignited in the first place — lighting the building's OWN fixture for the first
            // time is a construction detail, not a draw on the village's shared wood stockpile.
            // Ongoing top-offs below (current > 0, a real mid-burn low point) still cost the pool.
            if (current <= 0.0001f)
            {
                if (!CardUtil.SetDurability(card, FuelStat, max))
                {
                    LogVerdict(envUid, "free first ignition FAILED — SetDurability refused the write.");
                    return;
                }

                // Read the value back off the same card before claiming success. SetDurability
                // returning true only means a reflection write did not throw; it does NOT prove the
                // value landed on the instance the game will save. If it silently diverges, the
                // hearth stays at 0, stays classified lit (vanilla's OnZero needs a genuine
                // positive->zero crossing, .decomp/InGameCardBase.cs:9366, so a card sitting at 0
                // can never transform itself), and this branch re-fires on every single tick —
                // which reads as "the fix ran and the hearth is still cold" and would also flood
                // the log. Verdict-gated so it reports once per environment instead.
                float readBack = CardUtil.GetDurability(card, FuelStat);
                if (float.IsNaN(readBack) || readBack < max - 0.5f)
                {
                    LogVerdict(envUid, $"free first ignition did NOT stick — wrote {max:0}, read back {readBack}. The write is not reaching the card the game saves.");
                    return;
                }

                CardVisualsRefresh.RefreshDurabilityVisuals(card);
                CardVisualsRefresh.RefreshOpenInventoryPopup();
                // Wording preserved verbatim: cmc-village-hearth-fuel-not-refilling names this exact
                // string as the signal to grep for. Do not reword without updating that retro.
                Plugin.Logger.LogInfo($"[VillageFireplacePatch] '{envUid}' hearth spawned dead (never ignited) — lit for free, no pool debit (0 -> {max:0}).");
                _lastVerdict.Remove(envUid);
                return;
            }

            // The fuel comes out of the Town Wood Pile's shared pool, in the same FuelCapacity
            // units. All-or-nothing: if the pile cannot cover the whole top-off we skip entirely
            // and let the hearth burn down on its normal schedule, rather than holding it at a
            // permanent trickle (Village_Master_Plan.md §10.11 Chunk G item 6).
            //
            // ORDER IS LOAD-BEARING, and matches Revive below: PEEK the pool, apply the refill,
            // and only debit once SetDurability has actually reported success. Debiting first (the
            // first version) and then discarding SetDurability's bool return meant a failed refill
            // still burned the village's fuel — the player pays and the hearth stays cold.
            float needed = max - current;
            float pool = TownWoodPilePatch.CurrentFuel();
            Plugin.Logger.LogDebug($"[VillageFireplacePatch] TopOffIfLow('{envUid}'): needed={needed:0}, pool={pool:0}");
            // The pile being too short to pay is the single most player-visible way this system
            // "does nothing", and it was previously only reported at Debug — invisible in a default
            // install, which is why this failure survived multiple reports undiagnosed. Verdict-
            // gated so a hearth sitting unaffordable for days still costs exactly one line.
            if (pool >= 0f && pool < needed)
            {
                LogVerdict(envUid, $"left to burn down — the Town Wood Pile holds {pool:0} but a full top-off needs {needed:0}. Stock the pile to resume auto-tending.");
                return;
            }

            if (!CardUtil.SetDurability(card, FuelStat, max))
            {
                LogVerdict(envUid, "refill FAILED — SetDurability refused the write; pool NOT debited.");
                return;
            }
            TownWoodPilePatch.TryConsume(needed, $"top off the '{envUid}' hearth");
            CardVisualsRefresh.RefreshDurabilityVisuals(card);
            CardVisualsRefresh.RefreshOpenInventoryPopup();
            _lastVerdict.Remove(envUid);
            Plugin.Logger.LogDebug($"[VillageFireplacePatch] Topped off '{envUid}' fireplace fuel ({current:0} -> {max:0}).");
        }

        private static void Revive(object card, string envUid)
        {
            float max = CardUtil.GetDurabilityMax(card, FuelStat);
            if (float.IsNaN(max) || max <= 0f) max = 96f;

            // A revive re-lights from cold, so it costs a FULL hearth's worth of pool fuel. The
            // pool is PEEKED (not debited) before the transform so a pile that cannot pay leaves
            // the card as FireplaceExtinguished for the player to re-light by hand — never a lit
            // fireplace with no fuel behind it. A negative reading means the stat is unreadable;
            // that fails open, matching TryConsume's own stance. The debit lands only after the
            // transform actually succeeded, so a failed transform never silently burns pool fuel.
            float pool = TownWoodPilePatch.CurrentFuel();
            Plugin.Logger.LogDebug($"[VillageFireplacePatch] Revive('{envUid}'): max={max:0}, pool={pool:0}");
            if (pool >= 0f && pool < max)
            {
                LogVerdict(envUid, $"left extinguished — the Town Wood Pile holds {pool:0} but a re-light from cold costs a full {max:0}. Stock the pile, or feed this hearth by hand.");
                return;
            }

            if (!CardUtil.TransformCardInPlace(card, FireplaceLitUid))
            {
                LogVerdict(envUid, "re-light FAILED — TransformCardInPlace returned false; pool NOT debited.");
                return;
            }
            if (!CardUtil.SetDurability(card, FuelStat, max))
            {
                LogVerdict(envUid, "re-light transformed the card but FAILED to set fuel — pool NOT debited.");
                return;
            }
            TownWoodPilePatch.TryConsume(max, $"re-light the '{envUid}' hearth");
            CardVisualsRefresh.RefreshDurabilityVisuals(card);
            CardVisualsRefresh.RefreshOpenInventoryPopup();
            _lastVerdict.Remove(envUid);
            Plugin.Logger.LogDebug($"[VillageFireplacePatch] Re-lit and topped off an extinguished '{envUid}' fireplace.");
        }

        // Every live in-game instance of the given card UID on the CURRENT board.
        //
        // AllCards is NOT purely current-environment-scoped, despite the shorthand this comment
        // used to carry. ChangeEnvironment clears it and then re-adds `cardsRemainingInBG`
        // (.decomp/GameManager.cs:10321-10328) — carried inventory, IndependentFromEnv cards, the
        // hand/weather/event cards — so a card belonging to no board at all, or one the player is
        // carrying, is in the list too. The game's own lookups therefore always pair the UID test
        // with an environment test (e.g. .decomp/GameManager.cs:1657-1659, which gates on
        // `CardEnvironment.MatchesEnv(_InEnv)`), and this one did not. That matters here because
        // the caller's "exactly one fireplace-type card" guard turns any extra match into a
        // permanent, silent shutdown of auto-tending for that room — so a stray fireplace-type
        // card that is not even on this board could disable the building's own hearth.
        // Filtering by the same EnvID.MatchesPlayerEnv the framework uses in
        // GameQuery.IsInPlayerEnv keeps the count to cards actually in the room.
        private static List<object> FindAllLiveCards(object gm, string uid)
        {
            var found = new List<object>();
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return found;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                if (CardUtil.GetCardUniqueId(card) != uid) continue;
                if (!IsInCurrentEnv(card)) continue;
                found.Add(card);
            }
            return found;
        }

        /// <summary>
        /// True when the card is on the player's current board. Same two-step read the framework's
        /// own <c>GameQuery.IsInPlayerEnv</c> uses: <c>CardEnvironment</c> (an <c>EnvID</c>) then
        /// its <c>MatchesPlayerEnv</c> property, which resolves to
        /// <c>MatchesEnv(GameManager.Instance.CurrentEnvironment)</c>.
        /// </summary>
        private static bool IsInCurrentEnv(object card)
        {
            var env = CardUtil.GetMemberValue(card, "CardEnvironment");
            if (env == null) return false;
            return CardUtil.GetMemberValue(env, "MatchesPlayerEnv") is bool inEnv && inEnv;
        }
    }
}
