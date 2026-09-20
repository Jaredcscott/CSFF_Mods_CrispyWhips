# Sirus23 Mod Collection

**Version:** 1.21.5
**Author:** Sirus, Jared
**For:** Card Survival: Fantasy Forest (EA 0.65)
**Requires:** CSFFModFramework 2.5.0+

## Overview

Sirus23 Mod Collection bundles three animal companion systems and a full sheep husbandry chain for Card Survival: Fantasy Forest. Content is loaded through CSFFModFramework, with blueprints registered through `BlueprintTabs.json`, perks loaded from `CharacterPerk/`, and runtime behavior handled by focused Harmony patches.

**Dependencies:** `CSFFModFramework` only (declared `SoftDependency`, load-order only). This mod does not depend on, and is not depended on by, any other in-house content mod.

## Animal Companions

Three companions are available, each unlocked by a matching character-creation perk. All three share the same upkeep model: Hunger (SpoilageTime), Thirst (UsageDurability), and Morale (Progress) drain passively over time. Feed and water them via drag interactions, or let them self-sustain — if a companion's hunger or thirst drops below 20% and a matching food or water source is on the board, it automatically eats or drinks from the nearest one. Pet or play with them as self-actions to restore morale. Neglect causes them to die or flee.

### Wolf Companion

The **Wolf Friend** perk starts the player with a loyal wolf companion. The wolf can be fed, watered, treated, petted, played with, sent hunting, used to track scents, and set to guard camp. Its hunger, thirst, morale, and health are tracked on the card over time. Upkeep ticks run on CSFFModFramework's `TickEvents`, and the food/water item sets come from the framework's per-game-version `VanillaIds` registry.

Wildlife and ambient encounters are suppressed while the wolf is present in the player's current environment via the declarative `EncounterGuards/WolfGuard.json` (evaluated by the framework's encounter guard service). NPC encounters are not suppressed.

**Pack Bond** — a hidden perk granted the first time the wolf's one-time **Bond with Wolf** action (8 DTP) is performed. Grants +15 to the Tracking skill, representing heightened instincts from the bond.

### Fox Companion

The **Fox Friend** perk starts the player with a cunning fox companion. The fox can be fed (meats, fish, small game), given dried-berry treats, watered, petted, played with, sent **Scouting Ahead** (a 3-DTP action that sharpens the player's Tracking skill and returns 1–2 raw meat), and used to **Retrieve Partridge** (a timed hunt returning partridge). The fox has a higher morale drain than the wolf. If neglected it dies; its body remains.

Like the owl, the fox partially suppresses wildlife and ambient encounters while present (`EncounterGuards/FoxGuard.json`, 35% suppression chance - weaker than the wolf's full suppression).

The regular wild fox you already encounter in the world can also be tamed without taking the perk — no separate creature to find. Offering it berries, fresh or dried (Springberries, Bilberries, or Juniper Berries), directly tames it into the same Fox Companion the perk grants. (Before 1.21.1 only the dried forms were accepted, and a refused berry gave no feedback at all; this widening is not yet confirmed in-game.) **The equivalent Wild Wolf tame path is a planned follow-up and is not yet implemented — the Wolf remains perk-only for now.**

### Owl Companion

The **Owl Friend** perk starts the player with a silent owl companion. The owl accepts meat and dried-meat treats; it can be petted, assigned **Night Watch** (a 4-DTP rest action that restores sleep and reduces stress), and used to **Retrieve Mouse** for a small foraging return. The owl has a slower hunger and thirst rate than the wolf or fox. Like the wolf, it partially suppresses wildlife and ambient encounters while present (`EncounterGuards/OwlGuard.json`, 35% suppression chance — weaker than the wolf's full suppression). Unlike the wolf and fox, the owl won't follow you into caves or structures — it waits outside and rejoins you when you return to the outdoors. If neglected, it silently flies away — no body remains.

A **Wild Owl** can also be found at night (20:00–6:00), drawn toward the light of your camp. It leaves discoverable tracks (feathers and pellets) in whatever spot it last visited — find them the next morning, with a chance to spot them scaling with your **Tracking** skill. Approaching the owl starts an encounter (it may flee, or fight back); offering it a mouse, minnow, or dead partridge attempts to tame it — success chance scales with your **Tracking** skill (roughly 50% untrained, 75% at 150 Tracking), and a failed attempt has a small chance to provoke the owl into attacking instead of just fleeing. A successful tame grants the same Owl Companion the perk does, and the wild owl disappears for good. The wild owl can also be caught alive in a baited snare. From 1.20.7 it can also come after you unprompted: between 22:00 and 04:00, if it is sharing your environment, it may start that same encounter itself, at most once per night. (Before 1.20.7 this was authored but could never fire, so an owl only ever fought you if you approached it or provoked it with a failed tame.) It is suppressed while a tamed Owl Companion is on the board and respawns some time after the companion is lost — killing or catching it in combat instead starts a longer respawn timer.

## Sheep Husbandry

Sheep Husbandry adds sheep, rams, lambs, milking, shearing, wool processing, dairy processing, and supporting structures. The chain includes sheep milk, curdled milk, cream, butter, buttermilk, yogurt, ricotta, sheep cheese, aged sheep cheese, wool, lanolin, yarn, woven cloth, wool clothing and bedding, shears, cheese cloth, butter churn, sheep feeder, sheep pen.

Shearing has a chance to yield **Lanolin**, a waxy byproduct. Blend it with frostleaf powder into a **Lanolin Salve** (blueprint under Survival › Medical, discovered by having Lanolin on the board) — apply it to your skin for a lasting Comfort boost.

Wild sheep and rams are defined with `CardData/Trigger/` JSON. Runtime trigger support is provided by CSFFModFramework's trigger service.

### Tend Flock

The Sheep Feeder's **"Tend Flock"** action shears every ready tame or lactating sheep in the current environment in one click, instead of shearing each sheep by hand — the same Wool threshold, 2x Wool output, and 25% chance of Lanolin as the individual Shear action, just applied to the whole nearby flock at once. **Milking is not automated** — dragging a container onto a Lactating Sheep by hand is still how milk is collected; automating it would require the game's liquid-container attachment mechanism, which has no existing precedent in this mod and was intentionally left out of this pass.

### Prepared Dairy Dishes

Butter, Sheep Cheese, and Cream each have a second use beyond eating raw: drag Butter onto Roasted Cattail Root for **Buttered Roots**, drag Sheep Cheese onto Wheat Roundbread for **Cheese-Stuffed Flatbread**, or drag Cream onto Mashed Turnroot for **Creamy Mash**. Each combination consumes the dairy item and transforms the vanilla dish into the new prepared meal, which offers stronger nutrition than either ingredient alone. **Nutrition values and the recipe pairings are initial balance guesses, not final-tuned numbers.**

### Dairy Preservation

Fresh dairy spoils fast. Two preservation steps extend it: drag **Salt** onto Butter to work it
through into **Salted Butter**, which keeps three times as long as the fresh kind; and drag any fire
source onto Sheep Cheese to **Smoke the Cheese**, producing **Smoked Cheese**, which keeps four times
as long as fresh cheese and is the most valuable thing the dairy chain makes. Both consume the fresh
product. **The shelf-life multipliers are initial balance guesses, not final-tuned numbers.**

### Cultured Dairy Dishes

Yogurt, Sour Cream, Ricotta and Buttermilk each gained a prepared dish on the same drag-onto-a-vanilla-
food pattern as the Butter/Cheese/Cream dishes above: Yogurt onto Dried Springberries gives **Berry
Yogurt**, Sour Cream onto Boiled Turnroot gives **Soured Turnroot**, Ricotta onto a Rye Roundbread
gives a **Ricotta Rye Round**, and Buttermilk onto Wheat Hardtack gives **Buttermilk Hardtack**. With
these, all eleven dairy products in the chain have a use beyond the eat button. **Nutrition values and
pairings are initial balance guesses, not final-tuned numbers.**

### Straining Curds by Hand

The cheese cloth is used to curdle sheep milk, and it has a second use: drag it onto **Curdled Milk**
to **Strain Curds** (3 DTP), pressing the curds by hand with no fire. It is slower than the fire-based
"Press Cheese", and the whey drains away rather than being collected, so it is the fallback for when
you have no fire rather than a straight upgrade.

### Salvaging Sheep Remains

A sheep taken by a predator leaves a **Sheep Remains** card. Drag any cutting tool onto it for
**Salvage Remains** (3 DTP), recovering 2x Wool, a Fresh Hide, 2x Raw Meat and Bones before the
remains rot. **Yields are an initial balance guess, not final-tuned numbers.**

### Sheep Pen (pen-or-perish)

The **Sheep Pen** (`sh_sheep_pen`, blueprint `sh_bp_sheep_pen` under Farming › Animal Husbandry, 8 Plank + 4 Rope + 4 Stone) is a buildable enclosure that holds up to 4 tame sheep and/or rams as storage — drag them in, and their wool regrowth, milk, and ram-proximity breeding continue normally while penned (confirmed via decompile: a card's own `AlwaysUpdate` flag, which tame sheep already carry, keeps applying regardless of container nesting).

**Pen or perish:** every in-game night, any tame sheep, ram, or lactating sheep left OUTSIDE a pen (in the environment the player is currently standing in) risks being lost — either wandering off, or taken by a predator overnight (which leaves a **Sheep Remains** card behind so the loss is never a silent mystery). A **Wolf Companion** present in that environment stands guard and cuts the predation chance to a sixth (6% to 1% per unpenned sheep per night); the wandering-off chance (4%) is unchanged, because a guard deters predators but does not herd. Only a pen makes a flock fully safe. (Before 1.21.2 the wolf suppressed the roll entirely, which made the pen pointless for anyone with the wolf; changed after playthrough r33, tracker T2.101. Not yet confirmed in-game: T2.101 and its follow-up row are still open.) Penned sheep are always exempt. **The escape and predation percentages are conservative starting placeholders, not final-tuned balance values** — they may be adjusted after playtest feedback. Picking the pen back up (its "Pick Up" action) safely releases any sheep inside onto the ground first rather than destroying them.

The escape/predation check only evaluates whichever environment the player currently occupies when a night passes — a flock left at a completely different, unvisited environment is not at risk until the player is present there again.

## Felt Working

The felt pathway provides an alternative to the yarn and weaving chain. Press 4 raw Wool into a sheet of Felted Wool, then use felted wool to craft a Felt Hat (head, +8 Cold Resistance), Felt Mittens (hands, +5 Cold Resistance), Felt Vest (outer torso, +12 Cold Resistance — shares the outer-torso slot with the Wool Tunic), Felt Boots (outer shoes, +8 Cold Resistance, also requires Twine), or knit 2 Yarn directly into Wool Socks (inner shoes, +5 Cold Resistance). Felt 3 Felted Wool into a **Felt Bedroll** — a bedding item, like the Wool Blanket, but a distinctly stronger one: it grants a bigger sleep-insulation bonus than the blanket (+16 vs. +10), at the cost of more carry weight (900g vs. 700g). All seven blueprints unlock from the Cloth subtab of the Tailoring tab.

Felt Mittens, Felt Vest, and Felt Bedroll's stat values are an initial balance guess, not final-tuned numbers.

## Feather Pillow

Processing an owl carcass yields six Feathers, which nothing else in the mod consumed. The **Feather
Pillow** (blueprint under Tailoring > Cloth, discovered by having Feathers on the board: 1 Woven
Cloth + 12 Feathers + 4 Twine) absorbs them into the mod's existing bedding line, granting **+10
Comfort** while carried. **The Comfort value is an initial balance guess, not a final-tuned number.**

## Companion Treats

Each companion has a craftable treat that reads as a deliberate bonding act rather than routine
feeding: **Bone Treat** (1 Bones + 1 Dried Meat) for the wolf, **Owl Treat** (1 Raw Meat + 1 Salt)
for the owl, and **Berry Mix** (1 Dried Springberries + 1 Dried Bilberries) for the fox. All three
blueprints sit under Survival > Cooking. Giving one restores twice the bond a raw treat does, plus a
small Morale gain and some relief from Loneliness for the player. The companions' existing raw-food
Feed and Give Treat actions are unchanged. **The bond and stat values are initial balance guesses,
not final-tuned numbers.**

## Crafting Tabs

Blueprints are mapped through `BlueprintTabs.json` and injected by CSFFModFramework. The collection does not ship `ScriptableObject/CardTabGroup/` overrides.

## Development Notes

This project targets .NET Framework 4.8. It uses shared assembly references from `../HerbsAndFungi/lib/` for local builds, including `0Harmony.dll`, `BepInEx.dll`, Unity assemblies, and `Assembly-CSharp-nstrip.dll`. A local `lib/` junction may also point at that shared folder for audit tools that expect a per-mod lib directory.

Use `Development_Tools/Deploy-Mods.ps1 -CSFFModFramework -Sirus23ModCollection` to build and deploy the framework plus this mod. The deploy script preserves the trigger JSON subfolder required by CSFFModFramework.
