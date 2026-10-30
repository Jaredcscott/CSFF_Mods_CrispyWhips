# CSFF Mod Framework

The shared base for Card Survival: Fantasy Forest mods. It loads mod content from JSON and wires it into the game, so a content mod needs C# only for logic of its own.

It also fixes several base-game bugs and speeds up travel to long-unvisited places.

- **Version:** 2.29.0
- **Game version:** EA 0.69c
- **Requires:** BepInEx 5.x
- **Languages:** English and Simplified Chinese

The first half of this page is for players. Mod authors start at [For mod authors](#for-mod-authors).

## For players

### Installation

1. Install BepInEx 5.x.
2. Extract the framework zip into `BepInEx/plugins/`. Its files land in `BepInEx/plugins/CSFF_Mod_Framework/`.
3. Install the content mods you want. BepInEx sorts the load order for you.

| File | Purpose |
|---|---|
| `CSFFModFramework.dll` | The framework |
| `LitJSON.dll` | JSON parsing, bundled so mods that use `LitJson` types load cleanly |
| `UnityGifDecoder.dll` | GIF decoding for animated cards (MIT, unmodified) |
| `ModInfo.json` | The framework's manifest |
| `ContentManifest.json` | The list of files this version ships, used to clean up after a copy-over update (below) |
| `SpriteCache/` | Created at runtime to cache decoded pictures. Safe to delete; it rebuilds on the next launch |

**Updating by copying over the old folder.** Release zips of this mod family now carry a `ContentManifest.json`. When you extract a new version over the old folder, the framework moves every card, text or picture file the new version no longer ships to `BepInEx/CSFF_Retired/<mod>/<version>/` at startup, so a leftover file cannot load as stale content. Files are moved, never deleted, and retired folders older than 30 days are removed.

It acts only when the manifest's version matches the mod's `ModInfo.json`, and it moves nothing when more than half the mod's files would go. The log has one line per mod naming the count. This has not yet been checked in a running game. Mod Update Manager replaces the whole folder when it installs, so it never leaves such files.

The framework does not ship stand-ins for Pikachu ModCore or ModLoader. A third-party mod that hard-depends on one of those needs that loader installed.

### What the framework adds

**Portal Hub.** The Portal Kit blueprint (Magic > Implements) is researched with a Witch Stone: bind a witch stone into fresh fur and dried wolfsbane, sealed with animal fat.

Set the kit up anywhere to get a Portal Hub with a "Pack Up Portal" button and one "Travel to [world]" button per installed world mod.

Arriving through the Hub puts an Exit card on the board that takes you back. It appears only after a real portal trip in the current run and disappears after 24 in-game hours.

After a reload or restart, the Exit returns you to where your placed Portal Hub stands, or to the one you left most recently if you built several.

The **Arcane Wayfinder** perk starts a character with a Portal Kit. The perk and the Portal Kit blueprint are hidden when no installed mod adds a portal world. Advanced Copper Tools, Community Mod Chest and Herbs and Fungi each add one.

**Shared metal parts.** The Metal Sheet (named Copper, Ghost Bronze, Tin, Tin Bronze, White Bronze or Iron Sheet by its metal), the Metal Nail and Tin Solder.

Six blueprints make them, in Metal & Clay > Metal Crafts: Metal Sheet (copper and every alloy), Iron Sheet, Cast Metal Sheet, Forged Metal Nail (every metal, iron included), Cast Copper Nails and Forge Tin Solder. The earlier Bronze Sheet and Forge Iron Nails recipes keep their UIDs as retired stubs that hand their research to the Metal Sheet and Forged Metal Nail.

A sheet or nail heats up in a stove or forge (named Heated, with a glowing picture) and cools outside it. Each has a few strikes by its metal; a hard hammer on a heated one spends a strike and adds Quality from your Smithing skill. A Tin Solder stick lasts five recipes: a recipe that takes it spends one use and keeps the stick.

A save from Chiwei's Additional Processing Equipment keeps its metal sheets, nails and solder when her mod is removed: `UidAliases.json` maps her IDs onto these cards.

These parts moved here from Advanced Copper Tools in 2.28.0 under the same IDs, so saves keep them and other mods can use them without ACT. With an older ACT that still ships them, the framework uses ACT's copies instead.

**Lamp Oil and Stearin Cake.** A liquid oil (`APE_LQ_LampOil`, Chiwei's ID) and eight Stearin Cakes (`APE_StearinCake`, her ID) made by the Lamp Oil blueprint in Survival > Fire: press a bowl of melted fat with a cloth and a heavy stone. A Stearin Cake seals, waterproofs and treats like fat and is added to the base game's Fat or Wax and Waterproofing Agent groups (candles, bows). It is in the framework's Oil group, so every lamp and flask that takes melted fat takes it (in proportion to the liquid), and it is not food. A knife on raw fatty or lean fish meat now also has a Mince action that gives fat and fish scraps. With an older Additional Processing Equipment that still ships Lamp Oil, the framework uses that mod's card and recipe instead.

**Brine and Concentrated Brine.** Drop salt on a container of water (a bowl, a pot, a Herbs and Fungi pickle vat) and the water becomes Brine (`APE_LQ_SaltWater`, Chiwei's ID); more salt raises its Saltiness, and at full saltiness it becomes Concentrated Brine (`APE_LQ_SaltSyrup`), which salts food left in it. Brine is the one plain brine every mod shares: Herbs and Fungi pickles in it. A mod's own brine joins the framework's Brine group (`GpTag_FW_Brine`) by carrying the tag `tag_x_Brine`. With an older Additional Processing Equipment that still ships these liquids, the framework uses that mod's cards instead.

**Curing in brine.** Food soaking in Brine is salted, so it spoils at three quarters of the speed. Raw meat or raw lean fish in a Clay Bowl holding at least 100 Brine becomes Meat in Brine or Fish in Brine and, about two days later, Salt-Cured Meat or Salt-Cured Fish (`advanced_copper_tools_salt_cured_meat`, `advanced_copper_tools_salt_cured_fish`: Advanced Copper Tools' IDs, which these cards keep since they moved here in 2.29.0). Taken out early it stops curing; Rinse off the Brine turns it back into raw meat. The cure time (192 ticks) and the brine it uses (half a unit a tick for each piece soaking) are placeholders.

**Values kept on one card.** A mod can remember a number on a single card (where its metal came from, how well its parts were worked) and keep it in your save. The framework writes these beside each save, in a `CSFFModFramework` folder inside the save slot's own folder, one small file per save. Deleting the slot deletes them; a save without them simply starts with none. Advanced Copper Tools' Large Saw is the first to use it.

**Perk origin tags.** Every perk a mod adds shows a short tag after its name, such as `Swimmer [CMC]`, so perks from different mods can be told apart. Vanilla perks get no tag.

The tag shows in the character creation lists, the selected-perk panel, the character select card and the in-game character sheet, including their tooltips. It is display only: perk names in saves and stat reports are unchanged.

It is confirmed on the character sheet and in the WikiMod tooltip, and in the character creation lists in a scripted run. Switch it off with `ShowModOriginTag`.

**Card motion.** Mods can ask for a little movement on their art: NPCs and animals breathe softly and plants sway. The framework bends the card's own picture, so there are no extra image files and no extra texture memory, and with 24 moving cards on the board it measured about a tenth of a millisecond per frame.

It has been seen working on board cards and in the inspection popup, and in the encounter picture in a scripted run. Switch it off with `CardMotion`.

**Animated card art.** A mod can give a card a looping GIF, such as a fire that flickers while it is lit or a water wheel that turns. An animation tied to a stat (a kettle that steams only while hot) starts and stops as the stat changes, and the still picture comes back when no animation applies.

### Bug fixes

These are always on and need nothing from you.

- Two instanced environments at the same map spot (for example two player-built cabins) no longer lock every action, whether on travel or when you place a structure kit.
- The vanilla Animal Enclosure and Chicken Coop no longer lock every action when you enter them.
- A broken card entry can no longer turn an autosave into a permanent "I can't do two things at once..." lock.
- Arriving through the Portal Hub no longer leaves you in no environment.
- A travel button that went dead after the map changed is rebuilt when you open the travel screen.
- Leaving a passive effect that multiplies a stat now undoes the multiply for every stat, not only spoilage.
- NPC rows with three buttons (Talk, Trade and Commissions) fit their panel, and long button labels shrink to fit.
- An error in another mod's code no longer breaks the blueprint screen or the travel screen.
- Placed blueprint containers that lost a contained blueprint get it back on load, without freezing the game.
- Card swaps (the jail bed to tunnel, pickle vats, the Tea Station) and stat-gated actions work on EA 0.69c.
- Mod rooms no longer share one saved board, so the Inn's cards and the Bear Cave's bear stay out of the jail cell and the other mod rooms.
- Animal tracks that have already run out are dropped before each save. The game only clears them where you stand, so on a long run every other place kept every track ever left there, and the autosave every two in-game hours could freeze the game for seconds. Tracks you can still follow are kept.
- The framework's load-time cleanup leaves the base game's own cards exactly as they ship, so "Wash yourself" at a river no longer drains the fuel from every card on the board.
- A mod perk that picks your starting place starts you there: Far Territory's Island Start puts you on Landfall Strand, not at River Clearing.
- In Chinese, the popup for a mod animal or person shows its translated name as its description, not the English one.

### Compatibility with other mods

- **CardSizeReduce** scales cards again on EA 0.62b and later. `SlotScaleFactor` sets the base and location slot lines.
- **CheatsPatch** finds the game's renamed cheats class.
- **WikiMod:** a patch that no longer fits this game version is skipped and the rest load. Its Chinese stat names stay out of an English game, and errors in its data pass and quick-find window are caught. The first two fixes can be switched off.
- **Pikachu ModLoader:** when it is installed, the duplicate copies of mod content it creates are removed, so blueprint research no longer resets on load.

### Faster travel and smoother play

Entering a place you have not visited for a long time makes the game replay every missed 15-minute tick, which could freeze it for over a minute on old saves.

The framework caps that replay at 14 in-game days, limits it to 2 seconds of real time and replays the ticks in batches.

It also throttles off-screen cards, caches decoded pictures so later launches load faster, pre-sizes the animation pool and cuts per-frame log spam. Two more options are off by default: compressed mod pictures, and slower decision-making for NPCs outside your current location.

### Configuration

Settings live in `BepInEx/config/crispywhips.CSFFModFramework.cfg`, created on first run. BepInEx reads it once at startup, so quit to desktop and relaunch after a change. The optional [BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases) (BepInEx 5 build) shows them in game (press F1 and find "CSFF Mod Framework"), but the framework reads nearly all of them at startup, so a change made there also needs a relaunch.

| Section | Key | Default | What it does |
|---|---|---|---|
| General | `VerboseLogging` | `false` | Detailed per-item log lines, for bug reports |
| General | `ForceChineseMode` | `false` | Loads the Chinese text whatever the game language, for testing translations |
| General | `EnableLoadDiagnostics` | `false` | Two extra scans at load, for investigating research-timer problems |
| Performance | `OffScreenCardThrottleEnabled` | `true` | Updates off-screen, still cards less often |
| Performance | `OffScreenCardThrottleFrames` | `3` | Throttled cards update once every N frames (2 to 10) |
| Performance | `DOTweenTweenerCapacity` / `DOTweenSequenceCapacity` | `1000` / `200` | Pre-sizes the animation pool to avoid stutter mid-session |
| Performance | `DOTweenQuietLogs` | `true` | Logs animation errors only |
| Performance | `SlotAssignmentLogSuppressEnabled` | `true` | Removes a per-frame warning the game logs on busy boards |
| Performance | `AmbienceArrayReuseEnabled` | `true` | Stops the ambience effect allocating memory every frame |
| Performance | `CatchUpTickCap` | `1344` | Most missed ticks replayed when you enter a place (1344 = 14 days). `0` turns off this cap only; the budget and batching below stay on until set to `0` too |
| Performance | `CatchUpChainCapTicks` | `672` | A smaller cap (7 days) for a hop that follows another capped hop within the window below. Decay skipped on a pass-through place is not made up later |
| Performance | `CatchUpChainWindowSeconds` | `180` | Real seconds after a capped hop during which the smaller cap applies |
| Performance | `CatchUpBudgetMs` | `2000` | Real-time budget for one replay; once spent, the rest of that hop's ticks are skipped. `0` disables |
| Performance | `CatchUpMinTicks` | `384` | A replay is never cut shorter than this, so short-lived food still finishes rotting |
| Performance | `CatchUpBatchTicks` | `16` | Replays missed ticks in chunks of up to this many. `0` disables |
| Performance | `CatchUpBatchMinTicks` | `96` | Replays shorter than this are not batched |
| Performance | `CatchUpEquipmentScanSkip` | `true` | Skips the equipment check during a replay, where no replayed card is equipped. `false` restores the vanilla check |
| Performance | `LogCatchUpEquipmentScanHits` | `true` | Logs a line if an equipment check during a replay returns true. It can only see this while `CatchUpEquipmentScanSkip` is `false` |
| Performance | `CompressModSprites` | `false` | Compresses mod pictures to about a quarter of their memory; art can look slightly softer. The picture cache rebuilds once after a change |
| Performance | `OffEnvNpcAiThrottle` | `false` | NPCs outside your current place choose their next task every second tick. Their stats and actions still run every tick; companions are never throttled |
| Wildlife | `WildlifeRaidsEnabled` | `false` | Once a day, wildlife may spoil food in open, unguarded containers |
| Wildlife | `WildlifeRaidDailyChance` | `0.35` | Daily chance of a raid while raids are on |
| Wildlife | `WildlifeRaidStressPenalty` | `2` | Stress added by a successful raid |
| Wildlife | `BearRaidChance` | `0.5` | Needs raids on. Chance (0 to 1) that a fight with the brown bear also spoils one food item in an open container where you are. Sealed containers are safe, and fights with people never trigger it |
| Wildlife | `BearRaidEscalationDays` | `0` (off) | Needs raids on. After this many days in a row with a raid where you are, the next raid is a brown bear fight. Guarded places, such as the Community Mod Chest village, stop it. The count resets on loading a save |
| Visuals | `CardMotion` | `true` | The breathing and swaying on mod art that asks for it. `false` keeps every card still |
| Visuals | `CardMotionUpdatesPerSecond` | `20` | How often moving cards are redrawn (5 to 60). Lower costs less |
| Perks | `ShowModOriginTag` | `true` | Shows the mod tag after modded perk names. `false` hides it |
| Animals | `AnimalsEnabled` | `true` | Loads the roaming animals mods declare. `false` removes them all |
| Quests | `EnableQuestInjection` | `false` | Leave off. Attaching mod quest logs to characters once reset blueprint research in a player's save, and the cause was never found |
| Compatibility | `RescueStaleWikiModPatchAll` | `true` | Skips only the WikiMod patch that no longer fits, so the rest of WikiMod still loads |
| Compatibility | `GuardWikiModChineseInEnglish` | `true` | Runs WikiMod's built-in Chinese stat names only when the game is in Chinese, so English stat labels (Quality, Freshness, Temperature) stay English |
| CardScale | `SlotScaleFactor` | `0.75` | Base and location slot scale when CardSizeReduce is installed (0.25 to 1.0) |
| Diagnostics | `AuthoringLint` | `true` | Logs one warning per mod that ships JSON which loads but does nothing, and one per field two mods both replace or per CardTabGroup file that matches a vanilla group. Silent when nothing is found; changes nothing |
| Diagnostics | `LogAutoSaveCalls`, `LogBlueprintSearchMisses`, `LogTrackTiming`, `LogWildlifeStuck`, `LogFrameTiming`, `LogCatchUpEndState` | `false` | Extra log lines for bug reports: autosave call stacks, crafting-journal search misses, travel timing, wildlife stuck on one node, a frame-time summary every 60 seconds, and the state of cards after a travel replay. Each installs nothing unless set to `true` |

### Other mods by the same author

| Mod | Plugins folder | What it adds |
|---|---|---|
| Advanced Copper Tools | `Advanced_Copper_Tools` | Copper and bronze metalworking, wheelbarrow, bathtub, stove, lantern, oil chain, tea kettle, tea blending station, copper chest |
| Community Mod Chest | `Community_Mod_Chest` | Community-suggested apparel, weapons and armor, character traits, pottery, decorations, fishing gear and a village east of the River Clearing |
| Herbs and Fungi | `Herbs_And_Fungi` | Herbalism, mushroom foraging, hemp farming, oil press, pickling, drying racks, medicinal teas, perks |
| Sirus23 Mod Collection | `Sirus23_Mod_Collection` | Wolf, fox and owl companions, sheep husbandry and felt-working |
| Water Driven Infrastructure | `Water_Driven_Infrastructure` | Water wheels, sawmills, forges, grinding mills, ore sluices, fishponds and mill race outlets |
| Quick Transfer | `Quick_Transfer` | Shift, Ctrl and Ctrl+Shift right-click to move several cards at once |
| Repeat Action | `Repeat_Action` | Repeats your last action, with keybinds and safety limits |
| Skill Speed Boost | `Skill_Speed_Boost` | Per-skill XP multipliers, difficulty profiles, staleness decay, synergies, level scaling |
| Homestead Perks | `Homestead_Perks` | Forty-four character creation perks; nineteen grant a placeable structure kit (Homestead, Cabin, Mud Hut and more) |
| Partner Overhaul | `Partner_Overhaul` | Fixes for vanilla Partner bugs: eating stew and broth, resuming cleaning, garden over-watering, fireplace fuel, map-wide sounds and interrupted dialog |
| Mod Update Manager | `Mod_Update_Manager` | Nexus update checker (F3), plus a one-click installer and updater for this mod family |

Each mod's current version is in its own `ModInfo.json`.

Four more are in early development and install through Mod Update Manager as pre-release: Forest Lore (`Forest_Lore`), Far Territory (`Far_Territory`), Harsh Wilds (`Harsh_Wilds`) and Utility Belt (`Utility_Belt`), which carries its own copies of Repeat Action, Quick Transfer and Skill Speed Boost.

Every content mod above declares a soft dependency on the framework, which only sets load order. No content mod declares a dependency on another.

Some add extra content when a partner is installed, such as Herbs and Fungi oils in ACT, ACT's chest and armor in CMC, and bronze gears in WDI.

Repeat Action and Mod Update Manager do not use the framework; Mod Update Manager bundles copies of the other mods for its installer but runs without them.

Quick Transfer and Skill Speed Boost use the framework for part of their features. They load without it, but those features fail.

## For mod authors

Everything in this section happens automatically once a mod ships the file or folder named. Write C# only for logic of your own: custom action handling, vanilla card patching, or Harmony patches on gameplay methods.

To share cards, tags and recipes with other mods, start at [Making mods work together](#making-mods-work-together) (Chinese: [README.zh.md](README.zh.md)).

### Content folders

Each top-level folder name is the type it holds, matching the vanilla JSON export layout: `CardData`, `CharacterPerk`, `PerkGroup`, `GameStat`, `SpiceTag`, `FlavourTag`, `NPCStat`, `NPCDuty`, `NPCHidingGroup`, `NPCAgent`, `NPCCharacterPerk`, `Encounter`, `SelfTriggeredAction`, `Objective`, `QuestLog`, `GameModifierPackage`, `PlayerCharacter`, `CookingRecipeGroup`, `ConstructionCardGroup`, `BookmarkGroup` and `LocalTickCounter`.

Any other ScriptableObject type loads from `ScriptableObject/<TypeName>/`. Objects with a UniqueID go into the game's UID table and the rest are registered by name. Their `*WarpData` references resolve, including those inside non-UID objects such as dialog. Check support at runtime with `Api.Framework.SupportsContentType(name)`.

Most of these types need nothing more, because the game itself picks them up at run start: `CookingRecipeGroup`, `BookmarkGroup`, `ConstructionCardGroup`, `NPCCharacterPerk`, `SelfTriggeredAction`, a `FlavourTag`, and a `GameModifierPackage` referenced from a character's `EasyPackageWarpData`. The exceptions have their own files below.

`Resource/` holds pictures (`Resource/Picture/*.png`), audio, GIFs (`Resource/GIF/*.gif` with `CardData/Gif/*.json`) and asset bundles. `CardData/Motion/*.json` maps sprite names to a card motion profile: `"Sprites": { "<sprite>": "npc" }` with `npc`, `animal`, `plant`, `location` or `still`, or inline numbers (`Breathe`, `Sway`, `Drift`, `Period`), and an optional `"Profiles"` object names your own. No image files are needed. `Localization/` holds the CSV text (`SimpEn.csv`, `SimpCn.csv`).

### WarpData

A `*WarpData` key resolves a UniqueID, GUID, tag or sprite name into the field it names, in arrays and `List<T>` fields alike, and creates tags that do not exist yet. It also reaches a private base-class field, such as an action's `ActionSoundsWarpData`. A sound field (`ActionSoundsWarpData`, `WhenCreatedSoundsWarpData`, `BackgroundSoundWarpData`) takes the name of a sound the game has, such as `Smithing` or `WoodAppear`; material words such as `Metal` or `Wood` are not sounds. A name that matches no sound is dropped, and one Info line per mod lists such names.

Every `*WarpData` key needs its `*WarpType` sibling: 3 Reference, 4 Add, 5 Modify or 6 AddReference. Without one the key resolves nothing, and a boot warning lists it with the mod it came from.

### Declarative files

| File | What it does |
|---|---|
| `BlueprintTabs.json` | `{ "<tab LocalizationKey>": ["<blueprint UID>", ...] }`. Puts construction blueprints in a crafting tab, matched by exact key. An unknown key sends the blueprint to Survival > Support once the blueprint screen is open. A `"NewSubTabs": [{ "Key", "Parent", "DefaultText", "Icon", "After" }]` entry in the same file adds a subtab under the main tab keyed `Parent`; list blueprints under its `Key` |
| `SmeltingRecipes.json` | Smelting recipes in the vanilla Furnace and every station tagged `tag_SmeltingContainer` (the vanilla Bloomery, WDI's forge and workshop). Not the vanilla Forge |
| `DropInjections.json` | Adds drops to location card actions such as Forage, optionally by season |
| `InjectImprovementInto.json` | `[{ "TargetEnvUID": "<CT8 UID>", "ImprovementUID": "<CT10 UID>" }]`. Makes an environment improvement buildable at a location |
| `TradingValues.json` | `{ "<CardData UID>": <number> }` written onto `CardData.TradingValue`. Keys starting with `_` are comments. Later mods win a conflict (with a warning); a targeted `GameSourceModify` patch still wins over both |
| `TagGroups.json` | `[{ "Name": "GpTag_...", "Label": "...", "LabelKey": "...", "Tags": ["tag_..."], "ExtraCards": ["<UID>"], "ExcludeLiquids": false }]`. A card group filled at load with every card that carries one of the tags (from any mod) plus the extra cards, for a blueprint slot's `RequiredTabGroupWarpData` or a liquid check's `RequiredGroupWarpData`, which cannot name a tag. The framework ships `GpTag_FW_Oil` (any oil, with vanilla rendered fat), `GpTag_FW_OilIngredient` (any oil that is not a liquid) and `GpTag_FW_Brine` (`tag_x_Brine`, with the framework Brine) |
| `LiquidConversions.json` | `[{ "Solid": "<CardData UID>", "Liquid": "<CT9 CardData UID>", "Quantity": 5 }]` (2.29.0). Lets a powder go into a jar, pouch or barrel as its liquid form and come back out with the liquid's Empty button, as vanilla salt and flour do. `Quantity` is the liquid units one solid card becomes (vanilla: 5 for a pinch-class powder, about 1.5 x `ObjectWeight` for bulk); it must be above 0. Give the liquid `tag_PowderLiquid`, as vanilla's powder liquids have, so a Pouch takes it. A card can be in one pair only: across mods the first in load order keeps it, a later row naming it is skipped with a warning naming both mods, and a pair naming a card vanilla already pairs is skipped. Vanilla's own pairs always win |
| `KitContents.json` | `[{ "Kit": "<kit card UID>", "ActionNamePrefix": "Place", "Name": "...", "LogTag": "...", "Items": [{ "Card": "<UID>", "Count": 15, "Stats": { "SpecialDurability4": 1 }, "OnlyIfCardExists": false }] }]` (2.29.0). When the player's action on the kit card ends, each item spawns `Count` times (1 to 100) on the current board, in order, with its stat values. An `OnlyIfCardExists` item is skipped, with one Debug line (`LogIfMissing`), when its card is not loaded. A kit card can be declared once across all mods |
| `StatResets.json` | `[{ "Stat": "<GameStat UID>", "Latch": "<hidden saved GameStat UID, 0..1>", "Target": "default", "When": "FirstTick", "LogTag": "...", "LogReset": "... {before} -> {target} ...", "LogUnchanged": "..." }]` (2.29.0). Resets a stat's base once per save, for a mod whose older versions wrote the stat permanently. The base goes to `Target` (`"default"`, the stat's own base value, or a number) through the game's own stat change, is checked, and only then is the latch set to 1, so a failed reset is tried again. `When`: `FirstTick` (on each in-game tick until done) or `RunStart` (as the save loads, then each tick until done). `LogReset` is written at Info, `LogUnchanged` at Debug when the base was already there (without it, `LogReset` either way). Two rows may reset one stat; a latch belongs to one row across all mods |
| `CardData/Trigger/*.json` | Spawns a card on the player's board on a timer |
| `SelfTriggeredAction/*.json` | Vanilla stat-triggered events. The game runs them; the framework checks them at load |
| `EncounterGuards/*.json` | Stops encounters while a guard card or guard place is present |
| `FlavourMatrix/*.json` | Flavour synergy pairs between flavour tags |
| `Modifiers.json` | `{ "AutoApplyPackages": ["<GameModifierPackage UID>"] }`. Applies packages to every new game |
| `Quests.json`, `Characters.json` | Attach quest logs to characters, and add characters to the select screen |
| `MapMod.json` | Registers a world on the Portal Hub |
| `WorldMap/MapNodes.json`, `WorldMap/FullMap.json` | New map locations and connections |
| `Animals/*.json` | Roaming animals from one manifest each |
| `GameSourceModify/*.json` | Patches an existing vanilla or mod object in place |
| `UidAliases.json` | `{ "old_uid": "new_uid" }` beside `ModInfo.json`, for a card you renamed or merged. Any mod's `*WarpData` naming the old UID resolves to the new card, and the old UID is registered as a second key so a save that names it finds the new card. An old UID that is still a real card is never shadowed. Aliasing a removed **environment** to a live one (2.28.5) also moves a save made inside it: the removed board's cards are folded into the target's board and the player arrives there. The removed-environment half was measured on a test rig on 2026-10-15 (a save made inside ACT's Metal Mines and one inside H&F's Foraging Path both landed at their vanilla place with every card); the renamed-card half is not confirmed in a running game yet |

**Smelting rows** look like `{ "Item": "<uid>", "NuggetReturn": 3, "Duration": 32 }`. Optional `"MetalType"` (a number above 0 and up to 1000: Copper 100, Tin 120, Iron 200) sets the Metal Type of the returned Metal Nuggets, which otherwise come back as Copper.

Optional `"InheritMetal": true` returns nuggets with the smelted item's own Metal Quality and Metal Type, the vanilla melt rule. An explicit `MetalType` still wins for Metal Type.

**Drop injections:**

```json
[
  {
    "Locations": { "Uids": ["exact_uid"], "CardNameKeyContains": ["GroveOak"], "Tags": ["tag_ForestBiome"] },
    "Action": "Forage",
    "ActionMode": "exact",
    "Drops": [
      { "Card": "my_mod_item", "Chance": 8.0, "Quantity": [1, 1] },
      { "Card": "my_mod_chanterelle", "Chance": 4.0, "Seasons": ["Spring", "Summer", "Autumn"], "SeasonChance": { "Autumn": 12.0 } }
    ]
  }
]
```

A rule fires on any location matching one of `Uids` (exact), `CardNameKeyContains` (substring of `CardName.LocalizationKey`) or `Tags`. `Action` is the action's `ActionName.DefaultText`, matched exactly or, with `"ActionMode": "contains"`, as a substring. `Chance` is 0 to 100, and `Quantity` defaults to `[1, 1]`.

`Seasons` (Spring, Summer, Autumn or Fall, Winter) limits a drop to those seasons, the way vanilla seasonal forage works. `SeasonChance` gives a season its own absolute chance, higher or lower than `Chance`.

A bad season entry is ignored with a warning and the rest still apply. A card already in the action's drops is skipped.

**Spawn triggers** (`CardData/Trigger/*.json`) take these keys:

- `UniqueID`, and `TriggerCardWarpData`: the card to spawn.
- `SpawnChance`: 0 to 100.
- `TriggerFrequency`: in ticks; 96 is one day, the default.
- `MaxOnBoard`, with `MaxOnBoardWarpData` listing the cards that count toward the cap.
- `SpawnLocation`: above 0 means outdoors only, the default.

Day counters reset on restart. For stat or season conditions, use a `SelfTriggeredAction` instead.

**Encounter guards**, one per file:

```json
{
  "Name": "WolfCompanionGuard",
  "GuardCardUids": ["fc_wolf_companion"],
  "GuardEnvironmentUids": ["myEnvVillage"],
  "EncounterUids": [],
  "AgentUids": [],
  "SuppressChance": 100
}
```

A guard needs `GuardCardUids` or `GuardEnvironmentUids`, and a match on either in the player's place counts. With `EncounterUids` and `AgentUids` empty, it stops every encounter that starts without an NPC; a non-empty `EncounterUids` narrows it to those encounters.

An NPC agent's own attack is stopped only when the guard names the agent in `AgentUids` or the encounter in a non-empty `EncounterUids`. A companion guard with both lists empty never reaches one.

A fight the player chose (Approach, a dialog's attack answer, Give Chase) is never stopped, and a save resuming a fight is never cancelled.

**Quests and characters.** Do not ship `Quests.json` on a released mod. Attaching a mod quest log through it once reset blueprint research on save load, and the cause was never found. Nothing attaches unless the player sets `EnableQuestInjection`.

The format is `{ "Quests": [ { "QuestLog": "<uid>", "Characters": ["Huntsman"] } ] }`; an empty `Characters` list means all characters.

`Characters.json` is `{ "Characters": [ { "Character": "<uid or name>", "Roster": "Fates" } ] }`, with `Fates`, `Ways` or `Both`. It has never been tested in a real game, and neither has removing a mod whose character a save uses.

**Game modifier packages.** To tie a package to a character, reference it from the character's `EasyPackageWarpData` (`EasyPackageWarpType: 3`); the player toggles it as the Easy Package at character creation. `Modifiers.json` applies packages to every new game whatever the character, adding their `StartingStatModifiers` and `AddedCards`.

It affects new games only, has no opt-out for the player and adds to a character's own Easy Package. It works in a running game on a test mod; no released mod ships one yet.

**GameSourceModify.** `GameSourceModify/<Name>.json` patches the object named by the file name or an inner `"UniqueID"`: a card, perk or stat, or a non-UID object such as a `WeaponMove`, `DamageType` or `CardTag`. `MatchTagWarpData` patches every card with a tag, and `MatchTypeWarpData` every card of a CardType.

Arrays are replaced, so an empty array erases the target's list and logs a warning. Use `"_appendArrays": { "FieldName": ["uid1", "uid2"] }` to add entries instead.

A patch can only reach objects loaded at the main menu. Scene objects, such as the stat tabs, need a C# patch.

### Perks

- A perk goes into the PerkGroup named by `CharacterPerkPerkGroup` (Situational by default) and is removed from groups the game put it in by itself. `"CharacterPerkPerkGroup": "None"` keeps it out of every group, for perks granted only during a run through `AddedInRunPerksWarpData`.
- A perk's starting place (`OverrideEnvironmentWarpData`) is kept when it names an environment card a mod ships. One that names a base-game board is cleared at load, since a mod perk usually carries that by accident.
- `AddedCardsStats` and `EquippedCardsStats` give a perk's starting items real values instead of the card defaults (quality 0, Plank ID 0, leather Skin Type 0). Liquids are not covered.
- Write them as `[ { "Card": "<uid>", "SpecialDurability4": 1 } ]` beside the matching `...WarpData` list, with `Card` exactly as it appears there.
- On `EquippedCards`, every card from the first declared one on is equipped in the perk's own order, so a container listed before its contents exists before they are handed out.
- The origin tag comes from the optional `"ShortName"` in your `ModInfo.json` (for example `"CMC"`): at most 8 characters, with whitespace and `[ ] < >` removed.
- Without it, the tag is the initials of the mod's `Name` plus any digits, skipping small words, up to 5 characters (`Sirus23 Mod Collection` gives `S23MC`). A one-word name gives its first 3 letters.

### Mod discovery and Pikachu ModLoader

The framework looks for `ModInfo.json` up to two folder levels deep under `BepInEx/plugins/`. If two folders hold a mod with the same name, it loads the one with more content files.

When Pikachu ModLoader or ModCore is installed, a mod whose `ModInfo.json` has the `ModLoaderVerison` field is left to that loader. Leave the field out of a framework mod, along with `ModEditorVersion`.

A mod with that field is still loaded by the framework when it ships a framework-only file: `BlueprintTabs.json`, `SmeltingRecipes.json`, `DropInjections.json`, `InjectImprovementInto.json`, `TradingValues.json`, `WorldMap/MapNodes.json`, `WorldMap/FullMap.json`, `EncounterGuards/*.json`, `Quests.json`, `Characters.json`, `MapMod.json`, `Modifiers.json`, `LiquidConversions.json`, `KitContents.json`, `StatResets.json`, `FlavourMatrix/*.json`, `Animals/*.json`, or a `GameSourceModify/` patch using `MatchTagWarpData` or `MatchTypeWarpData`.

### Unresolved references

A row in a declarative file that names a UID nothing loaded is skipped and logged, and nothing is created for it. Each file's Info summary counts the skips, so a player's log shows them with `VerboseLogging` off.

The log level follows the likely cause. A UID from a partner mod that is not installed logs at Debug (drop injections, trading values, a `BlueprintAlternates` alternate), and an authoring error at Warn or Error.

`GameSourceModify`, `InjectImprovementInto.json`, `LiquidConversions.json`, `KitContents.json` and `StatResets.json` choose per UID: Warn when a GUID, vanilla name or installed mod should have supplied it, Debug otherwise.

Three kinds keep the row, because dropping it would change what it protects. A gate condition naming a missing UID reads false (`ConnectionGates`, `SealableGates`, a `ConditionalDrops` condition), and an `EncounterGuards` `EncounterUids` or `AgentUids` entry stays so the list never empties into "every encounter".

A missing `PathCardUID` falls back to the node's own location card. `Api.RecipeInjector` skips a null `CompatibleCards` entry, and injects nothing (returning false) when every card is null and no tag is given.

### Authoring lint

After loading, the framework warns once per mod about JSON that loads cleanly and then does nothing:

- an out-of-range flavour `Intensity`
- a misspelt durability key, or a durability block that sets values without `Active`
- a `TransferRules` bool no mask backs
- a flat drop list
- a ScriptableObject name another mod holds
- a `CardTag` file named like a vanilla tag

It also warns about another mod's UID in a structural field, such as a blueprint ingredient or a drop, when your plugin declares no hard dependency on that mod.

Three cases are exempt: a card-on-board condition that is not `Inverted` (it just stays unmet), a drop in an NPC agent's own `AgentActions[].DroppedCards`, and the framework's own metal parts. The lint reports only. Switch it off with `AuthoringLint`.

The same switch covers two conflict warnings (2.29.0):

- **Two mods replacing one field.** A `GameSourceModify` patch replaces a field when it writes it as a plain key, or as `<Field>WarpData` with WarpType 3 or 5; it appends with `_appendArrays` or WarpType 4 or 6. When two mods replace the same field of the same object, the log names both mods, the object and the field once. Append instead wherever you can.
- **A CardTabGroup file that matches a vanilla group.** A `ScriptableObject/CardTabGroup/` file named like a vanilla group, or carrying a vanilla group's `TabName.ParentObjectID`, gets one warning. Declare groups in `TagGroups.json` and place blueprints with `BlueprintTabs.json` instead.

### Adding a map location

Ship `<YourMod>/WorldMap/MapNodes.json`. The easiest way clones a vanilla location: the framework copies its environment (CT4) and explorable location (CT8) cards under your new UIDs, keeping every tag, ambience, improvement list and tree drop pointing at the live vanilla objects.

Cloning is the only way to inherit vanilla's tags. Vanilla JSON exports name tags by obfuscated asset names, which must never be copied into mod JSON.

```json
[
  {
    "EnvironmentUID": "cmcEnvVillagePath",
    "LocationUID": "cmcLocVillagePath",
    "CloneOfEnvironmentUID": "5af32ab7ce936684d99add01fc56015a",
    "DisplayName": "Village Path",
    "NameLocalizationKey": "CMC_VillagePath_CardName",
    "EnvNameLocalizationKey": "CMC_Env_VillagePath_CardName",
    "Coords": { "x": 10.0, "y": 0.0, "z": 0.0 },
    "Connections": [
      { "EnvironmentUID": "2b19b942a09fdd148a43798e942a74eb", "PathCost": 10.0 }
    ]
  }
]
```

To ship your own environment card instead, omit `CloneOfEnvironmentUID`; `EnvironmentUID` must then name your CT4 card.

Environment UIDs must not contain underscores. The game splits saved environment keys on `_`, so the cards saved in such a place are never restored.

Clone an outdoor location, never an interior. An instanced environment such as a cabin is refused with an error, because it would crash a later trip.

| Field | Required | Description |
|---|---|---|
| `EnvironmentUID` | Yes | UID of the CT4 environment card. With `CloneOfEnvironmentUID`, the new UID given to the clone |
| `CloneOfEnvironmentUID` | No | Vanilla CT4 UID to copy (Green Tangle is `5af32ab7ce936684d99add01fc56015a`). It must have a CT8 location card in its `DefaultEnvCardDrops`, as every standard location does |
| `LocationUID` | With a clone | New UID given to the cloned CT8 card |
| `DisplayName` | With a clone | Name for both clones (new strings are built, so the template's are untouched) |
| `NameLocalizationKey` / `EnvNameLocalizationKey` | No | CSV keys for the names. Defaults: `<LocationUID>_CardName` and `<EnvironmentUID>_CardName`. Add the rows to `Localization/SimpEn.csv` |
| `Coords` | No | Map position. Vanilla uses a 10-unit grid, x east and z north; the River Clearing is `(0,0,0)` and the Green Tangle `(-10,0,0)`. Pick a cell no vanilla location and no other mod's map file uses |
| `HideFromMap` | No | `true` hides the node on the travel map. Default `false` |
| `Icon` | No | Sprite name for the map icon. A clone defaults to the template's icon |
| `Connections` | No | Travel links to other environments |

| Connection field | Required | Description |
|---|---|---|
| `EnvironmentUID` | Yes | The connected CT4 environment, vanilla or another node in the same file |
| `PathCardUID` | No | Travel card for the link. Defaults to the node's own location card, the vanilla convention |
| `PathCost` | No | Travel cost; vanilla uses `10.0`, the default |
| `TravelDirection` | No | `"North"`, `"South"`, `"East"`, `"West"` or `{"x":…,"z":…}` (z +1 is north, x +1 east). Omit it to derive the direction from the two nodes' `Coords` |
| `TravelActionTags` | No | Action tag names for the travel action. Omit to inherit the template's, as vanilla travel does |
| `HideConnection` | No | `true` hides the line on the map. Default `false` |

Every connection is mirrored: declaring A to B also creates B to A in the opposite direction, using B's own location card and travel tags.

**Further node fields.** `MapNodeDefinition` and its nested types in `Loading/WorldMapLoader.cs` document these in full.

| Field | What it does |
|---|---|
| `CapacityStats` | Overrides SpecialDurability1 to 4 on the cloned CT8: the caps and regrowth of Trees, Overgrowth, Foraging and Fertility |
| `ConnectionGates` | Shows or hides a connection and its travel button by condition. `HideTravelDA` strips the button while locked; `RestoreDAOnUnlock` puts it back |
| `SealableGates` | Seals a passage on a `SealTrigger` and reopens it when the `ChallengeCardUID` card is cleared. `ResealCondition: {"Type":"TimerRegrowth"}` seals it again later |
| `ConditionalDrops` | Spawns and removes board cards at runtime by condition: `AlwaysTrue`, `HasPerk`, `ImprovementBuilt`, `SeasonRange`, `DayRange`, `FirstVisit`, `QuestActive` |
| `ExtraDropUIDs` | Extra cards (trees, resource piles) placed on the board on the first visit; they keep their state across saves |
| `CardImage` | Card art for both clones; without it they keep the template's art |
| `VanillaExits` | One-way travel buttons from this node to vanilla locations |
| `StripAllInheritedDrops` | Removes every card the template would place except the location card, so `ExtraDropUIDs` are the only extras. At run start the saved board is rebuilt from the declared drops only if one of them appears too many times or another such node's card turns up. Cards the player left there are never counted and survive a reload |
| `StripLegacyBoardUIDs` | Cards to clear from boards saved before `StripAllInheritedDrops` was set |

Gate conditions use `Type` `Always`, `PerkEquipped`, `ImprovementBuilt`, `StatThreshold` or `Season`, shared by `ConnectionGates`, `SealableGates` (`SealTrigger`, `GateConditions`) and the Animals `Spawn.Conditions`. `PerkEquipped` is true for a perk taken at character creation or granted during the run.

A sealable gate opens only when an action on the challenge card has an `ActionName.LocalizationKey` starting with `ClearActionKeyPrefix`. If the card can be cleared several ways, every one of those keys needs the prefix. Otherwise that way destroys the card and the passage stays locked.

**Reading other mods' map edges.** `Api.WorldMap.InjectedEdges` lists every injected connection in both directions as `MapEdge` (`SourceEnvUid`, `SourceLocationUid`, `Direction` 0 N, 1 S, 2 E, 3 W, `DestinationEnvUid`, `DestinationLocationUid`, `PathCost`, `HiddenOnInGameMap`, `SourceMod`).

`Api.WorldMap.GetInjectedEdgesJson()` returns the same edges in WDI's `MillRaceMapEdges.json` schema. Query either from a `LoadMainGameData` postfix or after `Api.FrameworkEvents` signals that loading finished. `Api.WorldMap` also has `ToggleEdge`, `StripTravelDa`, `RestoreTravelDa`, `RegisterGate` and `EvaluateGates`.

**Troubleshooting.**

- `WorldMapInjector: N node(s) injected` confirms the injector ran; its `skipped` count is nodes that failed to register or link at run start.
- A node, connection, `VanillaExits` target or `ExtraDropUIDs` entry whose UID does not resolve is removed at load with a warning, and counted on the `WorldMapInjector: prepared` line.
- `will be ONE-WAY` names a connection whose target had no map node.
- The injector runs only when some mod ships `WorldMap/MapNodes.json` or `WorldMap/FullMap.json`.

### Portal worlds

A mod ships `MapMod.json` at its root, and its world gets a "Travel to [WorldName]" button on the Portal Hub. No C# is needed.

```json
{ "WorldName": "The Iron Wastes", "EnvironmentUID": "mymodEnvHub" }
```

`EnvironmentUID` is the CT4 environment to travel to. The framework adds the Exit card itself.

`WorldNameKey` (optional) is a key in the mod's Localization CSVs for the world's name, such as `"WorldNameKey": "MyMod_IronWastes_Name"`. The button then reads "Travel to" plus that name in the game language (前往… in Chinese); without it, the button uses `WorldName` in every language.

The travel and exit buttons are wired during the map pass, which runs only when some mod ships a `WorldMap/` map file. A mod with `MapMod.json` alone gets a button that spends time and goes nowhere. Every in-house world mod ships both.

### Instant blueprint unlock

Any `DismantleAction`, `DialogAction` or `Objective.OnCompleteActions` entry can teach a blueprint outright through the vanilla field `BlueprintsFullUnlock`:

```json
"BlueprintsFullUnlockWarpData": ["your_blueprint_uid"]
```

It is always an array. When the action runs, the blueprint's model card spawns on the player's board and the blueprint becomes Available, with no research step and no gate item.

Blueprints in its `AlsoUnlocks` unlock too, and a CT10 improvement is added to the unlocked improvements.

A readable schematic is a consumable item whose "Read" action carries this field and `"ReceivingCardChanges": { "ModType": 3 }`, which destroys the item. Inside an NPC's `DialogAction` the blueprint is taught when the player picks that answer; inside `Objective.OnCompleteActions`, when the objective completes.

### Matching parts across slots (`MatchAcross`, 2.29.0)

A blueprint can require that the cards in several of its slots share one stat value, so sheets and nails are of one metal. Add the key to the blueprint JSON, or to its `.extras.json` sidecar:

```json
"MatchAcross": [{ "Stat": "Special4", "Elements": ["0.0", "0.2"] }]
```

`Stat` is `Spoilage`, `Usage`, `Fuel`, `Progress` or `Special1` to `Special4`. Each element is `"<stage>.<element>"` (zero-based positions in `BlueprintStages` and its `RequiredElements`) or `"<stage>.<uid>"` (the element's required card UID or tab group name). A card whose stat differs from a card already in a listed slot is refused the way the game refuses any card that does not fit. An earlier stage counts only through an element with `"Remember": true`. The full rules are in the CHANGELOG (2.29.0, For mod authors).

### NPC agents

Mod `NPCAgent/*.json` files are loaded and checked at load: null `AgentStats`, `AgentDuties`, `Interactions` and `AgentActions` arrays become empty, and an empty `AgentName.DefaultText` warns.

```json
{
  "UniqueID": "my_npc_agent",
  "AgentName": { "DefaultText": "Forest Elder" },
  "AgentStats": [],
  "AgentDuties": [],
  "Interactions": [],
  "AgentActions": []
}
```

Loading an agent does not spawn it. A live NPC needs a `WorldSettings.NPCAgents` entry or a `GameManager.CreateNPC` call. Either ship an `Animals/*.json` manifest whose `"Ref"` names your agent, which adds the entry for you, or spawn it from C# with `GameManager.CreateNPC` and `AssignOrCreateNPCCards`.

With `VerboseLogging` on, each run start logs `[DIAGNOSTICS]` lines that change nothing:

| Line | Meaning |
|---|---|
| `No NPCAgent-typed fields/props found on GameManager` | Look for the `NPCManager` / `WorldNPCManager` lines that follow |
| `Found N instance(s) of NPCManager` | The game keeps NPCs on a separate component; the following lines show its fields |
| `N/M mod NPCAgent(s) confirmed in <list>` / `Not listed (informational)` | Whether your agent is in a GameManager list. Being listed neither spawns an agent nor is needed for spawning |
| `live InGameNPC confirmed for <agent>` | The agent is spawned |
| `NO live InGameNPC for <agent>` | Nothing spawned it; use an `Animals/*.json` `Ref` or C# |

### Roaming animals (`Animals/*.json`)

One manifest per species adds a wild animal with no C#: day or night presence, wandering, tracks, trap catches, a combat encounter, and taming into a companion.

The framework builds the `NPCAgent` with its duties and timers, plus the tracks, traps, encounter and tame interactions. A manifest can `Ref` a hand-written `NPCAgent` or `Encounter` and let the framework fill the rest.

`Spawn.Conditions` keeps a species out of the world while any condition is false, using the gate condition objects above. `Spawn.ConditionRespawnTicks` sets how long after they all hold again it returns.

Use them for a winter-only visitor, or for an animal players can switch off through a hidden GameStat.

The Approach button reads the framework's `CSFFMFW_Animal_Approach` row, so it follows the game language. `Encounter.ApproachLabelLocalizationKey` gives a species its own wording, and `Encounter.EnemyNameLocalizationKey` names a generated encounter's enemy. The NPC popup's description reads the manifest's `LocalizationKey` row, or an optional top-level `DescriptionLocalizationKey`, which is ignored when `Agent.Ref` is set. A key with no row in the mod's `SimpEn.csv` logs a warning.

An `AffectItems` duty can be limited to the containers it may open with `ContainerCards` (card UIDs), `ContainerTags` and `ContainerRequired`, alongside `InTrapContainers`. With `InTrapContainers` false and neither list set, the raider may take food from every container, and the loader warns.

A manifest in `Animals/` is live content the moment it ships, so never leave a test species there.

Examples: `Sirus23_Mod_Collection/Animals/Owl.json` (its `Ref` names a hand-written agent) and `Hawk.json`; Far Territory and Harsh Wilds ship more. The full field reference is `Documentation/Design/Animals_Schema.md` in the source repository.

### Making mods work together

Two mods can share a card, a recipe slot or a tag without knowing about each other, as long as neither needs the other installed. The steps below are the order the suite follows, each with the shape of a real shipped file. Every shared resource is listed in the suite's Interop Registry, kept in the author's development repo (not the public mod repo). A Chinese version of this page is [README.zh.md](README.zh.md).

**1. Use a vanilla tag first.** When the base game already has a tag for what you share, put it on your card and stop: nothing to agree on, and it works in any load order. The suite does this for lamp oil (`tag_Oil`), burnables (`tag_Fuel`), safe water (`tag_WaterSafe`) and more. The vanilla names are in the extracted game data's `CardTag.txt` (`Documentation/GameData/CSFF-JsonData_Current/ScriptableObjectObjectName/CardTag.txt` in the source repository). Tags go in by name, beside a `*WarpType` of 3. This is the Tin Solder card (`CardData/MetalParts/Item/TinSolder.json`): three vanilla tags and one interop tag.

```json
"CardTagsWarpData": [ "tag_Metal", "tag_Preservable", "tag_TraderInterestX2Metal", "tag_x_Solder" ],
"CardTagsWarpType": 3
```

**2. Use an interop tag `tag_x_<Name>` when no vanilla tag fits.** The `x_` marks a tag shared between mods and keeps it from ever colliding with a vanilla one. Name it in `CardTagsWarpData` as above and do not ship a `ScriptableObject/CardTag/` file for it: the first mod that names a missing tag creates it, and every other mod that names it gets the same tag. The framework's own are `tag_x_MetalSheet`, `tag_x_MetalNail`, `tag_x_Solder` and `tag_x_Brine`. A tag alone does not fill a recipe slot or a liquid check, because those name a card or a card group, not a tag. That is step 3.

**3. Declare a group in `TagGroups.json` instead of shipping CardTabGroup files.** A group built here is refilled at load with every card that carries one of its tags, from any mod, plus the `ExtraCards` you list by UID (an extra card that is not installed is skipped). This is the framework's solder group:

```json
{
  "Name": "GpTag_FW_Solder",
  "Label": "Solder",
  "LabelKey": "CSFFMFW_GpTag_FW_Solder_TabName",
  "Tags": ["tag_x_Solder"],
  "ExtraCards": ["act_tin_solder"]
}
```

A blueprint slot takes the group by name (`CardData/Blueprint/Bp_LargeSaw.json` in Advanced Copper Tools):

```json
"RequiredTabGroupWarpData": "GpTag_FW_MetalSheet",
"RequiredTabGroupWarpType": 3
```

A liquid check names a group in `RequiredGroupWarpData`. Several mods may declare one group `Name`; the declarations merge, so a mod adds a card of its own to someone else's group by declaring the same name with only its extras: `{ "Name": "GpTag_FW_MetalSheet", "ExtraCards": ["act_iron_sheet"] }` is the whole entry in Advanced Copper Tools' `TagGroups.json`. A group widens which cards fit a slot; the slot's own stat conditions still run on each card. Do not ship a `ScriptableObject/CardTabGroup/` file for this. One named like a base-game group, or carrying a base-game group's `TabName.ParentObjectID`, can take that group's place, and the framework warns once per such file (2.29.0). Put blueprints into crafting tabs with `BlueprintTabs.json`.

**4. Metal Type bands.** The framework's Metal Sheet and Metal Nail keep their metal in `SpecialDurability4` (Metal Type), one of six values: 100 Copper, 110 Ghost Bronze, 120 Tin, 130 Tin Bronze, 140 White Bronze, 200 Iron. A slot that wants only some metals gates `Special4`; this one (the Large Saw's sheet slot) takes every metal from 0 to 200:

```json
"Special4": { "Active": true, "FloatValue": 0.0, "MaxValue": 200.0 }
```

A heated part is the same card in the Fuel band 151 to 300 (cold is 0 to 150), so never gate Fuel, Usage or Special1 to Special3 in a slot that takes these parts, or heated and cold parts stop filling it alike. A card from your mod joins the sheet, nail and solder groups by carrying `tag_x_MetalSheet`, `tag_x_MetalNail` or `tag_x_Solder`. Give it `SpecialDurability4` as Metal Type with the values above if slots should tell its metal: a card whose Special4 is inactive passes every Metal Type gate. A solder from your mod must carry `SpecialDurability1` as Uses, because the recipes keep the stick and spend one use, and a card without that stat is never used up.

**5. `UidAliases.json` when you rename or merge a card.** Put `{ "old_uid": "new_uid" }` beside `ModInfo.json`. Any `*WarpData` naming the old UID, in your mod or another, then resolves to the new card, and a save that holds the old UID finds the new one (aliasing a removed environment to a live one also moves a save made inside it; the renamed-card half is not confirmed in a running game yet). Only UIDs that no longer exist are aliased, so an old UID that is still a real card is never shadowed. These are rows from the framework's own file, which maps Chiwei's metal part IDs onto the framework's cards:

```json
{
  "APE_MetalSheet":       "advanced_copper_tools_metal_sheet",
  "APE_MetalSolder_Ball": "4b0f4937a5ecb90499428c8c10288afc"
}
```

If two mods alias one old UID to different targets, the first in load order keeps it and the log warns. If you ship a card under a UID the framework also ships, yours loads and the framework skips its own copy. When you merge two blueprints into one, keep the old file as a retired stub (out of `BlueprintTabs.json`, `CardsOnBoard` emptied, `AlsoUnlocksWarpData` naming the merged blueprint), so a half-built site of the old recipe still finishes and a save that learned it keeps the research.

**6. `_appendArrays` instead of a replace.** A `GameSourceModify` patch replaces a field when it writes the plain key, or `<Field>WarpData` with WarpType 3 or 5; it appends with `_appendArrays` or WarpType 4 or 6. Replacing a list erases what base-game or another mod put there. This is the whole of the framework's patch that makes Stearin Cake a wax for vanilla candles (`GameSourceModify/GpTag_FatOrWax.json`, named for the group it patches):

```json
{
  "_appendArrays": {
    "IncludedCards": ["APE_StearinCake"]
  }
}
```

To add to a card's `CardInteractions`, write `CardInteractionsWarpData` with `"CardInteractionsWarpType": 4` (`GameSourceModify/FishMeatFattyRaw.json`). New in 2.29.0: when two mods replace the same field of the same object, the log carries one warning naming both mods, the object and the field, ending with the hint to append instead. The first 25 are warnings and the rest go to Debug under one counting warning. It runs under the `AuthoringLint` switch (see Authoring lint above).

**7. PNG frames for animations.** A GIF pixel is either clear or solid, so a soft shadow or glow is lost. Put the frames in `Resource/GIF/<GifName>/` as PNG files and name `<GifName>` in `CardData/Gif/*.json` exactly as you would a GIF (this is the Portal's, `CardData/Gif/PortalHub.json`, whose animation is a GIF file):

```json
{
  "CardUniqueId": "csffmfwportalplaced",
  "CardGif": { "GifName": "Portal_Placed", "Loop": true }
}
```

Frames play in the numeric order of the last number in each file name (`flame_2.png` before `flame_10.png`) and every frame keeps full transparency. Timing is 100 ms a frame unless the folder holds a `frames.json`: `{ "DelayMs": 80 }` for all frames or `{ "DelaysMs": [80, 80, 120] }` per frame (a short list repeats its last value). Keep every frame the same size. A GIF of the same name wins and the folder is skipped; GIF names are shared by all mods, so prefix yours. Guide: `Documentation/CSFF_GIF_Authoring.md` in the source repository. The framework ships no frame folder yet, so the `frames.json` shape comes from the loader (`Gif/GifLoader.cs`).

**8. `LiquidConversions.json` for a powder that pours.** One file beside `ModInfo.json` lets a powder go into a jar, pouch or barrel as its liquid form and come back out with the liquid's Empty button, as vanilla salt and flour do. No released mod ships a pair yet; this is the test fixture (`Development_Tools/Tests/Fixtures/Framework_LiquidConversions/FW_LiquidConversions_FixtureA/LiquidConversions.json`):

```json
[
    { "Solid": "fwlcfix_a_powder", "Liquid": "fwlcfix_a_lq_powder", "Quantity": 5 }
]
```

`Quantity` is the liquid units one solid card becomes (vanilla uses 5 for a pinch of salt) and must be above 0. The liquid is a CT9 card; give it `tag_PowderLiquid` so a Pouch takes it. A card can be in one pair only: the first mod in load order keeps it, a later row naming it is skipped with a warning naming both mods, and vanilla's own pairs always win. See Declarative files above.

**9. `MatchAcross` when parts must match.** A blueprint can require that the cards in several slots share one stat value, so the sheets and nails of a recipe are of one metal. This is the Large Saw (`CardData/Blueprint/Bp_LargeSaw.json`): the sheet slot (stage 0, element 0) and the nail slot (stage 0, element 2) must agree on `Special4`.

```json
"MatchAcross": [
  { "Stat": "Special4", "Elements": [ "0.0", "0.2" ] }
]
```

The full rules (element references, `Remember`, the load warnings) are in Matching parts across slots above.

**10. `Api.CardState` for a number the card has no room for.** A card has eight stats and some are already taken; `CardState` keeps named numbers on one card in the save. Declare, once, that a value follows the card through a transform and is averaged over a blueprint's ingredients into its result. This is Advanced Copper Tools' Large Saw, which remembers the smithing quality of its parts:

```csharp
CardState.DeclareCarry("act.large_saw.part_quality", true, CardStateMerge.Average, "advanced_copper_tools_large_saw");
CardState.DeclareBlueprintSeed("act.large_saw.part_quality", "SpecialDurability3",
    "advanced_copper_tools_metal_sheet", "advanced_copper_tools_copper_nails");
float quality = CardState.Get(card, "act.large_saw.part_quality");   // NaN when the card has none
```

Prefix every name with your mod; call `Set` from a running game, not at the main menu. The rest is in C# API below.

**11. Escape sequences in mod CSVs.** A literal `\n` (and `\r\n`) in a value of a mod's `Localization/*.csv` now shows as a line break, in the English and the Chinese column, as it does in vanilla text. A `SimpCn.csv` row is Key, English, Chinese, so `Key,"First line.\nSecond line.",第一行。\n第二行。` is two lines in both languages. A real line break inside a quoted value still works, a lone `\r` is left alone, and there is no way to show a literal backslash-n. A Chinese column left empty shows the English text.

**12. Ask for an Interop Registry row before you share something new.** The suite keeps an Interop Registry (in the author's development repo, not in the public mod repo) that lists every resource shared between mods, one row each (the R rows): the resource, its tier, its owner, its producers and its consumers. The tiers are V (a vanilla tag), X (an interop tag `tag_x_*`) and U (a card UID that is used only while the partner mod is present, for what a tag cannot say: a trade row, an alternate ingredient, a drop or recipe injection). R26 to R29, for example, are the metal part and brine tags above. The row's owner (a mod, not a person) defines what the resource means, such as units and which stat holds what. Producers tag their own cards without asking, and consumers ship the acceptance on their side. The S rows say which mod owns a stat, and the C rows are claims on concepts and UIDs so two mods do not make the same card; a UID belongs to whichever mod ships it, never by prefix. A content mod does not hard-depend on another: a partner mod's UID goes only where a missing card is skipped (a drop slot, a list), never into a required ingredient. To propose a row for your mod, post the resource, the tag or UID, what its owner defines, and who produces and consumes it on the framework's Nexus page; the author adds the row and the framework docs name it.

### C# API

Public helpers live under `CSFFModFramework.Api` and `CSFFModFramework.Util.CardUtil`. They replace reflection scaffolding and Harmony patches that every mod used to write for itself, and that could not be combined across mods.

**Utilities**

| API | What it does |
|---|---|
| `Api.Reflect` | `GetMember(obj, names...)` / `SetMember` falling back from property to field to backing field, cached per type and name; `TryGetType(names...)`; `DeepClone` (cycle-safe, Unity references kept); `AllFlags` |
| `Api.Collections` | `Append` / `AppendAll` / `Merge` on an array or `List<T>` member (creates it if null, skips duplicates); `CreateLike`; `SetCollection` |
| `Api.Inventory` | `Cards(container)` across all slots; `Find` / `Count` by UID or `tag_*`; `Consume`; `Eject`, which removes a card without the game moving it to a neighbouring container |
| `Api.Gate` | `OncePerFrame`, `OncePerDtpTick` and `OncePerDayRollover` dedup gates |
| `Api.LocalizedStringBuilder` | `Create` / `CreateLike` / `Populate` for LocalizedStrings built in code |
| `Api.VanillaIds` | Name-to-GUID lookup from the game data (`Get`, `GetStat`, `Group`) with curated groups `LargeTrees`, `Fires`, `OpenStorage`, `WaterSources`, `AnimalFoods`, `BearEncounters` and `BearAgents` |
| `Api.ContentRegistry.RegisterWithResult` | Returns `Registered`, `DuplicateSkipped` or `Failed`. `Register` returns the same as a bool |
| `CardUtil.GetDurability` / `SetDurability` / `GetDurabilityMax` | Read and write a card's stats by JSON or runtime name, across both runtime shapes |
| `CardUtil.TransformInPlacePreservingStats` | Swaps a card's model in place and carries over the stats you choose |
| `CardUtil.GrantInRunPerk(perkUid)` / `CardUtil.IsPerkHeld(perkUid)` | Grants a perk during a run as `AddedInRunPerksWarpData` does: saved, re-applied on load, and applied now. Returns true when held afterwards; a failed apply is rolled back with a warning. `IsPerkHeld` checks both character-creation and in-run perks, while `IsPerkEquipped` sees only the first |
| `CardUtil.TryRemoveCard` / `RemoveCardCleanly` | Removes a board card through the game's own removal, which also clears it from the game's card lists and drops no loot; or removes a card from an inventory by swapping it for a placeholder |
| `CardUtil.FindCardsWhere(predicate)` | Every loaded `CardData` the predicate accepts, vanilla and mod, complete from the end of the framework's load. Cards another plugin adds to `AllData` itself are included from run start. `CardUtil.FindCardsByUniqueIds` looks up by UID |

**Runtime services**

| API | What it does |
|---|---|
| `Api.ActionRouter` | One shared patch over the game's action routines. Register an `ActionHandler { CardUid or CardPredicate, ActionKeyPrefix or ActionNamePrefix, Timing = Cancel / Before / AfterWrapped, Before, After }`. A card-on-card action dispatches once, and its `Cancel` and `Before` handlers run before the dragged card is used up. An `AfterWrapped` context carries `Produced` / `ProducedUids`: cards spawned while the action ran, which can include cards other cards made during its ticks. It patches only on the first `Register` |
| `Api.SpawnService` | `Spawn(uidOrCardData)` puts a card on the current board through `GameManager.GiveCard`; it returns null even on success. `Spawn(uid, statOverrides)` applies the overrides through the game's drop path (a liquid still spawns at its defaults). `OnNextSpawn` and a card's JSON `SpawnStatDefaults` are accepted but do nothing: use a drop's `TransferRules` or a perk's `AddedCardsStats` instead. `CardSpawned(card, uid)` fires for every card the game spawns |
| `Api.TickEvents` | `DtpTick` and `DayRollover` events and `Interval(seconds, callback)` timers, each subscriber isolated from the others' exceptions |
| `Api.EncounterGuards` | `Register(name, ctx => suppress)` stops encounter starts with no NPC. `Register(name, fn, includeAgentStarts: true)` also sees an agent's own attack, with `ctx.Source`, `ctx.AgentUid` and `ctx.EncounterUid` set. The same scope rules as the JSON guards apply |
| `Api.ContentModPlugin` | Optional plugin base class: Harmony setup, `RegisterPatches` with per-patch `TryApply`, the standard one-line load log, and `OnModAwake` / `OnModDestroy`. Subclassing makes the framework a hard requirement; keep the soft `[BepInDependency]` for load order |

**State access**

| API | What it does |
|---|---|
| `Api.CardState` (2.29.0) | Named numbers on ONE card in the game, kept in the save: `Set(card, "mymod.origin", 2)`, `Get(card, name)` (`NaN` when absent, or `Get(card, name, fallback)`), `Has`, `Remove`, `Names`. For a value the card's eight stats have no room for. Saved per checkpoint in `Games/Game_N/CSFFModFramework/<save>.cardstate`; a card that leaves the game takes its values with it at the next save; two slots never share values. `DeclareCarry(name, throughTransform, CardStateMerge.Average, onlyIntoResultUids...)` keeps a value through a transform (`CardUtil.TransformCardInPlace` and the game's own) and passes it from a blueprint's ingredients to its result (`First`, `Min`, `Max`, `Sum`, `Average`); undeclared names stay where they were set and a transform drops them. `DeclareBlueprintSeed(name, "SpecialDurability3", ingredientUids...)` lets a part with no value give one of its stats instead. Both declarations are idempotent. Not carried: a transform the game runs away from the player (it makes save data, not a card), a poured liquid, and ingredients a blueprint gives back. First user: ACT's Large Saw keeps its sheets' and nails' smithing quality |
| `Api.CardFinder` | Cached lookup of in-game cards (`AllCards()`, `Find` / `FindAll` by UID or predicate). Refreshes when the card count changes; call `Invalidate()` after a model swap |
| `Api.StatAccess` | By UID: `Resolve(uid)` returns this run's live `InGameStat`, `Get(uid)` its value (`NaN` on failure), and `Set(uid, value)` / `Modify(uid, delta)` write through the game's own stat change, so clamps, statuses and triggers behave as vanilla. `ResolveNpc(npc, uid)` returns an NPC's stat. Works for vanilla GUIDs and mod UIDs; do not keep a resolved stat across a reload. On a live `InGameStat` you hold: `GetCurrentValue`, `SetCurrentValue`, `ModifyCurrentValue`, `GetMaxValue`, `GetStatModel`, `GetUniqueId`. Pass the live stat, never the `GameStat` definition, which has no current value and logs a warning |
| `Api.RecipeInjector` | Adds a `CookingRecipe` to a station's `CookingRecipes` from a `RecipeSpec` (compatible cards or tags, duration, cooker and ingredient changes) |
| `Api.ContainerSort` | Reorders a container's slots in place by a durability (Usage, Spoilage, Special1 to 4, or Quality where the card has one), ascending or descending. No mod calls it yet |
| `Api.BlueprintAlternates` | `AddAlternateIngredient(allData, primaryUid, alternateUid)` lets every blueprint slot that takes the primary accept the alternate too, which leaks into vanilla recipes when the primary is a vanilla material. `AddAlternateIngredientIn(allData, primaryUid, alternateUid, blueprintUids, label)` limits it to the listed blueprints. Both return the number of slots patched; an empty list patches nothing and warns |
| `GameQuery.EquippedCards` | A fresh list of the card in each filled equipment slot, in slot order, that you may keep and change. Empty with a warning when equipment cannot be read (such as at the main menu) |
| `CardUtil.GetLiquidQuantity` / `CardUtil.ModifyLiquid` | How much liquid a card or container holds, and adding or removing it through the game's own change, which clamps to capacity and empties the container properly. `ModifyLiquid` never creates or swaps a liquid |
| `Api.Npc` | `MoveTo(object npc, string envUid)` moves a live NPC to the CT4 environment you name through `GameManager.MoveNPC`, true when it arrives. It builds the destination every call; never cache an `EnvID` yourself, because a mod environment's key reads 0 at the main menu and the move then silently goes nowhere. Instanced environments are out of scope. No mod calls it yet |

### Architecture

`Loading/LoadOrchestrator.cs` runs the load passes in order, each timed and isolated: mod discovery, map caches, sprites and GIFs, JSON loading, removal of ModLoader duplicates, WarpData resolution, normalization, the injectors, world map preparation and `GameSourceModify`.

Localization, audio, perks and blueprint tabs come last. Read that file for the exact order.

| Folder | Holds |
|---|---|
| `Discovery/` | Mod probing and the ModLoader decision (`ModManifest.HasFrameworkOnlyMarkers`) |
| `Loading/` | JSON, sprites, localization, map nodes, encounter guards and the load orchestrator |
| `Data/` | The database, `WarpResolver` and `GameSourceModifier` |
| `Injection/` | Blueprint, perk, smelting, drop, improvement, trading value and world map injectors, and the gate services |
| `Animals/` | The roaming animal system |
| `Portal/` | `MapMod.json` loading and Portal Hub travel |
| `Triggers/` | Spawn triggers |
| `Patching/` | Harmony patches: `BugFixes/`, `Performance/`, `Diagnostics/` |
| `Wildlife/` | The optional raid mechanic |
| `Stubs/LitJson/` | Source of the bundled `LitJSON.dll` (v0.19.0.0) |

## Recent releases

**2.28.9.** Dragging a card no longer freezes the board when one card's data is broken, and a card animation can be a folder of PNG frames, so soft shadows and glows keep their transparency.

**2.28.8.** A starting-location perk from a mod starts you where it says: Far Territory's Island Start puts you on Landfall Strand again. In Chinese, the popup of a mod animal or person shows its translated name as its description instead of the English one.

**2.28.7.** No more freeze every two in-game hours on a long run: animal tracks that have already run out are dropped before each save, which shrank one old save from 29.7 MB to 9.3 MB. The animated Portal sits where its still picture does.

**2.28.6.** Washing yourself at a river no longer drains the fuel from every card on the board (seen with Herbs and Fungi installed): the framework's load-time cleanup leaves the base game's own data as it ships.

**2.28.5.** When a mod removes a location, a save made there moves you and that board's cards to the place the mod names, so no board is lost.

**2.28.4.** Card pictures are back in the game's own framing instead of stretched over the whole card.

**2.28.3.** Card motion (NPCs and animals breathe, plants sway), animations tied to a stat follow it while the card is on the board, and the Portal Hub and its Exit are animated.

**2.28.2.** Card swaps and stat-gated actions work on EA 0.69c, and a tame attempt's plain fail counts toward the odds.

**2.28.1.** Copy-over updates retire files a new version no longer ships, `UidAliases.json` keeps renamed cards working, and 19 framework card pictures are cropped small like vanilla art.

**2.28.0.** Shared metal parts moved here from ACT, optional picture compression and NPC throttling, an authoring lint, and fixes for mod rooms sharing one saved board and for guards that never stopped a predator's attack.

Every release, with the full list of changes, is in [CHANGELOG.md](CHANGELOG.md).

## Credits

Portal Kit and placed Portal art by Chiwei, used with permission (see `ASSET_CREDITS.md`).

## License

Released under the [MIT License](LICENSE). Copyright (c) 2026 Jared Scott.

Bundled libraries keep their own licenses:

- **LitJSON:** public domain, reimplemented in `Stubs/LitJson/LitJsonStub.cs`
- **UnityGifDecoder:** MIT License, copyright (c) 2020 3DI70R (see `THIRD_PARTY_LICENSES.md`)
