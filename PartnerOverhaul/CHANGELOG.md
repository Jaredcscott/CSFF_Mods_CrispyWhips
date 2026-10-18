# Changelog

## 1.1.4 (2026-10-17)

### Added
- **Talk with your Partner.** The Partner's Talk menu has a new answer, "Talk with" and the name you chose, that opens a short conversation in which your Partner speaks in its own words instead of the game's narration. Each line it says takes 15 minutes and, like Casual Talk, puts off the Partner's own next remark to you; it gives no stats. "Step back" closes it. Needs CSFFModFramework, which loads the conversation; without it the Talk menu is exactly the game's. Setting: `Talk With Your Partner` (new `Conversations` section), default ON. This is the mod's first text of its own, in English only.
- **Your Partner opens the conversation by saying how it is doing.** The first line depends on its state, most pressing first: cold, hungry, worn out, stressed, feeling neglected, low in spirits, happy, or a plain "I'm doing fine" when none applies. A new "How are you holding up?" answer on the later lines asks again. Each remark costs the same 15 minutes and gives no stats. Wording is a first draft, English only. Without CSFFModFramework nothing changes.
- **Talk about the world around you.** A new "Talk about..." answer in the "Talk with" conversation lists what your Partner has something to say about right now: the season you are in, morning or night, the home you are standing in (Cabin, Mud Hut, Enclosure), a Garden Plot in the room, a lit Hearth in the room, falling snow, rain, and a storm (heavy or torrential rain). With Community Mod Chest installed it also offers that mod's Village and Village Inn while you are there; without it those two never appear. Each topic is one spoken line, takes 15 minutes, and gives no stats, like the other lines. A topic your Partner has told you stays out of the list for about a season (30 days), so the list refills as the year turns. Adds fourteen small hidden markers to the Partner (the game shows none of them) and needs CSFFModFramework. English only. Not yet confirmed in a running game.
- **Your Partner can start a conversation itself (opt-in).** New setting `Partner Starts Conversations`, default OFF. When on, your Partner opens the "Talk with" conversation by itself at most once a day, only while it is on your board, and more often in the morning. It stays quiet while it is hungry, cold, guarded, neglected, stressed or close to leaving, so the game's own requests come first, and it never interrupts a ritual. Each line takes 15 minutes and gives no stats. Needs CSFFModFramework. With the setting off nothing changes. Not yet confirmed in a running game.
- **Your Partner's opening words now match its personality.** The "How are you holding up?" openers (cold, hungry, worn out, stressed, neglected, low, happy, plain) each have four extra wordings: assertive, anxious, warm and reserved. Which one plays follows the personality perks your Partner was given, never the kind of Partner preset it started from: the Confident, Trusting, Anxious, Impulsive, Introvert, Reserved and Independent perks each add a small hidden marker (four markers in all, one per wording; none is shown in game), and a Partner holding one hears that wording on a new run and on an old save alike. A Partner with several hears the more guarded wording (reserved, then anxious, then warm, then assertive). A Partner with none of those perks, such as a custom one, is read from how assertive, argumentative, trusting or solitary it is, as before; one who fits none of them hears the same lines as before. Wording is a first draft, English only. Not yet confirmed in a running game.
- **Your Partner remembers what just happened.** After you win a fight or get away from one with your Partner beside you, finish a structure (such as a Cabin, Mud Hut or Enclosure) where it is, or land the first fish of the day there, the next "Talk with" conversation can open on it ("You held your ground back there."), and "Talk about..." lists it. A memory lasts one day and is talked about once. Being cold, hungry or worn out still comes first. Adds four small hidden markers to the Partner and needs CSFFModFramework. English only. Not yet confirmed in a running game.
- **Your Partner notices how YOU are.** When you are badly hurt, bleeding, septic, very hungry or very cold, the next "Talk with" conversation can open on a remark about you ("You're limping and trying not to show it."). It is words only: it heals and feeds nothing, takes 15 minutes like the other lines, and gives no stats. It reads YOUR state, not the Partner's, so a hungry Partner with a well-fed you does not say "you look hungry". The Partner's own cold, hunger, exhaustion and memories still come first; a remark about you comes before its stress, neglect and mood. The thresholds are first guesses and may be tuned (Pain 201 or more, Blood Loss 3 or more, Sepsis 901 or more, Satiation 70 or less, Body Temperature 50 or below). Wording is a first draft, English only. Not yet confirmed in a running game.
- **Your Partner marks the big moments in your bond.** The first "Talk with" after your Partner reaches the top of Trust ("Looks really trusting.") or of Affection, and after it has been with you 7, 30 and 100 days, opens on a line about it, once. It comes before everything else, even cold or hunger, and never plays again for that milestone. Words only: no Trust, Affection or other stat is raised, beyond the usual 15 minutes and put-off next remark. An older Partner who is already past several anniversaries hears only the biggest. The Relationship stat has no milestone because it starts at its maximum and the game gives it no bands to cross. Adds five hidden markers to the Partner and needs CSFFModFramework. English only. Not yet confirmed in a running game.

### Fixed
- **A Partner who is neither comfortable nor uncomfortable now shows a status.** The game's Comfort lines skip 0 to 32, so a Partner there showed nothing. The card now reads "Looks at ease." in that range. It is text only and changes nothing about the Partner. English only.
- **Your Partner is called by name in dialog.** The Talk window title and each dialog log line showed "Partner" rather than the name you chose. They now show the chosen name. Only Partners are renamed: traders, the Hag and other characters keep their own titles, and a scene that sets its own title keeps it. If the name is empty the game's text is used.
- **Charcoal can now be reserved.** Listing Charcoal (`51c751469bd15ce46804e399479d1764`) in `Reserved Fuel/Wood UIDs` used to leave Fire Keeping's Charcoal branch open, so the Partner stood at a fire with only reserved Charcoal and fed nothing. The branch now steps aside for a reserved Charcoal and the duty tries its other fuel. With the list empty, nothing changes.

### Changed
- **With `Partners Tend The Hearth` on, a Hearth only takes a feed while its fuel is below 50%.** A Partner's Fire Keeping tops the Hearth up when it runs low instead of loading it full, which keeps the big loads your job. The limit is on the Hearth's feed actions, so it applies to you too; Campfires and Fireplaces behave as before. 50% is a placeholder and may be tuned. With the setting off (the default) nothing changes.
- **Easy Mode now consolidates duplicate wounds for that run.** Tick the Easy Mode toggle on the character screen when you start a new game, and repeat wounds of one type no longer stack in that run, exactly as with `Consolidate Duplicate Wounds` on. Your other runs keep vanilla stacking. The setting itself still defaults to false and, when true, still applies to every run. The game's Easy Mode added nothing on its own, so this is all the toggle does. A save you already started with Easy Mode ticked gets it too. Loading a normal save after an Easy Mode run in the same session no longer carries the Easy Mode mark into that save (a base-game leak).

## 1.1.3 (2026-10-15)

A balance pass to keep the mod in step with the base game.

### Fixed
- **Partners really stop over-watering a full garden plot.** The 1.1.0 fix never worked: it set its 90% limit on a scale that runs from 0 to 1, so every plot passed. A Garden Plot at 90% Hydration or more now offers no "Water" to a Partner or to you; below 90% it waters as before.

### Changed
- **Hearth tending is now an opt-in setting, `Partners Tend The Hearth`, default OFF.** The game gives the Hearth no Partner duties and it is already the strongest fire, so this is a choice rather than a bug fix. With it off, a Partner no longer feeds or warms itself at a Hearth or a Hearth (Awakened), and a Hearth mark already in your save does nothing but is not lost. This is a new setting, so every install, existing ones included, starts with it off: set it to true in `crispywhips.partner_overhaul.cfg` and restart to keep the 1.1.2 behaviour. From then on BepInEx keeps whatever value you set until you change it or delete the entry.
- Clearer text for two opt-in settings. `Consolidate Duplicate Wounds` now says it makes combat less dangerous, because repeat wounds' pain, bleeding and infection no longer stack. `Reduce Partner Move Costs` now says it skips the whole cost on a share of hops (about half at the default 50) rather than discounting every hop. Neither setting's behaviour changed.

## 1.1.2 (2026-10-13)

### Fixed
- **Partners now eat Night Broth and Stew when starving.** The eating duties only pick items tagged Human Food, and these two lacked it, so the 1.1.0 fix never took effect. Both now carry the tag. A hungry (not starving) Partner still ignores broth in its own pack; the starving duty takes it from a bowl nobody owns.

### Changed
- The startup log line now also counts tags added to vanilla cards, and Cold Partner Asks For Warmth logs a warning once if a game update changes the request it widens.

## 1.1.1 (2026-10-12)

Version number only: nothing changed since 1.1.0, which has not been released on Nexus, so its notes below are what this release brings over the last published version.

## 1.1.0

### Added
- **New opt-in `Cold Partner Asks For Warmth` (default OFF).** A following Partner on your board that
  "Looks very cold." (Body Temperature 1 to 50) with no fire in reach now voices its need for warmth
  through the game's own Express Need for Warmth request, instead of only once it "Looks hypothermic."
  (0 and below). It keeps vanilla's dialog-cooldown gate, moves nobody and changes no other duty, and each ask
  is one `[ColdPartnerWarmth]` Info line. Default only reaches fresh installs: BepInEx keeps a value
  already in an existing `.cfg`. `Patcher/ColdPartnerWarmthPatch.cs`.
- **Partners tend and warm themselves at a Hearth and a Hearth (Awakened).** Both cards can now be
  marked for Fire Keeping; their Feed Firewood, Feed Fuel (both) and Feed Charcoal actions and their
  Warm Up carry the duty and the action tags the Campfire's own do. Charcoal also takes
  `PartnerAction3` so the widened tag-fuel step reaches it.
  The mark also survives the fire dying: the two cards a Hearth and a Hearth (Awakened) become when their
  fuel runs out now list Fire Keeping too (the game keeps a duty mark across a transform only where the new
  card lists that duty; a harness run showed the mark dropped without it). `Patcher/GameLoadPatch.cs`
  `FixHearthDuties`.
- **New opt-in `Partners Drink From Lakes And Streams` (default OFF).** Wires Drink Free and Seek
  Drink to the "Drink" of RiverLake, RiverBog, RiverMarsh, RiverMire, RiverSwamp and StreamQEE.
  Default only reaches fresh installs: BepInEx keeps a value already in an existing `.cfg`.
- **Duty marks survive the River, Pond and Heather/Meadowgrass swaps.** FrozenRiver lists Dig Mud and
  Spearfish, River lists Create Ice Hole, PondFrozen lists Dig Mud, and each patch lists the other's
  harvest duty. The twin's duty menu shows a toggle that does nothing there. A scripted run confirmed
  the River and Pond swaps and a Meadowgrass patch turning into Heather; the Heather to Meadowgrass
  direction cannot keep a mark, because there the game destroys the Heather patch and spawns a new
  Meadowgrass patch rather than transforming it, and a new card starts with no marks.

### Fixed
- **"Reserved Fuel/Wood UIDs" no longer leaves a Partner standing by a dying fire when the listed
  Wood sits beside an unlisted fuel.** Fire Keeping tests "Wood on the board" before it ever looks
  at `tag_Fuel` cards, and enters the single feed step behind that test; the option emptied that
  step's item list, so with a reserved Wood on the board and a Twigs beside it the duty read
  unselectable (T3.39 measured `NotSelectable` true) or finished having fed nothing, and the fire went
  out. When the list is set, the duty's own fuel checks that name a listed card (Firewood, Wood,
  Dried Peat; board and inventory) are replaced at boot, in Fire Keeping's step list only, by a check
  that keeps vanilla's conditions and outcomes and answers no for a listed card, so the duty falls
  through to its other fuels. Nothing changes while the list is empty (the default). A listed
  `tag_Fuel` card is not skipped this way. `Patcher/ReserveAwareDecision.cs`,
  `GameLoadPatch.GuardReservedFuelBranches`.
- **Four one-card wiring gaps:** Boar Piglet carcass offers Butcher Medium (Skin Medium kept), the
  Arrowhead and Chisel blade blanks' "Smith" carry Place on Ground, Wheat Bloodbread's "Eat" carries
  Eat and Eat Free, and the Mud Hut Right Room carries the Clean marker and ref.
- **A Partner now actually feeds Charcoal into a marked Fire Pit or Fireplace.** 1.1.0 gave the
  "Feed Charcoal" action the Fire Keeping duty and its `PartnerAction3` tag, but a harness run on
  1.1.0 (T2.500) showed Fire Keeping still unselectable with only Charcoal on the board, and no
  feed: the duty reaches its `PartnerAction3` step only through a "`tag_Fuel` on the board" check,
  and vanilla Charcoal has no `tag_Fuel`. That one step of the Fire Keeping duty is now replaced at
  boot by a check that keeps vanilla's conditions and outcomes and also passes when Charcoal is on
  the board. Charcoal is NOT given `tag_Fuel`: every fire's "Feed Fuel" (`tag_Fuel`) action sits
  ahead of its "Feed Charcoal", so retagging would change what a player's own charcoal drag does.
  The vanilla decision asset is not edited (only Fire Keeping's step list points at the new check)
  and no step number moves, so saves are unaffected either way. If a game update changes the
  duty's shape the change is skipped with a Warning in `LogOutput.log`; when it applies, one Info
  line reads `[GameLoadPatch] Firekeeping: step <n> ('Tag Fuel on Board') now also passes on
  Charcoal, ...`. Vanilla's "Feed Charcoal" on both fires also accepts Embers, so a Partner may
  feed Embers lying beside the Charcoal, as a player can (Embers alone do not open the step). Confirmed in a scripted run on both fires.
  `Patcher/CharcoalFuelDecision.cs` (new), `Patcher/GameLoadPatch.cs`
  `WidenTagFuelDecisionToCharcoal`.
- **Partners can feed Charcoal into a Fire Pit as well as a Fireplace, and the Fireplace Pine Needles
  claim is withdrawn.** Fire Keeping only performs actions that carry one of its action tags
  (`PartnerAction1/2/3`), so a duty ref alone never made a Partner feed anything. "Feed Charcoal" on
  both cards now carries the Firekeeping duty and `PartnerAction3` (the tag-fuel step). Pine Needles
  are not fed: measured on 1.1.0, the duty has no step for them, so the 1.0.0 line below saying
  Partners can feed Pine Needles into a Fireplace was wrong. `Patcher/GameLoadPatch.cs`
  `FixFireplaceFuelGap` and the new `AppendDutyRefToAllNamed`.
- **Pouring a powder into a container that cannot take all of it no longer destroys the powder.**
  Vanilla's pour action for a powder (Acorn Flour, ground medicines, any solid with a liquid form)
  always destroys the card but adds only what the container has room for. A Pouch holds 150 and one
  Acorn Flour pours 112.5, so a second flour into a part-full Pouch vanished with 75 of it lost, on
  a single drag and on a stack drag alike (measured in a running game on 1.0.5). Such a pour is now
  refused and the card stays whole; a pour that fits is unchanged. A partial pour is refused rather
  than split because a part-empty flour card would still count as a whole flour in every recipe.
  New config `Fixes` / `Refuse Partial Powder Pours` (default ON; turning it off restores vanilla).
  `Patcher/PartialPowderPourPatch.cs`.
- **A Partner's Comfort is now visible on its card.** Vanilla's `Partner_Comfort` NPCStat has five
  fully-authored status lines ("Looks very comfortable", "Looks comfortable", "Looks
  uncomfortable", "Looks very uncomfortable", "Looks terribly uncomfortable"), every one shipped
  with `VisibleToPlayer` and `ShowInMainTab` false, the shape 1.0.3 fixed for Energy.
  `Patcher/GameLoadPatch.cs` `FixPartnerStatVisibility` now walks a list of those two stats and
  flips both flags on every entry of each at boot, with its own idempotence check and its own
  warning if a stat goes missing on a game update. Vanilla's bands leave 0 to 32 without a line, so
  a Partner at a neutral Comfort still shows none; Wellbeing, Appetite, Filth and Intestines stay
  hidden. In `LogOutput.log` the Energy line now ends "(were hidden)" instead of "(were fully
  hidden)", and a matching `[GameLoadPatch] Partner_Comfort: made 5 status text(s) visible on the
  Partner card` line joins it.

### Changed
- **"Reserved Fuel/Wood UIDs" now acts only on an allied Partner's Fire Keeping.** The option's
  filter hooks the item sort that every NPC and every duty shares
  (`NPCItemSelectionSettings.SortFoundItemsByPreference`), and it used to withhold the listed items
  from all of them: a reserved item was hidden from every Partner duty, not only Fire Keeping, and
  from wild animals too, while the option's description promised fuel duties only. It now checks the
  sorting NPC and duty and filters only when an allied Partner is choosing items for Fire Keeping
  (`PartnerDuty_Firekeeping`). The scope is the duty, not the card, so fires added by other mods that
  offer Fire Keeping stay covered. This is a change in what the option does, not a new default, so
  it applies to every install that has the list set; an empty list (the default) still does
  nothing. Checked in a scripted run, not yet confirmed. `Patcher/WoodReserveListPatch.cs`
  `FilterReservedItems` and `AppliesTo`; the config description in `Plugin.cs` names the scope.
- **"Reduce Partner Move Costs" now discounts only allied Partners; wild animals pay their full
  move cost again.** The option skips the cost routine every NPC move runs
  (`MoveDutyAction.ApplyCosts`), and until now it did so for whichever NPC was moving: measured with
  the option at 100 percent, a wild wolf's flee hop kept its 35 stamina. The routine now checks the
  moving NPC and applies the discount only when it is allied with the player; the random percent
  roll and the 50 percent default are unchanged. This changes what the option does, not its
  default, so it applies to every install that has the option on. Confirmed in a scripted run
  (T3.80). `Patcher/PathSpeedPatch.cs` `MaybeSkipCosts` and `AppliesTo`.

### Docs
- The README's diagnostics section said both options were ON by default; they have been OFF since
  1.0.5. It now also records which reported Partner bugs turned out to be vanilla behaviour when
  checked in a running game (clothes, brain-tanning, cauldron ownership). Its "Still under
  investigation" section is now "Looked into and closed: fishing, water and cold": the fishing trip
  that cannot fish and the Partner that would not fetch water from the CMC village were both closed
  by measurement with no code change (the repeating fishing report was the test driver not pressing
  Continue; a following Partner waits for Hydration 0 before it fetches water), and a measured cold
  night adds that a following Partner with no fire on your board cannot warm itself and dies of
  hypothermia in about 18.5 game hours.

- The README's twin-swap line said a Heather or Meadowgrass mark carries across in both directions;
  only the Meadowgrass to Heather direction can (see Added). Its "not yet confirmed" notes now follow
  the test record: the Charcoal feed, the Reduce Partner Move Costs scope and Consolidate Duplicate
  Wounds are confirmed; the Hearth, the four wiring gaps and Reserved Fuel/Wood UIDs are checked in
  a scripted run and not yet confirmed; a Partner drinking at a lake has not yet been seen at all.

### Config note for existing installs
- BepInEx keeps the values already written in `BepInEx/config/crispywhips.partner_overhaul.cfg`, so
  the defaults of the two new options (`Partners Drink From Lakes And Streams`, `Cold Partner Asks
  For Warmth`, both OFF) reach fresh installs only. The narrowed scopes of `Reserved Fuel/Wood UIDs`
  and `Reduce Partner Move Costs` are code changes, not new defaults, so they apply to everyone who
  has those options set. Logging is unchanged in shape: the new wirings fold into the one aggregate
  `[GameLoadPatch] attached N duty ref(s)` Info line (per-target detail at Debug), and both
  `[Diagnostics]` options still default OFF.

### Fixed - diagnostics
- **The cauldron ownership diagnostic now actually prints.** Its `OwnershipToggle.Refresh` trace
  read a card's `AllowedUsers` list through reflection restricted to non-public fields, but on
  EA 0.68b that field is public, so the lookup returned nothing and the trace quietly returned
  before logging. With `Enable Diagnostics` on, opening a card that uses ownership (a Clay
  Cauldron) with a Partner allied now logs `[PartnerDiagnostics] OwnershipToggle.Refresh on '...'
  ... AllowedUsers count=N` as intended. The list is now read directly, so a future rename breaks
  the build instead of silencing the line.

### Changed - diagnostics
- **The duty trace covers every item duty a Partner weighs, not only brain-tanning.** Until now it
  logged only duties whose name contained "BrainTan", which gave no view of firekeeping, cleaning or
  modded station duties. It now logs `[PartnerDiagnostics] Duty '<duty>' step <n> on '<Partner>':
  CanBePerformed=... started=... affect=...`, plus the cards a started action is holding, for every
  item duty an allied Partner considers. To keep the log readable it prints only when that
  Partner's verdict for that duty step changes, never once per evaluation. Still off unless
  `Enable Diagnostics` is on.

## 1.0.5

### Changed - log verbosity (pre-distribution pass)
- **Both `[Diagnostics]` config options now default to OFF** (`Enable Diagnostics`,
  `Enable Pouch Transfer Diagnostics`). They shipped ON in 1.0.4 to gather evidence for four
  still-unconfirmed Partner bugs, but measured against a real 2-boot `LogOutput.log` they were
  the single largest source of mod log output in the whole suite: 47 Info lines, of which the
  `FindCardToEquip` equip trace alone accounted for 33. The diagnostics themselves are unchanged
  and every one of their Info sites already honours the gate, so reproducing any of those four
  bugs is still one config flip away. **If you are reporting one of those bugs, turn the matching
  option back on and re-capture the log.**
- Note for existing installs: BepInEx keeps the value already written in
  `BepInEx/config/crispywhips.partner_overhaul.cfg`, so this new default only applies to fresh
  installs. Existing users who want the quieter log must set both options to `false` themselves.
- `[GameLoadPatch]` per-target duty attachment is now one aggregate Info line
  (`attached N duty ref(s) to vanilla actions`) instead of 11 individual lines. The per-target
  list is still available at Debug level.
- `[WoodReserveListPatch]`'s startup config echo now logs at Info only when the reserved list is
  NON-empty; the empty case (the shipped default, so the case every player hits) drops to Debug.
  This partially reverses 1.0.4's decision to always echo the count: that rationale
  (distinguishing "confirmed no-op by design" from "config never loaded") is preserved and is
  still observable by enabling Debug logging.
- `[WoundStacking]`'s per-suppressed-wound line drops to Debug; it fired once per suppressed
  wound during combat.

## 1.0.4

### Diagnostics -- no behavior change
- `WoodReserveListPatch` now emits one `[WoodReserveListPatch]` LogInfo line on its first
  evaluation each run stating the reserved fuel/wood UID count, even when it is zero (e.g.
  `Reserved fuel/wood UIDs: 0 (list empty; the reserve filter is a no-op)`). This is a second
  startup Info line beyond the mod's single `PartnerOverhaul vX loaded.` summary -- previously
  `RefreshReservedSetIfNeeded` only logged when the reserved set was non-empty, so a default
  (empty) config produced zero log output for this feature, indistinguishable from the config
  never having loaded at all. Config-echo diagnostic of the same class as the framework's own
  `LocalizationLoader: language=` line (see root CLAUDE.md's Debugging Discipline). Filter
  behavior (`FilterReservedItems`) is unchanged; only added Info logging.

## 1.0.3

### Fixed
- A Partner's Energy level is now visible on their inspection card. Vanilla's `Partner_Energy`
  NPCStat has fully-authored status text ("Looks exhausted." / "Looks very tired." / "Looks
  tired." / "Looks full of energy.") but ships every entry with `VisibleToPlayer`/`ShowInMainTab`
  false, unlike the sibling need stats (Satiation/BodyTemperature/Thirst/Wakefulness), which show
  theirs. `Patcher/GameLoadPatch.cs` `FixPartnerEnergyVisibility` (since 1.1.0 `FixPartnerStatVisibility`, which also covers Comfort) flips both flags on each entry
  at boot (`GameLoad.LoadMainGameData` postfix), matching the pattern already used by their
  siblings, no new text authored, nothing else about the stat's behavior changes.

## 1.0.2

### Diagnostics (opt-in, default ON): no behavior change
- Added `[PouchTransferDiagnostics]` LogInfo logging on the generic engine liquid-transfer clamp
  (`InGameCardBase.ClampLiquidTransferQuantity`), scoped to fire only when Pouch or AcornFlourRaw
  is one of the two cards involved, for the reported pouch/acorn-flour stacking math bug. New
  independent config toggle: "Enable Pouch Transfer Diagnostics" (`Patcher/PouchTransferDiagnosticsPatch.cs`).
  Diagnostic only, the underlying stacking bug is not fixed by this release.

## 1.0.1

### Added (opt-in, default OFF)
- "Consolidate Duplicate Wounds" config option, when enabled, a combat round will not apply a
  wound the player already has equipped (`Patcher/WoundStackingPatch.cs`, transpiler on
  `EncounterPopup.GenerateAndApplyPlayerWound`). Filters only the per-round wound-candidate list;
  damage resolution, stat changes, armor durability and the encounter log are unchanged.
  **Not yet verified in-game.**

## 1.0.0: Initial release

### Fixed
- Partners can now eat stew/broth (Eat/EatFree duty wired onto `OLD_LQ_StewWater`/`LQ_NightBroth`
  Drink actions; thirst still targets plain water only).
- Partners resume cleaning finished Cabin/MudHut/Enclosure/MudHutEnclosure/MudHutExpansionLeft
  homes (Clean duty wired onto the finished-home "Clean" action, matching the mid-construction
  variants' existing wiring).
- Partners stop endlessly re-watering an already-saturated garden plot (armed a Hydration-percent
  gate on GardenPlot's "Water" action).
- Partners can feed Pine Needles/Charcoal into a Fireplace (Firekeeping duty wired onto both
  actions).
- Removed a stray "Defecate" button from the "Tree Top" scenic event.
- NPC action sounds (eating, working) are no longer audible from a different environment than the
  player's current one.
- A Partner's dialog no longer interrupts an in-progress player action (e.g. a spirit-summon
  ritual).

### Added (opt-in, default OFF)
- "Reduce Partner Move Costs" config option, discounts inter-environment travel move-cost.
- "Reserved Fuel/Wood UIDs" config option, exclude specific items from Partner auto-fuel-feeding.

### Diagnostics (opt-in, default ON)
- LogInfo-level diagnostics for 4 still-unconfirmed bugs: fishing-line AI freeze, brain-tanning
  stuck-on-first-hide, clothes/carry-weight/temperature, cauldron ownership reset.
