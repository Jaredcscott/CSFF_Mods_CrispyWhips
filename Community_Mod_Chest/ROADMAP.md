# Roadmap: Community Mod Chest
Version at time of writing: 1.68.1
Date: 2026-08-24
Audit score: 9/10 (PASS — 0 CRITICAL, 0 design gap; consolidated 2026-08-24; critical-analysis verdict SHIPS WITH CAVEATS — 1 low-severity mechanical finding, W9, plus 1 cosmetic Chinese-anchor E23)

## Current State

**Theme**: A community-suggested content grab-bag that has grown a full village-simulation layer on top — apparel/weapons/armor/pottery/comfort items plus a named-resident village east of the River Clearing (Inn Keeper, Miller, Weaver, Apothecary, Professor), a seven-course Academy, a four-guard Town Watch with a Crime/Jail/Banishment loop, a five-chest merchant economy, and tameable companion cats. (The Town Achievement Board was built then **benched** in 1.67.3 — code kept, wiring disabled, correctly absent from player-facing docs.)

**Content**: 73 items / 77 blueprints / 51 structures / 58 perks / 10 NPCAgents / 111 images (per-file mechanical count, 1.68.1). Declarative systems: `BlueprintTabs.json`, `DropInjections.json`, `InjectImprovementInto.json`, `TradingValues.json`, `MapMod.json`, `WorldMap/MapNodes.json`, `EncounterGuards/*.json`. ~50 `Patcher/*.cs` classes.

**Since 1.67.6**: two commits landed — **1.68.0** added the **Quiet Village** accessibility/performance trait (`QuietVillagePerkPatch.cs`: Miller/Weaver/Apothecary/Professor stay home/at the Academy/at the Inn and none of the four Town Watch guards take up posts, trading roaming/pursuit content for reduced simulation overhead — uses the `NPCDuty` perk-suppress weight pattern); **1.68.1** shipped an **NPC scheduler performance pass** (part of the chiweichiwei player-triage response, alongside framework 2.25.7). Neither introduced a new CRITICAL, design gap, or preflight regression. The `CMC_MarketStallDressed.png` art (awning/bunting "dressed" stall) is now present on disk — its former missing-image WARNING is RESOLVED.

**Stability**: 9/10 — 0 open CRITICAL, 0 design gaps. Open items are all low-severity: W9 (`VillageFounderPerkPatch.cs:163` unchecked latch write, confirmed still present) and E23 (one stale Chinese translation anchor on `apparel_hand_wraps_CardDescription`, cosmetic, English players unaffected). Acquisition coverage clean (0 unreachable / 0 dead-end across 399 produced / 117 consumed). Full audit trio (mod-report/critical-analysis/code-quality) re-ran 2026-08-24 against committed v1.68.1; per-category item/blueprint/structure/perk reports are 2026-08-08→08-16-era but were spot-re-verified this pass.

**Open work** (retrospectives — none block release):
- `cmc-village-conditional-drop-fixtures-missing` — 🟡 Pending. Village Inn/Academy/Jail board fixtures (`ConditionalDrops` on `cmcEnvVillage`) missing with no self-heal; reproduces on the dev's own saves. Diagnostics shipped (fwk 2.25.4); **root cause still open — needs a play session + log**. This is the most important open investigation.
- `portal-hub-env-overlap-2026-08-24` — 🟡 Pending. CMC's own Portal Hub registration (`cmcEnvVillage`) is exposed to the stale-`NextEnvironment.ParentEnvs` framework bug; fix shipped fwk 2.25.6, not yet confirmed in-game.
- `guard-kill-despawn` — 🟡 Pending. Event-driven despawn-latency fix (via `GameManager.OnEncounterEnemyDefeated`) shipped 1.54.3; not re-verified in-game.
- `CMC-HSP-compat` — 🟡 Pending. Structural fix (dropped to 2 overlapping kit perks, 1.52.0) confirmed for new characters; one legacy pre-1.52.0 save case unconfirmed.
- `river-bridge` — 🔵 **Graduated 2026-08-22** (crime-based travel lock removed in 1.58.0, confirmed in-game). No longer open.
- (Dormant, framework-scoped: `questinjector-blueprint-reset-risk`, `RETRO_CLOSURE_PLAN_2026-07-23` — CMC no longer references `QuestInjector`/`Quests.json`; not CMC's problem. `thicketpine-north-loadtimes` is a pure-vanilla area with zero CMC JSON references — tracked but not CMC-owned.)

**Framework compliance**: **Tier 2** — extensive adoption across ~48 C# files (132 `ActionRouter`/`SpawnService`/`TickEvents`/`ContentModPlugin`/`Api.*` call sites), plus declarative `WorldMap`/`DropInjections`/`InjectImprovementInto`/`TradingValues`/`EncounterGuards`/`MapMod`. No deprecated patterns (`DropCollectionGuardPatch` absent; no unfiltered hot-path prefixes; no `ModLoaderVerison`/`ModEditorVersion`). Single startup LogInfo line via the shared base class.

---

## Phase 0: Stabilize

> Audit score is 9/10 with 0 open CRITICAL/design gaps — this phase is now housekeeping and verification, not bug-fixing.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Fix W9 — guard `VillageFounderPerkPatch.cs:163`'s unchecked latch write (`if (WriteStat(...)) LogInfo else LogWarning`, matching every other write in the file) | Code robustness | P0 | Quick |
| Fix E23 — re-translate `apparel_hand_wraps_CardDescription` in `SimpCn.csv` and refresh its English-anchor column to match current `SimpEn.csv` | Localization | P1 | Quick |
| Play session to close the 4 open 🟡 retros — **priority: `cmc-village-conditional-drop-fixtures-missing`** (root cause still unknown, diagnostics armed), then `portal-hub-env-overlap`, `guard-kill-despawn`, `CMC-HSP-compat` (pre-1.52.0 profile load) | Retro close | P1 | Medium |
| In-game verify 1.68.0 Quiet Village trait (all four residents stay put; no guards spawn; overhead actually drops) and the 1.68.1 NPC scheduler perf pass | Verification | P1 | Medium |
| Decide the fate of the 2 TEMP diagnostic patches (`GuardCombatDiagnosticPatch`, `CompanionFollowDiagnostics`, `Plugin.cs:145/214`) — remove or demote `LogInfo`→`LogDebug` once confirmed against a live log | Cleanup | P2 | Quick |
| Refresh `items-report.md`/`blueprints-report.md`/`structures-report.md`/`perks-report.md` (still 1.45.0–1.59.3-era) via `/full-mod-audit-chain` | Audit coverage | P2 | Medium |

---

## Phase 1: Foundation

> Table-stakes hygiene. Mostly already healthy — the remaining items are small consistency fixes.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Versions synchronized (1.68.1 across ModInfo/Plugin.cs/README) | Version hygiene | — | DONE |
| Chinese localization (`SimpCn.csv`, 2183/2183 keys, parity clean but 1 stale anchor — see E23) | Localization | — | DONE (E23 in Phase 0) |
| Add the missing `LocalizationKey` on `CMC_MarketStall.json` CardName (untranslated name for CN players — the only Location card in the mod without one) | Localization | P1 | Quick |
| Normalize the 17 compact `Pk_*.json` perks (backfill `EquippedCards/AddedCards WarpData/Type` + `NoSafetyMode`); confirm/remove `Perk_Claws.json`'s trailing undisclosed `StarsCost: 1` | Perk template hygiene | P2 | Quick |
| Tidy the `ParentObjectID` mismatch in `CMC_JailCellBed.json` (non-load-bearing, cosmetic) | JSON hygiene | P2 | Quick |

---

## Phase 2: Core Expansion — DONE (1.60.0)

> All five items below shipped in the 1.60.0 batch; several later reworked. Pending only an in-game playthrough pass.

| Item | Shipped as |
|------|------------|
| Generalize Dress/Undress to the Wardrobe | Superseded 2026-08-17: Clothes/Coat/Wardrobe/Weapon Rack merged into one **Outfit Wardrobe** (`cmcOutfitWardrobe`) — three fixed 18-slot outfit sections with per-section Equip/Unequip (`OutfitWardrobeEquipService.cs`) |
| Wire the 5-chest economy into Trust | Every sale bumps the resident's Trust (`cmcStat<Resident>Trust` for Miller/Weaver/Apothecary/Professor; Inn Keeper's sale bumps his friendship stat) — `CopperChestPatch.cs` |
| First voluntary crime counter-play | Burglar's Kit (detection-roll discount while carried) + Miller-only "Make It Right" restitution CI |
| Talk for Thorne / Corrin / Vane | Three `DialogScene`/`DialogLine` pairs in distinct registers, plus a Talk entry on each guard's NPCAgent |
| Clay Ocarina | New pottery-tier instrument appended to `CMC_ShadowCat.json`'s taming trigger list |

### Also shipped, 1.61.0–1.68.1 (post-plan, incremental)

| Item | Shipped as |
|------|------------|
| Town Achievement Board | Built 1.61.0–1.64.0, **BENCHED 1.67.3** (fresh-save playtest found broken presentation; code kept, wiring disabled, excluded from docs) |
| Fireplace mechanism rework | `VillageFireplacePatch.cs` driving vanilla's Fireplace `PassiveEffects` (1.65.5–1.66.0) |
| Stray-improvement strip generalized | `StrayImprovementsPatch` across all 12 WorldMap locations (1.66.0) |
| Miller/Weaver "at work" duty | Engine `NPCDuty` chassis with a genuine profession action + daily trade-stock top-up — `CottageResidentWorkDutyPatch.cs` (1.67.0) |
| Quiet Village trait | `QuietVillagePerkPatch.cs` — suppresses resident commuting/foraging and all four guard posts for reduced sim overhead (1.68.0) |
| NPC scheduler performance pass | Scheduler tuning alongside framework 2.25.7 (1.68.1) |

---

## Phase 3: Integration & Depth

> Cross-mod hooks and late-game progression for experienced players.

### Metal weapon tier gated behind the Armorer course
**What**: A copper/iron weapon (copper mace-head / iron spearhead) gated on `Pk_GradArmorer`.
**Why**: The Armorer course grants copper/iron *armor* but weapons top out at fire-hardened/bone — the metal weapon the armor-only course implies. Pairs with a Sparring Post + the Outfit Wardrobe.
**Requires**: AdvancedCopperTools (a **HARD dependency** as of 1.56.0 — lean on ACT UIDs directly, no fallback).
**Complexity**: Medium

### Player-side remedy station keyed to CMC's drawback perks
**What**: An Apothecary Workbench / Mortar & Pestle (CT2, gated behind Herbalism/Medicine) producing 2–3 targeted remedies (Allergy Tonic, Stomach Settler, Clotting Salve) answering CMC's own drawback roster (`Bleeder`, `SeasonalAllergies`, `WeakStomach`, …).
**Why**: All medicine-making today is the *Apothecary's* NPC schedule; the player has no station to compound the remedies the Medicine/Herbalism courses point at. The concrete consumer for the CMC↔H&F herbal pairing.
**Requires**: HerbsAndFungi for potent inputs; framework station-blueprint rules (`ContainedBlueprintCardsWarpData`).
**Complexity**: Complex

### Reputation-scaled trade prices + a Village Reputation capstone
**What**: Graduate Reputation's single flat 25% Market Stall bonus into a curve (better Stall/Inn prices as Reputation rises; per-resident discounts as Trust climbs); give the full meter a one-time reward (Founder's Monument placeable or a festival beat).
**Why**: Reputation is visible on the Mental tab but rewards little beyond the one latch; a continuous curve + capstone completes the civic-growth arc.
**Requires**: `VillageReputationPatch` already computes a multiplier; avoid double-counting the 25% latch.
**Complexity**: Medium–Complex

---

## Phase 4: Polish

> Art and text that make the mod feel finished.

| Item | What | Complexity |
|------|------|------------|
| Town Watch + Jail art | Replace the blank/borrowed placeholder art on the 4 guard portraits (`CMC_Captain`, `CMC_Guard_*`) and the Jail/cell interior — the mod's largest self-disclosed honesty caveat | Medium |
| Distinct brewed-potion sprite | Apothecary Healing Mixture and Healing Potion share one sprite (M2); a distinct finished-potion sprite differentiates intermediate vs finished | Quick |
| ~~Commit `CMC_MarketStallDressed.png`~~ | **DONE** — the "dressed" Market Stall art is present on disk | — |
| Structure art variants | Extend the proven `CMC_MarketStall` ↔ `CMC_MarketStallDressed` self-transform to cottage faces / home signs (Ideas #3) | Medium |

---

## Long-term Vision

> Where this mod should be at v2.0.

Community Mod Chest has outgrown its "grab-bag" origins into a village-life simulation that rivals the base game's own settlement content: a living economy where resident savings circulate, a fully-realized crime axis (voluntary redemption, bribes, robbing civic structures, a lawful "join the Watch" path, jail labor, a fence for hot goods), and seasonal communal events (a snowed-in winter beat, a visiting trade caravan, harvest festivals). The natural v2.0 endpoint is a village that feels *inhabited whether or not the player is looking* — NPCs spending and restocking each other, seasonal rhythms the player plans around, and a Potter resident completing the craft-family roster so every content pillar has a village face. The 1.68.0 Quiet Village trait is a first acknowledgement that this depth has a simulation cost — v2.0 should make that overhead a tuned choice, not a trade-off.

**Potential major additions** (not yet justified — revisit after Phase 3):
- **A Potter resident + Pottery Kiln station** — pottery is CMC's largest content family yet the only pillar with no village NPC; a firing station would add quality/throughput.
- **Living economy — circulating resident savings + a visiting trade caravan** — chests currently only fill to a ceiling and freeze; make them ebb and refill, and bring outside goods to the valley seasonally.
- **The full constructive/illicit crime economy** — the redemption/bribe/fence/lawful-Watch axes the current design deliberately omits, built on the guard/Suspicion chassis already shipped.
- **Seasonal barriers for all four seasons** — a spring-thaw river-flood barrier at Clay Shoal completes the `SealableGates` "Season" set (winter drifts + autumn deadfall already ship).

These live in `Documentation/Ideas/Community_Mod_Chest/` (IDEAS.md + sibling specs `DEFERRED_ITEMS.md`, `VILLAGE_AREA.md`, `QUALITY_SPLIT.md`, `PERK_AloneInTheWorld.md`, benched `Wisp_and_NPCs/`).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod Community_Mod_Chest` and update this roadmap |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes (currently EA 0.66i), regenerate the nstrip lib, re-run `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis Community_Mod_Chest` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo Community_Mod_Chest` and bump minor version |
| Before every deploy | Confirm `bin/Release` is freshly built |

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
