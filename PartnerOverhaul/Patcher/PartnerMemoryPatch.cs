namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// PO-C7. The Partner remembers what just happened. Four short-lived memory NPCStats on
    /// Agent_Partner (NPCStat/poStatMemory*.json, registered by GameSourceModify/Agent_Partner.json):
    /// each starts at 0, is raised to 96 when its event happens and counts down by 1 a tick, so a
    /// memory lasts one day (plan 3.6, Q-T5). The Talk mood router (PO_PartnerTalk_Moods) and the
    /// topic hub (PO_PartnerTalk_Topics) weight or gate a spoken remark on it, and the remark line
    /// clears it, so one event is talked about at most once.
    ///
    /// Events (vanilla static Actions, GameManager.cs:30-68), each only while the Partner's card is on
    /// the player's board (AllCards env scoping: AssociatedCard.CardEnvironment.MatchesPlayerEnv):
    ///   - fight won: GameManager.OnEncounterEnemyDefeated (raised from EncounterPopup);
    ///   - got away: GameManager.OnEncounterPlayerEscaped;
    ///   - structure finished: GameManager.OnActionPerformed, by plan R4: a not-cancelled IsBpAction
    ///     whose first produced collection is "&lt;blueprint&gt;_blueprint" (only the LAST stage gets it)
    ///     and whose blueprint result is a Base or Location card;
    ///   - first catch of the day: GameManager.OnCardSpawned with a whole vanilla fish (the six
    ///     Fish_* cards; the export's tags are obfuscated, so a fixed UID list).
    ///
    /// Per-Run Event Registration (CLAUDE.md, pattern b): the subscription is made from a
    /// GameManager.InitializeStatsAndActions postfix, which runs on EVERY run boot, so a once-per-process latch early-returns before the
    /// four += lines; only the per-run state (the day latch, the stat cache) is reset ahead of it.
    /// Three save reloads therefore still give one handler per event.
    ///
    /// The write goes through InGameNPC.ChangeStat, an IEnumerator: it is STARTED as a coroutine on
    /// the GameManager, never called bare (a bare call only builds the state machine and changes
    /// nothing; CLAUDE.md "Runtime Card Removal"). Every early return on a lookup path logs a
    /// warn-once breadcrumb per cause. Calls no CSFFModFramework member.
    /// </summary>
    public static class PartnerMemoryPatch
    {
        internal const string PartnerModelName = "Agent_Partner";
        internal const string FightStatUid = "poStatMemoryFight";
        internal const string EscapeStatUid = "poStatMemoryEscape";
        internal const string BuildStatUid = "poStatMemoryBuild";
        internal const string CatchStatUid = "poStatMemoryCatch";
        internal const float MemoryStart = 96f; // one day of ticks; the stat clamps at 96
        private const string Source = "PartnerOverhaul_Memory";

        // Whole vanilla fish (Fish_Char, Fish_Eel, Fish_Perch, Fish_Pike, Fish_Sturgeon, Fish_Trout),
        // UIDs read from the EA 0.69c export CardData/Fish_*.json.
        private static readonly HashSet<string> CatchCardUids = new HashSet<string>
        {
            "a966c94277465f741857b994202791f3",
            "bb8a42f395c31894188ff3038d76128f",
            "e64237c9a44ea3945bc75b8b9f4735c0",
            "70df5b800c3fd56499fd412bb9ce7523",
            "de00abf9d0d186b42bed6807ba49a4cb",
            "f4972170adc8b3d4f9d4bc09c6e7f760",
        };

        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;
        private static readonly HashSet<string> Warned = new HashSet<string>();

        // Once-per-process latch (pattern b). Never reset per run.
        private static bool _subscribed;

        // Per-run state, reset at every run boot before the latch.
        private static int _lastCatchDay = -1;
        private static readonly Dictionary<string, NPCStat> StatCache = new Dictionary<string, NPCStat>();

        public static void ApplyPatch(Harmony harmony)
        {
            var initStats = AccessTools.Method(typeof(GameManager), "InitializeStatsAndActions");
            if (initStats == null)
            {
                // Still subscribe once: the per-run reset is lost, but the memories still work.
                WarnOnce("InitializeStatsAndActions-missing", "GameManager.InitializeStatsAndActions not found; subscribing at plugin start with no per-run reset");
                Subscribe();
                return;
            }
            harmony.Patch(initStats, postfix: new HarmonyMethod(typeof(PartnerMemoryPatch), nameof(InitStats_Postfix)));
            Logger.LogDebug("PartnerMemoryPatch applied.");
        }

        private static void WarnOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Logger.LogWarning($"[PartnerMemoryPatch] {cause}: {detail} (further occurrences suppressed)");
        }

        private static void DebugOnce(string cause, string detail)
        {
            if (Warned.Add(cause))
                Logger.LogDebug($"[PartnerMemoryPatch] {cause}: {detail}");
        }

        private static void InitStats_Postfix()
        {
            try
            {
                // Per-run reset first; the subscription below is once per process.
                _lastCatchDay = -1;
                StatCache.Clear();
                Subscribe();
            }
            catch (Exception ex)
            {
                WarnOnce("InitStats_Postfix-exception", ex.ToString());
            }
        }

        private static void Subscribe()
        {
            if (_subscribed) return; // once per process: a run boot re-fires this, the handlers must not stack
            _subscribed = true;
            GameManager.OnEncounterEnemyDefeated += OnEnemyDefeated;
            GameManager.OnEncounterPlayerEscaped += OnPlayerEscaped;
            GameManager.OnCardSpawned += OnCardSpawned;
            GameManager.OnActionPerformed += OnActionPerformed;
            Logger.LogDebug("[PartnerMemoryPatch] subscribed to the encounter, spawn and action events.");
        }

        /// <summary>Plugin.OnDestroy: drop the handlers so a re-loaded plugin does not double them.</summary>
        internal static void Unsubscribe()
        {
            if (!_subscribed) return;
            GameManager.OnEncounterEnemyDefeated -= OnEnemyDefeated;
            GameManager.OnEncounterPlayerEscaped -= OnPlayerEscaped;
            GameManager.OnCardSpawned -= OnCardSpawned;
            GameManager.OnActionPerformed -= OnActionPerformed;
            _subscribed = false;
        }

        private static void OnEnemyDefeated(Encounter _Encounter)
        {
            try { Remember(FightStatUid, "fight"); }
            catch (Exception ex) { WarnOnce("OnEnemyDefeated-exception", ex.ToString()); }
        }

        private static void OnPlayerEscaped(Encounter _Encounter)
        {
            try { Remember(EscapeStatUid, "escape"); }
            catch (Exception ex) { WarnOnce("OnPlayerEscaped-exception", ex.ToString()); }
        }

        private static void OnCardSpawned(InGameCardBase _Card)
        {
            try
            {
                if (_Card == null || _Card.CardModel == null) return;
                if (!CatchCardUids.Contains(_Card.CardModel.UniqueID)) return;
                var gm = MBSingleton<GameManager>.Instance;
                if (gm == null) { WarnOnce("catch-no-gamemanager", "GameManager.Instance is null when a fish spawned; no catch memory"); return; }
                int day = gm.CurrentDay;
                if (day == _lastCatchDay) return; // first catch of the day only
                if (Remember(CatchStatUid, "catch")) _lastCatchDay = day;
            }
            catch (Exception ex) { WarnOnce("OnCardSpawned-exception", ex.ToString()); }
        }

        private static void OnActionPerformed(ActionReport _Report)
        {
            try
            {
                if (!IsFinishedStructure(_Report)) return;
                Remember(BuildStatUid, "build");
            }
            catch (Exception ex) { WarnOnce("OnActionPerformed-exception", ex.ToString()); }
        }

        /// <summary>Plan R4: a finished build is a not-cancelled IsBpAction whose first produced
        /// collection is the last-stage "&lt;blueprint name&gt;_blueprint" one, and the structure is a
        /// Base or Location card in the blueprint's result.</summary>
        private static bool IsFinishedStructure(ActionReport report)
        {
            if (report.Cancelled) return false;
            var action = report.Action;
            if (action == null || !action.IsBpAction) return false;
            var bp = report.FromCard;
            if (bp == null) return false;
            var produced = action.ProducedCards;
            if (produced == null || produced.Length == 0 || produced[0] == null) return false;
            if (produced[0].CollectionName != bp.name + "_blueprint") return false;
            var result = bp.BlueprintResult;
            if (result == null || result.Length == 0)
            {
                DebugOnce("bp-no-result", $"finished blueprint '{bp.name}' has no BlueprintResult; no build memory");
                return false;
            }
            foreach (var drop in result)
            {
                var card = drop.DroppedCard;
                if (card != null && (card.CardType == CardTypes.Base || card.CardType == CardTypes.Location)) return true;
            }
            return false;
        }

        /// <summary>The live Partner when its card is on the player's board, else null.</summary>
        private static InGameNPC FindPartnerOnPlayerBoard(GameManager gm)
        {
            var all = gm.AllNPCs;
            if (all == null) { WarnOnce("AllNPCs-null", "GameManager.AllNPCs is null; no memory written"); return null; }
            bool anyPartner = false;
            foreach (var npc in all)
            {
                if (npc == null || npc.NPCModel == null || npc.NPCModel.name != PartnerModelName) continue;
                anyPartner = true;
                var card = npc.AssociatedCard;
                if (card == null) { DebugOnce("partner-no-card", "the Partner has no AssociatedCard; treated as not on the board"); continue; }
                if (card.CardEnvironment.MatchesPlayerEnv) return npc;
            }
            if (!anyPartner) DebugOnce("no-partner", "no Agent_Partner in this run; memories are not written");
            return null;
        }

        private static NPCStat ResolveStat(string uid)
        {
            if (StatCache.TryGetValue(uid, out var cached) && cached != null) return cached;
            var stat = UniqueIDScriptable.GetFromID<NPCStat>(uid);
            if (stat == null)
            {
                WarnOnce("stat-missing-" + uid, $"NPCStat '{uid}' did not resolve (CSFFModFramework absent, or NPCStat JSON not loaded); no memory written");
                return null;
            }
            StatCache[uid] = stat;
            return stat;
        }

        /// <summary>Raises one memory on the Partner. True when the write was started.</summary>
        private static bool Remember(string statUid, string kind)
        {
            var gm = MBSingleton<GameManager>.Instance;
            if (gm == null) { WarnOnce("no-gamemanager-" + kind, $"GameManager.Instance is null; no {kind} memory"); return false; }
            var partner = FindPartnerOnPlayerBoard(gm);
            if (partner == null) return false;
            var stat = ResolveStat(statUid);
            if (stat == null) return false;
            if (!partner.HasStat(stat))
            {
                WarnOnce("stat-not-on-partner-" + statUid, $"the Partner has no '{statUid}' (GameSourceModify/Agent_Partner.json not applied?); no {kind} memory");
                return false;
            }
            float before = partner.GetStatValue(stat);
            var mod = new NPCStatInstantModifier(stat, MemoryStart, null);
            // IEnumerator: start it, never call it bare.
            gm.StartCoroutine(partner.ChangeStat(mod, null, Source, -1));
            Logger.LogInfo($"[PartnerMemory] {kind} memory raised on the Partner ({before:0} -> {MemoryStart:0}).");
            return true;
        }
    }
}
