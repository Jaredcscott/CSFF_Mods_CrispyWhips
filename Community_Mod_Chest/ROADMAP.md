# Roadmap: Community Mod Chest
Version at time of writing: 1.67.6
Date: 2026-08-23
Audit score: 9/10 (PASS — 0 CRITICAL, 0 design gap, consolidated 2026-08-23; critical-analysis verdict SHIPS WITH CAVEATS — 1 new low-severity mechanical finding, W9)

## Current State

**Theme**: A community-suggested content grab-bag that has grown a full village-simulation layer on top — apparel/weapons/armor/pottery/comfort items plus a named-resident village east of the River Clearing (Inn Keeper, Miller, Weaver, Apothecary, Professor), a seven-course Academy, a four-guard Town Watch with a Crime/Jail/Banishment loop, a five-chest merchant economy, a Town Achievement Board, and tameable companion cats.

**Content**: 73 items / 77 blueprints / 51 structures / 55 perks / 10 NPCAgents / 112 images (per-file mechanical count, 1.67.0). Declarative systems: `BlueprintTabs.json`, `DropInjections.json`, `InjectImprovementInto.json`, `TradingValues.json`, `MapMod.json`, `WorldMap/MapNodes.json`. `Patcher/*.cs` files.

**Since 1.60.0**: the Town Achievement Board shipped end-to-end but was then **BENCHED** (1.61.0–1.64.0 build, 1.67.3 pulled — a fresh-save playtest found broken in-game presentation; code kept, wiring disabled); the Carpentry course's Clothes Rack/Coat Rack/Wardrobe/Weapon Rack were consolidated into one **Outfit Wardrobe** (three fixed 18-slot sections with per-section Equip/Unequip, 1.65.0); the Inn/Academy/Village Hall fireplace mechanism was reworked onto vanilla's own Fireplace `PassiveEffects` (`VillageFireplacePatch`, 1.65.5–1.66.0); the stray-improvement strip generalized to all 12 WorldMap locations (1.66.0); the Miller/Weaver gained a genuine "at work" engine `NPCDuty` (1.67.0); and a **Village Founder trust-boost subsystem** (`VillageFounderPerkPatch.cs`), an indoor/cave-aware **wild-Owl-adjacent partner indoor-follow rework** (`PartnerIndoorFollowPatch.cs`), a **tree-respawn self-healer** (`TreeRespawnPatch.cs`), a reed-based Hand Wraps recipe rework, and by-hand Snow Drift clearing all shipped through 1.67.6 (all now **committed**, first audit pass complete 2026-08-23).

**Stability**: 9/10 — 0 open CRITICAL, 0 design gaps, 1 low-severity open Warning (W9 — `VillageFounderPerkPatch`'s unchecked latch write, `.audit/summary.md`). Both findings the 2026-08-19 audit raised (Carpentry→Outfit Wardrobe unlock table; Weaver hemp recipe trivializing on a no-H&F install) are confirmed fixed and committed. Critical-analysis verdict **SHIPS WITH CAVEATS** (2026-08-23, the one new W9 finding). Acquisition coverage clean (0 unreachable / 0 dead-end). Full audit trio (mod-report/critical-analysis/code-quality) re-ran 2026-08-23 against committed v1.67.6.

**Open work**:
- `river-bridge` — INDEX.md still shows 🔴 Open, but the retro file itself was downgraded to 🟡 Pending Verification on 2026-08-15 and its underlying fix (removing the crime-based travel lock) shipped in 1.58.0 — the INDEX row is stale, not the mod. A `/resolve-retro` pass would clear this, the sole point keeping the score off 10.
- `guard-kill-despawn` — 🟡 Pending. Event-driven despawn-latency fix (via `GameManager.OnEncounterEnemyDefeated`) shipped 1.54.3; not re-verified in-game.
- `CMC-HSP-compat` — 🟡 Pending. Structural fix (dropped to 2 overlapping kit perks, 1.52.0) confirmed for new characters; one legacy pre-1.52.0 save case unconfirmed.
- (Dormant, framework-scoped: `questinjector-blueprint-reset-risk`, `RETRO_CLOSURE_PLAN_2026-07-23` — confirmed via grep that CMC no longer references `QuestInjector`/`Quests.json` at all; not CMC's problem.)

**Framework compliance**: **Tier 2** — extensive adoption across ~48 C# files (`ActionRouter`, `SpawnService`, `TickEvents`, `ContentModPlugin` base, `Api.EncounterGuards`, declarative `WorldMap`/`DropInjections`/`InjectImprovementInto`/`TradingValues`). No deprecated patterns (`DropCollectionGuardPatch` absent; no unfiltered hot-path prefixes; no `ModLoaderVerison`/`ModEditorVersion`). Single startup LogInfo line via the shared base class.

---

## Phase 0: Stabilize

> Audit score is 9/10 with 0 open CRITICAL/design gaps — this phase is now housekeeping, not bug-fixing. Do these before the next release so nothing is at risk of loss.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| ~~Commit the uncommitted 1.67.0 work~~ | Git hygiene | — | **DONE 2026-08-23** — v1.67.6 |
| Fix W9 — guard `VillageFounderPerkPatch.cs:163-164`'s unchecked latch write (`if (WriteStat(...)) LogInfo else LogWarning`) | Code robustness | P0 | Quick |
| ~~`/resolve-retro river-bridge`~~ | Retro close | — | **DONE 2026-08-22** |
| Playthrough pass to close `guard-kill-despawn` + `CMC-HSP-compat` via `/resolve-retro` (shipped fixes awaiting a confirming in-game check) | Retro close | P1 | Medium |
| In-game verify: Carpentry→Outfit Wardrobe unlock, Weaver no-H&F degrade, `PartnerIndoorFollowPatch` door-crossing (never confirmed in-game per its own CHANGELOG), `TreeRespawnPatch` duplicate-tree trim | Verification | P1 | Medium |
| Decide the fate of the 2 TEMP diagnostic patches (`GuardCombatDiagnosticPatch`, `CompanionFollowDiagnostics`) — remove or demote `LogInfo`→`LogDebug` once confirmed against a live log | Cleanup | P2 | Quick |
| Refresh `items-report.md`/`blueprints-report.md`/`structures-report.md`/`perks-report.md` (still 1.45.0–1.59.3-era) via `/full-mod-audit-chain` or individual `/audit-*` skills | Audit coverage | P2 | Medium |

---

## Phase 1: Foundation

> Table-stakes hygiene. Mostly already healthy — the remaining items are small consistency fixes.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Versions synchronized (1.67.0 across ModInfo/Plugin.cs/README) | Version hygiene | — | DONE |
| Chinese localization (`SimpCn.csv`, 2177/2177 keys, CLEAN parity) | Localization | — | DONE |
| Add the missing `LocalizationKey` on `CMC_MarketStall.json` CardName (untranslated name for CN players — confirmed the only Location card in the mod without one) | Localization | P1 | Quick |
| Normalize the 17 compact `Pk_*.json` perks (backfill `EquippedCards/AddedCards WarpData/Type` + `NoSafetyMode`); confirm/remove `Perk_Claws.json`'s trailing undisclosed `StarsCost: 1` | Perk template hygiene | P2 | Quick |
| Tidy the batch of `ParentObjectID` mismatches in `CMC_JailCellBed.json` (non-load-bearing, cosmetic) | JSON hygiene | P2 | Quick |
| ~~First audit pass of the 1.67.0 work~~ | Audit coverage | — | **DONE 2026-08-23** — mod-report/critical-analysis/code-quality all re-ran against committed v1.67.6 |

---

## Phase 2: Core Expansion — DONE (1.60.0)

> All five items below shipped in the 1.60.0 batch and are pending only an in-game playthrough pass (same unverified state as the rest of this session's work — see Phase 0).

| Item | Shipped as |
|------|------------|
| Generalize Dress/Undress to the Wardrobe | Superseded 2026-08-17: Clothes Rack, Coat Rack, Wardrobe, and Weapon Rack & Armor Stand were merged into one **Outfit Wardrobe** (`cmcOutfitWardrobe`) with three fixed 18-slot outfit sections and per-section Equip/Unequip DAs, replacing `ClothesRackEquipService.cs` with `OutfitWardrobeEquipService.cs` — this is effectively the "named outfit presets" idea this row said was out of scope |
| Wire the 5-chest economy into Trust | Every sale bumps the resident's Trust (`cmcStat<Resident>Trust` NPCStat for Miller/Weaver/Apothecary/Professor — the Apothecary's stat is new this pass; the Inn Keeper's sale bump writes his pre-existing friendship stat instead). The PurseTier board-status idea was not built |
| First voluntary crime counter-play | Burglar's Kit (detection-roll discount while carried) + Miller-only "Make It Right" restitution CI, both live in `CopperChestPatch.cs` |
| Talk for Thorne / Corrin / Vane | Three new `DialogScene`/`DialogLine` pairs, each in a distinct register, plus a Talk `Interactions[]` entry on each guard's NPCAgent |
| Clay Ocarina | New pottery-tier instrument, appended to `CMC_ShadowCat.json`'s taming CI trigger list, plus an optional flavor "Play a Tune" DA |

### Also shipped, 1.61.0–1.67.0 (post-plan, incremental)

| Item | Shipped as |
|------|------------|
| Town Achievement Board | 11 tracked feats in the Village Inn — `AchievementBoardSeedPatch`/`AchievementTrackerPatch`/`AchievementKillEffectsPatch` (1.61.0–1.64.0); detection code-complete, full playthrough confirmation still pending |
| Furniture consolidation | Clothes Rack/Coat Rack/Wardrobe/Weapon Rack merged into one **Outfit Wardrobe** (3×18-slot sections, per-section Equip/Unequip) — `OutfitWardrobeEquipService.cs`/`OutfitWardrobeSectionsPatch.cs` (1.65.0) |
| Fireplace mechanism rework | `IndoorHeatCapPatch`/`InnFireplacePatch` (forced stat patch) deleted, replaced by `VillageFireplacePatch.cs` driving vanilla's own Fireplace `PassiveEffects` (1.65.5–1.66.0) |
| Stray-improvement strip generalized | `VillageStrayImprovementsPatch` (Village-only) → `StrayImprovementsPatch` (all 12 WorldMap locations), fixing a Pine Trail travel softlock the Village-only version didn't cover (1.66.0) |
| Miller/Weaver "at work" duty | Engine `NPCDuty` chassis (same one Village Guards' patrol uses) keeps them at the Village during the day with a genuine profession action (grinding/weaving) instead of a cosmetic wander roll, plus a small daily trade-stock top-up — `CottageResidentWorkDutyPatch.cs` (1.67.0, **uncommitted**) |

---

## Phase 3: Integration & Depth

> Cross-mod hooks and late-game progression for experienced players.

### Metal weapon tier gated behind the Armorer course
**What**: A copper/iron weapon (copper mace-head / iron spearhead) gated on `Pk_GradArmorer`.
**Why**: The Armorer course grants copper/iron *armor* but weapons top out at fire-hardened/bone — the metal weapon the armor-only course implies. Pairs with a Sparring Post + the Outfit Wardrobe.
**Requires**: AdvancedCopperTools (now a **HARD dependency** as of 1.56.0 — lean on ACT UIDs directly, no fallback).
**Complexity**: Medium

### Player-side remedy station keyed to CMC's drawback perks
**What**: An Apothecary Workbench / Mortar & Pestle (CT2, gated behind Herbalism/Medicine) producing 2–3 targeted remedies (Allergy Tonic, Stomach Settler, Clotting Salve) that answer CMC's own rich drawback roster (`Bleeder`, `SeasonalAllergies`, `WeakStomach`, …).
**Why**: All medicine-making today is the *Apothecary's* NPC schedule; the player has no station to compound the remedies the Medicine/Herbalism courses point at. The concrete consumer for the CMC↔H&F herbal pairing.
**Requires**: HerbsAndFungi for potent inputs; framework station-blueprint rules (`ContainedBlueprintCardsWarpData`).
**Complexity**: Complex

### Reputation-scaled trade prices + a Village Reputation capstone
**What**: Graduate Reputation's single flat 25% Market Stall bonus into a curve (better Stall/Inn prices as Reputation rises; per-resident discounts as Trust climbs); and give the full meter a one-time reward (Founder's Monument placeable or a festival beat).
**Why**: Reputation is now visible on the Mental tab but rewards little beyond the one latch; a continuous curve + a capstone completes the civic-growth arc.
**Requires**: `VillageReputationPatch` already computes a multiplier; avoid double-counting the 25% latch.
**Complexity**: Medium–Complex

---

## Phase 4: Polish

> Art and text that make the mod feel finished.

| Item | What | Complexity |
|------|------|------------|
| Town Watch + Jail art | Replace the blank/borrowed placeholder art on the 4 guard portraits (`CMC_Captain`, `CMC_Guard_*`) and the Jail/cell interior — the mod's largest self-disclosed honesty caveat | Medium |
| Distinct brewed-potion sprite | Apothecary Healing Mixture and Healing Potion share one sprite; a distinct finished-potion sprite differentiates intermediate vs finished | Quick |
| Commit `CMC_MarketStallDressed.png` | Awning/bunting "dressed" Market Stall presentation was generated 2026-08-22 (untracked) — only remaining step is committing it; the image itself is done | Quick |

---

## Long-term Vision

> Where this mod should be at v2.0.

Community Mod Chest has outgrown its "grab-bag" origins into a village-life simulation that rivals the base game's own settlement content: a living economy where resident savings circulate, a fully-realized crime axis (voluntary redemption, bribes, robbing civic structures, a lawful "join the Watch" path, jail labor, a fence for hot goods), and seasonal communal events (a snowed-in winter beat, a visiting trade caravan, harvest festivals). The natural v2.0 endpoint is a village that feels *inhabited whether or not the player is looking* — NPCs spending and restocking each other, seasonal rhythms the player plans around, and a Potter resident completing the craft-family roster so every content pillar has a village face.

**Potential major additions** (not yet justified — revisit after Phase 3):
- **A Potter resident + Pottery Kiln station** — pottery is CMC's largest content family yet the only pillar with no village NPC; a firing station would add quality/throughput.
- **Living economy — circulating resident savings + a visiting trade caravan** — chests currently only fill to a ceiling and freeze; make them ebb and refill, and bring outside goods to the valley seasonally.
- **The full constructive/illicit crime economy** — the redemption/bribe/fence/lawful-Watch axes §10.8 deliberately omits, built on the guard/Suspicion chassis already shipped.

These live in `Documentation/Ideas/Community_Mod_Chest/` (IDEAS.md + sibling specs `DEFERRED_ITEMS.md`, `VILLAGE_AREA.md`, `QUALITY_SPLIT.md`, `PERK_AloneInTheWorld.md`, benched `Wisp_and_NPCs/`).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod Community_Mod_Chest` and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes (currently EA 0.66i), regenerate the nstrip lib, re-run `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis Community_Mod_Chest` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo Community_Mod_Chest` and bump minor version |
| Before every deploy | Confirm `bin/Release` is freshly built (the W1 stale-art class recurs) |

---

## Skill Cheatsheet for This Mod

```
/audit-mod Community_Mod_Chest         — full health check, updates .audit/
/critical-analysis Community_Mod_Chest — adversarial review
/repair-structures Community_Mod_Chest — auto-fix structure JSON issues
/repair-perks (via edit-perk)          — perk template normalization
/build-mod Community_Mod_Chest         — build Release DLL
/deploy-mods Community_Mod_Chest       — build + deploy to game
/update-mod-version Community_Mod_Chest <ver> — bump version in all 3 files
/export-to-repo Community_Mod_Chest    — push to public repo
```
