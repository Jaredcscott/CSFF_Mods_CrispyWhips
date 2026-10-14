# Roadmap: Utility Belt
Version at time of writing: 0.1.2
Date: 2026-10-13
Audit score: 9/10 (PASS, 0 CRITICAL / 0 WARNING)

## Current State

**Theme**: A quality-of-life mod that gathers small utilities into one download for players who would rather install one thing. It bundles Repeat Action 2.2.2, Quick Transfer 1.9.1 and Skill Speed Boost 1.11.1, and adds its own accessibility, display and inventory options (hide rain/snow, 12-hour clock, equipped-bag hotkeys, item lock, container sort, capacity/warmth readouts, quickswap, sealed mark, character randomiser, and three Repeat Action helpers).

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom images (adds no card content by design) - 21 C# source files, 16 config options/hotkeys.

**Stability**: 9/10 - 0 CRITICAL, 0 DESIGN GAP, 0 WARNING. Build 0 errors / 0 warnings; SimpEn/SimpCn parity clean 28/28; feature honesty exact (every config option wired to a Tick()/patch site). The single point docked is unverified in-game behaviour coverage (per-feature playthrough), not a known defect.

**Open work**: none open in `Documentation/Retrospectives/INDEX.md` (no entry matches UtilityBelt by folder, shorthand or GUID `crispywhips.utility_belt`).

**Framework compliance**: N/A by design. Plugin.cs declares no `[BepInDependency]`; the mod reaches its three bundled modules only by reflection through `Chainloader.PluginInfos`, and the bundled Quick Transfer / Skill Speed Boost need CSFF Mod Framework themselves. The Tier 2 card-mod services (ActionRouter/SpawnService/TickEvents) do not apply to a card-free utility mod. Harmony discipline is correct (large-caller targets, `AccessTools.Method` resolution with LogWarning-and-return, explicit per-patch `harmony.Patch`, `OnDestroy` unpatch/unsubscribe).

---

## Phase 0: Stabilize  *(skipped - audit score >= 8 and no open retrospectives)*

Nothing to stabilize. 0 CRITICAL, 0 WARNING, no open retros.

---

## Phase 1: Foundation

> The code foundation is already in place. What remains is verification and release hygiene, not code work.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Close the 18 pending human-verification rows (T2.1400-T2.1417) in `.claude/playthrough-test-status.json` | Verification debt | P1 | Complex (human playthrough) |
| Release 0.1.2: `/export-to-repo UtilityBelt`, add the public Mod_Repos folder, publish to Nexus (page 55) and refresh the MUM suite entry | Release work | P1 | Medium |
| After full human testing, move the mod into `Stable/` with `Move-ModFolder.ps1` (owner gate, 2026-10-12) | Lifecycle | P1 | Quick |
| Re-run `/build-mod UtilityBelt` if RepeatAction or QuickTransfer move back to the repo root (csproj currently builds against `..\Stable\...`) | Build hygiene | P2 | Quick |

Version hygiene is already clean (0.1.2 in ModInfo.json, Plugin.cs, README.md) and localization parity is clean (28/28), so those table-stakes items need no work.

---

## Phase 2: Core Expansion

> No code expansion is scoped. The mod's plan is drained and archived.

The entire feature set was built and the plan was closed out on 2026-10-12 (ARCHIVE disposition, see the Plan Reconciliation Log below). The full scope, as-built detail and the "Considered, not selected" table with rejection reasons now live in `Documentation/Design/UtilityBelt_As_Built.md`. No `Documentation/Plans/UtilityBelt/` folder exists, which is correct for a drained plan. The README "Planned" section confirms: "Nothing more is planned for this version's features."

Do not re-propose any row from the As-Built "Considered, not selected" table (for example U3, closed with no code) - each carries its rejection reason there.

---

## Phase 3: Integration & Depth

> No new integration work scoped.

The mod already integrates with its three bundled modules by reflection: Auto-Pull / Store-Products / Spare-Tool hook Repeat Action's public events (`OnRequirementsUnmet`, `OnIterationStarting/Done`), and the item-lock is honoured by Quick Transfer batches and Repeat Action runs via a reflection bridge. No further cross-mod coordination is justified at the current scope.

---

## Phase 4: Polish

> No art, animation or description work outstanding.

The mod ships no card art (0 items, 0 images). The 2026-10-13 audit found no description/field mismatches on the items spot-checked (lock scope, sort cycle order, hotkey defaults, Diagnostics log line format), and feature honesty passed with every documented option mapped to a live call site.

---

## Long-term Vision

> Where this mod should be at v1.0.

Utility Belt's natural endpoint is the single-download home for the crispywhips QoL mods plus a growing set of small accessibility, display and inventory conveniences - the "install one thing" alternative to the separate Quick Transfer, Repeat Action and Skill Speed Boost pages, which keep shipping standalone. v1.0 is reached once 0.1.2 is publicly released, the 18 verification rows are confirmed in-game, and the mod has moved into `Stable/`. The biggest addition that becomes natural at larger scale is folding further small standalone utilities into the same bundle as they are written, each removable by deleting its DLL.

**Potential major additions** (not yet justified - revisit only if the owner scopes new utilities):
- Additional bundled QoL modules as they are authored - fits the mod's stated "gathers small utilities into one download" identity.
- More per-player accessibility options in the same Accessibility/Display sections - fits the existing Hide Rain And Snow / Clock Format pattern.

Any of these worth building belongs in a new `Documentation/Plans/UtilityBelt/MASTER_PLAN.md` (or its Considered table, with the reason); never a stub under `Documentation/Ideas/`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod UtilityBelt` and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes, re-run `/diagnose-log`; rebuild to surface any bundled-module signature drift |
| After fixing a critical issue | Run `/critical-analysis UtilityBelt` to verify the fix |
| Before a public release | Run `/audit-mod` + `/export-to-repo UtilityBelt`, repackage the MUM suite |
| If RepeatAction/QuickTransfer move folders | Re-run `/build-mod UtilityBelt` to confirm the csproj was repointed |

---

## Skill Cheatsheet for This Mod

```
/audit-mod UtilityBelt          - full health check, updates .audit/
/critical-analysis UtilityBelt  - adversarial review
/build-mod UtilityBelt          - build Release DLL
/deploy-mods UtilityBelt        - build + deploy to game
/update-mod-version UtilityBelt <ver> - bump version in all 3 files
/export-to-repo UtilityBelt     - push to public repo
/playthrough-test-plan status   - check the 18 pending verification rows
```

---

## Plan Reconciliation Log

### 2026-10-04 - MASTER_PLAN
- **Verdict:** counts: 19 pack prompts and 18 Selected rows (P1-P4, B1-B6, U1-U4, C1-C3, P8; P0 is the Wave 0 research row) checked, plus 10 R items and E1. Verified done: 0. Built, runtime gates open (agent-run, not human rows): 3 (P1 scaffold `0d02e11d1`, P2 Repeat Action bundled `3ee83bbea`, E1 `pluginsAside` `a97453246`). Open: 19 prompts. Wave 0 offline answers for R4-R7 and R10 (art half) were already recorded at `21ddc90be`; R1-R3 and R8, R9 have no runtime answer yet.
- **Disposition:** KEEP IN PLACE (open work remains; the first open prompt is Prompt 1, the runtime gates for P1, E1 and P2).
- **Pruned:** nothing deleted (no item is verified done). Corrected three stale claims: the plan's "pack's Awaiting Verification list" pointers (sections 2.2 E1, 3, 4) now name the pack's "Built, runtime gates open" list; plan section 1.3 and P4's rules no longer say SkillSpeedBoost's drained round-2 plan still owns SSB features (cites the as-built record); pack Prompt 3's matching sentence. Added one dated status paragraph to the plan.
- **Evidence:** read by script or grep on 2026-10-04. `PluginVersion` consts: QuickTransfer/Plugin.cs 1.9.0, RepeatAction/Plugin.cs 2.2.0, SkillSpeedBoost/Plugin.cs 1.11.0, UB host 0.1.0. `UtilityBelt/ModuleRegistry.cs` has `Repeat_Action.dll` Bundled true and the other two false; `Utility_Belt.csproj` has `BuildBundledModules` and `CopyBundledModules` for Repeat_Action only; `Deploy-Mods.ps1` has the `-UtilityBelt` switch and `-BundledDllNames @("Repeat_Action")`; `UtilityBelt-Bundle.Tests.ps1` carries checks (a) and (e) (and no (f), as expected before Prompt 2); `UtilityBelt/bin/Release` holds exactly Utility_Belt.dll, Repeat_Action.dll, ModInfo.json, LICENSE, ASSET_CREDITS.md; `walkthrough.py` has the `pluginsAside` code. QT: `IsPinnedCard` guard at QuickTransferPatch.cs line 877 inside the candidate check, `KeyCode.X` at Plugin.cs line 251; RA `KeyCode.G` at Plugin.cs line 160. `.decomp/` anchors (all present): `SpecialEffectsManager.cs` HideWeatherEffects line 176; `GraphicsManager.SlotsLerpSpeed` line 159 read by `CardSlot.cs` lines 559-561; `GraphicsManager.cs` line 2124 `ReasonForNotEquipping`; `MainMenu.cs` EquipPerk(int,bool) line 1681, UnequipPerk(int,bool) line 1741, CheckThatMandatoryPerksAreValid line 1609, substring Contains line 1745, GetRating line 1581; `InspectionPopup.cs` EmptyInventory line 2175, SortInventory line 1972. Tracker: all 13 T-ids cited in the plan and pack (T1.124, T1.125, T2.181, T2.182, T2.213, T2.215, T2.218, T2.219, T2.331, T2.332, T2.333, T2.650, T2.651) each match exactly 1 row in `.claude/playthrough-test-status.json` (grep on the quoted id; controls: the same loop over the ids returned 1 for every id, none 0). Zero UB rows exist in the tracker (grep for UtilityBelt and Utility_Belt returned 0; control: the same file holds the QT and RA rows the loop found), and `.claude/nexus-publish-status.json` has no UB record. `Documentation/Plans/QuickTransfer/` and `Documentation/Plans/RepeatAction/` still hold plans, so Prompt 15's precondition is not met yet. Not verified: the deployed `Utility_Belt/` folder state and the game (no game run, per rules).
- **Verification debt:** no UB tracker rows exist yet; Prompt 1 files the scaffold-boot row (the scenario `T2.1290_ub_scaffold_boot.json` still reads tracker id UNFILED) and the later build prompts file their in-game rows.

### 2026-10-07 - MASTER_PLAN (cleanup-plans fleet sweep)
- **Verdict:** counts: 18 Selected rows (P0 research, P3, P4, B1-B6, U1-U4, C1-C3, P8) and 18 pack prompts (2-19) checked, plus R3 (QT, SSB) to R10. Verified done and pruned: 0 (nothing new landed). Obsolete: 0. Awaiting verification: 1 (the P1+P2+E1 unit, already pruned from the plan on 2026-10-06; T2.1290 pending with every harness leg PASS, unrecorded; added as the pack's one Awaiting verification line). Failed: 0. Stale corrected: 3 (Repeat Action now 2.2.1 so the plan's and pack's "check (e) red, RA next is 2.2.1" lines were history, rewritten as the measured state with check (d) red; the bundling pattern in plan section 5, section 20 and pack Prompts 2 and 3 lacked the Pack-Suite `ExtraDlls` list that `cabac799f` added; Q3 updated for 2.2.1). Open: 18 prompts, every Selected row.
- **Disposition:** KEEP IN PLACE - 18 open prompts of code work remain; the first, Prompt 2 (P3 Quick Transfer bundled), has no unmet dependency.
- **Pruned:** nothing deleted. Added a dated status paragraph to the plan; replaced the pack's Standing rules bundle-state sentences; added the pack's Awaiting verification list (T2.1290).
- **Evidence:** `git log ce23dd71c..HEAD -- UtilityBelt` is empty (control: `git log -5 -- UtilityBelt` lists `d085eedfe`, `3ee83bbea`, `0d02e11d1`). `UtilityBelt/ModuleRegistry.cs` still bundles only Repeat_Action.dll; `Utility_Belt.csproj` holds no 0Harmony or Assembly-CSharp reference and no `Patcher/` or `Localization/` folder exists, so no U, B or C feature has started. `PluginVersion`: RA 2.2.1 (`976e8321c`), QT 1.9.0, SSB 1.11.0, UB 0.1.0. md5 of `UtilityBelt/bin/Release/Repeat_Action.dll` and `RepeatAction/bin/Release/Repeat_Action.dll` both `6581b6d218d2318e3541342ab2484b55`. `UtilityBelt-Bundle.Tests.ps1` run offline: 11 passed, 1 failed, the failure (d) "README lists Repeat Action 2.2.0 but Plugin.cs is 2.2.1" (its own fixture test shows (d) can go red and green). `Development_Tools/Pack-Suite.ps1` UB entry `ExtraDlls=@("Repeat_Action.dll")`; no gate reads it (grep of `ExtraDll` across `Development_Tools/Tests/` hits only Deploy-Mods' framework-companion check). Tracker: T2.1290 (`ub_scaffold_boot`) is the only UtilityBelt row, status pending; no built unit lacks a row. `.claude/nexus-publish-status.json`: UB 0.1.0, RA 2.1.6, QT 1.8.1, SSB 1.10.6. Cited scenarios QT T2.215 and T2.651, SSB T1.124 and T1.125, RA T2.218 exist; QT `IsPinnedCard` guard and `KeyCode.X`, RA `KeyCode.G` present. QuickTransfer and RepeatAction plans still exist, so Prompt 15's precondition is unmet. No game was run.
- **Verification debt:** T2.1290.

### 2026-10-09 - MASTER_PLAN
- **Verdict:** counts: 18 Selected rows (P0 research, P3, P4, B1-B6, U1-U4, C1-C3, P8) and 18 pack prompts (2-19) checked, plus R3 (QT, SSB) to R10. Verified done: 1 (the P1+P2+E1 unit, pack Prompt 1, whose tracker row T2.1290 was recorded confirmed 2026-10-09 in `51641ef64`; not on the addendum's list of 40 skeptic-rejected PASSes). Obsolete: 0. Awaiting verification: 0. Failed: 0. Stale corrected: 0. Open: 18 prompts, every Selected row.
- **Disposition:** KEEP IN PLACE - 18 open prompts of code work remain. Next wave: Prompt 2 (P3 Quick Transfer bundled, deps none, game lane) dispatches now; Prompts 3 to 13 follow strictly one at a time (all game lane); Prompt 14 may slot in between Prompts 9 and 13; Prompts 15 to 18 wait on the Quick Transfer and Repeat Action round-2 plans being archived; Prompt 19 closes out.
- **Pruned:** the pack's Awaiting verification list (its one T2.1290 bullet) and that line in the header; no plan section deleted (the unit's sections were already pruned on 2026-10-06). Added a dated status paragraph to the plan, rewrote the pack header against `1405a21d4`, and marked the pack's bundle-state sentence re-measured. Uncommitted at writing (the coordinator commits).
- **Evidence:** `git log 189f8b99e..HEAD -- UtilityBelt RepeatAction QuickTransfer SkillSpeedBoost Development_Tools/Pack-Suite.ps1 Development_Tools/Tests/UtilityBelt-Bundle.Tests.ps1 TestHarness/client/scenarios/UB` returns 4 commits, none touching UB or module source (`56ef680ed` and `850b3dc9d` plan and ROADMAP text, `48b0a0c1c` the T2.1290 scenario, `e57d44cfc` only `SkillSpeedBoost/bin/Release/Skill_Speed_Boost.dll`); control: the same command lists those 4, so it can hit. Tracker: T2.1290 sits in `confirmedItems` (`ub_scaffold_boot`, `confirmedOn` 2026-10-09) and is the only UtilityBelt row (Python scan of `items`, `confirmedItems`, `droppedItems` for UtilityBelt/Utility Belt/Utility_Belt: 1 hit). All 15 T-ids the plan and pack cite match exactly one row each (grep on the quoted id). `PluginVersion`: RA 2.2.1, QT 1.9.0, SSB 1.11.0, UB 0.1.0. md5 of both `Repeat_Action.dll` copies `6581b6d218d2318e3541342ab2484b55`. `Invoke-Pester UtilityBelt-Bundle.Tests.ps1`: 11 passed, 1 failed, (d) "README lists Repeat Action 2.2.0 but Plugin.cs is 2.2.1" (Prompt 2 fixes it). Prompt 2 not started: Pack-Suite UB `ExtraDlls=@("Repeat_Action.dll")`, `Deploy-Mods.ps1` `-BundledDllNames @("Repeat_Action")`, no `UtilityBelt/Localization/`, README "Quick Transfer and Skill Speed Boost are not included yet". `git status --short` on UtilityBelt, QuickTransfer, RepeatAction and SkillSpeedBoost: empty. Cited scenarios QT T2.215 and T2.651, SSB T1.124 and T1.125, RA T2.218 exist; QT `IsPinnedCard` guard present. `.claude/nexus-publish-status.json`: UB 0.1.0, RA 2.1.6, QT 1.8.1, SSB 1.10.6. `Documentation/Plans/QuickTransfer/` and `Documentation/Plans/RepeatAction/` still hold plans, so Prompt 15's precondition is unmet. `git worktree list`: the main checkout only. No game was run.
- **Verification debt:** none open for this plan (T2.1290 confirmed); the build prompts file their own rows.

### 2026-10-12 - MASTER_PLAN (closeout, UB pack Prompt 19)
- **Verdict:** counts: 18 Selected rows (P1-P4, B1-B6, U1-U4, C1-C3, P8) and 19 pack prompts checked. Built: 16 rows (P1 to P4 earlier; U1, U2, B1 steps 1-5, B2, B3, B4, B5, B6, U4, C1, C2, C3); 0 ruled out; U3 closed with no code (Considered row 41, R8 measured); P8 resolved on the owner's answer to Q4. Every R item (R1 to R10) carries a dated answer in the plan's 2.1 or 2.2 (R9's M-1 to M-11 lines measured at the game). No code work remains.
- **Disposition:** ARCHIVE - moved whole to `Documentation/Design/UtilityBelt_As_Built.md` with its Considered table and rejection reasons; the Implementation Prompts pack was removed (precedent `c39509b5d`, `1c6a69632`). The mod stays at `UtilityBelt/` until it has been fully human tested (owner, 2026-10-12), and only then moves to `Stable/`.
- **P8 outcome:** owner answer to Q4 recorded 2026-10-12 (`adec40aa5`): "Yes, add at next release". Nexus page 55 (0.1.0 published) and the Mod Update Manager suite pre-release entry already exist; the public Mod_Repos folder is added by `/export-to-repo` at the next release, which is release work and not code, and was NOT run here. Plan Q3's version rule binds from the first public UB bundling these module builds (none of Repeat Action 2.2.1, Quick Transfer 1.9.0 or Skill Speed Boost 1.11.0 appears in `.claude/nexus-publish-status.json`).
- **Shipped by commit:** P1 `0d02e11d1`, P2 `3ee83bbea`, P3 `076f1b0c4`, P4 `0fb569875`, U1 `39b1f5d95` (re-run `9722969af`), U2 `cfaa072f8`, U3 closed `70c5f8e2e`, B4 `b6b340ef0`, B1 steps 1-3 `d5dd66648`, B2 `48d74ce8f`, B3 `7400d8b9a`, B5 `3a74f2d11`, B6 `b6c8fd8f7`, U4 `6cf3d8212`, B1 steps 4-5 `4ac24e61a`, C1 `0c429ff76` with `03d8dba14` and `da790ac44`, C2 `c0660a6c6` with `5398a25d8`, C3 `2dc0d0662`. Prompt 18 found R9's OnToolBroke and OnRunStopped seams unnecessary (the existing OnRequirementsUnmet event covers a broken worn tool); recorded in plan 2.2 and section 17.
- **Evidence:** read 2026-10-12 against HEAD `adec40aa5`: `/audit-mod UtilityBelt` preflight 0 CRITICAL / 0 WARNING, Section H clean (every ModInfo Description clause, README feature and CHANGELOG line has a bound ConfigEntry and a call site); `dotnet build -c Release` 0 errors / 0 warnings; gates green: Deploy-Mods 38, Version-Synchronization 24, Csproj-ContentIncludes 11, UtilityBelt-Bundle 15, Localization-DuplicateKeys 52, Localization-EnglishColumnCjk 28, Check-LocalizationParity CLEAN 28/28, PlaythroughRow-Citations 13, Playthrough-ChecklistSync 10, Playthrough-TrackerMapShape 4. Tracker rows T2.1400 to T2.1417 each exist (18 of 18).
- **Verification debt:** all pending in `.claude/playthrough-test-status.json`: U1 T2.1400 (harness) and T2.1401 (person); U2 T2.1402; B4 T2.1403; B1 T2.1404 (harness), T2.1405 (person, mark legibility) and T2.1414 (QT and RA lock bridges); B2 T2.1406; B3 T2.1407 and T2.1408 (person, line fits in English and Chinese); B5 T2.1409; B6 T2.1410 and T2.1411 (person); U4 T2.1412 and T2.1413 (person); C1 T2.1415; C2 T2.1416; C3 T2.1417. Not covered by any harness run, carried as caveats: a pile of one model holding a locked and an unlocked card (B2), a CannotBeTransferred card and an NPC equip (B5), starting a run with a rolled character (U4), a worn bag opened by Sort (B2).
