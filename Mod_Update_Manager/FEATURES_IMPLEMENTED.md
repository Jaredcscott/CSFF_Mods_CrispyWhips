# Mod Update Manager - Feature Status

Date: 2026-08-04

This document describes what is actually wired in the shipped mod. Planned features live under `Documentation/Ideas/Mod_Update_Manager/`.

## Shipped

- **Crispywhips Mod Suite installer** (the "Install & Update" tab, default on open, shipped v2.1.5) — one-click install/update of the 9-mod crispywhips family from ZIPs embedded directly in the MUM DLL (`ModSuiteExtractor.cs`, `SuiteVersionReader.cs`, `MiniZip.cs`, `SuiteModRegistry.cs`). Batch selection ("Select Out of Date / Not Installed" / "Select All") + "Apply Updates" extracts chosen mods straight into `BepInEx/plugins/`, preserving the framework's `SpriteCache/`. Works fully offline; no Nexus API key needed for this tab.
- Installed-mod scanning for standard plugin folders and one nested ModInfo.json level.
- Manual and built-in Nexus ID mapping.
- Nexus update checks with version comparison.
- Optional 24-hour API response caching with coalesced end-of-pass disk flush.
- Optional scheduled background checks for mapped mods.
- Optional slow Nexus ID discovery, disabled by default.
- IMGUI dashboard tabs: Install & Update, My Mods (All / Updates Available / Up to Date / Unmapped sub-tabs), Conflicts, Settings (Analytics folded in).
- Lightweight conflict hints based on known names and broad functionality patterns.
- Dependency validator (Conflicts tab, "Dependencies" sub-section) - flags any `[BepInDependency]` declared by an installed plugin whose target GUID is not installed, hard dependencies first and labelled separately from soft ones (`PluginMetadataReader.cs`, `DependencyValidator.cs`). Declarations are read from each DLL's CLI metadata tables; no plugin assembly is ever loaded, executed, or locked. Renders nothing when every dependency resolves; the scan outcome is logged once per scan.
- Basic update analytics derived from the current checked mod list.
- Favorites and Ignore — star mods to highlight them; ignore mods to exclude from update checks. Both states persist across sessions.
- Per-mod notes attached to any mod; saved alongside favorite/ignore state.
- Mod metadata display — Nexus summary and endorsement count per checked mod.
- Major-version update warning — updates that change the major version are flagged in red.
- On-demand changelog fetch — shows Nexus version history as an inline scrollable panel; not fetched at startup.
- Mod list export — Analytics tab generates a formatted plain-text list of all installed mods suitable for bug reports.
- Self-exclusion — Mod Update Manager suppresses itself from the Unable to Check list via a sentinel registry entry.
- Re-check Rate-Limited - the My Mods tab shows a banner and button when any mod's last check hit Nexus's HTTP 429; clicking it re-runs the staggered check for only those mods, not the full list.

## Not Shipped

- Backup-before-update behavior.
- Rollback UI.
- Version timeline UI.
- Automatic beta compatibility validation.
- Security scanning, cloud sync, or AI recommendations.
- Read-only mod profiles (save/diff an enabled-set) — see `ROADMAP.md` Long-term Vision.
- Self-update staging (MUM cannot overwrite its own running DLL) — see `Documentation/Ideas/Mod_Update_Manager/Embedded_Mod_Suite_Integration.md`.

## Build Status

`dotnet build .\Mod_Update_Manager\Mod_Update_Manager.csproj -c Release` succeeds with 0 errors and 0 warnings as of this audit.
