# Mod Update Manager — Changelog

All notable changes to this mod are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [2.1.55] - 2026-09-23

### Changed

- **Refreshed the embedded mod suite** (this entry also covers the unpublished 2.1.53 and 2.1.54
  repacks). Re-embeds Community Mod Chest 1.68.39. With it:
  - Trait conditions such as Nyctophobia and Nightcrawler's Sunlight Exposure now always match what
    a reload would show.
  - Nightcrawler's sun works again: it had been reading a base-game stat that stays at zero all day.
  - Spiritually Troubled and Harmonious follow chiweichiwei's design.
  - Abundant Growth no longer starts you with seeds.

  All nine suite mods were rebuilt against the base game's EA 0.68a update. Herbs and Fungi carries
  a repaired card image. The other suite mods are re-embedded at their current versions.

## [2.1.52] - 2026-09-22

### Changed

- **Refreshed the embedded mod suite.** Re-embeds CSFF Mod Framework 2.26.3 and Community Mod Chest
  1.68.38, both carrying new artwork by chiweichiwei: 37 perk icons and 16 trait-status icons that
  replace borrowed vanilla sprites, plus new Portal Kit and placed Portal art. Community Mod Chest
  1.68.38 also fixes a wedge at character creation for the Deadly Disease trait. The other seven
  suite mods are re-embedded unchanged.

## [2.1.51] - 2026-09-19

### Changed

- **Refreshed the embedded mod suite.** Re-embeds CSFF Mod Framework 2.26.2, Community Mod Chest
  1.68.36, Herbs & Fungi 1.13.5 and Skill Speed Boost 1.10.5 after their latest deploy. The other
  five suite mods are embedded at the same versions as 2.1.50. No change to Mod Update Manager's
  own behaviour.

---

## [2.1.50] - 2026-09-19

### Changed

- **Refreshed the embedded mod suite for the Nightcrawler trait.** Community Mod Chest 1.68.36 is
  re-embedded: a new character-creation trait built from chiweichiwei's Nightcrawler design,
  replacing the retired Sensitive Skin. The other eight suite mods are embedded at the same
  versions as 2.1.49. No change to Mod Update Manager's own behaviour.

---

## [2.1.49] - 2026-09-19

### Changed

- **Refreshed the embedded mod suite.** Re-embeds CSFF Mod Framework 2.26.2 (fixes actions
  permanently freezing with "I can't do two things at once..." right after an autosave),
  Community Mod Chest 1.68.35 (trait rework), Herbs & Fungi 1.13.5 and Skill Speed Boost 1.10.5,
  which had moved ahead of the 2.1.48 embed. The other five suite mods are embedded at the same
  versions as 2.1.48. No change to Mod Update Manager's own behaviour.

---

## [2.1.48] - 2026-09-18

### Changed

- **Refreshed the embedded mod suite with new card art.** Three mods are re-embedded:
  - **Community Mod Chest 1.68.32:** fifteen cards that were showing another object's picture now
    have their own.
  - **Advanced Copper Tools 1.16.8:** Salt-Cured Meat has its own picture.
  - **Herbs & Fungi 1.13.4:** Truffle Butter, Hemp Stalks and the Apothecary Shelf have their own
    pictures.

  The other six suite mods are embedded at the same versions as 2.1.47. No change to Mod Update
  Manager's own behaviour.

---

## [2.1.47] - 2026-09-18

### Changed

- **Refreshed the embedded mod suite.** Community Mod Chest 1.68.31 is re-embedded: the Miller's
  Inn, Academy and Cottage portraits now show the same man as his village portrait, each set in the
  room that location's card shows. The other eight suite mods are embedded at the same versions as
  2.1.46. No change to Mod Update Manager's own behaviour.

---

## [2.1.46] - 2026-09-18

### Changed

- **Refreshed the embedded mod suite.** Herbs and Fungi 1.13.3 is re-embedded with finished art for
  five cards that had been showing a placeholder, and Homestead Perks 1.2.4 now tags its perks
  "[HSP]" under CSFF Mod Framework 2.26.0's perk origin tag instead of falling back to "[HP]". The
  other seven suite mods are embedded at the same versions as 2.1.45. No change to Mod Update
  Manager's own behaviour.

---

## [2.1.45] - 2026-09-18

### Changed

- **Refreshed the embedded mod suite.** Advanced Copper Tools 1.16.7 and Water Driven
  Infrastructure 1.11.2 are re-embedded. Both releases only shrink their standalone downloads and
  correct their README version history; neither changes gameplay. The other seven suite mods are
  embedded at the same versions as 2.1.44. No change to Mod Update Manager's own behaviour.

---

## [2.1.44] - 2026-09-18

### Changed

- **Refreshed the embedded mod suite.** Community Mod Chest 1.68.30 is re-embedded with revised art
  for the Miller and Weaver at-home portraits and both Village Home Sign faces. Five of its images,
  and oversized art in Advanced Copper Tools and Water Driven Infrastructure, were also brought down
  to the 512-wide size every other card uses. Herbs and Fungi 1.13.2 picks up the Drying Kit perk's
  missing `EquippedCardsWarpType` key. The suite ZIPs were already packaged at reduced resolution,
  so this does not change Mod Update Manager's own download size. No change to its behaviour.

---

## [2.1.43] - 2026-09-17

### Changed

- **Refreshed the embedded mod suite.** Community Mod Chest 1.68.30 is re-embedded without the
  temporary trait diagnostics tracer, which only ran when a debug setting was switched on, so
  players see no difference. The other eight suite mods are embedded at the same versions as
  2.1.42. No change to Mod Update Manager's own behaviour.

---

## [2.1.42] - 2026-09-17

### Changed

- **Refreshed the embedded mod suite.** The Install & Update tab now installs CSFF Mod Framework
  2.26.0 (mod perks show which mod they come from, e.g. " [CMC]", at character creation and on
  the character sheet; NPC and animal schedules keyed to the hour of day now run at their intended
  clock times), Community Mod Chest 1.68.30 (Agoraphobia, Lunacy, Sensitive Skin, Insomniac,
  Drunkard, Lactose Intolerant and Seasonal Allergies now do what their descriptions say; Sinker
  and Swimmer affect swimming; the Aid perks hold their bonus), Herbs & Fungi 1.13.2 and Skill
  Speed Boost 1.10.4. No change to Mod Update Manager's own behaviour.

---

## [2.1.41] - 2026-09-15

### Fixed

- **`PluginMetadataReader`'s CustomAttribute-row parser could silently miss a real `[BepInPlugin]`/
  `[BepInDependency]` declaration with zero diagnostic trail.** A malformed metadata row hit a bare
  `catch { continue; }` (no log call), which read exactly like "this DLL doesn't declare that
  attribute" - indistinguishable from a genuine absence. Found by `/audit-mod`'s D17 check
  (silent-catch-on-a-reflection-path) against the Conflicts tab's Dependencies sub-section, added
  since the last code-quality pass. Now counts skipped rows and logs one aggregated
  `LogDebug` breadcrumb per scan (`"skipped N malformed CustomAttribute row(s) in '<dll>'"`) instead
  of staying silent - no behavior change, Debug-level only, invisible under BepInEx's default log
  filter. Audit re-run clean: 0 CRITICAL, 1 WARNING remaining (a cosmetic display-name fallback with
  no data-loss risk); both recurring-bug-class gates (`EmbeddedZipCurrency.Tests.ps1`,
  `Deploy-Mods.Tests.ps1`) re-verified green against the current 9-mod bundle.

---

## [2.1.40] - 2026-09-13

### Changed

- **Refreshed the embedded mod suite bundle so Community Mod Chest 1.68.24 includes the Town
  Achievement Board.** 2.1.39's copy of CMC 1.68.24 was packed before the board's return landed
  under that same version number (the known gap noted under 2.1.39). The board is back in the
  Village Inn with all eleven achievements, each marked unclaimed or earned and with a running
  count on the multi-part ones, and has not yet been played in-game. The Jail Cell and the Village
  Hall Boards room also gain the room descriptions they were missing. Bundled versions, read back
  from the embedded ZIPs: CSFF Mod Framework 2.25.32, Herbs & Fungi 1.13.0, Advanced Copper Tools
  1.16.6, Water-Driven Infrastructure 1.11.1, Community Mod Chest 1.68.24, Homestead Perks 1.2.3,
  Repeat Action 2.1.5, Quick Transfer 1.8.0 and Skill Speed Boost 1.10.2. No change to this mod's
  own code.

## [2.1.39] - 2026-09-12

*(Rollup entry, added 2026-09-13: 2.1.38 (2026-09-11) and 2.1.39 (2026-09-12) were embedded-suite
refreshes and version-string steps with no changes to this mod's own code. The bundled versions
below were read back from the 2.1.39 embedded ZIPs rather than inferred.)*

### Changed

- **Refreshed the embedded mod suite bundle.** Against 2.1.37: CSFF Mod Framework
  2.25.30 -> 2.25.32, Water-Driven Infrastructure 1.11.0 -> 1.11.1, and Community Mod Chest
  1.68.23 -> 1.68.24. The other six are unchanged: Herbs & Fungi 1.13.0, Advanced Copper Tools
  1.16.6, Homestead Perks 1.2.3, Repeat Action 2.1.5, Quick Transfer 1.8.0 and Skill Speed Boost
  1.10.2. The changes a player is most likely to notice: with WikiMod 3.5.1 installed on EA 0.67i,
  one outdated WikiMod patch no longer takes 23 of its other patches down with it, so its card
  stat tooltips should return (framework 2.25.31, not yet confirmed in-game); the suite writes far
  fewer lines to `LogOutput.log` at load (framework 2.25.32, Water-Driven Infrastructure 1.11.1);
  four Community Mod Chest cards that drew with no art, including the spring ford flood and the
  autumn deadfall blocking the road, now show their images; and two CMC recipe unlock hints that
  named a material the recipe never needed now name what it actually wants (CMC 1.68.24). Each
  mod's own CHANGELOG.md carries the full list.
- **Known gap in 2.1.39's copy of Community Mod Chest 1.68.24:** it was packed before the Town
  Achievement Board's return landed under that same version number, so the CMC inside 2.1.39 does
  not include the board. 2.1.40 carries it.

## [2.1.37] - 2026-09-10

*(Rollup entry, added 2026-09-11: 2.1.33, 2.1.34, 2.1.36 and 2.1.37 were embedded-suite refreshes
and version-string steps with no changes of their own, so every change below first shipped in
2.1.35 (2026-09-09). The bundled versions under "Changed" are the suite as it stands in 2.1.37,
read back from the embedded ZIPs rather than inferred.)*

### Added

- **Dependency validator in the Conflicts tab** (N6,
  `Documentation/Plans/Mod_Update_Manager/Audit_Remediation_Plan.md`). A new "Dependencies"
  sub-section flags any `[BepInDependency]` declared by an installed plugin whose target GUID is
  not among the installed `[BepInPlugin]` GUIDs. Hard dependencies are listed first and labelled
  distinctly from soft ones, because the consequences differ: BepInEx refuses to load a plugin
  whose hard dependency is missing, while a missing soft dependency only means the dependent
  loads without that integration. A declared minimum version is shown when the declaration
  carries one. The section renders nothing when every declared dependency resolves, and the whole
  sub-section is gated behind the existing `ShowConflictWarnings` setting like the rest of that
  tab.

  The declarations are read directly out of each DLL's **CLI metadata tables**
  (`PluginMetadataReader.cs`): a hand-written PE -> CLI header -> `#~` table walk that resolves
  `CustomAttribute` rows and decodes their `#Blob` fixed arguments. Nothing is loaded, executed,
  or locked - `Assembly.LoadFrom` on arbitrary third-party plugin DLLs would run their module
  initializers, pin the files for the process lifetime, and is exactly the stability regression a
  utility mod must not introduce; `System.Reflection.Metadata` is not available on net48 under
  the game's Mono runtime and shipping it was out of scope. Only the metadata region is buffered,
  never the whole file: the reference plugins tree holds 55.3 MB of DLL bytes but just 2.4 MB of
  metadata, most of the difference being this mod's own embedded suite ZIPs. Every failure mode
  is soft and per-DLL - a native, packed, or unreadable DLL logs at Debug and is skipped, and the
  scan can never throw into the tab.

  Two details that are easy to get wrong and are handled explicitly: a `[BepInDependency("guid")]`
  argument is **not** an IL string literal, so it lives in the `#Blob` heap as a UTF-8 SerString
  and not in the UTF-16-LE `#US` heap (byte-verified against `Herbs_And_Fungi.dll`: one UTF-8
  occurrence, zero UTF-16-LE); and `BepInDependency` has two constructors, so hard/soft is decided
  from the ctor's parameter KINDS rather than argument position - the `(string, string)` overload
  is a minimum-version form that BepInEx treats as a hard dependency, not a flags value.

  Measured against the live BepInEx tree with the built assembly: 37 DLLs read (25 plugins,
  12 core), 0 unreadable, 21 installed plugin GUIDs, 19 dependency declarations across 13 plugins,
  in ~180 ms for a cold scan; the result is cached and invalidated on mod rescan, never recomputed
  per frame. That scan found one genuinely unsatisfied declaration on the reference install:
  WikiMod declares a soft dependency on `ModCore`, but the installed Pikachu ModCore registers as
  `Pikachu.CSFF.ModCore`, so WikiMod's declaration never resolves and BepInEx does not order the
  two. In-game confirmation of the rendered sub-section is routed to `/playthrough-test-plan`.

- **"Re-check Rate-Limited" button** (N5, `Documentation/Plans/Mod_Update_Manager/Audit_Remediation_Plan.md`).
  A mod whose most recent Nexus check hit HTTP 429 is now marked with a new
  `InstalledModInfo.RateLimited` flag - set only on that specific outcome (never derived from
  the "Rate limited - try again later" display string), and cleared the moment that mod's next
  check succeeds. The My Mods tab now shows a banner with a count and a "Re-check Rate-Limited"
  button whenever any row carries the flag; clicking it re-runs the same staggered 0.5s-apart
  check used by "Check for Updates", but scoped to only the rate-limited subset - every other
  mod's last result is left untouched. `UpdateChecker`'s per-mod network loop was factored out
  into a shared `RunChecksCoroutine` so the full check and this filtered re-check can't drift
  apart. This is distinct from the separate Medium-Term "429 auto-backoff" idea, which is not
  built here and stays in `Documentation/Ideas/Mod_Update_Manager/IDEAS.md`. Forcing a real 429
  requires hammering the Nexus API and was not attempted; verification is that the button is
  correctly gated on the flag and re-runs only the flagged rows - routed to
  `/playthrough-test-plan` for an in-game pass.

- **Search filter persists across window close/reopen** (N4,
  `Documentation/Plans/Mod_Update_Manager/Audit_Remediation_Plan.md`). The My Mods tab's search
  filter is now saved to the existing mod-preferences file as a reserved `_searchFilter` key
  alongside the per-mod ignore/favorite/notes entries, and restored when the window is
  re-initialized - previously it was a session-only field, reset to empty every time the window
  was closed (F3) and reopened. Saved on every edit and on Clear, mirroring how favorites/notes
  already persist.

- **Suite-install completeness verification** (N2, `Documentation/Plans/Mod_Update_Manager/Audit_Remediation_Plan.md`).
  `ModSuiteExtractor.Extract` now walks the ZIP entry list a second time after extracting and
  confirms every non-directory entry actually exists on disk, instead of reporting success on
  the strength of the copy loop finishing with no exception thrown. A missing file logs each
  path at `LogWarning`, logs a summary at `LogError`, and returns `false` with an
  `[Install Incomplete]` status message and count. The Install & Update tab's per-mod badge now
  shows `[Install Incomplete]` (reusing the existing error label style) instead of silently
  reverting to `[Up to Date]` on the next version-status refresh - `SuiteVersionReader.RefreshAll`
  recomputes that badge from `ModInfo.json` alone, so completeness is now tracked as a separate
  `SuiteModEntry.LastInstallIncomplete` flag that refresh does not touch. The positive path (a
  clean install still shows the success badge) is unchanged.

- **Stale-framework banner** (N7, `Documentation/Plans/Mod_Update_Manager/Audit_Remediation_Plan.md`).
  The My Mods tab now renders a distinct banner at the top (alongside the existing rate-limited
  banner) when the installed CSFF Mod Framework (Nexus ID 30) is behind the newest version
  already known from the normal per-mod update check - no new network call. It names the
  installed and available versions and points the player at the "All"/"Updates Available"
  filters to update it. Uses `VersionComparer.NeedsUpdate` (never a string compare) and renders
  nothing when the framework is not installed, has not been checked yet, the last check failed,
  or it is already current. Localized key `Mod_Update_Manager_FrameworkStale` added to both
  `SimpEn.csv`/`SimpCn.csv` for parity, though (like the rest of this mod's OnGUI strings) the
  banner text itself is not yet routed through the game's localization dictionary.

### Changed

- **Refreshed the embedded mod suite bundle** to the fleet releases current as of 2026-09-10
  (bundled versions read back from the embedded ZIPs, not inferred): CSFF Mod Framework
  2.25.23 -> 2.25.30, Community Mod Chest 1.68.16 -> 1.68.23, Herbs & Fungi 1.10.16 -> 1.13.0,
  Advanced Copper Tools 1.16.3 -> 1.16.6, Water-Driven Infrastructure 1.10.19 -> 1.11.0,
  Quick Transfer 1.7.7 -> 1.8.0, Repeat Action 2.0.2 -> 2.1.5, and Skill Speed Boost
  1.9.7 -> 1.10.2. Homestead Perks is unchanged at 1.2.3. The changes a player is most likely
  to notice: a refused drag-and-drop no longer destroys the dragged card and pays nothing
  (framework 2.25.29), travel buttons that rendered and clicked but did nothing now work
  (2.25.30), installing Community Mod Chest no longer makes Advanced Copper Tools' Copper Sheet
  blueprint impossible to research and stall 21 downstream recipes (CMC 1.68.17), the Iron
  Fishing Rod no longer breaks on its first cast (CMC 1.68.18), and Herbs & Fungi ingredients
  now actually contribute flavour in stews, where 91 entries meant as "strong" had been inert
  (H&F 1.13.0). Quick Transfer, Repeat Action and Skill Speed Boost each gained a batch of
  opt-in settings. Each mod's own CHANGELOG.md carries the full list, including which entries
  its authors marked as not yet confirmed in-game.

### Fixed

- **Suite install no longer orphans a locked file silently** (`ModSuiteExtractor.cs`, finding G2
  of the 2026-09-07 `/code-quality` pass). The pre-extract wipe loop caught a failed
  `File.Delete` at `LogDebug`, which BepInEx suppresses by default, then continued and still
  reported the mod installed successfully - so a stale JSON left behind by a locked file could
  keep a live UniqueID in the plugin folder and produce the duplicate-UniqueID class of bug
  (blueprint research resets, crafted items rejected) with nothing in the log. The catch now
  logs at `LogWarning`, matching `SuiteVersionReader`'s handling of the same failure class in
  this mod. Cardinality is bounded by the file count of the one folder being wiped.
  Recorded here rather than in `.audit/` because this mod gitignores that folder.

### Documentation

- **Player-facing text corrections** (no code change), closing the three open audit Warnings
  plus the N3 row of `Documentation/Plans/Mod_Update_Manager/Audit_Remediation_Plan.md`:
  the `ModInfo.json` Description now names the Install & Update suite installer, which is the
  flagship feature and was absent from the player-facing blurb; `README.md`'s compatibility
  label moves off the stale EA 0.65 to EA 0.67i; `FEATURES_IMPLEMENTED.md` calls the bundle
  9 mods rather than 8 (`SuiteModRegistry.All` and `Pack-Suite.ps1` both list 9); and
  `README.md`'s feature list now enumerates the shipped four-tab layout
  (Install & Update / My Mods / Conflicts / Settings) instead of the pre-2.1.5 flat tab set.

## [2.1.32] — 2026-08-31

*(Rollup entry, added retroactively 2026-09-01: versions 2.1.25-2.1.31 (2026-08-24 to 2026-08-30)
shipped as suite-refresh and version-string steps with no changelog entries, under placeholder
commit messages. This entry covers the full jump from 2.1.24; the 2026-09-01 fleet feature audit
flagged the gap.)*

### Changed

- **Refreshed the embedded mod suite bundle** to the fleet releases current as of 2026-08-31
  (bundled versions read back from the embedded ZIPs, not inferred): Community Mod Chest
  1.68.1 -> 1.68.16 (NPC movement/scheduler fixes, notably partners no longer stranded when the
  player is indoors), CSFF Mod Framework 2.25.7 -> 2.25.23 (notably the `TryRemoveCard`
  coroutine-drive fix in 2.25.22 - reflection-resolved card removal previously succeeded silently
  without removing anything, affecting WorldMap live-trim, CMC tree respawn, and Sirus23 sheep-pen/
  wolf upkeep - plus WorldMap clone-environment drop trimming and travel catch-up batching),
  Advanced Copper Tools 1.16.1 -> 1.16.3 (encounter-path performance pass), and Herbs & Fungi
  1.10.14 -> 1.10.16 (forage-table double-counting fix + `ForageDropDensityScale` config). The
  other five bundled mods are unchanged from 2.1.24: Quick Transfer 1.7.7, Repeat Action 2.0.2,
  Skill Speed Boost 1.9.7, Water-Driven Infrastructure 1.10.19, Homestead Perks 1.2.3.

---

## [2.1.24] — 2026-08-24

### Changed

- **Refreshed the embedded mod suite bundle** to pick up two fleet releases that shipped after the 2.1.23 repackage: Community Mod Chest 1.68.1 (Quiet Village character perk — stands down the Watch and keeps NPCs home — plus an NPC scheduler performance pass) and CSFF Mod Framework 2.25.7 (fixes a Portal Hub travel map-overlap/board-corruption bug introduced upstream, adds the `Reflect.IsAlive` helper CMC's scheduler pass relies on). The other 7 bundled mods (Advanced Copper Tools, Herbs & Fungi, Quick Transfer, Repeat Action, Skill Speed Boost, Water-Driven Infrastructure, Homestead Perks) are unchanged from 2.1.23.

---

## [2.1.23] — 2026-08-23

### Changed

- **Repackaged the embedded mod suite bundle** used by the Install & Update tab — refreshed all 9 embedded suite ZIPs with each mod's latest release as of 2026-08-23: Advanced Copper Tools 1.16.1, Community Mod Chest 1.67.6, Herbs & Fungi 1.10.14, Quick Transfer 1.7.7, Repeat Action 2.0.2, Skill Speed Boost 1.9.7, Water-Driven Infrastructure 1.10.19, Homestead Perks 1.2.3, and CSFF Mod Framework 2.25.3 — replacing the versions bundled since 2.1.18/2.1.22. Embedded DLL payload grew from ~38.6 MB to ~39.6 MB.

---

## [2.1.22] — 2026-08-16

### Added

- **Homestead Perks joins the suite** — the Install & Update tab now lists a 9th installable/updatable mod alongside the existing 8: Homestead Perks (thirteen character-creation perks granting placeable structure kits — Homestead, Cabin, Mud Hut, Well, Cellar, Log Bed, Furnace, Forge, Oven, Rain Cistern, Tanning Pit, Pit Trap, and Path kits), registered in the Content category with its own embedded suite ZIP.

Versions 2.1.19–2.1.21 that preceded this were intermediate version-string-only steps with no independent content; this entry covers the full jump from 2.1.18.

---

## [2.1.18] — 2026-08-11

### Changed

- **Repackaged the embedded mod suite bundle** used by the Install & Update tab — refreshed all 8 embedded suite ZIPs with each mod's latest release as of 2026-08-09: Community Mod Chest 1.46.4 (new village WorldMap nodes), Advanced Copper Tools 1.15.7, Herbs & Fungi 1.10.9, Quick Transfer 1.7.6, Skill Speed Boost 1.9.6, Water-Driven Infrastructure 1.10.5, Repeat Action 2.0.1, and CSFF Mod Framework 2.21.3. Installing or updating via the suite tab no longer extracts stale copies (embedded DLL payload grew from ~35 MB to ~39.8 MB). This repackage shipped as 2.1.16; versions 2.1.17 and 2.1.18 that followed (2026-08-09 to 2026-08-11) are version-string-only rebuilds with no further bundle or code changes.

---

## [2.1.15] — 2026-08-07

### Fixed

- **Chinese localization never shipped.** The `.csproj` had no `Content Include` for `Localization/*.csv`, so `bin/Release/Localization/` was empty regardless of what existed in source — and the two Chinese strings that did exist were stranded in `SimpEn.csv`'s unused 3rd column (the loader reads `SimpEn.csv` only in English mode). Added the `Localization\*.csv` content item and created `Localization/SimpCn.csv` with both rows.

---

## [2.1.14] — 2026-08-04

### Fixed

- **Suite-ZIP version-read failures were logged at Debug level**, invisible under BepInEx's default log filter, even though the README's Troubleshooting section explicitly points users at `LogOutput.log` for this exact case ("Install & Update tab shows 'Unknown' status for every mod"). `SuiteVersionReader.cs` now logs these failures at Warning, so the documented troubleshooting step actually surfaces something.

### Changed

- `FEATURES_IMPLEMENTED.md` and `IMPLEMENTATION_SUMMARY.md` (internal dev docs) refreshed to reflect the Install & Update suite installer shipped in v2.1.5 — both had drifted stale and still described it as unshipped/planned.

---

## [2.1.9] — 2026-07-12

### Fixed

- **Embedded suite ZIPs were missing several content-folder types**, so mods installed via the "Install & Update" tab loaded incomplete. The packaging script (`Pack-Suite.ps1`) only copied `CardData`, `CharacterPerk`, `Localization`, `GameStat`, `WorldMap`, and `EncounterGuards`. It now copies the full set that the production deploy uses — adding `PerkGroup`, `ScriptableObject`, `Animals`, `NPCAgent`, `NPCStat`, `NPCDuty`, `Encounter`, `GameSourceModify`, and `Data`. This restores Water-Driven Infrastructure's mill-race map linkages (`Data/MillRaceMapEdges.json`) and any companion/NPC action data that the previous bundle silently dropped.
- Re-packaged all suite ZIPs against current mod builds, so the bundled CSFF Mod Framework now includes the latest companion-related fixes (e.g. the `NPCAgentActivationService` `UnityEngine.Object` guard). Installing the framework via MUM no longer breaks Sirus23 Mod Collection companions.

---

## [2.1.5] — 2026-07-12

### Added

- **Install & Update tab** (now the default tab on open) — one-click install or update of the entire crispywhips in-house mod suite (CSFFModFramework, Advanced Copper Tools, Herbs & Fungi, Water-Driven Infrastructure, Community Mod Chest, Repeat Action, Quick Transfer, Skill Speed Boost). Each mod's ZIP is embedded directly inside the MUM DLL — no internet connection or manual download required. Per-mod rows show installed vs. bundled version and a status badge (`[Up to Date]` / `[Update Available]` / `[Not Installed]`). **Select Out of Date / Not Installed** or **Select All** and then **Apply Updates** extracts chosen mods to `BepInEx/plugins/`, preserving the framework's `SpriteCache/`. A restart-required banner (with a one-click game-quit button) appears after applying. Sirus23 Mod Collection is not part of this bundle.
- `MiniZip.cs` — minimal custom ZIP reader for Mono compatibility. `System.IO.Compression` is unavailable in the game's runtime; suite ZIPs are packed with no-compression (`Stored` method) so this reader only needs to parse ZIP structure and copy bytes.
- `ModSuiteExtractor.cs` — extraction engine; reads entries from `MiniZip`, maps paths, and writes files to the plugins directory.
- `SuiteModRegistry.cs` — registry of mods in the bundle: folder name, display name, bundled-resource key, Nexus ID.
- `SuiteVersionReader.cs` — reads version metadata from the `ModInfo.json` embedded inside each suite ZIP so the UI can compare installed vs. bundled versions without extracting.

### Changed

- **Tab layout restructured**: "Install & Update" is now tab 0 (default). "My Mods" tab gains an inline filter toolbar — All / Updates Available / Up to Date / Unmapped — replacing the old separate tabs for each filter state. Analytics moved into the Settings tab.
- Startup log is now silent unless an error occurs; reduced Info noise on clean loads.

---

*Previous release: v2.1.1 (2026-06-23)*
