# Roadmap: Skill Speed Boost
Version at time of writing: 1.11.0 (built, unpublished; Nexus carries 1.10.6)
Date: 2026-10-11
Audit score: 10/10 - PASS (0 CRITICAL, 0 DESIGN GAP, 0 open MINOR, 1 WARNING = the E16 false positive)

## Current State

**Theme**: A pure-C# utility mod giving players fine-grained control over skill progression in CSFF -
per-skill XP multipliers (0-10x), staleness decay (global or per-skill), and a large stack of optional
situational bonuses (morning/night windows, area familiarity with floor and fade, synergy combos,
level scaling, first-use, well-rested, In-the-Flow, low-condition penalty, season/environment/tool/
action/perk bonuses, day streak, rested-XP bank, milestone burst, XP overflow, on-screen readout,
partner-skill follow, per-save progression) with one-key difficulty presets over the whole thing.

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom images / 0 localization rows.
Pure-C# / Harmony. Player-facing text is entirely BepInEx `Config.Bind` descriptions, which is why the
preflight "missing SimpEn.csv" (E16) is a standing, documented false positive - do not create a dummy
CSV to silence it.

**Stability**: 10/10 - PASS. critical-analysis (2026-10-11, v1.11.0) verdict SOLID: 0 critical /
0 mechanical / 0 design / 0 broken promises. code-quality (2026-10-11) 10/10, 0 crit / 0 warn across
26 files. Every one of the 27 README / 30 FEATURES.md features is wired at the single `ChangeStatValue`
composition point or a load/run-start postfix; all 20 bonus flags appear in BOTH the composition body
and the early-out guard (`SkillSpeedBoost-BonusComposition.Tests.ps1`: 113 passed).

**Open work**: **Zero open retrospectives** - `Documentation/Retrospectives/INDEX.md` has no entry for
SkillSpeedBoost, SSB, or `crispywhips.skill_speed_boost`. **Feature complete**: the round-2 content
plan drained on 2026-10-04 (archived to `Documentation/Design/SkillSpeedBoost_Content_Update_Round_2_As_Built.md`),
no plan remains under `Documentation/Plans/SkillSpeedBoost/`, and the mod is a candidate for `Stable/`
per `Stable/README.md`. The only non-blocking items are the release step (publish 1.11.0), one human
playthrough (T1.316), and a fleet-wide external tooling gap (nstrip regeneration).

**Framework compliance**: Tier 2, heavy and correct - uses `Reflect` / `StatAccess` / `CardUtil` across
`GameLoadPatch`, `MorningBonusPatch`, `PlayerConditionService`, `AreaFamiliarityPatch` and the round-2
services. The card-facing Tier 2 services (`ActionRouter`, `SpawnService`, `TickEvents`,
`EncounterGuards`, `ContentModPlugin`) are correctly absent - a 0-CardData mod that hooks the leaf
`ChangeStatValue` coroutine has nothing to route or spawn. No deprecated patterns: no
`DropCollectionGuardPatch`, no `PatchAll()`, no unfiltered hot-path prefix, no
`ModLoaderVerison`/`ModEditorVersion`. **No further Tier-2 migration is owed.**

---

## Phase 0: Stabilize  *(nothing open)*

> Audit score is 10/10 with zero open CRITICAL, DESIGN GAP, MINOR or retrospectives. There is no
> stabilization work. The sole pre-existing Phase-0 item (nstrip regeneration) is a fleet-wide external
> tooling gap, carried below in the Maintenance Calendar and as fleet tracker T1.71, not a defect in
> this mod's shipped bytes.

---

## Phase 1: Release & verification

> The mod is built and green; these close out the current version.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Publish 1.11.0 to Nexus (Nexus holds 1.10.6) once ready | Release | P1 | Quick (`/nexus-publish upload SkillSpeedBoost`) |
| Repackage the suite + bump MUM so the embed carries 1.11.0 (the `EmbeddedZipCurrency.Tests.ps1` pin still reads 1.10.6, self-expiring on the next `/package-mod-suite`) | Release | P1 | Quick |
| T1.316 - human playthrough that the Partner's skills follow your settings (harness T1.316 already `pass`; a human run is the only unmeasured confirmation) | Runtime verification | P1 | Medium (human, in-game) |
| Move SkillSpeedBoost into `Stable/` once published (feature complete, drained plan) | Workspace hygiene | P2 | Quick (`Development_Tools/Move-ModFolder.ps1`) |

---

## Phase 2: Deferred specs  *(decisions, not scheduled features)*

> No plan doc exists; these are the carried-forward backlog from `.audit/ideas.md`. Each is a decision
> owner Jared must make before any code, re-verified unbuilt this pass.

### Decision: `SkillXpModifierService` - one public extension point
**What**: `SkillXpModifierService.Register(Func<string skillName, float multiplier>)`, each provider
folded into `ChangeStat_Post`'s composed multiplier as one more loop.
**Why**: Highest leverage on the roadmap. Every cross-mod idea in Phase 3 currently wants its own
bespoke public setter; this collapses all of them into consumer-side-only changes with zero further SSB
edits, and supersedes the older informal "promote `SkillSynergies` to public".
**Requires**: three owner decisions - provider ordering vs. `MaxComposedMultiplier`, per-tick delegate
cost vs. caching at action start, and the API-stability commitment.
**Complexity**: Medium.

### Decision: Area Familiarity decay
**What**: Fade familiarity after N unvisited in-game days, mirroring the mod's own staleness philosophy.
The last-visit days already live in `AreaFamiliarityDays.tsv` (shipped with the round-2 fade), so the
schema groundwork is done.
**Why**: Familiarity is the only monotonic thing in a mod whose entire thesis is that unused things
fade; `AreaFamiliarityMinBonus` set the floor but nothing yet lowers a learned location.
**Requires**: a decision on decay rate and floor; mind the TSV rollback rule.
**Complexity**: Medium.

---

## Phase 3: Integration & Depth

> All soft-dependency only. SSB must stay fully functional with none of these installed. Every row
> becomes a consumer-side-only change once `SkillXpModifierService` exists.

### RepeatAction: confirm multipliers fire on automated repeats
**What**: Verify SSB's bonuses apply on RA-driven repeat actions, then decide on an
`ExcludeAutomatedFromSynergy` toggle so AFK looping cannot farm the +50% synergy combo.
**Why**: the synergy combo is the one SSB bonus with an obvious automation exploit.
**Requires**: none. **Complexity**: Medium.

### Station-anchored themed XP bonus (ACT / WDI / CMC)
**What**: Already shipped as the `StationBonusMap` config (any CT2 station UID resolves through
`AreaFamiliarityPatch.CurrentLocationUid`). Remaining work is only documenting the cross-mod recipes
(a CMC Study Desk, an ACT forge, a WDI workshop) and, cleaner, routing it through
`SkillXpModifierService` once that exists.
**Requires**: none for config; the service for the clean form. **Complexity**: Quick (docs) / Medium.

### HerbsAndFungi "Focus Tonic" -> timed global XP flag
**What**: An H&F consumable sets a timed flag; SSB folds it in as one more multiplier for a duration -
the first concrete consumer of `SkillXpModifierService`.
**Requires**: the Phase 2 `SkillXpModifierService` decision. **Complexity**: Medium.

---

## Phase 4: Polish

> A 0-CardData mod has no art or animation phase. Polish here means config text, diagnostics, and docs.

| Item | What | Complexity |
|------|------|------------|
| Config-description pass | Re-read every `Config.Bind` description as a player would - it is the mod's ONLY player-facing text, with no CSV or card description to correct it | Quick |
| Document the config-write side effect | Choosing a profile writes its toggles into the player's `.cfg`, so an individual key must be set AFTER picking a profile; README/FEATURES troubleshooting should say so, since "my setting reverted" is the shape of the support question it generates | Quick |
| Area Familiarity UI feedback | Players cannot see their familiarity level without reading a TSV; a perk-card display is the likely approach - **needs an owner decision** on whether it clutters the perk list | Medium |

---

## Long-term Vision

SSB's natural endpoint is **the fleet's skill-progression substrate, not just a slider pack**. The
solo-player arc is finished - the mod now ships every knob a single player could want, all off or no-op
by default, with presets that set the whole stack coherently. The next axis is outward:
`SkillXpModifierService` turns SSB into the one place any mod in this repo can say "this thing should
teach you faster," and every cross-mod idea on the backlog waits on exactly that one surface.

**Potential major additions** (not yet justified - revisit after Phase 3):
- **Per-action context capture (tool quality / action difficulty / biome)** - the `ActionRoutine_Post`
  -> `ChangeStatValue` bridge already pairs reliably (shipped for tool/action bonuses); deeper
  per-action signals ride the same plumbing.
- **Live `NoveltyCooldownDuration` re-tune without a reload** - would delete the "takes effect after
  reloading" caveat; investigate whether the two novelty fields specifically are the unstable part of
  the deep stat-graph hot-reload, or the full graph rewrite was.

Any of these worth building belongs in a new plan under `Documentation/Plans/SkillSpeedBoost/` (or its
Considered table, with the reason, if ruled out); never create a stub under `Documentation/Ideas/`,
which keeps no backlog since 2026-10-01.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod SkillSpeedBoost` and update this roadmap |
| Game version update | **Regenerate `lib/Assembly-CSharp-nstrip.dll` with NStrip** (a plain DLL copy does NOT fix this mod; fleet tracker T1.71), rebuild, then `/update-mod-version` and `/diagnose-log` |
| After adding any new XP bonus | Confirm it appears in BOTH the composition body and the early-out guard, then run `Development_Tools/Tests/SkillSpeedBoost-BonusComposition.Tests.ps1` |
| After fixing a critical issue | Run `/critical-analysis SkillSpeedBoost` to verify the fix |
| After a version bump | Run `/export-to-repo SkillSpeedBoost`, then `/nexus-publish upload SkillSpeedBoost` |

---

## Skill Cheatsheet for This Mod

```
/audit-mod SkillSpeedBoost         - full health check, updates .audit/
/critical-analysis SkillSpeedBoost - adversarial review
/code-quality SkillSpeedBoost      - C# reliability scan
/feature-map SkillSpeedBoost       - regenerate the feature table
/build-mod SkillSpeedBoost         - build Release DLL
/deploy-mods SkillSpeedBoost       - build + deploy to game
/update-mod-version SkillSpeedBoost <ver> - bump version in all 3 files
/export-to-repo SkillSpeedBoost    - push to public repo
/nexus-publish upload SkillSpeedBoost - upload the release zip to Nexus
```

*(`/repair-items` and `/repair-blueprints` are not applicable - this mod ships no JSON content.)*


## Plan Reconciliation Log

> Written by `/cleanup-plans` Step 4b (here, by the session that drained the plan). Append-only:
> never reword, reorder or delete an entry. `/roadmap` Step 7 preserves this section verbatim when
> it overwrites the rest of this file.

### 2026-09-07 - Audit_Remediation_Plan
- **Verdict:** 0 fix rows (the threshold gate never cleared: 0 open CRITICAL, 0 Design Gap), 1 feature
  row. **N1 low-condition XP suppression BUILT this pass** as v1.10.2. The 1-prompt pack is drained,
  0 open prompts remain, and the plan's own "Not promoted, and why" table held 7 rows that are all
  blocked on a design decision or an investigation, every one of them already sequenced with its
  reason in this ROADMAP's Phase 2 / Phase 3 / Long-term Vision sections - so nothing buildable was
  left behind.
- **Disposition:** DELETE from `Documentation/Plans/SkillSpeedBoost/` and ARCHIVE to
  `Documentation/Design/SkillSpeedBoost_Audit_Remediation_As_Built.md` (plan verbatim plus its prompt
  pack as an appendix), per `Documentation/Plans/README.md` step 4 and matching the ACT and CMC
  archives of the same day. Nothing is erased: the 7 rejection reasons travel with the archived doc
  and are independently live in this file.
- **Pruned:** the whole plan doc (137 lines) and the whole 1-prompt pack, both archived rather than
  dropped. The `Documentation/Plans/SkillSpeedBoost/` scope folder is now gone.
- **Evidence:** N1 as built, re-derived on disk rather than taken from the plan. Pre-build shipped
  state re-confirmed: `LowCondition` matched 0 times across `SkillSpeedBoost/**/*.cs` (unbuilt, not
  merely unlisted). Post-build: 3 `Config.Bind` entries under a new `[LowCondition]` section with
  defaults `false` / `60f` / `0f-1f` clamped `0.5f`; `SetLowConditionPenaltyEnabled` called by
  `ApplyProfileSettings` (9 setters now, one per preset key); `IsAnyConditionBelow` sharing
  `ResolveInstance`/`Evaluate` with `IsWellRested` behind a `Mode` discriminator and a per-consumer
  `WarnOnce` key prefix, so all 7 failure paths breadcrumb under `[LowCondition]` as well;
  `lowConditionOn` present in BOTH the hoist block and the early-out guard. Build: `dotnet build -c
  Release` clean, 0 warnings / 0 errors. Gate: `SkillSpeedBoost-BonusComposition.Tests.ps1` 32/32
  green (was 26/26), the count rising by the 3 new invariant-4 tests and 1 new no-op-default case.
  The gate is not merely green - it was watched failing first: it went RED 27/1 on the two new
  magnitudes until they were classified in `knownNonFlagReads` (invariant 1c doing its job), and then
  on a scratchpad fixture copy of the tree, break A (restore `multiplier <= 1f` early-out) drove 2
  named tests red, break B (restore `if (target > after)`) drove 1 red, and break C (drop
  `&& !lowConditionOn` from the guard) drove the pre-existing flag check red, with 32/32 on the
  unbroken control. **What no check here can see:** whether the penalty actually changes a number in
  a running game. That is the whole content of T1.78/T1.79, filed as `pending` in
  `.claude/playthrough-test-status.json` BEFORE this record was written, and it is why the CHANGELOG
  says the 60/0.5 defaults are tunable starting points rather than balanced values.

### 2026-09-14 - XP_Base_Measurement_And_Release_Plan
- **Verdict:** 1 code phase of 4, BUILT as v1.10.4 (`7911dddce`); 0 code rows remain. Of the 3-prompt pack,
  Prompts 1 (base-value measurement and its gates) and 2 (ship) are done in `7911dddce`, and Prompt 3 is
  this record. Phases 2-4 are not code: Phase 2 is human verification, carried by tracker rows, and
  Phases 3-4 (audit, MUM repackage, export, public push, Nexus upload, the GitHub issue #2 reply, owner
  gates G1-G4) are release work, handed to the queued fleet mission `csff-ssb-1104-release-2026-09-14`.
- **Disposition:** ARCHIVE to `Documentation/Design/SkillSpeedBoost_XP_Base_Measurement_As_Built.md` (plan verbatim, pack as an appendix) and DELETE both
  from `Documentation/Plans/SkillSpeedBoost/`, per `Documentation/Plans/README.md` step 4, matching this
  mod's 2026-09-07 archive.
- **Pruned:** the whole plan doc (124 lines) and the whole 3-prompt pack, archived rather than
  dropped; the `Documentation/Plans/SkillSpeedBoost/` scope folder is gone again. The one live
  forward-pointer, the `Skill_Speed_Boost` pin comment in
  `Development_Tools/Tests/EmbeddedZipCurrency.Tests.ps1`, was repointed in the same commit. The
  plan-authoring fleet mission record still names the old path and was left alone as a dated record.
- **Evidence:** re-derived on disk, not taken from the plan. `7911dddce` carries `ComposeBaseTarget` in
  `MorningBonusPatch.cs`, `ModInfo.json` at 1.10.4, tracker row T1.83 `pending`, and T1.82's note with
  the cave check. `SkillSpeedBoost-BonusComposition.Tests.ps1` was watched failing before the code: on
  a `fe731eed7` fixture 38 pass / 8 fail, exactly the 8 new tests (invariant 7, the five
  `ComposeBaseTarget` cases, the `LogSkillXpGains` default); on the tree 46 / 0; planted breaks on
  fixture copies went red on named tests (displayed reads restored 44 / 2, floor deleted 45 / 1).
  Release build 0 warnings, and the deployed DLL's sha256 equals the built one. Full workspace suite
  on a fingerprinted tree: Tests Passed: 1175, Failed: 0, Skipped: 4; that run's tree moved only in the GameDataExtractor Debug outputs that Extract-GameData.Tests.ps1 rebuilds, so the six gates behind 7911dddce were re-run one by one on a fingerprint-stable tree, all green. **Built differently from the plan:** Design step 5's fallback to a
  displayed-value measurement had nothing to write to, so a missing or unreadable `CurrentBaseValue`
  now passes the change through unscaled with one warning per cause. **Plan premises corrected**
  against EA 0.67i data: +4 malus on 409 entries (not 407) across Chop Wood, Shovel Snow, Cut Tree and
  Dig Peat, on forest locations and trees rather than only clearings; Stealth XP +0.5 on 32 encounter
  entries (not 16); 7 of the 13 `Caves_*` cards carry +150 Stealth, not all of them. **What no check
  here can see:** whether a hunt under a pinned penalty actually gets its bonus in a running game.
  That is T1.83, filed `pending` before this record was written, alongside T1.82 (1.10.3, now with the
  cave check) and T1.76-T1.80 (1.10.2), all still pending.

### 2026-09-28 - Content_Update_Plan (1.11.0)
- **Verdict:** 4 features of 4 BUILT as v1.11.0 in `f72ec14a5` (Night-Owl Bonus, Station Bonus,
  Area Familiarity fade, Day Streak); 0 code rows remain. The release tail is done except the MUM
  repackage, which was deliberately NOT run (see Disposition). 4 verification rows filed and all 4
  confirmed by harness on the running game (T1.123-T1.126, session r44), so no verification debt is
  left on this plan.
- **Disposition:** ARCHIVE to `Documentation/Design/SkillSpeedBoost_Content_Update_1_11_0_As_Built.md`
  (plan verbatim, plus an "As built" section recording every departure) and DELETE from
  `Documentation/Plans/SkillSpeedBoost/`, per `Documentation/Plans/README.md` step 4, matching this
  mod's two earlier archives. The plan's "Considered, not selected" rows and its two open questions
  (`SkillXpModifierService` timing, Area Familiarity UI feedback) stay unbuilt and are carried with
  their reasons in `Documentation/Ideas/SkillSpeedBoost/IDEAS.md`, which now marks the four features
  shipped. MUM: `Development_Tools/Tests/EmbeddedZipCurrency.Tests.ps1` pins
  `'Skill_Speed_Boost' = '1.10.6'` (self-expiring) because four other sessions held uncommitted
  suite-mod builds and `Pack-Suite.ps1` edits when this shipped; the next `/package-mod-suite` embeds
  1.11.0 and expires the pin.
- **Pruned:** the whole plan doc (450 lines), archived rather than dropped; the
  `Documentation/Plans/SkillSpeedBoost/` scope folder is gone again. The one live forward-pointer, the
  reconciliation note in `Documentation/Ideas/SkillSpeedBoost/IDEAS.md`, was repointed in the same
  commit; the plan-authoring fleet mission record still names the old path and was left alone as a
  dated record.
- **Evidence:** re-derived on disk and in the running game, not taken from the plan. `f72ec14a5`
  carries the four features (`IsNightWindow`, `Plugin.GetStationBonusMultiplier`,
  `AreaFamiliarityService.DecayedVisits` under its flag, `SkillStreakService.NoteActivity`, each
  called from `MorningBonusPatch.ChangeStat_Post`) and `ModInfo.json` at 1.11.0.
  `SkillSpeedBoost-BonusComposition.Tests.ps1` plus the new `SkillSpeedBoost-BonusLogic.Tests.ps1`:
  128/128, watched red first on 11 planted breaks in a scratchpad fixture copy (17 failures, every
  break caught; one first-draft break was an equivalent mutant and was replaced). Release build 0
  warnings; the deployed DLL is byte-identical to the committed one. Harness walkthrough
  `TestHarness/client/reports/walkthrough-20260928-091722` on SSB 1.11.0, TestHarness 1.3.0, EA 0.68b:
  T1.123 24/24, T1.124 24/24, T1.125 26/26, T1.126 35/35. **Built differently from the plan:** the
  station bonus needed the structure tracker's early-out opened (the plan would have shipped it dead
  with familiarity off, and T1.124 proves the player-default case live), and the fade's last-visit
  days live in a sibling `AreaFamiliarityDays.tsv` rather than a third column, because the 1.10.6
  loader drops three-column rows and then saves over the file. **What no check here can see:**
  whether the default numbers (1.5x at night, +2% a day for 10 days, 14 days of grace) feel right in
  play; they are tunable starting points, which the CHANGELOG says.

### 2026-10-04 - MASTER_PLAN (Content Update round 2)
- **Verdict:** 18 features of 18 BUILT into the unpublished 1.11.0 (A1, A2, A3, B1, B2, B3, C1 to C5,
  D1 to D4, E1 to E3, in `f8d8fe21c` through `0fa97ed4b`); 0 code rows remain, the pack's only open
  prompts were the harness batch (Prompt 2) and the release tail (Prompt 11), both release work rather
  than code. 17 verification rows filed: 16 confirmed `pass` by harness runs on the running game
  (T1.300 to T1.304 recorded 2026-10-04 03:30, T1.305 to T1.315 recorded 2026-10-04 04:18 from session
  csff-mods-6e's run of 2026-10-03), 1 `pending` (T1.316, E2, never run in game; also W0.5's live half).
  8 ideas stay ruled out with their reasons and 12 earlier-shipped rows are listed, both in the As-Built's
  Considered table. The owner-only question (may SSB ship its first data file) is recorded as decided on
  its default, the no-data-file toast E3 shipped.
- **Disposition:** ARCHIVE to `Documentation/Design/SkillSpeedBoost_Content_Update_Round_2_As_Built.md`
  (condensed: selection with commits and rows, Considered table with reasons, Wave 0 answers, every
  as-built choice, engine facts S9, risk register, the open T1.316 readings and release tail) and DELETE
  `MASTER_PLAN.md` and `MASTER_PLAN_Implementation_Prompts.md` from `Documentation/Plans/SkillSpeedBoost/`,
  per `Documentation/Plans/README.md` step 4, matching this mod's three earlier archives. The full plan
  and pack text resolve at `0fa97ed4b`. SkillSpeedBoost is now feature complete under `Stable/README.md`.
- **Pruned:** the whole plan (809 lines) and the 2-prompt pack, archived rather than dropped; the
  `Documentation/Plans/SkillSpeedBoost/` folder is gone again. Live forward-pointers repointed to the
  As-Built in the same commit: the `source` field of tracker rows T1.300 to T1.316, the two plan-path
  citations in each of 14 scenarios under `TestHarness/client/scenarios/SSB/` (T1.312 to T1.314 cite the
  plan by section id only, which the As-Built keeps), the T1.316 row of
  `Playthrough_Test/Playthrough_Checklist.html`, the SkillSpeedBoost row of
  `Documentation/Ideas/IDEAS_OVERVIEW.md` and the two citations in
  `Documentation/Plans/UtilityBelt/MASTER_PLAN.md`. Left alone as dated records: the three earlier SSB
  As-Built files, this log's earlier entries and the 2026-10-01 `Documentation/Reports/Plan_Review/plan-review.html`
  snapshot.
- **Evidence:** re-derived this session, not taken from the plan. Tracker read through
  `.claude/playthrough-test-status.json`: all 17 ids present (16 in `confirmedItems` at `pass`, T1.316 in
  `items` at `pending`). Gates on the tree at `0fa97ed4b` plus this close-out, one Pester run at a time:
  `SkillSpeedBoost-BonusComposition.Tests.ps1` 113 passed / 0 failed, `SkillSpeedBoost-BonusLogic.Tests.ps1`
  699 / 0, `Preflight-MechChecks.Tests.ps1` 5 / 0. Section H: every one of the 72 `Config.Bind` keys is named
  in README or FEATURES; every backticked setting name in README, FEATURES, CHANGELOG and the ModInfo
  Description exists in source (the two exceptions are a deliberate misspelling example in FEATURES and a
  1.9.x method name in an old CHANGELOG entry); every class added by the plan (`ScopedBonusConfig`,
  `SkillListConfig`, `SynergyGroupConfig`, `SeasonBonusService`, `EnvironmentBonusService`, `PerkBonusService`,
  `ProgressionStore`, `RestedBankService`, `MilestoneService`, `XpOverflowService`, `XpReadout`, `XpDigest`,
  `PartnerSkillPatch`) has a call site outside its own file; version 1.11.0 agrees across `ModInfo.json`,
  `Plugin.cs` and the README, and `.claude/nexus-publish-status.json` still reads 1.10.6. `.audit/feature-map.md`
  (gitignored here) was regenerated for 1.11.0. **What no check here can see:** whether the E2 Partner path
  scales a real partner gain in game (T1.316), and whether the round-2 default magnitudes feel right in play.
  **Not done, and why:** the E2 build is not deployed, T1.316 is not run, and the MUM repackage and deploy are
  not run, because the shared game lane was held by another session; Nexus publication is the owner's call.
