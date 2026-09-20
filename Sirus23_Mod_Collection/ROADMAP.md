# Roadmap: Sirus23 Mod Collection
Version at time of writing: 1.20.5
Date: 2026-09-05
Audit score: 9/10 - PASS (0 CRITICAL; 1 open Design Gap - D1 OwlTied art; 3 Warnings - W3/W4/W5; 4 Minor)

## Current State

**Theme**: Animal companions (Wolf / Fox / Owl) plus a full sheep-husbandry, dairy and wool/felt
production chain - a mid-game survival-and-livestock mod for players who want a managed flock and a
working animal partner.

**Content**: 46 items / 17 blueprints / 4 structures (CT2) / 1 liquid / 9 perks (8 selectable + the
runtime-granted Pack Bond) / 46 custom images / 2 spawn triggers. Declarative layer: `Animals/`
(Owl.json only - the test-only `TestHare.json` was stripped in v1.20.6, closing M4),
`NPCAgent/Agent_WildOwl.json`, `EncounterGuards/`
(**all 3 present again** - Wolf 100%, Owl 35%, Fox 35%), `GameSourceModify/VanillaFox_Agent{1,2}.json`
(the vanilla-agent fox tame path), `CardData/Trigger/` (wild sheep + ram spawn). 9 C# patch classes.

**Since 1.20.3**: **v1.20.4 (`ead6e0f55`, 2026-09-02) closed W1** - `EncounterGuards/FoxGuard.json`
was restored verbatim after the 1.20.3 fox rework deleted it as collateral, with a CHANGELOG
regression-window disclosure and a README parity line. **v1.20.5 (`7e29c94cb`)** added
`AlwaysUpdate: true` to the Sheep Milk CT9 liquid so it keeps spoiling while the player is away,
matching the EA 0.67e vanilla fix. The only later commits (`7e8f66ab0`, `fc7f3db63`) touched nothing
but `lib/Assembly-CSharp.dll` for the EA 0.67g/0.67h game-data refreshes.

**Stability**: 9/10 - 0 CRITICAL, 0 broken loops, 0 broken promises. One open Design Gap (**D1** -
`OwlCompanion.png` and `OwlTied.png` are byte-identical, MD5-reconfirmed 2026-09-05, so the tamed and
snared states are indistinguishable and both break the white-background convention) and three
Warnings: **W3** the Fox rework is built-not-verified in-game, **W4** `CompanionStayPatch` still emits
2 `LogInfo` diagnostics per Owl transition **even though their blocking precondition is now met**, and
**W5 (new)** the shared nstrip DLL this mod compiles against is 70 days stale.

**Open work**: **None in `Documentation/Retrospectives/INDEX.md`** - no open or pending row names this
mod, its `SH`/`Sirus` shorthand, or its plugin GUID. (One row mentions a *player* named Sirus23
reporting a Community_Mod_Chest bug - unrelated. The `sh-wild-sheep-spawn` and
`owl-taming-it-is-still-trade` retros are graduated/archived.) The real backlog is verification, not
bugs: **11 of 25 playthrough items are pending**, including `T2.99` (fox tame), `T2.154` (FoxGuard
load-log), `T2.150` (card-removal primitive), `T2.111` (wild owl M6 tame), `T2.101` (sheep pen) and
`T2.112` (Tend Flock). 13 are PASS, 1 skipped.

**Framework compliance**: **Tier 2 - strong, no gaps.** `ContentModPlugin` base; `Api.ActionRouter`
(12 call sites), `Api.SpawnService` (11), `Api.TickEvents` (5), `CardUtil` (61), declarative
`EncounterGuards/*.json`, declarative `Animals/*.json` + `AnimalLifecycleTicker` (Owl), and
`GameSourceModify/` for the fox. Scan for deprecated patterns returns **clean**: no
`DropCollectionGuardPatch`, no `ModLoaderVerison`/`ModEditorVersion`, no transpilers, no unfiltered
hot-path prefixes. Only tame-flow glue remains as mod C#, by design - the Fox and Wolf still run the
older `WolfTickPatch` upkeep model rather than the framework Animals agents (G8, deliberately held).

---

## Phase 0: Stabilize  *(light - no CRITICALs, no open retrospectives; one real runtime risk)*

> Fix before the next public release.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **W5 - regenerate the shared `Assembly-CSharp-nstrip.dll`.** `Sirus23_Mod_Collection.csproj:33-35` binds `Assembly-CSharp` to `..\HerbsAndFungi\lib\Assembly-CSharp-nstrip.dll`, dated **2026-06-27** while the game is on **EA 0.67h**. An nstrip variant cannot be fixed by copy - it must be regenerated with the user external NStrip tool, then every dependent mod rebuilt so a changed signature fails at compile time instead of throwing `MissingMethodException` at runtime. Fleet-level: 7 of 9 content mods share this DLL; Sirus23 has no local copy. | Game-version hygiene | **P0** | Medium (needs the user NStrip tool) |
| **W4 - demote the 2 `CompanionStayPatch` `LogInfo` diagnostics** (`:117`, `:122`) to `LogDebug`. **The block is gone**: the Owl biome-gated follow was confirmed in-game (`T2.75` = PASS), and no retrospective names these lines as a confirmation signal, so the CLAUDE.md demotion guard does not apply. | Log hygiene | P0 | Quick |
| **D1 - distinct art for `OwlTied.png`** (owl caught in a snare, one leg bound), white-background house style; ideally re-do `OwlCompanion.png` to match. Prompts staged in `.audit/ideas.md`. Precedent: the Wild Fox placeholder-to-real-art swap (v1.20.1). | Art | P0 | Quick |
| ~~M4 - `Animals/TestHare.json` disposition~~ | Housekeeping / feature honesty | - | **RESOLVED v1.20.6** (stripped; operator hit it in a live game. Its `Approach` button was also silently falling through to the vanilla `Combat_EncounterDuck`) |
| ~~W1 - Fox Companion encounter suppression~~ | Feature honesty | - | **RESOLVED v1.20.4** |
| ~~W2 - spinning-wheel dead references~~ | Dead-ref fix | - | **RESOLVED v1.20.0/1.20.2** |

---

## Phase 1: Foundation

> Table-stakes health. Most are GREEN - listed for the maintenance record.

| Item | Type | Status | Complexity |
|------|------|--------|------------|
| Versions synced across ModInfo / Plugin.cs / README (all **1.20.5**) | Version hygiene | DONE | - |
| Chinese localization (`SimpCn.csv`, 324/324 keys, parity CLEAN) | Localization | DONE | - |
| Bin/Release sync (0 missing / out-of-date / orphaned) | Build hygiene | DONE | - |
| Framework Tier 2 (ActionRouter / SpawnService / TickEvents / CardUtil / Animals / EncounterGuards / GameSourceModify) | Framework | DONE | - |
| No deprecated patterns (no `DropCollectionGuardPatch`, no `ModLoaderVerison`/`ModEditorVersion`, no transpilers) | Framework | DONE | - |
| **Domain sub-audit refresh** - `items`/`blueprints`/`structures`/`images`/`perks` reports are all dated **2026-07-27** (v1.5.3-v1.13.x) and predate the Sheep Pen, Felt line, dairy dishes and Fox rework entirely. Run `/full-mod-audit-chain Sirus23_Mod_Collection`. | Audit coverage | **TODO** | Medium |
| **W3 - in-game verify the Fox rework** (`T2.99`): tame from both `Agent_Fox1` and `Agent_Fox2`; confirm `fc_fox_companion` spawns at full stats, the wild instance retires (`AgentExists`->0) to the Spirit World with no interactivity gap, and no duplicate fox remains. Plus `T2.154`: confirm the load log registers **3** encounter guards. | Verification | P1 | Quick |
| **`T2.150` - re-test every card-removal-dependent behavior.** `CardUtil.TryRemoveCard` was a silent no-op (bare `Invoke` on the `IEnumerator DestroyCard` coroutine) until framework **2.25.22**. `SheepPenPatch` (predation/escape) and `WolfTickPatch` (starvation death, morale departure) are 2 of the 4 fleet-wide dead call sites. Nothing that depends on a card actually leaving the board has been observed working since the fix - this is the single highest-leverage verification item for the mod. | Verification | **P1** | Medium |
| **Clear the pending-verification backlog** - 11 of 25 playthrough items are pending, several for shipped headline features (`T2.111` wild owl M6 tame, `T2.101` sheep pen, `T2.112` Tend Flock, `T2.102` felt products, `T2.133` owl cave suppression, `T3.40` sheep-milk spoilage). Batch them into one husbandry-focused play session. | Verification | P1 | Complex |
| M1/M2/M3 polish - the `LanolinSalve` dead `SpoilageTime` config, the dead `WolfTickPatch.HasOwlCompanion()` + 2 stale `WildOwlLifecyclePatch` doc references, cosmetic perk-field gaps | Cleanup | TODO | Quick |

---

## Phase 2: Core Expansion

> The most impactful content additions that extend the mod core loop. All three close a real
> dead-end or an asymmetry the audit can already see, so they pay for themselves twice.

### ~~Close the byproduct dead-ends (H1 Salvage + NEW3 Feather sink)~~ - **DONE v1.21.0**
**Shipped 2026-09-07.** "Salvage Remains" CI on `sh_sheep_remains` (2x Wool + Fresh Hide +
2x Raw Meat + Bones) and the Feather Pillow (12 Feathers + 1 Woven Cloth + 4 Twine, +10
Comfort). Acceptance `T2.201` / `T2.205`. Original entry below for the record:

### Close the byproduct dead-ends (H1 Salvage + NEW3 Feather sink)
**What**: A "Salvage" interaction on `sh_sheep_remains` (scrap wool + hide + raw meat/bone), and a
down-bedding item consuming the 6x Feathers the owl carcass yields.
**Why**: Both are confirmed-open dead-ends on disk - `SheepRemains.json` has **0 CardInteractions, 0
DismantleActions, `TradingValue: 0`** and rots in ~5 days, and Feathers have **zero in-mod consumers**
(referenced only by `OwlTracks.json`, which produces them). Every loss the mod inflicts currently ends
in a card the player can do nothing with. Salvaging turns pen-or-perish from a flat tax into a
setback, which is the difference between a punishing system and a fair one.
**Requires**: none. Copy `OwlCarcass.json` self-consuming "Process Carcass" CI (`tag_Cutter` ->
`ModType: 3` -> `ProducedCards`) and `WoolBlanket.json`/`FeltBedroll.json` for the bedding stats.
**Complexity**: Quick (pure JSON + CSV, plus one sprite for the bedding item)

### ~~Finish the dairy chain payoff (NEW1 cultured dishes + preservation branch)~~ - **DONE v1.21.0**
**Shipped 2026-09-07.** All four cultured dishes plus Salted Butter / Smoked Cheese;
the cheese-cloth "Strain Curds" second use landed in the same pass. The dairy
chain's payoff now covers 11 of 11 items. Acceptance `T2.202` / `T2.203` / `T2.204`.
Original entry below for the record:

### Finish the dairy chain payoff (NEW1 cultured dishes + preservation branch)
**What**: Extend the shipped drag-onto-a-vanilla-dish `CardInteraction` pattern to the four cultured
products (`sh_yogurt`, `sh_sour_cream`, `sh_ricotta`, `sh_buttermilk`), and add a separate
preservation branch (drag Salt onto Butter -> Salted Butter; a fire/smoke source onto Sheep Cheese ->
Smoked Cheese) yielding longer-`SpoilageTime` variants.
**Why**: The v1.17.0 Prepared Dairy Dishes gave a second use to **3 of 11** dairy items. A grep
confirms the four cultured products are each referenced only by their own file and their producer -
they are eat-raw terminals. And the whole chain still has no preservation step, so every dairy
product is a race against the clock. These are the two halves of "the dairy chain has depth."
**Requires**: none. `Butter.json` "Butter the Roots" CI is the verbatim template.
**Complexity**: Medium (8-10 new CIs + 2-6 new cards, JSON + CSV)

### H2 - Wild Wolf tame path (the last missing companion acquisition) - **BLOCKED on `T2.99`, and now on `T2.207`**
**Not built 2026-09-07, deliberately.** The prerequisite below is unmet: `T2.99` is a
recorded FAIL. `T2.207` was filed as the cheap vanilla-only discriminator that decides
whether the cause is our data or the NPC drag-and-drop mechanism itself. See the Plan
Reconciliation Log at the end of this file. The hard constraint is unchanged:

### H2 - Wild Wolf tame path (the last missing companion acquisition)
**What**: Let a player tame a wild wolf without the Wolf Friend perk, by offering raw meat.
**Why**: Owl and Fox are both tameable in the wild; the Wolf is still perk-only and the README says so
explicitly. This closes the mod own stated "every companion obtainable without its perk" goal.
**Requires**: **A HARD CONSTRAINT, not a preference** - this MUST extend the real vanilla wolf agents
(`Agent_Wolf`, `Agent_WolfPack`, `Agent_PrimevalWolf`) via `GameSourceModify`, exactly as v1.20.3 did
for the fox. The v1.15.0 fox shipped a *separate* `sirus_fox` species and put two foxes in the world
doing the same job; the user rejected it outright and it cost a full rework. `GameSourceModify/`
currently holds only the two fox files. Do this after `T2.99` proves the fox pattern in-game.
**Complexity**: Medium

---

## Phase 3: Integration & Depth

> Systems that make existing content matter more, plus cross-mod hooks. All opt-in soft deps - the
> mod works standalone.

### G4 - Make the Sheep Feeder matter (feeding -> yield)
**What**: A clamped Condition/Nourishment `SpecialDurability` on sheep that drains over time, refills
on feeding, and gates or scales wool/milk output.
**Why**: Today `sh_lactating_sheep` produces milk and `sh_tame_sheep` grows wool whether or not the
player ever feeds them - the feeder is cosmetic storage with a batch-harvest button bolted on. This is
the difference between owning livestock and owning a wool dispenser.
**Requires**: A **clamped marker, never an accumulator** (CLAUDE.md errand antipattern - an unclamped
counter inflates past its own gate and silently hides the action forever). Decide binary gate vs.
scaled multiplier first.
**Complexity**: Medium (Complex if scaled - needs a runtime hook)

### H4 - Pen upkeep: Fence Condition + repair
**What**: A draining "Fence Condition" on the Sheep Pen; a worn pen lapses (contents fall back under
the nightly roll or a predator breaches it) until repaired with Plank + Rope via a "Mend the Fence" DA.
**Why**: The pen is permanent at zero cost, so pen-or-perish is *solved forever* the moment one pen
exists. Upkeep keeps the mod central tension alive past the early game. Revives the Pen Integrity
half of `SHEEP_PEN.md`.
**Requires**: Decide the weathering rate, the repair cost, and whether a lapsed pen reverts contents
to the roll or spills them out.
**Complexity**: Medium

### Cross-mod hooks (producer half already priced)
**What**: CM1 - WDI Fiber Mill wool-washing producing `sh_clean_wool` for higher yarn yield.
CM3/CM10 - ACT copper/iron Shears tier and copper dairy tooling, gated on `Special4` metal type
(already proven on `Bp_Shears`). CM2 - H&F herbs as the reagent for a companion "Treat Wounds" (CS5).
CM5/H7 - CMC village trading of wool/cheese/dairy and a butcher errand accepting salvaged
`sh_sheep_remains`.
**Why**: 30 items already carry NPC trading values (v1.5.3) - the producer half of the CMC hook is
done and idle. H7 depends on H1 shipping first.
**Requires**: Coordination with each partner mod version; consume by UID **with a vanilla-GUID
fallback**, as the shipped Lanolin Salve does with Frostleaf Powder.
**Complexity**: Medium per hook

### H3 - Felt outfit set bonus
**What**: An extra Cold Resistance / Warmth bonus while 3+ `sh_felt_*` pieces are equipped.
**Why**: The felt line is a complete six-garment set, but wearing it whole is just the flat per-piece
sum - there is no reward for committing to it.
**Requires**: No pure-JSON path; needs a `ChangeStatValue` C# hook counting equipped felt pieces
(CLAUDE.md Runtime Stat Change Hook). **Coordinate one shared counting hook with CMC apparel-set
idea rather than building two.** Note the single-composition-point rule: a new bonus must be added to
the early-out guard as well as the composition body, or it is silently dead when it is the only one on.
**Complexity**: Complex

---

## Phase 4: Polish

| Item | What | Complexity |
|------|------|------------|
| **`OwlTied.png` (D1)** | Distinct snared-owl art; currently byte-identical to `OwlCompanion.png` | Quick |
| `OwlCompanion.png` | Re-do to the white-background house style (currently an off-house forest scene) | Quick |
| Dairy sprite pass | Three dairy items still wear vanilla sprites: Buttermilk = `ClayBowl`, Whey = `Milk_Old`, Lanolin = `Fat` | Medium |
| SR3 - sheep art restyle | Restyle the 6 sheep/ram/lamb sprites to match vanilla animal cards (`sh_wild_sheep`, `sh_wild_male_sheep`, `sh_tame_sheep`, `sh_male_sheep`, `sh_lactating_sheep`, `sh_lamb`). Batch with the dairy pass. | Medium |
| `Animals/Owl.json` balance pass | 6+ inline "TUNABLE, NOT PLAYTESTED" markers on the tracks reveal curve, trap wariness/catch-split, aggression duty weight and tame success curve - resolve them after `T2.111` | Medium |
| Balance-placeholder pass | README explicitly flags the pen escape/predation rates, the dairy-dish nutrition values, and Felt Mittens/Vest/Bedroll stats as "initial guesses, not final-tuned" - close these out once the pending playtests land | Medium |

---

## Long-term Vision

At v2.0 this should be the definitive livestock-and-companion mod for CSFF: a flock you actively
manage rather than harvest, and three companions with real personalities and upkeep. The pieces are
almost all shipped - what is missing is *consequence*. Feeding should change yield (G4), pens should
wear out (H4), losses should leave something salvageable (H1), and the wolf should be obtainable the
way the fox and owl already are (H2). Get those four in and the mod stops being a content pack and
starts being a system.

The nearer-term discipline problem is verification, not features: 11 pending playthrough items across
shipped headline content, and a framework card-removal bug (`T2.150`) that may have silently disabled
predation, escape and companion death for an unknown period. **Prefer one husbandry playtest session
over one more content phase.**

**Potential major additions** (not yet justified - revisit after Phase 3):
- **SR1 - Animal traction (cattle line + ploughing)** - the owner-requested direction. Needs a cattle
  line first (copy sheep husbandry ~1:1), then a "Plough" DA gated on a draft bull. Bull must be a
  bonus, never a gate on vanilla farming.
- **NEW7 - Predator counterplay** - have a predation kill also drop a track card leading to a
  generated encounter with the culprit, turning a loss into a revenge hunt (feeds N6 wolf-pelt cloak).
  Reuses the Tracks + Encounter plumbing the Wild Owl already proves. Makes predation read as a
  two-way threat rather than a silent tax.
- **G7 - Companion combat assist instead of full suppression** - the Wolf currently *removes* wildlife
  encounters entirely (100%), which reads as an off-switch. A combat advantage in fired encounters
  would make the companion a defender instead.
- **H5/H6 - Distant-flock pen-or-perish and the wolf as an active sheepdog** - the nightly roll only
  evaluates the player current environment, so parking a flock somewhere unvisited is the safest
  play. Needs per-env flock tracking latched into a hidden GameStat (`reference_allcards_env_scoped`).

Specs live in `Documentation/Ideas/Sirus23_Mod_Collection/` (`IDEAS.md`, `SHEEP_PEN.md`, `LANOLIN.md`).

> **Guardrail**: this is a third-party author mod (Sirus, with Jared assisting). **Sirus has final
> say on direction - everything in Phases 2-4 is additive and none of it is required.** Phase 0 and
> the verification items in Phase 1 are the only rows that are genuinely owed.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod Sirus23_Mod_Collection` and update this roadmap |
| **Game version update** | Run `/update-mod-version`; **regenerate `Assembly-CSharp-nstrip.dll` with NStrip and rebuild** (W5 - a stale nstrip compiles clean and throws at runtime); re-run `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis Sirus23_Mod_Collection` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo Sirus23_Mod_Collection` and bump the minor version |
| After any in-game session | Record outcomes with `/playthrough-test-plan record` - 11 items are pending and only a human can advance them |

---

## Skill Cheatsheet for This Mod

```
/audit-mod Sirus23_Mod_Collection         - full health check, updates .audit/
/full-mod-audit-chain Sirus23_Mod_Collection - refresh the 5 stale domain sub-audits (P1)
/critical-analysis Sirus23_Mod_Collection - adversarial review
/repair-items Sirus23_Mod_Collection      - auto-fix item JSON issues
/build-mod Sirus23_Mod_Collection         - build Release DLL
/deploy-mods Sirus23_Mod_Collection       - build + deploy to game
/playthrough-test-plan record             - advance the 11 pending verification items
/update-mod-version Sirus23_Mod_Collection <ver> - bump version in all 3 files
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
