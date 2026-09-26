# Roadmap: Community Mod Chest
Version at time of writing: 1.68.44
Date: 2026-09-25
Audit score: 9/10 - PASS (0 CRITICAL, 1 CMC-owned Design Gap; see `.audit/summary.md`, consolidated 2026-09-25)

Re-derived 2026-09-25 during `/consolidate-audit`, after six releases in four days (1.68.39-1.68.44:
trait composite resync, Nightcrawler's sun back on Light, the witnessed-attack guard recheck, Herbalism
forage doubling, the half-step `TriggerRange` sweep, resident Trust, Market Stall revenue, clone-tile
tree doubling, the errand thanks lines, and the Stone Tile Floor going room-local for EA 0.68b parity).
The prior roadmap (2026-09-20) had no CMC-owned code left in Phase 0. That changed on 2026-09-23 when the
r39 TestHarness pass found DG1, the guard subdual gap, which is now the one code item in Phase 0.

## Current State

**Theme**: A community-suggested content grab-bag that has grown into the fleet's most elaborate village-simulation layer - a five-resident roster with daily schedules, trust and quest chains, an Academy with seven courses and graduate perks, a four-guard Town Watch with a Village Crime / Jail / Banishment loop, a five-chest merchant economy, three tameable cats, seasonal road blocks on 3 of 4 seasons, and a Village Reputation civic-growth meter. Aimed at players who want long-form settlement progression woven into the survival core.

**Content**: 80 items / 79 blueprints / 62 location cards (CT2 structures, exits, boards, interior CT8s) + 7 CT4 interior environments + 3 CT10 improvements + 1 event / 59 perks (+4 NPC perks) / 221 GameStats + 35 NPCStats / 10 NPCAgents / 6 guard Encounters / 184 custom images / 65 `Patcher/*.cs` files / 178 DialogLines + 12 DialogScenes / 1 SelfTriggeredAction. Declarative surface: `BlueprintTabs.json`, `DropInjections.json`, `InjectImprovementInto.json`, `MapMod.json`, `TradingValues.json`, `WorldMap/MapNodes.json`, `EncounterGuards/` (3), `GameSourceModify/` (15), `ScriptableObject/` dialog tree.

**Stability**: 9/10 - PASS. 0 CRITICAL; 1 CMC-owned Design Gap (DG1: no spear-guard fight can end with the player held, so the Captain summon, lenient arrest and jail are unreachable in real play); 2 warnings (the intentional K33 landmark build time, and a README that still calls the sign, jail and cell art blank placeholders). Version sync 1.68.44 across `ModInfo.json`, `Plugin.cs:30` and `README.md:3`; `SimpEn.csv`/`SimpCn.csv` 2390/2390 keys with none one-sided. Every `.audit/` report predates the 1.68.44 source commit; this consolidation re-read each carried finding on disk rather than re-running the audits.

**Released vs. source**: players have 1.68.41. The public repo was last published at `e9338e15d` (1.68.41, `.claude/mod-publish-status.json`), and the `Mod_Update_Manager` embed holds `Community_Mod_Chest.zip` at 1.68.41. 1.68.42-1.68.44 are source-only.

**Open work**: 0 red retrospectives. Yellow (Pending Verification / Open Plan) rows, almost all awaiting in-game confirmation of already-shipped fixes:
- `cmc-village-hearth-fuel-not-refilling` - attempt 3 shipped 1.68.19; T2.137 and T2.149 recorded `fail` on 2026-09-07 against older builds; the feature-map's one BROKEN grade (Town Wood Pile) rests on these.
- `cmc-guard-combat-no-damage` - diagnostic proven firing; the open question (T4.35) is which Encounter a guard fight opens. Now entangled with DG1: a guard fight that cannot end in a hold has fewer endings to observe.
- `river-bridge-east-click-noop` - fix in framework 2.25.30; awaits T2.186.
- `cmc-village-conditional-drop-fixtures-missing` - root cause fixed framework-side (DG-F1, `96a33a8d6`); Session 4 (2026-09-23) passed T2.242 live through the TestHarness (village fixtures and all 7 Hall boards exactly one across leave, return and reload), and T2.242 is now confirmed. It was not the last gate after all: the retro's closure plan also requires a Portal Hub arrival into the Village (CMC's `MapMod.json` world lands in `cmcEnvVillage`), which no run has done, so `/resolve-retro` held it at Pending Verification on 2026-09-25 and filed that check as `T2.277`. T2.185 (the framework change's own check) stays pending.
- `worldmap-clone-duplicate-terrain` - the r39 measurement (2026-09-23) put the doubled trees in board data (a vanilla-UID tree beside the clone's own `__envlocal` copy, surviving a reload); 1.68.43 fixed `TreeRespawnPatch` to recognise the tile's own tree and trim the vanilla copy. T2.128 and T2.147 still carry `fail` verdicts from 2026-09-07 and need re-arming for the new build.
- `guard-kill-despawn` - event-driven removal (1.54.3) not yet re-verified in-game.
- `CMC-HSP-compat` - fixed 1.68.20; awaits T3.41.
- Framework/fleet umbrellas: `wikimod-old-save-load-crash`, `questinjector-blueprint-reset-risk` (CMC does not use QuestInjector), `gate-red-baseline-rediscovery`, `RETRO_CLOSURE_PLAN_2026-09-01` + its pack.

**Framework compliance**: Tier 2. Action interception routes through `Api.ActionRouter` (9 files), spawning through `Api.SpawnService` (12 files), ticks through `Api.TickEvents` (41 files), and `Plugin.cs` extends `ContentModPlugin`; encounter suppression is declarative (`EncounterGuards/*.json`), improvements inject via `InjectImprovementInto.json`, blueprints via `BlueprintTabs.json`. No `DropCollectionGuardPatch`, no `ModLoaderVerison`/`ModEditorVersion`. AdvancedCopperTools is a HARD dependency (`Plugin.cs:21`); HerbsAndFungi, WaterDrivenInfrastructure and Sirus23 are soft (`Plugin.cs:22-24`).

---

## Phase 0: Stabilize

> One CMC-owned code item (DG1) and the evidence debt behind the yellow retros. DG1 has its own plan;
> build it from there, not from this table.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **DG1: give Thorne and Corrin a subdual enemy action** (`EncounterResult` 8, gated on `RequiredWrestlingState` 2, weighted to compete only once the player is losing) and align Vane's `PlayerDemoralizedEffects` with 1.45.0. Today the only result-8 value in `Encounter/` is `cmcEncounterSterlingArrest.json:1999`, so `cmcStatCaptainSummoned` never rises from a fight and every path into the jail hangs off it. Build steps, the options considered and the one open blind spot (vanilla `GenericEncounterPlayerAction` overrides) are in `Documentation/Plans/Community_Mod_Chest/Guard_Subdual_Summon_Plan.md`. | Design Gap fix | P0 | Medium |
| **Re-arm the tracker rows that 1.68.42 and 1.68.43 cite** but whose verdicts predate those fixes: T2.128, T2.147 (`fail`), T4.38 (`fail`), T2.110, T4.26, T2.98, T2.109 (`skip`). A verdict is a claim about the build it was recorded on. | Evidence currency | P0 | Quick |
| Collect the retro-gated runtime confirmations: hearths (T2.137/T2.149), guard-combat Encounter (T4.35), River Bridge eastbound (T2.186), AlwaysUpdate scoping (T2.185), CMC-HSP-compat (T3.41), Stone Tile Floor in CMC interiors (T2.187) and room-local (T2.262), companion follow window (T2.237). The TestHarness can drive most of these; the owner lifted the playthrough gate on code work (2026-09-17), so none of this blocks Phase 1-2. | Evidence gap | P1 | Human or harness playthrough |

---

## Phase 1: Foundation

> Hygiene is clean at the file level; what is left is refreshing the audit artifacts that fell behind a
> fast release cadence, one docs-honesty fix, and getting 1.68.42-1.68.44 to players.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **Correct `README.md:58` and `README.md:69`** (summary W2): both say the Village Home Sign faces and the Jail and its cell "ship blank placeholder art", but all four cards ship painted art (`CMC_VillageHomeSignRustic/Carved.png`, `CMC_Jail.png`, `CMC_JailCellInterior.png`). CHANGELOG Documentation line in the same commit. | Docs honesty | P1 | Quick |
| **Re-run `/code-quality Community_Mod_Chest`**: 11 C# files changed since the 1.68.37 review (+453/-35), including the new 283-line `Patcher/TraitCompositeResyncPatch.cs`, which no code-quality pass has read. Then `/critical-analysis` (its "0 broken promises" predates DG1) and `/audit-mod` at 1.68.44. | Audit hygiene | P1 | Medium |
| Regenerate `feature-map.md` (written for v1.68.16, 28 versions ago); refresh the five domain reports (2026-09-12) at the next content release. | Audit hygiene | P2 | Medium |
| **Ship 1.68.42-1.68.44 to players** once DG1 and the README fix land: `/export-to-repo Community_Mod_Chest`, then `/package-mod-suite` and deploy `Mod_Update_Manager` LAST (framework first, then the other suite mods, then the repack, then MUM). | Release | P1 | Quick |
| Minor polish batch (summary M1-M8), one commit: `StarsCost: 0` on `Pk_GradMedicine`, drop the four orphan `*WarpType` keys, normalise the bare-string `CardImage` on `CMC_MarketStall.json:13` / `CMC_TownWoodPile.json:13`, give `CMC_WellPlans` a weight, align `Perk_Bleeder` / `Perk_WeakStomach` JSON punctuation with the CSV. | Authoring hygiene | P2 | Quick |
| Version hygiene: **clean** (1.68.44 in all three files). No action. | Verified | - | - |
| Localization baseline: **clean** (2390/2390 EN/CN keys, none one-sided). No action. | Verified | - | - |

---

## Phase 2: Core Expansion

> The highest-value content additions, ordered by how much existing content they activate.

### Make the jail layer reachable content (DG1 follow-through)
**What**: after the subdual action lands, walk the whole arrest chain end to end: lose to Thorne or Corrin, Sterling summoned, the lenient "think better of it" arrest, sentencing, rations, warden shifts, the tunnel.
**Why**: the jail, its sentencing rules and its escape tunnel are among the mod's largest systems, and today real play cannot enter them (only the test fixtures can). Once reachable, the redemption track below and the wound-care crafting idea both gain a real player population.
**Requires**: Phase 0 DG1.
**Complexity**: Medium (mostly verification and tuning)

### The Apothecary Remedy Line
**What**: An Allergy Tonic, a Stomach Settler and a Clotting Salve produced at the Apothecary Cabin from H&F herbs plus CMC's own Herb Paste, each countering a shipped drawback trait.
**Why**: CMC ships seven drawback traits (`Bleeder`, `DeadlyDisease`, `SeasonalAllergies`, `WeakStomach`, `BadKidneys`, `Insomniac`, `Leper`) and a full Apothecary NPC, yet `CardData/Item/` holds no tonic, salve, remedy or antidote of any kind (filename sweep, 2026-09-25). It gives the Apothecary a reason past her quest chain and supplies the missing sink for the CMC-to-HerbsAndFungi pairing.
**Requires**: none (the Medicine course's Herb Poultice + Tincture is the pattern). Pairs with combat wound-care crafting (Clean Bandage / Suture Kit).
**Complexity**: Medium

### Widen the pigment-dye line + author FlavourTags (two ready pure-JSON wins; plan N1/N2)
**What**: (a) ochre/berry pigments on the `Bp_Pigment` grind pattern plus a coloured variant per garment, and the `Dye with Pigment` CI extended beyond the three garments that have it (`Chaperon`, `ClothCoat`, `ClothScarf`); (b) `FlavourTags` on CMC's food/herb roster and one `FlavourMatrix/*.json` synergy pair.
**Why**: both re-confirmed unbuilt 2026-09-25 (three dyed variants only; the 5 cards with a `FlavourTags` key all empty, no `FlavourMatrix/`). Both are already promoted in `Documentation/Plans/Community_Mod_Chest/Audit_Remediation_Plan.md`, with the Toys & Games use (N3).
**Requires**: `Intensity` is the `FlavourIntensities` ENUM (0=Medium, 1=Strong, 2=Subtle), NOT a strength number (CLAUDE.md); framework 2.23.6+ for FlavourMatrix.
**Complexity**: Medium (highly parallelizable, pure JSON)

### Voluntary Redemption / Making-Amends Track
**What**: A real path back to Clean without a cell or a fight: a Village Hall reparations fine scaled to the crime band, or a restitution errand that walks `cmcStatVillageCrime` down.
**Why**: the crime loop is almost entirely punitive; the only constructive path shipped is the "Make it right" Miller restitution drag. It matters more once DG1 makes arrests happen.
**Requires**: a design decision (fine-only vs. an errand chain). An errand chain **must** use the clamped-marker pattern (`reference_npc_errand_clamped_marker_pattern`), never an unclamped counter.
**Complexity**: Complex

---

## Phase 3: Integration & Depth

### Merchant purse-tier and Reputation bucket readouts
**What**: Publish a coarse `cmcStat<Resident>PurseTier` (lean / modest / flush) on the weekly accrual tick, and publish at least one of `VillageReputationPatch.cs`'s `ConstructionBucketMax` / `QuestBucketMax` (`:97-98`) contributions as its own hidden stat.
**Why**: both systems compute the information and throw it away; the patch writes only `cmcStatVillageReputation` (`:73`), and `GameStat/` holds no purse stat.
**Requires**: a decision on where the accrual patch publishes the tier (chest wealth is inventory, not a stat).
**Complexity**: Medium

### Gift-preference friendship for the Miller and Weaver
**What**: A gift drag that writes the Miller's and Weaver's trust stat directly (grain/flour for the Miller, cloth/dye for the Weaver).
**Why**: since 1.68.42 both carry a trust stat, but every drag on `Agent_Miller.json` (3) and `Agent_Weaver.json` (9) is an errand or interlock delivery; outside errands, the Inn Keeper's `Gift Wild Garlic` is the only drag across the five resident agents that writes a friendship stat.
**Requires**: the `DragAndDropActions` idiom with `GivenCardChanges.ModType:3`; keep gifts a slow secondary track, never an errand bypass.
**Complexity**: Quick

### Give the `Fugitive` trait a hook into the Watch
**What**: a small starting `cmcStatVillageCrime` in the Suspected band, or a one-time first-contact wariness flag.
**Why**: no `Patcher/` file mentions Fugitive and `Perk_Fugitive.json` touches no `cmcStat*`, so the trait's "hunted" flavour never meets the mod's own crime loop.
**Requires**: read the starting-stat clamp rule in CLAUDE.md (a `StartingStatModifiers` overshoot lands exactly on the cap) and `reference_hidden_stat_quest_state_machine`; "watched more closely", never "already Wanted".
**Complexity**: Medium

### A summer route hazard (optional - close the row if it does not fit the fiction)
**What**: A fourth seasonal `SealableGates` entry for summer.
**Why**: `WorldMap/MapNodes.json` carries `Season` `SealTrigger` entries for Autumn (`:85`), Winter (`:158`, `:170`, `:182`) and Spring (`:272`); summer is the only season with no route pressure.
**Requires**: **every** clearing action on the challenge card must share the gate's `ClearActionKeyPrefix` (CLAUDE.md; a mismatched by-hand action softlocks the road silently). Enforced by `MapNodes-Schema.Tests.ps1`.
**Complexity**: Quick

### WaterDrivenInfrastructure and SkillSpeedBoost hooks
**What**: Extend the Academy graduate perks: Architecture/Metallurgy already unlock WDI stations; an SSB-style learning-speed bonus inside a course's own skill domain is the natural next tier.
**Why**: deepens the Academy's payoff without adding courses.
**Requires**: confirm no domain overlap first, and read SSB's single-composition-point guard rule before wiring.
**Complexity**: Medium

> **Standing constraint, learned the hard way on 2026-09-04.** CMC may never again apply the C# blueprint-lock idiom to another mod's material. The Metallurgy course locked ACT's `advanced_copper_tools_bp_metal_sheet` and stranded 21 downstream ACT blueprints for every non-graduate, with zero in-game explanation. Fixed in 1.68.17 by replacing the lock with a grant (`AcademyCourseService.cs:161-169`). Lock END PRODUCTS only; **GRANT** materials. Enforced by `Development_Tools/Tests/Academy-CourseGating.Tests.ps1`.

---

## Phase 4: Polish

| Item | What | Complexity |
|------|------|------------|
| Dedicated Carpentry graduate icon | `Pk_GradCarpentry.json` reuses `VillageAcademyArchitecture`; the 2026-09-22 icon drop added none for Carpentry. A `/create-image` job | Quick |
| Toys & Games use (plan N3) | `CMC_BoneDice`, `CMC_SpinningTop`, `CMC_WheeledHorse` carry 0 actions; a cat play-toy Care lever and a small fidget self-action give the line a purpose | Medium |
| Retire the last two diagnostic patches | `GuardCombatDiagnosticPatch` (`Plugin.cs:237`) and `CompanionFollowDiagnostics` (`Plugin.cs:161`): delete or demote **only after** their retros are confirmed in-game (T4.35, T2.237). Grep `Documentation/Retrospectives/` for a diagnostic's tag before touching it | Quick |
| Close the art rows the prior roadmap carried | Village Home Sign faces, Jail + cell and the Market Stall dressed art all ship real art now (the stall pair diverged in `1b95a0405`); the distinct-potion-sprite row was closed by design 2026-09-12. Nothing to build; only the README sentences in Phase 1 remain | - |

---

## Long-term Vision

> Where this mod should be at v2.0.

Community Mod Chest has outgrown its own name. What began as a grab-bag of community item requests is now the fleet's most elaborate settlement simulation: a five-resident village with schedules, trust and quest chains, an Academy with seven degrees, a criminal-justice system with sentencing and escape, a merchant economy with per-NPC wealth, and a civic reputation meter. At v2.0 the mod's identity should lean fully into being **the village layer for Card Survival** - the thing a player installs when they want somewhere to belong rather than just more recipes.

The first gap between here and that identity is **reachability**: DG1 shows that a large shipped system (the arrest-to-jail chain) can exist, pass its fixture-driven walkthroughs, and still be unreachable in real play. The second is **reciprocity**: the village reacts to your crimes but not to your virtue, the drawback traits have no remedies, the Miller and Weaver have no gifts, and the Reputation meter's two halves are computed and discarded. The third and largest is **evidence, not content**: at ~93 feature groups the mod needs verification runs more than new systems, and the TestHarness now makes many of those runs an agent job rather than a human one.

**Potential major additions** (not yet justified - revisit after Phase 3):
- **Generic NPC archetypes** (Shopkeeper, Hirable Hunter, a charming companion, an Elder with quests) - specced in `VILLAGE_AREA.md`, deliberately not covered by the named-resident roster.
- **Reviving the benched Wisp quest-giver** (`Documentation/Ideas/Community_Mod_Chest/Wisp_and_NPCs/`) - its "wait for the vanilla NPC rework" blocker is overtaken by CMC's own NPCAgent chassis. Its lessons (non-instanced clone envs; never attach a vanilla QuestLog to the player save) must survive any revival.
- **Named outfit presets** - the one-motion Dress/Undress routing exists; the preset-storage half needs real design.
- **A sleep-axis scented incense** - deliberately deferred: the only hook is the `SleepClock` stat, and a passive `RateModifier` on the game's own sleep-timing stat is the vanilla-mechanics risk class CLAUDE.md warns about.

These live in `Documentation/Ideas/Community_Mod_Chest/`. `IDEAS.md` still lists two shipped Near-Term entries and one refuted one; its own 2026-09-20 append says `/consolidate-ideas Community_Mod_Chest` is due before the next append.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod Community_Mod_Chest` and update this roadmap |
| Game version update | Run `/update-mod-version`, refresh `lib/Assembly-CSharp-nstrip.dll` (CMC references `HerbsAndFungi/lib/Assembly-CSharp-nstrip.dll`; regenerating it needs the external NStrip install), re-run `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis Community_Mod_Chest` to verify the fix |
| After a fix batch lands | Run `/export-to-repo Community_Mod_Chest`, then repackage the suite and deploy `Mod_Update_Manager` **last** |
| Any change touching a sibling mod's content | Re-read the Phase 3 standing constraint above and run `Development_Tools/Tests/Academy-CourseGating.Tests.ps1` |
| A fix names an existing tracker row as its check | Re-arm that row for the new build in the same pass; a verdict recorded on an older build does not verify new code |
| Whenever a play session or harness run happens | Record results with `/playthrough-test-plan record` |

---

## Skill Cheatsheet for This Mod

```
/audit-mod Community_Mod_Chest             - full health check, updates .audit/
/critical-analysis Community_Mod_Chest     - adversarial review
/code-quality Community_Mod_Chest          - C# reliability review
/repair-items Community_Mod_Chest          - auto-fix item JSON issues
/repair-structures Community_Mod_Chest     - auto-fix structure JSON issues
/build-mod Community_Mod_Chest             - build Release DLL
/deploy-mods Community_Mod_Chest           - build + deploy to game
/update-mod-version Community_Mod_Chest <ver> - bump version in all 3 files
/export-to-repo Community_Mod_Chest        - push to public repo
/playthrough-test-plan record              - log in-game verification results
```

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

### 2026-09-17 - Trait_Effect_Repair_Plan (closeout)
- **Scope:** Fleet. The same entry is in `Community_Mod_Chest/ROADMAP.md`, `HerbsAndFungi/ROADMAP.md`, `Sirus23_Mod_Collection/ROADMAP.md` and `CSFFModFramework/ROADMAP.md`, the four mods the plan names.
- **Verdict:** every phase built: P1 (CMC 1.68.25), P1b (1.68.26, `1f71be4a7`), P1c (1.68.27, `b5d93a885`), P7 (CMC 1.68.28 and HerbsAndFungi 1.13.1, `e4fad82ab`), P2-P6 (CMC 1.68.30, Sirus23 1.21.3, CSFFModFramework 2.26.0, `7b1c68f64`), and the pack's last open prompt, Prompt 1 (delete the temporary `Community_Mod_Chest/Patcher/TraitDiagnostics.cs` tracer), in `8d25ed810` with no version bump. Prompt 1 was gated on playthrough row T2.239, which is still pending; the owner lifted that gate on 2026-09-17 ("We need to be able to proceed with code work without being blocked on a full playthrough"). The plan doc's own row inventory was compared against the pack rather than trusting its 0-open count, and three plan items with no recorded outcome were settled this pass, none needing code: section 3.6's pre-release check (no `TriggerRange` on the four infection stats Deadly Disease rate-modifies), section 3.2's Swimmer 5 Sun re-check (kept), and section 3.1's "drop madness from Lunacy's text" (already done in 1.68.30). 0 plan rows unbuilt.
- **Disposition:** ARCHIVE to `Documentation/Design/Trait_Effect_Repair_As_Built.md`: the plan with an archive banner, dated notes for Prompt 1 and the three checks above, and the drained pack appended as an appendix with Prompt 1 removed. Pack `Documentation/Plans/Fleet/Trait_Effect_Repair_Plan_Implementation_Prompts.md` deleted. The plan holds the only record of the ground truth behind the rework (R1-R20, D1-D9) and of every deviation from its starting values, so it is kept, not deleted. Not published: the public releases are still CMC 1.68.24, HerbsAndFungi 1.13.0, Sirus23 1.21.2 and CSFFModFramework 2.25.32 (`.claude/mod-publish-status.json` commits read back through each `ModInfo.json`), and the export is the owner's call. R14/R16's reading of the player's "+1.2 Speed" as the +1.2 Aid rate is still unconfirmed with the player; it is recorded in the archive's section 1.
- **Pruned:** Prompt 1 from the pack before the pack was folded into the archive and deleted; nothing else. The plan left `Documentation/Plans/Fleet/` whole.
- **Evidence:** Prompt 1: `dotnet build -c Release` 0 warnings 0 errors; the built `bin/Release/Community_Mod_Chest.dll` holds `TraitDiagnostics` (UTF-8) 0 times and `[TRAITDIAG]` (UTF-16-LE) 0 times, against controls `RiverSwimPatch` 1 and `[RiverSwimPatch]` 9; `grep -rn TraitDiagnostics --include=*.cs Community_Mod_Chest` 0 lines (control `RiverSwimPatch` 15); no file under `Documentation/Retrospectives/` cites `TRAITDIAG` or `EnableTraitDiagnostics`. Plan rows against code: `Community_Mod_Chest/GameStat/` holds all nine `CMC_Trait*.json` (eight trait stats plus `CMC_TraitSkinShelter.json`); `TraitsTickHandler.cs`, `TraitsActionHandler.cs` and `TraitDiagnostics.cs` absent from `Patcher/`; gates (a)-(e) plus the extended river swim test present as `TraitStat-CompositeBands`, `Perk-HeldTestNotAllPerks`, `PerkAidRate-HoldsTier`, `PerkStatClamp-Reachability`, `StatBase-NoVanillaSource` and `CMC-RiverSwim` `.Tests.ps1`; `CSFFModFramework/Patching/PerkOriginTagPatch.cs` and `Discovery/ModTag.cs` present, `[Perks] ShowModOriginTag` bound at `CSFFModFramework/Plugin.cs` and documented in its README config table; 12 `ModInfo.json` files carry `ShortName`; 21 Sirus23 JSON files reference `SaturationDairy` (by name or its UID `f4b08d0250e6099419e010a83578b9db`); `ModInfo.json` versions CMC 1.68.30, HerbsAndFungi 1.13.2, Sirus23 1.21.3, CSFFModFramework 2.26.0. Section 3.6 check: a JSON walk of 29,435 files (the vanilla EA 0.67i UniqueID and ScriptableObject exports plus every mod folder, 0 unparseable) found 0 `TriggerRange` objects whose `StatWarpData` is Infection_Gastrointestinal `1a8d37787d69c9b4aa05d332921f3763`, Infection_UpperRespiratory `3407bfc804966194e9e369a7cce6d07d`, Infection_Systemic `dc3cae53109fd5945b5279ec6291caae` or Infection_LowerRespiratory `fa47d156a14dac842bcdcbdbf8504e35` (by UID or name), with the same walk finding the control, vanilla `Tgr_Anxiety` on Stress at 240; `.decomp/` (949 `.cs`) names no infection stat (controls `HourOfTheDayValue` 4 files, `StatValueTrigger` 13), and no mod `.cs` references the four UIDs. Section 3.2: vanilla `CharacterPerk` SunsCost is only ever 0, 15 or 30 (71/38/20 of 129), and Swimmer's 5 matches the six other five-Sun CMC perks (CMC's SunsCost counts: 0 x20, 1 x21, 5 x7, 15 x5, and 10, 20, 25, 30, 100 once each), which include Abundant Growth's +1.2 Aid rate and Wide Hands' +15 skill offset; Swimmer now delivers what its price was set for. Section 3.1: `madness`, `insan` and `mania` absent from `Pk_Lunacy.json`, `CMC_TraitLunacy.json` and its CSV rows. Tracker: T2.239 and T1.84-T1.114 all present in `.claude/playthrough-test-status.json`, all `pending`, so lifecycle gate 2 holds; the 33 plan and pack path citations in it and the one in `Playthrough_Test/Playthrough_Checklist.html` were repointed at the archive in the same commit.
- **Verification debt:** T2.239 (Nyctophobia and the tracer's log lines), T1.84-T1.88 (P1b), T1.89-T1.97 (P1c), T1.98-T1.101 (P7 medicine), T1.102-T1.109 (P2 condition traits), T1.110-T1.111 (P3 swim and Aid), T1.112 (P4 Sirus23 dairy), T1.113-T1.114 (P5 origin tag and clock hour), all `pending`. T2.239 part (5), T1.114 part (4) and the optional evidence in T1.102-T1.108 read `[TRAITDIAG]` lines that CMC 1.68.30 on the dev install still prints until the next CMC deploy and no later build prints; each of those rows carries a dated 2026-09-17 note saying so.
