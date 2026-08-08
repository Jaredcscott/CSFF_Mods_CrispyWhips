using System;
using System.Collections;
using System.Reflection;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Academy course-gated blueprint locking.
    ///
    /// Since the village rework PR-1 (Event Timeline plan §8.7), course gating is
    /// UNCONDITIONAL: the Academy always stands in the Village, the old "Higher
    /// Education" trait is retired from character creation (asset kept for save
    /// safety, CharacterPerkPerkGroup "None"), and these advanced blueprints are
    /// taken out of normal research and handed out only by Academy courses, for
    /// every character:
    ///   - Architecture course            → WDI Water-Driven Sawmill + Grinding Mill
    ///   - Metallurgy course              → ACT Copper Sheet
    ///   - Architecture + Metallurgy      → WDI Water-Driven Forge + Workshop Kit
    ///   - Fishing course                 → CMC Iron Fishing Rod
    ///   - Armorer course                 → ACT Copper + Iron Breastplate/Helmet/Greaves/Gauntlets
    ///   - Herbalism course               → CMC Apothecary Cabin + Healing Mixture
    /// The Armorer course itself is pruned from the lectern entirely if ACT isn't
    /// installed (see AcademyPatch.PruneUnavailableCourses) — this table's "owning
    /// mod not installed" skip below is a secondary safety net.
    ///
    /// Locking uses the game's own quest-lock mechanism: at every run start
    /// (GameManager.OnGMInitialized, which fires at the end of FinishInitializing when
    /// saved blueprint states have already been replayed) the matching
    /// UnlockableCards entry gets Disabled=true and the model state is set to Hidden.
    /// An already-Available blueprint is NEVER touched — researched/earned blueprints
    /// survive mod updates and are restored by the save replay before we run.
    /// Both structures are per-GameManager-instance, so runs without the trait are
    /// unaffected.
    ///
    /// Unlocking goes through the public static GameManager.MakeBlueprintAvailable —
    /// the same routine CardAction.BlueprintsFullUnlock uses (spawns the model card,
    /// skips research, cascades AlsoUnlocks).
    ///
    /// Course completion is recorded as hidden in-run perks (CharacterPerkPerkGroup
    /// "None") granted natively by the Study actions' AddedInRunPerks — persisted in
    /// the save by the game itself.
    /// </summary>
    internal static class AcademyCourseService
    {
        internal const string GradArchitecture   = "cmcperkgradarchitecture";
        internal const string GradMetallurgy     = "cmcperkgradmetallurgy";
        internal const string GradHerbalism      = "cmcperkgradherbalism";
        internal const string GradFishing        = "cmcperkgradfishing";
        internal const string GradArmorer        = "cmcperkgradarmorer";
        internal const string GradMedicine       = "cmcperkgradmedicine";

        // BlueprintModelState enum: 0=Available, 1=Purchasable, 2=Locked, 3=Hidden
        private const int StateAvailable = 0;
        private const int StateHidden    = 3;

        private sealed class CourseUnlock
        {
            public string   Label;
            public string[] RequiredPerks;
            public string[] Blueprints;
        }

        private static readonly CourseUnlock[] CourseUnlocks =
        {
            new CourseUnlock
            {
                Label         = "Architecture",
                RequiredPerks = new[] { GradArchitecture },
                Blueprints    = new[] { "water_sawmill_bp_water_driven_sawmill", "water_sawmill_bp_grinding_mill" }
            },
            new CourseUnlock
            {
                Label         = "Metallurgy",
                RequiredPerks = new[] { GradMetallurgy },
                Blueprints    = new[] { "advanced_copper_tools_bp_metal_sheet" }
            },
            new CourseUnlock
            {
                Label         = "Architecture+Metallurgy",
                RequiredPerks = new[] { GradArchitecture, GradMetallurgy },
                Blueprints    = new[] { "water_sawmill_bp_water_driven_forge", "water_sawmill_bp_water_driven_workshop_kit" }
            },
            new CourseUnlock
            {
                Label         = "Fishing",
                RequiredPerks = new[] { GradFishing },
                Blueprints    = new[] { "BpCMCIronFishingRod" }
            },
            new CourseUnlock
            {
                Label         = "Armorer",
                RequiredPerks = new[] { GradArmorer },
                Blueprints    = new[]
                {
                    "advanced_copper_tools_bp_copper_breastplate",
                    "advanced_copper_tools_bp_copper_helmet",
                    "advanced_copper_tools_bp_copper_greaves",
                    "advanced_copper_tools_bp_copper_gauntlets",
                    "act_bp_iron_breastplate",
                    "act_bp_iron_helmet",
                    "act_bp_iron_greaves",
                    "act_bp_iron_gauntlets"
                }
            }
        };

        // Historically the "unconditional" table (vs. the trait-gated CourseUnlocks above); since
        // PR-1 retired the Higher Education trait BOTH tables apply to every run — kept separate
        // only because this one predates the merge. Blueprint is Hidden until the named graduate
        // perk is earned at the Academy, then handed out via MakeBlueprintAvailable.
        // The Apothecary's Cabin is the first consumer (Village Apothecary arc, Phase 1).
        private static readonly CourseUnlock[] UnconditionalCourseUnlocks =
        {
            new CourseUnlock
            {
                Label         = "Herbalism (Apothecary)",
                RequiredPerks = new[] { GradHerbalism },
                Blueprints    = new[] { "BpCMCApothecaryCabin", "bpcmcapothecaryhealingmixture" }
            }
        };

        // bpcmcapothecaryhealingmixture's recipe requires Herbs & Fungi ingredients (rare herbs
        // incl. hemp) by explicit owner request — there is no CMC-only substitute, unlike every
        // other soft H&F touchpoint in this mod (see project_soft_dep_doctrine). Rather than leave
        // the blueprint in an ambiguous state on a CMC-only install (ingredient WarpData that fails
        // to resolve could drop out of the required-elements list entirely, per the vanilla Cabin's
        // own null-element behavior, silently trivializing the recipe — or leave it permanently
        // uncraftable with no explanation), hide it outright whenever H&F isn't installed,
        // regardless of the Herbalism perk. herbs_fungi_hemp_flower_dried is used purely as an
        // "is H&F installed" presence probe.
        private const string HfPresenceProbeUid = "herbs_fungi_hemp_flower_dried";
        private const string HfGatedBlueprint    = "bpcmcapothecaryhealingmixture";

        private static bool _subscribed;
        private static Action _gmInitializedHandler;
        private static MethodInfo _getFromIdCardData;
        private static MethodInfo _makeBlueprintAvailable;
        private static FieldInfo _disabledField;
        private static bool _inRunPerkCheckFailureLogged;

        // ── Registration ─────────────────────────────────────────────────────

        public static void Initialize()
        {
            if (_subscribed) return;

            var gmType = CardUtil.FindGameType("GameManager");
            if (gmType == null)
            {
                Plugin.Logger.LogWarning("[AcademyCourseService] GameManager type not found — course gating inactive.");
                return;
            }

            var field = gmType.GetField("OnGMInitialized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(Action))
            {
                Plugin.Logger.LogWarning("[AcademyCourseService] GameManager.OnGMInitialized not found — course gating inactive.");
                return;
            }

            _gmInitializedHandler = OnRunStart;
            var current = (Action)field.GetValue(null);
            field.SetValue(null, (Action)Delegate.Combine(current, _gmInitializedHandler));
            _subscribed = true;

            Plugin.Logger.LogDebug("[AcademyCourseService] subscribed to GameManager.OnGMInitialized.");
        }

        // ── Run-start / post-course gating pass ───────────────────────────────

        private static void OnRunStart()
        {
            try
            {
                ApplyCourseGating("run start");
                ApplyUnconditionalGating("run start");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AcademyCourseService] run-start gating failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>Called by AcademyPatch after a Study action completes.</summary>
        public static void ResyncAfterCourse()
        {
            try
            {
                ApplyCourseGating("course completion");
                ApplyUnconditionalGating("course completion");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AcademyCourseService] post-course gating failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Course-gated pass for every run (PR-1 §8.7: the retired "Higher Education" trait no
        // longer conditions this table — the Academy is a permanent fixture and its courses gate
        // these blueprints for everyone).
        private static void ApplyCourseGating(string reason)
        {
            GateTable(CourseUnlocks, $"Academy course gating at {reason}");
        }

        // Unconditional pass: runs on every run regardless of the Higher Education trait — the
        // blueprints in UnconditionalCourseUnlocks are always graduate-perk-gated.
        private static void ApplyUnconditionalGating(string reason)
        {
            GateTable(UnconditionalCourseUnlocks, $"Academy gating at {reason}");
            HideIfDependencyMissing(HfGatedBlueprint, HfPresenceProbeUid, "Herbs & Fungi", reason);
        }

        // Forces a blueprint permanently Hidden when a cross-mod ingredient dependency isn't
        // installed — independent of (and layered on top of) the perk gate above. Never touches an
        // already-Available blueprint.
        private static void HideIfDependencyMissing(string bpUid, string presenceProbeUid, string depLabel, string reason)
        {
            if (FindCardData(presenceProbeUid) != null) return; // dependency installed — normal perk gate applies

            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return;

            var states = CardUtil.GetMemberValue(gm, "BlueprintModelStates") as IDictionary;
            var unlockables = CardUtil.GetMemberValue(gm, "UnlockableCards") as IList;
            if (states == null) return;

            var bp = FindCardData(bpUid);
            if (bp == null || !states.Contains(bp)) return;

            int state = Convert.ToInt32(states[bp]);
            if (state == StateAvailable) return; // never take away an available blueprint

            states[bp] = Enum.ToObject(states[bp].GetType(), StateHidden);
            DisableUnlockConditions(unlockables, bp);
            Plugin.Logger.LogInfo($"[AcademyCourseService] {depLabel} not installed at {reason} — {bpUid} hidden regardless of perk.");
        }

        // Shared gating loop: for each course in the table, unlock its blueprints if the required
        // graduate perk(s) are held, otherwise hide them and disable their normal unlock
        // conditions. An already-Available blueprint is never taken away.
        private static void GateTable(CourseUnlock[] table, string logContext)
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return;

            var states = CardUtil.GetMemberValue(gm, "BlueprintModelStates") as IDictionary;
            var unlockables = CardUtil.GetMemberValue(gm, "UnlockableCards") as IList;
            if (states == null)
            {
                Plugin.Logger.LogWarning("[AcademyCourseService] BlueprintModelStates not readable — course gating skipped.");
                return;
            }

            int locked = 0, unlocked = 0;
            foreach (var course in table)
            {
                bool earned = AllPerksHeld(gm, course.RequiredPerks);
                foreach (var bpUid in course.Blueprints)
                {
                    var bp = FindCardData(bpUid);
                    if (bp == null) continue;           // owning mod not installed
                    if (!states.Contains(bp)) continue; // not registered as a blueprint this run

                    int state = Convert.ToInt32(states[bp]);
                    if (state == StateAvailable)
                        continue; // never take away an available blueprint

                    if (earned)
                    {
                        MakeAvailable(bp);
                        unlocked++;
                        Plugin.Logger.LogDebug($"[AcademyCourseService] {course.Label} degree held — unlocked {bpUid}.");
                    }
                    else
                    {
                        states[bp] = Enum.ToObject(states[bp].GetType(), StateHidden);
                        DisableUnlockConditions(unlockables, bp);
                        locked++;
                        Plugin.Logger.LogDebug($"[AcademyCourseService] locked {bpUid} pending {course.Label} course.");
                    }
                }
            }

            if (locked > 0 || unlocked > 0)
                Plugin.Logger.LogInfo($"[AcademyCourseService] {logContext}: {locked} blueprint(s) locked, {unlocked} unlocked.");
        }

        // ── Course/perk queries ───────────────────────────────────────────────

        /// <summary>True if the given course-graduate (or any) perk is held: chosen at
        /// creation or granted in-run by a Study action.</summary>
        public static bool HasCourse(string perkUid)
        {
            if (CardUtil.IsPerkEquipped(perkUid)) return true;
            var gm = CardUtil.GetGameManagerInstance();
            return gm != null && InRunPerkHeld(gm, perkUid);
        }

        private static bool AllPerksHeld(object gm, string[] perkUids)
        {
            foreach (var uid in perkUids)
            {
                if (!CardUtil.IsPerkEquipped(uid) && !InRunPerkHeld(gm, uid))
                    return false;
            }
            return true;
        }

        private static bool InRunPerkHeld(object gm, string perkUid)
        {
            try
            {
                if (CardUtil.GetMemberValue(gm, "InRunAddedPerks") is not IEnumerable perks) return false;
                foreach (var perk in perks)
                {
                    if (perk == null) continue;
                    if (perkUid.Equals(CardUtil.GetMemberValue(perk, "UniqueID") as string, StringComparison.Ordinal))
                        return true;
                }
            }
            catch (Exception ex)
            {
                if (!_inRunPerkCheckFailureLogged)
                {
                    _inRunPerkCheckFailureLogged = true;
                    Plugin.Logger.LogWarning($"[AcademyCourseService] InRunPerkHeld reflection failed (perk-possession checks will silently report false): {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
            }
            return false;
        }

        // ── Game-side helpers (cached reflection) ─────────────────────────────

        private static object FindCardData(string uid)
        {
            try
            {
                if (_getFromIdCardData == null)
                {
                    var uidScriptableType = CardUtil.FindGameType("UniqueIDScriptable");
                    var cardDataType = CardUtil.FindGameType("CardData");
                    if (uidScriptableType == null || cardDataType == null) return null;

                    MethodInfo generic = null;
                    foreach (var m in uidScriptableType.GetMethods(BindingFlags.Static | BindingFlags.Public))
                    {
                        if (m.Name == "GetFromID" && m.IsGenericMethodDefinition)
                        {
                            generic = m;
                            break;
                        }
                    }
                    if (generic == null) return null;
                    _getFromIdCardData = generic.MakeGenericMethod(cardDataType);
                }
                return _getFromIdCardData.Invoke(null, new object[] { uid });
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AcademyCourseService] FindCardData reflection failed for uid={uid}: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return null;
            }
        }

        private static void MakeAvailable(object bp)
        {
            if (_makeBlueprintAvailable == null)
            {
                var gmType = CardUtil.FindGameType("GameManager");
                if (gmType == null) return;
                _makeBlueprintAvailable = AccessTools.Method(gmType, "MakeBlueprintAvailable");
                if (_makeBlueprintAvailable == null)
                {
                    Plugin.Logger.LogWarning("[AcademyCourseService] GameManager.MakeBlueprintAvailable not found — cannot unlock blueprints.");
                    return;
                }
            }
            _makeBlueprintAvailable.Invoke(null, new[] { bp });
        }

        private static void DisableUnlockConditions(IList unlockables, object bp)
        {
            if (unlockables == null) return;
            foreach (var entry in unlockables)
            {
                if (entry == null) continue;
                if (!ReferenceEquals(CardUtil.GetMemberValue(entry, "UnlockedCard"), bp)) continue;

                _disabledField ??= entry.GetType().GetField("Disabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _disabledField?.SetValue(entry, true);
                return;
            }
        }
    }
}
