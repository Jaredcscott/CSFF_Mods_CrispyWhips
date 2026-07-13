# Roadmap: CSFF Mod Framework
Version at time of writing: 2.7.6
Date: 2026-06-18
Audit score: 9/10 (utility mod — `/audit-mod CSFFModFramework`, 2026-06-13; re-run pending for 2.7.x surface)

## Current State

**Theme**: The single shared engine layer every in-house CSFF mod loads against. It owns mod discovery, JSON/WarpData loading, sprite/audio/localization, all injection (perk, blueprint, smelting, STA, NPC agent, world map), runtime services (ActionRouter, SpawnService, TickEvents, EncounterGuards), and the performance/bug-fix patch stack. Content mods only write C# for mod-specific logic.

**Content**: ~95 C# source files across `Loading/`, `Injection/`, `Api/`, `Patching/`, `Discovery/`, `Data/`, `Triggers/`, `Wildlife/`, `Gif/`. No CardData / images / perks (utility mod). Public API surface: Tier 1 (`Reflect`, `Collections`, `Inventory`, `Gate`, `LocalizedStringBuilder`, `VanillaIds`, `CardUtil`) + Tier 2 (`ActionRouter`, `SpawnService`, `TickEvents`, `EncounterGuards`, `ContentModPlugin`). **2.7.x additions**: two-pass WorldMap node injection (no false ONE-WAY warnings), instanced-env guard in `PrepareNode`, `CardCloneService` sets `AlwaysUpdate=false` on CT4/CT8 clones, `AlwaysUpdateService` skips CT4/CT8, `RequiredStatValues` stripped from injected travel DAs, `GameQuery.CurrentEnvironment` field-or-property resolution, `SpawnService` `BindingFlags.Static` GiveCard fix, `ExplorationPopupFix` finalizer for WikiMod NREs.

**Stability**: 9/10 — zero critical/design issues. Both data normalizers are mod-prefix filtered, both transpilers preserve `.labels`/`.blocks`, no hot-path prefixes, no `DropCollectionGuardPatch`. The −1 is purely the two pending runtime verifications below.

**Open work**:
- 🔵 `village-path-east-travel` — **Graduated 2026-06-16**. Root causes: `GameManager.CurrentEnvironment` was field not property (always null); `WaitForSeconds` stalls at `timeScale=0`; inherited Light gate on cloned travel DA showed red X at night. All three fixed; travel matrix partially confirmed (3/10 legs ✅).
- 🟡 `sh-wild-sheep-spawn` — TriggerLoader/TriggerService deployed; needs in-game verification (watch for `[TriggerService] initialized`, sheep appear after 1 in-game day at `SpawnChance:100`).
- ✅ `ExplorationPopupFix` — confirmed in deployed DLL 2026-06-18; `ExplorationPopup.Setup` finalizer swallows WikiMod NREs.

**Framework compliance**: This mod *is* the framework — it provides Tier 1/Tier 2 rather than consuming them. The relevant maturity axis is its own gap-audit roadmap: **Phases 0–5 shipped** (universal type loading, STA activation, NPC validation+injection, WorldMap nodes, quests/characters). **Phase 6 (tooling/docs) is incomplete**, and the **centralization plan's Tier 3 (declarative JSON systems) is partially shipped** — `SmeltingRecipeInjector` already exists; `ForageDropInjections`/`InjectImprovementInto` are landing now (see below).

> **2026-07-02 update:** `Documentation/Design/JSON_Only_Map_Kit_Analysis_and_Plan.md` found the WorldMap/portal subsystem specifically was *already* materially ahead of `Documentation/CSFF_Map_Travel_System.md` and `Documentation/Design/Unified_Map_Expansion_Design.md` §9 (both corrected in this same pass — see those files' new staleness banners). That analysis's roadmap is being implemented now: `SealableGates` (retires ACT/H&F hand-rolled gate patches), Portal Hub hardening (not a migration — the build-anywhere Portal Kit/Hub stays the one true portal mechanism, see `Documentation/Portal_Hub_System.md`), `QuestActive`/`StatThreshold`/edge-granularity gates, `ForageDropInjections`/`InjectImprovementInto`, a `CardUtil` GameManager/ImprovementBuilt dedup, an `/audit-mod` map-JSON validator, and a new Pester suite. This closes most of Phase 4's "Validation tooling" line item below and several Phase 2 items scoped to WorldMap specifically.

---

## Phase 0: Stabilize

> The `village-path-east-travel` retro is graduated. One verification and the adversarial review remain.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| ~~Verify `village-path-east-travel`~~ | ~~Retro close~~ | ~~P0~~ | ✅ Graduated 2026-06-16 |
| Verify `sh-wild-sheep-spawn` in-game (boost to `SpawnChance:100`/`TriggerFrequency:96`, watch for `[TriggerService] initialized`) | Retro close | P0 | Quick (needs game launch) |
| Verify remaining CMC village travel legs (Village Farm→VP South, VP→Village East, Village→VP West, VP→Foraging Forest South, Foraging Forest→VP North) | Travel matrix | P0 | Quick (needs game launch) |
| Re-run `/critical-analysis CSFFModFramework` — current report is v2.5.0 (2026-06-11); 2.7.x WorldMapInjector rewrite, AlwaysUpdate fix, ForeignInstanceReconciler, ExplorationPopupFix all uncovered | Adversarial review | P0 | Quick |
| ~~Confirm `NPCAgentActivationService` `[DIAGNOSTICS]` lines are `Log.Debug`~~ | ~~Logging norm~~ | ~~P1~~ | ✅ Verified 2026-06-18 — all at `Log.Debug` |

---

## Phase 1: Foundation

> Documentation debt is the real foundation gap here. Phases 2–5 shipped working injectors, but the authoring guides never followed — so the features are effectively unreachable by anyone but this repo.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Rewrite stale `NPCAgentActivationService` docstring + `LoadOrchestrator` comment — both still call it a "diagnostics build awaiting an injector," but `InjectMissingAgents` ships and runs | Doc hygiene | P1 | Quick |
| Confirm `FRAMEWORK_EVALUATION.md` archived to `Documentation/Retrospectives/` (audit says done — verify no stale copy in source root) | Doc hygiene | P1 | Quick |
| **Cookbook docs** (gap audit Phase 6): `CSFF_Patterns.md` sections — Adding a Spirit, Adding a Roaming Animal, Adding a Trader, Adding a Location, Adding a Quest Chain, Adding a Character | Authoring docs | P1 | Medium |
| `CSFF_Reference.md` + CLAUDE.md: land the corrected full CardType table (CT4=Environment, CT8=Construction, CT9=Liquid, CT10=Improvement, CT11=Damage, CT13=Invisible) — gap audit flagged this as still unwritten | Reference docs | P1 | Quick |
| Keep version sync (ModInfo/Plugin.cs/GlobalUsing/README) — currently aligned at 2.7.1; use `Update-ModVersion.ps1` on next bump | Version hygiene | P2 | Quick |

> Note: the framework ships `Localization/SimpEn.csv` for its own player-facing strings (Hub Portal cards). `LocalizationLoader` loads the framework's own CSV before content-mod CSVs — ModDiscovery deliberately skips the framework dir, so the separate load path in `LocalizationLoader.InjectLocalization` is required.

---

## Phase 2: Core Expansion

> Tier 3 declarative JSON systems (centralization plan §3, targeted v2.6.0). **Correction, 2026-07-02: this phase is further along than this section's own header claimed** — `SmeltingRecipeInjector` and `DropInjector` (`DropInjections.json`) are both fully implemented and wired into `LoadOrchestrator`, not "never shipped." Their gap was zero production adoption, not missing code — the same drift pattern found across this framework's WorldMap/portal subsystem (see `Documentation/Design/JSON_Only_Map_Kit_Analysis_and_Plan.md` §3.B). Remaining Tier 3 items below (`SpawnStatDefaults`, `RecipeInjections.json`) are genuinely still unbuilt.

### `DropInjections.json` (Tier 3 §3.1) — ✅ Shipped, now adopted
**Status**: Fully implemented (`Injection/DropInjector.cs`, wired into `LoadOrchestrator` phase 5e2) since before this entry was last accurate. Schema: `{ "Locations": {"Uids"|"CardNameKeyContains"|"Tags"}, "Action": "Forage", "ActionMode": "exact"|"contains", "Drops": [{"Card","Chance","Quantity":[1,1]}] }`. Matches action names (exact or substring), idempotent append.
**Acceptance proof (2026-07-02)**: Community_Mod_Chest migrated its `ForageInjectionPatch.AddDropsToForageActions`/`AddDrop` onto `DropInjections.json` (25 vanilla + 23 H&F soft-dependency items across two locations) — see `Community_Mod_Chest/DropInjections.json`. HerbsAndFungi's own ~400 LOC forage injection is NOT yet migrated — still open work, now unblocked by a proven real-world example instead of an untested schema.

### `SpawnStatDefaults` sidecar (Tier 3 §3.5)
**What**: A `"SpawnStatDefaults": {"SpecialDurability4": 200}` block on a card, honored by `SpawnService`'s single GiveCard postfix for **all** spawn paths (ProducedCards, OnFull, perk kits).
**Why**: Replaces every per-mod GiveCard-postfix stat-init currently hand-written (WDI iron-bar SD4=200, ACT armor defaults, Sirus perk-kit items) and the perk-spawned-item-defaults pattern. SpawnService already owns the postfix — this is a JSON read layered on top.
**Requires**: none (Tier 2 SpawnService already shipped).
**Complexity**: Quick–Medium

### `RecipeInjections.json` (Tier 3 §3.2)
**What**: Generalize `SmeltingRecipeInjector` beyond smelting: `{ "Stations": ["uid","tag_DryingRack"], "TemplateFrom": "first"|"<uid>", "Overrides": {"CompatibleCards","Duration","IngredientChanges"} }`. Clone-from-template CookingRecipe with duplicate detection (proven in the smelting injector).
**Why**: Covers ACT kettle heating, WDI kiln, H&F tendon drying — three mods currently each carrying their own recipe-injection code.
**Requires**: none.
**Complexity**: Medium

### Activate the dormant small types
**What**: `FlavourTag`, `CookingRecipeGroup`, `BookmarkGroup` load + register since 2.1.0 but are not injected/activated. FlavourTag = trivial SpiceTag parity (2-field schema); CookingRecipeGroup lets station mods register containers in the cooking UI properly; BookmarkGroup is a UI-hotkey group append.
**Why**: Low-risk parity wins; FlavourTag in particular unblocks H&F recipe chemistry.
**Requires**: none.
**Complexity**: Quick (each)

---

## Phase 3: Integration & Depth

> Unblock the content-mod categories that are stuck today, and retire the remaining duplicated C# patterns across in-house mods.

### Encounter activation + wiring (gap audit Phase 3 remainder)
**What**: `Encounter` loads since 2.1.0 but nothing exercises it. Diagnostics-first: confirm the game's encounter enumeration path, verify `LoadedFromNPCStat` warp triplets resolve against mod NPCStats, and that agent `Interactions → DroppedEncounter` / CT3 event cards fire.
**Why**: The single biggest content unlock — MagicAndSpirits, HostileEncounters, TradersAndNPCs, AnimalCompanions idea mods are all blocked on the roaming/encounter layer.
**Requires**: diagnostics pass before any injector (debugging-discipline rule 2).
**Complexity**: Complex

### `ProcessAllService`
**What**: Declarative "process all inventory" buttons — Grind All / Hammer All / Blast All share one shape across ACT + WDI. Either a JSON schema or a thin `Api` registration call on top of ActionRouter + Inventory + SpawnService.
**Why**: The centralization plan parked this until those three Tier 2 pieces landed — they now have, so it's unblocked. Retires duplicated per-mod "process all" code.
**Requires**: ActionRouter + Inventory + SpawnService (all shipped).
**Complexity**: Medium

### `ActionInjections.json` (Tier 3 §3.3)
**What**: Clone-from-template DismantleAction via JSON using the runtime action-injection pattern: `{ "TargetCards", "CloneAction":"Forage", "NewName":{...}, "DaytimeCost", "ProducedCards" }`.
**Why**: Removes the most common reason a content mod still ships `GameLoadPatch` C#.
**Requires**: **Design decision** — how far to support handler-backed (inventory consume/output) actions declaratively vs. leaving those to ActionRouter C#.
**Complexity**: Medium

### `GameSourceModify` nested-append (Tier 3 §3.4)
**What**: Extend `_appendArrays` to dotted paths one level into structs/objects: `InventoryFilter.TagFilters`, `PlantationCards`, `DismantleActions[name=Forage].ProducedCards`.
**Why**: Lets mods extend vanilla card sub-collections without C#; today the array-zeroing guard only protects top-level arrays.
**Requires**: **Design decision** — path-expression syntax and target-shape validation before mutating vanilla objects.
**Complexity**: Medium

### `Api.ModState` save-persistent helper
**What**: A keyed save-blob API for mod state the card system can't hold (NPC trust, companion morale, world-hardship toggles).
**Why**: Several idea mods (companions, traders, world-difficulty) need persistence beyond stats-on-cards.
**Requires**: **Design decision** — piggyback the game's save stream vs. a sidecar file keyed to the save slot; mod-removal safety is the hard part.
**Complexity**: Complex

---

## Phase 4: Polish

> The framework has no art to polish — "polish" here is authoring ergonomics, validation, and the runtime hooks that make injected content robust.

| Item | What | Complexity |
|------|------|------------|
| Validation tooling (gap audit Phase 6) | Extend `/audit-mod` to validate NPC/Encounter/STA/Quest/Map JSON: GUID resolution, required fields, bidirectional map-link symmetry, encounter stat sanity. Reuse WarpResolver unresolved-ref reporting where possible | Medium |
| `InGameMapWindow.Show` postfix hook | Standing remedy from `village-path-east-travel`: if the map UI builds its node list before load-time injection, re-apply at UI-open time (mirrors the `BlueprintModelsScreen.Show` lesson). Gate on the Phase 0 verification result | Medium |
| Change events on `TickEvents` | `GameQuery` reads season/weather/moon and `TickEvents` fires DtpTick/DayRollover, but there are no *change* events. Add year-rollover / season-change / weather-change via cheapest poll-and-diff in the existing Update loop | Medium |

---

## Long-term Vision

> Where the framework should be at v3.0.

By v3.0 the framework should make **every vanilla content category authorable in JSON-first form** — not just the item economy. Phases 0–5 already load and inject all 15+ UniqueIDScriptable types; the v3.0 milestone is closing the loop so that a third-party author (not just this repo) can ship a complete spirit, roaming animal, location, or scenario character using only declarative JSON + documented patterns, with `/audit-mod` validating it and a save-compat harness proving mod-removal safety. Tier 3 declarative systems (Phase 2) plus the cookbook docs (Phase 1) plus encounter activation (Phase 3) are the three legs of that goal; the runtime services (Tier 2) are already in place.

**Potential major additions** (not yet justified — revisit after Phase 3):
- **`CompanionService` (Tier 4, parked)** — pet hunger/thirst/morale sim with auto-feed/auto-drink from the board on `TickEvents`. Only Sirus needs it today; build when a second consumer appears, to retire Sirus's polling loop.
- **Save-compat test harness** — scripted save fixtures for the add-character/agent/quest → save → remove-mod → load matrix. The highest-risk gap across Phases 3–5; surfacing regressions before ship rather than in player saves becomes essential once third parties depend on injection.
- **UI extension hooks** (from `IDEAS.md`) — safer extension points for 12h clock, status-bar modes, action-button colors, map overlays, container capacity/sealed indicators.

These live in `Documentation/Ideas/CSFFModFramework/IDEAS.md` (already specced) and the centralization plan `Documentation/CSFFMFW_Centralization_Plan_2026-06-09.md`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new injector/service phase | Run `/audit-mod CSFFModFramework` and update this roadmap |
| After shipping a Tier 3 JSON system | Migrate one consumer mod onto it as the acceptance proof (the Tier 1/2 retrofit discipline: CMC → Sirus → ACT → H&F → WDI), then delete the replaced C# |
| Game version update | Re-extract via `/extract-latest-carddata` (regenerates `Api.VanillaIds`), `/update-mod-version`, check CLAUDE.md EA-version notes, re-run `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis CSFFModFramework` to verify the fix |
| Before tagging a release | Confirm version sync across ModInfo.json / Plugin.cs / GlobalUsing.cs / README.md, then `/export-to-repo CSFFModFramework` |
| Whenever a content mod still re-implements a framework pattern | Treat it as a centralization candidate — promote to a shared `Api`/injector |

---

## Skill Cheatsheet for This Mod

```
/audit-mod CSFFModFramework         — full health check, updates .audit/ (utility-mod aware)
/critical-analysis CSFFModFramework — adversarial review (re-run for 2.7.1 — current report is 2.5.0)
/build-mod CSFFModFramework         — build Release DLL
/deploy-mods CSFFModFramework       — build + deploy to BepInEx/plugins/CSFF_Mod_Framework/
                                      (deploy framework FIRST so content mods load against fresh code)
/extract-latest-carddata            — re-extract game data + regenerate Api.VanillaIds on game updates
/update-mod-version CSFFModFramework <ver> — bump version in all authoritative files
/export-to-repo CSFFModFramework    — push to public repo (validates version sync first)
/resolve-retro <slug>               — close sh-wild-sheep-spawn after in-game verification (village-path-east-travel already graduated 2026-06-16)
```

> Item/blueprint/structure/perk repair skills are N/A — the framework ships no CardData.
