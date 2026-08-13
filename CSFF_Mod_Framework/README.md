# CSFF Mod Framework

Standalone modding framework for Card Survival: Fantasy Forest. Provides mod discovery (with automatic reclaim of Pikachu-ModLoader-tagged mods that actually ship framework-only content), JSON data loading, map cache indexing, WarpData resolution, sprite/audio/GIF loading, localization, perk injection, blueprint tab injection, smelting recipe injection, declarative drop and environment-improvement injection, spawn triggers, SelfTriggeredAction activation, world map node injection (including a cross-mod Portal Hub travel system), NPC agent diagnostics, ProducedCards normalization, AlwaysUpdate enabling, and a suite of performance patches. Mods only need C# for mod-specific logic (forage drops, vanilla card patching, custom Harmony patches).

## Status

- **Version:** 2.22.2
- **Game Version**: EA 0.66d (framework `lib/Assembly-CSharp.dll` refreshed and rebuilt clean
  against the live EA 0.66d game assembly on 2026-08-10 — decompile + VanillaIds registry
  regenerated same day; in-game Harmony patch-apply verification still pending)
- The other 9 in-house mods have not yet been individually re-verified against EA 0.66d — most
  ship their own separate compile-time `lib/Assembly-CSharp.dll` or NStrip'd variant (not shared
  with the framework's), and several NStrip copies remain months-stale pending regeneration with
  the user's external NStrip tool.

## What Changed in 2.0.0 (2026-04-26)

The framework is now **standalone** — it no longer ships compatibility stubs for legacy external runtimes and no longer supports third-party mods that hard-depend on them.

- Legacy compatibility DLLs are **not** in the deploy output.
- Third-party mods that hard-depend on removed external runtimes will not load with this framework.
- The legacy compatibility stubs have been removed from the repository entirely. They are not built or deployed.
- All in-house content mods now declare a `SoftDependency` on `crispywhips.CSFFModFramework` for load ordering and no longer reference any Pikachu GUID.
- The legacy `ModLoaderVerison` manifest field is ignored by this framework; existing manifests may keep it, and new framework-based mods do not need it. **Superseded by 2.5.1+/2.11.1** — the field's mere *presence* now affects mod-discovery behavior when a Pikachu ModLoader/ModCore install is detected (see "Mod discovery" under "What the Framework Handles" below). Framework-native mods should still omit it.

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
| Performance | `OffScreenCardThrottleFrames` | `3` | Run throttled cards 1-in-N frames (clamped to [2, 10]) |
| Performance | `DOTweenTweenerCapacity` / `DOTweenSequenceCapacity` | `1000 / 200` | Pre-warm DOTween pool to avoid mid-session GC spikes |
| Performance | `CatchUpTickCap` | `1344` | Max game ticks (15 in-game min each) re-simulated when entering an environment; caps the travel freeze on long-unvisited locations. `0` = vanilla unbounded |
| Performance | `DOTweenQuietLogs` | `true` | Downshift DOTween log verbosity to ErrorsOnly |
| Performance | `SlotAssignmentLogSuppressEnabled` | `true` | Strip per-frame `Debug.LogWarning` spam from `DynamicLayoutSlot.AssignCard` |
| Performance | `AmbienceArrayReuseEnabled` | `true` | Reuse a cached `float[3]` in `AmbienceImageEffect.Update` instead of allocating per frame |
| Wildlife | `WildlifeRaidsEnabled` | `false` | Opt-in: once per in-game day, roll for wildlife to spoil food in unguarded `tag_NotSafeFromAnimals` containers |
| Wildlife | `WildlifeRaidDailyChance` | `0.35` | Daily probability when enabled |
| Wildlife | `BearRaidChance` | `0.5` | Probability (0–1) that a bear encounter also triggers a raid on nearby open containers; sealed containers are always safe |
| Wildlife | `WildlifeRaidStressPenalty` | `2` | Stress added on a successful raid |

ConfigurationManager is recommended for an in-game UI.

## In-House Mods (Same Author)

| Mod | Plugins Folder | Version | Description |
|---|---|---|---|
| Advanced Copper Tools | `Advanced_Copper_Tools` | 1.11.5 | Copper metalworking, wheelbarrow, bathtub, stove, lantern, oil chain, tea kettle, tea blending station, copper chest |
| Community Mod Chest | `Community_Mod_Chest` | 1.10.1 | Community-suggested content: apparel, weapons and armor, 39 character-creation traits, pottery, decorations, fishing gear, and a four-location village area east of the River Clearing |
| Herbs and Fungi | `Herbs_And_Fungi` | 1.9.3 | Herbalism, mushroom foraging, hemp farming, oil press, pickle fermentation, drying racks, medicinal teas, 15 perks |
| Sirus23 Mod Collection | `Sirus23_Mod_Collection` | 1.3.3 | Three animal companions (wolf, fox, owl), full sheep husbandry chain, and a felt-working pathway |
| Water Driven Infrastructure | `Water_Driven_Infrastructure` | 1.8.0 | Water wheels, sawmills, grinding mills, ore sluices (river/lake adjacent) |
| Quick Transfer | `Quick_Transfer` | 1.7.1 | Shift/Ctrl/Ctrl+Shift+Right-Click multi-card transfer with live preset indicator |
| Repeat Action | `Repeat_Action` | 1.6.2 | Repeat last action with configurable keybinds and safety limits |
| Skill Speed Boost | `Skill_Speed_Boost` | 1.9.2 | Per-skill XP multipliers, difficulty profiles, staleness decay, synergies, level scaling |
| Mod Update Manager | `Mod_Update_Manager` | 2.1.2 | Nexus Mods update checker with in-game UI (F3), plus a one-click installer/updater for this whole mod family |

Every in-house mod declares `[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]` for load ordering. None are truly framework-independent any more: Quick Transfer, Repeat Action, and Skill Speed Boost were originally pure-BepInEx QoL mods, but all three now call into the framework's Tier 1 utility API (`Api.Reflect`, `Api.CardUtil`, `Api.StatAccess`) for at least part of their core logic (QT's card-click reflection lookup; RA's card-identification helpers; SSB's staleness/area-familiarity/morning-bonus patches) — they will still load without the framework present, but that code path throws if it's missing. Mod Update Manager has no runtime dependency on the framework or any other mod; it only recognizes them by name/folder for Nexus tracking and its bundled-suite installer (see its own README).

### Dependency Graph

```
CSFFModFramework (base — no dependencies)
 ├─ soft: HerbsAndFungi
 ├─ soft: QuickTransfer
 ├─ soft: Sirus23_Mod_Collection
 ├─ soft: RepeatAction
 ├─ soft: SkillSpeedBoost
 ├─ soft: AdvancedCopperTools
 │   └─ soft: HerbsAndFungi (optional — enables the Render Hemp Seed Oil recipe)
 ├─ soft: Community_Mod_Chest
 │   └─ soft (functionally required for River Bridge / Market Stall / Academy Armorer course): AdvancedCopperTools
 └─ soft: WaterDrivenInfrastructure
     └─ soft (enhanced by): AdvancedCopperTools (fasteners/Workshop output prefer ACT's items when installed, WDI-native otherwise — no mod in this repo has a hard cross-mod dependency)

Mod_Update_Manager — standalone, zero dependencies (bundles copies of the mods above for its Install & Update tab, but does not require any of them to run)
```

`Advanced Copper Tools` is the only content mod other in-house mods build directly on top of — see its own README's "Compatibility" section for what depends on it.

## What the Framework Handles

- **Mod discovery** — scans `BepInEx/plugins/` two levels deep for `ModInfo.json`. When a Pikachu ModLoader/ModCore install is detected, mods carrying the `ModLoaderVerison` manifest field are normally skipped (that loader owns them) — **unless** the mod ships a framework-exclusive declarative file (`BlueprintTabs.json`, `SmeltingRecipes.json`, `DropInjections.json`, `InjectImprovementInto.json`, `WorldMap/MapNodes.json`, `EncounterGuards/*.json`, `Quests.json`, `Characters.json`, `MapMod.json`), in which case it's reclaimed and loaded through the framework instead (since 2.11.1). Same-named duplicate mod folders are deduplicated by picking the one with more content files, not the newer mtime.
- **Map cache indexing** — parses declared/generated `Data/*Map*.json` files once and exposes them through `MapCacheRegistry`
- **JSON data loading** — from each mod's top-level content directories (folder name = type name, matching the vanilla JSON export layout): `CardData`, `CharacterPerk`, `PerkGroup`, `GameStat`, `SpiceTag`, and (since 2.1.0) `FlavourTag`, `NPCStat`, `NPCDuty`, `NPCHidingGroup`, `NPCAgent`, `Encounter`, `SelfTriggeredAction`, `Objective`, `QuestLog`, `GameModifierPackage`, `PlayerCharacter`, `CookingRecipeGroup`, `ConstructionCardGroup`, `BookmarkGroup`, `LocalTickCounter`. Any other ScriptableObject type loads generically from `ScriptableObject/<TypeName>/`. **Scope note:** the 2.1.0 types are loaded and registered in the game's UID registry, and WarpData references to/from them resolve — but most are not yet *activated* (no NPC agent spawning, no quest attachment, no character-select injection). Exception: **`SelfTriggeredAction` is fully active since 2.2.0** (see below). The remaining injectors are planned in later phases; today those types are usable wherever vanilla code resolves them by GUID reference. Feature-detect via `Api.Framework.SupportsContentType(...)`.
- **WarpData resolution** — UniqueID/GUID references, runtime tag creation, nested array expansion, both array and `List<T>` field types
- **Sprite / Audio / Localization** — loads from each mod's `Resource/` and `Localization/` folders
- **Perk injection** — adds perks to the target `PerkGroup` and removes them from groups the engine auto-placed them into (e.g., Sex/Romance). `"CharacterPerkPerkGroup": "None"` (since 2.11.0) keeps a perk out of every group instead — for perks granted only at runtime via `AddedInRunPerksWarpData` (e.g. CMC Academy course "Graduate" perks)
- **Blueprint tab injection** — reads each mod's `BlueprintTabs.json` and injects entries by `LocalizationKey`
- **Smelting recipe injection** — reads each mod's `SmeltingRecipes.json` and injects `CookingRecipes` into vanilla forges/furnaces with duplicate detection
- **Drop injection** — reads each mod's `DropInjections.json` and appends `CardDrop` entries to matching `DismantleAction.ProducedCards` on location cards, matched by exact UID, `CardName.LocalizationKey` substring, or `CardTag` name; idempotent and cross-mod-soft-dependency safe (missing referenced cards are skipped quietly)
- **Environment improvement injection** — reads each mod's `InjectImprovementInto.json` (`[{ "TargetEnvUID": "<CT8 UID>", "ImprovementUID": "<CT10 UID>" }]`) and appends the CT10 improvement to the target CT8 location card's `EnvironmentImprovements` array; idempotent
- **Trading value injection (since 2.18.0)** — reads each mod's `TradingValues.json` (flat object map `{ "<CardData UniqueID>": <number> }`; `_`-prefixed keys are comments) and writes each value onto `CardData.TradingValue` at load, so NPC trading isn't full of vanilla's 0-cost items. Applies unconditionally to listed cards; later mods win UID conflicts (Warn logged); missing UIDs are skipped quietly (optional sibling mods); targeted `GameSourceModify/` patches still override (they run later)
- **Spawn triggers** — reads each mod's `CardData/Trigger/*.json` (ModCore-compatible schema) and periodically spawns a card on the player's board at a configurable chance/frequency/cap, driven by the framework's own `Update` loop (`Triggers/TriggerService.cs`) — the simpler alternative to `SelfTriggeredAction` for basic day-timer spawns
- **SelfTriggeredAction activation (since 2.2.0)** — mod STAs in `SelfTriggeredAction/*.json` are discovered by `GameManager` at run start automatically (registration into `AllData` is sufficient); the framework validates them at load (missing triggers, unresolved stats, save-state ID problems) and logs a one-line run-start confirmation. Authoring guide + decision table vs. the simpler `CardData/Trigger/*.json` spawn system: `Documentation/CSFF_Patterns.md` § SelfTriggeredAction
- **NPCAgent validation + diagnostics (since 2.3.0)** — at load time, `NPCAgentActivationService` validates every mod-owned `NPCAgent`: normalizes null arrays (`AgentStats`, `AgentDuties`, `Interactions`, `AgentActions`) that would NRE during GameManager initialization, and warns on missing `AgentName`. At each run start (via `OnGMInitialized`), the service surveys GameManager for NPC-typed fields/properties, checks for `NPCManager`/`WorldNPCManager` components, and reports whether mod agents appear in any discovered agent list — logging each finding as a `[DIAGNOSTICS]` line. This confirms whether `AllData` registration is sufficient or whether a future `NPCAgentInjector` must explicitly append agents. See "NPCAgent Diagnostics" section below.
- **WorldMap node injection (since 2.3.0)** — mods may ship `WorldMap/MapNodes.json` to add new travel locations to the in-game world map. The framework reads these at load time, resolves each environment UID to its CT4 `CardData`, creates the appropriate `MapEnvData` entries, and appends them to the `WorldMapData` singleton. Connections are bidirectional — declaring A→B automatically creates B→A so travel works in both directions. See "Adding a Map Location" section below.
- **Portal Hub System (since 2.8.0)** — a mod ships a root `MapMod.json` (`{ "WorldName": "...", "EnvironmentUID": "<CT4 UID>" }`) and the framework registers it as a travel destination on the shared, build-anywhere Portal Hub CT2 (`csffmfwportalplaced`) — no mod C# required. The Portal Kit is granted at run start by the framework's own "Arcane Wayfinder" perk. Each registered world automatically gets a "Travel to [WorldName]" button on the placed Hub and a "Return to Portal" exit card (`csffmfw_hub_exit`) injected into its CT4. This is the one supported cross-mod world-switching mechanism (a legacy `WorldMap/HubPortals.json` fixed-location schema was retired in 2.8.0).
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
- **CatchUpTickCap** — caps `ChangeEnvironment`'s per-game-tick catch-up replay at 14 in-game days (configurable); prevents the 70+ s "Not Responding" freeze when entering a location not visited in months on old saves

## Architecture

- `CSFFModFramework.dll` — the only framework binary the loader actually executes
- `Loading/LoadOrchestrator.cs` — orders ~30 load passes behind timing/try-catch isolation per phase. Abbreviated chain: ModDiscovery → MapCacheLoader → `Database.InitFromGame` → Sprite/GIF loading → `JsonDataLoader` → `ForeignInstanceReconciler` → `WarpResolver` → null-ref/PassiveEffect/ProducedCards/AlwaysUpdate normalization → Smelting/Drop/Improvement injectors → Trigger/EncounterGuard loaders → STA/NPCAgent validation → MapMod/WorldMap/Portal injection → `GameSourceModifier` → `SpriteResolver` → Localization/Audio/AssetBundle loading → Perk/Quest/Character injectors → `BlueprintInjector`. See the file itself for the authoritative, fully ordered phase list.
- `Discovery/ModDiscovery.cs` / `Discovery/ModManifest.cs` — mod probing, ModLoader-native skip/reclaim decision (`HasFrameworkOnlyMarkers`), content-count dedup
- `Injection/DropInjector.cs` / `Injection/ImprovementInjector.cs` / `Injection/TradingValueInjector.cs` — declarative `DropInjections.json` / `InjectImprovementInto.json` / `TradingValues.json` processing
- `Triggers/TriggerService.cs` — polls and fires mod `CardData/Trigger/*.json` spawn triggers from `Plugin.Update`
- `Portal/MapModLoader.cs`, `Portal/PortalRegistry.cs`, `Portal/PortalService.cs` — Portal Hub System: `MapMod.json` parsing, world registry, per-mod travel DA injection + `ActionRouter` handlers
- `Injection/NPCAgentActivationService.cs` — load-time validation + run-start diagnostics for mod NPCAgents (Phase 3)
- `Loading/WorldMapLoader.cs` — parses each mod's `WorldMap/MapNodes.json` using MiniJson
- `Injection/WorldMapInjector.cs` — appends parsed map nodes to the `WorldMapData` singleton with bidirectional link enforcement (Phase 4); delegates capacity-stat, connection-gate, sealable-gate, and conditional-drop handling to `Injection/EnvCapacityPatcher.cs`, `Injection/ConnectionGateService.cs`, `Injection/SealableGateService.cs`, `Injection/ConditionalDropService.cs`
- `Patching/` — Harmony patches grouped by concern (BugFixes, Performance, Diagnostics, BpFixPatch, GameLoadPatch, LocalizationPatch)
- `Wildlife/WildlifeRaidService.cs` — opt-in raid mechanic
- `Stubs/LitJson/` — in-tree LitJSON v0.19.0.0 source built into the bundled `LitJSON.dll`

Mods only need C# for **mod-specific logic**: custom action interception, forage drop injection, vanilla card patching, custom Harmony patches on gameplay methods. Anything in the "What the Framework Handles" list above is automatic.

## Key File Locations

- Vanilla game data dump: `Documentation/GameData/CSFF-JsonData_EA_0-65/`
- GUID lookups: `Documentation/GameData/CSFF-JsonData_EA_0-65/UniqueIDScriptableGUID/`
- LitJSON source: `Stubs/LitJson/LitJsonStub.cs` → `LitJSON.dll` (v0.19.0.0)

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

### Advanced Node Fields (not exhaustively documented here)

`MapNodeDefinition` (`Loading/WorldMapLoader.cs`) supports several additional per-node fields beyond the table above, added since framework v2.9: `CapacityStats` (per-stat SpecialDurability1–4 overrides on the cloned CT8 — Trees/Overgrowth/Foraging/Fertility caps and regen rates), `ConnectionGates` (declarative `ImprovementBuilt`/`PerkEquipped` gates that show/hide connections and their travel DAs, replacing per-mod `VillagePathUnlockPatch`-style Harmony patches), `SealableGates` (negative/challenge gates that seal on a trigger and reopen when a challenge card is cleared — retires ACT's `ACTCaveGatePatch` and H&F's `HFForestGatePatch`), `ConditionalDrops` (runtime-conditional board spawns for seasonal crops, perk-unlocked NPCs, quest items), `CardImage`, `ExtraDropUIDs`, `VanillaExits`, `StripAllInheritedDrops`, and `StripLegacyBoardUIDs`. See the doc comments on `MapNodeDefinition` and its nested `*Definition` types for the full schema, and `CSFFModFramework/CLAUDE.md` for behavioral notes.

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

## Instant Blueprint Unlock — `BlueprintsFullUnlock`

Any `DismantleAction`, `DialogAction`, or `Objective.OnCompleteActions` entry can instantly teach the player a blueprint — no C# required. Populate the field in mod JSON using WarpData:

```json
"BlueprintsFullUnlockWarpData": ["your_blueprint_uid"]
```

The array is resolved at load time. When the action executes, the game spawns the blueprint model card into the player's current environment (if not already held) and marks it **Available** immediately — bypassing the research timer.

### Use cases

**Schematic scroll** — a consumable item that teaches a blueprint when read:

```json
{
  "UniqueID": "cmc_schematic_copper_chest",
  "CardType": 0,
  "CardName": { "DefaultText": "Copper Chest Schematic" },
  "DismantleActions": [
    {
      "ActionName": { "DefaultText": "Read" },
      "DaytimeCost": 0,
      "UseMiniTicks": 1,
      "ReceivingCardChanges": { "ModType": 3 },
      "BlueprintsFullUnlockWarpData": ["act_bp_copper_chest"]
    }
  ]
}
```

`ModType: 3` destroys the scroll. The blueprint lands in the crafting journal as Available — no research step.

**NPC teaching** — inside a `DialogAction` on an `NPCAgent` Interaction, the same field teaches the blueprint when the player picks that dialog option.

**Quest reward** — inside `OnCompleteActions` on an `Objective`, the field teaches the blueprint when the objective completes.

### Behaviour details

- `BlueprintsFullUnlockWarpData` is always a JSON **array** (even for one entry).
- The blueprint model card **spawns on the board** in the player's current environment — the same visual as finding one in the wild.
- Cascades `AlsoUnlocks` declared on the blueprint — sibling blueprints are also marked Available.
- CT10 improvements listed here are added to `UnlockedImprovements` (same routine, different branch).
- This path **skips research entirely**: state goes straight to `Available`, not `Researching`.
- Does not require the player to hold any gate item — fires unconditionally when the action executes.

### Authoring guide

Full pattern with comparison table (schematic scroll vs. normal research vs. `StartUnlocked`): `Documentation/CSFF_Patterns.md` § Instant Blueprint Unlock.

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

## Additional Utility API (Tier 3 — v2.10.0)

Further reflection/state-access consolidation under `CSFFModFramework.Api`, replacing near-identical scaffolding independently duplicated across CMC, Sirus, ACT, and H&F.

| API | Purpose |
|---|---|
| `Api.CardFinder` | Cached whole-scene `InGameCardBase` lookup (`AllCards()`, `Find`/`FindAll` by UID or predicate), invalidated automatically when `GameManager.AllCards.Count` changes; `Invalidate()` for in-place CardModel swaps that don't change the count |
| `Api.StatAccess` | `GetCurrentValue`/`SetCurrentValue`/`ModifyCurrentValue`/`GetMaxValue`/`GetUniqueId` on a live `GameStat` instance, with property-then-field fallback across observed runtime shapes |
| `Api.RecipeInjector` | Generalized `CookingRecipe` injection onto a station's `CookingRecipes` array from a `RecipeSpec` (compatible cards/tags, duration, cooker/ingredient mod types) — replaces ACT's `VanillaFireKettlePatch` and H&F's tendon-drying recipe injection |
| `Api.ContainerSort` | Reorders a container's `InventorySlots` in place by a chosen durability axis (Usage/Quality/Spoilage/Special1–4), ascending or descending, without changing item counts. *(No fleet mod currently calls this — available for a future container-sort UI.)* |
| `Api.BlueprintAlternates` | `AddAlternateIngredient(allData, primaryUid, alternateUid)` walks CT7/CT10 `BlueprintStages[].RequiredElements[]` and attaches a `CardTabGroup` alternate so a slot accepts either card — replaces ACT's `PatchNailInterchangeability`; also used by WDI to accept ACT's fasteners as optional alternates without a hard dependency |

## Quests & Characters (Gap Phase 5 — v2.5.0)

> ⚠️ **DO NOT enable vanilla QuestLog auto-injection without reading this.** Attaching a mod
> `QuestLog` to `PlayerCharacter.Quests` via `Quests.json` caused a **user-confirmed blueprint
> research reset on save load** in CMC 1.7.0 (2026-06-17) — the single worst failure mode this
> codebase warns about (see the "Blueprint Research Persistence" rule in the repo `CLAUDE.md`).
> The feature was disabled two days later and **its root cause was never diagnosed**. Because of
> this, `QuestInjector` is **hard-gated OFF by default since 2.17.0**: even a mod that ships a valid
> `Quests.json` attaches nothing unless the player explicitly sets
> `Quests/EnableQuestInjection = true` in the framework's BepInEx config, and the loader logs a
> warning explaining why it refused. Do not re-enable it on any shipping mod until the Open
> Unknowns in `Documentation/Retrospectives/questinjector-blueprint-reset-risk.md` are diagnosed
> with a controlled single-variable test on a disposable save (does blueprint research survive a
> save/reload cycle?). `Characters.json` / `CharacterRosterInjector` has no incident history and is
> a separate, lower-risk surface — but it has also never been real-world tested.

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

> **Status (v2.17.0)**: injectors are implemented and validated against the EA 0.65
> data model (PlayerCharacter.Quests holds QuestLog refs; Gamemode "CharacterList"
> holds the Fates/Ways rosters). `QuestInjector` is **shipped-but-hard-gated-OFF** after
> the CMC 1.7.0 blueprint-reset incident (above) — `Quests/EnableQuestInjection` must be
> set true or nothing attaches. In-game verification and the save-compat test
> (add character → save → remove mod → load) are pending — run them before shipping
> content that depends on these.

## Version History

### v2.22.2
- **Fixed `SpawnLocation > 0` ("outdoor only") spawn triggers firing inside caves and building
  interiors.** `TriggerService` now gates on new `GameQuery.IsOutdoors` (instanced environments OR
  `tag_Cave`/`tag_EnvCaveSystem`/`tag_EnvIndoors`/`tag_Env_BearCave`/`tag_Env_WolfCave`-tagged
  boards) instead of `IsInInstancedEnvironment` alone — fixes every mod's outdoor-only
  `CardData/Trigger/*.json` entry (surfaced via Sirus23's wild sheep/ram spawns).

### v2.21.1
- **New `SealTrigger`/`GateConditions` type `"Always"`** — unconditionally true, for a
  `SealableGates` entry that should be sealed by default for every player regardless of
  perk/build state (rather than only once some other condition is met). Added because ACT's cave
  walls and H&F's forest trail were gated on `PerkEquipped`, leaving the passage silently open on
  any save — including existing saves installing the mod — where the player never took that perk.

### v2.19.1
- **Recompiled against the actual EA 0.66 game assembly.** The prior "Prepping for EA-0.66"
  release (2.19.0) only bumped version strings — `lib/Assembly-CSharp.dll` was still the EA 0.65
  binary. Rebuilding against the real EA 0.66 assembly surfaced two signature breaks in the Animal
  system: `InGameNPCStat.SetStatValue(float)` became private (fixed by switching to the public
  `SetStatValueFromEditor(float)` wrapper, which carries no editor-only behavior despite the name),
  and `NPCAgentSpawnSettings.SpawnedAgent` became a private field (fixed via `AccessTools.Field`
  reflection on the boxed struct). Release build now succeeds 0 errors/0 warnings against EA 0.66.
  In-game Harmony patch-apply verification still pending — see CHANGELOG.md for the full test plan.

### v2.19.0
- **`SealableGates`/`ConnectionGates` gain a `"Season"` condition type** (`GateConditions[].Type`/
  `SealTrigger.Type` = `"Season"`, `UID` = a season name, compared against `GameQuery.CurrentSeason`).
  Built for Community Mod Chest's winter-sealed Village roads.
- **Fixed**: a `SealableGates` gate whose `SealTrigger` goes false could stay showing LOCKED
  forever — `OnPoll` now tracks each gate's trigger-active state and forces one final
  `EvaluateAll()` on a true→false transition. No behavior change for existing monotonic-trigger gates.
- **Fixed**: `ResealCondition: {"Type":"TimerRegrowth"}` could never actually reseal within one
  continuous play session — `CheckResealTimer` no longer early-returns on `ClearedThisSession`
  before its own elapsed-day math.

### v2.18.2
- **Cleared cave passages no longer re-collapse after all veins are depleted.** The old-save cleanup in `WorldMapInjector.PreCreateCloneEnvSaveData` ("stale Exit-card fix") was wiping a clone env's entire `EnvironmentsData` entry — including `CurrentlyBuiltImprovements`, where `SealableGateService` stores permanent "wall cleared" markers — whenever no expected vein cards were found on the saved board. That correctly handled pre-strip old saves with inherited Exit cards but no veins, but it also fired for legitimately fully-depleted caves, erasing the cleared-passage markers and respawning every collapsed rock wall on next load. Fix: the stale-Exit wipe now also requires a `StripLegacyBoardUIDs` card to be present before removing the entry, confirming it's a genuinely contaminated old save rather than a depleted-but-valid cave.

### v2.18.1
- **`EncounterGuards/*.json` now supports environment-based suppression** (`GuardEnvironmentUids`, alongside the existing `GuardCardUids`, evaluated as OR). The loader previously silently skipped any guard file lacking `GuardCardUids` — Community Mod Chest's village wildlife-suppression guard was inert as a result.

### v2.18.0
- **Declarative bulk trading-value repricing (`TradingValues.json`)** — a mod may ship a flat `CardData` UniqueID → number map at its mod root; `Injection/TradingValueInjector` writes each value onto `CardData.TradingValue` at load. Built because vanilla leaves ~65% of items/liquids priced at 0. Later mods win UID conflicts (logged); missing UIDs are skipped quietly; a targeted `GameSourceModify/` patch still overrides a bulk price.

### v2.17.2
- **NPC-interaction button text no longer overflows its border** (Talk/Trade/Commissions row, dialog answer buttons). New `Patching.BugFixes.NPCButtonTextFit` postfixes `TooltipButton.Setup`, `DialogAnswerButton.Setup`, and `NPCInspectionPopup.SetupActions` to enable TMP shrink-to-fit auto-sizing the first time each button's text is seen. Covers every `IndexButton`-family button fleet-wide, not just NPC popups.

### v2.17.1
- **`ConnectionGateService` now warns** when a gate with `HideTravelDA: true` unlocks while stripped travel DAs sit in its restore cache but `RestoreDAOnUnlock` is false — previously a silent `MapNodes.json` authoring error that left a permanent red-X compass slot with no travel action. Found via CMC's Village Path gate.

### v2.17.0
- **Blueprint-container save-load freeze fixed** (`BlueprintContainerSaveLoadFix.ProcessOneCard` now yields through the drained vanilla coroutine instead of spin-draining it — a synchronous `while (MoveNext()) {}` on a coroutine that waits on real Unity frames hung the game solid on any save with a blueprint container missing a contained blueprint). Confirmed in-game 2026-07-19.
- **`QuestInjector` hard-gated OFF by default** (`Quests/EnableQuestInjection` BepInEx flag, default false) after the CMC 1.7.0 blueprint-research-reset incident — see the ⚠ note under "Quests & Characters" and `Documentation/Retrospectives/questinjector-blueprint-reset-risk.md`.

### v2.16.4
- **Deferred clone-ref resolution** (`WorldMapInjector.ResolveDeferredCloneRefs`): clone-node location cards can now reference other clone UIDs (blueprint gates, contained blueprints) without those refs resolving to null. The clone env/location pair is created during map-node prep, after WarpResolver runs, so a new LoadOrchestrator phase re-walks the deferred refs. Fixes clone location cards showing an empty "Have :" tooltip. Memory `reference_worldmap_clone_ref_deferred_resolve`.

### v2.14.1
- **Non-UID registration now distinguishes ModCore coexistence from real collisions**: confirmed in-game (Pikachu ModCore independently scans every plugin's `ScriptableObject/<Type>/` folder regardless of manifest tags, and had already created its own `WeaponMove`/`DamageType` instances before our own `JsonDataLoader` processed the same files). `Database.RegisterTypedSO`'s unconditional overwrite already made our instance canonical deterministically (nothing runs between `JsonDataLoader.LoadAll`'s registration and `WarpResolver.ResolveAll` that could touch `Database.AllScriptableObjectDict` for these types), so this was never a correctness bug — but the log couldn't tell a benign ModCore duplicate apart from a real same-name collision between two of OUR OWN mods. `JsonDataLoader` now tracks names it registers in the current pass: a collision against a name it already registered itself logs `Warn` (rename it — real mistake); a collision against a name it has NOT seen yet this pass (i.e. ModCore or another external loader got there first) logs `Info` ("already registered by another loader ... now canonical, no action needed"). No behavior change, pure signal-quality — matches the existing `[LoaderCoexistence]` framing already used for UID-type duplicates.

### v2.14.0
- **`GameSourceModify` non-UID target fallback**: `GameSourceModifier.ApplyAll`'s standard target resolution now falls back to `Database.AllScriptableObjectDict` (by name) when `GameRegistry.GetByUid` finds nothing — the same two-tier lookup `WarpResolver.Lookup` already uses for non-`UniqueIDScriptable` types. This lets a `GameSourceModify/<Name>.json` patch an EXISTING vanilla (or another mod's) `WeaponMove`, `DamageType`, `CardTag`, etc. in place — every card that already holds a reference to that shared instance sees the change. Previously `GameSourceModify` only worked on `UniqueIDScriptable` targets (`CardData`, `CharacterPerk`, `GameStat`, ...); patching a non-UID object required either shipping a same-named `ScriptableObject/<Type>/` file (which creates a disconnected NEW instance instead of mutating the original — no-op for anything already referencing the vanilla one) or a custom Harmony postfix. No changes needed elsewhere — `ApplyPatch`'s `JsonUtility.FromJsonOverwrite` + `WarpResolver.Walk` re-resolve already operate generically on any `UnityEngine.Object`. Completes the weapon/combat-modding surface started in 2.13.0: new attacks, new weapons, custom damage types, and now edits to existing attacks are all JSON-only.

### v2.13.0
- **Non-UID ScriptableObject registration**: `JsonDataLoader.LoadAll` now registers every non-`UniqueIDScriptable` object it materializes from a `ScriptableObject/<Type>/*.json` folder (via `Api.ContentRegistry.Register`) into `Database`'s per-type and flat name indexes — not just `UniqueIDScriptable` types. Previously, only `CardData`/`CharacterPerk`/etc. (all `UniqueIDScriptable`) were registered; plain `ScriptableObject` types like `WeaponMove` were parsed and instantiated but never made discoverable by name, so any `*WarpData` field resolving one by name (e.g. `CardData.WeaponMovesWarpData`) silently failed with zero log output. Logs a `Warn` if a mod's object name collides with an existing registration (e.g. forgetting to rename a copied vanilla asset like `SpearThrow`), and an `Info` summary line with the count registered. No changes were needed in `WarpResolver` — its `*WarpData`/`*WarpType` walk already resolves array fields like `WeaponMoves` generically by reflecting the base field name; this was purely a missing registration step. Unblocks modders adding custom weapon attacks (`WeaponMove` assets) and any other non-UID `ScriptableObject` type shipped via the generic `ScriptableObject/` folder.

### v2.12.0
- **`Api.BlueprintAlternates`**: new Tier 3 helper generalizing the alternate-ingredient pattern AdvancedCopperTools already used for iron/copper nail interchangeability (`AddAlternateIngredient(allData, primaryUid, alternateUid)`). ACT's own `PatchNailInterchangeability` now delegates to this shared implementation. Enabled WaterDrivenInfrastructure to drop its hard dependency on AdvancedCopperTools: WDI ships its own fasteners and uses this helper to accept ACT's originals interchangeably only when ACT is also installed — no mod in the repo has a hard cross-mod dependency anymore.

### v2.11.1
- **`ModDiscovery` reclaims mistagged framework-format mods**: a mod carrying the Pikachu `ModLoaderVerison` manifest field is normally skipped when a Pikachu ModLoader/ModCore install is detected (that loader owns it). `ModManifest.HasFrameworkOnlyMarkers` now checks whether the mod also ships a framework-exclusive declarative file (`BlueprintTabs.json`, `SmeltingRecipes.json`, `DropInjections.json`, `InjectImprovementInto.json`, `WorldMap/MapNodes.json`, `EncounterGuards/*.json`, `Quests.json`, `Characters.json`, `MapMod.json` — none of which ModLoader/ModCore's own loader reads) — if so, the mod is loaded through the framework instead of skipped. `ForeignInstanceReconciler` neutralizes the resulting duplicate `UniqueIDScriptable` instances ModLoader creates for it. Fixes third-party framework-format mods whose blueprint tabs/content silently never appeared because the mod was entirely skipped despite being authored for the framework.

### v2.11.0
- **`"CharacterPerkPerkGroup": "None"` opt-out**: `PerkInjector` now recognizes `"None"` as a token that keeps a perk out of every `PerkGroup` (invisible at character creation) instead of falling back to Situational — for perks granted only at runtime via `AddedInRunPerksWarpData` (e.g. CMC Academy course "Graduate" perks).

### v2.10.0
- **Centralization Tier 3 utility API**: `Api.CardFinder` (cached whole-scene `InGameCardBase` lookup, invalidated on board-count change), `Api.StatAccess` (GameStat current/max value get/set across runtime field-shape variants), `Api.RecipeInjector` (generalized `CookingRecipe` injection onto a station's `CookingRecipes` array). Replaces near-identical reflection scaffolding independently duplicated across CMC, Sirus, ACT, and H&F.

### v2.9.1
- Documentation-only release: added `CSFFModFramework/CLAUDE.md` internal developer notes. No functional changes.

### v2.9.0
- **`ConnectionGateService` rewrite**: fixes bidirectional DA strip/restore, value-type write-backs, node `HideFromInGameMap` toggling, and `IList` support (EA 0.65 `DismantleActions` is `List<T>`). New `_strippedDas` cache enables `RestoreDAOnUnlock: true` for gates that must re-enable travel buttons when opened.
- **`Api.WorldMap.ToggleEdge`**: hides/shows only the specific A↔B connection in WorldMapData — more precise than `RegisterGate` when gating the full target env would also affect unrelated connections (e.g. ACT's WaterfallCave↔TinCave edge vs Tin→Copper/Iron).
- **`Api.WorldMap.StripTravelDa` / `RestoreTravelDa`**: expose the DA strip/restore cache to mods for manual per-edge DA control.
- **`Api.WorldMap.EvaluateGates`**: public trigger for `ConnectionGateService.EvaluateAll` so mods can force re-evaluation after dig-marker writes or other state changes.
- **`RegisterGate` signature extended**: new `restoreOnUnlock` and `neighborCt8UID` parameters. Old callers (one positional argument) are unchanged.
- **`ConnectionGateDefinition`** gains `RestoreDAOnUnlock` field (default false) parsed from `MapNodes.json`.
- ACT `ACTCaveGatePatch` migrated to framework gates for Copper/Iron caves; WaterfallCave↔TinCave edge uses new `ToggleEdge` + `StripTravelDa`.
- CMC `VillagePathUnlockPatch` replaced by declarative `ConnectionGates` in `MapNodes.json` — ~580 lines of per-mod boilerplate removed.

### v2.8.0
- **Portal Hub redesign — portable, mod-exclusive, cabin-style isolation**: the Portal Kit can now be placed anywhere in the vanilla world (no fixed sacred site required). Each registered mod world maps to a CT4 `InstancedEnvironment` (same pattern as vanilla cabins) — players enter the mod's isolated space directly from the portal and exit via the auto-injected `csffmfw_hub_exit` card (`TravelToPreviousEnv`), which returns them to whichever vanilla environment the portal was placed in. Multiple mods coexist without WorldMap coordinate conflicts because mod maps are not WorldMap nodes; the portal is the only entry and exit point.
- **Hub Entrance at River Clearing removed**: `HubPortalInjector` no longer places the Crossroads entrance card in River Clearing's `CardsOnBoard`. All portal travel flows through the portable portal item. The `WorldMap/HubPortals.json` schema is retained for backward compatibility.
- **Auto-inject `csffmfw_hub_exit`**: `PortalService` now injects the framework's exit card into each registered mod CT4's `DefaultEnvCardDrops` automatically (idempotent — skips if already present). Mods do not need to ship the exit card themselves.
- **`AppendCardDrop` idempotency**: `HubPortalInjector.AppendCardDrop` now checks for duplicates before appending, preventing double-spawn of exit cards if a mod manually includes `csffmfw_hub_exit` in its CT4 JSON.
- **Wayfinder perk description updated** to reflect "place anywhere" mechanics.

### v2.7.6
- **Two-pass WorldMap node injection**: `WorldMapInjector.InjectIntoWorldMap` now registers all node `MapEnvData` entries in pass 1, then links connections in pass 2. Previously, a forward connection from node A to node B would log a spurious "ONE-WAY (no reverse link)" warning when B appeared later in `MapNodes.json` (B wasn't in the environments array yet, so the reverse link couldn't be added). Both directions were eventually added (B's own declared connection added both directions when B was processed), but the warning was misleading and the WorldMapData `ConnectsTo` array could contain a duplicate entry. Fixes the warning for Village→Wisp's Cabin in CMC and any future mod with mutual cross-references.

### v2.7.5
- **Instanced-environment guard in `WorldMapInjector`**: a `MapNodes.json` node whose `CloneOfEnvironmentUID` (or direct `EnvironmentUID`) resolves to an **instanced** environment (interior/instanced map — cabin/cave interiors, `CardData.InstancedEnvironment == true`) is now **refused with a loud `Log.Error`** instead of being injected. Such a node's `EnvID` is built with empty `ParentEnvs`, so `EnvID.GetRootEnv()` self-loops and `WorldMapData` distance/path calculation recurses infinitely → a **silent stack-overflow crash** on a later environment transition. Decompile-verified EA 0.65; root cause of `Documentation/Retrospectives/cmc-quest-injection-crash.md` (CMC's Wisp's Cabin cloned `Env_Cabin`). Modders must clone a non-instanced OUTDOOR environment for world-map nodes.

### v2.7.2
- **Travel DA injection**: `WorldMapInjector` now injects a reverse travel `DismantleAction` on the target CT8 location card for every connection declared in `WorldMap/MapNodes.json`. This is the fix for travel buttons not appearing — CSFF travel is DA-driven (`HasExplorationDirection=true`, `DroppedCard`=destination CT4), not WorldMapData-graph-driven. All prior map-node injection attempts worked but left no travel button on the reverse side.

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
- **Api.VanillaIds embedded registry**: 2,757 card + 592 stat GUIDs and 6 curated groups generated from EA 0.65 game data; `Development_Tools/Generate-VanillaIds.ps1` regenerates it and is wired into `/extract-latest-carddata`.
- **WildlifeRaidService data-driven**: open-storage container UIDs and the bear-encounter UID now come from `Api.VanillaIds` (hardcoded EA 0.65 values remain as fallback); inventory scans and day-rollover detection moved onto `Api.Inventory`/`Api.Gate`.
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
