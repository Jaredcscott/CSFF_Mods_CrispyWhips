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
    ///   - Architecture course            → WDI Water-Driven Sawmill + Grinding Mill
    ///   - Architecture + Metallurgy      → WDI Water-Driven Forge + Workshop Kit
    ///   - Fishing course                 → CMC Iron Fishing Rod
    ///   - Armorer course                 → every ACT armor blueprint: Copper, Tin, Ghost Bronze, Tin Bronze,
    ///                                      White Bronze and Iron Breastplate/Helmet/Greaves/Gauntlets, Copper, Iron and Ghost Bronze Shields
    ///   - Herbalism course               → CMC Apothecary Cabin + Healing Mixture
    /// The Armorer course itself is pruned from the lectern entirely if ACT isn't
    /// installed (see AcademyPatch.PruneUnavailableCourses) — this table's "owning
    /// mod not installed" skip below is a secondary safety net.
    ///
    /// GRANTS vs. LOCKS (CourseGrants table, added 1.68.17). A course payoff that is
    /// another mod's BASE MATERIAL is granted, never locked: it stays normally
    /// researchable, and passing the course simply hands it over early via
    /// MakeBlueprintAvailable. Only end-tier rewards (WDI water-driven buildings, ACT
    /// armor sets) use the hide-until-earned lock above.
    ///   - Metallurgy course              → ACT Copper Sheet + Bronze Sheet (GRANT, never hidden)
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
                    // ACT ships the iron breastplate as two blueprints (female and male fit). The single
                    // "act_bp_iron_breastplate" listed here until 1.69.0 matched no blueprint, so both
                    // breastplates were researchable without the degree.
                    "act_bp_iron_breastplate_f",
                    "act_bp_iron_breastplate_m",
                    "act_bp_iron_helmet",
                    "act_bp_iron_greaves",
                    "act_bp_iron_gauntlets",
                    // ACT 1.18.0 alloy armor and the Copper Shield (owner 2026-09-29: "Gate all armor"). An older
                    // ACT without these blueprints is harmless: GateTable skips a UID that resolves to nothing.
                    "act_bp_copper_shield",
                    // The Iron Shield and Ghost Bronze Shield (UIDs read from ACT's Bp_IronShield.json and
                    // Bp_GhostBronzeShield.json, Phase F4).
                    "act_bp_iron_shield",
                    "act_bp_ghostbronze_shield",
                    "act_bp_bronze_helmet",
                    "act_bp_bronze_breastplate",
                    "act_bp_bronze_gauntlets",
                    "act_bp_bronze_greaves",
                    "act_bp_tin_helmet",
                    "act_bp_tin_breastplate",
                    "act_bp_tin_gauntlets",
                    "act_bp_tin_greaves",
                    "act_bp_ghostbronze_helmet",
                    "act_bp_ghostbronze_breastplate",
                    "act_bp_ghostbronze_gauntlets",
                    "act_bp_ghostbronze_greaves",
                    "act_bp_whitebronze_helmet",
                    "act_bp_whitebronze_breastplate",
                    "act_bp_whitebronze_gauntlets",
                    "act_bp_whitebronze_greaves"
                }
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

                    MakeAvailable(bp);
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
