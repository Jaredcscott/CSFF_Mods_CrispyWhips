# Water-Driven Infrastructure : Changelog

All notable changes to this mod are documented here.

## [1.12.5] - 2026-10-13

### Fixed
- **The Water-Driven Forge and Workshop no longer heat themselves with no fuel in them.** An empty forge was still gaining heat and climbing on its own. Heat now comes only while the station holds fuel, and an empty forge cools. Applies to the Forge, Workshop and Expanded Workshop.
- **A pile of Bronze Gears can be spread out**, so you can drag out the one metal type a recipe needs.
- **The Ore Sluice's help text shows the current drop rates** (copper 2% / 1% / 0.5%, greenstone 3% / 2% / 1%, iron and tin 1% / 0.5% / 0.25%, for mud, dirt and fine dirt) and says a pile whose rolls all miss leaves a Stone. English and Chinese.

### Internal
- WDI now builds against the plain game `Assembly-CSharp.dll`, like the framework, so a game update is caught at build time. The one private game method it used is reached by reflection and logs an error if it ever disappears. No change in play.

## [1.12.4] - 2026-10-12

Version number only. Nothing changed since 1.12.3, which was never published, so the notes below are what this release adds over Nexus 1.11.3.

## [1.12.3] - 2026-10-12

Fixes for the Nexus bug reports of 2026-09-10, 2026-09-18 and 2026-10-11. Scripted in-game checks cover them; none is confirmed by hand yet.

### Fixed
- **Blast no longer warms the world for good.** Every Blast raised the world temperature permanently, so perceived temperature stayed high for the rest of the run. Blast now heats only the forge, and an affected save is put back when it loads.
- **Partners only work stations you mark.** A Partner could pick an Operate or Harvest duty with nothing marked and keep walking to a station. All 12 WDI duties now need a marked card.
- **Smelt Greenstone works like the base game.** The powder becomes one copper Metal Nugget at the powder's Quality, plus one Greenstone Slag. It used to give bars at quality 0.
- **Machine-made nuggets no longer come with free Strikes.** The Ore Sluice, Blast and the self-smelting of WDI parts gave nuggets Strikes that let you hammer them to full quality for nothing.
- **Ore Sluice nuggets come out at Metal Quality 35**, as the help text says (they came out at 5).
- **Melted metal keeps its own quality** (Nexus report, 2026-10-11). The old 50% nugget floor is gone, so poor iron forged into an axle and melted back no longer comes out at 50. Each item's nuggets take that item's quality, as in the base game. An item with no quality of its own gives quality 0.
- **WDI's gears, saw blade and iron parts now remember the quality** of the bar or crucible they were made from and melt back at it. Parts made before 1.12.3 melt at 0.
- **Blast melts each item at its own quality**, not the best item's.
- **Iron Parts melt into 3 nuggets**, not 6. One bar makes two Iron Parts, which used to melt into 12 nuggets between them.
- **Cast Metal Lump takes 6 nuggets and keeps their average quality.** It took 4 and raised poor metal to quality 50. The Unfinished Metal Bar it makes now matches the base game's.
- **Hammer All finishing an Iron Bloom gives the Iron Nugget its Strikes and visible Quality** (Nexus report, 2026-09-10). Before, the nugget sat at 0 Strikes and Quality 0 and could not be smithed.

### Changed
- **The Ore Sluice finds much less metal** (Chiwei's Nexus report, 2026-10-05). It out-produced the game's copper mine about 8 times over. A mud pile now gives a copper nugget 2% of the time (was 8%) and a greenstone 3% (was 10%). Dirt gives 1% and 2%, fine dirt 0.5% and 1%. Tin and iron drop to 1% from mud, 0.5% from dirt and 0.25% from fine dirt. Flint, stone and clay are unchanged.
- **WDI's own nails, solder and sheets are retired for the framework's shared ones** (Chiwei's Nexus report, 2026-10-05). Every WDI recipe now takes the shared Metal Nail, Tin Solder and Metal Sheet, one for one. The Stamp Mill takes 2 Iron Sheets, the Water Basin 1 Metal Sheet of any metal but iron, and a rivet slot takes a Metal Nail of any metal.
- **Six WDI blueprints left the journal:** Forge Copper Rivet, Forge Iron Rivet, Forge Alloy Solder, Forge Cast Copper Sheet, Forge Cast Iron Sheet and Cut Iron Rivets from Sheet. The framework's blueprints in the same tab make the shared parts. Forge Iron Nails makes iron nails.
- **Old parts in a save turn into the shared ones.** Retired cards are named "(old)" and reforge themselves within a quarter of an hour, or at once with the Reforge button. Copper cards become a Copper Sheet or Copper Nail, iron cards an Iron Sheet or Iron Nail, and Alloy Solder a Tin Solder. Converted sheets start at Metal Quality 0.
- **Expand costs 4 Iron Nails** (Metal Nails at Metal Type Iron) instead of 4 Cast Iron Rivets. Copper and bronze nails do not count.
- **The Workshop's Forge Iron Rivets button is now Forge Iron Nails.** The Workshop always hands over the shared cards, and WDI logs a warning at load if the framework is too old to ship them.
- **The fastener stand-ins are gone.** With every slot naming the shared cards, WDI no longer registers its rivets and solder as alternates for other mods' parts.
- Blast's description no longer says it warms you, and Cast Metal Lump's says six nuggets and their average quality.
- 27 card pictures are cropped to their subject like the game's own art, so each item fills its card and the pictures take about a sixth of the space. The Water Basin has a new, larger picture, and the Cattail Bed and Reed Bed pictures are closer to the base game's.

## [1.12.2] - 2026-10-10

This release folds in 1.12.0 and 1.12.1, which were never published (Nexus holds 1.11.3).

### Added
- **The Water Wheel card is animated.** Four frames turn the wheel while the pedestal stays still. The still picture remains as a fallback.
- **Expand for the Sawmill and the Workshop.** A new Expand button (30 minutes) rebuilds either station larger in place. The Expanded Sawmill holds 40,000 weight instead of 20,000, the Expanded Workshop 7,000 instead of 5,000. Contents, Temperature and a fitted Overshot Wheel stay. Each has its own Partner duty. There is no way back, and Pack Up returns the plain kit. Cost and capacity are placeholders.
- **Partners fish the pond.** Mark a Filled or Stocked Fishpond with the new **Fish the Fishpond** duty and a Partner presses its Catch buttons under the same rules as you. It takes the first species still in the pond, in button order.
- **Bronze gears with Advanced Copper Tools.** Cast Large and Small Bronze Gear (Metal Crafts tab) are cast from a crucible at Metal Type 120 to 140 and fit wherever the same-size copper gear is needed. They give no bonus. Without ACT the blueprints never appear.

### Removed
- **Cold-Water Cache**, added during 1.12.0 testing, is removed and set aside for a later version. A cache built in a test save is dropped when the save loads, with everything inside it.

### Fixed
- **A Partner no longer stands at an empty Grinding Mill, Stamp Mill or Ore Sluice pressing its button.** The duty is now skipped while no marked station holds anything to process, so the Partner does other work. Pressing the button yourself is unchanged.
- **Five cards that showed blank now have art:** Large and Small Bronze Gear, the Water-Driven Stamp Mill, its kit and its blueprint. The Overshot Wheel and its blueprint show the Water Wheel picture.
- **The Stamp Mill's outline is cleaner.** A thin white rim is gone.
- **A fishpond no longer hands over a whole fish from a fraction of one.** The Catch and Ice Fish buttons opened at a hundredth of a fish. They now need one whole fish.
- **Fish from a pond's Catch buttons come out at Quality 72 again.** Since rod and spear fishing arrived, they kept the vanilla card's starting Quality.
- **The Expanded Sawmill's description reads in full.** An unquoted comma cut it off in both languages.
- **Smelt Greenstone on the Forge and Workshop** gave Metal Nuggets instead of the Unfinished Metal Bar. (1.12.3 later changed this recipe again.)

### Changed
- **The Overshot Wheel is built from the mod's own parts:** 1 Water Wheel, 1 Iron Axle, 1 Large Copper Gear and 1 Iron Bearing, instead of buying the wheel's raw materials a second time.
- **The Overshot Wheel no longer claims to speed up a Partner.** A Partner's duty actions never cost time, so the wheel only saves you time.
- **Needs CSFFModFramework 2.28.0 or later,** which ships the Metal Sheet, Metal Nail and Tin Solder that used to be ACT's.
- **Advanced Copper Tools is no longer a plugin dependency.** WDI never needed it for load order. No change in play.
- **The Forge and Workshop no longer name ACT's tin ore.** They accept every card tagged for 1,100-degree smelting, which includes it. The per-boot "unresolved" log line without ACT is gone.

## [1.12.0] - 2026-09-28

Waterside Homestead: the mill race banks water in a cistern for winter, feeds the fishpond and waters a reed bed, and building it all trains your skills.

### Added
- **Mill-Race Cistern Feed.** A running Mill Race Outlet fills a Rain Cistern at the same location with River Water, 50 per 15 minutes, about 4.5 days from empty. A cistern already holding rainwater or another liquid is left alone until drawn empty. A frozen outlet feeds nothing.
- **Fishpond Feeding.** Drag Fish Scraps, Bugs or any fishing bait onto a Filled or Stocked Fishpond and **Scatter Fish Food** adds a quarter fish each to Pike, Perch and Minnow. Only a species of two or more breeds.
- **Reed Bed and Cattail Bed** (Farming > Agriculture). Research takes 2 hours once a Mill Race is on the board. Plant them at a river or lake, or anywhere a connected outlet waters. They regrow like the wild patches, with a harvest about every 3 days (faster in spring, none in winter). Harvest by hand, scythe or sickle.
- **A Partner can harvest the beds** with two new duties, Harvest the Reed Bed and Harvest the Cattail Bed. Vanilla's reed duty never reached the beds.
- **Skill training.** Every WDI blueprint trains a vanilla skill (Smithing, Crafting, Woodworking, Herbalism). Sawmill Cut trains Woodworking 0.5 per log, and pond Catch and Ice Fish train Fishing 0.5 per fish. The batch buttons stay XP-free.
- **Eel in the Fishpond.** Drag a vanilla Eel onto a pond to **Stock Eel** (up to 10, no breeding). **Catch Eel** and **Ice Fish Eel** take one out at Quality 72. Eels survive the freeze and thaw.
- **Sawmill Saw All.** The sawmill now takes Tree Logs (capacity raised from 5,000 to 20,000, six logs). **Saw All** (1 hour) saws every log inside, each giving 8 Wooden Planks and 2 Wood Shavings.
- **Workshop iron crafts.** **Forge Iron Rivets** and **Hammer Iron Sheet** (1 hour each) turn six iron nuggets into six rivets or one sheet, behind the same heat gate as the copper crafts. (Forge Iron Rivets became Forge Iron Nails in 1.12.3.)
- **Winter heat.** A frozen outlet no longer stops stations while a heat source stands at the location: a lit Campfire, Fire Pit, Fireplace, Oven, Kiln, Furnace, Bloomery, Forge or Copper Brazier, or a Forge or Workshop at Temperature 900 or more. Heat does not thaw the outlet, so a cistern stays unfed until spring.
- **Water Basin** (Construction > Furniture). A stone basin that adds 10 to your Sanctuary while it stands at your location. Research 2 hours, build 1.5 hours, one per location, built where a Mill Race brings water. Clear Basin removes it and returns nothing.
- **Control Valve.** "Close Valve" on a running outlet stops the water, and a cistern there stops filling. "Open Valve" restarts both.
- **Rod and spear fishing.** Drag a fishing rod onto a pond to **Fish**, or a spear to **Spearfish** (30 minutes, 0.5 Fishing XP). The catch is a random species weighted by how many the pond holds, at Quality 72. With no fish, nothing happens and no time passes.
- **Overshot Wheel.** Fit it to a Sawmill, Grinding Mill, Workshop or Ore Sluice to shorten that station's batch button: Saw All 1 hour to 30 minutes, Grind All and Hammer All 30 to 15, Sluice All 45 to 15. Pack Up (Overshot) returns the wheel.
- **Water-Driven Stamp Mill** (Construction > Furniture). An ore crusher with an 18-slot hopper for Greenstone, Greenstone Slag and Bog Iron. **Stamp All** (30 minutes) crushes them at the ore's Quality plus a hammer bonus (Greenstone +7, Bog Iron +3). It has a Partner duty, Operate the Stamp Mill. Tin ore is refused.
- **Water-source tag and plumbed bathtub.** Another mod can tag a card as a water source so WDI structures can be built beside it. With Advanced Copper Tools, a running outlet fills an empty placed Copper Bathtub.
- **Setting: Seasons > Winter Station Lock** (off by default). When on, WDI stations stop in winter everywhere unless a heat source stands there. The lock existed before but read the season wrongly and never fired. With it off, nothing changes for existing players. A changed value needs a full game relaunch.

### Fixed
- **Materials from WDI come back usable** (Chiwei's Nexus report, 2026-09-27). Planks from taking kits apart no longer come out as no wood at all but as oak. Sawmill Cut gives planks of the log's own wood and dryness. The Workshop Kit's Forge Hammer comes back iron. Smelt Ore bars come out copper at the ore's quality.
- **A Copper Sheet hammered at the Workshop keeps the nuggets' average Metal Quality** (with Advanced Copper Tools). Every sheet used to come out at quality 0.
- **Blast melts ACT items into nuggets of their own metal** (with Advanced Copper Tools). Every nugget used to come back copper, and ACT's alloy armour, iron armour, shields and Iron Large Saw were not melted at all.
- **Copper gears and the saw blade keep forge quality when smelted.** WDI's own parts were missing from the quality fix.
- **A pond catch costs exactly its button's time.** A hidden extra step failed on every catch and logged an error. It never charged time, so play is unchanged: Catch costs 30 minutes (Crayfish and Frog 15) and Ice Fish 45.
- **Station batch buttons no longer leave used-up cards alive in the game.** Blast, Saw All, Sluice All, Grind All and the Workshop crafts left melted nuggets and sawn logs counted on the board and still running their own effects.
- **Finishing a Mill Race connects the location at the other end,** including one you have never visited or have left. The old code failed and logged "Reciprocal completion failed". A race that was only started no longer counts as a connection.
- **The Grinding Mill now takes the grindables 1.9.0 promised, and only those.** A bug kept the wider list from applying, so Bone Splinters were refused. Hammer jobs that are not milling (breaking a cast, bones or mud bricks, mashing berries, cracking geodes) stay out. It now takes 49 cards: 39 vanilla and 10 Herbs and Fungi dried herbs, mushrooms and peanuts.
- **Help text matches the code.** The Sawmill's help now says it can be placed at a river, lake or outlet-watered location, and that you drag a log onto it.
- **Quieter log.** Reflection misses in the action handlers now log once each instead of failing silently, and per-click lookups are cached.

### Docs
- The game version is corrected to EA 0.68b in the README and mod description.

## [1.11.3] - 2026-09-25

### Fixed
- **Copper nuggets from self-smelting copper items now get the forge's quality floor.** The base
  game marks every new metal nugget as copper the moment it appears, and the mod read that mark as
  "this nugget was already handled elsewhere", so it skipped every nugget and they came out at the
  item's own low quality. It now treats the copper mark as the default it is. In-game check tracked
  as T2.131.
- **The same mistake applied to smelting the iron parts, bearing, axle and wrench back into
  nuggets:** those nuggets could stay marked copper instead of iron. Fixed the same way; not yet
  confirmed in-game.

## [1.11.2] - 2026-09-18

### Changed
- **Smaller download (now 12.0 MB).** Two packaging causes, neither in the mod itself. The build
  script wrote a second, byte-identical copy of every card image into `Resource/Texture2D/`
  alongside `Resource/Picture/`, which nothing ever read, so every release archive carried the
  whole art set twice. Separately, the Filled Fishpond image had shipped at 848x1264 instead of the
  512-wide size every other card in this mod uses. Both are fixed. No gameplay or content change.

### Docs
- README version history now lists v1.11.1 (the quieter-startup-log release), which the published
  copy was missing while it still labelled v1.11.0 as current.
- README notes the 2026-09-06 playtest report of the Cut Iron Rivets from Sheet craft failing, and
  the 2026-09-15 source re-check that found no wiring defect. Cause still unconfirmed (T2.113).

## [1.11.1] - 2026-09-11

### Changed - log verbosity (pre-distribution pass)
- `[MillDutyPatch]`'s per-duty attachment line dropped from Info to Debug and replaced with one
  aggregate (`attached N Partner duty/duties`). It previously emitted five Info lines at load,
  one per grafted duty (Grinding Mill, Ore Sluice, Sawmill, Water-Driven Forge, Water-Driven
  Workshop). Every failure path in that method already logged at Warning and is unchanged, so a
  duty that fails to attach is still reported just as loudly.
- No behavior change: the duties themselves attach exactly as before.

## [1.11.0] - 2026-09-07

### Added
- **Fish Funnel** (Advanced Tools tab) - a river-placed woven timber wing-wall that steers
  passing fish toward a vanilla Funnel Trap set in the same water, doubling how fast the trap's
  Common Fish Population builds up. Chain: `water_sawmill_bp_fish_funnel` (6 Planks + 2 Rope +
  4 Twine, 1.5 hours, gated on a Mill Race and on building outdoors at a river) ->
  `water_sawmill_fish_funnel_kit` -> `water_sawmill_fish_funnel_placed`. Implemented as a CT2
  structure carrying a `RemotePassiveEffects` entry that adds +10 to the trap's
  `SpecialDurability1` rate (vanilla base is 10, so 20/dtp), targeting vanilla
  `FunnelTrapLocation` `16eb1777246a82e4b877a2973faf7876` by reference. **The +100% figure is a
  first-pass placeholder, not a balanced value**, and the whole feature is unconfirmed in-game.
  Two authoring notes worth keeping: a kit-placed CT10 improvement can never fire a remote
  passive effect (`GameManager.AddRemotePassiveEffects` returns early unless `BlueprintComplete`,
  which only the improvement-menu build flow ever sets), and plain `PassiveEffects` only ever
  touches the card's own stats, so neither of those two shapes would have worked. Funnels stack
  additively by design: `PassiveEffect.OnBoardEffectCap` is keyed on a global effect name rather
  than per environment, so capping at 1 would silently disable a second funnel at another river.
- **Ore Sluice washed tailings** - a soil pile whose mineral rolls all miss now leaves a vanilla
  Stone behind, so Sluice All never yields literally nothing (previously about 29% of mud piles
  and 34% of fine dirt did). Routed to real vanilla Stone rather than a minted tailings item:
  EA 0.67i ships no Gravel, Sand or Pebble card at all, and Stone is the only aggregate in the
  game with existing uses. Implemented as a floor in `ActionInterceptPatch.RollSluiceDrops`, not
  as a per-wash byproduct, so it cannot inflate the stone economy on rolls that already paid out.

## [1.10.20] - 2026-09-07

### Fixed
- **The Cast Metal Sheet now carries a copper metal-type stamp, so it can no longer stand in for a
  bronze-grade sheet in Advanced Copper Tools' Bronze armor recipes.** `WaterDrivenSawmill_CastMetalSheet`
  declared its Metal Type stat (`SpecialDurability4`) inactive. On its own that was harmless, but ACT's
  `PatchSheetInterchangeability` registers the Cast Metal Sheet as an accepted alternate everywhere ACT's
  own Copper Sheet is required - which, as of ACT 1.16.5, includes the four Bronze armor blueprints and
  their `Special4 {110, 140}` bronze-grade gate. The game skips that range test entirely when the card's
  Special4 stat is inactive (`if ((bool)card.SpecialDurability4 && ...)`), so a Cast Metal Sheet would
  have satisfied a Bronze armor slot without being bronze at all - re-opening, through WDI, exactly the
  tier-collapse ACT 1.16.5 had just closed. The sheet is now stamped Active at 100.0 (copper grade,
  `ValidValues [100.0]`), which fails the 110-140 gate as intended while leaving every Copper-tier and
  WDI-internal use of the sheet unchanged. Cosmetic only for players without ACT installed.
  Not yet verified in-game.

## [1.10.19] - 2026-08-23

### Fixed
- **Hammer All's Metal Quality boost never actually moved the "Quality" number players see on hammerable metal items.** The 1.10.15 investigation concluded the boost logic was sound and the missing display was purely a vanilla `AlwaysHide` quirk, but a follow-up pass found a real bug underneath that conclusion: vanilla metal-bar items (e.g. `MetalBarUnfinished`) carry **two** independent quality-named stats, `SpecialDurability2` ("Metal Quality", hidden) and `SpecialDurability3` ("Quality", the one shown in the inspection popup). `ApplyWorkshopQualityBoost` only ever boosted SD2, so "Metal Quality" climbed +5 per Hammer All press exactly as intended while the player-visible "Quality" line stayed frozen, indistinguishable in-game from the boost doing nothing at all. Confirmed via in-game log and screenshots on 2026-08-23. Now boosts both active "Quality"-named slots, matching how `CompleteHammeredCard` already carries both SD2 and SD3 through a finished transform. Quality gain still requires the item to have taken at least one Strike (`SpecialDurability1 > 0`), a same-day follow-up excludes any Smith-hammerable item from the "quality-only" fallback bucket so an item sitting at 0 Strikes can't farm free quality through that path.

## [1.10.18] - 2026-08-22

### Fixed
- **Vanilla iron items (Iron Nuggets, Unfinished/Finished Wrought Iron Bars) never registered as "in a smelting container" in the Forge or Workshop, so their `Smelted` (Progress) stat only ever drained instead of filling.** The vanilla `tag_SmeltingContainer` tag that these items' PassiveEffects check by object reference is an obfuscated-name asset (`PulsingOutline_7769` under EA 0.66i), WarpResolver's name-based lookup for the plain `"tag_SmeltingContainer"` string in the Forge/Workshop's `CardTagsWarpData` doesn't find that exact vanilla object, so it silently created a different SO that never matches. Only the iron-specific variant of this problem (`tag_SmeltingContainerIron`, gating Temperature) had a fix; the general container-tag fix was missing. Generalized the existing iron-tag injection (`InjectSmeltingContainerIronTag` in `GameLoadPatch.cs`) to also extract and inject the vanilla `tag_SmeltingContainer` SO (sourced from `MetalBarUnfinished`'s own PassiveEffects) into both stations, alongside the existing iron-specific injection.

## [1.10.17] - 2026-08-22

### Changed
- **Forge and Workshop self-smelting (iron parts/bearing/axle/wrench, copper gears/saw blades) now retains the melted item's own quality instead of always flattening it to the 50% floor.** Blast already read the smelted item's quality and carried it into the resulting nuggets (floored at 50% so machine processing never *lowers* quality); the two `Progress`→`OnFull` self-smelt paths added in 1.10.16 only ever applied the flat 50% floor and ignored the source item's actual quality. Both paths now snapshot the melted item's `SpecialDurability2` quality percentage before it's consumed and apply the same "retain quality, floor at 50%" rule Blast uses. WDI's own iron parts and copper gears carry no quality stat of their own, so in practice they still resolve to the 50% default, but any item (present or future, WDI or another mod) that self-smelts via this path and DOES carry a quality stat now has that quality honored in the nuggets it produces.

## [1.10.16] - 2026-08-22

### Fixed
- **Copper Nuggets smelted from Copper Gears/Saw Blades (and any other self-smelting metal item) came out at 0% quality**, unlike Blast/iron-item smelting which already floors quality at 50%. These items melt via a `Progress` → `OnFull` → `ProducedCards` JSON path (no C# involved), and `ProducedCards` cannot set a spawned card's quality stats, so the resulting nuggets kept whatever quality the source item had, including 0% for items spawned directly (e.g. via dev/debug spawn) rather than crafted through a blueprint. Only the 4 hardcoded iron parts (`IronSmeltItemIds`) had a quality-floor fix; copper gears/blades did not. Added a second GiveCard hook that discovers *any* self-smelting item at runtime (any CardData whose `Progress.OnFull.ProducedCards` includes a copper nugget) rather than a hardcoded list, and floors its output nuggets' quality the same way Blast/iron smelting already does, so the Forge and Workshop now always apply a minimum quality to every nugget they produce, regardless of the source item's own quality.

## [1.10.15] - 2026-08-19

### Fixed
- **Natural Windflow still went dead below 790°, in the exact windy environments (e.g. High Grove) it's supposed to rescue.** The v1.10.5 fix replaced a fuel-based gate with a temperature floor (SpoilageRange ≥790°) on the "Natural Windflow" PassiveEffect (Forge and Workshop), but this just moved the dead zone rather than removing it: any structure below 790° with no fuel, freshly placed, lit only briefly, or one that burned through its fuel before crossing the threshold, got zero windflow rescue from "Lose Temperature without fuel" (which fires from just 1° up) and cooled straight back toward 0, even standing in a confirmed-windy environment. Lowered Natural Windflow's floor from 790° to 1°, matching "Lose Temperature without fuel"'s own floor, a windy environment now counters cooling as soon as the structure has any heat at all, while an unlit, never-fueled structure still can't self-ignite from ambient wind alone (both effects still require >0°). Confirmed via a dedicated verification pass that no other JSON or C# path (mill-race water gating, etc.) additionally suppresses either effect.
- **Water-Driven Workshop's "Cast Metal Lump" action produced an item the Workshop itself would then refuse to accept back into its own inventory**, silently dead-ending the advertised Cast Metal Lump → Hammer All progression. "Cast Metal Lump" spawns vanilla `MetalBarUnfinished` (WDI's own `UnfinishedLumpID` constant points at this vanilla item, not a WDI-native one), but that item carries no CardTags and was never added to the Workshop's `InventoryFilter.AcceptedCardsWarpData`, so a player who crafted a lump could never drag it back in, despite the Workshop's own help text already claiming to accept "heated lump of metal." Added its UID to the accepted-cards list.

### Documentation
- **Clarified that Hammer All's Metal Quality boost is real but never visibly displayed.** A dedicated investigation traced the full boost path (`ApplyWorkshopQualityBoost`/`IsMetalQualityTool` in `ActionInterceptPatch.cs`) end-to-end and found the underlying logic sound, every hammerable candidate (MetalNugget, MetalBarUnfinished, vanilla tool blanks) correctly qualifies and is boosted every press. The player-visible symptom ("hammer strikes decrement but quality never seems to change") traces to vanilla's own item data: `SpecialDurability2` ("Metal Quality") on every one of these items is flagged `HidingOptions: AlwaysHide`, so the game's own inspection popup never renders that stat's bar regardless of value, confirmed against `.decomp/DurabilityStat.cs`'s `Show()` method, which returns `false` unconditionally for `AlwaysHide`. This is vanilla-wide behavior, not something WDI's own JSON controls, so it isn't something WDI can safely flip without touching a shared vanilla object. The Workshop's `CardHelpSection` (and matching localization) now say so explicitly: the quality gain doesn't show as a number on the item itself, but does carry through once the item is finished or transformed.

### Notes
- **The public WDI release (Nexus / public repo) is still on v1.10.0** (published 2026-07-17) as of this fix, 15 versions behind dev, missing every fix from v1.10.1 onward including the original (incomplete) Natural Windflow and Metal Quality fixes from v1.10.5. Two player bug reports against "the new version" were very likely testing pre-1.10.5 code, not the 1.10.5/1.10.6 fixes the changelog already claimed. Publishing is a separate, explicit step (`/export-to-repo`) and has not been run as part of this fix.

## [1.10.14] - 2026-08-16

### Added
- **Cut Iron Rivets from Sheet** (Metal Crafts tab), shear one Cast Iron Sheet into 8 Cast Iron Rivets with any hammering tool. Cold work: no forge, no heated bar, no other mod required. Research 8 ticks, 3 daytime points per craft, gated on having a Cast Iron Sheet on the board.

### Changed
- The Cast Iron Sheet is no longer inert for players without AdvancedCopperTools. Until now its only consumers were ACT blueprints, so a framework-only player who crafted one had nothing to do with it; the new blueprint gives it an in-mod sink that feeds WDI's own fastener economy. A sheet comes from one iron bar (6 nuggets) and cuts into 8 rivets, so plate-cutting is a better metal-to-fastener rate than forging rivets one nugget at a time, payment for the extra bar-forging and heating work.
- Cast Iron Sheet card text rewritten: it previously said the sheet was "accepted anywhere a Cast Copper Sheet is required," which was only true with AdvancedCopperTools installed. It now names the rivet-cutting blueprint and states the ACT interchange as the conditional it is.

### Notes
- **Not verified in-game.** The blueprint, its tab registration, and its localization are statically verified only; nobody has yet crafted a sheet and cut it into rivets in a running game.

## [1.10.13] - 2026-08-16

### Fixed
- Partner station duties (Grinding Mill, Ore Sluice, Sawmill, Forge, Workshop) were registered at a base weight far below vanilla's own Partner duties, so duty selection almost always went to a native duty instead, a diagnostic play session showed a station duty reporting itself fully eligible and still never being picked all session. All five now register at a weight competitive with vanilla's, while staying below survival duties such as sleeping.
- Duty construction now aborts with a log line instead of reporting success when a reflected engine field is missing, so a future game update surfaces as a warning rather than a duty that silently attaches in an unusable state.

### Notes
- The weight change is **not verified in-game.** It addresses the measured cause of the duties never being selected; it does not prove they now run. All five duties remain unverified.
- A separate, still-open issue was observed in the same session: four of the five stations reported no reachable path for the Partner. That is under investigation and may simply reflect which structures were built in that particular save, it is not addressed here.

### Documentation
- README corrected: the architecture table said three grafted duties (now five), and the compatibility header still said EA 0.65 (the mod targets EA 0.66).

## [1.10.12] - 2026-08-16

### Added
- Water-Driven Forge and Water-Driven Workshop are now wired for Partner Duty Assignment, each with its own duty (`wdiOperateForge_Duty`, `wdiOperateWorkshop_Duty`) alongside the existing Fire keeping toggle. The Forge duty covers **Smelt Ore**; the Workshop duty covers **Hammer All** followed by **Smelt Ore**.

### Notes
- These two duties are **explicitly UNVERIFIED in-game** and are not advertised as working features. The Ore Sluice (1.10.10) and Sawmill (1.10.11) duties are likewise still unverified; only the Grinding Mill duty has been confirmed in-game.
- **Deliberately reduced scope:** these duties operate a forge that is *already at temperature*. Lighting and heating (Light Forge / Blast / Bellows) remain player actions. `Blast` is intentionally not automated, it burns fuel and leaves an Ash card behind on every press whether or not anything is loaded to smelt, and its own temperature gate stays satisfied at any lit temperature, so an autonomous Blast would repeat indefinitely. `Increase Temperature` (bellows) is not automated either: it precedes `Smelt Ore` in the interaction list under a looser gate, and the engine takes the first matching action, so marking it would prevent the smelt from ever being chosen.
- The Workshop duty performs `Hammer All` before `Smelt Ore` because hammering needs 50% heat and consumes none, while smelting needs 60% and spends a large part of it. This ordering only decides which runs *first* within a single pass, whether the smelt step runs at all still depends on the workshop being hot enough and on ore being reachable at that moment.
- These duties consume ore lying loose in the environment. The Partner does **not** look inside containers, so ore stored in the station's own inventory (or any chest) will not be picked up; leave it on the ground near the station.
- Same known limitation as the other station duties: the Duty Assignment toggle gates whether the Partner *does the work*, not whether the duty can be *selected*.

## [1.10.11] - 2026-08-16

### Added
- Water-Driven Sawmill is now wired for Partner Duty Assignment (`wdiOperateSawmill_Duty`), covering the drag-based **Cut** interaction. This is the first duty in the mod built on the `CardOnCardAction` path rather than a DismantleAction button.

### Fixed
- Duty ScriptableObjects created at load are marked `DontUnloadUnusedAsset`, so a Unity asset-unload can no longer silently disable the Grinding Mill, Ore Sluice, or Sawmill duties with no log output.

### Notes
- The Sawmill duty is **explicitly UNVERIFIED in-game**: it has never been observed running, and is not advertised as a working feature until an in-game pass confirms it. The Ore Sluice duty (1.10.10) is likewise still unverified. Only the Grinding Mill duty has been confirmed in-game.
- Known limitation, shared by all three station duties: the Duty Assignment toggle gates whether the Partner *does the work*, but not whether the duty can be *selected*, with the toggle off a Partner may still walk to the station and then idle. Tightening this would prevent the Partner from ever travelling to a station in another environment, so it is left as-is.

## [1.10.10] - 2026-08-16

### Added
- Ore Sluice can now be assigned to a Partner's Duty Assignment list, letting a recruited companion walk to it and press Sluice All on the player's behalf when toggled on.

## [1.10.8] - 2026-08-14

### Added
- Water-Driven Grinding Mill can now be assigned to a Partner's Duty Assignment list, letting a recruited companion walk to it and press Grind All on the player's behalf when toggled on.

## [1.10.7] - 2026-08-14

### Added
- Water-Driven Forge and Workshop can now be assigned to a Partner's fire-tending duty.

## [1.10.6] - 2026-08-11

### Added
- **Blast (Forge and Workshop) now warms the player.** Added a `StatModifications` entry to both stations' "Blast" action that pushes the vanilla `BaseTemperature` GameStat (ambient temperature, range -80..80) toward its +80 maximum by a fixed amount large enough to reach the cap in one use regardless of starting value, the stat's own clamp does the "push toward 80" work, so it never overshoots. Most useful in winter, when ambient temperature runs deeply negative.

### Changed
- **Forge and Workshop hold heat 3x longer.** Both stations' "Lose Temperature without fuel" and "High Temperature loss without windflow" `PassiveEffects` are now a third of their previous magnitude (−100→−33.3°/hour and −300→−100°/hour respectively), so the structure takes triple the time to cool from any given temperature, players need to Blast less often to keep smelting.

## [1.10.5] - 2026-08-09

### Fixed
- **Hammer All never raised Metal Quality on heated nuggets.** `IsMetalQualityTool`'s tag gate (`tag_Metal`/`tag_ToolBlank`/`tag_CopperSmall`/`tag_CopperBig`) never matched any real item, vanilla MetalNugget/MetalBarUnfinished carry no gameplay tags at all (the Workshop's own InventoryFilter has to allowlist MetalNugget by exact UID for the same reason), so every strike just burned down vanilla's own inert "Strikes" stat with no quality payoff. Dropped the tag requirement; the existing active-"...Quality"-named SpecialDurability2 check is a sufficient, safe signal on its own.
- **Natural Windflow went dead the moment fuel hit exactly 0**, even in a confirmed-windy environment (e.g. High Grove), the forge/workshop would just bleed heat via "Lose Temperature without fuel" with no windflow rescue, contradicting the "wind carries it to smelting temp on its own" description. The effect's gate now requires the structure already be substantially hot (≥790°, reached only through real fuel-burning) instead of requiring live fuel, an unlit, never-fueled forge still can't self-heat from ambient wind alone, but wind now carries an already-hot forge through fuel gaps as advertised.

### Technical
- Added a diagnostic breadcrumb to `IsWorkshopPrimaryStorageContext`'s 7-method stack-trace match, logs a `LogWarning` if it never matches across 50 calls, so a future game update silently renaming those methods surfaces as a log line instead of a permanently-silent Workshop storage-routing regression.
- Removed two unused sprite assets (`AlloySolder.png`, `CopperRivet.png`) whose items have referenced other sprite names (`act_tin_solder`, `CopperNails`) since they were added, dead files, no behavior change.

## [1.10.4] - 2026-08-09

### Fixed
- **Mill Race (North/South/East/West) improvements were appearing as buildable at indoor locations that have nothing to do with the outdoor mill-race network**: e.g. CMC's Village Inn interior. `InjectMillRaceImprovements`'s fallback pass (fills in directions unlinked by the static map) iterated every CardType-8 location in the merged mod data, including building interiors, instead of only locations that already have a validated mill-race edge. Scoped the fallback to locations that appear as an endpoint of at least one real map edge.

## [1.10.3] - 2026-08-07

### Fixed
- **9 stale Chinese translations re-synced to current English.** `Localization/SimpCn.csv` rows for the
  Water-Driven Forge (help text, Blast/Hammer-All/Smelt-Ore action text and fail messages, all now
  require flowing water), the Ore Sluice (now ballasted with heavy stones instead of iron
  bearing/axle hardware), the Fishpond drain action, the frozen Mill Race Outlet description (now
  mentions the Copper Brazier heat workaround), and the Iron Parts blueprint (simplified from
  "wrought iron bar" to "iron bar") had drifted from their English source after earlier content edits.
  Caught by the fleet Chinese-parity checker's stale-anchor check (E23).

## [1.10.2] - 2026-08-07

### Fixed
- **Sawmill and Forge construction wrongly demanded a Wooden Shovel AND a Metal Shovel simultaneously.** Both blueprints listed the two as separate `RequiredElements`, and blueprint tool matching is exact-reference (not tag-based), owning either shovel alone never satisfied the other slot. Both entries are now a single `GpTag_Shovel` tab-group requirement, so any one shovel (including the vanilla Antler) is accepted, matching the "bring a shovel" intent.
- **Sawmill's hammer-stage requirement only accepted the Metal Hammer**, silently rejecting the Forge Hammer and Stone even though all three are vanilla-curated as interchangeable hard hammering tools. Now uses the `GpTag_HammeringToolHard` tab group instead of a hardcoded single item.

## [1.10.1] - 2026-07-23

### Fixed
- **Blast-produced copper nuggets shipped at 0 quality.** `HandleBlastAllInner` checked `SpawnService.Spawn`'s return value to find the newly-created nugget, but `GiveCard` returns `void` in EA 0.65 so `Spawn` always returns `null` on success, the quality-init code never ran. Now uses the same pre/post ID-diff pattern as the Sluice/Grind/Fish paths.
- **README's Water-Driven Forge cooling claim didn't match its passive-effect stack.** "Cools −40°/hour when idle" wasn't reachable from any combination of the forge's temperature `PassiveEffects`, reworded to describe the actual behavior (fast heat loss with no fuel; needs windflow to hold heat above ~1000°).

### Changed
- Added `LogDebug` breadcrumbs to 8 previously-silent `catch` blocks across `ActionInterceptPatch.cs`, `GameLoadPatch.cs`, `MillRaceMapEdgeProvider.cs`, and `MillRaceNetwork.cs` so future reflection/version-drift failures on these paths leave a diagnostic trail instead of failing invisibly.

## [1.10.0] - 2026-07-20

### Added
- **Own fasteners, decoupled from Advanced Copper Tools**: WDI now ships its own Iron Rivet and Cast Iron Sheet items so it no longer hard-depends on ACT. When ACT is installed, its equivalents are accepted interchangeably via `Api.BlueprintAlternates` (no hard cross-mod dependency).

### Changed
- **Fishpond "Drain" now warns that the fish population is lost.** Draining a stocked pond back to a kit permanently discards its fish population (stored in the pond's durability stats), the action description now says so explicitly instead of the neutral "abandon the fish / drain the pond" wording, so the loss isn't a surprise.

## [1.9.0] - 2026-07-16

### Added
- **Grinding Mill now accepts every vanilla grindable** (wheat/rye grains → flour, edible acorns → acorn flour, bone splinters → bonemeal, charcoal → ash, and more). The mill's inventory filter previously accepted only `tag_Millable` items, a tag no vanilla item carries, so vanilla grindables could never enter the mill. The filter is now populated at load with every item carrying a grinding-tool interaction, the same detection Grind All already uses.
- **Chip Ice** action on the frozen Mill Race Outlet, 2 daytime, yields a vanilla Ice Block. The winter freeze is now a resource: stock an Ice Pit or melt for water.
- **Wood Shavings byproduct**: the Sawmill Cut action now yields 2 vanilla Wood Shavings alongside the 8 Planks, tinder from every log.
- **Ash byproduct**: every Forge/Workshop Blast leaves a pile of vanilla Ash (lye/soil uses).
- **Fishpond "Draw Unclean Water"**: drag a water container onto a filled or stocked fishpond to draw unclean water, the PLAYER_GUIDE already promised this; now it works. (Not available while frozen.)
- **Four new character-creation perks**: Prospector's Start (Ore Sluice Kit, 2 Moons), Angler's Start (Fishpond Kit, 2 Moons), Homesteader's Waterworks (Mill Race Outlet Kit + Mill Race, 2 Moons), and Millwright (+75 Woodworking head start, 30 Suns).

### Fixed
- **Fishpond Kit could not be placed anywhere** (regression from 1.7.0): the Place action's water requirement was authored in a malformed form that could never be satisfied, so the button always fell back to the "Requires Water" notice, even at a river. The JSON gate is removed; placement is now governed by the same mill-race water-access check as every other station kit (at a water source, or at a location with a connected Mill Race Outlet).
- **Mill Race Outlet Kit dismantle now returns the full build cost**: added the 4 Copper Rivets that were previously lost (the action already claimed to "recover all materials").

## [1.8.1] - 2026-07-12
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

## [1.7.0] - 2026-06-21

### Changed
- **Ore Sluice**: metal ore drop rates reduced across the board. Mud drop rates: Iron/Tin Nuggets 8%→3%, Copper Nuggets 12%→8%, Greenstone 22%→10%, Stone 55%→40%. Flint and Clay rates unchanged.
- **Water-Driven Forge**: now has 14 inventory slots for storing metal nuggets and clay items directly (previously no storage). Description updated to note natural wind heating in highland environments and that iron heats more slowly than copper.
- **Water-Driven Workshop**: description updated with the same natural wind heating note and iron heating clarification.
- **Fishpond (Frozen)**: description now clarifies that a frozen pond cannot be packed up; you must wait for the spring thaw.
- **Forge Start perk**: cost changed from 8 Moons to 1 Star.
- **Grinding Mill Start perk**: cost reduced from 8 Moons to 2 Moons.
- **Sawmill Start perk**: cost reduced from 8 Moons to 2 Moons.
- **Fishpond Kit**: shows a "Requires Water" message when you attempt to place it outside a river or lake environment.
- **Mill Race Outlet Kit**: shows a "Must be Outdoors" message when used inside a building.
- **Iron Parts**: now tagged as a high-temperature smelt item (requires 1100°C+); removed an old progress-based OnFull smelting path that is no longer needed.
- **Forge Iron Parts blueprint**: unlock condition text updated to "Wrought Iron Bar Needed".

### Fixed
- **Iron smelting**: iron nuggets and iron blooms now actually heat up in the WDI Forge and Workshop. A runtime fix injects the correct vanilla iron-smelting container tag so that iron's heat drain is cancelled by the forge's heat output. Previously iron would never reach smelting temperature.
- **Iron bar metal type**: iron bars smelted in the WDI Forge now correctly carry the iron metal-type flag. Previously bars could emerge as copper type due to a null-result bug in the GiveCard callback.
- **Fishpond population poll**: the fishpond's fish population growth timer no longer stalls during game pauses or transitions. Previously used `WaitForSeconds` which respects `timeScale=0`, causing polls to freeze while the game waited for player input.
- **Grind All**: now correctly detects output items for cards that produce results via the ProducedCards path rather than TransformInto; previously those items would produce nothing from Grind All.
- **Mill race network**: mod-injected world map locations (such as CMC's Village Path) are now automatically included in the mill race connectivity graph at load time.

### Technical
- Blueprint and item cross-references to ACT items updated to the current UID format (underscore-stripped), fixing silent lookup failures for any WDI recipe that requires ACT copper nails, sheets, or tools.

---

## [1.6.0] - 2026-06-12

### Technical
- Updated internal map data reference to EA 0.65 game data.
- Mill race edge provider updated for compatibility with EA 0.65.

---

## [1.5.0] - 2026-05-xx

- Quality of WDI items no longer decreases over time.
- Natural windflow mechanic added to the forge.
- Hammer All deduplication fix.
