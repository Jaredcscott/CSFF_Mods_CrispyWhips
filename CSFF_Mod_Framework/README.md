# CSFF Mod Framework

Standalone modding framework for Card Survival: Fantasy Forest. Provides mod discovery, JSON data loading, map cache indexing, WarpData resolution, sprite/audio loading, localization, perk injection, blueprint tab injection, smelting recipe injection, SelfTriggeredAction activation, world map node injection, NPC agent diagnostics, ProducedCards normalization, AlwaysUpdate enabling, and a suite of performance patches. Mods only need C# for mod-specific logic (forage drops, vanilla card patching, custom Harmony patches).

## Status

- **Version:** 2.7.1
- **Game Version**: EA 0.64f
- All in-house mods are maintained against EA 0.64f.

## What Changed in 2.0.0 (2026-04-26)

The framework is now **standalone** — it no longer ships compatibility stubs for legacy external runtimes and no longer supports third-party mods that hard-depend on them.

- Legacy compatibility DLLs are **not** in the deploy output.
- Third-party mods that hard-depend on removed external runtimes will not load with this framework.
- The legacy compatibility stubs have been removed from the repository entirely. They are not built or deployed.
- All in-house content mods now declare a `SoftDependency` on `crispywhips.CSFFModFramework` for load ordering and no longer reference any Pikachu GUID.
- The legacy `ModLoaderVerison` manifest field is ignored by this framework; existing manifests may keep it, and new framework-based mods do not need it.

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
| General | `ForceChineseMode` | `false` | When true, loads Chinese (SimpCn.csv) regardless of game language setting — use to test translations without changing system language |
| General | `EnableLoadDiagnostics` | `false` | Two extra AllData scans around WarpResolver — enable only when investigating research-timer regressions |
| Performance | `OffScreenCardThrottleEnabled` | `true` | Throttle `InGameCardBase.LateUpdate` for off-screen, non-animating cards |
| Performance | `OffScreenCardThrottleFrames` | `3` | Run throttled cards 1-in-N frames |
| Performance | `DOTweenCapacityTweeners` / `DOTweenCapacitySequences` | `1000 / 200` | Pre-warm DOTween pool to avoid mid-session GC spikes |
| Wildlife | `WildlifeRaidsEnabled` | `false` | Opt-in: once per in-game day, roll for wildlife to spoil food in unguarded `tag_NotSafeFromAnimals` containers |
| Wildlife | `WildlifeRaidDailyChance` | `0.35` | Daily probability when enabled |
| Wildlife | `BearRaidChance` | `0.5` | Probability (0–1) that a bear encounter also triggers a raid on nearby open containers; sealed containers are always safe |
| Wildlife | `WildlifeRaidStressPenalty` | `2` | Stress added on a successful raid |

ConfigurationManager is recommended for an in-game UI.

## In-House Mods (Same Author)

| Mod | Plugins Folder | Version | Description |
|---|---|---|---|
| Advanced Copper Tools | `Advanced_Copper_Tools` | 1.9.0 | Copper metalworking, wheelbarrow, bathtub, stove, lantern, oil chain, tea kettle, tea blending station, copper chest |
| Herbs and Fungi | `Herbs_And_Fungi` | 1.8.0 | Herbalism, mushroom foraging, hemp farming, oil press, pickle fermentation, drying racks, medicinal teas, 15 perks |
| Water Driven Infrastructure | `Water_Driven_Infrastructure` | 1.6.0 | Water wheels, sawmills, grinding mills, ore sluices (river/lake adjacent) |
| Quick Transfer | `Quick_Transfer` | 1.6.1 | Shift/Ctrl/Ctrl+Shift+Right-Click multi-card transfer with live preset indicator |
| Repeat Action | `Repeat_Action` | 1.6.0 | Repeat last action with configurable keybinds and safety limits |
| Skill Speed Boost | `Skill_Speed_Boost` | 1.9.1 | Per-skill XP multipliers, difficulty profiles, staleness decay, synergies, level scaling |
| Mod Update Manager | `Mod_Update_Manager` | 2.1.1 | Nexus Mods update checker with in-game UI (F8) |

Each in-house content mod declares `[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]`. The QoL mods (Quick Transfer, Repeat Action, Skill Speed Boost) work standalone on BepInEx 5.x and do not require the framework.

## What the Framework Handles

- **Mod discovery** — scans `BepInEx/plugins/` two levels deep for `ModInfo.json`
- **Map cache indexing** — parses declared/generated `Data/*Map*.json` files once and exposes them through `MapCacheRegistry`
- **JSON data loading** — from each mod's top-level content directories (folder name = type name, matching the vanilla JSON export layout): `CardData`, `CharacterPerk`, `PerkGroup`, `GameStat`, `SpiceTag`, and (since 2.1.0) `FlavourTag`, `NPCStat`, `NPCDuty`, `NPCHidingGroup`, `NPCAgent`, `Encounter`, `SelfTriggeredAction`, `Objective`, `QuestLog`, `GameModifierPackage`, `PlayerCharacter`, `CookingRecipeGroup`, `ConstructionCardGroup`, `BookmarkGroup`, `LocalTickCounter`. Any other ScriptableObject type loads generically from `ScriptableObject/<TypeName>/`. **Scope note:** the 2.1.0 types are loaded and registered in the game's UID registry, and WarpData references to/from them resolve — but most are not yet *activated* (no NPC agent spawning, no quest attachment, no character-select injection). Exception: **`SelfTriggeredAction` is fully active since 2.2.0** (see below). The remaining injectors are planned in later phases; today those types are usable wherever vanilla code resolves them by GUID reference. Feature-detect via `Api.Framework.SupportsContentType(...)`.
- **WarpData resolution** — UniqueID/GUID references, runtime tag creation, nested array expansion, both array and `List<T>` field types
- **Sprite / Audio / Localization** — loads from each mod's `Resource/` and `Localization/` folders
- **Perk injection** — adds perks to the target `PerkGroup` and removes them from groups the engine auto-placed them into (e.g., Sex/Romance)
- **Blueprint tab injection** — reads each mod's `BlueprintTabs.json` and injects entries by `LocalizationKey`
- **Smelting recipe injection** — reads each mod's `SmeltingRecipes.json` and injects `CookingRecipes` into vanilla forges/furnaces with duplicate detection
- **SelfTriggeredAction activation (since 2.2.0)** — mod STAs in `SelfTriggeredAction/*.json` are discovered by `GameManager` at run start automatically (registration into `AllData` is sufficient); the framework validates them at load (missing triggers, unresolved stats, save-state ID problems) and logs a one-line run-start confirmation. Authoring guide + decision table vs. the simpler `CardData/Trigger/*.json` spawn system: `Documentation/CSFF_Patterns.md` § SelfTriggeredAction
- **NPCAgent validation + diagnostics (since 2.3.0)** — at load time, `NPCAgentActivationService` validates every mod-owned `NPCAgent`: normalizes null arrays (`AgentStats`, `AgentDuties`, `Interactions`, `AgentActions`) that would NRE during GameManager initialization, and warns on missing `AgentName`. At each run start (via `OnGMInitialized`), the service surveys GameManager for NPC-typed fields/properties, checks for `NPCManager`/`WorldNPCManager` components, and reports whether mod agents appear in any discovered agent list — logging each finding as a `[DIAGNOSTICS]` line. This confirms whether `AllData` registration is sufficient or whether a future `NPCAgentInjector` must explicitly append agents. See "NPCAgent Diagnostics" section below.
- **WorldMap node injection (since 2.3.0)** — mods may ship `WorldMap/MapNodes.json` to add new travel locations to the in-game world map. The framework reads these at load time, resolves each environment UID to its CT4 `CardData`, creates the appropriate `MapEnvData` entries, and appends them to the `WorldMapData` singleton. Connections are bidirectional — declaring A→B automatically creates B→A so travel works in both directions. See "Adding a Map Location" section below.
- **ProducedCards normalization** — initializes default fields, fixes `Vector2Int.Quantity == (0,0)` to `(1,1)`, cleans null entries
- **AlwaysUpdate** — enables ticking on mod-owned cards
- **GameSourceModify** — patches vanilla objects from mod JSON overrides
- **BpFix** — sets `GameManager.BlueprintPurchasing = true` and `PurchasingWithTime = true` so research timers and the "+" research button stay enabled

## Performance Patches (active by default unless noted)

- **3 startup `Resources.FindObjectsOfTypeAll` calls** (ScriptableObject, Sprite, AudioClip), plus one cached UI-time `CardTabGroup` scan for blueprint journal tabs — every other service reuses the cached dictionaries
- **JSON file cache** — every mod JSON is read once into `JsonDataLoader.JsonByUniqueId`; downstream services never re-read from disk
- **Map cache registry** — generated map details such as WDI mill-race edges are parsed once with `MiniJson` and reused by mod code
- **Sprite texture cache** — decoded PNG bytes cached under `SpriteCache/`, keyed by MD5 of normalized path + source mtime; cuts sprite load from ~67% of total load time to < 5% on warm runs
- **Reflection field cache** — `(Type, fieldName)` → `FieldInfo` dictionary across all services
- **DOTween capacity pre-warm** — sized once at startup so animations don't trigger a pool-resize GC spike mid-session
- **OffScreenCardThrottle** — `InGameCardBase.LateUpdate` runs 1-in-3 frames for off-screen, non-animating cards (configurable; biggest remaining card-count win)
- **SlotAssignmentLogSuppress** — transpiler strips `Debug.LogWarning` calls from `DynamicLayoutSlot.AssignCard` (per-frame spam in late-game saves with many improvements)
- **AmbienceArrayReuse** — reuses a cached `float[3]` inside `AmbienceImageEffect.Update` instead of allocating one per frame

## Architecture

- `CSFFModFramework.dll` — the only framework binary the loader actually executes
- `Loading/LoadOrchestrator.cs` — orders the load passes (Database → JSON → WarpResolver → Sprite/Audio → Perk/Blueprint inject → STA validation → NPC validation/diagnostics → WorldMap injection → ProducedCards/AlwaysUpdate)
- `Injection/NPCAgentActivationService.cs` — load-time validation + run-start diagnostics for mod NPCAgents (Phase 3)
- `Loading/WorldMapLoader.cs` — parses each mod's `WorldMap/MapNodes.json` using MiniJson
- `Injection/WorldMapInjector.cs` — appends parsed map nodes to the `WorldMapData` singleton with bidirectional link enforcement (Phase 4)
- `Patching/` — Harmony patches grouped by concern (BugFixes, Performance, Diagnostics, BpFixPatch, GameLoadPatch, LocalizationPatch)
- `Wildlife/WildlifeRaidService.cs` — opt-in raid mechanic
- `Stubs/LitJson/` — in-tree LitJSON v0.19.0.0 source built into the bundled `LitJSON.dll`

Mods only need C# for **mod-specific logic**: custom action interception, forage drop injection, vanilla card patching, custom Harmony patches on gameplay methods. Anything in the "What the Framework Handles" list above is automatic.

## Key File Locations

- Vanilla game data dump: `Documentation/GameData/CSFF-JsonData_EA_0-64f/`
- GUID lookups: `Documentation/GameData/CSFF-JsonData_EA_0-64f/UniqueIDScriptableGUID/`
- LitJSON source: `Stubs/LitJson/LitJsonStub.cs` → `LitJSON.dll` (v0.19.0.0)
- Starter kit: `CSFF_Modding_Starter_Kit/Documentation/`

---

## Adding a Map Location (`WorldMap/MapNodes.json`)

Place a file at `<YourMod>/WorldMap/MapNodes.json`. The framework auto-detects it during mod probing and runs the injector. There are two ways to define the location's cards:

**Clone-based (recommended)** — mirror an existing vanilla location at runtime. The framework clones the loaded vanilla CT4 environment card *and* its CT8 explorable-location card under your new UIDs, keeping every tag, ambience clip, improvement list, tree drop, and blueprint reference pointing at the live vanilla SOs. This is the only way to meet the full vanilla minimum definition: vanilla JSON exports reference tags through obfuscated asset names that must never be copied into mod JSON (CLAUDE.md §Obfuscated WarpData Names).

```json
[
  {
    "EnvironmentUID": "cmc_env_village_path",
    "LocationUID": "cmc_loc_village_path",
    "CloneOfEnvironmentUID": "5af32ab7ce936684d99add01fc56015a",
    "DisplayName": "Village Path",
    "NameLocalizationKey": "CMC_VillagePath_CardName",
    "EnvNameLocalizationKey": "CMC_Env_VillagePath_CardName",
    "Coords": { "x": 10.0, "y": 0.0, "z": 0.0 },
    "Connections": [
      { "EnvironmentUID": "2b19b942a09fdd148a43798e942a74eb", "PathCost": 10.0 }
    ]
  }
]
```

**JSON-card-based** — omit `CloneOfEnvironmentUID` and ship your own CT4 `CardData` JSON; `EnvironmentUID` must resolve to it at injection time.

### Field Reference

| Field | Required | Description |
|---|---|---|
| `EnvironmentUID` | Yes | `UniqueID` of the CT4 environment card. With `CloneOfEnvironmentUID`, this is the NEW UID assigned to the clone. |
| `CloneOfEnvironmentUID` | No | Vanilla CT4 UID to mirror at runtime (e.g. Green Tangle = `5af32ab7ce936684d99add01fc56015a`). |
| `LocationUID` | With CloneOf | NEW UID assigned to the cloned CT8 explorable-location card. |
| `DisplayName` | With CloneOf | Player-visible name for both clones (fresh `CardName` LocalizedStrings are built — the template's are never mutated). |
| `NameLocalizationKey` / `EnvNameLocalizationKey` | No | CSV keys for the clone names. Defaults: `<LocationUID>_CardName` / `<EnvironmentUID>_CardName`. Add matching rows to `Localization/SimpEn.csv`. |
| `Coords` | No | `Vector4` world-map position. Vanilla uses a 10-unit grid: x=+east, z=+north (River Clearing is `(0,0,0)`, Green Tangle `(-10,0,0)`). |
| `HideFromMap` | No | `true` = not shown on the travel UI. Default `false`. |
| `Icon` | No | Sprite name for the map node icon (`Database.SpriteDict`). Clone nodes default to the template node's icon. |
| `Connections` | No | Array of travel links to other environment nodes. |

**Connection fields:**

| Field | Required | Description |
|---|---|---|
| `EnvironmentUID` | Yes | `UniqueID` of the connected CT4 environment (vanilla or from another mod node injected earlier in the same file). |
| `PathCardUID` | No | Travel card for the link. Vanilla convention: each node's connections use its OWN CT8 location card — clone nodes default to the cloned location card. |
| `PathCost` | No | Travel cost. Vanilla standard is `10.0` (the default). |
| `TravelDirection` | No | `"North"`/`"South"`/`"East"`/`"West"` or `{"x":…,"z":…}` unit vector (z=+1 North, x=+1 East). Omit to derive automatically from the two nodes' `Coords`. |
| `TravelActionTags` | No | Runtime ActionTag names applied to the travel action. Omit to inherit the template/connected node's travel tags (matches vanilla travel). |
| `HideConnection` | No | `true` = hides the line on the map UI. Default `false`. |

### Bidirectional Links

Every declared connection is **automatically mirrored**: `A → B` also writes `B → A` with the travel direction negated, using B's own existing PathCard and travel tags (the vanilla convention). The mill-race lesson applies — single-direction edges must not create one-way connectivity.

### Map-Consuming Mods (`Api.WorldMap`)

Every injected connection is recorded — both directions — in `CSFFModFramework.Api.WorldMap`. Consumers that maintain their own map graphs (e.g. WaterDrivenInfrastructure's mill-race system) extend themselves automatically:

- `Api.WorldMap.InjectedEdges` — typed `MapEdge` list (`SourceEnvUid`, `SourceLocationUid`, `Direction` 0=N/1=S/2=E/3=W, `DestinationEnvUid`, `DestinationLocationUid`, `PathCost`, `HiddenOnInGameMap`, `SourceMod`).
- `Api.WorldMap.GetInjectedEdgesJson()` — the same edges serialized in WDI's `MillRaceMapEdges.json` schema, for reflection-based consumers reusing an existing edge-file parser.

Query from a `LoadMainGameData` postfix (content-mod postfixes run after framework loading) or after `Api.FrameworkEvents` signals load completion.

### Troubleshooting

- **Node doesn't appear on map**: If the map UI builds its node list before load-time injection takes effect, the fix mirrors the BlueprintModelsScreen lesson — add a `InGameMapWindow.Show` postfix hook to re-apply node data at UI-open time. A `WorldMapInjector: N node(s) injected` line confirms the injector ran; `skipped` count > 0 = a UID didn't resolve (see warnings above it).
- **One-way travel**: the connection target had no existing map node at injection time — the injector logs a `will be ONE-WAY` warning naming the UID.
- **Clone skipped**: `CloneOfEnvironmentUID` must be a CT4 card whose `DefaultEnvCardDrops` contains a CT8 location card (all standard vanilla locations qualify); `LocationUID` is required.
- **Feature flag**: The injector only runs if at least one loaded mod has `HasWorldMapNodes = true` (detected from `WorldMap/MapNodes.json` existing in that mod's folder).

---

## NPCAgent Diagnostics

Mod `NPCAgent` instances are loaded into `AllData` from `NPCAgent/*.json` since framework 2.1.0. Since 2.3.0, `NPCAgentActivationService` adds load-time validation and run-start diagnostics.

### What Happens at Load

For every mod-owned `NPCAgent`, the framework:
1. Normalizes null arrays (`AgentStats`, `AgentDuties`, `Interactions`, `AgentActions`) to empty arrays — prevents `NullReferenceException` during GameManager NPC initialization.
2. Warns if `AgentName.DefaultText` is empty — the agent will appear unnamed in any UI button.

### What Happens at Run Start

After `GameManager.OnGMInitialized` fires (one run start after load), the framework checks whether mod agents appear in the GameManager agent list and injects any that are missing. The survey `[DIAGNOSTICS]` lines are logged at **Debug level** — they appear only when `VerboseLogging = true` in the BepInEx config. The injection result is always logged at Info level.

Missing agents are auto-injected at run start; no mod-side C# is required:

```
NPCAgentActivation: 0/1 mod NPCAgent(s) in GameManager.AllNPCAgents. Missing: my_npc_agent
NPCAgentActivation: [DIAGNOSTICS] Mod agents NOT auto-discovered — injecting into GameManager.AllNPCAgents
NPCAgentActivation: injected my_npc_agent (my_npc_agent) into GameManager agent list
NPCAgentActivation: 1/1 mod NPCAgent(s) injected
```

Or, if AllData registration is sufficient:
```
NPCAgentActivation: 1/1 mod NPCAgent(s) confirmed in GameManager.AllNPCAgents — AllData auto-discovery sufficient
```

### Reading the Output

| Line | What it means |
|---|---|
| `No NPCAgent-typed fields/props found on GameManager` | GameManager doesn't hold the agent list directly; look for the `NPCManager` / `WorldNPCManager` component lines below it. |
| `Found N instance(s) of NPCManager` | The game delegates NPC management to a separate component; subsequent lines show its fields. |
| `AllData registration is sufficient` | Your mod NPCAgent will activate automatically — no extra C# needed. |
| `Mod agents NOT auto-discovered — injecting into <fieldName>` | The framework auto-injected your agent into `GameManager.<fieldName>`. No mod-side C# needed. If you see follow-up errors, a `TargetInvocationException` on the inject line indicates the agent list's type rejects the add — open an issue with your NPCAgent JSON and the full error. |

### Authoring an NPCAgent

Minimal `NPCAgent/*.json`:
```json
{
  "UniqueID": "my_npc_agent",
  "AgentName": { "DefaultText": "Forest Elder" },
  "AgentStats": [],
  "AgentDuties": [],
  "Interactions": [],
  "AgentActions": []
}
```

Null arrays are normalized by the framework but it is cleaner to supply empty arrays explicitly. `AgentStats`, `AgentDuties`, `Interactions`, and `AgentActions` accept GUID/UID references resolved via WarpData.

> **Status (v2.6.0)**: Injection is implemented. `NPCAgentActivationService` validates at load time and injects missing agents into the GameManager agent list at `OnGMInitialized`. Survey `[DIAGNOSTICS]` lines are Debug-level only (set `VerboseLogging = true` to see them).

---

## Public Utility API (Tier 1 — v2.4.0)

Public helpers for content-mod C# under `CSFFModFramework.Api` (plus the long-standing
`CSFFModFramework.Util.CardUtil`). They replace the reflection scaffolding, array mutation,
inventory loops, GUID tables, and dedup guards that every mod previously re-implemented
(see `Documentation/CSFFMFW_Centralization_Plan_2026-06-09.md`).

| API | Purpose | Status |
|---|---|---|
| `Api.Reflect` | `GetMember(obj, names...)` / `SetMember` with property→field→backing-field fallback and per-(Type,name) caching; `TryGetType(names...)`; `DeepClone` (cycle-guarded, Unity refs preserved); `AllFlags` constants | Proven in CMC 1.1.0 and Sirus 1.1.0 (sheep reproduction DeepClone) |
| `Api.Collections` | `Append`/`AppendAll`/`Merge` on Array or `List<T>` members (null-instantiates, dedups by reference, auto-marks dirty for compaction); `CreateLike`; `SetCollection` | Used by Sirus 1.1.0 and the Phase 5 injectors |
| `Api.Inventory` | `Cards(container)` flattened slot traversal, `Find`/`Count` by UID or `tag_*`, `Consume`, `Eject` (spawn-eject removal — no OnDestroy relocation) | Used by WildlifeRaidService; mod adoption with ACT/WDI migrations |
| `Api.Gate` | `OncePerFrame`, `OncePerDtpTick`, `OncePerDayRollover` dedup gates | Used by WildlifeRaidService |
| `Api.LocalizedStringBuilder` | `Create`/`CreateLike`/`Populate` — programmatic LocalizedStrings, field-vs-property safe | Ships for the H&F/Sirus migrations |
| `Api.VanillaIds` | name→GUID registry embedded from extracted game data (`Get`, `GetStat`, `Group`, curated groups: `LargeTrees`, `Fires`, `OpenStorage`, `WaterSources`, `AnimalFoods`, `BearEncounters`); regenerate via `Development_Tools/Generate-VanillaIds.ps1` on every game-data extraction | Used by WildlifeRaidService/WildlifeRaidPatch |
| `Api.ContentRegistry.RegisterWithResult` | `Registered` / `DuplicateSkipped` / `Failed` outcome enum (D2) | `Register(bool)` delegates to it |
| `CardUtil.GetDurability` / `SetDurability` / `GetDurabilityMax` | Absolute stat read/write + max read over both runtime shapes (flat `CurrentX` properties and `DurabilityStats` containers), JSON or runtime stat names | Proven in CMC QualitySplit and Sirus WolfTick |
| `CardUtil.TransformInPlacePreservingStats` | In-place CardModel swap that captures/restores chosen durability stats | Formalizes WDI fishpond pattern; adoption with WDI migration |
| `CardUtil.TryRemoveCard` / `RemoveCardCleanly` | Game-method removal (board cards) / placeholder-swap removal (in-inventory cards) | Used by Sirus WolfTick |

## Runtime Services API (Tier 2 — v2.5.0)

Framework-owned runtime services under `CSFFModFramework.Api`. Each replaces a Harmony
patch shape that mods previously re-implemented (and that could not safely compose
across mods — multiple iterator postfixes on one coroutine never compose).

| API | Purpose | Status |
|---|---|---|
| `Api.ActionRouter` | ONE framework patch set on `ActionRoutine` / `CardOnCardActionRoutine` / `PerformStackActionRoutine` / `PerformActionAsEnumerator`; mods register `ActionHandler { CardUid/CardPredicate, ActionKeyPrefix/ActionNamePrefix, Timing = Cancel/Before/AfterWrapped, Before, After }`. Built-in two-tier action identity, per-handler frame dedup, the single IEnumerator wrap point, the canonical cancel stub (game-state restore), and an external-postfix conflict warning. Patches lazily on first `Register` — zero overhead with no consumers | Proven in CMC 1.2.0 QualitySplit; WDI/ACT station intercepts migrate next |
| `Api.SpawnService` | `Spawn(uidOrCardData, statOverrides)` — GiveCard spawn + immediate stat init; `OnNextSpawn(uid, statOverrides, count, ttlFrames)` — queued overrides for game-side spawns (ProducedCards, OnFull, perk kits) serviced by ONE GiveCard postfix; `CardSpawned` event | Proven in CMC 1.2.0 (main-shard + remainder-shard quality) |
| `Api.TickEvents` | `DtpTick` / `DayRollover` events + `Interval(seconds, callback)` real-time timers, driven by one framework Update loop with per-subscriber exception isolation | Proven in Sirus 1.1.0 WolfTick |
| `Api.EncounterGuards` | `Register(name, ctx => suppress)` wildlife-encounter suppression through the framework's single `StartEncounter` prefix (NPC encounters never suppressed); declarative `EncounterGuards/*.json` option (guard cards in player env + optional encounter filter + chance) | Proven in Sirus 1.1.0 (`EncounterGuards/WolfGuard.json`) |
| `Api.ContentModPlugin` | Optional plugin base class: Harmony creation, `RegisterPatches` with `TryApply` per-patch isolation, canonical one-line load log, UnpatchSelf. Subclassing makes the framework DLL a hard runtime requirement (keep the `[BepInDependency]` soft attribute for load order) | Used by Sirus 1.1.0 and CMC 1.2.0 |

## Quests & Characters (Gap Phase 5 — v2.5.0)

Author content with the standard folders (loaded + warp-resolved since 2.1.0), then
attach it with a root manifest:

- **`Objective/*.json` + `QuestLog/*.json` + `Quests.json`** — each manifest entry
  attaches a QuestLog to characters: `{ "Quests": [ { "QuestLog": "<uid>",
  "Characters": ["Huntsman"] } ] }`. Characters match by UniqueID, asset name, or
  display name; empty list = all non-editor characters.
- **`PlayerCharacter/*.json` + `Characters.json`** — each entry adds a character to a
  select-screen roster: `{ "Characters": [ { "Character": "<uid-or-name>",
  "Roster": "Fates" } ] }` (`Fates` | `Ways` | `Both`). `GameModifierPackage` needs no
  manifest — reference it from the character's `EasyPackageWarpData`.

> **Status (v2.5.0)**: injectors are implemented and validated against the EA 0.64f
> data model (PlayerCharacter.Quests holds QuestLog refs; Gamemode "CharacterList"
> holds the Fates/Ways rosters). In-game verification and the save-compat test
> (add character → save → remove mod → load) are pending — run them before shipping
> content that depends on these.

## Version History

### v2.7.1
- Version sync: `Plugin.cs` version aligned with `GlobalUsing.cs` / `ModInfo.json` / `README.md`.
- Outer `try`/`catch` added to `BlueprintScreenFix.ApplyPatch`, `LocalizationPatch.ApplyPatch`, and `GameLoadPatch.ApplyPatch` for consistency with all other patch setup methods.
- NPCAgent section updated: `[DIAGNOSTICS]` survey lines are Debug-only (VerboseLogging gate); `NPCAgentActivationService` injection status documented accurately.

### v2.7.0
- **Clone-based WorldMap nodes**: `WorldMap/MapNodes.json` entries may now specify `CloneOfEnvironmentUID` to mirror a vanilla CT4/CT8 location pair at runtime via `CardCloneService` — no need to ship full `CardData` JSON files for the cloned cards. This is the only safe path to inherit obfuscated tag references from vanilla.
- **CardCloneService**: deep-clone infrastructure for CT4 environment + CT8 explorable-location card pairs; builds fresh `CardName` `LocalizedString` instances so the template's strings are never mutated.
- **Api.WorldMap edge API**: `InjectedEdges` (`List<MapEdge>` with `SourceEnvUid`, `Direction`, `PathCost`, etc.) and `GetInjectedEdgesJson()` (WDI mill-race schema) for map-graph consumers. WDI 1.6.0 uses this API to auto-extend its mill-race graph with framework-injected edges.

### v2.6.0
- **ForeignInstanceReconciler**: Pikachu ModLoader scans every `plugins/*/ModInfo.json` in a `UniqueIDScriptable.ClearDict` prefix, loading and registering duplicate SO instances that win `GetFromID` lookups — which resets blueprint research on save-load and breaks reference-identity checks. The reconciler (runs after `JsonDataLoader.LoadAll`, BEFORE `WarpResolver`) rebinds `AllUniqueObjects` and swaps `AllUniqueObjectsAsInts` in place to point at the framework's canonical instances, then removes duplicates from `AllData`.
- **NPCAgentActivationService** injection implemented: agents not auto-discovered by GameManager are appended to the found agent list at `OnGMInitialized`; no mod-side C# required. Survey `[DIAGNOSTICS]` lines demoted to Debug level.

### v2.5.0
- **Centralization Tier 2 — runtime services**: `Api.ActionRouter` (single action-dispatch layer with the one IEnumerator wrap point), `Api.SpawnService` (spawn + stat-init via one GiveCard postfix), `Api.TickEvents` (DtpTick/DayRollover/Interval), `Api.EncounterGuards` (single StartEncounter prefix + `EncounterGuards/*.json` declarative guards), `Api.ContentModPlugin` base class. `CardUtil.GetDurabilityMax` added.
- **Gap Phase 5 — quests & characters**: `QuestInjector` (`Quests.json` → PlayerCharacter quest lists) and `CharacterRosterInjector` (`Characters.json` → Gamemode Fates/Ways rosters).
- **Acceptance retrofits**: Sirus 1.1.0 (WolfTick → TickEvents + VanillaIds + CardUtil; encounter guard → JSON; sheep patching → Reflect/Collections/GameDataReady; ContentModPlugin) and CMC 1.2.0 (QualitySplit → ActionRouter + SpawnService). In-game smoke tests pending.

### v2.4.0
- **Centralization Tier 1 — public utility layer**: `Api.Reflect`, `Api.Collections`, `Api.Inventory`, `Api.Gate`, `Api.LocalizedStringBuilder`, `Api.VanillaIds`, plus `CardUtil` durability get/set, stat-preserving transform, and card removal helpers (see table above). Additive only — no behavior changes to existing loading.
- **Api.VanillaIds embedded registry**: 2,757 card + 592 stat GUIDs and 6 curated groups generated from EA 0.64f game data; `Development_Tools/Generate-VanillaIds.ps1` regenerates it and is wired into `/extract-latest-carddata`.
- **WildlifeRaidService data-driven**: open-storage container UIDs and the bear-encounter UID now come from `Api.VanillaIds` (hardcoded EA 0.64f values remain as fallback); inventory scans and day-rollover detection moved onto `Api.Inventory`/`Api.Gate`.
- **ContentRegistry.RegisterWithResult** (FRAMEWORK_EVALUATION D2): callers can now distinguish duplicate-skips from failures.
- **CMC 1.1.0** retrofitted onto the Tier 1 APIs as the acceptance proof (~130 LOC of local reflection removed).

### v2.3.0
- **NPCAgent validation + diagnostics (gap audit Phase 3)**: `NPCAgentActivationService` validates all mod-owned `NPCAgent` instances at load time (null-array normalization, missing name warnings). At run start via `OnGMInitialized`, it surveys GameManager for NPC-typed fields/properties and any `NPCManager`/`WorldNPCManager` components, then checks whether mod agents appear in the discovered lists — logging the survey as `[DIAGNOSTICS]` lines at Debug level. Injection of missing agents was completed in v2.6.0.
- **WorldMap node injection (gap audit Phase 4)**: mods may ship `WorldMap/MapNodes.json` to add new locations to the in-game travel map. `WorldMapLoader` parses the JSON with MiniJson; `WorldMapInjector` resolves UID references to CT4 `CardData` SOs, constructs `MapEnvData` entries via reflection-based `Activator.CreateInstance`, and appends them to `WorldMapData.Environments`. All declared connections are made bidirectional automatically.
- Framework description and `Plugin.cs` version bumped to 2.3.0.

### v2.2.0
- **SelfTriggeredAction activation (gap audit Phase 2)**: mod STAs are now fully active. Research confirmed `GameManager.InitializeStatsAndActions()` discovers every exact-type STA from `DataBase.AllData` at run start — no roster injection needed. New `StaActivationService` validates mod STAs at load (null `Actions` normalization, empty/unresolved `StatChangeTrigger` warnings, OnlyOnce save-state ID checks) and confirms activation in the log after `OnGMInitialized`. Worked example: `Development_Tools/TestMods/FrameworkStaTest/`.

### v2.1.0
- **Content-type expansion (gap audit Phase 1)**: the JSON loader now loads 15 additional `UniqueIDScriptable` types from top-level mod folders — `FlavourTag`, `NPCStat`, `NPCDuty`, `NPCHidingGroup`, `NPCAgent`, `Encounter`, `SelfTriggeredAction`, `Objective`, `QuestLog`, `GameModifierPackage`, `PlayerCharacter`, `CookingRecipeGroup`, `ConstructionCardGroup`, `BookmarkGroup`, `LocalTickCounter`. Loaded + registered + WarpData-resolved; activation injectors (agent spawning, STA scheduling, quest/character attachment) come in later phases.
- New public API: `Api.Framework` — `Version`, `SupportedContentTypes`, `SupportsContentType(name)` for downstream feature detection.
- `GameSourceModify`: warns when a patch contains an empty array (`"Foo": []`) that would erase a non-empty collection on the target — accidental vanilla-data wipes are now visible in the log (use `_appendArrays` to add entries).
- `WarpResolver`: element-creation failures (WarpType 4/6 `Activator.CreateInstance`) now log at Debug instead of being silently swallowed.
- `GifLoader` is now gated behind a `CardData/Gif/*.json` feature probe like every other optional phase.

### v2.0.8
- Version bump for release alongside ACT 1.7.8, H&F 1.6.10, WDI 1.3.3, QT 1.6.1, RA 1.4.1, SSB 1.9.1, MUM 2.1.1

### v2.0.7
- Blueprint injector updated to use live UI tabs at `BlueprintModelsScreen.Show` time (with `Resources.FindObjectsOfTypeAll<CardTabGroup>` fallback) — fixes "no tab found" errors for all content mods on EA 0.63f

### v2.0.6
- Added `MapCacheRegistry` and `Assets.MapCaches` support so mods can ship generated map JSON caches (for example WDI mill-race edge maps) and reuse the framework's parsed copy instead of re-reading/parsing during their own load patches

### v2.0.5
- EA 0.63 compatibility pass
- `BlueprintContainerSaveLoadFix`: uses `_isInGameplay` flag to separate save-load path from gameplay path; `SpawnDefault_Postfix` is a no-op during load (deferred via `OnGMInitialized`) — **never re-introduce a synchronous drain in this postfix** (causes freeze at "Current Character:")
- `CardScaleCompat`: defers all Harmony patching into a coroutine; no `SafePatcher.TryPatch` during `Awake` (CSR 3.3.0 root cause + EA 0.62b fix)
- `ReflectionCache.FindType`: guards `ReflectionTypeLoadException` via `rtle.Types`; never caches null
- `CreateInstanceSafe`: initializes `LocalizedString` fields on all created instances (WikiMod crash fix)
- `AccessTools.Field` → auto-property backing field fallback added for all services

### v2.0.4
- Blueprint-container save/load repair: re-spawns missing contained blueprints and forces `BlueprintModelStates` to `Available` for every placed container on load

### v2.0.0
- **Breaking change**: framework is now standalone. Legacy ModCore/ModLoader compatibility stubs removed; third-party mods that hard-depend on them will not load with this version.
- All in-house content mods updated to `[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]`; no Pikachu GUID references remain in any in-house mod.
- `ModLoaderVerison` manifest field ignored (intentional typo in game's own schema; new mods do not need to include it)

### v1.x series
- Incremental feature additions: WarpData resolver, perk injector, blueprint tab injector, smelting recipe injector, ProducedCards normalizer, AlwaysUpdate, GIF animation support, performance patches (SpriteTextureCache, OffScreenCardThrottle, SlotAssignmentLogSuppress, AmbienceArrayReuse, DOTweenPrewarm)

---

## License

Released under the [MIT License](LICENSE). Copyright (c) 2026 Jared Scott.

Bundled third-party libraries retain their original MIT-compatible licenses:
- **LitJSON** — public domain (reimplemented in `Stubs/LitJson/LitJsonStub.cs`)
- **UnityGifDecoder** — MIT License, copyright (c) 2020 3DI70R (see `THIRD_PARTY_LICENSES.md`)
