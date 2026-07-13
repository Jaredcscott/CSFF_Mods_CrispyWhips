# Mod Update Manager — Changelog

All notable changes to this mod are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

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
