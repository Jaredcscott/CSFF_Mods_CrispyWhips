# Water-Driven Infrastructure — Changelog

All notable changes to this mod are documented here.

## [1.9.0] — 2026-07-16

### Added
- **Grinding Mill now accepts every vanilla grindable** (wheat/rye grains → flour, edible acorns → acorn flour, bone splinters → bonemeal, charcoal → ash, and more). The mill's inventory filter previously accepted only `tag_Millable` items — a tag no vanilla item carries — so vanilla grindables could never enter the mill. The filter is now populated at load with every item carrying a grinding-tool interaction, the same detection Grind All already uses.
- **Chip Ice** action on the frozen Mill Race Outlet — 2 daytime, yields a vanilla Ice Block. The winter freeze is now a resource: stock an Ice Pit or melt for water.
- **Wood Shavings byproduct**: the Sawmill Cut action now yields 2 vanilla Wood Shavings alongside the 8 Planks — tinder from every log.
- **Ash byproduct**: every Forge/Workshop Blast leaves a pile of vanilla Ash (lye/soil uses).
- **Fishpond "Draw Unclean Water"**: drag a water container onto a filled or stocked fishpond to draw unclean water — the PLAYER_GUIDE already promised this; now it works. (Not available while frozen.)
- **Four new character-creation perks**: Prospector's Start (Ore Sluice Kit, 2 Moons), Angler's Start (Fishpond Kit, 2 Moons), Homesteader's Waterworks (Mill Race Outlet Kit + Mill Race, 2 Moons), and Millwright (+75 Woodworking head start, 30 Suns).

### Fixed
- **Fishpond Kit could not be placed anywhere** (regression from 1.7.0): the Place action's water requirement was authored in a malformed form that could never be satisfied, so the button always fell back to the "Requires Water" notice — even at a river. The JSON gate is removed; placement is now governed by the same mill-race water-access check as every other station kit (at a water source, or at a location with a connected Mill Race Outlet).
- **Mill Race Outlet Kit dismantle now returns the full build cost**: added the 4 Copper Rivets that were previously lost (the action already claimed to "recover all materials").

## [1.8.1] — 2026-07-12
*(Covers changes since the last published release on 2026-06-23.)*

### Added
- **Standalone fastener pipeline**: WDI now ships Cast Copper Rivet, Alloy Solder, and Cast Metal Sheet items plus their Metal Crafts blueprints.
- **AdvancedCopperTools compatibility is optional**: if ACT is installed, WDI accepts ACT Copper Nails, Tin Solder, and Copper Sheet interchangeably with WDI's own fasteners and workshop outputs.

### Changed
- **AdvancedCopperTools is no longer a hard dependency.** WDI is fully playable with CSFFModFramework alone.
- **Construction recipes now use WDI-native fasteners** for sawmills, forges, grinding mills, ore sluices, fishponds, mill race outlets, and workshop upgrades.
- **Fishpond construction time reduced** from 12 to 8 daytime ticks per stage, cutting the full 5-stage build from 15 hours to 10 hours.
- **Water-Driven Forge and Workshop max temperature increased to 1800**, and iron components now smelt at the 1100+ threshold used by the rest of the forge pipeline.
- **Fishpond population handling now tracks species counts and stocked/unstocked variants** more reliably, including winter/frozen state handling.

### Fixed
- **GameLoadPatch now logs reflection failures explicitly** instead of silently skipping kiln recipe copy, greenstone smelting, iron-container tagging, mill race improvements, and ACT fastener alternates.
- **Iron smelting support is more reliable**: WDI forges/workshops receive the actual vanilla `tag_SmeltingContainerIron` reference at runtime.
- **Workshop actions now choose the right output family**: ACT items when ACT is present, WDI-native fasteners and sheets otherwise.

### Technical
- Added centralized ACT detection via `ActCompat` and routed fastener alternates through the framework `BlueprintAlternates` helper.
- Action interception was narrowed to station-backed WDI behavior; sawmill cutting is now pure JSON rather than intercepted C#.

## [1.7.0] — 2026-06-21

### Changed
- **Ore Sluice** — metal ore drop rates reduced across the board. Mud drop rates: Iron/Tin Nuggets 8%→3%, Copper Nuggets 12%→8%, Greenstone 22%→10%, Stone 55%→40%. Flint and Clay rates unchanged.
- **Water-Driven Forge** — now has 14 inventory slots for storing metal nuggets and clay items directly (previously no storage). Description updated to note natural wind heating in highland environments and that iron heats more slowly than copper.
- **Water-Driven Workshop** — description updated with the same natural wind heating note and iron heating clarification.
- **Fishpond (Frozen)** — description now clarifies that a frozen pond cannot be packed up; you must wait for the spring thaw.
- **Forge Start perk** — cost changed from 8 Moons to 1 Star.
- **Grinding Mill Start perk** — cost reduced from 8 Moons to 2 Moons.
- **Sawmill Start perk** — cost reduced from 8 Moons to 2 Moons.
- **Fishpond Kit** — shows a "Requires Water" message when you attempt to place it outside a river or lake environment.
- **Mill Race Outlet Kit** — shows a "Must be Outdoors" message when used inside a building.
- **Iron Parts** — now tagged as a high-temperature smelt item (requires 1100°C+); removed an old progress-based OnFull smelting path that is no longer needed.
- **Forge Iron Parts blueprint** — unlock condition text updated to "Wrought Iron Bar Needed".

### Fixed
- **Iron smelting** — iron nuggets and iron blooms now actually heat up in the WDI Forge and Workshop. A runtime fix injects the correct vanilla iron-smelting container tag so that iron's heat drain is cancelled by the forge's heat output. Previously iron would never reach smelting temperature.
- **Iron bar metal type** — iron bars smelted in the WDI Forge now correctly carry the iron metal-type flag. Previously bars could emerge as copper type due to a null-result bug in the GiveCard callback.
- **Fishpond population poll** — the fishpond's fish population growth timer no longer stalls during game pauses or transitions. Previously used `WaitForSeconds` which respects `timeScale=0`, causing polls to freeze while the game waited for player input.
- **Grind All** — now correctly detects output items for cards that produce results via the ProducedCards path rather than TransformInto; previously those items would produce nothing from Grind All.
- **Mill race network** — mod-injected world map locations (such as CMC's Village Path) are now automatically included in the mill race connectivity graph at load time.

### Technical
- Blueprint and item cross-references to ACT items updated to the current UID format (underscore-stripped), fixing silent lookup failures for any WDI recipe that requires ACT copper nails, sheets, or tools.

---

## [1.6.0] — 2026-06-12

### Technical
- Updated internal map data reference to EA 0.65 game data.
- Mill race edge provider updated for compatibility with EA 0.65.

---

## [1.5.0] — 2026-05-xx

- Quality of WDI items no longer decreases over time.
- Natural windflow mechanic added to the forge.
- Hammer All deduplication fix.
