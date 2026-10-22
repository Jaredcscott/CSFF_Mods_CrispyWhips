# Skill Speed Boost: Changelog

All notable changes to this mod are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [1.11.2] - 2026-10-22

### Fixed

- **Modders:** when clearing the EA 0.69 skill cache fails, the log now names the real error instead of the reflection wrapper's "Exception has been thrown by the target of an invocation".

## [1.11.1] - 2026-10-12

1.11.0 was never released on Nexus, so its notes below ship in this release too.

### Fixed

- **Skill gates see the boosted level straight away on EA 0.69.** EA 0.69 caches each skill's value for the checks that unlock actions, dialog and blueprints. A check made mid-gain could keep the old level until the skill next changed. The mod now refreshes that cache whenever it adjusts a skill.
- **A gain the low-condition penalty stops completely no longer uses up your daily bonuses.** With `LowConditionMultiplier` at 0, a blocked gain still spent the skill's first use of the day, day-streak day and rested-bank XP. Those are now kept for a gain that counts.
- **The XP log options keep the mod running.** With every XP bonus off but `LogSkillXpGains` or `LogDailyXpSummary` on, the mod stopped early and logged nothing.
- **One failed patch no longer stops the rest.** The startup line says how many applied. The framework stays optional: the mod uses it when it is installed.
- **A bonus can no longer push a skill past its maximum when the maximum cannot be read.** If a game update hides a skill's maximum, that skill keeps the game's own gain and the log says why once.
- **Skills from other mods keep their per-skill settings when their name has an apostrophe, quote, bracket or equals sign.** BepInEx refuses those characters in a setting name, so such skills silently lost their staleness and multiplier settings. The setting name now leaves them out.
- **A structure bonus can no longer carry into a new run.** Quitting mid-action at a structure could apply that structure's familiarity and station bonus to a later gain somewhere else.
- **Modders:** the morning and night windows now read the framework's shared clock.

## [1.11.0] - 2026-09-27

Eleven optional bonuses, optional XP overflow and XP readout, synergy tuning and groups, and a daily XP summary log. Every new setting defaults to off, empty or the old built-in value, so an existing config behaves as on 1.10.6. One exception: with skill synergies on and Community Mod Chest installed, its Melee Fighting skill now counts in the Combat group. Empty `SynergyGroupOverrides` to keep the old groups.

### Added

- **Night-owl bonus.** `NightBonusEnabled` gives `NightBonusMultiplier` (1.5) from `NightStartHour` (22) to `NightEndHour` (2), wrapping past midnight. If it overlaps the morning window, an hour inside both gets both.
- **Station bonus.** `StationBonusMap` (empty by default) gives extra XP at structures you name, as `id=multiplier` pairs such as `forge_id=1.25;desk_id=1.1`. It counts while you work at the structure, works for any mod's structures and does not need area familiarity.
- **Familiarity fade.** `AreaFamiliarityDecayEnabled` fades a location's bonus after `AreaFamiliarityDecayAfterDays` (14) days away, by `AreaFamiliarityDecayPerDay` (1) actions a day. Your recorded visits are never overwritten, so working there again, or turning the fade off, restores the full bonus. Rolling back to 1.10.6 loses nothing.
- **Day streak.** `StreakBonusEnabled` adds `StreakBonusPerDay` (+2%) for each consecutive in-game day with skill XP, up to `StreakBonusMaxDays` (10), so +20% at the defaults. A day without XP, loading an older save or a new character restarts it.
- **Season bonus.** `SeasonBonusMap` gives faster or slower XP in the seasons you name, such as `Winter:Woodworking=1.25;Summer=1.1`. Leave the skill out, or write `*`, for every skill. A skill's own entry beats the every-skill entry, and values below 1 slow it.
- **Environment bonus.** `EnvironmentBonusMap` does the same for kinds of place, such as `Env_Cave*:Knapping=1.2;Env_Grove*=1.1`. It does not grow with visits.
- **Tool and action bonuses.** `ToolBonusMap` and `ActionBonusMap` give faster or slower XP with the tools and from the actions you name, such as `tag_Axe=1.1` or `Grind=1.2`. A tool is the card a drag-and-drop action uses, so grinding Acorns counts the mortar.
- **Perk bonus.** `PerkBonusEnabled` turns on `PerkBonusMap`, faster or slower XP while you hold a perk you name, whether picked at character creation or granted during the run. It ships with Community Mod Chest's seven Academy graduate perks at 1.25 each, which do nothing without that mod. When several held perks name one skill, the highest applies.
- **In the Flow bonus.** `FocusBonusEnabled` multiplies XP by `FocusBonusMultiplier` (1.2) while your Focus is in its top band (72 and up on the current game). Anything that raises Focus becomes a learning aid, such as Advanced Copper Tools' Focus Tea, Herbs & Fungi's Lion's Mane Broth or Community Mod Chest's inn cat.
- **Morning and night skill lists.** `MorningBonusSkills` and `NightBonusSkills` limit those bonuses to the skills you list. Empty means every skill.
- **Rested-XP bank.** `RestedBankEnabled` banks `RestedBankGainsPerDay` (3) gains for each full day a skill goes unused, up to `RestedBankMaxGains` (10). Each gain spends one and is multiplied by `RestedBankMultiplier` (2.0). A skill's first gain ever banks nothing, and neither does loading an earlier save.
- **Milestone burst.** `MilestoneBonusEnabled` adds `MilestoneBonusXp` (1) skill point the first time a skill reaches each `MilestoneEvery` (25) breakpoint, once per character. Nothing scales it and `MaxComposedMultiplier` does not cap it. It never passes a skill's maximum.
- **XP overflow.** `XpOverflowEnabled` sends `XpOverflowFraction` (0.5) of the XP a maxed skill cannot take to the least trained related skill below its maximum. Only the receiver's own multiplier applies.
- **XP readout.** `XpReadoutEnabled` shows a short message near the time wheel naming the bonuses that applied, such as `Skill XP x1.50: morning, well rested`. It appears when the bonuses change and again after `XpReadoutMinSeconds` (60). It changes no XP.
- **Partner skills follow your settings.** `PartnerSkillsFollowSettings` gives the Partner companion's skills your staleness settings and your XP multiplier. Situational bonuses never apply to the Partner. Combat Experience is not a skill this mod tracks, so it keeps the game's rules.
- **Per-save progression.** `PerSaveProgression` keeps area familiarity, its fade, the day streak and the rested bank separately for each save slot. The first save you load with it on gets a copy of the shared files, and new games start empty. `PerSaveResetOnNewGame` (default on) clears what a deleted game left in a slot. The shared files are never changed, so turning it off goes back to them.
- **Synergy tuning.** `SynergyBonusPerAction` (0.1), `SynergyMaxCombo` (5) and `SynergyComboTimeoutMinutes` (5) are now settings, and `SynergyTimeoutUnit` counts the timeout in `RealMinutes` (default) or `GameMinutes`. The in-game clock moves in 15-minute steps, so use 30 or more.
- **Synergy that needs a change of skill.** `SynergyRequireDifferentSkill` grows a combo only when you switch to a different skill of the group. Repeating one skill, by hand or through RepeatAction, holds the combo.
- **Synergy groups.** `SynergyGroupOverrides` adds, moves or removes a skill's groups, as `Skill Name=GroupA,GroupB`. The default puts Community Mod Chest's Melee Fighting in Combat.
- **Daily XP summary.** `LogDailyXpSummary` writes one line per skill per in-game day: its gains, the game's total, the total after this mod and the average multiplier.
- **`LogSkillXpGains` shows more.** Each `[XpGain]` line now ends with the structure, environment, action and tool, which are the values to copy into the bonus maps.

### Changed

- **Presets.** Casual and Grinder now also turn on the night bonus, streak, rested bank, milestone burst and XP overflow. Immersive turns on the streak. Casual, Grinder and Immersive turn on the perk bonus and In the Flow bonus. Every preset writes `SynergyRequireDifferentSkill` off. A preset you chose earlier is not re-applied: choose it again, or clear `AppliedProfile`.
- The mod now notes which structure you work at whenever area familiarity, the station bonus or `LogSkillXpGains` is on.

### Fixed

- **Staleness settings apply without leaving the game.** Changing `EnableSkillStaleness`, a `<Skill>_UseStaleness` or a `<Skill>_StalenessMultiplier` while playing, or choosing a preset, now applies to every skill at once. The settings are also applied again at the start of every run. Before, an in-game change did nothing until a restart.
- **Turning staleness back on restores each skill's own values.** The mod records them before it first changes them. If another mod changes them afterwards (WikiMod's staleness setting does), its value is the one restored.
- **The docs no longer say loading a save applies a setting.** Loading a save never re-read the config: BepInEx reads the `.cfg` file once, at startup. An in-game change applies at once, and a file edit needs a full restart.
- **A perk granted during a run is no longer multiplied by your XP settings.** 1.10.6 fixed the perks a run starts with. A perk granted later, such as the Sirus23 collection's Pack Bond, still counted as XP: at a multiplier of 8, a perk's +75 Woodworking took the skill from 0 to its maximum of 150.

## [1.10.6] - 2026-09-25

### Fixed

- **A perk's starting skill bonus is no longer multiplied by your XP settings.** When a new run
  starts, the game hands out every perk's starting stats the same way it records practice, so the
  mod treated a perk's "+75 Herbalism" as 75 XP earned and multiplied it: with a multiplier of 8 the
  perk started you at 150 instead of 75. The mod now leaves the game's own start-of-run setup alone
  and only scales skill gains made while you play.

## [1.10.5] - 2026-09-19

### Fixed

- **Your XP settings no longer touch Arousal.** The game's EA 0.68 update added an Arousal stat
  that uses the same staleness flag as a skill, so the mod counted it as a 33rd vanilla skill. Every
  XP setting then applied to it: a global multiplier of 3 made arousal build three times as fast,
  and the mod's staleness settings were written onto it. The mod now leaves Arousal alone, as it
  already did for Stress, Morale, Focus and the other mental stats. If your config gained
  `Arousal_Multiplier`, `Arousal_UseStaleness` and `Arousal_StalenessMultiplier` entries under
  1.10.4, they now do nothing and can be deleted.

---

## [1.10.4] - 2026-09-14

### Fixed

- **Your XP settings now apply while a status is holding a skill down.** Some statuses lower a
  skill for as long as they last. The clearest case is Stealth: noisy work such as chopping wood
  or shovelling snow brings up "Animals noticed your Actions", which takes 75 off Stealth. Below 75
  trained Stealth that shows your Stealth as 0, and while it did, XP you earned (from a hunt, say)
  still reached the skill but the mod could not see it, so none of your XP settings applied to it.
  The mod now reads the skill you have actually trained, underneath any temporary effect, so the
  global and per-skill multipliers and every bonus apply as configured.
- **A per-skill multiplier of `0` now also stops XP earned under such a status.** Before, a skill
  set to `0` could still level while a status was holding it down.
- **Level scaling now uses your trained level** rather than the lowered value a status shows.
- No save-compatibility impact.

### Added

- **`LogSkillXpGains`** (Diagnostics, default off). Writes one line to `LogOutput.log` for every
  skill XP gain the mod handles: the gain the game applied, the combined XP multiplier, the gain
  after it, and the skill's trained value before and after. A gain as small as +0.5 cannot be read
  off the skill bar; this shows it. It logs on every gain, so turn it off again when you are done.

---

## [1.10.3] - 2026-09-14

### Fixed

- **A campfire going out no longer raises your Stealth.** A lit campfire lowers Stealth by 150
  while you are at camp. When it went out, the mod read those 150 points coming back as Stealth
  XP and added its XP bonus on top, so Stealth ended up higher than you had trained it, and each
  campfire that burned out pushed it further. Quitting and reloading put it back. It happened only
  with at least 1 point in Stealth and some setting that multiplies XP, and area familiarity is one
  of those and is on by default. The same applied to any temporary effect on a skill ending: a
  card's passive effect, a status, a time-of-day effect, or an action's temporary modifier. The mod
  now multiplies only real XP gains and leaves those effects to the game.
- **Reported on GitHub after EA 0.66a.** 1.9.6 added logging to investigate it and 1.9.7 removed
  that logging without finding the cause. The cause was in this mod, not in the base game.
- **A skill with a per-skill multiplier of `0` no longer keeps part of an expired effect.** That
  setting undoes XP gains, and it was undoing the returning 150 Stealth too, leaving the campfire
  penalty partly in place after the fire was out.
- No save-compatibility impact. The extra points were never written into your saved skill value,
  which is why a reload always cleared them.

---

## [1.10.2] - 2026-09-07

One new opt-in feature, off by default: the low-condition XP penalty, the last Near-Term item the
1.10.0 batch skipped. Nothing changes for an existing config until you turn it on.

### Added

- **A low-condition XP penalty.** `LowConditionPenaltyEnabled` with `LowConditionThreshold`
  (default `60`) and `LowConditionMultiplier` (default `0.5`) multiply skill XP by a sub-1.0 factor
  while ANY of Energy, Satiation or Hydration is below the threshold percentage of its current
  maximum. It is the exact inverse of the well-rested bonus at the same threshold: at or above 60%
  on all three earns 1.25x if that bonus is on, below 60% on any one costs you half, and there is
  no gap between them. It composes with every other multiplier rather than replacing them, so a 3x
  global and a 0.5x penalty award 1.5x. `1.0` disables it; `0` means no XP at all while a stat is
  low. **Both numbers are TUNABLE starting points, not a balance verdict** - they are the values
  the plan specified, not values that have been played to.
  It reuses the same cached, live-maximum-aware stat reads as the well-rested bonus, so running
  both costs nothing extra on the XP path, and it fails closed in the player's favour: if a stat
  cannot be read no penalty is applied, and one `[LowCondition]` warning per session names the
  lookup that failed.

### Fixed

- **XP settings no longer reach your mental stats.** Every multiplier this mod applies, the new
  penalty included, ran on Morale, Stress, Focus, Loneliness, Gratification, Altered Mindstate,
  Mental Structure, Thought Depth and Profile as well as on skills. The mod has always known
  those nine are not skills and skips them when it configures staleness, but the runtime check
  fell back to the same `UsesNovelty` flag vanilla sets on all of them, and that flag is not a
  skill test: on the EA 0.67i data 41 vanilla stats set it and 10 are not skills. Because the
  nine are deliberately left out of the skill list, the fallback was exactly what decided their
  case, and it said yes. **This was live in a stock config**, since area familiarity defaults to
  on: a Morale gain was scaled by your familiarity bonus at that location and also counted as a
  visit toward it. Both skill tests now read one shared exclusion list. Found while checking
  whether vanilla already had a low-condition XP mechanism of its own (it does not: the only
  multiplier vanilla applies to a skill gain is staleness).

### Changed

- **`Hardcore`, `Legacy` and `Immersive` now switch the penalty ON.** Those three presets could
  previously only turn bonuses off; this is the first thing they have to turn on. **Your existing
  config is not affected by upgrading:** since 1.10.1 a preset is applied once and `AppliedProfile`
  already records yours, so nothing is re-written until you pick a preset again or clear that key.
  The `ActiveProfile` description, the README preset example and the FEATURES preset grid all say
  so - a preset now writes nine keys rather than eight.
- **The composed multiplier is applied in both directions.** The XP postfix stopped as soon as the
  composed total was not greater than 1, and wrote the stat only when the new value was higher.
  Both were correct while every term could only raise the total, and both would have discarded
  this penalty in full, silently, with no log line and every other check green. A total below 1 is
  now written too, and only a total of exactly 1 counts as no change. **No existing behaviour
  moves:** none of the seven older terms can produce a factor below 1 (the familiarity multiplier
  is `1 + bonus`, and the synergy and level-scaling terms are already gated above 1).

### Notes

- The composition gate also grew a fifth invariant, so the two skill tests cannot drift apart
  again: the runtime one must consult the load-time exclusion list, and that list must stay
  populated (an emptied list would leave the first check true and meaningless). Both were
  watched red on a fixture copy.
- The composition gate grew a fourth invariant for exactly the trap above: it now fails if the
  method regains an early-out on a not-above-1 multiplier, or a write gated on "higher than
  before", and it requires the tolerance comparison that replaced them. All three were watched RED
  on a scratchpad fixture copy before going green, alongside a fourth break confirming the existing
  guard check covers the new flag. Gate now 32/32.
- Not yet confirmed in-game: that the penalty reduces a real XP gain by the configured factor, and
  that the default config is unchanged against 1.10.1. Both are filed as playthrough items T1.78
  and T1.79.

---

## [1.10.1] - 2026-09-06

Audit remediation. One player-facing behaviour fix, one diagnostic gap closed, and a docs pass.
No new features, no new config defaults to worry about: an existing config behaves as it did on
1.10.0 apart from the profile fix below, which is the point of the release.

### Fixed

- **A difficulty preset no longer overwrites your individual settings on every launch.** Choosing
  a preset writes eight keys. Until now the mod re-asserted the chosen preset at every start, so an
  individual toggle you changed afterwards survived until the next launch and no further - while
  both README and FEATURES told you to make exactly that edit. A preset is now written ONCE, the
  first time the game starts after you choose it (or immediately if you change it while running),
  and then left alone. A new `AppliedProfile` bookkeeping key under `[DifficultyProfiles]` records
  which preset was written; clear it to apply your chosen preset fresh.
- **The well-rested bonus now says why it is not applying.** `PlayerConditionService` promised a
  once-per-session warning on a read failure, but every realistic way for it to break returned
  quietly without throwing, so the warning could never fire and the bonus simply never applied -
  indistinguishable from "you never met the threshold". All five of those paths, plus an unreadable
  stat value, now emit one warning each naming the exact lookup that failed. Being below the
  threshold is still silent, because that is the normal answer.

### Changed

- **The `ActiveProfile` config description now matches what a preset actually does.** It previously
  listed six of the eight keys a preset writes (omitting `DailyFirstUseBonusEnabled` and
  `WellRestedBonusEnabled`), described Immersive without its daily-first-use bonus, and called
  Hardcore and Legacy "all bonuses off" when neither touches your per-skill multipliers. The text is
  now accurate on all three points, and the list of valid preset names is generated from the presets
  themselves so it cannot drift again.
- **Docs corrected.** `FEATURES.md` documented a `Blade_Multiplier` key that does not exist (there
  is no Blade skill; the melee skills are `Knife Fighting`, `Axe Fighting`, `ClubFighting` and
  `Spear Fighting`), illustrated skill synergies with two skills that do not exist ("Foraging" and
  "Herbal"; the real chain used is Herbalism to Butchering to Cooking, which share the Survival
  group), listed nine of the thirteen shipped features, and carried a configuration sample missing
  every key added since v1.7. `README.md` spelled `ClubFighting` with a space, which is not the
  config key, and still described the mod as being for EA 0.65. All corrected.
- **The performance note is now precise.** Both docs said the XP postfix "fires only on skill XP
  gains". It RUNS on every stat change and does its work only on skill XP gains - and because
  `EnablePerSkillMultipliers` defaults to true, its early-out is not reached under stock settings.

### Notes

- The test gate that protects the single XP composition point was hardened. It enforced its rule for
  one way of writing a bonus flag; two other realistic ways of adding a bonus passed it while leaving
  the bonus silently dead. Both now fail the gate, demonstrated by breaking a copy of the source and
  watching each break go red for the right reason.

---

## [1.10.0] - 2026-09-05

Seven tuning features, every one of them off (or set to a no-op value) by default. An existing
config file behaves exactly as it did on 1.9.7 until you change something.

### Added

- **A starting bonus for unfamiliar places.** `AreaFamiliarityMinBonus` (default `0`) sets what a
  location is worth before you have ever worked it. At the default of 0 nothing changes: a fresh
  location still gives no bonus and still ramps to `AreaFamiliarityMaxBonus` with use. Set it to
  0.10 and every location starts at +10% instead, still ramping to the same maximum. Min is
  clamped to Max first, so a config where Min is larger cannot make a strange place better than
  a familiar one.
- **A first-use-of-the-day bonus.** `DailyFirstUseBonusEnabled` with `DailyFirstUseMultiplier`
  (default `1.5x`) multiplies the first XP each skill earns on each in-game day. It rewards a day
  spent across several crafts over a day spent grinding one. A skill you have set to 0x never
  spends its daily claim.
- **A well-rested bonus.** `WellRestedBonusEnabled` with `WellRestedMultiplier` (default `1.25x`)
  and `WellRestedThreshold` (default `60`) apply while Energy, Satiation and Hydration are ALL at
  or above that percentage of their current maximum. It reads the live maximum, so a perk that
  raises a cap is respected. If any of the three cannot be read the bonus simply does not apply.
- **A cap on the combined multiplier.** `MaxComposedMultiplier` (default `0`, meaning uncapped)
  limits the total after every bonus has stacked. These bonuses multiply, so a few reasonable
  settings can combine into a number none of them suggests on its own: 10x global with the
  morning, familiarity, synergy and level-scaling bonuses all live is already past 65x. Set a
  ceiling and that is the most any single gain can be worth. `LogMultiplierCapHits` shows when it
  actually bites.
- **A choice of level-scaling curve.** `LevelScalingCurve` accepts `Linear` (the previous and
  still default behaviour), `EaseIn` (little help until a skill is well along, then steep, so it
  pays to push a skill toward its ceiling) and `EaseOut` (steep early then flat, so most of the
  help lands on skills you have barely started).
- **An effective-settings log.** `LogEffectiveSettings` (default off) writes one line per skill
  after load showing the XP multiplier that actually resolved and whether it came from a per-skill
  override or the global setting, plus the staleness state, decay rate and resulting cooldown, on
  top of two header lines covering every global toggle. This is the first thing to turn on when a
  setting does not look like it is taking effect: it reports what the mod resolved rather than
  what the file asked for.
- **An Immersive difficulty preset.** 1x XP with staleness on, no raw multiplier at all, and only
  the bonuses that come from something happening in the world: area familiarity, the morning
  window, first use of the day, and being rested and fed.

### Changed

- **Difficulty profiles now set the whole stack, not half of it.** A preset used to write only
  `SkillExpMultiplier` and `EnableSkillStaleness`, which meant every feature added after v1.7
  silently kept whatever value it already had. Picking "Hardcore" could still leave three bonuses
  running. Each preset now writes all six feature toggles explicitly, including the ones it turns
  off, so the preset you choose is the preset you get. The full grid of what each preset sets is
  in FEATURES.md.
  - Consequence worth knowing: because a profile writes those keys into your config file, set an
    individual key AFTER choosing a profile if you want to differ from it, not before.

### Notes

- **One diagnostic gap closed on the way past.** The write path had a last silent exit: if the mod
  could not resolve any writable stat field it returned quietly, so every bonus would stop applying
  with nothing in the log to say why. It now logs one warning naming the likely cause, a game-update
  field rename. Log output only, no behaviour change.
- No save-compatibility impact: nothing here touches saved data. The daily first-use ledger lives
  in memory and re-arms on load.
- The well-rested check is cached for one real-time second, so adding it costs nothing measurable
  on the XP path even though it reads three stats.

---

## [1.9.7] - 2026-08-24

### Changed

- **Removed the temporary `[MorningBonus][DIAG]` diagnostic logging added in v1.9.6.** The
  capture window for the player-reported vanilla bug (a large stat penalty, e.g. the -150
  Stealth hit from an extinguished campfire, allegedly misapplied as a positive XP gain) closed
  without a reproduction against this mod's code path. No behavior change from this mod either
  way: the diagnostics were informational only, and removing them restores the pre-1.9.6 log
  output.
- **Silent-catch reflection breadcrumbs upgraded `LogDebug` → `LogWarning`** in
  `MorningBonusPatch` (`SetCurrentValue`, `IsMorningWindow`) and `AreaFamiliarityPatch`
  (`TryGetLocationUid`). BepInEx's default filter suppresses `LogDebug`, so a future
  field-rename that silently breaks bonus application or area-familiarity tracking is now
  actually visible in `LogOutput.log` by default instead of invisible.

## [1.9.6] - 2026-08-09

### Diagnostic

- Added temporary `LogInfo` diagnostics to `MorningBonusPatch.ChangeStat_Post` to investigate a player-reported vanilla bug (EA 0.66a+) where a large stat penalty (e.g. the -150 Stealth hit from an extinguished campfire) is allegedly misapplied as a positive XP gain. Logs the raw before/after/delta on any skill-stat change of magnitude ≥50 before this mod's XP-bonus math runs, and separately logs if a bonus is then compounded on top of it. No behavior change: informational only, to confirm or rule out this mod as a contributor before deciding on a fix. Will be removed/demoted once the report is resolved.

---

## [1.9.5] - 2026-07-16

### Fixed

- **Morning Bonus window offset**: `IsMorningWindow` computed "hours since day start" instead of the actual in-game clock hour, omitting `DaySettings.DayStartingHour`. With the vanilla day starting at 04:00, the default `MorningStartHour=5`/`MorningEndHour=9` window was firing 09:00–13:00 instead of the advertised early-morning hours, and the config text's "12 = midday" anchor was wrong. `IsMorningWindow` now reads `DaySettings.DayStartingHour`/`DailyPoints` live and matches `GameManager.HourOfTheDayValue`'s conversion, so `MorningStartHour`/`MorningEndHour` are true clock hours (0 = midnight, 12 = noon).

---

## [1.9.3] - 2026-07-12

### Changed

- All local reflection replaced with CSFFModFramework `Api.Reflect`, `Api.StatAccess`, and `Util.StatAccess` utilities. CSFFModFramework is now declared a `SoftDependency`.
- `MorningBonusPatch` now reads and writes stat values via `StatAccess.GetCurrentValue` / `StatAccess.GetMaxValue` with explicit NaN-to-fallback translation (NaN → 0f for current value; NaN → `float.MaxValue` for max), preserving the safe-fallback semantics of the prior reflection code.
- Skill-staleness modification (removing novelty/staleness on configured skills) now uses `Reflect.SetMember` / `Reflect.GetBool` instead of manual `FieldInfo.SetValue` calls.

### Fixed

- **Area familiarity patch**: now correctly filters to CT2 (placed structure) cards before granting familiarity XP. The prior version did not filter by `CardType`, granting spurious XP from non-structure cards in some cases.

---

*Previous release: v1.9.1 (2026-06-23)*
