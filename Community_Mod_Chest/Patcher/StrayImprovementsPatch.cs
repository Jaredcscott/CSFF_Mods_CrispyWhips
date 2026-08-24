using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Every CMC WorldMap clone location (<c>WorldMap/MapNodes.json</c>, 12 nodes total) is
    /// <c>Object.Instantiate</c>-cloned from a vanilla CT8 explorable-location template
    /// (<c>CSFFModFramework/Loading/CardCloneService.cs</c> <c>TryCloneEnvironmentPair</c> →
    /// <c>CloneCard</c>), which deep-copies <c>EnvironmentImprovements</c> verbatim along with
    /// everything else. Originally discovered on the Village's template (vanilla
    /// <c>ClearingOak_GreenGlade</c>, CMC 1.65.2) and re-audited across all 12 nodes 2026-08-21
    /// (script cross-referenced each node's <c>CloneOfEnvironmentUID</c> CT4 against the CT8 its
    /// own <c>DefaultEnvCardDrops</c> resolves, per <c>CardCloneService.FindLocationCard</c>'s real
    /// pairing rule — first drop entry whose target is CardType 8 — rather than filename
    /// guesswork): 11 of the 12 templates carry a subset of the SAME 8 vanilla improvements CMC
    /// never intended to inherit — <c>Imp_PathNorth/East/South/West</c> and
    /// <c>Imp_HuntingFencesNorth/East/South/West</c> — unlocked, freely buildable, with zero CMC
    /// localization or tab awareness. Only cmcLocClayFlats' template
    /// (<c>River_ClearingAlder_BoggyMeadows</c>) carries none today; it is still covered below
    /// (a no-op) so a future template swap can't silently reopen the gap.
    ///
    /// <para>Completing one of the Path improvements resolves its travel destination from the CT8's
    /// <c>InGameCardBase.WorldTravelDestinations</c> cache (.decomp/InGameCardBase.cs ~4355; consumed
    /// by <c>CardData.GetRoadDestinationEnv</c>, .decomp/CardData.cs:2312), built once when the CT8's
    /// CardModel is first applied and never rebuilt afterward. CMC's seasonal snow-drift/deadfall
    /// SealableGates strip and restore travel DAs on these same CT8 cards every year, so that cache
    /// can diverge from the live DA state by the time a player actually finishes building one of
    /// these stray roads. Reported first on the Village ("built a road and could no longer travel
    /// south — it looped back to the Village instead", fixed 1.65.2 for cmcLocVillage only), then
    /// again on Pine Trail (arriving from the south auto-continues straight into the Village) —
    /// same root cause, un-fixed node.</para>
    ///
    /// <para><strong>Fix, part 1</strong> (<see cref="InitStats_Postfix"/>): strip the 8 stray GUIDs
    /// from every clone location's buildable <c>EnvironmentImprovements</c> list every boot, before
    /// a player can ever see or build them. Runs on the <c>GameManager.InitializeStatsAndActions</c>
    /// postfix — CardData SOs need no save-data dependency, so this can run as early as possible.</para>
    ///
    /// <para><strong>Fix, part 2</strong> (<see cref="OnRunStart_UnmarkBuiltStrays"/>): a save where a
    /// player already completed one of these 8 stray improvements before this fix shipped has its
    /// UID recorded in that environment's persisted <c>CurrentlyBuiltImprovements</c>
    /// (<c>GameManager.EnvironmentsData</c>, a <c>List&lt;string&gt;</c> — .decomp/EnvironmentSaveData.cs:29).
    /// That list is populated for real by <c>GameManager.LoadCards()</c> (.decomp/GameManager.cs:3542,
    /// reading <c>CurrentGameData.EnvironmentsData</c>), which runs INSIDE <c>Awake()</c> AFTER
    /// <c>InitializeStatsAndActions()</c> (.decomp/GameManager.cs: line 2385 vs. 2433) — so reading it
    /// from the part-1 postfix would always see it empty. This half instead hooks
    /// <c>GameManager.OnGMInitialized</c> (fires after <c>Awake</c> completes; same load-order
    /// reasoning as <c>InteriorEnvSaveDataPatch</c>) and removes only the 8 known stray GUIDs from
    /// each affected env's list, by exact string match. <c>CurrentlyBuiltImprovements</c> is also
    /// where CMC's OWN <c>SealableGateService</c> Marker gates persist their "cleared" state (e.g.
    /// <c>cmc_village_snow_north_cleared</c>) — those strings never collide with a stray GUID, so
    /// they are never touched. There is no vanilla API to remove a placed CT10 improvement card
    /// itself; un-marking + de-listing is the same scope as the original Village fix.</para>
    /// </summary>
    internal static class StrayImprovementsPatch
    {
        // (EnvironmentUID, LocationUID) pairs for every WorldMap/MapNodes.json clone node — same
        // 12-entry idiom as TreeRespawnPatch.EnvTrees. Hardcoded rather than parsed from
        // MapNodes.json at runtime: this list only changes when a new clone node is authored, and
        // every other per-node correction patch in this mod (TreeRespawnPatch, InteriorEnvSaveDataPatch)
        // already follows this convention.
        private static readonly (string EnvUid, string LocUid)[] CloneNodes =
        {
            ("cmcEnvVillagePath",     "cmcLocVillagePath"),
            ("cmcEnvHighGrove",       "cmcLocHighGrove"),
            ("cmcEnvPineTrail",       "cmcLocPineTrail"),
            ("cmcEnvVillage",         "cmcLocVillage"),
            ("cmcEnvVillageFarm",     "cmcLocVillageFarm"),
            ("cmcEnvClayFlats",       "cmcLocClayFlats"),      // template carries 0 improvements today — kept for future-proofing
            ("cmcEnvMarshHollow",     "cmcLocMarshHollow"),
            ("cmcEnvMossyClearing",   "cmcLocMossyClearing"),
            ("cmcEnvForagingForest",  "cmcLocForagingForest"),
            ("cmcEnvHuntersCrossing", "cmcLocHuntersCrossing"),
            ("cmcEnvDeerMeadow",      "cmcLocDeerMeadow"),
            ("cmcEnvBadgerWarren",    "cmcLocBadgerWarren"),
        };

        private static readonly HashSet<string> StrayImprovementUids = new(StringComparer.Ordinal)
        {
            "dd31ad4b8b070494294fde82c6523143", // Imp_PathNorth
            "aceac961438f7cc48996a34de4fd8844", // Imp_PathEast
            "b1d3de19f5f9660428e4983bc90eace6", // Imp_PathSouth
            "c0d621b8981ad3b499559262912ed7f8", // Imp_PathWest
            "55e19a489d6a04547b6df7455ff2cdcd", // Imp_HuntingFencesNorth
            "76eab4fec6695fb43b6503ad3d4e7d78", // Imp_HuntingFencesEast
            "90d63db8ef5f184428d940bd4d606449", // Imp_HuntingFencesSouth
            "6f664537b2282e346b5fe4fddd7410ba", // Imp_HuntingFencesWest
        };

        private static bool _initialized;
        private static Action _gmInitializedHandler;

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            var gmType = CardUtil.FindGameType("GameManager");
            if (gmType == null)
            {
                Plugin.Logger.LogWarning("[StrayImprovementsPatch] GameManager type not found — stray vanilla road/fence improvements will not be handled.");
                return;
            }

            var initStatsMethod = AccessTools.Method(gmType, "InitializeStatsAndActions");
            if (initStatsMethod != null)
            {
                harmony.Patch(initStatsMethod, postfix: new HarmonyMethod(typeof(StrayImprovementsPatch), nameof(InitStats_Postfix)));
            }
            else
            {
                Plugin.Logger.LogWarning("[StrayImprovementsPatch] GameManager.InitializeStatsAndActions not found — stray improvements will remain buildable.");
            }

            var onGmInitField = gmType.GetField("OnGMInitialized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (onGmInitField != null && onGmInitField.FieldType == typeof(Action))
            {
                _gmInitializedHandler = OnRunStart_UnmarkBuiltStrays;
                var current = (Action)onGmInitField.GetValue(null);
                onGmInitField.SetValue(null, (Action)Delegate.Combine(current, _gmInitializedHandler));
            }
            else
            {
                Plugin.Logger.LogWarning("[StrayImprovementsPatch] GameManager.OnGMInitialized not found — already-built stray improvements on existing saves will not be cleared.");
            }

            Plugin.Logger.LogDebug("[StrayImprovementsPatch] initialized.");
        }

        // ------------------------------------------------------------- part 1 ---

        private static void InitStats_Postfix()
        {
            int totalStripped = 0, nodesAffected = 0;

            foreach (var (_, locUid) in CloneNodes)
            {
                try
                {
                    int stripped = StripBuildableImprovements(locUid);
                    if (stripped > 0) { nodesAffected++; totalStripped += stripped; }
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[StrayImprovementsPatch] '{locUid}' buildable-list strip failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
            }

            if (totalStripped > 0)
                Plugin.Logger.LogInfo($"[StrayImprovementsPatch] Removed {totalStripped} stray vanilla road/fence improvement(s) from {nodesAffected} clone location(s)' buildable list.");
        }

        /// <summary>
        /// Strips the 8 stray GUIDs from <paramref name="locUid"/>'s CardData.EnvironmentImprovements.
        /// Returns the number removed (0 if the card/field is missing, empty, or already clean).
        /// </summary>
        private static int StripBuildableImprovements(string locUid)
        {
            var location = CardUtil.GetCardDataById(locUid);
            if (location == null)
            {
                Plugin.Logger.LogDebug($"[StrayImprovementsPatch] '{locUid}' not found — skipped.");
                return 0;
            }

            var field = CardUtil.GetCachedField(location.GetType(), "EnvironmentImprovements");
            if (field?.GetValue(location) is not Array arr || arr.Length == 0)
                return 0;

            var kept = new List<object>();
            int removed = 0;
            foreach (var entry in arr)
            {
                var card = entry == null ? null : CardUtil.GetMemberValue(entry, "Card");
                var uid = card == null ? null : CardUtil.GetMemberValue(card, "UniqueID") as string;
                if (uid != null && StrayImprovementUids.Contains(uid))
                {
                    removed++;
                    continue;
                }
                kept.Add(entry);
            }

            if (removed == 0) return 0;

            var elementType = arr.GetType().GetElementType();
            var next = Array.CreateInstance(elementType!, kept.Count);
            for (int i = 0; i < kept.Count; i++) next.SetValue(kept[i], i);
            field.SetValue(location, next);

            Plugin.Logger.LogDebug($"[StrayImprovementsPatch] '{locUid}': removed {removed} stray improvement(s) from EnvironmentImprovements ({arr.Length} -> {next.Length}).");
            return removed;
        }

        // ------------------------------------------------------------- part 2 ---

        private static void OnRunStart_UnmarkBuiltStrays()
        {
            try
            {
                int totalRemoved = 0, envsAffected = 0;

                foreach (var (envUid, _) in CloneNodes)
                {
                    try
                    {
                        int removed = UnmarkBuiltImprovements(envUid);
                        if (removed > 0) { envsAffected++; totalRemoved += removed; }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Logger.LogWarning($"[StrayImprovementsPatch] '{envUid}' CurrentlyBuiltImprovements cleanup failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                    }
                }

                if (totalRemoved > 0)
                    Plugin.Logger.LogInfo($"[StrayImprovementsPatch] Cleared {totalRemoved} already-built stray improvement marker(s) from {envsAffected} environment(s)' saved state.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[StrayImprovementsPatch] OnRunStart_UnmarkBuiltStrays failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// Clears any of the 8 stray GUIDs from <paramref name="envUid"/>'s persisted
        /// <c>CurrentlyBuiltImprovements</c> (<c>GameManager.EnvironmentsData</c>), so a save where a
        /// player already completed one of these before this fix shipped is un-stuck without a fresh
        /// save. Exact-string-match removal only — matches an env-save entry the same way
        /// <c>CardUtil.IsImprovementBuilt</c>/<c>MarkImprovementBuilt</c> do (by <c>EnvironmentID</c>,
        /// <c>DictionaryKey</c> via <see cref="CardUtil.EnvKeyMatchesUid"/>, or raw dictionary key),
        /// since an env can carry more than one matching entry shape across a save's lifetime.
        /// </summary>
        private static int UnmarkBuiltImprovements(string envUid)
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return 0;

            var envDataField = CardUtil.GetCachedField(gm.GetType(), "EnvironmentsData");
            if (envDataField?.GetValue(gm) is not IDictionary envData) return 0;

            int totalRemoved = 0;
            foreach (DictionaryEntry entry in envData)
            {
                var value = entry.Value;
                if (value == null) continue;
                var vt = value.GetType();
                var envId = CardUtil.GetCachedField(vt, "EnvironmentID")?.GetValue(value) as string;
                var dictKey = CardUtil.GetCachedField(vt, "DictionaryKey")?.GetValue(value) as string;
                bool isTarget =
                    envUid.Equals(envId, StringComparison.Ordinal) ||
                    CardUtil.EnvKeyMatchesUid(envUid, dictKey) ||
                    envUid.Equals(entry.Key as string, StringComparison.Ordinal);
                if (!isTarget) continue;

                var cbiField = CardUtil.GetCachedField(vt, "CurrentlyBuiltImprovements");
                if (cbiField?.GetValue(value) is not IList built || built.Count == 0) continue;

                for (int i = built.Count - 1; i >= 0; i--)
                {
                    if (built[i] is string s && StrayImprovementUids.Contains(s))
                    {
                        built.RemoveAt(i);
                        totalRemoved++;
                    }
                }
            }

            if (totalRemoved > 0)
                Plugin.Logger.LogDebug($"[StrayImprovementsPatch] '{envUid}': cleared {totalRemoved} stray already-built marker(s) from CurrentlyBuiltImprovements.");
            return totalRemoved;
        }
    }
}
