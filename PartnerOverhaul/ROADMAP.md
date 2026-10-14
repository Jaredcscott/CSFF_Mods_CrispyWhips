# Partner Overhaul Roadmap

**Current version:** 1.1.2 (2026-10-13)

## Next

- No plan is open (2026-10-10). The round-2 plan is built and archived as
  `Documentation/Design/PartnerOverhaul_As_Built.md`. The mod stays at the repo root: since 2026-10-12 a mod
  moves into `Stable/` only after the owner says it has been fully human tested (root CLAUDE.md, "Where a
  mod lives").
- Owed, none of it code: the skeptic review of T2.502, T2.503 and T3.39; the person checks T3.81 (lake
  drinking) and T3.82 (the cold warning's use and its Chinese config text); a real-install redeploy for
  the 2026-10-10 README/ModInfo corrections; the owner's push of the public export and a first Nexus
  upload once the page exists.

## Plan Reconciliation Log

> Written by `/cleanup-plans` Step 4b. Append-only: never reword, reorder or delete an entry.
> `/roadmap` Step 7 preserves this section verbatim when it overwrites the rest of this file.

### 2026-09-28 - Content_Update_Plan (PartnerOverhaul 1.1.0)
- **Verdict:** 8 of 8 items have an outcome. Built: 2 (item 2 pour refusal, item 7 widened duty
  trace), plus an unplanned fix to the ownership diagnostic found under item 1. Closed by research,
  no code: 3 (item 1 ownership, item 4 brain-tan, item 5 clothes). Measured but unresolved, moved
  on: 2 (item 3 fishing report loop, item 6 Seek Drink stall). Release tail (item 8) done.
- **Disposition:** ARCHIVE. Moved to `Documentation/Design/PartnerOverhaul_Content_Update_1_1_0_As_Built.md`
  with an outcome table at its head; the two open stalls went to
  `Documentation/Plans/PartnerOverhaul/Partner_Duty_Stalls_Plan.md`; the three closures went to
  `Documentation/Ideas/PartnerOverhaul/PARTNER_BUGS_PENDING_EVIDENCE.md` section 4 (since 2026-10-01
  `Documentation/Research/PartnerOverhaul/PARTNER_BUGS_PENDING_EVIDENCE.md`).
- **Pruned:** item 1's planned legacy-save warning line, never built: its premise was refuted by
  `.decomp/` (`GameLoad.SaveGameDataToFile` always writes the v3 format that carries ownership).
- **Evidence:** TestHarness runs on 2026-09-28, EA 0.68b, fixture slot 7 (FX-partner) or a fresh
  game. On 1.0.5: `RESEARCH_pouch_acorn_flour.json` red (one flour pours 112.5, Pouch holds 150,
  second card destroyed with 37.5 added, 75 lost, single and stack drag); T2.156 red only at the
  ownership step; `RESEARCH_ownership_roundtrip.json` green (owner survives reopen and save/load).
  On 1.1.0: T2.436 AUTO scenario PASS including its option-off control, after a first run that went
  red on the stack leg and was fixed; T2.156 PASS; `RESEARCH_braintan_stuck.json` green (hide 1 at
  Treatment 8 by hour 2, hide 2 by hour 4, both Brain Mush used). `RESEARCH_clothes_weight_temp.json`:
  coat worn at perceived -1.95 (weight 200, perceived to 1.8), off at 35 and still off at 28.
  `RESEARCH_partner_water_fetch.json` leg C: Seek Drink toward Env_River_ClearingOak_RiverClearing,
  stalled at cmcEnvPineTrail for 3 hours, Hydration 77.5 to 71.5.
  `RESEARCH_fishing_line_freeze.json`: fishing for hours with line and bait; Done plus a reopening
  report dialog with a rod or no bait.
- **Tracker:** T2.436 (pending, the pour refusal); T2.156 and T2.157 carry dated notes with these
  results.

### 2026-09-29 - Partner_Duty_Stalls_Plan (follow-up to PartnerOverhaul 1.1.0)
- **Verdict:** 2 of 2 items have an outcome, both closed by research with no code change. Item 1
  (a thirsty Partner stalls one room out at Pine Trail) is vanilla design plus a sampling artifact.
  Item 2 (the fishing report dialog keeps reopening) is a harness artifact.
- **Disposition:** ARCHIVE. Moved to `Documentation/Design/PartnerOverhaul_Duty_Stalls_As_Built.md`
  with an outcome table at its head; the two closures went to
  `Documentation/Ideas/PartnerOverhaul/PARTNER_BUGS_PENDING_EVIDENCE.md` section 4, and the one
  design question that remains (should a following Partner fetch water) to
  `Documentation/Ideas/PartnerOverhaul/IDEAS.md` (since 2026-10-01 the research file is in
  `Documentation/Research/PartnerOverhaul/`, and the design question is feature 9 of
  `Documentation/Plans/PartnerOverhaul/MASTER_PLAN.md`).
- **Pruned:** item 1's Deadfall-gate hypothesis, refuted by `.decomp/` (the gate toggles only the
  map-display flags, and `MoveNPC` checks no gate); item 2's "fix the flag the Done step fails to
  clear", refuted by the real dialog path, where both flags clear and time passes.
- **Evidence:** TestHarness runs on 2026-09-29, EA 0.68b, fixture slot 7 (FX-partner).
  `RESEARCH_partner_water_fetch.json` leg D, one tick at a time: the Partner is only ever in the
  Village or Pine Trail, `PartnerDuty_SeekDrink` current at Pine Trail with weight 118 to 163
  beside `PartnerDuty_Follow` at 1000. T2.437 PASS: following, the Partner never reaches River
  Clearing and Hydration falls; after its own 'Stop Following' answer it reaches River Clearing
  and drinks. T2.438 PASS: real Go Fishing sets both expedition flags, a line with no bait or
  Minnow reaches the Done duty on the first tick, the report appears once, its Continue clears both
  flags and eight more points pass; the same scenario answering the report with the runner's
  `continue` verb fails the report-closed, time and Done-duty checks.
- **Tracker:** T2.437 and T2.438 (pending, AUTO scenarios PASS); T2.156 and T2.157 keep their
  dated notes, now with the fishing question marked closed.

### 2026-10-03 - MASTER_PLAN (PartnerOverhaul round 2), `/audit-plan` pass
- **Verdict:** plan still ACTIVE (not drained). Of the 11 features plus Wave 0: 1 built and awaiting
  verification (feature 6, Comfort on the card, found already in 1.1.0 source with docs and the extended
  T2.124 scenario), 10 OPEN (features 1 to 5 and 7 to 11 have no code; W0.1 to W0.7 have no recorded
  DECISION). Eleven Wave 0 research scenarios are authored (`50d3fdb9`) and unrun; W0.5 has none.
- **Disposition:** KEEP. Nothing deleted from the plan except feature 6's body (collapsed to a pointer) and
  its decided Open question 4; the pack retired Prompt 4 and gained an Awaiting verification entry.
- **Evidence:** `PartnerOverhaul/Patcher/GameLoadPatch.cs` at HEAD `d7dc567f5` (`FixPartnerStatVisibility`,
  `HiddenPartnerStats`, one `FirstOrDefault` per Fireplace action in `FixFireplaceFuelGap`), `PathSpeedPatch`
  and `WoodReserveListPatch` without NPC or duty parameters, README/CHANGELOG/ModInfo Comfort lines, the
  EA 0.68b export for the cards named in features 1 to 3, 7 and 11 (UniqueIDs and unwired actions match; the
  pack wrongly gave River's UID as FrozenRiver's, corrected).
- **Tracker:** T2.124 (`skip`) covers Feature 6 and needs a note update after its run; T2.103, T3.39,
  T3.38 unchanged. No new rows were filed by this pass.
- **Docs drift noted, not fixed here:** README "Still under investigation" is false (both items closed
  2026-09-29).

### 2026-10-04 - MASTER_PLAN
- **Verdict:** counts: 11 features plus Wave 0 (7 items) and the pack's 10 open prompts (6 to 11, 13 to 16) checked. Verified done: Wave 0 W0.1 to W0.4, W0.6, W0.7 (recorded DECISIONs, kept as load-bearing for open prompts) and feature 11 (closed by W0.7, no code). Built, awaiting verification: 2 (feature 1 Charcoal feeds, `8a1269652`; feature 6 Comfort on the card). Open: 8 features (2, 3, 4, 5, 7, 8, 9, 10), W0.5, and the release tail.
- **Disposition:** KEEP IN PLACE (open work remains; the pack's first open prompt is Prompt 9, feature 4).
- **Pruned:** plan section 11 (the closed feature 11 stub; its Considered row and W0.7 findings stay); two citations to it repointed (plan W0.7 DECISION line, pack header). No pack prompt removed: every remaining prompt is open.
- **Evidence:** Grep of `PartnerOverhaul/**/*.cs`: `AppendCardDutyIfMissing`, `HearthUid`, `FixOneCardWiringSlips`, `AppliesTo`, `_OnNPC`, `ExpressNeedWarmth`, `Drink From Lakes` each 0 files (control: `AppendDutyRefToAllNamed`, `AppendActionTagIfMissing` and `FixPartnerStatVisibility` hit `GameLoadPatch.cs`; `_FromNPC` hits only `PartnerDiagnosticsPatch.cs`). `PathSpeedPatch.MaybeSkipCosts` takes only `ref IEnumerator __result` and `WoodReserveListPatch.FilterReservedItems` only `ref List<InGameCardBase> _Items`. `FixFireplaceFuelGap` wires `Feed Charcoal` with `PartnerAction3`. `Development_Tools/Partner-DutyWiring-Audit.py` absent. `git log` since `8a1269652`: the PartnerOverhaul commits touch only `bin/Release` files. Every `T<n>.<n>` id in the plan (15) and pack (9) resolves in `.claude/playthrough-test-status.json` (script, one `"id"` match each). T2.500 and T2.501 read `pending`, T2.124 `skip`.
- **Verification debt:** T2.500 (AUTO, never run), T2.501 (MANUAL), T2.124 (Comfort legs unrun; row note still Energy-only), T3.39 pending.

### 2026-10-07 - MASTER_PLAN (cleanup-plans fleet sweep)
- **Verdict:** 10 features plus Wave 0 and the release tail checked against `PartnerOverhaul/` at `56ef680ed`. Verified done: 0 newly (Wave 0 W0.1 to W0.4, W0.6, W0.7 and feature 11 stay recorded as before). Obsolete: 0. Built, awaiting verification: 7 (features 1, 2, 3, 5, 6, 7, 8). Failed: 1 (feature 4, T3.39, re-opened as pack Prompt 17). Stale claims: 0. Open: W0.5 (Prompt 6), feature 9 (Prompt 14), feature 10 (Prompt 15), the measurement of features 2, 3, 7, 8 (Prompt 18), the feature 4 fix (Prompt 17) and the release tail (Prompt 16).
- **Disposition:** KEEP IN PLACE - 6 open rows of work remain (pack Prompts 17, 6, 18, 14, 15 and 16; Prompt 17 is first and has no unmet dependency).
- **Pruned:** plan sections 1, 2, 3, 5, 6, 7 and 8 collapsed to one-line awaiting-verification pointers (section 1's "ref plus tag is never sufficient" lesson folded into the Risk register's first row); the five dated status paragraphs and the three Execution Status bullets condensed into one history paragraph and one bullet (the full text resolves at `56ef680ed`). Pack: the four Awaiting verification bullets compacted to one line each naming their rows; no prompt removed (none is done); Prompt 18 gained the note that the six lake Drinks equalling River's first is unconfirmed. Plan 581 to 561 lines.
- **Evidence:** `git log f6f544a4d..HEAD -- PartnerOverhaul Documentation/Plans/PartnerOverhaul TestHarness/client/scenarios/PO` = 3 commits, all docs or tracker (`28fddc7fb`, `b82d4dc28`, `72273e90f`); no source commit. Grep of `PartnerOverhaul/Patcher/*.cs`: `FixHearthDuties`, `FixOneCardWiringSlips`, `FixSeasonalTwinMarks`, `WireLakeAndStreamDrinking` (called only when `PartnersDrinkFromLakes` is on), `AppliesTo` in both `WoodReserveListPatch` and `PathSpeedPatch`, `WidenTagFuelDecisionToCharcoal` each hit; `ExpressNeedWarmth` / its UID 0 hits and `Development_Tools/Partner-DutyWiring-Audit.py` absent (control: the same grep hits `FixPartnerStatVisibility`, so it can find a method). Deployed `Partner_Overhaul/PartnerOverhaul.dll` md5 equals `bin/Release` (built 2026-10-06 12:18). Tracker: T2.500, T2.501, T2.502, T2.503, T2.504, T3.39, T3.80, T3.81 `pending`, T2.124 and T2.103 `skip`, T3.38 confirmed. Harness reports on disk dated 2026-10-07: T2.500 PASS 07:10, T2.501 scenario PASS 07:11, T2.124 PASS 07:17, T2.103 PASS 02:54; none recorded. No Prompt 14 research report and no T3.39 rerun on disk.
- **Verification debt:** T2.500 and T2.124 (harness PASS to record; T2.124's note still Energy-only), T3.80 (PASS to record; pre-change red never run), T2.501 (human), T2.502, T2.503, T2.504, T3.81 (AUTO, scenarios unwritten, pack Prompt 18), T3.39 (FAILED, Prompt 17).

### 2026-10-09 - MASTER_PLAN
- **Verdict:** 10 features plus Wave 0 and the release tail checked against `PartnerOverhaul/` at `cc21d71ee`. Verified done: 1 newly (feature 6, Comfort on the card, T2.124 confirmed in `51641ef64`). Obsolete: 0. Built, awaiting verification: 2 (features 1 and 5). Failed: 5 (features 2, 3, 7 and 8 red on HEAD on 2026-10-09; feature 4 red a third time). Stale claims: 2, corrected in the pack (Prompt 18 still asked for scenarios that `2652a05af` had written, and Prompts 14, 16 and 18 asked for person rows the r46 lane split `9191af9f9` makes harness clauses). Open: W0.5 (Prompt 6), feature 9 (Prompt 14), feature 10 (Prompt 15), the release tail (Prompt 16), plus the two fix passes (Prompts 17 and 18).
- **Disposition:** KEEP IN PLACE - 6 open prompts remain; Prompt 17 is first and has no unmet dependency (next wave: 17, then 18, 14, 6, 15, 16, one at a time on the live-game lane except 15).
- **Pruned:** plan section 6 deleted and its rows in the Selected and Remaining implementation tables removed (feature 6 verified by T2.124, `51641ef64`); the Release tail's "run T2.124's Comfort legs and update that row's note" clause dropped. Pack: the feature 6 Awaiting verification bullet removed; the features 2, 3, 7, 8 bullet moved to a new "Failed - needs a fix pass" list with feature 4; Prompt 18 rewritten from "write the scenarios" to "diagnose and fix the four reds"; the person-row asks in Prompts 14 and 16 narrowed to the lane split. Plan sections 2, 3, 7 and 8 re-marked FAILED. No source commit in this pass.
- **Evidence:** `git log 56ef680ed..HEAD -- PartnerOverhaul TestHarness/client/scenarios/PO Documentation/Plans/PartnerOverhaul` = 6 commits, none touching `PartnerOverhaul/Patcher` or `Plugin.cs` (`2652a05af`, `a47b5b8e3`, `48b0a0c1c`, `dcb8d6cc7` scenarios; `e57d44cfc` `bin/Release/ModInfo.json`; `850b3dc9d` plan docs); last source commit `f6f544a4d`. Grep of `PartnerOverhaul/Patcher/*.cs`: `FixHearthDuties`, `FixOneCardWiringSlips`, `FixSeasonalTwinMarks`, `WireLakeAndStreamDrinking` and both `AppliesTo` hit; `Development_Tools/Partner-DutyWiring-Audit.py` and `Development_Tools/Tests/PartnerDutyWiring.Tests.ps1` absent (0; control: the same `ls` lists `RESEARCH_po_f9_express_need_warmth.json`, so the probe reads the tree). md5 of `PartnerOverhaul.dll` in rigs 2 to 7 equals `bin/Release` (`d45fc61e`). 2026-10-09 reports (batch `walkthrough-20261009-041821`): T2.502 FAIL 4 steps, T2.503 FAIL 2, T2.504 FAIL 1 (an earlier 02:24 run PASS on the pre-strengthened scenario), T3.81 FAIL 12, T3.39 FAIL 2; T2.124, T2.500, T2.501, T3.80 PASS. Audit `autoplay-audit-20261009-083746.json` classes T2.500, T2.501 and T3.80 PARTIAL, unreviewed. Tracker: T2.124 in confirmedItems; every other PartnerOverhaul row this plan cites `pending` (T2.103 `skip`). No report on disk for `RESEARCH_po_f9_express_need_warmth.json`.
- **Verification debt:** T2.500, T2.501 (harness PASS; FIX-SCENARIO at the 2026-10-09 second review, reviewers split; T3.80 recorded CONFIRMED the same day), T2.502, T2.503, T2.504, T3.81 (FAILED, Prompt 18), T3.39 (FAILED, Prompt 17).

### 2026-10-10 - MASTER_PLAN (PartnerOverhaul round 2), release tail and closeout
- **Verdict:** 10 features plus Wave 0 and the release tail checked against `PartnerOverhaul/` at `73208a50b`. Verified done: features 1, 5, 6, 8 and 10 (T2.500, T2.501, T3.80, T2.124 and T2.504 in confirmedItems; `PartnerDutyWiring.Tests.ps1` 9/9), Wave 0 W0.1 to W0.7 answered (W0.5 `57f117e07`), feature 11 closed by W0.7. Built, awaiting verification: 4 (features 2, 3, 4 with green scenarios awaiting a skeptic review; features 7 and 9 owing a person). Obsolete: 0. Open code work: 0 (the release tail landed as `fed63101e` docs and tracker, `73208a50b` audit).
- **Disposition:** ARCHIVE - the plan moved to `Documentation/Design/PartnerOverhaul_As_Built.md` with its Considered, not selected table and every rejection reason intact, plus an as-built outcome table; the pack `MASTER_PLAN_Implementation_Prompts.md` was deleted (last text at `73208a50b`); the mod moved to `Stable/PartnerOverhaul/`.
- **Pruned:** the whole pack (Prompt 16 done; Prompt 19 done in `7319fa1ea`; the Shared paragraphs only served those two). Nothing removed from the plan text itself; every citation of `Documentation/Plans/PartnerOverhaul/MASTER_PLAN.md` was repointed to the as-built path.
- **Evidence:** `git log --oneline -- PartnerOverhaul Documentation/Plans/PartnerOverhaul TestHarness/client/scenarios/PO Development_Tools/Partner-DutyWiring-Audit.py`: last source commit `ab09a9899`; the plan's prompts map to commits `c26c1f267`, `616524ab7`, `1e4e9df12` (Wave 0), `b817a9477` (1), `d11ae4952` and `e0b040b63` (4), `7da4ed809` (5), `f6f544a4d` and `cacc60696` (2, 3, 7, 8), `ab09a9899` (9), `069ecdcf5` (10), `57f117e07` (W0.5), `7319fa1ea` (scenario fixes). Dev lane rig 10 on 2026-10-10, deployed `PartnerOverhaul.dll` byte-identical to `bin/Release` (cmp): T2.502, T2.503, T3.39, T3.81, T3.82 PASS (`TestHarness/client/reports/walkthrough-20261010-125525/summary.md`) and T2.500, T2.501, T2.504, T3.80 PASS (`walkthrough-20261010-133600/summary.md`). `/audit-mod` 0 CRITICAL, 9/10. Gates: Mod-Regression 80/80, Csproj-ContentIncludes 11/11, Version-Synchronization 24/24, PartnerDutyWiring 9/9, Deploy-Mods 38 passed 2 skipped.
- **Verification debt:** T2.502, T2.503, T3.39 (`pending`, skeptic review of the `7319fa1ea` fixes), T3.81 and T3.82 (`pending`, person-only), T2.436 and T2.437 (`pending`, from 1.1.0), T2.103 (`skip`, boot wiring only: stew/broth eating, finished-home cleaning and garden overwatering have no behaviour leg), T2.104 (`skip`) with its person half T2.1378 (`pending`).

### 2026-10-10 - Stable move attempt (addendum to the entry above)
- **Verdict:** the closeout above holds (plan archived, pack removed), but the mod did NOT move: its "the mod moved to `Stable/PartnerOverhaul/`" Disposition clause is not yet true.
- **Disposition:** ARCHIVE stands; the Stable move is pending.
- **Evidence:** `Move-ModFolder.ps1 -ModFolder PartnerOverhaul -WhatIf` after `b188f883b`: 0 open plans, 0 uncommitted paths, 0 paths past 259 characters, nothing to repoint. The real run failed twice: `git mv PartnerOverhaul Stable/PartnerOverhaul failed, and nothing else was changed: fatal: renaming 'PartnerOverhaul' failed: Permission denied`. No process command line or loaded module named the folder, so the holder is an open directory handle (an editor, a build node or a peer session's shell); `Stable/PartnerOverhaul` was not created and the tree was left unchanged.
