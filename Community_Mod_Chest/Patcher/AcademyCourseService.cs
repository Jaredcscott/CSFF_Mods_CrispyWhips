using System;
using System.Collections;
using System.Reflection;
using CSFFModFramework.Api;
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
    ///   - Architecture + Metallurgy      → WDI Water-Driven Forge + Workshop Kit
    ///   - Fishing course                 → CMC Iron Fishing Rod
    ///   - Carpentry course               → CMC furnishings and decor (CourseUnlocks "Carpentry")
    ///   - Herbalism course               → CMC Apothecary Cabin + Healing Mixture
    ///   - Medicine course                → CMC Herb Poultice, Tincture and Spring Tonic
    /// (The Sawmill, Grinding Mill and ACT armor moved to CourseGrants on 2026-10-18: see below.)
    /// The Armorer course itself is pruned from the lectern entirely if ACT isn't
    /// installed (see AcademyPatch.PruneUnavailableCourses) — this table's "owning
    /// mod not installed" skip below is a secondary safety net.
    ///
    /// GRANTS vs. LOCKS (CourseGrants table, added 1.68.17). A course payoff that is
    /// another mod's BASE MATERIAL is granted, never locked: it stays normally
    /// researchable, and passing the course simply hands it over early via
    /// MakeBlueprintAvailable. Only end-tier rewards (WDI water-driven buildings, ACT
    /// armor sets) used the hide-until-earned lock above; since owner 2026-10-18 (reversing
    /// "Gate all armor" of 2026-09-29) only the Forge and Workshop Kit still do.
    ///   - Metallurgy course              → ACT Copper Sheet + Bronze Sheet (GRANT, never hidden)
    ///   - Architecture course            → WDI Water-Driven Sawmill + Grinding Mill (GRANT, owner 2026-10-18)
    ///   - Armorer course                 → every ACT armor blueprint in CourseGrants (GRANT, owner 2026-10-18)
    /// Why: gating ACT's Copper Sheet was a lock from 2026-07-05 (opt-in behind the
    /// Higher Education trait) and unconditional for every character from 2026-07-20
    /// (Village overhaul PR-1). Copper Sheet is ACT's tier-1 material - 21 downstream
    /// ACT blueprints consume it - so hiding it read to players as CMC breaking ACT,
    /// with no in-game signal saying where the recipe went (Nexus report, 2026-09-04).
    /// Both BlueprintModelStates and UnlockableCards are rebuilt per run
    /// (GameManager.cs:232/335/2753/2788), so an affected save recovers on next load.
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
        internal const string GradCarpentry      = "cmcperkgradcarpentry";

        // ── Skill teaching (Part II row N5, session-inferred design, +3 and the cap are tunable) ───────────
        // Every study session (the final exam included) also raises the course's ONE vanilla skill by
        // TaughtPerSession, until the course has taught TaughtCap in all. The Sit In shape: Sit In adds +3 to a
        // skill through a JSON StatModification (instant, on a skill whose BaseRatePerTick is 0, so it is
        // permanent); the C# equivalent is HiddenStat.SetBase, which goes through the framework's StatAccess.Modify
        // (the same permanent ChangeStatValue). Each cap is tracked by that course's own clamped hidden stat
        // (GameStat/CMC_AcademyTaught<Course>.json, 0..TaughtCap, BaseRatePerTick 0), never inferred from the
        // skill's current value, which the player also raises by doing. The Graduate trait only moves the
        // lectern's hours (AcademyPatch.GraduateBonus), never this call, so a doubled session teaches once.
        // Every skill below has BaseRatePerTick 0 and MinMaxValue 0..150 (Insight, unused here, is 0..1000).
        internal const float TaughtPerSession = 3f;
        internal const float TaughtCap = 15f;

        internal sealed class CourseSkill
        {
            public string GradPerk;      // the course's graduate perk, the key AcademyPatch.Courses also uses
            public string SkillUid;      // vanilla Skill_* GameStat GUID
            public string SkillLabel;    // for the log
            public string TaughtStatUid; // CMC hidden counter of points taught so far
        }

        internal static readonly CourseSkill[] CourseSkills =
        {
            new CourseSkill { GradPerk = GradArchitecture, SkillUid = "d4b67ed3a92b283488db1554bb23270a", SkillLabel = "Crafting",      TaughtStatUid = "cmcStatAcademyTaughtArchitecture" },
            new CourseSkill { GradPerk = GradMetallurgy,   SkillUid = "5e59f7e4d27b87e45b9c0b6b7bfcbf64", SkillLabel = "Smithing",      TaughtStatUid = "cmcStatAcademyTaughtMetallurgy" },
            new CourseSkill { GradPerk = GradHerbalism,    SkillUid = "85559650c938ef843af92c18f5b0c6c7", SkillLabel = "Herbalism",     TaughtStatUid = "cmcStatAcademyTaughtHerbalism" },
            new CourseSkill { GradPerk = GradFishing,      SkillUid = "61a14db03419ad34ea39f82968082c06", SkillLabel = "Fishing",       TaughtStatUid = "cmcStatAcademyTaughtFishing" },
            new CourseSkill { GradPerk = GradArmorer,      SkillUid = "e347b21369b355e4d89011407ec7726b", SkillLabel = "Metalworking",  TaughtStatUid = "cmcStatAcademyTaughtArmorer" },
            // Medicine shares Herbalism with the Herbalism course (no vanilla medicine skill); each keeps its own cap.
            new CourseSkill { GradPerk = GradMedicine,     SkillUid = "85559650c938ef843af92c18f5b0c6c7", SkillLabel = "Herbalism",     TaughtStatUid = "cmcStatAcademyTaughtMedicine" },
            new CourseSkill { GradPerk = GradCarpentry,    SkillUid = "66d3ce9da7ddfca49a89e31143fe9e43", SkillLabel = "Woodworking",   TaughtStatUid = "cmcStatAcademyTaughtCarpentry" },
        };

        /// <summary>The points one session teaches: TaughtPerSession, cut to what the course may still teach and to
        /// what the skill has room for (skillMax - skillBase), never negative. Pure, so tests can pin it.</summary>
        internal static float TaughtAmount(float taughtSoFar, float skillBase, float skillMax)
        {
            float grant = Math.Min(TaughtPerSession, TaughtCap - taughtSoFar);
            grant = Math.Min(grant, skillMax - skillBase);
            return grant > 0f ? grant : 0f;
        }

        /// <summary>Teaches the course's skill once for one completed study action. Writes the skill first and the
        /// counter only after the skill reads the new value, so a failed skill write never spends the cap.</summary>
        internal static void TeachSkill(string gradPerk)
        {
            try
            {
                CourseSkill row = null;
                foreach (var cs in CourseSkills) if (cs.GradPerk == gradPerk) { row = cs; break; }
                if (row == null) return;

                float taught = HiddenStat.Get(row.TaughtStatUid);
                float skillBase = HiddenStat.GetBase(row.SkillUid);
                float skillMax = HiddenStat.GetMax(row.SkillUid);
                if (taught < 0f || float.IsNaN(skillBase) || skillMax < 0f)
                {
                    Plugin.Logger.LogDebug($"[AcademyCourseService] TeachSkill({row.SkillLabel}): counter or skill not readable yet (taught {taught}, skill {skillBase}/{skillMax}); nothing taught.");
                    return;
                }

                float grant = TaughtAmount(taught, skillBase, skillMax);
                if (grant <= 0f) return;

                if (!HiddenStat.SetBase(row.SkillUid, skillBase + grant))
                {
                    Plugin.Logger.LogWarning($"[AcademyCourseService] Could not raise {row.SkillLabel} by {grant:0.##}; the course taught nothing this session.");
                    return;
                }
                if (!HiddenStat.Set(row.TaughtStatUid, taught + grant))
                    WarnOnce("taught-counter-write:" + row.TaughtStatUid, $"[AcademyCourseService] {row.SkillLabel} was raised but its course counter could not be written, so this course's {TaughtCap:0.##}-point cap is not counting. Logged once.");
                Plugin.Logger.LogDebug($"[AcademyCourseService] Course taught {row.SkillLabel} +{grant:0.##} ({taught + grant:0.##}/{TaughtCap:0.##} from this course).");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AcademyCourseService] TeachSkill failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

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
                Label         = "Carpentry",
                RequiredPerks = new[] { GradCarpentry },
                Blueprints    = new[]
                {
                    "cmcbpoutfitwardrobe",
                    "cmcbpwickerchair",
                    "bpcmctrainingpost",
                    // Round 2 H2, H3 and J4: hunt trophies and camp furnishings (decor, no recipe consumes any
                    // of them, so the lock hides only end products; UIDs read from the shipped blueprints).
                    "cmcbpboartrophy",
                    "cmcbpstagtrophy",
                    "cmcbpclothrug",
                    "cmcbpcanvascharcoal",
                    "cmcbpcanvasochre",
                    "cmcbpcanvasbillberry",
                    "cmcbplogbench",
                    "cmcbppotpourri",
                    "cmcbpframedmap",
                    "cmcbpicesculpture",
                    "cmcbpstonecairn",
                    // Round 2 I4: curtains over a built window (decor, nothing is crafted from them).
                    "cmcbpcurtains"
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
            },
            // Medicine (N4): the course's graduate perk (Pk_GradMedicine.json,
            // CharacterPerkPerkGroup "None") already existed with no craftable payoff —
            // GradMedicine has been declared above since before this entry existed, but
            // nothing ever gated a blueprint behind it. Herb Poultice + Tincture close
            // that gap the same way the Apothecary's own Herbalism payoff does.
            new CourseUnlock
            {
                Label         = "Medicine",
                RequiredPerks = new[] { GradMedicine },
                Blueprints    = new[] { "cmcbpherbpoultice", "cmcbptincture", "cmcbpspringtonic" }
            }
        };

        // GRANT-ONLY table. Same shape as the tables above, but GrantTable never hides anything:
        // these blueprints stay in normal research for every character, and passing the course
        // simply hands the recipe over early (MakeBlueprintAvailable, skipping the research timer).
        // Use this for any payoff that is another mod's foundational material - see the GRANTS vs.
        // LOCKS note in the class summary. A course whose ONLY payoff sits here still reads as a
        // real reward in-game (instant recipe, no research cost) without removing content a player
        // installed a different mod to get.
        private static readonly CourseUnlock[] CourseGrants =
        {
            new CourseUnlock
            {
                Label         = "Metallurgy",
                RequiredPerks = new[] { GradMetallurgy },
                Blueprints    = new[] { "advanced_copper_tools_bp_metal_sheet", "act_bp_bronze_sheet" }
            },
            new CourseUnlock
            {
                Label         = "Architecture",
                RequiredPerks = new[] { GradArchitecture },
                Blueprints    = new[] { "water_sawmill_bp_water_driven_sawmill", "water_sawmill_bp_grinding_mill" }
            },
            new CourseUnlock
            {
                Label         = "Armorer",
                RequiredPerks = new[] { GradArmorer },
                Blueprints    = new[]
                {
                    // ACT's armor merge (ACT 1.18.13, owner 2026-10-22): one blueprint per piece, whatever the
                    // metal (the piece takes the metal of its sheets), on the old copper UIDs. The 23 alloy and
                    // iron blueprints this list named until then are retired: out of every tab, and their research
                    // passes to these five (AlsoUnlocks), so granting them would add nothing. An older ACT that
                    // still ships them separately gets only these five granted; the rest stay researchable as before.
                    "advanced_copper_tools_bp_copper_breastplate",
                    "advanced_copper_tools_bp_copper_helmet",
                    "advanced_copper_tools_bp_copper_greaves",
                    "advanced_copper_tools_bp_copper_gauntlets",
                    "act_bp_copper_shield"
                }
            }
        };

        // One CMC recipe still hard-requires a Herbs & Fungi ingredient: the Weaver's hemp recipe
        // (BpCMCWeaverProcessHemp) needs herbs_fungi_hemp_stalks, and there is no CMC-only substitute. On a
        // CMC-only install that ingredient WarpData fails to resolve and the element drops out of the required
        // list entirely (BlueprintElement.ValidRequirements returns false for a null RequiredCard, and the
        // construction popup then skips it, confirmed in the decompile), so the recipe would degrade to
        // "2 MetalNugget -> 120 Fibers" with no hemp. It is a STATION-CONTAINED operation blueprint
        // (StartUnlocked:true) that lives in GameManager.AllMiniBlueprints, NOT BlueprintModelStates, so a state
        // hide can't reach it: it is removed from its host station's ContainedBlueprintCards instead
        // (StripContainedBlueprintIfMissing), so the Weaver station never offers it; the station's other four
        // recipes (Rope, LargeCloth, ProcessNettle, ProcessFlax) are CMC-only and stay available.
        // The probe is the stalks themselves, the ingredient that would drop, so an H&F rename of any other
        // card cannot reopen the trivialized recipe.
        //
        // The Healing Mixture (bpcmcapothecaryhealingmixture) and the Apothecary's Cabin need no guard: since
        // CMC 1.68.3 their rare-herb slots take the vanilla GpTag_SpiritHerb group (CMC's own Silverwort Blossom
        // and Hollow Sage are in it; H&F's dried herbs join it through CMC's own GameSourceModify), so neither has an H&F
        // ingredient to drop.
        // NOTE: GetFromID is case-SENSITIVE (UniqueIDScriptable.LoadID does not normalize case), so each
        // UID below must match its JSON UniqueID verbatim: the Weaver recipe/station ship PascalCase/camelCase.
        private const string HfPresenceProbeUid  = "herbs_fungi_hemp_stalks";
        private const string HfWeaverStationUid  = "cmcCottageWeaver";
        private const string HfWeaverHempRecipe  = "BpCMCWeaverProcessHemp";

        // Round 2 J18: the Wool-Quilted Cap and Vest pad the plain pieces' cloth with Sirus23's raw wool
        // (sh_wool). Without Sirus23 that ingredient does not resolve and its slot is left null (measured
        // 2026-10-11: four requirement slots, the Wool one null; the game reads a null slot as no
        // requirement), so each recipe would turn the plain piece's cloth into a warmer garment for free. Both blueprints are therefore kept Hidden, out of the tab, whenever
        // sh_wool does not resolve; sh_wool is its own presence probe because it IS the ingredient that would
        // drop. BlueprintTabs.json has no conditional form and the framework injects tabs at UI time, so the
        // lever is a per-run BlueprintModelStates hide (the course gate's own lever): both dictionaries are
        // rebuilt every run, so installing Sirus23 later restores the two recipes on the next load. The plain
        // Quilted Cap and Quilted Vest are not touched either way, and nothing here warns (one Debug line).
        internal const string WoolProbeUid = "sh_wool";
        internal static readonly string[] WoolQuiltedBlueprints = { "cmcbpwoolquiltedcap", "cmcbpwoolquiltedvest" };
        private static bool _woolHideLogged;

        // Round 2 J15: the King of the Forest capstone (SelfTriggeredAction/CMC_KingOfTheForest.json) unlocks the
        // Forest Throne and the Forest Crown through its BlueprintsFullUnlock. The Crown is hammered from the Metal
        // Sheet; if that card does not resolve, the sheet would drop out of the Crown's requirement list and
        // the capstone would hand over a Crown made of a scrap of cloth. So, whenever the sheet is unresolved, the
        // Crown is taken out of the capstone's BlueprintsFullUnlock (the Throne still unlocks) and kept Hidden, out
        // of the tab, with one Debug line and no warning. CMC no longer depends on ACT (Fleet plan feature 2), and
        // since CSFFModFramework 2.28.0 the framework ships the Metal Sheet under this UniqueID with or without ACT,
        // so this only fires on an older framework; it is the plan's HIDE rule applied anyway.
        internal const string CrownSheetProbeUid = "advanced_copper_tools_metal_sheet";
        internal const string CrownBlueprintUid = "cmcbpcrown";
        internal const string KingCapstoneStaUid = "cmcTrgKingOfTheForest";
        private static bool _crownHideLogged;

        private static bool _subscribed;
        private static Action _gmInitializedHandler;
        private static MethodInfo _getFromIdCardData;
        // One latch: both early exits mean "GetFromID<CardData> cannot be built", and every caller
        // then no-ops course gating, so one breadcrumb names the cause (CLAUDE.md Silent Catch Blocks, G5).
        private static bool _getFromIdUnavailableWarned;
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

            // OnGMInitialized can fire before GameManager.InRunAddedPerks finishes being
            // restored from the save — confirmed 2026-08-09 (a save with all 6 Academy
            // courses genuinely graduated logged "12 locked, 0 unlocked" at run start, since
            // HasCourse/AllPerksHeld found no perks yet). That wrongly re-locks blueprints
            // whose course was already earned. A one-shot delayed recheck a few seconds
            // later re-runs the same idempotent pass once InRunAddedPerks has settled.
            _deferredRecheckHandle = TickEvents.Interval(5f, DeferredRecheck, "AcademyCourseServiceDeferredRecheck");

            Plugin.Logger.LogDebug("[AcademyCourseService] subscribed to GameManager.OnGMInitialized.");
        }

        // ── Run-start / post-course gating pass ───────────────────────────────

        private static TickEvents.IntervalHandle _deferredRecheckHandle;

        private static void OnRunStart() => RunGatingPass("run start");

        private static void DeferredRecheck()
        {
            TickEvents.Cancel(_deferredRecheckHandle);
            _deferredRecheckHandle = null;
            RunGatingPass("deferred run-start recheck");
        }

        private static void RunGatingPass(string reason)
        {
            try
            {
                ApplyCourseGating(reason);
                ApplyUnconditionalGating(reason);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AcademyCourseService] {reason} gating failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
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
            GrantTable(CourseGrants, $"Academy course grants at {reason}");
        }

        // Unconditional pass: runs on every run regardless of the Higher Education trait — the
        // blueprints in UnconditionalCourseUnlocks are always graduate-perk-gated.
        private static void ApplyUnconditionalGating(string reason)
        {
            GateTable(UnconditionalCourseUnlocks, $"Academy gating at {reason}");
            StripContainedBlueprintIfMissing(HfWeaverStationUid, HfWeaverHempRecipe, HfPresenceProbeUid, "Herbs & Fungi", reason);
            HideWoolQuiltedIfNoWool(reason);
            HideCrownIfNoCopperSheet(reason);
        }

        // J15 Crown guard (see CrownSheetProbeUid). The capstone's unlock list is CMC's own SelfTriggeredAction, so
        // trimming it touches no vanilla object; it is idempotent, and the run-start pass repeats it every run.
        private static void HideCrownIfNoCopperSheet(string reason)
        {
            if (FindCardData(CrownSheetProbeUid) != null) return; // the Metal Sheet resolves (framework 2.28.0+ or ACT): the Crown unlocks as built

            var notes = new System.Collections.Generic.List<string>();
            var crown = FindCardData(CrownBlueprintUid);
            if (crown == null) notes.Add(CrownBlueprintUid + " not loaded");

            var sta = UniqueIDScriptable.GetFromID<SelfTriggeredAction>(KingCapstoneStaUid);
            if (sta == null || sta.Actions == null) notes.Add(KingCapstoneStaUid + " not loaded, so its unlock list was not trimmed");
            else if (crown != null)
            {
                int trimmed = 0;
                foreach (var action in sta.Actions)
                {
                    var list = action?.BlueprintsFullUnlock;
                    if (list == null || Array.IndexOf(list, crown) < 0) continue;
                    var kept = new System.Collections.Generic.List<CardData>(list.Length);
                    foreach (var bp in list) if (!ReferenceEquals(bp, crown)) kept.Add(bp);
                    action.BlueprintsFullUnlock = kept.ToArray();
                    trimmed++;
                }
                if (trimmed > 0) notes.Add("removed from the capstone's unlock list");
            }

            var gm = CardUtil.GetGameManagerInstance();
            var states = gm == null ? null : CardUtil.GetMemberValue(gm, "BlueprintModelStates") as IDictionary;
            var unlockables = gm == null ? null : CardUtil.GetMemberValue(gm, "UnlockableCards") as IList;
            if (states == null) notes.Add("BlueprintModelStates not readable, so not hidden");
            else if (crown != null && states.Contains(crown))
            {
                if (Convert.ToInt32(states[crown]) == StateAvailable) notes.Add("already researched, left available");
                else
                {
                    states[crown] = Enum.ToObject(states[crown].GetType(), StateHidden);
                    DisableUnlockConditions(unlockables, crown);
                    notes.Add("hidden");
                }
            }

            if (_crownHideLogged) return;
            _crownHideLogged = true;
            Plugin.Logger.LogDebug($"[AcademyCourseService] {CrownSheetProbeUid} unresolved at {reason}: Forest Crown " + string.Join("; ", notes) + ". The Forest Throne still unlocks.");
        }

        // J18 tab guard (see WoolProbeUid). Never takes away an already-Available blueprint, like every
        // other hide here. Logs one Debug line per process, naming every blueprint it could not reach and
        // why, so a renamed member or a missing card reads as a cause rather than as "nothing to hide".
        private static void HideWoolQuiltedIfNoWool(string reason)
        {
            if (FindCardData(WoolProbeUid) != null) return; // Sirus23 installed: the normal research gate applies

            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return; // no run yet; the next pass (run start or the deferred recheck) retries

            var states = CardUtil.GetMemberValue(gm, "BlueprintModelStates") as IDictionary;
            var unlockables = CardUtil.GetMemberValue(gm, "UnlockableCards") as IList;
            if (states == null)
            {
                if (!_woolHideLogged)
                {
                    _woolHideLogged = true;
                    Plugin.Logger.LogDebug($"[AcademyCourseService] {WoolProbeUid} unresolved at {reason}, but BlueprintModelStates is not readable: wool-quilted blueprints not hidden.");
                }
                return;
            }

            int hidden = 0;
            var skipped = new System.Collections.Generic.List<string>();
            foreach (var bpUid in WoolQuiltedBlueprints)
            {
                var bp = FindCardData(bpUid);
                if (bp == null) { skipped.Add(bpUid + " (card not loaded)"); continue; }
                if (!states.Contains(bp)) { skipped.Add(bpUid + " (not a blueprint this run)"); continue; }
                if (Convert.ToInt32(states[bp]) == StateAvailable) { skipped.Add(bpUid + " (already researched)"); continue; }

                states[bp] = Enum.ToObject(states[bp].GetType(), StateHidden);
                DisableUnlockConditions(unlockables, bp);
                hidden++;
            }

            if (_woolHideLogged) return;
            _woolHideLogged = true;
            Plugin.Logger.LogDebug($"[AcademyCourseService] Sirus23 Mod Collection not installed ({WoolProbeUid} unresolved) at {reason}: {hidden} wool-quilted blueprint(s) hidden" +
                (skipped.Count > 0 ? "; not hidden: " + string.Join(", ", skipped) : "") + ".");
        }

        // Removes a station-CONTAINED operation blueprint from its host station's ContainedBlueprintCards
        // when a cross-mod ingredient dependency isn't installed, so the station never offers a recipe
        // that would trivialize once its unresolved ingredient drops out. This is the correct lever for
        // contained op blueprints (StartUnlocked:true) — they live in GameManager.AllMiniBlueprints, not
        // BlueprintModelStates, so a BlueprintModelStates hide can't touch them. CardData.
        // ContainedBlueprintCards is a public CardData[] the game reads live from the shared model
        // (.decomp/CardData.cs), so mutating it here removes the recipe from every station instance.
        // Idempotent (a UID already absent writes nothing); the station's other contained recipes are
        // left untouched.
        private static void StripContainedBlueprintIfMissing(string stationUid, string containedBpUid, string presenceProbeUid, string depLabel, string reason)
        {
            try
            {
                if (FindCardData(presenceProbeUid) != null) return; // dependency installed — leave the recipe in place

                var station = FindCardData(stationUid);
                if (station == null) return;

                var field = station.GetType().GetField("ContainedBlueprintCards",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null)
                {
                    Plugin.Logger.LogDebug($"[AcademyCourseService] {stationUid} has no ContainedBlueprintCards field — cannot strip {containedBpUid}.");
                    return;
                }
                if (field.GetValue(station) is not Array arr || arr.Length == 0) return;

                var kept = new System.Collections.Generic.List<object>(arr.Length);
                bool removed = false;
                for (int i = 0; i < arr.Length; i++)
                {
                    var el = arr.GetValue(i);
                    if (el != null && UidOf(el) == containedBpUid) { removed = true; continue; }
                    kept.Add(el);
                }
                if (!removed) return; // already stripped or never present — idempotent no-op

                var next = Array.CreateInstance(field.FieldType.GetElementType(), kept.Count);
                for (int i = 0; i < kept.Count; i++) next.SetValue(kept[i], i);
                field.SetValue(station, next);

                Plugin.Logger.LogInfo($"[AcademyCourseService] {depLabel} not installed at {reason} — {containedBpUid} removed from {stationUid}'s contained recipes.");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AcademyCourseService] StripContainedBlueprintIfMissing({stationUid},{containedBpUid}) failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Reads a UniqueIDScriptable's UniqueID by reflection (field or auto-property backing).
        private static string UidOf(object scriptable)
        {
            if (scriptable == null) return null;
            var f = scriptable.GetType().GetField("UniqueID",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(scriptable) as string;
            var p = scriptable.GetType().GetProperty("UniqueID",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return p?.GetValue(scriptable) as string;
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
                        if (!MakeAvailable(bp)) continue; // MakeAvailable warned once; not counted as unlocked
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

        // Grant-only loop: hands the course's blueprints over once the degree is held, and does
        // NOTHING otherwise. Unlike GateTable this never writes Hidden and never disables an
        // unlock condition, so the blueprint remains researchable by its own normal conditions for
        // players who skip the course. Idempotent: an already-Available blueprint is skipped.
        private static void GrantTable(CourseUnlock[] table, string logContext)
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return;

            var states = CardUtil.GetMemberValue(gm, "BlueprintModelStates") as IDictionary;
            if (states == null)
            {
                Plugin.Logger.LogWarning("[AcademyCourseService] BlueprintModelStates not readable - course grants skipped.");
                return;
            }

            int granted = 0;
            foreach (var course in table)
            {
                if (!AllPerksHeld(gm, course.RequiredPerks)) continue; // degree not earned - leave it in research

                foreach (var bpUid in course.Blueprints)
                {
                    var bp = FindCardData(bpUid);
                    if (bp == null) continue;           // owning mod not installed
                    if (!states.Contains(bp)) continue; // not registered as a blueprint this run
                    if (Convert.ToInt32(states[bp]) == StateAvailable) continue; // already researched or granted

                    if (!MakeAvailable(bp)) continue; // MakeAvailable warned once; not counted as granted
                    granted++;
                    Plugin.Logger.LogDebug($"[AcademyCourseService] {course.Label} degree held - granted {bpUid}.");
                }
            }

            if (granted > 0)
                Plugin.Logger.LogInfo($"[AcademyCourseService] {logContext}: {granted} blueprint(s) granted.");
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

        private static void WarnGetFromIdUnavailable(string cause)
        {
            if (_getFromIdUnavailableWarned) return;
            _getFromIdUnavailableWarned = true;
            Plugin.Logger.LogWarning($"[AcademyCourseService] {cause}; course gating is inactive (every course card resolves to nothing). Logged once.");
        }

        private static object FindCardData(string uid)
        {
            try
            {
                if (_getFromIdCardData == null)
                {
                    var uidScriptableType = CardUtil.FindGameType("UniqueIDScriptable");
                    var cardDataType = CardUtil.FindGameType("CardData");
                    if (uidScriptableType == null || cardDataType == null)
                    {
                        WarnGetFromIdUnavailable("UniqueIDScriptable or CardData type not found");
                        return null;
                    }

                    MethodInfo generic = null;
                    foreach (var m in uidScriptableType.GetMethods(BindingFlags.Static | BindingFlags.Public))
                    {
                        if (m.Name == "GetFromID" && m.IsGenericMethodDefinition)
                        {
                            generic = m;
                            break;
                        }
                    }
                    if (generic == null)
                    {
                        WarnGetFromIdUnavailable("no generic UniqueIDScriptable.GetFromID");
                        return null;
                    }
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

        /// <summary>Unlocks one blueprint. False (and one warning per cause) when the game method cannot be
        /// reached, so callers never count or log a blueprint as unlocked that was not.</summary>
        private static bool MakeAvailable(object bp)
        {
            if (_makeBlueprintAvailable == null)
            {
                var gmType = CardUtil.FindGameType("GameManager");
                if (gmType == null)
                {
                    WarnOnce("make-available-no-gm-type", "[AcademyCourseService] GameManager type not found - cannot unlock course blueprints. Logged once.");
                    return false;
                }
                _makeBlueprintAvailable = AccessTools.Method(gmType, "MakeBlueprintAvailable");
                if (_makeBlueprintAvailable == null)
                {
                    WarnOnce("make-available-missing", "[AcademyCourseService] GameManager.MakeBlueprintAvailable not found - cannot unlock course blueprints. Logged once.");
                    return false;
                }
            }
            _makeBlueprintAvailable.Invoke(null, new[] { bp });
            return true;
        }

        private static void DisableUnlockConditions(IList unlockables, object bp)
        {
            if (unlockables == null) return;
            foreach (var entry in unlockables)
            {
                if (entry == null) continue;
                if (!ReferenceEquals(CardUtil.GetMemberValue(entry, "UnlockedCard"), bp)) continue;

                _disabledField ??= entry.GetType().GetField("Disabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_disabledField == null)
                {
                    WarnOnce("unlock-disabled-field-missing", $"[AcademyCourseService] {entry.GetType().Name}.Disabled not found - a hidden course blueprint keeps its own research condition and can still be researched without the course. Logged once.");
                    return;
                }
                _disabledField.SetValue(entry, true);
                return;
            }
        }

        // One latch per cause (CLAUDE.md Silent Catch Blocks, G5).
        private static readonly System.Collections.Generic.HashSet<string> _warned = new(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
