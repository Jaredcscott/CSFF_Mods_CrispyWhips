# CSFF Mod Framework

Standalone modding framework for Card Survival: Fantasy Forest. Provides mod discovery, JSON data loading, WarpData resolution, sprite/audio loading, localization, perk injection, blueprint tab injection, smelting recipe injection, ProducedCards normalization, AlwaysUpdate enabling, and a suite of performance patches. Mods only need C# for mod-specific logic (forage drops, vanilla card patching, custom Harmony patches).

## Status

- **Framework Version**: 2.0.2
- **Game Version**: EA 0.62d
- All in-house mods verified working in-game on EA 0.62b; 2.0.2 keeps the EA 0.62d compatibility bump and adds a save-load fix for placed blueprint containers (e.g. Oil Press) whose Recipes tab was missing after reload — the framework now re-spawns missing inventory blueprints and forces `BlueprintModelStates` to `Available` for every contained recipe of every placed container.

## What Changed in 2.0.0 (2026-04-26)

The framework is now **standalone** — it no longer ships compatibility stubs for legacy external runtimes and no longer supports third-party mods that hard-depend on them.

- Legacy compatibility DLLs are **not** in the deploy output.
- Third-party mods that hard-depend on removed external runtimes will not load with this framework.
- The legacy compatibility stubs have been removed from the repository entirely. They are not built or deployed.
- All in-house content mods now declare a hard dependency on `crispywhips.CSFFModFramework` v2.0.0 and no longer reference any Pikachu GUID.
- Legacy loader-version manifest fields have been stripped from every `ModInfo.json` in the repo.

## Installation

Single prerequisite for all in-house content mods: **CSFFModFramework**.

Install order: `BepInEx 5.x` → `CSFFModFramework` → any content mod. BepInEx resolves load order from each mod's `[BepInDependency]` declarations.

Deployed layout under `BepInEx/plugins/CSFF_Mod_Framework/`:

| File | Purpose |
|---|---|
| `CSFFModFramework.dll` | Core framework |
| `LitJSON.dll` | JSON parsing (used internally by the framework). Bundled so mods that also reference `LitJson` types resolve cleanly. |
| `UnityGifDecoder.dll` | GIF decoding for the framework's built-in GIF animation support (`Resource/GIF/*.gif` + `CardData/Gif/*.json`). MIT, redistributed unmodified. |
| `ModInfo.json` | Framework manifest |
| `SpriteCache/` | Generated at runtime — caches decoded PNG textures. Safe to delete; regenerated on next launch. |

## Configuration

`BepInEx/config/crispywhips.CSFFModFramework.cfg` (created on first run):

| Section | Key | Default | Purpose |
|---|---|---|---|
| General | `VerboseLogging` | `false` | Per-item diagnostic traces |
| General | `EnableLoadDiagnostics` | `false` | Two extra AllData scans around WarpResolver — enable only when investigating research-timer regressions |
| Performance | `OffScreenCardThrottleEnabled` | `true` | Throttle `InGameCardBase.LateUpdate` for off-screen, non-animating cards |
| Performance | `OffScreenCardThrottleFrames` | `3` | Run throttled cards 1-in-N frames |
| Performance | `DOTweenCapacityTweeners` / `DOTweenCapacitySequences` | `1000 / 200` | Pre-warm DOTween pool to avoid mid-session GC spikes |
| Wildlife | `WildlifeRaidsEnabled` | `false` | Opt-in: once per in-game day, roll for wildlife to spoil food in unguarded `tag_NotSafeFromAnimals` containers |
| Wildlife | `WildlifeRaidDailyChance` | `0.35` | Daily probability when enabled |
| Wildlife | `WildlifeRaidStressPenalty` | `2` | Stress added on a successful raid |

ConfigurationManager is recommended for an in-game UI.

## In-House Mods (Same Author)

| Mod | Plugins Folder | Version | Description |
|---|---|---|---|
| Advanced Copper Tools | `Advanced_Copper_Tools` | 1.7.1 | Copper metalworking, wheelbarrow, bathtub, stove, lantern, oil chain, tea kettle, tea blending station, copper chest |
| Herbs and Fungi | `Herbs_And_Fungi` | 1.6.3 | Herbalism, mushroom foraging, hemp farming, oil press, pickle fermentation, drying racks, medicinal teas, 15 perks |
| Water Driven Infrastructure | `Water_Driven_Infrastructure` | 1.2.0 | Water wheels, sawmills, grinding mills, ore sluices (river/lake adjacent) |
| Quick Transfer | `Quick_Transfer` | 1.5.1 | CTRL+Right-Click multi-card transfer between inventories |
| Repeat Action | `Repeat_Action` | 1.3.1 | Repeat last action with configurable keybinds and safety limits |
| Skill Speed Boost | `Skill_Speed_Boost` | 1.7.0 | Per-skill XP multipliers, difficulty profiles, optional staleness decay |
| Mod Update Manager | `Mod_Update_Manager` | 2.0.0 | Nexus Mods update checker with in-game UI (F8) |

Each in-house content mod declares `[BepInDependency("crispywhips.CSFFModFramework", "2.0.0")]`. The QoL mods (Quick Transfer, Repeat Action, Skill Speed Boost) work standalone on BepInEx 5.x and do not require the framework.

## What the Framework Handles

- **Mod discovery** — scans `BepInEx/plugins/` two levels deep for `ModInfo.json`
- **JSON data loading** — CardData, CharacterPerk, GameStat, etc. from each mod's subdirectories
- **WarpData resolution** — UniqueID/GUID references, runtime tag creation, nested array expansion, both array and `List<T>` field types
- **Sprite / Audio / Localization** — loads from each mod's `Resource/` and `Localization/` folders
- **Perk injection** — adds perks to the target `PerkGroup` and removes them from groups the engine auto-placed them into (e.g., Sex/Romance)
- **Blueprint tab injection** — reads each mod's `BlueprintTabs.json` and injects entries by `LocalizationKey`
- **Smelting recipe injection** — reads each mod's `SmeltingRecipes.json` and injects `CookingRecipes` into vanilla forges/furnaces with duplicate detection
- **ProducedCards normalization** — initializes default fields, fixes `Vector2Int.Quantity == (0,0)` to `(1,1)`, cleans null entries
- **AlwaysUpdate** — enables ticking on mod-owned cards
- **GameSourceModify** — patches vanilla objects from mod JSON overrides
- **BpFix** — sets `GameManager.BlueprintPurchasing = true` and `PurchasingWithTime = true` so research timers and the "+" research button stay enabled

## Performance Patches (active by default unless noted)

- **3 total `Resources.FindObjectsOfTypeAll` calls** (ScriptableObject, Sprite, AudioClip) — every service reuses the cached dictionaries
- **JSON file cache** — every mod JSON is read once into `JsonDataLoader.JsonByUniqueId`; downstream services never re-read from disk
- **Sprite texture cache** — decoded PNG bytes cached under `SpriteCache/`, keyed by MD5 of normalized path + source mtime; cuts sprite load from ~67% of total load time to < 5% on warm runs
- **Reflection field cache** — `(Type, fieldName)` → `FieldInfo` dictionary across all services
- **DOTween capacity pre-warm** — sized once at startup so animations don't trigger a pool-resize GC spike mid-session
- **OffScreenCardThrottle** — `InGameCardBase.LateUpdate` runs 1-in-3 frames for off-screen, non-animating cards (configurable; biggest remaining card-count win)
- **SlotAssignmentLogSuppress** — transpiler strips `Debug.LogWarning` calls from `DynamicLayoutSlot.AssignCard` (per-frame spam in late-game saves with many improvements)
- **AmbienceArrayReuse** — reuses a cached `float[3]` inside `AmbienceImageEffect.Update` instead of allocating one per frame

## Architecture

- `CSFFModFramework.dll` — the only framework binary the loader actually executes
- `Loading/LoadOrchestrator.cs` — orders the load passes (Database → JSON → WarpResolver → Sprite/Audio → Perk/Blueprint inject → ProducedCards/AlwaysUpdate)
- `Patching/` — Harmony patches grouped by concern (BugFixes, Performance, Diagnostics, BpFixPatch, GameLoadPatch, LocalizationPatch)
- `Wildlife/WildlifeRaidService.cs` — opt-in raid mechanic
- `Stubs/LitJson/` — in-tree LitJSON v0.18.0.0 source built into the bundled `LitJSON.dll`
Mods only need C# for **mod-specific logic**: custom action interception, forage drop injection, vanilla card patching, custom Harmony patches on gameplay methods. Anything in the "What the Framework Handles" list above is automatic.

## Key File Locations

- Vanilla game data dump: `Documentation/GameData/CSFF-JsonData_EA_0-62d/`
- GUID lookups: `Documentation/GameData/CSFF-JsonData_EA_0-62d/UniqueIDScriptableGUID/`
- LitJSON source: `Stubs/LitJson/LitJsonStub.cs` → `LitJSON.dll` (v0.18.0.0)
- Starter kit: `CSFF_Modding_Starter_Kit/Documentation/`

## License

Released under the [MIT License](LICENSE). Copyright (c) 2026 Jared Scott.

Bundled third-party libraries retain their original MIT-compatible licenses:
- **LitJSON** — public domain (reimplemented in `Stubs/LitJson/LitJsonStub.cs`)
- **UnityGifDecoder** — MIT License, copyright (c) 2020 3DI70R (see `THIRD_PARTY_LICENSES.md`)
