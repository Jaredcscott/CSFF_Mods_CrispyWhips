# Mod Update Manager — Changelog

All notable changes to this mod are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

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
