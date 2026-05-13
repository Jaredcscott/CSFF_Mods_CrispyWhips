# Mod Update Manager - Feature Status

Date: 2026-04-30

This document describes what is actually wired in the shipped mod. Planned features live under `Documentation/Ideas/Mod_Update_Manager/`.

## Shipped

- Installed-mod scanning for standard plugin folders and one nested ModInfo.json level.
- Manual and built-in Nexus ID mapping.
- Nexus update checks with version comparison.
- Optional 24-hour API response caching.
- Optional scheduled background checks for mapped mods.
- Optional slow Nexus ID discovery, disabled by default.
- IMGUI dashboard tabs for all mods, update candidates, up-to-date mods, unmapped mods, conflicts, analytics, and settings.
- Lightweight conflict hints based on known names and broad functionality patterns.
- Basic update analytics derived from the current checked mod list.

## API Present But Not Exposed In UI

- `NexusApiClient.GetChangelog()` exists, but the dashboard does not display changelogs or beta-compatibility release notes yet.

## Not Shipped

- Automatic downloads or installs.
- One-click updates.
- Batch update installation.
- Backup-before-update behavior.
- Rollback UI.
- Version timeline UI.
- Full Nexus metadata display.
- Automatic beta compatibility validation.
- Security scanning, cloud sync, or AI recommendations.

## Build Status

`dotnet build .\Mod_Update_Manager\Mod_Update_Manager.csproj -c Release` succeeds with 0 errors and 0 warnings as of this audit.
