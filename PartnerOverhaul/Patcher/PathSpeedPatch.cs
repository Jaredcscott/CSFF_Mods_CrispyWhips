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

        private static bool MaybeSkipCosts(ref IEnumerator __result)
        {
            if (!Plugin.ReduceNPCMoveCosts.Value) return true;
            try
            {
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

        private static IEnumerator EmptyEnumerator()
        {
            yield break;
        }
    }
}
