# Changelog

## 1.2.2 (2026-08-14)
- Path Kit perk now grants 6 Path Kits instead of 3 — completing a road direction actually
  consumes 2 kits per direction in practice, so 3 kits only covered 1.5 directions.

## 1.2.1 (2026-08-14)
- Path Kit now offers only the 4 cardinal directions (North/South/East/West) instead of all 8
  compass directions — the 4 diagonal "Build Path" actions cluttered the action list and are
  removed, along with their orphaned localization rows.

## 1.2.0 (2026-08-14)
- Added three new structure-kit perks, same one-item "Place" pattern as the original nine:
  Well Kit (35 Suns, transforms into vanilla `Well`), Cellar Kit (40 Suns, transforms into vanilla
  `MudHutConstructionDoorEntranceMainCellar` — the Cellar's own self-contained entrance card, same
  pattern already proven by Cabin Kit), and Pit Trap Kit (20 Suns, transforms into vanilla
  `PitTrap`, still needs bait after placing). All three skip vanilla's normal 5-to-11-stage
  construction blueprint entirely, same as every other kit in this mod.

## 1.1.0 (2026-08-14)
- Added Path Kit perk (20 Suns): grants 3 Path Kit items. Each has 8 directional "Build Path"
  actions (N/S/E/W + diagonals) that instantly complete the matching vanilla `Imp_Path*` road
  improvement at the player's current location, via `ProducedCards` + `CompletedImprovements` —
  no C# required. New content, not part of the original CMC extraction.

## 1.0.0 (2026-08-14)
- Initial release. Nine placeable-structure-kit perks extracted from Community Mod Chest:
  Homestead, Cabin Kit, Mud Hut Kit, Log Bed Kit, Furnace Kit, Forge Kit, Oven Kit, Rain Cistern
  Kit, Tanning Pit Kit.
- Homestead Kit's building-material bundle swaps CMC's Advanced Copper Tools-dependent Copper
  Nails for vanilla Metal Nuggets (x20), removing the only cross-mod dependency so this mod runs
  standalone.
