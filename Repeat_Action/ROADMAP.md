# Roadmap: RepeatAction
Version at time of writing: 2.2.1
Date: 2026-10-11
Audit score: 10/10 - release-ready (consolidated 2026-10-11; critical-analysis SOLID 2026-10-11, code-quality 10/10 2026-10-11)

> **2026-10-11 - consolidated audit refresh.** 10/10 PASS. 0 CRITICAL, 0 DESIGN GAP, 1 WARNING (stale
> "popup's Build button" text in the `Saved Sequences` config description and two code comments - fix at
> the Prompt 15 release edit), 2 MINOR (ModInfo Description under-claims Repeat Presets / Pause-Resume;
> UtilityBelt README over-claims "Every kind of action" - out of scope, raise with the UB audit). The
> MASTER_PLAN is active: Features 1–17, 19, 20, 21 and 24 are built; what remains is the release tail
> (Prompt 15 Ship, Prompt 16 Closeout) plus three person-lane in-game verifications (T2.701, T2.718,
> T2.1381). Prompt 15 waits on the owner's ask (owner, 2026-10-10: stop before shipping).

## Current State

**Theme**: A standalone quality-of-life utility that repeats your last player-initiated action (Forage, Chop, Travel, Eat, drag-drop crafting, stack actions, group harvests) N times (or Unlimited) with a single keypress, with pinned actions, recorded sequences, presets, pause/resume, and a wide range of safety stops - halting cleanly the moment the game's own requirement checks stop passing and reporting the game's own reason.

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom images - a pure C# utility (5 source files: `Plugin.cs`, `Patcher/ActionPatch.cs`, `Patcher/Sequences.cs`, `Presets.cs`, `Messages.cs`).

**Stability**: 10/10 - 0 CRITICAL, 0 DESIGN GAP, 1 WARNING (docs text), 2 MINOR (docs under/over-claim). Clean Release build 0/0. Version parity 2.2.1 across ModInfo.json / Plugin.cs / README.md; BinSync clean. The 2.0.0 native-dispatch architecture is soundly engineered: capture via non-mutating, try/catch-wrapped prefixes on the game's public dispatch funnels; timeScale-safe coroutines; re-entrancy-guarded with `isRepeating` reset in `finally`; Harmony targets resolved at compile time via `typeof(GameManager)`. The one reflection path (`CurrentActionBlockers`, `GraphicsManager` typing state) is cached, null-guarded and breadcrumbed, degrading gracefully if a game field is renamed. `lib/Assembly-CSharp.dll` byte-matches the framework canonical.

**Open work**: No code defects. The WARNING and both MINOR items are one-line docs edits scheduled into the Prompt 15 release commit. Remaining plan work is the release tail (Ship + Closeout) and three person-lane in-game verifications (T2.701 real-keyboard half, T2.718 Chinese-toast screenshots, T2.1381 art read). No open or pending retrospectives for RepeatAction (only a graduated `foraging-RA`).

**Framework compliance**: N/A by design - RepeatAction 2.0.0+ is intentionally **standalone** (no `[BepInDependency]`, no CSFFModFramework reference). It drives the game's own public `GameManager` funnels directly rather than through framework Tier 2 services. This is correct for its architecture, not a compliance gap. Reusing `Api.ActionRouter` is a future option explicitly weighed against the standalone stance, not a requirement.

---

## Phase 0: Stabilize  *(skipped - audit score 10/10, no open retrospectives)*

Nothing to stabilize.

---

## Phase 1: Foundation  *(COMPLETE)*

Version hygiene synced at 2.2.1; BinSync clean; localization N/A (0 CSV by design); framework Tier 2 N/A by design (standalone). All 2026-08-24 warnings and hygiene items closed in v2.1.0.

---

## Phase 2–E: Core & Run-Control Features  *(BUILT - in the release tail)*

> The mod's feature scope is tracked in `Documentation/Plans/RepeatAction/MASTER_PLAN.md`. Features 1–17, 19, 20, 21 and 24 are built (count +/-/wheel, Unlimited + backstop, dual keybinds, stat floors/ceilings, nightfall/season/encounter/in-game-hours/quantity stops, pin/fire, refuel-from-pin, spare-tool-on-break, durability floor, pause/resume, presets, recorded sequences, per-card group mode, progress bar, count indicator, verbose diagnostics, completion sound, lock-to-target, iteration delay). What is left is not new features - it is the release.

| Item | Type | Status |
|------|------|--------|
| Prompt 15 - Ship: CHANGELOG fold, deploy, MUM repack | Release | Open - waits on the owner's ask |
| Prompt 16 - Closeout: audit, roadmap, as-built record, Stable move | Release | Open - deps Prompt 15 |
| W1 - replace stale "popup's Build button" config text (`Plugin.cs:260`, `Sequences.cs:23`, `ActionPatch.cs:1519`) | Docs honesty | Fold into Prompt 15 |
| M1 - add Repeat Presets / Pause-Resume to ModInfo Description | Docs honesty | Fold into Prompt 15 |

Person-lane in-game verification (no code work): **T2.701** (real-keyboard half), **T2.718** (Chinese-toast screenshots), **T2.1381** (art read). These are hard gates for Prompt 16's plan clear.

---

## Phase 3: Integration & Depth  *(verification pass)*

> Cross-mod repeat-verification is mostly play-test, not code. See `.audit/ideas.md` Cross-Mod table: WDI (sawmill/workshop), ACT (Grind-All/forge), H&F (drying-rack/oil-press), Sirus23 (shear/feed/dairy), SkillSpeedBoost (sped-up completion timing must not misfire a timeout stop), QuickTransfer (shared re-find primitive). Each needs a confirmed clean stop on its own gate.

**Optional, not scheduled**: promote native-dispatch replay into `Api.ActionRouter`. Re-introduces a framework dependency against the deliberate standalone stance - do not pursue unless that constraint is intentionally relaxed. Complexity: Complex.

---

## Phase 4: Polish  *(COMPLETE)*

Progress bar, count indicator, verbose diagnostics and completion sound all shipped (v2.1.2–v2.2.0). No open polish items.

---

## Long-term Vision

> Where RepeatAction should be at v3.0.

A small, reliable **action-automation layer** that never surprises the player: it repeats any single action honestly (with the game's own stop reason), scales from a fixed count to Unlimited-with-a-backstop, pins and fires deferred actions, records and replays short sequences, carries named presets, and is proven to stop cleanly against every repetitive loop the sibling mods add. The remaining frontier is a **rest-and-retry-once on a recoverable stat block** (design pending - weighed against refuel-from-pin, which already covers the common Eat/Drink case) and continued breadth of recorded-sequence ergonomics. These live in `Documentation/Plans/RepeatAction/MASTER_PLAN.md`; the macro/sequence recorder is already built (Features 20 et al.), and rest-and-retry sits in the plan's Considered table with its reason. RA ships zero content by design.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| Owner asks to ship | Run Prompt 15 (CHANGELOG fold, deploy, MUM repack - framework → content → repack → MUM), folding W1/M1 docs fixes into the commit |
| After Prompt 15 | Run Prompt 16 Closeout once T2.701/T2.718/T2.1381 are filed; move to `Stable/` when the plan drains |
| Game version update | Refresh `lib/Assembly-CSharp.dll` (byte-copy from framework canonical - non-nstrip, plain copy), rebuild, watch the boot-time funnel signature log for drift, run `/diagnose-log` |
| After any feature or fix | Run `/audit-mod RepeatAction` and update this roadmap |
| After a public release | Run `/export-to-repo RepeatAction`; record the build it compiled against in `CHANGELOG.md` |

---

## Skill Cheatsheet for This Mod

```
/audit-mod RepeatAction            - full health check, updates .audit/
/critical-analysis RepeatAction    - adversarial review
/code-quality RepeatAction         - C# reliability/maintainability scan
/build-mod RepeatAction            - build Release DLL
/deploy-mods RepeatAction          - build + deploy to game
/playthrough-test-plan record      - advance the in-game verification items
/update-mod-version RepeatAction <ver> - bump version in all 3 files
/export-to-repo RepeatAction       - push to public repo
```

---

## Plan Reconciliation Log

> Written per `/cleanup-plans` Step 4b (this first entry by the session that built the plan, same
> shape). Append-only: never reword, reorder or delete an entry. `/roadmap` Step 7 preserves this
> section verbatim when it overwrites the rest of this file.

### 2026-09-08 - Audit_Remediation_Plan (second Near-Term tier, promoted 2026-09-05)
- **Verdict:** 5 of 5 feature rows built (N1-N5), one patch release each, v2.1.1 through v2.1.5;
  0 rows ruled out. The Fixes section was empty by design (0 promoted; 0 open CRITICAL and 0 open
  Design Gap at promotion). No code work remains.
- **Disposition:** ARCHIVE - moved to `Documentation/Design/RepeatAction_v2.1.5_As_Built.md`,
  together with the acceptance criteria and flagged design decisions of its Implementation Prompts
  pack (never committed; refreshed 2026-09-07 against `f06153209`, 5 open / 0 awaiting verification),
  which was removed with it. `Documentation/Plans/RepeatAction/` is now empty.
- **Pruned:** the whole plan doc (Features table N1-N5 and its Promotion Log, carried into the
  as-built) and the whole 5-prompt pack; nothing was left behind as a stub.
- **Evidence:** commits `c863847c0` (2.1.1, N1 `Per-Card Group Repeat`), `3eb2d9b4b` (2.1.2, N2
  `Verbose Run Diagnostics`), `8dc02553d` (2.1.3, N3 `Show Count Indicator`), `3dbab945d` (2.1.4,
  N4 `Show Progress Bar`), `294a343f6` (2.1.5, N5 `Extra Stat Thresholds`); each step
  `dotnet build -c Release` 0 warnings / 0 errors against the EA 0.67i lib, each bumped via
  `Update-ModVersion.ps1` with the 3 version strings re-validated in sync. `Plugin.cs` now binds 5
  config entries that the 2026-09-05 promotion's Step 3a grep gate had found absent. Inbound
  reference sweep for `Plans/RepeatAction` over the repo (gitignore-aware Grep) and the memory
  store returned only the plan and pack's own self-references, so nothing needed repointing.
  `git status --porcelain -- RepeatAction/` was clean before the first edit and every commit staged
  exactly the seven RepeatAction paths it named.
- **Verification debt:** T2.216, T2.217, T2.218, T2.219, T2.220 present in
  `.claude/playthrough-test-status.json` `items`, all `pending` (five new `ra_2_1_*` slugs, checked
  against `items`, `confirmedItems` and `droppedItems` before insertion; ids were allocated from the
  live ceiling after a reserved range T2.213-T2.217 went stale within minutes). Plan-lifecycle gate 2
  is therefore satisfied. Still open from the previous tier: T2.164 and T2.165 (`skip` in r33).

### 2026-09-28 - Content_Update_Plan (RepeatAction 2.2.0, written 2026-09-26)
- **Verdict:** 5 of 5 features built, in one 2.2.0 release (the plan's recommended default over five
  patches): `Iteration Delay (seconds)`, `Stop At Nightfall` + `Stop At Season Change`, `Lock To
  Original Target`, `Play Completion Sound` and Pinned Action (`Pin Action Key` P, `Fire Pinned Action
  Key` F, with either modifier). 0 features ruled out. No code work remains.
- **Deliberate departures from the plan text** (each also in the as-built header and the tracker notes):
  "Stop Before Night" shipped as Stop At Nightfall, where only a run that has been in daylight stops, so
  the setting cannot make repeats unusable after dark. The lock also skips `FindActionAnywhere`, which
  the plan missed and which would otherwise have found the action on any other card; it never locks the
  dragged card, and a travel repeat stops after one move. The pin clears only when `TryDispatch` reports
  its card or action gone on the first iteration (a new `targetGone` out-parameter), not on any
  first-iteration failure, so a pin blocked by an unmet requirement survives. The plan's `GroupDef` null
  guard was dropped, because `GroupInventoryAction` is a struct. The completion clip is
  `DefaultCookingComplete`, with `DefaultProgressFull` only as a fallback: a live probe found
  `DefaultProgressFull` and every other `Default*Full` clip null on the EA 0.68b SoundManager, after the
  first harness run of T2.414 logged "Completion sound skipped". `Repeat_Action.csproj` gained a
  `UnityEngine.AudioModule` reference for `AudioClip`, against the plan's "no csproj change".
- **Disposition:** ARCHIVE - moved to
  `Documentation/Design/RepeatAction_Content_Update_2_2_0_As_Built.md` with a new as-built header.
  `Documentation/Plans/RepeatAction/` is empty again. `Documentation/Ideas/RepeatAction/IDEAS.md` marks
  all five rows SHIPPED, and its dated 2026-09-26 reconciliation note keeps its original path with the
  new location added beside it.
- **Pruned:** nothing left as a stub; the whole plan body is in the as-built.
- **Evidence:** `dotnet build -c Release` 0 warnings / 0 errors; version 2.2.0 set by
  `Update-ModVersion.ps1` in `ModInfo.json`, `Plugin.cs` and `README.md`. Deployed through
  `Deploy-Mods.ps1 -RepeatAction` (release zip `Repeat_Action_2-2-0.zip`); the deployed DLL was
  confirmed to carry the clip fix (member name in UTF-8, literal in UTF-16-LE). Harness batch
  `walkthrough-20260928-085203`: T2.410 26/26 (one allowed-failure step, a deliberate 5 s real-time
  wait), T2.411 24/24, T2.412 22/22, T2.413 34/34, T2.414 29/29, T2.415 46/46. The first batch
  (`walkthrough-20260928-043414`) went 2 PASS / 4 FAIL: three of the four were the Sirus23 night-owl
  encounter popup pausing runs at 22:15-22:45 (the harness starts at a random hour), fixed in the
  scenarios by a 14:00 clock pin and a 20:00 night; the fourth was the null clip above, which was a real
  mod defect. Every settings key was checked bound once and consumed.
  `EmbeddedZipCurrency.Tests.ps1` passed 22 / 0 failed with the new pin.
- **Verification debt:** none open. T2.410-T2.415 moved to `confirmedItems` in r43 (2026-09-28). Two
  things were read from code and never driven: Lock's drag-drop exemption (`DispatchCardOnCard`), and
  whether the cooking chime sounds right on a "Stopped" ending, a listening judgement. The MUM suite
  still embeds RepeatAction 2.1.6 until the next `/package-mod-suite`, pinned in
  `EmbeddedZipCurrency.Tests.ps1`. The repack was not run because `Pack-Suite.ps1` and several suite
  mods held other sessions' uncommitted work, which a repack would have shipped. Still open from earlier
  tiers (measured in `items` at recording time): T2.216-T2.220 (the 2.1.1-2.1.5 rows, `pending`, each
  with a scenario), T2.264 and T2.265 (2.1.6, `pending`, no scenario) and T2.164 (`skip`, MANUAL).

### 2026-10-04 - MASTER_PLAN
- **Verdict:** counts: 24 features plus Wave 0 (11 items) checked against the code at `db2c99eb6`. Verified done: 11 features with a PASS or confirmed tracker row (3 to 8, 10 to 12, 14, 15), Wave 0 items W0.1 to W0.8 answered. Built, awaiting a row that only a person or a screenshot read can close: Features 1, 2 and 13. Failed: Feature 9 (T2.708). Open and unbuilt: Features 16 to 24 and W0.9 to W0.11. Pack: 14 open prompts before and after (this pass changed no prompt), 3 awaiting-verification entries.
- **Disposition:** KEEP IN PLACE (open work remains; the pack's first open prompt is Prompt 17, the Feature 9 fix pass, which runs before Prompt 5's refactor of the same loop).
- **Pruned:** from the plan, the Selected-table rows of Features 3, 4, 5, 6, 7, 8, 10, 11, 12, 14 and 15 and the two shipped-in-2.2.0 rows at the top of the Considered table (iteration delay to pinned action; action history log). Nothing from the pack. "## Wave 0 findings" was kept whole because scenarios, `ActionPatch.cs` comments and the Quick Transfer and Utility Belt plans cite its W0.x entries.
- **Evidence:** a grep of `RepeatAction/*.cs` for `ActionDisplay`, `RefuelFromPinned`, `RepeatPresets`, `SavedSequences`, `ReceivingCardCeilings`, `Count Up Key` and `class Messages` returned 0 hits, and the same pattern run with `PlayerIsTyping`, `OngoingEncounter` and `GivenCardTransformed` added returned hits for those three (the control that the probe can hit). `Plugin.cs` still reads `KeyCode.Equals`, `KeypadPlus`, `Minus` and `KeypadMinus` directly (lines 444 to 447, so Feature 21 is unbuilt) and its contract comment still says "planned" (line 123, which Prompt 14 corrects). `ActionPatch.cs` carries `RunRepeat(cap, count, fromPin)`, `TryDispatch`, `CheckStatThresholds`, `CheckQuantityStops` and the `GivenCardTransformed(cap)` block after it (line 962), as Prompt 17 describes. `git log` shows `2e6f99174` as the last commit under `RepeatAction/` source, so nothing changed since the pack refresh. `QuickTransfer/RepeatActionBridge.cs` and `QuickTransfer-RepeatActionYield.Tests.ps1` exist (Prompt 14's precondition); `RepeatAction-MessageTable.Tests.ps1` does not (Feature 16 unbuilt); `.claude/nexus-publish-status.json` still holds RepeatAction 2.1.6. A script read every `T\d+\.\d+` id in the plan and pack (27 distinct) and all 27 exist in the tracker; T2.700 to T2.704 read pass, T2.705 to T2.707, T2.709 to T2.713, T2.715 and T2.716 sit in `confirmedItems`.
- **Verification debt:** T2.708 (fail, Prompt 17), T2.701 (pending, a person row), T2.714 (pending, an ASSIST screenshot read), T2.650 (pending, Quick Transfer's two-mod row, rerun on the current Quick Transfer build); from earlier tiers T2.455, T2.264 and T2.265 (pending, scenarios are pack Prompt 6) and T2.164 (skip).

### 2026-10-07 - MASTER_PLAN (cleanup-plans fleet sweep)
- **Verdict:** counts: every plan claim re-read against the code at `189f8b99e` (last `RepeatAction/` source commit `976e8321c`, 2.2.1) and the harness reports on disk. Verified done and pruned: 1 (Feature 9, section 9: T2.708 confirmed). Obsolete: 0. Awaiting verification: 5 features (2, 13, 16, 19, 21; rows T2.701, T2.714, T2.717, T2.718, T2.720, T2.721). Failed: 0 (Feature 16 left the Failed list: T2.717 passed every step on the harness 2026-10-07, unrecorded). Stale claims corrected: 3 (the 2.2.1 bump is done, not future work; `UtilityBelt/bin/Release/Repeat_Action.dll` is no longer the `2e6f99174` build; the MUM embed is 2.2.0, not 2.1.6). Open: Features 18, 20, 22, 23 and 24, W0.9 to W0.11, and the ship and closeout. Pack: 9 open prompts before and after (Prompts 6, 7, 8, 10, 11, 12, 13, 15, 16), 4 awaiting plus 1 failed before, 5 awaiting after.
- **Disposition:** KEEP IN PLACE - 9 open prompts of work remain (Features 18, 20, 22, 23 and 24 unbuilt, Wave 0 W0.9 to W0.11 unanswered, then the ship and closeout). First dispatchable: Prompt 6 (deps none).
- **Pruned:** plan section 9 (Feature 9, a one-line shipped record); the plan's six dated status paragraphs of 2026-10-04 to 2026-10-06, condensed to one history paragraph (the full text is at `git show 189f8b99e:Documentation/Plans/RepeatAction/MASTER_PLAN.md`); Feature 16's T2.717 fix pass from the Remaining table and from pack Prompt 6; the pack's "Failed - needs a fix pass" section; the pack top note's stale version history. Plan 759 to 745 lines, pack 149 to 146.
- **Evidence:** tracker read by id: T2.708, T2.713, T2.719 and T2.650 in `confirmedItems`, T2.700 pass, T2.701, T2.714, T2.717, T2.718, T2.720, T2.721, T2.264, T2.265 and T2.455 pending, T2.164 skip. Harness reports of 2026-10-07 under `TestHarness/client/reports/`: T2.717 `...-20261007-044938` PASS (steps 20 to 22 pass), T2.714 `...-044740` and T2.718 `...-045220` PASS on their automated steps, T2.701 `...-055309` FAIL on step 15 only (steps 13 and 14, Repeat Action's own, pass), T2.164 `...-030833`, T2.264 `...-032817`, T2.265 `...-033248` and T2.455 `...-035431` FAIL at the same steps as 2026-10-05. Builds: md5 `6581b6d218d2318e3541342ab2484b55` for the deployed `Repeat_Action.dll` (mtime 2026-10-06 12:34), `RepeatAction/bin/Release`, `git show HEAD:RepeatAction/bin/Release/Repeat_Action.dll` and `UtilityBelt/bin/Release/Repeat_Action.dll`; `git show 2e6f99174:...` gives `6ffbdd71e8e028c37dd8bde2218643c0`; the DLL inside `Mod_Update_Manager/Resources/EmbeddedMods/Repeat_Action.zip` gives `7c955099628efccd182bf7277237782c`, equal to `git show a2509e73f:RepeatAction/bin/Release/Repeat_Action.dll`, with ModInfo 2.2.0. Version triple 2.2.1 in `ModInfo.json`, `Plugin.cs` and `README.md`. `git log 976e8321c..HEAD -- RepeatAction/ TestHarness/client/scenarios/RA/` prints nothing. Code: a grep of `RepeatAction/*.cs` and `Patcher/` for `RecordSequence`, `SavedSequences`, `ReceivingCardCeilings`, `RunSequence` and `Record Sequence` returned 0 hits, while the same grep run for `Count Up Key`, `Next Preset Key`, `Repeat Presets`, `ReadPlayerIsTyping`, `DispatchAndWait` and `IndicatorText` returned hits (the control that the probe can hit). No T2.720 or T2.721 scenario exists in `TestHarness/client/scenarios/RA/`, and `T2.650_qt_ra_shift_chord.json` has no `Count Up Key` or `PageUp` (0 hits).
- **Verification debt:** T2.701, T2.714, T2.717, T2.718, T2.720, T2.721 (built features); T2.164, T2.264, T2.265, T2.455 (Feature 18, pack Prompt 6).

### 2026-10-09 - MASTER_PLAN
- **Verdict:** counts: every plan claim re-read against the code at `6f4058efb` (no `RepeatAction/` source commit after `976e8321c`, 2.2.1), the tracker and the 2026-10-09 harness reports. Verified done: 2 (Features 19 and 21: T2.720 and T2.721 confirmed in `51641ef64`). Obsolete: 0. Awaiting verification: 3 features, each on a person-lane row only (Feature 2 on T2.701, real input; Feature 13 on T2.1381, art; Feature 16 on T2.718, screenshots of the Chinese toasts); T2.714 and T2.717 were confirmed. Failed: 0 at plan level (Feature 18's four scenarios still fail or were rejected, and are open work). Stale claims corrected: 0 beyond the status pointers. Open: Features 18, 20, 22, 23 and 24, W0.9 to W0.11, then the ship and closeout. Pack: 9 open prompts before and after (6, 7, 8, 10, 11, 12, 13, 15, 16); awaiting entries 5 before, 3 after.
- **Disposition:** KEEP IN PLACE - 9 open prompts remain. First dispatchable: Prompt 6 (deps none, game lane); the next wave runs 6, 7, 8, 10, 11, 12 and 13 one at a time on the game, then Prompt 15 at the owner's ask and Prompt 16 to close out.
- **Pruned:** from the pack, the Awaiting verification entries for Features 19 and 21 (T2.720 and T2.721 confirmed in `51641ef64`) and the step in Prompt 6 that reran T2.701 and T2.717; the Feature 13 and Feature 16 entries were rewritten to their person-lane rows. From the plan, the "awaiting verification as T2.720 and T2.721" sentence in the Phase E intro and the T2.714, T2.717, T2.720 and T2.721 entries of the Remaining-table awaiting line; no plan section was deleted. Pack Prompt 6 was refreshed with the 2026-10-09 readings (T2.164's capture drop that does nothing and its exact ==80 float compare; T2.264's rejected PASS; the T2.265 and T2.455 walk stopping "requirements no longer met" at Shady Bank). No commit made by this pass; the working-tree edits await the sweep's commit.
- **Evidence:** tracker read by id with python: T2.700, T2.702 to T2.704 pass; T2.713, T2.714, T2.717, T2.719, T2.720, T2.721 and T2.708 carry `confirmedOn` (T2.714, T2.717, T2.720, T2.721 on 2026-10-09); T2.701, T2.718, T2.1379, T2.1380, T2.1381, T2.218, T2.219, T2.264, T2.265 and T2.455 pending; T2.164 skip. Reports under `TestHarness/client/reports/` of 2026-10-09: T2.164 `...-062507` FAIL (steps 32 and 38 read 80.00001 against ==80; step 49 "Dropping 'Birch Wine' onto 'Clay Bowl' does nothing") and `...-003056` FAIL (step 45, the same drop); T2.265 `...-055154` and `...-003656` FAIL at steps 14, 16 and 35; T2.455 `...-054612` and `...-004952` FAIL at steps 30, 31 ("Stopped after 2/3: requirements no longer met") and 33. Builds: md5 `6581b6d218d2318e3541342ab2484b55` for the deployed `Repeat_Action.dll` (mtime 2026-10-06 12:34), `RepeatAction/bin/Release` and `UtilityBelt/bin/Release`; the MUM embed's DLL `7c955099628efccd182bf7277237782c` with ModInfo 2.2.0. Version 2.2.1 in `ModInfo.json` and `Plugin.cs`; CHANGELOG heads `[2.2.1]` then `[2.2.0]`. `git log 189f8b99e..HEAD -- RepeatAction/` prints only `56ef680ed` (the last sweep's ROADMAP entry). Code: a grep of `RepeatAction/*.cs` and `Patcher/*.cs` for `SavedSequences`, `RecordSequence`, `ReceivingCardCeilings` and `RunSequence` returned 0 hits, while `DispatchAndWait` and `ExtraStatThresholds` returned 11 hits across the same two files (the control that the probe can hit).
- **Verification debt:** T2.701, T2.718 and T2.1381 (built features, person lane); T2.164, T2.264, T2.265 and T2.455 (Feature 18, pack Prompt 6); T2.218, T2.219, T2.1379 and T2.1380 (2.1.3 and 2.1.4 regression rows the ship step reruns).
