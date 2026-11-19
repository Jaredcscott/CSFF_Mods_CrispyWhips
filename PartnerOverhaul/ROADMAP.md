# Roadmap: Partner Overhaul
Version at time of writing: 1.1.7
Date: 2026-11-15
Audit score: 10/10 - PASS (0 open findings at any severity)

## Current State

**Theme**: A Harmony-fix mod for the base game's Partner companions (eating, cleaning, watering, fire-keeping, duty marks, display) that also gives the Partner a voice: an opt-in spoken "Talk with" conversation system (openers by mood/personality, world topics, memories, player-state remarks, bond milestones, first-visit remarks). For players who keep a Partner and want it to behave sensibly and feel alive.

**Content**: 2 items / 1 blueprint / 0 structures / 0 perks / 2 custom images (the opt-in Ember Pot and its lit form). Everything else is behaviour patches plus ~34 NPCStat marker files and ~60 DialogLine ScriptableObjects for the conversation system, and 7 `GameSourceModify` files (Agent_Partner registration + 6 per-NPC voice-stat files).

**Stability**: 10/10 - PASS. 0 open CRITICAL, 0 design gaps, 0 open warnings, 0 open minor items. The three findings the prior roadmap tracked are all CLOSED and committed 2026-11-14, and confirmed by the 2026-11-15 module re-runs: W3 (ModInfo Description now names the Expedition Stories opt-in, `372b51462`), V4 (the talk and topics patches walk DialogLine once per run boot, `43a5a71cd`), M2 (Modules.json dropped five phantom modwide globs, `351fdb8de`). All 12 modules read CURRENT; every real module is SOLID.

**Open work**: No open retrospective names PartnerOverhaul, "PO" or `crispywhips.partner_overhaul`. Both feature plans are built and archived as `Documentation/Design/PartnerOverhaul_As_Built.md` and `Documentation/Design/PartnerOverhaul_Balance_And_Conversations_As_Built.md`; there is no `Documentation/Plans/PartnerOverhaul/` backlog. What remains is one release action (a Nexus re-upload carrying both the ASSET_CREDITS fix and the new `SimpCn.csv`), two person-gated runtime rows (T2.507, T2.1378), and optional polish.

**Framework compliance**: Soft-dependency on CSFFModFramework (present, correct `[BepInDependency(... SoftDependency)]`). This is a behaviour-patch mod, so the Tier 2 content-injection services (ActionRouter / SpawnService / TickEvents / ContentModPlugin) are largely N/A - a grep of `Patcher/*.cs` finds zero hits, and the mod patches vanilla duty/dialog/stat paths directly via Harmony and reflection, all guarded (warn-once breadcrumbs, per-run latches, in-place transpiler mutation). No deprecated patterns (no `DropCollectionGuardPatch`, no `ModLoaderVerison`/`ModEditorVersion`, no unfiltered hot-path prefix). The only framework-backed content, the Ember Pot, loads declaratively; its C# only strips warmth / hides the blueprint when the option is off.

---

## Phase 0: Stabilize  *(audit score is 10/10, no open findings and no open retrospectives - clear)*

> No code, data or docs defect is open. The three prior-round items (W3 Description, V4 DialogLine scan, M2 Modules.json globs) all closed and committed 2026-11-14. The one remaining item is a release action, not a fix.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Re-upload to Nexus so the live v1.1.7 package carries the corrected `ASSET_CREDITS.md` (fixed in repo at `2e49f980a`; the live package, uploaded 2026-10-30, still has the old "ships no card art" line) **and** the new `SimpCn.csv` (shipped `609a48e32`, not in the live package). | Release action | P0 | Quick |

---

## Phase 1: Foundation

> Table-stakes housekeeping. All satisfied as of this writing.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Version sync across ModInfo.json / Plugin.cs / README.md | Version hygiene | P1 | DONE (all read 1.1.7) |
| `Localization/SimpCn.csv` first Chinese pass (218 keys; W1) | Localization | P1 | DONE 2026-11-13 (`609a48e32`, owner decision 2026-11-12; parity CLEAN 218/218; not yet read in a running game or by a Chinese speaker) |

The first `SimpCn.csv` has landed, so the parity gate now binds this mod: every later EN row must ship its CN row in the same commit. Gate: `Development_Tools/Tests/PartnerOverhaul-ChineseLocalization.Tests.ps1` (parity plus placeholders, which the parity script does not read). One remaining follow-up: add PartnerOverhaul to `$script:LocParityFleetMods` in `Check-LocalizationParity.ps1` so a no-arg fleet parity run covers it too (today only the PO-specific gate does). The `Config.Bind` setting names/descriptions (14) and the no-framework "Looks at ease." Comfort label remain English by design - the framework loads the CSV and those strings are not CSV-backed.

---

## Phase 2: Core Expansion

> The mod's two feature plans are built and archived; new content is not currently justified beyond small, grounded extensions. The conversation system is the mod's growth surface.

### More soft "Talk about..." topic hooks for other installed mods' landmarks
**What**: extend the existing optional-topic pattern (CMC Village/Inn, WDI mills - both confirmed in-game, T2.513/T2.523) with topics for other mods' signature landmarks, each gated on a `RequiredCardsOnBoard`/`RequiredEnvironment` check so it is silently absent without that mod.
**Why**: the Talk-with system is where the mod's recent value is; each hook is cheap and adds flavour only when the paired mod is present, with no hard dependency.
**Requires**: none (but see the shared DialogLine index in Phase 4 - it would keep the boot-scan count flat as topics are added).
**Complexity**: Quick per topic (one DialogLine + one gated route + one EN/CN localization row pair).

### Close the last two runtime-verification rows
**What**: close the 2 remaining pending tracker rows - T2.507 (the "Talk with" answer appears in the Partner's Talk menu, the without-framework leg) and T2.1378 (the sound/dialog person-scope half split from T2.104) - via the playthrough tracker. 43 of the mod's 45 rows are already in `confirmedItems`; these two are the person/precondition-gated remainder.
**Why**: T2.507 is the entry point to the mod's headline conversation feature; closing it confirms the system is reachable in a running game, not just in data.
**Requires**: `/playthrough-test-plan record` on a human-run build.
**Complexity**: Medium (human verification, not code).

---

## Phase 3: Integration & Depth

> Cross-mod hooks; all current ones are soft and optional.

### Deeper soft integrations with CMC / WDI
**What**: the Talk-with system already references CMC's Village/Inn and WDI's mills. Natural next steps are memory/first-visit hooks for other mods' distinctive locations, following the same absent-without-the-mod pattern.
**Why**: emergent flavour that rewards running the mods together, at no cost to players who do not.
**Requires**: coordination only where a landmark's UID is referenced; no version lockstep (soft gates).
**Complexity**: Medium.

---

## Phase 4: Polish

> Code and text refinements; the mod's art is already complete.

| Item | What | Complexity |
|------|------|------------|
| Shared single-pass DialogLine index (beyond V4) | V4 (closed 2026-11-14) reduced the talk and topics patches to one `DialogLine` walk per boot each, but `PartnerExpeditionStoryPatch` and `PartnerExpressVoicePatch` still walk once each, outside V4's scope. Replace every walk with one boot-populated `Dictionary<string,DialogLine>` cleared at run boot (or resolve lines via the framework's cached `AllScriptableObjectDict` by type). Bounded one-time cost today, no gameplay effect; worth doing if more dialog subsystems land. | Medium |
| Docs-honesty propagation check | A small check that lists README "not yet confirmed"/"has not yet been seen" caveats whose named `po_` tracker row is now in `confirmedItems`, and symmetrically flags a critical-analysis asserting "none confirmed" against a per-row-id read. Makes a W2-class under-claim catchable instead of needing a manual both-directions re-read each consolidation. | Medium |
| Description-vs-wired-opt-in check (W3 direction) | A check that every `ConfigEntry` in an opt-in config section with a wired `ApplyPatch` has a matching clause in the `ModInfo.json` Description and README opt-in list. Would have caught the Expedition Stories omission (W3, fixed 2026-11-14) and guards future opt-ins. | Medium |

---

## Long-term Vision

> Where this mod should be at v1.2.

Partner Overhaul's natural endpoint is a fully human-verified bug-fix layer plus a mature, localized conversation companion. With the first Chinese localization pass now shipped (`609a48e32`), the 218-key conversation system - the bulk of the mod's value - reaches Chinese players; the remaining open step toward that endpoint is closing the last two runtime-verification rows so the headline conversation entry point (T2.507) is confirmed in a running game, not just in data. Beyond that, the conversation surface grows topic-by-topic as other mods add landmarks, with no change to the fix layer.

**Potential major additions** (not yet justified - revisit after Phase 2):
- A broader topic framework that lets other mods register their own "Talk about..." topics declaratively - fits the mod's theme if several mods want to hook the conversation system, but over-engineered for the current two integrations.
- A second warmth tier above the Ember Pot - explicitly declined by the owner (2026-10-20, T2.526): the pot is meant to slow cooling, not replace a fire, so a stronger variant is out of scope for now.

Any of these worth building belongs in a plan under `Documentation/Plans/PartnerOverhaul/` (or its Considered table, with the reason, if not); never a stub under `Documentation/Ideas/`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod PartnerOverhaul` and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes, re-run `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis PartnerOverhaul` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo PartnerOverhaul` and bump minor version |
| Before the next Nexus upload | Confirm `ASSET_CREDITS.md` ships corrected (fixed `2e49f980a`) and `SimpCn.csv` is in the package |
| Any later `SimpEn.csv` edit | Ship the matching `SimpCn.csv` row in the same commit (parity now binds the mod) |

---

## Skill Cheatsheet for This Mod

```
/audit-mod PartnerOverhaul         - full health check, updates .audit/
/critical-analysis PartnerOverhaul - adversarial review
/repair-items PartnerOverhaul      - auto-fix item JSON issues
/repair-blueprints PartnerOverhaul - auto-fix blueprint JSON issues
/build-mod PartnerOverhaul         - build Release DLL
/deploy-mods PartnerOverhaul       - build + deploy to game
/update-mod-version PartnerOverhaul <ver> - bump version in all 3 files
/export-to-repo PartnerOverhaul    - push to public repo
/nexus-publish upload PartnerOverhaul - upload release + changelog to Nexus
```

---

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
