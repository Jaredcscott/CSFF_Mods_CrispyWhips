namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// New QoL feature, NOT a bug fix — confirmed via research that vanilla's "path speed bonus"
    /// (the RoadTravelingAid* GameStat family) only ever reaches CardAction.CollectActionModifiers
    /// for the PLAYER's own travel DAs; MoveDutyAction (NPC movement) never calls that method at
    /// all, and there is no vanilla CardTag identifying a "path" tile for NPC movement to key off
    /// (confirmed: zero tag_Path/tag_Road entries in the full vanilla CardTag list). So a literal
    /// "partners get the same path bonus as the player" isn't a groundable fix — this instead
    /// offers a general, honestly-labeled, OPT-IN discount on the resource-stat cost a Partner
    /// pays for inter-environment travel (MoveDutyAction.MoveCosts). Default OFF.
    /// Scope (1.1.0): MoveDutyAction.ApplyCosts runs for whichever NPC moves, wild animals
    /// included (measured: a wild Agent_Wolf's WolfFleeMove stamina cost was skipped at 100%),
    /// so the prefix now returns to vanilla unless the moving NPC is allied with the player.
    /// </summary>
    public static class PathSpeedPatch
    {
        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;

        public static void ApplyPatch(Harmony harmony)
        {
            var applyCosts = AccessTools.Method(typeof(MoveDutyAction), "ApplyCosts");
            if (applyCosts == null)
            {
                Logger.LogWarning("[PathSpeedPatch] MoveDutyAction.ApplyCosts not found — move-cost discount feature unavailable.");
                return;
            }
            harmony.Patch(applyCosts, prefix: new HarmonyMethod(typeof(PathSpeedPatch), nameof(MaybeSkipCosts)));
            Logger.LogDebug("PathSpeedPatch applied.");
        }

        // _OnNPC is injected by Harmony by parameter name from ApplyCosts(InGameNPC _OnNPC). Kept
        // TYPED (never object): a destroyed Unity object is only null under UnityEngine.Object's ==.
        private static bool MaybeSkipCosts(ref IEnumerator __result, InGameNPC _OnNPC)
        {
            if (!Plugin.ReduceNPCMoveCosts.Value) return true;
            try
            {
                if (!AppliesTo(_OnNPC != null && _OnNPC.AlliedWithPlayer)) return true; // wild NPCs pay vanilla costs
                int pct = Plugin.NPCMoveCostReductionPercent.Value;
                if (pct > 0 && UnityEngine.Random.Range(0, 100) < pct)
                {
                    __result = EmptyEnumerator();
                    return false; // skip original entirely for this hop — no partial mutation of shared MoveCosts data
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[PathSpeedPatch] MaybeSkipCosts failed: {ex}");
            }
            return true;
        }

        /// <summary>
        /// True only for an NPC allied with the player (a Partner): the one NPC the option's text
        /// promises to discount. Internal so a harness leg can call it.
        /// </summary>
        internal static bool AppliesTo(bool allied)
        {
            return allied;
        }

        private static IEnumerator EmptyEnumerator()
        {
            yield break;
        }
    }
}
