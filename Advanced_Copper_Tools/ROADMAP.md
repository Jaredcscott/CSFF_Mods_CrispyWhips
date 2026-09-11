# Roadmap: AdvancedCopperTools
Version at time of writing: 1.16.5
Date: 2026-09-06
Audit score: 10/10 - PASS (0 CRITICAL, 0 Design Gap; consolidated 2026-09-06, Phase 0 cleared 2026-09-06)

> **Rewritten 2026-09-06 after a ground-truthed consolidation.** The 2026-09-05 roadmap rested on a
> critical-analysis (verdict BROKEN, 6/10) that predated the 2026-09-06 04:45 commit `9c5ea23c6`,
> which shipped the joint C1+G1 fix (real bronze-grade sheet supply + live SD4 gate), retired the dead
> armor-scaling scaffolding, and closed the feature-honesty findings (phantom perk, false soap claim,
> art-reuse disclosure). Every "resolved" claim in the current `.audit/summary.md` was re-verified on
> disk this pass. The mod is back on the release line; what remains is a short doc-honesty tail and
> optional content expansion.

## Current State

**Theme**: Mid-game copper-and-bronze metalworking - a comfort/throughput/light tier layered on vanilla
metallurgy, plus a mineable cave system (copper/tin/iron veins, salt mine, rock quarry) that supplies it.

**Content**: 51 items / 47 blueprints / 24 structures / 7 perks / 47 custom images / 1 WorldMap.

**Stability**: 9/10 PASS - 0 CRITICAL, 0 Design Gap, 3 WARNING (all doc-honesty/reliability), 6 MINOR.

**Open work**: no open ACT retrospectives (all Graduated or archived-with-pointer).

**Framework compliance**: Tier 2 - uses `Api.*`/`ActionRouter`/`SpawnService`-class services across
6 patchers (`GameLoadPatch`, `HeatHeldLiquidPatch`, `TinOreSmeltPatch`, `IronVeinQualityPatch`,
`SawEffectPatch`, `TeaStationPatch`, `VanillaFireKettlePatch`). No deprecated patterns
(`DropCollectionGuardPatch`/`InitializeNullFields`/`PatchAll()`) present. Versions synced 1.16.5×3.
Chinese localization present (`SimpCn.csv`, parity clean 741/741).

---

## Phase 0: Stabilize  *(skipped - audit score ≥ 8, no open retrospectives)*

Nothing outstanding. The prior Phase 0 (metal-sheet gate, Bronze craftability, feature honesty) shipped
in v1.16.5 and is re-verified resolved.

---

## Phase 1: Foundation  *(doc-honesty cleanup - cheap, do next)*

> The 1.16.5 armor-scaling removal and the README tier-scope fix were done in the same session but not
> reconciled with each other, leaving a small false trail. All Quick.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| ~~README:394 - drop the "repairs a missing `ArmorValueDurabilitiesMultiplier` entry" clause (summary W1)~~ **DONE 2026-09-06.** Clause dropped; the bullet now also documents `PatchSheetInterchangeability`, which had shipped undocumented. | Doc honesty | P1 | ✅ Done |
| CHANGELOG `[1.16.5]` - change "Removed … `EffectScalesWithDurabilities` blocks" to "disabled …", or actually delete the inert blocks (summary W3 / M3) | Doc honesty | P1 | Quick |
| ~~Delete the inert armor scaffolding from all 13 armor JSONs (summary M3)~~ **RETIRED 2026-09-06 as a non-issue.** Do NOT delete it: all 2709 vanilla `PassiveEffects` entries sampled carry `EffectScalesWithDurabilities`, so the disabled-in-place form IS the vanilla shape and removing it would deviate from vanilla for no gain. `ArmorValueDurabilitiesMultiplier` is already absent. The defect that mattered (an `EffectName` advertising scaling that could never happen) was fixed by rewording to the flat value. | Cleanup | - | ❌ Won't do |
| Strengthen the armor-repair warn-once so a total reflection failure is distinguishable from a genuine "0 changed" (summary W2 / code-quality G5) | Reliability | P2 | Quick |
| ~~`IronNailSmeltPatch` - rename to reflect it handles tin-ore smelting (summary M2)~~ **DONE 2026-09-06.** Renamed file + class to `TinOreSmeltPatch` via `git mv` (history follows); `Plugin.cs` registration and README hook list updated. | Maintainability | P3 | ✅ Done |
| ~~Confirm `bp_copper_brazier`'s dual-tab registration is intentional (summary M1)~~ **RETIRED 2026-09-06 as a false positive.** It is deliberate and documented: the README's own Crafting Tabs table lists Copper Brazier under both `Survival -> Fire` and `Construction -> Furniture`. | UX | - | ❌ Not a defect |

---

## Phase 2: Core Expansion

> The 1–2 most natural next content additions. Both are already specced in `Documentation/Ideas/`.

### Copper Ore Crusher / secondary smelting yield
**What**: a placed crusher (or drag-hammer CI on the stove/station) turning raw ore into a distinct
"crushed ore" item that smelts at a better nugget yield.
**Why**: named in this roadmap's Long-term Vision as the mod's next major addition; deepens the mining
loop the cave system already feeds.
**Requires**: crushed ore MUST be its own item with its OWN smelting entry - never a second recipe on
the same item (CLAUDE.md §Smelting Container Tag, the WDI 48-vs-12 copper bug). Decide standalone-
structure vs CI, and the yield delta, first.
**Complexity**: Complex.

### Metal Sieve
**What**: a tool/station for more efficient flour/powder output.
**Why**: extends the Tea Station's grind loop; a natural QoL rung.
**Requires**: decide standalone vs WDI-mill-output integration before building.
**Complexity**: Medium.

---

## Phase 3: Integration & Depth

> Both former blockers here are now cleared by the v1.16.5 work.

### Metal Alloying step (copper + tin → bronze nugget)
**What**: an explicit alloy recipe with copper-only lockouts, feeding the now-live SD4 sheet gate.
**Why**: the SD4 ladder now reaches the sheet and Bronze gear reads it, but there's no explicit alloy
step - this closes the ladder's bottom rung. *Previously downstream of the inert gate; unblocked.*
**Requires**: a concrete design pass; do not silently eat tin nuggets.
**Complexity**: Complex.

### Copper Shield (off-hand defensive piece)
**What**: a block-slot armor piece rounding out head/arms/legs/torso.
**Why**: fills the one missing armor zone. *Previously blocked on the dead armor-scaling multiplier;
that scaffolding was removed in v1.16.5, so a shield ships flat armor like the other 13 - nothing dead
to inherit.*
**Requires**: confirm a shield-appropriate `EquipmentTag`/armor zone exists first.
**Complexity**: Medium.

### Proximity warmth from lit placed heat sources
**What**: a passive Body-Temperature buff while standing near a lit ACT stove/brazier/bathtub.
**Why**: makes the three placed heat sources meaningfully better than a plain campfire and ties them
into one comfort system.
**Requires**: investigate whether vanilla exposes a proximity-warmth hook before designing
(`CSFF_Patterns.md` light + fuel).
**Complexity**: Medium.

---

## Phase 4: Polish

| Item | What | Complexity |
|------|------|------------|
| Bronze tier art | 5 PNGs to distinguish Bronze Helmet/Breastplate/Gauntlets/Greaves + Ore Chest from their Copper twins (currently reused sprites, disclosed in README - the honest interim state) | Medium |
| Cave-lantern utility | If vanilla exposes a cave-darkness penalty, gate comfortable mining on carrying a lit Metal Lantern - ties the lantern to the cave dungeon | Medium |
| ~~Description accuracy pass~~ **DONE 2026-09-06** | Ore Chest "rather than food" reworded to the real mechanic (food fits, but gains no spoilage protection). Cauldron "six cooking slots" also corrected to capacity-based phrasing (weight-limited 3000, not slot-limited). | ✅ Done |

---

## Long-term Vision

At v1.17+, ACT becomes the complete mid-game metallurgy layer: raw ore → crushed ore → alloyed nuggets
→ graded sheets → a full copper/bronze/iron gear tree, sourced end-to-end from its own cave system and
interchangeable with WDI's water-driven hardware. The Ore Crusher is the keystone that turns the cave
veins from a flat drop into a processing chain; the alloy step and Metal Sieve round out the "everything
has an upgrade path" identity.

**Potential major additions** (not yet justified - revisit after Phase 3):
- Copper Forge / smithing-upgrade station - a hotter/faster ACT forge as the prerequisite tier for a
  future Ironworks; fits the "own end-to-end supply chain" theme.
- Copper Still / distillation - highest-risk, no vanilla template; must ship standalone ACT value
  (purified water / concentrate) so it isn't dead content without H&F.

These have specs in `Documentation/Ideas/AdvancedCopperTools/IDEAS.md`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod AdvancedCopperTools` and update this roadmap |
| Game version update | Run `/update-mod-version`, refresh `lib/Assembly-CSharp.dll`, check CLAUDE.md EA notes, `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis AdvancedCopperTools` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo AdvancedCopperTools` and bump minor version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod AdvancedCopperTools         - full health check, updates .audit/
/critical-analysis AdvancedCopperTools - adversarial review (currently STALE - 2026-09-05, predates v1.16.5)
/repair-items AdvancedCopperTools      - auto-fix item JSON issues
/repair-blueprints AdvancedCopperTools - auto-fix blueprint JSON issues
/build-mod AdvancedCopperTools         - build Release DLL
/deploy-mods AdvancedCopperTools       - build + deploy to game
/update-mod-version AdvancedCopperTools <ver> - bump version in all 3 files
/export-to-repo AdvancedCopperTools    - push to public repo
```

---

## Plan Reconciliation Log

### 2026-09-07 - Audit_Remediation_Plan
- **Verdict:** Fully drained. F1 (MetalSheet SpecialDurability4 rewritten to the real DurabilityStat
  shape, Active with ValidValues [100..140], SD2/SD3 inactive), F2 (Bp_BronzeSheet.json supplies a
  bronze-grade sheet gated at Special4 {110,140}; Bp_MetalSheet gates {0,100}; all four Bronze armor
  blueprints gate the sheet at {110,140} while Copper armor carries no gate), and F3 (README art-reuse
  disclosure) all VERIFIED on disk. Shipped in v1.16.5. 0 open prompts; 0 promotable ideas.
- **Disposition:** ARCHIVE - moved to Documentation/Design/ACT_Audit_Remediation_As_Built.md (the plan
  holds the F1+F2 joint-fix sequencing rationale and the metal-type gate tracing, worth keeping as an
  as-built record). Sibling pack deleted. Executed and committed 9456ee523 (2026-09-07).
- **Pruned:** the entire plan doc (143 lines) and its Implementation Prompts pack (Prompts 1-3) left
  Documentation/Plans/; the AdvancedCopperTools scope folder is now gone. Nothing partially pruned -
  the plan was fully drained, not trimmed.
- **Evidence:** bogus-DurabilityStat-key grep `grep -rlE '"(DurabilityName|MaxDurability|StartDurability|Restricted|IsHidden)"'`
  across AdvancedCopperTools/**/*.json returned 0 hits; control = DurabilityStat-FieldNames.Tests.ps1
  red self-demo fires on the bogus shape (suite 6/6 green incl. red demos), proving the detector can
  hit. Special4-gate parse of every armor+sheet blueprint returned Bp_MetalSheet {0,100},
  Bp_BronzeSheet {110,140}, four Bronze armor {110,140}, six Copper armor NO gate; control = the same
  parse legitimately discriminates Copper (no gate) from Bronze (gated), and an earlier parse using the
  wrong field path returned "no gate" everywhere until corrected. Remainders confirmed homed:
  ROADMAP Phase 4 (optional 5-PNG Bronze/Ore-Chest art) and .audit/ideas.md row 3 (Copper Shield, now
  unblocked since v1.16.5 removed the dead armor-scaling scaffolding).
- **Verification debt:** T2.173 (tier A, the decisive bronze-sheet metal-type transfer, T2.91 depends
  on it), T2.91 (Bronze armor set), T2.155 (armor repair all tiers) - all present in
  .claude/playthrough-test-status.json, all `pending`.
- **Cross-mod tail paid same commit:** WDI 1.10.19 -> 1.10.20. WDI's Cast Metal Sheet had SD4
  activated at copper grade (100.0) on 2026-09-06 because ACT's PatchSheetInterchangeability registers
  it as an alternate wherever ACT's Copper Sheet is required - which as of v1.16.5 includes the Bronze
  gate, and the game skips a Special4 range test when the stat is inactive - but that behavior change
  shipped and deployed under the already-published 1.10.19 label with no changelog entry. Bumped and
  documented (WDI CHANGELOG [1.10.20]).

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
