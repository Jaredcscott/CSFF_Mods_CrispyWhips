using System;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;
using UnityEngine;

namespace WaterDrivenInfrastructure.Patcher
{
    /// <summary>
    /// Tier 1 custom NPCDuty registrations (Duties/Ownership fleet plan, M2/M3) — lets a
    /// recruited vanilla Partner NPC autonomously walk to a WDI station and press its single
    /// "do everything" action once the player has toggled that station's Duty Assignment marker
    /// on. Started with the Grinding Mill (M2 proof-of-concept); M3 extends the same file to the
    /// Ore Sluice ("Sluice All"), the Sawmill ("Cut") and the Forge/Workshop ("Smelt Ore" /
    /// "Hammer All") rather than forking a competing patcher.
    ///
    /// Two shapes of station work are supported, and they are NOT interchangeable —
    /// NPCCardActionSelectionSettings.ActionType is fixed per STEP and branches to entirely
    /// separate engine code:
    ///  • DismantleAction (mill, sluice, workshop's "Hammer All") — one self-button; the duty
    ///    needs no second item.
    ///  • CardOnCardAction (sawmill, forge/workshop's "Smelt Ore") — a drag-based CardInteraction;
    ///    the duty must additionally tell the engine how to find the DRAGGED item. See
    ///    BuildAffectAction's cardOnCard block.
    /// A duty may chain several steps (see the Workshop below); because the two shapes read
    /// disjoint action lists, mixing them in one sequence is unambiguous without any ActionTag
    /// filtering.
    ///
    /// JSON-shell path (confirmed viable this session — see root CLAUDE.md
    /// §Custom Non-UID ScriptableObjects / memory reference_npcduty_authoring_footguns):
    /// NPCDuty/&lt;uid&gt;.json (a TOP-LEVEL mod folder, sibling to CardData/ —
    /// JsonDataLoader.ResolveDir has no CardData/&lt;subDir&gt; fallback) authors everything on
    /// NPCDuty except
    /// ActionSequence (a Unity sub-asset graph, not JSON-authorable) and is registered into
    /// DataBase.AllData by JsonDataLoader.LoadAll BEFORE WarpResolver runs, so each station's
    /// CompatibleDutiesWarpData reference resolves with zero extra C#. This patch's only job is
    /// grafting ActionSequence onto each already-loaded NPCDuty via GetFromID + reflection, and
    /// appending an NPCDutyRef per duty to the vanilla Partner NPCAgent template's AgentDuties —
    /// mirrors Community_Mod_Chest/Patcher/AshPartnerDutyPatch.cs exactly (reflection-only; every
    /// type resolved via CardUtil.FindGameType, never assumed to be compile-time accessible even
    /// though WDI's own lib/Assembly-CSharp-nstrip.dll happens to carry a reference — that DLL
    /// is a manually-regenerated, version-driftable artifact per root CLAUDE.md §Game-Update
    /// Reference Refresh, so this patch deliberately does not depend on it staying in sync).
    /// </summary>
    public static class MillDutyPatch
    {
        private const string MillDutyUid = "wdiOperateGrindingMill_Duty";
        private const string MillUid = "water_sawmill_grinding_mill_placed";
        private const string SluiceDutyUid = "wdiOperateOreSluice_Duty";
        private const string SluiceUid = "water_sawmill_ore_sluice_placed";
        private const string SawmillDutyUid = "wdiOperateSawmill_Duty";
        private const string SawmillUid = "water_sawmill_placed";
        private const string ForgeDutyUid = "wdiOperateForge_Duty";
        private const string ForgeUid = "water_sawmill_forge_placed";
        private const string WorkshopDutyUid = "wdiOperateWorkshop_Duty";
        private const string WorkshopUid = "water_sawmill_workshop_placed";
        /// <summary>
        /// Shared BaseWeight for every station duty grafted by this patcher.
        ///
        /// Raised 20 -> 850 (2026-08-16). 20 was NOT merely conservative, it was a starvation bug:
        /// a real play session's NPCDutySelectionInfo dump showed the Ore Sluice duty reporting
        /// notSelectable=False (fully eligible, reachable, nothing blocking) and STILL never being
        /// chosen across the whole session, because the vanilla Partner's ~30 native duties run
        /// BaseWeight 900-1000 — up to 50x higher — and InGameNPC.CheckForDuties picks the highest
        /// TotalWeight. Note this also means the Grinding Mill's earlier "confirmed working in-game"
        /// result was probably a low-competition window, not evidence that 20 was adequate.
        ///
        /// 850 sits just under the native band on purpose: high enough to win against ordinary
        /// idle/chore duties, low enough that genuine survival duties (PartnerDuty_Sleep and
        /// friends, observed activating in that same session) still outrank it rather than being
        /// permanently starved by a station the player happens to have toggled on. This is a
        /// tuning judgement from Duties_Ownership_Plan.md § M3.5, NOT a measured optimum — it has
        /// not itself been validated in-game.
        /// </summary>
        private const int StationDutyBaseWeight = 850;

        // Vanilla Agent_Partner NPCAgent (UniqueIDScriptableGUID/NPCAgent.json) — the single
        // shared template every recruited Partner spawns from. Appending here reaches every
        // Partner, not just one recruit.
        private const string PartnerAgentGuid = "b6a4cc575cf9ddd41b321ec619db21fd";

        private static bool _millGrafted;
        private static bool _sluiceGrafted;
        private static bool _sawmillGrafted;
        private static bool _forgeGrafted;
        private static bool _workshopGrafted;

        /// <summary>
        /// Which engine action list a single AffectItemsDutyAction step searches. This is fixed
        /// per step (NPCCardActionSelectionSettings.ActionType) and the two lists are disjoint,
        /// which is what makes a multi-step sequence unambiguous: a DismantleAction step can only
        /// ever pick a duty-marked entry from CardData.DismantleActions, and a CardOnCardAction
        /// step only from CardData.CardInteractions. Within ONE list the engine takes the FIRST
        /// duty-marked action whose SimpleConditionsCheck passes and stops
        /// (NPCCardActionSelectionSettings.CollectDismantleActionsList's break /
        /// CollectCardOnCardActionsList's return) — so never mark two actions of the same shape on
        /// the same card for the same duty unless array order is genuinely the priority you want.
        /// </summary>
        private enum AffectShape
        {
            DismantleAction,
            CardOnCardAction
        }

        // ── reflection handles, resolved once ──────────────────────────────────
        private static Type _uidType, _npcDutyActionType, _moveDutyActionType, _affectItemsDutyActionType,
            _npcItemSelectionSettingsType, _npcCardActionSelectionSettingsType, _npcDutyRefType,
            _npcDutyWeightsType, _cardOrTagRefWithDurabilitiesType, _cardOrTagQuantityType,
            _generalConditionType, _cardDataType, _cardTagType, _npcStatInstantModifierType, _actionTagType;
        private static MethodInfo _getFromIdMethod;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gameLoadType = AccessTools.TypeByName("GameLoad");
                var loadMainGameDataMethod = AccessTools.Method(gameLoadType, "LoadMainGameData");
                var postfixMethod = AccessTools.Method(typeof(MillDutyPatch), nameof(LoadMainGameData_Postfix));
                var postfix = new HarmonyMethod(postfixMethod)
                {
                    // Run after the framework's own postfix so WarpResolver (and therefore the
                    // JSON-authored NPCDuty's registration into AllData) has already completed.
                    after = new[] { "crispywhips.CSFFModFramework" }
                };
                harmony.Patch(loadMainGameDataMethod, postfix: postfix);
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[MillDutyPatch] Failed to apply patch: {ex}");
            }
        }

        private static void LoadMainGameData_Postfix()
        {
            try
            {
                GraftDutyAndAttach();
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogError($"[MillDutyPatch] LoadMainGameData postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool ResolveTypes()
        {
            if (_npcDutyActionType != null) return true;

            _uidType = CardUtil.FindGameType("UniqueIDScriptable");
            _npcDutyActionType = CardUtil.FindGameType("NPCDutyAction");
            _moveDutyActionType = CardUtil.FindGameType("MoveDutyAction");
            _affectItemsDutyActionType = CardUtil.FindGameType("AffectItemsDutyAction");
            _npcItemSelectionSettingsType = CardUtil.FindGameType("NPCItemSelectionSettings");
            _npcCardActionSelectionSettingsType = CardUtil.FindGameType("NPCCardActionSelectionSettings");
            _npcDutyRefType = CardUtil.FindGameType("NPCDutyRef");
            _npcDutyWeightsType = CardUtil.FindGameType("NPCDutyWeights");
            _cardOrTagRefWithDurabilitiesType = CardUtil.FindGameType("CardOrTagRefWithDurabilities");
            _cardOrTagQuantityType = CardUtil.FindGameType("CardOrTagQuantity");
            _generalConditionType = CardUtil.FindGameType("GeneralCondition");
            _cardDataType = CardUtil.FindGameType("CardData");
            _cardTagType = CardUtil.FindGameType("CardTag");
            _npcStatInstantModifierType = CardUtil.FindGameType("NPCStatInstantModifier");
            _actionTagType = CardUtil.FindGameType("ActionTag");

            if (_uidType == null || _npcDutyActionType == null || _moveDutyActionType == null
                || _affectItemsDutyActionType == null || _npcItemSelectionSettingsType == null
                || _npcCardActionSelectionSettingsType == null || _npcDutyRefType == null
                || _npcDutyWeightsType == null || _cardOrTagRefWithDurabilitiesType == null
                || _cardOrTagQuantityType == null || _generalConditionType == null || _cardDataType == null
                || _cardTagType == null || _npcStatInstantModifierType == null || _actionTagType == null)
            {
                Plugin.Logger?.LogWarning("[MillDutyPatch] One or more NPCDuty engine types not found — mill duty not attached.");
                return false;
            }

            _getFromIdMethod = _uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            if (_getFromIdMethod == null)
            {
                Plugin.Logger?.LogWarning("[MillDutyPatch] UniqueIDScriptable.GetFromID(string) not found — mill duty not attached.");
                return false;
            }
            return true;
        }

        private static void GraftDutyAndAttach()
        {
            if (!ResolveTypes()) return;

            var partnerAgent = _getFromIdMethod.Invoke(null, new object[] { PartnerAgentGuid });
            if (partnerAgent == null)
            {
                Plugin.Logger?.LogWarning("[MillDutyPatch] vanilla Agent_Partner NPCAgent not found (unexpected game update?) — duties not attached.");
                return;
            }

            if (!_millGrafted)
                _millGrafted = GraftOneDuty(partnerAgent, MillDutyUid, MillUid, "wdiOperateGrindingMill", "Grinding Mill",
                    AffectShape.DismantleAction);

            if (!_sluiceGrafted)
                _sluiceGrafted = GraftOneDuty(partnerAgent, SluiceDutyUid, SluiceUid, "wdiOperateOreSluice", "Ore Sluice",
                    AffectShape.DismantleAction);

            // Sawmill is the first CARD-ON-CARD (drag-based CardInteraction) duty in this file —
            // its "Cut" action is a CI, not a DismantleAction, so it takes the cardOnCard branch.
            if (!_sawmillGrafted)
                _sawmillGrafted = GraftOneDuty(partnerAgent, SawmillDutyUid, SawmillUid, "wdiOperateSawmill", "Sawmill",
                    AffectShape.CardOnCardAction);

            // ── Forge / Workshop (Duties-Ownership plan M3, Tier 1 candidate #4) ──────────────
            // REDUCED SCOPE, deliberately: the Partner only operates a forge the PLAYER has
            // already brought up to temperature. Only "Smelt Ore" (forge + workshop) and
            // "Hammer All" (workshop) carry this duty's CompatibleNPCDuties marker.
            //
            // "Blast" is deliberately NOT marked even though it is the JSON's own heat-up step.
            // ActionInterceptPatch.HandleBlastAllInner spawns a vanilla Ash card on EVERY press
            // regardless of whether anything was loaded to smelt, and the DA itself burns 16 fuel
            // — while its own gate (RequiredSpoilagePercent 1%..100%) stays satisfied at ANY lit
            // temperature. An autonomous Blast would therefore re-select forever, littering ash
            // and draining fuel that the already-shipped Firekeeping duty would dutifully refill.
            // Automating it would be net-harmful, not merely unproven.
            //
            // "Increase Temperature" (the bellows CI) is a safe heat-up action but is ALSO not
            // marked: it sits BEFORE "Smelt Ore" in CardInteractions and its gate (>=1% heat) is
            // strictly looser than Smelt Ore's (>=60%), so under CollectCardOnCardActionsList's
            // first-match-wins rule marking both would make the Partner pump the bellows forever
            // and never smelt. Fixing that needs either a CardInteractions reorder (implicit and
            // fragile) or custom ActionTag SOs driving ActionTagFilter — out of scope here.
            //
            // Two duties rather than one covering both cards: NPCItemSelectionSettings'
            // FoundAValidMatch returns the FIRST ItemPool entry that has enough matches, so a
            // single duty holding {forge, workshop} would always resolve to the forge and never
            // touch the workshop whenever both are placed and toggled on.
            if (!_forgeGrafted)
                _forgeGrafted = GraftOneDuty(partnerAgent, ForgeDutyUid, ForgeUid, "wdiOperateForge", "Water-Driven Forge",
                    AffectShape.CardOnCardAction);

            // Workshop runs the DismantleAction step FIRST on purpose: "Hammer All" needs >=50%
            // heat and consumes none, while "Smelt Ore" needs >=60% and spends 400 of it — doing
            // the hammer first means one duty pass can still land both on a workshop that is hot
            // enough for both, instead of the smelt knocking the temperature below the hammer gate.
            if (!_workshopGrafted)
                _workshopGrafted = GraftOneDuty(partnerAgent, WorkshopDutyUid, WorkshopUid, "wdiOperateWorkshop", "Water-Driven Workshop",
                    AffectShape.DismantleAction, AffectShape.CardOnCardAction);
        }

        /// <param name="affectSteps">
        /// One AffectItemsDutyAction per entry, appended after the Move step in the order given.
        /// Must contain at least one entry — a duty whose ActionSequence is Move-only is
        /// selectable but can never do any work.
        /// </param>
        private static bool GraftOneDuty(object partnerAgent, string dutyUid, string cardUid, string actionNamePrefix, string logLabel, params AffectShape[] affectSteps)
        {
            if (affectSteps == null || affectSteps.Length == 0)
            {
                Plugin.Logger?.LogWarning($"[MillDutyPatch] {logLabel} duty requested with no affect steps — refusing to attach a Move-only duty.");
                return false;
            }

            var duty = _getFromIdMethod.Invoke(null, new object[] { dutyUid });
            if (duty == null)
            {
                Plugin.Logger?.LogWarning($"[MillDutyPatch] NPCDuty '{dutyUid}' not found in AllData — is NPCDuty/{dutyUid}.json (top-level mod folder) shipped and deployed? {logLabel} duty not attached.");
                return false;
            }

            // Same asset-unload protection as the actions built below: this duty carries the
            // ActionSequence we graft on, and the caller's _xxxGrafted latch means we never
            // come back to rebuild it.
            if (duty is UnityEngine.Object dutyObj) dutyObj.hideFlags = HideFlags.DontUnloadUnusedAsset;

            var targetCard = CardUtil.GetCardDataById(cardUid);
            if (targetCard == null)
            {
                Plugin.Logger?.LogWarning($"[MillDutyPatch] {logLabel} CardData '{cardUid}' not resolvable — {logLabel} duty not attached.");
                return false;
            }

            var move = BuildMoveAction(targetCard, actionNamePrefix);
            if (move == null)
            {
                Plugin.Logger?.LogWarning($"[MillDutyPatch] Failed to build the {logLabel} move action — see prior warnings.");
                return false;
            }

            var sequence = Array.CreateInstance(_npcDutyActionType, affectSteps.Length + 1);
            sequence.SetValue(move, 0);
            for (int i = 0; i < affectSteps.Length; i++)
            {
                // Single-step duties keep the historical "<prefix>_Affect" asset name so the three
                // already-shipped duties (mill/sluice/sawmill) are byte-for-byte unchanged.
                var affectName = affectSteps.Length == 1 ? actionNamePrefix + "_Affect" : $"{actionNamePrefix}_Affect{i}";
                var affect = BuildAffectAction(targetCard, affectName, affectSteps[i] == AffectShape.CardOnCardAction);
                if (affect == null)
                {
                    Plugin.Logger?.LogWarning($"[MillDutyPatch] Failed to build {logLabel} affect step {i} ({affectSteps[i]}) — {logLabel} duty not attached.");
                    return false;
                }
                sequence.SetValue(affect, i + 1);
            }

            if (!Reflect.SetMember(duty, "ActionSequence", sequence))
            {
                Plugin.Logger?.LogWarning($"[MillDutyPatch] NPCDuty.ActionSequence field not found — {logLabel} duty not attached.");
                return false;
            }

            if (!AttachDutyRefToAgent(partnerAgent, duty))
            {
                Plugin.Logger?.LogWarning($"[MillDutyPatch] Failed to attach NPCDutyRef to Agent_Partner.AgentDuties for {logLabel}.");
                return false;
            }

            Plugin.Logger?.LogInfo($"[MillDutyPatch] {logLabel} duty attached to Agent_Partner (JSON-shell path).");
            return true;
        }

        // ── action construction ─────────────────────────────────────────────────

        private static object BuildMoveAction(object targetCard, string actionNamePrefix)
        {
            var move = ScriptableObject.CreateInstance(_moveDutyActionType);
            move.name = actionNamePrefix + "_Move";
            // Without this a Unity asset-unload can silently collect the action out from under
            // the grafted ActionSequence — no log, duty just stops being performable, and the
            // _xxxGrafted latches mean GraftOneDuty never re-runs to repair it. Matches the
            // framework's own Animals/DutyBuilder.cs.
            move.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Reflect.SetMember(move, "RequiredForSelectingDuty", false);
            SetEnumField(move, "MovementType", "Pathfind");
            // Neither the mill nor the sluice has a dedicated CardTag (CardTagsWarpData is just
            // tag_Wood/tag_Stone/tag_Bag, confirmed by grep) — target the CardData directly via
            // MoveToItems rather than MoveToEnvTag/MoveToTags (which resolve ENVIRONMENT tags,
            // not card tags — confirmed by reading MoveDutyAction.FindDestination's
            // MoveToEnvTag case).
            SetEnumField(move, "MoveDestination", "MoveToItem");

            var target = Activator.CreateInstance(_cardOrTagRefWithDurabilitiesType);
            Reflect.SetMember(target, "Target", targetCard);
            var items = Array.CreateInstance(_cardOrTagRefWithDurabilitiesType, 1);
            items.SetValue(target, 0);
            Reflect.SetMember(move, "MoveToItems", items);

            // Null-array hygiene on unused targeting fields — the engine iterates these
            // without null guards on some paths (mirrors AshPartnerDutyPatch.NewMoveAction).
            Reflect.SetMember(move, "MoveToEnvironments", Array.CreateInstance(_cardDataType, 0));
            Reflect.SetMember(move, "MoveToTags", Array.CreateInstance(_cardTagType, 0));
            Reflect.SetMember(move, "MoveCosts", Array.CreateInstance(_npcStatInstantModifierType, 0));
            SetEnumField(move, "CostRequirements", "IgnoreCost");

            return move;
        }

        /// <param name="cardOnCard">
        /// false = the station's work is a DismantleAction (a single self-button, e.g. "Grind All" /
        /// "Sluice All"); true = it is a CardInteraction (the player normally DRAGS an item onto the
        /// station, e.g. the Sawmill's "Cut"). The two take completely different engine paths inside
        /// NPCCardActionSelectionSettings.TryAddingCardToNPCActionList, and the CardOnCard path has
        /// an extra hard requirement — see the GivenCardSelection block below.
        /// </param>
        private static object BuildAffectAction(object targetCard, string affectName, bool cardOnCard = false)
        {
            var affect = ScriptableObject.CreateInstance(_affectItemsDutyActionType);
            affect.name = affectName;
            affect.hideFlags = HideFlags.DontUnloadUnusedAsset; // see BuildMoveAction
            Reflect.SetMember(affect, "RequiredForSelectingDuty", false);

            var itemSelection = Activator.CreateInstance(_npcItemSelectionSettingsType);
            var target = Activator.CreateInstance(_cardOrTagRefWithDurabilitiesType);
            Reflect.SetMember(target, "Target", targetCard);
            var quantity = Activator.CreateInstance(_cardOrTagQuantityType);
            Reflect.SetMember(quantity, "Target", target);
            Reflect.SetMember(quantity, "Quantity", Vector2.one);

            var poolType = typeof(System.Collections.Generic.List<>).MakeGenericType(_cardOrTagQuantityType);
            var pool = (System.Collections.IList)Activator.CreateInstance(poolType);
            pool.Add(quantity);
            // Load-bearing: this is the ONLY thing telling the duty which card to work on. A
            // silent miss leaves an empty pool, SelectItems returns null every tick, and the
            // Partner walks to the station and stands there — with "duty attached" still logged.
            if (!Reflect.SetMember(itemSelection, "ItemPool", pool))
            {
                Plugin.Logger?.LogWarning($"[MillDutyPatch] NPCItemSelectionSettings.ItemPool not found on the receiving-card selection for '{affectName}' (game update?) — duty step not built.");
                return null;
            }
            SetEnumField(itemSelection, "SearchPriority", "OnlyEnvironment");
            // The mechanism that makes the player's Duty Assignment toggle actually matter —
            // ItemScore.Exclude = !card.AssociatedDuties.Contains(thisDuty) when set to
            // OnlyItemsMarkedWithDuty (confirmed via research doc + ItemScore.cs citation).
            SetEnumField(itemSelection, "DutyPriority", "OnlyItemsMarkedWithDuty");
            if (!Reflect.SetMember(affect, "ItemSelection", itemSelection))
            {
                // AffectItemsDutyAction.StartDutyAction dereferences ItemSelection with no null
                // guard — a miss here is an NRE inside a duty coroutine, not a graceful skip.
                Plugin.Logger?.LogWarning($"[MillDutyPatch] AffectItemsDutyAction.ItemSelection not found for '{affectName}' (game update?) — duty step not built.");
                return null;
            }

            SetEnumField(affect, "AffectType", "PerformActionOnCard");

            var actionToPerform = Activator.CreateInstance(_npcCardActionSelectionSettingsType);
            SetEnumField(actionToPerform, "ActionType", cardOnCard ? "CardOnCardAction" : "DismantleAction");
            Reflect.SetMember(actionToPerform, "ActionTagFilter", Array.CreateInstance(_actionTagType, 0));

            if (cardOnCard)
            {
                // ── CardOnCardAction branch (drag-based CardInteraction) ──────────────
                // NPCCardActionSelectionSettings.CollectCardOnCardActionsList needs a SECOND item
                // selection to source the GIVEN card (the thing the player would have dragged).
                // Three things here are load-bearing and every one of them is a SILENT failure if
                // wrong — the DismantleAction path never touches any of them, which is why the
                // already-confirmed mill/sluice duties could not have surfaced these:
                //
                // 1. GivenCardSelection MUST be non-null. CollectCardOnCardActionsList calls
                //    GivenCardSelection.SelectItems(...) with NO null guard — leaving the field at
                //    its Activator default (null) is an NRE inside a duty coroutine, not a
                //    graceful skip.
                // 2. GivenCardSelectionMethod = UseActionCompatibleCards makes the engine pass the
                //    CI's OWN CompatibleCards.GetTriggerCards() as the search pool, so the drag
                //    candidates are derived from the "Cut" action itself (currently TreeLog). No
                //    log UID is hardcoded here, and re-pointing the CI at a different input later
                //    needs no change in this file.
                // 3. DutyPriority MUST NOT be OnlyItemsMarkedWithDuty. That setting is what makes
                //    the player's Duty Assignment toggle gate the STATION (ItemScore sets
                //    Exclude = !item.AssociatedDuties.Contains(duty)) — but a loose log on the
                //    ground carries no AssociatedDuties, so reusing it here would exclude every
                //    candidate and the Partner would stand at a working sawmill doing nothing.
                //    The station-level gate is already enforced by the receiving-card ItemSelection
                //    above; this selection must stay IgnoreDuty.
                var givenSelection = Activator.CreateInstance(_npcItemSelectionSettingsType);
                var givenPool = (System.Collections.IList)Activator.CreateInstance(
                    typeof(System.Collections.Generic.List<>).MakeGenericType(_cardOrTagQuantityType));
                // Unused under UseActionCompatibleCards, but kept non-null for hygiene. Guarded
                // like the ActionSequence write — a silent miss here NREs later inside
                // NPCCardActionSelectionSettings.CollectCardOnCardActionsList.
                if (!Reflect.SetMember(givenSelection, "ItemPool", givenPool))
                    Plugin.Logger?.LogWarning("[MillDutyPatch] NPCItemSelectionSettings.ItemPool not found on the given-card selection — CardOnCard duty may not source its dragged item.");
                // Prefer a log already lying in the environment, but fall back to one the Partner
                // is carrying (SelectItems' PrioritizeEnvironment case searches env, then inventory).
                SetEnumField(givenSelection, "SearchPriority", "PrioritizeEnvironment");
                SetEnumField(givenSelection, "DutyPriority", "IgnoreDuty");           // see (3) above
                SetEnumField(givenSelection, "OwnershipPriority", "IgnoreOwnership");
                // MANDATORY on this path — CollectCardOnCardActionsList dereferences
                // GivenCardSelection with no null guard, so a silent miss here is an NRE inside
                // a duty coroutine rather than a graceful skip.
                if (!Reflect.SetMember(actionToPerform, "GivenCardSelection", givenSelection))
                {
                    Plugin.Logger?.LogWarning("[MillDutyPatch] NPCCardActionSelectionSettings.GivenCardSelection not found (game update?) — a CardOnCard duty without it NREs at dispatch, so this duty is deliberately NOT attached.");
                    return null; // GraftOneDuty treats null as build failure and skips the graft
                }
                SetEnumField(actionToPerform, "GivenCardSelectionMethod", "UseActionCompatibleCards");
            }

            // Also dereferenced unguarded (AffectItemsDutyAction passes it straight into
            // SelectItems and PerformActions) — abort rather than ship a step that NREs on use.
            if (!Reflect.SetMember(affect, "ActionToPerform", actionToPerform))
            {
                Plugin.Logger?.LogWarning($"[MillDutyPatch] AffectItemsDutyAction.ActionToPerform not found for '{affectName}' (game update?) — duty step not built.");
                return null;
            }

            return affect;
        }

        private static bool AttachDutyRefToAgent(object agent, object duty)
        {
            var existing = Reflect.GetMember(agent, "AgentDuties") as Array;
            if (existing != null)
            {
                foreach (var entry in existing)
                {
                    if (entry == null) continue;
                    if (ReferenceEquals(Reflect.GetMember(entry, "TargetDuty"), duty))
                        return true; // idempotent — already attached from a prior LoadMainGameData pass
                }
            }

            var weights = Activator.CreateInstance(_npcDutyWeightsType);
            foreach (var field in _npcDutyWeightsType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!field.FieldType.IsArray) continue;
                field.SetValue(weights, Array.CreateInstance(field.FieldType.GetElementType(), 0));
            }
            var baseWeightField = _npcDutyWeightsType.GetField("BaseWeight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (baseWeightField == null)
            {
                // footgun #2: TotalWeight <= 0 => never selectable. Without this field the duty
                // would attach at weight 0 and silently never run, so fail loudly instead.
                Plugin.Logger?.LogWarning("[MillDutyPatch] NPCDutyWeights.BaseWeight not found (game update?) — refusing to attach a zero-weight duty that could never be selected.");
                return false;
            }
            baseWeightField.SetValue(weights, StationDutyBaseWeight);

            var dutyRef = Activator.CreateInstance(_npcDutyRefType);
            // Every one of these is load-bearing: a silent miss leaves a duty that either NREs
            // downstream or attaches in a permanently unusable state while this method still
            // reports success. Guard each and abort rather than logging "duty attached" for a
            // half-built NPCDutyRef.
            if (!Reflect.SetMember(dutyRef, "TargetDuty", duty))
            {
                Plugin.Logger?.LogWarning("[MillDutyPatch] NPCDutyRef.TargetDuty not found (game update?) — duty not attached.");
                return false;
            }
            SetEnumField(dutyRef, "ActivatingMode", "ActivateAutomatically");
            Reflect.SetMember(dutyRef, "StartActive", true);
            Reflect.SetMember(dutyRef, "ActivatingConditions", EmptyCondition());
            if (!Reflect.SetMember(dutyRef, "PreferenceWeights", weights))
            {
                // footgun #1: PreferenceWeights lives on NPCDutyRef, not NPCDuty. If this write
                // misses, the weights object above is discarded and the entry falls back to a
                // default (0) weight — selectable never, with no other symptom.
                Plugin.Logger?.LogWarning("[MillDutyPatch] NPCDutyRef.PreferenceWeights not found (game update?) — duty not attached (it would have had zero weight).");
                return false;
            }

            int oldLen = existing?.Length ?? 0;
            var merged = Array.CreateInstance(_npcDutyRefType, oldLen + 1);
            if (existing != null) Array.Copy(existing, merged, oldLen);
            merged.SetValue(dutyRef, oldLen);

            return Reflect.SetMember(agent, "AgentDuties", merged);
        }

        private static object EmptyCondition()
        {
            object boxed = Activator.CreateInstance(_generalConditionType);
            foreach (var field in _generalConditionType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!field.FieldType.IsArray) continue;
                field.SetValue(boxed, Array.CreateInstance(field.FieldType.GetElementType(), 0));
            }
            return boxed;
        }

        private static void SetEnumField(object instance, string fieldName, string enumValueName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                Plugin.Logger?.LogWarning($"[MillDutyPatch] Enum field '{fieldName}' not found on {instance.GetType().Name}.");
                return;
            }
            try { field.SetValue(instance, Enum.Parse(field.FieldType, enumValueName)); }
            catch (Exception ex) { Plugin.Logger?.LogWarning($"[MillDutyPatch] Failed to set {instance.GetType().Name}.{fieldName} = {enumValueName}: {ex.Message}"); }
        }
    }
}
