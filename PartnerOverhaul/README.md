# Partner Overhaul

Fixes a cluster of vanilla EA "Partner" companion NPC bugs, and ships opt-in diagnostics for a
handful more that are real but not yet root-caused. Ships zero new cards — every change here
patches or mutates *existing* vanilla content.

**Version:** 1.0.4
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
- **Partners can feed Pine Needles/Charcoal into a Fireplace**, same missing-wiring shape as the
  cleaning fix above.
- **Removed a stray "Defecate" button** from the "Tree Top" scenic climb-a-tree event — leftover
  from a copy-pasted action template, unrelated to the event's own content.
- **A Partner's Energy level is now visible on their card.** Vanilla fully authors Energy status
  text ("Looks exhausted." / "Looks very tired." / "Looks tired." / "Looks full of energy.") but
  ships it with `VisibleToPlayer`/`ShowInMainTab` both false on every entry — unlike Hunger,
  Thirst, Warmth and Wakefulness, whose equivalent entries are shown. There was no way to tell how
  tired your Partner was; now there is.
- **NPC action sounds are no longer audible from a different environment.** Vanilla plays every
  NPC's eating/working sound through a single global, non-positional audio source with no
  distance/environment check at all.
- **A Partner starting dialog no longer interrupts an in-progress player action** (e.g. a
  spirit-summon ritual). Vanilla's modern Partner Duty dialog trigger had no "is the player busy"
  guard at all.

## Diagnostics (opt-in, default ON)

Four more reported bugs are real but not yet safely fixable without evidence — no blind fixes,
per this project's own debugging discipline:

- Fishing line reportedly freezing a Partner's entire AI (eating/drinking/moving all stop).
- A Partner getting stuck brain-tanning the same hide and never moving to the next marked one.
- Clothes carried by a Partner not reducing carry weight / not preventing cold the way equipped
  clothes should (the underlying equip → weight-reduction → temperature pipeline is confirmed
  correctly wired in vanilla code, so this is a narrower edge case, not a missing mechanism).
- Cauldron/stew ownership resetting unexpectedly.

If you hit any of these, play a session with `LogOutput.log` diagnostics on (default) and share
the log — the `[PartnerDiagnostics]` lines are what a real fix will be built from.

A reported pouch/acorn-flour stacking-math bug also has opt-in diagnostics now (1.0.2):
`[PouchTransferDiagnostics]` lines log the receiving container, source card, input quantity, and
clamped result whenever a Pouch or acorn flour is involved in a liquid pour/transfer. This is the
generic engine transfer-clamp shared by hundreds of vanilla items, so a real fix needs a returned
log before anything is touched — **not fixed yet, diagnostics only.**

**Not investigated yet:** general "Partner won't leave the house for water without being led"
reports — see the plan doc for status.

## New features (opt-in, default OFF — NOT bug fixes)

These exist because vanilla's actual behavior turned out to be a design limitation, not a bug, on
investigation — see the plan doc for the research behind each:

- **Reduce Partner Move Costs** (config) — discounts the resource-stat cost a Partner pays for
  inter-environment travel. Vanilla has no "path tile" concept for NPC movement (only the
  player's own travel-DA speed bonus does), so this is a general discount, not a path-specific one.
- **Reserved Fuel/Wood UIDs** (config) — a comma-separated list of CardData UniqueIDs a Partner
  will never auto-select for Firekeeping. No in-game UI yet; edit the config value directly.
- **Consolidate Duplicate Wounds** (config, 1.0.1) — stops a combat round from applying a wound
  you already have equipped, instead of stacking a 28th separate Minor Laceration. Vanilla spawns
  a fresh, independently-healing wound card every round with no dedup at all, so the count you see
  is accurate — turning that off is a combat *design* change, not a bug fix, hence opt-in. Damage,
  stat changes, armor wear and the encounter log are untouched. **Not yet confirmed in-game** —
  verifying it needs a live encounter that lands the same wound type across several rounds.

## Known interactions

- **Invincibility** (in-house dev mod) prefixes the same `EncounterPopup.GenerateAndApplyPlayerWound`
  and skips its body while toggled ON. In those rounds the wound-consolidation transpiler above does
  not run - which is correct by construction, since a skipped body generates no wound candidates at
  all (there is nothing to consolidate). With the toggle OFF, or without that mod, consolidation is
  fully active. Assessed benign in the 2026-09-01 fleet audit; no action needed.
- **AdvancedCopperTools** also prefixes the same method (armor-list repair, void prefix) - runs in
  all cases, no interaction with the transpiler.
