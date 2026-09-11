# Skill Speed Boost — Changelog

All notable changes to this mod are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

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

## [1.9.7] — 2026-08-24

### Changed

- **Removed the temporary `[MorningBonus][DIAG]` diagnostic logging added in v1.9.6.** The
  capture window for the player-reported vanilla bug (a large stat penalty, e.g. the -150
  Stealth hit from an extinguished campfire, allegedly misapplied as a positive XP gain) closed
  without a reproduction against this mod's code path. No behavior change from this mod either
  way — the diagnostics were informational only, and removing them restores the pre-1.9.6 log
  output.
- **Silent-catch reflection breadcrumbs upgraded `LogDebug` → `LogWarning`** in
  `MorningBonusPatch` (`SetCurrentValue`, `IsMorningWindow`) and `AreaFamiliarityPatch`
  (`TryGetLocationUid`). BepInEx's default filter suppresses `LogDebug`, so a future
  field-rename that silently breaks bonus application or area-familiarity tracking is now
  actually visible in `LogOutput.log` by default instead of invisible.

## [1.9.6] — 2026-08-09

### Diagnostic

- Added temporary `LogInfo` diagnostics to `MorningBonusPatch.ChangeStat_Post` to investigate a player-reported vanilla bug (EA 0.66a+) where a large stat penalty (e.g. the -150 Stealth hit from an extinguished campfire) is allegedly misapplied as a positive XP gain. Logs the raw before/after/delta on any skill-stat change of magnitude ≥50 before this mod's XP-bonus math runs, and separately logs if a bonus is then compounded on top of it. No behavior change — informational only, to confirm or rule out this mod as a contributor before deciding on a fix. Will be removed/demoted once the report is resolved.

---

## [1.9.5] — 2026-07-16

### Fixed

- **Morning Bonus window offset** — `IsMorningWindow` computed "hours since day start" instead of the actual in-game clock hour, omitting `DaySettings.DayStartingHour`. With the vanilla day starting at 04:00, the default `MorningStartHour=5`/`MorningEndHour=9` window was firing 09:00–13:00 instead of the advertised early-morning hours, and the config text's "12 = midday" anchor was wrong. `IsMorningWindow` now reads `DaySettings.DayStartingHour`/`DailyPoints` live and matches `GameManager.HourOfTheDayValue`'s conversion, so `MorningStartHour`/`MorningEndHour` are true clock hours (0 = midnight, 12 = noon).

---

## [1.9.3] — 2026-07-12

### Changed

- All local reflection replaced with CSFFModFramework `Api.Reflect`, `Api.StatAccess`, and `Util.StatAccess` utilities. CSFFModFramework is now declared a `SoftDependency`.
- `MorningBonusPatch` now reads and writes stat values via `StatAccess.GetCurrentValue` / `StatAccess.GetMaxValue` with explicit NaN-to-fallback translation (NaN → 0f for current value; NaN → `float.MaxValue` for max), preserving the safe-fallback semantics of the prior reflection code.
- Skill-staleness modification (removing novelty/staleness on configured skills) now uses `Reflect.SetMember` / `Reflect.GetBool` instead of manual `FieldInfo.SetValue` calls.

### Fixed

- **Area familiarity patch** — now correctly filters to CT2 (placed structure) cards before granting familiarity XP. The prior version did not filter by `CardType`, granting spurious XP from non-structure cards in some cases.

---

*Previous release: v1.9.1 (2026-06-23)*
