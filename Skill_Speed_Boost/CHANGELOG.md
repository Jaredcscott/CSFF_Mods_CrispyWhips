# Skill Speed Boost — Changelog

All notable changes to this mod are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

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
