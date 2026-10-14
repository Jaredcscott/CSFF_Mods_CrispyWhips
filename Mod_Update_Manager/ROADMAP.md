# Roadmap: Mod Update Manager
Version at time of writing: 2.1.63, unpublished (Nexus has 2.1.61); it carries 2.1.62's changes and the
round-2 plan's phases A, G and B (phase C is repo tooling with no player-facing change)
Date: 2026-10-11 (Current State refreshed by `/consolidate-audit`; phase rows reconciled against source 2026-10-02/03; the roadmap was written 2026-09-25)
Audit score: 10/10 at the `.audit/summary.md` consolidation of 2026-10-11 (critical-analysis + code-quality both 2026-10-11, source at `663c8122e`)

## Current State

**Theme**: A standalone BepInEx utility for players who run modpacks. It scans installed CSFF mods,
checks them against Nexus Mods for updates, and ships a one-click "Install & Update" tab that
extracts the 15-mod crispywhips suite straight out of ZIPs embedded in its own DLL: the nine released
mods (CSFF Mod Framework, Advanced Copper Tools, Herbs & Fungi, Water Driven Infrastructure, Community
Mod Chest, Homestead Perks, Repeat Action, Quick Transfer, Skill Speed Boost) plus six newer mods
labelled (Pre-release) in their rows (Forest Lore, Far Territory, Harsh Wilds, Partner Overhaul,
Sirus23 Mod Collection, Utility Belt). It never downloads, deletes, or auto-updates anything from
Nexus. (The "9-mod" / "Sirus23 not bundled" framing in earlier revisions is retired; it was the H1/H2/H4
feature-count under-claim fixed in source by `663c8122e`, 2026-10-11.)

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom card images. UTILITY mod:
52 tracked `.cs` files (51 at the root plus `Patcher/GameLoadPatch.cs`), 15 embedded suite ZIPs
(`Resources/EmbeddedMods/`: 9 released + 6 pre-release), 1 IMGUI dashboard (F3), 1 Harmony postfix
(`GameLoad.LoadMainGameData`), 247 localization keys (EN + SimpCn, parity CLEAN 247/247 by
`Development_Tools/Check-LocalizationParity.ps1`; `MumText` now reads them at runtime - round-2 feature 22).

**Stability**: The 2026-09-25 critical-analysis and code-quality passes found one CRITICAL-class
defect (Select All + Apply downgraded a newer install) and seven warnings (wrong Steam app id on the
relaunch button, a three-mod gap in the Nexus registry, the README build pin, a wipe that ran before
the archive was validated, no Nexus request timeout, checked ids auto-persisted into the user mappings
file). All shipped fixed in 2.1.59 and are on Nexus since 2.1.61 (2026-09-26). Since then 2.1.62 (a
staged, crash-safe suite install, the Tested line, one 429 retry, GUID-confirmed conflicts, mapping-row
retirement) and the round-2 plan's phases A, G, B, C (2026-10-02) and D (2026-10-03) are built and
gated in source; none has reached players, because 2.1.63 waits on a suite repack that waits on the
suite mods' own releases (Plan Reconciliation Log, 2026-10-02 and 2026-10-03).

**Open work**: no code is left. The round-2 plan (features 1-23, phases A-G) drained 2026-10-03 and is `Documentation/Design/Mod_Update_Manager_Round2_As_Built.md`; every feature is built and gated, and the in-game checks are 30 pending tracker rows (T2.1250-T2.1265, T2.1268-T2.1272 and the older T2.222-T2.227, T2.263, T2.370-T2.373). What remains is release: the suite repack and the MUM deploy wait on the nine suite mods' own releases. No retrospective names Mod_Update_Manager, `MUM` or
`mod_update_manager` (`Documentation/Retrospectives/INDEX.md` has no row for it). The audit-remediation
plan (N1-N7) closed 2026-09-08 (`Documentation/Design/Mod_Update_Manager_Audit_Remediation_As_Built.md`)
and the round-1 content plan on 2026-09-26 (`Documentation/Design/Mod_Update_Manager_Content_Update_As_Built.md`).

**Framework compliance**: N/A by design, and this is a deliberate architectural stance, not a gap.
MUM declares no `[BepInDependency]` on CSFFModFramework, ModCore, or ModLoader and uses zero Tier 2
services (`ActionRouter` / `SpawnService` / `TickEvents` / `ContentModPlugin` - grep returns 0 hits;
the `Dependency*` source files READ other mods' `[BepInDependency]` declarations for the Conflicts tab,
they do not declare one). Its mod-local `SimpleJson`, `MiniZip`, and `ModScanner` are intentional
zero-dependency implementations, NOT framework-service duplication to be "fixed". Do not migrate.

**Known risk surface** (unscored):
- `Resources/EmbeddedMods/*.zip` is gitignored, so embedded-payload drift is invisible to every git
  diff and code-level audit. It is GATED: `Development_Tools/Tests/EmbeddedZipCurrency.Tests.ps1` fails
  (in the strict mode the release path uses) when an embed lags its source. 8 of the 9 embeds are
  pinned as known-stale on 2026-10-02 (`$knownStaleEmbeds` keys on the embedded version: framework
  2.27.1, ACT 1.17.0, H&F 1.14.0, WDI 1.11.3, CMC 1.69.1, HSP 1.2.4, RepeatAction 2.1.6, SSB 1.10.6;
  Quick Transfer 1.9.0 matches its source); only `/package-mod-suite` expires a pin.
  `Development_Tools/Tests/MUM-EmbedContents.Tests.ps1` (plan feature 13) adds the DLLs inside each
  embed and the built DLL's resources.
- Hard-coded external identifiers (the Steam app id, the Nexus registry) pass every build and
  preflight when wrong. Both are now gated by `Development_Tools/Tests/MUM-ExternalIdentifiers.Tests.ps1`
  (`c36ffc60f`, `f75e4ea60`), with self-demonstrations on the defects MUM actually shipped. Since
  `fd222e4d8` (round-2 feature 14) the studio rows of the Nexus registry are generated from
  `Development_Tools/Nexus/nexus-mods.json` and each `ModInfo.json` by
  `Development_Tools/Update-KnownModRegistry.ps1`, and the same test runs its `-Check` and reads the
  generated block back in both directions.
- In-game verification debt, all `pending` in the tracker's `items` map: T2.222, T2.223, T2.224,
  T2.227 and T2.263 (which absorbed T2.221 in the r41 review), T2.370 to T2.373 (2.1.62), and
  T2.1250 to T2.1256 (round-2 phases A and G) and T2.1257 to T2.1262 (round-2 phase B), filed
  2026-10-02, and T2.1263 to T2.1265 (round-2 phase D), filed 2026-10-03. Phase C names no in-game
  check.

---

## Phase 0: Stabilize

> No open retrospective, so there is no code to stabilize. What remains is getting the built
> releases to players and making their verification reachable.

| Item | Type | Priority | Status |
|------|------|----------|--------|
| Ship the 2026-09-25 `[Unreleased]` fixes (downgrade, wrong relaunch id, registry gap, missing timeout, auto-persist) | Release | done | SHIPPED in 2.1.59, on Nexus since 2.1.61 (2026-09-26) |
| Release 2.1.63 (2.1.62 plus the round-2 plan's phases A and G): framework, the other suite mods, then `/package-mod-suite` keeping 2.1.63 while it is unpublished (its Step 2), then MUM last; expires the 8 embed pins | Release | P0 | OPEN - release-ready in source 2026-10-02 (gates green, version synced, CHANGELOG one dated section); the repack waits on the suite mods' own releases, every one of whose sources is ahead of Nexus (Plan Reconciliation Log, 2026-10-02) |
| Move T2.263 from `confirmedItems` to `items` in `.claude/playthrough-test-status.json` | Tracker fix | done | DONE 2026-09-25 (the `/consolidate-audit` pass moved it) |
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
| Regenerate `.audit/feature-map.md` (`/feature-map Mod_Update_Manager`) | Audit hygiene | P1 | OPEN - Quick. It dates from v2.1.32 and names none of the dependency validator, install-incomplete badge, rate-limit re-check, framework-stale banner or persisted filter. Scheduled: the round-2 plan's Release tail runs `/audit-mod` then `/consolidate-audit` (which regenerates it) with the phase F release |
| Name the "Install & Update" suite installer in `ModInfo.json` Description | Docs honesty | done | SHIPPED (`34c656c44`, plan row N3) |
| Versions synced across ModInfo/Plugin.cs/README (all 2.1.63) | Version hygiene | done | read 2026-10-02 |
| Build 0-warn, bin/Release sync clean, no `ModLoaderVerison`/`ModEditorVersion` | Build hygiene | done | preflight 2026-09-25 |
| Localization: EN + `SimpCn.csv`, 25/25 keys, parity CLEAN | Localization | done | 2026-10-02; see the localization-reader decision in Phase 4 |

---

## Phase 2: Core Expansion

> Hardening the flagship installer. The destructive-path items below are all shipped; what is left
> for the installer is the round-2 plan's phase B (features 7 to 12).

### Apply never downgrades, never wipes what it cannot replace  [SHIPPED 2026-09-25 in `ad111142c`, published in 2.1.59]
**What**: Apply re-reads each installed version at apply time and skips a row whose embed is older
or `[Unknown]`, naming skipped rows in a done line that renders under the restart banner; the rules
moved verbatim into `SuiteBatch.Plan` in 2.1.63 (round-2 feature 3, `af488f19e`), where
`MUM-CompiledLogic.Tests.ps1` pins each one.
**Status**: published; in-game checks T2.263 and, on the 2.1.63 build, T2.1253.

### Crash-safe destructive clean  [SHIPPED in 2.1.62, `SuiteInstaller.cs`; unpublished until 2.1.63 ships]
**What**: each mod is extracted into `<Folder>__mum_staging`, size-verified there, and swapped in by
renaming the installed folder to `<Folder>__mum_old`; a failed swap is undone, and an interrupted
run's leftovers are cleared or restored first (round-1 as-built,
`Documentation/Design/Mod_Update_Manager_Content_Update_As_Built.md`).
**Status**: in-game check T2.370. The loader-visibility defect it left (a leftover staging or old
folder sits where BepInEx loads from) is round-2 feature 7, and a failed row's retry without a
restart shipped in 2.1.63 (feature 3, T2.1252).

### Retire auto-persisted mapping rows left by earlier versions  [SHIPPED in 2.1.62, `ModMappingManager.RetireRegistryDuplicates`]
**What**: on load, a mod's `ModUpdateManager_Mappings.json` rows are dropped when every one of them
equals the current `KnownModRegistry` id; a row that differs (a real user override) is kept.
**Status**: shipped; static, no in-game row (round-1 as-built, feature 5).

### Suite-install verification pass  [SHIPPED 2026-09-08 in `03bb95706`, plan row N2; size check added `ad111142c`]
**Status**: shipped; its verification debt (T2.221) was merged into T2.263 in the r41 review.

### Dependency / SoftDependency graph validator  [SHIPPED 2026-09-08 in `5039b75db`, plan row N6]
**Status**: shipped; verification debt T2.227.

---

## Phase 3: Integration & Depth

> Cross-mod tooling. MUM is the natural consumer of metadata the rest of the suite already produces.

### Stale-framework detector banner  [SHIPPED 2026-09-08 in `a768c3ffd`, plan row N7]
**Status**: shipped; verification debt T2.224.

### Nexus request timeout  [SHIPPED 2026-09-25 in `ad111142c`, published in 2.1.59]
**Status**: `REQUEST_TIMEOUT_SECONDS = 30` on every Nexus request (`NexusApiClient`), so a request that
never answers counts as a failed check instead of stalling checks for the session.

### Rate-limit handling: manual re-check + one automatic retry  [both SHIPPED]
**What**: the manual "re-check rate-limited mods" action shipped 2026-09-08 (`a768c3ffd`, plan row N5,
debt T2.223); one retry after 5 s in the 429 branch of `NexusApiClient.GetModInfoCoroutine` shipped in
2.1.62 (debt T2.372). Since 2.1.63 the Re-check button also bypasses the 24-hour cache (round-2
feature 4).

### Per-mod "compatible game build" chip  [SHIPPED in 2.1.62, `CompatibilityChip.cs`]
**What**: the newest `Compatible with ... EA x.y` line in a fetched Nexus changelog is shown under it
as "Tested: EA x.y", rated against the running game version (debt T2.371). 2.1.63's changelog reader
(round-2 feature 2) no longer drops a version whose notes hold a bracket, so the line can read it
(debt T2.1251). The README build pin (Phase 1) still stands beside it.

### Build-time `KnownModRegistry` generation  [SHIPPED 2026-10-02 in `fd222e4d8`, round-2 feature 14; repo tooling, no player-facing change]
**What**: the folder/display-name to Nexus-ID map for the studio's own published mods is generated from
`Development_Tools/Nexus/nexus-mods.json` and each sibling `ModInfo.json` by
`Development_Tools/Update-KnownModRegistry.ps1`, which rewrites only the rows between the
`// <studio-mods ...>` markers in `KnownModRegistry.cs` and the `SelfNexusModId` constant between the
`// <studio-self-id ...>` markers (round-2 feature 23's constant); `-Check` reports drift and writes
nothing.
**Why**: the DETECTION half shipped first (`c36ffc60f`): `MUM-ExternalIdentifiers.Tests.ps1` failed when a
published studio mod had no registry row under its deploy folder. Generation removes the hand edit that
gate forced on every new publish.
**Status**: SHIPPED. Hand rows remain for third-party mods, the three studio mods with no
`nexus-mods.json` entry (Stop Auto Save 23, More Field Expansion 24, Modding Starter Kit 29) and seven
aliases the generated keys cannot reach (Nexus page names, `RemoveSkillStaleness`, `CommunityModChest`,
`ModUpdateManager`, the `crispywhips.mod_update_manager` GUID). Every old key resolves to the same id as
before.

### Suite compatibility matrix  [ruled out as a file, 2026-10-01]
**Status**: the round-2 plan's Considered table rules out the matrix file (it needs a release-time
producer outside MUM); its cheap cousin, "installed framework older than the one this bundle was packed
with", is round-2 feature 11.

---

## Phase 4: Polish

> No card art exists or is wanted here (0 CardData). Polish for this mod means UI affordances and precision.

| Item | What | Status / Complexity |
|------|------|---------------------|
| Persist the search filter | `_searchFilter` saved into `ModPreferences` so it survives an F3 close | SHIPPED 2026-09-08 (plan row N4, debt T2.222) |
| Drop the `//`-comment header from the mappings file | `CreateDefaultMappings` writes bare JSON (`ModMappingManager.cs:91-108`); `SimpleJson.cs:46-52` still strips `//` lines so old files parse | SHIPPED 2026-06-17 (`35642c7ca`, MUM 2.1.1). Listed OPEN here until 2026-09-25 |
| Framework-first install order | `ApplySuiteUpdates` puts `CSFF_Mod_Framework` first whenever it is selected (`UpdateManagerUI.cs:906-913`) | SHIPPED 2026-07-09 (`650625393`) as a silent reorder. Open only as an optional "framework installed first" note in the restart banner - Quick |
| MUM runtime-localization decision | No `LocalizationManager`/`GetText` read exists in any MUM `.cs`, so the 25 `SimpCn.csv` rows never render | DECIDED 2026-10-01; SHIPPED in 2.1.63 (round-2 feature 22, `MumText`, 247 rows, parity CLEAN), debt T2.1272 |
| Sortable mod list | Fixed sorts only (by name; favorites then name) | Shipped in 2.1.63 (round-2 feature 18): ModSorter, the Sort button, gated by `MUM-CompiledLogic`; debt T2.1268 |
| Conflict-detector precision | Pairs re-keyed by `BepInPlugin` GUID with name guesses labelled as a heuristic tier | SHIPPED in 2.1.62 (`PluginGuidCollisions.cs`, `ConflictDetector`), debt T2.373 |
| Window opens once; a check never moves you off your tab | `UpdateWindowPolicy` plus the `OpenWindowOnUpdates` option | SHIPPED in 2.1.63 (round-2 feature 1, `4eca74276`), debt T2.1250 |
| MUM's own line on the Install & Update tab | Its version and a Nexus page button above the suite rows | SHIPPED in 2.1.63 (round-2 feature 23, `e18399090`), debt T2.1256 |
| Playthrough-test coverage | 30 MUM rows, all `pending` in `items` (none confirmed yet): T2.222-T2.224, T2.227, T2.263, T2.370-T2.373, T2.1250-T2.1265, T2.1268-T2.1272. They are the only route from the MEDIUM feature groups to any VERIFIED grade | OPEN - human-gated |

---

## Long-term Vision

> Where this mod should be at v3.0.

MUM's endpoint is the suite's front door: the one thing a player installs first, which then installs
and keeps current everything else, and which can explain a broken load order without a log file. The
Nexus-tracking half is essentially finished, and the installer half now validates before it deletes,
never moves a mod backwards, swaps in a verified staged copy, lets a failed row be retried, and knows
about dependencies. What remains is the round-2 plan: an installer that keeps its working folders out
of every loader's view and decides dependencies for you (phase B), one request per check (phase D),
more depth in My Mods (phase E) and runtime localization (phase F). Self-update stays out: MUM can
update all nine suite mods but not itself, because it never downloads and its own DLL is locked while
running; the Install & Update tab's line for MUM itself (2.1.63) says so and links its Nexus page.

**Former major additions, each now decided in the round-2 plan:**
- **MUM self-update via `.pending` staging** - ruled out 2026-10-01
  (`Documentation/Design/Mod_Update_Manager_Round2_As_Built.md` Considered table); the spec resolves at
  `git show 09d54469c:Documentation/Ideas/Mod_Update_Manager/Embedded_Mod_Suite_Integration.md`.
- **Read-only mod profiles** - round-2 feature 20 (phase E); a profile that drives the installer stays
  deferred (Considered table).
- **Backup and rollback** - round-2 feature 12 (phase B), opt-in "keep the previous version" and Roll
  back.
- **In-game "What's New" popup on version bump** - replaced by NEW chips and a version history, round-2
  feature 19 (phase E).
- **Health chip per first-party row** - ruled out (Considered table: `.audit/` is gitignored in nine
  mods and goes stale, so the chip would show players a self-assessed score).

Full inventory: `Documentation/Design/Mod_Update_Manager_Round2_As_Built.md` (the round-2 plan, as built 2026-10-03;
its Considered table holds what was ruled out; the old Ideas file
resolves at `09d54469c`). Standing guardrails, carried into that plan's section 0, still apply: every
backup/rollback and file-deletion path is opt-in and audited before wiring, and MUM stays
zero-runtime-dependency. The framework-first install order shipped in `650625393` (see Phase 4);
IDEAS.md listed it under Shipped from the 2026-09-25 consolidation.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| Any suite mod changes | Repackage (`/package-mod-suite` / `Pack-Suite.ps1`) BEFORE deploying MUM, and deploy MUM LAST - framework, then content mods, then repackage, then MUM |
| A repack while MUM's own version is unpublished | Keep the version and fold into its CHANGELOG section, one dated section for every unpublished change (`/package-mod-suite` Step 2, since 2026-10-02) |
| A suite mod's source moves ahead of its embed with no MUM release | Pin it in `$knownStaleEmbeds` keyed on the EMBEDDED version; the next repackage expires it |
| Every repackage | State which suite-mod versions shipped in the commit message and CHANGELOG; the payload is gitignored and otherwise leaves no trace |
| Any new suite mod, or any new `JsonDataLoader.DirToTypeName` folder type | Update the content-folder lists in BOTH `Pack-Suite.ps1` AND `Deploy-Mods.ps1`, plus `SuiteModRegistry.cs`, then run `Deploy-Mods.Tests.ps1` |
| A studio mod gets a Nexus page | Add it to `Development_Tools/Nexus/nexus-mods.json`, then run `powershell -File Development_Tools/Update-KnownModRegistry.ps1` to regenerate the studio rows of `KnownModRegistry.cs` (never edit those rows by hand); `MUM-ExternalIdentifiers.Tests.ps1` runs its `-Check` and fails in both directions: a published mod the block lacks or maps to a different id, and a block key that is no published mod's folder, deploy folder or ModInfo Name |
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

### 2026-09-26 - Content_Update_Plan (MUM 2.1.62, plan closed)
- **Verdict:** all 5 features shipped in MUM 2.1.62, committed together (`git log -1 --
  Mod_Update_Manager/SuiteInstaller.cs` finds it). (1) Crash-safe suite install:
  `SuiteInstaller.cs` extracts into `<Folder>__mum_staging`, size-verifies there, renames the
  installed folder to `<Folder>__mum_old` and staging into place, undoes a failed second rename,
  and clears or restores leftovers of an interrupted run first. (2) The "Tested: EA x.y" line
  (`CompatibilityChip.cs`, drawn under the inline changelog). (3) One 429 retry after 5 s on
  `WaitForSecondsRealtime` (`NexusApiClient.GetModInfoCoroutine`). (4) A GUID-confirmed conflict
  tier (`PluginGuidCollisions.cs` over `PluginMetadataReader`) above the name heuristics, which
  are now labelled as such. (5) `ModMappingManager.RetireRegistryDuplicates` on load.
- **Departures from the plan text, each deliberate:** SpriteCache is MOVED across after the swap,
  not copied into staging, because it measured 1.43 GB on the dev install; mapping retirement drops
  a mod's rows only when EVERY row for that mod equals the registry, since dropping one could hand
  the lookup to a sibling row with a different ID; the CardSizeReduce/UIScale pair stays in the
  heuristic tier because UIScale is not installed, so its GUID could not be read;
  `[Install Incomplete]` now means only "the folder could not be put back" (a verify failure now
  leaves the install untouched, so the old meaning cannot occur); `ModInfoJson` moved from
  `ModScanner.cs` to `SimpleJson.cs` and a `MumLog` sink was added so the gate can compile the
  logic without BepInEx.
- **Evidence:** measured OS semantics before building (Windows 11, a view mapped the way Mono maps
  an assembly, handle closed afterwards): the mapped file cannot be overwritten but can be deleted
  and its folder renamed, while any open handle inside a folder blocks renaming it, which is why the
  swap is safe in-game and a held file fails cleanly. `Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1`
  (new) compiles the host-free files and drives them: 35/35 green, also under StrictMode 3 with
  Stop; every one of its 7 eggs was contained and their anchors match the live source once; a
  planted break went red on 6 checks. Release build 0 warnings 0 errors; parity CLEAN 15/15;
  Deploy-Mods + Stable-Layout + Csproj-ContentIncludes 73/73; Mod-Regression + Preflight-MechChecks
  + Localization-EnglishColumnCjk + NestedRepo-Boundaries 116/116. MUM-ExternalIdentifiers +
  EmbeddedZipCurrency: 40 passed, 5 pinned skips, 2 failed, both embed staleness from suite mods
  that moved after the 2.1.61 pack (Advanced Copper Tools 1.16.11, committed `b41706855` at 18:08;
  Quick Transfer 1.9.0, uncommitted in the worktree), red at HEAD before this work and cleared only
  by a repack. The live `LogOutput.log` confirms the GUID tier's advice: `Skipping [ModCore 0.0.0.0]
  because a newer version exists (ModCore 3.4.1)`.
- **Not in this release on purpose:** no suite repack; the embed is 2.1.61's. A repack is a
  release step that follows the suite mods (framework, content, repack, MUM last), and Herbs and
  Fungi's `d5eb56a22` is an unversioned 1.14.0 checkpoint still labelled 1.13.7, so a repack before
  that release must pack H&F from its last released commit (csff-mods-82, 2026-09-26).
- **The two stale findings the plan asked to report were already reconciled:** `ROADMAP.md`
  Phase 4 marks the mappings `//` header (`35642c7ca`) and framework-first install order
  (`650625393`) SHIPPED, and `IDEAS.md` lists the install order under Shipped. The one sentence
  still saying IDEAS.md listed it as unbuilt was corrected in this pass.
- **Residual, documented in code:** with opt-in Nexus discovery enabled, a discovered ID outranks
  the registry, so a dropped duplicate row can in principle hand a mod to a discovered ID.
- **Disposition:** moved to `Documentation/Design/Mod_Update_Manager_Content_Update_As_Built.md`; `Documentation/Plans/Mod_Update_Manager/` is empty.
- **Verification debt now carried by the playthrough tracker:** T2.370 (staged swap in the running
  game, and a held file refused cleanly), T2.371 (the Tested line), T2.372 (the 429 retry, needs a
  real 429), T2.373 (the `[Same plugin GUID]` row, positive case already on the dev install). T2.223,
  T2.227 and T2.263 were annotated where 2.1.62 changed what they describe. The plan asked for ids
  after T2.287; concurrent sessions had claimed blocks up to T2.365, so these start at T2.370.

### 2026-10-02 - MASTER_PLAN round 2, release of phases A and G (2.1.63 ready; repack blocked)
- **Verdict:** phases A (features 1-6) and G (feature 23) are built and committed: feature 1
  `4eca74276`, 2 `359e8f87f`, 3 `af488f19e` (README `a16356410`), 4 `455d97ed5`, 5 `437741fd8`,
  6 `eadd7cecc` (README `d15f48b5c`), 23 `e18399090`. Phase C is not releasable (feature 13 landed at
  `86a9e546f`, feature 14 has not); phases B, D, E and F are unbuilt.
- **Disposition:** the plan STAYS in `Documentation/Plans/Mod_Update_Manager/`, since phases B to F are
  its remaining code; nothing was pruned, archived or deleted. Its Release tail carries a dated line
  with this release's version decision and the blocked repack.
- **Evidence:** the MUM gates were run on HEAD `c7fb3550e` at 08:32 through a StrictMode-3 wrapper
  (`MUM-CompiledLogic` 262/262, `MUM-ExternalIdentifiers` 27/27, `MUM-EmbedContents` 43 passed with
  9 non-strict skips, each an embed older than its source build), then with the plan's other gates
  on the edited tree at 08:49 (`EmbeddedZipCurrency` 18 passed and 17 non-strict skips, `Deploy-Mods`
  39, `Csproj-ContentIncludes` 11, `Version-Synchronization` 22, `Localization-EnglishColumnCjk` 24,
  `PlaythroughRow-Citations` 13, `Plans-Policy` 9, `PackSuite-UnreleasedGuard` 6, all green).
  `Stable-Layout` was red on one check from `5d32f670b` (a framework-lane test joining
  `TestHarness\...` onto the repo root), fixed in `e1693d4c9` and then 23/23. README re-read against
  source in both directions: the Network section's three request shapes, their headers and
  timeouts, the key check reading only the HTTP status, the button-only cache bypass, the
  once-per-session window, Retry failed and MUM's own line all match the code; the ModInfo.json
  Description gained the retry.
- **Version:** kept at 2.1.63. Nexus has 2.1.61 (`nexus_publish.py status`, run that morning), so neither 2.1.62
  nor 2.1.63 ever shipped; `[2.1.62]` is folded into one `[2.1.63]` section dated 2026-10-02, and
  `/package-mod-suite` Step 2 now keeps an unpublished version instead of auto-bumping it (the
  2026-09-27 pack had bumped an unpublished 2.1.62). ModInfo.json, Plugin.cs and README read 2.1.63.
- **Release: not repacked, not deployed.** The same status run read every suite mod's source ahead
  of its Nexus release: framework 2.28.0 (Nexus 2.26.12), Advanced Copper Tools 1.18.0 (1.16.10),
  Herbs and Fungi 1.14.1 (1.13.7), Water Driven Infrastructure 1.12.0 (1.11.3), Community Mod Chest
  1.69.3 (1.68.45), Homestead Perks 1.3.1 (1.2.4), Repeat Action 2.2.0 (2.1.6), Quick Transfer 1.9.0
  (1.8.1), Skill Speed Boost 1.11.0 (1.10.6), and Advanced Copper Tools, Repeat Action and Quick
  Transfer held uncommitted work when read. The
  Pack-Suite unreleased guard would refuse none (no suite CHANGELOG heads Unreleased), but packing
  now would embed suite work ahead of those mods' own releases. The repack and the MUM deploy follow
  those releases in the `/deploy-mods MUM` order (framework, the other suite mods, repack, MUM last);
  that session rewrites `[2.1.63]`'s "Refreshed the embedded mod suite" entry to what it packs and
  dates the section to the release day. The built `bin/Release` DLL still embeds the 2026-09-27 pack.
- **Verification debt now carried by the playthrough tracker** (filed in `1f781eb67`, before this
  entry): T2.1250 (feature 1), T2.1251 (feature 2; re-checks T2.371 on the new reader), T2.1252
  (feature 3, Retry failed), T2.1253 (feature 3; re-checks T2.263 on the moved Apply rules), T2.1254
  (feature 4), T2.1255 (feature 5), T2.1256 (feature 23). Feature 6 is docs and a repo gate, with no
  in-game step. T2.263 and T2.371 are left as recorded.
- **This roadmap's rows reconciled in the same pass:** the Phase 2-4 rows for 2.1.62's items (the
  crash-safe install, mapping-row retirement, the 429 retry, the Tested chip, GUID-confirmed
  conflicts) now read shipped, and the rows the round-2 plan absorbed point at their features.

### 2026-10-02 - MASTER_PLAN round 2, release of phase B (2.1.63 ready; repack blocked)
- **Verdict:** phase B (features 7-12) is built and committed: feature 7 `3cb61382e`, 8 `b3db80c73`,
  9 `dc1eacb8a`, 10 `870166a98`, 11 `ff8c7f791`, 12 `4200f0782` (docs `72ddc1f0d`). Phase C is
  still not releasable (feature 13 landed at `86a9e546f`, feature 14 has not); D, E and F are
  unbuilt. Phases A and G were released in the entry above.
- **Disposition:** the plan STAYS in `Documentation/Plans/Mod_Update_Manager/`, since phases C to F
  are its remaining code; nothing was pruned, archived or deleted. Its Release tail carries a
  dated line for this release.
- **Evidence:** the MUM gates were run on 2026-10-02 on this tree through a StrictMode-3 wrapper
  (`MUM-CompiledLogic` 747/747, `MUM-ExternalIdentifiers` 27/27, `MUM-EmbedContents` 43 passed with
  9 non-strict skips, each an embed older than its source build), then `Deploy-Mods` 39,
  `Stable-Layout` 23, `Csproj-ContentIncludes` 11, `Version-Synchronization` 22,
  `Localization-EnglishColumnCjk` 24 and `PackSuite-UnreleasedGuard` 6, all green, and
  `Check-LocalizationParity` for MUM CLEAN 54/54. `EmbeddedZipCurrency` in strict mode read 18
  passed and 17 failed, every failure an embed older than its source (framework 2.27.1 against
  2.28.0, and so on for all nine; Quick Transfer's embed is the same version as its source but
  holds older bytes), which only a repack clears.
- **Docs, both directions:** README, CHANGELOG `[2.1.63]` and the ModInfo.json Description were
  re-read against `SuiteInstaller`, `SuiteInstaller.Kept`, `SuiteBatch`, `SuiteDependencies`,
  `SuiteInstalledElsewhere`, `ModSuiteExtractor`, `UpdateManagerUI` and `ModUpdateConfig`. Four
  wording fixes: the keep option's prune ("oldest first" named an order the code does not use; the
  oldest copies past the count are the ones deleted), "Nothing is kept unless you turn the option
  on" (a rollback keeps the copy it replaces whether the option is on or off, gated as RollbackOff),
  the empty `__mum_probe` folder that the first Apply of a session makes in `plugins/` for a moment
  (the README and CHANGELOG now say so; "no `__mum_` folder under `plugins/`" holds for staging and
  backup folders, not for the probe), and Retry failed (it lives in the restart banner, which shows
  only once a mod in the batch installed, so after a failed framework the README says to press
  Apply Updates again). Feature 8's figure: the plan's Done line asks for the freeze "measured
  alone"; the W0.3 record (section 2) says the machine was never idle, so the CHANGELOG states 1.7
  to 4.8 s over all nine mods (median 3.3, six runs) and 0.9 to 2.6 s for Community Mod Chest as
  measured with the game running and other programs busy. A quiet-machine re-measure is closed as
  impractical on a checkout where other sessions run continuously.
- **Version:** kept at 2.1.63. Nexus has 2.1.61 (`nexus_publish.py status`, run for this release), so
  nothing between 2.1.61 and this build was published; phase B's lines sit in the one `[2.1.63]`
  section, each written by its feature commit. ModInfo.json, Plugin.cs and README read 2.1.63
  (`Version-Synchronization` green); `Update-ModVersion.ps1` had nothing to do.
- **Release: not repacked, not deployed.** `.claude/mod-deploy-status.json` reads all nine suite mods,
  the framework among them, PENDING (each folder's newest commit differs from its last deployed one), and
  the status run reads every source ahead of Nexus: framework 2.28.0 (Nexus 2.26.12), Advanced
  Copper Tools 1.18.0 (1.16.10), Herbs and Fungi 1.14.1 (1.13.7; its release zip is 1.14.0), Water
  Driven Infrastructure 1.12.0 (1.11.3), Community Mod Chest 1.69.3 (1.68.45), Homestead Perks 1.3.1
  (1.2.4; its release zip is 1.2.4), Repeat Action 2.2.0 (2.1.6), Quick Transfer 1.9.0 (1.8.1), Skill
  Speed Boost 1.11.0 (1.10.6). Quick Transfer and Repeat Action held uncommitted work when read,
  which Pack-Suite would bake into the embed, and the live game was held by a game lock
  (`.claude/game-lock.txt`). The Pack-Suite unreleased guard would refuse none (no suite CHANGELOG
  heads Unreleased). The repack and the MUM deploy follow in the `/deploy-mods MUM` order
  (framework, the other suite mods, repack, MUM last); that session rewrites `[2.1.63]`'s
  "Refreshed the embedded mod suite" entry to what it packs and dates the section to the release day.
  The built `bin/Release` DLL still embeds the 2026-09-27 pack.
- **Verification debt now carried by the playthrough tracker** (filed in `bd154da79`, before this
  entry): T2.1257 (feature 7; re-checks T2.370 on the new layout), T2.1258 (feature 8; re-runs T2.370
  on the coroutine build), T2.1259 (feature 9), T2.1260 (feature 10), T2.1261 (feature 11) and T2.1262
  (feature 12). Features 9 and 11 name no in-game check in the plan; they are filed because
  feature 11's CHANGELOG entry says its tab has not been seen in a running game and feature 9's
  2500-character cap is a placeholder nobody has seen draw. T2.370 is left as recorded.

### 2026-10-02 - MASTER_PLAN round 2, release of phase C (repo tooling; nothing to repack)
- **Verdict:** phase C (features 13 and 14) is built and committed: feature 13 `86a9e546f` (the DLLs
  inside each embed and the built DLL's resources gated, `Pack-Suite.ps1` throws on a missing plugin
  DLL), feature 14 `fd222e4d8` (the studio rows of `KnownModRegistry.cs` generated by
  `Development_Tools/Update-KnownModRegistry.ps1`), plus `a6f51134e` (`/publish-mod` step 8 now says to
  add a new Nexus id to `nexus-mods.json` and run the generator instead of editing the registry by
  hand). Phases A, G and B were released in the entries above; D, E and F (features 15-22) are unbuilt.
- **Disposition:** the plan STAYS in `Documentation/Plans/Mod_Update_Manager/`, since phases D to F are
  its remaining code; nothing was pruned, archived or deleted. Its Release tail carries a dated line for
  this release.
- **Evidence:** on `fd222e4d8` the dispatcher rebuilt MUM with `dotnet build -c Release --no-incremental`
  (0 warnings, 0 errors) and ran through a StrictMode-3 wrapper, in the foreground: `MUM-CompiledLogic`
  747/747, `MUM-ExternalIdentifiers` 50/50 (23 of them the new feature-14 Describe), `MUM-EmbedContents`
  43 passed with 9 non-strict skips (each an embed older than its source build, unchanged since phase
  B), `Version-Synchronization` 22/22, `Stable-Layout` 23/23, `Localization-EnglishColumnCjk` 24/24.
  `powershell -File Development_Tools/Update-KnownModRegistry.ps1 -Check` printed OK and exited 0 on the
  real tree. The diff of `KnownModRegistry.cs` was read row by row against `GetNexusModId` and
  `IsSelfMod`: every removed spelling (for example `Advanced Copper Tools`, `Mod Update Manager`) is
  still reached through their underscore and space transforms, and the seven kept aliases are the keys
  no transform reaches. The release-path gates that read no MUM source were run after this entry's
  edits (the counts are in the commit message that carries this entry).
- **Docs, both directions:** no player-facing change, so README, CHANGELOG `[2.1.63]` and the ModInfo.json
  Description are untouched by this phase (neither feature changes what MUM does in the game). This
  file's Current State still described the tree before phase B (features 7-12 listed as unbuilt, 33
  `.cs` files and 25 localization keys, verification debt ending at T2.1256); it now reads phases A, G,
  B and C built, 37 tracked `.cs` files and 54 keys at `a6f51134e`, and debt through T2.1262. The
  registry generation row above and the Maintenance Calendar's "A studio mod gets a Nexus page" row now
  describe the generator and the two-way check.
- **Version:** kept at 2.1.63. Nexus has 2.1.61 (`nexus_publish.py status`, run for this release), and
  phase C adds no CHANGELOG line, so nothing changes in any version file.
- **Release: not repacked, not deployed.** Phase C changes nothing a player sees (feature 14 recompiles
  the registry rows, and every key resolves to the id it did before), and the suite repack is still blocked as in the phase B entry: the status run for this release read every
  suite source ahead of Nexus (framework 2.28.0 against 2.26.12, Advanced Copper Tools 1.18.0 against
  1.16.10, Herbs and Fungi 1.14.1 against 1.13.7, Water Driven Infrastructure 1.12.0 against 1.11.3,
  Community Mod Chest 1.69.3 against 1.68.45, Homestead Perks 1.3.1 against 1.2.4, Repeat Action 2.2.0
  against 2.1.6, Quick Transfer 1.9.0 against 1.8.1, Skill Speed Boost 1.11.0 against 1.10.6), and Quick
  Transfer and Repeat Action held uncommitted work in the tree. The built `bin/Release` DLL still embeds
  the 2026-09-27 pack and stays uncommitted for the repack commit.
- **Verification debt:** none new. Neither feature names an in-game check (plan Release tail, Tracker
  rows), so no tracker row was filed; the next free id in the MUM block stays T2.1263.

### 2026-10-03 - MASTER_PLAN round 2, release of phase D (2.1.63 ready; repack blocked)
- **Verdict:** phase D (features 15-17) is built and committed: feature 15 `96045dee7` plus `9c1305d15`
  (one key-less public GraphQL request per check, per-mod v1 only for what it leaves and only with a
  key, `UsePublicEndpointWithoutKey` off by default, the Application-Name and Application-Version
  headers on every request), feature 16 `6a37d3695` (the opt-in "New and updated" filter in My Mods,
  `DiscoveryFeed.cs`), feature 17 `d122ac995` (the opt-in Endorse button, `NexusEndorse.cs`). Phases A,
  G, B and C were released in the entries above; E and F (features 18-22) are unbuilt.
- **Disposition:** the plan STAYS in `Documentation/Plans/Mod_Update_Manager/`, since phases E and F are
  its remaining code; nothing was pruned, archived or deleted. Its Release tail carries a dated line for
  this release.
- **Departures from the plan text, each deliberate:** feature 16 has no `DiscoveryFeedMaxLookups` and
  makes no per-id lookups. W0-1 section 7 found a root `mods` query whose one answer carries every field
  a row shows, so the cap is `DiscoveryFeedSize` (1 to 50, default 10, a placeholder): the size of that
  one request and the most rows drawn. The list also leaves out any mod whose `adultContent` is not
  false, which W0-1 section 6 item 3 asked for and the plan text predates. Feature 17 reads no part of
  Nexus's reply even for display text (the prompt forbids it, stricter than W0-1 section 7 allows), so a
  refusal names all the reasons Nexus refuses for instead of the one it gave.
- **How it was built:** feature 16 by a subagent of the previous dispatching session (92c02667), cut off
  with its last gate run unanswered and adopted by csff-mods-6d after all seven live sessions disclaimed
  it; feature 17 by a subagent cut off by an API limit before any build. Both were finished and
  verified here: one em-dash removed from README, feature 17's egg-coverage check rewritten to take its
  tables as test data (it read discovery-time variables at run time and so reported 31 driven eggs as
  undriven), and one README sentence about MUM's own row corrected (a manual mapping would give it a
  button). Commit messages carry the detail.
- **Evidence:** on each feature commit MUM was rebuilt `-c Release --no-incremental` (0 warnings, 0
  errors) and run through a StrictMode-3 wrapper in the foreground: after feature 16, `MUM-CompiledLogic`
  999, `MUM-ExternalIdentifiers` 56, `Version-Synchronization` 22, `Localization-EnglishColumnCjk` 24
  (1101/0); after feature 17, 1145, 56, 22, 24 (1247/0). Parity CLEAN 66/66 then 79/79. The endorse
  request was read field by field against W0-1 section 4.1 and section 7 (lines 381-393): method,
  path with `.json`, `apikey` and `Content-Type: application/json`, body `{"Version": ...}` always
  sent, 200/201 endorsed, 202 unknown, other 4xx refused, 401 and 429 named. Nothing was sent to Nexus
  by any build, test or script.
- **Docs, both directions:** README (Shipped Features, the tab and Configuration tables, the Network
  table and its no-key paragraph, Data Locations, five Troubleshooting entries), CHANGELOG `[2.1.63]`
  Added and the ModInfo.json Description were written with each feature and re-read against the code
  for this release; README's Network section names all four kinds of request MUM can now send.
- **Version:** kept at 2.1.63. Nexus has 2.1.61 (`nexus_publish.py status`, run for this release), so
  phase D's lines sit in the same `[2.1.63]` section.
- **Release: not repacked, not deployed.** The status run for this release read every suite source
  still ahead of Nexus (framework 2.28.0 against 2.26.12, Advanced Copper Tools 1.18.0 against 1.16.10,
  Herbs and Fungi 1.14.1 against 1.13.7, Quick Transfer 1.9.0 against 1.8.1, Repeat Action 2.2.0 against
  2.1.6, Skill Speed Boost 1.11.0 against 1.10.6, Water Driven Infrastructure 1.12.0 against 1.11.3,
  Community Mod Chest 1.69.3 against 1.68.45, Homestead Perks 1.3.1 against 1.2.4), and the framework
  plan's session asked that its mid-plan 2.28.0 build not ride a repack before that plan's release tail.
  The built `bin/Release` DLL still embeds the 2026-09-27 pack and stays uncommitted for the repack
  commit.
- **Verification debt:** T2.1263 (feature 15: a check with and without a key against the live public
  answer), T2.1264 (feature 16: the live list, its header counts, Refresh, and that reopening sends
  nothing), T2.1265 (feature 17: the question, Yes and No, and the three HTTP statuses W0-1 could not
  settle, recorded on a real account), filed in `cef1d79f1`. Next free id in the MUM block: T2.1266.

### 2026-10-03 - MASTER_PLAN round 2, `/audit-plan` ground-truth pass (plan pruned to phases E and F)
- **Verdict (each claim re-read against current source, not the plan's status text):** 18 features
  and Wave 0 are built or answered; 4 are open. Built and in source with a pending tracker row each: features
  1-5 and 23 (T2.1250 to T2.1256), 7-12 (T2.1257 to T2.1262), 15-17 (T2.1263 to T2.1265); features 6, 13 and 14
  need no in-game row (README sentence, `MUM-EmbedContents.Tests.ps1` plus the `Pack-Suite.ps1` throw, and
  `Update-KnownModRegistry.ps1` with the `// <studio-mods` markers in `KnownModRegistry.cs`). Wave 0: records
  W0-1, W0-2 and W0-3 exist under `Documentation/Research/Mod_Update_Manager/`. Open: features 19, 20, 21, 22 (no
  `VersionLedger`, `ModProfile`, `LoadedStatus` or `MumText` file; `Grep` for `LocalizationManager`/`GetText` in
  `Mod_Update_Manager/*.cs` returns nothing). Feature 18 is a fifth state: built, ungated.
- **Feature 18 finding:** `ModSorter.cs`, the Sort button, `ModPreferences` `_sortMode` and five CSV rows landed in
  the bulk commit `50d3fdb9` with no phase record. `grep -rl ModSorter Development_Tools/` returns nothing, so no
  `MUM-CompiledLogic` case, egg or `$hostFree` entry exists, while `ModSorter.cs`'s header and the `[2.1.63]`
  CHANGELOG entry claim a test suite and mutation testing. The claim is false until Prompt 19 builds the gate; no
  tracker row exists for the button either. Flagged, not edited (this pass edits the plan, pack and this log only).
- **Disposition:** the plan STAYS in `Documentation/Plans/Mod_Update_Manager/` (phases E and F are code still to
  write); sections 1-17 and 23, Wave 0 and the phase G section were pruned, nothing renumbered. The pack lost
  Prompts 1-18 and keeps 19-24; Prompt 19 now finishes feature 18 and is the next action.
- **Stale claims corrected in what remains:** `UpdateManagerUI.cs` is 1,923 lines (plan said 1,260); 66
  string-literal-first `GUILayout` calls (plan said 94); both CSVs hold 84 rows (plan said 15); the
  "Pack-Suite guard in flight" note (committed `3abc9660e`); `SuiteBatch.Record` carries no version, so
  feature 19's Apply event needs it passed in; W0.2's branch is decided (the load-order list ships).
- **Docs/version drift (not edited):** besides the feature 18 claim, `Mod_Update_Manager/ROADMAP.md` Phase 4 still
  lists the sortable list as OPEN, and its "Playthrough-test coverage" row says 16 MUM rows where 25 exist
  (T2.1257 to T2.1265 and T2.1250 to T2.1256 plus the four older). Version strings agree at 2.1.63 across
  `ModInfo.json`, `Plugin.cs` and the README header; `ModInfo.json` carries no `ModLoaderVerison` or
  `ModEditorVersion`.
- **Evidence (limits):** the checkout is shallow (53 commits); history older than `50d3fdb9`'s neighbours was not
  readable, so the earlier phase commits were confirmed by the code they name and this log, not by `git show`.
  `.decomp/` and the embedded ZIPs are absent here, so W0.4 and the ZIP-dependent gates were not re-run. No gate was
  executed (no PowerShell); gate results above are the 2026-10-03 phase D record, not this pass.
- **Verification debt:** unchanged, T2.1250 to T2.1265 pending. Next free id in the MUM block: T2.1266.
- **Feature 18 resolved 2026-10-03:** `ModSorter.cs`, `InstalledModInfo.cs` and `ModPreferences.cs` joined `$hostFree` in
  `MUM-CompiledLogic.Tests.ps1` with a ModSorter Describe (every order, ties, favorites first, Next wrapping, the saved
  word and its fallbacks, the saved file staying valid JSON) and eleven mutation checks, each watched failing on its own
  broken copy; the `[2.1.63]` CHANGELOG sentence and `ModSorter.cs`'s header are now true. Still owed: the in-game
  tracker row for the Sort button (filed after the peer-held tracker edits are committed).
- **Feature 19 built 2026-10-03:** `VersionLedger.cs`, `ModUpdateConfig.VersionLogPath`, the observe-once call in
  `Plugin.OnGameDataLoaded`, the NEW chip, History button and Mark all as seen on My Mods rows, and the Apply event
  recorded in `ApplySuiteCoroutine`; ten `Mod_Update_Manager_Version*` rows in both CSVs (parity CLEAN, 94 keys each);
  gated by a `MUM-CompiledLogic` Describe with seventeen mutation checks and a source-shape Describe. Still owed: its
  in-game tracker row (with feature 18's). Unreleased: `bin/Release` was rebuilt locally and not committed (the repack
  in the release tail rebuilds it).
- **Feature 20 built 2026-10-03:** `ModProfile.cs` (snapshot, text format, diff, `ProfileStore`), `ModUpdateConfig.ProfilesDir`,
  the Profiles section in the Settings tab, sixteen `Mod_Update_Manager_Profile*` rows in both CSVs (parity CLEAN, 110 keys
  each), gated by a `MUM-CompiledLogic` Describe with eight mutation checks. Still owed: its in-game tracker row.
- **Feature 21 built 2026-10-03:** `LoadedStatus.cs`, `LoadedProbe.cs`, the status line on My Mods rows and the Show load
  order button on the Conflicts tab; eleven `Mod_Update_Manager_Loaded*`/`_LoadOrder*` rows in both CSVs (parity CLEAN,
  121 keys each); nine mutation checks and a source-shape Describe in `MUM-CompiledLogic`. Still owed: its in-game
  tracker row.
- **Feature 22 built 2026-10-03:** `MumText.cs` plus the generated `MumText.Table.cs` (`Development_Tools/MumText/generate_table.py`,
  `--check` mode used by the gate), every window string in `UpdateManagerUI.cs`, the status and error lines in `UpdateChecker.cs`
  and the texts of `VersionLedger`, `ModProfile` and `LoadedStatus` now read through `MumText.Get`. The lookup is bound by
  reflection to `LocalizationManager.GetText` in `UpdateManagerUI.BindText`, with a `Font.HasCharacter` glyph check and a
  cache reset on every open. Both CSVs hold 247 rows (parity CLEAN); `SimpEn.csv`'s dead third column was removed. Gated by the
  new `MUM-Localization.Tests.ps1` (CSV, table and sources agree; five mutation checks) and a `MUM-CompiledLogic` Describe on
  `MumText` (five more); the older source-shape checks read the window source back in its pre-conversion shape through
  `Read-MumUiSource`. Left in English on purpose: log lines, and error text that arrives from Nexus's replies in
  `NexusApiClient`. Still owed: its in-game tracker row (switch the game to Chinese and read the window).

### 2026-10-03 - MASTER_PLAN round 2, closeout (plan drained and archived; release still blocked)
- **Verdict:** all 23 features are built, gated and committed; no code work remains. Features 1-17 and 23 were
  recorded in the entries above; 18-22 landed 2026-10-03 (`207244b76`, `5a83a8829`, `a3d99ac4c`, `7238dea7b`,
  `8ce985c21`, `ddeed031f`).
- **Disposition:** ARCHIVE. `Documentation/Plans/Mod_Update_Manager/MASTER_PLAN.md` moved to
  `Documentation/Design/Mod_Update_Manager_Round2_As_Built.md` and its prompt pack was deleted
  (the pack's 24 prompts are all built or retired; it resolves at `a3402bca4`); `Documentation/Plans/Mod_Update_Manager/`
  is gone. The Considered table travelled with the plan. Inbound live pointers (tracker rows, the HTML checklist,
  the Wave 0 records, `package-mod-suite.md`, this file, the mod's docs) were repointed in the same pass; dated records
  (the dispositions above, CHANGELOG, older As_Built files) were left alone.
- **Evidence (re-run 2026-10-03, not carried from the plan's own status text):** MUM-CompiledLogic,
  MUM-ExternalIdentifiers, MUM-Localization, Version-Synchronization, Localization-EnglishColumnCjk,
  Csproj-ContentIncludes, PlaythroughRow-Citations and Plans-Policy together 1412 passed, 0 failed; Chinese
  parity CLEAN at 247 keys; `/audit-mod` 0 CRITICAL, Section H clean (`c8f4915ca`).
- **Verification debt:** the five rows T2.1268 to T2.1272 (filed `cbe08a494`) plus T2.1250 to T2.1265, all pending in
  `.claude/playthrough-test-status.json`; they need a person at the OnGUI window.
- **Release debt (not code):** repack and deploy in the documented order (framework, the other suite mods, the
  suite repack, MUM last), blocked on the nine suite mods' own releases. The mod is feature complete and
  moves to `Stable/` with `Move-ModFolder.ps1`, but not in this pass: the script refuses a folder with uncommitted
  changes, and `bin/Release/mod_update_manager.dll` stays uncommitted until the repack commit.
