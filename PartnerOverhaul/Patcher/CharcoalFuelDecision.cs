namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// Stands in for vanilla's "Tag Fuel on Board" step inside the Fire Keeping duty only, so that
    /// Charcoal on the board opens the same PartnerAction3 feed step a tag_Fuel card does.
    ///
    /// Why a stand-in and not a data edit: Fire Keeping reaches its PartnerAction3 step ("Firekeep
    /// with Tag Fuel") only through that decision, whose condition is RequiredTagsOnBoard
    /// [tag_Fuel], and vanilla Charcoal carries no tag_Fuel (it has tag_Millable and
    /// tag_TraderInterestX2Metal), so the duty read NotSelectable with only Charcoal around
    /// (T2.500, measured on 1.1.0). A GeneralCondition ANDs every board requirement, so no data
    /// shape can say "tag_Fuel OR Charcoal". Giving Charcoal tag_Fuel would, but that is a vanilla
    /// card change every fire sees: each fire's "Feed Fuel" (tag_Fuel) CardInteraction sits before
    /// its "Feed Charcoal", so a player's charcoal drag would start resolving as Feed Fuel.
    ///
    /// This class keeps the vanilla decision's own Conditions, OnTrue and OnFalse, and adds one
    /// alternative condition (the same conditions with Charcoal in place of tag_Fuel). It is
    /// installed by replacing the one step in the Fire Keeping duty's ActionSequence, never by
    /// mutating the vanilla decision asset, which other duties may share. Step indices do not move,
    /// so a saved duty step stays valid with or without the mod.
    ///
    /// PO-N1 (1.1.4): the Charcoal alternative honours "Reserved Fuel/Wood UIDs" with the same
    /// test ReserveAwareDecision uses, so a reserved Charcoal never opens the feed branch.
    /// GameLoadPatch.GuardReservedFuelBranches still skips this subclass: the guard lives here.
    /// </summary>
    public class CharcoalFuelDecision : TakeSimpleDecisionDutyAction
    {
        // Same shape as Conditions with tag_Fuel swapped for Charcoal (built by GameLoadPatch).
        public GeneralCondition AlternativeConditions;

        private static bool _warnedEvaluate;

        private bool Evaluate(InGameNPC npc, NPCDuty duty)
        {
            try
            {
                var gm = MBSingleton<GameManager>.Instance;
                bool notInBase = gm != null && gm.NotInBase;
                var fromCard = npc ? npc.AssociatedCard : null;
                if (!Conditions.IsEmpty && Conditions.ConditionsValid(notInBase, fromCard, null, npc)) return true;
                if (AlternativeConditions.IsEmpty) return false;
                // PO-N1: the alternative names Charcoal. When Charcoal is on the "Reserved Fuel/Wood
                // UIDs" list the reserve filter empties the feed step's item list, so the branch is
                // answered false here and the duty falls through (the T3.39 shape). Empty list: no change.
                if (WoodReserveListPatch.AppliesTo(duty, npc && npc.AlliedWithPlayer)
                    && ReserveAwareDecision.NamesReservedCard(AlternativeConditions, WoodReserveListPatch.IsReserved))
                    return false;
                return AlternativeConditions.ConditionsValid(notInBase, fromCard, null, npc);
            }
            catch (Exception ex)
            {
                // Fall back to vanilla's negative branch; say so once, never per tick.
                if (!_warnedEvaluate)
                {
                    _warnedEvaluate = true;
                    Plugin.Logger.LogWarning($"[CharcoalFuelDecision] condition check threw, treating as 'no fuel' (vanilla OnFalse): {ex}");
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
