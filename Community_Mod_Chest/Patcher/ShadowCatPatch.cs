using System;
using System.Collections;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Shadow the Cat (Documentation/Design/Village_Master_As_Built.md §3.6/§10.7) — a second,
    /// wholly independent companion-cat chain modeled on LostCatPatch.cs. Shares zero
    /// UIDs/files/state with Ash (cmcStrayCat/cmcInnCat/cmcStatLostCat) — Ash is untouchable
    /// (see memory: user_cat_ashton).
    ///
    /// <para>The spawn gate is AcademyCourseService.HasCourse(GradHerbalism), which returns true
    /// forever once earned (perks never un-grant), so the "Shadow already exists" guard is the
    /// ONLY thing stopping a repeat spawn every 2-second tick.</para>
    ///
    /// <para><b>Why the guard is a persistent stat, not a board scan (1.69.11, T2.1453).</b>
    /// GameManager.AllCards holds the CURRENT board plus carried and IndependentFromEnv cards only;
    /// every other environment's cards live in GameManager.EnvironmentsData (root CLAUDE.md
    /// "An AllCards UID scan is NOT 'the cards in this room'"). The guard used to be an AllCards scan,
    /// so a tamed Shadow set down in another environment was invisible to it and a second, wild Shadow
    /// spawned in the Foraging Forest. Now the hidden GameStat cmcStatShadowCatPresent (clamped 0..1)
    /// carries it: this patch sets it to 1 the moment a Shadow is confirmed on the board, the wild
    /// card's Play Music taming sets it again (JSON), and each of the tamed card's three "Slips Into
    /// the Dark" OnZero destroys clears it (JSON), so a cat lost to neglect can turn up again. Once the
    /// Apothecary's Cabin stands on the forest board nothing here runs, so a Shadow consumed by it never
    /// returns.</para>
    ///
    /// <para><b>Full scan, once per forest visit per run.</b> The first qualifying tick of each forest
    /// visit (and again whenever the latch drops from 1 to 0 during the visit) scans every Shadow form
    /// (wild, tamed, hunt trail) in AllCards and in every other environment's saved board, nested
    /// inventories included. With the latch at 0 a find back-fills it (a save made before the stat
    /// existed). With the latch at 1 and a COMPLETE scan finding nothing, the latch is cleared with one
    /// Info line (self-heal: Shadow left the game by a route that is not one of the three OnZero
    /// destroys, so without this the cabin's last stage could never be finished). An incomplete scan
    /// (AllCards or EnvironmentsData unreadable) never clears the latch. The "once per visit" state is
    /// keyed on the GameManager instance, so a save reload (a new run, root CLAUDE.md "Per-Run Event
    /// Registration") always rescans.</para>
    ///
    /// <para>The old Gate 3 ("cabin blueprint on board and no tamed Shadow = final stage under way",
    /// latching a session-static flag) is gone: it also fired when the cabin blueprint was placed
    /// BEFORE Shadow was ever tamed, and then Shadow never appeared, so the cabin's last stage, which
    /// needs him, could never be finished. The session-static cabin flag is gone too: it leaked from one
    /// save into another loaded in the same process.</para>
    /// </summary>
    internal static class ShadowCatPatch
    {
        private const string UntamedUid = "cmcShadowCat";
        private const string TamedUid = "cmcShadowCatTamed";
        private const string ApothecaryCabinUid = "cmcApothecaryCabin";
        internal const string PresentStatUid = "cmcStatShadowCatPresent";

        // Same forest as Ash (cmcEnvForagingForest) — deliberate design tension, flagged in
        // the master plan §9.7: differentiate narratively (nocturnal/pitch-black) rather than
        // by hour window, since both prowl dusk-to-dawn.
        private const string TargetEnvUid = "cmcEnvForagingForest";
        private const float ProwlStartHour = 17f; // dusk
        private const float ProwlEndHour = 6f;    // dawn

        private static bool _initialized;

        // Per-run, per-visit scan state. _runGm is the GameManager the state belongs to: a save reload
        // builds a new GameManager, so the state resets with the run even though gm is null at the menu.
        private static object _runGm;
        private static bool _visitScanDone;
        private static float _lastPresent = -1f;

        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning("[ShadowCatPatch] " + message);
        }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(2f, CheckAndSpawn, "ShadowCatProwlCheck");
            Plugin.Logger.LogDebug("[ShadowCatPatch] initialized.");
        }

        private static void CheckAndSpawn()
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                if (!ReferenceEquals(gm, _runGm))
                {
                    _runGm = gm;
                    _visitScanDone = false;
                    _lastPresent = -1f;
                }

                // Environment first, so leaving the forest at any hour re-arms the full scan.
                if (!string.Equals(GameQuery.CurrentEnvironmentUniqueId, TargetEnvUid, StringComparison.Ordinal))
                {
                    _visitScanDone = false;
                    return;
                }

                var hour = GameQuery.HourOfDay;
                if (hour < ProwlStartHour && hour >= ProwlEndHour) return;

                if (!AcademyCourseService.HasCourse(AcademyCourseService.GradHerbalism)) return;

                // Gate 1: the persistent "a Shadow exists in this run" latch.
                float present = HiddenStat.Get(PresentStatUid);
                if (present < 0f)
                {
                    WarnOnce("present-unreadable", $"hidden stat '{PresentStatUid}' is not readable, so Shadow is not spawned (is GameStat/CMC_ShadowCatPresent.json deployed?).");
                    return;
                }
                // The latch dropped during this visit (he slipped into the dark): allow a fresh full scan.
                if (_lastPresent >= 0.5f && present < 0.5f) _visitScanDone = false;
                _lastPresent = present;

                // Gate 2: the cabin already stands (it is built in this forest, so the board scan sees it).
                // Checked before the self-heal, so a Shadow consumed by the cabin keeps the latch at 1.
                if (CardOnBoard(gm, ApothecaryCabinUid)) return;

                // Gate 3: while tamed Shadow is out on a night hunt (round 2 E4) his card is his trail,
                // so no Shadow card exists anywhere; the hunt state stat says he is still yours.
                if (HiddenStat.Get(AshBoarHuntPatch.ShadowHuntSpotStatUid) >= 0.5f) return;

                if (present >= 0.5f)
                {
                    // Self-heal, once per visit: a latch with no Shadow anywhere would block him for good.
                    if (_visitScanDone) return;
                    _visitScanDone = true;
                    if (FindShadowAnywhere(gm, out bool complete) || !complete) return;
                    if (SetLatch(0f, "heal"))
                    {
                        _lastPresent = 0f;
                        Plugin.Logger.LogInfo("[ShadowCatPatch] No Shadow on any board, carried or saved, but his marker said he was here: marker cleared, so he can turn up in the forest again.");
                    }
                    return;
                }

                // Gate 4: back-fill for a save made before the latch existed (a full scan once per visit,
                // the cheap board scan on every other tick).
                bool found;
                if (!_visitScanDone)
                {
                    _visitScanDone = true;
                    found = FindShadowAnywhere(gm, out _);
                }
                else found = ShadowExistsAnywhere(gm, out _);
                if (found)
                {
                    SetLatch(1f, "backfill");
                    return;
                }

                // SpawnService.Spawn returns null on success (GiveCard is void this game
                // version) — verify by re-querying the board, not the return value.
                SpawnService.Spawn(UntamedUid);
                if (!ShadowExistsAnywhere(gm, out _))
                    Plugin.Logger.LogWarning("[ShadowCatPatch] Shadow cat did not appear after spawn — is cmcShadowCat loaded?");
                else if (SetLatch(1f, "spawn"))
                    Plugin.Logger.LogInfo("[ShadowCatPatch] Shadow slips out from between the pines.");
                // else: SetLatch warned once; the next tick's board scan sees him and retries the write.
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[ShadowCatPatch] CheckAndSpawn failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool SetLatch(float value, string cause)
        {
            if (HiddenStat.Set(PresentStatUid, value)) return true;
            WarnOnce("latch-" + cause, $"could not write hidden stat '{PresentStatUid}' = {value:0} ({cause}); the one-Shadow marker is not updated, so the guard falls back to the board scan.");
            return false;
        }

        private static bool IsShadowUid(string uid)
            => uid == UntamedUid || uid == TamedUid || uid == AshBoarHuntPatch.ShadowTrailCardUid;

        /// <summary>Every Shadow form anywhere: the live board (AllCards) and every other environment's
        /// saved board. <paramref name="complete"/> is false when either half could not be read.</summary>
        private static bool FindShadowAnywhere(object gm, out bool complete)
        {
            if (ShadowExistsAnywhere(gm, out bool boardOk)) { complete = boardOk; return true; }
            bool found = ShadowOnAnySavedBoard(gm, out bool savedOk);
            complete = boardOk && savedOk;
            return found;
        }

        /// <summary>The current board plus carried and IndependentFromEnv cards (GameManager.AllCards).</summary>
        private static bool ShadowExistsAnywhere(object gm, out bool readable)
        {
            readable = false;
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards)
            {
                WarnOnce("allcards", "GameManager.AllCards unreadable - the board half of the Shadow dupe-guard is blind.");
                return false;
            }
            readable = true;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                var model = Reflect.GetMember(card, "CardModel");
                if (model == null) continue;
                if (IsShadowUid(Reflect.GetMember(model, "UniqueID") as string)) return true;
            }
            return false;
        }

        private static bool CardOnBoard(object gm, string wantedUid)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return false;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                var model = Reflect.GetMember(card, "CardModel");
                if (model == null) continue;
                if (Reflect.GetMember(model, "UniqueID") as string == wantedUid) return true;
            }
            return false;
        }

        /// <summary>
        /// Any Shadow form on another environment's SAVED board (GameManager.EnvironmentsData, a
        /// Dictionary&lt;EnvDictKey, EnvironmentSaveDataByReference&gt;). Saved cards carry
        /// CardID = UniqueIDScriptable.SaveID(model), i.e. "uid(name)" (.decomp/UniqueIDScriptable.cs).
        /// The current environment's entry is skipped: it is the snapshot from the last departure,
        /// and the live board (AllCards) is the truth for it.
        /// </summary>
        private static bool ShadowOnAnySavedBoard(object gm, out bool readable)
        {
            readable = false;
            if (Reflect.GetMember(gm, "EnvironmentsData") is not IDictionary envData)
            {
                WarnOnce("envdata", "GameManager.EnvironmentsData unreadable - a Shadow set down in another environment cannot be seen, so a second one may appear, and a lost Shadow's marker is never cleared.");
                return false;
            }
            object currentKey = null;
            var currentEnv = Reflect.GetMember(gm, "CurrentEnvironment");
            if (currentEnv != null) currentKey = Reflect.GetMember(currentEnv, "DictionnaryKey");
            if (currentKey == null)
            {
                // Without the key the forest's own stale snapshot is scanned too; it can only over-find.
                WarnOnce("currentkey", "GameManager.CurrentEnvironment.DictionnaryKey unreadable - the forest's own stale saved board is scanned too.");
            }
            readable = true;

            foreach (DictionaryEntry entry in envData)
            {
                if (currentKey != null && currentKey.Equals(entry.Key)) continue;
                var save = entry.Value;
                if (save == null) continue;
                if (ListHasShadow(Reflect.GetMember(save, "GetRegularCards", "AllRegularCards"))) return true;
                if (ListHasShadow(Reflect.GetMember(save, "GetInventoryCards", "AllInventoryCards"))) return true;
                if (ListHasShadow(Reflect.GetMember(save, "NestedInventoryCards"))) return true;
            }
            return false;
        }

        private static bool ListHasShadow(object list)
        {
            if (list is not IEnumerable cards) return false;
            foreach (var saved in cards)
            {
                if (saved == null) continue;
                if (SavedIdIsShadow(Reflect.GetMember(saved, "CardID") as string)) return true;
                // Cards held inside a saved container (InventoryCardSaveDataByReference.CardsInInventoryRef[].RegularCardRef).
                if (Reflect.GetMember(saved, "CardsInInventoryRef") is IEnumerable slots)
                {
                    foreach (var slot in slots)
                    {
                        if (slot == null) continue;
                        var inner = Reflect.GetMember(slot, "RegularCardRef");
                        if (inner != null && SavedIdIsShadow(Reflect.GetMember(inner, "CardID") as string)) return true;
                    }
                }
            }
            return false;
        }

        private static bool SavedIdIsShadow(string savedId)
        {
            if (string.IsNullOrEmpty(savedId)) return false;
            int paren = savedId.IndexOf('(');
            return IsShadowUid(paren >= 0 ? savedId.Substring(0, paren) : savedId);
        }
    }
}
