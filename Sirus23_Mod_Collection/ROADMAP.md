# Roadmap: Sirus23 Mod Collection
Version at time of writing: 1.22.10
Date: 2026-10-23
Audit score: 10/10 - PASS (0 CRITICAL)

## Current State

**Theme**: Animal companions (Wolf, Fox, Owl, Hawk, Ferret, Truffle Hog) and full sheep husbandry - milking, wool, dairy, felt-working, pen-or-perish predation, and a draft-animal farmstead. A mid-game survival/husbandry loop that layers onto vanilla livestock.

**Content**: 71 items / 29 blueprints / 7 structures / 9 perks / 78 custom images / 2 triggers / 2 Animals manifests (Owl, Hawk) / 4 GameSourceModify patches / 3 EncounterGuards / 1 liquid.

**Stability**: 10/10 - 0 CRITICAL, 0 design gaps, 2 open warnings. Both open warnings are player-facing description-vs-data inaccuracies in the Working Animals module (Ox Plough's "the Bull gets hungrier" claim with no field implementing it; Ox Cart's unconditional haul text that is actually Bull-conditional) surfaced by the working-module critical-analysis (CURRENT at `a3e7e9e4c`, 2026-10-23). The 2026-10-21 reliability warnings (Tend Flock and owl cave-rule Debug-only catches, RecallWhistle saved-weight catch) are all RESOLVED; the 2026-10-20 per-type CRITICALs and their warnings are fixed in 8e0a8b7de, Pack Bond guard and Shears text in da5ee9b33. Module-scoped audits: 8 of 10 modules fully CURRENT and clean (companions, dairy, textiles, wolf, fox, owl, hawk, working); sheep and modwide reports are STALE and defer to the mod-level pass.

**Open work**: No open or pending retrospectives name this mod. No open plan existed before this pass (the vanilla-balance-and-niche plan was built and archived as `Documentation/Design/Sirus23_Vanilla_Balance_And_Niche_As_Built.md`, 1.22.6-1.22.8). `/audit-to-plan` may open a MASTER_PLAN remediation section for the two Ox docs warnings. What otherwise remains is in-game verification on tracker rows and the release of 1.22.9/1.22.10.

**Framework compliance**: Tier 2, fully adopted. `Plugin.cs` extends `ContentModPlugin`; patchers use `ActionRouter`, `SpawnService`, `TickEvents`, `CompanionService` and declarative `EncounterGuards`. Declarative content (BlueprintTabs, GameSourceModify, Animals manifests, EncounterGuards) used throughout. No deprecated patterns (`DropCollectionGuardPatch`, unfiltered hot-path prefixes, `ModLoaderVerison`) present.

---

## Phase 0: Stabilize

> Audit score is 10/10 with 0 CRITICAL and no open retrospectives. The two open warnings are player-facing docs-honesty, worth landing before the next public upload; neither fails the mod. The prior pass's two logging warnings (W1/W2) and the RecallWhistle catch are RESOLVED (same commit, 2026-10-21).

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Ox Plough Bull-hunger text (README:247): soften the "the Bull gets hungrier" line to match what ships (player/field/plough bear the cost), OR add a real Bull NPCStat hunger write on the "Plough the Field" path | Docs-honesty (or small feature) | P1 | Quick (text) / Medium (feature) |
| Ox Cart haul text (OxCart.json CardDescription + SimpCn + README:251): ADJUDICATE first - split container capacity (`MaxWeightCapacity: 6000`, real) from haul-while-held (the Bull-only CantHaulSoMuchPenalty drop); then qualify the text as Bull-conditional or make the no-Bull branch actually haul more. Confirm the Bull-branch haul magnitude in game before quoting a number | Docs-honesty | P1 | Quick (text) / Medium (verify + retune) |

---

## Phase 1: Foundation

> Table-stakes; all currently met - this phase is a standing checklist, not open work.

| Item | Type | State |
|------|------|-------|
| Versions synced across ModInfo/Plugin.cs/README (1.22.10 x3) | Version hygiene | DONE |
| Tier 2 framework adoption (ActionRouter/SpawnService/TickEvents/CompanionService) | Framework | DONE |
| SimpCn.csv Chinese parity (CLEAN; 1.22.10 Ferret food text in both files) | Localization | DONE |
| bin/Release sync clean | Build hygiene | DONE |
| Publish 1.22.9/1.22.10 to Nexus - re-verify with `/nexus-publish status`; build needs framework > Nexus 2.26.12, so ship in a framework batch; Pack Bond needs framework 2.28.0 | Release | TODO |

---

## Phase 2: Core Expansion

> The mod's core loops are complete and recently extended (1.22.8 niche content, 1.22.9/1.22.10 fixes). No new core content is justified until the pending in-game verification confirms what is already built. The next "expansion" work is closing verification debt, not writing features.

### Close 1.22.8 / 1.22.9 / 1.22.10 verification debt
**What**: Run the pending tracker rows in-game and record results: T2.587-T2.591 (the 1.22.9 audit fix set: feeder spill, Rotten Remains, bedroll sleep tag and plough, Pack Bond, EN/CN text), T2.582 (felt wet-weather), T2.583 (fox forager's-aid), T2.585 (owl pest-control cadence), T2.586 (lanolin waterproofing), the older pending companion/sheep rows (T2.561-T2.574, tame rows T2.551/T2.552 await `record`), plus the working-module Ox Cart haul-magnitude check (new, for Phase 0 W2).
**Why**: The code is built; these are open questions on `.claude/playthrough-test-status.json`, not findings. A failed row returns as a fix. Code work is not blocked on them (CLAUDE.md: code never waits on a playthrough).
**Requires**: a running build; the Transfer-TestingState sync if verified on the testbox.
**Complexity**: Medium (human playthrough, not code).

---

## Phase 3: Integration & Depth

> Cross-mod producer/consumer halves are all shipped or live in the partner mod's own scope.

### Cross-mod state (all resolved)
**What**: Wool washing (shipped in-mod), H&F herb remedies in Treat Wounds + herb-infused dairy (shipped), Lanolin-stands-in-for-Fat (shipped, needs framework 2.28.0+), ACT copper/iron Shears tier (Sirus `tag_Shears` CI already accepts it, no Sirus change), RepeatAction repeat milking/shearing (shipped, T2.22 confirmed), PartnerOverhaul NPCs eat Sirus food (shipped).
**Why**: Documented in `.audit/ideas.md` Cross-Mod table; no open Sirus-side work remains.
**Requires**: none.
**Complexity**: N/A (done).

---

## Phase 4: Polish

> Art and text are in good shape (78 custom images, WildHawk sprite wired via Animals manifest). No open art gaps.

| Item | What | Complexity |
|------|------|------------|
| Art review pass | Run `/review-art Sirus23_Mod_Collection` if any 1.22.8 image was regenerated/altered and needs a fresh verdict | Quick |
| Description accuracy | README is heavily caveated and matches the tracker; re-check "not yet confirmed in the game" lines after each verification pass so passing rows do not leave stale under-claims. The two Ox cards (Phase 0) are the current live description-vs-data gaps | Quick |

---

## Long-term Vision

> Where this mod should be at v1.23+.

Sirus23 is feature-complete for its theme: a companion-and-husbandry layer that closes vanilla's livestock and fibre gaps end to end (acquire -> tend -> produce -> preserve -> wear). Its natural endpoint is a fully-verified, Nexus-published 1.22.x with every companion, sheep, dairy and felt loop confirmed in-game and the two Ox docs gaps closed, then a move toward `Stable/` once the verification debt is cleared and the mod only updates to track game/framework versions. The biggest addition that would become natural at a larger scale is a dedicated shepherd/trainer NPC economy, but that was ruled out in the as-built plan (it belongs to CMC's village scope) - do not re-propose it here.

**Potential major additions** (not yet justified - revisit only after verification debt clears):
- None currently selected. The as-built plan's "Considered, not selected" rulings (husbandry mastery perk, shepherd-trainer NPC) carry their rejection reasons; do not re-promote.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod Sirus23_Mod_Collection` and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes, re-run `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis Sirus23_Mod_Collection` to verify the fix |
| After a verification pass | Re-check README "not yet confirmed" lines against `.claude/playthrough-test-status.json` |
| Before release | Run `/nexus-publish status`; ship in a framework batch (build needs framework > Nexus 2.26.12) |

---

## Skill Cheatsheet for This Mod

```
/audit-mod Sirus23_Mod_Collection         - full health check, updates .audit/
/critical-analysis Sirus23_Mod_Collection - adversarial review
/code-quality Sirus23_Mod_Collection      - C# reliability review
/repair-items Sirus23_Mod_Collection      - auto-fix item JSON issues
/build-mod Sirus23_Mod_Collection         - build Release DLL
/deploy-mods Sirus23_Mod_Collection       - build + deploy to game
/nexus-publish status                     - live-vs-local version per mod
/export-to-repo Sirus23_Mod_Collection    - push to public repo
```

---

## Plan Reconciliation Log

> Append-only: never reword, reorder or delete an entry. `/roadmap` Step 7 preserves
> this section verbatim when it overwrites the rest of this file. Record only what
> was ground-truthed.

### 2026-09-07 - `Documentation/Plans/Sirus23_Mod_Collection/` DRAINED and CLEARED
- **Verdict:** 16 rows total. 2 fixes resolved (F1 reclassified a false positive 2026-08-11, F2 fixed
  v1.13.2). 12 features shipped: N1, N2, N4, N5 and N6 plus the N3 Fox half before this pass, and N7,
  N9, N10, N11, N12, N13 in this pass at v1.21.0. 2 rows did not ship and were rehomed rather than
  dropped (N3 Wolf half, F3/N8 art). 0 rows remain dispatchable.
- **Disposition:** ARCHIVE, then DELETE from `Documentation/Plans/`. The plan and its seven-prompt
  pack now live at `Documentation/Design/Sirus23_Audit_Remediation_As_Built.md`, carried through
  VERBATIM (only the title and the stale `PLANS-POLICY-EXEMPT` marker were replaced), because several
  mid-build design decisions exist nowhere else: the `CookingRecipe` ruling, the `SelfTriggeredAction`
  ruling for the Sheep Pen night roll, and the Sheep Pen persistence verdict.
- **Pruned:** all 16 rows, plus all 7 prompts in the pack. The pack had already been drained of
  dispatchable work before this pass (it only ever decomposed N1-N6); N7 and N9-N13 were built
  directly from the plan rows and never became prompts. Before deleting either file, every
  substantive line of both was asserted present in the archive: 42 of 53 plan lines and 123 of 123
  pack lines matched exactly, and all 11 unmatched lines were confirmed to lie inside the header
  block that was deliberately replaced.
- **Evidence:** the six rows built this pass were verified by `dotnet build` (0 warnings / 0 errors),
  `Check-LocalizationParity.ps1` (Sirus23 393/393 CLEAN, +69 EN and +69 CN rows), and
  `Audit-Mod-Preflight.ps1` (0 CRITICAL, 1 WARNING - the pre-existing `wildowl_carcass` J27
  self-consuming-CI false positive, memory `reference_preflight_self_consuming_ci_blind_spot`). The
  full `Development_Tools/Tests` suite ran 1036 tests. Content correctness was checked differently in
  kind from the build: every `*WarpData` field in the new and patched JSON was asserted to have its
  `*WarpType` sibling, and every card reference was resolved against the 2,600 UIDs in
  `Documentation/GameData/CSFF-JsonData_Current/UniqueIDScriptableGUID/CardData.json` plus the mod's
  own UID set BEFORE any write - four of the eight patched cards were additionally proven
  round-trip byte-stable under `json.dumps(indent=4)` so the diff carries no reformatting noise, and
  the three companion cards, which are NOT round-trip stable, were patched by surgical text insert
  with prefix and suffix bytes asserted unchanged. Resulting diff is additive: +45 to +105 lines and
  0 to 1 deletions per patched file, the single deletion in each case being the line that gained a
  trailing comma. Rows N1/N2/N4/N5/N6 and the N3 Fox half were re-confirmed present on disk rather
  than believed from the plan's own status column. The two non-shipping rows were checked the same
  way: `GameSourceModify/` holds only the two `VanillaFox_Agent*.json` files, and both owl PNGs still
  hash `bcaa23e71b88553124054af717c07a26`.

**Two rows did NOT ship. Neither is homeless, and neither is a to-do for the owner:**

- **N3 Wolf half (Phase 2 "H2") - NOT BUILT, blocked on a human verdict.** This ROADMAP's own Phase 2
  entry states the prerequisite in writing: "do this after `T2.99` proves the fox pattern in-game."
  `T2.99` is a recorded FAIL from playthrough r33. A decompile trace this pass confirmed the mechanism
  is real (`NPCAgent.DragAndDropActions` is read only at `InGameNPC.CreateModelCard`,
  `.decomp/InGameNPC.cs:1327-1331`, which copies it onto the NPC's card as `CardInteractions`) and
  that a trigger miss is completely silent, but it could NOT distinguish three candidate causes; all
  three are now written into `T2.99`'s own tracker note. Rather than leave that as a question, the
  cheap discriminator the trace surfaced was filed as **`T2.207`**: drag an axe onto a vanilla
  **Wandering Oak**, the ONLY vanilla NPCAgent shipping a non-empty `DragAndDropActions` array
  (verified by parsing all 45), so it exercises the whole NPC drag-and-drop mechanism with zero mod
  content involved. **The hard constraint travels with the row:** extend `Agent_Wolf` /
  `Agent_WolfPack` / `Agent_PrimevalWolf` through `GameSourceModify` mirroring
  `GameSourceModify/VanillaFox_Agent1.json`, NEVER a new `sirus_wolf` species - the design the owner
  rejected on 2026-08-25, which cost a full rework in v1.20.3. All three wolf agents were confirmed
  this pass to have empty `DragAndDropActions` in vanilla and to be ordinary roaming NPCs with board
  cards, structurally identical to `Agent_Fox1`, so a plain field set is safe when the time comes.
  `Documentation/Ideas/Sirus23_Mod_Collection/IDEAS.md`'s own H2 entry was found still describing the
  REJECTED separate-species approach and was corrected in place in the same pass.
- **F3 / N8 distinct `OwlTied.png` art - BLOCKED ON TOOLING.** `/create-image` needs the OpenArt MCP,
  not connected in any session that has reached this row. Recorded in
  `Documentation/Ideas/Sirus23_Mod_Collection/IDEAS.md` (Design Gaps W3) and
  `Sirus23_Mod_Collection/.audit/ideas.md` (D1), both carrying the unblocking condition and the
  explicitly rejected shortcut (do not ship a third copy of the same PNG under a new name).

**Verification debt this pass created:** tracker rows **`T2.201`-`T2.206`** (one per shipped feature)
plus **`T2.207`** (the vanilla drag-and-drop discriminator). Every balance number in v1.21.0 is an
explicit placeholder, flagged as such in the CHANGELOG, the README and each tracker row.
`.audit/ideas.md` and `IDEAS.md` were both reconciled in the same pass so `/audit-to-plan` cannot
re-promote any of the six shipped rows.

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

### 2026-09-17 - Trait_Effect_Repair_Plan (closeout)
- **Scope:** Fleet. The same entry is in `Community_Mod_Chest/ROADMAP.md`, `HerbsAndFungi/ROADMAP.md`, `Sirus23_Mod_Collection/ROADMAP.md` and `CSFFModFramework/ROADMAP.md`, the four mods the plan names.
- **Verdict:** every phase built: P1 (CMC 1.68.25), P1b (1.68.26, `1f71be4a7`), P1c (1.68.27, `b5d93a885`), P7 (CMC 1.68.28 and HerbsAndFungi 1.13.1, `e4fad82ab`), P2-P6 (CMC 1.68.30, Sirus23 1.21.3, CSFFModFramework 2.26.0, `7b1c68f64`), and the pack's last open prompt, Prompt 1 (delete the temporary `Community_Mod_Chest/Patcher/TraitDiagnostics.cs` tracer), in `8d25ed810` with no version bump. Prompt 1 was gated on playthrough row T2.239, which is still pending; the owner lifted that gate on 2026-09-17 ("We need to be able to proceed with code work without being blocked on a full playthrough"). The plan doc's own row inventory was compared against the pack rather than trusting its 0-open count, and three plan items with no recorded outcome were settled this pass, none needing code: section 3.6's pre-release check (no `TriggerRange` on the four infection stats Deadly Disease rate-modifies), section 3.2's Swimmer 5 Sun re-check (kept), and section 3.1's "drop madness from Lunacy's text" (already done in 1.68.30). 0 plan rows unbuilt.
- **Disposition:** ARCHIVE to `Documentation/Design/Trait_Effect_Repair_As_Built.md`: the plan with an archive banner, dated notes for Prompt 1 and the three checks above, and the drained pack appended as an appendix with Prompt 1 removed. Pack `Documentation/Plans/Fleet/Trait_Effect_Repair_Plan_Implementation_Prompts.md` deleted. The plan holds the only record of the ground truth behind the rework (R1-R20, D1-D9) and of every deviation from its starting values, so it is kept, not deleted. Not published: the public releases are still CMC 1.68.24, HerbsAndFungi 1.13.0, Sirus23 1.21.2 and CSFFModFramework 2.25.32 (`.claude/mod-publish-status.json` commits read back through each `ModInfo.json`), and the export is the owner's call. R14/R16's reading of the player's "+1.2 Speed" as the +1.2 Aid rate is still unconfirmed with the player; it is recorded in the archive's section 1.
- **Pruned:** Prompt 1 from the pack before the pack was folded into the archive and deleted; nothing else. The plan left `Documentation/Plans/Fleet/` whole.
- **Evidence:** Prompt 1: `dotnet build -c Release` 0 warnings 0 errors; the built `bin/Release/Community_Mod_Chest.dll` holds `TraitDiagnostics` (UTF-8) 0 times and `[TRAITDIAG]` (UTF-16-LE) 0 times, against controls `RiverSwimPatch` 1 and `[RiverSwimPatch]` 9; `grep -rn TraitDiagnostics --include=*.cs Community_Mod_Chest` 0 lines (control `RiverSwimPatch` 15); no file under `Documentation/Retrospectives/` cites `TRAITDIAG` or `EnableTraitDiagnostics`. Plan rows against code: `Community_Mod_Chest/GameStat/` holds all nine `CMC_Trait*.json` (eight trait stats plus `CMC_TraitSkinShelter.json`); `TraitsTickHandler.cs`, `TraitsActionHandler.cs` and `TraitDiagnostics.cs` absent from `Patcher/`; gates (a)-(e) plus the extended river swim test present as `TraitStat-CompositeBands`, `Perk-HeldTestNotAllPerks`, `PerkAidRate-HoldsTier`, `PerkStatClamp-Reachability`, `StatBase-NoVanillaSource` and `CMC-RiverSwim` `.Tests.ps1`; `CSFFModFramework/Patching/PerkOriginTagPatch.cs` and `Discovery/ModTag.cs` present, `[Perks] ShowModOriginTag` bound at `CSFFModFramework/Plugin.cs` and documented in its README config table; 12 `ModInfo.json` files carry `ShortName`; 21 Sirus23 JSON files reference `SaturationDairy` (by name or its UID `f4b08d0250e6099419e010a83578b9db`); `ModInfo.json` versions CMC 1.68.30, HerbsAndFungi 1.13.2, Sirus23 1.21.3, CSFFModFramework 2.26.0. Section 3.6 check: a JSON walk of 29,435 files (the vanilla EA 0.67i UniqueID and ScriptableObject exports plus every mod folder, 0 unparseable) found 0 `TriggerRange` objects whose `StatWarpData` is Infection_Gastrointestinal `1a8d37787d69c9b4aa05d332921f3763`, Infection_UpperRespiratory `3407bfc804966194e9e369a7cce6d07d`, Infection_Systemic `dc3cae53109fd5945b5279ec6291caae` or Infection_LowerRespiratory `fa47d156a14dac842bcdcbdbf8504e35` (by UID or name), with the same walk finding the control, vanilla `Tgr_Anxiety` on Stress at 240; `.decomp/` (949 `.cs`) names no infection stat (controls `HourOfTheDayValue` 4 files, `StatValueTrigger` 13), and no mod `.cs` references the four UIDs. Section 3.2: vanilla `CharacterPerk` SunsCost is only ever 0, 15 or 30 (71/38/20 of 129), and Swimmer's 5 matches the six other five-Sun CMC perks (CMC's SunsCost counts: 0 x20, 1 x21, 5 x7, 15 x5, and 10, 20, 25, 30, 100 once each), which include Abundant Growth's +1.2 Aid rate and Wide Hands' +15 skill offset; Swimmer now delivers what its price was set for. Section 3.1: `madness`, `insan` and `mania` absent from `Pk_Lunacy.json`, `CMC_TraitLunacy.json` and its CSV rows. Tracker: T2.239 and T1.84-T1.114 all present in `.claude/playthrough-test-status.json`, all `pending`, so lifecycle gate 2 holds; the 33 plan and pack path citations in it and the one in `Playthrough_Test/Playthrough_Checklist.html` were repointed at the archive in the same commit.
- **Verification debt:** T2.239 (Nyctophobia and the tracer's log lines), T1.84-T1.88 (P1b), T1.89-T1.97 (P1c), T1.98-T1.101 (P7 medicine), T1.102-T1.109 (P2 condition traits), T1.110-T1.111 (P3 swim and Aid), T1.112 (P4 Sirus23 dairy), T1.113-T1.114 (P5 origin tag and clock hour), all `pending`. T2.239 part (5), T1.114 part (4) and the optional evidence in T1.102-T1.108 read `[TRAITDIAG]` lines that CMC 1.68.30 on the dev install still prints until the next CMC deploy and no later build prints; each of those rows carries a dated 2026-09-17 note saying so.

### 2026-09-28 - `Documentation/Plans/Sirus23_Mod_Collection/MASTER_PLAN.md` BUILT and CLEARED
- **Verdict:** 5 features. 4 shipped whole in 1.22.0 (Ox Plough, sheep manure, Wool Yarn at the vanilla loom
  with the rename, twin lambs); feature 5 shipped half: new art for the wild ewe and ram, while the owner kept
  both owl pictures after seeing three samples of each. 0 rows remain dispatchable.
- **Disposition:** ARCHIVE, then remove from `Documentation/Plans/`: the plan now lives at
  `Documentation/Design/Sirus23_Content_Update_1_22_0_As_Built.md`, body verbatim, only the title and status
  block replaced by an as-built header naming where the build departed from the text.
- **Pruned:** the whole plan (Wave 0 answers, five features, release tail). The owl half is not dropped: its
  decline, with both OpenArt history ids, is on `Documentation/Ideas/Sirus23_Mod_Collection/IDEAS.md` W3.
- **Evidence:** `dotnet build -c Release` 0 warnings / 0 errors, and a `-v:d` build showed every reference
  resolving into this mod's own `lib/` (which is why `lib/CSFFModFramework.dll` was refreshed: the stale 2.10.0
  copy lacked `Api.BlueprintAlternates`; recorded in the root CLAUDE.md game-update paragraph). Full
  `Development_Tools/Tests` suite 2012 passed / 4 failed, none of the four from this work: a WDI suite ZIP
  embed and a HomesteadPerks T1.127 citation (peers' in-flight releases), and the checklist-mirror pair, which
  then went green for T2.400-T2.404 once those rows were added to both checklist pages (the remaining misses
  are peers' rows). After the art was applied, `CardArt-ShippedForm`, `CardArt-PickRecord`,
  `PlaythroughRow-Citations`, `CardData-Integrity`, `Preflight-LocalizationParity`, `Csproj-ContentIncludes`,
  `Mod-Regression` and `Plans-Policy` passed (168 of 170, the 2 being that peer-row mirror). Art: every sample
  was finished by `/process-card-art`, then `key_enclosed.py` and `fill_holes.py --skip-paper 250`, because the
  paper key had eaten pale subject in 7 of 15 samples (a ram's horn pockets, the ewe's back, the plough's yoke
  tip, a snared owl's thigh; one owl companion sample was beyond repair). Owner picks: plough C, ram A, ewe A;
  Claude's predictions matched 1 of the 3 picked cards (the ewe).
- **Tracker rows holding the verification debt:** T2.400 (plough), T2.401 (manure), T2.402 (loom), T2.403
  (twins and the birth text), T2.404 (the new art).

### 2026-10-04 - `Documentation/Plans/Sirus23_Mod_Collection/` thirteen built features removed (plan KEPT, 32 prompts open)
- **Verdict:** 13 features of the round-2 plan are built and awaiting an in-game check, and were removed
  from the plan and its pack at the owner's request ("remove completed work from the plan and prompts").
  None is verified in game: all 15 of their tracker rows read `pending` on 2026-10-04, none `fail`, so none
  returns to code work. The plan keeps features 2, 3, 10, 11, 16, 17, 21 to 35 and 37 to 42 (32 open
  prompts, 0 awaiting verification).
- **Disposition:** DELETE from the plan and pack, no archive. Every removed section had already been
  collapsed to a one-line "BUILT, awaiting verification" pointer (the 2026-10-03 `/audit-plan` pass and the
  builds since), and every removed pack bullet's check is carried whole by its tracker row's VERIFY,
  EXPECTED and FAIL text. The last text of both files is at `869aa973b`. Section and prompt numbers are not
  reused.
- **Pruned (feature, pack prompt, build commit, tracker rows):**
  - 4 shear the ram, Prompt 3, `4398efb78` with the Shears wear-out fix `234f52dba`: T2.530, T2.532
  - 5 sheep cheese and dairy flavours, Prompt 4, `d4d05ea3c`: T2.531
  - 6 preserved butters keep butter's cooking uses, Prompt 5, `40e55c15c`: T2.533
  - 7 sheep butter at vanilla's butter hooks, Prompt 6, raw-filling half `490ef329a`, melt half `481988a7f`: T2.534, T2.541
  - 8 lanolin stands in for Fat, Prompt 28, `d8f4b5e0`: T2.538
  - 9 the animal trader sells sheep, Prompt 7, `51c3de2b6`: T2.535
  - 12 fleece-on sheepskin as Fresh Fur, Prompt 8, `590cc56c6`: T2.536
  - 13 felt garments take dye, Prompt 9, `f6e5df87d`: T2.537
  - 14 Felt Rug, Prompt 10, `c8fa8283`: T2.539
  - 15 felt outfit set bonus, Prompt 11, `39efab7c`: T2.540
  - 18 Fox and Owl Health with Treat Wounds, Prompt 12, inside `50d3fdb91` ("Wide work"): T2.544, filed
    in this pass because it had no row
  - 19 Wolf Den and Owl Perch, Prompt 13, `b420599e3`: T2.543
  - 20 Ox Cart, Prompt 14, `e5852f8d0`: T2.542
- **Evidence:** measured in this pass: the status of every row above read from
  `.claude/playthrough-test-status.json` (15 `pending`); feature 18's JSON re-read before filing T2.544
  (both companions carry SpecialDurability1 480 with OnZero "Fox Has Died" / "Owl Has Flown Away!", the
  PassiveEffects "Starving Health Drain" and "Parched Health Drain" at Special1 -2, and a Treat Wounds CI);
  feature 19's gates at its build (Release build 0 warnings; Preflight-MechChecks, CardData-Integrity,
  CardArt-ShippedForm, Csproj-ContentIncludes, Localization-DuplicateKeys, Localization-EnglishColumnCjk
  green; parity 479/479). The other features' presence in the source rests on the 2026-10-03 `/audit-plan`
  pass recorded in the plan's header (each re-read from source, JSON, CSVs and tracker) and on their own
  build commits; this pass did not re-read them. One known defect is open and is not this plan's work: a
  harness run on T2.542 found the Ox Cart's Bull gate completing with no Bull, because 22 of the 24 Sirus23
  blueprints wrote `CardsOnBoard` `Amount`, which binds to nothing (the real field is
  `CardOnBoardSubObjective.Quantity`); another session's sweep fixing all 22 was uncommitted when this entry
  was written.
- **Tracker rows holding the verification debt:** T2.530 to T2.544 (all `pending`). Six of them (T2.538,
  T2.539, T2.540, T2.541, T2.542, T2.543) cited a removed plan section as their source and now cite this
  entry instead.

### 2026-10-04 - MASTER_PLAN (second pass)
- **Verdict:** counts: 32 pack prompts and 27 plan features checked (2, 3, 10, 11, 16, 17, 21 to 35, 37 to 42, plus the Release tail and open questions); 0 verified done, 0 built-awaiting, 27 open (32 open pack prompts, 0 awaiting verification). One STALE-CLAIM class corrected: three source line citations. Two history paragraphs deleted.
- **Disposition:** KEEP IN PLACE (open work remains; Prompt 1, game lane, is still the first dispatchable entry).
- **Pruned:** plan header: the two "Progress 2026-10-03" paragraphs (features 7 and 20, built and pruned earlier today, their checks live on T2.541 and T2.542). No section and no pack prompt removed, because nothing left in either is done. Corrections: `GameLoadPatch.cs` `SetCollection` citation 537 to 623, README "unvisited environment" 145 to 154, README "does not herd" 143 to 152; a new Audit status paragraph appended.
- **Evidence:** `git grep -F` over `Sirus23_Mod_Collection/` (excluding bin and obj) for 26 identifiers of the open features (Nourishment, VanillaWolf_Agent, WolfTameInit, fc_wolf_pup, Fence Condition, SheepBell, sh_predator_tracks, hw_event_blackbear, hw_event_eagle, Hawk, sir_ferret, sh_washed_wool, fc_owlet, RecallWhistle, tag_x_AnimalRemedy, sh_truffle_hog, Pick Through, PartnerDuty, tag_FibersMedium and others): zero hits in source for every one except Nourishment and Fence Condition, which hit only `ROADMAP.md` and `.audit/ideas.md` prose; control: `AddAlternateIngredientIn` hit `Patcher/GameLoadPatch.cs` (a built feature). `ls TestHarness/client/scenarios/SIR`: 18 files, none for T2.400 to T2.403 or T2.454 (T2.541 and T2.542 scenarios exist). Tracker script: every T-id in the plan (22) and the pack (11) resolves in `.claude/playthrough-test-status.json`, zero missing; T2.99 and T2.101 `fail`, T2.207 and T2.400 to T2.404 and T2.454 `pending`. `git log` since the 16:43 prune (`ece6f1e3e`): `0545edd00` (22 blueprints, `CardsOnBoard` `Quantity`, which closes the Ox Cart defect named in the first entry) and doc-only commits; no open feature touched. Not verified: whether the deployed DLL matches source (no game run), and the W0 research verdicts (read as written, not re-derived).
- **Verification debt:** T2.99 (fail), T2.101 (fail), T2.207, T2.400, T2.401, T2.402, T2.403, T2.404, T2.454 (pending) gate the open work; T2.530 to T2.544 (pending) are the checks for the pruned built features.

### 2026-10-07 - MASTER_PLAN (cleanup-plans fleet sweep)
- **Verdict:** 19 open plan features (3, 22, 23, 25, 27, 28 to 35, 37 to 42), the four built-but-pruned features 10, 11, 16 and 17, and all 22 pack prompts re-checked against source and the newest harness reports. Verified done and pruned: 16 plan claims (section 0.2 rows 21, 24 and 26; ten answered Wave-0 rows for built features; three Risk rows for built features) plus the Execution Status history and pack Prompt 29's done step 5. Obsolete: 0. Awaiting verification: 3 pack entries (feature 3's T2.400 review; features 10, 11, 16 and 17; the felt pile toggle). FAILED: 1, feature 19 (T2.543's rerun, 2026-10-07 02:24, step 93: wolf Morale -1.40625 in another place, expected -1; not yet written on the row), re-opened as plan section 19. Stale claims corrected: 7 (the W0-13 row's Morale multiply, open questions 5, 7 and 8, two README line citations, the Release tail's feature 16 sentence). Open: 22 prompts (18 plan features plus probes and closeouts), none built.
- **Disposition:** KEEP IN PLACE - 22 open prompts of code work remain (Prompt 47 art, the W0-15/18/21/31 probes, features 23 and 27, the two release closeouts, and Phases F and G, which wait on Sirus's sign-off).
- **Pruned:** plan: Execution Status section; the five dated history paragraphs of the status block, condensed into one History paragraph; section 0.2 rows 21, 24, 26; section 0.4 rows W0-2 to W0-7, W0-11, W0-14, W0-16, W0-17 (their DECISION lines stay in the research files); Risk rows for features 10/11 (SetCollection, merged in `a3180d17a`), 20 (Bull travel, held by T2.542) and 26 (raid UIDs, T2.549 confirmed). Pack: Prompt 29 step 5; the recorded-PASS clauses of the Awaiting list; Prompt 46's twin-lamb rerun clause (T2.403 confirmed). Added: plan section 19 (FAILED), a Risk row for the vanilla multiplier cancel flaw, the pack's Failed list; Prompt 47 widened to the Wolf Pup's borrowed sprite and moved first (no game needed).
- **Evidence:** `git log ea241d274..HEAD -- Sirus23_Mod_Collection` (excluding bin and obj): one commit, `a3180d17a` (features 10, 11, 16, 17 and Prompt 46), spot-checked (`ConfigureBirthReceivingChanges` sets Transform; `PatchSheepReproduction` merges by EffectName prefix; `WolfPup.json`, `VanillaWolf_Agent.json`, `VanillaWolfPack_Agent.json` present). Grep over the mod source for 20 open-feature identifiers (hawk, ferret, washed_wool, owlet, recall_whistle, truffle_hog, tag_x_AnimalRemedy, tag_Fibers, PartnerDuty, DryFodder, Pick Through, the three Enclosure UIDs and others): hits only in `Patcher/IndoorOrCaveEnv.cs` (Enclosure UIDs, the companion stay list, not the night roll) and `GameLoadPatch.cs` (`AddAlternateIngredientIn`, built feature 8; the control that the probe hits). `CardImageWarpData` of SheepBell, Bp_SheepBell, PredatorTracks, OxCart, Bp_OxCart and WolfPup still read CopperNecklace, OwlTracks, sh_ox_plough and WolfCompanion. No "MEASURED (harness" section in any of the seven research files; no "Sirus sign-off" line in the plan. Harness reports on disk (`TestHarness/client/reports/`, gitignored): T2.543 20261007-022417 FAIL at step 93 (-1.40625 = (-1 - 2 x 0.75) x 0.5625, matching `.decomp/InGameCardBase.cs` `CancelPassiveEffectDurabilities`, which checks a Multiply flag for Spoilage only); T2.550 20261007-044540 PASS; T2.400 20261006-211039 PASS with steps 7 and 56 allowed failures and no `review.json`. Deployed `Sirus23_Mod_Collection.dll` md5 equals `bin/Release` (92389128...). A JSON scan of every Sirus23 card found non-Spoilage Multiply flags on 9 effects: the Wolf Den, the Owl Perch, and the Well Fed, Spring Growth and Winter Chill effects on TameSheep, MaleSheep and LactatingSheep. The fix is another session's uncommitted `CSFFModFramework/Patching/BugFixes/PassiveMultiplierCancelFix.cs` (gate `Framework-PassiveMultiplierCancel.Tests.ps1`). PartnerOverhaul W0.5 still unanswered.
- **Verification debt:** T2.543 (FAILED run, row still pending; rerun after the framework fix), T2.553 and T2.554 (need scenarios and the framework fix), T2.550 (PASS awaiting record), T2.551, T2.552, T2.400 (review), T2.404, T2.555, T2.556, T2.542, T2.99 (fail; person's live tame), T2.207.

### 2026-10-09 - MASTER_PLAN
- **Verdict:** 19 open plan features (3, 19, 22, 23, 25, 27, 28 to 35, 37 to 42) and all 22 prior pack prompts re-checked against source, the tracker after `aa60ef1b0`, `9191af9f9`, `8ba267cb3` and `51641ef64`, and the 2026-10-09 six-rig harness reports. Verified done: 3 pack Awaiting entries' rows (T2.550, T2.555, T2.556 confirmed). Obsolete: 0. Awaiting verification: 1 (feature 3's T2.400, three unrecorded harness passes needing the allowed-failure review). FAILED: 4 items, all in harness runs not yet on the rows: T2.543 (feature 19, now step 101), T2.553 and T2.554 (features 10, 11: milk rate 1.51365), T2.551 and T2.552 (features 16, 17: the mod's own AgentExists retire error, no pup). Stale claims corrected: 2 (section 19 and the Risk row called the framework multiplier-cancel fix uncommitted; it landed in `e57d44cfc`). Open: 23 prompts (the new fix pass Prompt 48, art Prompt 47, four probes, features 23 and 27, two closeouts, Prompt 30, Phases F and G on Sirus's sign-off), none built.
- **Disposition:** KEEP IN PLACE - 23 open prompts of code work remain; Prompt 48 (fix pass) is first, then Prompt 47.
- **Pruned:** pack: the Awaiting entries "Features 10, 11, 16 and 17" (T2.550 confirmed; T2.551 to T2.554 moved to the Failed list) and "Felt pile toggle" (T2.555 now in confirmedItems); the old Failed entry's "uncommitted fix" text. Added: pack Prompt 48 and a four-item Failed list; Prompt 29 now depends on Prompt 48. Plan: Next up rewritten for 2026-10-09; section 19 corrected and its failure updated; two Risk rows updated; one dated Status paragraph appended. No plan section deleted, so no citation sweep was needed.
- **Evidence:** `git log 189f8b99e..HEAD -- Sirus23_Mod_Collection` and the plan and research folders: no Sirus23 source commit (only scenario commits `48b0a0c1c`, `e57d44cfc`, `2652a05af`, `a47b5b8e3`, `dcb8d6cc7` under `TestHarness/client/scenarios/SIR/`, and the 2026-10-07 plan sweep `56ef680ed`). `grep -c "MEASURED (harness"` over the seven research files: 0 each; `grep "Sirus sign-off:"` in the plan: only the 2026-10-07 status prose (control: the probe hits that line). `grep PassiveMultiplierCancel CSFFModFramework` hits `Plugin.cs` (wired) and the built `CSFFModFramework.dll` (1 hit). `TestHarness/client/reports/walkthrough-20261009-041821/build.json`: Sirus23 sha256 `00ab225c...` and framework `934166b0...`, both equal to `bin/Release`. Reports read: T2.543 20261009-075304 FAIL step 101 (Hunger -0.5625, expected -0.75; steps 92 to 95 incl. Morale -1 PASS); T2.553 20261009-072410 FAIL steps 34, 73 to 75; T2.554 20261009-080235 FAIL steps 28, 54, 68 (Usage rate 1.51365); T2.551 20261009-063532 FAIL step 26 and T2.552 20261009-080431 FAIL steps 20, 23, 25, 33 (log error from `Patcher/CompanionHuntPatch.cs` `TrySetStatValue`, whose two null exits carry no cause); T2.542 20261009-075338 FAIL step 67 (travel hidden by stats); T2.99 20261009-061625 FAIL step 31 (fresh berry drop does nothing); T2.400 20261009-074618 PASS. The tracker owner's 2026-10-09 skeptic list rejects T2.404's pass; T2.400 is in neither the confirmed nor the rejected list. PartnerOverhaul MASTER_PLAN W0.5 still has no answer.
- **Verification debt:** T2.400 (pending, review), T2.404 (pending, person), T2.543, T2.551, T2.552, T2.553, T2.554 (pending, harness FAILs awaiting Prompt 48), T2.542 (pending, harness FAIL), T2.99 (fail; live tame), T2.207 (pending).

### 2026-10-09 (later) - MASTER_PLAN, second /cleanup-plans pass
- **Verdict:** NOT drained. 23 open prompts (48, 47, 15, 16, 23, 27, 29, 30, 31-45), 1 awaiting verification (T2.400), 4 failed (Prompt 48). Prompt 48 narrowed: T2.543, T2.542 and T2.99 are scenario defects fixed offline in 618ef7c78 (rerun only); code remains for the wolf tame retire and the milk rate.
- **Disposition:** KEEP IN PLACE - code work remains.
- **Pruned:** nothing. Corrected: section 19 (T2.543 scenario defect); risk register row 1 (cause is CompanionHuntPatch.TrySetStatValue looking up a public SetStatValue that does not exist; .decomp/InGameNPCStat.cs has only a private IEnumerator SetStatValue and a public SetStatValueFromEditor); section 0.3 water trough (companions drink only water since 31034e212).
- **Evidence:** audited against 1d9cbf121. Commits since e5b8406d2: 31034e212 and 287d0a88c (not plan work), 618ef7c78 (scenario rework), 7219cd11d. Four sprites still borrowed; no research file has a MEASURED (harness section; no "Sirus sign-off:" line; PartnerOverhaul W0.5 unanswered in its own plan. PlaythroughRow-Citations 13/13.
- **Verification debt:** T2.400 (record owed); T2.99, T2.542, T2.543 (reruns); T2.551, T2.552, T2.553, T2.554 (Prompt 48 code); T2.102 may need reopening after 287d0a88c.

### 2026-10-11 - Fleet MASTER_PLAN (cross-mod decoupling), /cleanup-plans Fleet apply
- Scope: Fleet (cross-mod plan; the same entry is recorded in the ROADMAP of every mod it names: Community_Mod_Chest, AdvancedCopperTools, WaterDrivenInfrastructure, HomesteadPerks, HerbsAndFungi, Sirus23_Mod_Collection, CSFFModFramework).
- **Verdict:** DRAINED. Features 1-13 all built (1-3, 6, 7, 9, 12 in the commits `33ab18fe9`, `a3f965d43`, `76aac3d8a`, `cc0115b9e`, `fed7f7d0a`, `c6b2bad35`, `31034e212`, `e7b558ead`, `7219cd11d`), 0 open pack prompts, 0 unbuilt rows; every acceptance row recorded CONFIRMED (T2.789, T2.632, T2.997, T2.999, T2.998, T2.549, T2.557, T2.560, T2.1236 to T2.1242). Open question 4 settled by measurement, not by an owner choice.
- **Disposition:** ARCHIVE - the plan moved to `Documentation/Design/Fleet_Cross_Mod_Decoupling_As_Built.md` with its Considered table and its drained prompt pack appended; the pack was deleted.
- **Pruned:** plan sections 1, 2, 3, 6, 7, 9 and 12 (each BUILT and confirmed), the stale "awaiting verification" pointers, open question 4's default text (replaced by its measured outcome); pack Prompts 9 and 10 (Prompt 10 had already been built in `e7b558ead`, the pack was stale on that point).
- **Evidence:** on dev lane rig 6 at HEAD (H&F, Harsh Wilds and Sirus23 deployed from the working tree, the rest synced from the real install), harness PASS for T2.557, T2.560, T2.632, T2.997 (both variants), T2.999 and T2.1236 to T2.1242; two independent skeptic reviews of all twelve rows (`.claude/handoffs/skeptic-review-2026-10-11-fleet/r-a.md`, `r-b.md`): 11 RECORD-PASS, and T2.560 FIX-SCENARIO (its brine leg dragged the non-draggable Liquid sub-card, so it passed on any build; fixed in the scenario and rerun PASS with a real refusal, "Dropping 'Pickle Brine' onto 'Wolf Companion' does nothing", beside the same container type with water running Give Water once). Three of the seven absent-partner pins had drifted since 2026-10-09 and were re-measured from real alone-boots (CMC 48 entries, five sprites now resolve; H&F plus twelve of its own map-node forward references; Harsh Wilds plus the Forest Lore `fl_mud_volcano` weight row; WDI now 0 lines), each entry checked against `Documentation/Interop_Registry.md`. End state: `grep BepInDependency(` over every `Plugin.cs` finds 13 files and none names anything but CSFFModFramework (the 13 matches are the control that the scan can hit). Open question 4 probe: with Sirus23 set aside the Wool-Quilted Cap and Vest each keep four requirement slots and the Wool one is null (measured), and a researched blueprint is left alone by `AcademyCourseService.HideWoolQuiltedIfNoWool` (read from code).
- **Verification debt:** none open. One unplayed fact, deliberately without a row: what a craft of an already-researched Wool-Quilted recipe asks for after Sirus23 is removed (the game reads a null slot as no requirement, the fleet audit's reading of the code); CMC's README states it as unplayed. Releases still owed per mod under its own version rules (`.claude/nexus-publish-status.json` against `ModInfo.json`, read 2026-10-11): framework 2.28.0 (Nexus holds 2.26.12), CMC 1.69.6 (1.68.45), ACT 1.18.1 (1.16.10), WDI 1.12.2 (1.11.3), HSP 1.3.1 (1.2.4), H&F 1.14.1 (1.13.7) and Sirus23 1.22.0 (1.21.6); MUM not repacked for this change.

### 2026-10-12 - MASTER_PLAN (round 2), /cleanup-plans archive after the closeout
- Scope: `Documentation/Plans/Sirus23_Mod_Collection/` (the round 2 plan, features 28 to 42 and the audit remediation R1 to R10, with its pack).
- **Verdict:** DRAINED of code. Every feature is built or ruled out (feature 42 is ruled out, plan section 0.3); the pack declared 0 open prompts; the audit remediation R1 to R10 landed in `01c8a43a8`. The plan's own closeout (pack Prompt 45) and its "Next up" list say what remains: verification and release, none of it code.
- **Disposition:** ARCHIVE - the plan moved to `Documentation/Design/Sirus23_Mod_Collection_As_Built.md` with its Considered table (section 0.3, the rejection reasons) and the drained pack appended verbatim; both files were removed from `Documentation/Plans/`, which no longer has a Sirus23 folder. Inbound paths in the tracker (23 `source` strings), four As_Built records, `Documentation/Ideas/IDEAS_OVERVIEW.md`, the Harsh Wilds plan and the seven research files were repointed; the Ideas-era log entries above, the `.audit/` files and the spent `Development_Tools/Sirus23/` one-shot scripts keep the old path as history. The mod stays at the repo root; its move to `Stable/` is a separate step (`Move-ModFolder.ps1` refuses while the folder has uncommitted changes, and `.art/review/verdicts.json` was uncommitted when this was filed).
- **Pruned:** nothing was deleted from the content of either file; only the plan's stale exemption marker (`PLANS-POLICY-EXEMPT`, which waited for this archive) was replaced by a one-line note and an as-built banner was added. The banner corrects one stale line: the version triple is 1.22.1 since the release prep `61af5c011`, not 1.22.0.
- **Evidence:** read this pass, not believed from the plan: all 32 tracker ids the pack and plan name (T2.99, T2.102, T2.133, T2.176, T2.178, T2.404, T2.530, T2.535, T2.541, T2.542, T2.543, T2.551 to T2.554, T2.561 to T2.575, T2.1382, T2.1383) exist as rows in `.claude/playthrough-test-status.json`; source for the hawk (`Animals/Hawk.json`, `CardData/Item/HawkCompanion.json`), ferret (`Patcher/FerretTrapPatch.cs`), Recall Whistle (`Patcher/RecallWhistlePatch.cs`) and truffle hog (`Patcher/TruffleHogPatch.cs`) is on disk; `dotnet build -c Release` of the mod (to a temp folder) 0 warnings, 0 errors; `ModInfo.json` reads 1.22.1. Not re-run this pass: the harness and the Pester suite beyond the plan-policy gates named in the commit.
- **Verification debt:** every item sits on its own tracker row: person checks T2.404 (art for the ox plough, wild sheep, Sheep Bell and Predator Tracks), T2.1382 (Feather Pillow balance), T2.1383 (Felt Rug art), T2.133 (the wild owl duty guard, no scenario); harness-passed rows held for a person (T2.569, T2.571, T2.572, T2.573, T2.574); earlier rows still pending (T2.102, T2.530, T2.535, T2.541, T2.176, T2.178). Owed outside the tracker: Sirus's review of the CHANGELOG's Phase F and G group before anything is exported, uploaded or published (a decline moves that feature to section 0.3 of the As_Built and takes its JSON, code and docs out of the mod), and the deploy line (framework first, Sirus23, repackage the suite, Mod Update Manager last).

### 2026-10-18 - MASTER_PLAN (vanilla balance and niche content), built and archived
- Scope: `Documentation/Plans/Sirus23_Mod_Collection/` (the 2026-10-14 plan: section 1 balance rows S23-B1 to S23-B29, section 2 niche rows N1 to N5, with its pack).
- **Verdict:** DRAINED. Section 1 shipped in 1.22.6 and its carried-passive leak in 1.22.7 (`794fbf016`); N2 shipped in 1.22.7 (`2f0164a7c`; the pack still listed it as open); N1, N3, N4 and N5 built in 1.22.8 (unreleased). S23-B17 stays ruled out.
- **Disposition:** ARCHIVE - the plan moved to `Documentation/Design/Sirus23_Vanilla_Balance_And_Niche_As_Built.md` with a section 2.1 As built table; plan and pack removed, so `Documentation/Plans/` again has no Sirus23 folder. The 18 tracker `source`/`note` paths, the Sirus23-Balance test header, this ROADMAP, the CMC plan's Comfort release note and `Documentation/Ideas/IDEAS_OVERVIEW.md` were repointed; dated log entries and the spent `Development_Tools/Sirus23/` scripts keep the old path as history.
- **Decisions:** owner, 2026-10-18 (chat): N4's treatment is raw Lanolin and only the six Sirus23 felt/wool pieces take it. Dispatcher retune before commit: N4 RainProtection 0.75 -> 0.28 per piece, because at 0.75 a full treated set reached Max Protection, above any vanilla coat (owner 2026-10-14, "balance, not steamroll").
- **Evidence:** `Deploy-Mods.ps1 -Sirus23ModCollection` built and deployed 1.22.8 clean (the N3 C# compiled). Workspace Pester run 2026-10-18: Sirus23-Balance, Framework-EncounterGuardScope, Localization and CardArt containers green. The suite total was red for causes outside this mod: MUM-CompiledLogic and the framework fixtures could not start dotnet in the run's shell, plus Far Territory, Harsh Wilds, ACT pre-rewrite pins and embedded-ZIP currency for six mods (Sirus23 among them, owed at the suite pack).
- **Verification debt:** T2.582 (N1), T2.583 (N5), T2.585 (N3), T2.586 (N4), with T2.576 to T2.581 and T2.1713 to T2.1723 still pending; the id between T2.583 and T2.585 is unused. Release: framework, CMC and Sirus23 1.22.8 in one suite batch, Mod Update Manager last.
