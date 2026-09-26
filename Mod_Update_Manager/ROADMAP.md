# Roadmap: Mod Update Manager
Version at time of writing: 2.1.58 (source carries an `[Unreleased]` CHANGELOG section of fixes on top)
Date: 2026-09-25
Audit score: 10/10 (release-ready in source, per `.audit/summary.md` consolidated 2026-09-25)

## Current State

**Theme**: A standalone BepInEx utility for players who run modpacks. It scans installed CSFF mods,
checks them against Nexus Mods for updates, and ships a one-click "Install & Update" tab that
extracts the 9-mod crispywhips suite (CSFF Mod Framework, Advanced Copper Tools, Herbs & Fungi, Water
Driven Infrastructure, Community Mod Chest, Homestead Perks, Repeat Action, Quick Transfer, Skill
Speed Boost) straight out of ZIPs embedded in its own DLL. Sirus23 Mod Collection is deliberately not
bundled. It never downloads, deletes, or auto-updates anything from Nexus.

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom card images. UTILITY mod:
23 source `.cs` files, 9 embedded suite ZIPs (`Resources/EmbeddedMods/`), 1 IMGUI dashboard (F3),
1 Harmony postfix (`GameLoad.LoadMainGameData`), 11 localization keys (EN + SimpCn, parity clean).

**Stability**: 10/10 in source - 0 open CRITICAL, 0 DESIGN GAP, 0 open WARNING. The 2026-09-25
critical-analysis and code-quality passes found one CRITICAL-class defect (Select All + Apply
downgraded a newer install) and seven warnings (wrong Steam app id on the relaunch button, a
three-mod gap in the Nexus registry, the README build pin, a wipe that ran before the archive was
validated, no Nexus request timeout, checked ids auto-persisted into the user mappings file). All are
fixed in source (`809368100`, `ad111142c`, `f75e4ea60`) and re-read on disk at consolidation. **The
published 2.1.58 still carries them** until the next MUM pack ships the `[Unreleased]` entries: its
public copy still runs `steam://run/1413240`.

**Open work**: No retrospective names Mod_Update_Manager, `MUM` or `mod_update_manager`
(`Documentation/Retrospectives/INDEX.md` has no row for it). The audit-remediation plan (N1-N7) closed
2026-09-08 and is archived at `Documentation/Design/Mod_Update_Manager_Audit_Remediation_As_Built.md`.

**Framework compliance**: N/A by design, and this is a deliberate architectural stance, not a gap.
MUM declares no `[BepInDependency]` on CSFFModFramework, ModCore, or ModLoader and uses zero Tier 2
services (`ActionRouter` / `SpawnService` / `TickEvents` / `ContentModPlugin` - grep returns 0 hits;
the `Dependency*` source files READ other mods' `[BepInDependency]` declarations for the Conflicts tab,
they do not declare one). Its mod-local `SimpleJson`, `MiniZip`, and `ModScanner` are intentional
zero-dependency implementations, NOT framework-service duplication to be "fixed". Do not migrate.

**Known risk surface** (unscored):
- `Resources/EmbeddedMods/*.zip` is gitignored, so embedded-payload drift is invisible to every git
  diff and code-level audit. It is GATED: `Development_Tools/Tests/EmbeddedZipCurrency.Tests.ps1` fails
  (in the strict mode the release path uses) when an embed lags its source. 6 embeds are pinned as
  known-stale today (`$knownStaleEmbeds`: ACT 1.16.8, H&F 1.13.6, CMC 1.68.41, WDI 1.11.2, SSB 1.10.5,
  RepeatAction 2.1.5); only `/package-mod-suite` expires a pin.
- Hard-coded external identifiers (the Steam app id, the Nexus registry) pass every build and
  preflight when wrong. Both are now gated by `Development_Tools/Tests/MUM-ExternalIdentifiers.Tests.ps1`
  (`c36ffc60f`, `f75e4ea60`), with self-demonstrations on the defects MUM actually shipped.
- In-game verification debt: T2.221, T2.222, T2.223, T2.224 and T2.227 are `pending` in the tracker's
  `items` map. T2.263, the only in-game check of the Apply skip and the Steam relaunch, is `pending`
  but filed in `confirmedItems`, so the rendered checklist does not carry it (summary.md P1).

---

## Phase 0: Stabilize

> Source is at 10/10 with no open retrospective, so there is no code to stabilize. What remains is
> getting the source fixes to players and making their verification reachable.

| Item | Type | Priority | Status |
|------|------|----------|--------|
| Ship the `[Unreleased]` fixes: next MUM pack (framework, then content mods, then `/package-mod-suite`, then MUM last). Closes the published build's downgrade, wrong relaunch id, registry gap, missing timeout and auto-persist, and expires the 6 embed pins | Release | P0 | OPEN - a release operation, not code; the MUM-last order is enforced by `Deploy-Mods.Tests.ps1` for the skill text |
| Move T2.263 from `confirmedItems` to `items` in `.claude/playthrough-test-status.json` | Tracker fix | P0 | OPEN - Quick. As filed, the only in-game check of the Apply skip and the relaunch never reaches a human |
| Pester gate: FAIL when any embedded suite ZIP's version lags its source mod | Regression guard | done | SHIPPED 2026-09-08 (`03bb95706`, `EmbeddedZipCurrency.Tests.ps1`) |
| Gate hard-coded external identifiers (Nexus registry vs `nexus-mods.json`, every `steam://run` literal vs the CSFF app id) | Regression guard | done | SHIPPED 2026-09-25 (`c36ffc60f`, `f75e4ea60`, `MUM-ExternalIdentifiers.Tests.ps1`) |
| Record shipped suite-mod versions in every repackage commit / CHANGELOG entry | Process | done | ADOPTED - CHANGELOG [2.1.57]/[2.1.58] list the refreshed embeds by version |

---

## Phase 1: Foundation

> Table-stakes hygiene. Everything the 2026-09-05 roadmap listed here has shipped; the build pin is
> now letterless, and the one remaining choice is whether to drop it to a range.

| Item | Type | Priority | Status |
|------|------|----------|--------|
| End the build-pin drift: the letterless `EA 0.68` (`f75e4ea60`) holds across hotfixes but moves at 0.69; advertise a range (`EA 0.68 and later`) instead, since MUM's only game touchpoint is the stable `GameLoad.LoadMainGameData` postfix | Docs honesty (recurring, 5 recurrences) | P1 | PARTLY DONE - letter drift ended; the range is a Quick docs decision. The parsed per-mod chip is the richer alternative (Phase 3) |
| Regenerate `.audit/feature-map.md` (`/feature-map Mod_Update_Manager`) | Audit hygiene | P1 | OPEN - Quick. It dates from v2.1.32 and names none of the dependency validator, install-incomplete badge, rate-limit re-check, framework-stale banner or persisted filter |
| Name the "Install & Update" suite installer in `ModInfo.json` Description | Docs honesty | done | SHIPPED (`34c656c44`, plan row N3) |
| Versions synced across ModInfo/Plugin.cs/README (all 2.1.58) | Version hygiene | done | preflight 2026-09-25 |
| Build 0-warn, bin/Release sync clean, no `ModLoaderVerison`/`ModEditorVersion` | Build hygiene | done | preflight 2026-09-25 |
| Localization: EN + `SimpCn.csv`, 11/11 keys, parity clean | Localization | done | See the localization-reader decision in Phase 4 |

---

## Phase 2: Core Expansion

> Hardening the flagship installer. The 2026-09-25 fixes closed most of the destructive-path risk;
> one loss case and one user-file residual remain.

### Apply never downgrades, never wipes what it cannot replace  [SHIPPED 2026-09-25 in `ad111142c`, unreleased]
**What**: `ApplySuiteUpdates` re-reads each installed version at apply time and skips a row whose
embed is older or `[Unknown]` (`UpdateManagerUI.cs:924-936`), naming skipped rows in a done line that
now renders under the restart banner; `ModSuiteExtractor` reads and validates the whole archive
(`MiniZip.Validate`) before deleting anything, and the post-extract verify compares file sizes.
**Status**: in source; ships at the next MUM pack; in-game check T2.263 (misfiled, see Phase 0).

### Crash-safe destructive clean (narrowed)
**What**: extract to a temp dir and swap on success, or snapshot to `.bak` and restore on failure.
**Why**: the remaining loss case is an I/O failure AFTER the wipe (disk full, a file locked by
antivirus mid-write): the old install is gone and `[Install Incomplete]` reports the new one short.
The archive-unreadable case, and the Deflate / comment-bearing ZIP case the previous roadmap wanted
detected at selection time, now fail before the wipe, so that half is closed.
**Requires**: a temp-dir-swap vs `.bak` decision; opt-in per the standing MUM guardrail. The existing
plan-then-wipe split in `ModSuiteExtractor.Extract` (`:48-63`, then `:66-78`) is where it slots in.
**Complexity**: Medium

### Retire auto-persisted mapping rows left by earlier versions
**What**: on load, drop a `ModUpdateManager_Mappings.json` row whose id equals the current
`KnownModRegistry` id for the same key; keep every row that differs (a real user override).
**Why**: versions before `ad111142c` wrote every checked id into that file, which outranks the
registry, so those rows would freeze today's ids against any later registry correction. Dropping only
registry-identical rows changes no lookup today. No shipped wrong registry id is known, so this is
latent until the registry is next corrected.
**Requires**: a decision on whether MUM may rewrite a user config file on load (IDEAS.md guardrail).
**Complexity**: Quick

### Suite-install verification pass  [SHIPPED 2026-09-08 in `03bb95706`, plan row N2; size check added `ad111142c`]
**Status**: shipped; verification debt T2.221.

### Dependency / SoftDependency graph validator  [SHIPPED 2026-09-08 in `5039b75db`, plan row N6]
**Status**: shipped; verification debt T2.227.

---

## Phase 3: Integration & Depth

> Cross-mod tooling. MUM is the natural consumer of metadata the rest of the suite already produces.

### Stale-framework detector banner  [SHIPPED 2026-09-08 in `a768c3ffd`, plan row N7]
**Status**: shipped; verification debt T2.224.

### Nexus request timeout  [SHIPPED 2026-09-25 in `ad111142c`, unreleased]
**Status**: `REQUEST_TIMEOUT_SECONDS = 30` on all three Nexus requests (`NexusApiClient.cs:53`), so a
request that never answers counts as a failed check instead of stalling checks for the session.

### Rate-limit handling: manual re-check (shipped) + auto-backoff (open)
**What**: the manual "re-check rate-limited mods" action SHIPPED 2026-09-08 (`a768c3ffd`, plan row N5,
debt T2.223). Still open: a single exponential-backoff retry in the 429 branch of `NexusApiClient`
(`:340-345`, which today flags the row and warns, with no retry).
**Why**: a check burst can 429 mods that then stay unchecked for the whole session.
**Requires**: retry-count / delay-ceiling decision.
**Complexity**: Medium

### Per-mod "compatible game build" chip
**What**: parse a `Compatible with ... EA 0.XX` token out of the already-fetched Nexus changelog and
render a per-mod chip, yellow when the token build < the running game build.
**Why**: turns the hand-maintained build pin (Phase 1) into surfaced data, and helps players judge
third-party mods too.
**Requires**: a clean source for the running game build (investigate `GameTechInfo`, which the
2026-09-25 critical-analysis read at `.decomp/GameTechInfo.cs`).
**Complexity**: Medium

### Build-time `KnownModRegistry` generation
**What**: generate the folder/display-name to Nexus-ID map at build time from
`Development_Tools/Nexus/nexus-mods.json` and each sibling `ModInfo.json`, instead of editing
`KnownModRegistry.cs` by hand. MUM's own `ModInfo.json` still has no `NexusModId`.
**Why**: the DETECTION half shipped (`c36ffc60f`): `MUM-ExternalIdentifiers.Tests.ps1` now fails when a
published studio mod has no registry row under its deploy folder, which was the original reason for
this item. Generation would remove the hand edit that gate forces on every new publish.
**Requires**: coordination with `Update-ModVersion.ps1` / `/export-to-repo`.
**Complexity**: Medium

### Suite compatibility matrix
**What**: a `Documentation/compatibility-matrix.json` produced at release time listing tested version
combos; MUM flags an installed combination that was never tested together.
**Why**: the suite ships nine interdependent mods and a framework; players hit untested combinations
long before the maintainer does.
**Requires**: a release-time producer step; the embed currency gate makes the version data trustworthy.
**Complexity**: Complex

---

## Phase 4: Polish

> No card art exists or is wanted here (0 CardData). Polish for this mod means UI affordances and precision.

| Item | What | Status / Complexity |
|------|------|---------------------|
| Persist the search filter | `_searchFilter` saved into `ModPreferences` so it survives an F3 close | SHIPPED 2026-09-08 (plan row N4, debt T2.222) |
| Drop the `//`-comment header from the mappings file | `CreateDefaultMappings` writes bare JSON (`ModMappingManager.cs:91-108`); `SimpleJson.cs:46-52` still strips `//` lines so old files parse | SHIPPED 2026-06-17 (`35642c7ca`, MUM 2.1.1). Listed OPEN here until 2026-09-25 |
| Framework-first install order | `ApplySuiteUpdates` puts `CSFF_Mod_Framework` first whenever it is selected (`UpdateManagerUI.cs:906-913`) | SHIPPED 2026-07-09 (`650625393`) as a silent reorder. Open only as an optional "framework installed first" note in the restart banner - Quick |
| MUM runtime-localization decision | No `LocalizationManager`/`GetText` read exists in any MUM `.cs`, so the 11 `SimpCn.csv` rows never render. Either route IMGUI strings through the dictionary, OR declare MUM English-only, delete the dead rows, and exempt it from the parity gate. Do NOT do half of the first option | OPEN - Medium (design decision) |
| Sortable mod list | Fixed sorts only (`UpdateManagerUI.cs:435` by name, `:989` favorites then name); needs default-sort-key + persistence decision | OPEN - Medium |
| Conflict-detector precision | `_knownConflicts` is name-keyed (`ConflictDetector.cs:43`, one pair at `:204`); re-key by `BepInPlugin` GUID and demote name guesses to a labeled "heuristic" tier (two DLLs share the `Pikachu.CSFF.ModCore` GUID, a real case) | OPEN - Medium |
| Playthrough-test coverage | 6 MUM rows, all `pending`: T2.221-T2.224 and T2.227 in `items`, T2.263 misfiled (Phase 0). They are the only route from 7 MEDIUM feature groups to any VERIFIED grade | OPEN - human-gated |

---

## Long-term Vision

> Where this mod should be at v3.0.

MUM's endpoint is the suite's front door: the one thing a player installs first, which then installs
and keeps current everything else, and which can explain a broken load order without a log file. The
Nexus-tracking half is essentially finished, and the installer half now validates before it deletes,
never moves a mod backwards, verifies what it wrote, and knows about dependencies. What remains is a
reversible wipe, the compatibility surface (range/chip/matrix) that ends the version-label chore, and
the one structural gap left: self-update. MUM can update all nine suite mods but not itself, because
its own DLL is locked while running.

**Potential major additions** (not yet justified - revisit after the open Phase 2/3 items):
- **MUM self-update via `.pending` staging** - extract the new build alongside the locked DLL and swap
  it before BepInEx locks on next start. High-risk (touches its own running assembly); needs a
  throwaway-install proof of concept and explicit destructive-action confirmation. Spec:
  `Documentation/Ideas/Mod_Update_Manager/Embedded_Mod_Suite_Integration.md`.
- **Read-only mod profiles** - snapshot the installed set as a named profile and diff current vs saved.
  Ship the read-only comparison only; a profile that drives the installer is state-changing and deferred.
- **Backup and rollback** - back up a mod folder before install/update, keep N backups per mod, restore
  from a chosen one. Generalizes Phase 2's crash-safe clean. Strictly opt-in.
- **In-game "What's New" popup on version bump** - the `GameLoad` postfix is already hooked; persist
  last-seen versions and queue a one-time summary from the cached Nexus changelog. Prefer an F3 "NEW"
  badge over interrupting boot.
- **Health chip per first-party row** - bake each sibling mod's `.audit/summary.md` Overall Score into a
  package-time JSON and show "Health: N/10".

Full inventory: `Documentation/Ideas/Mod_Update_Manager/IDEAS.md`. Standing guardrails from that file
still apply: every backup/rollback and file-deletion path is opt-in and audited before wiring, and MUM
stays zero-runtime-dependency. Note that IDEAS.md still lists the framework-first install order as
unbuilt; it shipped in `650625393` (see Phase 4).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| Any suite mod changes | Repackage (`/package-mod-suite` / `Pack-Suite.ps1`) BEFORE deploying MUM, and deploy MUM LAST - framework, then content mods, then repackage, then MUM |
| A suite mod's source moves ahead of its embed with no MUM release | Pin it in `$knownStaleEmbeds` keyed on the EMBEDDED version; the next repackage expires it |
| Every repackage | State which suite-mod versions shipped in the commit message and CHANGELOG; the payload is gitignored and otherwise leaves no trace |
| Any new suite mod, or any new `JsonDataLoader.DirToTypeName` folder type | Update the content-folder lists in BOTH `Pack-Suite.ps1` AND `Deploy-Mods.ps1`, plus `SuiteModRegistry.cs`, then run `Deploy-Mods.Tests.ps1` |
| A studio mod gets a Nexus page | Add it to `Development_Tools/Nexus/nexus-mods.json` AND `KnownModRegistry.cs`; `MUM-ExternalIdentifiers.Tests.ps1` fails while `nexus-mods.json` names a published mod the registry lacks or maps to a different id (it does not check the reverse direction) |
| Game version update | Re-check the README build line (letterless `EA 0.68` until 0.69, or ship the range/chip); MUM has no typed game-symbol surface, so the stale `lib/Assembly-CSharp-nstrip.dll` is vestigial and a refresh is not a blocker |
| After any Phase 2 item | Run `/audit-mod Mod_Update_Manager`, then `/consolidate-audit Mod_Update_Manager` |
| Before publishing | Run `/critical-analysis Mod_Update_Manager` and confirm the embedded-ZIP currency gate is green in strict mode |

---

## Skill Cheatsheet for This Mod

```
/audit-mod Mod_Update_Manager          - full health check, updates .audit/
/critical-analysis Mod_Update_Manager  - adversarial review
/code-quality Mod_Update_Manager       - C# reliability scan (the main sub-audit for a utility mod)
/feature-map Mod_Update_Manager        - regenerate the feature-level regression anchor
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
