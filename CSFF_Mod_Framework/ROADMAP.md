# Roadmap: CSFF Mod Framework
Version at time of writing: 2.25.30
Date: 2026-09-10
Audit score: 9/10 (PASS - 0 preflight CRITICAL, 0 design gaps, 1 fix-shipped retrospective pending in-game verification, 2 warnings)

## Current State

**Theme**: The shared engine every in-house CSFF mod depends on. Mod discovery, JSON data loading, WarpData resolution, sprite/GIF loading, localization, blueprint/perk/drop/improvement injection, SelfTriggeredAction activation, WorldMap node injection + a cross-mod Portal Hub, NPC agent diagnostics, a declarative Animal System, and a suite of performance patches. Content mods only write C# for mod-specific logic.

**Content** (the framework's own minimal payload): 1 item / 1 blueprint / 2 structures / 1 perk / 2 custom images. The real product is 144 `.cs` files of engine code.

**Stability**: 9/10 - clean Release build (0 errors / 0 warnings), versions synced at 2.25.30 across `ModInfo.json` / `Plugin.cs` / `README.md`, localization parity clean (13 EN / 13 CN), Bin/Release sync clean, code-quality 10/10. The one deduction is a single runtime defect whose fix shipped in 2.25.30 and awaits in-game verification (below).

**Open work**:
- 🟡 `river-bridge-east-click-noop` (fix shipped 2.25.30, verification pending) - eastbound River Clearing -> Village Path travel button renders, is clickable, and performed no travel. Framework travel-DA injection onto a vanilla CT8 (the reverse direction; the forward injection onto a clone CT8 works). Root cause pinned and fixed: `ConnectionGateService.EvaluateAll` ran on a main-menu tick where `GameManager.Instance` is C#-null, flipping the gate LOCKED and stripping one DA; fix = live-`GameManager` guard on `EvaluateAll`/`SealableGateService.OnPoll` + a self-healing travel-DA cache resync (`TravelDaCacheResync`), commit `c6973b394`, gated by `Framework-ConnectionGateEvaluation.Tests.ps1`. Closes on `T2.186`.
- 🟡 Pending verification (human/runtime-gated, not re-verified this pass): `worldmap-clone-duplicate-terrain`, `gate-red-baseline-rediscovery`, `wikimod-old-save-load-crash`.
- 🟡 Open plan: `questinjector-blueprint-reset-risk` (the `QuestInjector` path is shipped but hard-gated OFF pending root-cause diagnosis of its blueprint-research reset).

**Framework compliance**: N/A in the usual sense - this mod IS the Tier-2 service layer (`ActionRouter`, `SpawnService`, `TickEvents`, `ContentModPlugin`, `EncounterGuards`). It carries no `ModLoaderVerison`, no `DropCollectionGuardPatch`, no unfiltered hot-path prefixes; its load-time normalizers filter strictly by mod prefix. Code-quality found 0 reliability risks across 146 scanned files.

---

## Phase 0: Stabilize

> Fix / close before any new engine capability lands. All three are runtime- or human-gated, so "fix" here means capture evidence, not write code blind.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Close `river-bridge-east-click-noop` (fix shipped 2.25.30): relaunch (quit-to-desktop, not a save reload), build the bridge, hold Pathfinder, attempt eastbound travel, then grep `LogOutput.log` for `[TravelDaCacheResync]` FIRST per the retro's Open Unknown #1 outcome map. Capture closure evidence, do not re-discover | Retro close (READ first) | P0 | Medium |
| Re-verify the three 🟡 pending retros on the next play session (`worldmap-clone-duplicate-terrain` needs a fresh-boot FIRST visit to a clone tile; `wikimod-old-save-load-crash` needs a real old-save load) | Retro verify | P0 | Medium |
| Decide and act on `questinjector-blueprint-reset-risk`: run the single-variable graduation test on a disposable save, or keep the gate OFF and record that decision | Retro / design | P1 | Complex |

---

## Phase 1: Foundation

> Table-stakes health. Most of this is already GREEN and should stay that way; the work is keeping it green each game version, not first-time setup.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Keep `ModInfo.json` / `Plugin.cs` / `README.md` versions synced (use `Update-ModVersion.ps1`) | Version hygiene | P1 | Quick |
| On each game update, refresh `lib/Assembly-CSharp.dll` and force a clean rebuild; regenerate the 9 NStrip binders with the owner's external tool, then re-run `Development_Tools/RefCheck/` | Game-version hygiene | P1 | Medium |
| Keep localization parity (13/13) as keys are added; any new `SimpEn.csv` row gets a matching `SimpCn.csv` row in the same commit | Localization | P1 | Quick |

---

## Phase 2: Core Expansion (engine capability)

> The highest-value engine surfaces that are loaded-but-dormant or shipped-but-undocumented. Near-Term items from `Documentation/Ideas/CSFFModFramework/IDEAS.md` - no design decision blocking.

### Author-facing cookbook docs for the shipped-but-undocumented injectors
**What**: a `Documentation/CSFF_Patterns.md` cookbook entry for the Animal System (`Animals/*.json` schema v1 - `AnimalService`/`AnimalLoader`/`AnimalValidator`/`DutyBuilder`/`LifecycleTemplateBuilder`/`AnimalLifecycleTicker`/`SpawnRegistrar`) and for `NPCCharacterPerk` (2.20.0).
**Why**: the code is fully shipped and demonstrated (Sirus23's `TestHare.json`, one manifest, zero mod C#), but with no cookbook entry it is unreachable by any author but the one who wrote it. Highest value-per-effort in the backlog.
**Requires**: none.
**Complexity**: Medium.

### Activate the loaded-but-dormant `DirToTypeName` types
**What**: thin injectors (mirroring `PerkInjector`/`BlueprintInjector`) for `FlavourTag` (trivial, SpiceTag parity), `CookingRecipeGroup`, `ConstructionCardGroup` (12 vanilla instances), and `GameModifierPackage` standalone activation via `Modifiers.json`.
**Why**: each loads and registers since 2.1.0 but nothing injects/applies it, so the capability is invisible to downstream mods. Unblocks CookingExpanded/DairyWorkshop/Brewery, DecorationAndComfort, and HardcoreMode/ChallengeRun idea mods.
**Requires**: per type, first confirm load-time (`AllData`) vs UI-time (`*Screen.Show` postfix) enumeration.
**Complexity**: Medium.

### Ship a first consumer for `NPCCharacterPerk`
**What**: one `NPCAgent` + per-variant personality-perk bundles (mirrors vanilla Partner presets); land CMC Village Guards (built 1.48.0, unplayed) as the acceptance proof.
**Why**: the injector has no consumer yet, so it is unexercised. A shipped consumer both proves the path and becomes the cookbook example.
**Requires**: the Village Guards content (exists) + a play session.
**Complexity**: Medium.

---

## Phase 3: Integration & Depth

> Durable engine hardening and generalization. Design decisions required.

### Build the save-compat test harness
**What**: scripted fixtures for the add-content -> save -> remove-mod -> load matrix (re-authoring the deleted `fwHarnessTestCharacter` is step 0).
**Why**: closes four unverified injection paths at once - `QuestInjector` (gated OFF), `SealableGates.ResealCondition` (one unplaytested consumer), `CharacterRosterInjector` (no consumer ever), `NPCCharacterPerk` (no consumer yet). The durable alternative to testing these on a player's live save.
**Requires**: Phase 2 `NPCCharacterPerk` consumer is a useful first fixture subject.
**Complexity**: Complex.

### Generalize `WildlifeRaidService` -> `Api.Raid` / `Raids.json`
**What**: keep the engine (day-rollover roll, container scan, sealed-container exemption, dedup gate) in the framework; expose rule values (trigger encounter, target tag, effect) via a registration seam.
**Why**: fully shipped but hardcoded to one rule (bear encounter spoils food in `tag_NotSafeFromAnimals` containers). First/only consumer is Sirus23.
**Requires**: a second consumer to validate the seam shape.
**Complexity**: Medium.

### Author-time static check for gate misconfiguration
**What**: fold the runtime-only `HideTravelDA:true` + `RestoreDAOnUnlock:false` permanent-red-X warning into the `F21-F27` WorldMap validators (`/audit-environments` + preflight), detectable in `MapNodes.json` at author time; add a `LockConditions`/`GateConditions` mutual-exclusion check.
**Why**: today the condition only fires at runtime, on a player's map, after the mod ships.
**Requires**: none.
**Complexity**: Medium.

---

## Phase 4: Polish

> Non-blocking cleanup.

| Item | What | Complexity |
|------|------|------------|
| `Api.ContainerSort` disposition (summary W2) | Either wire the sort engine into a container-sort UI (a "Sort" action on chests/shelves), or relocate its spec to `Documentation/Ideas/` and delete the source. Honestly disclosed as unused future API; reaches no player either way | Medium |
| Portal kit field completeness (summary M1, stale-sourced) | Backfill the 11 boilerplate fields on `csffmfw_portal_kit.json` for consistency; no runtime impact (unique-on-board, cannot be trashed) | Quick |
| `NoSafetyMode` on the Wayfinder perk (summary M2, stale-sourced) | Add the field for completeness; non-functional (defaults false) | Quick |

---

## Long-term Vision

> Where the framework should be at v3.0.

The framework's job is to make a new CSFF mod authorable almost entirely in declarative JSON. The remaining distance to that vision is not more engines - most of them already ship - but the two things that keep shipped engines from being used: documentation (the cookbook gap) and save-safety proof (the harness gap). v3.0 is the point at which every loaded `DirToTypeName` type has both an activation surface and a cookbook entry, every injection path has passed the save-compat matrix, and `Api.ModState` gives mods a sanctioned save-persistent store so nothing has to risk the `QuestInjector` blueprint-reset class of bug again.

**Potential major additions** (not yet justified - revisit after Phase 3):
- `Api.ModState` save-persistent keyed store - retires the need for risky save-stream injection for NPC trust / companion morale / world-hardship toggles.
- `ProcessAllService` (declarative Grind/Hammer/Blast All) - one shape shared across ACT + WDI, now unblocked by ActionRouter + SpawnService.
- `LocalTickCounter` end-to-end usability - the last `DirToTypeName` entry no pass has exercised; could retire every mod's hand-rolled periodic-effect poll.

These live in `Documentation/Ideas/CSFFModFramework/IDEAS.md` with fuller specs.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new engine capability | Run `/audit-mod CSFFModFramework` + `/code-quality CSFFModFramework` and update this roadmap |
| Game version update | Refresh every mod's `lib/Assembly-CSharp.dll`, force clean rebuilds, regenerate NStrip binders, re-run `Development_Tools/RefCheck/`, `/decompile-assembly`, `/extract-latest-carddata` |
| After closing the travel retro | Run `/critical-analysis CSFFModFramework` to confirm, and `/resolve-retro csff river-bridge-east-click-noop` (runtime-evidence gate) |
| After a framework change that content mods load against | Deploy the framework FIRST, then content mods, then repackage the suite, then MUM last |

---

## Skill Cheatsheet for This Mod

```
/audit-mod CSFFModFramework          - full health check, updates .audit/
/code-quality CSFFModFramework       - C# reliability/maintainability scan
/critical-analysis CSFFModFramework  - adversarial review
/build-mod CSFFModFramework          - build Release DLL
/deploy-mods CSFFMFW                 - build + deploy to game (framework folder)
/diagnose-log                        - parse a BepInEx/Player log against known patterns
/decompile-assembly                  - regenerate .decomp/ after a game update
```

---

## Plan Reconciliation Log

> Append-only: never reword, reorder or delete an entry. `/roadmap` Step 7 preserves
> this section verbatim when it overwrites the rest of this file. Record only what
> was ground-truthed.

### 2026-09-08 - Fleet plans consolidated into Master_Plan (Master_Plan, Duties_Ownership_Plan, Playthrough_R33_Failure_Remediation_Plan)
- **Scope:** Fleet. This same entry is recorded in the ROADMAP of CSFFModFramework, Community_Mod_Chest,
  Sirus23_Mod_Collection, WaterDrivenInfrastructure and AdvancedCopperTools. PartnerOverhaul, named by the
  R33 plan for T3.39, has no ROADMAP.md, so its rows are carried by the tracker and by
  `Documentation/Plans/Fleet/Master_Plan.md` only.
- **Verdict:** Master_Plan (2026-08-16 revision): 3 of 3 open items closed in-game (T2.63 and T2.77 PASS r33;
  T2.75 and T2.68 PASS r31); its 3-prompt pack is 3 of 3 VERIFIED-DONE; 1 loose end (fleet legacy-card
  GUID scan) still unactioned and carried. Duties_Ownership_Plan: M0, M0b, M2 and 4 of 5 M1 checks
  confirmed; M3 2 of 4 stations PASS (T2.87 Ore Sluice, T2.88 Sawmill) and 2 FAIL-confounded (T2.89 Forge,
  T2.90 Workshop); M3.5 root cause A fixed in WDI 1.10.13 and validated by r33 (three station duties ran),
  root cause B still confounded with an unbuilt or cold station; the 5th M1 check (ACT Brazier/Lantern
  ownership rows) had no tracker row and was backfilled as T2.212; M4 Fishpond is an owner decision with no
  sequencing intent; the pack's T1.56 ActionRouter note is still an open framework defect. 0 code rows
  remain in the Duties plan itself. Playthrough_R33_Failure_Remediation_Plan (14 rows, written 2026-09-06):
  re-derived today; 2 rows dissolved (T3.39 precondition unmet, T1.71 mis-scoped), 1 certain code fix still
  unwritten (CopperChestPatch GetSlotForCard arity, T4.40 and T4.38), 1 gated on a tester answer (T2.99, now
  behind the T2.207 vanilla discriminator), 1 design decision (T2.101), 6 human-gated re-runs, 2 needing a
  symptom; of its 5 Wave-0 actions 3 have since landed (Disk LogLevels includes Debug, VerboseLogging =
  true, CMC 1.68.19 once-per-change hearth verdict logging) and 2 remain (T3.39 re-arm, T1.71 re-scope).
- **Disposition:** Master_Plan KEEP, rewritten in place as the single fleet plan with a regenerated pack (6
  prompts, 1 open decision). Duties_Ownership_Plan ARCHIVE to
  `Documentation/Design/Duties_Ownership_As_Built.md` (plan verbatim, pack appended as an appendix) because
  the Blast/bellows automation rejection, the one-duty-per-card finding and the M3.5 diagnosis exist
  nowhere else; its pack DELETED from Plans; M4 moved to
  `Documentation/Ideas/WaterDrivenInfrastructure/IDEAS.md` carrying its reason.
  Playthrough_R33_Failure_Remediation_Plan DELETE, folded into Master_Plan (per-row evidence already lives
  verbatim in the tracker notes; last full text at commit f4bc0f9d5); the T2.101 design question moved to
  `Documentation/Ideas/Sirus23_Mod_Collection/SHEEP_PEN.md` with its reasoning.
- **Pruned:** Master pack Prompts 1-3 and the plan's three Open Work sections (all PASS); Duties sections M0,
  M0b, M1 (4 of 5 checks), M2, M3 Ore Sluice and Sawmill, M3.5 root cause A; R33 Class A rows as defects,
  the Class D instrumentation asks now shipped in CMC 1.68.19, and the two Wave-0 config preconditions now
  armed in the deployed install.
- **Evidence:** tracker read by id across items/confirmedItems/droppedItems (124/229/20 rows at read time):
  T2.63, T2.77, T2.87, T2.88 carry `r33 PASS`; T2.75, T2.68 carry `PASS via r31`; T2.89, T2.90, T2.99,
  T2.101, T2.149, T2.137, T2.147, T2.128, T1.51, T2.113, T3.39, T1.71, T4.40, T4.38 all read `fail`;
  T1.56's note still reads `OPEN FRAMEWORK DEFECT - NOT YET FIXED`. Code re-read, not inherited:
  `Community_Mod_Chest/Patcher/CopperChestPatch.cs` still invokes `GetSlotForCard` with 4 arguments while
  `.decomp/GraphicsManager.cs:2587` declares 5; `MillDutyPatch.StationDutyBaseWeight = 850`, and all five
  station UID constants match their CardData `UniqueID`s; the forge and workshop `CompatibleDutiesWarpData`
  arrays carry both the Firekeeping GUID and their station duty UID; `CSFFModFramework/Api/ActionRouter.cs`
  still resolves `_cocReceiverIdx` as the first `InGameCardBase` parameter;
  `Sirus23_Mod_Collection/GameSourceModify/VanillaFox_Agent1.json` `TriggerCardsWarpData` still holds only
  the three dried-berry GUIDs; `SheepPenPatch.cs` still returns before the roll when a wolf is present;
  `VillageFireplacePatch.LogVerdict` (Info, once per change) is present, added in 1.68.19; all four ACT
  Brazier/Lantern CardData halves carry `UsesOwnershipSystem: true`. Deployed config read directly:
  `BepInEx.cfg` `[Logging.Disk] LogLevels` includes Debug, `crispywhips.CSFFModFramework.cfg`
  `VerboseLogging = true`, `crispywhips.partner_overhaul.cfg` `Reserved Fuel/Wood UIDs` empty.
  `ls */.audit/legacy-card-refs*` returned nothing (control: the same shell listed 13 `.audit/` folders).
  Citation sweep: 3 live pointers repointed (`.claude/commands/plan-to-prompts.md`,
  `.claude/commands/audit-plan.md`, `Playthrough_Test/README.md`) plus 3 memory files; two dated records
  (`CSFFModFramework/CHANGELOG.md` [2.22.5] and the `MillDutyPatch.cs` doc comment) left as written, and
  the archive's header names the old path so a grep for it still lands.
- **Verification debt:** T2.89, T2.90, T2.212 (new), T2.207 then T2.99, T2.101, T2.149, T2.137, T2.147,
  T2.128, T1.51, T2.113, T3.39, T1.71, T4.40, T4.38, all present in `.claude/playthrough-test-status.json`.

### 2026-09-09 - Fleet Master_Plan (Scope: Fleet)
- **Scope:** Fleet. The same entry is recorded in AdvancedCopperTools, CSFFModFramework,
  Community_Mod_Chest, HerbsAndFungi, Sirus23_Mod_Collection and WaterDrivenInfrastructure
  (PartnerOverhaul, also named by the plan, has no ROADMAP.md).
- **Verdict:** 0 of the 7 prompts and 1 decision carried by the 2026-09-08 rewrite remain as code.
  Prompts 1-4 BUILT, awaiting in-game verification (CMC 1.68.22, Sirus23 1.21.1, PartnerOverhaul
  1.0.4 + tracker re-arm, CSFFModFramework 2.25.29); Prompts 5 and 6 verified done 2026-09-09;
  Prompt 7 REFUTED (its premise was false, nothing built); the OPEN-DECISION (T2.101) taken as
  delegated and shipped as Sirus23 1.21.2 (partial wolf guard). 5 items awaiting verification, all
  with tracker rows.
- **Disposition:** ARCHIVE. Plan moved to `Documentation/Design/Fleet_Master_Plan_As_Built.md`
  with the refreshed pack appended as an appendix; the pack file deleted from
  `Documentation/Plans/Fleet/`. Inbound citations (tracker row sources, two earlier As_Built notes,
  three memory files) repointed or annotated in the same commit.
- **Pruned:** section 1.6 / Prompt 7 (closed as refuted, recorded in
  `Community_Mod_Chest/.audit/legacy-card-refs-2026-09-08.md` Correction 2); section 2.1 /
  OPEN-DECISION (decided; reasoning kept in
  `Documentation/Ideas/Sirus23_Mod_Collection/SHEEP_PEN.md`). Sections 1.1-1.4 were already
  collapsed to awaiting-verification pointers by the 2026-09-09 refresh and are unchanged.
- **Evidence:** Prompt 7: `git blame -L369,369 -- Community_Mod_Chest/TradingValues.json` returns
  `fa3dffd6f` (2026-07-27) for the row pricing `85f90db88eaa1804186ea7c3e561dd92` at 1500;
  `git log --oneline -- Community_Mod_Chest/TradingValues.json` returns that single commit; the
  export's `CardData/LeatherGloves.json` re-read as UniqueID `85f90db88eaa1804186ea7c3e561dd92`,
  `TradingValue` 0.0 (so the listed row is what prices it); grep for that GUID across every
  `TradingValues.json` and `GameSourceModify/` file returns only the CMC row (no later override).
  T2.101: r33 log quoted in the tracker row (five nights of `stood guard`, zero losses, 7/7/7/7/3
  unpenned); `SheepPenPatch.cs` early return replaced by `GuardedPredationChance` 0.01f; Release
  build 0 warnings / 0 errors; `Deploy-Mods.ps1 -Sirus23ModCollection` deployed 84 CardData, DLL
  byte-identical to bin/Release, the new literal `wolf on guard` present in the DLL's #US heap
  (UTF-16-LE); zip `Sirus23_Mod_Collection_1-21-2.zip`. Pack header at archive: 0 open, 5
  awaiting verification.
- **Verification debt:** T4.40, T4.38 (CMC Copper Chest); T2.207 then T2.99 (Sirus23 fox bait);
  T3.39, T1.71, T2.147, T2.128 (tracker hygiene, PartnerOverhaul count line, clone-tile live
  trim); T1.56 via T1.81 (framework card-on-card dispatch); T2.101 and T2.232 (Sheep Pen, without
  and with a wolf); plus the section 3 rows T2.89, T2.90, T2.212, T2.149, T2.137, T1.51, T2.113.
  All present in `.claude/playthrough-test-status.json`.
