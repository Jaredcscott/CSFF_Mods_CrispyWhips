# CSFF Mod Framework — Changelog

All notable changes to CSFFModFramework are documented here.

---

## [2.22.2] — 2026-08-12

### Fixed

- **`SpawnLocation > 0` ("outdoor only") spawn triggers still fired inside caves and building
  interiors.** `TriggerService`'s outdoor gating checked only `GameQuery.IsInInstancedEnvironment`,
  which covers player-built instanced structures (cabin, mud hut, cellar, enclosure, mine, coop)
  but not plain (non-instanced) CT4/CT8 boards tagged as caves or indoor spaces — walk-in caves and
  village-style building interiors. Reported via Sirus23's wild sheep/ram spawn triggers
  (`sh_tgr_wild_sheep_spawn`, `sh_tgr_wild_male_sheep_spawn`) still spawning underground and indoors.
  New `GameQuery.IsInIndoorOrCaveEnvironment` checks the current environment's `CardTags` against
  the known indoor/cave tag set (`tag_Cave`, `tag_EnvCaveSystem`, `tag_EnvIndoors`,
  `tag_Env_BearCave`, `tag_Env_WolfCave`); new `GameQuery.IsOutdoors` combines both checks.
  `TriggerService` now gates on `IsOutdoors` instead of `!IsInInstancedEnvironment` alone — fixes
  every mod's outdoor-only `CardData/Trigger/*.json` entry, not just Sirus23's.

## [2.22.1] — 2026-08-11

### Fixed

- **`ImprovementBuilt` connection gates (and SealableGates marker reads) silently stopped matching
  an environment once the player had left it.** `GameManager` re-adds an env's `EnvironmentsData`
  entry with a names-annotated `DictionaryKey` (`AddNamesToEnvKey` — e.g.
  `"2b19b942…(Env_River_ClearingOak_RiverClearing)"`) when the player leaves the env, but
  `CardUtil.IsImprovementBuilt`, `CardUtil.MarkImprovementBuilt`, and
  `SealableGateService.IsTargetEnvEntry` compared the bare env UID ordinally against that field
  (the entry's other match paths never fire: `EnvironmentID` doesn't exist on
  `EnvironmentSaveDataByReference`, and the dictionary is keyed by `EnvDictKey` structs, not
  strings). Result: a hand-built River Bridge recorded correctly in `CurrentlyBuiltImprovements`
  never unlocked the Community Mod Chest River Clearing → Village Path connection — the East
  compass slot stayed a red X with zero log output. The same mismatch made SealableGates marker
  *reads* (`IsMarkerSet`/`ReadMarkerDay`/`FindMarkerEntry`) miss save-loaded entries, a plausible
  contributor to the recurring "dug-open gates re-seal after reload" family. New
  `CardUtil.EnvKeyMatchesUid` normalizes the entry key with the engine's own
  `UniqueIDScriptable.RemoveNamesFromEnvKey` before comparing; all three call sites now use it.
  Perk-gated (`PerkEquipped`) connections were never affected, which is why the Village Pathfinder
  travel path passed earlier playthrough testing while the hand-build path had never worked.

## [2.22.0] — 2026-08-11

### Fixed

- **`AlwaysUpdateService` only skipped forcing `AlwaysUpdate=true` on mod-authored CT4/CT8 (env/
  explorable) cards; it never corrected one shipped with `AlwaysUpdate=true` in the first place.**
  A mod-authored env node with that flag true is `IndependentFromEnv`, so `ChangeEnvironment`
  re-homes it onto the player instead of leaving it behind — the same travel-softlock precondition
  `CardCloneService` already force-corrects for cloned nodes at clone time (Community Mod Chest
  shipped 7 interior CT8 locations with `AlwaysUpdate:true` for a month before the data was
  hand-fixed). `AlwaysUpdateService.EnableAll` now actively forces `AlwaysUpdate=false` on any
  mod-authored CT4/CT8 card found with it true, logging one `LogInfo` per corrected card so a
  mis-authored env node is visible at load. Vanilla cards and non-env mod cards are unaffected.
- **`PortalService` could strand a player with no way back after teleporting to a clone-env world
  that is also a registered portal destination.** The hub-exit skip for clone-env worlds (added so
  ACT's mining caves, which carry their own map-travel exit, don't get a redundant return card) applied
  to every clone env, including Community Mod Chest's `cmcEnvVillage` — a `MapMod.json` portal
  destination whose only route back toward vanilla is gated behind the river bridge or a trait perk.
  New `WorldMapInjector.CloneNodeHasOwnExit` distinguishes clone envs that already have their own
  authored way out (a `VanillaExits` compass exit, or a `Connections` entry to a non-clone
  environment) from clone envs whose only connections lead to sibling clone nodes. `PortalService`
  now seeds the `csffmfw_hub_exit` return card only into the latter. ACT's mining-cave portal
  behavior is unchanged — it already has its own exit and is unaffected by the narrowed skip.

## [2.21.3] — 2026-08-09

### Fixed

- **`WorldMapInjector.PreCreateCloneEnvSaveData`'s old-save reseed logic no longer discards
  `CurrentlyBuiltImprovements` when it force-removes a clone env's `EnvironmentsData` entry.**
  That field is where `SealableGateService`'s Marker model stores "cleared" state for every gate
  whose `MarkerEnvUID` is the env being reseeded (e.g. ACT's Tin Cave hub, which owns the marker
  for the Copper/Iron/Tin/Quarry cave-in walls). Three of the function's four contamination
  heuristics — the zero-card check, the stale-Exit-card check, and the `StripLegacyBoardUIDs`
  check — removed and recreated the entry outright with no awareness of that field, silently
  un-marking every already-dug passage sharing the hub and reproducing "cave connections/tunnels
  collapse again after reload" even on saves where the 2.21.1-era `GetSealableGateSlack` guard
  (which only covered the fourth, count-based heuristic) had never fired. Fix snapshots
  `CurrentlyBuiltImprovements` before any removal and restores it onto the recreated entry,
  independent of which heuristic triggered the reseed.

## [2.21.2] — 2026-08-09

### Fixed

- **`ModManifest.HasFrameworkOnlyMarkers` now recognizes a `GameSourceModify/` bulk-match patch
  (`MatchTagWarpData`/`MatchTypeWarpData`) as framework-exclusive content.** A `ModLoaderVerison`-tagged
  mod using this framework extension — real Pikachu ModLoader/ModCore's own GameSourceModify only
  supports single-UID targeting — was being skipped by `ModDiscovery`'s coexistence check whenever an
  actual Pikachu loader was also installed, on the assumption that loader owned it. Reported via a
  Nexus comment: a third-party freshness/spoilage-rate mod's `tag_Preservable`-wide patch only ever
  affected vanilla items, never any content-mod item, because it was never loaded by the framework at
  all in that configuration. The detection flag (`HasGSMTagOrTypeMatch`) already existed for an
  unrelated load-order optimization but was never wired into the reclaim check.

## [2.21.1] — 2026-08-09

### Added

- **New `SealTrigger`/`GateConditions` type `"Always"`** — unconditionally true, for a `SealableGates`
  entry that must be sealed by default for every player rather than only once a perk is equipped.
  Added because ACT's cave walls and H&F's forest trail were gated on `PerkEquipped`, which meant
  the passage was silently wide open for any player (including on an existing save that installs
  the mod) who never took the associated perk — the perk was never actually required to dig through
  the wall (that's tool-tag gated on the `CardInteraction` itself), only to make the wall exist at
  all. `Documentation/CSFF_Reference.md` §WarpData/gate condition table unaffected — see
  `Documentation/CSFF_Map_Travel_System.md`.

## [2.21.0] — 2026-08-09

### Added

- **`CatchUpTickCap` performance patch — fixes the long "Not Responding" freeze when traveling
  to a location not visited in a long time on old saves.** Vanilla `GameManager.ChangeEnvironment`
  replays its per-game-tick simulation step (`ApplyRates`) once for every tick (15 in-game
  minutes) elapsed since the destination environment was last visited, with no upper bound, in a
  single synchronous frame. Measured on a Year-4 save via `TrackingTimingDiagnostics`: a location
  ~405 in-game days stale replayed 38,891 catch-up ticks at ~1.9 ms each — a 73-second hard
  freeze on one travel (CPU scaled linearly with tick count; card count was flat across fast and
  slow travels, ruling out the load/classification passes). New Harmony prefix on
  `ChangeEnvironment` clamps the destination's `LastUpdatedTick` so at most
  `[Performance] CatchUpTickCap` ticks are re-simulated (default `1344` = 14 in-game days;
  `0` restores vanilla unbounded behavior). Elapsed time beyond the cap is skipped, not
  simulated — plant/tree growth is unaffected (vanilla jumps card-attached counters straight to
  the live value on the first catch-up tick); only rate-driven decay beyond the 14-day window is
  lost, and perishables fully spoil well within it. Logs one Info line whenever the cap engages.

## [2.20.7] — 2026-08-08

### Fixed

- **The Portal Hub System's "Return to Portal" button never appeared inside any registered mod
  hub** (CMC Village, ACT Metal Mines, H&F Foraging Forest). `csffmfw_hub_exit.json`'s Exit DA
  used vanilla `TravelToPreviousEnv: true`, which only produces cards when the CURRENT
  environment has a populated `ParentEnvs` chain (i.e. is an instanced env reached by an actual
  travel transition) — every registered hub destination is a normal non-instanced WorldMap node,
  so `CardAction.WillProduceCards()` always evaluated false there, and with `DaytimeCost: 0` and
  no other qualifying field, `WillHaveAnEffect()` also returned false — the button was silently
  invisible, zero log output, on every single portal trip since the feature shipped. Fixed by
  tracking the player's environment explicitly: `PortalService.StartEnvironmentTravel` now
  records the departure env UID before each outbound "Travel to [WorldName]" trip
  (`_returnEnvUid`, session-scoped), and a new `RegisterHubExitHandler` / `StartReturnTravel`
  drives the return trip through the same env-travel mechanism when "Return to Portal" is
  clicked. `csffmfw_hub_exit.json` now uses `AlwaysShow: true` instead of the non-functional
  `TravelToPreviousEnv`.

## [2.20.6] — 2026-08-08

### Fixed

- **Entering/exiting a building could permanently lock every action with "I can't do two
  things at once..." until the game was fully restarted.** Vanilla `WorldMapData.AddInstancedEnv`
  throws an unhandled `ArgumentException` when two independently-registered instanced
  environments (e.g. a player-built cabin/mine/mud-hut construction site) both compute the
  default map Coordinates `(0,0,0,0)` in the same session. `GameManager.ChangeEnvironment`
  calls `AddInstancedEnv` synchronously from its own coroutine, so the unhandled throw aborted
  `ChangeEnvironment` mid-flight and never reached the code that clears `GameManager.RootAction`
  — leaving the action-lock (`GameManager.PerformingAction`) stuck `true` forever. Added a
  Harmony finalizer (`Patching/BugFixes/ChangeEnvironmentCrashGuard.cs`) on
  `ChangeEnvironment`'s MoveNext that swallows this specific exception and logs full diagnostics,
  so the coroutine ends gracefully (the colliding env just doesn't get pre-registered that time,
  same graceful-skip vanilla already uses elsewhere in `WorldMapData`) instead of permanently
  locking the player out of every action.

## [2.20.5] — 2026-08-07

### Fixed

- **Mod-injected localization (CMC, etc.) stayed in Chinese even after switching the game's
  Language option to English.** `LocalizationLoader.GetLanguageSuffix()` detected the active
  language by reflecting `LocalizationManager.CurrentLanguage` and checking whether its
  `.ToString()` contained `"Chinese"`/`"Cn"` — but that field/property is an **`int` index** into
  `Languages[]` (0=English, 1=简体中文 in vanilla EA 0.66b), so a value's string form (`"0"`, `"1"`)
  can never contain those substrings. Detection silently fell through every time to a
  `CheckOptionsJson()` fallback that reads `Options.json` off disk — which the game only rewrites
  when the Options menu closes (`OptionsMenu.OnDisable → GameLoad.SaveOptions`), not when
  `ApplyLanguage()` fires `LocalizationManager.SetLanguage`. So mod strings kept loading from
  whatever language was last *saved* to disk instead of the live selection, while vanilla UI text
  (read directly from the game's own live `CurrentTexts` dict) switched correctly — the mismatch
  reported as "options say English but mod cards/dialog show Chinese." Fixed by comparing the
  reflected `int` value directly (`langIndex == 1 ? "Cn" : "En"`) instead of string-matching its
  `ToString()`, so `LocalizationLoader.ReloadForLanguage()` (postfixed onto
  `LocalizationManager.LoadLanguage`) now agrees with the live in-game language on every switch,
  with no scene-reload or Options.json save required.

---

## [2.20.4] — 2026-08-07

### Fixed

- **P4 (recurrence, now closed): `lib/Assembly-CSharp.dll` was still the EA 0.66 binary while the
  installed game had moved to EA 0.66b** (2026-08-06 23:20 update; confirmed via the game's own
  displayed version string as a lettered patch, not the major 0.67 bump first suspected — see
  memory `project_game_version`). Refreshed `lib/Assembly-CSharp.dll` to the live 0.66b binary
  (MD5 `197c590e6279de914dd68c76fc61d69d`) and regenerated `.decomp/` (939 files). Clean rebuild,
  0 errors/0 warnings — every compile-time-bound `GameManager` member the framework calls directly
  (`Awake`, `InitializeStatsAndActions`, `AllBlueprintModels`) still compiles, and every
  string-targeted reflection/`SafePatcher` patch target (`GameLoad.LoadMainGameData`,
  `BlueprintModelsScreen.Show`/`Toggle`, `ExplorationPopup.Setup`, `LocalizationManager.LoadLanguage`,
  `NPCInspectionPopup.SetupActions`, `GameManager.ChangeEnvironment`/`GiveCard`, etc.) was confirmed
  present by name in the fresh decompile. Consistent with 0.66b's small +21-file delta (no
  schema/GUID churn) — no signature breaks found this time, unlike the 0.65→0.66 jump (v2.19.1).

---

## [2.20.3] — 2026-08-07

### Fixed

- **Chinese localization never shipped for the framework's own strings** (Portal Hub, Portal Kit, Arcane Wayfinder perk). Created `Localization/SimpCn.csv` with all 13 rows.
- **`CSFFMFW_BpPortalKit_CardDescription` had an unquoted comma in `SimpEn.csv`**, so the CSV parser split it into three columns — the extra fragment (" sealed with animal fat. Assembles a portable Portal Hub kit.") was silently dropped, truncating the description shown to English-mode players. Quoted the field.

---

## [2.20.2] — 2026-08-06

### Fixed
- **`ConnectionGates.LockConditions` was never read from JSON.** 2.20.0 added the
  `LockConditions` field to `ConnectionGateDefinition` and the any-met-forces-LOCKED evaluation
  in `ConnectionGateService.BuildCondition`, but `WorldMapLoader.ParseConnectionGates` only ever
  parsed `"GateConditions"` — so a gate authored with `LockConditions` in `WorldMap/MapNodes.json`
  loaded with an empty list and the lock silently never applied. `ParseConnectionGates` now reads
  both keys through a shared `ParseGateCondition` helper (also reused by `SealTrigger`, which had
  a third copy of the same five-field read). First consumer: CMC's `cmcStatVillageCrime`
  banishment gate.

## [2.20.1] — 2026-08-06

### Fixed
- **`WorldMapInjector.ResolveDeferredCloneRefs()` now also re-walks non-UID ScriptableObjects**
  (`DialogLine`, `DialogScene`, `WeaponMove`, ...). Previously it only re-resolved `*WarpData`
  references on UID-keyed `CardData` cards after WorldMap clone nodes were created, so a dialog
  `Conditions.RequiredEnvironmentWarpData` pointing at a clone env UID (e.g.
  `cmcEnvVillageFarm`/`cmcEnvVillage`/`cmcEnvForagingForest`/`cmcEnvVillagePath`) never resolved —
  `RequiredEnvironment` stayed null, and `GeneralCondition.ConditionsValid` treats a null
  `RequiredEnvironment` as an unconditional pass. Symptom: multiple env-gated dialog answers
  sharing near-identical text (CMC Professor's "What is this place?", one per map node) all
  appeared simultaneously regardless of the player's actual location. See root `CLAUDE.md`
  §Debugging Discipline and memory `reference_worldmap_clone_ref_deferred_resolve`.

## [2.20.0] — 2026-08-06

### Added
- **`NPCCharacterPerk` JSON loading** (`NPCCharacterPerk/*.json`). The type is a
  `UniqueIDScriptable` via `CompletableObject` (same chain as `CharacterPerk`/`Objective`) and
  registers through the standard path. Unblocks shared-chassis NPC designs — one `NPCAgent`
  plus per-variant personality perk bundles, mirroring vanilla's Partner presets (first consumer:
  CMC Village Guards; closes that plan's R12).
- **`ConnectionGates.LockConditions`** (`WorldMap/MapNodes.json`): conditions that force a gate
  LOCKED when met (any one → locked), overriding `GateConditions`. Lets a negative axis (e.g. a
  crime/notoriety `StatThreshold`) close a connection an improvement/perk gate would otherwise
  hold open, without registering a second, conflicting gate on the same `ConnectionUID`.
- **Mid-run connection-gate re-evaluation**: gates now also re-evaluate on a 5 s
  `TickEvents.Interval` (state-change-guarded — no work unless a gate actually flips).
  Previously gates were only evaluated at run start and on improvement completion, so a
  `StatThreshold`/`Season`/`PerkEquipped` change mid-run did not take effect until reload.

## [2.19.1] — 2026-08-06

### Fixed
- **Recompiled against the actual EA 0.66 game assembly** (`lib/Assembly-CSharp.dll` was still the
  EA 0.65-era binary — the "Prepping for EA-0.66" commit only bumped version strings and shipped
  unrelated fixes, it never replaced the compile-time reference DLL). Rebuilding against the real
  EA 0.66 assembly surfaced two genuine signature breaks in the Animal system:
  - **`InGameNPCStat.SetStatValue(float)` is no longer public.** EA 0.66 made it a private
    `IEnumerator SetStatValue(float, NPCStatModifierTypes)`. `AnimalLifecycleTicker.cs`'s 11 direct
    stat writes (exists/blood/respawn-timer sets) now go through the public
    `SetStatValueFromEditor(float)` wrapper instead — despite the name, it carries no editor-only
    behavior (confirmed by decompile: it's a one-line `StartCoroutine(SetStatValue(value,
    NPCStatModifierTypes.Permanent))` forwarder), so it's the correct runtime replacement.
  - **`NPCAgentSpawnSettings.SpawnedAgent` is no longer a public field.** `SpawnRegistrar.cs`'s
    spawn-queue injection (dedup check + new-entry construction) now goes through reflection
    (`AccessTools.Field`) on the boxed struct instead of direct field access — the public `GetAgent`
    property can't be used as a substitute since it has no setter and falls back to a preset's
    `TemplateAgent`, which isn't the semantics the dedup check needs.
  - Both were confirmed by an actual `dotnet build` failure against the correct EA 0.66 DLL (13
    compile errors), not by static signature diffing alone. Release build now succeeds with 0
    errors, 0 warnings against the real EA 0.66 assembly.
  - **Runtime verification still pending** — a clean compile confirms these two call sites, not
    every Harmony `[HarmonyPatch(typeof(...))]` target elsewhere in the framework. Launch the game
    with this build and check `LogOutput.log`/`Player.log` for `HarmonyLib` patch-apply exceptions
    or `MissingMethodException` during plugin `Awake` before treating the framework as fully
    EA-0.66-verified.

---

## [2.19.0] — 2026-08-05

### Added
- **`SealableGates`/`ConnectionGates` gain a `"Season"` condition type.** `GateConditions[].Type`/
  `SealTrigger.Type` now accepts `"Season"` (`UID` = a season name, "Spring"|"Summer"|"Autumn"|
  "Winter", case-insensitive), compared against `GameQuery.CurrentSeason`. Built for Community
  Mod Chest's winter-sealed Village roads, but usable by any mod wanting a connection or
  challenge gate to key off the current season.

### Fixed
- **A `SealableGates` gate whose `SealTrigger` goes false could stay showing LOCKED forever.**
  Every trigger shipped before now was monotonic (`PerkEquipped`/`ImprovementBuilt` only ever go
  false→true once in normal play), so `SealableGateService.OnPoll` skipping a gate's entire body
  once its trigger read false never mattered — nothing else was still calling
  `ConnectionGateService.EvaluateAll()` to pick up the change. A `"Season"` trigger legitimately
  flips true→false→true every year, and without a fix, a road nobody dug through before the
  season ended would never reopen (short of a full game restart). `OnPoll` now tracks each gate's
  trigger-active state across ticks and forces one final `EvaluateAll()` on a true→false
  transition. No behavior change for existing monotonic-trigger gates.
- **`SealableGates`' `ResealCondition: {"Type":"TimerRegrowth"}` could never actually reseal
  within one continuous play session.** `CheckResealTimer` early-returned on
  `state.ClearedThisSession` before reaching its own elapsed-day math — and that flag is only
  ever reset on a full game restart, making the reseal permanently unreachable after the first
  clear unless the player reloaded a save. (This path had zero production usage until this
  release, which is why it was never caught.) Removed the redundant early-return (the elapsed-day
  check already covers "too soon to reseal" on its own) and reset `ClearedThisSession` plus the
  gate's `SeedRequestedEnvUIDs` entries when a reseal fires, so the challenge card can be
  reseeded next time the trigger reactivates.

---

## [2.18.2] — 2026-08-02

### Fixed
- **Cleared cave passages no longer re-collapse after all veins are depleted.** The old-save
  cleanup in `WorldMapInjector.PreCreateCloneEnvSaveData` ("stale Exit-card fix") wiped a clone
  env's entire `EnvironmentsData` entry — including `CurrentlyBuiltImprovements` where
  `SealableGateService` stores permanent "wall cleared" markers — whenever no expected
  ExtraDrops (veins) were found in the saved board. This correctly handled pre-strip old saves
  that contained inherited Exit cards but no vein cards; however it also fired for legitimately
  fully-depleted caves (all veins mined out), erasing the cleared-passage markers and causing
  all collapsed rock walls to respawn on the next load. Fix: the stale-Exit wipe now additionally
  requires at least one `StripLegacyBoardUIDs` card to be present in the entry before removing
  it, confirming it is genuinely a contaminated old-save rather than a depleted-but-valid cave.

---

## [2.18.1] — 2026-08-02

### Fixed
- **`EncounterGuards/*.json` now supports environment-based suppression (`GuardEnvironmentUids`).**
  The loader previously recognized only `GuardCardUids` and silently skipped any guard file
  lacking that field (logging `missing GuardCardUids — skipped`). Community Mod Chest's
  `CMC_VillageNoWildlife.json` uses environment UIDs to suppress wildlife inside village
  environments, so its guard was never registered — village wildlife suppression was inert.
  A guard is now valid with either `GuardCardUids` or `GuardEnvironmentUids` (or both, evaluated
  as OR); the predicate suppresses a wildlife encounter when the player stands in a listed
  environment (via `GameQuery.CurrentEnvironmentUniqueId`), still respecting the `EncounterUids`
  filter and `SuppressChance`.

---

## [2.18.0] — 2026-07-27

### Added
- **Declarative bulk trading-value repricing (`TradingValues.json`)** — a mod may ship a flat
  JSON object map of `CardData` UniqueID → number in its mod root; the new
  `Injection/TradingValueInjector` (LoadOrchestrator phase 5i-a3) writes each value onto
  `CardData.TradingValue` at load. Built because vanilla leaves ~65% of items/liquids at
  `TradingValue: 0`, which NPCs trade as "free". Values apply unconditionally (listed cards are
  retuned even if already priced); keys starting with `_` are ignored (comments); negative
  values are rejected with a Warn; a UID priced by two mods logs a Warn and the later mod in
  load order wins; UIDs not found in the registry are Debug-logged and skipped (a fleet price
  table may cover an optional sibling mod). Runs before `GameSourceModifier`, so a targeted
  `GameSourceModify/` patch still overrides a bulk price. `TradingValues.json` also counts as a
  framework-only marker for `ModManifest.HasFrameworkOnlyMarkers` (mistagged-mod reclaim).

## [2.17.2] — 2026-07-27

### Fixed
- **NPC-interaction button text overflowing its border** (Talk/Trade/Commissions row in
  `NPCInspectionPopup`, and `DialogsPopup` answer buttons). These buttons ship with TextMeshPro
  auto-sizing disabled, so any label wider than the button's authored width clipped past the
  border instead of shrinking to fit. New `Patching.BugFixes.NPCButtonTextFit` postfixes
  `TooltipButton.Setup`, `DialogAnswerButton.Setup`, and `NPCInspectionPopup.SetupActions` to
  enable TMP shrink-to-fit auto-sizing the first time each button's text component is seen
  (`fontSizeMax` = the button's original authored size, so already-fitting text is visually
  unchanged). `TooltipButton.Setup` underlies every `IndexButton`-family button in the game, so
  this covers the same overflow class fleet-wide, not just the two NPC popups. Requires a new
  `Unity.TextMeshPro.dll` reference (already shipped in `lib/`, now wired into the csproj).

---

## [2.17.1] — 2026-07-21

### Changed
- **`ConnectionGateService` now logs a `Warn` when a gate with `HideTravelDA: true` flips to
  unlocked while stripped travel DAs sit in its restore cache but `RestoreDAOnUnlock` is false.**
  This configuration shows the map connection but leaves the compass slot as a permanent red X
  (slot exists, no travel action) for the rest of the game process — almost always a
  `MapNodes.json` authoring error, and previously completely silent. Found via CMC's Village
  Path gate (River Clearing → East red X despite built bridge + Pathfinder perk). The doc
  example in the service header no longer models `"RestoreDAOnUnlock": false`.

---

## [2.17.0] — 2026-07-19

### Fixed
- **Game froze solid (Not Responding) when loading a save that contains a blueprint container with a missing contained-blueprint card** (e.g. CMC 1.21.0 Miller/Alchemist content). `BlueprintContainerSaveLoadFix.ProcessOneCard` reflect-invoked vanilla `GameManager.SpawnDefaultContainedBlueprints` and drained the returned `IEnumerator` with a synchronous `while (iter.MoveNext()) {}`. That vanilla coroutine is not a bounded computation — when a contained blueprint is actually missing it schedules a real Unity coroutine (`StartCoroutineEx(AddCard(...))`) and then `while (CoroutineController.WaitForControllerList(...)) yield return null;`, a wait that can only resolve across real frames. A synchronous drain never yields to Unity, so `AddCard` never advances and `MoveNext()` returns true forever — a deterministic infinite spin, not a race. Rewrote `ProcessOneCard` to yield through the enumerator step-by-step (`yield return iter.Current`) so Unity processes frames between steps. Confirmed fixed in-game by the user 2026-07-19 (same save, same content, loads and responds normally). Retrospective: `Documentation/Retrospectives/blueprint-container-save-load-freeze.md`; memory `reference_synchronous_coroutine_drain_freeze`.

### Changed
- **Vanilla QuestLog auto-injection (`QuestInjector`) is now hard-gated OFF by default.** A mod shipping `Quests.json` will NOT attach any `QuestLog` to `PlayerCharacter.Quests` unless the player explicitly sets `Quests/EnableQuestInjection = true` in the framework's BepInEx config. This exact path caused a user-confirmed blueprint research reset on save load in CMC 1.7.0 and the root cause was never diagnosed (`Documentation/Retrospectives/questinjector-blueprint-reset-risk.md`). Previously the injector ran and attached whenever a manifest was present, emitting only a warning; it now refuses to attach and logs why. `CharacterRosterInjector` (a separate, lower-risk surface with no incident history) is unchanged.

---

## [2.16.4] — 2026-07-16

### Fixed
- **Clone-node location cards whose JSON referenced other clone UIDs (blueprint gates, contained blueprints, improvements) resolved those refs to null**, because the clone env/location pair is created during `WorldMapInjector.PrepareAll` — after `WarpResolver` has already walked all JSON. Symptom: a clone location card showing an empty "Have :" tooltip line (a blueprint availability gate that never resolved) and clone-referencing fields silently staying null. Added a `WorldMapInjector.ResolveDeferredCloneRefs` LoadOrchestrator phase that runs immediately after `WorldMapInjector` and re-walks the deferred JSON refs for cards keyed by clone UIDs registered this run, filling the reference fields WarpResolver couldn't. Reference-token (WarpType 3) only — Add cases are handled by the dedicated `ImprovementInjector`. Memory: `reference_worldmap_clone_ref_deferred_resolve`.

---

## [2.16.3] — 2026-07-16

### Fixed
- **`InjectImprovementInto.json` could never target a mod map node's own location card**: the
  ImprovementInjector load phase ran before `WorldMapInjector.PrepareAll` created the clone
  env/location pairs, so a `TargetEnvUID` naming a clone CT8 (e.g. CMC's `cmcLocVillage`) was
  always "not found in registry — skipped". The phase now runs after map-node preparation
  (5i-a2); vanilla CT8 targets are order-insensitive and unaffected.

---

## [2.15.2] — 2026-07-16

### Fixed
- **`CardUtil.GetDurability`/`SetDurability` never worked for `UsageDurability`**: the JSON stat name mapped to a nonexistent `CurrentUsage` runtime member (the real `InGameCardBase` field is `CurrentUsageDurability`), so reads returned NaN and writes silently failed. Player-visible fallout fixed by this: the CMC Academy's Armorer course never charged its 100 tuition enrollment fee (its progress lives on `UsageDurability`), and Sirus companion thirst initialization on spawn was a no-op.

---

## [2.14.1] — 2026-07-12
*(Covers framework releases since the last published release on 2026-06-23.)*

### Added
- **Declarative animal system foundation**: mods can now ship `Animals/*.json` manifests that the framework validates and turns into generated NPC agents, with config gating and run-start spawn registration. This is the first milestone of the animal pipeline: schema loading, validation, generated agents, lifecycle templates, model-card inventory safety, and deferred-section warnings for not-yet-implemented animal features.
- **JSON-only non-UID ScriptableObject support**: `ScriptableObject/<Type>/*.json` assets such as `WeaponMove`, `DamageType`, and `CardTag` are now registered by name so WarpData can resolve them. This unblocks JSON-authored custom attacks and other non-UID assets.
- **`GameSourceModify` support for non-UID targets**: JSON patches can now modify existing vanilla or modded non-UID objects by name, allowing in-place edits to shared `WeaponMove`, `DamageType`, `CardTag`, and similar assets.
- **Shared utility APIs**: added `Api.BlueprintAlternates`, `Api.CardFinder`, `Api.StatAccess`, and `Api.RecipeInjector` so content mods no longer need to duplicate reflection-heavy helpers for alternate ingredients, runtime card lookup, stat access, or station recipe injection.
- **Perk group opt-out**: `"CharacterPerkPerkGroup": "None"` keeps runtime-only perks out of character creation instead of forcing them into the Situational tab.

### Changed
- **Portal Hub flow redesigned**: the Portal Kit is now portable and can be placed anywhere in the vanilla world; mod worlds use isolated portal environments with auto-injected exit cards instead of a fixed River Clearing entrance.
- **WorldMap gate handling hardened**: connection gates now support precise edge toggling, travel DA strip/restore, run-start re-evaluation, and framework-owned declarative gates used by ACT and H&F.
- **Mod discovery is more forgiving**: framework-format mods that accidentally carry Pikachu `ModLoaderVerison` fields are reclaimed when they also ship framework-only marker files such as `BlueprintTabs.json`, `MapMod.json`, `WorldMap/MapNodes.json`, or `Animals/*.json`.
- **Loader coexistence logging improved**: non-UID name collisions now distinguish benign external-loader duplicates from real same-pass mod collisions.

### Fixed
- **NPC actions with no drops no longer crash** when converted to game actions; null `DroppedCards` arrays are backfilled before WarpResolver.
- **Blueprint container save/load handling no longer synchronously drains coroutines**, avoiding load freezes around station-contained blueprints.
- **WorldMap node injection no longer double-seeds or loses run-start location cards** in the covered gate and portal scenarios.
- **Framework-format third-party mods no longer silently lose blueprint tabs** just because Pikachu ModLoader or ModCore is installed.

### Technical
- Load orchestration now includes the animal phase, NPCAction drop repair, declarative improvement injection, sealable gates, shared recipe injection, and expanded vanilla ID resources.
- ACT, H&F, WDI, CMC, and Sirus integrations were progressively moved onto shared framework services, reducing duplicated mod-local Harmony and reflection code.

---

## [2.11.1] — 2026-07-05

### Fixed
- **Framework-format mods mistagged with Pikachu `ModLoaderVerison` are no longer skipped.** `ModDiscovery.DiscoverMods` now checks whether a `ModLoaderVerison`-tagged mod ships framework-exclusive declarative content (`BlueprintTabs.json`, `SmeltingRecipes.json`, `DropInjections.json`, `InjectImprovementInto.json`, `WorldMap/MapNodes.json`, `EncounterGuards/*.json`, `Quests.json`, `Characters.json`, `MapMod.json`) before deciding to skip it in favor of an installed Pikachu ModLoader/ModCore. Mods with framework-only markers are loaded through the framework's own pipeline instead — `ForeignInstanceReconciler` neutralizes the resulting duplicate `UniqueIDScriptable` instances ModLoader creates for them. Fixes third-party mods (e.g. DurosCoinage's `BlueprintTabs.json`) whose blueprint tabs silently never appeared because the mod was entirely skipped by the framework despite being authored for it.

---

## [2.8.0] — 2026-06-21
*(Covers versions 2.2.0 → 2.6.0 → 2.7.x → 2.8.0, since v2.0.8)*

### Added
- **Portal Hub System**: A new **Portal Kit** item can be placed anywhere to erect a Portal Hub — a standing stone that opens a gateway to worlds added by installed mods. Pack it up and move it at any time.
- **Arcane Wayfinder perk**: A free starting perk that grants a Portal Kit at run start. Available immediately for all runs.
- **WorldMap node injection**: Mods can now add fully functional new locations to the world map with their own environments, resources, and travel connections — appearing alongside vanilla map nodes.
- **Clone-based map environments**: Mods can clone vanilla biomes (oak groves, pine clearings, caves, etc.) as new locations, inheriting trees, resources, and ambience.
- **Quest support**: Mods can now ship quest lines that appear in the journal and integrate with the standard objective/reward system.
- **Custom character support**: Mods can add selectable player characters that appear in the character-select screen.
- **SelfTriggeredAction support** *(v2.2.0)*: Mods can ship stat-gated events, seasonal triggers, blueprint unlocks, and perk grants without any C# code — activated automatically each run.
- **Encounter guards**: Mods can now suppress specific wildlife encounters in designated areas (e.g. no bear attacks inside a protected grove).
- **SpawnStatDefaults**: Mod items can declare initial stat values (starting durability, metal type, etc.) applied every time they are spawned — no per-mod C# postfix needed.

### Fixed
- **Blueprint research no longer resets on save/load** when Pikachu ModLoader or ModCore is installed alongside framework mods. ModLoader was creating duplicate card instances that caused the game to lose track of researched blueprints — the ForeignInstanceReconciler now guarantees the framework's instances are canonical.
- **Travel popup no longer crashes with WikiMod installed** alongside mods that add WorldMap locations. WikiMod's internal error is now caught so travel buttons stay functional.
- **Clone-environment location cards no longer follow the player** between maps when the cloned template had `AlwaysUpdate: true`.
- **Mod-added travel direction buttons no longer show a red ✗ all night** and activate only at dawn — inherited light/stamina stat gates are now stripped from injected travel actions.
- **Modded map node environments no longer cause the world map to break** when node UIDs contained underscores — UIDs now use camelCase, fixing silent save-data match failures.
- **DefaultEnvCardDrops from clone templates no longer re-spawn every entry** into a modded area — follower drops from the template are neutralized so board state persists correctly.

### Technical
- Tier 1 utility API (`Api.Reflect`, `Collections`, `Inventory`, `Gate`, `LocalizedStringBuilder`, `VanillaIds`) available to mod authors for common reflection and data-access patterns.
- Tier 2 runtime services (`Api.ActionRouter`, `SpawnService`, `TickEvents`, `EncounterGuards`, `ContentModPlugin`) — mod actions, spawns, and timed events no longer require per-mod Harmony patches on game coroutines.
- Type loading hardened against `ReflectionTypeLoadException` from third-party assemblies — a bad DLL no longer aborts framework startup *(v2.2.0)*.
