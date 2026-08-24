# Roadmap: Sirus23 Mod Collection
Version at time of writing: 1.20.2
Date: 2026-08-23
Audit score: 9/10 — PASS (0 CRITICAL, 0 DESIGN GAP, 1 open WARNING — W3 art quality)

## Current State

**Theme**: Animal companions (Wolf / Fox / Owl) plus a full sheep-husbandry + dairy + wool/felt production chain — a mid-game survival-and-livestock mod for players who want a managed flock and a working animal partner.

**Content**: 46 items / 17 blueprints / 4 structures / 9 perks / custom images / declarative animal layer: `Animals/` (Fox, Owl), `Encounter/` + `NPCAgent/` (Wild Fox, Wild Owl), `EncounterGuards/` (Fox, Owl, Wolf).

**Since 1.16.1**: the spinning-wheel refactor cleanup (W2) resolved; the Wild Owl gained a guard-duty suppression (`WildOwlDutyGuardPatch.cs`, keeps the untamed Owl out of caves/mines/interiors during its nightly seek-player duty) and its shared `IndoorOrCaveEnv.cs` UID/tag allowlist (extracted out of `CompanionStayPatch.cs`); 8 real-art PNGs replaced Wild Fox placeholders (`WildFox.png` deleted). `critical-analysis`/`code-quality` both re-ran 2026-08-23 against v1.20.2: **SOLID** / **10/10**, 0 failures — the new Owl guard-duty code and its extracted helper are fully reviewed and clean.

**Stability**: 9/10 — 0 CRITICAL, 0 DESIGN GAP. The prior W1 (`CompanionStayPatch` temp `LogInfo` lines) and W2 (spinning-wheel cleanup) are both resolved. One open item remains: **W3 — `OwlCompanion.png`/`OwlTied.png` are byte-identical** (same 550,413-byte PNG for two different mechanical states — tamed vs. tied/leashed). Not urgent, art-quality only.

**Open work**: None in `Documentation/Retrospectives/INDEX.md` — no 🔴 Open or 🟡 Pending rows match this mod (the `sh-wild-sheep-spawn` retro is graduated/archived).

**Framework compliance**: Tier 2 — strong. Uses `ContentModPlugin` base, `Api.ActionRouter` + `Api.SpawnService` (companion hunt/scout/retrieve), `Api.TickEvents` (companion upkeep), declarative `EncounterGuards/*.json`, and the framework `AnimalLifecycleTicker` driven by declarative `Animals/*.json`. No deprecated patterns (no `DropCollectionGuardPatch`, no `ModLoaderVerison`/`ModEditorVersion`, no unfiltered hot-path prefixes, no transpilers). Only tame-flow glue remains as mod C#, by design (Phase 5 note in-code).

---

## Phase 0: Stabilize  *(light — no CRITICALs or open retros; one open WARNING)*

> Fix before the next public release. Cosmetic, not gameplay-blocking.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **W3 — distinct art for `OwlTied.png`** (currently identical to `OwlCompanion.png`) so the tied/leashed state reads differently from the tamed-companion state | Art | P0 | Quick |
| ~~W1 — demote `CompanionStayPatch`'s LogInfo lines~~ | Log hygiene | — | **RESOLVED** — carried, see summary.md |
| ~~W2 — finish the spinning-wheel refactor cleanup~~ | Feature-honesty / dead-ref fix | — | **RESOLVED 2026-08-23** |
| **In-game verify the new Wild Owl guard-duty suppression** — stand in a cave/mine/interior with the Owl's nightly seek-player duty otherwise eligible, confirm no teleport-in and the log shows `[WildOwlDutyGuard] ... suppressed` | Verification | P1 | Quick |

---

## Phase 1: Foundation

> Table-stakes health. Most are already GREEN for this mod — listed for the maintenance record.

| Item | Type | Status | Complexity |
|------|------|--------|------------|
| Versions synced across ModInfo / Plugin.cs / README (all 1.16.1) | Version hygiene | DONE | — |
| Chinese localization (`SimpCn.csv`, 294/294 keys, parity CLEAN) | Localization | DONE | — |
| Bin/Release sync (0 missing / out-of-date / orphaned) | Build hygiene | DONE | — |
| Framework Tier 2 (ActionRouter / SpawnService / TickEvents / AnimalLifecycleTicker / EncounterGuards) | Framework | DONE | — |
| Run the 5 domain sub-audits (`/full-mod-audit-chain`) — items/blueprints/structures/images/perks reports are STALE (2026-07-27), predating the Sheep Pen / Felt / Fox-tame content | Audit coverage | TODO | Medium |
| Confirm `Pk_SH_CraftingKit`'s `StartUnlocked: true` is intended vs. a partial regression of the v1.13.2 perk fix | Perk audit | TODO | Quick |

---

## Phase 2: Core Expansion

> The most impactful content additions that extend the mod's core loops (all pre-specced in `Documentation/Ideas/`).

### H1 — Salvage the Sheep Remains (close the 1.16.0 predation dead-end)
**What**: Add a "Salvage" interaction to `sh_sheep_remains` (a `tag_Cutter` drag-CI or self-DA) producing scrap wool + hide + raw meat/bone.
**Why**: The predation-loss card 1.16.0 introduced is a true dead-end (empty interactions, rots in ~5 days) — a lost sheep should leave a partial recovery, not just a decaying card. Also closes a dead-end a future `/audit-items` will flag.
**Requires**: none. Copy `OwlCarcass.json`'s "Process Carcass" self-consuming CI verbatim.
**Complexity**: Quick (pure JSON + CSV)

### H2 — Wild Wolf tame path (the last missing companion acquisition)
**What**: `Animals/Wolf.json` + `Encounter/Encounter_WildWolf.json` + `NPCAgent/Agent_WildWolf.json` + a `WildWolfLifecyclePatch.cs` twin of `WildFoxLifecyclePatch`, tamed by offering raw meat, lifecycle owned by `AnimalLifecycleTicker`.
**Why**: The README explicitly defers this ("Wolf remains perk-only for now"). Owl → Fox has now proven the wild-tame template twice, making the Wolf a near-1:1 copy. Ships the wolf-half of the long-standing G1 goal (every companion obtainable without its perk).
**Requires**: none (Fox chain is the template).
**Complexity**: Medium

### #3 — "Tend Flock" herd QoL action on the Sheep Feeder
**What**: A Milk-All / Shear-All herd action on the placed Sheep Feeder, turning a storage-only container into the husbandry hub.
**Why**: With the Sheep Pen now holding up to 4 animals, per-sheep milking/shearing is repetitive; a herd action is the natural payoff. `ActionRouter` + spawn-eject and the Grind-All IEnumerator-wrap pattern are already in-repo.
**Requires**: none.
**Complexity**: Medium

---

## Phase 3: Integration & Depth

### H4 — Pen upkeep: fence weathering + repair
**What**: Give the Sheep Pen a draining "Fence Condition" stat; a worn pen's protection lapses (contents fall back under the nightly roll or a predator breaches it) until repaired with Plank + Rope via a "Mend the Fence" DA.
**Why**: The shipped pen is permanent at zero cost, so pen-or-perish becomes solved the moment one pen exists. Upkeep turns it into a maintained investment. Revives the "Pen Integrity" half of the old #8.
**Requires**: Phase 2 core loop stable.
**Complexity**: Medium

### H3 — Felt outfit set bonus
**What**: Extra Cold Resistance / Warmth (or a Wetness-resistance nod to lanolin waterproofing) only while 3+ `sh_felt_*` pieces are equipped together.
**Why**: The felt line is now a complete six-garment set but wearing it whole is just the flat sum. A set bonus rewards committing to it.
**Requires**: a `ChangeStatValue` C# hook counting equipped felt pieces (no pure-JSON path) — the same design question CMC's apparel-set idea raises; coordinate a shared counting hook.
**Complexity**: Complex

### Cross-mod hooks (all opt-in soft deps; mod works standalone)
- **CMC** — village trading of wool/cheese/dairy (already priced); a butcher/tanner errand accepting `sh_sheep_remains` (H7) for a payout; a shepherd-trainer NPC (G9).
- **WDI** — Fiber Mill wool-washing for higher yarn yield; water-adjacent churn speed-up.
- **H&F** — medicinal herbs for a companion "Treat Wounds" CI (CS5) + herb-infused dairy variants.
- **ACT** — copper/iron Shears tier (`Special4` metal gating already proven on `Bp_Shears`).
- **RA** — verify RepeatAction picks up the mod's milk/shear/churn CIs; if so, recommend RA instead of building "Tend Flock" from scratch.

### CS5 — Companion stat normalization
**What**: Add Thirst (`UsageDurability`) + Health (`SpecialDurability1`) to Fox and Owl (Wolf already has all four), with Give Water / Treat Wounds CIs.
**Why**: The three companions are advertised as one cohesive upkeep system but Fox/Owl carry half the surface.
**Requires**: none. `WolfCompanion.json` CIs are the template.
**Complexity**: Medium

---

## Phase 4: Polish

| Item | What | Complexity |
|------|------|------------|
| Final felt-line art | Replace the 3 placeholder white-card PNGs (`sh_felt_mittens.png` / `sh_felt_vest.png` / `sh_felt_bedroll.png`) with finished illustrations | Quick |
| Dairy-chain art | Replace vanilla-sprite reuse: Buttermilk (`ClayBowl`), Whey (`Milk_Old`), Lanolin (`Fat`) with dedicated PNGs | Quick |
| Sheep sprite restyle (SR3, owner-approved) | Restyle the 6 sheep/ram/lamb sprites to match vanilla animal cards | Medium |
| Balance pass | Tune the Sheep Pen escape/predation rates (self-documented placeholders) and the felt-item stat values after playtest | Medium |
| Description pass | After W2's cleanup, refresh the README feature list to surface the felt line, Lanolin Salve, Sheep Pen, and Wild Fox tame | Quick |

---

## Long-term Vision

At v2.0, Sirus23 Mod Collection should be a complete, self-maintaining livestock-and-companion package: all three companions obtainable both by perk and by wild tame (Wolf being the last gap), a flock that carries genuine ongoing stakes (pen upkeep, distant-flock risk, active sheepdog protection), and a wool/dairy/felt economy deep enough to feed a village trade loop. The single biggest addition not yet justified by current scope is a **second livestock species** (cattle — SR1/SR2's draft-animal traction and cart transport) that reuses the sheep-husbandry chassis ~1:1 and unlocks ploughing and hauling.

**Potential major additions** (revisit after Phase 3):
- **Distant-flock pen-or-perish (H5)** — a catch-up roll on return so leaving a flock unattended anywhere carries the intended risk, not just at camp.
- **Cattle line + animal traction (SR1/SR2)** — copy sheep husbandry for a cow/bull line, then a "Plough" DA and a horse/ox cart; owner-requested.
- **Active sheepdog role (H6)** — extend the wolf's shipped presence-suppression into an active herd-back / distant-flock guard.

These live in `Documentation/Ideas/Sirus23_Mod_Collection/` (IDEAS.md + SHEEP_PEN.md + LANOLIN.md).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod Sirus23_Mod_Collection` and update this roadmap |
| Game version update | Run `/update-mod-version`, refresh `lib/Assembly-CSharp-nstrip.dll` (NStrip — manual), re-run `/diagnose-log` |
| After fixing a warning/critical | Run `/critical-analysis Sirus23_Mod_Collection` to verify |
| Before public release | Run `/full-mod-audit-chain Sirus23_Mod_Collection` (domain sub-audits are stale) + `/export-to-repo` |

---

## Skill Cheatsheet for This Mod

```
/audit-mod Sirus23_Mod_Collection            — full health check, updates .audit/
/full-mod-audit-chain Sirus23_Mod_Collection — preflight + all 6 domain audits + consolidate
/critical-analysis Sirus23_Mod_Collection    — adversarial review
/consolidate-ideas Sirus23_Mod_Collection    — prune shipped N1/N2 from IDEAS.md
/repair-items Sirus23_Mod_Collection         — auto-fix item JSON issues
/build-mod Sirus23_Mod_Collection            — build Release DLL
/deploy-mods -Sirus23ModCollection           — build + deploy to game
/export-to-repo Sirus23_Mod_Collection       — push to public repo
```
