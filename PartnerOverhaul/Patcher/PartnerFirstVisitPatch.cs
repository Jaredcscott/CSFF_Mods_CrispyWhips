namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// PO-C13. First visits: a one-shot remark the first time the Partner is with you in a new region.
    /// One clamped marker NPCStat per region (NPCStat/poStatRegion*.json, registered on Agent_Partner by
    /// GameSourceModify/Agent_Partner.json; plan R3: an old save gets it at its start value 0):
    ///   0 = never been there together, 1 = arrived, remark not yet said, 2 = remark said (for good).
    /// This hook only moves 0 to 1. The Talk mood router (PO_PartnerTalk_Moods) weights the
    /// PO_PartnerTalk_FirstVisit_* line on the marker at exactly 1, and that line adds +1, so it plays once.
    ///
    /// A region is a vanilla CT4 environment tag (plan 3.2 PO-C13, the export carrier sets in the
    /// dated note of the PO-C13 bullet), matched with the game's own GameManager.EnvironmentHasTag on the
    /// PLAYER's current environment while the Partner's card is on that board (MatchesPlayerEnv), so a
    /// solo expedition does not count. Indoor boards (tag_EnvIndoors, which every cave, mine and home
    /// carries) never count: tag_Env_River is also on Env_WaterfallCaves and every cave cloned from it,
    /// and a nested home inherits its parent environment's tags.
    ///
    /// Environment entry (CLAUDE.md "Runtime Card Spawning", the ChangeEnvironment bullet): never sample
    /// once. A per-run coroutine polls every PollSeconds and does nothing while the game is initializing,
    /// in an environment transition or catching up, or while the Partner is not yet on the board; it
    /// keeps polling, so an arrival seen late is still recorded. The only "handled" state is the marker
    /// itself, read back on every poll; a write that has not landed after RetryPolls polls is re-issued.
    ///
    /// InGameNPC.ChangeStat is an IEnumerator: it is STARTED with gm.StartCoroutine, never called bare.
    /// Every early return on a lookup path logs a warn-once breadcrumb per cause. Calls no
    /// CSFFModFramework member.
    /// </summary>
    public static class PartnerFirstVisitPatch
    {
        internal const string PartnerModelName = "Agent_Partner";
        internal const string IndoorsTagName = "tag_EnvIndoors";
        private const float PollSeconds = 2f;
        private const int RetryPolls = 5;
        private const string Source = "PartnerOverhaul_FirstVisit";

        // Region marker stat UID -> the vanilla CardTag runtime name that defines the region.
        internal static readonly string[][] Regions =
        {
            new[] { "poStatRegionLake", "tag_Env_LakeArea" },
            new[] { "poStatRegionWetlands", "tag_Env_Wetlands" },
            new[] { "poStatRegionRiver", "tag_Env_River" },
            new[] { "poStatRegionPine", "tag_Env_GrovePine" },
            new[] { "poStatRegionOak", "tag_Env_GroveOak" },
        };

        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;
        private static readonly HashSet<string> Warned = new HashSet<string>();

        // Per-run state, reset at every run boot.
        private static int _runGeneration;
        private static int _pollCount;
        private static readonly Dictionary<string, CardTag> TagCache = new Dictionary<string, CardTag>();
        private static readonly HashSet<string> MissingTags = new HashSet<string>();
        private static readonly Dictionary<string, NPCStat> StatCache = new Dictionary<string, NPCStat>();
        private static readonly Dictionary<string, int> PendingSince = new Dictionary<string, int>();

        public static void ApplyPatch(Harmony harmony)
        {
            if (Plugin.PartnerTalkAnswer != null && !Plugin.PartnerTalkAnswer.Value)
            {
                Logger.LogDebug("PartnerFirstVisitPatch skipped: 'Talk With Your Partner' is off, so no first-visit remark can be heard.");
                return;
            }
            var initStats = AccessTools.Method(typeof(GameManager), "InitializeStatsAndActions");
            if (initStats == null)
            {
                WarnOnce("InitializeStatsAndActions-missing", "GameManager.InitializeStatsAndActions not found; first-visit markers are off");
                return;
            }
            harmony.Patch(initStats, postfix: new HarmonyMethod(typeof(PartnerFirstVisitPatch), nameof(InitStats_Postfix)));
            Logger.LogDebug("PartnerFirstVisitPatch applied.");
        }

        private static void WarnOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Logger.LogWarning($"[PartnerFirstVisitPatch] {cause}: {detail} (further occurrences suppressed)");
        }

        private static void DebugOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Logger.LogDebug($"[PartnerFirstVisitPatch] {cause}: {detail}");
        }

        private static void InitStats_Postfix(GameManager __instance)
        {
            try
            {
                // Per-run reset; a new generation ends any poll loop left from an earlier run.
                _runGeneration++;
                _pollCount = 0;
                TagCache.Clear();
                MissingTags.Clear();
                StatCache.Clear();
                PendingSince.Clear();
                var gm = __instance != null ? __instance : MBSingleton<GameManager>.Instance;
                if (gm == null) { WarnOnce("boot-no-gamemanager", "no GameManager at run boot; first-visit markers are off this run"); return; }
                gm.StartCoroutine(PollLoop(_runGeneration));
            }
            catch (Exception ex)
            {
                WarnOnce("InitStats_Postfix-exception", ex.ToString());
            }
        }

        private static IEnumerator PollLoop(int generation)
        {
            var wait = new WaitForSecondsRealtime(PollSeconds);
            while (generation == _runGeneration)
            {
                yield return wait;
                if (generation != _runGeneration) yield break;
                try { Poll(); }
                catch (Exception ex) { WarnOnce("Poll-exception", ex.ToString()); }
            }
        }

        /// <summary>One sample. Returns without a verdict whenever the board may not be ready; the loop
        /// samples again, so nothing is latched on a not-ready board.</summary>
        private static void Poll()
        {
            _pollCount++;
            var gm = MBSingleton<GameManager>.Instance;
            if (gm == null) return;
            if (gm.IsInitializing || gm.EnvironmentTransition || gm.IsCatchingUp) return; // not ready: retry
            var env = gm.CurrentEnvironment;
            if (env.IsNull) return;
            var partner = FindPartnerOnPlayerBoard(gm);
            if (partner == null) return; // not with the player (or not on this board yet): retry
            var indoors = ResolveTag(IndoorsTagName);
            if (indoors == null) return;
            if (gm.EnvironmentHasTag(env, indoors, null)) return; // caves, mines, homes never count

            foreach (var region in Regions)
            {
                string statUid = region[0];
                var tag = ResolveTag(region[1]);
                if (tag == null) continue;
                if (!gm.EnvironmentHasTag(env, tag, null)) continue;
                var stat = ResolveStat(statUid);
                if (stat == null) continue;
                if (!partner.HasStat(stat))
                {
                    WarnOnce("stat-not-on-partner-" + statUid, $"the Partner has no '{statUid}' (GameSourceModify/Agent_Partner.json not applied?); no first-visit marker");
                    continue;
                }
                if (partner.GetStatValue(stat) >= 0.5f)
                {
                    PendingSince.Remove(statUid); // already arrived (1) or already heard (2)
                    continue;
                }
                if (PendingSince.TryGetValue(statUid, out int since))
                {
                    if (_pollCount - since < RetryPolls) continue; // the write is still landing
                    WarnOnce("write-not-landed-" + statUid, $"the '{statUid}' marker still reads 0 {RetryPolls} polls after it was raised; raising it again");
                }
                var mod = new NPCStatInstantModifier(stat, 1f, null);
                // IEnumerator: start it, never call it bare.
                gm.StartCoroutine(partner.ChangeStat(mod, null, Source, -1));
                PendingSince[statUid] = _pollCount;
                Logger.LogInfo($"[PartnerFirstVisit] first time with the Partner in region {region[1]} ({env.EnvCard.name}); remark queued for the next Talk.");
            }
        }

        /// <summary>The live, initialized Partner when its card is on the player's board, else null.</summary>
        private static InGameNPC FindPartnerOnPlayerBoard(GameManager gm)
        {
            var all = gm.AllNPCs;
            if (all == null) { WarnOnce("AllNPCs-null", "GameManager.AllNPCs is null; no first-visit marker"); return null; }
            bool anyPartner = false;
            foreach (var npc in all)
            {
                if (npc == null || npc.NPCModel == null || npc.NPCModel.name != PartnerModelName) continue;
                anyPartner = true;
                if (!npc.Initialized) continue;
                var card = npc.AssociatedCard;
                if (card == null) { DebugOnce("partner-no-card", "the Partner has no AssociatedCard; treated as not on the board"); continue; }
                if (card.CardEnvironment.MatchesPlayerEnv) return npc;
            }
            if (!anyPartner) DebugOnce("no-partner", "no Agent_Partner in this run; no first-visit markers");
            return null;
        }

        private static CardTag ResolveTag(string name)
        {
            if (TagCache.TryGetValue(name, out var cached) && cached != null) return cached;
            if (MissingTags.Contains(name)) return null; // vanilla tags load before the run boots: one scan per run
            foreach (var tag in Resources.FindObjectsOfTypeAll<CardTag>())
            {
                if (tag != null && tag.name == name)
                {
                    TagCache[name] = tag;
                    return tag;
                }
            }
            MissingTags.Add(name);
            WarnOnce("tag-missing-" + name, $"CardTag '{name}' is not loaded (renamed by a game update?); its region is skipped");
            return null;
        }

        private static NPCStat ResolveStat(string uid)
        {
            if (StatCache.TryGetValue(uid, out var cached) && cached != null) return cached;
            var stat = UniqueIDScriptable.GetFromID<NPCStat>(uid);
            if (stat == null)
            {
                WarnOnce("stat-missing-" + uid, $"NPCStat '{uid}' did not resolve (CSFFModFramework absent, or NPCStat JSON not loaded); no first-visit marker");
                return null;
            }
            StatCache[uid] = stat;
            return stat;
        }
    }
}
