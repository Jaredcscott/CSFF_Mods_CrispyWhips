# Partner Overhaul

Fixes a cluster of vanilla EA "Partner" companion NPC bugs, and ships opt-in diagnostics for
reporting new ones. Ships zero new cards — every change here
patches or mutates *existing* vanilla content.

**Version:** 1.1.0
**Author:** Jared
**Requires:** CSFFModFramework (soft dependency — for boot ordering only; no framework content is
loaded by this mod)

## Fixes (confirmed root cause, via decompiled vanilla source)

- **Partners can now eat stew/broth.** Vanilla's stew (`OLD_LQ_StewWater`) and broth
  (`LQ_NightBroth`) shipped with their "Drink" action invisible to the Eat duty engine — a Partner
  had no way to satisfy hunger from a pot of either, and would substitute plain water instead
  (matching the "drinks all the water when hungry" report). Thirst still targets plain water only.
- **Partners resume cleaning finished homes.** The "Clean" action on a *finished* Cabin/MudHut/
  Enclosure was missing the wiring that its own mid-construction variants have — a Partner could
  clean while your home was being built, then never again once it was finished.
- **Partners stop over-watering a capped garden plot.** The "Water" action had no check on the
  plot's current Hydration, so a watering Partner would keep draining every water container
  available even after the plot was already full.
- **Partners can feed Charcoal into a Fireplace or a Fire Pit.** Fire Keeping only performs actions
  that carry one of its own action tags, so the Charcoal feed on both now carries the duty and the
  tag, same missing-wiring shape as the cleaning fix above. Fire Keeping also only reaches that
  feed step when a `tag_Fuel` card (Twigs and the like) is around, and Charcoal is not one, so
  since 1.1.0 the duty's own "fuel on the board" check also counts Charcoal; Charcoal itself is not
  retagged, so feeding it by hand works exactly as before. Confirmed in a scripted run on both
  fires. Pine Needles are NOT fed: measured on
  1.1.0, Fire Keeping has no step for them (they are a fragrance item, not a fuel), so earlier
  versions of this list that said a Partner feeds Pine Needles into a Fireplace were wrong.
- **Partners can tend a Hearth and warm themselves at it.** A Hearth and a Hearth (Awakened) had no
  Partner wiring at all. You can now mark either for Fire Keeping, and a marked one is fed Firewood,
  fuel and Charcoal and used for Warming Up, as a Campfire is. The mark survives the Hearth's own
  transform (the fire dying turns it into an extinguished Hearth, which now also lists Fire Keeping).
  Checked in a scripted run; not yet confirmed.
- **Four small wiring gaps closed.** You can mark a Boar Piglet carcass for Butchering (its mark
  menu offered Skinning, which nothing on it uses; Skinning stays listed so an old mark is not
  lost); a Partner smiths the Arrowhead and Chisel blade blanks as it does the other 45; eats Wheat
  Bloodbread as it eats Rye Bloodbread; and cleans the Mud Hut's Right Room as it cleans the Left.
  Checked in a scripted run; not yet confirmed.
- **Duty marks survive the seasonal swaps.** The marks you put on a River (Dig Mud, Spearfish), a
  frozen River (Create Ice Hole) and a Pond (Dig Mud) used to clear silently when the card froze or
  thawed. Both twins now list each other's duties, so the mark carries across. A Heather patch and a
  Meadowgrass patch also list each other's harvest duty, which keeps the mark when a Meadowgrass
  patch turns into Heather. The other direction cannot keep it: there the game removes the Heather
  patch and creates a new Meadowgrass patch, and a new card starts with no marks. The cost: the
  twin's mark menu shows a toggle that does nothing there (for example Spearfish on frozen ice).
  Confirmed in a scripted run for the River, Pond and Meadowgrass-to-Heather swaps.
- **Removed a stray "Defecate" button** from the "Tree Top" scenic climb-a-tree event — leftover
  from a copy-pasted action template, unrelated to the event's own content.
- **A Partner's Energy level is now visible on their card.** Vanilla fully authors Energy status
  text ("Looks exhausted." / "Looks very tired." / "Looks tired." / "Looks full of energy.") but
  ships it with `VisibleToPlayer`/`ShowInMainTab` both false on every entry — unlike Hunger,
  Thirst, Warmth and Wakefulness, whose equivalent entries are shown. There was no way to tell how
  tired your Partner was; now there is.
- **A Partner's Comfort is now visible on their card (1.1.0).** Vanilla authors five Comfort lines
  ("Looks very comfortable", "Looks comfortable", "Looks uncomfortable", "Looks very
  uncomfortable", "Looks terribly uncomfortable") but ships every one with
  `VisibleToPlayer`/`ShowInMainTab` both false, the same as Energy above, so a Partner's comfort
  never showed. Vanilla's bands cover 67 to 100, 33 to 66, -49 to -1, -75 to -50 and -100 to -76 on
  a stat that starts at 0, so a Partner whose Comfort sits between 0 and 32 still shows no Comfort
  line (that gap is vanilla's data, not this fix). By vanilla's data, its Body Temperature statuses
  for heat and cold and its sleepy statuses push Comfort down. Wellbeing, Appetite, Filth and
  Intestines stay hidden: they read as internal values, not as something a person says about a
  companion.
- **NPC action sounds are no longer audible from a different environment.** Vanilla plays every
  NPC's eating/working sound through a single global, non-positional audio source with no
  distance/environment check at all.
- **A Partner starting dialog no longer interrupts an in-progress player action** (e.g. a
  spirit-summon ritual). Vanilla's modern Partner Duty dialog trigger had no "is the player busy"
  guard at all.
- **Pouring a powder into a container that is nearly full no longer destroys it (1.1.0).** Vanilla
  pours Acorn Flour (and any powder with a liquid form) by destroying the whole card and adding only
  what fits. A Pouch holds 150 and one Acorn Flour pours 112.5, so a second flour vanished with 75 of
  it lost, whether you dragged one card or a stack. Now a pour that cannot take the whole card is
  refused and the card stays in your inventory; pours that fit are unchanged. Config: `Fixes` /
  `Refuse Partial Powder Pours` (default ON). The vanilla loss was measured in a running game; the
  refusal itself is not yet confirmed in one.

## Looked into, not bugs

These were reported as Partner bugs and checked in a running game for 1.1.0; each is how vanilla
is meant to work:

- **Clothes that "don't lighten the load or keep the Partner warm."** A worn coat does both: in the
  cold a Fur Coat's weight drops from 1200 to 200 and it warms the Partner. A Partner takes its
  clothes off when it feels warm (vanilla's own clothing preferences), and a coat that is carried
  but not worn weighs its full amount.
- **Brain-tanning "stuck on the first hide."** A Partner finishes a marked Small hide with one Brain
  Mush and moves on to the next. By vanilla's data a Large hide takes four Brain Mush, so a Partner
  stays on it much longer.
- **Cauldron ownership "resetting."** Giving a Partner a Clay Cauldron survives reopening its popup
  and a save and reload.

## Looked into and closed: fishing, water and cold

These were open reports in earlier versions of this page. Each was measured in a running game and
needed no change from this mod:

- **A fishing trip that can't fish.** With a Fishing Line and bait a Partner fishes for hours as
  intended, catching Minnows about twice an hour. Without bait, or with only a Fishing Rod, the
  expedition ends on its first tick and the Partner reports back once; pressing that report's
  Continue ends the expedition and the Partner goes back to its normal duties. The report that
  "kept coming back" in testing was the test driver failing to press that Continue button, not the
  game.
- **"Won't leave the house for water."** A Partner with water at hand drinks it without leaving.
  With none, a Partner that is *not* following you sets off once its Hydration is 90 or lower
  ("Looks very thirsty."): told to stop following, one walked from the Community Mod Chest village
  to the river in four moves and drank (Hydration 78 to 288). A Partner that *is* following you
  stays with you instead, because vanilla's Follow duty (weight 1000) outweighs Seek Drink until
  Hydration reaches 0; at 0 Seek Drink weighs 1001, and the Partner walks to the river, drinks and
  comes back to you. From Hydration 90 that takes about 45 game hours, with rising stress and
  falling Favour on the way but no lasting harm. Tell a following Partner to stop following if you
  want it to fetch water sooner.
- **A following Partner in the cold.** A Partner warms itself only at a fire it can reach: Seek
  Fire looks for one within its own short range and Warm Up needs one on the Partner's board. With
  no fire on the board you are on, a following Partner cannot warm itself; on a measured cold night
  its Body Temperature fell from 46 to -95 in 12 game hours and it died of hypothermia about 18.5
  game hours in. With a lit Campfire placed on the same board it warmed up at once. Keep a fire on
  the board you are on in cold weather, or tell your Partner to stop following.
  The opt-in Cold Partner Asks For Warmth only makes it ask you for that fire earlier.

## Diagnostics (opt-in, default OFF)

For reporting a Partner bug. Both options are off by default; turn the one you need on in
`BepInEx/config/crispywhips.partner_overhaul.cfg`, play until the problem shows, and share
`LogOutput.log`:

- **Enable Diagnostics** logs `[PartnerDiagnostics]` lines: each Partner's current duty (checked
  every 10 seconds), what it has equipped after it re-dresses, a cauldron's owners when you open it,
  and, since 1.1.0, every duty that acts on items (brain-tanning among them) with whether the
  Partner can do it, printed only when that answer changes.
- **Enable Pouch Transfer Diagnostics** logs `[PouchTransferDiagnostics]` lines for any liquid pour
  involving a Pouch or Acorn Flour.

## New features (opt-in, default OFF — NOT bug fixes)

These exist because vanilla's actual behavior turned out to be a design limitation, not a bug, on
investigation — see the plan doc for the research behind each:

- **Reduce Partner Move Costs** (config) — discounts the resource-stat cost a Partner pays for
  inter-environment travel. Vanilla has no "path tile" concept for NPC movement (only the
  player's own travel-DA speed bonus does), so this is a general discount, not a path-specific one.
  Since 1.1.0 it discounts only NPCs allied with you (your Partner). Earlier versions also skipped
  the cost for wild animals, so a fleeing wolf could keep its stamina on a random share of
  its hops; they now pay their full vanilla cost. Confirmed in a scripted run.
- **Reserved Fuel/Wood UIDs** (config) — a comma-separated list of CardData UniqueIDs a Partner
  will never auto-select for Fire Keeping. No in-game UI yet; edit the config value directly.
  Since 1.1.0 the list applies only to an allied Partner's Fire Keeping duty, on any fire that
  offers it (modded stoves and forges included); every other duty, and every other NPC, wild
  animals included, still sees the listed items. Earlier versions withheld them from all of those
  too, which this option never promised. When a listed card is one of the duty's own fuel
  checks (Firewood, Wood or Dried Peat on the board or in the Partner's inventory), that check now
  answers no, so Fire Keeping goes on to its other fuels (a Twigs or other `tag_Fuel` card) instead
  of entering a branch with nothing left to burn and standing by a dying fire. A listed `tag_Fuel`
  card itself is not skipped this way: if every fuel the Partner can reach is listed, she feeds
  nothing. The list is read when the game starts, so edit it, then restart. Checked in a scripted
  run; not yet confirmed.
- **Partners Drink From Lakes And Streams** (config, default OFF) - lets a Partner drink from a Lake,
  Bog, Marsh, Mire, Swamp or Stream as it drinks from a River, and lets its Seek Drink trip end at
  one. Seek Drink picks the closest water, so on a map with both a river and a lake the
  destination can change; that is why it is opt-in. Ponds are not included (their drink is a
  poorer trade with a heavier infection roll). Takes effect at game load. Only the wiring has been
  checked: a Partner drinking at a lake or stream has not yet been seen in a running game.
- **Cold Partner Asks For Warmth** (config, default OFF) - while a following Partner on your board
  "Looks very cold." (Body Temperature 1 to 50) and no fire is in reach, it voices its need for
  warmth to you through the game's own Express Need for Warmth request. Vanilla only lets it ask once
  it "Looks hypothermic." (0 and below), which is one step from death. The option changes nothing
  else: it moves nobody, does not change what the Partner does about the cold (it still cannot warm
  itself without a fire on your board, see the finding below), keeps vanilla's dialog-cooldown gate on
  the request (the same one every Express Need request has), and only applies while it is on your board. Each ask is one
  `[ColdPartnerWarmth] ... asked for warmth` line in `LogOutput.log`. Takes effect at game load.
  Checked in a scripted run, not yet in a hand-played game.
- **Consolidate Duplicate Wounds** (config, 1.0.1) — stops a combat round from applying a wound
  you already have equipped, instead of stacking a 28th separate Minor Laceration. Vanilla spawns
  a fresh, independently-healing wound card every round with no dedup at all, so the count you see
  is accurate — turning that off is a combat *design* change, not a bug fix, hence opt-in. Damage,
  stat changes, armor wear and the encounter log are untouched. Confirmed in a running game
  (an encounter landing the same wound type across several rounds).

## Known interactions

- **Invincibility** (in-house dev mod) prefixes the same `EncounterPopup.GenerateAndApplyPlayerWound`
  and skips its body while toggled ON. In those rounds the wound-consolidation transpiler above does
  not run - which is correct by construction, since a skipped body generates no wound candidates at
  all (there is nothing to consolidate). With the toggle OFF, or without that mod, consolidation is
  fully active. Assessed benign in the 2026-09-01 fleet audit; no action needed.
- **AdvancedCopperTools** also prefixes the same method (armor-list repair, void prefix) - runs in
  all cases, no interaction with the transpiler.
