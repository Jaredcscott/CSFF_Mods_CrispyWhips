using System;
using System.Collections;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The Inn Keeper's lost cat (Documentation/Design/Village_Master_As_Built.md §3.6, merged from the
    /// former standalone InnKeeper plan's Chat-dialog arc).
    ///
    /// State machine lives in the hidden GameStat cmcStatLostCat (GameStat/CMC_LostCat.json),
    /// written ONLY by authored JSON actions — this patch never writes it:
    ///   0 = story not heard; 1 = Inn Keeper's dialog answer armed the search (+1 via
    ///   DialogAction.StatModifications); 2 = cat befriended (+1 via the Stray Cat's feed CI);
    ///   3 = Inn Keeper thanked, reward given (+1 via the thank-you dialog line).
    ///
    /// This patch only handles the one thing JSON cannot: spawning the Stray Cat card when the
    /// player is standing in the right environment during the right hours while the stat is
    /// exactly in the "armed" band. Spawn goes through Api.SpawnService (framework-sanctioned;
    /// avoids the raw GiveCard reflection dance). Dupe-safety is a scan for either cat form,
    /// checked LAST because it is the only non-trivial gate: GameManager.AllCards (the current
    /// board plus carried cards) on every tick, and once per forest visit every other
    /// environment's saved board (GameManager.EnvironmentsData). AllCards alone missed a stray
    /// carried out of the forest and set down elsewhere before he was fed, so a second stray
    /// appeared (companions critical analysis, LostCat-DupeGuard); the saved-board read is the
    /// one ShadowCatPatch has used since 1.69.11.
    /// </summary>
    internal static class LostCatPatch
    {
        private const string QuestStatUid = "cmcStatLostCat";
        private const string StrayCatUid = "cmcStrayCat";
        private const string InnCatUid = "cmcInnCat";

        // Where and when the cat prowls — must stay consistent with the Inn Keeper's dialog text
        // (CMC_InnKeeperTalk: "seen prowling the Foraging Forest around dusk").
        private const string TargetEnvUid = "cmcEnvForagingForest";
        private const float ProwlStartHour = 17f; // dusk
        private const float ProwlEndHour = 6f;    // dawn

        private static bool _initialized;

        private static Type _gmType;

        // Per-run, per-visit saved-board scan state. _runGm is the GameManager the state belongs to: a
        // save reload builds a new GameManager, so the state resets with the run.
        private static object _runGm;
        private static bool _visitScanDone;
        private static bool _catOnSavedBoard;

        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning("[LostCatPatch] " + message);
        }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(2f, CheckAndSpawn, "LostCatProwlCheck");
            Plugin.Logger.LogDebug("[LostCatPatch] initialized.");
        }

        private static bool ResolveTypes()
        {
            if (_gmType != null) return true;
            _gmType = CardUtil.FindGameType("GameManager");
            return _gmType != null;
        }

        private static void CheckAndSpawn()
        {
            try
            {
                if (!ResolveTypes()) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                if (!ReferenceEquals(gm, _runGm))
                {
                    _runGm = gm;
                    _visitScanDone = false;
                    _catOnSavedBoard = false;
                }

                // Cheap gates first: environment (so leaving the forest at any hour re-arms the
                // saved-board scan), then hour window, then quest stat.
                if (!string.Equals(GameQuery.CurrentEnvironmentUniqueId, TargetEnvUid, StringComparison.Ordinal))
                {
                    _visitScanDone = false;
                    _catOnSavedBoard = false;
                    return;
                }

                // While the boards are being swapped neither half of the dupe guard can be trusted: the
                // forest's own cards may not be back in AllCards and the board just left may not be saved.
                if (GameQuery.IsTransitioning) return;

                var hour = GameQuery.HourOfDay;
                if (hour < ProwlStartHour && hour >= ProwlEndHour) return;

                var statValue = ReadQuestStat(gm);
                if (statValue < 0.5f || statValue >= 1.5f) return; // only the "armed" band spawns

                if (CatExistsAnywhere(gm)) return;

                // A cat set down in another environment is on that environment's saved board, which
                // AllCards does not hold. Saved boards cannot change while the player stays here, so
                // one read per forest visit is enough.
                if (!_visitScanDone)
                {
                    _visitScanDone = true;
                    _catOnSavedBoard = CatOnAnySavedBoard(gm);
                    if (_catOnSavedBoard)
                        Plugin.Logger.LogInfo("[LostCatPatch] The cat is on another board, where he was set down: no second stray appears in the forest.");
                }
                if (_catOnSavedBoard) return;

                // SpawnService.Spawn returns null on success (GiveCard is void this game
                // version) — verify by re-querying the board, not the return value.
                SpawnService.Spawn(StrayCatUid);
                if (CatExistsAnywhere(gm))
                    Plugin.Logger.LogInfo("[LostCatPatch] The stray cat slinks out of the undergrowth.");
                else
                    Plugin.Logger.LogWarning("[LostCatPatch] Stray cat did not appear after spawn — is cmcStrayCat loaded?");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[LostCatPatch] CheckAndSpawn failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Delegates to the framework's StatAccess (fleet plan section 10, W0.10 row 1 READ side)
        // instead of hand-rolling UniqueIDScriptable.GetFromID + GameManager.StatsDict here.
        private static float ReadQuestStat(object gm)
        {
            float value = StatAccess.Get(QuestStatUid);
            return float.IsNaN(value) ? -1f : value;
        }

        private static bool CatExistsAnywhere(object gm)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards)
            {
                WarnOnce("allcards", "GameManager.AllCards unreadable - the board half of the stray cat dupe-guard is blind.");
                return false;
            }
            foreach (var card in allCards)
            {
                if (card == null) continue;
                var model = Reflect.GetMember(card, "CardModel");
                if (model == null) continue;
                if (IsCatUid(Reflect.GetMember(model, "UniqueID") as string)) return true;
            }
            return false;
        }

        // Block bodies here and in SavedIdIsCat: the CMC test compiles both with the C# 5 compiler.
        private static bool IsCatUid(string uid)
        {
            return uid == StrayCatUid || uid == InnCatUid;
        }

        /// <summary>
        /// Either cat form on another environment's SAVED board (GameManager.EnvironmentsData, a
        /// Dictionary&lt;EnvDictKey, EnvironmentSaveDataByReference&gt;). Saved cards carry
        /// CardID = UniqueIDScriptable.SaveID(model), i.e. "uid(name)" (.decomp/UniqueIDScriptable.cs).
        /// The current environment's entry is skipped: it is the snapshot from the last departure,
        /// and the live board (AllCards) is the truth for it. Same read as
        /// ShadowCatPatch.ShadowOnAnySavedBoard. Unreadable data warns once and reports "not found",
        /// which is the guard this patch had before.
        /// </summary>
        private static bool CatOnAnySavedBoard(object gm)
        {
            if (Reflect.GetMember(gm, "EnvironmentsData") is not IDictionary envData)
            {
                WarnOnce("envdata", "GameManager.EnvironmentsData unreadable - a stray cat set down in another environment cannot be seen, so a second one may appear.");
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

            foreach (DictionaryEntry entry in envData)
            {
                if (currentKey != null && currentKey.Equals(entry.Key)) continue;
                var save = entry.Value;
                if (save == null) continue;
                if (ListHasCat(Reflect.GetMember(save, "GetRegularCards", "AllRegularCards"))) return true;
                if (ListHasCat(Reflect.GetMember(save, "GetInventoryCards", "AllInventoryCards"))) return true;
                if (ListHasCat(Reflect.GetMember(save, "NestedInventoryCards"))) return true;
            }
            return false;
        }

        private static bool ListHasCat(object list)
        {
            if (list is not IEnumerable cards) return false;
            foreach (var saved in cards)
            {
                if (saved == null) continue;
                if (SavedIdIsCat(Reflect.GetMember(saved, "CardID") as string)) return true;
                // Cards held inside a saved container (InventoryCardSaveDataByReference.CardsInInventoryRef[].RegularCardRef).
                if (Reflect.GetMember(saved, "CardsInInventoryRef") is IEnumerable slots)
                {
                    foreach (var slot in slots)
                    {
                        if (slot == null) continue;
                        var inner = Reflect.GetMember(slot, "RegularCardRef");
                        if (inner != null && SavedIdIsCat(Reflect.GetMember(inner, "CardID") as string)) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>True when a saved card id ("uid(name)" or a bare uid) is either cat form. Pure, so
        /// CMC tests can pin the rule.</summary>
        internal static bool SavedIdIsCat(string savedId)
        {
            if (string.IsNullOrEmpty(savedId)) return false;
            int paren = savedId.IndexOf('(');
            return IsCatUid(paren >= 0 ? savedId.Substring(0, paren) : savedId);
        }
    }
}
