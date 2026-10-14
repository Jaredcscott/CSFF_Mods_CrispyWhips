# CSFF Mod Framework

The shared base for Card Survival: Fantasy Forest mods. It loads mod content from JSON and wires it into the game, so a content mod needs C# only for logic of its own.

It also fixes several base-game bugs and speeds up travel to long-unvisited places.

- **Version:** 2.28.3
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

Seven blueprints make them, in Metal & Clay > Metal Crafts: Metal Sheet, Bronze Sheet, Iron Sheet, Forged Copper Nail, Cast Copper Nails, Forge Iron Nails and Forge Tin Solder.

These parts moved here from Advanced Copper Tools in 2.28.0 under the same IDs, so saves keep them and other mods can use them without ACT. With an older ACT that still ships them, the framework uses ACT's copies instead.

**Perk origin tags.** Every perk a mod adds shows a short tag after its name, such as `Swimmer [CMC]`, so perks from different mods can be told apart. Vanilla perks get no tag.

The tag shows in the character creation lists, the selected-perk panel, the character select card and the in-game character sheet, including their tooltips. It is display only: perk names in saves and stat reports are unchanged.

It is confirmed on the character sheet and in the WikiMod tooltip; the character creation lists are not yet confirmed in a running game. Switch it off with `ShowModOriginTag`.

**Card motion.** Mods can ask for a little movement on their art: NPCs and animals breathe softly and plants sway. The framework bends the card's own picture, so there are no extra image files and no extra texture memory, and with 24 moving cards on the board it measured about a tenth of a millisecond per frame.

It has been seen working on board cards and in the inspection popup. The encounter picture is not yet confirmed in a running game. Switch it off with `CardMotion`.

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

Settings live in `BepInEx/config/crispywhips.CSFFModFramework.cfg`, created on first run. BepInEx reads it once at startup, so quit to desktop and relaunch after a change. ConfigurationManager gives an in-game editor.

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
| Diagnostics | `AuthoringLint` | `true` | Logs one warning per mod that ships JSON which loads but does nothing. Silent when nothing is found; changes nothing |
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

### Content folders

Each top-level folder name is the type it holds, matching the vanilla JSON export layout: `CardData`, `CharacterPerk`, `PerkGroup`, `GameStat`, `SpiceTag`, `FlavourTag`, `NPCStat`, `NPCDuty`, `NPCHidingGroup`, `NPCAgent`, `NPCCharacterPerk`, `Encounter`, `SelfTriggeredAction`, `Objective`, `QuestLog`, `GameModifierPackage`, `PlayerCharacter`, `CookingRecipeGroup`, `ConstructionCardGroup`, `BookmarkGroup` and `LocalTickCounter`.

Any other ScriptableObject type loads from `ScriptableObject/<TypeName>/`. Objects with a UniqueID go into the game's UID table and the rest are registered by name. Their `*WarpData` references resolve, including those inside non-UID objects such as dialog. Check support at runtime with `Api.Framework.SupportsContentType(name)`.

Most of these types need nothing more, because the game itself picks them up at run start: `CookingRecipeGroup`, `BookmarkGroup`, `ConstructionCardGroup`, `NPCCharacterPerk`, `SelfTriggeredAction`, a `FlavourTag`, and a `GameModifierPackage` referenced from a character's `EasyPackageWarpData`. The exceptions have their own files below.

`Resource/` holds pictures (`Resource/Picture/*.png`), audio, GIFs (`Resource/GIF/*.gif` with `CardData/Gif/*.json`) and asset bundles. `CardData/Motion/*.json` maps sprite names to a card motion profile: `"Sprites": { "<sprite>": "npc" }` with `npc`, `animal`, `plant`, `location` or `still`, or inline numbers (`Breathe`, `Sway`, `Drift`, `Period`), and an optional `"Profiles"` object names your own. No image files are needed. `Localization/` holds the CSV text (`SimpEn.csv`, `SimpCn.csv`).

### WarpData

A `*WarpData` key resolves a UniqueID, GUID, tag or sprite name into the field it names, in arrays and `List<T>` fields alike, and creates tags that do not exist yet. It also reaches a private base-class field, such as an action's `ActionSoundsWarpData`.

Every `*WarpData` key needs its `*WarpType` sibling: 3 Reference, 4 Add, 5 Modify or 6 AddReference. Without one the key resolves nothing, and a boot warning lists it with the mod it came from.

### Declarative files

| File | What it does |
|---|---|
| `BlueprintTabs.json` | `{ "<tab LocalizationKey>": ["<blueprint UID>", ...] }`. Puts construction blueprints in a crafting tab, matched by exact key. An unknown key sends the blueprint to Survival > Support once the blueprint screen is open. A `"NewSubTabs": [{ "Key", "Parent", "DefaultText", "Icon", "After" }]` entry in the same file adds a subtab under the main tab keyed `Parent`; list blueprints under its `Key` |
| `SmeltingRecipes.json` | Smelting recipes in the vanilla Furnace and every station tagged `tag_SmeltingContainer` (the vanilla Bloomery, WDI's forge and workshop). Not the vanilla Forge |
| `DropInjections.json` | Adds drops to location card actions such as Forage, optionally by season |
| `InjectImprovementInto.json` | `[{ "TargetEnvUID": "<CT8 UID>", "ImprovementUID": "<CT10 UID>" }]`. Makes an environment improvement buildable at a location |
| `TradingValues.json` | `{ "<CardData UID>": <number> }` written onto `CardData.TradingValue`. Keys starting with `_` are comments. Later mods win a conflict (with a warning); a targeted `GameSourceModify` patch still wins over both |
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
| `UidAliases.json` | `{ "old_uid": "new_uid" }` beside `ModInfo.json`, for a card you renamed or merged. Any mod's `*WarpData` naming the old UID resolves to the new card, and the old UID is registered as a second key so a save that names it finds the new card. An old UID that is still a real card is never shadowed. The save half is not yet confirmed in a running game |

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
- `AddedCardsStats` and `EquippedCardsStats` give a perk's starting items real values instead of the card defaults (quality 0, Plank ID 0, leather Skin Type 0). Liquids are not covered.
- Write them as `[ { "Card": "<uid>", "SpecialDurability4": 1 } ]` beside the matching `...WarpData` list, with `Card` exactly as it appears there.
- On `EquippedCards`, every card from the first declared one on is equipped in the perk's own order, so a container listed before its contents exists before they are handed out.
- The origin tag comes from the optional `"ShortName"` in your `ModInfo.json` (for example `"CMC"`): at most 8 characters, with whitespace and `[ ] < >` removed.
- Without it, the tag is the initials of the mod's `Name` plus any digits, skipping small words, up to 5 characters (`Sirus23 Mod Collection` gives `S23MC`). A one-word name gives its first 3 letters.

### Mod discovery and Pikachu ModLoader

The framework looks for `ModInfo.json` up to two folder levels deep under `BepInEx/plugins/`. If two folders hold a mod with the same name, it loads the one with more content files.

When Pikachu ModLoader or ModCore is installed, a mod whose `ModInfo.json` has the `ModLoaderVerison` field is left to that loader. Leave the field out of a framework mod, along with `ModEditorVersion`.

A mod with that field is still loaded by the framework when it ships a framework-only file: `BlueprintTabs.json`, `SmeltingRecipes.json`, `DropInjections.json`, `InjectImprovementInto.json`, `TradingValues.json`, `WorldMap/MapNodes.json`, `WorldMap/FullMap.json`, `EncounterGuards/*.json`, `Quests.json`, `Characters.json`, `MapMod.json`, `Modifiers.json`, `FlavourMatrix/*.json`, `Animals/*.json`, or a `GameSourceModify/` patch using `MatchTagWarpData` or `MatchTypeWarpData`.

### Unresolved references

A row in a declarative file that names a UID nothing loaded is skipped and logged, and nothing is created for it. Each file's Info summary counts the skips, so a player's log shows them with `VerboseLogging` off.

The log level follows the likely cause. A UID from a partner mod that is not installed logs at Debug (drop injections, trading values, a `BlueprintAlternates` alternate), and an authoring error at Warn or Error.

`GameSourceModify` and `InjectImprovementInto.json` choose per UID: Warn when a GUID, vanilla name or installed mod should have supplied it, Debug otherwise.

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

The travel and exit buttons are wired during the map pass, which runs only when some mod ships a `WorldMap/` map file. A mod with `MapMod.json` alone gets a button that spends time and goes nowhere. Every in-house world mod ships both.

### Instant blueprint unlock

Any `DismantleAction`, `DialogAction` or `Objective.OnCompleteActions` entry can teach a blueprint outright through the vanilla field `BlueprintsFullUnlock`:

```json
"BlueprintsFullUnlockWarpData": ["your_blueprint_uid"]
```

It is always an array. When the action runs, the blueprint's model card spawns on the player's board and the blueprint becomes Available, with no research step and no gate item.

Blueprints in its `AlsoUnlocks` unlock too, and a CT10 improvement is added to the unlocked improvements.

A readable schematic is a consumable item whose "Read" action carries this field and `"ReceivingCardChanges": { "ModType": 3 }`, which destroys the item. Inside an NPC's `DialogAction` the blueprint is taught when the player picks that answer; inside `Objective.OnCompleteActions`, when the objective completes.

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

The Approach button reads the framework's `CSFFMFW_Animal_Approach` row, so it follows the game language. `Encounter.ApproachLabelLocalizationKey` gives a species its own wording, and `Encounter.EnemyNameLocalizationKey` names a generated encounter's enemy. A key with no row in the mod's `SimpEn.csv` logs a warning.

An `AffectItems` duty can be limited to the containers it may open with `ContainerCards` (card UIDs), `ContainerTags` and `ContainerRequired`, alongside `InTrapContainers`. With `InTrapContainers` false and neither list set, the raider may take food from every container, and the loader warns.

A manifest in `Animals/` is live content the moment it ships, so never leave a test species there.

Examples: `Sirus23_Mod_Collection/Animals/Owl.json` (its `Ref` names a hand-written agent) and `Hawk.json`; Far Territory and Harsh Wilds ship more. The full field reference is `Documentation/Design/Animals_Schema.md` in the source repository.

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
