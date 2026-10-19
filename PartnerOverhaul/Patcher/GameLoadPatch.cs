namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// Fixes 6 confirmed vanilla data-authoring gaps by mutating existing vanilla CardData
    /// objects at boot, once WarpResolver/ForeignInstanceReconciler have already run (this
    /// mod's framework SoftDependency guarantees load order — see Plugin.cs). Every fix is
    /// idempotent (checks before mutating) and independently try/caught so one missing card
    /// on a future game update can't take the others down with it.
    ///
    /// Root-cause research: the plan doc that documented it, Documentation/Plans/PartnerOverhaul/
    /// PartnerOverhaul_Plan.md, was retired 2026-09-05 (171efccbb) once all 6 fixes below had
    /// shipped; read it pre-deletion via `git show 171efccbb^:Documentation/Plans/PartnerOverhaul/
    /// PartnerOverhaul_Plan.md`. GUIDs for each target are cited inline below instead.
    /// </summary>
    public static class GameLoadPatch
    {
        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;

        // Vanilla CardData UIDs (Documentation/GameData/.../UniqueIDScriptableGUID/CardData.json)
        private const string OldLqStewWaterUid = "a0e1cf6d47685a741b5cd9889fb39227";
        private const string LqNightBrothUid = "688d181eb1ea12642a028b4943ec6013";
        private const string CabinUid = "82d66ecd24814a94abd8446862a041b7";
        private const string MudHutUid = "8dad01b8594c63a4883cc49170bc70eb";
        private const string EnclosureUid = "066ad822f09706e4087e2c1af6b50d26";
        private const string MudHutEnclosureUid = "77ff1cc7a0ee7d445a3ca20fca021efc";
        private const string MudHutExpansionLeftUid = "d077e3b6aee2d884dbd77059db9423cf";
        private const string GardenPlotUid = "f853a0a8ad6c64c4b81eabb8367c0c5c";
        private const string FireplaceUid = "e50543ef8a7e7d543a42e199adeee963";
        private const string FirePitUid = "f16aa437d91e2434ab7a4b7a8827a826";
        // Campfire is only the READ-ONLY reference for the PartnerAction1/2/3 ActionTag assets
        // (its wired Feed actions carry them); nothing on it is mutated here.
        private const string CampfireUid = "63e4efe772bf6f649b30c893a0257ef0";
        private const string HearthUid = "e6cc2a4d002a46745abc87aac39680b6";
        private const string HearthAwakenedUid = "760141d2251da2947b2d537c4b2eeacb";
        // The cards a Hearth and a Hearth (Awakened) become when their fuel runs out. The game's
        // Transform carries a duty mark over only where the new card lists that duty in its
        // CompatibleDuties (GameManager dutiesToTransfer, W0.3), so both need Fire Keeping or the
        // mark is dropped at the moment the fire dies (T2.502 steps 97 and 103, MEASURED).
        private const string HearthExtinguishedUid = "9fa420dfadae9ef47bc2086ddd8d1717";
        private const string HearthAwakenedExtinguishedUid = "f6c8cbd6ffe2bbf40acd398ca3e911fd";
        private const string MudHutExpansionRightUid = "72ea743da5b1a1a4ea25297a2f71f8b5";
        private const string BoarPigletCarcassUid = "b770a77711ccb1246b20561781384d0b";
        private const string BlankArrowBroadheadUid = "2d4b3ba562fd1aa43858448ffafeb6b4";
        private const string BlankChiselBladeUid = "b34f3d5938e9f5a4dbae651248d1c1bc";
        private const string WheatBloodbreadUid = "4c9de778d3a4b884da84272757b2e781";
        private const string RiverUid = "f6ba7b43e3e920b49a84510ca27f527c";
        private const string FrozenRiverUid = "34b1b24e75a310d43b1466055284b4f8";
        private const string PondFrozenUid = "a30a71b1dda89d44d92e7e64d3cbc679";
        private const string HeatherPatchUid = "efa6ac9e8a811b441b8e4896b393ea23";
        private const string MeadowgrassPatchUid = "77ad4538d55bd774caea034c7dcfd578";
        // Feature 7: the six cards whose "Drink" equals River's first Drink (ponds excluded on purpose).
        private static readonly (string Uid, string Label)[] LakeAndStreamCards =
        {
            ("abbcffdf3cc2b7345b26bbbf9b92b514", "RiverLake"), ("e476c2de086e43748857c00465382020", "RiverBog"),
            ("9f4f948fddabac94a9614bfd67be7bbd", "RiverMarsh"), ("36f4783eb36b4e44b946a73bffc37cee", "RiverMire"),
            ("476b7ac3916a37447a26e76b5758d4ee", "RiverSwamp"), ("fdcac0486875fde469f719449790d03e", "StreamQEE"),
        };
        private const string TreeTopEventUid ="bd685ebfe220e5d449fd4b8eb6126fe2";
        // Charcoal(木炭), the card Fire Keeping's widened tag-fuel step also accepts (Fix 4).
        private const string CharcoalUid = "51c751469bd15ce46804e399479d1764";

        // Vanilla NPCDuty UIDs (Documentation/GameData/.../UniqueIDScriptableGUID/NPCDuty.json)
        private const string PartnerDutyEatUid = "a9de36f279b1e6449a01d9b05e45aa5d";
        private const string PartnerDutyEatFreeUid = "a4340107e78034a4192d126d8129ec67";
        private const string PartnerDutyCleanUid = "95fb94d06ae17a442b63c1c11d812d35";
        private const string PartnerDutyFirekeepingUid = "6147eff5b0fe3014ca57364f4d075514";
        private const string PartnerDutyWarmUpUid = "50379c4e254088643bdc8d5ef0f3db0f";
        private const string PartnerDutyButcherMediumUid = "fe171d0c65bf57c4aa4a7e7ed024dc41";
        private const string PartnerDutySmithPlaceOnGroundUid = "46907493948f9d445aa6cad97c9756e6";
        private const string PartnerDutyDigMudUid = "fd617a47bbff9894e94fa98adac4c348";
        private const string PartnerDutySpearfishUid = "2594b3adc3dcb544080d4993aad0635b";
        private const string PartnerDutyCreateIceHoleUid = "142f39f357040f34193f5d5b03c3899c";
        private const string PartnerDutyHarvestHeatherUid = "5ab7c8571e685bd4ab9f6f38e3e14d9a";
        private const string PartnerDutyHarvestMeadowgrassUid = "ee65a81697c1e0f40ab8552e36b7c7f7";
        private const string PartnerDutyDrinkFreeUid = "9060e702466d6764eb64e5983e72215e";
        private const string PartnerDutySeekDrinkUid = "86e706b400d59a24e8ced4a174201f50";

        // Vanilla NPCStat UIDs (Documentation/GameData/.../NPCStat/Partner_Energy.json and
        // Partner_Comfort.json)
        private const string PartnerEnergyUid = "325d096ddda132b4d9c1aea62d37153a";
        private const string PartnerComfortUid = "58e370bb17e5f7f459c67bb9c13a949d";

        // The Partner NPCStats whose every Status ships with VisibleToPlayer and ShowInMainTab
        // false (Fix 6). The label is the stat's vanilla asset name, used in the log lines. The
        // other all-hidden Partner stats (Wellbeing, Appetite, Filth, Intestines) are left hidden
        // on purpose: they read as internal values, not as something a person says about a
        // companion (plan section 6).
        private static readonly (string Uid, string Label)[] HiddenPartnerStats =
        {
            (PartnerEnergyUid, "Partner_Energy"),
            (PartnerComfortUid, "Partner_Comfort"),
        };

        // Above this current Hydration fraction, GardenPlot's "Water" CardInteraction is gated off:
        // stops a Partner from endlessly re-watering an already-saturated plot. A FRACTION, not a
        // percent: DurabilityConditions compares RequiredFuelPercent with CurrentFuelPercent, which
        // is CurrentFuel / FuelCapacity.Max on a 0..1 scale, so 1.1.x's 90f (0..90) always passed.
        private const float GardenOverwaterGateFraction = 0.9f;

        // Hearth and Hearth (Awakened) Feed CardInteractions only offer while the Hearth's fuel is
        // below this FRACTION of its capacity (0..1 CurrentFuelPercent scale, same trap as above).
        // PLACEHOLDER, TUNABLE (plan section 2, "Fire Keeping by fire tier"): armed only when
        // Partners Tend The Hearth is true, and only on the Hearth cards, never Campfire/Fireplace.
        private const float HearthFeedGateFraction = 0.5f;

        public static void ApplyPatch(Harmony harmony)
        {
            var targetType = AccessTools.TypeByName("GameLoad");
            var targetMethod = AccessTools.Method(targetType, "LoadMainGameData");
            var postfix = AccessTools.Method(typeof(GameLoadPatch), nameof(LoadMainGameData_Postfix));
            harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
            Logger.LogDebug("GameLoadPatch applied.");
        }

        // Counts duty refs appended across the whole load pass so the per-target detail can stay
        // at Debug and still be summarised in one Info line. Reset per pass: LoadMainGameData
        // runs again on every return to the main menu.
        private static int _dutyRefsAdded;
        private static int _poolEntriesAdded;

        private static void LoadMainGameData_Postfix()
        {
            _dutyRefsAdded = 0;
            _poolEntriesAdded = 0;
            try { FixStewBrothEating(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixStewBrothEating failed: {ex}"); }
            try { FixCleaningGap(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixCleaningGap failed: {ex}"); }
            try { FixGardenOverwatering(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixGardenOverwatering failed: {ex}"); }
            try { FixFireplaceFuelGap(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixFireplaceFuelGap failed: {ex}"); }
            try { GuardReservedFuelBranches(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] GuardReservedFuelBranches failed: {ex}"); }
            try { FixStrayDefecateButton(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixStrayDefecateButton failed: {ex}"); }
            // Opt-in since 1.1.3: vanilla's Hearth has no duties, so Partner tending is a design change,
            // not a bug fix. Read at game load; a Hearth mark in a save stays inert while this is off.
            if (Plugin.PartnersTendTheHearth != null && Plugin.PartnersTendTheHearth.Value)
            {
                try { FixHearthDuties(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixHearthDuties failed: {ex}"); }
            }
            try { FixOneCardWiringSlips(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixOneCardWiringSlips failed: {ex}"); }
            try { FixSeasonalTwinMarks(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixSeasonalTwinMarks failed: {ex}"); }
            if (Plugin.PartnersDrinkFromLakes != null && Plugin.PartnersDrinkFromLakes.Value)
            {
                try { WireLakeAndStreamDrinking(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] WireLakeAndStreamDrinking failed: {ex}"); }
            }
            try { FixPartnerStatVisibility(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixPartnerStatVisibility failed: {ex}"); }

            if (_dutyRefsAdded > 0 || _poolEntriesAdded > 0)
                Logger.LogInfo($"[GameLoadPatch] attached {_dutyRefsAdded} duty ref(s) to vanilla actions and {_poolEntriesAdded} item-pool entr(ies) to Eat duties (enable Debug logging for the per-target list).");
        }

        // ── Fix 1: stew/broth "Drink" is invisible to the Eat duty engine ──────────────────
        private static void FixStewBrothEating()
        {
            var eatDuty = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyEatUid);
            var eatFreeDuty = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyEatFreeUid);

            // The Eat and EatFree duties' AffectItemsDutyAction select from an item pool keyed on the
            // global tag tag_HumanFood plus the LQ_Water card. A duty ref on the Drink action alone is
            // therefore not enough: the broth and stew carry only tag_Heatable, so the duty never picked
            // them (T2.103 2026-10-13, 0 Drink action.done events). The two cards are added to those
            // two pools as card entries, exactly as LQ_Water already is. They are NOT given the tag:
            // tag_HumanFood is also read by the Stress Eat pool, the Partner's food preference list,
            // the framework's wildlife raids, QuickTransfer's tag filter and pantry slot filters, none
            // of which should see a broth (Documentation/Research/PartnerOverhaul/humanfood_consumers_2026-10-18.md).
            foreach (var (uid, label) in new[] { (OldLqStewWaterUid, "OLD_LQ_StewWater"), (LqNightBrothUid, "LQ_NightBroth") })
            {
                var card = UniqueIDScriptable.GetFromID<CardData>(uid);
                if (card == null) { Logger.LogWarning($"[GameLoadPatch] {label} not resolvable — skipping stew/broth eat fix."); continue; }

                AddCardToEatItemPools(eatDuty, "PartnerDuty_Eat", card, label);
                AddCardToEatItemPools(eatFreeDuty, "PartnerDuty_EatFree", card, label);

                var drink = card.DismantleActions?.FirstOrDefault(a => a.ActionName != null && a.ActionName.DefaultText == "Drink");
                if (drink == null) { Logger.LogWarning($"[GameLoadPatch] {label}: 'Drink' DismantleAction not found — skipping."); continue; }

                // Deliberately NOT PartnerDuty_DrinkFree — a thirsty Partner should still
                // target plain water, matching vanilla's own LQ_Water wiring pattern.
                AppendDutyRefIfMissing(drink, eatDuty, "PartnerDuty_Eat", $"{label}.Drink");
                AppendDutyRefIfMissing(drink, eatFreeDuty, "PartnerDuty_EatFree", $"{label}.Drink");
            }
        }

        // Appends the card to the item pool of every AffectItemsDutyAction step of the duty whose pool
        // is the human-food pool (it names the tag_HumanFood tag), unless a card entry for it is
        // already there. Idempotent: LoadMainGameData runs again on every return to the main menu.
        private static void AddCardToEatItemPools(NPCDuty duty, string dutyLabel, CardData card, string cardLabel)
        {
            if (duty == null) { Logger.LogWarning($"[GameLoadPatch] {cardLabel}: duty '{dutyLabel}' not resolvable - item pool unchanged."); return; }
            var foodPoolsSeen = 0;
            foreach (var step in duty.ActionSequence ?? Array.Empty<NPCDutyAction>())
            {
                var affect = step as AffectItemsDutyAction;
                var pool = affect?.ItemSelection?.ItemPool;
                if (pool == null) continue;
                var isFoodPool = false;
                foreach (var entry in pool)
                {
                    var tag = entry.Target.Tag;
                    if (tag != null && tag.name == "tag_HumanFood") { isFoodPool = true; break; }
                }
                if (!isFoodPool) continue;
                foodPoolsSeen++;
                var present = false;
                foreach (var entry in pool)
                    if (entry.Target.Card == card) { present = true; break; }
                if (present)
                {
                    Logger.LogDebug($"[GameLoadPatch] {cardLabel}: already in {dutyLabel}'s item pool - skipping (idempotent).");
                    continue;
                }
                pool.Add(new CardOrTagQuantity(card));
                _poolEntriesAdded++;
                Logger.LogDebug($"[GameLoadPatch] {cardLabel}: added to {dutyLabel}'s item pool so the Eat step selects it.");
            }
            if (foodPoolsSeen == 0)
                Logger.LogWarning($"[GameLoadPatch] {cardLabel}: no human-food item pool found on {dutyLabel} - the card stays outside it.");
        }

        // ── Fix 2: "Clean" on finished homes is invisible to the Clean duty engine ─────────
        private static void FixCleaningGap()
        {
            var cleanDuty = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyCleanUid);
            var homes = new[]
            {
                (CabinUid, "Cabin"), (MudHutUid, "MudHut"), (EnclosureUid, "Enclosure"),
                (MudHutEnclosureUid, "MudHutEnclosure"), (MudHutExpansionLeftUid, "MudHutExpansionLeft"),
            };

            foreach (var (uid, label) in homes)
            {
                var card = UniqueIDScriptable.GetFromID<CardData>(uid);
                if (card == null) { Logger.LogWarning($"[GameLoadPatch] {label} not resolvable — skipping cleaning fix."); continue; }

                var clean = card.CardInteractions?.FirstOrDefault(a => a.ActionName != null && a.ActionName.DefaultText == "Clean");
                if (clean == null) { Logger.LogWarning($"[GameLoadPatch] {label}: 'Clean' CardInteraction not found — skipping."); continue; }

                AppendDutyRefIfMissing(clean, cleanDuty, "PartnerDuty_Clean", $"{label}.Clean");
            }
        }

        // ── Fix 3: GardenPlot "Water" never re-checks current Hydration ────────────────────
        private static void FixGardenOverwatering()
        {
            var card = UniqueIDScriptable.GetFromID<CardData>(GardenPlotUid);
            if (card == null) { Logger.LogWarning("[GameLoadPatch] GardenPlot not resolvable — skipping over-watering fix."); return; }

            var water = card.CardInteractions?.FirstOrDefault(a => a.ActionName != null && a.ActionName.DefaultText == "Water");
            if (water == null) { Logger.LogWarning("[GameLoadPatch] GardenPlot: 'Water' CardInteraction not found — skipping."); return; }

            // OptionalValue.Active is `protected` — read it via the class's own public
            // `implicit operator bool` instead of the field directly.
            if (water.RequiredReceivingDurabilities.RequiredFuelPercent)
            {
                Logger.LogDebug("[GameLoadPatch] GardenPlot.Water already gated — skipping (idempotent).");
                return;
            }

            // SetActiveWithValue(Vector2) is OptionalRangeValue's public mutator — sets
            // Active=true plus FloatValue/MaxValue from the (min,max) pair in one call.
            water.RequiredReceivingDurabilities.RequiredFuelPercent.SetActiveWithValue(new Vector2(0f, GardenOverwaterGateFraction));
            Logger.LogInfo($"[GameLoadPatch] GardenPlot.Water: armed RequiredFuelPercent gate (0-{GardenOverwaterGateFraction * 100f:0}% Hydration).");
        }

        // ── Fix 4: Fireplace and Fire Pit "Feed Charcoal" invisible to the Firekeeping duty ──
        // Scope (re-scoped 2026-10-04 from Wave 0 W0.1, MEASURED): the Firekeeping duty's
        // ActionSequence only performs AffectItemsDutyActions whose ActionTagFilter is
        // PartnerAction1/2/3, so a Feed action needs the duty ref AND one of those ActionTags.
        // "Feed Pine Needles" is NOT wired: the duty has no step for NeedlesFresh and the CI
        // carries no PartnerAction tag, so a ref would be inert (plan MASTER_PLAN section 1,
        // "Considered, not selected"). Charcoal takes PartnerAction3, the tag-fuel step, and
        // (1.1.0, folded in 2026-10-05; T2.500 measured red on the earlier 1.1.0 build without it) the decision that gates that step is
        // widened to accept Charcoal too: WidenTagFuelDecisionToCharcoal + CharcoalFuelDecision.
        private static void FixFireplaceFuelGap()
        {
            var firekeepingDuty = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyFirekeepingUid);
            var tagFuelAction = FindPartnerActionTag("PartnerAction3");

            foreach (var (uid, label) in new[] { (FireplaceUid, "Fireplace"), (FirePitUid, "FirePit") })
            {
                // Each card is its own try block so one missing card cannot skip the other.
                try
                {
                    var card = UniqueIDScriptable.GetFromID<CardData>(uid);
                    if (card == null) { Logger.LogWarning($"[GameLoadPatch] {label} not resolvable - skipping fuel-gap fix."); continue; }
                    AppendDutyRefToAllNamed(card.CardInteractions, "Feed Charcoal", firekeepingDuty, "PartnerDuty_Firekeeping", label, tagFuelAction, "PartnerAction3");
                }
                catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixFireplaceFuelGap failed for {label}: {ex}"); }
            }

            // The wiring above is necessary but not sufficient: the duty reaches its PartnerAction3
            // step only through a "tag_Fuel on board" decision, and Charcoal has no tag_Fuel, so
            // T2.500 measured Fire Keeping NotSelectable with only Charcoal on the board (1.1.0).
            try { WidenTagFuelDecisionToCharcoal(firekeepingDuty); }
            catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixFireplaceFuelGap failed widening the tag-fuel step: {ex}"); }
        }

        // Replaces Fire Keeping's "Tag Fuel on Board" step with a CharcoalFuelDecision that keeps
        // the vanilla conditions and outcomes and also passes when Charcoal is on the board. Only
        // this duty's ActionSequence array changes; the vanilla decision asset is left untouched and
        // no step index moves. Refuses (Warning, no change) unless the sequence has exactly one
        // such decision and its OnTrue step is the AffectItemsDutyAction filtered on PartnerAction3,
        // the tag 'Feed Charcoal' now carries; Charcoal reaching any other step would be a guess.
        private static void WidenTagFuelDecisionToCharcoal(NPCDuty duty)
        {
            if (duty == null) { Logger.LogWarning("[GameLoadPatch] PartnerDuty_Firekeeping not resolvable - Charcoal stays unreachable."); return; }
            var seq = duty.ActionSequence;
            if (seq == null || seq.Length == 0) { Logger.LogWarning("[GameLoadPatch] Firekeeping ActionSequence empty - Charcoal stays unreachable."); return; }

            if (seq.Any(a => a is CharcoalFuelDecision))
            {
                Logger.LogDebug("[GameLoadPatch] Firekeeping tag-fuel step already accepts Charcoal - skipping (idempotent).");
                return;
            }

            var candidates = new List<int>();
            for (int i = 0; i < seq.Length; i++)
            {
                var d = seq[i] as TakeSimpleDecisionDutyAction;
                if (d == null || d.GetType() != typeof(TakeSimpleDecisionDutyAction)) continue;
                var tags = d.Conditions.RequiredTagsOnBoard;
                if (tags == null || !tags.Any(t => t != null && t.name == "tag_Fuel")) continue;
                if (d.OnTrue == null || d.OnTrue.Outcome != DutyDecisionOutcomes.MoveToSpecificStep) continue;
                candidates.Add(i);
            }
            if (candidates.Count != 1)
            {
                Logger.LogWarning($"[GameLoadPatch] Firekeeping: expected 1 'tag_Fuel on board' decision, found {candidates.Count} - Charcoal stays unreachable (game update?).");
                return;
            }

            int stepIdx = candidates[0];
            var original = (TakeSimpleDecisionDutyAction)seq[stepIdx];
            int feedIdx = original.OnTrue.MoveToStep;
            var feed = feedIdx >= 0 && feedIdx < seq.Length ? seq[feedIdx] as AffectItemsDutyAction : null;
            if (feed == null)
            {
                Logger.LogWarning($"[GameLoadPatch] Firekeeping: step {stepIdx} leads to step {feedIdx}, which is not an AffectItemsDutyAction - Charcoal stays unreachable.");
                return;
            }
            var actionField = AccessTools.Field(typeof(AffectItemsDutyAction), "ActionToPerform");
            if (actionField == null) { Logger.LogWarning("[GameLoadPatch] AffectItemsDutyAction.ActionToPerform field not found - Charcoal stays unreachable."); return; }
            var filter = (actionField.GetValue(feed) as NPCCardActionSelectionSettings)?.ActionTagFilter;
            if (filter == null || !filter.Any(t => t != null && t.name == "PartnerAction3"))
            {
                Logger.LogWarning($"[GameLoadPatch] Firekeeping: step {feedIdx} does not filter on PartnerAction3 - Charcoal stays unreachable.");
                return;
            }

            var charcoal = UniqueIDScriptable.GetFromID<CardData>(CharcoalUid);
            if (charcoal == null) { Logger.LogWarning("[GameLoadPatch] Charcoal not resolvable - Charcoal stays unreachable."); return; }

            // GeneralCondition is a struct: this is a copy, so the vanilla decision's arrays are
            // untouched; only the two board fields are replaced on the copy.
            var alternative = original.Conditions;
            alternative.RequiredTagsOnBoard = Array.Empty<CardTag>();
            alternative.RequiredCardsOnBoard = new[] { charcoal };

            var widened = ScriptableObject.CreateInstance<CharcoalFuelDecision>();
            widened.name = original.name + " (PartnerOverhaul: or Charcoal)";
            widened.hideFlags = HideFlags.DontUnloadUnusedAsset;
            widened.RequiredForSelectingDuty = original.RequiredForSelectingDuty;
            widened.Conditions = original.Conditions;
            widened.OnTrue = original.OnTrue;
            widened.OnFalse = original.OnFalse;
            widened.AlternativeConditions = alternative;

            var newSeq = (NPCDutyAction[])seq.Clone();
            newSeq[stepIdx] = widened;
            duty.ActionSequence = newSeq;
            Logger.LogInfo($"[GameLoadPatch] Firekeeping: step {stepIdx} ('{original.name}') now also passes on Charcoal, leading to the PartnerAction3 feed at step {feedIdx}.");
        }

        // "Reserved Fuel/Wood UIDs" (opt-in): replaces each Fire Keeping decision step whose
        // condition names a reserved card with a ReserveAwareDecision, so a reserved Wood on the
        // board no longer sends the duty into a feed branch the reserve filter empties (T3.39).
        // Only this duty's ActionSequence array changes, no step index moves, and nothing happens
        // when the option is empty (the default). Only exact TakeSimpleDecisionDutyAction steps are
        // replaced: CharcoalFuelDecision (the tag-fuel step) is a subclass and is left alone
        // here because it guards its own Charcoal alternative (PO-N1).
        private static void GuardReservedFuelBranches()
        {
            var raw = Plugin.ReservedFuelUIDs != null ? Plugin.ReservedFuelUIDs.Value : null;
            var reserved = new HashSet<string>((raw ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
            if (reserved.Count == 0) { Logger.LogDebug("[GameLoadPatch] Reserved Fuel/Wood UIDs empty - Fire Keeping branches left as vanilla."); return; }

            var duty = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyFirekeepingUid);
            var seq = duty != null ? duty.ActionSequence : null;
            if (seq == null || seq.Length == 0) { Logger.LogWarning("[GameLoadPatch] PartnerDuty_Firekeeping or its ActionSequence not resolvable - reserved fuel branches not guarded."); return; }

            var newSeq = (NPCDutyAction[])seq.Clone();
            int replaced = 0;
            for (int i = 0; i < seq.Length; i++)
            {
                var d = seq[i] as TakeSimpleDecisionDutyAction;
                if (d == null || d.GetType() != typeof(TakeSimpleDecisionDutyAction)) continue;
                if (!ReserveAwareDecision.NamesReservedCard(d.Conditions, reserved.Contains)) continue;

                var guard = ScriptableObject.CreateInstance<ReserveAwareDecision>();
                guard.name = d.name + " (PartnerOverhaul: skips reserved)";
                guard.hideFlags = HideFlags.DontUnloadUnusedAsset;
                guard.RequiredForSelectingDuty = d.RequiredForSelectingDuty;
                guard.Conditions = d.Conditions;
                guard.OnTrue = d.OnTrue;
                guard.OnFalse = d.OnFalse;
                newSeq[i] = guard;
                replaced++;
                Logger.LogDebug($"[GameLoadPatch] Firekeeping: step {i} ('{d.name}') now skips a reserved card.");
            }
            if (replaced == 0) { Logger.LogInfo("[GameLoadPatch] Reserved Fuel/Wood UIDs: no Fire Keeping step names a reserved card - nothing guarded."); return; }
            duty.ActionSequence = newSeq;
            Logger.LogInfo($"[GameLoadPatch] Firekeeping: {replaced} step(s) now skip a reserved card, so the duty falls through to its other fuel branches.");
        }

        // ── Fix 5: stray "Defecate" DA on the Tree Top scenic event ────────────────────────
        private static void FixStrayDefecateButton()
        {
            var card = UniqueIDScriptable.GetFromID<CardData>(TreeTopEventUid);
            if (card == null) { Logger.LogWarning("[GameLoadPatch] Event_TreeTopBase not resolvable — skipping stray Defecate fix."); return; }
            if (card.DismantleActions == null) return;

            int removed = card.DismantleActions.RemoveAll(a => a.ActionName != null && a.ActionName.DefaultText == "Defecate");
            if (removed > 0)
                Logger.LogInfo($"[GameLoadPatch] Event_TreeTopBase: removed {removed} stray 'Defecate' DismantleAction(s).");
        }

        // ── Fix 6: Partner card shows no Energy or Comfort status at all ───────────────────
        // NPCStat.Statuses[] holds fully-authored threshold text (Energy: "Looks exhausted." /
        // "Looks very tired." / "Looks tired." / "Looks full of energy."; Comfort: "Looks very
        // comfortable" through "Looks terribly uncomfortable"), but every entry on vanilla's
        // Partner_Energy and Partner_Comfort has VisibleToPlayer=false AND ShowInMainTab=false,
        // unlike every sibling need stat (Partner_Satiation/BodyTemperature/Thirst/Wakefulness),
        // which set both true on their non-neutral entries so NPCInspectionPopup's main-tab status
        // list (InGameNPC.VisibleStatuses, tab index 0) actually surfaces them. Net effect: the
        // player has zero in-game visibility into a Partner's energy or comfort. Comfort has no
        // neutral entry that vanilla could mean to hide (its five bands cover 67 to 100, 33 to 66,
        // -49 to -1, -75 to -50 and -100 to -76 on a stat that starts at 0), so every one is a
        // deviation worth showing; the gap between 0 and 32 stays blank, as vanilla authored it.
        // VisibleToPlayer is a private [SerializeField] on the NPCStatStatus class (not the
        // ScriptableObject itself), so it needs one reflected field set per entry; ShowInMainTab
        // is public.
        private static void FixPartnerStatVisibility()
        {
            var visibleField = typeof(NPCStatStatus).GetField("VisibleToPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
            if (visibleField == null) { Logger.LogWarning("[GameLoadPatch] NPCStatStatus.VisibleToPlayer field not found - layout changed?"); return; }

            foreach (var (uid, label) in HiddenPartnerStats)
            {
                // Each stat is its own try block, as every fix in this class is, so one stat that
                // throws (or goes missing on a game update) cannot take the other's fix down.
                try { MakeStatStatusesVisible(uid, label, visibleField); }
                catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixPartnerStatVisibility failed for {label}: {ex}"); }
            }

            try { AddComfortAtEaseStatus(visibleField); }
            catch (Exception ex) { Logger.LogError($"[GameLoadPatch] AddComfortAtEaseStatus failed: {ex}"); }
        }

        // PO-N2: vanilla's Partner_Comfort bands skip 0 to 32, so a Partner there shows no status.
        // Append one information-only status over that gap (no stat effect). Idempotent: skipped
        // when a status already carries our LocalizationKey.
        private const string ComfortAtEaseKey = "PartnerOverhaul_Comfort_AtEase";
        private const string ComfortAtEaseText = "Looks at ease.";

        private static void AddComfortAtEaseStatus(FieldInfo visibleField)
        {
            var stat = UniqueIDScriptable.GetFromID<NPCStat>(PartnerComfortUid);
            if (stat == null) { Logger.LogWarning("[GameLoadPatch] Partner_Comfort not resolvable - skipping at-ease status."); return; }
            if (stat.Statuses == null) { Logger.LogWarning("[GameLoadPatch] Partner_Comfort has no Statuses - skipping at-ease status."); return; }

            foreach (var existing in stat.Statuses)
            {
                if (existing != null && existing.GameName.LocalizationKey == ComfortAtEaseKey)
                {
                    Logger.LogDebug("[GameLoadPatch] Partner_Comfort at-ease status already present - skipping (idempotent).");
                    return;
                }
            }

            var status = new NPCStatStatus
            {
                GameName = new LocalizedString { ParentObjectID = PartnerComfortUid, LocalizationKey = ComfortAtEaseKey, DefaultText = ComfortAtEaseText },
                ShowInMainTab = true,
                Priority = 0,
                ValueRange = new Vector2Int(0, 32),
                UsePercentageForRange = false,
                EffectsOnStats = new StatModifier[0],
                EffectsOnNPCStats = new NPCStatPassiveModifierEffect[0],
                EffectsOnNPCStatModifiers = new NPCStatDutyScaler[0],
                DeleteThisAgent = false,
                DeleteAgentMessage = new OptionalLocalizedStringValue(false, default(LocalizedString)),
            };
            visibleField.SetValue(status, true);

            var grown = new NPCStatStatus[stat.Statuses.Length + 1];
            Array.Copy(stat.Statuses, grown, stat.Statuses.Length);
            grown[grown.Length - 1] = status;
            stat.Statuses = grown;
            Logger.LogInfo("[GameLoadPatch] Partner_Comfort: added the 0 to 32 'Looks at ease.' status (information only).");
        }

        private static void MakeStatStatusesVisible(string uid, string label, FieldInfo visibleField)
        {
            var stat = UniqueIDScriptable.GetFromID<NPCStat>(uid);
            if (stat == null) { Logger.LogWarning($"[GameLoadPatch] {label} not resolvable - skipping visibility fix."); return; }
            if (stat.Statuses == null || stat.Statuses.Length == 0) { Logger.LogWarning($"[GameLoadPatch] {label} has no Statuses - skipping."); return; }

            // Idempotent: LoadMainGameData runs again on every return to the main menu, and an
            // entry that is already visible in both ways is left alone and not counted.
            int changed = 0;
            foreach (var status in stat.Statuses)
            {
                if (status == null) continue;
                if (status.ShowInMainTab && (bool)visibleField.GetValue(status)) continue;
                visibleField.SetValue(status, true);
                status.ShowInMainTab = true;
                changed++;
            }

            if (changed == 0)
            {
                Logger.LogDebug($"[GameLoadPatch] {label} statuses already visible - skipping (idempotent).");
                return;
            }
            Logger.LogInfo($"[GameLoadPatch] {label}: made {changed} status text(s) visible on the Partner card (were hidden).");
        }

        // ── Feature 2: Hearth and Hearth (Awakened) ─────────────────────────────────────────
        // Both cards ship 12 identical CardInteractions and no duty wiring (Wave 0 W0.2, MEASURED).
        // By INDEX with a name check, never by name alone: indices 0 and 1 are NOT_USED_ firewood
        // cards, 6/8/10 are hand actions that must stay unwired. Each wired action mirrors the
        // Campfire's own matching action (duty ref plus its ActionTags), because a ref alone does
        // not reach the Firekeeping steps (W0.1). Charcoal also gets PartnerAction3, as on
        // Fireplace and FirePit, so the widened tag-fuel decision can feed it.
        private static void FixHearthDuties()
        {
            var firekeeping = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyFirekeepingUid);
            var warmUp = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyWarmUpUid);
            var campfire = UniqueIDScriptable.GetFromID<CardData>(CampfireUid);
            if (campfire == null) { Logger.LogWarning("[GameLoadPatch] Campfire not resolvable - skipping Hearth duties."); return; }
            var partnerAction3 = FindPartnerActionTag("PartnerAction3");

            // The extinguished twins: list Fire Keeping so a mark survives the fire dying.
            foreach (var (twinUid, twinLabel) in new[] { (HearthExtinguishedUid, "HearthExtinguished"), (HearthAwakenedExtinguishedUid, "HearthAwakenedExtinguished") })
            {
                try
                {
                    var twin = UniqueIDScriptable.GetFromID<CardData>(twinUid);
                    if (twin == null) { Logger.LogWarning($"[GameLoadPatch] {twinLabel} not resolvable - its Fire Keeping mark will not survive the fire dying."); continue; }
                    AppendCardDutyIfMissing(twin, firekeeping, "PartnerDuty_Firekeeping", twinLabel);
                }
                catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixHearthDuties failed for {twinLabel}: {ex}"); }
            }

            // (Hearth CI index, expected name, Campfire CI index to mirror, extra PartnerAction3)
            var ciRows = new (int Ci, string Name, int CampfireCi, bool ExtraP3)[]
            {
                (2, "Feed Firewood", 2, false), (3, "Feed Fuel", 4, false),
                (4, "Feed Fuel", 5, false), (5, "Feed Charcoal", 6, true),
            };

            foreach (var (uid, label, warmUpDa) in new[] { (HearthUid, "Hearth", 2), (HearthAwakenedUid, "HearthAwakened", 3) })
            {
                try
                {
                    var card = UniqueIDScriptable.GetFromID<CardData>(uid);
                    if (card == null) { Logger.LogWarning($"[GameLoadPatch] {label} not resolvable - skipping."); continue; }
                    AppendCardDutyIfMissing(card, firekeeping, "PartnerDuty_Firekeeping", label);

                    foreach (var row in ciRows)
                    {
                        var target = ActionAt(card.CardInteractions, row.Ci, row.Name, label);
                        var reference = ActionAt(campfire.CardInteractions, row.CampfireCi, row.Name, "Campfire");
                        if (target == null || reference == null) continue;
                        var where = $"{label}.CI[{row.Ci}] {row.Name}";
                        AppendDutyRefIfMissing(target, firekeeping, "PartnerDuty_Firekeeping", where);
                        MirrorActionTags(target, reference, where);
                        if (row.ExtraP3 && partnerAction3 != null) AppendActionTagIfMissing(target, partnerAction3, "PartnerAction3", where);
                        // Feed only while the Hearth's own fuel is below the threshold (0..1 scale).
                        if (target.RequiredReceivingDurabilities.RequiredFuelPercent)
                            Logger.LogDebug($"[GameLoadPatch] {where} already fuel-gated - skipping (idempotent).");
                        else
                        {
                            target.RequiredReceivingDurabilities.RequiredFuelPercent.SetActiveWithValue(new Vector2(0f, HearthFeedGateFraction));
                            Logger.LogInfo($"[GameLoadPatch] {where}: armed RequiredFuelPercent gate (0-{HearthFeedGateFraction * 100f:0}% fuel).");
                        }
                    }

                    var warmTarget = ActionAt(card.DismantleActions, warmUpDa, "Warm Up", label);
                    var warmRef = ActionAt(campfire.DismantleActions, 2, "Warm Up", "Campfire");
                    if (warmTarget != null && warmRef != null)
                    {
                        AppendDutyRefIfMissing(warmTarget, warmUp, "PartnerDuty_WarmUp", $"{label}.DA[{warmUpDa}] Warm Up");
                        MirrorActionTags(warmTarget, warmRef, $"{label}.DA[{warmUpDa}] Warm Up");
                    }
                }
                catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixHearthDuties failed for {label}: {ex}"); }
            }
        }

        // ── Feature 3: four one-card slips plus the Mud Hut Right Room ──────────────────────
        private static void FixOneCardWiringSlips()
        {
            var butcher = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyButcherMediumUid);
            var smith = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutySmithPlaceOnGroundUid);
            var eat = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyEatUid);
            var eatFree = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyEatFreeUid);
            var clean = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyCleanUid);

            // (a) Boar piglet: its card marker is SkinMedium, which no action carries. APPEND
            // ButcherMedium (keeps any inert SkinMedium mark a save already holds; Open question 1).
            Slip("BoarPigletCarcass", BoarPigletCarcassUid, card =>
                AppendCardDutyIfMissing(card, butcher, "PartnerDuty_ButcherMedium", "BoarPigletCarcass"));

            // (b) Two blade blanks whose "Smith" lacks the ref the other 45 carry.
            foreach (var (uid, label) in new[] { (BlankArrowBroadheadUid, "Blank_MetalArrowBroadhead"), (BlankChiselBladeUid, "Blank_MetalChiselBlade") })
            {
                var l = label;
                Slip(l, uid, card => AppendDutyRefToAllNamed(AllActions(card), "Smith", smith, "PartnerDuty_SmithPlaceOnGround", l));
            }

            // (c) Wheat Bloodbread: Eat and EatFree on "Eat", as Rye Bloodbread and both pasties.
            Slip("WheatBloodbread", WheatBloodbreadUid, card =>
            {
                AppendDutyRefToAllNamed(AllActions(card), "Eat", eat, "PartnerDuty_Eat", "WheatBloodbread");
                AppendDutyRefToAllNamed(AllActions(card), "Eat", eatFree, "PartnerDuty_EatFree", "WheatBloodbread");
            });

            // (d) Mud Hut Right Room needs BOTH halves: the Clean marker and the action ref.
            Slip("MudHutExpansionRight", MudHutExpansionRightUid, card =>
            {
                AppendCardDutyIfMissing(card, clean, "PartnerDuty_Clean", "MudHutExpansionRight");
                AppendDutyRefToAllNamed(AllActions(card), "Clean", clean, "PartnerDuty_Clean", "MudHutExpansionRight");
            });
        }

        private static void Slip(string label, string uid, Action<CardData> edit)
        {
            try
            {
                var card = UniqueIDScriptable.GetFromID<CardData>(uid);
                if (card == null) { Logger.LogWarning($"[GameLoadPatch] {label} not resolvable - skipping wiring."); return; }
                edit(card);
            }
            catch (Exception ex) { Logger.LogError($"[GameLoadPatch] wiring failed for {label}: {ex}"); }
        }

        // ── Feature 8: keep duty marks through the seasonal twin swaps ──────────────────────
        // GameManager re-adds a transferred mark only where the new card lists the duty in its
        // CompatibleDuties (W0.3 MEASURED: marks drop across River/FrozenRiver). Each append stays
        // inert where no action carries the duty; the README says so. Pond's own DigMud mark is
        // kept by giving PondFrozen DigMud; Pond needs nothing (it already lists it).
        private static void FixSeasonalTwinMarks()
        {
            var digMud = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyDigMudUid);
            var spearfish = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutySpearfishUid);
            var iceHole = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyCreateIceHoleUid);
            var heather = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyHarvestHeatherUid);
            var meadow = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyHarvestMeadowgrassUid);

            var rows = new (string Uid, string Label, (NPCDuty Duty, string DutyLabel)[] Duties)[]
            {
                (FrozenRiverUid, "FrozenRiver", new[] { (digMud, "PartnerDuty_DigMud"), (spearfish, "PartnerDuty_Spearfish") }),
                (RiverUid, "River", new[] { (iceHole, "PartnerDuty_CreateIceHole") }),
                (PondFrozenUid, "PondFrozen", new[] { (digMud, "PartnerDuty_DigMud") }),
                (HeatherPatchUid, "HeatherPatch", new[] { (meadow, "PartnerDuty_HarvestMeadowgrass") }),
                (MeadowgrassPatchUid, "MeadowgrassPatch", new[] { (heather, "PartnerDuty_HarvestHeather") }),
            };
            foreach (var row in rows)
            {
                var duties = row.Duties;
                var label = row.Label;
                Slip(label, row.Uid, card =>
                {
                    foreach (var (duty, dutyLabel) in duties) AppendCardDutyIfMissing(card, duty, dutyLabel, label);
                });
            }
        }

        // ── Feature 7 (opt-in): Partners drink at lakes, bogs, marshes, mires, swamps, streams ──
        // River's first "Drink" carries DrinkFree + SeekDrink; the six listed cards' Drink has the
        // same stats and no duty. Ponds are excluded on purpose (a worse Drink, plan Considered).
        private static void WireLakeAndStreamDrinking()
        {
            var drinkFree = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyDrinkFreeUid);
            var seekDrink = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutySeekDrinkUid);
            var river = UniqueIDScriptable.GetFromID<CardData>(RiverUid);
            var riverDrink = river?.DismantleActions?.FirstOrDefault(a => a?.ActionName != null && a.ActionName.DefaultText == "Drink");
            if (riverDrink == null) { Logger.LogWarning("[GameLoadPatch] River 'Drink' not found - skipping lake drinking."); return; }

            foreach (var (uid, label) in LakeAndStreamCards)
            {
                var l = label;
                Slip(l, uid, card =>
                {
                    var drink = card.DismantleActions?.FirstOrDefault(a => a?.ActionName != null && a.ActionName.DefaultText == "Drink");
                    if (drink == null) { Logger.LogWarning($"[GameLoadPatch] {l}: 'Drink' not found - skipping."); return; }
                    AppendDutyRefIfMissing(drink, drinkFree, "PartnerDuty_DrinkFree", $"{l}.Drink");
                    AppendDutyRefIfMissing(drink, seekDrink, "PartnerDuty_SeekDrink", $"{l}.Drink");
                    MirrorActionTags(drink, riverDrink, $"{l}.Drink");
                });
            }
        }

        // ── shared helpers ──────────────────────────────────────────────────────────────
        private static IEnumerable<CardAction> AllActions(CardData card) =>
            (card.CardInteractions ?? Array.Empty<CardOnCardAction>()).Cast<CardAction>()
                .Concat(card.DismantleActions ?? new List<DismantleCardAction>());

        // The action at an exact index, only if its name matches (a game update that shifts indices
        // gets a Warning and no edit rather than a wrong one).
        private static CardAction ActionAt<T>(IList<T> list, int index, string expectedName, string context) where T : CardAction
        {
            if (list == null || index < 0 || index >= list.Count || list[index] == null
                || list[index].ActionName == null || list[index].ActionName.DefaultText != expectedName)
            {
                Logger.LogWarning($"[GameLoadPatch] {context}: action [{index}] is not '{expectedName}' - skipping (game update?).");
                return null;
            }
            return list[index];
        }

        private static void AppendCardDutyIfMissing(CardData card, NPCDuty duty, string dutyLabel, string context)
        {
            if (duty == null) { Logger.LogWarning($"[GameLoadPatch] {context}: card duty '{dutyLabel}' not resolvable - skipping."); return; }
            var existing = card.CompatibleDuties ?? Array.Empty<NPCDuty>();
            if (existing.Contains(duty))
            {
                Logger.LogDebug($"[GameLoadPatch] {context}: card duty {dutyLabel} already present - skipping (idempotent).");
                return;
            }
            var newArr = new NPCDuty[existing.Length + 1];
            Array.Copy(existing, newArr, existing.Length);
            newArr[existing.Length] = duty;
            card.CompatibleDuties = newArr;
            _dutyRefsAdded++;
            Logger.LogDebug($"[GameLoadPatch] {context}: added {dutyLabel} to CompatibleDuties.");
        }

        private static void MirrorActionTags(CardAction target, CardAction reference, string context)
        {
            foreach (var tag in reference.ActionTags ?? Array.Empty<ActionTag>())
                if (tag != null) AppendActionTagIfMissing(target, tag, tag.name, context);
        }

        private static void AppendDutyRefIfMissing(CardAction action, NPCDuty duty, string dutyLabel, string context)
        {
            if (duty == null) { Logger.LogWarning($"[GameLoadPatch] {context}: duty '{dutyLabel}' not resolvable — skipping."); return; }

            var existing = action.CompatibleNPCDuties ?? Array.Empty<NPCDutyOrDutyTagRef>();
            if (existing.Any(r => r.DutyTarget == duty))
            {
                Logger.LogDebug($"[GameLoadPatch] {context}: {dutyLabel} already present — skipping (idempotent).");
                return;
            }

            var newArr = new NPCDutyOrDutyTagRef[existing.Length + 1];
            Array.Copy(existing, newArr, existing.Length);
            newArr[existing.Length] = MakeDutyRef(duty);
            action.CompatibleNPCDuties = newArr;
            _dutyRefsAdded++;
            // Per-target detail is Debug (12 targets = 12 Info lines every boot); the caller
            // emits one aggregate Info line instead. Per CLAUDE.md § Mod Logging Norms.
            Logger.LogDebug($"[GameLoadPatch] {context}: added {dutyLabel} to CompatibleNPCDuties.");
        }

        // Wires EVERY action in the array or list whose ActionName.DefaultText equals actionName
        // (a card can carry several same-named CardInteractions, one per accepted card), optionally
        // adding an ActionTag too. Warns when none matches. Idempotent through its two helpers.
        private static void AppendDutyRefToAllNamed(IEnumerable<CardAction> actions, string actionName, NPCDuty duty, string dutyLabel, string context, ActionTag actionTag = null, string tagLabel = null)
        {
            var matches = (actions ?? Enumerable.Empty<CardAction>())
                .Where(a => a != null && a.ActionName != null && a.ActionName.DefaultText == actionName).ToList();
            if (matches.Count == 0)
            {
                Logger.LogWarning($"[GameLoadPatch] {context}: '{actionName}' action not found - skipping.");
                return;
            }
            for (int i = 0; i < matches.Count; i++)
            {
                var where = $"{context}.{actionName}[{i}]";
                AppendDutyRefIfMissing(matches[i], duty, dutyLabel, where);
                if (actionTag != null) AppendActionTagIfMissing(matches[i], actionTag, tagLabel, where);
            }
        }

        private static void AppendActionTagIfMissing(CardAction action, ActionTag tag, string tagLabel, string context)
        {
            var existing = action.ActionTags ?? Array.Empty<ActionTag>();
            if (existing.Contains(tag))
            {
                Logger.LogDebug($"[GameLoadPatch] {context}: {tagLabel} already present - skipping (idempotent).");
                return;
            }
            var newArr = new ActionTag[existing.Length + 1];
            Array.Copy(existing, newArr, existing.Length);
            newArr[existing.Length] = tag;
            action.ActionTags = newArr;
            _dutyRefsAdded++;
            Logger.LogDebug($"[GameLoadPatch] {context}: added action tag {tagLabel}.");
        }

        // The PartnerAction1/2/3 assets are not UniqueID scriptables, so they are read off the
        // Campfire's own Feed actions (which carry them), never created. Null plus a Warning when
        // the reference no longer carries one (a game update), which skips the tag, not the ref.
        private static ActionTag FindPartnerActionTag(string tagName)
        {
            var campfire = UniqueIDScriptable.GetFromID<CardData>(CampfireUid);
            if (campfire == null) { Logger.LogWarning($"[GameLoadPatch] Campfire not resolvable - no {tagName} reference."); return null; }
            var found = (campfire.CardInteractions ?? Array.Empty<CardOnCardAction>()).Cast<CardAction>()
                .Concat(campfire.DismantleActions ?? new List<DismantleCardAction>())
                .Where(a => a != null && a.ActionTags != null)
                .SelectMany(a => a.ActionTags)
                .FirstOrDefault(t => t != null && t.name == tagName);
            if (found == null) Logger.LogWarning($"[GameLoadPatch] No Campfire action carries {tagName} - wiring the duty ref without it.");
            return found;
        }

        private static NPCDutyOrDutyTagRef MakeDutyRef(NPCDuty duty)
        {
            var dutyRef = default(NPCDutyOrDutyTagRef);
            ReflectionHelper.SetPrivateField(ref dutyRef, "Target", duty);
            return dutyRef;
        }
    }
}
