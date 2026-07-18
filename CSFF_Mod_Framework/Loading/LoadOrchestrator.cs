using System.Diagnostics;
using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Injection;
using CSFFModFramework.Portal;
using CSFFModFramework.Reflection;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

internal static class LoadOrchestrator
{
    // The game does not recreate its ScriptableObject graph on new-game start —
    // the same graph persists for the session, so skipping re-runs is safe.
    private static bool _loaded = false;
    internal static bool LoadSucceeded { get; private set; }

    /// <summary>
    /// When true, run the two <see cref="DiagBlueprintResearch"/> passes around WarpResolver.
    /// Off by default — each pass walks all ScriptableObjects and is only useful when
    /// investigating a research-timer regression. Bound in Plugin.Awake.
    /// </summary>
    internal static bool EnableLoadDiagnostics = false;

    /// <summary>
    /// Snapshot of mods discovered this load. Populated after <see cref="ModDiscovery.DiscoverMods"/>
    /// returns; surfaced to third-party code through <see cref="Api.ModRegistry"/>.
    /// </summary>
    internal static IReadOnlyList<ModManifest> LoadedMods { get; private set; }
        = Array.Empty<ModManifest>();

    internal static void Execute()
    {
        if (_loaded) return;
        _loaded = true;
        LoadSucceeded = false;
        _anyRunPhaseFailed = false;

        string currentPhase = "initialization";
        try
        {
            Log.Debug("=== CSFFModFramework Loading ===");
            var totalSw = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();

            // Reset cross-service dirty tracker (per-load scope).
            FrameworkDirtyTracker.Reset();

            // 1. Discover mods
            currentPhase = "ModDiscovery";
            var mods = ModDiscovery.DiscoverMods();
            LoadedMods = mods;
            LogTiming(sw, "ModDiscovery");

            // 1a. Load free-form map caches supplied by mods (e.g. generated world-map edge lists).
            // Downstream mod logic can query Api.MapCacheRegistry instead of re-reading/parsing files.
            currentPhase = "MapCacheLoader";
            if (mods.Any(m => m.HasMapCaches))
                RunPhase(sw, "MapCacheLoader", () => MapCacheLoader.LoadAll(mods));
            else
            {
                MapCacheLoader.Clear();
                Log.Debug("[Skip] MapCacheLoader: no mod ships map cache JSON");
            }

            // 2. Init database from game resources
            currentPhase = "Database.InitFromGame";
            Database.InitFromGame();
            LogTiming(sw, "Database.InitFromGame");

            // 3. Load sprites
            RunPhase(sw, "SpriteLoader", () => SpriteLoader.LoadAll(mods), warnMs: 3000);

            // 3a. Decode GIFs and load CardData/Gif/*.json definitions. Drives the
            //     animations applied by Patching.GifAnimationPatch at runtime.
            if (mods.Any(m => m.HasGifContent))
                RunPhase(sw, "GifLoader", () => Gif.GifLoader.LoadAll(mods));
            else
                Log.Debug("[Skip] GifLoader: no mod ships CardData/Gif/ JSON");

            // 4. Load JSON data (cards, perks, etc.) — also caches JSON + UniqueIDs for downstream
            currentPhase = "JsonDataLoader";
            JsonDataLoader.LoadAll(mods);
            LogTiming(sw, "JsonDataLoader", warnMs: 3000);

            // 4b. Supersede duplicate instances created by Pikachu ModLoader/ModCore for
            //     UIDs we own (they load every ModInfo.json mod inside a ClearDict prefix,
            //     so their duplicates win GetFromID — which resets blueprint research on
            //     save load and splits reference identity). MUST run before WarpResolver
            //     so warps link against the canonical instances.
            RunPhase(sw, "ForeignInstanceReconciler", () => ForeignInstanceReconciler.ReconcileAll());

            // 4c. Extract SpawnStatDefaults blocks from already-loaded card JSON.
            //     Reads JsonDataLoader.ParsedJsonByUniqueId (in-memory, no I/O) and
            //     registers defaults with SpawnService so its GiveCard postfix can
            //     apply them to every matching spawn without per-mod C# postfixes.
            RunPhase(sw, "SpawnStatDefaultsLoader", () => SpawnStatDefaultsLoader.LoadAll());

            // 4d. Rebuild NPCAgent.AgentActions[].DroppedCards from raw parsed JSON —
            //     JsonUtility.FromJsonOverwrite leaves this null (array-of-objects nested inside
            //     another array-of-objects; see class doc). Must run before WarpResolver so the
            //     rebuilt CardDrop[] targets are plain resolved CardData refs from GameRegistry.
            RunPhase(sw, "NPCActionDroppedCardsRepair", () => NPCActionDroppedCardsRepair.RepairAll());

            // 5. Resolve ALL WarpData references (THE key fix)
            var allData = GetAllData();

            // Bulk-mark every mod-loaded object as dirty so NullReferenceCompactor
            // walks the right scope (vanilla objects only re-enter the dirty set
            // when an injection service explicitly modifies them).
            FrameworkDirtyTracker.MarkAllModObjects(allData);

            // DIAG: Snapshot vanilla blueprint research state BEFORE warp resolution
            if (EnableLoadDiagnostics)
                DiagBlueprintResearch(allData, "BEFORE WarpResolver");

            currentPhase = "WarpResolver";
            WarpResolver.ResolveAll(allData, mods);
            LogTiming(sw, "WarpResolver");

            // 5a2. Strip null entries from any UnityEngine.Object collection on every loaded
            //      ScriptableObject. Unresolved WarpData refs leave nulls in CardTag[] /
            //      CardData[] / GameStat[] / RequiredDurability[]; the game NREs when it
            //      iterates those collections (DurabilitiesAreCorrect on drag, TagIsOnBoard
            //      on day-tick). Removing null slots is always safe — a missing reference is
            //      functionally identical to no entry.
            RunPhase(sw, "NullReferenceCompactor", () => NullReferenceCompactor.CompactAll(allData));

            // 5b. Normalize PassiveEffects null fields (prevents NullRef in UpdatePassiveEffectStacks)
            RunPhase(sw, "PassiveEffectNormalizer", () => PassiveEffectNormalizer.NormalizeAll(allData, mods));

            // 5c. Normalize ProducedCards defaults and clean null entries
            RunPhase(sw, "ProducedCardService", () => ProducedCardService.ProcessAll(allData, mods));

            // 5d. Enable AlwaysUpdate ticking on mod cards (durability, spoilage, etc.)
            RunPhase(sw, "AlwaysUpdate", () => AlwaysUpdateService.EnableAll(allData, mods));

            // 5e. Inject custom smelting recipes into forge/furnace
            if (mods.Any(m => m.HasSmeltingRecipes))
                RunPhase(sw, "SmeltingRecipeInjector", () => SmeltingRecipeInjector.InjectAll(allData, mods));
            else
                Log.Debug("[Skip] SmeltingRecipeInjector: no mod ships SmeltingRecipes.json");

            // 5e2. Append declarative drops to location card DismantleAction ProducedCards.
            if (mods.Any(m => m.HasDropInjections))
                RunPhase(sw, "DropInjector", () => DropInjector.InjectAll(allData, mods));
            else
                Log.Debug("[Skip] DropInjector: no mod ships DropInjections.json");

            // 5f. Load mod-defined spawn triggers (CardData/Trigger/*.json). Runtime firing is
            //     handled by TriggerService via Plugin.Update — no SO injection needed here.
            if (mods.Any(m => m.HasTriggers))
                RunPhase(sw, "TriggerLoader", () => Triggers.TriggerLoader.LoadAll(mods));
            else
                Log.Debug("[Skip] TriggerLoader: no mod ships CardData/Trigger/ JSON");

            // 5f2. Register declarative wildlife-encounter guards (EncounterGuards/*.json).
            //      Runtime suppression flows through the framework's single StartEncounter
            //      prefix (Patching.EncounterGuardPatch → Api.EncounterGuards).
            if (mods.Any(m => m.HasEncounterGuards))
                RunPhase(sw, "EncounterGuardLoader", () => EncounterGuardLoader.LoadAll(mods));
            else
                Log.Debug("[Skip] EncounterGuardLoader: no mod ships EncounterGuards/ JSON");

            // 5g. Validate mod SelfTriggeredActions and arm the run-start activation check.
            //     No injection needed: GameManager.InitializeStatsAndActions() discovers STAs
            //     by iterating DataBase.AllData, where JsonDataLoader already registered them.
            RunPhase(sw, "StaActivationService", () => StaActivationService.ValidateAll(allData));

            // 5g2. Animals: build NPCAgent graphs from declarative Animals/*.json manifests and
            //      queue them for spawn registration (GameManager.Awake prefix appends them to
            //      WorldSettings.NPCAgents). Runs before NPCAgentActivationService so generated
            //      agents get its validation/normalization pass. Design:
            //      Documentation/Design/Animal_System_Plan.md.
            if (mods.Any(m => m.HasAnimals))
                RunPhase(sw, "AnimalService", () => Animals.AnimalService.LoadAll(mods));
            else
                Log.Debug("[Skip] AnimalService: no mod ships Animals/*.json");

            // 5h. Phase 3: Validate mod NPCAgents at load time, then inject any that GameManager
            //     didn't auto-discover into its agent list at OnGMInitialized. NPCDuty/NPCStat/
            //     NPCHidingGroup are loaded into AllData by JsonDataLoader and resolve via WarpData.
            //     A verbose [DIAGNOSTICS] survey of GameManager NPC fields is available at Debug
            //     level (visible with VerboseLogging=true in the BepInEx config).
            RunPhase(sw, "NPCAgentActivationService", () => NPCAgentActivationService.ValidateAll(allData));

            // 5j. HubPortals.json schema removed in v2.8.0 — portal travel handled via portable
            //     Portal Kit + PortalService (Phase 5k). Phase slot retained for ordering.
            Log.Debug("[Skip] HubPortalInjector: WorldMap/HubPortals.json schema retired in v2.8.0");

            // 5k. Portal Hub System (the ONE supported portal path — the shared, build-anywhere
            //     Portal Kit/Hub; see PortalService's class doc, 2026-07-02):
            //     MapModLoader: parse MapMod.json from each mod; populate PortalRegistry
            //                  with mod world names and EnvironmentUID/SacredSiteUID targets.
            //     PortalService.InjectHubTravelDAs runs at phase 5i-b below (one travel DA per
            //     mod world on the shared csffmfwportalplaced card).
            //     ActionRouter handlers + hub_exit injection run at OnGMInitialized
            //     (WorldMapInjector.OnGMInitialized → PortalService.RegisterHubTravelHandlers /
            //      InjectExitCardsIntoModHubs), after clone envs exist.
            if (mods.Any(m => m.HasMapMod))
                RunPhase(sw, "MapModLoader", () => MapModLoader.LoadAll(mods));
            else
                Log.Debug("[Skip] MapModLoader: no mod ships MapMod.json");

            // 5i. Phase 4: Prepare mod WorldMap/MapNodes.json entries. WorldMapData is a static
            //     plain ScriptableObject (not UniqueIDScriptable) that is NOT loaded into memory
            //     at this point (verified EA 0.64f — Resources.FindObjectsOfTypeAll(WorldMapData)
            //     returns empty during LoadMainGameData; it loads later, around GameManager init).
            //     So PrepareAll only does the load-time work here — cloning env/location pairs into
            //     AllData and recording Api.WorldMap travel edges (both consumed by WDI's own
            //     LoadMainGameData postfix) — and defers the actual WorldMapData.Environments[]
            //     node injection to GameManager.OnGMInitialized via WorldMapInjector.InjectIntoWorldMap.
            if (mods.Any(m => m.HasWorldMapNodes))
            {
                RunPhase(sw, "WorldMapInjector", () =>
                {
                    var mapNodes = WorldMapLoader.LoadAll(mods);
                    var fullMap  = mods.Any(m => m.HasFullMap) ? WorldMapLoader.LoadFullMap(mods) : null;
                    if (mapNodes.Count > 0 || fullMap != null)
                        WorldMapInjector.PrepareAll(mapNodes, fullMap);
                });
            }
            else
                Log.Debug("[Skip] WorldMapInjector: no mod ships WorldMap/MapNodes.json or WorldMap/FullMap.json");

            // 5i-a1. Re-resolve mod-card *WarpData references that point at a clone node's env/location
            //        UID. Clone CardData (e.g. cmcLocVillage) is registered by WorldMapInjector.PrepareAll
            //        (5i) AFTER WarpResolver (phase 5), so a construction-blueprint gate like
            //        CardsOnBoard[].CardWarpData: "cmcLocVillage" resolved to null on the main pass — the
            //        unlock objective's Card stayed null, the "Have :" tooltip showed no card, and the gate
            //        could never complete, so the blueprint never unlocked even at the cloned env (CMC
            //        Miller's/Weaver's Cottage + Village Hall). This idempotent re-walk fills those deferred
            //        refs now that the clones exist. Must run after WorldMapInjector (5i).
            if (mods.Any(m => m.HasWorldMapNodes))
                RunPhase(sw, "WorldMapInjector.ResolveDeferredCloneRefs", WorldMapInjector.ResolveDeferredCloneRefs);

            // 5i-a2. Append a CT10 improvement to a target CT8's EnvironmentImprovements array —
            //        declarative alternative to per-mod C# (e.g. CMC's RiverBridgeImprovementPatch).
            //        MUST run after WorldMapInjector.PrepareAll (5i): a TargetEnvUID may be a mod
            //        map node's clone CT8 (e.g. CMC's cmcLocVillage well), which only enters the
            //        registry when PrepareAll clones the env/location pair. Vanilla CT8 targets
            //        are order-insensitive.
            if (mods.Any(m => m.HasImprovementInjections))
                RunPhase(sw, "ImprovementInjector", () => ImprovementInjector.InjectAll(allData, mods));
            else
                Log.Debug("[Skip] ImprovementInjector: no mod ships InjectImprovementInto.json");

            // 5i-b. Inject travel DismantleActions into the shared placed Portal Hub CT2 so
            //       each registered mod world gets its own "Travel to X" button. Runs after
            //       MapModLoader (5k) populated PortalRegistry with world names and targets —
            //       the ONE supported portal mechanism (see PortalService's class doc).
            if (mods.Any(m => m.HasMapMod))
                RunPhase(sw, "PortalService.InjectHubTravelDAs", () => PortalService.InjectHubTravelDAs());

            // 6. Build DataMap before GameSourceModify — only needed when a mod uses
            //    MatchTagWarpData / MatchTypeWarpData bulk patches.
            if (mods.Any(m => m.HasGSMTagOrTypeMatch))
                RunPhase(sw, "DataMap (pre-GameSourceModifier)", () => DataMap.BuildMaps(allData));
            else
                Log.Debug("[Skip] DataMap (pre-GameSourceModifier): no mod uses MatchTagWarpData/MatchTypeWarpData");

            // 7. Apply GameSourceModify patches
            RunPhase(sw, "GameSourceModifier", () => GameSourceModifier.ApplyAll(mods, allData));

            // 8. Rebuild maps so later services see tag/type changes made by patches.
            RunPhase(sw, "DataMap (post-GameSourceModifier)", () => DataMap.BuildMaps(allData));

            // 8a. Resolve sprites for cards and perks
            RunPhase(sw, "SpriteResolver", () => SpriteResolver.ResolveAll(allData, mods));

            // 9. Load localization (framework-owned CSVs load first inside LocalizationLoader)
            if (LocalizationLoader.HasFrameworkLocalization || mods.Any(m => m.HasLocalization))
                RunPhase(sw, "LocalizationLoader", () => LocalizationLoader.LoadAll(mods));
            else
                Log.Debug("[Skip] LocalizationLoader: no localization CSV found (framework or mods)");

            // 10. Load audio clips from mod directories
            if (mods.Any(m => m.HasAudio))
                RunPhase(sw, "AudioLoader", () => AudioLoader.LoadAll(mods));
            else
                Log.Debug("[Skip] AudioLoader: no mod ships Resource/Audio/ content");

            // 11. Load asset bundles from mod directories
            if (mods.Any(m => m.HasAssetBundles))
                RunPhase(sw, "AssetBundleLoader", () => AssetBundleLoader.LoadAll(mods));
            else
                Log.Debug("[Skip] AssetBundleLoader: no mod ships Resource/*.ab");

            // 11b. Inject perks into PerkGroups (Situational tab by default)
            // 11d. Clear hardcoded OverrideEnvironment on mod perks
            RunPhase(sw, "PerkInjector", () =>
            {
                PerkInjector.InjectAll(allData, mods);
                PerkRelocationService.ClearOverrideEnvironments(allData, mods);
            });

            // 11b2. Phase 5: attach mod QuestLogs to PlayerCharacter quest lists (Quests.json)
            //       and mod PlayerCharacters to the Gamemode select rosters (Characters.json).
            //       Both run after WarpResolver (quest/character cross-refs resolved) and
            //       before the post-injection compactor (Collections.Append dirty-marks
            //       the mutated vanilla SOs).
            if (mods.Any(m => m.HasQuestManifest))
                RunPhase(sw, "QuestInjector", () => Injection.QuestInjector.InjectAll(allData, mods));
            else
                Log.Debug("[Skip] QuestInjector: no mod ships Quests.json");

            if (mods.Any(m => m.HasCharacterManifest))
                RunPhase(sw, "CharacterRosterInjector", () => Injection.CharacterRosterInjector.InjectAll(allData, mods));
            else
                Log.Debug("[Skip] CharacterRosterInjector: no mod ships Characters.json");

            // 11e. Second NullReferenceCompactor pass — picks up vanilla objects that
            //      GameSourceModifier / SmeltingRecipeInjector / PerkInjector mutated
            //      after the first compaction at step 5a2. allowFullSweep:false makes this
            //      a no-op when nothing was touched (avoids paying the full sweep twice).
            RunPhase(sw, "NullReferenceCompactor (post-injection)",
                () => NullReferenceCompactor.CompactAll(allData, allowFullSweep: false));

            // 11c. Queue blueprint tab injection (tabs may not exist yet during LoadMainGameData)
            if (mods.Any(m => m.HasBlueprintTabs))
                RunPhase(sw, "BlueprintInjector", () => BlueprintInjector.InjectAll(allData, mods));
            else
                Log.Debug("[Skip] BlueprintInjector: no mod ships BlueprintTabs.json");

            // 12. BlueprintPurchasing/PurchasingWithTime are set by BlueprintFlagFix in GameManager.Awake postfix.

            // 12a. Re-spawn contained blueprints on placed instances whose CardsInInventory
            //      didn't get populated during save load (template's ContainedBlueprintCards
            //      was empty at restore time because WarpResolver hadn't run yet). Symptom:
            //      placed Oil Press / similar blueprint containers lose their Recipes tab
            //      after save/exit/reload. No-op on first launch (no save loaded). Defers
            //      one frame via coroutine so any in-flight Init coroutines complete first.
            RunPhase(sw, "BlueprintContainerSaveLoadFix", () => BlueprintContainerSaveLoadFix.Schedule());

            // DIAG: Snapshot vanilla blueprint research state AFTER all processing
            if (EnableLoadDiagnostics)
                DiagBlueprintResearch(allData, "AFTER all framework loading");

            // Flush any background sprite-cache writes scheduled during SpriteLoader
            // before we declare the load complete — the next launch must see them.
            RunPhase(sw, "SpriteTextureCache.AwaitPendingWrites", () => SpriteTextureCache.AwaitPendingWrites());

            totalSw.Stop();
            var totalMs = totalSw.ElapsedMilliseconds;
            Log.Info($"=== CSFFModFramework Loading Complete ({totalMs}ms) ===");
            // Perf baseline: warm cache target ~3500ms, cold (first run) target ~11700ms.
            // Warn when significantly over baseline so regressions surface in LogOutput.log.
            if (totalMs > 15000)
                Log.Warn($"[Perf] Load time {totalMs}ms exceeds 15s regression threshold (baseline ~3.5s warm / ~11.7s cold). Check [Timing] lines above for the slow phase.");

            if (_anyRunPhaseFailed)
                Log.Error("Framework: one or more phases failed — LoadSucceeded remains false.");
            else
            {
                LoadSucceeded = true;
                Api.FrameworkEvents.RaiseLoaded();
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Framework loading failed in phase '{currentPhase}': {ex}");
        }
    }

    private static void LogTiming(Stopwatch sw, string phase, int warnMs = 500)
    {
        var ms = sw.ElapsedMilliseconds;
        if (ms >= warnMs)
            Log.Warn($"[Timing SLOW] {phase}: {ms}ms");
        else
            Log.Debug($"[Timing] {phase}: {ms}ms");
        sw.Restart();
    }

    private static bool _anyRunPhaseFailed;

    private static void RunPhase(Stopwatch sw, string name, Action phase, int warnMs = 500)
    {
        try { phase(); }
        catch (Exception ex)
        {
            Log.Error($"Phase '{name}' failed: {ex}");
            _anyRunPhaseFailed = true;
        }
        LogTiming(sw, name, warnMs);
    }

    /// <summary>
    /// Diagnostic: log BlueprintUnlockTicksCost on a few well-known vanilla blueprints
    /// to detect if something is zeroing research timers.
    /// </summary>
    private static void DiagBlueprintResearch(IEnumerable allData, string phase)
    {
        try
        {
            // Well-known vanilla blueprint UniqueIDs to check
            var watchList = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Hand Drill (vanilla)
                { "f19444dbb12acf4419fd8be1ecdec49e", "HandDrill" },
                // Wooden Flute (vanilla)
                { "5bbd19e51a8e1b14f978edd22e44dcc2", "WoodenFlute" },
            };

            var ticksField = AccessTools.Field(typeof(CardData), "BlueprintUnlockTicksCost");
            var cardTypeField = AccessTools.Field(typeof(CardData), "CardType");

            int totalBlueprints = 0;
            int zeroCostBlueprints = 0;

            foreach (var item in allData)
            {
                if (item is not CardData card) continue;

                // Check CardType == 7 (Blueprint)
                var ct = cardTypeField?.GetValue(card);
                int cardTypeVal = -1;
                if (ct != null)
                {
                    try { cardTypeVal = (int)(CardTypes)ct; } catch { }
                }
                if (cardTypeVal != 7) continue;

                totalBlueprints++;
                float ticks = 0;
                if (ticksField != null)
                    ticks = (float)ticksField.GetValue(card);

                if (ticks <= 0) zeroCostBlueprints++;

                if (watchList.TryGetValue(card.UniqueID, out var name))
                {
                    Log.Debug($"[BlueprintDiag] {phase}: {name} ({card.UniqueID}) BlueprintUnlockTicksCost={ticks}");
                }
            }

            Log.Debug($"[BlueprintDiag] {phase}: {totalBlueprints} total blueprints, {zeroCostBlueprints} with zero/negative cost");
        }
        catch (Exception ex)
        {
            Log.Warn($"[BlueprintDiag] {phase}: error: {Log.ExceptionText(ex)}");
        }
    }

    internal static IEnumerable GetAllData() =>
        GameRegistry.AllData ?? (IEnumerable)Array.Empty<object>();
}
