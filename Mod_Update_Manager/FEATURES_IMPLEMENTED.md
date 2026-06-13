# Mod Update Manager - Feature Status

Date: 2026-06-13

This document describes what is actually wired in the shipped mod. Planned features live under `Documentation/Ideas/Mod_Update_Manager/`.

## Shipped

- Installed-mod scanning for standard plugin folders and one nested ModInfo.json level.
- Manual and built-in Nexus ID mapping.
- Nexus update checks with version comparison.
- Optional 24-hour API response caching with coalesced end-of-pass disk flush.
- Optional scheduled background checks for mapped mods.
- Optional slow Nexus ID discovery, disabled by default.
- IMGUI dashboard tabs for all mods, update candidates, up-to-date mods, unmapped mods, conflicts, analytics, and settings.
- Lightweight conflict hints based on known names and broad functionality patterns.
- Basic update analytics derived from the current checked mod list.
- Favorites and Ignore — star mods to highlight them; ignore mods to exclude from update checks. Both states persist across sessions.
- Per-mod notes attached to any mod; saved alongside favorite/ignore state.
- Mod metadata display — Nexus summary and endorsement count per checked mod.
- Major-version update warning — updates that change the major version are flagged in red.
- On-demand changelog fetch — shows Nexus version history as an inline scrollable panel; not fetched at startup.
- Mod list export — Analytics tab generates a formatted plain-text list of all installed mods suitable for bug reports.
- Self-exclusion — Mod Update Manager suppresses itself from the Unable to Check list via a sentinel registry entry.

## Not Shipped

- Automatic downloads or installs.
- One-click updates.
- Batch update installation.
- Backup-before-update behavior.
- Rollback UI.
- Version timeline UI.
- Automatic beta compatibility validation.
- Security scanning, cloud sync, or AI recommendations.

## Build Status

`dotnet build .\Mod_Update_Manager\Mod_Update_Manager.csproj -c Release` succeeds with 0 errors and 0 warnings as of this audit.
