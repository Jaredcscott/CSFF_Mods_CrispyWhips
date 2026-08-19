# Water-Driven Infrastructure — Changelog

All notable changes to this mod are documented here.

## [1.10.15] — 2026-08-19

### Fixed
- **Natural Windflow still went dead below 790°, in the exact windy environments (e.g. High Grove) it's supposed to rescue.** The v1.10.5 fix replaced a fuel-based gate with a temperature floor (SpoilageRange ≥790°) on the "Natural Windflow" PassiveEffect (Forge and Workshop), but this just moved the dead zone rather than removing it: any structure below 790° with no fuel — freshly placed, lit only briefly, or one that burned through its fuel before crossing the threshold — got zero windflow rescue from "Lose Temperature without fuel" (which fires from just 1° up) and cooled straight back toward 0, even standing in a confirmed-windy environment. Lowered Natural Windflow's floor from 790° to 1°, matching "Lose Temperature without fuel"'s own floor — a windy environment now counters cooling as soon as the structure has any heat at all, while an unlit, never-fueled structure still can't self-ignite from ambient wind alone (both effects still require >0°). Confirmed via a dedicated verification pass that no other JSON or C# path (mill-race water gating, etc.) additionally suppresses either effect.
- **Water-Driven Workshop's "Cast Metal Lump" action produced an item the Workshop itself would then refuse to accept back into its own inventory**, silently dead-ending the advertised Cast Metal Lump → Hammer All progression. "Cast Metal Lump" spawns vanilla `MetalBarUnfinished` (WDI's own `UnfinishedLumpID` constant points at this vanilla item, not a WDI-native one), but that item carries no CardTags and was never added to the Workshop's `InventoryFilter.AcceptedCardsWarpData` — so a player who crafted a lump could never drag it back in, despite the Workshop's own help text already claiming to accept "heated lump of metal." Added its UID to the accepted-cards list.

### Documentation
- **Clarified that Hammer All's Metal Quality boost is real but never visibly displayed.** A dedicated investigation traced the full boost path (`ApplyWorkshopQualityBoost`/`IsMetalQualityTool` in `ActionInterceptPatch.cs`) end-to-end and found the underlying logic sound — every hammerable candidate (MetalNugget, MetalBarUnfinished, vanilla tool blanks) correctly qualifies and is boosted every press. The player-visible symptom ("hammer strikes decrement but quality never seems to change") traces to vanilla's own item data: `SpecialDurability2` ("Metal Quality") on every one of these items is flagged `HidingOptions: AlwaysHide`, so the game's own inspection popup never renders that stat's bar regardless of value — confirmed against `.decomp/DurabilityStat.cs`'s `Show()` method, which returns `false` unconditionally for `AlwaysHide`. This is vanilla-wide behavior, not something WDI's own JSON controls, so it isn't something WDI can safely flip without touching a shared vanilla object. The Workshop's `CardHelpSection` (and matching localization) now say so explicitly: the quality gain doesn't show as a number on the item itself, but does carry through once the item is finished or transformed.

### Notes
- **The public WDI release (Nexus / public repo) is still on v1.10.0** (published 2026-07-17) as of this fix — 15 versions behind dev, missing every fix from v1.10.1 onward including the original (incomplete) Natural Windflow and Metal Quality fixes from v1.10.5. Two player bug reports against "the new version" were very likely testing pre-1.10.5 code, not the 1.10.5/1.10.6 fixes the changelog already claimed. Publishing is a separate, explicit step (`/export-to-repo`) and has not been run as part of this fix.

## [1.10.14] — 2026-08-16

### Added
- **Cut Iron Rivets from Sheet** (Metal Crafts tab) — shear one Cast Iron Sheet into 8 Cast Iron Rivets with any hammering tool. Cold work: no forge, no heated bar, no other mod required. Research 8 ticks, 3 daytime points per craft, gated on having a Cast Iron Sheet on the board.

### Changed
- The Cast Iron Sheet is no longer inert for players without AdvancedCopperTools. Until now its only consumers were ACT blueprints, so a framework-only player who crafted one had nothing to do with it; the new blueprint gives it an in-mod sink that feeds WDI's own fastener economy. A sheet comes from one iron bar (6 nuggets) and cuts into 8 rivets, so plate-cutting is a better metal-to-fastener rate than forging rivets one nugget at a time — payment for the extra bar-forging and heating work.
- Cast Iron Sheet card text rewritten: it previously said the sheet was "accepted anywhere a Cast Copper Sheet is required," which was only true with AdvancedCopperTools installed. It now names the rivet-cutting blueprint and states the ACT interchange as the conditional it is.

### Notes
- **Not verified in-game.** The blueprint, its tab registration, and its localization are statically verified only; nobody has yet crafted a sheet and cut it into rivets in a running game.

## [1.10.13] — 2026-08-16

### Fixed
- Partner station duties (Grinding Mill, Ore Sluice, Sawmill, Forge, Workshop) were registered at a base weight far below vanilla's own Partner duties, so duty selection almost always went to a native duty instead — a diagnostic play session showed a station duty reporting itself fully eligible and still never being picked all session. All five now register at a weight competitive with vanilla's, while staying below survival duties such as sleeping.
- Duty construction now aborts with a log line instead of reporting success when a reflected engine field is missing, so a future game update surfaces as a warning rather than a duty that silently attaches in an unusable state.

### Notes
- The weight change is **not verified in-game.** It addresses the measured cause of the duties never being selected; it does not prove they now run. All five duties remain unverified.
- A separate, still-open issue was observed in the same session: four of the five stations reported no reachable path for the Partner. That is under investigation and may simply reflect which structures were built in that particular save — it is not addressed here.

### Documentation
- README corrected: the architecture table said three grafted duties (now five), and the compatibility header still said EA 0.65 (the mod targets EA 0.66).

## [1.10.12] — 2026-08-16

### Added
- Water-Driven Forge and Water-Driven Workshop are now wired for Partner Duty Assignment, each with its own duty (`wdiOperateForge_Duty`, `wdiOperateWorkshop_Duty`) alongside the existing Fire keeping toggle. The Forge duty covers **Smelt Ore**; the Workshop duty covers **Hammer All** followed by **Smelt Ore**.

### Notes
- These two duties are **explicitly UNVERIFIED in-game** and are not advertised as working features. The Ore Sluice (1.10.10) and Sawmill (1.10.11) duties are likewise still unverified; only the Grinding Mill duty has been confirmed in-game.
- **Deliberately reduced scope:** these duties operate a forge that is *already at temperature*. Lighting and heating (Light Forge / Blast / Bellows) remain player actions. `Blast` is intentionally not automated — it burns fuel and leaves an Ash card behind on every press whether or not anything is loaded to smelt, and its own temperature gate stays satisfied at any lit temperature, so an autonomous Blast would repeat indefinitely. `Increase Temperature` (bellows) is not automated either: it precedes `Smelt Ore` in the interaction list under a looser gate, and the engine takes the first matching action, so marking it would prevent the smelt from ever being chosen.
- The Workshop duty performs `Hammer All` before `Smelt Ore` because hammering needs 50% heat and consumes none, while smelting needs 60% and spends a large part of it. This ordering only decides which runs *first* within a single pass — whether the smelt step runs at all still depends on the workshop being hot enough and on ore being reachable at that moment.
- These duties consume ore lying loose in the environment. The Partner does **not** look inside containers, so ore stored in the station's own inventory (or any chest) will not be picked up; leave it on the ground near the station.
- Same known limitation as the other station duties: the Duty Assignment toggle gates whether the Partner *does the work*, not whether the duty can be *selected*.

## [1.10.11] — 2026-08-16

### Added
- Water-Driven Sawmill is now wired for Partner Duty Assignment (`wdiOperateSawmill_Duty`), covering the drag-based **Cut** interaction. This is the first duty in the mod built on the `CardOnCardAction` path rather than a DismantleAction button.

### Fixed
- Duty ScriptableObjects created at load are marked `DontUnloadUnusedAsset`, so a Unity asset-unload can no longer silently disable the Grinding Mill, Ore Sluice, or Sawmill duties with no log output.

### Notes
- The Sawmill duty is **explicitly UNVERIFIED in-game** — it has never been observed running, and is not advertised as a working feature until an in-game pass confirms it. The Ore Sluice duty (1.10.10) is likewise still unverified. Only the Grinding Mill duty has been confirmed in-game.
- Known limitation, shared by all three station duties: the Duty Assignment toggle gates whether the Partner *does the work*, but not whether the duty can be *selected* — with the toggle off a Partner may still walk to the station and then idle. Tightening this would prevent the Partner from ever travelling to a station in another environment, so it is left as-is.

## [1.10.10] — 2026-08-16

### Added
- Ore Sluice can now be assigned to a Partner's Duty Assignment list, letting a recruited companion walk to it and press Sluice All on the player's behalf when toggled on.

## [1.10.8] — 2026-08-14

### Added
- Water-Driven Grinding Mill can now be assigned to a Partner's Duty Assignment list, letting a recruited companion walk to it and press Grind All on the player's behalf when toggled on.

## [1.10.7] — 2026-08-14

### Added
- Water-Driven Forge and Workshop can now be assigned to a Partner's fire-tending duty.

## [1.10.6] — 2026-08-11

### Added
- **Blast (Forge and Workshop) now warms the player.** Added a `StatModifications` entry to both stations' "Blast" action that pushes the vanilla `BaseTemperature` GameStat (ambient temperature, range -80..80) toward its +80 maximum by a fixed amount large enough to reach the cap in one use regardless of starting value — the stat's own clamp does the "push toward 80" work, so it never overshoots. Most useful in winter, when ambient temperature runs deeply negative.

### Changed
- **Forge and Workshop hold heat 3x longer.** Both stations' "Lose Temperature without fuel" and "High Temperature loss without windflow" `PassiveEffects` are now a third of their previous magnitude (−100→−33.3°/hour and −300→−100°/hour respectively), so the structure takes triple the time to cool from any given temperature — players need to Blast less often to keep smelting.

## [1.10.5] — 2026-08-09

### Fixed
- **Hammer All never raised Metal Quality on heated nuggets.** `IsMetalQualityTool`'s tag gate (`tag_Metal`/`tag_ToolBlank`/`tag_CopperSmall`/`tag_CopperBig`) never matched any real item — vanilla MetalNugget/MetalBarUnfinished carry no gameplay tags at all (the Workshop's own InventoryFilter has to allowlist MetalNugget by exact UID for the same reason) — so every strike just burned down vanilla's own inert "Strikes" stat with no quality payoff. Dropped the tag requirement; the existing active-"...Quality"-named SpecialDurability2 check is a sufficient, safe signal on its own.
- **Natural Windflow went dead the moment fuel hit exactly 0**, even in a confirmed-windy environment (e.g. High Grove) — the forge/workshop would just bleed heat via "Lose Temperature without fuel" with no windflow rescue, contradicting the "wind carries it to smelting temp on its own" description. The effect's gate now requires the structure already be substantially hot (≥790°, reached only through real fuel-burning) instead of requiring live fuel — an unlit, never-fueled forge still can't self-heat from ambient wind alone, but wind now carries an already-hot forge through fuel gaps as advertised.

### Technical
- Added a diagnostic breadcrumb to `IsWorkshopPrimaryStorageContext`'s 7-method stack-trace match — logs a `LogWarning` if it never matches across 50 calls, so a future game update silently renaming those methods surfaces as a log line instead of a permanently-silent Workshop storage-routing regression.
- Removed two unused sprite assets (`AlloySolder.png`, `CopperRivet.png`) whose items have referenced other sprite names (`act_tin_solder`, `CopperNails`) since they were added — dead files, no behavior change.

## [1.10.4] — 2026-08-09

### Fixed
- **Mill Race (North/South/East/West) improvements were appearing as buildable at indoor locations that have nothing to do with the outdoor mill-race network** — e.g. CMC's Village Inn interior. `InjectMillRaceImprovements`'s fallback pass (fills in directions unlinked by the static map) iterated every CardType-8 location in the merged mod data, including building interiors, instead of only locations that already have a validated mill-race edge. Scoped the fallback to locations that appear as an endpoint of at least one real map edge.

## [1.10.3] — 2026-08-07

### Fixed
- **9 stale Chinese translations re-synced to current English.** `Localization/SimpCn.csv` rows for the
  Water-Driven Forge (help text, Blast/Hammer-All/Smelt-Ore action text and fail messages — all now
  require flowing water), the Ore Sluice (now ballasted with heavy stones instead of iron
  bearing/axle hardware), the Fishpond drain action, the frozen Mill Race Outlet description (now
  mentions the Copper Brazier heat workaround), and the Iron Parts blueprint (simplified from
  "wrought iron bar" to "iron bar") had drifted from their English source after earlier content edits.
  Caught by the fleet Chinese-parity checker's stale-anchor check (E23).

## [1.10.2] — 2026-08-07

### Fixed
- **Sawmill and Forge construction wrongly demanded a Wooden Shovel AND a Metal Shovel simultaneously.** Both blueprints listed the two as separate `RequiredElements`, and blueprint tool matching is exact-reference (not tag-based) — owning either shovel alone never satisfied the other slot. Both entries are now a single `GpTag_Shovel` tab-group requirement, so any one shovel (including the vanilla Antler) is accepted, matching the "bring a shovel" intent.
- **Sawmill's hammer-stage requirement only accepted the Metal Hammer**, silently rejecting the Forge Hammer and Stone even though all three are vanilla-curated as interchangeable hard hammering tools. Now uses the `GpTag_HammeringToolHard` tab group instead of a hardcoded single item.

## [1.10.1] — 2026-07-23

### Fixed
- **Blast-produced copper nuggets shipped at 0 quality.** `HandleBlastAllInner` checked `SpawnService.Spawn`'s return value to find the newly-created nugget, but `GiveCard` returns `void` in EA 0.65 so `Spawn` always returns `null` on success — the quality-init code never ran. Now uses the same pre/post ID-diff pattern as the Sluice/Grind/Fish paths.
- **README's Water-Driven Forge cooling claim didn't match its passive-effect stack.** "Cools −40°/hour when idle" wasn't reachable from any combination of the forge's temperature `PassiveEffects` — reworded to describe the actual behavior (fast heat loss with no fuel; needs windflow to hold heat above ~1000°).

### Changed
- Added `LogDebug` breadcrumbs to 8 previously-silent `catch` blocks across `ActionInterceptPatch.cs`, `GameLoadPatch.cs`, `MillRaceMapEdgeProvider.cs`, and `MillRaceNetwork.cs` so future reflection/version-drift failures on these paths leave a diagnostic trail instead of failing invisibly.

## [1.10.0] — 2026-07-20

### Added
- **Own fasteners, decoupled from Advanced Copper Tools**: WDI now ships its own Iron Rivet and Cast Iron Sheet items so it no longer hard-depends on ACT. When ACT is installed, its equivalents are accepted interchangeably via `Api.BlueprintAlternates` (no hard cross-mod dependency).

### Changed
- **Fishpond "Drain" now warns that the fish population is lost.** Draining a stocked pond back to a kit permanently discards its fish population (stored in the pond's durability stats) — the action description now says so explicitly instead of the neutral "abandon the fish / drain the pond" wording, so the loss isn't a surprise.

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
