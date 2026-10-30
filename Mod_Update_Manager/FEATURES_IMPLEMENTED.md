# Mod Update Manager - Feature Status

Date: 2026-10-29

This document describes what is actually wired in the shipped mod. Planned features live in `Documentation/Design/Mod_Update_Manager_Round2_As_Built.md`.

## Shipped

- **Crispywhips Mod Suite installer** (the "Install & Update" tab, default on open, shipped v2.1.5) - one-click install/update of the 15-mod crispywhips family from ZIPs embedded directly in the MUM DLL (`ModSuiteExtractor.cs`, `SuiteVersionReader.cs`, `MiniZip.cs`, `SuiteModRegistry.cs`). Batch selection ("Select Out of Date / Not Installed" / "Select All") + "Apply Updates" installs chosen mods into `BepInEx/plugins/`, preserving the framework's `SpriteCache/`. Works fully offline; no Nexus API key needed for this tab.
- Crash-safe suite install (v2.1.62, `SuiteInstaller.cs`) - each mod is extracted into `BepInEx/mum_work/staging/<Folder>` and size-verified there (since 2.1.63; 2.1.62 staged inside `plugins/` as `<Folder>__mum_staging`, which a closed game could leave for loaders to read); only then is the installed folder renamed to `BepInEx/mum_work/old/<Folder>` and the staging folder renamed into its place, so a failed or interrupted apply leaves the installed copy exactly as it was. A copy an interrupted earlier apply left aside is restored first.
- Apply in steps (v2.1.63, `SuiteInstaller.InstallSteps`, `UpdateManagerUI.ApplySuiteCoroutine`) - Apply runs as a coroutine that installs a few files per step and waits one frame (`yield return null`) between steps, so the window keeps drawing an "Extracting <mod> (n of m files)" status line and the game keeps rendering; the swap's two renames stay inside one step.
- Bundled description in the "?" panel (v2.1.63, `SuiteDescription.cs`, `SuiteVersionReader.ReadEmbeddedInfo`) - the details panel under a suite row shows the Description from the bundled mod's own `ModInfo.json` (read from the embedded ZIP), in a 120-pixel scroll view cut at the `DetailsMaxChars` setting (default 2500) with a pointer to the Nexus page; a bundled mod with no description shows the registry's hand-typed line instead.
- Installed-elsewhere rows (v2.1.63, `SuiteInstalledElsewhere.cs`) - a suite mod found under a folder other than `BepInEx/plugins/<Folder>/` (its bundled `ModInfo.json` Name matched against the scan of `plugins/`, leaving out the row's own folder and Mod Update Manager's own staging, backup and work folders) reads `[Installed elsewhere]` with its path when nothing is in its own folder; Apply skips it and neither Select button picks it. The rows are classified again with the scan made when the game loads (`Plugin.OnGameDataLoaded`) and on **Refresh Mod List**.
- Installed-mod scanning for standard plugin folders and one nested ModInfo.json level.
- Manual and built-in Nexus ID mapping.
- Nexus update checks with version comparison.
- Optional 24-hour API response caching with coalesced end-of-pass disk flush.
- Optional scheduled background checks for mapped mods.
- Optional slow Nexus ID discovery, disabled by default.
- IMGUI dashboard tabs: Install & Update, My Mods (All / Updates Available / Up to Date / Unmapped sub-tabs), Conflicts, Settings (Analytics folded in).
- Conflict hints in two tiers (v2.1.62): `[Same plugin GUID]` for two plugin DLLs that register one BepInEx GUID, read from their metadata (`PluginGuidCollisions.cs`, via `PluginMetadataReader.cs`); name-pattern and hand-listed pairs shown muted with "(heuristic - check manually)". The scan outcome is logged once per scan.
- Dependency validator (Conflicts tab, "Dependencies" sub-section) - flags any `[BepInDependency]` declared by an installed plugin whose target GUID is not installed, hard dependencies first and labelled separately from soft ones (`PluginMetadataReader.cs`, `DependencyValidator.cs`). Declarations are read from each DLL's CLI metadata tables; no plugin assembly is ever loaded, executed, or locked. Renders nothing when every dependency resolves; the scan outcome is logged once per scan.
- Basic update analytics derived from the current checked mod list.
- Favorites and Ignore - star mods to highlight them; ignore mods to exclude from update checks. Both states persist across sessions.
- Per-mod notes attached to any mod; saved alongside favorite/ignore state.
- Mod metadata display - Nexus summary and endorsement count per checked mod.
- Major-version update warning - updates that change the major version are flagged in red.
- On-demand changelog fetch - shows Nexus version history as an inline scrollable panel; not fetched at startup.
- Tested-build line (v2.1.62, `CompatibilityChip.cs`) - under a fetched changelog, "Tested: EA x.y" when the release notes carry a "Compatible with ... EA x.y" line: green for the running build or newer, orange with the running version named when older, grey when either side does not read as MAJOR.MINOR.
- Mod list export - Analytics tab generates a formatted plain-text list of all installed mods suitable for bug reports.
- Self-exclusion - Mod Update Manager suppresses itself from the Unable to Check list via a sentinel registry entry.
- Re-check Rate-Limited - the My Mods tab shows a banner and button when any mod's last check hit Nexus's HTTP 429; clicking it re-runs the staggered check for only those mods, not the full list. Since v2.1.62 a 429 is first retried once, 5 real-time seconds later, so only a mod that is rate-limited twice lands there.
- Mapping cleanup on load (v2.1.62) - rows in `ModUpdateManager_Mappings.json` that only repeat the built-in registry are dropped, so a later registry correction reaches the player; differing rows (real overrides) and rows for mods the registry does not know are kept, and the file is rewritten only when something was dropped.
- Keep the previous version, and Roll back (v2.1.63, off by default, `SuiteKeepPolicy`/`SuiteInstaller.Kept.cs`) - `KeepPreviousSuiteVersion` makes Apply keep the copy it replaces in `BepInEx/mum_work/kept/<Folder>/<version>/` instead of deleting it. A row with a kept copy shows `[Previous: <version>]` and a **Roll back** button; `KeptVersions` (1 to 3, default 1) sets how many are held and prunes the rest.
- NEW chips and a History button on My Mods rows (v2.1.63, `VersionLedger.cs`) - a row shows **NEW: old to new** when a mod's installed version changed since the game last started, until **Mark all as seen**; **History** lists a mod's recorded changes (up to 20) with date and source (startup scan or suite Apply).
- Update-the-whole-suite handoff (v2.1.70, `MumHandoff.cs`) - replacing Mod Update Manager with a newer release opens Install & Update with every suite mod that release updates already ticked, carried by a small note (`BepInEx/config/ModUpdateManager_Handoff.txt`) the older MUM leaves and the newer one reads and deletes.
- Mod Update Manager's own update line (v2.1.70) - an update check also asks Nexus about Mod Update Manager's own page in the same request; when a newer one is out, the top of Install & Update shows a line with a **Download** button.

## Not Shipped

- Backup-before-update behavior for mods outside the bundled suite (the suite's own Keep Previous Version/Roll back, above, covers only the 15 bundled mods).
- Automatic beta compatibility validation.
- Security scanning, cloud sync, or AI recommendations.
- Read-only mod profiles (save/diff an enabled-set) beyond the Settings tab's named Profiles (save/Compare/Delete) - see `ROADMAP.md` Long-term Vision.
- Self-update staging (MUM cannot overwrite its own running DLL) - ruled out; the reason is in the Considered table of `Documentation/Design/Mod_Update_Manager_Round2_As_Built.md` ("MUM self-update via `.pending` staging").

## Build Status

`dotnet build .\Mod_Update_Manager\Mod_Update_Manager.csproj -c Release` succeeds with 0 errors and 0 warnings as of this audit.
