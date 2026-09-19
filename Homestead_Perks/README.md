# Homestead Perks

**Version:** 1.2.4
**Author:** Jared
**Requires:** CSFFModFramework (soft dependency)
**Language:** English, Simplified Chinese

A standalone mini-mod for Card Survival: Fantasy Forest. Thirteen character-creation perks in the
Situational tab: twelve grant a one-time-placeable structure kit (nine are a working recreation of
the abandoned "Better Perk Buildings" mod, extracted from Community Mod Chest so it can run on its
own; three — Well, Cellar, Pit Trap — are new kits for other vanilla structures), and one — Path
Kit — grants reusable instant road-building charges.

## Contents

- **Homestead** (100 Suns) — start with a Homestead Kit. Drop it wherever you choose and Place it
  to unpack a Cabin Kit, two Rain Cistern Kits, and a stockpile of building materials (30 Planks,
  30 Mud Bricks, 50 Stones, 20 Heavy Stones, 15 Tree Logs, 20 Clay, 10 Rope, 20 Metal Nuggets) all
  at once — then Place each kit to raise it.
- **Cabin Kit** (60 Suns) — also grants a broom blueprint.
- **Mud Hut Kit** (40 Suns) — also grants a broom blueprint.
- **Cellar Kit** (40 Suns) — an underground room; food stored there lasts longer.
- **Well Kit** (35 Suns) — a permanent water source.
- **Forge Kit** (25 Suns)
- **Furnace Kit** (20 Suns)
- **Pit Trap Kit** (20 Suns) — a big-game trap; still needs bait after placing.
- **Path Kit** (20 Suns) — start with 6 Path Kits. Each one has 4 directional "Build Path" actions
  (North, South, East, West); using one instantly finishes the vanilla road improvement in that
  direction at your current location — no digging, no waiting. Works outdoors, and only has an
  effect in a direction that actually leads somewhere.
- **Log Bed Kit** (15 Suns)
- **Oven Kit** (15 Suns)
- **Tanning Pit Kit** (15 Suns)
- **Rain Cistern Kit** (10 Suns)

Every structure-kit item can be carried anywhere and raised with a single "Place" action — each one
only once. Eleven of the twelve structure-kit perks transform their kit directly into a vanilla
structure counterpart (Cabin, Mud Hut, Well, Cellar, Log Bed, Furnace, Forge, Oven, Rain Cistern,
Tanning Pit, Pit Trap); the twelfth, Homestead, instead unpacks into three of those same kit items
(one Cabin Kit + two Rain Cistern Kits) plus raw materials, each of which is then placed separately.
No new structure content is added anywhere in the chain — only the portable-kit delivery mechanism.
Path Kit instead completes one of vanilla's own `Imp_Path*` environment improvements directly (`Imp_PathNorth`/
`South`/`East`/`West`) — same vanilla content, delivered instantly rather than through whatever
means the base game normally uses to build it.

## Notes

Nine of the structure-kit perks (Homestead, Cabin, Mud Hut, Log Bed, Furnace, Forge, Oven, Rain
Cistern, Tanning Pit) are a trimmed extraction of Community Mod Chest's Homestead /
Better-Perk-Buildings perks — none of CMC's village content, NPCs, or quests are included. Path
Kit, Well Kit, Cellar Kit, and Pit Trap Kit are new content, not part of that extraction.
Community Mod Chest has since trimmed its own kit-perk lineup down to two (its "Founders Kit" —
the same Homestead-style bundle, just renamed — and its own "Rain Cistern Kit"), so those are the
only two perks that still functionally overlap with this mod. Running both mods together is safe
(distinct UniqueIDs, no load conflict) but redundant for those two: you'd see both this mod's
"Homestead" and CMC's "Founders Kit" at character creation, and two identically-named "Rain
Cistern Kit" perks.
