# Roadmap: Utility Belt
Version at time of writing: 0.1.5
Date: 2026-10-18
Audit score: 9/10 (PASS, 0 CRITICAL / 0 DESIGN GAP / 0 open WARNING)

## Current State

**Theme**: A quality-of-life mod that gathers small utilities into one download for players who would rather install one thing. It bundles Repeat Action 2.2.2, Quick Transfer 1.9.1 and Skill Speed Boost 1.11.1, and adds its own accessibility, display, inventory and equipment options. It ships no cards, items, blueprints, structures or perks by design.

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom images (adds no card content by design) - ~21 C# source files; ~16+ config options/hotkeys across Diagnostics, Accessibility, Display, Character, Equipment, RepeatAction and Hotkeys sections.

**Stability**: 9/10 - 0 CRITICAL, 0 DESIGN GAP, 0 open WARNING. The 2026-10-18 code-quality pass scored 10/10 (20 .cs files read in full); its one flagged warning (G2, a patch-handler exception logged at `LogDebug` in `ContainerFilterPatch.cs`) was fixed the same day in commit `00c1ed5a0` (now routed through `WarnOnce`). Release build 0 errors / 0 warnings; SimpEn/SimpCn parity clean 30/30; feature honesty exact (every config option wired to a `Tick()` or patch site). The single point docked is unverified in-game behaviour coverage (per-feature playthrough), not a known defect.

**Open work**: none open in `Documentation/Retrospectives/INDEX.md` (no entry matches UtilityBelt by folder, shorthand or GUID `crispywhips.utility_belt`). The 0.1.0, 0.1.4 and in-flight 0.1.5 plans were all built and archived (see Plan Reconciliation Log); no `Documentation/Plans/UtilityBelt/` folder exists, which is correct for a drained plan. As-built scope lives in `Documentation/Design/UtilityBelt_As_Built.md` and `Documentation/Design/UtilityBelt_0_1_4_As_Built.md`.

**Framework compliance**: N/A by design. UtilityBelt declares no `[BepInDependency]` and is a Harmony-only client-side QoL mod - it does not consume framework Tier 2 services (ActionRouter/SpawnService/TickEvents/ContentModPlugin) because it ships no content and patches the game directly. The bundled Quick Transfer and Skill Speed Boost modules carry their own framework soft-dependency. All Harmony patches target large callers via compile-time `typeof(...)`, cache reflection lookups, null-guard every `AccessTools` result, and `OnDestroy` unpatches and unsubscribes.

---

## Phase 0: Stabilize

> Skipped - audit score is 9/10 and no open retrospectives exist. Nothing to stabilize.

The only genuinely unverified item is per-feature in-game behaviour (sort order, swap outcome, filter hiding, randomiser band-matching), which is human-playthrough / TestHarness work tracked in `.claude/playthrough-test-status.json` (rows T2.1400-T2.1428), not a code defect.

---

## Phase 1: Foundation

> Table-stakes maintenance. All currently satisfied; listed as recurring health checks.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Versions synced across ModInfo (0.1.5) / Plugin.cs (0.1.5) / README (0.1.5) | Version hygiene | - done | Quick |
| SimpCn.csv parity (30/30 clean) | Localization | - done | Quick |
| Re-date `mod-report.md` / `preflight.json` (currently 2026-10-13, v0.1.2 state) via `/audit-mod UtilityBelt` | Audit freshness | P1 | Quick |
| Record the passing 0.1.4/0.1.5 harness rows (T2.1418-T2.1428) that ran PASS but are not yet autoplay-recorded | Verification debt | P2 | Medium |

---

## Phase 2: Core Expansion

> UtilityBelt is feature-complete for its stated scope; the README states nothing more is planned. No content expansion is scoped - it is a QoL bundle, not a content mod. Any new QoL option is a candidate, not a committed addition (see Long-term Vision).

---

## Phase 3: Integration & Depth

> The mod's integration surface is already its reason for existing: it bundles and hooks three standalone mods.

### Keep bundled module versions in lockstep with their standalones
**What**: Repeat Action 2.2.2, Quick Transfer 1.9.1, Skill Speed Boost 1.11.1 are embedded. When any standalone ships a new version, re-embed it and bump UB.
**Why**: a bundled module drifting behind its standalone is the one honesty risk unique to this mod's design.
**Requires**: coordination with each module's own release.
**Complexity**: Quick (per bump), covered by `Development_Tools/Tests/UtilityBelt-Bundle.Tests.ps1`.

---

## Phase 4: Polish

> No card art to produce (0 custom images by design). Polish here is text and log hygiene.

| Item | What | Complexity |
|------|------|------------|
| Description accuracy pass | Re-read ModInfo Description / README against config defaults after each option added (last pass clean) | Quick |
| Log-level consistency | G2 closed; keep genuine failures at LogWarning/LogError and verbose tracing at LogDebug | Quick |

---

## Long-term Vision

> Where Utility Belt should be at v1.0.

A single, trusted QoL download that a player installs once and forgets, covering the accessibility, display, inventory and equipment conveniences the base game lacks, while staying honest that its three headline features are themselves standalone mods bundled for convenience. Its natural endpoint is a stable 1.0 once every current option has a recorded in-game verification pass and the three bundled modules have each reached their own 1.0-equivalent maturity. The biggest not-yet-justified addition is a small in-game options panel (rather than BepInEx config-file editing) for its ~16 toggles - natural only if the option count keeps growing.

**Potential major additions** (not yet justified - revisit after the current options are all playthrough-verified):
- In-game config UI for UB's own toggles - fits the accessibility theme; justified only as the option list grows past comfortable config-file editing.
- A per-feature on/off summary readout - fits the "install once and forget" goal by showing at a glance what is active.

Any of these worth building belongs in a fresh `Documentation/Plans/UtilityBelt/` plan (or its Considered table, with the reason); never a stub under `Documentation/Ideas/`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new option/feature | Run `/audit-mod UtilityBelt` and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes, re-run `/diagnose-log` |
| A bundled module ships a new standalone version | Re-embed it, bump UB, run `UtilityBelt-Bundle.Tests.ps1` |
| After fixing any issue | Run `/critical-analysis UtilityBelt` to verify |

---

## Skill Cheatsheet for This Mod

```
/audit-mod UtilityBelt         - full health check, updates .audit/
/critical-analysis UtilityBelt - adversarial review
/code-quality UtilityBelt      - C# reliability scan
/build-mod UtilityBelt         - build Release DLL
/deploy-mods UtilityBelt       - build + deploy to game
/update-mod-version UtilityBelt <ver> - bump version in all 3 files
/export-to-repo UtilityBelt    - push to public repo
/nexus-publish upload UtilityBelt - publish release to Nexus
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

### 2026-10-17 - MASTER_PLAN (0.1.4 plan closeout, session 9b55e1df, mission ub-plan-close-2026-10-17)
- **Verdict:** counts: 7 Selected rows (B7 to B13), 1 Wave 0 item (R11) and 9 pack prompts checked. Built: 7 of 7, all in 0.1.4 (live on Nexus 2026-10-18T03:43Z). R11 answered from the code on Harman (no numbers in vanilla's Encumbrance popup, so B9 was built). One defect found and fixed: the B11 worn mark landed on food and wood (0.1.5, below). No code work remains.
- **Disposition:** ARCHIVE - `Documentation/Plans/UtilityBelt/MASTER_PLAN.md` and its Implementation Prompts pack removed; the plan's text with **As built** lines lives in `Documentation/Design/UtilityBelt_0_1_4_As_Built.md`. Harman had already archived it on 2026-10-16 (`2f0164a7c` added the as-built), but the port to Jareds-Desktop kept the two plan files and carried none of the eleven tracker rows the as-built named, so both were redone here.
- **Evidence:** read and run 2026-10-17 and 2026-10-18 on Jareds-Desktop. `dotnet build -c Release` 0 errors / 0 warnings; `UtilityBelt-ConfigDefaults.Tests.ps1` and `UtilityBelt-Bundle.Tests.ps1` 21 passed, 0 failed. Harness on dev lane rig 6, each scenario run alone with `csff.py run`: B7 T2.1418 PASS 94/94, B8 T2.1419 PASS 39/39, B9 T2.1420 PASS 28/28, B10 T2.1422 PASS 68/68 and B13 T2.1427 PASS 49/49 on 0.1.4 (md5 0bf2ed26); B11 T2.1423 FAIL on 0.1.4 (Raw Meat carried the mark: its Usage slot is vanilla's Drying counter, active, max 24, at 0), then PASS 38/38 on 0.1.5 (md5 58ccec63); B12 T2.1425 failed once on a scenario fault (the starting Flint took the lock) and passed 59/59 once the scenario removed it. Reports under `TestHarness/client/reports/ub-b*-2026101[78]-*`.
- **0.1.5 (Unreleased):** `WornToolMark.IsWearStat` marks only Usage that is wear (an OnZero action, no OnFull action, the default Usage name); 184 of the 337 vanilla Items with an active Usage stat pass it. Version bumped with `Update-ModVersion.ps1`; not published (the owner's `/publish-mod` step).
- **Verification debt:** pending in `.claude/playthrough-test-status.json`: harness rows T2.1418, T2.1419, T2.1420, T2.1422, T2.1423, T2.1425 and T2.1427 (each run PASS above, not yet recorded by autoplay), person rows T2.1421 (carry-weight tooltip look), T2.1424 (worn mark look), T2.1426 (filter box focus and typing, windowed) and T2.1428 (pin mark look).
