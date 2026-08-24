# Roadmap: Advanced Copper Tools
Version at time of writing: 1.15.9
Date: 2026-08-15
Audit score: 10/10 (PASS — 0 CRITICAL, 0 DESIGN GAP, 2 WARNING, 2 MINOR)

## Current State

**Theme**: A mid-game copper-and-bronze metalworking tier — comfort, throughput, and light. Metal sheets/nails feed tools (saw, wheelbarrow), cookware (pan, cauldron, tea station), light (lantern, brazier), armor (copper + iron tiers), and a 5-cave mining network (copper/tin/iron/salt/rock) gated behind Tin Solder.

**Content**: 45 items / 40 blueprints / 23 structures / 9 perks / 47 custom images. 0 SelfTriggeredAction, 0 Trigger JSON. Localization: SimpEn.csv + SimpCn.csv (Chinese shipped, 682/682 parity).

**Stability**: 10/10 — release-ready. 0 CRITICAL, 0 open design gaps. 2 WARNINGs (1 log-only self-resolving; 1 verification-pending, not a defect); 2 MINOR polish items. Both historically-open findings (IronOre orphaned dead content; 8 "broken" sprite refs) re-verified RESOLVED this consolidation.

**Open work**: none. `Documentation/Retrospectives/INDEX.md` has zero 🔴 Open or 🟡 Pending rows naming ACT/AdvancedCopperTools; all 4 ACT retros are 🔵 Graduated/archived.

**Framework compliance**: Tier 2 — uses `Api.ActionRouter` (Grind All / Draw Boiled Water routed handlers), `ContentModPlugin` base, and per-class `TryApply(ApplyPatch)` registration (not `PatchAll()`). No deprecated/dangerous patterns in source (no `DropCollectionGuardPatch`, no unfiltered hot-path prefixes, no `ModLoaderVerison`, no `Object.Destroy` on cards). One default-off legacy patch (`HeatHeldLiquidPatch`) remains behind a config flag pending removal.

---

## Phase 0: Stabilize  *(skipped — audit score 10/10, no open retrospectives)*

No CRITICAL fixes or retro closures outstanding. The two open WARNINGs are handled as verification/hygiene items in Phase 1, not stabilization blockers.

---

## Phase 1: Foundation

> Table-stakes health. Versions synced (1.15.9 ×3), Chinese shipped, Tier 2 adopted — most of this phase is already done. What remains is verification-coupled hygiene.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **In-game verify Duty/Ownership Firekeeping (W2)** — load a save with a recruited Partner NPC; confirm the duty toggle appears on stove/brazier/lantern and the Partner keeps tending across the Lit/Unlit `CardData` swap; confirm Ownership rows on brazier/lantern. Then un-withhold the feature in ModInfo/README. | Runtime verification | P1 | Medium |
| **Demote the Grind All log line (W1)** — `TeaStationPatch.cs:178` `LogInfo` → `LogDebug`, in the SAME commit that records the 1.15.8 pour-quantity play-verification and updates `playthrough-test-status.json` `act_tea_station_and_bathtub` (still dated 2026-07-21). | Logging hygiene | P1 | Quick |
| **Retire `HeatHeldLiquidPatch` (M2)** — once the Water Temp/Charges path is runtime-confirmed (same play session as W1), delete the default-off legacy patch and its config flag. Removes the mod's last full-scene per-tick scan. | Code hygiene / Tier 2 | P2 | Quick |
| Version hygiene — ModInfo/Plugin.cs/README all `1.15.9` ✓ (README §Version History header still labels "v1.15.7 (current)" — cosmetic, update on next edit). | Version hygiene | P2 | Quick |

---

## Phase 2: Core Expansion

> The most impactful content additions that extend the mod's existing loops. All are pure-JSON or ~2-line C# and reuse shipped mechanisms.

### Bulk Mineral Crate ("Ore Chest")
**What**: Clone `CopperPantry.json`/`CopperPantryPlaced.json` into a high-capacity, animal-safe storage crate; keep the **weight-based** inventory mode (`MaxWeightCapacity`, empty `InventorySlots` — do NOT add null slots), drop the spoilage modifier. Gate on a Metal Sheet, Construction → Furniture.
**Why**: The 5-cave network yields 5 bulk raws (Greenstone, Bog Iron, Tin Ore, Salt, Stone) but the only ACT storage is the spoilage-focused Copper Chest. This closes the produce-side of the mining loop.
**Requires**: none.
**Complexity**: Medium (pure JSON).

### Salt-Cured Meat / Jerky
**What**: raw meat + Salt → slow-spoiling ration, with an explicit eat DA (`ReceivingCardChanges.ModType: 3` — `EdibleStats` alone renders no button).
**Why**: The Salt Mine's only mod-local payoff today is feeding vanilla cooking. This gives Salt a self-contained ACT sink.
**Requires**: confirm a vanilla raw-meat tag/GUID first (do not fabricate — look it up in `UniqueIDScriptableGUID/CardData.json`).
**Complexity**: Medium (pure JSON).

### "Brew All" station action
**What**: Add a Brew-All DA to the lit Tea Station, extending the existing `HandleGrindAll` structure in `TeaStationPatch.cs` (in-place `CardModel` transform per matching card).
**Why**: The station dries → grinds-all → heats → brews 3 teas, but has no batch-brew to match Grind All. Completes the station's processing symmetry and seeds the master-plan `ProcessAllService`.
**Requires**: none (3 teas already ship). Do NOT invent a new mechanism — reuse the routed handler.
**Complexity**: Medium.

### Bronze / White-Bronze Armor mid-tier
**What**: Clone the four `Copper*` armor item JSONs + their `Bp_Copper*` blueprints; retag name/art for bronze; gate each blueprint's metal-sheet/lump slot on `Special4` in the bronze range (SD4 GhostBronze=110 / TinBronze=130 / WhiteBronze=140, per `AdvancedCopperTools/CLAUDE.md` § Metal Type Gating); set Armor Values between the copper and iron tiers; keep furnace-recyclable.
**Why**: Copper and Iron armor tiers exist with a protection gap between them; the SD4 metal-type ladder is already wired for pans/sheets/oil flask/cauldron but not armor, and no `BronzeArmor`/`BronzeHelmet` exists in vanilla.
**Requires**: decide whether to also split Male/Female torso art like the iron tier already does. Distinct from the Metal Alloying System long-term idea (recipe lockouts/cosmetic substitution) — this is a concrete protection-progression rung, not a re-color.
**Complexity**: Medium (pure JSON + art).

### Soap-enhanced Warm Bath (reuses vanilla `Soap`)
**What**: A `CardInteraction` on `CopperBathtub_Warm_Placed.json` (and the Hot tier) with `CompatibleCards` = vanilla `Soap`/`SoapyMixture` and `ReceivingCardChanges.ModType: 3` for a larger cleansing + morale boost (optionally a short one-bath hygiene buff). No new item, no new art.
**Why**: The Copper Bathtub is the mod's cleansing/morale centerpiece but nothing deepens a bath. Vanilla already ships `Soap`/`Lye`/`SoapyMixture` — reuse them rather than minting an ACT-specific consumable.
**Requires**: none. If later routing the stove's ash into this, go ash → vanilla `Lye` → vanilla `Soap`, not a new ACT chain.
**Complexity**: Quick (single CI, no new content).

---

## Phase 3: Integration & Depth

> Cross-mod hooks and late-game progression for experienced players.

### Iron Saw (tier-2 Large Saw)
**What**: JSON clone of `LargeSaw.json` + `Bp_LargeSaw.json` (iron recipe, higher durability) + a ~2-line `SawEffectPatch` edit adding the iron-saw UID to the large-tree bonus set.
**Why**: The mod has a full iron-armor tier, but every ACT *tool* is copper-only. An Iron Saw is the natural iron-tier throughput item.
**Requires**: decide bonus magnitude (−25 vs. −50 for guaranteed 1-hit).
**Complexity**: Medium.

### Coppersmith / Prospecting progression perk
**What**: A flat +75 `StartingStatModifiers` perk on a verified vanilla Metalworking/Smithing or mining/strength skill.
**Why**: All 9 current perks grant items/equipment; 0 are skill head-starts.
**Requires**: confirm the exact skill exists in `reference_vanilla_skill_names` (31 real `Skill_*`; do not invent `Skill_Mining`). Pick Suns/Moons cost.
**Complexity**: Medium.

### H&F medicine cauldron + cave fungi (soft-dep)
**What**: (a) shared `tag_MedicineBrewStation` on the Copper Cauldron so H&F brew CIs accept it; (b) H&F cave-fungi drop-injection / `SelfTriggeredAction` gated on the 5 ACT cave env UIDs.
**Why**: makes the caves a dual-purpose destination and the cauldron a premium brew vessel.
**Requires**: coordination with H&F (blocked on H&F Phase 6 for the cauldron tag); build jointly, not unilaterally.
**Complexity**: Medium.

### WDI hardware supply (already partially wired)
**What**: keep ACT `metal_sheet`/`copper_nails`/Tin Solder interchangeable with WDI cast sheets/rivets/Alloy Solder (shipped v1.14.0/v1.15.3); extend to any new WDI blueprint hardware slots as WDI grows.
**Why**: ACT is the metalwork-intermediate supplier for WDI infrastructure.
**Requires**: keep the copper-tier gate stable across both mods.
**Complexity**: Quick per new slot.

### Rejected: Superior ACT Mining Pick + Rare Secondary Vein Drop, Copper Ingot / Trade Bar
Both were built in a 2026-08-16 batch (v1.16.0 dev), then pulled before release on user review: **too close to
vanilla** — a pick that out-mines `MetalPickaxe` and a value-store trade good both read as thin re-skins of
existing vanilla roles rather than something distinct enough to justify. Do NOT rebuild either as originally
specced. If revisited, the differentiator needs to be sharper than "same thing, faster/denser."

---

## Phase 4: Polish

> Art and text that make the mod feel finished. All optional — the mod is player-complete without them.

| Item | What | Complexity |
|------|------|------------|
| Tea + Oil differentiating art (M1) | 4 PNGs (Calming/Warming/Focus Tea + Oil) to replace shared vanilla `ClayBowl` art; briefs ready in image-prompts-missing.md Priority 3; one-line `CardImageWarpData` edit each | Quick |
| Dedicated Collapsed Rock Face art | `act_collapsed_rock_face.png` for `CollapsedWallCopper/Iron` (currently vanilla `Tunnel_1` fallback — correct, not blank); pure cosmetic differentiation | Quick |
| README version-history header refresh | §Version History still labels "v1.15.7 (current)" — realign to 1.15.9 | Quick |

---

## Long-term Vision

> Where ACT should be at v2.0.

ACT's natural endpoint is a complete **metalwork → ironworks progression spine**: copper basics feed a mid-tier of tools/cookware/light/comfort, and iron pieces (armor already shipped; saw + a broader tool tier next) provide the late-game upgrade path — with the 5-cave mining network as the raw-material economy underneath and Tin Solder as the tech gate. The biggest addition that becomes natural at that scale is a **secondary-yield ore-processing step** (a placed crusher turning raw ore into crushed ore that smelts at a better nugget yield), plus a unified `ProcessAllService` (Grind All / Brew All / Forge All) so every ACT station shares one batch-action handler.

**Potential major additions** (not yet justified — revisit after Phase 3):
- **Copper Ore Crusher / secondary smelting yield** — deepens the mining economy; must ship crushed ore as a genuinely separate item with its OWN smelting entry (never a 2nd recipe on the same item — see the WDI 48-vs-12 copper bug).
- **Forge All batch action on the stove/forge** — third `HandleGrindAll` consumer; the concrete seed for `ProcessAllService`.
- **Ironworks tier as a follow-on mod** — ACT's metalwork chain is the natural prerequisite gate for a heavier iron-manufacturing mod.

These live in `Documentation/Ideas/AdvancedCopperTools/IDEAS.md` (Copper Ore Crusher, Metal Sieve, Metal Hunting Trap, Copper Shield, Wildlife Raid v2 all have specs there).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod AdvancedCopperTools` and update this roadmap |
| Game version update | Run `/update-mod-version`, refresh `lib/Assembly-CSharp-nstrip.dll` via NStrip (ACT is an nstrip mod — copy won't work), re-run `/diagnose-log` |
| After fixing a critical issue | Run `/critical-analysis AdvancedCopperTools` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo AdvancedCopperTools` and bump minor version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod AdvancedCopperTools         — full health check, updates .audit/
/critical-analysis AdvancedCopperTools — adversarial review
/repair-items AdvancedCopperTools      — auto-fix item JSON issues
/repair-blueprints AdvancedCopperTools — auto-fix blueprint JSON issues
/build-mod AdvancedCopperTools         — build Release DLL
/deploy-mods AdvancedCopperTools       — build + deploy to game
/update-mod-version AdvancedCopperTools <ver> — bump version in all 3 files
/export-to-repo AdvancedCopperTools    — push to public repo
```
