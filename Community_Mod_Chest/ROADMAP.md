# Roadmap: Community Mod Chest
Version at time of writing: 1.46.6
Date: 2026-08-11
Audit score: 9/10 — PASS (0 real CRITICAL)

## Current State

**Theme**: A large community-suggested content pack whose center of gravity is an enterable Village east of the River Clearing — Inn, Academy (six courses), five named residents on daily schedules, Town Watch guards, a jail-and-escape loop, per-resident Copper Chests, Village Hall + seven notice boards, and long-form civic construction — wrapped around a broad supporting layer of apparel, pottery, comfort/decor, fishing gear, weapons/armor, and character-creation traits.

**Content**: 59 items / 66 blueprints / 51 structures & locations / 51 perks / 108 custom images

**Stability**: 9/10 — 0 real CRITICAL (3 preflight CRITICAL families all verified false positives), 0 design gaps, 8 warnings (4 intentional/FP, 4 actionable). Mechanical layer clean: versions synced, build 0/0, bin/Release in sync, WorldMap UIDs clean, EN/CN localization 1870/1870.

**Open work**: No 🔴 Open retrospective names Community_Mod_Chest. Two 🟡 Open Plan retros touch CMC-adjacent *framework* features CMC does not actively use — `questinjector-blueprint-reset-risk` (QuestInjector disabled since 1.7.0; CMC uses its own hidden-stat quest tracking instead) and `RETRO_CLOSURE_PLAN_2026-07-23` (abandoned-for-now). Neither blocks CMC.

**Framework compliance**: Tier 2. `ContentModPlugin` base manages the Harmony lifecycle; `RegisterPatches` uses per-patch `TryApply(...)` (not `PatchAll`); `SoftDependency` declared for ACT/H&F/WDI/Sirus23. Content injection is declarative (`BlueprintTabs.json`, `InjectImprovementInto.json`, `DropInjections.json`, `EncounterGuards/*.json`, `TradingValues.json`, `MapNodes.json`). No deprecated/dangerous patterns present (0 `DropCollectionGuardPatch`, 0 unfiltered hot-path prefixes, 0 `ModLoaderVerison`/`ModEditorVersion`, no local framework-service duplication).

---

## Phase 0: Stabilize

> Small, mechanical reliability + hygiene fixes. None block release on their own, but W1 carries a real player-visible failure, so land these before the next content phase.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **D4 / W1** — `GraduatePerkPatch.cs:116,181`: mirror `AcademyPatch.FindAllLiveLecternCards` (iterate every `cmcAcademyLectern` match, backfill each, latch `PlacedStatUid=1` only after ≥1 write). Current single-match self-heal can permanently freeze the player-visible lectern at 0%. | Reliability fix | P0 | Medium |
| **D3 / W3** — `ResolveAgent` null path (`CottageResidentSpawnPatch`, `GuardSpawnPatch`, + InnKeeper/Professor/Apothecary schedule patches): emit a one-shot `LogWarning` naming the unresolved `AgentUid` before returning false. | Diagnosability fix | P0 | Quick |
| **A10 / W2** — demote the 6 shipped `TEMP DIAGNOSTIC` `LogInfo` sites to `LogDebug` (or gate `CompanionFollowDiagnostics` behind a `Config.Bind` flag; delete `AcademyPatch.DumpLecternInstanceIdentity`). | Logging hygiene | P1 | Quick |
| **W4** — reproduce the player-reported professor travel softlock (west→north→professor) in-game to confirm whether the `AlwaysUpdate:false` correction resolved it; CHANGELOG 1.46.6 is explicit this was only a rule-compliance fix, not a root-cause resolution. | Runtime verification | P0 | Medium |
| **A8** — in-game confirm the Iron Rod shows exactly one enhanced "Fish" button (no duplicate vanilla Fish); re-test after any EA game bump. | Runtime verification | P2 | Quick |

---

## Phase 1: Foundation

> Table-stakes hygiene. Mostly already satisfied — kept here as the maintenance baseline.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Versions synchronized (ModInfo/Plugin.cs/README all 1.46.6) | Version hygiene | — | Done |
| Chinese localization present + at parity (SimpCn.csv 1870/1870) | Localization | — | Done |
| Framework Tier 2 (`ContentModPlugin`, `TryApply`, declarative injection) | Framework | — | Done |
| Perk template normalization — backfill `EquippedCardsWarpData/Type`, `AddedCardsWarpData/Type`, `NoSafetyMode` on the 17 compact `Pk_*.json`; remove the out-of-place `StarsCost` from `Perk_Claws.json` | Maintainability (M1/M2) | P2 | Quick |
| Re-run `/critical-analysis Community_Mod_Chest` after the guard/Sterling-dialogue work is fully committed so the adversarial trail covers the arrest-summon subsystem | Audit-trail | P2 | Quick |

---

## Phase 2: Core Expansion

> The most natural next content, all reusing proven CMC chassis.

### Burglar's Kit + "Make it right" restitution (close the crime loop's open ends)
**What**: A Lockpick/Pry Bar CT0 tool that shaves points off the Copper Chest detection roll, and a restitution interaction (drag Salt/copper back onto a robbed chest) that calls a new `ReduceCrime(amount, reason)` — the first voluntary crime-down path.
**Why**: The five-resident Copper Chest sell/rob economy and the Town Watch/crime/jail loop are fully shipped, but crime is currently one-directional (you can only accrue it). These give the player agency over the risk side.
**Requires**: none (reuses `CopperChestPatch`'s `ActionRouter` handler; ~3-line detection read + one item + one blueprint).
**Complexity**: Medium

### Talk action for the remaining three guards
**What**: Extend Captain Sterling's shipped crime-band-varying Talk dialog to Nella Thorne, Old Corrin, and Iris Vane so the whole Watch is approachable, not just the Captain.
**Why**: Sterling's Talk is live; the other three expose only "Attack." Replicating the `CMC_SterlingTalk*` DialogScene wiring makes the guard layer feel uniform.
**Requires**: Sterling's version verified in-game.
**Complexity**: Medium

### Craftable instrument (Clay Ocarina / Bone Whistle) — make Shadow-taming self-sufficient
**What**: A pottery-tier Clay Ocarina or bone-tier Bone Whistle that (a) counts as a valid Shadow-the-Cat taming trigger, and (b) carries a small "Play a Tune" Comfort/Sanity DismantleAction.
**Why**: Shadow — the final requirement to finish the Apothecary's Cabin — is currently tamed ONLY by a *vanilla* Wooden/Bone Flute or Frame Drum. A player who never crafts one has no path to Shadow and therefore no path to complete the Cabin. Appending the new UID to `CMC_ShadowCat.json`'s taming-trigger array closes a real, currently-unstated dependency gap. Pure JSON.
**Requires**: none.
**Complexity**: Quick

---

## Phase 3: Integration & Depth

### Cat care depth — Cat Food Bowl + high-Care payoff
**What**: A Cat Food Bowl self-serve station (the food-side mirror of the 1.34 Rain-Cistern auto-drink), plus a high-Care "Content" tier that occasionally leaves a small gift at camp — making the Care stat two-directional instead of purely punishing.
**Why**: All three cats share the Care chassis (`AshCatTickPatch`, `SpecialDurability3`), which currently only ever drains toward the cat leaving. Reward tending, and give an away-from-camp player a passive food source.
**Requires**: none (small C# copy of the existing auto-drink branch).
**Complexity**: Medium

### Cat auto-drink ↔ WDI water containers (soft-dep)
**What**: When WDI is installed, add its water-container UIDs to `AshCatTickPatch.ExplicitWaterIds` so a WDI water setup doubles as a companion water source; keep the three-vanilla-container fallback.
**Why**: One-array extension that makes the two mods' water systems read as one feature.
**Requires**: WDI installed (silent cross-mod soft-dep).
**Complexity**: Quick

### Village Renown capstone — Founder's Monument or festival
**What**: A one-time visible reward when Village Renown maxes (a Founder's Monument placeable, or a seasonal festival beat), distinct from the continuous Market Stall sales bonus.
**Why**: The Renown meter's only current payoff is a flat 25% Stall bonus; the *full* meter earns no capstone.
**Requires**: none (monument reuses the `VillageRenownPatch` completion hook).
**Complexity**: Medium

---

## Phase 4: Polish

| Item | What | Complexity |
|------|------|------------|
| Town Watch + Jail art | Commission dedicated art for the 4 guard portraits (`CMC_Captain`, `CMC_Guard_LanternGuard`, `CMC_Guard_SpearMan`, `CMC_Guard_SpearWoman`) and the Jail/cell interior — the mod's largest self-disclosed art caveat | Medium |
| Distinct brewed-potion sprite | Give `CMC_ApothecaryHealingPotion` its own sprite instead of sharing the Healing Mixture art (items-report M3) | Quick |
| Resident portraits at more locations | Add stall/cottage-interior portrait variants for the residents who still show a generic portrait there (extends the proven envCard portrait-swap) | Quick |
| Description accuracy pass | Re-verify the large `ModInfo.json` Description against wiring after each guard/jail/copper-chest change (feature-honesty currently PASS) | Quick |

---

## Long-term Vision

By v2.0, Community Mod Chest is effectively a self-contained "living village" layer on top of vanilla CSFF: a settlement whose residents keep believable schedules, an economy the player can trade into or steal from with real consequences, an Academy that gates a progression tree, and a civic-construction arc with a visible capstone — all sitting above a mature supporting catalog of apparel/pottery/comfort/fishing content. The mod is already close to that vision; the remaining distance is depth and finish (two-directional relationship loops, crime agency, capstone rewards, dedicated art) rather than new pillars. The biggest addition that becomes natural at that scale — but is not yet justified — is a unified companion-care system shared with a future AnimalCompanions mod, so the three cats read as first-class members of one system rather than bespoke one-offs.

**Potential major additions** (not yet justified — revisit after Phase 3):
- New Academy course (Culinary or Husbandry) — the course-injection pattern is proven; fits the "living village" theme and pairs with the cat/companion content.
- Apothecary remedy line keyed to CMC's own drawback perks (Allergy Tonic, Stomach Settler, Clotting Salve) — closes the one-directional drawback-trait loop.
- Fish-preservation chain (drying/smoking rack) — a classic survival food gap CMC's fishing tools currently leave open, and doubles as the source of cat fish-treats.

These live in `Documentation/Ideas/Community_Mod_Chest/` (IDEAS.md, DEFERRED_ITEMS.md, VILLAGE_AREA.md, QUALITY_SPLIT.md, Wisp_and_NPCs/) — 10 dated `/generate-ideas` passes already spec most of them.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod Community_Mod_Chest` and update this roadmap |
| Game version update | Run `/update-mod-version`, refresh `lib/Assembly-CSharp.dll` (note: CMC references H&F's `Assembly-CSharp-nstrip.dll` by relative path — regenerate via NStrip on game bump), check CLAUDE.md EA notes, re-run `/diagnose-log` |
| After fixing a critical/reliability issue | Run `/critical-analysis Community_Mod_Chest` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo Community_Mod_Chest` and bump minor version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod Community_Mod_Chest         -- full health check, updates .audit/
/critical-analysis Community_Mod_Chest -- adversarial review
/repair-items Community_Mod_Chest       -- auto-fix item JSON issues
/repair-blueprints Community_Mod_Chest  -- auto-fix blueprint JSON issues
/build-mod Community_Mod_Chest          -- build Release DLL
/deploy-mods Community_Mod_Chest        -- build + deploy to game
/update-mod-version Community_Mod_Chest <ver> -- bump version in all 3 files
/export-to-repo Community_Mod_Chest     -- push to public repo
```
