# Roadmap: Community Mod Chest
Version at time of writing: 1.68.17
Date: 2026-09-05
Audit score: 7/10 (FIX BEFORE RELEASE - 0 CRITICAL, 2 CMC-owned design gaps, 20 warnings, 6 minor)
Re-derived 2026-09-05 after the mod's first-ever `/audit-environments` run. The report's headline
CRITICAL was re-graded to a Design Gap AND assigned to `CSFFModFramework` (DG-F1), so it is not
counted here - see Phase 0.

## Current State

**Theme**: A community-suggested content grab-bag that has grown a mature village-simulation layer on top of it - a named-resident roster with daily schedules, an Academy with seven courses and graduate perks, a four-guard Town Watch with a full Village Crime / Jail / Banishment enforcement loop, a five-chest merchant economy, tameable cats, seasonal road blocks, and a Village Reputation civic-growth meter. Aimed at players who want long-form settlement progression woven into the survival core.

**Content**: 81 item JSONs / 79 blueprints / 60 location cards (CT2 structures + exits + boards) / 7 interior CT4 environments / 3 CT10 improvements / 58 perks / 10 NPCAgents / 206 GameStats / 116 custom images / 66 C# files. Declarative surface: `BlueprintTabs.json`, `DropInjections.json`, `InjectImprovementInto.json`, `MapMod.json`, `TradingValues.json`, `WorldMap/MapNodes.json`, `EncounterGuards/`, `GameSourceModify/`, `ScriptableObject/` dialog tree.

**Stability**: 7/10. Zero CRITICAL, **two CMC-owned design gaps** (both from the 2026-09-05
environments audit: DG1 uncovered CT2 board seeds, DG2 un-floorable CMC interiors), zero
unreachable/dead-end items (415 produced / 119 consumed). Code quality 10/10 with 0 warnings. Clean build (0 errors / 0 warnings), full EN/CN localization parity (2273/2273 keys, 0 missing / 0 extra / 0 stale). WorldMap mechanical hygiene clean. The one open code finding from the last cycle (A10, hot-path diagnostic logging) was **resolved 2026-09-05**.

**Open work**: 0 genuine red retrospectives. 7 items Pending Verification / Open Plan, almost all awaiting in-game reconfirmation of already-shipped fixes:
- `cmc-village-hearth-fuel-not-refilling` - fix shipped 1.68.13. **The INDEX.md glyph is stale (still shows red, should be yellow)**; no negative reports across 1.68.14-1.68.17. Flagged in two prior consolidations and still uncorrected.
- `portal-hub-env-overlap-2026-08-24` - **the one item with a known live failure**: outbound-arrival fix confirmed in-game, but the instanced-interior return-trip fix (framework 2.25.14) FAILED in-game 2026-08-27. Owes a Session 5 root-cause pass.
- `cmc-village-conditional-drop-fixtures-missing`, `worldmap-clone-duplicate-terrain`, `guard-kill-despawn`, `CMC-HSP-compat` - shipped fixes awaiting in-game reconfirmation.
- `questinjector-blueprint-reset-risk` (framework-level, Open Plan) - CMC no longer uses QuestInjector; low current risk, root cause undiagnosed fleet-wide.
- Umbrella: `Documentation/Retrospectives/RETRO_CLOSURE_PLAN_2026-09-01` + its 13-prompt pack.

**Framework compliance**: Tier 2. Action interception routes through `Api.ActionRouter` (no direct `ActionRoutine` or coroutine patches anywhere in the tree - the only textual hit is a comment), encounter suppression is declarative via `EncounterGuards/*.json`, improvements inject via `InjectImprovementInto.json`, blueprints via `BlueprintTabs.json`. Reconcilers iterate every matching instance (the historical duplicate-instance desync class is handled). No `DropCollectionGuardPatch`, no unfiltered hot-path prefixes, no `ModLoaderVerison`/`ModEditorVersion`. AdvancedCopperTools is a documented HARD dependency (since 1.56.0); HerbsAndFungi / WaterDrivenInfrastructure / HomesteadPerks are soft with graceful degradation.

---

## Phase 0: Stabilize

> The environments audit changed this phase's shape. Two CMC-owned Design Gaps now lead it, and the
> single largest finding of that audit is **NOT CMC's to fix**: `AlwaysUpdateService` force-setting
> `AlwaysUpdate = true` on every mod CT2 card is a `CSFFModFramework` design gap (**DG-F1**, tracked
> in `CSFFModFramework/ROADMAP.md` Phase 0 and `CSFFModFramework/.audit/summary.md`). CMC's DG1 is a
> workaround for it and is blocked on that decision; DG2 is independent and buildable now. There is
> still no CRITICAL, and A10 has already cleared.
>
> **Ownership rule for this phase: do not attempt to fix `AlwaysUpdate` behaviour from the CMC tree.**
> The mechanism is confirmed, the consequence is global tracking rather than card loss, and the only
> CMC-side lever (`ConditionalDrops` + `ForceStay`) does not address the cause.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **DG1: 28 CMC CT2 cards are seeded onto environment boards through paths that never restore `AlwaysUpdate = false`.** Only 9 CMC cards ride the `ConditionalDrops` + `ForceStay: true` correction; the other 28 keep the framework-forced value - 23 interior fixtures in the 7 hand-authored CT4 interiors' `DefaultEnvCardDrops` (7 Exits, 7 Village Hall notice boards, 5 Copper Chests, Lectern, Inn Counter, Jail Door/Bed/RationTray) plus 5 SealableGate challenge cards seeded via `SeedOnEnvUIDs`. `CardCloneService.NeutralizeFollowerDrops` runs for CLONE envs only; a hand-authored CT4 gets no equivalent pass. **OWNERSHIP: the root cause is framework-side (DG-F1) - the only CMC-side lever is the declarative `ForceStay` route, which is a WORKAROUND, not a fix. Sequence this AFTER the framework's DG-F1 decision lands**, because a scoping change there may make it unnecessary. | Design Gap (blocked on framework) | **P0 (blocked)** | Medium |
| **DG2: no CMC-authored interior can host CMC's own Stone Tile Floor improvement.** `cmcimpstonetilefloor` is injected into 9 vanilla CT8 interiors (all 11 `InjectImprovementInto.json` targets resolve), but zero CMC interiors are targets and none of the 7 declares `EnvironmentImprovements` - a player who builds out the village cannot floor any building the mod itself adds. Purely declarative: add the 7 interior CT8 UIDs to `InjectImprovementInto.json`. **Fully CMC-owned and buildable now** - independent of DG-F1 and DG1. | Design Gap | **P0** | Quick |
| ~~Run `/audit-environments Community_Mod_Chest`~~ - **DONE 2026-09-05.** Output: `.audit/environments-report.md`. All six of the skill's named critical checks PASSED (0 `AlwaysUpdate:true` on CT4/CT8, 0 underscore env UIDs, all 12 clone templates non-instanced, no travel DA carries `RequiredStatValues`, 7/7 CT4-CT8 pairs, no non-CT8 env-watch spawning), plus 5/5 SealableGate prefix matches across 9 clearing actions, 0 coord collisions, 0 clone-name collisions and 0 missing `*WarpType` companions. It produced DG1/DG2 above, W16-W20, M3-M6, and the framework-owned DG-F1. | Audit coverage gap | **DONE** | - |
| Add `CardName.LocalizationKey` to `CardData/Location/CMC_MarketStall.json` - the `CMC_MarketStall_CardName` rows **already exist in both `SimpEn.csv` and `SimpCn.csv`** and are stranded, so Chinese players see an untranslated name. Closes the mod's only localization hole. | Localization fix | P0 | Quick |
| Correct the `cmc-village-hearth-fuel-not-refilling` glyph in `Documentation/Retrospectives/INDEX.md` from red to yellow - the retro file itself documents the 1.68.13 fix. One character; flagged twice and still open. | Doc accuracy | P0 | Quick |
| Recover the truncated blocking reason for playtest **T2.135 (Town Wood Pile)** and either run or retire it - it is the sole cause of the mod's only BROKEN feature-map grade, and it is a blocked test, not a code defect. | Evidence gap | P0 | Quick |
| Spot-check `CardData/Location/CMC_JailCellBed.json`'s nested `ParentObjectID` (a GUID) against its own `UniqueID` (`cmcJailCellBed`) - present-and-mismatched is a real bug where omitted is convention. | Structure fix | P1 | Quick |
| Remove the stray `StarsCost` field from `CharacterPerk/Perk_Claws.json:203`. | Schema hygiene | P1 | Quick |

---

## Phase 1: Foundation

> Table stakes. Most of this is already done - what remains is refreshing artifacts that have gone stale under a fast release cadence.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Refresh the 5 per-domain audit reports (`/audit-items`, `/audit-blueprints`, `/audit-structures`, `/audit-images`, `/audit-perks`) - all dated 2026-08-24, all predate the 2026-09-05 source commit and are UNTRUSTED per CLAUDE.md. Individual findings were re-checked on disk this consolidation, but the reports themselves were not re-run. | Audit hygiene | P1 | Medium |
| Regenerate `feature-map.md` for 1.68.17 - it is written for 1.68.16 and carries its own regenerate-after-version-bump warning. | Audit hygiene | P1 | Medium |
| Re-run `/critical-analysis Community_Mod_Chest` against 1.68.17 - the current report is dated 2026-09-01 and two source commits have landed since (the ACT Copper Sheet lock removal and the heather Incense Burner), neither of which it has reviewed. | Audit hygiene | P1 | Medium |
| Version hygiene: **already clean** (ModInfo.json / Plugin.cs / README.md all read 1.68.17; bin/Release sync shows 0 missing, 0 out-of-date, 0 orphaned). No action. | Verified | - | - |
| Localization baseline: **already clean** (2273/2273 EN/CN parity, no header row, no duplicate keys) apart from the Market Stall key in Phase 0. | Verified | - | - |

---

## Phase 2: Core Expansion

> The three highest-value content additions, ordered by how much existing content they activate.

### The Apothecary Remedy Line
**What**: An Allergy Tonic, a Stomach Settler, and a Clotting Salve produced at the Apothecary Cabin from H&F herbs plus CMC's own Herb Paste, each countering a specific shipped drawback trait.
**Why**: This is the single strongest unbuilt idea in the mod. CMC ships seven drawback traits (`Bleeder`, `DeadlyDisease`, `SeasonalAllergies`, `WeakStomach`, `BadKidneys`, `Insomniac`, `Leper`) and a full Apothecary NPC with a quest chain - and `ls CardData/Item/` finds **no tonic, salve, settler, remedy or antidote of any kind**. Every one of those traits is currently a one-way debuff with no counter-play. It simultaneously gives the Apothecary a reason to exist past her quest chain and supplies the missing sink for the CMC-to-HerbsAndFungi herbal pairing.
**Requires**: none (the Medicine course's Herb Poultice + Tincture is the pattern to copy).
**Complexity**: Medium

### The DEFERRED_ITEMS.md Residue
**What**: Club, Sling + Sling Stones, Bone Helmet, Grinding Slab Kit, Stone Block, Stone Tile (singular), Cloth Mask, Long Johns, and the Hunting Stand system.
**Why**: Fully specced in `Documentation/Ideas/Community_Mod_Chest/DEFERRED_ITEMS.md` with UniqueIDs already confirmed safe to reuse. This is the cheapest content-per-token work left in the mod - no design decisions, no new mechanisms, pure JSON on proven patterns.
**Requires**: check each against vanilla before building. The bone-tier tools in this same family were correctly dropped because vanilla already ships `BoneNeedle`/`HookBone`, and the same trap applies here.
**Complexity**: Medium (but highly parallelizable)

### Voluntary Redemption / Making-Amends Track
**What**: A real path back to Clean without a cell or a fight - a Village Hall reparations fine scaled to the crime band, or a restitution errand that walks `cmcStatVillageCrime` down.
**Why**: The crime loop is the mod's most elaborate system (guard pursuit, gauntlet, jail sentencing, hidden-tunnel escape, banishment) and it is almost entirely punitive. The only constructive path shipped is the single "Make it right" Miller restitution seed. A player who commits one theft has no way to make good on it short of serving time.
**Requires**: a design decision - fine-only (a CI calling `ReduceCrime`) versus an errand chain. If an errand chain, it **must** use the clamped-marker pattern (`reference_npc_errand_clamped_marker_pattern`), never an unclamped counter, or the errand softlocks itself over time.
**Complexity**: Complex

---

## Phase 3: Integration & Depth

### Merchant purse-tier and Reputation bucket readouts
**What**: Publish a coarse `cmcStat<Resident>PurseTier` (lean / modest / flush) on the weekly accrual tick, and publish at least one of `VillageReputationPatch.cs`'s internally-computed `ConstructionBucketMax` / `QuestBucketMax` values as its own hidden stat.
**Why**: Both systems already compute the information and then throw it away. Sales bounce when a chest cannot afford an item, and a thief picks a target by purse size, but none of it is legible until a sale fails. Reputation reads identically whether the player built everything and knows nobody, or knows everybody and built nothing. Neither `ls GameStat/` search finds a `*Purse*` or `*Bucket*` stat today.
**Requires**: a decision on where the accrual patch publishes the tier (chest wealth is inventory, not a stat).
**Complexity**: Medium

### Gift-preference friendship for the Miller and Weaver
**What**: A gift-preference drag interaction mirroring the Professor's - grain/flour for the Miller, cloth/dye for the Weaver.
**Why**: Verified 2026-09-05: `Agent_Apothecary.json`, `Agent_InnKeeper.json` and `Agent_Professor.json` carry gift handling; the Miller and Weaver carry none, so their Trust moves only through scripted errands. Two of the five named residents cannot be befriended at all outside a quest.
**Requires**: the Professor's JSON `DragAndDropAction` idiom (`GivenCardChanges.ModType:3`); ships fastest as pure JSON. Keep gifts a slow secondary track, never an errand bypass.
**Complexity**: Quick

### A summer route hazard (optional - close the row if it does not fit the fiction)
**What**: A fourth seasonal `SealableGates` entry for summer.
**Why**: `WorldMap/MapNodes.json` now carries `SealTrigger` `"Season"` entries for Autumn (Deadfall), Winter (three Snow Drifts) and Spring (Clay Shoal flood) - three live templates to copy. Summer is the only season with no route pressure.
**Requires**: **every** clearing action on the challenge card must share the gate's `ClearActionKeyPrefix` (CLAUDE.md - a mismatched by-hand action softlocks the road silently). Enforced by `MapNodes-Schema.Tests.ps1`.
**Complexity**: Quick

### WaterDrivenInfrastructure and SkillSpeedBoost hooks
**What**: Extend the Academy graduate perks - Architecture/Metallurgy already unlock WDI stations; an SSB-style learning-speed bonus inside a course's own skill domain is the natural next tier.
**Why**: Deepens the Academy's payoff without adding courses.
**Requires**: confirm no domain overlap first, and read SSB's single-composition-point guard rule before wiring - a bonus missing from that early-out guard is silently dead when it is the only one enabled.
**Complexity**: Medium

> **Standing constraint, learned the hard way on 2026-09-04.** CMC may never again apply the C# blueprint-lock idiom to another mod's material. The Metallurgy course locked ACT's `advanced_copper_tools_bp_metal_sheet` and stranded 21 downstream ACT blueprints for every non-graduate, with zero in-game explanation. Fixed in 1.68.17 by replacing the lock with a grant. Lock END PRODUCTS only; **GRANT** materials. Enforced by `Development_Tools/Tests/Academy-CourseGating.Tests.ps1`.

---

## Phase 4: Polish

| Item | What | Complexity |
|------|------|------------|
| Distinct finished-potion sprite | `CMC_ApothecaryHealingMixture.json` and `CMC_ApothecaryHealingPotion.json` both point at `CMC_Alchemist_Potions`, so the intermediate and the finished product are visually identical on the board | Quick |
| Distinct Market Stall dressed art | `CMC_MarketStall.png` and `CMC_MarketStallDressed.png` are **byte-identical** (md5 `589df0ef...`). The self-transform mechanism works; the art does not yet differ, so the awning toggle is invisible | Quick |
| Retire the last two diagnostic patches | `GuardCombatDiagnosticPatch.cs` and `CompanionFollowDiagnostics.cs` - delete or demote **only after** `guard-kill-despawn` and companion-follow are log-confirmed in-game. Grep `Documentation/Retrospectives/` for a diagnostic's tag before touching it | Quick |
| Blueprint schema-completeness sweep | ~51 blueprint files omit optional keys (`CardImage` placeholder and similar). Cosmetic, non-breaking; batch it with the Phase 1 `/audit-blueprints` refresh | Quick |
| `CMC_AshBoarTrail.json` placement | `CardType: 2` filed under `CardData/Item/`. Loads and works; move it to `CardData/Location/` only alongside other structure work, and only after confirming nothing keys on the path | Quick |
| Perk schema consistency | 17 compact `Pk_*.json` files omit required-field keys when the backing array is empty, deviating from every other perk's explicit-`false` style | Quick |

---

## Long-term Vision

> Where this mod should be at v2.0.

Community Mod Chest has outgrown its own name. What began as a grab-bag of community item requests is now the fleet's most elaborate settlement simulation: a five-resident village with schedules and quest chains, an Academy with seven degrees, a criminal-justice system with sentencing and escape, a merchant economy with per-NPC wealth, and a civic reputation meter that tracks both what you built and who you know. At v2.0 the mod's identity should stop apologizing for the grab-bag and lean fully into being **the village layer for Card Survival** - the thing a player installs when they want somewhere to belong rather than just more recipes.

The two structural gaps standing between here and that identity are both about **reciprocity**. First, the village currently reacts to your crimes but not to your virtue: there is a punishment ladder and no redemption ladder, and residents treat a Wanted or Banished player exactly like a model citizen. Second, the village gives the player systems but very little that gives back - the drawback traits have no remedies, the residents have no gifts, and the Reputation meter's two halves are computed and discarded. Closing those two loops would turn a set of impressive mechanisms into a place that responds.

The third and largest opportunity is **evidence, not content**. This mod carries 93 feature groups of which only 19 are human-verified, ~916 LOC of Achievement Board code nobody has ever watched run, a potential permadeath fix (the Bleeder Blood Pressure floor) with zero in-game confirmation, and a self-described release blocker (T4.21, the jail safety net) verified on paper only. At this scale, a single dedicated verification playthrough is worth more than a whole content phase.

**Potential major additions** (not yet justified - revisit after Phase 3):
- **Generic NPC archetypes** (Shopkeeper, Hirable Hunter, a charming companion, an Elder with quests) - specced in `VILLAGE_AREA.md` and deliberately not covered by the named-resident roster. CMC now has a mature NPCAgent chassis, so the build cost has fallen sharply since the spec was written.
- **Reviving the benched Wisp quest-giver** (`Documentation/Ideas/Community_Mod_Chest/Wisp_and_NPCs/`) - its original blocker was "wait for the vanilla NPC rework," which is overtaken by events now that CMC built its own chassis. Its hard-won lessons (non-instanced clone envs; never attach a vanilla QuestLog to the player save) remain load-bearing and must survive any revival.
- **Named outfit presets** - the one-motion Dress/Undress mechanism already covers both the Clothes Rack and the Wardrobe; the preset-*storage* half needs real design, not routing generalization.
- **A sleep-axis scented incense** - deliberately deferred, not merely unbuilt. The only hook is the `SleepClock` stat, and hanging a passive `RateModifier` on the game's own sleep-timing stat is the vanilla-mechanics risk class CLAUDE.md warns about. Needs a discrete sleep-quality mechanism identified first.

These live in `Documentation/Ideas/Community_Mod_Chest/`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod Community_Mod_Chest` and update this roadmap |
| Game version update | Run `/update-mod-version`, refresh `lib/Assembly-CSharp-nstrip.dll` (CMC references `HerbsAndFungi/lib/Assembly-CSharp-nstrip.dll` by relative path - regenerating it needs the external NStrip install), re-run `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis Community_Mod_Chest` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo Community_Mod_Chest`, bump minor version, then repackage the suite and deploy `Mod_Update_Manager` **last** |
| Any change touching a sibling mod's content | Re-read the Phase 3 standing constraint above and run `Development_Tools/Tests/Academy-CourseGating.Tests.ps1` |
| Whenever a play session happens | Record results with `/playthrough-test-plan record` - this mod's largest deficit is verification, not code |

---

## Skill Cheatsheet for This Mod

```
/audit-mod Community_Mod_Chest             - full health check, updates .audit/
/audit-environments Community_Mod_Chest    - THE missing audit; run this first
/critical-analysis Community_Mod_Chest     - adversarial review
/repair-items Community_Mod_Chest          - auto-fix item JSON issues
/repair-structures Community_Mod_Chest     - auto-fix structure JSON issues
/build-mod Community_Mod_Chest             - build Release DLL
/deploy-mods Community_Mod_Chest           - build + deploy to game
/update-mod-version Community_Mod_Chest <ver> - bump version in all 3 files
/export-to-repo Community_Mod_Chest        - push to public repo
/playthrough-test-plan record              - log in-game verification results
```

---

## 2026-09-05 environments-audit addendum

Two testing notes from `.audit/environments-report.md` that belong in the playthrough plan rather than
in a phase table:

1. **All 7 CMC interiors are `InstancedEnvironment: false`** (verified in every CT4 file), so they
   **cannot** serve as the instanced-interior fixture for `portal-hub-env-overlap-2026-08-24`'s T2.146
   repro. A tester who reaches for the Village Inn or the Academy - the most convenient interiors in the
   fleet - gets a **false pass**. The retest needs a genuinely instanced vanilla interior (Cabin /
   Cellar / Coop / Enclosure / attic / mine). Write this into the T2.146 item text.
2. **All 12 clone boards and all 7 interior boards in the current save are duplicate-free**, which
   narrows `worldmap-clone-duplicate-terrain`'s Open Unknown #3. It rules out a pre-existing
   contaminated baseline in the author's slot; the save is too young to be a negative result.

Also worth carrying: `cmc-village-conditional-drop-fixtures-missing` **Open Unknown #2 is now answered
statically** (root cause is `CSFFModFramework/Loading/AlwaysUpdateService.cs:72`; all three candidate
hypotheses ruled out). The retro is **deliberately still open** - closure needs `/resolve-retro`'s
runtime-evidence gate, and the freshest local save cannot corroborate the T1.59 pass because its
`VisitedEnvironments` lists only `cmcEnvVillagePath`.

## Plan Reconciliation Log

> Written by `/cleanup-plans` Step 4b. Append-only: never reword, reorder or delete an entry.
> `/roadmap` Step 7 preserves this section verbatim when it overwrites the rest of this file.

### 2026-09-07 - Audit_Remediation_Plan
- **Verdict:** 2 fix rows, 0 feature rows. **F2 BUILT this pass** in v1.68.21; **F1 confirmed already
  closed** (re-derived, not inherited); the 4 former feature rows N1-N4 had already been withdrawn to
  the plan's third-state table on 2026-09-07 with their recorded rejection reasons, and 0 rows
  remained buildable after F2 landed. The 1-prompt pack is drained.
- **Disposition:** DELETE from `Documentation/Plans/Community_Mod_Chest/` and ARCHIVE to
  `Documentation/Design/CMC_Audit_Remediation_As_Built.md` (plan verbatim + its prompt pack as an
  appendix), per `Documentation/Plans/README.md` step 4. Nothing was erased: every withdrawn row's
  rejection reason travels with the archived doc, and the four N1-N4 reasons were already stamped
  into `Documentation/Ideas/Community_Mod_Chest/DEFERRED_ITEMS.md` by the 2026-09-07 pass.
- **Pruned:** the whole plan doc and the whole 1-prompt pack, both now archived rather than dropped.
- **Evidence:** F2, as built. `CardData/Location/` enumerated by CardType+UniqueID: exactly **7 CT8
  cards**, matching F2's list one-for-one, and `grep -c EnvironmentImprovements` returned **0** on all
  7; a `grep -rl '"CardType": 8'` across the rest of `CardData/` returned **0**, so the set of 7 is
  complete and the plan's warning about `cmcJailCellLocation` not being named `*InteriorLocation` is
  real. `InjectImprovementInto.json` held **11** entries (2 other improvements + 9
  `cmcimpstonetilefloor`); those 9 reverse-mapped through
  `Documentation/GameData/CSFF-JsonData_Current/UniqueIDScriptableGUID/CardData.json` to the vanilla
  Cabin/MudHut family only (Cabin, its StartingPoint / CabinRoom / Attic construction cards, MudHut,
  its Left/Right expansions and its Room / StartingPoint cards), confirming zero CMC targets. Now
  **18** entries; the diff is append-only (`git diff --numstat` = 8/1, the single deletion being the
  comma added to the previous last line) and the 11 originals were asserted byte-identical after the
  write. `CSFFModFramework/Injection/ImprovementInjector.cs` read in full: `TargetEnvUID` resolves
  through a UniqueID lookup over `allData` + `GameRegistry.AllUniqueObjects`, so a plain mod UID is
  correct and a GUID hash would be wrong here; injection is idempotent and warns on an unresolved
  UID. All 7 targets carry `AlwaysUpdate: false`, so none is in the CT4/CT8 travel-softlock shape.
  Release build clean (0 warnings, 0 errors); `bin/Release/InjectImprovementInto.json` parsed and
  compared equal to source (18/18); deployed copy at
  `BepInEx/plugins/Community_Mod_Chest/InjectImprovementInto.json` parsed at 18 entries.
- **Evidence, F1 (closed; re-derived rather than inherited):** the plan asserted DG-F1 was resolved
  upstream in framework 2.25.28. Confirmed by reading
  `CSFFModFramework/Loading/AlwaysUpdateService.cs`: `EnableAll` now carries a `skippedInert` branch
  and the header comment "For all OTHER mod cards we want AlwaysUpdate = true, EXCEPT inert CT2
  fixtures (DG-F1)", with per-cause warn-once fallbacks to the pre-DG-F1 behaviour on a reflection
  miss. The force-set the row was written against no longer runs for inert CT2 cards, so F2 was the
  only buildable row and F1 needed no CMC-side change.
- **Docs-honesty, both directions:** the fix made two existing claims stale in the UNDER-claiming
  direction, which the F2 row did not anticipate. `Imp_StoneTileFloor.json` `CardDescription` and
  `README.md:44` both said the floor could be laid "in cabins and mud huts"; both now name the seven
  village interiors, and the CSV rows were updated in the same pass (`SimpEn.csv` line 529,
  `SimpCn.csv` line 589, English columns byte-identical, Chinese retranslated).
  `Check-LocalizationParity.ps1` reports CMC CLEAN at 2273/2273. `ModInfo.json` `Description` needed
  no change: it never mentioned the Stone Tile Floor (`grep -c -i tile` = 0).
- **Verification debt:** **T2.187** (`cmc_stone_tile_floor_village_interiors`, tier B, `pending`) in
  `.claude/playthrough-test-status.json`, carrying both halves of the acceptance check: the floor
  actually appearing and building in at least two CMC interiors including `cmcJailCellLocation`, and
  no `ImprovementInjector: ... not found in registry` warning for any of the 7 UIDs at load.
  Plan-lifecycle gate 2 is therefore satisfied. F1's own residual in-game check remains **T2.185**
  (framework-owned, unchanged by this pass).
- **Blockers a future implementer must not step on:** the archived doc's third-state table is the
  record for 14 candidates that must NOT be re-promoted, including the four withdrawn feature rows
  (Sling, Bone Helmet, Grinding Slab Kit, Stone Block). Bone Helmet in particular shipped in 1.16.0
  and was deliberately removed in `e9fabc27d` with **no reason recorded anywhere**; re-adding it
  re-litigates a lost decision and needs that reason established first.

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

### 2026-09-12 - Release_Hardening_Plan (Phases 1-3 of 4)

- **Verdict:** 8 Phase 1 rows, 2 Phase 2 rows, 2 Phase 3 rows, 5 Phase 4 rows. **Phases 1-3 are
  complete and pruned out of the plan doc; Phase 4 remains and is BLOCKED on a human reproduction.**
  Phase 1: 4 rows BUILT in CMC 1.68.24 (H2, H3, H4, H6), 3 rows REFUTED and deliberately not built
  (H1/W3, H5/W18, H7/W8+M1), 1 row closed by owner decision (H8/M2). Phase 2: both rows landed, the
  audit re-scored 8/10 -> 9/10. Phase 3: both rows closed as "precondition never met, diagnostic
  retained", which this plan's own Done gate allows; BOTH diagnostics stay in the tree. Phase 4: 0
  of 5 rows buildable today, so the plan doc stays in `Documentation/Plans/`.
- **Disposition:** KEEP (pruned). Not DELETE and not ARCHIVE: A2-A5 are genuine unbuilt code work,
  so `Documentation/Plans/` still answers "what needs building?" truthfully for this file. Phases
  1-3 were deleted from the doc per `Documentation/Plans/README.md`'s no-finished-work-history rule;
  A1 was collapsed to a one-line pointer at its tracker row, per the same README's section rule.
- **Pruned:** the whole of Phases 1, 2 and 3 plus their ground rules, and the Phase 4 A1
  reproduction procedure (now `T2.236`). Nothing was erased: every refutation's evidence is written
  into `Community_Mod_Chest/.audit/summary.md` against its own finding row and into the CMC 1.68.24
  CHANGELOG entry, and both Phase 3 preconditions are written into their tracker rows in full.
- **Verification debt filed:** `T2.236` (Achievement Board reproduction on a dev build, carrying the
  whole static clearance so nobody re-checks it) and `T2.237` (companion-follow observation window,
  carrying the correction to BOTH mistaken readings of the live log). Tracker went 151 -> 153 rows,
  `git diff --numstat` +18/-0.
- **Evidence:** Phase 1 (built). `CMC_AshBoarTrail.json` `git mv`'d `CardData/Item/` ->
  `CardData/Location/`; it is `CardType: 2`, and `CSFFModFramework/Loading/JsonDataLoader.cs`
  `DirToTypeName` was read in full to confirm it keys on the top-level `CardData` folder only, so
  the subfolder is organisational and the unchanged UniqueID `cmcAshBoarTrail` means saves are
  unaffected. `CMC_TownWoodPile.json` given an explicit `"AlwaysUpdate": false`: its 4 siblings were
  identified by reading the `cmcEnvVillage` node's `ConditionalDrops` list in
  `WorldMap/MapNodes.json` (`cmcInn`, `cmcAcademy`, `cmcJail` and one GUID entry, all `ForceStay:
  true`), and the 3 named ones were confirmed to carry `AlwaysUpdate=False` already;
  `CSFFModFramework/Injection/ConditionalDropService.cs` `RegisterNode` was read to confirm it
  already forced that value, so the change is provably zero-runtime. Two `CardDescription` blocks
  added with one `SimpEn.csv` and one `SimpCn.csv` row each in the same commit;
  `Check-LocalizationParity.ps1` CLEAN at **2280/2280** across the fleet. 20 `Pk_*.json` files given
  their missing keys, after reading `CSFFModFramework/Data/WarpResolver.cs:233` to confirm the
  warpType-3 path skips an already-populated array, and after confirming the only 2 perks with
  populated warp arrays were not missing those keys. Every JSON write ran through a guarded script
  that asserted each anchor matched exactly once, re-parsed the result, compared the parsed object
  against the pre-image field by field, and asserted the line-ending delta; 3 files whose byte
  format could not be reproduced were refused by that guard and rewritten by targeted textual
  insert instead. Build 0 warnings 0 errors; commit `8d5656afa`, 60 files, +141/-43, staged numstat
  compared against the commit's own and matching exactly.
- **Evidence, Phase 1 (refuted, and each would have shipped a regression or pure churn).** W3: read
  `.decomp/CharacterPerk.cs` and found `public int StarsCost` at line 11, plus `IsPurchasable`
  returning `StarsCost > 0` when `SunsCost` and `MoonsCost` are both 0, which is exactly
  `Perk_Claws` (0/0/1). The row asked for the key to be DELETED; that would have made the trait
  unpurchasable. Only the key's alphabetical position was actually wrong. W18: traced the single
  consumer `CSFFModFramework/Api/GameQuery.cs:306-336` to `EnvID.MainEnvCard`, read
  `.decomp/EnvID.cs` to confirm that field, then proved it is the CT4 by finding `CMC_Inn.json`'s
  travel DismantleAction dropping `cmcInnInterior` (CT4), and confirmed all 7 CT4 partners already
  carry `tag_EnvIndoors` while `EncounterGuards/CMC_InteriorsNoWildlife.json` keys on those same 7
  CT4 UIDs by name rather than by tag. A sweep of all 14 mods found **0** cards of any CardType
  carrying an environment tag on a CT8. The vanilla JSON export was checked first and discarded as
  evidence: it serialises no resolved tags at all (0 hits across 2841 files), the same misleading-
  precedent trap CLAUDE.md records for `InventorySlots`. W8/M1: swept all **79** `Bp_*.json` for
  the classes that actually have a runtime consequence and found **0** over the `BuildingDaytimeCost`
  cap, **0** `*WarpData` missing its `*WarpType` sibling, **0** `(0,0)` result quantities, and all
  **38** files lacking a `CardImage` key carrying a resolvable `CardImageWarpData` instead.
- **Evidence, Phase 2 (re-score).** The red-retrospective term went to zero. Verified by PARSING the
  status column of `Documentation/Retrospectives/INDEX.md` rather than grepping the glyph: 0 red, 13
  yellow, 9 graduated. A raw grep returns 1 red, and that single match is a prose mention inside
  `cmc-village-hearth-fuel-not-refilling`'s own row describing a status corrected on 2026-09-05,
  which is exactly the false-positive shape CLAUDE.md's measurement rule warns about. This was
  already predicted in summary.md's own Retrospective Status section on 2026-09-09.
  Score = 10 - 0 (red retro) - 0 (CMC-owned DESIGN GAP) - 1 (5 open WARNINGs, flat threshold) =
  **9/10**. The open-row count was then verified by classifying all 26 W/M rows and asserting the
  classified count equalled the input count, so a silently-unparsed row could not inflate the
  result: 21 closed, 5 open (`W10`, `W12`, `W13`, `W14`, `W15`), 0 open minors. Each of the 5
  survivors carries a written reason it stays open, which is R2's stated alternative acceptance;
  4 of the 5 are human-verification debt and the fifth is structural.
- **Evidence, Phase 3 (both preconditions unmet, both diagnostics retained).** D2: the retro
  `cmc-guard-combat-no-damage` reads yellow Pending Verification in INDEX.md and needs `T4.35`,
  which is a human reading a guard fight's `Approach <EncounterName>` line. Untouched. D1: this row
  named a `Player-prev.log` line as candidate confirming evidence. Read the LIVE
  `BepInEx/LogOutput.log` (the repo-root copy is a stale 539,086-byte partial against the live
  565,839 bytes, per CLAUDE.md's deploy-verify rule) and found BOTH obvious readings wrong. The
  "(caught up)" line at 3245 fires while the player env is `2b19b942a09fdd148a43798e942a74eb`, a
  VANILLA env, so it proves nothing about CMC nodes. The later "NOT in the player's env" line at
  3565 looks like proof the bug is live, but the session ended 358 lines later with ZERO further env
  changes, and the diagnostic logs only on a stuck<->caught-up transition (15s poll, dedup key
  "same"/"diff", confirmed by reading `CompanionFollowDiagnostics.Run`), so no second sample was
  ever taken. The genuinely new result is in that dump's move-probe:
  `MoveDestination=MoveToPlayer mapPath.IsValid=True StepCount=2 End=cmcEnvVillagePath`, i.e. A*
  into a CMC node RESOLVES and the `MapDict` root cause this diagnostic was written for IS fixed.
  What blocks `PartnerDuty_Follow` now is "Conditions are not valid", and that duty has exactly ONE
  non-default condition, `InBackground`, confirmed from the vanilla NPCDuty export;
  `.decomp/GeneralCondition.cs:237` force-fails any duty carrying `InBackground`/`NotInBackground`
  while `GameManager.IsTravelling`, which is a plausible benign explanation for a sample taken at
  the moment of arrival. All of this is written into `T2.237` so the next reader does not repeat
  either mistake.
- **Evidence, Phase 4 (not built, and why no fix was attempted).** The whole static surface was
  cleared and came back clean: 22 DAs forming 11 locked/earned pairs, each with `AlwaysShow: true`,
  a unique `ActionName.DefaultText` and a unique `LocalizationKey` (so the
  `InspectionPopup._AlreadyDisplayedActions` same-name dedupe is ruled out); 44 CSV rows present in
  BOTH `SimpEn.csv` and `SimpCn.csv` with 22 distinct English `_Name` values and 0 duplicates; all
  **79** `cmcStatAch*` UniqueIDs referenced by the card and by the three patches resolving to
  shipped `GameStat/` files with 0 typos and 0 orphans; and the hide mechanism correct by default,
  traced through `.decomp/DismantleActionButton.cs` (hides when `StatsAreCorrect` is false AND
  `MissingStats` is empty) and `.decomp/CardAction.cs:1051` `StatsAreCorrect` (leaves `MissingStats`
  empty when neither `HideAllWhenNotMet` nor `NotifyWhenNotMet` is set, which is this card's case).
  `VillageHallBoardsPatch` was found ALREADY enabled in `Plugin.cs` and already owning
  `cmcBoardAchievements` including the summary line and 6 progress counters. Since no static defect
  exists, shipping a fix would be fixing an unconfirmed hypothesis, which CLAUDE.md Debugging
  Discipline rule 2 forbids, so the reproduction was filed as `T2.236` with the clearance attached
  and nothing was changed. Re-confirmed the board is still unadvertised (0 "achievement" mentions in
  `ModInfo.json` and `README.md`), so the bench remains docs-honest.
- **Incidental fix, outside the plan.** `Community_Mod_Chest/Features.json` did not parse: two
  unescaped `"` pairs inside one string value on line 775. Bisected to commit `12ed4991f` (CMC
  1.68.20, 2026-09-09); it is dev-only metadata, absent from the csproj, from `bin/Release` and from
  the deployed folder, so no player was ever affected. Repaired by escaping exactly 4 characters on
  that one line, asserted to change only line 775 and to leave the file parsing.

### 2026-09-13 - Release_Hardening_Plan (Phase 4, plan complete)

- **Verdict:** Phase 4's 5 rows, 0 left buildable, so the plan is Done. A1 (human reproduction) SUPERSEDED rather
  than performed: the diagnosis it was waiting for was reached from shipped code and data, so `T2.236` was
  re-purposed from a reproduction request into the acceptance check. A2 BUILT as the retro
  `cmc-achievement-board-presentation`. A3 BUILT (layout fix, three patches re-enabled, Inn spawn restored). A4
  BUILT (`ModInfo.json`, `README.md` and `CHANGELOG.md` claim the board). A5 BUILT (tracker rows below).
- **Disposition:** ARCHIVE to `Documentation/Design/CMC_Release_Hardening_As_Built.md` (the plan text unchanged,
  below an as-built header) and remove it from `Documentation/Plans/Community_Mod_Chest/`, per
  `Documentation/Plans/README.md` step 4. Version: folded into the unpublished 1.68.24 instead of the plan's
  1.69.0, because `.claude/mod-publish-status.json` records CMC's last publish at `480bb9e50`, which carries
  1.68.23, and MUM's last publish carries 2.1.37, not the 2.1.39 that embeds 1.68.24.
- **Pruned:** the whole plan doc (Phase 4, its not-in-this-plan list and its Done gate), archived rather than
  dropped.
- **Verification debt filed:** `T2.236` (fresh-save board shows the summary and all eleven lines, with the
  objective `truncated=False` log line), `T2.168` (the 11 detectors; re-pointed from an r33 SKIP that ran on the
  benched build, status back to pending), and new `T2.238` (fresh-save presence, old-save backfill and the popup
  restore check, re-arming `T2.170` and `T2.169`, which stay confirmed as history). `T2.237`'s source was
  repointed at the archived doc.
- **Evidence:** Root cause. `VillageHallBoardsPatch.UpdateActionsVisibility` hides `DismantleOptionsParent` for
  every board, so entries exist only as prose in `InspectionPopup.DescriptionText`; `ApplyDescriptionSizing` sets
  that text to `TextOverflowModes.Truncate` with a 60% auto-size floor, and `.decomp/InspectionPopup.cs` has no
  `ScrollRect` on it (its only one is `InventoryScrollView`). Both came in with `5a77611a6` (2026-07-29), before
  the board. Simulating every board's fresh-save output from its JSON and the game's own range test gave the
  achievement board 19 paragraphs / 1047 characters against 4-8 paragraphs for the other seven (largest Weaver,
  7 / 698), with the summary printed last. Of the candidates, only a tail cut removes several entries AND the
  summary. The plan's A1 "hide mechanism" row had traced `DismantleActionButton.Setup`, which never runs for a
  board.
- **Evidence, counter-claim checked.** `T2.170` was recorded PASS in r33 (2026-09-06) as "board present, 11 locked
  lines, 0 of 11 counter" on a build where the board could not spawn in a fresh run. All 85 JSON files under the
  game's save folder were scanned: the board is in 2, both `Game_0`, last written 2026-08-28; the control card
  `cmcBoardInnKeeper` is in 11.
- **Evidence, build and gates.** Fresh-save output is now 3 paragraphs / 493 characters. The source patch asserted
  every anchor exactly once and ran all validations before writing; the Inn drop list equals the pre-bench list at
  `8d31b665d^`, and no other key in that file changed. Before the build only the three patched source files were
  dirty in `Community_Mod_Chest/`. Release build 0 warnings / 0 errors; the DLL carries `AppendAchievementSections`,
  `ProgressSuffix`, `GetVisibleBoardActions` and `LogBoardFit` (UTF-8) and the `text layout: lines=` literal
  (UTF-16-LE), and 0 occurrences of `AppendAchievementStatusLines`. `Framework-PerRunHandlerRegistration.Tests.ps1`
  5/5. All 72 vanilla GUIDs hard-coded in the tracker and kill-effect patches resolve on EA 0.67i; none of the
  board's source, data or helper files changed between the bench and this fix.
