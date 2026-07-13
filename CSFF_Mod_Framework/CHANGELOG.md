# CSFF Mod Framework — Changelog

All notable changes to CSFFModFramework are documented here.

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
