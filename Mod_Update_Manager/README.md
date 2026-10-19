# Mod Update Manager

**Version:** 2.1.71  
**Author:** Jared (crispywhips)  
**For:** Card Survival: Fantasy Forest (EA 0.69c)

## Overview

Mod Update Manager installs and updates the crispywhips mod suite, and checks your other CSFF mods against Nexus Mods. Press **F3** in game to open it.

- **Install & Update tab:** 15 suite mods are packed inside the mod's own DLL, so installing needs no download and no API key.
- **My Mods, Conflicts and Settings tabs:** report Nexus updates, conflicts and missing dependencies for the mods you have.

Nothing is installed, replaced or rolled back until you press a button. It never downloads a mod: for mods outside the suite it reports updates and you install them yourself. It does not test mods against game betas. If a mod's Nexus release notes name the game build the author tested on, the changelog view shows it as a "Tested: EA x.y" line.

**Language:** English and Simplified Chinese. A Chinese line the window font cannot draw is shown in English instead of as empty boxes.

## Install & Update tab

The window opens here. It works offline.

### The bundled mods

| Released | Pre-release |
|----------|-------------|
| CSFF Mod Framework | Forest Lore |
| Advanced Copper Tools | Far Territory |
| Herbs & Fungi Expansion | Harsh Wilds |
| Water Driven Infrastructure | Partner Overhaul |
| Community Mod Chest | Sirus23 Mod Collection |
| Homestead Perks | Utility Belt |
| Repeat Action | |
| Quick Transfer | |
| Skill Training Speed Boost | |

Pre-release rows say **(Pre-release)**. Utility Belt includes its own copies of Repeat Action, Quick Transfer and Skill Speed Boost. If you also install any of those separately, BepInEx runs only the newer copy, because they share one plugin ID.

### Rows and status

Each row shows the installed and bundled versions and a badge: `[Up to Date]`, `[Update Available]`, `[Not Installed]`, `[Install Incomplete]` or `[Installed elsewhere]`. The last two are explained under Troubleshooting. For details, the **?** button under a row shows what that bundled mod's own `ModInfo.json` says about it. Nothing is fetched.

### Applying updates

Tick mods, or press **Select Out of Date / Not Installed** or **Select All**, then **Apply Updates**. The framework's `SpriteCache/` is kept.

Apply never downgrades. A mod newer than its bundled copy, or whose bundled copy cannot be read, is left alone and listed as skipped. So is an `[Installed elsewhere]` row.

When a ticked mod cannot work without another, Apply installs that one too if it is missing or older: the framework, or any suite mod it hard-depends on. The row is ticked for you, cannot be unticked, and reads "also installing: <mod> requires it" with a **Clear** button. Clearing it tells you what will happen: BepInEx refuses to load the mod, or, for the framework, the mod loads with some features off. Optional dependencies are not pulled in.

If the framework fails, nothing after it is touched. If another mod fails, the mods that need it are skipped and the rest carry on. A line above the rows says when your framework is older than the bundled one while your content mods are current.

### Safe installs

Apply never leaves a mod half-replaced. Each mod is unpacked into `BepInEx/mum_work/staging/` and every file is size-checked. Only then is your old copy moved to `BepInEx/mum_work/old/`, the new one moved into `BepInEx/plugins/`, and the old one deleted. Windows will not delete a mod file the running game has loaded, so that old copy stays in `old/` until the next time the game starts, when Mod Update Manager deletes it. A failed or interrupted apply leaves your installed copy as it was. A game closed partway leaves either the old copy or the new one, whole.

`BepInEx/mum_work/` sits beside `plugins/`, so BepInEx, the framework and My Mods never see it. If a folder cannot move between the two (for example when `plugins` is a link to another drive), the log says so once, and that session unpacks inside `plugins/` instead.

Apply works a few files at a time, so the window keeps drawing and the status line says what it is doing. Its controls stay disabled until the batch ends. A link inside a replaced copy is removed as a link, never followed.

After a batch a banner asks you to restart, with a **Quit Game** button. If some mods installed and one failed, **Retry failed (N)** applies the failed mods again, plus any held back because a mod they need failed.

### Keeping the previous version

**Keep the previous version, and Roll back (off by default)**

Turn on **Keep the previous version when updating a suite mod** in Settings (`KeepPreviousSuiteVersion`). Apply then moves the copy it replaces to `BepInEx/mum_work/kept/<Folder>/<version>/` instead of deleting it. The game never loads it.

A mod with a kept copy shows `[Previous: <version>]` and a **Roll back** button. Rolling back needs a restart and keeps the copy it replaces, so you can go forward again. `KeptVersions` (1 to 3, default 1) sets how many copies each mod holds; older ones are deleted, only inside `kept/`.

A replaced copy is deleted instead if its `ModInfo.json` has no readable, path-safe version, if it matches the version just installed, or if it lacks a `ModInfo.json` and a DLL.

Disk cost: each kept copy is the size of that mod's folder. On a developer install on 2026-10-13, the fifteen suite mod folders came to 231,828,844 bytes. That excludes the framework's `SpriteCache`, which moves to the new copy and is never kept. Far Territory is 73.6 MB, Community Mod Chest 51.2 MB, Harsh Wilds 31.4 MB and Herbs & Fungi 25.0 MB.

Turning the option off stops new copies. Copies already kept stay and are still offered. Delete their folders under `BepInEx/mum_work/kept/` to remove them.

### Updating the whole suite: download only Mod Update Manager

Every Mod Update Manager release carries the newest version of all fifteen suite mods, so you never need to download them one by one:

1. Download the newest Mod Update Manager from Nexus and replace your `Mod_Update_Manager` folder with it.
2. Start the game. The window opens on Install & Update with every suite mod you have that this release updates already ticked, and a line saying which version you came from.
3. Press **Apply Updates**, then restart when asked.

Only mods you already have are ticked: the upgrade never adds a mod you did not install. Nothing is installed until you press Apply.

When an update check runs (with an API key, or with **Check for updates without an API key** on), it also asks Nexus about Mod Update Manager's own page, in the same request. If a newer one is out, the line at the top of Install & Update says so with a **Download** button for its Nexus files page. When the startup check also finds updates, its one automatic open lands on this tab instead of My Mods. It also leaves a small note, `BepInEx/config/ModUpdateManager_Handoff.txt`, naming the suite mods Nexus had newer versions of. The new Mod Update Manager reads it on its first start, and if Nexus had a newer version of a mod than even the new release carries, it says so instead of reading as done. The note is deleted once read.

Coming from 2.1.63 to 2.1.69, which leave no note, the first start still finds the upgrade from the version history and ticks the same mods. From an older one, which kept no history, press **Select Out of Date / Not Installed** instead.

## My Mods tab

### Finding your mods

The scan runs when the game starts and when you press **Refresh Mod List**. It reads `ModInfo.json` from each plugin folder and one nested level. A plugin DLL with no `ModInfo.json` is listed with an Unknown version.

Mods link to Nexus IDs through a built-in registry of known CSFF mods, a `NexusModId` in a mod's `ModInfo.json`, or a mapping you add (see "Linking mods to Nexus").

- **Self-exclusion**: Mod Update Manager is never a row in My Mods, and it cannot replace its own running DLL. The Install & Update tab shows a read-only line for it with its version and a **Nexus page** button, which becomes a **Download** button when a check found a newer one (see "Updating the whole suite"). Select All and Apply Updates never touch that line. Newer suite versions arrive inside each new release.

### Update checks

A check asks Nexus about every mapped mod in one public request. Only a mod that answer does not settle is asked about on its own, which needs an API key. A mod Nexus lists as hidden or removed is offered no update. A major-version change is flagged in red.

Without a key nothing is sent, unless you turn on **Check for updates without an API key** in Settings.

With the startup check, the window opens by itself once, on Updates Available, when it finds an update. Background checks and the check buttons never reopen it or switch tabs. `OpenWindowOnUpdates` turns the one open off.

With caching on, Nexus answers are kept for 24 hours for the startup and background checks. **Check for Updates** and **Re-check Rate-Limited** always ask Nexus. A rate-limited request is retried once after five seconds.

Saving a key checks it with Nexus once and shows "Key accepted", "Key rejected", "Could not reach Nexus", or that Nexus is rate limiting.

### Filters, sorting and marks

Filters: All, Updates Available, Up to Date and Unmapped, plus **New and updated** while `EnableNexusDiscovery` is on.

The **Sort:** button cycles **Name** (default), **Update status first**, **Endorsements** (most first) and **Author** (A to Z, none last). Favorites stay on top in every order, and your choice is remembered. Sorting sends nothing and does not apply to the Unmapped or New and updated lists.

Star a mod to keep it on top, ignore one to leave it out of checks, and add a short note to any mod.

### NEW chips and History

When a mod's installed version changed since the game last started, its row shows **NEW: 1.13.7 to 1.14.0** until you press **Mark all as seen**. A mod that appeared reads **NEW: installed 1.0.0**. The first start only records what is installed.

**History** on each row lists that mod's recorded changes, newest first, up to 20, with the date and whether it came from a **startup scan** or a **suite Apply**.

### Was it loaded?

Each row says **Loaded by BepInEx**, **No plugin loaded from this folder**, or **Load status unknown** when BepInEx's list cannot be read. With the CSFF Mod Framework installed, it also says whether the framework loaded data from that folder, or whether another copy may have been used.

### Changelogs

**Changelog** on a checked mod shows its Nexus version history in a panel under the row.

### Endorse button (off by default)

Turn on **Show an Endorse button on mods with a Nexus ID** in Settings (`EnableEndorseButton`). With an API key set, each row whose mod has a Nexus ID and a readable installed version gets an **Endorse** button.

It asks "Endorse (mod name) on Nexus with your account?" Only **Yes** sends anything. The line under the row then reports what Nexus answered: endorsed, refused, key not accepted, rate limited, or not known. Take an endorsement back on the mod's Nexus page.

### New and updated list (off by default)

With `EnableNexusDiscovery` on, the **New and updated** filter lists the most recently updated CSFF mods on Nexus that you do not have, each with name, version, summary, endorsements and an **Open Nexus Page** button. It is fetched when you first open the filter in a session and on **Refresh**, never at startup. It needs an API key, or **Check for updates without an API key** turned on.

Mods you have installed or mapped, this mod's own page, and pages Nexus marks as adult or does not rate are left out. A line above the list counts what was shown and left out. `DiscoveryFeedSize` (1 to 50, default 10) sets how many Nexus is asked for and the most shown. Nothing is saved to disk.

`EnableNexusDiscovery` also turns on a slow scan that looks up Nexus IDs for unmapped mods. It needs an API key and is off by default to save API quota.

## Conflicts tab

`[Same plugin GUID]` marks a confirmed conflict: two plugin DLLs register the same BepInEx plugin ID, and BepInEx loads only one. Other entries are guesses from mod names, shown muted and marked "(heuristic - check manually)".

**Dependencies** lists each declared dependency that is not installed, hard ones first. **Show load order** lists the loaded plugins in the order BepInEx enumerates them, for bug reports. BepInEx does not promise that order.

## Settings tab

Settings holds the API key, the Endorse button, manual Nexus mappings, keeping the previous suite version, background checks, caching and discovery. It also shows update counts and simple estimates.

The mod list export builds a plain-text list of your installed mods (name, version, author, status, latest version, favorites, ignored marks and notes) with a **Copy to Clipboard** button for bug reports.

**Profiles** saves your installed mods (folder, name, version) under a name of up to 60 letters, numbers, spaces, hyphens or underscores. **Compare** shows **Missing here**, **Extra here**, **Older here** and **Newer here** against the current install. **Delete** removes only that profile's file. Deleting or saving over the profile you last compared clears that comparison. Profiles install and remove nothing and send nothing. Copy a profile file to a friend and they can drop it into the same folder.

## Requirements

| Requirement | Notes |
|-------------|-------|
| Card Survival: Fantasy Forest | Steam version (EA 0.69c) |
| BepInEx 5.x | Mod loader |
| Nexus Mods API key | Free with a Nexus account. Needed only for the Nexus checks on My Mods, unless you turn on **Check for updates without an API key** |
| Internet | Needed only for update checks |

No other mod is required, including the CSFF Mod Framework.

## Installation

1. Install BepInEx if you have not.
2. Extract the `Mod_Update_Manager` folder to `BepInEx/plugins/`.
3. Launch the game and press **F3**.

For Nexus checks, open **Settings**, paste your key from `https://www.nexusmods.com/users/myaccount?tab=api+access` and click **Save**. With no account, tick **Check for updates without an API key** instead.

## Linking mods to Nexus

- Add a manual mapping in Settings.
- Enter a Nexus ID on a row of the My Mods **Unmapped** filter and save it.
- Add `"NexusModId": "123"` to a mod's `ModInfo.json`.

The Nexus ID is the number at the end of the mod page URL.

## Configuration

File: `BepInEx/config/crispywhips.mod_update_manager.cfg`

The **Settings** tab covers most of these in game. The optional [BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases) (BepInEx 5 build) shows all of them: press F1 and find "Mod_Update_Manager". A change made there applies at once, except `WindowWidth`, `WindowHeight`, `DiscoveryMaxScanId`, `DiscoveryMaxConsecutiveMisses` and `CheckOnStartup`, which need a restart. `CheckIntervalMinutes` applies at the next launch or when background checking is switched back on.

| Setting | Default | Description |
|---------|---------|-------------|
| NexusApiKey | empty | Your Nexus API key |
| UsePublicEndpointWithoutKey | false | Check for updates without a key (see "Network") |
| EnableEndorseButton | false | Show the **Endorse** button on My Mods rows |
| CheckOnStartup | true | Check for updates after game data loads |
| OpenWindowOnUpdates | true | Let the startup check open the window once when it finds an update |
| ShowOnlyUpdates | false | Show only mods with updates in the main list |
| ToggleKey | F3 | Key that opens and closes the window |
| WindowWidth | 900 | Window width |
| WindowHeight | 680 | Window height |
| DetailsMaxChars | 2500 | Longest description the **?** panel shows before cutting it (200 to 10000) |
| KeepPreviousSuiteVersion | false | Keep the copy Apply replaces, so you can roll back |
| KeptVersions | 1 | Copies kept per suite mod, 1 to 3 |
| EnableBackgroundChecking | false | Check mapped mods in the background |
| CheckIntervalMinutes | 60 | Minutes between background checks, 10 to 1440 |
| ShowConflictWarnings | true | Show conflict hints and missing dependencies |
| CachingEnabled | true | Keep Nexus answers for 24 hours for startup and background checks |
| EnableNexusDiscovery | false | Adds the **New and updated** filter and the Nexus ID scan |
| DiscoveryMaxScanId | 2000 | Highest Nexus ID the scan reaches |
| DiscoveryMaxConsecutiveMisses | 500 | Stop the scan after this many misses in a row |
| DiscoveryFeedSize | 10 | Rows the **New and updated** filter asks for and shows, 1 to 50 |

### Network

Mod Update Manager talks to one host, `api.nexusmods.com`. Every request carries `User-Agent: Mod_Update_Manager/<version>`, `Application-Name: Mod_Update_Manager` and `Application-Version: <version>`. Nexus's API Acceptable Use Policy asks applications for the last two.

Requests that use your API key carry it in an `apikey` header, with `Accept: application/json`. The two public requests below carry no key. Your key is stored in plain text in `BepInEx/config/crispywhips.mod_update_manager.cfg`, like any BepInEx setting, and is sent only to Nexus.

Nothing else is sent: no mod names or folders, no game or save data, no file contents, only the Nexus IDs, the count and the one installed version the table names.

| When | What is sent |
|------|--------------|
| A check for updates: the startup check, a background check, **Check for Updates**, **Re-check Rate-Limited** | First, one `POST /v2/graphql` to Nexus's public read endpoint, with no key, naming the game (`cardsurvivalfantasyforest`) and the Nexus ID of every mod being checked (mapped, not ignored, with a readable version). It asks for each mod's name, version, status, last update time, endorsement count and summary. With `CachingEnabled` on, the startup and background checks leave out a mod answered under 24 hours ago, and send nothing when every mod was; the two buttons always ask. Then, only while a key is set, one `GET /v1/games/cardsurvivalfantasyforest/mods/<id>.json` for each mod that answer did not settle: every mod when the public answer failed or could not be read, otherwise only mods it did not list. The Nexus ID in the address is all that names the mod. A request answered with HTTP 429 (rate limited) is sent once more after a short wait. Without a key nothing more is sent, and an unsettled mod shows as not checked, never as up to date. |
| **Changelog** clicked on a mod | One `GET /v1/games/cardsurvivalfantasyforest/mods/<id>/changelogs.json`, the first time that mod's changelog is opened in a game session. |
| **Save** clicked on the API key | One `GET /v1/users/validate.json`, which carries the key. Only the HTTP status is read. The reply is not read, saved, logged or shown. Never sent at startup. |
| **Yes** on a mod's Endorse question (only with `EnableEndorseButton` on and an API key set) | One `POST /v1/games/cardsurvivalfantasyforest/mods/<id>/endorse.json`, which carries the key, `Content-Type: application/json` and the body `{"Version":"<installed version>"}`. That is the version you have installed, as Nexus lists it; for the few mods the built-in registry records with a different version on Nexus, that one. It endorses that mod with your Nexus account. The **Endorse** button itself only asks "Endorse (mod name) on Nexus with your account?" and sends nothing, and **No** sends nothing. Never sent at startup or by any check, never for more than the one mod, and never repeated on its own. Only the HTTP status is read. |
| Nexus ID discovery (`EnableNexusDiscovery`, off by default) | One mod-page request at a time with a long pause between them, walking Nexus IDs upward from where the last scan stopped, up to `DiscoveryMaxScanId`, whether or not you have that mod installed. Needs an API key. |
| The **New and updated** filter on My Mods (only with `EnableNexusDiscovery` on, and an API key set or **Check for updates without an API key** on) | One list request, the first time you open that filter in a game session and again each time you press its **Refresh** button. Never at startup or from any check. It is one `POST /v2/graphql` to Nexus's public read endpoint, with no key, naming the game (`cardsurvivalfantasyforest`) and a count (`DiscoveryFeedSize`, 1 to 50, default 10). It asks for the most recently updated mods of that game: each one's Nexus ID, name, version, summary, endorsement count, last update time and whether it is marked adult content. None of your Nexus IDs, mod names or folders is sent. Nothing is downloaded, the answer stays in memory only (not in a file or the response cache) until the game closes, and no second request is made to fill in a row. |

With no key set, nothing is sent unless you turn on **Check for updates without an API key** (`UsePublicEndpointWithoutKey`, off by default). A check then sends only the public query above. The changelog, key check, Endorse button and Nexus ID scan each need a key.

The exception is the **New and updated** list. With no key it runs only when you have turned on both `EnableNexusDiscovery` and **Check for updates without an API key**, and sends its one list request. With a key it needs only `EnableNexusDiscovery`.

The Install & Update tab sends nothing, because the suite is inside the DLL. **Open Nexus Page** and **Nexus page** hand an address to your browser, and **Quit Game** asks Steam to start the game again. None of the three sends anything itself.

For anyone reading the source: every request above is made by `NexusApiClient` (the requests that use your key) or `NexusPublicClient` (the two public requests). An automated test in the mod's source fails if any other part of the mod starts sending something.

## Data Locations

- Mod mappings: `BepInEx/config/ModUpdateManager_Mappings.json`
- Nexus response cache: `BepInEx/config/ModUpdateManager_Cache.json`
- Nexus discovery cache: `BepInEx/config/nexus_discovery_cache.json`
- Favorites, ignore, notes, and the My Mods search text and sort order: `BepInEx/config/ModUpdateManager_Preferences.json`
- Version history for the NEW chips and History lists: `BepInEx/config/ModUpdateManager_VersionLog.txt`, one line per change (`folder|from|to|date|source`) plus a `#seen|date` line for Mark all as seen. An unreadable line is skipped and counted in one log line. Delete the file to start the history over.
- Saved profiles: `BepInEx/config/ModUpdateManager_Profiles/<name>.profile.txt`, one line per mod (`folder|name|version`). Lines starting with `#` are ignored, and a line that cannot be read is skipped and counted in the Compare summary.
- Upgrade note for the next Mod Update Manager: `BepInEx/config/ModUpdateManager_Handoff.txt`, written only after a check found a newer one on Nexus, deleted when the newer one has read it. Deleting it by hand only loses the "still behind Nexus" line; the new release still ticks the mods it updates.
- The **New and updated** list has no file. It lives in memory for the running game only.
- Install & Update work folder: `BepInEx/mum_work/`, beside `plugins/`, so nothing in it is loaded. Its `staging/` and `old/` folders are empty except during Apply Updates or after one was cut short.
- An Apply is cut short when the game closes mid-update, a row shows `[Install Incomplete]`, or a replaced copy cannot be deleted. The next start of the game clears it, as does the next Apply of that mod, putting a moved-aside copy back first.
- Its `kept/<Folder>/<version>/` folders exist only if you turned on **Keep the previous version when updating a suite mod**. Each is a whole earlier copy of that mod. Only you, or the pruning that holds `KeptVersions` copies (never while the option is off), deletes one.

## Release notes and game builds

Authors: for a release made for a CSFF beta or new game build, name the supported build in the Nexus release notes, for example `Compatible with Card Survival: Fantasy Forest EA 0.68a`.

The newest such line shows under the changelog as **Tested: EA x.y**. It is green for your game version or newer, orange (naming your version) when older, and grey when either cannot be read. Hotfix letters are ignored, so a mod tested on 0.68a counts as current on 0.68b. It repeats what the author wrote; nothing is tested.

## Troubleshooting

**The window does not open with F3**
- Check `BepInEx/LogOutput.log` for errors and look for `Mod_Update_Manager v2.1.71 loaded.`
- Make sure BepInEx is installed correctly.

**Install & Update shows "[Unknown]" for every mod**
- The bundled mods could not be read. `LogOutput.log` has the error. The other tabs still work.

**Apply Updates says "FAILED: ... your installed copy was not changed"**
- Nothing was replaced. The message says why.
- "did not extract fully" often means antivirus briefly locked a file. `LogOutput.log` has `Suite verify:` lines naming the files.
- "a file in its folder is in use" means another program holds a file in `BepInEx/plugins/<Folder>/` open. Close it, then press **Retry failed** in the banner. With no banner, select the mod and press Apply Updates again.

**A row shows "[Install Incomplete]" after Apply Updates**
- Rare: the new copy could not be moved into place and the old one could not be moved back. Your previous copy is safe in `BepInEx/mum_work/old/<Folder>/` (or `BepInEx/plugins/<Folder>__mum_old/` if the log says that session unpacked inside `plugins/`).
- Restart the game, then press Apply Updates. It puts the previous copy back before trying again.

**Roll back says "FAILED: ... your installed copy was not changed"**
- Nothing was swapped, and the kept copy is still in `BepInEx/mum_work/kept/<Folder>/<version>/`.
- "the kept copy ... is not there or is incomplete" means that folder changed after the row was drawn.
- "a file in its folder is in use" means another program holds a file. Close it and press **Roll back** again.
- "there is no work folder to roll back through this session" means a folder could not move between `BepInEx/mum_work/` and `plugins/`. Nothing is kept or rolled back that session.

**A row shows "[Installed elsewhere]"**
- The mod is not in `BepInEx/plugins/<Folder>/`, but a copy with the same mod name sits in another folder under `plugins/`, which the row names. Extracting a release zip into a folder named after it does this, for example `plugins/Herbs_And_Fungi_1-14-0/Herbs_And_Fungi/`.
- Apply skips it, because a second copy could mix code and data from different versions.
- To manage the mod here, move the inner folder to sit directly in `BepInEx/plugins/` as `<Folder>`, or delete the extra copy, then press **Refresh Mod List**.
- A copy in the mod's own folder always decides the row, so this badge never hides an update.

**A row shows "also installing: ... requires it"**
- A mod you ticked cannot work without that row, which is missing or older than the bundled copy. Press **Clear** to leave it out. Ticking it again, a Select button or **Retry failed** brings it back.

**Apply Updates says "The framework did not install, so nothing after it was touched." or lists skipped mods**
- A failed framework stops the batch. When another mod fails, each mod that needs it is skipped and named, and **Retry failed** tries them all again. The banner appears once any mod in the batch installed. Without it, press **Apply Updates** again.

**The tab says the installed framework is older than this bundle's**
- Your framework is older than the one packed into this Mod Update Manager while a content mod is as new as its bundled copy. It is a note only. Select the framework's `[Update Available]` row and press **Apply Updates**.

**API key not set, or Save says "Key rejected"**
- Copy the whole key again from the Nexus page above. A missing character or a pasted line break is enough to break it.

**Save says "Could not reach Nexus", rate limiting, or an HTTP status**
- The key was saved, but Nexus gave no verdict. Check your connection, wait a few minutes and click Save again, or press **Check for Updates**.

**A mod has no Endorse button**
- It needs `EnableEndorseButton` on, an API key, a Nexus ID on that row, and a readable installed version. Map an Unmapped mod first. Mod Update Manager's own row has none.

**Endorse says Nexus refused**
- Nexus accepts an endorsement only from an account that downloaded that mod at least 15 minutes ago, is not its author, and for a version its page lists. The line shows only the HTTP status, so it cannot say which rule applied. Download the mod while signed in, wait 15 minutes and try again.

**Updates are not detected**
- Check the Nexus ID and that the installed mod has a readable version in `ModInfo.json`.
- Press **Check for Updates**. It always asks Nexus, so a release from this morning shows.

**A mod shows "Not published on Nexus (status: ...)"**
- Nexus lists that page as hidden or removed, so no update is offered. Your installed copy is not affected.

**A mod shows "Nexus's answer did not list this mod" or "Nexus's public answer could not be read"**
- You are checking without a key and the public request did not settle that mod. Check its Nexus ID, or set a key, which asks about it on its own.

**The New and updated filter is missing, or says it needs an API key**
- It appears only while `EnableNexusDiscovery` is on. It needs a key or **Check for updates without an API key**. Nothing is sent while the message shows.

**The New and updated list shows few rows, or none**
- The header line counts what Nexus listed and what was left out. Only the `DiscoveryFeedSize` newest mods are listed, so with most of the suite installed you see few. Raise it (up to 50) in the cfg and press **Refresh**.
- If the line says the list could not be read, press **Refresh**. `LogOutput.log` says why.

## Credits

- **Author:** Jared (crispywhips)
- **Community:** Chiwei, for trait designs, playtesting and artwork in the bundled Community Mod Chest and CSFF Mod Framework
- **Libraries:** BepInEx and Harmony
- **API:** Nexus Mods API
- **Game:** Card Survival: Fantasy Forest by WinterSpring Games

## License

MIT License. Feel free to modify and redistribute.
