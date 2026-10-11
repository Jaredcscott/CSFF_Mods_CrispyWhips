namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// Stands in for a Fire Keeping "X on Board / X in Inventory" step whose card is on the
    /// "Reserved Fuel/Wood UIDs" list, so the duty falls through to its other fuel branches
    /// instead of entering a branch the reserve filter has already emptied.
    ///
    /// Why this exists (T3.39, MEASURED 2026-10-09): Fire Keeping's ActionSequence is a chain of
    /// decisions, each "X on Board" true leading to ONE feed step and then "Reduce Favour and
    /// Complete". With Wood on the board that decision is true, the feed step's item list is
    /// built by NPCItemSelectionSettings.SortFoundItemsByPreference, WoodReserveListPatch removes
    /// every reserved Wood from it, the list is empty, and the duty either reads NotSelectable
    /// (CanBePerformed false) or completes having fed nothing. It never reaches the
    /// tag_Fuel branch that holds the unreserved twig, so the fire goes out beside a Partner who
    /// holds fuel she may burn.
    ///
    /// Behaviour: identical to vanilla's TakeSimpleDecisionDutyAction except that when the
    /// vanilla condition is true ONLY because of a reserved card (the condition names a reserved
    /// CardData in RequiredCardsOnBoard / RequiredCardsInInventory) and the reserve filter would
    /// act for this NPC and duty (WoodReserveListPatch.AppliesTo), the decision answers false.
    /// Reservation is by UniqueID, so every card the condition tests is reserved: the branch it
    /// guards is always empty after filtering, and answering false is exact, never a guess.
    ///
    /// Installed by GameLoadPatch.GuardReservedFuelBranches by replacing steps in the Fire Keeping
    /// duty's own ActionSequence array (the vanilla decision assets are untouched and step
    /// indices do not move, as CharcoalFuelDecision does), and only when the option is set.
    /// </summary>
    public class ReserveAwareDecision : TakeSimpleDecisionDutyAction
    {
        private static bool _warnedEvaluate;

        // True when the vanilla condition names a reserved card (board or inventory list).
        internal static bool NamesReservedCard(GeneralCondition c, Func<string, bool> isReserved)
        {
            return AnyReserved(c.RequiredCardsOnBoard, isReserved) || AnyReserved(c.RequiredCardsInInventory, isReserved);
        }

        private static bool AnyReserved(CardData[] cards, Func<string, bool> isReserved)
        {
            if (cards == null) return false;
            for (int i = 0; i < cards.Length; i++)
                if (cards[i] != null && isReserved(cards[i].UniqueID)) return true;
            return false;
        }

        private bool Evaluate(InGameNPC npc, NPCDuty duty)
        {
            try
            {
                if (Conditions.IsEmpty) return false;
                var gm = MBSingleton<GameManager>.Instance;
                bool notInBase = gm != null && gm.NotInBase;
                var fromCard = npc ? npc.AssociatedCard : null;
                if (!Conditions.ConditionsValid(notInBase, fromCard, null, npc)) return false;
                if (WoodReserveListPatch.AppliesTo(duty, npc && npc.AlliedWithPlayer)
                    && NamesReservedCard(Conditions, WoodReserveListPatch.IsReserved))
                    return false;
                return true;
            }
            catch (Exception ex)
            {
                // Vanilla's negative branch; say so once, never per tick.
                if (!_warnedEvaluate)
                {
                    _warnedEvaluate = true;
                    Plugin.Logger.LogWarning($"[ReserveAwareDecision] condition check threw, treating as false (vanilla OnFalse): {ex}");
                }
                return false;
            }
        }

        public override IEnumerator StartDutyAction(InGameNPC _OnNPC)
        {
            // Vanilla's own body: set the outcome synchronously on the first MoveNext.
            var duty = _OnNPC ? _OnNPC.CurrentDuty.Duty : null;
            _OnNPC.CurrentDuty.SetDutyDecisionOutcome(Evaluate(_OnNPC, duty) ? OnTrue : OnFalse);
            yield break;
        }

        public override bool CanBePerformed(InGameNPC _FromNPC, NPCDuty _FromDuty, bool _ActionStarted, int _FromStep)
        {
            if (!_FromNPC || !_FromDuty) return false;
            var outcome = Evaluate(_FromNPC, _FromDuty) ? OnTrue : OnFalse;
            // Mirrors vanilla's private CheckIfNextActionCanBePerformed.
            if (outcome.Outcome == DutyDecisionOutcomes.CancelDuty) return false;
            var next = _FromDuty.GetAction(outcome, _FromStep, out int nextStep);
            return !next || next.CanBePerformed(_FromNPC, _FromDuty, _ActionStarted, nextStep);
        }
    }
}
