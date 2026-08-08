# Roadmap: Community Mod Chest
Version at time of writing: 1.45.0
Date: 2026-08-08
Audit score: **5/10 - FAIL: 4 CRITICAL issues** (0 Design Gap, 9 WARNING, ~53 MINOR) - see `.audit/summary.md`

## Current State

**Theme**: CSFF's definitive **village-life expansion** -- an explorable settlement east of the River Clearing with enterable Inn and Academy, resident NPCs on daily schedules (Inn Keeper, Professor, Miller, Weaver, Apothecary), quest chains, buildable civic structures, a Renown economy, tameable companion cats, a four-guard **Town Watch** with a crime/jail/escape loop and (as of this session's uncommitted work) a Captain who can talk instead of only fight, plus a large supporting layer of apparel, pottery, comfort items, weapons/armor, and character-creation traits.

**Content**: 59 items / 65 blueprints / 47 location cards / 7 environment cards / 2 CT10 improvements / 51 CharacterPerks (+ 4 guard NPCCharacterPerks) / 10 NPCAgents (6 residents + 4 guards) / 101 GameStats / 107 custom PNGs / 48 C# patch classes / TradingValues price table. Bilingual: `SimpEn.csv` (1,921 rows) + `SimpCn.csv` (2,002 rows), CLEAN parity per the last preflight run.

**Stability**: **5/10, a real regression from the 9/10 shipped-version verdict.** This is not new breakage -- it's newly-*discovered* breakage: this cycle re-ran `/audit-items`, `/audit-blueprints`, `/audit-structures`, `/audit-images`, `/audit-perks`, and `/code-quality` in full (all had been stale since v1.36.1-v1.37.1) and surfaced **4 CRITICAL findings** the mechanical preflight and the standing `critical-analysis.md` had no visibility into: two blank-card sprite bugs (`CMC_JailRationTray.json`, `CMC_JailRationFood.json`) and two code-quality reliability defects in `GuardDutyPatch.cs` and five NPC-spawn call sites. All four are well-scoped, one-line-to-moderate fixes (see Phase 0). `critical-analysis.md` itself is now **content-stale**: it predates a substantial batch of this session's **uncommitted** work (`Patcher/GuardWantedReactionPatch.cs`, the `CMC_SterlingTalk*` DialogScene/DialogLine set, three new GameStats) that has had zero adversarial review.

**Open work**:
- **Fix the 4 fresh CRITICALs** (Phase 0, below) -- two sprite typos, one `Object.Destroy` retry race, one missing-log NPC-spawn silent-failure class.
- **In-game verification (top priority, unchanged in substance)**: the village stack -- Town Watch / Jail / Copper Chest / arrest-summon, and now the new Sterling Talk dialog -- is built and (once C1-C4 land) statically clean, but **never play-tested**. Static audits cannot close this.
- **W5**: Professor Commissions popup "?" placeholder + duplicate "Specimen Request" button when 2+ commissions are ready -- still open; `LogCommissionPopupState` instrumentation is still live at `Patcher/ProfessorSchedulePatch.cs:803/815`, never root-caused.
- **W6**: Iron Rod fishing runtime check (`IronRodFishingPatch.cs:199`'s `JsonUtility` clone doesn't re-borrow `RequiredStatValues`/`ReceivingCardChanges`) -- still an open critical-analysis "Runtime Verification Required" item.
- **Re-run `/critical-analysis`** once C1-C4 land and this session's guard/Sterling-dialogue work is committed -- the arrest-summon subsystem and the new Talk dialog have never had an adversarial pass.
- **Role split (planned, not executed)**: Village Master Plan section 10.8.11 -- guard role split with Captain = Sterling (renamed in v1.44.6; role split itself not executed). This session's uncommitted work (Sterling Talk, `CMC_SterlingWarningGiven`) is the FIRST concrete step toward it.
- **Retrospectives**: no OPEN rows reference CMC. `questinjector-blueprint-reset-risk` and `RETRO_CLOSURE_PLAN_2026-07-23` are both PENDING-dormant framework paths with **zero** CMC exposure (grep-confirmed: no `QuestInjector` reference in CMC source).

**Framework compliance**: **Tier 2** -- `Plugin : ContentModPlugin`; uses `ActionRouter` (Market Stall / Well / Copper Chest / guard-outcome handlers), `SpawnService`, `TickEvents`, `EncounterGuards`; declarative `WorldMap/MapNodes.json`, `TradingValues.json`, `BlueprintTabs.json`, `InjectImprovementInto.json`. Min framework **2.15.1+** (dialog body WarpData, now also used by the new Sterling Talk dialog scene); **2.18.0+** trading table, **2.19.0+** winter road blockage, **2.20.0+** `NPCCharacterPerk` loading (guard Suspicion), **2.20.2+** Village Crime banishment lock (`ConnectionGates.LockConditions`).

---

## Phase 0: Stabilize

> Static analysis has gone as far as it can on the old code. This cycle's full sub-audit fan-out found four real, well-scoped CRITICALs that must land before anything else -- then it's back to the same play-test backlog as before.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **C1 -- fix `CMC_JailRationTray.json`**: `CardImageWarpData: "ClayPlate"` (the vanilla card's own UID) -> `"Plate_Clay"` (its real sprite name). Blank card today, silently. Mirror the edit into `bin/Release/`. | Bug fix | P0 | Quick |
| **C2 -- fix `CMC_JailRationFood.json`**: `CardImageWarpData: "ClayBowl"` -> `"Bowl_Clay"`. Same anti-pattern, same fix shape. Mirror into `bin/Release/`. | Bug fix | P0 | Quick |
| **C3 -- fix the `EnsureVillageTerritoryTag` retry race** in `Patcher/GuardDutyPatch.cs:1021-1064`: stage `replacement` `CardTags` arrays and only call `Reflect.SetMember` after the whole batch resolves (true all-or-nothing), or roll back already-applied stamps before `Object.Destroy(tag)` on the failure path. | Bug fix | P0 | Medium |
| **C4 -- add `LogWarning` at each of the 5 NPC-spawn null branches** (`AshPartnerSpawnPatch.cs:139-140`, `GuardSpawnPatch.cs:187-256`, `InnKeeperSpawnPatch.cs:196-228`, `CottageResidentSpawnPatch.cs:276-350`, `ProfessorSchedulePatch.cs:445-476`) so a silent post-spawn `AllNPCs` lookup miss is diagnosable instead of invisible. | Code quality | P0 | Medium |
| **Re-run `/critical-analysis Community_Mod_Chest`** once C1-C4 land and this session's guard/Sterling-dialogue work (`GuardWantedReactionPatch.cs`, `CMC_SterlingTalk*`, 3 new GameStats) is committed -- the current report is content-stale and has zero coverage of that subsystem. | Audit hygiene | P0 | Medium |
| **In-game verify the whole shipped village stack** -- guard spawn/beats, crime raise, pursuit, sequential gauntlet, arrest -> jail, warden shifts + escape tunnel, rations, copper-chest accrual/sell/theft, the banishment lock, **and now the new Sterling Talk dialog** (crime-band-varying greeting + one-time warning). Record results via `/playthrough-test-plan Community_Mod_Chest record`. | Runtime QA | P0 | Complex (play session) |
| **W5 -- Professor Commissions popup.** "?" placeholder + duplicated "Specimen Request" button when 2+ commissions are ready. Capture `LogCommissionPopupState` output in a play session with 2+ ready commissions, root-cause (lead: `.decomp/BlueprintConstructionPopup.cs:99-126` index alignment), fix, then demote the instrumentation to `LogDebug`. | Bug fix | P0 | Medium (needs play session) |
| **W6 -- Iron Rod fishing runtime check.** `IronRodFishingPatch.cs:199`'s `JsonUtility` value-clone does not re-borrow `RequiredStatValues`/`ReceivingCardChanges`; the vanilla River "Fish" CI could not be inspected offline. Fish each water card with the Iron Rod -- watch for an NRE or a silently-dropped stat gate. Fold into the same play session as the item above. | Runtime QA | P0 | Quick (within a play session) |

---

## Phase 1: Foundation

> Table-stakes hygiene. The sub-report refresh that used to sit here is now DONE -- all six per-type audits are current as of this consolidation.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| ~~Per-type sub-report refresh (`/audit-items`, `/audit-blueprints`, `/audit-structures`, `/audit-images`, `/audit-perks`)~~ | Audit hygiene | **CLOSED** | All five re-ran in full this cycle (2026-08-08), superseding the v1.36.1/v1.37.1 scope. |
| **Perk schema normalization** -- backfill the omitted `EquippedCardsWarpData`/`Type`, `AddedCardsWarpData`/`Type`, `NoSafetyMode` keys on the 17 compact `Pk_*.json` perks (use `Pk_GradMedicine.json` as the template), and resolve `Perk_Claws.json`'s undisclosed `StarsCost: 1`. | Schema hygiene | P1 | Quick |
| **Promote failure-path `LogDebug` to `LogWarning`/`LogInfo`** across `CopperChestPatch.cs`, `HiddenStat.cs`, `GuardWitnessPatch.cs`, `VillageHallBoardsPatch.cs`, `ProfessorSchedulePatch.cs`, `CardVisualsRefresh.cs`, `QuestChainSchedulePatch.cs`, `GuardOutcomePatch.cs` (code-quality.md G2) so subsystem-degradation traces survive the default BepInEx filter. | Code quality | P1 | Quick |
| **Guard role split** (Village Master Plan section 10.8.11) -- Captain = **Sterling**, not Ashdown. The Talk-dialog work landing this session is the first concrete piece; full role differentiation still pending. | Content / plan follow-through | P1 | Medium |
| ~~CQ1 -- `ex.Message` -> `ex.InnerException?.ToString()`~~ | Code quality | **CLOSED** | Confirmed clean this cycle -- `code-quality.md` B1 verified every `catch` block programmatically. |
| ~~Chinese localization parity~~ | Localization | **CLOSED** | `mod-report.md` this cycle: CLEAN, 0 missing/extra/no-CJK/stale. |
| Version hygiene -- synced at **1.45.0** across ModInfo/Plugin.cs/README (re-verify after each release via `/update-mod-version`). | Version hygiene | P1 | Quick (already clean) |

---

## Phase 2: Core Expansion

> The most impactful content additions. One of last cycle's Phase 2 items is now **in progress** (uncommitted); the rest are unchanged.

### Extend the Town Watch Talk dialog to the other three guards
**What**: This session's uncommitted work gave **Captain Sterling** a crime-band-varying Talk dialog (`GuardWantedReactionPatch.cs`, `CMC_SterlingTalk*` DialogScene/DialogLine, `CMC_SterlingWarningGiven` GameStat) -- the guards' only prior action was "Attack." Extend the same pattern to Thorne, Corrin, and Vane, each with their own personality-flavored lines.
**Why**: `Documentation/Ideas/Community_Mod_Chest/IDEAS.md`'s most recent pass (2026-08-08) flagged this exact gap ("the only action on any of them is Attack"); Sterling's version is the proof of concept, now built.
**Requires**: Phase 0 in-game verification of Sterling's Talk dialog first -- confirm the pattern works before tripling it.
**Complexity**: Medium (3x the Sterling pattern, one DialogScene/Line set + Talk DA per guard).

### Roll the Copper Chest out to the other four residents
**What**: The Miller Copper Chest (weekly salt/flour accrual, sell-from-real-contents, theft/heat roll) shipped as an explicit **template**. Clone it to the **Weaver** (cloth/cord), **Apothecary** (tinctures/herbs), **Inn Keeper** (staples/coin), and **Professor** (specimens), each with themed restock goods and its own affordability + theft heat.
**Why**: README states outright "Miller only for now -- the Weaver, Apothecary, Inn Keeper, and Professor have no chest yet"; `project_cmc_copper_chest` calls the Miller build a "rollout template," and `Village_Master_Plan.md` section 10.8.3.7 already sequences it as next-chunk work.
**Requires**: **Phase 0 in-game verification of the Miller instance first** -- cloning an unverified pattern multiplies any defect by five.
**Complexity**: Medium (per-resident clone of `CopperChestPatch` + location card + stat trio).

### Cat care line -- Cat Bed + fish treats + food bowl
**What**: The three cats track a **Care** stat and self-serve from a Rain Cistern, but the only Care inputs are Pet/Feed/Give Water. Add a **Cat Bed** (CT2 comfort placeable), a **fish-treat / dried-fish snack** (Feed CI raising Care + Hunger), and a **Cat Food Bowl** self-serve station (food-side mirror of the cistern auto-drink). Confirmed still unshipped this cycle (no `CatBed`/`FoodBowl` card in the tree).
**Why**: The Care mechanic shipped without proactive player inputs -- a companion the player tamed through a whole storyline has little to do day-to-day. Strongest audit-backed content gap.
**Requires**: none (bed/treat pure JSON; bowl = small C# copy of `AshCatTickPatch`'s auto-drink branch).
**Complexity**: Medium.

### Craftable instrument -- Clay Ocarina / Bone Whistle
**What**: A CMC-made instrument (pottery or bone tier) that (a) counts as a valid Shadow-taming trigger and (b) carries a "Play a Tune" comfort DA. Confirmed still unshipped this cycle (no `Ocarina`/`Whistle` card in the tree).
**Why**: Taming Shadow -- the final requirement to finish the Apothecary's Cabin -- currently needs a **vanilla** Wooden/Bone Flute or Frame Drum. A player who never crafts one has no path to Shadow, and therefore none to the Cabin. Closes a real, currently-unstated dependency gap.
**Requires**: none -- append the new UID to `CMC_ShadowCat.json`'s taming-CI `TriggerCardsWarpData`. Pure JSON.
**Complexity**: Quick.

---

## Phase 3: Integration & Depth

> Cross-mod hooks and progression payoffs for experienced players.

### Burglar's Kit + "Make it right with the Miller"
**What**: A Lockpick/Pry Bar CT0 tool that shaves points off the Copper Chest detection roll, and a restitution interaction (drag Salt/copper onto the chest to call the new `ReduceCrime(amount, reason)`).
**Why**: `IDEAS.md`'s 2026-08-07 pass -- gives the just-shipped theft mechanic a craftable payoff and adds the loop's first voluntary crime-down path.
**Requires**: none for the kit (pure JSON + a ~3-line detection-calc read); the restitution interaction reuses `CopperChestPatch`'s existing `ActionRouter` handler.
**Complexity**: Quick (kit) / Medium (restitution).

### A fair civic tell for the hidden crime standing
**What**: `cmcStatVillageCrime` is fully hidden (`Visibility:2`/`CannotBeInspected`). Now that guards, pursuit, jail, and (this session) Sterling's Talk dialog are live, add a purely-informational, player-sought tell -- ask the Inn Keeper "how do folks see me?" or a discreet Village Standing board line -- reflecting the crime *band* in prose, not the number.
**Why**: closes a real fairness gap the enforcement loop opened. Read-only; must not duplicate the guards' own reactions or Sterling's new Talk dialog.
**Requires**: coordinate with the shipped enforcement surfaces, including this session's Sterling Talk work; board status-tier `RequiredStatValues` idiom.
**Complexity**: Medium.

### Medicine course -> tangible item payoff (Herb Poultice + Tincture)
**What**: Add 1-2 medical blueprints gated behind `cmcperkgradmedicine` -- Herb Poultice (HerbPaste + Cloth -> heals a Wound / stops Bleeding) and a Tincture consumable.
**Why**: Medicine is the only Academy course whose graduate perk grants no craftable payoff; pairs with the mod's own Bleeder/DeadlyDisease drawbacks.
**Requires**: none (follows `Bp_HerbPaste` + `AcademyCourseService` gating). Pure JSON.
**Complexity**: Medium.

### Cross-mod: cat water source + shared fuel
**What**: Add WDI water-container UIDs to `AshCatTickPatch.ExplicitWaterIds` so a WDI water setup doubles as a cat auto-drink source (soft-dep, keep the 3-vanilla-container fallback). Separately, a shared fuel/oil item so ACT Metal Lantern + CMC Ceramic Lamp draw from one consumable.
**Why**: Makes the two mods' water/lighting systems read as one feature; reduces inventory clutter.
**Requires**: WDI (optional) / ACT (hard dep) UIDs; silent-soft-dep discipline (`feedback_cross_mod_output_dependency`).
**Complexity**: Medium.

### Village Renown capstone -- Founder's Monument
**What**: A one-time placeable (or a village-festival beat) rewarding a full Renown meter, distinct from the Academy Scholar capstone.
**Why**: Renown's only current payoff is the Market Stall 25% sales bonus.
**Requires**: existing `VillageRenownPatch` completion hook (monument = trivial CT2 spawn).
**Complexity**: Medium.

---

## Phase 4: Polish

> Art, legibility, and text that make the mod feel finished.

| Item | What | Complexity |
|------|------|------------|
| **Jail + Guard art** | Replace the blank placeholder `CMC_Jail` / `CMC_JailCellInterior` PNGs and the four guards' borrowed portraits with custom art. README already discloses these as placeholder. Art briefs must say "Captain Reeve Sterling" and should account for his new Talk-dialog presence. | Medium |
| Care-tier flavor readout | Make the Care stat legible before it hits zero -- a varied "Pet" response / inspection line gated by `RequiredStatValues` on `SpecialDurability3` (board-tier idiom). Pure JSON + CSV | Quick |
| Description accuracy pass | Confirm the extensive README guard/jail/chest/Sterling-Talk prose matches shipped behavior once C1-C4 land; verify milestone/course reactions fire once each | Medium |
| Cosmetic tidy-ups | `CMC_AshBoarTrail.json` (CT2 marker filed under `CardData/Item/`) folder placement; `Bp_CMC_MarketStall.json` orphan `CardsOnBoardWarpType`; `CMC_JailCellBed.json`'s borrowed vanilla `BedRoll` localization keys (re-key to `CMC_`-prefixed); ~51 blueprint schema-completeness gaps; `CMC_CopperChestMiller.json`'s unverified `"ChestPlaced"` sprite | Quick |
| (Fleet tooling, not CMC) | Teach `Audit-Mod-Preflight.ps1` AcquisitionCoverage to parse `NPCAgent` produce/consume chains -- four audit passes in a row have re-adjudicated the same J26/J27 false positives | Medium |

---

## Long-term Vision

> Where Community Mod Chest should be at v2.0.

CMC has fully become the game's **village-life simulation**. With v1.44's Town Watch / Crime / Jail loop, the civic-consequences half of that vision shipped; this session's uncommitted Sterling Talk dialog is the first step past "the Watch only fights you" toward a Watch you can actually talk to. The natural v2.0 is a **fully reactive, fully verified village**: the guard/crime/jail/Talk system proven in-game and adversarially reviewed, the guard role split executed and extended to all four guards, the Copper Chest generalized to all five residents, a fair way to read your own standing, and a proper **companion-care subsystem** for the three cats (Care legibility, bowls, high-Care rewards, away-trip errands) reading as first-class. At that scale the mod is less "a chest of community items" and more "the village + companions expansion," where the pottery/apparel/comfort item layers stay stable while the *systems* deepen and get art.

**Potential major additions** (not yet justified -- revisit after Phase 3):
- **Recurring seasonal village festival / market day** -- now that `GameQuery.CurrentSeason` resolves and the Farm spawns per-season fields, a rotating civic event fits the reactive-village theme.
- **Companion "away-trip" errands generalized from the Ash boar hunt** -- any deeply-bonded cat occasionally goes exploring; following its trail yields a keepsake. Makes the Care stat *drive* content.
- **A visiting trade caravan** -- a periodic outside vendor with rotating exotic stock, a sink for a maxed local economy (`IDEAS.md` 2026-08-08 pass).
- **Spin the cat companion-care convention into the planned AnimalCompanions mod** -- CMC seeds the shared care/stat chassis; draw the seam before either mod hardens it.

These have specs under `Documentation/Ideas/Community_Mod_Chest/` (IDEAS.md, DEFERRED_ITEMS.md, VILLAGE_AREA.md, QUALITY_SPLIT.md, PERK_AloneInTheWorld.md). The Wisp's Cabin / Wisp-NPC system remains **benched** pending the vanilla NPC rework (`Documentation/Ideas/Community_Mod_Chest/Wisp_and_NPCs/`). Sequenced work lives in `Documentation/Plans/Community_Mod_Chest/` (Village_Master_Plan.md + Village_Master_Implementation_Prompts.md + Audit_Remediation_Plan.md).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| **Immediately** | Fix C1-C4 (Phase 0), commit this session's guard/Sterling-dialogue work, then re-run `/critical-analysis Community_Mod_Chest` |
| After that | Play-session verification of the village stack including the new Sterling Talk dialog; record via `/playthrough-test-plan Community_Mod_Chest record` |
| After any new content phase | Run `/audit-mod Community_Mod_Chest`, then `/consolidate-audit Community_Mod_Chest` to refresh `.audit/` + this roadmap |
| Game version update (EA 0.67 pending) | Run `/update-mod-version`, check CLAUDE.md EA notes, re-run `/diagnose-log`; re-verify the EA 0.66 `CreateNPC` 3-param fix still holds |
| After fixing W5 | Run `/critical-analysis Community_Mod_Chest` to verify |
| After Phase 2 complete | `/export-to-repo Community_Mod_Chest` and bump version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod Community_Mod_Chest         - full health check, updates .audit/
/consolidate-audit Community_Mod_Chest - merge .audit/ into summary/ideas/ROADMAP/plan
/critical-analysis Community_Mod_Chest - adversarial review
/playthrough-test-plan Community_Mod_Chest - build/record the in-game QA checklist
/repair-items Community_Mod_Chest      - auto-fix item JSON issues
/repair-blueprints Community_Mod_Chest - auto-fix blueprint JSON issues
/build-mod Community_Mod_Chest         - build Release DLL
/deploy-mods Community_Mod_Chest       - build + deploy to game
/update-mod-version Community_Mod_Chest <ver> - bump version in all 3 files
/export-to-repo Community_Mod_Chest    - push to public repo
```
