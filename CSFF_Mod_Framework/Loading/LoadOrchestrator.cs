using System.Diagnostics;
using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Injection;
using CSFFModFramework.Reflection;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

internal static class LoadOrchestrator
{
    private static bool _loaded = false;

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

        try
        {
            Log.Info("=== CSFFModFramework Loading ===");
            var totalSw = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();

            // Reset cross-service dirty tracker (per-load scope).
            FrameworkDirtyTracker.Reset();

            // 1. Discover mods
            var mods = ModDiscovery.DiscoverMods();
            LoadedMods = mods;
            LogTiming(sw, "ModDiscovery");

            // 2. Init database from game resources
            Database.InitFromGame();
            LogTiming(sw, "Database.InitFromGame");

            // 3. Load sprites
            SpriteLoader.LoadAll(mods);
            LogTiming(sw, "SpriteLoader");

            // 3a. Decode GIFs and load CardData/Gif/*.json definitions. Drives the
            //     animations applied by Patching.GifAnimationPatch at runtime. No-op
            //     when no mod ships GIF content.
            Gif.GifLoader.LoadAll(mods);
            LogTiming(sw, "GifLoader");

            // 4. Load JSON data (cards, perks, etc.) — also caches JSON + UniqueIDs for downstream
            JsonDataLoader.LoadAll(mods);
            LogTiming(sw, "JsonDataLoader");

            // 5. Resolve ALL WarpData references (THE key fix)
            var allData = GetAllData();

            // Bulk-mark every mod-loaded object as dirty so NullReferenceCompactor
            // walks the right scope (vanilla objects only re-enter the dirty set
            // when an injection service explicitly modifies them).
            FrameworkDirtyTracker.MarkAllModObjects(allData);

            // DIAG: Snapshot vanilla blueprint research state BEFORE warp resolution
            if (EnableLoadDiagnostics)
                DiagBlueprintResearch(allData, "BEFORE WarpResolver");

            WarpResolver.ResolveAll(allData, mods);
            LogTiming(sw, "WarpResolver");

            // 5a2. Strip null entries from any UnityEngine.Object collection on every loaded
            //      ScriptableObject. Unresolved WarpData refs leave nulls in CardTag[] /
            //      CardData[] / GameStat[] / RequiredDurability[]; the game NREs when it
            //      iterates those collections (DurabilitiesAreCorrect on drag, TagIsOnBoard
            //      on day-tick). Removing null slots is always safe — a missing reference is
            //      functionally identical to no entry.
            NullReferenceCompactor.CompactAll(allData);
            LogTiming(sw, "NullReferenceCompactor");

            // 5b. Normalize PassiveEffects null fields (prevents NullRef in UpdatePassiveEffectStacks)
            PassiveEffectNormalizer.NormalizeAll(allData, mods);
            LogTiming(sw, "PassiveEffectNormalizer");

            // 5c. Normalize ProducedCards defaults and clean null entries
            ProducedCardService.ProcessAll(allData, mods);
            LogTiming(sw, "ProducedCardService");


            // 5d. Enable AlwaysUpdate ticking on mod cards (durability, spoilage, etc.)
            AlwaysUpdateService.EnableAll(allData, mods);
            LogTiming(sw, "AlwaysUpdate");

            // 5e. Inject custom smelting recipes into forge/furnace
            if (mods.Any(m => m.HasSmeltingRecipes))
            {
                SmeltingRecipeInjector.InjectAll(allData, mods);
                LogTiming(sw, "SmeltingRecipeInjector");
            }
            else
            {
                Log.Debug("[Skip] SmeltingRecipeInjector: no mod ships SmeltingRecipes.json");
            }

            // 6. Apply GameSourceModify patches
            GameSourceModifier.ApplyAll(mods, allData);
            LogTiming(sw, "GameSourceModifier");

            // 7. Resolve sprites for cards and perks
            SpriteResolver.ResolveAll(allData, mods);
            LogTiming(sw, "SpriteResolver");

            // 8. Build DataMap (per-type dictionaries by name and GUID)
            DataMap.BuildMaps(allData);
            LogTiming(sw, "DataMap");

            // 9. Load localization
            if (mods.Any(m => m.HasLocalization))
            {
                LocalizationLoader.LoadAll(mods);
                LogTiming(sw, "LocalizationLoader");
            }
            else
            {
                Log.Debug("[Skip] LocalizationLoader: no mod ships Localization/*.csv");
            }

            // 10. Load audio clips from mod directories
            if (mods.Any(m => m.HasAudio))
            {
                AudioLoader.LoadAll(mods);
                LogTiming(sw, "AudioLoader");
            }
            else
            {
                Log.Debug("[Skip] AudioLoader: no mod ships Resource/Audio/ content");
            }

            // 11. Load asset bundles from mod directories
            if (mods.Any(m => m.HasAssetBundles))
            {
                AssetBundleLoader.LoadAll(mods);
                LogTiming(sw, "AssetBundleLoader");
            }
            else
            {
                Log.Debug("[Skip] AssetBundleLoader: no mod ships Resource/*.ab");
            }

            // 11b. Inject perks into PerkGroups (Situational tab by default)
            PerkInjector.InjectAll(allData, mods);

            // 11d. Clear hardcoded OverrideEnvironment on mod perks
            PerkRelocationService.ClearOverrideEnvironments(allData, mods);
            LogTiming(sw, "PerkInjector");

            // 11e. Second NullReferenceCompactor pass — picks up vanilla objects that
            //      GameSourceModifier / SmeltingRecipeInjector / PerkInjector mutated
            //      after the first compaction at step 5a2. allowFullSweep:false makes this
            //      a no-op when nothing was touched (avoids paying the full sweep twice).
            NullReferenceCompactor.CompactAll(allData, allowFullSweep: false);
            LogTiming(sw, "NullReferenceCompactor (post-injection)");

            // 11c. Queue blueprint tab injection (tabs may not exist yet during LoadMainGameData)
            if (mods.Any(m => m.HasBlueprintTabs))
            {
                BlueprintInjector.InjectAll(allData, mods);
                LogTiming(sw, "BlueprintInjector");
            }
            else
            {
                Log.Debug("[Skip] BlueprintInjector: no mod ships BlueprintTabs.json");
            }

            // 12. BlueprintPurchasing/PurchasingWithTime are set by BlueprintFlagFix in GameManager.Awake postfix.

            // 12a. Re-spawn contained blueprints on placed instances whose CardsInInventory
            //      didn't get populated during save load (template's ContainedBlueprintCards
            //      was empty at restore time because WarpResolver hadn't run yet). Symptom:
            //      placed Oil Press / similar blueprint containers lose their Recipes tab
            //      after save/exit/reload. No-op on first launch (no save loaded). Defers
            //      one frame via coroutine so any in-flight Init coroutines complete first.
            BlueprintContainerSaveLoadFix.Schedule();
            LogTiming(sw, "BlueprintContainerSaveLoadFix.Schedule");

            // DIAG: Snapshot vanilla blueprint research state AFTER all processing
            if (EnableLoadDiagnostics)
                DiagBlueprintResearch(allData, "AFTER all framework loading");

            // Flush any background sprite-cache writes scheduled during SpriteLoader
            // before we declare the load complete — the next launch must see them.
            SpriteTextureCache.AwaitPendingWrites();

            totalSw.Stop();
            var totalMs = totalSw.ElapsedMilliseconds;
            Log.Info($"=== CSFFModFramework Loading Complete ({totalMs}ms) ===");
            // Perf baseline: warm cache target ~3500ms, cold (first run) target ~11700ms.
            // Warn when significantly over baseline so regressions surface in LogOutput.log.
            if (totalMs > 15000)
                Log.Warn($"[Perf] Load time {totalMs}ms exceeds 15s regression threshold (baseline ~3.5s warm / ~11.7s cold). Check [Timing] lines above for the slow phase.");

            Api.FrameworkEvents.RaiseLoaded();
        }
        catch (Exception ex)
        {
            Log.Error($"Framework loading failed: {ex}");
        }
    }

    private static void LogTiming(Stopwatch sw, string phase)
    {
        var ms = sw.ElapsedMilliseconds;
        if (ms >= 50)
            Log.Info($"[Timing] {phase}: {ms}ms");
        else
            Log.Debug($"[Timing] {phase}: {ms}ms");
        sw.Restart();
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
            Log.Warn($"[BlueprintDiag] {phase}: error: {ex.Message}");
        }
    }

    internal static IEnumerable GetAllData() =>
        GameRegistry.AllData ?? (IEnumerable)Array.Empty<object>();
}
