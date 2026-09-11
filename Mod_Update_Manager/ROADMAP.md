# Roadmap: Mod Update Manager
Version at time of writing: 2.1.32
Date: 2026-09-05
Audit score: 10/10 (release-ready, per `.audit/summary.md` consolidated 2026-09-05)

## Current State

**Theme**: A standalone BepInEx utility for players who run modpacks. It scans installed CSFF mods,
checks them against Nexus Mods for updates, and ships a one-click "Install & Update" tab that
extracts the 9-mod crispywhips suite (CSFF Mod Framework, Advanced Copper Tools, Herbs & Fungi, Water
Driven Infrastructure, Community Mod Chest, Homestead Perks, Repeat Action, Quick Transfer, Skill
Speed Boost) straight out of ZIPs embedded in its own DLL. Sirus23 Mod Collection is deliberately not
bundled. It never downloads, deletes, or auto-updates anything from Nexus.

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom card images. UTILITY mod:
21 source `.cs` files, 9 embedded suite ZIPs (`Resources/EmbeddedMods/`), 1 IMGUI dashboard (F3),
1 Harmony postfix (`GameLoad.LoadMainGameData`), 2 localization keys (EN + SimpCn).

**Stability**: 10/10 - 0 CRITICAL, 0 DESIGN GAP, 0 open WARNING in code. The only open findings are
three documentation-accuracy defects (stale EA 0.65 compatibility label; "8-mod" where the bundle is
9; an imprecise tab enumeration) plus five minor polish items. Code quality is 10/10 across all nine
check families; build is 0 errors / 0 warnings; bin/Release sync and Chinese parity are clean.

**Open work**: None blocking. No entry in `Documentation/Retrospectives/INDEX.md` names
Mod_Update_Manager, the `MUM` shorthand, or the plugin GUID `crispywhips.mod_update_manager`
(re-confirmed by full-text grep including `_Archive/`).

**Framework compliance**: N/A by design, and this is a deliberate architectural stance, not a gap.
MUM declares no `[BepInDependency]` on CSFFModFramework, ModCore, or ModLoader and uses zero Tier 2
services (`ActionRouter` / `SpawnService` / `TickEvents` / `ContentModPlugin` - grep returns 0 hits).
Its mod-local `SimpleJson`, `MiniZip`, and `ModScanner` are intentional zero-dependency
implementations, NOT framework-service duplication to be "fixed". Tier 2 is not applicable to a
network + IMGUI utility that touches the game through a single once-per-load postfix. Do not migrate.

**Known risk surface** (unscored, from `.audit/summary.md` Risk & Coverage Notes):
- `Resources/EmbeddedMods/*.zip` is gitignored (`.gitignore:30`), so embedded-payload drift is
  invisible to every git diff and to every code-level audit. No automated guard exists.
- Suite-completeness was last verified by a sub-audit on 2026-08-11, against
  `Development_Tools/Pack-Suite.ps1` only and at the 8-mod count. The invariant itself IS gated -
  `Development_Tools/Tests/Deploy-Mods.Tests.ps1` asserts every `JsonDataLoader.DirToTypeName` folder
  appears in BOTH `Pack-Suite.ps1` and `Deploy-Mods.ps1` (a folder missing from both is silently never
  shipped AND deleted from existing installs, since `ModSuiteExtractor` wipes the target folder) - but
  no `.audit/` report records a run of that suite against the current 9-mod bundle.
- 7/7 feature groups sit at MEDIUM confidence, 0 VERIFIED: no playtest digest exists for this mod, so
  no human-observed PASS exists for suite extraction, a live Nexus check, or the boot hook firing.

---

## Phase 0: Stabilize

> Audit score is 10/10 with no open retrospectives, so there is nothing to stabilize in the usual
> sense. What belongs here instead are the two guards for the one bug class that has actually
> recurred in this mod's history (embedded-ZIP staleness, the v2.1.11-era CRITICAL, caught only by
> manual re-audit). The packaging folder-coverage invariant is already gated by
> `Deploy-Mods.Tests.ps1`; what it lacks is a recorded run against the 9-mod bundle.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Pester gate: FAIL when any embedded suite ZIP's `ModInfo.json` version < the sibling mod folder's current version | Regression guard | P0 | Medium |  SHIPPED 2026-09-08 as Development_Tools/Tests/EmbeddedZipCurrency.Tests.ps1 (03bb95706)
| Run the EXISTING `Deploy-Mods.Tests.ps1` suite against the 9-mod bundle and record the verdict in `.audit/` (the both-scripts folder-coverage invariant is already gated there - do not author a new check) | Audit trail | P1 | Quick |
| Record the shipped suite-mod versions in every repackage commit message, so a gitignored payload refresh is legible in history | Process | P1 | Quick |

Both gates must be demonstrated failing before they are trusted (fleet rule: a gate nobody has
watched fail is not a gate). Break them on a fixture copy under the scratchpad, never by mutating a
tracked file in place.

---

## Phase 1: Foundation

> Table-stakes hygiene. Most is already satisfied; the open rows are the documentation defects from
> this consolidation, which are cheap and should ship as a single docs commit.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Bump the EA 0.65 compatibility label (`README.md:5`, `:41`) to the current supported build or a range (game is EA 0.67h) | Docs honesty (W1) | P1 | Quick |
| "8-mod crispywhips family" -> "9-mod" (`FEATURES_IMPLEMENTED.md:9`); Homestead Perks is the 9th | Docs accuracy (W2) | P1 | Quick |
| Name the "Install & Update" suite installer in `ModInfo.json` Description - the flagship feature is currently absent from the player-facing text | Docs honesty (M1) | P1 | Quick |
| Align the `README.md:26` tab bullet with the accurate "UI Tabs" section (Analytics is under Settings; the sub-tab is "Unmapped") | Docs accuracy (W3) | P2 | Quick |
| Versions synced across ModInfo/Plugin.cs/README (all 2.1.32) | Version hygiene | done | - |
| Build 0-warn, bin/Release sync clean, no `ModLoaderVerison`/`ModEditorVersion` | Build hygiene | done | - |
| Localization: EN + `SimpCn.csv`, 2/2 keys, parity clean | Localization | done | - |
| CHANGELOG rollup for 2.1.25-2.1.32 (commit `a03608ddc`) | Docs honesty | done | - |

---

## Phase 2: Core Expansion

> The three additions that most directly harden or extend the flagship installer. All are pure
> `System.IO` / IMGUI work with no new dependency and no game-data contact.

### Suite-install verification pass  [SHIPPED 2026-09-08 in 03bb95706, plan row N2]
**What**: after `ModSuiteExtractor.Extract` completes, walk the ZIP entry list and `File.Exists` each
destination; render `[Install Incomplete]` on that row instead of a success badge when any entry is
missing.
**Why**: `Extract` currently reports success purely on "no exception thrown" (`ModSuiteExtractor.cs:84`),
and the locked-file path only `LogDebug`s "may orphan" and continues. That is exactly the
orphan/partial-write class the repo deploy rules warn about, and today a player gets a green badge on
a half-written plugin folder.
**Requires**: none.
**Complexity**: Medium

### Crash-safe destructive clean
**What**: replace clean-then-extract with either extract-to-temp + atomic swap on success, or a `.bak`
snapshot restored on failure. Also detect Deflate-compressed or comment-bearing ZIPs at *selection*
time rather than letting `MiniZip` throw mid-extract.
**Why**: `Extract` wipes the target plugin folder (preserving only `SpriteCache/`) before writing, so
a mid-extract failure leaves the player with the old install gone and the new one incomplete.
`MiniZip` assumes Stored-only entries and no ZIP comment, so a suite ZIP ever repacked with real
compression fails after the user has already clicked Apply.
**Requires**: pairs naturally with the verification pass above.
**Complexity**: Complex

### Dependency / SoftDependency graph validator  [plan row N6 IN FLIGHT 2026-09-08: built and deployed but uncommitted, owned by another session]
**What**: read each scanned mod's `[BepInDependency]` declarations (assembly metadata or `ModInfo.json`
dependency keys) and flag any declared dependency whose target GUID is absent from the installed set.
Surface as a "Dependencies" sub-section in the Conflicts tab.
**Why**: `ModScanner` already loads every plugin DLL, so the data is in hand. A missing
`crispywhips.CSFFModFramework` soft-dep is the single most common silent mis-wiring across this
repo's own mods, and MUM is the only tool positioned to show it to a player before they file a bug.
**Requires**: none.
**Complexity**: Medium

---

## Phase 3: Integration & Depth

> Cross-mod tooling. MUM is the natural consumer of metadata the rest of the suite already produces.

### Auto-seeded `KnownModRegistry`
**What**: generate the folder/display-name to Nexus-ID map at build time from each sibling mod's
`ModInfo.json` (plus an optional `NexusModId` key), instead of maintaining it by hand in
`KnownModRegistry.cs`. MUM's own `ModInfo.json` still has no `NexusModId`.
**Why**: a renamed mod folder currently drops silently out of the map with no failing check.
**Requires**: coordination with `Update-ModVersion.ps1` / `/export-to-repo`.
**Complexity**: Medium

### Stale-framework detector banner  [SHIPPED 2026-09-08 in a768c3ffd, plan row N7]
**What**: the framework (Nexus ID 30) is already in the registry and its latest version is already
fetched. Add an All-Mods banner when the installed framework version is behind: "content mods may
load against stale framework code".
**Why**: mirrors the repo's own deploy-framework-first invariant, and needs no new network call.
**Requires**: none.
**Complexity**: Quick

### Suite compatibility matrix
**What**: a `Documentation/compatibility-matrix.json` produced at release time listing tested version
combos; MUM flags an installed combination that was never tested together.
**Why**: the suite ships nine interdependent mods and a framework; players hit untested combinations
long before the maintainer does.
**Requires**: a release-time producer step; Phase 0's currency gate makes the version data trustworthy.
**Complexity**: Complex

### Rate-limit handling: manual re-check + auto-backoff  [manual re-check SHIPPED 2026-09-08 in a768c3ffd, plan row N5; auto-backoff still Medium-Term]
**What**: a "re-check rate-limited mods" action that re-runs `UpdateChecker` for HTTP-429'd rows only,
plus a single exponential-backoff retry inside `NexusApiClient` (`:327-330`).
**Why**: a check burst can 429 mods that then stay unchecked for the whole session with only a log
warning.
**Requires**: retry-count / delay-ceiling decision for the auto-backoff half.
**Complexity**: Medium

---

## Phase 4: Polish

> No card art exists or is wanted here (0 CardData). Polish for this mod means UI affordances and
> precision.

| Item | What | Complexity |
|------|------|------------|
| Persist the search filter | Save `_searchFilter` (`UpdateManagerUI.cs:26`) into `ModPreferences` so it survives an F3 close; the clear-"x" already ships | Quick |
| Sortable mod list | Data already on `InstalledModInfo`; needs a default-sort key decision (status vs alpha vs endorsement-desc) and a persistence decision | Medium |
| Conflict-detector precision | Re-key `_knownConflicts` by `BepInPlugin` GUID and demote name-substring guesses (`ConflictDetector.cs:197`) to a labeled "heuristic" tier | Medium |
| Drop the `//`-comment header from the mappings file | `ModMappingManager.CreateDefaultMappings()` writes non-standard JSON that must then be stripped on read; the README already documents the format | Quick |
| Delete the no-op `Start()` keybind hint | `Plugin.cs` `LogDebug` line, invisible by default and duplicated in README + config | Quick |
| Playthrough-test coverage | Add MUM rows to `/playthrough-test-plan`: suite extraction applies cleanly, a live Nexus check renders, the boot hook fires. This is the only route from 7 MEDIUM to any VERIFIED grade | Quick |

---

## Long-term Vision

> Where this mod should be at v3.0.

MUM's endpoint is the suite's front door: the one thing a player installs first, which then installs
and keeps current everything else, and which can explain a broken load order without a log file. The
Nexus-tracking half is essentially finished; the growth is all on the installer half - verified,
crash-safe extraction, a trustworthy embedded payload guarded by a gate rather than by memory, and
enough dependency/compatibility awareness to answer "why is this mod not working" in the dashboard
instead of in a Nexus comment thread. Self-update is the last structural gap: MUM can update all nine
suite mods but not itself, because its own DLL is locked while running.

**Potential major additions** (not yet justified - revisit after Phase 3):
- **MUM self-update via `.pending` staging** - extract the new build alongside the locked DLL and swap
  it before BepInEx locks on next start. High-risk (touches its own running assembly); needs a
  throwaway-install proof of concept and explicit destructive-action confirmation. Spec:
  `Documentation/Ideas/Mod_Update_Manager/Embedded_Mod_Suite_Integration.md`.
- **Read-only mod profiles** - snapshot the installed set as a named profile and diff current vs saved.
  Ship the read-only comparison only; a profile that drives the installer is state-changing and stays
  deferred.
- **Backup and rollback** - back up a mod folder before install/update, keep N backups per mod,
  restore from a chosen one. Generalizes Phase 2's crash-safe clean. Strictly opt-in.
- **In-game "What's New" popup on version bump** - the `GameLoad` postfix is already hooked; persist
  last-seen versions and queue a one-time summary from the cached Nexus changelog. Prefer an F3 "NEW"
  badge over interrupting boot.
- **Health chip per first-party row** - bake each sibling mod's `.audit/summary.md` Overall Score into
  a package-time JSON and show "Health: N/10".

Full inventory: `Documentation/Ideas/Mod_Update_Manager/IDEAS.md`. Standing guardrails from that file
still apply: every backup/rollback and file-deletion path is opt-in and audited before wiring, and MUM
stays zero-runtime-dependency.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| Any suite mod changes | Repackage (`/package-mod-suite` / `Pack-Suite.ps1`) BEFORE deploying MUM, and deploy MUM LAST - framework, then content mods, then repackage, then MUM |
| Every repackage | State which suite-mod versions shipped in the commit message; the payload is gitignored and otherwise leaves no trace |
| Any new suite mod, or any new `JsonDataLoader.DirToTypeName` folder type | Update the content-folder lists in BOTH `Pack-Suite.ps1` AND `Deploy-Mods.ps1`, plus `SuiteModRegistry.cs`, then run `Deploy-Mods.Tests.ps1` |
| Game version update | Bump the README compatibility label; MUM has no typed game-symbol surface, so the stale `lib/Assembly-CSharp-nstrip.dll` is vestigial and a refresh is not a blocker |
| After any Phase 2 item | Run `/audit-mod Mod_Update_Manager`, then `/consolidate-audit Mod_Update_Manager` |
| Before publishing | Run `/critical-analysis Mod_Update_Manager` and confirm the embedded-ZIP currency gate is green |

---

## Skill Cheatsheet for This Mod

```
/audit-mod Mod_Update_Manager          - full health check, updates .audit/
/critical-analysis Mod_Update_Manager  - adversarial review
/code-quality Mod_Update_Manager       - C# reliability scan (the main sub-audit for a utility mod)
/build-mod Mod_Update_Manager          - build Release DLL
/package-mod-suite [version]           - rebuild every suite mod, re-embed the ZIPs, bump MUM
/deploy-mods Mod_Update_Manager        - build + deploy (ALWAYS last in a batch deploy)
/update-mod-version Mod_Update_Manager <ver> - bump version in all 3 files
/export-to-repo Mod_Update_Manager     - push to public repo
```

*Not applicable to this mod (no CardData): `/repair-items`, `/repair-blueprints`,
`/audit-items`, `/audit-blueprints`, `/audit-structures`, `/audit-perks`, `/audit-images`.*

---

## Plan Reconciliation Log

### 2026-09-08 - Audit_Remediation_Plan (partial: 6 of 7 rows)
- **Verdict:** 6 of 7 rows shipped and committed; 1 row (N6) in flight. The Fixes table was
  empty by design (0 CRITICAL, 0 Design Gap at 10/10), so this plan was always the Near-Term
  feature list. Committed: N3 (ModInfo Description names the suite installer) in `34c656c44`;
  N1 (embedded-ZIP currency Pester gate) and N2 (post-extract completeness walk plus the
  `[Install Incomplete]` badge) in `03bb95706`; N4 (search filter persisted as a reserved
  `_searchFilter` key in the existing preferences file), N5 (`InstalledModInfo.RateLimited`
  set only in the 429 branch, `RunChecksCoroutine` shared by the full and filtered checks,
  My Mods banner plus Re-check button) and N7 (`DrawFrameworkStaleBanner`, Nexus id 30,
  compared with `VersionComparer.NeedsUpdate`) in `a768c3ffd`.
- **Evidence:** re-derived rather than carried on the plan journal's word - every row was
  re-read against shipped source on 2026-09-08 instead of being accepted from the plan's own
  Execution Status entries. N1 was additionally RUN, which is a differently-shaped check
  from grepping for its threshold: 22 tests, default mode 15 passed with 7 pinned skips,
  strict mode (`CSFF_EMBED_CURRENCY_STRICT=1`, which is what the release path sets) 15
  passed and 7 FAILED, each naming a stale embed. Its self-demonstration goes red on a
  scratchpad fixture rewritten older, green on a current one, and red on a 1.9.0-vs-1.10.0
  pair that proves the comparison is not a string compare. All 11 gate input files were
  fingerprinted either side of the run and none moved, so the result is readable rather
  than contaminated.
- **N6 (dependency / SoftDependency graph validator) is NOT claimed as shipped.** As of
  2026-09-08 19:09 local it exists only as uncommitted work on the main checkout, owned by
  another live session: `DependencyValidator.cs` and `PluginMetadataReader.cs` untracked,
  plus edits to `UpdateManagerUI.cs`, `UpdateChecker.cs`, both Localization CSVs and the
  four docs files. It does build - the Release DLL is newer than every source file and
  carries `DependencyValidator`, `PluginMetadataReader`, `DrawDependencyIssues` and
  `GetIssues` as UTF-8 metadata names. It took the metadata-only read the prompt pack
  required, walking the CLI tables and decoding the attribute arguments out of the `#Blob`
  heap, so no third-party assembly is loaded to draw a tab. That is a READING of a peer's
  in-flight tree, not a verification of it, and the row stays open until it is committed.
- **Known-stale embedded ZIPs, carried forward deliberately:** 7 of the 9 embedded suite
  ZIPs are behind their source mods and are PINNED as known-stale in the N1 gate, which the
  release path refuses to honour. The gap has widened since the pins were written
  (Community_Mod_Chest source is now 1.68.22 against an embed of 1.68.18; RepeatAction 2.1.5
  against 2.1.0), which is expected: each pin keys on the EMBEDDED version, so a source bump
  cannot expire it and only a repackage can. Clearing them means `/package-mod-suite`, a
  release operation this plan deliberately does not run.
- **Disposition:** the plan STAYS in `Documentation/Plans/` with N6 as its only row. The six
  shipped rows were pruned out of both the plan doc and its prompt pack in the same pass
  (lifecycle step 3), so the folder still answers "what needs building?". Archiving to
  `Documentation/Design/` is step 4 and is blocked on N6 becoming durable.
- **Verification debt now carried by the playthrough tracker:** T2.221 (N2 suite-install
  positive path), T2.222 (N4 filter survives an F3 close), T2.223 (N5 button gated on the
  rate-limited subset; a real 429 cannot be forced on demand), T2.224 (N7 banner appears
  only when the framework is actually behind). All four carry real steps, expected results
  and negative cases. N6 still needs an equivalent row, which is a hard gate on archiving.
  N1 correctly has no row: it is a repo test with no in-game step.
- **Not promoted, recorded so a later pass cannot re-promote it:** the Medium-Term rows
  (crash-safe destructive clean, conflict-detector GUID re-keying, sortable list, 429
  auto-backoff, suite install-order enforcement) each need a design decision and stay in
  `Documentation/Ideas/Mod_Update_Manager/IDEAS.md` carrying their stated reason. Extending
  suite-completeness verification to `Deploy-Mods.ps1` was dropped as ALREADY GATED by
  `Development_Tools/Tests/Deploy-Mods.Tests.ps1`.

### 2026-09-08 - Audit_Remediation_Plan (final: row N6, plan closed)

- **Verdict:** the plan's LAST open row is shipped, so all 7 of 7 rows (N1-N7) are built and
  committed and no code work remains. N6, the dependency / SoftDependency graph validator,
  landed in `5039b75db` (13 files, +969/-4) on top of `c6cb0da66`. It was built by a different
  session from the one writing this record; the two lanes were split deliberately, with the
  builder owning every `Mod_Update_Manager/` source file plus the N6 tracker row, and this
  session owning the plan doc, this log, and the archive.
- **Evidence:** the branch-plan gate in the prompt pack allowed a STOP-if-blocked outcome, and
  it resolved to Option B, a metadata-only read, decided by measurement before any UI was
  written. `PluginMetadataReader.cs` (645 lines) walks PE to CLI header to metadata root to the
  `#~` tables to `CustomAttribute` rows and decodes the `#Blob` fixed arguments, so no
  third-party assembly is loaded, executed or locked in order to draw a tab. Live scan of the
  real BepInEx tree with the built assembly: 37 DLLs read (25 plugins, 12 core), 0 unreadable,
  21 installed plugin GUIDs, 19 declarations across 13 plugins, about 180 ms cold, cached and
  invalidated on rescan. Build 0 warnings / 0 errors. Localization parity CLEAN 11/11 and
  `Localization-EnglishColumnCjk` 22/22. Deployed MUM only, no suite repackage. One genuinely
  unsatisfied declaration exists on the reference install, which is the feature doing its job:
  WikiMod soft-depends on the bare GUID `ModCore` while the installed Pikachu ModCore registers
  as `Pikachu.CSFF.ModCore`. Independently checked by this session before the archive: the
  README's claim that the sub-section is gated behind `ShowConflictWarnings` is accurate,
  because `DrawConflicts` early-returns on that config before reaching `DrawDependencyIssues`.
- **Disposition:** ARCHIVE. `Documentation/Plans/Mod_Update_Manager/Audit_Remediation_Plan.md`
  moved to `Documentation/Design/Mod_Update_Manager_Audit_Remediation_As_Built.md`, and its
  drained prompt pack deleted, per lifecycle step 4. Both Done conditions hold: no code work
  remains, and every awaiting-verification item has a real tracker row.
- **Verification debt, all of it now in the playthrough tracker:** T2.221 (N2 suite-install
  completeness badge), T2.222 (N4 search filter survives an F3 close), T2.223 (N5 re-check
  button gated on the rate-limited subset), T2.224 (N7 stale-framework banner), T2.227 (N6
  Dependencies sub-section). All five are `pending` and none can be advanced by an agent. N1
  correctly has no row: it is a repo Pester gate with no in-game step.
- **Citations repointed in this pass:** the 5 tracker rows above plus
  `Development_Tools/Tests/EmbeddedZipCurrency.Tests.ps1`, `Mod_Update_Manager/DependencyValidator.cs`
  and three comment lines in `Mod_Update_Manager/UpdateManagerUI.cs` all cited the old plan
  path and now cite the archive. The 6 citations in `Mod_Update_Manager/CHANGELOG.md` were
  deliberately LEFT pointing at the old path: they are dated provenance entries describing what
  was true when each row shipped, and rewriting a dated record to match a later move is the
  thing the reference-sweep rule forbids. This log entry is where a reader following one of
  them finds the current location.
- **Carried forward, NOT closed by this plan:** 7 of the 9 embedded suite ZIPs remain behind
  their source mods and stay PINNED as known-stale in the N1 gate, which the release path
  refuses to honour (`CSFF_EMBED_CURRENCY_STRICT=1`). Each pin keys on the EMBEDDED version, so
  a source bump cannot expire one and only `/package-mod-suite` can. That is a release
  operation this plan deliberately never ran. Also recorded and not acted on: two installed
  DLLs register the same `Pikachu.CSFF.ModCore` GUID, which belongs to the Medium-Term
  conflict-detector GUID re-keying idea; and MUM has no runtime CSV reader, so every IMGUI
  string renders from a hardcoded English const and the `SimpCn.csv` rows have never reached a
  Chinese player. That last one is pre-existing across N2, N5, N6 and N7 alike, is not a defect
  in any of them, and is filed as a backlog row in
  `Documentation/Ideas/Mod_Update_Manager/IDEAS.md` rather than left in a chat log.
