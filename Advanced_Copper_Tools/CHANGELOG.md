# Advanced Copper Tools — Changelog

All notable changes to this mod are documented here.

## [1.11.6] — 2026-07-12
*(Covers changes since the last published release on 2026-06-23.)*

### Added
- **Cave Prospector perk**: optional 1-Star challenge perk that seals the cave network with Collapsed Rock Face barriers. Clear each barrier once to reopen the route and recover loose stone.
- **Wearable Metal Pan rain collection**: the wearable pan can now catch fresh rainwater during rain.

### Changed
- **Copper construction recipes now use tin solder more broadly**, adding a solder requirement to advanced copper builds such as armor, stove, cauldron, bathtub, brazier, kettle, oil flask, lantern oilwell, wheelbarrow parts, and the large saw.
- **Cave barriers accept more tools**: Collapsed Rock Faces can be cleared with a pickaxe, shovel, axe, antler, or knife instead of only pickaxe/shovel.
- **Tin Solder unlock and recipe now gate on tin nuggets** instead of the old raw Tin Ore requirement.
- **Iron Nails now gate on dried Bog Iron** and the ACT-only Iron Ore item was removed in favor of vanilla Bog Iron.
- **Tin Vein mining time now matches Copper and Iron Veins**, and iron/tin vein behavior has been normalized across cave nodes.

### Fixed
- **Iron Vein drops now spawn as usable vanilla Bog Iron with correct quality** instead of ACT Iron Ore or zero-quality output.
- **Cave Prospector gates now use framework sealable gates**, fixing portal bypass/stale travel-button behavior after digging through the walls.
- **Localization duplicate and truncation issues were cleaned up**, restoring corrupted Copper Stove, Tea Station, Copper Pantry, Metal Lantern, Large Saw, Metal Pan perk, and Chinese text rows.
- **Copper Stove help text now correctly lists charcoal as valid fuel.**

### Technical
- Nail interchangeability now delegates to `CSFFModFramework.Api.BlueprintAlternates` instead of a duplicated local implementation.
- ACT now uses the framework `ContentModPlugin` lifecycle for patch registration.

## [1.11.3] — 2026-07-04

### Changed
- **Tin Vein** mining times raised to match Copper Vein and Iron Vein: pickaxe 6→8, Chip Away 8→24. All three ore veins now take the same time to mine.

## [1.11.2] — 2026-07-04

### Fixed
- **Iron Vein** mining with a pickaxe cost 20 daytime ticks per strike (roughly 2 hours per attempt) — far more than Copper Vein (8) or Tin Vein (6) for a comparable yield. Reduced to 8 to match Copper Vein.
- Bog iron mined directly from an Iron Vein now spawns at **50% Quality** instead of 0%. Vein-mined iron previously skipped the vanilla Fresh → Dried process (which normally raises Quality), leaving it at the lowest possible grade.

## [1.10.1] — 2026-06-21

*(Changes since v1.8.0 — covers v1.9.0, v1.10.0, and v1.10.1)*

### Added
- **Iron Ore** — a raw ore chunk that smelts into an iron-grade metal nugget in a forge or furnace at 900°C. Heavier than copper ore and harder to refine.
- **Tin Ore** — a raw ore chunk that smelts into a tin-grade metal nugget at 900°C.
- **Iron Nail** — a hand-forged iron nail; stronger than copper and interchangeable with copper nails in all construction recipes.
- **Tin Solder** — a tin solder stick for joining metal parts or sealing seams.
- **Forge Iron Nails** (blueprint) — convert iron-grade metal nuggets into iron nails in a forge.
- **Forge Tin Solder** (blueprint) — melt a tin-grade metal nugget into solder sticks.
- **Copper Vein, Iron Vein, Tin Vein** — mineable ore veins found inside the new cave locations; use a pickaxe for best yield, or any digging/striking tool for slower extraction.
- **3 new world map locations**: Copper Vein Cave, Iron Vein Cave, and Tin Vein Cave — explorable cave areas each containing a dedicated ore vein.

### Changed
- **Building Materials** perk: cost reduced from 4 Moons to 2 Moons; kit description corrected ("small leather" instead of "leather").
- **Bathtub Kit** perk: cost reduced from 2 Moons to 1 Moon.
- **Large Saw** perk: converted to a drawback perk (45 Suns cost, was 3 Moons) — the saw is powerful but you pay a survival penalty for starting with it.

### Fixed
- Iron Ore and Tin Ore now correctly produce **iron-grade** and **tin-grade** metal nuggets when smelted. Previously they defaulted to copper-grade, meaning they were only accepted by copper forge recipes and couldn't be used for iron tool blueprints.
- Ore cave veins now drop **vanilla Bog Iron** directly rather than a custom intermediate item, aligning with vanilla smelt chains and improving compatibility.

### Technical
- Saw effect logic migrated to the framework's Tier 2 ActionRouter API (no player-visible change).
- ⚠️ **All item UniqueIDs had underscores stripped** (e.g. `advanced_copper_tools_copper_nails` → `advancedcoppertoolscoppernails`). This is a **save-breaking change** — items from v1.8.0 saves will not be recognized after upgrading. Start a new run after updating.
